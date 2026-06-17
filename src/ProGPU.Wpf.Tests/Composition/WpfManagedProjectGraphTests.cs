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
        var proGpuHostPath = FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfWindowHost.cs");

        var mediaContext = File.ReadAllText(mediaContextPath);
        var renderService = File.ReadAllText(renderServicePath);
        var presentationCoreProject = File.ReadAllText(presentationCoreProjectPath);
        var activationService = File.ReadAllText(activationServicePath);
        var proGpuActivation = File.ReadAllText(proGpuActivationPath);
        var proGpuScheduler = File.ReadAllText(proGpuSchedulerPath);
        var proGpuHost = File.ReadAllText(proGpuHostPath);

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

        Assert.Contains("internal static void FlushDispatcherOperations(object window, DispatcherPriority markerPriority)", activationService, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.PushFrame(frame)", activationService, StringComparison.Ordinal);
        Assert.Contains("public interface IWpfDelayedRenderScheduler : IWpfRenderScheduler", proGpuScheduler, StringComparison.Ordinal);
        Assert.Contains("void RequestRender(TimeSpan delay)", proGpuScheduler, StringComparison.Ordinal);
        Assert.Contains("PortableMediaContextRenderServiceTypeName", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryRegisterMediaContextRenderService(presentationCoreAssembly)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("typeof(Action<TimeSpan>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("IWpfDelayedRenderScheduler delayedScheduler", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.RenderWakeupRequested += OnHostRenderWakeupRequested", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryFlushDispatcherOperations(Window, \"Render\")", proGpuActivation, StringComparison.Ordinal);
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
        Assert.Contains("ClosePortableWindowActivation();", window, StringComparison.Ordinal);
        Assert.Contains("private bool IsPortableWindowActive", window, StringComparison.Ordinal);
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
        Assert.True(
            application.IndexOf("PortableWindowActivationService.TryRun(MainWindow)", StringComparison.Ordinal)
                < application.IndexOf("FlushPortableDispatcherOperations(DispatcherPriority.ApplicationIdle)", StringComparison.Ordinal),
            "Application.Run must service queued shutdown work after the portable native run loop exits.");
        Assert.True(
            application.IndexOf("PortableWindowActivationService.TryRun(MainWindow)", StringComparison.Ordinal)
                < application.IndexOf("RunDispatcher(null);", StringComparison.Ordinal),
            "Application.Run must use the portable native run loop before falling back to WPF Dispatcher.Run.");
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
        Assert.Contains("System.Windows.Media.DrawingVisual", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.GeometryDrawing", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.FormattedText", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Media.GlyphRun", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawLine\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawRoundedRectangle\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawEllipse\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawGeometry\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawDrawing\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawGlyphRun\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("\"DrawText\"", harnessProgram, StringComparison.Ordinal);
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
        Assert.Contains("ProGpuRenderCommandType.PushOpacity", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.PushGeometryClip", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGpuRenderCommandType.PushOpacityMask", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained opacity mask", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained rounded rectangle radius X", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained guideline snapped rect X", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained drawing resource path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained glyph run", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("real DrawingVisual retained formatted text", harnessProgram, StringComparison.Ordinal);
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

        var harnessProject = XDocument.Load(harnessProjectPath);
        var appXaml = File.ReadAllText(appXamlPath);
        var smokeResourcesXaml = File.ReadAllText(smokeResourcesXamlPath);
        var mainWindowXaml = File.ReadAllText(mainWindowXamlPath);
        var smokeUserControlXaml = File.ReadAllText(smokeUserControlXamlPath);
        var appCodeBehind = File.ReadAllText(appCodeBehindPath);
        var mainWindowCodeBehind = File.ReadAllText(mainWindowCodeBehindPath);
        var smokeUserControlCodeBehind = File.ReadAllText(smokeUserControlCodeBehindPath);

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

        var page = Assert.Single(
            harnessProject.Descendants("Page"),
            item => item.Attribute("Include")?.Value == "MainWindow.xaml");
        Assert.Equal("MainWindow.xaml", page.Attribute("Include")?.Value);
        Assert.Equal("MSBuild:Compile", page.Element("Generator")?.Value);

        AssertCompileInclude(harnessProject, "App.xaml.cs");
        AssertCompileInclude(harnessProject, "MainWindow.xaml.cs");
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
        Assert.Contains("SolidColorBrush x:Key=\"ReplacementAccentBrush\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ControlTemplate x:Key=\"SmokeButtonTemplate\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource AccentBrush}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{TemplateBinding Content}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"SmokeTextBoxStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"BasedOnTextBoxStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource SmokeTextBoxStyle}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"based on text box style\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style x:Key=\"TriggeredButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style.Triggers", appXaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding IsWarning}\" Value=\"True\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"trigger active\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("public partial class App : Application", appCodeBehind, StringComparison.Ordinal);

        Assert.Contains("ResourceDictionary", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("SolidColorBrush x:Key=\"MergedAccentBrush\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("Thickness x:Key=\"MergedBlockMargin\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("Style TargetType=\"{x:Type CheckBox}\"", smokeResourcesXaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Tag\" Value=\"implicit merged style\"", smokeResourcesXaml, StringComparison.Ordinal);

        Assert.Contains("x:Class=\"ProGPU.Wpf.RealXamlCompilerHarness.MainWindow\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("TextBox", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RichTextBox", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BasedOnTextBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource BasedOnTextBoxStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"compiled BasedOn TextBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FlowDocument", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompiledDocument\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"IntroParagraph\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Bold>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentLink\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("NavigateUri=\"https://example.test/progpu-wpf\"", mainWindowXaml, StringComparison.Ordinal);
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
        Assert.Contains("x:Name=\"AncestorBindingBorder\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"ancestor binding source\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RelativeSourceBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("RelativeSource={RelativeSource AncestorType={x:Type Border}}, Path=Tag", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ValidatedBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ValidatedText, UpdateSourceTrigger=Explicit, ValidatesOnDataErrors=True, NotifyOnValidationError=True", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProviderGreetingBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Source={StaticResource ProviderGreeting}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MarkupExtensionBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{local:SmokeText Prefix=compiled, Value=markup}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MergedResourceBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource MergedAccentBrush}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"{StaticResource MergedBlockMargin}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("local:SmokeUserControl", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NestedControl\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AttachedLayoutGrid\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.RowDefinitions", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.ColumnDefinitions", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GridFirstCell\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ImplicitStyleCheckBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EventButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnXamlClick\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SmokeCommand}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:componentModel=\"clr-namespace:System.ComponentModel;assembly=WindowsBase\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:local=\"clr-namespace:ProGPU.Wpf.RealXamlCompilerHarness\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SortedItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.SortDescriptions", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("componentModel:SortDescription", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Direction=\"Descending\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PropertyName=\"Name\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ObjectDataProvider", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ProviderGreeting\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsAsynchronous=\"False\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("MethodName=\"CreateProviderGreeting\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ObjectType=\"{x:Type local:ProviderDataFactory}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ObjectDataProvider.MethodParameters", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<sys:String>provider</sys:String>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<sys:String>7</sys:String>", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Window.CommandBindings", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Static local:MainWindow.SmokeRoutedCommand}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CanExecute=\"OnSmokeCommandCanExecute\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Executed=\"OnSmokeCommandExecuted\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RoutedCommandButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CommandTarget=\"{Binding ElementName=InputBox}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TemplatedButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Template=\"{StaticResource SmokeButtonTemplate}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TriggeredButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding TriggerButtonText}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource TriggeredButtonStyle}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Items}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedItem, Mode=TwoWay}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SortedItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource SortedItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ListBox.ItemsPanel", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsPanelTemplate", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataTemplate DataType=\"{x:Type local:SmokeItem}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Name}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StaticResource AccentBrush", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DataContext = new SmokeViewModel();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public static RoutedUICommand SmokeRoutedCommand", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class ProviderDataFactory", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string CreateProviderGreeting", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{prefix} data {value}\";", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeTextExtension : MarkupExtension", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public override object ProvideValue(IServiceProvider serviceProvider)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{Prefix} {Value} extension\";", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeCommandCanExecute", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnSmokeCommandExecuted", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("RoutedCommandExecutionCount++", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int XamlClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnXamlClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastXamlClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeViewModel : INotifyPropertyChanged", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("IDataErrorInfo", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ObservableCollection<SmokeItem> Items", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string TriggerButtonText", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string ValidatedText", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ValidatedText is required", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsWarning", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public SmokeItem? SelectedItem", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged();", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeItem", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeCommand : ICommand", mainWindowCodeBehind, StringComparison.Ordinal);

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
        Assert.Contains("Create(compilerHarness, MainWindowTypeName)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(resources, \"AccentBrush\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetProperty(resources, \"MergedDictionaries\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("TryFindResource\", \"MergedAccentBrush\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(resources, \"SmokeTextBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(resources, \"BasedOnTextBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox BasedOn style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox BasedOn base style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox BasedOn inherited MinWidth", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"InputBox\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateTextBoxSelection(inputBox)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox selected text replacement", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRichFlowDocument(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"DocumentBox\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Bold", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.Hyperlink", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("https://example.test/progpu-wpf", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.List", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Documents.TextRange", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange paragraph text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument list items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"BindingBlock\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"CommandButton\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateBindingAndCommand(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBlock property-change binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button command binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateAdvancedBindingFeatures(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PriorityBinding fallback value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PriorityBinding child bindings", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PriorityBinding fallback path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetPriorityBindingExpression", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiBinding string-format value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RelativeSource ancestor binding value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation error state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation restored error state", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateObjectDataProvider(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider method parameters", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider bound text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider binding source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMarkupExtension(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MarkupExtension TextBlock", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MarkupExtension provided text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMergedResourceDictionary(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled merged-resource foreground", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateNestedUserControl(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Wpf.RealXamlCompilerHarness.SmokeUserControl", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(nestedControl, \"ControlTitle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled UserControl ElementName binding value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled UserControl click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateReadOnlyGridCollectionsAndAttachedProperties(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Grid row definitions", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDependencyPropertyValue(firstCell, layoutGrid.GetType(), \"RowProperty\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateImplicitMergedStyle(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled implicit CheckBox style tag", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateXamlEventHandler(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XAML Click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"RoutedCommandButton\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRoutedCommand(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled routed command target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("InvokeTwoArgumentCommand(routedCommand, \"Execute\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateTemplateAndDynamicResource(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button control template", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate dynamic resource update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateStyleAndDataTrigger(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button triggered style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTrigger active value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetField(window, \"ItemsList\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateItemsBindingAndTemplate(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox ItemsSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate text binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox collection-change items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource sort direction", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource collection-change first item", harnessProgram, StringComparison.Ordinal);
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
        Assert.Contains("Invoke(application, \"Run\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationServiceTypeName = \"System.Windows.PortableWindowActivationService\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("new Func<object, object>(recorder.Activate)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("new Action<object>(recorder.Run)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMainWindow(window, _application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(resources, \"BasedOnTextBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox BasedOn style", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox BasedOn local setter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox BasedOn inherited margin top", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateBindingAndCommand(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBlock property-change binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateTextBoxSelection(inputBox)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled TextBox selected text replacement", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRichFlowDocument(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument TextRange paragraph text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled FlowDocument list items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Button command binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateAdvancedBindingFeatures(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PriorityBinding fallback value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PriorityBinding child bindings", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled PriorityBinding first path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetPriorityBindingExpression", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MultiBinding string-format value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled RelativeSource binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation invalid source value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled validation restored source value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateObjectDataProvider(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider synchronous flag", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider object type", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider object instance", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider second parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ObjectDataProvider binding source", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMarkupExtension(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MarkupExtension TextBlock", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MarkupExtension provided text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMergedResourceDictionary(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled merged-resource margin top", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateNestedUserControl(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled UserControl ElementName binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled UserControl click routed event name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateReadOnlyGridCollectionsAndAttachedProperties(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Grid column definitions", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDependencyPropertyValue(secondCell, layoutGrid.GetType(), \"ColumnProperty\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateImplicitMergedStyle(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled implicit CheckBox style margin top", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateXamlEventHandler(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled XAML Click routed event name", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateRoutedCommand(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled routed command target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateTemplateAndDynamicResource(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate named part", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ControlTemplate dynamic resource update", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateStyleAndDataTrigger(window, application)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTrigger inactive value", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTrigger active brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateItemsBindingAndTemplate(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox two-way selected item binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled DataTemplate text binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource sort property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled sorted ListBox generated items", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource collection-change first item", harnessProgram, StringComparison.Ordinal);
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
        Assert.Contains("GetDictionaryValue(themeDictionary, \"AccentButtonStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"DefaultRichTextBoxStyle\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetDictionaryValue(themeDictionary, \"WindowTemplateKey\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(window, \"FindName\", \"DocumentBox\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled themed RichTextBox", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(window, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(button, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(richTextBox, \"ApplyTemplate\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertType(GetProperty(richTextBox, \"Template\"), \"System.Windows.Controls.ControlTemplate\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("AssertCollectionCount(children, expectedMinimum: 14", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("GetCollectionItem(children, GetCollectionCount(children) - 1)", harnessProgram, StringComparison.Ordinal);
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
        var systemResources = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "SystemResources.cs"));
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
        var textServicesLoader = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "Shared",
            "MS",
            "Internal",
            "TextServicesLoader.cs"));
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
        var lineServices = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "MS",
            "Internal",
            "TextFormatting",
            "LineServices.cs"));
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

        AssertGuardBefore(compositionExports, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.MilCoreApi.EnterCompositionEngineLock()");
        AssertGuardBefore(uiElement, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.GetDC(desktopWnd)");
        AssertGuardBefore(pathGeometry, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.MilCoreApi.MilUtility_PathGeometryBounds");
        Assert.Contains("private static MilRectD GetManagedPathBoundsAsRB", pathGeometry, StringComparison.Ordinal);
        Assert.Contains("ParsePathGeometryData(pathData, context)", pathGeometry, StringComparison.Ordinal);
        Assert.Contains("PathStreamGeometryContext", pathGeometry, StringComparison.Ordinal);
        AssertGuardBefore(systemResources, "if (!OperatingSystem.IsWindows())", "new HwndWrapper(");
        AssertGuardBefore(systemResources, "if (OperatingSystem.IsWindows())", "XamlAccessLevel.AssemblyAccessTo(assembly)");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETHIGHCONTRAST");
        Assert.Contains("private const int DefaultScrollBarMetric = 17", systemParameters, StringComparison.Ordinal);
        Assert.Contains("private const int DefaultPrimaryScreenWidth = 1024", systemParameters, StringComparison.Ordinal);
        Assert.Contains("private const int DefaultPrimaryScreenHeight = 768", systemParameters, StringComparison.Ordinal);
        Assert.Contains("private static double GetSystemMetricPixel(SM metric, int fallbackPixel)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsWindows()", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_primaryScreenWidth = GetSystemMetricPixel(SM.CXSCREEN, DefaultPrimaryScreenWidth)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_primaryScreenHeight = GetSystemMetricPixel(SM.CYSCREEN, DefaultPrimaryScreenHeight)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_verticalScrollBarWidth = GetSystemMetricPixel(SM.CXVSCROLL, DefaultScrollBarMetric)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_horizontalScrollBarHeight = GetSystemMetricPixel(SM.CYHSCROLL, DefaultScrollBarMetric)", systemParameters, StringComparison.Ordinal);
        Assert.Contains("_caretWidth = 1.0", systemParameters, StringComparison.Ordinal);
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETCARETWIDTH");
        AssertGuardBefore(textSelection, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.GetLocaleInfoW");
        Assert.Contains("return cultureInfo.TextInfo.IsRightToLeft", textSelection, StringComparison.Ordinal);
        AssertGuardBefore(textServicesLoader, "if (!OperatingSystem.IsWindows())", "Invariant.Assert(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA");
        Assert.Contains("return null;", textServicesLoader, StringComparison.Ordinal);
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
        AssertGuardBefore(classification, "if (OperatingSystem.IsWindows())", "MILGetClassificationTables(out ct)");
        Assert.Contains("GetManagedUnicodeClass", classification, StringComparison.Ordinal);
        Assert.Contains("ManagedCharAttributeOf", classification, StringComparison.Ordinal);
        Assert.Contains("case UnicodeCategory.PrivateUse:", classification, StringComparison.Ordinal);
        Assert.Contains("return ManagedPrivateUseClass;", classification, StringComparison.Ordinal);
        Assert.Contains("ManagedPrivateUseClass => CreateManagedAttribute", classification, StringComparison.Ordinal);
        AssertGuardBefore(lineServices, "if (OperatingSystem.IsWindows())", "LoGetEscStringImpl(ref escStringInfo)");
        Assert.Contains("s_managedObjectReplacement", lineServices, StringComparison.Ordinal);
        Assert.Contains("The full WPF LineServices path is still native.", typeface, StringComparison.Ordinal);
        Assert.Contains("ContainsOnlyPrivateUseCharacters", typeface, StringComparison.Ordinal);
        Assert.Contains("IsPrivateUseCharacter", typeface, StringComparison.Ordinal);
        Assert.Contains("the existing null-glyph unshaped GlyphRun path", typeface, StringComparison.Ordinal);
        AssertGuardBefore(typeface, "if (!OperatingSystem.IsWindows())", "TypographyAvailabilities typography");
        AssertGuardBefore(uxThemeWrapper, "_themeState = OperatingSystem.IsWindows()", "SafeNativeMethods.IsUxThemeActive()");
        Assert.Contains("new ThemeState(true, \"Aero2\", \"NormalColor\")", uxThemeWrapper, StringComparison.Ordinal);
        AssertGuardBefore(dpiAwareness, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetWindowDpiAwarenessContext(hWnd)");
        AssertGuardBefore(osVersionHelper, "if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))", "IsWindows10RS5OrGreater()");
        AssertGuardBefore(osVersionHelper, "return OperatingSystemVersion.WindowsXPSP2;", "throw new Exception(\"OSVersionHelper.GetOsVersion Could not detect OS!\")");
        AssertGuardBefore(uiaCoreTypesApi, "if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))", "RawUiaGetReservedNotSupportedValue(out notSupportedValue)");
        AssertGuardBefore(uiaCoreTypesApi, "if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))", "RawUiaGetReservedMixedAttributeValue(out mixedAttributeValue)");
        AssertGuardBefore(uiaCoreTypesApi, "if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))", "LoadLibraryHelper.SecureLoadLibraryEx(DllImport.UIAutomationCore");
        Assert.Contains("s_reservedNotSupportedValue", uiaCoreTypesApi, StringComparison.Ordinal);
        Assert.Contains("s_reservedMixedAttributeValue", uiaCoreTypesApi, StringComparison.Ordinal);
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
