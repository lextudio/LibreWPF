using ProGPU.Wpf.ShowcaseApp;
using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

public class PassiveIdleBoundaryTests
{
    [Fact]
    public void PhaseJournalPreservesCallerFilesAndRecordsOnlyPhaseIdentity()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"idle-journal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "phases.jsonl");
        try
        {
            using (var journal = new PassiveIdlePhaseJournal(path)) journal.Write("loaded");
            string original = File.ReadAllText(path);
            using var record = System.Text.Json.JsonDocument.Parse(original);
            Assert.Equal("loaded", record.RootElement.GetProperty("phase").GetString());
            Assert.Equal(Environment.ProcessId, record.RootElement.GetProperty("processId").GetInt32());
            Assert.Equal(3, record.RootElement.EnumerateObject().Count());
            Assert.Throws<IOException>(() => new PassiveIdlePhaseJournal(path));
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    [Fact]
    public void PhaseJournalHasFixedRecordBudget()
    {
        string path = Path.Combine(Path.GetTempPath(), $"idle-journal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var journal = new PassiveIdlePhaseJournal(path))
            {
                for (int index = 0; index < 32; index++) journal.Write("phase");
                Assert.Throws<InvalidOperationException>(() => journal.Write("overflow"));
            }
            Assert.Equal(32, File.ReadAllLines(path).Length);
            Assert.InRange(new FileInfo(path).Length, 1, 16384);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(true, false, false, false)] // Current explicit request was already consumed.
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void ImmediateWorkRejectsPassiveAdmission(bool rendering, bool pending, bool recovery, bool dpi)
    {
        var state = new ProGpuWpfRenderActivitySnapshot(rendering, pending, recovery, dpi, 5, 0);
        Assert.True(state.HasImmediatePresentationWork);
    }

    [Fact]
    public async Task NativeUpdateObservesOnlyOnceAndRetiresBeforeReading()
    {
        Action? update = null;
        int wakes = 0, reads = 0;
        Action? saved = null;
        var task = PassiveIdleBoundary.ObserveOnceAsync(
            callback => { saved = update = callback; },
            callback => { if (update == callback) update = null; },
            () => wakes++,
            () => { Assert.Null(update); reads++; return 5; }, TimeSpan.FromSeconds(1));
        Assert.False(task.IsCompleted);
        update!();
        saved!();
        Assert.Equal(5, await task);
        Assert.Null(update);
        Assert.Equal(1, reads);
        Assert.Equal(1, wakes);
    }

    [Fact]
    public async Task PendingFrameFailsOnceWithoutRetryOrEscapingNativeCallback()
    {
        Action? update = null;
        int reads = 0;
        var expected = new InvalidOperationException("pending presentation");
        var task = PassiveIdleBoundary.ObserveOnceAsync<int>(
            callback => update = callback, _ => update = null, () => { },
            () => { reads++; throw expected; }, TimeSpan.FromSeconds(1));
        update!(); // The native event must not receive the callback exception.
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => task));
        Assert.Equal(1, reads);
        Assert.Null(update);
    }

    [Fact]
    public async Task WakeFailureRetiresWithoutObserving()
    {
        Action? update = null;
        var expected = new InvalidOperationException("closed native owner");
        var task = PassiveIdleBoundary.ObserveOnceAsync<int>(
            callback => update = callback, _ => update = null, () => throw expected,
            () => throw new InvalidOperationException("unexpected read"), TimeSpan.FromSeconds(1));
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => task));
        Assert.Null(update);
    }

    [Fact]
    public async Task MissingUpdateTimesOutAndLateCallbackCannotRead()
    {
        Action? update = null, saved = null;
        int reads = 0;
        var task = PassiveIdleBoundary.ObserveOnceAsync(
            callback => saved = update = callback, _ => update = null, () => { },
            () => ++reads, TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<TimeoutException>(() => task);
        saved!();
        Assert.Null(update);
        Assert.Equal(0, reads);
    }

    [Fact]
    public void AbsenceOfImmediateWorkDoesNotHideLaterSameStatePresentation()
    {
        var state = new ProGpuWpfRenderActivitySnapshot(false, false, false, false, 5, 0);
        Assert.False(state.HasImmediatePresentationWork);
        // Future native events or delayed scheduler work are still failures.
        var measured = new PassiveIdleInterval.Result(5, 6, 2000, 0, 0, 0, 0, 0);
        Assert.Throws<InvalidOperationException>(() => measured.RequireIdle());
        Assert.Equal(1, measured.ExtraPresentations);
    }
}
