using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xceed.Wpf.AvalonDock;
using Xceed.Wpf.AvalonDock.Layout;
using Xceed.Wpf.AvalonDock.Layout.Serialization;
using Xceed.Wpf.AvalonDock.Themes;
using Xceed.Wpf.Toolkit;
using Xceed.Wpf.Toolkit.PropertyGrid;

namespace ProGPU.Wpf.ToolkitApp;

public partial class MainWindow : Window
{
    private const string LiveValidationEnvironmentVariable = "PROGPU_WPF_TOOLKIT_LIVE_VALIDATE";
    private const int LiveValidationMaxAttempts = 400;
    private static readonly TimeSpan LiveValidationRetryDelay = TimeSpan.FromMilliseconds(16);
    private readonly ToolkitViewModel _viewModel = new();
    private bool _liveValidationStarted;

    public MainWindow()
    {
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += OnToolkitWindowLoaded;
        StartLiveValidationIfRequired();
    }

    private void AddDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        int index = _viewModel.DocumentCount + 1;
        var document = new ToolkitDocument(
            $"Generated {index}",
            "ProGPU",
            DateTime.Today.AddDays(index),
            $"Generated AvalonDock document {index}.");
        _viewModel.Documents.Add(document);
        _viewModel.SelectedDocument = document;
        _viewModel.Activity.Add($"Added document {index}");

        DocumentPane.Children.Add(
            new LayoutDocument
            {
                ContentId = $"generated-{index}",
                Title = document.Title,
                IconSource = TryFindResource("DocumentIcon") as ImageSource,
                Content = new TextBox
                {
                    Text = document.Body,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16)
                }
            });

        _viewModel.Status = $"Added {document.Title}";
    }

    private void ActivateEditorButton_Click(object sender, RoutedEventArgs e)
    {
        EditorDocument.IsSelected = true;
        EditorDocument.IsActive = true;
        _viewModel.Status = "Editor document activated";
        _viewModel.Activity.Add("Activated editor document");
    }

    private void TogglePropertyPaneButton_Click(object sender, RoutedEventArgs e)
    {
        if (PropertyPane.IsHidden)
        {
            PropertyPane.Show();
            _viewModel.Status = "Property pane shown";
            _viewModel.Activity.Add("Shown property pane");
        }
        else
        {
            PropertyPane.Hide(false);
            _viewModel.Status = "Property pane hidden";
            _viewModel.Activity.Add("Hidden property pane");
        }
    }

    private void ToggleActivityAutoHideButton_Click(object sender, RoutedEventArgs e)
    {
        ActivityPane.ToggleAutoHide();
        _viewModel.Status = ActivityPane.IsAutoHidden ? "Activity pane auto-hidden" : "Activity pane docked";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void ToggleAgendaAutoHideButton_Click(object sender, RoutedEventArgs e)
    {
        AgendaPane.ToggleAutoHide();
        _viewModel.Status = AgendaPane.IsAutoHidden ? "Agenda pane auto-hidden" : "Agenda pane docked";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void SerializeLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.LastSerializedLayout = SerializeCurrentLayout();
        _viewModel.Status = "AvalonDock layout serialized";
        _viewModel.Activity.Add("Serialized AvalonDock layout");
    }

    internal string SerializeCurrentLayout()
    {
        using var stream = new MemoryStream();
        var serializer = new XmlLayoutSerializer(DockManager);
        serializer.Serialize(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static DockingManager RoundTripLayout(string layoutXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutXml);

        var manager = new DockingManager();
        var serializer = new XmlLayoutSerializer(manager);
        serializer.LayoutSerializationCallback += (_, args) =>
        {
            args.Content ??= new TextBlock
            {
                Text = args.Model.ContentId,
                Margin = new Thickness(8)
            };
        };

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(layoutXml));
        serializer.Deserialize(stream);
        return manager;
    }

    internal ToolkitViewModel ViewModel => _viewModel;

    private void OnToolkitWindowLoaded(object sender, RoutedEventArgs e)
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
        Console.WriteLine("ProGPU WPF Toolkit live input validation started.");
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await ValidateRequiredLiveToolkitAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Environment.Exit(1);
                }
            });
    }

    private async Task ValidateRequiredLiveToolkitAsync()
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
            Console.WriteLine($"ProGPU WPF Toolkit live input validation succeeded: {geometryStatus}; {inputStatus}.");
            Environment.Exit(0);
            return;
        }

        Console.Error.WriteLine("Expected the Toolkit app to present a stable ProGPU frame before live input validation.");
        Environment.Exit(1);
    }

    private async Task<string> ValidateLiveInputAsync(object liveHost)
    {
        string lastTargetState = "not checked";
        bool focusedFilter = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            focusedFilter = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    if (!TryRaiseLiveMouseClick(liveHost, FilterTextBox, "FilterTextBox", out lastTargetState))
                    {
                        return false;
                    }

                    FilterTextBox.Text = string.Empty;
                    FilterTextBox.CaretIndex = 0;
                    FilterTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    return true;
                },
                DispatcherPriority.Send);
            if (focusedFilter)
            {
                break;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!focusedFilter)
        {
            throw new InvalidOperationException(
                $"Expected Toolkit live filter TextBox to become visible and hit-testable before injecting input, but last state was: {lastTargetState}.");
        }

        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                if (!FilterTextBox.IsKeyboardFocusWithin)
                {
                    throw new InvalidOperationException(
                        $"Expected Toolkit live host click to focus FilterTextBox, but focused '{DescribeInputElement(Keyboard.FocusedElement)}'. {lastTargetState}.");
                }

                foreach (char character in "Dock")
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
                AssertEqual("Dock", FilterTextBox.Text, "Toolkit live WatermarkTextBox text");
                AssertEqual("Dock", ViewModel.FilterText, "Toolkit live FilterText binding source");
            },
            DispatcherPriority.Send);

        int documentsBeforeAdd = ViewModel.DocumentCount;
        await ClickLiveControlAsync(liveHost, AddDocumentButton, "AddDocumentButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(documentsBeforeAdd + 1, ViewModel.DocumentCount, "Toolkit live added document count");
                AssertEqual($"Added Generated {documentsBeforeAdd + 1}", ViewModel.Status, "Toolkit live Add document status");
                AssertEqual(documentsBeforeAdd + 1, DocumentPane.ChildrenCount, "Toolkit live AvalonDock document pane count");
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, ActivateEditorButton, "ActivateEditorButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(true, EditorDocument.IsSelected, "Toolkit live editor document selected state");
                AssertEqual(true, EditorDocument.IsActive, "Toolkit live editor document active state");
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, TogglePropertyPaneButton, "TogglePropertyPaneButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(true, PropertyPane.IsHidden, "Toolkit live property pane hidden state");
                if (!DockLayoutRoot.Hidden.Contains(PropertyPane))
                {
                    throw new InvalidOperationException("Expected Toolkit live property pane to be in AvalonDock hidden collection.");
                }
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, TogglePropertyPaneButton, "TogglePropertyPaneButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => AssertEqual(false, PropertyPane.IsHidden, "Toolkit live property pane restored state"),
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, ToggleActivityAutoHideButton, "ToggleActivityAutoHideButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(true, ActivityPane.IsAutoHidden, "Toolkit live activity pane auto-hide state");
                if (DockLayoutRoot.RightSide.ChildrenCount == 0)
                {
                    throw new InvalidOperationException("Expected Toolkit live activity pane to move into the AvalonDock right auto-hide side.");
                }
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, ToggleAgendaAutoHideButton, "ToggleAgendaAutoHideButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(false, AgendaPane.IsAutoHidden, "Toolkit live agenda pane docked state");
                AssertEqual(true, AgendaPane.IsVisible, "Toolkit live agenda pane visible state");
                if (AgendaPane.Parent is LayoutAnchorGroup)
                {
                    throw new InvalidOperationException("Expected Toolkit live agenda pane to leave the AvalonDock left auto-hide group.");
                }
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, SerializeLayoutButton, "SerializeLayoutButton");
        return await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                if (!ViewModel.LastSerializedLayout.Contains("<LayoutRoot", StringComparison.Ordinal) ||
                    !ViewModel.LastSerializedLayout.Contains("ContentId=\"editor\"", StringComparison.Ordinal) ||
                    !ViewModel.LastSerializedLayout.Contains("ContentId=\"activity\"", StringComparison.Ordinal) ||
                    !ViewModel.LastSerializedLayout.Contains("ContentId=\"agenda\"", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Expected Toolkit live AvalonDock serialization to include document content ids.");
                }

                var roundTripped = RoundTripLayout(ViewModel.LastSerializedLayout);
                if (roundTripped.Layout.RootPanel is null ||
                    roundTripped.Layout.RootPanel.ChildrenCount != DockLayoutRoot.RootPanel.ChildrenCount)
                {
                    throw new InvalidOperationException("Expected Toolkit live AvalonDock deserialization to restore root panel shape.");
                }

                return "host mouse/text input, binding update, AvalonDock document activation, anchorable hide/show, auto-hide side groups, and layout serialization updated";
            },
            DispatcherPriority.Send);
    }

    private async Task ClickLiveControlAsync(object liveHost, FrameworkElement target, string targetName)
    {
        string lastTargetState = "not checked";
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            bool sentClick = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () => TryRaiseLiveMouseClick(liveHost, target, targetName, out lastTargetState),
                DispatcherPriority.Send);
            if (sentClick)
            {
                await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
                return;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        throw new InvalidOperationException(
            $"Expected Toolkit live target {targetName} to become visible and hit-testable, but last state was: {lastTargetState}.");
    }

    private bool TryRaiseLiveMouseClick(object liveHost, FrameworkElement target, string targetName, out string targetState)
    {
        targetState =
            $"{targetName}.IsVisible={target.IsVisible}, " +
            $"{targetName}.ActualSize={target.ActualWidth:0.###}x{target.ActualHeight:0.###}, " +
            $"{targetName}.IsEnabled={target.IsEnabled}, " +
            $"{targetName}.Focusable={target.Focusable}, " +
            $"{targetName}.IsHitTestVisible={target.IsHitTestVisible}";
        if (!target.IsVisible ||
            target.ActualWidth <= 1.0 ||
            target.ActualHeight <= 1.0 ||
            !target.IsEnabled ||
            !target.IsHitTestVisible)
        {
            return false;
        }

        Point center = target.TranslatePoint(
            new Point(Math.Max(1.0, target.ActualWidth) / 2.0, Math.Max(1.0, target.ActualHeight) / 2.0),
            this);
        object? hit = InputHitTest(center);
        targetState += $", Input=({center.X:0.###}, {center.Y:0.###}), InputHitTest={DescribeInputElement(hit)}";
        if (hit == null)
        {
            return false;
        }

        RaiseHostInput(liveHost, "MouseMove", x: center.X, y: center.Y);
        RaiseHostInput(liveHost, "MouseDown", x: center.X, y: center.Y, button: "Left");
        RaiseHostInput(liveHost, "MouseUp", x: center.X, y: center.Y, button: "Left");
        return true;
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

        AssertEqual(980u, logicalWidth, "Toolkit live ProGPU WPF logical width");
        AssertEqual(640u, logicalHeight, "Toolkit live ProGPU WPF logical height");
        if (pixelWidth < logicalWidth || pixelHeight < logicalHeight)
        {
            throw new InvalidOperationException(
                $"Expected Toolkit live ProGPU WPF pixels to cover logical content, but got logical {logicalWidth}x{logicalHeight} and pixels {pixelWidth}x{pixelHeight}.");
        }

        if (viewportX != 0 || viewportY != 0 || viewportWidth != pixelWidth || viewportHeight != pixelHeight)
        {
            throw new InvalidOperationException(
                $"Expected Toolkit live ProGPU WPF viewport to use the full physical target, but got viewport {viewportWidth}x{viewportHeight}@{viewportX},{viewportY} for pixels {pixelWidth}x{pixelHeight}.");
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

internal sealed class ToolkitViewModel : INotifyPropertyChanged
{
    private ToolkitDocument _selectedDocument;
    private int _priority = 4;
    private string _filterText = string.Empty;
    private DateTime? _dueDate = DateTime.Today.AddDays(7).AddHours(9);
    private bool _isBusy;
    private string _status = "Toolkit sample ready";
    private string _lastSerializedLayout = string.Empty;

    public ToolkitViewModel()
    {
        Documents =
        [
            new("Overview", "WPF", DateTime.Today, "No-source-change SDK app consuming Extended WPF Toolkit."),
            new("AvalonDock", "Xceed", DateTime.Today.AddDays(-1), "DockingManager layout with documents and anchorables.")
        ];
        Categories = ["Framework", "Toolkit", "AvalonDock", "Rendering"];
        SelectedCategories = ["Toolkit", "AvalonDock"];
        Activity = ["Toolkit package loaded", "AvalonDock layout loaded"];
        _selectedDocument = Documents[0];
        Documents.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DocumentCount));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ToolkitDocument> Documents { get; }

    public ObservableCollection<string> Categories { get; }

    public ObservableCollection<string> SelectedCategories { get; }

    public ObservableCollection<string> Activity { get; }

    public ToolkitDocument SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (!ReferenceEquals(_selectedDocument, value))
            {
                _selectedDocument = value;
                OnPropertyChanged();
            }
        }
    }

    public int DocumentCount => Documents.Count;

    public int Priority
    {
        get => _priority;
        set
        {
            if (_priority != value)
            {
                _priority = value;
                OnPropertyChanged();
            }
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText != value)
            {
                _filterText = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            if (_dueDate != value)
            {
                _dueDate = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (!string.Equals(_status, value, StringComparison.Ordinal))
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastSerializedLayout
    {
        get => _lastSerializedLayout;
        set
        {
            if (!string.Equals(_lastSerializedLayout, value, StringComparison.Ordinal))
            {
                _lastSerializedLayout = value;
                OnPropertyChanged();
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class ToolkitDocument : INotifyPropertyChanged
{
    private string _body;

    public ToolkitDocument(string title, string owner, DateTime modified, string body)
    {
        Title = title;
        Owner = owner;
        Modified = modified;
        _body = body;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public string Owner { get; }

    public DateTime Modified { get; }

    public string Body
    {
        get => _body;
        set
        {
            if (!string.Equals(_body, value, StringComparison.Ordinal))
            {
                _body = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Body)));
            }
        }
    }
}

internal static class ToolkitSelfTest
{
    public static void Validate(MainWindow window, bool expectLoaded = false)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Dispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));

        Require<DockingManager>(window, "DockManager");
        Require<IntegerUpDown>(window, "PriorityEditor");
        Require<WatermarkTextBox>(window, "FilterTextBox");
        Require<DateTimePicker>(window, "DueDatePicker");
        Require<CheckComboBox>(window, "CategoryPicker");
        Require<BusyIndicator>(window, "BusyIndicator");
        Require<PropertyGrid>(window, "DocumentPropertyGrid");
        Require<Button>(window, "ActivateEditorButton");
        Require<Button>(window, "TogglePropertyPaneButton");
        Require<Button>(window, "ToggleActivityAutoHideButton");
        Require<Button>(window, "ToggleAgendaAutoHideButton");
        Require<Button>(window, "SerializeLayoutButton");

        if (window.DockManager.Theme is not AeroTheme)
        {
            throw new InvalidOperationException("Expected AvalonDock AeroTheme from Extended.Wpf.Toolkit package.");
        }

        if (window.DockManager.DocumentHeaderTemplate is null)
        {
            throw new InvalidOperationException("Expected AvalonDock document header template to be loaded from sample XAML.");
        }

        if (window.DockLayoutRoot.RootPanel is null || window.DockLayoutRoot.RootPanel.ChildrenCount != 3)
        {
            throw new InvalidOperationException("Expected AvalonDock root panel with toolkit, document, and property panes.");
        }

        if (!window.AgendaPane.IsAutoHidden ||
            !window.ContactsPane.IsAutoHidden ||
            window.DockLayoutRoot.LeftSide.ChildrenCount != 1)
        {
            throw new InvalidOperationException("Expected startup AvalonDock side anchorables to be auto-hidden on the left side.");
        }

        if (window.DocumentPane.ChildrenCount != 2)
        {
            throw new InvalidOperationException($"Expected two startup AvalonDock documents, got {window.DocumentPane.ChildrenCount}.");
        }

        if (window.OverviewDocument.IconSource is null ||
            window.EditorDocument.IconSource is null ||
            window.ToolkitPane.IconSource is null)
        {
            throw new InvalidOperationException("Expected AvalonDock icon resources to bind into documents and anchorables.");
        }

        if (window.DocumentPropertyGrid.SelectedObject is ToolkitDocument selected)
        {
            if (!string.Equals(selected.Title, "Overview", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected toolkit PropertyGrid to bind to the selected document.");
            }
        }
        else if (expectLoaded)
        {
            throw new InvalidOperationException("Expected loaded toolkit PropertyGrid to bind to the selected document.");
        }
        else if (BindingOperations.GetBindingExpression(window.DocumentPropertyGrid, PropertyGrid.SelectedObjectProperty) is null)
        {
            throw new InvalidOperationException("Expected toolkit PropertyGrid SelectedObject binding expression.");
        }

        if (window.ViewModel.Documents.Count != 2 ||
            window.ViewModel.Categories.Count != 4 ||
            window.ViewModel.SelectedCategories.Count != 2)
        {
            throw new InvalidOperationException("Expected toolkit sample view-model collections to be initialized.");
        }

        if (window.PriorityEditor.Value != window.ViewModel.Priority)
        {
            throw new InvalidOperationException("Expected IntegerUpDown value binding to initialize.");
        }

        window.AddDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (window.DocumentPane.ChildrenCount != 3 || window.ViewModel.Documents.Count != 3)
        {
            throw new InvalidOperationException("Expected AvalonDock document insertion to update model and layout.");
        }

        if (!string.Equals(window.ViewModel.Status, "Added Generated 3", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Add document command to update sample status.");
        }

        window.ActivateEditorButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (!window.EditorDocument.IsSelected || !window.EditorDocument.IsActive)
        {
            throw new InvalidOperationException("Expected AvalonDock document activation to update selected/active document state.");
        }

        window.TogglePropertyPaneButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (!window.PropertyPane.IsHidden || !window.DockLayoutRoot.Hidden.Contains(window.PropertyPane))
        {
            throw new InvalidOperationException("Expected AvalonDock property anchorable to hide into the layout hidden collection.");
        }

        window.TogglePropertyPaneButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (window.PropertyPane.IsHidden)
        {
            throw new InvalidOperationException("Expected AvalonDock property anchorable to show from the hidden collection.");
        }

        window.ToggleActivityAutoHideButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (!window.ActivityPane.IsAutoHidden || window.DockLayoutRoot.RightSide.ChildrenCount == 0)
        {
            throw new InvalidOperationException("Expected AvalonDock activity anchorable to auto-hide into the right side.");
        }

        window.ToggleAgendaAutoHideButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (window.AgendaPane.IsAutoHidden || window.AgendaPane.Parent is LayoutAnchorGroup)
        {
            throw new InvalidOperationException("Expected AvalonDock agenda anchorable to dock back from the left auto-hide side.");
        }

        window.SerializeLayoutButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (!window.ViewModel.LastSerializedLayout.Contains("<LayoutRoot", StringComparison.Ordinal) ||
            !window.ViewModel.LastSerializedLayout.Contains("ContentId=\"editor\"", StringComparison.Ordinal) ||
            !window.ViewModel.LastSerializedLayout.Contains("ContentId=\"activity\"", StringComparison.Ordinal) ||
            !window.ViewModel.LastSerializedLayout.Contains("ContentId=\"agenda\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected AvalonDock layout serialization to include document content ids.");
        }

        var roundTripped = MainWindow.RoundTripLayout(window.ViewModel.LastSerializedLayout);
        if (roundTripped.Layout.RootPanel is null ||
            roundTripped.Layout.RootPanel.ChildrenCount != window.DockLayoutRoot.RootPanel.ChildrenCount)
        {
            throw new InvalidOperationException("Expected AvalonDock layout deserialization to restore the root panel shape.");
        }

        if (expectLoaded && !window.IsLoaded)
        {
            throw new InvalidOperationException("Expected Toolkit app window to be loaded during Application.Run validation.");
        }
    }

    private static T Require<T>(FrameworkElement root, string name)
        where T : class
    {
        return root.FindName(name) as T
            ?? throw new InvalidOperationException($"Expected {typeof(T).FullName} named {name}.");
    }
}
