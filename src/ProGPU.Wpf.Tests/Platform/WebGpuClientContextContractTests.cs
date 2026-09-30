using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

// These guard host integration; the live multi-window harness separately checks
// the actual Silk window before and after native event/render turns.
public sealed class WebGpuClientContextContractTests
{
    [Fact]
    public void WebGpuWindowDisablesClientContextControlBeforeCreation()
    {
        string source = Read("src/ProGPU.Wpf/ProGpuWpfWindowHost.cs");
        string setup = Between(source, "private void EnsureWindow()", "private void OnLoad()");
        int disable = setup.IndexOf("windowOptions.IsContextControlDisabled = true;", StringComparison.Ordinal);
        int create = setup.IndexOf("_window = WpfPopupWindowFactory.Create(", StringComparison.Ordinal);
        Assert.True(disable >= 0 && create > disable,
            "WebGPU windows must disable Silk context rebinding before native window creation.");
        Assert.Equal(1, setup.Split("windowOptions.IsContextControlDisabled = ", StringSplitOptions.None).Length - 1);
        int noSwap = setup.IndexOf("windowOptions.ShouldSwapAutomatically = false;", StringComparison.Ordinal);
        Assert.True(noSwap >= 0 && noSwap < create);
        int standard = setup.IndexOf("() => Window.Create(windowOptions)", StringComparison.Ordinal);
        int owned = setup.IndexOf("NativePopupWindow.CreateOwnedCocoaWindow(", StringComparison.Ordinal);
        Assert.True(standard > create && owned > create,
            "Both standard and owned popup factories must receive the prepared WebGPU window options.");
        Assert.Contains("windowOptions);", setup[owned..], StringComparison.Ordinal);
        Assert.Contains("!createdOwnedCocoa && SilkNetGlfwDpiService.TryConfigureDpiWindowHints()", setup, StringComparison.Ordinal);
        // Transparent X11 windows still require their alpha-capable client visual.
        Assert.Contains("RequiresClientApiForTransparentFramebuffer(", setup, StringComparison.Ordinal);
        Assert.Contains("? GraphicsAPI.Default", setup, StringComparison.Ordinal);
        Assert.Contains(": GraphicsAPI.None;", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("RendererMode", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void InitialClientContextIsStillReleasedBeforeWebGpuSurfaceCreation()
    {
        string source = Read("src/ProGPU.Wpf/ProGpuWpfWindowHost.cs");
        string load = Between(source, "private void OnLoad()", "private void ReleaseUnusedClientGraphicsContext()");
        int release = load.IndexOf("ReleaseUnusedClientGraphicsContext();", StringComparison.Ordinal);
        Assert.True(release >= 0 && load.IndexOf("EnsureCompositionTargetLoaded();", StringComparison.Ordinal) > release);
        string detach = Between(source, "private void ReleaseUnusedClientGraphicsContext()", "private bool EnsureCompositionTargetLoaded()");
        Assert.Contains("_window?.GLContext?.Clear();", detach, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(", detach, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeHarnessChecksActualContextOwnershipAfterEveryWindowTurn()
    {
        string source = Read("src/ProGPU.Wpf.MultiWindowSmokeHarness/Program.cs");
        Assert.Contains("host.Show();\n        RequireWebGpuContextOwnership(host);", source, StringComparison.Ordinal);
        Assert.Contains("hosts[index].DoEvents();\n                RequireWebGpuContextOwnership(hosts[index]);", source, StringComparison.Ordinal);
        Assert.Contains("!window.IsContextControlDisabled", source, StringComparison.Ordinal);
        Assert.Contains("window.ShouldSwapAutomatically", source, StringComparison.Ordinal);
        Assert.Contains("window.GLContext?.IsCurrent == true", source, StringComparison.Ordinal);
        Assert.Contains("transparent: index > 0", source, StringComparison.Ordinal);
        Assert.Contains("\"smoke reopened\", transparent: true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPresentedWindowRequiresActualGlyphComputeReadback()
    {
        string source = Read("src/ProGPU.Wpf.MultiWindowSmokeHarness/Program.cs");
        string show = Between(source, "private static ProGpuWpfWindowHost ShowWindow(", "private static void AssertExactlyOneRenderDeviceOwner(");
        Assert.Contains("GlyphComputeReadback.Validate(host.CompositionTarget!.Context);", show, StringComparison.Ordinal);
        string probe = Read("src/ProGPU.Wpf.MultiWindowSmokeHarness/GlyphComputeReadback.cs");
        Assert.Contains("GpuComputeExecutionPreference.NativeCompute", probe, StringComparison.Ordinal);
        Assert.Contains("finally { context.ComputeExecutionPreference = original; }", probe, StringComparison.Ordinal);
        Assert.Contains("atlas.AtlasTexture.ReadPixels()", probe, StringComparison.Ordinal);
        Assert.Contains("atlas.RasterComputePassCount == 0", probe, StringComparison.Ordinal);
        Assert.Contains("foreach (GlyphInfo glyph in glyphs)", probe, StringComparison.Ordinal);
        Assert.Contains("RequireCoverage(pixels, atlas.AtlasTexture.Width, atlas.AtlasTexture.Height, glyph)", probe, StringComparison.Ordinal);
        string project = Read("src/ProGPU.Wpf.MultiWindowSmokeHarness/ProGPU.Wpf.MultiWindowSmokeHarness.csproj");
        Assert.Contains("Link=\"Fonts/Inter-Regular.ttf\"", project, StringComparison.Ordinal);
        Assert.Contains("Link=\"Fonts/LICENSE.txt\"", project, StringComparison.Ordinal);
    }

    private static string Between(string source, string start, string end)
    {
        int first = source.IndexOf(start, StringComparison.Ordinal);
        int last = source.IndexOf(end, StringComparison.Ordinal);
        Assert.True(first >= 0 && last > first);
        return source[first..last];
    }

    private static string Read(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "eng/progpu-wpf-sdk-ci.sh")))
                return File.ReadAllText(Path.Combine(directory.FullName, relative)).Replace("\r\n", "\n", StringComparison.Ordinal);
        throw new FileNotFoundException("The LibreWPF checkout is unavailable.");
    }
}
