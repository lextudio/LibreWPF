using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Xceed.Wpf.AvalonDock;
using Xceed.Wpf.AvalonDock.Layout;
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
