using System.Reflection;
using System.Windows.Media.ProGPU.Platform;
using Silk.NET.Windowing;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("not a window")]
    public void NonactivatingDisplayRejectsNonWindowObjects(object? value)
    {
        Assert.False(new SilkNetWpfWindowDecorationService().TryShowWithoutActivation(value!));
    }

    [Fact]
    public void UnknownPopupProviderCannotRequestAnActivatingFallback()
    {
        var (window, probe) = CreatePopupProviderProbe();
        Assert.Throws<PlatformNotSupportedException>(() =>
            new SilkNetWpfWindowDecorationService().TryShowWithoutActivation(window));
        Assert.Equal(0, probe.VisibilityWrites);
        Assert.Equal(0, probe.OpaqueHandleReads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void NonactivatingDisplayRejectsColdOrClosingWindows(bool initialized, bool closing)
    {
        var (window, probe) = CreatePopupProviderProbe();
        probe.Initialized = initialized;
        probe.Closing = closing;
        var service = new SilkNetWpfWindowDecorationService();
        Assert.Throws<InvalidOperationException>(() => service.TryShowWithoutActivation(window));
        Assert.Equal(0, probe.NativeReads);
        Assert.Equal(0, probe.VisibilityWrites);
    }

    [Fact]
    public void NonactivatingProviderFailureRemainsTheOriginalError()
    {
        var (window, probe) = CreatePopupProviderProbe();
        var failure = new DllNotFoundException("Provider lookup failure");
        probe.NativeFailure = failure;
        Assert.Same(failure, Assert.Throws<DllNotFoundException>(() =>
            new SilkNetWpfWindowDecorationService().TryShowWithoutActivation(window)));
        Assert.Equal(0, probe.VisibilityWrites);
        Assert.Equal(0, probe.OpaqueHandleReads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void PopupAdmissionRejectsNonWindowIdentitiesBeforeNativeAccess(bool ownerIsWindow, bool popupIsWindow)
    {
        var (owner, ownerProbe) = CreatePopupProviderProbe();
        var (popup, popupProbe) = CreatePopupProviderProbe();
        object ownerValue = ownerIsWindow ? owner : new object();
        object popupValue = popupIsWindow ? popup : new object();
        var service = new SilkNetWpfWindowDecorationService();
        Assert.False(service.TryPreparePopupOwner(ownerValue, popupValue));
        Assert.False(service.TryShowOwnedPopup(ownerValue, popupValue,
            () => Assert.Fail("Rejected identities must not invoke display.")));
        Assert.Equal(0, ownerProbe.NativeReads);
        Assert.Equal(0, popupProbe.NativeReads);
    }

    [Fact]
    public void PopupOwnerMustHaveAnActualNativeIdentityOnEveryPlatform()
    {
        var (owner, ownerProbe) = CreatePopupProviderProbe();
        var (popup, popupProbe) = CreatePopupProviderProbe();
        var service = new SilkNetWpfWindowDecorationService();
        Assert.False(service.TryPreparePopupOwner(owner, popup));
        Assert.False(service.TryShowOwnedPopup(owner, popup,
            () => Assert.Fail("An absent native owner must not invoke display.")));
        Assert.Equal(0, ownerProbe.OpaqueHandleReads);
        Assert.Equal(0, popupProbe.OpaqueHandleReads);
        Assert.Equal(0, popupProbe.NativeReads);
        Assert.Equal(0, popupProbe.VisibilityWrites);
    }

    [Fact]
    public void PopupShowRejectsNullCallbackBeforeProviderAccess()
    {
        var (window, probe) = CreatePopupProviderProbe();
        Assert.Throws<ArgumentNullException>(() =>
            new SilkNetWpfWindowDecorationService().TryShowOwnedPopup(window, window, null!));
        Assert.Equal(0, probe.NativeReads);
        Assert.Equal(0, probe.VisibilityWrites);
    }

    [Fact]
    public void PopupOwnerCloseIntentDoesNotReplaceNativeIdentityValidation()
    {
        var (owner, ownerProbe) = CreatePopupProviderProbe();
        var (popup, popupProbe) = CreatePopupProviderProbe();
        ownerProbe.Closing = true;
        var failure = new InvalidOperationException("Native owner lookup");
        ownerProbe.NativeFailure = failure;
        var service = new SilkNetWpfWindowDecorationService();
        // Closing may be cancelled by the application's confirmation dialog.
        // Retain native owner validation rather than equating intent to release.
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => service.TryPreparePopupOwner(owner, popup)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => service.TryShowOwnedPopup(owner, popup, () => { })));
        Assert.Equal(0, popupProbe.NativeReads);
        Assert.Equal(0, popupProbe.VisibilityWrites);
    }

    private static (IWindow Window, PopupProviderProbe Probe) CreatePopupProviderProbe()
    {
        var window = DispatchProxy.Create<IWindow, PopupProviderProbe>();
        return (window, (PopupProviderProbe)(object)window);
    }

    public class PopupProviderProbe : DispatchProxy
    {
        internal bool Initialized = true, Closing;
        internal int NativeReads, OpaqueHandleReads, VisibilityWrites;
        internal Exception? NativeFailure;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            switch (method!.Name)
            {
                case "get_IsInitialized": return Initialized;
                case "get_IsClosing": return Closing;
                case "get_Native":
                    NativeReads++;
                    if (NativeFailure is not null) throw NativeFailure;
                    return null;
                case "get_Handle":
                    OpaqueHandleReads++;
                    throw new InvalidOperationException("An opaque handle is not a native provider.");
                case "set_IsVisible":
                    VisibilityWrites++;
                    throw new InvalidOperationException("An unsupported provider must not show.");
                default: throw new InvalidOperationException("Unexpected provider access: " + method.Name);
            }
        }
    }
}
