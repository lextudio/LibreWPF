using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    private readonly ToolkitViewModel _viewModel = new();

    public MainWindow()
    {
        DataContext = _viewModel;
        InitializeComponent();
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
        Require<Button>(window, "SerializeLayoutButton");

        if (window.DockManager.Theme is not AeroTheme)
        {
            throw new InvalidOperationException("Expected AvalonDock AeroTheme from Extended.Wpf.Toolkit package.");
        }

        if (window.DockLayoutRoot.RootPanel is null || window.DockLayoutRoot.RootPanel.ChildrenCount != 3)
        {
            throw new InvalidOperationException("Expected AvalonDock root panel with toolkit, document, and property panes.");
        }

        if (window.DocumentPane.ChildrenCount != 2)
        {
            throw new InvalidOperationException($"Expected two startup AvalonDock documents, got {window.DocumentPane.ChildrenCount}.");
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

        window.SerializeLayoutButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (!window.ViewModel.LastSerializedLayout.Contains("<LayoutRoot", StringComparison.Ordinal) ||
            !window.ViewModel.LastSerializedLayout.Contains("ContentId=\"editor\"", StringComparison.Ordinal))
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
