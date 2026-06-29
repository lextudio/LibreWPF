using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Wpf.Interop;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaFormattedText = System.Windows.Media.FormattedText;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;
using PortableVisualChildrenSource = ProGPU.Wpf.Interop.IPortableVisualChildrenSource;
using PortableVisualLayoutState = ProGPU.Wpf.Interop.PortableVisualLayoutState;
using PortableVisualLayoutStateSource = ProGPU.Wpf.Interop.IPortableVisualLayoutStateSource;
using PortableVisualState = ProGPU.Wpf.Interop.PortableVisualState;
using PortableVisualStateSource = ProGPU.Wpf.Interop.IPortableVisualStateSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfVisualTreeReflectionRenderer
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly WpfRenderDataReflectionBridge _renderDataBridge;

    public WpfVisualTreeReflectionRenderer()
        : this(new WpfRenderDataReflectionBridge())
    {
    }

    public WpfVisualTreeReflectionRenderer(WpfRenderDataReflectionBridge renderDataBridge)
    {
        _renderDataBridge = renderDataBridge ?? throw new ArgumentNullException(nameof(renderDataBridge));
    }

    public WpfVisualReplayResult ReplaySubtree(
        object rootVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(rootVisual);
        ArgumentNullException.ThrowIfNull(sink);

        var stats = new ReplayStats();
        ReplaySubtreeCore(rootVisual, sink, resources, imageSourceAdapter, stats, allowRetainedVisualOwnerScopes: true);
        return stats.ToResult();
    }

    internal bool CanReplaySubtreeIntoCurrentRetainedVisual(
        object rootVisual,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(rootVisual);
        return TryCreateRetainedVisualState(rootVisual, imageSourceAdapter, out _);
    }

    internal bool TryReplaySubtreeIntoCurrentRetainedVisual(
        object rootVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out WpfVisualReplayResult result)
    {
        ArgumentNullException.ThrowIfNull(rootVisual);
        ArgumentNullException.ThrowIfNull(sink);

        var stats = new ReplayStats();
        if (!TryReplaySubtreeIntoCurrentRetainedVisualCore(rootVisual, sink, resources, imageSourceAdapter, stats))
        {
            result = default;
            return false;
        }

        result = stats.ToResult();
        return true;
    }

    private void ReplaySubtreeCore(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats,
        bool allowRetainedVisualOwnerScopes)
    {
        stats.VisualCount++;

        if (allowRetainedVisualOwnerScopes
            && TryReplaySubtreeWithRetainedVisualOwner(visual, sink, resources, imageSourceAdapter, stats))
        {
            return;
        }

        var popCount = PushVisualState(visual, sink, imageSourceAdapter, stats);
        RegisterRetainedVisualOwner(visual, sink);
        RegisterRetainedVisualStateDependencies(visual, sink);

        if (!ReplayViewport3DVisual(visual, sink, stats))
        {
            ReplayVisualContent(visual, sink, resources, imageSourceAdapter, stats);

            foreach (var child in ExtractChildren(visual))
            {
                stats.ChildEdgeCount++;
                ReplaySubtreeCore(child, sink, resources, imageSourceAdapter, stats, allowRetainedVisualOwnerScopes: false);
            }
        }

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }
    }

    private bool TryReplaySubtreeIntoCurrentRetainedVisualCore(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats)
    {
        stats.VisualCount++;

        if (sink is not IWpfRetainedVisualBranchSink retainedVisualBranchSink
            || sink is not IWpfRetainedVisualStateSink retainedVisualStateSink
            || !TryCreateRetainedVisualState(visual, imageSourceAdapter, out var visualState))
        {
            return false;
        }

        retainedVisualBranchSink.RegisterVisualOwner(visual);
        RegisterRetainedVisualStateDependencies(visual, sink);
        retainedVisualStateSink.ApplyVisualState(visualState);

        var contentTransformPopCount = 0;
        try
        {
            contentTransformPopCount = PushRetainedVisualStateContentTransform(visualState, sink);

            if (!ReplayViewport3DVisual(visual, sink, stats))
            {
                ReplayVisualContent(visual, sink, resources, imageSourceAdapter, stats);

                foreach (var child in ExtractChildren(visual))
                {
                    stats.ChildEdgeCount++;
                    ReplaySubtreeCore(
                        child,
                        sink,
                        resources,
                        imageSourceAdapter,
                        stats,
                        allowRetainedVisualOwnerScopes: true);
                }
            }
        }
        finally
        {
            PopRetainedVisualStateContentTransform(contentTransformPopCount, sink);
        }

        return true;
    }

    private bool TryReplaySubtreeWithRetainedVisualOwner(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats)
    {
        if (sink is not IWpfRetainedVisualBranchSink retainedVisualBranchSink
            || sink is not IWpfRetainedVisualStateSink retainedVisualStateSink
            || !TryCreateRetainedVisualState(visual, imageSourceAdapter, out var visualState))
        {
            TraceRetainedVisualOwnerState(visual, "unsupported");
            return false;
        }

        if (!retainedVisualBranchSink.PushVisualOwner(visual))
        {
            TraceRetainedVisualOwnerState(visual, "push-failed");
            return false;
        }

        TraceRetainedVisualOwnerState(visual, "retained");
        var replayed = false;
        try
        {
            RegisterRetainedVisualStateDependencies(visual, sink);
            retainedVisualStateSink.ApplyVisualState(visualState);

            var contentTransformPopCount = 0;
            try
            {
                contentTransformPopCount = PushRetainedVisualStateContentTransform(visualState, sink);

                if (!ReplayViewport3DVisual(visual, sink, stats))
                {
                    ReplayVisualContent(visual, sink, resources, imageSourceAdapter, stats);

                    foreach (var child in ExtractChildren(visual))
                    {
                        stats.ChildEdgeCount++;
                        ReplaySubtreeCore(
                            child,
                            sink,
                            resources,
                            imageSourceAdapter,
                            stats,
                            allowRetainedVisualOwnerScopes: true);
                    }
                }
            }
            finally
            {
                PopRetainedVisualStateContentTransform(contentTransformPopCount, sink);
            }

            replayed = true;
            return true;
        }
        finally
        {
            retainedVisualBranchSink.PopVisualOwner();
            if (!replayed)
            {
                stats.UnsupportedVisualStateCount++;
            }
        }
    }

    private static void RegisterRetainedVisualOwner(object visual, IWpfCompositionCommandSink sink)
    {
        if (sink is IWpfRetainedVisualBranchSink retainedVisualBranchSink)
        {
            retainedVisualBranchSink.RegisterVisualOwner(visual);
        }
    }

    private static void RegisterRetainedVisualStateDependencies(object visual, IWpfCompositionCommandSink sink)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            if (visualState.HasTransform)
            {
                RegisterRetainedVisualDependency(visualState.Transform, sink);
            }

            if (visualState.HasClip)
            {
                RegisterRetainedVisualDependency(visualState.Clip, sink);
            }

            if (visualState.HasOpacityMask)
            {
                RegisterRetainedVisualDependency(visualState.OpacityMask, sink);
            }

            if (visualState.HasEffect)
            {
                RegisterRetainedVisualDependency(visualState.Effect, sink);
            }

            if (visualState.HasBitmapEffect)
            {
                RegisterRetainedVisualDependency(visualState.BitmapEffect, sink);
            }

            if (visualState.HasBitmapEffectInput)
            {
                RegisterRetainedVisualDependency(visualState.BitmapEffectInput, sink);
            }

            if (visualState.HasCacheMode)
            {
                RegisterRetainedVisualDependency(visualState.CacheMode, sink);
            }
        }

        if (TryGetPortableVisualLayoutState(visual, out var layoutState) && layoutState.HasLayoutClip)
        {
            RegisterRetainedVisualDependency(layoutState.LayoutClip, sink);
        }
    }

    private static void RegisterRetainedVisualDependencies(
        IEnumerable<object?> dependencies,
        IWpfCompositionCommandSink sink)
    {
        foreach (var dependency in dependencies)
        {
            WpfRetainedVisualDependencyRegistrar.Register(sink, dependency);
        }
    }

    private static void RegisterRetainedVisualDependency(object? dependency, IWpfCompositionCommandSink sink)
    {
        WpfRetainedVisualDependencyRegistrar.Register(sink, dependency);
    }

    private static void RegisterRetainedVisualDirectDependency(object? dependency, IWpfCompositionCommandSink sink)
    {
        WpfRetainedVisualDependencyRegistrar.RegisterDirect(sink, dependency);
    }

    private static bool TryCreateRetainedVisualState(
        object visual,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out WpfRetainedVisualState state)
    {
        state = default;
        var offset = Vector2.Zero;
        var transform = Matrix4x4.Identity;
        var opacity = 1f;
        WpfReplayRect? clipBounds = null;
        WpfReplayRect? outerClipBounds = null;
        if (!TryCreateRetainedOpacityMaskState(visual, out var opacityMask, out var opacityMaskBounds))
        {
            return false;
        }

        if (TryReadVisualTransform(visual, out var transformValue))
        {
            if (!WpfReflectionResourceResolver.TryAdaptTransformMatrix(transformValue, out transform))
            {
                return false;
            }
        }

        if (TryReadOffset(visual, out var offsetX, out var offsetY))
        {
            offset = new Vector2((float)offsetX, (float)offsetY);
        }

        if (TryGetVisualClipBounds(visual, out var rectangleClipBounds))
        {
            if (!IsUsableBounds(rectangleClipBounds))
            {
                return false;
            }

            var combinedClipBounds = CombineClipBounds(clipBounds, rectangleClipBounds);
            if (!IsUsableBounds(combinedClipBounds))
            {
                return false;
            }

            clipBounds = combinedClipBounds;
        }

        if (!clipBounds.HasValue && TryCreateImplicitRetainedVisualClip(visual, out var implicitClipBounds))
        {
            clipBounds = implicitClipBounds;
        }

        if (TryGetScrollableAreaClipBounds(visual, out var scrollableClipBounds))
        {
            if (!IsUsableBounds(scrollableClipBounds))
            {
                return false;
            }

            outerClipBounds = scrollableClipBounds;
        }

        if (TryReadOpacity(visual, out var opacityDouble))
        {
            opacity = (float)opacityDouble;
        }

        if (HasUnsupportedRetainedVisualOwnerState(visual))
        {
            return false;
        }

        if (TryCreateSingleNativeRetainedVisualScopeState(
                visual,
                offset,
                transform,
                opacity,
                clipBounds,
                outerClipBounds,
                opacityMask,
                opacityMaskBounds,
                imageSourceAdapter,
                out state))
        {
            return true;
        }

        if (HasNativeRetainedVisualScopeState(visual))
        {
            return false;
        }

        Vector2? size = null;
        if (TryReadRenderSizeBounds(visual, out var bounds))
        {
            size = new Vector2((float)bounds.Width, (float)bounds.Height);
        }

        state = new WpfRetainedVisualState(
            offset,
            transform,
            opacity,
            clipBounds,
            size,
            contentBounds: null,
            opacityMask: opacityMask,
            opacityMaskBounds: opacityMaskBounds,
            outerClipBounds: outerClipBounds);
        return true;
    }

    private static int PushRetainedVisualStateContentTransform(
        in WpfRetainedVisualState state,
        IWpfCompositionCommandSink sink)
    {
        if (!state.ContentBounds.HasValue)
        {
            return 0;
        }

        var bounds = state.ContentBounds.Value;
        if (bounds.X == 0 && bounds.Y == 0)
        {
            return 0;
        }

        if (sink is IWpfNativeTransformCommandSink nativeTransformSink)
        {
            nativeTransformSink.PushNativeTransform(Matrix4x4.CreateTranslation((float)-bounds.X, (float)-bounds.Y, 0f));
        }
        else
        {
            sink.PushNoOpScope();
        }

        return 1;
    }

    private static void PopRetainedVisualStateContentTransform(int popCount, IWpfCompositionCommandSink sink)
    {
        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }
    }

    private static bool HasUnsupportedRetainedVisualOwnerState(object visual)
    {
        return false;
    }

    private static bool HasNativeRetainedVisualScopeState(object visual)
    {
        return HasVisualEffect(visual)
            || HasVisualBitmapEffect(visual)
            || HasVisualBitmapEffectInput(visual)
            || HasVisualCacheMode(visual);
    }

    private static bool HasVisualEffect(object visual)
    {
        return TryGetVisualEffect(visual, out _);
    }

    private static bool TryGetVisualEffect(object visual, out object? effect)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            effect = visualState.Effect;
            return visualState.HasEffect && effect != null;
        }

        effect = null;
        return false;
    }

    private static bool HasVisualBitmapEffect(object visual)
    {
        return TryGetVisualBitmapEffect(visual, out _);
    }

    private static bool TryGetVisualBitmapEffect(object visual, out object? bitmapEffect)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            bitmapEffect = visualState.BitmapEffect;
            return visualState.HasBitmapEffect && bitmapEffect != null;
        }

        bitmapEffect = null;
        return false;
    }

    private static bool HasVisualBitmapEffectInput(object visual)
    {
        return TryGetVisualBitmapEffectInput(visual, out _);
    }

    private static bool TryGetVisualBitmapEffectInput(object visual, out object? bitmapEffectInput)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            bitmapEffectInput = visualState.BitmapEffectInput;
            return visualState.HasBitmapEffectInput && bitmapEffectInput != null;
        }

        bitmapEffectInput = null;
        return false;
    }

    private static bool HasVisualCacheMode(object visual)
    {
        return TryGetVisualCacheMode(visual, out _);
    }

    private static bool TryGetVisualCacheMode(object visual, out object? cacheMode)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            cacheMode = visualState.CacheMode;
            return visualState.HasCacheMode && cacheMode != null;
        }

        cacheMode = null;
        return false;
    }

    private static bool TryGetVisualBitmapScalingMode(object visual, out object? bitmapScalingMode)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            bitmapScalingMode = visualState.BitmapScalingMode;
            return visualState.HasBitmapScalingMode && bitmapScalingMode != null;
        }

        bitmapScalingMode = null;
        return false;
    }

    private static bool TryGetVisualEdgeMode(object visual, out object? edgeMode)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            edgeMode = visualState.EdgeMode;
            return visualState.HasEdgeMode && edgeMode != null;
        }

        edgeMode = null;
        return false;
    }

    private static bool TryGetVisualClearTypeHint(object visual, out object? clearTypeHint)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            clearTypeHint = visualState.ClearTypeHint;
            return visualState.HasClearTypeHint && clearTypeHint != null;
        }

        clearTypeHint = null;
        return false;
    }

    private static bool TryGetVisualTextRenderingMode(object visual, out object? textRenderingMode)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            textRenderingMode = visualState.TextRenderingMode;
            return visualState.HasTextRenderingMode && textRenderingMode != null;
        }

        textRenderingMode = null;
        return false;
    }

    private static bool TryGetVisualTextHintingMode(object visual, out object? textHintingMode)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            textHintingMode = visualState.TextHintingMode;
            return visualState.HasTextHintingMode && textHintingMode != null;
        }

        textHintingMode = null;
        return false;
    }

    private static bool TryCreateSingleNativeRetainedVisualScopeState(
        object visual,
        Vector2 offset,
        Matrix4x4 transform,
        float opacity,
        WpfReplayRect? clipBounds,
        WpfReplayRect? outerClipBounds,
        MediaBrush? opacityMask,
        WpfReplayRect? opacityMaskBounds,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out WpfRetainedVisualState state)
    {
        state = default;

        var effectStateCount = 0;
        global::ProGPU.Scene.EffectBase? effect = null;
        var cacheAsLayer = false;

        if (TryGetVisualEffect(visual, out var effectValue))
        {
            if (!WpfEffectReflection.TryCreateProGpuEffect(effectValue, out effect, imageSourceAdapter))
            {
                return false;
            }

            effectStateCount++;
        }

        if (TryGetVisualBitmapEffect(visual, out var bitmapEffect))
        {
            if (effectStateCount != 0)
            {
                return false;
            }

            TryGetVisualBitmapEffectInput(visual, out var bitmapEffectInput);
            if (!WpfEffectReflection.TryCreateProGpuPushEffect(bitmapEffect, bitmapEffectInput, out effect, imageSourceAdapter))
            {
                return false;
            }

            effectStateCount++;
        }
        else if (HasVisualBitmapEffectInput(visual))
        {
            return false;
        }

        if (HasVisualCacheMode(visual))
        {
            cacheAsLayer = true;
        }

        if (effectStateCount == 0 && !cacheAsLayer)
        {
            return false;
        }

        Vector2? size = null;
        WpfReplayRect? contentBounds = null;
        var retainedOffset = offset;
        var retainedTransform = transform;
        var retainedClipBounds = clipBounds;
        var retainedOpacityMaskBounds = opacityMaskBounds;
        if (TryReadRetainedVisualBounds(visual, out var bounds))
        {
            size = new Vector2((float)bounds.Width, (float)bounds.Height);
            contentBounds = bounds;
            retainedClipBounds = clipBounds.HasValue
                ? OffsetBounds(clipBounds.Value, -bounds.X, -bounds.Y)
                : null;
            retainedOpacityMaskBounds = opacityMaskBounds.HasValue
                ? OffsetBounds(opacityMaskBounds.Value, -bounds.X, -bounds.Y)
                : null;

            var boundsOffset = new Vector2((float)bounds.X, (float)bounds.Y);
            if (transform == Matrix4x4.Identity)
            {
                retainedOffset = offset + boundsOffset;
            }
            else
            {
                retainedTransform = Matrix4x4.CreateTranslation((float)bounds.X, (float)bounds.Y, 0f) * transform;
            }
        }

        state = new WpfRetainedVisualState(
            retainedOffset,
            retainedTransform,
            opacity,
            retainedClipBounds,
            size,
            effect,
            cacheAsLayer,
            contentBounds,
            opacityMask,
            retainedOpacityMaskBounds,
            outerClipBounds);
        return true;
    }

    private static bool TryCreateRetainedOpacityMaskState(
        object visual,
        out MediaBrush? opacityMask,
        out WpfReplayRect? opacityMaskBounds)
    {
        opacityMask = null;
        opacityMaskBounds = null;

        if (!TryGetOpacityMask(visual, out var opacityMaskValue) || opacityMaskValue == null)
        {
            return true;
        }

        opacityMask = WpfReflectionResourceResolver.AdaptBrush(opacityMaskValue);
        if (opacityMask == null || !TryReadOpacityMaskBounds(visual, out var bounds))
        {
            opacityMask = null;
            return false;
        }

        opacityMaskBounds = bounds;
        return true;
    }

    private static bool TryReadRectangleClipBounds(object clip, out WpfReplayRect bounds)
    {
        if (TryReadRect(clip, out bounds) && IsUsableBounds(bounds))
        {
            return true;
        }

        if (!TryGetPropertyValue(clip, "Rect", out var rectValue)
            || rectValue == null
            || !TryReadRect(rectValue, out bounds)
            || !IsUsableBounds(bounds))
        {
            bounds = default;
            return false;
        }

        if ((TryReadDoubleProperty(clip, "RadiusX", out var radiusX) && radiusX != 0)
            || (TryReadDoubleProperty(clip, "RadiusY", out var radiusY) && radiusY != 0))
        {
            bounds = default;
            return false;
        }

        if (TryGetPropertyValue(clip, "Transform", out var transformValue) && transformValue != null)
        {
            if (!WpfReflectionResourceResolver.TryAdaptTransformMatrix(transformValue, out var transform)
                || !WpfReflectionResourceResolver.IsIdentityMatrix(transform))
            {
                bounds = default;
                return false;
            }
        }

        return true;
    }

    private static WpfReplayRect CombineClipBounds(WpfReplayRect? current, WpfReplayRect next)
    {
        if (!current.HasValue)
        {
            return next;
        }

        var x1 = Math.Max(current.Value.X, next.X);
        var y1 = Math.Max(current.Value.Y, next.Y);
        var x2 = Math.Min(current.Value.X + current.Value.Width, next.X + next.Width);
        var y2 = Math.Min(current.Value.Y + current.Value.Height, next.Y + next.Height);
        return x2 <= x1 || y2 <= y1
            ? WpfReplayRect.Empty
            : new WpfReplayRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static WpfReplayRect OffsetBounds(WpfReplayRect bounds, double offsetX, double offsetY)
    {
        return new WpfReplayRect(bounds.X + offsetX, bounds.Y + offsetY, bounds.Width, bounds.Height);
    }

    private static Matrix4x4 ToMatrix4x4(MediaTransform transform)
    {
        return WpfReflectionResourceResolver.TryAdaptTransformMatrix(transform, out var matrix)
            ? matrix
            : Matrix4x4.Identity;
    }

    private static bool ReplayViewport3DVisual(
        object visual,
        IWpfCompositionCommandSink sink,
        ReplayStats stats)
    {
        if (!TypeNameEndsWith(visual, "Viewport3DVisual"))
        {
            return false;
        }

        if (sink is IWpfViewport3DCommandSink viewport3DSink
            && viewport3DSink.DrawViewport3D(visual))
        {
            stats.ContentCount++;
        }
        else
        {
            stats.UnsupportedContentCount++;
        }

        return true;
    }

    private void ReplayVisualContent(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats)
    {
        if (!WpfVisualContentReflectionBridge.TryExtractContent(visual, out var content) || content == null)
        {
            return;
        }

        if (!HasRenderDataShape(content.GetType()))
        {
            stats.UnsupportedContentCount++;
            return;
        }

        stats.ContentCount++;
        var snapshot = WpfRenderDataReflectionBridge.Extract(content);
        RegisterRetainedVisualDirectDependency(content, sink);
        RegisterRetainedVisualDependencies(snapshot.DependentResources, sink);
        stats.AddRenderData(_renderDataBridge.Replay(snapshot, sink, resources, imageSourceAdapter));
    }

    private static int PushVisualState(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats)
    {
        var popCount = 0;
        var localVisualTransform = Matrix4x4.Identity;
        var canProjectScrollableClipToOuterSpace = true;

        if (TryReadVisualTransform(visual, out var transform))
        {
            if (sink is IWpfNativeTransformCommandSink nativeTransformSink
                && WpfReflectionResourceResolver.TryAdaptTransformMatrix(transform, out var nativeTransform))
            {
                nativeTransformSink.PushNativeTransform(nativeTransform);
                localVisualTransform = nativeTransform * localVisualTransform;
                popCount++;
            }
            else if (WpfReflectionResourceResolver.AdaptTransform(transform) is { } mediaTransform)
            {
                sink.PushTransform(mediaTransform);
                canProjectScrollableClipToOuterSpace = false;
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryReadOffset(visual, out var offsetX, out var offsetY) && (offsetX != 0 || offsetY != 0))
        {
            if (sink is IWpfNativeTransformCommandSink nativeTransformSink)
            {
                var offsetTransform = Matrix4x4.CreateTranslation((float)offsetX, (float)offsetY, 0f);
                nativeTransformSink.PushNativeTransform(offsetTransform);
                localVisualTransform = offsetTransform * localVisualTransform;
            }
            else
            {
                sink.PushNoOpScope();
                canProjectScrollableClipToOuterSpace = false;
            }

            popCount++;
        }

        if (TryGetVisualClipBounds(visual, out var rectangleClipBounds))
        {
            PushRectangleClip(sink, rectangleClipBounds);
            popCount++;
        }
        else if (TryGetVisualClip(visual, out var clip) && clip != null)
        {
            var clipGeometry = WpfReflectionResourceResolver.AdaptGeometry(clip);
            if (clipGeometry != null)
            {
                sink.PushClip(clipGeometry);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetScrollableAreaClipBounds(visual, out var scrollableClipBounds))
        {
            if (IsUsableBounds(scrollableClipBounds))
            {
                if (!localVisualTransform.IsIdentity
                    && canProjectScrollableClipToOuterSpace
                    && sink is IWpfNativeTransformCommandSink nativeTransformSink
                    && Matrix4x4.Invert(localVisualTransform, out var inverseLocalTransform))
                {
                    nativeTransformSink.PushNativeTransform(inverseLocalTransform);
                    popCount++;
                    PushRectangleClip(sink, scrollableClipBounds);
                    popCount++;
                    nativeTransformSink.PushNativeTransform(localVisualTransform);
                    popCount++;
                }
                else
                {
                    PushRectangleClip(sink, scrollableClipBounds);
                    popCount++;
                }
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryReadOpacity(visual, out var opacity)
            && opacity != 1)
        {
            sink.PushOpacity(opacity);
            popCount++;
        }

        if (TryGetOpacityMask(visual, out var opacityMask) && opacityMask != null)
        {
            var mediaOpacityMask = WpfReflectionResourceResolver.AdaptBrush(opacityMask);
            if (mediaOpacityMask != null && TryReadOpacityMaskBounds(visual, out var opacityMaskBounds))
            {
                WpfPortableCommandSinkBridge.PushOpacityMask(sink, mediaOpacityMask, opacityMaskBounds);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetVisualEffect(visual, out var effect))
        {
            if (WpfEffectReflection.TryCreateProGpuEffect(effect, out var proGpuEffect, imageSourceAdapter)
                && WpfPortableCommandSinkBridge.TryPushVisualEffect(
                    sink,
                    proGpuEffect,
                    TryReadOpacityMaskBounds(visual, out var effectBounds) ? effectBounds : null))
            {
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetVisualBitmapEffect(visual, out var bitmapEffect))
        {
            TryGetVisualBitmapEffectInput(visual, out var bitmapEffectInput);
            if (WpfEffectReflection.TryCreateProGpuPushEffect(bitmapEffect, bitmapEffectInput, out var proGpuBitmapEffect, imageSourceAdapter)
                && WpfPortableCommandSinkBridge.TryPushVisualEffect(
                    sink,
                    proGpuBitmapEffect,
                    TryReadOpacityMaskBounds(visual, out var bitmapEffectBounds) ? bitmapEffectBounds : null))
            {
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }
        else if (HasVisualBitmapEffectInput(visual))
        {
            stats.UnsupportedVisualStateCount++;
        }

        if (HasVisualCacheMode(visual))
        {
            if (WpfPortableCommandSinkBridge.TryPushVisualCache(
                sink,
                TryReadOpacityMaskBounds(visual, out var cacheBounds) ? cacheBounds : null))
            {
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryCreateVisualGuidelineSet(visual, out var guidelineSet))
        {
            sink.PushGuidelineSet(guidelineSet);
            popCount++;
        }

        if (TryGetVisualBitmapScalingMode(visual, out var bitmapScalingMode))
        {
            if (WpfBitmapScalingModeReflection.IsSupported(bitmapScalingMode))
            {
                sink.PushBitmapScalingMode(bitmapScalingMode);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetVisualEdgeMode(visual, out var edgeMode))
        {
            if (WpfEdgeModeReflection.IsSupported(edgeMode))
            {
                sink.PushEdgeMode(edgeMode);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        var pushedTextRenderingMode = false;
        if (TryGetVisualTextRenderingMode(visual, out var textRenderingMode))
        {
            if (WpfTextRenderingModeReflection.IsSupported(textRenderingMode))
            {
                sink.PushTextRenderingMode(textRenderingMode);
                popCount++;
                pushedTextRenderingMode = true;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (!pushedTextRenderingMode
            && TryGetVisualClearTypeHint(visual, out var clearTypeHint)
            && WpfTextRenderingModeReflection.TryMapClearTypeHintToTextRenderingMode(clearTypeHint, out var clearTypeMode))
        {
            sink.PushTextRenderingMode(clearTypeMode);
            popCount++;
        }

        if (TryGetVisualTextHintingMode(visual, out var textHintingMode))
        {
            if (WpfTextRenderingModeReflection.IsSupportedTextHintingMode(textHintingMode))
            {
                sink.PushTextHintingMode(textHintingMode);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        stats.UnsupportedVisualStateCount += CountUnsupportedVisualState(visual);

        return popCount;
    }

    private static void PushRectangleClip(IWpfCompositionCommandSink sink, WpfReplayRect bounds)
    {
        if (sink is IWpfNativeClipCommandSink nativeClipSink)
        {
            nativeClipSink.PushNativeClip(bounds);
            return;
        }

        sink.PushClip(WpfReflectionResourceResolver.CreateRectanglePath(bounds));
    }

    private static int CountUnsupportedVisualState(object visual)
    {
        var count = 0;

        if (TryGetVisualClearTypeHint(visual, out var clearTypeHint)
            && !WpfTextRenderingModeReflection.IsSupportedClearTypeHint(clearTypeHint))
        {
            count++;
        }

        return count;
    }

    private static bool TryCreateVisualGuidelineSet(object visual, out object guidelineSet)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            if (!visualState.HasSnappingGuidelinesX && !visualState.HasSnappingGuidelinesY)
            {
                guidelineSet = null!;
                return false;
            }

            guidelineSet = new VisualGuidelineSet(
                visualState.HasSnappingGuidelinesX ? visualState.SnappingGuidelinesX ?? Array.Empty<double>() : Array.Empty<double>(),
                visualState.HasSnappingGuidelinesY ? visualState.SnappingGuidelinesY ?? Array.Empty<double>() : Array.Empty<double>());
            return true;
        }

        guidelineSet = null!;
        return false;
    }

    private static IReadOnlyList<object> ExtractChildren(object visual)
    {
        if (visual is PortableVisualChildrenSource visualChildrenSource)
        {
            return ExtractPortableVisualChildren(visualChildrenSource);
        }

        return Array.Empty<object>();
    }

    private static IReadOnlyList<object> ExtractPortableVisualChildren(PortableVisualChildrenSource visualChildrenSource)
    {
        if (!visualChildrenSource.TryGetPortableVisualChildCount(out var count) || count <= 0)
        {
            return Array.Empty<object>();
        }

        var result = new List<object>(count);
        for (var i = 0; i < count; i++)
        {
            if (visualChildrenSource.TryGetPortableVisualChild(i, out var child) && child != null)
            {
                result.Add(child);
            }
        }

        return result;
    }

    private static bool TryGetPortableVisualState(object visual, out PortableVisualState state)
    {
        if (visual is PortableVisualStateSource visualStateSource
            && visualStateSource.TryGetPortableVisualState(out state))
        {
            return true;
        }

        state = null!;
        return false;
    }

    private static bool TryGetPortableVisualLayoutState(object visual, out PortableVisualLayoutState state)
    {
        if (visual is PortableVisualLayoutStateSource visualLayoutSource
            && visualLayoutSource.TryGetPortableVisualLayoutState(out state))
        {
            return true;
        }

        state = null!;
        return false;
    }

    private static bool TryReadOffset(object visual, out double x, out double y)
    {
        x = 0;
        y = 0;

        if (TryGetPortableVisualState(visual, out var visualState) && visualState.HasOffset)
        {
            x = visualState.Offset.X;
            y = visualState.Offset.Y;
            return true;
        }

        return false;
    }

    private static bool TryReadVisualTransform(object visual, out object? transform)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            transform = visualState.Transform;
            return visualState.HasTransform && transform != null;
        }

        transform = null;
        return false;
    }

    private static bool TryReadOpacity(object visual, out double opacity)
    {
        if (TryGetPortableVisualState(visual, out var visualState) && visualState.HasOpacity)
        {
            opacity = visualState.Opacity;
            return true;
        }

        opacity = 1.0;
        return false;
    }

    private static bool TryGetOpacityMask(object visual, out object? opacityMask)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            opacityMask = visualState.OpacityMask;
            return visualState.HasOpacityMask && opacityMask != null;
        }

        opacityMask = null;
        return false;
    }

    private static bool TryReadOpacityMaskBounds(object visual, out WpfReplayRect bounds)
    {
        foreach (var propertyName in new[] { "Bounds", "DescendantBounds", "VisualContentBounds", "ContentBounds" })
        {
            if (TryGetPropertyValue(visual, propertyName, out var boundsValue)
                && boundsValue != null
                && TryReadRect(boundsValue, out bounds)
                && IsUsableBounds(bounds))
            {
                return true;
            }
        }

        if (TryReadRenderSizeBounds(visual, out bounds))
        {
            return true;
        }

        if (TryInferVisualContentBounds(visual, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryReadRetainedVisualBounds(object visual, out WpfReplayRect bounds)
    {
        if (TryReadRenderSizeBounds(visual, out bounds))
        {
            return true;
        }

        return TryReadOpacityMaskBounds(visual, out bounds);
    }

    private static bool TryCreateImplicitRetainedVisualClip(object visual, out WpfReplayRect clipBounds)
    {
        if (!RequiresImplicitRetainedVisualClip(visual))
        {
            clipBounds = default;
            return false;
        }

        return TryReadRenderSizeBounds(visual, out clipBounds);
    }

    private static bool TryReadRenderSizeBounds(object visual, out WpfReplayRect bounds)
    {
        if (TryGetPortableVisualLayoutState(visual, out var layoutState)
            && TryReadPortableRenderSizeBounds(layoutState, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryReadPortableRenderSizeBounds(PortableVisualLayoutState state, out WpfReplayRect bounds)
    {
        if (state.HasRenderSize
            && state.RenderSize.Width > 0
            && state.RenderSize.Height > 0)
        {
            bounds = new WpfReplayRect(0, 0, state.RenderSize.Width, state.RenderSize.Height);
            return IsUsableBounds(bounds);
        }

        bounds = default;
        return false;
    }

    private static bool RequiresImplicitRetainedVisualClip(object visual)
    {
        var type = visual.GetType();
        if (type.FullName?.StartsWith("Xceed.Wpf.DataGrid.", StringComparison.Ordinal) != true)
        {
            return false;
        }

        return type.Name.Contains("Cell", StringComparison.Ordinal);
    }

    private static void TraceRetainedVisualOwnerState(object visual, string state)
    {
        if (!IsRetainedVisualTraceEnabled())
        {
            return;
        }

        if (!TryGetPropertyValue(visual, "Name", out var nameValue) ||
            nameValue == null ||
            string.IsNullOrWhiteSpace(nameValue.ToString()))
        {
            return;
        }

        Console.Error.WriteLine(
            $"ProGPU WPF retained visual {state}: {visual.GetType().Name}#{nameValue}");
    }

    private static bool IsRetainedVisualTraceEnabled()
    {
        string? value = Environment.GetEnvironmentVariable("PROGPU_WPF_TRACE_RETAINED_VISUALS");
        return string.Equals(value, "1", StringComparison.Ordinal) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryInferVisualContentBounds(object visual, out WpfReplayRect bounds)
    {
        bounds = default;
        var hasBounds = false;

        if (WpfVisualContentReflectionBridge.TryExtractContent(visual, out var content)
            && content != null
            && HasRenderDataShape(content.GetType()))
        {
            var snapshot = WpfRenderDataReflectionBridge.Extract(content);
            var resolver = WpfReflectionResourceResolver.FromDependentResources(snapshot.DependentResources);
            var sink = new BoundsAccumulatingSink();
            try
            {
                _ = new WpfMilRenderDataDecoder().Decode(snapshot.RenderData, sink, resolver);
            }
            catch (TypeLoadException)
            {
                return false;
            }

            if (sink.TryGetBounds(out var contentBounds))
            {
                bounds = contentBounds;
                hasBounds = true;
            }
        }

        foreach (var child in ExtractChildren(visual))
        {
            if (!TryReadOpacityMaskBounds(child, out var childBounds))
            {
                continue;
            }

            if (!TryProjectChildBoundsIntoParent(child, childBounds, out var projectedChildBounds))
            {
                bounds = default;
                return false;
            }

            bounds = hasBounds ? UnionBounds(bounds, projectedChildBounds) : projectedChildBounds;
            hasBounds = true;
        }

        return hasBounds && IsUsableBounds(bounds);
    }

    private static bool TryProjectChildBoundsIntoParent(object child, WpfReplayRect childBounds, out WpfReplayRect parentBounds)
    {
        parentBounds = default;
        if (!TryClipChildBounds(child, childBounds, out var clippedBounds))
        {
            return false;
        }

        var transform = Matrix4x4.Identity;
        if (TryReadVisualTransform(child, out var transformValue))
        {
            if (!WpfReflectionResourceResolver.TryAdaptTransformMatrix(transformValue, out transform))
            {
                return false;
            }
        }

        if (TryReadOffset(child, out var offsetX, out var offsetY) && (offsetX != 0 || offsetY != 0))
        {
            transform = Matrix4x4.CreateTranslation((float)offsetX, (float)offsetY, 0f) * transform;
        }

        parentBounds = TransformBounds(clippedBounds, transform);
        if (TryGetScrollableAreaClipBounds(child, out var scrollableClipBounds))
        {
            if (!IsUsableBounds(scrollableClipBounds))
            {
                return false;
            }

            parentBounds = IntersectBounds(parentBounds, scrollableClipBounds);
        }

        return IsUsableBounds(parentBounds);
    }

    private static bool TryClipChildBounds(object child, WpfReplayRect childBounds, out WpfReplayRect clippedBounds)
    {
        clippedBounds = childBounds;
        if (!IsUsableBounds(clippedBounds))
        {
            return false;
        }

        WpfReplayRect? clipBounds = null;
        if (TryGetVisualClipBounds(child, out var childClipBounds))
        {
            clipBounds = childClipBounds;
        }

        if (!clipBounds.HasValue)
        {
            return true;
        }

        clippedBounds = IntersectBounds(clippedBounds, clipBounds.Value);
        return IsUsableBounds(clippedBounds);
    }

    private static bool TryReadRect(object rectValue, out WpfReplayRect bounds)
    {
        if (TryReadDoubleProperty(rectValue, "X", out var x)
            && TryReadDoubleProperty(rectValue, "Y", out var y)
            && TryReadDoubleProperty(rectValue, "Width", out var width)
            && TryReadDoubleProperty(rectValue, "Height", out var height))
        {
            bounds = new WpfReplayRect(x, y, width, height);
            return true;
        }

        bounds = default;
        return false;
    }

    private static WpfReplayRect ToReplayRect(PortableRect bounds)
    {
        return bounds.IsEmpty
            ? default
            : new WpfReplayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static bool TryGetScrollableAreaClipBounds(object visual, out WpfReplayRect bounds)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            if (visualState.HasScrollableAreaClip)
            {
                bounds = ToReplayRect(visualState.ScrollableAreaClip);
                return true;
            }
        }

        bounds = default;
        return false;
    }

    private static bool TryGetVisualClipBounds(object visual, out WpfReplayRect bounds)
    {
        bounds = default;
        var hasBounds = false;

        if (TryGetPortableVisualState(visual, out var visualState) && visualState.HasClip)
        {
            if (visualState.Clip == null || !TryReadRectangleClipBounds(visualState.Clip, out bounds))
            {
                bounds = default;
                return false;
            }

            hasBounds = true;
        }

        if (TryGetPortableVisualLayoutState(visual, out var layoutState)
            && layoutState.HasLayoutClip
            && layoutState.LayoutClip != null)
        {
            if (!TryReadRectangleClipBounds(layoutState.LayoutClip, out var layoutClipBounds))
            {
                bounds = default;
                return false;
            }

            bounds = hasBounds ? CombineClipBounds(bounds, layoutClipBounds) : layoutClipBounds;
            hasBounds = true;
        }

        if (TryCreateClipToBoundsClipBounds(visual, out var clipToBoundsBounds))
        {
            bounds = hasBounds ? CombineClipBounds(bounds, clipToBoundsBounds) : clipToBoundsBounds;
            hasBounds = true;
        }

        if (!hasBounds || !IsUsableBounds(bounds))
        {
            bounds = default;
            return false;
        }

        return true;
    }

    private static bool TryGetVisualClip(object visual, out object? clip)
    {
        var hasPortableVisualState = TryGetPortableVisualState(visual, out var visualState);
        object? currentClip = null;
        var hasCurrentClip = false;
        if (hasPortableVisualState && visualState.HasClip)
        {
            currentClip = visualState.Clip;
            hasCurrentClip = currentClip != null;
        }

        var hasPortableLayoutState = TryGetPortableVisualLayoutState(visual, out var layoutState);
        if (hasPortableLayoutState && layoutState.HasLayoutClip && layoutState.LayoutClip != null)
        {
            if (hasCurrentClip)
            {
                return TryCreateIntersectedClip(layoutState.LayoutClip, currentClip!, out clip);
            }

            clip = layoutState.LayoutClip;
            return true;
        }

        if (TryCreateClipToBoundsClip(visual, out var clipToBoundsClip))
        {
            if (hasCurrentClip)
            {
                return TryCreateIntersectedClip(clipToBoundsClip!, currentClip!, out clip);
            }

            clip = clipToBoundsClip;
            return true;
        }

        clip = currentClip;
        return hasCurrentClip;
    }

    private static bool TryCreateClipToBoundsClipBounds(object visual, out WpfReplayRect bounds)
    {
        bounds = default;
        if (TryGetPortableVisualLayoutState(visual, out var layoutState) && layoutState.HasClipToBounds)
        {
            return layoutState.ClipToBounds
                && TryReadPortableRenderSizeBounds(layoutState, out bounds);
        }

        return false;
    }

    private static bool TryCreateClipToBoundsClip(object visual, out object? clip)
    {
        clip = null;
        if (TryCreateClipToBoundsClipBounds(visual, out var bounds))
        {
            clip = CreateRectangleClipGeometry(bounds);
            return true;
        }

        return false;
    }

    private static bool TryCreateIntersectedClip(object first, object second, out object? clip)
    {
        if (TryReadRectangleClipBounds(first, out var firstBounds)
            && TryReadRectangleClipBounds(second, out var secondBounds))
        {
            var combined = CombineClipBounds(firstBounds, secondBounds);
            clip = CreateRectangleClipGeometry(combined);
            return true;
        }

        var firstGeometry = WpfReflectionResourceResolver.AdaptGeometry(first);
        var secondGeometry = WpfReflectionResourceResolver.AdaptGeometry(second);
        if (firstGeometry == null || secondGeometry == null)
        {
            clip = null;
            return false;
        }

        clip = new System.Windows.Media.CombinedGeometry(
            System.Windows.Media.GeometryCombineMode.Intersect,
            firstGeometry,
            secondGeometry);
        return true;
    }

    private static System.Windows.Media.RectangleGeometry CreateRectangleClipGeometry(WpfReplayRect bounds)
    {
        return new System.Windows.Media.RectangleGeometry(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    private static bool IsUsableBounds(WpfReplayRect bounds)
    {
        return double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height)
            && bounds.Width > 0
            && bounds.Height > 0;
    }

    private static WpfReplayRect UnionBounds(WpfReplayRect left, WpfReplayRect right)
    {
        var x1 = Math.Min(left.X, right.X);
        var y1 = Math.Min(left.Y, right.Y);
        var x2 = Math.Max(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Max(left.Y + left.Height, right.Y + right.Height);

        return new WpfReplayRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static WpfReplayRect IntersectBounds(WpfReplayRect left, WpfReplayRect right)
    {
        var x1 = Math.Max(left.X, right.X);
        var y1 = Math.Max(left.Y, right.Y);
        var x2 = Math.Min(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Min(left.Y + left.Height, right.Y + right.Height);

        return x2 <= x1 || y2 <= y1
            ? WpfReplayRect.Empty
            : new WpfReplayRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static WpfReplayRect TransformBounds(WpfReplayRect bounds, System.Numerics.Matrix4x4 transform)
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

        return new WpfReplayRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static WpfReplayRect? ApplyClip(WpfReplayRect bounds, WpfReplayRect? clip)
    {
        if (!IsUsableBounds(bounds))
        {
            return null;
        }

        if (!clip.HasValue)
        {
            return bounds;
        }

        var clipped = IntersectBounds(bounds, clip.Value);
        return IsUsableBounds(clipped) ? clipped : null;
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

    private static bool TryReadBoolProperty(object instance, string propertyName, out bool value)
    {
        value = false;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue) || propertyValue == null)
        {
            return false;
        }

        if (propertyValue is bool boolValue)
        {
            value = boolValue;
            return true;
        }

        return bool.TryParse(propertyValue.ToString(), out value);
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

    private static bool TryGetPropertyValue(object instance, string propertyName, out object? value)
    {
        if (instance == null)
        {
            value = null;
            return false;
        }

        var property = FindProperty(instance.GetType(), propertyName);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            value = null;
            return false;
        }

        try
        {
            value = property.GetValue(instance);
            return true;
        }
        catch (TargetInvocationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (MethodAccessException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        value = null;
        return false;
    }

    private static bool TryGetFieldValue(object instance, string fieldName, out object? value)
    {
        if (instance == null)
        {
            value = null;
            return false;
        }

        var field = FindField(instance.GetType(), fieldName);
        if (field == null)
        {
            value = null;
            return false;
        }

        try
        {
            value = field.GetValue(instance);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (FieldAccessException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        value = null;
        return false;
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

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var property = current.GetProperty(name, MemberFlags);
            if (property != null)
            {
                return property;
            }
        }

        return null;
    }

    private static MethodInfo? FindParameterlessMethod(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                MemberFlags,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (method != null)
            {
                return method;
            }
        }

        return null;
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name, MemberFlags);
            if (field != null)
            {
                return field;
            }
        }

        return null;
    }

    private static MethodInfo? FindMethod(Type type, string name, params Type[] parameterTypes)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var method = current.GetMethod(name, MemberFlags, binder: null, types: parameterTypes, modifiers: null);
            if (method != null)
            {
                return method;
            }
        }

        return null;
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
            default:
                result = 0;
                return false;
        }
    }

    private static bool TypeNameEndsWith(object instance, string suffix)
    {
        var type = instance.GetType();
        return type.Name.EndsWith(suffix, StringComparison.Ordinal)
            || (type.FullName?.EndsWith("." + suffix, StringComparison.Ordinal) ?? false);
    }

    private static bool HasRenderDataShape(Type contentType)
    {
        return FindField(contentType, "_buffer") != null
            && FindField(contentType, "_curOffset") != null
            && FindField(contentType, "_dependentResources") != null;
    }

    private sealed class BoundsAccumulatingSink : IWpfCompositionCommandSink
    {
        private enum PushKind
        {
            NoOp,
            Clip,
            Transform
        }

        private readonly Stack<PushKind> _pushStack = new();
        private readonly Stack<System.Numerics.Matrix4x4> _transformStack = new();
        private readonly Stack<WpfReplayRect?> _clipStack = new();
        private WpfReplayRect _bounds;
        private bool _hasBounds;

        public BoundsAccumulatingSink()
        {
            _transformStack.Push(System.Numerics.Matrix4x4.Identity);
            _clipStack.Push(null);
        }

        public MediaDrawingContext DrawingContext => null!;

        public bool TryGetBounds(out WpfReplayRect bounds)
        {
            bounds = _bounds;
            return _hasBounds && IsUsableBounds(_bounds);
        }

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
            var thickness = Math.Max(1, pen?.Thickness ?? 1);
            var minX = Math.Min(point0.X, point1.X) - thickness / 2;
            var minY = Math.Min(point0.Y, point1.Y) - thickness / 2;
            var maxX = Math.Max(point0.X, point1.X) + thickness / 2;
            var maxY = Math.Max(point0.Y, point1.Y) + thickness / 2;
            AddBounds(new WpfReplayRect(minX, minY, maxX - minX, maxY - minY));
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
            AddBounds(InflateForPen(FromMediaRect(rectangle), pen));
        }

        public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            AddBounds(InflateForPen(FromMediaRect(rectangle), pen));
        }

        public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
        {
            AddBounds(InflateForPen(new WpfReplayRect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2), pen));
        }

        public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
            AddBounds(InflateForPen(FromMediaRect(geometry.Bounds), pen));
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
            AddBounds(FromMediaRect(rectangle));
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle, Rect sourceRectangle)
        {
            AddBounds(FromMediaRect(rectangle));
        }

        public void DrawText(MediaFormattedText formattedText, Point origin)
        {
        }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
            if (TryGetGlyphRunBounds(glyphRun, out var bounds))
            {
                AddBounds(bounds);
            }
        }

        public void PushClip(MediaGeometry clipGeometry)
        {
            var clip = TransformBounds(FromMediaRect(clipGeometry.Bounds), _transformStack.Peek());
            var currentClip = _clipStack.Peek();
            _clipStack.Push(currentClip.HasValue ? IntersectBounds(currentClip.Value, clip) : clip);
            _pushStack.Push(PushKind.Clip);
        }

        public void PushOpacity(double opacity)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushTransform(MediaTransform transform)
        {
            var nativeTransform = WpfReflectionResourceResolver.TryAdaptTransformMatrix(transform, out var adaptedTransform)
                ? adaptedTransform
                : System.Numerics.Matrix4x4.Identity;
            _transformStack.Push(nativeTransform * _transformStack.Peek());
            _pushStack.Push(PushKind.Transform);
        }

        public void PushNoOpScope()
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushGuidelineSet()
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushGuidelineY1(double coordinate)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void Pop()
        {
            if (_pushStack.Count == 0)
            {
                return;
            }

            var kind = _pushStack.Pop();
            if (kind == PushKind.Transform && _transformStack.Count > 1)
            {
                _transformStack.Pop();
            }
            else if (kind == PushKind.Clip && _clipStack.Count > 1)
            {
                _clipStack.Pop();
            }
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }

        private void AddBounds(WpfReplayRect bounds)
        {
            var transformed = TransformBounds(bounds, _transformStack.Peek());
            var clipped = ApplyClip(transformed, _clipStack.Peek());
            if (!clipped.HasValue)
            {
                return;
            }

            _bounds = _hasBounds ? UnionBounds(_bounds, clipped.Value) : clipped.Value;
            _hasBounds = true;
        }

        private static WpfReplayRect InflateForPen(WpfReplayRect bounds, MediaPen? pen)
        {
            if (pen == null || !IsUsableBounds(bounds))
            {
                return bounds;
            }

            var halfThickness = Math.Max(0, pen.Thickness) / 2;
            return new WpfReplayRect(
                bounds.X - halfThickness,
                bounds.Y - halfThickness,
                bounds.Width + halfThickness * 2,
                bounds.Height + halfThickness * 2);
        }

        private static bool TryGetGlyphRunBounds(MediaGlyphRun glyphRun, out WpfReplayRect bounds)
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

            bounds = TransformBounds(
                new WpfReplayRect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)),
                glyphRun.Transform);
            return IsUsableBounds(bounds);
        }

        private static WpfReplayRect FromMediaRect(Rect bounds)
        {
            return new WpfReplayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
    }

    private sealed class VisualGuidelineSet : IPortableGuidelineSetSource
    {
        private readonly double[] _guidelinesX;
        private readonly double[] _guidelinesY;

        public VisualGuidelineSet(double[] guidelinesX, double[] guidelinesY)
        {
            _guidelinesX = guidelinesX;
            _guidelinesY = guidelinesY;
        }

        public bool TryGetPortableGuidelineSet(out PortableGuidelineSet guidelineSet)
        {
            guidelineSet = new PortableGuidelineSet(
                isFrozen: true,
                isDynamic: true,
                _guidelinesX,
                _guidelinesY);
            return true;
        }
    }

    private sealed class ReplayStats
    {
        private int _renderDataRecordCount;
        private int _renderDataAppliedCount;
        private int _renderDataSkippedCount;
        private int _renderDataUnsupportedCount;

        public int VisualCount { get; set; }

        public int ContentCount { get; set; }

        public int ChildEdgeCount { get; set; }

        public int UnsupportedContentCount { get; set; }

        public int UnsupportedVisualStateCount { get; set; }

        public void AddRenderData(WpfMilDecodeResult result)
        {
            _renderDataRecordCount += result.RecordCount;
            _renderDataAppliedCount += result.AppliedCount;
            _renderDataSkippedCount += result.SkippedCount;
            _renderDataUnsupportedCount += result.UnsupportedCount;
        }

        public WpfVisualReplayResult ToResult()
        {
            return new WpfVisualReplayResult(
                VisualCount,
                ContentCount,
                ChildEdgeCount,
                UnsupportedContentCount,
                UnsupportedVisualStateCount,
                new WpfMilDecodeResult(
                    _renderDataRecordCount,
                    _renderDataAppliedCount,
                    _renderDataSkippedCount,
                    _renderDataUnsupportedCount));
        }
    }
}

public readonly record struct WpfVisualReplayResult(
    int VisualCount,
    int ContentCount,
    int ChildEdgeCount,
    int UnsupportedContentCount,
    int UnsupportedVisualStateCount,
    WpfMilDecodeResult RenderData);
