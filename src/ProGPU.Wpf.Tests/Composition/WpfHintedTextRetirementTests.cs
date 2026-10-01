using System.Windows.Media.ProGPU.Composition;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

// Synthetic IDisposable values exercise only the real cleanup primitive;
// they never stand in for native formatting, resource snapshots or source UI.
public sealed class WpfHintedTextRetirementTests
{
    [Fact]
    public void SuccessfulRetirementClearsTheOwnerExactlyOnce()
    {
        var pending = new WpfHintedTextRetirement();
        var owner = new Owner();
        pending.Capture(owner);
        pending.Dispose();
        pending.Dispose();
        Assert.Equal(1, owner.Calls);
        var next = new Owner();
        pending.Capture(next);
        pending.Dispose();
        Assert.Equal(1, next.Calls);
    }

    [Fact]
    public void FailedRetirementPreservesPrimaryErrorAndExactOwnerForRetry()
    {
        var pending = new WpfHintedTextRetirement();
        var primary = new ArgumentException("original publication failure");
        var cleanup = new InvalidOperationException("native retirement failure");
        var owner = new Owner { Failure = cleanup, RemainingFailures = 2 };
        pending.Capture(owner);
        pending.DisposePreservingFailure(primary);
        Assert.Same(cleanup, primary.Data["HintedReflowCleanupFailure"]);
        Assert.Same(cleanup, Assert.Throws<InvalidOperationException>(() => pending.Dispose()));
        pending.Dispose();
        pending.Dispose();
        Assert.Equal(3, owner.Calls);
    }

    [Fact]
    public void AFailedOwnerCannotBeOverwritten()
    {
        var pending = new WpfHintedTextRetirement();
        var owner = new Owner { RemainingFailures = 1, Failure = new InvalidOperationException() };
        pending.Capture(owner);
        Assert.Throws<InvalidOperationException>(() => pending.Dispose());
        var replacement = new Owner();
        Assert.Throws<InvalidOperationException>(() => pending.Capture(replacement));
        pending.Dispose();
        Assert.Equal(2, owner.Calls);
        Assert.Equal(0, replacement.Calls);
    }

    [Fact]
    public void ReentrantCleanupCannotRepeatOrReplaceTheRetiringOwner()
    {
        var pending = new WpfHintedTextRetirement();
        var owner = new Owner { RemainingFailures = 1, Failure = new InvalidOperationException() };
        pending.Capture(owner);
        owner.DuringDispose = () =>
        {
            pending.Dispose();
            Assert.Throws<InvalidOperationException>(() => pending.Capture(new Owner()));
        };
        Assert.Throws<InvalidOperationException>(() => pending.Dispose());
        pending.Dispose();
        Assert.Equal(2, owner.Calls);
    }

    [Fact]
    public void MissingOwnerIsRejectedWithoutChangingAnExistingRetry()
    {
        var pending = new WpfHintedTextRetirement();
        var owner = new Owner();
        pending.Capture(owner);
        Assert.Throws<ArgumentNullException>(() => pending.Capture(null!));
        pending.Dispose();
        Assert.Equal(1, owner.Calls);
    }

    private sealed class Owner : IDisposable
    {
        internal int Calls, RemainingFailures;
        internal Exception? Failure;
        internal Action? DuringDispose;
        public void Dispose()
        {
            Calls++;
            DuringDispose?.Invoke();
            if (RemainingFailures > 0) { RemainingFailures--; throw Failure!; }
        }
    }
}
