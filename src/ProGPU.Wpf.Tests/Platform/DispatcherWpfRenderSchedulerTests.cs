using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class DispatcherWpfRenderSchedulerTests
{
    [Fact]
    public void RequestRenderUsesTimerThenDispatcherRenderPriority()
    {
        var dispatcher = new QueuedWpfDispatcherService();
        var timers = new TestTimerService();
        using var scheduler = new DispatcherWpfRenderScheduler(
            dispatcher,
            timers,
            TimeSpan.FromMilliseconds(10));
        var requestCount = 0;
        scheduler.RenderRequested += (_, _) => requestCount++;

        scheduler.RequestRender();

        Assert.True(scheduler.HasPendingRenderRequest);
        Assert.True(timers.LastTimer!.IsEnabled);
        Assert.False(dispatcher.ProcessPending());

        timers.LastTimer.Tick();

        Assert.False(timers.LastTimer.IsEnabled);
        Assert.True(dispatcher.ProcessPending());
        Assert.Equal(1, requestCount);
        Assert.True(scheduler.HasPendingRenderRequest);
    }

    [Fact]
    public void RequestRenderCoalescesUntilConsumed()
    {
        var dispatcher = new QueuedWpfDispatcherService();
        var timers = new TestTimerService();
        using var scheduler = new DispatcherWpfRenderScheduler(dispatcher, timers);

        scheduler.RequestRender();
        scheduler.RequestRender();
        timers.LastTimer!.Tick();
        dispatcher.ProcessPending();
        scheduler.RequestRender();

        Assert.Equal(1, timers.LastTimer.StartCount);

        Assert.True(scheduler.ConsumeRenderRequest());
        scheduler.RequestRender();

        Assert.Equal(2, timers.LastTimer.StartCount);
    }

    [Fact]
    public void ConsumeRenderRequestCancelsQueuedDispatcherCallback()
    {
        var dispatcher = new QueuedWpfDispatcherService();
        var timers = new TestTimerService();
        using var scheduler = new DispatcherWpfRenderScheduler(dispatcher, timers);
        var requestCount = 0;
        scheduler.RenderRequested += (_, _) => requestCount++;

        scheduler.RequestRender();
        timers.LastTimer!.Tick();

        Assert.True(scheduler.ConsumeRenderRequest());

        Assert.False(scheduler.HasPendingRenderRequest);
        Assert.False(dispatcher.ProcessPending());
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public void ResetClearsPendingRequestAndStopsTimer()
    {
        var timers = new TestTimerService();
        using var scheduler = new DispatcherWpfRenderScheduler(
            new QueuedWpfDispatcherService(),
            timers);

        scheduler.RequestRender();
        scheduler.Reset();

        Assert.False(scheduler.HasPendingRenderRequest);
        Assert.False(timers.LastTimer!.IsEnabled);
        Assert.Equal(1, timers.LastTimer.StopCount);
    }

    [Fact]
    public void DisposeDisposesTimerAndRejectsNewRequests()
    {
        var timers = new TestTimerService();
        var scheduler = new DispatcherWpfRenderScheduler(
            new QueuedWpfDispatcherService(),
            timers);

        scheduler.Dispose();

        Assert.True(timers.LastTimer!.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => scheduler.RequestRender());
    }

    private sealed class TestTimerService : IWpfTimerService
    {
        public TestTimer? LastTimer { get; private set; }

        public IWpfTimer CreateTimer(TimeSpan interval, Action callback, bool isRepeating = true)
        {
            LastTimer = new TestTimer(interval, callback, isRepeating);
            return LastTimer;
        }
    }

    private sealed class TestTimer : IWpfTimer
    {
        private readonly Action _callback;
        private readonly bool _isRepeating;

        public TestTimer(TimeSpan interval, Action callback, bool isRepeating)
        {
            Interval = interval;
            _callback = callback;
            _isRepeating = isRepeating;
        }

        public TimeSpan Interval { get; }

        public bool IsEnabled { get; private set; }

        public bool IsDisposed { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public void Start()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            StartCount++;
            IsEnabled = true;
        }

        public void Stop()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            StopCount++;
            IsEnabled = false;
        }

        public void Dispose()
        {
            IsEnabled = false;
            IsDisposed = true;
        }

        public void Tick()
        {
            if (!IsEnabled)
            {
                return;
            }

            if (!_isRepeating)
            {
                IsEnabled = false;
            }

            _callback();
        }
    }
}
