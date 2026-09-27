using ProGPU.Wpf.ShowcaseApp;
using Xunit;

namespace ProGPU.Wpf.Tests;

// Source admission checks are not live native rendering qualification. The
// executable metric tests cover the shared BCL observer; the separate opt-in
// Showcase process must supply all four actual native receipts.
public class ShowcasePassiveIdleSourceContractTests
{
    [Fact]
    public void ResizeDiagnosticsBracketTheActualOperationAndNeverObserveAnIdleInterval()
    {
        string fixture = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.IdleLayoutClip.cs");
        int registration = fixture.IndexOf("using (var resizeDiagnostics = journal is null ? null :", StringComparison.Ordinal);
        int setter = fixture.IndexOf("host.SetClientSize(resizedWidth, resizedHeight);", registration, StringComparison.Ordinal);
        int check = fixture.IndexOf("resizeDiagnostics?.ThrowIfFailed();", setter, StringComparison.Ordinal);
        int disposed = fixture.IndexOf("}", check, StringComparison.Ordinal);
        int completed = fixture.IndexOf("journal?.Write(\"native-resize-setter-returned\")", disposed, StringComparison.Ordinal);
        int interval = fixture.IndexOf("var resized = await ObserveIdlePhaseAsync", completed, StringComparison.Ordinal);
        Assert.True(registration >= 0 && setter > registration && check > setter && disposed > check && completed > disposed && interval > completed);
        Assert.Contains("ProGpuWpfDiagnostics.ObserveNativeResize(host, journal.WriteResize)", fixture, StringComparison.Ordinal);
        string host = Read("src/ProGPU.Wpf/ProGpuWpfWindowHost.cs");
        string capture = host[host.IndexOf("private void TraceResizeCheckpoint(", StringComparison.Ordinal)..host.IndexOf("public void DoEvents()", StringComparison.Ordinal)];
        Assert.Contains("if (scope is null || !scope.CanRecord) return;", capture, StringComparison.Ordinal);
        Assert.Contains("catch (Exception error) { scope.Fail(error); }", capture, StringComparison.Ordinal);
        foreach (string forbidden in new[] { "RequestRender(", "ProcessPending(", "DoRender(", "Consume", "FramebufferSize", "NativeWindowHandle", "GetGpuMemory", "File.", "Console." })
            Assert.DoesNotContain(forbidden, capture, StringComparison.Ordinal);
        Assert.Contains("Resize diagnostics are already registered on this host.", host, StringComparison.Ordinal);
        Assert.Contains("if (ReferenceEquals(_resizeDiagnostics, scope)) _resizeDiagnostics = null;", host, StringComparison.Ordinal);
        foreach (string[] ordered in new[]
        {
            new[] { "ProGpuWpfResizeStage.ClientSizeEntered", "ProGpuWpfResizeStage.NativeSizeResolving", "ResolveNativeWindowSizeForLogicalClientSize(", "ProGpuWpfResizeStage.NativeSizeAssigning", "window.Size = nativeSize;", "ProGpuWpfResizeStage.NativeSizeAssigned" },
            new[] { "ProGpuWpfResizeStage.SwapChainConfigureEntering", "if (!_target.Context.TryReconfigureIfNeeded(pixelWidth, pixelHeight))", "ProGpuWpfResizeStage.SwapChainConfigureRejected", "ProGpuWpfResizeStage.SwapChainConfigureReturned" },
            new[] { "ProGpuWpfResizeStage.FramebufferResizeEntered", "ProGpuWpfResizeStage.FramebufferResizeSkipped", "ProGpuWpfResizeStage.FramebufferSourceUnavailable", "ProGpuWpfResizeStage.FramebufferRenderEntering", "OnRender(0d);", "ProGpuWpfResizeStage.FramebufferRenderReturned" },
            new[] { "ProGpuWpfResizeStage.SourceLayoutEntering", "if (!_portablePresentationSourceBridge.TrySetClientSize(clientWidth, clientHeight))", "ProGpuWpfResizeStage.SourceLayoutRejected", "ProGpuWpfResizeStage.SourceLayoutReturned" }
        })
        {
            int previous = -1;
            foreach (string item in ordered)
            {
                int current = host.IndexOf(item, previous + 1, StringComparison.Ordinal);
                Assert.True(current > previous, $"Missing or reordered checkpoint: {item}");
                previous = current;
            }
        }
    }

    [Fact]
    public void IdleStartupWaitsForLoadedAndDoesNotRestart()
    {
        bool started = false;
        Assert.False(PassiveIdleStartup.TryStart(isLoaded: false, ref started));
        Assert.False(started);
        Assert.True(PassiveIdleStartup.TryStart(isLoaded: true, ref started));
        Assert.True(started);
        Assert.False(PassiveIdleStartup.TryStart(isLoaded: true, ref started));
        Assert.False(PassiveIdleStartup.TryStart(isLoaded: false, ref started));
        Assert.False(PassiveIdleStartup.TryStart(isLoaded: true, ref started));
        Assert.True(started);
    }

    [Fact]
    public void ObserverHasNoDispatcherLayoutRenderQueryOrStatusSideEffects()
    {
        string source = Read("samples/ProGPU.Wpf.ShowcaseApp/PassiveIdleInterval.cs");
        foreach (string forbidden in new[] { "WakeLive", "UpdateLayout(", "Dispatcher.", "TryGetWindowHost(",
            "TryGetNativePerformanceSnapshot(", "TryPollNativeMemoryCheckpoint(", "GetGpuHitTest", "Console.", "File.", "GC.Collect(" })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(duration).ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("GC.GetTotalAllocatedBytes(precise: true)", source, StringComparison.Ordinal);
        Assert.Contains("ExtraPresentations != 0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("while (", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActualShowcaseFixtureIsSeparateAndPreservesRealNativeOwnership()
    {
        string source = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.IdleLayoutClip.cs");
        int configuration = source.IndexOf("ValidateIdleLayoutClipConfiguration();", StringComparison.Ordinal);
        int startup = source.IndexOf("if (!PassiveIdleStartup.TryStart(IsLoaded, ref _liveValidationStarted)) return true;", StringComparison.Ordinal);
        int launch = source.IndexOf("_ = Task.Run(", StringComparison.Ordinal);
        Assert.True(configuration >= 0 && startup > configuration && launch > startup,
            "The actual Loaded state must admit the idle task after configuration validation.");
        Assert.Contains("TimeSpan.FromSeconds(30)", source, StringComparison.Ordinal);
        Assert.Contains("host.HasPresentedFrame", source, StringComparison.Ordinal);
        string window = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.xaml.cs");
        int constructor = window.IndexOf("public MainWindow()", StringComparison.Ordinal);
        int loaded = window.IndexOf("private void OnShowcaseWindowLoaded(", StringComparison.Ordinal);
        int afterLoaded = window.IndexOf("internal static IReadOnlyList<string> FrameworkThemeNames", StringComparison.Ordinal);
        Assert.Contains("StartLiveValidationIfRequired();", window[constructor..loaded], StringComparison.Ordinal);
        Assert.Contains("StartLiveValidationIfRequired();", window[loaded..afterLoaded], StringComparison.Ordinal);
        Assert.Contains("Loaded=\"OnShowcaseWindowLoaded\"", Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.xaml"), StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(host.WpfRootVisual, this)", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(host.PortablePresentationSourceBridge?.RootVisual, this)", source, StringComparison.Ordinal);
        Assert.Contains("host.LastNativeMilSessionFrame is null", source, StringComparison.Ordinal);
        Assert.Contains("!PortableWpfRuntime.IsMediaBackendFrozen", source, StringComparison.Ordinal);
        Assert.Contains("native.DeviceRecoveryCount != fixture.OriginalRecovery", source, StringComparison.Ordinal);
        Assert.Contains("panel.Children.Insert(0, zero)", source, StringComparison.Ordinal);
        Assert.Contains("new Border { Width = 80, Height = 0, ClipToBounds = true", source, StringComparison.Ordinal);
        Assert.Contains("fixture.Viewer.ScrollToVerticalOffset(", source, StringComparison.Ordinal);
        int previousResizeCheckpoint = -1;
        foreach (string checkpoint in new[] { "journal?.Write(\"native-resize-request\")",
            "journal?.Write(\"native-resize-callback-entered\")", "host.SetClientSize(resizedWidth, resizedHeight);",
            "journal?.Write(\"native-resize-setter-returned\")", "WakeLiveRenderHost(host);",
            "journal?.Write(\"native-resize-wake-returned\")",
            "await WaitForLiveNativeResizeAsync(host, (uint)resizedWidth, (uint)resizedHeight",
            "journal?.Write(\"native-resize-geometry-observed\")",
            "var resized = await ObserveIdlePhaseAsync(" })
        {
            int current = source.IndexOf(checkpoint, previousResizeCheckpoint + 1, StringComparison.Ordinal);
            Assert.True(current > previousResizeCheckpoint, $"Missing or reordered resize checkpoint: {checkpoint}");
            previousResizeCheckpoint = current;
        }
        foreach (string phase in new[] { "initial", "scrolled", "native-resized", "restored" })
            Assert.Contains($"\"{phase}\", receipt, journal)", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.Contains("fixture.Panel.Children.Remove(fixture.Zero)", source, StringComparison.Ordinal);
        Assert.Contains("receipt.UiRestored = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableFrameCoalescing =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableNativeMemoryDiagnostics =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryPollNativeMemoryCheckpoint(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetGpuHitTest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GC.Collect(", source, StringComparison.Ordinal);
        Assert.Contains("MainWindow.ValidateIdleLayoutClipConfiguration();", Read("samples/ProGPU.Wpf.ShowcaseApp/App.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("StartIdleLayoutClipValidationIfRequested()", Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.xaml.cs"), StringComparison.Ordinal);
        // Keep forced-frame performance validation separate, not relabelled idle.
        Assert.Contains("PresentNativePerformanceFrameAsync(host)", Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.NativePerformance.cs"), StringComparison.Ordinal);
        Assert.Contains("python3 ./eng/test-progpu-wpf-showcase-idle.py -v", Read(".github/workflows/progpu-wpf-sdk.yml"), StringComparison.Ordinal);
        Assert.Contains("python3 ./eng/test-showcase-idle-crash.py -v", Read(".github/workflows/progpu-wpf-sdk.yml"), StringComparison.Ordinal);
        string gate = Read("eng/progpu-wpf-layout-clip.sh");
        foreach (string name in new[] { "PassiveIdleIntervalTests", "ShowcasePassiveIdleSourceContractTests", "PassiveIdleBoundaryTests" })
        {
            Assert.Contains($"FullyQualifiedName~ProGPU.Wpf.Tests.{name}.", gate, StringComparison.Ordinal);
            Assert.Contains($"\"ProGPU.Wpf.Tests.{name}\": ", gate, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PassiveEndpointUsesOneNativeUpdateAndReadOnlyResizeObservations()
    {
        string fixture = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.IdleLayoutClip.cs");
        Assert.Equal(2, fixture.Split("requestRenderWhileObserving: false", StringSplitOptions.None).Length - 1);
        Assert.Contains("window.Update += update", fixture, StringComparison.Ordinal);
        Assert.Contains("window.Update -= update", fixture, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(host.SilkWindow, window)", fixture, StringComparison.Ordinal);
        Assert.Contains("activity.HasImmediatePresentationWork", fixture, StringComparison.Ordinal);
        Assert.Contains("after != activity", fixture, StringComparison.Ordinal);
        Assert.Contains("ReadIdleBoundaryAsync(host, fixture)", fixture, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(1)", fixture, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(2)", fixture, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromTicks(LiveValidationRetryDelay.Ticks * LiveValidationMaxAttempts)", fixture, StringComparison.Ordinal);
        string shared = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.xaml.cs");
        Assert.Contains("bool requestRenderWhileObserving = true", shared, StringComparison.Ordinal);
        Assert.Contains(": await InvokeWithLiveNativeLoopWakeAsync(liveHost, ReadLayout, DispatcherPriority.Send)", shared, StringComparison.Ordinal);
        string host = Read("src/ProGPU.Wpf/ProGpuWpfWindowHost.cs");
        int reader = host.IndexOf("internal bool TryGetRenderActivitySnapshot(", StringComparison.Ordinal);
        int end = host.IndexOf("internal void RecordNativePerformanceSnapshot(", reader, StringComparison.Ordinal);
        string body = host[reader..end];
        Assert.Contains("PlatformServices.Dispatcher.CheckAccess()", body, StringComparison.Ordinal);
        Assert.Contains("_isRendering,", body, StringComparison.Ordinal);
        foreach (string forbidden in new[] { "ConsumeRenderRequest(", "RequestRender(", "Reset(", "ProcessPending(", "GetGpuMemorySnapshot(" })
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
        // Original ordering explains why an ordinary dispatcher callback is not
        // an after-render boundary; the native update is outside that callback.
        int render = host.IndexOf("private void OnRender(double deltaSeconds)", StringComparison.Ordinal);
        Assert.True(host.IndexOf("_isRendering = true;", render, StringComparison.Ordinal) <
            host.IndexOf("ProcessDispatcherQueueCore();", render, StringComparison.Ordinal));
        Assert.Contains("TaskCreationOptions.RunContinuationsAsynchronously", Read("samples/ProGPU.Wpf.ShowcaseApp/PassiveIdleBoundary.cs"));
        int begin = fixture.IndexOf("IdleSourceState before = await ReadIdleBoundaryAsync(", StringComparison.Ordinal);
        int endInterval = fixture.IndexOf("receipt.Phases.Add(phase)", begin, StringComparison.Ordinal);
        Assert.DoesNotContain("journal", fixture[begin..endInterval], StringComparison.Ordinal);
    }

    private static string Read(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "eng", "progpu-wpf-sdk-ci.sh")))
                return File.ReadAllText(Path.Combine(directory.FullName, relative));
        throw new FileNotFoundException("Could not find the current LibreWPF source checkout.");
    }
}
