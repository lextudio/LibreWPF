using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Media.ProGPU.Composition;
using MediaBrush = System.Windows.Media.Brush;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaBitmapSource = System.Windows.Media.Imaging.BitmapSource;
using MediaMatrix = System.Windows.Media.Matrix;
using MediaMatrixTransform = System.Windows.Media.MatrixTransform;
using MediaTransform = System.Windows.Media.Transform;
using PortableAlignmentX = ProGPU.Wpf.Interop.PortableAlignmentX;
using PortableAlignmentY = ProGPU.Wpf.Interop.PortableAlignmentY;
using PortableBrushMappingMode = ProGPU.Wpf.Interop.PortableBrushMappingMode;
using PortableMatrix3x2 = ProGPU.Wpf.Interop.PortableMatrix3x2;
using PortableRect = ProGPU.Wpf.Interop.PortableRect;
using PortableStretch = ProGPU.Wpf.Interop.PortableStretch;
using PortableTileBrush = ProGPU.Wpf.Interop.PortableTileBrush;
using PortableTileBrushKind = ProGPU.Wpf.Interop.PortableTileBrushKind;
using PortableTileBrushSource = ProGPU.Wpf.Interop.IPortableTileBrushSource;
using PortableTileMode = ProGPU.Wpf.Interop.PortableTileMode;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal static class WpfReflectionDrawingReplay
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MaxTileBrushReplayTiles = 1024;

    private enum SupportedTileMode
    {
        None,
        Tile,
        FlipX,
        FlipY,
        FlipXY
    }

    private enum SupportedStretch
    {
        None,
        Fill,
        Uniform,
        UniformToFill
    }

    private enum SupportedAlignmentX
    {
        Left,
        Center,
        Right
    }

    private enum SupportedAlignmentY
    {
        Top,
        Center,
        Bottom
    }

    private readonly record struct TileBrushReplayTile(Rect Bounds, int Column, int Row)
    {
        public bool FlipX(SupportedTileMode tileMode)
        {
            return (tileMode == SupportedTileMode.FlipX || tileMode == SupportedTileMode.FlipXY)
                && (Column & 1) != 0;
        }

        public bool FlipY(SupportedTileMode tileMode)
        {
            return (tileMode == SupportedTileMode.FlipY || tileMode == SupportedTileMode.FlipXY)
                && (Row & 1) != 0;
        }
    }

    public static bool TryReplay(
        object? drawing,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter = null)
    {
        var status = Replay(drawing, sink, imageSourceAdapter);
        return status == WpfDrawingReplayStatus.Applied
            || status == WpfDrawingReplayStatus.PartiallyApplied;
    }

    public static WpfDrawingReplayStatus Replay(
        object? drawing,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (drawing == null)
        {
            return WpfDrawingReplayStatus.Skipped;
        }

        if (TypeNameEndsWith(drawing, "GeometryDrawing"))
        {
            return TryReplayGeometryDrawing(drawing, sink, imageSourceAdapter);
        }

        if (TypeNameEndsWith(drawing, "DrawingGroup"))
        {
            return TryReplayDrawingGroup(drawing, sink, imageSourceAdapter);
        }

        if (TypeNameEndsWith(drawing, "ImageDrawing"))
        {
            return TryReplayImageDrawing(drawing, sink, imageSourceAdapter)
                ? WpfDrawingReplayStatus.Applied
                : WpfDrawingReplayStatus.Unsupported;
        }

        if (TypeNameEndsWith(drawing, "GlyphRunDrawing"))
        {
            return TryReplayGlyphRunDrawing(drawing, sink)
                ? WpfDrawingReplayStatus.Applied
                : WpfDrawingReplayStatus.Unsupported;
        }

        return WpfDrawingReplayStatus.Unsupported;
    }

    private static WpfDrawingReplayStatus TryReplayGeometryDrawing(
        object drawing,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        if (!TryGetPropertyValue(drawing, "Geometry", out var geometryValue)
            || WpfReflectionResourceResolver.AdaptGeometry(geometryValue) is not { } geometry)
        {
            return WpfDrawingReplayStatus.Unsupported;
        }

        TryGetPropertyValue(drawing, "Brush", out var brushValue);
        TryGetPropertyValue(drawing, "Pen", out var penValue);

        var brush = WpfReflectionResourceResolver.AdaptBrush(brushValue);
        var pen = WpfReflectionResourceResolver.AdaptPen(penValue);
        var hasBrush = brushValue != null;
        var hasPen = penValue != null;
        var appliedAny = false;
        var unsupportedAny = hasPen && pen == null;

        if (!hasBrush)
        {
            if (pen != null)
            {
                sink.DrawGeometry(null, pen, geometry);
                appliedAny = true;
            }
        }
        else if (IsTileBrush(brushValue)
            && TryReplayTileBrushFill(brushValue!, geometry, sink, imageSourceAdapter, out var tileBrushStatus))
        {
            appliedAny = true;
            unsupportedAny |= tileBrushStatus == WpfDrawingReplayStatus.PartiallyApplied;
            if (pen != null)
            {
                sink.DrawGeometry(null, pen, geometry);
            }
        }
        else if (brush != null)
        {
            sink.DrawGeometry(brush, pen, geometry);
            appliedAny = true;
        }
        else
        {
            unsupportedAny = true;
            if (pen != null)
            {
                sink.DrawGeometry(null, pen, geometry);
                appliedAny = true;
            }
        }

        return appliedAny
            ? unsupportedAny ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied
            : unsupportedAny ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;
    }

    internal static bool TryReplayTileBrushFill(
        object brush,
        MediaGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        if (TryReplayPortableTileBrushFill(brush, geometry, sink, imageSourceAdapter, out status))
        {
            return true;
        }

        if (TryReplayImageBrushFill(brush, geometry, sink, imageSourceAdapter))
        {
            status = WpfDrawingReplayStatus.Applied;
            return true;
        }

        if (TryReplayDrawingBrushFill(brush, geometry, sink, imageSourceAdapter, out status))
        {
            return true;
        }

        if (TryReplayVisualBrushFill(brush, geometry, sink, imageSourceAdapter, out status))
        {
            return true;
        }

        status = WpfDrawingReplayStatus.Skipped;
        return false;
    }

    private static bool TryReplayPortableTileBrushFill(
        object brush,
        MediaGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        if (brush is not PortableTileBrushSource portableSource
            || !portableSource.TryGetPortableTileBrush(out var portableBrush))
        {
            return false;
        }

        switch (portableBrush.Kind)
        {
            case PortableTileBrushKind.Image:
                if (TryReplayPortableImageBrushFill(portableBrush, geometry, sink, imageSourceAdapter))
                {
                    status = WpfDrawingReplayStatus.Applied;
                    return true;
                }

                return false;

            case PortableTileBrushKind.Drawing:
                return TryReplayPortableDrawingBrushFill(portableBrush, geometry, sink, imageSourceAdapter, out status);

            case PortableTileBrushKind.Visual:
                return TryReplayPortableVisualBrushFill(portableBrush, geometry, sink, imageSourceAdapter, out status);

            default:
                return false;
        }
    }

    private static bool TryReplayPortableImageBrushFill(
        PortableTileBrush brush,
        MediaGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        if (!TryGetOptionalBrushTransform(brush, out var brushTransform)
            || !TryGetSupportedTileMode(brush, out var tileMode)
            || !TryGetSupportedStretch(brush, out var stretch)
            || !TryGetTileBrushAlignment(brush, out var alignmentX, out var alignmentY)
            || !IsUsableRect(geometry.Bounds, out var geometryBounds)
            || !TryGetOptionalRelativeBrushTransform(brush, geometryBounds, out var relativeTransform)
            || ResolveImageSource(brush.Content, imageSourceAdapter) is not { } imageSource
            || !TryGetTileBrushDestinationBounds(brush, geometryBounds, out var imageBounds)
            || !TryGetImageBrushSourceRect(brush, imageSource, out var sourceRect)
            || !TryGetImageStretchSourceBounds(stretch, sourceRect, imageSource, out var imageStretchSourceBounds)
            || !TryGetTileBounds(imageBounds, geometryBounds, tileMode, out var tileBounds))
        {
            return false;
        }

        var popCount = 0;
        sink.PushClip(geometry);
        popCount++;

        if (brush.Opacity != 1)
        {
            sink.PushOpacity(Math.Clamp(brush.Opacity, 0, 1));
            popCount++;
        }

        if (relativeTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, relativeTransform);
            popCount++;
        }

        if (brushTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, brushTransform);
            popCount++;
        }

        foreach (var tile in tileBounds)
        {
            if (!TryGetStretchedTile(tile, imageStretchSourceBounds, stretch, alignmentX, alignmentY, out var stretchedTile, out var needsTileClip))
            {
                continue;
            }

            var tilePopCount = 0;
            if (needsTileClip)
            {
                sink.PushClip(WpfReflectionResourceResolver.CreateRectanglePath(tile.Bounds));
                tilePopCount++;
            }

            if (TryCreateTileFlipTransform(stretchedTile, tileMode, out var tileTransform))
            {
                WpfPortableCommandSinkBridge.PushTransform(sink, tileTransform);
                tilePopCount++;
            }

            if (sourceRect.HasValue)
            {
                sink.DrawImage(imageSource, stretchedTile.Bounds, sourceRect.Value);
            }
            else
            {
                sink.DrawImage(imageSource, stretchedTile.Bounds);
            }

            for (var i = 0; i < tilePopCount; i++)
            {
                sink.Pop();
            }
        }

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }

        return true;
    }

    private static bool TryReplayPortableDrawingBrushFill(
        PortableTileBrush brush,
        MediaGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        var drawingValue = brush.Content;
        if (!TryGetOptionalBrushTransform(brush, out var brushTransform)
            || !TryGetSupportedTileMode(brush, out var tileMode)
            || !TryGetSupportedStretch(brush, out var stretch)
            || !TryGetTileBrushAlignment(brush, out var alignmentX, out var alignmentY)
            || !IsUsableRect(geometry.Bounds, out var geometryBounds)
            || !TryGetOptionalRelativeBrushTransform(brush, geometryBounds, out var relativeTransform)
            || !TryGetDrawingBounds(drawingValue, imageSourceAdapter, out var drawingBounds)
            || !TryGetTileBrushDestinationBounds(brush, geometryBounds, out var destinationBounds)
            || !TryGetTileBrushSourceBounds(brush, drawingBounds, out var sourceBounds, out var hasSourceClip)
            || !TryGetTileBounds(destinationBounds, geometryBounds, tileMode, out var tileBounds))
        {
            return false;
        }

        var popCount = 0;
        sink.PushClip(geometry);
        popCount++;

        if (brush.Opacity != 1)
        {
            sink.PushOpacity(Math.Clamp(brush.Opacity, 0, 1));
            popCount++;
        }

        if (relativeTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, relativeTransform);
            popCount++;
        }

        if (brushTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, brushTransform);
            popCount++;
        }

        var appliedAny = false;
        var unsupportedAny = false;
        foreach (var tile in tileBounds)
        {
            if (!TryGetStretchedTile(tile, sourceBounds, stretch, alignmentX, alignmentY, out var stretchedTile, out var needsTileClip)
                || !TryCreateBoundsMappingTransform(sourceBounds, stretchedTile, tileMode, out var transform))
            {
                continue;
            }

            var tilePopCount = 0;
            if (needsTileClip)
            {
                sink.PushClip(WpfReflectionResourceResolver.CreateRectanglePath(tile.Bounds));
                tilePopCount++;
            }

            WpfPortableCommandSinkBridge.PushTransform(sink, transform);
            tilePopCount++;

            if (hasSourceClip)
            {
                sink.PushClip(WpfReflectionResourceResolver.CreateRectanglePath(sourceBounds));
                tilePopCount++;
            }

            var tileStatus = Replay(drawingValue, sink, imageSourceAdapter);
            appliedAny |= tileStatus == WpfDrawingReplayStatus.Applied
                || tileStatus == WpfDrawingReplayStatus.PartiallyApplied;
            unsupportedAny |= tileStatus == WpfDrawingReplayStatus.Unsupported
                || tileStatus == WpfDrawingReplayStatus.PartiallyApplied;

            for (var i = 0; i < tilePopCount; i++)
            {
                sink.Pop();
            }
        }

        status = appliedAny
            ? unsupportedAny ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied
            : unsupportedAny ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }

        return status == WpfDrawingReplayStatus.Applied
            || status == WpfDrawingReplayStatus.PartiallyApplied;
    }

    private static bool TryReplayPortableVisualBrushFill(
        PortableTileBrush brush,
        MediaGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        var visualValue = brush.Content;
        if (!TryGetOptionalBrushTransform(brush, out var brushTransform)
            || !TryGetSupportedTileMode(brush, out var tileMode)
            || !TryGetSupportedStretch(brush, out var stretch)
            || !TryGetTileBrushAlignment(brush, out var alignmentX, out var alignmentY)
            || !IsUsableRect(geometry.Bounds, out var geometryBounds)
            || !TryGetOptionalRelativeBrushTransform(brush, geometryBounds, out var relativeTransform)
            || !TryGetVisualBounds(visualValue, out var visualBounds)
            || !TryGetTileBrushDestinationBounds(brush, geometryBounds, out var destinationBounds)
            || !TryGetTileBrushSourceBounds(brush, visualBounds, out var sourceBounds, out var hasSourceClip)
            || !TryGetTileBounds(destinationBounds, geometryBounds, tileMode, out var tileBounds))
        {
            return false;
        }

        var popCount = 0;
        sink.PushClip(geometry);
        popCount++;

        if (brush.Opacity != 1)
        {
            sink.PushOpacity(Math.Clamp(brush.Opacity, 0, 1));
            popCount++;
        }

        if (relativeTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, relativeTransform);
            popCount++;
        }

        if (brushTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, brushTransform);
            popCount++;
        }

        var appliedAny = false;
        var unsupportedAny = false;
        foreach (var tile in tileBounds)
        {
            if (!TryGetStretchedTile(tile, sourceBounds, stretch, alignmentX, alignmentY, out var stretchedTile, out var needsTileClip)
                || !TryCreateBoundsMappingTransform(sourceBounds, stretchedTile, tileMode, out var transform))
            {
                continue;
            }

            var tilePopCount = 0;
            if (needsTileClip)
            {
                sink.PushClip(WpfReflectionResourceResolver.CreateRectanglePath(tile.Bounds));
                tilePopCount++;
            }

            WpfPortableCommandSinkBridge.PushTransform(sink, transform);
            tilePopCount++;

            if (hasSourceClip)
            {
                sink.PushClip(WpfReflectionResourceResolver.CreateRectanglePath(sourceBounds));
                tilePopCount++;
            }

            var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(
                visualValue,
                sink,
                resources: null,
                imageSourceAdapter: CreateImageSourceAdapter(imageSourceAdapter));
            var tileStatus = ToDrawingReplayStatus(result);
            appliedAny |= tileStatus == WpfDrawingReplayStatus.Applied
                || tileStatus == WpfDrawingReplayStatus.PartiallyApplied;
            unsupportedAny |= tileStatus == WpfDrawingReplayStatus.Unsupported
                || tileStatus == WpfDrawingReplayStatus.PartiallyApplied;

            for (var i = 0; i < tilePopCount; i++)
            {
                sink.Pop();
            }
        }

        status = appliedAny
            ? unsupportedAny ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied
            : unsupportedAny ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }

        return status == WpfDrawingReplayStatus.Applied
            || status == WpfDrawingReplayStatus.PartiallyApplied;
    }

    internal static bool TryReplayImageBrushFill(
        object brush,
        MediaGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        if (!TypeNameEndsWith(brush, "ImageBrush"))
        {
            return false;
        }

        if (!TryGetOptionalBrushTransform(brush, "Transform", out var brushTransform)
            || !TryGetSupportedTileMode(brush, out var tileMode)
            || !TryGetSupportedStretch(brush, out var stretch)
            || !TryGetTileBrushAlignment(brush, out var alignmentX, out var alignmentY)
            || !IsUsableRect(geometry.Bounds, out var geometryBounds)
            || !TryGetOptionalRelativeBrushTransform(brush, geometryBounds, out var relativeTransform)
            || !TryGetPropertyValue(brush, "ImageSource", out var imageValue)
            || ResolveImageSource(imageValue, imageSourceAdapter) is not { } imageSource
            || !TryGetTileBrushDestinationBounds(brush, geometryBounds, out var imageBounds)
            || !TryGetImageBrushSourceRect(brush, imageSource, out var sourceRect)
            || !TryGetImageStretchSourceBounds(stretch, sourceRect, imageSource, out var imageStretchSourceBounds)
            || !TryGetTileBounds(imageBounds, geometryBounds, tileMode, out var tileBounds))
        {
            return false;
        }

        var popCount = 0;
        sink.PushClip(geometry);
        popCount++;

        if (TryReadDoubleProperty(brush, "Opacity", out var opacity) && opacity != 1)
        {
            sink.PushOpacity(Math.Clamp(opacity, 0, 1));
            popCount++;
        }

        if (relativeTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, relativeTransform);
            popCount++;
        }

        if (brushTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, brushTransform);
            popCount++;
        }

        foreach (var tile in tileBounds)
        {
            if (!TryGetStretchedTile(tile, imageStretchSourceBounds, stretch, alignmentX, alignmentY, out var stretchedTile, out var needsTileClip))
            {
                continue;
            }

            var tilePopCount = 0;
            if (needsTileClip)
            {
                sink.PushClip(WpfReflectionResourceResolver.CreateRectanglePath(tile.Bounds));
                tilePopCount++;
            }

            if (TryCreateTileFlipTransform(stretchedTile, tileMode, out var tileTransform))
            {
                WpfPortableCommandSinkBridge.PushTransform(sink, tileTransform);
                tilePopCount++;
            }

            if (sourceRect.HasValue)
            {
                sink.DrawImage(imageSource, stretchedTile.Bounds, sourceRect.Value);
            }
            else
            {
                sink.DrawImage(imageSource, stretchedTile.Bounds);
            }

            for (var i = 0; i < tilePopCount; i++)
            {
                sink.Pop();
            }
        }

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }

        return true;
    }

    private static bool TryReplayDrawingBrushFill(
        object brush,
        MediaGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        if (!TypeNameEndsWith(brush, "DrawingBrush")
            || !TryGetOptionalBrushTransform(brush, "Transform", out var brushTransform)
            || !TryGetSupportedTileMode(brush, out var tileMode)
            || !TryGetSupportedStretch(brush, out var stretch)
            || !TryGetTileBrushAlignment(brush, out var alignmentX, out var alignmentY)
            || !TryGetPropertyValue(brush, "Drawing", out var drawingValue)
            || drawingValue == null
            || !IsUsableRect(geometry.Bounds, out var geometryBounds)
            || !TryGetOptionalRelativeBrushTransform(brush, geometryBounds, out var relativeTransform)
            || !TryGetDrawingBounds(drawingValue, imageSourceAdapter, out var drawingBounds)
            || !TryGetTileBrushDestinationBounds(brush, geometryBounds, out var destinationBounds)
            || !TryGetTileBrushSourceBounds(brush, drawingBounds, out var sourceBounds, out var hasSourceClip)
            || !TryGetTileBounds(destinationBounds, geometryBounds, tileMode, out var tileBounds))
        {
            return false;
        }

        var popCount = 0;
        sink.PushClip(geometry);
        popCount++;

        if (TryReadDoubleProperty(brush, "Opacity", out var opacity) && opacity != 1)
        {
            sink.PushOpacity(Math.Clamp(opacity, 0, 1));
            popCount++;
        }

        if (relativeTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, relativeTransform);
            popCount++;
        }

        if (brushTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, brushTransform);
            popCount++;
        }

        var appliedAny = false;
        var unsupportedAny = false;
        foreach (var tile in tileBounds)
        {
            if (!TryGetStretchedTile(tile, sourceBounds, stretch, alignmentX, alignmentY, out var stretchedTile, out var needsTileClip)
                || !TryCreateBoundsMappingTransform(sourceBounds, stretchedTile, tileMode, out var transform))
            {
                continue;
            }

            var tilePopCount = 0;
            if (needsTileClip)
            {
                sink.PushClip(WpfReflectionResourceResolver.CreateRectanglePath(tile.Bounds));
                tilePopCount++;
            }

            WpfPortableCommandSinkBridge.PushTransform(sink, transform);
            tilePopCount++;

            if (hasSourceClip)
            {
                sink.PushClip(WpfReflectionResourceResolver.CreateRectanglePath(sourceBounds));
                tilePopCount++;
            }

            var tileStatus = Replay(drawingValue, sink, imageSourceAdapter);
            appliedAny |= tileStatus == WpfDrawingReplayStatus.Applied
                || tileStatus == WpfDrawingReplayStatus.PartiallyApplied;
            unsupportedAny |= tileStatus == WpfDrawingReplayStatus.Unsupported
                || tileStatus == WpfDrawingReplayStatus.PartiallyApplied;

            for (var i = 0; i < tilePopCount; i++)
            {
                sink.Pop();
            }
        }

        status = appliedAny
            ? unsupportedAny ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied
            : unsupportedAny ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }

        return status == WpfDrawingReplayStatus.Applied
            || status == WpfDrawingReplayStatus.PartiallyApplied;
    }

    private static bool TryReplayVisualBrushFill(
        object brush,
        MediaGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        if (!TypeNameEndsWith(brush, "VisualBrush")
            || !TryGetOptionalBrushTransform(brush, "Transform", out var brushTransform)
            || !TryGetSupportedTileMode(brush, out var tileMode)
            || !TryGetSupportedStretch(brush, out var stretch)
            || !TryGetTileBrushAlignment(brush, out var alignmentX, out var alignmentY)
            || !TryGetPropertyValue(brush, "Visual", out var visualValue)
            || visualValue == null
            || !IsUsableRect(geometry.Bounds, out var geometryBounds)
            || !TryGetOptionalRelativeBrushTransform(brush, geometryBounds, out var relativeTransform)
            || !TryGetVisualBounds(visualValue, out var visualBounds)
            || !TryGetTileBrushDestinationBounds(brush, geometryBounds, out var destinationBounds)
            || !TryGetTileBrushSourceBounds(brush, visualBounds, out var sourceBounds, out var hasSourceClip)
            || !TryGetTileBounds(destinationBounds, geometryBounds, tileMode, out var tileBounds))
        {
            return false;
        }

        var popCount = 0;
        sink.PushClip(geometry);
        popCount++;

        if (TryReadDoubleProperty(brush, "Opacity", out var opacity) && opacity != 1)
        {
            sink.PushOpacity(Math.Clamp(opacity, 0, 1));
            popCount++;
        }

        if (relativeTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, relativeTransform);
            popCount++;
        }

        if (brushTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, brushTransform);
            popCount++;
        }

        var appliedAny = false;
        var unsupportedAny = false;
        foreach (var tile in tileBounds)
        {
            if (!TryGetStretchedTile(tile, sourceBounds, stretch, alignmentX, alignmentY, out var stretchedTile, out var needsTileClip)
                || !TryCreateBoundsMappingTransform(sourceBounds, stretchedTile, tileMode, out var transform))
            {
                continue;
            }

            var tilePopCount = 0;
            if (needsTileClip)
            {
                sink.PushClip(WpfReflectionResourceResolver.CreateRectanglePath(tile.Bounds));
                tilePopCount++;
            }

            WpfPortableCommandSinkBridge.PushTransform(sink, transform);
            tilePopCount++;

            if (hasSourceClip)
            {
                sink.PushClip(WpfReflectionResourceResolver.CreateRectanglePath(sourceBounds));
                tilePopCount++;
            }

            var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(
                visualValue,
                sink,
                resources: null,
                imageSourceAdapter: CreateImageSourceAdapter(imageSourceAdapter));
            var tileStatus = ToDrawingReplayStatus(result);
            appliedAny |= tileStatus == WpfDrawingReplayStatus.Applied
                || tileStatus == WpfDrawingReplayStatus.PartiallyApplied;
            unsupportedAny |= tileStatus == WpfDrawingReplayStatus.Unsupported
                || tileStatus == WpfDrawingReplayStatus.PartiallyApplied;

            for (var i = 0; i < tilePopCount; i++)
            {
                sink.Pop();
            }
        }

        status = appliedAny
            ? unsupportedAny ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied
            : unsupportedAny ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }

        return status == WpfDrawingReplayStatus.Applied
            || status == WpfDrawingReplayStatus.PartiallyApplied;
    }

    private static WpfDrawingReplayStatus TryReplayDrawingGroup(
        object drawingGroup,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        if (!TryResolveDrawingGroupEffect(
                drawingGroup,
                imageSourceAdapter,
                out var effect,
                out var effectBounds,
                out var hasEffect))
        {
            return WpfDrawingReplayStatus.Unsupported;
        }

        var hasOpacityMask = TryResolveOpacityMask(
            drawingGroup,
            imageSourceAdapter,
            out var opacityMask,
            out var opacityMaskBounds);
        if (!hasOpacityMask && HasNonNullProperty(drawingGroup, "OpacityMask"))
        {
            return WpfDrawingReplayStatus.Unsupported;
        }

        var popCount = 0;

        var hasTransform = TryGetPropertyValue(drawingGroup, "Transform", out var transformValue) && transformValue != null;
        var transform = hasTransform ? WpfReflectionResourceResolver.AdaptTransform(transformValue) : null;
        if (hasTransform && transform == null)
        {
            return WpfDrawingReplayStatus.Unsupported;
        }

        var hasClip = TryGetPropertyValue(drawingGroup, "ClipGeometry", out var clipValue) && clipValue != null;
        var clip = hasClip ? WpfReflectionResourceResolver.AdaptGeometry(clipValue) : null;
        if (hasClip && clip == null)
        {
            return WpfDrawingReplayStatus.Unsupported;
        }

        if (transform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, transform);
            popCount++;
        }

        if (clip != null)
        {
            sink.PushClip(clip);
            popCount++;
        }

        if (TryGetPropertyValue(drawingGroup, "Opacity", out var opacityValue)
            && TryConvertToDouble(opacityValue, out var opacity)
            && opacity != 1)
        {
            sink.PushOpacity(opacity);
            popCount++;
        }

        if (hasOpacityMask)
        {
            WpfPortableCommandSinkBridge.PushOpacityMask(sink, opacityMask, ToReplayRect(opacityMaskBounds));
            popCount++;
        }

        var unsupportedGroupState = false;
        if (HasNonNullProperty(drawingGroup, "CacheMode"))
        {
            if (TryGetDrawingGroupCacheBounds(drawingGroup, imageSourceAdapter, out var cacheBounds)
                && WpfPortableCommandSinkBridge.TryPushDrawingCache(sink, ToReplayRect(cacheBounds)))
            {
                popCount++;
            }
            else
            {
                unsupportedGroupState = true;
            }
        }

        if (hasEffect)
        {
            if (!WpfPortableCommandSinkBridge.TryPushVisualEffect(sink, effect!, ToReplayRect(effectBounds)))
            {
                for (var i = 0; i < popCount; i++)
                {
                    sink.Pop();
                }

                return WpfDrawingReplayStatus.Unsupported;
            }

            popCount++;
        }

        if (TryGetPropertyValue(drawingGroup, "GuidelineSet", out var guidelineSet) && guidelineSet != null)
        {
            sink.PushGuidelineSet(guidelineSet);
            popCount++;
        }

        var unsupportedRenderOptions = HasUnsupportedRenderOptionState(drawingGroup);
        if (TryGetPropertyValue(drawingGroup, "BitmapScalingMode", out var bitmapScalingMode)
            && WpfBitmapScalingModeReflection.HasExplicitValue(bitmapScalingMode))
        {
            if (WpfBitmapScalingModeReflection.IsSupported(bitmapScalingMode))
            {
                sink.PushBitmapScalingMode(bitmapScalingMode);
                popCount++;
            }
            else
            {
                unsupportedRenderOptions = true;
            }
        }

        if (TryGetPropertyValue(drawingGroup, "EdgeMode", out var edgeMode)
            && WpfEdgeModeReflection.HasExplicitValue(edgeMode))
        {
            if (WpfEdgeModeReflection.IsSupported(edgeMode))
            {
                sink.PushEdgeMode(edgeMode);
                popCount++;
            }
            else
            {
                unsupportedRenderOptions = true;
            }
        }

        var pushedTextRenderingMode = false;
        if (TryGetPropertyValue(drawingGroup, "TextRenderingMode", out var textRenderingMode)
            && WpfTextRenderingModeReflection.HasExplicitValue(textRenderingMode))
        {
            if (WpfTextRenderingModeReflection.IsSupported(textRenderingMode))
            {
                sink.PushTextRenderingMode(textRenderingMode);
                popCount++;
                pushedTextRenderingMode = true;
            }
            else
            {
                unsupportedRenderOptions = true;
            }
        }

        if (!pushedTextRenderingMode
            && TryGetPropertyValue(drawingGroup, "ClearTypeHint", out var clearTypeHint)
            && WpfTextRenderingModeReflection.HasExplicitClearTypeHint(clearTypeHint)
            && WpfTextRenderingModeReflection.TryMapClearTypeHintToTextRenderingMode(clearTypeHint, out var clearTypeMode))
        {
            sink.PushTextRenderingMode(clearTypeMode);
            popCount++;
        }

        if (TryGetPropertyValue(drawingGroup, "TextHintingMode", out var textHintingMode)
            && WpfTextRenderingModeReflection.HasExplicitTextHintingMode(textHintingMode))
        {
            if (WpfTextRenderingModeReflection.IsSupportedTextHintingMode(textHintingMode))
            {
                sink.PushTextHintingMode(textHintingMode);
                popCount++;
            }
            else
            {
                unsupportedRenderOptions = true;
            }
        }

        var appliedAny = false;
        var unsupportedAny = unsupportedGroupState || unsupportedRenderOptions;
        foreach (var child in ExtractChildren(drawingGroup))
        {
            var childStatus = Replay(child, sink, imageSourceAdapter);
            appliedAny |= childStatus == WpfDrawingReplayStatus.Applied
                || childStatus == WpfDrawingReplayStatus.PartiallyApplied;
            unsupportedAny |= childStatus == WpfDrawingReplayStatus.Unsupported
                || childStatus == WpfDrawingReplayStatus.PartiallyApplied;
        }

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }

        if (appliedAny && unsupportedAny)
        {
            return WpfDrawingReplayStatus.PartiallyApplied;
        }

        if (appliedAny)
        {
            return WpfDrawingReplayStatus.Applied;
        }

        return unsupportedAny ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;
    }

    private static bool TryReplayImageDrawing(
        object drawing,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        if (!TryGetPropertyValue(drawing, "ImageSource", out var imageValue)
            || !TryGetPropertyValue(drawing, "Rect", out var rectValue)
            || rectValue == null
            || !TryReadRect(rectValue, out var rectangle)
            || ResolveImageSource(imageValue, imageSourceAdapter) is not { } imageSource)
        {
            return false;
        }

        sink.DrawImage(imageSource, rectangle);
        return true;
    }

    private static MediaImageSource? ResolveImageSource(
        object? imageSource,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        return imageSource is MediaImageSource mediaImageSource
            ? mediaImageSource
            : imageSourceAdapter?.Invoke(imageSource);
    }

    private static bool TryGetOptionalBrushTransform(
        object brush,
        string propertyName,
        out MediaTransform? transform)
    {
        transform = null;
        if (!TryGetPropertyValue(brush, propertyName, out var transformValue) || transformValue == null)
        {
            return true;
        }

        if (WpfReflectionResourceResolver.TryAdaptTransformMatrix(transformValue, out var matrix))
        {
            if (WpfReflectionResourceResolver.IsIdentityMatrix(matrix))
            {
                return true;
            }

            return TryCreateMatrixTransform(matrix, out transform);
        }

        transform = WpfReflectionResourceResolver.AdaptTransform(transformValue);
        if (transform == null)
        {
            return false;
        }

        if (transform.Value.IsIdentity)
        {
            transform = null;
        }

        return true;
    }

    private static bool TryGetOptionalRelativeBrushTransform(
        object brush,
        Rect fillBounds,
        out MediaTransform? transform)
    {
        transform = null;
        if (!TryGetPropertyValue(brush, "RelativeTransform", out var transformValue) || transformValue == null)
        {
            return true;
        }

        if (WpfReflectionResourceResolver.TryAdaptTransformMatrix(transformValue, out var relativeMatrix))
        {
            if (WpfReflectionResourceResolver.IsIdentityMatrix(relativeMatrix))
            {
                return true;
            }

            return TryCreateRelativeBoundsTransform(relativeMatrix, fillBounds, out transform);
        }

        var relativeTransform = WpfReflectionResourceResolver.AdaptTransform(transformValue);
        if (relativeTransform == null)
        {
            return false;
        }

        if (relativeTransform.Value.IsIdentity)
        {
            return true;
        }

        return TryCreateRelativeBoundsTransform(relativeTransform.Value, fillBounds, out transform);
    }

    private static bool TryGetOptionalBrushTransform(
        PortableTileBrush brush,
        out MediaTransform? transform)
    {
        transform = null;
        if (!brush.HasTransform || brush.Transform.IsIdentity)
        {
            return true;
        }

        return TryCreateMatrixTransform(ToMatrix4x4(brush.Transform), out transform);
    }

    private static bool TryGetOptionalRelativeBrushTransform(
        PortableTileBrush brush,
        Rect fillBounds,
        out MediaTransform? transform)
    {
        transform = null;
        if (!brush.HasRelativeTransform || brush.RelativeTransform.IsIdentity)
        {
            return true;
        }

        return TryCreateRelativeBoundsTransform(ToMatrix4x4(brush.RelativeTransform), fillBounds, out transform);
    }

    private static System.Numerics.Matrix4x4 ToMatrix4x4(PortableMatrix3x2 matrix)
    {
        return new System.Numerics.Matrix4x4(
            (float)matrix.M11,
            (float)matrix.M12,
            0f,
            0f,
            (float)matrix.M21,
            (float)matrix.M22,
            0f,
            0f,
            0f,
            0f,
            1f,
            0f,
            (float)matrix.OffsetX,
            (float)matrix.OffsetY,
            0f,
            1f);
    }

    private static bool TryCreateRelativeBoundsTransform(
        System.Numerics.Matrix4x4 relativeMatrix,
        Rect fillBounds,
        out MediaTransform? transform)
    {
        transform = null;
        if (!IsUsableRect(fillBounds, out fillBounds))
        {
            return false;
        }

        var boundsMatrix = System.Numerics.Matrix4x4.CreateTranslation((float)-fillBounds.X, (float)-fillBounds.Y, 0)
            * System.Numerics.Matrix4x4.CreateScale((float)(1 / fillBounds.Width), (float)(1 / fillBounds.Height), 1)
            * relativeMatrix
            * System.Numerics.Matrix4x4.CreateScale((float)fillBounds.Width, (float)fillBounds.Height, 1)
            * System.Numerics.Matrix4x4.CreateTranslation((float)fillBounds.X, (float)fillBounds.Y, 0);

        return TryCreateMatrixTransform(boundsMatrix, out transform);
    }

    private static bool TryCreateMatrixTransform(
        System.Numerics.Matrix4x4 matrix,
        out MediaTransform? transform)
    {
        transform = null;
        if (!NearlyEqual(matrix.M13, 0)
            || !NearlyEqual(matrix.M14, 0)
            || !NearlyEqual(matrix.M23, 0)
            || !NearlyEqual(matrix.M24, 0)
            || !NearlyEqual(matrix.M31, 0)
            || !NearlyEqual(matrix.M32, 0)
            || !NearlyEqual(matrix.M33, 1)
            || !NearlyEqual(matrix.M34, 0)
            || !NearlyEqual(matrix.M43, 0)
            || !NearlyEqual(matrix.M44, 1))
        {
            return false;
        }

        transform = new MediaMatrixTransform(new MediaMatrix
        {
            M11 = matrix.M11,
            M12 = matrix.M12,
            M21 = matrix.M21,
            M22 = matrix.M22,
            OffsetX = matrix.M41,
            OffsetY = matrix.M42
        });
        return true;
    }

    private static bool TryGetSupportedTileMode(object brush, out SupportedTileMode tileMode)
    {
        tileMode = SupportedTileMode.None;
        if (!TryGetPropertyValue(brush, "TileMode", out var value) || value == null)
        {
            return true;
        }

        var name = value.ToString();
        if (string.Equals(name, "None", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(name, "Tile", StringComparison.Ordinal))
        {
            tileMode = SupportedTileMode.Tile;
            return true;
        }

        if (string.Equals(name, "FlipX", StringComparison.Ordinal))
        {
            tileMode = SupportedTileMode.FlipX;
            return true;
        }

        if (string.Equals(name, "FlipY", StringComparison.Ordinal))
        {
            tileMode = SupportedTileMode.FlipY;
            return true;
        }

        if (string.Equals(name, "FlipXY", StringComparison.Ordinal))
        {
            tileMode = SupportedTileMode.FlipXY;
            return true;
        }

        return false;
    }

    private static bool TryGetSupportedTileMode(PortableTileBrush brush, out SupportedTileMode tileMode)
    {
        switch (brush.TileMode)
        {
            case PortableTileMode.None:
                tileMode = SupportedTileMode.None;
                return true;
            case PortableTileMode.Tile:
                tileMode = SupportedTileMode.Tile;
                return true;
            case PortableTileMode.FlipX:
                tileMode = SupportedTileMode.FlipX;
                return true;
            case PortableTileMode.FlipY:
                tileMode = SupportedTileMode.FlipY;
                return true;
            case PortableTileMode.FlipXY:
                tileMode = SupportedTileMode.FlipXY;
                return true;
            default:
                tileMode = SupportedTileMode.None;
                return false;
        }
    }

    private static bool TryGetSupportedStretch(object brush, out SupportedStretch stretch)
    {
        stretch = SupportedStretch.Fill;
        if (!TryGetPropertyValue(brush, "Stretch", out var value) || value == null)
        {
            return true;
        }

        var name = value.ToString();
        if (string.Equals(name, "None", StringComparison.Ordinal))
        {
            stretch = SupportedStretch.None;
            return true;
        }

        if (string.Equals(name, "Fill", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(name, "Uniform", StringComparison.Ordinal))
        {
            stretch = SupportedStretch.Uniform;
            return true;
        }

        if (string.Equals(name, "UniformToFill", StringComparison.Ordinal))
        {
            stretch = SupportedStretch.UniformToFill;
            return true;
        }

        return false;
    }

    private static bool TryGetSupportedStretch(PortableTileBrush brush, out SupportedStretch stretch)
    {
        switch (brush.Stretch)
        {
            case PortableStretch.None:
                stretch = SupportedStretch.None;
                return true;
            case PortableStretch.Fill:
                stretch = SupportedStretch.Fill;
                return true;
            case PortableStretch.Uniform:
                stretch = SupportedStretch.Uniform;
                return true;
            case PortableStretch.UniformToFill:
                stretch = SupportedStretch.UniformToFill;
                return true;
            default:
                stretch = SupportedStretch.Fill;
                return false;
        }
    }

    private static bool EnumPropertyIsAbsentOrNamed(object instance, string propertyName, string supportedName)
    {
        if (!TryGetPropertyValue(instance, propertyName, out var value) || value == null)
        {
            return true;
        }

        return string.Equals(value.ToString(), supportedName, StringComparison.Ordinal);
    }

    private static bool TryGetTileBrushAlignment(
        object brush,
        out SupportedAlignmentX alignmentX,
        out SupportedAlignmentY alignmentY)
    {
        alignmentX = SupportedAlignmentX.Center;
        alignmentY = SupportedAlignmentY.Center;

        if (TryGetPropertyValue(brush, "AlignmentX", out var alignmentXValue) && alignmentXValue != null)
        {
            var name = alignmentXValue.ToString();
            if (string.Equals(name, "Left", StringComparison.Ordinal))
            {
                alignmentX = SupportedAlignmentX.Left;
            }
            else if (string.Equals(name, "Center", StringComparison.Ordinal))
            {
                alignmentX = SupportedAlignmentX.Center;
            }
            else if (string.Equals(name, "Right", StringComparison.Ordinal))
            {
                alignmentX = SupportedAlignmentX.Right;
            }
            else
            {
                return false;
            }
        }

        if (TryGetPropertyValue(brush, "AlignmentY", out var alignmentYValue) && alignmentYValue != null)
        {
            var name = alignmentYValue.ToString();
            if (string.Equals(name, "Top", StringComparison.Ordinal))
            {
                alignmentY = SupportedAlignmentY.Top;
            }
            else if (string.Equals(name, "Center", StringComparison.Ordinal))
            {
                alignmentY = SupportedAlignmentY.Center;
            }
            else if (string.Equals(name, "Bottom", StringComparison.Ordinal))
            {
                alignmentY = SupportedAlignmentY.Bottom;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetTileBrushAlignment(
        PortableTileBrush brush,
        out SupportedAlignmentX alignmentX,
        out SupportedAlignmentY alignmentY)
    {
        alignmentX = brush.AlignmentX switch
        {
            PortableAlignmentX.Left => SupportedAlignmentX.Left,
            PortableAlignmentX.Right => SupportedAlignmentX.Right,
            _ => SupportedAlignmentX.Center
        };

        alignmentY = brush.AlignmentY switch
        {
            PortableAlignmentY.Top => SupportedAlignmentY.Top,
            PortableAlignmentY.Bottom => SupportedAlignmentY.Bottom,
            _ => SupportedAlignmentY.Center
        };

        return true;
    }

    private static bool TryGetTileBrushDestinationBounds(object brush, Rect fillBounds, out Rect destinationBounds)
    {
        destinationBounds = default;
        var viewport = new Rect(0, 0, 1, 1);
        if (!TryGetPropertyValue(brush, "Viewport", out var viewportValue) || viewportValue == null)
        {
            return TryGetViewportDestinationBounds(brush, fillBounds, viewport, out destinationBounds);
        }

        return TryReadRect(viewportValue, out viewport)
            && IsUsableRect(viewport, out viewport)
            && TryGetViewportDestinationBounds(brush, fillBounds, viewport, out destinationBounds);
    }

    private static bool TryGetTileBrushDestinationBounds(
        PortableTileBrush brush,
        Rect fillBounds,
        out Rect destinationBounds)
    {
        destinationBounds = default;
        var viewport = ToRect(brush.Viewport);
        return IsUsableRect(viewport, out viewport)
            && TryGetViewportDestinationBounds(brush, fillBounds, viewport, out destinationBounds);
    }

    private static bool TryGetViewportDestinationBounds(
        object brush,
        Rect fillBounds,
        Rect viewport,
        out Rect destinationBounds)
    {
        destinationBounds = default;

        if (!TryGetPropertyValue(brush, "ViewportUnits", out var viewportUnitsValue)
            || viewportUnitsValue == null
            || string.Equals(viewportUnitsValue.ToString(), "RelativeToBoundingBox", StringComparison.Ordinal))
        {
            return IsUsableRect(
                new Rect(
                    fillBounds.X + fillBounds.Width * viewport.X,
                    fillBounds.Y + fillBounds.Height * viewport.Y,
                    fillBounds.Width * viewport.Width,
                    fillBounds.Height * viewport.Height),
                out destinationBounds);
        }

        if (string.Equals(viewportUnitsValue.ToString(), "Absolute", StringComparison.Ordinal))
        {
            return IsUsableRect(viewport, out destinationBounds);
        }

        return false;
    }

    private static bool TryGetViewportDestinationBounds(
        PortableTileBrush brush,
        Rect fillBounds,
        Rect viewport,
        out Rect destinationBounds)
    {
        destinationBounds = default;

        if (brush.ViewportUnits == PortableBrushMappingMode.RelativeToBoundingBox)
        {
            return IsUsableRect(
                new Rect(
                    fillBounds.X + fillBounds.Width * viewport.X,
                    fillBounds.Y + fillBounds.Height * viewport.Y,
                    fillBounds.Width * viewport.Width,
                    fillBounds.Height * viewport.Height),
                out destinationBounds);
        }

        if (brush.ViewportUnits == PortableBrushMappingMode.Absolute)
        {
            return IsUsableRect(viewport, out destinationBounds);
        }

        return false;
    }

    private static bool TryGetTileBounds(
        Rect viewport,
        Rect fillBounds,
        SupportedTileMode tileMode,
        out IReadOnlyList<TileBrushReplayTile> tileBounds)
    {
        tileBounds = Array.Empty<TileBrushReplayTile>();
        if (!IsUsableRect(viewport, out viewport)
            || !IsUsableRect(fillBounds, out fillBounds))
        {
            return false;
        }

        if (tileMode == SupportedTileMode.None)
        {
            tileBounds = new[] { new TileBrushReplayTile(viewport, 0, 0) };
            return true;
        }

        var startX = (int)Math.Floor((fillBounds.X - viewport.X) / viewport.Width);
        var endX = (int)Math.Ceiling((fillBounds.X + fillBounds.Width - viewport.X) / viewport.Width) - 1;
        var startY = (int)Math.Floor((fillBounds.Y - viewport.Y) / viewport.Height);
        var endY = (int)Math.Ceiling((fillBounds.Y + fillBounds.Height - viewport.Y) / viewport.Height) - 1;

        var columnCount = endX - startX + 1;
        var rowCount = endY - startY + 1;
        if (columnCount <= 0
            || rowCount <= 0
            || columnCount > MaxTileBrushReplayTiles
            || rowCount > MaxTileBrushReplayTiles
            || columnCount * rowCount > MaxTileBrushReplayTiles)
        {
            return false;
        }

        var tiles = new List<TileBrushReplayTile>(columnCount * rowCount);
        for (var y = startY; y <= endY; y++)
        {
            for (var x = startX; x <= endX; x++)
            {
                tiles.Add(new TileBrushReplayTile(
                    new Rect(
                        viewport.X + x * viewport.Width,
                        viewport.Y + y * viewport.Height,
                        viewport.Width,
                        viewport.Height),
                    x,
                    y));
            }
        }

        tileBounds = tiles;
        return true;
    }

    private static bool TryGetImageStretchSourceBounds(
        SupportedStretch stretch,
        Rect? sourceRect,
        MediaImageSource imageSource,
        out Rect sourceBounds)
    {
        sourceBounds = default;
        if (stretch == SupportedStretch.Fill)
        {
            return true;
        }

        if (sourceRect.HasValue)
        {
            return IsUsableRect(sourceRect.Value, out sourceBounds);
        }

        return TryGetImagePixelBounds(imageSource, out sourceBounds);
    }

    private static bool TryGetStretchedTile(
        TileBrushReplayTile tile,
        Rect sourceBounds,
        SupportedStretch stretch,
        SupportedAlignmentX alignmentX,
        SupportedAlignmentY alignmentY,
        out TileBrushReplayTile stretchedTile,
        out bool needsTileClip)
    {
        stretchedTile = tile;
        needsTileClip = false;

        if (stretch == SupportedStretch.Fill)
        {
            return true;
        }

        if (!IsUsableRect(sourceBounds, out sourceBounds)
            || !IsUsableRect(tile.Bounds, out var tileBounds))
        {
            return false;
        }

        var width = sourceBounds.Width;
        var height = sourceBounds.Height;
        if (stretch == SupportedStretch.Uniform || stretch == SupportedStretch.UniformToFill)
        {
            var scaleX = tileBounds.Width / sourceBounds.Width;
            var scaleY = tileBounds.Height / sourceBounds.Height;
            var scale = stretch == SupportedStretch.Uniform
                ? Math.Min(scaleX, scaleY)
                : Math.Max(scaleX, scaleY);
            width = sourceBounds.Width * scale;
            height = sourceBounds.Height * scale;
        }

        var x = alignmentX switch
        {
            SupportedAlignmentX.Left => tileBounds.X,
            SupportedAlignmentX.Right => tileBounds.X + tileBounds.Width - width,
            _ => tileBounds.X + (tileBounds.Width - width) / 2
        };
        var y = alignmentY switch
        {
            SupportedAlignmentY.Top => tileBounds.Y,
            SupportedAlignmentY.Bottom => tileBounds.Y + tileBounds.Height - height,
            _ => tileBounds.Y + (tileBounds.Height - height) / 2
        };

        var stretchedBounds = new Rect(x, y, width, height);
        if (!IsUsableRect(stretchedBounds, out stretchedBounds))
        {
            return false;
        }

        stretchedTile = new TileBrushReplayTile(stretchedBounds, tile.Column, tile.Row);
        needsTileClip = stretchedBounds.X < tileBounds.X
            || stretchedBounds.Y < tileBounds.Y
            || stretchedBounds.X + stretchedBounds.Width > tileBounds.X + tileBounds.Width
            || stretchedBounds.Y + stretchedBounds.Height > tileBounds.Y + tileBounds.Height;
        return true;
    }

    private static bool TryGetImageBrushSourceRect(
        object brush,
        MediaImageSource imageSource,
        out Rect? sourceRect)
    {
        sourceRect = null;
        if (!TryGetPropertyValue(brush, "Viewbox", out var viewboxValue) || viewboxValue == null)
        {
            if (EnumPropertyIsAbsentOrNamed(brush, "ViewboxUnits", "RelativeToBoundingBox"))
            {
                return true;
            }

            if (EnumPropertyIsAbsentOrNamed(brush, "ViewboxUnits", "Absolute"))
            {
                sourceRect = new Rect(0, 0, 1, 1);
                return true;
            }

            return false;
        }

        if (!TryReadRect(viewboxValue, out var viewbox)
            || !IsUsableRect(viewbox, out viewbox))
        {
            return false;
        }

        if (!TryGetPropertyValue(brush, "ViewboxUnits", out var viewboxUnitsValue)
            || viewboxUnitsValue == null
            || string.Equals(viewboxUnitsValue.ToString(), "RelativeToBoundingBox", StringComparison.Ordinal))
        {
            if (IsFullRelativeRect(viewbox))
            {
                return true;
            }

            if (!TryGetImagePixelBounds(imageSource, out var imageBounds))
            {
                return false;
            }

            return IsUsableRect(
                    new Rect(
                        imageBounds.X + imageBounds.Width * viewbox.X,
                        imageBounds.Y + imageBounds.Height * viewbox.Y,
                        imageBounds.Width * viewbox.Width,
                        imageBounds.Height * viewbox.Height),
                    out var relativeSourceRect)
                && AssignSourceRect(relativeSourceRect, out sourceRect);
        }

        return string.Equals(viewboxUnitsValue.ToString(), "Absolute", StringComparison.Ordinal)
            && AssignSourceRect(viewbox, out sourceRect);
    }

    private static bool TryGetImageBrushSourceRect(
        PortableTileBrush brush,
        MediaImageSource imageSource,
        out Rect? sourceRect)
    {
        sourceRect = null;
        var viewbox = ToRect(brush.Viewbox);
        if (!IsUsableRect(viewbox, out viewbox))
        {
            return false;
        }

        if (brush.ViewboxUnits == PortableBrushMappingMode.RelativeToBoundingBox)
        {
            if (IsFullRelativeRect(viewbox))
            {
                return true;
            }

            if (!TryGetImagePixelBounds(imageSource, out var imageBounds))
            {
                return false;
            }

            return IsUsableRect(
                    new Rect(
                        imageBounds.X + imageBounds.Width * viewbox.X,
                        imageBounds.Y + imageBounds.Height * viewbox.Y,
                        imageBounds.Width * viewbox.Width,
                        imageBounds.Height * viewbox.Height),
                    out var relativeSourceRect)
                && AssignSourceRect(relativeSourceRect, out sourceRect);
        }

        return brush.ViewboxUnits == PortableBrushMappingMode.Absolute
            && AssignSourceRect(viewbox, out sourceRect);
    }

    private static bool TryGetImagePixelBounds(MediaImageSource imageSource, out Rect bounds)
    {
        if (imageSource is MediaBitmapSource bitmapSource
            && bitmapSource.PixelWidth > 0
            && bitmapSource.PixelHeight > 0)
        {
            bounds = new Rect(0, 0, bitmapSource.PixelWidth, bitmapSource.PixelHeight);
            return true;
        }

        if (TryReadIntProperty(imageSource, "PixelWidth", out var width)
            && TryReadIntProperty(imageSource, "PixelHeight", out var height)
            && width > 0
            && height > 0)
        {
            bounds = new Rect(0, 0, width, height);
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryGetTileBrushSourceBounds(
        object brush,
        Rect contentBounds,
        out Rect sourceBounds,
        out bool hasSourceClip)
    {
        sourceBounds = contentBounds;
        hasSourceClip = false;

        if (!TryGetPropertyValue(brush, "Viewbox", out var viewboxValue) || viewboxValue == null)
        {
            if (EnumPropertyIsAbsentOrNamed(brush, "ViewboxUnits", "RelativeToBoundingBox"))
            {
                return true;
            }

            sourceBounds = new Rect(0, 0, 1, 1);
            hasSourceClip = true;
            return EnumPropertyIsAbsentOrNamed(brush, "ViewboxUnits", "Absolute")
                && IsUsableRect(sourceBounds, out sourceBounds);
        }

        if (!TryReadRect(viewboxValue, out var viewbox)
            || !IsUsableRect(viewbox, out viewbox))
        {
            return false;
        }

        if (!TryGetPropertyValue(brush, "ViewboxUnits", out var viewboxUnitsValue)
            || viewboxUnitsValue == null
            || string.Equals(viewboxUnitsValue.ToString(), "RelativeToBoundingBox", StringComparison.Ordinal))
        {
            if (IsFullRelativeRect(viewbox))
            {
                return true;
            }

            sourceBounds = new Rect(
                contentBounds.X + contentBounds.Width * viewbox.X,
                contentBounds.Y + contentBounds.Height * viewbox.Y,
                contentBounds.Width * viewbox.Width,
                contentBounds.Height * viewbox.Height);
            hasSourceClip = true;
            return IsUsableRect(sourceBounds, out sourceBounds);
        }

        if (string.Equals(viewboxUnitsValue.ToString(), "Absolute", StringComparison.Ordinal))
        {
            sourceBounds = viewbox;
            hasSourceClip = !RectNearlyEqual(sourceBounds, contentBounds);
            return IsUsableRect(sourceBounds, out sourceBounds);
        }

        sourceBounds = default;
        hasSourceClip = false;
        return false;
    }

    private static bool TryGetTileBrushSourceBounds(
        PortableTileBrush brush,
        Rect contentBounds,
        out Rect sourceBounds,
        out bool hasSourceClip)
    {
        sourceBounds = contentBounds;
        hasSourceClip = false;

        var viewbox = ToRect(brush.Viewbox);
        if (!IsUsableRect(viewbox, out viewbox))
        {
            return false;
        }

        if (brush.ViewboxUnits == PortableBrushMappingMode.RelativeToBoundingBox)
        {
            if (IsFullRelativeRect(viewbox))
            {
                return true;
            }

            sourceBounds = new Rect(
                contentBounds.X + contentBounds.Width * viewbox.X,
                contentBounds.Y + contentBounds.Height * viewbox.Y,
                contentBounds.Width * viewbox.Width,
                contentBounds.Height * viewbox.Height);
            hasSourceClip = true;
            return IsUsableRect(sourceBounds, out sourceBounds);
        }

        if (brush.ViewboxUnits == PortableBrushMappingMode.Absolute)
        {
            sourceBounds = viewbox;
            hasSourceClip = !RectNearlyEqual(sourceBounds, contentBounds);
            return IsUsableRect(sourceBounds, out sourceBounds);
        }

        sourceBounds = default;
        hasSourceClip = false;
        return false;
    }

    private static bool AssignSourceRect(Rect value, out Rect? sourceRect)
    {
        sourceRect = value;
        return true;
    }

    private static Rect ToRect(PortableRect rect)
    {
        return rect.IsEmpty
            ? Rect.Empty
            : new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static bool RelativeRectPropertyIsAbsentOrFull(object instance, string propertyName)
    {
        if (!TryGetPropertyValue(instance, propertyName, out var rectValue) || rectValue == null)
        {
            return true;
        }

        return TryReadRect(rectValue, out var rect)
            && IsFullRelativeRect(rect);
    }

    private static bool IsFullRelativeRect(Rect rect)
    {
        return NearlyEqual(rect.X, 0)
            && NearlyEqual(rect.Y, 0)
            && NearlyEqual(rect.Width, 1)
            && NearlyEqual(rect.Height, 1);
    }

    private static bool RectNearlyEqual(Rect left, Rect right)
    {
        return NearlyEqual(left.X, right.X)
            && NearlyEqual(left.Y, right.Y)
            && NearlyEqual(left.Width, right.Width)
            && NearlyEqual(left.Height, right.Height);
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= 0.000001;
    }

    private static bool TryCreateBoundsMappingTransform(
        Rect sourceBounds,
        TileBrushReplayTile tile,
        SupportedTileMode tileMode,
        out MediaMatrixTransform transform)
    {
        transform = null!;
        var destinationBounds = tile.Bounds;
        if (!IsUsableRect(sourceBounds, out sourceBounds)
            || !IsUsableRect(destinationBounds, out destinationBounds))
        {
            return false;
        }

        var flipX = tile.FlipX(tileMode);
        var flipY = tile.FlipY(tileMode);
        var scaleX = destinationBounds.Width / sourceBounds.Width * (flipX ? -1 : 1);
        var scaleY = destinationBounds.Height / sourceBounds.Height * (flipY ? -1 : 1);
        transform = new MediaMatrixTransform(new MediaMatrix
        {
            M11 = scaleX,
            M22 = scaleY,
            OffsetX = (flipX ? destinationBounds.X + destinationBounds.Width : destinationBounds.X) - sourceBounds.X * scaleX,
            OffsetY = (flipY ? destinationBounds.Y + destinationBounds.Height : destinationBounds.Y) - sourceBounds.Y * scaleY
        });
        return true;
    }

    private static bool TryCreateTileFlipTransform(
        TileBrushReplayTile tile,
        SupportedTileMode tileMode,
        out MediaMatrixTransform transform)
    {
        transform = null!;
        var flipX = tile.FlipX(tileMode);
        var flipY = tile.FlipY(tileMode);
        if (!flipX && !flipY)
        {
            return false;
        }

        var bounds = tile.Bounds;
        transform = new MediaMatrixTransform(new MediaMatrix
        {
            M11 = flipX ? -1 : 1,
            M22 = flipY ? -1 : 1,
            OffsetX = flipX ? bounds.X + bounds.X + bounds.Width : 0,
            OffsetY = flipY ? bounds.Y + bounds.Y + bounds.Height : 0
        });
        return true;
    }

    private static bool TryReplayGlyphRunDrawing(object drawing, IWpfCompositionCommandSink sink)
    {
        if (!TryGetPropertyValue(drawing, "GlyphRun", out var glyphRunValue)
            || WpfReflectionResourceResolver.AdaptGlyphRun(glyphRunValue) is not { } glyphRun)
        {
            return false;
        }

        TryGetPropertyValue(drawing, "ForegroundBrush", out var foregroundBrushValue);
        sink.DrawGlyphRun(WpfReflectionResourceResolver.AdaptBrush(foregroundBrushValue), glyphRun);
        return true;
    }

    private static bool HasUnsupportedRenderOptionState(object drawingGroup)
    {
        if (TryGetPropertyValue(drawingGroup, "ClearTypeHint", out var clearTypeHint)
            && WpfTextRenderingModeReflection.HasExplicitClearTypeHint(clearTypeHint)
            && !WpfTextRenderingModeReflection.IsSupportedClearTypeHint(clearTypeHint))
        {
            return true;
        }

        return TryGetPropertyValue(drawingGroup, "TextHintingMode", out var textHintingMode)
            && WpfTextRenderingModeReflection.HasExplicitTextHintingMode(textHintingMode)
            && !WpfTextRenderingModeReflection.IsSupportedTextHintingMode(textHintingMode);
    }

    private static bool TryResolveDrawingGroupEffect(
        object drawingGroup,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out global::ProGPU.Scene.EffectBase? effect,
        out Rect? bounds,
        out bool hasEffect)
    {
        effect = null;
        bounds = null;
        hasEffect = false;

        if (TryGetPropertyValue(drawingGroup, "Effect", out var effectValue) && effectValue != null)
        {
            hasEffect = true;
            if (!WpfEffectReflection.TryCreateProGpuEffect(
                    effectValue,
                    out var proGpuEffect,
                    CreateImageSourceAdapter(imageSourceAdapter))
                || !TryGetDrawingGroupEffectBounds(drawingGroup, imageSourceAdapter, out bounds))
            {
                return false;
            }

            effect = proGpuEffect;
            return true;
        }

        if (TryGetPropertyValue(drawingGroup, "BitmapEffect", out var bitmapEffect) && bitmapEffect != null)
        {
            hasEffect = true;
            TryGetPropertyValue(drawingGroup, "BitmapEffectInput", out var bitmapEffectInput);
            if (!WpfEffectReflection.TryCreateProGpuPushEffect(
                    bitmapEffect,
                    bitmapEffectInput,
                    out var proGpuEffect,
                    CreateImageSourceAdapter(imageSourceAdapter))
                || !TryGetDrawingGroupEffectBounds(drawingGroup, imageSourceAdapter, out bounds))
            {
                return false;
            }

            effect = proGpuEffect;
            return true;
        }

        return !HasNonNullProperty(drawingGroup, "BitmapEffectInput");
    }

    private static bool TryGetDrawingGroupEffectBounds(
        object drawingGroup,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out Rect? bounds)
    {
        bounds = null;
        if (TryReadFiniteRectProperty(drawingGroup, "Bounds", out var explicitBounds)
            || TryInferDrawingGroupContentBounds(drawingGroup, imageSourceAdapter, out explicitBounds))
        {
            bounds = explicitBounds;
            return true;
        }

        return false;
    }

    private static bool TryGetDrawingGroupCacheBounds(
        object drawingGroup,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out Rect? bounds)
    {
        return TryGetDrawingGroupEffectBounds(drawingGroup, imageSourceAdapter, out bounds);
    }

    private static bool HasExplicitRenderingHint(object source, string propertyName)
    {
        if (!TryGetPropertyValue(source, propertyName, out var value))
        {
            return false;
        }

        var text = value?.ToString();
        return !string.IsNullOrWhiteSpace(text)
            && !string.Equals(text, "Unspecified", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "Default", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveOpacityMask(
        object drawingGroup,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out MediaBrush? opacityMask,
        out Rect bounds)
    {
        opacityMask = null;
        bounds = default;

        if (!TryGetPropertyValue(drawingGroup, "OpacityMask", out var maskValue) || maskValue == null)
        {
            return false;
        }

        opacityMask = WpfReflectionResourceResolver.AdaptBrush(maskValue);
        if (opacityMask == null)
        {
            return false;
        }

        return TryReadFiniteRectProperty(drawingGroup, "Bounds", out bounds)
            || TryInferDrawingGroupContentBounds(drawingGroup, imageSourceAdapter, out bounds);
    }

    internal static bool TryGetDrawingBounds(
        object drawing,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out Rect bounds)
    {
        if (TryReadFiniteRectProperty(drawing, "Bounds", out bounds))
        {
            return true;
        }

        if (TypeNameEndsWith(drawing, "GeometryDrawing"))
        {
            return TryGetPropertyValue(drawing, "Geometry", out var geometryValue)
                && WpfReflectionResourceResolver.AdaptGeometry(geometryValue) is { } geometry
                && IsUsableRect(geometry.Bounds, out bounds);
        }

        if (TypeNameEndsWith(drawing, "ImageDrawing"))
        {
            return TryGetPropertyValue(drawing, "Rect", out var rectValue)
                && rectValue != null
                && TryReadRect(rectValue, out var imageRect)
                && IsUsableRect(imageRect, out bounds);
        }

        if (TypeNameEndsWith(drawing, "GlyphRunDrawing"))
        {
            return TryGetPropertyValue(drawing, "GlyphRun", out var glyphRunValue)
                && WpfReflectionResourceResolver.AdaptGlyphRun(glyphRunValue) is { } glyphRun
                && TryGetGlyphRunBounds(glyphRun, out bounds);
        }

        if (TypeNameEndsWith(drawing, "DrawingGroup")
            && TryInferDrawingGroupContentBounds(drawing, imageSourceAdapter, out bounds))
        {
            if (TryGetPropertyValue(drawing, "Transform", out var transformValue) && transformValue != null)
            {
                if (WpfReflectionResourceResolver.AdaptTransform(transformValue) is not { } transform)
                {
                    bounds = default;
                    return false;
                }

                bounds = TransformBounds(bounds, transform.Value);
            }

            return IsUsableRect(bounds, out bounds);
        }

        bounds = default;
        return false;
    }

    internal static bool TryGetVisualBounds(object visual, out Rect bounds)
    {
        foreach (var propertyName in new[] { "Bounds", "DescendantBounds", "VisualContentBounds", "ContentBounds" })
        {
            if (TryReadFiniteRectProperty(visual, propertyName, out bounds))
            {
                return true;
            }
        }

        if (TryGetPropertyValue(visual, "RenderSize", out var renderSize)
            && renderSize != null
            && TryReadDoubleProperty(renderSize, "Width", out var width)
            && TryReadDoubleProperty(renderSize, "Height", out var height)
            && IsUsableRect(new Rect(0, 0, width, height), out bounds))
        {
            return true;
        }

        if (TryReadDoubleProperty(visual, "ActualWidth", out width)
            && TryReadDoubleProperty(visual, "ActualHeight", out height)
            && IsUsableRect(new Rect(0, 0, width, height), out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static WpfDrawingReplayStatus ToDrawingReplayStatus(WpfVisualReplayResult result)
    {
        var applied = result.ContentCount > 0
            || result.RenderData.AppliedCount > 0;
        var unsupported = result.UnsupportedContentCount > 0
            || result.UnsupportedVisualStateCount > 0
            || result.RenderData.UnsupportedCount > 0;

        if (applied && unsupported)
        {
            return WpfDrawingReplayStatus.PartiallyApplied;
        }

        if (applied)
        {
            return WpfDrawingReplayStatus.Applied;
        }

        return unsupported ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;
    }

    private static IWpfImageSourceAdapter? CreateImageSourceAdapter(Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        return imageSourceAdapter == null ? null : new DelegateImageSourceAdapter(imageSourceAdapter);
    }

    private static bool TryInferDrawingGroupContentBounds(
        object drawingGroup,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out Rect bounds)
    {
        var hasBounds = false;
        bounds = default;

        foreach (var child in ExtractChildren(drawingGroup))
        {
            if (!TryGetDrawingBounds(child, imageSourceAdapter, out var childBounds))
            {
                continue;
            }

            bounds = hasBounds ? UnionBounds(bounds, childBounds) : childBounds;
            hasBounds = true;
        }

        if (!hasBounds)
        {
            bounds = default;
            return false;
        }

        if (TryGetPropertyValue(drawingGroup, "ClipGeometry", out var clipValue)
            && clipValue != null
            && WpfReflectionResourceResolver.AdaptGeometry(clipValue) is { } clipGeometry
            && IsUsableRect(clipGeometry.Bounds, out var clipBounds))
        {
            bounds = IntersectBounds(bounds, clipBounds);
        }

        return IsUsableRect(bounds, out bounds);
    }

    private static bool TryGetGlyphRunBounds(MediaGlyphRun glyphRun, out Rect bounds)
    {
        bounds = default;

        if (glyphRun.FontSize <= 0)
        {
            return false;
        }

        var minX = glyphRun.Position.X;
        var minY = glyphRun.Position.Y - glyphRun.FontSize;
        var maxX = glyphRun.Position.X;
        var maxY = glyphRun.Position.Y;

        if (glyphRun.GlyphPositions.Length == 0)
        {
            maxX += glyphRun.FontSize;
        }
        else
        {
            foreach (var position in glyphRun.GlyphPositions)
            {
                minX = Math.Min(minX, glyphRun.Position.X + position.X);
                minY = Math.Min(minY, glyphRun.Position.Y + position.Y - glyphRun.FontSize);
                maxX = Math.Max(maxX, glyphRun.Position.X + position.X + glyphRun.FontSize);
                maxY = Math.Max(maxY, glyphRun.Position.Y + position.Y);
            }
        }

        return IsUsableRect(TransformBounds(
            new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)),
            glyphRun.Transform),
            out bounds);
    }

    private static bool TryReadFiniteRectProperty(object instance, string propertyName, out Rect bounds)
    {
        bounds = default;
        return TryGetPropertyValue(instance, propertyName, out var boundsValue)
            && boundsValue != null
            && TryReadRect(boundsValue, out var rect)
            && IsUsableRect(rect, out bounds);
    }

    private static Rect UnionBounds(Rect left, Rect right)
    {
        var x1 = Math.Min(left.X, right.X);
        var y1 = Math.Min(left.Y, right.Y);
        var x2 = Math.Max(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Max(left.Y + left.Height, right.Y + right.Height);

        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private static Rect IntersectBounds(Rect left, Rect right)
    {
        var x1 = Math.Max(left.X, right.X);
        var y1 = Math.Max(left.Y, right.Y);
        var x2 = Math.Min(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Min(left.Y + left.Height, right.Y + right.Height);

        return x2 <= x1 || y2 <= y1
            ? Rect.Empty
            : new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private static Rect TransformBounds(Rect bounds, System.Numerics.Matrix4x4 transform)
    {
        if (transform.IsIdentity)
        {
            return bounds;
        }

        var p1 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)bounds.X, (float)bounds.Y), transform);
        var p2 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)(bounds.X + bounds.Width), (float)bounds.Y), transform);
        var p3 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)bounds.X, (float)(bounds.Y + bounds.Height)), transform);
        var p4 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)(bounds.X + bounds.Width), (float)(bounds.Y + bounds.Height)), transform);

        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool IsUsableRect(Rect rect, out Rect bounds)
    {
        bounds = rect;
        return !rect.IsEmpty
            && double.IsFinite(rect.X)
            && double.IsFinite(rect.Y)
            && double.IsFinite(rect.Width)
            && double.IsFinite(rect.Height)
            && rect.Width > 0
            && rect.Height > 0;
    }

    private static WpfReplayRect? ToReplayRect(Rect? bounds)
    {
        return bounds.HasValue ? ToReplayRect(bounds.Value) : null;
    }

    private static WpfReplayRect ToReplayRect(Rect bounds)
    {
        return new WpfReplayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static bool HasNonNullProperty(object instance, string propertyName)
    {
        return TryGetPropertyValue(instance, propertyName, out var value) && value != null;
    }

    private static IReadOnlyList<object> ExtractChildren(object drawingGroup)
    {
        if (!TryGetPropertyValue(drawingGroup, "Children", out var children) || children == null)
        {
            return Array.Empty<object>();
        }

        if (!TryReadIntProperty(children, "Count", out var count) || count <= 0)
        {
            return Array.Empty<object>();
        }

        var getChild = FindIndexer(children.GetType());
        if (getChild == null)
        {
            return Array.Empty<object>();
        }

        var result = new List<object>(count);
        for (var i = 0; i < count; i++)
        {
            var child = getChild(children, i);
            if (child != null)
            {
                result.Add(child);
            }
        }

        return result;
    }

    private static bool TryReadRect(object rectValue, out Rect rectangle)
    {
        if (rectValue is Rect mediaRect)
        {
            rectangle = mediaRect;
            return true;
        }

        if (TryReadDoubleProperty(rectValue, "X", out var x)
            && TryReadDoubleProperty(rectValue, "Y", out var y)
            && TryReadDoubleProperty(rectValue, "Width", out var width)
            && TryReadDoubleProperty(rectValue, "Height", out var height))
        {
            rectangle = new Rect(x, y, width, height);
            return true;
        }

        rectangle = default;
        return false;
    }

    private static bool TryReadIntProperty(object instance, string propertyName, out int value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        if (propertyValue is int intValue)
        {
            value = intValue;
            return true;
        }

        return false;
    }

    private static bool TryReadDoubleProperty(object instance, string propertyName, out double value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        return TryConvertToDouble(propertyValue, out value);
    }

    private static bool TryConvertToDouble(object? value, out double result)
    {
        switch (value)
        {
            case double doubleValue:
                result = doubleValue;
                return true;
            case float floatValue:
                result = floatValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case uint uintValue:
                result = uintValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryGetPropertyValue(object instance, string propertyName, out object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, MemberFlags);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            value = null;
            return false;
        }

        value = property.GetValue(instance);
        return true;
    }

    private static Func<object, int, object?>? FindIndexer(Type type)
    {
        var indexer = type.GetProperty("Item", MemberFlags, binder: null, returnType: null, types: new[] { typeof(int) }, modifiers: null);
        if (indexer != null)
        {
            return (instance, index) => indexer.GetValue(instance, new object[] { index });
        }

        var getter = type.GetMethod("get_Item", MemberFlags, binder: null, types: new[] { typeof(int) }, modifiers: null);
        if (getter != null)
        {
            return (instance, index) => getter.Invoke(instance, new object[] { index });
        }

        return null;
    }

    internal static bool IsTileBrush(object? brush)
    {
        return brush != null
            && (brush is PortableTileBrushSource
                || TypeNameEndsWith(brush, "ImageBrush")
                || TypeNameEndsWith(brush, "DrawingBrush")
                || TypeNameEndsWith(brush, "VisualBrush"));
    }

    private static bool TypeNameEndsWith(object resource, string typeName)
    {
        return resource.GetType().Name.EndsWith(typeName, StringComparison.Ordinal);
    }

    private sealed class DelegateImageSourceAdapter : IWpfImageSourceAdapter
    {
        private readonly Func<object?, MediaImageSource?> _adapter;

        public DelegateImageSourceAdapter(Func<object?, MediaImageSource?> adapter)
        {
            _adapter = adapter;
        }

        public MediaImageSource? AdaptImageSource(object? imageSource)
        {
            return _adapter(imageSource);
        }
    }
}
