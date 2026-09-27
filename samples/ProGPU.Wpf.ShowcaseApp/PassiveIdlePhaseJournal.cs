using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ProGPU.Wpf.ShowcaseApp;

// Only called between measured intervals. No timers, polling or environment dump.
internal sealed class PassiveIdlePhaseJournal : IDisposable
{
    private readonly FileStream _stream;
    private int _count;

    internal PassiveIdlePhaseJournal(string path) =>
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);

    internal void Write(string phase)
    {
        if (++_count > 32 || phase.Length > 64)
            throw new InvalidOperationException("Passive phase journal budget exceeded.");
        JsonSerializer.Serialize(_stream, new { phase, processId = Environment.ProcessId,
            timestamp = Stopwatch.GetTimestamp() });
        _stream.WriteByte((byte)'\n');
        _stream.Flush(flushToDisk: true);
    }

    public void Dispose() => _stream.Dispose();
}
