using System.Reflection;
using System.Windows.Media.ProGPU;
using ProGPU.Wpf.Interop;
using Silk.NET.Windowing;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void NativePopupDeliveryDoesNotTurnUnhandledScrollIntoConsumption(bool native, bool sourceHandled, bool expected)
    {
        using var owner = new ProGpuWpfWindowHost();
        using var popup = new WpfPortableNativePopupHost(owner, new FakePortablePresentationSource(),
            new PortablePopupCreateRequest(null, null, IntPtr.Zero, 0, 0, 0, 0, false, false), 1, 1);
        var packet = new PortablePointerInput(PortablePointerEventKind.Scroll, 1.25, 2.75, 1, -1, 0, 0,
            0.25, -0.5, PortablePointerScrollUnit.Points);
        var input = native ? new WpfInputEventArgs(packet, WpfInputModifiers.None) :
            new WpfInputEventArgs(WpfInputEventKind.MouseWheel, deltaY: -0.5);
        int deliveries = 0;
        popup.SetInputHandler(value => { ++deliveries; value.Handled = sourceHandled; return true; });
        popup.RaiseInputForDiagnostics(input);
        Assert.Equal(1, deliveries);
        Assert.Equal(expected, input.Handled);
        Assert.Same(native ? packet : null, input.NativePointer);
    }

    [Theory]
    [InlineData(false, false, 1, 1)]
    [InlineData(false, true, 1, 0)]
    [InlineData(true, false, 0, 1)]
    [InlineData(true, true, 0, 1)]
    public void PopupQueueDrainDoesNotPollGlobalNativeModalEvents(bool popup, bool modal,
        int expectedModalCalls, int expectedWindowCalls)
    {
        var window = DispatchProxy.Create<IWindow, PopupLifecycleProbe>();
        var probe = (PopupLifecycleProbe)(object)window;
        int modalCalls = 0;
        ProGpuWpfWindowHost.PumpWindowEvents(window, popup, () => { ++modalCalls; return modal; });
        Assert.Equal(expectedModalCalls, modalCalls);
        Assert.Equal(expectedWindowCalls, probe.EventCalls);
    }

    [Fact]
    public void OwnerModalPollFailureDoesNotFallThroughToAnotherNativePoll()
    {
        var window = DispatchProxy.Create<IWindow, PopupLifecycleProbe>();
        var probe = (PopupLifecycleProbe)(object)window;
        var failure = new InvalidOperationException("native modal poll");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            ProGpuWpfWindowHost.PumpWindowEvents(window, false, () => throw failure)));
        Assert.Equal(0, probe.EventCalls);
    }

    [Fact]
    public void PopupInitializationFailurePreservesOriginalErrorAndNativeRetirementRetry()
    {
        using var owner = new ProGpuWpfWindowHost();
        using var popup = new WpfPortableNativePopupHost(owner, new FakePortablePresentationSource(),
            new PortablePopupCreateRequest(null, null, IntPtr.Zero, 0, 0, 0, 0, false, false), 1, 1);
        var host = (ProGpuWpfWindowHost)typeof(WpfPortableNativePopupHost)
            .GetField("_popupHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup)!;
        var window = DispatchProxy.Create<IWindow, PopupLifecycleProbe>();
        var probe = (PopupLifecycleProbe)(object)window;
        var original = new InvalidOperationException("native popup initialization");
        probe.InitializationFailure = original;
        probe.RetirementFailure = new InvalidOperationException("native popup retirement");
        SetPrivateField(host, "_window", window);
        SetPrivateField(host, "_nativeWindowThreadId", Environment.CurrentManagedThreadId);
        try
        {
            Assert.Same(original, Assert.Throws<InvalidOperationException>(popup.Show));
            Assert.Same(window, host.SilkWindow);
            Assert.Equal(1, probe.DisposalCalls);
        }
        finally
        {
            probe.RetirementFailure = null;
            host.Dispose();
        }
        Assert.Null(host.SilkWindow);
        Assert.Equal(2, probe.DisposalCalls);
    }

    public class PopupLifecycleProbe : DispatchProxy
    {
        internal int EventCalls, DisposalCalls;
        internal Exception? InitializationFailure, RetirementFailure;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            switch (method!.Name)
            {
                case "get_IsInitialized": return false;
                case "get_IsClosing": return false;
                case "set_IsVisible": return null;
                case "DoEvents": ++EventCalls; return null;
                case "Initialize": throw InitializationFailure ?? new InvalidOperationException("Unexpected initialization.");
                case "Dispose":
                    ++DisposalCalls;
                    if (RetirementFailure != null) throw RetirementFailure;
                    return null;
                default:
                    if (method.Name.StartsWith("remove_", StringComparison.Ordinal)) return null;
                    throw new InvalidOperationException("Unexpected provider access: " + method.Name);
            }
        }
    }
}
