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
    public void SmokeWaitsForTheBuiltAnyCpuAppHostAndPropagatesItsExitCode()
    {
        var scriptPath = FindRepoPath("eng", "progpu-wpf-windows-anycpu-smoke.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("-Filter \"AnyCpuSmoke.exe\"", script, StringComparison.Ordinal);
        Assert.Contains("$appProcess = Start-Process", script, StringComparison.Ordinal);
        Assert.Contains("-FilePath $appHost.FullName", script, StringComparison.Ordinal);
        Assert.Contains("-RedirectStandardOutput $stdoutPath", script, StringComparison.Ordinal);
        Assert.Contains("-RedirectStandardError $stderrPath", script, StringComparison.Ordinal);
        Assert.Contains("Get-Content $stderrPath", script, StringComparison.Ordinal);
        Assert.Contains("$appProcess.WaitForExit(30000)", script, StringComparison.Ordinal);
        Assert.Contains("Stop-Process -Id $appProcess.Id -Force", script, StringComparison.Ordinal);
        Assert.Contains("$appProcess.ExitCode -ne 0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("& $appHost.FullName", script, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet run --project", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeRequiresVisibleTextPixelsInsteadOfProcessSurvivalAlone()
    {
        var scriptPath = FindRepoPath("eng", "progpu-wpf-windows-anycpu-smoke.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("x:Name=\"SmokeText\"", script, StringComparison.Ordinal);
        Assert.Contains("new RenderTargetBitmap", script, StringComparison.Ordinal);
        Assert.Contains("bitmap.Render(SmokeText);", script, StringComparison.Ordinal);
        Assert.Contains("bitmap.CopyPixels", script, StringComparison.Ordinal);
        Assert.Contains("if (coveredPixels < 32)", script, StringComparison.Ordinal);
        Assert.Contains("LibreWPF rendered no visible text pixels", script, StringComparison.Ordinal);
        Assert.Contains("AssertTextRendered();", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SmokeRequiresManagedFrameworkInTheWindowsDependencyFile()
    {
        var scriptPath = FindRepoPath("eng", "progpu-wpf-windows-anycpu-smoke.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("-Filter \"PresentationFramework.dll\"", script, StringComparison.Ordinal);
        Assert.Contains("build output is missing PresentationFramework.dll", script, StringComparison.Ordinal);
        Assert.Contains("-Filter \"AnyCpuSmoke.deps.json\"", script, StringComparison.Ordinal);
        Assert.Contains("lib/net10.0/PresentationFramework.dll", script, StringComparison.Ordinal);
        Assert.Contains("dependency file does not contain PresentationFramework.dll", script, StringComparison.Ordinal);
        foreach (string removed in new[] { "DirectWriteForwarder.dll", "ijwhost.dll" })
        {
            Assert.Contains($"build output unexpectedly contains {removed}", script.Replace("$removedAsset", removed), StringComparison.Ordinal);
        }
        Assert.Contains("dependency file unexpectedly contains", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TransportShipsOnePortablePresentationCoreWithoutRidManagedAssets()
    {
        var project = File.ReadAllText(FindRepoPath(
            "packaging",
            "Microsoft.DotNet.Wpf.GitHub",
            "Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj"));
        var audit = File.ReadAllText(FindRepoPath("eng", "progpu-preview-package-audit.sh"));
        var sdkWorkflow = File.ReadAllText(FindRepoPath(".github", "workflows", "progpu-wpf-sdk.yml"));
        var releaseWorkflow = File.ReadAllText(FindRepoPath(".github", "workflows", "progpu-wpf-release.yml"));

        Assert.DoesNotContain("DirectWriteForwarder", project.Replace("DirectWriteForwarder.dll", string.Empty), StringComparison.Ordinal);
        Assert.DoesNotContain("LibreWpfWindowsManagedPayloadDir", project, StringComparison.Ordinal);
        Assert.DoesNotContain("IjwHost", project, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimes/win-x64/lib", project, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimes/win-arm64/lib", project, StringComparison.Ordinal);
        Assert.Contains("runtimes/win-x64/native", project, StringComparison.Ordinal);
        Assert.Contains("runtimes/win-arm64/native", project, StringComparison.Ordinal);
        Assert.Contains("^runtimes/[^/]+/lib/", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("windows-managed-runtime", sdkWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("windows-managed-runtime", releaseWorkflow, StringComparison.Ordinal);
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
