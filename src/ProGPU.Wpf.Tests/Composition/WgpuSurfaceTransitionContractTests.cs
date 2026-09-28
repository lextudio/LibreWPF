using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WgpuSurfaceTransitionContractTests
{
    [Fact]
    public void WpfHostDefersResizeAndRenderingWhileSurfaceCapabilitiesAreUnavailable()
    {
        string host = File.ReadAllText(FindRepoPath("src", "ProGPU.Wpf", "ProGpuWpfWindowHost.cs"));

        Assert.DoesNotContain("_target.Context.TryConfigureSwapChain(", host, StringComparison.Ordinal);
        int defer = host.IndexOf("if (!_target.Context.TryReconfigureIfNeeded(pixelWidth, pixelHeight, waitForNativeCompletion: false))", StringComparison.Ordinal);
        int retry = host.IndexOf("RequestPresentationRetryAndWakeNativeLoop();", defer, StringComparison.Ordinal);
        int stop = host.IndexOf("return;", retry, StringComparison.Ordinal);
        int configured = host.IndexOf("TraceResizeCheckpoint(ProGpuWpfResizeStage.SwapChainConfigureReturned", stop, StringComparison.Ordinal);
        int render = host.IndexOf("if (RenderNativeMilFrame(", configured, StringComparison.Ordinal);
        Assert.True(defer >= 0 && retry > defer && stop > retry && configured > stop && render > configured);
        Assert.DoesNotContain("WaitIdle(", host[defer..configured], StringComparison.Ordinal);
        Assert.DoesNotContain("_target.Context.ConfigureSwapChain(\n            geometry.PixelWidth", host, StringComparison.Ordinal);
        Assert.DoesNotContain("_target.Context.ReconfigureIfNeeded(pixelWidth, pixelHeight);", host, StringComparison.Ordinal);
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }
}
