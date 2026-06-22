using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
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
        "System.Private.Windows.Core",
        "System.Windows.Extensions",
        "StbImageSharp"
    ];

    private static readonly PackageAssemblyExpectation[] s_packageAssemblyExpectations =
    [
        new("Microsoft.DotNet.Wpf.GitHub", "WindowsBase", "net11.0", "WPF"),
        new("Microsoft.DotNet.Wpf.GitHub", "System.Xaml", "net11.0", "MicrosoftBcl"),
        new("Microsoft.DotNet.Wpf.GitHub", "PresentationCore", "net11.0", "WPF"),
        new("Microsoft.DotNet.Wpf.GitHub", "PresentationFramework", "net11.0", "WPF"),
        new("Microsoft.DotNet.Wpf.GitHub", "PresentationUI", "net11.0", "WPF"),
        new("Microsoft.DotNet.Wpf.GitHub", "ReachFramework", "net11.0", "WPF"),
        new("Microsoft.DotNet.Wpf.GitHub", "UIAutomationTypes", "net11.0", "WPF"),
        new("Microsoft.DotNet.Wpf.GitHub", "UIAutomationProvider", "net11.0", "WPF"),
        new("Microsoft.DotNet.Wpf.GitHub", "System.Windows.Input.Manipulations", "net11.0", "MicrosoftBcl"),
        new("Microsoft.DotNet.Wpf.GitHub", "System.Windows.Primitives", "net11.0", "WPF"),
        new("Microsoft.DotNet.Wpf.GitHub", "PresentationFramework.Aero2", "net11.0", "WPF"),
        new("Microsoft.DotNet.Wpf.GitHub", "PresentationFramework.Fluent", "net11.0", "WPF"),
        new("ProGPU.Wpf", "ProGPU.Wpf", "net10.0", "ProGPU"),
        new("ProGPU.Backend", "ProGPU.Backend", "net10.0", "ProGPU"),
        new("ProGPU.Scene", "ProGPU.Scene", "net10.0", "ProGPU"),
        new("ProGPU.Vector", "ProGPU.Vector", "net10.0", "ProGPU"),
        new("ProGPU.Text", "ProGPU.Text", "net10.0", "ProGPU"),
        new("ProGPU.Compute", "ProGPU.Compute", "net10.0", "ProGPU"),
        new("ProGPU.Transpiler", "ProGPU.Transpiler", "net10.0", "ProGPU")
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
        AssertContains(sdkProps, "<ProGpuWpfStbImageSharpVersion Condition=\"'$(ProGpuWpfStbImageSharpVersion)' == ''\">2.30.15</ProGpuWpfStbImageSharpVersion>", "SDK StbImageSharp version default");
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
        AssertContains(portableProps, "<PackageReference Include=\"StbImageSharp\" Version=\"$(ProGpuWpfStbImageSharpVersion)\" />", "SDK StbImageSharp package reference");

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

        ValidatePackageAssemblyIdentities(packageFeed);
    }

    private static void ValidatePackageAssemblyIdentities(string packageFeed)
    {
        var expectedAssemblyVersion = new Version(11, 0, 0, 0);
        var publicKeyTokensByGroup = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (PackageAssemblyExpectation expectation in s_packageAssemblyExpectations)
        {
            string packagePath = Path.Combine(packageFeed, $"{expectation.PackageId}.{SdkVersion}.nupkg");
            string description = $"{expectation.PackageId}/{expectation.AssemblySimpleName}";
            RequireFile(packagePath, $"{description} package");

            using ZipArchive package = ZipFile.OpenRead(packagePath);
            string nuspec = ReadPackageEntry(package, $"{expectation.PackageId}.nuspec", $"{description} nuspec");
            AssertContains(nuspec, $"<version>{SdkVersion}</version>", $"{description} package version");

            string assemblyEntryName = $"lib/{expectation.TargetFramework}/{expectation.AssemblySimpleName}.dll";
            ZipArchiveEntry assemblyEntry = RequirePackageEntry(package, assemblyEntryName, $"{description} runtime assembly");
            AssemblyName identity = ReadPackageAssemblyName(assemblyEntry, $"{description} runtime assembly");

            AssertEqual(expectation.AssemblySimpleName, identity.Name ?? string.Empty, $"{description} assembly name");
            AssertEqual(expectedAssemblyVersion, identity.Version ?? new Version(0, 0, 0, 0), $"{description} assembly version");

            string publicKeyToken = GetPublicKeyToken(identity);
            if (expectation.PublicKeyTokenGroup.Length == 0)
            {
                continue;
            }

            if (publicKeyToken.Length == 0)
            {
                throw new InvalidOperationException($"Expected {description} assembly to have a public key token.");
            }

            if (publicKeyTokensByGroup.TryGetValue(expectation.PublicKeyTokenGroup, out string? expectedPublicKeyToken))
            {
                AssertEqual(expectedPublicKeyToken, publicKeyToken, $"{description} {expectation.PublicKeyTokenGroup} public key token");
            }
            else
            {
                publicKeyTokensByGroup.Add(expectation.PublicKeyTokenGroup, publicKeyToken);
            }
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
                <Resource Include="Assets/ExternalResource.txt" />
                <Resource Include="Assets/ExternalImage.png" />
              </ItemGroup>
            </Project>
            """);

        WriteFile(
            Path.Combine(appRoot, "Assets", "ExternalResource.txt"),
            "External SDK pack resource text");
        File.WriteAllBytes(
            Path.Combine(appRoot, "Assets", "ExternalImage.png"),
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAE0lEQVR4nGP4z8DwHwwZGP6DAQBJyAn3FGMynQAAAABJRU5ErkJggg=="));

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
                    x:Key="{ComponentResourceKey TypeInTargetAssembly={x:Type local:MainWindow}, ResourceId=ExternalComponentAccentBrush}"
                    Color="#4E7A9D" />
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
                    <ControlTemplate.Triggers>
                        <Trigger Property="Tag" Value="template-trigger-active">
                            <Trigger.EnterActions>
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation
                                            Storyboard.TargetName="ExternalTemplateContent"
                                            Storyboard.TargetProperty="MinWidth"
                                            To="23"
                                            Duration="0:0:0" />
                                    </Storyboard>
                                </BeginStoryboard>
                            </Trigger.EnterActions>
                            <Trigger.ExitActions>
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation
                                            Storyboard.TargetName="ExternalTemplateContent"
                                            Storyboard.TargetProperty="MinWidth"
                                            To="0"
                                            Duration="0:0:0" />
                                    </Storyboard>
                                </BeginStoryboard>
                            </Trigger.ExitActions>
                        </Trigger>
                    </ControlTemplate.Triggers>
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
                xmlns:sys="clr-namespace:System;assembly=System.Runtime"
                Title="External SDK App"
                Width="320"
                Height="200"
                Closing="OnExternalWindowClosing"
                Closed="OnExternalWindowClosed"
                AllowDrop="True"
                PreviewDragEnter="OnExternalPreviewDragEnter"
                DragEnter="OnExternalDragEnter"
                PreviewDragOver="OnExternalPreviewDragOver"
                DragOver="OnExternalDragOver"
                PreviewDragLeave="OnExternalPreviewDragLeave"
                DragLeave="OnExternalDragLeave"
                PreviewDrop="OnExternalPreviewDrop"
                Drop="OnExternalDrop">
                <Window.Resources>
                    <DataTemplate x:Key="ExternalGroupHeaderTemplate">
                        <TextBlock
                            x:Name="ExternalGroupHeaderText"
                            Text="{Binding Name, StringFormat=Group: {0}}" />
                    </DataTemplate>
                    <DataTemplate
                        x:Key="ExternalFrameworkItemTemplate"
                        DataType="{x:Type local:ExternalItem}">
                        <TextBlock
                            x:Name="ExternalFrameworkTemplateText"
                            Text="{Binding Name, StringFormat=Framework template {0}}" />
                    </DataTemplate>
                    <DataTemplate
                        x:Key="ExternalRenderingItemTemplate"
                        DataType="{x:Type local:ExternalItem}">
                        <TextBlock
                            x:Name="ExternalRenderingTemplateText"
                            Text="{Binding Name, StringFormat=Rendering template {0}}" />
                    </DataTemplate>
                    <DataTemplate
                        x:Key="ExternalDefaultItemTemplate"
                        DataType="{x:Type local:ExternalItem}">
                        <TextBlock
                            x:Name="ExternalDefaultTemplateText"
                            Text="{Binding Kind, StringFormat=Default template {0}}" />
                    </DataTemplate>
                    <local:ExternalItemTemplateSelector
                        x:Key="ExternalItemTemplateSelector"
                        DefaultTemplate="{StaticResource ExternalDefaultItemTemplate}"
                        FrameworkTemplate="{StaticResource ExternalFrameworkItemTemplate}"
                        RenderingTemplate="{StaticResource ExternalRenderingItemTemplate}" />
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
                    <CollectionViewSource
                        x:Key="ExternalFilteredItems"
                        Source="{Binding ExternalItems}"
                        Filter="OnExternalItemsFilter" />
                    <CollectionViewSource
                        x:Key="ExternalLiveFilteredItems"
                        Source="{Binding ExternalLiveItems}"
                        Filter="OnExternalItemsFilter"
                        IsLiveFilteringRequested="True">
                        <CollectionViewSource.LiveFilteringProperties>
                            <sys:String>IsActive</sys:String>
                        </CollectionViewSource.LiveFilteringProperties>
                    </CollectionViewSource>
                    <CollectionViewSource
                        x:Key="ExternalLiveSortedItems"
                        Source="{Binding ExternalLiveItems}"
                        IsLiveSortingRequested="True">
                        <CollectionViewSource.SortDescriptions>
                            <componentModel:SortDescription
                                PropertyName="Name"
                                Direction="Ascending" />
                        </CollectionViewSource.SortDescriptions>
                        <CollectionViewSource.LiveSortingProperties>
                            <sys:String>Name</sys:String>
                        </CollectionViewSource.LiveSortingProperties>
                    </CollectionViewSource>
                    <CollectionViewSource
                        x:Key="ExternalLiveGroupedItems"
                        Source="{Binding ExternalLiveItems}"
                        IsLiveGroupingRequested="True">
                        <CollectionViewSource.GroupDescriptions>
                            <PropertyGroupDescription PropertyName="Kind" />
                        </CollectionViewSource.GroupDescriptions>
                        <CollectionViewSource.LiveGroupingProperties>
                            <sys:String>Kind</sys:String>
                        </CollectionViewSource.LiveGroupingProperties>
                    </CollectionViewSource>
                    <CollectionViewSource
                        x:Key="ExternalCurrencyItems"
                        Source="{Binding ExternalItems}" />
                    <Style
                        x:Key="ExternalEventSetterButtonStyle"
                        TargetType="{x:Type Button}">
                        <Setter Property="Content" Value="External event setter button" />
                        <Setter Property="Tag" Value="event-setter-style" />
                        <EventSetter
                            Event="Click"
                            Handler="OnExternalStyleEventButtonClick" />
                    </Style>
                    <Style
                        x:Key="ExternalPropertyTriggerActionTextStyle"
                        TargetType="{x:Type TextBlock}">
                        <Setter Property="Text" Value="External property trigger action target" />
                        <Setter Property="Opacity" Value="0.91" />
                        <Setter Property="IsEnabled" Value="False" />
                        <Style.Triggers>
                            <Trigger Property="IsEnabled" Value="True">
                                <Trigger.EnterActions>
                                    <BeginStoryboard>
                                        <Storyboard>
                                            <DoubleAnimation
                                                Storyboard.TargetProperty="Opacity"
                                                To="0.43"
                                                Duration="0:0:0" />
                                        </Storyboard>
                                    </BeginStoryboard>
                                </Trigger.EnterActions>
                                <Trigger.ExitActions>
                                    <BeginStoryboard>
                                        <Storyboard>
                                            <DoubleAnimation
                                                Storyboard.TargetProperty="Opacity"
                                                To="0.91"
                                                Duration="0:0:0" />
                                        </Storyboard>
                                    </BeginStoryboard>
                                </Trigger.ExitActions>
                            </Trigger>
                        </Style.Triggers>
                    </Style>
                    <Style
                        x:Key="ExternalDataTriggeredTextStyle"
                        TargetType="{x:Type TextBlock}">
                        <Setter Property="Text" Value="External data trigger inactive" />
                        <Setter Property="Tag" Value="data-inactive" />
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsExternalDataTriggerActive}" Value="True">
                                <Setter Property="Text" Value="External data trigger active" />
                                <Setter Property="Tag" Value="data-active" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                    <Style
                        x:Key="ExternalMultiDataTriggeredTextStyle"
                        TargetType="{x:Type TextBlock}">
                        <Setter Property="Text" Value="External multi data trigger inactive" />
                        <Setter Property="Tag" Value="multi-data-inactive" />
                        <Style.Triggers>
                            <MultiDataTrigger>
                                <MultiDataTrigger.Conditions>
                                    <Condition Binding="{Binding IsExternalDataTriggerActive}" Value="True" />
                                    <Condition Binding="{Binding IsExternalMultiTriggerReady}" Value="True" />
                                </MultiDataTrigger.Conditions>
                                <Setter Property="Text" Value="External multi data trigger active" />
                                <Setter Property="Tag" Value="multi-data-active" />
                            </MultiDataTrigger>
                        </Style.Triggers>
                    </Style>
                    <Style
                        x:Key="ExternalMultiTriggerActionTextStyle"
                        TargetType="{x:Type TextBlock}">
                        <Setter Property="Text" Value="External multi trigger action target" />
                        <Setter Property="Opacity" Value="0.88" />
                        <Setter Property="IsEnabled" Value="False" />
                        <Setter Property="Tag" Value="Disarmed" />
                        <Style.Triggers>
                            <MultiTrigger>
                                <MultiTrigger.Conditions>
                                    <Condition Property="IsEnabled" Value="True" />
                                    <Condition Property="Tag" Value="Armed" />
                                </MultiTrigger.Conditions>
                                <MultiTrigger.EnterActions>
                                    <BeginStoryboard>
                                        <Storyboard>
                                            <DoubleAnimation
                                                Storyboard.TargetProperty="Opacity"
                                                To="0.58"
                                                Duration="0:0:0" />
                                        </Storyboard>
                                    </BeginStoryboard>
                                </MultiTrigger.EnterActions>
                                <MultiTrigger.ExitActions>
                                    <BeginStoryboard>
                                        <Storyboard>
                                            <DoubleAnimation
                                                Storyboard.TargetProperty="Opacity"
                                                To="0.88"
                                                Duration="0:0:0" />
                                        </Storyboard>
                                    </BeginStoryboard>
                                </MultiTrigger.ExitActions>
                            </MultiTrigger>
                        </Style.Triggers>
                    </Style>
                    <Style
                        x:Key="ExternalDataTriggerActionTextStyle"
                        TargetType="{x:Type TextBlock}">
                        <Setter Property="Text" Value="External data trigger action target" />
                        <Setter Property="Opacity" Value="0.82" />
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsExternalDataTriggerActionActive}" Value="True">
                                <DataTrigger.EnterActions>
                                    <BeginStoryboard>
                                        <Storyboard>
                                            <DoubleAnimation
                                                Storyboard.TargetProperty="Opacity"
                                                To="0.31"
                                                Duration="0:0:0" />
                                        </Storyboard>
                                    </BeginStoryboard>
                                </DataTrigger.EnterActions>
                                <DataTrigger.ExitActions>
                                    <BeginStoryboard>
                                        <Storyboard>
                                            <DoubleAnimation
                                                Storyboard.TargetProperty="Opacity"
                                                To="0.82"
                                                Duration="0:0:0" />
                                        </Storyboard>
                                    </BeginStoryboard>
                                </DataTrigger.ExitActions>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                    <Style
                        x:Key="ExternalMultiDataTriggerActionTextStyle"
                        TargetType="{x:Type TextBlock}">
                        <Setter Property="Text" Value="External multi data trigger action target" />
                        <Setter Property="Opacity" Value="0.76" />
                        <Style.Triggers>
                            <MultiDataTrigger>
                                <MultiDataTrigger.Conditions>
                                    <Condition Binding="{Binding IsExternalMultiDataTriggerActionReady}" Value="True" />
                                    <Condition Binding="{Binding IsExternalMultiDataTriggerActionArmed}" Value="True" />
                                </MultiDataTrigger.Conditions>
                                <MultiDataTrigger.EnterActions>
                                    <BeginStoryboard>
                                        <Storyboard>
                                            <DoubleAnimation
                                                Storyboard.TargetProperty="Opacity"
                                                To="0.24"
                                                Duration="0:0:0" />
                                        </Storyboard>
                                    </BeginStoryboard>
                                </MultiDataTrigger.EnterActions>
                                <MultiDataTrigger.ExitActions>
                                    <BeginStoryboard>
                                        <Storyboard>
                                            <DoubleAnimation
                                                Storyboard.TargetProperty="Opacity"
                                                To="0.76"
                                                Duration="0:0:0" />
                                        </Storyboard>
                                    </BeginStoryboard>
                                </MultiDataTrigger.ExitActions>
                            </MultiDataTrigger>
                        </Style.Triggers>
                    </Style>
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
                        x:Name="ExternalLocalizedText"
                        x:Uid="ExternalLocalizedText"
                        Localization.Attributes="$Content (Readable Modifiable Text)"
                        Localization.Comments="$Content (External SDK localization comment)"
                        Text="External localized text" />
                    <TextBlock
                        x:Name="StaticResourceText"
                        Foreground="{StaticResource ExternalStaticBrush}"
                        Text="{StaticResource ExternalStaticText}" />
                    <TextBlock
                        x:Name="ExternalComponentResourceText"
                        Foreground="{StaticResource {ComponentResourceKey TypeInTargetAssembly={x:Type local:MainWindow}, ResourceId=ExternalComponentAccentBrush}}"
                        Text="External component resource" />
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
                    <Image
                        x:Name="ExternalXamlResourceImage"
                        Width="2"
                        Height="2"
                        Stretch="None"
                        Source="Assets/ExternalImage.png" />
                    <Rectangle
                        x:Name="ExternalXamlImageBrushRectangle"
                        Width="2"
                        Height="2">
                        <Rectangle.Fill>
                            <ImageBrush ImageSource="pack://application:,,,/Assets/ExternalImage.png" />
                        </Rectangle.Fill>
                    </Rectangle>
                    <TextBlock
                        x:Name="ExternalObjectProviderText"
                        Text="{Binding Source={StaticResource ExternalObjectDataProvider}}" />
                    <TextBlock
                        x:Name="ExternalXmlProviderText"
                        Text="{Binding Source={StaticResource ExternalXmlDataProvider}, XPath=@name}" />
                    <TextBlock
                        x:Name="ExternalMarkupExtensionText"
                        Text="{local:ExternalText Prefix=external, Value=markup}" />
                    <Border
                        x:Name="ExternalAncestorBindingBorder"
                        Tag="External ancestor tag">
                        <TextBlock
                            x:Name="ExternalAncestorBindingText"
                            Text="{Binding RelativeSource={RelativeSource AncestorType={x:Type Border}}, Path=Tag}" />
                    </Border>
                    <Button
                        x:Name="ExternalStyledButton"
                        Style="{StaticResource ExternalTriggeredButtonStyle}" />
                    <Button
                        x:Name="ExternalEventSetterButton"
                        Style="{StaticResource ExternalEventSetterButtonStyle}" />
                    <TextBlock
                        x:Name="ExternalPropertyTriggerActionText"
                        Style="{StaticResource ExternalPropertyTriggerActionTextStyle}" />
                    <TextBlock
                        x:Name="ExternalMultiTriggerActionText"
                        Style="{StaticResource ExternalMultiTriggerActionTextStyle}" />
                    <TextBlock
                        x:Name="ExternalDataTriggerText"
                        Style="{StaticResource ExternalDataTriggeredTextStyle}" />
                    <TextBlock
                        x:Name="ExternalMultiDataTriggerText"
                        Style="{StaticResource ExternalMultiDataTriggeredTextStyle}" />
                    <TextBlock
                        x:Name="ExternalDataTriggerActionText"
                        Style="{StaticResource ExternalDataTriggerActionTextStyle}" />
                    <TextBlock
                        x:Name="ExternalMultiDataTriggerActionText"
                        Style="{StaticResource ExternalMultiDataTriggerActionTextStyle}" />
                    <TextBlock
                        x:Name="ExternalLoadedStoryboardText"
                        Loaded="OnExternalLoadedStoryboardTextLoaded"
                        Opacity="1"
                        Text="External loaded storyboard target">
                        <TextBlock.Triggers>
                            <EventTrigger RoutedEvent="FrameworkElement.Loaded">
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation
                                            Storyboard.TargetName="ExternalLoadedStoryboardText"
                                            Storyboard.TargetProperty="Opacity"
                                            To="0.37"
                                            Duration="0:0:0" />
                                    </Storyboard>
                                </BeginStoryboard>
                            </EventTrigger>
                        </TextBlock.Triggers>
                    </TextBlock>
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
                    <StackPanel
                        x:Name="ExternalKeyboardNavigationPanel"
                        KeyboardNavigation.TabNavigation="Cycle">
                        <Button
                            x:Name="ExternalKeyboardNavigationFirstButton"
                            Content="External navigation first" />
                        <Button
                            x:Name="ExternalKeyboardNavigationSecondButton"
                            Content="External navigation second" />
                    </StackPanel>
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
                    <ContentControl
                        x:Name="ExternalTemplateSelectorPresenter"
                        Content="{Binding SelectedExternalItem}"
                        ContentTemplateSelector="{StaticResource ExternalItemTemplateSelector}" />
                    <ItemsControl
                        x:Name="ExternalTemplateSelectorItems"
                        ItemTemplateSelector="{StaticResource ExternalItemTemplateSelector}"
                        ItemsSource="{Binding ExternalItems}" />
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
                    <ListBox
                        x:Name="ExternalFilteredItemsList"
                        DisplayMemberPath="Name"
                        ItemsSource="{Binding Source={StaticResource ExternalFilteredItems}}" />
                    <ListBox
                        x:Name="ExternalLiveFilteredItemsList"
                        DisplayMemberPath="Name"
                        ItemsSource="{Binding Source={StaticResource ExternalLiveFilteredItems}}" />
                    <ListBox
                        x:Name="ExternalLiveSortedItemsList"
                        DisplayMemberPath="Name"
                        ItemsSource="{Binding Source={StaticResource ExternalLiveSortedItems}}" />
                    <ListBox
                        x:Name="ExternalLiveGroupedItemsList"
                        DisplayMemberPath="Name"
                        ItemsSource="{Binding Source={StaticResource ExternalLiveGroupedItems}}">
                        <ListBox.GroupStyle>
                            <GroupStyle HeaderTemplate="{StaticResource ExternalGroupHeaderTemplate}" />
                        </ListBox.GroupStyle>
                    </ListBox>
                    <ListBox
                        x:Name="ExternalCurrencyItemsList"
                        DisplayMemberPath="Name"
                        IsSynchronizedWithCurrentItem="True"
                        ItemsSource="{Binding Source={StaticResource ExternalCurrencyItems}}"
                        SelectedIndex="1" />
                    <ListBox x:Name="ExternalCompositeItemsList">
                        <ListBox.ItemsSource>
                            <CompositeCollection>
                                <sys:String>External composite header</sys:String>
                                <CollectionContainer Collection="{x:Static local:ExternalCompositeProvider.Items}" />
                                <ListBoxItem Content="External composite item container" />
                            </CompositeCollection>
                        </ListBox.ItemsSource>
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
                        x:Name="ExternalBindingTransferTextBox"
                        SourceUpdated="OnExternalBindingSourceUpdated"
                        TargetUpdated="OnExternalBindingTargetUpdated"
                        Text="{Binding ExternalBindingTransferText, Mode=TwoWay, UpdateSourceTrigger=Explicit, NotifyOnSourceUpdated=True, NotifyOnTargetUpdated=True}" />
                    <TextBox
                        x:Name="ExternalValidationTextBox"
                        AutomationProperties.AutomationId="ExternalValidationTextBoxAutomation"
                        AutomationProperties.HelpText="External SDK validation text"
                        AutomationProperties.LabeledBy="{Binding ElementName=ExternalAccessLabel}"
                        AutomationProperties.Name="External validation input"
                        InputLanguageManager.InputLanguage="en-US"
                        InputMethod.PreferredImeConversionMode="Native, FullShape"
                        InputMethod.PreferredImeSentenceMode="Automatic"
                        InputMethod.PreferredImeState="On"
                        Validation.Error="OnExternalValidationError"
                        TextChanged="OnExternalValidationTextChanged">
                        <TextBox.Text>
                            <Binding
                                Path="ValidationText"
                                Mode="TwoWay"
                                NotifyOnValidationError="True"
                                UpdateSourceTrigger="Explicit">
                                <Binding.ValidationRules>
                                    <local:ExternalNonEmptyValidationRule />
                                </Binding.ValidationRules>
                            </Binding>
                        </TextBox.Text>
                        <InputMethod.InputScope>
                            <InputScope
                                RegularExpression="[A-Z0-9]+"
                                SrgsMarkup="external-sdk-input-scope">
                                <InputScope.Names>
                                    <InputScopeName>EmailSmtpAddress</InputScopeName>
                                </InputScope.Names>
                                <InputScope.PhraseList>
                                    <InputScopePhrase>external package phrase</InputScopePhrase>
                                </InputScope.PhraseList>
                            </InputScope>
                        </InputMethod.InputScope>
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
                    <StackPanel x:Name="ExternalRoutedEventPanel">
                        <local:ExternalRoutedEventControl
                            x:Name="ExternalRoutedEventControl"
                            Content="External routed event source"
                            ExternalBubble="OnExternalCustomBubble"
                            ExternalTunnel="OnExternalCustomTunnel" />
                    </StackPanel>
                    <StackPanel
                        x:Name="ExternalDependencyPropertyPanel"
                        local:ExternalDependencyPropertyControl.InheritedLabel="External inherited label">
                        <local:ExternalDependencyPropertyControl
                            x:Name="ExternalDependencyPropertyControl"
                            CoercedNumber="120"
                            TrackedText="compiled tracked text" />
                        <local:ExternalDependencyPropertyControl
                            x:Name="ExternalDependencyPropertyLocalControl"
                            local:ExternalDependencyPropertyControl.InheritedLabel="External local label"
                            CoercedNumber="42" />
                    </StackPanel>
                    <Button
                        x:Name="ExternalCommandButton"
                        Command="{x:Static local:MainWindow.ExternalCommand}"
                        CommandParameter="ExternalCommandParameter"
                        Click="OnExternalCommandButtonClick"
                        Content="Run command" />
                    <Button
                        x:Name="ExternalRequeryCommandButton"
                        Command="{Binding ExternalRequeryCommand}"
                        CommandParameter="ExternalRequeryParameter"
                        Content="Run requery command" />
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
            using System.Collections.Specialized;
            using System.Collections.Generic;
            using System.ComponentModel;
            using System.Globalization;
            using System.IO;
            using System.IO.Compression;
            using System.Linq;
            using System.Reflection;
            using System.Windows;
            using System.Windows.Automation;
            using System.Windows.Automation.Peers;
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

            public partial class MainWindow : Window, INotifyPropertyChanged
            {
                public static readonly RoutedUICommand ExternalCommand = new(
                    "External SDK command",
                    nameof(ExternalCommand),
                    typeof(MainWindow));

                public ExternalRequeryCommand ExternalRequeryCommand { get; } = new();

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

                public ObservableCollection<ExternalItem> ExternalLiveItems { get; } =
                [
                    new ExternalItem("Live Alpha", "Framework", true),
                    new ExternalItem("Live Beta", "Rendering", false),
                    new ExternalItem("Live Gamma", "Data", false)
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

                public string ExternalBindingTransferText { get; set; } = "external transfer initial";

                public string BindingGroupFirstName { get; set; } = "group: Ada";

                public string BindingGroupLastName { get; set; } = "group: Lovelace";

                public bool IsExternalDataTriggerActive
                {
                    get => _isExternalDataTriggerActive;
                    set
                    {
                        if (_isExternalDataTriggerActive != value)
                        {
                            _isExternalDataTriggerActive = value;
                            OnPropertyChanged(nameof(IsExternalDataTriggerActive));
                        }
                    }
                }

                public bool IsExternalMultiTriggerReady
                {
                    get => _isExternalMultiTriggerReady;
                    set
                    {
                        if (_isExternalMultiTriggerReady != value)
                        {
                            _isExternalMultiTriggerReady = value;
                            OnPropertyChanged(nameof(IsExternalMultiTriggerReady));
                        }
                    }
                }

                public bool IsExternalDataTriggerActionActive
                {
                    get => _isExternalDataTriggerActionActive;
                    set
                    {
                        if (_isExternalDataTriggerActionActive != value)
                        {
                            _isExternalDataTriggerActionActive = value;
                            OnPropertyChanged(nameof(IsExternalDataTriggerActionActive));
                        }
                    }
                }

                public bool IsExternalMultiDataTriggerActionReady
                {
                    get => _isExternalMultiDataTriggerActionReady;
                    set
                    {
                        if (_isExternalMultiDataTriggerActionReady != value)
                        {
                            _isExternalMultiDataTriggerActionReady = value;
                            OnPropertyChanged(nameof(IsExternalMultiDataTriggerActionReady));
                        }
                    }
                }

                public bool IsExternalMultiDataTriggerActionArmed
                {
                    get => _isExternalMultiDataTriggerActionArmed;
                    set
                    {
                        if (_isExternalMultiDataTriggerActionArmed != value)
                        {
                            _isExternalMultiDataTriggerActionArmed = value;
                            OnPropertyChanged(nameof(IsExternalMultiDataTriggerActionArmed));
                        }
                    }
                }

                public event PropertyChangedEventHandler? PropertyChanged;

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

                public int ExternalBindingSourceUpdatedCount { get; private set; }

                public int ExternalBindingTargetUpdatedCount { get; private set; }

                public string? LastExternalBindingSourceUpdatedSenderName { get; private set; }

                public string? LastExternalBindingTargetUpdatedSenderName { get; private set; }

                public string? LastExternalBindingSourceUpdatedTargetName { get; private set; }

                public string? LastExternalBindingTargetUpdatedTargetName { get; private set; }

                public string? LastExternalBindingSourceUpdatedPropertyName { get; private set; }

                public string? LastExternalBindingTargetUpdatedPropertyName { get; private set; }

                public string? LastExternalBindingSourceUpdatedRoutedEventName { get; private set; }

                public string? LastExternalBindingTargetUpdatedRoutedEventName { get; private set; }

                public int ExternalValidationErrorAddedCount { get; private set; }

                public int ExternalValidationErrorRemovedCount { get; private set; }

                public string? LastExternalValidationErrorAction { get; private set; }

                public string? LastExternalValidationErrorContent { get; private set; }

                public string? LastExternalValidationErrorRoutedEventName { get; private set; }

                public string? LastExternalValidationErrorSenderName { get; private set; }

                public int ExternalSliderValueChangedCount { get; private set; }

                public double LastExternalSliderValue { get; private set; }

                public string? LastExternalCheckBoxRoutedEventName { get; private set; }

                public string? LastExternalRadioButtonCheckedName { get; private set; }

                public string? LastExternalRadioButtonUncheckedName { get; private set; }

                public string? LastExternalToggleButtonRoutedEventName { get; private set; }

                public int ExternalCommandCanExecuteCount { get; private set; }

                public int ExternalCommandExecutedCount { get; private set; }

                public int ExternalCommandButtonClickCount { get; private set; }

                public int ExternalStyleEventButtonClickCount { get; private set; }

                public string? LastExternalStyleEventSenderName { get; private set; }

                public string? LastExternalStyleEventRoutedEventName { get; private set; }

                public int ExternalBubbleRoutedEventCount { get; private set; }

                public string? LastExternalBubbleSenderName { get; private set; }

                public string? LastExternalBubbleOriginalSourceName { get; private set; }

                public string? LastExternalBubbleRoutedEventName { get; private set; }

                public int ExternalTunnelRoutedEventCount { get; private set; }

                public string? LastExternalTunnelSenderName { get; private set; }

                public string? LastExternalTunnelOriginalSourceName { get; private set; }

                public string? LastExternalTunnelRoutedEventName { get; private set; }

                public int ExternalPreviewDragEnterCount { get; private set; }

                public int ExternalDragEnterCount { get; private set; }

                public int ExternalPreviewDragOverCount { get; private set; }

                public int ExternalDragOverCount { get; private set; }

                public int ExternalPreviewDragLeaveCount { get; private set; }

                public int ExternalDragLeaveCount { get; private set; }

                public int ExternalPreviewDropCount { get; private set; }

                public int ExternalDropCount { get; private set; }

                public string? LastExternalDropText { get; private set; }

                public int LastExternalDropFileCount { get; private set; }

                public string? LastExternalDropFirstFile { get; private set; }

                public string? LastExternalDropAllowedEffects { get; private set; }

                public string? LastExternalDropEffects { get; private set; }

                public string? LastExternalDropRoutedEventName { get; private set; }

                public string? LastExternalPreviewDropRoutedEventName { get; private set; }

                public string? LastExternalPreviewDragEnterRoutedEventName { get; private set; }

                public string? LastExternalDragEnterRoutedEventName { get; private set; }

                public string? LastExternalPreviewDragOverRoutedEventName { get; private set; }

                public string? LastExternalDragOverRoutedEventName { get; private set; }

                public string? LastExternalPreviewDragLeaveRoutedEventName { get; private set; }

                public string? LastExternalDragLeaveRoutedEventName { get; private set; }

                public string? LastExternalDragEnterAllowedEffects { get; private set; }

                public string? LastExternalDragOverAllowedEffects { get; private set; }

                public string? LastExternalDragEnterEffects { get; private set; }

                public string? LastExternalDragOverEffects { get; private set; }

                public double LastExternalDropX { get; private set; }

                public double LastExternalDropY { get; private set; }

                public int ExternalLoadedStoryboardTextLoadedCount { get; private set; }

                public string? LastExternalLoadedStoryboardTextRoutedEventName { get; private set; }

                public int ExternalItemsFilterCount { get; private set; }

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

                public int ExternalWindowClosingCount { get; private set; }

                public int ExternalWindowClosedCount { get; private set; }

                public bool CancelNextExternalWindowClose { get; set; }

                public bool LastExternalWindowClosingCancelBefore { get; private set; }

                public bool LastExternalWindowClosingCancelAfter { get; private set; }

                public string? LastExternalWindowClosingSenderType { get; private set; }

                public string? LastExternalWindowClosedSenderType { get; private set; }

                private void OnExternalWindowClosing(object sender, CancelEventArgs e)
                {
                    ExternalWindowClosingCount++;
                    LastExternalWindowClosingSenderType = sender.GetType().Name;
                    LastExternalWindowClosingCancelBefore = e.Cancel;

                    if (CancelNextExternalWindowClose)
                    {
                        e.Cancel = true;
                        CancelNextExternalWindowClose = false;
                    }

                    LastExternalWindowClosingCancelAfter = e.Cancel;
                }

                private void OnExternalWindowClosed(object sender, EventArgs e)
                {
                    ExternalWindowClosedCount++;
                    LastExternalWindowClosedSenderType = sender.GetType().Name;
                }

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

                private void OnExternalBindingSourceUpdated(object sender, DataTransferEventArgs e)
                {
                    ExternalBindingSourceUpdatedCount++;
                    LastExternalBindingSourceUpdatedSenderName = (sender as FrameworkElement)?.Name;
                    LastExternalBindingSourceUpdatedTargetName = (e.TargetObject as FrameworkElement)?.Name;
                    LastExternalBindingSourceUpdatedPropertyName = e.Property?.Name;
                    LastExternalBindingSourceUpdatedRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalBindingTargetUpdated(object sender, DataTransferEventArgs e)
                {
                    ExternalBindingTargetUpdatedCount++;
                    LastExternalBindingTargetUpdatedSenderName = (sender as FrameworkElement)?.Name;
                    LastExternalBindingTargetUpdatedTargetName = (e.TargetObject as FrameworkElement)?.Name;
                    LastExternalBindingTargetUpdatedPropertyName = e.Property?.Name;
                    LastExternalBindingTargetUpdatedRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalValidationError(object sender, ValidationErrorEventArgs e)
                {
                    if (e.Action == ValidationErrorEventAction.Added)
                    {
                        ExternalValidationErrorAddedCount++;
                    }
                    else if (e.Action == ValidationErrorEventAction.Removed)
                    {
                        ExternalValidationErrorRemovedCount++;
                    }

                    LastExternalValidationErrorAction = e.Action.ToString();
                    LastExternalValidationErrorContent = e.Error.ErrorContent?.ToString();
                    LastExternalValidationErrorRoutedEventName = e.RoutedEvent?.Name;
                    LastExternalValidationErrorSenderName = (sender as FrameworkElement)?.Name;
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

                private void OnExternalStyleEventButtonClick(object sender, RoutedEventArgs e)
                {
                    ExternalStyleEventButtonClickCount++;
                    LastExternalStyleEventSenderName = (sender as FrameworkElement)?.Name;
                    LastExternalStyleEventRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalCustomBubble(object sender, RoutedEventArgs e)
                {
                    ExternalBubbleRoutedEventCount++;
                    LastExternalBubbleSenderName = (sender as FrameworkElement)?.Name;
                    LastExternalBubbleOriginalSourceName = (e.OriginalSource as FrameworkElement)?.Name;
                    LastExternalBubbleRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalCustomTunnel(object sender, RoutedEventArgs e)
                {
                    ExternalTunnelRoutedEventCount++;
                    LastExternalTunnelSenderName = (sender as FrameworkElement)?.Name;
                    LastExternalTunnelOriginalSourceName = (e.OriginalSource as FrameworkElement)?.Name;
                    LastExternalTunnelRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalPreviewDragEnter(object sender, DragEventArgs e)
                {
                    ExternalPreviewDragEnterCount++;
                    LastExternalPreviewDragEnterRoutedEventName = e.RoutedEvent?.Name;
                    LastExternalDragEnterAllowedEffects = e.AllowedEffects.ToString();
                }

                private void OnExternalDragEnter(object sender, DragEventArgs e)
                {
                    ExternalDragEnterCount++;
                    LastExternalDragEnterRoutedEventName = e.RoutedEvent?.Name;
                    LastExternalDragEnterAllowedEffects = e.AllowedEffects.ToString();
                    e.Effects = DragDropEffects.Move;
                    LastExternalDragEnterEffects = e.Effects.ToString();
                }

                private void OnExternalPreviewDragOver(object sender, DragEventArgs e)
                {
                    ExternalPreviewDragOverCount++;
                    LastExternalPreviewDragOverRoutedEventName = e.RoutedEvent?.Name;
                    LastExternalDragOverAllowedEffects = e.AllowedEffects.ToString();
                }

                private void OnExternalDragOver(object sender, DragEventArgs e)
                {
                    ExternalDragOverCount++;
                    LastExternalDragOverRoutedEventName = e.RoutedEvent?.Name;
                    LastExternalDragOverAllowedEffects = e.AllowedEffects.ToString();
                    e.Effects = DragDropEffects.Move;
                    LastExternalDragOverEffects = e.Effects.ToString();
                }

                private void OnExternalPreviewDragLeave(object sender, DragEventArgs e)
                {
                    ExternalPreviewDragLeaveCount++;
                    LastExternalPreviewDragLeaveRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalDragLeave(object sender, DragEventArgs e)
                {
                    ExternalDragLeaveCount++;
                    LastExternalDragLeaveRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalPreviewDrop(object sender, DragEventArgs e)
                {
                    ExternalPreviewDropCount++;
                    LastExternalPreviewDropRoutedEventName = e.RoutedEvent?.Name;
                    LastExternalDropAllowedEffects = e.AllowedEffects.ToString();
                }

                private void OnExternalDrop(object sender, DragEventArgs e)
                {
                    ExternalDropCount++;
                    LastExternalDropText = e.Data.GetDataPresent(DataFormats.UnicodeText)
                        ? e.Data.GetData(DataFormats.UnicodeText) as string
                        : e.Data.GetData(DataFormats.Text) as string;
                    var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                    LastExternalDropFileCount = files?.Length ?? 0;
                    LastExternalDropFirstFile = files?.FirstOrDefault();
                    LastExternalDropAllowedEffects = e.AllowedEffects.ToString();
                    LastExternalDropRoutedEventName = e.RoutedEvent?.Name;
                    Point position = e.GetPosition(this);
                    LastExternalDropX = position.X;
                    LastExternalDropY = position.Y;
                    e.Effects = DragDropEffects.Move;
                    LastExternalDropEffects = e.Effects.ToString();
                    e.Handled = true;
                }

                private void OnExternalLoadedStoryboardTextLoaded(object sender, RoutedEventArgs e)
                {
                    ExternalLoadedStoryboardTextLoadedCount++;
                    LastExternalLoadedStoryboardTextRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalItemsFilter(object sender, FilterEventArgs e)
                {
                    ExternalItemsFilterCount++;
                    e.Accepted = e.Item is ExternalItem item && item.IsActive;
                }

                private void OnPropertyChanged(string propertyName)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }

                private bool _isExternalDataTriggerActive;

                private bool _isExternalMultiTriggerReady;

                private bool _isExternalDataTriggerActionActive;

                private bool _isExternalMultiDataTriggerActionReady;

                private bool _isExternalMultiDataTriggerActionArmed;
            }

            public sealed class ExternalItem : INotifyPropertyChanged
            {
                private string _name;
                private string _kind;
                private bool _isActive;

                public ExternalItem(string name, string kind, bool isActive = false)
                {
                    _name = name;
                    _kind = kind;
                    _isActive = isActive;
                }

                public string Name
                {
                    get => _name;
                    set
                    {
                        if (_name != value)
                        {
                            _name = value;
                            OnPropertyChanged(nameof(Name));
                        }
                    }
                }

                public string Kind
                {
                    get => _kind;
                    set
                    {
                        if (_kind != value)
                        {
                            _kind = value;
                            OnPropertyChanged(nameof(Kind));
                        }
                    }
                }

                public bool IsActive
                {
                    get => _isActive;
                    set
                    {
                        if (_isActive != value)
                        {
                            _isActive = value;
                            OnPropertyChanged(nameof(IsActive));
                        }
                    }
                }

                public event PropertyChangedEventHandler? PropertyChanged;

                private void OnPropertyChanged(string propertyName)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }
            }

            public sealed class ExternalItemTemplateSelector : DataTemplateSelector
            {
                public DataTemplate? FrameworkTemplate { get; set; }

                public DataTemplate? RenderingTemplate { get; set; }

                public DataTemplate? DefaultTemplate { get; set; }

                public override DataTemplate? SelectTemplate(object item, DependencyObject container)
                {
                    return item is ExternalItem externalItem
                        ? externalItem.Kind switch
                        {
                            "Framework" => FrameworkTemplate,
                            "Rendering" => RenderingTemplate,
                            _ => DefaultTemplate
                        }
                        : DefaultTemplate;
                }
            }

            public static class ExternalResourceFactory
            {
                public static string CreateSummary(string prefix, int value)
                {
                    return $"{prefix}:{value}";
                }
            }

            public static class ExternalCompositeProvider
            {
                public static ObservableCollection<ExternalItem> Items { get; } =
                [
                    new ExternalItem("Composite Alpha", "Framework"),
                    new ExternalItem("Composite Beta", "Rendering")
                ];
            }

            public sealed class ExternalRequeryCommand : ICommand
            {
                public int CanExecuteProbeCount { get; private set; }

                public int ExecuteCount { get; private set; }

                public bool CanExecuteValue { get; set; }

                public object? LastParameter { get; private set; }

                public event EventHandler? CanExecuteChanged
                {
                    add
                    {
                        if (value != null)
                        {
                            CommandManager.RequerySuggested += value;
                        }
                    }

                    remove
                    {
                        if (value != null)
                        {
                            CommandManager.RequerySuggested -= value;
                        }
                    }
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

            public sealed class ExternalRoutedEventControl : Button
            {
                public static readonly RoutedEvent ExternalBubbleEvent = EventManager.RegisterRoutedEvent(
                    nameof(ExternalBubble),
                    RoutingStrategy.Bubble,
                    typeof(RoutedEventHandler),
                    typeof(ExternalRoutedEventControl));

                public static readonly RoutedEvent ExternalTunnelEvent = EventManager.RegisterRoutedEvent(
                    nameof(ExternalTunnel),
                    RoutingStrategy.Tunnel,
                    typeof(RoutedEventHandler),
                    typeof(ExternalRoutedEventControl));

                public event RoutedEventHandler ExternalBubble
                {
                    add => AddHandler(ExternalBubbleEvent, value);
                    remove => RemoveHandler(ExternalBubbleEvent, value);
                }

                public event RoutedEventHandler ExternalTunnel
                {
                    add => AddHandler(ExternalTunnelEvent, value);
                    remove => RemoveHandler(ExternalTunnelEvent, value);
                }

                public void RaiseExternalBubble()
                {
                    RaiseEvent(new RoutedEventArgs(ExternalBubbleEvent, this));
                }

                public void RaiseExternalTunnel()
                {
                    RaiseEvent(new RoutedEventArgs(ExternalTunnelEvent, this));
                }
            }

            public sealed class ExternalDependencyPropertyControl : Control
            {
                public static readonly DependencyProperty InheritedLabelProperty =
                    DependencyProperty.RegisterAttached(
                        "InheritedLabel",
                        typeof(string),
                        typeof(ExternalDependencyPropertyControl),
                        new FrameworkPropertyMetadata(
                            "default inherited label",
                            FrameworkPropertyMetadataOptions.Inherits));

                public static readonly DependencyProperty CoercedNumberProperty =
                    DependencyProperty.Register(
                        nameof(CoercedNumber),
                        typeof(int),
                        typeof(ExternalDependencyPropertyControl),
                        new FrameworkPropertyMetadata(
                            0,
                            OnCoercedNumberChanged,
                            CoerceNumber),
                        value => value is int);

                public static readonly DependencyProperty TrackedTextProperty =
                    DependencyProperty.Register(
                        nameof(TrackedText),
                        typeof(string),
                        typeof(ExternalDependencyPropertyControl),
                        new FrameworkPropertyMetadata(
                            "default tracked text",
                            OnTrackedTextChanged));

                public int CoercedNumber
                {
                    get => (int)GetValue(CoercedNumberProperty);
                    set => SetValue(CoercedNumberProperty, value);
                }

                public string TrackedText
                {
                    get => (string)GetValue(TrackedTextProperty);
                    set => SetValue(TrackedTextProperty, value);
                }

                public int CoercedNumberChangeCount { get; private set; }

                public int LastCoercedNumberOldValue { get; private set; }

                public int LastCoercedNumberNewValue { get; private set; }

                public int TrackedTextChangeCount { get; private set; }

                public string? LastTrackedTextOldValue { get; private set; }

                public string? LastTrackedTextNewValue { get; private set; }

                public static string GetInheritedLabel(DependencyObject element)
                {
                    return (string)element.GetValue(InheritedLabelProperty);
                }

                public static void SetInheritedLabel(DependencyObject element, string value)
                {
                    element.SetValue(InheritedLabelProperty, value);
                }

                private static object CoerceNumber(DependencyObject element, object baseValue)
                {
                    return Math.Clamp((int)baseValue, 0, 100);
                }

                private static void OnCoercedNumberChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
                {
                    var control = (ExternalDependencyPropertyControl)element;
                    control.CoercedNumberChangeCount++;
                    control.LastCoercedNumberOldValue = (int)e.OldValue;
                    control.LastCoercedNumberNewValue = (int)e.NewValue;
                }

                private static void OnTrackedTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
                {
                    var control = (ExternalDependencyPropertyControl)element;
                    control.TrackedTextChangeCount++;
                    control.LastTrackedTextOldValue = (string)e.OldValue;
                    control.LastTrackedTextNewValue = (string)e.NewValue;
                }
            }

            public sealed class ExternalTextExtension : MarkupExtension
            {
                public static int ProvideValueCount { get; private set; }

                public static string? LastTargetPropertyName { get; private set; }

                public string Prefix { get; set; } = string.Empty;

                public string Value { get; set; } = string.Empty;

                public override object ProvideValue(IServiceProvider serviceProvider)
                {
                    ProvideValueCount++;
                    if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target)
                    {
                        LastTargetPropertyName = target.TargetProperty switch
                        {
                            DependencyProperty dependencyProperty => dependencyProperty.Name,
                            PropertyInfo propertyInfo => propertyInfo.Name,
                            _ => target.TargetProperty?.ToString()
                        };
                    }

                    return $"{Prefix}:{Value}";
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
                    ValidatePackResources();
                    ValidateSystemParameters(window);
                    ValidateWindowChrome(window);
                    ValidateSystemCommands(window);
                    ValidateLauncher();
                    ValidateMessageBox(window);
                    ValidateFileDialogs(window);
                    ValidateClipboard();
                    ValidateFreezableResources();
                    ValidateManagedFrameworkCollections();
                    ValidateManagedImagingObjects();
                    ValidateLooseXamlReaderWriter();
                    ValidateMarkupExtensions(window);
                    ValidateDataProviders(window);
                    ValidateBindings(window);
                    ValidateInputManagers(window);
                    ValidateBindingGroup(window);
                    ValidatePortableDragDrop(window);
                    ValidateRoutedEvents(window);
                    ValidateDependencyProperties(window);
                    ValidateStylesAndTemplates(window);
                    ValidateLoadedStoryboardMetadata(window);
                    ValidatePropertyTriggerActionsMetadata(window);
                    ValidateMultiTriggerActionsMetadata(window);
                    ValidateDataTriggerActionsMetadata(window);
                    ValidateMultiDataTriggerActionsMetadata(window);
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
                    ValidateLoadedStoryboardAfterRun(window);
                    ValidatePropertyTriggerActionsAfterRun(window);
                    ValidateMultiTriggerActionsAfterRun(window);
                    ValidateDataTriggerActionsAfterRun(window);
                    ValidateMultiDataTriggerActionsAfterRun(window);
                    ValidateVisualStateTransitions(window);
                    ValidateAdornerLayer(window);
                    ValidateAccessKeyRoutingAfterRun(window);
                    ValidateKeyboardNavigationAfterRun(window);
                    ValidateApplicationWindowLifetime(app, window);

                    App.MarkExternalRunValidated();
                }

                private static void ValidateApplicationWindowLifetime(App app, MainWindow window)
                {
                    int closingCountBefore = window.ExternalWindowClosingCount;
                    int closedCountBefore = window.ExternalWindowClosedCount;

                    window.CancelNextExternalWindowClose = true;
                    window.Close();
                    DrainDispatcher();

                    AssertEqual(closingCountBefore + 1, window.ExternalWindowClosingCount, "external SDK canceled window Closing count");
                    AssertEqual(closedCountBefore, window.ExternalWindowClosedCount, "external SDK canceled window Closed count");
                    AssertEqual(false, window.CancelNextExternalWindowClose, "external SDK canceled window close request reset");
                    AssertEqual(false, window.LastExternalWindowClosingCancelBefore, "external SDK canceled window Closing initial cancel state");
                    AssertEqual(true, window.LastExternalWindowClosingCancelAfter, "external SDK canceled window Closing final cancel state");
                    AssertEqual(nameof(MainWindow), window.LastExternalWindowClosingSenderType, "external SDK canceled window Closing sender");
                    AssertEqual(true, window.IsVisible, "external SDK canceled window visibility");

                    bool containsMainWindowAfterCanceledClose = false;
                    foreach (Window candidate in app.Windows)
                    {
                        if (ReferenceEquals(candidate, window))
                        {
                            containsMainWindowAfterCanceledClose = true;
                            break;
                        }
                    }

                    AssertEqual(true, containsMainWindowAfterCanceledClose, "external SDK application windows contains main window after canceled close");

                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    AssertEqual(ShutdownMode.OnMainWindowClose, app.ShutdownMode, "external SDK application main-window shutdown mode");

                    window.Close();

                    AssertEqual(closingCountBefore + 2, window.ExternalWindowClosingCount, "external SDK final window Closing count");
                    AssertEqual(closedCountBefore + 1, window.ExternalWindowClosedCount, "external SDK final window Closed count");
                    AssertEqual(false, window.LastExternalWindowClosingCancelBefore, "external SDK final window Closing initial cancel state");
                    AssertEqual(false, window.LastExternalWindowClosingCancelAfter, "external SDK final window Closing final cancel state");
                    AssertEqual(nameof(MainWindow), window.LastExternalWindowClosedSenderType, "external SDK final window Closed sender");
                    AssertEqual(false, window.IsVisible, "external SDK final window visibility");
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
                    var localizedText = RequireType<TextBlock>(
                        window.FindName("ExternalLocalizedText"),
                        "external SDK localized text block");
                    AssertEqual("External localized text", localizedText.Text, "external SDK x:Uid text");
                    AssertEqual("ExternalLocalizedText", localizedText.Uid, "external SDK x:Uid value");
                    AssertEqual(
                        "$Content (External SDK localization comment)",
                        Localization.GetComments(localizedText),
                        "external SDK Localization.Comments");
                    AssertEqual(
                        "$Content (Readable Modifiable Text)",
                        Localization.GetAttributes(localizedText),
                        "external SDK Localization.Attributes");
                    AssertEqual(
                        "External SDK resource text",
                        appResources["ExternalStaticText"],
                        "external SDK application static text resource");
                    AssertBrushColor(
                        RequireType<Brush>(appResources["ExternalStaticBrush"], "external SDK application static brush resource"),
                        "#FFA65A2A",
                        "external SDK application static brush resource");
                    var componentResourceKey = new ComponentResourceKey(typeof(MainWindow), "ExternalComponentAccentBrush");
                    var componentBrush = RequireType<Brush>(
                        appResources[componentResourceKey],
                        "external SDK ComponentResourceKey application brush");
                    AssertBrushColor(componentBrush, "#FF4E7A9D", "external SDK ComponentResourceKey application brush");
                    AssertEqual(
                        componentBrush,
                        window.TryFindResource(componentResourceKey),
                        "external SDK ComponentResourceKey window lookup");

                    var staticResourceText = RequireType<TextBlock>(
                        window.FindName("StaticResourceText"),
                        "external SDK static resource text block");
                    AssertEqual("External SDK resource text", staticResourceText.Text, "external SDK static resource text");
                    AssertBrushColor(staticResourceText.Foreground, "#FFA65A2A", "external SDK static resource foreground");

                    var componentResourceText = RequireType<TextBlock>(
                        window.FindName("ExternalComponentResourceText"),
                        "external SDK ComponentResourceKey text block");
                    AssertEqual("External component resource", componentResourceText.Text, "external SDK ComponentResourceKey text");
                    AssertBrushColor(componentResourceText.Foreground, "#FF4E7A9D", "external SDK ComponentResourceKey foreground");

                    var xamlResourceImage = RequireType<Image>(
                        window.FindName("ExternalXamlResourceImage"),
                        "external SDK XAML resource image");
                    var xamlResourceImageSource = RequireType<BitmapSource>(
                        xamlResourceImage.Source,
                        "external SDK XAML resource image source");
                    AssertEqual(2, xamlResourceImageSource.PixelWidth, "external SDK XAML resource image pixel width");
                    AssertEqual(2, xamlResourceImageSource.PixelHeight, "external SDK XAML resource image pixel height");
                    AssertEqual(PixelFormats.Bgra32, xamlResourceImageSource.Format, "external SDK XAML resource image Bgra32 format");
                    byte[] xamlResourceImagePixels = new byte[16];
                    xamlResourceImageSource.CopyPixels(xamlResourceImagePixels, 8, 0);
                    AssertEqual((byte)0xFF, xamlResourceImagePixels[2], "external SDK XAML resource image top-left red byte");
                    AssertEqual((byte)0xFF, xamlResourceImagePixels[15], "external SDK XAML resource image final alpha byte");

                    var xamlImageBrushRectangle = RequireType<System.Windows.Shapes.Rectangle>(
                        window.FindName("ExternalXamlImageBrushRectangle"),
                        "external SDK XAML ImageBrush rectangle");
                    var xamlImageBrush = RequireType<ImageBrush>(
                        xamlImageBrushRectangle.Fill,
                        "external SDK XAML ImageBrush fill");
                    var xamlImageBrushSource = RequireType<BitmapSource>(
                        xamlImageBrush.ImageSource,
                        "external SDK XAML ImageBrush source");
                    AssertEqual(2, xamlImageBrushSource.PixelWidth, "external SDK XAML ImageBrush pixel width");
                    AssertEqual(2, xamlImageBrushSource.PixelHeight, "external SDK XAML ImageBrush pixel height");
                    AssertEqual(PixelFormats.Bgra32, xamlImageBrushSource.Format, "external SDK XAML ImageBrush Bgra32 format");
                    byte[] xamlImageBrushPixels = new byte[16];
                    xamlImageBrushSource.CopyPixels(xamlImageBrushPixels, 8, 0);
                    AssertEqual((byte)0xFF, xamlImageBrushPixels[5], "external SDK XAML ImageBrush top-right green byte");
                    AssertEqual((byte)0xFF, xamlImageBrushPixels[15], "external SDK XAML ImageBrush final alpha byte");

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

                    var frameworkTemplate = RequireType<DataTemplate>(
                        window.FindResource("ExternalFrameworkItemTemplate"),
                        "external SDK framework item selector template");
                    var renderingTemplate = RequireType<DataTemplate>(
                        window.FindResource("ExternalRenderingItemTemplate"),
                        "external SDK rendering item selector template");
                    var defaultTemplate = RequireType<DataTemplate>(
                        window.FindResource("ExternalDefaultItemTemplate"),
                        "external SDK default item selector template");
                    var selector = RequireType<ExternalItemTemplateSelector>(
                        window.FindResource("ExternalItemTemplateSelector"),
                        "external SDK item template selector resource");
                    AssertEqual(frameworkTemplate, selector.FrameworkTemplate, "external SDK item template selector framework template");
                    AssertEqual(renderingTemplate, selector.RenderingTemplate, "external SDK item template selector rendering template");
                    AssertEqual(defaultTemplate, selector.DefaultTemplate, "external SDK item template selector default template");

                    var selectorPresenter = RequireType<ContentControl>(
                        window.FindName("ExternalTemplateSelectorPresenter"),
                        "external SDK content template selector presenter");
                    AssertEqual(window.SelectedExternalItem, selectorPresenter.Content, "external SDK content template selector content");
                    AssertEqual(selector, selectorPresenter.ContentTemplateSelector, "external SDK content template selector binding");
                    AssertEqual(frameworkTemplate, selector.SelectTemplate(window.ExternalItems[0], selectorPresenter), "external SDK content template selector selected template");
                    AssertTemplateText(frameworkTemplate, window.ExternalItems[0], "Framework template Alpha", "external SDK framework selected template text");
                    AssertTemplateText(renderingTemplate, window.ExternalItems[1], "Rendering template Beta", "external SDK rendering selected template text");

                    var selectorItems = RequireType<ItemsControl>(
                        window.FindName("ExternalTemplateSelectorItems"),
                        "external SDK item template selector items control");
                    AssertEqual(selector, selectorItems.ItemTemplateSelector, "external SDK ItemsControl ItemTemplateSelector");
                    AssertEqual(2, selectorItems.Items.Count, "external SDK item template selector item count");

                    var itemsList = RequireType<ListBox>(
                        window.FindName("ExternalItemsList"),
                        "external SDK bound items list");
                    AssertEqual(2, itemsList.Items.Count, "external SDK bound items count");
                    AssertEqual(1, itemsList.SelectedIndex, "external SDK selected item index");
                    AssertEqual(window.ExternalItems[1], itemsList.SelectedItem, "external SDK selected item");
                    window.ExternalItems.Add(new ExternalItem("Gamma", "Data"));
                    DrainDispatcher();
                    AssertEqual(3, itemsList.Items.Count, "external SDK bound items count after collection change");
                    AssertEqual(3, selectorItems.Items.Count, "external SDK item template selector collection count after mutation");
                    AssertEqual(defaultTemplate, selector.SelectTemplate(window.ExternalItems[2], selectorItems), "external SDK item template selector default selected template");
                    AssertTemplateText(defaultTemplate, window.ExternalItems[2], "Default template Data", "external SDK default selected template text");
                }

                private static void ValidatePackResources()
                {
                    AssertEqual(
                        "External SDK pack resource text",
                        ReadPackResourceText(new Uri("Assets/ExternalResource.txt", UriKind.Relative)),
                        "external SDK relative Resource stream text");
                    AssertEqual(
                        "External SDK pack resource text",
                        ReadPackResourceText(new Uri("pack://application:,,,/Assets/ExternalResource.txt", UriKind.Absolute)),
                        "external SDK absolute pack Resource stream text");
                }

                private static string ReadPackResourceText(Uri resourceUri)
                {
                    var resourceInfo = Application.GetResourceStream(resourceUri)
                        ?? throw new InvalidOperationException($"Expected external SDK resource stream for '{resourceUri}'.");
                    using var reader = new StreamReader(resourceInfo.Stream);
                    return reader.ReadToEnd();
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

                private static void ValidateLauncher()
                {
                    Type serviceType = typeof(Application).Assembly.GetType(
                            "System.Windows.PortableLauncherService",
                            throwOnError: false)
                        ?? throw new TypeLoadException("System.Windows.PortableLauncherService");
                    var isEnabledProperty = serviceType.GetProperty(
                            "IsEnabled",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? throw new MissingMemberException(serviceType.FullName, "IsEnabled");
                    if (!OperatingSystem.IsWindows())
                    {
                        AssertEqual(
                            true,
                            (bool)(isEnabledProperty.GetValue(null) ?? false),
                            "external SDK portable launcher service enabled");
                    }

                    var registerMethod = serviceType.GetMethod(
                            "Register",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            binder: null,
                            types: new[] { typeof(Func<object, bool>) },
                            modifiers: null)
                        ?? throw new MissingMethodException(serviceType.FullName, "Register");
                    var tryLaunchMethod = serviceType.GetMethod(
                            "TryLaunch",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            binder: null,
                            types:
                            [
                                typeof(Uri),
                                typeof(string),
                                typeof(bool),
                                typeof(bool).MakeByRefType()
                            ],
                            modifiers: null)
                        ?? throw new MissingMethodException(serviceType.FullName, "TryLaunch");

                    int requestCount = 0;
                    string? requestUri = null;
                    string? requestTargetFrame = null;
                    string? requestIsTopLevel = null;
                    Func<object, bool> handler = request =>
                    {
                        requestCount++;
                        requestUri = ReadPortableRequestString(request, "Uri");
                        requestTargetFrame = ReadPortableRequestString(request, "TargetFrame");
                        requestIsTopLevel = ReadPortableRequestString(request, "IsTopLevel");
                        return true;
                    };

                    IDisposable? registration = null;
                    try
                    {
                        registration = (IDisposable?)registerMethod.Invoke(null, new object[] { handler });
                        object?[] launchArguments =
                        [
                            new Uri("https://example.test/external-sdk-launch"),
                            "ExternalTargetFrame",
                            true,
                            false
                        ];

                        AssertEqual(
                            true,
                            (bool)(tryLaunchMethod.Invoke(null, launchArguments) ?? false),
                            "external SDK portable launcher handled request");
                        AssertEqual(true, (bool)(launchArguments[3] ?? false), "external SDK portable launcher launched state");
                        AssertEqual(1, requestCount, "external SDK portable launcher request count");
                        AssertEqual("https://example.test/external-sdk-launch", requestUri, "external SDK portable launcher request URI");
                        AssertEqual("ExternalTargetFrame", requestTargetFrame, "external SDK portable launcher target frame");
                        AssertEqual(bool.TrueString, requestIsTopLevel, "external SDK portable launcher top-level flag");
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

                    var customDataObject = new DataObject();
                    customDataObject.SetData(DataFormats.UnicodeText, "external SDK data object text", autoConvert: false);
                    customDataObject.SetData("ExternalSdkCustomFormat", "external SDK custom payload", autoConvert: false);
                    Clipboard.SetDataObject(customDataObject, copy: true);
                    AssertEqual(true, Clipboard.ContainsText(), "external SDK Clipboard data object text state");
                    AssertEqual(
                        "external SDK data object text",
                        Clipboard.GetText(),
                        "external SDK Clipboard data object text");
                    AssertEqual(
                        "external SDK custom payload",
                        Clipboard.GetData("ExternalSdkCustomFormat"),
                        "external SDK Clipboard custom data format");
                    var currentDataObject = RequireType<DataObject>(
                        Clipboard.GetDataObject(),
                        "external SDK Clipboard current data object after SetDataObject");
                    AssertEqual(true, currentDataObject.GetDataPresent("ExternalSdkCustomFormat", autoConvert: false), "external SDK Clipboard custom format present");
                    AssertEqual(
                        "external SDK custom payload",
                        currentDataObject.GetData("ExternalSdkCustomFormat", autoConvert: false),
                        "external SDK Clipboard custom data object payload");
                    AssertEqual(
                        true,
                        currentDataObject.TryGetData("ExternalSdkCustomFormat", autoConvert: false, out string typedCustomPayload),
                        "external SDK Clipboard typed custom data retrieval state");
                    AssertEqual(
                        "external SDK custom payload",
                        typedCustomPayload,
                        "external SDK Clipboard typed custom data retrieval");
                    AssertEqual(true, Clipboard.IsCurrent(currentDataObject), "external SDK Clipboard SetDataObject current state");

                    var fileDropList = new StringCollection
                    {
                        "/tmp/external-sdk-alpha.txt",
                        "/tmp/external-sdk-beta.txt"
                    };
                    Clipboard.SetFileDropList(fileDropList);
                    AssertEqual(true, Clipboard.ContainsFileDropList(), "external SDK Clipboard file-drop state");
                    var roundTripFileDropList = Clipboard.GetFileDropList();
                    AssertEqual(2, roundTripFileDropList.Count, "external SDK Clipboard file-drop count");
                    AssertEqual("/tmp/external-sdk-alpha.txt", roundTripFileDropList[0], "external SDK Clipboard first file-drop item");
                    AssertEqual("/tmp/external-sdk-beta.txt", roundTripFileDropList[1], "external SDK Clipboard second file-drop item");

                    Clipboard.Clear();
                    AssertEqual(false, Clipboard.ContainsText(), "external SDK Clipboard cleared text state");
                    AssertEqual(false, Clipboard.ContainsFileDropList(), "external SDK Clipboard cleared file-drop state");
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

                private static void ValidateManagedFrameworkCollections()
                {
                    Assembly presentationFramework = typeof(Window).Assembly;

                    Type listOfObjectType = presentationFramework.GetType("MS.Internal.ListOfObject", throwOnError: true)!;
                    var backingList = new System.Collections.ArrayList
                    {
                        "alpha",
                        "bravo"
                    };
                    var list = RequireType<IList<object>>(
                        Activator.CreateInstance(
                            listOfObjectType,
                            BindingFlags.Instance | BindingFlags.NonPublic,
                            binder: null,
                            args: new object[] { backingList },
                            culture: null),
                        "external SDK ListOfObject wrapper");
                    AssertEqual(2, list.Count, "external SDK ListOfObject initial count");
                    AssertEqual("alpha", list[0], "external SDK ListOfObject index getter");
                    AssertEqual(1, list.IndexOf("bravo"), "external SDK ListOfObject index lookup");
                    AssertEqual(false, list.IsReadOnly, "external SDK ListOfObject mutable state");
                    list.Insert(1, "charlie");
                    AssertEqual("charlie", backingList[1], "external SDK ListOfObject insert forwards to IList");
                    list[2] = "delta";
                    AssertEqual("delta", backingList[2], "external SDK ListOfObject index setter forwards to IList");
                    list.Add("echo");
                    AssertEqual(4, backingList.Count, "external SDK ListOfObject add forwards to IList");
                    AssertEqual(true, list.Remove("charlie"), "external SDK ListOfObject remove existing state");
                    AssertEqual(false, list.Contains("charlie"), "external SDK ListOfObject remove existing value");
                    list.RemoveAt(0);
                    AssertEqual("delta", backingList[0], "external SDK ListOfObject remove-at forwards to IList");
                    object[] copiedListItems = new object[4];
                    list.CopyTo(copiedListItems, 1);
                    AssertEqual("delta", copiedListItems[1], "external SDK ListOfObject copy first value");
                    AssertEqual("echo", copiedListItems[2], "external SDK ListOfObject copy second value");
                    list.Clear();
                    AssertEqual(0, backingList.Count, "external SDK ListOfObject clear forwards to IList");

                    Type weakDictionaryType = presentationFramework
                        .GetType("MS.Internal.WeakDictionary`2", throwOnError: true)!
                        .MakeGenericType(typeof(object), typeof(string));
                    var weakDictionary = RequireType<IDictionary<object, string>>(
                        Activator.CreateInstance(weakDictionaryType),
                        "external SDK WeakDictionary instance");
                    object firstKey = new();
                    object secondKey = new();
                    weakDictionary.Add(firstKey, "first");
                    weakDictionary.Add(secondKey, "second");
                    AssertEqual(2, weakDictionary.Count, "external SDK WeakDictionary count");
                    AssertEqual(true, weakDictionary.ContainsKey(firstKey), "external SDK WeakDictionary contains key");
                    AssertEqual(true, weakDictionary.TryGetValue(secondKey, out string? secondValue), "external SDK WeakDictionary try-get state");
                    AssertEqual("second", secondValue ?? string.Empty, "external SDK WeakDictionary try-get value");
                    AssertEqual("updated", weakDictionary[firstKey] = "updated", "external SDK WeakDictionary index setter return");
                    AssertEqual("updated", weakDictionary[firstKey], "external SDK WeakDictionary index getter");

                    object[] copiedKeys = new object[3];
                    weakDictionary.Keys.CopyTo(copiedKeys, 1);
                    AssertEqual(null, copiedKeys[0], "external SDK WeakDictionary key copy offset sentinel");
                    AssertEqual(true, copiedKeys.Contains(firstKey), "external SDK WeakDictionary key copy first key");
                    AssertEqual(true, copiedKeys.Contains(secondKey), "external SDK WeakDictionary key copy second key");
                    AssertEqual(true, weakDictionary.Keys.Contains(firstKey), "external SDK WeakDictionary key collection contains");
                    AssertEqual(true, weakDictionary.Keys.IsReadOnly, "external SDK WeakDictionary key collection read-only");

                    string[] copiedValues = new string[3];
                    weakDictionary.Values.CopyTo(copiedValues, 1);
                    AssertEqual(null, copiedValues[0], "external SDK WeakDictionary value copy offset sentinel");
                    AssertEqual(true, copiedValues.Contains("updated"), "external SDK WeakDictionary value copy updated value");
                    AssertEqual(true, copiedValues.Contains("second"), "external SDK WeakDictionary value copy second value");
                    AssertEqual(true, weakDictionary.Values.Contains("updated"), "external SDK WeakDictionary value collection contains");
                    AssertEqual(false, weakDictionary.Values.Contains("missing"), "external SDK WeakDictionary value collection missing state");
                    AssertEqual(true, weakDictionary.Values.IsReadOnly, "external SDK WeakDictionary value collection read-only");

                    bool keyAddThrew = false;
                    try
                    {
                        weakDictionary.Keys.Add(new object());
                    }
                    catch (NotSupportedException)
                    {
                        keyAddThrew = true;
                    }

                    AssertEqual(true, keyAddThrew, "external SDK WeakDictionary key collection add rejected");

                    bool valueClearThrew = false;
                    try
                    {
                        weakDictionary.Values.Clear();
                    }
                    catch (NotSupportedException)
                    {
                        valueClearThrew = true;
                    }

                    AssertEqual(true, valueClearThrew, "external SDK WeakDictionary value collection clear rejected");
                    AssertEqual(true, weakDictionary.Remove(secondKey), "external SDK WeakDictionary remove existing key");
                    AssertEqual(false, weakDictionary.ContainsKey(secondKey), "external SDK WeakDictionary removed key state");
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

                    AssertEqual(2, BitmapPalettes.BlackAndWhite.Colors.Count, "external SDK BitmapPalettes.BlackAndWhite color count");
                    AssertEqual(Color.FromRgb(0x00, 0x00, 0x00), BitmapPalettes.BlackAndWhite.Colors[0], "external SDK BitmapPalettes.BlackAndWhite first color");
                    AssertEqual(Color.FromRgb(0xFF, 0xFF, 0xFF), BitmapPalettes.BlackAndWhite.Colors[1], "external SDK BitmapPalettes.BlackAndWhite final color");
                    AssertEqual(4, BitmapPalettes.Gray4.Colors.Count, "external SDK BitmapPalettes.Gray4 color count");
                    AssertEqual(Color.FromRgb(0x55, 0x55, 0x55), BitmapPalettes.Gray4.Colors[1], "external SDK BitmapPalettes.Gray4 second color");
                    AssertEqual(216, BitmapPalettes.WebPalette.Colors.Count, "external SDK BitmapPalettes.WebPalette color count");
                    AssertEqual(Color.FromRgb(0x00, 0x00, 0x00), BitmapPalettes.WebPalette.Colors[0], "external SDK BitmapPalettes.WebPalette first color");
                    AssertEqual(Color.FromRgb(0xFF, 0xFF, 0xFF), BitmapPalettes.WebPalette.Colors[215], "external SDK BitmapPalettes.WebPalette final color");

                    var generatedPalette = new BitmapPalette(bitmapSource, 4);
                    AssertEqual(4, generatedPalette.Colors.Count, "external SDK BitmapPalette from BGRA source color count");
                    AssertEqual(Color.FromArgb(0xFF, 0x30, 0x20, 0x10), generatedPalette.Colors[0], "external SDK BitmapPalette from BGRA source first color");
                    AssertEqual(Color.FromArgb(0xFF, 0xC0, 0xB0, 0xA0), generatedPalette.Colors[3], "external SDK BitmapPalette from BGRA source final color");

                    var indexedPaletteSource = BitmapSource.Create(
                        2,
                        2,
                        96.0,
                        96.0,
                        PixelFormats.Indexed8,
                        BitmapPalettes.Gray256,
                        new byte[] { 0, 1, 2, 3 },
                        2);
                    AssertEqual(256, indexedPaletteSource.Palette.Colors.Count, "external SDK Indexed8 source palette color count");
                    var copiedIndexedPalette = new BitmapPalette(indexedPaletteSource, 3);
                    AssertEqual(3, copiedIndexedPalette.Colors.Count, "external SDK BitmapPalette from Indexed8 source color count");
                    AssertEqual(BitmapPalettes.Gray256.Colors[2], copiedIndexedPalette.Colors[2], "external SDK BitmapPalette from Indexed8 source third color");

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

                    byte[] pngBytes = CreateRgbaPngBytes(pixels, 2, 2, 8);
                    AssertEqual((byte)0x89, pngBytes[0], "external SDK generated PNG signature byte 0");
                    AssertEqual((byte)'P', pngBytes[1], "external SDK generated PNG signature byte 1");
                    var pngDecoder = BitmapDecoder.Create(
                        new MemoryStream(pngBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(PngBitmapDecoder), pngDecoder.GetType(), "external SDK BitmapDecoder.Create PNG decoder type");
                    AssertEqual(1, pngDecoder.Frames.Count, "external SDK BitmapDecoder.Create PNG frame count");
                    AssertEqual(2, pngDecoder.Frames[0].PixelWidth, "external SDK BitmapDecoder.Create PNG pixel width");
                    AssertEqual(2, pngDecoder.Frames[0].PixelHeight, "external SDK BitmapDecoder.Create PNG pixel height");
                    AssertEqual(PixelFormats.Bgra32, pngDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create PNG Bgra32 format");
                    var decodedPngPixels = new byte[pixels.Length];
                    pngDecoder.Frames[0].CopyPixels(decodedPngPixels, 8, 0);
                    AssertEqual(pixels[0], decodedPngPixels[0], "external SDK BitmapDecoder.Create PNG top-left blue byte");
                    AssertEqual(pixels[14], decodedPngPixels[14], "external SDK BitmapDecoder.Create PNG bottom-right red byte");

                    var directPngDecoder = new PngBitmapDecoder(
                        new MemoryStream(pngBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(1, directPngDecoder.Frames.Count, "external SDK PngBitmapDecoder frame count");
                    AssertEqual(PixelFormats.Bgra32, directPngDecoder.Frames[0].Format, "external SDK PngBitmapDecoder Bgra32 format");

                    byte[] interlacedPngBytes = CreateAdam7RgbaPngBytes(pixels, 2, 2, 8);
                    var interlacedPngDecoder = BitmapDecoder.Create(
                        new MemoryStream(interlacedPngBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(PngBitmapDecoder), interlacedPngDecoder.GetType(), "external SDK BitmapDecoder.Create interlaced PNG decoder type");
                    AssertEqual(1, interlacedPngDecoder.Frames.Count, "external SDK BitmapDecoder.Create interlaced PNG frame count");
                    AssertEqual(2, interlacedPngDecoder.Frames[0].PixelWidth, "external SDK BitmapDecoder.Create interlaced PNG pixel width");
                    AssertEqual(2, interlacedPngDecoder.Frames[0].PixelHeight, "external SDK BitmapDecoder.Create interlaced PNG pixel height");
                    AssertEqual(PixelFormats.Bgra32, interlacedPngDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create interlaced PNG Bgra32 format");
                    var decodedInterlacedPngPixels = new byte[pixels.Length];
                    interlacedPngDecoder.Frames[0].CopyPixels(decodedInterlacedPngPixels, 8, 0);
                    AssertEqual(pixels[0], decodedInterlacedPngPixels[0], "external SDK BitmapDecoder.Create interlaced PNG top-left blue byte");
                    AssertEqual(pixels[14], decodedInterlacedPngPixels[14], "external SDK BitmapDecoder.Create interlaced PNG bottom-right red byte");

                    byte[] iconBytes = CreatePngIconBytes(pngBytes, 2, 2);
                    AssertEqual((byte)0x00, iconBytes[0], "external SDK generated ICO reserved byte 0");
                    AssertEqual((byte)0x01, iconBytes[2], "external SDK generated ICO type byte 0");
                    AssertEqual((byte)0x02, iconBytes[6], "external SDK generated ICO directory width byte");
                    var iconDecoder = BitmapDecoder.Create(
                        new MemoryStream(iconBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(IconBitmapDecoder), iconDecoder.GetType(), "external SDK BitmapDecoder.Create ICO decoder type");
                    AssertEqual(1, iconDecoder.Frames.Count, "external SDK BitmapDecoder.Create ICO frame count");
                    AssertEqual(2, iconDecoder.Frames[0].PixelWidth, "external SDK BitmapDecoder.Create ICO pixel width");
                    AssertEqual(2, iconDecoder.Frames[0].PixelHeight, "external SDK BitmapDecoder.Create ICO pixel height");
                    AssertEqual(PixelFormats.Bgra32, iconDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create ICO Bgra32 format");
                    var decodedIconPixels = new byte[pixels.Length];
                    iconDecoder.Frames[0].CopyPixels(decodedIconPixels, 8, 0);
                    AssertEqual(pixels[0], decodedIconPixels[0], "external SDK BitmapDecoder.Create ICO top-left blue byte");
                    AssertEqual(pixels[14], decodedIconPixels[14], "external SDK BitmapDecoder.Create ICO bottom-right red byte");

                    var directIconDecoder = new IconBitmapDecoder(
                        new MemoryStream(iconBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(1, directIconDecoder.Frames.Count, "external SDK IconBitmapDecoder frame count");
                    AssertEqual(2, directIconDecoder.Frames[0].PixelWidth, "external SDK IconBitmapDecoder pixel width");
                    AssertEqual(PixelFormats.Bgra32, directIconDecoder.Frames[0].Format, "external SDK IconBitmapDecoder Bgra32 format");

                    byte[] dibIconBytes = CreateDibIconBytes(pixels, 2, 2, 8);
                    var dibIconDecoder = BitmapDecoder.Create(
                        new MemoryStream(dibIconBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(IconBitmapDecoder), dibIconDecoder.GetType(), "external SDK BitmapDecoder.Create DIB ICO decoder type");
                    AssertEqual(1, dibIconDecoder.Frames.Count, "external SDK BitmapDecoder.Create DIB ICO frame count");
                    AssertEqual(2, dibIconDecoder.Frames[0].PixelWidth, "external SDK BitmapDecoder.Create DIB ICO pixel width");
                    AssertEqual(2, dibIconDecoder.Frames[0].PixelHeight, "external SDK BitmapDecoder.Create DIB ICO pixel height");
                    AssertEqual(PixelFormats.Bgra32, dibIconDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create DIB ICO Bgra32 format");
                    var decodedDibIconPixels = new byte[pixels.Length];
                    dibIconDecoder.Frames[0].CopyPixels(decodedDibIconPixels, 8, 0);
                    AssertEqual(pixels[0], decodedDibIconPixels[0], "external SDK BitmapDecoder.Create DIB ICO top-left blue byte");
                    AssertEqual(pixels[14], decodedDibIconPixels[14], "external SDK BitmapDecoder.Create DIB ICO bottom-right red byte");
                    AssertEqual((byte)0x00, decodedDibIconPixels[15], "external SDK BitmapDecoder.Create DIB ICO masked alpha byte");

                    var directDibIconDecoder = new IconBitmapDecoder(
                        new MemoryStream(dibIconBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(1, directDibIconDecoder.Frames.Count, "external SDK DIB IconBitmapDecoder frame count");
                    AssertEqual(2, directDibIconDecoder.Frames[0].PixelWidth, "external SDK DIB IconBitmapDecoder pixel width");

                    byte[] jpegBytes = CreateJpegBytes();
                    AssertEqual((byte)0xFF, jpegBytes[0], "external SDK generated JPEG signature byte 0");
                    AssertEqual((byte)0xD8, jpegBytes[1], "external SDK generated JPEG signature byte 1");
                    AssertEqual((byte)0xFF, jpegBytes[2], "external SDK generated JPEG signature byte 2");
                    var jpegDecoder = BitmapDecoder.Create(
                        new MemoryStream(jpegBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(JpegBitmapDecoder), jpegDecoder.GetType(), "external SDK BitmapDecoder.Create JPEG decoder type");
                    AssertEqual(1, jpegDecoder.Frames.Count, "external SDK BitmapDecoder.Create JPEG frame count");
                    AssertEqual(2, jpegDecoder.Frames[0].PixelWidth, "external SDK BitmapDecoder.Create JPEG pixel width");
                    AssertEqual(2, jpegDecoder.Frames[0].PixelHeight, "external SDK BitmapDecoder.Create JPEG pixel height");
                    AssertEqual(PixelFormats.Bgra32, jpegDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create JPEG Bgra32 format");
                    var decodedJpegPixels = new byte[pixels.Length];
                    jpegDecoder.Frames[0].CopyPixels(decodedJpegPixels, 8, 0);
                    int jpegRgbTotal = 0;
                    for (int offset = 0; offset < decodedJpegPixels.Length; offset += 4)
                    {
                        jpegRgbTotal += decodedJpegPixels[offset] + decodedJpegPixels[offset + 1] + decodedJpegPixels[offset + 2];
                        AssertEqual((byte)0xFF, decodedJpegPixels[offset + 3], "external SDK BitmapDecoder.Create JPEG alpha byte " + offset / 4);
                    }

                    AssertAtLeast(1, jpegRgbTotal, "external SDK BitmapDecoder.Create JPEG nonblank RGB total");

                    var directJpegDecoder = new JpegBitmapDecoder(
                        new MemoryStream(jpegBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(1, directJpegDecoder.Frames.Count, "external SDK JpegBitmapDecoder frame count");
                    AssertEqual(2, directJpegDecoder.Frames[0].PixelWidth, "external SDK JpegBitmapDecoder pixel width");
                    AssertEqual(PixelFormats.Bgra32, directJpegDecoder.Frames[0].Format, "external SDK JpegBitmapDecoder Bgra32 format");

                    byte[] gifBytes = CreateGifBytes();
                    AssertEqual((byte)'G', gifBytes[0], "external SDK generated GIF signature byte 0");
                    AssertEqual((byte)'I', gifBytes[1], "external SDK generated GIF signature byte 1");
                    AssertEqual((byte)'F', gifBytes[2], "external SDK generated GIF signature byte 2");
                    var gifDecoder = BitmapDecoder.Create(
                        new MemoryStream(gifBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(GifBitmapDecoder), gifDecoder.GetType(), "external SDK BitmapDecoder.Create GIF decoder type");
                    AssertEqual(2, gifDecoder.Frames.Count, "external SDK BitmapDecoder.Create GIF frame count");
                    AssertEqual(2, gifDecoder.Frames[0].PixelWidth, "external SDK BitmapDecoder.Create GIF pixel width");
                    AssertEqual(2, gifDecoder.Frames[0].PixelHeight, "external SDK BitmapDecoder.Create GIF pixel height");
                    AssertEqual(PixelFormats.Bgra32, gifDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create GIF Bgra32 format");
                    AssertEqual(PixelFormats.Bgra32, gifDecoder.Frames[1].Format, "external SDK BitmapDecoder.Create GIF second frame Bgra32 format");
                    var firstGifMetadata = RequireType<BitmapMetadata>(
                        gifDecoder.Frames[0].Metadata,
                        "external SDK BitmapDecoder.Create GIF first-frame metadata");
                    var secondGifMetadata = RequireType<BitmapMetadata>(
                        gifDecoder.Frames[1].Metadata,
                        "external SDK BitmapDecoder.Create GIF second-frame metadata");
                    AssertEqual("gif", firstGifMetadata.Format, "external SDK BitmapDecoder.Create GIF metadata format");
                    AssertEqual(true, firstGifMetadata.IsReadOnly, "external SDK BitmapDecoder.Create GIF metadata read-only state");
                    AssertEqual(true, firstGifMetadata.ContainsQuery("/grctlext/Delay"), "external SDK BitmapDecoder.Create GIF delay query presence");
                    AssertEqual((ushort)5, firstGifMetadata.GetQuery("/grctlext/Delay"), "external SDK BitmapDecoder.Create GIF first-frame delay metadata");
                    AssertEqual((ushort)7, secondGifMetadata.GetQuery("/grctlext/Delay"), "external SDK BitmapDecoder.Create GIF second-frame delay metadata");
                    AssertEqual((byte)1, firstGifMetadata.GetQuery("/grctlext/Disposal"), "external SDK BitmapDecoder.Create GIF disposal metadata");
                    AssertEqual((ushort)2, firstGifMetadata.GetQuery("/imgdesc/Width"), "external SDK BitmapDecoder.Create GIF image descriptor width metadata");
                    AssertEqual(false, firstGifMetadata.GetQuery("/imgdesc/InterlaceFlag"), "external SDK BitmapDecoder.Create GIF interlace metadata");
                    var decodedGifPixels = new byte[pixels.Length];
                    gifDecoder.Frames[0].CopyPixels(decodedGifPixels, 8, 0);
                    var decodedSecondGifPixels = new byte[pixels.Length];
                    gifDecoder.Frames[1].CopyPixels(decodedSecondGifPixels, 8, 0);
                    int gifRgbTotal = 0;
                    for (int offset = 0; offset < decodedGifPixels.Length; offset += 4)
                    {
                        gifRgbTotal += decodedGifPixels[offset] + decodedGifPixels[offset + 1] + decodedGifPixels[offset + 2];
                        AssertEqual((byte)0xFF, decodedGifPixels[offset + 3], "external SDK BitmapDecoder.Create GIF alpha byte " + offset / 4);
                        AssertEqual((byte)0xFF, decodedSecondGifPixels[offset + 3], "external SDK BitmapDecoder.Create GIF second-frame alpha byte " + offset / 4);
                    }

                    AssertAtLeast(1, gifRgbTotal, "external SDK BitmapDecoder.Create GIF nonblank RGB total");
                    AssertEqual((byte)0xFF, decodedGifPixels[2], "external SDK BitmapDecoder.Create GIF first-frame red byte");
                    AssertEqual((byte)0xFF, decodedSecondGifPixels[1], "external SDK BitmapDecoder.Create GIF second-frame green byte");

                    var directGifDecoder = new GifBitmapDecoder(
                        new MemoryStream(gifBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(2, directGifDecoder.Frames.Count, "external SDK GifBitmapDecoder frame count");
                    AssertEqual(2, directGifDecoder.Frames[0].PixelWidth, "external SDK GifBitmapDecoder pixel width");
                    AssertEqual(2, directGifDecoder.Frames[1].PixelHeight, "external SDK GifBitmapDecoder second-frame pixel height");
                    AssertEqual(PixelFormats.Bgra32, directGifDecoder.Frames[0].Format, "external SDK GifBitmapDecoder Bgra32 format");
                    var directGifMetadata = RequireType<BitmapMetadata>(
                        directGifDecoder.Frames[1].Metadata,
                        "external SDK GifBitmapDecoder second-frame metadata");
                    AssertEqual((ushort)7, directGifMetadata.GetQuery("/grctlext/Delay"), "external SDK GifBitmapDecoder second-frame delay metadata");

                    byte[] tiffBytes = CreateTiffBytes(pixels, 2, 2);
                    AssertEqual((byte)'I', tiffBytes[0], "external SDK generated TIFF byte order byte 0");
                    AssertEqual((byte)'I', tiffBytes[1], "external SDK generated TIFF byte order byte 1");
                    var tiffDecoder = BitmapDecoder.Create(
                        new MemoryStream(tiffBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(TiffBitmapDecoder), tiffDecoder.GetType(), "external SDK BitmapDecoder.Create TIFF decoder type");
                    AssertEqual(1, tiffDecoder.Frames.Count, "external SDK BitmapDecoder.Create TIFF frame count");
                    AssertEqual(2, tiffDecoder.Frames[0].PixelWidth, "external SDK BitmapDecoder.Create TIFF pixel width");
                    AssertEqual(2, tiffDecoder.Frames[0].PixelHeight, "external SDK BitmapDecoder.Create TIFF pixel height");
                    AssertEqual(PixelFormats.Bgra32, tiffDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create TIFF Bgra32 format");
                    var tiffMetadata = RequireType<BitmapMetadata>(
                        tiffDecoder.Frames[0].Metadata,
                        "external SDK BitmapDecoder.Create TIFF metadata");
                    AssertEqual("tiff", tiffMetadata.Format, "external SDK BitmapDecoder.Create TIFF metadata format");
                    AssertEqual(true, tiffMetadata.ContainsQuery("/ifd/{ushort=274}"), "external SDK BitmapDecoder.Create TIFF orientation query presence");
                    AssertEqual((ushort)6, tiffMetadata.GetQuery("/ifd/{ushort=274}"), "external SDK BitmapDecoder.Create TIFF orientation metadata");
                    var decodedTiffPixels = new byte[pixels.Length];
                    tiffDecoder.Frames[0].CopyPixels(decodedTiffPixels, 8, 0);
                    AssertEqual(pixels[0], decodedTiffPixels[0], "external SDK BitmapDecoder.Create TIFF top-left blue byte");
                    AssertEqual(pixels[5], decodedTiffPixels[5], "external SDK BitmapDecoder.Create TIFF second green byte");
                    AssertEqual(pixels[14], decodedTiffPixels[14], "external SDK BitmapDecoder.Create TIFF bottom-right red byte");

                    var directTiffDecoder = new TiffBitmapDecoder(
                        new MemoryStream(tiffBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(1, directTiffDecoder.Frames.Count, "external SDK TiffBitmapDecoder frame count");
                    AssertEqual(2, directTiffDecoder.Frames[0].PixelWidth, "external SDK TiffBitmapDecoder pixel width");
                    AssertEqual(PixelFormats.Bgra32, directTiffDecoder.Frames[0].Format, "external SDK TiffBitmapDecoder Bgra32 format");
                    var directTiffMetadata = RequireType<BitmapMetadata>(
                        directTiffDecoder.Frames[0].Metadata,
                        "external SDK TiffBitmapDecoder metadata");
                    AssertEqual((ushort)6, directTiffMetadata.GetQuery("/ifd/{ushort=274}"), "external SDK TiffBitmapDecoder orientation metadata");

                    byte[] secondTiffPixels =
                    [
                        0xA0, 0xB0, 0xC0, 0xFF,
                        0x70, 0x80, 0x90, 0xFF,
                        0x40, 0x50, 0x60, 0xFF,
                        0x10, 0x20, 0x30, 0xFF
                    ];
                    byte[] multiFrameTiffBytes = CreateMultiFrameTiffBytes(pixels, secondTiffPixels, 2, 2);
                    var multiFrameTiffDecoder = BitmapDecoder.Create(
                        new MemoryStream(multiFrameTiffBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(TiffBitmapDecoder), multiFrameTiffDecoder.GetType(), "external SDK BitmapDecoder.Create multi-frame TIFF decoder type");
                    AssertEqual(2, multiFrameTiffDecoder.Frames.Count, "external SDK BitmapDecoder.Create multi-frame TIFF frame count");
                    AssertEqual(2, multiFrameTiffDecoder.Frames[1].PixelWidth, "external SDK BitmapDecoder.Create multi-frame TIFF second pixel width");
                    AssertEqual(PixelFormats.Bgra32, multiFrameTiffDecoder.Frames[1].Format, "external SDK BitmapDecoder.Create multi-frame TIFF second Bgra32 format");
                    var decodedSecondTiffPixels = new byte[secondTiffPixels.Length];
                    multiFrameTiffDecoder.Frames[1].CopyPixels(decodedSecondTiffPixels, 8, 0);
                    AssertEqual(secondTiffPixels[0], decodedSecondTiffPixels[0], "external SDK BitmapDecoder.Create multi-frame TIFF second top-left blue byte");
                    AssertEqual(secondTiffPixels[14], decodedSecondTiffPixels[14], "external SDK BitmapDecoder.Create multi-frame TIFF second bottom-right red byte");

                    var directMultiFrameTiffDecoder = new TiffBitmapDecoder(
                        new MemoryStream(multiFrameTiffBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(2, directMultiFrameTiffDecoder.Frames.Count, "external SDK multi-frame TiffBitmapDecoder frame count");
                    AssertEqual(2, directMultiFrameTiffDecoder.Frames[1].PixelHeight, "external SDK multi-frame TiffBitmapDecoder second pixel height");

                    byte[] paletteTiffBytes = CreatePaletteTiffBytes([0, 1, 2, 3], 2, 2, 4);
                    var paletteTiffDecoder = BitmapDecoder.Create(
                        new MemoryStream(paletteTiffBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(TiffBitmapDecoder), paletteTiffDecoder.GetType(), "external SDK BitmapDecoder.Create palette TIFF decoder type");
                    AssertEqual(1, paletteTiffDecoder.Frames.Count, "external SDK BitmapDecoder.Create palette TIFF frame count");
                    AssertEqual(2, paletteTiffDecoder.Frames[0].PixelWidth, "external SDK BitmapDecoder.Create palette TIFF pixel width");
                    AssertEqual(2, paletteTiffDecoder.Frames[0].PixelHeight, "external SDK BitmapDecoder.Create palette TIFF pixel height");
                    AssertEqual(PixelFormats.Bgra32, paletteTiffDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create palette TIFF Bgra32 format");
                    var decodedPaletteTiffPixels = new byte[pixels.Length];
                    paletteTiffDecoder.Frames[0].CopyPixels(decodedPaletteTiffPixels, 8, 0);
                    AssertEqual(pixels[0], decodedPaletteTiffPixels[0], "external SDK BitmapDecoder.Create palette TIFF top-left blue byte");
                    AssertEqual(pixels[5], decodedPaletteTiffPixels[5], "external SDK BitmapDecoder.Create palette TIFF second green byte");
                    AssertEqual(pixels[14], decodedPaletteTiffPixels[14], "external SDK BitmapDecoder.Create palette TIFF bottom-right red byte");

                    var directPaletteTiffDecoder = new TiffBitmapDecoder(
                        new MemoryStream(paletteTiffBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(1, directPaletteTiffDecoder.Frames.Count, "external SDK palette TiffBitmapDecoder frame count");
                    AssertEqual(2, directPaletteTiffDecoder.Frames[0].PixelWidth, "external SDK palette TiffBitmapDecoder pixel width");
                    AssertEqual(PixelFormats.Bgra32, directPaletteTiffDecoder.Frames[0].Format, "external SDK palette TiffBitmapDecoder Bgra32 format");

                    byte[] rgba16PngBytes = CreateRgba16PngBytes(pixels, 2, 2, 8);
                    var rgba16PngDecoder = BitmapDecoder.Create(
                        new MemoryStream(rgba16PngBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(PngBitmapDecoder), rgba16PngDecoder.GetType(), "external SDK BitmapDecoder.Create 16-bit RGBA PNG decoder type");
                    AssertEqual(PixelFormats.Bgra32, rgba16PngDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create 16-bit RGBA PNG Bgra32 format");
                    var decodedRgba16Pixels = new byte[pixels.Length];
                    rgba16PngDecoder.Frames[0].CopyPixels(decodedRgba16Pixels, 8, 0);
                    AssertEqual(pixels[0], decodedRgba16Pixels[0], "external SDK BitmapDecoder.Create 16-bit RGBA PNG top-left blue byte");
                    AssertEqual(pixels[14], decodedRgba16Pixels[14], "external SDK BitmapDecoder.Create 16-bit RGBA PNG bottom-right red byte");

                    Color[] indexed4Palette =
                    [
                        Color.FromRgb(0x00, 0x00, 0x00),
                        Color.FromRgb(0xCC, 0x22, 0x22),
                        Color.FromRgb(0x22, 0xAA, 0x44),
                        Color.FromRgb(0x22, 0x44, 0xCC)
                    ];
                    byte[] indexed4PngBytes = CreateIndexedPngBytes(
                        [0, 1, 2, 3],
                        indexed4Palette,
                        [0xFF, 0xFF, 0x80, 0xFF],
                        2,
                        2,
                        4);
                    var indexed4PngDecoder = BitmapDecoder.Create(
                        new MemoryStream(indexed4PngBytes),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(PngBitmapDecoder), indexed4PngDecoder.GetType(), "external SDK BitmapDecoder.Create Indexed4 PNG decoder type");
                    AssertEqual(PixelFormats.Bgra32, indexed4PngDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create Indexed4 PNG Bgra32 format");
                    var decodedIndexed4PngPixels = new byte[pixels.Length];
                    indexed4PngDecoder.Frames[0].CopyPixels(decodedIndexed4PngPixels, 8, 0);
                    AssertEqual((byte)0x44, decodedIndexed4PngPixels[8], "external SDK BitmapDecoder.Create Indexed4 PNG bottom-left blue byte");
                    AssertEqual((byte)0xAA, decodedIndexed4PngPixels[9], "external SDK BitmapDecoder.Create Indexed4 PNG bottom-left green byte");
                    AssertEqual((byte)0x22, decodedIndexed4PngPixels[10], "external SDK BitmapDecoder.Create Indexed4 PNG bottom-left red byte");
                    AssertEqual((byte)0x80, decodedIndexed4PngPixels[11], "external SDK BitmapDecoder.Create Indexed4 PNG bottom-left alpha byte");

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

                    string pngPath = Path.Combine(Path.GetTempPath(), "external-sdk-managed-image-" + Guid.NewGuid().ToString("N") + ".png");
                    File.WriteAllBytes(pngPath, pngBytes);
                    try
                    {
                        var pngUri = new Uri(pngPath);
                        var uriPngDecoder = BitmapDecoder.Create(
                            pngUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(typeof(PngBitmapDecoder), uriPngDecoder.GetType(), "external SDK BitmapDecoder.Create URI PNG decoder type");
                        AssertEqual(1, uriPngDecoder.Frames.Count, "external SDK BitmapDecoder.Create URI PNG frame count");
                        AssertEqual(PixelFormats.Bgra32, uriPngDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create URI PNG Bgra32 format");

                        var directUriPngDecoder = new PngBitmapDecoder(
                            pngUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(1, directUriPngDecoder.Frames.Count, "external SDK PngBitmapDecoder URI frame count");
                        AssertEqual(2, directUriPngDecoder.Frames[0].PixelWidth, "external SDK PngBitmapDecoder URI pixel width");

                        var pngBitmapImage = new BitmapImage(pngUri);
                        AssertEqual(2, pngBitmapImage.PixelWidth, "external SDK BitmapImage URI PNG pixel width");
                        AssertEqual(2, pngBitmapImage.PixelHeight, "external SDK BitmapImage URI PNG pixel height");
                        AssertEqual(PixelFormats.Bgra32, pngBitmapImage.Format, "external SDK BitmapImage URI PNG Bgra32 format");
                        var pngBitmapImagePixels = new byte[pixels.Length];
                        pngBitmapImage.CopyPixels(pngBitmapImagePixels, 8, 0);
                        AssertEqual(pixels[0], pngBitmapImagePixels[0], "external SDK BitmapImage URI PNG top-left blue byte");
                        AssertEqual(pixels[14], pngBitmapImagePixels[14], "external SDK BitmapImage URI PNG bottom-right red byte");
                    }
                    finally
                    {
                        File.Delete(pngPath);
                    }

                    var packPngUri = new Uri("pack://application:,,,/Assets/ExternalImage.png", UriKind.Absolute);
                    var packPngDecoder = BitmapDecoder.Create(
                        packPngUri,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(typeof(PngBitmapDecoder), packPngDecoder.GetType(), "external SDK BitmapDecoder.Create pack PNG decoder type");
                    AssertEqual(1, packPngDecoder.Frames.Count, "external SDK BitmapDecoder.Create pack PNG frame count");
                    AssertEqual(2, packPngDecoder.Frames[0].PixelWidth, "external SDK BitmapDecoder.Create pack PNG pixel width");
                    AssertEqual(2, packPngDecoder.Frames[0].PixelHeight, "external SDK BitmapDecoder.Create pack PNG pixel height");
                    AssertEqual(PixelFormats.Bgra32, packPngDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create pack PNG Bgra32 format");

                    var directPackPngDecoder = new PngBitmapDecoder(
                        packPngUri,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    AssertEqual(1, directPackPngDecoder.Frames.Count, "external SDK PngBitmapDecoder pack URI frame count");
                    AssertEqual(2, directPackPngDecoder.Frames[0].PixelWidth, "external SDK PngBitmapDecoder pack URI pixel width");

                    var packPngBitmapImage = new BitmapImage(packPngUri);
                    AssertEqual(2, packPngBitmapImage.PixelWidth, "external SDK BitmapImage pack PNG pixel width");
                    AssertEqual(2, packPngBitmapImage.PixelHeight, "external SDK BitmapImage pack PNG pixel height");
                    AssertEqual(PixelFormats.Bgra32, packPngBitmapImage.Format, "external SDK BitmapImage pack PNG Bgra32 format");
                    var packPngBitmapImagePixels = new byte[pixels.Length];
                    packPngBitmapImage.CopyPixels(packPngBitmapImagePixels, 8, 0);
                    AssertEqual((byte)0xFF, packPngBitmapImagePixels[2], "external SDK BitmapImage pack PNG top-left red byte");
                    AssertEqual((byte)0xFF, packPngBitmapImagePixels[15], "external SDK BitmapImage pack PNG final alpha byte");

                    string interlacedPngPath = Path.Combine(Path.GetTempPath(), "external-sdk-managed-image-" + Guid.NewGuid().ToString("N") + "-interlaced.png");
                    File.WriteAllBytes(interlacedPngPath, interlacedPngBytes);
                    try
                    {
                        var interlacedPngUri = new Uri(interlacedPngPath);
                        var uriInterlacedPngDecoder = BitmapDecoder.Create(
                            interlacedPngUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(typeof(PngBitmapDecoder), uriInterlacedPngDecoder.GetType(), "external SDK BitmapDecoder.Create URI interlaced PNG decoder type");
                        AssertEqual(1, uriInterlacedPngDecoder.Frames.Count, "external SDK BitmapDecoder.Create URI interlaced PNG frame count");
                        AssertEqual(PixelFormats.Bgra32, uriInterlacedPngDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create URI interlaced PNG Bgra32 format");

                        var interlacedPngBitmapImage = new BitmapImage(interlacedPngUri);
                        AssertEqual(2, interlacedPngBitmapImage.PixelWidth, "external SDK BitmapImage URI interlaced PNG pixel width");
                        AssertEqual(2, interlacedPngBitmapImage.PixelHeight, "external SDK BitmapImage URI interlaced PNG pixel height");
                        AssertEqual(PixelFormats.Bgra32, interlacedPngBitmapImage.Format, "external SDK BitmapImage URI interlaced PNG Bgra32 format");
                        var interlacedPngBitmapImagePixels = new byte[pixels.Length];
                        interlacedPngBitmapImage.CopyPixels(interlacedPngBitmapImagePixels, 8, 0);
                        AssertEqual(pixels[0], interlacedPngBitmapImagePixels[0], "external SDK BitmapImage URI interlaced PNG top-left blue byte");
                        AssertEqual(pixels[14], interlacedPngBitmapImagePixels[14], "external SDK BitmapImage URI interlaced PNG bottom-right red byte");
                    }
                    finally
                    {
                        File.Delete(interlacedPngPath);
                    }

                    string iconPath = Path.Combine(Path.GetTempPath(), "external-sdk-managed-image-" + Guid.NewGuid().ToString("N") + ".ico");
                    File.WriteAllBytes(iconPath, iconBytes);
                    try
                    {
                        var iconUri = new Uri(iconPath);
                        var uriIconDecoder = BitmapDecoder.Create(
                            iconUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(typeof(IconBitmapDecoder), uriIconDecoder.GetType(), "external SDK BitmapDecoder.Create URI ICO decoder type");
                        AssertEqual(1, uriIconDecoder.Frames.Count, "external SDK BitmapDecoder.Create URI ICO frame count");
                        AssertEqual(PixelFormats.Bgra32, uriIconDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create URI ICO Bgra32 format");

                        var directUriIconDecoder = new IconBitmapDecoder(
                            iconUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(1, directUriIconDecoder.Frames.Count, "external SDK IconBitmapDecoder URI frame count");
                        AssertEqual(2, directUriIconDecoder.Frames[0].PixelWidth, "external SDK IconBitmapDecoder URI pixel width");

                        var iconBitmapImage = new BitmapImage(iconUri);
                        AssertEqual(2, iconBitmapImage.PixelWidth, "external SDK BitmapImage URI ICO pixel width");
                        AssertEqual(2, iconBitmapImage.PixelHeight, "external SDK BitmapImage URI ICO pixel height");
                        AssertEqual(PixelFormats.Bgra32, iconBitmapImage.Format, "external SDK BitmapImage URI ICO Bgra32 format");
                        var iconBitmapImagePixels = new byte[pixels.Length];
                        iconBitmapImage.CopyPixels(iconBitmapImagePixels, 8, 0);
                        AssertEqual(pixels[0], iconBitmapImagePixels[0], "external SDK BitmapImage URI ICO top-left blue byte");
                        AssertEqual(pixels[14], iconBitmapImagePixels[14], "external SDK BitmapImage URI ICO bottom-right red byte");
                    }
                    finally
                    {
                        File.Delete(iconPath);
                    }

                    string dibIconPath = Path.Combine(Path.GetTempPath(), "external-sdk-managed-image-" + Guid.NewGuid().ToString("N") + "-dib.ico");
                    File.WriteAllBytes(dibIconPath, dibIconBytes);
                    try
                    {
                        var dibIconUri = new Uri(dibIconPath);
                        var uriDibIconDecoder = BitmapDecoder.Create(
                            dibIconUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(typeof(IconBitmapDecoder), uriDibIconDecoder.GetType(), "external SDK BitmapDecoder.Create URI DIB ICO decoder type");
                        AssertEqual(1, uriDibIconDecoder.Frames.Count, "external SDK BitmapDecoder.Create URI DIB ICO frame count");

                        var dibIconBitmapImage = new BitmapImage(dibIconUri);
                        AssertEqual(2, dibIconBitmapImage.PixelWidth, "external SDK BitmapImage URI DIB ICO pixel width");
                        AssertEqual(2, dibIconBitmapImage.PixelHeight, "external SDK BitmapImage URI DIB ICO pixel height");
                        AssertEqual(PixelFormats.Bgra32, dibIconBitmapImage.Format, "external SDK BitmapImage URI DIB ICO Bgra32 format");
                        var dibIconBitmapImagePixels = new byte[pixels.Length];
                        dibIconBitmapImage.CopyPixels(dibIconBitmapImagePixels, 8, 0);
                        AssertEqual((byte)0x00, dibIconBitmapImagePixels[15], "external SDK BitmapImage URI DIB ICO masked alpha byte");
                    }
                    finally
                    {
                        File.Delete(dibIconPath);
                    }

                    string jpegPath = Path.Combine(Path.GetTempPath(), "external-sdk-managed-image-" + Guid.NewGuid().ToString("N") + ".jpg");
                    File.WriteAllBytes(jpegPath, jpegBytes);
                    try
                    {
                        var jpegUri = new Uri(jpegPath);
                        var uriJpegDecoder = BitmapDecoder.Create(
                            jpegUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(typeof(JpegBitmapDecoder), uriJpegDecoder.GetType(), "external SDK BitmapDecoder.Create URI JPEG decoder type");
                        AssertEqual(1, uriJpegDecoder.Frames.Count, "external SDK BitmapDecoder.Create URI JPEG frame count");
                        AssertEqual(PixelFormats.Bgra32, uriJpegDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create URI JPEG Bgra32 format");

                        var directUriJpegDecoder = new JpegBitmapDecoder(
                            jpegUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(1, directUriJpegDecoder.Frames.Count, "external SDK JpegBitmapDecoder URI frame count");
                        AssertEqual(2, directUriJpegDecoder.Frames[0].PixelWidth, "external SDK JpegBitmapDecoder URI pixel width");

                        var jpegBitmapImage = new BitmapImage(jpegUri);
                        AssertEqual(2, jpegBitmapImage.PixelWidth, "external SDK BitmapImage URI JPEG pixel width");
                        AssertEqual(2, jpegBitmapImage.PixelHeight, "external SDK BitmapImage URI JPEG pixel height");
                        AssertEqual(PixelFormats.Bgra32, jpegBitmapImage.Format, "external SDK BitmapImage URI JPEG Bgra32 format");
                        var jpegBitmapImagePixels = new byte[pixels.Length];
                        jpegBitmapImage.CopyPixels(jpegBitmapImagePixels, 8, 0);
                        AssertEqual((byte)0xFF, jpegBitmapImagePixels[3], "external SDK BitmapImage URI JPEG first alpha byte");
                        AssertEqual((byte)0xFF, jpegBitmapImagePixels[15], "external SDK BitmapImage URI JPEG final alpha byte");
                    }
                    finally
                    {
                        File.Delete(jpegPath);
                    }

                    string gifPath = Path.Combine(Path.GetTempPath(), "external-sdk-managed-image-" + Guid.NewGuid().ToString("N") + ".gif");
                    File.WriteAllBytes(gifPath, gifBytes);
                    try
                    {
                        var gifUri = new Uri(gifPath);
                        var uriGifDecoder = BitmapDecoder.Create(
                            gifUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(typeof(GifBitmapDecoder), uriGifDecoder.GetType(), "external SDK BitmapDecoder.Create URI GIF decoder type");
                        AssertEqual(2, uriGifDecoder.Frames.Count, "external SDK BitmapDecoder.Create URI GIF frame count");
                        AssertEqual(PixelFormats.Bgra32, uriGifDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create URI GIF Bgra32 format");
                        AssertEqual(PixelFormats.Bgra32, uriGifDecoder.Frames[1].Format, "external SDK BitmapDecoder.Create URI GIF second-frame Bgra32 format");
                        var uriGifMetadata = RequireType<BitmapMetadata>(
                            uriGifDecoder.Frames[1].Metadata,
                            "external SDK BitmapDecoder.Create URI GIF second-frame metadata");
                        AssertEqual((ushort)7, uriGifMetadata.GetQuery("/grctlext/Delay"), "external SDK BitmapDecoder.Create URI GIF second-frame delay metadata");

                        var directUriGifDecoder = new GifBitmapDecoder(
                            gifUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(2, directUriGifDecoder.Frames.Count, "external SDK GifBitmapDecoder URI frame count");
                        AssertEqual(2, directUriGifDecoder.Frames[0].PixelWidth, "external SDK GifBitmapDecoder URI pixel width");
                        var directUriGifMetadata = RequireType<BitmapMetadata>(
                            directUriGifDecoder.Frames[0].Metadata,
                            "external SDK GifBitmapDecoder URI first-frame metadata");
                        AssertEqual((ushort)5, directUriGifMetadata.GetQuery("/grctlext/Delay"), "external SDK GifBitmapDecoder URI first-frame delay metadata");
                        var directUriSecondGifPixels = new byte[pixels.Length];
                        directUriGifDecoder.Frames[1].CopyPixels(directUriSecondGifPixels, 8, 0);
                        AssertEqual((byte)0xFF, directUriSecondGifPixels[1], "external SDK GifBitmapDecoder URI second-frame green byte");

                        var gifBitmapImage = new BitmapImage(gifUri);
                        AssertEqual(2, gifBitmapImage.PixelWidth, "external SDK BitmapImage URI GIF pixel width");
                        AssertEqual(2, gifBitmapImage.PixelHeight, "external SDK BitmapImage URI GIF pixel height");
                        AssertEqual(PixelFormats.Bgra32, gifBitmapImage.Format, "external SDK BitmapImage URI GIF Bgra32 format");
                        var gifBitmapImagePixels = new byte[pixels.Length];
                        gifBitmapImage.CopyPixels(gifBitmapImagePixels, 8, 0);
                        AssertEqual((byte)0xFF, gifBitmapImagePixels[3], "external SDK BitmapImage URI GIF first alpha byte");
                        AssertEqual((byte)0xFF, gifBitmapImagePixels[15], "external SDK BitmapImage URI GIF final alpha byte");
                    }
                    finally
                    {
                        File.Delete(gifPath);
                    }

                    string tiffPath = Path.Combine(Path.GetTempPath(), "external-sdk-managed-image-" + Guid.NewGuid().ToString("N") + ".tif");
                    File.WriteAllBytes(tiffPath, tiffBytes);
                    try
                    {
                        var tiffUri = new Uri(tiffPath);
                        var uriTiffDecoder = BitmapDecoder.Create(
                            tiffUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(typeof(TiffBitmapDecoder), uriTiffDecoder.GetType(), "external SDK BitmapDecoder.Create URI TIFF decoder type");
                        AssertEqual(1, uriTiffDecoder.Frames.Count, "external SDK BitmapDecoder.Create URI TIFF frame count");
                        AssertEqual(PixelFormats.Bgra32, uriTiffDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create URI TIFF Bgra32 format");
                        var uriTiffMetadata = RequireType<BitmapMetadata>(
                            uriTiffDecoder.Frames[0].Metadata,
                            "external SDK BitmapDecoder.Create URI TIFF metadata");
                        AssertEqual((ushort)6, uriTiffMetadata.GetQuery("/ifd/{ushort=274}"), "external SDK BitmapDecoder.Create URI TIFF orientation metadata");

                        var directUriTiffDecoder = new TiffBitmapDecoder(
                            tiffUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(1, directUriTiffDecoder.Frames.Count, "external SDK TiffBitmapDecoder URI frame count");
                        AssertEqual(2, directUriTiffDecoder.Frames[0].PixelWidth, "external SDK TiffBitmapDecoder URI pixel width");
                        var directUriTiffMetadata = RequireType<BitmapMetadata>(
                            directUriTiffDecoder.Frames[0].Metadata,
                            "external SDK TiffBitmapDecoder URI metadata");
                        AssertEqual((ushort)6, directUriTiffMetadata.GetQuery("/ifd/{ushort=274}"), "external SDK TiffBitmapDecoder URI orientation metadata");

                        var tiffBitmapImage = new BitmapImage(tiffUri);
                        AssertEqual(2, tiffBitmapImage.PixelWidth, "external SDK BitmapImage URI TIFF pixel width");
                        AssertEqual(2, tiffBitmapImage.PixelHeight, "external SDK BitmapImage URI TIFF pixel height");
                        AssertEqual(PixelFormats.Bgra32, tiffBitmapImage.Format, "external SDK BitmapImage URI TIFF Bgra32 format");
                        var tiffBitmapImagePixels = new byte[pixels.Length];
                        tiffBitmapImage.CopyPixels(tiffBitmapImagePixels, 8, 0);
                        AssertEqual(pixels[0], tiffBitmapImagePixels[0], "external SDK BitmapImage URI TIFF top-left blue byte");
                        AssertEqual(pixels[14], tiffBitmapImagePixels[14], "external SDK BitmapImage URI TIFF bottom-right red byte");
                    }
                    finally
                    {
                        File.Delete(tiffPath);
                    }

                    string multiFrameTiffPath = Path.Combine(Path.GetTempPath(), "external-sdk-managed-image-" + Guid.NewGuid().ToString("N") + "-multiframe.tif");
                    File.WriteAllBytes(multiFrameTiffPath, multiFrameTiffBytes);
                    try
                    {
                        var multiFrameTiffUri = new Uri(multiFrameTiffPath);
                        var uriMultiFrameTiffDecoder = BitmapDecoder.Create(
                            multiFrameTiffUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(typeof(TiffBitmapDecoder), uriMultiFrameTiffDecoder.GetType(), "external SDK BitmapDecoder.Create URI multi-frame TIFF decoder type");
                        AssertEqual(2, uriMultiFrameTiffDecoder.Frames.Count, "external SDK BitmapDecoder.Create URI multi-frame TIFF frame count");
                        var uriSecondTiffPixels = new byte[secondTiffPixels.Length];
                        uriMultiFrameTiffDecoder.Frames[1].CopyPixels(uriSecondTiffPixels, 8, 0);
                        AssertEqual(secondTiffPixels[0], uriSecondTiffPixels[0], "external SDK BitmapDecoder.Create URI multi-frame TIFF second top-left blue byte");
                    }
                    finally
                    {
                        File.Delete(multiFrameTiffPath);
                    }

                    string paletteTiffPath = Path.Combine(Path.GetTempPath(), "external-sdk-managed-image-" + Guid.NewGuid().ToString("N") + "-palette.tif");
                    File.WriteAllBytes(paletteTiffPath, paletteTiffBytes);
                    try
                    {
                        var paletteTiffUri = new Uri(paletteTiffPath);
                        var uriPaletteTiffDecoder = BitmapDecoder.Create(
                            paletteTiffUri,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);
                        AssertEqual(typeof(TiffBitmapDecoder), uriPaletteTiffDecoder.GetType(), "external SDK BitmapDecoder.Create URI palette TIFF decoder type");
                        AssertEqual(1, uriPaletteTiffDecoder.Frames.Count, "external SDK BitmapDecoder.Create URI palette TIFF frame count");
                        AssertEqual(PixelFormats.Bgra32, uriPaletteTiffDecoder.Frames[0].Format, "external SDK BitmapDecoder.Create URI palette TIFF Bgra32 format");

                        var paletteTiffBitmapImage = new BitmapImage(paletteTiffUri);
                        AssertEqual(2, paletteTiffBitmapImage.PixelWidth, "external SDK BitmapImage URI palette TIFF pixel width");
                        AssertEqual(2, paletteTiffBitmapImage.PixelHeight, "external SDK BitmapImage URI palette TIFF pixel height");
                        AssertEqual(PixelFormats.Bgra32, paletteTiffBitmapImage.Format, "external SDK BitmapImage URI palette TIFF Bgra32 format");
                        var paletteTiffBitmapImagePixels = new byte[pixels.Length];
                        paletteTiffBitmapImage.CopyPixels(paletteTiffBitmapImagePixels, 8, 0);
                        AssertEqual(pixels[0], paletteTiffBitmapImagePixels[0], "external SDK BitmapImage URI palette TIFF top-left blue byte");
                        AssertEqual(pixels[14], paletteTiffBitmapImagePixels[14], "external SDK BitmapImage URI palette TIFF bottom-right red byte");
                    }
                    finally
                    {
                        File.Delete(paletteTiffPath);
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

                private static byte[] CreateJpegBytes()
                {
                    return Convert.FromBase64String(
                        "/9j//gAQTGF2YzYyLjI4LjEwMAD/2wBDAAgEBAQEBAUFBQUFBQYGBgYGBgYGBgYGBgYHBwcICAgHBwcGBgcHCAgICAkJCQgICAgJCQoKCgwMCwsODg4RERT/xABnAAEBAAAAAAAAAAAAAAAAAAADBwEBAQEAAAAAAAAAAAAAAAAAAgQHEAACAgICAwEAAAAAAAAAAAACAQMFBgQAEXa0NwcRAAICAgIBBQEBAAAAAAAAAAMCAQQFBgAHEbV2snMiNzT/wAARCAACAAIDARIAAhIAAxIA/9oADAMBAAIRAxEAPwChYBVVc+CYpLLo6csklDTnJIevEZmZaMLIiIgbIib7bb7b4/538/xHx6l9CDmQ9nmLS7K3arVI9YANmzwQAC0iCEQ8lYVBjGkwiIixCoixELEeIjh7Y/qe+e6th9Us8a6/gcgsXLmJxtu1ZiLFmzYp1zHsGL+yGMUg2chCPMs7vMszTMzPnllH/FW+gXwjn//Z");
                }

                private static byte[] CreateGifBytes()
                {
                    List<byte> gif = new List<byte>();
                    gif.AddRange(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' });
                    WriteUInt16LittleEndian(gif, 2);
                    WriteUInt16LittleEndian(gif, 2);
                    gif.Add(0xF1);
                    gif.Add(0);
                    gif.Add(0);
                    gif.AddRange(new byte[]
                    {
                        0x00, 0x00, 0x00,
                        0xFF, 0x00, 0x00,
                        0x00, 0xFF, 0x00,
                        0x00, 0x00, 0xFF
                    });

                    gif.AddRange(new byte[] { 0x21, 0xFF, 0x0B });
                    gif.AddRange(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                    gif.AddRange(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
                    WriteGifFrame(gif, colorIndex: 1, delay: 5);
                    WriteGifFrame(gif, colorIndex: 2, delay: 7);
                    gif.Add(0x3B);
                    return gif.ToArray();
                }

                private static void WriteGifFrame(List<byte> gif, byte colorIndex, ushort delay)
                {
                    gif.AddRange(new byte[] { 0x21, 0xF9, 0x04, 0x04 });
                    WriteUInt16LittleEndian(gif, delay);
                    gif.Add(0);
                    gif.Add(0);
                    gif.Add(0x2C);
                    WriteUInt16LittleEndian(gif, 0);
                    WriteUInt16LittleEndian(gif, 0);
                    WriteUInt16LittleEndian(gif, 2);
                    WriteUInt16LittleEndian(gif, 2);
                    gif.Add(0);
                    gif.Add(2);
                    byte[] imageData = PackGifCodes(new[] { 4, colorIndex, 4, colorIndex, 4, colorIndex, 4, colorIndex, 5 }, 3);
                    gif.Add((byte)imageData.Length);
                    gif.AddRange(imageData);
                    gif.Add(0);
                }

                private static byte[] PackGifCodes(IReadOnlyList<int> codes, int codeSize)
                {
                    int bitCount = checked(codes.Count * codeSize);
                    byte[] bytes = new byte[(bitCount + 7) / 8];
                    int bitOffset = 0;
                    foreach (int code in codes)
                    {
                        for (int bit = 0; bit < codeSize; bit++)
                        {
                            if (((code >> bit) & 1) != 0)
                            {
                                bytes[bitOffset / 8] |= (byte)(1 << (bitOffset % 8));
                            }

                            bitOffset++;
                        }
                    }

                    return bytes;
                }

                private static byte[] CreateTiffBytes(byte[] bgraPixels, int width, int height)
                {
                    const int entryCount = 11;
                    int ifdOffset = 8;
                    int bitsPerSampleOffset = ifdOffset + 2 + (entryCount * 12) + 4;
                    int pixelOffset = bitsPerSampleOffset + 6;
                    int pixelByteCount = checked(width * height * 3);
                    byte[] tiff = new byte[checked(pixelOffset + pixelByteCount)];

                    tiff[0] = (byte)'I';
                    tiff[1] = (byte)'I';
                    WriteUInt16LittleEndian(tiff, 2, 42);
                    WriteUInt32LittleEndian(tiff, 4, (uint)ifdOffset);
                    WriteUInt16LittleEndian(tiff, ifdOffset, entryCount);

                    int entryOffset = ifdOffset + 2;
                    WriteTiffShortEntry(tiff, ref entryOffset, 256, (ushort)width);
                    WriteTiffShortEntry(tiff, ref entryOffset, 257, (ushort)height);
                    WriteTiffOffsetEntry(tiff, ref entryOffset, 258, 3, 3, (uint)bitsPerSampleOffset);
                    WriteTiffShortEntry(tiff, ref entryOffset, 259, 1);
                    WriteTiffShortEntry(tiff, ref entryOffset, 262, 2);
                    WriteTiffLongEntry(tiff, ref entryOffset, 273, (uint)pixelOffset);
                    WriteTiffShortEntry(tiff, ref entryOffset, 274, 6);
                    WriteTiffShortEntry(tiff, ref entryOffset, 277, 3);
                    WriteTiffLongEntry(tiff, ref entryOffset, 278, (uint)height);
                    WriteTiffLongEntry(tiff, ref entryOffset, 279, (uint)pixelByteCount);
                    WriteTiffShortEntry(tiff, ref entryOffset, 284, 1);
                    WriteUInt32LittleEndian(tiff, entryOffset, 0);

                    WriteUInt16LittleEndian(tiff, bitsPerSampleOffset + 0, 8);
                    WriteUInt16LittleEndian(tiff, bitsPerSampleOffset + 2, 8);
                    WriteUInt16LittleEndian(tiff, bitsPerSampleOffset + 4, 8);

                    int sourceOffset = 0;
                    int destinationOffset = pixelOffset;
                    for (int i = 0; i < width * height; i++)
                    {
                        tiff[destinationOffset++] = bgraPixels[sourceOffset + 2];
                        tiff[destinationOffset++] = bgraPixels[sourceOffset + 1];
                        tiff[destinationOffset++] = bgraPixels[sourceOffset + 0];
                        sourceOffset += 4;
                    }

                    return tiff;
                }

                private static byte[] CreateMultiFrameTiffBytes(byte[] firstBgraPixels, byte[] secondBgraPixels, int width, int height)
                {
                    const int entryCount = 10;
                    int ifdOffset = 8;
                    int ifdByteCount = 2 + (entryCount * 12) + 4;
                    int secondIfdOffset = ifdOffset + ifdByteCount;
                    int firstBitsPerSampleOffset = secondIfdOffset + ifdByteCount;
                    int secondBitsPerSampleOffset = firstBitsPerSampleOffset + 6;
                    int firstPixelOffset = secondBitsPerSampleOffset + 6;
                    int pixelByteCount = checked(width * height * 3);
                    int secondPixelOffset = firstPixelOffset + pixelByteCount;
                    byte[] tiff = new byte[checked(secondPixelOffset + pixelByteCount)];

                    tiff[0] = (byte)'I';
                    tiff[1] = (byte)'I';
                    WriteUInt16LittleEndian(tiff, 2, 42);
                    WriteUInt32LittleEndian(tiff, 4, (uint)ifdOffset);

                    WriteTiffRgbDirectory(
                        tiff,
                        ifdOffset,
                        width,
                        height,
                        firstBitsPerSampleOffset,
                        firstPixelOffset,
                        pixelByteCount,
                        (uint)secondIfdOffset);
                    WriteTiffRgbDirectory(
                        tiff,
                        secondIfdOffset,
                        width,
                        height,
                        secondBitsPerSampleOffset,
                        secondPixelOffset,
                        pixelByteCount,
                        0);
                    WriteTiffRgbPixels(tiff, firstPixelOffset, firstBgraPixels, width, height);
                    WriteTiffRgbPixels(tiff, secondPixelOffset, secondBgraPixels, width, height);

                    return tiff;
                }

                private static void WriteTiffRgbDirectory(
                    byte[] tiff,
                    int ifdOffset,
                    int width,
                    int height,
                    int bitsPerSampleOffset,
                    int pixelOffset,
                    int pixelByteCount,
                    uint nextIfdOffset)
                {
                    const int entryCount = 10;
                    WriteUInt16LittleEndian(tiff, ifdOffset, entryCount);
                    int entryOffset = ifdOffset + 2;
                    WriteTiffShortEntry(tiff, ref entryOffset, 256, (ushort)width);
                    WriteTiffShortEntry(tiff, ref entryOffset, 257, (ushort)height);
                    WriteTiffOffsetEntry(tiff, ref entryOffset, 258, 3, 3, (uint)bitsPerSampleOffset);
                    WriteTiffShortEntry(tiff, ref entryOffset, 259, 1);
                    WriteTiffShortEntry(tiff, ref entryOffset, 262, 2);
                    WriteTiffLongEntry(tiff, ref entryOffset, 273, (uint)pixelOffset);
                    WriteTiffShortEntry(tiff, ref entryOffset, 277, 3);
                    WriteTiffLongEntry(tiff, ref entryOffset, 278, (uint)height);
                    WriteTiffLongEntry(tiff, ref entryOffset, 279, (uint)pixelByteCount);
                    WriteTiffShortEntry(tiff, ref entryOffset, 284, 1);
                    WriteUInt32LittleEndian(tiff, entryOffset, nextIfdOffset);

                    WriteUInt16LittleEndian(tiff, bitsPerSampleOffset + 0, 8);
                    WriteUInt16LittleEndian(tiff, bitsPerSampleOffset + 2, 8);
                    WriteUInt16LittleEndian(tiff, bitsPerSampleOffset + 4, 8);
                }

                private static void WriteTiffRgbPixels(byte[] tiff, int pixelOffset, byte[] bgraPixels, int width, int height)
                {
                    int sourceOffset = 0;
                    int destinationOffset = pixelOffset;
                    for (int i = 0; i < width * height; i++)
                    {
                        tiff[destinationOffset++] = bgraPixels[sourceOffset + 2];
                        tiff[destinationOffset++] = bgraPixels[sourceOffset + 1];
                        tiff[destinationOffset++] = bgraPixels[sourceOffset + 0];
                        sourceOffset += 4;
                    }
                }

                private static byte[] CreatePaletteTiffBytes(byte[] indices, int width, int height, int bitsPerSample)
                {
                    int colorCount = 1 << bitsPerSample;
                    const int entryCount = 11;
                    int ifdOffset = 8;
                    int colorMapOffset = ifdOffset + 2 + (entryCount * 12) + 4;
                    int colorMapByteCount = colorCount * 3 * 2;
                    int pixelOffset = colorMapOffset + colorMapByteCount;
                    int sourceStride = ((width * bitsPerSample) + 7) / 8;
                    int pixelByteCount = checked(sourceStride * height);
                    byte[] tiff = new byte[checked(pixelOffset + pixelByteCount)];

                    tiff[0] = (byte)'I';
                    tiff[1] = (byte)'I';
                    WriteUInt16LittleEndian(tiff, 2, 42);
                    WriteUInt32LittleEndian(tiff, 4, (uint)ifdOffset);
                    WriteUInt16LittleEndian(tiff, ifdOffset, entryCount);

                    int entryOffset = ifdOffset + 2;
                    WriteTiffShortEntry(tiff, ref entryOffset, 256, (ushort)width);
                    WriteTiffShortEntry(tiff, ref entryOffset, 257, (ushort)height);
                    WriteTiffShortEntry(tiff, ref entryOffset, 258, (ushort)bitsPerSample);
                    WriteTiffShortEntry(tiff, ref entryOffset, 259, 1);
                    WriteTiffShortEntry(tiff, ref entryOffset, 262, 3);
                    WriteTiffLongEntry(tiff, ref entryOffset, 273, (uint)pixelOffset);
                    WriteTiffShortEntry(tiff, ref entryOffset, 277, 1);
                    WriteTiffLongEntry(tiff, ref entryOffset, 278, (uint)height);
                    WriteTiffLongEntry(tiff, ref entryOffset, 279, (uint)pixelByteCount);
                    WriteTiffShortEntry(tiff, ref entryOffset, 284, 1);
                    WriteTiffOffsetEntry(tiff, ref entryOffset, 320, 3, (uint)(colorCount * 3), (uint)colorMapOffset);
                    WriteUInt32LittleEndian(tiff, entryOffset, 0);

                    byte[] red = [0x30, 0x60, 0x90, 0xC0];
                    byte[] green = [0x20, 0x50, 0x80, 0xB0];
                    byte[] blue = [0x10, 0x40, 0x70, 0xA0];
                    for (int index = 0; index < colorCount; index++)
                    {
                        byte redValue = index < red.Length ? red[index] : (byte)0;
                        byte greenValue = index < green.Length ? green[index] : (byte)0;
                        byte blueValue = index < blue.Length ? blue[index] : (byte)0;
                        WriteUInt16LittleEndian(tiff, colorMapOffset + (index * 2), redValue * 257);
                        WriteUInt16LittleEndian(tiff, colorMapOffset + ((colorCount + index) * 2), greenValue * 257);
                        WriteUInt16LittleEndian(tiff, colorMapOffset + (((colorCount * 2) + index) * 2), blueValue * 257);
                    }

                    for (int y = 0; y < height; y++)
                    {
                        int rowOffset = pixelOffset + (y * sourceStride);
                        for (int x = 0; x < width; x++)
                        {
                            int index = y * width + x;
                            int bitOffset = x * bitsPerSample;
                            int byteOffset = rowOffset + (bitOffset / 8);
                            int shift = 8 - bitsPerSample - (bitOffset % 8);
                            tiff[byteOffset] |= (byte)(indices[index] << shift);
                        }
                    }

                    return tiff;
                }

                private static void WriteTiffShortEntry(byte[] target, ref int offset, ushort tag, ushort value)
                {
                    WriteUInt16LittleEndian(target, offset + 0, tag);
                    WriteUInt16LittleEndian(target, offset + 2, 3);
                    WriteUInt32LittleEndian(target, offset + 4, 1);
                    WriteUInt16LittleEndian(target, offset + 8, value);
                    WriteUInt16LittleEndian(target, offset + 10, 0);
                    offset += 12;
                }

                private static void WriteTiffLongEntry(byte[] target, ref int offset, ushort tag, uint value)
                {
                    WriteTiffOffsetEntry(target, ref offset, tag, 4, 1, value);
                }

                private static void WriteTiffOffsetEntry(byte[] target, ref int offset, ushort tag, ushort type, uint count, uint value)
                {
                    WriteUInt16LittleEndian(target, offset + 0, tag);
                    WriteUInt16LittleEndian(target, offset + 2, type);
                    WriteUInt32LittleEndian(target, offset + 4, count);
                    WriteUInt32LittleEndian(target, offset + 8, value);
                    offset += 12;
                }

                private static void WriteUInt16LittleEndian(List<byte> target, int value)
                {
                    target.Add((byte)value);
                    target.Add((byte)(value >> 8));
                }

                private static void WriteUInt16LittleEndian(byte[] target, int offset, int value)
                {
                    target[offset + 0] = (byte)value;
                    target[offset + 1] = (byte)(value >> 8);
                }

                private static void WriteUInt32LittleEndian(byte[] target, int offset, uint value)
                {
                    target[offset + 0] = (byte)value;
                    target[offset + 1] = (byte)(value >> 8);
                    target[offset + 2] = (byte)(value >> 16);
                    target[offset + 3] = (byte)(value >> 24);
                }

                private static byte[] CreatePngIconBytes(byte[] pngBytes, byte width, byte height)
                {
                    byte[] icon = new byte[checked(22 + pngBytes.Length)];
                    WriteLittleEndianUInt16(icon, 0, 0);
                    WriteLittleEndianUInt16(icon, 2, 1);
                    WriteLittleEndianUInt16(icon, 4, 1);
                    icon[6] = width;
                    icon[7] = height;
                    icon[8] = 0;
                    icon[9] = 0;
                    WriteLittleEndianUInt16(icon, 10, 1);
                    WriteLittleEndianUInt16(icon, 12, 32);
                    WriteLittleEndianUInt32(icon, 14, (uint)pngBytes.Length);
                    WriteLittleEndianUInt32(icon, 18, 22);
                    Buffer.BlockCopy(pngBytes, 0, icon, 22, pngBytes.Length);
                    return icon;
                }

                private static byte[] CreateDibIconBytes(byte[] bgraPixels, int width, int height, int stride)
                {
                    int xorStride = checked(width * 4);
                    int maskStride = checked((width + 31) / 32 * 4);
                    int dibLength = checked(40 + xorStride * height + maskStride * height);
                    byte[] icon = new byte[checked(22 + dibLength)];

                    WriteLittleEndianUInt16(icon, 0, 0);
                    WriteLittleEndianUInt16(icon, 2, 1);
                    WriteLittleEndianUInt16(icon, 4, 1);
                    icon[6] = (byte)width;
                    icon[7] = (byte)height;
                    icon[8] = 0;
                    icon[9] = 0;
                    WriteLittleEndianUInt16(icon, 10, 1);
                    WriteLittleEndianUInt16(icon, 12, 32);
                    WriteLittleEndianUInt32(icon, 14, (uint)dibLength);
                    WriteLittleEndianUInt32(icon, 18, 22);

                    int dibOffset = 22;
                    WriteLittleEndianUInt32(icon, dibOffset, 40);
                    WriteLittleEndianUInt32(icon, dibOffset + 4, (uint)width);
                    WriteLittleEndianUInt32(icon, dibOffset + 8, (uint)(height * 2));
                    WriteLittleEndianUInt16(icon, dibOffset + 12, 1);
                    WriteLittleEndianUInt16(icon, dibOffset + 14, 32);
                    WriteLittleEndianUInt32(icon, dibOffset + 16, 0);
                    WriteLittleEndianUInt32(icon, dibOffset + 20, (uint)(xorStride * height + maskStride * height));
                    WriteLittleEndianUInt32(icon, dibOffset + 24, 0);
                    WriteLittleEndianUInt32(icon, dibOffset + 28, 0);
                    WriteLittleEndianUInt32(icon, dibOffset + 32, 0);
                    WriteLittleEndianUInt32(icon, dibOffset + 36, 0);

                    int xorOffset = dibOffset + 40;
                    for (int fileRow = 0; fileRow < height; fileRow++)
                    {
                        int sourceRow = (height - 1 - fileRow) * stride;
                        Buffer.BlockCopy(bgraPixels, sourceRow, icon, xorOffset + fileRow * xorStride, xorStride);
                    }

                    int maskOffset = xorOffset + xorStride * height;
                    int maskedX = width - 1;
                    icon[maskOffset + maskedX / 8] = (byte)(0x80 >> (maskedX % 8));
                    return icon;
                }

                private static byte[] CreateRgbaPngBytes(byte[] bgraPixels, int width, int height, int stride)
                {
                    byte[] rawRows = new byte[checked((width * 4 + 1) * height)];
                    int rawOffset = 0;
                    for (int y = 0; y < height; y++)
                    {
                        rawRows[rawOffset++] = 0;
                        int sourceRow = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int sourceOffset = sourceRow + x * 4;
                            rawRows[rawOffset++] = bgraPixels[sourceOffset + 2];
                            rawRows[rawOffset++] = bgraPixels[sourceOffset + 1];
                            rawRows[rawOffset++] = bgraPixels[sourceOffset];
                            rawRows[rawOffset++] = bgraPixels[sourceOffset + 3];
                        }
                    }

                    using var compressed = new MemoryStream();
                    using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                    {
                        zlib.Write(rawRows, 0, rawRows.Length);
                    }

                    using var png = new MemoryStream();
                    png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                    byte[] ihdr = new byte[13];
                    WriteBigEndianUInt32(ihdr, 0, (uint)width);
                    WriteBigEndianUInt32(ihdr, 4, (uint)height);
                    ihdr[8] = 8;
                    ihdr[9] = 6;
                    WritePngChunk(png, "IHDR", ihdr);
                    WritePngChunk(png, "IDAT", compressed.ToArray());
                    WritePngChunk(png, "IEND", Array.Empty<byte>());
                    return png.ToArray();
                }

                private static byte[] CreateAdam7RgbaPngBytes(byte[] bgraPixels, int width, int height, int stride)
                {
                    int[] startX = [0, 4, 0, 2, 0, 1, 0];
                    int[] startY = [0, 0, 4, 0, 2, 0, 1];
                    int[] deltaX = [8, 8, 4, 4, 2, 2, 1];
                    int[] deltaY = [8, 8, 8, 4, 4, 2, 2];

                    using var rawRows = new MemoryStream();
                    for (int pass = 0; pass < startX.Length; pass++)
                    {
                        int passWidth = GetAdam7PassSize(width, startX[pass], deltaX[pass]);
                        int passHeight = GetAdam7PassSize(height, startY[pass], deltaY[pass]);
                        if (passWidth == 0 || passHeight == 0)
                        {
                            continue;
                        }

                        for (int y = 0; y < passHeight; y++)
                        {
                            rawRows.WriteByte(0);
                            int sourceY = startY[pass] + y * deltaY[pass];
                            int sourceRow = sourceY * stride;
                            for (int x = 0; x < passWidth; x++)
                            {
                                int sourceX = startX[pass] + x * deltaX[pass];
                                int sourceOffset = sourceRow + sourceX * 4;
                                rawRows.WriteByte(bgraPixels[sourceOffset + 2]);
                                rawRows.WriteByte(bgraPixels[sourceOffset + 1]);
                                rawRows.WriteByte(bgraPixels[sourceOffset]);
                                rawRows.WriteByte(bgraPixels[sourceOffset + 3]);
                            }
                        }
                    }

                    using var compressed = new MemoryStream();
                    using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                    {
                        byte[] rawBytes = rawRows.ToArray();
                        zlib.Write(rawBytes, 0, rawBytes.Length);
                    }

                    using var png = new MemoryStream();
                    png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                    byte[] ihdr = new byte[13];
                    WriteBigEndianUInt32(ihdr, 0, (uint)width);
                    WriteBigEndianUInt32(ihdr, 4, (uint)height);
                    ihdr[8] = 8;
                    ihdr[9] = 6;
                    ihdr[12] = 1;
                    WritePngChunk(png, "IHDR", ihdr);
                    WritePngChunk(png, "IDAT", compressed.ToArray());
                    WritePngChunk(png, "IEND", Array.Empty<byte>());
                    return png.ToArray();
                }

                private static int GetAdam7PassSize(int size, int start, int delta)
                {
                    return size <= start ? 0 : ((size - start + delta - 1) / delta);
                }

                private static byte[] CreateRgba16PngBytes(byte[] bgraPixels, int width, int height, int stride)
                {
                    byte[] rawRows = new byte[checked((width * 8 + 1) * height)];
                    int rawOffset = 0;
                    for (int y = 0; y < height; y++)
                    {
                        rawRows[rawOffset++] = 0;
                        int sourceRow = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int sourceOffset = sourceRow + x * 4;
                            WriteRepeated16BitSample(rawRows, ref rawOffset, bgraPixels[sourceOffset + 2]);
                            WriteRepeated16BitSample(rawRows, ref rawOffset, bgraPixels[sourceOffset + 1]);
                            WriteRepeated16BitSample(rawRows, ref rawOffset, bgraPixels[sourceOffset]);
                            WriteRepeated16BitSample(rawRows, ref rawOffset, bgraPixels[sourceOffset + 3]);
                        }
                    }

                    using var compressed = new MemoryStream();
                    using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                    {
                        zlib.Write(rawRows, 0, rawRows.Length);
                    }

                    using var png = new MemoryStream();
                    png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                    byte[] ihdr = new byte[13];
                    WriteBigEndianUInt32(ihdr, 0, (uint)width);
                    WriteBigEndianUInt32(ihdr, 4, (uint)height);
                    ihdr[8] = 16;
                    ihdr[9] = 6;
                    WritePngChunk(png, "IHDR", ihdr);
                    WritePngChunk(png, "IDAT", compressed.ToArray());
                    WritePngChunk(png, "IEND", Array.Empty<byte>());
                    return png.ToArray();
                }

                private static byte[] CreateIndexedPngBytes(byte[] indices, Color[] palette, byte[] alpha, int width, int height, int bitDepth)
                {
                    int rowBytes = checked((width * bitDepth + 7) / 8);
                    byte[] rawRows = new byte[checked((rowBytes + 1) * height)];
                    int rawOffset = 0;
                    int sourceOffset = 0;
                    for (int y = 0; y < height; y++)
                    {
                        rawRows[rawOffset++] = 0;
                        int rowOffset = rawOffset;
                        rawOffset += rowBytes;
                        for (int x = 0; x < width; x++)
                        {
                            int bitOffset = x * bitDepth;
                            int shift = 8 - bitDepth - bitOffset % 8;
                            rawRows[rowOffset + bitOffset / 8] |= (byte)(indices[sourceOffset++] << shift);
                        }
                    }

                    using var compressed = new MemoryStream();
                    using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                    {
                        zlib.Write(rawRows, 0, rawRows.Length);
                    }

                    using var png = new MemoryStream();
                    png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                    byte[] ihdr = new byte[13];
                    WriteBigEndianUInt32(ihdr, 0, (uint)width);
                    WriteBigEndianUInt32(ihdr, 4, (uint)height);
                    ihdr[8] = (byte)bitDepth;
                    ihdr[9] = 3;
                    WritePngChunk(png, "IHDR", ihdr);

                    byte[] paletteBytes = new byte[checked(palette.Length * 3)];
                    for (int i = 0; i < palette.Length; i++)
                    {
                        paletteBytes[i * 3] = palette[i].R;
                        paletteBytes[i * 3 + 1] = palette[i].G;
                        paletteBytes[i * 3 + 2] = palette[i].B;
                    }

                    WritePngChunk(png, "PLTE", paletteBytes);
                    WritePngChunk(png, "tRNS", alpha);
                    WritePngChunk(png, "IDAT", compressed.ToArray());
                    WritePngChunk(png, "IEND", Array.Empty<byte>());
                    return png.ToArray();
                }

                private static void WriteRepeated16BitSample(byte[] buffer, ref int offset, byte value)
                {
                    buffer[offset++] = value;
                    buffer[offset++] = value;
                }

                private static void WritePngChunk(Stream stream, string chunkType, byte[] data)
                {
                    WriteBigEndianUInt32(stream, (uint)data.Length);
                    byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(chunkType);
                    stream.Write(typeBytes, 0, typeBytes.Length);
                    stream.Write(data, 0, data.Length);
                    uint crc = Crc32(typeBytes, data);
                    WriteBigEndianUInt32(stream, crc);
                }

                private static uint Crc32(byte[] typeBytes, byte[] data)
                {
                    uint crc = 0xFFFFFFFF;
                    foreach (byte value in typeBytes)
                    {
                        crc = UpdateCrc32(crc, value);
                    }

                    foreach (byte value in data)
                    {
                        crc = UpdateCrc32(crc, value);
                    }

                    return ~crc;
                }

                private static uint UpdateCrc32(uint crc, byte value)
                {
                    crc ^= value;
                    for (int i = 0; i < 8; i++)
                    {
                        crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320u;
                    }

                    return crc;
                }

                private static void WriteBigEndianUInt32(Stream stream, uint value)
                {
                    Span<byte> buffer = stackalloc byte[4];
                    WriteBigEndianUInt32(buffer, 0, value);
                    stream.Write(buffer);
                }

                private static void WriteBigEndianUInt32(Span<byte> buffer, int offset, uint value)
                {
                    buffer[offset] = (byte)(value >> 24);
                    buffer[offset + 1] = (byte)(value >> 16);
                    buffer[offset + 2] = (byte)(value >> 8);
                    buffer[offset + 3] = (byte)value;
                }

                private static void WriteLittleEndianUInt16(Span<byte> buffer, int offset, ushort value)
                {
                    buffer[offset] = (byte)value;
                    buffer[offset + 1] = (byte)(value >> 8);
                }

                private static void WriteLittleEndianUInt32(Span<byte> buffer, int offset, uint value)
                {
                    buffer[offset] = (byte)value;
                    buffer[offset + 1] = (byte)(value >> 8);
                    buffer[offset + 2] = (byte)(value >> 16);
                    buffer[offset + 3] = (byte)(value >> 24);
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
                        "<TextBox x:Name=\"ExternalLooseInputScopeTextBox\" Text=\"External loose input scope text\">" +
                        "<InputMethod.InputScope>" +
                        "<InputScope RegularExpression=\"[a-z]+\" SrgsMarkup=\"external-loose-input-scope\">" +
                        "<InputScope.Names>" +
                        "<InputScopeName>EmailUserName</InputScopeName>" +
                        "</InputScope.Names>" +
                        "<InputScope.PhraseList>" +
                        "<InputScopePhrase>external loose phrase</InputScopePhrase>" +
                        "</InputScope.PhraseList>" +
                        "</InputScope>" +
                        "</InputMethod.InputScope>" +
                        "</TextBox>" +
                        "</StackPanel>";
                    var root = RequireType<StackPanel>(
                        XamlReader.Parse(looseXaml),
                        "external SDK loose XamlReader root");
                    AssertEqual("ExternalLooseRoot", root.Name, "external SDK loose XamlReader root name");
                    AssertEqual(3, root.Children.Count, "external SDK loose XamlReader child count");
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

                    var inputScopeTextBox = RequireType<TextBox>(
                        root.FindName("ExternalLooseInputScopeTextBox"),
                        "external SDK loose XamlReader InputScope TextBox");
                    AssertEqual(true, ReferenceEquals(root.Children[2], inputScopeTextBox), "external SDK loose XamlReader InputScope TextBox child");
                    AssertEqual("External loose input scope text", inputScopeTextBox.Text, "external SDK loose XamlReader InputScope TextBox text");
                    var looseInputScope = InputMethod.GetInputScope(inputScopeTextBox)
                        ?? throw new InvalidOperationException("Expected external SDK loose XamlReader TextBox InputScope.");
                    AssertEqual("[a-z]+", looseInputScope.RegularExpression, "external SDK loose XamlReader InputScope regular expression");
                    AssertEqual("external-loose-input-scope", looseInputScope.SrgsMarkup, "external SDK loose XamlReader InputScope SRGS markup");
                    AssertEqual(1, looseInputScope.Names.Count, "external SDK loose XamlReader InputScope names");
                    var looseInputScopeName = RequireType<InputScopeName>(
                        looseInputScope.Names[0],
                        "external SDK loose XamlReader InputScopeName");
                    AssertEqual(InputScopeNameValue.EmailUserName, looseInputScopeName.NameValue, "external SDK loose XamlReader InputScopeName value");
                    AssertEqual(1, looseInputScope.PhraseList.Count, "external SDK loose XamlReader InputScope phrases");
                    var looseInputScopePhrase = RequireType<InputScopePhrase>(
                        looseInputScope.PhraseList[0],
                        "external SDK loose XamlReader InputScopePhrase");
                    AssertEqual("external loose phrase", looseInputScopePhrase.Name, "external SDK loose XamlReader InputScopePhrase text");

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

                private static void ValidateMarkupExtensions(MainWindow window)
                {
                    var markupText = RequireType<TextBlock>(
                        window.FindName("ExternalMarkupExtensionText"),
                        "external SDK markup extension text block");
                    AssertEqual("external:markup", markupText.Text, "external SDK compiled MarkupExtension provided text");
                    AssertAtLeast(1, ExternalTextExtension.ProvideValueCount, "external SDK compiled MarkupExtension ProvideValue count");
                    AssertEqual("Text", ExternalTextExtension.LastTargetPropertyName, "external SDK compiled MarkupExtension target property");
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

                    var transferTextBox = RequireType<TextBox>(
                        window.FindName("ExternalBindingTransferTextBox"),
                        "external SDK binding transfer text box");
                    var transferBindingExpression = transferTextBox.GetBindingExpression(TextBox.TextProperty)
                        ?? throw new InvalidOperationException("Expected external SDK binding transfer BindingExpression.");
                    AssertEqual("ExternalBindingTransferText", transferBindingExpression.ParentBinding.Path.Path, "external SDK binding transfer path");
                    AssertEqual(BindingMode.TwoWay, transferBindingExpression.ParentBinding.Mode, "external SDK binding transfer mode");
                    AssertEqual(UpdateSourceTrigger.Explicit, transferBindingExpression.ParentBinding.UpdateSourceTrigger, "external SDK binding transfer update source trigger");
                    AssertEqual(true, transferBindingExpression.ParentBinding.NotifyOnSourceUpdated, "external SDK binding transfer NotifyOnSourceUpdated");
                    AssertEqual(true, transferBindingExpression.ParentBinding.NotifyOnTargetUpdated, "external SDK binding transfer NotifyOnTargetUpdated");
                    AssertEqual("external transfer initial", transferTextBox.Text, "external SDK binding transfer initial target value");

                    int targetUpdatedBefore = window.ExternalBindingTargetUpdatedCount;
                    window.ExternalBindingTransferText = "external transfer target refresh";
                    transferBindingExpression.UpdateTarget();
                    DrainDispatcher();
                    AssertEqual("external transfer target refresh", transferTextBox.Text, "external SDK Binding TargetUpdated target value");
                    AssertAtLeast(targetUpdatedBefore + 1, window.ExternalBindingTargetUpdatedCount, "external SDK Binding TargetUpdated routed event");
                    AssertEqual("ExternalBindingTransferTextBox", window.LastExternalBindingTargetUpdatedSenderName, "external SDK Binding TargetUpdated sender");
                    AssertEqual("ExternalBindingTransferTextBox", window.LastExternalBindingTargetUpdatedTargetName, "external SDK Binding TargetUpdated target object");
                    AssertEqual("Text", window.LastExternalBindingTargetUpdatedPropertyName, "external SDK Binding TargetUpdated property");
                    AssertEqual("TargetUpdated", window.LastExternalBindingTargetUpdatedRoutedEventName, "external SDK Binding TargetUpdated routed event name");

                    int sourceUpdatedBefore = window.ExternalBindingSourceUpdatedCount;
                    transferTextBox.Text = "external transfer source refresh";
                    transferBindingExpression.UpdateSource();
                    DrainDispatcher();
                    AssertEqual("external transfer source refresh", window.ExternalBindingTransferText, "external SDK Binding SourceUpdated source value");
                    AssertAtLeast(sourceUpdatedBefore + 1, window.ExternalBindingSourceUpdatedCount, "external SDK Binding SourceUpdated routed event");
                    AssertEqual("ExternalBindingTransferTextBox", window.LastExternalBindingSourceUpdatedSenderName, "external SDK Binding SourceUpdated sender");
                    AssertEqual("ExternalBindingTransferTextBox", window.LastExternalBindingSourceUpdatedTargetName, "external SDK Binding SourceUpdated target object");
                    AssertEqual("Text", window.LastExternalBindingSourceUpdatedPropertyName, "external SDK Binding SourceUpdated property");
                    AssertEqual("SourceUpdated", window.LastExternalBindingSourceUpdatedRoutedEventName, "external SDK Binding SourceUpdated routed event name");

                    var ancestorBindingText = RequireType<TextBlock>(
                        window.FindName("ExternalAncestorBindingText"),
                        "external SDK ancestor binding text block");
                    DrainDispatcher();
                    AssertEqual("External ancestor tag", ancestorBindingText.Text, "external SDK RelativeSource ancestor binding value");
                    var ancestorBindingExpression = ancestorBindingText.GetBindingExpression(TextBlock.TextProperty)
                        ?? throw new InvalidOperationException("Expected external SDK RelativeSource ancestor BindingExpression.");
                    AssertEqual("Tag", ancestorBindingExpression.ParentBinding.Path.Path, "external SDK RelativeSource ancestor binding path");
                    var ancestorRelativeSource = ancestorBindingExpression.ParentBinding.RelativeSource
                        ?? throw new InvalidOperationException("Expected external SDK RelativeSource ancestor binding metadata.");
                    AssertEqual(RelativeSourceMode.FindAncestor, ancestorRelativeSource.Mode, "external SDK RelativeSource ancestor mode");
                    AssertEqual(typeof(Border), ancestorRelativeSource.AncestorType, "external SDK RelativeSource ancestor type");
                    AssertEqual(1, ancestorRelativeSource.AncestorLevel, "external SDK RelativeSource ancestor level");

                    var validationTextBox = RequireType<TextBox>(
                        window.FindName("ExternalValidationTextBox"),
                        "external SDK validation text box");
                    var textBindingExpression = validationTextBox.GetBindingExpression(TextBox.TextProperty)
                        ?? throw new InvalidOperationException("Expected external SDK validation BindingExpression.");
                    AssertEqual(true, textBindingExpression.ParentBinding.NotifyOnValidationError, "external SDK validation binding NotifyOnValidationError");
                    AssertEqual("valid external text", validationTextBox.Text, "external SDK validation text initial value");
                    var validationInputScope = InputMethod.GetInputScope(validationTextBox)
                        ?? throw new InvalidOperationException("Expected external SDK validation TextBox InputScope.");
                    AssertEqual("[A-Z0-9]+", validationInputScope.RegularExpression, "external SDK compiled InputScope regular expression");
                    AssertEqual("external-sdk-input-scope", validationInputScope.SrgsMarkup, "external SDK compiled InputScope SRGS markup");
                    AssertEqual(1, validationInputScope.Names.Count, "external SDK compiled InputScope names");
                    var validationInputScopeName = RequireType<InputScopeName>(
                        validationInputScope.Names[0],
                        "external SDK compiled InputScopeName");
                    AssertEqual(InputScopeNameValue.EmailSmtpAddress, validationInputScopeName.NameValue, "external SDK compiled InputScopeName value");
                    AssertEqual(1, validationInputScope.PhraseList.Count, "external SDK compiled InputScope phrases");
                    var validationInputScopePhrase = RequireType<InputScopePhrase>(
                        validationInputScope.PhraseList[0],
                        "external SDK compiled InputScopePhrase");
                    AssertEqual("external package phrase", validationInputScopePhrase.Name, "external SDK compiled InputScopePhrase text");
                    int textChangedBeforeValidation = window.ExternalValidationTextChangedCount;
                    int validationAddedBefore = window.ExternalValidationErrorAddedCount;
                    int validationRemovedBefore = window.ExternalValidationErrorRemovedCount;
                    validationTextBox.Text = string.Empty;
                    textBindingExpression.UpdateSource();
                    AssertEqual(true, Validation.GetHasError(validationTextBox), "external SDK validation failure state");
                    AssertEqual(1, Validation.GetErrors(validationTextBox).Count, "external SDK validation failure error count");
                    AssertAtLeast(validationAddedBefore + 1, window.ExternalValidationErrorAddedCount, "external SDK validation error added count");
                    AssertEqual(validationRemovedBefore, window.ExternalValidationErrorRemovedCount, "external SDK validation error removed count before recovery");
                    AssertEqual("Added", window.LastExternalValidationErrorAction, "external SDK validation error added action");
                    AssertEqual("External value is required", window.LastExternalValidationErrorContent, "external SDK validation error added content");
                    AssertEqual("ValidationError", window.LastExternalValidationErrorRoutedEventName, "external SDK validation error added routed event");
                    AssertEqual("ExternalValidationTextBox", window.LastExternalValidationErrorSenderName, "external SDK validation error added sender");
                    AssertAtLeast(textChangedBeforeValidation + 1, window.ExternalValidationTextChangedCount, "external SDK TextBox validation TextChanged failure count");
                    AssertEqual(string.Empty, window.LastExternalValidationText, "external SDK TextBox validation TextChanged failure text");
                    validationTextBox.Text = "recovered external text";
                    textBindingExpression.UpdateSource();
                    AssertEqual(false, Validation.GetHasError(validationTextBox), "external SDK validation recovery state");
                    AssertEqual(0, Validation.GetErrors(validationTextBox).Count, "external SDK validation recovery error count");
                    AssertAtLeast(validationRemovedBefore + 1, window.ExternalValidationErrorRemovedCount, "external SDK validation error removed count");
                    AssertEqual("Removed", window.LastExternalValidationErrorAction, "external SDK validation error removed action");
                    AssertEqual("External value is required", window.LastExternalValidationErrorContent, "external SDK validation error removed content");
                    AssertEqual("ValidationError", window.LastExternalValidationErrorRoutedEventName, "external SDK validation error removed routed event");
                    AssertEqual("ExternalValidationTextBox", window.LastExternalValidationErrorSenderName, "external SDK validation error removed sender");
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

                private static void ValidateInputManagers(MainWindow window)
                {
                    var validationTextBox = RequireType<TextBox>(
                        window.FindName("ExternalValidationTextBox"),
                        "external SDK input manager text box");

                    var inputLanguageManager = InputLanguageManager.Current;
                    AssertEqual(true, inputLanguageManager.AvailableInputLanguages.Cast<object>().Any(), "external SDK InputLanguageManager available language count");
                    var attachedLanguage = InputLanguageManager.GetInputLanguage(validationTextBox);
                    AssertEqual("en-US", attachedLanguage.Name, "external SDK compiled InputLanguageManager attached language");

                    if (!OperatingSystem.IsWindows())
                    {
                        AssertEqual(CultureInfo.CurrentCulture.Name, inputLanguageManager.CurrentInputLanguage.Name, "external SDK InputLanguageManager current culture");
                        var requestedLanguage = CultureInfo.GetCultureInfo("en-US");
                        inputLanguageManager.CurrentInputLanguage = requestedLanguage;
                        AssertEqual("en-US", inputLanguageManager.CurrentInputLanguage.Name, "external SDK InputLanguageManager set current language");
                        inputLanguageManager.CurrentInputLanguage = CultureInfo.CurrentCulture;
                    }

                    AssertEqual(InputMethodState.On, InputMethod.GetPreferredImeState(validationTextBox), "external SDK compiled InputMethod preferred IME state");
                    AssertEqual(ImeConversionModeValues.Native | ImeConversionModeValues.FullShape, InputMethod.GetPreferredImeConversionMode(validationTextBox), "external SDK compiled InputMethod preferred conversion mode");
                    AssertEqual(ImeSentenceModeValues.Automatic, InputMethod.GetPreferredImeSentenceMode(validationTextBox), "external SDK compiled InputMethod preferred sentence mode");

                    var inputMethod = InputMethod.Current;
                    if (!OperatingSystem.IsWindows())
                    {
                        AssertEqual(InputMethodState.Off, inputMethod.ImeState, "external SDK InputMethod default IME state");
                        AssertEqual(ImeConversionModeValues.Alphanumeric, inputMethod.ImeConversionMode, "external SDK InputMethod default conversion mode");
                        AssertEqual(ImeSentenceModeValues.None, inputMethod.ImeSentenceMode, "external SDK InputMethod default sentence mode");
                        AssertEqual(false, inputMethod.CanShowConfigurationUI, "external SDK InputMethod configure UI availability");
                        AssertEqual(false, inputMethod.CanShowRegisterWordUI, "external SDK InputMethod register-word UI availability");

                        inputMethod.ImeState = InputMethodState.On;
                        inputMethod.MicrophoneState = InputMethodState.On;
                        inputMethod.HandwritingState = InputMethodState.On;
                        inputMethod.SpeechMode = SpeechMode.Dictation;
                        inputMethod.ImeConversionMode = ImeConversionModeValues.Native | ImeConversionModeValues.FullShape;
                        inputMethod.ImeSentenceMode = ImeSentenceModeValues.Automatic;
                        AssertEqual(InputMethodState.On, inputMethod.ImeState, "external SDK InputMethod set IME state");
                        AssertEqual(InputMethodState.On, inputMethod.MicrophoneState, "external SDK InputMethod set microphone state");
                        AssertEqual(InputMethodState.On, inputMethod.HandwritingState, "external SDK InputMethod set handwriting state");
                        AssertEqual(SpeechMode.Dictation, inputMethod.SpeechMode, "external SDK InputMethod set speech mode");
                        AssertEqual(ImeConversionModeValues.Native | ImeConversionModeValues.FullShape, inputMethod.ImeConversionMode, "external SDK InputMethod set conversion mode");
                        AssertEqual(ImeSentenceModeValues.Automatic, inputMethod.ImeSentenceMode, "external SDK InputMethod set sentence mode");

                        inputMethod.ImeState = InputMethodState.Off;
                        inputMethod.MicrophoneState = InputMethodState.Off;
                        inputMethod.HandwritingState = InputMethodState.Off;
                        inputMethod.ImeConversionMode = ImeConversionModeValues.Alphanumeric;
                        inputMethod.ImeSentenceMode = ImeSentenceModeValues.None;
                    }
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

                private static void ValidateRoutedEvents(MainWindow window)
                {
                    var panel = RequireType<StackPanel>(
                        window.FindName("ExternalRoutedEventPanel"),
                        "external SDK routed event panel");
                    var control = RequireType<ExternalRoutedEventControl>(
                        window.FindName("ExternalRoutedEventControl"),
                        "external SDK custom routed event control");

                    AssertEqual("ExternalBubble", ExternalRoutedEventControl.ExternalBubbleEvent.Name, "external SDK custom bubble routed event name");
                    AssertEqual(RoutingStrategy.Bubble, ExternalRoutedEventControl.ExternalBubbleEvent.RoutingStrategy, "external SDK custom bubble routing strategy");
                    AssertEqual("ExternalTunnel", ExternalRoutedEventControl.ExternalTunnelEvent.Name, "external SDK custom tunnel routed event name");
                    AssertEqual(RoutingStrategy.Tunnel, ExternalRoutedEventControl.ExternalTunnelEvent.RoutingStrategy, "external SDK custom tunnel routing strategy");

                    var bubblePanelSenderName = string.Empty;
                    var bubblePanelOriginalSourceName = string.Empty;
                    panel.AddHandler(
                        ExternalRoutedEventControl.ExternalBubbleEvent,
                        new RoutedEventHandler((sender, e) =>
                        {
                            bubblePanelSenderName = (sender as FrameworkElement)?.Name ?? string.Empty;
                            bubblePanelOriginalSourceName = (e.OriginalSource as FrameworkElement)?.Name ?? string.Empty;
                        }));
                    control.RaiseExternalBubble();
                    AssertEqual(1, window.ExternalBubbleRoutedEventCount, "external SDK custom bubble routed event XAML handler count");
                    AssertEqual("ExternalRoutedEventControl", window.LastExternalBubbleSenderName, "external SDK custom bubble source handler sender");
                    AssertEqual("ExternalRoutedEventControl", window.LastExternalBubbleOriginalSourceName, "external SDK custom bubble original source");
                    AssertEqual("ExternalBubble", window.LastExternalBubbleRoutedEventName, "external SDK custom bubble routed event name from args");
                    AssertEqual("ExternalRoutedEventPanel", bubblePanelSenderName, "external SDK custom bubble AddHandler panel sender");
                    AssertEqual("ExternalRoutedEventControl", bubblePanelOriginalSourceName, "external SDK custom bubble AddHandler original source");

                    var tunnelPanelSenderName = string.Empty;
                    var tunnelPanelOriginalSourceName = string.Empty;
                    panel.AddHandler(
                        ExternalRoutedEventControl.ExternalTunnelEvent,
                        new RoutedEventHandler((sender, e) =>
                        {
                            tunnelPanelSenderName = (sender as FrameworkElement)?.Name ?? string.Empty;
                            tunnelPanelOriginalSourceName = (e.OriginalSource as FrameworkElement)?.Name ?? string.Empty;
                        }));
                    control.RaiseExternalTunnel();
                    AssertEqual(1, window.ExternalTunnelRoutedEventCount, "external SDK custom tunnel routed event XAML handler count");
                    AssertEqual("ExternalRoutedEventControl", window.LastExternalTunnelSenderName, "external SDK custom tunnel source handler sender");
                    AssertEqual("ExternalRoutedEventControl", window.LastExternalTunnelOriginalSourceName, "external SDK custom tunnel original source");
                    AssertEqual("ExternalTunnel", window.LastExternalTunnelRoutedEventName, "external SDK custom tunnel routed event name from args");
                    AssertEqual("ExternalRoutedEventPanel", tunnelPanelSenderName, "external SDK custom tunnel AddHandler panel sender");
                    AssertEqual("ExternalRoutedEventControl", tunnelPanelOriginalSourceName, "external SDK custom tunnel AddHandler original source");
                }

                private static void ValidatePortableDragDrop(MainWindow window)
                {
                    Type serviceType = typeof(Window).Assembly.GetType("System.Windows.PortableWindowActivationService", throwOnError: true)!;
                    MethodInfo processDragDrop = serviceType.GetMethod(
                        "ProcessDragDrop",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                        ?? throw new InvalidOperationException("Expected portable window activation service to expose ProcessDragDrop.");
                    MethodInfo processDragDropEvent = serviceType.GetMethod(
                        "ProcessDragDropEvent",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                        ?? throw new InvalidOperationException("Expected portable window activation service to expose ProcessDragDropEvent.");

                    var allowedEffects = DragDropEffects.Copy | DragDropEffects.Move;
                    int enterResult = InvokePortableDragDrop(
                        processDragDropEvent,
                        window,
                        dragDropEventKind: 1,
                        file: "/tmp/external-sdk-enter.txt",
                        text: "external SDK enter text",
                        x: 10.0,
                        y: 20.0,
                        allowedEffects,
                        DragDropEffects.Copy);
                    int overResult = InvokePortableDragDrop(
                        processDragDropEvent,
                        window,
                        dragDropEventKind: 2,
                        file: "/tmp/external-sdk-over.txt",
                        text: "external SDK over text",
                        x: 12.0,
                        y: 24.0,
                        allowedEffects,
                        DragDropEffects.Copy);
                    int leaveResult = InvokePortableDragDrop(
                        processDragDropEvent,
                        window,
                        dragDropEventKind: 3,
                        file: "/tmp/external-sdk-leave.txt",
                        text: "external SDK leave text",
                        x: 13.0,
                        y: 26.0,
                        allowedEffects,
                        DragDropEffects.Copy);
                    object? result = processDragDropEvent.Invoke(
                        null,
                        [
                            window,
                            0,
                            new[] { "/tmp/external-sdk-drop.txt" },
                            "external SDK drop text",
                            14.0,
                            28.0,
                            (int)allowedEffects,
                            (int)DragDropEffects.Copy
                        ]);
                    object? wrapperResult = processDragDrop.Invoke(
                        null,
                        [
                            window,
                            new[] { "/tmp/external-sdk-wrapper-drop.txt" },
                            "external SDK wrapper drop text",
                            16.0,
                            32.0,
                            (int)allowedEffects,
                            (int)DragDropEffects.Copy
                        ]);

                    AssertEqual((int)DragDropEffects.Move, enterResult, "external SDK portable drag-enter accepted effect");
                    AssertEqual((int)DragDropEffects.Move, overResult, "external SDK portable drag-over accepted effect");
                    AssertEqual((int)DragDropEffects.Copy, leaveResult, "external SDK portable drag-leave fallback effect");
                    AssertEqual((int)DragDropEffects.Move, (int)result!, "external SDK portable drag/drop accepted effect");
                    AssertEqual((int)DragDropEffects.Move, (int)wrapperResult!, "external SDK portable drag/drop wrapper accepted effect");
                    AssertEqual(1, window.ExternalPreviewDragEnterCount, "external SDK portable drag-enter preview count");
                    AssertEqual(1, window.ExternalDragEnterCount, "external SDK portable drag-enter count");
                    AssertEqual(1, window.ExternalPreviewDragOverCount, "external SDK portable drag-over preview count");
                    AssertEqual(1, window.ExternalDragOverCount, "external SDK portable drag-over count");
                    AssertEqual(1, window.ExternalPreviewDragLeaveCount, "external SDK portable drag-leave preview count");
                    AssertEqual(1, window.ExternalDragLeaveCount, "external SDK portable drag-leave count");
                    AssertEqual(2, window.ExternalPreviewDropCount, "external SDK portable drag/drop preview count");
                    AssertEqual(2, window.ExternalDropCount, "external SDK portable drag/drop drop count");
                    AssertEqual("PreviewDragEnter", window.LastExternalPreviewDragEnterRoutedEventName, "external SDK portable drag-enter preview event");
                    AssertEqual("DragEnter", window.LastExternalDragEnterRoutedEventName, "external SDK portable drag-enter event");
                    AssertEqual("PreviewDragOver", window.LastExternalPreviewDragOverRoutedEventName, "external SDK portable drag-over preview event");
                    AssertEqual("DragOver", window.LastExternalDragOverRoutedEventName, "external SDK portable drag-over event");
                    AssertEqual("PreviewDragLeave", window.LastExternalPreviewDragLeaveRoutedEventName, "external SDK portable drag-leave preview event");
                    AssertEqual("DragLeave", window.LastExternalDragLeaveRoutedEventName, "external SDK portable drag-leave event");
                    AssertEqual("PreviewDrop", window.LastExternalPreviewDropRoutedEventName, "external SDK portable drag/drop preview event");
                    AssertEqual("Drop", window.LastExternalDropRoutedEventName, "external SDK portable drag/drop event");
                    AssertEqual("external SDK wrapper drop text", window.LastExternalDropText, "external SDK portable drag/drop text");
                    AssertEqual(1, window.LastExternalDropFileCount, "external SDK portable drag/drop file count");
                    AssertEqual("/tmp/external-sdk-wrapper-drop.txt", window.LastExternalDropFirstFile, "external SDK portable drag/drop first file");
                    AssertEqual(allowedEffects.ToString(), window.LastExternalDragEnterAllowedEffects, "external SDK portable drag-enter allowed effects");
                    AssertEqual(allowedEffects.ToString(), window.LastExternalDragOverAllowedEffects, "external SDK portable drag-over allowed effects");
                    AssertEqual(allowedEffects.ToString(), window.LastExternalDropAllowedEffects, "external SDK portable drag/drop allowed effects");
                    AssertEqual(DragDropEffects.Move.ToString(), window.LastExternalDragEnterEffects, "external SDK portable drag-enter handler effect");
                    AssertEqual(DragDropEffects.Move.ToString(), window.LastExternalDragOverEffects, "external SDK portable drag-over handler effect");
                    AssertEqual(DragDropEffects.Move.ToString(), window.LastExternalDropEffects, "external SDK portable drag/drop handler effect");
                    AssertEqual(16.0, window.LastExternalDropX, "external SDK portable drag/drop x");
                    AssertEqual(32.0, window.LastExternalDropY, "external SDK portable drag/drop y");

                    static int InvokePortableDragDrop(
                        MethodInfo method,
                        MainWindow window,
                        int dragDropEventKind,
                        string file,
                        string text,
                        double x,
                        double y,
                        DragDropEffects allowedEffects,
                        DragDropEffects acceptedEffect)
                    {
                        object? result = method.Invoke(
                            null,
                            [
                                window,
                                dragDropEventKind,
                                new[] { file },
                                text,
                                x,
                                y,
                                (int)allowedEffects,
                                (int)acceptedEffect
                            ]);
                        return (int)result!;
                    }
                }

                private static void ValidateDependencyProperties(MainWindow window)
                {
                    var panel = RequireType<StackPanel>(
                        window.FindName("ExternalDependencyPropertyPanel"),
                        "external SDK dependency-property panel");
                    var control = RequireType<ExternalDependencyPropertyControl>(
                        window.FindName("ExternalDependencyPropertyControl"),
                        "external SDK dependency-property control");
                    var localControl = RequireType<ExternalDependencyPropertyControl>(
                        window.FindName("ExternalDependencyPropertyLocalControl"),
                        "external SDK local dependency-property control");

                    AssertEqual("External inherited label", ExternalDependencyPropertyControl.GetInheritedLabel(control), "external SDK inherited attached property value");
                    var inheritedSource = DependencyPropertyHelper.GetValueSource(
                        control,
                        ExternalDependencyPropertyControl.InheritedLabelProperty);
                    AssertEqual(BaseValueSource.Inherited, inheritedSource.BaseValueSource, "external SDK inherited attached property value source");

                    AssertEqual("External local label", ExternalDependencyPropertyControl.GetInheritedLabel(localControl), "external SDK local attached property value");
                    var localSource = DependencyPropertyHelper.GetValueSource(
                        localControl,
                        ExternalDependencyPropertyControl.InheritedLabelProperty);
                    AssertEqual(BaseValueSource.Local, localSource.BaseValueSource, "external SDK local attached property value source");

                    ExternalDependencyPropertyControl.SetInheritedLabel(panel, "External inherited label updated");
                    AssertEqual("External inherited label updated", ExternalDependencyPropertyControl.GetInheritedLabel(control), "external SDK inherited attached property update");
                    AssertEqual("External local label", ExternalDependencyPropertyControl.GetInheritedLabel(localControl), "external SDK local attached property precedence");

                    AssertEqual(100, control.CoercedNumber, "external SDK coerced dependency property value");
                    var coercedSource = DependencyPropertyHelper.GetValueSource(
                        control,
                        ExternalDependencyPropertyControl.CoercedNumberProperty);
                    AssertEqual(BaseValueSource.Local, coercedSource.BaseValueSource, "external SDK coerced dependency property base source");
                    AssertEqual(true, coercedSource.IsCoerced, "external SDK coerced dependency property source flag");
                    AssertAtLeast(1, control.CoercedNumberChangeCount, "external SDK coerced dependency property initial callback count");
                    AssertEqual(100, control.LastCoercedNumberNewValue, "external SDK coerced dependency property initial callback new value");

                    control.CoercedNumber = -7;
                    AssertEqual(0, control.CoercedNumber, "external SDK coerced dependency property lower clamp");
                    AssertEqual(0, control.LastCoercedNumberNewValue, "external SDK coerced dependency property lower callback new value");

                    control.CoercedNumber = 64;
                    AssertEqual(64, control.CoercedNumber, "external SDK coerced dependency property in-range value");
                    var inRangeSource = DependencyPropertyHelper.GetValueSource(
                        control,
                        ExternalDependencyPropertyControl.CoercedNumberProperty);
                    AssertEqual(false, inRangeSource.IsCoerced, "external SDK in-range dependency property coercion flag");

                    AssertEqual(42, localControl.CoercedNumber, "external SDK local dependency property in-range XAML value");
                    AssertEqual("compiled tracked text", control.TrackedText, "external SDK dependency property tracked text value");
                    AssertEqual("compiled tracked text", control.ReadLocalValue(ExternalDependencyPropertyControl.TrackedTextProperty), "external SDK dependency property tracked text local value");
                    var trackedTextChangeCount = control.TrackedTextChangeCount;
                    control.TrackedText = "runtime tracked text";
                    AssertEqual("runtime tracked text", control.TrackedText, "external SDK dependency property runtime text value");
                    AssertEqual(trackedTextChangeCount + 1, control.TrackedTextChangeCount, "external SDK dependency property changed callback count");
                    AssertEqual("compiled tracked text", control.LastTrackedTextOldValue, "external SDK dependency property changed callback old value");
                    AssertEqual("runtime tracked text", control.LastTrackedTextNewValue, "external SDK dependency property changed callback new value");
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

                    styledButton.Tag = "template-trigger-active";
                    DrainDispatcher();
                    AssertClose(23.0, templateContent.MinWidth, "external SDK Application.Run control template trigger EnterActions MinWidth");

                    styledButton.Tag = "base-style";
                    DrainDispatcher();
                    AssertClose(0.0, templateContent.MinWidth, "external SDK Application.Run control template trigger ExitActions MinWidth");
                }

                private static void ValidateLoadedStoryboardMetadata(MainWindow window)
                {
                    var loadedStoryboardText = RequireType<TextBlock>(
                        window.FindName("ExternalLoadedStoryboardText"),
                        "external SDK loaded storyboard text");
                    AssertEqual("External loaded storyboard target", loadedStoryboardText.Text, "external SDK loaded storyboard text content");
                    AssertClose(1.0, loadedStoryboardText.Opacity, "external SDK loaded storyboard initial opacity");
                    AssertEqual(1, loadedStoryboardText.Triggers.Count, "external SDK loaded storyboard trigger count");
                    var eventTrigger = RequireType<EventTrigger>(
                        loadedStoryboardText.Triggers[0],
                        "external SDK loaded storyboard event trigger");
                    AssertEqual(FrameworkElement.LoadedEvent, eventTrigger.RoutedEvent, "external SDK loaded storyboard routed event");
                    AssertEqual(1, eventTrigger.Actions.Count, "external SDK loaded storyboard action count");
                    var beginStoryboard = RequireType<BeginStoryboard>(
                        eventTrigger.Actions[0],
                        "external SDK loaded storyboard begin action");
                    var storyboard = RequireType<Storyboard>(
                        beginStoryboard.Storyboard,
                        "external SDK loaded storyboard");
                    AssertEqual(1, storyboard.Children.Count, "external SDK loaded storyboard child count");
                    var doubleAnimation = RequireType<DoubleAnimation>(
                        storyboard.Children[0],
                        "external SDK loaded storyboard double animation");
                    AssertEqual(0.37, doubleAnimation.To ?? double.NaN, "external SDK loaded storyboard animation target value");
                    AssertEqual(TimeSpan.Zero, doubleAnimation.Duration.TimeSpan, "external SDK loaded storyboard animation duration");
                    AssertEqual(FillBehavior.HoldEnd, doubleAnimation.FillBehavior, "external SDK loaded storyboard fill behavior");
                    AssertEqual("ExternalLoadedStoryboardText", Storyboard.GetTargetName(doubleAnimation), "external SDK loaded storyboard target name");
                    var targetProperty = RequireType<PropertyPath>(
                        Storyboard.GetTargetProperty(doubleAnimation),
                        "external SDK loaded storyboard target property");
                    AssertEqual("Opacity", targetProperty.Path?.ToString() ?? string.Empty, "external SDK loaded storyboard target property path");
                    AssertEqual(0, window.ExternalLoadedStoryboardTextLoadedCount, "external SDK loaded storyboard initial handler count");
                }

                private static void ValidateLoadedStoryboardAfterRun(MainWindow window)
                {
                    DrainDispatcher();

                    var loadedStoryboardText = RequireType<TextBlock>(
                        window.FindName("ExternalLoadedStoryboardText"),
                        "external SDK Application.Run loaded storyboard text");
                    AssertClose(0.37, loadedStoryboardText.Opacity, "external SDK Application.Run loaded storyboard opacity");
                    AssertAtLeast(1, window.ExternalLoadedStoryboardTextLoadedCount, "external SDK loaded storyboard handler count");
                    AssertEqual("Loaded", window.LastExternalLoadedStoryboardTextRoutedEventName, "external SDK loaded storyboard routed event name");
                }

                private static void ValidatePropertyTriggerActionsMetadata(MainWindow window)
                {
                    var actionStyle = RequireType<Style>(
                        window.FindResource("ExternalPropertyTriggerActionTextStyle"),
                        "external SDK property trigger action style");
                    AssertEqual(typeof(TextBlock), actionStyle.TargetType, "external SDK property trigger action style target type");
                    AssertEqual(3, actionStyle.Setters.Count, "external SDK property trigger action style setter count");
                    AssertEqual(1, actionStyle.Triggers.Count, "external SDK property trigger action style trigger count");
                    var trigger = RequireType<Trigger>(
                        actionStyle.Triggers[0],
                        "external SDK property trigger action trigger");
                    AssertEqual(UIElement.IsEnabledProperty, trigger.Property, "external SDK property trigger action property");
                    AssertEqual("True", trigger.Value?.ToString(), "external SDK property trigger action value");
                    AssertEqual(1, trigger.EnterActions.Count, "external SDK property trigger action EnterActions count");
                    AssertTriggerActionStoryboard(trigger.EnterActions[0], 0.43, "external SDK property trigger action EnterActions");
                    AssertEqual(1, trigger.ExitActions.Count, "external SDK property trigger action ExitActions count");
                    AssertTriggerActionStoryboard(trigger.ExitActions[0], 0.91, "external SDK property trigger action ExitActions");

                    var actionText = RequireType<TextBlock>(
                        window.FindName("ExternalPropertyTriggerActionText"),
                        "external SDK property trigger action text");
                    AssertEqual(actionStyle, actionText.Style, "external SDK property trigger action text style");
                    AssertEqual("External property trigger action target", actionText.Text, "external SDK property trigger action text content");
                    AssertEqual(false, actionText.IsEnabled, "external SDK property trigger action initial IsEnabled");
                    AssertClose(0.91, actionText.Opacity, "external SDK property trigger action initial opacity");
                }

                private static void ValidatePropertyTriggerActionsAfterRun(MainWindow window)
                {
                    DrainDispatcher();

                    var actionText = RequireType<TextBlock>(
                        window.FindName("ExternalPropertyTriggerActionText"),
                        "external SDK Application.Run property trigger action text");
                    AssertClose(0.91, actionText.Opacity, "external SDK Application.Run property trigger action initial opacity");

                    actionText.IsEnabled = true;
                    DrainDispatcher();
                    AssertClose(0.43, actionText.Opacity, "external SDK Application.Run property trigger EnterActions opacity");

                    actionText.IsEnabled = false;
                    DrainDispatcher();
                    AssertClose(0.91, actionText.Opacity, "external SDK Application.Run property trigger ExitActions opacity");
                }

                private static void ValidateMultiTriggerActionsMetadata(MainWindow window)
                {
                    var actionStyle = RequireType<Style>(
                        window.FindResource("ExternalMultiTriggerActionTextStyle"),
                        "external SDK multi trigger action style");
                    AssertEqual(typeof(TextBlock), actionStyle.TargetType, "external SDK multi trigger action style target type");
                    AssertEqual(4, actionStyle.Setters.Count, "external SDK multi trigger action style setter count");
                    AssertEqual(1, actionStyle.Triggers.Count, "external SDK multi trigger action style trigger count");
                    var multiTrigger = RequireType<MultiTrigger>(
                        actionStyle.Triggers[0],
                        "external SDK multi trigger action trigger");
                    AssertEqual(2, multiTrigger.Conditions.Count, "external SDK multi trigger action condition count");
                    AssertEqual(UIElement.IsEnabledProperty, multiTrigger.Conditions[0].Property, "external SDK multi trigger action first property");
                    AssertEqual("True", multiTrigger.Conditions[0].Value?.ToString(), "external SDK multi trigger action first value");
                    AssertEqual(FrameworkElement.TagProperty, multiTrigger.Conditions[1].Property, "external SDK multi trigger action second property");
                    AssertEqual("Armed", multiTrigger.Conditions[1].Value?.ToString(), "external SDK multi trigger action second value");
                    AssertEqual(1, multiTrigger.EnterActions.Count, "external SDK multi trigger action EnterActions count");
                    AssertTriggerActionStoryboard(multiTrigger.EnterActions[0], 0.58, "external SDK multi trigger action EnterActions");
                    AssertEqual(1, multiTrigger.ExitActions.Count, "external SDK multi trigger action ExitActions count");
                    AssertTriggerActionStoryboard(multiTrigger.ExitActions[0], 0.88, "external SDK multi trigger action ExitActions");

                    var actionText = RequireType<TextBlock>(
                        window.FindName("ExternalMultiTriggerActionText"),
                        "external SDK multi trigger action text");
                    AssertEqual(actionStyle, actionText.Style, "external SDK multi trigger action text style");
                    AssertEqual("External multi trigger action target", actionText.Text, "external SDK multi trigger action text content");
                    AssertEqual(false, actionText.IsEnabled, "external SDK multi trigger action initial IsEnabled");
                    AssertEqual("Disarmed", actionText.Tag?.ToString() ?? string.Empty, "external SDK multi trigger action initial Tag");
                    AssertClose(0.88, actionText.Opacity, "external SDK multi trigger action initial opacity");
                }

                private static void ValidateMultiTriggerActionsAfterRun(MainWindow window)
                {
                    DrainDispatcher();

                    var actionText = RequireType<TextBlock>(
                        window.FindName("ExternalMultiTriggerActionText"),
                        "external SDK Application.Run multi trigger action text");
                    AssertClose(0.88, actionText.Opacity, "external SDK Application.Run multi trigger action initial opacity");

                    actionText.IsEnabled = true;
                    DrainDispatcher();
                    AssertClose(0.88, actionText.Opacity, "external SDK Application.Run multi trigger action partial-condition opacity");

                    actionText.Tag = "Armed";
                    DrainDispatcher();
                    AssertClose(0.58, actionText.Opacity, "external SDK Application.Run multi trigger EnterActions opacity");

                    actionText.IsEnabled = false;
                    DrainDispatcher();
                    AssertClose(0.88, actionText.Opacity, "external SDK Application.Run multi trigger ExitActions opacity");

                    actionText.IsEnabled = true;
                    DrainDispatcher();
                    AssertClose(0.58, actionText.Opacity, "external SDK Application.Run multi trigger re-enter opacity");

                    actionText.Tag = "Disarmed";
                    DrainDispatcher();
                    AssertClose(0.88, actionText.Opacity, "external SDK Application.Run multi trigger final ExitActions opacity");
                }

                private static void ValidateDataTriggerActionsMetadata(MainWindow window)
                {
                    var actionStyle = RequireType<Style>(
                        window.FindResource("ExternalDataTriggerActionTextStyle"),
                        "external SDK data trigger action style");
                    AssertEqual(typeof(TextBlock), actionStyle.TargetType, "external SDK data trigger action style target type");
                    AssertEqual(2, actionStyle.Setters.Count, "external SDK data trigger action style setter count");
                    AssertEqual(1, actionStyle.Triggers.Count, "external SDK data trigger action style trigger count");
                    var dataTrigger = RequireType<DataTrigger>(
                        actionStyle.Triggers[0],
                        "external SDK data trigger action trigger");
                    var binding = RequireType<Binding>(
                        dataTrigger.Binding,
                        "external SDK data trigger action binding");
                    AssertEqual("IsExternalDataTriggerActionActive", binding.Path.Path, "external SDK data trigger action binding path");
                    AssertEqual("True", dataTrigger.Value?.ToString(), "external SDK data trigger action value");
                    AssertEqual(1, dataTrigger.EnterActions.Count, "external SDK data trigger action EnterActions count");
                    AssertTriggerActionStoryboard(dataTrigger.EnterActions[0], 0.31, "external SDK data trigger action EnterActions");
                    AssertEqual(1, dataTrigger.ExitActions.Count, "external SDK data trigger action ExitActions count");
                    AssertTriggerActionStoryboard(dataTrigger.ExitActions[0], 0.82, "external SDK data trigger action ExitActions");

                    var actionText = RequireType<TextBlock>(
                        window.FindName("ExternalDataTriggerActionText"),
                        "external SDK data trigger action text");
                    AssertEqual(actionStyle, actionText.Style, "external SDK data trigger action text style");
                    AssertEqual("External data trigger action target", actionText.Text, "external SDK data trigger action text content");
                    AssertClose(0.82, actionText.Opacity, "external SDK data trigger action initial opacity");
                }

                private static DoubleAnimation AssertTriggerActionStoryboard(
                    TriggerAction action,
                    double expectedTo,
                    string description,
                    string expectedTargetProperty = "Opacity")
                {
                    var beginStoryboard = RequireType<BeginStoryboard>(
                        action,
                        description + " BeginStoryboard");
                    var storyboard = RequireType<Storyboard>(
                        beginStoryboard.Storyboard,
                        description + " storyboard");
                    AssertEqual(1, storyboard.Children.Count, description + " storyboard child count");
                    var doubleAnimation = RequireType<DoubleAnimation>(
                        storyboard.Children[0],
                        description + " animation");
                    AssertEqual(expectedTo, doubleAnimation.To ?? double.NaN, description + " target value");
                    AssertEqual(TimeSpan.Zero, doubleAnimation.Duration.TimeSpan, description + " duration");
                    AssertEqual(FillBehavior.HoldEnd, doubleAnimation.FillBehavior, description + " fill behavior");
                    var targetProperty = RequireType<PropertyPath>(
                        Storyboard.GetTargetProperty(doubleAnimation),
                        description + " target property");
                    AssertEqual(expectedTargetProperty, targetProperty.Path?.ToString() ?? string.Empty, description + " target property path");
                    return doubleAnimation;
                }

                private static void ValidateDataTriggerActionsAfterRun(MainWindow window)
                {
                    DrainDispatcher();

                    var actionText = RequireType<TextBlock>(
                        window.FindName("ExternalDataTriggerActionText"),
                        "external SDK Application.Run data trigger action text");
                    AssertClose(0.82, actionText.Opacity, "external SDK Application.Run data trigger action initial opacity");

                    window.IsExternalDataTriggerActionActive = true;
                    DrainDispatcher();
                    AssertClose(0.31, actionText.Opacity, "external SDK Application.Run data trigger EnterActions opacity");

                    window.IsExternalDataTriggerActionActive = false;
                    DrainDispatcher();
                    AssertClose(0.82, actionText.Opacity, "external SDK Application.Run data trigger ExitActions opacity");
                }

                private static void ValidateMultiDataTriggerActionsMetadata(MainWindow window)
                {
                    var actionStyle = RequireType<Style>(
                        window.FindResource("ExternalMultiDataTriggerActionTextStyle"),
                        "external SDK multi data trigger action style");
                    AssertEqual(typeof(TextBlock), actionStyle.TargetType, "external SDK multi data trigger action style target type");
                    AssertEqual(2, actionStyle.Setters.Count, "external SDK multi data trigger action style setter count");
                    AssertEqual(1, actionStyle.Triggers.Count, "external SDK multi data trigger action style trigger count");
                    var multiDataTrigger = RequireType<MultiDataTrigger>(
                        actionStyle.Triggers[0],
                        "external SDK multi data trigger action trigger");
                    AssertEqual(2, multiDataTrigger.Conditions.Count, "external SDK multi data trigger action condition count");
                    var readyBinding = RequireType<Binding>(
                        multiDataTrigger.Conditions[0].Binding,
                        "external SDK multi data trigger action ready binding");
                    AssertEqual("IsExternalMultiDataTriggerActionReady", readyBinding.Path.Path, "external SDK multi data trigger action ready binding path");
                    AssertEqual("True", multiDataTrigger.Conditions[0].Value?.ToString(), "external SDK multi data trigger action ready value");
                    var armedBinding = RequireType<Binding>(
                        multiDataTrigger.Conditions[1].Binding,
                        "external SDK multi data trigger action armed binding");
                    AssertEqual("IsExternalMultiDataTriggerActionArmed", armedBinding.Path.Path, "external SDK multi data trigger action armed binding path");
                    AssertEqual("True", multiDataTrigger.Conditions[1].Value?.ToString(), "external SDK multi data trigger action armed value");
                    AssertEqual(1, multiDataTrigger.EnterActions.Count, "external SDK multi data trigger action EnterActions count");
                    AssertTriggerActionStoryboard(multiDataTrigger.EnterActions[0], 0.24, "external SDK multi data trigger action EnterActions");
                    AssertEqual(1, multiDataTrigger.ExitActions.Count, "external SDK multi data trigger action ExitActions count");
                    AssertTriggerActionStoryboard(multiDataTrigger.ExitActions[0], 0.76, "external SDK multi data trigger action ExitActions");

                    var actionText = RequireType<TextBlock>(
                        window.FindName("ExternalMultiDataTriggerActionText"),
                        "external SDK multi data trigger action text");
                    AssertEqual(actionStyle, actionText.Style, "external SDK multi data trigger action text style");
                    AssertEqual("External multi data trigger action target", actionText.Text, "external SDK multi data trigger action text content");
                    AssertClose(0.76, actionText.Opacity, "external SDK multi data trigger action initial opacity");
                }

                private static void ValidateMultiDataTriggerActionsAfterRun(MainWindow window)
                {
                    DrainDispatcher();

                    var actionText = RequireType<TextBlock>(
                        window.FindName("ExternalMultiDataTriggerActionText"),
                        "external SDK Application.Run multi data trigger action text");
                    AssertClose(0.76, actionText.Opacity, "external SDK Application.Run multi data trigger action initial opacity");

                    window.IsExternalMultiDataTriggerActionReady = true;
                    DrainDispatcher();
                    AssertClose(0.76, actionText.Opacity, "external SDK Application.Run multi data trigger action partial-condition opacity");

                    window.IsExternalMultiDataTriggerActionArmed = true;
                    DrainDispatcher();
                    AssertClose(0.24, actionText.Opacity, "external SDK Application.Run multi data trigger EnterActions opacity");

                    window.IsExternalMultiDataTriggerActionReady = false;
                    DrainDispatcher();
                    AssertClose(0.76, actionText.Opacity, "external SDK Application.Run multi data trigger ExitActions opacity");

                    window.IsExternalMultiDataTriggerActionArmed = false;
                    DrainDispatcher();
                }

                private static void ValidateStylesAndTemplates(MainWindow window)
                {
                    var basedStyle = RequireType<Style>(
                        window.FindResource("ExternalBasedButtonStyle"),
                        "external SDK based button style");
                    var triggeredStyle = RequireType<Style>(
                        window.FindResource("ExternalTriggeredButtonStyle"),
                        "external SDK triggered button style");
                    var eventSetterStyle = RequireType<Style>(
                        window.FindResource("ExternalEventSetterButtonStyle"),
                        "external SDK event setter button style");
                    var dataTriggeredStyle = RequireType<Style>(
                        window.FindResource("ExternalDataTriggeredTextStyle"),
                        "external SDK data trigger text style");
                    var multiDataTriggeredStyle = RequireType<Style>(
                        window.FindResource("ExternalMultiDataTriggeredTextStyle"),
                        "external SDK multi data trigger text style");
                    var buttonTemplate = RequireType<ControlTemplate>(
                        window.FindResource("ExternalButtonTemplate"),
                        "external SDK button control template");

                    AssertEqual(typeof(Button), basedStyle.TargetType, "external SDK based style target type");
                    AssertEqual(typeof(Button), triggeredStyle.TargetType, "external SDK triggered style target type");
                    AssertEqual(typeof(Button), eventSetterStyle.TargetType, "external SDK event setter style target type");
                    AssertEqual(typeof(TextBlock), dataTriggeredStyle.TargetType, "external SDK data trigger style target type");
                    AssertEqual(typeof(TextBlock), multiDataTriggeredStyle.TargetType, "external SDK multi data trigger style target type");
                    AssertEqual(basedStyle, triggeredStyle.BasedOn, "external SDK style BasedOn link");
                    AssertEqual(3, basedStyle.Setters.Count, "external SDK based style setter count");
                    AssertEqual(2, triggeredStyle.Setters.Count, "external SDK triggered style setter count");
                    AssertEqual(3, eventSetterStyle.Setters.Count, "external SDK event setter style setter count");
                    AssertEqual(2, dataTriggeredStyle.Setters.Count, "external SDK data trigger style setter count");
                    AssertEqual(1, dataTriggeredStyle.Triggers.Count, "external SDK data trigger style trigger count");
                    AssertEqual(2, multiDataTriggeredStyle.Setters.Count, "external SDK multi data trigger style setter count");
                    AssertEqual(1, multiDataTriggeredStyle.Triggers.Count, "external SDK multi data trigger style trigger count");
                    AssertEqual(1, triggeredStyle.Triggers.Count, "external SDK triggered style trigger count");
                    var dataTrigger = RequireType<DataTrigger>(
                        dataTriggeredStyle.Triggers[0],
                        "external SDK data trigger");
                    var dataTriggerBinding = RequireType<Binding>(
                        dataTrigger.Binding,
                        "external SDK data trigger binding");
                    AssertEqual("IsExternalDataTriggerActive", dataTriggerBinding.Path.Path, "external SDK data trigger binding path");
                    AssertEqual("True", dataTrigger.Value?.ToString(), "external SDK data trigger value");
                    var multiDataTrigger = RequireType<MultiDataTrigger>(
                        multiDataTriggeredStyle.Triggers[0],
                        "external SDK multi data trigger");
                    AssertEqual(2, multiDataTrigger.Conditions.Count, "external SDK multi data trigger condition count");
                    var firstMultiDataTriggerBinding = RequireType<Binding>(
                        multiDataTrigger.Conditions[0].Binding,
                        "external SDK multi data trigger first binding");
                    AssertEqual("IsExternalDataTriggerActive", firstMultiDataTriggerBinding.Path.Path, "external SDK multi data trigger first binding path");
                    AssertEqual("True", multiDataTrigger.Conditions[0].Value?.ToString(), "external SDK multi data trigger first value");
                    var secondMultiDataTriggerBinding = RequireType<Binding>(
                        multiDataTrigger.Conditions[1].Binding,
                        "external SDK multi data trigger second binding");
                    AssertEqual("IsExternalMultiTriggerReady", secondMultiDataTriggerBinding.Path.Path, "external SDK multi data trigger second binding path");
                    AssertEqual("True", multiDataTrigger.Conditions[1].Value?.ToString(), "external SDK multi data trigger second value");
                    var eventSetter = RequireType<EventSetter>(
                        eventSetterStyle.Setters.OfType<EventSetter>().SingleOrDefault(),
                        "external SDK event setter style click event setter");
                    AssertEqual(ButtonBase.ClickEvent, eventSetter.Event, "external SDK event setter routed event");
                    AssertEqual(typeof(Button), buttonTemplate.TargetType, "external SDK control template target type");
                    AssertEqual(1, buttonTemplate.Triggers.Count, "external SDK control template trigger action count");
                    var templateTrigger = RequireType<Trigger>(
                        buttonTemplate.Triggers[0],
                        "external SDK control template trigger action trigger");
                    AssertEqual(FrameworkElement.TagProperty, templateTrigger.Property, "external SDK control template trigger action property");
                    AssertEqual("template-trigger-active", templateTrigger.Value?.ToString(), "external SDK control template trigger action value");
                    AssertEqual(1, templateTrigger.EnterActions.Count, "external SDK control template trigger action EnterActions count");
                    var templateEnterAnimation = AssertTriggerActionStoryboard(
                        templateTrigger.EnterActions[0],
                        23.0,
                        "external SDK control template trigger action EnterActions",
                        "MinWidth");
                    AssertEqual("ExternalTemplateContent", Storyboard.GetTargetName(templateEnterAnimation), "external SDK control template trigger action EnterActions target name");
                    AssertEqual(1, templateTrigger.ExitActions.Count, "external SDK control template trigger action ExitActions count");
                    var templateExitAnimation = AssertTriggerActionStoryboard(
                        templateTrigger.ExitActions[0],
                        0.0,
                        "external SDK control template trigger action ExitActions",
                        "MinWidth");
                    AssertEqual("ExternalTemplateContent", Storyboard.GetTargetName(templateExitAnimation), "external SDK control template trigger action ExitActions target name");

                    var styledButton = RequireType<Button>(
                        window.FindName("ExternalStyledButton"),
                        "external SDK styled button");
                    var eventSetterButton = RequireType<Button>(
                        window.FindName("ExternalEventSetterButton"),
                        "external SDK event setter styled button");
                    AssertEqual(triggeredStyle, styledButton.Style, "external SDK styled button style");
                    AssertEqual("External styled button", styledButton.Content, "external SDK styled button content setter");
                    AssertEqual("base-style", styledButton.Tag, "external SDK BasedOn style tag setter");
                    AssertBrushColor(styledButton.Background, "#FF254C6A", "external SDK BasedOn style background");
                    AssertBrushColor(styledButton.Foreground, "#FFF4D35E", "external SDK BasedOn style foreground");
                    AssertEqual(eventSetterStyle, eventSetterButton.Style, "external SDK event setter button style");
                    AssertEqual("External event setter button", eventSetterButton.Content, "external SDK event setter content setter");
                    AssertEqual("event-setter-style", eventSetterButton.Tag, "external SDK event setter tag setter");
                    AssertEqual(0, window.ExternalStyleEventButtonClickCount, "external SDK event setter initial click count");
                    eventSetterButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, eventSetterButton));
                    AssertEqual(1, window.ExternalStyleEventButtonClickCount, "external SDK EventSetter click count");
                    AssertEqual("ExternalEventSetterButton", window.LastExternalStyleEventSenderName, "external SDK EventSetter sender name");
                    AssertEqual("Click", window.LastExternalStyleEventRoutedEventName, "external SDK EventSetter routed event");

                    var dataTriggerText = RequireType<TextBlock>(
                        window.FindName("ExternalDataTriggerText"),
                        "external SDK data trigger text");
                    var multiDataTriggerText = RequireType<TextBlock>(
                        window.FindName("ExternalMultiDataTriggerText"),
                        "external SDK multi data trigger text");
                    AssertEqual(dataTriggeredStyle, dataTriggerText.Style, "external SDK data trigger text style");
                    AssertEqual(multiDataTriggeredStyle, multiDataTriggerText.Style, "external SDK multi data trigger text style");
                    AssertEqual("External data trigger inactive", dataTriggerText.Text, "external SDK data trigger initial text");
                    AssertEqual("data-inactive", dataTriggerText.Tag, "external SDK data trigger initial tag");
                    AssertEqual("External multi data trigger inactive", multiDataTriggerText.Text, "external SDK multi data trigger initial text");
                    AssertEqual("multi-data-inactive", multiDataTriggerText.Tag, "external SDK multi data trigger initial tag");

                    window.IsExternalDataTriggerActive = true;
                    DrainDispatcher();
                    AssertEqual("External data trigger active", dataTriggerText.Text, "external SDK data trigger active text");
                    AssertEqual("data-active", dataTriggerText.Tag, "external SDK data trigger active tag");
                    AssertEqual("External multi data trigger inactive", multiDataTriggerText.Text, "external SDK multi data trigger one-condition text");
                    AssertEqual("multi-data-inactive", multiDataTriggerText.Tag, "external SDK multi data trigger one-condition tag");

                    window.IsExternalMultiTriggerReady = true;
                    DrainDispatcher();
                    AssertEqual("External multi data trigger active", multiDataTriggerText.Text, "external SDK multi data trigger active text");
                    AssertEqual("multi-data-active", multiDataTriggerText.Tag, "external SDK multi data trigger active tag");

                    window.IsExternalDataTriggerActive = false;
                    DrainDispatcher();
                    AssertEqual("External data trigger inactive", dataTriggerText.Text, "external SDK data trigger exit text");
                    AssertEqual("data-inactive", dataTriggerText.Tag, "external SDK data trigger exit tag");
                    AssertEqual("External multi data trigger inactive", multiDataTriggerText.Text, "external SDK multi data trigger exit text");
                    AssertEqual("multi-data-inactive", multiDataTriggerText.Tag, "external SDK multi data trigger exit tag");

                    window.IsExternalMultiTriggerReady = false;
                    DrainDispatcher();

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

                    var filteredItems = RequireType<CollectionViewSource>(
                        window.FindResource("ExternalFilteredItems"),
                        "external SDK filtered CollectionViewSource");
                    var filteredList = RequireType<ListBox>(
                        window.FindName("ExternalFilteredItemsList"),
                        "external SDK filtered items list");
                    AssertEqual(filteredItems.View, filteredList.ItemsSource, "external SDK filtered ListBox ItemsSource view");
                    AssertAtLeast(1, window.ExternalItemsFilterCount, "external SDK filtered CollectionViewSource filter event count");
                    var filteredViewItems = filteredItems.View.Cast<object>().ToArray();
                    AssertEqual(1, filteredViewItems.Length, "external SDK filtered CollectionViewSource active item count");
                    AssertEqual(window.ExternalItems[0], filteredViewItems[0], "external SDK filtered CollectionViewSource active item");
                    AssertEqual(1, filteredList.Items.Count, "external SDK filtered ListBox item count");
                    window.ExternalItems[2].IsActive = true;
                    filteredItems.View.Refresh();
                    DrainDispatcher();
                    var refreshedFilteredViewItems = filteredItems.View.Cast<object>().ToArray();
                    AssertEqual(2, refreshedFilteredViewItems.Length, "external SDK filtered CollectionViewSource refreshed item count");
                    AssertEqual(window.ExternalItems[2], refreshedFilteredViewItems[1], "external SDK filtered CollectionViewSource refreshed active item");
                    AssertEqual(2, filteredList.Items.Count, "external SDK filtered ListBox refreshed item count");

                    var liveFilteredItems = RequireType<CollectionViewSource>(
                        window.FindResource("ExternalLiveFilteredItems"),
                        "external SDK live filtered CollectionViewSource");
                    var liveFilteredList = RequireType<ListBox>(
                        window.FindName("ExternalLiveFilteredItemsList"),
                        "external SDK live filtered items list");
                    AssertEqual(true, liveFilteredItems.IsLiveFilteringRequested == true, "external SDK live filtered CollectionViewSource live filtering requested");
                    AssertEqual(1, liveFilteredItems.LiveFilteringProperties.Count, "external SDK live filtered CollectionViewSource live property count");
                    AssertEqual("IsActive", liveFilteredItems.LiveFilteringProperties[0], "external SDK live filtered CollectionViewSource live property");
                    AssertEqual(liveFilteredItems.View, liveFilteredList.ItemsSource, "external SDK live filtered ListBox ItemsSource view");
                    var liveFilteredViewItems = liveFilteredItems.View.Cast<object>().ToArray();
                    AssertEqual(1, liveFilteredViewItems.Length, "external SDK live filtered CollectionViewSource initial item count");
                    AssertEqual(window.ExternalLiveItems[0], liveFilteredViewItems[0], "external SDK live filtered CollectionViewSource initial active item");
                    window.ExternalLiveItems[1].IsActive = true;
                    DrainDispatcher();
                    liveFilteredViewItems = liveFilteredItems.View.Cast<object>().ToArray();
                    AssertEqual(2, liveFilteredViewItems.Length, "external SDK live filtered CollectionViewSource live update item count");
                    AssertEqual(window.ExternalLiveItems[1], liveFilteredViewItems[1], "external SDK live filtered CollectionViewSource live update item");
                    AssertEqual(2, liveFilteredList.Items.Count, "external SDK live filtered ListBox live update count");

                    var liveSortedItems = RequireType<CollectionViewSource>(
                        window.FindResource("ExternalLiveSortedItems"),
                        "external SDK live sorted CollectionViewSource");
                    var liveSortedList = RequireType<ListBox>(
                        window.FindName("ExternalLiveSortedItemsList"),
                        "external SDK live sorted items list");
                    AssertEqual(true, liveSortedItems.IsLiveSortingRequested == true, "external SDK live sorted CollectionViewSource live sorting requested");
                    AssertEqual(1, liveSortedItems.SortDescriptions.Count, "external SDK live sorted CollectionViewSource sort count");
                    AssertEqual("Name", liveSortedItems.SortDescriptions[0].PropertyName, "external SDK live sorted CollectionViewSource sort property");
                    AssertEqual(1, liveSortedItems.LiveSortingProperties.Count, "external SDK live sorted CollectionViewSource live property count");
                    AssertEqual("Name", liveSortedItems.LiveSortingProperties[0], "external SDK live sorted CollectionViewSource live property");
                    AssertEqual(liveSortedItems.View, liveSortedList.ItemsSource, "external SDK live sorted ListBox ItemsSource view");
                    var liveSortedViewItems = liveSortedItems.View.Cast<object>().ToArray();
                    AssertEqual(window.ExternalLiveItems[0], liveSortedViewItems[0], "external SDK live sorted CollectionViewSource initial first item");
                    AssertEqual(window.ExternalLiveItems[2], liveSortedViewItems[2], "external SDK live sorted CollectionViewSource initial third item");
                    window.ExternalLiveItems[2].Name = "Live Aaron";
                    DrainDispatcher();
                    liveSortedViewItems = liveSortedItems.View.Cast<object>().ToArray();
                    AssertEqual(window.ExternalLiveItems[2], liveSortedViewItems[0], "external SDK live sorted CollectionViewSource live resort first item");
                    AssertEqual(window.ExternalLiveItems[2], liveSortedList.Items[0], "external SDK live sorted ListBox live resort first item");

                    var liveGroupedItems = RequireType<CollectionViewSource>(
                        window.FindResource("ExternalLiveGroupedItems"),
                        "external SDK live grouped CollectionViewSource");
                    var liveGroupedList = RequireType<ListBox>(
                        window.FindName("ExternalLiveGroupedItemsList"),
                        "external SDK live grouped items list");
                    AssertEqual(true, liveGroupedItems.IsLiveGroupingRequested == true, "external SDK live grouped CollectionViewSource live grouping requested");
                    AssertEqual(1, liveGroupedItems.GroupDescriptions.Count, "external SDK live grouped CollectionViewSource group count");
                    var liveGroupDescription = RequireType<PropertyGroupDescription>(
                        liveGroupedItems.GroupDescriptions[0],
                        "external SDK live grouped CollectionViewSource group description");
                    AssertEqual("Kind", liveGroupDescription.PropertyName, "external SDK live grouped CollectionViewSource group property");
                    AssertEqual(1, liveGroupedItems.LiveGroupingProperties.Count, "external SDK live grouped CollectionViewSource live property count");
                    AssertEqual("Kind", liveGroupedItems.LiveGroupingProperties[0], "external SDK live grouped CollectionViewSource live property");
                    AssertEqual(liveGroupedItems.View, liveGroupedList.ItemsSource, "external SDK live grouped ListBox ItemsSource view");
                    AssertEqual(1, liveGroupedList.GroupStyle.Count, "external SDK live grouped ListBox GroupStyle count");
                    AssertEqual(groupHeaderTemplate, liveGroupedList.GroupStyle[0].HeaderTemplate, "external SDK live grouped ListBox header template");
                    var liveGroups = liveGroupedItems.View.Groups
                        ?? throw new InvalidOperationException("Expected external SDK live grouped CollectionViewSource groups.");
                    AssertEqual(3, liveGroups.Count, "external SDK live grouped CollectionViewSource initial group count");
                    AssertEqual(1, GetGroupItemCount(liveGroups, "Framework"), "external SDK live grouped CollectionViewSource initial Framework group count");
                    AssertEqual(1, GetGroupItemCount(liveGroups, "Data"), "external SDK live grouped CollectionViewSource initial Data group count");
                    window.ExternalLiveItems[2].Kind = "Framework";
                    DrainDispatcher();
                    liveGroups = liveGroupedItems.View.Groups
                        ?? throw new InvalidOperationException("Expected external SDK live grouped CollectionViewSource groups after change.");
                    AssertEqual(2, liveGroups.Count, "external SDK live grouped CollectionViewSource live regroup count");
                    AssertEqual(2, GetGroupItemCount(liveGroups, "Framework"), "external SDK live grouped CollectionViewSource live regroup Framework count");
                    AssertEqual(false, ContainsGroup(liveGroups, "Data"), "external SDK live grouped CollectionViewSource live regroup removed Data group");

                    var currencyItems = RequireType<CollectionViewSource>(
                        window.FindResource("ExternalCurrencyItems"),
                        "external SDK currency CollectionViewSource");
                    var currencyList = RequireType<ListBox>(
                        window.FindName("ExternalCurrencyItemsList"),
                        "external SDK currency items list");
                    AssertEqual(currencyItems.View, currencyList.ItemsSource, "external SDK currency ListBox ItemsSource view");
                    AssertEqual(true, currencyList.IsSynchronizedWithCurrentItem, "external SDK currency ListBox synchronized current item");
                    currencyList.SelectedIndex = 1;
                    DrainDispatcher();
                    AssertEqual(window.ExternalItems[1], currencyList.SelectedItem, "external SDK currency ListBox selected item");
                    AssertEqual(window.ExternalItems[1], currencyItems.View.CurrentItem, "external SDK currency current item from selection");
                    AssertEqual(true, currencyItems.View.MoveCurrentToPosition(2), "external SDK currency move current result");
                    DrainDispatcher();
                    AssertEqual(window.ExternalItems[2], currencyItems.View.CurrentItem, "external SDK currency current item after move");
                    AssertEqual(window.ExternalItems[2], currencyList.SelectedItem, "external SDK currency ListBox selected item after current move");

                    var compositeList = RequireType<ListBox>(
                        window.FindName("ExternalCompositeItemsList"),
                        "external SDK composite items list");
                    var compositeItems = RequireType<CompositeCollection>(
                        compositeList.ItemsSource,
                        "external SDK CompositeCollection source");
                    AssertEqual(3, compositeItems.Count, "external SDK CompositeCollection source part count");
                    AssertEqual("External composite header", compositeItems[0], "external SDK CompositeCollection static item");
                    var compositeContainer = RequireType<CollectionContainer>(
                        compositeItems[1],
                        "external SDK CompositeCollection container");
                    AssertEqual(ExternalCompositeProvider.Items, compositeContainer.Collection, "external SDK CompositeCollection static source items");
                    var compositeContainerItem = RequireType<ListBoxItem>(
                        compositeItems[2],
                        "external SDK CompositeCollection inline list item");
                    AssertEqual(
                        "External composite item container",
                        compositeContainerItem.Content,
                        "external SDK CompositeCollection inline item content");
                    AssertEqual(4, compositeList.Items.Count, "external SDK CompositeCollection initial flattened item count");
                    AssertEqual("External composite header", compositeList.Items[0], "external SDK CompositeCollection initial static item");
                    AssertEqual(ExternalCompositeProvider.Items[0], compositeList.Items[1], "external SDK CompositeCollection first source item");
                    AssertEqual(ExternalCompositeProvider.Items[1], compositeList.Items[2], "external SDK CompositeCollection second source item");
                    AssertEqual(compositeContainerItem, compositeList.Items[3], "external SDK CompositeCollection initial inline item");
                    ExternalCompositeProvider.Items.Add(new ExternalItem("Composite Gamma", "Data"));
                    DrainDispatcher();
                    AssertEqual(5, compositeList.Items.Count, "external SDK CompositeCollection collection-change flattened item count");
                    AssertEqual(ExternalCompositeProvider.Items[2], compositeList.Items[3], "external SDK CompositeCollection collection-change appended source item");
                    AssertEqual(compositeContainerItem, compositeList.Items[4], "external SDK CompositeCollection collection-change inline item");

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
                    var requeryCommandButton = RequireType<Button>(
                        window.FindName("ExternalRequeryCommandButton"),
                        "external SDK requery command button");
                    var validationTextBox = RequireType<TextBox>(
                        window.FindName("ExternalValidationTextBox"),
                        "external SDK access-key target text box");
                    var accessLabel = RequireType<Label>(
                        window.FindName("ExternalAccessLabel"),
                        "external SDK access label");
                    var standaloneAccessText = RequireType<AccessText>(
                        window.FindName("ExternalStandaloneAccessText"),
                        "external SDK standalone access text");
                    var keyboardNavigationPanel = RequireType<StackPanel>(
                        window.FindName("ExternalKeyboardNavigationPanel"),
                        "external SDK keyboard navigation panel");
                    var firstKeyboardNavigationButton = RequireType<Button>(
                        window.FindName("ExternalKeyboardNavigationFirstButton"),
                        "external SDK first keyboard navigation button");
                    var secondKeyboardNavigationButton = RequireType<Button>(
                        window.FindName("ExternalKeyboardNavigationSecondButton"),
                        "external SDK second keyboard navigation button");
                    AssertEqual(commandButton, FocusManager.GetFocusedElement(focusPanel), "external SDK focus manager focused element");
                    AssertEqual(true, FocusManager.GetIsFocusScope(focusPanel), "external SDK focus manager scope flag");
                    AssertEqual(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(focusPanel), "external SDK tab navigation mode");
                    AssertEqual(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetControlTabNavigation(focusPanel), "external SDK control-tab navigation mode");
                    AssertEqual(KeyboardNavigationMode.Contained, KeyboardNavigation.GetDirectionalNavigation(focusPanel), "external SDK directional navigation mode");
                    AssertEqual(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(keyboardNavigationPanel), "external SDK nested keyboard navigation mode");
                    AssertEqual("External navigation first", firstKeyboardNavigationButton.Content, "external SDK first keyboard navigation button content");
                    AssertEqual("External navigation second", secondKeyboardNavigationButton.Content, "external SDK second keyboard navigation button content");
                    AssertEqual(validationTextBox, accessLabel.Target, "external SDK label access-key target");
                    AssertEqual("_External access target", accessLabel.Content, "external SDK label access-key content");
                    AssertEqual("_External standalone access", standaloneAccessText.Text, "external SDK standalone access text");
                    AssertEqual("ExternalValidationTextBoxAutomation", AutomationProperties.GetAutomationId(validationTextBox), "external SDK automation id");
                    AssertEqual("External validation input", AutomationProperties.GetName(validationTextBox), "external SDK automation name");
                    AssertEqual("External SDK validation text", AutomationProperties.GetHelpText(validationTextBox), "external SDK automation help text");
                    AssertEqual(accessLabel, AutomationProperties.GetLabeledBy(validationTextBox), "external SDK automation labeled-by element");
                    var labelPeer = RequireType<LabelAutomationPeer>(
                        UIElementAutomationPeer.CreatePeerForElement(accessLabel),
                        "external SDK label automation peer");
                    var validationPeer = RequireType<TextBoxAutomationPeer>(
                        UIElementAutomationPeer.CreatePeerForElement(validationTextBox),
                        "external SDK text box automation peer");
                    AssertEqual("ExternalValidationTextBoxAutomation", validationPeer.GetAutomationId(), "external SDK automation peer id");
                    AssertEqual("External validation input", validationPeer.GetName(), "external SDK automation peer name");
                    AssertEqual("External SDK validation text", validationPeer.GetHelpText(), "external SDK automation peer help text");
                    AssertEqual(labelPeer, validationPeer.GetLabeledBy(), "external SDK automation peer labeled-by peer");
                    AssertEqual(accessLabel, labelPeer.Owner, "external SDK label automation peer owner");
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

                    AssertEqual(window.ExternalRequeryCommand, requeryCommandButton.Command, "external SDK requery command binding");
                    AssertEqual("ExternalRequeryParameter", requeryCommandButton.CommandParameter, "external SDK requery command parameter");
                    window.ExternalRequeryCommand.CanExecuteValue = false;
                    CommandManager.InvalidateRequerySuggested();
                    DrainDispatcher();
                    AssertEqual(false, requeryCommandButton.IsEnabled, "external SDK requery command disabled state");

                    int requeryProbeBefore = window.ExternalRequeryCommand.CanExecuteProbeCount;
                    window.ExternalRequeryCommand.CanExecuteValue = true;
                    CommandManager.InvalidateRequerySuggested();
                    DrainDispatcher();
                    AssertEqual(true, requeryCommandButton.IsEnabled, "external SDK requery command enabled state");
                    AssertAtLeast(
                        requeryProbeBefore + 1,
                        window.ExternalRequeryCommand.CanExecuteProbeCount,
                        "external SDK requery command can-execute probe count");

                    int requeryExecuteBefore = window.ExternalRequeryCommand.ExecuteCount;
                    RequireType<ICommand>(
                        requeryCommandButton.Command,
                        "external SDK requery command interface")
                        .Execute(requeryCommandButton.CommandParameter);
                    AssertEqual(requeryExecuteBefore + 1, window.ExternalRequeryCommand.ExecuteCount, "external SDK requery command execute count");
                    AssertEqual("ExternalRequeryParameter", window.ExternalRequeryCommand.LastParameter, "external SDK requery command last parameter");
                }

                private static void ValidateAccessKeyRoutingAfterRun(MainWindow window)
                {
                    var validationTextBox = RequireType<TextBox>(
                        window.FindName("ExternalValidationTextBox"),
                        "external SDK access-key routing target text box");
                    var presentationSource = PresentationSource.FromVisual(window)
                        ?? throw new InvalidOperationException("Expected external SDK window to have a presentation source.");

                    AssertEqual(true, AccessKeyManager.IsKeyRegistered(presentationSource, "E"), "external SDK access-key manager registered label key");
                    Keyboard.ClearFocus();
                    AssertEqual(false, ReferenceEquals(validationTextBox, Keyboard.FocusedElement), "external SDK access-key manager cleared focus");
                    AssertEqual(false, AccessKeyManager.ProcessKey(presentationSource, "E", false), "external SDK access-key manager process last key");
                    AssertEqual(validationTextBox, Keyboard.FocusedElement, "external SDK access-key manager focused label target");
                    Keyboard.ClearFocus();
                }

                private static void ValidateKeyboardNavigationAfterRun(MainWindow window)
                {
                    var keyboardNavigationPanel = RequireType<StackPanel>(
                        window.FindName("ExternalKeyboardNavigationPanel"),
                        "external SDK keyboard navigation runtime panel");
                    var firstButton = RequireType<Button>(
                        window.FindName("ExternalKeyboardNavigationFirstButton"),
                        "external SDK first keyboard navigation runtime button");
                    var secondButton = RequireType<Button>(
                        window.FindName("ExternalKeyboardNavigationSecondButton"),
                        "external SDK second keyboard navigation runtime button");

                    keyboardNavigationPanel.UpdateLayout();
                    AssertEqual(firstButton, Keyboard.Focus(firstButton), "external SDK KeyboardNavigation initial focus");
                    AssertEqual(firstButton, Keyboard.FocusedElement, "external SDK KeyboardNavigation focused first button");
                    AssertEqual(true, firstButton.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "external SDK KeyboardNavigation next move result");
                    AssertEqual(secondButton, Keyboard.FocusedElement, "external SDK KeyboardNavigation focused second button");
                    AssertEqual(true, secondButton.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "external SDK KeyboardNavigation cycle next move result");
                    AssertEqual(firstButton, Keyboard.FocusedElement, "external SDK KeyboardNavigation cycled first button");
                    AssertEqual(true, firstButton.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)), "external SDK KeyboardNavigation previous move result");
                    AssertEqual(secondButton, Keyboard.FocusedElement, "external SDK KeyboardNavigation cycled previous button");
                    Keyboard.ClearFocus();
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

                private static int GetGroupItemCount(System.Collections.IEnumerable groups, string name)
                {
                    foreach (object group in groups)
                    {
                        if (group is CollectionViewGroup collectionViewGroup
                            && string.Equals(collectionViewGroup.Name?.ToString(), name, StringComparison.Ordinal))
                        {
                            return collectionViewGroup.ItemCount;
                        }
                    }

                    return 0;
                }

                private static void AssertBrushColor(Brush brush, string expected, string description)
                {
                    var solidColorBrush = RequireType<SolidColorBrush>(brush, description);
                    AssertEqual(expected, solidColorBrush.Color.ToString(), description);
                }

                private static void AssertTemplateText(DataTemplate template, object dataContext, string expectedText, string description)
                {
                    var text = RequireType<TextBlock>(
                        template.LoadContent(),
                        description + " root");
                    text.DataContext = dataContext;
                    DrainDispatcher();
                    AssertEqual(expectedText, text.Text, description);
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
        AssertContains(appProject, "<Resource Include=\"Assets/ExternalResource.txt\" />", "external app WPF resource item");
        AssertContains(appProject, "<Resource Include=\"Assets/ExternalImage.png\" />", "external app WPF image resource item");
        AssertContains(libraryProject, $"<Project Sdk=\"ProGPU.Wpf.Sdk/{SdkVersion}\">", "external library SDK");
        AssertContains(libraryProject, "<UseWPF>true</UseWPF>", "external library WPF property");
        RequireFile(Path.Combine(workRoot, LibraryAssemblyName, "Properties", "AssemblyInfo.cs"), "external SDK library ThemeInfo source");
        RequireFile(Path.Combine(workRoot, LibraryAssemblyName, "Themes", "Generic.xaml"), "external SDK library Generic.xaml source");
        RequireFile(Path.Combine(workRoot, AppAssemblyName, "Assets", "ExternalResource.txt"), "external SDK app WPF resource source");
        RequireFile(Path.Combine(workRoot, AppAssemblyName, "Assets", "ExternalImage.png"), "external SDK app WPF image resource source");
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
        AssertContains(depsJson, "StbImageSharp", "external SDK StbImageSharp package dependency");
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
            Assembly presentationCore = loadContext.LoadFromAssemblyPath(Path.Combine(outputRoot, "PresentationCore.dll"));

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

            Type portablePresentationSourceType = GetRequiredType(presentationCore, "System.Windows.PortablePresentationSource");
            MethodInfo setPortableClientSize = portablePresentationSourceType.GetMethod(
                "SetClientSize",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(double), typeof(double)],
                modifiers: null)
                ?? throw new MissingMethodException(portablePresentationSourceType.FullName, "SetClientSize");
            AssertEqual(typeof(void), setPortableClientSize.ReturnType, "external SDK portable presentation source client-size return type");

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
            AssertRetainedWpfLayerUsesLogicalBoundsAndDpiScale(proGpuWpf, proGpuScene, "external SDK");
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static void AssertRetainedWpfLayerUsesLogicalBoundsAndDpiScale(
        Assembly proGpuWpf,
        Assembly proGpuScene,
        string descriptionPrefix)
    {
        Type drawingFrameType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfDrawingFrame");
        Type containerVisualType = GetRequiredType(proGpuScene, "ProGPU.Scene.ContainerVisual");
        Type drawingVisualType = GetRequiredType(proGpuScene, "ProGPU.Scene.DrawingVisual");
        object sceneRoot = Create(containerVisualType);
        object retainedRoot = Create(containerVisualType);
        object flatRoot = Create(drawingVisualType);
        object frame = Create(
            drawingFrameType,
            sceneRoot,
            retainedRoot,
            flatRoot,
            840u,
            1680u,
            null,
            null,
            true,
            null,
            420u,
            840u,
            2.0,
            2.0);

        AssertEqual(420u, GetProperty(frame, "LogicalWidth"), $"{descriptionPrefix} ProGPU WPF drawing frame logical width");
        AssertEqual(840u, GetProperty(frame, "LogicalHeight"), $"{descriptionPrefix} ProGPU WPF drawing frame logical height");
        AssertEqual(840u, GetProperty(frame, "PixelWidth"), $"{descriptionPrefix} ProGPU WPF drawing frame pixel width");
        AssertEqual(1680u, GetProperty(frame, "PixelHeight"), $"{descriptionPrefix} ProGPU WPF drawing frame pixel height");
        AssertEqual(2.0, GetProperty(frame, "DpiScaleX"), $"{descriptionPrefix} ProGPU WPF drawing frame DPI scale X");
        AssertEqual(2.0, GetProperty(frame, "DpiScaleY"), $"{descriptionPrefix} ProGPU WPF drawing frame DPI scale Y");
        AssertEqual(new Vector2(420f, 840f), GetProperty(sceneRoot, "Size"), $"{descriptionPrefix} ProGPU scene root logical size");
        AssertEqual(new Vector2(420f, 840f), GetProperty(retainedRoot, "Size"), $"{descriptionPrefix} ProGPU retained WPF layer logical size");
        AssertEqual(new Vector2(420f, 840f), GetProperty(flatRoot, "Size"), $"{descriptionPrefix} ProGPU flat WPF layer logical size");
        AssertEqual(new Vector3(2f, 2f, 1f), GetProperty(retainedRoot, "Scale"), $"{descriptionPrefix} ProGPU retained WPF layer scale");
        AssertEqual(Vector2.Zero, GetProperty(retainedRoot, "RenderTransformOrigin"), $"{descriptionPrefix} ProGPU retained WPF layer transform origin");
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

    private static AssemblyName ReadPackageAssemblyName(ZipArchiveEntry entry, string description)
    {
        string tempPath = Path.Combine(
            Path.GetTempPath(),
            "progpu-wpf-sdk-package-" + Guid.NewGuid().ToString("N") + ".dll");

        try
        {
            using (Stream source = entry.Open())
            using (FileStream destination = File.Create(tempPath))
            {
                source.CopyTo(destination);
            }

            return AssemblyName.GetAssemblyName(tempPath);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException)
        {
            throw new InvalidOperationException($"Could not read {description} identity from package entry '{entry.FullName}'.", ex);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string GetPublicKeyToken(AssemblyName identity)
    {
        byte[]? publicKeyToken = identity.GetPublicKeyToken();
        return publicKeyToken is null || publicKeyToken.Length == 0
            ? string.Empty
            : string.Concat(publicKeyToken.Select(value => value.ToString("x2")));
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

    private static object Create(Type type, params object?[] args)
    {
        return Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args,
            culture: null)
            ?? throw new InvalidOperationException($"Could not create '{type.FullName}'.");
    }

    private static object GetProperty(object instance, string propertyName)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        return property.GetValue(instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' returned null.");
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

    private readonly record struct PackageAssemblyExpectation(
        string PackageId,
        string AssemblySimpleName,
        string TargetFramework,
        string PublicKeyTokenGroup);
}
