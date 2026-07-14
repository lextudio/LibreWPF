using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using ProGPU.Backend;
using ProGPU.DirectX;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Wpf.Interop;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using ProGpuPathGeometry = global::ProGPU.Vector.PathGeometry;
using ProGpuPrimitivePathGeometry = global::ProGPU.Vector.PrimitivePathGeometry;
using ProGpuRect = global::ProGPU.Scene.Rect;
using ProGpuRenderTargetViewport = global::ProGPU.Scene.RenderTargetViewport;
using PortableVisualLayoutStateSource = ProGPU.Wpf.Interop.IPortableVisualLayoutStateSource;
using SilkWindowBorder = Silk.NET.Windowing.WindowBorder;
using SilkWindowState = Silk.NET.Windowing.WindowState;

namespace System.Windows.Media.ProGPU;

public unsafe sealed class ProGpuWpfWindowHost : IDisposable
{
    private const string TraceRenderSurfaceEnvironmentVariable = "PROGPU_WPF_TRACE_RENDER_SURFACE";
    private const string TraceInputEnvironmentVariable = "PROGPU_WPF_TRACE_INPUT";
    private const string TraceNativeLoopEnvironmentVariable = "PROGPU_WPF_TRACE_NATIVE_LOOP";
    private const int HitTestOwnerBufferCapacity = 64;
    private static readonly TimeSpan PortableNativeLoopActiveDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan PortableNativeLoopIdleDelay = TimeSpan.FromMilliseconds(16);

    private static readonly bool s_traceRenderSurface = IsTraceEnabled(TraceRenderSurfaceEnvironmentVariable);
    private static readonly bool s_traceInput = IsTraceEnabled(TraceInputEnvironmentVariable);
    private static readonly bool s_traceNativeLoop = IsTraceEnabled(TraceNativeLoopEnvironmentVariable);

    // Guard against the spurious GLFW MouseUp that showing another of our own native windows induces
    // on the previously-focused window while a mouse button is physically held down. On the portable
    // (Silk/GLFW) backend, showing a window steals OS focus, and the OS ends the current button
    // "session" on the old window by delivering a real button-up (same position, no movement). That
    // phantom up prematurely ends an in-progress Thumb/splitter drag - see
    // OpenDevelop src/Libraries/AvalonDock/docs/librewpf.md. We arm this guard only when a button is
    // actually pressed at the moment a window is shown (so hover/release-driven menus and tooltips,
    // which don't hold a button, never arm it), and swallow only an up that matches the phantom's
    // fingerprint: same window, at the press position, with no drag movement since the down, within
    // a short window after the show.
    private const long SpuriousUpAfterWindowShowMs = 250;
    // GLFW_MOUSE_PASSTHROUGH (GLFW 3.4). Not in Silk.NET.GLFW 2.23's WindowAttributeSetter enum
    // (which stops at FocusOnShow=0x2000C), but the native glfwSetWindowAttrib accepts the raw
    // value; on macOS/Cocoa it maps to NSWindow.ignoresMouseEvents.
    private const int GlfwMousePassthroughAttrib = 0x0002000D;
    private static long s_lastWindowShownTicks = long.MinValue;
    private static bool s_mouseButtonPressedSomewhere;
    // Windows we made mouse-passthrough for the duration of the current press (drag), so their
    // native windows don't steal the in-progress drag from the window that owns the capture.
    private static readonly List<ProGpuWpfWindowHost> s_dragPassthroughHosts = new();

    private bool _mouseButtonDownSeen;
    private bool _mouseMovedSinceDown;
    private double _mouseDownX;
    private double _mouseDownY;
    private bool _dragPassthroughApplied;

    private void NoteWindowShownForSpuriousUpGuard()
    {
        // Only act while a button is actually held - otherwise a click landing right as an
        // unrelated popup/tooltip appears could be wrongly affected.
        if (!s_mouseButtonPressedSomewhere)
        {
            return;
        }

        // (1) Arm the spurious-up swallow (below), and (2) make this transient window
        // mouse-passthrough so the drag keeps flowing to the window that owns the press instead of
        // this overlay stealing it (LibreWPF has no cross-window mouse capture; showing a window
        // mid-drag otherwise both injects a phantom up and hijacks subsequent moves). Cleared on
        // release. See OpenDevelop src/Libraries/AvalonDock/docs/librewpf.md.
        s_lastWindowShownTicks = Environment.TickCount64;
        if (!_dragPassthroughApplied)
        {
            _dragPassthroughApplied = true;
            if (!s_dragPassthroughHosts.Contains(this))
            {
                s_dragPassthroughHosts.Add(this);
            }
        }
        TrySetMousePassthrough(true);
    }

    private unsafe void TrySetFocusOnShow(bool enabled)
    {
        if (_window?.Native?.Glfw is not { } handle || handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Silk.NET.GLFW.Glfw.GetApi().SetWindowAttrib(
                (Silk.NET.GLFW.WindowHandle*)handle,
                Silk.NET.GLFW.WindowAttributeSetter.FocusOnShow,
                enabled);
            if (s_traceInput)
            {
                Console.WriteLine(
                    $"ProGPU WPF: focus-on-show {(enabled ? "ON" : "OFF")} " +
                    $"window#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_window)}");
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    private unsafe void TrySetMousePassthrough(bool enabled)
    {
        if (_window?.Native?.Glfw is not { } handle || handle == IntPtr.Zero)
        {
            return; // handle not ready yet (see OnLoad) or non-GLFW backend
        }

        try
        {
            var glfw = Silk.NET.GLFW.Glfw.GetApi();
            glfw.SetWindowAttrib(
                (Silk.NET.GLFW.WindowHandle*)handle,
                (Silk.NET.GLFW.WindowAttributeSetter)GlfwMousePassthroughAttrib,
                enabled);
            if (s_traceInput)
            {
                Console.WriteLine(
                    $"ProGPU WPF: mouse-passthrough {(enabled ? "ON" : "OFF")} " +
                    $"window#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_window)}");
            }
        }
        catch
        {
            // Best-effort: never let a cursor/passthrough tweak break input processing.
        }
    }

    private static void ClearDragPassthroughHosts()
    {
        if (s_dragPassthroughHosts.Count == 0)
        {
            return;
        }

        foreach (var host in s_dragPassthroughHosts)
        {
            host._dragPassthroughApplied = false;
            host.TrySetMousePassthrough(false);
        }

        s_dragPassthroughHosts.Clear();
    }

    // Every host (the main window plus any secondary windows and popups) registers itself here so
    // that Run()'s frame pump can drive ALL of them each tick. Without this, only the window whose
    // Run() is on the call stack ever received Update/Render ticks - a popup created via
    // WpfPortablePopupActivation never called Run() itself, so its own native window creation and
    // any dispatcher work happened underneath the main window's Run() loop while the main window
    // sat idle until that nested work finished (observed as the main window's toolbar freezing
    // while a ToolTip/Popup was visible).
    private static readonly object s_activeHostsLock = new();
    private static readonly List<ProGpuWpfWindowHost> s_activeHosts = new();

    // Silk.NET's IWindow.Dispose()/Reset() refuses to run while ANY window's DoEvents() is on the
    // call stack ("You cannot call Reset inside of the render loop!"). Now that PumpAllActiveHosts
    // drives every host's DoEvents() from a single loop, a host can legitimately get Dispose()'d
    // reentrantly while another host's DoEvents() call is still on the stack - e.g. hovering the
    // main window forces a ToolTip's popup host to close from inside the main host's own input
    // processing. Track pump reentrancy so Dispose() can defer the actual native window teardown
    // until the current pump tick fully unwinds instead of disposing mid-callback.
    private static int s_pumpDepth;
    private static readonly List<IWindow> s_pendingNativeWindowDisposals = new();

    private readonly ProGpuWpfWindowOptions _options;
    private IWindow? _window;
    private ProGpuWpfCompositionTarget? _target;
    private ProGpuDirectXDevice? _directXDevice;
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
    private readonly List<WpfPortablePopupBridge> _portablePopupBridges = new();
    private readonly WpfPortablePopupService? _portablePopupService;
    private readonly IDisposable? _portablePopupServiceRegistration;
    private object? _wpfRootVisual;
    private double _portablePresentationSourceDpiScaleX = double.NaN;
    private double _portablePresentationSourceDpiScaleY = double.NaN;
    private int _portablePresentationSourceClientWidth = -1;
    private int _portablePresentationSourceClientHeight = -1;
    private bool _isDisposed;
    private bool _isNativeLoopRunning;
    private bool _isLoadingCompositionTarget;
    private bool _disposeNativeWindowWhenLoopExits;
    private bool _hasPresentedFrame;
    private bool _ownsRenderScheduler;
    private bool _isRendering;
    private bool _isInNativeWindowCloseCallback;
    private bool _isForwardingPlatformInput;
    private bool _isProcessingRenderSchedulerWakeup;
    private bool _isProcessingDispatcherWorkWakeup;
    private bool _forceFullWpfReplay;
    private bool _isHostVisible;
    private bool _hasNativeWindowCloseStarted;
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
    private PortableWindowRegion? _windowRegion;

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
        if (!OperatingSystem.IsWindows())
        {
            _portablePopupService = new WpfPortablePopupService(this);
            _portablePopupServiceRegistration = PortableWpfServiceRegistry.RegisterPopupService(_portablePopupService);
        }

        lock (s_activeHostsLock)
        {
            s_activeHosts.Add(this);
        }
    }

    public event EventHandler<ProGpuWpfFrameEventArgs>? Render;

    internal event EventHandler? RenderWakeupRequested;

    internal event EventHandler? UpdateTick;

    public event EventHandler<WpfInputEventArgs>? InputReceived;

    public event EventHandler<WpfDragDropEventArgs>? DragDropReceived;

    public event EventHandler<WpfWindowEventArgs>? WindowEventReceived;

    public event EventHandler<ProGpuWpfWindowClosingEventArgs>? Closing;

    public IWindow? SilkWindow => _window;

    public ProGpuWpfCompositionTarget? CompositionTarget => _target;

    public ProGpuDirectXDevice? DirectXDevice
    {
        get
        {
            ThrowIfDisposed();
            if (_target == null)
            {
                return null;
            }

            if (_directXDevice is { Context: var context } && ReferenceEquals(context, _target.Context))
            {
                return _directXDevice;
            }

            _directXDevice?.Dispose();
            _directXDevice = ProGpuDirectXDevice.FromContext(
                _target.Context,
                new ProGpuDirectXDeviceOptions
                {
                    Label = "ProGPU WPF DirectX Device",
                    MinimumFeatureLevel = DxFeatureLevel.Direct3D9_3
                });
            return _directXDevice;
        }
    }

    public bool IsVisible => _isHostVisible || (_window?.IsVisible ?? false);

    public ProGpuWpfWindowState WindowState => _windowState;

    public string Title => _window?.Title ?? _windowTitle;

    public int Width => _clientWidth;

    public int Height => _clientHeight;

    public int? Left => _window?.Position.X ?? _windowLeft;

    public int? Top => _window?.Position.Y ?? _windowTop;

    public bool Topmost => _window?.TopMost ?? _windowTopmost;

    public ProGpuWpfWindowBorder WindowBorder => _windowBorder;

    public PortableWindowRegion? WindowRegion => _windowRegion;

    public object? PortablePresentationSource => _portablePresentationSourceBridge?.Source;

    public WpfPortablePresentationSourceBridge? PortablePresentationSourceBridge => _portablePresentationSourceBridge;

    internal WpfCursor? LastPortableCursor { get; private set; }

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
            RequestRenderAndWakeNativeLoop();
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

    public bool HasPresentedFrame => Volatile.Read(ref _hasPresentedFrame);

    public ProGpuWpfFrameState LastPresentedFrameState { get; private set; }

    internal RenderSurfaceGeometry LastResolvedRenderSurfaceGeometry { get; private set; }

    internal double CurrentDpiScaleX => ResolveCurrentPortableDpiScale(
        LastResolvedRenderSurfaceGeometry.DpiScaleX,
        _portablePresentationSourceDpiScaleX);

    internal double CurrentDpiScaleY => ResolveCurrentPortableDpiScale(
        LastResolvedRenderSurfaceGeometry.DpiScaleY,
        _portablePresentationSourceDpiScaleY);

    public long SkippedFrameCount { get; private set; }

    public long RetainedWpfReplaySkipCount { get; private set; }

    public long RetainedWpfBranchReplayCount { get; private set; }

    internal bool ForceFullWpfReplayForNextFrame => _forceFullWpfReplay;

    internal long RenderSchedulerWakeupCount { get; private set; }

    internal long DispatcherWakeupCount { get; private set; }

    internal long NativeLoopWakeupCount { get; private set; }

    internal long NativeLoopOwnerActivationCount { get; private set; }

    internal long NativeLoopOwnerIterationCount { get; private set; }

    internal long NativeLoopOwnerDoEventsCallCount { get; private set; }

    internal bool HasGpuHitTestCache => !_isDisposed && _target?.LastGpuHitTestIndex != null;

    public Action<MediaDrawingContext, ProGpuWpfFrameEventArgs>? Draw { get; set; }

    public Action<WpfCompositionDrawingContext, ProGpuWpfFrameEventArgs>? WpfDraw { get; set; }

    internal Func<ProGpuWpfDrawingFrame, IWpfImageSourceAdapter?, IDisposable?> RenderDataSinkProviderRegistrationFactory { get; set; } = RegisterDefaultRenderDataSinkProvider;

    /// <summary>
    /// Runs this host's native window loop, but - unlike Silk's own <c>IWindow.Run()</c> - drives
    /// every currently active <see cref="ProGpuWpfWindowHost"/> (this window plus any secondary
    /// windows and popups) each tick via <see cref="DoEvents"/>, so windows created while this loop
    /// is running (e.g. a ToolTip's popup host) get their own Update/Render ticks instead of
    /// stalling until this window's loop notices them.
    /// </summary>
    public void Run()
    {
        ThrowIfDisposed();
        _isHostVisible = true;
        EnsureWindow();
        _window!.IsVisible = true;
        _isNativeLoopRunning = true;
        TraceNativeLoop("run entering: " + CreateNativeLoopTraceState());
        try
        {
            RunPortableNativeLoop();
        }
        catch (Exception ex)
        {
            TraceNativeLoop("run failed: " + ex);
            throw;
        }
        finally
        {
            _isNativeLoopRunning = false;
            DisposeDeferredNativeWindowIfNeeded();
            TraceNativeLoop("run leaving: " + CreateNativeLoopTraceState());
        }
    }

    private void RunPortableNativeLoop()
    {
        if (!ShouldKeepPortableNativeRunLoopAlive())
        {
            TraceNativeLoop("owner loop skipped: " + CreateNativeLoopTraceState());
            return;
        }

        NativeLoopOwnerActivationCount++;
        TraceNativeLoop("owner loop entering: " + CreateNativeLoopTraceState());
        while (ShouldKeepPortableNativeRunLoopAlive())
        {
            var hadPendingRender = WpfRenderScheduler.HasPendingRenderRequest;
            NativeLoopOwnerDoEventsCallCount++;
            try
            {
                PumpAllActiveHosts();
                DrainPendingNativeWindowDisposals();
            }
            catch (ObjectDisposedException ex) when (!ShouldKeepPortableNativeRunLoopAlive())
            {
                TraceNativeLoop("owner loop close/dispose exit after ObjectDisposedException: " + ex.ObjectName);
                return;
            }
            catch (ObjectDisposedException ex)
            {
                TraceNativeLoop("owner loop unexpected ObjectDisposedException: " + ex);
                throw;
            }
            catch (InvalidOperationException ex) when (!ShouldKeepPortableNativeRunLoopAlive())
            {
                TraceNativeLoop("owner loop close/dispose exit after InvalidOperationException: " + ex.Message);
                return;
            }
            catch (Exception ex)
            {
                TraceNativeLoop("owner loop unexpected exception: " + ex);
                throw;
            }

            NativeLoopOwnerIterationCount++;
            if (!ShouldKeepPortableNativeRunLoopAlive())
            {
                TraceNativeLoop("owner loop stopping after DoEvents: " + CreateNativeLoopTraceState());
                return;
            }

            Thread.Sleep(hadPendingRender || WpfRenderScheduler.HasPendingRenderRequest
                ? PortableNativeLoopActiveDelay
                : PortableNativeLoopIdleDelay);
        }

        TraceNativeLoop("owner loop leaving: " + CreateNativeLoopTraceState());
    }

    private bool ShouldKeepPortableNativeRunLoopAlive()
    {
        var window = _window;
        return !_isDisposed &&
            !_hasNativeWindowCloseStarted &&
            window != null;
    }

    /// <summary>
    /// Runs exactly one tick of the same pump <see cref="Run"/>'s loop body performs, reentrantly if
    /// called while already nested inside a pump (<see cref="s_pumpDepth"/> covers this - native
    /// window teardown mid-tick already defers correctly). Used to let a modal
    /// <c>Dispatcher.PushFrame</c> (WPF <c>Window.ShowDialog</c>) keep servicing real native input -
    /// including the eventual click that closes the dialog - while it's "blocked": this whole stack
    /// is single-threaded, so nothing else can pump native events for us while we wait, unlike
    /// Win32's <c>GetMessage</c> which the OS itself keeps feeding. See docs/menus.md and
    /// WindowsBase's <c>Dispatcher.PushManagedFrameImpl</c>.
    /// </summary>
    internal static void PumpOnce()
    {
        PumpAllActiveHosts();
        DrainPendingNativeWindowDisposals();
    }

    private static void PumpAllActiveHosts()
    {
        ProGpuWpfWindowHost[] hosts;
        lock (s_activeHostsLock)
        {
            hosts = s_activeHosts.ToArray();
        }

        foreach (var host in hosts)
        {
            if (host._isDisposed)
            {
                continue;
            }

            s_pumpDepth++;
            try
            {
                host.DoEvents();
            }
            finally
            {
                s_pumpDepth--;
            }
        }
    }

    private static void DrainPendingNativeWindowDisposals()
    {
        if (s_pendingNativeWindowDisposals.Count == 0)
        {
            return;
        }

        IWindow[] pending = s_pendingNativeWindowDisposals.ToArray();
        s_pendingNativeWindowDisposals.Clear();
        TraceHostLifecycle("DrainPendingNativeWindowDisposals count=" + pending.Length);
        foreach (var window in pending)
        {
            window.Dispose();
        }
    }

    private static void TraceHostLifecycle(string message)
    {
        if (Environment.GetEnvironmentVariable("LIBREWPF_MENU_INPUT_LOG") != "1")
        {
            return;
        }

        try
        {
            System.IO.File.AppendAllText(
                "/tmp/librewpf-menu-input.log",
                DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + " HOSTLIFECYCLE " + message + Environment.NewLine);
        }
        catch
        {
            // Diagnostics only.
        }
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
            NoteWindowShownForSpuriousUpGuard();
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

        RequestRenderAndWakeNativeLoop();
    }

    public void SetWindowState(ProGpuWpfWindowState windowState)
    {
        ThrowIfDisposed();

        _windowState = windowState;
        if (_window != null)
        {
            _window.WindowState = ToSilkWindowState(windowState);
        }

        RequestRenderAndWakeNativeLoop();
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

        RequestRenderAndWakeNativeLoop();
    }

    public void SetClientSize(int width, int height)
    {
        ThrowIfDisposed();
        SetClientSizeCore(width, height, updatePortablePresentationSource: true);
    }

    public void SetPosition(int left, int top)
    {
        ThrowIfDisposed();

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_TRACE_POPUP_POSITION") == "1")
        {
            try { System.IO.File.AppendAllText("/tmp/tooltiptest_debug.log", DateTime.Now.ToString("HH:mm:ss.fff") + $" [TRACE SetPosition] host={GetHashCode()} left={left} top={top}\n"); } catch { }
        }

        _windowLeft = left;
        _windowTop = top;
        if (_window != null)
        {
            _window.Position = new Vector2D<int>(left, top);
        }

        RequestRenderAndWakeNativeLoop();
    }

    public void SetTopmost(bool topmost)
    {
        ThrowIfDisposed();

        _windowTopmost = topmost;
        if (_window != null)
        {
            _window.TopMost = topmost;
        }

        RequestRenderAndWakeNativeLoop();
    }

    public void SetWindowBorder(ProGpuWpfWindowBorder windowBorder)
    {
        ThrowIfDisposed();

        _windowBorder = windowBorder;
        if (_window != null)
        {
            _window.WindowBorder = ToSilkWindowBorder(windowBorder);
        }

        RequestRenderAndWakeNativeLoop();
    }

    public void SetWindowRegion(PortableWindowRegion region)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(region);

        _windowRegion = region.IsEmpty ? null : region;
        ApplyWindowRegionToCompositionTarget();
        RequestRenderAndWakeNativeLoop();
    }

    private void ApplyWindowRegionToCompositionTarget()
    {
        if (_target == null)
        {
            return;
        }

        _target.SceneRootVisual.GeometryClip = TryCreateWindowRegionClip(_windowRegion, out var clip)
            ? clip
            : null;
    }

    internal static bool TryCreateWindowRegionClip(
        PortableWindowRegion? region,
        out ProGpuPathGeometry? clip)
    {
        clip = null;
        if (region == null || region.IsEmpty || !TryToSceneRect(region.Bounds, out var bounds))
        {
            return false;
        }

        clip = ProGpuPrimitivePathGeometry.CreateRectangle(
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height);

        var excludedRects = region.ExcludedRects;
        for (int i = 0; i < excludedRects.Count; i++)
        {
            if (!TryToSceneRect(excludedRects[i], out var excluded) ||
                !TryIntersect(bounds, excluded, out var clippedExcluded))
            {
                continue;
            }

            clip = new ProGpuPathGeometry
            {
                IsCombined = true,
                PathA = clip,
                PathB = ProGpuPrimitivePathGeometry.CreateRectangle(
                    clippedExcluded.X,
                    clippedExcluded.Y,
                    clippedExcluded.Width,
                    clippedExcluded.Height),
                Op = 0
            };
        }

        return true;
    }

    private static bool TryToSceneRect(PortableRect rect, out ProGpuRect sceneRect)
    {
        if (rect.IsEmpty ||
            !double.IsFinite(rect.X) ||
            !double.IsFinite(rect.Y) ||
            !double.IsFinite(rect.Width) ||
            !double.IsFinite(rect.Height) ||
            rect.Width <= 0 ||
            rect.Height <= 0)
        {
            sceneRect = default;
            return false;
        }

        sceneRect = new ProGpuRect(
            (float)rect.X,
            (float)rect.Y,
            (float)rect.Width,
            (float)rect.Height);
        return float.IsFinite(sceneRect.X) &&
               float.IsFinite(sceneRect.Y) &&
               float.IsFinite(sceneRect.Width) &&
               float.IsFinite(sceneRect.Height) &&
               sceneRect.Width > 0 &&
               sceneRect.Height > 0;
    }

    private static bool TryIntersect(ProGpuRect left, ProGpuRect right, out ProGpuRect intersection)
    {
        float x1 = Math.Max(left.X, right.X);
        float y1 = Math.Max(left.Y, right.Y);
        float x2 = Math.Min(left.Right, right.Right);
        float y2 = Math.Min(left.Bottom, right.Bottom);
        if (x2 <= x1 || y2 <= y1)
        {
            intersection = default;
            return false;
        }

        intersection = new ProGpuRect(x1, y1, x2 - x1, y2 - y1);
        return true;
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

        RequestRenderAndWakeNativeLoop();
    }

    public void DoEvents()
    {
        ThrowIfDisposed();

        // Flushing the WPF dispatcher (here and via the Update/Render events below) can run
        // arbitrary WPF code, including code that disposes THIS host reentrantly - e.g. a ToolTip's
        // popup closing itself in response to input processed during this very tick. Dispose() runs
        // synchronously (only the native window teardown is deferred while a pump is in progress),
        // so `_window`/`_target` can go null partway through this method; bail out immediately after
        // each such call instead of dereferencing a field that Dispose() already cleared.
        ProcessDispatcherQueueCore();
        if (_isDisposed)
        {
            return;
        }

        EnsureWindow();

        IWindow? window = _window;
        if (window == null)
        {
            return;
        }

        // Only touch native visibility on an actual change. Silk's setter maps to a real
        // show/order-front (or hide) call on the underlying platform window; re-asserting it every
        // tick (harmless while nothing pumped popups in a loop) now runs ~60-90x/sec for EVERY host
        // once PumpAllActiveHosts drives them all, and repeatedly re-showing a Topmost popup steals
        // focus/flashes on macOS instead of just staying shown.
        if (window.IsVisible != _isHostVisible)
        {
            window.IsVisible = _isHostVisible;
        }

        if (!window.IsInitialized)
        {
            window.Initialize();
        }

        if (!ShouldKeepPortableNativeRunLoopAlive())
        {
            DisposeDeferredNativeWindowIfNeeded();
            return;
        }

        if (!EnsureCompositionTargetLoaded() || !ShouldKeepPortableNativeRunLoopAlive())
        {
            DisposeDeferredNativeWindowIfNeeded();
            return;
        }

        if (!ReferenceEquals(window, _window))
        {
            return;
        }

        window.DoEvents();
        if (!ShouldKeepPortableNativeRunLoopAlive())
        {
            DisposeDeferredNativeWindowIfNeeded();
            return;
        }

        window.DoUpdate();
        EnsureCompositionTargetLoaded();
        if (ReferenceEquals(window, _window))
        {
            window.DoRender();
        }
        DisposeDeferredNativeWindowIfNeeded();
        if (_isDisposed)
        {
            return;
        }

        ProcessDispatcherQueueCore();
    }

    public void Close()
    {
        if (_window == null)
        {
            return;
        }

        RequestNativeWindowClose(_window);
    }

    public bool SetCursor(WpfCursor cursor)
    {
        ThrowIfDisposed();

        return SetCursorCore(cursor);
    }

    internal bool ApplyPortableCursor(WpfCursor cursor)
    {
        ThrowIfDisposed();

        LastPortableCursor = cursor;
        return SetCursorCore(cursor);
    }

    private bool SetCursorCore(WpfCursor cursor)
    {
        if (_window == null)
        {
            return false;
        }

        if (_attachedInputService is ISilkNetWpfInputContextProvider inputContextProvider &&
            inputContextProvider.TryGetInputContext(_window, out var inputContext))
        {
            return PlatformServices.Cursors.SetCursor(inputContext, cursor);
        }

        return PlatformServices.Cursors.SetCursor(_window, cursor);
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
        object? rootVisual = null,
        double dpiScaleX = 1.0,
        double dpiScaleY = 1.0)
    {
        ThrowIfDisposed();

        if (!WpfPortablePresentationSourceBridge.TryCreate(
                this,
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

        _isDisposed = true;

        lock (s_activeHostsLock)
        {
            s_activeHosts.Remove(this);
        }

        IWindow? window = _window;
        bool deferNativeWindowDispose = window != null &&
            (_isNativeLoopRunning ||
                _isRendering ||
                _isProcessingDispatcherWorkWakeup ||
                _isInNativeWindowCloseCallback ||
                s_pumpDepth > 0);
        bool disposeNativeWindow = window != null && !deferNativeWindowDispose;
        // _isRendering means we're on THIS host's own OnRender call stack right now - e.g. OnRender's
        // ProcessDispatcherQueueCore() drained queued WPF work that turned around and disposed this
        // very host (a popup closing itself as a reentrant side effect of its own render pass, seen in
        // practice when a shared-slot eviction lands here). Silk refuses window.Dispose() mid-render
        // just like it refuses it mid-pump, so this must defer exactly like the pump-depth case below -
        // NOT skip disposal outright. Skipping it left the native window neither hidden nor destroyed,
        // a permanent leak that reproduced as "closed" popups staying visibly open.
        TraceHostLifecycle(
            "Dispose hash=" + GetHashCode() +
            " disposeNativeWindow=" + disposeNativeWindow +
            " deferNativeWindowDispose=" + deferNativeWindowDispose +
            " pumpDepth=" + s_pumpDepth +
            " isRendering=" + _isRendering);

        if (window != null && !deferNativeWindowDispose)
        {
            window.Load -= OnLoad;
            window.Update -= OnUpdate;
            window.Render -= OnRender;
            window.Resize -= OnResize;
            window.Closing -= OnClosing;
        }
        else if (deferNativeWindowDispose)
        {
            _disposeNativeWindowWhenLoopExits = true;
            RequestNativeWindowClose(window!);
        }

        DetachInputService();
        DetachDragDropService();
        DetachWindowEventService();
        DetachDispatcherService();
        DisposePortablePopupService();
        DisposePortablePresentationSourceBridge();
        DisposeTarget();
        if (deferNativeWindowDispose && window != null && s_pumpDepth > 0)
        {
            // Deferring native teardown (Silk refuses Reset()/Dispose() mid-pump) only postpones
            // releasing the window's resources - it must NOT stay on screen in the meantime. Without
            // this, an evicted shared-menu-slot popup (see WpfPortablePopupActivation) keeps showing
            // its last rendered frame until the next full pump tick drains s_pendingNativeWindowDisposals,
            // which can be long enough (or get outraced by another eviction) that several "closed"
            // popups appear to stay open simultaneously.
            window!.IsVisible = false;
            s_pendingNativeWindowDisposals.Add(window);
            // The pending-disposal queue now owns the native window.  Clear the
            // host reference so a re-entrant update/render callback cannot
            // dispose the same Silk window a second time.
            _window = null;
        }
        else if (disposeNativeWindow)
        {
            window!.Dispose();
        }

        DetachRenderScheduler(_wpfRenderScheduler);
        DisposeOwnedRenderScheduler();

        _target = null;
        if (!deferNativeWindowDispose)
        {
            _window = null;
        }
    }

    private void DisposeDeferredNativeWindowIfNeeded()
    {
        if (!_disposeNativeWindowWhenLoopExits ||
            _isNativeLoopRunning ||
            _isRendering ||
            _isProcessingDispatcherWorkWakeup ||
            _isProcessingRenderSchedulerWakeup ||
            _isInNativeWindowCloseCallback ||
            s_pumpDepth > 0)
        {
            return;
        }

        _disposeNativeWindowWhenLoopExits = false;
        IWindow? window = _window;
        if (window == null)
        {
            return;
        }

        window.Load -= OnLoad;
        window.Update -= OnUpdate;
        window.Render -= OnRender;
        window.Resize -= OnResize;
        window.Closing -= OnClosing;
        window.Dispose();
        _window = null;
    }

    private void RequestNativeWindowClose(IWindow window)
    {
        bool closeAlreadyStarted = _hasNativeWindowCloseStarted;
        _hasNativeWindowCloseStarted = true;
        TraceNativeLoop((closeAlreadyStarted ? "close request already pending: " : "close requested: ") + CreateNativeLoopTraceState());
        if (closeAlreadyStarted)
        {
            return;
        }

        window.Close();
        TryRequestNativeLoopWakeup(window.ContinueEvents);
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
        // If a mouse button is held, a drag is in progress and this is a transient overlay window.
        // Create it hidden so ShowCore can suppress focus-on-show (which would otherwise steal the
        // drag-origin window's macOS mouse grab and lose the MouseUp) before making it visible.
        windowOptions.IsVisible = _isHostVisible && !s_mouseButtonPressedSomewhere;
        windowOptions.WindowState = ToSilkWindowState(_windowState);
        windowOptions.TopMost = _windowTopmost;
        windowOptions.WindowBorder = ToSilkWindowBorder(_windowBorder);
        windowOptions.TransparentFramebuffer = _options.TransparentFramebuffer;
        if (_windowLeft.HasValue && _windowTop.HasValue)
        {
            windowOptions.Position = new Vector2D<int>(_windowLeft.Value, _windowTop.Value);
        }

        _window = Window.Create(windowOptions);
        _hasNativeWindowCloseStarted = false;
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Resize += OnResize;
        _window.Closing += OnClosing;
    }

    private void OnLoad()
    {
        EnsureCompositionTargetLoaded();

        // If this window was shown during an active press before its native GLFW handle existed,
        // apply the deferred mouse-passthrough now that the handle is available.
        if (_dragPassthroughApplied)
        {
            TrySetMousePassthrough(true);
        }
    }

    private bool EnsureCompositionTargetLoaded()
    {
        if (_isDisposed || _hasNativeWindowCloseStarted)
        {
            return false;
        }

        if (_isLoadingCompositionTarget)
        {
            TraceNativeLoop("composition target load deferred during reentrant initialization");
            return false;
        }

        if (_target != null)
        {
            return true;
        }

        if (_window == null)
        {
            return false;
        }

        if (!CanCreateNativeRenderSurface(_window))
        {
            return false;
        }

        _isLoadingCompositionTarget = true;
        try
        {
            IWindow window = _window;
            ProGpuWpfCompositionTarget target = ProGpuWpfCompositionTarget.CreateForWindow(window);
            if (_isDisposed || _hasNativeWindowCloseStarted || !ReferenceEquals(window, _window))
            {
                target.Dispose();
                return false;
            }

            _target = target;
            target.RenderInvalidated += OnCompositionTargetRenderInvalidated;
            target.Context.VSync = _options.VSync;
            if (_options.TransparentFramebuffer)
            {
                // A transparent (AllowsTransparency) window must clear to fully-transparent black
                // instead of the compositor's default opaque dark background - otherwise the alpha
                // channel is 1.0 everywhere and the "transparent" framebuffer still composites as an
                // opaque dark fill. This is what makes AvalonDock's OverlayWindow / drop-target compass
                // actually see-through on the portable (Silk.NET/GLFW) backend.
                target.Compositor.ClearColor = new System.Numerics.Vector4(0f, 0f, 0f, 0f);
            }
            ApplyWindowRegionToCompositionTarget();
            if (!CanFinishCompositionTargetLoad(target, window))
            {
                DisposeTarget();
                return false;
            }

            AttachInputService();
            if (!CanFinishCompositionTargetLoad(target, window))
            {
                DisposeTarget();
                return false;
            }

            AttachDragDropService();
            AttachWindowEventService();
            if (!CanFinishCompositionTargetLoad(target, window))
            {
                DisposeTarget();
                return false;
            }

            SynchronizePortablePresentationSourceGeometry();
            RequestRenderAndWakeNativeLoop();
            return true;
        }
        catch
        {
            DisposeTarget();
            throw;
        }
        finally
        {
            _isLoadingCompositionTarget = false;
        }
    }

    private bool CanFinishCompositionTargetLoad(ProGpuWpfCompositionTarget target, IWindow window)
    {
        return !_isDisposed &&
            !_hasNativeWindowCloseStarted &&
            ReferenceEquals(window, _window) &&
            ReferenceEquals(target, _target);
    }

    private static bool CanCreateNativeRenderSurface(IWindow window)
    {
        if (window is not IView view || view.Handle == IntPtr.Zero)
        {
            return false;
        }

        return window is INativeWindowSource { Native: not null };
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
            RequestRenderAndWakeNativeLoop();
            return;
        }

        var geometry = ResolveCurrentRenderSurfaceGeometry();
        SynchronizePortablePresentationSourceGeometry(geometry);
        _target.Context.ConfigureSwapChain(
            geometry.PixelWidth,
            geometry.PixelHeight);
        _target.SceneRootVisual.Invalidate();
        _target.RootVisual.Invalidate();
        RequestRenderAndWakeNativeLoop();
    }

    private void OnUpdate(double deltaSeconds)
    {
        if (_isDisposed)
        {
            DisposeDeferredNativeWindowIfNeeded();
            return;
        }

        TryProcessDispatcherWorkWakeup();
        UpdateTick?.Invoke(this, EventArgs.Empty);
        DisposeDeferredNativeWindowIfNeeded();
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
            if (_isDisposed)
            {
                return;
            }

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

            using var currentContextScope = global::ProGPU.Backend.WgpuContext.PushCurrent(_target.Context);

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
            var frameState = CaptureFrameState(
                _target,
                logicalWidth,
                logicalHeight,
                pixelWidth,
                pixelHeight,
                dpiScale);

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
            var activeWpfImageSourceAdapter = _target.CreateFrameImageSourceAdapter(WpfImageSourceAdapter);
            IReadOnlyList<WpfRetainedVisualBranchReplayTarget> dirtyBranchReplayTargets = Array.Empty<WpfRetainedVisualBranchReplayTarget>();
            var canReplayDirtyWpfBranches = wpfRootVisual != null &&
                shouldReplayWpfRootVisual &&
                !forceFullWpfReplay &&
                _target.TryPrepareDirtyRetainedVisualBranchReplayTargets(
                    wpfRootVisual,
                    activeWpfImageSourceAdapter,
                    out dirtyBranchReplayTargets);
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
                                dirtyBranchReplayTargets,
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

                if (_portablePopupBridges.Count > 0)
                {
                    LastVisualReplayResult = AddWpfVisualReplayResults(
                        LastVisualReplayResult,
                        ReplayPortablePopups(
                            _target,
                            drawingFrame,
                            activeWpfImageSourceAdapter));
                }

                TraceRenderReplayResult(wpfRootVisual, LastVisualReplayResult);

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
                RecordPresentedFrame(CaptureFrameState(
                    _target,
                    logicalWidth,
                    logicalHeight,
                    pixelWidth,
                    pixelHeight,
                    dpiScale));
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
        if (!s_traceRenderSurface)
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

    private void TraceRenderReplayResult(object? rootVisual, WpfVisualReplayResult result)
    {
        if (!s_traceRenderSurface)
        {
            return;
        }

        Console.WriteLine(
            "ProGPU WPF replay: " +
            $"host={GetHashCode():x}, root={rootVisual?.GetType().Name ?? "<null>"}, " +
            $"portableChildren={rootVisual is IPortableVisualChildrenSource}, " +
            $"visuals={result.VisualCount}, content={result.ContentCount}, " +
            $"renderData={result.RenderData.RecordCount}/{result.RenderData.AppliedCount}, " +
            $"unsupported={result.UnsupportedContentCount}/{result.UnsupportedVisualStateCount}");
    }

    private void TraceNativeLoop(string message)
    {
        if (!s_traceNativeLoop)
        {
            return;
        }

        Console.WriteLine("ProGPU WPF native loop: " + message);
    }

    private string CreateNativeLoopTraceState()
    {
        return $"disposed={_isDisposed}, closeStarted={_hasNativeWindowCloseStarted}, " +
            $"hostVisible={_isHostVisible}, hasWindow={_window != null}, " +
            $"ownerActivations={NativeLoopOwnerActivationCount}, ownerDoEvents={NativeLoopOwnerDoEventsCallCount}, " +
            $"ownerIterations={NativeLoopOwnerIterationCount}";
    }

    private WpfVisualReplayResult ReplayPortablePopups(
        ProGpuWpfCompositionTarget target,
        ProGpuWpfDrawingFrame drawingFrame,
        IWpfImageSourceAdapter? activeWpfImageSourceAdapter)
    {
        var result = default(WpfVisualReplayResult);
        for (int i = 0; i < _portablePopupBridges.Count; i++)
        {
            result = AddWpfVisualReplayResults(
                result,
                _portablePopupBridges[i].Replay(
                    target,
                    drawingFrame,
                    WpfResourceResolver,
                    activeWpfImageSourceAdapter));
        }

        return result;
    }

    private static WpfVisualReplayResult AddWpfVisualReplayResults(
        WpfVisualReplayResult left,
        WpfVisualReplayResult right)
    {
        return new WpfVisualReplayResult(
            left.VisualCount + right.VisualCount,
            left.ContentCount + right.ContentCount,
            left.ChildEdgeCount + right.ChildEdgeCount,
            left.UnsupportedContentCount + right.UnsupportedContentCount,
            left.UnsupportedVisualStateCount + right.UnsupportedVisualStateCount,
            new WpfMilDecodeResult(
                left.RenderData.RecordCount + right.RenderData.RecordCount,
                left.RenderData.AppliedCount + right.RenderData.AppliedCount,
                left.RenderData.SkippedCount + right.RenderData.SkippedCount,
                left.RenderData.UnsupportedCount + right.RenderData.UnsupportedCount));
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
            monitorDpiScale,
            trustNativeLogicalSizeWhenFramebufferScalesNative: HasPresentedFrame);
        logicalSize = ReconcileResolvedLogicalClientSize(
            logicalSize,
            framebufferSize,
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

    internal RenderSurfaceGeometry ResolveCurrentRenderSurfaceGeometryForDiagnostics()
    {
        return ResolveCurrentRenderSurfaceGeometry();
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
        bool dpiScaleChanged = UpdatePortablePresentationSourceDpiScale(geometry.DpiScaleX, geometry.DpiScaleY);
        bool clientSizeChanged = UpdatePortablePresentationSourceClientSize(geometry.LogicalWidth, geometry.LogicalHeight);
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
            monitorDpiScale,
            trustNativeLogicalSizeWhenFramebufferScalesNative: HasPresentedFrame);
        logicalSize = ReconcileResolvedLogicalClientSize(
            logicalSize,
            framebufferSize,
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
        Vector2D<int> framebufferSize,
        int cachedWidth,
        int cachedHeight,
        double monitorDpiScale)
    {
        return new Vector2D<int>(
            ReconcileResolvedLogicalClientDimension(
                resolvedSize.X,
                framebufferSize.X,
                cachedWidth,
                monitorDpiScale),
            ReconcileResolvedLogicalClientDimension(
                resolvedSize.Y,
                framebufferSize.Y,
                cachedHeight,
                monitorDpiScale));
    }

    private static int ReconcileResolvedLogicalClientDimension(
        int resolvedDimension,
        int framebufferDimension,
        int cachedDimension,
        double monitorDpiScale)
    {
        if (resolvedDimension <= 0 || cachedDimension <= 0)
        {
            return Math.Max(1, resolvedDimension > 0 ? resolvedDimension : cachedDimension);
        }

        var larger = Math.Max(resolvedDimension, cachedDimension);
        var smaller = Math.Min(resolvedDimension, cachedDimension);
        if (FramebufferDimensionMatchesScaledLogicalDimension(
                resolvedDimension,
                framebufferDimension,
                monitorDpiScale))
        {
            return resolvedDimension;
        }

        return larger == resolvedDimension &&
            DimensionsDifferByDpiScale(larger, smaller, monitorDpiScale)
                ? smaller
                : resolvedDimension;
    }

    private static bool FramebufferDimensionMatchesScaledLogicalDimension(
        int logicalDimension,
        int framebufferDimension,
        double monitorDpiScale)
    {
        var normalizedScale = NormalizeMonitorDpiScale(monitorDpiScale);
        if (logicalDimension <= 0 || framebufferDimension <= 0 || normalizedScale <= 1.0)
        {
            return false;
        }

        return Math.Abs(framebufferDimension - logicalDimension * normalizedScale) <=
            Math.Max(2.0, normalizedScale);
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

        if (_wpfRootVisual is not PortableVisualLayoutStateSource layoutStateSource ||
            !layoutStateSource.TryGetPortableVisualLayoutState(out var layoutState) ||
            !layoutState.HasRenderSize)
        {
            return false;
        }

        var width = layoutState.RenderSize.Width;
        var height = layoutState.RenderSize.Height;
        if (!double.IsFinite(width) ||
            !double.IsFinite(height) ||
            width <= 0.0 ||
            height <= 0.0)
        {
            return false;
        }

        renderSize = new Vector2D<int>(
            Math.Max(1, (int)Math.Ceiling(width)),
            Math.Max(1, (int)Math.Ceiling(height)));
        return true;
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
        return ResolveLogicalClientSize(
            nativeSize,
            framebufferSize,
            cachedWidth,
            cachedHeight,
            monitorDpiScale,
            trustNativeLogicalSizeWhenFramebufferScalesNative: false);
    }

    private static Vector2D<int> ResolveLogicalClientSize(
        Vector2D<int> nativeSize,
        Vector2D<int> framebufferSize,
        int cachedWidth,
        int cachedHeight,
        double monitorDpiScale,
        bool trustNativeLogicalSizeWhenFramebufferScalesNative)
    {
        return new Vector2D<int>(
            ResolveLogicalClientDimension(
                nativeSize.X,
                framebufferSize.X,
                cachedWidth,
                monitorDpiScale,
                trustNativeLogicalSizeWhenFramebufferScalesNative),
            ResolveLogicalClientDimension(
                nativeSize.Y,
                framebufferSize.Y,
                cachedHeight,
                monitorDpiScale,
                trustNativeLogicalSizeWhenFramebufferScalesNative));
    }

    private static int ResolveLogicalClientDimension(
        int nativeDimension,
        int framebufferDimension,
        int cachedDimension,
        double monitorDpiScale,
        bool trustNativeLogicalSizeWhenFramebufferScalesNative)
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

        if (trustNativeLogicalSizeWhenFramebufferScalesNative &&
            FramebufferDimensionMatchesScaledLogicalDimension(nativeDimension, framebufferDimension, dpiScale))
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

    /// <summary>
    /// Resolves this window's true backing/display scale (e.g. 2.0 on a Retina display) directly
    /// from the native window, without requiring a render surface to have been created yet. Used to
    /// seed a popup's PresentationSource DPI at creation time so it matches its owner immediately,
    /// rather than defaulting to 1.0 and only syncing later when (if) the popup host renders through
    /// <see cref="OnRender"/>. On Windows real WPF a popup inherits its owner's DPI for free; this is
    /// the portable equivalent. Falls back to 1.0 if the native window can't report a scale yet.
    /// </summary>
    internal double ResolveWindowBackingScaleForPortableSource()
    {
        if (_isDisposed)
        {
            return 1.0;
        }

        try
        {
            double scale = ResolveCurrentMonitorDpiScale();
            return double.IsFinite(scale) && scale >= 1.0 && scale <= 8.0 ? scale : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private double ResolveCurrentMonitorDpiScale()
    {
        // Prefer the window's live framebuffer/size ratio - the true backing scale (e.g. 2.0 on a
        // Retina display). The platform monitor service derives its scale from GLFW's
        // VideoMode.Resolution / bounds, which on macOS returns a bogus ~1.05 for a genuine 2x
        // display. Feeding that ~1.05 as the monitorDpiScale makes
        // DisplayScaleResolver.ResolveDisplayScaleWithPlatformFallback short-circuit (it treats any
        // value > 1.0 as authoritative) so it never consults the accurate Cocoa backingScaleFactor,
        // and the resulting ~1.0 scale mis-places every popup (menu/ComboBox/ToolTip) by ~2x when
        // the presentation source DPI happens to be synced from this path. The 1-arg overload
        // computes FramebufferSize/Size and falls back to the native backing scale when that ratio
        // is unavailable, so it is accurate in both cases.
        if (_window != null && _window.Size.X > 0 && _window.FramebufferSize.X > 0)
        {
            return DisplayScaleResolver.ResolveWindowDisplayScale(_window);
        }

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

    private static double ResolveCurrentPortableDpiScale(double geometryDpiScale, double cachedDpiScale)
    {
        if (double.IsFinite(geometryDpiScale) && geometryDpiScale > 0.0)
        {
            return NormalizeMonitorDpiScale(geometryDpiScale);
        }

        if (double.IsFinite(cachedDpiScale) && cachedDpiScale > 0.0)
        {
            return NormalizeMonitorDpiScale(cachedDpiScale);
        }

        return 1.0;
    }

    private void OnClosing()
    {
        _isInNativeWindowCloseCallback = true;
        try
        {
            _hasNativeWindowCloseStarted = true;
            TraceNativeLoop("closing event entering: " + CreateNativeLoopTraceState());
            var args = new ProGpuWpfWindowClosingEventArgs();
            Closing?.Invoke(this, args);
            if (args.Cancel)
            {
                if (_window != null)
                {
                    _window.IsClosing = false;
                }

                _hasNativeWindowCloseStarted = false;
                _isHostVisible = true;
                TraceNativeLoop("closing event canceled: " + CreateNativeLoopTraceState());
                RequestRenderAndWakeNativeLoop();
                return;
            }

            _isHostVisible = false;
            DisposeTarget();
            TraceNativeLoop("closing event accepted: " + CreateNativeLoopTraceState());
        }
        finally
        {
            _isInNativeWindowCloseCallback = false;
        }
    }

    private void OnCompositionTargetRenderInvalidated(object? sender, EventArgs e)
    {
        RequestRenderAndWakeNativeLoop();
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

        return !HasPresentedFrame || LastPresentedFrameState != frameState;
    }

    internal void RecordPresentedFrame(ProGpuWpfFrameState frameState)
    {
        LastPresentedFrameState = frameState;
        Volatile.Write(ref _hasPresentedFrame, true);
    }

    private bool HasExplicitFrameCallbacks => Draw != null || WpfDraw != null || Render != null;

    private static ProGpuWpfFrameState CaptureFrameState(
        ProGpuWpfCompositionTarget target,
        uint logicalWidth,
        uint logicalHeight,
        uint pixelWidth,
        uint pixelHeight,
        double dpiScale)
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
            target.LastRetainedBranchInvalidationUsedFallback,
            logicalWidth: logicalWidth,
            logicalHeight: logicalHeight,
            dpiScale: dpiScale);
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

    internal bool TryHitTestOwner(double x, double y, out object? owner)
    {
        owner = null;
        if (!double.IsFinite(x) ||
            !double.IsFinite(y) ||
            x < float.MinValue ||
            x > float.MaxValue ||
            y < float.MinValue ||
            y > float.MaxValue)
        {
            return false;
        }

        ProGpuWpfCompositionTarget? target = GetGpuHitTestTargetAfterRefresh();
        if (target == null)
        {
            return false;
        }

        return target.TryHitTestOwner(
            new System.Numerics.Vector2((float)x, (float)y),
            out owner,
            out _);
    }

    internal bool TryHitTestOwners(double x, double y, out object?[] owners)
    {
        owners = Array.Empty<object?>();
        object?[] ownerBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (!TryHitTestOwners(x, y, ownerBuffer, out int ownerCount))
            {
                return false;
            }

            if (ownerCount == 0)
            {
                return true;
            }

            owners = CopyHitTestResults(ownerBuffer.AsSpan(0, ownerCount));
            return true;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }
    }

    internal bool TryHitTestOwners(double x, double y, Span<object?> owners, out int ownerCount)
    {
        ownerCount = 0;
        if (!double.IsFinite(x) ||
            !double.IsFinite(y) ||
            x < float.MinValue ||
            x > float.MaxValue ||
            y < float.MinValue ||
            y > float.MaxValue)
        {
            return false;
        }

        ProGpuWpfCompositionTarget? target = GetGpuHitTestTargetAfterRefresh();
        if (target == null)
        {
            return false;
        }

        return target.TryHitTestOwners(
            new System.Numerics.Vector2((float)x, (float)y),
            owners,
            out ownerCount,
            out _);
    }

    private ProGpuWpfCompositionTarget? GetGpuHitTestTargetAfterRefresh()
    {
        ProGpuWpfCompositionTarget? target = _target;
        if (_isDisposed || target == null)
        {
            return null;
        }

        if (!_isRendering &&
            !_isForwardingPlatformInput &&
            (target.DetectWpfSourceChanges() ||
                target.WpfInvalidationTracker.IsDirty ||
                WpfRenderScheduler.HasPendingRenderRequest))
        {
            TryProcessRenderSchedulerWakeup();
        }

        target = _target;
        return _isDisposed ? null : target;
    }

    internal bool TryQueryHitTestBoundsOwners(double minX, double minY, double maxX, double maxY, out object?[] owners)
    {
        owners = Array.Empty<object?>();
        object?[] ownerBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (!TryQueryHitTestBoundsOwners(minX, minY, maxX, maxY, ownerBuffer, out int ownerCount))
            {
                return false;
            }

            if (ownerCount == 0)
            {
                return true;
            }

            owners = CopyHitTestResults(ownerBuffer.AsSpan(0, ownerCount));
            return true;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }
    }

    internal bool TryQueryHitTestBoundsOwners(double minX, double minY, double maxX, double maxY, Span<object?> owners, out int ownerCount)
    {
        ownerCount = 0;
        if (!double.IsFinite(minX) ||
            !double.IsFinite(minY) ||
            !double.IsFinite(maxX) ||
            !double.IsFinite(maxY) ||
            minX < float.MinValue ||
            minX > float.MaxValue ||
            minY < float.MinValue ||
            minY > float.MaxValue ||
            maxX < float.MinValue ||
            maxX > float.MaxValue ||
            maxY < float.MinValue ||
            maxY > float.MaxValue)
        {
            return false;
        }

        ProGpuWpfCompositionTarget? target = GetGpuHitTestTargetAfterRefresh();
        if (target == null)
        {
            return false;
        }

        return target.TryQueryHitTestBoundsOwners(
            new System.Numerics.Vector2((float)minX, (float)minY),
            new System.Numerics.Vector2((float)maxX, (float)maxY),
            owners,
            out ownerCount,
            out _);
    }

    internal bool TryGetGpuHitTestCacheSnapshot(out ProGpuWpfDiagnostics.GpuHitTestCacheSnapshot snapshot)
    {
        snapshot = default;
        ProGpuWpfCompositionTarget? target = GetGpuHitTestTargetAfterRefresh();
        if (target == null)
        {
            return false;
        }

        var index = target.LastGpuHitTestIndex;
        snapshot = new ProGpuWpfDiagnostics.GpuHitTestCacheSnapshot(
            index is not null,
            target.LastGpuHitTestDeviceIndex is not null,
            index?.Primitives.Count ?? 0,
            index?.Nodes.Count ?? 0,
            index?.PrimitiveIndices.Count ?? 0,
            index?.PathSegments.Count ?? 0,
            target.GpuHitTestOwnerMap.Count);
        return true;
    }

    private static object?[] CopyHitTestResults(ReadOnlySpan<object?> results)
    {
        if (results.IsEmpty)
        {
            return Array.Empty<object?>();
        }

        var copy = new object?[results.Length];
        results.CopyTo(copy);
        return copy;
    }

    internal bool TryQueryHitTestBoundsCandidates(double minX, double minY, double maxX, double maxY, out object?[] candidates)
    {
        candidates = Array.Empty<object?>();
        object?[] candidateBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (!TryQueryHitTestBoundsCandidates(minX, minY, maxX, maxY, candidateBuffer, out int candidateCount))
            {
                return false;
            }

            if (candidateCount == 0)
            {
                return true;
            }

            candidates = CopyHitTestResults(candidateBuffer.AsSpan(0, candidateCount));
            return true;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(candidateBuffer, clearArray: true);
        }
    }

    internal bool TryQueryHitTestBoundsCandidates(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)
    {
        candidateCount = 0;
        if (!double.IsFinite(minX) ||
            !double.IsFinite(minY) ||
            !double.IsFinite(maxX) ||
            !double.IsFinite(maxY) ||
            minX < float.MinValue ||
            minX > float.MaxValue ||
            minY < float.MinValue ||
            minY > float.MaxValue ||
            maxX < float.MinValue ||
            maxX > float.MaxValue ||
            maxY < float.MinValue ||
            maxY > float.MaxValue)
        {
            return false;
        }

        ProGpuWpfCompositionTarget? target = GetGpuHitTestTargetAfterRefresh();
        if (target == null)
        {
            return false;
        }

        return target.TryQueryHitTestBoundsCandidates(
            new System.Numerics.Vector2((float)minX, (float)minY),
            new System.Numerics.Vector2((float)maxX, (float)maxY),
            candidates,
            out candidateCount,
            out _);
    }

    internal bool TryQueryHitTestEllipseCandidates(double minX, double minY, double maxX, double maxY, out object?[] candidates)
    {
        candidates = Array.Empty<object?>();
        object?[] candidateBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (!TryQueryHitTestEllipseCandidates(minX, minY, maxX, maxY, candidateBuffer, out int candidateCount))
            {
                return false;
            }

            if (candidateCount == 0)
            {
                return true;
            }

            candidates = CopyHitTestResults(candidateBuffer.AsSpan(0, candidateCount));
            return true;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(candidateBuffer, clearArray: true);
        }
    }

    internal bool TryQueryHitTestEllipseCandidates(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)
    {
        candidateCount = 0;
        if (!double.IsFinite(minX) ||
            !double.IsFinite(minY) ||
            !double.IsFinite(maxX) ||
            !double.IsFinite(maxY) ||
            minX < float.MinValue ||
            minX > float.MaxValue ||
            minY < float.MinValue ||
            minY > float.MaxValue ||
            maxX < float.MinValue ||
            maxX > float.MaxValue ||
            maxY < float.MinValue ||
            maxY > float.MaxValue)
        {
            return false;
        }

        ProGpuWpfCompositionTarget? target = GetGpuHitTestTargetAfterRefresh();
        if (target == null)
        {
            return false;
        }

        return target.TryQueryHitTestEllipseCandidates(
            new System.Numerics.Vector2((float)minX, (float)minY),
            new System.Numerics.Vector2((float)maxX, (float)maxY),
            candidates,
            out candidateCount,
            out _);
    }

    private void AttachInputService()
    {
        if (_window == null || _isDisposed || _hasNativeWindowCloseStarted)
        {
            return;
        }

        IWindow window = _window;
        TraceNativeLoop(
            $"input attach entering: host={GetHashCode():x}, handle={window.Handle}, " +
            $"hadSubscription={_inputSubscription != null}");
        DetachInputService();

        var input = PlatformServices.Input;
        try
        {
            input.InputReceived += OnPlatformInputReceived;
            IDisposable inputSubscription = input.Attach(window);
            if (_isDisposed ||
                _hasNativeWindowCloseStarted ||
                !ReferenceEquals(window, _window))
            {
                inputSubscription.Dispose();
                input.InputReceived -= OnPlatformInputReceived;
                TraceNativeLoop($"input attach canceled after host close: host={GetHashCode():x}, handle={window.Handle}");
                return;
            }

            _inputSubscription = inputSubscription;
            _attachedInputService = input;
            TraceNativeLoop($"input attached: host={GetHashCode():x}, handle={window.Handle}");
        }
        catch (PlatformNotSupportedException)
        {
            input.InputReceived -= OnPlatformInputReceived;
            _inputSubscription = null;
            _attachedInputService = null;
        }
        catch
        {
            input.InputReceived -= OnPlatformInputReceived;
            throw;
        }
    }

    private void DetachInputService()
    {
        IWindow? window = _window;
        bool hadSubscription = _inputSubscription != null;
        if (hadSubscription)
        {
            TraceNativeLoop(
                $"input detach entering: host={GetHashCode():x}, handle={window?.Handle ?? IntPtr.Zero}");
        }

        _inputSubscription?.Dispose();
        _inputSubscription = null;

        if (_attachedInputService != null)
        {
            _attachedInputService.InputReceived -= OnPlatformInputReceived;
            _attachedInputService = null;
        }

        if (hadSubscription && window != null)
        {
            TraceNativeLoop($"input detached: host={GetHashCode():x}, handle={window.Handle}");
        }
    }

    private void OnPlatformInputReceived(object? sender, WpfInputEventArgs e)
    {
        if (_isDisposed || _isInNativeWindowCloseCallback)
        {
            return;
        }

        if (e.SourceWindow != null && !ReferenceEquals(e.SourceWindow, _window))
        {
            // This window host only processes input tagged to its own native window. There is no
            // cross-window mouse-capture escape hatch here: even when Mouse.Captured points at an
            // element in THIS window, input physically over a *different* native window (e.g. an
            // AvalonDock resizer-ghost overlay Window shown mid-splitter-drag) is dropped here
            // rather than redirected to the capture owner. That's why a splitter drag stalls once
            // the overlay appears (DragDelta/Up never reach the captured Thumb). See the drop trace
            // below; PROGPU_WPF_TRACE_INPUT=1 makes it visible.
            if (s_traceInput
                && (e.Kind == WpfInputEventKind.MouseMove
                    || e.Kind == WpfInputEventKind.MouseDown
                    || e.Kind == WpfInputEventKind.MouseUp))
            {
                // Capture owner (Mouse.Captured) isn't referenceable from this host layer - read it
                // on the app side (tooltiptest splitter log / AvalonDock avd.input.query). The
                // decisive fact here is that THIS window is dropping a mouse event that belongs to a
                // different native window while a drag is in progress.
                Console.WriteLine(
                    "ProGPU WPF input drop (cross-window): " +
                    $"{e.Kind} x {e.X:0.###}, y {e.Y:0.###}, " +
                    $"sourceWindow#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e.SourceWindow)} " +
                    $"hostWindow#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_window)}");
            }

            return;
        }

        // Track this window's own press/move state and swallow the spurious post-window-show button-up
        // (see NoteWindowShownForSpuriousUpGuard). Runs before forwarding so the phantom up never
        // reaches WPF's input system and can't end an in-progress capture/drag.
        switch (e.Kind)
        {
            case WpfInputEventKind.MouseDown:
                _mouseButtonDownSeen = true;
                _mouseMovedSinceDown = false;
                _mouseDownX = e.X;
                _mouseDownY = e.Y;
                s_mouseButtonPressedSomewhere = true;
                break;

            case WpfInputEventKind.MouseMove:
                if (_mouseButtonDownSeen)
                {
                    _mouseMovedSinceDown = true;
                }
                break;

            case WpfInputEventKind.MouseUp:
                if (_mouseButtonDownSeen
                    && !_mouseMovedSinceDown
                    && Environment.TickCount64 - s_lastWindowShownTicks <= SpuriousUpAfterWindowShowMs
                    && Math.Abs(e.X - _mouseDownX) < 2.0
                    && Math.Abs(e.Y - _mouseDownY) < 2.0)
                {
                    // Phantom up induced by another window being shown while this button was held.
                    // Consume it (button stays logically pressed so the drag continues), and disarm
                    // so only the first such up is swallowed.
                    s_lastWindowShownTicks = long.MinValue;
                    if (s_traceInput)
                    {
                        Console.WriteLine(
                            "ProGPU WPF input: swallowed spurious post-window-show MouseUp " +
                            $"x {e.X:0.###}, y {e.Y:0.###}");
                    }
                    return;
                }

                _mouseButtonDownSeen = false;
                s_mouseButtonPressedSomewhere = false;
                // Real release: undo any mouse-passthrough we applied to transient windows shown
                // during this press.
                ClearDragPassthroughHosts();
                break;
        }

        TraceInputEvent("native", e);
        var input = NormalizeInputEventForCurrentRenderSurface(e);
        TraceInputEvent("wpf", input);
        _isForwardingPlatformInput = true;
        try
        {
            InputReceived?.Invoke(this, input);
            if (!ReferenceEquals(input, e))
            {
                e.Handled = input.Handled;
            }
        }
        finally
        {
            _isForwardingPlatformInput = false;
        }

        RequestRenderAndWakeNativeLoop();
    }

    internal bool TryProcessPortablePopupInput(WpfInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);

        for (int i = _portablePopupBridges.Count - 1; i >= 0; i--)
        {
            if (_portablePopupBridges[i].TryProcessInput(input))
            {
                return true;
            }
        }

        return false;
    }

    internal void RaiseInputForDiagnostics(WpfInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        OnPlatformInputReceived(null, input);
    }

    private static void TraceInputEvent(string stage, WpfInputEventArgs input)
    {
        if (!s_traceInput)
        {
            return;
        }

        string character = input.Character.HasValue
            ? input.Character.Value.ToString()
            : string.Empty;
        Console.WriteLine(
            "ProGPU WPF input " +
            $"{stage}: {input.Kind}, " +
            $"key '{input.Key ?? string.Empty}', " +
            $"scan {input.ScanCode}, " +
            $"char '{character}', " +
            $"x {input.X:0.###}, y {input.Y:0.###}, " +
            $"delta {input.DeltaX:0.###},{input.DeltaY:0.###}, " +
            $"button {input.Button}, modifiers {input.Modifiers}, handled {input.Handled}");
    }

    private static bool IsTraceEnabled(string environmentVariable)
    {
        return Environment.GetEnvironmentVariable(environmentVariable) == "1";
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
            NativeInputCoordinatesLookPhysical(_window.Size, geometry, input));
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
            input.Modifiers,
            input.SourceWindow)
        {
            Handled = input.Handled
        };
        return normalized;
    }

    internal static bool NativeInputCoordinatesLookPhysical(
        Vector2D<int> nativeSize,
        RenderSurfaceGeometry geometry,
        WpfInputEventArgs input)
    {
        if (!IsPointerInput(input.Kind))
        {
            return false;
        }

        return PointerInputCoordinateExceedsLogicalClient(input, geometry);
    }

    internal static bool NativeWindowSizeLooksPhysical(
        Vector2D<int> nativeSize,
        RenderSurfaceGeometry geometry)
    {
        if (geometry.DpiScale <= 1.0 + double.Epsilon)
        {
            return false;
        }

        var viewportWidth = ResolveGeometryViewportDimension(geometry.ViewportWidth, geometry.PixelWidth);
        var viewportHeight = ResolveGeometryViewportDimension(geometry.ViewportHeight, geometry.PixelHeight);
        var nativeWidth = Math.Abs(nativeSize.X);
        var nativeHeight = Math.Abs(nativeSize.Y);
        if (nativeWidth <= 0 || nativeHeight <= 0)
        {
            return false;
        }

        return NativeDimensionLooksPhysical(nativeWidth, geometry.LogicalWidth, geometry.PixelWidth, viewportWidth) &&
            NativeDimensionLooksPhysical(nativeHeight, geometry.LogicalHeight, geometry.PixelHeight, viewportHeight);
    }

    private static bool NativeDimensionLooksPhysical(
        int nativeDimension,
        uint logicalDimension,
        uint pixelDimension,
        uint viewportDimension)
    {
        if (logicalDimension == 0u ||
            NativeDimensionMatches(nativeDimension, logicalDimension))
        {
            return false;
        }

        return NativeDimensionMatches(nativeDimension, pixelDimension) ||
            NativeDimensionMatches(nativeDimension, viewportDimension);
    }

    private static bool NativeDimensionMatches(int nativeDimension, uint targetDimension)
    {
        return targetDimension > 0u &&
            Math.Abs(nativeDimension - (int)targetDimension) <= 1;
    }

    internal static bool PointerInputCoordinateExceedsLogicalClient(
        WpfInputEventArgs input,
        RenderSurfaceGeometry geometry)
    {
        if (!IsPointerInput(input.Kind))
        {
            return false;
        }

        return PointerCoordinateExceedsLogicalClient(input.X, geometry.LogicalWidth) ||
            PointerCoordinateExceedsLogicalClient(input.Y, geometry.LogicalHeight);
    }

    private static bool PointerCoordinateExceedsLogicalClient(double coordinate, uint logicalDimension)
    {
        if (!double.IsFinite(coordinate) || coordinate < 0.0 || logicalDimension == 0u)
        {
            return false;
        }

        return coordinate > logicalDimension + 1.0;
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
        RequestRenderAndWakeNativeLoop();
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
        RequestRenderAndWakeNativeLoop();
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

        ProGpuWpfCompositionTarget target = _target;
        _target = null;
        _directXDevice?.Dispose();
        _directXDevice = null;
        target.RenderInvalidated -= OnCompositionTargetRenderInvalidated;
        target.Dispose();
        WpfRenderScheduler.Reset();
        LastPresentedFrameState = default;
        Volatile.Write(ref _hasPresentedFrame, false);
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
        if (_isDisposed)
        {
            return;
        }

        RenderSchedulerWakeupCount++;
        RenderWakeupRequested?.Invoke(this, EventArgs.Empty);
        if (!TryProcessRenderSchedulerWakeup())
        {
            TryRequestNativeLoopWakeup();
        }
    }

    internal bool TryRequestNativeLoopWakeup()
    {
        var window = _window;
        return window != null && TryRequestNativeLoopWakeup(window.ContinueEvents);
    }

    internal void RequestRenderAndWakeNativeLoop()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            WpfRenderScheduler.RequestRender();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        TryRequestNativeLoopWakeup();
    }

    internal void InvalidateWpfSourceForPortableRender(object? source)
    {
        if (_target == null)
        {
            return;
        }

        object? dirtySource = source ?? _wpfRootVisual;
        if (dirtySource != null)
        {
            _target.WpfInvalidationTracker.MarkDirty(dirtySource);
            return;
        }

        _target.WpfInvalidationTracker.MarkDirty();
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
        if (_isDisposed || _window == null || _isRendering || _isProcessingRenderSchedulerWakeup)
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
            try
            {
                _window.DoRender();
            }
            catch (Exception ex) when (IsRecoverableDispatcherRenderException(ex))
            {
                RequestRenderAndWakeNativeLoop();
                return false;
            }

            return true;
        }
        finally
        {
            _isProcessingRenderSchedulerWakeup = false;
            DisposeDeferredNativeWindowIfNeeded();
        }
    }

    private static bool IsRecoverableDispatcherRenderException(Exception exception)
    {
        var baseException = exception.GetBaseException();
        return baseException is InvalidOperationException invalidOperation &&
            invalidOperation.Message.IndexOf(
                "dispatcher processing is suspended",
                StringComparison.OrdinalIgnoreCase) >= 0;
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

    internal bool TryCreatePortablePopup(
        PortablePopupCreateRequest request,
        out object? presentationSource)
    {
        presentationSource = null;
        if (_isDisposed ||
            request == null ||
            !OwnsPortablePopupOwner(request.OwnerPresentationSource, request.OwnerHandle))
        {
            return false;
        }

        if (!WpfPortablePopupBridge.TryCreate(this, request, out var bridge))
        {
            return false;
        }

        _portablePopupBridges.Add(bridge!);
        presentationSource = bridge!.Source;
        RequestRenderAndWakeNativeLoop();
        return true;
    }

    internal bool TrySetPortablePopupPosition(object presentationSource, int x, int y)
    {
        if (!TryFindPortablePopup(presentationSource, out var popup))
        {
            return false;
        }

        popup.TrySetPosition(x, y);
        return true;
    }

    internal bool TrySetPortablePopupSize(object presentationSource, int width, int height)
    {
        if (!TryFindPortablePopup(presentationSource, out var popup))
        {
            return false;
        }

        popup.TrySetSize(width, height);
        return true;
    }

    internal bool TryShowPortablePopup(object presentationSource)
    {
        if (!TryFindPortablePopup(presentationSource, out var popup))
        {
            return false;
        }

        popup.TryShow();
        return true;
    }

    internal bool TryHidePortablePopup(object presentationSource)
    {
        if (!TryFindPortablePopup(presentationSource, out var popup))
        {
            return false;
        }

        popup.TryHide();
        return true;
    }

    internal bool TrySetPortablePopupHitTestable(object presentationSource, bool hitTestable)
    {
        if (!TryFindPortablePopup(presentationSource, out var popup))
        {
            return false;
        }

        popup.TrySetHitTestable(hitTestable);
        return true;
    }

    internal bool TryDestroyPortablePopup(object presentationSource)
    {
        if (!TryFindPortablePopup(presentationSource, out var popup))
        {
            return false;
        }

        _portablePopupBridges.Remove(popup);
        popup.Dispose();
        RequestRenderAndWakeNativeLoop();
        return true;
    }

    internal void ClearPortablePopups()
    {
        if (_portablePopupBridges.Count == 0)
        {
            return;
        }

        DisposePortablePopupBridges();
        RequestRenderAndWakeNativeLoop();
    }

    private bool OwnsPortablePopupOwner(object? ownerPresentationSource, IntPtr ownerHandle)
    {
        var rootBridge = _portablePresentationSourceBridge;
        if (rootBridge != null &&
            (ReferenceEquals(ownerPresentationSource, rootBridge.Source) ||
             (ownerHandle != IntPtr.Zero && ownerHandle == rootBridge.Handle)))
        {
            return true;
        }

        for (int i = 0; i < _portablePopupBridges.Count; i++)
        {
            var popup = _portablePopupBridges[i];
            if (ReferenceEquals(ownerPresentationSource, popup.Source) ||
                (ownerHandle != IntPtr.Zero && ownerHandle == popup.Handle))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryFindPortablePopup(object presentationSource, out WpfPortablePopupBridge popup)
    {
        if (presentationSource != null)
        {
            for (int i = _portablePopupBridges.Count - 1; i >= 0; i--)
            {
                popup = _portablePopupBridges[i];
                if (ReferenceEquals(presentationSource, popup.Source))
                {
                    return true;
                }
            }
        }

        popup = null!;
        return false;
    }

    private void DisposePortablePopupService()
    {
        _portablePopupServiceRegistration?.Dispose();
        DisposePortablePopupBridges();
    }

    private void DisposePortablePopupBridges()
    {
        for (int i = _portablePopupBridges.Count - 1; i >= 0; i--)
        {
            _portablePopupBridges[i].Dispose();
        }

        _portablePopupBridges.Clear();
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
        // If a mouse button is held, this window is being shown during an in-progress drag (e.g. an
        // AvalonDock overlay/floating ghost). On macOS, a window that takes key focus on show breaks
        // the drag-origin window's implicit mouse grab, so the subsequent MouseUp never reaches it and
        // the drag can't complete. EnsureWindow created it hidden in that case; initialize it so its
        // native handle exists, suppress focus-on-show, then make it visible so the origin keeps grab.
        if (s_mouseButtonPressedSomewhere && !_window!.IsVisible)
        {
            if (!_window.IsInitialized)
            {
                _window.Initialize();
            }
            TrySetFocusOnShow(false);
        }
        _window!.IsVisible = true;
        NoteWindowShownForSpuriousUpGuard();

        if (!_window.IsInitialized)
        {
            _window.Initialize();
        }
        else if (requestRenderWhenInitialized)
        {
            RequestRenderAndWakeNativeLoop();
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
