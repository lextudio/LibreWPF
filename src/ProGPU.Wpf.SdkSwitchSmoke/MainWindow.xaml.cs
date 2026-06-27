using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace ProGPU.Wpf.SdkSwitchSmoke;

public partial class MainWindow : Window
{
    public static RoutedUICommand SmokeCommand { get; } = new(
        "Smoke Command",
        "SmokeCommand",
        typeof(MainWindow));

    private const string LiveValidationEnvironmentVariable = "PROGPU_WPF_SDK_SWITCH_LIVE_VALIDATE";
    private const int LiveValidationMaxAttempts = 300;
    private static readonly TimeSpan LiveValidationRetryDelay = TimeSpan.FromMilliseconds(16);

    public MainWindow()
    {
        DataContext = new SmokeViewModel();
        InitializeComponent();
    }

    public int SmokeCommandCanExecuteCount { get; private set; }

    public int SmokeCommandExecutionCount { get; private set; }

    public string? LastSmokeCommandParameter { get; private set; }

    public int EventSetterClickCount { get; private set; }

    public string? LastEventSetterSenderName { get; private set; }

    public string? LastEventSetterRoutedEventName { get; private set; }

    public int SmokeRoutedEventCount { get; private set; }

    public int MenuClickCount { get; private set; }

    public int MenuCheckedCount { get; private set; }

    public int MenuUncheckedCount { get; private set; }

    public int ManagedCheckBoxCheckedCount { get; private set; }

    public int ManagedCheckBoxUncheckedCount { get; private set; }

    public int ManagedRadioCheckedCount { get; private set; }

    public int ManagedRadioUncheckedCount { get; private set; }

    public string? LastManagedRadioCheckedName { get; private set; }

    public int PasswordChangedCount { get; private set; }

    public string? LastPasswordChangedSenderName { get; private set; }

    public string? LastPasswordChangedRoutedEventName { get; private set; }

    public int DateSelectionChangedCount { get; private set; }

    public string? LastDateSelectionChangedSenderName { get; private set; }

    public int SelectorSelectionChangedCount { get; private set; }

    public int TabSelectionChangedCount { get; private set; }

    public int ExpanderExpandedCount { get; private set; }

    public int ExpanderCollapsedCount { get; private set; }

    public int RangeValueChangedCount { get; private set; }

    public int SmokeFrameNavigatingCount { get; private set; }

    public int SmokeFrameNavigatedCount { get; private set; }

    public int SmokeFrameLoadCompletedCount { get; private set; }

    public string? LastSmokeFrameNavigatingUri { get; private set; }

    public string? LastSmokeFrameNavigationMode { get; private set; }

    public string? LastSmokeFrameNavigatedUri { get; private set; }

    public string? LastSmokeFrameNavigatedContentType { get; private set; }

    public string? LastSmokeFrameLoadCompletedUri { get; private set; }

    public int SmokePageFunctionReturnCount { get; private set; }

    public string? LastSmokePageFunctionResult { get; private set; }

    public int LiveRenderSurfaceValidationCount { get; private set; }

    public string? LiveRenderSurfaceValidationStatus { get; private set; }

    public int DocumentLinkRequestNavigateCount { get; private set; }

    public string? LastDocumentLinkRequestNavigateUri { get; private set; }

    public string? LastDocumentLinkRequestNavigateRoutedEventName { get; private set; }

    public int LoadedStoryboardTextLoadedCount { get; private set; }

    public string? LastLoadedStoryboardTextRoutedEventName { get; private set; }

    public object? LastSmokeRoutedEventSender { get; private set; }

    public object? LastSmokeRoutedEventSource { get; private set; }

    private int _liveRenderSurfaceValidationAttempts;

    private void OnSdkSwitchSmokeWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (Environment.GetEnvironmentVariable(LiveValidationEnvironmentVariable) == "1")
        {
            var expectedLogicalWidth = ToExpectedLogicalDimension(Width, RenderSize.Width);
            var expectedLogicalHeight = ToExpectedLogicalDimension(Height, RenderSize.Height);
            _ = ValidateRequiredLiveRenderSurfaceGeometryAsync(
                expectedLogicalWidth,
                expectedLogicalHeight);
            return;
        }

        ScheduleLiveRenderSurfaceValidation();
    }

    private void ValidateLiveRenderSurfaceGeometry()
    {
        bool requireLiveValidation = Environment.GetEnvironmentVariable(LiveValidationEnvironmentVariable) == "1";
        _liveRenderSurfaceValidationAttempts++;
        if (!TryGetPortableActivationHost(out object? host))
        {
            if (TryScheduleLiveRenderSurfaceValidationRetry())
            {
                return;
            }

            if (requireLiveValidation)
            {
                throw new InvalidOperationException("Expected the SDK-switch smoke app to have a live ProGPU host.");
            }

            return;
        }

        object liveHost = host
            ?? throw new InvalidOperationException("Expected the SDK-switch smoke app to have a live ProGPU host.");
        if (GetRequiredProperty(liveHost, "HasPresentedFrame") is bool hasPresentedFrame &&
            !hasPresentedFrame)
        {
            if (TryScheduleLiveRenderSurfaceValidationRetry())
            {
                return;
            }

            if (requireLiveValidation)
            {
                throw new InvalidOperationException("Expected the SDK-switch smoke app to present a ProGPU frame before live geometry validation.");
            }

            return;
        }

        var expectedLogicalWidth = ToExpectedLogicalDimension(Width, RenderSize.Width);
        var expectedLogicalHeight = ToExpectedLogicalDimension(Height, RenderSize.Height);
        string status = ValidateLiveRenderSurfaceGeometryCore(
            liveHost,
            expectedLogicalWidth,
            expectedLogicalHeight);
        LiveRenderSurfaceValidationCount++;
        LiveRenderSurfaceValidationStatus = status;
        if (requireLiveValidation)
        {
            Console.WriteLine($"ProGPU WPF SDK switch live geometry validation succeeded: {LiveRenderSurfaceValidationStatus}.");
            Environment.Exit(0);
        }
    }

    private async Task ValidateRequiredLiveRenderSurfaceGeometryAsync(
        uint expectedLogicalWidth,
        uint expectedLogicalHeight)
    {
        int presentedSampleCount = 0;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            await Task.Delay(LiveValidationRetryDelay).ConfigureAwait(false);
            if (!TryGetPortableActivationHost(out object? liveHost) ||
                liveHost == null)
            {
                continue;
            }

            if (GetRequiredProperty(liveHost, "HasPresentedFrame") is not bool hasPresentedFrame ||
                !hasPresentedFrame)
            {
                WakeLiveRenderHost(liveHost);
                continue;
            }

            presentedSampleCount++;
            if (presentedSampleCount < 5)
            {
                continue;
            }

            try
            {
                string status = await InvokeWithLiveHostWakeAsync(
                    liveHost,
                    () => ValidateLiveRenderSurfaceGeometryCore(
                        liveHost,
                        expectedLogicalWidth,
                        expectedLogicalHeight),
                    DispatcherPriority.Send);
                LiveRenderSurfaceValidationCount++;
                LiveRenderSurfaceValidationStatus = status;
                Console.WriteLine($"ProGPU WPF SDK switch live geometry validation succeeded: {status}.");
                string inputStatus = await ValidateLiveInputAsync(liveHost);
                Console.WriteLine($"ProGPU WPF SDK switch live input validation succeeded: {status}; {inputStatus}.");
                Environment.Exit(0);
            }
            catch (Exception ex) when (ex is InvalidOperationException or MissingMemberException or MissingMethodException or TypeLoadException)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(1);
            }
        }

        Console.Error.WriteLine("Expected the SDK-switch smoke app to present a stable ProGPU frame before live geometry validation.");
        Environment.Exit(1);
    }

    private async Task<string> ValidateLiveInputAsync(object liveHost)
    {
        Button? actionButton = null;
        TextBox? inputBox = null;
        SmokeViewModel? viewModel = null;
        Point inputPoint = new();
        object? inputHit = null;
        string lastTargetState = "not checked";

        bool sentPointerInput = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            sentPointerInput = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    actionButton = Require<Button>("ActionButton");
                    lastTargetState =
                        $"ActionButton.IsVisible={actionButton.IsVisible}, " +
                        $"ActionButton.ActualSize={actionButton.ActualWidth:0.###}x{actionButton.ActualHeight:0.###}, " +
                        $"ActionButton.IsEnabled={actionButton.IsEnabled}, " +
                        $"ActionButton.Focusable={actionButton.Focusable}, " +
                        $"ActionButton.IsHitTestVisible={actionButton.IsHitTestVisible}";
                    if (!actionButton.IsVisible ||
                        actionButton.ActualWidth <= 1.0 ||
                        actionButton.ActualHeight <= 1.0 ||
                        !actionButton.IsEnabled ||
                        !actionButton.IsHitTestVisible)
                    {
                        return false;
                    }

                    Point center = actionButton.TranslatePoint(
                        new Point(Math.Max(1.0, actionButton.ActualWidth) / 2.0, Math.Max(1.0, actionButton.ActualHeight) / 2.0),
                        this);
                    object? hit = InputHitTest(center);
                    lastTargetState += $", Input=({center.X:0.###}, {center.Y:0.###}), InputHitTest={DescribeInputElement(hit)}";
                    if (hit == null)
                    {
                        return false;
                    }

                    ClickStatus.Text = "not clicked";
                    inputPoint = center;
                    inputHit = hit;
                    RaiseHostInput(liveHost, "MouseMove", x: center.X, y: center.Y);
                    RaiseHostInput(liveHost, "MouseDown", x: center.X, y: center.Y, button: "Left");
                    RaiseHostInput(liveHost, "MouseUp", x: center.X, y: center.Y, button: "Left");
                    return true;
                },
                DispatcherPriority.Send);
            if (sentPointerInput)
            {
                break;
            }

            await Task.Delay(LiveValidationRetryDelay).ConfigureAwait(false);
        }

        if (!sentPointerInput)
        {
            throw new InvalidOperationException(
                $"Expected SDK-switch live ActionButton to become visible and hit-testable before injecting input, but last state was: {lastTargetState}.");
        }

        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual("clicked", ClickStatus.Text, "SDK-switch live ActionButton click status");

                inputBox = Require<TextBox>("InputBox");
                viewModel = Require<SmokeViewModel>(DataContext as SmokeViewModel, "SDK-switch live input view model");
                inputBox.Text = string.Empty;
                inputBox.CaretIndex = 0;
                inputBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                Keyboard.Focus(inputBox);
                if (!ReferenceEquals(Keyboard.FocusedElement, inputBox))
                {
                    throw new InvalidOperationException(
                        $"Expected SDK-switch live input setup to focus InputBox, but focused '{DescribeInputElement(Keyboard.FocusedElement)}'. " +
                        $"MouseInput=({inputPoint.X:0.###}, {inputPoint.Y:0.###}), " +
                        $"MouseInputHitTest={DescribeInputElement(inputHit)}.");
                }

                foreach (char character in "Sdk")
                {
                    string key = char.ToUpperInvariant(character).ToString();
                    RaiseHostInput(liveHost, "KeyDown", key: key);
                    RaiseHostInput(liveHost, "TextInput", character: character);
                    RaiseHostInput(liveHost, "KeyUp", key: key);
                }
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        int commandCountBefore = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual("Sdk", Require<TextBox>(inputBox, "SDK-switch live InputBox").Text, "SDK-switch live TextBox text after host text input");
                AssertEqual("Sdk", Require<SmokeViewModel>(viewModel, "SDK-switch live input view model").InputText, "SDK-switch live view-model source after host text input");

                int before = SmokeCommandExecutionCount;
                RaiseHostInput(liveHost, "KeyDown", key: "F6", modifiers: "Control");
                RaiseHostInput(liveHost, "KeyUp", key: "F6", modifiers: "Control");
                return before;
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        return await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(commandCountBefore + 1, SmokeCommandExecutionCount, "SDK-switch live Ctrl+F6 KeyBinding execution count");
                AssertEqual("input binding payload", LastSmokeCommandParameter, "SDK-switch live Ctrl+F6 KeyBinding command parameter");
                AssertEqual("input binding payload", CommandStatus.Text, "SDK-switch live command status text");
                return "mouse button click, TextBox text input, and Ctrl+F6 KeyBinding updated";
            },
            DispatcherPriority.Send);
    }

    private static string ValidateLiveRenderSurfaceGeometryCore(
        object liveHost,
        uint expectedLogicalWidth,
        uint expectedLogicalHeight)
    {
        object geometry = InvokeRequired(liveHost, "ResolveCurrentRenderSurfaceGeometry");
        var logicalWidth = Convert.ToUInt32(GetRequiredProperty(geometry, "LogicalWidth"));
        var logicalHeight = Convert.ToUInt32(GetRequiredProperty(geometry, "LogicalHeight"));
        var pixelWidth = Convert.ToUInt32(GetRequiredProperty(geometry, "PixelWidth"));
        var pixelHeight = Convert.ToUInt32(GetRequiredProperty(geometry, "PixelHeight"));
        var dpiScale = Convert.ToDouble(GetRequiredProperty(geometry, "DpiScale"), CultureInfo.InvariantCulture);
        var viewportX = Convert.ToUInt32(GetRequiredProperty(geometry, "ViewportX"));
        var viewportY = Convert.ToUInt32(GetRequiredProperty(geometry, "ViewportY"));
        var viewportWidth = Convert.ToUInt32(GetRequiredProperty(geometry, "ViewportWidth"));
        var viewportHeight = Convert.ToUInt32(GetRequiredProperty(geometry, "ViewportHeight"));

        AssertClose(logicalWidth, expectedLogicalWidth, "live ProGPU WPF logical width");
        AssertClose(logicalHeight, expectedLogicalHeight, "live ProGPU WPF logical height");
        if (pixelWidth < logicalWidth || pixelHeight < logicalHeight)
        {
            throw new InvalidOperationException(
                $"Expected live ProGPU WPF pixels to cover logical content, but got logical {logicalWidth}x{logicalHeight} and pixels {pixelWidth}x{pixelHeight}.");
        }

        if (dpiScale > 1.01 &&
            (pixelWidth <= logicalWidth || pixelHeight <= logicalHeight))
        {
            throw new InvalidOperationException(
                $"Expected live ProGPU WPF high-DPI pixels to exceed logical size, but got logical {logicalWidth}x{logicalHeight}, pixels {pixelWidth}x{pixelHeight}, DPI {dpiScale}.");
        }

        if (viewportWidth < logicalWidth || viewportHeight < logicalHeight)
        {
            throw new InvalidOperationException(
                $"Expected live ProGPU WPF viewport to cover logical content, but got logical {logicalWidth}x{logicalHeight} and viewport {viewportWidth}x{viewportHeight} at {viewportX},{viewportY}.");
        }

        if (viewportX + viewportWidth > pixelWidth || viewportY + viewportHeight > pixelHeight)
        {
            throw new InvalidOperationException(
                $"Expected live ProGPU WPF viewport to fit inside the physical target, but got viewport {viewportWidth}x{viewportHeight} at {viewportX},{viewportY} and pixels {pixelWidth}x{pixelHeight}.");
        }

        if (viewportX != 0 || viewportY != 0 || viewportWidth != pixelWidth || viewportHeight != pixelHeight)
        {
            throw new InvalidOperationException(
                $"Expected live ProGPU WPF viewport to use the full physical target, but got viewport {viewportWidth}x{viewportHeight} at {viewportX},{viewportY} and pixels {pixelWidth}x{pixelHeight}.");
        }

        if (dpiScale > 1.01 &&
            (viewportWidth <= logicalWidth || viewportHeight <= logicalHeight))
        {
            throw new InvalidOperationException(
                $"Expected live ProGPU WPF high-DPI viewport to exceed logical size, but got logical {logicalWidth}x{logicalHeight}, viewport {viewportWidth}x{viewportHeight}, DPI {dpiScale}.");
        }

        if (TryGetLiveFramebufferSize(liveHost, out var framebufferWidth, out var framebufferHeight))
        {
            AssertClose(pixelWidth, framebufferWidth, "live ProGPU WPF physical framebuffer width");
            AssertClose(pixelHeight, framebufferHeight, "live ProGPU WPF physical framebuffer height");
            if ((framebufferWidth > logicalWidth || framebufferHeight > logicalHeight) &&
                dpiScale <= 1.01)
            {
                throw new InvalidOperationException(
                    $"Expected live ProGPU WPF DPI to track the physical framebuffer, but got logical {logicalWidth}x{logicalHeight}, framebuffer {framebufferWidth}x{framebufferHeight}, DPI {dpiScale}.");
            }
        }

        if (TryGetLivePresentedFramePixelSize(liveHost, out var framePixelWidth, out var framePixelHeight))
        {
            AssertClose(framePixelWidth, pixelWidth, "live ProGPU WPF presented frame pixel width");
            AssertClose(framePixelHeight, pixelHeight, "live ProGPU WPF presented frame pixel height");
        }

        if (TryGetLiveSwapChainSize(liveHost, out var swapChainWidth, out var swapChainHeight))
        {
            AssertClose(swapChainWidth, pixelWidth, "live ProGPU WPF swapchain pixel width");
            AssertClose(swapChainHeight, pixelHeight, "live ProGPU WPF swapchain pixel height");
        }

        return $"logical {logicalWidth}x{logicalHeight}, pixels {pixelWidth}x{pixelHeight}, viewport {viewportWidth}x{viewportHeight}@{viewportX},{viewportY}, dpi {dpiScale:0.###}";
    }

    private async Task InvokeWithLiveHostWakeAsync(
        object liveHost,
        Action callback,
        DispatcherPriority priority)
    {
        if (Dispatcher.CheckAccess())
        {
            callback();
            return;
        }

        DispatcherOperation operation = Dispatcher.InvokeAsync(callback, priority);
        WakeLiveRenderHost(liveHost);
        await operation;
    }

    private async Task<T> InvokeWithLiveHostWakeAsync<T>(
        object liveHost,
        Func<T> callback,
        DispatcherPriority priority)
    {
        if (Dispatcher.CheckAccess())
        {
            return callback();
        }

        DispatcherOperation<T> operation = Dispatcher.InvokeAsync(callback, priority);
        WakeLiveRenderHost(liveHost);
        return await operation;
    }

    private static void WakeLiveRenderHost(object liveHost)
    {
        object scheduler = GetRequiredProperty(liveHost, "WpfRenderScheduler");
        MethodInfo requestRender = scheduler.GetType().GetMethod(
            "RequestRender",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)
            ?? throw new MissingMethodException(scheduler.GetType().FullName, "RequestRender");
        requestRender.Invoke(scheduler, null);

        MethodInfo? requestNativeLoopWakeup = liveHost.GetType().GetMethod(
            "TryRequestNativeLoopWakeup",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        requestNativeLoopWakeup?.Invoke(liveHost, null);
    }

    private static void RaiseHostInput(
        object liveHost,
        string kind,
        string? key = null,
        char? character = null,
        double x = 0.0,
        double y = 0.0,
        string button = "None",
        string modifiers = "None")
    {
        object input = CreateWpfInputEventArgs(liveHost, kind, key, character, x, y, button, modifiers);
        MethodInfo method = liveHost.GetType().GetMethod(
            "OnPlatformInputReceived",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(liveHost.GetType().FullName, "OnPlatformInputReceived");
        method.Invoke(liveHost, new object?[] { null, input });
    }

    private static object CreateWpfInputEventArgs(
        object liveHost,
        string kind,
        string? key,
        char? character,
        double x,
        double y,
        string button,
        string modifiers)
    {
        Assembly assembly = liveHost.GetType().Assembly;
        Type inputType = assembly.GetType("System.Windows.Media.ProGPU.Platform.WpfInputEventArgs", throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.Media.ProGPU.Platform.WpfInputEventArgs");
        Type kindType = assembly.GetType("System.Windows.Media.ProGPU.Platform.WpfInputEventKind", throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.Media.ProGPU.Platform.WpfInputEventKind");
        Type buttonType = assembly.GetType("System.Windows.Media.ProGPU.Platform.WpfMouseButton", throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.Media.ProGPU.Platform.WpfMouseButton");
        Type modifiersType = assembly.GetType("System.Windows.Media.ProGPU.Platform.WpfInputModifiers", throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.Media.ProGPU.Platform.WpfInputModifiers");

        return Activator.CreateInstance(
            inputType,
            Enum.Parse(kindType, kind),
            key,
            0,
            character.HasValue ? character.Value : null,
            x,
            y,
            0.0,
            0.0,
            Enum.Parse(buttonType, button),
            Enum.Parse(modifiersType, modifiers))
            ?? throw new InvalidOperationException("Expected WpfInputEventArgs construction to succeed.");
    }

    private T Require<T>(string name)
        where T : class
    {
        return FindName(name) as T
            ?? throw new InvalidOperationException($"Expected {name} to be a {typeof(T).Name}.");
    }

    private static T Require<T>(T? value, string description)
        where T : class
    {
        return value
            ?? throw new InvalidOperationException($"Expected {description} to be a {typeof(T).Name}.");
    }

    private static string DescribeInputElement(object? element)
    {
        if (element == null)
        {
            return "<null>";
        }

        if (element is FrameworkElement frameworkElement && !string.IsNullOrEmpty(frameworkElement.Name))
        {
            return $"{element.GetType().Name}#{frameworkElement.Name}";
        }

        return element.GetType().Name;
    }

    private static void AssertEqual<T>(T expected, T actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expected}' but was '{actual}'.");
        }
    }

    private void ScheduleLiveRenderSurfaceValidation()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(ValidateLiveRenderSurfaceGeometry));
    }

    private bool TryScheduleLiveRenderSurfaceValidationRetry()
    {
        if (_liveRenderSurfaceValidationAttempts >= LiveValidationMaxAttempts)
        {
            return false;
        }

        _ = Task.Delay(LiveValidationRetryDelay).ContinueWith(
            _ => ScheduleLiveRenderSurfaceValidation(),
            TaskScheduler.Default);
        return true;
    }

    private bool TryGetPortableActivationHost(out object? host)
    {
        host = null;
        PropertyInfo? activationProperty = typeof(Window).GetProperty(
            "PortableWindowActivation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        object? activation = activationProperty?.GetValue(this);
        if (activation == null)
        {
            return false;
        }

        PropertyInfo? hostProperty = activation.GetType().GetProperty(
            "Host",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        host = hostProperty?.GetValue(activation);
        return host != null;
    }

    private static bool TryGetLiveFramebufferSize(object liveHost, out uint width, out uint height)
    {
        width = 0;
        height = 0;
        object? silkWindow = TryGetProperty(liveHost, "SilkWindow");
        if (silkWindow == null)
        {
            return false;
        }

        object? framebufferSize = TryGetProperty(silkWindow, "FramebufferSize");
        return framebufferSize != null &&
            TryReadPositiveUintProperty(framebufferSize, "X", out width) &&
            TryReadPositiveUintProperty(framebufferSize, "Y", out height);
    }

    private static bool TryGetLivePresentedFramePixelSize(object liveHost, out uint width, out uint height)
    {
        width = 0;
        height = 0;
        object? frameState = TryGetProperty(liveHost, "LastPresentedFrameState");
        return frameState != null &&
            TryReadPositiveUintProperty(frameState, "PixelWidth", out width) &&
            TryReadPositiveUintProperty(frameState, "PixelHeight", out height);
    }

    private static bool TryGetLiveSwapChainSize(object liveHost, out uint width, out uint height)
    {
        width = 0;
        height = 0;
        object? target = TryGetProperty(liveHost, "CompositionTarget");
        object? context = target == null
            ? null
            : TryGetProperty(target, "Context");
        return context != null &&
            TryReadPositiveUintField(context, "_lastWidth", out width) &&
            TryReadPositiveUintField(context, "_lastHeight", out height);
    }

    private static object? TryGetProperty(object target, string propertyName)
    {
        return target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(target);
    }

    private static object? TryGetField(object target, string fieldName)
    {
        return target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(target);
    }

    private static object InvokeRequired(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        return method.Invoke(target, null)
            ?? throw new InvalidOperationException($"Expected {target.GetType().FullName}.{methodName} to return a value.");
    }

    private static object GetRequiredProperty(object target, string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(target.GetType().FullName, propertyName);
        return property.GetValue(target)
            ?? throw new InvalidOperationException($"Expected {target.GetType().FullName}.{propertyName} to return a value.");
    }

    private static bool TryReadPositiveUintProperty(object target, string propertyName, out uint value)
    {
        value = 0;
        object? rawValue = TryGetProperty(target, propertyName);
        return TryConvertPositiveUint(rawValue, out value);
    }

    private static bool TryReadPositiveUintField(object target, string fieldName, out uint value)
    {
        value = 0;
        object? rawValue = TryGetField(target, fieldName);
        return TryConvertPositiveUint(rawValue, out value);
    }

    private static bool TryConvertPositiveUint(object? rawValue, out uint value)
    {
        value = 0;
        try
        {
            int dimension = Convert.ToInt32(rawValue, CultureInfo.InvariantCulture);
            if (dimension <= 0)
            {
                return false;
            }

            value = (uint)dimension;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static uint ToExpectedLogicalDimension(double declaredDimension, double renderDimension)
    {
        double value = double.IsFinite(declaredDimension) && declaredDimension > 0.0
            ? declaredDimension
            : renderDimension;
        return Math.Max(1u, (uint)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static void AssertClose(uint actual, uint expected, string description)
    {
        if (Math.Abs((long)actual - expected) > 1)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be {expected}, but got {actual}.");
        }
    }

    private void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        ClickStatus.Text = "clicked";
    }

    private void OnEventSetterButtonClick(object sender, RoutedEventArgs e)
    {
        EventSetterClickCount++;
        LastEventSetterSenderName = (sender as FrameworkElement)?.Name;
        LastEventSetterRoutedEventName = e.RoutedEvent?.Name;
        EventSetterStatus.Text = "event setter clicked";
        e.Handled = true;
    }

    private void OnSmokeCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        SmokeCommandCanExecuteCount++;
        e.CanExecute = true;
        e.Handled = true;
    }

    private void OnSmokeCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SmokeCommandExecutionCount++;
        LastSmokeCommandParameter = e.Parameter?.ToString();
        CommandStatus.Text = LastSmokeCommandParameter ?? "executed";
        e.Handled = true;
    }

    private void OnMenuItemClick(object sender, RoutedEventArgs e)
    {
        MenuClickCount++;
        MenuStatus.Text = "menu click";
        e.Handled = true;
    }

    private void OnCheckableMenuItemChecked(object sender, RoutedEventArgs e)
    {
        MenuCheckedCount++;
        if (MenuStatus != null)
        {
            MenuStatus.Text = "menu checked";
        }

        e.Handled = true;
    }

    private void OnCheckableMenuItemUnchecked(object sender, RoutedEventArgs e)
    {
        MenuUncheckedCount++;
        if (MenuStatus != null)
        {
            MenuStatus.Text = "menu unchecked";
        }

        e.Handled = true;
    }

    private void OnManagedCheckBoxChecked(object sender, RoutedEventArgs e)
    {
        ManagedCheckBoxCheckedCount++;
        if (CheckChoiceStatus != null)
        {
            CheckChoiceStatus.Text = "check checked";
        }

        e.Handled = true;
    }

    private void OnManagedCheckBoxUnchecked(object sender, RoutedEventArgs e)
    {
        ManagedCheckBoxUncheckedCount++;
        if (CheckChoiceStatus != null)
        {
            CheckChoiceStatus.Text = "check unchecked";
        }

        e.Handled = true;
    }

    private void OnManagedRadioChecked(object sender, RoutedEventArgs e)
    {
        ManagedRadioCheckedCount++;
        LastManagedRadioCheckedName = (sender as FrameworkElement)?.Name;
        if (CheckChoiceStatus != null)
        {
            CheckChoiceStatus.Text = $"radio checked: {LastManagedRadioCheckedName}";
        }

        e.Handled = true;
    }

    private void OnManagedRadioUnchecked(object sender, RoutedEventArgs e)
    {
        ManagedRadioUncheckedCount++;
        if (CheckChoiceStatus != null)
        {
            CheckChoiceStatus.Text = "radio unchecked";
        }

        e.Handled = true;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        PasswordChangedCount++;
        LastPasswordChangedSenderName = (sender as FrameworkElement)?.Name;
        LastPasswordChangedRoutedEventName = e.RoutedEvent?.Name;
        if (PasswordStatus != null)
        {
            PasswordStatus.Text = "password changed";
        }

        e.Handled = true;
    }

    private void OnDateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DateSelectionChangedCount++;
        LastDateSelectionChangedSenderName = (sender as FrameworkElement)?.Name;
        if (DateStatus != null)
        {
            DateStatus.Text = $"date changed: {LastDateSelectionChangedSenderName}";
        }

        e.Handled = true;
    }

    private void OnSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectorSelectionChangedCount++;
        if (SelectorStatus != null)
        {
            SelectorStatus.Text = $"selector selected: {SmokeComboBox.SelectedValue}";
        }

        e.Handled = true;
    }

    private void OnTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender))
        {
            return;
        }

        TabSelectionChangedCount++;
        if (TabStatus != null && SmokeTabs.SelectedItem is TabItem selectedTab)
        {
            TabStatus.Text = $"tab selected: {selectedTab.Header}";
        }

        e.Handled = true;
    }

    private void OnSmokeExpanderExpanded(object sender, RoutedEventArgs e)
    {
        ExpanderExpandedCount++;
        if (RangeStatus != null)
        {
            RangeStatus.Text = "range expanded";
        }

        e.Handled = true;
    }

    private void OnSmokeExpanderCollapsed(object sender, RoutedEventArgs e)
    {
        ExpanderCollapsedCount++;
        if (RangeStatus != null)
        {
            RangeStatus.Text = "range collapsed";
        }

        e.Handled = true;
    }

    private void OnRangeValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        RangeValueChangedCount++;
        if (RangeStatus != null)
        {
            RangeStatus.Text = "range value: " + e.NewValue.ToString("0.##", CultureInfo.InvariantCulture);
        }

        e.Handled = true;
    }

    private void OnSmokeFrameNavigating(object sender, NavigatingCancelEventArgs e)
    {
        SmokeFrameNavigatingCount++;
        LastSmokeFrameNavigatingUri = e.Uri?.ToString();
        LastSmokeFrameNavigationMode = e.NavigationMode.ToString();
    }

    private void OnSmokeFrameNavigated(object sender, NavigationEventArgs e)
    {
        SmokeFrameNavigatedCount++;
        LastSmokeFrameNavigatedUri = e.Uri?.ToString();
        LastSmokeFrameNavigatedContentType = e.Content?.GetType().FullName;
        if (e.Content is SmokePageFunction pageFunction)
        {
            pageFunction.Return -= OnSmokePageFunctionReturn;
            pageFunction.Return += OnSmokePageFunctionReturn;
        }
    }

    private void OnSmokeFrameLoadCompleted(object sender, NavigationEventArgs e)
    {
        SmokeFrameLoadCompletedCount++;
        LastSmokeFrameLoadCompletedUri = e.Uri?.ToString();
    }

    private void OnSmokePageFunctionReturn(object sender, ReturnEventArgs<string> e)
    {
        SmokePageFunctionReturnCount++;
        LastSmokePageFunctionResult = e.Result;
    }

    private void OnSmokeBubbled(object sender, RoutedEventArgs e)
    {
        SmokeRoutedEventCount++;
        LastSmokeRoutedEventSender = sender;
        LastSmokeRoutedEventSource = e.OriginalSource;
        RoutedEventStatus.Text = e.RoutedEvent.Name;
        e.Handled = true;
    }

    private void OnDocumentLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        DocumentLinkRequestNavigateCount++;
        LastDocumentLinkRequestNavigateUri = e.Uri?.ToString();
        LastDocumentLinkRequestNavigateRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnLoadedStoryboardTextLoaded(object sender, RoutedEventArgs e)
    {
        LoadedStoryboardTextLoadedCount++;
        LastLoadedStoryboardTextRoutedEventName = e.RoutedEvent?.Name;
    }
}

public sealed class SmokeViewModel : INotifyPropertyChanged
{
    private string _mutableStatus = "initial binding status";

    public SmokeViewModel()
    {
        Items = new ObservableCollection<SmokeItem>
        {
            new SmokeItem(
                "Window",
                "portable",
                "Framework",
                new SmokeItem("Startup", "managed", "Framework")),
            new SmokeItem("Scene", "ProGPU", "Rendering", false),
            new SmokeItem("XAML", "compiled", "Framework")
        };
    }

    public string Title { get; } = "ProGPU WPF SDK switch managed subsystem smoke";

    public string InputText { get; set; } = "editable package text";

    public string ValidationText { get; set; } = "valid package text";

    public SmokeRequeryCommand RequeryCommand { get; } = new();

    public string MutableStatus
    {
        get => _mutableStatus;
        set
        {
            if (_mutableStatus == value)
            {
                return;
            }

            _mutableStatus = value;
            OnPropertyChanged();
        }
    }

    public bool IsHighlighted { get; } = true;

    public bool IsCritical { get; } = true;

    public ObservableCollection<SmokeItem> Items { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SmokeRequeryCommand : ICommand
{
    public int CanExecuteProbeCount { get; private set; }

    public int ExecuteCount { get; private set; }

    public bool CanExecuteValue { get; set; }

    public object? LastParameter { get; private set; }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter)
    {
        CanExecuteProbeCount++;
        return CanExecuteValue;
    }

    public void Execute(object? parameter)
    {
        ExecuteCount++;
        LastParameter = parameter;
    }
}

public sealed class SmokeItem
{
    public SmokeItem(string name, string value, string category)
        : this(name, value, category, [])
    {
    }

    public SmokeItem(string name, string value, string category, params SmokeItem[] children)
        : this(name, value, category, true, children)
    {
    }

    public SmokeItem(string name, string value, string category, bool isActive, params SmokeItem[] children)
    {
        Name = name;
        Value = value;
        Category = category;
        IsActive = isActive;
        Children = new ObservableCollection<SmokeItem>(children);
    }

    public string Name { get; }

    public string Value { get; }

    public string Category { get; }

    public bool IsActive { get; set; }

    public ObservableCollection<SmokeItem> Children { get; }
}

public sealed class SmokeItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FrameworkTemplate { get; set; }

    public DataTemplate? RenderingTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item is SmokeItem { Category: "Rendering" }
            ? RenderingTemplate
            : FrameworkTemplate;
    }
}

public sealed class SmokeRoutedEventSource : FrameworkElement
{
    public static readonly RoutedEvent SmokeBubbledEvent = EventManager.RegisterRoutedEvent(
        "SmokeBubbled",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(SmokeRoutedEventSource));

    public event RoutedEventHandler SmokeBubbled
    {
        add => AddHandler(SmokeBubbledEvent, value);
        remove => RemoveHandler(SmokeBubbledEvent, value);
    }

    public void RaiseSmokeBubbled()
    {
        RaiseEvent(new RoutedEventArgs(SmokeBubbledEvent, this));
    }
}

public sealed class SmokeNonEmptyValidationRule : ValidationRule
{
    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        string text = value as string ?? value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ValidationResult(false, "Value is required");
        }

        return ValidationResult.ValidResult;
    }
}

public static class SmokeResourceFactory
{
    public static string CreateGreeting(string prefix, int value)
    {
        return $"{prefix}:{value}";
    }
}
