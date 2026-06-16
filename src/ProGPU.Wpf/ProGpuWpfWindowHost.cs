using System;
using System.Reflection;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using System.Windows.Media.ProGPU.Platform;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using SilkWindowState = Silk.NET.Windowing.WindowState;

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
    private IWpfDispatcherService? _attachedDispatcherService;
    private IWpfPlatformServices _platformServices = CrossPlatformWpfPlatformServices.Instance;
    private IWpfRenderScheduler _wpfRenderScheduler;
    private WpfPortablePresentationSourceBridge? _portablePresentationSourceBridge;
    private object? _wpfRootVisual;
    private double _portablePresentationSourceDpiScaleX = double.NaN;
    private double _portablePresentationSourceDpiScaleY = double.NaN;
    private bool _isDisposed;
    private bool _hasPresentedFrame;
    private bool _ownsRenderScheduler;
    private bool _isRendering;
    private bool _isProcessingRenderSchedulerWakeup;
    private bool _isProcessingDispatcherWorkWakeup;
    private bool _isHostVisible;
    private ProGpuWpfWindowState _windowState;
    private string _windowTitle;
    private int _clientWidth;
    private int _clientHeight;

    public ProGpuWpfWindowHost(ProGpuWpfWindowOptions? options = null)
    {
        _options = options ?? new ProGpuWpfWindowOptions();
        _isHostVisible = _options.IsVisible;
        _windowState = _options.WindowState;
        _windowTitle = _options.Title;
        _clientWidth = Math.Max(1, _options.Width);
        _clientHeight = Math.Max(1, _options.Height);
        _wpfRenderScheduler = CreateDefaultRenderScheduler(_platformServices, out _ownsRenderScheduler);
        AttachDispatcherService(_platformServices.Dispatcher);
        AttachRenderScheduler(_wpfRenderScheduler);
    }

    public event EventHandler<ProGpuWpfFrameEventArgs>? Render;

    internal event EventHandler? RenderWakeupRequested;

    public event EventHandler<WpfInputEventArgs>? InputReceived;

    public event EventHandler<WpfDragDropEventArgs>? DragDropReceived;

    public event EventHandler<WpfWindowEventArgs>? WindowEventReceived;

    public event EventHandler<ProGpuWpfWindowClosingEventArgs>? Closing;

    public IWindow? SilkWindow => _window;

    public ProGpuWpfCompositionTarget? CompositionTarget => _target;

    public bool IsVisible => _window?.IsVisible ?? _isHostVisible;

    public ProGpuWpfWindowState WindowState => _windowState;

    public string Title => _window?.Title ?? _windowTitle;

    public int Width => _window?.Size.X ?? _clientWidth;

    public int Height => _window?.Size.Y ?? _clientHeight;

    public object? PortablePresentationSource => _portablePresentationSourceBridge?.Source;

    public WpfPortablePresentationSourceBridge? PortablePresentationSourceBridge => _portablePresentationSourceBridge;

    public IWpfPlatformServices PlatformServices
    {
        get => _platformServices;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            DetachDispatcherService();
            _platformServices = value;
            AttachDispatcherService(_platformServices.Dispatcher);
            if (_ownsRenderScheduler)
            {
                ReplaceRenderScheduler(
                    CreateDefaultRenderScheduler(_platformServices, out var ownsScheduler),
                    ownsScheduler);
            }
        }
    }

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

    public IWpfRenderScheduler WpfRenderScheduler
    {
        get => _wpfRenderScheduler;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ReplaceRenderScheduler(value, ownsScheduler: false);
        }
    }

    public WpfVisualReplayResult LastVisualReplayResult { get; private set; }

    public WpfCompositionDrawingContextResult LastSourceDrawingResult { get; private set; }

    public bool IsWpfRootVisualDirty => _target?.WpfInvalidationTracker.IsDirty ?? false;

    public bool EnableFrameCoalescing { get; set; } = true;

    public bool HasPresentedFrame => _hasPresentedFrame;

    public ProGpuWpfFrameState LastPresentedFrameState { get; private set; }

    public long SkippedFrameCount { get; private set; }

    public long RetainedWpfReplaySkipCount { get; private set; }

    public long RetainedWpfBranchReplayCount { get; private set; }

    internal long RenderSchedulerWakeupCount { get; private set; }

    internal long DispatcherWakeupCount { get; private set; }

    internal long NativeLoopWakeupCount { get; private set; }

    public Action<MediaDrawingContext, ProGpuWpfFrameEventArgs>? Draw { get; set; }

    public Action<WpfCompositionDrawingContext, ProGpuWpfFrameEventArgs>? WpfDraw { get; set; }

    internal Func<ProGpuWpfDrawingFrame, IWpfImageSourceAdapter?, IDisposable?> RenderDataSinkProviderRegistrationFactory { get; set; } = RegisterDefaultRenderDataSinkProvider;

    public void Run()
    {
        ThrowIfDisposed();
        _isHostVisible = true;
        EnsureWindow();
        _window!.IsVisible = true;
        _window!.Run();
    }

    public void Initialize()
    {
        ThrowIfDisposed();
        ShowCore(requestRenderWhenInitialized: false);
    }

    public void Show()
    {
        ThrowIfDisposed();
        ShowCore(requestRenderWhenInitialized: true);
    }

    public void Hide()
    {
        ThrowIfDisposed();

        _isHostVisible = false;
        if (_window != null)
        {
            _window.IsVisible = false;
        }

        WpfRenderScheduler.RequestRender();
    }

    public void SetWindowState(ProGpuWpfWindowState windowState)
    {
        ThrowIfDisposed();

        _windowState = windowState;
        if (_window != null)
        {
            _window.WindowState = ToSilkWindowState(windowState);
        }

        WpfRenderScheduler.RequestRender();
    }

    public void SetTitle(string title)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(title);

        _windowTitle = title;
        if (_window != null)
        {
            _window.Title = _windowTitle;
        }

        WpfRenderScheduler.RequestRender();
    }

    public void SetClientSize(int width, int height)
    {
        ThrowIfDisposed();

        _clientWidth = Math.Max(1, width);
        _clientHeight = Math.Max(1, height);
        if (_window != null)
        {
            _window.Size = new Vector2D<int>(_clientWidth, _clientHeight);
        }

        WpfRenderScheduler.RequestRender();
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

    public bool TryCreatePortablePresentationSource(
        Assembly presentationCoreAssembly,
        object? rootVisual = null,
        double dpiScaleX = 1.0,
        double dpiScaleY = 1.0)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(presentationCoreAssembly);

        if (!WpfPortablePresentationSourceBridge.TryCreate(
                this,
                presentationCoreAssembly,
                dpiScaleX,
                dpiScaleY,
                out WpfPortablePresentationSourceBridge? bridge))
        {
            return false;
        }

        AttachPortablePresentationSourceBridge(bridge!, dpiScaleX, dpiScaleY);
        if (rootVisual != null)
        {
            bridge!.RootVisual = rootVisual;
        }

        return true;
    }

    public bool TryBindPortablePresentationSource(object presentationSource)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(presentationSource);

        if (!WpfPortablePresentationSourceBridge.TryBind(
                this,
                presentationSource,
                out WpfPortablePresentationSourceBridge? bridge))
        {
            return false;
        }

        AttachPortablePresentationSourceBridge(bridge!, double.NaN, double.NaN);
        return true;
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
            _window.Update -= OnUpdate;
            _window.Render -= OnRender;
            _window.Resize -= OnResize;
            _window.Closing -= OnClosing;
        }

        DetachInputService();
        DetachDragDropService();
        DetachWindowEventService();
        DetachDispatcherService();
        DisposePortablePresentationSourceBridge();
        DisposeTarget();
        _window?.Dispose();
        DetachRenderScheduler(_wpfRenderScheduler);
        DisposeOwnedRenderScheduler();

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
        windowOptions.Size = new Vector2D<int>(_clientWidth, _clientHeight);
        windowOptions.Title = _windowTitle;
        windowOptions.VSync = _options.VSync;
        windowOptions.IsEventDriven = _options.IsEventDriven;
        windowOptions.IsVisible = _isHostVisible;
        windowOptions.WindowState = ToSilkWindowState(_windowState);

        _window = Window.Create(windowOptions);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
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
        _target.SceneRootVisual.Invalidate();
        _target.RootVisual.Invalidate();
        WpfRenderScheduler.RequestRender();
    }

    private void OnUpdate(double deltaSeconds)
    {
        TryProcessDispatcherWorkWakeup();
    }

    private void OnRender(double deltaSeconds)
    {
        if (_isRendering)
        {
            return;
        }

        _isRendering = true;
        try
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
            UpdatePortablePresentationSourceDpiScale(dpiScale, dpiScale);
            _target.DetectWpfSourceChanges();
            var frameState = CaptureFrameState(_target, pixelWidth, pixelHeight);

            if (!ShouldRenderFrame(frameState))
            {
                SkippedFrameCount++;
                return;
            }

            _target.Context.ReconfigureIfNeeded(pixelWidth, pixelHeight);

            object? wpfRootVisual = _wpfRootVisual;
            var shouldReplayWpfRootVisual = wpfRootVisual != null &&
                _target.ShouldReplayVisualSubtree(wpfRootVisual);
            var canReplayDirtyWpfBranches = wpfRootVisual != null &&
                shouldReplayWpfRootVisual &&
                _target.CanReplayDirtyRetainedVisualBranches(wpfRootVisual);
            var clearRetainedWpfVisualRoot = wpfRootVisual == null ||
                (shouldReplayWpfRootVisual && !canReplayDirtyWpfBranches);
            var drawingFrame = _target.BeginDrawingFrame(
                pixelWidth,
                pixelHeight,
                clearRetainedWpfVisualRoot);

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

                if (wpfRootVisual != null)
                {
                    if (shouldReplayWpfRootVisual)
                    {
                        if (canReplayDirtyWpfBranches &&
                            _target.TryReplayDirtyRetainedVisualBranches(
                                wpfRootVisual,
                                drawingFrame,
                                WpfResourceResolver,
                                WpfImageSourceAdapter,
                                out var branchReplayResult))
                        {
                            LastVisualReplayResult = branchReplayResult;
                            RetainedWpfBranchReplayCount++;
                        }
                        else
                        {
                            using var sink = new ProGpuRetainedCompositionCommandSink(
                                drawingFrame,
                                _target.Context,
                                _target.Viewport3DTextureCache);
                            LastVisualReplayResult = _target.ReplayVisualSubtree(
                                wpfRootVisual,
                                sink,
                                WpfResourceResolver,
                                WpfImageSourceAdapter);
                        }
                    }
                    else
                    {
                        RetainedWpfReplaySkipCount++;
                    }
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

            if (Present(pixelWidth, pixelHeight))
            {
                RecordPresentedFrame(CaptureFrameState(_target, pixelWidth, pixelHeight));
            }
        }
        finally
        {
            _isRendering = false;
        }
    }

    private bool Present(uint pixelWidth, uint pixelHeight)
    {
        if (_target == null)
        {
            return false;
        }

        var surfaceTexture = new SurfaceTexture();
        _target.Context.Wgpu.SurfaceGetCurrentTexture(_target.Context.Surface, &surfaceTexture);

        if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success)
        {
            return false;
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
            return true;
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
        var args = new ProGpuWpfWindowClosingEventArgs();
        Closing?.Invoke(this, args);
        if (args.Cancel)
        {
            if (_window != null)
            {
                _window.IsClosing = false;
            }

            _isHostVisible = true;
            WpfRenderScheduler.RequestRender();
            return;
        }

        _isHostVisible = false;
        DisposeTarget();
    }

    private void OnCompositionTargetRenderInvalidated(object? sender, EventArgs e)
    {
        WpfRenderScheduler.RequestRender();
    }

    internal bool ShouldRenderFrame(ProGpuWpfFrameState frameState)
    {
        if (!EnableFrameCoalescing)
        {
            return true;
        }

        if (HasExplicitFrameCallbacks)
        {
            return true;
        }

        if (WpfRenderScheduler.HasPendingRenderRequest)
        {
            return true;
        }

        return !_hasPresentedFrame || LastPresentedFrameState != frameState;
    }

    internal void RecordPresentedFrame(ProGpuWpfFrameState frameState)
    {
        LastPresentedFrameState = frameState;
        _hasPresentedFrame = true;
    }

    private bool HasExplicitFrameCallbacks => Draw != null || WpfDraw != null || Render != null;

    private static ProGpuWpfFrameState CaptureFrameState(
        ProGpuWpfCompositionTarget target,
        uint pixelWidth,
        uint pixelHeight)
    {
        return new ProGpuWpfFrameState(
            pixelWidth,
            pixelHeight,
            target.SceneChangeVersion,
            target.RetainedWpfChangeVersion,
            target.FlatDrawingChangeVersion,
            target.LastRetainedBranchInvalidationCount,
            target.LastRetainedBranchDirtySourceCount,
            target.LastRetainedBranchMappedSourceCount,
            target.LastRetainedBranchUnmappedSourceCount,
            target.LastRetainedBranchSharedWithCleanSourceVisualCount,
            target.LastRetainedBranchInvalidationUsedFallback);
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

    private void AttachDispatcherService(IWpfDispatcherService dispatcher)
    {
        DetachDispatcherService();
        dispatcher.WorkAvailable += OnDispatcherWorkAvailable;
        _attachedDispatcherService = dispatcher;
    }

    private void DetachDispatcherService()
    {
        if (_attachedDispatcherService != null)
        {
            _attachedDispatcherService.WorkAvailable -= OnDispatcherWorkAvailable;
            _attachedDispatcherService = null;
        }
    }

    private void OnDispatcherWorkAvailable(object? sender, EventArgs e)
    {
        DispatcherWakeupCount++;
        if (!TryProcessDispatcherWorkWakeup())
        {
            TryRequestNativeLoopWakeup();
        }
    }

    internal bool TryProcessDispatcherWorkWakeup()
    {
        if (_isRendering || _isProcessingDispatcherWorkWakeup)
        {
            return false;
        }

        try
        {
            if (!PlatformServices.Dispatcher.CheckAccess())
            {
                return false;
            }
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }

        _isProcessingDispatcherWorkWakeup = true;
        try
        {
            return ProcessDispatcherQueueCore();
        }
        finally
        {
            _isProcessingDispatcherWorkWakeup = false;
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
        LastPresentedFrameState = default;
        _hasPresentedFrame = false;
        SkippedFrameCount = 0;
        RetainedWpfReplaySkipCount = 0;
        RetainedWpfBranchReplayCount = 0;
    }

    private void ReplaceRenderScheduler(IWpfRenderScheduler scheduler, bool ownsScheduler)
    {
        if (ReferenceEquals(_wpfRenderScheduler, scheduler))
        {
            _ownsRenderScheduler = ownsScheduler;
            return;
        }

        DetachRenderScheduler(_wpfRenderScheduler);
        DisposeOwnedRenderScheduler();
        _wpfRenderScheduler = scheduler;
        _ownsRenderScheduler = ownsScheduler;
        AttachRenderScheduler(_wpfRenderScheduler);
    }

    private void AttachRenderScheduler(IWpfRenderScheduler scheduler)
    {
        scheduler.RenderRequested += OnRenderSchedulerRenderRequested;
    }

    private void DetachRenderScheduler(IWpfRenderScheduler scheduler)
    {
        scheduler.RenderRequested -= OnRenderSchedulerRenderRequested;
    }

    private void OnRenderSchedulerRenderRequested(object? sender, EventArgs e)
    {
        RenderSchedulerWakeupCount++;
        RenderWakeupRequested?.Invoke(this, EventArgs.Empty);
        if (!TryProcessRenderSchedulerWakeup())
        {
            TryRequestNativeLoopWakeup();
        }
    }

    private bool TryRequestNativeLoopWakeup()
    {
        var window = _window;
        return window != null && TryRequestNativeLoopWakeup(window.ContinueEvents);
    }

    internal bool TryRequestNativeLoopWakeup(Action continueEvents)
    {
        ArgumentNullException.ThrowIfNull(continueEvents);

        try
        {
            continueEvents();
            NativeLoopWakeupCount++;
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal bool TryProcessRenderSchedulerWakeup()
    {
        if (_window == null || _isRendering || _isProcessingRenderSchedulerWakeup)
        {
            return false;
        }

        try
        {
            if (!PlatformServices.Dispatcher.CheckAccess())
            {
                return false;
            }
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }

        _isProcessingRenderSchedulerWakeup = true;
        try
        {
            _window.DoRender();
            return true;
        }
        finally
        {
            _isProcessingRenderSchedulerWakeup = false;
        }
    }

    internal bool UpdatePortablePresentationSourceDpiScale(double dpiScaleX, double dpiScaleY)
    {
        if (_portablePresentationSourceBridge == null)
        {
            return false;
        }

        if (double.IsFinite(_portablePresentationSourceDpiScaleX) &&
            double.IsFinite(_portablePresentationSourceDpiScaleY) &&
            Math.Abs(_portablePresentationSourceDpiScaleX - dpiScaleX) < double.Epsilon &&
            Math.Abs(_portablePresentationSourceDpiScaleY - dpiScaleY) < double.Epsilon)
        {
            return false;
        }

        if (!_portablePresentationSourceBridge.TrySetDeviceScale(dpiScaleX, dpiScaleY))
        {
            return false;
        }

        _portablePresentationSourceDpiScaleX = dpiScaleX;
        _portablePresentationSourceDpiScaleY = dpiScaleY;
        return true;
    }

    private void AttachPortablePresentationSourceBridge(
        WpfPortablePresentationSourceBridge bridge,
        double dpiScaleX,
        double dpiScaleY)
    {
        DisposePortablePresentationSourceBridge();
        _portablePresentationSourceBridge = bridge;
        _portablePresentationSourceDpiScaleX = dpiScaleX;
        _portablePresentationSourceDpiScaleY = dpiScaleY;
        bridge.SyncHostRootVisual();
    }

    private void DisposePortablePresentationSourceBridge()
    {
        _portablePresentationSourceBridge?.Dispose();
        _portablePresentationSourceBridge = null;
        _portablePresentationSourceDpiScaleX = double.NaN;
        _portablePresentationSourceDpiScaleY = double.NaN;
    }

    private void DisposeOwnedRenderScheduler()
    {
        if (_ownsRenderScheduler && _wpfRenderScheduler is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _ownsRenderScheduler = false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private void ShowCore(bool requestRenderWhenInitialized)
    {
        _isHostVisible = true;
        EnsureWindow();
        _window!.IsVisible = true;

        if (!_window.IsInitialized)
        {
            _window.Initialize();
        }
        else if (requestRenderWhenInitialized)
        {
            WpfRenderScheduler.RequestRender();
        }
    }

    private static IDisposable? RegisterDefaultRenderDataSinkProvider(
        ProGpuWpfDrawingFrame drawingFrame,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        return drawingFrame.TryRegisterRenderDataSinkProvider(imageSourceAdapter, out IDisposable? registration)
            ? registration
            : null;
    }

    private static IWpfRenderScheduler CreateDefaultRenderScheduler(
        IWpfPlatformServices platformServices,
        out bool ownsScheduler)
    {
        try
        {
            ownsScheduler = true;
            return new DispatcherWpfRenderScheduler(
                platformServices.Dispatcher,
                platformServices.Timers);
        }
        catch (PlatformNotSupportedException)
        {
            ownsScheduler = false;
            return new CoalescingWpfRenderScheduler();
        }
    }

    private static SilkWindowState ToSilkWindowState(ProGpuWpfWindowState windowState)
    {
        return windowState switch
        {
            ProGpuWpfWindowState.Minimized => SilkWindowState.Minimized,
            ProGpuWpfWindowState.Maximized => SilkWindowState.Maximized,
            _ => SilkWindowState.Normal
        };
    }
}
