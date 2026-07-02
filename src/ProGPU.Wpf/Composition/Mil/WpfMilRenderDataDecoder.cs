using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Media.ProGPU.Composition;
using MediaBrush = System.Windows.Media.Brush;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaLineSegment = System.Windows.Media.LineSegment;
using MediaPathGeometry = System.Windows.Media.PathGeometry;
using MediaPen = System.Windows.Media.Pen;
using MediaRectangleGeometry = System.Windows.Media.RectangleGeometry;
using MediaTransform = System.Windows.Media.Transform;
using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableGeometryPathKind = ProGPU.Wpf.Interop.PortableGeometryPathKind;
using PortableGeometryPathSource = ProGPU.Wpf.Interop.IPortableGeometryPathSource;
using PortablePathSegmentKind = ProGPU.Wpf.Interop.PortablePathSegmentKind;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfMilRenderDataDecoder
{
    private const int RecordHeaderSize = 8;

    public WpfMilDecodeResult Decode(
        ReadOnlySpan<byte> renderData,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver resources)
    {
        return sink is IWpfNativePrimitiveCommandSink nativeSink
            ? DecodeNative(renderData, sink, nativeSink, resources)
            : DecodeTyped(renderData, sink, resources);
    }

    private WpfMilDecodeResult DecodeTyped(
        ReadOnlySpan<byte> renderData,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver resources)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(resources);

        var pushStack = new Stack<bool>();
        var recordCount = 0;
        var appliedCount = 0;
        var skippedCount = 0;
        var unsupportedCount = 0;
        var offset = 0;

        while (offset < renderData.Length)
        {
            if (renderData.Length - offset < RecordHeaderSize)
            {
                throw new InvalidOperationException("Truncated WPF MIL render data record header.");
            }

            var recordSize = ReadInt32(renderData, offset);
            var commandId = (WpfMilCommandId)ReadInt32(renderData, offset + 4);

            if (recordSize < RecordHeaderSize || recordSize % 8 != 0)
            {
                throw new InvalidOperationException($"Invalid WPF MIL render data record size {recordSize} at offset {offset}.");
            }

            if (recordSize > renderData.Length - offset)
            {
                throw new InvalidOperationException($"Truncated WPF MIL render data record at offset {offset}.");
            }

            var payload = renderData.Slice(offset + RecordHeaderSize, recordSize - RecordHeaderSize);
            recordCount++;
            var unsupportedStateBefore = GetUnsupportedStateCount(sink);

            switch (commandId)
            {
                case WpfMilCommandId.DrawLine:
                case WpfMilCommandId.DrawLineAnimate:
                    sink.DrawLine(
                        ResolveOptionalPen(resources, ReadUInt32(payload, 32)),
                        ReadPoint(payload, 0),
                        ReadPoint(payload, 16));
                    appliedCount++;
                    if (commandId == WpfMilCommandId.DrawLineAnimate)
                    {
                        unsupportedCount += CountUnsupportedAnimationHandles(payload, 36, 40);
                    }

                    break;

                case WpfMilCommandId.DrawRectangle:
                case WpfMilCommandId.DrawRectangleAnimate:
                    sink.DrawRectangle(
                        ResolveOptionalBrush(resources, ReadUInt32(payload, 32)),
                        ResolveOptionalPen(resources, ReadUInt32(payload, 36)),
                        ReadRect(payload, 0));
                    appliedCount++;
                    if (commandId == WpfMilCommandId.DrawRectangleAnimate)
                    {
                        unsupportedCount += CountUnsupportedAnimationHandles(payload, 40);
                    }

                    break;

                case WpfMilCommandId.DrawRoundedRectangle:
                case WpfMilCommandId.DrawRoundedRectangleAnimate:
                    sink.DrawRoundedRectangle(
                        ResolveOptionalBrush(resources, ReadUInt32(payload, 48)),
                        ResolveOptionalPen(resources, ReadUInt32(payload, 52)),
                        ReadRect(payload, 0),
                        ReadDouble(payload, 32),
                        ReadDouble(payload, 40));
                    appliedCount++;
                    if (commandId == WpfMilCommandId.DrawRoundedRectangleAnimate)
                    {
                        unsupportedCount += CountUnsupportedAnimationHandles(payload, 56, 60, 64);
                    }

                    break;

                case WpfMilCommandId.DrawEllipse:
                case WpfMilCommandId.DrawEllipseAnimate:
                    sink.DrawEllipse(
                        ResolveOptionalBrush(resources, ReadUInt32(payload, 32)),
                        ResolveOptionalPen(resources, ReadUInt32(payload, 36)),
                        ReadPoint(payload, 0),
                        ReadDouble(payload, 16),
                        ReadDouble(payload, 24));
                    appliedCount++;
                    if (commandId == WpfMilCommandId.DrawEllipseAnimate)
                    {
                        unsupportedCount += CountUnsupportedAnimationHandles(payload, 40, 44, 48);
                    }

                    break;

                case WpfMilCommandId.DrawGeometry:
                    var brush = ResolveOptionalBrush(resources, ReadUInt32(payload, 0));
                    var pen = ResolveOptionalPen(resources, ReadUInt32(payload, 4));
                    var geometryToken = ReadUInt32(payload, 8);
                    if (TryDrawNativeGeometry(resources, sink, brush, pen, geometryToken))
                    {
                        appliedCount++;
                    }
                    else if (TryResolveGeometry(resources, geometryToken, out var geometry))
                    {
                        sink.DrawGeometry(brush, pen, geometry);
                        appliedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.DrawImage:
                case WpfMilCommandId.DrawImageAnimate:
                    if (TryResolveImageSource(resources, ReadUInt32(payload, 32), out var imageSource))
                    {
                        sink.DrawImage(imageSource, ReadRect(payload, 0));
                        appliedCount++;
                        if (commandId == WpfMilCommandId.DrawImageAnimate)
                        {
                            unsupportedCount += CountUnsupportedAnimationHandles(payload, 36);
                        }
                    }
                    else
                    {
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.DrawGlyphRun:
                    if (TryResolveGlyphRun(resources, ReadUInt32(payload, 4), out var glyphRun))
                    {
                        sink.DrawGlyphRun(
                            ResolveOptionalBrush(resources, ReadUInt32(payload, 0)),
                            glyphRun);
                        appliedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.DrawDrawing:
                    switch (ReplayDrawing(resources, ReadUInt32(payload, 0), sink))
                    {
                        case WpfDrawingReplayStatus.Applied:
                            appliedCount++;
                            break;
                        case WpfDrawingReplayStatus.PartiallyApplied:
                            appliedCount++;
                            unsupportedCount++;
                            break;
                        case WpfDrawingReplayStatus.Unsupported:
                            unsupportedCount++;
                            break;
                        default:
                            skippedCount++;
                            break;
                    }
                    break;

                case WpfMilCommandId.PushClip:
                    var clipToken = ReadUInt32(payload, 0);
                    if (clipToken == 0)
                    {
                        sink.PushNoOpScope();
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else if (TryPushClip(resources, sink, clipToken))
                    {
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else
                    {
                        pushStack.Push(false);
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.PushOpacityMask:
                    var opacityMaskToken = ReadUInt32(payload, 16);
                    if (opacityMaskToken == 0)
                    {
                        sink.PushNoOpScope();
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else if (TryResolveBrush(resources, opacityMaskToken, out var opacityMask))
                    {
                        sink.PushOpacityMask(opacityMask, ReadRectF(payload, 0));
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else
                    {
                        pushStack.Push(false);
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.PushOpacity:
                case WpfMilCommandId.PushOpacityAnimate:
                    sink.PushOpacity(ReadDouble(payload, 0));
                    pushStack.Push(true);
                    appliedCount++;
                    if (commandId == WpfMilCommandId.PushOpacityAnimate)
                    {
                        unsupportedCount += CountUnsupportedAnimationHandles(payload, 8);
                    }

                    break;

                case WpfMilCommandId.PushTransform:
                    var transformToken = ReadUInt32(payload, 0);
                    if (transformToken == 0)
                    {
                        sink.PushNoOpScope();
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else if (sink is IWpfNativeTransformCommandSink nativeTransformSink
                        && TryResolveNativeTransform(resources, transformToken, out var nativeTransform))
                    {
                        nativeTransformSink.PushNativeTransform(nativeTransform);
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else if (TryResolveTransform(resources, transformToken, out var transform))
                    {
                        sink.PushTransform(transform);
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else
                    {
                        pushStack.Push(false);
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.PushGuidelineSet:
                    if (TryResolveGuidelineSet(resources, ReadUInt32(payload, 0), out var guidelineSet))
                    {
                        sink.PushGuidelineSet(guidelineSet);
                    }
                    else
                    {
                        sink.PushGuidelineSet();
                    }

                    pushStack.Push(true);
                    appliedCount++;
                    break;

                case WpfMilCommandId.PushGuidelineY1:
                    sink.PushGuidelineY1(ReadDouble(payload, 0));
                    pushStack.Push(true);
                    appliedCount++;
                    break;

                case WpfMilCommandId.PushGuidelineY2:
                    sink.PushGuidelineY2(ReadDouble(payload, 0), ReadDouble(payload, 8));
                    pushStack.Push(true);
                    appliedCount++;
                    break;

                case WpfMilCommandId.Pop:
                    if (pushStack.Count == 0 || pushStack.Pop())
                    {
                        sink.Pop();
                        appliedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.DrawVideo:
                case WpfMilCommandId.DrawVideoAnimate:
                case WpfMilCommandId.PushEffect:
                    if (IsPushCommand(commandId))
                    {
                        pushStack.Push(false);
                    }

                    unsupportedCount++;
                    break;

                default:
                    unsupportedCount++;
                    break;
            }

            var unsupportedStateDelta = GetUnsupportedStateCount(sink) - unsupportedStateBefore;
            if (unsupportedStateDelta > 0)
            {
                unsupportedCount += unsupportedStateDelta;
            }

            offset += recordSize;
        }

        while (pushStack.Count > 0)
        {
            if (pushStack.Pop())
            {
                sink.Pop();
                unsupportedCount++;
            }
        }

        return new WpfMilDecodeResult(recordCount, appliedCount, skippedCount, unsupportedCount);
    }

    private static WpfMilDecodeResult DecodeNative(
        ReadOnlySpan<byte> renderData,
        IWpfCompositionCommandSink sink,
        IWpfNativePrimitiveCommandSink nativeSink,
        IWpfMilResourceResolver resources)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(nativeSink);
        ArgumentNullException.ThrowIfNull(resources);

        var pushStack = new Stack<bool>();
        var recordCount = 0;
        var appliedCount = 0;
        var skippedCount = 0;
        var unsupportedCount = 0;
        var offset = 0;

        while (offset < renderData.Length)
        {
            if (renderData.Length - offset < RecordHeaderSize)
            {
                throw new InvalidOperationException("Truncated WPF MIL render data record header.");
            }

            var recordSize = ReadInt32(renderData, offset);
            var commandId = (WpfMilCommandId)ReadInt32(renderData, offset + 4);

            if (recordSize < RecordHeaderSize || recordSize % 8 != 0)
            {
                throw new InvalidOperationException($"Invalid WPF MIL render data record size {recordSize} at offset {offset}.");
            }

            if (recordSize > renderData.Length - offset)
            {
                throw new InvalidOperationException($"Truncated WPF MIL render data record at offset {offset}.");
            }

            var payload = renderData.Slice(offset + RecordHeaderSize, recordSize - RecordHeaderSize);
            recordCount++;
            var unsupportedStateBefore = GetUnsupportedStateCount(sink);

            switch (commandId)
            {
                case WpfMilCommandId.DrawLine:
                case WpfMilCommandId.DrawLineAnimate:
                    nativeSink.DrawNativeLine(
                        ResolveOptionalPen(resources, ReadUInt32(payload, 32)),
                        ReadReplayPoint(payload, 0),
                        ReadReplayPoint(payload, 16));
                    appliedCount++;
                    if (commandId == WpfMilCommandId.DrawLineAnimate)
                    {
                        unsupportedCount += CountUnsupportedAnimationHandles(payload, 36, 40);
                    }

                    break;

                case WpfMilCommandId.DrawRectangle:
                case WpfMilCommandId.DrawRectangleAnimate:
                    nativeSink.DrawNativeRectangle(
                        ResolveOptionalBrush(resources, ReadUInt32(payload, 32)),
                        ResolveOptionalPen(resources, ReadUInt32(payload, 36)),
                        ReadReplayRect(payload, 0));
                    appliedCount++;
                    if (commandId == WpfMilCommandId.DrawRectangleAnimate)
                    {
                        unsupportedCount += CountUnsupportedAnimationHandles(payload, 40);
                    }

                    break;

                case WpfMilCommandId.DrawRoundedRectangle:
                case WpfMilCommandId.DrawRoundedRectangleAnimate:
                    nativeSink.DrawNativeRoundedRectangle(
                        ResolveOptionalBrush(resources, ReadUInt32(payload, 48)),
                        ResolveOptionalPen(resources, ReadUInt32(payload, 52)),
                        ReadReplayRect(payload, 0),
                        ReadDouble(payload, 32),
                        ReadDouble(payload, 40));
                    appliedCount++;
                    if (commandId == WpfMilCommandId.DrawRoundedRectangleAnimate)
                    {
                        unsupportedCount += CountUnsupportedAnimationHandles(payload, 56, 60, 64);
                    }

                    break;

                case WpfMilCommandId.DrawEllipse:
                case WpfMilCommandId.DrawEllipseAnimate:
                    nativeSink.DrawNativeEllipse(
                        ResolveOptionalBrush(resources, ReadUInt32(payload, 32)),
                        ResolveOptionalPen(resources, ReadUInt32(payload, 36)),
                        ReadReplayPoint(payload, 0),
                        ReadDouble(payload, 16),
                        ReadDouble(payload, 24));
                    appliedCount++;
                    if (commandId == WpfMilCommandId.DrawEllipseAnimate)
                    {
                        unsupportedCount += CountUnsupportedAnimationHandles(payload, 40, 44, 48);
                    }

                    break;

                case WpfMilCommandId.DrawGeometry:
                    var nativeBrush = ResolveOptionalBrush(resources, ReadUInt32(payload, 0));
                    var nativePen = ResolveOptionalPen(resources, ReadUInt32(payload, 4));
                    var nativeGeometryToken = ReadUInt32(payload, 8);
                    if (TryDrawNativeGeometry(resources, sink, nativeBrush, nativePen, nativeGeometryToken))
                    {
                        appliedCount++;
                    }
                    else if (TryResolveGeometry(resources, nativeGeometryToken, out var geometry))
                    {
                        sink.DrawGeometry(nativeBrush, nativePen, geometry);
                        appliedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.DrawImage:
                case WpfMilCommandId.DrawImageAnimate:
                    if (TryResolveImageSource(resources, ReadUInt32(payload, 32), out var imageSource))
                    {
                        nativeSink.DrawNativeImage(imageSource, ReadReplayRect(payload, 0));
                        appliedCount++;
                        if (commandId == WpfMilCommandId.DrawImageAnimate)
                        {
                            unsupportedCount += CountUnsupportedAnimationHandles(payload, 36);
                        }
                    }
                    else
                    {
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.DrawGlyphRun:
                    if (TryResolveRawResource(resources, ReadUInt32(payload, 4), out var glyphRun))
                    {
                        nativeSink.DrawNativeGlyphRun(
                            ResolveOptionalBrush(resources, ReadUInt32(payload, 0)),
                            glyphRun);
                        appliedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.DrawDrawing:
                    switch (ReplayDrawing(resources, ReadUInt32(payload, 0), sink))
                    {
                        case WpfDrawingReplayStatus.Applied:
                            appliedCount++;
                            break;
                        case WpfDrawingReplayStatus.PartiallyApplied:
                            appliedCount++;
                            unsupportedCount++;
                            break;
                        case WpfDrawingReplayStatus.Unsupported:
                            unsupportedCount++;
                            break;
                        default:
                            skippedCount++;
                            break;
                    }
                    break;

                case WpfMilCommandId.PushClip:
                    var clipToken = ReadUInt32(payload, 0);
                    if (clipToken == 0)
                    {
                        sink.PushNoOpScope();
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else if (TryPushClip(resources, sink, clipToken))
                    {
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else
                    {
                        pushStack.Push(false);
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.PushOpacityMask:
                    var opacityMaskToken = ReadUInt32(payload, 16);
                    if (opacityMaskToken == 0)
                    {
                        sink.PushNoOpScope();
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else if (TryResolveBrush(resources, opacityMaskToken, out var opacityMask))
                    {
                        nativeSink.PushNativeOpacityMask(opacityMask, ReadReplayRectF(payload, 0));
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else
                    {
                        pushStack.Push(false);
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.PushOpacity:
                case WpfMilCommandId.PushOpacityAnimate:
                    sink.PushOpacity(ReadDouble(payload, 0));
                    pushStack.Push(true);
                    appliedCount++;
                    if (commandId == WpfMilCommandId.PushOpacityAnimate)
                    {
                        unsupportedCount += CountUnsupportedAnimationHandles(payload, 8);
                    }

                    break;

                case WpfMilCommandId.PushTransform:
                    var transformToken = ReadUInt32(payload, 0);
                    if (transformToken == 0)
                    {
                        sink.PushNoOpScope();
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else if (sink is IWpfNativeTransformCommandSink nativeTransformSink
                        && TryResolveNativeTransform(resources, transformToken, out var nativeTransform))
                    {
                        nativeTransformSink.PushNativeTransform(nativeTransform);
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else if (TryResolveTransform(resources, transformToken, out var transform))
                    {
                        sink.PushTransform(transform);
                        pushStack.Push(true);
                        appliedCount++;
                    }
                    else
                    {
                        pushStack.Push(false);
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.PushGuidelineSet:
                    if (TryResolveGuidelineSet(resources, ReadUInt32(payload, 0), out var guidelineSet))
                    {
                        sink.PushGuidelineSet(guidelineSet);
                    }
                    else
                    {
                        sink.PushGuidelineSet();
                    }

                    pushStack.Push(true);
                    appliedCount++;
                    break;

                case WpfMilCommandId.PushGuidelineY1:
                    sink.PushGuidelineY1(ReadDouble(payload, 0));
                    pushStack.Push(true);
                    appliedCount++;
                    break;

                case WpfMilCommandId.PushGuidelineY2:
                    sink.PushGuidelineY2(ReadDouble(payload, 0), ReadDouble(payload, 8));
                    pushStack.Push(true);
                    appliedCount++;
                    break;

                case WpfMilCommandId.Pop:
                    if (pushStack.Count == 0 || pushStack.Pop())
                    {
                        sink.Pop();
                        appliedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.DrawVideo:
                case WpfMilCommandId.DrawVideoAnimate:
                case WpfMilCommandId.PushEffect:
                    if (IsPushCommand(commandId))
                    {
                        pushStack.Push(false);
                    }

                    unsupportedCount++;
                    break;

                default:
                    unsupportedCount++;
                    break;
            }

            var unsupportedStateDelta = GetUnsupportedStateCount(sink) - unsupportedStateBefore;
            if (unsupportedStateDelta > 0)
            {
                unsupportedCount += unsupportedStateDelta;
            }

            offset += recordSize;
        }

        while (pushStack.Count > 0)
        {
            if (pushStack.Pop())
            {
                sink.Pop();
                unsupportedCount++;
            }
        }

        return new WpfMilDecodeResult(recordCount, appliedCount, skippedCount, unsupportedCount);
    }

    private static int GetUnsupportedStateCount(IWpfCompositionCommandSink sink)
    {
        return sink is IWpfCompositionCommandSinkDiagnostics diagnostics
            ? diagnostics.UnsupportedStateCount
            : 0;
    }

    private static bool IsPushCommand(WpfMilCommandId commandId)
    {
        return commandId is WpfMilCommandId.PushEffect;
    }

    private static int CountUnsupportedAnimationHandles(ReadOnlySpan<byte> payload, params int[] offsets)
    {
        var count = 0;
        foreach (var offset in offsets)
        {
            if (ReadUInt32(payload, offset) != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static MediaBrush? ResolveOptionalBrush(IWpfMilResourceResolver resources, uint resourceToken)
    {
        return resourceToken == 0 ? null : resources.ResolveBrush(resourceToken);
    }

    private static MediaPen? ResolveOptionalPen(IWpfMilResourceResolver resources, uint resourceToken)
    {
        return resourceToken == 0 ? null : resources.ResolvePen(resourceToken);
    }

    private static bool TryResolveBrush(IWpfMilResourceResolver resources, uint resourceToken, out MediaBrush? brush)
    {
        brush = resourceToken == 0 ? null : resources.ResolveBrush(resourceToken);
        return brush != null;
    }

    private static bool TryResolveGeometry(IWpfMilResourceResolver resources, uint resourceToken, out MediaGeometry geometry)
    {
        geometry = resourceToken == 0 ? null! : resources.ResolveGeometry(resourceToken)!;
        return geometry != null;
    }

    private static bool TryResolvePortableGeometryPath(
        IWpfMilResourceResolver resources,
        uint resourceToken,
        out PortableGeometryPath geometry)
    {
        geometry = null!;
        return TryResolveRawResource(resources, resourceToken, out var resource)
            && resource is PortableGeometryPathSource portableGeometry
            && portableGeometry.TryGetPortableGeometryPath(out geometry)
            && geometry != null;
    }

    private static bool TryDrawNativeGeometry(
        IWpfMilResourceResolver resources,
        IWpfCompositionCommandSink sink,
        MediaBrush? brush,
        MediaPen? pen,
        uint geometryToken)
    {
        return sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && TryResolvePortableGeometryPath(resources, geometryToken, out var geometry)
            && nativeGeometrySink.DrawNativeGeometry(brush, pen, geometry);
    }

    private static bool TryResolveImageSource(IWpfMilResourceResolver resources, uint resourceToken, out MediaImageSource imageSource)
    {
        imageSource = resourceToken == 0 ? null! : resources.ResolveImageSource(resourceToken)!;
        return imageSource != null;
    }

    private static bool TryResolveGlyphRun(IWpfMilResourceResolver resources, uint resourceToken, out MediaGlyphRun glyphRun)
    {
        glyphRun = resourceToken == 0 ? null! : resources.ResolveGlyphRun(resourceToken)!;
        return glyphRun != null;
    }

    private static bool TryResolveRawResource(IWpfMilResourceResolver resources, uint resourceToken, out object resource)
    {
        resource = null!;
        return resourceToken != 0
            && resources is IWpfRawMilResourceResolver rawResources
            && rawResources.TryResolveRawResource(resourceToken, out resource);
    }

    private static bool TryResolveTransform(IWpfMilResourceResolver resources, uint resourceToken, out MediaTransform transform)
    {
        transform = resourceToken == 0 ? null! : resources.ResolveTransform(resourceToken)!;
        return transform != null;
    }

    private static bool TryResolveNativeTransform(
        IWpfMilResourceResolver resources,
        uint resourceToken,
        out Matrix4x4 transform)
    {
        if (TryResolveRawResource(resources, resourceToken, out var resource)
            && WpfResourceResolver.TryAdaptTransformMatrix(resource, out transform))
        {
            return true;
        }

        transform = Matrix4x4.Identity;
        return false;
    }

    private static bool TryResolveGuidelineSet(IWpfMilResourceResolver resources, uint resourceToken, out object guidelineSet)
    {
        guidelineSet = null!;
        if (resourceToken == 0 || resources is not IWpfGuidelineSetResourceResolver guidelineSetResources)
        {
            return false;
        }

        guidelineSet = guidelineSetResources.ResolveGuidelineSet(resourceToken)!;
        return guidelineSet != null;
    }

    private static bool TryPushClip(
        IWpfMilResourceResolver resources,
        IWpfCompositionCommandSink sink,
        uint clipToken)
    {
        if (TryResolvePortableGeometryPath(resources, clipToken, out var portableClip))
        {
            if (sink is IWpfNativeClipCommandSink nativePortableClipSink
                && TryGetRectangleClipBounds(portableClip, out var portableClipBounds))
            {
                nativePortableClipSink.PushNativeClip(portableClipBounds);
                return true;
            }

            if (sink is IWpfNativeGeometryCommandSink nativeGeometrySink
                && nativeGeometrySink.PushNativeGeometryClip(portableClip))
            {
                return true;
            }
        }

        if (!TryResolveGeometry(resources, clipToken, out var clipGeometry))
        {
            return false;
        }

        if (sink is IWpfNativeClipCommandSink nativeClipSink
            && TryGetRectangleClipBounds(clipGeometry, out var clipBounds))
        {
            nativeClipSink.PushNativeClip(clipBounds);
            return true;
        }

        sink.PushClip(clipGeometry);
        return true;
    }

    private static bool TryGetRectangleClipBounds(PortableGeometryPath geometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (!geometry.Transform.IsIdentity
            || geometry.Kind != PortableGeometryPathKind.Path
            || geometry.Figures.Length != 1)
        {
            return false;
        }

        var figure = geometry.Figures[0];
        if (!figure.IsClosed || !figure.IsFilled)
        {
            return false;
        }

        var segmentCount = figure.Segments.Length;
        if (segmentCount is not (3 or 4))
        {
            return false;
        }

        var points = new Point[4];
        points[0] = new Point(figure.StartPoint.X, figure.StartPoint.Y);
        for (var i = 0; i < 3; i++)
        {
            var segment = figure.Segments[i];
            if (segment.Kind != PortablePathSegmentKind.Line)
            {
                return false;
            }

            points[i + 1] = new Point(segment.Point1.X, segment.Point1.Y);
        }

        if (segmentCount == 4)
        {
            var segment = figure.Segments[3];
            if (segment.Kind != PortablePathSegmentKind.Line
                || !NearlyEqual(segment.Point1.X, points[0].X)
                || !NearlyEqual(segment.Point1.Y, points[0].Y))
            {
                return false;
            }
        }

        return TryCreateRectangleFromPolygon(points, out bounds);
    }

    private static bool TryGetRectangleClipBounds(MediaGeometry geometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (!HasIdentityGeometryTransform(geometry))
        {
            return false;
        }

        if (geometry is MediaRectangleGeometry rectangleGeometry)
        {
            return TryCreateUsableRect(rectangleGeometry.Rect, out bounds);
        }

        return geometry is MediaPathGeometry pathGeometry
            && TryGetRectanglePathBounds(pathGeometry, out bounds);
    }

    private static bool HasIdentityGeometryTransform(MediaGeometry geometry)
    {
        var transform = geometry.Transform;
        return transform == null
            || (WpfResourceResolver.TryAdaptTransformMatrix(transform, out var matrix)
                && WpfResourceResolver.IsIdentityMatrix(matrix));
    }

    private static bool TryGetRectanglePathBounds(MediaPathGeometry pathGeometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (pathGeometry.Figures.Count != 1)
        {
            return false;
        }

        var figure = pathGeometry.Figures[0];
        if (!figure.IsClosed || !figure.IsFilled)
        {
            return false;
        }

        var segmentCount = figure.Segments.Count;
        if (segmentCount is not (3 or 4))
        {
            return false;
        }

        var points = new Point[4];
        points[0] = figure.StartPoint;
        for (var i = 0; i < 3; i++)
        {
            if (figure.Segments[i] is not MediaLineSegment lineSegment)
            {
                return false;
            }

            points[i + 1] = lineSegment.Point;
        }

        if (segmentCount == 4)
        {
            if (figure.Segments[3] is not MediaLineSegment closingSegment
                || !NearlyEqual(closingSegment.Point.X, points[0].X)
                || !NearlyEqual(closingSegment.Point.Y, points[0].Y))
            {
                return false;
            }
        }

        return TryCreateRectangleFromPolygon(points, out bounds);
    }

    private static bool TryCreateRectangleFromPolygon(Point[] points, out WpfReplayRect bounds)
    {
        bounds = default;
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        var width = right - left;
        var height = bottom - top;
        if (!IsFinite(left)
            || !IsFinite(top)
            || !IsFinite(width)
            || !IsFinite(height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            var isOnVerticalEdge = NearlyEqual(point.X, left) || NearlyEqual(point.X, right);
            var isOnHorizontalEdge = NearlyEqual(point.Y, top) || NearlyEqual(point.Y, bottom);
            if (!isOnVerticalEdge || !isOnHorizontalEdge)
            {
                return false;
            }

            var next = points[(i + 1) % points.Length];
            var sameX = NearlyEqual(point.X, next.X);
            var sameY = NearlyEqual(point.Y, next.Y);
            if (sameX == sameY)
            {
                return false;
            }
        }

        bounds = new WpfReplayRect(left, top, width, height);
        return true;
    }

    private static bool TryCreateUsableRect(Rect rect, out WpfReplayRect bounds)
    {
        bounds = new WpfReplayRect(rect.X, rect.Y, rect.Width, rect.Height);
        return IsFinite(bounds.X)
            && IsFinite(bounds.Y)
            && IsFinite(bounds.Width)
            && IsFinite(bounds.Height)
            && bounds.Width > 0
            && bounds.Height > 0;
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= 0.000001;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static WpfDrawingReplayStatus ReplayDrawing(
        IWpfMilResourceResolver resources,
        uint resourceToken,
        IWpfCompositionCommandSink sink)
    {
        return resources is IWpfDrawingResourceResolver drawingResources
            ? drawingResources.ReplayDrawing(resourceToken, sink)
            : WpfDrawingReplayStatus.Skipped;
    }

    private static Point ReadPoint(ReadOnlySpan<byte> payload, int offset)
    {
        return new Point(ReadDouble(payload, offset), ReadDouble(payload, offset + 8));
    }

    private static Rect ReadRect(ReadOnlySpan<byte> payload, int offset)
    {
        return new Rect(
            ReadDouble(payload, offset),
            ReadDouble(payload, offset + 8),
            ReadDouble(payload, offset + 16),
            ReadDouble(payload, offset + 24));
    }

    private static Rect ReadRectF(ReadOnlySpan<byte> payload, int offset)
    {
        return new Rect(
            ReadSingle(payload, offset),
            ReadSingle(payload, offset + 4),
            ReadSingle(payload, offset + 8),
            ReadSingle(payload, offset + 12));
    }

    private static WpfReplayPoint ReadReplayPoint(ReadOnlySpan<byte> payload, int offset)
    {
        return new WpfReplayPoint(
            ReadDouble(payload, offset),
            ReadDouble(payload, offset + 8));
    }

    private static WpfReplayRect ReadReplayRect(ReadOnlySpan<byte> payload, int offset)
    {
        return new WpfReplayRect(
            ReadDouble(payload, offset),
            ReadDouble(payload, offset + 8),
            ReadDouble(payload, offset + 16),
            ReadDouble(payload, offset + 24));
    }

    private static WpfReplayRect ReadReplayRectF(ReadOnlySpan<byte> payload, int offset)
    {
        return new WpfReplayRect(
            ReadSingle(payload, offset),
            ReadSingle(payload, offset + 4),
            ReadSingle(payload, offset + 8),
            ReadSingle(payload, offset + 12));
    }

    private static int ReadInt32(ReadOnlySpan<byte> payload, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, sizeof(int)));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> payload, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, sizeof(uint)));
    }

    private static float ReadSingle(ReadOnlySpan<byte> payload, int offset)
    {
        return BitConverter.Int32BitsToSingle(ReadInt32(payload, offset));
    }

    private static double ReadDouble(ReadOnlySpan<byte> payload, int offset)
    {
        return BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(offset, sizeof(long))));
    }
}
