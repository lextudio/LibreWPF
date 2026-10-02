using System.Windows.Media.ProGPU.Composition;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

// Only the lifetime primitive uses synthetic owners. These controls neither
// manufacture a native paragraph nor invoke font/native/rendering operations.
public sealed class WpfHintedTextLifetimeTests
{
    [Fact]
    public void SiblingReferencesKeepOriginalOwnerUntilTheFinalReferenceEnds()
    {
        var owner = new Owner();
        var lifetime = new WpfHintedTextLifetime(owner);
        using var paragraph = lifetime.Acquire();
        using var continuation = paragraph.Retain();
        using var glyphRun = paragraph.Retain();

        paragraph.Dispose();
        paragraph.Dispose();
        Assert.True(paragraph.IsDisposed);
        Assert.False(continuation.IsDisposed);
        Assert.False(glyphRun.IsDisposed);
        Assert.Equal(0, owner.Calls);

        continuation.Dispose();
        Assert.Equal(0, owner.Calls);
        glyphRun.Dispose();
        glyphRun.Dispose();
        Assert.True(glyphRun.IsDisposed);
        Assert.Equal(1, owner.Calls);
    }

    [Fact]
    public void DisposedReferenceCannotAcquireEvenWhenASiblingRemainsLive()
    {
        var owner = new Owner();
        var lifetime = new WpfHintedTextLifetime(owner);
        using var first = lifetime.Acquire();
        using var sibling = first.Retain();
        first.Dispose();

        Assert.Throws<ObjectDisposedException>(() => first.Retain());
        using var later = sibling.Retain();
        sibling.Dispose();
        Assert.Equal(0, owner.Calls);
        later.Dispose();
        Assert.Equal(1, owner.Calls);
    }

    [Fact]
    public void RetirementRejectsNewReferencesWithoutRevivingTheOwner()
    {
        var owner = new Owner();
        var lifetime = new WpfHintedTextLifetime(owner);
        using var reference = lifetime.Acquire();
        reference.Dispose();

        Assert.Throws<ObjectDisposedException>(() => reference.Retain());
        Assert.Throws<ObjectDisposedException>(() => lifetime.Acquire());
        Assert.Equal(1, owner.Calls);
    }

    [Fact]
    public void LastReferenceFailureRetainsExactOwnerForRetryWithoutEndingTwice()
    {
        var failure = new InvalidOperationException("original hinted owner retirement fault");
        var owner = new Owner { Failure = failure, FailuresRemaining = 1 };
        var lifetime = new WpfHintedTextLifetime(owner);
        using var first = lifetime.Acquire();
        using var last = first.Retain();
        first.Dispose();

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => last.Dispose()));
        Assert.True(last.IsDisposed);
        Assert.Equal(1, owner.Calls);
        Assert.Throws<ObjectDisposedException>(() => last.Retain());
        Assert.Throws<ObjectDisposedException>(() => lifetime.Acquire());

        last.Dispose();
        last.Dispose();
        first.Dispose();
        Assert.Equal(2, owner.Calls);
    }

    [Fact]
    public void RepeatedRetirementFailuresKeepOneEndedReferenceAndTheOriginalOwner()
    {
        var failure = new InvalidOperationException("repeated hinted owner retirement fault");
        var owner = new Owner { Failure = failure, FailuresRemaining = 2 };
        var lifetime = new WpfHintedTextLifetime(owner);
        using var reference = lifetime.Acquire();

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => reference.Dispose()));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => reference.Dispose()));
        Assert.True(reference.IsDisposed);
        Assert.Equal(2, owner.Calls);
        Assert.Throws<ObjectDisposedException>(() => lifetime.Acquire());

        reference.Dispose();
        reference.Dispose();
        Assert.Equal(3, owner.Calls);
    }

    [Fact]
    public void ReentrantRetirementEndsTheOwnerOnceAndRejectsAcquisition()
    {
        var owner = new Owner();
        var lifetime = new WpfHintedTextLifetime(owner);
        using var reference = lifetime.Acquire();
        owner.DuringDispose = () =>
        {
            Assert.True(reference.IsDisposed);
            reference.Dispose();
            Assert.Throws<ObjectDisposedException>(() => reference.Retain());
            Assert.Throws<ObjectDisposedException>(() => lifetime.Acquire());
        };

        reference.Dispose();
        reference.Dispose();
        Assert.Equal(1, owner.Calls);
    }

    [Fact]
    public void ReentrantFailureKeepsTheSameRetirementRetry()
    {
        var failure = new InvalidOperationException("reentrant hinted owner retirement fault");
        var owner = new Owner { Failure = failure, FailuresRemaining = 1 };
        var lifetime = new WpfHintedTextLifetime(owner);
        using var reference = lifetime.Acquire();
        owner.DuringDispose = reference.Dispose;

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => reference.Dispose()));
        Assert.True(reference.IsDisposed);
        Assert.Equal(1, owner.Calls);

        reference.Dispose();
        reference.Dispose();
        Assert.Equal(2, owner.Calls);
    }

    private sealed class Owner : IDisposable
    {
        internal int Calls;
        internal int FailuresRemaining;
        internal Exception? Failure;
        internal Action? DuringDispose;

        public void Dispose()
        {
            Calls++;
            DuringDispose?.Invoke();
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw Failure!;
            }
        }
    }
}
