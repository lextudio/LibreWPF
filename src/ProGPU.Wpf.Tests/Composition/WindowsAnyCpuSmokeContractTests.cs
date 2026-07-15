using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WindowsAnyCpuSmokeContractTests
{
    [Fact]
    public void SmokeDefersShutdownUntilLoadedReturns()
    {
        var scriptPath = FindRepoPath("eng", "progpu-wpf-windows-anycpu-smoke.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("Dispatcher.BeginInvoke(", script, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Threading.DispatcherPriority.ApplicationIdle", script, StringComparison.Ordinal);
        Assert.Contains("new Action(() => Application.Current.Shutdown(0))", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Console.WriteLine($\"LibreWPF Windows AnyCPU smoke succeeded with {nativePath}.\");\n        Application.Current.Shutdown(0);",
            script,
            StringComparison.Ordinal);
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
