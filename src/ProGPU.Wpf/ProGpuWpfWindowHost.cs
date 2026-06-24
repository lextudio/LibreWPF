using System;
using System.Reflection;
using ProGPU.Backend;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using System.Windows.Media.ProGPU.Platform;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using ProGpuRenderTargetViewport = global::ProGPU.Scene.RenderTargetViewport;
using SilkWindowBorder = Silk.NET.Windowing.WindowBorder;
using SilkWindowState = Silk.NET.Windowing.WindowState;

namespace System.Windows.Media.ProGPU;

public unsafe sealed class ProGpuWpfWindowHost : IDisposable
{
    private const string TraceRenderSurfaceEnvironmentVariable = "PROGPU_WPF_TRACE_RENDER_SURFACE";

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
    private int _portablePresentationSourceClientWidth = -1;
    private int _portablePresentationSourceClientHeight = -1;
    private bool _isDisposed;
    private bool _hasPresentedFrame;
    private bool _ownsRenderScheduler;
    private bool _isRendering;
    private bool _isProcessingRenderSchedulerWakeup;
    private bool _isProcessingDispatcherWorkWakeup;
    private bool _forceFullWpfReplay;
    private bool _isHostVisible;
    private ProGpuWpfWindowState _windowState;
    private string _windowTitle;
    private int _clientWidth;
    private int _clientHeight;
    private int _requestedLogicalClientWidth = -1;
    private int _requestedLogicalClientHeight = -1;
    private int _declaredLogicalClientWidth = -1;
    private int _declaredLogicalClientHeight = -1;
    private int? _windowLeft;
    private int? _windowTop;
    private bool _windowTopmost;
    private ProGpuWpfWindowBorder _windowBorder;

    internal readonly record struct RenderSurfaceGeometry(
        uint LogicalWidth,
        uint LogicalHeight,
        uint PixelWidth,
        uint PixelHeight,
        double DpiScaleX,
        double DpiScaleY,
        double DpiScale,
        uint ViewportX = 0,
        uint ViewportY = 0,
        uint ViewportWidth = 0,
        uint ViewportHeight = 0);

    public ProGpuWpfWindowHost(ProGpuWpfWindowOptions? options = null)
    {
        _options = options ?? new ProGpuWpfWindowOptions();
        _isHostVisible = _options.IsVisible;
        _windowState = _options.WindowState;
        _windowTitle = _options.Title;
        _clientWidth = Math.Max(1, _options.Width);
        _clientHeight = Math.Max(1, _options.Height);
        _requestedLogicalClientWidth = _clientWidth;
        _requestedLogicalClientHeight = _clientHeight;
        _declaredLogicalClientWidth = _clientWidth;
        _declaredLogicalClientHeight = _clientHeight;
        _windowLeft = _options.Left;
        _windowTop = _options.Top;
        _windowTopmost = _options.Topmost;
        _windowBorder = _options.WindowBorder;
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

    public int Width => _clientWidth;

    public int Height => _clientHeight;

    public int? Left => _window?.Position.X ?? _windowLeft;

    public int? Top => _window?.Position.Y ?? _windowTop;

    public bool Topmost => _window?.TopMost ?? _windowTopmost;

    public ProGpuWpfWindowBorder WindowBorder => _windowBorder;

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

    internal RenderSurfaceGeometry LastResolvedRenderSurfaceGeometry { get; private set; }

    public long SkippedFrameCount { get; private set; }

    public long RetainedWpfReplaySkipCount { get; private set; }

    public long RetainedWpfBranchReplayCount { get; private set; }

    internal bool ForceFullWpfReplayForNextFrame => _forceFullWpfReplay;

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

    internal void DeferShowUntilRun()
    {
        ThrowIfDisposed();

        _isHostVisible = true;
        if (_window != null)
        {
            _window.IsVisible = true;
        }
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
        SetClientSizeCore(width, height, updatePortablePresentationSource: true);
    }

    public void SetPosition(int left, int top)
    {
        ThrowIfDisposed();

        _windowLeft = left;
        _windowTop = top;
        if (_window != null)
        {
            _window.Position = new Vector2D<int>(left, top);
        }

        WpfRenderScheduler.RequestRender();
    }

    public void SetTopmost(bool topmost)
    {
        ThrowIfDisposed();

        _windowTopmost = topmost;
        if (_window != null)
        {
            _window.TopMost = topmost;
        }

        WpfRenderScheduler.RequestRender();
    }

    public void SetWindowBorder(ProGpuWpfWindowBorder windowBorder)
    {
        ThrowIfDisposed();

        _windowBorder = windowBorder;
        if (_window != null)
        {
            _window.WindowBorder = ToSilkWindowBorder(windowBorder);
        }

        WpfRenderScheduler.RequestRender();
    }

    internal void SetInitialClientSize(int width, int height)
    {
        ThrowIfDisposed();
        SetClientSizeCore(width, height, updatePortablePresentationSource: false);
    }

    private void SetClientSizeCore(int width, int height, bool updatePortablePresentationSource)
    {
        _clientWidth = Math.Max(1, width);
        _clientHeight = Math.Max(1, height);
        _requestedLogicalClientWidth = _clientWidth;
        _requestedLogicalClientHeight = _clientHeight;
        _declaredLogicalClientWidth = _clientWidth;
        _declaredLogicalClientHeight = _clientHeight;
        if (_window != null)
        {
            _window.Size = new Vector2D<int>(_clientWidth, _clientHeight);
        }

        if (updatePortablePresentationSource)
        {
            UpdatePortablePresentationSourceClientSize((uint)_clientWidth, (uint)_clientHeight);
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

    public bool TryBeginDragMove()
    {
        ThrowIfDisposed();

        return _window != null && PlatformServices.WindowDecorations.TryBeginDragMove(_window);
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
        windowOptions.TopMost = _windowTopmost;
        windowOptions.WindowBorder = ToSilkWindowBorder(_windowBorder);
        if (_windowLeft.HasValue && _windowTop.HasValue)
        {
            windowOptions.Position = new Vector2D<int>(_windowLeft.Value, _windowTop.Value);
        }

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
        SynchronizePortablePresentationSourceGeometry();
        WpfRenderScheduler.RequestRender();
    }

    private void OnResize(Vector2D<int> size)
    {
        if (_window == null)
        {
            UpdateClientSizeFromNativeResize(size);
        }
        else
        {
            var framebufferSize = _window.FramebufferSize;
            var monitorDpiScale = ResolveCurrentMonitorDpiScale();
            UpdateClientSizeFromNativeResize(size, framebufferSize, monitorDpiScale);
        }

        if (_target == null || _window == null)
        {
            WpfRenderScheduler.RequestRender();
            return;
        }

        var geometry = ResolveCurrentRenderSurfaceGeometry();
        SynchronizePortablePresentationSourceGeometry(geometry);
        _target.Context.ConfigureSwapChain(
            geometry.PixelWidth,
            geometry.PixelHeight);
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
            if (_target == null || _window == null || _target.Context.Surface == null)
            {
                ProcessDispatcherQueueCore();
                return;
            }

            var geometry = ResolveCurrentRenderSurfaceGeometry();
            SynchronizePortablePresentationSourceGeometry(geometry);
            ProcessDispatcherQueueCore();

            if (_target == null || _window == null || _target.Context.Surface == null)
            {
                return;
            }

            geometry = ResolveCurrentRenderSurfaceGeometry();
            SynchronizePortablePresentationSourceGeometry(geometry);
            var pixelWidth = geometry.PixelWidth;
            var pixelHeight = geometry.PixelHeight;
            var logicalWidth = geometry.LogicalWidth;
            var logicalHeight = geometry.LogicalHeight;
            var dpiScaleX = geometry.DpiScaleX;
            var dpiScaleY = geometry.DpiScaleY;
            var dpiScale = geometry.DpiScale;
            var viewportX = geometry.ViewportX;
            var viewportY = geometry.ViewportY;
            var viewportWidth = ResolveGeometryViewportDimension(geometry.ViewportWidth, pixelWidth);
            var viewportHeight = ResolveGeometryViewportDimension(geometry.ViewportHeight, pixelHeight);
            _target.DetectWpfSourceChanges();
            var frameState = CaptureFrameState(_target, pixelWidth, pixelHeight);

            if (!ShouldRenderFrame(frameState))
            {
                SkippedFrameCount++;
                return;
            }

            _target.Context.ReconfigureIfNeeded(pixelWidth, pixelHeight);

            object? wpfRootVisual = _wpfRootVisual;
            var forceFullWpfReplay = _forceFullWpfReplay;
            var shouldReplayWpfRootVisual = wpfRootVisual != null &&
                (forceFullWpfReplay || _target.ShouldReplayVisualSubtree(wpfRootVisual));
            var canReplayDirtyWpfBranches = wpfRootVisual != null &&
                shouldReplayWpfRootVisual &&
                !forceFullWpfReplay &&
                _target.CanReplayDirtyRetainedVisualBranches(wpfRootVisual);
            var clearRetainedWpfVisualRoot = wpfRootVisual == null ||
                (shouldReplayWpfRootVisual && !canReplayDirtyWpfBranches);
            var drawingFrame = _target.BeginDrawingFrame(
                viewportWidth,
                viewportHeight,
                clearRetainedWpfVisualRoot,
                logicalWidth,
                logicalHeight,
                dpiScaleX,
                dpiScaleY);
            var activeWpfImageSourceAdapter = _target.CreateFrameImageSourceAdapter(WpfImageSourceAdapter);

            using (IDisposable? renderDataSinkProviderRegistration = RegisterRenderDataSinkProvider(drawingFrame, activeWpfImageSourceAdapter))
            {
                var args = new ProGpuWpfFrameEventArgs(
                    drawingContext: null,
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
                                activeWpfImageSourceAdapter,
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
                                activeWpfImageSourceAdapter);
                        }
                    }
                    else
                    {
                        RetainedWpfReplaySkipCount++;
                    }

                    _forceFullWpfReplay = false;
                }
                else
                {
                    _target.WpfInvalidationTracker.Detach();
                    LastVisualReplayResult = default;
                    _forceFullWpfReplay = false;
                }

                if (WpfDraw != null)
                {
                    using var sourceDrawingContext = drawingFrame.OpenCompositionDrawingContext(activeWpfImageSourceAdapter);
                    InvokeSourceDraw(sourceDrawingContext, args);
                }
                else
                {
                    LastSourceDrawingResult = default;
                }

                if (Draw != null)
                {
                    using var drawingContext = drawingFrame.OpenDrawingContext();
                    var drawArgs = new ProGpuWpfFrameEventArgs(
                        drawingContext,
                        pixelWidth,
                        pixelHeight,
                        deltaSeconds,
                        dpiScale,
                        drawingFrame);
                    Draw.Invoke(drawingContext, drawArgs);
                    Render?.Invoke(this, drawArgs);
                }
                else
                {
                    Render?.Invoke(this, args);
                }

                WpfRenderScheduler.ConsumeRenderRequest();
            }

            if (Present(
                    logicalWidth,
                    logicalHeight,
                    pixelWidth,
                    pixelHeight,
                    viewportX,
                    viewportY,
                    viewportWidth,
                    viewportHeight,
                    dpiScale))
            {
                RecordPresentedFrame(CaptureFrameState(_target, pixelWidth, pixelHeight));
                TraceRenderSurfaceGeometryIfRequested(geometry);
            }
        }
        finally
        {
            _isRendering = false;
        }
    }

    private bool Present(
        uint logicalWidth,
        uint logicalHeight,
        uint pixelWidth,
        uint pixelHeight,
        uint viewportX,
        uint viewportY,
        uint viewportWidth,
        uint viewportHeight,
        double dpiScale)
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
            _target.Render(
                logicalWidth,
                logicalHeight,
                pixelWidth,
                pixelHeight,
                new ProGpuRenderTargetViewport(
                    viewportX,
                    viewportY,
                    ResolveGeometryViewportDimension(viewportWidth, pixelWidth),
                    ResolveGeometryViewportDimension(viewportHeight, pixelHeight)),
                (float)dpiScale,
                targetView);
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

    private static void TraceRenderSurfaceGeometryIfRequested(RenderSurfaceGeometry geometry)
    {
        if (Environment.GetEnvironmentVariable(TraceRenderSurfaceEnvironmentVariable) != "1")
        {
            return;
        }

        Console.WriteLine(
            "ProGPU WPF render surface: " +
            $"logical {geometry.LogicalWidth}x{geometry.LogicalHeight}, " +
            $"pixels {geometry.PixelWidth}x{geometry.PixelHeight}, " +
            $"viewport {ResolveGeometryViewportDimension(geometry.ViewportWidth, geometry.PixelWidth)}x{ResolveGeometryViewportDimension(geometry.ViewportHeight, geometry.PixelHeight)}@{geometry.ViewportX},{geometry.ViewportY}, " +
            $"dpi {geometry.DpiScale:0.###}");
    }

    internal static RenderSurfaceGeometry ResolveRenderSurfaceGeometry(
        int clientWidth,
        int clientHeight,
        Vector2D<int> framebufferSize,
        double monitorDpiScale)
    {
        var logicalWidth = (uint)Math.Max(1, clientWidth);
        var logicalHeight = (uint)Math.Max(1, clientHeight);
        var pixelWidth = (uint)Math.Max(1, framebufferSize.X);
        var pixelHeight = (uint)Math.Max(1, framebufferSize.Y);
        var fallbackScale = NormalizeMonitorDpiScale(monitorDpiScale);

        if (fallbackScale > 1.0)
        {
            var scaledPixelWidth = (uint)Math.Max(1, (int)Math.Ceiling(logicalWidth * fallbackScale));
            var scaledPixelHeight = (uint)Math.Max(1, (int)Math.Ceiling(logicalHeight * fallbackScale));

            if (pixelWidth < scaledPixelWidth)
            {
                pixelWidth = Math.Max(pixelWidth, scaledPixelWidth);
            }

            if (pixelHeight < scaledPixelHeight)
            {
                pixelHeight = Math.Max(pixelHeight, scaledPixelHeight);
            }
        }

        var dpiScaleX = pixelWidth / (double)logicalWidth;
        var dpiScaleY = pixelHeight / (double)logicalHeight;

        return new RenderSurfaceGeometry(
            logicalWidth,
            logicalHeight,
            pixelWidth,
            pixelHeight,
            dpiScaleX,
            dpiScaleY,
            (dpiScaleX + dpiScaleY) / 2.0,
            ViewportWidth: pixelWidth,
            ViewportHeight: pixelHeight);
    }

    private RenderSurfaceGeometry ResolveCurrentRenderSurfaceGeometry()
    {
        RenderSurfaceGeometry geometry;
        var cachedLogicalClientWidth = GetCachedLogicalClientWidth();
        var cachedLogicalClientHeight = GetCachedLogicalClientHeight();
        if (_window == null)
        {
            geometry = ResolveRenderSurfaceGeometry(
                cachedLogicalClientWidth,
                cachedLogicalClientHeight,
                new Vector2D<int>(cachedLogicalClientWidth, cachedLogicalClientHeight),
                1.0);
            LastResolvedRenderSurfaceGeometry = geometry;
            return geometry;
        }

        var clientSize = _window.Size;
        var framebufferSize = _window.FramebufferSize;
        var monitorDpiScale = ResolveCurrentMonitorDpiScale();
        var logicalSize = ResolveLogicalClientSize(
            clientSize,
            framebufferSize,
            cachedLogicalClientWidth,
            cachedLogicalClientHeight,
            monitorDpiScale);
        logicalSize = ReconcileResolvedLogicalClientSize(
            logicalSize,
            cachedLogicalClientWidth,
            cachedLogicalClientHeight,
            monitorDpiScale);
        logicalSize = ReconcileResolvedLogicalClientSizeWithRootRenderSize(
            logicalSize,
            framebufferSize,
            monitorDpiScale);
        geometry = ResolveRenderSurfaceGeometry(
            logicalSize.X,
            logicalSize.Y,
            framebufferSize,
            monitorDpiScale);
        LastResolvedRenderSurfaceGeometry = geometry;
        return geometry;
    }

    private static uint ResolveGeometryViewportDimension(uint viewportDimension, uint fallbackPixelDimension)
    {
        return viewportDimension > 0u
            ? viewportDimension
            : Math.Max(1u, fallbackPixelDimension);
    }

    internal bool SynchronizePortablePresentationSourceDpiScale(RenderSurfaceGeometry geometry)
    {
        LastResolvedRenderSurfaceGeometry = geometry;
        return UpdatePortablePresentationSourceDpiScale(geometry.DpiScaleX, geometry.DpiScaleY);
    }

    private bool SynchronizePortablePresentationSourceDpiScale()
    {
        var geometry = ResolveCurrentRenderSurfaceGeometry();
        return SynchronizePortablePresentationSourceDpiScale(geometry);
    }

    internal bool SynchronizePortablePresentationSourceGeometry(RenderSurfaceGeometry geometry)
    {
        LastResolvedRenderSurfaceGeometry = geometry;
        bool clientSizeChanged = UpdatePortablePresentationSourceClientSize(geometry.LogicalWidth, geometry.LogicalHeight);
        bool dpiScaleChanged = UpdatePortablePresentationSourceDpiScale(geometry.DpiScaleX, geometry.DpiScaleY);
        return clientSizeChanged || dpiScaleChanged;
    }

    private bool SynchronizePortablePresentationSourceGeometry()
    {
        var geometry = ResolveCurrentRenderSurfaceGeometry();
        return SynchronizePortablePresentationSourceGeometry(geometry);
    }

    internal bool UpdateClientSizeFromNativeResize(Vector2D<int> size)
    {
        return UpdateClientSizeFromNativeResize(size, size, 1.0);
    }

    internal bool UpdateClientSizeFromNativeResize(
        Vector2D<int> size,
        Vector2D<int> framebufferSize,
        double monitorDpiScale)
    {
        var logicalSize = ResolveLogicalClientSize(
            size,
            framebufferSize,
            GetCachedLogicalClientWidth(),
            GetCachedLogicalClientHeight(),
            monitorDpiScale);
        logicalSize = ReconcileResolvedLogicalClientSize(
            logicalSize,
            GetCachedLogicalClientWidth(),
            GetCachedLogicalClientHeight(),
            monitorDpiScale);
        logicalSize = ReconcileResolvedLogicalClientSizeWithRootRenderSize(
            logicalSize,
            framebufferSize,
            monitorDpiScale);
        var clientWidth = logicalSize.X;
        var clientHeight = logicalSize.Y;
        if (_clientWidth == clientWidth && _clientHeight == clientHeight)
        {
            return false;
        }

        _clientWidth = clientWidth;
        _clientHeight = clientHeight;
        return true;
    }

    private int GetCachedLogicalClientWidth()
    {
        return ResolveCachedLogicalClientDimension(
            _portablePresentationSourceClientWidth,
            _requestedLogicalClientWidth,
            _declaredLogicalClientWidth,
            _clientWidth);
    }

    private int GetCachedLogicalClientHeight()
    {
        return ResolveCachedLogicalClientDimension(
            _portablePresentationSourceClientHeight,
            _requestedLogicalClientHeight,
            _declaredLogicalClientHeight,
            _clientHeight);
    }

    private static Vector2D<int> ReconcileResolvedLogicalClientSize(
        Vector2D<int> resolvedSize,
        int cachedWidth,
        int cachedHeight,
        double monitorDpiScale)
    {
        return new Vector2D<int>(
            ReconcileResolvedLogicalClientDimension(resolvedSize.X, cachedWidth, monitorDpiScale),
            ReconcileResolvedLogicalClientDimension(resolvedSize.Y, cachedHeight, monitorDpiScale));
    }

    private static int ReconcileResolvedLogicalClientDimension(
        int resolvedDimension,
        int cachedDimension,
        double monitorDpiScale)
    {
        if (resolvedDimension <= 0 || cachedDimension <= 0)
        {
            return Math.Max(1, resolvedDimension > 0 ? resolvedDimension : cachedDimension);
        }

        var larger = Math.Max(resolvedDimension, cachedDimension);
        var smaller = Math.Min(resolvedDimension, cachedDimension);
        return larger == resolvedDimension &&
            DimensionsDifferByDpiScale(larger, smaller, monitorDpiScale)
                ? smaller
                : resolvedDimension;
    }

    private Vector2D<int> ReconcileResolvedLogicalClientSizeWithRootRenderSize(
        Vector2D<int> resolvedSize,
        Vector2D<int> framebufferSize,
        double monitorDpiScale)
    {
        if (!TryGetWpfRootRenderSize(out var rootRenderSize))
        {
            return resolvedSize;
        }

        return new Vector2D<int>(
            ReconcileResolvedLogicalClientDimensionWithRootRenderSize(
                resolvedSize.X,
                rootRenderSize.X,
                framebufferSize.X,
                monitorDpiScale),
            ReconcileResolvedLogicalClientDimensionWithRootRenderSize(
                resolvedSize.Y,
                rootRenderSize.Y,
                framebufferSize.Y,
                monitorDpiScale));
    }

    private static int ReconcileResolvedLogicalClientDimensionWithRootRenderSize(
        int resolvedDimension,
        int rootRenderDimension,
        int framebufferDimension,
        double monitorDpiScale)
    {
        if (resolvedDimension <= 0 ||
            rootRenderDimension <= 0 ||
            framebufferDimension <= 0)
        {
            return resolvedDimension;
        }

        if (Math.Abs(resolvedDimension - framebufferDimension) > 1)
        {
            return resolvedDimension;
        }

        var larger = Math.Max(resolvedDimension, rootRenderDimension);
        var smaller = Math.Min(resolvedDimension, rootRenderDimension);
        if (larger != resolvedDimension)
        {
            return resolvedDimension;
        }

        return DimensionsDifferByDpiScale(larger, smaller, monitorDpiScale)
            ? rootRenderDimension
            : resolvedDimension;
    }

    private bool TryGetWpfRootRenderSize(out Vector2D<int> renderSize)
    {
        renderSize = default;
        if (_wpfRootVisual == null)
        {
            return false;
        }

        var renderSizeProperty = _wpfRootVisual.GetType().GetProperty(
            "RenderSize",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? rawRenderSize = renderSizeProperty?.GetValue(_wpfRootVisual);
        if (!TryReadPositiveFiniteDimension(rawRenderSize, "Width", out var width) ||
            !TryReadPositiveFiniteDimension(rawRenderSize, "Height", out var height))
        {
            return false;
        }

        renderSize = new Vector2D<int>(
            Math.Max(1, (int)Math.Ceiling(width)),
            Math.Max(1, (int)Math.Ceiling(height)));
        return true;
    }

    private static bool TryReadPositiveFiniteDimension(
        object? value,
        string propertyName,
        out double dimension)
    {
        dimension = 0.0;
        if (value == null)
        {
            return false;
        }

        var property = value.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? rawDimension = property?.GetValue(value);
        try
        {
            dimension = Convert.ToDouble(rawDimension);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }

        return double.IsFinite(dimension) && dimension > 0.0;
    }

    internal static int ResolveCachedLogicalClientDimension(
        int portablePresentationSourceDimension,
        int requestedLogicalDimension,
        int currentClientDimension)
    {
        if (portablePresentationSourceDimension > 0 && requestedLogicalDimension > 0)
        {
            var smaller = Math.Min(portablePresentationSourceDimension, requestedLogicalDimension);
            var larger = Math.Max(portablePresentationSourceDimension, requestedLogicalDimension);
            if (DimensionsDifferByDpiScale(larger, smaller))
            {
                return smaller;
            }
        }

        return portablePresentationSourceDimension > 0
            ? portablePresentationSourceDimension
            : requestedLogicalDimension > 0
                ? requestedLogicalDimension
                : currentClientDimension;
    }

    private static int ResolveCachedLogicalClientDimension(
        int portablePresentationSourceDimension,
        int requestedLogicalDimension,
        int declaredLogicalDimension,
        int currentClientDimension)
    {
        var resolved = ResolveCachedLogicalClientDimension(
            portablePresentationSourceDimension,
            requestedLogicalDimension,
            currentClientDimension);
        if (declaredLogicalDimension > 0 && resolved > 0)
        {
            var smaller = Math.Min(declaredLogicalDimension, resolved);
            var larger = Math.Max(declaredLogicalDimension, resolved);
            if (DimensionsDifferByDpiScale(larger, smaller))
            {
                return smaller;
            }
        }

        return resolved;
    }

    internal static Vector2D<int> ResolveLogicalClientSize(
        Vector2D<int> nativeSize,
        Vector2D<int> framebufferSize,
        int cachedWidth,
        int cachedHeight,
        double monitorDpiScale)
    {
        return new Vector2D<int>(
            ResolveLogicalClientDimension(nativeSize.X, framebufferSize.X, cachedWidth, monitorDpiScale),
            ResolveLogicalClientDimension(nativeSize.Y, framebufferSize.Y, cachedHeight, monitorDpiScale));
    }

    private static int ResolveLogicalClientDimension(
        int nativeDimension,
        int framebufferDimension,
        int cachedDimension,
        double monitorDpiScale)
    {
        var cached = Math.Max(1, cachedDimension);
        var fallback = Math.Max(1, nativeDimension > 0 ? nativeDimension : cached);
        var nativeMatchesFramebuffer = nativeDimension > 0 &&
            Math.Abs(nativeDimension - framebufferDimension) <= 1;
        var dpiScale = ResolveLogicalClientDpiScale(
            monitorDpiScale,
            nativeMatchesFramebuffer,
            framebufferDimension,
            cached);
        if (dpiScale <= 1.0 &&
            TryInferNativeDpiScaleFromCachedDips(nativeDimension, cached, out var inferredNativeDpiScale) &&
            FramebufferDimensionAllowsNativePhysicalClient(framebufferDimension, nativeDimension, inferredNativeDpiScale))
        {
            dpiScale = inferredNativeDpiScale;
        }

        if (dpiScale <= 1.0 || framebufferDimension <= 0)
        {
            return fallback;
        }

        var scaledLogical = Math.Max(
            1,
            (int)Math.Round(framebufferDimension / dpiScale, MidpointRounding.AwayFromZero));
        var nativeDiffersFromCached = nativeDimension > 0 &&
            Math.Abs(nativeDimension - cached) > 1;
        if (nativeDiffersFromCached &&
            NativeDimensionLooksPhysicalForCachedDips(nativeDimension, cached, dpiScale))
        {
            return cached;
        }

        var framebufferMatchesCachedScale =
            Math.Abs(framebufferDimension - cached * dpiScale) <= Math.Max(2.0, dpiScale);

        if (nativeMatchesFramebuffer && nativeDiffersFromCached)
        {
            return scaledLogical;
        }

        if (framebufferMatchesCachedScale)
        {
            return cached;
        }

        return fallback;
    }

    private static bool NativeDimensionLooksPhysicalForCachedDips(
        int nativeDimension,
        int cachedDimension,
        double dpiScale)
    {
        if (dpiScale <= 1.0 ||
            nativeDimension <= 0 ||
            cachedDimension <= 0)
        {
            return false;
        }

        return Math.Abs(nativeDimension - cachedDimension * dpiScale) <= Math.Max(2.0, dpiScale);
    }

    private static bool DimensionsDifferByDpiScale(
        int largerDimension,
        int smallerDimension,
        double monitorDpiScale = 1.0)
    {
        if (largerDimension <= 0 || smallerDimension <= 0 || largerDimension <= smallerDimension)
        {
            return false;
        }

        var scale = largerDimension / (double)smallerDimension;
        if (!double.IsFinite(scale) || scale < 1.25 || scale > 8.0)
        {
            return false;
        }

        var normalizedMonitorScale = NormalizeMonitorDpiScale(monitorDpiScale);
        if (normalizedMonitorScale > 1.0 &&
            Math.Abs(scale - normalizedMonitorScale) <= Math.Max(0.05, normalizedMonitorScale * 0.03))
        {
            return true;
        }

        ReadOnlySpan<double> commonScales = [1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0];
        foreach (var commonScale in commonScales)
        {
            if (Math.Abs(scale - commonScale) <= Math.Max(0.05, commonScale * 0.03))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryInferNativeDpiScaleFromCachedDips(
        int nativeDimension,
        int cachedDimension,
        out double dpiScale)
    {
        dpiScale = 1.0;
        if (nativeDimension <= 0 || cachedDimension <= 0)
        {
            return false;
        }

        var inferredScale = nativeDimension / (double)cachedDimension;
        if (!double.IsFinite(inferredScale) || inferredScale < 1.25 || inferredScale > 8.0)
        {
            return false;
        }

        dpiScale = inferredScale;
        return NativeDimensionLooksPhysicalForCachedDips(nativeDimension, cachedDimension, dpiScale);
    }

    private static bool FramebufferDimensionAllowsNativePhysicalClient(
        int framebufferDimension,
        int nativeDimension,
        double dpiScale)
    {
        if (framebufferDimension <= 0 || nativeDimension <= 0 || dpiScale <= 1.0)
        {
            return false;
        }

        if (Math.Abs(framebufferDimension - nativeDimension) <= Math.Max(2.0, dpiScale))
        {
            return true;
        }

        var framebufferToNativeScale = framebufferDimension / (double)nativeDimension;
        return double.IsFinite(framebufferToNativeScale) &&
            framebufferToNativeScale >= 1.0 &&
            framebufferToNativeScale <= dpiScale + 0.25;
    }

    private static double ResolveLogicalClientDpiScale(
        double monitorDpiScale,
        bool nativeMatchesFramebuffer,
        int framebufferDimension,
        int cachedDimension)
    {
        var dpiScale = NormalizeMonitorDpiScale(monitorDpiScale);
        if (dpiScale > 1.0 ||
            !nativeMatchesFramebuffer ||
            framebufferDimension <= 0 ||
            cachedDimension <= 0)
        {
            return dpiScale;
        }

        var inferredScale = framebufferDimension / (double)cachedDimension;
        return double.IsFinite(inferredScale) && inferredScale >= 1.25 && inferredScale <= 8.0
            ? inferredScale
            : dpiScale;
    }

    private double ResolveCurrentMonitorDpiScale()
    {
        return DisplayScaleResolver.ResolveWindowDisplayScale(
            _window,
            ResolveCurrentMonitorDpiScaleFromPlatformServices());
    }

    private double ResolveCurrentMonitorDpiScaleFromPlatformServices()
    {
        try
        {
            var monitors = PlatformServices.Monitors.GetMonitors();
            if (monitors.Count == 0)
            {
                return 1.0;
            }

            foreach (var monitor in monitors)
            {
                if (monitor.IsPrimary)
                {
                    return NormalizeMonitorDpiScale(monitor.DpiScale);
                }
            }

            return NormalizeMonitorDpiScale(monitors[0].DpiScale);
        }
        catch
        {
            return 1.0;
        }
    }

    internal static double ResolveMonitorDpiScaleWithPlatformFallback(
        double monitorDpiScale,
        Func<double?> platformDpiScaleProvider)
    {
        return DisplayScaleResolver.ResolveDisplayScaleWithPlatformFallback(
            monitorDpiScale,
            platformDpiScaleProvider);
    }

    private static double NormalizeMonitorDpiScale(double dpiScale)
    {
        return DisplayScaleResolver.NormalizeDisplayScale(dpiScale);
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
            target.LastRetainedBranchReplayTargetConflictCount,
            target.LastRetainedBranchInvalidationUsedFallback);
    }

    internal IDisposable? RegisterRenderDataSinkProvider(ProGpuWpfDrawingFrame drawingFrame)
    {
        return RegisterRenderDataSinkProvider(drawingFrame, WpfImageSourceAdapter);
    }

    internal IDisposable? RegisterRenderDataSinkProvider(
        ProGpuWpfDrawingFrame drawingFrame,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        ArgumentNullException.ThrowIfNull(drawingFrame);

        return RenderDataSinkProviderRegistrationFactory(drawingFrame, imageSourceAdapter);
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
        var input = NormalizeInputEventForCurrentRenderSurface(e);
        InputReceived?.Invoke(this, input);
        if (!ReferenceEquals(input, e))
        {
            e.Handled = input.Handled;
        }

        WpfRenderScheduler.RequestRender();
    }

    private WpfInputEventArgs NormalizeInputEventForCurrentRenderSurface(WpfInputEventArgs input)
    {
        if (!IsPointerInput(input.Kind) || _window == null)
        {
            return input;
        }

        var geometry = ResolveCurrentRenderSurfaceGeometry();
        return NormalizeInputEventForRenderSurfaceGeometry(
            input,
            geometry,
            NativeInputCoordinatesLookPhysical(_window.Size, geometry));
    }

    internal static WpfInputEventArgs NormalizeInputEventForRenderSurfaceGeometry(
        WpfInputEventArgs input,
        RenderSurfaceGeometry geometry,
        bool inputCoordinatesArePhysical)
    {
        if (!inputCoordinatesArePhysical || !IsPointerInput(input.Kind))
        {
            return input;
        }

        var viewportWidth = ResolveGeometryViewportDimension(geometry.ViewportWidth, geometry.PixelWidth);
        var viewportHeight = ResolveGeometryViewportDimension(geometry.ViewportHeight, geometry.PixelHeight);
        var scaleX = viewportWidth / (double)Math.Max(1u, geometry.LogicalWidth);
        var scaleY = viewportHeight / (double)Math.Max(1u, geometry.LogicalHeight);
        var normalized = new WpfInputEventArgs(
            input.Kind,
            input.Key,
            input.ScanCode,
            input.Character,
            NormalizeInputCoordinate(input.X, geometry.ViewportX, scaleX),
            NormalizeInputCoordinate(input.Y, geometry.ViewportY, scaleY),
            input.DeltaX,
            input.DeltaY,
            input.Button,
            input.Modifiers)
        {
            Handled = input.Handled
        };
        return normalized;
    }

    private static bool NativeInputCoordinatesLookPhysical(
        Vector2D<int> nativeSize,
        RenderSurfaceGeometry geometry)
    {
        return NativeInputDimensionLooksPhysical(
                nativeSize.X,
                geometry.LogicalWidth,
                ResolveGeometryViewportDimension(geometry.ViewportWidth, geometry.PixelWidth)) ||
            NativeInputDimensionLooksPhysical(
                nativeSize.Y,
                geometry.LogicalHeight,
                ResolveGeometryViewportDimension(geometry.ViewportHeight, geometry.PixelHeight));
    }

    private static bool NativeInputDimensionLooksPhysical(
        int nativeDimension,
        uint logicalDimension,
        uint pixelDimension)
    {
        if (nativeDimension <= 0 || logicalDimension == 0u || pixelDimension <= logicalDimension)
        {
            return false;
        }

        return Math.Abs(nativeDimension - pixelDimension) <= 2;
    }

    private static bool IsPointerInput(WpfInputEventKind kind)
    {
        return kind is WpfInputEventKind.MouseMove or
            WpfInputEventKind.MouseDown or
            WpfInputEventKind.MouseUp or
            WpfInputEventKind.MouseWheel;
    }

    private static double NormalizeInputCoordinate(double coordinate, uint viewportOffset, double scale)
    {
        if (!double.IsFinite(coordinate) || !double.IsFinite(scale) || scale <= 0.0)
        {
            return 0.0;
        }

        return (coordinate - viewportOffset) / scale;
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
        InvalidateWpfRootVisualForPresentationSourceGeometryChange();
        return true;
    }

    internal bool UpdatePortablePresentationSourceClientSize(uint logicalWidth, uint logicalHeight)
    {
        if (_portablePresentationSourceBridge == null)
        {
            return false;
        }

        var clientWidth = (int)Math.Min((uint)int.MaxValue, Math.Max(1u, logicalWidth));
        var clientHeight = (int)Math.Min((uint)int.MaxValue, Math.Max(1u, logicalHeight));
        if (_portablePresentationSourceClientWidth == clientWidth &&
            _portablePresentationSourceClientHeight == clientHeight)
        {
            return false;
        }

        if (!_portablePresentationSourceBridge.TrySetClientSize(clientWidth, clientHeight))
        {
            return false;
        }

        _portablePresentationSourceClientWidth = clientWidth;
        _portablePresentationSourceClientHeight = clientHeight;
        InvalidateWpfRootVisualForPresentationSourceGeometryChange();
        return true;
    }

    private void InvalidateWpfRootVisualForPresentationSourceGeometryChange()
    {
        _forceFullWpfReplay = true;

        if (_target == null)
        {
            return;
        }

        _target.SceneRootVisual.Invalidate();
        _target.RetainedWpfVisualRoot.Invalidate();
        _target.RootVisual.Invalidate();

        if (_wpfRootVisual != null)
        {
            _target.WpfInvalidationTracker.MarkDirty(_wpfRootVisual);
        }
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
        _portablePresentationSourceClientWidth = -1;
        _portablePresentationSourceClientHeight = -1;
        bridge.SyncHostRootVisual();
    }

    private void DisposePortablePresentationSourceBridge()
    {
        _portablePresentationSourceBridge?.Dispose();
        _portablePresentationSourceBridge = null;
        _portablePresentationSourceDpiScaleX = double.NaN;
        _portablePresentationSourceDpiScaleY = double.NaN;
        _portablePresentationSourceClientWidth = -1;
        _portablePresentationSourceClientHeight = -1;
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

    private static SilkWindowBorder ToSilkWindowBorder(ProGpuWpfWindowBorder windowBorder)
    {
        return windowBorder switch
        {
            ProGpuWpfWindowBorder.Fixed => SilkWindowBorder.Fixed,
            ProGpuWpfWindowBorder.Hidden => SilkWindowBorder.Hidden,
            _ => SilkWindowBorder.Resizable
        };
    }
}
