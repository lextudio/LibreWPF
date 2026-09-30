using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class ProGpuWpfResizePolicyTests
{
    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(false, false, false, true, true)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, true, true, true)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, true, true, false, false)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, true, true, false)]
    public void InlineResizeRequiresSafeNativeTracking(bool rendering, bool assigningSize,
        bool windows, bool tracking, bool expected)
    {
        Assert.Equal(expected, ProGpuWpfResizePolicy.ShouldRenderInline(
            rendering, assigningSize, windows, tracking));
    }

    [Fact]
    public void HostRetainsGeometryButConfiguresOnlyAtTheGuardedRenderBoundary()
    {
        string source = ReadHost();
        string resize = source[source.IndexOf("private void OnResize(", StringComparison.Ordinal)..
            source.IndexOf("private void AttachNativeDpiService()", StringComparison.Ordinal)];
        Assert.Contains("SynchronizePortablePresentationSourceGeometry(geometry);", resize);
        Assert.Contains("_target.SceneRootVisual.Invalidate();", resize);
        Assert.Contains("_target.RootVisual.Invalidate();", resize);
        Assert.Contains("RequestRenderAndWakeNativeLoop();", resize);
        Assert.DoesNotContain("TryConfigureSwapChain", resize);
        Assert.DoesNotContain("TryReconfigureIfNeeded", resize);
        Assert.Contains("ProGpuWpfResizePolicy.ShouldRenderInline(", resize);
        Assert.Contains("_nativeSizeAssignmentDepth != 0,", resize);
        Assert.Contains("OperatingSystem.IsWindows(),", resize);
        Assert.Contains("_windowController?.IsInteractiveMoveResize == true)", resize);
        int policy = resize.IndexOf("ProGpuWpfResizePolicy.ShouldRenderInline(", StringComparison.Ordinal);
        int deferred = resize.IndexOf("ProGpuWpfResizeStage.FramebufferRenderDeferred", policy, StringComparison.Ordinal);
        int exit = resize.IndexOf("return;", deferred, StringComparison.Ordinal);
        int render = resize.IndexOf("OnRender(0d);", exit, StringComparison.Ordinal);
        Assert.True(policy >= 0 && deferred > policy && exit > deferred && render > exit);
        string setter = source[source.IndexOf("private void SetClientSizeCore(", StringComparison.Ordinal)..
            source.IndexOf("internal ProGpuWpfResizeDiagnosticScope ObserveNativeResize(", StringComparison.Ordinal)];
        Assert.Contains("_nativeSizeAssignmentDepth++;\n            try\n            {\n                window.Size = nativeSize;\n            }\n            finally\n            {\n                _nativeSizeAssignmentDepth--;\n            }", setter);
        string frame = source[source.IndexOf("private void OnRender(", StringComparison.Ordinal)..
            source.IndexOf("private bool PresentNativeMil(", StringComparison.Ordinal)];
        int guard = frame.IndexOf("if (_isRendering)", StringComparison.Ordinal);
        int begin = frame.IndexOf("_isRendering = true;", guard, StringComparison.Ordinal);
        int configure = frame.IndexOf("if (!_target.Context.TryReconfigureIfNeeded(pixelWidth, pixelHeight, waitForNativeCompletion: false))", begin, StringComparison.Ordinal);
        int present = frame.IndexOf("if (RenderNativeMilFrame(", configure, StringComparison.Ordinal);
        Assert.True(guard >= 0 && begin > guard && configure > begin && present > configure);
        string wakeup = source[source.IndexOf("internal bool TryProcessRenderSchedulerWakeup()", StringComparison.Ordinal)..
            source.IndexOf("internal static bool ShouldProcessRenderSchedulerWakeupInline(", StringComparison.Ordinal)];
        Assert.Contains("if (_nativeSizeAssignmentDepth != 0 || _isRenderingLiveResize)\n        {\n            return false;\n        }", wakeup);
    }

    private static string ReadHost()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "src", "ProGPU.Wpf", "ProGpuWpfWindowHost.cs");
            if (File.Exists(path)) return File.ReadAllText(path).Replace("\r\n", "\n");
        }
        throw new FileNotFoundException("Could not locate ProGpuWpfWindowHost.cs.");
    }
}
