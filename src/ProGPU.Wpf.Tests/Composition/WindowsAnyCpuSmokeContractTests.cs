using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WindowsAnyCpuSmokeContractTests
{
    [Fact]
    public void SmokeKeepsTextWindowAliveAfterContentIsRendered()
    {
        var scriptPath = FindRepoPath("eng", "progpu-wpf-windows-anycpu-smoke.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("ContentRendered += OnContentRendered", script, StringComparison.Ordinal);
        Assert.Contains("ContentRendered -= OnContentRendered", script, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Threading.DispatcherTimer", script, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromSeconds(1)", script, StringComparison.Ordinal);
        Assert.Contains("_renderLifetimeTimer.Start();", script, StringComparison.Ordinal);
        Assert.Contains("private void OnRenderLifetimeElapsed", script, StringComparison.Ordinal);
        Assert.Contains("_renderLifetimeTimer.Stop();", script, StringComparison.Ordinal);
        Assert.Contains("Application.Current.Shutdown(0);", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.ApplicationIdle", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeLaunchesTheBuiltAnyCpuAppHostDirectly()
    {
        var scriptPath = FindRepoPath("eng", "progpu-wpf-windows-anycpu-smoke.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("-Filter \"AnyCpuSmoke.exe\"", script, StringComparison.Ordinal);
        Assert.Contains("& $appHost.FullName", script, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet run --project", script, StringComparison.Ordinal);
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }
}
