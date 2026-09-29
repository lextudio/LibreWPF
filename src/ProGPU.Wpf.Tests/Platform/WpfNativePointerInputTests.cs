using System;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class WpfNativePointerInputTests
{
    [Theory]
    [InlineData(NativePointerEventKind.Move, WpfInputEventKind.MouseMove)]
    [InlineData(NativePointerEventKind.Drag, WpfInputEventKind.MouseMove)]
    [InlineData(NativePointerEventKind.Enter, WpfInputEventKind.MouseMove)]
    [InlineData(NativePointerEventKind.Down, WpfInputEventKind.MouseDown)]
    [InlineData(NativePointerEventKind.Up, WpfInputEventKind.MouseUp)]
    [InlineData(NativePointerEventKind.Scroll, WpfInputEventKind.MouseWheel)]
    [InlineData(NativePointerEventKind.Leave, WpfInputEventKind.MouseLeave)]
    [InlineData(NativePointerEventKind.Cancel, WpfInputEventKind.MouseCancel)]
    public void AdapterRetainsEveryNativeKind(NativePointerEventKind native, WpfInputEventKind expected)
    {
        int button = native is NativePointerEventKind.Down or NativePointerEventKind.Up or NativePointerEventKind.Drag ? 0 : -1;
        var input = SilkNetWpfInputService.CreateNativePointerEvent(new NativePointerEvent(native, -2.5, 7.75, 1, button, 0, 0));
        Assert.Equal(expected, input.Kind);
        Assert.Equal((PortablePointerEventKind)native, input.NativePointer!.Kind);
        Assert.Equal((-2.5, 7.75), (input.X, input.Y));
    }

    [Theory]
    [InlineData(NativePointerScrollUnit.Points, 0.125, -0.125)]
    [InlineData(NativePointerScrollUnit.Lines, 0.25, -0.5)]
    public void DesktopTransformScalesOnlyPointScrollVectors(NativePointerScrollUnit unit, double dx, double dy)
    {
        var input = SilkNetWpfInputService.CreateNativePointerEvent(new NativePointerEvent(
            NativePointerEventKind.Scroll, 40, 80, 9.125, -1, 0, NativePointerModifiers.Super, 0.25, -0.5, unit, 4, 8));
        input.Handled = true;
        var mapped = ProGpuWpfWindowHost.NormalizeNativeDesktopInput(input, new PortableDesktopTransform(-1800, 400, 2, 4));
        Assert.Equal((20d, 20d, dx, dy), (mapped.X, mapped.Y, mapped.DeltaX, mapped.DeltaY));
        Assert.Equal((mapped.X, mapped.Y, dx, dy), (mapped.NativePointer!.X, mapped.NativePointer.Y, mapped.NativePointer.ScrollX, mapped.NativePointer.ScrollY));
        Assert.Equal((9.125, 4u, 8u), (mapped.NativePointer.Timestamp, mapped.NativePointer.ScrollPhase, mapped.NativePointer.MomentumPhase));
        Assert.Equal(input.NativePointer!.Modifiers, mapped.NativePointer.Modifiers);
        Assert.True(mapped.Handled);
        Assert.Same(input, WpfPortablePopupBridge.CreateNativeDiagnosticInput(true, input, 300, 200));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CapabilityReceivesActualTargetAndIndependentHandledResult(bool source, bool handled)
    {
        var service = new NativeService { Handled = handled };
        var target = new object();
        var input = CreateCancel();
        Assert.True(WpfNativePointerInput.Forward(service, target, input, source));
        Assert.Same(target, service.Target);
        Assert.Same(input.NativePointer, service.Input);
        Assert.Equal((int)input.Modifiers, service.Shortcuts);
        Assert.Equal(source, service.IsPresentationSource);
        Assert.Equal(handled, input.Handled);
        Assert.Equal(0, service.LegacyCalls);
    }

    [Fact]
    public void MissingOrRejectedCapabilityCannotFallBackToLegacyInput()
    {
        var input = CreateCancel();
        var legacy = new LegacyService();
        Assert.Throws<PlatformNotSupportedException>(() => WpfNativePointerInput.Forward(null, new object(), input, false));
        Assert.Throws<PlatformNotSupportedException>(() => WpfNativePointerInput.Forward(legacy, new object(), input, true));
        var rejected = new NativeService { Accepted = false };
        Assert.Throws<InvalidOperationException>(() => WpfNativePointerInput.Forward(rejected, new object(), input, false));
        Assert.Equal(0, legacy.LegacyCalls);
        Assert.Equal(0, rejected.LegacyCalls);
        var missingMetadata = new WpfInputEventArgs(WpfInputEventKind.MouseCancel);
        Assert.True(WpfNativePointerInput.RequiresNativeDispatch(missingMetadata));
        Assert.Throws<ArgumentException>(() => WpfNativePointerInput.Forward(rejected, new object(), missingMetadata, true));
    }

    private static WpfInputEventArgs CreateCancel() => SilkNetWpfInputService.CreateNativePointerEvent(
        new NativePointerEvent(NativePointerEventKind.Cancel, 1, 2, 3, -1, 0, NativePointerModifiers.Shift));

    private sealed class NativeService : LegacyService, IPortableNativePointerInputService
    {
        public object? Target;
        public PortablePointerInput? Input;
        public int Shortcuts;
        public bool IsPresentationSource, Handled, Accepted = true;
        public bool TryProcessNativePointerInputEvent(object window, PortablePointerInput input, int shortcutModifiers, out bool handled) =>
            Receive(window, input, shortcutModifiers, false, out handled);
        public bool TryProcessPresentationSourceNativePointerInputEvent(object source, PortablePointerInput input, int shortcutModifiers, out bool handled) =>
            Receive(source, input, shortcutModifiers, true, out handled);
        private bool Receive(object target, PortablePointerInput input, int shortcuts, bool source, out bool handled)
        {
            Target = target; Input = input; Shortcuts = shortcuts; IsPresentationSource = source;
            handled = Handled; return Accepted;
        }
    }

    private class LegacyService : IPortableWindowActivationServiceRegistrar
    {
        public int LegacyCalls;
        public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.PresentationFramework;
        public void Register(PortableWindowActivationCallbacks callbacks) { }
        public bool TryIsCurrentApplicationMainWindow(object window, out bool value) { value = false; return false; }
        public bool TryCloseWindow(object window, out PortableWindowCloseResult result) { result = default; return false; }
        public bool TrySetActivationState(object window, bool active) => false;
        public bool TryBeginInvokeInput(object window, Action callback) => false;
        public bool TryProcessInputEvent(object window, PortableWindowInputEvent input) { LegacyCalls++; return true; }
        public bool TryProcessPresentationSourceInputEvent(object source, PortableWindowInputEvent input) { LegacyCalls++; return true; }
        public bool TryFlushDispatcherOperations(object window, string priority, TimeSpan? timeout) => false;
        public bool TryProcessDragDropEvent(object window, int kind, string[] files, string? text, double x, double y,
            int allowedEffects, int acceptedEffect, out int result) { result = 0; return false; }
        public void Clear() { }
    }
}
