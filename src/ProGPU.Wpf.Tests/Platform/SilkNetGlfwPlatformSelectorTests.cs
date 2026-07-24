using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class SilkNetGlfwPlatformSelectorTests
{
    [Theory]
    [InlineData("wayland", "wayland-0", ":0", null, (int)LinuxGlfwPlatformPreference.X11)]
    [InlineData("wayland", "wayland-0", null, null, (int)LinuxGlfwPlatformPreference.Any)]
    [InlineData("x11", null, ":0", null, (int)LinuxGlfwPlatformPreference.Any)]
    [InlineData(null, null, ":0", null, (int)LinuxGlfwPlatformPreference.Any)]
    [InlineData("wayland", "wayland-0", ":0", "wayland", (int)LinuxGlfwPlatformPreference.Wayland)]
    [InlineData("wayland", "wayland-0", null, "x11", (int)LinuxGlfwPlatformPreference.X11)]
    [InlineData("x11", null, ":0", "WAYLAND", (int)LinuxGlfwPlatformPreference.Wayland)]
    public void ResolvePreferencePreservesDesktopWindowSemanticsWhenXWaylandIsAvailable(
        string? sessionType,
        string? waylandDisplay,
        string? x11Display,
        string? configuredPreference,
        int expected)
    {
        Assert.Equal(
            (LinuxGlfwPlatformPreference)expected,
            SilkNetGlfwPlatformSelector.ResolvePreference(
                sessionType,
                waylandDisplay,
                x11Display,
                configuredPreference));
    }
}
