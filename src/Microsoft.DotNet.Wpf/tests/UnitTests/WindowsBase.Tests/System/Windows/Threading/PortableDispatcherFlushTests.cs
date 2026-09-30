// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using System.Threading;

namespace System.Windows.Threading.Tests;

public class PortableDispatcherFlushTests
{
    [Fact]
    public void EmptyQueueCanCompleteOnlyWithoutTheWin32MessagePump() => Run(dispatcher =>
    {
        int posted = 0;
        dispatcher.Hooks.OperationPosted += (_, _) => posted++;
        for (int i = 0; i < 32; i++)
            Assert.Equal(!OperatingSystem.IsWindows(), dispatcher.CanCompletePortableFlushWithoutFrame(DispatcherPriority.Background));
        Assert.Equal(0, posted);
    });

    [Theory]
    [InlineData(DispatcherPriority.Send, false)]
    [InlineData(DispatcherPriority.Background, false)]
    [InlineData(DispatcherPriority.ContextIdle, true)]
    [InlineData(DispatcherPriority.Inactive, true)]
    public void QueueEligibilityPreservesPriorityAndNeverConsumesWork(DispatcherPriority priority, bool emptyAtMarker) => Run(dispatcher =>
    {
        bool invoked = false;
        DispatcherOperation operation = dispatcher.BeginInvoke(priority, new Action(() => invoked = true));
        try
        {
            Assert.Equal(!OperatingSystem.IsWindows() && emptyAtMarker,
                dispatcher.CanCompletePortableFlushWithoutFrame(DispatcherPriority.Background));
            Assert.False(invoked);
            Assert.Equal(DispatcherOperationStatus.Pending, operation.Status);
        }
        finally { operation.Abort(); }
    });

    [Theory]
    [InlineData(DispatcherPriority.Inactive)]
    [InlineData(DispatcherPriority.Invalid)]
    [InlineData((DispatcherPriority)11)]
    public void InvalidOrInactiveMarkerCannotShortcut(DispatcherPriority priority) => Run(dispatcher =>
        Assert.False(dispatcher.CanCompletePortableFlushWithoutFrame(priority)));

    [Fact]
    public void ForeignThreadCannotShortcut() => Run(dispatcher =>
    {
        bool? result = null;
        Run(_ => result = dispatcher.CanCompletePortableFlushWithoutFrame(DispatcherPriority.Background));
        Assert.False(result);
    });

    [Fact]
    public void DisabledProcessingCannotShortcut() => Run(dispatcher =>
    {
        using (dispatcher.DisableProcessing())
            Assert.False(dispatcher.CanCompletePortableFlushWithoutFrame(DispatcherPriority.Background));
    });

    [Fact]
    public void ShutdownCannotShortcut() => Run(dispatcher =>
    {
        dispatcher.InvokeShutdown();
        Assert.True(dispatcher.HasShutdownFinished);
        Assert.False(dispatcher.CanCompletePortableFlushWithoutFrame(DispatcherPriority.Background));
    });

    [Fact]
    public void RequestedNestedFrameExitCannotShortcut() => Run(dispatcher =>
    {
        var frame = new DispatcherFrame();
        bool inspected = false;
        dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            DispatcherOperation? wake = null;
            DispatcherHookEventHandler captureWake = (_, e) => wake = e.Operation;
            dispatcher.Hooks.OperationPosted += captureWake;
            Dispatcher.ExitAllFrames();
            dispatcher.Hooks.OperationPosted -= captureWake;
            // Remove ExitAllFrames' wake operation so the state, not its queue
            // entry, independently prevents admission.
            Assert.NotNull(wake);
            wake!.Abort();
            Assert.False(dispatcher.CanCompletePortableFlushWithoutFrame(DispatcherPriority.Background));
            inspected = true;
        }));
        Dispatcher.PushFrame(frame);
        Assert.True(inspected);
    });

    [Fact]
    public void DueTimerRequiresOriginalFrameWithoutPromotingOrInvokingIt() => Run(dispatcher =>
    {
        int ticks = 0;
        DispatcherOperation? operation = null;
        dispatcher.Hooks.OperationPosted += (_, e) =>
        {
            if (e.Operation.Priority == DispatcherPriority.Inactive) operation = e.Operation;
        };
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.Zero };
        timer.Tick += (_, _) => ticks++;
        // Starting on a different thread retains the actual due timer in the
        // dispatcher list instead of Start's owner-thread immediate promotion.
        StartDueTimer(dispatcher, timer);
        try
        {
            Assert.NotNull(operation);
            Assert.Equal(DispatcherPriority.Inactive, operation!.Priority);
            Assert.False(dispatcher.CanCompletePortableFlushWithoutFrame(DispatcherPriority.Background));
            Assert.Equal(DispatcherPriority.Inactive, operation.Priority);
            Assert.Equal(0, ticks);
        }
        finally { timer.Stop(); }
    });

    [Fact]
    public void FutureTimerRemainsEnabledAndInactive() => Run(dispatcher =>
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromHours(1) };
        int ticks = 0;
        timer.Tick += (_, _) => ticks++;
        timer.Start();
        try
        {
            Assert.Equal(!OperatingSystem.IsWindows(), dispatcher.CanCompletePortableFlushWithoutFrame(DispatcherPriority.Background));
            Assert.True(timer.IsEnabled);
            Assert.Equal(0, ticks);
        }
        finally { timer.Stop(); }
    });

    [Theory]
    [InlineData("post")]
    [InlineData("disable")]
    [InlineData("shutdown")]
    [InlineData("throw")]
    public void EarlierTimerPromotionReentrancyCannotUseStaleAdmission(string action) => Run(dispatcher =>
    {
        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, dispatcher) { Interval = TimeSpan.Zero };
        DispatcherOperation? timerOperation = null;
        DispatcherOperation? posted = null;
        DispatcherProcessingDisabled? disabled = null;
        var failure = new InvalidOperationException("promotion hook");
        dispatcher.Hooks.OperationPosted += (_, e) =>
        {
            if (e.Operation.Priority == DispatcherPriority.Inactive) timerOperation = e.Operation;
        };
        StartDueTimer(dispatcher, timer);
        bool hookInvoked = false;
        DispatcherHookEventHandler hook = (_, e) =>
        {
            if (!ReferenceEquals(e.Operation, timerOperation)) return;
            hookInvoked = true;
            switch (action)
            {
                case "post": posted = dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => { })); break;
                case "disable": disabled = dispatcher.DisableProcessing(); break;
                case "shutdown": dispatcher.InvokeShutdown(); break;
                case "throw": throw failure;
            }
        };
        dispatcher.Hooks.OperationPriorityChanged += hook;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.False(dispatcher.CanCompletePortableFlushWithoutFrame(DispatcherPriority.Background));
                Assert.False(hookInvoked);
                return;
            }

            // The real host promotes timers before its flush. This is not part
            // of the admission predicate: the predicate must use the resulting
            // current state, without itself running any promotion hooks.
            if (action == "throw")
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                    dispatcher.PromoteTimers(Environment.TickCount)));
            else
            {
                dispatcher.PromoteTimers(Environment.TickCount);
                Assert.False(dispatcher.CanCompletePortableFlushWithoutFrame(DispatcherPriority.Background));
            }
            Assert.True(hookInvoked);
        }
        finally
        {
            dispatcher.Hooks.OperationPriorityChanged -= hook;
            disabled?.Dispose();
            posted?.Abort();
            timer.Stop();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PromotionHookShutdownRetiresRemainingTimersAndPreservesCallbackFailure(bool throwAfterShutdown) => Run(dispatcher =>
    {
        var operations = new List<DispatcherOperation>();
        DispatcherHookEventHandler posted = (_, e) =>
        {
            if (e.Operation.Priority == DispatcherPriority.Inactive) operations.Add(e.Operation);
        };
        dispatcher.Hooks.OperationPosted += posted;
        var first = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.Zero };
        var second = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.Zero };
        int ticks = 0;
        first.Tick += (_, _) => ticks++;
        second.Tick += (_, _) => ticks++;
        StartDueTimer(dispatcher, first);
        StartDueTimer(dispatcher, second);
        dispatcher.Hooks.OperationPosted -= posted;
        Assert.Equal(2, operations.Count);

        int promotions = 0;
        var failure = new InvalidOperationException("shutdown hook failure");
        DispatcherHookEventHandler promoted = (_, e) =>
        {
            promotions++;
            Assert.Same(operations[0], e.Operation);
            dispatcher.InvokeShutdown();
            if (throwAfterShutdown) throw failure;
        };
        dispatcher.Hooks.OperationPriorityChanged += promoted;
        try
        {
            if (throwAfterShutdown)
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                    dispatcher.PromoteTimers(Environment.TickCount)));
            else
                dispatcher.PromoteTimers(Environment.TickCount);

            Assert.True(dispatcher.HasShutdownFinished);
            Assert.Equal(1, promotions);
            Assert.Equal(0, ticks);
            Assert.All(operations, operation => Assert.Equal(DispatcherOperationStatus.Aborted, operation.Status));
            Assert.False(dispatcher.CanCompletePortableFlushWithoutFrame(DispatcherPriority.Background));
        }
        finally
        {
            dispatcher.Hooks.OperationPriorityChanged -= promoted;
            first.Stop();
            second.Stop();
        }
    });

    private static void StartDueTimer(Dispatcher dispatcher, DispatcherTimer timer)
    {
        DispatcherOperation? update = null;
        DispatcherHookEventHandler hook = (_, e) =>
        {
            if (e.Operation.Priority == DispatcherPriority.Send) update = e.Operation;
        };
        dispatcher.Hooks.OperationPosted += hook;
        try { Run(_ => timer.Start()); }
        finally { dispatcher.Hooks.OperationPosted -= hook; }

        // AddTimer requests an owner-thread due-time refresh. Perform that exact
        // refresh without pumping/promoting the timer; retire its queued duplicate
        // so due-timer admission is tested independently of pending Send work.
        Assert.NotNull(update);
        update!.Abort();
        dispatcher.UpdateWin32Timer();
    }

    private static void Run(Action<Dispatcher> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            try { action(dispatcher); }
            catch (Exception error) { failure = error; }
            finally { if (!dispatcher.HasShutdownFinished) dispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Dispatcher control exceeded its 30-second deadline.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
