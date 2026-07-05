using System;
using System.Buffers;
using System.Numerics;
using ProGPU.Wpf.Interop;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using System.Windows.Media.ProGPU.Platform;

namespace System.Windows.Media.ProGPU;

internal sealed class WpfPortablePopupBridge : IDisposable
{
    private const int HitTestOwnerBufferCapacity = 64;

    private readonly ProGpuWpfWindowHost _host;
    private readonly IPortablePresentationSourceHost _source;
    private Func<double, double, object?>? _hitTestOverrideHandler;
    private Func<double, double, object?[]?>? _hitTestAllOverrideHandler;
    private PortableHitTestAllBufferOverride? _hitTestAllBufferOverrideHandler;
    private Func<double, double, double, double, object?[]?>? _hitTestBoundsOverrideHandler;
    private PortableGeometryHitTestBufferOverride? _hitTestBoundsBufferOverrideHandler;
    private Func<double, double, double, double, object?[]?>? _hitTestEllipseBoundsOverrideHandler;
    private PortableGeometryHitTestBufferOverride? _hitTestEllipseBoundsBufferOverrideHandler;
    private bool _isDisposed;

    private WpfPortablePopupBridge(
        ProGpuWpfWindowHost host,
        IPortablePresentationSourceHost source,
        int x,
        int y)
    {
        _host = host;
        _source = source;
        X = x;
        Y = y;
        Width = 1;
        Height = 1;
        IsHitTestable = true;
    }

    public object Source => _source;

    public object? RootVisual => _source.RootVisual;

    public IntPtr Handle => _source.Handle;

    public int X { get; private set; }

    public int Y { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsVisible { get; private set; }

    public bool IsHitTestable { get; private set; }

    internal static Func<double, double, IPortablePresentationSourceHost> PortablePresentationSourceFactory { get; set; } =
        PortablePresentationSourceHost.Create;

    private double LogicalX => X / _host.CurrentDpiScaleX;

    private double LogicalY => Y / _host.CurrentDpiScaleY;

    public static bool TryCreate(
        ProGpuWpfWindowHost host,
        PortablePopupCreateRequest request,
        out WpfPortablePopupBridge? bridge)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);

        IPortablePresentationSourceHost source;
        try
        {
            source = PortablePresentationSourceFactory(
                host.CurrentDpiScaleX,
                host.CurrentDpiScaleY);
        }
        catch (PlatformNotSupportedException)
        {
            bridge = null;
            return false;
        }

        bridge = new WpfPortablePopupBridge(host, source, request.X, request.Y);
        bridge.SubscribeToSource();
        bridge.InstallHitTestOverrides();
        return true;
    }

    public bool TrySetPosition(int x, int y)
    {
        ThrowIfDisposed();
        if (X == x && Y == y)
        {
            return false;
        }

        X = x;
        Y = y;
        RequestRender();
        return true;
    }

    public bool TrySetSize(int width, int height)
    {
        ThrowIfDisposed();
        int normalizedWidth = Math.Max(1, width);
        int normalizedHeight = Math.Max(1, height);
        if (Width == normalizedWidth && Height == normalizedHeight)
        {
            return false;
        }

        Width = normalizedWidth;
        Height = normalizedHeight;
        _source.SetClientSize(normalizedWidth, normalizedHeight);
        RequestRender();
        return true;
    }

    public bool TryShow()
    {
        ThrowIfDisposed();
        if (IsVisible)
        {
            return false;
        }

        IsVisible = true;
        RequestRender();
        return true;
    }

    public bool TryHide()
    {
        ThrowIfDisposed();
        if (!IsVisible)
        {
            return false;
        }

        IsVisible = false;
        RequestRender();
        return true;
    }

    public bool TrySetHitTestable(bool hitTestable)
    {
        ThrowIfDisposed();
        if (IsHitTestable == hitTestable)
        {
            return false;
        }

        IsHitTestable = hitTestable;
        RequestRender();
        return true;
    }

    public WpfVisualReplayResult Replay(
        ProGpuWpfCompositionTarget target,
        ProGpuWpfDrawingFrame drawingFrame,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(drawingFrame);

        object? rootVisual = RootVisual;
        if (!IsVisible || rootVisual == null)
        {
            return default;
        }

        using var sink = new ProGpuRetainedCompositionCommandSink(
            drawingFrame,
            target.Context,
            target.Viewport3DTextureCache);

        var nativeTransformSink = (IWpfNativeTransformCommandSink)sink;
        nativeTransformSink.PushNativeTransform(Matrix4x4.CreateTranslation(
            (float)LogicalX,
            (float)LogicalY,
            0f));
        try
        {
            return target.ReplayVisualSubtree(rootVisual, sink, resources, imageSourceAdapter);
        }
        finally
        {
            sink.Pop();
        }
    }

    public bool ContainsHostPoint(double x, double y)
    {
        if (!IsVisible || !IsHitTestable)
        {
            return false;
        }

        double logicalX = LogicalX;
        double logicalY = LogicalY;
        return x >= logicalX &&
            y >= logicalY &&
            x <= logicalX + Width &&
            y <= logicalY + Height;
    }

    public bool TryProcessInput(WpfInputEventArgs input)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);

        if (!IsVisible || !IsHitTestable)
        {
            return false;
        }

        if (IsPointerInput(input.Kind) && !ContainsHostPoint(input.X, input.Y))
        {
            return false;
        }

        if (!PortableWpfServiceRegistry.TryGetWindowActivationService(
                PortableWpfServiceKey.PresentationFramework,
                out var activationService))
        {
            return false;
        }

        var portableInput = CreatePortableWindowInputEvent(input);
        if (!activationService.TryProcessPresentationSourceInputEvent(Source, portableInput))
        {
            return false;
        }

        input.Handled = portableInput.Handled;
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _source.RenderRequested -= OnSourceRenderRequested;
        _source.CursorRequested -= OnSourceCursorRequested;

        if (_hitTestOverrideHandler != null &&
            ReferenceEquals(_source.HitTestOverride, _hitTestOverrideHandler))
        {
            _source.HitTestOverride = null;
        }

        if (_hitTestAllOverrideHandler != null &&
            ReferenceEquals(_source.HitTestAllOverride, _hitTestAllOverrideHandler))
        {
            _source.HitTestAllOverride = null;
        }

        if (_hitTestAllBufferOverrideHandler != null &&
            ReferenceEquals(_source.HitTestAllBufferOverride, _hitTestAllBufferOverrideHandler))
        {
            _source.HitTestAllBufferOverride = null;
        }

        if (_hitTestBoundsOverrideHandler != null &&
            ReferenceEquals(_source.HitTestBoundsOverride, _hitTestBoundsOverrideHandler))
        {
            _source.HitTestBoundsOverride = null;
        }

        if (_hitTestBoundsBufferOverrideHandler != null &&
            ReferenceEquals(_source.HitTestBoundsBufferOverride, _hitTestBoundsBufferOverrideHandler))
        {
            _source.HitTestBoundsBufferOverride = null;
        }

        if (_hitTestEllipseBoundsOverrideHandler != null &&
            ReferenceEquals(_source.HitTestEllipseBoundsOverride, _hitTestEllipseBoundsOverrideHandler))
        {
            _source.HitTestEllipseBoundsOverride = null;
        }

        if (_hitTestEllipseBoundsBufferOverrideHandler != null &&
            ReferenceEquals(_source.HitTestEllipseBoundsBufferOverride, _hitTestEllipseBoundsBufferOverrideHandler))
        {
            _source.HitTestEllipseBoundsBufferOverride = null;
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private void SubscribeToSource()
    {
        _source.RenderRequested += OnSourceRenderRequested;
        _source.CursorRequested += OnSourceCursorRequested;
    }

    private void InstallHitTestOverrides()
    {
        _hitTestOverrideHandler = TryHitTestOwner;
        _hitTestAllOverrideHandler = HitTestOwners;
        _hitTestAllBufferOverrideHandler = HitTestOwners;
        _hitTestBoundsOverrideHandler = HitTestBoundsOwners;
        _hitTestBoundsBufferOverrideHandler = HitTestBoundsOwners;
        _hitTestEllipseBoundsOverrideHandler = HitTestEllipseBoundsOwners;
        _hitTestEllipseBoundsBufferOverrideHandler = HitTestEllipseBoundsOwners;

        _source.HitTestOverride = _hitTestOverrideHandler;
        _source.HitTestAllOverride = _hitTestAllOverrideHandler;
        _source.HitTestAllBufferOverride = _hitTestAllBufferOverrideHandler;
        _source.HitTestBoundsOverride = _hitTestBoundsOverrideHandler;
        _source.HitTestBoundsBufferOverride = _hitTestBoundsBufferOverrideHandler;
        _source.HitTestEllipseBoundsOverride = _hitTestEllipseBoundsOverrideHandler;
        _source.HitTestEllipseBoundsBufferOverride = _hitTestEllipseBoundsBufferOverrideHandler;
    }

    private void OnSourceRenderRequested(object? sender, EventArgs e)
    {
        if (!_isDisposed)
        {
            RequestRender();
        }
    }

    private void OnSourceCursorRequested(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        _host.ApplyPortableCursor(WpfPortablePresentationSourceBridge.ToWpfCursor(
            _source.RequestedCursorName ?? _source.RequestedCursor?.ToString()));
    }

    private object? TryHitTestOwner(double rootX, double rootY)
    {
        object?[] ownerBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (HitTestOwners(rootX, rootY, ownerBuffer, out int ownerCount))
            {
                ReadOnlySpan<object?> owners = ownerBuffer.AsSpan(0, ownerCount);
                if (WpfPortablePresentationSourceBridge.TrySelectPointerInputOwner(owners, out object? selectedOwner))
                {
                    return selectedOwner;
                }

                return _host.HasGpuHitTestCache ? Source : null;
            }

            return _host.HasGpuHitTestCache ? Source : null;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }
    }

    private object?[]? HitTestOwners(double rootX, double rootY)
    {
        object?[] ownerBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (!HitTestOwners(rootX, rootY, ownerBuffer, out int ownerCount))
            {
                return _host.HasGpuHitTestCache ? Array.Empty<object>() : null;
            }

            if (ownerCount == 0)
            {
                return Array.Empty<object?>();
            }

            var owners = new object?[ownerCount];
            ownerBuffer.AsSpan(0, ownerCount).CopyTo(owners);
            return owners;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }
    }

    private bool HitTestOwners(double rootX, double rootY, Span<object?> owners, out int ownerCount)
    {
        if (!IsVisible ||
            !_host.TryHitTestOwners(LogicalX + rootX, LogicalY + rootY, owners, out ownerCount))
        {
            ownerCount = 0;
            return _host.HasGpuHitTestCache;
        }

        ownerCount = WpfPortablePresentationSourceBridge.FilterTransparentPointerOverlays(owners[..ownerCount]);
        return true;
    }

    private object?[]? HitTestBoundsOwners(double minX, double minY, double maxX, double maxY)
    {
        return HitTestGeometryOwners(minX, minY, maxX, maxY, isEllipse: false);
    }

    private bool HitTestBoundsOwners(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)
    {
        if (_host.TryQueryHitTestBoundsCandidates(
            LogicalX + minX,
            LogicalY + minY,
            LogicalX + maxX,
            LogicalY + maxY,
            candidates,
            out candidateCount))
        {
            return true;
        }

        candidateCount = 0;
        return _host.HasGpuHitTestCache;
    }

    private object?[]? HitTestEllipseBoundsOwners(double minX, double minY, double maxX, double maxY)
    {
        return HitTestGeometryOwners(minX, minY, maxX, maxY, isEllipse: true);
    }

    private bool HitTestEllipseBoundsOwners(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)
    {
        if (_host.TryQueryHitTestEllipseCandidates(
            LogicalX + minX,
            LogicalY + minY,
            LogicalX + maxX,
            LogicalY + maxY,
            candidates,
            out candidateCount))
        {
            return true;
        }

        candidateCount = 0;
        return _host.HasGpuHitTestCache;
    }

    private object?[]? HitTestGeometryOwners(double minX, double minY, double maxX, double maxY, bool isEllipse)
    {
        object?[] candidateBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            bool hit = isEllipse
                ? HitTestEllipseBoundsOwners(minX, minY, maxX, maxY, candidateBuffer, out int candidateCount)
                : HitTestBoundsOwners(minX, minY, maxX, maxY, candidateBuffer, out candidateCount);
            if (!hit)
            {
                return null;
            }

            if (candidateCount == 0)
            {
                return Array.Empty<object>();
            }

            var candidates = new object?[candidateCount];
            candidateBuffer.AsSpan(0, candidateCount).CopyTo(candidates);
            return candidates;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(candidateBuffer, clearArray: true);
        }
    }

    private PortableWindowInputEvent CreatePortableWindowInputEvent(WpfInputEventArgs input)
    {
        return new PortableWindowInputEvent(
            (int)input.Kind,
            input.Key,
            input.ScanCode,
            input.Character,
            input.X - LogicalX,
            input.Y - LogicalY,
            input.DeltaX,
            input.DeltaY,
            (int)input.Button,
            (int)input.Modifiers);
    }

    private void RequestRender()
    {
        _host.RequestRenderAndWakeNativeLoop();
    }

    private static bool IsPointerInput(WpfInputEventKind kind)
    {
        return kind is WpfInputEventKind.MouseMove or
            WpfInputEventKind.MouseDown or
            WpfInputEventKind.MouseUp or
            WpfInputEventKind.MouseWheel;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
