using ProGPU.Wpf.ShowcaseApp;
using Xunit;

namespace ProGPU.Wpf.Tests;

public class MenuLifecycleJournalTests
{
    [Fact]
    public void RecordsOrderedTransitionsWithoutWritingOutput()
    {
        var journal = new MenuLifecycleJournal();
        journal.Record("open", () => "loaded=True");
        journal.Record("close", () => "submenu=False");
        string[] lines = journal.FormatFailure().Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Contains("count=2 truncated=False stopwatch-frequency=", lines[0]);
        Assert.EndsWith(" open: loaded=True", lines[1]);
        Assert.EndsWith(" close: submenu=False", lines[2]);
        Assert.True(long.Parse(lines[1].Split(' ')[0]) <= long.Parse(lines[2].Split(' ')[0]));
        string source = Read("samples/ProGPU.Wpf.ShowcaseApp/MenuLifecycleJournal.cs");
        Assert.DoesNotContain("Console.", source);
        Assert.DoesNotContain("File.", source);
    }

    [Fact]
    public void CapacityStopsFurtherSourceReads()
    {
        var journal = new MenuLifecycleJournal();
        int reads = 0;
        for (int i = 0; i < MenuLifecycleJournal.Capacity + 20; i++)
            journal.Record("event", () => (++reads).ToString());
        Assert.Equal(64, reads);
        string[] lines = journal.FormatFailure().Split('\n');
        Assert.Equal(65, lines.Length);
        Assert.Contains("count=64 truncated=True", lines[0]);
    }

    [Fact]
    public void RecordLengthIsBounded()
    {
        var journal = new MenuLifecycleJournal();
        journal.Record("event", () => new string('x', 10_000));
        Assert.Equal(768, journal.FormatFailure().Split('\n')[1].Length);
    }

    [Fact]
    public void SourceReadFailureDoesNotEscapeObserver()
    {
        var journal = new MenuLifecycleJournal();
        journal.Record("event", () => throw new InvalidOperationException("source unavailable"));
        Assert.Contains("event: diagnostic-error=source unavailable", journal.FormatFailure());
    }

    [Fact]
    public void NestedReadKeepsOriginalChronology()
    {
        var journal = new MenuLifecycleJournal();
        journal.Record("outer", () =>
        {
            journal.Record("inner", () => "second");
            return "first";
        });
        string[] lines = journal.FormatFailure().Split('\n');
        Assert.EndsWith(" outer: first", lines[1]);
        Assert.EndsWith(" inner: second", lines[2]);
    }

    [Fact]
    public void SourceObserverIsReadOnlyAndDetachesEveryLifetime()
    {
        string source = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.ThemeDiagnostics.cs");
        foreach (string name in new[] { "Loaded", "Unloaded", "SubmenuOpened", "SubmenuClosed",
            "Activated", "Deactivated", "Closed", "Opened" })
        {
            Assert.Contains(name + " +=", source);
            Assert.Contains(name + " -=", source);
        }
        foreach (string name in new[] { "Keyboard.GotKeyboardFocusEvent", "Keyboard.LostKeyboardFocusEvent",
            "Mouse.GotMouseCaptureEvent", "Mouse.LostMouseCaptureEvent" })
        {
            Assert.Contains("window.AddHandler(" + name, source);
            Assert.Contains("_window.RemoveHandler(" + name, source);
        }
        foreach (string forbidden in new[] { ".ApplyTemplate(", ".UpdateLayout(", ".Focus(", ".Capture(",
            ".IsSubmenuOpen =", ".IsOpen =", ".Handled =", "Dispatcher.", "WakeLive" })
            Assert.DoesNotContain(forbidden, source);
        Assert.Contains("_popups.Count < 4", source);
        Assert.Contains("if (_disposed) return;", source);
    }

    [Fact]
    public void FailureOnlyIntegrationRetainsOriginalPopupGateAndPassiveSeparation()
    {
        string source = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.xaml.cs");
        int begin = source.IndexOf("private async Task<string> ValidateLiveFrameworkThemesAsync(", StringComparison.Ordinal);
        int end = source.IndexOf("private async Task<LivePopupSurfaceSnapshot> ValidateLiveMenuPopupSurfaceAsync(", begin, StringComparison.Ordinal);
        string theme = source[begin..end];
        Assert.Equal(1, theme.Split("fileMenuItem.IsSubmenuOpen = true;", StringSplitOptions.None).Length - 1);
        Assert.Contains("expectedPopupChildren: 1,", theme);
        Assert.Contains("exact: false,", theme);
        int failure = theme.IndexOf("catch\n", StringComparison.Ordinal);
        int write = theme.IndexOf("diagnostics?.WriteFailure();", StringComparison.Ordinal);
        Assert.True(failure >= 0 && write > failure);
        Assert.Contains("finally { diagnostics?.Dispose(); }", theme);
        Assert.Contains("CloseLivePopupSurfacesAsync(liveHost, diagnostics)", theme);
        Assert.Contains("for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)", source);
        Assert.Contains("await Task.Delay(LiveValidationRetryDelay);", source);
        Assert.DoesNotContain("ThemeMenuDiagnostics", Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.IdleLayoutClip.cs"));
        string gate = Read("eng/progpu-wpf-layout-clip.sh");
        Assert.Contains("\"ProGPU.Wpf.Tests.MenuLifecycleJournalTests\": 7", gate);
        Assert.Contains("FullyQualifiedName~ProGPU.Wpf.Tests.MenuLifecycleJournalTests.", gate);
    }

    private static string Read(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "eng", "progpu-wpf-sdk-ci.sh")))
                return File.ReadAllText(Path.Combine(directory.FullName, relative));
        throw new FileNotFoundException("Could not find the current LibreWPF source checkout.");
    }
}
