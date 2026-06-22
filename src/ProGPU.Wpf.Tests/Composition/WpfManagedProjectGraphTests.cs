using System.Xml.Linq;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfManagedProjectGraphTests
{
    [Theory]
    [InlineData("src/Microsoft.DotNet.Wpf/src/PresentationCore/PresentationCore.csproj")]
    [InlineData("src/Microsoft.DotNet.Wpf/src/PresentationFramework/PresentationFramework.csproj")]
    [InlineData("src/Microsoft.DotNet.Wpf/src/ReachFramework/ReachFramework.csproj")]
    public void DirectWriteForwarderReferenceIsWindowsOnly(string relativeProjectPath)
    {
        var projectPath = FindRepoPath(relativeProjectPath.Split('/'));
        var document = XDocument.Load(projectPath);

        var reference = Assert.Single(
            document.Descendants("ProjectReference"),
            reference =>
            {
                var include = reference.Attribute("Include")?.Value.Replace('/', '\\');
                return include?.EndsWith(@"DirectWriteForwarder\DirectWriteForwarder.vcxproj", StringComparison.OrdinalIgnoreCase) == true;
            });

        Assert.Equal("'$(OS)' == 'Windows_NT'", reference.Attribute("Condition")?.Value);
        Assert.Equal("TargetFramework;TargetFrameworks", reference.Element("UndefineProperties")?.Value);
    }

    [Theory]
    [InlineData(@"external\ProGPU\src\ProGPU.Text\SfntFontFace.cs", @"MS\Internal\Text\TextInterface\ProGPU\SfntFontFace.cs")]
    [InlineData(@"external\ProGPU\src\ProGPU.Text\SfntSimpleGlyphShaper.cs", @"MS\Internal\Text\TextInterface\ProGPU\SfntSimpleGlyphShaper.cs")]
    [InlineData(@"external\ProGPU\src\ProGPU.Text\SfntFontSubsetter.cs", @"MS\Internal\Text\TextInterface\ProGPU\SfntFontSubsetter.cs")]
    public void PresentationCoreIncludesProGpuTextSourceOnNonWindows(string sourcePath, string linkPath)
    {
        var projectPath = FindRepoPath("src", "Microsoft.DotNet.Wpf", "src", "PresentationCore", "PresentationCore.csproj");
        var document = XDocument.Load(projectPath);

        var compileItem = Assert.Single(
            document.Descendants("Compile"),
            item =>
            {
                var include = item.Attribute("Include")?.Value.Replace('/', '\\');
                return include?.EndsWith(sourcePath, StringComparison.OrdinalIgnoreCase) == true;
            });

        Assert.Equal("'$(OS)' != 'Windows_NT'", compileItem.Attribute("Condition")?.Value);
        Assert.Equal(linkPath, compileItem.Attribute("Link")?.Value);
    }

    [Fact]
    public void MediaContextNotificationWindowSkipsWin32WindowCreationOnNonWindows()
    {
        var sourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "MediaContextNotificationWindow.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", source, StringComparison.Ordinal);
        Assert.Contains("if (!s_isWindows)", source, StringComparison.Ordinal);
        Assert.Contains("new HwndWrapper", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("if (!s_isWindows)", StringComparison.Ordinal)
                < source.IndexOf("new HwndWrapper", StringComparison.Ordinal),
            "The non-Windows guard must run before creating the hidden HWND notification window.");
        Assert.Contains("_ownerMediaContext.Channel.SetNotificationWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemXamlNameScopeDictionaryImplementsDictionaryContract()
    {
        var sourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "System.Xaml",
            "System",
            "Xaml",
            "NameScopeDictionary.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("throw new NotImplementedException", source, StringComparison.Ordinal);
        Assert.Contains("int ICollection<KeyValuePair<string, object>>.Count", source, StringComparison.Ordinal);
        Assert.Contains("void ICollection<KeyValuePair<string, object>>.Clear()", source, StringComparison.Ordinal);
        Assert.Contains("void ICollection<KeyValuePair<string, object>>.CopyTo", source, StringComparison.Ordinal);
        Assert.Contains("bool ICollection<KeyValuePair<string, object>>.Remove", source, StringComparison.Ordinal);
        Assert.Contains("object IDictionary<string, object>.this[string key]", source, StringComparison.Ordinal);
        Assert.Contains("bool IDictionary<string, object>.TryGetValue", source, StringComparison.Ordinal);
        Assert.Contains("ICollection<string> IDictionary<string, object>.Keys", source, StringComparison.Ordinal);
        Assert.Contains("_underlyingNameScope.RegisterName(name, scopedElement)", source, StringComparison.Ordinal);
        Assert.Contains("_underlyingNameScope.UnregisterName(name)", source, StringComparison.Ordinal);
        Assert.Contains("return _underlyingNameScope is not null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationFrameworkInternalCollectionsImplementGenericContracts()
    {
        var listOfObjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "MS",
            "Internal",
            "ListOfObject.cs");
        var weakDictionaryPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "MS",
            "Internal",
            "WeakDictionary.cs");
        var listOfObject = File.ReadAllText(listOfObjectPath);
        var weakDictionary = File.ReadAllText(weakDictionaryPath);

        Assert.DoesNotContain("throw new NotImplementedException", listOfObject, StringComparison.Ordinal);
        Assert.DoesNotContain("throw new NotImplementedException", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("_list.Insert(index, item);", listOfObject, StringComparison.Ordinal);
        Assert.Contains("_list.RemoveAt(index);", listOfObject, StringComparison.Ordinal);
        Assert.Contains("_list[index] = value;", listOfObject, StringComparison.Ordinal);
        Assert.Contains("_list.Add(item);", listOfObject, StringComparison.Ordinal);
        Assert.Contains("get { return _list.IsReadOnly; }", listOfObject, StringComparison.Ordinal);
        Assert.Contains("_list.Remove(item);", listOfObject, StringComparison.Ordinal);
        Assert.Contains("throw new NotSupportedException();", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("public void CopyTo(KeyType[] array, int arrayIndex)", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("public bool Contains(ValueType item)", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("EqualityComparer<ValueType>.Default", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("public void CopyTo(ValueType[] array, int arrayIndex)", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("array[arrayIndex++] = key;", weakDictionary, StringComparison.Ordinal);
        Assert.Contains("array[arrayIndex++] = value;", weakDictionary, StringComparison.Ordinal);
    }

    [Fact]
    public void XamlWriterSkipsEmptyRuntimeNames()
    {
        var sourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Markup",
            "Primitives",
            "ElementMarkupObject.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("IsEmptyRuntimeNameProperty(pd, dpd, instance)", source, StringComparison.Ordinal);
        Assert.Contains("instance is not FrameworkElement && instance is not FrameworkContentElement", source, StringComparison.Ordinal);
        Assert.Contains("return String.IsNullOrEmpty(pd.GetValue(instance) as string);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaContextUsesPortableClockOutsideWindows()
    {
        var sourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "MediaContext.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", source, StringComparison.Ordinal);
        Assert.Contains("Stopwatch.Frequency", source, StringComparison.Ordinal);
        Assert.Contains("Stopwatch.GetTimestamp()", source, StringComparison.Ordinal);
        Assert.Contains("SafeNativeMethods.QueryPerformanceFrequency(out frequency)", source, StringComparison.Ordinal);
        Assert.Contains("SafeNativeMethods.QueryPerformanceCounter(out performanceCount)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeNativeMethods.QueryPerformanceFrequency(out _perfCounterFreq)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeNativeMethods.QueryPerformanceCounter(out qpcCurrentTime)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeNativeMethods.QueryPerformanceCounter(out _lastPresentationTime)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeNativeMethods.QueryPerformanceCounter(out counts)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaContextHasPortableRenderWakeupBoundary()
    {
        var mediaContextPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "MediaContext.cs");
        var renderServicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "PortableMediaContextRenderService.cs");
        var dragDropPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "DragDrop.cs");
        var presentationCoreProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "PresentationCore.csproj");
        var activationServicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "PortableWindowActivationService.cs");
        var proGpuActivationPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortableWindowActivation.cs");
        var proGpuSchedulerPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Platform",
            "IWpfRenderScheduler.cs");
        var proGpuPlatformServicesPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Platform",
            "IWpfPlatformServices.cs");
        var proGpuHostPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfWindowHost.cs");
        var proGpuOptionsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfWindowOptions.cs");
        var proGpuDrawingFramePath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfDrawingFrame.cs");
        var proGpuCompositionTargetPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfCompositionTarget.cs");
        var proGpuCompositorPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Scene",
            "Compositor.cs");
        var proGpuCompositorReviewTestsPath = FindRepoPath(
            "external",
            "ProGPU",
            "src",
            "ProGPU.Tests",
            "CompositorReviewRegressionTests.cs");
        var proGpuDrawingFrameTestsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.Tests",
            "ProGpuWpfDrawingFrameTests.cs");
        var proGpuWindowHostTestsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.Tests",
            "ProGpuWpfWindowHostTests.cs");
        var proGpuActivationTestsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.Tests",
            "WpfPortableWindowActivationTests.cs");

        var mediaContext = File.ReadAllText(mediaContextPath);
        var renderService = File.ReadAllText(renderServicePath);
        var dragDrop = File.ReadAllText(dragDropPath);
        var presentationCoreProject = File.ReadAllText(presentationCoreProjectPath);
        var activationService = File.ReadAllText(activationServicePath);
        var proGpuActivation = File.ReadAllText(proGpuActivationPath);
        var proGpuScheduler = File.ReadAllText(proGpuSchedulerPath);
        var proGpuPlatformServices = File.ReadAllText(proGpuPlatformServicesPath);
        var proGpuHost = File.ReadAllText(proGpuHostPath);
        var proGpuOptions = File.ReadAllText(proGpuOptionsPath);
        var proGpuDrawingFrame = File.ReadAllText(proGpuDrawingFramePath);
        var proGpuCompositionTarget = File.ReadAllText(proGpuCompositionTargetPath);
        var proGpuCompositor = File.ReadAllText(proGpuCompositorPath);
        var proGpuCompositorReviewTests = File.ReadAllText(proGpuCompositorReviewTestsPath);
        var proGpuDrawingFrameTests = File.ReadAllText(proGpuDrawingFrameTestsPath);
        var proGpuWindowHostTests = File.ReadAllText(proGpuWindowHostTestsPath);
        var proGpuActivationTests = File.ReadAllText(proGpuActivationTestsPath);

        Assert.Contains(@"<Compile Include=""System\Windows\Media\PortableMediaContextRenderService.cs"" />", presentationCoreProject, StringComparison.Ordinal);
        Assert.Contains("internal static class PortableMediaContextRenderService", renderService, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", renderService, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Action requestRender)", renderService, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Action<TimeSpan> requestRender)", renderService, StringComparison.Ordinal);
        Assert.Contains("internal static void RequestRender()", renderService, StringComparison.Ordinal);
        Assert.Contains("internal static void RequestRender(TimeSpan delay)", renderService, StringComparison.Ordinal);
        Assert.Contains("PortableMediaContextRenderService.RequestRender(nextTickNeeded)", mediaContext, StringComparison.Ordinal);
        Assert.Contains("RenderDisconnectedMessageHandlerCore(resizedCompositionTarget)", mediaContext, StringComparison.Ordinal);
        Assert.Contains("private void RenderDisconnectedMessageHandlerCore", mediaContext, StringComparison.Ordinal);
        Assert.Contains("ScheduleNextRenderOp(_timeDelay)", mediaContext, StringComparison.Ordinal);
        Assert.Contains("if (Channel != null)", mediaContext, StringComparison.Ordinal);
        Assert.Contains("EnterInterlockedPresentation();", mediaContext, StringComparison.Ordinal);

        Assert.Contains("internal static void FlushDispatcherOperations(object window, DispatcherPriority markerPriority)", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static bool FlushDispatcherOperations(object window, DispatcherPriority markerPriority, TimeSpan timeout)", activationService, StringComparison.Ordinal);
        Assert.Contains("FlushDispatcherOperations(window, markerPriority, Timeout.InfiniteTimeSpan)", activationService, StringComparison.Ordinal);
        Assert.Contains("markerOperation.Abort()", activationService, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.PushFrame(frame)", activationService, StringComparison.Ordinal);
        Assert.Contains("public interface IWpfDelayedRenderScheduler : IWpfRenderScheduler", proGpuScheduler, StringComparison.Ordinal);
        Assert.Contains("void RequestRender(TimeSpan delay)", proGpuScheduler, StringComparison.Ordinal);
        Assert.Contains("PortableMediaContextRenderServiceTypeName", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryRegisterMediaContextRenderService(presentationCoreAssembly)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("typeof(Action<TimeSpan>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("IWpfDelayedRenderScheduler delayedScheduler", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.RenderWakeupRequested += OnHostRenderWakeupRequested", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("SynchronizeInitialWindowState(updatePortablePresentationSource: false);", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("SynchronizeInitialWindowState(updatePortablePresentationSource: true);", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private void SynchronizeInitialWindowState(bool updatePortablePresentationSource)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ToLogicalClientDimension", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ToLogicalPositionDimension", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.SetInitialClientSize(width, height)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.SetClientSize(", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("public void SetPosition(object? left, object? top)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.SetPosition(windowLeft.Value, windowTop.Value)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("public void SetTopmost(bool topmost)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.SetTopmost(topmost)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("typeof(Action<object, object, object>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("13 => new object[] { activate, show, hide, setWindowState, setTitle, setClientSize, setPosition, setTopmost, setWindowBorder, close, run, dispose, dragMove }", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("public void SetWindowBorder(object? resizeMode, object? windowStyle)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.SetWindowBorder(ResolveWindowBorder(resizeMode, windowStyle, Host.WindowBorder))", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ResolveWindowBorder(window, options.WindowBorder)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.SetWindowBorder(ResolveWindowBorder(Window, Host.WindowBorder))", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryMapResizeModeToWindowBorder", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("WindowStyle", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ApplicationIdleFlushTimeout = TimeSpan.FromMilliseconds(250)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("internal void DeferShowUntilRun()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("Host.DeferShowUntilRun();", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private bool ShouldDeferNativeShowUntilRun()", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("return !_isNativeRunStarted && IsCurrentApplicationMainWindow(Window);", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("private static bool IsCurrentApplicationMainWindow(object window)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("_isNativeRunStarted = true;", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("FlushWpfDispatcherOperations(\"Loaded\", \"Render\")", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("FlushWpfDispatcherOperations(\"Render\", \"ApplicationIdle\")", proGpuActivation, StringComparison.Ordinal);
        Assert.DoesNotContain("FlushWpfDispatcherOperations(\"ApplicationIdle\")", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryFlushDispatcherOperations(Window, markerPriorityName, timeout)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("parameters[2].ParameterType != typeof(TimeSpan)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("FindPortableWindowActivationServiceType(window)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("typeof(Func<object, bool>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryDragMove()", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.TryBeginDragMove()", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("internal static DragDropEffects ProcessPortableDrop", dragDrop, StringComparison.Ordinal);
        Assert.Contains("internal static DragDropEffects ProcessPortableDragDrop", dragDrop, StringComparison.Ordinal);
        Assert.Contains("ProcessPortableDragDrop(\n                target,\n                DropEvent", dragDrop, StringComparison.Ordinal);
        Assert.Contains("internal static int ProcessDragDrop(", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static int ProcessDragDropEvent(", activationService, StringComparison.Ordinal);
        Assert.Contains("ToDragDropRoutedEvent(dragDropEventKind)", activationService, StringComparison.Ordinal);
        Assert.Contains("DragDrop.ProcessPortableDragDrop(", activationService, StringComparison.Ordinal);
        Assert.Contains("TryProcessPortableDragDrop(window, e)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("\"ProcessDragDropEvent\"", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("\"ProcessDragDrop\"", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("public bool TryBeginDragMove()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("PlatformServices.WindowDecorations.TryBeginDragMove(_window)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public int Width => _clientWidth;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public int Height => _clientHeight;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public int? Left => _window?.Position.X ?? _windowLeft;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public int? Top => _window?.Position.Y ?? _windowTop;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public bool Topmost => _window?.TopMost ?? _windowTopmost;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public ProGpuWpfWindowBorder WindowBorder => _windowBorder;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public void SetPosition(int left, int top)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_window.Position = new Vector2D<int>(left, top)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public void SetTopmost(bool topmost)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_window.TopMost = topmost", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public void SetWindowBorder(ProGpuWpfWindowBorder windowBorder)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_window.WindowBorder = ToSilkWindowBorder(windowBorder)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("windowOptions.WindowBorder = ToSilkWindowBorder(_windowBorder)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private static SilkWindowBorder ToSilkWindowBorder(ProGpuWpfWindowBorder windowBorder)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("public enum ProGpuWpfWindowBorder", proGpuOptions, StringComparison.Ordinal);
        Assert.Contains("internal void SetInitialClientSize(int width, int height)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("SetClientSizeCore(width, height, updatePortablePresentationSource: false)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private int _requestedLogicalClientWidth = -1;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_requestedLogicalClientWidth = _clientWidth;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_requestedLogicalClientHeight = _clientHeight;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal RenderSurfaceGeometry LastResolvedRenderSurfaceGeometry", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentRenderSurfaceGeometry()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveRenderSurfaceGeometry(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveLogicalClientSize(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveLogicalClientDpiScale(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("UpdateClientSizeFromNativeResize(size, framebufferSize, monitorDpiScale);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var clientSize = _window.Size;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var framebufferSize = _window.FramebufferSize;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("UpdatePortablePresentationSourceClientSize((uint)_clientWidth, (uint)_clientHeight);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var cachedLogicalClientWidth = GetCachedLogicalClientWidth();", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var cachedLogicalClientHeight = GetCachedLogicalClientHeight();", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private int GetCachedLogicalClientWidth()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private int GetCachedLogicalClientHeight()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal static int ResolveCachedLogicalClientDimension(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("DimensionsDifferByDpiScale(larger, smaller)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private static bool DimensionsDifferByDpiScale", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("portablePresentationSourceDimension > 0", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("requestedLogicalDimension > 0", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_requestedLogicalClientHeight = clientHeight;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var logicalSize = ResolveLogicalClientSize(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var logicalWidth = (uint)Math.Max(1, clientWidth);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var logicalHeight = (uint)Math.Max(1, clientHeight);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var fallbackScale = NormalizeMonitorDpiScale(monitorDpiScale);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var scaledPixelWidth = (uint)Math.Max(1, (int)Math.Ceiling(logicalWidth * fallbackScale));", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("PlatformServices.Monitors.GetMonitors()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("using ProGPU.Backend;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("ResolveMonitorDpiScaleWithPlatformFallback(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("DisplayScaleResolver.ResolveWindowDisplayScale(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("DisplayScaleResolver.ResolveDisplayScaleWithPlatformFallback(", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("DisplayScaleResolver.NormalizeDisplayScale(dpiScale)", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveNativePlatformDpiScale", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("TryResolveMacOsBackingScaleFactor", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("window is not INativeWindowSource nativeWindowSource", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("sel_registerName(\"screen\")", proGpuHost, StringComparison.Ordinal);
        Assert.DoesNotContain("backingScaleFactor", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var dpiScaleX = pixelWidth / (double)logicalWidth", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var dpiScaleY = pixelHeight / (double)logicalHeight", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("NativeDimensionLooksPhysicalForCachedDips(nativeDimension, cached, dpiScale)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private static bool NativeDimensionLooksPhysicalForCachedDips", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("TryInferNativeDpiScaleFromCachedDips(nativeDimension, cached", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private static bool FramebufferDimensionAllowsNativePhysicalClient", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("SynchronizePortablePresentationSourceGeometry();", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("SynchronizePortablePresentationSourceGeometry(geometry);", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("UpdatePortablePresentationSourceClientSize(geometry.LogicalWidth, geometry.LogicalHeight)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool UpdatePortablePresentationSourceClientSize(uint logicalWidth, uint logicalHeight)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("LastResolvedRenderSurfaceGeometry = geometry;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private bool _forceFullWpfReplay;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("internal bool ForceFullWpfReplayForNextFrame => _forceFullWpfReplay;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("var forceFullWpfReplay = _forceFullWpfReplay;", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("(forceFullWpfReplay || _target.ShouldReplayVisualSubtree(wpfRootVisual))", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("!forceFullWpfReplay", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("InvalidateWpfRootVisualForPresentationSourceGeometryChange();", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private void InvalidateWpfRootVisualForPresentationSourceGeometryChange()", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_target.WpfInvalidationTracker.MarkDirty(_wpfRootVisual);", proGpuHost, StringComparison.Ordinal);
        int hostOnRender = proGpuHost.IndexOf("private void OnRender(double deltaSeconds)", StringComparison.Ordinal);
        int hostPreReplayGeometrySync = proGpuHost.IndexOf("SynchronizePortablePresentationSourceGeometry(geometry);", hostOnRender, StringComparison.Ordinal);
        int hostPreReplayDispatcherDrain = proGpuHost.IndexOf("ProcessDispatcherQueueCore();", hostPreReplayGeometrySync, StringComparison.Ordinal);
        int hostDetectWpfSourceChanges = proGpuHost.IndexOf("_target.DetectWpfSourceChanges();", hostOnRender, StringComparison.Ordinal);
        Assert.True(
            hostOnRender >= 0 &&
            hostPreReplayGeometrySync >= 0 &&
            hostPreReplayGeometrySync < hostPreReplayDispatcherDrain &&
            hostPreReplayDispatcherDrain < hostDetectWpfSourceChanges,
            "The Silk.NET render callback must synchronize WPF logical geometry before draining dispatcher/layout work and before polling WPF render data.");
        Assert.Contains("_retainedWpfVisualRoot.Scale = new Vector3((float)DpiScaleX, (float)DpiScaleY, 1f)", proGpuDrawingFrame, StringComparison.Ordinal);
        Assert.Contains("logicalWidth,\n                logicalHeight,\n                dpiScaleX,\n                dpiScaleY", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("Present(logicalWidth, logicalHeight, pixelWidth, pixelHeight, dpiScale)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_target.Render(logicalWidth, logicalHeight, pixelWidth, pixelHeight, (float)dpiScale, targetView)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("uint renderTargetWidth", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("_explicitRenderTargetWidth = Math.Max(1, renderTargetWidth)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("uint renderWidth = _explicitRenderTargetWidth ?? width", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("ApplyRenderPassViewport(pass, renderWidth, renderHeight)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("RenderPassEncoderSetViewport(pass, 0f, 0f, targetWidth, targetHeight, 0f, 1f)", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("CurrentCanvasPixelWidth => _explicitRenderTargetWidth.HasValue", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("CurrentCanvasPixelHeight => _explicitRenderTargetHeight.HasValue", proGpuCompositor, StringComparison.Ordinal);
        Assert.Contains("ExplicitPhysicalRenderTargetPinsViewportToPhysicalFramebuffer", proGpuCompositorReviewTests, StringComparison.Ordinal);
        Assert.Contains("HighDpiRetainedWpfLayerRendersAcrossPhysicalFramebuffer", proGpuDrawingFrameTests, StringComparison.Ordinal);
        Assert.Contains("HighDpiSourceDrawingLayerRendersAcrossPhysicalFramebuffer", proGpuDrawingFrameTests, StringComparison.Ordinal);
        Assert.Contains("NativeResizeUsesPortablePresentationSourceLogicalCacheWhenHostCacheWasPhysical", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("NativeResizeRestoresRequestedDipsWhenStartupNativeCacheWasPolluted", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("ResolveCachedLogicalClientDimensionKeepsRequestedDipsWhenSourceCacheIsPhysical", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("NativeResizeRestoresRequestedDipsWhenPortableSourceCacheWasPhysical", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("SetClientSizeSynchronizesBoundPortablePresentationSourceImmediately", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("SetInitialClientSizeCachesLogicalSizeWithoutPortableSourceRelayout", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("SynchronizePortablePresentationSourceGeometryCachesHighDpiSurfaceGeometry", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("UpdatingPortablePresentationSourceClientSizeForcesFullWpfReplay", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("UpdatingPortablePresentationSourceDpiScaleForcesFullWpfReplay", proGpuWindowHostTests, StringComparison.Ordinal);
        Assert.Contains("TryAttachSynchronizesInitialWindowShapeBeforeFirstRender", proGpuActivationTests, StringComparison.Ordinal);
        Assert.Contains("HostDragDropUsesPortableWindowActivationServiceBeforeFallback", proGpuActivationTests, StringComparison.Ordinal);
        Assert.Contains("float dpiScale", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("Compositor.RenderScene(\n            SceneRootVisual,\n            logicalWidth,\n            logicalHeight,\n            pixelWidth,\n            pixelHeight,\n            dpiScale,\n            targetView)", proGpuCompositionTarget, StringComparison.Ordinal);
        Assert.Contains("IWpfWindowDecorationService WindowDecorations", proGpuPlatformServices, StringComparison.Ordinal);
        Assert.Contains("bool TryBeginDragMove(object window)", proGpuPlatformServices, StringComparison.Ordinal);
        Assert.Contains("IWpfMessageBoxService MessageBoxes", proGpuPlatformServices, StringComparison.Ordinal);
        Assert.Contains("string Show(WpfMessageBoxOptions options)", proGpuPlatformServices, StringComparison.Ordinal);
        Assert.Contains("_window.Update += OnUpdate", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("_window.Update -= OnUpdate", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("private void OnUpdate(double deltaSeconds)", proGpuHost, StringComparison.Ordinal);
        Assert.Contains("TryProcessDispatcherWorkWakeup();", proGpuHost, StringComparison.Ordinal);
    }

    [Fact]
    public void InputManagerUsesPortableDevicesOutsideWindows()
    {
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "PresentationCore.csproj");
        var inputManagerPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "InputManager.cs");
        var mouseDevicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "MouseDevice.cs");
        var portableKeyboardPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "PortableKeyboardDevice.cs");
        var portableMousePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "PortableMouseDevice.cs");

        var project = File.ReadAllText(projectPath);
        var inputManager = File.ReadAllText(inputManagerPath);
        var mouseDevice = File.ReadAllText(mouseDevicePath);
        var portableKeyboard = File.ReadAllText(portableKeyboardPath);
        var portableMouse = File.ReadAllText(portableMousePath);

        Assert.Contains(@"<Compile Include=""System\Windows\Input\PortableKeyboardDevice.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\Input\PortableMouseDevice.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains("internal sealed class PortableKeyboardDevice : KeyboardDevice", portableKeyboard, StringComparison.Ordinal);
        Assert.Contains("protected override KeyStates GetKeyStatesFromSystem(Key key)", portableKeyboard, StringComparison.Ordinal);
        Assert.Contains("internal sealed class PortableMouseDevice : MouseDevice", portableMouse, StringComparison.Ordinal);
        Assert.Contains("internal override MouseButtonState GetButtonStateFromSystem(MouseButton mouseButton)", portableMouse, StringComparison.Ordinal);
        Assert.Contains("if (OperatingSystem.IsWindows())", inputManager, StringComparison.Ordinal);
        Assert.Contains("new Win32KeyboardDevice(this)", inputManager, StringComparison.Ordinal);
        Assert.Contains("new Win32MouseDevice(this)", inputManager, StringComparison.Ordinal);
        Assert.Contains("new PortableKeyboardDevice(this)", inputManager, StringComparison.Ordinal);
        Assert.Contains("new PortableMouseDevice(this)", inputManager, StringComparison.Ordinal);
        Assert.Contains("if(OperatingSystem.IsWindows() && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)", inputManager, StringComparison.Ordinal);
        Assert.Contains("if (OperatingSystem.IsWindows())", mouseDevice, StringComparison.Ordinal);
        Assert.Contains("_doubleClickDeltaTime = 500;", mouseDevice, StringComparison.Ordinal);
        Assert.Contains("inputSource?.CompositionTarget != null && !inputSource.CompositionTarget.IsDisposed", mouseDevice, StringComparison.Ordinal);
        Assert.Contains("LocalHitTest(clientUnits, pt, inputSource, out enabledHit, out originalHit)", mouseDevice, StringComparison.Ordinal);
        AssertGuardBefore(mouseDevice, "if (OperatingSystem.IsWindows() && source != null", "UnsafeNativeMethods.WindowFromPoint");
        AssertGuardBefore(mouseDevice, "if (OperatingSystem.IsWindows() && source != null", "SafeNativeMethods.IsWindowEnabled");
        Assert.True(
            inputManager.IndexOf("if (OperatingSystem.IsWindows())", StringComparison.Ordinal)
                < inputManager.IndexOf("new Win32KeyboardDevice(this)", StringComparison.Ordinal),
            "InputManager must branch before constructing Win32 input devices.");
    }

    [Fact]
    public void MediaSystemSkipsMilCoreStartupOnNonWindows()
    {
        var sourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "MediaSystem.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", source, StringComparison.Ordinal);
        Assert.Contains("if (!s_isWindows)", source, StringComparison.Ordinal);
        Assert.Contains("return false;", source, StringComparison.Ordinal);
        Assert.Contains("UnsafeNativeMethods.MilVersionCheck", source, StringComparison.Ordinal);
        Assert.Contains("SafeNativeMethods.MilCompositionEngine_InitializePartitionManager", source, StringComparison.Ordinal);
        Assert.Contains("UnsafeNativeMethods.RenderOptions_EnableHardwareAccelerationInRdp", source, StringComparison.Ordinal);
        Assert.Contains("SafeNativeMethods.MilCompositionEngine_DeinitializePartitionManager", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("return false;", StringComparison.Ordinal)
                < source.IndexOf("UnsafeNativeMethods.MilVersionCheck", StringComparison.Ordinal),
            "The non-Windows startup path must return disconnected before any MILCore version check or partition startup.");

        var connectChannelsIndex = source.IndexOf("internal static bool ConnectChannels", StringComparison.Ordinal);
        var connectGuardIndex = source.IndexOf("if (!s_isWindows)", connectChannelsIndex, StringComparison.Ordinal);
        var createChannelsIndex = source.IndexOf("mc.CreateChannels()", StringComparison.Ordinal);
        Assert.True(
            connectGuardIndex > connectChannelsIndex && connectGuardIndex < createChannelsIndex,
            "The non-Windows channel path must return before creating DUCE channels.");

        var shutdownIndex = source.IndexOf("internal static void Shutdown", StringComparison.Ordinal);
        var shutdownGuardIndex = source.IndexOf("if (!s_isWindows)", shutdownIndex, StringComparison.Ordinal);
        var deinitializeIndex = source.IndexOf("SafeNativeMethods.MilCompositionEngine_DeinitializePartitionManager", StringComparison.Ordinal);
        Assert.True(
            shutdownGuardIndex > shutdownIndex && shutdownGuardIndex < deinitializeIndex,
            "The non-Windows shutdown path must return before MILCore partition deinitialization.");
    }

    [Fact]
    public void HwndTargetDoesNotRegisterWin32MessagesOnNonWindows()
    {
        var sourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "InterOp",
            "HwndTarget.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", source, StringComparison.Ordinal);
        Assert.Contains("if (!s_isWindows)", source, StringComparison.Ordinal);
        Assert.Contains("UnsafeNativeMethods.RegisterWindowMessage(\"UpdateWindowSettings\")", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("if (!s_isWindows)", StringComparison.Ordinal)
                < source.IndexOf("UnsafeNativeMethods.RegisterWindowMessage(\"UpdateWindowSettings\")", StringComparison.Ordinal),
            "The non-Windows guard must run before registering HwndTarget Win32 window messages.");
        Assert.True(
            source.IndexOf("throw new PlatformNotSupportedException", StringComparison.Ordinal)
                < source.IndexOf("SafeNativeMethods.GetCurrentSessionId()", StringComparison.Ordinal),
            "The non-Windows constructor failure must run before Win32 session or HWND initialization.");
    }

    [Fact]
    public void PresentationFrameworkHasPortableWindowActivationBoundary()
    {
        var windowPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Window.cs");
        var applicationPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Application.cs");
        var activationServicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "PortableWindowActivationService.cs");
        var portableInputPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "PortableInputEventArgs.cs");
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "PresentationFramework.csproj");

        var window = File.ReadAllText(windowPath);
        var application = File.ReadAllText(applicationPath);
        var activationService = File.ReadAllText(activationServicePath);
        var portableInput = File.ReadAllText(portableInputPath);
        var project = File.ReadAllText(projectPath);

        Assert.Contains("internal sealed class PortableInputEventArgs", portableInput, StringComparison.Ordinal);
        Assert.Contains("internal enum PortableInputEventKind", portableInput, StringComparison.Ordinal);
        Assert.Contains("internal enum PortableMouseButton", portableInput, StringComparison.Ordinal);
        Assert.Contains("internal enum PortableInputModifiers", portableInput, StringComparison.Ordinal);
        Assert.Contains("public bool Handled { get; set; }", portableInput, StringComparison.Ordinal);

        Assert.Contains("internal static class PortableWindowActivationService", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void Register", activationService, StringComparison.Ordinal);
        Assert.Contains("Func<object, object> activate", activationService, StringComparison.Ordinal);
        Assert.Contains("!OperatingSystem.IsWindows()", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryActivate(Window window, out object activation)", activationService, StringComparison.Ordinal);
        Assert.Contains("Action<object, object> setWindowState", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetWindowState(object activation, WindowState windowState)", activationService, StringComparison.Ordinal);
        Assert.Contains("Action<object, string> setTitle", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetTitle(object activation, string title)", activationService, StringComparison.Ordinal);
        Assert.Contains("Action<object, double, double> setClientSize", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetClientSize(object activation, double width, double height)", activationService, StringComparison.Ordinal);
        Assert.Contains("Action<object, double, double> setPosition", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetPosition(object activation, double left, double top)", activationService, StringComparison.Ordinal);
        Assert.Contains("Action<object, bool> setTopmost", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetTopmost(object activation, bool topmost)", activationService, StringComparison.Ordinal);
        Assert.Contains("Action<object, object, object> setWindowBorder", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetWindowBorder(object activation, ResizeMode resizeMode, WindowStyle windowStyle)", activationService, StringComparison.Ordinal);
        Assert.Contains("Func<object, bool> dragMove", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryDragMove(object activation)", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void SetActivationState(Window window, bool isActive)", activationService, StringComparison.Ordinal);
        Assert.Contains("NotifyPortableInputProvidersDeactivated(window)", activationService, StringComparison.Ordinal);
        Assert.Contains("source.GetInputProvider(typeof(KeyboardDevice))?.NotifyDeactivate()", activationService, StringComparison.Ordinal);
        Assert.Contains("source.GetInputProvider(typeof(MouseDevice))?.NotifyDeactivate()", activationService, StringComparison.Ordinal);
        Assert.Contains("window.HandleActivate(isActive)", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static void ProcessInput(Window window, PortableInputEventArgs input)", activationService, StringComparison.Ordinal);
        Assert.Contains("PresentationSource.CriticalFromVisual(window)", activationService, StringComparison.Ordinal);
        Assert.Contains("input.Handled = ProcessInput(source, input)", activationService, StringComparison.Ordinal);
        Assert.Contains("InputManager.UnsecureCurrent", activationService, StringComparison.Ordinal);
        Assert.Contains("new RawKeyboardInputReport", activationService, StringComparison.Ordinal);
        Assert.Contains("new RawTextInputReport", activationService, StringComparison.Ordinal);
        Assert.Contains("new RawMouseInputReport", activationService, StringComparison.Ordinal);
        Assert.Contains("InputManager.PreviewInputReportEvent", activationService, StringComparison.Ordinal);
        Assert.Contains("PortableKeyboardDevice", activationService, StringComparison.Ordinal);
        Assert.Contains("PortableMouseDevice", activationService, StringComparison.Ordinal);
        Assert.Contains("Mouse.MouseWheelDeltaForOneLine", activationService, StringComparison.Ordinal);
        Assert.DoesNotContain("Routed portable input is implemented in a later InputManager slice", activationService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryRun(Window window)", activationService, StringComparison.Ordinal);
        Assert.Contains("window.PortableWindowActivation", activationService, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\PortableInputEventArgs.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\PortableWindowActivationService.cs"" />", project, StringComparison.Ordinal);

        Assert.Contains("private object              _portableWindowActivation", window, StringComparison.Ordinal);
        Assert.Contains("internal object PortableWindowActivation", window, StringComparison.Ordinal);
        Assert.Contains("internal void HandlePortableInput(PortableInputEventArgs input)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.ProcessInput(this, input)", window, StringComparison.Ordinal);
        Assert.Contains("TryCreatePortableWindowDuringShow()", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.TryActivate(this, out object activation)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.Show(_portableWindowActivation)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.Hide(_portableWindowActivation)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetWindowState(_portableWindowActivation, windowState)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetTitle(_portableWindowActivation, Title)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetClientSize(_portableWindowActivation, Width, height)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetClientSize(_portableWindowActivation, width, Height)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetPosition(_portableWindowActivation, leftLogicalUnits, topLogicalUnits)", window, StringComparison.Ordinal);
        Assert.Contains("private void UpdatePortablePositionOnTopLeftChange(double leftLogicalUnits, double topLogicalUnits)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetTopmost(_portableWindowActivation, topmost)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetWindowBorder(_portableWindowActivation, ResizeMode, windowStyle)", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.SetWindowBorder(_portableWindowActivation, ResizeMode, WindowStyle)", window, StringComparison.Ordinal);
        Assert.Contains("&& !w.IsPortableWindowActive", window, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.TryDragMove(_portableWindowActivation)", window, StringComparison.Ordinal);
        Assert.Contains("if (PortableWindowActivationService.IsEnabled)", window, StringComparison.Ordinal);
        Assert.Contains("return ShowPortableDialog();", window, StringComparison.Ordinal);
        Assert.Contains("private Nullable<bool> ShowPortableDialog()", window, StringComparison.Ordinal);
        Assert.Contains("if (_showingAsDialog)", window, StringComparison.Ordinal);
        Assert.Contains("DoDialogHide();", window, StringComparison.Ordinal);
        Assert.Contains("if (IsPortableWindowActive)", window, StringComparison.Ordinal);
        AssertGuardBefore(window, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SendMessage( Handle, WindowMessage.WM_SYSCOMMAND");
        Assert.Contains("ClosePortableWindowActivation();", window, StringComparison.Ordinal);
        Assert.Contains("private bool IsPortableWindowActive", window, StringComparison.Ordinal);
        Assert.Contains("if (value != null && value.IsSourceWindowNull && !value.IsPortableWindowActive)", window, StringComparison.Ordinal);
        Assert.Contains("private bool IsLayoutSourceUnavailable", window, StringComparison.Ordinal);
        Assert.Contains("return !IsPortableWindowActive && (IsSourceWindowNull || IsCompositionTargetInvalid)", window, StringComparison.Ordinal);
        Assert.Contains("private Size GetWindowFrameSizeInMeasureUnits()", window, StringComparison.Ordinal);
        Assert.Contains("private Size GetWindowSizeInMeasureUnits()", window, StringComparison.Ordinal);
        Assert.Contains("if (IsPortableWindowActive)", window, StringComparison.Ordinal);
        Assert.Contains("mm.maxWidth = MinWidth > MaxWidth ? MinWidth : MaxWidth", window, StringComparison.Ordinal);
        Assert.Contains("mm.maxHeight = MinHeight > MaxHeight ? MinHeight : MaxHeight", window, StringComparison.Ordinal);
        Assert.Contains("return IsPortableWindowActive ? new Size(0, 0) : GetHwndNonClientAreaSizeInMeasureUnits()", window, StringComparison.Ordinal);
        Assert.Contains("return new Size(ToNonNegativeFiniteSize(width), ToNonNegativeFiniteSize(height))", window, StringComparison.Ordinal);
        Assert.Contains("private void RefreshPortableRootVisualState()", window, StringComparison.Ordinal);
        Assert.Contains("ApplyTemplate();", window, StringComparison.Ordinal);
        Assert.Contains("UpdateIsVisibleCache();", window, StringComparison.Ordinal);
        Assert.Contains("InvalidateForceInheritPropertyOnChildren(IsVisibleProperty);", window, StringComparison.Ordinal);
        Assert.Contains("Size windowSize = GetWindowSizeInMeasureUnits();", window, StringComparison.Ordinal);
        Assert.Contains("Measure(windowSize);", window, StringComparison.Ordinal);
        Assert.Contains("Arrange(new Rect(windowSize));", window, StringComparison.Ordinal);
        Assert.Contains("UpdateLayout();", window, StringComparison.Ordinal);
        Assert.Contains("private void RefreshPortableInheritedVisibility()", window, StringComparison.Ordinal);
        Assert.Contains("if (Content is UIElement contentElement)", window, StringComparison.Ordinal);
        Assert.Contains("UIElement.SynchronizeForceInheritProperties(contentElement, null, null, this)", window, StringComparison.Ordinal);
        Assert.Contains("contentElement.InvalidateForceInheritPropertyOnChildren(IsVisibleProperty)", window, StringComparison.Ordinal);
        Assert.True(
            window.IndexOf("RefreshPortableRootVisualState();", StringComparison.Ordinal)
                < window.IndexOf("PortableWindowActivationService.Show(_portableWindowActivation)", StringComparison.Ordinal),
            "Portable Window.Show must refresh the WPF root template, inherited visibility, and layout before showing the native host.");
        Assert.True(
            window.IndexOf("if (TryCreatePortableWindowDuringShow())", StringComparison.Ordinal)
                < window.IndexOf("CreateSourceWindow(true);", StringComparison.Ordinal),
            "Window.Show must try the portable activation service before falling back to HWND creation.");

        Assert.Contains("if (!OperatingSystem.IsWindows())", application, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.IsEnabled", application, StringComparison.Ordinal);
        Assert.Contains("FlushPortableDispatcherOperations(DispatcherPriority.Send)", application, StringComparison.Ordinal);
        Assert.Contains("FlushPortableDispatcherOperations(DispatcherPriority.ApplicationIdle)", application, StringComparison.Ordinal);
        Assert.Contains("private void FlushPortableDispatcherOperations(DispatcherPriority markerPriority)", application, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds(250)", application, StringComparison.Ordinal);
        Assert.Contains("wnd.Show();", application, StringComparison.Ordinal);
        Assert.Contains("wnd.Visibility = Visibility.Visible;", application, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.PushFrame(frame)", application, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService.TryRun(MainWindow)", application, StringComparison.Ordinal);
        Assert.Contains("if (!_appIsShutdown)", application, StringComparison.Ordinal);
        Assert.True(
            application.IndexOf("if (!OperatingSystem.IsWindows())", StringComparison.Ordinal)
                < application.IndexOf("new HwndWrapper", StringComparison.Ordinal),
            "Application.Run must skip the parking HWND before any HwndWrapper is created on non-Windows.");
        Assert.True(
            application.IndexOf("FlushPortableDispatcherOperations(DispatcherPriority.Send)", StringComparison.Ordinal)
                < application.IndexOf("window.Show();", StringComparison.Ordinal),
            "Application.Run must service queued startup work before synchronously showing the portable startup window.");
        Assert.True(
            application.IndexOf("window.Show();", StringComparison.Ordinal)
                < application.IndexOf("PortableWindowActivationService.TryRun(MainWindow)", StringComparison.Ordinal),
            "Application.Run must synchronously show the startup window before handing ownership to the portable native run loop.");
        int portableTryRun = application.IndexOf(
            "PortableWindowActivationService.TryRun(MainWindow)",
            StringComparison.Ordinal);
        int applicationIdleFlush = application.IndexOf(
            "FlushPortableDispatcherOperations(DispatcherPriority.ApplicationIdle)",
            StringComparison.Ordinal);
        Assert.True(
            portableTryRun < applicationIdleFlush,
            "Application.Run must leave normal and idle app work for the portable native run loop, then service queued shutdown work after it exits.");
        Assert.True(
            application.IndexOf("PortableWindowActivationService.TryRun(MainWindow)", StringComparison.Ordinal)
                < application.IndexOf("RunDispatcher(null);", StringComparison.Ordinal),
            "Application.Run must use the portable native run loop before falling back to WPF Dispatcher.Run.");
    }

    [Fact]
    public void PresentationFrameworkMessageBoxUsesPortableServiceOutsideWindows()
    {
        var messageBoxPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "MessageBox.cs");
        var messageBoxServicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "PortableMessageBoxService.cs");
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "PresentationFramework.csproj");
        var proGpuActivationPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortableWindowActivation.cs");
        var runtimeHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlRuntimeHarness",
            "Program.cs");
        var applicationRunHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealApplicationRunHarness",
            "Program.cs");
        var sdkRuntimeHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchRuntimeHarness",
            "Program.cs");

        var messageBox = File.ReadAllText(messageBoxPath);
        var messageBoxService = File.ReadAllText(messageBoxServicePath);
        var project = File.ReadAllText(projectPath);
        var proGpuActivation = File.ReadAllText(proGpuActivationPath);
        var runtimeHarness = File.ReadAllText(runtimeHarnessPath);
        var applicationRunHarness = File.ReadAllText(applicationRunHarnessPath);
        var sdkRuntimeHarness = File.ReadAllText(sdkRuntimeHarnessPath);

        Assert.Contains(@"<Compile Include=""System\Windows\PortableMessageBoxService.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains("internal readonly struct PortableMessageBoxRequest", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("internal static class PortableMessageBoxService", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Func<object, object> show)", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryShow(", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("return MessageBox.GetPortableFallbackResult(DefaultResult, Button)", messageBoxService, StringComparison.Ordinal);
        Assert.Contains("return !s_isWindows && Volatile.Read(ref s_show) != null", messageBoxService, StringComparison.Ordinal);

        Assert.Contains("return ShowCore(owner, messageBoxText, caption, button, icon, defaultResult, options)", messageBox, StringComparison.Ordinal);
        Assert.Contains("GetMessageBoxOwnerHandle(owner)", messageBox, StringComparison.Ordinal);
        Assert.Contains("if (ownerHandle == IntPtr.Zero && OperatingSystem.IsWindows())", messageBox, StringComparison.Ordinal);
        Assert.Contains("if (!OperatingSystem.IsWindows())", messageBox, StringComparison.Ordinal);
        Assert.Contains("PortableMessageBoxService.TryShow(", messageBox, StringComparison.Ordinal);
        Assert.Contains("return GetPortableFallbackResult(defaultResult, button)", messageBox, StringComparison.Ordinal);
        Assert.Contains("return new WindowInteropHelper(owner).Handle", messageBox, StringComparison.Ordinal);
        Assert.True(
            messageBox.IndexOf("if (!OperatingSystem.IsWindows())", StringComparison.Ordinal)
                < messageBox.IndexOf("UnsafeNativeMethods.MessageBox", StringComparison.Ordinal),
            "MessageBox.ShowCore must try the portable service before the Win32 MessageBox call.");

        Assert.Contains("PortableMessageBoxServiceTypeName", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryRegisterPresentationFrameworkMessageBoxService(presentationFrameworkAssembly)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("typeof(Func<object, object>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("ShowPortableMessageBox", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("new WpfMessageBoxOptions", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("CrossPlatformWpfPlatformServices.Instance.MessageBoxes.Show(options)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("\"FallbackResult\"", proGpuActivation, StringComparison.Ordinal);

        Assert.Contains("PortableMessageBoxServiceTypeName = \"System.Windows.PortableMessageBoxService\"", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMessageBox(presentationFramework, window)", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable MessageBox no-owner default result", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable MessageBox owner fallback result", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationFramework, PortableMessageBoxServiceTypeName)", runtimeHarness, StringComparison.Ordinal);

        Assert.Contains("PortableMessageBoxServiceTypeName = \"System.Windows.PortableMessageBoxService\"", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("RegisterPortableMessageBox(presentationFramework)", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMessageBox(_presentationFramework, typedActivation.Window)", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("portable MessageBox owner fallback result", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationFramework, PortableMessageBoxServiceTypeName)", applicationRunHarness, StringComparison.Ordinal);

        Assert.Contains("PortableMessageBoxServiceTypeName = \"System.Windows.PortableMessageBoxService\"", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("RegisterPortableMessageBox(presentationFramework)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMessageBox(presentationFramework, window)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMessageBox(_presentationFramework, typedActivation.Window)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable MessageBox SDK no-owner default result", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable MessageBox SDK owner fallback result", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationFramework, PortableMessageBoxServiceTypeName)", sdkRuntimeHarness, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationFrameworkBrowserLaunchUsesPortableServiceOutsideWindows()
    {
        var appSecurityManagerPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "MS",
            "Internal",
            "AppModel",
            "AppSecurityManager.cs");
        var launcherServicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "PortableLauncherService.cs");
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "PresentationFramework.csproj");
        var proGpuActivationPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortableWindowActivation.cs");

        var appSecurityManager = File.ReadAllText(appSecurityManagerPath);
        var launcherService = File.ReadAllText(launcherServicePath);
        var project = File.ReadAllText(projectPath);
        var proGpuActivation = File.ReadAllText(proGpuActivationPath);

        Assert.Contains(@"<Compile Include=""System\Windows\PortableLauncherService.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains("internal readonly struct PortableLaunchRequest", launcherService, StringComparison.Ordinal);
        Assert.Contains("internal static class PortableLauncherService", launcherService, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Func<object, bool> launch)", launcherService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryLaunch(Uri uri, string targetFrame, bool isTopLevel, out bool launched)", launcherService, StringComparison.Ordinal);
        Assert.Contains("return !s_isWindows && Volatile.Read(ref s_launch) != null", launcherService, StringComparison.Ordinal);

        Assert.Contains("if (!OperatingSystem.IsWindows() && isSafeLaunch)", appSecurityManager, StringComparison.Ordinal);
        Assert.Contains("PortableLauncherService.TryLaunch(destinationUri, targetName, fIsTopLevel", appSecurityManager, StringComparison.Ordinal);
        Assert.Contains("PortableLauncherService.TryLaunch(uri, null, true", appSecurityManager, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(SR.FailToLaunchDefaultBrowser)", appSecurityManager, StringComparison.Ordinal);
        Assert.True(
            appSecurityManager.IndexOf("if (!OperatingSystem.IsWindows() && isSafeLaunch)", StringComparison.Ordinal)
                < appSecurityManager.IndexOf("UnsafeNativeMethods.ShellExecute", StringComparison.Ordinal),
            "Safe browser launch must try the portable launcher before the Win32 ShellExecute path.");
        Assert.True(
            appSecurityManager.IndexOf("if (!OperatingSystem.IsWindows())", StringComparison.Ordinal)
                < appSecurityManager.IndexOf("UnsafeNativeMethods.ShellExecuteInfo", StringComparison.Ordinal),
            "Default browser launch must avoid ShellExecuteEx on non-Windows.");

        Assert.Contains("PortableLauncherServiceTypeName = \"System.Windows.PortableLauncherService\"", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryRegisterPresentationFrameworkLauncherService(presentationFrameworkAssembly)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("typeof(Func<object, bool>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("LaunchPortableUri", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("CrossPlatformWpfPlatformServices.Instance.Launcher", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains(".OpenUriAsync(uri!)", proGpuActivation, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationCoreClipboardUsesPortableServiceOutsideWindows()
    {
        var clipboardPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "clipboard.cs");
        var clipboardServicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "PortableClipboardService.cs");
        var portableManagedDataObjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "PortableManagedDataObject.cs");
        var dataObjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "dataobject.cs");
        var portableClipboardServiceTestsPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "tests",
            "UnitTests",
            "PresentationCore.Tests",
            "System",
            "Windows",
            "PortableClipboardServiceTests.cs");
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "PresentationCore.csproj");
        var runtimeHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlRuntimeHarness",
            "Program.cs");
        var applicationRunHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealApplicationRunHarness",
            "Program.cs");
        var sdkRuntimeHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchRuntimeHarness",
            "Program.cs");
        var proGpuActivationPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortableWindowActivation.cs");
        var portableBootstrapPath = FindRepoPath(
            "packaging",
            "ProGPU.Wpf.Sdk",
            "targets",
            "ProGPU.Wpf.Sdk.PortableBootstrap.cs");

        var clipboard = File.ReadAllText(clipboardPath);
        var clipboardService = File.ReadAllText(clipboardServicePath);
        var portableManagedDataObject = File.ReadAllText(portableManagedDataObjectPath);
        var dataObject = File.ReadAllText(dataObjectPath);
        var portableClipboardServiceTests = File.ReadAllText(portableClipboardServiceTestsPath);
        var project = File.ReadAllText(projectPath);
        var runtimeHarness = File.ReadAllText(runtimeHarnessPath);
        var applicationRunHarness = File.ReadAllText(applicationRunHarnessPath);
        var sdkRuntimeHarness = File.ReadAllText(sdkRuntimeHarnessPath);
        var proGpuActivation = File.ReadAllText(proGpuActivationPath);
        var portableBootstrap = File.ReadAllText(portableBootstrapPath);

        Assert.Contains(@"<Compile Include=""System\Windows\PortableClipboardService.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\PortableManagedDataObject.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains("internal static class PortableClipboardService", clipboardService, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Func<string?> getText, Action<string?> setText)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryGetDataObject(out IDataObject? dataObject)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TrySetDataObject(IDataObject dataObject, bool copy)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TrySetData(string format, object data, bool autoConvert, bool copy)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TrySetFileDropList(StringCollection fileDropList)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TrySetObject(object data, bool copy)", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryIsCurrent(IDataObject data, out bool isCurrent)", clipboardService, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed class PortableDataObject", clipboardService, StringComparison.Ordinal);
        Assert.Contains("new PortableManagedDataObject()", clipboardService, StringComparison.Ordinal);
        Assert.Contains("internal sealed class PortableManagedDataObject : ITypedDataObject", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("private readonly Dictionary<string, Entry> _data", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("using System.Text.Json;", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("internal void SetDataAsJson<T>(string format, T data)", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("private static bool TryDeserializeJsonPayload<T>", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("private sealed class JsonPayload", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("public bool TryGetData<T>", portableManagedDataObject, StringComparison.Ordinal);
        Assert.Contains("private readonly ITypedDataObject? _portableData;", dataObject, StringComparison.Ordinal);
        Assert.Contains("_portableData = new PortableManagedDataObject();", dataObject, StringComparison.Ordinal);
        Assert.Contains("portableManagedData.SetDataAsJson(format, data);", dataObject, StringComparison.Ordinal);
        Assert.Contains("return !s_isWindows;", clipboardService, StringComparison.Ordinal);
        Assert.Contains("s_dataObject = dataObject;", clipboardService, StringComparison.Ordinal);
        Assert.Contains("private static bool s_hasManagedClipboardState;", clipboardService, StringComparison.Ordinal);
        Assert.Contains("s_hasManagedClipboardState = true;", clipboardService, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref s_setText)?.Invoke(hasUnicodeText ? text : null);", clipboardService, StringComparison.Ordinal);
        Assert.Contains("ClearKeepsManagedStateAuthoritativeOverStaleNativeText", portableClipboardServiceTests, StringComparison.Ordinal);
        Assert.Contains("SetFileDropListClearsNativeTextMirror", portableClipboardServiceTests, StringComparison.Ordinal);

        Assert.Contains("if (PortableClipboardService.TryClear())", clipboard, StringComparison.Ordinal);
        Assert.Contains("if (PortableClipboardService.TryFlush())", clipboard, StringComparison.Ordinal);
        Assert.Contains("if (PortableClipboardService.TryGetDataObject(out IDataObject? portableDataObject))", clipboard, StringComparison.Ordinal);
        Assert.Contains("if (PortableClipboardService.TryIsCurrent(data, out bool isCurrent))", clipboard, StringComparison.Ordinal);
        Assert.Contains("if (PortableClipboardService.TrySetObject(data, copy))", clipboard, StringComparison.Ordinal);
        Assert.Contains("if (PortableClipboardService.TrySetFileDropList(fileDropList))", clipboard, StringComparison.Ordinal);
        Assert.Contains("bool autoConvert = IsDataFormatAutoConvert(format);", clipboard, StringComparison.Ordinal);
        Assert.Contains("if (PortableClipboardService.TrySetData(format, data, autoConvert, copy: true))", clipboard, StringComparison.Ordinal);
        Assert.Contains("private static void SetOleDataObject(object data, bool copy)", clipboard, StringComparison.Ordinal);
        Assert.Contains("private static void SetOleDataInternal(string format, object data, bool autoConvert)", clipboard, StringComparison.Ordinal);
        Assert.True(
            clipboard.IndexOf("PortableClipboardService.TryGetDataObject", StringComparison.Ordinal)
                < clipboard.IndexOf("ClipboardCore.GetDataObject", StringComparison.Ordinal),
            "Clipboard.GetDataObject must try the portable service before the OLE clipboard path.");
        Assert.True(
            clipboard.IndexOf("PortableClipboardService.TrySetObject", StringComparison.Ordinal)
                < clipboard.IndexOf("ClipboardCore.SetData(dataObject, copy)", StringComparison.Ordinal),
            "Clipboard.SetDataObject must try the portable service before the OLE clipboard path.");

        Assert.Contains("PortableClipboardServiceTypeName = \"System.Windows.PortableClipboardService\"", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableClipboard(presentationCore)", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard data object unicode text", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationCore, PortableClipboardServiceTypeName)", runtimeHarness, StringComparison.Ordinal);

        Assert.Contains("PortableClipboardServiceTypeName = \"System.Windows.PortableClipboardService\"", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableClipboard(presentationCore)", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard current data object", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationCore, PortableClipboardServiceTypeName)", applicationRunHarness, StringComparison.Ordinal);

        Assert.Contains("PortableClipboardServiceTypeName = \"System.Windows.PortableClipboardService\"", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableClipboard(presentationCore)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK data object unicode text", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK current data object", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableRichClipboardFormats(presentationCore)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK file drop state", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK custom data state", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK audio state", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK image data format state", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableJsonDataObject(presentationCore)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("PortableClipboardJsonPayload", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK JSON DataObject typed retrieval state", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK JSON clipboard typed retrieval state", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationCore, PortableClipboardServiceTypeName)", sdkRuntimeHarness, StringComparison.Ordinal);

        Assert.Contains("PortableClipboardServiceTypeName", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryRegisterPresentationCoreClipboardService", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("typeof(Func<string?>), typeof(Action<string?>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("GetPortableClipboardText", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("SetPortableClipboardText", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("CrossPlatformWpfPlatformServices.Instance.Clipboard", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("return string.IsNullOrEmpty(text) ? null : text", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("WpfPortableWindowActivation.TryRegisterPresentationCoreClipboardService(typeof(Clipboard).Assembly)", portableBootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationFrameworkFileDialogsUsePortableServiceOutsideWindows()
    {
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "PresentationFramework.csproj");
        var commonDialogPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "Microsoft",
            "Win32",
            "CommonDialog.cs");
        var commonItemDialogPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "Microsoft",
            "Win32",
            "CommonItemDialog.cs");
        var fileDialogPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "Microsoft",
            "Win32",
            "FileDialog.cs");
        var servicePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "Microsoft",
            "Win32",
            "PortableFileDialogService.cs");
        var activationPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfPortableWindowActivation.cs");
        var runtimeHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlRuntimeHarness",
            "Program.cs");
        var applicationRunHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealApplicationRunHarness",
            "Program.cs");
        var sdkRuntimeHarnessPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchRuntimeHarness",
            "Program.cs");

        var project = File.ReadAllText(projectPath);
        var commonDialog = File.ReadAllText(commonDialogPath);
        var commonItemDialog = File.ReadAllText(commonItemDialogPath);
        var fileDialog = File.ReadAllText(fileDialogPath);
        var service = File.ReadAllText(servicePath);
        var activation = File.ReadAllText(activationPath);
        var runtimeHarness = File.ReadAllText(runtimeHarnessPath);
        var applicationRunHarness = File.ReadAllText(applicationRunHarnessPath);
        var sdkRuntimeHarness = File.ReadAllText(sdkRuntimeHarnessPath);

        Assert.Contains(@"<Compile Include=""Microsoft\Win32\PortableFileDialogService.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains("internal static class PortableFileDialogService", service, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)", service, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable Register(Func<object, string> showDialog)", service, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryShowDialog(CommonItemDialog dialog, out string selectedPath)", service, StringComparison.Ordinal);
        Assert.Contains("private sealed class PortableFileDialogRequest", service, StringComparison.Ordinal);
        Assert.Contains("public string Kind { get; }", service, StringComparison.Ordinal);
        Assert.Contains("public string SuggestedItemName { get; }", service, StringComparison.Ordinal);

        Assert.Contains("itemDialog.TryRunPortableDialog(out bool? portableResult)", commonDialog, StringComparison.Ordinal);
        Assert.Contains("private bool? ShowWin32Dialog()", commonDialog, StringComparison.Ordinal);
        Assert.Contains("private bool? ShowWin32Dialog(Window owner)", commonDialog, StringComparison.Ordinal);
        Assert.True(
            commonDialog.IndexOf("itemDialog.TryRunPortableDialog(out bool? portableResult)", StringComparison.Ordinal)
                < commonDialog.IndexOf("UnsafeNativeMethods.GetActiveWindow()", StringComparison.Ordinal),
            "CommonDialog.ShowDialog must try the portable service before reading Win32 active-window state.");
        Assert.True(
            commonDialog.IndexOf("itemDialog.TryRunPortableDialog(out bool? portableResult)", StringComparison.Ordinal)
                < commonDialog.IndexOf("new WindowInteropHelper(owner)", StringComparison.Ordinal),
            "CommonDialog.ShowDialog(owner) must try the portable service before requiring an HWND owner.");

        Assert.Contains("internal bool TryRunPortableDialog(out bool? result)", commonItemDialog, StringComparison.Ordinal);
        Assert.Contains("private protected virtual bool TryHandlePortableItemOk(out object revertState)", commonItemDialog, StringComparison.Ordinal);
        Assert.Contains("private bool HandlePortableItemOk(string selectedPath)", commonItemDialog, StringComparison.Ordinal);
        Assert.Contains("OnItemOk(cancelArgs)", commonItemDialog, StringComparison.Ordinal);
        Assert.Contains("private protected override bool TryHandlePortableItemOk(out object restoreState)", fileDialog, StringComparison.Ordinal);
        Assert.Contains("return ProcessFileNames();", fileDialog, StringComparison.Ordinal);

        Assert.Contains("PortableFileDialogServiceTypeName = \"Microsoft.Win32.PortableFileDialogService\"", activation, StringComparison.Ordinal);
        Assert.Contains("TryRegisterPresentationFrameworkFileDialogService(presentationFrameworkAssembly)", activation, StringComparison.Ordinal);
        Assert.Contains("CrossPlatformWpfPlatformServices.Instance.FileDialogs", activation, StringComparison.Ordinal);
        Assert.Contains("ReadFileDialogPatterns(request)", activation, StringComparison.Ordinal);

        Assert.Contains("PortableFileDialogServiceTypeName = \"Microsoft.Win32.PortableFileDialogService\"", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableFileDialogs(presentationFramework)", runtimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable SaveFileDialog FileName", runtimeHarness, StringComparison.Ordinal);

        Assert.Contains("PortableFileDialogServiceTypeName = \"Microsoft.Win32.PortableFileDialogService\"", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableFileDialogs(presentationFramework)", applicationRunHarness, StringComparison.Ordinal);
        Assert.Contains("portable OpenFolderDialog FolderName", applicationRunHarness, StringComparison.Ordinal);

        Assert.Contains("PortableFileDialogServiceTypeName = \"Microsoft.Win32.PortableFileDialogService\"", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableFileDialogs(presentationFramework)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableFileDialogs(_presentationFramework, typedActivation.Window)", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ownerPrefix = owner is null ? \"no-owner\" : \"owner\"", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable SDK {ownerPrefix} SaveFileDialog FileName", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("portable SDK {ownerPrefix} OpenFolderDialog FolderName", sdkRuntimeHarness, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationFramework, PortableFileDialogServiceTypeName)", sdkRuntimeHarness, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedSubsystemBringupReusesRealWpfXamlFrameworkAndThemeProjects()
    {
        var systemXamlProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "System.Xaml",
            "System.Xaml.csproj");
        var presentationBuildTasksProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationBuildTasks",
            "PresentationBuildTasks.csproj");
        var presentationFrameworkProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "PresentationFramework.csproj");
        var fluentThemeProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "Themes",
            "PresentationFramework.Fluent",
            "PresentationFramework.Fluent.csproj");
        var realPresentationCoreHarnessProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealPresentationCoreHarness",
            "ProGPU.Wpf.RealPresentationCoreHarness.csproj");

        var systemXamlProject = XDocument.Load(systemXamlProjectPath);
        var presentationBuildTasksProject = XDocument.Load(presentationBuildTasksProjectPath);
        var presentationFrameworkProject = XDocument.Load(presentationFrameworkProjectPath);
        var fluentThemeProject = XDocument.Load(fluentThemeProjectPath);
        var realPresentationCoreHarnessProject = XDocument.Load(realPresentationCoreHarnessProjectPath);

        Assert.Equal("System.Xaml", Assert.Single(systemXamlProject.Descendants("AssemblyName")).Value);
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "System.Xaml", "System", "Xaml", "XamlReader.cs");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "System.Xaml", "System", "Xaml", "InfosetObjects", "XamlXmlReader.cs");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "System.Xaml", "System", "Xaml", "InfosetObjects", "XamlObjectWriter.cs");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "System.Xaml", "System", "Windows", "Markup", "MarkupExtension.cs");
        AssertProjectReference(systemXamlProject, @"System.Xaml\ref\System.Xaml-ref.csproj");

        var targetFrameworks = Assert.Single(presentationBuildTasksProject.Descendants("TargetFrameworks")).Value;
        Assert.Contains("$(BundledNETCoreAppTargetFramework)", targetFrameworks, StringComparison.Ordinal);
        Assert.Contains("$(NetFrameworkToolCurrent)", targetFrameworks, StringComparison.Ordinal);
        AssertCompileInclude(presentationBuildTasksProject, @"MS\Internal\MarkupCompiler\MarkupCompiler.cs");
        AssertCompileInclude(presentationBuildTasksProject, @"MS\Internal\MarkupCompiler\ParserExtension.cs");
        AssertCompileInclude(presentationBuildTasksProject, @"Microsoft\Build\Tasks\Windows\MarkupCompilePass1.cs");
        AssertCompileInclude(presentationBuildTasksProject, @"Microsoft\Build\Tasks\Windows\MarkupCompilePass2.cs");
        AssertCompileInclude(presentationBuildTasksProject, @"PresentationFramework\System\Windows\Markup\BamlBinaryWriter.cs", link: true);
        AssertCompileInclude(presentationBuildTasksProject, @"PresentationFramework\System\Windows\Markup\BamlRecords.cs", link: true);

        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Application.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Window.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\ResourceDictionary.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Style.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\ControlTemplate.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Data\ObjectDataProvider.cs");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "WindowsBase", "System", "Windows", "Data", "DataSourceProvider.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Controls\RichTextBox.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Documents\FlowDocument.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Markup\Baml2006\Baml2006Reader.cs");
        AssertCompileInclude(presentationFrameworkProject, @"System\Windows\Markup\XamlReader.cs");
        AssertProjectReference(presentationFrameworkProject, @"System.Xaml\System.Xaml.csproj");
        AssertProjectReference(presentationFrameworkProject, @"PresentationCore\PresentationCore.csproj");
        AssertProjectReference(presentationFrameworkProject, @"WindowsBase\WindowsBase.csproj");
        AssertProjectReference(presentationFrameworkProject, @"ReachFramework\ReachFramework.csproj");

        Assert.Equal("true", Assert.Single(fluentThemeProject.Descendants("InternalMarkupCompilation")).Value);
        var pageItem = Assert.Single(
            fluentThemeProject.Descendants("Page"),
            item => string.Equals(item.Attribute("Include")?.Value, @"**\*.xaml", StringComparison.Ordinal));
        Assert.Equal("MSBuild:Compile", pageItem.Element("Generator")?.Value);
        AssertProjectReference(fluentThemeProject, @"System.Xaml\System.Xaml.csproj");
        AssertProjectReference(fluentThemeProject, @"PresentationCore\PresentationCore.csproj");
        AssertProjectReference(fluentThemeProject, @"PresentationFramework\PresentationFramework.csproj");
        AssertProjectReference(fluentThemeProject, @"Themes\PresentationFramework.Fluent\ref\PresentationFramework.Fluent-ref.csproj");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "Themes", "PresentationFramework.Fluent", "Themes", "Fluent.xaml");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "Themes", "PresentationFramework.Fluent", "Themes", "Fluent.Light.xaml");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "Themes", "PresentationFramework.Fluent", "Styles", "Button.xaml");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "Themes", "PresentationFramework.Fluent", "Styles", "RichTextBox.xaml");
        AssertSourceFileExists("src", "Microsoft.DotNet.Wpf", "src", "Themes", "PresentationFramework.Fluent", "Styles", "Window.xaml");

        AssertProjectReference(realPresentationCoreHarnessProject, @"ProGPU.Wpf\ProGPU.Wpf.csproj");
        var realPresentationCoreReference = AssertProjectReference(
            realPresentationCoreHarnessProject,
            @"Microsoft.DotNet.Wpf\src\PresentationCore\PresentationCore.csproj");
        Assert.Equal("false", GetItemMetadata(realPresentationCoreReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(realPresentationCoreReference, "PrivateAssets"));
    }

    [Fact]
    public void RealPresentationFrameworkHarnessExercisesManagedFrameworkAndProGpuBridge()
    {
        var harnessProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealPresentationFrameworkHarness",
            "ProGPU.Wpf.RealPresentationFrameworkHarness.csproj");
        var harnessProgramPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealPresentationFrameworkHarness",
            "Program.cs");

        var harnessProject = XDocument.Load(harnessProjectPath);
        var harnessProgram = File.ReadAllText(harnessProgramPath);

        AssertPackageReference(harnessProject, "System.Configuration.ConfigurationManager");
        AssertPackageReference(harnessProject, "System.Formats.Nrbf");
        AssertPackageReference(harnessProject, "$(SystemIOPackagingPackage)");
        AssertPackageReference(harnessProject, "System.Windows.Extensions");

        AssertProjectReference(harnessProject, @"ProGPU.Wpf\ProGPU.Wpf.csproj");
        AssertProjectReference(harnessProject, @"external\ProGPU\src\ProGPU.Backend\ProGPU.Backend.csproj");
        AssertProjectReference(harnessProject, @"external\ProGPU\src\ProGPU.Scene\ProGPU.Scene.csproj");
        AssertProjectReference(harnessProject, @"external\ProGPU\src\ProGPU.Vector\ProGPU.Vector.csproj");

        var presentationCoreReference = AssertProjectReference(
            harnessProject,
            @"Microsoft.DotNet.Wpf\src\PresentationCore\PresentationCore.csproj");
        Assert.Equal("false", GetItemMetadata(presentationCoreReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(presentationCoreReference, "PrivateAssets"));

        var presentationFrameworkReference = AssertProjectReference(
            harnessProject,
            @"Microsoft.DotNet.Wpf\src\PresentationFramework\PresentationFramework.csproj");
        Assert.Equal("false", GetItemMetadata(presentationFrameworkReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(presentationFrameworkReference, "PrivateAssets"));

        Assert.Contains("WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("WpfRenderDataSinkProviderBridge.TryRegisterRenderDataSinkProvider", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuWpfCompositionTarget.CreateHeadless()", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.TextBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.RichTextBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.FlowDocument", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ControlTemplate", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.DrawingBrush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.DrawingVisual", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.GeometryDrawing", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.LinearGradientBrush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.RadialGradientBrush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.FormattedText", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.GlyphRun", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.ImageBrush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.Imaging.BitmapSource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("WpfBitmapSourceImageAdapter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("VerifyPortableSpellerFallback(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertEqual(\"NullSpellerInterop\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("RecordPortableSpellerSegment", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable speller segment count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawLine\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawRoundedRectangle\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawEllipse\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawGeometry\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawDrawing\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawGlyphRun\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawText\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawImage\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"PushOpacity\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"PushClip\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"PushTransform\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"PushOpacityMask\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"PushGuidelineSet\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("VerifyRetainedDrawingVisualBranch(target)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.DrawLine", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.DrawRoundedRect", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.DrawEllipse", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.DrawPath", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.DrawGlyphRun", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.DrawTexture", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.PushOpacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.PushGeometryClip", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.PushOpacityMask", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained opacity mask", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained rounded rectangle radius X", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained guideline snapped rect X", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained drawing resource path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained glyph run", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained formatted text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained bitmap image", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained image brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained drawing brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained linear gradient brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained radial gradient brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained linear gradient start X", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained radial gradient origin X", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual transformed line X offset", harnessProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void RealXamlCompilerHarnessUsesWpfApplicationDefinitionAndPagePipeline()
    {
        var harnessProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "ProGPU.Wpf.RealXamlCompilerHarness.csproj");
        var appXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "App.xaml");
        var smokeResourcesXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokeResources.xaml");
        var mainWindowXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "MainWindow.xaml");
        var smokeUserControlXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokeUserControl.xaml");
        var smokePageXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokePage.xaml");
        var smokeSecondPageXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokeSecondPage.xaml");
        var appCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "App.xaml.cs");
        var mainWindowCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "MainWindow.xaml.cs");
        var smokeUserControlCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokeUserControl.xaml.cs");
        var smokePageCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokePage.xaml.cs");
        var smokeSecondPageCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlCompilerHarness",
            "SmokeSecondPage.xaml.cs");

        var harnessProject = XDocument.Load(harnessProjectPath);
        var appXaml = File.ReadAllText(appXamlPath);
        var smokeResourcesXaml = File.ReadAllText(smokeResourcesXamlPath);
        var mainWindowXaml = File.ReadAllText(mainWindowXamlPath);
        var smokeUserControlXaml = File.ReadAllText(smokeUserControlXamlPath);
        var smokePageXaml = File.ReadAllText(smokePageXamlPath);
        var smokeSecondPageXaml = File.ReadAllText(smokeSecondPageXamlPath);
        var appCodeBehind = File.ReadAllText(appCodeBehindPath);
        var mainWindowCodeBehind = File.ReadAllText(mainWindowCodeBehindPath);
        var smokeUserControlCodeBehind = File.ReadAllText(smokeUserControlCodeBehindPath);
        var smokePageCodeBehind = File.ReadAllText(smokePageCodeBehindPath);
        var smokeSecondPageCodeBehind = File.ReadAllText(smokeSecondPageCodeBehindPath);

        Assert.Equal("true", Assert.Single(harnessProject.Descendants("InternalMarkupCompilation")).Value);

        var applicationDefinition = Assert.Single(harnessProject.Descendants("ApplicationDefinition"));
        Assert.Equal("App.xaml", applicationDefinition.Attribute("Include")?.Value);
        Assert.Equal("MSBuild:Compile", applicationDefinition.Element("Generator")?.Value);

        var smokeResourcesPage = Assert.Single(
            harnessProject.Descendants("Page"),
            item => item.Attribute("Include")?.Value == "SmokeResources.xaml");
        Assert.Equal("MSBuild:Compile", smokeResourcesPage.Element("Generator")?.Value);

        var smokeUserControlPage = Assert.Single(
            harnessProject.Descendants("Page"),
            item => item.Attribute("Include")?.Value == "SmokeUserControl.xaml");
        Assert.Equal("MSBuild:Compile", smokeUserControlPage.Element("Generator")?.Value);

        var smokePage = Assert.Single(
            harnessProject.Descendants("Page"),
            item => item.Attribute("Include")?.Value == "SmokePage.xaml");
        Assert.Equal("MSBuild:Compile", smokePage.Element("Generator")?.Value);

        var smokeSecondPage = Assert.Single(
            harnessProject.Descendants("Page"),
            item => item.Attribute("Include")?.Value == "SmokeSecondPage.xaml");
        Assert.Equal("MSBuild:Compile", smokeSecondPage.Element("Generator")?.Value);

        var page = Assert.Single(
            harnessProject.Descendants("Page"),
            item => item.Attribute("Include")?.Value == "MainWindow.xaml");
        Assert.Equal("MainWindow.xaml", page.Attribute("Include")?.Value);
        Assert.Equal("MSBuild:Compile", page.Element("Generator")?.Value);

        AssertCompileInclude(harnessProject, "App.xaml.cs");
        AssertCompileInclude(harnessProject, "MainWindow.xaml.cs");
        AssertCompileInclude(harnessProject, "SmokePage.xaml.cs");
        AssertCompileInclude(harnessProject, "SmokeSecondPage.xaml.cs");
        AssertCompileInclude(harnessProject, "SmokeUserControl.xaml.cs");
        AssertProjectReference(harnessProject, @"Microsoft.DotNet.Wpf\src\System.Xaml\System.Xaml.csproj");
        AssertProjectReference(harnessProject, @"Microsoft.DotNet.Wpf\src\WindowsBase\WindowsBase.csproj");
        AssertProjectReference(harnessProject, @"Microsoft.DotNet.Wpf\src\PresentationCore\PresentationCore.csproj");
        AssertProjectReference(harnessProject, @"Microsoft.DotNet.Wpf\src\PresentationFramework\PresentationFramework.csproj");

        Assert.DoesNotContain(
            harnessProject.Descendants("ProjectReference"),
            item => IncludeEndsWith(item, "Include", @"ProGPU.Wpf\ProGPU.Wpf.csproj"));
        Assert.DoesNotContain(
            harnessProject.Descendants("ProjectReference"),
            item => IncludeEndsWith(item, "Include", @"external\ProGPU\src\ProGPU.Scene\ProGPU.Scene.csproj"));

        Assert.Contains("x:Class=\"ProGPU.Wpf.RealXamlCompilerHarness.App\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("StartupUri=\"MainWindow.xaml\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ResourceDictionary.MergedDictionaries", appXaml, StringComparison.Ordinal);
        Assert.Contains("ResourceDictionary Source=\"SmokeResources.xaml\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"AccentBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ComponentResourceKey TypeInTargetAssembly={x:Type local:MainWindow}, ResourceId=SmokeComponentAccentBrush", appXaml, StringComparison.Ordinal);
        Assert.Contains("Color=\"#2F6B54\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"ReplacementAccentBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"UnsharedAccentBrush\" x:Shared=\"False\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"FreezableAccentBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Color=\"#B15E3B\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("LinearGradientBrush", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FreezableGradientBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("SpreadMethod=\"Reflect\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("GradientStop Color=\"#FF2F6B54\" Offset=\"0\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("GradientStop Color=\"#FFB15E3B\" Offset=\"0.5\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ControlTemplate x:Key=\"SmokeButtonTemplate\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ControlTemplate.Resources", appXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"TemplateBorderBrush\" Color=\"#6B4E9B\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource AccentBrush}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{StaticResource TemplateBorderBrush}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"2\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{TemplateBinding Content}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("VisualStateManager.VisualStateGroups", appXaml, StringComparison.Ordinal);
        Assert.Contains("VisualStateGroup x:Name=\"CommonStates\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("VisualState x:Name=\"Pressed\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.73\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ControlTemplate.Triggers", appXaml, StringComparison.Ordinal);
        Assert.Contains("Trigger Property=\"IsEnabled\" Value=\"False\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter TargetName=\"TemplateBorder\" Property=\"Opacity\" Value=\"0.42\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"SmokeTextBoxStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"BasedOnTextBoxStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource SmokeTextBoxStyle}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"based on text box style\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"TriggeredButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style.Triggers", appXaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding IsWarning}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"trigger active\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"PropertyTriggeredButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"property trigger active\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"MultiPropertyTriggeredButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("<MultiTrigger>", appXaml, StringComparison.Ordinal);
        Assert.Contains("Condition Property=\"IsDefault\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"multi property trigger active\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"TriggerActionButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Trigger.EnterActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("Trigger.ExitActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetProperty=\"Opacity\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.41\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"DataTriggerActionButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding IsTriggerActionActive}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger.EnterActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger.ExitActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.52\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"MultiDataTriggerActionButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Condition Binding=\"{Binding IsMultiTriggerActionReady}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Condition Binding=\"{Binding IsMultiTriggerActionArmed}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("MultiDataTrigger.EnterActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("MultiDataTrigger.ExitActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.63\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"MultiTriggerActionButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("MultiTrigger.EnterActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("MultiTrigger.ExitActions", appXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.74\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"MultiTriggeredButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("MultiDataTrigger", appXaml, StringComparison.Ordinal);
        Assert.Contains("Condition Binding=\"{Binding IsWarning}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Condition Binding=\"{Binding IsCritical}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"multi trigger active\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("public partial class App : Application", appCodeBehind, StringComparison.Ordinal);

        Assert.Contains("ResourceDictionary", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"MergedAccentBrush\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("Thickness x:Key=\"MergedBlockMargin\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("Style TargetType=\"{x:Type CheckBox}\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"implicit merged style\"", smokeResourcesXaml, StringComparison.Ordinal);

        Assert.Contains("x:Class=\"ProGPU.Wpf.RealXamlCompilerHarness.MainWindow\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeMenu\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FileMenuItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_File\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MenuCommandItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"menu command payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandTarget=\"{Binding ElementName=SmokeMenu}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Run _Command\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Separator />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MenuClickItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnMenuClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Click\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MenuCheckableItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsCheckable=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Checked=\"OnMenuCheckableChecked\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Unchecked=\"OnMenuCheckableUnchecked\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Checkable\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeToolBarTray\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ToolBar x:Name=\"SmokeToolBar\" Header=\"Smoke tools\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToolBarCommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"toolbar command payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Run toolbar\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Separator x:Name=\"ToolBarSeparator\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ToggleButton", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToolBarToggle\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Toggle toolbar\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeStatusBar\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StatusReadyItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Ready\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StatusTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"status text\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RangeValueSlider\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("TickFrequency=\"25\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding RangeValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RangeValueProgress\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"12\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding RangeValue}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContextMenuButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Button.ContextMenu", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ContextMenu x:Name=\"ContextButtonMenu\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContextCommandItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"context menu command payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Run Context _Command\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContextClickItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnContextMenuClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Context _Click\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Button.ToolTip", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContextButtonToolTip\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Placement=\"Right\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContextButtonToolTipText\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled ToolTip content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("TextBox", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CredentialBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("MaxLength=\"12\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PasswordChanged=\"OnPasswordChanged\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PasswordChar=\"#\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RichTextBox", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BasedOnTextBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource BasedOnTextBoxStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled BasedOn TextBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FlowDocument", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompiledDocument\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"IntroParagraph\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Bold>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Italic>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Underline>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Span x:Name=\"DocumentSpan\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<LineBreak />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("after line break", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentLink\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("NavigateUri=\"https://example.test/progpu-wpf\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RequestNavigate=\"OnDocumentLinkRequestNavigate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("LoadCompleted=\"OnSourceNavigationFrameLoadCompleted\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Navigated=\"OnSourceNavigationFrameNavigated\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Navigating=\"OnSourceNavigationFrameNavigating\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Figure x:Name=\"DocumentFigure\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("figure anchored text", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Floater x:Name=\"DocumentFloater\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("floater anchored text", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("InlineUIContainer x:Name=\"InlineActionContainer\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentInlineButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"inline document button\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Section x:Name=\"DocumentSection\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("section block text", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("BlockUIContainer x:Name=\"DocumentBlockContainer\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentBlockButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"block document button\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Table", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentTable\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Table.Columns", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("table alpha", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("table beta", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<List MarkerStyle=\"Decimal\">", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("first document item", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("second document item", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Greeting}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PriorityBindingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<PriorityBinding>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Binding Path=\"MissingPriorityText\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiBindingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<MultiBinding StringFormat=\"{}{0} / {1}\">", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Binding Path=\"Greeting\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Binding Path=\"ButtonText\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeUpperConverter", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeUpperConverter\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeJoinConverter", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeJoinConverter\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConvertedBindingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Converter={StaticResource SmokeUpperConverter}", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=converted", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConvertedMultiBindingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Converter=\"{StaticResource SmokeJoinConverter}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=\"converted-multi\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AncestorBindingBorder\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"ancestor binding source\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RelativeSourceBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RelativeSource={RelativeSource AncestorType={x:Type Border}}, Path=Tag", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingTransferBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SourceUpdated=\"OnBindingTransferSourceUpdated\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("TargetUpdated=\"OnBindingTransferTargetUpdated\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("BindingTransferText, Mode=TwoWay, UpdateSourceTrigger=Explicit, NotifyOnSourceUpdated=True, NotifyOnTargetUpdated=True", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ValidatedBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Validation.Error=\"OnValidatedBoxValidationError\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ValidatedText, UpdateSourceTrigger=Explicit, ValidatesOnDataErrors=True, NotifyOnValidationError=True", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RuleValidatedBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Validation.Error=\"OnRuleValidatedBoxValidationError\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Path=\"RuleValidatedText\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Binding.ValidationRules", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokePrefixValidationRule RequiredPrefix=\"rule:\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingGroupPanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StackPanel.BindingGroup", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("BindingGroup Name=\"SmokeBindingGroup\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("BindingGroup.ValidationRules", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeBindingGroupValidationRule", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FirstProperty=\"BindingGroupFirstName\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RequiredPrefix=\"group:\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SecondProperty=\"BindingGroupLastName\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingGroupFirstBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding BindingGroupFirstName, UpdateSourceTrigger=Explicit}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BindingGroupLastBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding BindingGroupLastName, UpdateSourceTrigger=Explicit}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProviderGreetingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Source={StaticResource ProviderGreeting}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"XmlProviderBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Source={StaticResource ProviderXml}, XPath=@Text}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StoryboardTargetBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Loaded=\"OnStoryboardTargetLoaded\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("TextBlock.Triggers", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("EventTrigger RoutedEvent=\"FrameworkElement.Loaded\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("BeginStoryboard", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetName=\"StoryboardTargetBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetProperty=\"Opacity\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DoubleAnimation", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.37\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Duration=\"0:0:0\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FillBehavior=\"HoldEnd\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StoryboardTriggerButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Button.Triggers", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RoutedEvent=\"Button.Click\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetProperty=\"Opacity\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.64\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MarkupExtensionBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{local:SmokeText Prefix=compiled, Value=markup}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StackPanel.Resources", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"ScopedAccentBrush\" Color=\"#6B4E9B\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Thickness x:Key=\"ScopedBlockMargin\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MergedResourceBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource MergedAccentBrush}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"{StaticResource MergedBlockMargin}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScopedResourceBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource ScopedAccentBrush}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"{StaticResource ScopedBlockMargin}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled scoped resource\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ComponentResourceBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource {ComponentResourceKey TypeInTargetAssembly={x:Type local:MainWindow}, ResourceId=SmokeComponentAccentBrush}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled component resource\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UnsharedResourceBorderA\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UnsharedResourceBorderB\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{StaticResource UnsharedAccentBrush}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeUserControl", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NestedControl\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AttachedLayoutGrid\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.RowDefinitions", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.ColumnDefinitions", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GridFirstCell\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LayoutPanelSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DockPanelSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("LastChildFill=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DockPanel.Dock=\"Left\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DockPanel.Dock=\"Right\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CanvasSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Canvas.Left=\"12\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Canvas.Top=\"6\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WrapPanelSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemWidth=\"64\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GridSplitterGrid\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GridSplitterSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ResizeBehavior=\"PreviousAndNext\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ResizeDirection=\"Columns\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ShowsPreview=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScrollingSmokePanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScrollViewerSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Visible\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CanContentScroll=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScrollViewerSixthItem\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VerticalScrollBarSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ViewportSize=\"2\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DateSelectionSmokePanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CalendarSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedDate=\"2026-06-17\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"SingleDate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DatePickerSmoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"160\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedDate=\"2026-06-18\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedDateFormat=\"Short\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImplicitStyleCheckBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToggleChoicePanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToggleChoiceCheckBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Checked=\"OnToggleChoiceChecked\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Unchecked=\"OnToggleChoiceUnchecked\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"toggle choice\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RadioChoiceAlpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RadioChoiceBeta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GroupName=\"SmokeChoiceGroup\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Checked=\"OnChoiceRadioChecked\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Unchecked=\"OnChoiceRadioUnchecked\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"choice alpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"choice beta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EventButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnXamlClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GotMouseCapture=\"OnXamlGotMouseCapture\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("LostMouseCapture=\"OnXamlLostMouseCapture\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("MouseWheel=\"OnXamlMouseWheel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeRootPanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("primitives:Thumb.DragDelta=\"OnBubbledThumbDragDelta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RepeatActionButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnRepeatButtonClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Delay=\"250\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Interval=\"75\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("primitives:Thumb", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DragManagerThumb\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DragStarted=\"OnDragManagerThumbDragStarted\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DragDelta=\"OnDragManagerThumbDragDelta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DragCompleted=\"OnDragManagerThumbDragCompleted\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"drag manager thumb\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StyledEventButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource EventSetterButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SmokeCommand}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CanExecuteCommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ToggleCommand}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"can execute payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RequeryCommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RequeryCommand}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"requery payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:componentModel=\"clr-namespace:System.ComponentModel;assembly=WindowsBase\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:local=\"clr-namespace:ProGPU.Wpf.RealXamlCompilerHarness\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:primitives=\"clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SortedItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.SortDescriptions", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("componentModel:SortDescription", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Direction=\"Descending\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PropertyName=\"Name\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"LiveSortedItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsLiveSortingRequested=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.LiveSortingProperties", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<sys:String>Name</sys:String>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FilteredItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Filter=\"OnFilteredItemsViewFilter\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"LiveFilteredItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsLiveFilteringRequested=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.LiveFilteringProperties", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"GroupedItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.GroupDescriptions", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PropertyGroupDescription PropertyName=\"Category\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"LiveGroupedItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsLiveGroupingRequested=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.LiveGroupingProperties", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<sys:String>Category</sys:String>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CurrencyItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataTemplate DataType=\"{x:Type local:SmokeDetail}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImplicitDetailTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"implicit data template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Title}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SelectedDetailTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FallbackDetailTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectedDetailTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"content template selector selected\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"content template selector fallback\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeDetailTemplateSelector", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeDetailTemplateSelector\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedTemplate=\"{StaticResource SelectedDetailTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FallbackTemplate=\"{StaticResource FallbackDetailTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExpanderHeaderTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExpanderHeaderTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"expander header template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"GroupBoxHeaderTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupBoxHeaderTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"group box header template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("HierarchicalDataTemplate", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeNodeTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataType=\"{x:Type local:SmokeNode}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Children}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NodeTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"hierarchical template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"AlphaItemTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DefaultItemTemplate\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectorTemplateTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"selector alpha template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"selector default template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeItemTemplateSelector", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeItemTemplateSelector\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("AlphaTemplate=\"{StaticResource AlphaItemTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DefaultTemplate=\"{StaticResource DefaultItemTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"AlphaItemContainerSelectorStyle\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DefaultItemContainerSelectorStyle\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeItemContainerStyleSelector", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeItemContainerStyleSelector\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("AlphaStyle=\"{StaticResource AlphaItemContainerSelectorStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DefaultStyle=\"{StaticResource DefaultItemContainerSelectorStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ObjectDataProvider", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ProviderGreeting\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsAsynchronous=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("MethodName=\"CreateProviderGreeting\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ObjectType=\"{x:Type local:ProviderDataFactory}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ObjectDataProvider.MethodParameters", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<sys:String>provider</sys:String>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<sys:String>7</sys:String>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("XmlDataProvider", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ProviderXml\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsAsynchronous=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("XPath=\"/Smoke/Message\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:XData", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Message Text=\"xml provider text\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"EventSetterButtonStyle\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"event setter style\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("EventSetter Event=\"Click\" Handler=\"OnStyledButtonClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Window.CommandBindings", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Static local:MainWindow.SmokeRoutedCommand}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CanExecute=\"OnSmokeCommandCanExecute\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Executed=\"OnSmokeCommandExecuted\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Window.InputBindings", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<KeyBinding", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"input binding payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"F6\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Modifiers=\"Control\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RoutedCommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandTarget=\"{Binding ElementName=InputBox}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClassCommandTargetBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClassCommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Static local:MainWindow.SmokeClassRoutedCommand}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"class command payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandTarget=\"{Binding ElementName=ClassCommandTargetBox}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TemplatedButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Template=\"{StaticResource SmokeButtonTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TriggeredButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding TriggerButtonText}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource TriggeredButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PropertyTriggeredButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource PropertyTriggeredButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiPropertyTriggeredButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MultiPropertyTriggeredButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TriggerActionButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource TriggerActionButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DataTriggerActionButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource DataTriggerActionButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiDataTriggerActionButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MultiDataTriggerActionButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiTriggerActionButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MultiTriggerActionButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiTriggeredButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MultiTriggeredButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Items}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedItem, Mode=TwoWay}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SortedItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource SortedItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveSortedItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource LiveSortedItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FilteredItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource FilteredItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveFilteredItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource LiveFilteredItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupedItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource GroupedItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ListBox.GroupStyle", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GroupStyle.HeaderTemplate", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupHeaderTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"group header template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveGroupedItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource LiveGroupedItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveGroupHeaderTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"live group header template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CurrencyItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsSynchronizedWithCurrentItem=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource CurrencyItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SmokeCompositeItemsProvider.Items", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompositeItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<CompositeCollection>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionContainer Collection=\"{x:Static local:SmokeCompositeItemsProvider.Items}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("composite header", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("composite footer", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AlternationItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("AlternationCount=\"2\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StringFormatItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemStringFormat=\"formatted {0}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Labels}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DisplayMemberItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberPath=\"Name\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValuePath=\"Category\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding SelectedCategory, Mode=TwoWay}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ItemsComboBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding ComboSelectedCategory, Mode=TwoWay}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectorEventPanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectionEventListBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"OnSelectionEventListBoxChanged\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"selection alpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"selection beta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectionEventComboBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"OnSelectionEventComboBoxChanged\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"combo alpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"combo beta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiSelectionEventListBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"Multiple\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"OnMultiSelectionEventListBoxChanged\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"multi alpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"multi beta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"multi gamma\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GridItemsListView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<ListView.View>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GridView AllowsColumnReorder=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Name\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberBinding=\"{Binding Name}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Category\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberBinding=\"{Binding Category}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ItemsDataGrid\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("AutoGenerateColumns=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CanUserAddRows=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ClipboardCopyMode=\"IncludeHeader\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CopyingRowClipboardContent=\"OnItemsDataGridCopyingRowClipboardContent\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GridLinesVisibility=\"Horizontal\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("HeadersVisibility=\"Column\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedItem, Mode=TwoWay}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataGrid.Columns", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataGridTextColumn", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataGridCheckBoxColumn", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ClipboardContentBinding=\"{Binding Name}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ClipboardContentBinding=\"{Binding Category}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ClipboardContentBinding=\"{Binding IsActive}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IsActive}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Active\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NodeTree\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemTemplate=\"{StaticResource SmokeNodeTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Nodes}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExplicitTree\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExplicitTreeAlpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"explicit alpha\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Expanded=\"OnExplicitTreeExpanded\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Collapsed=\"OnExplicitTreeCollapsed\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Selected=\"OnExplicitTreeSelected\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Unselected=\"OnExplicitTreeUnselected\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExplicitTreeAlphaChild\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"explicit alpha child\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExplicitTreeBeta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"explicit beta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectorItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemTemplateSelector=\"{StaticResource SmokeItemTemplateSelector}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StyleSelectorItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyleSelector=\"{StaticResource SmokeItemContainerStyleSelector}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StyleSelectorItemTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"style selector item template\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImplicitTemplateHost\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding Detail}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectorTemplateHost\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ContentTemplateSelector=\"{StaticResource SmokeDetailTemplateSelector}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeTabControl\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex=\"1\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<TabItem Header=\"alpha tab\">", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AlphaTabContent\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"tab alpha content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"alpha tab content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<TabItem Header=\"beta tab\">", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BetaTabContent\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"tab beta content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"beta tab content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeExpander\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding Detail}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("HeaderTemplate=\"{StaticResource ExpanderHeaderTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExpanderContentText\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"expander content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Greeting}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeGroupBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("HeaderTemplate=\"{StaticResource GroupBoxHeaderTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupBoxContentText\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"group box content\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ButtonText}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeAdornerDecorator\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AdornedButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"adorned button\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DependencyPropertyScopePanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeDependencyProperties.InheritedLabel=\"inherited smoke\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeDependencyPropertyControl", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DependencyPropertyTarget\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CoercedLevel=\"15\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeDependencyPropertyOwnerControl", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DependencyPropertyOwnerTarget\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("OwnerLevel=\"35\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeOverrideMetadataControl", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DependencyPropertyMetadataTarget\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CustomRoutedEventScopePanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeRoutedEventSource.SmokeBubbled=\"OnCustomRoutedEventScope\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeRoutedEventSource", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CustomRoutedEventSource\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SmokeBubbled=\"OnCustomRoutedEventSource\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClassRoutedEventScopePanel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeClassRoutedEventSource.SmokeClassBubbled=\"OnClassRoutedEventScope\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeClassRoutedEventSource", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClassRoutedEventSource\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SmokeClassBubbled=\"OnClassRoutedEventSource\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccessKeyFocusScope\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FocusManager.IsFocusScope=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FocusManager.FocusedElement=\"{Binding ElementName=AccessTargetBox}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AlternateAccessTargetBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccessTargetLabel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"_Access target\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"{Binding ElementName=AccessTargetBox}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccessTargetBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StandaloneAccessText\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"_Standalone access text\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MouseBindingSurface\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<TextBlock.InputBindings>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<MouseBinding", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"mouse binding payload\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("MouseAction=\"RightClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceNavigationFrame\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("NavigationUIVisibility=\"Hidden\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"SmokePage.xaml\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Class=\"ProGPU.Wpf.RealXamlCompilerHarness.SmokePage\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"compiled source page\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceNavigationPagePanel\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceNavigationPageText\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled source page content\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceNavigationPageButton\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnPageButtonClick\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"compiled page button\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Class=\"ProGPU.Wpf.RealXamlCompilerHarness.SmokeSecondPage\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"compiled second page\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceNavigationSecondPagePanel\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceNavigationSecondPageText\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled second page content\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("ListBox.ItemContainerStyle", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style TargetType=\"{x:Type ListBoxItem}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"container trigger inactive\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"container trigger active\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ListBox.ItemsPanel", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsPanelTemplate", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataTemplate DataType=\"{x:Type local:SmokeItem}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ItemTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Name}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataTemplate.Triggers", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding Name}\" Value=\"item beta\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Setter TargetName=\"ItemTextBlock\" Property=\"Tag\" Value=\"template trigger active\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StaticResource AccentBrush", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DataContext = new SmokeViewModel();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public static RoutedUICommand SmokeRoutedCommand", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public static RoutedUICommand SmokeClassRoutedCommand", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeClassCommandTextBox : TextBox", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CommandManager.RegisterClassCommandBinding", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CommandManager.RegisterClassInputBinding", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Key.F7, ModifierKeys.Control", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CommandParameter = \"class input payload\"", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new CommandBinding(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("OnSmokeClassCommandCanExecute", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("OnSmokeClassCommandExecuted", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class ProviderDataFactory", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string CreateProviderGreeting", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{prefix} data {value}\";", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeTextExtension : MarkupExtension", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public override object ProvideValue(IServiceProvider serviceProvider)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{Prefix} {Value} extension\";", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeAdorner : Adorner", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("protected override void OnRender(DrawingContext drawingContext)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("drawingContext.DrawRectangle(null, new Pen(Brushes.LimeGreen, 1.0), adornedBounds)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public static class SmokeDependencyProperties", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyProperty.RegisterAttached(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("FrameworkPropertyMetadataOptions.Inherits", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeDependencyPropertyControl : Control", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyProperty.Register(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private static object CoerceLevel(DependencyObject dependencyObject, object baseValue)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("control.CoercedLevelChangedCount++", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeDependencyPropertyOwnerControl : Control", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SmokeDependencyPropertyControl.CoercedLevelProperty.AddOwner(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int OwnerLevel", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyPropertyHelper.GetValueSource(this, OwnerLevelProperty).BaseValueSource.ToString()", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyPropertyHelper.GetValueSource(this, OwnerLevelProperty).IsCoerced", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyPropertyHelper.GetValueSource(this, OwnerLevelProperty).IsCurrent", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ClearValue(OwnerLevelProperty)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SetCurrentValue(OwnerLevelProperty, value)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public class SmokeMetadataBaseControl : Control", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ModeLabelProperty.OverrideMetadata(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeOverrideMetadataControl : SmokeMetadataBaseControl", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public delegate void SmokeRoutedEventHandler(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeRoutedEventArgs : RoutedEventArgs", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string Payload { get; }", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeRoutedEventSource : Control", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("EventManager.RegisterRoutedEvent(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RoutingStrategy.Bubble", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public event SmokeRoutedEventHandler SmokeBubbled", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("AddHandler(SmokeBubbledEvent, value)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RemoveHandler(SmokeBubbledEvent, value)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeRoutedEventArgs RaiseSmokeBubbled(string payload)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RaiseEvent(args)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int CustomRoutedEventSourceCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int CustomRoutedEventScopeCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnCustomRoutedEventSource(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnCustomRoutedEventScope(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastCustomRoutedEventScopeOriginalSourceName = DescribeElementName(e.OriginalSource)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastCustomRoutedEventScopePayload = e.Payload", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ClassRoutedEventScopePanel.AddHandler(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("handledEventsToo: true", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnClassRoutedEventSource(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnClassRoutedEventScope(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnClassRoutedEventScopeHandledToo(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeClassRoutedEventSource : Control", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public static readonly RoutedEvent SmokeClassBubbledEvent = EventManager.RegisterRoutedEvent(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("EventManager.RegisterClassHandler(", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new SmokeRoutedEventHandler(OnSmokeClassBubbledClassHandler)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public event SmokeRoutedEventHandler SmokeClassBubbled", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ClassHandlerCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeRoutedEventArgs RaiseSmokeClassBubbled(string payload)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private static void OnSmokeClassBubbledClassHandler(object sender, SmokeRoutedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeCommandCanExecute", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeCommandExecuted", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RoutedCommandExecutionCount++", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int XamlClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnXamlClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastXamlClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int XamlGotMouseCaptureCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnXamlGotMouseCapture", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastXamlGotMouseCaptureRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int XamlLostMouseCaptureCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnXamlLostMouseCapture", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastXamlLostMouseCaptureRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int XamlMouseWheelCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnXamlMouseWheel", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastXamlMouseWheelDelta = e.Delta", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastXamlMouseWheelRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int RepeatButtonClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnRepeatButtonClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastRepeatButtonClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("using System.Windows.Controls.Primitives;", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("using System.Windows.Navigation;", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ThumbDragStartedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ThumbDragDeltaCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ThumbDragCompletedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int BubbledThumbDragDeltaCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDragManagerThumbDragStarted(object sender, DragStartedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDragManagerThumbDragDelta(object sender, DragDeltaEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDragManagerThumbDragCompleted(object sender, DragCompletedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnBubbledThumbDragDelta(object sender, DragDeltaEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastBubbledThumbDragDeltaOriginalSourceName = e.OriginalSource is FrameworkElement source ? source.Name : null", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int DocumentLinkRequestNavigateCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDocumentLinkRequestNavigate(object sender, RequestNavigateEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastDocumentLinkRequestNavigateSenderName = sender switch", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastDocumentLinkRequestNavigateUri = e.Uri?.ToString()", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastDocumentLinkRequestNavigateRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int FrameNavigatingCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int FrameNavigatedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int FrameLoadCompletedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSourceNavigationFrameNavigating(object sender, NavigatingCancelEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSourceNavigationFrameNavigated(object sender, NavigationEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSourceNavigationFrameLoadCompleted(object sender, NavigationEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastFrameNavigatingNavigationMode = e.NavigationMode.ToString()", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastFrameNavigatedContentType = e.Content?.GetType().FullName", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastFrameLoadCompletedContentType = e.Content?.GetType().FullName", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int StyledClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnStyledButtonClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastStyledClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int MenuClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnMenuClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastMenuClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int MenuCheckableCheckedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnMenuCheckableChecked", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int MenuCheckableUncheckedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnMenuCheckableUnchecked", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ContextMenuClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnContextMenuClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastContextMenuClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int PasswordChangedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnPasswordChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastPasswordChangedRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ToggleChoiceCheckedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnToggleChoiceChecked", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnToggleChoiceUnchecked", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ChoiceRadioCheckedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnChoiceRadioChecked", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnChoiceRadioUnchecked", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ExplicitTreeExpandedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnExplicitTreeExpanded", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnExplicitTreeCollapsed", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ExplicitTreeSelectedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnExplicitTreeSelected", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnExplicitTreeUnselected", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ListBoxSelectionChangedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSelectionEventListBoxChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int MultiListBoxSelectionChangedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnMultiSelectionEventListBoxChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ComboBoxSelectionChangedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSelectionEventComboBoxChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private static string? DescribeSelectionItem(IList items)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int BindingTransferTargetUpdatedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnBindingTransferTargetUpdated", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int BindingTransferSourceUpdatedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnBindingTransferSourceUpdated", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private static string? DescribeElementName", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ValidatedBoxValidationErrorCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnValidatedBoxValidationError", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int RuleValidatedBoxValidationErrorCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnRuleValidatedBoxValidationError", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastRuleValidatedBoxValidationErrorRuleName = e.Error.RuleInError?.GetType().Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int StoryboardTargetLoadedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnStoryboardTargetLoaded", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastStoryboardTargetLoadedRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int FilteredItemsFilterCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnFilteredItemsViewFilter", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("e.Accepted = e.Item is SmokeItem smokeItem", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("string.Equals(smokeItem.Name, \"item beta\", StringComparison.Ordinal)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeViewModel : INotifyPropertyChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("IDataErrorInfo", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ObservableCollection<SmokeItem> Items", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ObservableCollection<SmokeNode> Nodes", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeDetail Detail", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string TriggerButtonText", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string ValidatedText", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string RuleValidatedText", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string BindingTransferText", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string BindingGroupFirstName", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string BindingGroupLastName", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ValidatedText is required", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsWarning", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsCritical", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsTriggerActionActive", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsMultiTriggerActionReady", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsMultiTriggerActionArmed", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeItem? SelectedItem", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string SelectedCategory", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private string _selectedCategory = \"secondary group\"", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string ComboSelectedCategory", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private string _comboSelectedCategory = \"secondary group\"", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public double RangeValue", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private double _rangeValue = 42.0", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeDetailTemplateSelector : DataTemplateSelector", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public DataTemplate? SelectedTemplate", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public override DataTemplate? SelectTemplate", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeItemContainerStyleSelector : StyleSelector", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public Style? AlphaStyle", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public override Style? SelectStyle", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeItem : INotifyPropertyChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("PropertyChangedEventHandler? PropertyChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string Category", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsActive", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeDetail", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeNode", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public ObservableCollection<SmokeNode> Children", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeItemTemplateSelector : DataTemplateSelector", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public DataTemplate? AlphaTemplate", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public DataTemplate? DefaultTemplate", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public override DataTemplate? SelectTemplate", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("string.Equals(smokeItem.Name, \"item alpha\", StringComparison.Ordinal)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeUpperConverter : IValueConverter", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeJoinConverter : IMultiValueConverter", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{prefix}:{text.ToUpperInvariant()}\";", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{prefix}:{string.Join(\"|\", parts)}\";", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokePrefixValidationRule : ValidationRule", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string RequiredPrefix", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ValidationResult.ValidResult", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeBindingGroupValidationRule : ValidationRule", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("value is not BindingGroup bindingGroup", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("bindingGroup.GetValue(item, propertyName)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeCommand : ICommand", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeToggleCommand ToggleCommand { get; } = new();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeToggleCommand : ICommand", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool CanExecuteValue { get; private set; }", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public void SetCanExecute(bool value)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeRequeryCommand RequeryCommand { get; } = new();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeRequeryCommand : ICommand", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CommandManager.RequerySuggested += value", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CommandManager.RequerySuggested -= value", mainWindowCodeBehind, StringComparison.Ordinal);

        Assert.Contains("x:Class=\"ProGPU.Wpf.RealXamlCompilerHarness.SmokeUserControl\"", smokeUserControlXaml, StringComparison.Ordinal);
        Assert.Contains("UserControl.Resources", smokeUserControlXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"UserControlBrush\"", smokeUserControlXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ControlTitle\"", smokeUserControlXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ElementNameMirror\"", smokeUserControlXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ElementName=ControlTitle, Path=Text}\"", smokeUserControlXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ControlEventButton\"", smokeUserControlXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnControlButtonClick\"", smokeUserControlXaml, StringComparison.Ordinal);
        Assert.Contains("public partial class SmokeUserControl : UserControl", smokeUserControlCodeBehind, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();", smokeUserControlCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ControlClickCount", smokeUserControlCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnControlButtonClick", smokeUserControlCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public partial class SmokePage : Page", smokePageCodeBehind, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();", smokePageCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int PageClickCount", smokePageCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnPageButtonClick", smokePageCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastPageClickRoutedEventName = e.RoutedEvent?.Name", smokePageCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public partial class SmokeSecondPage : Page", smokeSecondPageCodeBehind, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();", smokeSecondPageCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void RealXamlRuntimeHarnessLoadsCompiledBamlThroughRealPresentationFramework()
    {
        var harnessProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlRuntimeHarness",
            "ProGPU.Wpf.RealXamlRuntimeHarness.csproj");
        var harnessProgramPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealXamlRuntimeHarness",
            "Program.cs");

        var harnessProject = XDocument.Load(harnessProjectPath);
        var harnessProgram = File.ReadAllText(harnessProgramPath);

        AssertPackageReference(harnessProject, "System.Configuration.ConfigurationManager");
        AssertPackageReference(harnessProject, "System.Formats.Nrbf");
        AssertPackageReference(harnessProject, "$(SystemIOPackagingPackage)");
        AssertPackageReference(harnessProject, "System.Windows.Extensions");
        AssertProjectReference(harnessProject, @"ProGPU.Wpf\ProGPU.Wpf.csproj");

        var compilerHarnessReference = AssertProjectReference(
            harnessProject,
            @"ProGPU.Wpf.RealXamlCompilerHarness\ProGPU.Wpf.RealXamlCompilerHarness.csproj");
        Assert.Equal("false", GetItemMetadata(compilerHarnessReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(compilerHarnessReference, "PrivateAssets"));

        var presentationCoreReference = AssertProjectReference(
            harnessProject,
            @"Microsoft.DotNet.Wpf\src\PresentationCore\PresentationCore.csproj");
        Assert.Equal("false", GetItemMetadata(presentationCoreReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(presentationCoreReference, "PrivateAssets"));

        var presentationFrameworkReference = AssertProjectReference(
            harnessProject,
            @"Microsoft.DotNet.Wpf\src\PresentationFramework\PresentationFramework.csproj");
        Assert.Equal("false", GetItemMetadata(presentationFrameworkReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(presentationFrameworkReference, "PrivateAssets"));

        var aero2Reference = AssertProjectReference(
            harnessProject,
            @"Microsoft.DotNet.Wpf\src\Themes\PresentationFramework.Aero2\PresentationFramework.Aero2.csproj");
        Assert.Equal("false", GetItemMetadata(aero2Reference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(aero2Reference, "PrivateAssets"));

        Assert.Contains("CompilerHarnessAssemblyName = \"ProGPU.Wpf.RealXamlCompilerHarness\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loadContext.LoadFromAssemblyPath(compilerHarnessPath)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(application, \"InitializeComponent\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSystemXamlNameScopeDictionary(systemXaml)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Xaml.NameScopeDictionary", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("standalone System.Xaml NameScopeDictionary", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("wrapped System.Xaml NameScopeDictionary external key stays out of dictionary view", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("wrapped System.Xaml NameScopeDictionary clear preserves external name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("wrapped System.Xaml NameScopeDictionary underlying registration", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("wrapped System.Xaml NameScopeDictionary clear unregisters underlying name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlReader(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterSystemResourceKeyRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableSystemParameters(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.SystemParameters", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable SystemParameters.{propertyName}", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Markup.XamlReader", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Markup.XamlWriter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ParseLooseXaml(presentationFramework, looseXaml)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("SaveLooseXaml(presentationFramework, brush)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader style StaticResource brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader RelativeSource binding text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader Binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized GradientStop", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip {description} stop color", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.MenuItem", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("SeparatorStyleKey", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized system resource key member", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip system-key style target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterStyleRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized style dictionary", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip derived BasedOn style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip styled Button inherited Tag", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterControlTemplateRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized ControlTemplate", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip ControlTemplate trigger setter target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip applied ControlTemplate content presenter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterDataTemplateRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized DataTemplate", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader DataTemplate category binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip DataTemplate trigger setter target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip DataTemplate category TextBlock name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterHierarchicalDataTemplateRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized HierarchicalDataTemplate", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip HierarchicalDataTemplate ItemsSource path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip HierarchicalDataTemplate trigger setter target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip HierarchicalDataTemplate count TextBlock name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterItemsPanelTemplateRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized ItemsPanelTemplate", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip ItemsPanelTemplate panel name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip ItemsPanelTemplate item width", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterGroupStyleRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized GroupStyle HeaderTemplate", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip GroupStyle HidesIfEmpty", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip GroupStyle header TextBlock name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip GroupStyle panel orientation", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterFrameworkElementRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized FrameworkElement Button", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip FrameworkElement children", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip Button background", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip TextBox MinWidth", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterFlowDocumentRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized FlowDocument root", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip FlowDocument paragraph name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip FlowDocument TextRange second list item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Create(compilerHarness, MainWindowTypeName)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(resources, \"AccentBrush\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateFreezableBrushResource(resources)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateFreezableGradientBrushResource(resources)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Freezable brush clone mutable opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Freezable gradient brush clone mutable stop offset", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Freezable gradient current-value clone stop collection", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Freezable current-value clone opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetProperty(resources, \"MergedDictionaries\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", \"MergedAccentBrush\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateScopedResourceLookup(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(rootResources, \"ScopedAccentBrush\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled child FindResource scoped brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled child TryFindResource application fallback", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.ResourceReferenceKeyNotFoundException", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(resources, \"SmokeTextBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(resources, \"BasedOnTextBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox BasedOn style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox BasedOn base style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox BasedOn inherited MinWidth", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"InputBox\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateTextBoxSelection(inputBox)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox selected text replacement", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePasswordBox(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PasswordBox PasswordChanged count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PasswordBox secure password length", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PasswordBox clear changed count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRuntimeNameScope(window, inputBox)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(window, \"RegisterName\", \"RuntimeRegisteredButton\", registeredButton)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled namescope runtime registered lookup", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled namescope duplicate preserves original", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled namescope runtime re-register after unregister", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRichFlowDocument(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"DocumentBox\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Bold", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Italic", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Underline", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Span", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.LineBreak", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Hyperlink", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("https://example.test/progpu-wpf", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(hyperlink, \"DoClick\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Hyperlink RequestNavigate handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Hyperlink RequestNavigate routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Figure", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertFlowDocumentAnchoredBlockText(figure, \"figure anchored text\", \"figure\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Floater", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertFlowDocumentAnchoredBlockText(floater, \"floater anchored text\", \"floater\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.List", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.TextRange", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange paragraph text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange italic text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange underline text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange span text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange line-break text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange figure text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange floater text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument list items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.InlineUIContainer", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument inline Button content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.BlockUIContainer", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument block Button content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox selection text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleBold\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleBold applied weight", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleBold restored weight", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleItalic\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleItalic applied style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleItalic restored style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleUnderline\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleUnderline applied decoration location", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleUnderline restored decoration count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyFontSize\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyFontSize value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox IncreaseFontSize value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DecreaseFontSize value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyFontFamily\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyFontFamily value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyForeground\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyForeground color", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyBackground\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyBackground color", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleSubscript\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleSubscript applied variant", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleSubscript restored variant", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleSuperscript\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleSuperscript applied variant", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleSuperscript restored variant", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyInlineFlowDirectionRTL\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyInlineFlowDirectionRTL value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyInlineFlowDirectionLTR\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyInlineFlowDirectionLTR value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"AlignCenter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox AlignCenter paragraph alignment", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox AlignRight paragraph alignment", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox AlignJustify paragraph alignment", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox AlignLeft paragraph alignment", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyParagraphFlowDirectionRTL\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyParagraphFlowDirectionRTL value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyParagraphFlowDirectionLTR\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyParagraphFlowDirectionLTR value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument section blocks", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument table columns", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument table cells", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange section text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange second table cell", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("SetStaticProperty(textEditorType, \"IsTableEditingEnabled\", true)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.TextEditorTables", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"InsertRows\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox InsertRows table rows", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox InsertRows copied cells", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"InsertColumns\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox InsertColumns first row cells", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox InsertColumns copied row cells", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"DeleteRows\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DeleteRows table rows", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DeleteRows preserved original row", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"DeleteColumns\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DeleteColumns table-cell selection", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DeleteColumns first row cells", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DeleteColumns preserved second cell", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"MergeCells\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox MergeCells column span", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"SplitCell\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox SplitCell copied second cell", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleBullets\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleBullets marker style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleNumbering\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleNumbering marker style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"IncreaseIndentation\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox IncreaseIndentation nested list", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"DecreaseIndentation\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DecreaseIndentation top-level list items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"RemoveListMarkers\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox RemoveListMarkers document blocks", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"BindingBlock\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"CommandButton\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateBindingAndCommand(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"CanExecuteCommandButton\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CanExecute command initial button state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CanExecute command enabled button state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CanExecute command button execution count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CanExecute command disabled button state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowCommandManagerRequery(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("() => FlushDispatcherOperations(activationServiceType, window, \"Background\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"RequeryCommandButton\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetProperty(dataContext, \"RequeryCommand\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Input.CommandManager", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("InvalidateRequerySuggested", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CommandManager RequerySuggested enabled button state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CommandManager RequerySuggested button execution count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CommandManager RequerySuggested disabled button state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBlock property-change binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button command binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateAdvancedBindingFeatures(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PriorityBinding fallback value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PriorityBinding child bindings", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PriorityBinding fallback path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetPriorityBindingExpression", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiBinding string-format value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled converter binding value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled converter binding resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled converter parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiBinding converter value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiBinding converter resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiBinding converter child bindings", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetMultiBindingExpression", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RelativeSource ancestor binding value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation error state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation restored error state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ValidationRule binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Binding ValidationRules", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled custom ValidationRule parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ValidationRule rejected source value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation Error added action", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation Error removed rule", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ValidationRule Error added content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ValidationRule Error removed action", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ValidationRule restored error state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateBindingTransferEvents(window, dataContext)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled binding transfer NotifyOnSourceUpdated", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Binding SourceUpdated routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Binding TargetUpdated target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateBindingGroup(window, dataContext, validationType)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled BindingGroup custom ValidationRule", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled BindingGroup rejected commit", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled BindingGroup accepted commit", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled BindingGroup accepted first source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateObjectDataProvider(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider method parameters", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider bound text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider binding source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateXmlDataProvider(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XmlDataProvider resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XmlDataProvider XPath", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XmlDataProvider synchronous flag", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XmlDataProvider XPath bound text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XmlDataProvider binding XPath", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateStoryboardEventTrigger(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Storyboard target TextBlock", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Storyboard target initial Loaded count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled EventTrigger routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled BeginStoryboard action", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Storyboard children", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DoubleAnimation target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DoubleAnimation fill behavior", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled click Storyboard trigger Button", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled click DoubleAnimation target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowLoadedEvent(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Storyboard target post-Loaded opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowClickStoryboardEventTrigger(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("FlushDispatcherOperations(activationServiceType, window, \"Render\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled click Storyboard trigger Button post-click opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowStyleTriggerActions(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style Trigger EnterActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style Trigger ExitActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowMultiTriggerActions(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style MultiTrigger EnterActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style MultiTrigger ExitActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button MultiTrigger action style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiTrigger action condition count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiTrigger EnterActions BeginStoryboard", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiTrigger ExitActions target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowDataTriggerActions(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("FlushDispatcherOperations(activationServiceType, window, \"DataBind\", \"Render\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style DataTrigger EnterActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style DataTrigger ExitActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowMultiDataTriggerActions(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style MultiDataTrigger partial-condition opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style MultiDataTrigger EnterActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style MultiDataTrigger ExitActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Storyboard target Loaded handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Storyboard target Loaded routed event name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMarkupExtension(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MarkupExtension TextBlock", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MarkupExtension provided text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMergedResourceDictionary(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled merged-resource foreground", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.ComponentResourceKey", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComponentResourceKey lookup key", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComponentResourceKey TextBlock", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComponentResourceKey foreground", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComponentResourceKey application lookup", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateUnsharedResource(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled x:Shared=false StaticResource consumers", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled x:Shared=false dictionary lookup", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateNestedUserControl(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Wpf.RealXamlCompilerHarness.SmokeUserControl", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(nestedControl, \"ControlTitle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled UserControl ElementName binding value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled UserControl click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateReadOnlyGridCollectionsAndAttachedProperties(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Grid row definitions", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDependencyPropertyValue(firstCell, layoutGrid.GetType(), \"RowProperty\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLayoutPanels(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DockPanel left attached Dock", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Canvas top attached property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled WrapPanel item width", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled UniformGrid first column", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled UniformGrid third child text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Grid shared-size scope flag", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled shared-size first column group", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowSharedSizeGridLayout(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled shared-size Grid column width", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowGridSplitterDrag(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridSplitter columns to be measured", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridSplitter dragged left column width", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridSplitter dragged right column width", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowSliderThumbDrag(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Slider template Thumb", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Slider thumb drag value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateScrollingControls(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ScrollViewer content children", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ScrollBar updated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExecuteScrollBarCommand(scrollBar, \"LineDownCommand\", 5.0, \"SmallIncrement\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExecuteScrollBarCommand(scrollBar, \"ScrollToBottomCommand\", 10.0, \"Last\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("scrollRecorder.AssertLast(expectedScrollEventType, expectedValue", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateDateSelectionControls(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Calendar updated selected date", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DatePicker updated selected date", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateImplicitMergedStyle(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled implicit CheckBox style tag", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateToggleChoiceControls(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ToggleButton Checked routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RadioButton alpha Unchecked sender", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateXamlEventHandler(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XAML Click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRepeatButton(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RepeatButton interval", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RepeatButton Click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateThumbDragManager(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Thumb DragStarted handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Thumb DragDelta horizontal change", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Thumb bubbled DragDelta source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Thumb DragCompleted canceled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateStyleEventSetter(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled EventSetter Click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"RoutedCommandButton\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRoutedCommand(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled routed command target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("InvokeTwoArgumentCommand(routedCommand, \"Execute\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"ClassCommandTargetBox\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"ClassCommandButton\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled class command target binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled class routed command name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("class command CanExecute handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("class command execution count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("class command disabled CanExecute result", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateInputBinding(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Window input bindings", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled KeyGesture", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled KeyBinding command executed parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMouseBinding(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MouseBinding surface", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MouseGesture action", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MouseBinding routed command name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable class input KeyBinding focused target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable class input KeyBinding handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable class input KeyBinding command execution count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable class input KeyBinding command parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable class input KeyBinding ignores key up", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMenuItems(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("RaiseMenuItemClick(clickItem)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled command MenuItem routed command", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled command MenuItem CanExecute result", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MenuItem Click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled checkable MenuItem Checked handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled checkable MenuItem Unchecked handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled command MenuItem routed command count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateContextMenuAndToolTip(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ContextMenu Click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ContextMenu routed command count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ToolTip placement", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateToolBarAndStatusBar(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ToolBar routed command count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ToolBar ToggleButton checked state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled StatusBar TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRangeControls(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Slider Value binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Slider two-way value source update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExecuteSliderCommand(slider, dataContext, progress, \"IncreaseSmall\", 40.1", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Slider MaximizeValue command", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ProgressBar value after source update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableInputBindingActivation(presentationCore, activation, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("RaiseHostInput(portableActivation.Host, keyDown)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable input KeyBinding handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMouseBindingActivation(presentationCore, activation, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("WpfInputEventKind.MouseDown", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse MouseBinding command execution count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse MouseBinding command parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse MouseBinding ignores mouse up", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableTextInputActivation(presentationCore, activation, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable text input TextBox text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable text input caret index", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMouseClickActivation(presentationCore, activation, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("WpfInputEventKind.MouseDown", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse captured element after down", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse GotMouseCapture count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse LostMouseCapture count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse routed Click count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMouseWheelActivation(presentationCore, activation, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("WpfInputEventKind.MouseWheel", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse wheel routed event count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse wheel routed event delta", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetTransformToDevice(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateTemplateAndDynamicResource(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button control template", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled VisualStateManager group collection", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowTemplateVisualStateManager(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled VisualStateManager Pressed opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate dynamic resource update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate scoped resource brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate scoped BorderBrush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate scoped BorderThickness", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate trigger disabled opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateStyleAndDataTrigger(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button triggered style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTrigger active value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button property Trigger style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled property Trigger active value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled property Trigger restored brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button MultiTrigger style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiTrigger condition count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiTrigger disabled brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button Trigger action style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Trigger EnterActions BeginStoryboard", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Trigger ExitActions target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button DataTrigger action style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTrigger action binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTrigger EnterActions BeginStoryboard", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTrigger ExitActions target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button MultiDataTrigger action style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiDataTrigger action condition count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiDataTrigger EnterActions BeginStoryboard", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiDataTrigger ExitActions target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button MultiDataTrigger style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiDataTrigger partial-condition value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiDataTrigger active brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"ItemsList\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateItemsBindingAndTemplate(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateImplicitDataTemplate(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateContentTemplateSelector(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateHierarchicalDataTemplate(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateTabControl(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSectionControls(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateNavigationFrame(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled implicit DataTemplate detail model", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled implicit DataTemplate host content binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ContentTemplateSelector selected template resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ContentControl ContentTemplateSelector binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled HierarchicalDataTemplate child ItemsSource path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TreeView ItemsSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TreeView item template", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateExplicitTreeViewItems(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TreeViewItem Expanded routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled explicit TreeView selected beta item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TreeViewItem alpha Unselected routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TabControl selected index", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TabControl selected content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Expander HeaderTemplate resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Expander content binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupBox HeaderTemplate resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupBox content binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateAdornerDecorator(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowAdornerLayer(presentationFramework, compilerHarness, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled AdornerDecorator", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled SmokeAdorner adorned element", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled AdornerLayer added adorner", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateDependencyPropertyCore(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled dependency-property target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled inherited attached property target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled inherited attached property local precedence", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled coerced dependency property minimum value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled AddOwner dependency property coerced value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled AddOwner dependency property local value source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ClearValue dependency property metadata default", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled SetCurrentValue dependency property current source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled OverrideMetadata dependency property default", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateCustomRoutedEvent(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled custom routed event source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled custom routed event final handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled custom routed event source original source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled custom routed event scope sender", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled custom routed event scope handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateClassRoutedEvent(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled class routed event class handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled class routed event skipped normal scope count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled class routed event handled-too scope count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled class routed event handled-too scope handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateAccessKeyFocusScope(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowAccessKeyFocusScope(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled alternate access-key target TextBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled access-key Label target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FocusManager alternate Keyboard.Focus target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FocusManager live logical focus update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FocusManager logical focus restore", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Label access key registered", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Label access key focused target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableAccessKeyActivation(presentationCore, activation, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable access key handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable access key focused target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable access key clear focus", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableKeyboardNavigationActivation(presentationCore, activation, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Tab navigation handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Tab navigation focused target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Shift+Tab navigation focused target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowNavigationFrame(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Frame", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Page content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Page content text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Page content button", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Page click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Page click routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateFrameJournalNavigation(window, frame, flushDispatcherOperations)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertFrameNavigationEventState(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Frame second navigation events", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Frame journal back navigation events", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Frame journal forward navigation events", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled second Page content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Frame journal can go back", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Frame journal forward content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox ItemsSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox item container style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyle setter property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyle default setter value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyle DataTrigger binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyle DataTrigger setter value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate text binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate DataTrigger binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate DataTrigger setter target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate DataTrigger setter value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector alpha template resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector alpha binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector default template tag", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector alpha template property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox ItemTemplateSelector binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyleSelector resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyleSelector alpha style property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox ItemContainerStyleSelector binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DisplayMemberPath ListBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox DisplayMemberPath", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox SelectedValuePath", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox SelectedValue binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox two-way selected value source update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateComboBox(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComboBox SelectedValue binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComboBox collection-change items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComboBox two-way selected value source update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSelectorSelectionChangedEvents(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox SelectionChanged beta removed count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled multi-selection beta selected items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled multi-selection alpha removed item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComboBox SelectionChanged beta added item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateListViewGridView(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridView ListView collection-change items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridView name DisplayMemberBinding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridView ListView selected index after update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateDataGrid(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid ItemsSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid collection-change items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid SelectedItem binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid name binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid category binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid active binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid clipboard copy mode", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid name clipboard binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateDataGridClipboardContent(dataGrid, sourceItems, columns, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid clipboard formatted row", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid active item value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid two-way selected item source update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowDataGridRows(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated row", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated row item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated row selected state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated name cell content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated name cell text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated active cell content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated active cell value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowItemTemplateTriggerActivation(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowItemContainerAlternation(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled alternation ListBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox AlternationCount", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled alternation ListBox collection-change items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled alternation third item container index", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowItemStringFormat(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemStringFormat ListBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox ItemStringFormat", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemStringFormat ListBox collection-change items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemStringFormat collection-change generated item text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowTabControl(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowSectionControls(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowScrollingControls(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ScrollViewer vertical offset", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateGeneratedItemTemplateTextBlock(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate inactive generated item container", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TabControl beta generated content text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TabControl selected index after change", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Expander generated header binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Expander restored expanded state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupBox generated header binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupBox generated content binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyle trigger inactive generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate inactive generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate trigger inactive generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate active generated item container", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyle trigger active generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate active generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate trigger active generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowItemTemplateSelector(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowImplicitDataTemplate(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowContentTemplateSelector(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowHierarchicalDataTemplate(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled implicit DataTemplate generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled implicit DataTemplate generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ContentTemplateSelector generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ContentTemplateSelector generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled HierarchicalDataTemplate root generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled HierarchicalDataTemplate child generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled HierarchicalDataTemplate generated child items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateGeneratedSelectedTemplateTextBlock(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector alpha generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector alpha generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector default generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector default generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowItemContainerStyleSelector(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateGeneratedStyleSelectorItem(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyleSelector alpha generated container style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyleSelector default generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox collection-change items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource sort direction", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource sorting request", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource sorting properties", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox live CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled filtered CollectionViewSource resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox filtered CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource filtered item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource filtering request", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource filtering properties", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox live filtered CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("FilteredItemsFilterCount", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource group descriptions", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource group property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox grouped CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource grouping request", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource grouping properties", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox live grouped CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox GroupStyle entries", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupStyle HeaderTemplate binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live GroupStyle HeaderTemplate binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateCollectionViewGroup(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource collection-change groups", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled filtered CollectionViewSource collection-change item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource collection-change first item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource collection-change first item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource property-change refresh first item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource property-change first item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled filtered CollectionViewSource property-change removed items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live filtered CollectionViewSource property-change removed items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled filtered CollectionViewSource property-change accepted item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live filtered CollectionViewSource property-change accepted item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource property-change refresh groups", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource property-change grouped groups", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource property-change restored first item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource property-change restored first item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live filtered CollectionViewSource property-change restored item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource property-change restored groups", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled current-item CollectionViewSource resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox current-item synchronization", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource current item after selector selection", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox selected item after current-position move", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CompositeCollection container", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CompositeCollection static source items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CompositeCollection source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CompositeCollection initial flattened items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CompositeCollection collection-change appended collection item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowGroupStyleHeader(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupStyle header generated binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(window, \"FindName\", \"InputBox\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("new ProGpuWpfWindowHost(WpfPortableWindowActivation.CreateHostOptions(w))", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(window, \"Show\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(window, \"UpdateLayout\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetProperty(window, \"PortableWindowActivation\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable window visible state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableKeyboardFocus(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox visible after portable show", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("InvokeStatic(keyboardType, \"Focus\", inputBox)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox Keyboard.Focus return value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocused", harnessProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void RealApplicationRunHarnessExercisesStartupUriThroughPortableActivation()
    {
        var harnessProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealApplicationRunHarness",
            "ProGPU.Wpf.RealApplicationRunHarness.csproj");
        var harnessProgramPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealApplicationRunHarness",
            "Program.cs");

        var harnessProject = XDocument.Load(harnessProjectPath);
        var harnessProgram = File.ReadAllText(harnessProgramPath);

        AssertPackageReference(harnessProject, "System.Configuration.ConfigurationManager");
        AssertPackageReference(harnessProject, "System.Formats.Nrbf");
        AssertPackageReference(harnessProject, "$(SystemIOPackagingPackage)");
        AssertPackageReference(harnessProject, "System.Windows.Extensions");

        var compilerHarnessReference = AssertProjectReference(
            harnessProject,
            @"ProGPU.Wpf.RealXamlCompilerHarness\ProGPU.Wpf.RealXamlCompilerHarness.csproj");
        Assert.Equal("false", GetItemMetadata(compilerHarnessReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(compilerHarnessReference, "PrivateAssets"));

        var presentationCoreReference = AssertProjectReference(
            harnessProject,
            @"Microsoft.DotNet.Wpf\src\PresentationCore\PresentationCore.csproj");
        Assert.Equal("false", GetItemMetadata(presentationCoreReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(presentationCoreReference, "PrivateAssets"));

        var presentationFrameworkReference = AssertProjectReference(
            harnessProject,
            @"Microsoft.DotNet.Wpf\src\PresentationFramework\PresentationFramework.csproj");
        Assert.Equal("false", GetItemMetadata(presentationFrameworkReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(presentationFrameworkReference, "PrivateAssets"));

        Assert.DoesNotContain(
            harnessProject.Descendants("ProjectReference"),
            item => IncludeEndsWith(item, "Include", @"ProGPU.Wpf\ProGPU.Wpf.csproj"));
        Assert.DoesNotContain(
            harnessProject.Descendants("ProjectReference"),
            item => IncludeEndsWith(item, "Include", @"external\ProGPU\src\ProGPU.Scene\ProGPU.Scene.csproj"));

        Assert.Contains("CompilerHarnessAssemblyName = \"ProGPU.Wpf.RealXamlCompilerHarness\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(application, \"InitializeComponent\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSystemXamlNameScopeDictionary(systemXaml)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Xaml.NameScopeDictionary", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("standalone System.Xaml NameScopeDictionary", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("wrapped System.Xaml NameScopeDictionary external key stays out of dictionary view", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("wrapped System.Xaml NameScopeDictionary clear preserves external name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("wrapped System.Xaml NameScopeDictionary underlying registration", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("wrapped System.Xaml NameScopeDictionary clear unregisters underlying name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlReader(presentationFramework, presentationCore)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterSystemResourceKeyRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableSystemParameters(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.SystemParameters", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable SystemParameters.{propertyName}", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Markup.XamlReader", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Markup.XamlWriter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ParseLooseXaml(presentationFramework, looseXaml)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("SaveLooseXaml(presentationFramework, brush)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader style StaticResource brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader RelativeSource binding text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader Binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("LooseInputScopeTextBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("<InputMethod.InputScope>", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("<InputScopeName>EmailSmtpAddress</InputScopeName>", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("<InputScopePhrase>external phrase</InputScopePhrase>", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseInputScope(presentationCore, inputScopeTextBox)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Input.InputMethod", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader InputScopeName text content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader InputScopePhrase text content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized GradientStop", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip {description} stop color", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.MenuItem", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("SeparatorStyleKey", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized system resource key member", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip system-key style target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterStyleRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized style dictionary", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip derived BasedOn style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip styled Button inherited Tag", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterControlTemplateRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized ControlTemplate", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip ControlTemplate trigger setter target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip applied ControlTemplate content presenter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterDataTemplateRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized DataTemplate", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader DataTemplate category binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip DataTemplate trigger setter target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip DataTemplate category TextBlock name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterHierarchicalDataTemplateRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized HierarchicalDataTemplate", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip HierarchicalDataTemplate ItemsSource path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip HierarchicalDataTemplate trigger setter target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip HierarchicalDataTemplate count TextBlock name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterItemsPanelTemplateRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized ItemsPanelTemplate", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip ItemsPanelTemplate panel name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip ItemsPanelTemplate item width", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterGroupStyleRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized GroupStyle HeaderTemplate", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip GroupStyle HidesIfEmpty", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip GroupStyle header TextBlock name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip GroupStyle panel orientation", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterFrameworkElementRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized FrameworkElement Button", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip FrameworkElement children", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip Button background", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip TextBox MinWidth", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterFlowDocumentRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized FlowDocument root", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip FlowDocument paragraph name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip FlowDocument TextRange second list item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(application, \"Run\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationServiceTypeName = \"System.Windows.PortableWindowActivationService\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("new Func<object, object>(recorder.Activate)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("new Action<object, object, object>(recorder.SetWindowBorder)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("public void SetWindowBorder(object activation, object resizeMode, object windowStyle)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("method.GetParameters().Length == 2", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("new Action<object>(recorder.Run)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMainWindow(_presentationCore, window, _application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateFreezableBrushResource(resources)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateFreezableGradientBrushResource(resources)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Freezable brush clone mutable opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Freezable gradient brush clone mutable stop offset", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Freezable gradient current-value clone stop collection", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Freezable current-value clone opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(resources, \"BasedOnTextBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox BasedOn style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox BasedOn local setter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox BasedOn inherited margin top", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateBindingAndCommand(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"CanExecuteCommandButton\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CanExecute command initial button state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CanExecute command enabled button state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CanExecute command button execution count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CanExecute command disabled button state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowCommandManagerRequery(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("() => FlushDispatcherOperations(typedActivation.Window, \"Background\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"RequeryCommandButton\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetProperty(dataContext, \"RequeryCommand\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Input.CommandManager", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("InvalidateRequerySuggested", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CommandManager RequerySuggested enabled button state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CommandManager RequerySuggested button execution count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CommandManager RequerySuggested disabled button state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBlock property-change binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateTextBoxSelection(inputBox)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox selected text replacement", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePasswordBox(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PasswordBox PasswordChanged count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PasswordBox secure password length", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PasswordBox clear changed count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRuntimeNameScope(window, inputBox)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(window, \"RegisterName\", \"RuntimeRegisteredButton\", registeredButton)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled namescope runtime registered lookup", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled namescope duplicate preserves original", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled namescope runtime re-register after unregister", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRichFlowDocument(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Italic", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Underline", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Span", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.LineBreak", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Figure", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertFlowDocumentAnchoredBlockText(figure, \"figure anchored text\", \"figure\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Floater", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertFlowDocumentAnchoredBlockText(floater, \"floater anchored text\", \"floater\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange paragraph text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange italic text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange underline text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange span text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange line-break text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange figure text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange floater text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument list items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.InlineUIContainer", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument inline Button content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.BlockUIContainer", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument block Button content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox selection text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleBold\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleBold applied weight", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleBold restored weight", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleItalic\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleItalic applied style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleItalic restored style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleUnderline\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleUnderline applied decoration location", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleUnderline restored decoration count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyFontSize\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyFontSize value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox IncreaseFontSize value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DecreaseFontSize value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyFontFamily\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyFontFamily value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyForeground\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyForeground color", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyBackground\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyBackground color", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleSubscript\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleSubscript applied variant", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleSubscript restored variant", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleSuperscript\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleSuperscript applied variant", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleSuperscript restored variant", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyInlineFlowDirectionRTL\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyInlineFlowDirectionRTL value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyInlineFlowDirectionLTR\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyInlineFlowDirectionLTR value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"AlignCenter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox AlignCenter paragraph alignment", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox AlignRight paragraph alignment", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox AlignJustify paragraph alignment", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox AlignLeft paragraph alignment", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyParagraphFlowDirectionRTL\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyParagraphFlowDirectionRTL value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ApplyParagraphFlowDirectionLTR\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ApplyParagraphFlowDirectionLTR value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument section blocks", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument table columns", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument table cells", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange section text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange second table cell", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("SetStaticProperty(textEditorType, \"IsTableEditingEnabled\", true)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.TextEditorTables", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"InsertRows\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox InsertRows table rows", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox InsertRows copied cells", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"InsertColumns\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox InsertColumns first row cells", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox InsertColumns copied row cells", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"DeleteRows\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DeleteRows table rows", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DeleteRows preserved original row", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"DeleteColumns\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DeleteColumns table-cell selection", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DeleteColumns first row cells", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DeleteColumns preserved second cell", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"MergeCells\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox MergeCells column span", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"SplitCell\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox SplitCell copied second cell", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleBullets\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleBullets marker style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"ToggleNumbering\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox ToggleNumbering marker style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"IncreaseIndentation\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox IncreaseIndentation nested list", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"DecreaseIndentation\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox DecreaseIndentation top-level list items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(editingCommandsType, \"RemoveListMarkers\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RichTextBox RemoveListMarkers document blocks", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button command binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateAdvancedBindingFeatures(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PriorityBinding fallback value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PriorityBinding child bindings", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PriorityBinding first path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetPriorityBindingExpression", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiBinding string-format value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled converter binding value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled converter binding resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled converter parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiBinding converter value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiBinding converter resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiBinding converter child bindings", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetMultiBindingExpression", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RelativeSource binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation invalid source value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation restored source value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ValidationRule binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Binding ValidationRules", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled custom ValidationRule parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ValidationRule rejected source value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation Error added sender", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation Error removed content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ValidationRule Error added rule", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ValidationRule Error removed routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ValidationRule restored error state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateBindingTransferEvents(window, dataContext)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled binding transfer NotifyOnTargetUpdated", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Binding SourceUpdated property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Binding TargetUpdated routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateBindingGroup(window, dataContext, validationType)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled BindingGroup custom ValidationRule", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled BindingGroup rejected commit", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled BindingGroup accepted commit", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled BindingGroup accepted first source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateObjectDataProvider(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider synchronous flag", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider object type", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider object instance", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider second parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider binding source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateXmlDataProvider(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XmlDataProvider resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XmlDataProvider XPath", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XmlDataProvider synchronous flag", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XmlDataProvider XPath bound text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XmlDataProvider binding XPath", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateStoryboardEventTrigger(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Storyboard target TextBlock", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Storyboard target initial Loaded count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled EventTrigger routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled BeginStoryboard action", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Storyboard children", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DoubleAnimation target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DoubleAnimation fill behavior", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled click Storyboard trigger Button", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled click DoubleAnimation target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowLoadedEvent(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowClickStoryboardEventTrigger(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("FlushDispatcherOperations(typedActivation.Window, \"Render\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled click Storyboard trigger Button post-click opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowStyleTriggerActions(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style Trigger EnterActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style Trigger ExitActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowMultiTriggerActions(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style MultiTrigger EnterActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style MultiTrigger ExitActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button MultiTrigger action style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiTrigger action condition count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiTrigger EnterActions BeginStoryboard", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiTrigger ExitActions target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowDataTriggerActions(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("FlushDispatcherOperations(typedActivation.Window, \"DataBind\", \"Render\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style DataTrigger EnterActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style DataTrigger ExitActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowMultiDataTriggerActions(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style MultiDataTrigger partial-condition opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style MultiDataTrigger EnterActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled style MultiDataTrigger ExitActions opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLoadedEventHandlerState(activation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("FlushDispatcherOperations(typedActivation.Window, \"Loaded\", \"Render\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("PortableMediaContextRenderServiceTypeName", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("_mediaContextRenderRegistration = RegisterMediaContextRenderService()", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("RequestRenderFromMediaContext", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable MediaContext render request count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("PortablePresentationSourceTypeName", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreatePortablePresentationSource(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("SetProperty(source, \"RootVisual\", window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("typedActivation.DisposePresentationSource()", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Storyboard target post-Loaded opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Storyboard target Loaded handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Storyboard target Loaded routed event name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMarkupExtension(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MarkupExtension TextBlock", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MarkupExtension provided text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMergedResourceDictionary(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled merged-resource margin top", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.ComponentResourceKey", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComponentResourceKey lookup key", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComponentResourceKey TextBlock", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComponentResourceKey foreground", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComponentResourceKey application lookup", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateUnsharedResource(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled x:Shared=false StaticResource consumers", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled x:Shared=false dictionary lookup", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateNestedUserControl(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled UserControl ElementName binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled UserControl click routed event name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateReadOnlyGridCollectionsAndAttachedProperties(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Grid column definitions", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDependencyPropertyValue(secondCell, layoutGrid.GetType(), \"ColumnProperty\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLayoutPanels(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DockPanel right attached Dock", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Canvas left attached property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled WrapPanel item height", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled UniformGrid rows", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled UniformGrid children", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled shared-size second column group", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled shared-size second value column", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowSharedSizeGridLayout(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled shared-size Grid columns to be measured", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowGridSplitterDrag(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridSplitter columns to be measured", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridSplitter dragged left column width", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridSplitter dragged right column width", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridSplitter resize behavior", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridSplitter keyboard increment", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowSliderThumbDrag(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Slider template Thumb", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Slider thumb drag value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateScrollingControls(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ScrollViewer horizontal visibility", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ScrollBar viewport size", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExecuteScrollBarCommand(scrollBar, \"LineDownCommand\", 5.0, \"SmallIncrement\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExecuteScrollBarCommand(scrollBar, \"ScrollToTopCommand\", 0.0, \"First\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("scrollRecorder.AssertLast(expectedScrollEventType, expectedValue", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateDateSelectionControls(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Calendar selected date collection item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DatePicker selected date format", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateImplicitMergedStyle(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled implicit CheckBox style margin top", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateToggleChoiceControls(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ToggleButton Unchecked routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RadioButton beta Checked sender", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateXamlEventHandler(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XAML Click routed event name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRepeatButton(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RepeatButton delay", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RepeatButton Click routed event name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateThumbDragManager(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Thumb DragStarted horizontal offset", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Thumb DragDelta handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Thumb bubbled DragDelta routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Thumb DragCompleted horizontal change", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateStyleEventSetter(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled EventSetter Click routed event name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRoutedCommand(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled routed command target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"ClassCommandTargetBox\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"ClassCommandButton\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled class command target binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled class routed command name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("class command CanExecute handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("class command execution count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("class command disabled CanExecute result", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateInputBinding(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Window input bindings", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled KeyGesture", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled KeyBinding command executed parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMouseBinding(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MouseBinding surface", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MouseGesture action", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MouseBinding routed command name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run class input KeyBinding focused target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run class input KeyBinding handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run class input KeyBinding command execution count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run class input KeyBinding command parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run class input KeyBinding ignores key up", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMenuItems(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("RaiseMenuItemClick(clickItem)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled command MenuItem routed command", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled command MenuItem CanExecute result", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MenuItem Click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled checkable MenuItem Checked handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled checkable MenuItem Unchecked handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled command MenuItem routed command count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateContextMenuAndToolTip(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ContextMenu Click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ContextMenu routed command count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ToolTip placement", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateToolBarAndStatusBar(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ToolBar routed command count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ToolBar ToggleButton checked state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled StatusBar TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRangeControls(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Slider Value binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Slider two-way value source update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExecuteSliderCommand(slider, dataContext, progress, \"IncreaseSmall\", 40.1", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Slider MaximizeValue command", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ProgressBar value after source update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableInputBindingActivation(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreatePortableInputEvent(\"KeyDown\", \"F6\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run input KeyBinding handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMouseBindingActivation(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreatePortableInputEvent(\"MouseDown\", x: x, y: y, buttonName: \"Right\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run mouse MouseBinding command execution count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run mouse MouseBinding command parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run mouse MouseBinding ignores mouse up", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse MouseBinding persisted command parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableTextInputActivation(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreatePortableInputEvent(\"TextInput\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run text input TextBox text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMouseClickActivation(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreatePortableInputEvent(\"MouseDown\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run mouse captured element after down", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run mouse GotMouseCapture count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run mouse LostMouseCapture count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run mouse routed Click count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse routed Click persisted count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse GotMouseCapture persisted count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse LostMouseCapture persisted count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMouseWheelActivation(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreatePortableInputEvent(\"MouseWheel\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run mouse wheel routed event count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable mouse wheel persisted count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateTemplateAndDynamicResource(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate named part", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled VisualStateManager group collection", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowTemplateVisualStateManager(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled VisualStateManager Pressed opacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate dynamic resource update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate scoped resource brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate scoped BorderBrush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate scoped BorderThickness", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate trigger source state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateStyleAndDataTrigger(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTrigger inactive value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTrigger active brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button property Trigger style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled property Trigger active value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled property Trigger restored brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button MultiTrigger style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiTrigger condition count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiTrigger disabled brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button Trigger action style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Trigger EnterActions BeginStoryboard", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Trigger ExitActions target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button DataTrigger action style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTrigger action binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTrigger EnterActions BeginStoryboard", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTrigger ExitActions target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button MultiDataTrigger action style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiDataTrigger action condition count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiDataTrigger EnterActions BeginStoryboard", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiDataTrigger ExitActions target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button MultiDataTrigger style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiDataTrigger partial-condition brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiDataTrigger active value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateItemsBindingAndTemplate(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateImplicitDataTemplate(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateContentTemplateSelector(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateHierarchicalDataTemplate(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateTabControl(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSectionControls(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateNavigationFrame(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled implicit DataTemplate detail title", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled implicit DataTemplate host content binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ContentTemplateSelector selected template resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ContentControl ContentTemplateSelector binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled HierarchicalDataTemplate text binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TreeView ItemsSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TreeView generated root items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateExplicitTreeViewItems(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TreeViewItem Collapsed routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled explicit alpha unselected by TreeView manager", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TreeViewItem beta Selected sender", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TabControl selected index", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TabControl selected content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Expander HeaderTemplate resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Expander content binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupBox HeaderTemplate resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupBox content binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateAdornerDecorator(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowAdornerLayer(_presentationFramework, _compilerHarness, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled AdornerDecorator", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled SmokeAdorner adorned element", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled AdornerLayer added adorner", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateDependencyPropertyCore(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled dependency-property target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled inherited attached property target value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled inherited attached property local precedence", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled coerced dependency property minimum value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled AddOwner dependency property coerced value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled AddOwner dependency property local value source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ClearValue dependency property metadata default", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled SetCurrentValue dependency property current source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled OverrideMetadata dependency property default", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateCustomRoutedEvent(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled custom routed event source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled custom routed event final handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled custom routed event source original source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled custom routed event scope sender", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled custom routed event scope handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateClassRoutedEvent(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled class routed event class handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled class routed event skipped normal scope count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled class routed event handled-too scope count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled class routed event handled-too scope handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateAccessKeyFocusScope(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowAccessKeyFocusScope(_presentationCore, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled alternate access-key target TextBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled access-key Label target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FocusManager alternate Keyboard.Focus target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FocusManager live logical focus update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FocusManager logical focus restore", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Label access key registered", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Label access key focused target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableAccessKeyActivation(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run access key handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run access key focused target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run access key clear focus", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableKeyboardNavigationActivation(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run Tab navigation handled state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run Tab navigation focused target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Application.Run Shift+Tab navigation focused target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowNavigationFrame(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Frame", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Page content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Page content text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Page content button", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Page click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Page click routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateFrameJournalNavigation(window, frame, flushDispatcherOperations)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertFrameNavigationEventState(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Frame second navigation events", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Frame journal back navigation events", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Frame journal forward navigation events", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled second Page content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Frame journal can go back", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Frame journal forward content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox two-way selected item binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox item container style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyle setter property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyle default setter value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyle DataTrigger binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyle DataTrigger setter value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate text binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate DataTrigger value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate DataTrigger setter property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate DataTrigger setter value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector alpha template resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector alpha binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector default template tag", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector default template property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox ItemTemplateSelector binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyleSelector resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyleSelector default style property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox ItemContainerStyleSelector binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DisplayMemberPath ListBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox DisplayMemberPath", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox SelectedValuePath", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox SelectedValue binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox two-way selected value source update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateComboBox(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComboBox SelectedValue binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComboBox collection-change items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComboBox two-way selected value source update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSelectorSelectionChangedEvents(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox SelectionChanged routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled multi-selection beta remains selected item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled multi-selection alpha removed removed count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ComboBox SelectionChanged beta removed count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateListViewGridView(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridView ListView collection-change items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridView category DisplayMemberBinding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GridView ListView selected index after update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateDataGrid(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid ItemsSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid collection-change items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid SelectedItem binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid name binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid category binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid active binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid clipboard copy mode", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid name clipboard binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateDataGridClipboardContent(dataGrid, sourceItems, columns, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid clipboard formatted row", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid active item value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid two-way selected item source update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowDataGridRows(_presentationCore, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated row", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated row item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated row selected state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated name cell content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated name cell text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated active cell content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataGrid generated active cell value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowItemTemplateTriggerActivation(_presentationCore, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowItemContainerAlternation(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowItemContainerAlternation(activation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled alternation ListBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox AlternationCount", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled alternation ListBox collection-change items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled alternation third item container index", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowItemStringFormat(_presentationCore, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowItemStringFormat(_presentationCore, activation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemStringFormat ListBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox ItemStringFormat", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemStringFormat ListBox collection-change items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemStringFormat collection-change generated item text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowTabControl(_presentationCore, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowSectionControls(_presentationCore, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowScrollingControls(typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ScrollViewer vertical offset", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateGeneratedItemTemplateTextBlock(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate inactive generated item container", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TabControl beta generated content text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TabControl selected index after change", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Expander generated header binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Expander restored expanded state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupBox generated header binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupBox generated content binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyle trigger inactive generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate inactive generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate trigger inactive generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate active generated item container", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyle trigger active generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate active generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate trigger active generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowItemTemplateSelector(_presentationCore, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowImplicitDataTemplate(_presentationCore, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowContentTemplateSelector(_presentationCore, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateTabControl(activation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSectionControls(activation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowHierarchicalDataTemplate(_presentationCore, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled implicit DataTemplate generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled implicit DataTemplate generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ContentTemplateSelector generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ContentTemplateSelector generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled HierarchicalDataTemplate root generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled HierarchicalDataTemplate child generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled HierarchicalDataTemplate generated child items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateGeneratedSelectedTemplateTextBlock(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector alpha generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector alpha generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector default generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplateSelector default generated value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowItemContainerStyleSelector(_presentationCore, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyleSelector alpha generated container style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ItemContainerStyleSelector default generated TextBlock binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource sort property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled sorted ListBox generated items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource sorting request", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource sorting properties", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox live CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled filtered CollectionViewSource resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox filtered CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource filtered item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource filtering request", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource filtering properties", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox live filtered CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("FilteredItemsFilterCount", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource group descriptions", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource group property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox grouped CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource grouping request", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource grouping properties", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox live grouped CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox GroupStyle entries", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupStyle HeaderTemplate binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live GroupStyle HeaderTemplate binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateCollectionViewGroup(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource collection-change groups", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled filtered CollectionViewSource collection-change item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource collection-change first item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource collection-change first item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource property-change refresh first item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource property-change first item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled filtered CollectionViewSource property-change removed items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live filtered CollectionViewSource property-change removed items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled filtered CollectionViewSource property-change accepted item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live filtered CollectionViewSource property-change accepted item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource property-change refresh groups", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource property-change grouped groups", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource property-change restored first item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource property-change restored first item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live filtered CollectionViewSource property-change restored item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled live CollectionViewSource property-change restored groups", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled current-item CollectionViewSource resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox current-item synchronization", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource current item after selector selection", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox selected item after current-position move", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CompositeCollection container", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CompositeCollection static source items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CompositeCollection source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CompositeCollection initial flattened items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CompositeCollection collection-change appended collection item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowGroupStyleHeader(_presentationCore, activation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupStyle header generated binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("recorder.ValidateAfterRun()", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("if (RunCount != 1)", harnessProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void RealThemeRuntimeHarnessLoadsFluentThemeBamlThroughRealPresentationFramework()
    {
        var harnessProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealThemeRuntimeHarness",
            "ProGPU.Wpf.RealThemeRuntimeHarness.csproj");
        var harnessProgramPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.RealThemeRuntimeHarness",
            "Program.cs");

        var harnessProject = XDocument.Load(harnessProjectPath);
        var harnessProgram = File.ReadAllText(harnessProgramPath);

        AssertProjectReference(harnessProject, @"ProGPU.Wpf\ProGPU.Wpf.csproj");
        AssertProjectReference(harnessProject, @"external\ProGPU\src\ProGPU.Scene\ProGPU.Scene.csproj");

        var compilerHarnessReference = AssertProjectReference(
            harnessProject,
            @"ProGPU.Wpf.RealXamlCompilerHarness\ProGPU.Wpf.RealXamlCompilerHarness.csproj");
        Assert.Equal("false", GetItemMetadata(compilerHarnessReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(compilerHarnessReference, "PrivateAssets"));

        var fluentReference = AssertProjectReference(
            harnessProject,
            @"Microsoft.DotNet.Wpf\src\Themes\PresentationFramework.Fluent\PresentationFramework.Fluent.csproj");
        Assert.Equal("false", GetItemMetadata(fluentReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(fluentReference, "PrivateAssets"));

        var presentationCoreReference = AssertProjectReference(
            harnessProject,
            @"Microsoft.DotNet.Wpf\src\PresentationCore\PresentationCore.csproj");
        Assert.Equal("false", GetItemMetadata(presentationCoreReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(presentationCoreReference, "PrivateAssets"));

        var presentationFrameworkReference = AssertProjectReference(
            harnessProject,
            @"Microsoft.DotNet.Wpf\src\PresentationFramework\PresentationFramework.csproj");
        Assert.Equal("false", GetItemMetadata(presentationFrameworkReference, "ReferenceOutputAssembly"));
        Assert.Equal("all", GetItemMetadata(presentationFrameworkReference, "PrivateAssets"));

        Assert.Contains("FluentDictionaryUri = \"/PresentationFramework.Fluent;component/Themes/Fluent.xaml\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("SetProperty(themeDictionary, \"Source\", new Uri(FluentDictionaryUri, UriKind.Relative))", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultWindowStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultButtonStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"AccentButtonStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultCalendarStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultCalendarButtonStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultCalendarDayButtonStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultCalendarItemStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultCheckBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultComboBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultContextMenuStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultComboBoxItemStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultComboBoxTextBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultComboBoxToggleButtonStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultComboBoxTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"EditableComboBoxTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultDataGridStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultDataGridCellStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DataGridCheckBoxElementDefaultStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DataGridCheckBoxEditingElementDefaultStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultDataGridColumnFloatingHeaderStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultDataGridColumnHeaderStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultDataGridColumnHeadersPresenterStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultDataGridCellsPresenterStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultDataGridHeaderDropSeparatorStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultDataGridRowHeaderStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultDataGridRowStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultDatePickerStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DatePickerCalendarStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultDatePickerTextBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultExpanderStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultExpanderToggleButtonDownStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultGridSplitterStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultGroupBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultItemsControlStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultLabelStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultListBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultListBoxItemStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultListViewStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultListViewItemStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"ListViewTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultMenuStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultMenuItemStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultPasswordBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultPasswordBoxContextMenu\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultRadioButtonStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultRepeatButtonStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultResizeGripStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultScrollBarStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"HorizontalScrollBarTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"VerticalScrollBarTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultScrollViewerStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultSeparatorStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultStatusBarItemStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultTabControlStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultTabItemStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultTopTabControlStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultBottomTabControlStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultLeftTabControlStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultRightTabControlStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultTextBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultTextBoxControlTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultThumbStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultToggleButtonStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultToolTipStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultTreeViewStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultTreeViewItemStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultRichTextBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Button", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Calendar", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.CheckBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ComboBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ContextMenu", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.DataGrid", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.DataGridTextColumn", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.DataGridCheckBoxColumn", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Primitives.DataGridColumnHeader", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Primitives.DataGridColumnHeadersPresenter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.DatePicker", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Expander", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.GridSplitter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.GroupBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ItemsControl", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Label", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ListBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ListBoxItem", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ListView", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Menu", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.MenuItem", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.PasswordBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.RadioButton", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Primitives.RepeatButton", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Primitives.ResizeGrip", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Primitives.ScrollBar", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ScrollViewer", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Separator", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Slider", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Primitives.StatusBar", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ToolTip", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ToolBar", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ToolBarTray", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Primitives.Thumb", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Primitives.ToggleButton", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.TabControl", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.TreeView", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.ProgressBar", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", buttonType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", calendarType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", checkBoxType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", comboBoxType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", contextMenuType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", dataGridType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", dataGridColumnHeaderType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", dataGridColumnHeadersPresenterType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", datePickerType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", expanderType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", gridSplitterType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", groupBoxType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", itemsControlType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", labelType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", listBoxType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", listBoxItemType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", listViewType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", menuType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", passwordBoxType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", radioButtonType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", repeatButtonType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", resizeGripType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", scrollBarType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", scrollViewerType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", separatorType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", statusBarType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", tabControlType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", textBoxType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", toolTipType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", thumbType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", toggleButtonType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", toolBarType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", toolBarTrayType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", treeViewType", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(toolBarType, \"ButtonStyleKey\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(toolBarType, \"ToggleButtonStyleKey\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(toolBarType, \"SeparatorStyleKey\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(statusBarType, \"SeparatorStyleKey\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetStaticProperty(menuItemType, \"SeparatorStyleKey\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, buttonType)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, calendarType)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, dataGridType)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, datePickerType)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, textBoxType)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, sliderType)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, progressBarType)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"HorizontalSliderTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"VerticalSliderTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"SliderThumbStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"SliderButtonStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"WindowTemplateKey\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("implicit Button Fluent BasedOn default Button style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("implicit Calendar Fluent BasedOn default Calendar style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("implicit DataGrid Fluent BasedOn default DataGrid style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("implicit DatePicker Fluent BasedOn default DatePicker style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("implicit TextBox Fluent BasedOn default TextBox style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("DatePicker Calendar Fluent BasedOn default Calendar style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("DataGrid CheckBox editing Fluent BasedOn element style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("SetProperty(button, \"ContextMenu\", contextMenu)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("SetProperty(button, \"ToolTip\", toolTip)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(window, \"FindName\", \"DocumentBox\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled themed RichTextBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed ContextMenu", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed ToolTip", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed TextBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed TabControl", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed ListView", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed TreeView", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed Calendar", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed DatePicker", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed Menu", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed ToolBarTray", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed StatusBar", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed CheckBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed RadioButton", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed ToggleButton", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed RepeatButton", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed Expander", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed GroupBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed ScrollViewer", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed ScrollBar", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed default Button", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed ItemsControl", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed ListBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed Label", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed Separator", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed GridSplitter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed ResizeGrip", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed Thumb", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed ComboBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed PasswordBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed Slider", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed ProgressBar", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("created themed DataGrid", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Theme implicit button", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Theme implicit text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(window, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(button, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(buttonContextMenu, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ApplyItemsTemplates(buttonContextMenu, \"themed ContextMenu items\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(buttonToolTip, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(textBox, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(tabControl, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ApplyItemsTemplates(tabControl, \"themed TabControl items\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(listView, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ApplyItemsTemplates(listView, \"themed ListView items\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(treeView, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ApplyItemsTemplates(treeView, \"themed TreeView root items\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(calendar, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(datePicker, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(menu, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ApplyItemsTemplates(menu, \"themed Menu items\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(toolBarTray, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(themedToolBar, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ApplyItemsTemplates(themedToolBar, \"themed ToolBar items\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(statusBar, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ApplyItemsTemplates(statusBar, \"themed StatusBar items\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(checkBox, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(radioButton, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(toggleButton, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(repeatButton, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(expander, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(groupBox, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(scrollViewer, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(scrollBar, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(defaultButton, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(itemsControl, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(listBox, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ApplyItemsTemplates(listBox, \"themed ListBox items\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(separator, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(gridSplitter, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(resizeGrip, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(thumb, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(comboBox, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(passwordBox, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(slider, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(progressBar, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(dataGrid, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(richTextBox, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(textBox, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(tabControl, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(listView, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(treeView, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(calendar, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(datePicker, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(menu, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(themedToolBar, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(checkBox, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(radioButton, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(toggleButton, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(repeatButton, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(expander, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(groupBox, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(scrollViewer, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(scrollBar, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(defaultButton, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(itemsControl, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(listBox, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(separator, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(gridSplitter, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(resizeGrip, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(thumb, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(comboBox, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(passwordBox, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(slider, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(progressBar, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(dataGrid, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(richTextBox, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(buttonContextMenu, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(buttonToolTip, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(tabControl, \"Style\"), \"Template\", \"TabControl Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(listView, \"Style\"), \"Template\", \"ListView Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(treeView, \"Style\"), \"Template\", \"TreeView Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(calendar, \"Style\"), \"Template\", \"Calendar Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(datePicker, \"Style\"), \"Template\", \"DatePicker Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(datePicker, \"Style\"), \"CalendarStyle\", \"DatePicker Fluent calendar-style setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(menu, \"Style\"), \"Template\", \"Menu Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(buttonContextMenu, \"Style\"), \"Template\", \"ContextMenu Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(buttonToolTip, \"Style\"), \"Template\", \"ToolTip Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(themedToolBar, \"Style\"), \"Template\", \"ToolBar Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("StatusBarItem Fluent template setter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(checkBox, \"Style\"), \"Template\", \"CheckBox Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(radioButton, \"Style\"), \"Template\", \"RadioButton Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(toggleButton, \"Style\"), \"Template\", \"ToggleButton Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(repeatButton, \"Style\"), \"Template\", \"RepeatButton Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(expander, \"Style\"), \"Template\", \"Expander Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(groupBox, \"Style\"), \"Template\", \"GroupBox Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(scrollViewer, \"Style\"), \"Template\", \"ScrollViewer Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasTriggerSetter(GetProperty(scrollBar, \"Style\"), \"Orientation\", \"Horizontal\", \"Template\", \"ScrollBar Fluent horizontal template trigger\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasTriggerSetter(GetProperty(scrollBar, \"Style\"), \"Orientation\", \"Vertical\", \"Template\", \"ScrollBar Fluent vertical template trigger\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(defaultButton, \"Style\"), \"Template\", \"Default Button Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(itemsControl, \"Style\"), \"Template\", \"ItemsControl Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(listBox, \"Style\"), \"Template\", \"ListBox Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(GetCollectionItem(GetProperty(listBox, \"Items\"), 0), \"Style\"), \"Template\", \"ListBoxItem Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(separator, \"Style\"), \"Template\", \"Separator Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(gridSplitter, \"Style\"), \"Template\", \"GridSplitter Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(resizeGrip, \"Style\"), \"Template\", \"ResizeGrip Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(thumb, \"Style\"), \"Template\", \"Thumb Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(comboBox, \"Style\"), \"Template\", \"ComboBox Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(passwordBox, \"Style\"), \"Template\", \"PasswordBox Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(textBox, \"Style\"), \"Template\", \"TextBox Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(progressBar, \"Style\"), \"Template\", \"ProgressBar Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(dataGrid, \"Style\"), \"Template\", \"DataGrid Fluent template setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(dataGrid, \"Style\"), \"RowStyle\", \"DataGrid Fluent row-style setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(dataGrid, \"Style\"), \"RowHeaderStyle\", \"DataGrid Fluent row-header-style setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(dataGrid, \"Style\"), \"CellStyle\", \"DataGrid Fluent cell-style setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(dataGrid, \"Style\"), \"ColumnHeaderStyle\", \"DataGrid Fluent column-header-style setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(dataGrid, \"Style\"), \"DropLocationIndicatorStyle\", \"DataGrid Fluent drop-location-style setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertStyleHasSetter(GetProperty(dataGrid, \"Style\"), \"DragIndicatorStyle\", \"DataGrid Fluent drag-indicator-style setter\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ContextMenu item count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ToolTip content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed TextBox text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed TabControl selected index", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed TabItem header", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ListView selected index", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ListViewItem content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed TreeViewItem expanded state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed TreeViewItem child header", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed Calendar selected date", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed DatePicker selected date", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed DatePicker calendar-style dynamic resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed Menu root item count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed MenuItem child header", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ToolBarTray toolbar count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ToolBar toggle checked state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed StatusBarItem content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed CheckBox checked state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed RadioButton group name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ToggleButton checked state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed RepeatButton content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed Expander expanded state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed GroupBox content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ScrollViewer vertical visibility", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ScrollBar viewport size", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed default Button content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertEqual(4, GetCollectionCount(GetProperty(itemsControl, \"Items\")), \"themed ItemsControl item count\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ItemsControl item content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("runtime implicit themed Button item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("runtime implicit themed TextBox item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("runtime implicit Button Fluent style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("runtime implicit TextBox Fluent style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ListBox selected index", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ListBoxItem content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed Label focusable", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed Separator focusable", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed GridSplitter height", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed Thumb height", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ComboBox selected item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed PasswordBox password", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed Slider value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed ProgressBar value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed DataGrid column count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed DataGrid text binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed DataGrid checkbox binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed DataGrid selected row name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ThemeGridRow", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertCollectionCount(children, expectedMinimum: 44", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 31)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 30)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 29)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 28)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 27)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 26)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 25)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 24)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 23)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 22)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 21)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 20)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 19)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 18)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 17)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 16)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 15)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 14)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 13)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 12)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 11)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 10)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 9)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 8)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 7)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 6)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 5)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 4)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 3)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 2)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, childCount - 1)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateThemedVisualReplay(windowsBase, window)", harnessProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke(window, \"FindName\", \"ImplicitStyleCheckBox\")", harnessProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("SetEnumProperty(implicitStyleCheckBox, \"Visibility\", \"Collapsed\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("MeasureAndArrange(windowsBase, content, pixelWidth, pixelHeight)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(element, \"Measure\", availableSize)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(element, \"Arrange\", finalRect)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertPositiveSize(GetProperty(element, \"DesiredSize\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("target.ReplayVisualSubtreeRetained(content, pixelWidth, pixelHeight)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("replayResult.ContentCount", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("replayResult.RenderData.AppliedCount", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("target.RetainedVisualBranchCount", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("target.RetainedWpfVisualRoot.Children.Count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("CountRetainedCommands(target.RetainedWpfVisualRoot)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation", harnessProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualContentReflectionBridgeReadsUiElementDrawingContent()
    {
        var bridgeSource = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "Mil",
            "WpfVisualContentReflectionBridge.cs"));

        Assert.Contains("FindField(visualType, \"_content\")", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("FindField(visualType, \"_drawingContent\")", bridgeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RealPresentationFrameworkSmokeGuardsNativePlatformEntrypoints()
    {
        var compositionExports = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "Common",
            "Graphics",
            "exports.cs"));
        var uiElement = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "UIElement.cs"));
        var pathGeometry = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "PathGeometry.cs"));
        var geometry = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Geometry.cs"));
        var rectangleGeometry = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "RectangleGeometry.cs"));
        var stylusLogic = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "Stylus",
            "Common",
            "StylusLogic.cs"));
        var accessKeyManager = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "AccessKeyManager.cs"));
        var inputLanguageSource = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "InputLanguageSource.cs"));
        var inputLanguageManager = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "InputLanguageManager.cs"));
        var inputMethod = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "InputMethod.cs"));
        var tipTsfHelper = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "MS",
            "internal",
            "Interop",
            "TipTsfHelper.cs"));
        var systemResources = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "SystemResources.cs"));
        var xamlReader = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Markup",
            "XamlReader.cs"));
        var popupControlService = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Controls",
            "PopupControlService.cs"));
        var comboBox = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Controls",
            "ComboBox.cs"));
        var popup = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Controls",
            "Primitives",
            "Popup.cs"));
        var menu = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Controls",
            "Menu.cs"));
        var menuBase = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Controls",
            "Primitives",
            "MenuBase.cs"));
        var systemParameters = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "SystemParameters.cs"));
        var flowDocumentView = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "MS",
            "Internal",
            "Documents",
            "FlowDocumentView.cs"));
        var textSelection = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "windows",
            "Documents",
            "TextSelection.cs"));
        var textEditorTyping = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "windows",
            "Documents",
            "TextEditorTyping.cs"));
        var selectionWordBreaker = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "windows",
            "Documents",
            "SelectionWordBreaker.cs"));
        var textFindEngine = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "windows",
            "Documents",
            "TextFindEngine.cs"));
        var winEventHandler = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "windows",
            "Documents",
            "WinEventHandler.cs"));
        var moveSizeWinEventHandler = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "windows",
            "Documents",
            "MoveSizeWinEventHandler.cs"));
        var textEditorDragDrop = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "windows",
            "Documents",
            "TextEditorDragDrop.cs"));
        var textEditorContextMenu = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "windows",
            "Documents",
            "TextEditorContextMenu.cs"));
        var caretElement = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "windows",
            "Documents",
            "CaretElement.cs"));
        var safeNativeMethodsOther = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "Shared",
            "MS",
            "Win32",
            "SafeNativeMethodsOther.cs"));
        var safeNativeMethodsClr = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "Shared",
            "MS",
            "Win32",
            "SafeNativeMethodsCLR.cs"));
        var textServicesLoader = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "Shared",
            "MS",
            "Internal",
            "TextServicesLoader.cs"));
        var dataStreams = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "MS",
            "Internal",
            "DataStreams.cs"));
        var fontCacheUtil = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "MS",
            "Internal",
            "FontCache",
            "FontCacheUtil.cs"));
        var classification = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "MS",
            "Internal",
            "Classification.cs"));
        var cursor = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "Cursor.cs"));
        var lineServices = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "MS",
            "Internal",
            "TextFormatting",
            "LineServices.cs"));
        var textBoxLine = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "MS",
            "Internal",
            "documents",
            "TextBoxLine.cs"));
        var typeface = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Typeface.cs"));
        var uxThemeWrapper = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "MS",
            "Win32",
            "UxThemeWrapper.cs"));
        var windowBackdropManager = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Appearance",
            "WindowBackdropManager.cs"));
        var windowChromeWorker = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Shell",
            "WindowChromeWorker.cs"));
        var systemCommands = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "SystemCommands.cs"));
        var mimeTypeMapper = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "Shared",
            "MS",
            "Internal",
            "MimeTypeMapper.cs"));
        var application = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Application.cs"));
        var dispatcher = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WindowsBase",
            "System",
            "Windows",
            "Threading",
            "Dispatcher.cs"));
        var dpiAwareness = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "MS",
            "internal",
            "DpiUtil",
            "DpiUtil+DpiAwarenessContextHelper.cs"));
        var osVersionHelper = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "Shared",
            "System",
            "Windows",
            "Interop",
            "OSVersionHelper.cs"));
        var uiaCoreTypesApi = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "UIAutomation",
            "UIAutomationTypes",
            "MS",
            "Internal",
            "Automation",
            "UiaCoreTypesApi.cs"));
        var pointUtil = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "Shared",
            "MS",
            "Internal",
            "PointUtil.cs"));
        var inputElement = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Input",
            "InputElement.cs"));
        var uiElementAutomationPeer = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Automation",
            "Peers",
            "UIElementAutomationPeer.cs"));
        var uiElement3DAutomationPeer = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Automation",
            "Peers",
            "UIElement3DAutomationPeer.cs"));
        var genericRootAutomationPeer = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Automation",
            "Peers",
            "GenericRootAutomationPeer.cs"));
        var documentAutomationPeer = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Automation",
            "Peers",
            "DocumentAutomationPeer.cs"));
        var textElementAutomationPeer = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Automation",
            "Peers",
            "TextElementAutomationPeer.cs"));
        var windowAutomationPeer = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Automation",
            "Peers",
            "WindowAutomationPeer.cs"));
        var bmpBitmapDecoder = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Imaging",
            "BmpBitmapDecoder.cs"));
        var pngBitmapDecoder = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Imaging",
            "PngBitmapDecoder.cs"));
        var jpegBitmapDecoder = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Imaging",
            "JpegBitmapDecoder.cs"));
        var gifBitmapDecoder = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Imaging",
            "GifBitmapDecoder.cs"));
        var tiffBitmapDecoder = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Imaging",
            "TiffBitmapDecoder.cs"));
        var iconBitmapDecoder = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Imaging",
            "IconBitmapDecoder.cs"));
        var bitmapDecoder = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Imaging",
            "BitmapDecoder.cs"));
        var bitmapMetadata = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Imaging",
            "BitmapMetadata.cs"));
        var bitmapPalette = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Imaging",
            "BitmapPalette.cs"));

        AssertGuardBefore(compositionExports, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.MilCoreApi.EnterCompositionEngineLock()");
        Assert.Contains("CreateManagedPredefinedColors", bitmapPalette, StringComparison.Ordinal);
        Assert.Contains("InitializeManagedFromBitmapSource", bitmapPalette, StringComparison.Ordinal);
        Assert.Contains("ExtractManagedColors", bitmapPalette, StringComparison.Ordinal);
        Assert.Contains("AddRgbCube", bitmapPalette, StringComparison.Ordinal);
        AssertGuardBefore(bitmapPalette, "if (!OperatingSystem.IsWindows())", "_palette = CreateInternalPalette();");
        Assert.Contains("IReadOnlyDictionary<string, object> portableQueries", bitmapMetadata, StringComparison.Ordinal);
        Assert.Contains("_portableQueries = new Dictionary<string, object>(portableQueries, StringComparer.Ordinal);", bitmapMetadata, StringComparison.Ordinal);
        Assert.Contains("return _portableQueries.TryGetValue(query, out object value) ? value : null;", bitmapMetadata, StringComparison.Ordinal);
        Assert.Contains("return _portableQueries.Keys.GetEnumerator();", bitmapMetadata, StringComparison.Ordinal);
        Assert.Contains("return _portableQueries.ContainsKey(query);", bitmapMetadata, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryOpenPortableUriStream(", bitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("PackUriHelper.UriSchemePack", bitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("WpfWebRequestHelper.CreateRequestAndGetResponseStream(uri)", bitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("Uri.UriSchemeHttp", bitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("BitmapDecoder.TryOpenPortableUriStream(uri, out Stream stream)", bmpBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryCreatePortableFrame(", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryCreatePortableFrameFromUri(", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("BitmapDecoder.TryOpenPortableUriStream(uri, out Stream stream)", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("ZLibStream", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("PixelFormats.Bgra32", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("case 6:", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("private static bool IsSupportedBitDepth", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("ReadSample(source, sourceRow, x, 3, componentCount, bitDepth)", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("ScaleSampleToByte", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("IsTransparentRgb", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("DecodeAdam7Pixels", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("Adam7StartX", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("interlaceMethod > 1", pngBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("PngBitmapDecoder.TryCreatePortableFrameFromUri", bitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("return new PngBitmapDecoder(portablePngUriFrame", bitmapDecoder, StringComparison.Ordinal);
        AssertGuardBefore(bitmapDecoder, "PngBitmapDecoder.TryCreatePortableFrame", "SetupDecoderFromUriOrStream");
        Assert.Contains("internal static bool TryCreatePortableFrame(", jpegBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryCreatePortableFrameFromUri(", jpegBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("BitmapDecoder.TryOpenPortableUriStream(uri, out Stream stream)", jpegBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha)", jpegBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("PixelFormats.Bgra32", jpegBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("private static bool IsJpegSignature", jpegBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("JpegBitmapDecoder.TryCreatePortableFrameFromUri", bitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("return new JpegBitmapDecoder(portableJpegUriFrame", bitmapDecoder, StringComparison.Ordinal);
        AssertGuardBefore(bitmapDecoder, "JpegBitmapDecoder.TryCreatePortableFrame", "SetupDecoderFromUriOrStream");
        Assert.Contains("internal static bool TryCreatePortableFrame(", gifBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryCreatePortableFrames(", gifBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryCreatePortableFrameFromUri(", gifBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("BitmapDecoder.TryOpenPortableUriStream(uri, out Stream stream)", gifBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("TryCreatePortableFramesFromUri", gifBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("DecodeLzwIndices", gifBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("ReadGraphicControl", gifBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("ApplyPendingDisposal", gifBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("Deinterlace", gifBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("PixelFormats.Bgra32", gifBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("private static bool IsGifSignature", gifBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("GifBitmapDecoder.TryCreatePortableFramesFromUri", bitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("return new GifBitmapDecoder(portableGifUriFrames", bitmapDecoder, StringComparison.Ordinal);
        AssertGuardBefore(bitmapDecoder, "GifBitmapDecoder.TryCreatePortableFrames", "SetupDecoderFromUriOrStream");
        Assert.Contains("internal static bool TryCreatePortableFrame(", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryCreatePortableFrames(", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("TryCreatePortableFramesFromUri", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("BitmapDecoder.TryOpenPortableUriStream(uri, out Stream stream)", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("NextIfdOffset", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("MaxPortableFrameCount", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryCreatePortableFrameFromUri(", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("Portable TIFF decoding currently supports uncompressed chunky first-frame images.", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("CopyTiffRowToBgra", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("CopyPaletteTiffRowToBgra", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("PhotometricInterpretationTag", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("ColorMapTag", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("OrientationTag", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("CreateFrameMetadata(directory)", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("queries[\"/ifd/{ushort=274}\"]", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("ConvertTiffMetadataValue(directory.Orientation)", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("return value <= ushort.MaxValue ? (object)(ushort)value : value;", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("Portable TIFF palette decoding currently supports 1, 2, 4, and 8-bit indices.", tiffBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("TiffBitmapDecoder.TryCreatePortableFramesFromUri", bitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("ReadOnlyCollection<BitmapFrame> portableTiffFrames", bitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("return new TiffBitmapDecoder(portableTiffUriFrames", bitmapDecoder, StringComparison.Ordinal);
        AssertGuardBefore(bitmapDecoder, "TiffBitmapDecoder.TryCreatePortableFrame", "SetupDecoderFromUriOrStream");
        Assert.Contains("internal static bool TryCreatePortableFrame(", iconBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryCreatePortableFrameFromUri(", iconBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("BitmapDecoder.TryOpenPortableUriStream(uri, out Stream stream)", iconBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("PngBitmapDecoder.TryCreatePortableFrame(imageStream", iconBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("TryCreateDibFrame(imageBytes", iconBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("ReadPackedIndex", iconBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("IsMaskBitSet", iconBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("private static bool IsPngSignature", iconBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("Portable ICO decoding currently supports PNG-backed and BI_RGB DIB icon images.", iconBitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("IconBitmapDecoder.TryCreatePortableFrameFromUri", bitmapDecoder, StringComparison.Ordinal);
        Assert.Contains("return new IconBitmapDecoder(portableIconUriFrame", bitmapDecoder, StringComparison.Ordinal);
        AssertGuardBefore(bitmapDecoder, "IconBitmapDecoder.TryCreatePortableFrame", "SetupDecoderFromUriOrStream");
        AssertGuardBefore(uiElement, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.GetDC(desktopWnd)");
        AssertGuardBefore(pathGeometry, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.MilCoreApi.MilUtility_PathGeometryBounds");
        AssertGuardBefore(geometry, "if (!OperatingSystem.IsWindows())", "MilCoreApi.MilUtility_PolygonBounds");
        Assert.Contains("return GetManagedPolygonBounds(", geometry, StringComparison.Ordinal);
        Assert.Contains("private static unsafe Rect GetManagedPolygonBounds", geometry, StringComparison.Ordinal);
        Assert.Contains("private static MilRectD GetManagedPathBoundsAsRB", pathGeometry, StringComparison.Ordinal);
        Assert.Contains("ParsePathGeometryData(pathData, context)", pathGeometry, StringComparison.Ordinal);
        Assert.Contains("PathStreamGeometryContext", pathGeometry, StringComparison.Ordinal);
        Assert.Contains("if (!OperatingSystem.IsWindows())", rectangleGeometry, StringComparison.Ordinal);
        Assert.Contains("FillContainsManaged(rect, radiusX, radiusY, hitPoint)", rectangleGeometry, StringComparison.Ordinal);
        Assert.Contains("StrokeContainsManaged(rect, radiusX, radiusY, pen, hitPoint, tolerance, type)", rectangleGeometry, StringComparison.Ordinal);
        Assert.Contains("GeneralTransform inverse = transform.Inverse", rectangleGeometry, StringComparison.Ordinal);
        Assert.Contains("return OperatingSystem.IsWindows() && !CoreAppContextSwitches.DisableStylusAndTouchSupport", stylusLogic, StringComparison.Ordinal);
        AssertGuardBefore(stylusLogic, "if (!OperatingSystem.IsWindows())", "Registry.CurrentUser.OpenSubKey");
        AssertGuardBefore(accessKeyManager, "if (!global::System.OperatingSystem.IsWindows())", "UnsafeNativeMethods.GetActiveWindow()");
        Assert.Contains("PresentationSource.CriticalCurrentSources", accessKeyManager, StringComparison.Ordinal);
        Assert.Contains("GetPortableActiveSource()", accessKeyManager, StringComparison.Ordinal);
        Assert.Contains("_portableCurrentInputLanguage = CultureInfo.CurrentCulture", inputLanguageSource, StringComparison.Ordinal);
        Assert.Contains("return new CultureInfo[1] { CurrentInputLanguage }", inputLanguageSource, StringComparison.Ordinal);
        AssertGuardBefore(inputLanguageSource, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetKeyboardLayout(0)");
        AssertGuardBefore(inputLanguageSource, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetCurrentThreadId()");
        AssertGuardBefore(inputLanguageSource, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetKeyboardLayoutList(0, null)");
        AssertGuardBefore(inputLanguageSource, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetKeyboardLayout(_dispatcherThreadId)");
        AssertGuardBefore(inputLanguageManager, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetKeyboardLayout(0)");
        AssertGuardBefore(inputLanguageManager, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetKeyboardLayoutList(0, null)");
        Assert.Contains("private InputMethodState _portableImeState = InputMethodState.Off", inputMethod, StringComparison.Ordinal);
        Assert.Contains("private ImeConversionModeValues _portableImeConversionMode = ImeConversionModeValues.Alphanumeric", inputMethod, StringComparison.Ordinal);
        Assert.Contains("private ImeSentenceModeValues _portableImeSentenceMode = ImeSentenceModeValues.None", inputMethod, StringComparison.Ordinal);
        Assert.Contains("SetPortableInputMethodState(ref _portableImeState, value, InputMethodStateType.ImeState)", inputMethod, StringComparison.Ordinal);
        Assert.Contains("RaisePortableInputMethodStateChanged(stateType)", inputMethod, StringComparison.Ordinal);
        AssertGuardBefore(inputMethod, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetKeyboardLayout(0)");
        AssertGuardBefore(inputMethod, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.ImmGetContext(new HandleRef(this, hwnd))");
        AssertGuardBefore(inputMethod, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.ImmGetDefaultIMEWnd");
        AssertGuardBefore(tipTsfHelper, "if (!OperatingSystem.IsWindows())", "InputPane.GetForWindow(GetHwndSource(focusedObject))");
        Assert.Contains("internal static void Hide(DependencyObject focusedObject)\n        {\n            if (!OperatingSystem.IsWindows())", tipTsfHelper, StringComparison.Ordinal);
        AssertGuardBefore(systemResources, "if (!OperatingSystem.IsWindows())", "new HwndWrapper(");
        AssertGuardBefore(systemResources, "if (OperatingSystem.IsWindows())", "XamlAccessLevel.AssemblyAccessTo(assembly)");
        AssertGuardBefore(xamlReader, "if (internalTypeHelper != null && OperatingSystem.IsWindows())", "XamlAccessLevel.AssemblyAccessTo(streamInfo.Assembly)");
        AssertGuardBefore(popupControlService, "if (!OperatingSystem.IsWindows()", "MS.Win32.SafeNativeMethods.GetCapture()");
        AssertGuardBefore(comboBox, "(!OperatingSystem.IsWindows()", "MS.Win32.SafeNativeMethods.GetCapture()");
        Assert.Contains("PresentationSource source = PresentationSource.CriticalFromVisual(itemsHost);", comboBox, StringComparison.Ordinal);
        Assert.Contains("CompositionTarget compositionTarget = source?.CompositionTarget;", comboBox, StringComparison.Ordinal);
        Assert.DoesNotContain("HwndSource source = PresentationSource.CriticalFromVisual(itemsHost) as HwndSource;", comboBox, StringComparison.Ordinal);
        AssertGuardBefore(popup, "(!OperatingSystem.IsWindows()", "MS.Win32.SafeNativeMethods.GetCapture()");
        Assert.Contains("return GetPresentationSourceRootRect();", popup, StringComparison.Ordinal);
        Assert.Contains("private Rect GetPresentationSourceRootRect()", popup, StringComparison.Ordinal);
        Assert.Contains("if (!OperatingSystem.IsWindows())\n                {\n                    return;\n                }\n\n                int flags = NativeMethods.SWP_NOZORDER", popup, StringComparison.Ordinal);
        Assert.Contains("return false;\n                }\n\n                IntPtr foregroundWindow", popup, StringComparison.Ordinal);
        Assert.Contains("return IntPtr.Zero;\n                }\n\n                if (hwnd != null)", popup, StringComparison.Ordinal);
        AssertGuardBefore(menuBase, "(!OperatingSystem.IsWindows()", "MS.Win32.SafeNativeMethods.GetCapture()");
        AssertGuardBefore(menu, "if (OperatingSystem.IsWindows())", "PresentationSource.CriticalFromVisual(this) as System.Windows.Interop.HwndSource");
        Assert.Contains("else\n                {\n                    e.Handled = true;\n                }", menu, StringComparison.Ordinal);
        AssertGuardBefore(menuBase, "if (OperatingSystem.IsWindows())", "MS.Win32.UnsafeNativeMethods.GetFocus()");
        Assert.Contains("AreComponentResourceUrisEquivalent(loadBamlSyncInfo.BamlUri, curComponentUri)", application, StringComparison.Ordinal);
        Assert.Contains("BaseUriHelper.GetAssemblyNameAndPart(", application, StringComparison.Ordinal);
        Assert.Contains("AreComponentPartNamesEquivalent(firstPartName, secondPartName)", application, StringComparison.Ordinal);
        Assert.Contains("IsXamlBamlExtensionPair(firstExtension, secondExtension)", application, StringComparison.Ordinal);
        Assert.Contains("Path.ChangeExtension(firstPartName, null)", application, StringComparison.Ordinal);
        Assert.Contains("AreOptionalComponentAssemblyPartsCompatible(firstAssemblyVersion, secondAssemblyVersion)", application, StringComparison.Ordinal);
        Assert.Contains("IsResourceAssemblyName", application, StringComparison.Ordinal);
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETFOCUSBORDERWIDTH");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETFOCUSBORDERHEIGHT");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETHIGHCONTRAST");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETMOUSEVANISH");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETDROPSHADOW");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETFLATMENU");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETMENUDROPALIGNMENT");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETMENUFADE");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETMENUSHOWDELAY");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETCOMBOBOXANIMATION");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETCLIENTAREAANIMATION");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETCURSORSHADOW");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETGRADIENTCAPTIONS");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETHOTTRACKING");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETLISTBOXSMOOTHSCROLLING");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETMENUANIMATION");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETSELECTIONFADE");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETSTYLUSHOTTRACKING");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETTOOLTIPANIMATION");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETTOOLTIPFADE");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETUIEFFECTS");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETANIMATION");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETBORDER");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETDRAGFULLWINDOWS");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETFOREGROUNDFLASHCOUNT");
        Assert.Contains("private const int DefaultScrollBarMetric = 17", systemParameters, StringComparison.Ordinal);
        Assert.Contains("private const int DefaultMenuShowDelay = 400", systemParameters, StringComparison.Ordinal);
        Assert.Contains("private const int DefaultForegroundFlashCount = 7", systemParameters, StringComparison.Ordinal);
        Assert.Contains("private const int DefaultFocusBorderMetric = 1", systemParameters, StringComparison.Ordinal);
        Assert.Contains("private const int DefaultPrimaryScreenWidth = 1024", systemParameters, StringComparison.Ordinal);
        Assert.Contains("private const int DefaultPrimaryScreenHeight = 768", systemParameters, StringComparison.Ordinal);
        Assert.Contains("private static double GetSystemMetricPixel(SM metric, int fallbackPixel)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsWindows()", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_focusBorderWidth = ConvertPixel(DefaultFocusBorderMetric)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_focusBorderHeight = ConvertPixel(DefaultFocusBorderMetric)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_focusHorizontalBorderHeight = GetSystemMetricPixel(SM.CXFOCUSBORDER, DefaultFocusBorderMetric)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_focusVerticalBorderWidth = GetSystemMetricPixel(SM.CYFOCUSBORDER, DefaultFocusBorderMetric)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_primaryScreenWidth = GetSystemMetricPixel(SM.CXSCREEN, DefaultPrimaryScreenWidth)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_primaryScreenHeight = GetSystemMetricPixel(SM.CYSCREEN, DefaultPrimaryScreenHeight)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_verticalScrollBarWidth = GetSystemMetricPixel(SM.CXVSCROLL, DefaultScrollBarMetric)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_horizontalScrollBarHeight = GetSystemMetricPixel(SM.CYHSCROLL, DefaultScrollBarMetric)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_caretWidth = 1.0", systemParameters, StringComparison.Ordinal);
        Assert.Contains("private static int GetDefaultSystemMetric(SM metric)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("private static int GetStandardSystemMetric(Standard.SM metric)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("SM.CXMAXTRACK => DefaultPrimaryScreenWidth", systemParameters, StringComparison.Ordinal);
        Assert.Contains("SM.CYCAPTION => 23", systemParameters, StringComparison.Ordinal);
        Assert.Contains("SM.MOUSEPRESENT => 1", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_powerLineStatus = PowerLineStatus.Unknown", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_dpiX = 96", systemParameters, StringComparison.Ordinal);
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.GetSystemPowerStatus(ref status)");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.GetDC(desktopWnd)");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETCARETWIDTH");
        Assert.Contains("InputLanguageManager.Current.AvailableInputLanguages", textSelection, StringComparison.Ordinal);
        AssertGuardBefore(textSelection, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetKeyboardLayoutList(0, null)");
        AssertGuardBefore(textSelection, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.GetLocaleInfoW");
        Assert.Contains("return cultureInfo.TextInfo.IsRightToLeft", textSelection, StringComparison.Ordinal);
        AssertGuardBefore(textEditorTyping, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.ShowCursor(true)");
        AssertGuardBefore(textEditorTyping, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.PeekMessage");
        AssertGuardBefore(textEditorTyping, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.ShowCursor(false)");
        AssertGuardBefore(selectionWordBreaker, "if (OperatingSystem.IsWindows())", "SafeNativeMethods.GetStringTypeEx");
        Assert.Contains("private static UInt16 GetPortableCharType1(char ch)", selectionWordBreaker, StringComparison.Ordinal);
        Assert.Contains("private static UInt16 GetPortableCharType3(char ch)", selectionWordBreaker, StringComparison.Ordinal);
        Assert.Contains("CharUnicodeInfo.GetUnicodeCategory(ch)", selectionWordBreaker, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeNativeMethods.GetStringTypeEx", textFindEngine, StringComparison.Ordinal);
        Assert.Contains("SelectionWordBreaker.IsBlankOrWhiteSpaceCharacter", textFindEngine, StringComparison.Ordinal);
        AssertGuardBefore(winEventHandler, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SetWinEventHook");
        AssertGuardBefore(winEventHandler, "if (OperatingSystem.IsWindows())", "UnsafeNativeMethods.UnhookWinEvent");
        AssertGuardBefore(moveSizeWinEventHandler, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.GetParent");
        Assert.Contains("return window.IsEnabled", textEditorDragDrop, StringComparison.Ordinal);
        AssertGuardBefore(textEditorDragDrop, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.IsWindowEnabled");
        AssertGuardBefore(textEditorDragDrop, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SetForegroundWindow");
        Assert.Contains("ClipToPresentationSourceRoot(source, This.UiScope", textEditorContextMenu, StringComparison.Ordinal);
        AssertGuardBefore(textEditorContextMenu, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetClientRect");
        AssertGuardBefore(caretElement, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.CreateBitmap");
        AssertGuardBefore(caretElement, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.DestroyCaret()");
        AssertGuardBefore(caretElement, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.SetCaretPos");
        Assert.Contains("source is IWin32Window win32Window", caretElement, StringComparison.Ordinal);
        AssertGuardBefore(safeNativeMethodsOther, "if (!OperatingSystem.IsWindows())", "SafeNativeMethodsPrivate.GetCaretBlinkTime()");
        AssertGuardBefore(safeNativeMethodsClr, "if (!OperatingSystem.IsWindows())", "SafeNativeMethodsPrivate.GetTickCount()");
        Assert.Contains("return Environment.TickCount;", safeNativeMethodsClr, StringComparison.Ordinal);
        AssertGuardBefore(textServicesLoader, "if (!OperatingSystem.IsWindows())", "Invariant.Assert(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA");
        Assert.Contains("return null;", textServicesLoader, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Private.Windows.BinaryFormat", dataStreams, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Private.Windows.Core", dataStreams, StringComparison.Ordinal);
        Assert.Contains("this.Formatter.Serialize(byteStream, currentValue)", dataStreams, StringComparison.Ordinal);
        Assert.Contains("private static bool IsNativePtsFormatterAvailable", flowDocumentView, StringComparison.Ordinal);
        Assert.Contains("global::System.OperatingSystem.IsWindows()", flowDocumentView, StringComparison.Ordinal);
        Assert.Contains("if (!IsNativePtsFormatterAvailable)", flowDocumentView, StringComparison.Ordinal);
        Assert.Contains("return null;", flowDocumentView, StringComparison.Ordinal);
        Assert.Contains("new DocumentPageTextView(this, _document.StructuralCache.TextContainer)", flowDocumentView, StringComparison.Ordinal);
        Assert.True(
            flowDocumentView.IndexOf("if (!IsNativePtsFormatterAvailable)", StringComparison.Ordinal)
                < flowDocumentView.IndexOf("new DocumentPageTextView(this, _document.StructuralCache.TextContainer)", StringComparison.Ordinal),
            "FlowDocumentView must skip native PTS text-view creation before constructing DocumentPageTextView on non-Windows.");
        AssertGuardBefore(fontCacheUtil, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.CreateFile(");
        Assert.Contains("OpenManagedFile(fileName)", fontCacheUtil, StringComparison.Ordinal);
        Assert.Contains("File.ReadAllBytes(fileName)", fontCacheUtil, StringComparison.Ordinal);
        AssertGuardBefore(cursor, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.LoadImageCursor");
        Assert.Contains("LoadPortableCursorFallback()", cursor, StringComparison.Ordinal);
        Assert.Contains("_cursorType = CursorType.Arrow", cursor, StringComparison.Ordinal);
        AssertGuardBefore(classification, "if (OperatingSystem.IsWindows())", "MILGetClassificationTables(out ct)");
        Assert.Contains("GetManagedUnicodeClass", classification, StringComparison.Ordinal);
        Assert.Contains("ManagedCharAttributeOf", classification, StringComparison.Ordinal);
        Assert.Contains("case UnicodeCategory.PrivateUse:", classification, StringComparison.Ordinal);
        Assert.Contains("return ManagedPrivateUseClass;", classification, StringComparison.Ordinal);
        Assert.Contains("ManagedPrivateUseClass => CreateManagedAttribute", classification, StringComparison.Ordinal);
        AssertGuardBefore(lineServices, "if (OperatingSystem.IsWindows())", "LoGetEscStringImpl(ref escStringInfo)");
        Assert.Contains("s_managedObjectReplacement", lineServices, StringComparison.Ordinal);
        Assert.Contains("native LineServices fallback is unavailable in the portable bring-up", textBoxLine, StringComparison.Ordinal);
        Assert.Contains("!global::System.OperatingSystem.IsWindows() ||", textBoxLine, StringComparison.Ordinal);
        Assert.Contains("lineProperties.TextAlignment != TextAlignment.Justify", textBoxLine, StringComparison.Ordinal);
        Assert.Contains("The full WPF LineServices path is still native.", typeface, StringComparison.Ordinal);
        Assert.Contains("ContainsOnlyPrivateUseCharacters", typeface, StringComparison.Ordinal);
        Assert.Contains("IsPrivateUseCharacter", typeface, StringComparison.Ordinal);
        Assert.Contains("the existing null-glyph unshaped GlyphRun path", typeface, StringComparison.Ordinal);
        AssertGuardBefore(typeface, "if (!OperatingSystem.IsWindows())", "TypographyAvailabilities typography");
        AssertGuardBefore(uxThemeWrapper, "_themeState = OperatingSystem.IsWindows()", "SafeNativeMethods.IsUxThemeActive()");
        Assert.Contains("new ThemeState(true, \"Aero2\", \"NormalColor\")", uxThemeWrapper, StringComparison.Ordinal);
        AssertGuardBefore(windowBackdropManager, "if (!OperatingSystem.IsWindows())", "new WindowInteropHelper(window).Handle");
        AssertGuardBefore(windowBackdropManager, "if (!OperatingSystem.IsWindows())", "NativeMethods.DwmSetWindowAttributeSystemBackdropType");
        Assert.Contains("OperatingSystem.IsWindows() &&\n                                                                        Utility.IsWindows11_22H2OrNewer", windowBackdropManager, StringComparison.Ordinal);
        AssertGuardBefore(windowChromeWorker, "if (!OperatingSystem.IsWindows())", "new WindowInteropHelper(_window).Handle");
        AssertGuardBefore(windowChromeWorker, "if (!OperatingSystem.IsWindows())", "HwndSource.FromHwnd(_hwnd)");
        AssertGuardBefore(windowChromeWorker, "if (!OperatingSystem.IsWindows() || IntPtr.Zero == _hwnd || _hwndSource == null || _hwndSource.IsDisposed)", "NativeMethods.DwmIsCompositionEnabled()");
        AssertGuardBefore(windowChromeWorker, "if (!OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero || _hwndSource == null)", "_hwndSource.RemoveHook(_WndProc)");
        Assert.Contains("private void _ApplyPortableCustomChrome()", windowChromeWorker, StringComparison.Ordinal);
        Assert.Contains("if (_chromeInfo == null || _window == null)", windowChromeWorker, StringComparison.Ordinal);
        AssertGuardBefore(systemCommands, "if (!OperatingSystem.IsWindows())", "new WindowInteropHelper(window).Handle");
        AssertGuardBefore(systemCommands, "if (!OperatingSystem.IsWindows())", "NativeMethods.GetSystemMenu(hwnd, false)");
        Assert.Contains("window.WindowState = WindowState.Maximized", systemCommands, StringComparison.Ordinal);
        Assert.Contains("window.WindowState = WindowState.Minimized", systemCommands, StringComparison.Ordinal);
        Assert.Contains("window.WindowState = WindowState.Normal", systemCommands, StringComparison.Ordinal);
        Assert.Contains("GetPortableMimeTypeFromExtension", mimeTypeMapper, StringComparison.Ordinal);
        Assert.Contains("if (OperatingSystem.IsWindows())", mimeTypeMapper, StringComparison.Ordinal);
        AssertGuardBefore(mimeTypeMapper, "if (OperatingSystem.IsWindows())", "GetMimeTypeFromUrlMon(uriSource)");
        Assert.Contains("\"txt\" => TextPlainMime", mimeTypeMapper, StringComparison.Ordinal);
        Assert.Contains("_ => OctetMime", mimeTypeMapper, StringComparison.Ordinal);
        AssertGuardBefore(dpiAwareness, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetWindowDpiAwarenessContext(hWnd)");
        AssertGuardBefore(osVersionHelper, "if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))", "IsWindows10RS5OrGreater()");
        AssertGuardBefore(osVersionHelper, "return OperatingSystemVersion.WindowsXPSP2;", "throw new Exception(\"OSVersionHelper.GetOsVersion Could not detect OS!\")");
        AssertGuardBefore(uiaCoreTypesApi, "if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))", "RawUiaGetReservedNotSupportedValue(out notSupportedValue)");
        AssertGuardBefore(uiaCoreTypesApi, "if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))", "RawUiaGetReservedMixedAttributeValue(out mixedAttributeValue)");
        AssertGuardBefore(uiaCoreTypesApi, "if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))", "LoadLibraryHelper.SecureLoadLibraryEx(DllImport.UIAutomationCore");
        Assert.Contains("s_reservedNotSupportedValue", uiaCoreTypesApi, StringComparison.Ordinal);
        Assert.Contains("s_reservedMixedAttributeValue", uiaCoreTypesApi, StringComparison.Ordinal);
        Assert.Contains("internal static Rect ClientToScreen(Rect rectClient, PresentationSource presentationSource)", pointUtil, StringComparison.Ordinal);
        Assert.Contains("Point corner1 = ClientToScreen(rectClient.TopLeft, presentationSource);", pointUtil, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientToScreen(Rect rectClient, HwndSource hwndSource)", pointUtil, StringComparison.Ordinal);
        Assert.Contains("PresentationSource sourceFrom = PresentationSource.CriticalFromVisual(rootFrom);", inputElement, StringComparison.Ordinal);
        Assert.Contains("PresentationSource sourceTo = PresentationSource.CriticalFromVisual(rootTo);", inputElement, StringComparison.Ordinal);
        Assert.Contains("PointUtil.ClientToScreen(ptTranslated, sourceFrom)", inputElement, StringComparison.Ordinal);
        Assert.Contains("PointUtil.ScreenToClient(ptScreen, sourceTo)", inputElement, StringComparison.Ordinal);
        Assert.DoesNotContain("PresentationSource.CriticalFromVisual(rootFrom) as HwndSource", inputElement, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceFrom.Handle", inputElement, StringComparison.Ordinal);
        Assert.Contains("PointUtil.ClientToScreen(rectClient, presentationSource)", uiElementAutomationPeer, StringComparison.Ordinal);
        Assert.Contains("PointUtil.ClientToScreen(rectClient, presentationSource)", uiElement3DAutomationPeer, StringComparison.Ordinal);
        AssertGuardBefore(genericRootAutomationPeer, "if(name == string.Empty && OperatingSystem.IsWindows())", "UnsafeNativeMethods.GetWindowText");
        AssertGuardBefore(genericRootAutomationPeer, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetWindowRect");
        Assert.Contains("return base.GetBoundingRectangleCore();", genericRootAutomationPeer, StringComparison.Ordinal);
        Assert.Contains("PointUtil.ClientToScreen(boundingRect, presentationSource)", documentAutomationPeer, StringComparison.Ordinal);
        Assert.Contains("PointUtil.ClientToScreen(rectClient, presentationSource)", textElementAutomationPeer, StringComparison.Ordinal);
        Assert.DoesNotContain("presentationSource as HwndSource", uiElementAutomationPeer, StringComparison.Ordinal);
        Assert.DoesNotContain("presentationSource as HwndSource", uiElement3DAutomationPeer, StringComparison.Ordinal);
        Assert.DoesNotContain("as HwndSource", documentAutomationPeer, StringComparison.Ordinal);
        Assert.DoesNotContain("presentationSource as HwndSource", textElementAutomationPeer, StringComparison.Ordinal);
        AssertGuardBefore(windowAutomationPeer, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetWindowRect");
        Assert.Contains("return GetPortableBoundingRectangle(window);", windowAutomationPeer, StringComparison.Ordinal);
        Assert.Contains("PresentationSource.CriticalFromVisual(window)", windowAutomationPeer, StringComparison.Ordinal);
        AssertGuardBefore(application, "if (!global::System.OperatingSystem.IsWindows())", "UnsafeNativeMethods.PlaySound(soundFile");
        AssertGuardBefore(application, "if (!global::System.OperatingSystem.IsWindows())", "Registry.CurrentUser.OpenSubKey(regPath)");
        AssertGuardBefore(application, "if (!WindowsInternal.HasItem(wnd))", "wnd.Visibility = Visibility.Visible");
        AssertGuardBefore(application, "if (MainWindow == null)", "wnd.Visibility = Visibility.Visible");
        Assert.Contains("_useWin32MessagePump = OperatingSystem.IsWindows();", dispatcher, StringComparison.Ordinal);
        AssertGuardBefore(dispatcher, "if (_useWin32MessagePump)", "new MessageOnlyHwndWrapper()");
        AssertGuardBefore(dispatcher, "if (!_useWin32MessagePump)", "MSG msg = new MSG()");
        Assert.Contains("PushManagedFrameImpl(frame)", dispatcher, StringComparison.Ordinal);
        Assert.Contains("while(frame.Continue || HasPendingManagedOperation())", dispatcher, StringComparison.Ordinal);
        Assert.Contains("HasPendingManagedOperation()", dispatcher, StringComparison.Ordinal);
        AssertGuardBefore(dispatcher, "if (!_useWin32MessagePump)", "UnsafeNativeMethods.MsgWaitForMultipleObjectsEx");
        Assert.Contains("return !_useWin32MessagePump;", dispatcher, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("src/Microsoft.DotNet.Wpf/src/System.Xaml/System.Xaml.csproj")]
    [InlineData("src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/PresentationBuildTasks.csproj")]
    [InlineData("src/Microsoft.DotNet.Wpf/src/PresentationFramework/PresentationFramework.csproj")]
    [InlineData("src/Microsoft.DotNet.Wpf/src/PresentationUI/PresentationUI.csproj")]
    [InlineData("src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/PresentationFramework.Fluent.csproj")]
    public void ManagedWpfSubsystemProjectsDoNotReferenceProGpuBridge(string relativeProjectPath)
    {
        var projectPath = FindRepoPath(relativeProjectPath.Split('/'));
        var project = File.ReadAllText(projectPath);

        Assert.DoesNotContain("ProGPU.Wpf", project, StringComparison.Ordinal);
        Assert.DoesNotContain(@"external\ProGPU", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGPU.Scene", project, StringComparison.Ordinal);
    }

    [Fact]
    public void ProGpuWpfSdkProvidesSwitchOnlyPackagingSurface()
    {
        var sdkProjectPath = FindRepoPath(
            "packaging",
            "ProGPU.Wpf.Sdk",
            "ProGPU.Wpf.Sdk.ArchNeutral.csproj");
        var sdkPropsPath = FindRepoPath(
            "packaging",
            "ProGPU.Wpf.Sdk",
            "Sdk",
            "Sdk.props");
        var sdkTargetsPath = FindRepoPath(
            "packaging",
            "ProGPU.Wpf.Sdk",
            "Sdk",
            "Sdk.targets");
        var portablePropsPath = FindRepoPath(
            "packaging",
            "ProGPU.Wpf.Sdk",
            "targets",
            "ProGPU.Wpf.Sdk.props");
        var portableTargetsPath = FindRepoPath(
            "packaging",
            "ProGPU.Wpf.Sdk",
            "targets",
            "ProGPU.Wpf.Sdk.targets");
        var portableBootstrapPath = FindRepoPath(
            "packaging",
            "ProGPU.Wpf.Sdk",
            "targets",
            "ProGPU.Wpf.Sdk.PortableBootstrap.cs");
        var smokeProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "ProGPU.Wpf.SdkSwitchSmoke.csproj");
        var smokeDirectoryBuildPropsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "Directory.Build.props");
        var libraryProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchLibrary",
            "ProGPU.Wpf.SdkSwitchLibrary.csproj");
        var libraryDirectoryBuildPropsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchLibrary",
            "Directory.Build.props");
        var libraryNuGetConfigPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchLibrary",
            "NuGet.config");
        var libraryPanelXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchLibrary",
            "LibraryPanel.xaml");
        var libraryPanelCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchLibrary",
            "LibraryPanel.xaml.cs");
        var smokeNuGetConfigPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "NuGet.config");
        var smokeAppXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "App.xaml");
        var smokeAppCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "App.xaml.cs");
        var smokeResourcesXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "SmokeResources.xaml");
        var smokeMainWindowXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "MainWindow.xaml");
        var smokeMainWindowCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "MainWindow.xaml.cs");
        var smokePanelXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "SmokePanel.xaml");
        var smokePanelCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "SmokePanel.xaml.cs");
        var smokePageXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "SmokePage.xaml");
        var smokePageCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "SmokePage.xaml.cs");
        var smokeSecondPageXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "SmokeSecondPage.xaml");
        var smokeSecondPageCodeBehindPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "SmokeSecondPage.xaml.cs");
        var smokeItemDisplayConverterPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "SmokeItemDisplayConverter.cs");
        var smokeThemedControlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "SmokeThemedControl.cs");
        var smokeGenericThemeXamlPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "Themes",
            "Generic.xaml");
        var smokeAssemblyInfoPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchSmoke",
            "Properties",
            "AssemblyInfo.cs");
        var proGpuWpfProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGPU.Wpf.csproj");
        var proGpuWpfDirectoryBuildPropsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Directory.Build.props");
        var proGpuWpfAssemblyInfoPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Properties",
            "AssemblyInfo.cs");
        var proGpuWpfTestsProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.Tests",
            "ProGPU.Wpf.Tests.csproj");
        var proGpuWpfCommandSinkPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "ProGpuCompositionCommandSink.cs");
        var wpfCompositionDrawingContextPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "WpfCompositionDrawingContext.cs");
        var wpfObjectRenderDataDrawingContextPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "WpfObjectRenderDataDrawingContext.cs");
        var wpfReflectionDrawingReplayPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "Mil",
            "WpfReflectionDrawingReplay.cs");
        var wpfPortableCommandSinkBridgePath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "Mil",
            "WpfPortableCommandSinkBridge.cs");
        var proGpuWpfDrawingFramePath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfDrawingFrame.cs");
        var proGpuWpfWindowHostPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfWindowHost.cs");
        var wpfCompositionDrawingContextTestsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.Tests",
            "Composition",
            "WpfCompositionDrawingContextTests.cs");
        var wpfReflectionResourceResolverTestsPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.Tests",
            "Composition",
            "Mil",
            "WpfReflectionResourceResolverTests.cs");
        var wpfMilRenderDataDecoderPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "Mil",
            "WpfMilRenderDataDecoder.cs");
        var wpfReflectionResourceResolverPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "Mil",
            "WpfReflectionResourceResolver.cs");
        var wpfTransportTargetsPath = FindRepoPath(
            "packaging",
            "Microsoft.DotNet.Wpf.GitHub",
            "Directory.Build.targets");
        var wpfTransportArchNeutralProjectPath = FindRepoPath(
            "packaging",
            "Microsoft.DotNet.Wpf.GitHub",
            "Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj");
        var runtimeHarnessProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchRuntimeHarness",
            "ProGPU.Wpf.SdkSwitchRuntimeHarness.csproj");
        var runtimeHarnessProgramPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkSwitchRuntimeHarness",
            "Program.cs");
        var externalSdkHarnessProjectPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkExternalSmokeHarness",
            "ProGPU.Wpf.SdkExternalSmokeHarness.csproj");
        var externalSdkHarnessProgramPath = FindRepoPath(
            "src",
            "ProGPU.Wpf.SdkExternalSmokeHarness",
            "Program.cs");
        var spellerInteropBasePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Documents",
            "SpellerInteropBase.cs");
        var sdkCiScriptPath = FindRepoPath(
            "eng",
            "progpu-wpf-sdk-ci.sh");
        var sdkCiWorkflowPath = FindRepoPath(
            ".github",
            "workflows",
            "progpu-wpf-sdk.yml");

        var sdkProject = XDocument.Load(sdkProjectPath);
        var sdkProps = File.ReadAllText(sdkPropsPath);
        var sdkTargets = File.ReadAllText(sdkTargetsPath);
        var portableProps = File.ReadAllText(portablePropsPath);
        var portableTargets = File.ReadAllText(portableTargetsPath);
        var portableBootstrap = File.ReadAllText(portableBootstrapPath);
        var smokeProject = File.ReadAllText(smokeProjectPath);
        var smokeDirectoryBuildProps = File.ReadAllText(smokeDirectoryBuildPropsPath);
        var libraryProject = File.ReadAllText(libraryProjectPath);
        var libraryDirectoryBuildProps = File.ReadAllText(libraryDirectoryBuildPropsPath);
        var libraryNuGetConfig = File.ReadAllText(libraryNuGetConfigPath);
        var libraryPanelXaml = File.ReadAllText(libraryPanelXamlPath);
        var libraryPanelCodeBehind = File.ReadAllText(libraryPanelCodeBehindPath);
        var smokeNuGetConfig = File.ReadAllText(smokeNuGetConfigPath);
        var smokeAppXaml = File.ReadAllText(smokeAppXamlPath);
        var smokeAppCodeBehind = File.ReadAllText(smokeAppCodeBehindPath);
        var smokeResourcesXaml = File.ReadAllText(smokeResourcesXamlPath);
        var smokeMainWindowXaml = File.ReadAllText(smokeMainWindowXamlPath);
        var smokeMainWindowCodeBehind = File.ReadAllText(smokeMainWindowCodeBehindPath);
        var smokePanelXaml = File.ReadAllText(smokePanelXamlPath);
        var smokePanelCodeBehind = File.ReadAllText(smokePanelCodeBehindPath);
        var smokePageXaml = File.ReadAllText(smokePageXamlPath);
        var smokePageCodeBehind = File.ReadAllText(smokePageCodeBehindPath);
        var smokeSecondPageXaml = File.ReadAllText(smokeSecondPageXamlPath);
        var smokeSecondPageCodeBehind = File.ReadAllText(smokeSecondPageCodeBehindPath);
        var smokeItemDisplayConverter = File.ReadAllText(smokeItemDisplayConverterPath);
        var smokeThemedControl = File.ReadAllText(smokeThemedControlPath);
        var smokeGenericThemeXaml = File.ReadAllText(smokeGenericThemeXamlPath);
        var smokeAssemblyInfo = File.ReadAllText(smokeAssemblyInfoPath);
        var proGpuWpfProject = File.ReadAllText(proGpuWpfProjectPath);
        var proGpuWpfDirectoryBuildProps = File.ReadAllText(proGpuWpfDirectoryBuildPropsPath);
        var proGpuWpfAssemblyInfo = File.ReadAllText(proGpuWpfAssemblyInfoPath);
        var proGpuWpfTestsProject = File.ReadAllText(proGpuWpfTestsProjectPath);
        var proGpuWpfCommandSink = File.ReadAllText(proGpuWpfCommandSinkPath);
        var wpfCompositionDrawingContext = File.ReadAllText(wpfCompositionDrawingContextPath);
        var wpfObjectRenderDataDrawingContext = File.ReadAllText(wpfObjectRenderDataDrawingContextPath);
        var wpfReflectionDrawingReplay = File.ReadAllText(wpfReflectionDrawingReplayPath);
        var wpfPortableCommandSinkBridge = File.ReadAllText(wpfPortableCommandSinkBridgePath);
        var proGpuWpfDrawingFrame = File.ReadAllText(proGpuWpfDrawingFramePath);
        var proGpuWpfWindowHost = File.ReadAllText(proGpuWpfWindowHostPath);
        var wpfCompositionDrawingContextTests = File.ReadAllText(wpfCompositionDrawingContextTestsPath);
        var wpfReflectionResourceResolverTests = File.ReadAllText(wpfReflectionResourceResolverTestsPath);
        var wpfMilRenderDataDecoder = File.ReadAllText(wpfMilRenderDataDecoderPath);
        var wpfReflectionResourceResolver = File.ReadAllText(wpfReflectionResourceResolverPath);
        var wpfTransportTargets = File.ReadAllText(wpfTransportTargetsPath);
        var wpfTransportArchNeutralProject = File.ReadAllText(wpfTransportArchNeutralProjectPath);
        var runtimeHarnessProject = File.ReadAllText(runtimeHarnessProjectPath);
        var runtimeHarnessProgram = File.ReadAllText(runtimeHarnessProgramPath);
        var externalSdkHarnessProject = File.ReadAllText(externalSdkHarnessProjectPath);
        var externalSdkHarnessProgram = File.ReadAllText(externalSdkHarnessProgramPath);
        var spellerInteropBase = File.ReadAllText(spellerInteropBasePath);
        var sdkCiScript = File.ReadAllText(sdkCiScriptPath);
        var sdkCiWorkflow = File.ReadAllText(sdkCiWorkflowPath);

        Assert.Contains("ProGPU/Silk.NET SDK for portable WPF applications", sdkProject.ToString(), StringComparison.Ordinal);
        Assert.Contains("MSBuildProjectName.Replace('.ArchNeutral','')", sdkProject.ToString(), StringComparison.Ordinal);
        Assert.Contains("wpf;progpu;silk.net;msbuild-sdk", sdkProject.ToString(), StringComparison.Ordinal);
        Assert.Contains("PackageReadmeFile", sdkProject.ToString(), StringComparison.Ordinal);
        Assert.Contains("Sdk\\*", sdkProject.ToString(), StringComparison.Ordinal);
        Assert.Contains("targets\\*", sdkProject.ToString(), StringComparison.Ordinal);
        Assert.Contains("README.md", sdkProject.ToString(), StringComparison.Ordinal);
        Assert.Contains("<None Include=\"README.md\" Pack=\"true\" PackagePath=\"\\\" />", sdkProject.ToString(), StringComparison.Ordinal);
        Assert.Contains("<None Include=\"Sdk\\**\\*\" Pack=\"true\" PackagePath=\"Sdk\\%(RecursiveDir)\" />", sdkProject.ToString(), StringComparison.Ordinal);
        Assert.Contains("<None Include=\"targets\\**\\*\" Pack=\"true\" PackagePath=\"targets\\%(RecursiveDir)\" />", sdkProject.ToString(), StringComparison.Ordinal);
        AssertProjectReference(sdkProject, @"PresentationBuildTasks\PresentationBuildTasks.csproj");

        Assert.Contains("<_ProGpuWpfSdkImported>true</_ProGpuWpfSdkImported>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfUseWpfMarkup Condition=\"'$(ProGpuWpfUseWpfMarkup)' == ''\">true</ProGpuWpfUseWpfMarkup>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfUsePortableFrameworkReferences Condition=\"'$(ProGpuWpfUsePortableFrameworkReferences)' == ''\">true</ProGpuWpfUsePortableFrameworkReferences>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<UseWPF Condition=\"'$(ProGpuWpfUsePortableFrameworkReferences)' == 'true'\">false</UseWPF>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<EnableWindowsTargeting Condition=\"'$(EnableWindowsTargeting)' == ''\">true</EnableWindowsTargeting>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<CopyLocalLockFileAssemblies Condition=\"'$(ProGpuWpfUsePortableFrameworkReferences)' == 'true' And '$(CopyLocalLockFileAssemblies)' == ''\">true</CopyLocalLockFileAssemblies>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<NoWarn>$(NoWarn);NETSDK1137</NoWarn>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfEnablePortableBootstrap Condition=\"'$(ProGpuWpfEnablePortableBootstrap)' == ''\">true</ProGpuWpfEnablePortableBootstrap>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfUseCurrentRuntimeIdentifier Condition=\"'$(ProGpuWpfUseCurrentRuntimeIdentifier)' == ''\">true</ProGpuWpfUseCurrentRuntimeIdentifier>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<RuntimeIdentifier Condition=\"'$(ProGpuWpfUseCurrentRuntimeIdentifier)' == 'true' And '$(RuntimeIdentifier)' == ''\">$([System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier)</RuntimeIdentifier>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<AppendRuntimeIdentifierToOutputPath Condition=\"'$(ProGpuWpfUseCurrentRuntimeIdentifier)' == 'true' And '$(AppendRuntimeIdentifierToOutputPath)' == ''\">false</AppendRuntimeIdentifierToOutputPath>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfPlatform Condition=\"'$(ProGpuWpfPlatform)' == ''\">SilkNet</ProGpuWpfPlatform>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfRenderingBackend Condition=\"'$(ProGpuWpfRenderingBackend)' == ''\">ProGPU</ProGpuWpfRenderingBackend>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfSdkVersion Condition=\"'$(ProGpuWpfSdkVersion)' == ''\">11.0.0-dev</ProGpuWpfSdkVersion>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfRuntimeFrameworkVersion Condition=\"'$(ProGpuWpfRuntimeFrameworkVersion)' == ''\">11.0.0-preview.4.26210.111</ProGpuWpfRuntimeFrameworkVersion>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<RuntimeFrameworkVersion Condition=\"'$(ProGpuWpfUsePortableFrameworkReferences)' == 'true' And '$(RuntimeFrameworkVersion)' == '' And '$(ProGpuWpfRuntimeFrameworkVersion)' != ''\">$(ProGpuWpfRuntimeFrameworkVersion)</RuntimeFrameworkVersion>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfReferenceMode Condition=\"'$(ProGpuWpfReferenceMode)' == '' And ('$(ProGpuWpfManagedReferenceRoot)' != '' Or '$(ProGpuReferenceRoot)' != '')\">LocalArtifacts</ProGpuWpfReferenceMode>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfManagedPackageId Condition=\"'$(ProGpuWpfManagedPackageId)' == ''\">Microsoft.DotNet.Wpf.GitHub</ProGpuWpfManagedPackageId>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfManagedPackageVersion Condition=\"'$(ProGpuWpfManagedPackageVersion)' == ''\">$(ProGpuWpfPackageVersion)</ProGpuWpfManagedPackageVersion>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("ProGpuWpfClearMutablePackageOutputs", sdkProps, StringComparison.Ordinal);
        Assert.Contains("Contains(`-dev`)", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfSilkNetVersion Condition=\"'$(ProGpuWpfSilkNetVersion)' == ''\">2.23.0</ProGpuWpfSilkNetVersion>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfSystemIOPackagingVersion Condition=\"'$(ProGpuWpfSystemIOPackagingVersion)' == ''\">", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfSystemWindowsExtensionsVersion Condition=\"'$(ProGpuWpfSystemWindowsExtensionsVersion)' == ''\">", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<ProGpuWpfStbImageSharpVersion Condition=\"'$(ProGpuWpfStbImageSharpVersion)' == ''\">2.30.15</ProGpuWpfStbImageSharpVersion>", sdkProps, StringComparison.Ordinal);
        Assert.Contains("<Import Sdk=\"Microsoft.NET.Sdk.WindowsDesktop\" Project=\"Sdk.props\" />", sdkProps, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Wpf.Sdk.props", sdkProps, StringComparison.Ordinal);

        Assert.Contains("name: ProGPU WPF SDK", sdkCiWorkflow, StringComparison.Ordinal);
        Assert.Contains("submodules: recursive", sdkCiWorkflow, StringComparison.Ordinal);
        Assert.Contains("global-json-file: global.json", sdkCiWorkflow, StringComparison.Ordinal);
        Assert.Contains("./eng/progpu-wpf-sdk-ci.sh", sdkCiWorkflow, StringComparison.Ordinal);
        Assert.Contains("external/ProGPU/src/ProGPU.Backend/ProGPU.Backend.csproj", sdkCiScript, StringComparison.Ordinal);
        Assert.Contains("external/ProGPU/src/ProGPU.Scene/ProGPU.Scene.csproj", sdkCiScript, StringComparison.Ordinal);
        Assert.Contains("Building managed WPF transport payload", sdkCiScript, StringComparison.Ordinal);
        Assert.Contains("src/Microsoft.DotNet.Wpf/src/WindowsBase/WindowsBase.csproj", sdkCiScript, StringComparison.Ordinal);
        Assert.Contains("src/Microsoft.DotNet.Wpf/src/PresentationFramework/PresentationFramework.csproj", sdkCiScript, StringComparison.Ordinal);
        Assert.Contains("src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/PresentationFramework.Fluent.csproj", sdkCiScript, StringComparison.Ordinal);
        Assert.Contains("packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj", sdkCiScript, StringComparison.Ordinal);
        Assert.Contains("src/ProGPU.Wpf/ProGPU.Wpf.csproj", sdkCiScript, StringComparison.Ordinal);
        Assert.Contains("packaging/ProGPU.Wpf.Sdk/ProGPU.Wpf.Sdk.ArchNeutral.csproj", sdkCiScript, StringComparison.Ordinal);
        Assert.Contains("src/ProGPU.Wpf.SdkSwitchSmoke/ProGPU.Wpf.SdkSwitchSmoke.csproj", sdkCiScript, StringComparison.Ordinal);
        Assert.Contains("src/ProGPU.Wpf.SdkSwitchRuntimeHarness/ProGPU.Wpf.SdkSwitchRuntimeHarness.csproj", sdkCiScript, StringComparison.Ordinal);
        Assert.Contains("src/ProGPU.Wpf.SdkExternalSmokeHarness/ProGPU.Wpf.SdkExternalSmokeHarness.csproj", sdkCiScript, StringComparison.Ordinal);
        Assert.Contains("ProGpuWpfSdkProvidesSwitchOnlyPackagingSurface", sdkCiScript, StringComparison.Ordinal);

        Assert.Contains("<_ProGpuWpfProjectUseWPF>$(UseWPF)</_ProGpuWpfProjectUseWPF>", sdkTargets, StringComparison.Ordinal);
        Assert.Contains("<UseWPF Condition=\"'$(ProGpuWpfUsePortableFrameworkReferences)' == 'true'\">false</UseWPF>", sdkTargets, StringComparison.Ordinal);
        Assert.Contains("<Import Sdk=\"Microsoft.NET.Sdk.WindowsDesktop\" Project=\"Sdk.targets\" />", sdkTargets, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Wpf.Sdk.targets", sdkTargets, StringComparison.Ordinal);

        Assert.Contains("<DefaultXamlRuntime Condition=\"'$(DefaultXamlRuntime)' == ''\">Wpf</DefaultXamlRuntime>", portableProps, StringComparison.Ordinal);
        Assert.Contains("<InternalMarkupCompilation Condition=\"'$(ProGpuWpfUseWpfMarkup)' == 'true' And '$(InternalMarkupCompilation)' == ''\">true</InternalMarkupCompilation>", portableProps, StringComparison.Ordinal);
        Assert.Contains("<AlwaysCompileMarkupFilesInSeparateDomain Condition=\"'$(ProGpuWpfUseWpfMarkup)' == 'true' And '$(AlwaysCompileMarkupFilesInSeparateDomain)' == ''\">false</AlwaysCompileMarkupFilesInSeparateDomain>", portableProps, StringComparison.Ordinal);
        Assert.Contains("<_ProGpuWpfManagedReferenceRoot Condition=\"'$(ProGpuWpfManagedReferenceRoot)' != ''\">$([MSBuild]::EnsureTrailingSlash('$(ProGpuWpfManagedReferenceRoot)'))</_ProGpuWpfManagedReferenceRoot>", portableProps, StringComparison.Ordinal);
        Assert.Contains("<_ProGpuReferenceRoot Condition=\"'$(ProGpuReferenceRoot)' != ''\">$([MSBuild]::EnsureTrailingSlash('$(ProGpuReferenceRoot)'))</_ProGpuReferenceRoot>", portableProps, StringComparison.Ordinal);
        Assert.Contains("<ApplicationDefinition Include=\"App.xaml\"", portableProps, StringComparison.Ordinal);
        Assert.Contains("<Page Include=\"**/*.xaml\"", portableProps, StringComparison.Ordinal);
        Assert.Contains("<None Remove=\"**/*.xaml\"", portableProps, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Silk.NET.Input\" Version=\"$(ProGpuWpfSilkNetVersion)\" />", portableProps, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Silk.NET.Windowing\" Version=\"$(ProGpuWpfSilkNetVersion)\" />", portableProps, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Silk.NET.WebGPU\" Version=\"$(ProGpuWpfSilkNetVersion)\" />", portableProps, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Silk.NET.WebGPU.Native.WGPU\" Version=\"$(ProGpuWpfSilkNetVersion)\" />", portableProps, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"System.Configuration.ConfigurationManager\" Version=\"$(ProGpuWpfSystemConfigurationConfigurationManagerVersion)\" />", portableProps, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"System.Formats.Nrbf\" Version=\"$(ProGpuWpfSystemFormatsNrbfVersion)\" />", portableProps, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"System.IO.Packaging\" Version=\"$(ProGpuWpfSystemIOPackagingVersion)\" />", portableProps, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"System.Windows.Extensions\" Version=\"$(ProGpuWpfSystemWindowsExtensionsVersion)\" />", portableProps, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"StbImageSharp\" Version=\"$(ProGpuWpfStbImageSharpVersion)\" />", portableProps, StringComparison.Ordinal);

        Assert.Contains("<FrameworkReference Remove=\"Microsoft.WindowsDesktop.App.WPF\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<CopyLocalLockFileAssemblies Condition=\"'$(ProGpuWpfUsePortableFrameworkReferences)' == 'true'\">true</CopyLocalLockFileAssemblies>", portableTargets, StringComparison.Ordinal);
        Assert.Contains("$(ProGpuWpfEnablePortableBootstrap)", portableTargets, StringComparison.Ordinal);
        Assert.Contains("And ('$(OutputType)' == 'Exe' Or '$(OutputType)' == 'WinExe')", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<Compile Include=\"$(MSBuildThisFileDirectory)ProGPU.Wpf.Sdk.PortableBootstrap.cs\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("Link=\"ProGPU.Wpf.Sdk.PortableBootstrap.cs\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("Visible=\"false\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("_ProGpuWpfSdkRemoveWindowsDesktopPackageDownloads", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<PackageDownload Remove=\"Microsoft.WindowsDesktop.App.Ref\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"$(ProGpuWpfManagedPackageId)\" Version=\"$(ProGpuWpfManagedPackageVersion)\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"ProGPU.Wpf\" Version=\"$(ProGpuWpfPackageVersion)\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"ProGPU.Backend\" Version=\"$(ProGpuPackageVersion)\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"ProGPU.Scene\" Version=\"$(ProGpuPackageVersion)\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"ProGPU.Vector\" Version=\"$(ProGpuPackageVersion)\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"ProGPU.Text\" Version=\"$(ProGpuPackageVersion)\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"ProGPU.Compute\" Version=\"$(ProGpuPackageVersion)\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"ProGPU.Transpiler\" Version=\"$(ProGpuPackageVersion)\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<Reference Include=\"WindowsBase\" HintPath=\"$(_ProGpuWpfManagedReferenceRoot)WindowsBase.dll\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<Reference Include=\"PresentationFramework\" HintPath=\"$(_ProGpuWpfManagedReferenceRoot)PresentationFramework.dll\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<Reference Include=\"ProGPU.Wpf\" HintPath=\"$(_ProGpuReferenceRoot)ProGPU.Wpf.dll\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<Reference Include=\"ProGPU.Compute\" HintPath=\"$(_ProGpuReferenceRoot)ProGPU.Compute.dll\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<Reference Include=\"ProGPU.Transpiler\" HintPath=\"$(_ProGpuReferenceRoot)ProGPU.Transpiler.dll\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("Condition=\"Exists('$(_ProGpuWpfManagedReferenceRoot)PresentationUI.dll')\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("Condition=\"Exists('$(_ProGpuWpfManagedReferenceRoot)UIAutomationTypes.dll')\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("Condition=\"Exists('$(_ProGpuWpfManagedReferenceRoot)PresentationFramework.Fluent.dll')\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("Condition=\"Exists('$(_ProGpuReferenceRoot)ProGPU.Text.dll')\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("_ProGpuWpfSdkCopyLocalRuntimeAssets", portableTargets, StringComparison.Ordinal);
        Assert.Contains("_ProGpuWpfSdkClearMutablePackageOutputs", portableTargets, StringComparison.Ordinal);
        Assert.Contains("BeforeTargets=\"_ProGpuWpfSdkCopyPackageRuntimeAssets\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("$(ProGpuWpfClearMutablePackageOutputs)", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<_ProGpuWpfSdkMutablePackageOutput Include=\"$(TargetDir)ProGPU.Wpf.dll\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<_ProGpuWpfSdkMutablePackageOutput Include=\"$(TargetDir)ProGPU.Scene.dll\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("<Delete Files=\"@(_ProGpuWpfSdkExistingMutablePackageOutput)\" />", portableTargets, StringComparison.Ordinal);
        Assert.Contains("_ProGpuWpfSdkCopyPackageRuntimeAssets", portableTargets, StringComparison.Ordinal);
        Assert.Contains("_ProGpuWpfSdkCopyNativeRuntimeAssets", portableTargets, StringComparison.Ordinal);
        Assert.Contains("DependsOnTargets=\"ResolveLockFileCopyLocalFiles\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("DependsOnTargets=\"ResolvePackageAssets\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("DestinationFiles=\"@(RuntimeCopyLocalItems->'$(TargetDir)%(DestinationSubDirectory)%(Filename)%(Extension)')\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("DestinationFiles=\"@(NativeCopyLocalItems->'$(TargetDir)%(DestinationSubPath)')\"", portableTargets, StringComparison.Ordinal);
        Assert.Contains("local artifact mode requires ProGpuWpfManagedReferenceRoot", portableTargets, StringComparison.Ordinal);
        Assert.Contains("local artifact mode requires ProGpuReferenceRoot", portableTargets, StringComparison.Ordinal);

        Assert.Contains("namespace ProGPU.Wpf.Sdk;", portableBootstrap, StringComparison.Ordinal);
        Assert.Contains("internal static class ProGpuWpfSdkPortableBootstrap", portableBootstrap, StringComparison.Ordinal);
        Assert.Contains("[ModuleInitializer]", portableBootstrap, StringComparison.Ordinal);
        Assert.Contains("if (OperatingSystem.IsWindows())", portableBootstrap, StringComparison.Ordinal);
        Assert.Contains("WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation(typeof(Application).Assembly)", portableBootstrap, StringComparison.Ordinal);
        Assert.Contains("WpfPortableWindowActivation.TryRegisterPresentationCoreClipboardService(typeof(Clipboard).Assembly)", portableBootstrap, StringComparison.Ordinal);

        Assert.Contains("<Project Sdk=\"ProGPU.Wpf.Sdk/11.0.0-dev\">", smokeProject, StringComparison.Ordinal);
        Assert.Contains("<OutputType>WinExe</OutputType>", smokeProject, StringComparison.Ordinal);
        Assert.Contains("<TargetFramework>net11.0</TargetFramework>", smokeProject, StringComparison.Ordinal);
        Assert.Contains("<UseWPF>true</UseWPF>", smokeProject, StringComparison.Ordinal);
        Assert.Contains(@"<ProjectReference Include=""..\ProGPU.Wpf.SdkSwitchLibrary\ProGPU.Wpf.SdkSwitchLibrary.csproj"" />", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableDefaultItems", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDefinition Include", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Page Include", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Compile Include", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGpuWpfUseWpfMarkup", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGpuWpfUsePortableFrameworkReferences", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGpuWpfReferenceMode", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("InternalMarkupCompilation", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("AlwaysCompileMarkupFilesInSeparateDomain", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeFrameworkVersion", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("AssemblyName", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("RootNamespace", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateDependencyFile", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("ImplicitUsings", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Nullable", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableNETAnalyzers", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("EnforceCodeStyleInBuild", smokeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("NoWarn", smokeProject, StringComparison.Ordinal);
        Assert.Contains(@"..\..\Directory.Build.props", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<EnableNETAnalyzers>false</EnableNETAnalyzers>", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<RestorePackagesPath>", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<RestoreForce>true</RestoreForce>", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<RestoreForceEvaluate>true</RestoreForceEvaluate>", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("_ProGpuWpfSdkSwitchClearMutablePackageCache", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.DoesNotContain("Condition=\"'$(MSBuildProjectName)' == 'ProGPU.Wpf.SdkSwitchSmoke'\"", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("progpu.wpf/$(ProGpuWpfSdkSwitchPackageVersion)", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("progpu.scene/$(ProGpuWpfSdkSwitchPackageVersion)", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("microsoft.dotnet.wpf.github/$(ProGpuWpfSdkSwitchPackageVersion)", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGpuWpfReferenceMode", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGpuWpfManagedReferenceRoot", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGpuReferenceRoot", smokeDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<Project Sdk=\"ProGPU.Wpf.Sdk/11.0.0-dev\">", libraryProject, StringComparison.Ordinal);
        Assert.Contains("<TargetFramework>net11.0</TargetFramework>", libraryProject, StringComparison.Ordinal);
        Assert.Contains("<UseWPF>true</UseWPF>", libraryProject, StringComparison.Ordinal);
        Assert.DoesNotContain("OutputType", libraryProject, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGpuWpf", libraryProject, StringComparison.Ordinal);
        Assert.DoesNotContain("ImplicitUsings", libraryProject, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableNETAnalyzers", libraryProject, StringComparison.Ordinal);
        Assert.Contains(@"..\..\Directory.Build.props", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<EnableNETAnalyzers>false</EnableNETAnalyzers>", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<RestorePackagesPath>", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<RestoreForce>true</RestoreForce>", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<RestoreForceEvaluate>true</RestoreForceEvaluate>", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("_ProGpuWpfSdkSwitchClearMutablePackageCache", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("Condition=\"'$(MSBuildProjectName)' == 'ProGPU.Wpf.SdkSwitchSmoke'\"", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("progpu.wpf/$(ProGpuWpfSdkSwitchPackageVersion)", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("progpu.scene/$(ProGpuWpfSdkSwitchPackageVersion)", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("microsoft.dotnet.wpf.github/$(ProGpuWpfSdkSwitchPackageVersion)", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGpuWpfReferenceMode", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGpuWpfManagedReferenceRoot", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGpuReferenceRoot", libraryDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("x:Class=\"ProGPU.Wpf.SdkSwitchLibrary.LibraryPanel\"", libraryPanelXaml, StringComparison.Ordinal);
        Assert.Contains("LibraryAccentBrush", libraryPanelXaml, StringComparison.Ordinal);
        Assert.Contains("ElementName=LibraryOwner", libraryPanelXaml, StringComparison.Ordinal);
        Assert.Contains("compiled library BAML", libraryPanelXaml, StringComparison.Ordinal);
        Assert.Contains("public partial class LibraryPanel : UserControl", libraryPanelCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyProperty.Register", libraryPanelCodeBehind, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();", libraryPanelCodeBehind, StringComparison.Ordinal);
        Assert.Contains("artifacts/packages/Release/NonShipping", smokeNuGetConfig, StringComparison.Ordinal);
        Assert.Contains("globalPackagesFolder", smokeNuGetConfig, StringComparison.Ordinal);
        Assert.Contains("artifacts/nuget/ProGPU.Wpf.SdkSwitchSmoke", smokeNuGetConfig, StringComparison.Ordinal);
        Assert.Contains("dotnet-eng", smokeNuGetConfig, StringComparison.Ordinal);
        Assert.Contains("dotnet11-transport", smokeNuGetConfig, StringComparison.Ordinal);
        Assert.Contains("artifacts/packages/Release/NonShipping", libraryNuGetConfig, StringComparison.Ordinal);
        Assert.Contains("globalPackagesFolder", libraryNuGetConfig, StringComparison.Ordinal);
        Assert.Contains("artifacts/nuget/ProGPU.Wpf.SdkSwitchSmoke", libraryNuGetConfig, StringComparison.Ordinal);
        Assert.Contains("dotnet-eng", libraryNuGetConfig, StringComparison.Ordinal);
        Assert.Contains("dotnet11-transport", libraryNuGetConfig, StringComparison.Ordinal);
        Assert.Contains("ResourceDictionary Source=\"SmokeResources.xaml\"", smokeAppXaml, StringComparison.Ordinal);
        Assert.Contains("SmokeAccentBrush", smokeAppXaml, StringComparison.Ordinal);
        Assert.Contains("Startup=\"OnAppStartup\"", smokeAppXaml, StringComparison.Ordinal);
        Assert.Contains("Exit=\"OnAppExit\"", smokeAppXaml, StringComparison.Ordinal);
        Assert.Contains("public int StartupEventCount", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int StartupArgsLength", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ExitEventCount", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int LastExitCode", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("using System.Windows.Media;", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnAppStartup(object sender, StartupEventArgs e)", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("StartupEventCount++", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("StartupArgsLength = e.Args.Length", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Resources[\"StartupInjectedBrush\"]", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new SolidColorBrush(Color.FromRgb(0x7A, 0x4E, 0xB2))", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Resources[\"StartupInjectedText\"]", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnAppExit(object sender, ExitEventArgs e)", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ExitEventCount++", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastExitCode = e.ApplicationExitCode", smokeAppCodeBehind, StringComparison.Ordinal);
        Assert.Contains("MergedAccentBrush", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("UnsharedAccentBrush", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("x:Shared=\"False\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FreezableAccentBrush\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("LinearGradientBrush", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FreezableGradientBrush\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("SpreadMethod=\"Reflect\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("GradientStop Color=\"#2F6B54\" Offset=\"0\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("GradientStop Color=\"#B15E3B\" Offset=\"0.5\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("SmokePanelMargin", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("SmokeListTextStyle", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"BasedOnSmokeTextStyle\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource SmokeListTextStyle}\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("ObjectDataProvider", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ProviderGreeting\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("MethodName=\"CreateGreeting\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("<sys:String>provider</sys:String>", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("<sys:Int32>7</sys:Int32>", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:componentModel=\"clr-namespace:System.ComponentModel;assembly=WindowsBase\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"840\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<ControlTemplate TargetType=\"{x:Type Button}\">", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<VisualStateManager.VisualStateGroups>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<VisualStateGroup x:Name=\"CommonStates\">", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<ControlTemplate.Triggers>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PropertyTriggeredTextStyle\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"Tag\" Value=\"Active\">", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DataTriggeredTextStyle\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<DataTrigger Binding=\"{Binding IsHighlighted}\" Value=\"True\">", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MultiTriggeredTextStyle\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<MultiTrigger>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MultiDataTriggeredTextStyle\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<MultiDataTrigger>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Title}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Window.CommandBindings>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Static local:MainWindow.SmokeCommand}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CanExecute=\"OnSmokeCommandCanExecute\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Executed=\"OnSmokeCommandExecuted\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Window.InputBindings>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"input binding payload\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<MouseBinding", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Gesture=\"LeftDoubleClick\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"mouse binding payload\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnActionButtonClick\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Button.ToolTip>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ActionToolTip\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Action tooltip content\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Placement=\"Right\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Button.ContextMenu>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ActionContextMenu\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContextCommandMenuItem\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"context menu command payload\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Context command\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContextCheckableMenuItem\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"_Context checked\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommandButton\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"routed command payload\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"EventSetterButtonStyle\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<EventSetter Event=\"Click\" Handler=\"OnEventSetterButtonClick\" />", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EventSetterButton\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource EventSetterButtonStyle}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EventSetterStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeMenu\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommandMenuItem\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"menu command payload\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClickMenuItem\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnMenuItemClick\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Separator />", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CheckableMenuItem\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsCheckable=\"True\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"True\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Checked=\"OnCheckableMenuItemChecked\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Unchecked=\"OnCheckableMenuItemUnchecked\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MenuStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CheckChoicePanel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ManagedCheckBox\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Checked=\"OnManagedCheckBoxChecked\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Unchecked=\"OnManagedCheckBoxUnchecked\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Managed check\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ManagedRadioAlpha\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ManagedRadioBeta\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GroupName=\"ManagedRadioGroup\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Checked=\"OnManagedRadioChecked\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Unchecked=\"OnManagedRadioUnchecked\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CheckChoiceStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RootPanel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartupResourceText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource StartupInjectedBrush}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{DynamicResource StartupInjectedText}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeRoutedEventSource.SmokeBubbled=\"OnSmokeBubbled\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PropertyTriggerStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DataTriggerStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiTriggerStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiDataTriggerStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LoadedStoryboardText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Loaded=\"OnLoadedStoryboardTextLoaded\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("EventTrigger RoutedEvent=\"FrameworkElement.Loaded\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("BeginStoryboard", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetName=\"LoadedStoryboardText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetProperty=\"Opacity\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DoubleAnimation", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("To=\"0.42\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BasedOnResourceText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource BasedOnSmokeTextStyle}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProviderGreetingText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Source={StaticResource ProviderGreeting}}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<XmlDataProvider", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"XmlSmokeData\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsAsynchronous=\"False\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<x:XData>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<item name=\"xml\" value=\"provider\" />", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"XmlProviderText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("XPath=@name", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UnsharedBrushBorder\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{StaticResource UnsharedAccentBrush}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InputText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<InputMethod.InputScope>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RegularExpression=\"[0-9a-z]+\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SrgsMarkup=\"sdk-input-scope\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<InputScopeName>EmailSmtpAddress</InputScopeName>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<InputScopePhrase>package phrase</InputScopePhrase>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccessKeyFocusPanel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FocusManager.IsFocusScope=\"True\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FocusManager.FocusedElement=\"{Binding ElementName=InputBox}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigation.ControlTabNavigation=\"Cycle\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigation.DirectionalNavigation=\"Contained\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigation.TabNavigation=\"Cycle\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InputAccessLabel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"_Input access\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"{Binding ElementName=InputBox}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StandaloneAccessText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"_Standalone access\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AncestorBindingBorder\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"ancestor binding value\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AncestorBindingText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RelativeSource={RelativeSource AncestorType={x:Type Border}}", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MutableStatusText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding MutableStatus}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ValidatedInputBox\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Path=\"ValidationText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("NotifyOnValidationError=\"True\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("UpdateSourceTrigger=\"PropertyChanged\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<local:SmokeNonEmptyValidationRule />", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ValidationStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Path=(Validation.HasError)", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CredentialBox\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("MaxLength=\"12\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PasswordChanged=\"OnPasswordChanged\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PasswordChar=\"#\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PasswordStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CalendarSmoke\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DisplayDate=\"2026-06-01\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DisplayDateEnd=\"2026-12-31\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DisplayDateStart=\"2026-01-01\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FirstDayOfWeek=\"Monday\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsTodayHighlighted=\"False\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedDate=\"2026-06-17\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedDatesChanged=\"OnDateSelectionChanged\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"SingleDate\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DatePickerSmoke\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedDate=\"2026-06-18\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedDateChanged=\"OnDateSelectionChanged\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedDateFormat=\"Short\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DateStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeRoutedEventSource", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RoutedEventSource\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RoutedEventStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"routed event not raised\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Items}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemTemplate=\"{StaticResource SmokeItemTemplate}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<DataTemplate DataType=\"{x:Type local:SmokeItem}\">", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImplicitSmokeItemText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Name, StringFormat=implicit: {0}}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImplicitItemPresenter\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImplicitStylePanel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Style TargetType=\"{x:Type TextBlock}\">", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"implicit style active\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{DynamicResource SmokeAccentBrush}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImplicitStyledText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<HierarchicalDataTemplate", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeHierarchyTemplate\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Children}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeWrapPanelTemplate\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<WrapPanel Orientation=\"Horizontal\" />", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeItemContainerStyle\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ContentPresenter}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"container style active\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ItemsCountText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Items.Count, StringFormat=items: {0}}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PanelItemsControl\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("AlternationCount=\"3\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle=\"{StaticResource SmokeItemContainerStyle}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemStringFormat=\"panel item: {0}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsPanel=\"{StaticResource SmokeWrapPanelTemplate}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeListView\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<ListView.View>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<GridView>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GridViewColumn", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberBinding=\"{Binding Name}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberBinding=\"{Binding Value}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ListViewStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StringFormat=list view: {0}", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MultiBindingSummaryText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<MultiBinding Converter=\"{StaticResource SmokeItemSummaryConverter}\">", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Path=\"SelectedItem.Name\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Path=\"SelectedItem.Value\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PriorityBindingText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<PriorityBinding>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Path=\"MissingPriorityTitle\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Path=\"Title\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding SelectedItem, ElementName=ItemsList}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeItemDisplayConverter", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeItemSummaryConverter", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LayoutGrid\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Grid.RowDefinitions>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Grid.ColumnDefinitions>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LayoutLabel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"0\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConvertedSelectedItemText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Converter={StaticResource SmokeItemDisplayConverter}", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FormattedInputText\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.ColumnSpan=\"2\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StringFormat=Input: {0}", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DockLayoutPanel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("LastChildFill=\"True\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DockPanel.Dock=\"Top\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DockPanel.Dock=\"Left\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StringFormat=dock fill: {0}", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CanvasLayoutPanel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Canvas.Left=\"12\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Canvas.Top=\"8\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UniformLayoutPanel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Columns=\"3\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Rows=\"1\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"GroupedItems\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<CollectionViewSource.SortDescriptions>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("componentModel:SortDescription PropertyName=\"Name\" Direction=\"Ascending\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<PropertyGroupDescription PropertyName=\"Category\" />", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupedItemsControl\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource GroupedItems}}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<GroupStyle HeaderTemplate=\"{StaticResource SmokeGroupHeaderTemplate}\" />", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeFrameworkItemTemplate\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SmokeRenderingItemTemplate\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeItemTemplateSelector", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FrameworkTemplate=\"{StaticResource SmokeFrameworkItemTemplate}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RenderingTemplate=\"{StaticResource SmokeRenderingItemTemplate}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectorItemsControl\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemTemplateSelector=\"{StaticResource SmokeItemTemplateSelector}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeComboBox\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberPath=\"Name\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValuePath=\"Value\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex=\"1\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"OnSelectorSelectionChanged\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectorStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeTabs\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex=\"1\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"OnTabsSelectionChanged\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FrameworkTab\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Framework\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RenderingTab\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Rendering\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TabStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeToolBarTray\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeToolBar\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Smoke tools\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToolBarCommandButton\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"toolbar command payload\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Run toolbar\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Separator x:Name=\"ToolBarSeparator\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ToggleButton", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToolBarToggle\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Toggle toolbar\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeStatusBar\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StatusReadyItem\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Ready\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StatusTextBlock\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"status text\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeGroupBox\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Managed range\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeExpander\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Expanded=\"OnSmokeExpanderExpanded\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Collapsed=\"OnSmokeExpanderCollapsed\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Range details\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeScrollViewer\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScrollContentPanel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeSlider\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ValueChanged=\"OnRangeValueChanged\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeProgressBar\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding Value, ElementName=SmokeSlider}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RangeStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeDataGrid\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("AutoGenerateColumns=\"False\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CanUserAddRows=\"False\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<DataGrid.Columns>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataGridTextColumn", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding Name}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding Category}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataGridCheckBoxColumn", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IsActive}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DataGridStatus\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StringFormat=data grid: {0}", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HierarchyTree\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemTemplate=\"{StaticResource SmokeHierarchyTemplate}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokePanel", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompiledSmokePanel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Caption=\"Compiled user control\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PanelContent=\"{Binding SelectedItem.Value, ElementName=ItemsList}\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("clr-namespace:ProGPU.Wpf.SdkSwitchLibrary;assembly=ProGPU.Wpf.SdkSwitchLibrary", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("library:LibraryPanel", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompiledLibraryPanel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"SDK library panel\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeThemedControl", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ThemedSmokeControl\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Generic theme default style\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Frame", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SmokeFrame\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("LoadCompleted=\"OnSmokeFrameLoadCompleted\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Navigated=\"OnSmokeFrameNavigated\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Navigating=\"OnSmokeFrameNavigating\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("NavigationUIVisibility=\"Hidden\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"SmokePage.xaml\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<RichTextBox", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<FlowDocument>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("NavigateUri=\"https://example.com/progpu-wpf\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RequestNavigate=\"OnDocumentLinkRequestNavigate\"", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Inline document button", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<List MarkerStyle=\"Square\">", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Document list item one", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Table CellSpacing=\"2\">", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<TableColumn Width=\"120\" />", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Column A", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<BlockUIContainer>", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Block UI document content", smokeMainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Class=\"ProGPU.Wpf.SdkSwitchSmoke.SmokePanel\"", smokePanelXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"{Binding Caption, ElementName=Root}\"", smokePanelXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PanelCaption\"", smokePanelXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Caption, ElementName=Root}\"", smokePanelXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PanelRelativeCaption\"", smokePanelXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"{Binding Caption, ElementName=Root}\"", smokePanelXaml, StringComparison.Ordinal);
        Assert.Contains("RelativeSource={RelativeSource Self}", smokePanelXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PanelContentPresenter\"", smokePanelXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding PanelContent, ElementName=Root}\"", smokePanelXaml, StringComparison.Ordinal);
        Assert.Contains("public partial class SmokePanel : UserControl", smokePanelCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyProperty CaptionProperty", smokePanelCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DependencyProperty PanelContentProperty", smokePanelCodeBehind, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();", smokePanelCodeBehind, StringComparison.Ordinal);
        Assert.Contains("x:Class=\"ProGPU.Wpf.SdkSwitchSmoke.SmokePage\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"Compiled Smoke Page\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PageTitle\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource SmokeAccentBrush}\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Compiled page content\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PageSubtitle\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Frame loaded SDK-built BAML\"", smokePageXaml, StringComparison.Ordinal);
        Assert.Contains("public partial class SmokePage : Page", smokePageCodeBehind, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();", smokePageCodeBehind, StringComparison.Ordinal);
        Assert.Contains("x:Class=\"ProGPU.Wpf.SdkSwitchSmoke.SmokeSecondPage\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"Compiled Second Page\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SecondPageTitle\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("Compiled second page content", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SecondPageSubtitle\"", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("Frame navigated to SDK-built BAML", smokeSecondPageXaml, StringComparison.Ordinal);
        Assert.Contains("public partial class SmokeSecondPage : Page", smokeSecondPageCodeBehind, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();", smokeSecondPageCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeItemDisplayConverter : IValueConverter", smokeItemDisplayConverter, StringComparison.Ordinal);
        Assert.Contains("item.Name}={item.Value}/{item.Category", smokeItemDisplayConverter, StringComparison.Ordinal);
        Assert.Contains("Binding.DoNothing", smokeItemDisplayConverter, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeItemSummaryConverter : IMultiValueConverter", smokeItemDisplayConverter, StringComparison.Ordinal);
        Assert.Contains("public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)", smokeItemDisplayConverter, StringComparison.Ordinal);
        Assert.Contains("public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)", smokeItemDisplayConverter, StringComparison.Ordinal);
        Assert.Contains("DependencyProperty.UnsetValue", smokeItemDisplayConverter, StringComparison.Ordinal);
        Assert.Contains("[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]", smokeAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeThemedControl : Control", smokeThemedControl, StringComparison.Ordinal);
        Assert.Contains("DependencyProperty TextProperty", smokeThemedControl, StringComparison.Ordinal);
        Assert.Contains("DefaultStyleKeyProperty.OverrideMetadata", smokeThemedControl, StringComparison.Ordinal);
        Assert.Contains("new FrameworkPropertyMetadata(typeof(SmokeThemedControl))", smokeThemedControl, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type local:SmokeThemedControl}\"", smokeGenericThemeXaml, StringComparison.Ordinal);
        Assert.Contains("ComponentResourceKey TypeInTargetAssembly={x:Type local:SmokeThemedControl}, ResourceId=ThemeBadgeBrush", smokeGenericThemeXaml, StringComparison.Ordinal);
        Assert.Contains("Color=\"#7A4EB2\"", smokeGenericThemeXaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource MergedAccentBrush", smokeGenericThemeXaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource SmokeAccentBrush", smokeGenericThemeXaml, StringComparison.Ordinal);
        Assert.Contains("<ControlTemplate TargetType=\"{x:Type local:SmokeThemedControl}\">", smokeGenericThemeXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ThemeRoot\"", smokeGenericThemeXaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{TemplateBinding Background}\"", smokeGenericThemeXaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{DynamicResource {ComponentResourceKey TypeInTargetAssembly={x:Type local:SmokeThemedControl}, ResourceId=ThemeBadgeBrush}}\"", smokeGenericThemeXaml, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"1\"", smokeGenericThemeXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ThemeText\"", smokeGenericThemeXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{TemplateBinding Text}\"", smokeGenericThemeXaml, StringComparison.Ordinal);
        Assert.Contains("DataContext = new SmokeViewModel();", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public static RoutedUICommand SmokeCommand", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnActionButtonClick", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ClickStatus.Text = \"clicked\";", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeCommandCanExecute", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeCommandExecuted", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SmokeCommandExecutionCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int EventSetterClickCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string? LastEventSetterSenderName", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string? LastEventSetterRoutedEventName", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnEventSetterButtonClick", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("EventSetterClickCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastEventSetterRoutedEventName = e.RoutedEvent?.Name", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int MenuClickCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int MenuCheckedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int MenuUncheckedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnMenuItemClick", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("MenuClickCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnCheckableMenuItemChecked", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("MenuCheckedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnCheckableMenuItemUnchecked", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("MenuUncheckedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("if (MenuStatus != null)", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ManagedCheckBoxCheckedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ManagedCheckBoxUncheckedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ManagedRadioCheckedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ManagedRadioUncheckedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string? LastManagedRadioCheckedName", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnManagedCheckBoxChecked", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ManagedCheckBoxCheckedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnManagedCheckBoxUnchecked", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ManagedCheckBoxUncheckedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnManagedRadioChecked", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastManagedRadioCheckedName = (sender as FrameworkElement)?.Name", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnManagedRadioUnchecked", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ManagedRadioUncheckedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int PasswordChangedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string? LastPasswordChangedSenderName", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string? LastPasswordChangedRoutedEventName", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int DateSelectionChangedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string? LastDateSelectionChangedSenderName", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnPasswordChanged", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("PasswordChangedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastPasswordChangedRoutedEventName = e.RoutedEvent?.Name", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDateSelectionChanged", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DateSelectionChangedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastDateSelectionChangedSenderName = (sender as FrameworkElement)?.Name", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int SelectorSelectionChangedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int TabSelectionChangedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ExpanderExpandedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ExpanderCollapsedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int RangeValueChangedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int SmokeFrameNavigatingCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int SmokeFrameNavigatedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int SmokeFrameLoadCompletedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string? LastSmokeFrameNavigatingUri", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string? LastSmokeFrameNavigationMode", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string? LastSmokeFrameNavigatedContentType", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSelectorSelectionChanged", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SelectorSelectionChangedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("selector selected: {SmokeComboBox.SelectedValue}", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnTabsSelectionChanged", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(e.OriginalSource, sender)", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("TabSelectionChangedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("tab selected: {selectedTab.Header}", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeExpanderExpanded", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ExpanderExpandedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeExpanderCollapsed", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ExpanderCollapsedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnRangeValueChanged", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RangeValueChangedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("e.NewValue.ToString(\"0.##\", CultureInfo.InvariantCulture)", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeFrameNavigating(object sender, NavigatingCancelEventArgs e)", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SmokeFrameNavigatingCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastSmokeFrameNavigationMode = e.NavigationMode.ToString()", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeFrameNavigated(object sender, NavigationEventArgs e)", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SmokeFrameNavigatedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastSmokeFrameNavigatedContentType = e.Content?.GetType().FullName", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeFrameLoadCompleted(object sender, NavigationEventArgs e)", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SmokeFrameLoadCompletedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("using System.Windows.Navigation;", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int DocumentLinkRequestNavigateCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string? LastDocumentLinkRequestNavigateUri", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string? LastDocumentLinkRequestNavigateRoutedEventName", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDocumentLinkRequestNavigate(object sender, RequestNavigateEventArgs e)", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DocumentLinkRequestNavigateCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastDocumentLinkRequestNavigateUri = e.Uri?.ToString()", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastDocumentLinkRequestNavigateRoutedEventName = e.RoutedEvent?.Name", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int LoadedStoryboardTextLoadedCount", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string? LastLoadedStoryboardTextRoutedEventName", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnLoadedStoryboardTextLoaded(object sender, RoutedEventArgs e)", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LoadedStoryboardTextLoadedCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastLoadedStoryboardTextRoutedEventName = e.RoutedEvent?.Name", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SmokeRoutedEventCount++", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeBubbled", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastSmokeRoutedEventSender = sender;", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastSmokeRoutedEventSource = e.OriginalSource;", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeRoutedEventSource : FrameworkElement", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("EventManager.RegisterRoutedEvent", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RoutingStrategy.Bubble", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RaiseSmokeBubbled()", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("SmokeViewModel : INotifyPropertyChanged", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private string _mutableStatus = \"initial binding status\";", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string MutableStatus", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged();", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("PropertyChanged?.Invoke", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string ValidationText { get; set; } = \"valid package text\";", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeNonEmptyValidationRule : ValidationRule", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public override ValidationResult Validate(object value, CultureInfo cultureInfo)", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new ValidationResult(false, \"Value is required\")", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ValidationResult.ValidResult", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsHighlighted", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsCritical", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ObservableCollection<SmokeItem>", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new SmokeItem(\"Startup\", \"managed\", \"Framework\")", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("new SmokeItem(\"Scene\", \"ProGPU\", \"Rendering\", false)", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string Category", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsActive", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public ObservableCollection<SmokeItem> Children", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeItemTemplateSelector : DataTemplateSelector", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public DataTemplate? FrameworkTemplate", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public DataTemplate? RenderingTemplate", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public override DataTemplate? SelectTemplate(object item, DependencyObject container)", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("Category: \"Rendering\"", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public static class SmokeResourceFactory", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("CreateGreeting(string prefix, int value)", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{prefix}:{value}\";", smokeMainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("<SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>", proGpuWpfProject, StringComparison.Ordinal);
        Assert.Contains("<SignAssembly>true</SignAssembly>", proGpuWpfProject, StringComparison.Ordinal);
        Assert.Contains(@"<AssemblyOriginatorKeyFile>..\..\external\ProGPU\eng\ProGPU.snk</AssemblyOriginatorKeyFile>", proGpuWpfProject, StringComparison.Ordinal);
        Assert.Contains("<Version Condition=\"'$(Version)' == ''\">11.0.0-dev</Version>", proGpuWpfDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<PackageVersion Condition=\"'$(PackageVersion)' == ''\">11.0.0-dev</PackageVersion>", proGpuWpfDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion Condition=\"'$(AssemblyVersion)' == ''\">11.0.0.0</AssemblyVersion>", proGpuWpfDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<FileVersion Condition=\"'$(FileVersion)' == ''\">11.0.0.0</FileVersion>", proGpuWpfDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion Condition=\"'$(InformationalVersion)' == ''\">11.0.0-dev</InformationalVersion>", proGpuWpfDirectoryBuildProps, StringComparison.Ordinal);
        Assert.Contains("<SignAssembly>true</SignAssembly>", proGpuWpfTestsProject, StringComparison.Ordinal);
        Assert.Contains(@"<AssemblyOriginatorKeyFile>..\..\external\ProGPU\eng\ProGPU.snk</AssemblyOriginatorKeyFile>", proGpuWpfTestsProject, StringComparison.Ordinal);
        Assert.Contains("InternalsVisibleTo(", proGpuWpfAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Wpf.Tests, PublicKey=", proGpuWpfAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("c891cb91", proGpuWpfAssemblyInfo, StringComparison.Ordinal);
        Assert.Contains("PresentationCore\\PresentationCore.csproj\" PrivateAssets=\"all\"", proGpuWpfProject, StringComparison.Ordinal);
        Assert.Contains("ReadReplayPoint", wpfMilRenderDataDecoder, StringComparison.Ordinal);
        Assert.Contains("ReadReplayRect", wpfMilRenderDataDecoder, StringComparison.Ordinal);
        Assert.Contains("DrawNativeGlyphRun", wpfMilRenderDataDecoder, StringComparison.Ordinal);
        Assert.Contains("TryResolveRawResource", wpfMilRenderDataDecoder, StringComparison.Ordinal);
        Assert.Contains("TryAdaptNativeGlyphRun", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("AdaptNativeBrush", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("AdaptNativePen", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("TryReadReplayPoint(startPointValue", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("TryReadReplayPoint(centerValue", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("TryReadReplayRect(rectValue", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("TryParseGeometryText(typeof(MediaGeometry)", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("TryParseGeometryText(typeof(PathGeometry)", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.DoesNotContain("PathGeometry.Parse(pathText)", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("CreateCombinedGeometry(geometry1, geometry2, pathOperation)", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProGpuCombinedGeometry", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("TryAssignDashStyle", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("GetType(\"System.Windows.Media.DashStyle\"", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("TryConvertCombinedGeometryToNativePath(geometry", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("TryReadGeometryCombinePathOperation", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.DoesNotContain("geometry is ProGpuCombinedGeometry", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("TryReadGeometryBounds(geometry", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("TryReadReplayRect(boundsValue", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.DoesNotContain("var bounds = geometry.Bounds;", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("ReadGeometryTransform(geometry)", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("TryReadTransformValue(transformValue", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("TryReadMatrix4x4(matrixValue", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.DoesNotContain("geometry.Transform?.Value", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("TryGetPropertyValue(geometry, \"Figures\"", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("foreach (var figure in EnumerateObjects(figuresValue))", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("TryAppendPathSegment(segment", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("TypeNameEndsWith(segment, \"LineSegment\")", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var figure in geometry.Figures)", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("geometry.GetType().GetMethod(", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("typeof(global::ProGPU.Scene.DrawingContext)", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.DoesNotContain("geometry.Draw(recordingContext", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("CreateGlyphRunBounds", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGpuWpfPen", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("TryReadDashStyle(pen", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.Contains("nativeDashArray", proGpuWpfCommandSink, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(Path.GetDirectoryName(proGpuWpfCommandSinkPath)!, "ProGpuWpfPen.cs")),
            "The transition ProGpuWpfPen wrapper should stay removed; WPF pen dash metadata belongs on native ProGPU.Vector.Pen.");
        Assert.Contains("internal static bool IsTileBrush(object? brush)", wpfReflectionDrawingReplay, StringComparison.Ordinal);
        Assert.Contains("TypeNameEndsWith(brush, \"ImageBrush\")", wpfReflectionDrawingReplay, StringComparison.Ordinal);
        Assert.Contains("TypeNameEndsWith(brush, \"DrawingBrush\")", wpfReflectionDrawingReplay, StringComparison.Ordinal);
        Assert.Contains("TypeNameEndsWith(brush, \"VisualBrush\")", wpfReflectionDrawingReplay, StringComparison.Ordinal);
        Assert.Contains("TryReplayTileBrushFill(brushValue!", wpfReflectionDrawingReplay, StringComparison.Ordinal);
        Assert.Contains("WpfPortableCommandSinkBridge.PushTransform(sink, relativeTransform)", wpfReflectionDrawingReplay, StringComparison.Ordinal);
        Assert.Contains("WpfPortableCommandSinkBridge.PushTransform(_sink, transform)", wpfCompositionDrawingContext, StringComparison.Ordinal);
        Assert.Contains("nativeSink.PushNativeTransform(nativeTransform)", wpfPortableCommandSinkBridge, StringComparison.Ordinal);
        Assert.Contains("nativeSink.PushNativeTransform(transform)", wpfPortableCommandSinkBridge, StringComparison.Ordinal);
        Assert.Contains("TryCreateManagedMatrixTransform(transform, out var mediaTransform)", wpfPortableCommandSinkBridge, StringComparison.Ordinal);
        Assert.Contains("internal static bool TryCreateManagedMatrixTransform(", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("matrixTransformType = typeof(MediaTransform).Assembly.GetType(\"System.Windows.Media.MatrixTransform\")", wpfReflectionResourceResolver, StringComparison.Ordinal);
        Assert.Contains("private readonly Func<object?, MediaImageSource?>? _imageSourceAdapter", wpfCompositionDrawingContext, StringComparison.Ordinal);
        Assert.Contains("TryReplayTileBrushRectangle(brush, pen, rectangle)", wpfCompositionDrawingContext, StringComparison.Ordinal);
        Assert.Contains("TryReplayTileBrushGeometry(brush, pen, geometry)", wpfCompositionDrawingContext, StringComparison.Ordinal);
        Assert.Contains("WpfReflectionDrawingReplay.TryReplayTileBrushFill(", wpfObjectRenderDataDrawingContext, StringComparison.Ordinal);
        Assert.Contains("if (mediaBrush != null)", wpfObjectRenderDataDrawingContext, StringComparison.Ordinal);
        Assert.Contains("_sink is IWpfNativeTransformCommandSink nativeTransformSink", wpfObjectRenderDataDrawingContext, StringComparison.Ordinal);
        Assert.Contains("WpfReflectionResourceResolver.TryAdaptTransformMatrix(transform, out var nativeTransform)", wpfObjectRenderDataDrawingContext, StringComparison.Ordinal);
        Assert.Contains("OpenCompositionDrawingContext(IWpfImageSourceAdapter? imageSourceAdapter)", proGpuWpfDrawingFrame, StringComparison.Ordinal);
        Assert.Contains("OpenCompositionDrawingContext(activeWpfImageSourceAdapter)", proGpuWpfWindowHost, StringComparison.Ordinal);
        Assert.Contains("ObjectRenderDataDrawingContextReplaysMediaDrawingBrushBeforeGenericMediaBrushPath", wpfCompositionDrawingContextTests, StringComparison.Ordinal);
        Assert.Contains("\"PushNativeTransform\"", wpfCompositionDrawingContextTests, StringComparison.Ordinal);
        Assert.Contains("GeneratedDrawingContextReplaysMediaImageBrushRectangleThroughImageSourceAdapter", wpfCompositionDrawingContextTests, StringComparison.Ordinal);
        Assert.Contains("GeneratedDrawingContextFallsBackToGenericMediaBrushWhenTileReplayUnsupported", wpfCompositionDrawingContextTests, StringComparison.Ordinal);
        Assert.Contains("ObjectRenderDataDrawingContextFallsBackToGenericMediaBrushWhenTileReplayUnsupported", wpfCompositionDrawingContextTests, StringComparison.Ordinal);
        Assert.Contains("ObjectRenderDataDrawingContextPushesReflectedTransformsThroughNativeSink", wpfCompositionDrawingContextTests, StringComparison.Ordinal);
        Assert.Contains("DecodePushTransformFallsBackToLocalMatrixTransformWhenForeignAssemblyShadowsType", wpfReflectionResourceResolverTests, StringComparison.Ordinal);
        Assert.Contains("\"System.Windows.Media.MatrixTransform\"", wpfReflectionResourceResolverTests, StringComparison.Ordinal);
        Assert.Contains("$([MSBuild]::IsOSPlatform('Windows'))", wpfTransportTargets, StringComparison.Ordinal);
        Assert.Contains("<_PowerShellExe Condition=\"'$(_PowerShellExe)' == ''\">pwsh</_PowerShellExe>", wpfTransportTargets, StringComparison.Ordinal);
        Assert.Contains("ValidateManagedWpfTransportPayload", wpfTransportTargets, StringComparison.Ordinal);
        Assert.Contains("$(PackageName.Contains('Microsoft.DotNet.Wpf.GitHub'))", wpfTransportTargets, StringComparison.Ordinal);
        Assert.Contains(@"lib\$(TargetFramework)\PresentationFramework.dll", wpfTransportTargets, StringComparison.Ordinal);
        Assert.Contains(@"ref\$(TargetFramework)\PresentationFramework.dll", wpfTransportTargets, StringComparison.Ordinal);
        Assert.Contains(@"lib\$(TargetFramework)\PresentationFramework.Fluent.dll", wpfTransportTargets, StringComparison.Ordinal);
        Assert.Contains("AddManagedWpfTransportPrivateWinFormsPayload", wpfTransportTargets, StringComparison.Ordinal);
        Assert.Contains("$(PkgMicrosoft_Private_Winforms)", wpfTransportTargets, StringComparison.Ordinal);
        Assert.Contains(@"lib\$(TargetFramework)\System.Private.Windows.Core.dll", wpfTransportTargets, StringComparison.Ordinal);
        Assert.Contains("Build the managed WPF assemblies for $(Configuration)|$(TargetFramework) before packing $(PackageName).", wpfTransportTargets, StringComparison.Ordinal);
        Assert.Contains("<IncludeAssembliesInArchNeutralPackage>true</IncludeAssembliesInArchNeutralPackage>", wpfTransportArchNeutralProject, StringComparison.Ordinal);

        Assert.Contains("<Project Sdk=\"Microsoft.NET.Sdk\">", runtimeHarnessProject, StringComparison.Ordinal);
        Assert.Contains("<OutputType>Exe</OutputType>", runtimeHarnessProject, StringComparison.Ordinal);
        Assert.Contains("<TargetFramework>net11.0</TargetFramework>", runtimeHarnessProject, StringComparison.Ordinal);
        Assert.Contains("<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>", runtimeHarnessProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.NET.Sdk.WindowsDesktop", runtimeHarnessProject, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGPU.Wpf.Sdk", runtimeHarnessProject, StringComparison.Ordinal);

        Assert.Contains("ProGPU.Wpf.SdkSwitchSmoke", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("LibraryAssemblyName = \"ProGPU.Wpf.SdkSwitchLibrary\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK switch library assembly", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RequireOutputRuntimeAssets(appOutputRoot, packageFeed)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLocalProGpuPackagesMatchAvailableRepositoryBuilds(repoRoot, packageFeed)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLocalWpfPackageMatchesAvailableRepositoryBuilds(wpfRoot, packageFeed)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetRepositoryProGpuAssemblyPath(repoRoot, assemblyName)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("local {packageId} package matches {expectedAssemblyDescription}", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("repository WPF transport {assemblyName}.dll", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RequireOutputAssemblyMatchesLocalPackage(appOutputRoot, packageFeed, \"ProGPU.Wpf\", \"ProGPU.Wpf\", \"net10.0\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RequireOutputAssemblyMatchesLocalPackage(appOutputRoot, packageFeed, \"ProGPU.Scene\", \"ProGPU.Scene\", \"net10.0\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RequireOutputAssemblyMatchesLocalPackage(\n                appOutputRoot,\n                packageFeed,\n                \"Microsoft.DotNet.Wpf.GitHub\",\n                assemblyName,\n                \"net11.0\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK switch output {assemblySimpleName}.dll matches local {packageId} package", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ComputeFileSha256(outputPath)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindAssembly(_appOutputRoot, fileName)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("LoadUnmanagedDll(string unmanagedDllName)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetUnmanagedDllCandidates(unmanagedDllName)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PreloadSdkWindowingPlatform(loadContext, inputs.AppOutputRoot)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RegisterSdkNativeResolver(glfwAssembly, appOutputRoot)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Silk.NET.Windowing.Glfw", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ClearPortableActivation(activationServiceType);", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertDelegateTarget(\"_show\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FlushDispatcherOperations(window, \"ApplicationIdle\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"artifacts\",", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"Release\",", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"Microsoft.DotNet.Wpf.GitHub\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"Microsoft.DotNet.Wpf.GitHub.Debug\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"UIAutomationTypes\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"System.Windows.Primitives\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"PresentationFramework.Fluent\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"PresentationFramework.Aero2\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"ProGPU.Compute\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"ProGPU.Transpiler\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"Silk.NET.Windowing.Common\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"Silk.NET.WebGPU\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetNativeAssetCandidates(\"wgpu\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetNativeAssetCandidates(\"glfw\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RequireAnyFile", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"progpu-wpf-sdk-smoke\", \"progpu\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InvokeVoid(app, \"InitializeComponent\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RunSdkPortableBootstrapSmoke", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Wpf.Sdk.ProGpuWpfSdkPortableBootstrap", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RuntimeHelpers.RunModuleConstructor", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK portable bootstrap activation enabled", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK portable bootstrap MessageBox enabled", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK portable bootstrap file dialog enabled", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK portable bootstrap loaded ProGPU.Wpf", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableSystemCommands(presentationFramework, window)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.SystemCommands", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable SDK SystemCommands restore state", runtimeHarnessProgram, StringComparison.Ordinal);

        Assert.Contains("<Project Sdk=\"Microsoft.NET.Sdk\">", externalSdkHarnessProject, StringComparison.Ordinal);
        Assert.Contains("<TargetFramework>net11.0</TargetFramework>", externalSdkHarnessProject, StringComparison.Ordinal);
        Assert.Contains("<RuntimeFrameworkVersion>11.0.0-preview.4.26210.111</RuntimeFrameworkVersion>", externalSdkHarnessProject, StringComparison.Ordinal);
        Assert.DoesNotContain("ProGPU.Wpf.Sdk", externalSdkHarnessProject, StringComparison.Ordinal);
        Assert.Contains("Path.GetTempPath()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Wpf.SdkExternalSmoke", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalSdkApp", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalSdkLibrary", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<Project Sdk=\"ProGPU.Wpf.Sdk/{SdkVersion}\">", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<OutputType>WinExe</OutputType>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<UseWPF>true</UseWPF>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<ProjectReference Include=\"../{LibraryAssemblyName}/{LibraryAssemblyName}.csproj\" />", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Assets/ExternalResource.txt\" />", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Assets/ExternalImage.png\" />", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(appRoot, \"Assets\", \"ExternalResource.txt\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(appRoot, \"Assets\", \"ExternalImage.png\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalThemedControl : Control", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DefaultStyleKeyProperty.OverrideMetadata", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(libraryRoot, \"Themes\", \"Generic.xaml\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ComponentResourceKey TypeInTargetAssembly={x:Type local:ExternalThemedControl}", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalThemeBorderBrush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PROGPU_WPF_EXTERNAL_VALIDATE", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PROGPU_WPF_EXTERNAL_RUN_VALIDATE", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("External SDK Application.Run validation succeeded.", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertContains(locText, \"ExternalLocalizationRoot\",", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertContains(locText, \"ExternalLocalizationText\",", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSystemCommands(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SystemCommands.MaximizeWindow(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK SystemCommands show system menu no-op state", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("xmlns:shell=\"clr-namespace:System.Windows.Shell;assembly=PresentationFramework\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("xmlns:wpf=\"clr-namespace:System.Windows;assembly=PresentationFramework\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<shell:WindowChrome.WindowChrome>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("shell:WindowChrome.IsHitTestVisibleInChrome=\"True\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("shell:WindowChrome.ResizeGripDirection=\"BottomRight\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Static wpf:SystemCommands.MaximizeWindowCommand}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Static wpf:SystemCommands.MinimizeWindowCommand}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Static wpf:SystemCommands.RestoreWindowCommand}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Static wpf:SystemCommands.ShowSystemMenuCommand}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalSystemCommandExecuted", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSystemCommandButton(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK XAML WindowChrome attached value", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK XAML SystemCommands maximize", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Startup=\"OnExternalAppStartup\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Exit=\"OnExternalAppExit\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalStartupResourceText", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Normal", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateApplicationRunAndShutdown", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateApplicationExit", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Render", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Closing=\"OnExternalWindowClosing\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Closed=\"OnExternalWindowClosed\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateApplicationWindowLifetime(app, window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CancelNextExternalWindowClose", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("External SDK secondary window", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ApplicationContainsWindow(app, secondaryWindow)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK application exit count before main close", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("External SDK owned window", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Owner = window", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window.OwnedWindows.Count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK owned window Closing attempted cancel state", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("External SDK modal dialog", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("modalDialog.ShowDialog()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("modalDialog.DialogResult = true", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK modal dialog result", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ShutdownMode.OnMainWindowClose", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("App.ExternalStartupEventCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("App.ExternalExitEventCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalRunValidated", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Resources[\"ExternalStartupText\"]", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Resources[\"ExternalStartupBrush\"]", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("app.MainWindow", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("app.Windows.Count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ShutdownMode.OnLastWindowClose", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"ExternalLocalizedText\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Localization.Attributes=\"$Content (Readable Modifiable Text)\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Localization.Comments=\"$Content (External SDK localization comment)\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("LocalizationAssemblyName = \"ExternalLocalizationApp\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PrepareExternalLocalizationProject(workRoot)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<LocalizationDirectivesToLocFile>All</LocalizationDirectivesToLocFile>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateExternalLocalizationDirectives(workRoot)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("LocalizedView.loc", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalLocalizationRoot", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("External localization text comment", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(appRoot, \"ExternalResources.xaml\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<ResourceDictionary Source=\"ExternalResources.xaml\" />", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalStaticBrush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ComponentResourceKey TypeInTargetAssembly={x:Type local:MainWindow}, ResourceId=ExternalComponentAccentBrush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalComponentResourceText", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalDynamicBrush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalRuntimeMergedResourceText", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalRuntimeMergedText", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalRuntimeMergedBrush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalFreezableBrush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalFreezableGradientBrush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("LinearGradientBrush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GradientStop Color=\"#2F6B54\" Offset=\"0\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GradientStop Color=\"#B15E3B\" Offset=\"0.5\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalStaticText", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalObjectDataProvider", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ObjectDataProvider.MethodParameters", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalResourceFactory", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalXmlDataProvider", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:XData", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalObjectProviderText", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalXmlProviderText", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalMarkupExtensionText", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Text=\"{local:ExternalText Prefix=external, Value=markup}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("public sealed class ExternalTextExtension : MarkupExtension", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("public override object ProvideValue(IServiceProvider serviceProvider)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("IProvideValueTarget", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMarkupExtensions(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK compiled MarkupExtension provided text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalButtonTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalBasedButtonStyle", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalTriggeredButtonStyle", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalEventSetterButtonStyle", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource ExternalBasedButtonStyle}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<EventSetter", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Handler=\"OnExternalStyleEventButtonClick\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"IsEnabled\" Value=\"False\">", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Background=\"{TemplateBinding Background}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Content=\"{TemplateBinding Content}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<VisualStateManager.VisualStateGroups>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<VisualStateGroup x:Name=\"ExternalCommonStates\">", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<VisualState x:Name=\"Pressed\">", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetProperty=\"Opacity\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<ControlTemplate.Triggers>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"Tag\" Value=\"template-trigger-active\">", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK control template trigger action EnterActions target name", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run control template trigger EnterActions MinWidth", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("using System.Windows.Media.Animation;", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalLoadedStoryboardText\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Loaded=\"OnExternalLoadedStoryboardTextLoaded\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("EventTrigger RoutedEvent=\"FrameworkElement.Loaded\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("BeginStoryboard", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetName=\"ExternalLoadedStoryboardText\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExternalPropertyTriggerActionTextStyle\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Trigger.EnterActions", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Trigger.ExitActions", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalPropertyTriggerActionText\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExternalMultiTriggerActionTextStyle\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("MultiTrigger.EnterActions", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("MultiTrigger.ExitActions", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalMultiTriggerActionText\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExternalDataTriggerActionTextStyle\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DataTrigger.EnterActions", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DataTrigger.ExitActions", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetProperty=\"Opacity\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalDataTriggerActionText\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExternalMultiDataTriggerActionTextStyle\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("MultiDataTrigger.EnterActions", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("MultiDataTrigger.ExitActions", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalMultiDataTriggerActionText\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource ExternalTriggeredButtonStyle}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalEventSetterButton\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource ExternalEventSetterButtonStyle}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalStyleEventButtonClickCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalStyleEventButtonClick", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalLoadedStoryboardTextLoadedCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalLoadedStoryboardTextLoaded", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateStylesAndTemplates(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLoadedStoryboardMetadata(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLoadedStoryboardAfterRun(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePropertyTriggerActionsMetadata(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePropertyTriggerActionsAfterRun(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMultiTriggerActionsMetadata(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMultiTriggerActionsAfterRun(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateDataTriggerActionsMetadata(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateDataTriggerActionsAfterRun(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMultiDataTriggerActionsMetadata(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMultiDataTriggerActionsAfterRun(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("triggeredStyle.BasedOn", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("triggeredStyle.Triggers.Count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("eventSetterStyle.Setters.OfType<EventSetter>()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("eventSetterButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, eventSetterButton))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK EventSetter routed event", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("buttonTemplate.FindName(\"ExternalTemplateRoot\", styledButton)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("VisualStateManager.GetVisualStateGroups(templateRoot)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Storyboard.GetTargetProperty(doubleAnimation)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loaded storyboard target property path", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run loaded storyboard opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertTriggerActionStoryboard(multiTrigger.EnterActions[0], 0.58, \"external SDK multi trigger action EnterActions\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertTriggerActionStoryboard(multiTrigger.ExitActions[0], 0.88, \"external SDK multi trigger action ExitActions\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertTriggerActionStoryboard(dataTrigger.EnterActions[0], 0.31, \"external SDK data trigger action EnterActions\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertTriggerActionStoryboard(dataTrigger.ExitActions[0], 0.82, \"external SDK data trigger action ExitActions\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertTriggerActionStoryboard(multiDataTrigger.EnterActions[0], 0.24, \"external SDK multi data trigger action EnterActions\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertTriggerActionStoryboard(multiDataTrigger.ExitActions[0], 0.76, \"external SDK multi data trigger action ExitActions\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run multi trigger action partial-condition opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run multi trigger EnterActions opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run multi trigger ExitActions opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run multi trigger re-enter opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run data trigger EnterActions opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run data trigger ExitActions opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run multi data trigger action partial-condition opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run multi data trigger EnterActions opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run multi data trigger ExitActions opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateVisualStateTransitions(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("VisualStateManager.GoToState(styledButton, \"Pressed\", false)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run VisualStateManager Pressed opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Application.Run VisualStateManager Normal transition", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK property trigger background setter", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK TemplateBinding triggered background", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalItemsPanelTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalItemContainerStyle", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalGroupedItems", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalGroupHeaderTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalFrameworkItemTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalRenderingItemTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalDefaultItemTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalItemTemplateSelector", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DefaultTemplate=\"{StaticResource ExternalDefaultItemTemplate}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalFrameworkItemContainerStyle", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalDefaultItemContainerStyle", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalItemContainerStyleSelector", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyleSelector=\"{StaticResource ExternalItemContainerStyleSelector}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalStyleSelectorItemsList", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalStyleSelectorItemTextBlock", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalFilteredItems", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Filter=\"OnExternalItemsFilter\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalLiveFilteredItems", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("IsLiveFilteringRequested=\"True\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<CollectionViewSource.LiveFilteringProperties>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<sys:String>IsActive</sys:String>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalLiveSortedItems", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("IsLiveSortingRequested=\"True\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<CollectionViewSource.LiveSortingProperties>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalLiveFilteredItemsList", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalLiveSortedItemsList", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalLiveGroupedItems", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("IsLiveGroupingRequested=\"True\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<CollectionViewSource.LiveGroupingProperties>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<sys:String>Kind</sys:String>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalLiveGroupedItemsList", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalCurrencyItems", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("IsSynchronizedWithCurrentItem=\"True\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalCompositeItemsList", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<CompositeCollection>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CollectionContainer Collection=\"{x:Static local:ExternalCompositeProvider.Items}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("public static class ExternalCompositeProvider", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<CollectionViewSource.SortDescriptions>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("componentModel:SortDescription", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PropertyName=\"Name\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<CollectionViewSource.GroupDescriptions>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PropertyGroupDescription PropertyName=\"Kind\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalGroupedItemsList", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<ListBox.GroupStyle>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GroupStyle HeaderTemplate=\"{StaticResource ExternalGroupHeaderTemplate}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalLayoutGrid", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalDockPanel", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalCanvasChild", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalUniformGrid", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalGridSplitterGrid", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalGridSplitter", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalItemsPanelList", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalTemplateSelectorPresenter", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ContentTemplateSelector=\"{StaticResource ExternalItemTemplateSelector}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalTemplateSelectorItems", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ItemTemplateSelector=\"{StaticResource ExternalItemTemplateSelector}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalListView", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<ListView.View>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GridViewColumn", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberBinding=\"{Binding Name}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberBinding=\"{Binding Kind}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalDataGrid", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AutoGenerateColumns=\"False\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CanUserAddRows=\"False\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DataGridTextColumn", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DataGridCheckBoxColumn", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IsActive}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("public bool IsActive", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(IsActive))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLayoutsAndItems(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Grid.GetColumnSpan(gridValue)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DockPanel.GetDock(dockTop)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Canvas.GetLeft(canvasChild)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK GridSplitter resize behavior", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateGridSplitterDragAfterRun(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK GridSplitter dragged left column width", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK GridSplitter dragged right column width", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("itemsPanelTemplate.LoadContent()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("itemPanelList.ItemContainerStyle", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK item panel list collection count after mutation", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK item template selector framework template", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK content template selector selected template", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertTemplateText(frameworkTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK rendering selected template text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK item template selector collection count after mutation", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK item template selector default selected template", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ListBox ItemContainerStyleSelector", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateItemContainerStyleSelectorAfterRun(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ItemContainerStyleSelector framework generated container style", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ItemContainerStyleSelector default generated TextBlock text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindVisualDescendantByName(itemContainer, \"ExternalStyleSelectorItemTextBlock\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK grouped CollectionViewSource sort property", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK grouped CollectionViewSource group property", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK grouped ListBox ItemsSource view", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK grouped CollectionViewSource view group count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK group header generated text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK filtered CollectionViewSource filter event count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("filteredItems.View.Refresh()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK filtered ListBox refreshed item count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK currency current item from selection", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("currencyItems.View.MoveCurrentToPosition(2)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK currency ListBox selected item after current move", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK CompositeCollection source part count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK CompositeCollection static source items", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK CompositeCollection initial flattened item count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK CompositeCollection collection-change appended source item", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK list view grid-view column count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK list view name binding path", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK data grid column count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK data grid active binding path", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK data grid selected item after change", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalNodeTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<HierarchicalDataTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DataType=\"{x:Type local:ExternalNode}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Children}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalTreeView", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalExplicitTreeView", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalTreeRootItem", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Expanded=\"OnExternalTreeItemExpanded\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Selected=\"OnExternalTreeItemSelected\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ObservableCollection<ExternalNode> ExternalNodes", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalTreeExpandedCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalTreeSelectedCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("nodeTemplate.ItemsSource", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK node template ItemsSource path", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK tree selected original source", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK tree unselected event count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalMenu", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalCommandMenuItem", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalCheckableMenuItem", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalPopupOwnerButton", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalToolTip", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalContextMenu", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalContextCheckableMenuItem", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalCheckBox", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalRadioAlpha", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalToggleButton", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalToolBarTray", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalToolBarCommandButton", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalStatusBar", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalPasswordBox", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalCalendar", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalDatePicker", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalSlider", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalProgressBar", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalRepeatButton", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalScrollBar", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalDragThumb", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalMenuItemClick", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalContextMenuItemChecked", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalRadioButtonChecked", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalPasswordChanged", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalSliderValueChanged", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalRepeatButtonClick", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalScrollBarScroll", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalThumbDragStarted", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalThumbDragDelta", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalThumbDragCompleted", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalBubbledThumbDragDelta", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMenusAndChoiceControls(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateToolbarStatusRangePasswordDateControls(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateThumbDragManager(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK menu command executed count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK context command executed count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK tooltip placement", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK check box unchecked routed event", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK radio unchecked sender", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK toggle unchecked routed event", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK toolbar routed command count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK status bar selected item binding", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK PasswordBox secure password length", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Calendar selected date collection item", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK DatePicker selected date format", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProgressBar ElementName binding", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProgressBar value after Slider update", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK RepeatButton routed event", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ScrollBar LineDown command", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ScrollBar ScrollToBottom command", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Thumb DragStarted handler count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Thumb DragDelta horizontal change", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Thumb bubbled DragDelta original source", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Thumb DragCompleted canceled state", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalAdornerDecorator", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalAdornedButton", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("public sealed class ExternalAdorner : Adorner", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateAdornerDecorator(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateAdornerLayer(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK AdornerLayer added adorner", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalComboBox", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SelectedValuePath=\"Kind\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding SelectedExternalKind, Mode=TwoWay}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"OnExternalSelectionChanged\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalTabControl", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalGroupBox", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalExpander", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Expanded=\"OnExternalExpanderExpanded\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Collapsed=\"OnExternalExpanderCollapsed\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalScrollViewer", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SelectedExternalKind { get; set; } = \"Rendering\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalSelectionChangedCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalExpanderExpandedCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSelectorsAndContent(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("comboBox.GetBindingExpression(Selector.SelectedValueProperty)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK combo box two-way selected value source update", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("tabControl.SelectedIndex = 0", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK tab selection source name", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK group box content binding", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK expander expanded event count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK scroll viewer vertical visibility", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalRichTextBox", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FlowDocument PagePadding=\"4\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalDocumentLink", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("NavigateUri=\"https://example.test/external-sdk\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RequestNavigate=\"OnExternalDocumentLinkRequestNavigate\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InlineUIContainer", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("BlockUIContainer", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<Table CellSpacing=\"0\">", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalDocumentLinkRequestNavigateCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("using System.Windows.Documents;", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRichDocuments(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("hyperlink.DoClick()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Hyperlink RequestNavigate routed event", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK FlowDocument list marker style", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK FlowDocument table cell count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRichTextEditingCommands(richTextBox, introParagraph, plainRun, documentList)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("EditingCommands.ToggleBold.Execute(null, richTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK RichTextBox ToggleBold applied weight", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("EditingCommands.ToggleItalic.Execute(null, richTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK RichTextBox ToggleItalic applied style", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("EditingCommands.ToggleUnderline.Execute(null, richTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK RichTextBox ToggleUnderline decoration location", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("EditingCommands.AlignRight.Execute(null, richTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK RichTextBox AlignCenter paragraph alignment", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("EditingCommands.ToggleBullets.Execute(null, richTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK RichTextBox ToggleNumbering marker style", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK RichTextBox selection text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new TextRange(document.ContentStart, document.ContentEnd).Text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalSpellCheckTextBox", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SpellCheck.IsEnabled=\"True\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSpellCheck(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK SpellCheck no-op next spelling error", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK SpellCheck custom dictionary add count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK SpellCheck disabled instance value", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("if (!System.OperatingSystem.IsWindows())", spellerInteropBase, StringComparison.Ordinal);
        Assert.Contains("return new NullSpellerInterop();", spellerInteropBase, StringComparison.Ordinal);
        Assert.Contains("private sealed class NullSpellerInterop : SpellerInteropBase", spellerInteropBase, StringComparison.Ordinal);
        Assert.Contains("internal override bool CanSpellCheck(CultureInfo culture)", spellerInteropBase, StringComparison.Ordinal);
        Assert.Contains("return false;", spellerInteropBase, StringComparison.Ordinal);
        Assert.Contains("ExternalItemTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DataType=\"{x:Type local:ExternalItem}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ObservableCollection<ExternalItem>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateApplicationResources(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePackResources()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Application.GetResourceStream(resourceUri)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("pack://application:,,,/Assets/ExternalResource.txt", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK relative Resource stream text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK absolute pack Resource stream text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new ComponentResourceKey(typeof(MainWindow), \"ExternalComponentAccentBrush\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ComponentResourceKey application brush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ComponentResourceKey window lookup", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ComponentResourceKey foreground", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Localization.GetComments(localizedText)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Localization.GetAttributes(localizedText)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK x:Uid value", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Localization.Attributes", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK localization root directive uid", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK localization text directive uid", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK localization modifiable attribute output", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK localization unmodifiable attribute output", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateFreezableResources()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Freezable brush clone mutable opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Freezable brush current-value clone opacity", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Freezable gradient stop collection frozen state", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Freezable gradient clone mutable stop offset", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Freezable gradient current-value clone stop collection", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateManagedFrameworkCollections()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("MS.Internal.ListOfObject", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ListOfObject insert forwards to IList", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ListOfObject clear forwards to IList", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("MS.Internal.WeakDictionary`2", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK WeakDictionary key collection contains", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK WeakDictionary value collection contains", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK WeakDictionary key collection add rejected", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK WeakDictionary value collection clear rejected", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("using System.Windows.Media.Imaging;", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateManagedImagingObjects()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("BitmapSource.Create(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("BitmapPalettes.BlackAndWhite", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapPalettes.WebPalette color count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new BitmapPalette(bitmapSource, 4)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapPalette from BGRA source first color", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new BitmapPalette(indexedPaletteSource, 3)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapPalette from Indexed8 source third color", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("BitmapFrame.Create(bitmapSource)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new BmpBitmapEncoder()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("bmpEncoder.Save(bmpStream)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BmpBitmapEncoder bottom-left blue byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("BitmapDecoder.Create(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new BmpBitmapDecoder(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create BMP top-left blue byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new BitmapImage(bmpUri)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create URI BMP decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapImage URI BMP top-left blue byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new PngBitmapDecoder(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create PNG decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create PNG top-left blue byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create URI PNG decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new BitmapImage(pngUri)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapImage URI PNG top-left blue byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("pack://application:,,,/Assets/ExternalImage.png", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create pack PNG decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK PngBitmapDecoder pack URI frame count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapImage pack PNG top-left red byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalXamlResourceImage\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Source=\"Assets/ExternalImage.png\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalXamlImageBrushRectangle\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<ImageBrush ImageSource=\"pack://application:,,,/Assets/ExternalImage.png\" />", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK XAML resource image top-left red byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK XAML ImageBrush top-right green byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreateAdam7RgbaPngBytes(pixels, 2, 2, 8)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create interlaced PNG decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create interlaced PNG bottom-right red byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create URI interlaced PNG decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new BitmapImage(interlacedPngUri)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapImage URI interlaced PNG bottom-right red byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreatePngIconBytes(pngBytes, 2, 2)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new IconBitmapDecoder(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create ICO decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create ICO top-left blue byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create URI ICO decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new BitmapImage(iconUri)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapImage URI ICO top-left blue byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreateDibIconBytes(pixels, 2, 2, 8)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create DIB ICO decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create DIB ICO masked alpha byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create URI DIB ICO decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapImage URI DIB ICO masked alpha byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreateJpegBytes()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new JpegBitmapDecoder(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create JPEG decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create JPEG nonblank RGB total", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create URI JPEG decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new BitmapImage(jpegUri)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapImage URI JPEG first alpha byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreateGifBytes()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new GifBitmapDecoder(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create GIF decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create GIF first-frame delay metadata", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create GIF second-frame delay metadata", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("firstGifMetadata.ContainsQuery(\"/grctlext/Delay\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("firstGifMetadata.GetQuery(\"/imgdesc/Width\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create GIF second-frame green byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK GifBitmapDecoder frame count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK GifBitmapDecoder second-frame delay metadata", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK GifBitmapDecoder URI second-frame green byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create GIF nonblank RGB total", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create URI GIF decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create URI GIF second-frame delay metadata", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK GifBitmapDecoder URI first-frame delay metadata", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new BitmapImage(gifUri)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapImage URI GIF first alpha byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreateTiffBytes(pixels, 2, 2)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new TiffBitmapDecoder(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create TIFF decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create TIFF metadata format", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create TIFF orientation query presence", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create TIFF orientation metadata", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK TiffBitmapDecoder orientation metadata", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create TIFF bottom-right red byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create URI TIFF decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create URI TIFF orientation metadata", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK TiffBitmapDecoder URI orientation metadata", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new BitmapImage(tiffUri)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapImage URI TIFF top-left blue byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("WriteTiffShortEntry(tiff, ref entryOffset, 274, 6)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreateMultiFrameTiffBytes(pixels, secondTiffPixels, 2, 2)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create multi-frame TIFF frame count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK multi-frame TiffBitmapDecoder frame count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create URI multi-frame TIFF frame count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreatePaletteTiffBytes([0, 1, 2, 3], 2, 2, 4)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create palette TIFF decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create URI palette TIFF decoder type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapImage URI palette TIFF bottom-right red byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreateRgbaPngBytes(pixels, 2, 2, 8)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreateRgba16PngBytes(pixels, 2, 2, 8)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreateIndexedPngBytes(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create 16-bit RGBA PNG bottom-right red byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapDecoder.Create Indexed4 PNG bottom-left alpha byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PixelFormats.Indexed8", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Indexed8 BitmapDecoder palette green", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Indexed8 BitmapImage URI bottom-left index", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("WriteableBitmap(2, 2, 96.0, 96.0, PixelFormats.Bgra32", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapSource copied blue byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BitmapFrame copied red byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK WriteableBitmap copied second-row blue byte", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Image source BitmapFrame", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ImageBrush source BitmapSource", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("using System.Windows.Markup;", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlReaderWriter()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XamlReader.Parse(looseXaml)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XamlWriter.Save(brush)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalLooseAccentBrush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalLooseTextStyle", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlReader style StaticResource brush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlReader Binding path", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalLooseInputScopeTextBox", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external-loose-input-scope", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InputScopeNameValue.EmailUserName", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlReader InputScopeName value", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlReader InputScopePhrase text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter serialized GradientStop", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip second stop color", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("MenuItem.SeparatorStyleKey", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XamlWriter.Save(systemResourceDictionary)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter serialized system resource key member", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip system-key style target", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalWriterBaseButtonStyle", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalWriterButtonStyle", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XamlWriter.Save(styleDictionary)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter serialized style BasedOn", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip style BasedOn setter", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter styled Button inherited Tag", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalWriterButtonTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XamlWriter.Save(templateDictionary)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter serialized ControlTemplate triggers", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip ControlTemplate trigger property", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter applied ControlTemplate content presenter", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalWriterDataTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XamlWriter.Save(dataTemplateDictionary)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlReader DataTemplate name binding path", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter serialized DataTemplate triggers", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip DataTemplate trigger binding path", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter DataTemplate trigger setter target", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip DataTemplate kind TextBlock name", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalWriterNodeTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XamlWriter.Save(hierarchicalTemplateDictionary)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlReader HierarchicalDataTemplate ItemsSource path", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlReader HierarchicalDataTemplate name binding path", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter serialized HierarchicalDataTemplate triggers", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip HierarchicalDataTemplate trigger binding path", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter HierarchicalDataTemplate trigger setter target", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip HierarchicalDataTemplate kind TextBlock name", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalWriterItemsPanelTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XamlWriter.Save(itemsPanelTemplateDictionary)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter serialized ItemsPanelTemplate panel", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip ItemsPanelTemplate panel name", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip ItemsPanelTemplate orientation", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip ItemsPanelTemplate item width", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalWriterGroupStyle", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XamlWriter.Save(groupStyleDictionary)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlReader GroupStyle header binding path", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter serialized GroupStyle HeaderTemplate", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter serialized GroupStyle Panel", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip GroupStyle HidesIfEmpty", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip GroupStyle header TextBlock name", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip GroupStyle panel orientation", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalWriterElementRoot", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XamlWriter.Save(frameworkElementRoot)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter serialized FrameworkElement button", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip FrameworkElement root name", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip FrameworkElement button background", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip FrameworkElement TextBox text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalWriterParagraph", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XamlWriter.Save(flowDocument)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter serialized FlowDocument root", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip FlowDocument paragraph name", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip FlowDocument table cell count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip FlowDocument TextRange second list item", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateThemeTemplateXamlWriterRoundTrip(window, themedControl)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XamlWriter.Save(themeTemplate)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter serialized themed ControlTemplate root", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip themed ControlTemplate target", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK loose XamlWriter round-trip themed ControlTemplate component resource brush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateDataProviders(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ObjectDataProvider data", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ObjectDataProvider bound text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ObjectDataProvider binding source", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK XmlDataProvider bound text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK XmlDataProvider binding XPath", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalUpperConverter", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalSummaryConverter", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("public sealed class ExternalItemTemplateSelector : DataTemplateSelector", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FrameworkTemplate { get; set; }", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RenderingTemplate { get; set; }", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DefaultTemplate { get; set; }", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("public sealed class ExternalItemContainerStyleSelector : StyleSelector", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FrameworkStyle { get; set; }", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DefaultStyle { get; set; }", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalNonEmptyValidationRule", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalBindingGroupValidationRule", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Converter={StaticResource ExternalUpperConverter}", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<MultiBinding Converter=\"{StaticResource ExternalSummaryConverter}\">", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<PriorityBinding>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalBindingTransferTextBox\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SourceUpdated=\"OnExternalBindingSourceUpdated\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("TargetUpdated=\"OnExternalBindingTargetUpdated\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalBindingTransferText, Mode=TwoWay, UpdateSourceTrigger=Explicit, NotifyOnSourceUpdated=True, NotifyOnTargetUpdated=True", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalAncestorBindingBorder\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding RelativeSource={RelativeSource AncestorType={x:Type Border}}, Path=Tag}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<local:ExternalNonEmptyValidationRule />", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalBindingGroupPanel\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<BindingGroup Name=\"ExternalBindingGroup\">", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FirstProperty=\"BindingGroupFirstName\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SecondProperty=\"BindingGroupLastName\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalBindingGroupFirstBox\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding BindingGroupFirstName, UpdateSourceTrigger=Explicit}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalBindingGroupLastBox\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding BindingGroupLastName, UpdateSourceTrigger=Explicit}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalRoutedEventControl\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalBubble=\"OnExternalCustomBubble\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalTunnel=\"OnExternalCustomTunnel\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RegisterRoutedEvent(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RoutingStrategy.Bubble", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RoutingStrategy.Tunnel", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalDependencyPropertyControl\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("local:ExternalDependencyPropertyControl.InheritedLabel=\"External inherited label\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CoercedNumber=\"120\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("TrackedText=\"compiled tracked text\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FrameworkPropertyMetadataOptions.Inherits", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CoerceNumber", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("TextChanged=\"OnExternalValidationTextChanged\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Validation.Error=\"OnExternalValidationError\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("NotifyOnValidationError=\"True\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalBindingTransferText { get; set; } = \"external transfer initial\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalBindingSourceUpdatedCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalBindingTargetUpdatedCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalBindingSourceUpdated", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalBindingTargetUpdated", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InputLanguageManager.InputLanguage=\"en-US\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InputMethod.PreferredImeState=\"On\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InputMethod.PreferredImeConversionMode=\"Native, FullShape\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InputMethod.PreferredImeSentenceMode=\"Automatic\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RegularExpression=\"[A-Z0-9]+\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SrgsMarkup=\"external-sdk-input-scope\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<InputScopeName>EmailSmtpAddress</InputScopeName>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<InputScopePhrase>external package phrase</InputScopePhrase>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidationText { get; set; } = \"valid external text\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("BindingGroupFirstName { get; set; } = \"group: Ada\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("BindingGroupLastName { get; set; } = \"group: Lovelace\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalValidationTextChangedCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalValidationTextChanged", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalValidationErrorAddedCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalValidationErrorRemovedCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnExternalValidationError", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateBindings(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateInputManagers(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AllowDrop=\"True\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PreviewDragEnter=\"OnExternalPreviewDragEnter\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DragEnter=\"OnExternalDragEnter\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PreviewDragOver=\"OnExternalPreviewDragOver\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DragOver=\"OnExternalDragOver\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PreviewDragLeave=\"OnExternalPreviewDragLeave\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DragLeave=\"OnExternalDragLeave\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PreviewDrop=\"OnExternalPreviewDrop\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Drop=\"OnExternalDrop\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableDragDrop(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.PortableWindowActivationService", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"ProcessDragDropEvent\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"ProcessDragDrop\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalPreviewDragEnterCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalDragOverCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalDragLeaveCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalPreviewDropCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalDropCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable drag-enter accepted effect", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable drag-over accepted effect", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable drag-leave fallback effect", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable drag/drop accepted effect", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable drag/drop wrapper accepted effect", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable drag/drop first file", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateBindingGroup(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRoutedEvents(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateDependencyProperties(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("BindingOperations.GetMultiBindingExpression", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("BindingOperations.GetPriorityBindingExpression", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ancestorBindingExpression.ParentBinding.RelativeSource", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RelativeSourceMode.FindAncestor", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK RelativeSource ancestor binding value", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Validation.GetHasError(validationTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Validation.GetErrors(validationTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK validation error added count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK validation error added routed event", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK validation error removed count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK validation error removed sender", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InputMethod.GetInputScope(validationTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK compiled InputScopeName value", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK compiled InputScopePhrase text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InputLanguageManager.Current", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InputLanguageManager.GetInputLanguage(validationTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK InputLanguageManager set current language", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InputMethod.GetPreferredImeState(validationTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK InputMethod set conversion mode", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK binding transfer NotifyOnSourceUpdated", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK binding transfer NotifyOnTargetUpdated", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Binding SourceUpdated property", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Binding TargetUpdated routed event name", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK validation source update", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalExceptionValidationTextBox", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<ExceptionValidationRule />", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("public string ExceptionValidationText", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("External exception validation rejected value.", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ExceptionValidationRule count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK exception validation rejected source value", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK exception validation recovered source value", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("bindingGroup.ValidateWithoutUpdate()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("bindingGroup.CommitEdit()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BindingGroup rejected commit", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK BindingGroup accepted first source", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("control.RaiseExternalBubble()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK custom bubble AddHandler panel sender", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("control.RaiseExternalTunnel()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK custom tunnel AddHandler panel sender", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DependencyPropertyHelper.GetValueSource(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK inherited attached property value source", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK coerced dependency property source flag", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK dependency property changed callback new value", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validationTextBox.Select(9, 7)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validationTextBox.SelectedText = \"selection\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validationTextBox.AppendText(\" appended\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK TextBox selected text replacement", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK TextBox editing source update", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validationTextBox.UndoLimit = 8", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validationTextBox.Undo()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validationTextBox.Redo()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK TextBox empty Undo text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK TextBox empty Redo text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<Window.CommandBindings>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<Window.InputBindings>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Static local:MainWindow.ExternalCommand}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CanExecute=\"OnExternalCommandCanExecute\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Executed=\"OnExternalCommandExecuted\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Gesture=\"Ctrl+E\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<MouseBinding", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Gesture=\"LeftDoubleClick\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalMouseCommandParameter", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FocusManager.FocusedElement=\"{Binding ElementName=ExternalCommandButton}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigation.TabNavigation=\"Cycle\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalAccessLabel", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Target=\"{Binding ElementName=ExternalValidationTextBox}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ExternalValidationTextBoxAutomation\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LabeledBy=\"{Binding ElementName=ExternalAccessLabel}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.GetAutomationId(validationTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.GetLabeledBy(validationTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("UIElementAutomationPeer.CreatePeerForElement(validationTextBox)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("UIElementAutomationPeer.CreatePeerForElement(accessLabel)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validationPeer.GetLabeledBy()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalStandaloneAccessText", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Text=\"_External standalone access\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalKeyboardNavigationPanel\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalKeyboardNavigationFirstButton\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalKeyboardNavigationSecondButton\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigation.GetTabNavigation(keyboardNavigationPanel)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateKeyboardNavigationAfterRun(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK KeyboardNavigation cycled previous button", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PresentationSource.FromVisual(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AccessKeyManager.IsKeyRegistered(presentationSource, \"E\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AccessKeyManager.ProcessKey(presentationSource, \"E\", false)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK access-key manager focused label target", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnExternalCommandButtonClick\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalRequeryCommandButton", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ExternalRequeryCommand}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CommandManager.RequerySuggested += value", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CommandManager.InvalidateRequerySuggested()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK requery command enabled state", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK requery command execute count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalClassCommandTarget\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalClassCommandButton\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Static local:ExternalClassCommandTextBox.ExternalClassCommand}\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"ExternalClassCommandParameter\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("public sealed class ExternalClassCommandTextBox : TextBox", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CommandManager.RegisterClassCommandBinding", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CommandManager.RegisterClassInputBinding", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalClassInputParameter", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK class command executed count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateClassInputBindingAfterRun(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("CreatePortableInputEvent(\"KeyDown\", key: \"F8\", modifiersName: \"Control\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("HandlePortableInput(window, keyDown)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK class input binding key event handled", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK class input binding command parameter", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK class input binding ignores key up", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateCommandsAndFocus(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window.CommandBindings.Count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window.InputBindings.Count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RequireType<MouseBinding>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("MouseAction.LeftDoubleClick", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK mouse binding command executed count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigation.GetDirectionalNavigation(focusPanel)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK label access-key target", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK standalone access text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("MainWindow.ExternalCommand.Execute(\"DirectCommandParameter\", commandButton)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("commandButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, commandButton))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("commandButton.CommandParameter, commandButton", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK button command parameter", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("appResources.MergedDictionaries.Count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalUnsharedBrush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Shared=\"False\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalUnsharedBrushTextA", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalUnsharedBrushTextB", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK x:Shared=false StaticResource consumers", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK x:Shared=false dictionary lookup", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("appResources[\"ExternalDynamicBrush\"] = new SolidColorBrush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK updated dynamic resource foreground", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("appResources.MergedDictionaries.Add(runtimeMergedDictionary)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK runtime merged dynamic resource text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK updated runtime merged dynamic resource foreground", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSystemParameters(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateWindowChrome(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("WindowChrome.SetWindowChrome(window, chrome)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK WindowChrome attached value", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.FocusBorderWidth", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.PrimaryScreenWidthKey", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.WorkArea", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.MenuShowDelay", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.ClientAreaAnimation", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.ForegroundFlashCount", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.WheelScrollLines", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK SystemParameters.{propertyName} resource", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLauncher()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.PortableLauncherService", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable launcher service enabled", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable launcher handled request", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable launcher request URI", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable launcher target frame", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMessageBox(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.PortableMessageBoxService", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable MessageBox service enabled", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RegisterDeterministicMessageBox(serviceType)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ShowDeterministicMessageBox", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK MessageBox no-owner default result", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK MessageBox owner fallback result", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateFileDialogs(window)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Win32.PortableFileDialogService", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable file dialog service enabled", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK OpenFileDialog FileName", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK owner SaveFileDialog FileName", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK owner OpenFolderDialog FolderName", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK file dialog request count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateClipboard()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetText(\"external SDK clipboard text\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Clipboard data object unicode text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Clipboard current data object", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetDataObject(customDataObject, copy: true)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ExternalSdkCustomFormat", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("currentDataObject.TryGetData(\"ExternalSdkCustomFormat\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Clipboard typed custom data retrieval", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetFileDropList(fileDropList)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Clipboard.GetFileDropList()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Clipboard file-drop count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Clipboard cleared file-drop state", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK Clipboard cleared text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExternalDataTriggeredTextStyle\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<DataTrigger Binding=\"{Binding IsExternalDataTriggerActive}\" Value=\"True\">", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ExternalMultiDataTriggeredTextStyle\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<MultiDataTrigger>", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<Condition Binding=\"{Binding IsExternalMultiTriggerReady}\" Value=\"True\" />", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("public partial class MainWindow : Window, INotifyPropertyChanged", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(IsExternalDataTriggerActive))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(IsExternalMultiTriggerReady))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(IsExternalDataTriggerActionActive))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(IsExternalMultiDataTriggerActionReady))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(IsExternalMultiDataTriggerActionArmed))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("public sealed class ExternalItem : INotifyPropertyChanged", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(IsActive))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(Name))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK data trigger active text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK multi data trigger one-condition text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK multi data trigger active tag", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK multi data trigger exit text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("template.LoadContent()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK item template name binding", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window.ExternalItems.Add(new ExternalItem(\"Gamma\", \"Data\"))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK bound items count after collection change", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK live filtered CollectionViewSource live update item count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window.ExternalLiveItems[1].IsActive = true", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK live sorted CollectionViewSource live resort first item", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window.ExternalLiveItems[2].Name = \"Live Aaron\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK live grouped CollectionViewSource live regroup Framework count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window.ExternalLiveItems[2].Kind = \"Framework\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK live grouped CollectionViewSource live regroup removed Data group", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExternalVirtualizingItemsList\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("VirtualizingStackPanel Orientation=\"Vertical\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK VirtualizingPanel IsVirtualizing attached value", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK VirtualizingPanel virtualization mode", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new MainWindow()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window.FindName(\"ExternalThemedControl\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("themedControl.Template.FindName(\"ThemeRoot\", themedControl)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("themedControl.Template.FindName(\"ThemeText\", themedControl)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK themed control TemplateBinding text", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK themed control component resource brush", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK library Generic.xaml source", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(appRoot, \"ExternalPage.xaml\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(appRoot, \"ExternalSecondPage.xaml\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Class=\"ExternalSdkApp.ExternalPage\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("x:Class=\"ExternalSdkApp.ExternalSecondPage\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Source=\"ExternalPage.xaml\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Navigating=\"OnExternalFrameNavigating\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Navigated=\"OnExternalFrameNavigated\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("LoadCompleted=\"OnExternalFrameLoadCompleted\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DrainDispatcher()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ApplicationIdle", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(250)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("markerOperation.Abort()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("frame.Navigate(new Uri(\"ExternalSecondPage.xaml\", UriKind.Relative))", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("frame.GoBack()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("frame.GoForward()", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("frame.CanGoForward", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK initial frame navigation mode", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK second frame content type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK back frame navigation mode", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK forward frame navigation mode", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK app merged resource dictionary source", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK app compiled page source", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("NuGet.config", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSdkPackageLayout(packageFeed)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLocalProGpuPackagesMatchAvailableRepositoryBuilds(repoRoot, packageFeed)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLocalWpfPackageMatchesAvailableRepositoryBuilds(repoRoot, packageFeed)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetRepositoryProGpuAssemblyPath(repoRoot, assemblyName)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetRepositoryWpfAssemblyPath(repoRoot, assemblyName)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("local {packageId} package matches {expectedAssemblyDescription}", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("repository WPF transport {assemblyName}.dll", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePackageAssemblyIdentities(packageFeed)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateExternalOutput(outputRoot, packageFeed)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateOutputAssemblyMatchesLocalPackage(outputRoot, packageFeed, \"ProGPU.Wpf\", \"ProGPU.Wpf\", \"net10.0\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateOutputAssemblyMatchesLocalPackage(outputRoot, packageFeed, \"ProGPU.Scene\", \"ProGPU.Scene\", \"net10.0\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateOutputAssemblyMatchesLocalPackage(\n                outputRoot,\n                packageFeed,\n                \"Microsoft.DotNet.Wpf.GitHub\",\n                assemblyName,\n                \"net11.0\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK output {assemblySimpleName}.dll matches local {packageId} package", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ComputeStreamSha256(packageStream)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Wpf.Sdk.{SdkVersion}.nupkg", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new(\"Microsoft.DotNet.Wpf.GitHub\", \"PresentationCore\", \"net11.0\", \"WPF\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new(\"ProGPU.Wpf\", \"ProGPU.Wpf\", \"net10.0\", \"ProGPU\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new(\"ProGPU.Scene\", \"ProGPU.Scene\", \"net10.0\", \"ProGPU\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssemblyName.GetAssemblyName(tempPath)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Expected {description} assembly to have a public key token.", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertEqual(expectedAssemblyVersion", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<packageType name=\\\"MSBuildSdk\\\" />", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ReadPackageEntry(package, \"Sdk/Sdk.props\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ReadPackageEntry(package, \"targets/ProGPU.Wpf.Sdk.PortableBootstrap.cs\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertNoPackageEntryPrefix(package, \"lib/\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertNoPackageEntryPrefix(package, \"ref/\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("_ProGpuWpfSdkCopyNativeRuntimeAssets", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"StbImageSharp\"", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK StbImageSharp package reference", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK StbImageSharp package dependency", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("External SDK smoke must not rely on generated Directory.Build.props or Directory.Build.targets files.", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("globalPackagesFolder", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(workRoot, \".packages\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuWpfManagedReferenceRoot", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Microsoft.DotNet.Wpf.GitHub", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Compute", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Transpiler", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetNativeAssetCandidates(\"wgpu\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetNativeAssetCandidates(\"glfw\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateProGpuHiDpiRenderSurface(outputRoot)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU WPF host logical width property", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU WPF host window border property", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU WPF host window border method parameter count", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window.ResizeMode = ResizeMode.CanResizeWithGrip", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window.WindowStyle = WindowStyle.None", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK application main window updated resize mode", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK application main window updated window style", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK portable presentation source client-size return type", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU WPF composition render logical/physical surface", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU WPF host present logical/physical render overload", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU WPF host portable source logical-size synchronization", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU WPF host logical-size cache DPI reconciliation", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Backend.DisplayScaleResolver", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertDisplayScaleResolver(displayScaleResolverType, \"external SDK\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU backend native display-scale fallback", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU WPF host delegates display-scale fallback to ProGPU backend", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU WPF composition target forwards logical/physical render surface", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU compositor render logical/physical surface", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU compositor canvas pixel width explicit render target", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU compositor render pass viewport application", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("external SDK ProGPU compositor physical render target viewport", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RenderPassEncoderSetViewport", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertRetainedWpfLayerUsesLogicalBoundsAndDpiScale(proGpuWpf, proGpuScene, \"external SDK\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertPackagedHighDpiRetainedWpfPixelsFillPhysicalTarget(", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("packaged retained WPF HiDPI upper-left pixel", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("packaged retained WPF HiDPI lower-right pixel", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PushCurrentDirectory(nativeAssetRoot)", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetRequiredType(proGpuScene, \"ProGPU.Scene.DrawingVisual\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetRequiredType(proGpuVector, \"ProGPU.Vector.SolidColorBrush\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetProperty(target, \"RetainedWpfVisualRoot\")", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU retained WPF layer logical size", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU retained WPF layer scale", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertPropertyGetterReferencesField", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertMethodCallsMethod", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertMethodCallsSpecificMethod", externalSdkHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("[\"root\", \"logicalWidth\", \"logicalHeight\", \"renderTargetWidth\", \"renderTargetHeight\", \"dpiScale\", \"targetView\"]", externalSdkHarnessProgram, StringComparison.Ordinal);

        Assert.Contains("ValidateProGpuHiDpiRenderSurface(inputs)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU WPF host logical width property", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU WPF host window border property", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU WPF host window border method parameter count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("new Action<object, object, object>(recorder.SetWindowBorder)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("activated SDK window live resize mode", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("activated SDK window live window style", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK portable activation recorder window border target", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK portable presentation source client-size return type", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU WPF composition render logical/physical surface", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU WPF host present logical/physical render overload", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU WPF host portable source logical-size synchronization", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU WPF host logical-size cache DPI reconciliation", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Backend.DisplayScaleResolver", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertDisplayScaleResolver(displayScaleResolverType, \"SDK\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU backend native display-scale fallback", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU WPF host delegates display-scale fallback to ProGPU backend", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU WPF composition target forwards logical/physical render surface", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU compositor render logical/physical surface", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU compositor canvas pixel width explicit render target", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU compositor render pass viewport application", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK ProGPU compositor physical render target viewport", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RenderPassEncoderSetViewport", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertRetainedWpfLayerUsesLogicalBoundsAndDpiScale(proGpuWpf, proGpuScene, \"SDK\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertPackagedHighDpiRetainedWpfPixelsFillPhysicalTarget(", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("packaged retained WPF HiDPI upper-left pixel", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("packaged retained WPF HiDPI lower-right pixel", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PushCurrentDirectory(nativeAssetRoot)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetRequiredType(proGpuScene, \"ProGPU.Scene.DrawingVisual\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetRequiredType(proGpuVector, \"ProGPU.Vector.SolidColorBrush\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetProperty(target, \"RetainedWpfVisualRoot\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU retained WPF layer logical size", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU retained WPF layer scale", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertPropertyGetterReferencesField", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertMethodCallsMethod", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertMethodCallsSpecificMethod", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("[\"logicalWidth\", \"logicalHeight\", \"pixelWidth\", \"pixelHeight\", \"dpiScale\", \"targetView\"]", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PortableClipboardServiceTypeName = \"System.Windows.PortableClipboardService\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PortableFileDialogServiceTypeName = \"Microsoft.Win32.PortableFileDialogService\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PortableMessageBoxServiceTypeName = \"System.Windows.PortableMessageBoxService\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableSystemParameters(presentationFramework, app)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableWindowChrome(presentationFramework, window)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Shell.WindowChrome", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable SDK WindowChrome attached value", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.SystemParameters", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable SDK SystemParameters.{propertyName}", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable SDK SystemParameters.{propertyName} resource", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PrimaryScreenWidth", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertPortableSystemParameterRect(systemParametersType, resourceOwner, \"WorkArea\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"MenuShowDelay\", 400", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"ClientAreaAnimation\", false", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"ForegroundFlashCount\", 7", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("WheelScrollLines", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ResolveSystemParameterResource(systemParametersType, resourceOwner, propertyName)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableClipboard(presentationCore)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableFileDialogs(presentationFramework)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RegisterPortableMessageBox(presentationFramework)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMessageBox(presentationFramework, window)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable Clipboard SDK data object unicode text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ownerPrefix = owner is null ? \"no-owner\" : \"owner\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable SDK {ownerPrefix} SaveFileDialog FileName", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable SDK {ownerPrefix} OpenFolderDialog FolderName", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable MessageBox SDK no-owner default result", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("portable MessageBox SDK owner fallback result", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationFramework, PortableMessageBoxServiceTypeName)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationFramework, PortableFileDialogServiceTypeName)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ClearPortableService(presentationCore, PortableClipboardServiceTypeName)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertEqual(\"MainWindow.xaml\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", \"SmokeAccentBrush\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", \"MergedAccentBrush\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", \"UnsharedAccentBrush\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("application x:Shared=false resource instance", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", \"SmokePanelMargin\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", \"ProviderGreeting\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("application object data provider result", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateFreezableBrushResource(app)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateFreezableGradientBrushResource(app)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK Freezable brush clone mutable opacity", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK Freezable gradient clone mutable stop offset", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK Freezable gradient current-value clone stop collection", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSdkLooseXamlReaderWriter(presentationFramework, presentationCore)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Markup.XamlReader", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Markup.XamlWriter", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK loose XamlReader style StaticResource brush", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK loose XamlReader Binding path", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SdkLooseInputScopeTextBox", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<InputScopeName>EmailUserName</InputScopeName>", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("<InputScopePhrase>sdk loose phrase</InputScopePhrase>", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"sdk-loose-input-scope\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"SDK loose XamlReader\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InputScopeName value", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InputScopePhrase text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK loose XamlWriter serialized GradientStop", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateSdkLooseGradientStop(GetCollectionItem(roundTrippedStops, 1)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateApplicationRunLifetime(app)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateApplicationInitialLifetimeState(app)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateApplicationShutdownLifetimeState(app)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK Application.Current before run", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK Application.ShutdownMode before run", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK Application.Windows before run", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK Application.MainWindow before run", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK Application.Current during run", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK Application.Windows startup window", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK Application.Current after shutdown", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK Application.Windows after shutdown", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("application startup event initial count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("application Startup event count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("application Startup args length", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("application Exit event count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("application Exit code", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("application Startup injected brush", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("application Startup injected text resource", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertEqual(\"#FF356D9E\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("loaded storyboard TextBlock", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("loaded storyboard EventTrigger", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("loaded storyboard DoubleAnimation target value", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("loaded storyboard post-Loaded opacity", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK loaded storyboard handler count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateApplicationDynamicResourceInvalidation", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("set_Item\", \"SmokeAccentBrush\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("set_Item\", \"MergedAccentBrush\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("message dynamic resource updated color", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("action button dynamic resource updated color", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertAssignableTo(window, \"System.Windows.Window\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"Message\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"RootPanel\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"StartupResourceText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("startup resource foreground color", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ActionButton\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"CommandButton\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("Window.CommandBindings", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetProperty(window, \"InputBindings\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window input binding count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window key binding command parameter", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window mouse binding command parameter", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window mouse binding gesture action", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window mouse binding command executed parameter", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("command button routed command CanExecute", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window routed command execution count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"EventSetterButton\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("event setter button style target type", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.EventSetter", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("event setter button routed event", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("event setter click count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("event setter routed event name", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("action button tooltip", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("action tooltip content", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("action tooltip placement", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("action button context menu", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("action context menu item count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("context command menu item", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("context menu command execution count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("context menu command payload observed", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("action context menu separator", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("context checkable menu item checked", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("context checkable menu item unchecked", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeMenu\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke root menu item count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke menu separator", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"CommandMenuItem\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("command menu routed command CanExecute", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window routed command menu parameter", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"MenuStatus\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ClickMenuItem\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RaiseRoutedEvent(clickMenuItem, \"ClickEvent\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window menu click count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"CheckableMenuItem\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SetProperty(checkableMenuItem, \"IsChecked\", false)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("checkable menu item toggled unchecked", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window menu unchecked count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SetProperty(checkableMenuItem, \"IsChecked\", true)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("checkable menu item toggled checked", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window menu checked count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"CheckChoicePanel\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ManagedCheckBox\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("managed check box unchecked by click", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("managed check box checked by click", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ManagedRadioAlpha\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ManagedRadioBeta\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("managed radio alpha unchecked after beta click", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("managed radio beta checked by click", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("managed radio beta last checked name", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("managed radio alpha rechecked by click", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("managed radio beta unchecked after alpha click", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("managed radio alpha last checked name", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("private static void RaiseRoutedEvent", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("action button control template", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("action button visual state group count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("property trigger text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("data trigger text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("multi trigger text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("multi data trigger text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"BasedOnResourceText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("based-on resource inherited font weight", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ProviderGreetingText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("provider greeting text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindResource\", \"XmlSmokeData\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("window XML data provider", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"XmlProviderText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("XML provider XPath binding text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"UnsharedBrushBorder\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("unshared border brush color", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"InputBox\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"[0-9a-z]+\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"sdk-input-scope\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"SDK compiled BAML\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"AccessKeyFocusPanel\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("access key focus scope flag", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("access key focus initial focused element", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Input.KeyboardNavigation", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("access key tab navigation mode", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("access key control tab navigation mode", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("access key directional navigation mode", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"InputAccessLabel\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("input access label target", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"StandaloneAccessText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("standalone access text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"AncestorBindingBorder\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ancestor binding border tag", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"AncestorBindingText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ancestor binding relative-source mode", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ancestor binding resolved text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetAssemblyFromContext", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"MutableStatusText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("mutable status initial binding text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("MutableStatus\", \"updated binding status\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("mutable status property changed binding text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ValidatedInputBox\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validated input box initial text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Controls.Validation", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validated input empty validation state", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validation status empty text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validated input rejected source update", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validated input corrected validation state", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("validated input corrected source update", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"CredentialBox\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("credential password box secure password length", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("credential password box changed sender", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("credential password box routed event", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("credential password box cleared secure password length", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"CalendarSmoke\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("calendar smoke selected date collection item", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("calendar smoke updated selected date", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("calendar smoke selection changed sender", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"DatePickerSmoke\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("date picker smoke selected date format", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("date picker smoke updated selected date", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("date picker smoke selection changed sender", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("private static void AssertDate", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"RoutedEventSource\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RaiseSmokeBubbled", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("custom routed event count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("custom routed event bubbled sender", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("custom routed event original source", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("custom routed event status text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ItemsList\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeStatusBar\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke status bar item count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"StatusReadyItem\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("status ready item content", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"StatusTextBlock\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("status selected item text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ItemsCountText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("initial items count binding text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"PanelItemsControl\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("panel items alternation count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("panel items string format", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("panel items container style setter count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("panel items panel template", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("panel items panel orientation", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeListView\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke list view grid view", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke list view name binding path", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke list view value binding path", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ListViewStatus\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("list view initial selected text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke list view changed selected item", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("list view changed selected text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"MultiBindingSummaryText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("multi binding converter text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"PriorityBindingText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("priority binding fallback text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetPriorityBindingExpression", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("priority binding expression child count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("priority binding active path", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SelectedItemPresenter\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.DataTemplateKey", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("implicit item data template", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("implicit item template binding path", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ImplicitItemPresenter\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("implicit item template resolved text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ImplicitStylePanel\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ImplicitStyledText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("implicit text style target type", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("implicit styled text foreground color", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"LayoutGrid\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("layout grid row definition count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"LayoutLabel\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("layout label grid column", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ConvertedSelectedItemText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("converted selected item text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"FormattedInputText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("formatted input binding text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetColumnSpan", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"DockLayoutPanel\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("dock layout last child fill", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("dock layout top dock", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("dock layout left dock", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("dock fill binding text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"CanvasLayoutPanel\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("canvas positioned left", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("canvas positioned top", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"UniformLayoutPanel\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("uniform layout rows", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("uniform layout columns", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("uniform cell three text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindResource\", \"GroupedItems\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("grouped items group description count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("grouped items view group count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"GroupedItemsControl\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("grouped items group header template", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SelectorItemsControl\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke item template selector", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindResource\", \"SmokeFrameworkItemTemplate\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindResource\", \"SmokeRenderingItemTemplate\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("framework item selected template", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("rendering item selected template", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeComboBox\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke combo box initial selected value", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke combo box changed selected value", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("selector selection changed count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("selector status after combo selection", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeTabs\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke tab initial selected index", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"FrameworkTab\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"RenderingTab\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke tab changed selected item", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("tab selection changed count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("tab status after tab selection", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeToolBarTray\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke toolbar tray toolbar count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeToolBar\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke toolbar header", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ToolBarCommandButton\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("toolbar command execution count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("toolbar command payload observed", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ToolBarSeparator\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ToolBarToggle\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("toolbar toggle checked", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("toolbar toggle unchecked", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeGroupBox\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke group box header", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeExpander\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke expander collapsed count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke expander expanded count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeScrollViewer\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke scroll viewer vertical visibility", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ScrollContentPanel\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("scroll content child count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeSlider\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke slider changed value", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeProgressBar\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke progress changed bound value", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("range value changed count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("range status after slider value", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeDataGrid\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke data grid column count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke data grid name binding path", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke data grid category binding path", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke data grid active binding path", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"DataGridStatus\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("data grid initial selected text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("smoke data grid changed selected item", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("data grid changed selected text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("data grid items count after collection change", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("items list count after collection change", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("panel items count after collection change", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("list view count after collection change", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("selector items count after collection change", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("items count binding text after collection change", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("dynamic framework item selected template", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"HierarchyTree\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("hierarchy tree root item count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.HierarchicalDataTemplate", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("hierarchy item source binding", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("hierarchy first item child count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("hierarchy first child name", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"CompiledSmokePanel\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled user control dependency property", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled user control bound dependency property", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"PanelCaption\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled user control element-name binding", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"PanelRelativeCaption\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled user control relative-source binding", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"PanelContentPresenter\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled user control content binding", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"CompiledLibraryPanel\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Wpf.SdkSwitchLibrary.LibraryPanel", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled SDK library user control dependency property", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled SDK library element-name title binding", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled SDK library element-name tag binding", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled SDK library BAML text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled SDK library resource brush color", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ThemedSmokeControl\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed custom control default template", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ThemeText\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed custom control template binding", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"ThemeRoot\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed custom control background color", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed custom control component resource color", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("themed custom control border thickness left", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SmokeFrame\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled page frame source", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Wpf.SdkSwitchSmoke.SmokePage", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled frame navigating count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled frame navigated count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled frame load completed count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled frame navigation mode", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled frame navigated content type", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SmokeSecondPage.xaml", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled frame second page navigate result", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled second frame page", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SecondPageTitle\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled second page title text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"SecondPageSubtitle\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled second page subtitle text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled frame second page content type", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled frame journal can go back", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InvokeVoid(smokeFrame, \"GoBack\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled frame back navigation mode", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled frame back content type", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"PageTitle\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled page dynamic resource foreground", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"PageSubtitle\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled page subtitle text", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FindName\", \"DocumentBox\"", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("rich text hyperlink URI", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InvokeVoid(documentHyperlink, \"DoClick\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK rich text hyperlink RequestNavigate count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK rich text hyperlink RequestNavigate URI", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("SDK rich text hyperlink RequestNavigate routed event", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("rich text inline UI button content", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("rich text list marker style", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("rich text table column count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("rich text table cell count", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("rich text block UI text content", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("private static object FindFirstByType", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("InvokeVoid(actionButton, \"OnClick\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("RunApplicationRunSmoke", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationService", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PortablePresentationSource", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("PortableMediaContextRenderService", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableMessageBox(_presentationFramework, typedActivation.Window)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePortableFileDialogs(_presentationFramework, typedActivation.Window)", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("object exitCode = Invoke(app, \"Run\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertEqual(0, exitCode", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertSame(typedActivation.Window, GetProperty(_application, \"MainWindow\")", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.Contains("FlushDispatcherOperations", runtimeHarnessProgram, StringComparison.Ordinal);
        Assert.DoesNotContain(".Show()", runtimeHarnessProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationUiUsesManagedPrintingReferenceForNonWindowsBringup()
    {
        var presentationUiProjectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationUI",
            "PresentationUI.csproj");
        var presentationUiProject = XDocument.Load(presentationUiProjectPath);

        var nativePrintingReference = AssertProjectReference(
            presentationUiProject,
            @"System.Printing\System.Printing.vcxproj");
        Assert.Equal("'$(OS)' == 'Windows_NT'", nativePrintingReference.Attribute("Condition")?.Value);
        Assert.Equal("TargetFramework;TargetFrameworks", nativePrintingReference.Element("UndefineProperties")?.Value);

        var managedPrintingReference = AssertProjectReference(
            presentationUiProject,
            @"System.Printing\ref\System.Printing-ref.csproj");
        Assert.Equal("'$(OS)' != 'Windows_NT'", managedPrintingReference.Attribute("Condition")?.Value);
    }

    [Fact]
    public void CompositionTargetSupportsPortableNonDuceRootOwnership()
    {
        var compositionTargetPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "CompositionTarget.cs");
        var portableTargetPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "PortableCompositionTarget.cs");
        var portableSourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "PortablePresentationSource.cs");
        var projectPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "PresentationCore.csproj");

        var compositionTarget = File.ReadAllText(compositionTargetPath);
        var portableTarget = File.ReadAllText(portableTargetPath);
        var portableSource = File.ReadAllText(portableSourcePath);
        var project = File.ReadAllText(projectPath);

        Assert.Contains("internal virtual bool UsesDuceComposition", compositionTarget, StringComparison.Ordinal);
        Assert.Contains("internal virtual void OnRootVisualChanged", compositionTarget, StringComparison.Ordinal);
        Assert.Contains("if (UsesDuceComposition)", compositionTarget, StringComparison.Ordinal);
        Assert.Contains("DUCE.Channel channel = mediaContext.Channel", compositionTarget, StringComparison.Ordinal);
        Assert.Contains("channel != null", compositionTarget, StringComparison.Ordinal);
        Assert.Contains("OnRootVisualChanged(oldRootVisual, _rootVisual)", compositionTarget, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaContext.From(Dispatcher).GetChannels()", compositionTarget, StringComparison.Ordinal);
        var setRootVisualIndex = compositionTarget.IndexOf("private void SetRootVisual", StringComparison.Ordinal);
        Assert.True(
            compositionTarget.IndexOf("if (UsesDuceComposition)", setRootVisualIndex, StringComparison.Ordinal)
                < compositionTarget.IndexOf("_contentRoot.IsOnChannel(channel)", setRootVisualIndex, StringComparison.Ordinal),
            "DUCE content-root checks must stay behind the DUCE-composition branch.");

        Assert.Contains("internal sealed class PortableCompositionTarget : CompositionTarget", portableTarget, StringComparison.Ordinal);
        Assert.Contains("internal override bool UsesDuceComposition", portableTarget, StringComparison.Ordinal);
        Assert.Contains("get { return false; }", portableTarget, StringComparison.Ordinal);
        Assert.Contains("internal override void CreateUCEResources", portableTarget, StringComparison.Ordinal);
        Assert.Contains("internal override void ReleaseUCEResources", portableTarget, StringComparison.Ordinal);
        Assert.Contains("public override Matrix TransformToDevice", portableTarget, StringComparison.Ordinal);
        Assert.Contains("private void SetDeviceScaleCore", portableTarget, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\Media\PortableCompositionTarget.cs"" />", project, StringComparison.Ordinal);

        Assert.Contains("internal sealed class PortablePresentationSource : PresentationSource, IDisposable", portableSource, StringComparison.Ordinal);
        Assert.Contains("private readonly PortableCompositionTarget _compositionTarget", portableSource, StringComparison.Ordinal);
        Assert.Contains("private readonly PortableKeyboardInputProvider _keyboardInputProvider", portableSource, StringComparison.Ordinal);
        Assert.Contains("private readonly PortableMouseInputProvider _mouseInputProvider", portableSource, StringComparison.Ordinal);
        Assert.Contains("AddSource();", portableSource, StringComparison.Ordinal);
        Assert.Contains("RemoveSource();", portableSource, StringComparison.Ordinal);
        Assert.Contains("RootChanged(oldRootVisual, _rootVisual)", portableSource, StringComparison.Ordinal);
        Assert.Contains("_keyboardInputProvider.OnRootChanged(oldRootVisual, _rootVisual)", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal event EventHandler RenderRequested", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal void SetDeviceScale(double dpiScaleX, double dpiScaleY)", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal void SetClientSize(double width, double height)", portableSource, StringComparison.Ordinal);
        Assert.Contains("private void ApplyRootVisualLayout()", portableSource, StringComparison.Ordinal);
        Assert.Contains("rootUIElement.Measure(_clientSize);", portableSource, StringComparison.Ordinal);
        Assert.Contains("rootUIElement.Arrange(new Rect(new Point(), _clientSize));", portableSource, StringComparison.Ordinal);
        Assert.Contains("protected override CompositionTarget GetCompositionTargetCore()", portableSource, StringComparison.Ordinal);
        Assert.Contains("return _isDisposed ? null : _compositionTarget;", portableSource, StringComparison.Ordinal);
        Assert.Contains("internal override IInputProvider GetInputProvider(Type inputDevice)", portableSource, StringComparison.Ordinal);
        Assert.Contains("inputDevice == typeof(MouseDevice)", portableSource, StringComparison.Ordinal);
        Assert.Contains("inputDevice == typeof(KeyboardDevice)", portableSource, StringComparison.Ordinal);
        Assert.Contains("private sealed class PortableKeyboardInputProvider : IKeyboardInputProvider, IDisposable", portableSource, StringComparison.Ordinal);
        Assert.Contains("private sealed class PortableMouseInputProvider : IMouseInputProvider, IDisposable", portableSource, StringComparison.Ordinal);
        Assert.Contains("InputManager.Current.RegisterInputProvider(this)", portableSource, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Focus(null)", portableSource, StringComparison.Ordinal);
        Assert.Contains("void IInputProvider.NotifyDeactivate()\n            {\n                ReleaseMouseCapture(reportInput: true);\n            }", portableSource, StringComparison.Ordinal);
        Assert.Contains("RawMouseActions.Activate | RawMouseActions.CancelCapture", portableSource, StringComparison.Ordinal);
        Assert.Contains("_site.ReportInput(report)", portableSource, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\PortablePresentationSource.cs"" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public void SilkNetWindowDecorationServiceUsesNativeDragMoveBoundary()
    {
        var sourcePath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Platform",
            "SilkNetWpfWindowDecorationService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("window is not IView view", source, StringComparison.Ordinal);
        Assert.Contains("view.Handle == IntPtr.Zero", source, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsWindows()", source, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsMacOS()", source, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsLinux()", source, StringComparison.Ordinal);
        Assert.Contains("view is not INativeWindowSource nativeWindowSource", source, StringComparison.Ordinal);
        Assert.Contains("return nativeWindowSource.Native", source, StringComparison.Ordinal);
        Assert.Contains("GetNativeWindow(view)", source, StringComparison.Ordinal);
        Assert.Contains("var win32 = nativeWindow.Win32", source, StringComparison.Ordinal);
        Assert.Contains("return win32.HasValue ? win32.Value.Item2 : IntPtr.Zero", source, StringComparison.Ordinal);
        Assert.Contains("var cocoa = nativeWindow.Cocoa", source, StringComparison.Ordinal);
        Assert.Contains("return cocoa.GetValueOrDefault()", source, StringComparison.Ordinal);
        Assert.Contains("var x11 = nativeWindow.X11", source, StringComparison.Ordinal);
        Assert.Contains("new X11WindowHandle(x11.Value.Item1, x11.Value.Item2)", source, StringComparison.Ordinal);
        Assert.Contains("TryBeginWin32DragMove(GetWin32Hwnd(view))", source, StringComparison.Ordinal);
        Assert.Contains("TryBeginCocoaDragMove(GetCocoaWindow(view))", source, StringComparison.Ordinal);
        Assert.Contains("return TryBeginX11DragMove(x11.Display, x11.Window)", source, StringComparison.Ordinal);
        Assert.Contains("hwnd == IntPtr.Zero", source, StringComparison.Ordinal);
        Assert.Contains("ReleaseCapture();", source, StringComparison.Ordinal);
        Assert.Contains("SendMessage(hwnd, WM_SYSCOMMAND, (IntPtr)SC_MOUSEMOVE, IntPtr.Zero)", source, StringComparison.Ordinal);
        Assert.Contains("SendMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero)", source, StringComparison.Ordinal);
        Assert.Contains("private const string ObjCLibrary = \"/usr/lib/libobjc.A.dylib\"", source, StringComparison.Ordinal);
        Assert.Contains("[SupportedOSPlatform(\"macos\")]", source, StringComparison.Ordinal);
        Assert.Contains("ObjCGetClass(\"NSApplication\")", source, StringComparison.Ordinal);
        Assert.Contains("SelRegisterName(\"sharedApplication\")", source, StringComparison.Ordinal);
        Assert.Contains("SelRegisterName(\"currentEvent\")", source, StringComparison.Ordinal);
        Assert.Contains("SelRegisterName(\"performWindowDragWithEvent:\")", source, StringComparison.Ordinal);
        Assert.Contains("currentEvent == IntPtr.Zero", source, StringComparison.Ordinal);
        Assert.Contains("ObjCMsgSend(nsWindow, performDragSelector, currentEvent)", source, StringComparison.Ordinal);
        Assert.Contains("EntryPoint = \"objc_msgSend\"", source, StringComparison.Ordinal);
        Assert.Contains("private const string X11Library = \"libX11.so.6\"", source, StringComparison.Ordinal);
        Assert.Contains("private const int NetWmMoveresizeMove = 8", source, StringComparison.Ordinal);
        Assert.Contains("[SupportedOSPlatform(\"linux\")]", source, StringComparison.Ordinal);
        Assert.Contains("XDefaultRootWindow(display)", source, StringComparison.Ordinal);
        Assert.Contains("XQueryPointer(", source, StringComparison.Ordinal);
        Assert.Contains("XInternAtom(display, \"_NET_WM_MOVERESIZE\", onlyIfExists: false)", source, StringComparison.Ordinal);
        Assert.Contains("XUngrabPointer(display, UIntPtr.Zero)", source, StringComparison.Ordinal);
        Assert.Contains("XSendEvent(", source, StringComparison.Ordinal);
        Assert.Contains("SubstructureRedirectMask | SubstructureNotifyMask", source, StringComparison.Ordinal);
        Assert.Contains("XFlush(display)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public bool TryBeginDragMove(object window)\n    {\n        return false;\n    }",
            source,
            StringComparison.Ordinal);
    }

    private static XElement AssertProjectReference(XDocument project, string includeSuffix)
    {
        return Assert.Single(
            project.Descendants("ProjectReference"),
            item => IncludeEndsWith(item, "Include", includeSuffix));
    }

    private static XElement AssertPackageReference(XDocument project, string include)
    {
        return Assert.Single(
            project.Descendants("PackageReference"),
            item => string.Equals(item.Attribute("Include")?.Value, include, StringComparison.Ordinal));
    }

    private static void AssertGuardBefore(string source, string guard, string guardedCall)
    {
        var guardIndex = source.IndexOf(guard, StringComparison.Ordinal);
        var guardedCallIndex = source.IndexOf(guardedCall, StringComparison.Ordinal);

        Assert.True(guardIndex >= 0, $"Expected guard '{guard}' to exist.");
        Assert.True(guardedCallIndex >= 0, $"Expected guarded call '{guardedCall}' to exist.");
        Assert.True(
            guardIndex < guardedCallIndex,
            $"Expected guard '{guard}' to appear before '{guardedCall}'.");
    }

    private static void AssertCompileInclude(XDocument project, string includeSuffix, bool link = false)
    {
        Assert.Contains(
            project.Descendants("Compile"),
            item => IncludeEndsWith(item, link ? "Link" : "Include", includeSuffix));
    }

    private static bool IncludeEndsWith(XElement element, string attributeName, string includeSuffix)
    {
        var include = attributeName == "Link"
            ? element.Element("Link")?.Value.Replace('/', '\\')
            : element.Attribute(attributeName)?.Value.Replace('/', '\\');
        return include?.EndsWith(includeSuffix, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? GetItemMetadata(XElement element, string metadataName)
    {
        return element.Attribute(metadataName)?.Value ?? element.Element(metadataName)?.Value;
    }

    private static void AssertSourceFileExists(params string[] pathSegments)
    {
        Assert.True(File.Exists(FindRepoPath(pathSegments)), $"Expected source file '{Path.Combine(pathSegments)}' to exist.");
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }
}
