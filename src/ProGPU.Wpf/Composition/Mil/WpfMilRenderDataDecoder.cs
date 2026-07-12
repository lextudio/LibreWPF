using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Windows;
using System.Windows.Media.ProGPU.Composition;
using MediaBrush = System.Windows.Media.Brush;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaRectangleGeometry = System.Windows.Media.RectangleGeometry;
using MediaTransform = System.Windows.Media.Transform;
using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableGeometryPathSource = ProGPU.Wpf.Interop.IPortableGeometryPathSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfMilRenderDataDecoder
{
    private const int RecordHeaderSize = 8;
    private const int InitialPushStackCapacity = 32;

    private ref struct DecodePushStack
    {
        private Span<bool> _items;
        private bool[]? _rentedItems;
        private int _count;

        public DecodePushStack(Span<bool> initialItems)
        {
            _items = initialItems;
            _rentedItems = null;
            _count = 0;
        }

        public readonly int Count => _count;

        public void Push(bool value)
        {
            if (_count >= _items.Length)
            {
                Grow();
            }

            _items[_count++] = value;
        }

        public bool Pop()
        {
            return _items[--_count];
        }

        public void Dispose()
        {
            if (_rentedItems != null)
            {
                ArrayPool<bool>.Shared.Return(_rentedItems, clearArray: true);
                _rentedItems = null;
            }

            _items = Span<bool>.Empty;
            _count = 0;
        }

        private void Grow()
        {
            var newSize = _items.Length == 0 ? InitialPushStackCapacity : _items.Length * 2;
            var rented = ArrayPool<bool>.Shared.Rent(newSize);
            _items.Slice(0, _count).CopyTo(rented);

            var previousRented = _rentedItems;
            _items = rented;
            _rentedItems = rented;

            if (previousRented != null)
            {
                ArrayPool<bool>.Shared.Return(previousRented, clearArray: true);
            }
        }
    }

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

        Span<bool> initialPushStack = stackalloc bool[InitialPushStackCapacity];
        using var pushStack = new DecodePushStack(initialPushStack);
        var diagnostics = sink as IWpfCompositionCommandSinkDiagnostics;
        var nativeTransformSink = sink as IWpfNativeTransformCommandSink;
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
            var unsupportedStateBefore = GetUnsupportedStateCount(diagnostics);

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
                    var rectangle = ReadRect(payload, 0);
                    var rectangleBrushToken = ReadUInt32(payload, 32);
                    var rectanglePen = ResolveOptionalPen(resources, ReadUInt32(payload, 36));
                    if (TryReplayTileBrushFill(resources, rectangleBrushToken, rectangle, sink, out var rectangleBrushStatus))
                    {
                        if (rectanglePen != null)
                        {
                            sink.DrawRectangle(null, rectanglePen, rectangle);
                        }

                        unsupportedCount += rectangleBrushStatus == WpfDrawingReplayStatus.PartiallyApplied ? 1 : 0;
                    }
                    else
                    {
                        sink.DrawRectangle(
                            ResolveOptionalBrush(resources, rectangleBrushToken),
                            rectanglePen,
                            rectangle);
                    }

                    appliedCount++;
                    if (commandId == WpfMilCommandId.DrawRectangleAnimate)
                    {
                        unsupportedCount += CountUnsupportedAnimationHandles(payload, 40);
                    }

                    break;

                case WpfMilCommandId.DrawRoundedRectangle:
                case WpfMilCommandId.DrawRoundedRectangleAnimate:
                    var roundedRectangle = ReadRect(payload, 0);
                    var radiusX = ReadDouble(payload, 32);
                    var radiusY = ReadDouble(payload, 40);
                    var roundedBrushToken = ReadUInt32(payload, 48);
                    var roundedPen = ResolveOptionalPen(resources, ReadUInt32(payload, 52));
                    if (TryReplayTileBrushPrimitiveFill(resources, roundedBrushToken, new MediaRectangleGeometry(roundedRectangle, radiusX, radiusY), sink, out var roundedBrushStatus))
                    {
                        if (roundedPen != null)
                        {
                            sink.DrawRoundedRectangle(null, roundedPen, roundedRectangle, radiusX, radiusY);
                        }

                        unsupportedCount += roundedBrushStatus == WpfDrawingReplayStatus.PartiallyApplied ? 1 : 0;
                    }
                    else
                    {
                        sink.DrawRoundedRectangle(ResolveOptionalBrush(resources, roundedBrushToken), roundedPen, roundedRectangle, radiusX, radiusY);
                    }
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
                    var brushToken = ReadUInt32(payload, 0);
                    var brush = ResolveOptionalBrush(resources, brushToken);
                    var pen = ResolveOptionalPen(resources, ReadUInt32(payload, 4));
                    var geometryToken = ReadUInt32(payload, 8);
                    if (TryReplayTileBrushGeometryFill(resources, brushToken, geometryToken, sink, out var geometryBrushStatus))
                    {
                        if (pen != null && TryResolveGeometry(resources, geometryToken, out var outlineGeometry))
                        {
                            DrawMediaGeometry(sink, null, pen, outlineGeometry);
                        }

                        appliedCount++;
                        unsupportedCount += geometryBrushStatus == WpfDrawingReplayStatus.PartiallyApplied ? 1 : 0;
                    }
                    else if (TryDrawNativeGeometry(resources, sink, brush, pen, geometryToken))
                    {
                        appliedCount++;
                    }
                    else if (TryResolveGeometry(resources, geometryToken, out var geometry))
                    {
                        if (!TryDrawPrimitiveGeometry(sink, brush, pen, geometry))
                        {
                            DrawMediaGeometry(sink, brush, pen, geometry);
                        }

                        appliedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.DrawImage:
                case WpfMilCommandId.DrawImageAnimate:
                    var imageRectangle = ReadRect(payload, 0);
                    var imageToken = ReadUInt32(payload, 32);
                    if (TryReplayDrawingImage(resources, imageToken, imageRectangle, sink, out var drawingImageStatus))
                    {
                        appliedCount++;
                        unsupportedCount += drawingImageStatus == WpfDrawingReplayStatus.PartiallyApplied ? 1 : 0;
                        if (commandId == WpfMilCommandId.DrawImageAnimate)
                        {
                            unsupportedCount += CountUnsupportedAnimationHandles(payload, 36);
                        }
                    }
                    else if (TryResolveImageSource(resources, imageToken, out var imageSource))
                    {
                        sink.DrawImage(imageSource, imageRectangle);
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
                    else if (nativeTransformSink != null
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

            var unsupportedStateDelta = GetUnsupportedStateCount(diagnostics) - unsupportedStateBefore;
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

        Span<bool> initialPushStack = stackalloc bool[InitialPushStackCapacity];
        using var pushStack = new DecodePushStack(initialPushStack);
        var diagnostics = sink as IWpfCompositionCommandSinkDiagnostics;
        var nativeTransformSink = sink as IWpfNativeTransformCommandSink;
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
            var unsupportedStateBefore = GetUnsupportedStateCount(diagnostics);

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
                    var nativeRectangle = ReadRect(payload, 0);
                    var nativeRectangleBrushToken = ReadUInt32(payload, 32);
                    var nativeRectanglePen = ResolveOptionalPen(resources, ReadUInt32(payload, 36));
                    if (TryReplayTileBrushFill(resources, nativeRectangleBrushToken, nativeRectangle, sink, out var nativeRectangleBrushStatus))
                    {
                        if (nativeRectanglePen != null)
                        {
                            nativeSink.DrawNativeRectangle(null, nativeRectanglePen, ReadReplayRect(payload, 0));
                        }

                        unsupportedCount += nativeRectangleBrushStatus == WpfDrawingReplayStatus.PartiallyApplied ? 1 : 0;
                    }
                    else
                    {
                        nativeSink.DrawNativeRectangle(
                            ResolveOptionalBrush(resources, nativeRectangleBrushToken),
                            nativeRectanglePen,
                            ReadReplayRect(payload, 0));
                    }

                    appliedCount++;
                    if (commandId == WpfMilCommandId.DrawRectangleAnimate)
                    {
                        unsupportedCount += CountUnsupportedAnimationHandles(payload, 40);
                    }

                    break;

                case WpfMilCommandId.DrawRoundedRectangle:
                case WpfMilCommandId.DrawRoundedRectangleAnimate:
                    var nativeRoundedRectangle = ReadRect(payload, 0);
                    var nativeRadiusX = ReadDouble(payload, 32);
                    var nativeRadiusY = ReadDouble(payload, 40);
                    var nativeRoundedBrushToken = ReadUInt32(payload, 48);
                    var nativeRoundedPen = ResolveOptionalPen(resources, ReadUInt32(payload, 52));
                    if (TryReplayTileBrushPrimitiveFill(resources, nativeRoundedBrushToken, new MediaRectangleGeometry(nativeRoundedRectangle, nativeRadiusX, nativeRadiusY), sink, out var nativeRoundedBrushStatus))
                    {
                        if (nativeRoundedPen != null)
                        {
                            nativeSink.DrawNativeRoundedRectangle(null, nativeRoundedPen, ReadReplayRect(payload, 0), nativeRadiusX, nativeRadiusY);
                        }

                        unsupportedCount += nativeRoundedBrushStatus == WpfDrawingReplayStatus.PartiallyApplied ? 1 : 0;
                    }
                    else
                    {
                        nativeSink.DrawNativeRoundedRectangle(ResolveOptionalBrush(resources, nativeRoundedBrushToken), nativeRoundedPen, ReadReplayRect(payload, 0), nativeRadiusX, nativeRadiusY);
                    }
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
                    var nativeBrushToken = ReadUInt32(payload, 0);
                    var nativeBrush = ResolveOptionalBrush(resources, nativeBrushToken);
                    var nativePen = ResolveOptionalPen(resources, ReadUInt32(payload, 4));
                    var nativeGeometryToken = ReadUInt32(payload, 8);
                    if (TryReplayTileBrushGeometryFill(resources, nativeBrushToken, nativeGeometryToken, sink, out var nativeGeometryBrushStatus))
                    {
                        if (nativePen != null && TryResolveGeometry(resources, nativeGeometryToken, out var nativeOutlineGeometry))
                        {
                            DrawMediaGeometry(sink, null, nativePen, nativeOutlineGeometry);
                        }

                        appliedCount++;
                        unsupportedCount += nativeGeometryBrushStatus == WpfDrawingReplayStatus.PartiallyApplied ? 1 : 0;
                    }
                    else if (TryDrawNativeGeometry(resources, sink, nativeBrush, nativePen, nativeGeometryToken))
                    {
                        appliedCount++;
                    }
                    else if (TryResolveGeometry(resources, nativeGeometryToken, out var geometry))
                    {
                        if (!TryDrawPrimitiveGeometry(sink, nativeBrush, nativePen, geometry))
                        {
                            DrawMediaGeometry(sink, nativeBrush, nativePen, geometry);
                        }

                        appliedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                    break;

                case WpfMilCommandId.DrawImage:
                case WpfMilCommandId.DrawImageAnimate:
                    var nativeImageRectangle = ReadRect(payload, 0);
                    var nativeImageToken = ReadUInt32(payload, 32);
                    if (TryReplayDrawingImage(resources, nativeImageToken, nativeImageRectangle, sink, out var nativeDrawingImageStatus))
                    {
                        appliedCount++;
                        unsupportedCount += nativeDrawingImageStatus == WpfDrawingReplayStatus.PartiallyApplied ? 1 : 0;
                        if (commandId == WpfMilCommandId.DrawImageAnimate)
                        {
                            unsupportedCount += CountUnsupportedAnimationHandles(payload, 36);
                        }
                    }
                    else if (TryResolveImageSource(resources, nativeImageToken, out var imageSource))
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
                    else if (nativeTransformSink != null
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

            var unsupportedStateDelta = GetUnsupportedStateCount(diagnostics) - unsupportedStateBefore;
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

    private static int GetUnsupportedStateCount(IWpfCompositionCommandSinkDiagnostics? diagnostics)
    {
        return diagnostics?.UnsupportedStateCount ?? 0;
    }

    private static bool IsPushCommand(WpfMilCommandId commandId)
    {
        return commandId is WpfMilCommandId.PushEffect;
    }

    private static int CountUnsupportedAnimationHandles(ReadOnlySpan<byte> payload, int offset)
    {
        return ReadUInt32(payload, offset) != 0 ? 1 : 0;
    }

    private static int CountUnsupportedAnimationHandles(ReadOnlySpan<byte> payload, int offset0, int offset1)
    {
        return CountUnsupportedAnimationHandles(payload, offset0)
            + CountUnsupportedAnimationHandles(payload, offset1);
    }

    private static int CountUnsupportedAnimationHandles(ReadOnlySpan<byte> payload, int offset0, int offset1, int offset2)
    {
        return CountUnsupportedAnimationHandles(payload, offset0)
            + CountUnsupportedAnimationHandles(payload, offset1)
            + CountUnsupportedAnimationHandles(payload, offset2);
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

    private static bool TryReplayDrawingImage(
        IWpfMilResourceResolver resources,
        uint resourceToken,
        Rect rectangle,
        IWpfCompositionCommandSink sink,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        return TryResolveRawResource(resources, resourceToken, out var resource)
            && WpfDrawingReplay.TryReplayDrawingImage(resource, rectangle, sink, null, out status);
    }

    private static bool TryReplayTileBrushFill(
        IWpfMilResourceResolver resources,
        uint brushToken,
        Rect rectangle,
        IWpfCompositionCommandSink sink,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        return TryResolveRawResource(resources, brushToken, out var brush)
            && WpfDrawingReplay.IsTileBrush(brush)
            && WpfDrawingReplay.TryReplayTileBrushFill(brush, rectangle, sink, null, out status);
    }

    private static bool TryReplayTileBrushGeometryFill(
        IWpfMilResourceResolver resources,
        uint brushToken,
        uint geometryToken,
        IWpfCompositionCommandSink sink,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        return TryResolveRawResource(resources, brushToken, out var brush)
            && WpfDrawingReplay.IsTileBrush(brush)
            && TryResolveGeometry(resources, geometryToken, out var geometry)
            && WpfDrawingReplay.TryReplayTileBrushFill(brush, geometry, sink, null, out status);
    }

    private static bool TryReplayTileBrushPrimitiveFill(
        IWpfMilResourceResolver resources,
        uint brushToken,
        MediaGeometry geometry,
        IWpfCompositionCommandSink sink,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        return TryResolveRawResource(resources, brushToken, out var brush)
            && WpfDrawingReplay.IsTileBrush(brush)
            && WpfDrawingReplay.TryReplayTileBrushFill(brush, geometry, sink, null, out status);
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

        if (sink is IWpfNativeGeometryCommandSink nativeMediaGeometrySink
            && nativeMediaGeometrySink.PushNativeGeometryClip(clipGeometry))
        {
            return true;
        }

        sink.PushClip(clipGeometry);
        return true;
    }

    private static bool TryGetRectangleClipBounds(PortableGeometryPath geometry, out WpfReplayRect bounds)
    {
        return WpfPortableRectangleClipReader.TryGetRectangleClipBounds(geometry, out bounds);
    }

    private static bool TryGetRectangleClipBounds(MediaGeometry geometry, out WpfReplayRect bounds)
    {
        return WpfMediaRectangleClipReader.TryGetRectangleClipBounds(geometry, out bounds);
    }

    private static bool TryDrawPrimitiveGeometry(
        IWpfCompositionCommandSink sink,
        MediaBrush? brush,
        MediaPen? pen,
        MediaGeometry geometry)
    {
        return TryDrawPrimitiveLineGeometry(sink, pen, geometry)
            || TryDrawPrimitivePolylineGeometry(sink, brush, pen, geometry)
            || TryDrawPrimitiveRectangleGeometry(sink, brush, pen, geometry)
            || TryDrawPrimitiveEllipseGeometry(sink, brush, pen, geometry);
    }

    private static void DrawMediaGeometry(
        IWpfCompositionCommandSink sink,
        MediaBrush? brush,
        MediaPen? pen,
        MediaGeometry geometry)
    {
        if (sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && nativeGeometrySink.DrawNativeGeometry(brush, pen, geometry))
        {
            return;
        }

        sink.DrawGeometry(brush, pen, geometry);
    }

    private static bool TryDrawPrimitiveLineGeometry(
        IWpfCompositionCommandSink sink,
        MediaPen? pen,
        MediaGeometry geometry)
    {
        if (pen == null
            || !WpfMediaLineGeometryReader.TryGetLinePoints(geometry, out var startPoint, out var endPoint))
        {
            return false;
        }

        if (sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            nativeSink.DrawNativeLine(
                pen,
                new WpfReplayPoint(startPoint.X, startPoint.Y),
                new WpfReplayPoint(endPoint.X, endPoint.Y));
        }
        else
        {
            sink.DrawLine(pen, startPoint, endPoint);
        }

        return true;
    }

    private static bool TryDrawPrimitivePolylineGeometry(
        IWpfCompositionCommandSink sink,
        MediaBrush? brush,
        MediaPen? pen,
        MediaGeometry geometry)
    {
        if (brush != null
            || pen == null
            || !WpfMediaLineGeometryReader.TryGetPolylineSegments(geometry, out var segments))
        {
            return false;
        }

        if (sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                nativeSink.DrawNativeLine(pen, segment.StartPoint, segment.EndPoint);
            }
        }
        else
        {
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                sink.DrawLine(
                    pen,
                    new Point(segment.StartPoint.X, segment.StartPoint.Y),
                    new Point(segment.EndPoint.X, segment.EndPoint.Y));
            }
        }

        return true;
    }

    private static bool TryDrawPrimitiveRectangleGeometry(
        IWpfCompositionCommandSink sink,
        MediaBrush? brush,
        MediaPen? pen,
        MediaGeometry geometry)
    {
        if (!TryGetPrimitiveRectangleGeometry(geometry, out var rectangle, out var radiusX, out var radiusY))
        {
            if (brush != null
                || pen == null
                || !WpfMediaRectangleClipReader.TryGetRectangleStrokeBounds(geometry, out var rectangleBounds))
            {
                return false;
            }

            rectangle = ToRect(rectangleBounds);
            radiusX = 0;
            radiusY = 0;
        }

        if (sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            var replayRectangle = ToReplayRect(rectangle);
            if (radiusX > 0 || radiusY > 0)
            {
                nativeSink.DrawNativeRoundedRectangle(brush, pen, replayRectangle, radiusX, radiusY);
            }
            else
            {
                nativeSink.DrawNativeRectangle(brush, pen, replayRectangle);
            }
        }
        else if (radiusX > 0 || radiusY > 0)
        {
            sink.DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        }
        else
        {
            sink.DrawRectangle(brush, pen, rectangle);
        }

        return true;
    }

    private static bool TryGetPrimitiveRectangleGeometry(
        MediaGeometry geometry,
        out Rect rectangle,
        out double radiusX,
        out double radiusY)
    {
        if (geometry is MediaRectangleGeometry rectangleGeometry
            && HasIdentityGeometryTransform(rectangleGeometry)
            && IsUsableRect(rectangleGeometry.Rect, out rectangle)
            && IsUsableRadius(rectangleGeometry.RadiusX, out radiusX)
            && IsUsableRadius(rectangleGeometry.RadiusY, out radiusY))
        {
            return true;
        }

        if (WpfMediaRectangleClipReader.TryGetRectangleClipBounds(geometry, out var rectangleBounds))
        {
            rectangle = ToRect(rectangleBounds);
            radiusX = 0;
            radiusY = 0;
            return true;
        }

        rectangle = default;
        radiusX = default;
        radiusY = default;
        return false;
    }

    private static bool TryDrawPrimitiveEllipseGeometry(
        IWpfCompositionCommandSink sink,
        MediaBrush? brush,
        MediaPen? pen,
        MediaGeometry geometry)
    {
        if (!WpfMediaEllipseGeometryReader.TryGetEllipseGeometry(geometry, out var center, out var radiusX, out var radiusY))
        {
            return false;
        }

        if (sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            nativeSink.DrawNativeEllipse(brush, pen, new WpfReplayPoint(center.X, center.Y), radiusX, radiusY);
        }
        else
        {
            sink.DrawEllipse(brush, pen, center, radiusX, radiusY);
        }

        return true;
    }

    private static bool HasIdentityGeometryTransform(MediaGeometry geometry)
    {
        var transform = geometry.Transform;
        return transform == null
            || (WpfResourceResolver.TryAdaptTransformMatrix(transform, out var matrix)
                && WpfResourceResolver.IsIdentityMatrix(matrix));
    }

    private static bool IsUsableRect(Rect rectangle, out Rect usableRectangle)
    {
        usableRectangle = rectangle;
        return !rectangle.IsEmpty
            && double.IsFinite(rectangle.X)
            && double.IsFinite(rectangle.Y)
            && double.IsFinite(rectangle.Width)
            && double.IsFinite(rectangle.Height)
            && rectangle.Width > 0
            && rectangle.Height > 0;
    }

    private static bool IsUsableRadius(double radius, out double usableRadius)
    {
        usableRadius = Math.Max(0, radius);
        return double.IsFinite(radius);
    }

    private static Rect ToRect(WpfReplayRect rectangle)
    {
        return new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    private static WpfReplayRect ToReplayRect(Rect rectangle)
    {
        return new WpfReplayRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
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
