using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.ProGPU;

namespace ProGPU.Wpf.ShowcaseApp;

// Only called between measured intervals. No timers, polling or environment dump.
internal sealed class PassiveIdlePhaseJournal : IDisposable
{
    private readonly FileStream _stream;
    private int _count;
    private int _resizeCount;
    private static readonly JsonSerializerOptions s_resizeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<ProGpuWpfResizeStage>() }
    };

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

    // Synchronous setup only, with an independent budget: the original32 phase
    // records and their admission stay unchanged. Never called during ObserveAsync.
    internal void WriteResize(ProGpuWpfResizeCheckpoint checkpoint)
    {
        if (++_resizeCount > 64)
            throw new InvalidOperationException("Passive resize journal budget exceeded.");
        byte[] record = JsonSerializer.SerializeToUtf8Bytes(new
        {
            phase = "native-resize-checkpoint", processId = Environment.ProcessId,
            timestamp = Stopwatch.GetTimestamp(), sequence = _resizeCount, checkpoint
        }, s_resizeOptions);
        if (record.Length > 2048)
            throw new InvalidOperationException("Passive resize journal record exceeded its byte budget.");
        _stream.Write(record);
        _stream.WriteByte((byte)'\n');
        _stream.Flush(flushToDisk: true);
    }

    public void Dispose() => _stream.Dispose();
}
