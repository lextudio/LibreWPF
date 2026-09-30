using System;
using System.Reflection;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class WpfPortableWindowActivationTests
{
    [Theory]
    [InlineData(false, "none", false)]
    [InlineData(true, "none", false)]
    [InlineData(false, "cancel", false)]
    [InlineData(true, "cancel", false)]
    [InlineData(false, "deactivate", false)]
    [InlineData(true, "deactivate", false)]
    [InlineData(false, "hide", false)]
    [InlineData(true, "hide", false)]
    [InlineData(false, "none", true)]
    [InlineData(true, "none", true)]
    [InlineData(false, "cancel", true)]
    [InlineData(true, "cancel", true)]
    public void MouseUpRetainsReentrantPressLayout(bool queued, string boundary, bool throwAfterPress)
    {
        var service = new NativeCancelActivationService
        {
            QueueInputCallbacks = queued,
            RunQueuedInputOnInputFlush = true
        };
        using var registration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost { WpfRenderScheduler = new CoalescingWpfRenderScheduler() };
        Assert.True(WpfPortableWindowActivation.TryAttach(host, new FakeWindow(),
            new FakePortablePresentationSource(), out var activation));
        using var lease = activation;
        host.WpfRenderScheduler.RequestRender();
        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseDown, button: WpfMouseButton.Left));
        var failure = new ApplicationException("Source mouse-up failed after a new press.");
        service.ProcessInputCallback = input =>
        {
            Assert.Equal((int)WpfInputEventKind.MouseUp, input.Kind);
            service.ProcessInputCallback = null;
            // Once the queued source callback executes it is on the dispatcher;
            // nested native/source ingress is consequently direct.
            service.QueueInputCallbacks = false;
            try
            {
                switch (boundary)
                {
                    case "cancel":
                        RaiseHostInputEvent(host, SilkNetWpfInputService.CreateNativePointerEvent(
                            new NativePointerEvent(NativePointerEventKind.Cancel, 10, 20, 1,
                                -1, 0, NativePointerModifiers.None)));
                        break;
                    case "deactivate":
                        RaiseHostWindowEvent(host, WpfWindowEventKind.Deactivated);
                        RaiseHostWindowEvent(host, WpfWindowEventKind.Activated);
                        break;
                    case "hide":
                        RaiseHostWindowEvent(host, WpfWindowEventKind.Hidden);
                        RaiseHostWindowEvent(host, WpfWindowEventKind.Shown);
                        break;
                }

                RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseDown, button: WpfMouseButton.Left));
                if (throwAfterPress) throw failure;
            }
            finally
            {
                service.QueueInputCallbacks = queued;
            }
        };

        Action release = () => RaiseHostInputEvent(host,
            new WpfInputEventArgs(WpfInputEventKind.MouseUp, button: WpfMouseButton.Left));
        if (throwAfterPress)
            Assert.Same(failure, Assert.Throws<TargetInvocationException>(release).InnerException);
        else
            release();

        // An owner-thread callback queued during a flush now belongs to a real
        // Input operation. Advance that modeled dispatcher turn explicitly.
        service.RunPostedInput();
        Assert.Equal(boundary == "cancel" ? 1 : 0, service.CancelCount);
        service.InputDispatchLog.Clear();
        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseMove, x: 30));
        Assert.Contains("Flush:Render", service.InputDispatchLog);

        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseUp, button: WpfMouseButton.Left));
        service.RunPostedInput();
        service.InputDispatchLog.Clear();
        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseMove, x: 40));
        Assert.DoesNotContain("Flush:Render", service.InputDispatchLog);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void MouseUpRetiresOnlyItsObservedButtonPress(bool queued, bool hadOldPress)
    {
        var service = new TestWindowActivationServiceRegistrar
        {
            QueueInputCallbacks = queued,
            RunQueuedInputOnInputFlush = true
        };
        using var registration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost { WpfRenderScheduler = new CoalescingWpfRenderScheduler() };
        Assert.True(WpfPortableWindowActivation.TryAttach(host, new FakeWindow(),
            new FakePortablePresentationSource(), out var activation));
        using var lease = activation;
        host.WpfRenderScheduler.RequestRender();
        if (hadOldPress)
            RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseDown, button: WpfMouseButton.Left));

        // A different button must not keep the released left button alive. An
        // unmatched up, conversely, must not remove a new press of that button.
        WpfMouseButton nextButton = hadOldPress ? WpfMouseButton.Right : WpfMouseButton.Left;
        service.ProcessInputCallback = input =>
        {
            Assert.Equal((int)WpfInputEventKind.MouseUp, input.Kind);
            service.ProcessInputCallback = null;
            service.QueueInputCallbacks = false;
            try
            {
                RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseDown, button: nextButton));
            }
            finally
            {
                service.QueueInputCallbacks = queued;
            }
        };

        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseUp, button: WpfMouseButton.Left));
        service.RunPostedInput();
        service.InputDispatchLog.Clear();
        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseMove, x: 30));
        Assert.Contains("Flush:Render", service.InputDispatchLog);
        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseUp, button: nextButton));
        service.InputDispatchLog.Clear();
        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseMove, x: 40));
        Assert.DoesNotContain("Flush:Render", service.InputDispatchLog);
    }

    private sealed class NativeCancelActivationService : TestWindowActivationServiceRegistrar,
        IPortableNativePointerInputService
    {
        public int CancelCount { get; private set; }

        public bool TryProcessNativePointerInputEvent(object window, PortablePointerInput input,
            int shortcutModifiers, out bool handled)
        {
            Assert.Equal(PortablePointerEventKind.Cancel, input.Kind);
            Assert.Equal((10d, 20d, 1d), (input.X, input.Y, input.Timestamp));
            Assert.Equal(-1, input.Button);
            CancelCount++;
            handled = true;
            return true;
        }

        public bool TryProcessPresentationSourceNativePointerInputEvent(object source, PortablePointerInput input,
            int shortcutModifiers, out bool handled)
        {
            handled = false;
            return false;
        }
    }
}
