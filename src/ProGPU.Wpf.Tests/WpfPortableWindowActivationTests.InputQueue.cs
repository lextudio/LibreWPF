using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class WpfPortableWindowActivationTests
{
    [Fact]
    public void FlushReentryOwnsPacketsAndReplaysFifoWithLayoutBetweenPressedEvents()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var registration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost { WpfRenderScheduler = new CoalescingWpfRenderScheduler() };
        Assert.True(WpfPortableWindowActivation.TryAttach(host, new FakeWindow(), new FakePortablePresentationSource(), out var activation));
        using var lease = activation;
        service.InputDispatchLog.Clear();
        service.FlushCallback = priority =>
        {
            Assert.Equal("Render", priority);
            service.FlushCallback = null;
            foreach (var packet in new[] { Move(10), Move(20), new WpfInputEventArgs(WpfInputEventKind.MouseUp, x: 30, button: WpfMouseButton.Left) })
            {
                RaiseHostInputEvent(host, packet);
                Assert.True(packet.Handled);
            }
            Assert.Equal(1, service.InputCount); // Only the outer Down has run.
            service.FlushCallback = _ =>
            {
                service.FlushCallback = null;
                RaiseHostInputEvent(host, Move(40)); // Reentry during replay must stay last.
            };
        };
        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseDown, button: WpfMouseButton.Left));
        service.RunPostedInput();
        Assert.Equal(new[] { "ProcessInput:0", "Flush:Render", "ProcessInput:10", "Flush:Render",
            "ProcessInput:20", "Flush:Render", "ProcessInput:30", "Flush:Render", "ProcessInput:40" }, service.InputDispatchLog);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelRetiresPendingMotionButPreservesActualCancelAndNewPress(bool duringReplay)
    {
        var service = new NativeCancelActivationService();
        using var registration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost { WpfRenderScheduler = new CoalescingWpfRenderScheduler() };
        Assert.True(WpfPortableWindowActivation.TryAttach(host, new FakeWindow(), new FakePortablePresentationSource(), out var activation));
        using var lease = activation;
        service.InputDispatchLog.Clear();
        void CancelAndRepress()
        {
            RaiseHostInputEvent(host, SilkNetWpfInputService.CreateNativePointerEvent(
                new NativePointerEvent(NativePointerEventKind.Cancel, 10, 20, 1, -1, 0, NativePointerModifiers.None)));
            RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseDown, x: 30, button: WpfMouseButton.Left));
            RaiseHostInputEvent(host, Move(40));
        }
        service.FlushCallback = _ =>
        {
            service.FlushCallback = null;
            RaiseHostInputEvent(host, Move(10));
            RaiseHostInputEvent(host, Move(20));
            if (!duringReplay) CancelAndRepress();
        };
        service.ProcessInputCallback = input =>
        {
            if (duringReplay && input.X == 10)
            {
                service.ProcessInputCallback = null;
                CancelAndRepress();
            }
        };
        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseDown, button: WpfMouseButton.Left));
        service.RunPostedInput();
        Assert.Equal(1, service.CancelCount);
        Assert.Equal(duringReplay ? new[] { "ProcessInput:0", "ProcessInput:10", "ProcessInput:30", "ProcessInput:40" }
            : new[] { "ProcessInput:0", "ProcessInput:30", "ProcessInput:40" },
            service.InputDispatchLog.Where(item => item.StartsWith("ProcessInput:", StringComparison.Ordinal)));
        Assert.Equal("Flush:Render", service.InputDispatchLog.Last());
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("deactivated")]
    [InlineData("disposed")]
    [InlineData("root")]
    [InlineData("modal")]
    public void DeferredInputRejectsRetiredSourceOrModalOwner(string boundary)
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var registration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost { WpfRenderScheduler = new CoalescingWpfRenderScheduler() };
        var source = new FakePortablePresentationSource();
        Assert.True(WpfPortableWindowActivation.TryAttach(host, new FakeWindow(), source, out var activation));
        using var lease = activation;
        IDisposable? modal = null;
        service.FlushCallback = _ =>
        {
            service.FlushCallback = null;
            RaiseHostInputEvent(host, Move(10));
            switch (boundary)
            {
                case "hidden": RaiseHostWindowEvent(host, WpfWindowEventKind.Hidden); break;
                case "deactivated": RaiseHostWindowEvent(host, WpfWindowEventKind.Deactivated); break;
                case "disposed": activation!.Dispose(); break;
                case "root": source.RootVisual = null; break;
                case "modal": modal = PortableModalInputScope.Enter(new object()); break;
            }
        };
        try
        {
            RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseDown, button: WpfMouseButton.Left));
            service.RunPostedInput();
            Assert.Equal(1, service.InputCount);
        }
        finally { modal?.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredInputPreservesFirstExceptionAndUnattemptedFifo(bool throwDuringReplay)
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var registration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost { WpfRenderScheduler = new CoalescingWpfRenderScheduler() };
        Assert.True(WpfPortableWindowActivation.TryAttach(host, new FakeWindow(), new FakePortablePresentationSource(), out var activation));
        using var lease = activation;
        var failure = new ApplicationException("Original source failure");
        service.FlushCallback = _ =>
        {
            service.FlushCallback = null;
            RaiseHostInputEvent(host, Move(10));
            RaiseHostInputEvent(host, Move(20));
            if (!throwDuringReplay) throw failure;
        };
        service.ProcessInputCallback = input =>
        {
            if (throwDuringReplay && input.X == 10)
            {
                service.ProcessInputCallback = null;
                throw failure;
            }
        };
        Exception observed = Assert.ThrowsAny<Exception>(() =>
        {
            RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseDown, button: WpfMouseButton.Left));
            service.RunPostedInput();
        });
        Assert.Same(failure, observed.GetBaseException());
        Assert.Equal(throwDuringReplay ? 2 : 1, service.InputCount);
        RaiseHostInputEvent(host, Move(30));
        service.RunPostedInput();
        Assert.Equal(new[] { "ProcessInput:0", "ProcessInput:10", "ProcessInput:20", "ProcessInput:30" },
            service.InputDispatchLog.Where(item => item.StartsWith("ProcessInput:", StringComparison.Ordinal)));
    }

    [Fact]
    public void OffThreadNativeIngressIsClaimedWhileOwnerFlushIsActive()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var registration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost { WpfRenderScheduler = new CoalescingWpfRenderScheduler() };
        Assert.True(WpfPortableWindowActivation.TryAttach(host, new FakeWindow(), new FakePortablePresentationSource(), out var activation));
        using var lease = activation;
        service.FlushCallback = _ =>
        {
            service.FlushCallback = null;
            var packet = Move(10);
            Assert.True(Task.Run(() => RaiseHostInputEvent(host, packet)).Wait(TimeSpan.FromSeconds(5)));
            Assert.True(packet.Handled);
            Assert.Equal(1, service.InputCount);
        };
        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseDown, button: WpfMouseButton.Left));
        service.RunPostedInput();
        Assert.Equal(2, service.InputCount);
    }

    [Fact]
    public void RejectedDeferredPostDoesNotReplayOrLeavePendingOwnership()
    {
        var service = new TestWindowActivationServiceRegistrar { AcceptPostedInput = false };
        using var registration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost { WpfRenderScheduler = new CoalescingWpfRenderScheduler() };
        Assert.True(WpfPortableWindowActivation.TryAttach(host, new FakeWindow(), new FakePortablePresentationSource(), out var activation));
        using var lease = activation;
        var packet = Move(10);
        service.FlushCallback = _ =>
        {
            service.FlushCallback = null;
            RaiseHostInputEvent(host, packet);
        };
        Exception failure = Assert.ThrowsAny<Exception>(() => RaiseHostInputEvent(host,
            new WpfInputEventArgs(WpfInputEventKind.MouseDown, button: WpfMouseButton.Left)));
        Assert.IsType<InvalidOperationException>(failure.GetBaseException());
        Assert.True(packet.Handled);
        Assert.Equal(1, service.InputCount);
        service.AcceptPostedInput = true;
        RaiseHostInputEvent(host, Move(20));
        service.RunPostedInput();
        Assert.Equal(2, service.InputCount);
        Assert.Equal(20, service.LastInput!.X);
    }

    private static WpfInputEventArgs Move(double x) => new(WpfInputEventKind.MouseMove, x: x);
}
