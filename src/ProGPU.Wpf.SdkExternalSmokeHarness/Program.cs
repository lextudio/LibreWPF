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
                StartupUri="MainWindow.xaml" />
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
            Path.Combine(appRoot, "MainWindow.xaml"),
            """
            <Window
                x:Class="ExternalSdkApp.MainWindow"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:library="clr-namespace:ExternalSdkLibrary;assembly=ExternalSdkLibrary"
                Title="External SDK App"
                Width="320"
                Height="200">
                <StackPanel>
                    <TextBlock
                        x:Name="TitleText"
                        Text="External SDK app" />
                    <library:ExternalPanel
                        x:Name="ExternalPanel"
                        Caption="External SDK library panel" />
                </StackPanel>
            </Window>
            """);

        WriteFile(
            Path.Combine(appRoot, "MainWindow.xaml.cs"),
            """
            using System.Windows;

            namespace ExternalSdkApp;

            public partial class MainWindow : Window
            {
                public MainWindow()
                {
                    InitializeComponent();
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
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.Environment["DOTNET_ROLL_FORWARD"] = "Major";
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
