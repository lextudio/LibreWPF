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
using System.Windows.Threading;

namespace ProGPU.Wpf.HelloApp;

public partial class MainWindow : Window
{
    private const string LiveValidationEnvironmentVariable = "PROGPU_WPF_HELLO_LIVE_VALIDATE";
    private const int LiveValidationMaxAttempts = 400;
    private static readonly TimeSpan LiveValidationRetryDelay = TimeSpan.FromMilliseconds(16);
    private bool _liveValidationStarted;

    internal HelloViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnHelloWindowLoaded;
        StartLiveValidationIfRequired();
    }

    private void OnUpdateButtonClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Status = "Updated for " + ViewModel.Name;
        ViewModel.Items.Add("Clicked at " + DateTimeOffset.Now.ToString("HH:mm:ss"));
    }

    private void OnHelloWindowLoaded(object sender, RoutedEventArgs e)
    {
        StartLiveValidationIfRequired();
    }

    private void StartLiveValidationIfRequired()
    {
        if (_liveValidationStarted ||
            Environment.GetEnvironmentVariable(LiveValidationEnvironmentVariable) != "1")
        {
            return;
        }

        _liveValidationStarted = true;
        Console.WriteLine("ProGPU WPF HelloApp live input validation started.");
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await ValidateRequiredLiveHelloAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Environment.Exit(1);
                }
            });
    }

    private async Task ValidateRequiredLiveHelloAsync()
    {
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            await Task.Delay(LiveValidationRetryDelay);
            if (!TryGetPortableActivationHost(out var liveHost) || liveHost == null)
            {
                continue;
            }

            if (GetRequiredProperty(liveHost, "HasPresentedFrame") is not bool hasPresentedFrame ||
                !hasPresentedFrame)
            {
                WakeLiveRenderHost(liveHost);
                continue;
            }

            string geometryStatus = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () => ValidateLiveRenderSurfaceGeometryCore(liveHost),
                DispatcherPriority.Send);
            string inputStatus = await ValidateLiveInputAsync(liveHost);
            Console.WriteLine($"ProGPU WPF HelloApp live input validation succeeded: {geometryStatus}; {inputStatus}.");
            Environment.Exit(0);
            return;
        }

        Console.Error.WriteLine("Expected the Hello app to present a stable ProGPU frame before live input validation.");
        Environment.Exit(1);
    }

    private async Task<string> ValidateLiveInputAsync(object liveHost)
    {
        TextBox? nameBox = null;
        Button? updateButton = null;
        Point inputPoint = new();
        string lastTargetState = "not checked";

        bool sentPointerInput = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            sentPointerInput = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    nameBox = Require<TextBox>("NameBox");
                    updateButton = Require<Button>("UpdateButton");
                    lastTargetState =
                        $"TextBox.IsVisible={nameBox.IsVisible}, " +
                        $"TextBox.ActualSize={nameBox.ActualWidth:0.###}x{nameBox.ActualHeight:0.###}, " +
                        $"TextBox.IsEnabled={nameBox.IsEnabled}, " +
                        $"TextBox.Focusable={nameBox.Focusable}, " +
                        $"TextBox.IsHitTestVisible={nameBox.IsHitTestVisible}";
                    if (!nameBox.IsVisible ||
                        nameBox.ActualWidth <= 1.0 ||
                        nameBox.ActualHeight <= 1.0 ||
                        !nameBox.IsEnabled ||
                        !nameBox.Focusable ||
                        !nameBox.IsHitTestVisible)
                    {
                        return false;
                    }

                    Point center = nameBox.TranslatePoint(
                        new Point(Math.Max(1.0, nameBox.ActualWidth) / 2.0, Math.Max(1.0, nameBox.ActualHeight) / 2.0),
                        this);
                    object? hit = InputHitTest(center);
                    lastTargetState += $", Input=({center.X:0.###}, {center.Y:0.###}), InputHitTest={DescribeInputElement(hit)}";
                    if (hit == null)
                    {
                        return false;
                    }

                    nameBox.Text = string.Empty;
                    nameBox.CaretIndex = 0;
                    nameBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    inputPoint = center;
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

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!sentPointerInput)
        {
            throw new InvalidOperationException(
                $"Expected Hello live input target to become visible and hit-testable before injecting input, but last state was: {lastTargetState}.");
        }

        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                if (!ReferenceEquals(Keyboard.FocusedElement, nameBox))
                {
                    throw new InvalidOperationException(
                        $"Expected Hello live host click to focus NameBox, but focused '{DescribeInputElement(Keyboard.FocusedElement)}'. " +
                        $"Input=({inputPoint.X:0.###}, {inputPoint.Y:0.###}), {lastTargetState}.");
                }

                foreach (char character in "Live")
                {
                    string key = char.ToUpperInvariant(character).ToString();
                    RaiseHostInput(liveHost, "KeyDown", key: key);
                    RaiseHostInput(liveHost, "TextInput", character: character);
                    RaiseHostInput(liveHost, "KeyUp", key: key);
                }
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual("Live", Require<TextBox>("NameBox").Text, "Hello live TextBox text after host input");
                AssertEqual("Live", ViewModel.Name, "Hello live view-model source after host input");

                Button button = Require<Button>("UpdateButton");
                Point center = button.TranslatePoint(
                    new Point(Math.Max(1.0, button.ActualWidth) / 2.0, Math.Max(1.0, button.ActualHeight) / 2.0),
                    this);
                RaiseHostInput(liveHost, "MouseMove", x: center.X, y: center.Y);
                RaiseHostInput(liveHost, "MouseDown", x: center.X, y: center.Y, button: "Left");
                RaiseHostInput(liveHost, "MouseUp", x: center.X, y: center.Y, button: "Left");
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        return await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual("Updated for Live", ViewModel.Status, "Hello live button status");
                AssertEqual("Updated for Live", Require<TextBlock>("SubtitleText").Text, "Hello live bound status text");
                AssertEqual("Ready for Live", Require<TextBlock>("FooterText").Text, "Hello live footer text");
                AssertEqual(4, Require<ListBox>("ItemsList").Items.Count, "Hello live item count");
                return "input TextBox focus, text binding, and button click updated";
            },
            DispatcherPriority.Send);
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

    private static string ValidateLiveRenderSurfaceGeometryCore(object liveHost)
    {
        object geometry = InvokeRequired(liveHost, "ResolveCurrentRenderSurfaceGeometry");
        var logicalWidth = Convert.ToUInt32(GetRequiredProperty(geometry, "LogicalWidth"), CultureInfo.InvariantCulture);
        var logicalHeight = Convert.ToUInt32(GetRequiredProperty(geometry, "LogicalHeight"), CultureInfo.InvariantCulture);
        var pixelWidth = Convert.ToUInt32(GetRequiredProperty(geometry, "PixelWidth"), CultureInfo.InvariantCulture);
        var pixelHeight = Convert.ToUInt32(GetRequiredProperty(geometry, "PixelHeight"), CultureInfo.InvariantCulture);
        var dpiScale = Convert.ToDouble(GetRequiredProperty(geometry, "DpiScale"), CultureInfo.InvariantCulture);
        var viewportX = Convert.ToUInt32(GetRequiredProperty(geometry, "ViewportX"), CultureInfo.InvariantCulture);
        var viewportY = Convert.ToUInt32(GetRequiredProperty(geometry, "ViewportY"), CultureInfo.InvariantCulture);
        var viewportWidth = Convert.ToUInt32(GetRequiredProperty(geometry, "ViewportWidth"), CultureInfo.InvariantCulture);
        var viewportHeight = Convert.ToUInt32(GetRequiredProperty(geometry, "ViewportHeight"), CultureInfo.InvariantCulture);

        AssertEqual(520u, logicalWidth, "Hello live ProGPU WPF logical width");
        AssertEqual(360u, logicalHeight, "Hello live ProGPU WPF logical height");
        if (pixelWidth < logicalWidth || pixelHeight < logicalHeight)
        {
            throw new InvalidOperationException(
                $"Expected Hello live ProGPU WPF pixels to cover logical content, but got logical {logicalWidth}x{logicalHeight} and pixels {pixelWidth}x{pixelHeight}.");
        }

        if (viewportX != 0 || viewportY != 0 || viewportWidth != pixelWidth || viewportHeight != pixelHeight)
        {
            throw new InvalidOperationException(
                $"Expected Hello live ProGPU WPF viewport to use the full physical target, but got viewport {viewportWidth}x{viewportHeight}@{viewportX},{viewportY} for pixels {pixelWidth}x{pixelHeight}.");
        }

        return $"logical {logicalWidth}x{logicalHeight}, pixels {pixelWidth}x{pixelHeight}, viewport {viewportWidth}x{viewportHeight}@{viewportX},{viewportY}, dpi {dpiScale:0.###}";
    }

    private static void RaiseHostInput(
        object liveHost,
        string kind,
        string? key = null,
        char? character = null,
        double x = 0.0,
        double y = 0.0,
        string button = "None")
    {
        object input = CreateWpfInputEventArgs(liveHost, kind, key, character, x, y, button);
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
        string button)
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
            Enum.Parse(modifiersType, "None"))
            ?? throw new InvalidOperationException("Expected WpfInputEventArgs construction to succeed.");
    }

    private static object InvokeRequired(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        return method.Invoke(target, null)
            ?? throw new InvalidOperationException($"Expected {methodName} to return a value.");
    }

    private static object GetRequiredProperty(object target, string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(target.GetType().FullName, propertyName);
        return property.GetValue(target)
            ?? throw new InvalidOperationException($"Expected {propertyName} to have a value.");
    }

    private T Require<T>(string name)
        where T : class
    {
        return FindName(name) as T
            ?? throw new InvalidOperationException($"Expected {name} to be a {typeof(T).Name}.");
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
}

internal sealed class HelloViewModel : INotifyPropertyChanged
{
    private string _name = "WPF";
    private string _status = "Running through ProGPU.Wpf.Sdk";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                Footer = "Ready for " + value;
                OnPropertyChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    public string Footer
    {
        get => _footer;
        private set
        {
            if (_footer != value)
            {
                _footer = value;
                OnPropertyChanged();
            }
        }
    }

    private string _footer = "Ready for WPF";

    public ObservableCollection<string> Items { get; } =
    [
        "Compiled App.xaml",
        "Compiled MainWindow.xaml",
        "Binding and collection view"
    ];

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal static class HelloSelfTest
{
    public static void Validate(MainWindow window, bool expectStartupActivation)
    {
        AssertEqual("ProGPU WPF Hello", window.Title, "window title");
        AssertEqual(true, window.IsVisible, "window visibility");
        AssertEqual(true, ReferenceEquals(window.DataContext, window.ViewModel), "data context");
        AssertEqual("ProGPU WPF Hello", Require<TextBlock>(window, "TitleText").Text, "title text");
        AssertEqual("Running through ProGPU.Wpf.Sdk", Require<TextBlock>(window, "SubtitleText").Text, "bound status text");
        AssertEqual("Ready for WPF", Require<TextBlock>(window, "FooterText").Text, "bound footer text");
        AssertEqual(3, Require<ListBox>(window, "ItemsList").Items.Count, "initial item count");
        AssertEqual("WPF", Require<TextBox>(window, "NameBox").Text, "initial text binding");

        Require<TextBox>(window, "NameBox").Text = "ProGPU";
        Require<TextBox>(window, "NameBox").GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Require<Button>(window, "UpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        AssertEqual("ProGPU", window.ViewModel.Name, "updated view model name");
        AssertEqual("Updated for ProGPU", window.ViewModel.Status, "button command status");
        AssertEqual("Updated for ProGPU", Require<TextBlock>(window, "SubtitleText").Text, "updated bound status text");
        AssertEqual("Ready for ProGPU", Require<TextBlock>(window, "FooterText").Text, "updated footer text");
        AssertEqual(4, Require<ListBox>(window, "ItemsList").Items.Count, "updated item count");

        if (expectStartupActivation)
        {
            AssertEqual(1, App.StartupEventCount, "startup event count");
            AssertEqual(2, App.StartupArgumentCount, "startup argument count");
            AssertEqual(2, Application.Current.Properties["HelloStartupArgumentCount"], "startup argument count property");
            AssertEqual("hello-alpha|hello beta", Application.Current.Properties["HelloStartupArguments"], "startup arguments property");
        }
    }

    private static T Require<T>(FrameworkElement root, string name)
        where T : class
    {
        return root.FindName(name) as T
            ?? throw new InvalidOperationException($"Expected {name} to be a {typeof(T).Name}.");
    }

    private static void AssertEqual<T>(T expected, T actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expected}' but was '{actual}'.");
        }
    }
}
