using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;

internal static class Program
{
    private const string SdkVersion = "11.0.0-dev";
    private const string AppAssemblyName = "ExternalSdkApp";
    private const string LibraryAssemblyName = "ExternalSdkLibrary";

    private static readonly string[] s_requiredWpfRuntimeAssemblies =
    [
        "WindowsBase",
        "System.Xaml",
        "PresentationCore",
        "PresentationFramework",
        "PresentationUI",
        "ReachFramework",
        "UIAutomationTypes",
        "UIAutomationProvider",
        "System.Windows.Input.Manipulations",
        "System.Windows.Primitives",
        "PresentationFramework.Aero2",
        "PresentationFramework.Fluent"
    ];

    private static readonly string[] s_requiredProGpuRuntimeAssemblies =
    [
        "ProGPU.Wpf",
        "ProGPU.Backend",
        "ProGPU.Scene",
        "ProGPU.Vector",
        "ProGPU.Text",
        "ProGPU.Compute",
        "ProGPU.Transpiler"
    ];

    private static readonly string[] s_requiredSilkNetRuntimeAssemblies =
    [
        "Silk.NET.Core",
        "Silk.NET.GLFW",
        "Silk.NET.Input.Common",
        "Silk.NET.Input.Glfw",
        "Silk.NET.Maths",
        "Silk.NET.WebGPU",
        "Silk.NET.Windowing.Common",
        "Silk.NET.Windowing.Glfw"
    ];

    private static readonly string[] s_requiredSupportRuntimeAssemblies =
    [
        "System.Configuration.ConfigurationManager",
        "System.Diagnostics.EventLog",
        "System.Formats.Nrbf",
        "System.IO.Packaging",
        "System.Security.Cryptography.ProtectedData",
        "System.Windows.Extensions"
    ];

    private static int Main()
    {
        try
        {
            string repoRoot = FindRepoRoot();
            string packageFeed = Path.Combine(repoRoot, "artifacts", "packages", "Release", "NonShipping");
            RequireDirectory(packageFeed, "local package feed");

            string workRoot = Path.Combine(Path.GetTempPath(), "ProGPU.Wpf.SdkExternalSmoke");
            string appProjectPath = PrepareExternalSdkApp(workRoot, packageFeed);
            string dotnetPath = Path.Combine(repoRoot, ".dotnet", "dotnet");

            RunProcess(dotnetPath, repoRoot, "build", appProjectPath, "-v:minimal");

            ValidateExternalProjectShape(workRoot);
            string outputRoot = Path.Combine(workRoot, AppAssemblyName, "bin", "Debug", "net11.0");
            ValidateExternalOutput(outputRoot);
            RunProcess(
                dotnetPath,
                outputRoot,
                new Dictionary<string, string>
                {
                    ["PROGPU_WPF_EXTERNAL_VALIDATE"] = "1"
                },
                Path.Combine(outputRoot, AppAssemblyName + ".dll"));

            Console.WriteLine("ProGPU WPF external SDK smoke succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static string PrepareExternalSdkApp(string workRoot, string packageFeed)
    {
        if (Directory.Exists(workRoot))
        {
            Directory.Delete(workRoot, recursive: true);
        }

        string appRoot = Path.Combine(workRoot, AppAssemblyName);
        string libraryRoot = Path.Combine(workRoot, LibraryAssemblyName);
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(libraryRoot);

        WriteFile(
            Path.Combine(workRoot, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="ProGPUWpfLocalArtifacts" value="{SecurityElement.Escape(packageFeed)}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        WriteFile(
            Path.Combine(libraryRoot, LibraryAssemblyName + ".csproj"),
            $"""
            <Project Sdk="ProGPU.Wpf.Sdk/{SdkVersion}">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <UseWPF>true</UseWPF>
              </PropertyGroup>
            </Project>
            """);

        WriteFile(
            Path.Combine(libraryRoot, "Properties", "AssemblyInfo.cs"),
            """
            using System.Windows;

            [assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]
            """);

        WriteFile(
            Path.Combine(libraryRoot, "ExternalPanel.xaml"),
            """
            <UserControl
                x:Class="ExternalSdkLibrary.ExternalPanel"
                x:Name="PanelOwner"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Border Padding="4">
                    <TextBlock
                        x:Name="CaptionText"
                        Text="{Binding Caption, ElementName=PanelOwner}" />
                </Border>
            </UserControl>
            """);

        WriteFile(
            Path.Combine(libraryRoot, "ExternalPanel.xaml.cs"),
            """
            using System.Windows;
            using System.Windows.Controls;

            namespace ExternalSdkLibrary;

            public partial class ExternalPanel : UserControl
            {
                public static readonly DependencyProperty CaptionProperty =
                    DependencyProperty.Register(
                        nameof(Caption),
                        typeof(string),
                        typeof(ExternalPanel),
                        new PropertyMetadata("External SDK library"));

                public ExternalPanel()
                {
                    InitializeComponent();
                }

                public string Caption
                {
                    get => (string)GetValue(CaptionProperty);
                    set => SetValue(CaptionProperty, value);
                }
            }
            """);

        WriteFile(
            Path.Combine(libraryRoot, "ExternalThemedControl.cs"),
            """
            using System.Windows;
            using System.Windows.Controls;

            namespace ExternalSdkLibrary;

            public sealed class ExternalThemedControl : Control
            {
                public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
                    nameof(Text),
                    typeof(string),
                    typeof(ExternalThemedControl),
                    new FrameworkPropertyMetadata(string.Empty));

                static ExternalThemedControl()
                {
                    DefaultStyleKeyProperty.OverrideMetadata(
                        typeof(ExternalThemedControl),
                        new FrameworkPropertyMetadata(typeof(ExternalThemedControl)));
                }

                public string Text
                {
                    get => (string)GetValue(TextProperty);
                    set => SetValue(TextProperty, value);
                }
            }
            """);

        WriteFile(
            Path.Combine(libraryRoot, "Themes", "Generic.xaml"),
            """
            <ResourceDictionary
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:ExternalSdkLibrary">
                <SolidColorBrush
                    x:Key="{ComponentResourceKey TypeInTargetAssembly={x:Type local:ExternalThemedControl}, ResourceId=ExternalThemeBorderBrush}"
                    Color="#7A4EB2" />

                <Style TargetType="{x:Type local:ExternalThemedControl}">
                    <Setter Property="Background" Value="#6B8F3A" />
                    <Setter Property="Foreground" Value="#356D9E" />
                    <Setter Property="Padding" Value="5" />
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="{x:Type local:ExternalThemedControl}">
                                <Border
                                    x:Name="ThemeRoot"
                                    Background="{TemplateBinding Background}"
                                    BorderBrush="{DynamicResource {ComponentResourceKey TypeInTargetAssembly={x:Type local:ExternalThemedControl}, ResourceId=ExternalThemeBorderBrush}}"
                                    BorderThickness="2"
                                    Padding="{TemplateBinding Padding}">
                                    <TextBlock
                                        x:Name="ThemeText"
                                        Foreground="{TemplateBinding Foreground}"
                                        Text="{TemplateBinding Text}" />
                                </Border>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </ResourceDictionary>
            """);

        string appProjectPath = Path.Combine(appRoot, AppAssemblyName + ".csproj");
        WriteFile(
            appProjectPath,
            $"""
            <Project Sdk="ProGPU.Wpf.Sdk/{SdkVersion}">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net11.0</TargetFramework>
                <UseWPF>true</UseWPF>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="../{LibraryAssemblyName}/{LibraryAssemblyName}.csproj" />
              </ItemGroup>
            </Project>
            """);

        WriteFile(
            Path.Combine(appRoot, "App.xaml"),
            """
            <Application
                x:Class="ExternalSdkApp.App"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                StartupUri="MainWindow.xaml">
                <Application.Resources>
                    <ResourceDictionary>
                        <ResourceDictionary.MergedDictionaries>
                            <ResourceDictionary Source="ExternalResources.xaml" />
                        </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                </Application.Resources>
            </Application>
            """);

        WriteFile(
            Path.Combine(appRoot, "App.xaml.cs"),
            """
            using System.Windows;

            namespace ExternalSdkApp;

            public partial class App : Application
            {
            }
            """);

        WriteFile(
            Path.Combine(appRoot, "ExternalResources.xaml"),
            """
            <ResourceDictionary
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:ExternalSdkApp">
                <SolidColorBrush
                    x:Key="ExternalStaticBrush"
                    Color="#A65A2A" />
                <SolidColorBrush
                    x:Key="ExternalDynamicBrush"
                    Color="#225588" />
                <sys:String
                    x:Key="ExternalStaticText"
                    xmlns:sys="clr-namespace:System;assembly=System.Private.CoreLib">External SDK resource text</sys:String>
                <DataTemplate
                    x:Key="ExternalItemTemplate"
                    DataType="{x:Type local:ExternalItem}">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock
                            x:Name="ExternalItemNameText"
                            Text="{Binding Name}" />
                        <TextBlock
                            x:Name="ExternalItemKindText"
                            Text="{Binding Kind}" />
                    </StackPanel>
                </DataTemplate>
            </ResourceDictionary>
            """);

        WriteFile(
            Path.Combine(appRoot, "MainWindow.xaml"),
            """
            <Window
                x:Class="ExternalSdkApp.MainWindow"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:ExternalSdkApp"
                xmlns:library="clr-namespace:ExternalSdkLibrary;assembly=ExternalSdkLibrary"
                Title="External SDK App"
                Width="320"
                Height="200">
                <Window.CommandBindings>
                    <CommandBinding
                        Command="{x:Static local:MainWindow.ExternalCommand}"
                        CanExecute="OnExternalCommandCanExecute"
                        Executed="OnExternalCommandExecuted" />
                </Window.CommandBindings>
                <Window.InputBindings>
                    <KeyBinding
                        Command="{x:Static local:MainWindow.ExternalCommand}"
                        Gesture="Ctrl+E" />
                </Window.InputBindings>
                <StackPanel
                    x:Name="ExternalFocusPanel"
                    FocusManager.FocusedElement="{Binding ElementName=ExternalCommandButton}"
                    FocusManager.IsFocusScope="True"
                    KeyboardNavigation.ControlTabNavigation="Cycle"
                    KeyboardNavigation.DirectionalNavigation="Contained"
                    KeyboardNavigation.TabNavigation="Cycle">
                    <TextBlock
                        x:Name="TitleText"
                        Text="External SDK app" />
                    <TextBlock
                        x:Name="StaticResourceText"
                        Foreground="{StaticResource ExternalStaticBrush}"
                        Text="{StaticResource ExternalStaticText}" />
                    <TextBlock
                        x:Name="DynamicResourceText"
                        Foreground="{DynamicResource ExternalDynamicBrush}"
                        Text="External SDK dynamic resource" />
                    <ContentControl
                        x:Name="ExternalTemplatePresenter"
                        Content="{Binding SelectedExternalItem}"
                        ContentTemplate="{StaticResource ExternalItemTemplate}" />
                    <ListBox
                        x:Name="ExternalItemsList"
                        DisplayMemberPath="Name"
                        ItemsSource="{Binding ExternalItems}"
                        SelectedIndex="1" />
                    <Button
                        x:Name="ExternalCommandButton"
                        Command="{x:Static local:MainWindow.ExternalCommand}"
                        CommandParameter="ExternalCommandParameter"
                        Click="OnExternalCommandButtonClick"
                        Content="Run command" />
                    <library:ExternalPanel
                        x:Name="ExternalPanel"
                        Caption="External SDK library panel" />
                    <library:ExternalThemedControl
                        x:Name="ExternalThemedControl"
                        Text="External SDK themed control" />
                    <Frame
                        x:Name="ExternalFrame"
                        Source="ExternalPage.xaml"
                        NavigationUIVisibility="Hidden"
                        Navigating="OnExternalFrameNavigating"
                        Navigated="OnExternalFrameNavigated"
                        LoadCompleted="OnExternalFrameLoadCompleted" />
                </StackPanel>
            </Window>
            """);

        WriteFile(
            Path.Combine(appRoot, "ExternalPage.xaml"),
            """
            <Page
                x:Class="ExternalSdkApp.ExternalPage"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:library="clr-namespace:ExternalSdkLibrary;assembly=ExternalSdkLibrary"
                Title="External Page">
                <StackPanel>
                    <TextBlock
                        x:Name="ExternalPageTitle"
                        Text="External SDK page" />
                    <library:ExternalPanel
                        x:Name="ExternalPagePanel"
                        Caption="External SDK page panel" />
                </StackPanel>
            </Page>
            """);

        WriteFile(
            Path.Combine(appRoot, "ExternalPage.xaml.cs"),
            """
            using System.Windows.Controls;

            namespace ExternalSdkApp;

            public partial class ExternalPage : Page
            {
                public ExternalPage()
                {
                    InitializeComponent();
                }
            }
            """);

        WriteFile(
            Path.Combine(appRoot, "ExternalSecondPage.xaml"),
            """
            <Page
                x:Class="ExternalSdkApp.ExternalSecondPage"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Title="External Second Page">
                <StackPanel>
                    <TextBlock
                        x:Name="ExternalSecondPageTitle"
                        Text="External SDK second page" />
                </StackPanel>
            </Page>
            """);

        WriteFile(
            Path.Combine(appRoot, "ExternalSecondPage.xaml.cs"),
            """
            using System.Windows.Controls;

            namespace ExternalSdkApp;

            public partial class ExternalSecondPage : Page
            {
                public ExternalSecondPage()
                {
                    InitializeComponent();
                }
            }
            """);

        WriteFile(
            Path.Combine(appRoot, "MainWindow.xaml.cs"),
            """
            using System;
            using System.Collections.ObjectModel;
            using System.Collections.Generic;
            using System.Windows;
            using System.Windows.Controls;
            using System.Windows.Controls.Primitives;
            using System.Windows.Input;
            using System.Windows.Media;
            using System.Windows.Navigation;
            using System.Windows.Threading;
            using ExternalSdkLibrary;

            namespace ExternalSdkApp;

            public partial class MainWindow : Window
            {
                public static readonly RoutedUICommand ExternalCommand = new(
                    "External SDK command",
                    nameof(ExternalCommand),
                    typeof(MainWindow));

                public MainWindow()
                {
                    DataContext = this;
                    InitializeComponent();
                }

                public ObservableCollection<ExternalItem> ExternalItems { get; } =
                [
                    new ExternalItem("Alpha", "Framework"),
                    new ExternalItem("Beta", "Rendering")
                ];

                public ExternalItem SelectedExternalItem => ExternalItems[0];

                public int ExternalCommandCanExecuteCount { get; private set; }

                public int ExternalCommandExecutedCount { get; private set; }

                public int ExternalCommandButtonClickCount { get; private set; }

                public object? LastExternalCommandParameter { get; private set; }

                public string? LastExternalCommandName { get; private set; }

                public int ExternalFrameNavigatingCount { get; private set; }

                public int ExternalFrameNavigatedCount { get; private set; }

                public int ExternalFrameLoadCompletedCount { get; private set; }

                public string? LastExternalFrameNavigatingUri { get; private set; }

                public string? LastExternalFrameNavigatedUri { get; private set; }

                public string? LastExternalFrameLoadCompletedUri { get; private set; }

                public string? LastExternalFrameNavigationMode { get; private set; }

                public string? LastExternalFrameContentType { get; private set; }

                private void OnExternalFrameNavigating(object sender, NavigatingCancelEventArgs e)
                {
                    ExternalFrameNavigatingCount++;
                    LastExternalFrameNavigatingUri = e.Uri?.ToString();
                    LastExternalFrameNavigationMode = e.NavigationMode.ToString();
                }

                private void OnExternalFrameNavigated(object sender, NavigationEventArgs e)
                {
                    ExternalFrameNavigatedCount++;
                    LastExternalFrameNavigatedUri = e.Uri?.ToString();
                    LastExternalFrameContentType = e.Content?.GetType().FullName;
                }

                private void OnExternalFrameLoadCompleted(object sender, NavigationEventArgs e)
                {
                    ExternalFrameLoadCompletedCount++;
                    LastExternalFrameLoadCompletedUri = e.Uri?.ToString();
                }

                private void OnExternalCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
                {
                    ExternalCommandCanExecuteCount++;
                    e.CanExecute = true;
                    e.Handled = true;
                }

                private void OnExternalCommandExecuted(object sender, ExecutedRoutedEventArgs e)
                {
                    ExternalCommandExecutedCount++;
                    LastExternalCommandParameter = e.Parameter;
                    LastExternalCommandName = (e.Command as RoutedCommand)?.Name;
                    e.Handled = true;
                }

                private void OnExternalCommandButtonClick(object sender, RoutedEventArgs e)
                {
                    ExternalCommandButtonClickCount++;
                }
            }

            public sealed class ExternalItem
            {
                public ExternalItem(string name, string kind)
                {
                    Name = name;
                    Kind = kind;
                }

                public string Name { get; }

                public string Kind { get; }
            }

            public partial class App
            {
                protected override void OnStartup(StartupEventArgs e)
                {
                    if (Environment.GetEnvironmentVariable("PROGPU_WPF_EXTERNAL_VALIDATE") == "1")
                    {
                        ExternalSdkValidation.Run();
                        Shutdown();
                        return;
                    }

                    base.OnStartup(e);
                }
            }

            internal static class ExternalSdkValidation
            {
                public static void Run()
                {
                    var window = new MainWindow();
                    var panel = RequireType<ExternalPanel>(
                        window.FindName("ExternalPanel"),
                        "external SDK app library user-control");
                    var captionText = RequireType<TextBlock>(
                        panel.FindName("CaptionText"),
                        "external SDK user-control named TextBlock");
                    AssertEqual("External SDK library panel", captionText.Text, "external SDK user-control ElementName binding");
                    ValidateApplicationResources(window);
                    ValidateCommandsAndFocus(window);

                    var themedControl = RequireType<ExternalThemedControl>(
                        window.FindName("ExternalThemedControl"),
                        "external SDK app library themed control");
                    themedControl.ApplyTemplate();
                    if (themedControl.Template is null)
                    {
                        throw new InvalidOperationException("External SDK themed library control did not receive its Generic.xaml default template.");
                    }

                    var themeRoot = RequireType<Border>(
                        themedControl.Template.FindName("ThemeRoot", themedControl),
                        "external SDK themed control template root");
                    var themeText = RequireType<TextBlock>(
                        themedControl.Template.FindName("ThemeText", themedControl),
                        "external SDK themed control template text");

                    AssertEqual("External SDK themed control", themeText.Text, "external SDK themed control TemplateBinding text");
                    AssertBrushColor(themeRoot.Background, "#FF6B8F3A", "external SDK themed control background");
                    AssertBrushColor(themeRoot.BorderBrush, "#FF7A4EB2", "external SDK themed control component resource brush");
                    AssertBrushColor(themeText.Foreground, "#FF356D9E", "external SDK themed control foreground");

                    AssertEqual(2.0, themeRoot.BorderThickness.Left, "external SDK themed control border left");
                    AssertEqual(2.0, themeRoot.BorderThickness.Top, "external SDK themed control border top");
                    AssertEqual(2.0, themeRoot.BorderThickness.Right, "external SDK themed control border right");
                    AssertEqual(2.0, themeRoot.BorderThickness.Bottom, "external SDK themed control border bottom");

                    var frame = RequireType<Frame>(
                        window.FindName("ExternalFrame"),
                        "external SDK compiled page frame");
                    DrainDispatcher();

                    var page = RequireType<ExternalPage>(
                        frame.Content,
                        "external SDK initial compiled page");
                    var pageTitle = RequireType<TextBlock>(
                        page.FindName("ExternalPageTitle"),
                        "external SDK initial compiled page title");
                    var pagePanel = RequireType<ExternalPanel>(
                        page.FindName("ExternalPagePanel"),
                        "external SDK initial compiled page library user-control");
                    var pagePanelCaption = RequireType<TextBlock>(
                        pagePanel.FindName("CaptionText"),
                        "external SDK initial compiled page library user-control caption");
                    AssertEqual("External SDK page", pageTitle.Text, "external SDK initial compiled page text");
                    AssertEqual("External SDK page panel", pagePanelCaption.Text, "external SDK initial compiled page library binding");
                    AssertAtLeast(1, window.ExternalFrameNavigatingCount, "external SDK initial frame navigating count");
                    AssertAtLeast(1, window.ExternalFrameNavigatedCount, "external SDK initial frame navigated count");
                    AssertAtLeast(1, window.ExternalFrameLoadCompletedCount, "external SDK initial frame load completed count");
                    AssertEndsWith(window.LastExternalFrameNavigatingUri, "ExternalPage.xaml", "external SDK initial frame navigating URI");
                    AssertEndsWith(window.LastExternalFrameNavigatedUri, "ExternalPage.xaml", "external SDK initial frame navigated URI");
                    AssertEndsWith(window.LastExternalFrameLoadCompletedUri, "ExternalPage.xaml", "external SDK initial frame load completed URI");
                    AssertEqual("New", window.LastExternalFrameNavigationMode, "external SDK initial frame navigation mode");
                    AssertEqual(typeof(ExternalPage).FullName, window.LastExternalFrameContentType, "external SDK initial frame content type");

                    int navigatingCountBeforeSecondPage = window.ExternalFrameNavigatingCount;
                    int navigatedCountBeforeSecondPage = window.ExternalFrameNavigatedCount;
                    int loadCompletedCountBeforeSecondPage = window.ExternalFrameLoadCompletedCount;
                    AssertEqual(true, frame.Navigate(new Uri("ExternalSecondPage.xaml", UriKind.Relative)), "external SDK second page navigate result");
                    DrainDispatcher();

                    var secondPage = RequireType<ExternalSecondPage>(
                        frame.Content,
                        "external SDK second compiled page");
                    var secondPageTitle = RequireType<TextBlock>(
                        secondPage.FindName("ExternalSecondPageTitle"),
                        "external SDK second compiled page title");
                    AssertEqual("External SDK second page", secondPageTitle.Text, "external SDK second compiled page text");
                    AssertAtLeast(navigatingCountBeforeSecondPage + 1, window.ExternalFrameNavigatingCount, "external SDK second frame navigating count");
                    AssertAtLeast(navigatedCountBeforeSecondPage + 1, window.ExternalFrameNavigatedCount, "external SDK second frame navigated count");
                    AssertAtLeast(loadCompletedCountBeforeSecondPage + 1, window.ExternalFrameLoadCompletedCount, "external SDK second frame load completed count");
                    AssertEndsWith(window.LastExternalFrameNavigatedUri, "ExternalSecondPage.xaml", "external SDK second frame navigated URI");
                    AssertEqual(typeof(ExternalSecondPage).FullName, window.LastExternalFrameContentType, "external SDK second frame content type");
                    AssertEqual(true, frame.CanGoBack, "external SDK frame can go back");

                    int navigatingCountBeforeBack = window.ExternalFrameNavigatingCount;
                    int navigatedCountBeforeBack = window.ExternalFrameNavigatedCount;
                    frame.GoBack();
                    DrainDispatcher();

                    RequireType<ExternalPage>(
                        frame.Content,
                        "external SDK returned compiled page");
                    AssertAtLeast(navigatingCountBeforeBack + 1, window.ExternalFrameNavigatingCount, "external SDK back frame navigating count");
                    AssertAtLeast(navigatedCountBeforeBack + 1, window.ExternalFrameNavigatedCount, "external SDK back frame navigated count");
                    AssertEqual("Back", window.LastExternalFrameNavigationMode, "external SDK back frame navigation mode");
                    AssertEqual(typeof(ExternalPage).FullName, window.LastExternalFrameContentType, "external SDK back frame content type");
                }

                private static void ValidateApplicationResources(MainWindow window)
                {
                    var appResources = Application.Current?.Resources
                        ?? throw new InvalidOperationException("External SDK validation requires Application resources.");
                    AssertAtLeast(1, appResources.MergedDictionaries.Count, "external SDK application merged dictionary count");
                    AssertEqual(
                        "External SDK resource text",
                        appResources["ExternalStaticText"],
                        "external SDK application static text resource");
                    AssertBrushColor(
                        RequireType<Brush>(appResources["ExternalStaticBrush"], "external SDK application static brush resource"),
                        "#FFA65A2A",
                        "external SDK application static brush resource");

                    var staticResourceText = RequireType<TextBlock>(
                        window.FindName("StaticResourceText"),
                        "external SDK static resource text block");
                    AssertEqual("External SDK resource text", staticResourceText.Text, "external SDK static resource text");
                    AssertBrushColor(staticResourceText.Foreground, "#FFA65A2A", "external SDK static resource foreground");

                    var dynamicResourceText = RequireType<TextBlock>(
                        window.FindName("DynamicResourceText"),
                        "external SDK dynamic resource text block");
                    AssertBrushColor(dynamicResourceText.Foreground, "#FF225588", "external SDK initial dynamic resource foreground");
                    appResources["ExternalDynamicBrush"] = new SolidColorBrush(Color.FromRgb(0x45, 0x76, 0x23));
                    DrainDispatcher();
                    AssertBrushColor(dynamicResourceText.Foreground, "#FF457623", "external SDK updated dynamic resource foreground");

                    var template = RequireType<DataTemplate>(
                        window.FindResource("ExternalItemTemplate"),
                        "external SDK item data template");
                    var templateRoot = RequireType<StackPanel>(
                        template.LoadContent(),
                        "external SDK item template root");
                    templateRoot.DataContext = window.SelectedExternalItem;
                    DrainDispatcher();
                    AssertAtLeast(2, templateRoot.Children.Count, "external SDK item template child count");
                    var itemNameText = RequireType<TextBlock>(
                        templateRoot.Children[0],
                        "external SDK item template name text");
                    var itemKindText = RequireType<TextBlock>(
                        templateRoot.Children[1],
                        "external SDK item template kind text");
                    AssertEqual("Alpha", itemNameText.Text, "external SDK item template name binding");
                    AssertEqual("Framework", itemKindText.Text, "external SDK item template kind binding");

                    var templatePresenter = RequireType<ContentControl>(
                        window.FindName("ExternalTemplatePresenter"),
                        "external SDK content template presenter");
                    AssertEqual(window.SelectedExternalItem, templatePresenter.Content, "external SDK content presenter content binding");
                    AssertEqual(template, templatePresenter.ContentTemplate, "external SDK content presenter template");

                    var itemsList = RequireType<ListBox>(
                        window.FindName("ExternalItemsList"),
                        "external SDK bound items list");
                    AssertEqual(2, itemsList.Items.Count, "external SDK bound items count");
                    AssertEqual(1, itemsList.SelectedIndex, "external SDK selected item index");
                    AssertEqual(window.ExternalItems[1], itemsList.SelectedItem, "external SDK selected item");
                    window.ExternalItems.Add(new ExternalItem("Gamma", "Data"));
                    DrainDispatcher();
                    AssertEqual(3, itemsList.Items.Count, "external SDK bound items count after collection change");
                }

                private static void ValidateCommandsAndFocus(MainWindow window)
                {
                    AssertEqual(1, window.CommandBindings.Count, "external SDK command binding count");
                    var commandBinding = RequireType<CommandBinding>(
                        window.CommandBindings[0],
                        "external SDK command binding");
                    AssertEqual(MainWindow.ExternalCommand, commandBinding.Command, "external SDK command binding command");

                    AssertEqual(1, window.InputBindings.Count, "external SDK input binding count");
                    var keyBinding = RequireType<KeyBinding>(
                        window.InputBindings[0],
                        "external SDK key binding");
                    AssertEqual(MainWindow.ExternalCommand, keyBinding.Command, "external SDK key binding command");
                    AssertEqual(Key.E, keyBinding.Key, "external SDK key binding key");
                    AssertEqual(ModifierKeys.Control, keyBinding.Modifiers, "external SDK key binding modifiers");

                    var focusPanel = RequireType<StackPanel>(
                        window.FindName("ExternalFocusPanel"),
                        "external SDK focus panel");
                    var commandButton = RequireType<Button>(
                        window.FindName("ExternalCommandButton"),
                        "external SDK command button");
                    AssertEqual(commandButton, FocusManager.GetFocusedElement(focusPanel), "external SDK focus manager focused element");
                    AssertEqual(true, FocusManager.GetIsFocusScope(focusPanel), "external SDK focus manager scope flag");
                    AssertEqual(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(focusPanel), "external SDK tab navigation mode");
                    AssertEqual(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetControlTabNavigation(focusPanel), "external SDK control-tab navigation mode");
                    AssertEqual(KeyboardNavigationMode.Contained, KeyboardNavigation.GetDirectionalNavigation(focusPanel), "external SDK directional navigation mode");
                    AssertEqual(MainWindow.ExternalCommand, commandButton.Command, "external SDK command button command");
                    AssertEqual("ExternalCommandParameter", commandButton.CommandParameter, "external SDK command button parameter");

                    int canExecuteBefore = window.ExternalCommandCanExecuteCount;
                    int executedBefore = window.ExternalCommandExecutedCount;
                    MainWindow.ExternalCommand.Execute("DirectCommandParameter", commandButton);
                    AssertAtLeast(canExecuteBefore + 1, window.ExternalCommandCanExecuteCount, "external SDK direct command can-execute count");
                    AssertEqual(executedBefore + 1, window.ExternalCommandExecutedCount, "external SDK direct command executed count");
                    AssertEqual("DirectCommandParameter", window.LastExternalCommandParameter, "external SDK direct command parameter");
                    AssertEqual(nameof(MainWindow.ExternalCommand), window.LastExternalCommandName, "external SDK command name");

                    int clickBefore = window.ExternalCommandButtonClickCount;
                    int buttonExecutedBefore = window.ExternalCommandExecutedCount;
                    commandButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, commandButton));
                    AssertEqual(clickBefore + 1, window.ExternalCommandButtonClickCount, "external SDK generated button click count");
                    RequireType<RoutedCommand>(
                        commandButton.Command,
                        "external SDK command button routed command")
                        .Execute(commandButton.CommandParameter, commandButton);
                    AssertEqual(buttonExecutedBefore + 1, window.ExternalCommandExecutedCount, "external SDK button command executed count");
                    AssertEqual("ExternalCommandParameter", window.LastExternalCommandParameter, "external SDK button command parameter");
                }

                private static void DrainDispatcher()
                {
                    var frame = new DispatcherFrame();
                    Dispatcher.CurrentDispatcher.BeginInvoke(
                        DispatcherPriority.ApplicationIdle,
                        new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                }

                private static T RequireType<T>(object? value, string description)
                {
                    if (value is T typed)
                    {
                        return typed;
                    }

                    throw new InvalidOperationException(
                        $"Expected {description} to be {typeof(T).FullName}, but found {value?.GetType().FullName ?? "<null>"}.");
                }

                private static void AssertBrushColor(Brush brush, string expected, string description)
                {
                    var solidColorBrush = RequireType<SolidColorBrush>(brush, description);
                    AssertEqual(expected, solidColorBrush.Color.ToString(), description);
                }

                private static void AssertEqual<T>(T expected, T actual, string description)
                {
                    if (!EqualityComparer<T>.Default.Equals(expected, actual))
                    {
                        throw new InvalidOperationException(
                            $"Expected {description} to be '{expected}', but found '{actual}'.");
                    }
                }

                private static void AssertAtLeast(int expectedMinimum, int actual, string description)
                {
                    if (actual < expectedMinimum)
                    {
                        throw new InvalidOperationException(
                            $"Expected {description} to be at least '{expectedMinimum}', but found '{actual}'.");
                    }
                }

                private static void AssertEndsWith(string? value, string expectedSuffix, string description)
                {
                    if (value is null || !value.EndsWith(expectedSuffix, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Expected {description} to end with '{expectedSuffix}', but found '{value ?? "<null>"}'.");
                    }
                }
            }
            """);

        return appProjectPath;
    }

    private static void ValidateExternalProjectShape(string workRoot)
    {
        string appProject = File.ReadAllText(Path.Combine(workRoot, AppAssemblyName, AppAssemblyName + ".csproj"));
        string libraryProject = File.ReadAllText(Path.Combine(workRoot, LibraryAssemblyName, LibraryAssemblyName + ".csproj"));

        AssertContains(appProject, $"<Project Sdk=\"ProGPU.Wpf.Sdk/{SdkVersion}\">", "external app SDK");
        AssertContains(appProject, "<OutputType>WinExe</OutputType>", "external app output type");
        AssertContains(appProject, "<UseWPF>true</UseWPF>", "external app WPF property");
        AssertContains(appProject, $"<ProjectReference Include=\"../{LibraryAssemblyName}/{LibraryAssemblyName}.csproj\" />", "external app project reference");
        AssertContains(libraryProject, $"<Project Sdk=\"ProGPU.Wpf.Sdk/{SdkVersion}\">", "external library SDK");
        AssertContains(libraryProject, "<UseWPF>true</UseWPF>", "external library WPF property");
        RequireFile(Path.Combine(workRoot, LibraryAssemblyName, "Properties", "AssemblyInfo.cs"), "external SDK library ThemeInfo source");
        RequireFile(Path.Combine(workRoot, LibraryAssemblyName, "Themes", "Generic.xaml"), "external SDK library Generic.xaml source");
        RequireFile(Path.Combine(workRoot, AppAssemblyName, "ExternalResources.xaml"), "external SDK app merged resource dictionary source");
        RequireFile(Path.Combine(workRoot, AppAssemblyName, "ExternalPage.xaml"), "external SDK app compiled page source");
        RequireFile(Path.Combine(workRoot, AppAssemblyName, "ExternalSecondPage.xaml"), "external SDK app second compiled page source");

        AssertDoesNotContain(appProject, "ProGpuWpfReferenceMode", "external app local artifact mode");
        AssertDoesNotContain(appProject, "ProGpuWpfManagedReferenceRoot", "external app managed artifact root");
        AssertDoesNotContain(appProject, "ProGpuReferenceRoot", "external app ProGPU artifact root");
        AssertDoesNotContain(libraryProject, "ProGpuWpfReferenceMode", "external library local artifact mode");
        AssertDoesNotContain(libraryProject, "ProGpuWpfManagedReferenceRoot", "external library managed artifact root");
        AssertDoesNotContain(libraryProject, "ProGpuReferenceRoot", "external library ProGPU artifact root");

        if (File.Exists(Path.Combine(workRoot, "Directory.Build.props")) ||
            File.Exists(Path.Combine(workRoot, "Directory.Build.targets")))
        {
            throw new InvalidOperationException("External SDK smoke must not rely on generated Directory.Build.props or Directory.Build.targets files.");
        }
    }

    private static void ValidateExternalOutput(string outputRoot)
    {
        RequireFile(Path.Combine(outputRoot, AppAssemblyName + ".dll"), "external SDK app assembly");
        RequireFile(Path.Combine(outputRoot, LibraryAssemblyName + ".dll"), "external SDK library assembly");

        foreach (string assemblyName in s_requiredWpfRuntimeAssemblies
                     .Concat(s_requiredProGpuRuntimeAssemblies)
                     .Concat(s_requiredSilkNetRuntimeAssemblies)
                     .Concat(s_requiredSupportRuntimeAssemblies))
        {
            RequireFile(Path.Combine(outputRoot, assemblyName + ".dll"), $"external SDK output asset '{assemblyName}.dll'");
        }

        RequireAnyFile(outputRoot, GetNativeAssetCandidates("wgpu"), "external SDK output native WebGPU runtime asset");
        RequireAnyFile(outputRoot, GetNativeAssetCandidates("glfw"), "external SDK output native GLFW runtime asset");

        string depsJson = File.ReadAllText(Path.Combine(outputRoot, AppAssemblyName + ".deps.json"));
        AssertContains(depsJson, "Microsoft.DotNet.Wpf.GitHub", "external SDK WPF transport package dependency");
        AssertContains(depsJson, "ProGPU.Wpf", "external SDK ProGPU WPF package dependency");
        AssertContains(depsJson, "ProGPU.Compute", "external SDK ProGPU compute package dependency");
        AssertContains(depsJson, "ProGPU.Transpiler", "external SDK ProGPU transpiler package dependency");
        AssertContains(depsJson, LibraryAssemblyName, "external SDK referenced library dependency");
    }

    private static string RunProcess(string fileName, string workingDirectory, params string[] arguments)
    {
        return RunProcess(fileName, workingDirectory, environment: null, arguments);
    }

    private static string RunProcess(
        string fileName,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.Environment["DOTNET_ROLL_FORWARD"] = "Major";
        if (environment is not null)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        string output = standardOutput + standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command '{fileName} {string.Join(" ", arguments)}' failed with exit code {process.ExitCode}.{Environment.NewLine}{output}");
        }

        return output;
    }

    private static string[] GetNativeAssetCandidates(string assetName)
    {
        return assetName switch
        {
            "wgpu" when OperatingSystem.IsWindows() => ["wgpu_native.dll"],
            "wgpu" when OperatingSystem.IsMacOS() => ["libwgpu_native.dylib"],
            "wgpu" => ["libwgpu_native.so"],
            "glfw" when OperatingSystem.IsWindows() => ["glfw3.dll"],
            "glfw" when OperatingSystem.IsMacOS() => ["libglfw.3.dylib"],
            "glfw" => ["libglfw.so.3"],
            _ => throw new ArgumentOutOfRangeException(nameof(assetName), assetName, null)
        };
    }

    private static string FindRepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Microsoft.Dotnet.Wpf.sln")) &&
                Directory.Exists(Path.Combine(directory, "packaging", "ProGPU.Wpf.Sdk")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static void WriteFile(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new ArgumentException("Path has no directory.", nameof(path)));
        File.WriteAllText(path, contents);
    }

    private static void RequireDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Missing {description}: {path}");
        }
    }

    private static void RequireFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing {description}: {path}", path);
        }
    }

    private static void RequireAnyFile(string root, IEnumerable<string> candidates, string description)
    {
        foreach (string candidate in candidates)
        {
            if (File.Exists(Path.Combine(root, candidate)))
            {
                return;
            }
        }

        throw new FileNotFoundException($"Missing {description} under {root}.");
    }

    private static void AssertContains(string value, string expected, string description)
    {
        if (!value.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected {description} to contain '{expected}'.");
        }
    }

    private static void AssertDoesNotContain(string value, string unexpected, string description)
    {
        if (value.Contains(unexpected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected {description} not to contain '{unexpected}'.");
        }
    }
}
