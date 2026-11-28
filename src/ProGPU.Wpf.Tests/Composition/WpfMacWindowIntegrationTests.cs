using System.Windows.Media.ProGPU.Platform;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfMacWindowIntegrationTests
{
    [Theory]
    [InlineData(0, NativeWindowBackdrop.None)]
    [InlineData(1, NativeWindowBackdrop.Transparent)]
    [InlineData(2, NativeWindowBackdrop.Blur)]
    [InlineData(3, NativeWindowBackdrop.Acrylic)]
    [InlineData(4, NativeWindowBackdrop.Mica)]
    [InlineData(5, NativeWindowBackdrop.MicaAlt)]
    [InlineData(99, NativeWindowBackdrop.None)]
    public void BackdropCodesMapToTheControllerBackdrops(int code, NativeWindowBackdrop expected)
    {
        Assert.Equal(expected, WpfMacWindowIntegration.MapBackdrop(code));
    }

    [Theory]
    [InlineData(2, 12L)]
    [InlineData(3, 18L)]
    [InlineData(4, 21L)]
    [InlineData(5, 22L)]
    [InlineData(0, 0L)]
    [InlineData(1, 0L)]
    public void MaterialCodesMatchNsVisualEffectMaterial(int code, long expected)
    {
        Assert.Equal(expected, WpfMacWindowIntegration.MapMaterial(code));
    }

    [Theory]
    [InlineData(0, NativeWindowTheme.Default)]
    [InlineData(1, NativeWindowTheme.Light)]
    [InlineData(2, NativeWindowTheme.Dark)]
    [InlineData(7, NativeWindowTheme.Default)]
    public void AppearanceCodesMapToControllerThemes(int code, NativeWindowTheme expected)
    {
        Assert.Equal(expected, WpfMacWindowIntegration.MapAppearance(code));
    }

    [Theory]
    [InlineData(double.NaN, -1d)]
    [InlineData(0d, -1d)]
    [InlineData(-4d, -1d)]
    [InlineData(double.PositiveInfinity, -1d)]
    [InlineData(52d, 52d)]
    public void TitleBarHeightFallsBackToTheSystemDefault(double requested, double expected)
    {
        Assert.Equal(expected, WpfMacWindowIntegration.MapTitleBarHeight(requested));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    public void OnlyARealBackdropNeedsATransparentFramebuffer(int backdrop, bool expected)
    {
        Assert.Equal(expected, WpfMacWindowIntegration.RequiresTransparentFramebuffer(backdrop));
    }

    [Fact]
    public void WithoutAControllerNothingIsAppliedAndNothingIsReported()
    {
        int applied = 0;
        nint effectView = 0;

        PortableMacWindowResult? result = WpfMacWindowIntegration.Apply(
            controller: null,
            transparentFramebuffer: true,
            new PortableMacWindowRequest(true, 52d, 4, 2),
            ref applied,
            ref effectView);

        Assert.Null(result);
        Assert.Equal(0, applied);
        Assert.Equal(0, effectView);
    }

    [Fact]
    public void ActivationDerivesTheFramebufferFromTheBackdropWithoutRequiringAnUndecoratedWindow()
    {
        string activation = File.ReadAllText(FindRepoPath("src", "ProGPU.Wpf", "WpfPortableWindowActivation.cs"));
        string integration = File.ReadAllText(FindRepoPath("src", "ProGPU.Wpf", "Platform", "WpfMacWindowIntegration.cs"));

        Assert.Contains("state.HasMacWindow && WpfMacWindowIntegration.RequiresTransparentFramebuffer(state.MacBackdrop)", activation, StringComparison.Ordinal);
        Assert.Contains("ApplyMacWindow = (activation, request) =>", activation, StringComparison.Ordinal);

        // The material is a sibling below the content view: the controller's own effect view is a
        // subview of the layer-hosting content view and would cover the rendered frame.
        Assert.Contains("\"addSubview:positioned:relativeTo:\"", integration, StringComparison.Ordinal);
        Assert.Contains("SendObject(contentView, Selector(\"superview\"))", integration, StringComparison.Ordinal);
        Assert.Contains("request.Backdrop >= 2 ? NativeWindowBackdrop.Transparent", integration, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFusedBarDragSkipsElementsMarkedHitTestVisibleInChromeIncludingAncestors()
    {
        string window = File.ReadAllText(FindRepoPath(
            "src", "Microsoft.DotNet.Wpf", "src", "PresentationFramework", "System", "Windows", "Window.cs"));

        Assert.Contains("private bool TryBeginPortableMacCaptionDrag(Point mousePosition, int clickCount)", window, StringComparison.Ordinal);
        Assert.Contains("IsInHitTestVisibleChrome(InputHitTest(mousePosition) as DependencyObject)", window, StringComparison.Ordinal);
        // Custom chrome keeps its own drag path untouched.
        Assert.Contains("if (!_hasPortableCustomChrome)", window, StringComparison.Ordinal);
    }

    private static string FindRepoPath(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }
}
