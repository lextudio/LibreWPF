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
        Assert.Contains("VerifyRetainedDrawingVisualBranch(target)", harnessProgram, StringComparison.Ordinal);
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
        var uxThemeWrapper = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "MS",
            "Win32",
            "UxThemeWrapper.cs"));
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

        AssertGuardBefore(compositionExports, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.MilCoreApi.EnterCompositionEngineLock()");
        AssertGuardBefore(uiElement, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.GetDC(desktopWnd)");
        AssertGuardBefore(systemResources, "if (!OperatingSystem.IsWindows())", "new HwndWrapper(");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETHIGHCONTRAST");
        AssertGuardBefore(uxThemeWrapper, "_themeState = OperatingSystem.IsWindows()", "SafeNativeMethods.IsUxThemeActive()");
        AssertGuardBefore(dpiAwareness, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetWindowDpiAwarenessContext(hWnd)");
        Assert.Contains("_useWin32MessagePump = OperatingSystem.IsWindows();", dispatcher, StringComparison.Ordinal);
        AssertGuardBefore(dispatcher, "if (_useWin32MessagePump)", "new MessageOnlyHwndWrapper()");
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
