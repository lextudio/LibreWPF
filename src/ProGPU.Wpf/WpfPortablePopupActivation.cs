using System;
using System.Threading;
using ProGPU.Wpf.Interop;
using System.Windows.Media.ProGPU.Platform;

namespace System.Windows.Media.ProGPU;

using PlatformServicesInstance = CrossPlatformWpfPlatformServices;

/// <summary>
/// Portable (non-Windows) backing for <c>System.Windows.Controls.Primitives.Popup</c>: gives each
/// open popup a genuine, separate native window (mirroring real Windows WPF's WS_POPUP HwndSource
/// per <c>Popup</c>), instead of rendering it as an overlay inside the owning window.
/// </summary>
public sealed class WpfPortablePopupActivation : IDisposable
{
    // Menu/dropdown popups (MenuItem submenus, ContextMenu - see Popup.UsesSharedPortablePopupWindow)
    // all funnel through this single slot instead of getting their own native window. Real Windows
    // WPF gets "opening a sibling top-level menu closes the previous one" for free because HWND
    // mouse capture spans windows; our per-window input pipeline doesn't forward capture across
    // separate native popup windows, so without this a stale dropdown could be left on screen.
    // Forcing every menu popup through one shared slot makes that impossible by construction: opening
    // a new one always evicts (disposes) whatever previously held the slot first.
    private static WpfPortablePopupActivation? s_currentSharedMenuOccupant;

    // Tracks how many popups (from this process) are currently open. Used to suppress spurious
    // Deactivated events on the main window — a popup's native window steals real OS focus on this
    // backend (Silk/GLFW has no WS_EX_NOACTIVATE equivalent), which fires a Deactivated that would
    // otherwise confuse MenuBase.IsMenuMode / Mouse.Capture. Real Windows WPF popups never trigger
    // that event, so suppressing it while any of our own popups are open is the correct approximation.
    private static int s_openPopupCount;

    internal static bool HasAnyOpenPopup => s_openPopupCount > 0;

    private bool _isDisposed;
    private bool _occupiesSharedMenuSlot;

    private WpfPortablePopupActivation(ProGpuWpfWindowHost host, object presentationSource)
    {
        Host = host;
        PresentationSource = presentationSource;
    }

    public ProGpuWpfWindowHost Host { get; }

    public object PresentationSource { get; }

    public static bool TryRegisterPresentationFrameworkPopupActivation()
    {
        if (!PortableWpfServiceRegistry.TryGetPopupActivationService(
                PortableWpfServiceKey.PresentationFramework,
                out var activationService))
        {
            return false;
        }

        activationService.Register(CreatePopupActivationCallbacks());
        return true;
    }

    private static PortablePopupActivationCallbacks CreatePopupActivationCallbacks()
    {
        return new PortablePopupActivationCallbacks(
            create: (x, y, transparent, useSharedWindow) =>
                TryCreate(x, y, transparent, useSharedWindow, out var activation) ? activation : null,
            getPresentationSource: activation => ((WpfPortablePopupActivation)activation).PresentationSource,
            show: activation => ((WpfPortablePopupActivation)activation).Show(),
            hide: activation => ((WpfPortablePopupActivation)activation).Hide(),
            setPosition: (activation, position, x, y, size, width, height) =>
                ((WpfPortablePopupActivation)activation).SetPosition(position, x, y, size, width, height),
            dispose: activation => ((WpfPortablePopupActivation)activation).Dispose(),
            getScreenOrigin: WpfPortableWindowActivation.TryGetScreenOrigin,
            getMonitorBounds: TryGetMonitorBoundsForPoint);
    }

    private static bool TryGetMonitorBoundsForPoint(double screenX, double screenY, out double left, out double top, out double width, out double height)
    {
        left = top = width = height = 0;

        try
        {
            var monitors = PlatformServicesInstance.Instance.Monitors.GetMonitors();
            if (monitors == null || monitors.Count == 0)
            {
                return false;
            }

            foreach (var m in monitors)
            {
                if (screenX >= m.X && screenX < m.X + m.Width &&
                    screenY >= m.Y && screenY < m.Y + m.Height)
                {
                    left = m.X;
                    top = m.Y;
                    width = m.Width;
                    height = m.Height;
                    return true;
                }
            }

            // Point isn't on any known monitor; fall back to primary.
            foreach (var m in monitors)
            {
                if (m.IsPrimary)
                {
                    left = m.X;
                    top = m.Y;
                    width = m.Width;
                    height = m.Height;
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryCreate(double x, double y, bool transparent, bool useSharedWindow, out WpfPortablePopupActivation? activation)
    {
        activation = null;

        TracePopupLifecycle(
            "TryCreate useSharedWindow=" + useSharedWindow +
            " hasOccupant=" + (s_currentSharedMenuOccupant != null) +
            " occupantHash=" + (s_currentSharedMenuOccupant?.GetHashCode().ToString() ?? "null"));

        if (useSharedWindow && s_currentSharedMenuOccupant is { } previousOccupant)
        {
            TracePopupLifecycle("TryCreate evicting occupantHash=" + previousOccupant.GetHashCode());
            // Evict whoever currently holds the shared menu slot before creating the new one - see
            // the comment on s_currentSharedMenuOccupant for why this is the fix. WPF's own Popup for
            // the evicted menu still thinks IsOpen == true until its own dismiss logic eventually
            // runs, but Dispose() here is idempotent (guarded by _isDisposed), so that later call just
            // no-ops harmlessly once it does.
            //
            // Releasing WPF's Mouse.Capture (held by the evicted menu) happens on the WPF side, in
            // Popup.BuildWindow, right before this Create call - this ProGPU.Wpf layer has no
            // reference to real WPF's Mouse/input types (its own "PresentationCore" reference is
            // ProGPU's minimal rendering stub, not Microsoft.DotNet.Wpf's PresentationCore).
            previousOccupant.Dispose();
        }

        var options = new ProGpuWpfWindowOptions
        {
            Title = string.Empty,
            Width = 1,
            Height = 1,
            Left = (int)Math.Round(x),
            Top = (int)Math.Round(y),
            IsVisible = false,
            Topmost = true,
            WindowBorder = ProGpuWpfWindowBorder.Hidden,
        };

        var host = new ProGpuWpfWindowHost(options);

        // Pre-increment the popup count BEFORE Initialize() makes the native window
        // visible. On this backend (Silk/GLFW has no WS_EX_NOACTIVATE equivalent), a
        // popup's native window steals real OS focus, which fires a Deactivated event
        // on the main window that would otherwise confuse MenuBase.IsMenuMode. The
        // Deactivated suppression in WpfPortableWindowActivation checks HasAnyOpenPopup,
        // so the count must already be >0 before the window appears on screen.
        int preCount = Interlocked.Increment(ref s_openPopupCount);
        TracePopupLifecycle("HasAnyOpenPopup pre-incremented count=" + preCount);

        host.Initialize();

        if (!host.TryCreatePortablePresentationSource(rootVisual: null, dpiScaleX: 1.0, dpiScaleY: 1.0) ||
            host.PortablePresentationSource is not { } presentationSource)
        {
            Interlocked.Decrement(ref s_openPopupCount);
            host.Dispose();
            return false;
        }

        activation = new WpfPortablePopupActivation(host, presentationSource);

        // Register so PortableWindowActivationService's capture-redirection can resolve this
        // popup's screen origin (see WpfPortableWindowActivation.RegisterPresentationSourceHost for
        // why this matters - without it, hovering from this popup onto a sibling top-level header
        // never redirects/translates the MouseMove against the main window, so the hover-driven
        // menu-switch that real WPF gets for free never fires here).
        WpfPortableWindowActivation.RegisterPresentationSourceHost(presentationSource, host);

        // Feed the popup's own native-window pointer input into the WPF input pipeline. Unlike the
        // main window (wired by WpfPortableWindowActivation), the popup has no owning WPF Window, so
        // its input is routed against the popup PresentationSource instead. Without this hookup a
        // menu dropdown / ContextMenu / ToolTip renders but never sees hover, press, or click.
        host.InputReceived += activation.OnHostInputReceived;

        if (useSharedWindow)
        {
            activation._occupiesSharedMenuSlot = true;
            s_currentSharedMenuOccupant = activation;
        }

        TracePopupLifecycle("TryCreate created activationHash=" + activation.GetHashCode() + " occupiesSlot=" + activation._occupiesSharedMenuSlot);

        return true;
    }

    private static void TracePopupLifecycle(string message)
    {
        if (Environment.GetEnvironmentVariable("LIBREWPF_MENU_INPUT_LOG") != "1")
        {
            return;
        }

        try
        {
            System.IO.File.AppendAllText(
                "/tmp/librewpf-menu-input.log",
                DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + " POPUPACTIVATION " + message + Environment.NewLine);
        }
        catch
        {
            // Diagnostics only.
        }
    }

    private void OnHostInputReceived(object? sender, WpfInputEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (TryForwardInputToPopupSource(e))
        {
            // Forwarding a click can synchronously close+dispose this very popup (a leaf menu item's
            // command tears down the shared menu window), so the host may be gone by the time we get
            // here. Re-check and swallow a late ObjectDisposedException rather than crash the loop.
            if (_isDisposed)
            {
                return;
            }

            try
            {
                // Repaint the popup so hover/press visuals (menu-item highlight) reflect the new state.
                Host.InvalidateWpfSourceForPortableRender(PresentationSource);
                Host.WpfRenderScheduler.RequestRender();
                Host.TryRequestNativeLoopWakeup();
            }
            catch (ObjectDisposedException)
            {
                // Popup was disposed mid-dispatch; nothing left to repaint.
            }
        }
    }

    private bool TryForwardInputToPopupSource(WpfInputEventArgs e)
    {
        if (!PortableWpfServiceRegistry.TryGetWindowActivationService(
                PortableWpfServiceKey.PresentationFramework,
                out var activationService))
        {
            return false;
        }

        var input = new PortableWindowInputEvent(
            (int)e.Kind,
            e.Key,
            e.ScanCode,
            e.Character,
            e.X,
            e.Y,
            e.DeltaX,
            e.DeltaY,
            (int)e.Button,
            (int)e.Modifiers);

        // TryProcessInputEvent accepts the popup's PresentationSource as the routing token (it has no
        // owning WPF Window). The PresentationFramework side dispatches it to ProcessInputForSource.
        if (activationService.TryProcessInputEvent(PresentationSource, input))
        {
            e.Handled = input.Handled;
            return true;
        }

        return false;
    }

    private void Show()
    {
        if (_isDisposed)
        {
            return;
        }

        Host.Show();
    }

    private void Hide()
    {
        if (_isDisposed)
        {
            return;
        }

        Host.Hide();
    }

    private void SetPosition(bool position, double x, double y, bool size, double width, double height)
    {
        if (_isDisposed)
        {
            return;
        }

        if (position)
        {
            Host.SetPosition((int)Math.Round(x), (int)Math.Round(y));
        }

        if (size)
        {
            Host.SetClientSize(Math.Max(1, (int)Math.Round(width)), Math.Max(1, (int)Math.Round(height)));
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        TracePopupLifecycle("Dispose activationHash=" + GetHashCode() + " occupiesSlot=" + _occupiesSharedMenuSlot);

        _isDisposed = true;
        int count = Interlocked.Decrement(ref s_openPopupCount);
        TracePopupLifecycle("HasAnyOpenPopup decremented count=" + count);
        Host.InputReceived -= OnHostInputReceived;
        WpfPortableWindowActivation.UnregisterPresentationSourceHost(PresentationSource);
        if (_occupiesSharedMenuSlot && ReferenceEquals(s_currentSharedMenuOccupant, this))
        {
            s_currentSharedMenuOccupant = null;
        }

        Host.Dispose();
    }
}
