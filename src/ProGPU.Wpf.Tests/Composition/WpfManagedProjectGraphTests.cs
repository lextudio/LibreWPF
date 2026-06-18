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
        Assert.Contains("if (Channel != null)", mediaContext, StringComparison.Ordinal);
        Assert.Contains("EnterInterlockedPresentation();", mediaContext, StringComparison.Ordinal);

        Assert.Contains("internal static void FlushDispatcherOperations(object window, DispatcherPriority markerPriority)", activationService, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.PushFrame(frame)", activationService, StringComparison.Ordinal);
        Assert.Contains("public interface IWpfDelayedRenderScheduler : IWpfRenderScheduler", proGpuScheduler, StringComparison.Ordinal);
        Assert.Contains("void RequestRender(TimeSpan delay)", proGpuScheduler, StringComparison.Ordinal);
        Assert.Contains("PortableMediaContextRenderServiceTypeName", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryRegisterMediaContextRenderService(presentationCoreAssembly)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("typeof(Action<TimeSpan>)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("IWpfDelayedRenderScheduler delayedScheduler", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("Host.RenderWakeupRequested += OnHostRenderWakeupRequested", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("FlushWpfDispatcherOperations(\"Loaded\", \"Render\")", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("FlushWpfDispatcherOperations(\"Render\")", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("TryFlushDispatcherOperations(Window, markerPriorityName)", proGpuActivation, StringComparison.Ordinal);
        Assert.Contains("FindPortableWindowActivationServiceType(window)", proGpuActivation, StringComparison.Ordinal);
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
        Assert.Contains("x:Name=\"MergedResourceBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource MergedAccentBrush}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"{StaticResource MergedBlockMargin}\"", mainWindowXaml, StringComparison.Ordinal);
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
        Assert.Contains("x:Key=\"FilteredItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Filter=\"OnFilteredItemsViewFilter\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"GroupedItemsView\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.GroupDescriptions", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("PropertyGroupDescription PropertyName=\"Category\"", mainWindowXaml, StringComparison.Ordinal);
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
        Assert.Contains("x:Name=\"FilteredItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource FilteredItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupedItemsList\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Source={StaticResource GroupedItemsView}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ListBox.GroupStyle", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("GroupStyle.HeaderTemplate", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GroupHeaderTextBlock\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"group header template\"", mainWindowXaml, StringComparison.Ordinal);
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
        Assert.Contains("GridLinesVisibility=\"Horizontal\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("HeadersVisibility=\"Column\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedItem, Mode=TwoWay}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataGrid.Columns", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataGridTextColumn", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("DataGridCheckBoxColumn", mainWindowXaml, StringComparison.Ordinal);
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
        Assert.Contains("x:Name=\"AccessKeyFocusScope\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FocusManager.IsFocusScope=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("FocusManager.FocusedElement=\"{Binding ElementName=AccessTargetBox}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccessTargetLabel\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"_Access target\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"{Binding ElementName=AccessTargetBox}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccessTargetBox\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StandaloneAccessText\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"_Standalone access text\"", mainWindowXaml, StringComparison.Ordinal);
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
        Assert.Contains("public sealed class ProviderDataFactory", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string CreateProviderGreeting", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{prefix} data {value}\";", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeTextExtension : MarkupExtension", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public override object ProvideValue(IServiceProvider serviceProvider)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("return $\"{Prefix} {Value} extension\";", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public sealed class SmokeAdorner : Adorner", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("protected override void OnRender(DrawingContext drawingContext)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("drawingContext.DrawRectangle(null, new Pen(Brushes.LimeGreen, 1.0), adornedBounds)", mainWindowCodeBehind, StringComparison.Ordinal);
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
        Assert.Contains("public int ThumbDragStartedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ThumbDragDeltaCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int ThumbDragCompletedCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int BubbledThumbDragDeltaCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDragManagerThumbDragStarted(object sender, DragStartedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDragManagerThumbDragDelta(object sender, DragDeltaEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDragManagerThumbDragCompleted(object sender, DragCompletedEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnBubbledThumbDragDelta(object sender, DragDeltaEventArgs e)", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastBubbledThumbDragDeltaOriginalSourceName = e.OriginalSource is FrameworkElement source ? source.Name : null", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int StyledClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnStyledButtonClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastStyledClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public int MenuClickCount", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnMenuClick", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("LastMenuClickRoutedEventName = e.RoutedEvent?.Name", mainWindowCodeBehind, StringComparison.Ordinal);
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
        Assert.Contains("public sealed class SmokeItem", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public string Category", mainWindowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("public bool IsActive { get; set; }", mainWindowCodeBehind, StringComparison.Ordinal);
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
        Assert.Contains("System.Windows.Markup.XamlReader", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Markup.XamlWriter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ParseLooseXaml(presentationFramework, looseXaml)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("SaveLooseXaml(presentationFramework, brush)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader style StaticResource brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader RelativeSource binding text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader Binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized GradientStop", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip {description} stop color", harnessProgram, StringComparison.Ordinal);
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
        Assert.Contains("ValidateInputBinding(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Window input bindings", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled KeyGesture", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled KeyBinding command executed parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMenuItems(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("RaiseMenuItemClick(clickItem)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled command MenuItem routed command", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled command MenuItem CanExecute result", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MenuItem Click handler count", harnessProgram, StringComparison.Ordinal);
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
        Assert.Contains("ValidateAccessKeyFocusScope(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowAccessKeyFocusScope(presentationCore, window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled access-key Label target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Label access key registered", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Label access key focused target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowNavigationFrame(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Frame", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Page content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Page content text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Page content button", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Page click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Page click routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateFrameJournalNavigation(frame, flushDispatcherOperations)", harnessProgram, StringComparison.Ordinal);
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
        Assert.Contains("compiled filtered CollectionViewSource resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox filtered CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource filtered item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("FilteredItemsFilterCount", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource group descriptions", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource group property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox grouped CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox GroupStyle entries", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupStyle HeaderTemplate binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateCollectionViewGroup(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource collection-change groups", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled filtered CollectionViewSource collection-change item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource collection-change first item", harnessProgram, StringComparison.Ordinal);
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
        Assert.Contains("ValidateLooseXamlReader(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateLooseXamlWriterRoundTrip(presentationFramework)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Markup.XamlReader", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Markup.XamlWriter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ParseLooseXaml(presentationFramework, looseXaml)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("SaveLooseXaml(presentationFramework, brush)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader style StaticResource brush", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader RelativeSource binding text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlReader Binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter serialized GradientStop", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("loose XamlWriter round-trip {description} stop color", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("Invoke(application, \"Run\")", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("PortableWindowActivationServiceTypeName = \"System.Windows.PortableWindowActivationService\"", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("new Func<object, object>(recorder.Activate)", harnessProgram, StringComparison.Ordinal);
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
        Assert.Contains("ValidateInputBinding(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Window input bindings", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled KeyGesture", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled KeyBinding command executed parameter", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateMenuItems(window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("RaiseMenuItemClick(clickItem)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled command MenuItem routed command", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled command MenuItem CanExecute result", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled MenuItem Click handler count", harnessProgram, StringComparison.Ordinal);
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
        Assert.Contains("ValidateAccessKeyFocusScope(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowAccessKeyFocusScope(_presentationCore, typedActivation.Window)", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled access-key Label target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Label access key registered", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Label access key focused target", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidatePostShowNavigationFrame(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Frame", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Page content", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Page content text", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled Page content button", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Page click handler count", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled source Page click routed event", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateFrameJournalNavigation(frame, flushDispatcherOperations)", harnessProgram, StringComparison.Ordinal);
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
        Assert.Contains("compiled filtered CollectionViewSource resource", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox filtered CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource filtered item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("FilteredItemsFilterCount", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource group descriptions", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource group property", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox grouped CollectionViewSource binding", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled ListBox GroupStyle entries", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled GroupStyle HeaderTemplate binding path", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("ValidateCollectionViewGroup(", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource collection-change groups", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled filtered CollectionViewSource collection-change item", harnessProgram, StringComparison.Ordinal);
        Assert.Contains("compiled CollectionViewSource collection-change first item", harnessProgram, StringComparison.Ordinal);
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
        Assert.Contains("themed ItemsControl item content", harnessProgram, StringComparison.Ordinal);
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
        AssertGuardBefore(systemResources, "if (!OperatingSystem.IsWindows())", "new HwndWrapper(");
        AssertGuardBefore(systemResources, "if (OperatingSystem.IsWindows())", "XamlAccessLevel.AssemblyAccessTo(assembly)");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETHIGHCONTRAST");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETMOUSEVANISH");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETDROPSHADOW");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETCOMBOBOXANIMATION");
        AssertGuardBefore(systemParameters, "if (!OperatingSystem.IsWindows())", "UnsafeNativeMethods.SystemParametersInfo(NativeMethods.SPI_GETMENUANIMATION");
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
        AssertGuardBefore(dpiAwareness, "if (!OperatingSystem.IsWindows())", "SafeNativeMethods.GetWindowDpiAwarenessContext(hWnd)");
        AssertGuardBefore(osVersionHelper, "if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))", "IsWindows10RS5OrGreater()");
        AssertGuardBefore(osVersionHelper, "return OperatingSystemVersion.WindowsXPSP2;", "throw new Exception(\"OSVersionHelper.GetOsVersion Could not detect OS!\")");
        AssertGuardBefore(uiaCoreTypesApi, "if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))", "RawUiaGetReservedNotSupportedValue(out notSupportedValue)");
        AssertGuardBefore(uiaCoreTypesApi, "if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))", "RawUiaGetReservedMixedAttributeValue(out mixedAttributeValue)");
        AssertGuardBefore(uiaCoreTypesApi, "if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))", "LoadLibraryHelper.SecureLoadLibraryEx(DllImport.UIAutomationCore");
        Assert.Contains("s_reservedNotSupportedValue", uiaCoreTypesApi, StringComparison.Ordinal);
        Assert.Contains("s_reservedMixedAttributeValue", uiaCoreTypesApi, StringComparison.Ordinal);
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
