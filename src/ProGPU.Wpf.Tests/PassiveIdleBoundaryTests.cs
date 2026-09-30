using ProGPU.Wpf.ShowcaseApp;
using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

public class PassiveIdleBoundaryTests
{
    [Fact]
    public void ResizeDiagnosticObserverFailureIsDeferredAndScopeDetachesOnce()
    {
        var expected = new IOException("owned journal failure");
        int observed = 0, detached = 0;
        var scope = new ProGpuWpfResizeDiagnosticScope(_ => { observed++; throw expected; },
            () => detached++, new object(), new object(), new object());
        scope.Record(default); // Must not throw into a native callback.
        scope.Record(default);
        Assert.Same(expected, Assert.Throws<IOException>(scope.ThrowIfFailed));
        scope.Dispose();
        scope.Dispose();
        scope.Record(default);
        Assert.Equal(1, observed);
        Assert.Equal(1, detached);
        Assert.False(scope.CanRecord);
    }

    [Fact]
    public void ResizeDiagnosticCapacityRejectsOverflowWithoutExtraCallback()
    {
        int observed = 0;
        using var scope = new ProGpuWpfResizeDiagnosticScope(_ => observed++, () => { }, null, null, null);
        for (int index = 0; index < 64; index++) scope.Record(default);
        scope.ThrowIfFailed();
        scope.Record(default);
        scope.Record(default);
        Assert.Throws<InvalidOperationException>(scope.ThrowIfFailed);
        Assert.Equal(64, observed);
    }

    [Fact]
    public void ResizeDiagnosticDisposePreservesOriginalProductException()
    {
        var expected = new InvalidOperationException("original resize failure");
        int detached = 0;
        var actual = Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var scope = new ProGpuWpfResizeDiagnosticScope(_ => throw new IOException("journal"),
                () => detached++, null, null, null);
            scope.Record(default);
            throw expected;
        }));
        Assert.Same(expected, actual);
        Assert.Equal(1, detached);
    }

    [Fact]
    public void ResizeDiagnosticPreservesTypedIdentityAndActivityWithoutInferringAcquisition()
    {
        var expected = ResizeCheckpoint();
        ProGpuWpfResizeCheckpoint observed = default;
        var window = new object();
        using var scope = new ProGpuWpfResizeDiagnosticScope(value => observed = value,
            () => { }, window, null, null);
        scope.Record(expected);
        scope.ThrowIfFailed();
        Assert.Equal(expected, observed);
        Assert.Same(window, scope.Window);
        Assert.Null(scope.Target);
        Assert.Null(scope.Source);
        Assert.Equal(Environment.CurrentManagedThreadId, scope.RegistrationThreadId);
        Assert.True(observed.Activity.IsRendering);
        Assert.False(observed.Activity.HasPendingPresentationRequest);
    }

    [Fact]
    public void ResizeJournalPreservesPrimitiveStateAndHasIndependentFixedBudget()
    {
        string path = Path.Combine(Path.GetTempPath(), $"idle-resize-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var journal = new PassiveIdlePhaseJournal(path))
            {
                for (int index = 0; index < 64; index++) journal.WriteResize(ResizeCheckpoint());
                Assert.Throws<InvalidOperationException>(() => journal.WriteResize(ResizeCheckpoint()));
                for (int index = 0; index < 32; index++) journal.Write("phase");
                Assert.Throws<InvalidOperationException>(() => journal.Write("overflow"));
            }
            string[] lines = File.ReadAllLines(path);
            Assert.Equal(96, lines.Length);
            Assert.InRange(new FileInfo(path).Length, 1, 64 * 2049 + 16384);
            using var parsed = System.Text.Json.JsonDocument.Parse(lines[0]);
            var root = parsed.RootElement;
            Assert.Equal("native-resize-checkpoint", root.GetProperty("phase").GetString());
            Assert.Equal(Environment.ProcessId, root.GetProperty("processId").GetInt32());
            Assert.Equal(1, root.GetProperty("sequence").GetInt32());
            var state = root.GetProperty("checkpoint");
            Assert.Equal("NativeSizeAssigning", state.GetProperty("stage").GetString());
            Assert.Equal(19, state.GetProperty("managedThreadId").GetInt32());
            Assert.Equal(17, state.GetProperty("registrationThreadId").GetInt32());
            Assert.False(state.GetProperty("isOwnerThread").GetBoolean());
            Assert.Equal(900, state.GetProperty("argumentWidth").GetInt32());
            Assert.Equal(640, state.GetProperty("argumentHeight").GetInt32());
            Assert.Equal(760, state.GetProperty("clientWidth").GetInt32());
            Assert.Equal(560, state.GetProperty("sourceHeight").GetInt32());
            Assert.Equal(900, state.GetProperty("requestedWidth").GetInt32());
            Assert.True(state.GetProperty("sameWindow").GetBoolean());
            Assert.False(state.GetProperty("sameTarget").GetBoolean());
            Assert.True(state.GetProperty("activity").GetProperty("isRendering").GetBoolean());
            Assert.False(state.GetProperty("activity").GetProperty("hasPendingPresentationRequest").GetBoolean());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResizeJournalFailedWriteDoesNotEscapeObserverAndRetainsFirstFailure()
    {
        string path = Path.Combine(Path.GetTempPath(), $"idle-resize-{Guid.NewGuid():N}.jsonl");
        try
        {
            var journal = new PassiveIdlePhaseJournal(path);
            journal.Dispose();
            using var scope = new ProGpuWpfResizeDiagnosticScope(journal.WriteResize, () => { }, null, null, null);
            scope.Record(ResizeCheckpoint());
            Assert.Throws<ObjectDisposedException>(scope.ThrowIfFailed);
            scope.Fail(new IOException("later"));
            Assert.Throws<ObjectDisposedException>(scope.ThrowIfFailed);
            Assert.Empty(File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    private static ProGpuWpfResizeCheckpoint ResizeCheckpoint() => new(
        ProGpuWpfResizeStage.NativeSizeAssigning, 19, 17, false, true,
        new(true, false, false, false, 3, 0), 900, 640, 760, 560, 760, 560,
        900, 640, true, true, true, true, false, true, false, false, true, true, 2);

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
