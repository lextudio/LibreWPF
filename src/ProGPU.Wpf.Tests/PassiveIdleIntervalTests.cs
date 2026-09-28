using System.Diagnostics;
using ProGPU.Wpf.ShowcaseApp;
using Xunit;

namespace ProGPU.Wpf.Tests;

public class PassiveIdleIntervalTests
{
    [Fact]
    public void ExactZeroPresentationsAllowsReportedCpuAllocationAndCollections()
    {
        var result = PassiveIdleInterval.Difference(
            new(12, 0, 50, 100, 1, 2, 3),
            new(12, Stopwatch.Frequency * 2, 10050, 500, 2, 3, 4));
        result.RequireIdle();
        Assert.Equal(0, result.ExtraPresentations);
        Assert.Equal(2000, result.WallMilliseconds);
        Assert.Equal(1, result.CpuMilliseconds);
        Assert.Equal(400, result.AllocatedBytes);
        Assert.Equal(1, result.Gen0Collections);
        Assert.Equal(1, result.Gen1Collections);
        Assert.Equal(1, result.Gen2Collections);
    }

    [Theory]
    [InlineData(12, 13)]
    [InlineData(12, 11)]
    [InlineData(0, 0)]
    public void ExtraMissingOrRegressedPresentationsFail(long before, long after)
    {
        var result = new PassiveIdleInterval.Result(before, after, 2000, 0, 0, 0, 0, 0);
        Assert.Throws<InvalidOperationException>(() => result.RequireIdle());
    }

    [Fact]
    public async Task ObservationReadsOnlyTwoEndpoints()
    {
        using var process = Process.GetCurrentProcess();
        int reads = 0;
        var result = await PassiveIdleInterval.ObserveAsync(process, () => { reads++; return 3; },
            TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, reads);
        Assert.True(result.WallMilliseconds >= 1);
        result.RequireIdle();
    }

    [Fact]
    public async Task EarlyTimerWakeWaitsOnlyForOriginalDeadlineRemainder()
    {
        long now = 0;
        var delays = new List<TimeSpan>();
        await PassiveIdleInterval.WaitForDurationAsync(0, TimeSpan.FromSeconds(2), () => now, duration =>
        {
            delays.Add(duration);
            now = delays.Count == 1 ? Stopwatch.Frequency * 1998 / 1000 : Stopwatch.Frequency * 2;
            return Task.CompletedTask;
        });
        Assert.Equal(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(2) }, delays);
    }

    [Fact]
    public async Task FractionalRemainderUsesANonzeroTimerAndElapsedDeadlineDoesNotWait()
    {
        long now = 0;
        var delays = new List<TimeSpan>();
        await PassiveIdleInterval.WaitForDurationAsync(0, TimeSpan.FromTicks(1), () => now, duration =>
        {
            delays.Add(duration);
            now = Stopwatch.Frequency;
            return Task.CompletedTask;
        });
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(1) }, delays);
        await PassiveIdleInterval.WaitForDurationAsync(0, TimeSpan.FromSeconds(1), () => now,
            _ => throw new InvalidOperationException("An elapsed deadline must not wait again."));
    }

    [Fact]
    public async Task NonadvancingAndRegressedClocksFailWithoutUnboundedRetries()
    {
        int delays = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => PassiveIdleInterval.WaitForDurationAsync(
            0, TimeSpan.FromSeconds(2), () => 0, _ => { ++delays; return Task.CompletedTask; }));
        Assert.Equal(4, delays);
        await Assert.ThrowsAsync<InvalidOperationException>(() => PassiveIdleInterval.WaitForDurationAsync(
            0, TimeSpan.FromSeconds(2), () => -Stopwatch.Frequency,
            _ => throw new InvalidOperationException("A regressed clock must fail before another wait.")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5001)]
    public async Task InvalidOrUnboundedDurationFailsBeforeObservation(int milliseconds)
    {
        using var process = Process.GetCurrentProcess();
        int reads = 0;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PassiveIdleInterval.ObserveAsync(
            process, () => { reads++; return 3; }, TimeSpan.FromMilliseconds(milliseconds)));
        Assert.Equal(0, reads);
    }
}
