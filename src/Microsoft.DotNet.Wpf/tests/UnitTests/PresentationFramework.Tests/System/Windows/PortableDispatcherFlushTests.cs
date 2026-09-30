// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;

namespace System.Windows;

[Collection("Sequential")]
public class PortableDispatcherFlushTests
{
    [Fact]
    public void EmptyManagedFlushDoesNotManufactureDispatcherWork() => Run(window =>
    {
        Flush(window);
        int posted = 0;
        DispatcherHookEventHandler hook = (_, _) => posted++;
        window.Dispatcher.Hooks.OperationPosted += hook;
        try
        {
            for (int i = 0; i < 32; i++) Assert.True(Flush(window));
            if (OperatingSystem.IsWindows()) Assert.True(posted >= 32);
            else Assert.Equal(0, posted);
        }
        finally { window.Dispatcher.Hooks.OperationPosted -= hook; }
    });

    [Fact]
    public void FlushPreservesExistingPriorityAndFifoOrder() => Run(window =>
    {
        var order = new List<string>();
        DispatcherOperation idle = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => order.Add("idle")));
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => order.Add("first")));
        window.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => order.Add("render")));
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => order.Add("second")));
        try
        {
            Assert.True(Flush(window));
            Assert.Equal(new[] { "render", "first", "second" }, order);
            Assert.Equal(DispatcherOperationStatus.Pending, idle.Status);
        }
        finally { idle.Abort(); }
    });

    [Fact]
    public void LowerPriorityWorkIsRetainedForItsOwnFlush() => Run(window =>
    {
        bool invoked = false;
        DispatcherOperation idle = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => invoked = true));
        try
        {
            Assert.True(Flush(window));
            Assert.False(invoked);
            Assert.Equal(DispatcherOperationStatus.Pending, idle.Status);
            Assert.True(PortableWindowActivationService.FlushDispatcherOperations(
                window, DispatcherPriority.ApplicationIdle, TimeSpan.FromSeconds(1)));
            Assert.True(invoked);
        }
        finally { idle.Abort(); }
    });

    [Theory]
    [InlineData(-2d)]
    [InlineData(2147483648d)]
    public void InvalidTimeoutStillThrows(double milliseconds) => Run(window =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PortableWindowActivationService.FlushDispatcherOperations(
            window, DispatcherPriority.Background, TimeSpan.FromMilliseconds(milliseconds))));

    [Fact]
    public void ImmediateTimeoutDoesNotBecomeSuccessfulEmptyFlush() => Run(window =>
        Assert.False(PortableWindowActivationService.FlushDispatcherOperations(
            window, DispatcherPriority.Background, TimeSpan.Zero)));

    [Theory]
    [InlineData(DispatcherPriority.Invalid)]
    [InlineData((DispatcherPriority)11)]
    public void InvalidPriorityStillThrows(DispatcherPriority priority) => Run(window =>
        Assert.Throws<InvalidEnumArgumentException>(() => PortableWindowActivationService.FlushDispatcherOperations(
            window, priority, TimeSpan.FromSeconds(1))));

    [Fact]
    public void DisabledProcessingStillThrows() => Run(window =>
    {
        using (window.Dispatcher.DisableProcessing())
            Assert.Throws<InvalidOperationException>(() => Flush(window));
    });

    [Fact]
    public void ShutdownDoesNotReportACompletedFlush() => Run(window =>
    {
        window.Dispatcher.InvokeShutdown();
        Assert.False(Flush(window));
    });

    [Fact]
    public void DueTimerAndReentrantWorkAreActuallyExecuted() => Run(window =>
    {
        var order = new List<string>();
        DispatcherOperation? timerOperation = null;
        SynchronizationContext? promotionContext = null;
        DispatcherHookEventHandler posted = (_, e) =>
        {
            if (timerOperation == null && e.Operation.Priority == DispatcherPriority.Inactive) timerOperation = e.Operation;
        };
        DispatcherHookEventHandler promoted = (_, e) =>
        {
            if (ReferenceEquals(e.Operation, timerOperation)) promotionContext = SynchronizationContext.Current;
        };
        window.Dispatcher.Hooks.OperationPosted += posted;
        window.Dispatcher.Hooks.OperationPriorityChanged += promoted;
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.Zero };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            order.Add("timer");
            window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => order.Add("posted")));
        };
        Exception? startFailure = null;
        var start = new Thread(() =>
        {
            try { timer.Start(); }
            catch (Exception error) { startFailure = error; }
        }) { IsBackground = true };
        start.Start();
        Assert.True(start.Join(TimeSpan.FromSeconds(30)));
        try
        {
            Assert.Null(startFailure);
            Assert.NotNull(timerOperation);
            Assert.Null(promotionContext);
            Assert.True(Flush(window));
            Assert.Equal(new[] { "timer", "posted" }, order);
            Assert.False(timer.IsEnabled);
            Assert.IsType<DispatcherSynchronizationContext>(promotionContext);
        }
        finally
        {
            window.Dispatcher.Hooks.OperationPosted -= posted;
            window.Dispatcher.Hooks.OperationPriorityChanged -= promoted;
            timer.Stop();
        }
    });

    private static bool Flush(Window window) => PortableWindowActivationService.FlushDispatcherOperations(
        window, DispatcherPriority.Background, TimeSpan.FromSeconds(1));

    private static void Run(Action<Window> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            try { action(new Window()); }
            catch (Exception error) { failure = error; }
            finally { if (!dispatcher.HasShutdownFinished) dispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Dispatcher control exceeded its 30-second deadline.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
