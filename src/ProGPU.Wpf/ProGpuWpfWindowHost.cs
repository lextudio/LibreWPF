using System;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using System.Windows.Media.ProGPU.Platform;
using MediaDrawingContext = System.Windows.Media.DrawingContext;

namespace System.Windows.Media.ProGPU;

public unsafe sealed class ProGpuWpfWindowHost : IDisposable
{
    private readonly ProGpuWpfWindowOptions _options;
    private IWindow? _window;
    private ProGpuWpfCompositionTarget? _target;
    private IDisposable? _inputSubscription;
    private IWpfInputService? _attachedInputService;
    private IDisposable? _dragDropSubscription;
    private IWpfDragDropService? _attachedDragDropService;
    private IDisposable? _windowEventSubscription;
    private IWpfWindowEventService? _attachedWindowEventService;
    private object? _wpfRootVisual;
    private bool _isDisposed;

    public ProGpuWpfWindowHost(ProGpuWpfWindowOptions? options = null)
    {
        _options = options ?? new ProGpuWpfWindowOptions();
    }

    public event EventHandler<ProGpuWpfFrameEventArgs>? Render;

    public event EventHandler<WpfInputEventArgs>? InputReceived;

    public event EventHandler<WpfDragDropEventArgs>? DragDropReceived;

    public event EventHandler<WpfWindowEventArgs>? WindowEventReceived;

    public IWindow? SilkWindow => _window;

    public ProGpuWpfCompositionTarget? CompositionTarget => _target;

    public IWpfPlatformServices PlatformServices { get; set; } = CrossPlatformWpfPlatformServices.Instance;

    public object? WpfRootVisual
    {
        get => _wpfRootVisual;
        set
        {
            if (ReferenceEquals(_wpfRootVisual, value))
            {
                return;
            }

            _wpfRootVisual = value;
            WpfRenderScheduler.RequestRender();
        }
    }

    public IWpfMilResourceResolver? WpfResourceResolver { get; set; }

    public IWpfImageSourceAdapter? WpfImageSourceAdapter { get; set; } = new WpfBitmapSourceImageAdapter();

    public IWpfRenderScheduler WpfRenderScheduler { get; set; } = new CoalescingWpfRenderScheduler();

    public WpfVisualReplayResult LastVisualReplayResult { get; private set; }

    public WpfCompositionDrawingContextResult LastSourceDrawingResult { get; private set; }

    public bool IsWpfRootVisualDirty => _target?.WpfInvalidationTracker.IsDirty ?? false;

    public Action<MediaDrawingContext, ProGpuWpfFrameEventArgs>? Draw { get; set; }

    public Action<WpfCompositionDrawingContext, ProGpuWpfFrameEventArgs>? WpfDraw { get; set; }

    internal Func<ProGpuWpfDrawingFrame, IWpfImageSourceAdapter?, IDisposable?> RenderDataSinkProviderRegistrationFactory { get; set; } = RegisterDefaultRenderDataSinkProvider;

    public void Run()
    {
        ThrowIfDisposed();
        EnsureWindow();
        _window!.Run();
    }

    public void Initialize()
    {
        ThrowIfDisposed();
        EnsureWindow();
        _window!.Initialize();
    }

    public void DoEvents()
    {
        ThrowIfDisposed();
        ProcessDispatcherQueueCore();
        EnsureWindow();
        _window!.DoEvents();
        _window.DoUpdate();
        _window.DoRender();
        ProcessDispatcherQueueCore();
    }

    public void Close()
    {
        _window?.Close();
    }

    public bool SetCursor(WpfCursor cursor)
    {
        ThrowIfDisposed();

        return _window != null && PlatformServices.Cursors.SetCursor(_window, cursor);
    }

    public bool ProcessDispatcherQueue()
    {
        ThrowIfDisposed();
        return ProcessDispatcherQueueCore();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_window != null)
        {
            _window.Load -= OnLoad;
            _window.Render -= OnRender;
            _window.Resize -= OnResize;
            _window.Closing -= OnClosing;
        }

        DetachInputService();
        DetachDragDropService();
        DetachWindowEventService();
        DisposeTarget();
        _window?.Dispose();

        _target = null;
        _window = null;
        _isDisposed = true;
    }

    private void EnsureWindow()
    {
        if (_window != null)
        {
            return;
        }

        var windowOptions = WindowOptions.Default;
        windowOptions.API = GraphicsAPI.None;
        windowOptions.Size = new Vector2D<int>(_options.Width, _options.Height);
        windowOptions.Title = _options.Title;
        windowOptions.VSync = _options.VSync;

        _window = Window.Create(windowOptions);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Resize += OnResize;
        _window.Closing += OnClosing;
    }

    private void OnLoad()
    {
        if (_window == null)
        {
            return;
        }

        _target = ProGpuWpfCompositionTarget.CreateForWindow(_window);
        _target.RenderInvalidated += OnCompositionTargetRenderInvalidated;
        _target.Context.VSync = _options.VSync;
        AttachInputService();
        AttachDragDropService();
        AttachWindowEventService();
        WpfRenderScheduler.RequestRender();
    }

    private void OnResize(Vector2D<int> size)
    {
        if (_target == null || _window == null)
        {
            return;
        }

        var framebufferSize = _window.FramebufferSize;
        _target.Context.ConfigureSwapChain(
            (uint)Math.Max(1, framebufferSize.X),
            (uint)Math.Max(1, framebufferSize.Y));
        _target.RootVisual.Invalidate();
        WpfRenderScheduler.RequestRender();
    }

    private void OnRender(double deltaSeconds)
    {
        ProcessDispatcherQueueCore();

        if (_target == null || _window == null || _target.Context.Surface == null)
        {
            return;
        }

        var framebufferSize = _window.FramebufferSize;
        var pixelWidth = (uint)Math.Max(1, framebufferSize.X);
        var pixelHeight = (uint)Math.Max(1, framebufferSize.Y);
        var logicalWidth = Math.Max(1, _window.Size.X);
        var dpiScale = pixelWidth / (double)logicalWidth;

        _target.Context.ReconfigureIfNeeded(pixelWidth, pixelHeight);

        var drawingFrame = _target.BeginDrawingFrame(pixelWidth, pixelHeight);

        using (IDisposable? renderDataSinkProviderRegistration = RegisterRenderDataSinkProvider(drawingFrame))
        using (var drawingContext = drawingFrame.OpenDrawingContext())
        {
            var args = new ProGpuWpfFrameEventArgs(
                drawingContext,
                pixelWidth,
                pixelHeight,
                deltaSeconds,
                dpiScale,
                drawingFrame);

            if (_wpfRootVisual != null)
            {
                using var sink = new ProGpuCompositionCommandSink(
                    drawingContext,
                    _target.Context,
                    _target.Viewport3DTextureCache);
                LastVisualReplayResult = _target.ReplayVisualSubtree(
                    _wpfRootVisual,
                    sink,
                    WpfResourceResolver,
                    WpfImageSourceAdapter);
            }
            else
            {
                _target.WpfInvalidationTracker.Detach();
                LastVisualReplayResult = default;
            }

            if (WpfDraw != null)
            {
                using var sourceDrawingContext = drawingFrame.OpenCompositionDrawingContext();
                InvokeSourceDraw(sourceDrawingContext, args);
            }
            else
            {
                LastSourceDrawingResult = default;
            }

            Draw?.Invoke(drawingContext, args);
            Render?.Invoke(this, args);
            WpfRenderScheduler.ConsumeRenderRequest();
        }

        Present(pixelWidth, pixelHeight);
    }

    private void Present(uint pixelWidth, uint pixelHeight)
    {
        if (_target == null)
        {
            return;
        }

        var surfaceTexture = new SurfaceTexture();
        _target.Context.Wgpu.SurfaceGetCurrentTexture(_target.Context.Surface, &surfaceTexture);

        if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success)
        {
            return;
        }

        var viewDescriptor = new TextureViewDescriptor
        {
            Format = _target.Context.SwapChainFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        var targetView = _target.Context.Wgpu.TextureCreateView(surfaceTexture.Texture, &viewDescriptor);
        try
        {
            _target.Render(pixelWidth, pixelHeight, targetView);
            _target.Context.Wgpu.SurfacePresent(_target.Context.Surface);
        }
        finally
        {
            if (targetView != null)
            {
                _target.Context.Wgpu.TextureViewRelease(targetView);
            }
        }
    }

    private void OnClosing()
    {
        DisposeTarget();
    }

    private void OnCompositionTargetRenderInvalidated(object? sender, EventArgs e)
    {
        WpfRenderScheduler.RequestRender();
    }

    internal IDisposable? RegisterRenderDataSinkProvider(ProGpuWpfDrawingFrame drawingFrame)
    {
        ArgumentNullException.ThrowIfNull(drawingFrame);

        return RenderDataSinkProviderRegistrationFactory(drawingFrame, WpfImageSourceAdapter);
    }

    internal void InvokeSourceDraw(
        WpfCompositionDrawingContext sourceDrawingContext,
        ProGpuWpfFrameEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(sourceDrawingContext);
        ArgumentNullException.ThrowIfNull(args);

        if (WpfDraw == null)
        {
            LastSourceDrawingResult = default;
            return;
        }

        try
        {
            WpfDraw(sourceDrawingContext, args);
        }
        finally
        {
            sourceDrawingContext.Close();
            LastSourceDrawingResult = sourceDrawingContext.Result;
        }
    }

    private void AttachInputService()
    {
        if (_window == null)
        {
            return;
        }

        DetachInputService();

        var input = PlatformServices.Input;
        try
        {
            input.InputReceived += OnPlatformInputReceived;
            _inputSubscription = input.Attach(_window);
            _attachedInputService = input;
        }
        catch (PlatformNotSupportedException)
        {
            input.InputReceived -= OnPlatformInputReceived;
            _inputSubscription = null;
            _attachedInputService = null;
        }
    }

    private void DetachInputService()
    {
        _inputSubscription?.Dispose();
        _inputSubscription = null;

        if (_attachedInputService != null)
        {
            _attachedInputService.InputReceived -= OnPlatformInputReceived;
            _attachedInputService = null;
        }
    }

    private void OnPlatformInputReceived(object? sender, WpfInputEventArgs e)
    {
        InputReceived?.Invoke(this, e);
        WpfRenderScheduler.RequestRender();
    }

    private void AttachDragDropService()
    {
        if (_window == null)
        {
            return;
        }

        DetachDragDropService();

        var dragDrop = PlatformServices.DragDrop;
        try
        {
            dragDrop.DragDropReceived += OnPlatformDragDropReceived;
            _dragDropSubscription = dragDrop.Attach(_window);
            _attachedDragDropService = dragDrop;
        }
        catch (PlatformNotSupportedException)
        {
            dragDrop.DragDropReceived -= OnPlatformDragDropReceived;
            _dragDropSubscription = null;
            _attachedDragDropService = null;
        }
    }

    private void DetachDragDropService()
    {
        _dragDropSubscription?.Dispose();
        _dragDropSubscription = null;

        if (_attachedDragDropService != null)
        {
            _attachedDragDropService.DragDropReceived -= OnPlatformDragDropReceived;
            _attachedDragDropService = null;
        }
    }

    private void OnPlatformDragDropReceived(object? sender, WpfDragDropEventArgs e)
    {
        DragDropReceived?.Invoke(this, e);
        WpfRenderScheduler.RequestRender();
    }

    private void AttachWindowEventService()
    {
        if (_window == null)
        {
            return;
        }

        DetachWindowEventService();

        var windowEvents = PlatformServices.WindowEvents;
        try
        {
            windowEvents.WindowEventReceived += OnPlatformWindowEventReceived;
            _windowEventSubscription = windowEvents.Attach(_window);
            _attachedWindowEventService = windowEvents;
        }
        catch (PlatformNotSupportedException)
        {
            windowEvents.WindowEventReceived -= OnPlatformWindowEventReceived;
            _windowEventSubscription = null;
            _attachedWindowEventService = null;
        }
    }

    private void DetachWindowEventService()
    {
        _windowEventSubscription?.Dispose();
        _windowEventSubscription = null;

        if (_attachedWindowEventService != null)
        {
            _attachedWindowEventService.WindowEventReceived -= OnPlatformWindowEventReceived;
            _attachedWindowEventService = null;
        }
    }

    private void OnPlatformWindowEventReceived(object? sender, WpfWindowEventArgs e)
    {
        WindowEventReceived?.Invoke(this, e);
        WpfRenderScheduler.RequestRender();
    }

    private bool ProcessDispatcherQueueCore()
    {
        try
        {
            return PlatformServices.Dispatcher.ProcessPending();
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private void DisposeTarget()
    {
        DetachInputService();
        DetachDragDropService();
        DetachWindowEventService();

        if (_target == null)
        {
            return;
        }

        _target.RenderInvalidated -= OnCompositionTargetRenderInvalidated;
        _target.Dispose();
        _target = null;
        WpfRenderScheduler.Reset();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private static IDisposable? RegisterDefaultRenderDataSinkProvider(
        ProGpuWpfDrawingFrame drawingFrame,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        return drawingFrame.TryRegisterRenderDataSinkProvider(imageSourceAdapter, out IDisposable? registration)
            ? registration
            : null;
    }
}
