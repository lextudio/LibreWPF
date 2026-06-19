using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
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
            ValidateSdkPackageLayout(packageFeed);

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
            string applicationRunOutput = RunProcess(
                dotnetPath,
                outputRoot,
                new Dictionary<string, string>
                {
                    ["PROGPU_WPF_EXTERNAL_RUN_VALIDATE"] = "1"
                },
                Path.Combine(outputRoot, AppAssemblyName + ".dll"));
            AssertContains(
                applicationRunOutput,
                "External SDK Application.Run validation succeeded.",
                "external SDK Application.Run validation output");

            Console.WriteLine("ProGPU WPF external SDK smoke succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void ValidateSdkPackageLayout(string packageFeed)
    {
        string packagePath = Path.Combine(packageFeed, $"ProGPU.Wpf.Sdk.{SdkVersion}.nupkg");
        RequireFile(packagePath, "ProGPU WPF SDK package");

        using ZipArchive package = ZipFile.OpenRead(packagePath);

        string nuspec = ReadPackageEntry(package, "ProGPU.Wpf.Sdk.nuspec", "SDK nuspec");
        string sdkProps = ReadPackageEntry(package, "Sdk/Sdk.props", "SDK root props import");
        string sdkTargets = ReadPackageEntry(package, "Sdk/Sdk.targets", "SDK root targets import");
        string portableProps = ReadPackageEntry(package, "targets/ProGPU.Wpf.Sdk.props", "portable SDK props");
        string portableTargets = ReadPackageEntry(package, "targets/ProGPU.Wpf.Sdk.targets", "portable SDK targets");
        string portableBootstrap = ReadPackageEntry(package, "targets/ProGPU.Wpf.Sdk.PortableBootstrap.cs", "portable SDK bootstrap");
        _ = ReadPackageEntry(package, "README.md", "SDK readme");

        AssertContains(nuspec, "<id>ProGPU.Wpf.Sdk</id>", "SDK nuspec package id");
        AssertContains(nuspec, $"<version>{SdkVersion}</version>", "SDK nuspec version");
        AssertContains(nuspec, "<packageType name=\"MSBuildSdk\" />", "SDK nuspec package type");
        AssertContains(nuspec, "<dependencies>", "SDK nuspec dependency group");

        AssertContains(sdkProps, "<ProGpuWpfSdkVersion Condition=\"'$(ProGpuWpfSdkVersion)' == ''\">11.0.0-dev</ProGpuWpfSdkVersion>", "SDK root version default");
        AssertContains(sdkProps, "<Import Sdk=\"Microsoft.NET.Sdk.WindowsDesktop\" Project=\"Sdk.props\" />", "SDK root WindowsDesktop props import");
        AssertContains(sdkProps, "ProGPU.Wpf.Sdk.props", "SDK root portable props import");
        AssertContains(sdkTargets, "<Import Sdk=\"Microsoft.NET.Sdk.WindowsDesktop\" Project=\"Sdk.targets\" />", "SDK root WindowsDesktop targets import");
        AssertContains(sdkTargets, "ProGPU.Wpf.Sdk.targets", "SDK root portable targets import");

        AssertContains(portableProps, "<InternalMarkupCompilation Condition=\"'$(ProGpuWpfUseWpfMarkup)' == 'true' And '$(InternalMarkupCompilation)' == ''\">true</InternalMarkupCompilation>", "SDK markup compiler default");
        AssertContains(portableProps, "<AlwaysCompileMarkupFilesInSeparateDomain Condition=\"'$(ProGpuWpfUseWpfMarkup)' == 'true' And '$(AlwaysCompileMarkupFilesInSeparateDomain)' == ''\">false</AlwaysCompileMarkupFilesInSeparateDomain>", "SDK markup compiler appdomain default");
        AssertContains(portableProps, "<ApplicationDefinition Include=\"App.xaml\"", "SDK default app XAML item");
        AssertContains(portableProps, "<Page Include=\"**/*.xaml\"", "SDK default page XAML item");
        AssertContains(portableProps, "<PackageReference Include=\"Silk.NET.WebGPU.Native.WGPU\" Version=\"$(ProGpuWpfSilkNetVersion)\" />", "SDK native WebGPU package reference");
        AssertContains(portableProps, "<PackageReference Include=\"System.IO.Packaging\" Version=\"$(ProGpuWpfSystemIOPackagingVersion)\" />", "SDK WPF support package reference");

        AssertContains(portableTargets, "<FrameworkReference Remove=\"Microsoft.WindowsDesktop.App.WPF\" />", "SDK WindowsDesktop framework suppression");
        AssertContains(portableTargets, "<PackageReference Include=\"$(ProGpuWpfManagedPackageId)\" Version=\"$(ProGpuWpfManagedPackageVersion)\" />", "SDK managed WPF transport package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"ProGPU.Wpf\" Version=\"$(ProGpuWpfPackageVersion)\" />", "SDK ProGPU WPF package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"ProGPU.Compute\" Version=\"$(ProGpuPackageVersion)\" />", "SDK ProGPU compute package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"ProGPU.Transpiler\" Version=\"$(ProGpuPackageVersion)\" />", "SDK ProGPU transpiler package reference");
        AssertContains(portableTargets, "<Compile Include=\"$(MSBuildThisFileDirectory)ProGPU.Wpf.Sdk.PortableBootstrap.cs\"", "SDK portable bootstrap injection");
        AssertContains(portableTargets, "_ProGpuWpfSdkCopyPackageRuntimeAssets", "SDK managed runtime copy target");
        AssertContains(portableTargets, "_ProGpuWpfSdkCopyNativeRuntimeAssets", "SDK native runtime copy target");

        AssertContains(portableBootstrap, "[ModuleInitializer]", "SDK bootstrap module initializer");
        AssertContains(portableBootstrap, "WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation", "SDK presentation framework activation bootstrap");
        AssertContains(portableBootstrap, "WpfPortableWindowActivation.TryRegisterPresentationCoreClipboardService", "SDK presentation core clipboard bootstrap");

        AssertNoPackageEntryPrefix(package, "build/", "SDK package build folder");
        AssertNoPackageEntryPrefix(package, "buildTransitive/", "SDK package buildTransitive folder");
        AssertNoPackageEntryPrefix(package, "contentFiles/", "SDK package content files folder");
        AssertNoPackageEntryPrefix(package, "lib/", "SDK package lib folder");
        AssertNoPackageEntryPrefix(package, "ref/", "SDK package ref folder");
        AssertNoPackageEntryPrefix(package, "tools/", "SDK package tools folder");
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
              <config>
                <add key="globalPackagesFolder" value="{SecurityElement.Escape(Path.Combine(workRoot, ".packages"))}" />
              </config>
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
                StartupUri="MainWindow.xaml"
                Startup="OnExternalAppStartup"
                Exit="OnExternalAppExit">
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
                xmlns:local="clr-namespace:ExternalSdkApp"
                xmlns:sys="clr-namespace:System;assembly=System.Private.CoreLib">
                <local:ExternalUpperConverter x:Key="ExternalUpperConverter" />
                <local:ExternalSummaryConverter x:Key="ExternalSummaryConverter" />
                <SolidColorBrush
                    x:Key="ExternalStaticBrush"
                    Color="#A65A2A" />
                <SolidColorBrush
                    x:Key="ExternalUnsharedBrush"
                    x:Shared="False"
                    Color="#C45A2B" />
                <SolidColorBrush
                    x:Key="ExternalDynamicBrush"
                    Color="#225588" />
                <SolidColorBrush
                    x:Key="ExternalFreezableBrush"
                    Color="#5B8C7A"
                    Opacity="0.75" />
                <LinearGradientBrush
                    x:Key="ExternalFreezableGradientBrush"
                    StartPoint="0,0"
                    EndPoint="1,1"
                    Opacity="0.8">
                    <GradientStop Color="#2F6B54" Offset="0" />
                    <GradientStop Color="#B15E3B" Offset="0.5" />
                    <GradientStop Color="#4B5E9D" Offset="1" />
                </LinearGradientBrush>
                <sys:String
                    x:Key="ExternalStaticText">External SDK resource text</sys:String>
                <ObjectDataProvider
                    x:Key="ExternalObjectDataProvider"
                    IsAsynchronous="False"
                    MethodName="CreateSummary"
                    ObjectType="{x:Type local:ExternalResourceFactory}">
                    <ObjectDataProvider.MethodParameters>
                        <sys:String>external-provider</sys:String>
                        <sys:Int32>3</sys:Int32>
                    </ObjectDataProvider.MethodParameters>
                </ObjectDataProvider>
                <XmlDataProvider
                    x:Key="ExternalXmlDataProvider"
                    IsAsynchronous="False"
                    XPath="/external/item">
                    <x:XData>
                        <external xmlns="">
                            <item name="external-xml" value="provider" />
                        </external>
                    </x:XData>
                </XmlDataProvider>
                <ControlTemplate
                    x:Key="ExternalButtonTemplate"
                    TargetType="{x:Type Button}">
                    <Border
                        x:Name="ExternalTemplateRoot"
                        Background="{TemplateBinding Background}"
                        Padding="3">
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="ExternalCommonStates">
                                <VisualState x:Name="Normal">
                                    <Storyboard>
                                        <DoubleAnimation
                                            Storyboard.TargetName="ExternalTemplateContent"
                                            Storyboard.TargetProperty="Opacity"
                                            To="1"
                                            Duration="0:0:0" />
                                    </Storyboard>
                                </VisualState>
                                <VisualState x:Name="Pressed">
                                    <Storyboard>
                                        <DoubleAnimation
                                            Storyboard.TargetName="ExternalTemplateContent"
                                            Storyboard.TargetProperty="Opacity"
                                            To="0.42"
                                            Duration="0:0:0" />
                                    </Storyboard>
                                </VisualState>
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>
                        <ContentPresenter
                            x:Name="ExternalTemplateContent"
                            Content="{TemplateBinding Content}" />
                    </Border>
                </ControlTemplate>
                <Style
                    x:Key="ExternalBasedButtonStyle"
                    TargetType="{x:Type Button}">
                    <Setter Property="Background" Value="#254C6A" />
                    <Setter Property="Foreground" Value="#F4D35E" />
                    <Setter Property="Tag" Value="base-style" />
                </Style>
                <Style
                    x:Key="ExternalTriggeredButtonStyle"
                    BasedOn="{StaticResource ExternalBasedButtonStyle}"
                    TargetType="{x:Type Button}">
                    <Setter Property="Content" Value="External styled button" />
                    <Setter Property="Template" Value="{StaticResource ExternalButtonTemplate}" />
                    <Style.Triggers>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Background" Value="#8E3B46" />
                            <Setter Property="Tag" Value="disabled-style" />
                        </Trigger>
                    </Style.Triggers>
                </Style>
                <ItemsPanelTemplate x:Key="ExternalItemsPanelTemplate">
                    <WrapPanel Orientation="Horizontal" />
                </ItemsPanelTemplate>
                <Style
                    x:Key="ExternalItemContainerStyle"
                    TargetType="{x:Type ListBoxItem}">
                    <Setter Property="Tag" Value="external item container" />
                </Style>
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
                <HierarchicalDataTemplate
                    x:Key="ExternalNodeTemplate"
                    DataType="{x:Type local:ExternalNode}"
                    ItemsSource="{Binding Children}">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock
                            x:Name="ExternalNodeNameText"
                            Text="{Binding Name}" />
                        <TextBlock
                            x:Name="ExternalNodeKindText"
                            Text="{Binding Kind}" />
                    </StackPanel>
                </HierarchicalDataTemplate>
            </ResourceDictionary>
            """);

        WriteFile(
            Path.Combine(appRoot, "MainWindow.xaml"),
            """
            <Window
                x:Class="ExternalSdkApp.MainWindow"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:componentModel="clr-namespace:System.ComponentModel;assembly=WindowsBase"
                xmlns:local="clr-namespace:ExternalSdkApp"
                xmlns:library="clr-namespace:ExternalSdkLibrary;assembly=ExternalSdkLibrary"
                Title="External SDK App"
                Width="320"
                Height="200">
                <Window.Resources>
                    <DataTemplate x:Key="ExternalGroupHeaderTemplate">
                        <TextBlock
                            x:Name="ExternalGroupHeaderText"
                            Text="{Binding Name, StringFormat=Group: {0}}" />
                    </DataTemplate>
                    <CollectionViewSource
                        x:Key="ExternalGroupedItems"
                        Source="{Binding ExternalItems}">
                        <CollectionViewSource.SortDescriptions>
                            <componentModel:SortDescription
                                PropertyName="Name"
                                Direction="Ascending" />
                        </CollectionViewSource.SortDescriptions>
                        <CollectionViewSource.GroupDescriptions>
                            <PropertyGroupDescription PropertyName="Kind" />
                        </CollectionViewSource.GroupDescriptions>
                    </CollectionViewSource>
                </Window.Resources>
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
                        x:Name="ExternalUnsharedBrushTextA"
                        Foreground="{StaticResource ExternalUnsharedBrush}"
                        Text="External SDK unshared resource A" />
                    <TextBlock
                        x:Name="ExternalUnsharedBrushTextB"
                        Foreground="{StaticResource ExternalUnsharedBrush}"
                        Text="External SDK unshared resource B" />
                    <TextBlock
                        x:Name="DynamicResourceText"
                        Foreground="{DynamicResource ExternalDynamicBrush}"
                        Text="External SDK dynamic resource" />
                    <TextBlock
                        x:Name="ExternalStartupResourceText"
                        Foreground="{DynamicResource ExternalStartupBrush}"
                        Text="{DynamicResource ExternalStartupText}" />
                    <TextBlock
                        x:Name="ExternalObjectProviderText"
                        Text="{Binding Source={StaticResource ExternalObjectDataProvider}}" />
                    <TextBlock
                        x:Name="ExternalXmlProviderText"
                        Text="{Binding Source={StaticResource ExternalXmlDataProvider}, XPath=@name}" />
                    <Button
                        x:Name="ExternalStyledButton"
                        Style="{StaticResource ExternalTriggeredButtonStyle}" />
                    <Menu x:Name="ExternalMenu">
                        <MenuItem
                            x:Name="ExternalRootMenuItem"
                            Header="_External">
                            <MenuItem
                                x:Name="ExternalCommandMenuItem"
                                Header="_Command"
                                Command="{x:Static local:MainWindow.ExternalCommand}"
                                CommandParameter="ExternalMenuCommandParameter"
                                CommandTarget="{Binding ElementName=ExternalCommandButton}" />
                            <Separator x:Name="ExternalMenuSeparator" />
                            <MenuItem
                                x:Name="ExternalClickMenuItem"
                                Header="_Click"
                                Click="OnExternalMenuItemClick" />
                            <MenuItem
                                x:Name="ExternalCheckableMenuItem"
                                Header="_Checkable"
                                IsCheckable="True"
                                Checked="OnExternalMenuItemChecked"
                                Unchecked="OnExternalMenuItemUnchecked" />
                        </MenuItem>
                    </Menu>
                    <Button
                        x:Name="ExternalPopupOwnerButton"
                        Content="External popup owner">
                        <Button.ToolTip>
                            <ToolTip
                                x:Name="ExternalToolTip"
                                Placement="Right">
                                <TextBlock
                                    x:Name="ExternalToolTipText"
                                    Text="External tooltip content" />
                            </ToolTip>
                        </Button.ToolTip>
                        <Button.ContextMenu>
                            <ContextMenu x:Name="ExternalContextMenu">
                                <MenuItem
                                    x:Name="ExternalContextCommandMenuItem"
                                    Header="Context command"
                                    Command="{x:Static local:MainWindow.ExternalCommand}"
                                    CommandParameter="ExternalContextCommandParameter"
                                    CommandTarget="{Binding ElementName=ExternalCommandButton}" />
                                <Separator x:Name="ExternalContextMenuSeparator" />
                                <MenuItem
                                    x:Name="ExternalContextClickMenuItem"
                                    Header="Context click"
                                    Click="OnExternalContextMenuItemClick" />
                                <MenuItem
                                    x:Name="ExternalContextCheckableMenuItem"
                                    Header="Context checkable"
                                    IsCheckable="True"
                                    Checked="OnExternalContextMenuItemChecked"
                                    Unchecked="OnExternalContextMenuItemUnchecked" />
                            </ContextMenu>
                        </Button.ContextMenu>
                    </Button>
                    <CheckBox
                        x:Name="ExternalCheckBox"
                        Content="External check"
                        IsChecked="False"
                        Checked="OnExternalCheckBoxChecked"
                        Unchecked="OnExternalCheckBoxUnchecked" />
                    <RadioButton
                        x:Name="ExternalRadioAlpha"
                        Content="External alpha"
                        GroupName="ExternalChoiceGroup"
                        Checked="OnExternalRadioButtonChecked"
                        Unchecked="OnExternalRadioButtonUnchecked" />
                    <RadioButton
                        x:Name="ExternalRadioBeta"
                        Content="External beta"
                        GroupName="ExternalChoiceGroup"
                        IsChecked="True"
                        Checked="OnExternalRadioButtonChecked"
                        Unchecked="OnExternalRadioButtonUnchecked" />
                    <ToggleButton
                        x:Name="ExternalToggleButton"
                        Content="External toggle"
                        IsChecked="False"
                        Checked="OnExternalToggleButtonChecked"
                        Unchecked="OnExternalToggleButtonUnchecked" />
                    <ToolBarTray x:Name="ExternalToolBarTray">
                        <ToolBar x:Name="ExternalToolBar">
                            <Button
                                x:Name="ExternalToolBarCommandButton"
                                Command="{x:Static local:MainWindow.ExternalCommand}"
                                CommandParameter="ExternalToolBarCommandParameter"
                                CommandTarget="{Binding ElementName=ExternalCommandButton}"
                                Content="External toolbar command" />
                            <Separator x:Name="ExternalToolBarSeparator" />
                            <ToggleButton
                                x:Name="ExternalToolBarToggle"
                                Content="External toolbar toggle"
                                IsChecked="False" />
                        </ToolBar>
                    </ToolBarTray>
                    <StatusBar x:Name="ExternalStatusBar">
                        <StatusBarItem x:Name="ExternalStatusBarItem">
                            <TextBlock
                                x:Name="ExternalStatusReadyText"
                                Text="External status ready" />
                        </StatusBarItem>
                        <TextBlock
                            x:Name="ExternalStatusItemText"
                            Text="{Binding SelectedExternalItem.Name}" />
                    </StatusBar>
                    <PasswordBox
                        x:Name="ExternalPasswordBox"
                        MaxLength="16"
                        PasswordChar="*"
                        PasswordChanged="OnExternalPasswordChanged" />
                    <Calendar
                        x:Name="ExternalCalendar"
                        FirstDayOfWeek="Monday"
                        SelectionMode="SingleDate" />
                    <DatePicker
                        x:Name="ExternalDatePicker"
                        FirstDayOfWeek="Monday"
                        SelectedDateFormat="Long" />
                    <Slider
                        x:Name="ExternalSlider"
                        Minimum="0"
                        Maximum="100"
                        SmallChange="2"
                        LargeChange="10"
                        TickFrequency="5"
                        IsSnapToTickEnabled="True"
                        Value="25"
                        ValueChanged="OnExternalSliderValueChanged" />
                    <ProgressBar
                        x:Name="ExternalProgressBar"
                        Minimum="0"
                        Maximum="100"
                        Value="{Binding Value, ElementName=ExternalSlider}" />
                    <Label
                        x:Name="ExternalAccessLabel"
                        Target="{Binding ElementName=ExternalValidationTextBox}"
                        Content="_External access target" />
                    <AccessText
                        x:Name="ExternalStandaloneAccessText"
                        Text="_External standalone access" />
                    <AdornerDecorator x:Name="ExternalAdornerDecorator">
                        <Button
                            x:Name="ExternalAdornedButton"
                            Content="External adorned button"
                            Tag="external adorned button" />
                    </AdornerDecorator>
                    <Grid x:Name="ExternalLayoutGrid">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="*" />
                        </Grid.RowDefinitions>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <TextBlock
                            x:Name="ExternalGridLabel"
                            Grid.Row="0"
                            Grid.Column="0"
                            Text="External grid label" />
                        <TextBlock
                            x:Name="ExternalGridValue"
                            Grid.Row="1"
                            Grid.Column="1"
                            Grid.ColumnSpan="1"
                            Text="{Binding SelectedExternalItem.Name}" />
                    </Grid>
                    <DockPanel
                        x:Name="ExternalDockPanel"
                        LastChildFill="True">
                        <TextBlock
                            x:Name="ExternalDockTop"
                            DockPanel.Dock="Top"
                            Text="External dock top" />
                        <TextBlock
                            x:Name="ExternalDockFill"
                            Text="{Binding SelectedExternalItem.Kind}" />
                    </DockPanel>
                    <Canvas x:Name="ExternalCanvas">
                        <TextBlock
                            x:Name="ExternalCanvasChild"
                            Canvas.Left="12"
                            Canvas.Top="7"
                            Text="External canvas child" />
                    </Canvas>
                    <UniformGrid
                        x:Name="ExternalUniformGrid"
                        Rows="1"
                        Columns="3">
                        <TextBlock Text="One" />
                        <TextBlock Text="Two" />
                        <TextBlock Text="Three" />
                    </UniformGrid>
                    <ContentControl
                        x:Name="ExternalTemplatePresenter"
                        Content="{Binding SelectedExternalItem}"
                        ContentTemplate="{StaticResource ExternalItemTemplate}" />
                    <ListBox
                        x:Name="ExternalItemsList"
                        DisplayMemberPath="Name"
                        ItemsSource="{Binding ExternalItems}"
                        SelectedIndex="1" />
                    <ListBox
                        x:Name="ExternalItemsPanelList"
                        AlternationCount="4"
                        ItemContainerStyle="{StaticResource ExternalItemContainerStyle}"
                        ItemsPanel="{StaticResource ExternalItemsPanelTemplate}"
                        ItemsSource="{Binding ExternalItems}"
                        ItemStringFormat="External item {0}" />
                    <ListBox
                        x:Name="ExternalGroupedItemsList"
                        ItemsSource="{Binding Source={StaticResource ExternalGroupedItems}}">
                        <ListBox.GroupStyle>
                            <GroupStyle HeaderTemplate="{StaticResource ExternalGroupHeaderTemplate}" />
                        </ListBox.GroupStyle>
                    </ListBox>
                    <ListView
                        x:Name="ExternalListView"
                        ItemsSource="{Binding ExternalItems}"
                        SelectedIndex="0">
                        <ListView.View>
                            <GridView>
                                <GridViewColumn
                                    Header="Name"
                                    DisplayMemberBinding="{Binding Name}" />
                                <GridViewColumn
                                    Header="Kind"
                                    DisplayMemberBinding="{Binding Kind}" />
                            </GridView>
                        </ListView.View>
                    </ListView>
                    <DataGrid
                        x:Name="ExternalDataGrid"
                        AutoGenerateColumns="False"
                        CanUserAddRows="False"
                        ItemsSource="{Binding ExternalItems}"
                        SelectedIndex="1">
                        <DataGrid.Columns>
                            <DataGridTextColumn
                                Header="Name"
                                Binding="{Binding Name}" />
                            <DataGridTextColumn
                                Header="Kind"
                                Binding="{Binding Kind}" />
                            <DataGridCheckBoxColumn
                                Header="Active"
                                Binding="{Binding IsActive}" />
                        </DataGrid.Columns>
                    </DataGrid>
                    <TreeView
                        x:Name="ExternalTreeView"
                        ItemTemplate="{StaticResource ExternalNodeTemplate}"
                        ItemsSource="{Binding ExternalNodes}" />
                    <TreeView x:Name="ExternalExplicitTreeView">
                        <TreeViewItem
                            x:Name="ExternalTreeRootItem"
                            Header="External root"
                            IsExpanded="False"
                            Expanded="OnExternalTreeItemExpanded"
                            Collapsed="OnExternalTreeItemCollapsed"
                            Selected="OnExternalTreeItemSelected"
                            Unselected="OnExternalTreeItemUnselected">
                            <TreeViewItem
                                x:Name="ExternalTreeChildItem"
                                Header="External child"
                                Selected="OnExternalTreeItemSelected"
                                Unselected="OnExternalTreeItemUnselected" />
                        </TreeViewItem>
                    </TreeView>
                    <ComboBox
                        x:Name="ExternalComboBox"
                        DisplayMemberPath="Name"
                        ItemsSource="{Binding ExternalItems}"
                        SelectedValuePath="Kind"
                        SelectedValue="{Binding SelectedExternalKind, Mode=TwoWay}"
                        SelectionChanged="OnExternalSelectionChanged" />
                    <TabControl
                        x:Name="ExternalTabControl"
                        SelectedIndex="1"
                        SelectionChanged="OnExternalSelectionChanged">
                        <TabItem
                            x:Name="ExternalFrameworkTab"
                            Header="Framework">
                            <TextBlock
                                x:Name="ExternalFrameworkTabText"
                                Text="Framework tab" />
                        </TabItem>
                        <TabItem
                            x:Name="ExternalRenderingTab"
                            Header="Rendering">
                            <TextBlock
                                x:Name="ExternalRenderingTabText"
                                Text="{Binding SelectedExternalItem.Kind}" />
                        </TabItem>
                    </TabControl>
                    <GroupBox
                        x:Name="ExternalGroupBox"
                        Header="External group">
                        <TextBlock
                            x:Name="ExternalGroupText"
                            Text="{Binding SelectedExternalItem.Name}" />
                    </GroupBox>
                    <Expander
                        x:Name="ExternalExpander"
                        Header="External expander"
                        IsExpanded="False"
                        Expanded="OnExternalExpanderExpanded"
                        Collapsed="OnExternalExpanderCollapsed">
                        <TextBlock
                            x:Name="ExternalExpanderText"
                            Text="External expanded content" />
                    </Expander>
                    <ScrollViewer
                        x:Name="ExternalScrollViewer"
                        HorizontalScrollBarVisibility="Disabled"
                        VerticalScrollBarVisibility="Auto">
                        <StackPanel x:Name="ExternalScrollContent">
                            <TextBlock Text="External scroll row 1" />
                            <TextBlock Text="External scroll row 2" />
                        </StackPanel>
                    </ScrollViewer>
                    <RichTextBox
                        x:Name="ExternalRichTextBox"
                        IsReadOnly="False">
                        <FlowDocument PagePadding="4">
                            <Paragraph>
                                <Run Text="External " />
                                <Bold><Run Text="rich" /></Bold>
                                <Italic><Run Text=" italic" /></Italic>
                                <Underline><Run Text=" underline" /></Underline>
                                <Span><Run Text=" span" /></Span>
                                <LineBreak />
                                <Hyperlink
                                    x:Name="ExternalDocumentLink"
                                    NavigateUri="https://example.test/external-sdk"
                                    RequestNavigate="OnExternalDocumentLinkRequestNavigate">
                                    <Run Text="link" />
                                </Hyperlink>
                                <InlineUIContainer>
                                    <Button Content="external inline button" />
                                </InlineUIContainer>
                            </Paragraph>
                            <List MarkerStyle="Decimal">
                                <ListItem>
                                    <Paragraph><Run Text="External list one" /></Paragraph>
                                </ListItem>
                                <ListItem>
                                    <Paragraph><Run Text="External list two" /></Paragraph>
                                </ListItem>
                            </List>
                            <Section>
                                <Paragraph><Run Text="External section" /></Paragraph>
                            </Section>
                            <BlockUIContainer>
                                <Button Content="external block button" />
                            </BlockUIContainer>
                            <Table CellSpacing="0">
                                <Table.Columns>
                                    <TableColumn Width="96" />
                                    <TableColumn Width="96" />
                                </Table.Columns>
                                <TableRowGroup>
                                    <TableRow>
                                        <TableCell><Paragraph><Run Text="External cell alpha" /></Paragraph></TableCell>
                                        <TableCell><Paragraph><Run Text="External cell beta" /></Paragraph></TableCell>
                                    </TableRow>
                                </TableRowGroup>
                            </Table>
                        </FlowDocument>
                    </RichTextBox>
                    <TextBlock
                        x:Name="ExternalConverterText"
                        Text="{Binding SelectedExternalItem.Name, Converter={StaticResource ExternalUpperConverter}, ConverterParameter=converted}" />
                    <TextBlock x:Name="ExternalMultiBindingText">
                        <TextBlock.Text>
                            <MultiBinding Converter="{StaticResource ExternalSummaryConverter}">
                                <Binding Path="SelectedExternalItem.Name" />
                                <Binding Path="SelectedExternalItem.Kind" />
                            </MultiBinding>
                        </TextBlock.Text>
                    </TextBlock>
                    <TextBlock x:Name="ExternalPriorityBindingText">
                        <TextBlock.Text>
                            <PriorityBinding>
                                <Binding Path="MissingExternalItem.Value" />
                                <Binding Path="SelectedExternalItem.Kind" />
                            </PriorityBinding>
                        </TextBlock.Text>
                    </TextBlock>
                    <TextBox
                        x:Name="ExternalValidationTextBox"
                        TextChanged="OnExternalValidationTextChanged">
                        <TextBox.Text>
                            <Binding
                                Path="ValidationText"
                                Mode="TwoWay"
                                UpdateSourceTrigger="Explicit">
                                <Binding.ValidationRules>
                                    <local:ExternalNonEmptyValidationRule />
                                </Binding.ValidationRules>
                            </Binding>
                        </TextBox.Text>
                    </TextBox>
                    <StackPanel
                        x:Name="ExternalBindingGroupPanel"
                        Margin="0,4,0,0">
                        <StackPanel.BindingGroup>
                            <BindingGroup Name="ExternalBindingGroup">
                                <BindingGroup.ValidationRules>
                                    <local:ExternalBindingGroupValidationRule
                                        FirstProperty="BindingGroupFirstName"
                                        RequiredPrefix="group:"
                                        SecondProperty="BindingGroupLastName" />
                                </BindingGroup.ValidationRules>
                            </BindingGroup>
                        </StackPanel.BindingGroup>
                        <TextBox
                            x:Name="ExternalBindingGroupFirstBox"
                            Text="{Binding BindingGroupFirstName, UpdateSourceTrigger=Explicit}" />
                        <TextBox
                            x:Name="ExternalBindingGroupLastBox"
                            Text="{Binding BindingGroupLastName, UpdateSourceTrigger=Explicit}" />
                    </StackPanel>
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
            using System.Globalization;
            using System.IO;
            using System.Linq;
            using System.Reflection;
            using System.Windows;
            using System.Windows.Controls;
            using System.Windows.Controls.Primitives;
            using System.Windows.Data;
            using System.Windows.Documents;
            using System.Windows.Input;
            using System.Windows.Markup;
            using System.Windows.Media;
            using System.Windows.Media.Animation;
            using System.Windows.Media.Imaging;
            using System.Windows.Navigation;
            using System.Windows.Shell;
            using System.Windows.Threading;
            using ExternalSdkLibrary;
            using Microsoft.Win32;

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
                    new ExternalItem("Alpha", "Framework", true),
                    new ExternalItem("Beta", "Rendering", false)
                ];

                public ObservableCollection<ExternalNode> ExternalNodes { get; } =
                [
                    new ExternalNode(
                        "Root",
                        "Framework",
                        [
                            new ExternalNode("Child", "Rendering")
                        ]),
                    new ExternalNode("Sibling", "Data")
                ];

                public ExternalItem SelectedExternalItem => ExternalItems[0];

                public string SelectedExternalKind { get; set; } = "Rendering";

                public string ValidationText { get; set; } = "valid external text";

                public string BindingGroupFirstName { get; set; } = "group: Ada";

                public string BindingGroupLastName { get; set; } = "group: Lovelace";

                public int ExternalSelectionChangedCount { get; private set; }

                public string? LastExternalSelectionSourceName { get; private set; }

                public int ExternalExpanderExpandedCount { get; private set; }

                public int ExternalExpanderCollapsedCount { get; private set; }

                public int ExternalDocumentLinkRequestNavigateCount { get; private set; }

                public string? LastExternalDocumentLinkRequestNavigateSenderName { get; private set; }

                public string? LastExternalDocumentLinkRequestNavigateUri { get; private set; }

                public string? LastExternalDocumentLinkRequestNavigateRoutedEventName { get; private set; }

                public int ExternalTreeExpandedCount { get; private set; }

                public int ExternalTreeCollapsedCount { get; private set; }

                public int ExternalTreeSelectedCount { get; private set; }

                public int ExternalTreeUnselectedCount { get; private set; }

                public string? LastExternalTreeExpandedOriginalSourceName { get; private set; }

                public string? LastExternalTreeCollapsedOriginalSourceName { get; private set; }

                public string? LastExternalTreeSelectedOriginalSourceName { get; private set; }

                public string? LastExternalTreeUnselectedOriginalSourceName { get; private set; }

                public int ExternalMenuClickCount { get; private set; }

                public int ExternalMenuCheckedCount { get; private set; }

                public int ExternalMenuUncheckedCount { get; private set; }

                public int ExternalContextMenuClickCount { get; private set; }

                public int ExternalContextMenuCheckedCount { get; private set; }

                public int ExternalContextMenuUncheckedCount { get; private set; }

                public string? LastExternalMenuRoutedEventName { get; private set; }

                public string? LastExternalContextMenuRoutedEventName { get; private set; }

                public int ExternalCheckBoxCheckedCount { get; private set; }

                public int ExternalCheckBoxUncheckedCount { get; private set; }

                public int ExternalRadioButtonCheckedCount { get; private set; }

                public int ExternalRadioButtonUncheckedCount { get; private set; }

                public int ExternalToggleButtonCheckedCount { get; private set; }

                public int ExternalToggleButtonUncheckedCount { get; private set; }

                public int ExternalPasswordChangedCount { get; private set; }

                public int ExternalValidationTextChangedCount { get; private set; }

                public string? LastExternalValidationText { get; private set; }

                public int ExternalSliderValueChangedCount { get; private set; }

                public double LastExternalSliderValue { get; private set; }

                public string? LastExternalCheckBoxRoutedEventName { get; private set; }

                public string? LastExternalRadioButtonCheckedName { get; private set; }

                public string? LastExternalRadioButtonUncheckedName { get; private set; }

                public string? LastExternalToggleButtonRoutedEventName { get; private set; }

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

                private void OnExternalSelectionChanged(object sender, SelectionChangedEventArgs e)
                {
                    ExternalSelectionChangedCount++;
                    LastExternalSelectionSourceName = (sender as FrameworkElement)?.Name;
                }

                private void OnExternalExpanderExpanded(object sender, RoutedEventArgs e)
                {
                    ExternalExpanderExpandedCount++;
                }

                private void OnExternalExpanderCollapsed(object sender, RoutedEventArgs e)
                {
                    ExternalExpanderCollapsedCount++;
                }

                private void OnExternalDocumentLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
                {
                    ExternalDocumentLinkRequestNavigateCount++;
                    LastExternalDocumentLinkRequestNavigateSenderName = (sender as TextElement)?.Name;
                    LastExternalDocumentLinkRequestNavigateUri = e.Uri?.ToString();
                    LastExternalDocumentLinkRequestNavigateRoutedEventName = e.RoutedEvent?.Name;
                    e.Handled = true;
                }

                private void OnExternalTreeItemExpanded(object sender, RoutedEventArgs e)
                {
                    ExternalTreeExpandedCount++;
                    LastExternalTreeExpandedOriginalSourceName = (e.OriginalSource as FrameworkElement)?.Name;
                }

                private void OnExternalTreeItemCollapsed(object sender, RoutedEventArgs e)
                {
                    ExternalTreeCollapsedCount++;
                    LastExternalTreeCollapsedOriginalSourceName = (e.OriginalSource as FrameworkElement)?.Name;
                }

                private void OnExternalTreeItemSelected(object sender, RoutedEventArgs e)
                {
                    ExternalTreeSelectedCount++;
                    LastExternalTreeSelectedOriginalSourceName = (e.OriginalSource as FrameworkElement)?.Name;
                }

                private void OnExternalTreeItemUnselected(object sender, RoutedEventArgs e)
                {
                    ExternalTreeUnselectedCount++;
                    LastExternalTreeUnselectedOriginalSourceName = (e.OriginalSource as FrameworkElement)?.Name;
                }

                private void OnExternalMenuItemClick(object sender, RoutedEventArgs e)
                {
                    ExternalMenuClickCount++;
                    LastExternalMenuRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalMenuItemChecked(object sender, RoutedEventArgs e)
                {
                    ExternalMenuCheckedCount++;
                    LastExternalMenuRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalMenuItemUnchecked(object sender, RoutedEventArgs e)
                {
                    ExternalMenuUncheckedCount++;
                    LastExternalMenuRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalContextMenuItemClick(object sender, RoutedEventArgs e)
                {
                    ExternalContextMenuClickCount++;
                    LastExternalContextMenuRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalContextMenuItemChecked(object sender, RoutedEventArgs e)
                {
                    ExternalContextMenuCheckedCount++;
                    LastExternalContextMenuRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalContextMenuItemUnchecked(object sender, RoutedEventArgs e)
                {
                    ExternalContextMenuUncheckedCount++;
                    LastExternalContextMenuRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalCheckBoxChecked(object sender, RoutedEventArgs e)
                {
                    ExternalCheckBoxCheckedCount++;
                    LastExternalCheckBoxRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalCheckBoxUnchecked(object sender, RoutedEventArgs e)
                {
                    ExternalCheckBoxUncheckedCount++;
                    LastExternalCheckBoxRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalRadioButtonChecked(object sender, RoutedEventArgs e)
                {
                    ExternalRadioButtonCheckedCount++;
                    LastExternalRadioButtonCheckedName = (sender as FrameworkElement)?.Name;
                }

                private void OnExternalRadioButtonUnchecked(object sender, RoutedEventArgs e)
                {
                    ExternalRadioButtonUncheckedCount++;
                    LastExternalRadioButtonUncheckedName = (sender as FrameworkElement)?.Name;
                }

                private void OnExternalToggleButtonChecked(object sender, RoutedEventArgs e)
                {
                    ExternalToggleButtonCheckedCount++;
                    LastExternalToggleButtonRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalToggleButtonUnchecked(object sender, RoutedEventArgs e)
                {
                    ExternalToggleButtonUncheckedCount++;
                    LastExternalToggleButtonRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalPasswordChanged(object sender, RoutedEventArgs e)
                {
                    ExternalPasswordChangedCount++;
                }

                private void OnExternalValidationTextChanged(object sender, TextChangedEventArgs e)
                {
                    ExternalValidationTextChangedCount++;
                    LastExternalValidationText = (sender as TextBox)?.Text;
                }

                private void OnExternalSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
                {
                    ExternalSliderValueChangedCount++;
                    LastExternalSliderValue = e.NewValue;
                }

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
                public ExternalItem(string name, string kind, bool isActive = false)
                {
                    Name = name;
                    Kind = kind;
                    IsActive = isActive;
                }

                public string Name { get; }

                public string Kind { get; }

                public bool IsActive { get; set; }
            }

            public static class ExternalResourceFactory
            {
                public static string CreateSummary(string prefix, int value)
                {
                    return $"{prefix}:{value}";
                }
            }

            public sealed class ExternalNode
            {
                public ExternalNode(string name, string kind)
                    : this(name, kind, [])
                {
                }

                public ExternalNode(string name, string kind, IEnumerable<ExternalNode> children)
                {
                    Name = name;
                    Kind = kind;
                    Children = new ObservableCollection<ExternalNode>(children);
                }

                public string Name { get; }

                public string Kind { get; }

                public ObservableCollection<ExternalNode> Children { get; }
            }

            public sealed class ExternalUpperConverter : IValueConverter
            {
                public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
                {
                    return $"{value?.ToString()?.ToUpperInvariant()}:{parameter}";
                }

                public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                {
                    throw new NotSupportedException();
                }
            }

            public sealed class ExternalSummaryConverter : IMultiValueConverter
            {
                public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
                {
                    return string.Join("|", values.Select(value => value?.ToString()));
                }

                public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
                {
                    throw new NotSupportedException();
                }
            }

            public sealed class ExternalNonEmptyValidationRule : ValidationRule
            {
                public override ValidationResult Validate(object value, CultureInfo cultureInfo)
                {
                    string? text = value?.ToString();
                    return string.IsNullOrWhiteSpace(text)
                        ? new ValidationResult(false, "External value is required")
                        : ValidationResult.ValidResult;
                }
            }

            public sealed class ExternalBindingGroupValidationRule : ValidationRule
            {
                public string FirstProperty { get; set; } = string.Empty;

                public string SecondProperty { get; set; } = string.Empty;

                public string RequiredPrefix { get; set; } = string.Empty;

                public override ValidationResult Validate(object value, CultureInfo cultureInfo)
                {
                    if (value is not BindingGroup bindingGroup)
                    {
                        return new ValidationResult(false, "Expected a BindingGroup value.");
                    }

                    foreach (object item in bindingGroup.Items)
                    {
                        if (!HasRequiredPrefix(bindingGroup, item, FirstProperty) ||
                            !HasRequiredPrefix(bindingGroup, item, SecondProperty))
                        {
                            return new ValidationResult(false, $"External BindingGroup values must start with '{RequiredPrefix}'.");
                        }
                    }

                    return ValidationResult.ValidResult;
                }

                private bool HasRequiredPrefix(BindingGroup bindingGroup, object item, string propertyName)
                {
                    object value = bindingGroup.GetValue(item, propertyName);
                    string text = value?.ToString() ?? string.Empty;
                    return text.StartsWith(RequiredPrefix, StringComparison.Ordinal);
                }
            }

            public sealed class ExternalAdorner : Adorner
            {
                public ExternalAdorner(UIElement adornedElement)
                    : base(adornedElement)
                {
                    IsHitTestVisible = false;
                }

                protected override void OnRender(DrawingContext drawingContext)
                {
                    base.OnRender(drawingContext);

                    var adornedBounds = new Rect(AdornedElement.RenderSize);
                    drawingContext.DrawRectangle(null, new Pen(Brushes.LimeGreen, 1.0), adornedBounds);
                }
            }

            public partial class App
            {
                private static bool s_externalRunValidationRequested;

                public static int ExternalStartupEventCount { get; private set; }

                public static int ExternalStartupArgumentCount { get; private set; }

                public static int ExternalExitEventCount { get; private set; }

                public static int ExternalExitCode { get; private set; }

                public static bool ExternalRunValidated { get; private set; }

                protected override void OnStartup(StartupEventArgs e)
                {
                    if (Environment.GetEnvironmentVariable("PROGPU_WPF_EXTERNAL_VALIDATE") == "1")
                    {
                        ExternalSdkValidation.Run();
                        Shutdown();
                        return;
                    }

                    if (Environment.GetEnvironmentVariable("PROGPU_WPF_EXTERNAL_RUN_VALIDATE") == "1")
                    {
                        s_externalRunValidationRequested = true;
                        base.OnStartup(e);
                        Dispatcher.BeginInvoke(
                            DispatcherPriority.ApplicationIdle,
                            new Action(ExternalSdkValidation.ValidateApplicationRunAndShutdown));
                        return;
                    }

                    base.OnStartup(e);
                }

                protected override void OnExit(ExitEventArgs e)
                {
                    base.OnExit(e);

                    if (s_externalRunValidationRequested)
                    {
                        ExternalSdkValidation.ValidateApplicationExit(e.ApplicationExitCode);
                        Console.WriteLine("External SDK Application.Run validation succeeded.");
                    }
                }

                private void OnExternalAppStartup(object sender, StartupEventArgs e)
                {
                    ExternalStartupEventCount++;
                    ExternalStartupArgumentCount = e.Args.Length;
                    Resources["ExternalStartupText"] = "External SDK startup resource";
                    Resources["ExternalStartupBrush"] = new SolidColorBrush(Color.FromRgb(0x17, 0x62, 0x83));
                }

                private void OnExternalAppExit(object sender, ExitEventArgs e)
                {
                    ExternalExitEventCount++;
                    ExternalExitCode = e.ApplicationExitCode;
                }

                public static void MarkExternalRunValidated()
                {
                    ExternalRunValidated = true;
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
                    ValidateSystemParameters(window);
                    ValidateWindowChrome(window);
                    ValidateSystemCommands(window);
                    ValidateMessageBox(window);
                    ValidateFileDialogs(window);
                    ValidateClipboard();
                    ValidateFreezableResources();
                    ValidateManagedImagingObjects();
                    ValidateLooseXamlReaderWriter();
                    ValidateDataProviders(window);
                    ValidateBindings(window);
                    ValidateBindingGroup(window);
                    ValidateStylesAndTemplates(window);
                    ValidateMenusAndChoiceControls(window);
                    ValidateToolbarStatusRangePasswordDateControls(window);
                    ValidateAdornerDecorator(window);
                    ValidateLayoutsAndItems(window);
                    ValidateSelectorsAndContent(window);
                    ValidateRichDocuments(window);
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

                public static void ValidateApplicationRunAndShutdown()
                {
                    var app = RequireType<App>(
                        Application.Current,
                        "external SDK current application");
                    AssertEqual(1, App.ExternalStartupEventCount, "external SDK application startup event count");
                    AssertEqual(0, App.ExternalStartupArgumentCount, "external SDK application startup argument count");
                    AssertEqual(ShutdownMode.OnLastWindowClose, app.ShutdownMode, "external SDK application shutdown mode");

                    var window = RequireType<MainWindow>(
                        app.MainWindow,
                        "external SDK application main window");
                    AssertEqual(true, window.IsVisible, "external SDK application main window visibility");
                    AssertAtLeast(1, app.Windows.Count, "external SDK application windows count");

                    bool containsMainWindow = false;
                    foreach (Window candidate in app.Windows)
                    {
                        if (ReferenceEquals(candidate, window))
                        {
                            containsMainWindow = true;
                            break;
                        }
                    }

                    AssertEqual(true, containsMainWindow, "external SDK application windows contains main window");

                    var titleText = RequireType<TextBlock>(
                        window.FindName("TitleText"),
                        "external SDK Application.Run startup window title");
                    AssertEqual("External SDK app", titleText.Text, "external SDK Application.Run startup window text");

                    AssertEqual(
                        "External SDK startup resource",
                        app.Resources["ExternalStartupText"],
                        "external SDK application startup text resource");
                    AssertBrushColor(
                        RequireType<Brush>(app.Resources["ExternalStartupBrush"], "external SDK application startup brush resource"),
                        "#FF176283",
                        "external SDK application startup brush resource");

                    var startupResourceText = RequireType<TextBlock>(
                        window.FindName("ExternalStartupResourceText"),
                        "external SDK startup resource text block");
                    AssertEqual("External SDK startup resource", startupResourceText.Text, "external SDK startup dynamic resource text");
                    AssertBrushColor(startupResourceText.Foreground, "#FF176283", "external SDK startup dynamic resource foreground");
                    ValidateVisualStateTransitions(window);
                    ValidateAdornerLayer(window);

                    App.MarkExternalRunValidated();
                    app.Shutdown(0);
                }

                public static void ValidateApplicationExit(int exitCode)
                {
                    AssertEqual(0, exitCode, "external SDK application exit code");
                    AssertEqual(1, App.ExternalStartupEventCount, "external SDK application exit-observed startup event count");
                    AssertEqual(1, App.ExternalExitEventCount, "external SDK application exit event count");
                    AssertEqual(0, App.ExternalExitCode, "external SDK application exit event code");
                    AssertEqual(true, App.ExternalRunValidated, "external SDK application run validated before exit");
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

                    var unsharedBrushTextA = RequireType<TextBlock>(
                        window.FindName("ExternalUnsharedBrushTextA"),
                        "external SDK x:Shared=false first consumer text block");
                    var unsharedBrushTextB = RequireType<TextBlock>(
                        window.FindName("ExternalUnsharedBrushTextB"),
                        "external SDK x:Shared=false second consumer text block");
                    AssertBrushColor(unsharedBrushTextA.Foreground, "#FFC45A2B", "external SDK x:Shared=false StaticResource first consumer foreground");
                    AssertBrushColor(unsharedBrushTextB.Foreground, "#FFC45A2B", "external SDK x:Shared=false StaticResource second consumer foreground");
                    AssertEqual(
                        false,
                        ReferenceEquals(unsharedBrushTextA.Foreground, unsharedBrushTextB.Foreground),
                        "external SDK x:Shared=false StaticResource consumers");
                    var unsharedBrushLookupA = RequireType<SolidColorBrush>(
                        appResources["ExternalUnsharedBrush"],
                        "external SDK x:Shared=false first dictionary brush lookup");
                    var unsharedBrushLookupB = RequireType<SolidColorBrush>(
                        appResources["ExternalUnsharedBrush"],
                        "external SDK x:Shared=false second dictionary brush lookup");
                    AssertBrushColor(unsharedBrushLookupA, "#FFC45A2B", "external SDK x:Shared=false first dictionary brush color");
                    AssertBrushColor(unsharedBrushLookupB, "#FFC45A2B", "external SDK x:Shared=false second dictionary brush color");
                    AssertEqual(
                        false,
                        ReferenceEquals(unsharedBrushLookupA, unsharedBrushLookupB),
                        "external SDK x:Shared=false dictionary lookup");

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

                private static void ValidateSystemParameters(FrameworkElement resourceOwner)
                {
                    AssertSystemParameterMetric(
                        resourceOwner,
                        "FocusBorderWidth",
                        SystemParameters.FocusBorderWidth,
                        SystemParameters.FocusBorderWidthKey,
                        1.0);
                    AssertSystemParameterMetric(
                        resourceOwner,
                        "FocusBorderHeight",
                        SystemParameters.FocusBorderHeight,
                        SystemParameters.FocusBorderHeightKey,
                        1.0);
                    AssertSystemParameterMetric(
                        resourceOwner,
                        "PrimaryScreenWidth",
                        SystemParameters.PrimaryScreenWidth,
                        SystemParameters.PrimaryScreenWidthKey,
                        1024.0);
                    AssertSystemParameterMetric(
                        resourceOwner,
                        "PrimaryScreenHeight",
                        SystemParameters.PrimaryScreenHeight,
                        SystemParameters.PrimaryScreenHeightKey,
                        768.0);
                    AssertSystemParameterMetric(
                        resourceOwner,
                        "VerticalScrollBarWidth",
                        SystemParameters.VerticalScrollBarWidth,
                        SystemParameters.VerticalScrollBarWidthKey,
                        17.0);
                    AssertSystemParameterMetric(
                        resourceOwner,
                        "HorizontalScrollBarHeight",
                        SystemParameters.HorizontalScrollBarHeight,
                        SystemParameters.HorizontalScrollBarHeightKey,
                        17.0);
                    AssertSystemParameterMetric(
                        resourceOwner,
                        "CaretWidth",
                        SystemParameters.CaretWidth,
                        SystemParameters.CaretWidthKey,
                        1.0);
                    AssertSystemParameterRect(
                        resourceOwner,
                        "WorkArea",
                        SystemParameters.WorkArea,
                        SystemParameters.WorkAreaKey,
                        0.0,
                        0.0,
                        1024.0,
                        768.0);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "HighContrast",
                        SystemParameters.HighContrast,
                        SystemParameters.HighContrastKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "DropShadow",
                        SystemParameters.DropShadow,
                        SystemParameters.DropShadowKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "FlatMenu",
                        SystemParameters.FlatMenu,
                        SystemParameters.FlatMenuKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "MenuDropAlignment",
                        SystemParameters.MenuDropAlignment,
                        SystemParameters.MenuDropAlignmentKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "MenuFade",
                        SystemParameters.MenuFade,
                        SystemParameters.MenuFadeKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "MenuShowDelay",
                        SystemParameters.MenuShowDelay,
                        SystemParameters.MenuShowDelayKey,
                        400);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "ClientAreaAnimation",
                        SystemParameters.ClientAreaAnimation,
                        SystemParameters.ClientAreaAnimationKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "CursorShadow",
                        SystemParameters.CursorShadow,
                        SystemParameters.CursorShadowKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "GradientCaptions",
                        SystemParameters.GradientCaptions,
                        SystemParameters.GradientCaptionsKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "HotTracking",
                        SystemParameters.HotTracking,
                        SystemParameters.HotTrackingKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "ListBoxSmoothScrolling",
                        SystemParameters.ListBoxSmoothScrolling,
                        SystemParameters.ListBoxSmoothScrollingKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "SelectionFade",
                        SystemParameters.SelectionFade,
                        SystemParameters.SelectionFadeKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "StylusHotTracking",
                        SystemParameters.StylusHotTracking,
                        SystemParameters.StylusHotTrackingKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "UIEffects",
                        SystemParameters.UIEffects,
                        SystemParameters.UIEffectsKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "MinimizeAnimation",
                        SystemParameters.MinimizeAnimation,
                        SystemParameters.MinimizeAnimationKey,
                        false);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "Border",
                        SystemParameters.Border,
                        SystemParameters.BorderKey,
                        1);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "DragFullWindows",
                        SystemParameters.DragFullWindows,
                        SystemParameters.DragFullWindowsKey,
                        true);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "ForegroundFlashCount",
                        SystemParameters.ForegroundFlashCount,
                        SystemParameters.ForegroundFlashCountKey,
                        7);
                    AssertSystemParameterValue(
                        resourceOwner,
                        "WheelScrollLines",
                        SystemParameters.WheelScrollLines,
                        SystemParameters.WheelScrollLinesKey,
                        3);
                }

                private static void AssertSystemParameterMetric(
                    FrameworkElement resourceOwner,
                    string propertyName,
                    double value,
                    object resourceKey,
                    double expectedNonWindowsValue)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        if (value < 0)
                        {
                            throw new InvalidOperationException(
                                $"Expected external SDK SystemParameters.{propertyName} to be non-negative, but found '{value}'.");
                        }
                    }
                    else
                    {
                        AssertClose(expectedNonWindowsValue, value, $"external SDK SystemParameters.{propertyName}");
                    }

                    object resourceValue = resourceOwner.TryFindResource(resourceKey)
                        ?? throw new InvalidOperationException($"Expected external SDK SystemParameters.{propertyName} resource.");
                    AssertClose(
                        value,
                        Convert.ToDouble(resourceValue, CultureInfo.InvariantCulture),
                        $"external SDK SystemParameters.{propertyName} resource");
                }

                private static void AssertSystemParameterRect(
                    FrameworkElement resourceOwner,
                    string propertyName,
                    Rect value,
                    object resourceKey,
                    double expectedX,
                    double expectedY,
                    double expectedWidth,
                    double expectedHeight)
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        AssertClose(expectedX, value.X, $"external SDK SystemParameters.{propertyName}.X");
                        AssertClose(expectedY, value.Y, $"external SDK SystemParameters.{propertyName}.Y");
                        AssertClose(expectedWidth, value.Width, $"external SDK SystemParameters.{propertyName}.Width");
                        AssertClose(expectedHeight, value.Height, $"external SDK SystemParameters.{propertyName}.Height");
                    }

                    object resourceValue = resourceOwner.TryFindResource(resourceKey)
                        ?? throw new InvalidOperationException($"Expected external SDK SystemParameters.{propertyName} resource.");
                    AssertEqual(value, (Rect)resourceValue, $"external SDK SystemParameters.{propertyName} resource");
                }

                private static void AssertSystemParameterValue<T>(
                    FrameworkElement resourceOwner,
                    string propertyName,
                    T value,
                    object resourceKey,
                    T expectedNonWindowsValue)
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        AssertEqual(expectedNonWindowsValue, value, $"external SDK SystemParameters.{propertyName}");
                    }

                    object resourceValue = resourceOwner.TryFindResource(resourceKey)
                        ?? throw new InvalidOperationException($"Expected external SDK SystemParameters.{propertyName} resource.");
                    AssertEqual(value, (T)resourceValue, $"external SDK SystemParameters.{propertyName} resource");
                }

                private static void ValidateWindowChrome(MainWindow window)
                {
                    var chrome = new WindowChrome
                    {
                        CaptionHeight = 32.0,
                        ResizeBorderThickness = new Thickness(6.0),
                        GlassFrameThickness = new Thickness(0.0),
                        NonClientFrameEdges = NonClientFrameEdges.Top,
                        UseAeroCaptionButtons = false
                    };

                    WindowChrome.SetWindowChrome(window, chrome);
                    AssertEqual(chrome, WindowChrome.GetWindowChrome(window), "external SDK WindowChrome attached value");
                    AssertEqual(32.0, chrome.CaptionHeight, "external SDK WindowChrome caption height");
                    AssertEqual(NonClientFrameEdges.Top, chrome.NonClientFrameEdges, "external SDK WindowChrome non-client frame edges");

                    WindowChrome.SetIsHitTestVisibleInChrome(window, true);
                    AssertEqual(true, WindowChrome.GetIsHitTestVisibleInChrome(window), "external SDK WindowChrome hit-test attached value");

                    WindowChrome.SetWindowChrome(window, null);
                    if (WindowChrome.GetWindowChrome(window) is not null)
                    {
                        throw new InvalidOperationException("Expected external SDK WindowChrome cleared value to be null.");
                    }
                }

                private static void ValidateSystemCommands(MainWindow window)
                {
                    SystemCommands.MaximizeWindow(window);
                    AssertEqual(WindowState.Maximized, window.WindowState, "external SDK SystemCommands maximize state");

                    SystemCommands.MinimizeWindow(window);
                    AssertEqual(WindowState.Minimized, window.WindowState, "external SDK SystemCommands minimize state");

                    SystemCommands.RestoreWindow(window);
                    AssertEqual(WindowState.Normal, window.WindowState, "external SDK SystemCommands restore state");

                    SystemCommands.ShowSystemMenu(window, new Point(12.0, 24.0));
                    AssertEqual(WindowState.Normal, window.WindowState, "external SDK SystemCommands show system menu no-op state");
                }

                private static void ValidateMessageBox(Window window)
                {
                    Type serviceType = typeof(MessageBox).Assembly.GetType(
                            "System.Windows.PortableMessageBoxService",
                            throwOnError: false)
                        ?? throw new TypeLoadException("System.Windows.PortableMessageBoxService");
                    var isEnabledProperty = serviceType.GetProperty(
                            "IsEnabled",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? throw new MissingMemberException(serviceType.FullName, "IsEnabled");
                    if (!OperatingSystem.IsWindows())
                    {
                        AssertEqual(
                            true,
                            (bool)(isEnabledProperty.GetValue(null) ?? false),
                            "external SDK portable MessageBox service enabled");
                    }

                    IDisposable? registration = RegisterDeterministicMessageBox(serviceType);
                    try
                    {
                        var noOwnerResult = MessageBox.Show(
                            "external SDK message",
                            "external SDK caption",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Warning,
                            MessageBoxResult.No,
                            MessageBoxOptions.None);
                        AssertEqual(
                            MessageBoxResult.No,
                            noOwnerResult,
                            "external SDK MessageBox no-owner default result");

                        var ownerResult = MessageBox.Show(
                            window,
                            "external SDK owner message",
                            "external SDK owner caption",
                            MessageBoxButton.OKCancel,
                            MessageBoxImage.Information,
                            MessageBoxResult.None,
                            MessageBoxOptions.None);
                        AssertEqual(
                            MessageBoxResult.OK,
                            ownerResult,
                            "external SDK MessageBox owner fallback result");
                    }
                    finally
                    {
                        registration?.Dispose();
                    }
                }

                private static IDisposable? RegisterDeterministicMessageBox(Type serviceType)
                {
                    var registerMethod = serviceType.GetMethod(
                            "Register",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            binder: null,
                            types: new[] { typeof(Func<object, object>) },
                            modifiers: null)
                        ?? throw new MissingMethodException(serviceType.FullName, "Register");

                    return registerMethod.Invoke(
                        null,
                        new object[] { (Func<object, object>)ShowDeterministicMessageBox }) as IDisposable;
                }

                private static object ShowDeterministicMessageBox(object request)
                {
                    return ReadPortableRequestString(request, "FallbackResult");
                }

                private static void ValidateFileDialogs(Window window)
                {
                    Type serviceType = typeof(OpenFileDialog).Assembly.GetType(
                            "Microsoft.Win32.PortableFileDialogService",
                            throwOnError: false)
                        ?? throw new TypeLoadException("Microsoft.Win32.PortableFileDialogService");
                    var isEnabledProperty = serviceType.GetProperty(
                            "IsEnabled",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? throw new MissingMemberException(serviceType.FullName, "IsEnabled");
                    if (!OperatingSystem.IsWindows())
                    {
                        AssertEqual(
                            true,
                            (bool)(isEnabledProperty.GetValue(null) ?? false),
                            "external SDK portable file dialog service enabled");
                    }

                    var registerMethod = serviceType.GetMethod(
                            "Register",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            binder: null,
                            types: new[] { typeof(Func<object, string?>) },
                            modifiers: null)
                        ?? throw new MissingMethodException(serviceType.FullName, "Register");

                    string tempDirectory = Path.Combine(
                        Path.GetTempPath(),
                        "progpu-wpf-external-file-dialog-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDirectory);
                    string openPath = Path.Combine(tempDirectory, "open.txt");
                    string savePathWithoutExtension = Path.Combine(tempDirectory, "saved");
                    string savePath = savePathWithoutExtension + ".txt";
                    File.WriteAllText(openPath, "external SDK file dialog");

                    int requestCount = 0;
                    var seenKinds = new List<string>();
                    Func<object, string?> handler = request =>
                    {
                        string kind = ReadPortableRequestString(request, "Kind");
                        seenKinds.Add(kind);
                        requestCount++;

                        return kind switch
                        {
                            "SaveFile" => savePathWithoutExtension,
                            "PickFolder" => tempDirectory,
                            _ => openPath
                        };
                    };

                    IDisposable? registration = null;
                    try
                    {
                        registration = (IDisposable?)registerMethod.Invoke(null, new object[] { handler });

                        var openDialog = new OpenFileDialog
                        {
                            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
                        };
                        AssertEqual(true, openDialog.ShowDialog(), "external SDK OpenFileDialog result");
                        AssertEqual(openPath, openDialog.FileName, "external SDK OpenFileDialog FileName");
                        AssertEqual("open.txt", openDialog.SafeFileName, "external SDK OpenFileDialog SafeFileName");

                        var saveDialog = new SaveFileDialog
                        {
                            DefaultExt = "txt",
                            OverwritePrompt = false
                        };
                        AssertEqual(true, saveDialog.ShowDialog(window), "external SDK owner SaveFileDialog result");
                        AssertEqual(savePath, saveDialog.FileName, "external SDK owner SaveFileDialog FileName");
                        AssertEqual("saved.txt", saveDialog.SafeFileName, "external SDK owner SaveFileDialog SafeFileName");

                        var folderDialog = new OpenFolderDialog();
                        AssertEqual(true, folderDialog.ShowDialog(window), "external SDK owner OpenFolderDialog result");
                        AssertEqual(tempDirectory, folderDialog.FolderName, "external SDK owner OpenFolderDialog FolderName");
                        AssertEqual(Path.GetFileName(tempDirectory), folderDialog.SafeFolderName, "external SDK owner OpenFolderDialog SafeFolderName");

                        AssertEqual(3, requestCount, "external SDK file dialog request count");
                        AssertEqual("OpenFile", seenKinds[0], "external SDK file dialog open request kind");
                        AssertEqual("SaveFile", seenKinds[1], "external SDK file dialog save request kind");
                        AssertEqual("PickFolder", seenKinds[2], "external SDK file dialog folder request kind");
                    }
                    finally
                    {
                        registration?.Dispose();
                        Directory.Delete(tempDirectory, recursive: true);
                    }
                }

                private static string ReadPortableRequestString(object request, string propertyName)
                {
                    return request.GetType()
                        .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(request)
                        ?.ToString()
                        ?? string.Empty;
                }

                private static void ValidateClipboard()
                {
                    Clipboard.Clear();
                    AssertEqual(false, Clipboard.ContainsText(), "external SDK Clipboard initial text state");

                    Clipboard.SetText("external SDK clipboard text");
                    AssertEqual(true, Clipboard.ContainsText(), "external SDK Clipboard text state after SetText");
                    AssertEqual("external SDK clipboard text", Clipboard.GetText(), "external SDK Clipboard GetText");

                    var dataObject = Clipboard.GetDataObject()
                        ?? throw new InvalidOperationException("Expected external SDK Clipboard data object.");
                    AssertEqual(
                        "external SDK clipboard text",
                        dataObject.GetData(DataFormats.UnicodeText, autoConvert: false),
                        "external SDK Clipboard data object unicode text");
                    AssertEqual(true, Clipboard.IsCurrent(dataObject), "external SDK Clipboard current data object");

                    Clipboard.Flush();
                    AssertEqual("external SDK clipboard text", Clipboard.GetText(), "external SDK Clipboard flushed text");

                    Clipboard.Clear();
                    AssertEqual(false, Clipboard.ContainsText(), "external SDK Clipboard cleared text state");
                    AssertEqual(string.Empty, Clipboard.GetText(), "external SDK Clipboard cleared text");
                }

                private static void ValidateFreezableResources()
                {
                    var appResources = Application.Current?.Resources
                        ?? throw new InvalidOperationException("External SDK validation requires Application resources.");
                    var brush = RequireType<SolidColorBrush>(
                        appResources["ExternalFreezableBrush"],
                        "external SDK Freezable brush resource");
                    AssertEqual("#FF5B8C7A", brush.Color.ToString(), "external SDK Freezable brush color");
                    AssertEqual(0.75, brush.Opacity, "external SDK Freezable brush opacity");
                    AssertEqual(true, brush.CanFreeze, "external SDK Freezable brush can freeze");
                    if (!brush.IsFrozen)
                    {
                        brush.Freeze();
                    }

                    AssertEqual(true, brush.IsFrozen, "external SDK Freezable brush frozen state");
                    var brushClone = brush.Clone();
                    AssertEqual(false, brushClone.IsFrozen, "external SDK Freezable brush clone mutable state");
                    brushClone.Opacity = 0.33;
                    AssertEqual(0.33, brushClone.Opacity, "external SDK Freezable brush clone mutable opacity");
                    var brushCurrentValueClone = brush.CloneCurrentValue();
                    AssertEqual(false, brushCurrentValueClone.IsFrozen, "external SDK Freezable brush current-value clone mutable state");
                    AssertEqual("#FF5B8C7A", brushCurrentValueClone.Color.ToString(), "external SDK Freezable brush current-value clone color");
                    AssertEqual(0.75, brushCurrentValueClone.Opacity, "external SDK Freezable brush current-value clone opacity");

                    var gradient = RequireType<LinearGradientBrush>(
                        appResources["ExternalFreezableGradientBrush"],
                        "external SDK Freezable gradient resource");
                    AssertEqual(0.8, gradient.Opacity, "external SDK Freezable gradient opacity");
                    AssertEqual(3, gradient.GradientStops.Count, "external SDK Freezable gradient stop count");
                    AssertEqual("#FF2F6B54", gradient.GradientStops[0].Color.ToString(), "external SDK Freezable gradient first stop color");
                    AssertEqual(0.5, gradient.GradientStops[1].Offset, "external SDK Freezable gradient second stop offset");
                    AssertEqual(true, gradient.CanFreeze, "external SDK Freezable gradient can freeze");
                    if (!gradient.IsFrozen)
                    {
                        gradient.Freeze();
                    }

                    AssertEqual(true, gradient.IsFrozen, "external SDK Freezable gradient frozen state");
                    AssertEqual(true, gradient.GradientStops.IsFrozen, "external SDK Freezable gradient stop collection frozen state");
                    AssertEqual(true, gradient.GradientStops[1].IsFrozen, "external SDK Freezable gradient stop frozen state");
                    var gradientClone = gradient.Clone();
                    AssertEqual(false, gradientClone.IsFrozen, "external SDK Freezable gradient clone mutable state");
                    AssertEqual(false, gradientClone.GradientStops.IsFrozen, "external SDK Freezable gradient clone stop collection mutable state");
                    gradientClone.GradientStops[1].Offset = 0.65;
                    AssertEqual(0.65, gradientClone.GradientStops[1].Offset, "external SDK Freezable gradient clone mutable stop offset");
                    AssertEqual(0.5, gradient.GradientStops[1].Offset, "external SDK Freezable gradient original stop offset");
                    var gradientCurrentValueClone = gradient.CloneCurrentValue();
                    AssertEqual(3, gradientCurrentValueClone.GradientStops.Count, "external SDK Freezable gradient current-value clone stop count");
                    AssertEqual(false, gradientCurrentValueClone.GradientStops.IsFrozen, "external SDK Freezable gradient current-value clone stop collection");
                    AssertEqual("#FF4B5E9D", gradientCurrentValueClone.GradientStops[2].Color.ToString(), "external SDK Freezable gradient current-value clone third stop color");
                }

                private static void ValidateManagedImagingObjects()
                {
                    byte[] pixels =
                    [
                        0x10, 0x20, 0x30, 0xFF,
                        0x40, 0x50, 0x60, 0xFF,
                        0x70, 0x80, 0x90, 0xFF,
                        0xA0, 0xB0, 0xC0, 0xFF
                    ];

                    var bitmapSource = BitmapSource.Create(
                        2,
                        2,
                        96.0,
                        96.0,
                        PixelFormats.Bgra32,
                        null,
                        pixels,
                        8);
                    AssertEqual(2, bitmapSource.PixelWidth, "external SDK BitmapSource pixel width");
                    AssertEqual(2, bitmapSource.PixelHeight, "external SDK BitmapSource pixel height");
                    AssertClose(96.0, bitmapSource.DpiX, "external SDK BitmapSource DpiX");
                    AssertClose(96.0, bitmapSource.DpiY, "external SDK BitmapSource DpiY");
                    AssertEqual(PixelFormats.Bgra32, bitmapSource.Format, "external SDK BitmapSource Bgra32 format");

                    var copiedPixels = new byte[pixels.Length];
                    bitmapSource.CopyPixels(copiedPixels, 8, 0);
                    AssertEqual(pixels[0], copiedPixels[0], "external SDK BitmapSource copied blue byte");
                    AssertEqual(pixels[5], copiedPixels[5], "external SDK BitmapSource copied second green byte");
                    AssertEqual(pixels[14], copiedPixels[14], "external SDK BitmapSource copied final red byte");

                    var bitmapFrame = BitmapFrame.Create(bitmapSource);
                    AssertEqual(2, bitmapFrame.PixelWidth, "external SDK BitmapFrame pixel width");
                    AssertEqual(2, bitmapFrame.PixelHeight, "external SDK BitmapFrame pixel height");
                    AssertEqual(PixelFormats.Bgra32, bitmapFrame.Format, "external SDK BitmapFrame Bgra32 format");
                    var framePixels = new byte[pixels.Length];
                    bitmapFrame.CopyPixels(framePixels, 8, 0);
                    AssertEqual(pixels[10], framePixels[10], "external SDK BitmapFrame copied red byte");

                    var bmpEncoder = new BmpBitmapEncoder();
                    bmpEncoder.Frames.Add(bitmapFrame);
                    using var bmpStream = new MemoryStream();
                    bmpEncoder.Save(bmpStream);
                    byte[] bmpBytes = bmpStream.ToArray();
                    AssertEqual((byte)'B', bmpBytes[0], "external SDK BmpBitmapEncoder signature byte 0");
                    AssertEqual((byte)'M', bmpBytes[1], "external SDK BmpBitmapEncoder signature byte 1");
                    AssertEqual(2, BitConverter.ToInt32(bmpBytes, 18), "external SDK BmpBitmapEncoder pixel width");
                    AssertEqual(2, BitConverter.ToInt32(bmpBytes, 22), "external SDK BmpBitmapEncoder pixel height");
                    AssertEqual(32, BitConverter.ToUInt16(bmpBytes, 28), "external SDK BmpBitmapEncoder bits per pixel");
                    int bmpPixelOffset = BitConverter.ToInt32(bmpBytes, 10);
                    AssertEqual(54, bmpPixelOffset, "external SDK BmpBitmapEncoder pixel offset");
                    AssertEqual(pixels[8], bmpBytes[bmpPixelOffset], "external SDK BmpBitmapEncoder bottom-left blue byte");
                    AssertEqual(pixels[13], bmpBytes[bmpPixelOffset + 5], "external SDK BmpBitmapEncoder bottom-right green byte");
                    AssertEqual(pixels[2], bmpBytes[bmpPixelOffset + 10], "external SDK BmpBitmapEncoder top-left red byte");

                    var bmpDecoder = BitmapDecoder.Create(
                        new MemoryStream(bmpBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(BmpBitmapDecoder), bmpDecoder.GetType(), "external SDK BitmapDecoder.Create BMP decoder type");
                    AssertEqual(1, bmpDecoder.Frames.Count, "external SDK BitmapDecoder.Create BMP frame count");
                    AssertEqual(2, bmpDecoder.Frames[0].PixelWidth, "external SDK BitmapDecoder.Create BMP pixel width");
                    AssertEqual(2, bmpDecoder.Frames[0].PixelHeight, "external SDK BitmapDecoder.Create BMP pixel height");
                    AssertEqual(PixelFormats.Bgra32, bmpDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create BMP Bgra32 format");
                    var decodedBmpPixels = new byte[pixels.Length];
                    bmpDecoder.Frames[0].CopyPixels(decodedBmpPixels, 8, 0);
                    AssertEqual(pixels[0], decodedBmpPixels[0], "external SDK BitmapDecoder.Create BMP top-left blue byte");
                    AssertEqual(pixels[14], decodedBmpPixels[14], "external SDK BitmapDecoder.Create BMP bottom-right red byte");

                    var directBmpDecoder = new BmpBitmapDecoder(
                        new MemoryStream(bmpBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(1, directBmpDecoder.Frames.Count, "external SDK BmpBitmapDecoder frame count");
                    AssertEqual(PixelFormats.Bgra32, directBmpDecoder.Frames[0].Format, "external SDK BmpBitmapDecoder Bgra32 format");

                    string bmpPath = Path.Combine(Path.GetTempPath(), "external-sdk-managed-image-" + Guid.NewGuid().ToString("N") + ".bmp");
                    File.WriteAllBytes(bmpPath, bmpBytes);
                    try
                    {
                        var bmpUri = new Uri(bmpPath);
                        var uriBmpDecoder = BitmapDecoder.Create(
                            bmpUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(typeof(BmpBitmapDecoder), uriBmpDecoder.GetType(), "external SDK BitmapDecoder.Create URI BMP decoder type");
                        AssertEqual(1, uriBmpDecoder.Frames.Count, "external SDK BitmapDecoder.Create URI BMP frame count");
                        AssertEqual(PixelFormats.Bgra32, uriBmpDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create URI BMP Bgra32 format");

                        var directUriBmpDecoder = new BmpBitmapDecoder(
                            bmpUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(1, directUriBmpDecoder.Frames.Count, "external SDK BmpBitmapDecoder URI frame count");
                        AssertEqual(2, directUriBmpDecoder.Frames[0].PixelWidth, "external SDK BmpBitmapDecoder URI pixel width");

                        var bitmapImage = new BitmapImage(bmpUri);
                        AssertEqual(2, bitmapImage.PixelWidth, "external SDK BitmapImage URI BMP pixel width");
                        AssertEqual(2, bitmapImage.PixelHeight, "external SDK BitmapImage URI BMP pixel height");
                        AssertEqual(PixelFormats.Bgra32, bitmapImage.Format, "external SDK BitmapImage URI BMP Bgra32 format");
                        var bitmapImagePixels = new byte[pixels.Length];
                        bitmapImage.CopyPixels(bitmapImagePixels, 8, 0);
                        AssertEqual(pixels[0], bitmapImagePixels[0], "external SDK BitmapImage URI BMP top-left blue byte");
                        AssertEqual(pixels[14], bitmapImagePixels[14], "external SDK BitmapImage URI BMP bottom-right red byte");
                    }
                    finally
                    {
                        File.Delete(bmpPath);
                    }

                    var indexedPalette = new BitmapPalette(
                    [
                        Color.FromRgb(0x00, 0x00, 0x00),
                        Color.FromRgb(0xCC, 0x22, 0x22),
                        Color.FromRgb(0x22, 0xAA, 0x44),
                        Color.FromRgb(0x22, 0x44, 0xCC)
                    ]);
                    byte[] indexedPixels = [0, 1, 2, 3];
                    var indexedSource = BitmapSource.Create(
                        2,
                        2,
                        96.0,
                        96.0,
                        PixelFormats.Indexed8,
                        indexedPalette,
                        indexedPixels,
                        2);
                    var indexedEncoder = new BmpBitmapEncoder();
                    indexedEncoder.Frames.Add(BitmapFrame.Create(indexedSource));
                    using var indexedBmpStream = new MemoryStream();
                    indexedEncoder.Save(indexedBmpStream);
                    byte[] indexedBmpBytes = indexedBmpStream.ToArray();
                    AssertEqual(8, BitConverter.ToUInt16(indexedBmpBytes, 28), "external SDK Indexed8 BMP bits per pixel");
                    AssertEqual(4, BitConverter.ToInt32(indexedBmpBytes, 46), "external SDK Indexed8 BMP color table size");
                    var indexedDecoder = BitmapDecoder.Create(
                        new MemoryStream(indexedBmpBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(PixelFormats.Indexed8, indexedDecoder.Frames[0].Format, "external SDK Indexed8 BitmapDecoder format");
                    AssertEqual(4, indexedDecoder.Frames[0].Palette.Colors.Count, "external SDK Indexed8 BitmapDecoder palette count");
                    AssertEqual("#FF22AA44", indexedDecoder.Frames[0].Palette.Colors[2].ToString(), "external SDK Indexed8 BitmapDecoder palette green");
                    var decodedIndexedPixels = new byte[indexedPixels.Length];
                    indexedDecoder.Frames[0].CopyPixels(decodedIndexedPixels, 2, 0);
                    AssertEqual(indexedPixels[0], decodedIndexedPixels[0], "external SDK Indexed8 BitmapDecoder top-left index");
                    AssertEqual(indexedPixels[3], decodedIndexedPixels[3], "external SDK Indexed8 BitmapDecoder bottom-right index");

                    string indexedBmpPath = Path.Combine(Path.GetTempPath(), "external-sdk-indexed-image-" + Guid.NewGuid().ToString("N") + ".bmp");
                    File.WriteAllBytes(indexedBmpPath, indexedBmpBytes);
                    try
                    {
                        var indexedBmpUri = new Uri(indexedBmpPath);
                        var indexedBitmapImage = new BitmapImage(indexedBmpUri);
                        AssertEqual(PixelFormats.Indexed8, indexedBitmapImage.Format, "external SDK Indexed8 BitmapImage URI format");
                        AssertEqual(4, indexedBitmapImage.Palette.Colors.Count, "external SDK Indexed8 BitmapImage URI palette count");
                        var indexedImagePixels = new byte[indexedPixels.Length];
                        indexedBitmapImage.CopyPixels(indexedImagePixels, 2, 0);
                        AssertEqual(indexedPixels[1], indexedImagePixels[1], "external SDK Indexed8 BitmapImage URI top-right index");
                        AssertEqual(indexedPixels[2], indexedImagePixels[2], "external SDK Indexed8 BitmapImage URI bottom-left index");
                    }
                    finally
                    {
                        File.Delete(indexedBmpPath);
                    }

                    var writeableBitmap = new WriteableBitmap(2, 2, 96.0, 96.0, PixelFormats.Bgra32, null);
                    writeableBitmap.WritePixels(new Int32Rect(0, 0, 2, 2), pixels, 8, 0);
                    var writeablePixels = new byte[pixels.Length];
                    writeableBitmap.CopyPixels(writeablePixels, 8, 0);
                    AssertEqual(pixels[8], writeablePixels[8], "external SDK WriteableBitmap copied second-row blue byte");
                    AssertEqual(pixels[15], writeablePixels[15], "external SDK WriteableBitmap copied final alpha byte");

                    var image = new Image
                    {
                        Source = writeableBitmap,
                        Width = 2,
                        Height = 2,
                        Stretch = Stretch.None
                    };
                    AssertEqual(writeableBitmap, image.Source, "external SDK Image source WriteableBitmap");
                    AssertEqual(Stretch.None, image.Stretch, "external SDK Image stretch");

                    var frameImage = new Image
                    {
                        Source = bitmapFrame,
                        Width = 2,
                        Height = 2,
                        Stretch = Stretch.None
                    };
                    AssertEqual(bitmapFrame, frameImage.Source, "external SDK Image source BitmapFrame");

                    var imageBrush = new ImageBrush(bitmapSource)
                    {
                        Stretch = Stretch.None,
                        TileMode = TileMode.Tile,
                        ViewportUnits = BrushMappingMode.Absolute,
                        Viewport = new Rect(0, 0, 2, 2)
                    };
                    AssertEqual(bitmapSource, imageBrush.ImageSource, "external SDK ImageBrush source BitmapSource");
                    AssertEqual(TileMode.Tile, imageBrush.TileMode, "external SDK ImageBrush tile mode");
                    AssertEqual(BrushMappingMode.Absolute, imageBrush.ViewportUnits, "external SDK ImageBrush viewport units");
                    AssertEqual(new Rect(0, 0, 2, 2), imageBrush.Viewport, "external SDK ImageBrush viewport");
                }

                private static void ValidateLooseXamlReaderWriter()
                {
                    string looseXaml =
                        "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                        "x:Name=\"ExternalLooseRoot\">" +
                        "<StackPanel.Resources>" +
                        "<SolidColorBrush x:Key=\"ExternalLooseAccentBrush\" Color=\"#4F7CAC\" />" +
                        "<Style x:Key=\"ExternalLooseTextStyle\" TargetType=\"{x:Type TextBlock}\">" +
                        "<Setter Property=\"Tag\" Value=\"external loose style tag\" />" +
                        "<Setter Property=\"Foreground\" Value=\"{StaticResource ExternalLooseAccentBrush}\" />" +
                        "</Style>" +
                        "</StackPanel.Resources>" +
                        "<TextBlock x:Name=\"ExternalLooseText\" Style=\"{StaticResource ExternalLooseTextStyle}\" Text=\"External loose xaml text\" />" +
                        "<TextBox x:Name=\"ExternalLooseTextBox\" Tag=\"External loose binding text\" Text=\"{Binding Tag, RelativeSource={RelativeSource Self}}\" />" +
                        "</StackPanel>";
                    var root = RequireType<StackPanel>(
                        XamlReader.Parse(looseXaml),
                        "external SDK loose XamlReader root");
                    AssertEqual("ExternalLooseRoot", root.Name, "external SDK loose XamlReader root name");
                    AssertEqual(2, root.Children.Count, "external SDK loose XamlReader child count");
                    var accentBrush = RequireType<SolidColorBrush>(
                        root.Resources["ExternalLooseAccentBrush"],
                        "external SDK loose XamlReader brush resource");
                    AssertEqual("#FF4F7CAC", accentBrush.Color.ToString(), "external SDK loose XamlReader brush color");
                    var textStyle = RequireType<Style>(
                        root.Resources["ExternalLooseTextStyle"],
                        "external SDK loose XamlReader style resource");
                    AssertEqual(typeof(TextBlock), textStyle.TargetType, "external SDK loose XamlReader style target");

                    var textBlock = RequireType<TextBlock>(
                        root.FindName("ExternalLooseText"),
                        "external SDK loose XamlReader named TextBlock");
                    AssertEqual(true, ReferenceEquals(root.Children[0], textBlock), "external SDK loose XamlReader TextBlock child");
                    AssertEqual(textStyle, textBlock.Style, "external SDK loose XamlReader StaticResource style");
                    AssertEqual("External loose xaml text", textBlock.Text, "external SDK loose XamlReader text");
                    AssertEqual("external loose style tag", textBlock.Tag, "external SDK loose XamlReader style setter tag");
                    AssertEqual(true, ReferenceEquals(accentBrush, textBlock.Foreground), "external SDK loose XamlReader style StaticResource brush");

                    var textBox = RequireType<TextBox>(
                        root.FindName("ExternalLooseTextBox"),
                        "external SDK loose XamlReader named TextBox");
                    AssertEqual(true, ReferenceEquals(root.Children[1], textBox), "external SDK loose XamlReader TextBox child");
                    AssertEqual("External loose binding text", textBox.Tag, "external SDK loose XamlReader TextBox tag");
                    DrainDispatcher();
                    AssertEqual("External loose binding text", textBox.Text, "external SDK loose XamlReader RelativeSource binding text");
                    var textBoxBinding = textBox.GetBindingExpression(TextBox.TextProperty)
                        ?? throw new InvalidOperationException("Expected external SDK loose XamlReader TextBox BindingExpression.");
                    AssertEqual("Tag", textBoxBinding.ParentBinding.Path.Path, "external SDK loose XamlReader Binding path");

                    string writableXaml =
                        "<LinearGradientBrush xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                        "StartPoint=\"0,0\" EndPoint=\"1,1\" Opacity=\"0.625\" SpreadMethod=\"Reflect\">" +
                        "<GradientStop Color=\"#4F7CAC\" Offset=\"0\" />" +
                        "<GradientStop Color=\"#B15E3B\" Offset=\"1\" />" +
                        "</LinearGradientBrush>";
                    var brush = RequireType<LinearGradientBrush>(
                        XamlReader.Parse(writableXaml),
                        "external SDK loose XamlWriter source brush");
                    string serialized = XamlWriter.Save(brush);
                    AssertContains("LinearGradientBrush", serialized, "external SDK loose XamlWriter serialized brush");
                    AssertContains("GradientStop", serialized, "external SDK loose XamlWriter serialized GradientStop");
                    var roundTrippedBrush = RequireType<LinearGradientBrush>(
                        XamlReader.Parse(serialized),
                        "external SDK loose XamlWriter round-trip brush");
                    AssertEqual(0.625, roundTrippedBrush.Opacity, "external SDK loose XamlWriter round-trip brush opacity");
                    AssertEqual(GradientSpreadMethod.Reflect, roundTrippedBrush.SpreadMethod, "external SDK loose XamlWriter round-trip spread method");
                    AssertEqual(2, roundTrippedBrush.GradientStops.Count, "external SDK loose XamlWriter round-trip GradientStop count");
                    AssertEqual("#FF4F7CAC", roundTrippedBrush.GradientStops[0].Color.ToString(), "external SDK loose XamlWriter round-trip first stop color");
                    AssertEqual(0.0, roundTrippedBrush.GradientStops[0].Offset, "external SDK loose XamlWriter round-trip first stop offset");
                    AssertEqual("#FFB15E3B", roundTrippedBrush.GradientStops[1].Color.ToString(), "external SDK loose XamlWriter round-trip second stop color");
                    AssertEqual(1.0, roundTrippedBrush.GradientStops[1].Offset, "external SDK loose XamlWriter round-trip second stop offset");

                    var systemResourceKey = MenuItem.SeparatorStyleKey;
                    var systemResourceStyle = new Style(typeof(MenuItem));
                    var systemResourceDictionary = new ResourceDictionary
                    {
                        { systemResourceKey, systemResourceStyle }
                    };
                    string systemResourceSerialized = XamlWriter.Save(systemResourceDictionary);
                    AssertContains("ResourceDictionary", systemResourceSerialized, "external SDK loose XamlWriter serialized system ResourceDictionary");
                    AssertContains("x:Key", systemResourceSerialized, "external SDK loose XamlWriter serialized system resource key directive");
                    AssertContains("MenuItem", systemResourceSerialized, "external SDK loose XamlWriter serialized system resource key owner");
                    AssertContains("SeparatorStyleKey", systemResourceSerialized, "external SDK loose XamlWriter serialized system resource key member");
                    var roundTrippedSystemResources = RequireType<ResourceDictionary>(
                        XamlReader.Parse(systemResourceSerialized),
                        "external SDK loose XamlWriter round-trip system ResourceDictionary");
                    var roundTrippedSystemStyle = RequireType<Style>(
                        roundTrippedSystemResources[systemResourceKey],
                        "external SDK loose XamlWriter round-trip system-key style");
                    AssertEqual(typeof(MenuItem), roundTrippedSystemStyle.TargetType, "external SDK loose XamlWriter round-trip system-key style target");

                    string styleDictionaryXaml =
                        "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                        "<Style x:Key=\"ExternalWriterBaseButtonStyle\" TargetType=\"{x:Type Button}\">" +
                        "<Setter Property=\"Tag\" Value=\"external writer base tag\" />" +
                        "</Style>" +
                        "<Style x:Key=\"ExternalWriterButtonStyle\" TargetType=\"{x:Type Button}\" BasedOn=\"{StaticResource ExternalWriterBaseButtonStyle}\">" +
                        "<Setter Property=\"Content\" Value=\"external writer style content\" />" +
                        "<Setter Property=\"MinWidth\" Value=\"144\" />" +
                        "</Style>" +
                        "</ResourceDictionary>";
                    var styleDictionary = RequireType<ResourceDictionary>(
                        XamlReader.Parse(styleDictionaryXaml),
                        "external SDK loose XamlWriter style dictionary source");
                    string styleSerialized = XamlWriter.Save(styleDictionary);
                    AssertContains("ExternalWriterBaseButtonStyle", styleSerialized, "external SDK loose XamlWriter serialized base style key");
                    AssertContains("ExternalWriterButtonStyle", styleSerialized, "external SDK loose XamlWriter serialized derived style key");
                    AssertContains("BasedOn", styleSerialized, "external SDK loose XamlWriter serialized style BasedOn");
                    AssertContains("Setter", styleSerialized, "external SDK loose XamlWriter serialized style setters");
                    var roundTrippedStyles = RequireType<ResourceDictionary>(
                        XamlReader.Parse(styleSerialized),
                        "external SDK loose XamlWriter round-trip style dictionary");
                    var baseStyle = RequireType<Style>(
                        roundTrippedStyles["ExternalWriterBaseButtonStyle"],
                        "external SDK loose XamlWriter round-trip base style");
                    var derivedStyle = RequireType<Style>(
                        roundTrippedStyles["ExternalWriterButtonStyle"],
                        "external SDK loose XamlWriter round-trip derived style");
                    AssertEqual(typeof(Button), baseStyle.TargetType, "external SDK loose XamlWriter round-trip base style target");
                    AssertEqual(typeof(Button), derivedStyle.TargetType, "external SDK loose XamlWriter round-trip derived style target");
                    var basedOnStyle = RequireType<Style>(
                        derivedStyle.BasedOn,
                        "external SDK loose XamlWriter round-trip style BasedOn");
                    AssertEqual(typeof(Button), basedOnStyle.TargetType, "external SDK loose XamlWriter round-trip style BasedOn target");
                    AssertEqual(1, basedOnStyle.Setters.Count, "external SDK loose XamlWriter round-trip style BasedOn setter count");
                    AssertLooseStyleSetter(basedOnStyle.Setters[0], FrameworkElement.TagProperty, "external writer base tag", "external SDK loose XamlWriter round-trip style BasedOn setter");
                    AssertEqual(1, baseStyle.Setters.Count, "external SDK loose XamlWriter round-trip base style setter count");
                    AssertLooseStyleSetter(baseStyle.Setters[0], FrameworkElement.TagProperty, "external writer base tag", "external SDK loose XamlWriter base Tag setter");
                    AssertEqual(2, derivedStyle.Setters.Count, "external SDK loose XamlWriter round-trip derived style setter count");
                    AssertLooseStyleSetter(derivedStyle.Setters[0], ContentControl.ContentProperty, "external writer style content", "external SDK loose XamlWriter derived Content setter");
                    AssertLooseStyleSetter(derivedStyle.Setters[1], FrameworkElement.MinWidthProperty, 144.0, "external SDK loose XamlWriter derived MinWidth setter");
                    var styledButton = new Button { Style = derivedStyle };
                    AssertEqual("external writer base tag", styledButton.Tag, "external SDK loose XamlWriter styled Button inherited Tag");
                    AssertEqual("external writer style content", styledButton.Content, "external SDK loose XamlWriter styled Button content");
                    AssertEqual(144.0, styledButton.MinWidth, "external SDK loose XamlWriter styled Button MinWidth");

                    string templateDictionaryXaml =
                        "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                        "<ControlTemplate x:Key=\"ExternalWriterButtonTemplate\" TargetType=\"{x:Type Button}\">" +
                        "<Border x:Name=\"ExternalTemplateBorder\" Padding=\"{TemplateBinding Padding}\" Background=\"{TemplateBinding Background}\">" +
                        "<ContentPresenter x:Name=\"ExternalTemplateContent\" RecognizesAccessKey=\"True\" />" +
                        "</Border>" +
                        "<ControlTemplate.Triggers>" +
                        "<Trigger Property=\"IsDefault\" Value=\"True\">" +
                        "<Setter TargetName=\"ExternalTemplateBorder\" Property=\"Tag\" Value=\"external default template state\" />" +
                        "</Trigger>" +
                        "</ControlTemplate.Triggers>" +
                        "</ControlTemplate>" +
                        "</ResourceDictionary>";
                    var templateDictionary = RequireType<ResourceDictionary>(
                        XamlReader.Parse(templateDictionaryXaml),
                        "external SDK loose XamlWriter template dictionary source");
                    string templateSerialized = XamlWriter.Save(templateDictionary);
                    AssertContains("ControlTemplate", templateSerialized, "external SDK loose XamlWriter serialized ControlTemplate");
                    AssertContains("ContentPresenter", templateSerialized, "external SDK loose XamlWriter serialized ControlTemplate ContentPresenter");
                    AssertContains("ControlTemplate.Triggers", templateSerialized, "external SDK loose XamlWriter serialized ControlTemplate triggers");
                    AssertContains("ExternalTemplateBorder", templateSerialized, "external SDK loose XamlWriter serialized ControlTemplate target name");
                    var roundTrippedTemplates = RequireType<ResourceDictionary>(
                        XamlReader.Parse(templateSerialized),
                        "external SDK loose XamlWriter round-trip template dictionary");
                    var template = RequireType<ControlTemplate>(
                        roundTrippedTemplates["ExternalWriterButtonTemplate"],
                        "external SDK loose XamlWriter round-trip ControlTemplate");
                    AssertEqual(typeof(Button), template.TargetType, "external SDK loose XamlWriter round-trip ControlTemplate target");
                    AssertEqual(1, template.Triggers.Count, "external SDK loose XamlWriter round-trip ControlTemplate trigger count");
                    var trigger = RequireType<Trigger>(
                        template.Triggers[0],
                        "external SDK loose XamlWriter round-trip ControlTemplate trigger");
                    AssertEqual(Button.IsDefaultProperty, trigger.Property, "external SDK loose XamlWriter round-trip ControlTemplate trigger property");
                    AssertEqual(true, trigger.Value, "external SDK loose XamlWriter round-trip ControlTemplate trigger value");
                    AssertEqual(1, trigger.Setters.Count, "external SDK loose XamlWriter round-trip ControlTemplate trigger setter count");
                    var triggerSetter = AssertLooseStyleSetter(
                        trigger.Setters[0],
                        FrameworkElement.TagProperty,
                        "external default template state",
                        "external SDK loose XamlWriter ControlTemplate trigger Tag setter");
                    AssertEqual("ExternalTemplateBorder", triggerSetter.TargetName, "external SDK loose XamlWriter ControlTemplate trigger setter target");
                    var templatedButton = new Button
                    {
                        Template = template,
                        Content = "external templated writer button"
                    };
                    templatedButton.ApplyTemplate();
                    AssertEqual(
                        true,
                        template.FindName("ExternalTemplateBorder", templatedButton) is Border,
                        "external SDK loose XamlWriter applied ControlTemplate border");
                    AssertEqual(
                        true,
                        template.FindName("ExternalTemplateContent", templatedButton) is ContentPresenter,
                        "external SDK loose XamlWriter applied ControlTemplate content presenter");

                    string dataTemplateDictionaryXaml =
                        "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                        "<DataTemplate x:Key=\"ExternalWriterDataTemplate\">" +
                        "<StackPanel x:Name=\"ExternalTemplateRoot\" Tag=\"external writer data template root\">" +
                        "<TextBlock x:Name=\"ExternalTemplateNameText\" Text=\"{Binding Name}\" />" +
                        "<TextBlock x:Name=\"ExternalTemplateKindText\" Text=\"{Binding Kind}\" />" +
                        "</StackPanel>" +
                        "<DataTemplate.Triggers>" +
                        "<DataTrigger Binding=\"{Binding IsActive}\" Value=\"True\">" +
                        "<Setter TargetName=\"ExternalTemplateNameText\" Property=\"Tag\" Value=\"external active template item\" />" +
                        "</DataTrigger>" +
                        "</DataTemplate.Triggers>" +
                        "</DataTemplate>" +
                        "</ResourceDictionary>";
                    var dataTemplateDictionary = RequireType<ResourceDictionary>(
                        XamlReader.Parse(dataTemplateDictionaryXaml),
                        "external SDK loose XamlWriter DataTemplate dictionary source");
                    var parsedDataTemplate = RequireType<DataTemplate>(
                        dataTemplateDictionary["ExternalWriterDataTemplate"],
                        "external SDK loose XamlReader DataTemplate");
                    var parsedDataTemplateRoot = RequireType<StackPanel>(
                        parsedDataTemplate.LoadContent(),
                        "external SDK loose XamlReader DataTemplate root");
                    AssertEqual(2, parsedDataTemplateRoot.Children.Count, "external SDK loose XamlReader DataTemplate child count");
                    var parsedNameText = RequireType<TextBlock>(
                        parsedDataTemplateRoot.Children[0],
                        "external SDK loose XamlReader DataTemplate name text");
                    var parsedKindText = RequireType<TextBlock>(
                        parsedDataTemplateRoot.Children[1],
                        "external SDK loose XamlReader DataTemplate kind text");
                    AssertEqual("Name", parsedNameText.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path, "external SDK loose XamlReader DataTemplate name binding path");
                    AssertEqual("Kind", parsedKindText.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path, "external SDK loose XamlReader DataTemplate kind binding path");

                    string dataTemplateSerialized = XamlWriter.Save(dataTemplateDictionary);
                    AssertContains("DataTemplate", dataTemplateSerialized, "external SDK loose XamlWriter serialized DataTemplate");
                    AssertContains("ExternalWriterDataTemplate", dataTemplateSerialized, "external SDK loose XamlWriter serialized DataTemplate key");
                    AssertContains("TextBlock", dataTemplateSerialized, "external SDK loose XamlWriter serialized DataTemplate TextBlock");
                    AssertContains("DataTemplate.Triggers", dataTemplateSerialized, "external SDK loose XamlWriter serialized DataTemplate triggers");
                    var roundTrippedDataTemplates = RequireType<ResourceDictionary>(
                        XamlReader.Parse(dataTemplateSerialized),
                        "external SDK loose XamlWriter round-trip DataTemplate dictionary");
                    var dataTemplate = RequireType<DataTemplate>(
                        roundTrippedDataTemplates["ExternalWriterDataTemplate"],
                        "external SDK loose XamlWriter round-trip DataTemplate");
                    AssertEqual(1, dataTemplate.Triggers.Count, "external SDK loose XamlWriter round-trip DataTemplate trigger count");
                    var dataTrigger = RequireType<DataTrigger>(
                        dataTemplate.Triggers[0],
                        "external SDK loose XamlWriter round-trip DataTemplate trigger");
                    var dataTriggerBinding = RequireType<Binding>(
                        dataTrigger.Binding,
                        "external SDK loose XamlWriter round-trip DataTemplate trigger binding");
                    AssertEqual("IsActive", dataTriggerBinding.Path.Path, "external SDK loose XamlWriter round-trip DataTemplate trigger binding path");
                    AssertEqual("True", dataTrigger.Value?.ToString(), "external SDK loose XamlWriter round-trip DataTemplate trigger value");
                    AssertEqual(1, dataTrigger.Setters.Count, "external SDK loose XamlWriter round-trip DataTemplate trigger setter count");
                    var dataTriggerSetter = AssertLooseStyleSetter(
                        dataTrigger.Setters[0],
                        FrameworkElement.TagProperty,
                        "external active template item",
                        "external SDK loose XamlWriter DataTemplate trigger Tag setter");
                    AssertEqual("ExternalTemplateNameText", dataTriggerSetter.TargetName, "external SDK loose XamlWriter DataTemplate trigger setter target");

                    var dataTemplateRoot = RequireType<StackPanel>(
                        dataTemplate.LoadContent(),
                        "external SDK loose XamlWriter round-trip DataTemplate root");
                    AssertEqual("ExternalTemplateRoot", dataTemplateRoot.Name, "external SDK loose XamlWriter round-trip DataTemplate root name");
                    AssertEqual("external writer data template root", dataTemplateRoot.Tag, "external SDK loose XamlWriter round-trip DataTemplate root tag");
                    AssertEqual(2, dataTemplateRoot.Children.Count, "external SDK loose XamlWriter round-trip DataTemplate child count");
                    AssertEqual(
                        "ExternalTemplateNameText",
                        RequireType<TextBlock>(
                            dataTemplateRoot.Children[0],
                            "external SDK loose XamlWriter round-trip DataTemplate name text").Name,
                        "external SDK loose XamlWriter round-trip DataTemplate name TextBlock name");
                    AssertEqual(
                        "ExternalTemplateKindText",
                        RequireType<TextBlock>(
                            dataTemplateRoot.Children[1],
                            "external SDK loose XamlWriter round-trip DataTemplate kind text").Name,
                        "external SDK loose XamlWriter round-trip DataTemplate kind TextBlock name");

                    string hierarchicalTemplateDictionaryXaml =
                        "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                        "<HierarchicalDataTemplate x:Key=\"ExternalWriterNodeTemplate\" ItemsSource=\"{Binding Children}\">" +
                        "<StackPanel x:Name=\"ExternalNodeTemplateRoot\" Tag=\"external writer hierarchical template root\">" +
                        "<TextBlock x:Name=\"ExternalNodeNameText\" Text=\"{Binding Name}\" />" +
                        "<TextBlock x:Name=\"ExternalNodeKindText\" Text=\"{Binding Kind}\" />" +
                        "</StackPanel>" +
                        "<HierarchicalDataTemplate.Triggers>" +
                        "<DataTrigger Binding=\"{Binding IsActive}\" Value=\"True\">" +
                        "<Setter TargetName=\"ExternalNodeNameText\" Property=\"Tag\" Value=\"external active writer node\" />" +
                        "</DataTrigger>" +
                        "</HierarchicalDataTemplate.Triggers>" +
                        "</HierarchicalDataTemplate>" +
                        "</ResourceDictionary>";
                    var hierarchicalTemplateDictionary = RequireType<ResourceDictionary>(
                        XamlReader.Parse(hierarchicalTemplateDictionaryXaml),
                        "external SDK loose XamlWriter HierarchicalDataTemplate dictionary source");
                    var parsedHierarchicalTemplate = RequireType<HierarchicalDataTemplate>(
                        hierarchicalTemplateDictionary["ExternalWriterNodeTemplate"],
                        "external SDK loose XamlReader HierarchicalDataTemplate");
                    var parsedItemsSource = RequireType<Binding>(
                        parsedHierarchicalTemplate.ItemsSource,
                        "external SDK loose XamlReader HierarchicalDataTemplate ItemsSource");
                    AssertEqual("Children", parsedItemsSource.Path.Path, "external SDK loose XamlReader HierarchicalDataTemplate ItemsSource path");
                    var parsedHierarchicalRoot = RequireType<StackPanel>(
                        parsedHierarchicalTemplate.LoadContent(),
                        "external SDK loose XamlReader HierarchicalDataTemplate root");
                    AssertEqual(2, parsedHierarchicalRoot.Children.Count, "external SDK loose XamlReader HierarchicalDataTemplate child count");
                    var parsedNodeNameText = RequireType<TextBlock>(
                        parsedHierarchicalRoot.Children[0],
                        "external SDK loose XamlReader HierarchicalDataTemplate name text");
                    var parsedNodeKindText = RequireType<TextBlock>(
                        parsedHierarchicalRoot.Children[1],
                        "external SDK loose XamlReader HierarchicalDataTemplate kind text");
                    AssertEqual("Name", parsedNodeNameText.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path, "external SDK loose XamlReader HierarchicalDataTemplate name binding path");
                    AssertEqual("Kind", parsedNodeKindText.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path, "external SDK loose XamlReader HierarchicalDataTemplate kind binding path");

                    string hierarchicalTemplateSerialized = XamlWriter.Save(hierarchicalTemplateDictionary);
                    AssertContains("HierarchicalDataTemplate", hierarchicalTemplateSerialized, "external SDK loose XamlWriter serialized HierarchicalDataTemplate");
                    AssertContains("ExternalWriterNodeTemplate", hierarchicalTemplateSerialized, "external SDK loose XamlWriter serialized HierarchicalDataTemplate key");
                    AssertContains("ItemsSource", hierarchicalTemplateSerialized, "external SDK loose XamlWriter serialized HierarchicalDataTemplate ItemsSource");
                    AssertContains("HierarchicalDataTemplate.Triggers", hierarchicalTemplateSerialized, "external SDK loose XamlWriter serialized HierarchicalDataTemplate triggers");
                    var roundTrippedHierarchicalTemplates = RequireType<ResourceDictionary>(
                        XamlReader.Parse(hierarchicalTemplateSerialized),
                        "external SDK loose XamlWriter round-trip HierarchicalDataTemplate dictionary");
                    var hierarchicalTemplate = RequireType<HierarchicalDataTemplate>(
                        roundTrippedHierarchicalTemplates["ExternalWriterNodeTemplate"],
                        "external SDK loose XamlWriter round-trip HierarchicalDataTemplate");
                    var itemsSource = RequireType<Binding>(
                        hierarchicalTemplate.ItemsSource,
                        "external SDK loose XamlWriter round-trip HierarchicalDataTemplate ItemsSource");
                    AssertEqual("Children", itemsSource.Path.Path, "external SDK loose XamlWriter round-trip HierarchicalDataTemplate ItemsSource path");
                    AssertEqual(1, hierarchicalTemplate.Triggers.Count, "external SDK loose XamlWriter round-trip HierarchicalDataTemplate trigger count");
                    var hierarchicalTrigger = RequireType<DataTrigger>(
                        hierarchicalTemplate.Triggers[0],
                        "external SDK loose XamlWriter round-trip HierarchicalDataTemplate trigger");
                    var hierarchicalTriggerBinding = RequireType<Binding>(
                        hierarchicalTrigger.Binding,
                        "external SDK loose XamlWriter round-trip HierarchicalDataTemplate trigger binding");
                    AssertEqual("IsActive", hierarchicalTriggerBinding.Path.Path, "external SDK loose XamlWriter round-trip HierarchicalDataTemplate trigger binding path");
                    AssertEqual("True", hierarchicalTrigger.Value?.ToString(), "external SDK loose XamlWriter round-trip HierarchicalDataTemplate trigger value");
                    AssertEqual(1, hierarchicalTrigger.Setters.Count, "external SDK loose XamlWriter round-trip HierarchicalDataTemplate trigger setter count");
                    var hierarchicalTriggerSetter = AssertLooseStyleSetter(
                        hierarchicalTrigger.Setters[0],
                        FrameworkElement.TagProperty,
                        "external active writer node",
                        "external SDK loose XamlWriter HierarchicalDataTemplate trigger Tag setter");
                    AssertEqual("ExternalNodeNameText", hierarchicalTriggerSetter.TargetName, "external SDK loose XamlWriter HierarchicalDataTemplate trigger setter target");

                    var hierarchicalRoot = RequireType<StackPanel>(
                        hierarchicalTemplate.LoadContent(),
                        "external SDK loose XamlWriter round-trip HierarchicalDataTemplate root");
                    AssertEqual("ExternalNodeTemplateRoot", hierarchicalRoot.Name, "external SDK loose XamlWriter round-trip HierarchicalDataTemplate root name");
                    AssertEqual("external writer hierarchical template root", hierarchicalRoot.Tag, "external SDK loose XamlWriter round-trip HierarchicalDataTemplate root tag");
                    AssertEqual(2, hierarchicalRoot.Children.Count, "external SDK loose XamlWriter round-trip HierarchicalDataTemplate child count");
                    AssertEqual(
                        "ExternalNodeNameText",
                        RequireType<TextBlock>(
                            hierarchicalRoot.Children[0],
                            "external SDK loose XamlWriter round-trip HierarchicalDataTemplate name text").Name,
                        "external SDK loose XamlWriter round-trip HierarchicalDataTemplate name TextBlock name");
                    AssertEqual(
                        "ExternalNodeKindText",
                        RequireType<TextBlock>(
                            hierarchicalRoot.Children[1],
                            "external SDK loose XamlWriter round-trip HierarchicalDataTemplate kind text").Name,
                        "external SDK loose XamlWriter round-trip HierarchicalDataTemplate kind TextBlock name");

                    string itemsPanelTemplateDictionaryXaml =
                        "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                        "<ItemsPanelTemplate x:Key=\"ExternalWriterItemsPanelTemplate\">" +
                        "<WrapPanel x:Name=\"ExternalWriterItemsHostPanel\" Orientation=\"Horizontal\" ItemWidth=\"48\" ItemHeight=\"24\" Tag=\"external writer items panel\" />" +
                        "</ItemsPanelTemplate>" +
                        "</ResourceDictionary>";
                    var itemsPanelTemplateDictionary = RequireType<ResourceDictionary>(
                        XamlReader.Parse(itemsPanelTemplateDictionaryXaml),
                        "external SDK loose XamlWriter ItemsPanelTemplate dictionary source");
                    string itemsPanelTemplateSerialized = XamlWriter.Save(itemsPanelTemplateDictionary);
                    AssertContains("ItemsPanelTemplate", itemsPanelTemplateSerialized, "external SDK loose XamlWriter serialized ItemsPanelTemplate");
                    AssertContains("ExternalWriterItemsPanelTemplate", itemsPanelTemplateSerialized, "external SDK loose XamlWriter serialized ItemsPanelTemplate key");
                    AssertContains("WrapPanel", itemsPanelTemplateSerialized, "external SDK loose XamlWriter serialized ItemsPanelTemplate panel");
                    AssertContains("ExternalWriterItemsHostPanel", itemsPanelTemplateSerialized, "external SDK loose XamlWriter serialized ItemsPanelTemplate panel name");
                    var roundTrippedItemsPanelTemplates = RequireType<ResourceDictionary>(
                        XamlReader.Parse(itemsPanelTemplateSerialized),
                        "external SDK loose XamlWriter round-trip ItemsPanelTemplate dictionary");
                    var itemsPanelTemplate = RequireType<ItemsPanelTemplate>(
                        roundTrippedItemsPanelTemplates["ExternalWriterItemsPanelTemplate"],
                        "external SDK loose XamlWriter round-trip ItemsPanelTemplate");
                    var itemsPanel = RequireType<WrapPanel>(
                        itemsPanelTemplate.LoadContent(),
                        "external SDK loose XamlWriter round-trip ItemsPanelTemplate panel");
                    AssertEqual("ExternalWriterItemsHostPanel", itemsPanel.Name, "external SDK loose XamlWriter round-trip ItemsPanelTemplate panel name");
                    AssertEqual("external writer items panel", itemsPanel.Tag, "external SDK loose XamlWriter round-trip ItemsPanelTemplate panel tag");
                    AssertEqual(Orientation.Horizontal, itemsPanel.Orientation, "external SDK loose XamlWriter round-trip ItemsPanelTemplate orientation");
                    AssertEqual(48.0, itemsPanel.ItemWidth, "external SDK loose XamlWriter round-trip ItemsPanelTemplate item width");
                    AssertEqual(24.0, itemsPanel.ItemHeight, "external SDK loose XamlWriter round-trip ItemsPanelTemplate item height");

                    string groupStyleDictionaryXaml =
                        "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                        "<GroupStyle x:Key=\"ExternalWriterGroupStyle\" HidesIfEmpty=\"True\">" +
                        "<GroupStyle.HeaderTemplate>" +
                        "<DataTemplate>" +
                        "<StackPanel x:Name=\"ExternalWriterGroupHeaderRoot\" Tag=\"external writer group header root\">" +
                        "<TextBlock x:Name=\"ExternalWriterGroupHeaderText\" Text=\"{Binding Name}\" Tag=\"external writer group header text\" />" +
                        "</StackPanel>" +
                        "</DataTemplate>" +
                        "</GroupStyle.HeaderTemplate>" +
                        "<GroupStyle.Panel>" +
                        "<ItemsPanelTemplate>" +
                        "<StackPanel x:Name=\"ExternalWriterGroupItemsPanel\" Orientation=\"Horizontal\" Tag=\"external writer group panel\" />" +
                        "</ItemsPanelTemplate>" +
                        "</GroupStyle.Panel>" +
                        "</GroupStyle>" +
                        "</ResourceDictionary>";
                    var groupStyleDictionary = RequireType<ResourceDictionary>(
                        XamlReader.Parse(groupStyleDictionaryXaml),
                        "external SDK loose XamlWriter GroupStyle dictionary source");
                    var parsedGroupStyle = RequireType<GroupStyle>(
                        groupStyleDictionary["ExternalWriterGroupStyle"],
                        "external SDK loose XamlReader GroupStyle");
                    AssertEqual(true, parsedGroupStyle.HidesIfEmpty, "external SDK loose XamlReader GroupStyle HidesIfEmpty");
                    var parsedGroupHeaderTemplate = RequireType<DataTemplate>(
                        parsedGroupStyle.HeaderTemplate,
                        "external SDK loose XamlReader GroupStyle HeaderTemplate");
                    var parsedGroupHeaderRoot = RequireType<StackPanel>(
                        parsedGroupHeaderTemplate.LoadContent(),
                        "external SDK loose XamlReader GroupStyle header root");
                    AssertEqual(1, parsedGroupHeaderRoot.Children.Count, "external SDK loose XamlReader GroupStyle header child count");
                    AssertEqual(
                        "Name",
                        RequireType<TextBlock>(
                            parsedGroupHeaderRoot.Children[0],
                            "external SDK loose XamlReader GroupStyle header text").GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path,
                        "external SDK loose XamlReader GroupStyle header binding path");
                    var parsedGroupPanelTemplate = RequireType<ItemsPanelTemplate>(
                        parsedGroupStyle.Panel,
                        "external SDK loose XamlReader GroupStyle Panel");
                    var parsedGroupPanel = RequireType<StackPanel>(
                        parsedGroupPanelTemplate.LoadContent(),
                        "external SDK loose XamlReader GroupStyle panel root");
                    AssertEqual(Orientation.Horizontal, parsedGroupPanel.Orientation, "external SDK loose XamlReader GroupStyle panel orientation");

                    string groupStyleSerialized = XamlWriter.Save(groupStyleDictionary);
                    AssertContains("GroupStyle", groupStyleSerialized, "external SDK loose XamlWriter serialized GroupStyle");
                    AssertContains("ExternalWriterGroupStyle", groupStyleSerialized, "external SDK loose XamlWriter serialized GroupStyle key");
                    AssertContains("GroupStyle.HeaderTemplate", groupStyleSerialized, "external SDK loose XamlWriter serialized GroupStyle HeaderTemplate");
                    AssertContains("GroupStyle.Panel", groupStyleSerialized, "external SDK loose XamlWriter serialized GroupStyle Panel");
                    AssertContains("ExternalWriterGroupHeaderRoot", groupStyleSerialized, "external SDK loose XamlWriter serialized GroupStyle header root");
                    AssertContains("ExternalWriterGroupItemsPanel", groupStyleSerialized, "external SDK loose XamlWriter serialized GroupStyle panel name");
                    var roundTrippedGroupStyles = RequireType<ResourceDictionary>(
                        XamlReader.Parse(groupStyleSerialized),
                        "external SDK loose XamlWriter round-trip GroupStyle dictionary");
                    var groupStyle = RequireType<GroupStyle>(
                        roundTrippedGroupStyles["ExternalWriterGroupStyle"],
                        "external SDK loose XamlWriter round-trip GroupStyle");
                    AssertEqual(true, groupStyle.HidesIfEmpty, "external SDK loose XamlWriter round-trip GroupStyle HidesIfEmpty");

                    var groupHeaderTemplate = RequireType<DataTemplate>(
                        groupStyle.HeaderTemplate,
                        "external SDK loose XamlWriter round-trip GroupStyle HeaderTemplate");
                    var groupHeaderRoot = RequireType<StackPanel>(
                        groupHeaderTemplate.LoadContent(),
                        "external SDK loose XamlWriter round-trip GroupStyle header root");
                    AssertEqual("ExternalWriterGroupHeaderRoot", groupHeaderRoot.Name, "external SDK loose XamlWriter round-trip GroupStyle header root name");
                    AssertEqual("external writer group header root", groupHeaderRoot.Tag, "external SDK loose XamlWriter round-trip GroupStyle header root tag");
                    AssertEqual(1, groupHeaderRoot.Children.Count, "external SDK loose XamlWriter round-trip GroupStyle header child count");
                    var groupHeaderText = RequireType<TextBlock>(
                        groupHeaderRoot.Children[0],
                        "external SDK loose XamlWriter round-trip GroupStyle header text");
                    AssertEqual("ExternalWriterGroupHeaderText", groupHeaderText.Name, "external SDK loose XamlWriter round-trip GroupStyle header TextBlock name");
                    AssertEqual("external writer group header text", groupHeaderText.Tag, "external SDK loose XamlWriter round-trip GroupStyle header TextBlock tag");

                    var groupPanelTemplate = RequireType<ItemsPanelTemplate>(
                        groupStyle.Panel,
                        "external SDK loose XamlWriter round-trip GroupStyle Panel");
                    var groupPanel = RequireType<StackPanel>(
                        groupPanelTemplate.LoadContent(),
                        "external SDK loose XamlWriter round-trip GroupStyle panel");
                    AssertEqual("ExternalWriterGroupItemsPanel", groupPanel.Name, "external SDK loose XamlWriter round-trip GroupStyle panel name");
                    AssertEqual("external writer group panel", groupPanel.Tag, "external SDK loose XamlWriter round-trip GroupStyle panel tag");
                    AssertEqual(Orientation.Horizontal, groupPanel.Orientation, "external SDK loose XamlWriter round-trip GroupStyle panel orientation");

                    string frameworkElementXaml =
                        "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                        "x:Name=\"ExternalWriterElementRoot\" Orientation=\"Vertical\" Tag=\"external writer root\">" +
                        "<Button x:Name=\"ExternalWriterButton\" Content=\"external writer button\" Tag=\"external writer button tag\" MinWidth=\"96\" Background=\"#2F6E8E\" />" +
                        "<TextBox x:Name=\"ExternalWriterTextBox\" Text=\"external writer text\" MinWidth=\"120\" />" +
                        "</StackPanel>";
                    var frameworkElementRoot = RequireType<StackPanel>(
                        XamlReader.Parse(frameworkElementXaml),
                        "external SDK loose XamlWriter FrameworkElement source root");
                    string frameworkElementSerialized = XamlWriter.Save(frameworkElementRoot);
                    AssertContains("StackPanel", frameworkElementSerialized, "external SDK loose XamlWriter serialized FrameworkElement root");
                    AssertContains("ExternalWriterElementRoot", frameworkElementSerialized, "external SDK loose XamlWriter serialized FrameworkElement root name");
                    AssertContains("ExternalWriterButton", frameworkElementSerialized, "external SDK loose XamlWriter serialized FrameworkElement button");
                    AssertContains("ExternalWriterTextBox", frameworkElementSerialized, "external SDK loose XamlWriter serialized FrameworkElement TextBox");

                    var roundTrippedFrameworkElementRoot = RequireType<StackPanel>(
                        XamlReader.Parse(frameworkElementSerialized),
                        "external SDK loose XamlWriter round-trip FrameworkElement root");
                    AssertEqual("ExternalWriterElementRoot", roundTrippedFrameworkElementRoot.Name, "external SDK loose XamlWriter round-trip FrameworkElement root name");
                    AssertEqual(Orientation.Vertical, roundTrippedFrameworkElementRoot.Orientation, "external SDK loose XamlWriter round-trip FrameworkElement root orientation");
                    AssertEqual("external writer root", roundTrippedFrameworkElementRoot.Tag, "external SDK loose XamlWriter round-trip FrameworkElement root tag");
                    AssertEqual(2, roundTrippedFrameworkElementRoot.Children.Count, "external SDK loose XamlWriter round-trip FrameworkElement child count");

                    var roundTrippedFrameworkElementButton = RequireType<Button>(
                        roundTrippedFrameworkElementRoot.Children[0],
                        "external SDK loose XamlWriter round-trip FrameworkElement button");
                    AssertEqual("ExternalWriterButton", roundTrippedFrameworkElementButton.Name, "external SDK loose XamlWriter round-trip FrameworkElement button name");
                    AssertEqual("external writer button", roundTrippedFrameworkElementButton.Content, "external SDK loose XamlWriter round-trip FrameworkElement button content");
                    AssertEqual("external writer button tag", roundTrippedFrameworkElementButton.Tag, "external SDK loose XamlWriter round-trip FrameworkElement button tag");
                    AssertEqual(96.0, roundTrippedFrameworkElementButton.MinWidth, "external SDK loose XamlWriter round-trip FrameworkElement button min width");
                    AssertBrushColor(roundTrippedFrameworkElementButton.Background, "#FF2F6E8E", "external SDK loose XamlWriter round-trip FrameworkElement button background");

                    var roundTrippedFrameworkElementTextBox = RequireType<TextBox>(
                        roundTrippedFrameworkElementRoot.Children[1],
                        "external SDK loose XamlWriter round-trip FrameworkElement TextBox");
                    AssertEqual("ExternalWriterTextBox", roundTrippedFrameworkElementTextBox.Name, "external SDK loose XamlWriter round-trip FrameworkElement TextBox name");
                    AssertEqual("external writer text", roundTrippedFrameworkElementTextBox.Text, "external SDK loose XamlWriter round-trip FrameworkElement TextBox text");
                    AssertEqual(120.0, roundTrippedFrameworkElementTextBox.MinWidth, "external SDK loose XamlWriter round-trip FrameworkElement TextBox min width");

                    string flowDocumentXaml =
                        "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                        "FontSize=\"14\" Tag=\"external writer document\">" +
                        "<Paragraph Name=\"ExternalWriterParagraph\" Tag=\"external writer paragraph\">" +
                        "external writer paragraph <Bold>bold text</Bold><Italic> italic text</Italic><Underline> underline text</Underline>" +
                        "<Hyperlink NavigateUri=\"https://example.test/external-sdk-writer\">link text</Hyperlink>" +
                        "</Paragraph>" +
                        "<Section Name=\"ExternalWriterSection\"><Paragraph>external writer section text</Paragraph></Section>" +
                        "<Table CellSpacing=\"2\">" +
                        "<Table.Columns><TableColumn /><TableColumn /></Table.Columns>" +
                        "<TableRowGroup><TableRow>" +
                        "<TableCell><Paragraph>external writer table alpha</Paragraph></TableCell>" +
                        "<TableCell><Paragraph>external writer table beta</Paragraph></TableCell>" +
                        "</TableRow></TableRowGroup>" +
                        "</Table>" +
                        "<List MarkerStyle=\"Decimal\">" +
                        "<ListItem><Paragraph>external writer first item</Paragraph></ListItem>" +
                        "<ListItem><Paragraph>external writer second item</Paragraph></ListItem>" +
                        "</List>" +
                        "</FlowDocument>";
                    var flowDocument = RequireType<FlowDocument>(
                        XamlReader.Parse(flowDocumentXaml),
                        "external SDK loose XamlWriter FlowDocument source");
                    string flowDocumentSerialized = XamlWriter.Save(flowDocument);
                    AssertContains("FlowDocument", flowDocumentSerialized, "external SDK loose XamlWriter serialized FlowDocument root");
                    AssertContains("ExternalWriterParagraph", flowDocumentSerialized, "external SDK loose XamlWriter serialized FlowDocument paragraph name");
                    AssertContains("Bold", flowDocumentSerialized, "external SDK loose XamlWriter serialized FlowDocument Bold");
                    AssertContains("Hyperlink", flowDocumentSerialized, "external SDK loose XamlWriter serialized FlowDocument Hyperlink");
                    AssertContains("Table", flowDocumentSerialized, "external SDK loose XamlWriter serialized FlowDocument Table");
                    AssertContains("List", flowDocumentSerialized, "external SDK loose XamlWriter serialized FlowDocument List");
                    if (flowDocumentSerialized.Contains(" Name=\"\"", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Expected external SDK loose XamlWriter serialized FlowDocument not to emit empty runtime names, got '{flowDocumentSerialized}'.");
                    }

                    var roundTrippedFlowDocument = RequireType<FlowDocument>(
                        XamlReader.Parse(flowDocumentSerialized),
                        "external SDK loose XamlWriter round-trip FlowDocument");
                    AssertEqual(14.0, roundTrippedFlowDocument.FontSize, "external SDK loose XamlWriter round-trip FlowDocument font size");
                    AssertEqual("external writer document", roundTrippedFlowDocument.Tag, "external SDK loose XamlWriter round-trip FlowDocument tag");
                    AssertEqual(4, roundTrippedFlowDocument.Blocks.Count, "external SDK loose XamlWriter round-trip FlowDocument block count");

                    var roundTrippedParagraph = RequireType<Paragraph>(
                        roundTrippedFlowDocument.Blocks.FirstBlock,
                        "external SDK loose XamlWriter round-trip FlowDocument paragraph");
                    AssertEqual("ExternalWriterParagraph", roundTrippedParagraph.Name, "external SDK loose XamlWriter round-trip FlowDocument paragraph name");
                    AssertEqual("external writer paragraph", roundTrippedParagraph.Tag, "external SDK loose XamlWriter round-trip FlowDocument paragraph tag");
                    var roundTrippedBold = RequireFirstInline<Bold>(
                        roundTrippedParagraph.Inlines,
                        "external SDK loose XamlWriter round-trip FlowDocument bold inline");
                    AssertEqual("bold text", RequireFirstInline<Run>(roundTrippedBold.Inlines, "external SDK loose XamlWriter round-trip FlowDocument bold run").Text, "external SDK loose XamlWriter round-trip FlowDocument bold text");
                    var roundTrippedItalic = RequireFirstInline<Italic>(
                        roundTrippedParagraph.Inlines,
                        "external SDK loose XamlWriter round-trip FlowDocument italic inline");
                    AssertEqual("italic text", RequireFirstInline<Run>(roundTrippedItalic.Inlines, "external SDK loose XamlWriter round-trip FlowDocument italic run").Text, "external SDK loose XamlWriter round-trip FlowDocument italic text");
                    var roundTrippedUnderline = RequireFirstInline<Underline>(
                        roundTrippedParagraph.Inlines,
                        "external SDK loose XamlWriter round-trip FlowDocument underline inline");
                    AssertEqual("underline text", RequireFirstInline<Run>(roundTrippedUnderline.Inlines, "external SDK loose XamlWriter round-trip FlowDocument underline run").Text, "external SDK loose XamlWriter round-trip FlowDocument underline text");
                    var roundTrippedHyperlink = RequireFirstInline<Hyperlink>(
                        roundTrippedParagraph.Inlines,
                        "external SDK loose XamlWriter round-trip FlowDocument hyperlink");
                    AssertEqual("https://example.test/external-sdk-writer", roundTrippedHyperlink.NavigateUri?.ToString(), "external SDK loose XamlWriter round-trip FlowDocument hyperlink URI");

                    var roundTrippedSection = RequireType<Section>(
                        roundTrippedParagraph.NextBlock,
                        "external SDK loose XamlWriter round-trip FlowDocument section");
                    AssertEqual("ExternalWriterSection", roundTrippedSection.Name, "external SDK loose XamlWriter round-trip FlowDocument section name");
                    AssertParagraphText(
                        RequireType<Paragraph>(roundTrippedSection.Blocks.FirstBlock, "external SDK loose XamlWriter round-trip FlowDocument section paragraph"),
                        "external writer section text",
                        "writer section");

                    var roundTrippedTable = RequireType<Table>(
                        roundTrippedSection.NextBlock,
                        "external SDK loose XamlWriter round-trip FlowDocument table");
                    AssertEqual(2, roundTrippedTable.Columns.Count, "external SDK loose XamlWriter round-trip FlowDocument table columns");
                    AssertEqual(1, roundTrippedTable.RowGroups.Count, "external SDK loose XamlWriter round-trip FlowDocument table row group count");
                    var roundTrippedRow = roundTrippedTable.RowGroups[0].Rows[0];
                    AssertEqual(2, roundTrippedRow.Cells.Count, "external SDK loose XamlWriter round-trip FlowDocument table cell count");
                    AssertTableCellText(roundTrippedRow.Cells[0], "external writer table alpha", "writer first");
                    AssertTableCellText(roundTrippedRow.Cells[1], "external writer table beta", "writer second");

                    var roundTrippedList = RequireType<System.Windows.Documents.List>(
                        roundTrippedTable.NextBlock,
                        "external SDK loose XamlWriter round-trip FlowDocument list");
                    AssertEqual(TextMarkerStyle.Decimal, roundTrippedList.MarkerStyle, "external SDK loose XamlWriter round-trip FlowDocument list marker style");
                    AssertEqual(2, roundTrippedList.ListItems.Count, "external SDK loose XamlWriter round-trip FlowDocument list item count");
                    AssertListItemText(roundTrippedList.ListItems.FirstListItem, "external writer first item", "writer first");
                    AssertListItemText(roundTrippedList.ListItems.FirstListItem.NextListItem, "external writer second item", "writer second");

                    string flowDocumentText = new TextRange(
                        roundTrippedFlowDocument.ContentStart,
                        roundTrippedFlowDocument.ContentEnd).Text;
                    AssertContains("external writer paragraph", flowDocumentText, "external SDK loose XamlWriter round-trip FlowDocument TextRange paragraph text");
                    AssertContains("bold text", flowDocumentText, "external SDK loose XamlWriter round-trip FlowDocument TextRange bold text");
                    AssertContains("italic text", flowDocumentText, "external SDK loose XamlWriter round-trip FlowDocument TextRange italic text");
                    AssertContains("underline text", flowDocumentText, "external SDK loose XamlWriter round-trip FlowDocument TextRange underline text");
                    AssertContains("link text", flowDocumentText, "external SDK loose XamlWriter round-trip FlowDocument TextRange hyperlink text");
                    AssertContains("external writer section text", flowDocumentText, "external SDK loose XamlWriter round-trip FlowDocument TextRange section text");
                    AssertContains("external writer table alpha", flowDocumentText, "external SDK loose XamlWriter round-trip FlowDocument TextRange first table cell");
                    AssertContains("external writer table beta", flowDocumentText, "external SDK loose XamlWriter round-trip FlowDocument TextRange second table cell");
                    AssertContains("external writer first item", flowDocumentText, "external SDK loose XamlWriter round-trip FlowDocument TextRange first list item");
                    AssertContains("external writer second item", flowDocumentText, "external SDK loose XamlWriter round-trip FlowDocument TextRange second list item");
                }

                private static void ValidateDataProviders(MainWindow window)
                {
                    var objectProvider = RequireType<ObjectDataProvider>(
                        window.FindResource("ExternalObjectDataProvider"),
                        "external SDK ObjectDataProvider resource");
                    AssertEqual(false, objectProvider.IsAsynchronous, "external SDK ObjectDataProvider synchronous flag");
                    AssertEqual("CreateSummary", objectProvider.MethodName, "external SDK ObjectDataProvider method name");
                    AssertEqual(typeof(ExternalResourceFactory), objectProvider.ObjectType, "external SDK ObjectDataProvider object type");
                    AssertEqual(2, objectProvider.MethodParameters.Count, "external SDK ObjectDataProvider method parameter count");
                    AssertEqual("external-provider", RequireType<string>(objectProvider.MethodParameters[0], "external SDK ObjectDataProvider first parameter"), "external SDK ObjectDataProvider first parameter");
                    AssertEqual(3, RequireType<int>(objectProvider.MethodParameters[1], "external SDK ObjectDataProvider second parameter"), "external SDK ObjectDataProvider second parameter");
                    AssertEqual("external-provider:3", objectProvider.Data, "external SDK ObjectDataProvider data");

                    var objectProviderText = RequireType<TextBlock>(
                        window.FindName("ExternalObjectProviderText"),
                        "external SDK ObjectDataProvider text block");
                    AssertEqual("external-provider:3", objectProviderText.Text, "external SDK ObjectDataProvider bound text");
                    var objectProviderBinding = objectProviderText.GetBindingExpression(TextBlock.TextProperty)
                        ?? throw new InvalidOperationException("Expected external SDK ObjectDataProvider text BindingExpression.");
                    AssertEqual(objectProvider, objectProviderBinding.ParentBinding.Source, "external SDK ObjectDataProvider binding source");

                    var xmlProvider = RequireType<XmlDataProvider>(
                        window.FindResource("ExternalXmlDataProvider"),
                        "external SDK XmlDataProvider resource");
                    AssertEqual(false, xmlProvider.IsAsynchronous, "external SDK XmlDataProvider synchronous flag");
                    AssertEqual("/external/item", xmlProvider.XPath, "external SDK XmlDataProvider XPath");

                    var xmlProviderText = RequireType<TextBlock>(
                        window.FindName("ExternalXmlProviderText"),
                        "external SDK XmlDataProvider text block");
                    AssertEqual("external-xml", xmlProviderText.Text, "external SDK XmlDataProvider bound text");
                    var xmlProviderBinding = xmlProviderText.GetBindingExpression(TextBlock.TextProperty)
                        ?? throw new InvalidOperationException("Expected external SDK XmlDataProvider text BindingExpression.");
                    AssertEqual(xmlProvider, xmlProviderBinding.ParentBinding.Source, "external SDK XmlDataProvider binding source");
                    AssertEqual("@name", xmlProviderBinding.ParentBinding.XPath, "external SDK XmlDataProvider binding XPath");
                }

                private static void ValidateBindings(MainWindow window)
                {
                    var converterText = RequireType<TextBlock>(
                        window.FindName("ExternalConverterText"),
                        "external SDK converter text block");
                    AssertEqual("ALPHA:converted", converterText.Text, "external SDK value converter output");

                    var multiBindingText = RequireType<TextBlock>(
                        window.FindName("ExternalMultiBindingText"),
                        "external SDK multibinding text block");
                    AssertEqual("Alpha|Framework", multiBindingText.Text, "external SDK multibinding converter output");
                    var multiBindingExpression = BindingOperations.GetMultiBindingExpression(
                        multiBindingText,
                        TextBlock.TextProperty);
                    if (multiBindingExpression is null)
                    {
                        throw new InvalidOperationException("Expected external SDK MultiBindingExpression.");
                    }

                    AssertEqual(2, multiBindingExpression.ParentMultiBinding.Bindings.Count, "external SDK multibinding child binding count");

                    var priorityBindingText = RequireType<TextBlock>(
                        window.FindName("ExternalPriorityBindingText"),
                        "external SDK priority binding text block");
                    AssertEqual("Framework", priorityBindingText.Text, "external SDK priority binding fallback output");
                    var priorityBindingExpression = BindingOperations.GetPriorityBindingExpression(
                        priorityBindingText,
                        TextBlock.TextProperty);
                    if (priorityBindingExpression is null)
                    {
                        throw new InvalidOperationException("Expected external SDK PriorityBindingExpression.");
                    }

                    AssertEqual(2, priorityBindingExpression.ParentPriorityBinding.Bindings.Count, "external SDK priority binding child binding count");

                    var validationTextBox = RequireType<TextBox>(
                        window.FindName("ExternalValidationTextBox"),
                        "external SDK validation text box");
                    var textBindingExpression = validationTextBox.GetBindingExpression(TextBox.TextProperty)
                        ?? throw new InvalidOperationException("Expected external SDK validation BindingExpression.");
                    AssertEqual("valid external text", validationTextBox.Text, "external SDK validation text initial value");
                    int textChangedBeforeValidation = window.ExternalValidationTextChangedCount;
                    validationTextBox.Text = string.Empty;
                    textBindingExpression.UpdateSource();
                    AssertEqual(true, Validation.GetHasError(validationTextBox), "external SDK validation failure state");
                    AssertAtLeast(textChangedBeforeValidation + 1, window.ExternalValidationTextChangedCount, "external SDK TextBox validation TextChanged failure count");
                    AssertEqual(string.Empty, window.LastExternalValidationText, "external SDK TextBox validation TextChanged failure text");
                    validationTextBox.Text = "recovered external text";
                    textBindingExpression.UpdateSource();
                    AssertEqual(false, Validation.GetHasError(validationTextBox), "external SDK validation recovery state");
                    AssertEqual("recovered external text", window.ValidationText, "external SDK validation source update");
                    AssertAtLeast(textChangedBeforeValidation + 2, window.ExternalValidationTextChangedCount, "external SDK TextBox validation TextChanged recovery count");
                    AssertEqual("recovered external text", window.LastExternalValidationText, "external SDK TextBox validation TextChanged recovery text");

                    int textChangedBeforeEditing = window.ExternalValidationTextChangedCount;
                    validationTextBox.Text = "external editing text";
                    AssertAtLeast(textChangedBeforeEditing + 1, window.ExternalValidationTextChangedCount, "external SDK TextBox editing TextChanged count");
                    AssertEqual("external editing text", window.LastExternalValidationText, "external SDK TextBox editing TextChanged text");
                    validationTextBox.Select(9, 7);
                    AssertEqual(9, validationTextBox.SelectionStart, "external SDK TextBox selection start");
                    AssertEqual(7, validationTextBox.SelectionLength, "external SDK TextBox selection length");
                    AssertEqual("editing", validationTextBox.SelectedText, "external SDK TextBox selected text");
                    validationTextBox.SelectedText = "selection";
                    AssertEqual("external selection text", validationTextBox.Text, "external SDK TextBox selected text replacement");
                    validationTextBox.CaretIndex = validationTextBox.Text.Length;
                    AssertEqual(validationTextBox.Text.Length, validationTextBox.CaretIndex, "external SDK TextBox caret index");
                    validationTextBox.AppendText(" appended");
                    AssertEqual("external selection text appended", validationTextBox.Text, "external SDK TextBox AppendText result");
                    textBindingExpression.UpdateSource();
                    AssertEqual("external selection text appended", window.ValidationText, "external SDK TextBox editing source update");

                    validationTextBox.IsUndoEnabled = false;
                    validationTextBox.Text = string.Empty;
                    validationTextBox.IsUndoEnabled = true;
                    validationTextBox.UndoLimit = 8;
                    AssertEqual(false, validationTextBox.CanUndo, "external SDK TextBox undo stack reset state");
                    validationTextBox.Text = "external undo base";
                    validationTextBox.AppendText(" changed");
                    AssertEqual(false, validationTextBox.CanUndo, "external SDK TextBox programmatic append CanUndo state");
                    AssertEqual(false, validationTextBox.CanRedo, "external SDK TextBox programmatic append CanRedo state");
                    AssertEqual(false, validationTextBox.Undo(), "external SDK TextBox empty Undo result");
                    AssertEqual("external undo base changed", validationTextBox.Text, "external SDK TextBox empty Undo text");
                    AssertEqual(false, validationTextBox.Redo(), "external SDK TextBox empty Redo result");
                    AssertEqual("external undo base changed", validationTextBox.Text, "external SDK TextBox empty Redo text");
                    AssertEqual(8, validationTextBox.UndoLimit, "external SDK TextBox UndoLimit");
                }

                private static void ValidateBindingGroup(MainWindow window)
                {
                    var panel = RequireType<StackPanel>(
                        window.FindName("ExternalBindingGroupPanel"),
                        "external SDK BindingGroup panel");
                    var bindingGroup = panel.BindingGroup
                        ?? throw new InvalidOperationException("Expected external SDK BindingGroup panel to expose a BindingGroup.");

                    AssertEqual("ExternalBindingGroup", bindingGroup.Name, "external SDK BindingGroup name");
                    AssertEqual(1, bindingGroup.Items.Count, "external SDK BindingGroup item count");
                    AssertEqual(window, bindingGroup.Items[0], "external SDK BindingGroup source item");
                    AssertEqual(1, bindingGroup.ValidationRules.Count, "external SDK BindingGroup validation rule count");
                    var rule = RequireType<ExternalBindingGroupValidationRule>(
                        bindingGroup.ValidationRules[0],
                        "external SDK BindingGroup validation rule");
                    AssertEqual("BindingGroupFirstName", rule.FirstProperty, "external SDK BindingGroup first property");
                    AssertEqual("BindingGroupLastName", rule.SecondProperty, "external SDK BindingGroup second property");
                    AssertEqual("group:", rule.RequiredPrefix, "external SDK BindingGroup required prefix");

                    var firstBox = RequireType<TextBox>(
                        window.FindName("ExternalBindingGroupFirstBox"),
                        "external SDK BindingGroup first text box");
                    var lastBox = RequireType<TextBox>(
                        window.FindName("ExternalBindingGroupLastBox"),
                        "external SDK BindingGroup last text box");
                    AssertEqual("group: Ada", firstBox.Text, "external SDK BindingGroup first initial text");
                    AssertEqual("group: Lovelace", lastBox.Text, "external SDK BindingGroup last initial text");
                    AssertEqual("group: Ada", window.BindingGroupFirstName, "external SDK BindingGroup first initial source");
                    AssertEqual("group: Lovelace", window.BindingGroupLastName, "external SDK BindingGroup last initial source");
                    AssertEqual("BindingGroupFirstName", firstBox.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path, "external SDK BindingGroup first binding path");
                    AssertEqual("BindingGroupLastName", lastBox.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path, "external SDK BindingGroup last binding path");

                    AssertEqual(false, Validation.GetHasError(panel), "external SDK BindingGroup initial error state");
                    AssertEqual(true, bindingGroup.ValidateWithoutUpdate(), "external SDK BindingGroup initial validation");

                    firstBox.Text = "Ada";
                    AssertEqual(false, bindingGroup.CommitEdit(), "external SDK BindingGroup rejected commit");
                    AssertEqual("group: Ada", window.BindingGroupFirstName, "external SDK BindingGroup rejected first source");
                    AssertEqual("group: Lovelace", window.BindingGroupLastName, "external SDK BindingGroup rejected last source");
                    AssertEqual(true, Validation.GetHasError(panel), "external SDK BindingGroup rejected error state");

                    firstBox.Text = "group: Grace";
                    lastBox.Text = "group: Hopper";
                    AssertEqual(true, bindingGroup.CommitEdit(), "external SDK BindingGroup accepted commit");
                    AssertEqual("group: Grace", window.BindingGroupFirstName, "external SDK BindingGroup accepted first source");
                    AssertEqual("group: Hopper", window.BindingGroupLastName, "external SDK BindingGroup accepted last source");
                    AssertEqual(false, Validation.GetHasError(panel), "external SDK BindingGroup accepted error state");
                }

                private static void ValidateVisualStateTransitions(MainWindow window)
                {
                    var styledButton = RequireType<Button>(
                        window.FindName("ExternalStyledButton"),
                        "external SDK Application.Run visual-state button");
                    styledButton.ApplyTemplate();
                    var template = styledButton.Template
                        ?? throw new InvalidOperationException("Expected external SDK Application.Run visual-state button template.");
                    var templateContent = RequireType<ContentPresenter>(
                        template.FindName("ExternalTemplateContent", styledButton),
                        "external SDK Application.Run visual-state content presenter");

                    AssertEqual(true, VisualStateManager.GoToState(styledButton, "Pressed", false), "external SDK Application.Run VisualStateManager Pressed transition");
                    DrainDispatcher();
                    AssertClose(0.42, templateContent.Opacity, "external SDK Application.Run VisualStateManager Pressed opacity");
                    AssertEqual(true, VisualStateManager.GoToState(styledButton, "Normal", false), "external SDK Application.Run VisualStateManager Normal transition");
                    DrainDispatcher();
                    AssertClose(1.0, templateContent.Opacity, "external SDK Application.Run VisualStateManager Normal opacity");
                }

                private static void ValidateStylesAndTemplates(MainWindow window)
                {
                    var basedStyle = RequireType<Style>(
                        window.FindResource("ExternalBasedButtonStyle"),
                        "external SDK based button style");
                    var triggeredStyle = RequireType<Style>(
                        window.FindResource("ExternalTriggeredButtonStyle"),
                        "external SDK triggered button style");
                    var buttonTemplate = RequireType<ControlTemplate>(
                        window.FindResource("ExternalButtonTemplate"),
                        "external SDK button control template");

                    AssertEqual(typeof(Button), basedStyle.TargetType, "external SDK based style target type");
                    AssertEqual(typeof(Button), triggeredStyle.TargetType, "external SDK triggered style target type");
                    AssertEqual(basedStyle, triggeredStyle.BasedOn, "external SDK style BasedOn link");
                    AssertEqual(3, basedStyle.Setters.Count, "external SDK based style setter count");
                    AssertEqual(2, triggeredStyle.Setters.Count, "external SDK triggered style setter count");
                    AssertEqual(1, triggeredStyle.Triggers.Count, "external SDK triggered style trigger count");
                    AssertEqual(typeof(Button), buttonTemplate.TargetType, "external SDK control template target type");

                    var styledButton = RequireType<Button>(
                        window.FindName("ExternalStyledButton"),
                        "external SDK styled button");
                    AssertEqual(triggeredStyle, styledButton.Style, "external SDK styled button style");
                    AssertEqual("External styled button", styledButton.Content, "external SDK styled button content setter");
                    AssertEqual("base-style", styledButton.Tag, "external SDK BasedOn style tag setter");
                    AssertBrushColor(styledButton.Background, "#FF254C6A", "external SDK BasedOn style background");
                    AssertBrushColor(styledButton.Foreground, "#FFF4D35E", "external SDK BasedOn style foreground");

                    styledButton.ApplyTemplate();
                    var templateRoot = RequireType<Border>(
                        buttonTemplate.FindName("ExternalTemplateRoot", styledButton),
                        "external SDK styled button template root");
                    var templateContent = RequireType<ContentPresenter>(
                        buttonTemplate.FindName("ExternalTemplateContent", styledButton),
                        "external SDK styled button template content presenter");
                    var stateGroups = VisualStateManager.GetVisualStateGroups(templateRoot);
                    AssertEqual(1, stateGroups.Count, "external SDK VisualStateManager group count");
                    var commonStates = RequireType<VisualStateGroup>(
                        stateGroups[0],
                        "external SDK VisualStateManager common states group");
                    AssertEqual("ExternalCommonStates", commonStates.Name, "external SDK VisualStateManager group name");
                    AssertEqual(2, commonStates.States.Count, "external SDK VisualStateManager state count");
                    var normalState = commonStates.States
                        .OfType<VisualState>()
                        .FirstOrDefault(state => state.Name == "Normal")
                        ?? throw new InvalidOperationException("Expected external SDK VisualStateManager Normal state.");
                    var pressedState = commonStates.States
                        .OfType<VisualState>()
                        .FirstOrDefault(state => state.Name == "Pressed")
                        ?? throw new InvalidOperationException("Expected external SDK VisualStateManager Pressed state.");
                    var normalStoryboard = normalState.Storyboard
                        ?? throw new InvalidOperationException("Expected external SDK VisualStateManager Normal storyboard.");
                    var pressedStoryboard = pressedState.Storyboard
                        ?? throw new InvalidOperationException("Expected external SDK VisualStateManager Pressed storyboard.");
                    AssertEqual(1, normalStoryboard.Children.Count, "external SDK VisualStateManager Normal storyboard child count");
                    AssertEqual(1, pressedStoryboard.Children.Count, "external SDK VisualStateManager Pressed storyboard child count");
                    var pressedAnimation = RequireType<DoubleAnimation>(
                        pressedStoryboard.Children[0],
                        "external SDK VisualStateManager Pressed animation");
                    AssertEqual(0.42, pressedAnimation.To ?? double.NaN, "external SDK VisualStateManager Pressed animation opacity");
                    AssertEqual(TimeSpan.Zero, pressedAnimation.Duration.TimeSpan, "external SDK VisualStateManager Pressed animation duration");
                    AssertBrushColor(templateRoot.Background, "#FF254C6A", "external SDK TemplateBinding background");
                    AssertEqual("External styled button", templateContent.Content, "external SDK TemplateBinding content");

                    styledButton.IsEnabled = false;
                    DrainDispatcher();
                    AssertEqual("disabled-style", styledButton.Tag, "external SDK property trigger tag setter");
                    AssertBrushColor(styledButton.Background, "#FF8E3B46", "external SDK property trigger background setter");
                    AssertBrushColor(templateRoot.Background, "#FF8E3B46", "external SDK TemplateBinding triggered background");

                    styledButton.IsEnabled = true;
                    DrainDispatcher();
                    AssertEqual("base-style", styledButton.Tag, "external SDK property trigger restored tag");
                    AssertBrushColor(styledButton.Background, "#FF254C6A", "external SDK property trigger restored background");
                }

                private static void ValidateMenusAndChoiceControls(MainWindow window)
                {
                    var commandButton = RequireType<Button>(
                        window.FindName("ExternalCommandButton"),
                        "external SDK command button for menu validation");
                    var menu = RequireType<Menu>(
                        window.FindName("ExternalMenu"),
                        "external SDK menu");
                    AssertEqual(1, menu.Items.Count, "external SDK menu root count");
                    var rootItem = RequireType<MenuItem>(
                        menu.Items[0],
                        "external SDK root menu item");
                    AssertEqual("_External", rootItem.Header, "external SDK root menu header");
                    AssertEqual(4, rootItem.Items.Count, "external SDK root menu child count");

                    var commandItem = RequireType<MenuItem>(
                        rootItem.Items[0],
                        "external SDK command menu item");
                    var separator = RequireType<Separator>(
                        rootItem.Items[1],
                        "external SDK menu separator");
                    var clickItem = RequireType<MenuItem>(
                        rootItem.Items[2],
                        "external SDK click menu item");
                    var checkableItem = RequireType<MenuItem>(
                        rootItem.Items[3],
                        "external SDK checkable menu item");
                    AssertEqual("ExternalMenuSeparator", separator.Name, "external SDK menu separator name");
                    AssertEqual(MainWindow.ExternalCommand, commandItem.Command, "external SDK command menu item command");
                    AssertEqual("ExternalMenuCommandParameter", commandItem.CommandParameter, "external SDK command menu item parameter");
                    AssertEqual("_Click", clickItem.Header, "external SDK click menu header");
                    AssertEqual(true, checkableItem.IsCheckable, "external SDK checkable menu flag");

                    int menuCommandExecutedBefore = window.ExternalCommandExecutedCount;
                    RequireType<RoutedCommand>(
                        commandItem.Command,
                        "external SDK command menu routed command")
                        .Execute(commandItem.CommandParameter, commandItem.CommandTarget ?? commandButton);
                    AssertEqual(menuCommandExecutedBefore + 1, window.ExternalCommandExecutedCount, "external SDK menu command executed count");
                    AssertEqual("ExternalMenuCommandParameter", window.LastExternalCommandParameter, "external SDK menu command parameter");

                    int menuClickBefore = window.ExternalMenuClickCount;
                    clickItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, clickItem));
                    AssertEqual(menuClickBefore + 1, window.ExternalMenuClickCount, "external SDK menu click count");
                    AssertEqual("Click", window.LastExternalMenuRoutedEventName, "external SDK menu click routed event");

                    int menuCheckedBefore = window.ExternalMenuCheckedCount;
                    int menuUncheckedBefore = window.ExternalMenuUncheckedCount;
                    checkableItem.IsChecked = true;
                    DrainDispatcher();
                    AssertEqual(true, checkableItem.IsChecked, "external SDK checkable menu checked state");
                    AssertAtLeast(menuCheckedBefore + 1, window.ExternalMenuCheckedCount, "external SDK checkable menu checked count");
                    AssertEqual("Checked", window.LastExternalMenuRoutedEventName, "external SDK checkable menu checked routed event");
                    checkableItem.IsChecked = false;
                    DrainDispatcher();
                    AssertEqual(false, checkableItem.IsChecked, "external SDK checkable menu unchecked state");
                    AssertAtLeast(menuUncheckedBefore + 1, window.ExternalMenuUncheckedCount, "external SDK checkable menu unchecked count");
                    AssertEqual("Unchecked", window.LastExternalMenuRoutedEventName, "external SDK checkable menu unchecked routed event");

                    var popupOwner = RequireType<Button>(
                        window.FindName("ExternalPopupOwnerButton"),
                        "external SDK popup owner button");
                    var toolTip = RequireType<ToolTip>(
                        popupOwner.ToolTip,
                        "external SDK tooltip");
                    var toolTipText = RequireType<TextBlock>(
                        toolTip.Content,
                        "external SDK tooltip text");
                    AssertEqual(PlacementMode.Right, toolTip.Placement, "external SDK tooltip placement");
                    AssertEqual("External tooltip content", toolTipText.Text, "external SDK tooltip content");

                    var contextMenu = RequireType<ContextMenu>(
                        popupOwner.ContextMenu,
                        "external SDK context menu");
                    AssertEqual(4, contextMenu.Items.Count, "external SDK context menu item count");
                    var contextCommandItem = RequireType<MenuItem>(
                        contextMenu.Items[0],
                        "external SDK context command menu item");
                    var contextSeparator = RequireType<Separator>(
                        contextMenu.Items[1],
                        "external SDK context menu separator");
                    var contextClickItem = RequireType<MenuItem>(
                        contextMenu.Items[2],
                        "external SDK context click menu item");
                    var contextCheckableItem = RequireType<MenuItem>(
                        contextMenu.Items[3],
                        "external SDK context checkable menu item");
                    AssertEqual("ExternalContextMenuSeparator", contextSeparator.Name, "external SDK context menu separator name");
                    AssertEqual(MainWindow.ExternalCommand, contextCommandItem.Command, "external SDK context command item command");
                    AssertEqual("ExternalContextCommandParameter", contextCommandItem.CommandParameter, "external SDK context command item parameter");
                    AssertEqual("Context click", contextClickItem.Header, "external SDK context click item header");
                    AssertEqual(true, contextCheckableItem.IsCheckable, "external SDK context checkable menu flag");

                    int contextCommandExecutedBefore = window.ExternalCommandExecutedCount;
                    RequireType<RoutedCommand>(
                        contextCommandItem.Command,
                        "external SDK context command routed command")
                        .Execute(contextCommandItem.CommandParameter, contextCommandItem.CommandTarget ?? commandButton);
                    AssertEqual(contextCommandExecutedBefore + 1, window.ExternalCommandExecutedCount, "external SDK context command executed count");
                    AssertEqual("ExternalContextCommandParameter", window.LastExternalCommandParameter, "external SDK context command parameter");

                    int contextClickBefore = window.ExternalContextMenuClickCount;
                    contextClickItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, contextClickItem));
                    AssertEqual(contextClickBefore + 1, window.ExternalContextMenuClickCount, "external SDK context menu click count");
                    AssertEqual("Click", window.LastExternalContextMenuRoutedEventName, "external SDK context menu click routed event");

                    int contextCheckedBefore = window.ExternalContextMenuCheckedCount;
                    int contextUncheckedBefore = window.ExternalContextMenuUncheckedCount;
                    contextCheckableItem.IsChecked = true;
                    DrainDispatcher();
                    AssertEqual(true, contextCheckableItem.IsChecked, "external SDK context checkable checked state");
                    AssertAtLeast(contextCheckedBefore + 1, window.ExternalContextMenuCheckedCount, "external SDK context checked count");
                    AssertEqual("Checked", window.LastExternalContextMenuRoutedEventName, "external SDK context checked routed event");
                    contextCheckableItem.IsChecked = false;
                    DrainDispatcher();
                    AssertEqual(false, contextCheckableItem.IsChecked, "external SDK context checkable unchecked state");
                    AssertAtLeast(contextUncheckedBefore + 1, window.ExternalContextMenuUncheckedCount, "external SDK context unchecked count");
                    AssertEqual("Unchecked", window.LastExternalContextMenuRoutedEventName, "external SDK context unchecked routed event");

                    var checkBox = RequireType<CheckBox>(
                        window.FindName("ExternalCheckBox"),
                        "external SDK check box");
                    AssertEqual(false, checkBox.IsChecked == true, "external SDK initial check box state");
                    int checkBoxCheckedBefore = window.ExternalCheckBoxCheckedCount;
                    int checkBoxUncheckedBefore = window.ExternalCheckBoxUncheckedCount;
                    checkBox.IsChecked = true;
                    DrainDispatcher();
                    AssertEqual(true, checkBox.IsChecked == true, "external SDK check box checked state");
                    AssertAtLeast(checkBoxCheckedBefore + 1, window.ExternalCheckBoxCheckedCount, "external SDK check box checked count");
                    AssertEqual("Checked", window.LastExternalCheckBoxRoutedEventName, "external SDK check box checked routed event");
                    checkBox.IsChecked = false;
                    DrainDispatcher();
                    AssertEqual(false, checkBox.IsChecked == true, "external SDK check box unchecked state");
                    AssertAtLeast(checkBoxUncheckedBefore + 1, window.ExternalCheckBoxUncheckedCount, "external SDK check box unchecked count");
                    AssertEqual("Unchecked", window.LastExternalCheckBoxRoutedEventName, "external SDK check box unchecked routed event");

                    var radioAlpha = RequireType<RadioButton>(
                        window.FindName("ExternalRadioAlpha"),
                        "external SDK alpha radio button");
                    var radioBeta = RequireType<RadioButton>(
                        window.FindName("ExternalRadioBeta"),
                        "external SDK beta radio button");
                    AssertEqual("ExternalChoiceGroup", radioAlpha.GroupName, "external SDK alpha radio group");
                    AssertEqual("ExternalChoiceGroup", radioBeta.GroupName, "external SDK beta radio group");
                    AssertEqual(false, radioAlpha.IsChecked == true, "external SDK initial alpha radio state");
                    AssertEqual(true, radioBeta.IsChecked == true, "external SDK initial beta radio state");
                    int radioCheckedBefore = window.ExternalRadioButtonCheckedCount;
                    int radioUncheckedBefore = window.ExternalRadioButtonUncheckedCount;
                    radioAlpha.IsChecked = true;
                    DrainDispatcher();
                    AssertEqual(true, radioAlpha.IsChecked == true, "external SDK alpha radio checked state");
                    AssertEqual(false, radioBeta.IsChecked == true, "external SDK beta radio unchecked state");
                    AssertAtLeast(radioCheckedBefore + 1, window.ExternalRadioButtonCheckedCount, "external SDK radio checked count");
                    AssertAtLeast(radioUncheckedBefore + 1, window.ExternalRadioButtonUncheckedCount, "external SDK radio unchecked count");
                    AssertEqual("ExternalRadioAlpha", window.LastExternalRadioButtonCheckedName, "external SDK radio checked sender");
                    AssertEqual("ExternalRadioBeta", window.LastExternalRadioButtonUncheckedName, "external SDK radio unchecked sender");

                    var toggleButton = RequireType<ToggleButton>(
                        window.FindName("ExternalToggleButton"),
                        "external SDK toggle button");
                    AssertEqual(false, toggleButton.IsChecked == true, "external SDK initial toggle state");
                    int toggleCheckedBefore = window.ExternalToggleButtonCheckedCount;
                    int toggleUncheckedBefore = window.ExternalToggleButtonUncheckedCount;
                    toggleButton.IsChecked = true;
                    DrainDispatcher();
                    AssertEqual(true, toggleButton.IsChecked == true, "external SDK toggle checked state");
                    AssertAtLeast(toggleCheckedBefore + 1, window.ExternalToggleButtonCheckedCount, "external SDK toggle checked count");
                    AssertEqual("Checked", window.LastExternalToggleButtonRoutedEventName, "external SDK toggle checked routed event");
                    toggleButton.IsChecked = false;
                    DrainDispatcher();
                    AssertEqual(false, toggleButton.IsChecked == true, "external SDK toggle unchecked state");
                    AssertAtLeast(toggleUncheckedBefore + 1, window.ExternalToggleButtonUncheckedCount, "external SDK toggle unchecked count");
                    AssertEqual("Unchecked", window.LastExternalToggleButtonRoutedEventName, "external SDK toggle unchecked routed event");
                }

                private static void ValidateToolbarStatusRangePasswordDateControls(MainWindow window)
                {
                    var commandButton = RequireType<Button>(
                        window.FindName("ExternalCommandButton"),
                        "external SDK command button for toolbar validation");
                    var toolBarTray = RequireType<ToolBarTray>(
                        window.FindName("ExternalToolBarTray"),
                        "external SDK toolbar tray");
                    var toolBar = RequireType<ToolBar>(
                        window.FindName("ExternalToolBar"),
                        "external SDK toolbar");
                    AssertEqual(1, toolBarTray.ToolBars.Count, "external SDK toolbar tray toolbar count");
                    AssertEqual(toolBar, toolBarTray.ToolBars[0], "external SDK toolbar tray owns toolbar");
                    AssertEqual(3, toolBar.Items.Count, "external SDK toolbar item count");

                    var toolBarCommandButton = RequireType<Button>(
                        toolBar.Items[0],
                        "external SDK toolbar command button");
                    var toolBarSeparator = RequireType<Separator>(
                        toolBar.Items[1],
                        "external SDK toolbar separator");
                    var toolBarToggle = RequireType<ToggleButton>(
                        toolBar.Items[2],
                        "external SDK toolbar toggle");
                    AssertEqual("ExternalToolBarSeparator", toolBarSeparator.Name, "external SDK toolbar separator name");
                    AssertEqual(MainWindow.ExternalCommand, toolBarCommandButton.Command, "external SDK toolbar command button command");
                    AssertEqual("ExternalToolBarCommandParameter", toolBarCommandButton.CommandParameter, "external SDK toolbar command button parameter");
                    int toolBarExecutedBefore = window.ExternalCommandExecutedCount;
                    RequireType<RoutedCommand>(
                        toolBarCommandButton.Command,
                        "external SDK toolbar routed command")
                        .Execute(toolBarCommandButton.CommandParameter, toolBarCommandButton.CommandTarget ?? commandButton);
                    AssertEqual(toolBarExecutedBefore + 1, window.ExternalCommandExecutedCount, "external SDK toolbar routed command count");
                    AssertEqual("ExternalToolBarCommandParameter", window.LastExternalCommandParameter, "external SDK toolbar command parameter");
                    AssertEqual(false, toolBarToggle.IsChecked == true, "external SDK toolbar toggle initial state");
                    toolBarToggle.IsChecked = true;
                    DrainDispatcher();
                    AssertEqual(true, toolBarToggle.IsChecked == true, "external SDK toolbar toggle checked state");

                    var statusBar = RequireType<StatusBar>(
                        window.FindName("ExternalStatusBar"),
                        "external SDK status bar");
                    AssertEqual(2, statusBar.Items.Count, "external SDK status bar item count");
                    var statusBarItem = RequireType<StatusBarItem>(
                        statusBar.Items[0],
                        "external SDK status bar item");
                    var readyText = RequireType<TextBlock>(
                        statusBarItem.Content,
                        "external SDK status bar ready text");
                    var boundStatusText = RequireType<TextBlock>(
                        statusBar.Items[1],
                        "external SDK status bar bound text");
                    AssertEqual("External status ready", readyText.Text, "external SDK status bar ready text");
                    AssertEqual("Alpha", boundStatusText.Text, "external SDK status bar selected item binding");

                    var passwordBox = RequireType<PasswordBox>(
                        window.FindName("ExternalPasswordBox"),
                        "external SDK password box");
                    AssertEqual(16, passwordBox.MaxLength, "external SDK PasswordBox max length");
                    AssertEqual('*', passwordBox.PasswordChar, "external SDK PasswordBox password char");
                    int passwordChangedBefore = window.ExternalPasswordChangedCount;
                    passwordBox.Password = "external-secret";
                    DrainDispatcher();
                    AssertEqual("external-secret", passwordBox.Password, "external SDK PasswordBox password");
                    AssertEqual(15, passwordBox.SecurePassword.Length, "external SDK PasswordBox secure password length");
                    AssertAtLeast(passwordChangedBefore + 1, window.ExternalPasswordChangedCount, "external SDK PasswordBox changed count");
                    passwordBox.Clear();
                    DrainDispatcher();
                    AssertEqual(string.Empty, passwordBox.Password, "external SDK PasswordBox clear password");
                    AssertAtLeast(passwordChangedBefore + 2, window.ExternalPasswordChangedCount, "external SDK PasswordBox clear changed count");

                    var calendar = RequireType<System.Windows.Controls.Calendar>(
                        window.FindName("ExternalCalendar"),
                        "external SDK calendar");
                    var selectedDate = new DateTime(2026, 6, 19);
                    AssertEqual(DayOfWeek.Monday, calendar.FirstDayOfWeek, "external SDK Calendar first day");
                    AssertEqual(System.Windows.Controls.CalendarSelectionMode.SingleDate, calendar.SelectionMode, "external SDK Calendar selection mode");
                    calendar.SelectedDate = selectedDate;
                    AssertEqual(selectedDate, calendar.SelectedDate, "external SDK Calendar selected date");
                    AssertEqual(1, calendar.SelectedDates.Count, "external SDK Calendar selected date collection count");
                    AssertEqual(selectedDate, calendar.SelectedDates[0], "external SDK Calendar selected date collection item");

                    var datePicker = RequireType<DatePicker>(
                        window.FindName("ExternalDatePicker"),
                        "external SDK date picker");
                    var pickedDate = selectedDate.AddDays(1);
                    AssertEqual(DayOfWeek.Monday, datePicker.FirstDayOfWeek, "external SDK DatePicker first day");
                    AssertEqual(DatePickerFormat.Long, datePicker.SelectedDateFormat, "external SDK DatePicker selected date format");
                    datePicker.SelectedDate = pickedDate;
                    AssertEqual(pickedDate, datePicker.SelectedDate, "external SDK DatePicker selected date");

                    var slider = RequireType<Slider>(
                        window.FindName("ExternalSlider"),
                        "external SDK slider");
                    var progressBar = RequireType<ProgressBar>(
                        window.FindName("ExternalProgressBar"),
                        "external SDK progress bar");
                    AssertEqual(0.0, slider.Minimum, "external SDK Slider minimum");
                    AssertEqual(100.0, slider.Maximum, "external SDK Slider maximum");
                    AssertEqual(2.0, slider.SmallChange, "external SDK Slider small change");
                    AssertEqual(10.0, slider.LargeChange, "external SDK Slider large change");
                    AssertEqual(5.0, slider.TickFrequency, "external SDK Slider tick frequency");
                    AssertEqual(true, slider.IsSnapToTickEnabled, "external SDK Slider snap to tick");
                    AssertEqual(25.0, slider.Value, "external SDK Slider initial value");
                    AssertEqual(25.0, progressBar.Value, "external SDK ProgressBar initial bound value");
                    var progressBinding = progressBar.GetBindingExpression(RangeBase.ValueProperty)
                        ?? throw new InvalidOperationException("Expected external SDK ProgressBar Value binding.");
                    AssertEqual("ExternalSlider", progressBinding.ParentBinding.ElementName, "external SDK ProgressBar ElementName binding");
                    int sliderValueChangedBefore = window.ExternalSliderValueChangedCount;
                    slider.Value = 40.0;
                    DrainDispatcher();
                    AssertClose(40.0, slider.Value, "external SDK Slider changed value");
                    AssertClose(40.0, window.LastExternalSliderValue, "external SDK Slider event value");
                    AssertAtLeast(sliderValueChangedBefore + 1, window.ExternalSliderValueChangedCount, "external SDK Slider changed count");
                    AssertClose(40.0, progressBar.Value, "external SDK ProgressBar value after Slider update");
                }

                private static void ValidateAdornerDecorator(MainWindow window)
                {
                    var decorator = RequireType<AdornerDecorator>(
                        window.FindName("ExternalAdornerDecorator"),
                        "external SDK AdornerDecorator");
                    var adornedButton = RequireType<Button>(
                        window.FindName("ExternalAdornedButton"),
                        "external SDK adorned button");
                    AssertEqual(adornedButton, decorator.Child, "external SDK AdornerDecorator child");
                    AssertEqual("External adorned button", adornedButton.Content, "external SDK adorned button content");
                    AssertEqual("external adorned button", adornedButton.Tag, "external SDK adorned button tag");
                }

                private static void ValidateAdornerLayer(MainWindow window)
                {
                    var adornedButton = RequireType<Button>(
                        window.FindName("ExternalAdornedButton"),
                        "external SDK Application.Run adorned button");
                    var adornerLayer = AdornerLayer.GetAdornerLayer(adornedButton)
                        ?? throw new InvalidOperationException("Expected external SDK AdornerLayer after Application.Run startup.");
                    var adorner = new ExternalAdorner(adornedButton);
                    AssertEqual(adornedButton, adorner.AdornedElement, "external SDK Adorner adorned element");
                    AssertEqual(false, adorner.IsHitTestVisible, "external SDK Adorner hit testing");

                    adornerLayer.Add(adorner);
                    var adorners = adornerLayer.GetAdorners(adornedButton)
                        ?? throw new InvalidOperationException("Expected external SDK AdornerLayer adorners after add.");
                    AssertEqual(1, adorners.Length, "external SDK AdornerLayer adorner count");
                    AssertEqual(adorner, adorners[0], "external SDK AdornerLayer added adorner");

                    adornerLayer.Remove(adorner);
                    var remainingAdorners = adornerLayer.GetAdorners(adornedButton);
                    if (remainingAdorners is { Length: > 0 })
                    {
                        throw new InvalidOperationException(
                            $"Expected external SDK AdornerLayer to remove adorner, but found '{remainingAdorners.Length}'.");
                    }
                }

                private static void ValidateLayoutsAndItems(MainWindow window)
                {
                    var grid = RequireType<Grid>(
                        window.FindName("ExternalLayoutGrid"),
                        "external SDK layout grid");
                    AssertEqual(2, grid.RowDefinitions.Count, "external SDK grid row definition count");
                    AssertEqual(2, grid.ColumnDefinitions.Count, "external SDK grid column definition count");

                    var gridLabel = RequireType<TextBlock>(
                        window.FindName("ExternalGridLabel"),
                        "external SDK grid label");
                    var gridValue = RequireType<TextBlock>(
                        window.FindName("ExternalGridValue"),
                        "external SDK grid value");
                    AssertEqual(0, Grid.GetRow(gridLabel), "external SDK grid label row");
                    AssertEqual(0, Grid.GetColumn(gridLabel), "external SDK grid label column");
                    AssertEqual(1, Grid.GetRow(gridValue), "external SDK grid value row");
                    AssertEqual(1, Grid.GetColumn(gridValue), "external SDK grid value column");
                    AssertEqual(1, Grid.GetColumnSpan(gridValue), "external SDK grid value column span");
                    AssertEqual("Alpha", gridValue.Text, "external SDK grid binding text");

                    var dockPanel = RequireType<DockPanel>(
                        window.FindName("ExternalDockPanel"),
                        "external SDK dock panel");
                    var dockTop = RequireType<TextBlock>(
                        window.FindName("ExternalDockTop"),
                        "external SDK dock top text");
                    var dockFill = RequireType<TextBlock>(
                        window.FindName("ExternalDockFill"),
                        "external SDK dock fill text");
                    AssertEqual(true, dockPanel.LastChildFill, "external SDK dock panel last child fill");
                    AssertEqual(Dock.Top, DockPanel.GetDock(dockTop), "external SDK dock top attached property");
                    AssertEqual("Framework", dockFill.Text, "external SDK dock fill binding text");

                    var canvasChild = RequireType<TextBlock>(
                        window.FindName("ExternalCanvasChild"),
                        "external SDK canvas child");
                    AssertEqual(12.0, Canvas.GetLeft(canvasChild), "external SDK canvas child left");
                    AssertEqual(7.0, Canvas.GetTop(canvasChild), "external SDK canvas child top");

                    var uniformGrid = RequireType<UniformGrid>(
                        window.FindName("ExternalUniformGrid"),
                        "external SDK uniform grid");
                    AssertEqual(1, uniformGrid.Rows, "external SDK uniform grid rows");
                    AssertEqual(3, uniformGrid.Columns, "external SDK uniform grid columns");
                    AssertEqual(3, uniformGrid.Children.Count, "external SDK uniform grid child count");

                    var itemsPanelTemplate = RequireType<ItemsPanelTemplate>(
                        window.FindResource("ExternalItemsPanelTemplate"),
                        "external SDK items panel template");
                    var panelRoot = RequireType<WrapPanel>(
                        itemsPanelTemplate.LoadContent(),
                        "external SDK items panel template root");
                    AssertEqual(Orientation.Horizontal, panelRoot.Orientation, "external SDK items panel orientation");

                    var itemContainerStyle = RequireType<Style>(
                        window.FindResource("ExternalItemContainerStyle"),
                        "external SDK item container style");
                    AssertEqual(typeof(ListBoxItem), itemContainerStyle.TargetType, "external SDK item container style target type");
                    AssertEqual(1, itemContainerStyle.Setters.Count, "external SDK item container style setter count");

                    var itemPanelList = RequireType<ListBox>(
                        window.FindName("ExternalItemsPanelList"),
                        "external SDK item panel list");
                    AssertEqual(itemsPanelTemplate, itemPanelList.ItemsPanel, "external SDK item panel list ItemsPanel");
                    AssertEqual(itemContainerStyle, itemPanelList.ItemContainerStyle, "external SDK item panel list ItemContainerStyle");
                    AssertEqual(4, itemPanelList.AlternationCount, "external SDK item panel list alternation count");
                    AssertEqual("External item {0}", itemPanelList.ItemStringFormat, "external SDK item panel list string format");
                    AssertEqual(3, itemPanelList.Items.Count, "external SDK item panel list collection count after mutation");

                    var groupedItems = RequireType<CollectionViewSource>(
                        window.FindResource("ExternalGroupedItems"),
                        "external SDK grouped CollectionViewSource");
                    AssertEqual(1, groupedItems.SortDescriptions.Count, "external SDK grouped CollectionViewSource sort count");
                    AssertEqual("Name", groupedItems.SortDescriptions[0].PropertyName, "external SDK grouped CollectionViewSource sort property");
                    AssertEqual("Ascending", groupedItems.SortDescriptions[0].Direction.ToString(), "external SDK grouped CollectionViewSource sort direction");
                    AssertEqual(1, groupedItems.GroupDescriptions.Count, "external SDK grouped CollectionViewSource group count");
                    var groupDescription = RequireType<PropertyGroupDescription>(
                        groupedItems.GroupDescriptions[0],
                        "external SDK grouped CollectionViewSource group description");
                    AssertEqual("Kind", groupDescription.PropertyName, "external SDK grouped CollectionViewSource group property");

                    var groupedList = RequireType<ListBox>(
                        window.FindName("ExternalGroupedItemsList"),
                        "external SDK grouped items list");
                    AssertEqual(groupedItems.View, groupedList.ItemsSource, "external SDK grouped ListBox ItemsSource view");
                    AssertEqual(3, groupedList.Items.Count, "external SDK grouped ListBox item count");
                    AssertEqual(1, groupedList.GroupStyle.Count, "external SDK grouped ListBox GroupStyle count");
                    var groupHeaderTemplate = RequireType<DataTemplate>(
                        window.FindResource("ExternalGroupHeaderTemplate"),
                        "external SDK group header template");
                    AssertEqual(groupHeaderTemplate, groupedList.GroupStyle[0].HeaderTemplate, "external SDK grouped ListBox header template");
                    var viewItems = groupedItems.View.Cast<object>().ToArray();
                    AssertEqual(3, viewItems.Length, "external SDK grouped CollectionViewSource view item count");
                    AssertEqual(window.ExternalItems[0], viewItems[0], "external SDK grouped CollectionViewSource first sorted item");
                    AssertEqual(window.ExternalItems[1], viewItems[1], "external SDK grouped CollectionViewSource second sorted item");
                    AssertEqual(window.ExternalItems[2], viewItems[2], "external SDK grouped CollectionViewSource third sorted item");
                    var viewGroups = groupedItems.View.Groups
                        ?? throw new InvalidOperationException("Expected external SDK grouped CollectionViewSource groups.");
                    AssertEqual(3, viewGroups.Count, "external SDK grouped CollectionViewSource view group count");
                    AssertEqual(true, ContainsGroup(viewGroups, "Framework"), "external SDK grouped CollectionViewSource Framework group");
                    AssertEqual(true, ContainsGroup(viewGroups, "Rendering"), "external SDK grouped CollectionViewSource Rendering group");
                    AssertEqual(true, ContainsGroup(viewGroups, "Data"), "external SDK grouped CollectionViewSource Data group");

                    var firstGroup = RequireType<CollectionViewGroup>(
                        viewGroups[0],
                        "external SDK grouped CollectionViewSource first group");
                    var groupHeaderRoot = RequireType<TextBlock>(
                        groupHeaderTemplate.LoadContent(),
                        "external SDK group header template root");
                    groupHeaderRoot.DataContext = firstGroup;
                    DrainDispatcher();
                    AssertContains("Group: ", groupHeaderRoot.Text, "external SDK group header generated text");

                    var listView = RequireType<ListView>(
                        window.FindName("ExternalListView"),
                        "external SDK list view");
                    AssertEqual(3, listView.Items.Count, "external SDK list view collection count after mutation");
                    AssertEqual(0, listView.SelectedIndex, "external SDK list view selected index");
                    AssertEqual(window.ExternalItems[0], listView.SelectedItem, "external SDK list view selected item");
                    var gridView = RequireType<GridView>(
                        listView.View,
                        "external SDK list view grid view");
                    AssertEqual(2, gridView.Columns.Count, "external SDK list view grid-view column count");
                    var listViewNameColumn = gridView.Columns[0];
                    var listViewKindColumn = gridView.Columns[1];
                    AssertEqual("Name", RequireType<string>(listViewNameColumn.Header, "external SDK list view name column header"), "external SDK list view name column header");
                    AssertEqual("Kind", RequireType<string>(listViewKindColumn.Header, "external SDK list view kind column header"), "external SDK list view kind column header");
                    var listViewNameBinding = RequireType<Binding>(
                        listViewNameColumn.DisplayMemberBinding,
                        "external SDK list view name column binding");
                    var listViewKindBinding = RequireType<Binding>(
                        listViewKindColumn.DisplayMemberBinding,
                        "external SDK list view kind column binding");
                    AssertEqual("Name", listViewNameBinding.Path.Path, "external SDK list view name binding path");
                    AssertEqual("Kind", listViewKindBinding.Path.Path, "external SDK list view kind binding path");

                    var dataGrid = RequireType<DataGrid>(
                        window.FindName("ExternalDataGrid"),
                        "external SDK data grid");
                    AssertEqual(false, dataGrid.AutoGenerateColumns, "external SDK data grid auto-generate flag");
                    AssertEqual(false, dataGrid.CanUserAddRows, "external SDK data grid add-row flag");
                    AssertEqual(3, dataGrid.Items.Count, "external SDK data grid collection count after mutation");
                    AssertEqual(1, dataGrid.SelectedIndex, "external SDK data grid selected index");
                    AssertEqual(window.ExternalItems[1], dataGrid.SelectedItem, "external SDK data grid selected item");
                    AssertEqual(3, dataGrid.Columns.Count, "external SDK data grid column count");
                    var dataGridNameColumn = RequireType<DataGridTextColumn>(
                        dataGrid.Columns[0],
                        "external SDK data grid name column");
                    var dataGridKindColumn = RequireType<DataGridTextColumn>(
                        dataGrid.Columns[1],
                        "external SDK data grid kind column");
                    var dataGridActiveColumn = RequireType<DataGridCheckBoxColumn>(
                        dataGrid.Columns[2],
                        "external SDK data grid active column");
                    AssertEqual("Name", RequireType<string>(dataGridNameColumn.Header, "external SDK data grid name column header"), "external SDK data grid name column header");
                    AssertEqual("Kind", RequireType<string>(dataGridKindColumn.Header, "external SDK data grid kind column header"), "external SDK data grid kind column header");
                    AssertEqual("Active", RequireType<string>(dataGridActiveColumn.Header, "external SDK data grid active column header"), "external SDK data grid active column header");
                    var dataGridNameBinding = RequireType<Binding>(
                        dataGridNameColumn.Binding,
                        "external SDK data grid name binding");
                    var dataGridKindBinding = RequireType<Binding>(
                        dataGridKindColumn.Binding,
                        "external SDK data grid kind binding");
                    var dataGridActiveBinding = RequireType<Binding>(
                        dataGridActiveColumn.Binding,
                        "external SDK data grid active binding");
                    AssertEqual("Name", dataGridNameBinding.Path.Path, "external SDK data grid name binding path");
                    AssertEqual("Kind", dataGridKindBinding.Path.Path, "external SDK data grid kind binding path");
                    AssertEqual("IsActive", dataGridActiveBinding.Path.Path, "external SDK data grid active binding path");
                    dataGrid.SelectedIndex = 2;
                    DrainDispatcher();
                    AssertEqual(window.ExternalItems[2], dataGrid.SelectedItem, "external SDK data grid selected item after change");

                    var nodeTemplate = RequireType<HierarchicalDataTemplate>(
                        window.FindResource("ExternalNodeTemplate"),
                        "external SDK node hierarchical data template");
                    var nodeItemsSource = RequireType<Binding>(
                        nodeTemplate.ItemsSource,
                        "external SDK node template ItemsSource binding");
                    AssertEqual("Children", nodeItemsSource.Path.Path, "external SDK node template ItemsSource path");

                    var treeView = RequireType<TreeView>(
                        window.FindName("ExternalTreeView"),
                        "external SDK tree view");
                    AssertEqual(nodeTemplate, treeView.ItemTemplate, "external SDK tree view item template");
                    AssertEqual(2, treeView.Items.Count, "external SDK tree view root item count");
                    AssertEqual(window.ExternalNodes[0], treeView.Items[0], "external SDK tree view first root item");
                    AssertEqual(1, window.ExternalNodes[0].Children.Count, "external SDK tree node child count");
                    AssertEqual("Child", window.ExternalNodes[0].Children[0].Name, "external SDK tree child node name");

                    var templateRoot = RequireType<StackPanel>(
                        nodeTemplate.LoadContent(),
                        "external SDK node template root");
                    templateRoot.DataContext = window.ExternalNodes[0];
                    DrainDispatcher();
                    AssertEqual(2, templateRoot.Children.Count, "external SDK node template child count");
                    var nodeNameText = RequireType<TextBlock>(
                        templateRoot.Children[0],
                        "external SDK node template name text");
                    var nodeKindText = RequireType<TextBlock>(
                        templateRoot.Children[1],
                        "external SDK node template kind text");
                    AssertEqual("Root", nodeNameText.Text, "external SDK node template name binding");
                    AssertEqual("Framework", nodeKindText.Text, "external SDK node template kind binding");

                    var explicitTree = RequireType<TreeView>(
                        window.FindName("ExternalExplicitTreeView"),
                        "external SDK explicit tree view");
                    var rootItem = RequireType<TreeViewItem>(
                        window.FindName("ExternalTreeRootItem"),
                        "external SDK explicit root tree item");
                    var childItem = RequireType<TreeViewItem>(
                        window.FindName("ExternalTreeChildItem"),
                        "external SDK explicit child tree item");
                    AssertEqual(1, explicitTree.Items.Count, "external SDK explicit tree root count");
                    AssertEqual(rootItem, explicitTree.Items[0], "external SDK explicit tree root item");
                    AssertEqual("External root", rootItem.Header, "external SDK explicit root header");
                    AssertEqual("External child", childItem.Header, "external SDK explicit child header");
                    AssertEqual(1, rootItem.Items.Count, "external SDK explicit root child count");

                    int expandedBefore = window.ExternalTreeExpandedCount;
                    rootItem.IsExpanded = true;
                    DrainDispatcher();
                    AssertEqual(true, rootItem.IsExpanded, "external SDK explicit root expanded state");
                    AssertAtLeast(expandedBefore + 1, window.ExternalTreeExpandedCount, "external SDK tree expanded event count");
                    AssertEqual("ExternalTreeRootItem", window.LastExternalTreeExpandedOriginalSourceName, "external SDK tree expanded original source");

                    int selectedBefore = window.ExternalTreeSelectedCount;
                    childItem.IsSelected = true;
                    DrainDispatcher();
                    AssertEqual(true, childItem.IsSelected, "external SDK explicit child selected state");
                    AssertEqual(childItem, explicitTree.SelectedItem, "external SDK explicit tree selected item");
                    AssertAtLeast(selectedBefore + 1, window.ExternalTreeSelectedCount, "external SDK tree selected event count");
                    AssertEqual("ExternalTreeChildItem", window.LastExternalTreeSelectedOriginalSourceName, "external SDK tree selected original source");

                    int unselectedBefore = window.ExternalTreeUnselectedCount;
                    rootItem.IsSelected = true;
                    DrainDispatcher();
                    AssertEqual(false, childItem.IsSelected, "external SDK explicit child unselected state");
                    AssertEqual(true, rootItem.IsSelected, "external SDK explicit root selected state");
                    AssertEqual(rootItem, explicitTree.SelectedItem, "external SDK explicit tree selected root item");
                    AssertAtLeast(unselectedBefore + 1, window.ExternalTreeUnselectedCount, "external SDK tree unselected event count");
                    AssertEqual("ExternalTreeChildItem", window.LastExternalTreeUnselectedOriginalSourceName, "external SDK tree unselected original source");

                    int collapsedBefore = window.ExternalTreeCollapsedCount;
                    rootItem.IsExpanded = false;
                    DrainDispatcher();
                    AssertEqual(false, rootItem.IsExpanded, "external SDK explicit root collapsed state");
                    AssertAtLeast(collapsedBefore + 1, window.ExternalTreeCollapsedCount, "external SDK tree collapsed event count");
                    AssertEqual("ExternalTreeRootItem", window.LastExternalTreeCollapsedOriginalSourceName, "external SDK tree collapsed original source");
                }

                private static void ValidateSelectorsAndContent(MainWindow window)
                {
                    var comboBox = RequireType<ComboBox>(
                        window.FindName("ExternalComboBox"),
                        "external SDK combo box");
                    DrainDispatcher();
                    AssertEqual(3, comboBox.Items.Count, "external SDK combo box item count after mutation");
                    AssertEqual("Kind", comboBox.SelectedValuePath, "external SDK combo box selected value path");
                    AssertEqual("Rendering", comboBox.SelectedValue, "external SDK combo box selected value");
                    AssertEqual(1, comboBox.SelectedIndex, "external SDK combo box selected index");
                    AssertEqual(window.ExternalItems[1], comboBox.SelectedItem, "external SDK combo box selected item");
                    var selectedValueBinding = comboBox.GetBindingExpression(Selector.SelectedValueProperty)
                        ?? throw new InvalidOperationException("Expected external SDK ComboBox SelectedValue BindingExpression.");
                    AssertEqual("SelectedExternalKind", selectedValueBinding.ParentBinding.Path.Path, "external SDK combo box selected value binding path");

                    int comboSelectionBefore = window.ExternalSelectionChangedCount;
                    comboBox.SelectedIndex = 0;
                    DrainDispatcher();
                    AssertEqual("Framework", comboBox.SelectedValue, "external SDK combo box selected value after change");
                    AssertEqual("Framework", window.SelectedExternalKind, "external SDK combo box two-way selected value source update");
                    AssertAtLeast(comboSelectionBefore + 1, window.ExternalSelectionChangedCount, "external SDK combo box selection changed count");
                    AssertEqual("ExternalComboBox", window.LastExternalSelectionSourceName, "external SDK combo box selection source name");

                    var tabControl = RequireType<TabControl>(
                        window.FindName("ExternalTabControl"),
                        "external SDK tab control");
                    var frameworkTab = RequireType<TabItem>(
                        window.FindName("ExternalFrameworkTab"),
                        "external SDK framework tab item");
                    var renderingTab = RequireType<TabItem>(
                        window.FindName("ExternalRenderingTab"),
                        "external SDK rendering tab item");
                    AssertEqual(2, tabControl.Items.Count, "external SDK tab item count");
                    AssertEqual(1, tabControl.SelectedIndex, "external SDK tab selected index");
                    AssertEqual(renderingTab, tabControl.SelectedItem, "external SDK selected tab item");
                    AssertEqual("Framework", frameworkTab.Header, "external SDK framework tab header");
                    AssertEqual("Rendering", renderingTab.Header, "external SDK rendering tab header");
                    var renderingTabText = RequireType<TextBlock>(
                        window.FindName("ExternalRenderingTabText"),
                        "external SDK rendering tab text");
                    AssertEqual("Framework", renderingTabText.Text, "external SDK rendering tab content binding");

                    int tabSelectionBefore = window.ExternalSelectionChangedCount;
                    tabControl.SelectedIndex = 0;
                    DrainDispatcher();
                    AssertEqual(frameworkTab, tabControl.SelectedItem, "external SDK tab item after selected index change");
                    AssertAtLeast(tabSelectionBefore + 1, window.ExternalSelectionChangedCount, "external SDK tab selection changed count");
                    AssertEqual("ExternalTabControl", window.LastExternalSelectionSourceName, "external SDK tab selection source name");

                    var groupBox = RequireType<GroupBox>(
                        window.FindName("ExternalGroupBox"),
                        "external SDK group box");
                    var groupText = RequireType<TextBlock>(
                        groupBox.Content,
                        "external SDK group box content");
                    AssertEqual("External group", groupBox.Header, "external SDK group box header");
                    AssertEqual("Alpha", groupText.Text, "external SDK group box content binding");

                    var expander = RequireType<Expander>(
                        window.FindName("ExternalExpander"),
                        "external SDK expander");
                    var expanderText = RequireType<TextBlock>(
                        expander.Content,
                        "external SDK expander content");
                    AssertEqual("External expander", expander.Header, "external SDK expander header");
                    AssertEqual(false, expander.IsExpanded, "external SDK expander initial expanded state");
                    AssertEqual("External expanded content", expanderText.Text, "external SDK expander content text");

                    int expandedBefore = window.ExternalExpanderExpandedCount;
                    int collapsedBefore = window.ExternalExpanderCollapsedCount;
                    expander.IsExpanded = true;
                    DrainDispatcher();
                    AssertEqual(true, expander.IsExpanded, "external SDK expander expanded state");
                    AssertAtLeast(expandedBefore + 1, window.ExternalExpanderExpandedCount, "external SDK expander expanded event count");
                    expander.IsExpanded = false;
                    DrainDispatcher();
                    AssertEqual(false, expander.IsExpanded, "external SDK expander collapsed state");
                    AssertAtLeast(collapsedBefore + 1, window.ExternalExpanderCollapsedCount, "external SDK expander collapsed event count");

                    var scrollViewer = RequireType<ScrollViewer>(
                        window.FindName("ExternalScrollViewer"),
                        "external SDK scroll viewer");
                    var scrollContent = RequireType<StackPanel>(
                        window.FindName("ExternalScrollContent"),
                        "external SDK scroll content panel");
                    AssertEqual(ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility, "external SDK scroll viewer vertical visibility");
                    AssertEqual(ScrollBarVisibility.Disabled, scrollViewer.HorizontalScrollBarVisibility, "external SDK scroll viewer horizontal visibility");
                    AssertEqual(scrollContent, scrollViewer.Content, "external SDK scroll viewer content");
                    AssertEqual(2, scrollContent.Children.Count, "external SDK scroll content child count");
                }

                private static void ValidateRichDocuments(MainWindow window)
                {
                    var richTextBox = RequireType<RichTextBox>(
                        window.FindName("ExternalRichTextBox"),
                        "external SDK rich text box");
                    var document = richTextBox.Document;
                    AssertEqual(5, document.Blocks.Count, "external SDK FlowDocument block count");

                    var introParagraph = RequireType<Paragraph>(
                        document.Blocks.FirstBlock,
                        "external SDK FlowDocument intro paragraph");
                    var inlines = introParagraph.Inlines;
                    var bold = RequireFirstInline<Bold>(inlines, "external SDK FlowDocument bold inline");
                    AssertEqual("rich", RequireFirstInline<Run>(bold.Inlines, "external SDK FlowDocument bold run").Text, "external SDK FlowDocument bold run text");
                    var italic = RequireFirstInline<Italic>(inlines, "external SDK FlowDocument italic inline");
                    AssertEqual(" italic", RequireFirstInline<Run>(italic.Inlines, "external SDK FlowDocument italic run").Text, "external SDK FlowDocument italic run text");
                    var underline = RequireFirstInline<Underline>(inlines, "external SDK FlowDocument underline inline");
                    AssertEqual(" underline", RequireFirstInline<Run>(underline.Inlines, "external SDK FlowDocument underline run").Text, "external SDK FlowDocument underline run text");
                    var span = RequireFirstInlineExact<Span>(inlines, "external SDK FlowDocument span inline");
                    AssertEqual(" span", RequireFirstInline<Run>(span.Inlines, "external SDK FlowDocument span run").Text, "external SDK FlowDocument span run text");
                    RequireFirstInline<LineBreak>(inlines, "external SDK FlowDocument line break inline");

                    var hyperlink = RequireFirstInline<Hyperlink>(inlines, "external SDK FlowDocument hyperlink");
                    AssertEqual("ExternalDocumentLink", hyperlink.Name, "external SDK FlowDocument hyperlink name");
                    AssertEqual("https://example.test/external-sdk", hyperlink.NavigateUri?.ToString(), "external SDK FlowDocument hyperlink URI");
                    AssertEqual("link", RequireFirstInline<Run>(hyperlink.Inlines, "external SDK FlowDocument hyperlink run").Text, "external SDK FlowDocument hyperlink run text");
                    AssertEqual(0, window.ExternalDocumentLinkRequestNavigateCount, "external SDK Hyperlink initial RequestNavigate count");
                    hyperlink.DoClick();
                    AssertEqual(1, window.ExternalDocumentLinkRequestNavigateCount, "external SDK Hyperlink RequestNavigate handler count");
                    AssertEqual("ExternalDocumentLink", window.LastExternalDocumentLinkRequestNavigateSenderName, "external SDK Hyperlink RequestNavigate sender");
                    AssertEqual("https://example.test/external-sdk", window.LastExternalDocumentLinkRequestNavigateUri, "external SDK Hyperlink RequestNavigate URI");
                    AssertEqual("RequestNavigate", window.LastExternalDocumentLinkRequestNavigateRoutedEventName, "external SDK Hyperlink RequestNavigate routed event");

                    var inlineContainer = RequireFirstInline<InlineUIContainer>(inlines, "external SDK FlowDocument inline UI container");
                    var inlineButton = RequireType<Button>(inlineContainer.Child, "external SDK FlowDocument inline button");
                    AssertEqual("external inline button", inlineButton.Content, "external SDK FlowDocument inline button content");

                    var documentList = RequireType<System.Windows.Documents.List>(
                        introParagraph.NextBlock,
                        "external SDK FlowDocument list");
                    AssertEqual(TextMarkerStyle.Decimal, documentList.MarkerStyle, "external SDK FlowDocument list marker style");
                    AssertEqual(2, documentList.ListItems.Count, "external SDK FlowDocument list item count");
                    AssertListItemText(documentList.ListItems.FirstListItem, "External list one", "first");
                    AssertListItemText(documentList.ListItems.FirstListItem.NextListItem, "External list two", "second");

                    var section = RequireType<Section>(
                        documentList.NextBlock,
                        "external SDK FlowDocument section");
                    AssertEqual(1, section.Blocks.Count, "external SDK FlowDocument section block count");
                    AssertParagraphText(
                        RequireType<Paragraph>(section.Blocks.FirstBlock, "external SDK FlowDocument section paragraph"),
                        "External section",
                        "section");

                    var blockContainer = RequireType<BlockUIContainer>(
                        section.NextBlock,
                        "external SDK FlowDocument block UI container");
                    var blockButton = RequireType<Button>(blockContainer.Child, "external SDK FlowDocument block button");
                    AssertEqual("external block button", blockButton.Content, "external SDK FlowDocument block button content");

                    var table = RequireType<Table>(
                        blockContainer.NextBlock,
                        "external SDK FlowDocument table");
                    AssertEqual(2, table.Columns.Count, "external SDK FlowDocument table column count");
                    AssertEqual(1, table.RowGroups.Count, "external SDK FlowDocument table row group count");
                    var rowGroup = table.RowGroups[0];
                    AssertEqual(1, rowGroup.Rows.Count, "external SDK FlowDocument table row count");
                    var row = rowGroup.Rows[0];
                    AssertEqual(2, row.Cells.Count, "external SDK FlowDocument table cell count");
                    AssertTableCellText(row.Cells[0], "External cell alpha", "first");
                    AssertTableCellText(row.Cells[1], "External cell beta", "second");

                    richTextBox.Selection.Select(document.ContentStart, document.ContentEnd);
                    AssertContains("External section", richTextBox.Selection.Text, "external SDK RichTextBox selection text");
                    var documentText = new TextRange(document.ContentStart, document.ContentEnd).Text;
                    AssertContains("External cell beta", documentText, "external SDK FlowDocument TextRange table text");
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
                    var validationTextBox = RequireType<TextBox>(
                        window.FindName("ExternalValidationTextBox"),
                        "external SDK access-key target text box");
                    var accessLabel = RequireType<Label>(
                        window.FindName("ExternalAccessLabel"),
                        "external SDK access label");
                    var standaloneAccessText = RequireType<AccessText>(
                        window.FindName("ExternalStandaloneAccessText"),
                        "external SDK standalone access text");
                    AssertEqual(commandButton, FocusManager.GetFocusedElement(focusPanel), "external SDK focus manager focused element");
                    AssertEqual(true, FocusManager.GetIsFocusScope(focusPanel), "external SDK focus manager scope flag");
                    AssertEqual(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(focusPanel), "external SDK tab navigation mode");
                    AssertEqual(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetControlTabNavigation(focusPanel), "external SDK control-tab navigation mode");
                    AssertEqual(KeyboardNavigationMode.Contained, KeyboardNavigation.GetDirectionalNavigation(focusPanel), "external SDK directional navigation mode");
                    AssertEqual(validationTextBox, accessLabel.Target, "external SDK label access-key target");
                    AssertEqual("_External access target", accessLabel.Content, "external SDK label access-key content");
                    AssertEqual("_External standalone access", standaloneAccessText.Text, "external SDK standalone access text");
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

                private static T RequireFirstInline<T>(InlineCollection inlines, string description)
                    where T : Inline
                {
                    foreach (Inline inline in inlines)
                    {
                        if (inline is T typed)
                        {
                            return typed;
                        }
                    }

                    throw new InvalidOperationException(
                        $"Expected {description} to contain {typeof(T).FullName}.");
                }

                private static T RequireFirstInlineExact<T>(InlineCollection inlines, string description)
                    where T : Inline
                {
                    foreach (Inline inline in inlines)
                    {
                        if (inline.GetType() == typeof(T))
                        {
                            return (T)inline;
                        }
                    }

                    throw new InvalidOperationException(
                        $"Expected {description} to contain exact {typeof(T).FullName}.");
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

                private static bool ContainsGroup(System.Collections.IEnumerable groups, string name)
                {
                    foreach (object group in groups)
                    {
                        if (group is CollectionViewGroup collectionViewGroup
                            && string.Equals(collectionViewGroup.Name?.ToString(), name, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                private static void AssertBrushColor(Brush brush, string expected, string description)
                {
                    var solidColorBrush = RequireType<SolidColorBrush>(brush, description);
                    AssertEqual(expected, solidColorBrush.Color.ToString(), description);
                }

                private static void AssertParagraphText(Paragraph paragraph, string expectedText, string description)
                {
                    var run = RequireFirstInline<Run>(
                        paragraph.Inlines,
                        $"external SDK FlowDocument {description} run");
                    AssertEqual(expectedText, run.Text, $"external SDK FlowDocument {description} text");
                }

                private static void AssertListItemText(ListItem? listItem, string expectedText, string description)
                {
                    var item = RequireType<ListItem>(
                        listItem,
                        $"external SDK FlowDocument {description} list item");
                    var paragraph = RequireType<Paragraph>(
                        item.Blocks.FirstBlock,
                        $"external SDK FlowDocument {description} list item paragraph");
                    AssertParagraphText(paragraph, expectedText, $"{description} list item");
                }

                private static void AssertTableCellText(TableCell tableCell, string expectedText, string description)
                {
                    var paragraph = RequireType<Paragraph>(
                        tableCell.Blocks.FirstBlock,
                        $"external SDK FlowDocument {description} table cell paragraph");
                    AssertParagraphText(paragraph, expectedText, $"{description} table cell");
                }

                private static Setter AssertLooseStyleSetter(
                    SetterBase setterBase,
                    DependencyProperty expectedProperty,
                    object expectedValue,
                    string description)
                {
                    var setter = RequireType<Setter>(
                        setterBase,
                        description);
                    AssertEqual(expectedProperty, setter.Property, $"{description} property");
                    AssertEqual(expectedValue, setter.Value, $"{description} value");
                    return setter;
                }

                private static void AssertEqual<T>(T expected, T actual, string description)
                {
                    if (!EqualityComparer<T>.Default.Equals(expected, actual))
                    {
                        throw new InvalidOperationException(
                            $"Expected {description} to be '{expected}', but found '{actual}'.");
                    }
                }

                private static void AssertClose(double expected, double actual, string description)
                {
                    if (Math.Abs(expected - actual) > 0.000001)
                    {
                        throw new InvalidOperationException(
                            $"Expected {description} to be close to '{expected}', but found '{actual}'.");
                    }
                }

                private static void AssertContains(string expectedSubstring, string actual, string description)
                {
                    if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Expected {description} to contain '{expectedSubstring}', but found '{actual}'.");
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

        ValidateProGpuHiDpiRenderSurface(outputRoot);
    }

    private static void ValidateProGpuHiDpiRenderSurface(string outputRoot)
    {
        var loadContext = new AssemblyLoadContext("ProGPU WPF external SDK output validation", isCollectible: true);
        loadContext.Resolving += (_, assemblyName) =>
        {
            string? assemblyNameText = assemblyName.Name;
            if (string.IsNullOrEmpty(assemblyNameText))
            {
                return null;
            }

            string candidate = Path.Combine(outputRoot, assemblyNameText + ".dll");
            return File.Exists(candidate) ? loadContext.LoadFromAssemblyPath(candidate) : null;
        };

        try
        {
            Assembly proGpuWpf = loadContext.LoadFromAssemblyPath(Path.Combine(outputRoot, "ProGPU.Wpf.dll"));
            Assembly proGpuScene = loadContext.LoadFromAssemblyPath(Path.Combine(outputRoot, "ProGPU.Scene.dll"));

            Type windowHostType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfWindowHost");
            AssertPropertyType(windowHostType, "Width", typeof(int), "external SDK ProGPU WPF host logical width property");
            AssertPropertyType(windowHostType, "Height", typeof(int), "external SDK ProGPU WPF host logical height property");

            MethodInfo setClientSize = windowHostType.GetMethod(
                "SetClientSize",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                [typeof(int), typeof(int)],
                modifiers: null)
                ?? throw new MissingMethodException(windowHostType.FullName, "SetClientSize");
            AssertEqual(2, setClientSize.GetParameters().Length, "external SDK ProGPU WPF host client-size method parameter count");

            Type compositionTargetType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfCompositionTarget");
            MethodInfo compositionRender = FindMethodByParameterNames(
                compositionTargetType,
                "Render",
                ["logicalWidth", "logicalHeight", "pixelWidth", "pixelHeight", "dpiScale", "targetView"]);
            AssertParameterTypes(
                compositionRender,
                [typeof(uint), typeof(uint), typeof(uint), typeof(uint), typeof(float)],
                "external SDK ProGPU WPF composition render logical/physical surface");
            AssertEqual(true, compositionRender.GetParameters()[5].ParameterType.IsPointer, "external SDK ProGPU WPF composition render target view pointer");

            Type compositorType = GetRequiredType(proGpuScene, "ProGPU.Scene.Compositor");
            Type visualType = GetRequiredType(proGpuScene, "ProGPU.Scene.Visual");
            MethodInfo compositorRenderScene = FindMethodByParameterNames(
                compositorType,
                "RenderScene",
                ["root", "logicalWidth", "logicalHeight", "renderTargetWidth", "renderTargetHeight", "dpiScale", "targetView"]);
            AssertParameterTypes(
                compositorRenderScene,
                [visualType, typeof(uint), typeof(uint), typeof(uint), typeof(uint), typeof(float)],
                "external SDK ProGPU compositor render logical/physical surface");
            AssertEqual(true, compositorRenderScene.GetParameters()[6].ParameterType.IsPointer, "external SDK ProGPU compositor render target view pointer");
            AssertPropertyGetterReferencesField(
                compositorType,
                "CurrentCanvasPixelWidth",
                "_explicitRenderTargetWidth",
                "external SDK ProGPU compositor canvas pixel width explicit render target");
            AssertPropertyGetterReferencesField(
                compositorType,
                "CurrentCanvasPixelHeight",
                "_explicitRenderTargetHeight",
                "external SDK ProGPU compositor canvas pixel height explicit render target");
        }
        finally
        {
            loadContext.Unload();
        }
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

    private static string ReadPackageEntry(ZipArchive package, string entryName, string description)
    {
        using Stream stream = RequirePackageEntry(package, entryName, description).Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static ZipArchiveEntry RequirePackageEntry(ZipArchive package, string entryName, string description)
    {
        ZipArchiveEntry? entry = package.GetEntry(entryName);
        if (entry is null)
        {
            throw new FileNotFoundException($"Missing {description} package entry: {entryName}", entryName);
        }

        return entry;
    }

    private static void AssertNoPackageEntryPrefix(ZipArchive package, string prefix, string description)
    {
        if (package.Entries.Any(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Expected {description} to be absent.");
        }
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

    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)!
            ?? throw new TypeLoadException(typeName);
    }

    private static void AssertEqual(object expected, object actual, string description)
    {
        if (!object.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to be '{expected}', but found '{actual}'.");
        }
    }

    private static void AssertPropertyType(Type type, string propertyName, Type expectedType, string description)
    {
        PropertyInfo property = type.GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(type.FullName, propertyName);

        if (property.PropertyType != expectedType)
        {
            throw new InvalidOperationException(
                $"Expected {description} type to be '{expectedType.FullName}', but found '{property.PropertyType.FullName}'.");
        }
    }

    private static MethodInfo FindMethodByParameterNames(Type type, string methodName, string[] parameterNames)
    {
        MethodInfo? method = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .FirstOrDefault(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == parameterNames.Length &&
                    parameters
                        .Select(parameter => parameter.Name ?? string.Empty)
                        .SequenceEqual(parameterNames, StringComparer.Ordinal);
            });

        return method ?? throw new MissingMethodException(
            type.FullName,
            $"{methodName}({string.Join(", ", parameterNames)})");
    }

    private static void AssertParameterTypes(MethodInfo method, Type[] expectedParameterTypes, string description)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length < expectedParameterTypes.Length)
        {
            throw new InvalidOperationException(
                $"Expected {description} to have at least {expectedParameterTypes.Length} parameters, but found {parameters.Length}.");
        }

        for (int i = 0; i < expectedParameterTypes.Length; i++)
        {
            if (parameters[i].ParameterType != expectedParameterTypes[i])
            {
                throw new InvalidOperationException(
                    $"Expected {description} parameter '{parameters[i].Name}' type to be '{expectedParameterTypes[i].FullName}', but found '{parameters[i].ParameterType.FullName}'.");
            }
        }
    }

    private static void AssertPropertyGetterReferencesField(
        Type type,
        string propertyName,
        string fieldName,
        string description)
    {
        PropertyInfo property = type.GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(type.FullName, propertyName);
        MethodInfo getter = property.GetMethod
            ?? throw new MissingMethodException(type.FullName, propertyName + ".get");
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, fieldName);
        byte[] il = getter.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"Expected {description} getter IL.");
        byte[] fieldToken = BitConverter.GetBytes(field.MetadataToken);

        for (int i = 0; i <= il.Length - fieldToken.Length; i++)
        {
            if (il.AsSpan(i, fieldToken.Length).SequenceEqual(fieldToken))
            {
                return;
            }
        }

        throw new InvalidOperationException($"Expected {description} getter to reference '{fieldName}'.");
    }
}
