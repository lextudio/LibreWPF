using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Media.ProGPU;
using ProGPU.Backend.Native;

namespace ProGPU.Wpf.ShowcaseApp;

public partial class MainWindow
{
    private async Task<string> ValidateNativePerformanceAsync(ProGpuWpfWindowHost host)
    {
        bool previousCapture = host.EnableNativeMemoryDiagnostics;
        host.EnableNativeMemoryDiagnostics = true;
        try
        {
            for (int frame = 0; frame < 16; frame++)
                _ = await PresentNativePerformanceFrameAsync(host);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            const int frames = 120;
            var first = await PresentNativePerformanceFrameAsync(host);
            NativeGpuMemorySnapshot memoryBefore = RequireNativeMemory(first);
            // Diagnostic sample storage is allocated before measuring heap traffic.
            double[][] times = new double[8][];
            for (int stage = 0; stage < times.Length; stage++) times[stage] = new double[frames];
            using Process process = Process.GetCurrentProcess();
            long heapBefore = GC.GetGCMemoryInfo().HeapSizeBytes;
            long workingSetBefore = process.WorkingSet64;
            TimeSpan cpuBefore = process.TotalProcessorTime;
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            long started = Stopwatch.GetTimestamp();
            var last = first;
            ulong peakOwnedBytes = memoryBefore.TotalKnownOwnedBytes;
            NativeGpuMemorySnapshot peakMemory = memoryBefore;
            for (int frame = 0; frame < frames; frame++)
            {
                last = await PresentNativePerformanceFrameAsync(host);
                var memory = RequireNativeMemory(last);
                if (memory.EngineId != memoryBefore.EngineId ||
                    last.DeviceRecoveryCount != first.DeviceRecoveryCount ||
                    memory.SceneId != memoryBefore.SceneId)
                    throw new InvalidOperationException("Native performance sampling crossed an engine, recovery or scene identity boundary.");
                if (memory.TotalKnownOwnedBytes > peakOwnedBytes)
                {
                    peakOwnedBytes = memory.TotalKnownOwnedBytes;
                    peakMemory = memory;
                }
                times[0][frame] = last.CpuFrameTimeMs;
                times[1][frame] = last.SourceUpdateCpuTimeMs;
                times[2][frame] = last.SceneCompileCpuTimeMs;
                times[3][frame] = last.SceneInstallCpuTimeMs;
                times[4][frame] = last.SurfaceAcquireCpuTimeMs;
                times[5][frame] = last.SubmissionCpuTimeMs;
                times[6][frame] = last.PresentCpuTimeMs;
                times[7][frame] = last.MemoryInventoryCpuTimeMs;
                foreach (var stage in times)
                    if (!double.IsFinite(stage[frame]) || stage[frame] < 0)
                        throw new InvalidOperationException("Native performance sample contains an invalid CPU duration.");
            }
            double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            process.Refresh();
            double cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            var memoryAfter = RequireNativeMemory(last);
            if (last.PresentedFrameCount - first.PresentedFrameCount < frames ||
                last.Frame.CommandCount == 0 || last.Frame.DrawCallCount == 0 || last.Frame.SubmissionCount == 0)
                throw new InvalidOperationException("Native performance sampling did not observe 120 genuine rendered presentations.");
            const ulong maximumGrowth = 1024UL * 1024UL;
            ulong growth = memoryAfter.TotalKnownOwnedBytes >= memoryBefore.TotalKnownOwnedBytes
                ? memoryAfter.TotalKnownOwnedBytes - memoryBefore.TotalKnownOwnedBytes : 0;
            foreach (var stage in times) Array.Sort(stage);
            string[] names = ["host CPU", "source", "compile", "install", "acquire", "submit", "present", "inventory"];
            string[] summaries = new string[times.Length];
            for (int stage = 0; stage < times.Length; stage++)
                summaries[stage] = string.Create(CultureInfo.InvariantCulture,
                    $"{names[stage]} p50/p95/p99 {Percentile(times[stage], .50):0.###}/" +
                    $"{Percentile(times[stage], .95):0.###}/{Percentile(times[stage], .99):0.###} ms");
            string report = string.Create(CultureInfo.InvariantCulture,
                $"frames {frames}, wall {elapsedMs:0.###} ms, " +
                $"process CPU {cpuMs:0.###} ms, allocated {allocated} bytes ({(double)allocated / frames:0.##}/frame), " +
                $"{string.Join(", ", summaries)}, commands/draws {last.Frame.CommandCount}/{last.Frame.DrawCallCount}, " +
                $"engine {memoryAfter.EngineId}, scene/generation {memoryAfter.SceneId}/{memoryAfter.SceneGeneration}, " +
                $"native-owned logical GPU bytes {memoryBefore.TotalKnownOwnedBytes}->{memoryAfter.TotalKnownOwnedBytes} " +
                $"(peak {peakOwnedBytes}, peak pending batches {peakMemory.RetainedSubmissionBatchCount}), " +
                $"buffers/textures {memoryAfter.OwnedBufferCount}/{memoryAfter.OwnedTextureCount}, " +
                $"pending batches {memoryAfter.RetainedSubmissionBatchCount}, inventory CPU storage {memoryAfter.InventoryStorageBytes}, " +
                $"managed heap {heapBefore}->{GC.GetGCMemoryInfo().HeapSizeBytes}, " +
                $"working set {workingSetBefore}->{process.WorkingSet64}. " +
                $"Logical ownership excludes swapchain/driver residency; final matched Release/package qualification remains separate.");
            if (growth > maximumGrowth)
            {
                Console.Error.WriteLine("Showcase native performance measurement (failed memory gate): " + report);
                throw new InvalidOperationException($"Warmed native engine-owned storage grew beyond {maximumGrowth} bytes: " +
                    $"{memoryBefore.TotalKnownOwnedBytes}->{memoryAfter.TotalKnownOwnedBytes}, peak {peakOwnedBytes}; " +
                    $"buffers {memoryBefore.OwnedBufferCount}/{memoryBefore.OwnedBufferBytes}->" +
                    $"{peakMemory.OwnedBufferCount}/{peakMemory.OwnedBufferBytes}, " +
                    $"textures {memoryBefore.OwnedTextureCount}/{memoryBefore.OwnedTextureBytes}->" +
                    $"{peakMemory.OwnedTextureCount}/{peakMemory.OwnedTextureBytes}, " +
                    $"pending batches {memoryBefore.RetainedSubmissionBatchCount}->{peakMemory.RetainedSubmissionBatchCount}, " +
                    $"generation/submission {memoryBefore.SceneGeneration}/{memoryBefore.SubmissionIndex}->" +
                    $"{peakMemory.SceneGeneration}/{peakMemory.SubmissionIndex}, final {memoryAfter.TotalKnownOwnedBytes}.");
            }
            return "Showcase native performance validation succeeded: " + report;
        }
        finally
        {
            host.EnableNativeMemoryDiagnostics = previousCapture;
        }
    }

    private static NativeGpuMemorySnapshot RequireNativeMemory(ProGpuWpfDiagnostics.NativePerformanceSnapshot frame)
    {
        if (frame.GpuMemory is not { } memory || memory.EngineId == 0 ||
            memory.SceneId != frame.SceneUpdate.SceneId || memory.SceneGeneration != frame.SceneUpdate.Generation)
            throw new InvalidOperationException("Native memory was not captured for the published frame's scene generation.");
        if (!memory.HasCompleteTextureByteCount || memory.BorrowedViewCount != 0)
            throw new NotSupportedException("Native memory qualification requires quantified formats and external-image ownership: " +
                $"unquantified textures={memory.UnquantifiedTextureCount}, borrowed views={memory.BorrowedViewCount}.");
        return memory;
    }

    private static async Task<ProGpuWpfDiagnostics.NativePerformanceSnapshot> PresentNativePerformanceFrameAsync(
        ProGpuWpfWindowHost host)
    {
        long before = host.PresentedFrameCount;
        long skipped = host.SkippedFrameCount;
        long started = Stopwatch.GetTimestamp();
        WakeLiveRenderHost(host);
        for (int attempt = 0; attempt < 300; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(2));
            if (ProGpuWpfDiagnostics.TryGetNativePerformanceSnapshot(host, out var current) &&
                current.PresentedFrameCount > before && current.GpuMemory.HasValue)
                return current;
        }
        throw new InvalidOperationException($"Expected a native Showcase frame with memory diagnostics: " +
            $"presented={before}->{host.PresentedFrameCount}, skipped={skipped}->{host.SkippedFrameCount}, " +
            $"elapsed={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.###} ms.");
    }
}
