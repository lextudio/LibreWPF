using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProGPU.Wpf.ShowcaseApp;

// One native-update observation, not a wait-until-quiet loop. The callback owns
// admission and faults the task if work remains. No scheduler state is consumed.
internal static class PassiveIdleBoundary
{
    internal static async Task<T> ObserveOnceAsync<T>(Action<Action> subscribe,
        Action<Action> unsubscribe, Action wakeNativeLoop, Func<T> observe, TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        int retired = 0;
        void OnUpdate()
        {
            if (Interlocked.Exchange(ref retired, 1) != 0) return;
            try
            {
                unsubscribe(OnUpdate);
                completion.TrySetResult(observe());
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        }

        try
        {
            subscribe(OnUpdate);
            wakeNativeLoop();
            return await completion.Task.WaitAsync(timeout).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref retired, 1);
            unsubscribe(OnUpdate);
        }
    }
}
