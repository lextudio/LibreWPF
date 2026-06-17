using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Media.ProGPU;

internal static class Program
{
    private const string CompilerHarnessAssemblyName = "ProGPU.Wpf.RealXamlCompilerHarness";
    private const string FluentThemeAssemblyName = "PresentationFramework.Fluent";
    private const string AppTypeName = "ProGPU.Wpf.RealXamlCompilerHarness.App";
    private const string MainWindowTypeName = "ProGPU.Wpf.RealXamlCompilerHarness.MainWindow";
    private const string PortableWindowActivationServiceTypeName = "System.Windows.PortableWindowActivationService";
    private const string FluentDictionaryUri = "/PresentationFramework.Fluent;component/Themes/Fluent.xaml";

    [STAThread]
    private static int Main()
    {
        try
        {
            string repoRoot = FindRepoRoot();
            string presentationFrameworkPath = FindArtifactAssembly(repoRoot, "PresentationFramework");
            string presentationCorePath = FindArtifactAssembly(repoRoot, "PresentationCore");
            string compilerHarnessPath = FindArtifactAssembly(repoRoot, CompilerHarnessAssemblyName);
            string fluentThemePath = FindArtifactAssembly(repoRoot, FluentThemeAssemblyName);

            RunHarness(repoRoot, presentationFrameworkPath, presentationCorePath, compilerHarnessPath, fluentThemePath);
            Console.WriteLine("Real WPF Fluent theme runtime smoke succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RunHarness(
        string repoRoot,
        string presentationFrameworkPath,
        string presentationCorePath,
        string compilerHarnessPath,
        string fluentThemePath)
    {
        var loadContext = new WpfAssemblyLoadContext(
            repoRoot,
            presentationFrameworkPath,
            presentationCorePath,
            compilerHarnessPath,
            fluentThemePath);
        Assembly presentationFramework = loadContext.LoadFromAssemblyPath(presentationFrameworkPath);
        Assembly windowsBase = loadContext.LoadFromAssemblyName(new AssemblyName("WindowsBase"));
        loadContext.LoadFromAssemblyPath(fluentThemePath);
        Assembly compilerHarness = loadContext.LoadFromAssemblyPath(compilerHarnessPath);

        object? application = null;
        object? activation = null;
        Type? activationServiceType = null;

        try
        {
            application = Create(compilerHarness, AppTypeName);
            Invoke(application, "InitializeComponent");

            object window = Create(compilerHarness, MainWindowTypeName);
            object themeDictionary = LoadFluentThemeDictionary(presentationFramework);
            MergeThemeDictionary(application, themeDictionary);
            ApplyRepresentativeFluentStyles(presentationFramework, application, window, themeDictionary);
            ValidateThemedRuntimeState(window, application, themeDictionary);
            ValidateThemedVisualReplay(windowsBase, window);

            RegisterPortableActivation(
                presentationFramework,
                window,
                out activationServiceType,
                out activation);
        }
        finally
        {
            if (activation != null)
            {
                Invoke(activation, "Dispose");
            }

            activationServiceType?.GetMethod(
                "Clear",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);

            if (application != null)
            {
                Invoke(application, "Shutdown");
            }

            loadContext.Unload();
        }
    }

    private static object LoadFluentThemeDictionary(Assembly presentationFramework)
    {
        object themeDictionary = Create(presentationFramework, "System.Windows.ResourceDictionary");
        SetProperty(themeDictionary, "Source", new Uri(FluentDictionaryUri, UriKind.Relative));

        object source = GetProperty(themeDictionary, "Source");
        AssertEqual(FluentDictionaryUri, source.ToString(), "Fluent theme dictionary source");
        AssertCollectionCount(GetProperty(themeDictionary, "Keys"), expectedMinimum: 20, "Fluent theme dictionary keys");
        return themeDictionary;
    }

    private static void MergeThemeDictionary(object application, object themeDictionary)
    {
        object resources = GetProperty(application, "Resources");
        AddToCollection(GetProperty(resources, "MergedDictionaries"), themeDictionary);
        AssertCollectionCount(GetProperty(resources, "MergedDictionaries"), expectedMinimum: 1, "application merged dictionaries");
    }

    private static void ApplyRepresentativeFluentStyles(
        Assembly presentationFramework,
        object application,
        object window,
        object themeDictionary)
    {
        object windowStyle = GetDictionaryValue(themeDictionary, "DefaultWindowStyle");
        object buttonStyle = GetDictionaryValue(themeDictionary, "AccentButtonStyle");
        object calendarStyle = GetDictionaryValue(themeDictionary, "DefaultCalendarStyle");
        object comboBoxStyle = GetDictionaryValue(themeDictionary, "DefaultComboBoxStyle");
        object datePickerStyle = GetDictionaryValue(themeDictionary, "DefaultDatePickerStyle");
        object listViewStyle = GetDictionaryValue(themeDictionary, "DefaultListViewStyle");
        object listViewItemStyle = GetDictionaryValue(themeDictionary, "DefaultListViewItemStyle");
        object passwordBoxStyle = GetDictionaryValue(themeDictionary, "DefaultPasswordBoxStyle");
        object tabControlStyle = GetDictionaryValue(themeDictionary, "DefaultTabControlStyle");
        object tabItemStyle = GetDictionaryValue(themeDictionary, "DefaultTabItemStyle");
        object textBoxStyle = GetDictionaryValue(themeDictionary, "DefaultTextBoxStyle");
        object treeViewStyle = GetDictionaryValue(themeDictionary, "DefaultTreeViewStyle");
        object treeViewItemStyle = GetDictionaryValue(themeDictionary, "DefaultTreeViewItemStyle");
        object richTextBoxStyle = GetDictionaryValue(themeDictionary, "DefaultRichTextBoxStyle");
        Type calendarType = GetRequiredType(presentationFramework, "System.Windows.Controls.Calendar");
        Type comboBoxType = GetRequiredType(presentationFramework, "System.Windows.Controls.ComboBox");
        Type datePickerType = GetRequiredType(presentationFramework, "System.Windows.Controls.DatePicker");
        Type listViewType = GetRequiredType(presentationFramework, "System.Windows.Controls.ListView");
        Type passwordBoxType = GetRequiredType(presentationFramework, "System.Windows.Controls.PasswordBox");
        Type sliderType = GetRequiredType(presentationFramework, "System.Windows.Controls.Slider");
        Type tabControlType = GetRequiredType(presentationFramework, "System.Windows.Controls.TabControl");
        Type treeViewType = GetRequiredType(presentationFramework, "System.Windows.Controls.TreeView");
        Type progressBarType = GetRequiredType(presentationFramework, "System.Windows.Controls.ProgressBar");
        object sliderStyle = GetDictionaryValue(themeDictionary, sliderType);
        object progressBarStyle = GetDictionaryValue(themeDictionary, progressBarType);

        SetProperty(window, "Style", windowStyle);

        object content = GetProperty(window, "Content");
        object children = GetProperty(content, "Children");
        object richTextBox = Invoke(window, "FindName", "DocumentBox");
        AssertType(richTextBox, "System.Windows.Controls.RichTextBox", "compiled themed RichTextBox");
        SetProperty(richTextBox, "Style", richTextBoxStyle);

        object button = Create(presentationFramework, "System.Windows.Controls.Button");
        SetProperty(button, "Content", "themed button smoke");
        SetProperty(button, "Style", buttonStyle);
        AddToCollection(children, button);

        object textBox = Create(presentationFramework, "System.Windows.Controls.TextBox");
        SetProperty(textBox, "Text", "themed text box smoke");
        SetProperty(textBox, "Style", textBoxStyle);
        AddToCollection(children, textBox);

        object tabControl = Create(presentationFramework, "System.Windows.Controls.TabControl");
        object tabItems = GetProperty(tabControl, "Items");
        object firstTabItem = Create(presentationFramework, "System.Windows.Controls.TabItem");
        SetProperty(firstTabItem, "Header", "Theme tab one");
        SetProperty(firstTabItem, "Content", "Theme tab content one");
        SetProperty(firstTabItem, "Style", tabItemStyle);
        AddToCollection(tabItems, firstTabItem);
        object secondTabItem = Create(presentationFramework, "System.Windows.Controls.TabItem");
        SetProperty(secondTabItem, "Header", "Theme tab two");
        SetProperty(secondTabItem, "Content", "Theme tab content two");
        SetProperty(secondTabItem, "Style", tabItemStyle);
        AddToCollection(tabItems, secondTabItem);
        SetProperty(tabControl, "SelectedIndex", 1);
        SetProperty(tabControl, "Style", tabControlStyle);
        AddToCollection(children, tabControl);

        object listView = Create(presentationFramework, "System.Windows.Controls.ListView");
        object listViewItems = GetProperty(listView, "Items");
        object firstListViewItem = Create(presentationFramework, "System.Windows.Controls.ListViewItem");
        SetProperty(firstListViewItem, "Content", "Theme list item one");
        SetProperty(firstListViewItem, "Style", listViewItemStyle);
        AddToCollection(listViewItems, firstListViewItem);
        object secondListViewItem = Create(presentationFramework, "System.Windows.Controls.ListViewItem");
        SetProperty(secondListViewItem, "Content", "Theme list item two");
        SetProperty(secondListViewItem, "Style", listViewItemStyle);
        AddToCollection(listViewItems, secondListViewItem);
        SetProperty(listView, "SelectedIndex", 1);
        SetProperty(listView, "Style", listViewStyle);
        AddToCollection(children, listView);

        object treeView = Create(presentationFramework, "System.Windows.Controls.TreeView");
        object treeViewItems = GetProperty(treeView, "Items");
        object rootTreeViewItem = Create(presentationFramework, "System.Windows.Controls.TreeViewItem");
        SetProperty(rootTreeViewItem, "Header", "Theme tree root");
        SetProperty(rootTreeViewItem, "IsExpanded", true);
        SetProperty(rootTreeViewItem, "Style", treeViewItemStyle);
        object childTreeViewItem = Create(presentationFramework, "System.Windows.Controls.TreeViewItem");
        SetProperty(childTreeViewItem, "Header", "Theme tree child");
        SetProperty(childTreeViewItem, "Style", treeViewItemStyle);
        AddToCollection(GetProperty(rootTreeViewItem, "Items"), childTreeViewItem);
        AddToCollection(treeViewItems, rootTreeViewItem);
        SetProperty(treeView, "Style", treeViewStyle);
        AddToCollection(children, treeView);

        DateTime themeDate = new(2026, 1, 7);

        object calendar = Create(presentationFramework, "System.Windows.Controls.Calendar");
        SetProperty(calendar, "DisplayDate", themeDate);
        SetProperty(calendar, "SelectedDate", themeDate);
        SetEnumProperty(calendar, "FirstDayOfWeek", "Monday");
        SetProperty(calendar, "Style", calendarStyle);
        AddToCollection(children, calendar);

        object datePicker = Create(presentationFramework, "System.Windows.Controls.DatePicker");
        SetProperty(datePicker, "DisplayDate", themeDate);
        SetProperty(datePicker, "SelectedDate", themeDate);
        SetProperty(datePicker, "Style", datePickerStyle);
        AddToCollection(children, datePicker);

        object comboBox = Create(presentationFramework, "System.Windows.Controls.ComboBox");
        object comboBoxItems = GetProperty(comboBox, "Items");
        AddToCollection(comboBoxItems, "theme item one");
        AddToCollection(comboBoxItems, "theme item two");
        SetProperty(comboBox, "SelectedIndex", 1);
        SetProperty(comboBox, "Style", comboBoxStyle);
        AddToCollection(children, comboBox);

        object passwordBox = Create(presentationFramework, "System.Windows.Controls.PasswordBox");
        SetProperty(passwordBox, "Password", "theme-secret");
        SetProperty(passwordBox, "Style", passwordBoxStyle);
        AddToCollection(children, passwordBox);

        object slider = Create(presentationFramework, "System.Windows.Controls.Slider");
        SetProperty(slider, "Minimum", 0.0);
        SetProperty(slider, "Maximum", 100.0);
        SetProperty(slider, "Value", 42.0);
        SetProperty(slider, "Style", sliderStyle);
        AddToCollection(children, slider);

        object progressBar = Create(presentationFramework, "System.Windows.Controls.ProgressBar");
        SetProperty(progressBar, "Minimum", 0.0);
        SetProperty(progressBar, "Maximum", 100.0);
        SetProperty(progressBar, "Value", 64.0);
        SetProperty(progressBar, "Style", progressBarStyle);
        AddToCollection(children, progressBar);

        AssertSame(windowStyle, GetProperty(window, "Style"), "Window Fluent style");
        AssertSame(buttonStyle, GetProperty(button, "Style"), "Button Fluent style");
        AssertSame(textBoxStyle, GetProperty(textBox, "Style"), "TextBox Fluent style");
        AssertSame(tabControlStyle, GetProperty(tabControl, "Style"), "TabControl Fluent style");
        AssertSame(listViewStyle, GetProperty(listView, "Style"), "ListView Fluent style");
        AssertSame(treeViewStyle, GetProperty(treeView, "Style"), "TreeView Fluent style");
        AssertSame(calendarStyle, GetProperty(calendar, "Style"), "Calendar Fluent style");
        AssertSame(datePickerStyle, GetProperty(datePicker, "Style"), "DatePicker Fluent style");
        AssertSame(comboBoxStyle, GetProperty(comboBox, "Style"), "ComboBox Fluent style");
        AssertSame(passwordBoxStyle, GetProperty(passwordBox, "Style"), "PasswordBox Fluent style");
        AssertSame(sliderStyle, GetProperty(slider, "Style"), "Slider Fluent style");
        AssertSame(progressBarStyle, GetProperty(progressBar, "Style"), "ProgressBar Fluent style");
        AssertSame(richTextBoxStyle, GetProperty(richTextBox, "Style"), "RichTextBox Fluent style");
        AssertSame(buttonStyle, Invoke(application, "TryFindResource", "AccentButtonStyle"), "application Fluent resource lookup");
        AssertSame(textBoxStyle, Invoke(application, "TryFindResource", "DefaultTextBoxStyle"), "application Fluent TextBox resource lookup");
        AssertSame(calendarStyle, Invoke(application, "TryFindResource", "DefaultCalendarStyle"), "application Fluent Calendar resource lookup");
        AssertSame(comboBoxStyle, Invoke(application, "TryFindResource", "DefaultComboBoxStyle"), "application Fluent ComboBox resource lookup");
        AssertSame(datePickerStyle, Invoke(application, "TryFindResource", "DefaultDatePickerStyle"), "application Fluent DatePicker resource lookup");
        AssertType(Invoke(application, "TryFindResource", calendarType), "System.Windows.Style", "application Fluent Calendar implicit style lookup");
        AssertSame(passwordBoxStyle, Invoke(application, "TryFindResource", "DefaultPasswordBoxStyle"), "application Fluent PasswordBox resource lookup");
        AssertType(Invoke(application, "TryFindResource", comboBoxType), "System.Windows.Style", "application Fluent ComboBox implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", datePickerType), "System.Windows.Style", "application Fluent DatePicker implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", listViewType), "System.Windows.Style", "application Fluent ListView implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", passwordBoxType), "System.Windows.Style", "application Fluent PasswordBox implicit style lookup");
        AssertSame(sliderStyle, Invoke(application, "TryFindResource", sliderType), "application Fluent Slider implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", tabControlType), "System.Windows.Style", "application Fluent TabControl implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", treeViewType), "System.Windows.Style", "application Fluent TreeView implicit style lookup");
        AssertSame(progressBarStyle, Invoke(application, "TryFindResource", progressBarType), "application Fluent ProgressBar implicit style lookup");
    }

    private static void ValidateThemedRuntimeState(object window, object application, object themeDictionary)
    {
        object content = GetProperty(window, "Content");
        object children = GetProperty(content, "Children");
        AssertCollectionCount(children, expectedMinimum: 24, "themed stack panel children");

        int childCount = GetCollectionCount(children);
        object button = GetCollectionItem(children, childCount - 11);
        object textBox = GetCollectionItem(children, childCount - 10);
        object tabControl = GetCollectionItem(children, childCount - 9);
        object listView = GetCollectionItem(children, childCount - 8);
        object treeView = GetCollectionItem(children, childCount - 7);
        object calendar = GetCollectionItem(children, childCount - 6);
        object datePicker = GetCollectionItem(children, childCount - 5);
        object comboBox = GetCollectionItem(children, childCount - 4);
        object passwordBox = GetCollectionItem(children, childCount - 3);
        object slider = GetCollectionItem(children, childCount - 2);
        object progressBar = GetCollectionItem(children, childCount - 1);
        object richTextBox = Invoke(window, "FindName", "DocumentBox");
        DateTime themeDate = new(2026, 1, 7);
        AssertType(richTextBox, "System.Windows.Controls.RichTextBox", "compiled themed RichTextBox");
        AssertType(textBox, "System.Windows.Controls.TextBox", "created themed TextBox");
        AssertType(tabControl, "System.Windows.Controls.TabControl", "created themed TabControl");
        AssertType(listView, "System.Windows.Controls.ListView", "created themed ListView");
        AssertType(treeView, "System.Windows.Controls.TreeView", "created themed TreeView");
        AssertType(calendar, "System.Windows.Controls.Calendar", "created themed Calendar");
        AssertType(datePicker, "System.Windows.Controls.DatePicker", "created themed DatePicker");
        AssertType(comboBox, "System.Windows.Controls.ComboBox", "created themed ComboBox");
        AssertType(passwordBox, "System.Windows.Controls.PasswordBox", "created themed PasswordBox");
        AssertType(slider, "System.Windows.Controls.Slider", "created themed Slider");
        AssertType(progressBar, "System.Windows.Controls.ProgressBar", "created themed ProgressBar");

        AssertType(GetDictionaryValue(themeDictionary, "DefaultWindowStyle"), "System.Windows.Style", "DefaultWindowStyle");
        AssertType(GetDictionaryValue(themeDictionary, "AccentButtonStyle"), "System.Windows.Style", "AccentButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultCalendarStyle"), "System.Windows.Style", "DefaultCalendarStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultCalendarButtonStyle"), "System.Windows.Style", "DefaultCalendarButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultCalendarDayButtonStyle"), "System.Windows.Style", "DefaultCalendarDayButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultCalendarItemStyle"), "System.Windows.Style", "DefaultCalendarItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultComboBoxStyle"), "System.Windows.Style", "DefaultComboBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultComboBoxItemStyle"), "System.Windows.Style", "DefaultComboBoxItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultComboBoxTextBoxStyle"), "System.Windows.Style", "DefaultComboBoxTextBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultComboBoxToggleButtonStyle"), "System.Windows.Style", "DefaultComboBoxToggleButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultComboBoxTemplate"), "System.Windows.Controls.ControlTemplate", "DefaultComboBoxTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "EditableComboBoxTemplate"), "System.Windows.Controls.ControlTemplate", "EditableComboBoxTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDatePickerStyle"), "System.Windows.Style", "DefaultDatePickerStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DatePickerCalendarStyle"), "System.Windows.Style", "DatePickerCalendarStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDatePickerTextBoxStyle"), "System.Windows.Style", "DefaultDatePickerTextBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultListViewStyle"), "System.Windows.Style", "DefaultListViewStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultListViewItemStyle"), "System.Windows.Style", "DefaultListViewItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "ListViewTemplate"), "System.Windows.Controls.ControlTemplate", "ListViewTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultPasswordBoxStyle"), "System.Windows.Style", "DefaultPasswordBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultPasswordBoxContextMenu"), "System.Windows.Controls.ContextMenu", "DefaultPasswordBoxContextMenu");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTabControlStyle"), "System.Windows.Style", "DefaultTabControlStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTabItemStyle"), "System.Windows.Style", "DefaultTabItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTopTabControlStyle"), "System.Windows.Controls.ControlTemplate", "DefaultTopTabControlStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultBottomTabControlStyle"), "System.Windows.Controls.ControlTemplate", "DefaultBottomTabControlStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultLeftTabControlStyle"), "System.Windows.Controls.ControlTemplate", "DefaultLeftTabControlStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultRightTabControlStyle"), "System.Windows.Controls.ControlTemplate", "DefaultRightTabControlStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTextBoxStyle"), "System.Windows.Style", "DefaultTextBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTextBoxControlTemplate"), "System.Windows.Controls.ControlTemplate", "DefaultTextBoxControlTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTreeViewStyle"), "System.Windows.Style", "DefaultTreeViewStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTreeViewItemStyle"), "System.Windows.Style", "DefaultTreeViewItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultRichTextBoxStyle"), "System.Windows.Style", "DefaultRichTextBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, calendar.GetType()), "System.Windows.Style", "implicit Calendar Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, comboBox.GetType()), "System.Windows.Style", "implicit ComboBox Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, datePicker.GetType()), "System.Windows.Style", "implicit DatePicker Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, listView.GetType()), "System.Windows.Style", "implicit ListView Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, passwordBox.GetType()), "System.Windows.Style", "implicit PasswordBox Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, slider.GetType()), "System.Windows.Style", "implicit Slider Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, tabControl.GetType()), "System.Windows.Style", "implicit TabControl Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, treeView.GetType()), "System.Windows.Style", "implicit TreeView Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, progressBar.GetType()), "System.Windows.Style", "implicit ProgressBar Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, "HorizontalSliderTemplate"), "System.Windows.Controls.ControlTemplate", "HorizontalSliderTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "VerticalSliderTemplate"), "System.Windows.Controls.ControlTemplate", "VerticalSliderTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "SliderThumbStyle"), "System.Windows.Style", "SliderThumbStyle");
        AssertType(GetDictionaryValue(themeDictionary, "SliderButtonStyle"), "System.Windows.Style", "SliderButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "WindowTemplateKey"), "System.Windows.Controls.ControlTemplate", "WindowTemplateKey");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultControlContextMenu"), "System.Windows.Controls.ContextMenu", "DefaultControlContextMenu");

        AssertStyleTarget(GetProperty(window, "Style"), "System.Windows.Window", "Window Fluent style target");
        AssertStyleTarget(GetProperty(button, "Style"), "System.Windows.Controls.Button", "Button Fluent style target");
        AssertStyleTarget(GetProperty(textBox, "Style"), "System.Windows.Controls.TextBox", "TextBox Fluent style target");
        AssertStyleTarget(GetProperty(tabControl, "Style"), "System.Windows.Controls.TabControl", "TabControl Fluent style target");
        AssertStyleTarget(GetProperty(listView, "Style"), "System.Windows.Controls.ListView", "ListView Fluent style target");
        AssertStyleTarget(GetProperty(treeView, "Style"), "System.Windows.Controls.TreeView", "TreeView Fluent style target");
        AssertStyleTarget(GetProperty(calendar, "Style"), "System.Windows.Controls.Calendar", "Calendar Fluent style target");
        AssertStyleTarget(GetProperty(datePicker, "Style"), "System.Windows.Controls.DatePicker", "DatePicker Fluent style target");
        AssertStyleTarget(GetProperty(comboBox, "Style"), "System.Windows.Controls.ComboBox", "ComboBox Fluent style target");
        AssertStyleTarget(GetProperty(passwordBox, "Style"), "System.Windows.Controls.PasswordBox", "PasswordBox Fluent style target");
        AssertStyleTarget(GetProperty(slider, "Style"), "System.Windows.Controls.Slider", "Slider Fluent style target");
        AssertStyleTarget(GetProperty(progressBar, "Style"), "System.Windows.Controls.ProgressBar", "ProgressBar Fluent style target");
        AssertStyleTarget(GetProperty(richTextBox, "Style"), "System.Windows.Controls.RichTextBox", "RichTextBox Fluent style target");

        Invoke(window, "ApplyTemplate");
        Invoke(button, "ApplyTemplate");
        Invoke(textBox, "ApplyTemplate");
        Invoke(tabControl, "ApplyTemplate");
        ApplyItemsTemplates(tabControl, "themed TabControl items");
        Invoke(listView, "ApplyTemplate");
        ApplyItemsTemplates(listView, "themed ListView items");
        Invoke(treeView, "ApplyTemplate");
        ApplyItemsTemplates(treeView, "themed TreeView root items");
        Invoke(calendar, "ApplyTemplate");
        Invoke(datePicker, "ApplyTemplate");
        Invoke(comboBox, "ApplyTemplate");
        Invoke(passwordBox, "ApplyTemplate");
        Invoke(slider, "ApplyTemplate");
        Invoke(progressBar, "ApplyTemplate");
        Invoke(richTextBox, "ApplyTemplate");

        AssertType(GetProperty(window, "Template"), "System.Windows.Controls.ControlTemplate", "Window template");
        AssertType(GetProperty(button, "Template"), "System.Windows.Controls.ControlTemplate", "Button template");
        AssertType(GetProperty(textBox, "Template"), "System.Windows.Controls.ControlTemplate", "TextBox template");
        AssertType(GetProperty(tabControl, "Template"), "System.Windows.Controls.ControlTemplate", "TabControl template");
        AssertType(GetProperty(listView, "Template"), "System.Windows.Controls.ControlTemplate", "ListView template");
        AssertType(GetProperty(treeView, "Template"), "System.Windows.Controls.ControlTemplate", "TreeView template");
        AssertType(GetProperty(calendar, "Template"), "System.Windows.Controls.ControlTemplate", "Calendar template");
        AssertType(GetProperty(datePicker, "Template"), "System.Windows.Controls.ControlTemplate", "DatePicker template");
        AssertType(GetProperty(comboBox, "Template"), "System.Windows.Controls.ControlTemplate", "ComboBox template");
        AssertType(GetProperty(passwordBox, "Template"), "System.Windows.Controls.ControlTemplate", "PasswordBox template");
        AssertType(GetProperty(slider, "Template"), "System.Windows.Controls.ControlTemplate", "Slider template");
        AssertType(GetProperty(progressBar, "Template"), "System.Windows.Controls.ControlTemplate", "ProgressBar template");
        AssertType(GetProperty(richTextBox, "Template"), "System.Windows.Controls.ControlTemplate", "RichTextBox template");
        AssertStyleHasSetter(GetProperty(tabControl, "Style"), "Template", "TabControl Fluent template setter");
        AssertStyleHasSetter(GetProperty(listView, "Style"), "Template", "ListView Fluent template setter");
        AssertStyleHasSetter(GetProperty(treeView, "Style"), "Template", "TreeView Fluent template setter");
        AssertStyleHasSetter(GetProperty(calendar, "Style"), "Template", "Calendar Fluent template setter");
        AssertStyleHasSetter(GetProperty(datePicker, "Style"), "Template", "DatePicker Fluent template setter");
        AssertStyleHasSetter(GetProperty(comboBox, "Style"), "Template", "ComboBox Fluent template setter");
        AssertStyleHasSetter(GetProperty(passwordBox, "Style"), "Template", "PasswordBox Fluent template setter");
        AssertStyleHasSetter(GetProperty(textBox, "Style"), "Template", "TextBox Fluent template setter");
        AssertStyleHasSetter(GetProperty(progressBar, "Style"), "Template", "ProgressBar Fluent template setter");
        AssertStyleHasSetter(GetProperty(richTextBox, "Style"), "ContextMenu", "RichTextBox Fluent context-menu setter");
        AssertEqual("themed button smoke", GetProperty(button, "Content"), "themed button content");
        AssertEqual("themed text box smoke", GetProperty(textBox, "Text"), "themed TextBox text");
        AssertEqual(2, GetCollectionCount(GetProperty(tabControl, "Items")), "themed TabControl item count");
        AssertEqual(1, GetProperty(tabControl, "SelectedIndex"), "themed TabControl selected index");
        AssertEqual("Theme tab two", GetProperty(GetCollectionItem(GetProperty(tabControl, "Items"), 1), "Header"), "themed TabItem header");
        AssertEqual(2, GetCollectionCount(GetProperty(listView, "Items")), "themed ListView item count");
        AssertEqual(1, GetProperty(listView, "SelectedIndex"), "themed ListView selected index");
        AssertEqual("Theme list item two", GetProperty(GetCollectionItem(GetProperty(listView, "Items"), 1), "Content"), "themed ListViewItem content");
        AssertEqual(1, GetCollectionCount(GetProperty(treeView, "Items")), "themed TreeView root item count");
        object rootTreeViewItem = GetCollectionItem(GetProperty(treeView, "Items"), 0);
        AssertEqual("Theme tree root", GetProperty(rootTreeViewItem, "Header"), "themed TreeViewItem root header");
        AssertEqual(true, GetProperty(rootTreeViewItem, "IsExpanded"), "themed TreeViewItem expanded state");
        AssertEqual("Theme tree child", GetProperty(GetCollectionItem(GetProperty(rootTreeViewItem, "Items"), 0), "Header"), "themed TreeViewItem child header");
        AssertEqual(themeDate, GetProperty(calendar, "DisplayDate"), "themed Calendar display date");
        AssertEqual(themeDate, GetProperty(calendar, "SelectedDate"), "themed Calendar selected date");
        AssertEqual("Monday", GetProperty(calendar, "FirstDayOfWeek").ToString(), "themed Calendar first day");
        AssertEqual(themeDate, GetProperty(datePicker, "DisplayDate"), "themed DatePicker display date");
        AssertEqual(themeDate, GetProperty(datePicker, "SelectedDate"), "themed DatePicker selected date");
        AssertEqual(2, GetCollectionCount(GetProperty(comboBox, "Items")), "themed ComboBox item count");
        AssertEqual(1, GetProperty(comboBox, "SelectedIndex"), "themed ComboBox selected index");
        AssertEqual("theme item two", GetProperty(comboBox, "SelectedItem"), "themed ComboBox selected item");
        AssertEqual("theme-secret", GetProperty(passwordBox, "Password"), "themed PasswordBox password");
        AssertEqual(0.0, GetProperty(slider, "Minimum"), "themed Slider minimum");
        AssertEqual(100.0, GetProperty(slider, "Maximum"), "themed Slider maximum");
        AssertEqual(42.0, GetProperty(slider, "Value"), "themed Slider value");
        AssertEqual(64.0, GetProperty(progressBar, "Value"), "themed ProgressBar value");

        object appResources = GetProperty(application, "Resources");
        object mergedDictionaries = GetProperty(appResources, "MergedDictionaries");
        AssertCollectionCount(mergedDictionaries, expectedMinimum: 2, "application merged dictionaries after Fluent merge");
        AssertCollectionContainsSame(mergedDictionaries, themeDictionary, "merged Fluent dictionary");
    }

    private static void ValidateThemedVisualReplay(Assembly windowsBase, object window)
    {
        const uint pixelWidth = 420;
        const uint pixelHeight = 260;

        object content = GetProperty(window, "Content");

        MeasureAndArrange(windowsBase, content, pixelWidth, pixelHeight);

        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var replayResult = target.ReplayVisualSubtreeRetained(content, pixelWidth, pixelHeight);

        AssertAtLeast(1, replayResult.VisualCount, "Fluent themed visual replay count");
        AssertAtLeast(1, replayResult.ContentCount, "Fluent themed visual replay content count");
        AssertAtLeast(1, replayResult.RenderData.AppliedCount, "Fluent themed render-data applied commands");
        AssertAtLeast(1, replayResult.ChildEdgeCount, "Fluent themed visual child edges");
        AssertAtLeast(1, target.RetainedVisualBranchCount, "retained Fluent themed visual branch map");
        AssertAtLeast(1, target.RetainedWpfVisualRoot.Children.Count, "retained Fluent themed visual root children");
        AssertAtLeast(1, CountRetainedCommands(target.RetainedWpfVisualRoot), "retained Fluent themed ProGPU commands");
    }

    private static void MeasureAndArrange(Assembly windowsBase, object element, double width, double height)
    {
        object availableSize = Create(windowsBase, "System.Windows.Size", width, height);
        object finalRect = Create(windowsBase, "System.Windows.Rect", 0.0, 0.0, width, height);

        Invoke(element, "Measure", availableSize);
        Invoke(element, "Arrange", finalRect);
        Invoke(element, "UpdateLayout");

        AssertPositiveSize(GetProperty(element, "DesiredSize"), "themed content desired size");
        AssertPositiveSize(GetProperty(element, "RenderSize"), "themed content render size");
    }

    private static void ApplyItemsTemplates(object itemsOwner, string description)
    {
        object items = GetProperty(itemsOwner, "Items");
        AssertCollectionCount(items, expectedMinimum: 1, description);

        int count = GetCollectionCount(items);
        for (int i = 0; i < count; i++)
        {
            object item = GetCollectionItem(items, i);
            Invoke(item, "ApplyTemplate");

            object? childItems = GetOptionalProperty(item, "Items");
            if (childItems != null && GetCollectionCount(childItems) > 0)
            {
                ApplyItemsTemplates(item, $"{description} child items");
            }
        }
    }

    private static void RegisterPortableActivation(
        Assembly presentationFramework,
        object window,
        out Type activationServiceType,
        out object activation)
    {
        if (!WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation(
                presentationFramework,
                hostFactory: w => new ProGpuWpfWindowHost(WpfPortableWindowActivation.CreateHostOptions(w))))
        {
            throw new InvalidOperationException("Failed to register ProGPU portable activation with real PresentationFramework.");
        }

        activationServiceType = GetRequiredType(presentationFramework, PortableWindowActivationServiceTypeName);
        AssertEqual(true, GetStaticProperty(activationServiceType, "IsEnabled"), "portable activation enabled");

        MethodInfo tryActivate = activationServiceType.GetMethod(
            "TryActivate",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(activationServiceType.FullName, "TryActivate");
        object?[] parameters = { window, null };
        if (!Equals(true, tryActivate.Invoke(null, parameters)) || parameters[1] == null)
        {
            throw new InvalidOperationException("Real themed WPF window did not create a portable ProGPU activation.");
        }

        activation = parameters[1]!;
        if (activation is not WpfPortableWindowActivation portableActivation)
        {
            throw new InvalidOperationException($"Expected a ProGPU activation, got {activation.GetType().FullName}.");
        }

        AssertSame(window, portableActivation.Window, "activation window");
        AssertEqual("ProGPU WPF XAML smoke", portableActivation.Host.Title, "host title");
        AssertEqual(420, portableActivation.Host.Width, "host width");
        AssertEqual(260, portableActivation.Host.Height, "host height");
    }

    private static object Create(Assembly assembly, string typeName, params object?[] parameters)
    {
        Type type = GetRequiredType(assembly, typeName);
        return Activator.CreateInstance(type, parameters)
            ?? throw new InvalidOperationException($"Failed to create '{typeName}'.");
    }

    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)
            ?? throw new TypeLoadException($"Could not load '{typeName}' from '{assembly.FullName}'.");
    }

    private static object GetProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Expected '{instance.GetType().FullName}.{propertyName}' to have a value.");
    }

    private static object GetStaticProperty(Type type, string propertyName)
    {
        return type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException($"Expected '{type.FullName}.{propertyName}' to have a value.");
    }

    private static object GetDictionaryValue(object dictionary, object key)
    {
        if (dictionary is IDictionary nonGenericDictionary && nonGenericDictionary.Contains(key))
        {
            return nonGenericDictionary[key]
                ?? throw new InvalidOperationException($"Dictionary key '{key}' had a null value.");
        }

        object value = Invoke(dictionary, "get_Item", key);
        if (value == null)
        {
            throw new InvalidOperationException($"Dictionary key '{key}' had a null value.");
        }

        return value;
    }

    private static object GetCollectionItem(object collection, int index)
    {
        if (collection is IList list)
        {
            return list[index]
                ?? throw new InvalidOperationException($"Collection item {index} had a null value.");
        }

        return Invoke(collection, "get_Item", index);
    }

    private static object Invoke(object instance, string methodName, params object?[] parameters)
    {
        MethodInfo method = instance.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] candidateParameters = candidate.GetParameters();
                return candidateParameters.Length == parameters.Length;
            })
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        return method.Invoke(instance, parameters) ?? new object();
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        property.SetValue(instance, value);
    }

    private static void SetEnumProperty(object instance, string propertyName, string value)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        property.SetValue(instance, Enum.Parse(property.PropertyType, value));
    }

    private static void AddToCollection(object collection, object item)
    {
        MethodInfo add = collection.GetType().GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { item.GetType() },
            modifiers: null)
            ?? collection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method =>
                    method.Name == "Add" &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType.IsAssignableFrom(item.GetType()))
            ?? throw new MissingMethodException(collection.GetType().FullName, "Add");
        add.Invoke(collection, new[] { item });
    }

    private static void AssertCollectionCount(object collection, int expectedMinimum, string description)
    {
        int count = GetCollectionCount(collection);
        if (count < expectedMinimum)
        {
            throw new InvalidOperationException($"Expected {description} to contain at least {expectedMinimum} items, got {count}.");
        }
    }

    private static int GetCollectionCount(object collection)
    {
        object countValue =
            collection is Array array ? array.Length :
            collection is ICollection nonGenericCollection ? nonGenericCollection.Count :
            GetProperty(collection, "Count");

        return Convert.ToInt32(countValue);
    }

    private static void AssertStyleTarget(object style, string expectedTargetTypeName, string description)
    {
        object targetType = GetProperty(style, "TargetType");
        AssertEqual(expectedTargetTypeName, targetType.ToString(), description);
    }

    private static void AssertStyleHasSetter(object style, string dependencyPropertyName, string description)
    {
        object setters = GetProperty(style, "Setters");
        if (setters is not IEnumerable enumerable)
        {
            throw new InvalidOperationException($"Expected {description} to expose enumerable setters.");
        }

        foreach (object setterBase in enumerable)
        {
            if (!string.Equals(setterBase.GetType().FullName, "System.Windows.Setter", StringComparison.Ordinal))
            {
                continue;
            }

            object property = GetProperty(setterBase, "Property");
            if (string.Equals(GetProperty(property, "Name").ToString(), dependencyPropertyName, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException($"Expected {description} to include a '{dependencyPropertyName}' setter.");
    }

    private static void AssertType(object instance, string expectedFullName, string description)
    {
        if (!string.Equals(instance.GetType().FullName, expectedFullName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expectedFullName}', got '{instance.GetType().FullName}'.");
        }
    }

    private static void AssertPositiveSize(object size, string description)
    {
        double width = Convert.ToDouble(GetProperty(size, "Width"));
        double height = Convert.ToDouble(GetProperty(size, "Height"));
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be positive, got {width}x{height}.");
        }
    }

    private static void AssertAtLeast(int expectedMinimum, int actual, string description)
    {
        if (actual < expectedMinimum)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be at least {expectedMinimum}, got {actual}.");
        }
    }

    private static int CountRetainedCommands(object visual)
    {
        return CountRetainedCommands(visual, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static int CountRetainedCommands(object visual, ISet<object> visited)
    {
        if (!visited.Add(visual))
        {
            return 0;
        }

        int count = GetRetainedCommandCount(visual);
        PropertyInfo? childrenProperty = visual.GetType().GetProperty(
            "Children",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (childrenProperty?.GetValue(visual) is IEnumerable children)
        {
            foreach (object? child in children)
            {
                if (child != null)
                {
                    count += CountRetainedCommands(child, visited);
                }
            }
        }

        return count;
    }

    private static int GetRetainedCommandCount(object visual)
    {
        PropertyInfo? contextProperty = visual.GetType().GetProperty(
            "Context",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? context = contextProperty?.GetValue(visual);
        if (context == null)
        {
            return 0;
        }

        PropertyInfo? commandsProperty = context.GetType().GetProperty(
            "Commands",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? commands = commandsProperty?.GetValue(context);
        if (commands is ICollection nonGenericCollection)
        {
            return nonGenericCollection.Count;
        }

        object? count = commands == null ? null : GetOptionalProperty(commands, "Count");
        return count == null ? 0 : Convert.ToInt32(count);
    }

    private static object? GetOptionalProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
    }

    private static void AssertSame(object expected, object actual, string description)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to reference the same object.");
        }
    }

    private static void AssertCollectionContainsSame(object collection, object expected, string description)
    {
        if (collection is IEnumerable items)
        {
            foreach (object? item in items)
            {
                if (ReferenceEquals(expected, item))
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException($"Expected {description} to be present in the collection.");
    }

    private static void AssertEqual(object? expected, object? actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to be '{expected}', got '{actual}'.");
        }
    }

    private static string FindArtifactAssembly(string repoRoot, string assemblyName)
    {
        string artifactsRoot = Path.Combine(repoRoot, "artifacts", "bin", assemblyName);
        if (!Directory.Exists(artifactsRoot))
        {
            throw new DirectoryNotFoundException($"Artifacts directory was not found: {artifactsRoot}");
        }

        string[] candidates = Directory.GetFiles(
            artifactsRoot,
            $"{assemblyName}.dll",
            SearchOption.AllDirectories);

        string? selected = candidates
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}net11.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return selected
            ?? throw new FileNotFoundException($"Could not locate a net11.0 {assemblyName}.dll artifact.", artifactsRoot);
    }

    private static string? TryFindArtifactAssembly(string repoRoot, AssemblyName assemblyName)
    {
        if (assemblyName.Name == null)
        {
            return null;
        }

        string artifactsRoot = Path.Combine(repoRoot, "artifacts", "bin", assemblyName.Name);
        if (!Directory.Exists(artifactsRoot))
        {
            return null;
        }

        return Directory
            .GetFiles(artifactsRoot, $"{assemblyName.Name}.dll", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}net11.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string marker = Path.Combine(
                directory.FullName,
                "src",
                "Microsoft.DotNet.Wpf",
                "src",
                "PresentationFramework",
                "PresentationFramework.csproj");

            if (File.Exists(marker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the WPF repository root.");
    }

    private sealed class WpfAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly string _repoRoot;
        private readonly string _presentationFrameworkPath;
        private readonly string _presentationCorePath;
        private readonly string _compilerHarnessPath;
        private readonly string _fluentThemePath;
        private readonly AssemblyDependencyResolver _resolver;

        public WpfAssemblyLoadContext(
            string repoRoot,
            string presentationFrameworkPath,
            string presentationCorePath,
            string compilerHarnessPath,
            string fluentThemePath)
            : base(isCollectible: true)
        {
            _repoRoot = repoRoot;
            _presentationFrameworkPath = presentationFrameworkPath;
            _presentationCorePath = presentationCorePath;
            _compilerHarnessPath = compilerHarnessPath;
            _fluentThemePath = fluentThemePath;
            _resolver = new AssemblyDependencyResolver(fluentThemePath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, CompilerHarnessAssemblyName, StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_compilerHarnessPath);
            }

            if (string.Equals(assemblyName.Name, FluentThemeAssemblyName, StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_fluentThemePath);
            }

            if (string.Equals(assemblyName.Name, "PresentationFramework", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationFrameworkPath);
            }

            if (string.Equals(assemblyName.Name, "PresentationCore", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationCorePath);
            }

            string? artifactAssemblyPath = TryFindArtifactAssembly(_repoRoot, assemblyName);
            if (artifactAssemblyPath != null)
            {
                return LoadFromAssemblyPath(artifactAssemblyPath);
            }

            string outputAssemblyPath = Path.Combine(
                AppContext.BaseDirectory,
                $"{assemblyName.Name}.dll");
            if (File.Exists(outputAssemblyPath))
            {
                return LoadFromAssemblyPath(outputAssemblyPath);
            }

            string? resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath == null ? null : LoadFromAssemblyPath(resolvedPath);
        }
    }
}
