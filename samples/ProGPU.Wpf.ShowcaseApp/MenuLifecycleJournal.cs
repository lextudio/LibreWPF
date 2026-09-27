using System;
using System.Diagnostics;

namespace ProGPU.Wpf.ShowcaseApp;

// In-memory diagnostics only. The ordinary live theme probe owns this journal;
// it is never installed by the passive allocation/presentation probe.
internal sealed class MenuLifecycleJournal
{
    internal const int Capacity = 64;
    internal const int MaximumRecordLength = 768;
    private readonly string[] _records = new string[Capacity];
    private int _count;
    private bool _truncated;

    internal void Record(string transition, Func<string> observe)
    {
        if (_count == Capacity)
        {
            _truncated = true;
            return;
        }

        // Reserve the position before reading source properties, preserving
        // chronology even if a diagnostic getter causes a nested notification.
        int index = _count++;
        long timestamp = Stopwatch.GetTimestamp();
        string state;
        try { state = observe(); }
        catch (Exception error) { state = "diagnostic-error=" + error.Message; }
        string record = $"{timestamp} {transition}: {state}";
        _records[index] = record.Length <= MaximumRecordLength ? record : record[..MaximumRecordLength];
    }

    internal string FormatFailure() =>
        $"menu-lifecycle count={_count} truncated={_truncated} stopwatch-frequency={Stopwatch.Frequency}\n" +
        string.Join("\n", _records, 0, _count);
}
