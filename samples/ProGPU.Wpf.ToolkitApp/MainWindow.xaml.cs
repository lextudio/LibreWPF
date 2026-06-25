using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Xceed.Wpf.AvalonDock;
using Xceed.Wpf.AvalonDock.Layout;
using Xceed.Wpf.AvalonDock.Layout.Serialization;
using Xceed.Wpf.AvalonDock.Themes;
using Xceed.Wpf.Toolkit;
using Xceed.Wpf.Toolkit.Core;
using Xceed.Wpf.Toolkit.PropertyGrid;
using ToolkitRichTextBox = Xceed.Wpf.Toolkit.RichTextBox;

namespace ProGPU.Wpf.ToolkitApp;

public partial class MainWindow : Window
{
    private const string LiveValidationEnvironmentVariable = "PROGPU_WPF_TOOLKIT_LIVE_VALIDATE";
    private const int LiveValidationMaxAttempts = 400;
    private static readonly TimeSpan LiveValidationRetryDelay = TimeSpan.FromMilliseconds(16);
    private static readonly string[] AvalonDockThemeNames = ["Aero", "Metro", "VS2010"];
    private readonly ToolkitViewModel _viewModel = new();
    private int _avalonDockThemeIndex;
    private bool _liveValidationStarted;

    public MainWindow()
    {
        DataContext = _viewModel;
        InitializeComponent();
        SetAvalonDockTheme(AvalonDockThemeNames[_avalonDockThemeIndex], recordSwitch: false);
        DockManager.ActiveContentChanged += DockManager_ActiveContentChanged;
        DockManager.DocumentClosing += DockManager_DocumentClosing;
        DockManager.DocumentClosed += DockManager_DocumentClosed;
        DockManager.Floated += DockManager_Floated;
        DockManager.Docked += DockManager_Docked;
        DockManager.LayoutChanging += DockManager_LayoutChanging;
        DockManager.LayoutChanged += DockManager_LayoutChanged;
        SourceDockManager.ActiveContentChanged += SourceDockManager_ActiveContentChanged;
        OverviewDocument.Closed += OverviewDocument_Closed;
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

    private void AddSourceDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        var document = _viewModel.AddSourceDocument();
        SourceDockManager.ActiveContent = document;
        _viewModel.Status = $"Added source {document.Title}";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void ActivateSourceToolButton_Click(object sender, RoutedEventArgs e)
    {
        var tool = _viewModel.SourceAnchorables.First();
        SourceDockManager.ActiveContent = tool;
        _viewModel.Status = $"Activated source {tool.Title}";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void ActivateEditorButton_Click(object sender, RoutedEventArgs e)
    {
        EditorDocument.IsSelected = true;
        EditorDocument.IsActive = true;
        _viewModel.Status = "Editor document activated";
        _viewModel.Activity.Add("Activated editor document");
    }

    private void CloseOverviewDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DocumentPane.Children.Contains(OverviewDocument))
        {
            _viewModel.Status = "Overview document already closed";
            _viewModel.Activity.Add(_viewModel.Status);
            return;
        }

        OverviewDocument.Close();
        if (DocumentPane.Children.Contains(OverviewDocument))
        {
            return;
        }

        _viewModel.Status = "Overview document closed";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void ReopenOverviewDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DocumentPane.Children.Contains(OverviewDocument))
        {
            DocumentPane.Children.Insert(0, OverviewDocument);
        }

        OverviewDocument.IsSelected = true;
        OverviewDocument.IsActive = true;
        _viewModel.Status = "Overview document reopened";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void DockManager_ActiveContentChanged(object? sender, EventArgs e)
    {
        _viewModel.AvalonDockActiveContentChangedCount++;
        _viewModel.LastActiveContentTitle = DockLayoutRoot.LastFocusedDocument?.Title ??
            Convert.ToString(DockManager.ActiveContent, CultureInfo.InvariantCulture) ??
            string.Empty;
    }

    private void DockManager_DocumentClosing(object? sender, DocumentClosingEventArgs e)
    {
        _viewModel.AvalonDockDocumentClosingCount++;
        _viewModel.LastClosingDocumentContentId = e.Document?.ContentId ?? string.Empty;

        if (ReferenceEquals(e.Document, OverviewDocument) &&
            _viewModel.CancelNextOverviewClose)
        {
            e.Cancel = true;
            _viewModel.CancelNextOverviewClose = false;
            _viewModel.AvalonDockDocumentCloseCanceledCount++;
            _viewModel.Status = "Overview document close canceled";
            _viewModel.Activity.Add(_viewModel.Status);
        }
    }

    private void DockManager_DocumentClosed(object? sender, DocumentClosedEventArgs e)
    {
        _viewModel.AvalonDockDocumentClosedCount++;
        _viewModel.LastClosedDocumentContentId = e.Document?.ContentId ?? string.Empty;
    }

    private void DockManager_Floated(object? sender, EventArgs e)
    {
        _viewModel.AvalonDockFloatedCount++;
    }

    private void DockManager_Docked(object? sender, EventArgs e)
    {
        _viewModel.AvalonDockDockedCount++;
    }

    private void DockManager_LayoutChanging(object? sender, EventArgs e)
    {
        _viewModel.AvalonDockLayoutChangingCount++;
    }

    private void DockManager_LayoutChanged(object? sender, EventArgs e)
    {
        _viewModel.AvalonDockLayoutChangedCount++;
    }

    private void SourceDockManager_ActiveContentChanged(object? sender, EventArgs e)
    {
        _viewModel.SourceActiveContentChangedCount++;
        _viewModel.LastSourceActiveTitle =
            (_viewModel.SourceActiveContent as ToolkitDockItem)?.Title ??
            (SourceDockManager.ActiveContent as ToolkitDockItem)?.Title ??
            string.Empty;
    }

    private void OverviewDocument_Closed(object? sender, EventArgs e)
    {
        _viewModel.OverviewDocumentClosedCount++;
    }

    private void ToggleEditorFloatButton_Click(object sender, RoutedEventArgs e)
    {
        if (EditorDocument.IsFloating)
        {
            EditorDocument.DockAsDocument();
            ToggleEditorFloatButton.Content = "Float editor";
            _viewModel.Status = "Editor document docked";
            _viewModel.Activity.Add("Docked editor document");
        }
        else
        {
            EditorDocument.Float();
            ToggleEditorFloatButton.Content = "Dock editor";
            _viewModel.Status = "Editor document floated";
            _viewModel.Activity.Add("Floated editor document");
        }
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

    private void CycleDockThemeButton_Click(object sender, RoutedEventArgs e)
    {
        CycleAvalonDockTheme();
    }

    internal void CycleAvalonDockTheme()
    {
        int nextIndex = (_avalonDockThemeIndex + 1) % AvalonDockThemeNames.Length;
        SetAvalonDockTheme(AvalonDockThemeNames[nextIndex], recordSwitch: true);
    }

    private void SetAvalonDockTheme(string themeName, bool recordSwitch)
    {
        int nextIndex = Array.IndexOf(AvalonDockThemeNames, themeName);
        if (nextIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(themeName), themeName, "Unknown AvalonDock theme.");
        }

        _avalonDockThemeIndex = nextIndex;
        DockManager.Theme = CreateAvalonDockTheme(themeName);
        SourceDockManager.Theme = CreateAvalonDockTheme(themeName);
        _viewModel.ActiveDockThemeName = themeName;

        if (recordSwitch)
        {
            _viewModel.DockThemeSwitchCount++;
            _viewModel.Status = $"AvalonDock theme switched to {themeName}";
            _viewModel.Activity.Add(_viewModel.Status);
        }
    }

    private static Theme CreateAvalonDockTheme(string themeName)
    {
        return themeName switch
        {
            "Aero" => new AeroTheme(),
            "Metro" => new MetroTheme(),
            "VS2010" => new VS2010Theme(),
            _ => throw new ArgumentOutOfRangeException(nameof(themeName), themeName, "Unknown AvalonDock theme.")
        };
    }

    private void MarkReviewedButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedDocument.Body += Environment.NewLine + "Reviewed through Xceed DropDownButton.";
        _viewModel.Status = "Document marked reviewed";
        _viewModel.Activity.Add("Marked selected document reviewed");
        ActionDropDownButton.IsOpen = false;
    }

    private void SplitActionButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Status = $"Applied owner {_viewModel.SelectedOwner}";
        _viewModel.Activity.Add(_viewModel.Status);
        SplitActionButton.IsOpen = false;
    }

    private void AssignSdkOwnerButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedOwner = "SDK";
        _viewModel.Status = "Owner set to SDK";
        _viewModel.Activity.Add(_viewModel.Status);
        SplitActionButton.IsOpen = false;
    }

    private void ToolkitWizard_PageChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.WizardPageChanges++;
        _viewModel.WizardStatus = ToolkitWizard.CurrentPage?.Title ?? "No wizard page";
    }

    private void ToolkitWizard_Finish(object sender, CancelRoutedEventArgs e)
    {
        _viewModel.WizardFinishes++;
        _viewModel.WizardStatus = "Wizard finished";
        _viewModel.Status = "Wizard finished";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void ToolkitWizard_Cancel(object sender, RoutedEventArgs e)
    {
        _viewModel.WizardCancels++;
        _viewModel.WizardStatus = "Wizard canceled";
        _viewModel.Status = "Wizard canceled";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void DocumentCountSpinner_Spin(object sender, SpinEventArgs e)
    {
        ApplyDocumentSpinnerDelta(e.Direction == SpinDirection.Increase ? 1 : -1);
    }

    private void ApplyDocumentSpinnerDelta(int delta)
    {
        _viewModel.SpinnerCount += delta;
        _viewModel.Status = $"Spinner count {_viewModel.SpinnerCount}";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    internal void ExerciseDocumentCountSpinner()
    {
        int spinnerCountBefore = ViewModel.SpinnerCount;
        ApplyDocumentSpinnerDelta(1);
        AssertEqual(spinnerCountBefore + 1, ViewModel.SpinnerCount, "Toolkit ButtonSpinner increased count");
        ApplyDocumentSpinnerDelta(-1);
        AssertEqual(spinnerCountBefore, ViewModel.SpinnerCount, "Toolkit ButtonSpinner restored count");
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

    internal void ValidateEditorFloatingState(bool expectedFloating)
    {
        bool documentPaneContainsEditor = DocumentPane.Children.Any(document => ReferenceEquals(document, EditorDocument));
        if (expectedFloating)
        {
            AssertEqual(true, EditorDocument.IsFloating, "AvalonDock editor document floating state");
            AssertEqual(false, documentPaneContainsEditor, "AvalonDock editor document pane membership while floating");
            if (DockLayoutRoot.FloatingWindows.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one AvalonDock floating window model, got {DockLayoutRoot.FloatingWindows.Count}.");
            }

            AssertEqual("Dock editor", Convert.ToString(ToggleEditorFloatButton.Content, CultureInfo.InvariantCulture), "AvalonDock float toggle content");
            if (ViewModel.AvalonDockFloatedCount <= 0)
            {
                throw new InvalidOperationException("Expected AvalonDock Floated event to fire for the editor document.");
            }
        }
        else
        {
            AssertEqual(false, EditorDocument.IsFloating, "AvalonDock editor document floating state");
            AssertEqual(true, documentPaneContainsEditor, "AvalonDock editor document pane membership after docking");
            if (DockLayoutRoot.FloatingWindows.Count != 0)
            {
                throw new InvalidOperationException(
                    $"Expected no AvalonDock floating window models after docking, got {DockLayoutRoot.FloatingWindows.Count}.");
            }

            AssertEqual("Float editor", Convert.ToString(ToggleEditorFloatButton.Content, CultureInfo.InvariantCulture), "AvalonDock float toggle content");
            if (ViewModel.AvalonDockDockedCount <= 0)
            {
                throw new InvalidOperationException("Expected AvalonDock Docked event to fire for the editor document.");
            }
        }
    }

    internal void ValidateOverviewDocumentLifecycleState(bool expectedOpen)
    {
        bool documentPaneContainsOverview = DocumentPane.Children.Any(document => ReferenceEquals(document, OverviewDocument));
        AssertEqual(expectedOpen, documentPaneContainsOverview, "AvalonDock overview document pane membership");

        if (expectedOpen)
        {
            AssertEqual(true, OverviewDocument.IsSelected, "AvalonDock overview document selected state after reopen");
            AssertEqual(true, OverviewDocument.IsActive, "AvalonDock overview document active state after reopen");
        }
        else
        {
            AssertEqual("overview", ViewModel.LastClosedDocumentContentId, "AvalonDock last closed document content id");
            if (ViewModel.AvalonDockDocumentClosedCount <= 0 ||
                ViewModel.OverviewDocumentClosedCount <= 0)
            {
                throw new InvalidOperationException("Expected AvalonDock document closed events to fire for the overview document.");
            }
        }
    }

    internal void ValidateOverviewCloseCanceledState(int expectedDocumentCount, int expectedClosedCount)
    {
        if (!DocumentPane.Children.Contains(OverviewDocument))
        {
            throw new InvalidOperationException("Expected canceled AvalonDock overview close to keep the document in the pane.");
        }

        AssertEqual(expectedDocumentCount, DocumentPane.ChildrenCount, "AvalonDock document count after canceled overview close");
        AssertEqual(expectedClosedCount, ViewModel.OverviewDocumentClosedCount, "AvalonDock overview closed count after canceled close");
        AssertEqual("overview", ViewModel.LastClosingDocumentContentId, "AvalonDock last closing document content id");
        if (ViewModel.AvalonDockDocumentClosingCount <= 0 ||
            ViewModel.AvalonDockDocumentCloseCanceledCount <= 0)
        {
            throw new InvalidOperationException("Expected AvalonDock document closing and cancellation events to fire for the overview document.");
        }

        AssertEqual(false, ViewModel.CancelNextOverviewClose, "AvalonDock cancel next close reset state");
        AssertEqual("Overview document close canceled", ViewModel.Status, "AvalonDock canceled close status");
    }

    internal void ValidateToolkitPopupState(bool expectedOpen)
    {
        AssertEqual(expectedOpen, CategoryPicker.IsDropDownOpen, "Toolkit CheckComboBox dropdown state");
        AssertEqual(expectedOpen, ReminderTimePicker.IsOpen, "Toolkit TimePicker popup state");
        AssertEqual(expectedOpen, AccentColorPicker.IsOpen, "Toolkit ColorPicker popup state");
        AssertEqual(expectedOpen, EstimateEditor.IsOpen, "Toolkit CalculatorUpDown popup state");
        AssertEqual(expectedOpen, ActionDropDownButton.IsOpen, "Toolkit DropDownButton popup state");

        if (expectedOpen)
        {
            var popupSource = PresentationSource.FromVisual(ActionDropDownContentRoot);
            if (popupSource is not HwndSource ||
                popupSource.CompositionTarget == null)
            {
                throw new InvalidOperationException(
                    "Expected Xceed dropdown content to be rooted in the portable public HwndSource facade while open.");
            }
        }
    }

    internal void ValidateToolkitSplitButtonPopupState(bool expectedOpen)
    {
        AssertEqual(expectedOpen, SplitActionButton.IsOpen, "Toolkit SplitButton popup state");

        if (expectedOpen)
        {
            var splitPopupSource = PresentationSource.FromVisual(SplitActionDropDownContentRoot);
            if (splitPopupSource is not HwndSource ||
                splitPopupSource.CompositionTarget == null)
            {
                throw new InvalidOperationException(
                    "Expected Xceed SplitButton dropdown content to be rooted in the portable public HwndSource facade while open.");
            }

            if (OwnerPickerList.Items.Count != ViewModel.Owners.Count)
            {
                throw new InvalidOperationException("Expected Toolkit SplitButton list content to bind all owners while open.");
            }
        }
    }

    internal void ValidateAvalonDockDocumentContextMenuState(bool expectedOpen)
    {
        AssertEqual(expectedOpen, DockDocumentContextMenu.IsOpen, "AvalonDock document context menu open state");
        if (DockManager.DocumentContextMenu != DockDocumentContextMenu)
        {
            throw new InvalidOperationException("Expected AvalonDock DockingManager to expose the sample document context menu.");
        }

        if (expectedOpen)
        {
            var menuSource = PresentationSource.FromVisual(DockContextActivateEditorMenuItem);
            if (menuSource is not HwndSource ||
                menuSource.CompositionTarget == null)
            {
                throw new InvalidOperationException(
                    "Expected AvalonDock document context menu to be rooted in the portable public HwndSource facade while open.");
            }
        }
    }

    internal void ValidateAvalonDockLayoutReplacementEvents(string layoutXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutXml);

        int layoutChangingCountBefore = ViewModel.AvalonDockLayoutChangingCount;
        int layoutChangedCountBefore = ViewModel.AvalonDockLayoutChangedCount;

        var serializer = new XmlLayoutSerializer(DockManager);
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

        if (ViewModel.AvalonDockLayoutChangingCount <= layoutChangingCountBefore ||
            ViewModel.AvalonDockLayoutChangedCount <= layoutChangedCountBefore)
        {
            throw new InvalidOperationException("Expected AvalonDock layout changing/changed events to fire when DockingManager.Layout changes.");
        }
    }

    internal void ValidateSourceBackedAvalonDockState(bool mutateSources)
    {
        if (!ReferenceEquals(SourceDockManager.DocumentsSource, ViewModel.SourceDocuments))
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock documents source to use the view-model collection.");
        }

        if (!ReferenceEquals(SourceDockManager.AnchorablesSource, ViewModel.SourceAnchorables))
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock anchorables source to use the view-model collection.");
        }

        AssertEqual(ViewModel.SourceDocuments.Count, SourceDocumentPane.ChildrenCount, "AvalonDock source document count");
        AssertEqual(ViewModel.SourceAnchorables.Count, SourceAnchorablePane.ChildrenCount, "AvalonDock source anchorable count");

        var firstDocument = ViewModel.SourceDocuments.First();
        var generatedDocument = FindGeneratedDocument(firstDocument);
        AssertEqual(firstDocument.Title, generatedDocument.Title, "AvalonDock source document title");
        AssertEqual(firstDocument.ContentId, generatedDocument.ContentId, "AvalonDock source document content id");

        var firstAnchorable = ViewModel.SourceAnchorables.First();
        var generatedAnchorable = FindGeneratedAnchorable(firstAnchorable);
        AssertEqual(firstAnchorable.Title, generatedAnchorable.Title, "AvalonDock source anchorable title");
        AssertEqual(firstAnchorable.ContentId, generatedAnchorable.ContentId, "AvalonDock source anchorable content id");

        if (SourceDockManager.GetLayoutItemFromModel(generatedDocument) == null ||
            SourceDockManager.GetLayoutItemFromModel(generatedAnchorable) == null)
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock layout items to be discoverable from their generated layout models.");
        }

        int activeContentChangesBefore = ViewModel.SourceActiveContentChangedCount;
        SourceDockManager.ActiveContent = firstDocument;
        AssertEqual(firstDocument, ViewModel.SourceActiveContent, "AvalonDock source active document binding");
        AssertEqual(firstDocument.Title, ViewModel.LastSourceActiveTitle, "AvalonDock source active document title");
        if (ViewModel.SourceActiveContentChangedCount <= activeContentChangesBefore)
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock ActiveContentChanged to fire for a source document.");
        }

        activeContentChangesBefore = ViewModel.SourceActiveContentChangedCount;
        SourceDockManager.ActiveContent = firstAnchorable;
        AssertEqual(firstAnchorable, ViewModel.SourceActiveContent, "AvalonDock source active anchorable binding");
        AssertEqual(firstAnchorable.Title, ViewModel.LastSourceActiveTitle, "AvalonDock source active anchorable title");
        if (ViewModel.SourceActiveContentChangedCount <= activeContentChangesBefore)
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock ActiveContentChanged to fire for a source anchorable.");
        }

        if (mutateSources)
        {
            int documentCountBeforeAdd = SourceDocumentPane.ChildrenCount;
            var addedDocument = ViewModel.AddSourceDocument();
            PumpDispatcherUntil(
                this,
                () => SourceDocumentPane.ChildrenCount == documentCountBeforeAdd + 1,
                TimeSpan.FromSeconds(2),
                "AvalonDock source document insertion");
            var generatedAddedDocument = FindGeneratedDocument(addedDocument);
            AssertEqual(addedDocument.Title, generatedAddedDocument.Title, "AvalonDock added source document title");
            AssertEqual(addedDocument.ContentId, generatedAddedDocument.ContentId, "AvalonDock added source document content id");

            SourceDockManager.ActiveContent = addedDocument;
            AssertEqual(addedDocument, ViewModel.SourceActiveContent, "AvalonDock added source active document binding");
        }
    }

    internal void ValidateAvalonDockThemeState(string expectedThemeName)
    {
        AssertEqual(expectedThemeName, ViewModel.ActiveDockThemeName, "AvalonDock active theme name");
        AssertAvalonDockTheme(DockManager.Theme, expectedThemeName, "primary DockingManager");
        AssertAvalonDockTheme(SourceDockManager.Theme, expectedThemeName, "source-backed DockingManager");

        if (TryFindResource("ToolkitAccentBrush") is not SolidColorBrush ||
            TryFindResource("ToolkitSubtleBrush") is not SolidColorBrush)
        {
            throw new InvalidOperationException("Expected Toolkit application theme brushes to resolve after AvalonDock theme switching.");
        }

        if (DockManager.DocumentHeaderTemplate is null ||
            SourceDockManager.LayoutItemTemplate is null ||
            SourceDockManager.LayoutItemContainerStyle is null)
        {
            throw new InvalidOperationException("Expected AvalonDock templates and layout-item styles to remain loaded after theme switching.");
        }
    }

    private static void AssertAvalonDockTheme(Theme theme, string expectedThemeName, string managerName)
    {
        string expectedTypeName = expectedThemeName switch
        {
            "Aero" => nameof(AeroTheme),
            "Metro" => nameof(MetroTheme),
            "VS2010" => nameof(VS2010Theme),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedThemeName), expectedThemeName, "Unknown AvalonDock theme.")
        };
        string expectedAssemblyName = expectedThemeName switch
        {
            "Aero" => "Xceed.Wpf.AvalonDock.Themes.Aero",
            "Metro" => "Xceed.Wpf.AvalonDock.Themes.Metro",
            "VS2010" => "Xceed.Wpf.AvalonDock.Themes.VS2010",
            _ => throw new ArgumentOutOfRangeException(nameof(expectedThemeName), expectedThemeName, "Unknown AvalonDock theme.")
        };

        Type themeType = theme.GetType();
        AssertEqual(expectedTypeName, themeType.Name, $"{managerName} AvalonDock theme type");
        AssertEqual(expectedAssemblyName, themeType.Assembly.GetName().Name ?? string.Empty, $"{managerName} AvalonDock theme assembly");

        MethodInfo getResourceUri = themeType.GetMethod(
            "GetResourceUri",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(themeType.FullName, "GetResourceUri");
        var resourceUri = Convert.ToString(getResourceUri.Invoke(theme, null), CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(resourceUri) ||
            !resourceUri.Contains(expectedAssemblyName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected {managerName} AvalonDock theme resource URI to come from {expectedAssemblyName}, got '{resourceUri}'.");
        }
    }

    private LayoutDocument FindGeneratedDocument(ToolkitDockItem sourceDocument)
    {
        return SourceDocumentPane.Children
            .OfType<LayoutDocument>()
            .FirstOrDefault(document => ReferenceEquals(document.Content, sourceDocument))
            ?? throw new InvalidOperationException($"Expected AvalonDock source document '{sourceDocument.ContentId}' to generate a LayoutDocument.");
    }

    private LayoutAnchorable FindGeneratedAnchorable(ToolkitDockItem sourceAnchorable)
    {
        return SourceAnchorablePane.Children
            .OfType<LayoutAnchorable>()
            .FirstOrDefault(anchorable => ReferenceEquals(anchorable.Content, sourceAnchorable))
            ?? throw new InvalidOperationException($"Expected AvalonDock source anchorable '{sourceAnchorable.ContentId}' to generate a LayoutAnchorable.");
    }

    internal void ValidateToolkitInputEditorState()
    {
        AssertEqual(ViewModel.QuickSearchText, QuickSearchTextBox.Text, "Toolkit AutoSelectTextBox text binding target");
        AssertEqual(AutoSelectBehavior.OnFocus, QuickSearchTextBox.AutoSelectBehavior, "Toolkit AutoSelectTextBox behavior");
        AssertEqual(ViewModel.AccessCode, AccessCodeBox.Password, "Toolkit WatermarkPasswordBox password state");
        AssertEqual(ViewModel.ReferenceCode, ReferenceMaskTextBox.Text, "Toolkit MaskedTextBox text binding target");
        AssertEqual("LL-0000", ReferenceMaskTextBox.Mask, "Toolkit MaskedTextBox mask");
        AssertEqual(ViewModel.ReminderTime, ReminderTimePicker.Value, "Toolkit TimePicker value binding target");
        AssertEqual(ViewModel.ReviewedAt, ReviewedAtEditor.Value, "Toolkit DateTimeUpDown value binding target");
        AssertEqual(ViewModel.Effort, EffortEditor.Value, "Toolkit TimeSpanUpDown value binding target");
        AssertEqual(ViewModel.ByteScore, ByteScoreEditor.Value, "Toolkit ByteUpDown value binding target");
        AssertEqual(ViewModel.DoubleScale, DoubleScaleEditor.Value, "Toolkit DoubleUpDown value binding target");
        AssertEqual(ViewModel.WorkItemId, WorkItemIdEditor.Value, "Toolkit LongUpDown value binding target");
        AssertEqual(ViewModel.Budget, BudgetEditor.Value, "Toolkit DecimalUpDown value binding target");
        AssertEqual(ViewModel.AccentColor, AccentColorCanvas.SelectedColor, "Toolkit ColorCanvas selected color binding target");
        AssertEqual(ViewModel.RichNotes, ToolkitRichTextBox.Text, "Toolkit RichTextBox text binding target");
        AssertEqual(ViewModel.MultiLineNotes, MultiLineNotesEditor.Text, "Toolkit MultiLineTextEditor text binding target");
        AssertEqual(ViewModel.SelectedOwner, OwnerComboBox.SelectedItem as string, "Toolkit WatermarkComboBox selected item binding target");
        AssertEqual(ViewModel.PriorityRangeStart, PriorityRangeSlider.LowerValue, "Toolkit RangeSlider lower value binding target");
        AssertEqual(ViewModel.PriorityRangeEnd, PriorityRangeSlider.HigherValue, "Toolkit RangeSlider higher value binding target");
        AssertEqual(true, DocumentCountSpinner.ShowSpinner, "Toolkit ButtonSpinner spinner visibility");
        AssertEqual("Right", Convert.ToString(DocumentCountSpinner.SpinnerLocation, CultureInfo.InvariantCulture), "Toolkit ButtonSpinner spinner location");

        if (ToolkitRichTextBox.TextFormatter is not PlainTextFormatter)
        {
            throw new InvalidOperationException("Expected Toolkit RichTextBox to use PlainTextFormatter.");
        }

        if (OwnerComboBox.Items.Count != ViewModel.Owners.Count)
        {
            throw new InvalidOperationException("Expected Toolkit WatermarkComboBox to bind all owners.");
        }

        if (FlagListBox.Items.Count != ViewModel.Flags.Count)
        {
            throw new InvalidOperationException("Expected Toolkit CheckListBox to bind all flags.");
        }

        foreach (string selectedFlag in ViewModel.SelectedFlags)
        {
            if (!FlagListBox.SelectedItems.Contains(selectedFlag))
            {
                throw new InvalidOperationException($"Expected Toolkit CheckListBox selected item '{selectedFlag}'.");
            }
        }
    }

    internal void ValidateToolkitWizardState(bool expectLoaded)
    {
        if (ToolkitWizard.Items.Count != 2)
        {
            throw new InvalidOperationException($"Expected Toolkit Wizard to contain two pages, got {ToolkitWizard.Items.Count}.");
        }

        AssertEqual("Scope", WizardScopePage.Title, "Toolkit Wizard first page title");
        AssertEqual("Choose owner and priority range", WizardScopePage.Description, "Toolkit Wizard first page description");
        AssertEqual(WizardPageType.Interior, WizardScopePage.PageType, "Toolkit Wizard first page type");
        AssertEqual(false, WizardScopePage.CanFinish.GetValueOrDefault(), "Toolkit Wizard first page finish capability");
        AssertEqual("Review", WizardReviewPage.Title, "Toolkit Wizard review page title");
        AssertEqual("Confirm Toolkit state", WizardReviewPage.Description, "Toolkit Wizard review page description");
        AssertEqual(WizardPageType.Interior, WizardReviewPage.PageType, "Toolkit Wizard review page type");
        AssertEqual(true, WizardReviewPage.CanFinish.GetValueOrDefault(), "Toolkit Wizard review page finish capability");
        AssertEqual(false, ToolkitWizard.FinishButtonClosesWindow, "Toolkit Wizard finish close behavior");
        AssertEqual(false, ToolkitWizard.CancelButtonClosesWindow, "Toolkit Wizard cancel close behavior");

        if (expectLoaded && ToolkitWizard.CurrentPage == null)
        {
            throw new InvalidOperationException("Expected loaded Toolkit Wizard to select an initial page.");
        }
    }

    internal void ExerciseToolkitWizard()
    {
        ValidateToolkitWizardState(expectLoaded: true);

        int pageChangesBefore = ViewModel.WizardPageChanges;
        ToolkitWizard.CurrentPage = WizardReviewPage;
        AssertEqual(WizardReviewPage, ToolkitWizard.CurrentPage, "Toolkit Wizard current page after review navigation");
        if (ViewModel.WizardPageChanges <= pageChangesBefore)
        {
            throw new InvalidOperationException("Expected Toolkit Wizard page change event to update the view model.");
        }

        int finishesBefore = ViewModel.WizardFinishes;
        ToolkitWizard.RaiseEvent(new CancelRoutedEventArgs { RoutedEvent = Wizard.FinishEvent });
        if (ViewModel.WizardFinishes <= finishesBefore ||
            !string.Equals(ViewModel.WizardStatus, "Wizard finished", StringComparison.Ordinal) ||
            !string.Equals(ViewModel.Status, "Wizard finished", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Toolkit Wizard finish event to update sample state.");
        }

        int cancelsBefore = ViewModel.WizardCancels;
        ToolkitWizard.RaiseEvent(new RoutedEventArgs(Wizard.CancelEvent));
        if (ViewModel.WizardCancels <= cancelsBefore ||
            !string.Equals(ViewModel.WizardStatus, "Wizard canceled", StringComparison.Ordinal) ||
            !string.Equals(ViewModel.Status, "Wizard canceled", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Toolkit Wizard cancel event to update sample state.");
        }
    }

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

        await ValidateLivePopupControlsAsync(liveHost);
        await ValidateLiveAvalonDockDocumentContextMenuAsync(liveHost);
        await ValidateLiveInputEditorsAsync(liveHost);
        await ValidateLiveWizardAsync(liveHost);
        await ValidateLiveSourceBackedAvalonDockAsync(liveHost);
        await ValidateLiveAvalonDockThemeSwitchingAsync(liveHost);

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
                if (ViewModel.AvalonDockActiveContentChangedCount <= 0)
                {
                    throw new InvalidOperationException("Expected Toolkit live AvalonDock active content event to fire.");
                }
            },
            DispatcherPriority.Send);

        await ValidateLiveOverviewDocumentLifecycleAsync(liveHost);

        await ClickLiveControlAsync(liveHost, ToggleEditorFloatButton, "ToggleEditorFloatButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => EditorDocument.IsFloating && DockLayoutRoot.FloatingWindows.Count == 1,
            "Toolkit live editor document floating window model");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateEditorFloatingState(expectedFloating: true),
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, ToggleEditorFloatButton, "ToggleEditorFloatButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => !EditorDocument.IsFloating && DockLayoutRoot.FloatingWindows.Count == 0,
            "Toolkit live editor document docked model");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateEditorFloatingState(expectedFloating: false),
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
                    !ViewModel.LastSerializedLayout.Contains("ContentId=\"overview\"", StringComparison.Ordinal) ||
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

                ValidateAvalonDockLayoutReplacementEvents(ViewModel.LastSerializedLayout);

                return "host mouse/text input, binding update, Toolkit popup/dropdown editors, Toolkit masked/time/updown/checklist/rich/multiline/spinner editors, Toolkit auto-select/password/numeric/color-canvas controls, Toolkit selector/range/split controls, Toolkit wizard navigation, AvalonDock source-backed documents/anchorables, AvalonDock theme switching, AvalonDock document context menu and close cancellation, document activation, document close/reopen, floating document window, anchorable hide/show, auto-hide side groups, layout replacement events, and layout serialization updated";
            },
            DispatcherPriority.Send);
    }

    private async Task ValidateLivePopupControlsAsync(object liveHost)
    {
        await ClickLiveControlAsync(liveHost, ActionDropDownButton, "ActionDropDownButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => ActionDropDownButton.IsOpen,
            "Toolkit live DropDownButton popup open state");

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ActionDropDownButton.IsOpen = false;
                CategoryPicker.IsDropDownOpen = true;
                ReminderTimePicker.IsOpen = true;
                AccentColorPicker.IsOpen = true;
                EstimateEditor.IsOpen = true;
                ActionDropDownButton.IsOpen = true;
            },
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(
            liveHost,
            () => CategoryPicker.IsDropDownOpen &&
                  ReminderTimePicker.IsOpen &&
                  AccentColorPicker.IsOpen &&
                  EstimateEditor.IsOpen &&
                  ActionDropDownButton.IsOpen,
            "Toolkit live popup-backed controls open state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateToolkitPopupState(expectedOpen: true),
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AccentColorPicker.SelectedColor = Colors.MediumSeaGreen;
                EstimateEditor.Value = 42.25m;
                CategoryPicker.IsDropDownOpen = false;
                ReminderTimePicker.IsOpen = false;
                AccentColorPicker.IsOpen = false;
                EstimateEditor.IsOpen = false;
                ActionDropDownButton.IsOpen = false;
            },
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(
            liveHost,
            () => !CategoryPicker.IsDropDownOpen &&
                  !ReminderTimePicker.IsOpen &&
                  !AccentColorPicker.IsOpen &&
                  !EstimateEditor.IsOpen &&
                  !ActionDropDownButton.IsOpen,
            "Toolkit live popup-backed controls closed state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateToolkitPopupState(expectedOpen: false);
                AssertEqual(Colors.MediumSeaGreen, ViewModel.AccentColor, "Toolkit live ColorPicker selected color binding source");
                AssertEqual(42.25m, ViewModel.Estimate, "Toolkit live CalculatorUpDown value binding source");
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => SplitActionButton.IsOpen = true,
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(
            liveHost,
            () => SplitActionButton.IsOpen,
            "Toolkit live SplitButton dropdown open state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateToolkitSplitButtonPopupState(expectedOpen: true),
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => SplitActionButton.IsOpen = false,
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(
            liveHost,
            () => !SplitActionButton.IsOpen,
            "Toolkit live SplitButton dropdown closed state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateToolkitSplitButtonPopupState(expectedOpen: false),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveAvalonDockDocumentContextMenuAsync(object liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                DockDocumentContextMenu.PlacementTarget = DockManager;
                DockDocumentContextMenu.IsOpen = true;
            },
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(
            liveHost,
            () => DockDocumentContextMenu.IsOpen,
            "Toolkit live AvalonDock document context menu open state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateAvalonDockDocumentContextMenuState(expectedOpen: true);
                DockContextCancelNextCloseMenuItem.IsChecked = true;
                DockContextCancelNextCloseMenuItem.GetBindingExpression(MenuItem.IsCheckedProperty)?.UpdateSource();
                AssertEqual(true, ViewModel.CancelNextOverviewClose, "Toolkit live AvalonDock context menu cancellation binding");
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => DockDocumentContextMenu.IsOpen = false,
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(
            liveHost,
            () => !DockDocumentContextMenu.IsOpen,
            "Toolkit live AvalonDock document context menu closed state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateAvalonDockDocumentContextMenuState(expectedOpen: false),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveInputEditorsAsync(object liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                QuickSearchTextBox.Text = "Live quick search";
                QuickSearchTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                AccessCodeBox.Password = "live-code";
                ViewModel.AccessCode = AccessCodeBox.Password;
                ReferenceMaskTextBox.Text = "AB-1234";
                ReferenceMaskTextBox.GetBindingExpression(MaskedTextBox.TextProperty)?.UpdateSource();

                ReminderTimePicker.Value = DateTime.Today.AddHours(15).AddMinutes(45);
                ReviewedAtEditor.Value = DateTime.Today.AddHours(16).AddMinutes(30);
                EffortEditor.Value = TimeSpan.FromMinutes(135);
                ByteScoreEditor.Value = 72;
                DoubleScaleEditor.Value = 4.5;
                WorkItemIdEditor.Value = 16384L;
                BudgetEditor.Value = 256.50m;
                AccentColorCanvas.SelectedColor = Colors.DarkCyan;

                OwnerComboBox.SelectedItem = "ProGPU";
                OwnerComboBox.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();

                PriorityRangeSlider.LowerValue = 3.0;
                PriorityRangeSlider.HigherValue = 7.0;
                PriorityRangeSlider.GetBindingExpression(RangeSlider.LowerValueProperty)?.UpdateSource();
                PriorityRangeSlider.GetBindingExpression(RangeSlider.HigherValueProperty)?.UpdateSource();

                if (!ViewModel.SelectedFlags.Contains("Reviewed"))
                {
                    ViewModel.SelectedFlags.Add("Reviewed");
                }

                ToolkitRichTextBox.Text = "Live rich notes from Toolkit RichTextBox";
                ToolkitRichTextBox.GetBindingExpression(ToolkitRichTextBox.TextProperty)?.UpdateSource();
                MultiLineNotesEditor.Text = "Live multiline notes from Toolkit MultiLineTextEditor";
                MultiLineNotesEditor.GetBindingExpression(MultiLineTextEditor.TextProperty)?.UpdateSource();

                ExerciseDocumentCountSpinner();
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateToolkitInputEditorState();
                AssertEqual("Live quick search", ViewModel.QuickSearchText, "Toolkit live AutoSelectTextBox binding source");
                AssertEqual("live-code", ViewModel.AccessCode, "Toolkit live WatermarkPasswordBox password state");
                AssertEqual("AB-1234", ViewModel.ReferenceCode, "Toolkit live MaskedTextBox binding source");
                AssertEqual(DateTime.Today.AddHours(15).AddMinutes(45), ViewModel.ReminderTime, "Toolkit live TimePicker binding source");
                AssertEqual(DateTime.Today.AddHours(16).AddMinutes(30), ViewModel.ReviewedAt, "Toolkit live DateTimeUpDown binding source");
                AssertEqual(TimeSpan.FromMinutes(135), ViewModel.Effort, "Toolkit live TimeSpanUpDown binding source");
                AssertEqual((byte)72, ViewModel.ByteScore.GetValueOrDefault(), "Toolkit live ByteUpDown binding source");
                AssertEqual(4.5, ViewModel.DoubleScale.GetValueOrDefault(), "Toolkit live DoubleUpDown binding source");
                AssertEqual(16384L, ViewModel.WorkItemId.GetValueOrDefault(), "Toolkit live LongUpDown binding source");
                AssertEqual(256.50m, ViewModel.Budget.GetValueOrDefault(), "Toolkit live DecimalUpDown binding source");
                AssertEqual(Colors.DarkCyan, ViewModel.AccentColor.GetValueOrDefault(), "Toolkit live ColorCanvas binding source");
                AssertEqual("Live rich notes from Toolkit RichTextBox", ViewModel.RichNotes, "Toolkit live RichTextBox binding source");
                AssertEqual("Live multiline notes from Toolkit MultiLineTextEditor", ViewModel.MultiLineNotes, "Toolkit live MultiLineTextEditor binding source");
                AssertEqual("ProGPU", ViewModel.SelectedOwner, "Toolkit live WatermarkComboBox binding source");
                AssertEqual(3.0, ViewModel.PriorityRangeStart, "Toolkit live RangeSlider lower binding source");
                AssertEqual(7.0, ViewModel.PriorityRangeEnd, "Toolkit live RangeSlider higher binding source");
                if (!FlagListBox.SelectedItems.Contains("Reviewed"))
                {
                    throw new InvalidOperationException("Expected Toolkit live CheckListBox to select the added flag.");
                }
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, SplitActionButton, "SplitActionButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => AssertEqual("Applied owner ProGPU", ViewModel.Status, "Toolkit live SplitButton click status"),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveWizardAsync(object liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ExerciseToolkitWizard(),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveSourceBackedAvalonDockAsync(object liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateSourceBackedAvalonDockState(mutateSources: true),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveAvalonDockThemeSwitchingAsync(object liveHost)
    {
        int themeSwitchCountBefore = ViewModel.DockThemeSwitchCount;
        string[] expectedThemes = ["Metro", "VS2010", "Aero"];
        foreach (string expectedTheme in expectedThemes)
        {
            await ClickLiveControlAsync(liveHost, CycleDockThemeButton, "CycleDockThemeButton");
            await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    ValidateAvalonDockThemeState(expectedTheme);
                    AssertEqual(
                        $"AvalonDock theme switched to {expectedTheme}",
                        ViewModel.Status,
                        "Toolkit live AvalonDock theme switch status");
                },
                DispatcherPriority.Send);
        }

        if (ViewModel.DockThemeSwitchCount < themeSwitchCountBefore + expectedThemes.Length)
        {
            throw new InvalidOperationException("Expected live AvalonDock theme switch count to advance for each theme.");
        }
    }

    private async Task ValidateLiveOverviewDocumentLifecycleAsync(object liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                int documentCountBeforeCanceledClose = DocumentPane.ChildrenCount;
                int closedCountBeforeCanceledClose = ViewModel.OverviewDocumentClosedCount;
                ViewModel.CancelNextOverviewClose = true;
                CloseOverviewDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                ValidateOverviewCloseCanceledState(documentCountBeforeCanceledClose, closedCountBeforeCanceledClose);
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                int documentCountBeforeClose = DocumentPane.ChildrenCount;
                CloseOverviewDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                ValidateOverviewDocumentLifecycleState(expectedOpen: false);
                AssertEqual(documentCountBeforeClose - 1, DocumentPane.ChildrenCount, "Toolkit live AvalonDock document count after overview close");
                AssertEqual("Overview document closed", ViewModel.Status, "Toolkit live overview close status");
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                int documentCountBeforeReopen = DocumentPane.ChildrenCount;
                ReopenOverviewDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                ValidateOverviewDocumentLifecycleState(expectedOpen: true);
                AssertEqual(documentCountBeforeReopen + 1, DocumentPane.ChildrenCount, "Toolkit live AvalonDock document count after overview reopen");
                AssertEqual("Overview document reopened", ViewModel.Status, "Toolkit live overview reopen status");
            },
            DispatcherPriority.Send);
    }

    private async Task WaitForLiveConditionAsync(object liveHost, Func<bool> condition, string description)
    {
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            if (await InvokeWithLiveHostWakeAsync(liveHost, condition, DispatcherPriority.Background))
            {
                return;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        throw new InvalidOperationException($"Timed out waiting for {description}.");
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
        target.BringIntoView();
        target.UpdateLayout();

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

    private static void PumpDispatcherUntil(
        DispatcherObject dispatcherObject,
        Func<bool> condition,
        TimeSpan timeout,
        string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            dispatcherObject.Dispatcher.Invoke(
                static () => { },
                DispatcherPriority.Background);

            if (condition())
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException($"Timed out waiting for {description}.");
            }

            System.Threading.Thread.Sleep(1);
        }
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
    private string _quickSearchText = "Search";
    private string _accessCode = "sdk";
    private string _selectedOwner = "WPF";
    private double _priorityRangeStart = 2.0;
    private double _priorityRangeEnd = 8.0;
    private DateTime? _dueDate = DateTime.Today.AddDays(7).AddHours(9);
    private DateTime? _reminderTime = DateTime.Today.AddHours(10).AddMinutes(15);
    private DateTime? _reviewedAt = DateTime.Today.AddHours(11);
    private TimeSpan? _effort = TimeSpan.FromMinutes(90);
    private string _referenceCode = "PR-2048";
    private Color? _accentColor = Colors.SteelBlue;
    private decimal? _estimate = 12.5m;
    private byte? _byteScore = 12;
    private double? _doubleScale = 1.5;
    private long? _workItemId = 4096L;
    private decimal? _budget = 64.25m;
    private string _richNotes = "Toolkit rich notes";
    private string _multiLineNotes = "Toolkit multiline notes";
    private int _spinnerCount = 2;
    private bool _isBusy;
    private int _wizardPageChanges;
    private int _wizardFinishes;
    private int _wizardCancels;
    private string _wizardStatus = "Wizard idle";
    private int _avalonDockActiveContentChangedCount;
    private string _lastActiveContentTitle = string.Empty;
    private int _avalonDockDocumentClosingCount;
    private int _avalonDockDocumentClosedCount;
    private int _avalonDockDocumentCloseCanceledCount;
    private int _overviewDocumentClosedCount;
    private int _avalonDockFloatedCount;
    private int _avalonDockDockedCount;
    private int _avalonDockLayoutChangingCount;
    private int _avalonDockLayoutChangedCount;
    private bool _cancelNextOverviewClose;
    private string _lastClosingDocumentContentId = string.Empty;
    private string _lastClosedDocumentContentId = string.Empty;
    private object? _sourceActiveContent;
    private int _sourceActiveContentChangedCount;
    private string _lastSourceActiveTitle = string.Empty;
    private string _activeDockThemeName = "Aero";
    private int _dockThemeSwitchCount;
    private string _status = "Toolkit sample ready";
    private string _lastSerializedLayout = string.Empty;

    public ToolkitViewModel()
    {
        Documents =
        [
            new("Overview", "WPF", DateTime.Today, "No-source-change SDK app consuming Extended WPF Toolkit."),
            new("AvalonDock", "Xceed", DateTime.Today.AddDays(-1), "DockingManager layout with documents and anchorables.")
        ];
        SourceDocuments =
        [
            new("Source Overview", "source-overview", "Generated from DockingManager.DocumentsSource.", canClose: true),
            new("Source Editor", "source-editor", "Another source-backed document view model.", canClose: true)
        ];
        SourceAnchorables =
        [
            new("Source Tool", "source-tool", "Generated from DockingManager.AnchorablesSource.", canClose: false)
        ];
        Owners = ["WPF", "ProGPU", "SDK", "Xceed"];
        Categories = ["Framework", "Toolkit", "AvalonDock", "Rendering"];
        SelectedCategories = ["Toolkit", "AvalonDock"];
        Flags = ["Pinned", "Reviewed", "Blocked", "Urgent"];
        SelectedFlags = ["Pinned"];
        Activity = ["Toolkit package loaded", "AvalonDock layout loaded"];
        _selectedDocument = Documents[0];
        _sourceActiveContent = SourceDocuments[0];
        Documents.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DocumentCount));
        SourceDocuments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SourceDocumentCount));
        SourceAnchorables.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SourceAnchorableCount));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ToolkitDocument> Documents { get; }

    public ObservableCollection<ToolkitDockItem> SourceDocuments { get; }

    public ObservableCollection<ToolkitDockItem> SourceAnchorables { get; }

    public ObservableCollection<string> Categories { get; }

    public ObservableCollection<string> Owners { get; }

    public ObservableCollection<string> SelectedCategories { get; }

    public ObservableCollection<string> Flags { get; }

    public ObservableCollection<string> SelectedFlags { get; }

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

    public int SourceDocumentCount => SourceDocuments.Count;

    public int SourceAnchorableCount => SourceAnchorables.Count;

    public ToolkitDockItem AddSourceDocument()
    {
        int index = SourceDocuments.Count + 1;
        var document = new ToolkitDockItem(
            $"Source Generated {index}",
            $"source-generated-{index}",
            $"Generated source-backed AvalonDock document {index}.",
            canClose: true);
        SourceDocuments.Add(document);
        SourceActiveContent = document;
        return document;
    }

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

    public string QuickSearchText
    {
        get => _quickSearchText;
        set
        {
            if (!string.Equals(_quickSearchText, value, StringComparison.Ordinal))
            {
                _quickSearchText = value;
                OnPropertyChanged();
            }
        }
    }

    public string AccessCode
    {
        get => _accessCode;
        set
        {
            if (!string.Equals(_accessCode, value, StringComparison.Ordinal))
            {
                _accessCode = value;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedOwner
    {
        get => _selectedOwner;
        set
        {
            if (!string.Equals(_selectedOwner, value, StringComparison.Ordinal))
            {
                _selectedOwner = value;
                OnPropertyChanged();
            }
        }
    }

    public double PriorityRangeStart
    {
        get => _priorityRangeStart;
        set
        {
            if (!Equals(_priorityRangeStart, value))
            {
                _priorityRangeStart = value;
                OnPropertyChanged();
            }
        }
    }

    public double PriorityRangeEnd
    {
        get => _priorityRangeEnd;
        set
        {
            if (!Equals(_priorityRangeEnd, value))
            {
                _priorityRangeEnd = value;
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

    public DateTime? ReminderTime
    {
        get => _reminderTime;
        set
        {
            if (_reminderTime != value)
            {
                _reminderTime = value;
                OnPropertyChanged();
            }
        }
    }

    public string ReferenceCode
    {
        get => _referenceCode;
        set
        {
            if (!string.Equals(_referenceCode, value, StringComparison.Ordinal))
            {
                _referenceCode = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? ReviewedAt
    {
        get => _reviewedAt;
        set
        {
            if (_reviewedAt != value)
            {
                _reviewedAt = value;
                OnPropertyChanged();
            }
        }
    }

    public TimeSpan? Effort
    {
        get => _effort;
        set
        {
            if (_effort != value)
            {
                _effort = value;
                OnPropertyChanged();
            }
        }
    }

    public Color? AccentColor
    {
        get => _accentColor;
        set
        {
            if (_accentColor != value)
            {
                _accentColor = value;
                OnPropertyChanged();
            }
        }
    }

    public decimal? Estimate
    {
        get => _estimate;
        set
        {
            if (_estimate != value)
            {
                _estimate = value;
                OnPropertyChanged();
            }
        }
    }

    public byte? ByteScore
    {
        get => _byteScore;
        set
        {
            if (_byteScore != value)
            {
                _byteScore = value;
                OnPropertyChanged();
            }
        }
    }

    public double? DoubleScale
    {
        get => _doubleScale;
        set
        {
            if (_doubleScale != value)
            {
                _doubleScale = value;
                OnPropertyChanged();
            }
        }
    }

    public long? WorkItemId
    {
        get => _workItemId;
        set
        {
            if (_workItemId != value)
            {
                _workItemId = value;
                OnPropertyChanged();
            }
        }
    }

    public decimal? Budget
    {
        get => _budget;
        set
        {
            if (_budget != value)
            {
                _budget = value;
                OnPropertyChanged();
            }
        }
    }

    public string RichNotes
    {
        get => _richNotes;
        set
        {
            if (!string.Equals(_richNotes, value, StringComparison.Ordinal))
            {
                _richNotes = value;
                OnPropertyChanged();
            }
        }
    }

    public string MultiLineNotes
    {
        get => _multiLineNotes;
        set
        {
            if (!string.Equals(_multiLineNotes, value, StringComparison.Ordinal))
            {
                _multiLineNotes = value;
                OnPropertyChanged();
            }
        }
    }

    public int SpinnerCount
    {
        get => _spinnerCount;
        set
        {
            if (_spinnerCount != value)
            {
                _spinnerCount = value;
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

    public int WizardPageChanges
    {
        get => _wizardPageChanges;
        set
        {
            if (_wizardPageChanges != value)
            {
                _wizardPageChanges = value;
                OnPropertyChanged();
            }
        }
    }

    public int WizardFinishes
    {
        get => _wizardFinishes;
        set
        {
            if (_wizardFinishes != value)
            {
                _wizardFinishes = value;
                OnPropertyChanged();
            }
        }
    }

    public int WizardCancels
    {
        get => _wizardCancels;
        set
        {
            if (_wizardCancels != value)
            {
                _wizardCancels = value;
                OnPropertyChanged();
            }
        }
    }

    public string WizardStatus
    {
        get => _wizardStatus;
        set
        {
            if (!string.Equals(_wizardStatus, value, StringComparison.Ordinal))
            {
                _wizardStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockActiveContentChangedCount
    {
        get => _avalonDockActiveContentChangedCount;
        set
        {
            if (_avalonDockActiveContentChangedCount != value)
            {
                _avalonDockActiveContentChangedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastActiveContentTitle
    {
        get => _lastActiveContentTitle;
        set
        {
            if (!string.Equals(_lastActiveContentTitle, value, StringComparison.Ordinal))
            {
                _lastActiveContentTitle = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockDocumentClosingCount
    {
        get => _avalonDockDocumentClosingCount;
        set
        {
            if (_avalonDockDocumentClosingCount != value)
            {
                _avalonDockDocumentClosingCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockDocumentClosedCount
    {
        get => _avalonDockDocumentClosedCount;
        set
        {
            if (_avalonDockDocumentClosedCount != value)
            {
                _avalonDockDocumentClosedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockDocumentCloseCanceledCount
    {
        get => _avalonDockDocumentCloseCanceledCount;
        set
        {
            if (_avalonDockDocumentCloseCanceledCount != value)
            {
                _avalonDockDocumentCloseCanceledCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int OverviewDocumentClosedCount
    {
        get => _overviewDocumentClosedCount;
        set
        {
            if (_overviewDocumentClosedCount != value)
            {
                _overviewDocumentClosedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockFloatedCount
    {
        get => _avalonDockFloatedCount;
        set
        {
            if (_avalonDockFloatedCount != value)
            {
                _avalonDockFloatedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockDockedCount
    {
        get => _avalonDockDockedCount;
        set
        {
            if (_avalonDockDockedCount != value)
            {
                _avalonDockDockedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockLayoutChangingCount
    {
        get => _avalonDockLayoutChangingCount;
        set
        {
            if (_avalonDockLayoutChangingCount != value)
            {
                _avalonDockLayoutChangingCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockLayoutChangedCount
    {
        get => _avalonDockLayoutChangedCount;
        set
        {
            if (_avalonDockLayoutChangedCount != value)
            {
                _avalonDockLayoutChangedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CancelNextOverviewClose
    {
        get => _cancelNextOverviewClose;
        set
        {
            if (_cancelNextOverviewClose != value)
            {
                _cancelNextOverviewClose = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastClosingDocumentContentId
    {
        get => _lastClosingDocumentContentId;
        set
        {
            if (!string.Equals(_lastClosingDocumentContentId, value, StringComparison.Ordinal))
            {
                _lastClosingDocumentContentId = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastClosedDocumentContentId
    {
        get => _lastClosedDocumentContentId;
        set
        {
            if (!string.Equals(_lastClosedDocumentContentId, value, StringComparison.Ordinal))
            {
                _lastClosedDocumentContentId = value;
                OnPropertyChanged();
            }
        }
    }

    public object? SourceActiveContent
    {
        get => _sourceActiveContent;
        set
        {
            if (!ReferenceEquals(_sourceActiveContent, value))
            {
                _sourceActiveContent = value;
                OnPropertyChanged();
            }
        }
    }

    public int SourceActiveContentChangedCount
    {
        get => _sourceActiveContentChangedCount;
        set
        {
            if (_sourceActiveContentChangedCount != value)
            {
                _sourceActiveContentChangedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastSourceActiveTitle
    {
        get => _lastSourceActiveTitle;
        set
        {
            if (!string.Equals(_lastSourceActiveTitle, value, StringComparison.Ordinal))
            {
                _lastSourceActiveTitle = value;
                OnPropertyChanged();
            }
        }
    }

    public string ActiveDockThemeName
    {
        get => _activeDockThemeName;
        set
        {
            if (!string.Equals(_activeDockThemeName, value, StringComparison.Ordinal))
            {
                _activeDockThemeName = value;
                OnPropertyChanged();
            }
        }
    }

    public int DockThemeSwitchCount
    {
        get => _dockThemeSwitchCount;
        set
        {
            if (_dockThemeSwitchCount != value)
            {
                _dockThemeSwitchCount = value;
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

internal sealed class ToolkitDockItem : INotifyPropertyChanged
{
    private string _body;

    public ToolkitDockItem(string title, string contentId, string body, bool canClose)
    {
        Title = title;
        ContentId = contentId;
        _body = body;
        CanClose = canClose;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public string ContentId { get; }

    public bool CanClose { get; }

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
        Require<AutoSelectTextBox>(window, "QuickSearchTextBox");
        Require<WatermarkPasswordBox>(window, "AccessCodeBox");
        Require<WatermarkComboBox>(window, "OwnerComboBox");
        Require<RangeSlider>(window, "PriorityRangeSlider");
        Require<DateTimePicker>(window, "DueDatePicker");
        Require<TimePicker>(window, "ReminderTimePicker");
        Require<DateTimeUpDown>(window, "ReviewedAtEditor");
        Require<TimeSpanUpDown>(window, "EffortEditor");
        Require<MaskedTextBox>(window, "ReferenceMaskTextBox");
        Require<CheckComboBox>(window, "CategoryPicker");
        Require<CheckListBox>(window, "FlagListBox");
        Require<ColorPicker>(window, "AccentColorPicker");
        Require<CalculatorUpDown>(window, "EstimateEditor");
        Require<ByteUpDown>(window, "ByteScoreEditor");
        Require<DoubleUpDown>(window, "DoubleScaleEditor");
        Require<LongUpDown>(window, "WorkItemIdEditor");
        Require<DecimalUpDown>(window, "BudgetEditor");
        Require<ColorCanvas>(window, "AccentColorCanvas");
        Require<DropDownButton>(window, "ActionDropDownButton");
        Require<Button>(window, "MarkReviewedButton");
        Require<SplitButton>(window, "SplitActionButton");
        Require<ListBox>(window, "OwnerPickerList");
        Require<Button>(window, "AssignSdkOwnerButton");
        Require<Wizard>(window, "ToolkitWizard");
        Require<WizardPage>(window, "WizardScopePage");
        Require<WizardPage>(window, "WizardReviewPage");
        Require<ToolkitRichTextBox>(window, "ToolkitRichTextBox");
        Require<MultiLineTextEditor>(window, "MultiLineNotesEditor");
        Require<ButtonSpinner>(window, "DocumentCountSpinner");
        Require<BusyIndicator>(window, "BusyIndicator");
        Require<PropertyGrid>(window, "DocumentPropertyGrid");
        Require<Button>(window, "AddSourceDocumentButton");
        Require<Button>(window, "ActivateSourceToolButton");
        Require<DockingManager>(window, "SourceDockManager");
        Require<LayoutDocumentPane>(window, "SourceDocumentPane");
        Require<LayoutAnchorablePane>(window, "SourceAnchorablePane");
        Require<ContextMenu>(window, "DockDocumentContextMenu");
        Require<MenuItem>(window, "DockContextActivateEditorMenuItem");
        Require<MenuItem>(window, "DockContextCloseOverviewMenuItem");
        Require<MenuItem>(window, "DockContextCancelNextCloseMenuItem");
        Require<Button>(window, "ActivateEditorButton");
        Require<Button>(window, "CloseOverviewDocumentButton");
        Require<Button>(window, "ReopenOverviewDocumentButton");
        Require<Button>(window, "ToggleEditorFloatButton");
        Require<Button>(window, "TogglePropertyPaneButton");
        Require<Button>(window, "ToggleActivityAutoHideButton");
        Require<Button>(window, "ToggleAgendaAutoHideButton");
        Require<Button>(window, "CycleDockThemeButton");
        Require<Button>(window, "SerializeLayoutButton");

        window.ValidateAvalonDockThemeState("Aero");

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
            window.ViewModel.Owners.Count != 4 ||
            window.ViewModel.Categories.Count != 4 ||
            window.ViewModel.SelectedCategories.Count != 2 ||
            window.ViewModel.Flags.Count != 4 ||
            window.ViewModel.SelectedFlags.Count != 1)
        {
            throw new InvalidOperationException("Expected toolkit sample view-model collections to be initialized.");
        }

        if (window.PriorityEditor.Value != window.ViewModel.Priority)
        {
            throw new InvalidOperationException("Expected IntegerUpDown value binding to initialize.");
        }

        if (window.AccentColorPicker.SelectedColor != window.ViewModel.AccentColor ||
            window.EstimateEditor.Value != window.ViewModel.Estimate)
        {
            throw new InvalidOperationException("Expected Toolkit popup editor bindings to initialize.");
        }

        window.ValidateToolkitInputEditorState();
        window.ValidateToolkitWizardState(expectLoaded);
        window.ValidateSourceBackedAvalonDockState(mutateSources: true);

        if (expectLoaded)
        {
            window.CategoryPicker.IsDropDownOpen = true;
            window.ReminderTimePicker.IsOpen = true;
            window.AccentColorPicker.IsOpen = true;
            window.EstimateEditor.IsOpen = true;
            window.ActionDropDownButton.IsOpen = true;
            PumpDispatcherUntil(
                window,
                () => window.CategoryPicker.IsDropDownOpen &&
                      window.ReminderTimePicker.IsOpen &&
                      window.AccentColorPicker.IsOpen &&
                      window.EstimateEditor.IsOpen &&
                      window.ActionDropDownButton.IsOpen,
                TimeSpan.FromSeconds(2),
                "Toolkit popup-backed controls open state");
            window.ValidateToolkitPopupState(expectedOpen: true);

            window.AccentColorPicker.SelectedColor = Colors.MediumSeaGreen;
            window.EstimateEditor.Value = 42.25m;
            window.CategoryPicker.IsDropDownOpen = false;
            window.ReminderTimePicker.IsOpen = false;
            window.AccentColorPicker.IsOpen = false;
            window.EstimateEditor.IsOpen = false;
            window.ActionDropDownButton.IsOpen = false;
            PumpDispatcherUntil(
                window,
                () => !window.CategoryPicker.IsDropDownOpen &&
                      !window.ReminderTimePicker.IsOpen &&
                      !window.AccentColorPicker.IsOpen &&
                      !window.EstimateEditor.IsOpen &&
                      !window.ActionDropDownButton.IsOpen,
                TimeSpan.FromSeconds(2),
                "Toolkit popup-backed controls closed state");
            window.ValidateToolkitPopupState(expectedOpen: false);

            window.SplitActionButton.IsOpen = true;
            PumpDispatcherUntil(
                window,
                () => window.SplitActionButton.IsOpen,
                TimeSpan.FromSeconds(2),
                "Toolkit SplitButton dropdown open state");
            window.ValidateToolkitSplitButtonPopupState(expectedOpen: true);

            window.SplitActionButton.IsOpen = false;
            PumpDispatcherUntil(
                window,
                () => !window.SplitActionButton.IsOpen,
                TimeSpan.FromSeconds(2),
                "Toolkit SplitButton dropdown closed state");
            window.ValidateToolkitSplitButtonPopupState(expectedOpen: false);

            window.DockDocumentContextMenu.PlacementTarget = window.DockManager;
            window.DockDocumentContextMenu.IsOpen = true;
            PumpDispatcherUntil(
                window,
                () => window.DockDocumentContextMenu.IsOpen,
                TimeSpan.FromSeconds(2),
                "AvalonDock document context menu open state");
            window.ValidateAvalonDockDocumentContextMenuState(expectedOpen: true);

            window.DockContextCancelNextCloseMenuItem.IsChecked = true;
            window.DockContextCancelNextCloseMenuItem.GetBindingExpression(MenuItem.IsCheckedProperty)?.UpdateSource();
            if (!window.ViewModel.CancelNextOverviewClose)
            {
                throw new InvalidOperationException("Expected AvalonDock context menu checkable item to update close-cancellation binding.");
            }

            window.DockDocumentContextMenu.IsOpen = false;
            PumpDispatcherUntil(
                window,
                () => !window.DockDocumentContextMenu.IsOpen,
                TimeSpan.FromSeconds(2),
                "AvalonDock document context menu closed state");
            window.ValidateAvalonDockDocumentContextMenuState(expectedOpen: false);

            if (window.ViewModel.AccentColor != Colors.MediumSeaGreen ||
                window.ViewModel.Estimate != 42.25m)
            {
                throw new InvalidOperationException("Expected Toolkit popup editor changes to update bindings.");
            }

            window.QuickSearchTextBox.Text = "Application.Run quick search";
            window.QuickSearchTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            window.AccessCodeBox.Password = "run-code";
            window.ViewModel.AccessCode = window.AccessCodeBox.Password;
            window.ReferenceMaskTextBox.Text = "ZX-9876";
            window.ReferenceMaskTextBox.GetBindingExpression(MaskedTextBox.TextProperty)?.UpdateSource();
            window.ReminderTimePicker.Value = DateTime.Today.AddHours(16);
            window.ReviewedAtEditor.Value = DateTime.Today.AddHours(17);
            window.EffortEditor.Value = TimeSpan.FromHours(3);
            window.ByteScoreEditor.Value = 64;
            window.DoubleScaleEditor.Value = 2.5;
            window.WorkItemIdEditor.Value = 8192L;
            window.BudgetEditor.Value = 128.75m;
            window.AccentColorCanvas.SelectedColor = Colors.CadetBlue;
            window.OwnerComboBox.SelectedItem = "ProGPU";
            window.OwnerComboBox.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();
            window.PriorityRangeSlider.LowerValue = 3.0;
            window.PriorityRangeSlider.HigherValue = 7.0;
            window.PriorityRangeSlider.GetBindingExpression(RangeSlider.LowerValueProperty)?.UpdateSource();
            window.PriorityRangeSlider.GetBindingExpression(RangeSlider.HigherValueProperty)?.UpdateSource();
            window.ToolkitRichTextBox.Text = "Application.Run rich notes";
            window.ToolkitRichTextBox.GetBindingExpression(ToolkitRichTextBox.TextProperty)?.UpdateSource();
            window.MultiLineNotesEditor.Text = "Application.Run multiline notes";
            window.MultiLineNotesEditor.GetBindingExpression(MultiLineTextEditor.TextProperty)?.UpdateSource();
            int spinnerCountBefore = window.ViewModel.SpinnerCount;
            window.ExerciseDocumentCountSpinner();
            if (!window.ViewModel.SelectedFlags.Contains("Urgent"))
            {
                window.ViewModel.SelectedFlags.Add("Urgent");
            }

            window.ValidateToolkitInputEditorState();
            if (!string.Equals(window.ViewModel.QuickSearchText, "Application.Run quick search", StringComparison.Ordinal) ||
                !string.Equals(window.ViewModel.AccessCode, "run-code", StringComparison.Ordinal) ||
                !string.Equals(window.ViewModel.ReferenceCode, "ZX-9876", StringComparison.Ordinal) ||
                window.ViewModel.ReminderTime != DateTime.Today.AddHours(16) ||
                window.ViewModel.ReviewedAt != DateTime.Today.AddHours(17) ||
                window.ViewModel.Effort != TimeSpan.FromHours(3) ||
                window.ViewModel.ByteScore != 64 ||
                window.ViewModel.DoubleScale != 2.5 ||
                window.ViewModel.WorkItemId != 8192L ||
                window.ViewModel.Budget != 128.75m ||
                window.ViewModel.AccentColor != Colors.CadetBlue ||
                !string.Equals(window.ViewModel.SelectedOwner, "ProGPU", StringComparison.Ordinal) ||
                window.ViewModel.PriorityRangeStart != 3.0 ||
                window.ViewModel.PriorityRangeEnd != 7.0 ||
                !string.Equals(window.ViewModel.RichNotes, "Application.Run rich notes", StringComparison.Ordinal) ||
                !string.Equals(window.ViewModel.MultiLineNotes, "Application.Run multiline notes", StringComparison.Ordinal) ||
                window.ViewModel.SpinnerCount != spinnerCountBefore ||
                !window.FlagListBox.SelectedItems.Contains("Urgent"))
            {
                throw new InvalidOperationException("Expected Toolkit input editor changes to update bindings and selection state.");
            }
        }

        int themeSwitchCountBefore = window.ViewModel.DockThemeSwitchCount;
        window.CycleDockThemeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateAvalonDockThemeState("Metro");
        window.CycleDockThemeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateAvalonDockThemeState("VS2010");
        window.CycleDockThemeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateAvalonDockThemeState("Aero");
        if (window.ViewModel.DockThemeSwitchCount < themeSwitchCountBefore + 3)
        {
            throw new InvalidOperationException("Expected AvalonDock theme switch count to advance for all package themes.");
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

        if (window.ViewModel.AvalonDockActiveContentChangedCount <= 0)
        {
            throw new InvalidOperationException("Expected AvalonDock ActiveContentChanged event to fire after document activation.");
        }

        int documentCountBeforeCanceledOverviewClose = window.DocumentPane.ChildrenCount;
        int overviewClosedCountBeforeCanceledClose = window.ViewModel.OverviewDocumentClosedCount;
        window.ViewModel.CancelNextOverviewClose = true;
        window.CloseOverviewDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateOverviewCloseCanceledState(
            documentCountBeforeCanceledOverviewClose,
            overviewClosedCountBeforeCanceledClose);

        int documentCountBeforeOverviewClose = window.DocumentPane.ChildrenCount;
        window.CloseOverviewDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateOverviewDocumentLifecycleState(expectedOpen: false);
        if (window.DocumentPane.ChildrenCount != documentCountBeforeOverviewClose - 1 ||
            !string.Equals(window.ViewModel.Status, "Overview document closed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected AvalonDock overview close command to remove the document and update status.");
        }

        int documentCountBeforeOverviewReopen = window.DocumentPane.ChildrenCount;
        window.ReopenOverviewDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateOverviewDocumentLifecycleState(expectedOpen: true);
        if (window.DocumentPane.ChildrenCount != documentCountBeforeOverviewReopen + 1 ||
            !string.Equals(window.ViewModel.Status, "Overview document reopened", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected AvalonDock overview reopen command to restore the document and update status.");
        }

        string bodyBeforeReview = window.ViewModel.SelectedDocument.Body;
        window.ActionDropDownButton.IsOpen = true;
        window.MarkReviewedButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (window.ActionDropDownButton.IsOpen ||
            string.Equals(window.ViewModel.SelectedDocument.Body, bodyBeforeReview, StringComparison.Ordinal) ||
            !string.Equals(window.ViewModel.Status, "Document marked reviewed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Toolkit DropDownButton command to update the selected document and close the dropdown.");
        }

        window.SplitActionButton.IsOpen = true;
        window.AssignSdkOwnerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (window.SplitActionButton.IsOpen ||
            !string.Equals(window.ViewModel.SelectedOwner, "SDK", StringComparison.Ordinal) ||
            !string.Equals(window.ViewModel.Status, "Owner set to SDK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Toolkit SplitButton dropdown command to update owner selection and close the dropdown.");
        }

        window.SplitActionButton.RaiseEvent(new RoutedEventArgs(SplitButton.ClickEvent));
        if (!string.Equals(window.ViewModel.Status, "Applied owner SDK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Toolkit SplitButton primary command to update sample status.");
        }

        if (expectLoaded)
        {
            window.ExerciseToolkitWizard();
        }

        if (expectLoaded)
        {
            window.ToggleEditorFloatButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            PumpDispatcherUntil(
                window,
                () => window.EditorDocument.IsFloating && window.DockLayoutRoot.FloatingWindows.Count == 1,
                TimeSpan.FromSeconds(2),
                "AvalonDock editor document floating window model");
            window.ValidateEditorFloatingState(expectedFloating: true);

            window.ToggleEditorFloatButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            PumpDispatcherUntil(
                window,
                () => !window.EditorDocument.IsFloating && window.DockLayoutRoot.FloatingWindows.Count == 0,
                TimeSpan.FromSeconds(2),
                "AvalonDock editor document docked model");
            window.ValidateEditorFloatingState(expectedFloating: false);
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
            !window.ViewModel.LastSerializedLayout.Contains("ContentId=\"overview\"", StringComparison.Ordinal) ||
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

        window.ValidateAvalonDockLayoutReplacementEvents(window.ViewModel.LastSerializedLayout);

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

    private static void PumpDispatcherUntil(
        DispatcherObject dispatcherObject,
        Func<bool> condition,
        TimeSpan timeout,
        string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            dispatcherObject.Dispatcher.Invoke(
                static () => { },
                DispatcherPriority.Background);

            if (condition())
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException($"Timed out waiting for {description}.");
            }

            System.Threading.Thread.Sleep(1);
        }
    }
}
