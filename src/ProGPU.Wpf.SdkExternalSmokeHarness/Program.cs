using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

internal static class Program
{
    private const string OriginalWpfSdk = "Microsoft.NET.Sdk";
    private const string OriginalWindowsDesktopWpfSdk = "Microsoft.NET.Sdk.WindowsDesktop";
    private const string SdkVersion = "0.1.0-preview.35";
    private const string ProGpuPackageVersion = "0.1.0-preview.37";
    private const string PrepackagedProGpuDirectoryEnvironmentVariable = "PROGPU_WPF_PREPACKAGED_PROGPU_DIR";
    private const string ExternalAppTargetFramework = "net10.0-windows";
    private const string AppAssemblyName = "ExternalSdkApp";
    private const string AppOutputAssemblyName = "ExternalSdkShell";
    private const string LibraryAssemblyName = "ExternalSdkLibrary";
    private const string LibraryOutputAssemblyName = "ExternalSdkControls";
    private const string CentralPackageManagementAssemblyName = "ExternalCpmSdkApp";
    private const string CentralPackageManagementOutputAssemblyName = "ExternalCpmSdkShell";
    private const string LocalizationAssemblyName = "ExternalLocalizationApp";
    private const string DefaultItemsAssemblyName = "ExternalSdkDefaultItemsApp";
    private const string DefaultItemsLibraryAssemblyName = "ExternalSdkDefaultItemsLibrary";

    private static readonly string[] s_requiredWpfRuntimeAssemblies =
    [
        "WindowsBase",
        "System.Xaml",
        "PresentationCore",
        "PresentationFramework",
        "PresentationUI",
        "ReachFramework",
        "System.Printing",
        "UIAutomationTypes",
        "UIAutomationProvider",
        "System.Windows.Input.Manipulations",
        "System.Windows.Primitives",
        "PresentationFramework.Aero",
        "PresentationFramework.Aero2",
        "PresentationFramework.AeroLite",
        "PresentationFramework.Classic",
        "PresentationFramework.Fluent",
        "PresentationFramework.Luna",
        "PresentationFramework.Royale",
        "System.Windows.Controls.Ribbon"
    ];

    private static readonly string[] s_requiredProGpuRuntimeAssemblies =
    [
        "ProGPU.Wpf",
        "ProGPU.Wpf.Interop",
        "ProGPU.Backend",
        "ProGPU.DirectX",
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
        "OpenFontSharp",
        "StbImageSharp"
    ];

    private static readonly PackageAssemblyExpectation[] s_packageAssemblyExpectations =
    [
        new("LibreWPF.Transport", "WindowsBase", "net10.0", "WPF"),
        new("LibreWPF.Transport", "System.Xaml", "net10.0", "Ecma"),
        new("LibreWPF.Transport", "PresentationCore", "net10.0", "WPF"),
        new("LibreWPF.Transport", "PresentationFramework", "net10.0", "WPF"),
        new("LibreWPF.Transport", "PresentationUI", "net10.0", "WPF"),
        new("LibreWPF.Transport", "ReachFramework", "net10.0", "WPF"),
        new("LibreWPF.Transport", "System.Printing", "net10.0", "WPF"),
        new("LibreWPF.Transport", "UIAutomationTypes", "net10.0", "WPF"),
        new("LibreWPF.Transport", "UIAutomationProvider", "net10.0", "WPF"),
        new("LibreWPF.Transport", "System.Windows.Input.Manipulations", "net10.0", "Ecma"),
        new("LibreWPF.Transport", "System.Windows.Primitives", "net10.0", "WPF"),
        new("LibreWPF.Transport", "PresentationFramework.Aero", "net10.0", "WPF"),
        new("LibreWPF.Transport", "PresentationFramework.Aero2", "net10.0", "WPF"),
        new("LibreWPF.Transport", "PresentationFramework.AeroLite", "net10.0", "WPF"),
        new("LibreWPF.Transport", "PresentationFramework.Classic", "net10.0", "WPF"),
        new("LibreWPF.Transport", "PresentationFramework.Fluent", "net10.0", "WPF"),
        new("LibreWPF.Transport", "PresentationFramework.Luna", "net10.0", "WPF"),
        new("LibreWPF.Transport", "PresentationFramework.Royale", "net10.0", "WPF"),
        new("LibreWPF.Transport", "System.Windows.Controls.Ribbon", "net10.0", "Ecma"),
        new("LibreWPF.ProGPU", "ProGPU.Wpf", "net10.0", "ProGPU"),
        new("LibreWPF.Interop", "ProGPU.Wpf.Interop", "net10.0", "ProGPU"),
        new("ProGPU.Backend", "ProGPU.Backend", "net10.0", "ProGPU"),
        new("ProGPU.DirectX", "ProGPU.DirectX", "net10.0", "ProGPU"),
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
            string? prepackagedProGpuDirectory =
                Environment.GetEnvironmentVariable(PrepackagedProGpuDirectoryEnvironmentVariable);
            RequireDirectory(packageFeed, "local package feed");
            ValidateSdkPackageLayout(packageFeed);
            ValidateLocalProGpuPackageProvenance(
                repoRoot,
                packageFeed,
                prepackagedProGpuDirectory);
            ValidateLocalWpfPackageMatchesAvailableRepositoryBuilds(repoRoot, packageFeed);

            string workRoot = Path.Combine(Path.GetTempPath(), "ProGPU.Wpf.SdkExternalSmoke");
            string appProjectPath = PrepareExternalSdkApp(workRoot, packageFeed);
            string centralPackageManagementProjectPath = PrepareExternalCentralPackageManagementApp(
                Path.Combine(Path.GetTempPath(), "ProGPU.Wpf.SdkExternalCpmSmoke"),
                packageFeed);
            string localizationProjectPath = PrepareExternalLocalizationProject(workRoot);
            string defaultItemsProjectPath = PrepareExternalDefaultItemsApp(workRoot);
            string dotnetPath = ResolveDotNetHost(repoRoot);

            RunProcess(
                dotnetPath,
                repoRoot,
                "msbuild",
                appProjectPath,
                "-getProperty:RuntimeIdentifier",
                "-p:DesignTimeBuild=true",
                "-p:BuildingInsideVisualStudio=true",
                "-p:SkipCompilerExecution=true");
            RunProcess(dotnetPath, repoRoot, "build", appProjectPath, "-v:minimal");
            RunProcess(dotnetPath, repoRoot, "restore", centralPackageManagementProjectPath, "-v:minimal");
            RunProcess(dotnetPath, repoRoot, "build", centralPackageManagementProjectPath, "-v:minimal", "--no-restore");
            RunProcess(dotnetPath, repoRoot, "build", localizationProjectPath, "-v:minimal");
            RunProcess(dotnetPath, repoRoot, "build", defaultItemsProjectPath, "-v:minimal");

            ValidateExternalProjectShape(workRoot);
            ValidateExternalCentralPackageManagementProjectShape(centralPackageManagementProjectPath);
            ValidateExternalDefaultItemsProjectShape(workRoot);
            ValidateExternalLocalizationDirectives(workRoot);
            string outputRoot = Path.Combine(workRoot, AppAssemblyName, "bin", "Debug", ExternalAppTargetFramework);
            ValidateExternalOutput(outputRoot, packageFeed);
            string defaultItemsOutputRoot = Path.Combine(workRoot, DefaultItemsAssemblyName, "bin", "Debug", ExternalAppTargetFramework);
            ValidateExternalDefaultItemsOutput(defaultItemsOutputRoot);
            RunProcess(
                dotnetPath,
                outputRoot,
                new Dictionary<string, string>
                {
                    ["PROGPU_WPF_EXTERNAL_VALIDATE"] = "1"
                },
                Path.Combine(outputRoot, AppOutputAssemblyName + ".dll"));
            string applicationRunOutput = RunProcess(
                dotnetPath,
                outputRoot,
                new Dictionary<string, string>
                {
                    ["PROGPU_WPF_EXTERNAL_RUN_VALIDATE"] = "1"
                },
                Path.Combine(outputRoot, AppOutputAssemblyName + ".dll"),
                "external-startup-alpha",
                "external startup beta");
            AssertContains(
                applicationRunOutput,
                "External SDK Application.Run validation succeeded.",
                "external SDK Application.Run validation output");
            string applicationAppHostOutput = RunProcess(
                Path.Combine(outputRoot, GetAppHostFileName(AppOutputAssemblyName)),
                outputRoot,
                new Dictionary<string, string>
                {
                    ["PROGPU_WPF_EXTERNAL_RUN_VALIDATE"] = "1"
                },
                "external-startup-alpha",
                "external startup beta");
            AssertContains(
                applicationAppHostOutput,
                "External SDK Application.Run validation succeeded.",
                "external SDK apphost Application.Run validation output");
            string applicationLiveGeometryOutput = RunAppHostLiveValidationProbe(
                Path.Combine(outputRoot, GetAppHostFileName(AppOutputAssemblyName)),
                outputRoot,
                "PROGPU_WPF_EXTERNAL_LIVE_VALIDATE",
                "External SDK apphost live input validation succeeded:",
                "External SDK apphost live input",
                "external-startup-alpha",
                "external startup beta");
            AssertContains(
                applicationLiveGeometryOutput,
                "External SDK apphost live input validation succeeded:",
                "external SDK apphost live input validation output");
            string defaultItemsRunOutput = RunProcess(
                dotnetPath,
                defaultItemsOutputRoot,
                new Dictionary<string, string>
                {
                    ["PROGPU_WPF_EXTERNAL_DEFAULT_RUN_VALIDATE"] = "1"
                },
                Path.Combine(defaultItemsOutputRoot, DefaultItemsAssemblyName + ".dll"));
            AssertContains(
                defaultItemsRunOutput,
                "External SDK default-item Application.Run validation succeeded.",
                "external SDK default-item Application.Run validation output");
            string defaultItemsAppHostOutput = RunProcess(
                Path.Combine(defaultItemsOutputRoot, GetAppHostFileName(DefaultItemsAssemblyName)),
                defaultItemsOutputRoot,
                new Dictionary<string, string>
                {
                    ["PROGPU_WPF_EXTERNAL_DEFAULT_RUN_VALIDATE"] = "1"
                });
            AssertContains(
                defaultItemsAppHostOutput,
                "External SDK default-item Application.Run validation succeeded.",
                "external SDK default-item apphost validation output");
            string defaultItemsLiveGeometryOutput = RunAppHostLiveValidationProbe(
                Path.Combine(defaultItemsOutputRoot, GetAppHostFileName(DefaultItemsAssemblyName)),
                defaultItemsOutputRoot,
                "PROGPU_WPF_EXTERNAL_DEFAULT_LIVE_GEOMETRY_VALIDATE",
                "External SDK default-item apphost live geometry validation succeeded:",
                "External SDK default-item apphost live geometry");
            AssertContains(
                defaultItemsLiveGeometryOutput,
                "External SDK default-item apphost live geometry validation succeeded:",
                "external SDK default-item apphost live geometry validation output");

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
        string packagePath = Path.Combine(packageFeed, $"LibreWPF.Sdk.{SdkVersion}.nupkg");
        RequireFile(packagePath, "ProGPU WPF SDK package");

        using ZipArchive package = ZipFile.OpenRead(packagePath);

        string nuspec = ReadPackageEntry(package, "LibreWPF.Sdk.nuspec", "SDK nuspec");
        string sdkProps = ReadPackageEntry(package, "Sdk/Sdk.props", "SDK root props import");
        string sdkTargets = ReadPackageEntry(package, "Sdk/Sdk.targets", "SDK root targets import");
        string portableProps = ReadPackageEntry(package, "targets/ProGPU.Wpf.Sdk.props", "portable SDK props");
        string portableTargets = ReadPackageEntry(package, "targets/ProGPU.Wpf.Sdk.targets", "portable SDK targets");
        string portableBootstrap = ReadPackageEntry(package, "targets/ProGPU.Wpf.Sdk.PortableBootstrap.cs", "portable SDK bootstrap");
        _ = ReadPackageEntry(package, "README.md", "SDK readme");

        AssertContains(nuspec, "<id>LibreWPF.Sdk</id>", "SDK nuspec package id");
        AssertContains(nuspec, $"<version>{SdkVersion}</version>", "SDK nuspec version");
        AssertContains(nuspec, "<packageType name=\"MSBuildSdk\" />", "SDK nuspec package type");
        AssertContains(nuspec, "<dependencies>", "SDK nuspec dependency group");

        AssertContains(sdkProps, "<ProGpuWpfSdkVersion Condition=\"'$(ProGpuWpfSdkVersion)' == ''\">0.1.0-preview.35</ProGpuWpfSdkVersion>", "SDK root version default");
        AssertContains(sdkProps, "<ProGpuWpfRuntimeFrameworkVersion Condition=\"'$(ProGpuWpfRuntimeFrameworkVersion)' == ''\"></ProGpuWpfRuntimeFrameworkVersion>", "SDK runtime version override hook");
        AssertContains(sdkProps, "<RuntimeFrameworkVersion Condition=\"'$(ProGpuWpfUsePortableFrameworkReferences)' == 'true' And '$(RuntimeFrameworkVersion)' == '' And '$(ProGpuWpfRuntimeFrameworkVersion)' != ''\">$(ProGpuWpfRuntimeFrameworkVersion)</RuntimeFrameworkVersion>", "SDK runtime version opt-in");
        AssertDoesNotContain(sdkProps, "11.0.0-preview.4.26210.111", "SDK root runtime version default");
        AssertContains(sdkProps, "<ProGpuWpfOpenFontSharpVersion Condition=\"'$(ProGpuWpfOpenFontSharpVersion)' == ''\">1.0.0</ProGpuWpfOpenFontSharpVersion>", "SDK OpenFontSharp version default");
        AssertContains(sdkProps, "<ProGpuWpfStbImageSharpVersion Condition=\"'$(ProGpuWpfStbImageSharpVersion)' == ''\">2.30.15</ProGpuWpfStbImageSharpVersion>", "SDK StbImageSharp version default");
        const string windowsDesktopSdkPropsImport = "<Import Sdk=\"Microsoft.NET.Sdk.WindowsDesktop\" Project=\"Sdk.props\" />";
        const string portableRuntimeIdentifier = "<RuntimeIdentifier Condition=\"'$(ProGpuWpfUseCurrentRuntimeIdentifier)' == 'true' And '$(RuntimeIdentifier)' == '' And '$(NETCoreSdkRuntimeIdentifier)' != ''\">$(NETCoreSdkRuntimeIdentifier)</RuntimeIdentifier>";
        AssertContains(sdkProps, windowsDesktopSdkPropsImport, "SDK root WindowsDesktop props import");
        AssertContains(sdkProps, portableRuntimeIdentifier, "SDK portable runtime identifier");
        AssertDoesNotContain(sdkProps, "System.Runtime.InteropServices.RuntimeInformation", "SDK unsupported runtime property function");
        if (sdkProps.IndexOf(windowsDesktopSdkPropsImport, StringComparison.Ordinal)
            >= sdkProps.IndexOf(portableRuntimeIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SDK portable runtime identifier must follow the WindowsDesktop SDK props import.");
        }
        AssertContains(sdkProps, "ProGPU.Wpf.Sdk.props", "SDK root portable props import");
        AssertContains(sdkTargets, "<Import Sdk=\"Microsoft.NET.Sdk.WindowsDesktop\" Project=\"Sdk.targets\" />", "SDK root WindowsDesktop targets import");
        AssertContains(sdkTargets, "ProGPU.Wpf.Sdk.targets", "SDK root portable targets import");

        AssertContains(portableProps, "<InternalMarkupCompilation Condition=\"'$(ProGpuWpfUseWpfMarkup)' == 'true' And '$(InternalMarkupCompilation)' == ''\">true</InternalMarkupCompilation>", "SDK markup compiler default");
        AssertContains(portableProps, "<AlwaysCompileMarkupFilesInSeparateDomain Condition=\"'$(ProGpuWpfUseWpfMarkup)' == 'true' And '$(AlwaysCompileMarkupFilesInSeparateDomain)' == ''\">false</AlwaysCompileMarkupFilesInSeparateDomain>", "SDK markup compiler appdomain default");
        AssertContains(portableProps, "<EnableDefaultResourceItems Condition=\"'$(EnableDefaultResourceItems)' == ''\">true</EnableDefaultResourceItems>", "SDK default resource item switch");
        AssertContains(portableProps, "<ApplicationDefinition Include=\"App.xaml\"", "SDK default app XAML item");
        AssertContains(portableProps, "<Page Include=\"**/*.xaml\"", "SDK default page XAML item");
        AssertContains(portableTargets, "<PackageReference Include=\"Silk.NET.WebGPU.Native.WGPU\" Version=\"$(ProGpuWpfSilkNetVersion)\" />", "SDK native WebGPU package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"Silk.NET.WebGPU.Native.WGPU\" VersionOverride=\"$(ProGpuWpfSilkNetVersion)\" />", "SDK CPM native WebGPU package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"System.IO.Packaging\" Version=\"$(ProGpuWpfSystemIOPackagingVersion)\" />", "SDK WPF support package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"System.IO.Packaging\" VersionOverride=\"$(ProGpuWpfSystemIOPackagingVersion)\" />", "SDK CPM WPF support package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"OpenFontSharp\" Version=\"$(ProGpuWpfOpenFontSharpVersion)\" />", "SDK OpenFontSharp package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"OpenFontSharp\" VersionOverride=\"$(ProGpuWpfOpenFontSharpVersion)\" />", "SDK CPM OpenFontSharp package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"StbImageSharp\" Version=\"$(ProGpuWpfStbImageSharpVersion)\" />", "SDK StbImageSharp package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"StbImageSharp\" VersionOverride=\"$(ProGpuWpfStbImageSharpVersion)\" />", "SDK CPM StbImageSharp package reference");

        AssertContains(portableTargets, "<FrameworkReference Remove=\"Microsoft.WindowsDesktop.App.WPF\" />", "SDK WindowsDesktop framework suppression");
        AssertContains(portableTargets, "_ProGpuWpfSdkRemoveNetCoreSystemDrawingFacade", "SDK System.Drawing facade suppression target");
        AssertContains(portableTargets, "DependsOnTargets=\"ResolveTargetingPackAssets;ResolveLockFileReferences\"", "SDK System.Drawing facade suppression timing");
        AssertContains(portableTargets, "'%(ReferencePath.NuGetPackageId)' == 'ProGPU.System.Drawing.Common'", "SDK ProGPU System.Drawing reference detection");
        AssertContains(portableTargets, "<ReferencePathWithRefAssemblies", "SDK compiler System.Drawing facade suppression");
        AssertContains(portableTargets, "microsoft.netcore.app.ref", "SDK targeting-pack System.Drawing facade filter");
        AssertContains(portableTargets, "<_ProGpuWpfDefaultResourceItem Include=\"**/*.bmp;**/*.cur;**/*.gif;**/*.ico;**/*.jpg;**/*.jpeg;**/*.png;**/*.tif;**/*.tiff;**/*.wdp;**/*.webp\"", "SDK default image resource item");
        AssertContains(portableTargets, "<Resource Include=\"@(_ProGpuWpfDefaultResourceItem)\" />", "SDK default image resource include");
        AssertContains(portableTargets, "<None Remove=\"@(_ProGpuWpfDefaultResourceItem)\" />", "SDK default image resource None removal");
        AssertContains(portableTargets, "<PackageReference Include=\"$(ProGpuWpfManagedPackageId)\" Version=\"$(ProGpuWpfManagedPackageVersion)\" GeneratePathProperty=\"true\" />", "SDK managed WPF transport package path reference");
        AssertContains(portableTargets, "<PackageReference Include=\"$(ProGpuWpfManagedPackageId)\" VersionOverride=\"$(ProGpuWpfManagedPackageVersion)\" GeneratePathProperty=\"true\" />", "SDK CPM managed WPF transport package path reference");
        AssertContains(portableTargets, "<PackageReference Include=\"LibreWPF.ProGPU\" Version=\"$(ProGpuWpfPackageVersion)\" />", "SDK ProGPU WPF package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"LibreWPF.ProGPU\" VersionOverride=\"$(ProGpuWpfPackageVersion)\" />", "SDK CPM ProGPU WPF package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"LibreWPF.Interop\" Version=\"$(ProGpuPackageVersion)\" />", "SDK ProGPU WPF interop package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"LibreWPF.Interop\" VersionOverride=\"$(ProGpuPackageVersion)\" />", "SDK CPM ProGPU WPF interop package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"ProGPU.DirectX\" Version=\"$(ProGpuPackageVersion)\" />", "SDK ProGPU DirectX package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"ProGPU.DirectX\" VersionOverride=\"$(ProGpuPackageVersion)\" />", "SDK CPM ProGPU DirectX package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"ProGPU.Compute\" Version=\"$(ProGpuPackageVersion)\" />", "SDK ProGPU compute package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"ProGPU.Compute\" VersionOverride=\"$(ProGpuPackageVersion)\" />", "SDK CPM ProGPU compute package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"ProGPU.Transpiler\" Version=\"$(ProGpuPackageVersion)\" />", "SDK ProGPU transpiler package reference");
        AssertContains(portableTargets, "<PackageReference Include=\"ProGPU.Transpiler\" VersionOverride=\"$(ProGpuPackageVersion)\" />", "SDK CPM ProGPU transpiler package reference");
        AssertContains(portableTargets, "<Compile Include=\"$(MSBuildThisFileDirectory)ProGPU.Wpf.Sdk.PortableBootstrap.cs\"", "SDK portable bootstrap injection");
        AssertContains(portableTargets, "_ProGpuWpfSdkCopyManagedTransportRuntimeAssets", "SDK managed transport runtime copy target");
        AssertContains(portableTargets, "_ProGpuWpfSdkPreserveManagedTransportRuntimeAssetsInDependencyFile", "SDK managed transport dependency target");
        AssertContains(portableTargets, "BeforeTargets=\"GenerateBuildDependencyFile\"", "SDK managed transport dependency ordering");
        AssertContains(portableTargets, "Exclude=\"$(_ProGpuWpfManagedTransportRuntimeRoot)PresentationCore.dll\"", "SDK managed transport RID PresentationCore exclusion");
        AssertContains(portableTargets, "<NuGetPackageId>$(ProGpuWpfManagedPackageId)</NuGetPackageId>", "SDK managed transport dependency package identity");
        AssertContains(portableTargets, "<PathInPackage>lib/$(_ProGpuWpfManagedTransportRuntimeTfm)/%(Filename)%(Extension)</PathInPackage>", "SDK managed transport dependency package path");
        AssertContains(portableTargets, "BeforeTargets=\"_ProGpuWpfSdkCopyPackageRuntimeAssets\"", "SDK managed transport copy ordering");
        AssertContains(portableTargets, "librewpf.transport/$(ProGpuWpfManagedPackageVersion)/lib/$(_ProGpuWpfManagedTransportRuntimeTfm)/", "SDK managed transport package root");
        AssertContains(portableTargets, "'$(RestorePackagesPath)' != ''", "SDK isolated managed transport restore root");
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
        ValidateSdkToolPayload(package);

        ValidatePackageAssemblyIdentities(packageFeed);
    }

    private static void ValidateSdkToolPayload(ZipArchive package)
    {
        _ = RequirePackageEntry(package, "tools/net10.0/PresentationBuildTasks.dll", "Core MSBuild PresentationBuildTasks assembly");
        _ = RequirePackageEntry(package, "tools/net10.0/PresentationBuildTasks.dll.config", "Core MSBuild PresentationBuildTasks config");
        _ = RequirePackageEntry(package, "tools/net472/PresentationBuildTasks.dll", "desktop MSBuild PresentationBuildTasks assembly");
        _ = RequirePackageEntry(package, "tools/net472/PresentationBuildTasks.dll.config", "desktop MSBuild PresentationBuildTasks config");
    }

    private static void ValidatePackageAssemblyIdentities(string packageFeed)
    {
        var publicKeyTokensByGroup = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (PackageAssemblyExpectation expectation in s_packageAssemblyExpectations)
        {
            Version expectedAssemblyVersion = GetExpectedPackageAssemblyVersion(expectation);
            string packageVersion = GetPackageVersion(expectation.PackageId);
            string packagePath = Path.Combine(packageFeed, $"{expectation.PackageId}.{packageVersion}.nupkg");
            string description = $"{expectation.PackageId}/{expectation.AssemblySimpleName}";
            RequireFile(packagePath, $"{description} package");

            using ZipArchive package = ZipFile.OpenRead(packagePath);
            string nuspec = ReadPackageEntry(package, $"{expectation.PackageId}.nuspec", $"{description} nuspec");
            AssertContains(nuspec, $"<version>{packageVersion}</version>", $"{description} package version");

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

    private static Version GetExpectedPackageAssemblyVersion(PackageAssemblyExpectation expectation)
    {
        if (StringComparer.Ordinal.Equals(expectation.PublicKeyTokenGroup, "ProGPU"))
        {
            return new Version(0, 1, 0, 0);
        }

        return new Version(11, 0, 0, 0);
    }

    private static void ValidateLocalProGpuPackageProvenance(
        string repoRoot,
        string packageFeed,
        string? prepackagedProGpuDirectory)
    {
        if (!string.IsNullOrWhiteSpace(prepackagedProGpuDirectory))
        {
            RequireDirectory(prepackagedProGpuDirectory, "exact prepackaged ProGPU package source");
        }

        foreach (string assemblyName in s_requiredProGpuRuntimeAssemblies)
        {
            if (!string.IsNullOrWhiteSpace(prepackagedProGpuDirectory) &&
                !string.Equals(assemblyName, "ProGPU.Wpf", StringComparison.Ordinal))
            {
                ValidateLocalPackageMatchesPrepackagedSource(
                    packageFeed,
                    prepackagedProGpuDirectory,
                    GetPackageIdForRuntimeAssembly(assemblyName));
                continue;
            }

            string repositoryAssemblyPath = GetRepositoryProGpuAssemblyPath(repoRoot, assemblyName);
            if (!File.Exists(repositoryAssemblyPath))
            {
                continue;
            }

            ValidateLocalPackageAssemblyMatchesFile(
                packageFeed,
                GetPackageIdForRuntimeAssembly(assemblyName),
                assemblyName,
                "net10.0",
                repositoryAssemblyPath,
                $"repository Release {assemblyName}.dll");
        }
    }

    private static void ValidateLocalPackageMatchesPrepackagedSource(
        string packageFeed,
        string prepackagedProGpuDirectory,
        string packageId)
    {
        string packageVersion = GetPackageVersion(packageId);
        string localPackagePath = Path.Combine(packageFeed, $"{packageId}.{packageVersion}.nupkg");
        string prepackagedSourcePath = Path.Combine(
            prepackagedProGpuDirectory,
            $"{packageId}.{packageVersion}.nupkg");

        RequireFile(localPackagePath, $"{packageId} local package");
        RequireFile(prepackagedSourcePath, $"{packageId} exact prepackaged source");
        AssertEqual(
            ComputeFileSha256(prepackagedSourcePath),
            ComputeFileSha256(localPackagePath),
            $"local {packageId} package matches exact prepackaged source");
    }

    private static string GetPackageIdForRuntimeAssembly(string assemblyName)
    {
        return assemblyName switch
        {
            "ProGPU.Wpf" => "LibreWPF.ProGPU",
            "ProGPU.Wpf.Interop" => "LibreWPF.Interop",
            _ => assemblyName
        };
    }

    private static string GetPackageVersion(string packageId)
    {
        return packageId is "LibreWPF.Sdk" or "LibreWPF.Transport" or "LibreWPF.ProGPU"
            ? SdkVersion
            : ProGpuPackageVersion;
    }

    private static void ValidateLocalWpfPackageMatchesAvailableRepositoryBuilds(string repoRoot, string packageFeed)
    {
        foreach (string assemblyName in s_requiredWpfRuntimeAssemblies)
        {
            string repositoryAssemblyPath = GetRepositoryWpfAssemblyPath(repoRoot, assemblyName);
            if (!File.Exists(repositoryAssemblyPath))
            {
                continue;
            }

            ValidateLocalPackageAssemblyMatchesFile(
                packageFeed,
                "LibreWPF.Transport",
                assemblyName,
                "net10.0",
                repositoryAssemblyPath,
                $"repository WPF transport {assemblyName}.dll");
        }
    }

    private static void ValidateLocalPackageAssemblyMatchesFile(
        string packageFeed,
        string packageId,
        string assemblySimpleName,
        string targetFramework,
        string expectedAssemblyPath,
        string expectedAssemblyDescription)
    {
        string packageVersion = GetPackageVersion(packageId);
        string packagePath = Path.Combine(packageFeed, $"{packageId}.{packageVersion}.nupkg");
        string packageEntryName = $"lib/{targetFramework}/{assemblySimpleName}.dll";

        RequireFile(packagePath, $"{packageId} local package");

        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = RequirePackageEntry(
            package,
            packageEntryName,
            $"{packageId}/{assemblySimpleName} runtime assembly");

        using Stream packageStream = entry.Open();
        string packageHash = ComputeStreamSha256(packageStream);
        string repositoryHash = ComputeFileSha256(expectedAssemblyPath);
        AssertEqual(
            repositoryHash,
            packageHash,
            $"local {packageId} package matches {expectedAssemblyDescription}");
    }

    private static string GetRepositoryProGpuAssemblyPath(string repoRoot, string assemblySimpleName)
    {
        if (string.Equals(assemblySimpleName, "ProGPU.Wpf", StringComparison.Ordinal))
        {
            return Path.Combine(
                repoRoot,
                "src",
                "ProGPU.Wpf",
                "bin",
                "Release",
                "net10.0",
                assemblySimpleName + ".dll");
        }

        return Path.Combine(
            repoRoot,
            "external",
            "ProGPU",
            "src",
            assemblySimpleName,
            "bin",
            "Release",
            "net10.0",
            assemblySimpleName + ".dll");
    }

    private static string GetRepositoryWpfAssemblyPath(string repoRoot, string assemblySimpleName)
    {
        string releasePath = Path.Combine(
            repoRoot,
            "artifacts",
            "packaging",
            "Release",
            "LibreWPF.Transport",
            "lib",
            "net10.0",
            assemblySimpleName + ".dll");
        if (File.Exists(releasePath))
        {
            return releasePath;
        }

        return Path.Combine(
            repoRoot,
            "artifacts",
            "packaging",
            "Debug",
            "LibreWPF.Transport.Debug",
            "lib",
            "net10.0",
            assemblySimpleName + ".dll");
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
            SwitchWpfSdkOnly(
                $"""
            <Project Sdk="{OriginalWpfSdk}">
              <PropertyGroup>
                <AssemblyName>{LibraryOutputAssemblyName}</AssemblyName>
                <TargetFrameworks>{ExternalAppTargetFramework}</TargetFrameworks>
                <EnableDefaultItems>false</EnableDefaultItems>
                <UseWPF>true</UseWPF>
              </PropertyGroup>

              <ItemGroup>
                <Compile Include="ExternalPanel.xaml.cs" />
                <Compile Include="ExternalThemedControl.cs" />
                <Compile Include="Properties/AssemblyInfo.cs" />
                <Page Include="ExternalPanel.xaml" />
                <Page Include="Themes/Generic.xaml" />
              </ItemGroup>
            </Project>
            """,
                "external SDK library"));

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
            SwitchWpfSdkOnly(
                $"""
            <Project Sdk="{OriginalWindowsDesktopWpfSdk}">
              <PropertyGroup>
                <AssemblyName>{AppOutputAssemblyName}</AssemblyName>
                <OutputType>WinExe</OutputType>
                <TargetFramework>{ExternalAppTargetFramework}</TargetFramework>
                <EnableDefaultItems>false</EnableDefaultItems>
                <UseWPF>true</UseWPF>
              </PropertyGroup>

              <ItemGroup>
                <Compile Include="**/*.cs" />
                <ApplicationDefinition Include="App.xaml" />
                <Page Include="**/*.xaml" Exclude="App.xaml" />
                <None Include="App.config" />
                <ProjectReference Include="../{LibraryAssemblyName}/{LibraryAssemblyName}.csproj" />
                <PackageReference Include="Extended.Wpf.Toolkit" Version="5.1.2" />
                <PackageReference Include="ProGPU.System.Drawing.Common" Version="{ProGpuPackageVersion}" />
                <Resource Include="Assets/ExternalResource.txt" />
                <Resource Include="Assets/ExternalImage.png" />
                <SplashScreen Include="Assets/ExternalSplash.png" />
                <Content Include="Assets/ExternalContent.txt">
                  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
                  <TargetPath>Assets/ExternalContent.txt</TargetPath>
                </Content>
              </ItemGroup>

              <Target Name="ValidatePortableSystemDrawingCompilerReferences"
                      BeforeTargets="CoreCompile">
                <ItemGroup>
                  <_ExternalProGpuSystemDrawingReference
                    Include="@(ReferencePathWithRefAssemblies)"
                    Condition="'%(ReferencePathWithRefAssemblies.Filename)' == 'System.Drawing.Common' And ('%(ReferencePathWithRefAssemblies.NuGetPackageId)' == 'ProGPU.System.Drawing.Common' Or $([System.String]::Copy('%(ReferencePathWithRefAssemblies.Identity)').ToLowerInvariant().Contains('progpu.system.drawing.common')))" />
                  <_ExternalNetCoreSystemDrawingFacade
                    Include="@(ReferencePathWithRefAssemblies)"
                    Condition="'%(ReferencePathWithRefAssemblies.Filename)' == 'System.Drawing' And $([System.String]::Copy('%(ReferencePathWithRefAssemblies.RootDir)%(ReferencePathWithRefAssemblies.Directory)').ToLowerInvariant().Contains('microsoft.netcore.app.ref'))" />
                </ItemGroup>
                <Error Condition="'@(_ExternalProGpuSystemDrawingReference)' == ''"
                       Text="The external SDK smoke did not resolve ProGPU.System.Drawing.Common." />
                <Error Condition="'@(_ExternalNetCoreSystemDrawingFacade)' != ''"
                       Text="The external SDK smoke retained the Microsoft.NETCore.App.Ref System.Drawing facade: @(_ExternalNetCoreSystemDrawingFacade)." />
              </Target>
            </Project>
            """,
                OriginalWindowsDesktopWpfSdk,
                "external SDK app"));

        WriteFile(
            Path.Combine(appRoot, "Assets", "ExternalResource.txt"),
            "External SDK pack resource text");
        File.WriteAllBytes(
            Path.Combine(appRoot, "Assets", "ExternalImage.png"),
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAE0lEQVR4nGP4z8DwHwwZGP6DAQBJyAn3FGMynQAAAABJRU5ErkJggg=="));
        File.WriteAllBytes(
            Path.Combine(appRoot, "Assets", "ExternalSplash.png"),
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAE0lEQVR4nGP4z8DwHwwZGP6DAQBJyAn3FGMynQAAAABJRU5ErkJggg=="));
        WriteFile(
            Path.Combine(appRoot, "Assets", "ExternalContent.txt"),
            "External SDK copied content text");
        WriteFile(
            Path.Combine(appRoot, "App.config"),
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <configuration>
              <appSettings>
                <add key="ExternalSdkAppSetting" value="External SDK app config value" />
                <add key="ExternalSdkNumericSetting" value="42" />
              </appSettings>
            </configuration>
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
            Path.Combine(appRoot, "PortableDrawingContract.cs"),
            """
            using System.Drawing;

            namespace ExternalSdkApp;

            internal static class PortableDrawingContract
            {
                internal static Color ReadPixel()
                {
                    using var bitmap = new Bitmap(1, 1);
                    bitmap.SetPixel(0, 0, Color.FromArgb(255, 37, 73, 109));
                    return bitmap.GetPixel(0, 0);
                }
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
                <x:Array
                    x:Key="ExternalArrayItems"
                    Type="{x:Type sys:String}">
                    <sys:String>External array alpha</sys:String>
                    <sys:String>External array beta</sys:String>
                </x:Array>
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
                        <Grid>
                            <ContentPresenter
                                x:Name="ExternalTemplateContent"
                                Content="{TemplateBinding Content}" />
                            <TextBlock
                                x:Name="ExternalTemplatedParentText"
                                Visibility="Collapsed"
                                Text="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=Content}" />
                        </Grid>
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
                xmlns:library="clr-namespace:ExternalSdkLibrary;assembly=ExternalSdkControls"
                xmlns:primitives="clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework"
                xmlns:ribbon="clr-namespace:System.Windows.Controls.Ribbon;assembly=System.Windows.Controls.Ribbon"
                xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
                xmlns:sys="clr-namespace:System;assembly=System.Runtime"
                xmlns:wpf="clr-namespace:System.Windows;assembly=PresentationFramework"
                xmlns:xctk="http://schemas.xceed.com/wpf/xaml/toolkit"
                xmlns:xcad="http://schemas.xceed.com/wpf/xaml/avalondock"
                Title="External SDK App"
                Width="320"
                Height="200"
                Left="44"
                Top="52"
                Topmost="True"
                ResizeMode="NoResize"
                Loaded="OnExternalWindowLoaded"
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
                <shell:WindowChrome.WindowChrome>
                    <shell:WindowChrome
                        CaptionHeight="28"
                        ResizeBorderThickness="8"
                        GlassFrameThickness="0"
                        NonClientFrameEdges="Top"
                        UseAeroCaptionButtons="False" />
                </shell:WindowChrome.WindowChrome>
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
                    <DataTemplate DataType="{x:Type local:ExternalItem}">
                        <TextBlock
                            x:Name="ExternalImplicitItemTemplateText"
                            Text="{Binding Name, StringFormat=External implicit {0}}" />
                    </DataTemplate>
                    <local:ExternalItemTemplateSelector
                        x:Key="ExternalItemTemplateSelector"
                        DefaultTemplate="{StaticResource ExternalDefaultItemTemplate}"
                        FrameworkTemplate="{StaticResource ExternalFrameworkItemTemplate}"
                        RenderingTemplate="{StaticResource ExternalRenderingItemTemplate}" />
                    <Style
                        x:Key="ExternalFrameworkItemContainerStyle"
                        TargetType="{x:Type ListBoxItem}">
                        <Setter Property="Tag" Value="external style selector framework container" />
                    </Style>
                    <Style
                        x:Key="ExternalDefaultItemContainerStyle"
                        TargetType="{x:Type ListBoxItem}">
                        <Setter Property="Tag" Value="external style selector default container" />
                    </Style>
                    <local:ExternalItemContainerStyleSelector
                        x:Key="ExternalItemContainerStyleSelector"
                        DefaultStyle="{StaticResource ExternalDefaultItemContainerStyle}"
                        FrameworkStyle="{StaticResource ExternalFrameworkItemContainerStyle}" />
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
                    <ControlTemplate x:Key="ExternalValidationErrorTemplate">
                        <DockPanel LastChildFill="True">
                            <TextBlock
                                x:Name="ExternalValidationErrorGlyph"
                                DockPanel.Dock="Right"
                                Foreground="Crimson"
                                Tag="{Binding [0].ErrorContent}"
                                Text="!" />
                            <AdornedElementPlaceholder x:Name="ExternalValidationErrorPlaceholder" />
                        </DockPanel>
                    </ControlTemplate>
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
                    <CommandBinding
                        Command="{x:Static wpf:SystemCommands.MaximizeWindowCommand}"
                        CanExecute="OnExternalSystemCommandCanExecute"
                        Executed="OnExternalSystemCommandExecuted" />
                    <CommandBinding
                        Command="{x:Static wpf:SystemCommands.MinimizeWindowCommand}"
                        CanExecute="OnExternalSystemCommandCanExecute"
                        Executed="OnExternalSystemCommandExecuted" />
                    <CommandBinding
                        Command="{x:Static wpf:SystemCommands.RestoreWindowCommand}"
                        CanExecute="OnExternalSystemCommandCanExecute"
                        Executed="OnExternalSystemCommandExecuted" />
                    <CommandBinding
                        Command="{x:Static wpf:SystemCommands.ShowSystemMenuCommand}"
                        CanExecute="OnExternalSystemCommandCanExecute"
                        Executed="OnExternalSystemCommandExecuted" />
                </Window.CommandBindings>
                <Window.InputBindings>
                    <KeyBinding
                        Command="{x:Static local:MainWindow.ExternalCommand}"
                        Gesture="Ctrl+E" />
                    <MouseBinding
                        Command="{x:Static local:MainWindow.ExternalCommand}"
                        CommandParameter="ExternalMouseCommandParameter"
                        Gesture="LeftDoubleClick" />
                </Window.InputBindings>
                <StackPanel
                    x:Name="ExternalFocusPanel"
                    FocusManager.FocusedElement="{Binding ElementName=ExternalCommandButton}"
                    FocusManager.IsFocusScope="True"
                    KeyboardNavigation.ControlTabNavigation="Cycle"
                    KeyboardNavigation.DirectionalNavigation="Contained"
                    KeyboardNavigation.TabNavigation="Cycle"
                    primitives:Thumb.DragDelta="OnExternalBubbledThumbDragDelta">
                    <TextBlock
                        x:Name="TitleText"
                        MouseLeftButtonDown="OnExternalTitleMouseLeftButtonDown"
                        shell:WindowChrome.IsHitTestVisibleInChrome="True"
                        Text="External SDK app" />
                    <Border
                        x:Name="ExternalLiveMouseProbe"
                        Height="18"
                        Margin="0,24,0,0"
                        Background="Transparent"
                        MouseLeftButtonDown="OnExternalLiveMouseProbeMouseLeftButtonDown">
                        <TextBlock Text="External live mouse probe" />
                    </Border>
                    <TextBlock
                        x:Name="ExternalDispatcherTimerText"
                        Text="{Binding ExternalDispatcherTimerStatus}" />
                    <TextBlock
                        x:Name="ExternalAsyncContinuationText"
                        Text="{Binding ExternalAsyncContinuationStatus}" />
                    <TextBlock
                        x:Name="ExternalDispatcherInvokeAsyncText"
                        Text="{Binding ExternalDispatcherInvokeAsyncStatus}" />
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
                        x:Name="ExternalRuntimeMergedResourceText"
                        Foreground="{DynamicResource ExternalRuntimeMergedBrush}"
                        Text="{DynamicResource ExternalRuntimeMergedText}" />
                    <ItemsControl
                        x:Name="ExternalArrayItemsControl"
                        ItemsSource="{StaticResource ExternalArrayItems}" />
                    <TextBlock
                        x:Name="ExternalNullIntrinsicText"
                        Tag="{x:Null}"
                        Text="External null resource" />
                    <TextBlock
                        x:Name="ExternalStartupResourceText"
                        Foreground="{DynamicResource ExternalStartupBrush}"
                        Text="{DynamicResource ExternalStartupText}" />
                    <StackPanel x:Name="ExternalImplicitStylePanel">
                        <StackPanel.Resources>
                            <Style TargetType="{x:Type TextBlock}">
                                <Setter Property="Tag" Value="external implicit style active" />
                                <Setter Property="Foreground" Value="{DynamicResource ExternalStaticBrush}" />
                            </Style>
                        </StackPanel.Resources>
                        <TextBlock
                            x:Name="ExternalImplicitStyledText"
                            Text="External implicit style text" />
                    </StackPanel>
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
                    <TextBlock
                        x:Name="ExternalSelfBindingText"
                        Tag="External self tag"
                        Text="{Binding RelativeSource={RelativeSource Self}, Path=Tag}" />
                    <Border
                        x:Name="ExternalAncestorBindingBorder"
                        Tag="External ancestor tag">
                        <TextBlock
                            x:Name="ExternalAncestorBindingText"
                            Text="{Binding RelativeSource={RelativeSource AncestorType={x:Type Border}}, Path=Tag}" />
                    </Border>
                    <Border
                        x:Name="ExternalAncestorLevelOuterBorder"
                        Tag="External second ancestor tag">
                        <Border
                            x:Name="ExternalAncestorLevelInnerBorder"
                            Tag="External first ancestor tag">
                            <TextBlock
                                x:Name="ExternalAncestorLevelBindingText"
                                Text="{Binding RelativeSource={RelativeSource AncestorType={x:Type Border}, AncestorLevel=2}, Path=Tag}" />
                        </Border>
                    </Border>
                    <Button
                        x:Name="ExternalStyledButton"
                        Style="{StaticResource ExternalTriggeredButtonStyle}" />
                    <StackPanel
                        x:Name="ExternalChromeCommandPanel"
                        Orientation="Horizontal"
                        shell:WindowChrome.IsHitTestVisibleInChrome="True"
                        shell:WindowChrome.ResizeGripDirection="BottomRight">
                        <Button
                            x:Name="ExternalSystemMaximizeButton"
                            Command="{x:Static wpf:SystemCommands.MaximizeWindowCommand}"
                            CommandParameter="ExternalSystemMaximizeParameter"
                            CommandTarget="{Binding RelativeSource={RelativeSource AncestorType={x:Type Window}}}"
                            Content="Maximize" />
                        <Button
                            x:Name="ExternalSystemMinimizeButton"
                            Command="{x:Static wpf:SystemCommands.MinimizeWindowCommand}"
                            CommandParameter="ExternalSystemMinimizeParameter"
                            CommandTarget="{Binding RelativeSource={RelativeSource AncestorType={x:Type Window}}}"
                            Content="Minimize" />
                        <Button
                            x:Name="ExternalSystemRestoreButton"
                            Command="{x:Static wpf:SystemCommands.RestoreWindowCommand}"
                            CommandParameter="ExternalSystemRestoreParameter"
                            CommandTarget="{Binding RelativeSource={RelativeSource AncestorType={x:Type Window}}}"
                            Content="Restore" />
                        <Button
                            x:Name="ExternalSystemMenuButton"
                            Command="{x:Static wpf:SystemCommands.ShowSystemMenuCommand}"
                            CommandParameter="ExternalSystemMenuParameter"
                            CommandTarget="{Binding RelativeSource={RelativeSource AncestorType={x:Type Window}}}"
                            Content="Menu" />
                    </StackPanel>
                    <local:ExternalClassCommandTextBox
                        x:Name="ExternalClassCommandTarget"
                        Text="External class command target" />
                    <Button
                        x:Name="ExternalClassCommandButton"
                        Command="{x:Static local:ExternalClassCommandTextBox.ExternalClassCommand}"
                        CommandParameter="ExternalClassCommandParameter"
                        CommandTarget="{Binding ElementName=ExternalClassCommandTarget}"
                        Content="External class command" />
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
                    <primitives:Popup
                        x:Name="ExternalStandalonePopup"
                        AllowsTransparency="True"
                        Placement="Bottom"
                        PlacementTarget="{Binding ElementName=ExternalPopupOwnerButton}"
                        StaysOpen="False">
                        <Border
                            BorderBrush="DarkSlateGray"
                            BorderThickness="1"
                            Background="White"
                            Padding="4">
                            <TextBlock
                                x:Name="ExternalStandalonePopupText"
                                Text="External standalone popup content" />
                        </Border>
                    </primitives:Popup>
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
                    <ribbon:Ribbon
                        x:Name="ExternalRibbon"
                        Title="External Ribbon"
                        Visibility="Collapsed">
                        <ribbon:RibbonTab
                            x:Name="ExternalRibbonTab"
                            Header="External Tab">
                            <ribbon:RibbonGroup
                                x:Name="ExternalRibbonGroup"
                                Header="External Group">
                                <ribbon:RibbonButton
                                    x:Name="ExternalRibbonButton"
                                    Label="External Ribbon Button"
                                    Command="{x:Static local:MainWindow.ExternalCommand}"
                                    CommandParameter="ExternalRibbonCommandParameter"
                                    CommandTarget="{Binding ElementName=ExternalCommandButton}" />
                                <ribbon:RibbonCheckBox
                                    x:Name="ExternalRibbonCheckBox"
                                    Label="External Ribbon Check"
                                    IsChecked="True" />
                            </ribbon:RibbonGroup>
                        </ribbon:RibbonTab>
                    </ribbon:Ribbon>
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
                    <RepeatButton
                        x:Name="ExternalRepeatButton"
                        Content="External repeat"
                        Delay="250"
                        Interval="75"
                        Click="OnExternalRepeatButtonClick" />
                    <ScrollBar
                        x:Name="ExternalScrollBar"
                        Orientation="Vertical"
                        Minimum="0"
                        Maximum="10"
                        Value="4"
                        SmallChange="1"
                        LargeChange="3"
                        ViewportSize="2"
                        Scroll="OnExternalScrollBarScroll" />
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
                    <primitives:Thumb
                        x:Name="ExternalDragThumb"
                        Width="24"
                        Height="18"
                        Tag="external drag thumb"
                        DragStarted="OnExternalThumbDragStarted"
                        DragDelta="OnExternalThumbDragDelta"
                        DragCompleted="OnExternalThumbDragCompleted" />
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
                    <Grid
                        x:Name="ExternalGridSplitterGrid"
                        Width="180"
                        Height="32">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition
                                Width="60"
                                MinWidth="24" />
                            <ColumnDefinition Width="5" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <TextBlock
                            x:Name="ExternalGridSplitterLeftPane"
                            Grid.Column="0"
                            Text="external split left" />
                        <GridSplitter
                            x:Name="ExternalGridSplitter"
                            Grid.Column="1"
                            Width="5"
                            HorizontalAlignment="Stretch"
                            VerticalAlignment="Stretch"
                            ResizeBehavior="PreviousAndNext"
                            ResizeDirection="Columns"
                            ShowsPreview="True"
                            DragIncrement="3"
                            KeyboardIncrement="7" />
                        <TextBlock
                            x:Name="ExternalGridSplitterRightPane"
                            Grid.Column="2"
                            Text="external split right" />
                    </Grid>
                    <ContentControl
                        x:Name="ExternalTemplatePresenter"
                        Content="{Binding SelectedExternalItem}"
                        ContentTemplate="{StaticResource ExternalItemTemplate}" />
                    <ContentControl
                        x:Name="ExternalTemplateSelectorPresenter"
                        Content="{Binding SelectedExternalItem}"
                        ContentTemplateSelector="{StaticResource ExternalItemTemplateSelector}" />
                    <ContentPresenter
                        x:Name="ExternalImplicitTemplatePresenter"
                        Content="{Binding SelectedExternalItem}" />
                    <ItemsControl
                        x:Name="ExternalTemplateSelectorItems"
                        ItemTemplateSelector="{StaticResource ExternalItemTemplateSelector}"
                        ItemsSource="{Binding ExternalItems}" />
                    <ListBox
                        x:Name="ExternalStyleSelectorItemsList"
                        ItemContainerStyleSelector="{StaticResource ExternalItemContainerStyleSelector}"
                        ItemsSource="{Binding ExternalItems}">
                        <ListBox.ItemTemplate>
                            <DataTemplate DataType="{x:Type local:ExternalItem}">
                                <TextBlock
                                    x:Name="ExternalStyleSelectorItemTextBlock"
                                    Tag="external style selector item template"
                                    Text="{Binding Name}" />
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                    <ListBox
                        x:Name="ExternalItemsList"
                        DisplayMemberPath="Name"
                        ItemsSource="{Binding ExternalItems}"
                        SelectedIndex="1" />
                    <ListBox
                        x:Name="ExternalMultiSelectItemsList"
                        DisplayMemberPath="Name"
                        ItemsSource="{Binding ExternalItems}"
                        SelectionChanged="OnExternalMultiSelectionChanged"
                        SelectionMode="Multiple" />
                    <ListBox
                        x:Name="ExternalPreviousDataItemsList"
                        ItemsSource="{Binding ExternalItems}">
                        <ListBox.ItemTemplate>
                            <DataTemplate DataType="{x:Type local:ExternalItem}">
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock
                                        x:Name="ExternalPreviousDataCurrentText"
                                        Text="{Binding Name}" />
                                    <TextBlock
                                        x:Name="ExternalPreviousDataPreviousText"
                                        Text="{Binding RelativeSource={RelativeSource PreviousData}, Path=Name, FallbackValue=No previous}" />
                                </StackPanel>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                    <ListBox
                        x:Name="ExternalItemsPanelList"
                        AlternationCount="4"
                        ItemContainerStyle="{StaticResource ExternalItemContainerStyle}"
                        ItemsPanel="{StaticResource ExternalItemsPanelTemplate}"
                        ItemsSource="{Binding ExternalItems}"
                        ItemStringFormat="External item {0}" />
                    <ListBox
                        x:Name="ExternalVirtualizingItemsList"
                        DisplayMemberPath="Name"
                        ItemsSource="{Binding ExternalItems}"
                        ScrollViewer.CanContentScroll="True"
                        VirtualizingPanel.IsVirtualizing="True"
                        VirtualizingPanel.VirtualizationMode="Recycling">
                        <ListBox.ItemsPanel>
                            <ItemsPanelTemplate>
                                <VirtualizingStackPanel Orientation="Vertical" />
                            </ItemsPanelTemplate>
                        </ListBox.ItemsPanel>
                    </ListBox>
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
                    <xctk:WatermarkTextBox
                        x:Name="ExternalToolkitWatermarkTextBox"
                        Watermark="External toolkit watermark"
                        Text="{Binding ExternalToolkitText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                    <xctk:IntegerUpDown
                        x:Name="ExternalToolkitIntegerUpDown"
                        Minimum="1"
                        Maximum="9"
                        Value="{Binding ExternalToolkitNumber, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                    <xctk:ColorPicker
                        x:Name="ExternalToolkitColorPicker"
                        DisplayColorAndName="True"
                        ShowAvailableColors="True"
                        ShowRecentColors="True"
                        ShowStandardColors="True"
                        SelectedColor="{Binding ExternalToolkitAccentColor, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                    <xctk:CalculatorUpDown
                        x:Name="ExternalToolkitCalculatorUpDown"
                        Minimum="0"
                        Maximum="100"
                        Increment="0.25"
                        FormatString="F2"
                        Value="{Binding ExternalToolkitEstimate, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                    <xctk:BusyIndicator
                        x:Name="ExternalToolkitBusyIndicator"
                        BusyContent="External busy"
                        IsBusy="{Binding ExternalToolkitIsBusy, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}">
                        <TextBlock
                            x:Name="ExternalToolkitBusyContent"
                            Text="External BusyIndicator content" />
                    </xctk:BusyIndicator>
                    <xctk:PropertyGrid
                        x:Name="ExternalToolkitPropertyGrid"
                        AutoGenerateProperties="True"
                        SelectedObject="{Binding SelectedExternalItem}" />
                    <xctk:DropDownButton
                        x:Name="ExternalToolkitDropDownButton"
                        Content="External actions">
                        <xctk:DropDownButton.DropDownContent>
                            <StackPanel
                                x:Name="ExternalToolkitDropDownContentRoot"
                                MinWidth="160">
                                <Button
                                    x:Name="ExternalToolkitDropDownActionButton"
                                    Content="Apply external action"
                                    Click="OnExternalToolkitDropDownActionClick" />
                            </StackPanel>
                        </xctk:DropDownButton.DropDownContent>
                    </xctk:DropDownButton>
                    <xctk:SplitButton
                        x:Name="ExternalToolkitSplitButton"
                        Content="External split"
                        Click="OnExternalToolkitSplitButtonClick">
                        <xctk:SplitButton.DropDownContent>
                            <StackPanel
                                x:Name="ExternalToolkitSplitDropDownContentRoot"
                                MinWidth="160">
                                <Button
                                    x:Name="ExternalToolkitSplitDropDownActionButton"
                                    Content="Apply split action"
                                    Click="OnExternalToolkitSplitDropDownActionClick" />
                            </StackPanel>
                        </xctk:SplitButton.DropDownContent>
                    </xctk:SplitButton>
                    <xctk:RichTextBox
                        x:Name="ExternalToolkitRichTextBox"
                        Height="70"
                        Text="{Binding ExternalToolkitRichText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}">
                        <xctk:RichTextBox.TextFormatter>
                            <xctk:PlainTextFormatter />
                        </xctk:RichTextBox.TextFormatter>
                    </xctk:RichTextBox>
                    <xctk:MultiLineTextEditor
                        x:Name="ExternalToolkitMultiLineTextEditor"
                        Height="64"
                        Text="{Binding ExternalToolkitMultilineText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                    <xctk:ButtonSpinner
                        x:Name="ExternalToolkitButtonSpinner"
                        ShowSpinner="True"
                        SpinnerLocation="Right"
                        Spin="OnExternalToolkitButtonSpinnerSpin">
                        <Border Padding="4">
                            <TextBlock
                                x:Name="ExternalToolkitButtonSpinnerContent"
                                Text="{Binding ExternalToolkitSpinnerCount, StringFormat=External spinner count {0}}" />
                        </Border>
                    </xctk:ButtonSpinner>
                    <xctk:Wizard
                        x:Name="ExternalToolkitWizard"
                        Height="160"
                        BackButtonContent="Back"
                        NextButtonContent="Next"
                        FinishButtonContent="Finish"
                        CancelButtonContent="Cancel"
                        FinishButtonClosesWindow="False"
                        CancelButtonClosesWindow="False"
                        PageChanged="OnExternalToolkitWizardPageChanged"
                        Finish="OnExternalToolkitWizardFinish"
                        Cancel="OnExternalToolkitWizardCancel">
                        <xctk:WizardPage
                            x:Name="ExternalToolkitWizardScopePage"
                            Title="External scope"
                            Description="External SDK wizard scope"
                            PageType="Interior"
                            CanFinish="False">
                            <TextBlock Text="{Binding SelectedExternalItem.Name}" />
                        </xctk:WizardPage>
                        <xctk:WizardPage
                            x:Name="ExternalToolkitWizardReviewPage"
                            Title="External review"
                            Description="External SDK wizard review"
                            PageType="Interior"
                            CanFinish="True">
                            <TextBlock Text="{Binding ExternalToolkitWizardStatus}" />
                        </xctk:WizardPage>
                    </xctk:Wizard>
                    <xctk:WindowContainer
                        x:Name="ExternalToolkitWindowContainer"
                        Height="130"
                        Background="#FFEFF4F8"
                        ModalBackgroundBrush="#33000000">
                        <xctk:WindowControl
                            x:Name="ExternalToolkitWindowControl"
                            Caption="External toolkit window"
                            Width="230"
                            Height="104"
                            Left="12"
                            Top="10"
                            CloseButtonVisibility="Visible"
                            WindowStyle="SingleBorderWindow"
                            WindowBackground="#FFFFFFFF"
                            WindowInactiveBackground="#FFEFF4F8"
                            WindowBorderBrush="#5B8DEF"
                            WindowBorderThickness="1"
                            WindowThickness="2"
                            Visibility="{Binding ExternalToolkitWindowControlVisibility, Mode=TwoWay}"
                            Activated="OnExternalToolkitWindowControlActivated"
                            HeaderMouseLeftButtonClicked="OnExternalToolkitWindowControlHeaderMouseLeftButtonClicked"
                            HeaderDragDelta="OnExternalToolkitWindowControlHeaderDragDelta"
                            CloseButtonClicked="OnExternalToolkitWindowControlCloseButtonClicked">
                            <StackPanel Margin="8">
                                <TextBlock Text="External WindowControl primitive" />
                                <TextBox
                                    x:Name="ExternalToolkitWindowControlInputTextBox"
                                    Text="{Binding ExternalToolkitWindowControlText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
                            </StackPanel>
                        </xctk:WindowControl>
                    </xctk:WindowContainer>
                    <xcad:DockingManager
                        x:Name="ExternalDockManager"
                        AllowMixedOrientation="True"
                        AutoHideWindowClosingTimer="600"
                        GridSplitterHeight="4"
                        GridSplitterWidth="4"
                        Height="120">
                        <xcad:DockingManager.Theme>
                            <xcad:AeroTheme />
                        </xcad:DockingManager.Theme>
                        <xcad:LayoutRoot x:Name="ExternalDockLayoutRoot">
                            <xcad:LayoutPanel Orientation="Horizontal">
                                <xcad:LayoutAnchorablePane x:Name="ExternalDockAnchorablePane">
                                    <xcad:LayoutAnchorable
                                        x:Name="ExternalToolkitPane"
                                        Title="Toolkit"
                                        ContentId="external-toolkit"
                                        CanClose="False">
                                        <TextBlock Text="External Toolkit pane" />
                                    </xcad:LayoutAnchorable>
                                </xcad:LayoutAnchorablePane>
                                <xcad:LayoutDocumentPane x:Name="ExternalDockDocumentPane">
                                    <xcad:LayoutDocument
                                        x:Name="ExternalDockDocument"
                                        Title="Document"
                                        ContentId="external-document">
                                        <TextBlock Text="External dock document" />
                                    </xcad:LayoutDocument>
                                </xcad:LayoutDocumentPane>
                            </xcad:LayoutPanel>
                        </xcad:LayoutRoot>
                    </xcad:DockingManager>
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
                        IsTextSearchEnabled="True"
                        ItemsSource="{Binding ExternalItems}"
                        SelectedValuePath="Kind"
                        SelectedValue="{Binding SelectedExternalKind, Mode=TwoWay}"
                        SelectionChanged="OnExternalSelectionChanged"
                        TextSearch.TextPath="Name" />
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
                    <FlowDocumentScrollViewer
                        x:Name="ExternalFlowDocumentScrollViewer"
                        IsToolBarVisible="False">
                        <FlowDocument
                            ColumnWidth="320"
                            PagePadding="2">
                            <Paragraph>
                                <Run Text="External scroll viewer document" />
                            </Paragraph>
                            <List MarkerStyle="Disc">
                                <ListItem>
                                    <Paragraph><Run Text="External scroll viewer item" /></Paragraph>
                                </ListItem>
                            </List>
                        </FlowDocument>
                    </FlowDocumentScrollViewer>
                    <FlowDocumentPageViewer
                        x:Name="ExternalFlowDocumentPageViewer"
                        Zoom="125"
                        MinZoom="50"
                        MaxZoom="250">
                        <FlowDocument
                            ColumnWidth="360"
                            PagePadding="5">
                            <Paragraph>
                                <Run Text="External page viewer document" />
                            </Paragraph>
                            <List MarkerStyle="Square">
                                <ListItem>
                                    <Paragraph><Run Text="External page viewer item" /></Paragraph>
                                </ListItem>
                            </List>
                        </FlowDocument>
                    </FlowDocumentPageViewer>
                    <FlowDocumentReader
                        x:Name="ExternalFlowDocumentReader"
                        ViewingMode="Scroll">
                        <FlowDocument PagePadding="3">
                            <Paragraph>
                                <Run Text="External reader document" />
                            </Paragraph>
                        </FlowDocument>
                    </FlowDocumentReader>
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
                    <TextBlock
                        x:Name="ExternalFallbackBindingText"
                        Text="{Binding MissingExternalBindingText, FallbackValue=External fallback text}" />
                    <TextBlock
                        x:Name="ExternalTargetNullBindingText"
                        Text="{Binding ExternalNullBindingText, TargetNullValue=External null text}" />
                    <TextBox
                        x:Name="ExternalBindingTransferTextBox"
                        SourceUpdated="OnExternalBindingSourceUpdated"
                        TargetUpdated="OnExternalBindingTargetUpdated"
                        Text="{Binding ExternalBindingTransferText, Mode=TwoWay, UpdateSourceTrigger=Explicit, NotifyOnSourceUpdated=True, NotifyOnTargetUpdated=True}" />
                    <TextBox
                        x:Name="ExternalDelayedBindingTextBox"
                        Text="{Binding ExternalDelayedBindingText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged, Delay=25}" />
                    <TextBlock
                        x:Name="ExternalOneTimeBindingText"
                        Text="{Binding ExternalOneTimeBindingValue, Mode=OneTime}" />
                    <TextBox
                        x:Name="ExternalOneWayToSourceBindingTextBox"
                        Text="{Binding ExternalOneWayToSourceBindingText, Mode=OneWayToSource, UpdateSourceTrigger=PropertyChanged}" />
                    <TextBlock
                        x:Name="ExternalIndexedBindingText"
                        Text="{Binding ExternalIndexedItems[1].Name}" />
                    <TextBlock
                        x:Name="ExternalStringFormatBindingText"
                        Text="{Binding SelectedExternalItem.Name, StringFormat=External formatted {0}}" />
                    <TextBox
                        x:Name="ExternalSpellCheckTextBox"
                        SpellCheck.IsEnabled="True"
                        SpellCheck.SpellingReform="PreAndPostreform"
                        Text="external misspelled wrdz" />
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
                        Validation.ErrorTemplate="{StaticResource ExternalValidationErrorTemplate}"
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
                    <TextBox
                        x:Name="ExternalDataErrorValidationTextBox"
                        Validation.Error="OnExternalValidationError"
                        Text="{Binding DataErrorText, Mode=TwoWay, UpdateSourceTrigger=Explicit, ValidatesOnDataErrors=True, NotifyOnValidationError=True}" />
                    <TextBox
                        x:Name="ExternalNotifyDataErrorValidationTextBox"
                        Validation.Error="OnExternalValidationError"
                        Text="{Binding NotifyDataErrorText, Mode=TwoWay, UpdateSourceTrigger=Explicit, ValidatesOnNotifyDataErrors=True, NotifyOnValidationError=True}" />
                    <TextBox
                        x:Name="ExternalExceptionValidationTextBox"
                        Validation.Error="OnExternalValidationError">
                        <TextBox.Text>
                            <Binding
                                Path="ExceptionValidationText"
                                Mode="TwoWay"
                                NotifyOnValidationError="True"
                                UpdateSourceTrigger="Explicit">
                                <Binding.ValidationRules>
                                    <ExceptionValidationRule />
                                </Binding.ValidationRules>
                            </Binding>
                        </TextBox.Text>
                    </TextBox>
                    <TextBox
                        x:Name="ExternalExceptionFilterTextBox"
                        Validation.Error="OnExternalValidationError">
                        <TextBox.Text>
                            <Binding
                                Path="ExceptionFilterText"
                                Mode="TwoWay"
                                NotifyOnValidationError="True"
                                UpdateSourceTrigger="Explicit"
                                UpdateSourceExceptionFilter="OnExternalUpdateSourceExceptionFilter">
                                <Binding.ValidationRules>
                                    <ExceptionValidationRule />
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
                    <StackPanel
                        x:Name="ExternalNavigationCommandPanel"
                        Orientation="Horizontal">
                        <Button
                            x:Name="ExternalNavigationBackButton"
                            Command="NavigationCommands.BrowseBack"
                            CommandTarget="{Binding ElementName=ExternalFrame}"
                            Content="Back" />
                        <Button
                            x:Name="ExternalNavigationForwardButton"
                            Command="NavigationCommands.BrowseForward"
                            CommandTarget="{Binding ElementName=ExternalFrame}"
                            Content="Forward" />
                    </StackPanel>
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
                xmlns:library="clr-namespace:ExternalSdkLibrary;assembly=ExternalSdkControls"
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
            Path.Combine(appRoot, "ExternalPageFunction.xaml"),
            """
            <PageFunction
                x:Class="ExternalSdkApp.ExternalPageFunction"
                x:TypeArguments="sys:String"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:sys="clr-namespace:System;assembly=System.Private.CoreLib"
                Title="External PageFunction">
                <StackPanel>
                    <TextBlock
                        x:Name="ExternalPageFunctionTitle"
                        Text="External SDK page function" />
                    <TextBlock
                        x:Name="ExternalPageFunctionSubtitle"
                        Text="External SDK PageFunction return path" />
                </StackPanel>
            </PageFunction>
            """);

        WriteFile(
            Path.Combine(appRoot, "ExternalPageFunction.xaml.cs"),
            """
            using System.Windows.Navigation;

            namespace ExternalSdkApp;

            public partial class ExternalPageFunction : PageFunction<string>
            {
                public const string DefaultResult = "External PageFunction return";

                public ExternalPageFunction()
                {
                    InitializeComponent();
                }

                public void Complete(string? result = null)
                {
                    OnReturn(new ReturnEventArgs<string>(result ?? DefaultResult));
                }
            }
            """);

        WriteFile(
            Path.Combine(appRoot, "ExternalNavigationWindow.xaml"),
            """
            <NavigationWindow
                x:Class="ExternalSdkApp.ExternalNavigationWindow"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Title="External SDK NavigationWindow"
                Width="180"
                Height="120"
                Source="ExternalPage.xaml"
                ShowsNavigationUI="False"
                Navigating="OnExternalNavigationWindowNavigating"
                Navigated="OnExternalNavigationWindowNavigated"
                LoadCompleted="OnExternalNavigationWindowLoadCompleted" />
            """);

        WriteFile(
            Path.Combine(appRoot, "ExternalNavigationWindow.xaml.cs"),
            """
            using System;
            using System.Windows.Navigation;

            namespace ExternalSdkApp;

            public partial class ExternalNavigationWindow : NavigationWindow
            {
                public int NavigatingCount { get; private set; }

                public int NavigatedCount { get; private set; }

                public int LoadCompletedCount { get; private set; }

                public int ClosingCount { get; private set; }

                public int ClosedCount { get; private set; }

                public string? LastNavigatingUri { get; private set; }

                public string? LastNavigatedUri { get; private set; }

                public string? LastLoadCompletedUri { get; private set; }

                public string? LastNavigationMode { get; private set; }

                public string? LastContentType { get; private set; }

                public ExternalNavigationWindow()
                {
                    InitializeComponent();
                    Closing += (_, e) =>
                    {
                        ClosingCount++;
                        if (e.Cancel)
                        {
                            throw new InvalidOperationException("External NavigationWindow close should not be canceled.");
                        }
                    };
                    Closed += (_, _) => ClosedCount++;
                }

                private void OnExternalNavigationWindowNavigating(object sender, NavigatingCancelEventArgs e)
                {
                    NavigatingCount++;
                    LastNavigatingUri = e.Uri?.ToString();
                    LastNavigationMode = e.NavigationMode.ToString();
                }

                private void OnExternalNavigationWindowNavigated(object sender, NavigationEventArgs e)
                {
                    NavigatedCount++;
                    LastNavigatedUri = e.Uri?.ToString();
                    LastContentType = e.Content?.GetType().FullName;
                }

                private void OnExternalNavigationWindowLoadCompleted(object sender, NavigationEventArgs e)
                {
                    LoadCompletedCount++;
                    LastLoadCompletedUri = e.Uri?.ToString();
                }
            }
            """);

        WriteFile(
            Path.Combine(appRoot, "ExternalLoadComponentView.xaml"),
            """
            <UserControl
                x:Class="ExternalSdkApp.ExternalLoadComponentView"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:library="clr-namespace:ExternalSdkLibrary;assembly=ExternalSdkControls">
                <StackPanel>
                    <TextBlock
                        x:Name="ExternalLoadComponentText"
                        Foreground="{StaticResource ExternalStaticBrush}"
                        Text="{StaticResource ExternalStaticText}" />
                    <library:ExternalPanel
                        x:Name="ExternalLoadComponentPanel"
                        Caption="External loaded component panel" />
                </StackPanel>
            </UserControl>
            """);

        WriteFile(
            Path.Combine(appRoot, "ExternalLoadComponentView.xaml.cs"),
            """
            using System.Windows.Controls;

            namespace ExternalSdkApp;

            public partial class ExternalLoadComponentView : UserControl
            {
                public ExternalLoadComponentView()
                {
                    InitializeComponent();
                }
            }
            """);

        WriteFile(
            Path.Combine(appRoot, "ExternalManualLoadComponentView.xaml"),
            """
            <UserControl
                x:Class="ExternalSdkApp.ExternalManualLoadComponentView"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:library="clr-namespace:ExternalSdkLibrary;assembly=ExternalSdkControls">
                <StackPanel>
                    <TextBlock
                        x:Name="ExternalManualLoadComponentText"
                        Foreground="{StaticResource ExternalStaticBrush}"
                        Text="{StaticResource ExternalStaticText}" />
                    <library:ExternalPanel
                        x:Name="ExternalManualLoadComponentPanel"
                        Caption="External manual loaded component panel" />
                </StackPanel>
            </UserControl>
            """);

        WriteFile(
            Path.Combine(appRoot, "ExternalManualLoadComponentView.xaml.cs"),
            """
            using System.Windows.Controls;

            namespace ExternalSdkApp;

            public partial class ExternalManualLoadComponentView : UserControl
            {
            }
            """);

        WriteFile(
            Path.Combine(appRoot, "MainWindow.xaml.cs"),
            """
            using System;
            using System.Collections;
            using System.Collections.ObjectModel;
            using System.Collections.Specialized;
            using System.Collections.Generic;
            using System.Configuration;
            using System.ComponentModel;
            using System.Globalization;
            using System.IO;
            using System.IO.Compression;
            using System.Linq;
            using System.Net;
            using System.Net.Cache;
            using System.Net.Sockets;
            using System.Reflection;
            using System.Text;
            using System.Threading;
            using System.Windows;
            using System.Windows.Automation;
            using System.Windows.Automation.Peers;
            using System.Windows.Automation.Provider;
            using System.Windows.Controls;
            using System.Windows.Controls.Ribbon;
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
            using System.Threading.Tasks;
            using ExternalSdkLibrary;
            using Microsoft.Win32;

            namespace ExternalSdkApp;

            public partial class MainWindow : Window, INotifyPropertyChanged, IDataErrorInfo, INotifyDataErrorInfo
            {
                public static readonly RoutedUICommand ExternalCommand = new(
                    "External SDK command",
                    nameof(ExternalCommand),
                    typeof(MainWindow));

                private const string LiveValidationEnvironmentVariable = "PROGPU_WPF_EXTERNAL_LIVE_VALIDATE";
                private const int LiveValidationMaxAttempts = 1000;
                private static readonly TimeSpan LiveValidationRetryDelay = TimeSpan.FromMilliseconds(16);
                private bool _externalLiveValidationStarted;

                public ExternalRequeryCommand ExternalRequeryCommand { get; } = new();

                public MainWindow()
                {
                    DataContext = this;
                    InitializeComponent();
                    if (Environment.GetEnvironmentVariable(LiveValidationEnvironmentVariable) == "1")
                    {
                        Loaded += OnExternalLiveValidationLoaded;
                        StartExternalLiveValidationIfRequired();
                    }
                }

                public int ExternalTitleMouseDownCount { get; private set; }

                public int ExternalLiveMouseProbeDownCount { get; private set; }

                private void OnExternalLiveValidationLoaded(object sender, RoutedEventArgs e)
                {
                    StartExternalLiveValidationIfRequired();
                }

                private void StartExternalLiveValidationIfRequired()
                {
                    if (_externalLiveValidationStarted ||
                        Environment.GetEnvironmentVariable(LiveValidationEnvironmentVariable) != "1")
                    {
                        return;
                    }

                    _externalLiveValidationStarted = true;
                    _ = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await ValidateRequiredLiveExternalAsync().ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine(ex);
                                Environment.Exit(1);
                            }
                        });
                }

                private async Task ValidateRequiredLiveExternalAsync()
                {
                    for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
                    {
                        await Task.Delay(LiveValidationRetryDelay).ConfigureAwait(false);
                        if (!TryGetPortableActivationHost(out object? liveHost) ||
                            liveHost == null)
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
                        Console.WriteLine($"External SDK apphost live input validation succeeded: {geometryStatus}; {inputStatus}.");
                        Environment.Exit(0);
                        return;
                    }

                    Console.Error.WriteLine("Expected the external SDK apphost to present a stable ProGPU frame before live input validation.");
                    Environment.Exit(1);
                }

                private async Task<string> ValidateLiveInputAsync(object liveHost)
                {
                    FrameworkElement? mouseProbe = null;
                    TextBox? validationTextBox = null;
                    Point inputPoint = new();
                    object? inputHit = null;
                    string lastTargetState = "not checked";

                    bool sentPointerInput = false;
                    for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
                    {
                        sentPointerInput = await InvokeWithLiveHostWakeAsync(
                            liveHost,
                            () =>
                            {
                                mouseProbe = RequireType<FrameworkElement>(
                                    FindName("ExternalLiveMouseProbe"),
                                    "external SDK live mouse probe");
                                lastTargetState =
                                    $"ExternalLiveMouseProbe.IsVisible={mouseProbe.IsVisible}, " +
                                    $"ExternalLiveMouseProbe.ActualSize={mouseProbe.ActualWidth:0.###}x{mouseProbe.ActualHeight:0.###}, " +
                                    $"ExternalLiveMouseProbe.IsEnabled={mouseProbe.IsEnabled}, " +
                                    $"ExternalLiveMouseProbe.IsHitTestVisible={mouseProbe.IsHitTestVisible}";
                                if (!mouseProbe.IsVisible ||
                                    mouseProbe.ActualWidth <= 1.0 ||
                                    mouseProbe.ActualHeight <= 1.0 ||
                                    !mouseProbe.IsEnabled ||
                                    !mouseProbe.IsHitTestVisible)
                                {
                                    return false;
                                }

                                Point center = mouseProbe.TranslatePoint(
                                    new Point(Math.Max(1.0, mouseProbe.ActualWidth) / 2.0, Math.Max(1.0, mouseProbe.ActualHeight) / 2.0),
                                    this);
                                object? hit = InputHitTest(center);
                                lastTargetState += $", Input=({center.X:0.###}, {center.Y:0.###}), InputHitTest={DescribeInputElement(hit)}";
                                if (hit == null)
                                {
                                    return false;
                                }

                                inputPoint = center;
                                inputHit = hit;
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

                        await Task.Delay(LiveValidationRetryDelay).ConfigureAwait(false);
                    }

                    if (!sentPointerInput)
                    {
                        throw new InvalidOperationException(
                            $"Expected external SDK live mouse probe to become visible and hit-testable before injecting input, but last state was: {lastTargetState}.");
                    }

                    await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
                    await InvokeWithLiveHostWakeAsync(
                        liveHost,
                        () =>
                        {
                            AssertAtLeast(1, ExternalLiveMouseProbeDownCount, "external SDK live mouse probe down count");

                            validationTextBox = RequireType<TextBox>(
                                FindName("ExternalValidationTextBox"),
                                "external SDK live validation TextBox");
                            validationTextBox.Text = string.Empty;
                            validationTextBox.CaretIndex = 0;
                            validationTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                            Keyboard.Focus(validationTextBox);
                            if (!ReferenceEquals(Keyboard.FocusedElement, validationTextBox))
                            {
                                throw new InvalidOperationException(
                                    $"Expected external SDK live input setup to focus ExternalValidationTextBox, but focused '{DescribeInputElement(Keyboard.FocusedElement)}'. " +
                                    $"MouseInput=({inputPoint.X:0.###}, {inputPoint.Y:0.###}), " +
                                    $"MouseInputHitTest={DescribeInputElement(inputHit)}.");
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

                    int commandCountBefore = await InvokeWithLiveHostWakeAsync(
                        liveHost,
                        () =>
                        {
                            var liveValidationTextBox = RequireType<TextBox>(validationTextBox, "external SDK live validation TextBox");
                            AssertEqual("Live", liveValidationTextBox.Text, "external SDK live TextBox text after host text input");
                            liveValidationTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                            AssertEqual("Live", ValidationText, "external SDK live view-model source after host text input");

                            int before = ExternalCommandExecutedCount;
                            RaiseHostInput(liveHost, "KeyDown", key: "E", modifiers: "Control");
                            RaiseHostInput(liveHost, "KeyUp", key: "E", modifiers: "Control");
                            return before;
                        },
                        DispatcherPriority.Send);
                    await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

                    return await InvokeWithLiveHostWakeAsync(
                        liveHost,
                        () =>
                        {
                            AssertEqual(commandCountBefore + 1, ExternalCommandExecutedCount, "external SDK live Ctrl+E KeyBinding execution count");
                            AssertEqual(nameof(ExternalCommand), LastExternalCommandName, "external SDK live Ctrl+E KeyBinding command name");
                            return "mouse title click, TextBox text input, and Ctrl+E KeyBinding updated";
                        },
                        DispatcherPriority.Send);
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

                    AssertEqual(320u, logicalWidth, "external SDK live ProGPU WPF logical width");
                    AssertEqual(200u, logicalHeight, "external SDK live ProGPU WPF logical height");
                    if (pixelWidth < logicalWidth || pixelHeight < logicalHeight)
                    {
                        throw new InvalidOperationException(
                            $"Expected external SDK live ProGPU WPF pixels to cover logical content, but got logical {logicalWidth}x{logicalHeight} and pixels {pixelWidth}x{pixelHeight}.");
                    }

                    if (viewportX != 0 || viewportY != 0 || viewportWidth != pixelWidth || viewportHeight != pixelHeight)
                    {
                        throw new InvalidOperationException(
                            $"Expected external SDK live ProGPU WPF viewport to use the full physical target, but got viewport {viewportWidth}x{viewportHeight}@{viewportX},{viewportY} for pixels {pixelWidth}x{pixelHeight}.");
                    }

                    return $"logical {logicalWidth}x{logicalHeight}, pixels {pixelWidth}x{pixelHeight}, viewport {viewportWidth}x{viewportHeight}@{viewportX},{viewportY}, dpi {dpiScale:0.###}";
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

                private static void RaiseHostInput(
                    object liveHost,
                    string kind,
                    string? key = null,
                    char? character = null,
                    double x = 0.0,
                    double y = 0.0,
                    string button = "None",
                    string modifiers = "None")
                {
                    object input = CreateWpfInputEventArgs(liveHost, kind, key, character, x, y, button, modifiers);
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
                    string button,
                    string modifiers)
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
                        Enum.Parse(modifiersType, modifiers))
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

                private static T RequireType<T>(object? value, string description)
                {
                    return value is T typed
                        ? typed
                        : throw new InvalidOperationException($"Expected {description} to be {typeof(T).Name}, but found {value?.GetType().FullName ?? "<null>"}.");
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

                private static void AssertAtLeast(int minimum, int actual, string description)
                {
                    if (actual < minimum)
                    {
                        throw new InvalidOperationException(
                            $"Expected {description} to be at least {minimum}, but was {actual}.");
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

                public ObservableCollection<ExternalItem> ExternalIndexedItems { get; } =
                [
                    new ExternalItem("Indexed Alpha", "Binding", true),
                    new ExternalItem("Indexed Beta", "Binding", true)
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

                public string ExternalToolkitText { get; set; } = "external toolkit initial";

                public int? ExternalToolkitNumber { get; set; } = 4;

                public Color? ExternalToolkitAccentColor { get; set; } = Colors.SteelBlue;

                public decimal? ExternalToolkitEstimate { get; set; } = 12.50m;

                public bool ExternalToolkitIsBusy { get; set; }

                public string ExternalToolkitActionStatus { get; set; } = "external toolkit idle";

                public string ExternalToolkitRichText { get; set; } = "external toolkit rich initial";

                public string ExternalToolkitMultilineText { get; set; } = "external toolkit multiline initial";

                public int ExternalToolkitSpinnerCount { get; private set; } = 3;

                public int ExternalToolkitWizardPageChanges { get; private set; }

                public int ExternalToolkitWizardFinishes { get; private set; }

                public int ExternalToolkitWizardCancels { get; private set; }

                public string ExternalToolkitWizardStatus { get; private set; } = "external toolkit wizard idle";

                public Visibility ExternalToolkitWindowControlVisibility { get; set; } = Visibility.Visible;

                public string ExternalToolkitWindowControlText { get; set; } = "external toolkit window text";

                public int ExternalToolkitWindowControlActivatedCount { get; private set; }

                public int ExternalToolkitWindowControlHeaderClickCount { get; private set; }

                public int ExternalToolkitWindowControlHeaderDragCount { get; private set; }

                public int ExternalToolkitWindowControlCloseButtonClickCount { get; private set; }

                public string ExternalToolkitWindowControlStatus { get; private set; } = "external toolkit window visible";

                public string ValidationText { get; set; } = "valid external text";

                public string DataErrorText { get; set; } = "data: valid initial";

                public string Error => string.Empty;

                public string this[string columnName]
                {
                    get
                    {
                        if (string.Equals(columnName, nameof(DataErrorText), StringComparison.Ordinal)
                            && !DataErrorText.StartsWith("data:", StringComparison.Ordinal))
                        {
                            return "External IDataErrorInfo requires data: prefix.";
                        }

                        return string.Empty;
                    }
                }

                private string _notifyDataErrorText = "notify: valid initial";

                public string NotifyDataErrorText
                {
                    get => _notifyDataErrorText;
                    set
                    {
                        if (_notifyDataErrorText != value)
                        {
                            _notifyDataErrorText = value;
                            OnPropertyChanged(nameof(NotifyDataErrorText));
                            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(NotifyDataErrorText)));
                        }
                    }
                }

                public bool HasErrors => GetNotifyDataErrors(nameof(NotifyDataErrorText)).Any();

                public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

                public IEnumerable GetErrors(string? propertyName)
                {
                    return GetNotifyDataErrors(propertyName).ToArray();
                }

                private string _exceptionValidationText = "exception valid initial";

                public string ExceptionValidationText
                {
                    get => _exceptionValidationText;
                    set
                    {
                        if (string.Equals(value, "external exception trigger", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("External exception validation rejected value.");
                        }

                        if (_exceptionValidationText != value)
                        {
                            _exceptionValidationText = value;
                            OnPropertyChanged(nameof(ExceptionValidationText));
                        }
                    }
                }

                private string _exceptionFilterText = "filter valid initial";

                public string ExceptionFilterText
                {
                    get => _exceptionFilterText;
                    set
                    {
                        if (string.Equals(value, "external filter trigger", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("External exception filter rejected value.");
                        }

                        if (_exceptionFilterText != value)
                        {
                            _exceptionFilterText = value;
                            OnPropertyChanged(nameof(ExceptionFilterText));
                        }
                    }
                }

                public string ExternalBindingTransferText { get; set; } = "external transfer initial";

                public string ExternalDelayedBindingText { get; set; } = "external delayed initial";

                private string _externalOneTimeBindingValue = "external one-time initial";

                public string ExternalOneTimeBindingValue
                {
                    get => _externalOneTimeBindingValue;
                    set
                    {
                        if (_externalOneTimeBindingValue != value)
                        {
                            _externalOneTimeBindingValue = value;
                            OnPropertyChanged(nameof(ExternalOneTimeBindingValue));
                        }
                    }
                }

                public string ExternalOneWayToSourceBindingText { get; set; } = "external one-way source initial";

                public string? ExternalNullBindingText { get; } = null;

                public string BindingGroupFirstName { get; set; } = "group: Ada";

                public string BindingGroupLastName { get; set; } = "group: Lovelace";

                private string _externalDispatcherTimerStatus = "timer waiting";

                public string ExternalDispatcherTimerStatus
                {
                    get => _externalDispatcherTimerStatus;
                    private set
                    {
                        if (_externalDispatcherTimerStatus != value)
                        {
                            _externalDispatcherTimerStatus = value;
                            OnPropertyChanged(nameof(ExternalDispatcherTimerStatus));
                        }
                    }
                }

                private string _externalAsyncContinuationStatus = "async waiting";

                public string ExternalAsyncContinuationStatus
                {
                    get => _externalAsyncContinuationStatus;
                    private set
                    {
                        if (_externalAsyncContinuationStatus != value)
                        {
                            _externalAsyncContinuationStatus = value;
                            OnPropertyChanged(nameof(ExternalAsyncContinuationStatus));
                        }
                    }
                }

                private string _externalDispatcherInvokeAsyncStatus = "invoke async waiting";

                public string ExternalDispatcherInvokeAsyncStatus
                {
                    get => _externalDispatcherInvokeAsyncStatus;
                    private set
                    {
                        if (_externalDispatcherInvokeAsyncStatus != value)
                        {
                            _externalDispatcherInvokeAsyncStatus = value;
                            OnPropertyChanged(nameof(ExternalDispatcherInvokeAsyncStatus));
                        }
                    }
                }

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

                public int ExternalDispatcherTimerTickCount { get; private set; }

                public int ExternalAsyncContinuationCount { get; private set; }

                public int ExternalDispatcherInvokeAsyncCount { get; private set; }

                public string? LastExternalSelectionSourceName { get; private set; }

                public int ExternalMultiSelectionChangedCount { get; private set; }

                public string? LastExternalMultiSelectionSourceName { get; private set; }

                public int LastExternalMultiSelectionAddedCount { get; private set; }

                public int LastExternalMultiSelectionRemovedCount { get; private set; }

                public string? LastExternalMultiSelectionAddedName { get; private set; }

                public string? LastExternalMultiSelectionRemovedName { get; private set; }

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

                public int ExternalUpdateSourceExceptionFilterCount { get; private set; }

                public string? LastExternalUpdateSourceExceptionFilterMessage { get; private set; }

                public string? LastExternalUpdateSourceExceptionFilterPath { get; private set; }

                public int ExternalSliderValueChangedCount { get; private set; }

                public double LastExternalSliderValue { get; private set; }

                public int ExternalRepeatButtonClickCount { get; private set; }

                public string? LastExternalRepeatButtonClickSenderName { get; private set; }

                public string? LastExternalRepeatButtonClickRoutedEventName { get; private set; }

                public int ExternalScrollBarScrollCount { get; private set; }

                public string? LastExternalScrollBarSenderName { get; private set; }

                public string? LastExternalScrollBarEventType { get; private set; }

                public double LastExternalScrollBarNewValue { get; private set; }

                public int ExternalThumbDragStartedCount { get; private set; }

                public int ExternalThumbDragDeltaCount { get; private set; }

                public int ExternalThumbDragCompletedCount { get; private set; }

                public int ExternalBubbledThumbDragDeltaCount { get; private set; }

                public string? LastExternalThumbDragStartedSenderName { get; private set; }

                public string? LastExternalThumbDragDeltaSenderName { get; private set; }

                public string? LastExternalThumbDragCompletedSenderName { get; private set; }

                public string? LastExternalBubbledThumbDragDeltaSenderName { get; private set; }

                public string? LastExternalBubbledThumbDragDeltaOriginalSourceName { get; private set; }

                public string? LastExternalThumbDragStartedRoutedEventName { get; private set; }

                public string? LastExternalThumbDragDeltaRoutedEventName { get; private set; }

                public string? LastExternalThumbDragCompletedRoutedEventName { get; private set; }

                public string? LastExternalBubbledThumbDragDeltaRoutedEventName { get; private set; }

                public double LastExternalThumbDragStartedHorizontalOffset { get; private set; }

                public double LastExternalThumbDragStartedVerticalOffset { get; private set; }

                public double LastExternalThumbDragDeltaHorizontalChange { get; private set; }

                public double LastExternalThumbDragDeltaVerticalChange { get; private set; }

                public double LastExternalThumbDragCompletedHorizontalChange { get; private set; }

                public double LastExternalThumbDragCompletedVerticalChange { get; private set; }

                public bool LastExternalThumbDragCompletedCanceled { get; private set; }

                public double LastExternalBubbledThumbDragDeltaHorizontalChange { get; private set; }

                public double LastExternalBubbledThumbDragDeltaVerticalChange { get; private set; }

                public string? LastExternalCheckBoxRoutedEventName { get; private set; }

                public string? LastExternalRadioButtonCheckedName { get; private set; }

                public string? LastExternalRadioButtonUncheckedName { get; private set; }

                public string? LastExternalToggleButtonRoutedEventName { get; private set; }

                public int ExternalCommandCanExecuteCount { get; private set; }

                public int ExternalCommandExecutedCount { get; private set; }

                public int ExternalCommandButtonClickCount { get; private set; }

                public int ExternalSystemCommandCanExecuteCount { get; private set; }

                public int ExternalSystemCommandExecutedCount { get; private set; }

                public string? LastExternalSystemCommandName { get; private set; }

                public object? LastExternalSystemCommandParameter { get; private set; }

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

                public int ExternalFrameNavigationCanceledCount { get; private set; }

                public string? LastExternalFrameNavigatingUri { get; private set; }

                public string? LastExternalFrameNavigatedUri { get; private set; }

                public string? LastExternalFrameLoadCompletedUri { get; private set; }

                public string? LastExternalFrameNavigationMode { get; private set; }

                public string? LastExternalFrameContentType { get; private set; }

                public string? LastExternalFrameCanceledUri { get; private set; }

                public string? LastExternalFrameCanceledNavigationMode { get; private set; }

                public int ExternalPageFunctionReturnCount { get; private set; }

                public string? LastExternalPageFunctionResult { get; private set; }

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

                private void OnExternalMultiSelectionChanged(object sender, SelectionChangedEventArgs e)
                {
                    ExternalMultiSelectionChangedCount++;
                    LastExternalMultiSelectionSourceName = (sender as FrameworkElement)?.Name;
                    LastExternalMultiSelectionAddedCount = e.AddedItems.Count;
                    LastExternalMultiSelectionRemovedCount = e.RemovedItems.Count;
                    LastExternalMultiSelectionAddedName = e.AddedItems.Count > 0 ? GetExternalItemName(e.AddedItems[0]) : null;
                    LastExternalMultiSelectionRemovedName = e.RemovedItems.Count > 0 ? GetExternalItemName(e.RemovedItems[0]) : null;
                }

                private static string GetExternalItemName(object item)
                {
                    return item is ExternalItem externalItem
                        ? externalItem.Name
                        : item.ToString() ?? string.Empty;
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

                private void OnExternalTitleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
                {
                    ExternalTitleMouseDownCount++;
                    e.Handled = true;
                }

                private void OnExternalLiveMouseProbeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
                {
                    ExternalLiveMouseProbeDownCount++;
                    e.Handled = true;
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

                private object OnExternalUpdateSourceExceptionFilter(object bindExpression, Exception exception)
                {
                    ExternalUpdateSourceExceptionFilterCount++;
                    LastExternalUpdateSourceExceptionFilterMessage = exception.Message;
                    LastExternalUpdateSourceExceptionFilterPath = bindExpression is BindingExpression bindingExpression
                        ? bindingExpression.ParentBinding.Path.Path
                        : null;
                    return "External filtered exception: " + exception.Message;
                }

                private void OnExternalSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
                {
                    ExternalSliderValueChangedCount++;
                    LastExternalSliderValue = e.NewValue;
                }

                private void OnExternalRepeatButtonClick(object sender, RoutedEventArgs e)
                {
                    ExternalRepeatButtonClickCount++;
                    LastExternalRepeatButtonClickSenderName = (sender as FrameworkElement)?.Name;
                    LastExternalRepeatButtonClickRoutedEventName = e.RoutedEvent?.Name;
                }

                private void OnExternalScrollBarScroll(object sender, ScrollEventArgs e)
                {
                    ExternalScrollBarScrollCount++;
                    LastExternalScrollBarSenderName = (sender as FrameworkElement)?.Name;
                    LastExternalScrollBarEventType = e.ScrollEventType.ToString();
                    LastExternalScrollBarNewValue = e.NewValue;
                }

                private void OnExternalThumbDragStarted(object sender, DragStartedEventArgs e)
                {
                    ExternalThumbDragStartedCount++;
                    LastExternalThumbDragStartedSenderName = (sender as FrameworkElement)?.Name;
                    LastExternalThumbDragStartedRoutedEventName = e.RoutedEvent?.Name;
                    LastExternalThumbDragStartedHorizontalOffset = e.HorizontalOffset;
                    LastExternalThumbDragStartedVerticalOffset = e.VerticalOffset;
                }

                private void OnExternalThumbDragDelta(object sender, DragDeltaEventArgs e)
                {
                    ExternalThumbDragDeltaCount++;
                    LastExternalThumbDragDeltaSenderName = (sender as FrameworkElement)?.Name;
                    LastExternalThumbDragDeltaRoutedEventName = e.RoutedEvent?.Name;
                    LastExternalThumbDragDeltaHorizontalChange = e.HorizontalChange;
                    LastExternalThumbDragDeltaVerticalChange = e.VerticalChange;
                }

                private void OnExternalThumbDragCompleted(object sender, DragCompletedEventArgs e)
                {
                    ExternalThumbDragCompletedCount++;
                    LastExternalThumbDragCompletedSenderName = (sender as FrameworkElement)?.Name;
                    LastExternalThumbDragCompletedRoutedEventName = e.RoutedEvent?.Name;
                    LastExternalThumbDragCompletedHorizontalChange = e.HorizontalChange;
                    LastExternalThumbDragCompletedVerticalChange = e.VerticalChange;
                    LastExternalThumbDragCompletedCanceled = e.Canceled;
                }

                private void OnExternalBubbledThumbDragDelta(object sender, DragDeltaEventArgs e)
                {
                    ExternalBubbledThumbDragDeltaCount++;
                    LastExternalBubbledThumbDragDeltaSenderName = (sender as FrameworkElement)?.Name;
                    LastExternalBubbledThumbDragDeltaOriginalSourceName = e.OriginalSource is FrameworkElement source ? source.Name : null;
                    LastExternalBubbledThumbDragDeltaRoutedEventName = e.RoutedEvent?.Name;
                    LastExternalBubbledThumbDragDeltaHorizontalChange = e.HorizontalChange;
                    LastExternalBubbledThumbDragDeltaVerticalChange = e.VerticalChange;
                }

                private void OnExternalFrameNavigating(object sender, NavigatingCancelEventArgs e)
                {
                    ExternalFrameNavigatingCount++;
                    LastExternalFrameNavigatingUri = e.Uri?.ToString();
                    LastExternalFrameNavigationMode = e.NavigationMode.ToString();
                    if (LastExternalFrameNavigatingUri?.EndsWith("ExternalBlockedPage.xaml", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        e.Cancel = true;
                        ExternalFrameNavigationCanceledCount++;
                        LastExternalFrameCanceledUri = LastExternalFrameNavigatingUri;
                        LastExternalFrameCanceledNavigationMode = LastExternalFrameNavigationMode;
                    }
                }

                private void OnExternalFrameNavigated(object sender, NavigationEventArgs e)
                {
                    ExternalFrameNavigatedCount++;
                    LastExternalFrameNavigatedUri = e.Uri?.ToString();
                    LastExternalFrameContentType = e.Content?.GetType().FullName;
                    if (e.Content is ExternalPageFunction pageFunction)
                    {
                        pageFunction.Return -= OnExternalPageFunctionReturn;
                        pageFunction.Return += OnExternalPageFunctionReturn;
                    }
                }

                private void OnExternalFrameLoadCompleted(object sender, NavigationEventArgs e)
                {
                    ExternalFrameLoadCompletedCount++;
                    LastExternalFrameLoadCompletedUri = e.Uri?.ToString();
                }

                private void OnExternalPageFunctionReturn(object sender, ReturnEventArgs<string> e)
                {
                    ExternalPageFunctionReturnCount++;
                    LastExternalPageFunctionResult = e.Result;
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

                private void OnExternalSystemCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
                {
                    ExternalSystemCommandCanExecuteCount++;
                    e.CanExecute = true;
                    e.Handled = true;
                }

                private void OnExternalSystemCommandExecuted(object sender, ExecutedRoutedEventArgs e)
                {
                    ExternalSystemCommandExecutedCount++;
                    LastExternalSystemCommandParameter = e.Parameter;
                    LastExternalSystemCommandName = (e.Command as RoutedCommand)?.Name;

                    if (ReferenceEquals(e.Command, SystemCommands.MaximizeWindowCommand))
                    {
                        SystemCommands.MaximizeWindow(this);
                    }
                    else if (ReferenceEquals(e.Command, SystemCommands.MinimizeWindowCommand))
                    {
                        SystemCommands.MinimizeWindow(this);
                    }
                    else if (ReferenceEquals(e.Command, SystemCommands.RestoreWindowCommand))
                    {
                        SystemCommands.RestoreWindow(this);
                    }
                    else if (ReferenceEquals(e.Command, SystemCommands.ShowSystemMenuCommand))
                    {
                        SystemCommands.ShowSystemMenu(this, new Point(12.0, 24.0));
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unexpected external SDK system command '{e.Command}'.");
                    }

                    e.Handled = true;
                }

                private void OnExternalCommandButtonClick(object sender, RoutedEventArgs e)
                {
                    ExternalCommandButtonClickCount++;
                }

                private void OnExternalToolkitDropDownActionClick(object sender, RoutedEventArgs e)
                {
                    ExternalToolkitActionStatus = "external toolkit dropdown action";
                    ExternalToolkitDropDownButton.IsOpen = false;
                }

                private void OnExternalToolkitSplitButtonClick(object sender, RoutedEventArgs e)
                {
                    ExternalToolkitActionStatus = "external toolkit split primary action";
                }

                private void OnExternalToolkitSplitDropDownActionClick(object sender, RoutedEventArgs e)
                {
                    ExternalToolkitActionStatus = "external toolkit split dropdown action";
                    ExternalToolkitSplitButton.IsOpen = false;
                }

                private void OnExternalToolkitButtonSpinnerSpin(object sender, Xceed.Wpf.Toolkit.SpinEventArgs e)
                {
                    ApplyExternalToolkitSpinnerDelta(e.Direction == Xceed.Wpf.Toolkit.SpinDirection.Increase ? 1 : -1);
                }

                internal void ApplyExternalToolkitSpinnerDelta(int delta)
                {
                    ExternalToolkitSpinnerCount += delta;
                    OnPropertyChanged(nameof(ExternalToolkitSpinnerCount));
                }

                private void OnExternalToolkitWizardPageChanged(object sender, RoutedEventArgs e)
                {
                    ExternalToolkitWizardPageChanges++;
                    ExternalToolkitWizardStatus = ExternalToolkitWizard.CurrentPage?.Title ?? "external toolkit wizard no page";
                    OnPropertyChanged(nameof(ExternalToolkitWizardStatus));
                }

                private void OnExternalToolkitWizardFinish(object sender, Xceed.Wpf.Toolkit.Core.CancelRoutedEventArgs e)
                {
                    ExternalToolkitWizardFinishes++;
                    ExternalToolkitWizardStatus = "external toolkit wizard finished";
                    OnPropertyChanged(nameof(ExternalToolkitWizardStatus));
                }

                private void OnExternalToolkitWizardCancel(object sender, RoutedEventArgs e)
                {
                    ExternalToolkitWizardCancels++;
                    ExternalToolkitWizardStatus = "external toolkit wizard canceled";
                    OnPropertyChanged(nameof(ExternalToolkitWizardStatus));
                }

                private void OnExternalToolkitWindowControlActivated(object sender, RoutedEventArgs e)
                {
                    ExternalToolkitWindowControlActivatedCount++;
                    ExternalToolkitWindowControlStatus = "external toolkit window activated";
                    OnPropertyChanged(nameof(ExternalToolkitWindowControlStatus));
                }

                private void OnExternalToolkitWindowControlHeaderMouseLeftButtonClicked(object sender, MouseButtonEventArgs e)
                {
                    ExternalToolkitWindowControlHeaderClickCount++;
                    ExternalToolkitWindowControlStatus = "external toolkit window header clicked";
                    OnPropertyChanged(nameof(ExternalToolkitWindowControlStatus));
                }

                private void OnExternalToolkitWindowControlHeaderDragDelta(object sender, DragDeltaEventArgs e)
                {
                    ExternalToolkitWindowControlHeaderDragCount++;
                    ExternalToolkitWindowControlStatus = "external toolkit window header dragged";
                    OnPropertyChanged(nameof(ExternalToolkitWindowControlStatus));
                }

                private void OnExternalToolkitWindowControlCloseButtonClicked(object sender, RoutedEventArgs e)
                {
                    ExternalToolkitWindowControlCloseButtonClickCount++;
                    HideExternalToolkitWindowControl("external toolkit window closed");
                }

                internal void ShowExternalToolkitWindowControl()
                {
                    ExternalToolkitWindowControlVisibility = Visibility.Visible;
                    ExternalToolkitWindowControl.SetCurrentValue(VisibilityProperty, Visibility.Visible);
                    ExternalToolkitWindowControlStatus = "external toolkit window visible";
                    OnPropertyChanged(nameof(ExternalToolkitWindowControlVisibility));
                    OnPropertyChanged(nameof(ExternalToolkitWindowControlStatus));
                }

                internal void HideExternalToolkitWindowControl(string status)
                {
                    ExternalToolkitWindowControlVisibility = Visibility.Collapsed;
                    ExternalToolkitWindowControl.SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
                    ExternalToolkitWindowControlStatus = status;
                    OnPropertyChanged(nameof(ExternalToolkitWindowControlVisibility));
                    OnPropertyChanged(nameof(ExternalToolkitWindowControlStatus));
                }

                internal void ActivateExternalToolkitWindowControl()
                {
                    if (ExternalToolkitWindowControl.Visibility != Visibility.Visible)
                    {
                        ShowExternalToolkitWindowControl();
                    }

                    ExternalToolkitWindowControl.IsActive = false;
                    ExternalToolkitWindowControl.IsActive = true;
                    ExternalToolkitWindowControl.Focus();
                    ExternalToolkitWindowControlInputTextBox.Focus();
                }

                internal void RaiseExternalToolkitWindowControlHeaderClick()
                {
                    RaiseExternalToolkitWindowControlMouseEvent(
                        Xceed.Wpf.Toolkit.Primitives.WindowControl.HeaderMouseLeftButtonClickedEvent,
                        MouseButton.Left);
                }

                internal void RaiseExternalToolkitWindowControlHeaderDrag()
                {
                    var args = new DragDeltaEventArgs(6.0, 3.0)
                    {
                        RoutedEvent = Xceed.Wpf.Toolkit.Primitives.WindowControl.HeaderDragDeltaEvent,
                        Source = ExternalToolkitWindowControl,
                    };
                    ExternalToolkitWindowControl.RaiseEvent(args);
                }

                internal Button GetExternalToolkitWindowControlButton(string partName)
                {
                    ExternalToolkitWindowControl.ApplyTemplate();
                    ExternalToolkitWindowControl.UpdateLayout();
                    return ExternalToolkitWindowControl.Template?.FindName(partName, ExternalToolkitWindowControl) as Button
                        ?? throw new InvalidOperationException($"Expected external SDK Xceed WindowControl template button '{partName}'.");
                }

                private void RaiseExternalToolkitWindowControlMouseEvent(RoutedEvent routedEvent, MouseButton mouseButton)
                {
                    var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, mouseButton)
                    {
                        RoutedEvent = routedEvent,
                        Source = ExternalToolkitWindowControl,
                    };
                    ExternalToolkitWindowControl.RaiseEvent(args);
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

                private void OnExternalWindowLoaded(object sender, RoutedEventArgs e)
                {
                    if (_externalDispatcherTimer is not null)
                    {
                        return;
                    }

                    _externalDispatcherTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                    {
                        Interval = TimeSpan.FromMilliseconds(1)
                    };
                    _externalDispatcherTimer.Tick += OnExternalDispatcherTimerTick;
                    _externalDispatcherTimer.Start();
                    _ = RunExternalAsyncContinuationAsync();
                    _ = RunExternalDispatcherInvokeAsync();
                }

                private void OnExternalDispatcherTimerTick(object? sender, EventArgs e)
                {
                    _externalDispatcherTimer?.Stop();
                    ExternalDispatcherTimerTickCount++;
                    ExternalDispatcherTimerStatus = $"timer tick {ExternalDispatcherTimerTickCount}";
                }

                private async Task RunExternalAsyncContinuationAsync()
                {
                    await Task.Yield();
                    ExternalAsyncContinuationCount++;
                    ExternalAsyncContinuationStatus = Dispatcher.CheckAccess()
                        ? $"async dispatcher continuation {ExternalAsyncContinuationCount}"
                        : "async continuation left dispatcher";
                }

                private async Task RunExternalDispatcherInvokeAsync()
                {
                    DispatcherOperation<string> operation = Dispatcher.InvokeAsync(
                        () =>
                        {
                            ExternalDispatcherInvokeAsyncCount++;
                            return Dispatcher.CheckAccess()
                                ? $"invoke async dispatcher {ExternalDispatcherInvokeAsyncCount}"
                                : "invoke async left dispatcher";
                        },
                        DispatcherPriority.Background);
                    ExternalDispatcherInvokeAsyncStatus = await operation.Task;
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

                private IEnumerable<string> GetNotifyDataErrors(string? propertyName)
                {
                    if (propertyName is null
                        || string.Equals(propertyName, nameof(NotifyDataErrorText), StringComparison.Ordinal))
                    {
                        if (!NotifyDataErrorText.StartsWith("notify:", StringComparison.Ordinal))
                        {
                            yield return "External INotifyDataErrorInfo requires notify: prefix.";
                        }
                    }
                }

                private bool _isExternalDataTriggerActive;

                private bool _isExternalMultiTriggerReady;

                private bool _isExternalDataTriggerActionActive;

                private bool _isExternalMultiDataTriggerActionReady;

                private bool _isExternalMultiDataTriggerActionArmed;

                private DispatcherTimer? _externalDispatcherTimer;
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

            public sealed class ExternalItemContainerStyleSelector : StyleSelector
            {
                public Style? FrameworkStyle { get; set; }

                public Style? DefaultStyle { get; set; }

                public override Style? SelectStyle(object item, DependencyObject container)
                {
                    if (item is ExternalItem { Kind: "Framework" } && FrameworkStyle is not null)
                    {
                        return FrameworkStyle;
                    }

                    return DefaultStyle ?? base.SelectStyle(item, container);
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

            public sealed class ExternalClassCommandTextBox : TextBox
            {
                public static readonly RoutedUICommand ExternalClassCommand = new(
                    "External class command",
                    nameof(ExternalClassCommand),
                    typeof(ExternalClassCommandTextBox));

                static ExternalClassCommandTextBox()
                {
                    CommandManager.RegisterClassCommandBinding(
                        typeof(ExternalClassCommandTextBox),
                        new CommandBinding(
                            ExternalClassCommand,
                            OnExternalClassCommandExecuted,
                            OnExternalClassCommandCanExecute));
                    CommandManager.RegisterClassInputBinding(
                        typeof(ExternalClassCommandTextBox),
                        new KeyBinding(
                            ExternalClassCommand,
                            new KeyGesture(Key.F8, ModifierKeys.Control))
                        {
                            CommandParameter = "ExternalClassInputParameter"
                        });
                }

                public int ClassCommandCanExecuteCount { get; private set; }

                public int ClassCommandExecutedCount { get; private set; }

                public object? LastClassCommandParameter { get; private set; }

                public string? LastClassCommandName { get; private set; }

                private static void OnExternalClassCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
                {
                    if (sender is ExternalClassCommandTextBox textBox)
                    {
                        textBox.ClassCommandCanExecuteCount++;
                        e.CanExecute = true;
                        e.Handled = true;
                    }
                }

                private static void OnExternalClassCommandExecuted(object sender, ExecutedRoutedEventArgs e)
                {
                    if (sender is ExternalClassCommandTextBox textBox)
                    {
                        textBox.ClassCommandExecutedCount++;
                        textBox.LastClassCommandParameter = e.Parameter;
                        textBox.LastClassCommandName = (e.Command as RoutedCommand)?.Name;
                        e.Handled = true;
                    }
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

                public static string[] ExternalStartupArguments { get; private set; } = [];

                public static int ExternalExitEventCount { get; private set; }

                public static int ExternalExitCode { get; private set; }

                public static bool ExternalRunValidated { get; private set; }

                public static bool ExternalRunValidationRequested => s_externalRunValidationRequested;

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
                            DispatcherPriority.Normal,
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
                    ExternalStartupArguments = e.Args;
                    Properties["ExternalStartupArgumentCount"] = e.Args.Length;
                    Properties["ExternalStartupFirstArgument"] = e.Args.Length > 0 ? e.Args[0] : string.Empty;
                    Properties["ExternalStartupState"] = "External SDK startup state";
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
                    ValidateRuntimeResourceReference(window);
                    ValidateRuntimeNameScope(window);
                    ValidateApplicationLoadComponent();
                    ValidatePackResources();
                    ValidateAppConfiguration();
                    ValidateSplashScreen();
                    ValidateSystemParameters(window);
                    ValidateWindowChrome(window);
                    ValidateSystemCommands(window);
                    ValidateLauncher();
                    ValidateMessageBox(window);
                    ValidateFileDialogs(window);
                    ValidatePrintDialog();
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
                    ValidateRibbonControls(window);
                    ValidateToolbarStatusRangePasswordDateControls(window);
                    ValidateAdornerDecorator(window);
                    ValidateLayoutsAndItems(window);
                    ValidateSelectorsAndContent(window);
                    ValidateRichDocuments(window);
                    ValidateSpellCheck(window);
                    ValidateCommandsAndFocus(window);
                    ValidateThumbDragManager(window);
                    ValidateXceedToolkitAndAvalonDock(window, expectLoaded: false);

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
                    ValidateThemeTemplateXamlWriterRoundTrip(window, themedControl);

                    var frame = RequireType<Frame>(
                        window.FindName("ExternalFrame"),
                        "external SDK compiled page frame");
                    var navigationBackButton = RequireType<Button>(
                        window.FindName("ExternalNavigationBackButton"),
                        "external SDK navigation back command button");
                    var navigationForwardButton = RequireType<Button>(
                        window.FindName("ExternalNavigationForwardButton"),
                        "external SDK navigation forward command button");
                    AssertEqual(NavigationCommands.BrowseBack, navigationBackButton.Command, "external SDK navigation back button command");
                    AssertEqual(frame, navigationBackButton.CommandTarget, "external SDK navigation back button target");
                    AssertEqual(NavigationCommands.BrowseForward, navigationForwardButton.Command, "external SDK navigation forward button command");
                    AssertEqual(frame, navigationForwardButton.CommandTarget, "external SDK navigation forward button target");
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
                    AssertEqual(true, frame.CanGoForward, "external SDK frame can go forward");

                    int navigatingCountBeforeForward = window.ExternalFrameNavigatingCount;
                    int navigatedCountBeforeForward = window.ExternalFrameNavigatedCount;
                    int loadCompletedCountBeforeForward = window.ExternalFrameLoadCompletedCount;
                    frame.GoForward();
                    DrainDispatcher();

                    RequireType<ExternalSecondPage>(
                        frame.Content,
                        "external SDK forwarded compiled page");
                    AssertAtLeast(navigatingCountBeforeForward + 1, window.ExternalFrameNavigatingCount, "external SDK forward frame navigating count");
                    AssertAtLeast(navigatedCountBeforeForward + 1, window.ExternalFrameNavigatedCount, "external SDK forward frame navigated count");
                    AssertAtLeast(loadCompletedCountBeforeForward + 1, window.ExternalFrameLoadCompletedCount, "external SDK forward frame load completed count");
                    AssertEqual("Forward", window.LastExternalFrameNavigationMode, "external SDK forward frame navigation mode");
                    AssertEqual(typeof(ExternalSecondPage).FullName, window.LastExternalFrameContentType, "external SDK forward frame content type");
                    AssertEqual(true, frame.CanGoBack, "external SDK frame can go back after forward");
                    AssertEqual(false, frame.CanGoForward, "external SDK frame cannot go forward after forward");

                    int navigatingCountBeforeCommandBack = window.ExternalFrameNavigatingCount;
                    int navigatedCountBeforeCommandBack = window.ExternalFrameNavigatedCount;
                    AssertEqual(true, NavigationCommands.BrowseBack.CanExecute(null, frame), "external SDK navigation command back can execute");
                    NavigationCommands.BrowseBack.Execute(null, frame);
                    DrainDispatcher();

                    RequireType<ExternalPage>(
                        frame.Content,
                        "external SDK command returned compiled page");
                    AssertAtLeast(navigatingCountBeforeCommandBack + 1, window.ExternalFrameNavigatingCount, "external SDK command back frame navigating count");
                    AssertAtLeast(navigatedCountBeforeCommandBack + 1, window.ExternalFrameNavigatedCount, "external SDK command back frame navigated count");
                    AssertEqual("Back", window.LastExternalFrameNavigationMode, "external SDK command back frame navigation mode");
                    AssertEqual(typeof(ExternalPage).FullName, window.LastExternalFrameContentType, "external SDK command back frame content type");
                    AssertEqual(true, frame.CanGoForward, "external SDK frame can go forward after navigation command back");

                    int navigatingCountBeforeCommandForward = window.ExternalFrameNavigatingCount;
                    int navigatedCountBeforeCommandForward = window.ExternalFrameNavigatedCount;
                    int loadCompletedCountBeforeCommandForward = window.ExternalFrameLoadCompletedCount;
                    AssertEqual(true, NavigationCommands.BrowseForward.CanExecute(null, frame), "external SDK navigation command forward can execute");
                    NavigationCommands.BrowseForward.Execute(null, frame);
                    DrainDispatcher();

                    RequireType<ExternalSecondPage>(
                        frame.Content,
                        "external SDK command forwarded compiled page");
                    AssertAtLeast(navigatingCountBeforeCommandForward + 1, window.ExternalFrameNavigatingCount, "external SDK command forward frame navigating count");
                    AssertAtLeast(navigatedCountBeforeCommandForward + 1, window.ExternalFrameNavigatedCount, "external SDK command forward frame navigated count");
                    AssertAtLeast(loadCompletedCountBeforeCommandForward + 1, window.ExternalFrameLoadCompletedCount, "external SDK command forward frame load completed count");
                    AssertEqual("Forward", window.LastExternalFrameNavigationMode, "external SDK command forward frame navigation mode");
                    AssertEqual(typeof(ExternalSecondPage).FullName, window.LastExternalFrameContentType, "external SDK command forward frame content type");
                    AssertEqual(true, frame.CanGoBack, "external SDK frame can go back after navigation command forward");
                    AssertEqual(false, frame.CanGoForward, "external SDK frame cannot go forward after navigation command forward");

                    var frameNavigationService = frame.NavigationService
                        ?? throw new InvalidOperationException("Expected external SDK Frame NavigationService.");
                    var frameNavigationServiceSecondPage = RequireType<ExternalSecondPage>(
                        frame.Content,
                        "external SDK NavigationService second page content");
                    AssertEqual(frameNavigationService, frameNavigationServiceSecondPage.NavigationService, "external SDK page NavigationService property");
                    AssertEqual(frameNavigationService, NavigationService.GetNavigationService(frameNavigationServiceSecondPage), "external SDK page NavigationService lookup");
                    AssertEqual(true, frameNavigationService.CanGoBack, "external SDK Frame NavigationService can go back");
                    AssertEqual(false, frameNavigationService.CanGoForward, "external SDK Frame NavigationService cannot go forward before service back");

                    int navigatingCountBeforeServiceBack = window.ExternalFrameNavigatingCount;
                    int navigatedCountBeforeServiceBack = window.ExternalFrameNavigatedCount;
                    frameNavigationService.GoBack();
                    DrainDispatcher();

                    var frameNavigationServiceReturnedPage = RequireType<ExternalPage>(
                        frame.Content,
                        "external SDK NavigationService returned compiled page");
                    AssertEqual(frameNavigationService, frameNavigationServiceReturnedPage.NavigationService, "external SDK returned page NavigationService property");
                    AssertEqual(frameNavigationService, NavigationService.GetNavigationService(frameNavigationServiceReturnedPage), "external SDK returned page NavigationService lookup");
                    AssertAtLeast(navigatingCountBeforeServiceBack + 1, window.ExternalFrameNavigatingCount, "external SDK NavigationService back frame navigating count");
                    AssertAtLeast(navigatedCountBeforeServiceBack + 1, window.ExternalFrameNavigatedCount, "external SDK NavigationService back frame navigated count");
                    AssertEqual("Back", window.LastExternalFrameNavigationMode, "external SDK NavigationService back frame navigation mode");
                    AssertEqual(typeof(ExternalPage).FullName, window.LastExternalFrameContentType, "external SDK NavigationService back frame content type");
                    AssertEqual(true, frameNavigationService.CanGoForward, "external SDK Frame NavigationService can go forward after service back");

                    int navigatingCountBeforeServiceForward = window.ExternalFrameNavigatingCount;
                    int navigatedCountBeforeServiceForward = window.ExternalFrameNavigatedCount;
                    int loadCompletedCountBeforeServiceForward = window.ExternalFrameLoadCompletedCount;
                    frameNavigationService.GoForward();
                    DrainDispatcher();

                    var frameNavigationServiceForwardedPage = RequireType<ExternalSecondPage>(
                        frame.Content,
                        "external SDK NavigationService forwarded compiled page");
                    AssertEqual(frameNavigationService, frameNavigationServiceForwardedPage.NavigationService, "external SDK forwarded page NavigationService property");
                    AssertEqual(frameNavigationService, NavigationService.GetNavigationService(frameNavigationServiceForwardedPage), "external SDK forwarded page NavigationService lookup");
                    AssertAtLeast(navigatingCountBeforeServiceForward + 1, window.ExternalFrameNavigatingCount, "external SDK NavigationService forward frame navigating count");
                    AssertAtLeast(navigatedCountBeforeServiceForward + 1, window.ExternalFrameNavigatedCount, "external SDK NavigationService forward frame navigated count");
                    AssertAtLeast(loadCompletedCountBeforeServiceForward + 1, window.ExternalFrameLoadCompletedCount, "external SDK NavigationService forward frame load completed count");
                    AssertEqual("Forward", window.LastExternalFrameNavigationMode, "external SDK NavigationService forward frame navigation mode");
                    AssertEqual(typeof(ExternalSecondPage).FullName, window.LastExternalFrameContentType, "external SDK NavigationService forward frame content type");

                    int navigatingCountBeforeCanceled = window.ExternalFrameNavigatingCount;
                    int navigatedCountBeforeCanceled = window.ExternalFrameNavigatedCount;
                    int loadCompletedCountBeforeCanceled = window.ExternalFrameLoadCompletedCount;
                    int canceledCountBeforeCanceled = window.ExternalFrameNavigationCanceledCount;
                    frame.Navigate(new Uri("ExternalBlockedPage.xaml", UriKind.Relative));
                    DrainDispatcher();

                    RequireType<ExternalSecondPage>(
                        frame.Content,
                        "external SDK canceled navigation retained page");
                    AssertAtLeast(navigatingCountBeforeCanceled + 1, window.ExternalFrameNavigatingCount, "external SDK canceled frame navigating count");
                    AssertEqual(navigatedCountBeforeCanceled, window.ExternalFrameNavigatedCount, "external SDK canceled frame navigated count");
                    AssertEqual(loadCompletedCountBeforeCanceled, window.ExternalFrameLoadCompletedCount, "external SDK canceled frame load completed count");
                    AssertEqual(canceledCountBeforeCanceled + 1, window.ExternalFrameNavigationCanceledCount, "external SDK canceled frame count");
                    AssertEndsWith(window.LastExternalFrameNavigatingUri, "ExternalBlockedPage.xaml", "external SDK canceled frame navigating URI");
                    AssertEndsWith(window.LastExternalFrameCanceledUri, "ExternalBlockedPage.xaml", "external SDK canceled frame canceled URI");
                    AssertEqual("New", window.LastExternalFrameCanceledNavigationMode, "external SDK canceled frame navigation mode");
                    AssertEqual(typeof(ExternalSecondPage).FullName, window.LastExternalFrameContentType, "external SDK canceled frame retained content type");
                    AssertEqual(true, frame.CanGoBack, "external SDK frame can go back after canceled navigation");
                    AssertEqual(false, frame.CanGoForward, "external SDK frame cannot go forward after canceled navigation");

                    int navigatingCountBeforePageFunction = window.ExternalFrameNavigatingCount;
                    int navigatedCountBeforePageFunction = window.ExternalFrameNavigatedCount;
                    int loadCompletedCountBeforePageFunction = window.ExternalFrameLoadCompletedCount;
                    AssertEqual(true, frame.Navigate(new Uri("ExternalPageFunction.xaml", UriKind.Relative)), "external SDK PageFunction navigate result");
                    DrainDispatcher();

                    var pageFunction = RequireType<ExternalPageFunction>(
                        frame.Content,
                        "external SDK compiled PageFunction");
                    var pageFunctionTitle = RequireType<TextBlock>(
                        pageFunction.FindName("ExternalPageFunctionTitle"),
                        "external SDK compiled PageFunction title");
                    var pageFunctionSubtitle = RequireType<TextBlock>(
                        pageFunction.FindName("ExternalPageFunctionSubtitle"),
                        "external SDK compiled PageFunction subtitle");
                    AssertEqual("External SDK page function", pageFunctionTitle.Text, "external SDK compiled PageFunction title text");
                    AssertEqual("External SDK PageFunction return path", pageFunctionSubtitle.Text, "external SDK compiled PageFunction subtitle text");
                    AssertAtLeast(navigatingCountBeforePageFunction + 1, window.ExternalFrameNavigatingCount, "external SDK PageFunction frame navigating count");
                    AssertAtLeast(navigatedCountBeforePageFunction + 1, window.ExternalFrameNavigatedCount, "external SDK PageFunction frame navigated count");
                    AssertAtLeast(loadCompletedCountBeforePageFunction + 1, window.ExternalFrameLoadCompletedCount, "external SDK PageFunction frame load completed count");
                    AssertEndsWith(window.LastExternalFrameNavigatedUri, "ExternalPageFunction.xaml", "external SDK PageFunction frame navigated URI");
                    AssertEqual("New", window.LastExternalFrameNavigationMode, "external SDK PageFunction frame navigation mode");
                    AssertEqual(typeof(ExternalPageFunction).FullName, window.LastExternalFrameContentType, "external SDK PageFunction frame content type");
                    AssertEqual("External PageFunction", pageFunction.Title, "external SDK compiled PageFunction title");

                    int pageFunctionReturnCountBefore = window.ExternalPageFunctionReturnCount;
                    MethodInfo onFinish = pageFunction.GetType()
                        .BaseType?
                        .GetMethod("_OnFinish", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new MissingMethodException(pageFunction.GetType().FullName, "_OnFinish");
                    onFinish.Invoke(
                        pageFunction,
                        new object[] { new ReturnEventArgs<string>("External PageFunction runtime result") });
                    DrainDispatcher();

                    AssertAtLeast(pageFunctionReturnCountBefore + 1, window.ExternalPageFunctionReturnCount, "external SDK PageFunction return count");
                    AssertEqual("External PageFunction runtime result", window.LastExternalPageFunctionResult, "external SDK PageFunction return result");
                }

                private static void ValidateXceedToolkitAndAvalonDock(MainWindow window, bool expectLoaded)
                {
                    var watermarkTextBox = RequireType<Xceed.Wpf.Toolkit.WatermarkTextBox>(
                        window.FindName("ExternalToolkitWatermarkTextBox"),
                        "external SDK Xceed WatermarkTextBox");
                    AssertEqual("external toolkit initial", watermarkTextBox.Text, "external SDK Xceed WatermarkTextBox initial binding");
                    AssertEqual("External toolkit watermark", Convert.ToString(watermarkTextBox.Watermark, CultureInfo.InvariantCulture), "external SDK Xceed WatermarkTextBox watermark");
                    watermarkTextBox.Text = "external toolkit updated";
                    watermarkTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    AssertEqual("external toolkit updated", window.ExternalToolkitText, "external SDK Xceed WatermarkTextBox source update");

                    var integerUpDown = RequireType<Xceed.Wpf.Toolkit.IntegerUpDown>(
                        window.FindName("ExternalToolkitIntegerUpDown"),
                        "external SDK Xceed IntegerUpDown");
                    AssertEqual((int?)4, integerUpDown.Value, "external SDK Xceed IntegerUpDown initial binding");
                    integerUpDown.Value = 7;
                    var valueProperty = RequireType<DependencyProperty>(
                        integerUpDown.GetType()
                            .GetField("ValueProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                            ?.GetValue(null),
                        "external SDK Xceed IntegerUpDown ValueProperty");
                    integerUpDown.GetBindingExpression(valueProperty)?.UpdateSource();
                    AssertEqual((int?)7, window.ExternalToolkitNumber, "external SDK Xceed IntegerUpDown source update");

                    var colorPicker = RequireType<Xceed.Wpf.Toolkit.ColorPicker>(
                        window.FindName("ExternalToolkitColorPicker"),
                        "external SDK Xceed ColorPicker");
                    AssertEqual((Color?)Colors.SteelBlue, colorPicker.SelectedColor, "external SDK Xceed ColorPicker initial binding");
                    colorPicker.SelectedColor = Colors.MediumSeaGreen;
                    colorPicker.GetBindingExpression(Xceed.Wpf.Toolkit.ColorPicker.SelectedColorProperty)?.UpdateSource();
                    AssertEqual((Color?)Colors.MediumSeaGreen, window.ExternalToolkitAccentColor, "external SDK Xceed ColorPicker source update");

                    var calculatorUpDown = RequireType<Xceed.Wpf.Toolkit.CalculatorUpDown>(
                        window.FindName("ExternalToolkitCalculatorUpDown"),
                        "external SDK Xceed CalculatorUpDown");
                    AssertEqual((decimal?)12.50m, calculatorUpDown.Value, "external SDK Xceed CalculatorUpDown initial binding");
                    calculatorUpDown.Value = 42.25m;
                    var calculatorValueProperty = RequireType<DependencyProperty>(
                        calculatorUpDown.GetType()
                            .GetField("ValueProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                            ?.GetValue(null),
                        "external SDK Xceed CalculatorUpDown ValueProperty");
                    calculatorUpDown.GetBindingExpression(calculatorValueProperty)?.UpdateSource();
                    AssertEqual((decimal?)42.25m, window.ExternalToolkitEstimate, "external SDK Xceed CalculatorUpDown source update");

                    var busyIndicator = RequireType<Xceed.Wpf.Toolkit.BusyIndicator>(
                        window.FindName("ExternalToolkitBusyIndicator"),
                        "external SDK Xceed BusyIndicator");
                    AssertEqual(false, busyIndicator.IsBusy, "external SDK Xceed BusyIndicator initial busy state");
                    AssertEqual("External busy", Convert.ToString(busyIndicator.BusyContent, CultureInfo.InvariantCulture), "external SDK Xceed BusyIndicator busy content");
                    var busyContent = RequireType<TextBlock>(
                        window.FindName("ExternalToolkitBusyContent"),
                        "external SDK Xceed BusyIndicator content");
                    AssertEqual("External BusyIndicator content", busyContent.Text, "external SDK Xceed BusyIndicator content text");
                    busyIndicator.IsBusy = true;
                    busyIndicator.GetBindingExpression(Xceed.Wpf.Toolkit.BusyIndicator.IsBusyProperty)?.UpdateSource();
                    AssertEqual(true, window.ExternalToolkitIsBusy, "external SDK Xceed BusyIndicator source update");
                    busyIndicator.IsBusy = false;
                    busyIndicator.GetBindingExpression(Xceed.Wpf.Toolkit.BusyIndicator.IsBusyProperty)?.UpdateSource();
                    AssertEqual(false, window.ExternalToolkitIsBusy, "external SDK Xceed BusyIndicator source reset");

                    var propertyGrid = RequireType<Xceed.Wpf.Toolkit.PropertyGrid.PropertyGrid>(
                        window.FindName("ExternalToolkitPropertyGrid"),
                        "external SDK Xceed PropertyGrid");
                    AssertEqual(window.SelectedExternalItem, propertyGrid.SelectedObject, "external SDK Xceed PropertyGrid selected object binding");
                    if (BindingOperations.GetBindingExpression(propertyGrid, Xceed.Wpf.Toolkit.PropertyGrid.PropertyGrid.SelectedObjectProperty) is null)
                    {
                        throw new InvalidOperationException("Expected external SDK Xceed PropertyGrid SelectedObject binding expression.");
                    }

                    var dropDownButton = RequireType<Xceed.Wpf.Toolkit.DropDownButton>(
                        window.FindName("ExternalToolkitDropDownButton"),
                        "external SDK Xceed DropDownButton");
                    var splitButton = RequireType<Xceed.Wpf.Toolkit.SplitButton>(
                        window.FindName("ExternalToolkitSplitButton"),
                        "external SDK Xceed SplitButton");
                    AssertEqual(false, dropDownButton.IsOpen, "external SDK Xceed DropDownButton initial popup state");
                    AssertEqual(false, splitButton.IsOpen, "external SDK Xceed SplitButton initial popup state");

                    var richTextBox = RequireType<Xceed.Wpf.Toolkit.RichTextBox>(
                        window.FindName("ExternalToolkitRichTextBox"),
                        "external SDK Xceed RichTextBox");
                    AssertEqual("external toolkit rich initial", richTextBox.Text, "external SDK Xceed RichTextBox initial binding");
                    AssertEqual("PlainTextFormatter", richTextBox.TextFormatter.GetType().Name, "external SDK Xceed RichTextBox text formatter");
                    richTextBox.Text = "external toolkit rich updated";
                    richTextBox.GetBindingExpression(Xceed.Wpf.Toolkit.RichTextBox.TextProperty)?.UpdateSource();
                    AssertEqual("external toolkit rich updated", window.ExternalToolkitRichText, "external SDK Xceed RichTextBox source update");

                    var multilineEditor = RequireType<Xceed.Wpf.Toolkit.MultiLineTextEditor>(
                        window.FindName("ExternalToolkitMultiLineTextEditor"),
                        "external SDK Xceed MultiLineTextEditor");
                    AssertEqual("external toolkit multiline initial", multilineEditor.Text, "external SDK Xceed MultiLineTextEditor initial binding");
                    multilineEditor.Text = "external toolkit multiline updated\nsecond line";
                    multilineEditor.GetBindingExpression(Xceed.Wpf.Toolkit.MultiLineTextEditor.TextProperty)?.UpdateSource();
                    AssertEqual("external toolkit multiline updated\nsecond line", window.ExternalToolkitMultilineText, "external SDK Xceed MultiLineTextEditor source update");

                    var buttonSpinner = RequireType<Xceed.Wpf.Toolkit.ButtonSpinner>(
                        window.FindName("ExternalToolkitButtonSpinner"),
                        "external SDK Xceed ButtonSpinner");
                    AssertEqual(true, buttonSpinner.ShowSpinner, "external SDK Xceed ButtonSpinner visible spinner");
                    AssertEqual("Right", Convert.ToString(buttonSpinner.SpinnerLocation, CultureInfo.InvariantCulture), "external SDK Xceed ButtonSpinner spinner location");
                    AssertEqual(3, window.ExternalToolkitSpinnerCount, "external SDK Xceed ButtonSpinner initial count");

                    var wizard = RequireType<Xceed.Wpf.Toolkit.Wizard>(
                        window.FindName("ExternalToolkitWizard"),
                        "external SDK Xceed Wizard");
                    var scopePage = RequireType<Xceed.Wpf.Toolkit.WizardPage>(
                        window.FindName("ExternalToolkitWizardScopePage"),
                        "external SDK Xceed Wizard scope page");
                    var reviewPage = RequireType<Xceed.Wpf.Toolkit.WizardPage>(
                        window.FindName("ExternalToolkitWizardReviewPage"),
                        "external SDK Xceed Wizard review page");
                    AssertEqual(2, wizard.Items.Count, "external SDK Xceed Wizard page count");
                    AssertEqual("External scope", scopePage.Title, "external SDK Xceed Wizard scope title");
                    AssertEqual("External SDK wizard scope", scopePage.Description, "external SDK Xceed Wizard scope description");
                    AssertEqual("Interior", scopePage.PageType.ToString(), "external SDK Xceed Wizard scope page type");
                    AssertEqual(false, scopePage.CanFinish.GetValueOrDefault(), "external SDK Xceed Wizard scope finish capability");
                    AssertEqual("External review", reviewPage.Title, "external SDK Xceed Wizard review title");
                    AssertEqual("External SDK wizard review", reviewPage.Description, "external SDK Xceed Wizard review description");
                    AssertEqual("Interior", reviewPage.PageType.ToString(), "external SDK Xceed Wizard review page type");
                    AssertEqual(true, reviewPage.CanFinish.GetValueOrDefault(), "external SDK Xceed Wizard review finish capability");
                    AssertEqual(false, wizard.FinishButtonClosesWindow, "external SDK Xceed Wizard finish close behavior");
                    AssertEqual(false, wizard.CancelButtonClosesWindow, "external SDK Xceed Wizard cancel close behavior");

                    if (expectLoaded)
                    {
                        ValidateExternalToolkitDropDownButton(window, dropDownButton);
                        ValidateExternalToolkitSplitButton(window, splitButton);
                        ValidateExternalToolkitButtonSpinner(window);
                        ValidateExternalToolkitWizard(window, wizard, reviewPage);
                    }

                    ValidateExternalToolkitWindowControl(window, expectLoaded);

                    var dockManager = RequireType<Xceed.Wpf.AvalonDock.DockingManager>(
                        window.FindName("ExternalDockManager"),
                        "external SDK AvalonDock DockingManager");
                    AssertEqual(true, dockManager.AllowMixedOrientation, "external SDK AvalonDock mixed orientation option");
                    AssertEqual(4.0, dockManager.GridSplitterWidth, "external SDK AvalonDock grid splitter width");
                    AssertEqual(4.0, dockManager.GridSplitterHeight, "external SDK AvalonDock grid splitter height");
                    AssertEqual(600, dockManager.AutoHideWindowClosingTimer, "external SDK AvalonDock auto-hide close timer option");
                    AssertEqual("AeroTheme", dockManager.Theme.GetType().Name, "external SDK AvalonDock theme type");

                    var dockRoot = RequireType<Xceed.Wpf.AvalonDock.Layout.LayoutRoot>(
                        window.FindName("ExternalDockLayoutRoot"),
                        "external SDK AvalonDock layout root");
                    var documentPane = RequireType<Xceed.Wpf.AvalonDock.Layout.LayoutDocumentPane>(
                        window.FindName("ExternalDockDocumentPane"),
                        "external SDK AvalonDock document pane");
                    var anchorablePane = RequireType<Xceed.Wpf.AvalonDock.Layout.LayoutAnchorablePane>(
                        window.FindName("ExternalDockAnchorablePane"),
                        "external SDK AvalonDock anchorable pane");
                    var document = RequireType<Xceed.Wpf.AvalonDock.Layout.LayoutDocument>(
                        window.FindName("ExternalDockDocument"),
                        "external SDK AvalonDock document");
                    var anchorable = RequireType<Xceed.Wpf.AvalonDock.Layout.LayoutAnchorable>(
                        window.FindName("ExternalToolkitPane"),
                        "external SDK AvalonDock anchorable");

                    AssertEqual(dockRoot, dockManager.Layout, "external SDK AvalonDock manager layout root");
                    AssertEqual(1, documentPane.ChildrenCount, "external SDK AvalonDock document pane child count");
                    AssertEqual(1, anchorablePane.ChildrenCount, "external SDK AvalonDock anchorable pane child count");
                    AssertEqual("external-document", document.ContentId, "external SDK AvalonDock document content id");
                    AssertEqual("Document", document.Title, "external SDK AvalonDock document title");
                    AssertEqual("external-toolkit", anchorable.ContentId, "external SDK AvalonDock anchorable content id");
                    AssertEqual("Toolkit", anchorable.Title, "external SDK AvalonDock anchorable title");
                    AssertEqual(false, anchorable.CanClose, "external SDK AvalonDock anchorable close policy");

                    if (expectLoaded)
                    {
                        ValidateExternalAvalonDockRuntimeActions(
                            dockManager,
                            dockRoot,
                            documentPane,
                            anchorablePane,
                            document,
                            anchorable);
                    }
                }

                private static void ValidateExternalAvalonDockRuntimeActions(
                    Xceed.Wpf.AvalonDock.DockingManager dockManager,
                    Xceed.Wpf.AvalonDock.Layout.LayoutRoot dockRoot,
                    Xceed.Wpf.AvalonDock.Layout.LayoutDocumentPane documentPane,
                    Xceed.Wpf.AvalonDock.Layout.LayoutAnchorablePane anchorablePane,
                    Xceed.Wpf.AvalonDock.Layout.LayoutDocument document,
                    Xceed.Wpf.AvalonDock.Layout.LayoutAnchorable anchorable)
                {
                    document.IsSelected = true;
                    document.IsActive = true;
                    DrainDispatcher();
                    AssertEqual(true, document.IsSelected, "external SDK AvalonDock document selected state");
                    AssertEqual(true, document.IsActive, "external SDK AvalonDock document active state");

                    anchorable.Hide();
                    DrainDispatcher();
                    AssertEqual(true, anchorable.IsHidden, "external SDK AvalonDock anchorable hidden state");
                    AssertEqual(true, dockRoot.Hidden.Contains(anchorable), "external SDK AvalonDock hidden collection membership");

                    anchorable.Show();
                    DrainDispatcher();
                    AssertEqual(false, anchorable.IsHidden, "external SDK AvalonDock anchorable restored state");
                    AssertEqual(false, dockRoot.Hidden.Contains(anchorable), "external SDK AvalonDock hidden collection restore");
                    AssertEqual(1, anchorablePane.ChildrenCount, "external SDK AvalonDock restored anchorable pane child count");

                    string layoutXml = SerializeExternalAvalonDockLayout(dockManager);
                    if (!layoutXml.Contains("<LayoutRoot", StringComparison.Ordinal)
                        || !layoutXml.Contains("ContentId=\"external-document\"", StringComparison.Ordinal)
                        || !layoutXml.Contains("ContentId=\"external-toolkit\"", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Expected external SDK AvalonDock layout serialization to include content ids.");
                    }

                    var roundTripped = DeserializeExternalAvalonDockLayout(layoutXml);
                    if (roundTripped.Layout.RootPanel is null
                        || roundTripped.Layout.RootPanel.ChildrenCount != dockRoot.RootPanel.ChildrenCount)
                    {
                        throw new InvalidOperationException("Expected external SDK AvalonDock layout deserialization to restore the root panel shape.");
                    }

                    AssertEqual(1, documentPane.ChildrenCount, "external SDK AvalonDock document pane preserved child count");
                }

                private static void ValidateExternalToolkitWindowControl(MainWindow window, bool expectLoaded)
                {
                    var windowContainer = RequireType<Xceed.Wpf.Toolkit.Primitives.WindowContainer>(
                        window.FindName("ExternalToolkitWindowContainer"),
                        "external SDK Xceed WindowContainer");
                    var windowControl = RequireType<Xceed.Wpf.Toolkit.Primitives.WindowControl>(
                        window.FindName("ExternalToolkitWindowControl"),
                        "external SDK Xceed WindowControl");
                    var inputTextBox = RequireType<TextBox>(
                        window.FindName("ExternalToolkitWindowControlInputTextBox"),
                        "external SDK Xceed WindowControl input TextBox");

                    AssertEqual(true, windowContainer.Children.Contains(windowControl), "external SDK Xceed WindowControl WindowContainer membership");
                    AssertEqual(Visibility.Visible, windowControl.Visibility, "external SDK Xceed WindowControl initial visibility");
                    AssertEqual(window.ExternalToolkitWindowControlVisibility, windowControl.Visibility, "external SDK Xceed WindowControl visibility binding value");
                    AssertEqual("External toolkit window", Convert.ToString(windowControl.Caption, CultureInfo.InvariantCulture), "external SDK Xceed WindowControl caption");
                    AssertEqual(Visibility.Visible, windowControl.CloseButtonVisibility, "external SDK Xceed WindowControl close button visibility");
                    AssertEqual(WindowStyle.SingleBorderWindow, windowControl.WindowStyle, "external SDK Xceed WindowControl style");
                    AssertEqual(new Thickness(1), windowControl.WindowBorderThickness, "external SDK Xceed WindowControl border thickness");
                    AssertEqual(new Thickness(2), windowControl.WindowThickness, "external SDK Xceed WindowControl window thickness");
                    AssertEqual("external toolkit window text", inputTextBox.Text, "external SDK Xceed WindowControl input initial binding");
                    if (BindingOperations.GetBindingExpression(windowControl, UIElement.VisibilityProperty) is null)
                    {
                        throw new InvalidOperationException("Expected external SDK Xceed WindowControl visibility binding expression.");
                    }

                    inputTextBox.Text = "external toolkit window updated";
                    inputTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    AssertEqual("external toolkit window updated", window.ExternalToolkitWindowControlText, "external SDK Xceed WindowControl text source update");

                    if (!expectLoaded)
                    {
                        return;
                    }

                    windowControl.ApplyTemplate();
                    windowControl.UpdateLayout();
                    if (windowControl.ActualWidth <= 0 || windowControl.ActualHeight <= 0)
                    {
                        throw new InvalidOperationException("Expected external SDK Xceed WindowControl to participate in layout.");
                    }

                    _ = window.GetExternalToolkitWindowControlButton("PART_CloseButton");
                    if (windowControl.Template?.FindName("PART_HeaderThumb", windowControl) is not Thumb)
                    {
                        throw new InvalidOperationException("Expected external SDK Xceed WindowControl template to expose the header thumb.");
                    }

                    int activatedBefore = window.ExternalToolkitWindowControlActivatedCount;
                    window.ActivateExternalToolkitWindowControl();
                    DrainDispatcher();
                    AssertAtLeast(activatedBefore + 1, window.ExternalToolkitWindowControlActivatedCount, "external SDK Xceed WindowControl activated count");
                    AssertEqual(true, windowControl.IsActive, "external SDK Xceed WindowControl active state");

                    int headerClickBefore = window.ExternalToolkitWindowControlHeaderClickCount;
                    window.RaiseExternalToolkitWindowControlHeaderClick();
                    AssertEqual(headerClickBefore + 1, window.ExternalToolkitWindowControlHeaderClickCount, "external SDK Xceed WindowControl header click count");

                    int headerDragBefore = window.ExternalToolkitWindowControlHeaderDragCount;
                    window.RaiseExternalToolkitWindowControlHeaderDrag();
                    AssertEqual(headerDragBefore + 1, window.ExternalToolkitWindowControlHeaderDragCount, "external SDK Xceed WindowControl header drag count");

                    int closeClickBefore = window.ExternalToolkitWindowControlCloseButtonClickCount;
                    Button closeButton = window.GetExternalToolkitWindowControlButton("PART_CloseButton");
                    closeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, closeButton));
                    DrainDispatcher();
                    AssertEqual(closeClickBefore + 1, window.ExternalToolkitWindowControlCloseButtonClickCount, "external SDK Xceed WindowControl close button count");
                    AssertEqual(Visibility.Collapsed, windowControl.Visibility, "external SDK Xceed WindowControl collapsed after close button");
                    AssertEqual("external toolkit window closed", window.ExternalToolkitWindowControlStatus, "external SDK Xceed WindowControl close status");

                    window.ShowExternalToolkitWindowControl();
                    AssertEqual(Visibility.Visible, windowControl.Visibility, "external SDK Xceed WindowControl restored visibility");
                }

                private static void ValidateExternalToolkitButtonSpinner(MainWindow window)
                {
                    int countBefore = window.ExternalToolkitSpinnerCount;
                    window.ApplyExternalToolkitSpinnerDelta(1);
                    AssertEqual(countBefore + 1, window.ExternalToolkitSpinnerCount, "external SDK Xceed ButtonSpinner increased count");
                    window.ApplyExternalToolkitSpinnerDelta(-1);
                    AssertEqual(countBefore, window.ExternalToolkitSpinnerCount, "external SDK Xceed ButtonSpinner restored count");
                }

                private static void ValidateExternalToolkitWizard(
                    MainWindow window,
                    Xceed.Wpf.Toolkit.Wizard wizard,
                    Xceed.Wpf.Toolkit.WizardPage reviewPage)
                {
                    if (wizard.CurrentPage == null)
                    {
                        throw new InvalidOperationException("Expected external SDK Xceed Wizard to select an initial page when loaded.");
                    }

                    int pageChangesBefore = window.ExternalToolkitWizardPageChanges;
                    wizard.CurrentPage = reviewPage;
                    DrainDispatcher();
                    AssertEqual(reviewPage, wizard.CurrentPage, "external SDK Xceed Wizard current page after review navigation");
                    AssertAtLeast(pageChangesBefore + 1, window.ExternalToolkitWizardPageChanges, "external SDK Xceed Wizard page change count");
                    AssertEqual("External review", window.ExternalToolkitWizardStatus, "external SDK Xceed Wizard page status");

                    int finishesBefore = window.ExternalToolkitWizardFinishes;
                    wizard.RaiseEvent(new Xceed.Wpf.Toolkit.Core.CancelRoutedEventArgs
                    {
                        RoutedEvent = Xceed.Wpf.Toolkit.Wizard.FinishEvent,
                    });
                    DrainDispatcher();
                    AssertAtLeast(finishesBefore + 1, window.ExternalToolkitWizardFinishes, "external SDK Xceed Wizard finish count");
                    AssertEqual("external toolkit wizard finished", window.ExternalToolkitWizardStatus, "external SDK Xceed Wizard finish status");

                    int cancelsBefore = window.ExternalToolkitWizardCancels;
                    wizard.RaiseEvent(new RoutedEventArgs(Xceed.Wpf.Toolkit.Wizard.CancelEvent));
                    DrainDispatcher();
                    AssertAtLeast(cancelsBefore + 1, window.ExternalToolkitWizardCancels, "external SDK Xceed Wizard cancel count");
                    AssertEqual("external toolkit wizard canceled", window.ExternalToolkitWizardStatus, "external SDK Xceed Wizard cancel status");
                }

                private static string SerializeExternalAvalonDockLayout(
                    Xceed.Wpf.AvalonDock.DockingManager dockManager)
                {
                    using var stream = new MemoryStream();
                    var serializer = new Xceed.Wpf.AvalonDock.Layout.Serialization.XmlLayoutSerializer(dockManager);
                    serializer.Serialize(stream);
                    return Encoding.UTF8.GetString(stream.ToArray());
                }

                private static Xceed.Wpf.AvalonDock.DockingManager DeserializeExternalAvalonDockLayout(
                    string layoutXml)
                {
                    var manager = new Xceed.Wpf.AvalonDock.DockingManager();
                    var serializer = new Xceed.Wpf.AvalonDock.Layout.Serialization.XmlLayoutSerializer(manager);
                    serializer.LayoutSerializationCallback += (_, args) =>
                    {
                        args.Content ??= new TextBlock
                        {
                            Text = args.Model.ContentId,
                        };
                    };

                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(layoutXml));
                    serializer.Deserialize(stream);
                    return manager;
                }

                private static void ValidateExternalToolkitDropDownButton(
                    MainWindow window,
                    Xceed.Wpf.Toolkit.DropDownButton dropDownButton)
                {
                    var dropDownRoot = RequireType<FrameworkElement>(
                        window.FindName("ExternalToolkitDropDownContentRoot"),
                        "external SDK Xceed DropDownButton dropdown content root");
                    var dropDownActionButton = RequireType<Button>(
                        window.FindName("ExternalToolkitDropDownActionButton"),
                        "external SDK Xceed DropDownButton action");

                    dropDownButton.IsOpen = true;
                    DrainDispatcher();
                    dropDownButton.UpdateLayout();
                    dropDownRoot.UpdateLayout();
                    AssertEqual(true, dropDownButton.IsOpen, "external SDK Xceed DropDownButton open state");
                    AssertDropDownContentSource(
                        dropDownRoot,
                        "external SDK Xceed DropDownButton dropdown source");

                    dropDownActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    DrainDispatcher();
                    AssertEqual(false, dropDownButton.IsOpen, "external SDK Xceed DropDownButton closed by routed click");
                    AssertEqual("external toolkit dropdown action", window.ExternalToolkitActionStatus, "external SDK Xceed DropDownButton action status");
                }

                private static void ValidateExternalToolkitSplitButton(
                    MainWindow window,
                    Xceed.Wpf.Toolkit.SplitButton splitButton)
                {
                    var splitRoot = RequireType<FrameworkElement>(
                        window.FindName("ExternalToolkitSplitDropDownContentRoot"),
                        "external SDK Xceed SplitButton dropdown content root");
                    var splitActionButton = RequireType<Button>(
                        window.FindName("ExternalToolkitSplitDropDownActionButton"),
                        "external SDK Xceed SplitButton dropdown action");

                    splitButton.RaiseEvent(new RoutedEventArgs(Xceed.Wpf.Toolkit.SplitButton.ClickEvent));
                    DrainDispatcher();
                    AssertEqual("external toolkit split primary action", window.ExternalToolkitActionStatus, "external SDK Xceed SplitButton primary action status");

                    splitButton.IsOpen = true;
                    DrainDispatcher();
                    splitButton.UpdateLayout();
                    splitRoot.UpdateLayout();
                    AssertEqual(true, splitButton.IsOpen, "external SDK Xceed SplitButton open state");
                    AssertDropDownContentSource(
                        splitRoot,
                        "external SDK Xceed SplitButton dropdown source");

                    splitActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    DrainDispatcher();
                    AssertEqual(false, splitButton.IsOpen, "external SDK Xceed SplitButton closed by routed click");
                    AssertEqual("external toolkit split dropdown action", window.ExternalToolkitActionStatus, "external SDK Xceed SplitButton dropdown action status");
                }

                private static void AssertDropDownContentSource(FrameworkElement root, string description)
                {
                    var source = PresentationSource.FromVisual(root);
                    if (source is not System.Windows.Interop.HwndSource || source.CompositionTarget is null)
                    {
                        throw new InvalidOperationException(
                            $"Expected {description} to use the portable public HwndSource facade.");
                    }
                }

                public static void ValidateApplicationRunAndShutdown()
                {
                    var app = RequireType<App>(
                        Application.Current,
                        "external SDK current application");
                    AssertEqual(1, App.ExternalStartupEventCount, "external SDK application startup event count");
                    AssertEqual(2, App.ExternalStartupArgumentCount, "external SDK application startup argument count");
                    AssertEqual("external-startup-alpha", App.ExternalStartupArguments[0], "external SDK application startup first argument");
                    AssertEqual("external startup beta", App.ExternalStartupArguments[1], "external SDK application startup second argument");
                    AssertEqual(2, app.Properties["ExternalStartupArgumentCount"], "external SDK application startup argument count property");
                    AssertEqual("external-startup-alpha", app.Properties["ExternalStartupFirstArgument"], "external SDK application startup first argument property");
                    AssertEqual("External SDK startup state", app.Properties["ExternalStartupState"], "external SDK application startup state property");
                    AssertEqual(ShutdownMode.OnLastWindowClose, app.ShutdownMode, "external SDK application shutdown mode");

                    var window = RequireType<MainWindow>(
                        app.MainWindow,
                        "external SDK application main window");
                    AssertEqual(true, window.IsVisible, "external SDK application main window visibility");
                    AssertEqual(44.0, window.Left, "external SDK application main window left");
                    AssertEqual(52.0, window.Top, "external SDK application main window top");
                    AssertEqual(true, window.Topmost, "external SDK application main window topmost");
                    AssertEqual(ResizeMode.NoResize, window.ResizeMode, "external SDK application main window resize mode");
                    window.Left = 56.0;
                    window.Top = 72.0;
                    window.Topmost = false;
                    window.ResizeMode = ResizeMode.CanResizeWithGrip;
                    window.WindowStyle = WindowStyle.None;
                    DrainDispatcher();
                    AssertEqual(56.0, window.Left, "external SDK application main window updated left");
                    AssertEqual(72.0, window.Top, "external SDK application main window updated top");
                    AssertEqual(false, window.Topmost, "external SDK application main window updated topmost");
                    AssertEqual(ResizeMode.CanResizeWithGrip, window.ResizeMode, "external SDK application main window updated resize mode");
                    AssertEqual(WindowStyle.None, window.WindowStyle, "external SDK application main window updated window style");
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
                    ValidateDispatcherSynchronizationContextAfterRun(window);
                    ValidateAsyncContinuationAfterRun(window);
                    ValidateDispatcherInvokeAsyncAfterRun(window);
                    ValidateDispatcherUnhandledExceptionAfterRun(app, window);
                    ValidateDispatcherTimerAfterRun(window);
                    ValidateLoadedStoryboardAfterRun(window);
                    ValidatePropertyTriggerActionsAfterRun(window);
                    ValidateMultiTriggerActionsAfterRun(window);
                    ValidateDataTriggerActionsAfterRun(window);
                    ValidateMultiDataTriggerActionsAfterRun(window);
                    ValidateVisualStateTransitions(window);
                    ValidateGridSplitterDragAfterRun(window);
                    ValidateItemContainerStyleSelectorAfterRun(window);
                    ValidatePreviousDataBindingsAfterRun(window);
                    ValidateAlternationAfterRun(window);
                    ValidateMultiSelectionAfterRun(window);
                    ValidateAdornerLayer(window);
                    ValidateValidationErrorTemplateAfterRun(window);
                    ValidateAccessKeyRoutingAfterRun(window);
                    ValidateClassInputBindingAfterRun(window);
                    ValidateKeyboardNavigationAfterRun(window);
                    ValidatePopupOpeningAfterRun(window);
                    ValidateXceedToolkitAndAvalonDock(window, expectLoaded: true);
                    ValidateApplicationWindowLifetime(app, window);

                    App.MarkExternalRunValidated();
                }

                private static void ValidateDispatcherSynchronizationContextAfterRun(MainWindow window)
                {
                    var currentContext = SynchronizationContext.Current
                        ?? throw new InvalidOperationException("Expected external SDK Application.Run dispatcher synchronization context.");
                    AssertEqual(
                        typeof(DispatcherSynchronizationContext),
                        currentContext.GetType(),
                        "external SDK dispatcher synchronization context type");

                    var postedHasAccess = false;
                    currentContext.Post(
                        _ => postedHasAccess = window.Dispatcher.CheckAccess(),
                        state: null);
                    PumpDispatcherUntil(
                        () => postedHasAccess,
                        TimeSpan.FromSeconds(1),
                        "external SDK dispatcher synchronization context Post");
                    AssertEqual(true, postedHasAccess, "external SDK dispatcher synchronization context Post access");

                    var sendHasAccess = false;
                    currentContext.Send(
                        _ => sendHasAccess = window.Dispatcher.CheckAccess(),
                        state: null);
                    AssertEqual(true, sendHasAccess, "external SDK dispatcher synchronization context Send access");

                    var copy = currentContext.CreateCopy();
                    AssertEqual(
                        typeof(DispatcherSynchronizationContext),
                        copy.GetType(),
                        "external SDK dispatcher synchronization context copy type");

                    var copyPostHasAccess = false;
                    copy.Post(
                        _ => copyPostHasAccess = window.Dispatcher.CheckAccess(),
                        state: null);
                    PumpDispatcherUntil(
                        () => copyPostHasAccess,
                        TimeSpan.FromSeconds(1),
                        "external SDK dispatcher synchronization context copy Post");
                    AssertEqual(true, copyPostHasAccess, "external SDK dispatcher synchronization context copy Post access");
                }

                private static void ValidateAsyncContinuationAfterRun(MainWindow window)
                {
                    var asyncText = RequireType<TextBlock>(
                        window.FindName("ExternalAsyncContinuationText"),
                        "external SDK async continuation text");
                    PumpDispatcherUntil(
                        () => window.ExternalAsyncContinuationCount > 0,
                        TimeSpan.FromSeconds(1),
                        "external SDK dispatcher async continuation");
                    AssertEqual(1, window.ExternalAsyncContinuationCount, "external SDK dispatcher async continuation count");
                    AssertEqual(
                        "async dispatcher continuation 1",
                        window.ExternalAsyncContinuationStatus,
                        "external SDK dispatcher async continuation status");
                    AssertEqual(
                        "async dispatcher continuation 1",
                        asyncText.Text,
                        "external SDK dispatcher async continuation bound text");
                }

                private static void ValidateDispatcherInvokeAsyncAfterRun(MainWindow window)
                {
                    var invokeText = RequireType<TextBlock>(
                        window.FindName("ExternalDispatcherInvokeAsyncText"),
                        "external SDK dispatcher InvokeAsync text");
                    PumpDispatcherUntil(
                        () => window.ExternalDispatcherInvokeAsyncCount > 0,
                        TimeSpan.FromSeconds(1),
                        "external SDK dispatcher InvokeAsync");
                    AssertEqual(1, window.ExternalDispatcherInvokeAsyncCount, "external SDK dispatcher InvokeAsync count");
                    AssertEqual(
                        "invoke async dispatcher 1",
                        window.ExternalDispatcherInvokeAsyncStatus,
                        "external SDK dispatcher InvokeAsync status");
                    AssertEqual(
                        "invoke async dispatcher 1",
                        invokeText.Text,
                        "external SDK dispatcher InvokeAsync bound text");
                }

                private static void ValidateDispatcherUnhandledExceptionAfterRun(App app, MainWindow window)
                {
                    int exceptionCount = 0;
                    object? eventSender = null;
                    string? exceptionMessage = null;
                    bool initialHandledState = true;
                    DispatcherUnhandledExceptionEventHandler handler = (sender, e) =>
                    {
                        exceptionCount++;
                        eventSender = sender;
                        exceptionMessage = e.Exception.Message;
                        initialHandledState = e.Handled;
                        e.Handled = true;
                    };

                    app.DispatcherUnhandledException += handler;
                    try
                    {
                        window.Dispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new Action(() => throw new InvalidOperationException("external SDK handled dispatcher exception")));
                        PumpDispatcherUntil(
                            () => exceptionCount > 0,
                            TimeSpan.FromSeconds(1),
                            "external SDK dispatcher unhandled exception");
                    }
                    finally
                    {
                        app.DispatcherUnhandledException -= handler;
                    }

                    AssertEqual(1, exceptionCount, "external SDK dispatcher unhandled exception count");
                    AssertEqual(window.Dispatcher, eventSender, "external SDK dispatcher unhandled exception sender");
                    AssertEqual("external SDK handled dispatcher exception", exceptionMessage, "external SDK dispatcher unhandled exception message");
                    AssertEqual(false, initialHandledState, "external SDK dispatcher unhandled exception initial handled state");
                    AssertEqual(app, Application.Current, "external SDK dispatcher unhandled exception current application");
                    AssertEqual(true, window.IsVisible, "external SDK dispatcher unhandled exception main window remains visible");
                }

                private static void ValidateDispatcherTimerAfterRun(MainWindow window)
                {
                    var timerText = RequireType<TextBlock>(
                        window.FindName("ExternalDispatcherTimerText"),
                        "external SDK dispatcher timer text");
                    PumpDispatcherUntil(
                        () => window.ExternalDispatcherTimerTickCount > 0,
                        TimeSpan.FromSeconds(1),
                        "external SDK dispatcher timer tick");
                    AssertEqual(1, window.ExternalDispatcherTimerTickCount, "external SDK dispatcher timer tick count");
                    AssertEqual("timer tick 1", window.ExternalDispatcherTimerStatus, "external SDK dispatcher timer status");
                    AssertEqual("timer tick 1", timerText.Text, "external SDK dispatcher timer bound text");
                }

                private static void ValidatePopupOpeningAfterRun(MainWindow window)
                {
                    var rootMenuItem = RequireType<MenuItem>(
                        window.FindName("ExternalRootMenuItem"),
                        "external SDK Application.Run root menu item");
                    rootMenuItem.UpdateLayout();
                    AssertEqual(false, rootMenuItem.IsSubmenuOpen, "external SDK Application.Run menu item initial submenu state");
                    rootMenuItem.IsSubmenuOpen = true;
                    DrainDispatcher();
                    AssertEqual(true, rootMenuItem.IsSubmenuOpen, "external SDK Application.Run menu item opened through portable popup");
                    rootMenuItem.IsSubmenuOpen = false;
                    DrainDispatcher();
                    AssertEqual(false, rootMenuItem.IsSubmenuOpen, "external SDK Application.Run menu item closed through portable popup");

                    var comboBox = RequireType<ComboBox>(
                        window.FindName("ExternalComboBox"),
                        "external SDK Application.Run combo box");
                    comboBox.ApplyTemplate();
                    comboBox.UpdateLayout();
                    AssertEqual(false, comboBox.IsDropDownOpen, "external SDK Application.Run combo box initial dropdown state");
                    comboBox.IsDropDownOpen = true;
                    DrainDispatcher();
                    AssertEqual(true, comboBox.IsDropDownOpen, "external SDK Application.Run combo box opened through portable popup");
                    comboBox.IsDropDownOpen = false;
                    DrainDispatcher();
                    AssertEqual(false, comboBox.IsDropDownOpen, "external SDK Application.Run combo box closed through portable popup");

                    var popupOwner = RequireType<Button>(
                        window.FindName("ExternalPopupOwnerButton"),
                        "external SDK Application.Run popup owner button");

                    var standalonePopup = RequireType<Popup>(
                        window.FindName("ExternalStandalonePopup"),
                        "external SDK Application.Run standalone popup");
                    AssertEqual(PlacementMode.Bottom, standalonePopup.Placement, "external SDK Application.Run standalone popup placement");
                    AssertEqual(popupOwner, standalonePopup.PlacementTarget, "external SDK Application.Run standalone popup placement target");
                    AssertEqual(false, standalonePopup.IsOpen, "external SDK Application.Run standalone popup initial open state");
                    standalonePopup.IsOpen = true;
                    DrainDispatcher();
                    AssertEqual(true, standalonePopup.IsOpen, "external SDK Application.Run standalone popup opened through portable popup");
                    var standalonePopupText = RequireType<TextBlock>(
                        standalonePopup.Child is Border border ? border.Child : null,
                        "external SDK Application.Run standalone popup text");
                    AssertEqual("External standalone popup content", standalonePopupText.Text, "external SDK Application.Run standalone popup content");
                    standalonePopup.IsOpen = false;
                    DrainDispatcher();
                    AssertEqual(false, standalonePopup.IsOpen, "external SDK Application.Run standalone popup closed through portable popup");

                    var toolTip = RequireType<ToolTip>(
                        popupOwner.ToolTip,
                        "external SDK Application.Run tooltip");
                    toolTip.PlacementTarget = popupOwner;
                    AssertEqual(false, toolTip.IsOpen, "external SDK Application.Run tooltip initial open state");
                    toolTip.IsOpen = true;
                    DrainDispatcher();
                    AssertEqual(true, toolTip.IsOpen, "external SDK Application.Run tooltip opened through portable popup");
                    toolTip.IsOpen = false;
                    DrainDispatcher();
                    AssertEqual(false, toolTip.IsOpen, "external SDK Application.Run tooltip closed through portable popup");

                    var contextMenu = RequireType<ContextMenu>(
                        popupOwner.ContextMenu,
                        "external SDK Application.Run context menu");
                    contextMenu.PlacementTarget = popupOwner;
                    AssertEqual(false, contextMenu.IsOpen, "external SDK Application.Run context menu initial open state");
                    contextMenu.IsOpen = true;
                    DrainDispatcher();
                    AssertEqual(true, contextMenu.IsOpen, "external SDK Application.Run context menu opened through portable popup");
                    contextMenu.IsOpen = false;
                    DrainDispatcher();
                    AssertEqual(false, contextMenu.IsOpen, "external SDK Application.Run context menu closed through portable popup");
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

                    AssertEqual(true, ApplicationContainsWindow(app, window), "external SDK application windows contains main window after canceled close");

                    var secondaryWindow = new Window
                    {
                        Title = "External SDK secondary window",
                        Width = 96,
                        Height = 48,
                        Content = new TextBlock { Text = "External secondary" }
                    };
                    int secondaryClosingCount = 0;
                    int secondaryClosedCount = 0;
                    secondaryWindow.Closing += (_, e) =>
                    {
                        secondaryClosingCount++;
                        AssertEqual(false, e.Cancel, "external SDK secondary window Closing cancel state");
                    };
                    secondaryWindow.Closed += (_, _) => secondaryClosedCount++;

                    secondaryWindow.Show();
                    DrainDispatcher();

                    AssertEqual(true, secondaryWindow.IsVisible, "external SDK secondary window visibility after show");
                    AssertEqual(true, ApplicationContainsWindow(app, secondaryWindow), "external SDK application windows contains secondary window");

                    secondaryWindow.Close();
                    DrainDispatcher();

                    AssertEqual(1, secondaryClosingCount, "external SDK secondary window Closing count");
                    AssertEqual(1, secondaryClosedCount, "external SDK secondary window Closed count");
                    AssertEqual(false, secondaryWindow.IsVisible, "external SDK secondary window visibility after close");
                    AssertEqual(false, ApplicationContainsWindow(app, secondaryWindow), "external SDK application windows excludes closed secondary window");
                    AssertEqual(true, ApplicationContainsWindow(app, window), "external SDK application windows keeps main window after secondary close");
                    AssertEqual(0, App.ExternalExitEventCount, "external SDK application exit count before main close");

                    var sizeToContentContent = new Border
                    {
                        Width = 132.0,
                        Height = 74.0,
                        Child = new TextBlock { Text = "External sized content" }
                    };
                    var sizeToContentWindow = new Window
                    {
                        Title = "External SDK size-to-content window",
                        SizeToContent = SizeToContent.WidthAndHeight,
                        Content = sizeToContentContent
                    };
                    int sizeToContentClosingCount = 0;
                    int sizeToContentClosedCount = 0;
                    sizeToContentWindow.Closing += (_, e) =>
                    {
                        sizeToContentClosingCount++;
                        AssertEqual(false, e.Cancel, "external SDK size-to-content window Closing cancel state");
                    };
                    sizeToContentWindow.Closed += (_, _) => sizeToContentClosedCount++;

                    sizeToContentWindow.Show();
                    DrainDispatcher();
                    sizeToContentWindow.UpdateLayout();

                    AssertEqual(true, sizeToContentWindow.IsVisible, "external SDK size-to-content window visibility after show");
                    AssertEqual(true, ApplicationContainsWindow(app, sizeToContentWindow), "external SDK application windows contains size-to-content window");
                    AssertClose(132.0, sizeToContentWindow.ActualWidth, "external SDK size-to-content window ActualWidth");
                    AssertClose(74.0, sizeToContentWindow.ActualHeight, "external SDK size-to-content window ActualHeight");
                    AssertClose(132.0, GetPortableHostDouble(sizeToContentWindow, "Width"), "external SDK size-to-content portable host width");
                    AssertClose(74.0, GetPortableHostDouble(sizeToContentWindow, "Height"), "external SDK size-to-content portable host height");

                    sizeToContentContent.Width = 156.0;
                    sizeToContentContent.Height = 82.0;
                    sizeToContentContent.InvalidateMeasure();
                    DrainDispatcher();
                    sizeToContentWindow.UpdateLayout();

                    AssertClose(156.0, sizeToContentWindow.ActualWidth, "external SDK live size-to-content window ActualWidth");
                    AssertClose(82.0, sizeToContentWindow.ActualHeight, "external SDK live size-to-content window ActualHeight");
                    AssertClose(sizeToContentWindow.ActualWidth, GetPortableHostDouble(sizeToContentWindow, "Width"), 12.0, "external SDK live size-to-content portable host width");
                    AssertClose(sizeToContentWindow.ActualHeight, GetPortableHostDouble(sizeToContentWindow, "Height"), 8.0, "external SDK live size-to-content portable host height");
                    AssertBetween(144.0, 168.0, GetPortableHostDouble(sizeToContentWindow, "Width"), "external SDK live size-to-content portable host width bounds");
                    AssertBetween(74.0, 90.0, GetPortableHostDouble(sizeToContentWindow, "Height"), "external SDK live size-to-content portable host height bounds");

                    sizeToContentWindow.Close();
                    DrainDispatcher();

                    AssertEqual(1, sizeToContentClosingCount, "external SDK size-to-content window Closing count");
                    AssertEqual(1, sizeToContentClosedCount, "external SDK size-to-content window Closed count");
                    AssertEqual(false, sizeToContentWindow.IsVisible, "external SDK size-to-content window visibility after close");
                    AssertEqual(false, ApplicationContainsWindow(app, sizeToContentWindow), "external SDK application windows excludes size-to-content window");
                    AssertEqual(true, ApplicationContainsWindow(app, window), "external SDK application windows keeps main window after size-to-content close");

                    var navigationWindow = new ExternalNavigationWindow
                    {
                        Owner = window
                    };

                    navigationWindow.Show();
                    PumpDispatcherUntil(
                        () => navigationWindow.Content is ExternalPage,
                        TimeSpan.FromSeconds(1),
                        "external SDK NavigationWindow initial page");
                    DrainDispatcher();

                    AssertEqual(true, navigationWindow.IsVisible, "external SDK NavigationWindow visibility after show");
                    AssertEqual(true, ApplicationContainsWindow(app, navigationWindow), "external SDK application windows contains NavigationWindow");
                    AssertEqual(false, navigationWindow.ShowsNavigationUI, "external SDK NavigationWindow UI metadata");
                    AssertEqual(window, navigationWindow.Owner, "external SDK NavigationWindow owner");
                    var navigationWindowService = navigationWindow.NavigationService
                        ?? throw new InvalidOperationException("Expected external SDK NavigationWindow NavigationService.");
                    var navigationWindowInitialPage = RequireType<ExternalPage>(
                        navigationWindow.Content,
                        "external SDK NavigationWindow initial content");
                    AssertEqual(navigationWindowService, navigationWindowInitialPage.NavigationService, "external SDK NavigationWindow page NavigationService property");
                    AssertEqual(navigationWindowService, NavigationService.GetNavigationService(navigationWindowInitialPage), "external SDK NavigationWindow page NavigationService lookup");
                    AssertAtLeast(1, navigationWindow.NavigatingCount, "external SDK NavigationWindow initial navigating count");
                    AssertAtLeast(1, navigationWindow.NavigatedCount, "external SDK NavigationWindow initial navigated count");
                    AssertAtLeast(1, navigationWindow.LoadCompletedCount, "external SDK NavigationWindow initial load completed count");
                    AssertEndsWith(navigationWindow.LastNavigatingUri, "ExternalPage.xaml", "external SDK NavigationWindow initial navigating URI");
                    AssertEndsWith(navigationWindow.LastNavigatedUri, "ExternalPage.xaml", "external SDK NavigationWindow initial navigated URI");
                    AssertEndsWith(navigationWindow.LastLoadCompletedUri, "ExternalPage.xaml", "external SDK NavigationWindow initial load completed URI");
                    AssertEqual("New", navigationWindow.LastNavigationMode, "external SDK NavigationWindow initial navigation mode");
                    AssertEqual(typeof(ExternalPage).FullName, navigationWindow.LastContentType, "external SDK NavigationWindow initial content type");

                    AssertEqual(
                        "External SDK page",
                        RequireType<TextBlock>(
                            navigationWindowInitialPage.FindName("ExternalPageTitle"),
                            "external SDK NavigationWindow initial page title").Text,
                        "external SDK NavigationWindow initial page text");

                    int navigationWindowNavigatingBeforeSecond = navigationWindow.NavigatingCount;
                    int navigationWindowNavigatedBeforeSecond = navigationWindow.NavigatedCount;
                    int navigationWindowLoadCompletedBeforeSecond = navigationWindow.LoadCompletedCount;
                    AssertEqual(
                        true,
                        navigationWindow.Navigate(new Uri("ExternalSecondPage.xaml", UriKind.Relative)),
                        "external SDK NavigationWindow second page navigate result");
                    PumpDispatcherUntil(
                        () => navigationWindow.Content is ExternalSecondPage,
                        TimeSpan.FromSeconds(1),
                        "external SDK NavigationWindow second page");

                    AssertAtLeast(navigationWindowNavigatingBeforeSecond + 1, navigationWindow.NavigatingCount, "external SDK NavigationWindow second navigating count");
                    AssertAtLeast(navigationWindowNavigatedBeforeSecond + 1, navigationWindow.NavigatedCount, "external SDK NavigationWindow second navigated count");
                    AssertAtLeast(navigationWindowLoadCompletedBeforeSecond + 1, navigationWindow.LoadCompletedCount, "external SDK NavigationWindow second load completed count");
                    AssertEqual("New", navigationWindow.LastNavigationMode, "external SDK NavigationWindow second navigation mode");
                    AssertEqual(typeof(ExternalSecondPage).FullName, navigationWindow.LastContentType, "external SDK NavigationWindow second content type");
                    AssertEqual(true, navigationWindow.CanGoBack, "external SDK NavigationWindow can go back");
                    var navigationWindowSecondPage = RequireType<ExternalSecondPage>(
                        navigationWindow.Content,
                        "external SDK NavigationWindow second content");
                    AssertEqual(navigationWindowService, navigationWindowSecondPage.NavigationService, "external SDK NavigationWindow second page NavigationService property");
                    AssertEqual(navigationWindowService, NavigationService.GetNavigationService(navigationWindowSecondPage), "external SDK NavigationWindow second page NavigationService lookup");

                    int navigationWindowNavigatingBeforeBack = navigationWindow.NavigatingCount;
                    int navigationWindowNavigatedBeforeBack = navigationWindow.NavigatedCount;
                    AssertEqual(true, navigationWindowService.CanGoBack, "external SDK NavigationWindow NavigationService can go back");
                    navigationWindowService.GoBack();
                    PumpDispatcherUntil(
                        () => navigationWindow.Content is ExternalPage,
                        TimeSpan.FromSeconds(1),
                        "external SDK NavigationWindow service back page");

                    AssertAtLeast(navigationWindowNavigatingBeforeBack + 1, navigationWindow.NavigatingCount, "external SDK NavigationWindow back navigating count");
                    AssertAtLeast(navigationWindowNavigatedBeforeBack + 1, navigationWindow.NavigatedCount, "external SDK NavigationWindow back navigated count");
                    AssertEqual("Back", navigationWindow.LastNavigationMode, "external SDK NavigationWindow back navigation mode");
                    AssertEqual(typeof(ExternalPage).FullName, navigationWindow.LastContentType, "external SDK NavigationWindow back content type");
                    AssertEqual(true, navigationWindow.CanGoForward, "external SDK NavigationWindow can go forward");
                    var navigationWindowReturnedPage = RequireType<ExternalPage>(
                        navigationWindow.Content,
                        "external SDK NavigationWindow returned content");
                    AssertEqual(navigationWindowService, navigationWindowReturnedPage.NavigationService, "external SDK NavigationWindow returned page NavigationService property");
                    AssertEqual(navigationWindowService, NavigationService.GetNavigationService(navigationWindowReturnedPage), "external SDK NavigationWindow returned page NavigationService lookup");

                    int navigationWindowNavigatingBeforeForward = navigationWindow.NavigatingCount;
                    int navigationWindowNavigatedBeforeForward = navigationWindow.NavigatedCount;
                    AssertEqual(true, navigationWindowService.CanGoForward, "external SDK NavigationWindow NavigationService can go forward");
                    navigationWindowService.GoForward();
                    PumpDispatcherUntil(
                        () => navigationWindow.Content is ExternalSecondPage,
                        TimeSpan.FromSeconds(1),
                        "external SDK NavigationWindow service forward page");

                    AssertAtLeast(navigationWindowNavigatingBeforeForward + 1, navigationWindow.NavigatingCount, "external SDK NavigationWindow forward navigating count");
                    AssertAtLeast(navigationWindowNavigatedBeforeForward + 1, navigationWindow.NavigatedCount, "external SDK NavigationWindow forward navigated count");
                    AssertEqual("Forward", navigationWindow.LastNavigationMode, "external SDK NavigationWindow forward navigation mode");
                    AssertEqual(typeof(ExternalSecondPage).FullName, navigationWindow.LastContentType, "external SDK NavigationWindow forward content type");
                    var navigationWindowForwardedPage = RequireType<ExternalSecondPage>(
                        navigationWindow.Content,
                        "external SDK NavigationWindow forwarded content");
                    AssertEqual(navigationWindowService, navigationWindowForwardedPage.NavigationService, "external SDK NavigationWindow forwarded page NavigationService property");
                    AssertEqual(navigationWindowService, NavigationService.GetNavigationService(navigationWindowForwardedPage), "external SDK NavigationWindow forwarded page NavigationService lookup");

                    navigationWindow.Close();
                    DrainDispatcher();

                    AssertEqual(1, navigationWindow.ClosingCount, "external SDK NavigationWindow Closing count");
                    AssertEqual(1, navigationWindow.ClosedCount, "external SDK NavigationWindow Closed count");
                    AssertEqual(false, navigationWindow.IsVisible, "external SDK NavigationWindow visibility after close");
                    AssertEqual(false, ApplicationContainsWindow(app, navigationWindow), "external SDK application windows excludes closed NavigationWindow");
                    AssertEqual(true, ApplicationContainsWindow(app, window), "external SDK application windows keeps main window after NavigationWindow close");

                    var ownedWindow = new Window
                    {
                        Title = "External SDK owned window",
                        Width = 88,
                        Height = 44,
                        Owner = window,
                        Content = new TextBlock { Text = "External owned" }
                    };
                    int ownedClosingCount = 0;
                    int ownedClosedCount = 0;
                    bool ownedClosingCancelBefore = true;
                    bool ownedClosingCancelAfter = false;
                    ownedWindow.Closing += (_, e) =>
                    {
                        ownedClosingCount++;
                        ownedClosingCancelBefore = e.Cancel;
                        e.Cancel = true;
                        ownedClosingCancelAfter = e.Cancel;
                    };
                    ownedWindow.Closed += (_, _) => ownedClosedCount++;

                    ownedWindow.Show();
                    DrainDispatcher();

                    AssertEqual(true, ownedWindow.IsVisible, "external SDK owned window visibility after show");
                    AssertEqual(window, ownedWindow.Owner, "external SDK owned window owner");
                    AssertEqual(1, window.OwnedWindows.Count, "external SDK main window owned window count");
                    AssertEqual(ownedWindow, window.OwnedWindows[0], "external SDK main window owned window entry");
                    AssertEqual(true, ApplicationContainsWindow(app, ownedWindow), "external SDK application windows contains owned window");

                    var modalDialog = new Window
                    {
                        Title = "External SDK modal dialog",
                        Width = 90,
                        Height = 46,
                        Owner = window,
                        Content = new TextBlock { Text = "External modal" }
                    };
                    int modalLoadedCount = 0;
                    int modalClosingCount = 0;
                    int modalClosedCount = 0;
                    bool modalOwnerDuringLoaded = false;
                    bool modalInApplicationWindowsDuringLoaded = false;
                    int ownerOwnedWindowsCountDuringModal = 0;
                    modalDialog.Loaded += (_, _) =>
                    {
                        modalLoadedCount++;
                        modalOwnerDuringLoaded = ReferenceEquals(window, modalDialog.Owner);
                        modalInApplicationWindowsDuringLoaded = ApplicationContainsWindow(app, modalDialog);
                        ownerOwnedWindowsCountDuringModal = window.OwnedWindows.Count;
                        modalDialog.Dispatcher.BeginInvoke(
                            DispatcherPriority.Render,
                            new Action(() => modalDialog.DialogResult = true));
                    };
                    modalDialog.Closing += (_, e) =>
                    {
                        modalClosingCount++;
                        AssertEqual(false, e.Cancel, "external SDK modal dialog Closing cancel state");
                    };
                    modalDialog.Closed += (_, _) => modalClosedCount++;

                    bool? modalResult = modalDialog.ShowDialog();

                    AssertEqual(true, modalResult, "external SDK modal dialog result");
                    AssertEqual(1, modalLoadedCount, "external SDK modal dialog Loaded count");
                    AssertEqual(1, modalClosingCount, "external SDK modal dialog Closing count");
                    AssertEqual(1, modalClosedCount, "external SDK modal dialog Closed count");
                    AssertEqual(true, modalOwnerDuringLoaded, "external SDK modal dialog owner during Loaded");
                    AssertEqual(true, modalInApplicationWindowsDuringLoaded, "external SDK modal dialog Application.Windows during Loaded");
                    AssertEqual(2, ownerOwnedWindowsCountDuringModal, "external SDK owner OwnedWindows count during modal dialog");
                    AssertEqual(false, modalDialog.IsVisible, "external SDK modal dialog visibility after close");
                    AssertEqual(false, ApplicationContainsWindow(app, modalDialog), "external SDK application windows excludes closed modal dialog");
                    AssertEqual(1, window.OwnedWindows.Count, "external SDK main window owned window count after modal dialog");
                    AssertEqual(ownedWindow, window.OwnedWindows[0], "external SDK main window owned window entry after modal dialog");

                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    AssertEqual(ShutdownMode.OnMainWindowClose, app.ShutdownMode, "external SDK application main-window shutdown mode");

                    window.Close();

                    AssertEqual(closingCountBefore + 2, window.ExternalWindowClosingCount, "external SDK final window Closing count");
                    AssertEqual(closedCountBefore + 1, window.ExternalWindowClosedCount, "external SDK final window Closed count");
                    AssertEqual(false, window.LastExternalWindowClosingCancelBefore, "external SDK final window Closing initial cancel state");
                    AssertEqual(false, window.LastExternalWindowClosingCancelAfter, "external SDK final window Closing final cancel state");
                    AssertEqual(nameof(MainWindow), window.LastExternalWindowClosedSenderType, "external SDK final window Closed sender");
                    AssertEqual(false, window.IsVisible, "external SDK final window visibility");
                    AssertEqual(1, ownedClosingCount, "external SDK owned window Closing count after owner close");
                    AssertEqual(1, ownedClosedCount, "external SDK owned window Closed count after owner close");
                    AssertEqual(false, ownedClosingCancelBefore, "external SDK owned window Closing initial cancel state");
                    AssertEqual(true, ownedClosingCancelAfter, "external SDK owned window Closing attempted cancel state");
                    AssertEqual(false, ownedWindow.IsVisible, "external SDK owned window visibility after owner close");
                }

                private static double GetPortableHostDouble(Window window, string propertyName)
                {
                    var activationProperty = typeof(Window).GetProperty(
                        "PortableWindowActivation",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    object activation = activationProperty?.GetValue(window)
                        ?? throw new InvalidOperationException($"Expected {window.Title} to have a portable activation.");
                    var hostProperty = activation.GetType().GetProperty(
                        "Host",
                        BindingFlags.Instance | BindingFlags.Public);
                    object host = hostProperty?.GetValue(activation)
                        ?? throw new InvalidOperationException($"Expected {window.Title} portable activation to expose a host.");
                    var valueProperty = host.GetType().GetProperty(
                        propertyName,
                        BindingFlags.Instance | BindingFlags.Public);
                    object value = valueProperty?.GetValue(host)
                        ?? throw new InvalidOperationException($"Expected {window.Title} portable host to expose {propertyName}.");
                    return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                }

                private static bool ApplicationContainsWindow(App app, Window window)
                {
                    foreach (Window candidate in app.Windows)
                    {
                        if (ReferenceEquals(candidate, window))
                        {
                            return true;
                        }
                    }

                    return false;
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
                    var arrayItems = RequireType<string[]>(
                        appResources["ExternalArrayItems"],
                        "external SDK x:Array resource");
                    AssertEqual(2, arrayItems.Length, "external SDK x:Array resource length");
                    AssertEqual("External array alpha", arrayItems[0], "external SDK x:Array resource first item");
                    AssertEqual("External array beta", arrayItems[1], "external SDK x:Array resource second item");
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
                    AssertEqual(null, staticResourceText.TryFindResource("ExternalDefinitelyMissingResource"), "external SDK TryFindResource missing resource");
                    try
                    {
                        staticResourceText.FindResource("ExternalDefinitelyMissingResource");
                        throw new InvalidOperationException("Expected external SDK missing FindResource lookup to throw.");
                    }
                    catch (ResourceReferenceKeyNotFoundException ex)
                    {
                        AssertEqual("ExternalDefinitelyMissingResource", ex.Key, "external SDK FindResource missing resource key");
                    }

                    var arrayItemsControl = RequireType<ItemsControl>(
                        window.FindName("ExternalArrayItemsControl"),
                        "external SDK x:Array items control");
                    AssertEqual(arrayItems, arrayItemsControl.ItemsSource, "external SDK x:Array ItemsSource");
                    AssertEqual(2, arrayItemsControl.Items.Count, "external SDK x:Array items count");
                    var nullResourceText = RequireType<TextBlock>(
                        window.FindName("ExternalNullIntrinsicText"),
                        "external SDK x:Null intrinsic text block");
                    AssertEqual(null, nullResourceText.Tag, "external SDK x:Null intrinsic tag");

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

                    var runtimeMergedResourceText = RequireType<TextBlock>(
                        window.FindName("ExternalRuntimeMergedResourceText"),
                        "external SDK runtime merged dynamic resource text block");
                    var runtimeMergedDictionary = new ResourceDictionary
                    {
                        ["ExternalRuntimeMergedText"] = "External runtime merged resource",
                        ["ExternalRuntimeMergedBrush"] = new SolidColorBrush(Color.FromRgb(0x6A, 0x48, 0x8B))
                    };
                    appResources.MergedDictionaries.Add(runtimeMergedDictionary);
                    DrainDispatcher();
                    AssertEqual(
                        "External runtime merged resource",
                        runtimeMergedResourceText.Text,
                        "external SDK runtime merged dynamic resource text");
                    AssertBrushColor(
                        runtimeMergedResourceText.Foreground,
                        "#FF6A488B",
                        "external SDK runtime merged dynamic resource foreground");
                    runtimeMergedDictionary["ExternalRuntimeMergedText"] = "External runtime merged resource updated";
                    runtimeMergedDictionary["ExternalRuntimeMergedBrush"] = new SolidColorBrush(Color.FromRgb(0x24, 0x74, 0x63));
                    DrainDispatcher();
                    AssertEqual(
                        "External runtime merged resource updated",
                        runtimeMergedResourceText.Text,
                        "external SDK updated runtime merged dynamic resource text");
                    AssertBrushColor(
                        runtimeMergedResourceText.Foreground,
                        "#FF247463",
                        "external SDK updated runtime merged dynamic resource foreground");

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

                    var implicitStylePanel = RequireType<StackPanel>(
                        window.FindName("ExternalImplicitStylePanel"),
                        "external SDK implicit style panel");
                    var implicitStyledText = RequireType<TextBlock>(
                        window.FindName("ExternalImplicitStyledText"),
                        "external SDK implicit styled text");
                    AssertAtLeast(1, implicitStylePanel.Children.Count, "external SDK implicit style panel child count");
                    AssertEqual("External implicit style text", implicitStyledText.Text, "external SDK implicit styled text content");
                    AssertEqual("external implicit style active", implicitStyledText.Tag, "external SDK implicit styled text tag");
                    var implicitTextStyle = RequireType<Style>(
                        implicitStyledText.Style,
                        "external SDK implicit text style");
                    AssertEqual(typeof(TextBlock), implicitTextStyle.TargetType, "external SDK implicit text style target type");
                    AssertAtLeast(2, implicitTextStyle.Setters.Count, "external SDK implicit text style setter count");
                    AssertBrushColor(implicitStyledText.Foreground, "#FFA65A2A", "external SDK implicit styled text foreground");

                    var implicitTemplate = RequireType<DataTemplate>(
                        window.FindResource(new DataTemplateKey(typeof(ExternalItem))),
                        "external SDK implicit item data template");
                    var implicitTemplateRoot = RequireType<TextBlock>(
                        implicitTemplate.LoadContent(),
                        "external SDK implicit item template root");
                    var implicitTemplateBinding = BindingOperations.GetBindingExpression(implicitTemplateRoot, TextBlock.TextProperty)
                        ?? throw new InvalidOperationException("Expected external SDK implicit item template Text binding.");
                    AssertEqual("Name", implicitTemplateBinding.ParentBinding.Path.Path, "external SDK implicit item template binding path");
                    var implicitTemplatePresenter = RequireType<ContentPresenter>(
                        window.FindName("ExternalImplicitTemplatePresenter"),
                        "external SDK implicit template presenter");
                    AssertEqual(window.SelectedExternalItem, implicitTemplatePresenter.Content, "external SDK implicit template presenter content");
                    implicitTemplateRoot.DataContext = window.SelectedExternalItem;
                    DrainDispatcher();
                    AssertEqual("External implicit Alpha", implicitTemplateRoot.Text, "external SDK implicit item template resolved text");

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

                    var frameworkContainerStyle = RequireType<Style>(
                        window.FindResource("ExternalFrameworkItemContainerStyle"),
                        "external SDK framework item container style");
                    var defaultContainerStyle = RequireType<Style>(
                        window.FindResource("ExternalDefaultItemContainerStyle"),
                        "external SDK default item container style");
                    var containerStyleSelector = RequireType<ExternalItemContainerStyleSelector>(
                        window.FindResource("ExternalItemContainerStyleSelector"),
                        "external SDK item container style selector resource");
                    AssertEqual(typeof(ListBoxItem), frameworkContainerStyle.TargetType, "external SDK framework item container style target");
                    AssertEqual(typeof(ListBoxItem), defaultContainerStyle.TargetType, "external SDK default item container style target");
                    AssertEqual(frameworkContainerStyle, containerStyleSelector.FrameworkStyle, "external SDK item container style selector framework style");
                    AssertEqual(defaultContainerStyle, containerStyleSelector.DefaultStyle, "external SDK item container style selector default style");
                    var styleSelectorList = RequireType<ListBox>(
                        window.FindName("ExternalStyleSelectorItemsList"),
                        "external SDK item container style selector list");
                    AssertEqual(containerStyleSelector, styleSelectorList.ItemContainerStyleSelector, "external SDK ListBox ItemContainerStyleSelector");
                    AssertEqual(2, styleSelectorList.Items.Count, "external SDK item container style selector item count");

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

                private static void ValidateRuntimeResourceReference(MainWindow window)
                {
                    var appResources = Application.Current?.Resources
                        ?? throw new InvalidOperationException("External SDK validation requires Application resources.");
                    var focusPanel = RequireType<Panel>(
                        window.FindName("ExternalFocusPanel"),
                        "external SDK runtime resource reference host panel");
                    var runtimeText = new TextBlock
                    {
                        Text = "External runtime resource reference"
                    };

                    focusPanel.Children.Add(runtimeText);
                    runtimeText.SetResourceReference(TextBlock.ForegroundProperty, "ExternalDynamicBrush");
                    DrainDispatcher();
                    AssertBrushColor(runtimeText.Foreground, "#FF457623", "external SDK runtime SetResourceReference initial foreground");

                    appResources["ExternalDynamicBrush"] = new SolidColorBrush(Color.FromRgb(0x7B, 0x4A, 0x9C));
                    DrainDispatcher();
                    AssertBrushColor(runtimeText.Foreground, "#FF7B4A9C", "external SDK runtime SetResourceReference updated foreground");
                    focusPanel.Children.Remove(runtimeText);
                }

                private static void ValidateRuntimeNameScope(MainWindow window)
                {
                    var registeredButton = new Button
                    {
                        Content = "External runtime registered button"
                    };

                    window.RegisterName("ExternalRuntimeRegisteredButton", registeredButton);
                    AssertEqual(registeredButton, window.FindName("ExternalRuntimeRegisteredButton"), "external SDK runtime namescope registered object");

                    try
                    {
                        window.RegisterName("ExternalRuntimeRegisteredButton", new Button());
                        throw new InvalidOperationException("Expected external SDK runtime namescope duplicate registration to throw.");
                    }
                    catch (ArgumentException)
                    {
                    }

                    AssertEqual(registeredButton, window.FindName("ExternalRuntimeRegisteredButton"), "external SDK runtime namescope duplicate preserves original");

                    window.UnregisterName("ExternalRuntimeRegisteredButton");
                    AssertEqual(null, window.FindName("ExternalRuntimeRegisteredButton"), "external SDK runtime namescope unregister clears object");

                    var replacementButton = new Button
                    {
            ç®´ëFòµë(š+myÖævVD6÷VçC°¢f"F†—&D6öçF–æW"ÒvWDvVæW&FVDÆ—7D&÷„—FVÒ€¢×VÇF•6VÆV7DÆ—7BÀ¢v–æF÷räW‡FW&æÄ—FV×5³%ÒÀ¢&W‡FW&æÂ4D²×VÇF’×6VÆV7BF†—&B—FVÒ6öçF–æW""“°¢F†—&D6öçF–æW"ä—56VÆV7FVBÒG'VS°¢G&–äF—7F6†W"‚“° ¢76W'DWVÂƒ"Â×VÇF•6VÆV7DÆ—7Bå6VÆV7FVD—FV×2ä6÷VçBÂ&W‡FW&æÂ4D²×VÇF’×6VÆV7B6V6öæB6VÆV7FVB6÷VçB"“°¢76W'DWVÂ‡v–æF÷räW‡FW&æÄ—FV×5³ÒÂ×VÇF•6VÆV7DÆ—7Bå6VÆV7FVD—FV×5³ÒÂ&W‡FW&æÂ4D²×VÇF’×6VÆV7B&WF–æVBf—'7B—FVÒ"“°¢76W'DWVÂ‡v–æF÷räW‡FW&æÄ—FV×5³%ÒÂ×VÇF•6VÆV7DÆ—7Bå6VÆV7FVD—FV×5³ÒÂ&W‡FW&æÂ4D²×VÇF’×6VÆV7B6V6öæB6VÆV7FVB—FVÒ"“°¢76W'DDÆV7B‡6VÆV7F–öä&Vf÷&R²Âv–æF÷räW‡FW&æÄ×VÇF•6VÆV7F–öä6†ævVD6÷VçBÂ&W‡FW&æÂ4D²×VÇF’×6VÆV7B6V6öæB6VÆV7F–öâ6†ævVB6÷VçB"“°¢76W'DWVÂƒÂv–æF÷räÆ7DW‡FW&æÄ×VÇF•6VÆV7F–öäFFVD6÷VçBÂ&W‡FW&æÂ4D²×VÇF’×6VÆV7B6V6öæBFFVB6÷VçB"“°¢76W'DWVÂƒÂv–æF÷räÆ7DW‡FW&æÄ×VÇF•6VÆV7F–öå&VÖ÷fVD6÷VçBÂ&W‡FW&æÂ4D²×VÇF’×6VÆV7B6V6öæB&VÖ÷fVB6÷VçB"“°¢76W'DWVÂ‚$vÖÖ"Âv–æF÷räÆ7DW‡FW&æÄ×VÇF•6VÆV7F–öäFFVDæÖRóò7G&–æräV×G’Â&W‡FW&æÂ4D²×VÇF’×6VÆV7B6V6öæBFFVB—FVÒ"“° ¢6VÆV7F–öä&Vf÷&RÒv–æF÷räW‡FW&æÄ×VÇF•6VÆV7F–öä6†ævVD6÷VçC°¢f—'7D6öçF–æW"ä—56VÆV7FVBÒfÇ6S°¢G&–äF—7F6†W"‚“° ¢76W'DWVÂƒÂ×VÇF•6VÆV7DÆ—7Bå6VÆV7FVD—FV×2ä6÷VçBÂ&W‡FW&æÂ4D²×VÇF’×6VÆV7B&VÖ÷fVB6VÆV7FVB6÷VçB"“°¢76W'DWVÂ‡v–æF÷räW‡FW&æÄ—FV×5³%ÒÂ×VÇF•6VÆV7DÆ—7Bå6VÆV7FVD—FV×5³ÒÂ&W‡FW&æÂ4D²×VÇF’×6VÆV7B&VÖ–æ–ær6VÆV7FVB—FVÒ"“°¢76W'DDÆV7B‡6VÆV7F–öä&Vf÷&R²Âv–æF÷räW‡FW&æÄ×VÇF•6VÆV7F–öä6†ævVD6÷VçBÂ&W‡FW&æÂ4D²×VÇF’×6VÆV7B&VÖ÷fÂ6VÆV7F–öâ6†ævVB6÷VçB"“°¢76W'DWVÂƒÂv–æF÷räÆ7DW‡FW&æÄ×VÇF•6VÆV7F–öäFFVD6÷VçBÂ&W‡FW&æÂ4D²×VÇF’×6VÆV7B&VÖ÷fÂFFVB6÷VçB"“°¢76W'DWVÂƒÂv–æF÷räÆ7DW‡FW&æÄ×VÇF•6VÆV7F–öå&VÖ÷fVD6÷VçBÂ&W‡FW&æÂ4D²×VÇF’×6VÆV7B&VÖ÷fÂ&VÖ÷fVB6÷VçB"“°¢76W'DWVÂ‚$Ç†"Âv–æF÷räÆ7DW‡FW&æÄ×VÇF•6VÆV7F–öå&VÖ÷fVDæÖRóò7G&–æräV×G’Â&W‡FW&æÂ4D²×VÇF’×6VÆV7B&VÖ÷fVB—FVÒ"“°¢Ğ ¢&—fFR7FF–2Æ—7D&÷„—FVÒvWDvVæW&FVDÆ—7D&÷„—FVÒ€¢Æ—7D&÷‚Æ—7D&÷‚À¢ö&¦V7B—FVÒÀ¢7G&–ær—FVÔ6öçF–æW$FW67&—F–öâ¢°¢Æ—7D&÷‚å67&öÆÄ–çFõf–Wr†—FVÒ“°¢Æ—7D&÷‚åWFFTÆ–÷WB‚“° ¢&WGW&â&WV—&UG—SÄÆ—7D&÷„—FVÓâ€¢Æ—7D&÷‚ä—FVÔ6öçF–æW$vVæW&F÷"ä6öçF–æW$g&öÔ—FVÒ†—FVÒ’À¢—FVÔ6öçF–æW$FW67&—F–öâ“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFU&Wf–÷W4FF&–æF–æw4gFW%'Vâ„Ö–åv–æF÷rv–æF÷r¢°¢f"&Wf–÷W4FFÆ—7BÒ&WV—&UG—SÄÆ—7D&÷ƒâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ&Wf–÷W4FF—FV×4Æ—7B"’À¢&W‡FW&æÂ4D²Æ–6F–öâå'Vâ&Wf–÷W4FF—FV×2Æ—7B"“°¢76W'DWVÂƒ"Â&Wf–÷W4FFÆ—7Bä—FV×2ä6÷VçBÂ&W‡FW&æÂ4D²Æ–6F–öâå'Vâ&Wf–÷W4FF–æ—F–Â—FVÒ6÷VçB"“° ¢v–æF÷räW‡FW&æÄ—FV×2äFB†æWrW‡FW&æÄ—FVÒ‚$vÖÖ"Â$FF"’“°¢G&–äF—7F6†W"‚“° ¢76W'DWVÂƒ2Â&Wf–÷W4FFÆ—7Bä—FV×2ä6÷VçBÂ&W‡FW&æÂ4D²Æ–6F–öâå'Vâ&Wf–÷W4FF—FVÒ6÷VçBgFW"×WFF–öâ"“°¢fÆ–FFTvVæW&FVE&Wf–÷W4FF—FVÒ€¢&Wf–÷W4FFÆ—7BÀ¢v–æF÷räW‡FW&æÄ—FV×5³ÒÀ¢$Ç†"À¢$æò&Wf–÷W2"À¢&W‡FW&æÂ4D²&Wf–÷W4FFf—'7B—FVÒ6öçF–æW""À¢&W‡FW&æÂ4D²&Wf–÷W4FFf—'7B7W'&VçBFW‡B"À¢&W‡FW&æÂ4D²&Wf–÷W4FFf—'7B&Wf–÷W2FW‡B"“°¢fÆ–FFTvVæW&FVE&Wf–÷W4FF—FVÒ€¢&Wf–÷W4FFÆ—7BÀ¢v–æF÷räW‡FW&æÄ—FV×5³ÒÀ¢$&WF"À¢$Ç†"À¢&W‡FW&æÂ4D²&Wf–÷W4FF6V6öæB—FVÒ6öçF–æW""À¢&W‡FW&æÂ4D²&Wf–÷W4FF6V6öæB7W'&VçBFW‡B"À¢&W‡FW&æÂ4D²&Wf–÷W4FF6V6öæB&Wf–÷W2FW‡B"“°¢fÆ–FFTvVæW&FVE&Wf–÷W4FF—FVÒ€¢&Wf–÷W4FFÆ—7BÀ¢v–æF÷räW‡FW&æÄ—FV×5³%ÒÀ¢$vÖÖ"À¢$&WF"À¢&W‡FW&æÂ4D²&Wf–÷W4FFF†—&B—FVÒ6öçF–æW""À¢&W‡FW&æÂ4D²&Wf–÷W4FFF†—&B7W'&VçBFW‡B"À¢&W‡FW&æÂ4D²&Wf–÷W4FFF†—&B&Wf–÷W2FW‡B"“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFTvVæW&FVE&Wf–÷W4FF—FVÒ€¢Æ—7D&÷‚&Wf–÷W4FFÆ—7BÀ¢ö&¦V7B—FVÒÀ¢7G&–ærW‡V7FVD7W'&VçEFW‡BÀ¢7G&–ærW‡V7FVE&Wf–÷W5FW‡BÀ¢7G&–ær—FVÔ6öçF–æW$FW67&—F–öâÀ¢7G&–ær7W'&VçEFW‡DFW67&—F–öâÀ¢7G&–ær&Wf–÷W5FW‡DFW67&—F–öâ¢°¢&Wf–÷W4FFÆ—7Bå67&öÆÄ–çFõf–Wr†—FVÒ“°¢&Wf–÷W4FFÆ—7BåWFFTÆ–÷WB‚“° ¢f"—FVÔ6öçF–æW"Ò&WV—&UG—SÄÆ—7D&÷„—FVÓâ€¢&Wf–÷W4FFÆ—7Bä—FVÔ6öçF–æW$vVæW&F÷"ä6öçF–æW$g&öÔ—FVÒ†—FVÒ’À¢—FVÔ6öçF–æW$FW67&—F–öâ“°¢—FVÔ6öçF–æW"äÇ•FV×ÆFR‚“°¢—FVÔ6öçF–æW"åWFFTÆ–÷WB‚“° ¢f"7W'&VçEFW‡BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢f–æEf—7VÄFW66VæFçD'”æÖR†—FVÔ6öçF–æW"Â$W‡FW&æÅ&Wf–÷W4FF7W'&VçEFW‡B"’À¢7W'&VçEFW‡DFW67&—F–öâ“°¢f"&Wf–÷W5FW‡BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢f–æEf—7VÄFW66VæFçD'”æÖR†—FVÔ6öçF–æW"Â$W‡FW&æÅ&Wf–÷W4FF&Wf–÷W5FW‡B"’À¢&Wf–÷W5FW‡DFW67&—F–öâ“° ¢76W'DWVÂ†W‡V7FVD7W'&VçEFW‡BÂ7W'&VçEFW‡BåFW‡BÂ7W'&VçEFW‡DFW67&—F–öâ“°¢76W'DWVÂ†W‡V7FVE&Wf–÷W5FW‡BÂ&Wf–÷W5FW‡BåFW‡BÂ&Wf–÷W5FW‡DFW67&—F–öâ“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFU6VÆV7F÷'4æD6öçFVçB„Ö–åv–æF÷rv–æF÷r¢°¢f"6öÖ&ô&÷‚Ò&WV—&UG—SÄ6öÖ&ô&÷ƒâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ6öÖ&ô&÷‚"’À¢&W‡FW&æÂ4D²6öÖ&ò&÷‚"“°¢G&–äF—7F6†W"‚“°¢76W'DWVÂƒ2Â6öÖ&ô&÷‚ä—FV×2ä6÷VçBÂ&W‡FW&æÂ4D²6öÖ&ò&÷‚—FVÒ6÷VçBgFW"×WFF–öâ"“°¢76W'DWVÂ‚$¶–æB"Â6öÖ&ô&÷‚å6VÆV7FVEfÇVUF‚Â&W‡FW&æÂ4D²6öÖ&ò&÷‚6VÆV7FVBfÇVRF‚"“°¢76W'DWVÂ‡G'VRÂ6öÖ&ô&÷‚ä—5FW‡E6V&6„Væ&ÆVBÂ&W‡FW&æÂ4D²6öÖ&ò&÷‚FW‡B6V&6‚Væ&ÆVB"“°¢76W'DWVÂ‚$æÖR"ÂFW‡E6V&6‚ävWEFW‡EF‚†6öÖ&ô&÷‚’Â&W‡FW&æÂ4D²6öÖ&ò&÷‚FW‡B6V&6‚F‚"“°¢76W'DWVÂ‚%&VæFW&–ær"Â6öÖ&ô&÷‚å6VÆV7FVEfÇVRÂ&W‡FW&æÂ4D²6öÖ&ò&÷‚6VÆV7FVBfÇVR"“°¢76W'DWVÂƒÂ6öÖ&ô&÷‚å6VÆV7FVD–æFW‚Â&W‡FW&æÂ4D²6öÖ&ò&÷‚6VÆV7FVB–æFW‚"“°¢76W'DWVÂ‡v–æF÷räW‡FW&æÄ—FV×5³ÒÂ6öÖ&ô&÷‚å6VÆV7FVD—FVÒÂ&W‡FW&æÂ4D²6öÖ&ò&÷‚6VÆV7FVB—FVÒ"“°¢f"6VÆV7FVEfÇVT&–æF–ærÒ6öÖ&ô&÷‚ävWD&–æF–ætW‡&W76–öâ…6VÆV7F÷"å6VÆV7FVEfÇVU&÷W'G’¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡V7FVBW‡FW&æÂ4D²6öÖ&ô&÷‚6VÆV7FVEfÇVR&–æF–ætW‡&W76–öââ"“°¢76W'DWVÂ‚%6VÆV7FVDW‡FW&æÄ¶–æB"Â6VÆV7FVEfÇVT&–æF–ærå&VçD&–æF–æråF‚åF‚Â&W‡FW&æÂ4D²6öÖ&ò&÷‚6VÆV7FVBfÇVR&–æF–ærF‚"“° ¢–çB6öÖ&õ6VÆV7F–öä&Vf÷&RÒv–æF÷räW‡FW&æÅ6VÆV7F–öä6†ævVD6÷VçC°¢6öÖ&ô&÷‚å6VÆV7FVD–æFW‚Ò°¢G&–äF—7F6†W"‚“°¢76W'DWVÂ‚$g&ÖWv÷&²"Â6öÖ&ô&÷‚å6VÆV7FVEfÇVRÂ&W‡FW&æÂ4D²6öÖ&ò&÷‚6VÆV7FVBfÇVRgFW"6†ævR"“°¢76W'DWVÂ‚$g&ÖWv÷&²"Âv–æF÷rå6VÆV7FVDW‡FW&æÄ¶–æBÂ&W‡FW&æÂ4D²6öÖ&ò&÷‚Gvò×v’6VÆV7FVBfÇVR6÷W&6RWFFR"“°¢76W'DDÆV7B†6öÖ&õ6VÆV7F–öä&Vf÷&R²Âv–æF÷räW‡FW&æÅ6VÆV7F–öä6†ævVD6÷VçBÂ&W‡FW&æÂ4D²6öÖ&ò&÷‚6VÆV7F–öâ6†ævVB6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÄ6öÖ&ô&÷‚"Âv–æF÷räÆ7DW‡FW&æÅ6VÆV7F–öå6÷W&6TæÖRÂ&W‡FW&æÂ4D²6öÖ&ò&÷‚6VÆV7F–öâ6÷W&6RæÖR"“° ¢f"F$6öçG&öÂÒ&WV—&UG—SÅF$6öçG&öÃâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅF$6öçG&öÂ"’À¢&W‡FW&æÂ4D²F"6öçG&öÂ"“°¢f"g&ÖWv÷&µF"Ò&WV—&UG—SÅF$—FVÓâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄg&ÖWv÷&µF""’À¢&W‡FW&æÂ4D²g&ÖWv÷&²F"—FVÒ"“°¢f"&VæFW&–æuF"Ò&WV—&UG—SÅF$—FVÓâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ&VæFW&–æuF""’À¢&W‡FW&æÂ4D²&VæFW&–ærF"—FVÒ"“°¢76W'DWVÂƒ"ÂF$6öçG&öÂä—FV×2ä6÷VçBÂ&W‡FW&æÂ4D²F"—FVÒ6÷VçB"“°¢76W'DWVÂƒÂF$6öçG&öÂå6VÆV7FVD–æFW‚Â&W‡FW&æÂ4D²F"6VÆV7FVB–æFW‚"“°¢76W'DWVÂ‡&VæFW&–æuF"ÂF$6öçG&öÂå6VÆV7FVD—FVÒÂ&W‡FW&æÂ4D²6VÆV7FVBF"—FVÒ"“°¢76W'DWVÂ‚$g&ÖWv÷&²"Âg&ÖWv÷&µF"ä†VFW"Â&W‡FW&æÂ4D²g&ÖWv÷&²F"†VFW""“°¢76W'DWVÂ‚%&VæFW&–ær"Â&VæFW&–æuF"ä†VFW"Â&W‡FW&æÂ4D²&VæFW&–ærF"†VFW""“°¢f"&VæFW&–æuF%FW‡BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ&VæFW&–æuF%FW‡B"’À¢&W‡FW&æÂ4D²&VæFW&–ærF"FW‡B"“°¢76W'DWVÂ‚$g&ÖWv÷&²"Â&VæFW&–æuF%FW‡BåFW‡BÂ&W‡FW&æÂ4D²&VæFW&–ærF"6öçFVçB&–æF–ær"“° ¢–çBF%6VÆV7F–öä&Vf÷&RÒv–æF÷räW‡FW&æÅ6VÆV7F–öä6†ævVD6÷VçC°¢F$6öçG&öÂå6VÆV7FVD–æFW‚Ò°¢G&–äF—7F6†W"‚“°¢76W'DWVÂ†g&ÖWv÷&µF"ÂF$6öçG&öÂå6VÆV7FVD—FVÒÂ&W‡FW&æÂ4D²F"—FVÒgFW"6VÆV7FVB–æFW‚6†ævR"“°¢76W'DDÆV7B‡F%6VÆV7F–öä&Vf÷&R²Âv–æF÷räW‡FW&æÅ6VÆV7F–öä6†ævVD6÷VçBÂ&W‡FW&æÂ4D²F"6VÆV7F–öâ6†ævVB6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÅF$6öçG&öÂ"Âv–æF÷räÆ7DW‡FW&æÅ6VÆV7F–öå6÷W&6TæÖRÂ&W‡FW&æÂ4D²F"6VÆV7F–öâ6÷W&6RæÖR"“° ¢f"w&÷W&÷‚Ò&WV—&UG—SÄw&÷W&÷ƒâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄw&÷W&÷‚"’À¢&W‡FW&æÂ4D²w&÷W&÷‚"“°¢f"w&÷WFW‡BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢w&÷W&÷‚ä6öçFVçBÀ¢&W‡FW&æÂ4D²w&÷W&÷‚6öçFVçB"“°¢76W'DWVÂ‚$W‡FW&æÂw&÷W"Âw&÷W&÷‚ä†VFW"Â&W‡FW&æÂ4D²w&÷W&÷‚†VFW""“°¢76W'DWVÂ‚$Ç†"Âw&÷WFW‡BåFW‡BÂ&W‡FW&æÂ4D²w&÷W&÷‚6öçFVçB&–æF–ær"“° ¢f"W‡æFW"Ò&WV—&UG—SÄW‡æFW#â€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄW‡æFW""’À¢&W‡FW&æÂ4D²W‡æFW""“°¢f"W‡æFW%FW‡BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢W‡æFW"ä6öçFVçBÀ¢&W‡FW&æÂ4D²W‡æFW"6öçFVçB"“°¢76W'DWVÂ‚$W‡FW&æÂW‡æFW""ÂW‡æFW"ä†VFW"Â&W‡FW&æÂ4D²W‡æFW"†VFW""“°¢76W'DWVÂ†fÇ6RÂW‡æFW"ä—4W‡æFVBÂ&W‡FW&æÂ4D²W‡æFW"–æ—F–ÂW‡æFVB7FFR"“°¢76W'DWVÂ‚$W‡FW&æÂW‡æFVB6öçFVçB"ÂW‡æFW%FW‡BåFW‡BÂ&W‡FW&æÂ4D²W‡æFW"6öçFVçBFW‡B"“° ¢–çBW‡æFVD&Vf÷&RÒv–æF÷räW‡FW&æÄW‡æFW$W‡æFVD6÷VçC°¢–çB6öÆÆ6VD&Vf÷&RÒv–æF÷räW‡FW&æÄW‡æFW$6öÆÆ6VD6÷VçC°¢W‡æFW"ä—4W‡æFVBÒG'VS°¢G&–äF—7F6†W"‚“°¢76W'DWVÂ‡G'VRÂW‡æFW"ä—4W‡æFVBÂ&W‡FW&æÂ4D²W‡æFW"W‡æFVB7FFR"“°¢76W'DDÆV7B†W‡æFVD&Vf÷&R²Âv–æF÷räW‡FW&æÄW‡æFW$W‡æFVD6÷VçBÂ&W‡FW&æÂ4D²W‡æFW"W‡æFVBWfVçB6÷VçB"“°¢W‡æFW"ä—4W‡æFVBÒfÇ6S°¢G&–äF—7F6†W"‚“°¢76W'DWVÂ†fÇ6RÂW‡æFW"ä—4W‡æFVBÂ&W‡FW&æÂ4D²W‡æFW"6öÆÆ6VB7FFR"“°¢76W'DDÆV7B†6öÆÆ6VD&Vf÷&R²Âv–æF÷räW‡FW&æÄW‡æFW$6öÆÆ6VD6÷VçBÂ&W‡FW&æÂ4D²W‡æFW"6öÆÆ6VBWfVçB6÷VçB"“° ¢f"67&öÆÅf–WvW"Ò&WV—&UG—SÅ67&öÆÅf–WvW#â€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ67&öÆÅf–WvW""’À¢&W‡FW&æÂ4D²67&öÆÂf–WvW""“°¢f"67&öÆÄ6öçFVçBÒ&WV—&UG—SÅ7F6µæVÃâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ67&öÆÄ6öçFVçB"’À¢&W‡FW&æÂ4D²67&öÆÂ6öçFVçBæVÂ"“°¢76W'DWVÂ…67&öÆÄ&%f—6–&–Æ—G’äWFòÂ67&öÆÅf–WvW"åfW'F–6Å67&öÆÄ&%f—6–&–Æ—G’Â&W‡FW&æÂ4D²67&öÆÂf–WvW"fW'F–6Âf—6–&–Æ—G’"“°¢76W'DWVÂ…67&öÆÄ&%f—6–&–Æ—G’äF—6&ÆVBÂ67&öÆÅf–WvW"ä†÷&—¦öçFÅ67&öÆÄ&%f—6–&–Æ—G’Â&W‡FW&æÂ4D²67&öÆÂf–WvW"†÷&—¦öçFÂf—6–&–Æ—G’"“°¢76W'DWVÂ‡67&öÆÄ6öçFVçBÂ67&öÆÅf–WvW"ä6öçFVçBÂ&W‡FW&æÂ4D²67&öÆÂf–WvW"6öçFVçB"“°¢76W'DWVÂƒ"Â67&öÆÄ6öçFVçBä6†–ÆG&Vâä6÷VçBÂ&W‡FW&æÂ4D²67&öÆÂ6öçFVçB6†–ÆB6÷VçB"“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFU&–6„Fö7VÖVçG2„Ö–åv–æF÷rv–æF÷r¢°¢f"&–6…FW‡D&÷‚Ò&WV—&UG—SÅ&–6…FW‡D&÷ƒâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ&–6…FW‡D&÷‚"’À¢&W‡FW&æÂ4D²&–6‚FW‡B&÷‚"“°¢f"Fö7VÖVçBÒ&–6…FW‡D&÷‚äFö7VÖVçC°¢76W'DWVÂƒRÂFö7VÖVçBä&Æö6·2ä6÷VçBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB&Æö6²6÷VçB"“° ¢f"–çG&õ&w&‚Ò&WV—&UG—SÅ&w&ƒâ€¢Fö7VÖVçBä&Æö6·2äf—'7D&Æö6²À¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB–çG&ò&w&‚"“°¢f"–æÆ–æW2Ò–çG&õ&w&‚ä–æÆ–æW3°¢f"Æ–å'VâÒ&WV—&Tf—'7D–æÆ–æTW†7CÅ'Vãâ†–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBÆ–â'Vâ"“°¢76W'DWVÂ‚$W‡FW&æÂ"ÂÆ–å'VâåFW‡BÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBÆ–â'VâFW‡B"“°¢f"&öÆBÒ&WV—&Tf—'7D–æÆ–æSÄ&öÆCâ†–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB&öÆB–æÆ–æR"“°¢76W'DWVÂ‚'&–6‚"Â&WV—&Tf—'7D–æÆ–æSÅ'Vãâ†&öÆBä–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB&öÆB'Vâ"’åFW‡BÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB&öÆB'VâFW‡B"“°¢f"—FÆ–2Ò&WV—&Tf—'7D–æÆ–æSÄ—FÆ–3â†–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB—FÆ–2–æÆ–æR"“°¢76W'DWVÂ‚"—FÆ–2"Â&WV—&Tf—'7D–æÆ–æSÅ'Vãâ†—FÆ–2ä–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB—FÆ–2'Vâ"’åFW‡BÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB—FÆ–2'VâFW‡B"“°¢f"VæFW&Æ–æRÒ&WV—&Tf—'7D–æÆ–æSÅVæFW&Æ–æSâ†–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBVæFW&Æ–æR–æÆ–æR"“°¢76W'DWVÂ‚"VæFW&Æ–æR"Â&WV—&Tf—'7D–æÆ–æSÅ'Vãâ‡VæFW&Æ–æRä–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBVæFW&Æ–æR'Vâ"’åFW‡BÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBVæFW&Æ–æR'VâFW‡B"“°¢f"7âÒ&WV—&Tf—'7D–æÆ–æTW†7CÅ7ãâ†–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB7â–æÆ–æR"“°¢76W'DWVÂ‚"7â"Â&WV—&Tf—'7D–æÆ–æSÅ'Vãâ‡7âä–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB7â'Vâ"’åFW‡BÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB7â'VâFW‡B"“°¢&WV—&Tf—'7D–æÆ–æSÄÆ–æT'&V³â†–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBÆ–æR'&V²–æÆ–æR"“° ¢f"‡—W&Æ–æ²Ò&WV—&Tf—'7D–æÆ–æSÄ‡—W&Æ–æ³â†–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB‡—W&Æ–æ²"“°¢76W'DWVÂ‚$W‡FW&æÄFö7VÖVçDÆ–æ²"Â‡—W&Æ–æ²äæÖRÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB‡—W&Æ–æ²æÖR"“°¢76W'DWVÂ‚&‡GG3¢òöW†×ÆRçFW7BöW‡FW&æÂ×6F²"Â‡—W&Æ–æ²äæf–vFUW&“òåFõ7G&–ær‚’Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB‡—W&Æ–æ²U$’"“°¢76W'DWVÂ‚&Æ–æ²"Â&WV—&Tf—'7D–æÆ–æSÅ'Vãâ†‡—W&Æ–æ²ä–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB‡—W&Æ–æ²'Vâ"’åFW‡BÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB‡—W&Æ–æ²'VâFW‡B"“°¢76W'DWVÂƒÂv–æF÷räW‡FW&æÄFö7VÖVçDÆ–æµ&WVW7Dæf–vFT6÷VçBÂ&W‡FW&æÂ4D²‡—W&Æ–æ²–æ—F–Â&WVW7Dæf–vFR6÷VçB"“°¢‡—W&Æ–æ²äFô6Æ–6²‚“°¢76W'DWVÂƒÂv–æF÷räW‡FW&æÄFö7VÖVçDÆ–æµ&WVW7Dæf–vFT6÷VçBÂ&W‡FW&æÂ4D²‡—W&Æ–æ²&WVW7Dæf–vFR†æFÆW"6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÄFö7VÖVçDÆ–æ²"Âv–æF÷räÆ7DW‡FW&æÄFö7VÖVçDÆ–æµ&WVW7Dæf–vFU6VæFW$æÖRÂ&W‡FW&æÂ4D²‡—W&Æ–æ²&WVW7Dæf–vFR6VæFW""“°¢76W'DWVÂ‚&‡GG3¢òöW†×ÆRçFW7BöW‡FW&æÂ×6F²"Âv–æF÷räÆ7DW‡FW&æÄFö7VÖVçDÆ–æµ&WVW7Dæf–vFUW&’Â&W‡FW&æÂ4D²‡—W&Æ–æ²&WVW7Dæf–vFRU$’"“°¢76W'DWVÂ‚%&WVW7Dæf–vFR"Âv–æF÷räÆ7DW‡FW&æÄFö7VÖVçDÆ–æµ&WVW7Dæf–vFU&÷WFVDWfVçDæÖRÂ&W‡FW&æÂ4D²‡—W&Æ–æ²&WVW7Dæf–vFR&÷WFVBWfVçB"“° ¢f"–æÆ–æT6öçF–æW"Ò&WV—&Tf—'7D–æÆ–æSÄ–æÆ–æUT”6öçF–æW#â†–æÆ–æW2Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB–æÆ–æRT’6öçF–æW""“°¢f"–æÆ–æT'WGFöâÒ&WV—&UG—SÄ'WGFöãâ†–æÆ–æT6öçF–æW"ä6†–ÆBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB–æÆ–æR'WGFöâ"“°¢76W'DWVÂ‚&W‡FW&æÂ–æÆ–æR'WGFöâ"Â–æÆ–æT'WGFöâä6öçFVçBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB–æÆ–æR'WGFöâ6öçFVçB"“° ¢f"Fö7VÖVçDÆ—7BÒ&WV—&UG—SÅ7—7FVÒåv–æF÷w2äFö7VÖVçG2äÆ—7Câ€¢–çG&õ&w&‚äæW‡D&Æö6²À¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBÆ—7B"“°¢76W'DWVÂ…FW‡DÖ&¶W%7G–ÆRäFV6–ÖÂÂFö7VÖVçDÆ—7BäÖ&¶W%7G–ÆRÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBÆ—7BÖ&¶W"7G–ÆR"“°¢76W'DWVÂƒ"ÂFö7VÖVçDÆ—7BäÆ—7D—FV×2ä6÷VçBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBÆ—7B—FVÒ6÷VçB"“°¢76W'DÆ—7D—FVÕFW‡B†Fö7VÖVçDÆ—7BäÆ—7D—FV×2äf—'7DÆ—7D—FVÒÂ$W‡FW&æÂÆ—7BöæR"Â&f—'7B"“°¢76W'DÆ—7D—FVÕFW‡B†Fö7VÖVçDÆ—7BäÆ—7D—FV×2äf—'7DÆ—7D—FVÒäæW‡DÆ—7D—FVÒÂ$W‡FW&æÂÆ—7BGvò"Â'6V6öæB"“° ¢f"6V7F–öâÒ&WV—&UG—SÅ6V7F–öãâ€¢Fö7VÖVçDÆ—7BäæW‡D&Æö6²À¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB6V7F–öâ"“°¢76W'DWVÂƒÂ6V7F–öâä&Æö6·2ä6÷VçBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB6V7F–öâ&Æö6²6÷VçB"“°¢76W'E&w&…FW‡B€¢&WV—&UG—SÅ&w&ƒâ‡6V7F–öâä&Æö6·2äf—'7D&Æö6²Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB6V7F–öâ&w&‚"’À¢$W‡FW&æÂ6V7F–öâ"À¢'6V7F–öâ"“° ¢f"&Æö6´6öçF–æW"Ò&WV—&UG—SÄ&Æö6µT”6öçF–æW#â€¢6V7F–öâäæW‡D&Æö6²À¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB&Æö6²T’6öçF–æW""“°¢f"&Æö6´'WGFöâÒ&WV—&UG—SÄ'WGFöãâ†&Æö6´6öçF–æW"ä6†–ÆBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB&Æö6²'WGFöâ"“°¢76W'DWVÂ‚&W‡FW&æÂ&Æö6²'WGFöâ"Â&Æö6´'WGFöâä6öçFVçBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB&Æö6²'WGFöâ6öçFVçB"“° ¢f"F&ÆRÒ&WV—&UG—SÅF&ÆSâ€¢&Æö6´6öçF–æW"äæW‡D&Æö6²À¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBF&ÆR"“°¢76W'DWVÂƒ"ÂF&ÆRä6öÇVÖç2ä6÷VçBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBF&ÆR6öÇVÖâ6÷VçB"“°¢76W'DWVÂƒÂF&ÆRå&÷tw&÷W2ä6÷VçBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBF&ÆR&÷rw&÷W6÷VçB"“°¢f"&÷tw&÷WÒF&ÆRå&÷tw&÷W5³Ó°¢76W'DWVÂƒÂ&÷tw&÷Wå&÷w2ä6÷VçBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBF&ÆR&÷r6÷VçB"“°¢f"&÷rÒ&÷tw&÷Wå&÷w5³Ó°¢76W'DWVÂƒ"Â&÷rä6VÆÇ2ä6÷VçBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBF&ÆR6VÆÂ6÷VçB"“°¢76W'EF&ÆT6VÆÅFW‡B‡&÷rä6VÆÇ5³ÒÂ$W‡FW&æÂ6VÆÂÇ†"Â&f—'7B"“°¢76W'EF&ÆT6VÆÅFW‡B‡&÷rä6VÆÇ5³ÒÂ$W‡FW&æÂ6VÆÂ&WF"Â'6V6öæB"“° ¢fÆ–FFU&–6…FW‡DVF—F–æt6öÖÖæG2‡&–6…FW‡D&÷‚Â–çG&õ&w&‚ÂÆ–å'VâÂFö7VÖVçDÆ—7B“° ¢&–6…FW‡D&÷‚å6VÆV7F–öâå6VÆV7B†Fö7VÖVçBä6öçFVçE7F'BÂFö7VÖVçBä6öçFVçDVæB“°¢76W'D6öçF–ç2‚$W‡FW&æÂ6V7F–öâ"Â&–6…FW‡D&÷‚å6VÆV7F–öâåFW‡BÂ&W‡FW&æÂ4D²&–6…FW‡D&÷‚6VÆV7F–öâFW‡B"“°¢f"Fö7VÖVçEFW‡BÒæWrFW‡E&ævR†Fö7VÖVçBä6öçFVçE7F'BÂFö7VÖVçBä6öçFVçDVæB’åFW‡C°¢76W'D6öçF–ç2‚$W‡FW&æÂ6VÆÂ&WF"ÂFö7VÖVçEFW‡BÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçBFW‡E&ævRF&ÆRFW‡B"“° ¢f"67&öÆÅf–WvW"Ò&WV—&UG—SÄfÆ÷tFö7VÖVçE67&öÆÅf–WvW#â€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄfÆ÷tFö7VÖVçE67&öÆÅf–WvW""’À¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE67&öÆÅf–WvW""“°¢76W'DWVÂ†fÇ6RÂ67&öÆÅf–WvW"ä—5FööÄ&%f—6–&ÆRÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE67&öÆÅf–WvW"FööÆ&"fÆr"“°¢f"67&öÆÅf–WvW$Fö7VÖVçBÒ67&öÆÅf–WvW"äFö7VÖVç@¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡V7FVBW‡FW&æÂ4D²fÆ÷tFö7VÖVçE67&öÆÅf–WvW"Fö7VÖVçBâ"“°¢76W'DWVÂƒ"ãÂ67&öÆÅf–WvW$Fö7VÖVçBåvUFF–æräÆVgBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE67&öÆÅf–WvW"vRFF–ær"“°¢76W'DWVÂƒ3#ãÂ67&öÆÅf–WvW$Fö7VÖVçBä6öÇVÖåv–GF‚Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE67&öÆÅf–WvW"6öÇVÖâv–GF‚"“°¢76W'DWVÂƒ"Â67&öÆÅf–WvW$Fö7VÖVçBä&Æö6·2ä6÷VçBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE67&öÆÅf–WvW"&Æö6²6÷VçB"“°¢f"67&öÆÅf–WvW%&w&‚Ò&WV—&UG—SÅ&w&ƒâ€¢67&öÆÅf–WvW$Fö7VÖVçBä&Æö6·2äf—'7D&Æö6²À¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE67&öÆÅf–WvW"&w&‚"“°¢76W'E&w&…FW‡B‡67&öÆÅf–WvW%&w&‚Â$W‡FW&æÂ67&öÆÂf–WvW"Fö7VÖVçB"Â'67&öÆÂf–WvW""“°¢f"67&öÆÅf–WvW$Æ—7BÒ&WV—&UG—SÅ7—7FVÒåv–æF÷w2äFö7VÖVçG2äÆ—7Câ€¢67&öÆÅf–WvW%&w&‚äæW‡D&Æö6²À¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE67&öÆÅf–WvW"Æ—7B"“°¢76W'DWVÂ…FW‡DÖ&¶W%7G–ÆRäF—62Â67&öÆÅf–WvW$Æ—7BäÖ&¶W%7G–ÆRÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE67&öÆÅf–WvW"Æ—7BÖ&¶W"7G–ÆR"“°¢76W'DÆ—7D—FVÕFW‡B‡67&öÆÅf–WvW$Æ—7BäÆ—7D—FV×2äf—'7DÆ—7D—FVÒÂ$W‡FW&æÂ67&öÆÂf–WvW"—FVÒ"Â'67&öÆÂf–WvW""“° ¢f"vUf–WvW"Ò&WV—&UG—SÄfÆ÷tFö7VÖVçEvUf–WvW#â€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄfÆ÷tFö7VÖVçEvUf–WvW""’À¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçEvUf–WvW""“°¢76W'DWVÂƒ#RãÂvUf–WvW"å¦ööÒÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçEvUf–WvW"¦ööÒ"“°¢76W'DWVÂƒSãÂvUf–WvW"äÖ–å¦ööÒÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçEvUf–WvW"Ö–â¦ööÒ"“°¢76W'DWVÂƒ#SãÂvUf–WvW"äÖ…¦ööÒÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçEvUf–WvW"Ö‚¦ööÒ"“°¢f"vUf–WvW$Fö7VÖVçBÒ&WV—&UG—SÄfÆ÷tFö7VÖVçCâ€¢vUf–WvW"äFö7VÖVçBÀ¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçEvUf–WvW"Fö7VÖVçB"“°¢76W'DWVÂƒRãÂvUf–WvW$Fö7VÖVçBåvUFF–æräÆVgBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçEvUf–WvW"vRFF–ær"“°¢76W'DWVÂƒ3cãÂvUf–WvW$Fö7VÖVçBä6öÇVÖåv–GF‚Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçEvUf–WvW"6öÇVÖâv–GF‚"“°¢76W'DWVÂƒ"ÂvUf–WvW$Fö7VÖVçBä&Æö6·2ä6÷VçBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçEvUf–WvW"&Æö6²6÷VçB"“°¢f"vUf–WvW%&w&‚Ò&WV—&UG—SÅ&w&ƒâ€¢vUf–WvW$Fö7VÖVçBä&Æö6·2äf—'7D&Æö6²À¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçEvUf–WvW"&w&‚"“°¢76W'E&w&…FW‡B‡vUf–WvW%&w&‚Â$W‡FW&æÂvRf–WvW"Fö7VÖVçB"Â'vRf–WvW""“°¢f"vUf–WvW$Æ—7BÒ&WV—&UG—SÅ7—7FVÒåv–æF÷w2äFö7VÖVçG2äÆ—7Câ€¢vUf–WvW%&w&‚äæW‡D&Æö6²À¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçEvUf–WvW"Æ—7B"“°¢76W'DWVÂ…FW‡DÖ&¶W%7G–ÆRå7V&RÂvUf–WvW$Æ—7BäÖ&¶W%7G–ÆRÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçEvUf–WvW"Æ—7BÖ&¶W"7G–ÆR"“°¢76W'DÆ—7D—FVÕFW‡B‡vUf–WvW$Æ—7BäÆ—7D—FV×2äf—'7DÆ—7D—FVÒÂ$W‡FW&æÂvRf–WvW"—FVÒ"Â'vRf–WvW""“°¢f"vUf–WvW%FW‡BÒæWrFW‡E&ævR‡vUf–WvW$Fö7VÖVçBä6öçFVçE7F'BÂvUf–WvW$Fö7VÖVçBä6öçFVçDVæB’åFW‡C°¢76W'D6öçF–ç2‚$W‡FW&æÂvRf–WvW"—FVÒ"ÂvUf–WvW%FW‡BÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçEvUf–WvW"FW‡E&ævRFW‡B"“° ¢f"&VFW"Ò&WV—&UG—SÄfÆ÷tFö7VÖVçE&VFW#â€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄfÆ÷tFö7VÖVçE&VFW""’À¢&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE&VFW""“°¢76W'DWVÂ„fÆ÷tFö7VÖVçE&VFW%f–Wv–ætÖöFRå67&öÆÂÂ&VFW"åf–Wv–ætÖöFRÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE&VFW"f–Wv–ærÖöFR"“°¢f"&VFW$Fö7VÖVçBÒ&VFW"äFö7VÖVç@¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡V7FVBW‡FW&æÂ4D²fÆ÷tFö7VÖVçE&VFW"Fö7VÖVçBâ"“°¢76W'DWVÂƒ2ãÂ&VFW$Fö7VÖVçBåvUFF–æräÆVgBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE&VFW"vRFF–ær"“°¢76W'DWVÂƒÂ&VFW$Fö7VÖVçBä&Æö6·2ä6÷VçBÂ&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE&VFW"&Æö6²6÷VçB"“°¢76W'E&w&…FW‡B€¢&WV—&UG—SÅ&w&ƒâ‡&VFW$Fö7VÖVçBä&Æö6·2äf—'7D&Æö6²Â&W‡FW&æÂ4D²fÆ÷tFö7VÖVçE&VFW"&w&‚"’À¢$W‡FW&æÂ&VFW"Fö7VÖVçB"À¢'&VFW""“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFU&–6…FW‡DVF—F–æt6öÖÖæG2€¢&–6…FW‡D&÷‚&–6…FW‡D&÷‚À¢&w&‚–çG&õ&w&‚À¢'VâÆ–å'VâÀ¢7—7FVÒåv–æF÷w2äFö7VÖVçG2äÆ—7BFö7VÖVçDÆ—7B¢°¢FW‡E6VÆV7F–öâ6VÆV7F–öâÒ&–6…FW‡D&÷‚å6VÆV7F–öã°¢6VÆV7F–öâå6VÆV7B‡Æ–å'Vâä6öçFVçE7F'BÂÆ–å'Vâä6öçFVçDVæB“°¢76W'D6öçF–ç2‚$W‡FW&æÂ"Â6VÆV7F–öâåFW‡BÂ&W‡FW&æÂ4D²&–6…FW‡D&÷‚6öÖÖæB6VÆV7F–öâFW‡B"“° ¢76W'DWVÂ‡G'VRÂVF—F–æt6öÖÖæG2åFövvÆT&öÆBä6äW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚’Â&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆT&öÆB6äW†V7WFR"“°¢VF—F–æt6öÖÖæG2åFövvÆT&öÆBäW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚“°¢76W'DWVÂ„föçEvV–v‡G2ä&öÆBÂ6VÆV7F–öâävWE&÷W'G•fÇVR…FW‡DVÆVÖVçBäföçEvV–v‡E&÷W'G’’Â&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆT&öÆBÆ–VBvV–v‡B"“°¢VF—F–æt6öÖÖæG2åFövvÆT&öÆBäW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚“°¢76W'DWVÂ„föçEvV–v‡G2äæ÷&ÖÂÂ6VÆV7F–öâävWE&÷W'G•fÇVR…FW‡DVÆVÖVçBäföçEvV–v‡E&÷W'G’’Â&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆT&öÆB&W7F÷&VBvV–v‡B"“° ¢76W'DWVÂ‡G'VRÂVF—F–æt6öÖÖæG2åFövvÆT—FÆ–2ä6äW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚’Â&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆT—FÆ–26äW†V7WFR"“°¢VF—F–æt6öÖÖæG2åFövvÆT—FÆ–2äW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚“°¢76W'DWVÂ„föçE7G–ÆW2ä—FÆ–2Â6VÆV7F–öâävWE&÷W'G•fÇVR…FW‡DVÆVÖVçBäföçE7G–ÆU&÷W'G’’Â&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆT—FÆ–2Æ–VB7G–ÆR"“°¢VF—F–æt6öÖÖæG2åFövvÆT—FÆ–2äW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚“°¢76W'DWVÂ„föçE7G–ÆW2äæ÷&ÖÂÂ6VÆV7F–öâävWE&÷W'G•fÇVR…FW‡DVÆVÖVçBäföçE7G–ÆU&÷W'G’’Â&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆT—FÆ–2&W7F÷&VB7G–ÆR"“° ¢76W'DWVÂ‡G'VRÂVF—F–æt6öÖÖæG2åFövvÆUVæFW&Æ–æRä6äW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚’Â&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆUVæFW&Æ–æR6äW†V7WFR"“°¢VF—F–æt6öÖÖæG2åFövvÆUVæFW&Æ–æRäW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚“°¢f"FV6÷&F–öç2Ò&WV—&UG—SÅFW‡DFV6÷&F–öä6öÆÆV7F–öãâ€¢6VÆV7F–öâävWE&÷W'G•fÇVR„–æÆ–æRåFW‡DFV6÷&F–öç5&÷W'G’’À¢&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆUVæFW&Æ–æRFV6÷&F–öç2"“°¢76W'DWVÂƒÂFV6÷&F–öç2ä6÷VçBÂ&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆUVæFW&Æ–æRFV6÷&F–öâ6÷VçB"“°¢76W'DWVÂ…FW‡DFV6÷&F–öäÆö6F–öâåVæFW&Æ–æRÂFV6÷&F–öç5³ÒäÆö6F–öâÂ&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆUVæFW&Æ–æRFV6÷&F–öâÆö6F–öâ"“°¢VF—F–æt6öÖÖæG2åFövvÆUVæFW&Æ–æRäW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚“°¢f"&W7F÷&VDFV6÷&F–öç2Ò&WV—&UG—SÅFW‡DFV6÷&F–öä6öÆÆV7F–öãâ€¢6VÆV7F–öâävWE&÷W'G•fÇVR„–æÆ–æRåFW‡DFV6÷&F–öç5&÷W'G’’À¢&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆUVæFW&Æ–æR&W7F÷&VBFV6÷&F–öç2"“°¢76W'DWVÂƒÂ&W7F÷&VDFV6÷&F–öç2ä6÷VçBÂ&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆUVæFW&Æ–æR&W7F÷&VBFV6÷&F–öâ6÷VçB"“° ¢6VÆV7F–öâå6VÆV7B†–çG&õ&w&‚ä6öçFVçE7F'BÂ–çG&õ&w&‚ä6öçFVçDVæB“°¢76W'DWVÂ‡G'VRÂVF—F–æt6öÖÖæG2äÆ–vå&–v‡Bä6äW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚’Â&W‡FW&æÂ4D²&–6…FW‡D&÷‚Æ–vå&–v‡B6äW†V7WFR"“°¢VF—F–æt6öÖÖæG2äÆ–vå&–v‡BäW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚“°¢76W'DWVÂ…FW‡DÆ–væÖVçBå&–v‡BÂ–çG&õ&w&‚åFW‡DÆ–væÖVçBÂ&W‡FW&æÂ4D²&–6…FW‡D&÷‚Æ–vå&–v‡B&w&‚Æ–væÖVçB"“°¢76W'DWVÂ‡G'VRÂVF—F–æt6öÖÖæG2äÆ–vä6VçFW"ä6äW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚’Â&W‡FW&æÂ4D²&–6…FW‡D&÷‚Æ–vä6VçFW"6äW†V7WFR"“°¢VF—F–æt6öÖÖæG2äÆ–vä6VçFW"äW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚“°¢76W'DWVÂ…FW‡DÆ–væÖVçBä6VçFW"Â–çG&õ&w&‚åFW‡DÆ–væÖVçBÂ&W‡FW&æÂ4D²&–6…FW‡D&÷‚Æ–vä6VçFW"&w&‚Æ–væÖVçB"“°¢76W'DWVÂ‡G'VRÂVF—F–æt6öÖÖæG2äÆ–väÆVgBä6äW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚’Â&W‡FW&æÂ4D²&–6…FW‡D&÷‚Æ–väÆVgB6äW†V7WFR"“°¢VF—F–æt6öÖÖæG2äÆ–väÆVgBäW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚“°¢76W'DWVÂ…FW‡DÆ–væÖVçBäÆVgBÂ–çG&õ&w&‚åFW‡DÆ–væÖVçBÂ&W‡FW&æÂ4D²&–6…FW‡D&÷‚Æ–väÆVgB&w&‚Æ–væÖVçB"“° ¢f"f—'7DÆ—7E&w&‚Ò&WV—&UG—SÅ&w&ƒâ€¢Fö7VÖVçDÆ—7BäÆ—7D—FV×2äf—'7DÆ—7D—FVÓòä&Æö6·2äf—'7D&Æö6²À¢&W‡FW&æÂ4D²&–6…FW‡D&÷‚Æ—7B6öÖÖæB&w&‚"“°¢6VÆV7F–öâå6VÆV7B†f—'7DÆ—7E&w&‚ä6öçFVçE7F'BÂf—'7DÆ—7E&w&‚ä6öçFVçDVæB“°¢76W'DWVÂ‡G'VRÂVF—F–æt6öÖÖæG2åFövvÆT'VÆÆWG2ä6äW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚’Â&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆT'VÆÆWG26äW†V7WFR"“°¢VF—F–æt6öÖÖæG2åFövvÆT'VÆÆWG2äW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚“°¢76W'DWVÂ…FW‡DÖ&¶W%7G–ÆRäF—62ÂFö7VÖVçDÆ—7BäÖ&¶W%7G–ÆRÂ&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆT'VÆÆWG2Ö&¶W"7G–ÆR"“°¢76W'DWVÂ‡G'VRÂVF—F–æt6öÖÖæG2åFövvÆTçVÖ&W&–ærä6äW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚’Â&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆTçVÖ&W&–ær6äW†V7WFR"“°¢VF—F–æt6öÖÖæG2åFövvÆTçVÖ&W&–æräW†V7WFR†çVÆÂÂ&–6…FW‡D&÷‚“°¢76W'DWVÂ…FW‡DÖ&¶W%7G–ÆRäFV6–ÖÂÂFö7VÖVçDÆ—7BäÖ&¶W%7G–ÆRÂ&W‡FW&æÂ4D²&–6…FW‡D&÷‚FövvÆTçVÖ&W&–ærÖ&¶W"7G–ÆR"“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFU7VÆÄ6†V6²„Ö–åv–æF÷rv–æF÷r¢°¢f"FW‡D&÷‚Ò&WV—&UG—SÅFW‡D&÷ƒâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ7VÆÄ6†V6µFW‡D&÷‚"’À¢&W‡FW&æÂ4D²7VÆÂÖ6†V6²FW‡B&÷‚"“°¢76W'DWVÂ‡G'VRÂ7VÆÄ6†V6²ävWD—4Væ&ÆVB‡FW‡D&÷‚’Â&W‡FW&æÂ4D²7VÆÄ6†V6²Væ&ÆVBGF6†VBfÇVR"“°¢76W'DWVÂ…7VÆÆ–æu&Vf÷&Òå&TæE÷7G&Vf÷&ÒÂFW‡D&÷‚å7VÆÄ6†V6²å7VÆÆ–æu&Vf÷&ÒÂ&W‡FW&æÂ4D²7VÆÄ6†V6²7VÆÆ–ær&Vf÷&Ò–ç7Fæ6RfÇVR"“° ¢f"F–7F–öæ&–W2Ò7VÆÄ6†V6²ävWD7W7FöÔF–7F–öæ&–W2‡FW‡D&÷‚“°¢76W'DWVÂƒÂF–7F–öæ&–W2ä6÷VçBÂ&W‡FW&æÂ4D²7VÆÄ6†V6²–æ—F–Â7W7FöÒF–7F–öæ'’6÷VçB"“°¢F–7F–öæ&–W2äFB†æWrW&’‚&W‡FW&æÂ×6F²Ö7W7FöÒæÆW‚"ÂW&”¶–æBå&VÆF—fR’“°¢76W'DWVÂƒÂF–7F–öæ&–W2ä6÷VçBÂ&W‡FW&æÂ4D²7VÆÄ6†V6²7W7FöÒF–7F–öæ'’FB6÷VçB"“°¢F–7F–öæ&–W2äFB†æWrW&’‚&W‡FW&æÂ×6F²Ö7W7FöÒæÆW‚"ÂW&”¶–æBå&VÆF—fR’“°¢76W'DWVÂƒÂF–7F–öæ&–W2ä6÷VçBÂ&W‡FW&æÂ4D²7VÆÄ6†V6²GWÆ–6FR7W7FöÒF–7F–öæ'’6÷VçB"“° ¢76W'DWVÂ†çVÆÂÂFW‡D&÷‚ävWE7VÆÆ–ætW'&÷"ƒ’Â&W‡FW&æÂ4D²7VÆÄ6†V6²æòÖ÷7VÆÆ–ærW'&÷"&W7VÇB"“°¢76W'DWVÂ‚ÓÂFW‡D&÷‚ävWE7VÆÆ–ætW'&÷%7F'Bƒ’Â&W‡FW&æÂ4D²7VÆÄ6†V6²æòÖ÷7VÆÆ–ærW'&÷"7F'B"“°¢76W'DWVÂƒÂFW‡D&÷‚ävWE7VÆÆ–ætW'&÷$ÆVæwF‚ƒ’Â&W‡FW&æÂ4D²7VÆÄ6†V6²æòÖ÷7VÆÆ–ærW'&÷"ÆVæwF‚"“°¢76W'DWVÂ‚ÓÂFW‡D&÷‚ävWDæW‡E7VÆÆ–ætW'&÷$6†&7FW$–æFW‚ƒÂÆöv–6ÄF—&V7F–öâäf÷'v&B’Â&W‡FW&æÂ4D²7VÆÄ6†V6²æòÖ÷æW‡B7VÆÆ–ærW'&÷""“° ¢F–7F–öæ&–W2ä6ÆV"‚“°¢76W'DWVÂƒÂF–7F–öæ&–W2ä6÷VçBÂ&W‡FW&æÂ4D²7VÆÄ6†V6²7W7FöÒF–7F–öæ'’6ÆV"6÷VçB"“°¢7VÆÄ6†V6²å6WD—4Væ&ÆVB‡FW‡D&÷‚ÂfÇ6R“°¢76W'DWVÂ†fÇ6RÂFW‡D&÷‚å7VÆÄ6†V6²ä—4Væ&ÆVBÂ&W‡FW&æÂ4D²7VÆÄ6†V6²F—6&ÆVB–ç7Fæ6RfÇVR"“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFT6öÖÖæG4æDfö7W2„Ö–åv–æF÷rv–æF÷r¢°¢76W'DWVÂƒRÂv–æF÷rä6öÖÖæD&–æF–æw2ä6÷VçBÂ&W‡FW&æÂ4D²6öÖÖæB&–æF–ær6÷VçB"“°¢f"6öÖÖæD&–æF–ærÒ&WV—&UG—SÄ6öÖÖæD&–æF–æsâ€¢v–æF÷rä6öÖÖæD&–æF–æw5³ÒÀ¢&W‡FW&æÂ4D²6öÖÖæB&–æF–ær"“°¢76W'DWVÂ„Ö–åv–æF÷räW‡FW&æÄ6öÖÖæBÂ6öÖÖæD&–æF–ærä6öÖÖæBÂ&W‡FW&æÂ4D²6öÖÖæB&–æF–ær6öÖÖæB"“° ¢76W'DWVÂƒ"Âv–æF÷rä–çWD&–æF–æw2ä6÷VçBÂ&W‡FW&æÂ4D²–çWB&–æF–ær6÷VçB"“°¢f"¶W”&–æF–ærÒ&WV—&UG—SÄ¶W”&–æF–æsâ€¢v–æF÷rä–çWD&–æF–æw5³ÒÀ¢&W‡FW&æÂ4D²¶W’&–æF–ær"“°¢76W'DWVÂ„Ö–åv–æF÷räW‡FW&æÄ6öÖÖæBÂ¶W”&–æF–ærä6öÖÖæBÂ&W‡FW&æÂ4D²¶W’&–æF–ær6öÖÖæB"“°¢76W'DWVÂ„¶W’äRÂ¶W”&–æF–ærä¶W’Â&W‡FW&æÂ4D²¶W’&–æF–ær¶W’"“°¢76W'DWVÂ„ÖöF–f–W$¶W—2ä6öçG&öÂÂ¶W”&–æF–æräÖöF–f–W'2Â&W‡FW&æÂ4D²¶W’&–æF–ærÖöF–f–W'2"“°¢f"Ö÷W6T&–æF–ærÒ&WV—&UG—SÄÖ÷W6T&–æF–æsâ€¢v–æF÷rä–çWD&–æF–æw5³ÒÀ¢&W‡FW&æÂ4D²Ö÷W6R&–æF–ær"“°¢76W'DWVÂ„Ö–åv–æF÷räW‡FW&æÄ6öÖÖæBÂÖ÷W6T&–æF–ærä6öÖÖæBÂ&W‡FW&æÂ4D²Ö÷W6R&–æF–ær6öÖÖæB"“°¢76W'DWVÂ‚$W‡FW&æÄÖ÷W6T6öÖÖæE&ÖWFW""ÂÖ÷W6T&–æF–ærä6öÖÖæE&ÖWFW"Â&W‡FW&æÂ4D²Ö÷W6R&–æF–ær6öÖÖæB&ÖWFW""“°¢f"Ö÷W6TvW7GW&RÒ&WV—&UG—SÄÖ÷W6TvW7GW&Sâ€¢Ö÷W6T&–æF–ærävW7GW&RÀ¢&W‡FW&æÂ4D²Ö÷W6RvW7GW&R"“°¢76W'DWVÂ„Ö÷W6T7F–öâäÆVgDF÷V&ÆT6Æ–6²ÂÖ÷W6TvW7GW&RäÖ÷W6T7F–öâÂ&W‡FW&æÂ4D²Ö÷W6RvW7GW&R7F–öâ"“°¢76W'DWVÂ„ÖöF–f–W$¶W—2äæöæRÂÖ÷W6TvW7GW&RäÖöF–f–W'2Â&W‡FW&æÂ4D²Ö÷W6RvW7GW&RÖöF–f–W'2"“° ¢f"fö7W5æVÂÒ&WV—&UG—SÅ7F6µæVÃâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄfö7W5æVÂ"’À¢&W‡FW&æÂ4D²fö7W2æVÂ"“°¢f"6öÖÖæD'WGFöâÒ&WV—&UG—SÄ'WGFöãâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ6öÖÖæD'WGFöâ"’À¢&W‡FW&æÂ4D²6öÖÖæB'WGFöâ"“°¢f"6Æ746öÖÖæEF&vWBÒ&WV—&UG—SÄW‡FW&æÄ6Æ746öÖÖæEFW‡D&÷ƒâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ6Æ746öÖÖæEF&vWB"’À¢&W‡FW&æÂ4D²6Æ726öÖÖæBF&vWB"“°¢f"6Æ746öÖÖæD'WGFöâÒ&WV—&UG—SÄ'WGFöãâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ6Æ746öÖÖæD'WGFöâ"’À¢&W‡FW&æÂ4D²6Æ726öÖÖæB'WGFöâ"“°¢f"&WVW'”6öÖÖæD'WGFöâÒ&WV—&UG—SÄ'WGFöãâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ&WVW'”6öÖÖæD'WGFöâ"’À¢&W‡FW&æÂ4D²&WVW'’6öÖÖæB'WGFöâ"“°¢f"7—7FVÔÖ†–Ö—¦T'WGFöâÒ&WV—&UG—SÄ'WGFöãâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ7—7FVÔÖ†–Ö—¦T'WGFöâ"’À¢&W‡FW&æÂ4D²7—7FVÒÖ†–Ö—¦R6öÖÖæB'WGFöâ"“°¢f"7—7FVÔÖ–æ–Ö—¦T'WGFöâÒ&WV—&UG—SÄ'WGFöãâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ7—7FVÔÖ–æ–Ö—¦T'WGFöâ"’À¢&W‡FW&æÂ4D²7—7FVÒÖ–æ–Ö—¦R6öÖÖæB'WGFöâ"“°¢f"7—7FVÕ&W7F÷&T'WGFöâÒ&WV—&UG—SÄ'WGFöãâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ7—7FVÕ&W7F÷&T'WGFöâ"’À¢&W‡FW&æÂ4D²7—7FVÒ&W7F÷&R6öÖÖæB'WGFöâ"“°¢f"7—7FVÔÖVçT'WGFöâÒ&WV—&UG—SÄ'WGFöãâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ7—7FVÔÖVçT'WGFöâ"’À¢&W‡FW&æÂ4D²7—7FVÒÖVçR6öÖÖæB'WGFöâ"“°¢f"fÆ–FF–öåFW‡D&÷‚Ò&WV—&UG—SÅFW‡D&÷ƒâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅfÆ–FF–öåFW‡D&÷‚"’À¢&W‡FW&æÂ4D²66W72Ö¶W’F&vWBFW‡B&÷‚"“°¢f"66W74Æ&VÂÒ&WV—&UG—SÄÆ&VÃâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ66W74Æ&VÂ"’À¢&W‡FW&æÂ4D²66W72Æ&VÂ"“°¢f"7FæFÆöæT66W75FW‡BÒ&WV—&UG—SÄ66W75FW‡Câ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ7FæFÆöæT66W75FW‡B"’À¢&W‡FW&æÂ4D²7FæFÆöæR66W72FW‡B"“°¢f"¶W–&ö&Dæf–vF–öåæVÂÒ&WV—&UG—SÅ7F6µæVÃâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ¶W–&ö&Dæf–vF–öåæVÂ"’À¢&W‡FW&æÂ4D²¶W–&ö&Bæf–vF–öâæVÂ"“°¢f"f—'7D¶W–&ö&Dæf–vF–öä'WGFöâÒ&WV—&UG—SÄ'WGFöãâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ¶W–&ö&Dæf–vF–öäf—'7D'WGFöâ"’À¢&W‡FW&æÂ4D²f—'7B¶W–&ö&Bæf–vF–öâ'WGFöâ"“°¢f"6V6öæD¶W–&ö&Dæf–vF–öä'WGFöâÒ&WV—&UG—SÄ'WGFöãâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ¶W–&ö&Dæf–vF–öå6V6öæD'WGFöâ"’À¢&W‡FW&æÂ4D²6V6öæB¶W–&ö&Bæf–vF–öâ'WGFöâ"“°¢76W'DWVÂ†6öÖÖæD'WGFöâÂfö7W4ÖævW"ävWDfö7W6VDVÆVÖVçB†fö7W5æVÂ’Â&W‡FW&æÂ4D²fö7W2ÖævW"fö7W6VBVÆVÖVçB"“°¢76W'DWVÂ‡G'VRÂfö7W4ÖævW"ävWD—4fö7W566÷R†fö7W5æVÂ’Â&W‡FW&æÂ4D²fö7W2ÖævW"66÷RfÆr"“°¢76W'DWVÂ„¶W–&ö&Dæf–vF–öäÖöFRä7–6ÆRÂ¶W–&ö&Dæf–vF–öâävWEF$æf–vF–öâ†fö7W5æVÂ’Â&W‡FW&æÂ4D²F"æf–vF–öâÖöFR"“°¢76W'DWVÂ„¶W–&ö&Dæf–vF–öäÖöFRä7–6ÆRÂ¶W–&ö&Dæf–vF–öâävWD6öçG&öÅF$æf–vF–öâ†fö7W5æVÂ’Â&W‡FW&æÂ4D²6öçG&öÂ×F"æf–vF–öâÖöFR"“°¢76W'DWVÂ„¶W–&ö&Dæf–vF–öäÖöFRä6öçF–æVBÂ¶W–&ö&Dæf–vF–öâävWDF—&V7F–öæÄæf–vF–öâ†fö7W5æVÂ’Â&W‡FW&æÂ4D²F—&V7F–öæÂæf–vF–öâÖöFR"“°¢76W'DWVÂ„¶W–&ö&Dæf–vF–öäÖöFRä7–6ÆRÂ¶W–&ö&Dæf–vF–öâävWEF$æf–vF–öâ†¶W–&ö&Dæf–vF–öåæVÂ’Â&W‡FW&æÂ4D²æW7FVB¶W–&ö&Bæf–vF–öâÖöFR"“°¢76W'DWVÂ‚$W‡FW&æÂæf–vF–öâf—'7B"Âf—'7D¶W–&ö&Dæf–vF–öä'WGFöâä6öçFVçBÂ&W‡FW&æÂ4D²f—'7B¶W–&ö&Bæf–vF–öâ'WGFöâ6öçFVçB"“°¢76W'DWVÂ‚$W‡FW&æÂæf–vF–öâ6V6öæB"Â6V6öæD¶W–&ö&Dæf–vF–öä'WGFöâä6öçFVçBÂ&W‡FW&æÂ4D²6V6öæB¶W–&ö&Bæf–vF–öâ'WGFöâ6öçFVçB"“°¢76W'DWVÂ‡fÆ–FF–öåFW‡D&÷‚Â66W74Æ&VÂåF&vWBÂ&W‡FW&æÂ4D²Æ&VÂ66W72Ö¶W’F&vWB"“°¢76W'DWVÂ‚%ôW‡FW&æÂ66W72F&vWB"Â66W74Æ&VÂä6öçFVçBÂ&W‡FW&æÂ4D²Æ&VÂ66W72Ö¶W’6öçFVçB"“°¢76W'DWVÂ‚%ôW‡FW&æÂ7FæFÆöæR66W72"Â7FæFÆöæT66W75FW‡BåFW‡BÂ&W‡FW&æÂ4D²7FæFÆöæR66W72FW‡B"“°¢76W'DWVÂ‚$W‡FW&æÅfÆ–FF–öåFW‡D&÷„WFöÖF–öâ"ÂWFöÖF–öå&÷W'F–W2ävWDWFöÖF–öä–B‡fÆ–FF–öåFW‡D&÷‚’Â&W‡FW&æÂ4D²WFöÖF–öâ–B"“°¢76W'DWVÂ‚$W‡FW&æÂfÆ–FF–öâ–çWB"ÂWFöÖF–öå&÷W'F–W2ävWDæÖR‡fÆ–FF–öåFW‡D&÷‚’Â&W‡FW&æÂ4D²WFöÖF–öâæÖR"“°¢76W'DWVÂ‚$W‡FW&æÂ4D²fÆ–FF–öâFW‡B"ÂWFöÖF–öå&÷W'F–W2ävWD†VÇFW‡B‡fÆ–FF–öåFW‡D&÷‚’Â&W‡FW&æÂ4D²WFöÖF–öâ†VÇFW‡B"“°¢76W'DWVÂ†66W74Æ&VÂÂWFöÖF–öå&÷W'F–W2ävWDÆ&VÆVD'’‡fÆ–FF–öåFW‡D&÷‚’Â&W‡FW&æÂ4D²WFöÖF–öâÆ&VÆVBÖ'’VÆVÖVçB"“°¢f"Æ&VÅVW"Ò&WV—&UG—SÄÆ&VÄWFöÖF–öåVW#â€¢T”VÆVÖVçDWFöÖF–öåVW"ä7&VFUVW$f÷$VÆVÖVçB†66W74Æ&VÂ’À¢&W‡FW&æÂ4D²Æ&VÂWFöÖF–öâVW""“°¢f"fÆ–FF–öåVW"Ò&WV—&UG—SÅFW‡D&÷„WFöÖF–öåVW#â€¢T”VÆVÖVçDWFöÖF–öåVW"ä7&VFUVW$f÷$VÆVÖVçB‡fÆ–FF–öåFW‡D&÷‚’À¢&W‡FW&æÂ4D²FW‡B&÷‚WFöÖF–öâVW""“°¢76W'DWVÂ‚$W‡FW&æÅfÆ–FF–öåFW‡D&÷„WFöÖF–öâ"ÂfÆ–FF–öåVW"ävWDWFöÖF–öä–B‚’Â&W‡FW&æÂ4D²WFöÖF–öâVW"–B"“°¢76W'DWVÂ‚$W‡FW&æÂfÆ–FF–öâ–çWB"ÂfÆ–FF–öåVW"ävWDæÖR‚’Â&W‡FW&æÂ4D²WFöÖF–öâVW"æÖR"“°¢76W'DWVÂ‚$W‡FW&æÂ4D²fÆ–FF–öâFW‡B"ÂfÆ–FF–öåVW"ävWD†VÇFW‡B‚’Â&W‡FW&æÂ4D²WFöÖF–öâVW"†VÇFW‡B"“°¢76W'DWVÂ†Æ&VÅVW"ÂfÆ–FF–öåVW"ävWDÆ&VÆVD'’‚’Â&W‡FW&æÂ4D²WFöÖF–öâVW"Æ&VÆVBÖ'’VW""“°¢76W'DWVÂ†66W74Æ&VÂÂÆ&VÅVW"ä÷væW"Â&W‡FW&æÂ4D²Æ&VÂWFöÖF–öâVW"÷væW""“°¢fÆ–FFTWFöÖF–öåGFW&å&÷f–FW'2‡v–æF÷rÂ6öÖÖæD'WGFöâÂfÆ–FF–öåFW‡D&÷‚“°¢76W'DWVÂ„Ö–åv–æF÷räW‡FW&æÄ6öÖÖæBÂ6öÖÖæD'WGFöâä6öÖÖæBÂ&W‡FW&æÂ4D²6öÖÖæB'WGFöâ6öÖÖæB"“°¢76W'DWVÂ‚$W‡FW&æÄ6öÖÖæE&ÖWFW""Â6öÖÖæD'WGFöâä6öÖÖæE&ÖWFW"Â&W‡FW&æÂ4D²6öÖÖæB'WGFöâ&ÖWFW""“°¢76W'DWVÂ„W‡FW&æÄ6Æ746öÖÖæEFW‡D&÷‚äW‡FW&æÄ6Æ746öÖÖæBÂ6Æ746öÖÖæD'WGFöâä6öÖÖæBÂ&W‡FW&æÂ4D²6Æ726öÖÖæB'WGFöâ6öÖÖæB"“°¢76W'DWVÂ‚$W‡FW&æÄ6Æ746öÖÖæE&ÖWFW""Â6Æ746öÖÖæD'WGFöâä6öÖÖæE&ÖWFW"Â&W‡FW&æÂ4D²6Æ726öÖÖæB'WGFöâ&ÖWFW""“°¢76W'DWVÂ†6Æ746öÖÖæEF&vWBÂ6Æ746öÖÖæD'WGFöâä6öÖÖæEF&vWBÂ&W‡FW&æÂ4D²6Æ726öÖÖæB'WGFöâF&vWB"“° ¢–çB6äW†V7WFT&Vf÷&RÒv–æF÷räW‡FW&æÄ6öÖÖæD6äW†V7WFT6÷VçC°¢–çBW†V7WFVD&Vf÷&RÒv–æF÷räW‡FW&æÄ6öÖÖæDW†V7WFVD6÷VçC°¢Ö–åv–æF÷räW‡FW&æÄ6öÖÖæBäW†V7WFR‚$F—&V7D6öÖÖæE&ÖWFW""Â6öÖÖæD'WGFöâ“°¢76W'DDÆV7B†6äW†V7WFT&Vf÷&R²Âv–æF÷räW‡FW&æÄ6öÖÖæD6äW†V7WFT6÷VçBÂ&W‡FW&æÂ4D²F—&V7B6öÖÖæB6âÖW†V7WFR6÷VçB"“°¢76W'DWVÂ†W†V7WFVD&Vf÷&R²Âv–æF÷räW‡FW&æÄ6öÖÖæDW†V7WFVD6÷VçBÂ&W‡FW&æÂ4D²F—&V7B6öÖÖæBW†V7WFVB6÷VçB"“°¢76W'DWVÂ‚$F—&V7D6öÖÖæE&ÖWFW""Âv–æF÷räÆ7DW‡FW&æÄ6öÖÖæE&ÖWFW"Â&W‡FW&æÂ4D²F—&V7B6öÖÖæB&ÖWFW""“°¢76W'DWVÂ†æÖVöb„Ö–åv–æF÷räW‡FW&æÄ6öÖÖæB’Âv–æF÷räÆ7DW‡FW&æÄ6öÖÖæDæÖRÂ&W‡FW&æÂ4D²6öÖÖæBæÖR"“° ¢–çBÖ÷W6TW†V7WFVD&Vf÷&RÒv–æF÷räW‡FW&æÄ6öÖÖæDW†V7WFVD6÷VçC°¢&WV—&UG—SÅ&÷WFVD6öÖÖæCâ€¢Ö÷W6T&–æF–ærä6öÖÖæBÀ¢&W‡FW&æÂ4D²Ö÷W6R&–æF–ær&÷WFVB6öÖÖæB"¢äW†V7WFR†Ö÷W6T&–æF–ærä6öÖÖæE&ÖWFW"Â6öÖÖæD'WGFöâ“°¢76W'DWVÂ†Ö÷W6TW†V7WFVD&Vf÷&R²Âv–æF÷räW‡FW&æÄ6öÖÖæDW†V7WFVD6÷VçBÂ&W‡FW&æÂ4D²Ö÷W6R&–æF–ær6öÖÖæBW†V7WFVB6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÄÖ÷W6T6öÖÖæE&ÖWFW""Âv–æF÷räÆ7DW‡FW&æÄ6öÖÖæE&ÖWFW"Â&W‡FW&æÂ4D²Ö÷W6R&–æF–ærW†V7WFVB&ÖWFW""“° ¢–çB6Æ–6´&Vf÷&RÒv–æF÷räW‡FW&æÄ6öÖÖæD'WGFöä6Æ–6´6÷VçC°¢–çB'WGFöäW†V7WFVD&Vf÷&RÒv–æF÷räW‡FW&æÄ6öÖÖæDW†V7WFVD6÷VçC°¢6öÖÖæD'WGFöâå&—6TWfVçB†æWr&÷WFVDWfVçD&w2„'WGFöä&6Rä6Æ–6´WfVçBÂ6öÖÖæD'WGFöâ’“°¢76W'DWVÂ†6Æ–6´&Vf÷&R²Âv–æF÷räW‡FW&æÄ6öÖÖæD'WGFöä6Æ–6´6÷VçBÂ&W‡FW&æÂ4D²vVæW&FVB'WGFöâ6Æ–6²6÷VçB"“°¢&WV—&UG—SÅ&÷WFVD6öÖÖæCâ€¢6öÖÖæD'WGFöâä6öÖÖæBÀ¢&W‡FW&æÂ4D²6öÖÖæB'WGFöâ&÷WFVB6öÖÖæB"¢äW†V7WFR†6öÖÖæD'WGFöâä6öÖÖæE&ÖWFW"Â6öÖÖæD'WGFöâ“°¢76W'DWVÂ†'WGFöäW†V7WFVD&Vf÷&R²Âv–æF÷räW‡FW&æÄ6öÖÖæDW†V7WFVD6÷VçBÂ&W‡FW&æÂ4D²'WGFöâ6öÖÖæBW†V7WFVB6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÄ6öÖÖæE&ÖWFW""Âv–æF÷räÆ7DW‡FW&æÄ6öÖÖæE&ÖWFW"Â&W‡FW&æÂ4D²'WGFöâ6öÖÖæB&ÖWFW""“° ¢–çB6Æ746äW†V7WFT&Vf÷&RÒ6Æ746öÖÖæEF&vWBä6Æ746öÖÖæD6äW†V7WFT6÷VçC°¢–çB6Æ74W†V7WFVD&Vf÷&RÒ6Æ746öÖÖæEF&vWBä6Æ746öÖÖæDW†V7WFVD6÷VçC°¢&WV—&UG—SÅ&÷WFVD6öÖÖæCâ€¢6Æ746öÖÖæD'WGFöâä6öÖÖæBÀ¢&W‡FW&æÂ4D²6Æ726öÖÖæB&÷WFVB6öÖÖæB"¢äW†V7WFR†6Æ746öÖÖæD'WGFöâä6öÖÖæE&ÖWFW"Â6Æ746öÖÖæEF&vWB“°¢76W'DDÆV7B†6Æ746äW†V7WFT&Vf÷&R²Â6Æ746öÖÖæEF&vWBä6Æ746öÖÖæD6äW†V7WFT6÷VçBÂ&W‡FW&æÂ4D²6Æ726öÖÖæB6âÖW†V7WFR6÷VçB"“°¢76W'DWVÂ†6Æ74W†V7WFVD&Vf÷&R²Â6Æ746öÖÖæEF&vWBä6Æ746öÖÖæDW†V7WFVD6÷VçBÂ&W‡FW&æÂ4D²6Æ726öÖÖæBW†V7WFVB6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÄ6Æ746öÖÖæE&ÖWFW""Â6Æ746öÖÖæEF&vWBäÆ7D6Æ746öÖÖæE&ÖWFW"Â&W‡FW&æÂ4D²6Æ726öÖÖæB&ÖWFW""“°¢76W'DWVÂ†æÖVöb„W‡FW&æÄ6Æ746öÖÖæEFW‡D&÷‚äW‡FW&æÄ6Æ746öÖÖæB’Â6Æ746öÖÖæEF&vWBäÆ7D6Æ746öÖÖæDæÖRÂ&W‡FW&æÂ4D²6Æ726öÖÖæBæÖR"“° ¢fÆ–FFU7—7FVÔ6öÖÖæD'WGFöâ€¢v–æF÷rÀ¢7—7FVÔÖ†–Ö—¦T'WGFöâÀ¢7—7FVÔ6öÖÖæG2äÖ†–Ö—¦Uv–æF÷t6öÖÖæBÀ¢$W‡FW&æÅ7—7FVÔÖ†–Ö—¦U&ÖWFW""À¢v–æF÷u7FFRäÖ†–Ö—¦VBÀ¢&W‡FW&æÂ4D²„ÔÂ7—7FVÔ6öÖÖæG2Ö†–Ö—¦R"“°¢fÆ–FFU7—7FVÔ6öÖÖæD'WGFöâ€¢v–æF÷rÀ¢7—7FVÔÖ–æ–Ö—¦T'WGFöâÀ¢7—7FVÔ6öÖÖæG2äÖ–æ–Ö—¦Uv–æF÷t6öÖÖæBÀ¢$W‡FW&æÅ7—7FVÔÖ–æ–Ö—¦U&ÖWFW""À¢v–æF÷u7FFRäÖ–æ–Ö—¦VBÀ¢&W‡FW&æÂ4D²„ÔÂ7—7FVÔ6öÖÖæG2Ö–æ–Ö—¦R"“°¢fÆ–FFU7—7FVÔ6öÖÖæD'WGFöâ€¢v–æF÷rÀ¢7—7FVÕ&W7F÷&T'WGFöâÀ¢7—7FVÔ6öÖÖæG2å&W7F÷&Uv–æF÷t6öÖÖæBÀ¢$W‡FW&æÅ7—7FVÕ&W7F÷&U&ÖWFW""À¢v–æF÷u7FFRäæ÷&ÖÂÀ¢&W‡FW&æÂ4D²„ÔÂ7—7FVÔ6öÖÖæG2&W7F÷&R"“°¢fÆ–FFU7—7FVÔ6öÖÖæD'WGFöâ€¢v–æF÷rÀ¢7—7FVÔÖVçT'WGFöâÀ¢7—7FVÔ6öÖÖæG2å6†÷u7—7FVÔÖVçT6öÖÖæBÀ¢$W‡FW&æÅ7—7FVÔÖVçU&ÖWFW""À¢v–æF÷u7FFRäæ÷&ÖÂÀ¢&W‡FW&æÂ4D²„ÔÂ7—7FVÔ6öÖÖæG2ÖVçR"“° ¢76W'DWVÂ‡v–æF÷räW‡FW&æÅ&WVW'”6öÖÖæBÂ&WVW'”6öÖÖæD'WGFöâä6öÖÖæBÂ&W‡FW&æÂ4D²&WVW'’6öÖÖæB&–æF–ær"“°¢76W'DWVÂ‚$W‡FW&æÅ&WVW'•&ÖWFW""Â&WVW'”6öÖÖæD'WGFöâä6öÖÖæE&ÖWFW"Â&W‡FW&æÂ4D²&WVW'’6öÖÖæB&ÖWFW""“°¢v–æF÷räW‡FW&æÅ&WVW'”6öÖÖæBä6äW†V7WFUfÇVRÒfÇ6S°¢6öÖÖæDÖævW"ä–çfÆ–FFU&WVW'•7VvvW7FVB‚“°¢G&–äF—7F6†W"‚“°¢76W'DWVÂ†fÇ6RÂ&WVW'”6öÖÖæD'WGFöâä—4Væ&ÆVBÂ&W‡FW&æÂ4D²&WVW'’6öÖÖæBF—6&ÆVB7FFR"“° ¢–çB&WVW'•&ö&T&Vf÷&RÒv–æF÷räW‡FW&æÅ&WVW'”6öÖÖæBä6äW†V7WFU&ö&T6÷VçC°¢v–æF÷räW‡FW&æÅ&WVW'”6öÖÖæBä6äW†V7WFUfÇVRÒG'VS°¢6öÖÖæDÖævW"ä–çfÆ–FFU&WVW'•7VvvW7FVB‚“°¢G&–äF—7F6†W"‚“°¢76W'DWVÂ‡G'VRÂ&WVW'”6öÖÖæD'WGFöâä—4Væ&ÆVBÂ&W‡FW&æÂ4D²&WVW'’6öÖÖæBVæ&ÆVB7FFR"“°¢76W'DDÆV7B€¢&WVW'•&ö&T&Vf÷&R²À¢v–æF÷räW‡FW&æÅ&WVW'”6öÖÖæBä6äW†V7WFU&ö&T6÷VçBÀ¢&W‡FW&æÂ4D²&WVW'’6öÖÖæB6âÖW†V7WFR&ö&R6÷VçB"“° ¢–çB&WVW'”W†V7WFT&Vf÷&RÒv–æF÷räW‡FW&æÅ&WVW'”6öÖÖæBäW†V7WFT6÷VçC°¢&WV—&UG—SÄ”6öÖÖæCâ€¢&WVW'”6öÖÖæD'WGFöâä6öÖÖæBÀ¢&W‡FW&æÂ4D²&WVW'’6öÖÖæB–çFW&f6R"¢äW†V7WFR‡&WVW'”6öÖÖæD'WGFöâä6öÖÖæE&ÖWFW"“°¢76W'DWVÂ‡&WVW'”W†V7WFT&Vf÷&R²Âv–æF÷räW‡FW&æÅ&WVW'”6öÖÖæBäW†V7WFT6÷VçBÂ&W‡FW&æÂ4D²&WVW'’6öÖÖæBW†V7WFR6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÅ&WVW'•&ÖWFW""Âv–æF÷räW‡FW&æÅ&WVW'”6öÖÖæBäÆ7E&ÖWFW"Â&W‡FW&æÂ4D²&WVW'’6öÖÖæBÆ7B&ÖWFW""“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFTWFöÖF–öåGFW&å&÷f–FW'2€¢Ö–åv–æF÷rv–æF÷rÀ¢'WGFöâ6öÖÖæD'WGFöâÀ¢FW‡D&÷‚fÆ–FF–öåFW‡D&÷‚¢°¢f"6öÖÖæD'WGFöåVW"Ò&WV—&UG—SÄ'WGFöäWFöÖF–öåVW#â€¢T”VÆVÖVçDWFöÖF–öåVW"ä7&VFUVW$f÷$VÆVÖVçB†6öÖÖæD'WGFöâ’À¢&W‡FW&æÂ4D²6öÖÖæB'WGFöâWFöÖF–öâVW""“°¢f"–çfö¶U&÷f–FW"Ò&WV—&UG—SÄ”–çfö¶U&÷f–FW#â€¢6öÖÖæD'WGFöåVW"ävWEGFW&â…GFW&ä–çFW&f6Rä–çfö¶R’À¢&W‡FW&æÂ4D²6öÖÖæB'WGFöâ–çfö¶R&÷f–FW""“°¢–çBWFöÖF–öä6Æ–6´&Vf÷&RÒv–æF÷räW‡FW&æÄ6öÖÖæD'WGFöä6Æ–6´6÷VçC°¢–çBWFöÖF–öäW†V7WFVD&Vf÷&RÒv–æF÷räW‡FW&æÄ6öÖÖæDW†V7WFVD6÷VçC°¢–çfö¶U&÷f–FW"ä–çfö¶R‚“°¢G&–äF—7F6†W"‚“°¢76W'DWVÂ†WFöÖF–öä6Æ–6´&Vf÷&R²Âv–æF÷räW‡FW&æÄ6öÖÖæD'WGFöä6Æ–6´6÷VçBÂ&W‡FW&æÂ4D²WFöÖF–öâ–çfö¶R6Æ–6²6÷VçB"“°¢76W'DWVÂ†WFöÖF–öäW†V7WFVD&Vf÷&R²Âv–æF÷räW‡FW&æÄ6öÖÖæDW†V7WFVD6÷VçBÂ&W‡FW&æÂ4D²WFöÖF–öâ–çfö¶R6öÖÖæB6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÄ6öÖÖæE&ÖWFW""Âv–æF÷räÆ7DW‡FW&æÄ6öÖÖæE&ÖWFW"Â&W‡FW&æÂ4D²WFöÖF–öâ–çfö¶R6öÖÖæB&ÖWFW""“° ¢f"fÆ–FF–öåVW"Ò&WV—&UG—SÅFW‡D&÷„WFöÖF–öåVW#â€¢T”VÆVÖVçDWFöÖF–öåVW"ä7&VFUVW$f÷$VÆVÖVçB‡fÆ–FF–öåFW‡D&÷‚’À¢&W‡FW&æÂ4D²FW‡B&÷‚WFöÖF–öâVW"f÷"fÇVRGFW&â"“°¢f"fÇVU&÷f–FW"Ò&WV—&UG—SÄ•fÇVU&÷f–FW#â€¢fÆ–FF–öåVW"ävWEGFW&â…GFW&ä–çFW&f6RåfÇVR’À¢&W‡FW&æÂ4D²FW‡B&÷‚fÇVR&÷f–FW""“°¢76W'DWVÂ†fÇ6RÂfÇVU&÷f–FW"ä—5&VDöæÇ’Â&W‡FW&æÂ4D²WFöÖF–öâfÇVR&÷f–FW"&VBÖöæÇ’7FFR"“°¢fÇVU&÷f–FW"å6WEfÇVR‚&W‡FW&æÂWFöÖF–öâfÇVR"“°¢76W'DWVÂ‚&W‡FW&æÂWFöÖF–öâfÇVR"ÂfÇVU&÷f–FW"åfÇVRÂ&W‡FW&æÂ4D²WFöÖF–öâfÇVR&÷f–FW"fÇVR"“°¢76W'DWVÂ‚&W‡FW&æÂWFöÖF–öâfÇVR"ÂfÆ–FF–öåFW‡D&÷‚åFW‡BÂ&W‡FW&æÂ4D²WFöÖF–öâfÇVR&÷f–FW"FW‡B&÷‚FW‡B"“° ¢f"6†V6´&÷‚Ò&WV—&UG—SÄ6†V6´&÷ƒâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ6†V6´&÷‚"’À¢&W‡FW&æÂ4D²6†V6²&÷‚f÷"WFöÖF–öâVW""“°¢6†V6´&÷‚ä—46†V6¶VBÒfÇ6S°¢G&–äF—7F6†W"‚“°¢f"6†V6´&÷…VW"Ò&WV—&UG—SÄ6†V6´&÷„WFöÖF–öåVW#â€¢T”VÆVÖVçDWFöÖF–öåVW"ä7&VFUVW$f÷$VÆVÖVçB†6†V6´&÷‚’À¢&W‡FW&æÂ4D²6†V6²&÷‚WFöÖF–öâVW""“°¢f"FövvÆU&÷f–FW"Ò&WV—&UG—SÄ•FövvÆU&÷f–FW#â€¢6†V6´&÷…VW"ävWEGFW&â…GFW&ä–çFW&f6RåFövvÆR’À¢&W‡FW&æÂ4D²6†V6²&÷‚FövvÆR&÷f–FW""“°¢76W'DWVÂ…FövvÆU7FFRäöfbÂFövvÆU&÷f–FW"åFövvÆU7FFRÂ&W‡FW&æÂ4D²WFöÖF–öâFövvÆR–æ—F–Â7FFR"“°¢–çBWFöÖF–öä6†V6¶VD&Vf÷&RÒv–æF÷räW‡FW&æÄ6†V6´&÷„6†V6¶VD6÷VçC°¢FövvÆU&÷f–FW"åFövvÆR‚“°¢G&–äF—7F6†W"‚“°¢76W'DWVÂ…FövvÆU7FFRäöâÂFövvÆU&÷f–FW"åFövvÆU7FFRÂ&W‡FW&æÂ4D²WFöÖF–öâFövvÆRöâ7FFR"“°¢76W'DWVÂ‡G'VRÂ6†V6´&÷‚ä—46†V6¶VBÓÒG'VRÂ&W‡FW&æÂ4D²WFöÖF–öâFövvÆR6†V6²&÷‚7FFR"“°¢76W'DDÆV7B†WFöÖF–öä6†V6¶VD&Vf÷&R²Âv–æF÷räW‡FW&æÄ6†V6´&÷„6†V6¶VD6÷VçBÂ&W‡FW&æÂ4D²WFöÖF–öâFövvÆR6†V6¶VB6÷VçB"“°¢–çBWFöÖF–öåVæ6†V6¶VD&Vf÷&RÒv–æF÷räW‡FW&æÄ6†V6´&÷…Væ6†V6¶VD6÷VçC°¢FövvÆU&÷f–FW"åFövvÆR‚“°¢G&–äF—7F6†W"‚“°¢76W'DWVÂ…FövvÆU7FFRäöfbÂFövvÆU&÷f–FW"åFövvÆU7FFRÂ&W‡FW&æÂ4D²WFöÖF–öâFövvÆR&W7F÷&VB7FFR"“°¢76W'DWVÂ†fÇ6RÂ6†V6´&÷‚ä—46†V6¶VBÓÒG'VRÂ&W‡FW&æÂ4D²WFöÖF–öâFövvÆR6†V6²&÷‚&W7F÷&VB7FFR"“°¢76W'DDÆV7B†WFöÖF–öåVæ6†V6¶VD&Vf÷&R²Âv–æF÷räW‡FW&æÄ6†V6´&÷…Væ6†V6¶VD6÷VçBÂ&W‡FW&æÂ4D²WFöÖF–öâFövvÆRVæ6†V6¶VB6÷VçB"“° ¢f"6Æ–FW"Ò&WV—&UG—SÅ6Æ–FW#â€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ6Æ–FW""’À¢&W‡FW&æÂ4D²6Æ–FW"f÷"WFöÖF–öâVW""“°¢f"&öw&W74&"Ò&WV—&UG—SÅ&öw&W74&#â€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅ&öw&W74&""’À¢&W‡FW&æÂ4D²&öw&W72&"f÷"WFöÖF–öâVW""“°¢f"6Æ–FW%VW"Ò&WV—&UG—SÅ6Æ–FW$WFöÖF–öåVW#â€¢T”VÆVÖVçDWFöÖF–öåVW"ä7&VFUVW$f÷$VÆVÖVçB‡6Æ–FW"’À¢&W‡FW&æÂ4D²6Æ–FW"WFöÖF–öâVW""“°¢f"&ævU&÷f–FW"Ò&WV—&UG—SÄ•&ævUfÇVU&÷f–FW#â€¢6Æ–FW%VW"ävWEGFW&â…GFW&ä–çFW&f6Rå&ævUfÇVR’À¢&W‡FW&æÂ4D²6Æ–FW"&ævR&÷f–FW""“°¢76W'DWVÂ†fÇ6RÂ&ævU&÷f–FW"ä—5&VDöæÇ’Â&W‡FW&æÂ4D²WFöÖF–öâ&ævR&VBÖöæÇ’7FFR"“°¢76W'DWVÂƒãÂ&ævU&÷f–FW"äÖ–æ–×VÒÂ&W‡FW&æÂ4D²WFöÖF–öâ&ævRÖ–æ–×VÒ"“°¢76W'DWVÂƒãÂ&ævU&÷f–FW"äÖ†–×VÒÂ&W‡FW&æÂ4D²WFöÖF–öâ&ævRÖ†–×VÒ"“°¢76W'DWVÂƒ"ãÂ&ævU&÷f–FW"å6ÖÆÄ6†ævRÂ&W‡FW&æÂ4D²WFöÖF–öâ&ævR6ÖÆÂ6†ævR"“°¢76W'DWVÂƒãÂ&ævU&÷f–FW"äÆ&vT6†ævRÂ&W‡FW&æÂ4D²WFöÖF–öâ&ævRÆ&vR6†ævR"“°¢–çBWFöÖF–öå6Æ–FW$6†ævVD&Vf÷&RÒv–æF÷räW‡FW&æÅ6Æ–FW%fÇVT6†ævVD6÷VçC°¢&ævU&÷f–FW"å6WEfÇVRƒSRã“°¢G&–äF—7F6†W"‚“°¢76W'D6Æ÷6RƒSRãÂ&ævU&÷f–FW"åfÇVRÂ&W‡FW&æÂ4D²WFöÖF–öâ&ævRfÇVR"“°¢76W'D6Æ÷6RƒSRãÂ6Æ–FW"åfÇVRÂ&W‡FW&æÂ4D²WFöÖF–öâ&ævR6Æ–FW"fÇVR"“°¢76W'D6Æ÷6RƒSRãÂ&öw&W74&"åfÇVRÂ&W‡FW&æÂ4D²WFöÖF–öâ&ævR&öw&W72fÇVR"“°¢76W'D6Æ÷6RƒSRãÂv–æF÷räÆ7DW‡FW&æÅ6Æ–FW%fÇVRÂ&W‡FW&æÂ4D²WFöÖF–öâ&ævRWfVçBfÇVR"“°¢76W'DDÆV7B†WFöÖF–öå6Æ–FW$6†ævVD&Vf÷&R²Âv–æF÷räW‡FW&æÅ6Æ–FW%fÇVT6†ævVD6÷VçBÂ&W‡FW&æÂ4D²WFöÖF–öâ&ævR6†ævVB6÷VçB"“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFU7—7FVÔ6öÖÖæD'WGFöâ€¢Ö–åv–æF÷rv–æF÷rÀ¢'WGFöâ'WGFöâÀ¢&÷WFVD6öÖÖæBW‡V7FVD6öÖÖæBÀ¢7G&–ærW‡V7FVE&ÖWFW"À¢v–æF÷u7FFRW‡V7FVE7FFRÀ¢7G&–ærFW67&—F–öâ¢°¢G&–äF—7F6†W"‚“°¢76W'DWVÂ†W‡V7FVD6öÖÖæBÂ'WGFöâä6öÖÖæBÂB'¶FW67&—F–öçÒ6öÖÖæB&–æF–ær"“°¢76W'DWVÂ†W‡V7FVE&ÖWFW"Â'WGFöâä6öÖÖæE&ÖWFW"ÂB'¶FW67&—F–öçÒ6öÖÖæB&ÖWFW""“°¢76W'DWVÂ‡v–æF÷rÂ'WGFöâä6öÖÖæEF&vWBÂB'¶FW67&—F–öçÒ6öÖÖæBF&vWB"“° ¢–çB6äW†V7WFT&Vf÷&RÒv–æF÷räW‡FW&æÅ7—7FVÔ6öÖÖæD6äW†V7WFT6÷VçC°¢–çBW†V7WFVD&Vf÷&RÒv–æF÷räW‡FW&æÅ7—7FVÔ6öÖÖæDW†V7WFVD6÷VçC°¢76W'DWVÂ‡G'VRÂW‡V7FVD6öÖÖæBä6äW†V7WFR†'WGFöâä6öÖÖæE&ÖWFW"Âv–æF÷r’ÂB'¶FW67&—F–öçÒ6âW†V7WFR"“°¢W‡V7FVD6öÖÖæBäW†V7WFR†'WGFöâä6öÖÖæE&ÖWFW"Âv–æF÷r“° ¢76W'DDÆV7B†6äW†V7WFT&Vf÷&R²Âv–æF÷räW‡FW&æÅ7—7FVÔ6öÖÖæD6äW†V7WFT6÷VçBÂB'¶FW67&—F–öçÒ6âÖW†V7WFR6÷VçB"“°¢76W'DWVÂ†W†V7WFVD&Vf÷&R²Âv–æF÷räW‡FW&æÅ7—7FVÔ6öÖÖæDW†V7WFVD6÷VçBÂB'¶FW67&—F–öçÒW†V7WFVB6÷VçB"“°¢76W'DWVÂ†W‡V7FVD6öÖÖæBäæÖRÂv–æF÷räÆ7DW‡FW&æÅ7—7FVÔ6öÖÖæDæÖRÂB'¶FW67&—F–öçÒ6öÖÖæBæÖR"“°¢76W'DWVÂ†W‡V7FVE&ÖWFW"Âv–æF÷räÆ7DW‡FW&æÅ7—7FVÔ6öÖÖæE&ÖWFW"ÂB'¶FW67&—F–öçÒW†V7WFVB&ÖWFW""“°¢76W'DWVÂ†W‡V7FVE7FFRÂv–æF÷råv–æF÷u7FFRÂB'¶FW67&—F–öçÒv–æF÷r7FFR"“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFT66W74¶W•&÷WF–ætgFW%'Vâ„Ö–åv–æF÷rv–æF÷r¢°¢f"fÆ–FF–öåFW‡D&÷‚Ò&WV—&UG—SÅFW‡D&÷ƒâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÅfÆ–FF–öåFW‡D&÷‚"’À¢&W‡FW&æÂ4D²66W72Ö¶W’&÷WF–ærF&vWBFW‡B&÷‚"“°¢f"&W6VçFF–öå6÷W&6RÒ&W6VçFF–öå6÷W&6Räg&öÕf—7VÂ‡v–æF÷r¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡V7FVBW‡FW&æÂ4D²v–æF÷rFò†fR&W6VçFF–öâ6÷W&6Râ"“° ¢76W'DWVÂ‡G'VRÂ66W74¶W”ÖævW"ä—4¶W•&Vv—7FW&VB‡&W6VçFF–öå6÷W&6RÂ$R"’Â&W‡FW&æÂ4D²66W72Ö¶W’ÖævW"&Vv—7FW&VBÆ&VÂ¶W’"“°¢¶W–&ö&Bä6ÆV$fö7W2‚“°¢76W'DWVÂ†fÇ6RÂ&VfW&Væ6TWVÇ2‡fÆ–FF–öåFW‡D&÷‚Â¶W–&ö&Bäfö7W6VDVÆVÖVçB’Â&W‡FW&æÂ4D²66W72Ö¶W’ÖævW"6ÆV&VBfö7W2"“°¢76W'DWVÂ†fÇ6RÂ66W74¶W”ÖævW"å&ö6W74¶W’‡&W6VçFF–öå6÷W&6RÂ$R"ÂfÇ6R’Â&W‡FW&æÂ4D²66W72Ö¶W’ÖævW"&ö6W72Æ7B¶W’"“°¢76W'DWVÂ‡fÆ–FF–öåFW‡D&÷‚Â¶W–&ö&Bäfö7W6VDVÆVÖVçBÂ&W‡FW&æÂ4D²66W72Ö¶W’ÖævW"fö7W6VBÆ&VÂF&vWB"“°¢¶W–&ö&Bä6ÆV$fö7W2‚“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFT6Æ74–çWD&–æF–ætgFW%'Vâ„Ö–åv–æF÷rv–æF÷r¢°¢f"6Æ746öÖÖæEF&vWBÒ&WV—&UG—SÄW‡FW&æÄ6Æ746öÖÖæEFW‡D&÷ƒâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ6Æ746öÖÖæEF&vWB"’À¢&W‡FW&æÂ4D²6Æ72–çWB&–æF–ær'VçF–ÖRF&vWB"“° ¢¶W–&ö&Bäfö7W2†6Æ746öÖÖæEF&vWB“°¢76W'DWVÂ†6Æ746öÖÖæEF&vWBÂ¶W–&ö&Bäfö7W6VDVÆVÖVçBÂ&W‡FW&æÂ4D²6Æ72–çWB&–æF–ærfö7W6VBF&vWB"“°¢–çB6Æ74W†V7WFVD&Vf÷&RÒ6Æ746öÖÖæEF&vWBä6Æ746öÖÖæDW†V7WFVD6÷VçC°¢ö&¦V7B¶W”F÷vâÒ7&VFU÷'F&ÆT–çWDWfVçB‚$¶W”F÷vâ"Â¶W“¢$c‚"ÂÖöF–f–W'4æÖS¢$6öçG&öÂ"“°¢†æFÆU÷'F&ÆT–çWB‡v–æF÷rÂ¶W”F÷vâ“° ¢76W'DWVÂ‡G'VRÂvWE&÷W'G•fÇVSÆ&ööÃâ†¶W”F÷vâÂ$†æFÆVB"’Â&W‡FW&æÂ4D²6Æ72–çWB&–æF–ær¶W’WfVçB†æFÆVB"“°¢76W'DWVÂ†6Æ74W†V7WFVD&Vf÷&R²Â6Æ746öÖÖæEF&vWBä6Æ746öÖÖæDW†V7WFVD6÷VçBÂ&W‡FW&æÂ4D²6Æ72–çWB&–æF–ær6öÖÖæBW†V7WF–öâ6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÄ6Æ74–çWE&ÖWFW""Â6Æ746öÖÖæEF&vWBäÆ7D6Æ746öÖÖæE&ÖWFW"Â&W‡FW&æÂ4D²6Æ72–çWB&–æF–ær6öÖÖæB&ÖWFW""“°¢76W'DWVÂ†æÖVöb„W‡FW&æÄ6Æ746öÖÖæEFW‡D&÷‚äW‡FW&æÄ6Æ746öÖÖæB’Â6Æ746öÖÖæEF&vWBäÆ7D6Æ746öÖÖæDæÖRÂ&W‡FW&æÂ4D²6Æ72–çWB&–æF–ær6öÖÖæBæÖR"“° ¢ö&¦V7B¶W•WÒ7&VFU÷'F&ÆT–çWDWfVçB‚$¶W•W"Â¶W“¢$c‚"ÂÖöF–f–W'4æÖS¢$æöæR"“°¢†æFÆU÷'F&ÆT–çWB‡v–æF÷rÂ¶W•W“°¢76W'DWVÂ€¢6Æ74W†V7WFVD&Vf÷&R²À¢6Æ746öÖÖæEF&vWBä6Æ746öÖÖæDW†V7WFVD6÷VçBÀ¢&W‡FW&æÂ4D²6Æ72–çWB&–æF–ær–væ÷&W2¶W’W"“°¢¶W–&ö&Bä6ÆV$fö7W2‚“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFUF‡VÖ$G&tÖævW"„Ö–åv–æF÷rv–æF÷r¢°¢f"fö7W5æVÂÒ&WV—&UG—SÅ7F6µæVÃâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄfö7W5æVÂ"’À¢&W‡FW&æÂ4D²F‡VÖ"G&r&VçBæVÂ"“°¢f"F‡VÖ"Ò&WV—&UG—SÅF‡VÖ#â€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄG&uF‡VÖ""’À¢&W‡FW&æÂ4D²F‡VÖ"G&rÖævW""“°¢76W'DWVÂƒ#BãÂF‡VÖ"åv–GF‚Â&W‡FW&æÂ4D²F‡VÖ"v–GF‚"“°¢76W'DWVÂƒ‚ãÂF‡VÖ"ä†V–v‡BÂ&W‡FW&æÂ4D²F‡VÖ"†V–v‡B"“°¢76W'DWVÂ‚&W‡FW&æÂG&rF‡VÖ""ÂF‡VÖ"åFrÂ&W‡FW&æÂ4D²F‡VÖ"Fr"“°¢76W'DWVÂ†fÇ6RÂF‡VÖ"äfö7W6&ÆRÂ&W‡FW&æÂ4D²F‡VÖ"fö7W6&ÆRÖWFFF"“°¢76W'DWVÂ†fÇ6RÂF‡VÖ"ä—4G&vv–ærÂ&W‡FW&æÂ4D²F‡VÖ"–æ—F–ÂG&vv–ær7FFR"“°¢76W'DWVÂƒÂv–æF÷räW‡FW&æÅF‡VÖ$G&u7F'FVD6÷VçBÂ&W‡FW&æÂ4D²F‡VÖ"–æ—F–ÂG&u7F'FVB6÷VçB"“°¢76W'DWVÂƒÂv–æF÷räW‡FW&æÅF‡VÖ$G&tFVÇF6÷VçBÂ&W‡FW&æÂ4D²F‡VÖ"–æ—F–ÂG&tFVÇF6÷VçB"“°¢76W'DWVÂƒÂv–æF÷räW‡FW&æÅF‡VÖ$G&t6ö×ÆWFVD6÷VçBÂ&W‡FW&æÂ4D²F‡VÖ"–æ—F–ÂG&t6ö×ÆWFVB6÷VçB"“°¢76W'DWVÂƒÂv–æF÷räW‡FW&æÄ'V&&ÆVEF‡VÖ$G&tFVÇF6÷VçBÂ&W‡FW&æÂ4D²F‡VÖ"–æ—F–Â'V&&ÆVBG&tFVÇF6÷VçB"“° ¢f"7F'FVBÒæWrG&u7F'FVDWfVçD&w2ƒ"ãRÂ2ãR¢°¢&÷WFVDWfVçBÒF‡VÖ"äG&u7F'FVDWfVç@¢Ó°¢f"FVÇFÒæWrG&tFVÇFWfVçD&w2ƒBãÂbã¢°¢&÷WFVDWfVçBÒF‡VÖ"äG&tFVÇFWfVç@¢Ó°¢f"6ö×ÆWFVBÒæWrG&t6ö×ÆWFVDWfVçD&w2ƒ‚ãÂãÂG'VR¢°¢&÷WFVDWfVçBÒF‡VÖ"äG&t6ö×ÆWFVDWfVç@¢Ó° ¢F‡VÖ"å&—6TWfVçB‡7F'FVB“°¢F‡VÖ"å&—6TWfVçB†FVÇF“°¢F‡VÖ"å&—6TWfVçB†6ö×ÆWFVB“° ¢76W'DWVÂƒÂv–æF÷räW‡FW&æÅF‡VÖ$G&u7F'FVD6÷VçBÂ&W‡FW&æÂ4D²F‡VÖ"G&u7F'FVB†æFÆW"6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÄG&uF‡VÖ""Âv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&u7F'FVE6VæFW$æÖRÂ&W‡FW&æÂ4D²F‡VÖ"G&u7F'FVB6VæFW""“°¢76W'DWVÂ‚$G&u7F'FVB"Âv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&u7F'FVE&÷WFVDWfVçDæÖRÂ&W‡FW&æÂ4D²F‡VÖ"G&u7F'FVB&÷WFVBWfVçB"“°¢76W'DWVÂƒ"ãRÂv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&u7F'FVD†÷&—¦öçFÄöfg6WBÂ&W‡FW&æÂ4D²F‡VÖ"G&u7F'FVB†÷&—¦öçFÂöfg6WB"“°¢76W'DWVÂƒ2ãRÂv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&u7F'FVEfW'F–6Äöfg6WBÂ&W‡FW&æÂ4D²F‡VÖ"G&u7F'FVBfW'F–6Âöfg6WB"“° ¢76W'DWVÂƒÂv–æF÷räW‡FW&æÅF‡VÖ$G&tFVÇF6÷VçBÂ&W‡FW&æÂ4D²F‡VÖ"G&tFVÇF†æFÆW"6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÄG&uF‡VÖ""Âv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&tFVÇF6VæFW$æÖRÂ&W‡FW&æÂ4D²F‡VÖ"G&tFVÇF6VæFW""“°¢76W'DWVÂ‚$G&tFVÇF"Âv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&tFVÇF&÷WFVDWfVçDæÖRÂ&W‡FW&æÂ4D²F‡VÖ"G&tFVÇF&÷WFVBWfVçB"“°¢76W'DWVÂƒBãÂv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&tFVÇF†÷&—¦öçFÄ6†ævRÂ&W‡FW&æÂ4D²F‡VÖ"G&tFVÇF†÷&—¦öçFÂ6†ævR"“°¢76W'DWVÂƒbãÂv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&tFVÇFfW'F–6Ä6†ævRÂ&W‡FW&æÂ4D²F‡VÖ"G&tFVÇFfW'F–6Â6†ævR"“°¢76W'DWVÂƒÂv–æF÷räW‡FW&æÄ'V&&ÆVEF‡VÖ$G&tFVÇF6÷VçBÂ&W‡FW&æÂ4D²F‡VÖ"'V&&ÆVBG&tFVÇF†æFÆW"6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÄfö7W5æVÂ"Âv–æF÷räÆ7DW‡FW&æÄ'V&&ÆVEF‡VÖ$G&tFVÇF6VæFW$æÖRÂ&W‡FW&æÂ4D²F‡VÖ"'V&&ÆVBG&tFVÇF6VæFW""“°¢76W'DWVÂ‚$W‡FW&æÄG&uF‡VÖ""Âv–æF÷räÆ7DW‡FW&æÄ'V&&ÆVEF‡VÖ$G&tFVÇF÷&–v–æÅ6÷W&6TæÖRÂ&W‡FW&æÂ4D²F‡VÖ"'V&&ÆVBG&tFVÇF÷&–v–æÂ6÷W&6R"“°¢76W'DWVÂ‚$G&tFVÇF"Âv–æF÷räÆ7DW‡FW&æÄ'V&&ÆVEF‡VÖ$G&tFVÇF&÷WFVDWfVçDæÖRÂ&W‡FW&æÂ4D²F‡VÖ"'V&&ÆVBG&tFVÇF&÷WFVBWfVçB"“°¢76W'DWVÂƒBãÂv–æF÷räÆ7DW‡FW&æÄ'V&&ÆVEF‡VÖ$G&tFVÇF†÷&—¦öçFÄ6†ævRÂ&W‡FW&æÂ4D²F‡VÖ"'V&&ÆVBG&tFVÇF†÷&—¦öçFÂ6†ævR"“°¢76W'DWVÂƒbãÂv–æF÷räÆ7DW‡FW&æÄ'V&&ÆVEF‡VÖ$G&tFVÇFfW'F–6Ä6†ævRÂ&W‡FW&æÂ4D²F‡VÖ"'V&&ÆVBG&tFVÇFfW'F–6Â6†ævR"“° ¢76W'DWVÂƒÂv–æF÷räW‡FW&æÅF‡VÖ$G&t6ö×ÆWFVD6÷VçBÂ&W‡FW&æÂ4D²F‡VÖ"G&t6ö×ÆWFVB†æFÆW"6÷VçB"“°¢76W'DWVÂ‚$W‡FW&æÄG&uF‡VÖ""Âv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&t6ö×ÆWFVE6VæFW$æÖRÂ&W‡FW&æÂ4D²F‡VÖ"G&t6ö×ÆWFVB6VæFW""“°¢76W'DWVÂ‚$G&t6ö×ÆWFVB"Âv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&t6ö×ÆWFVE&÷WFVDWfVçDæÖRÂ&W‡FW&æÂ4D²F‡VÖ"G&t6ö×ÆWFVB&÷WFVBWfVçB"“°¢76W'DWVÂƒ‚ãÂv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&t6ö×ÆWFVD†÷&—¦öçFÄ6†ævRÂ&W‡FW&æÂ4D²F‡VÖ"G&t6ö×ÆWFVB†÷&—¦öçFÂ6†ævR"“°¢76W'DWVÂƒãÂv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&t6ö×ÆWFVEfW'F–6Ä6†ævRÂ&W‡FW&æÂ4D²F‡VÖ"G&t6ö×ÆWFVBfW'F–6Â6†ævR"“°¢76W'DWVÂ‡G'VRÂv–æF÷räÆ7DW‡FW&æÅF‡VÖ$G&t6ö×ÆWFVD6æ6VÆVBÂ&W‡FW&æÂ4D²F‡VÖ"G&t6ö×ÆWFVB6æ6VÆVB7FFR"“°¢76W'DWVÂ‡G'VRÂ&VfW&Væ6TWVÇ2†fö7W5æVÂÂF‡VÖ"å&VçB’Â&W‡FW&æÂ4D²F‡VÖ"Æöv–6Â&VçB"“°¢Ğ ¢&—fFR7FF–2ö&¦V7B7&VFU÷'F&ÆT–çWDWfVçB€¢7G&–ær¶–æDæÖRÀ¢7G&–æsò¶W’ÒçVÆÂÀ¢–çB66ä6öFRÒÀ¢7G&–ærÖöF–f–W'4æÖRÒ$æöæR"À¢6†#ò6†&7FW"ÒçVÆÂÀ¢F÷V&ÆR‚ÒÀ¢F÷V&ÆR’ÒÀ¢F÷V&ÆRFVÇF‚ÒÀ¢F÷V&ÆRFVÇF’ÒÀ¢7G&–ær'WGFöäæÖRÒ$æöæR"¢°¢76VÖ&Ç’&W6VçFF–öäg&ÖWv÷&²ÒG—Vöb…v–æF÷r’ä76VÖ&Ç“°¢G—R&w5G—RÒ&WV—&UG—SÅG—Sâ€¢&W6VçFF–öäg&ÖWv÷&²ävWEG—R‚%7—7FVÒåv–æF÷w2å÷'F&ÆT–çWDWfVçD&w2"ÂF‡&÷töäW'&÷#¢G'VR’À¢'÷'F&ÆR–çWBWfVçB&w2G—R"“°¢G—R¶–æEG—RÒ&WV—&UG—SÅG—Sâ€¢&W6VçFF–öäg&ÖWv÷&²ävWEG—R‚%7—7FVÒåv–æF÷w2å÷'F&ÆT–çWDWfVçD¶–æB"ÂF‡&÷töäW'&÷#¢G'VR’À¢'÷'F&ÆR–çWBWfVçB¶–æBG—R"“°¢G—R'WGFöåG—RÒ&WV—&UG—SÅG—Sâ€¢&W6VçFF–öäg&ÖWv÷&²ävWEG—R‚%7—7FVÒåv–æF÷w2å÷'F&ÆTÖ÷W6T'WGFöâ"ÂF‡&÷töäW'&÷#¢G'VR’À¢'÷'F&ÆRÖ÷W6R'WGFöâG—R"“°¢G—RÖöF–f–W'5G—RÒ&WV—&UG—SÅG—Sâ€¢&W6VçFF–öäg&ÖWv÷&²ävWEG—R‚%7—7FVÒåv–æF÷w2å÷'F&ÆT–çWDÖöF–f–W'2"ÂF‡&÷töäW'&÷#¢G'VR’À¢'÷'F&ÆR–çWBÖöF–f–W'2G—R"“° ¢&WGW&â7F—fF÷"ä7&VFT–ç7Fæ6R€¢&w5G—RÀ¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2À¢&–æFW#¢çVÆÂÀ¢&w3¢æWrö&¦V7CõµĞ¢°¢VçVÒå'6R†¶–æEG—RÂ¶–æDæÖR’À¢¶W’À¢66ä6öFRÀ¢6†&7FW"À¢‚À¢’À¢FVÇF‚À¢FVÇF’À¢VçVÒå'6R†'WGFöåG—RÂ'WGFöäæÖR’À¢VçVÒå'6R†ÖöF–f–W'5G—RÂÖöF–f–W'4æÖR¢ÒÀ¢7VÇGW&S¢çVÆÂ¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$f–ÆVBFò7&VFRw¶&w5G—RägVÆÄæÖWÒrâ"“°¢Ğ ¢&—fFR7FF–2fö–B†æFÆU÷'F&ÆT–çWB…v–æF÷rv–æF÷rÂö&¦V7B–çWB¢°¢ÖWF†öD–æfò†æFÆU÷'F&ÆT–çWBÒG—Vöb…v–æF÷r’ävWDÖWF†öB€¢$†æFÆU÷'F&ÆT–çWB"À¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡G—Vöb…v–æF÷r’ägVÆÄæÖRÂ$†æFÆU÷'F&ÆT–çWB"“° ¢†æFÆU÷'F&ÆT–çWBä–çfö¶R‡v–æF÷rÂ¶–çWEÒ“°¢Ğ ¢&—fFR7FF–2BvWE&÷W'G•fÇVSÅCâ†ö&¦V7B–ç7Fæ6RÂ7G&–ær&÷W'G”æÖR¢°¢&÷W'G”–æfò&÷W'G’Ò–ç7Fæ6RävWEG—R‚’ävWE&÷W'G’€¢&÷W'G”æÖRÀ¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢óòF‡&÷ræWrÖ—76–ætÖVÖ&W$W†6WF–öâ†–ç7Fæ6RävWEG—R‚’ägVÆÄæÖRÂ&÷W'G”æÖR“°¢ö&¦V7CòfÇVRÒ&÷W'G’ävWEfÇVR†–ç7Fæ6R“°¢&WGW&âfÇVR—2BG—V@¢òG—V@¢¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVBw¶–ç7Fæ6RävWEG—R‚’ägVÆÄæÖWÒç·&÷W'G”æÖWÒrFò&R·G—Vöb…B’ägVÆÄæÖWÒÂ'WBf÷VæB·fÇVSòävWEG—R‚’ägVÆÄæÖRóò#ÆçVÆÃâ'Òâ"“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFT¶W–&ö&Dæf–vF–öägFW%'Vâ„Ö–åv–æF÷rv–æF÷r¢°¢f"¶W–&ö&Dæf–vF–öåæVÂÒ&WV—&UG—SÅ7F6µæVÃâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ¶W–&ö&Dæf–vF–öåæVÂ"’À¢&W‡FW&æÂ4D²¶W–&ö&Bæf–vF–öâ'VçF–ÖRæVÂ"“°¢f"f—'7D'WGFöâÒ&WV—&UG—SÄ'WGFöãâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ¶W–&ö&Dæf–vF–öäf—'7D'WGFöâ"’À¢&W‡FW&æÂ4D²f—'7B¶W–&ö&Bæf–vF–öâ'VçF–ÖR'WGFöâ"“°¢f"6V6öæD'WGFöâÒ&WV—&UG—SÄ'WGFöãâ€¢v–æF÷räf–æDæÖR‚$W‡FW&æÄ¶W–&ö&Dæf–vF–öå6V6öæD'WGFöâ"’À¢&W‡FW&æÂ4D²6V6öæB¶W–&ö&Bæf–vF–öâ'VçF–ÖR'WGFöâ"“° ¢¶W–&ö&Dæf–vF–öåæVÂåWFFTÆ–÷WB‚“°¢76W'DWVÂ†f—'7D'WGFöâÂ¶W–&ö&Bäfö7W2†f—'7D'WGFöâ’Â&W‡FW&æÂ4D²¶W–&ö&Dæf–vF–öâ–æ—F–Âfö7W2"“°¢76W'DWVÂ†f—'7D'WGFöâÂ¶W–&ö&Bäfö7W6VDVÆVÖVçBÂ&W‡FW&æÂ4D²¶W–&ö&Dæf–vF–öâfö7W6VBf—'7B'WGFöâ"“°¢76W'DWVÂ‡G'VRÂf—'7D'WGFöâäÖ÷fTfö7W2†æWrG&fW'6Å&WVW7B„fö7W4æf–vF–öäF—&V7F–öâäæW‡B’’Â&W‡FW&æÂ4D²¶W–&ö&Dæf–vF–öâæW‡BÖ÷fR&W7VÇB"“°¢76W'DWVÂ‡6V6öæD'WGFöâÂ¶W–&ö&Bäfö7W6VDVÆVÖVçBÂ&W‡FW&æÂ4D²¶W–&ö&Dæf–vF–öâfö7W6VB6V6öæB'WGFöâ"“°¢76W'DWVÂ‡G'VRÂ6V6öæD'WGFöâäÖ÷fTfö7W2†æWrG&fW'6Å&WVW7B„fö7W4æf–vF–öäF—&V7F–öâäæW‡B’’Â&W‡FW&æÂ4D²¶W–&ö&Dæf–vF–öâ7–6ÆRæW‡BÖ÷fR&W7VÇB"“°¢76W'DWVÂ†f—'7D'WGFöâÂ¶W–&ö&Bäfö7W6VDVÆVÖVçBÂ&W‡FW&æÂ4D²¶W–&ö&Dæf–vF–öâ7–6ÆVBf—'7B'WGFöâ"“°¢76W'DWVÂ‡G'VRÂf—'7D'WGFöâäÖ÷fTfö7W2†æWrG&fW'6Å&WVW7B„fö7W4æf–vF–öäF—&V7F–öâå&Wf–÷W2’’Â&W‡FW&æÂ4D²¶W–&ö&Dæf–vF–öâ&Wf–÷W2Ö÷fR&W7VÇB"“°¢76W'DWVÂ‡6V6öæD'WGFöâÂ¶W–&ö&Bäfö7W6VDVÆVÖVçBÂ&W‡FW&æÂ4D²¶W–&ö&Dæf–vF–öâ7–6ÆVB&Wf–÷W2'WGFöâ"“°¢¶W–&ö&Bä6ÆV$fö7W2‚“°¢Ğ ¢&—fFR7FF–2fö–BG&–äF—7F6†W"‚¢°¢&ööÂÖ&¶W%&V6†VBÒfÇ6S°¢f"g&ÖRÒæWrF—7F6†W$g&ÖR‚“°¢f"Ö&¶W$÷W&F–öâÒF—7F6†W"ä7W'&VçDF—7F6†W"ä&Vv–ä–çfö¶R€¢F—7F6†W%&–÷&—G’äÆ–6F–öä–FÆRÀ¢æWr7F–öâ‚‚’Óà¢°¢Ö&¶W%&V6†VBÒG'VS°¢g&ÖRä6öçF–çVRÒfÇ6S°¢Ò’“°¢f"F–ÖW"ÒæWrF—7F6†W%F–ÖW"„F—7F6†W%&–÷&—G’å6VæB¢°¢–çFW'fÂÒF–ÖU7âäg&öÔÖ–ÆÆ—6V6öæG2ƒ#S¢Ó°¢F–ÖW"åF–6²³Ò…òÂò’Óà¢°¢F–ÖW"å7F÷‚“°¢g&ÖRä6öçF–çVRÒfÇ6S°¢Ó°¢F–ÖW"å7F'B‚“°¢F—7F6†W"åW6„g&ÖR†g&ÖR“°¢F–ÖW"å7F÷‚“°¢–b‚Ö&¶W%&V6†VBbbÖ&¶W$÷W&F–öâå7FGW2ÓÒF—7F6†W$÷W&F–öå7FGW2åVæF–ær¢°¢Ö&¶W$÷W&F–öâä&÷'B‚“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–BV×F—7F6†W%VçF–Â„gVæ3Æ&ööÃâ6öæF—F–öâÂF–ÖU7âF–ÖV÷WBÂ7G&–ærFW67&—F–öâ¢°¢f"FVFÆ–æRÒFFUF–ÖRåWF4æ÷r²F–ÖV÷WC°¢v†–ÆR‚6öæF—F–öâ‚’¢°¢G&–äF—7F6†W"‚“° ¢–b†6öæF—F–öâ‚’¢°¢&WGW&ã°¢Ğ ¢–b„FFUF–ÖRåWF4æ÷rãÒFVFÆ–æR¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B%F–ÖVB÷WBv—F–ærf÷"¶FW67&—F–öçÒâ"“°¢Ğ ¢F‡&VBå6ÆVWƒ“°¢Ğ¢Ğ ¢&—fFR7FF–2B&WV—&Tf—'7D–æÆ–æSÅCâ„–æÆ–æT6öÆÆV7F–öâ–æÆ–æW2Â7G&–ærFW67&—F–öâ¢v†W&RB¢–æÆ–æP¢°¢f÷&V6‚„–æÆ–æR–æÆ–æR–â–æÆ–æW2¢°¢–b†–æÆ–æR—2BG—VB¢°¢&WGW&âG—VC°¢Ğ¢Ğ ¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒFò6öçF–â·G—Vöb…B’ägVÆÄæÖWÒâ"“°¢Ğ ¢&—fFR7FF–2B&WV—&Tf—'7D–æÆ–æTW†7CÅCâ„–æÆ–æT6öÆÆV7F–öâ–æÆ–æW2Â7G&–ærFW67&—F–öâ¢v†W&RB¢–æÆ–æP¢°¢f÷&V6‚„–æÆ–æR–æÆ–æR–â–æÆ–æW2¢°¢–b†–æÆ–æRävWEG—R‚’ÓÒG—Vöb…B’¢°¢&WGW&â…B––æÆ–æS°¢Ğ¢Ğ ¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒFò6öçF–âW†7B·G—Vöb…B’ägVÆÄæÖWÒâ"“°¢Ğ ¢&—fFR7FF–2B&WV—&UG—SÅCâ†ö&¦V7CòfÇVRÂ7G&–ærFW67&—F–öâ¢°¢–b‡fÇVR—2BG—VB¢°¢&WGW&âG—VC°¢Ğ ¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒFò&R·G—Vöb…B’ägVÆÄæÖWÒÂ'WBf÷VæB·fÇVSòävWEG—R‚’ägVÆÄæÖRóò#ÆçVÆÃâ'Òâ"“°¢Ğ ¢&—fFR7FF–2g&ÖWv÷&´VÆVÖVçCòf–æEf—7VÄFW66VæFçD'”æÖR„FWVæFVæ7”ö&¦V7B&ö÷BÂ7G&–æræÖR¢°¢–çB6†–ÆD6÷VçBÒf—7VÅG&VT†VÇW"ävWD6†–ÆG&Vä6÷VçB‡&ö÷B“°¢f÷"†–çB’Ò²’Â6†–ÆD6÷VçC²’²²¢°¢FWVæFVæ7”ö&¦V7B6†–ÆBÒf—7VÅG&VT†VÇW"ävWD6†–ÆB‡&ö÷BÂ’“°¢–b†6†–ÆB—2g&ÖWv÷&´VÆVÖVçBVÆVÖVçBb`¢7G&–æräWVÇ2†VÆVÖVçBäæÖRÂæÖRÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢&WGW&âVÆVÖVçC°¢Ğ ¢g&ÖWv÷&´VÆVÖVçCòæW7FVBÒf–æEf—7VÄFW66VæFçD'”æÖR†6†–ÆBÂæÖR“°¢–b†æW7FVB—2æ÷BçVÆÂ¢°¢&WGW&âæW7FVC°¢Ğ¢Ğ ¢&WGW&âçVÆÃ°¢Ğ ¢&—fFR7FF–2&ööÂ6öçF–ç4w&÷W…7—7FVÒä6öÆÆV7F–öç2ä”VçVÖW&&ÆRw&÷W2Â7G&–æræÖR¢°¢f÷&V6‚†ö&¦V7Bw&÷W–âw&÷W2¢°¢–b†w&÷W—26öÆÆV7F–öåf–Wtw&÷W6öÆÆV7F–öåf–Wtw&÷W ¢bb7G&–æräWVÇ2†6öÆÆV7F–öåf–Wtw&÷WäæÖSòåFõ7G&–ær‚’ÂæÖRÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢&WGW&âG'VS°¢Ğ¢Ğ ¢&WGW&âfÇ6S°¢Ğ ¢&—fFR7FF–2–çBvWDw&÷W—FVÔ6÷VçB…7—7FVÒä6öÆÆV7F–öç2ä”VçVÖW&&ÆRw&÷W2Â7G&–æræÖR¢°¢f÷&V6‚†ö&¦V7Bw&÷W–âw&÷W2¢°¢–b†w&÷W—26öÆÆV7F–öåf–Wtw&÷W6öÆÆV7F–öåf–Wtw&÷W ¢bb7G&–æräWVÇ2†6öÆÆV7F–öåf–Wtw&÷WäæÖSòåFõ7G&–ær‚’ÂæÖRÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢&WGW&â6öÆÆV7F–öåf–Wtw&÷Wä—FVÔ6÷VçC°¢Ğ¢Ğ ¢&WGW&â°¢Ğ ¢&—fFR7FF–2fö–B76W'D''W6„6öÆ÷"„''W6‚''W6‚Â7G&–ærW‡V7FVBÂ7G&–ærFW67&—F–öâ¢°¢f"6öÆ–D6öÆ÷$''W6‚Ò&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ†''W6‚ÂFW67&—F–öâ“°¢76W'DWVÂ†W‡V7FVBÂ6öÆ–D6öÆ÷$''W6‚ä6öÆ÷"åFõ7G&–ær‚’ÂFW67&—F–öâ“°¢Ğ ¢&—fFR7FF–2fö–B76W'EFV×ÆFUFW‡B„FFFV×ÆFRFV×ÆFRÂö&¦V7BFF6öçFW‡BÂ7G&–ærW‡V7FVEFW‡BÂ7G&–ærFW67&—F–öâ¢°¢f"FW‡BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢FV×ÆFRäÆöD6öçFVçB‚’À¢FW67&—F–öâ²"&ö÷B"“°¢FW‡BäFF6öçFW‡BÒFF6öçFW‡C°¢G&–äF—7F6†W"‚“°¢76W'DWVÂ†W‡V7FVEFW‡BÂFW‡BåFW‡BÂFW67&—F–öâ“°¢Ğ ¢&—fFR7FF–2fö–B76W'E&w&…FW‡B…&w&‚&w&‚Â7G&–ærW‡V7FVEFW‡BÂ7G&–ærFW67&—F–öâ¢°¢f"'VâÒ&WV—&Tf—'7D–æÆ–æSÅ'Vãâ€¢&w&‚ä–æÆ–æW2À¢B&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB¶FW67&—F–öçÒ'Vâ"“°¢76W'DWVÂ†W‡V7FVEFW‡BÂ'VâåFW‡BÂB&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB¶FW67&—F–öçÒFW‡B"“°¢Ğ ¢&—fFR7FF–2fö–B76W'DÆ—7D—FVÕFW‡B„Æ—7D—FVÓòÆ—7D—FVÒÂ7G&–ærW‡V7FVEFW‡BÂ7G&–ærFW67&—F–öâ¢°¢f"—FVÒÒ&WV—&UG—SÄÆ—7D—FVÓâ€¢Æ—7D—FVÒÀ¢B&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB¶FW67&—F–öçÒÆ—7B—FVÒ"“°¢f"&w&‚Ò&WV—&UG—SÅ&w&ƒâ€¢—FVÒä&Æö6·2äf—'7D&Æö6²À¢B&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB¶FW67&—F–öçÒÆ—7B—FVÒ&w&‚"“°¢76W'E&w&…FW‡B‡&w&‚ÂW‡V7FVEFW‡BÂB'¶FW67&—F–öçÒÆ—7B—FVÒ"“°¢Ğ ¢&—fFR7FF–2fö–B76W'EF&ÆT6VÆÅFW‡B…F&ÆT6VÆÂF&ÆT6VÆÂÂ7G&–ærW‡V7FVEFW‡BÂ7G&–ærFW67&—F–öâ¢°¢f"&w&‚Ò&WV—&UG—SÅ&w&ƒâ€¢F&ÆT6VÆÂä&Æö6·2äf—'7D&Æö6²À¢B&W‡FW&æÂ4D²fÆ÷tFö7VÖVçB¶FW67&—F–öçÒF&ÆR6VÆÂ&w&‚"“°¢76W'E&w&…FW‡B‡&w&‚ÂW‡V7FVEFW‡BÂB'¶FW67&—F–öçÒF&ÆR6VÆÂ"“°¢Ğ ¢&—fFR7FF–26WGFW"76W'DÆö÷6U7G–ÆU6WGFW"€¢6WGFW$&6R6WGFW$&6RÀ¢FWVæFVæ7•&÷W'G’W‡V7FVE&÷W'G’À¢ö&¦V7BW‡V7FVEfÇVRÀ¢7G&–ærFW67&—F–öâ¢°¢f"6WGFW"Ò&WV—&UG—SÅ6WGFW#â€¢6WGFW$&6RÀ¢FW67&—F–öâ“°¢76W'DWVÂ†W‡V7FVE&÷W'G’Â6WGFW"å&÷W'G’ÂB'¶FW67&—F–öçÒ&÷W'G’"“°¢76W'DWVÂ†W‡V7FVEfÇVRÂ6WGFW"åfÇVRÂB'¶FW67&—F–öçÒfÇVR"“°¢&WGW&â6WGFW#°¢Ğ ¢&—fFR7FF–2fö–B76W'DWVÃÅCâ…BW‡V7FVBÂB7GVÂÂ7G&–ærFW67&—F–öâ¢°¢–b‚WVÆ—G”6ö×&W#ÅCâäFVfVÇBäWVÇ2†W‡V7FVBÂ7GVÂ’¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒFò&Rw¶W‡V7FVGÒrÂ'WBf÷VæBw¶7GVÇÒrâ"“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B76W'D6Æ÷6R†F÷V&ÆRW‡V7FVBÂF÷V&ÆR7GVÂÂ7G&–ærFW67&—F–öâ¢°¢76W'D6Æ÷6R†W‡V7FVBÂ7GVÂÂãÂFW67&—F–öâ“°¢Ğ ¢&—fFR7FF–2fö–B76W'D6Æ÷6R†F÷V&ÆRW‡V7FVBÂF÷V&ÆR7GVÂÂF÷V&ÆRFöÆW&æ6RÂ7G&–ærFW67&—F–öâ¢°¢–b„ÖF‚ä'2†W‡V7FVBÒ7GVÂ’âFöÆW&æ6R¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒFò&R6Æ÷6RFòw¶W‡V7FVGÒrÂ'WBf÷VæBw¶7GVÇÒrâ"“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B76W'D&WGvVVâ†F÷V&ÆRÖ–æ–×VÒÂF÷V&ÆRÖ†–×VÒÂF÷V&ÆR7GVÂÂ7G&–ærFW67&—F–öâ¢°¢–b†7GVÂÂÖ–æ–×VÒÇÂ7GVÂâÖ†–×VÒ¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒFò&R&WGvVVâw¶Ö–æ–×V×ÒræBw¶Ö†–×V×ÒrÂ'WBf÷VæBw¶7GVÇÒrâ"“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B76W'D6öçF–ç2‡7G&–ærW‡V7FVE7V'7G&–ærÂ7G&–ær7GVÂÂ7G&–ærFW67&—F–öâ¢°¢–b‚7GVÂä6öçF–ç2†W‡V7FVE7V'7G&–ærÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒFò6öçF–âw¶W‡V7FVE7V'7G&–æwÒrÂ'WBf÷VæBw¶7GVÇÒrâ"“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B76W'DDÆV7B†–çBW‡V7FVDÖ–æ–×VÒÂ–çB7GVÂÂ7G&–ærFW67&—F–öâ¢°¢–b†7GVÂÂW‡V7FVDÖ–æ–×VÒ¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒFò&RBÆV7Bw¶W‡V7FVDÖ–æ–×V×ÒrÂ'WBf÷VæBw¶7GVÇÒrâ"“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B76W'DVæG5v—F‚‡7G&–æsòfÇVRÂ7G&–ærW‡V7FVE7Vff—‚Â7G&–ærFW67&—F–öâ¢°¢–b‡fÇVR—2çVÆÂÇÂfÇVRäVæG5v—F‚†W‡V7FVE7Vff—‚Â7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒFòVæBv—F‚w¶W‡V7FVE7Vff—‡ÒrÂ'WBf÷VæBw·fÇVRóò#ÆçVÆÃâ'Òrâ"“°¢Ğ¢Ğ¢Ğ¢"""“° ¢&WGW&â&ö¦V7EFƒ°¢Ğ ¢&—fFR7FF–27G&–ær&W&TW‡FW&æÄ6VçG&Å6¶vTÖævVÖVçD‡7G&–ærv÷&µ&ö÷BÂ7G&–ær6¶vTfVVB¢°¢–b„F—&V7F÷'’äW†—7G2‡v÷&µ&ö÷B’¢°¢F—&V7F÷'’äFVÆWFR‡v÷&µ&ö÷BÂ&V7W'6—fS¢G'VR“°¢Ğ ¢7G&–ær&ö÷BÒF‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ6VçG&Å6¶vTÖævVÖVçD76VÖ&Ç”æÖR“°¢F—&V7F÷'’ä7&VFTF—&V7F÷'’†&ö÷B“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ$çTvWBæ6öæf–r"’À¢B"" ¢Ã÷†ÖÂfW'6–öãÒ#ã"Væ6öF–æsÒ'WFbÓ‚#óà¢Æ6öæf–wW&F–öãà¢Æ6öæf–sà¢ÆFB¶W“Ò&vÆö&Å6¶vW4föÆFW""fÇVSÒ'µ6V7W&—G”VÆVÖVçBäW66R…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ"ç6¶vW2"’—Ò"óà¢Âö6öæf–sà¢Ç6¶vU6÷W&6W3à¢Æ6ÆV"óà¢ÆFB¶W“Ò%&ôuUwdÆö6Ä'F–f7G2"fÇVSÒ'µ6V7W&—G”VÆVÖVçBäW66R‡6¶vTfVVB—Ò"óà¢ÆFB¶W“Ò&çVvWBæ÷&r"fÇVSÒ&‡GG3¢òö’æçVvWBæ÷&r÷c2ö–æFW‚æ§6öâ"óà¢Â÷6¶vU6÷W&6W3à¢Âö6öæf–wW&F–öãà¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ$F—&V7F÷'’å6¶vW2ç&÷2"’À¢"" ¢Å&ö¦V7Cà¢Å&÷W'G”w&÷Wà¢ÄÖævU6¶vUfW'6–öç46VçG&ÆÇ“çG'VSÂôÖævU6¶vUfW'6–öç46VçG&ÆÇ“à¢Âõ&÷W'G”w&÷Wà ¢Ä—FVÔw&÷Wà¢Å6¶vUfW'6–öâ–æ6ÇVFSÒ%7—7FVÒå&V7F—fR"fW'6–öãÒ#bãã"óà¢Âô—FVÔw&÷Wà¢Âõ&ö¦V7Cà¢"""“° ¢7G&–ær&ö¦V7EF‚ÒF‚ä6öÖ&–æR†&ö÷BÂ6VçG&Å6¶vTÖævVÖVçD76VÖ&Ç”æÖR²"æ77&ö¢"“°¢w&—FTf–ÆR€¢&ö¦V7EF‚À¢7v—F6…we6F´öæÇ’€¢B"" ¢Å&ö¦V7B6F³Ò'´÷&–v–æÅwe6F·Ò#à¢Å&÷W'G”w&÷Wà¢Ä÷WGWEG—Såv–äW†SÂô÷WGWEG—Sà¢Ä76VÖ&Ç”æÖSç´6VçG&Å6¶vTÖævVÖVçD÷WGWD76VÖ&Ç”æÖWÓÂô76VÖ&Ç”æÖSà¢ÅF&vWDg&ÖWv÷&³ç´W‡FW&æÄF&vWDg&ÖWv÷&·ÓÂõF&vWDg&ÖWv÷&³à¢ÅW6UucçG'VSÂõW6Uucà¢Âõ&÷W'G”w&÷Wà ¢Ä—FVÔw&÷Wà¢Å6¶vU&VfW&Væ6R–æ6ÇVFSÒ%7—7FVÒå&V7F—fR"óà¢Âô—FVÔw&÷Wà¢Âõ&ö¦V7Cà¢"""À¢&W‡FW&æÂ4D²6VçG&Â6¶vRÖævVÖVçB"’“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$ç†ÖÂ"’À¢"" ¢ÄÆ–6F–öà¢ƒ¤6Æ73Ò$W‡FW&æÄ7Õ6F´ä ¢†ÖÆç3Ò&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ÷&W6VçFF–öâ ¢†ÖÆç3§ƒÒ&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ ¢7F'GWW&“Ò$Ö–åv–æF÷rç†ÖÂ"óà¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$ç†ÖÂæ72"’À¢"" ¢W6–ær7—7FVÒåv–æF÷w3° ¢æÖW76RW‡FW&æÄ7Õ6F´° ¢V&Æ–2'F–Â6Æ72¢Æ–6F–öà¢°¢Ğ¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$Ö–åv–æF÷rç†ÖÂ"’À¢"" ¢Åv–æF÷p¢ƒ¤6Æ73Ò$W‡FW&æÄ7Õ6F´äÖ–åv–æF÷r ¢†ÖÆç3Ò&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ÷&W6VçFF–öâ ¢†ÖÆç3§ƒÒ&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ ¢F—FÆSÒ$W‡FW&æÂ5Ò4D² ¢v–GFƒÒ#3# ¢†V–v‡CÒ#ƒ#à¢Äw&–Cà¢ÅFW‡D&Æö6°¢†÷&—¦öçFÄÆ–væÖVçCÒ$6VçFW" ¢fW'F–6ÄÆ–væÖVçCÒ$6VçFW" ¢FW‡CÒ$6VçG&Â6¶vRÖævVÖVçB"óà¢Âôw&–Cà¢Âõv–æF÷sà¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$Ö–åv–æF÷rç†ÖÂæ72"’À¢"" ¢W6–ær7—7FVÒåv–æF÷w3° ¢æÖW76RW‡FW&æÄ7Õ6F´° ¢V&Æ–2'F–Â6Æ72Ö–åv–æF÷r¢v–æF÷p¢°¢V&Æ–2Ö–åv–æF÷r‚¢°¢–æ—F–Æ—¦T6ö×öæVçB‚“°¢Ğ¢Ğ¢"""“° ¢&WGW&â&ö¦V7EFƒ°¢Ğ ¢&—fFR7FF–27G&–ær&W&TW‡FW&æÄÆö6Æ—¦F–öå&ö¦V7B‡7G&–ærv÷&µ&ö÷B¢°¢7G&–ærÆö6Æ—¦F–öå&ö÷BÒF‚ä6öÖ&–æR‡v÷&µ&ö÷BÂÆö6Æ—¦F–öä76VÖ&Ç”æÖR“°¢F—&V7F÷'’ä7&VFTF—&V7F÷'’†Æö6Æ—¦F–öå&ö÷B“° ¢7G&–ærÆö6Æ—¦F–öå&ö¦V7EF‚ÒF‚ä6öÖ&–æR†Æö6Æ—¦F–öå&ö÷BÂÆö6Æ—¦F–öä76VÖ&Ç”æÖR²"æ77&ö¢"“°¢w&—FTf–ÆR€¢Æö6Æ—¦F–öå&ö¦V7EF‚À¢7v—F6…we6F´öæÇ’€¢B"" ¢Å&ö¦V7B6F³Ò'´÷&–v–æÅwe6F·Ò#à¢Å&÷W'G”w&÷Wà¢ÅF&vWDg&ÖWv÷&³ç´W‡FW&æÄF&vWDg&ÖWv÷&·ÓÂõF&vWDg&ÖWv÷&³à¢ÅW6UucçG'VSÂõW6Uucà¢ÄÆö6Æ—¦F–öäF—&V7F—fW5FôÆö4f–ÆSäÆÃÂôÆö6Æ—¦F–öäF—&V7F—fW5FôÆö4f–ÆSà¢Âõ&÷W'G”w&÷Wà¢Âõ&ö¦V7Cà¢"""À¢&W‡FW&æÂ4D²Æö6Æ—¦F–öâ"’“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†Æö6Æ—¦F–öå&ö÷BÂ$Æö6Æ—¦VEf–Wrç†ÖÂ"’À¢"" ¢ÅW6W$6öçG&öÀ¢†ÖÆç3Ò&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ÷&W6VçFF–öâ ¢†ÖÆç3§ƒÒ&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ ¢ƒ¥V–CÒ$W‡FW&æÄÆö6Æ—¦F–öå&ö÷B ¢Æö6Æ—¦F–öâäGG&–'WFW3Ò"D6öçFVçB…&VF&ÆRVæÖöF–f–&ÆRFW‡B’ ¢Æö6Æ—¦F–öâä6öÖÖVçG3Ò"D6öçFVçB„W‡FW&æÂÆö6Æ—¦F–öâ&ö÷B6öÖÖVçB’#à¢ÅFW‡D&Æö6°¢ƒ¥V–CÒ$W‡FW&æÄÆö6Æ—¦F–öåFW‡B ¢Æö6Æ—¦F–öâäGG&–'WFW3Ò"D6öçFVçB…&VF&ÆRÖöF–f–&ÆRFW‡B’ ¢Æö6Æ—¦F–öâä6öÖÖVçG3Ò"D6öçFVçB„W‡FW&æÂÆö6Æ—¦F–öâFW‡B6öÖÖVçB’ ¢FW‡CÒ$W‡FW&æÂÆö6Æ—¦F–öâFW‡B"óà¢ÂõW6W$6öçG&öÃà¢"""“° ¢&WGW&âÆö6Æ—¦F–öå&ö¦V7EFƒ°¢Ğ ¢&—fFR7FF–27G&–ær&W&TW‡FW&æÄFVfVÇD—FV×4‡7G&–ærv÷&µ&ö÷B¢°¢7G&–ær&ö÷BÒF‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖR“°¢7G&–ærÆ–'&'•&ö÷BÒF‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖR“°¢F—&V7F÷'’ä7&VFTF—&V7F÷'’†&ö÷B“°¢F—&V7F÷'’ä7&VFTF—&V7F÷'’†Æ–'&'•&ö÷B“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†Æ–'&'•&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖR²"æ77&ö¢"’À¢7v—F6…we6F´öæÇ’€¢B"" ¢Å&ö¦V7B6F³Ò'´÷&–v–æÅwe6F·Ò#à¢Å&÷W'G”w&÷Wà¢ÅF&vWDg&ÖWv÷&³ç´W‡FW&æÄF&vWDg&ÖWv÷&·ÓÂõF&vWDg&ÖWv÷&³à¢ÅW6UucçG'VSÂõW6Uucà¢Âõ&÷W'G”w&÷Wà¢Âõ&ö¦V7Cà¢"""À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’"’“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†Æ–'&'•&ö÷BÂ%&÷W'F–W2"Â$76VÖ&Ç”–æfòæ72"’À¢"" ¢W6–ær7—7FVÒåv–æF÷w3° ¢¶76VÖ&Ç“¢F†VÖT–æfò…&W6÷W&6TF–7F–öæ'”Æö6F–öâäæöæRÂ&W6÷W&6TF–7F–öæ'”Æö6F–öâå6÷W&6T76VÖ&Ç’•Ğ¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†Æ–'&'•&ö÷BÂ$FVfVÇD—FV×4Æ–'&'•æVÂç†ÖÂ"’À¢"" ¢ÅW6W$6öçG&öÀ¢ƒ¤6Æ73Ò$W‡FW&æÅ6F´FVfVÇD—FV×4Æ–'&'’äFVfVÇD—FV×4Æ–'&'•æVÂ ¢ƒ¤æÖSÒ$FVfVÇD—FV×4Æ–'&'•æVÅ&ö÷B ¢†ÖÆç3Ò&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ÷&W6VçFF–öâ ¢†ÖÆç3§ƒÒ&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ#à¢Ä&÷&FW"FF–æsÒ#"#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$Æ–'&'•æVÄ6F–öâ ¢FW‡CÒ'´&–æF–ær6F–öâÂVÆVÖVçDæÖSÔFVfVÇD—FV×4Æ–'&'•æVÅ&ö÷GÒ"óà¢Âô&÷&FW#à¢ÂõW6W$6öçG&öÃà¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†Æ–'&'•&ö÷BÂ$FVfVÇD—FV×4Æ–'&'•æVÂç†ÖÂæ72"’À¢"" ¢W6–ær7—7FVÒåv–æF÷w3°¢W6–ær7—7FVÒåv–æF÷w2ä6öçG&öÇ3° ¢æÖW76RW‡FW&æÅ6F´FVfVÇD—FV×4Æ–'&'“° ¢V&Æ–2'F–Â6Æ72FVfVÇD—FV×4Æ–'&'•æVÂ¢W6W$6öçG&öÀ¢°¢V&Æ–27FF–2&VFöæÇ’FWVæFVæ7•&÷W'G’6F–öå&÷W'G’ÒFWVæFVæ7•&÷W'G’å&Vv—7FW"€¢æÖVöb„6F–öâ’À¢G—Vöb‡7G&–ær’À¢G—Vöb„FVfVÇD—FV×4Æ–'&'•æVÂ’À¢æWr&÷W'G”ÖWFFF‡7G&–æräV×G’’“° ¢V&Æ–2FVfVÇD—FV×4Æ–'&'•æVÂ‚¢°¢–æ—F–Æ—¦T6ö×öæVçB‚“°¢Ğ ¢V&Æ–27G&–ær6F–öà¢°¢vWBÓâ‡7G&–ær”vWEfÇVR„6F–öå&÷W'G’“°¢6WBÓâ6WEfÇVR„6F–öå&÷W'G’ÂfÇVR“°¢Ğ ¢V&Æ–27G&–ær6F–öåFW‡BÓâÆ–'&'•æVÄ6F–öâåFW‡C°¢Ğ¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†Æ–'&'•&ö÷BÂ$FVfVÇD—FV×4Æ–'&'•vRç†ÖÂ"’À¢"" ¢ÅvP¢ƒ¤6Æ73Ò$W‡FW&æÅ6F´FVfVÇD—FV×4Æ–'&'’äFVfVÇD—FV×4Æ–'&'•vR ¢†ÖÆç3Ò&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ÷&W6VçFF–öâ ¢†ÖÆç3§ƒÒ&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ ¢F—FÆSÒ$FVfVÇB—FVÒÆ–'&'’vR#à¢Ä&÷&FW"FF–æsÒ#"#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4Æ–'&'•vUFW‡B ¢FW‡CÒ$FVfVÇB—FVÒÆ–'&'’6ö×–ÆVBvRFW‡B"óà¢Âô&÷&FW#à¢ÂõvSà¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†Æ–'&'•&ö÷BÂ$FVfVÇD—FV×4Æ–'&'•vRç†ÖÂæ72"’À¢"" ¢W6–ær7—7FVÒåv–æF÷w2ä6öçG&öÇ3° ¢æÖW76RW‡FW&æÅ6F´FVfVÇD—FV×4Æ–'&'“° ¢V&Æ–2'F–Â6Æ72FVfVÇD—FV×4Æ–'&'•vR¢vP¢°¢V&Æ–2FVfVÇD—FV×4Æ–'&'•vR‚¢°¢–æ—F–Æ—¦T6ö×öæVçB‚“°¢Ğ ¢V&Æ–27G&–ærvUFW‡BÓâFVfVÇD—FV×4Æ–'&'•vUFW‡BåFW‡C°¢Ğ¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†Æ–'&'•&ö÷BÂ$FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂæ72"’À¢"" ¢W6–ær7—7FVÒåv–æF÷w3°¢W6–ær7—7FVÒåv–æF÷w2ä6öçG&öÇ3° ¢æÖW76RW‡FW&æÅ6F´FVfVÇD—FV×4Æ–'&'“° ¢V&Æ–26VÆVB6Æ72FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂ¢6öçG&öÀ¢°¢V&Æ–27FF–2&VFöæÇ’FWVæFVæ7•&÷W'G’FW‡E&÷W'G’ÒFWVæFVæ7•&÷W'G’å&Vv—7FW"€¢æÖVöb…FW‡B’À¢G—Vöb‡7G&–ær’À¢G—Vöb„FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂ’À¢æWrg&ÖWv÷&µ&÷W'G”ÖWFFF‡7G&–æräV×G’’“° ¢7FF–2FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂ‚¢°¢FVfVÇE7G–ÆT¶W•&÷W'G’ä÷fW'&–FTÖWFFF€¢G—Vöb„FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂ’À¢æWrg&ÖWv÷&µ&÷W'G”ÖWFFF‡G—Vöb„FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂ’’“°¢Ğ ¢V&Æ–27G&–ærFW‡@¢°¢vWBÓâ‡7G&–ær”vWEfÇVR…FW‡E&÷W'G’“°¢6WBÓâ6WEfÇVR…FW‡E&÷W'G’ÂfÇVR“°¢Ğ¢Ğ¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†Æ–'&'•&ö÷BÂ%F†VÖW2"Â$vVæW&–2ç†ÖÂ"’À¢"" ¢Å&W6÷W&6TF–7F–öæ'¢†ÖÆç3Ò&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ÷&W6VçFF–öâ ¢†ÖÆç3§ƒÒ&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ ¢†ÖÆç3¦Æö6ÃÒ&6Ç"ÖæÖW76S¤W‡FW&æÅ6F´FVfVÇD—FV×4Æ–'&'’#à¢Å7G–ÆRF&vWEG—SÒ'·ƒ¥G—RÆö6Ã¤FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÇÒ#à¢Å6WGFW"&÷W'G“Ò$&6¶w&÷VæB"fÇVSÒ"3CScsƒ’"óà¢Å6WGFW"&÷W'G“Ò$f÷&Vw&÷VæB"fÇVSÒ"3#3CSb"óà¢Å6WGFW"&÷W'G“Ò%FF–ær"fÇVSÒ#2"óà¢Å6WGFW"&÷W'G“Ò%FV×ÆFR#à¢Å6WGFW"åfÇVSà¢Ä6öçG&öÅFV×ÆFRF&vWEG—SÒ'·ƒ¥G—RÆö6Ã¤FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÇÒ#à¢Ä&÷&FW ¢ƒ¤æÖSÒ$FVfVÇD—FV×4Æ–'&'•F†VÖU&ö÷B ¢&6¶w&÷VæCÒ'µFV×ÆFT&–æF–ær&6¶w&÷VæGÒ ¢FF–æsÒ'µFV×ÆFT&–æF–ærFF–æwÒ#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4Æ–'&'•F†VÖUFW‡B ¢f÷&Vw&÷VæCÒ'µFV×ÆFT&–æF–ærf÷&Vw&÷VæGÒ ¢FW‡CÒ'µFV×ÆFT&–æF–ærFW‡GÒ"óà¢Âô&÷&FW#à¢Âô6öçG&öÅFV×ÆFSà¢Âõ6WGFW"åfÇVSà¢Âõ6WGFW#à¢Âõ7G–ÆSà¢Âõ&W6÷W&6TF–7F–öæ'“à¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†Æ–'&'•&ö÷BÂ%&W6÷W&6W2"Â$FVfVÇD—FV×4Æ–'&'•&W6÷W&6W2ç†ÖÂ"’À¢"" ¢Å&W6÷W&6TF–7F–öæ'¢†ÖÆç3Ò&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ÷&W6VçFF–öâ ¢†ÖÆç3§ƒÒ&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ ¢†ÖÆç3§7—3Ò&6Ç"ÖæÖW76S¥7—7FVÓ¶76VÖ&Ç“Õ7—7FVÒå&—fFRä6÷&TÆ–"#à¢Ç7—3¥7G&–ærƒ¤¶W“Ò$FVfVÇD—FV×4Æ–'&'•&W6÷W&6UFW‡B#äFVfVÇB—FVÒÆ–'&'’&W6÷W&6RFW‡CÂ÷7—3¥7G&–æsà¢Å6öÆ–D6öÆ÷$''W6€¢ƒ¤¶W“Ò$FVfVÇD—FV×4Æ–'&'•&W6÷W&6T''W6‚ ¢6öÆ÷#Ò"3scSC3""óà¢Âõ&W6÷W&6TF–7F–öæ'“à¢"""“° ¢7G&–ær&ö¦V7EF‚ÒF‚ä6öÖ&–æR†&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖR²"æ77&ö¢"“°¢w&—FTf–ÆR€¢&ö¦V7EF‚À¢7v—F6…we6F´öæÇ’€¢B"" ¢Å&ö¦V7B6F³Ò'´÷&–v–æÅv–æF÷w4FW6·F÷we6F·Ò#à¢Å&÷W'G”w&÷Wà¢Ä÷WGWEG—Såv–äW†SÂô÷WGWEG—Sà¢ÅF&vWDg&ÖWv÷&³ç´W‡FW&æÄF&vWDg&ÖWv÷&·ÓÂõF&vWDg&ÖWv÷&³à¢ÅW6UucçG'VSÂõW6Uucà¢Âõ&÷W'G”w&÷Wà ¢Ä—FVÔw&÷Wà¢Å&ö¦V7E&VfW&Væ6R–æ6ÇVFSÒ"ââ÷´FVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖWÒ÷´FVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖWÒæ77&ö¢"óà¢Âô—FVÔw&÷Wà¢Âõ&ö¦V7Cà¢"""À¢÷&–v–æÅv–æF÷w4FW6·F÷we6F²À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ"’“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$ç†ÖÂ"’À¢"" ¢ÄÆ–6F–öà¢ƒ¤6Æ73Ò$W‡FW&æÅ6F´FVfVÇD—FV×4ä ¢†ÖÆç3Ò&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ÷&W6VçFF–öâ ¢†ÖÆç3§ƒÒ&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ ¢†ÖÆç3§7—3Ò&6Ç"ÖæÖW76S¥7—7FVÓ¶76VÖ&Ç“Õ7—7FVÒå&—fFRä6÷&TÆ–" ¢7F'GWW&“Ò$Ö–åv–æF÷rç†ÖÂ ¢7F'GWÒ$öäFVfVÇD—FV×57F'GW ¢W†—CÒ$öäFVfVÇD—FV×4W†—B#à¢ÄÆ–6F–öâå&W6÷W&6W3à¢Å&W6÷W&6TF–7F–öæ'“à¢Å&W6÷W&6TF–7F–öæ'’äÖW&vVDF–7F–öæ&–W3à¢Å&W6÷W&6TF–7F–öæ'’6÷W&6SÒ%&W6÷W&6W2ôFVfVÇD—FV×4&W6÷W&6W2ç†ÖÂ"óà¢Âõ&W6÷W&6TF–7F–öæ'’äÖW&vVDF–7F–öæ&–W3à¢Ç7—3¥7G&–ærƒ¤¶W“Ò$FVfVÇD—FV×5FW‡B#äFVfVÇB—FVÒ&W6÷W&6RFW‡CÂ÷7—3¥7G&–æsà¢Å6öÆ–D6öÆ÷$''W6€¢ƒ¤¶W“Ò$FVfVÇD—FV×4''W6‚ ¢6öÆ÷#Ò"333SSsr"óà¢Âõ&W6÷W&6TF–7F–öæ'“à¢ÂôÆ–6F–öâå&W6÷W&6W3à¢ÂôÆ–6F–öãà¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$ç†ÖÂæ72"’À¢"" ¢W6–ær7—7FVÓ°¢W6–ær7—7FVÒåv–æF÷w3°¢W6–ær7—7FVÒåv–æF÷w2åF‡&VF–æs° ¢æÖW76RW‡FW&æÅ6F´FVfVÇD—FV×4° ¢V&Æ–2'F–Â6Æ72 ¢°¢&—fFR7FF–2&ööÂ5÷'VåfÆ–FF–öå&WVW7FVC° ¢V&Æ–27FF–2–çB7F'GWWfVçD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–27FF–2–çBW†—DWfVçD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–27FF–2&ööÂÖ–åv–æF÷ufÆ–FFVB²vWC²&—fFR6WC²Ğ ¢&÷FV7FVB÷fW'&–FRfö–Böå7F'GW…7F'GWWfVçD&w2R¢°¢–b„Vçf—&öæÖVçBävWDVçf—&öæÖVçEf&–&ÆR‚%$ôuUõueôU…DU$äÅôDTdTÅEõ%TåõdÄ”DDR"’ÓÒ#"¢°¢5÷'VåfÆ–FF–öå&WVW7FVBÒG'VS°¢&6Räöå7F'GW†R“°¢F—7F6†W"ä&Vv–ä–çfö¶R€¢F—7F6†W%&–÷&—G’äæ÷&ÖÂÀ¢æWr7F–öâ…fÆ–FFTæE6‡WFF÷vâ’“°¢&WGW&ã°¢Ğ ¢&6Räöå7F'GW†R“°¢Ğ ¢&÷FV7FVB÷fW'&–FRfö–BöäW†—B„W†—DWfVçD&w2R¢°¢&6RäöäW†—B†R“° ¢–b‡5÷'VåfÆ–FF–öå&WVW7FVB¢°¢&WV—&R…7F'GWWfVçD6÷VçBÓÒÂ$W‡V7FVBFVfVÇBÖ—FVÒ7F'GWWfVçBFò'Vâöæ6Râ"“°¢&WV—&R„W†—DWfVçD6÷VçBÓÒÂ$W‡V7FVBFVfVÇBÖ—FVÒW†—BWfVçBFò'Vâöæ6Râ"“°¢&WV—&R„Ö–åv–æF÷ufÆ–FFVBÂ$W‡V7FVBFVfVÇBÖ—FVÒÖ–åv–æF÷rfÆ–FF–öâFò'Vââ"“°¢6öç6öÆRåw&—FTÆ–æR‚$W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–6F–öâå'VâfÆ–FF–öâ7V66VVFVBâ"“°¢Ğ¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×57F'GW†ö&¦V7B6VæFW"Â7F'GWWfVçD&w2R¢°¢7F'GWWfVçD6÷VçB²³°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×4W†—B†ö&¦V7B6VæFW"ÂW†—DWfVçD&w2R¢°¢W†—DWfVçD6÷VçB²³°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFTæE6‡WFF÷vâ‚¢°¢–b„7W'&VçCòäÖ–åv–æF÷r—2æ÷BÖ–åv–æF÷rv–æF÷r¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡V7FVBFVfVÇBÖ—FVÒÆ–6F–öâäÖ–åv–æF÷râ"“°¢Ğ ¢v–æF÷råfÆ–FFTFVfVÇD—FV×5'Vâ‚“°¢Ö–åv–æF÷ufÆ–FFVBÒG'VS°¢7W'&VçBå6‡WFF÷vâƒ“°¢Ğ ¢&—fFR7FF–2fö–B&WV—&R†&ööÂ6öæF—F–öâÂ7G&–ærÖW76vR¢°¢–b‚6öæF—F–öâ¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ†ÖW76vR“°¢Ğ¢Ğ¢Ğ¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$æ6öæf–r"’À¢"" ¢Ã÷†ÖÂfW'6–öãÒ#ã"Væ6öF–æsÒ'WFbÓ‚"óà¢Æ6öæf–wW&F–öãà¢Æ6WGF–æw3à¢ÆFB¶W“Ò$FVfVÇD—FV×56Fµ6WGF–ær"fÇVSÒ$FVfVÇB—FVÒ4D²6öæf–rfÇVR"óà¢Âö6WGF–æw3à¢Âö6öæf–wW&F–öãà¢"""“°¢F—&V7F÷'’ä7&VFTF—&V7F÷'’…F‚ä6öÖ&–æR†&ö÷BÂ$76WG2"’“°¢f–ÆRåw&—FTÆÄ'—FW2€¢F‚ä6öÖ&–æR†&ö÷BÂ$76WG2"Â$FVfVÇD—FV×4–ÖvRçær"’À¢6öçfW'Bäg&öÔ&6ScE7G&–ær‚&•d$õ's´vvôå5V„UVt”44”'—Fs´SÄUe#FäuG£„Gt‡wu¤udD$§”ã4dt×–å$¥%STW&´¦vvsÓÒ"’“°¢f–ÆRåw&—FTÆÄ'—FW2€¢F‚ä6öÖ&–æR†&ö÷BÂ$76WG2"Â$FVfVÇD—FV×47W'6÷"æ7W""’À¢6öçfW'Bäg&öÔ&6ScE7G&–ær‚$4TTtft6t$tT”$BôÓÒ"’“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ%&W6÷W&6W2"Â$FVfVÇD—FV×4&W6÷W&6W2ç†ÖÂ"’À¢"" ¢Å&W6÷W&6TF–7F–öæ'¢†ÖÆç3Ò&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ÷&W6VçFF–öâ ¢†ÖÆç3§ƒÒ&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ ¢†ÖÆç3§7—3Ò&6Ç"ÖæÖW76S¥7—7FVÓ¶76VÖ&Ç“Õ7—7FVÒå&—fFRä6÷&TÆ–"#à¢Ç7—3¥7G&–ærƒ¤¶W“Ò$FVfVÇD—FV×4F–7F–öæ'•FW‡B#äFVfVÇB—FVÒF–7F–öæ'’FW‡CÂ÷7—3¥7G&–æsà¢Å6öÆ–D6öÆ÷$''W6€¢ƒ¤¶W“Ò$FVfVÇD—FV×4F–7F–öæ'”''W6‚ ¢6öÆ÷#Ò"3CCss“’"óà¢Âõ&W6÷W&6TF–7F–öæ'“à¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$FVfVÇD—FV×5æVÂç†ÖÂ"’À¢"" ¢ÅW6W$6öçG&öÀ¢ƒ¤6Æ73Ò$W‡FW&æÅ6F´FVfVÇD—FV×4äFVfVÇD—FV×5æVÂ ¢ƒ¤æÖSÒ$FVfVÇD—FV×5æVÅ&ö÷B ¢†ÖÆç3Ò&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ÷&W6VçFF–öâ ¢†ÖÆç3§ƒÒ&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ#à¢Ä&÷&FW"FF–æsÒ#"#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ%æVÄ6F–öâ ¢FW‡CÒ'´&–æF–ær6F–öâÂVÆVÖVçDæÖSÔFVfVÇD—FV×5æVÅ&ö÷GÒ"óà¢Âô&÷&FW#à¢ÂõW6W$6öçG&öÃà¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$FVfVÇD—FV×5æVÂç†ÖÂæ72"’À¢"" ¢W6–ær7—7FVÒåv–æF÷w3°¢W6–ær7—7FVÒåv–æF÷w2ä6öçG&öÇ3° ¢æÖW76RW‡FW&æÅ6F´FVfVÇD—FV×4° ¢V&Æ–2'F–Â6Æ72FVfVÇD—FV×5æVÂ¢W6W$6öçG&öÀ¢°¢V&Æ–27FF–2&VFöæÇ’FWVæFVæ7•&÷W'G’6F–öå&÷W'G’ÒFWVæFVæ7•&÷W'G’å&Vv—7FW"€¢æÖVöb„6F–öâ’À¢G—Vöb‡7G&–ær’À¢G—Vöb„FVfVÇD—FV×5æVÂ’À¢æWr&÷W'G”ÖWFFF‡7G&–æräV×G’’“° ¢V&Æ–2FVfVÇD—FV×5æVÂ‚¢°¢–æ—F–Æ—¦T6ö×öæVçB‚“°¢Ğ ¢V&Æ–27G&–ær6F–öà¢°¢vWBÓâ‡7G&–ær”vWEfÇVR„6F–öå&÷W'G’“°¢6WBÓâ6WEfÇVR„6F–öå&÷W'G’ÂfÇVR“°¢Ğ ¢V&Æ–27G&–ær6F–öåFW‡BÓâæVÄ6F–öâåFW‡C°¢Ğ¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$FVfVÇD—FV×5vRç†ÖÂ"’À¢"" ¢ÅvP¢ƒ¤6Æ73Ò$W‡FW&æÅ6F´FVfVÇD—FV×4äFVfVÇD—FV×5vR ¢†ÖÆç3Ò&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ÷&W6VçFF–öâ ¢†ÖÆç3§ƒÒ&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ ¢F—FÆSÒ$FVfVÇB—FVÒvR#à¢Ä&÷&FW"FF–æsÒ#"#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×5vUFW‡B ¢FW‡CÒ$FVfVÇB—FVÒ6ö×–ÆVBvRFW‡B"óà¢Âô&÷&FW#à¢ÂõvSà¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$FVfVÇD—FV×5vRç†ÖÂæ72"’À¢"" ¢W6–ær7—7FVÒåv–æF÷w2ä6öçG&öÇ3° ¢æÖW76RW‡FW&æÅ6F´FVfVÇD—FV×4° ¢V&Æ–2'F–Â6Æ72FVfVÇD—FV×5vR¢vP¢°¢V&Æ–2FVfVÇD—FV×5vR‚¢°¢–æ—F–Æ—¦T6ö×öæVçB‚“°¢Ğ ¢V&Æ–27G&–ærvUFW‡BÓâFVfVÇD—FV×5vUFW‡BåFW‡C°¢Ğ¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$Ö–åv–æF÷rç†ÖÂ"’À¢"" ¢Åv–æF÷p¢ƒ¤6Æ73Ò$W‡FW&æÅ6F´FVfVÇD—FV×4äÖ–åv–æF÷r ¢†ÖÆç3Ò&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ÷&W6VçFF–öâ ¢†ÖÆç3§ƒÒ&‡GG¢ò÷66†VÖ2æÖ–7&÷6ögBæ6öÒ÷v–æg‚ó#b÷†ÖÂ ¢†ÖÆç3¦6ö×öæVçDÖöFVÃÒ&6Ç"ÖæÖW76S¥7—7FVÒä6ö×öæVçDÖöFVÃ¶76VÖ&Ç“Õv–æF÷w4&6R ¢†ÖÆç3¦Æ–'&'“Ò&6Ç"ÖæÖW76S¤W‡FW&æÅ6F´FVfVÇD—FV×4Æ–'&'“¶76VÖ&Ç“ÔW‡FW&æÅ6F´FVfVÇD—FV×4Æ–'&'’ ¢†ÖÆç3¦Æö6ÃÒ&6Ç"ÖæÖW76S¤W‡FW&æÅ6F´FVfVÇD—FV×4 ¢†ÖÆç3§&–Ö—F—fW3Ò&6Ç"ÖæÖW76S¥7—7FVÒåv–æF÷w2ä6öçG&öÇ2å&–Ö—F—fW3¶76VÖ&Ç“Õ&W6VçFF–öäg&ÖWv÷&² ¢†ÖÆç3§7—3Ò&6Ç"ÖæÖW76S¥7—7FVÓ¶76VÖ&Ç“Õ7—7FVÒå'VçF–ÖR ¢F—FÆSÒ$W‡FW&æÂ4D²FVfVÇB—FV×2 ¢v–GFƒÒ##c ¢†V–v‡CÒ#C ¢ÆöFVCÒ$öäFVfVÇD—FV×5v–æF÷tÆöFVB#à¢Åv–æF÷rå&W6÷W&6W3à¢Å&W6÷W&6TF–7F–öæ'“à¢Å&W6÷W&6TF–7F–öæ'’äÖW&vVDF–7F–öæ&–W3à¢Å&W6÷W&6TF–7F–öæ'’6÷W&6SÒ"ôW‡FW&æÅ6F´FVfVÇD—FV×4Æ–'&'“¶6ö×öæVçBõ&W6÷W&6W2ôFVfVÇD—FV×4Æ–'&'•&W6÷W&6W2ç†ÖÂ"óà¢Âõ&W6÷W&6TF–7F–öæ'’äÖW&vVDF–7F–öæ&–W3à¢ÆÆö6Ã¤FVfVÇD—FV×4—FVĞ¢ƒ¤¶W“Ò$FVfVÇD—FV×5FV×ÆFT—FVÒ ¢æÖSÒ$FVfVÇB—FVÒFV×ÆFRFF"óà¢Äö&¦V7DFF&÷f–FW ¢ƒ¤¶W“Ò$FVfVÇD—FV×4ö&¦V7DFF&÷f–FW" ¢—47–æ6‡&öæ÷W3Ò$fÇ6R ¢ÖWF†öDæÖSÒ$7&VFUFW‡B ¢ö&¦V7EG—SÒ'·ƒ¥G—RÆö6Ã¤FVfVÇD—FV×5&÷f–FW%6÷W&6WÒ"óà¢Å†ÖÄFF&÷f–FW ¢ƒ¤¶W“Ò$FVfVÇD—FV×5†ÖÄFF&÷f–FW" ¢—47–æ6‡&öæ÷W3Ò$fÇ6R ¢…FƒÒ"öFVfVÇD—FV×2ö—FVÒ#à¢Çƒ¥„FFà¢ÆFVfVÇD—FV×2†ÖÆç3Ò"#à¢Æ—FVÒæÖSÒ$FVfVÇB—FVÒ„ÔÂ&÷f–FW"FW‡B"óà¢ÂöFVfVÇD—FV×3à¢Â÷ƒ¥„FFà¢Âõ†ÖÄFF&÷f–FW#à¢Ä6öÆÆV7F–öåf–Wu6÷W&6P¢ƒ¤¶W“Ò$FVfVÇD—FV×56÷'FVD—FV×2 ¢6÷W&6SÒ'´&–æF–ær—FV×7Ò#à¢Ä6öÆÆV7F–öåf–Wu6÷W&6Rå6÷'DFW67&—F–öç3à¢Æ6ö×öæVçDÖöFVÃ¥6÷'DFW67&—F–öà¢&÷W'G”æÖSÒ$æÖR ¢F—&V7F–öãÒ$FW66VæF–ær"óà¢Âô6öÆÆV7F–öåf–Wu6÷W&6Rå6÷'DFW67&—F–öç3à¢Âô6öÆÆV7F–öåf–Wu6÷W&6Sà¢ÄFFFV×ÆFRƒ¤¶W“Ò$FVfVÇD—FV×4w&÷W†VFW%FV×ÆFR#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4w&÷W†VFW%FW‡B ¢FW‡CÒ'´&–æF–æræÖRÂ7G&–ætf÷&ÖCÔFVfVÇBw&÷W¢³×Ò"óà¢ÂôFFFV×ÆFSà¢Ä6öÆÆV7F–öåf–Wu6÷W&6P¢ƒ¤¶W“Ò$FVfVÇD—FV×4w&÷WVD—FV×2 ¢6÷W&6SÒ'´&–æF–ær—FV×7Ò#à¢Ä6öÆÆV7F–öåf–Wu6÷W&6Räw&÷WFW67&—F–öç3à¢Å&÷W'G”w&÷WFW67&—F–öâ&÷W'G”æÖSÒ$¶–æB"óà¢Âô6öÆÆV7F–öåf–Wu6÷W&6Räw&÷WFW67&—F–öç3à¢Âô6öÆÆV7F–öåf–Wu6÷W&6Sà¢ÆÆö6Ã¤FVfVÇD—FV×4—FVÔæÖT6öçfW'FW ¢ƒ¤¶W“Ò$FVfVÇD—FV×4—FVÔæÖT6öçfW'FW""óà¢ÆÆö6Ã¤FVfVÇD—FV×57FGW56VÆV7F–öä6öçfW'FW ¢ƒ¤¶W“Ò$FVfVÇD—FV×57FGW56VÆV7F–öä6öçfW'FW""óà¢Å6öÆ–D6öÆ÷$''W6€¢ƒ¤¶W“Ò$FVfVÇD—FV×4g&VW¦&ÆT''W6‚ ¢6öÆ÷#Ò"3T#„3t ¢÷6—G“Ò#ãsR"óà¢Å6öÆ–D6öÆ÷$''W6€¢ƒ¤¶W“Ò$FVfVÇD—FV×5Vç6†&VD''W6‚ ¢ƒ¥6†&VCÒ$fÇ6R ¢6öÆ÷#Ò"43CT$""óà¢Å7G–ÆP¢ƒ¤¶W“Ò$FVfVÇD—FV×57FGW5G&–vvW%7G–ÆR ¢F&vWEG—SÒ'·ƒ¥G—RFW‡D&Æö6·Ò#à¢Å6WGFW ¢&÷W'G“Ò%Fr ¢fÇVSÒ&FVfVÇBÖ—FVÒG&–vvW"–æ7F—fR"óà¢Å7G–ÆRåG&–vvW'3à¢ÄFFG&–vvW ¢&–æF–æsÒ'´&–æF–ær7FGW7Ò ¢fÇVSÒ$FVfVÇB—FVÒfÆ–FFVB6÷W&6R#à¢Å6WGFW ¢&÷W'G“Ò%Fr ¢fÇVSÒ&FVfVÇBÖ—FVÒG&–vvW"7F—fR"óà¢ÂôFFG&–vvW#à¢Âõ7G–ÆRåG&–vvW'3à¢Âõ7G–ÆSà¢Å7G–ÆP¢ƒ¤¶W“Ò$FVfVÇD—FV×5&÷W'G•G&–vvW%7G–ÆR ¢F&vWEG—SÒ'·ƒ¥G—RFW‡D&Æö6·Ò#à¢Å6WGFW ¢&÷W'G“Ò%Fr ¢fÇVSÒ&FVfVÇBÖ—FVÒ&÷W'G’G&–vvW"–æ7F—fR"óà¢Å7G–ÆRåG&–vvW'3à¢ÅG&–vvW ¢&÷W'G“Ò$—4Væ&ÆVB ¢fÇVSÒ$fÇ6R#à¢Å6WGFW ¢&÷W'G“Ò%Fr ¢fÇVSÒ&FVfVÇBÖ—FVÒ&÷W'G’G&–vvW"7F—fR"óà¢ÂõG&–vvW#à¢Âõ7G–ÆRåG&–vvW'3à¢Âõ7G–ÆSà¢Å7G–ÆP¢ƒ¤¶W“Ò$FVfVÇD—FV×4&6UFW‡E7G–ÆR ¢F&vWEG—SÒ'·ƒ¥G—RFW‡D&Æö6·Ò#à¢Å6WGFW ¢&÷W'G“Ò$f÷&Vw&÷VæB ¢fÇVSÒ"4dc##SSsr"óà¢Å6WGFW ¢&÷W'G“Ò%Fr ¢fÇVSÒ&FVfVÇBÖ—FVÒ&6VDöâ&6R6WGFW""óà¢Âõ7G–ÆSà¢Å7G–ÆP¢ƒ¤¶W“Ò$FVfVÇD—FV×4&6VDöåFW‡E7G–ÆR ¢F&vWEG—SÒ'·ƒ¥G—RFW‡D&Æö6·Ò ¢&6VDöãÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×4&6UFW‡E7G–ÆWÒ#à¢Å6WGFW ¢&÷W'G“Ò%FW‡B ¢fÇVSÒ$FVfVÇB—FVÒ&6VBÖöâFW‡B"óà¢Âõ7G–ÆSà¢Å7G–ÆP¢ƒ¤¶W“Ò$FVfVÇD—FV×5FV×ÆFVD'WGFöå7G–ÆR ¢F&vWEG—SÒ'·ƒ¥G—R'WGFöçÒ#à¢Å6WGFW ¢&÷W'G“Ò$&6¶w&÷VæB ¢fÇVSÒ"4ddS„cdb"óà¢Å6WGFW"&÷W'G“Ò%FV×ÆFR#à¢Å6WGFW"åfÇVSà¢Ä6öçG&öÅFV×ÆFRF&vWEG—SÒ'·ƒ¥G—R'WGFöçÒ#à¢Ä&÷&FW ¢ƒ¤æÖSÒ$FVfVÇD—FV×5FV×ÆFT'WGFöå&ö÷B ¢&6¶w&÷VæCÒ'µFV×ÆFT&–æF–ær&6¶w&÷VæGÒ ¢FsÒ&FVfVÇBÖ—FVÒFV×ÆFRVæ&ÆVB#à¢Ä6öçFVçE&W6VçFW ¢ƒ¤æÖSÒ$FVfVÇD—FV×5FV×ÆFT'WGFöä6öçFVçB ¢6öçFVçCÒ'µFV×ÆFT&–æF–ær6öçFVçGÒ"óà¢Âô&÷&FW#à¢Ä6öçG&öÅFV×ÆFRåG&–vvW'3à¢ÅG&–vvW ¢&÷W'G“Ò$—4Væ&ÆVB ¢fÇVSÒ$fÇ6R#à¢Å6WGFW ¢F&vWDæÖSÒ$FVfVÇD—FV×5FV×ÆFT'WGFöå&ö÷B ¢&÷W'G“Ò%Fr ¢fÇVSÒ&FVfVÇBÖ—FVÒFV×ÆFRF—6&ÆVB"óà¢ÂõG&–vvW#à¢Âô6öçG&öÅFV×ÆFRåG&–vvW'3à¢Âô6öçG&öÅFV×ÆFSà¢Âõ6WGFW"åfÇVSà¢Âõ6WGFW#à¢Âõ7G–ÆSà¢Ä—FV×5æVÅFV×ÆFRƒ¤¶W“Ò$FVfVÇD—FV×4Æ—7D&÷„—FV×5æVÂ#à¢Å7F6µæVÂ÷&–VçFF–öãÒ$†÷&—¦öçFÂ"óà¢Âô—FV×5æVÅFV×ÆFSà¢Å7G–ÆP¢ƒ¤¶W“Ò$FVfVÇD—FV×4Æ—7D&÷„—FVÕ7G–ÆR ¢F&vWEG—SÒ'·ƒ¥G—RÆ—7D&÷„—FV×Ò#à¢Å6WGFW ¢&÷W'G“Ò%FF–ær ¢fÇVSÒ#2"óà¢Âõ7G–ÆSà¢Å7G–ÆP¢ƒ¤¶W“Ò$FVfVÇD—FV×4WfVçE6WGFW$'WGFöå7G–ÆR ¢F&vWEG—SÒ'·ƒ¥G—R'WGFöçÒ#à¢ÄWfVçE6WGFW ¢WfVçCÒ$6Æ–6² ¢†æFÆW#Ò$öäFVfVÇD—FV×4WfVçE6WGFW$6Æ–6²"óà¢Âõ7G–ÆSà¢ÄFFFV×ÆFRƒ¤¶W“Ò$FVfVÇD—FV×56VÆV7FVDÇ†FV×ÆFR#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×56VÆV7FVDÇ†FV×ÆFUFW‡B ¢FW‡CÒ'´&–æF–æræÖRÂ7G&–ætf÷&ÖCÕ6VÆV7FVBÇ†¢³×Ò"óà¢ÂôFFFV×ÆFSà¢ÄFFFV×ÆFRƒ¤¶W“Ò$FVfVÇD—FV×56VÆV7FVD&WFFV×ÆFR#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×56VÆV7FVD&WFFV×ÆFUFW‡B ¢FW‡CÒ'´&–æF–æræÖRÂ7G&–ætf÷&ÖCÕ6VÆV7FVB&WF¢³×Ò"óà¢ÂôFFFV×ÆFSà¢ÆÆö6Ã¤FVfVÇD—FV×4æÖUFV×ÆFU6VÆV7F÷ ¢ƒ¤¶W“Ò$FVfVÇD—FV×4æÖUFV×ÆFU6VÆV7F÷""óà¢ÄFFFV×ÆFRFFG—SÒ'·ƒ¥G—RÆö6Ã¤FVfVÇD—FV×4—FV×Ò#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4–×Æ–6—EFV×ÆFUFW‡B ¢FsÒ&FVfVÇBÖ—FVÒ–×Æ–6—BFV×ÆFR ¢FW‡CÒ'´&–æF–æræÖRÂ7G&–ætf÷&ÖCÕFV×ÆFS¢³×Ò"óà¢ÂôFFFV×ÆFSà¢Ä†–W&&6†–6ÄFFFV×ÆFP¢ƒ¤¶W“Ò$FVfVÇD—FV×4æöFUFV×ÆFR ¢—FV×56÷W&6SÒ'´&–æF–ær6†–ÆG&VçÒ#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4æöFUFV×ÆFUFW‡B ¢FW‡CÒ'´&–æF–æræÖRÂ7G&–ætf÷&ÖCÔæöFS¢³×Ò"óà¢Âô†–W&&6†–6ÄFFFV×ÆFSà¢Âõ&W6÷W&6TF–7F–öæ'“à¢Âõv–æF÷rå&W6÷W&6W3à¢Åv–æF÷rä6öÖÖæD&–æF–æw3à¢Ä6öÖÖæD&–æF–æp¢6öÖÖæCÒ'·ƒ¥7FF–2Æö6Ã¤Ö–åv–æF÷räFVfVÇD—FV×46öÖÖæGÒ ¢6äW†V7WFSÒ$öäFVfVÇD—FV×46öÖÖæD6äW†V7WFR ¢W†V7WFVCÒ$öäFVfVÇD—FV×46öÖÖæDW†V7WFVB"óà¢Âõv–æF÷rä6öÖÖæD&–æF–æw3à¢Åv–æF÷rä–çWD&–æF–æw3à¢Ä¶W”&–æF–æp¢6öÖÖæCÒ'·ƒ¥7FF–2Æö6Ã¤Ö–åv–æF÷räFVfVÇD—FV×46öÖÖæGÒ ¢6öÖÖæE&ÖWFW#Ò&–çWBÖ&–æF–ær ¢¶W“Ò$" ¢ÖöF–f–W'3Ò$6öçG&öÂ"óà¢Âõv–æF÷rä–çWD&–æF–æw3à¢Å7F6µæVÃà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×5F—FÆUFW‡B ¢f÷&Vw&÷VæCÒ'´G–æÖ–5&W6÷W&6RFVfVÇD—FV×4''W6‡Ò ¢FW‡CÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×5FW‡GÒ"óà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4F–7F–öæ'•FW‡B ¢f÷&Vw&÷VæCÒ'´G–æÖ–5&W6÷W&6RFVfVÇD—FV×4F–7F–öæ'”''W6‡Ò ¢FW‡CÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×4F–7F–öæ'•FW‡GÒ"óà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4Æ–'&'•&W6÷W&6UFW‡B ¢f÷&Vw&÷VæCÒ'´G–æÖ–5&W6÷W&6RFVfVÇD—FV×4Æ–'&'•&W6÷W&6T''W6‡Ò ¢FW‡CÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×4Æ–'&'•&W6÷W&6UFW‡GÒ"óà¢Ä–ÖvP¢ƒ¤æÖSÒ$FVfVÇD—FV×4–ÖvR ¢v–GFƒÒ#" ¢†V–v‡CÒ#" ¢6÷W&6SÒ$76WG2ôFVfVÇD—FV×4–ÖvRçær"óà¢Å&V7FævÆP¢ƒ¤æÖSÒ$FVfVÇD—FV×4–ÖvT''W6…&V7FævÆR ¢v–GFƒÒ#" ¢†V–v‡CÒ#"#à¢Å&V7FævÆRäf–ÆÃà¢Ä–ÖvT''W6‚–ÖvU6÷W&6SÒ'6³¢òöÆ–6F–öã¢ÂÂÂô76WG2ôFVfVÇD—FV×4–ÖvRçær"óà¢Âõ&V7FævÆRäf–ÆÃà¢Âõ&V7FævÆSà¢Ä&÷&FW ¢ƒ¤æÖSÒ$FVfVÇD—FV×47W'6÷%F&vWB ¢v–GFƒÒ#B ¢†V–v‡CÒ#B ¢7W'6÷#Ò$76WG2ôFVfVÇD—FV×47W'6÷"æ7W""óà¢Å&–6…FW‡D&÷€¢ƒ¤æÖSÒ$FVfVÇD—FV×5&–6…FW‡D&÷‚ ¢—5&VDöæÇ“Ò$fÇ6R#à¢ÄfÆ÷tFö7VÖVçBvUFF–æsÒ#B#à¢Å&w&ƒà¢Å'VâFW‡CÒ$FVfVÇB—FVÒ"óà¢Ä&öÆCãÅ'VâFW‡CÒ'&–6‚FW‡B"óãÂô&öÆCà¢Ä—FÆ–3ãÅ'VâFW‡CÒ"—FÆ–2FW‡B"óãÂô—FÆ–3à¢Âõ&w&ƒà¢ÄÆ—7BÖ&¶W%7G–ÆSÒ$FV6–ÖÂ#à¢ÄÆ—7D—FVÓà¢Å&w&ƒãÅ'VâFW‡CÒ$FVfVÇB—FVÒÆ—7BVçG'’"óãÂõ&w&ƒà¢ÂôÆ—7D—FVÓà¢ÂôÆ—7Cà¢Ä&Æö6µT”6öçF–æW#à¢ÅFW‡D&Æö6²FW‡CÒ$FVfVÇB—FVÒ&Æö6²T’"óà¢Âô&Æö6µT”6öçF–æW#à¢ÂôfÆ÷tFö7VÖVçCà¢Âõ&–6…FW‡D&÷ƒà¢Å7F6µæVÂ÷&–VçFF–öãÒ$†÷&—¦öçFÂ#à¢Ä'WGFöà¢ƒ¤æÖSÒ$FVfVÇD—FV×46÷•&–6…FW‡D'WGFöâ ¢6öÖÖæCÒ$Æ–6F–öä6öÖÖæG2ä6÷’ ¢6öÖÖæEF&vWCÒ'´&–æF–ærVÆVÖVçDæÖSÔFVfVÇD—FV×5&–6…FW‡D&÷‡Ò ¢6öçFVçCÒ$6÷’FVfVÇB&–6‚FW‡B"óà¢Ä'WGFöà¢ƒ¤æÖSÒ$FVfVÇD—FV×57FU&–6…FW‡D'WGFöâ ¢6öÖÖæCÒ$Æ–6F–öä6öÖÖæG2å7FR ¢6öÖÖæEF&vWCÒ'´&–æF–ærVÆVÖVçDæÖSÔFVfVÇD—FV×5&–6…FW‡D&÷‡Ò ¢6öçFVçCÒ%7FRFVfVÇB&–6‚FW‡B"óà¢Âõ7F6µæVÃà¢ÄfÆ÷tFö7VÖVçE67&öÆÅf–WvW ¢ƒ¤æÖSÒ$FVfVÇD—FV×4fÆ÷tFö7VÖVçE67&öÆÅf–WvW" ¢fW'F–6Å67&öÆÄ&%f—6–&–Æ—G“Ò$WFò#à¢ÄfÆ÷tFö7VÖVçBvUFF–æsÒ#R#à¢Å&w&ƒà¢Å'VâFW‡CÒ$FVfVÇB—FVÒ67&öÆÂFö7VÖVçB"óà¢Âõ&w&ƒà¢ÂôfÆ÷tFö7VÖVçCà¢ÂôfÆ÷tFö7VÖVçE67&öÆÅf–WvW#à¢ÅFW‡D&÷€¢ƒ¤æÖSÒ$FVfVÇD—FV×57VÆÄ6†V6µFW‡D&÷‚ ¢7VÆÄ6†V6²ä—4Væ&ÆVCÒ%G'VR ¢7VÆÄ6†V6²å7VÆÆ–æu&Vf÷&ÓÒ%&TæE÷7G&Vf÷&Ò ¢FW‡CÒ&FVfVÇB—FVÒ7VÆÆ6†V6²FW‡B"óà¢Äw&–Bƒ¤æÖSÒ$FVfVÇD—FV×4w&–B#à¢Äw&–Bå&÷tFVf–æ—F–öç3à¢Å&÷tFVf–æ—F–öâ†V–v‡CÒ$WFò"óà¢Å&÷tFVf–æ—F–öâ†V–v‡CÒ$WFò"óà¢Âôw&–Bå&÷tFVf–æ—F–öç3à¢Äw&–Bä6öÇVÖäFVf–æ—F–öç3à¢Ä6öÇVÖäFVf–æ—F–öâv–GFƒÒ$WFò"óà¢Ä6öÇVÖäFVf–æ—F–öâv–GFƒÒ"¢"óà¢Âôw&–Bä6öÇVÖäFVf–æ—F–öç3à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4w&–D÷&–v–åFW‡B ¢w&–Bå&÷sÒ# ¢w&–Bä6öÇVÖãÒ# ¢FW‡CÒ$FVfVÇB—FVÒw&–B÷&–v–â"óà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4w&–D&÷VæEFW‡B ¢w&–Bå&÷sÒ# ¢w&–Bä6öÇVÖãÒ# ¢FW‡CÒ'´&–æF–ær7FGW2Â7G&–ætf÷&ÖCÔw&–C¢³×Ò"óà¢Âôw&–Cà¢ÄFö6µæVÀ¢ƒ¤æÖSÒ$FVfVÇD—FV×4Fö6µæVÂ ¢Æ7D6†–ÆDf–ÆÃÒ$fÇ6R#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4Fö6¶VEFW‡B ¢Fö6µæVÂäFö6³Ò$ÆVgB ¢FW‡CÒ$FVfVÇB—FVÒFö6²ÆVgB"óà¢ÅFW‡D&Æö6²FW‡CÒ$FVfVÇB—FVÒFö6²G&–Æ–ær"óà¢ÂôFö6µæVÃà¢Ä6çf0¢ƒ¤æÖSÒ$FVfVÇD—FV×46çf2 ¢v–GFƒÒ#ƒ ¢†V–v‡CÒ##B#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×46çf46†–ÆB ¢6çf2äÆVgCÒ#" ¢6çf2åF÷Ò#b ¢FW‡CÒ$FVfVÇB—FVÒ6çf2"óà¢Âô6çf3à¢ÅVæ–f÷&Ôw&–@¢ƒ¤æÖSÒ$FVfVÇD—FV×5Væ–f÷&Ôw&–B ¢&÷w3Ò# ¢6öÇVÖç3Ò#"#à¢ÅFW‡D&Æö6²FW‡CÒ$FVfVÇB—FVÒVæ–f÷&ÒöæR"óà¢ÅFW‡D&Æö6²FW‡CÒ$FVfVÇB—FVÒVæ–f÷&ÒGvò"óà¢ÂõVæ–f÷&Ôw&–Cà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×5G&ç6f÷&ÖVEFW‡B ¢&VæFW%G&ç6f÷&Ô÷&–v–ãÒ#ãRÃãR ¢FW‡CÒ$FVfVÇB—FVÒG&ç6f÷&ÖVB#à¢ÅFW‡D&Æö6²å&VæFW%G&ç6f÷&Óà¢ÅG&ç6f÷&Ôw&÷Wà¢Å66ÆUG&ç6f÷&Ò66ÆUƒÒ#ã#R"66ÆU“Ò#ãsR"óà¢Å&÷FFUG&ç6f÷&ÒævÆSÒ#R"óà¢ÅG&ç6ÆFUG&ç6f÷&ÒƒÒ#2"“Ò#B"óà¢ÂõG&ç6f÷&Ôw&÷Wà¢ÂõFW‡D&Æö6²å&VæFW%G&ç6f÷&Óà¢ÅFW‡D&Æö6²äÆ–÷WEG&ç6f÷&Óà¢Å6¶WuG&ç6f÷&ÒævÆUƒÒ#R"ævÆU“Ò#"óà¢ÂõFW‡D&Æö6²äÆ–÷WEG&ç6f÷&Óà¢ÂõFW‡D&Æö6³à¢ÄÆ&VÀ¢ƒ¤æÖSÒ$FVfVÇD—FV×57FGW4Æ&VÂ ¢6öçFVçCÒ%õ7FGW2 ¢F&vWCÒ'´&–æF–ærVÆVÖVçDæÖSÔFVfVÇD—FV×4VF—F&ÆU7FGW5FW‡D&÷‡Ò"óà¢Äw&÷W&÷€¢ƒ¤æÖSÒ$FVfVÇD—FV×4w&÷W&÷‚ ¢†VFW#Ò$FVfVÇB—FVÒw&÷W#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4w&÷W&÷…FW‡B ¢FW‡CÒ'´&–æF–ær7FGW2Â7G&–ætf÷&ÖCÔw&÷W¢³×Ò"óà¢Âôw&÷W&÷ƒà¢Å67&öÆÅf–WvW ¢ƒ¤æÖSÒ$FVfVÇD—FV×567&öÆÅf–WvW" ¢6ä6öçFVçE67&öÆÃÒ$fÇ6R ¢†÷&—¦öçFÅ67&öÆÄ&%f—6–&–Æ—G“Ò$F—6&ÆVB ¢fW'F–6Å67&öÆÄ&%f—6–&–Æ—G“Ò$WFò#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×567&öÆÅf–WvW%FW‡B ¢FW‡CÒ$FVfVÇB—FVÒ67&öÆÂf–WvW"6öçFVçB"óà¢Âõ67&öÆÅf–WvW#à¢Ä6†V6´&÷€¢ƒ¤æÖSÒ$FVfVÇD—FV×46†V6´&÷‚ ¢6öçFVçCÒ$FVfVÇB—FVÒ6†V6²&÷‚ ¢—46†V6¶VCÒ'´&–æF–ær—4f÷&Ô÷F–öäVæ&ÆVBÂÖöFSÕGvõv—Ò"óà¢Å7F6µæVÀ¢ƒ¤æÖSÒ$FVfVÇD—FV×5&F–õæVÂ ¢÷&–VçFF–öãÒ$†÷&—¦öçFÂ#à¢Å&F–ô'WGFöà¢ƒ¤æÖSÒ$FVfVÇD—FV×4f—'7E&F–ô'WGFöâ ¢6†V6¶VCÒ$öäFVfVÇD—FV×5&F–ô'WGFöä6†V6¶VB ¢6öçFVçCÒ$FVfVÇB—FVÒf—'7BÖöFR ¢w&÷WæÖSÒ$FVfVÇD—FV×4ÖöFR ¢—46†V6¶VCÒ%G'VR"óà¢Å&F–ô'WGFöà¢ƒ¤æÖSÒ$FVfVÇD—FV×56V6öæE&F–ô'WGFöâ ¢6†V6¶VCÒ$öäFVfVÇD—FV×5&F–ô'WGFöä6†V6¶VB ¢6öçFVçCÒ$FVfVÇB—FVÒ6V6öæBÖöFR ¢w&÷WæÖSÒ$FVfVÇD—FV×4ÖöFR"óà¢Âõ7F6µæVÃà¢Å6Æ–FW ¢ƒ¤æÖSÒ$FVfVÇD—FV×56Æ–FW" ¢—56æFõF–6´Væ&ÆVCÒ%G'VR ¢Ö†–×VÓÒ# ¢Ö–æ–×VÓÒ# ¢F–6´g&WVVæ7“Ò# ¢fÇVSÒ'´&–æF–ærf÷&Õ&öw&W72ÂÖöFSÕGvõv’ÂWFFU6÷W&6UG&–vvW#Õ&÷W'G”6†ævVGÒ"óà¢Å&öw&W74& ¢ƒ¤æÖSÒ$FVfVÇD—FV×5&öw&W74&" ¢Ö†–×VÓÒ# ¢Ö–æ–×VÓÒ# ¢fÇVSÒ'´&–æF–ærf÷&Õ&öw&W77Ò"óà¢Å77v÷&D&÷€¢ƒ¤æÖSÒ$FVfVÇD—FV×577v÷&D&÷‚ ¢Ö„ÆVæwFƒÒ#3" ¢77v÷&D6†ævVCÒ$öäFVfVÇD—FV×577v÷&D6†ævVB ¢77v÷&D6†#Ò"¢"óà¢Ä6ÆVæF ¢ƒ¤æÖSÒ$FVfVÇD—FV×46ÆVæF" ¢F—7Æ”FFSÒ###bÓbÓ#B ¢6VÆV7FVDFFSÒ'´&–æF–ær6VÆV7FVDFFRÂÖöFSÕGvõv—Ò ¢6VÆV7F–öäÖöFSÒ%6–ævÆTFFR"óà¢ÄFFU–6¶W ¢ƒ¤æÖSÒ$FVfVÇD—FV×4FFU–6¶W" ¢F—7Æ”FFU7F'CÒ###bÓÓ ¢F—7Æ”FFTVæCÒ###bÓ"Ó3 ¢6VÆV7FVDFFSÒ'´&–æF–ær6VÆV7FVDFFRÂÖöFSÕGvõv—Ò ¢6VÆV7FVDFFTf÷&ÖCÒ$Æöær"óà¢Ä'WGFöà¢ƒ¤æÖSÒ$FVfVÇD—FV×5÷W÷væW$'WGFöâ ¢6öçFVçCÒ$FVfVÇB—FVÒ÷W÷væW"#à¢Ä'WGFöâåFööÅF—à¢ÅFööÅF—ƒ¤æÖSÒ$FVfVÇD—FV×5FööÅF—#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×5FööÅF—FW‡B ¢FW‡CÒ$FVfVÇB—FVÒFööÇF—6öçFVçB"óà¢ÂõFööÅF—à¢Âô'WGFöâåFööÅF—à¢Âô'WGFöãà¢Ç&–Ö—F—fW3¥÷W ¢ƒ¤æÖSÒ$FVfVÇD—FV×57FæFÆöæU÷W ¢ÆÆ÷w5G&ç7&Væ7“Ò%G'VR ¢Æ6VÖVçCÒ$&÷GFöÒ ¢Æ6VÖVçEF&vWCÒ'´&–æF–ærVÆVÖVçDæÖSÔFVfVÇD—FV×5÷W÷væW$'WGFöçÒ ¢7F—4÷VãÒ$fÇ6R#à¢Ä&÷&FW"FF–æsÒ#"#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×57FæFÆöæU÷WFW‡B ¢FW‡CÒ$FVfVÇB—FVÒ7FæFÆöæR÷W6öçFVçB"óà¢Âô&÷&FW#à¢Â÷&–Ö—F—fW3¥÷Wà¢ÄÖVçRƒ¤æÖSÒ$FVfVÇD—FV×4ÖVçR#à¢ÄÖVçT—FVĞ¢ƒ¤æÖSÒ$FVfVÇD—FV×5&ö÷DÖVçT—FVÒ ¢†VFW#Ò%ôf–ÆR#à¢ÄÖVçT—FVĞ¢ƒ¤æÖSÒ$FVfVÇD—FV×46öÖÖæDÖVçT—FVÒ ¢6öÖÖæCÒ'·ƒ¥7FF–2Æö6Ã¤Ö–åv–æF÷räFVfVÇD—FV×46öÖÖæGÒ ¢6öÖÖæE&ÖWFW#Ò&ÖVçRÖ6öÖÖæB ¢†VFW#Ò%õ'Vâ"óà¢Å6W&F÷"ƒ¤æÖSÒ$FVfVÇD—FV×4ÖVçU6W&F÷""óà¢ÄÖVçT—FVĞ¢ƒ¤æÖSÒ$FVfVÇD—FV×46Æ–6´ÖVçT—FVÒ ¢6Æ–6³Ò$öäFVfVÇD—FV×4ÖVçT—FVÔ6Æ–6² ¢†VFW#Ò%ô6Æ–6²"óà¢ÄÖVçT—FVĞ¢ƒ¤æÖSÒ$FVfVÇD—FV×46†V6¶&ÆTÖVçT—FVÒ ¢6†V6¶VCÒ$öäFVfVÇD—FV×4ÖVçT—FVÔ6†V6¶VB ¢†VFW#Ò%ô6†V6² ¢—46†V6¶&ÆSÒ%G'VR ¢Væ6†V6¶VCÒ$öäFVfVÇD—FV×4ÖVçT—FVÕVæ6†V6¶VB"óà¢ÂôÖVçT—FVÓà¢ÂôÖVçSà¢ÅFööÄ&%G&’ƒ¤æÖSÒ$FVfVÇD—FV×5FööÄ&%G&’#à¢ÅFööÄ&"ƒ¤æÖSÒ$FVfVÇD—FV×5FööÄ&"#à¢Ä'WGFöà¢ƒ¤æÖSÒ$FVfVÇD—FV×5FööÄ&$6öÖÖæD'WGFöâ ¢6öÖÖæCÒ'·ƒ¥7FF–2Æö6Ã¤Ö–åv–æF÷räFVfVÇD—FV×46öÖÖæGÒ ¢6öÖÖæE&ÖWFW#Ò'FööÆ&"Ö6öÖÖæB ¢6öçFVçCÒ$FVfVÇB—FVÒFööÆ&"6öÖÖæB"óà¢Å6W&F÷"ƒ¤æÖSÒ$FVfVÇD—FV×5FööÄ&%6W&F÷""óà¢ÅFövvÆT'WGFöà¢ƒ¤æÖSÒ$FVfVÇD—FV×5FööÄ&%FövvÆR ¢6öçFVçCÒ$FVfVÇB—FVÒFööÆ&"FövvÆR ¢—46†V6¶VCÒ%G'VR"óà¢ÂõFööÄ&#à¢ÂõFööÄ&%G&“à¢Å7FGW4&"ƒ¤æÖSÒ$FVfVÇD—FV×57FGW4&"#à¢Å7FGW4&$—FVÒƒ¤æÖSÒ$FVfVÇD—FV×57FGW4&$—FVÒ#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×57FGW4&%FW‡B ¢FW‡CÒ'´&–æF–ær7FGW2Â7G&–ætf÷&ÖCÕ7FGW3¢³×Ò"óà¢Âõ7FGW4&$—FVÓà¢Âõ7FGW4&#à¢Ä'WGFöà¢ƒ¤æÖSÒ$FVfVÇD—FV×46öçFW‡DÖVçT÷væW" ¢6öçFVçCÒ$FVfVÇB—FVÒ6öçFW‡BÖVçR÷væW"#à¢Ä'WGFöâä6öçFW‡DÖVçSà¢Ä6öçFW‡DÖVçRƒ¤æÖSÒ$FVfVÇD—FV×46öçFW‡DÖVçR#à¢ÄÖVçT—FVĞ¢ƒ¤æÖSÒ$FVfVÇD—FV×46öçFW‡D6öÖÖæDÖVçT—FVÒ ¢6öÖÖæCÒ'·ƒ¥7FF–2Æö6Ã¤Ö–åv–æF÷räFVfVÇD—FV×46öÖÖæGÒ ¢6öÖÖæE&ÖWFW#Ò&6öçFW‡BÖ6öÖÖæB ¢†VFW#Ò$6öçFW‡B'Vâ"óà¢Å6W&F÷"ƒ¤æÖSÒ$FVfVÇD—FV×46öçFW‡DÖVçU6W&F÷""óà¢ÄÖVçT—FVĞ¢ƒ¤æÖSÒ$FVfVÇD—FV×46öçFW‡D6Æ–6´ÖVçT—FVÒ ¢6Æ–6³Ò$öäFVfVÇD—FV×46öçFW‡DÖVçT—FVÔ6Æ–6² ¢†VFW#Ò$6öçFW‡B6Æ–6²"óà¢ÄÖVçT—FVĞ¢ƒ¤æÖSÒ$FVfVÇD—FV×46öçFW‡D6†V6¶&ÆTÖVçT—FVÒ ¢6†V6¶VCÒ$öäFVfVÇD—FV×46öçFW‡DÖVçT—FVÔ6†V6¶VB ¢†VFW#Ò$6öçFW‡B6†V6² ¢—46†V6¶&ÆSÒ%G'VR ¢Væ6†V6¶VCÒ$öäFVfVÇD—FV×46öçFW‡DÖVçT—FVÕVæ6†V6¶VB"óà¢Âô6öçFW‡DÖVçSà¢Âô'WGFöâä6öçFW‡DÖVçSà¢Âô'WGFöãà¢ÅF$6öçG&öÀ¢ƒ¤æÖSÒ$FVfVÇD—FV×5F$6öçG&öÂ ¢6VÆV7FVD–æFWƒÒ##à¢ÅF$—FVĞ¢ƒ¤æÖSÒ$FVfVÇD—FV×4÷fW'f–WuF" ¢†VFW#Ò$÷fW'f–Wr#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4÷fW'f–WuF%FW‡B ¢FW‡CÒ$FVfVÇB—FVÒ÷fW'f–WrF""óà¢ÂõF$—FVÓà¢ÅF$—FVĞ¢ƒ¤æÖSÒ$FVfVÇD—FV×4FWF–Ç5F" ¢†VFW#Ò$FWF–Ç2#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4FWF–Ç5F%FW‡B ¢FW‡CÒ'´&–æF–ær7FGW2Â7G&–ætf÷&ÖCÕF#¢³×Ò"óà¢ÂõF$—FVÓà¢ÂõF$6öçG&öÃà¢ÄW‡æFW ¢ƒ¤æÖSÒ$FVfVÇD—FV×4W‡æFW" ¢†VFW#Ò$FVfVÇB—FVÒW‡æFW" ¢—4W‡æFVCÒ$fÇ6R ¢W‡æFVCÒ$öäFVfVÇD—FV×4W‡æFW$W‡æFVB ¢6öÆÆ6VCÒ$öäFVfVÇD—FV×4W‡æFW$6öÆÆ6VB#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4W‡æFW%FW‡B ¢FW‡CÒ$FVfVÇB—FVÒW‡æFW"6öçFVçB"óà¢ÂôW‡æFW#à¢ÆÆö6Ã¤FVfVÇD—FV×5æVÀ¢ƒ¤æÖSÒ$FVfVÇD—FV×5æVÂ ¢6F–öãÒ$FVfVÇB—FVÒæVÂ6F–öâ"óà¢ÆÆ–'&'“¤FVfVÇD—FV×4Æ–'&'•æVÀ¢ƒ¤æÖSÒ$FVfVÇD—FV×4Æ–'&'•æVÂ ¢6F–öãÒ$FVfVÇB—FVÒ&VfW&Væ6VBÆ–'&'’6F–öâ"óà¢ÆÆ–'&'“¤FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÀ¢ƒ¤æÖSÒ$FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂ ¢FW‡CÒ$FVfVÇB—FVÒÆ–'&'’F†VÖRFW‡B"óà¢Äg&ÖP¢ƒ¤æÖSÒ$FVfVÇD—FV×4g&ÖR ¢6÷W&6SÒ$FVfVÇD—FV×5vRç†ÖÂ ¢æf–vF–öåT•f—6–&–Æ—G“Ò$†–FFVâ"óà¢Ä6öçFVçE&W6VçFW ¢ƒ¤æÖSÒ$FVfVÇD—FV×4–×Æ–6—EFV×ÆFU&W6VçFW" ¢6öçFVçCÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×5FV×ÆFT—FV×Ò"óà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4ö&¦V7E&÷f–FW%FW‡B ¢FW‡CÒ'´&–æF–ær6÷W&6S×µ7FF–5&W6÷W&6RFVfVÇD—FV×4ö&¦V7DFF&÷f–FW'×Ò"óà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×5†ÖÅ&÷f–FW%FW‡B ¢FW‡CÒ'´&–æF–ær6÷W&6S×µ7FF–5&W6÷W&6RFVfVÇD—FV×5†ÖÄFF&÷f–FW'ÒÂ…FƒÔæÖWÒ"óà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4&÷VæE7FGW5FW‡B ¢FW‡CÒ'´&–æF–ær7FGW7Ò"óà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×5F&vWEWFFVE7FGW5FW‡B ¢&–æF–æråF&vWEWFFVCÒ$öäFVfVÇD—FV×4&–æF–æuF&vWEWFFVB ¢FW‡CÒ'´&–æF–ær7FGW2Âæ÷F–g”öåF&vWEWFFVCÕG'VWÒ"óà¢ÅFW‡D&÷€¢ƒ¤æÖSÒ$FVfVÇD—FV×4VF—F&ÆU7FGW5FW‡D&÷‚ ¢FW‡CÒ'´&–æF–ær7FGW2ÂÖöFSÕGvõv’ÂWFFU6÷W&6UG&–vvW#Õ&÷W'G”6†ævVGÒ"óà¢ÅFW‡D&÷€¢ƒ¤æÖSÒ$FVfVÇD—FV×56÷W&6UWFFVE7FGW5FW‡D&÷‚ ¢&–æF–ærå6÷W&6UWFFVCÒ$öäFVfVÇD—FV×4&–æF–æu6÷W&6UWFFVB ¢FW‡CÒ'´&–æF–ær7FGW2ÂÖöFSÕGvõv’Âæ÷F–g”öå6÷W&6UWFFVCÕG'VRÂWFFU6÷W&6UG&–vvW#ÔW‡Æ–6—GÒ"óà¢ÅFW‡D&÷‚ƒ¤æÖSÒ$FVfVÇD—FV×5fÆ–FFVE7FGW5FW‡D&÷‚#à¢ÅFW‡D&÷‚åFW‡Cà¢Ä&–æF–æp¢ÖöFSÒ%Gvõv’ ¢FƒÒ%7FGW2 ¢WFFU6÷W&6UG&–vvW#Ò$W‡Æ–6—B#à¢Ä&–æF–æråfÆ–FF–öå'VÆW3à¢ÆÆö6Ã¤FVfVÇD—FV×5&WV—&VEFW‡E'VÆRóà¢Âô&–æF–æråfÆ–FF–öå'VÆW3à¢Âô&–æF–æsà¢ÂõFW‡D&÷‚åFW‡Cà¢ÂõFW‡D&÷ƒà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4f÷&ÖGFVE7FGW5FW‡B ¢FW‡CÒ'´&–æF–ær7FGW2Â7G&–ætf÷&ÖCÔf÷&ÖGFVC¢³×Ò"óà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4fÆÆ&6µ7FGW5FW‡B ¢FW‡CÒ'´&–æF–ærÖ—76–ætfÆÆ&6µ7FGW2ÂfÆÆ&6µfÇVSÔFVfVÇB—FVÒfÆÆ&6²fÇVWÒ"óà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×5F&vWDçVÆÅ7FGW5FW‡B ¢FW‡CÒ'´&–æF–ær÷F–öæÅ7FGW2ÂF&vWDçVÆÅfÇVSÔFVfVÇB—FVÒF&vWBÖçVÆÂfÇVWÒ"óà¢ÅFW‡D&Æö6²ƒ¤æÖSÒ$FVfVÇD—FV×5&–÷&—G•7FGW5FW‡B#à¢ÅFW‡D&Æö6²åFW‡Cà¢Å&–÷&—G”&–æF–æsà¢Ä&–æF–ærFƒÒ$Ö—76–æu7FGW2"óà¢Ä&–æF–æp¢FƒÒ%7FGW2 ¢7G&–ætf÷&ÖCÒ%&–÷&—G“¢³Ò"óà¢Âõ&–÷&—G”&–æF–æsà¢ÂõFW‡D&Æö6²åFW‡Cà¢ÂõFW‡D&Æö6³à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×56VÆd&–æF–æuFW‡B ¢FsÒ$FVfVÇB—FVÒ6VÆb6÷W&6R ¢FW‡CÒ'´&–æF–ærFrÂ&VÆF—fU6÷W&6S×µ&VÆF—fU6÷W&6R6VÆgÒÂ7G&–ætf÷&ÖCÕ6VÆc¢³×Ò"óà¢Ä&÷&FW ¢ƒ¤æÖSÒ$FVfVÇD—FV×4æ6W7F÷$&–æF–æt&÷&FW" ¢FsÒ$FVfVÇB—FVÒæ6W7F÷"6÷W&6R#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4æ6W7F÷$&–æF–æuFW‡B ¢FW‡CÒ'´&–æF–ærFrÂ&VÆF—fU6÷W&6S×µ&VÆF—fU6÷W&6Ræ6W7F÷%G—S×·ƒ¥G—R&÷&FW'×ÒÂ7G&–ætf÷&ÖCÔæ6W7F÷#¢³×Ò"óà¢Âô&÷&FW#à¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×5G&–vvW&VE7FGW5FW‡B ¢7G–ÆSÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×57FGW5G&–vvW%7G–ÆWÒ ¢FW‡CÒ'´&–æF–ær7FGW7Ò"óà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×5&÷W'G•G&–vvW&VEFW‡B ¢7G–ÆSÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×5&÷W'G•G&–vvW%7G–ÆWÒ ¢FW‡CÒ$FVfVÇB—FVÒ&÷W'G’G&–vvW"FW‡B"óà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×4&6VDöåFW‡B ¢7G–ÆSÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×4&6VDöåFW‡E7G–ÆWÒ"óà¢Ä'WGFöà¢ƒ¤æÖSÒ$FVfVÇD—FV×5FV×ÆFVD'WGFöâ ¢6öçFVçCÒ$FVfVÇB—FVÒFV×ÆFVB'WGFöâ ¢7G–ÆSÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×5FV×ÆFVD'WGFöå7G–ÆWÒ"óà¢ÄÆ—7D&÷€¢ƒ¤æÖSÒ$FVfVÇD—FV×4Æ—7D&÷‚ ¢F—7Æ”ÖVÖ&W%FƒÒ$æÖR ¢—FVÔ6öçF–æW%7G–ÆSÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×4Æ—7D&÷„—FVÕ7G–ÆWÒ ¢—FV×5æVÃÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×4Æ—7D&÷„—FV×5æVÇÒ ¢—FV×56÷W&6SÒ'´&–æF–ær—FV×7Ò"óà¢ÄÆ—7D&÷€¢ƒ¤æÖSÒ$FVfVÇD—FV×56÷'FVDÆ—7D&÷‚ ¢F—7Æ”ÖVÖ&W%FƒÒ$æÖR ¢—57–æ6‡&öæ—¦VEv—F„7W'&VçD—FVÓÒ%G'VR ¢—FV×56÷W&6SÒ'´&–æF–ær6÷W&6S×µ7FF–5&W6÷W&6RFVfVÇD—FV×56÷'FVD—FV×7×Ò"óà¢ÄÆ—7Ef–Wp¢ƒ¤æÖSÒ$FVfVÇD—FV×4Æ—7Ef–Wr ¢—FV×56÷W&6SÒ'´&–æF–ær—FV×7Ò ¢6VÆV7FVD–æFWƒÒ##à¢ÄÆ—7Ef–Wråf–Wsà¢Äw&–Ef–Wsà¢Äw&–Ef–Wt6öÇVÖà¢†VFW#Ò$æÖR ¢F—7Æ”ÖVÖ&W$&–æF–æsÒ'´&–æF–æræÖWÒ"óà¢Äw&–Ef–Wt6öÇVÖà¢†VFW#Ò$¶–æB ¢F—7Æ”ÖVÖ&W$&–æF–æsÒ'´&–æF–ær¶–æGÒ"óà¢Âôw&–Ef–Wsà¢ÂôÆ—7Ef–Wråf–Wsà¢ÂôÆ—7Ef–Wsà¢ÄFFw&–@¢ƒ¤æÖSÒ$FVfVÇD—FV×4FFw&–B ¢WFôvVæW&FT6öÇVÖç3Ò$fÇ6R ¢6åW6W$FE&÷w3Ò$fÇ6R ¢—FV×56÷W&6SÒ'´&–æF–ær—FV×7Ò ¢6VÆV7FVD—FVÓÒ'´&–æF–ær6VÆV7FVD—FVÒÂÖöFSÕGvõv—Ò#à¢ÄFFw&–Bä6öÇVÖç3à¢ÄFFw&–EFW‡D6öÇVÖà¢†VFW#Ò$æÖR ¢&–æF–æsÒ'´&–æF–æræÖWÒ"óà¢ÄFFw&–EFW‡D6öÇVÖà¢†VFW#Ò$¶–æB ¢&–æF–æsÒ'´&–æF–ær¶–æGÒ"óà¢ÄFFw&–D6†V6´&÷„6öÇVÖà¢†VFW#Ò$7F—fR ¢&–æF–æsÒ'´&–æF–ær—47F—fWÒ"óà¢ÂôFFw&–Bä6öÇVÖç3à¢ÂôFFw&–Cà¢ÄFFw&–@¢ƒ¤æÖSÒ$FVfVÇD—FV×4Æ&vTFFw&–B ¢†V–v‡CÒ#ƒ ¢WFôvVæW&FT6öÇVÖç3Ò$fÇ6R ¢6åW6W$FE&÷w3Ò$fÇ6R ¢Væ&ÆT6öÇVÖåf—'GVÆ—¦F–öãÒ%G'VR ¢Væ&ÆU&÷uf—'GVÆ—¦F–öãÒ%G'VR ¢—FV×56÷W&6SÒ'´&–æF–ærÆ&vT—FV×7Ò ¢6VÆV7FVD—FVÓÒ'´&–æF–ær6VÆV7FVDÆ&vT—FVÒÂÖöFSÕGvõv—Ò ¢67&öÆÅf–WvW"ä6ä6öçFVçE67&öÆÃÒ%G'VR ¢67&öÆÅf–WvW"ä†÷&—¦öçFÅ67&öÆÄ&%f—6–&–Æ—G“Ò$WFò ¢67&öÆÅf–WvW"åfW'F–6Å67&öÆÄ&%f—6–&–Æ—G“Ò$WFò ¢f—'GVÆ—¦–æuæVÂä—5f—'GVÆ—¦–æsÒ%G'VR ¢f—'GVÆ—¦–æuæVÂå67&öÆÅVæ—CÒ%—†VÂ ¢f—'GVÆ—¦–æuæVÂåf—'GVÆ—¦F–öäÖöFSÒ%&V7–6Æ–ær#à¢ÄFFw&–Bä6öÇVÖç3à¢ÄFFw&–EFW‡D6öÇVÖà¢†VFW#Ò$–æFW‚ ¢v–GFƒÒ#ƒ ¢&–æF–æsÒ'´&–æF–ær–æFW‡Ò"óà¢ÄFFw&–EFW‡D6öÇVÖà¢†VFW#Ò$æÖR ¢v–GFƒÒ#c ¢&–æF–æsÒ'´&–æF–æræÖWÒ"óà¢ÄFFw&–EFW‡D6öÇVÖà¢†VFW#Ò$¶–æB ¢v–GFƒÒ#C ¢&–æF–æsÒ'´&–æF–ær¶–æGÒ"óà¢ÄFFw&–D6†V6´&÷„6öÇVÖà¢†VFW#Ò$7F—fR ¢v–GFƒÒ#ƒ ¢&–æF–æsÒ'´&–æF–ær—47F—fWÒ"óà¢ÂôFFw&–Bä6öÇVÖç3à¢ÂôFFw&–Cà¢ÄÆ—7D&÷€¢ƒ¤æÖSÒ$FVfVÇD—FV×4w&÷WVDÆ—7D&÷‚ ¢F—7Æ”ÖVÖ&W%FƒÒ$æÖR ¢—FV×56÷W&6SÒ'´&–æF–ær6÷W&6S×µ7FF–5&W6÷W&6RFVfVÇD—FV×4w&÷WVD—FV×7×Ò#à¢ÄÆ—7D&÷‚äw&÷W7G–ÆSà¢Äw&÷W7G–ÆR†VFW%FV×ÆFSÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×4w&÷W†VFW%FV×ÆFWÒ"óà¢ÂôÆ—7D&÷‚äw&÷W7G–ÆSà¢ÂôÆ—7D&÷ƒà¢ÄÆ—7D&÷‚ƒ¤æÖSÒ$FVfVÇD—FV×46ö×÷6—FTÆ—7D&÷‚#à¢ÄÆ—7D&÷‚ä—FV×56÷W&6Sà¢Ä6ö×÷6—FT6öÆÆV7F–öãà¢Ç7—3¥7G&–æsäFVfVÇB—FVÒ6ö×÷6—FR†VFW#Â÷7—3¥7G&–æsà¢Ä6öÆÆV7F–öä6öçF–æW"6öÆÆV7F–öãÒ'·ƒ¥7FF–2Æö6Ã¤FVfVÇD—FV×46ö×÷6—FU&÷f–FW"ä—FV×7Ò"óà¢ÄÆ—7D&÷„—FVÒ6öçFVçCÒ$FVfVÇB—FVÒ6ö×÷6—FR–æÆ–æR6öçF–æW""óà¢Âô6ö×÷6—FT6öÆÆV7F–öãà¢ÂôÆ—7D&÷‚ä—FV×56÷W&6Sà¢ÂôÆ—7D&÷ƒà¢ÅG&VUf–Wp¢ƒ¤æÖSÒ$FVfVÇD—FV×5G&VUf–Wr ¢—FVÕFV×ÆFSÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×4æöFUFV×ÆFWÒ ¢—FV×56÷W&6SÒ'´&–æF–æræöFW7Ò"óà¢Ä6öÖ&ô&÷€¢ƒ¤æÖSÒ$FVfVÇD—FV×46öÖ&ô&÷‚ ¢F—7Æ”ÖVÖ&W%FƒÒ$æÖR ¢—FV×56÷W&6SÒ'´&–æF–ær—FV×7Ò ¢6VÆV7FVD—FVÓÒ'´&–æF–ær6VÆV7FVD—FVÒÂÖöFSÕGvõv—Ò"óà¢ÅFW‡D&Æö6°¢ƒ¤æÖSÒ$FVfVÇD—FV×46öçfW'FVE6VÆV7F–öåFW‡B ¢FW‡CÒ'´&–æF–ær6VÆV7FVD—FVÒÂ6öçfW'FW#×µ7FF–5&W6÷W&6RFVfVÇD—FV×4—FVÔæÖT6öçfW'FW'×Ò"óà¢Ä6öçFVçD6öçG&öÀ¢ƒ¤æÖSÒ$FVfVÇD—FV×56VÆV7FVEFV×ÆFT6öçG&öÂ ¢6öçFVçCÒ'´&–æF–ær6VÆV7FVD—FV×Ò ¢6öçFVçEFV×ÆFU6VÆV7F÷#Ò'µ7FF–5&W6÷W&6RFVfVÇD—FV×4æÖUFV×ÆFU6VÆV7F÷'Ò"óà¢ÅFW‡D&Æö6²ƒ¤æÖSÒ$FVfVÇD—FV×4×VÇF”&–æF–æuFW‡B#à¢ÅFW‡D&Æö6²åFW‡Cà¢Ä×VÇF”&–æF–ær6öçfW'FW#Ò'µ7FF–5&W6÷W&6RFVfVÇD—FV×57FGW56VÆV7F–öä6öçfW'FW'Ò#à¢Ä&–æF–ærFƒÒ%7FGW2"óà¢Ä&–æF–ærFƒÒ%6VÆV7FVD—FVÒäæÖR"óà¢Âô×VÇF”&–æF–æsà¢ÂõFW‡D&Æö6²åFW‡Cà¢ÂõFW‡D&Æö6³à¢Ä'WGFöà¢ƒ¤æÖSÒ$FVfVÇD—FV×46öÖÖæD'WGFöâ ¢6öÖÖæCÒ'·ƒ¥7FF–2Æö6Ã¤Ö–åv–æF÷räFVfVÇD—FV×46öÖÖæGÒ ¢6öÖÖæE&ÖWFW#Ò&'WGFöâÖ6öÖÖæB ¢6öçFVçCÒ$FVfVÇB—FVÒ6öÖÖæB"óà¢Ä'WGFöà¢ƒ¤æÖSÒ$FVfVÇD—FV×4WfVçE6WGFW$'WGFöâ ¢6öçFVçCÒ$FVfVÇB—FVÒWfVçB6WGFW" ¢7G–ÆSÒ'µ7FF–5&W6÷W&6RFVfVÇD—FV×4WfVçE6WGFW$'WGFöå7G–ÆWÒ"óà¢Ä'WGFöà¢ƒ¤æÖSÒ$FVfVÇD—FV×4'WGFöâ ¢6Æ–6³Ò$öäFVfVÇD—FV×4'WGFöä6Æ–6² ¢6öçFVçCÒ$FVfVÇB—FVÒ'WGFöâ"óà¢Âõ7F6µæVÃà¢Âõv–æF÷sà¢"""“° ¢w&—FTf–ÆR€¢F‚ä6öÖ&–æR†&ö÷BÂ$Ö–åv–æF÷rç†ÖÂæ72"’À¢"" ¢W6–ær7—7FVÓ°¢W6–ær7—7FVÒä6öÆÆV7F–öç2ävVæW&–3°¢W6–ær7—7FVÒä6öÆÆV7F–öç2äö&¦V7DÖöFVÃ°¢W6–ær7—7FVÒä6öÆÆV7F–öç2å7V6–Æ—¦VC°¢W6–ær7—7FVÒä6öæf–wW&F–öã°¢W6–ær7—7FVÒä6ö×öæVçDÖöFVÃ°¢W6–ær7—7FVÒävÆö&Æ—¦F–öã°¢W6–ær7—7FVÒä”ó°¢W6–ær7—7FVÒäÆ–ç°¢W6–ær7—7FVÒå&VfÆV7F–öã°¢W6–ær7—7FVÒå'VçF–ÖRä6ö×–ÆW%6W'f–6W3°¢W6–ær7—7FVÒåF‡&VF–æråF6·3°¢W6–ærW‡FW&æÅ6F´FVfVÇD—FV×4Æ–'&'“°¢W6–ærÖ–7&÷6ögBåv–ã3#°¢W6–ær7—7FVÒåv–æF÷w3°¢W6–ær7—7FVÒåv–æF÷w2ä6öçG&öÇ3°¢W6–ær7—7FVÒåv–æF÷w2ä6öçG&öÇ2å&–Ö—F—fW3°¢W6–ær7—7FVÒåv–æF÷w2äFF°¢W6–ær7—7FVÒåv–æF÷w2äFö7VÖVçG3°¢W6–ær7—7FVÒåv–æF÷w2ä–çWC°¢W6–ær7—7FVÒåv–æF÷w2äÖVF–°¢W6–ær7—7FVÒåv–æF÷w2äÖVF–å&ôuS°¢W6–ær7—7FVÒåv–æF÷w2äÖVF–ä–Öv–æs°¢W6–ær7—7FVÒåv–æF÷w2åF‡&VF–æs° ¢æÖW76RW‡FW&æÅ6F´FVfVÇD—FV×4° ¢V&Æ–26VÆVB6Æ72FVfVÇD—FV×4—FVĞ¢°¢V&Æ–27G&–æræÖR²vWC²6WC²ÒÒ7G&–æräV×G“° ¢V&Æ–27G&–ær¶–æB²vWC²6WC²ÒÒ7G&–æräV×G“° ¢V&Æ–2&ööÂ—47F—fR²vWC²6WC²Ğ¢Ğ ¢V&Æ–26VÆVB6Æ72FVfVÇD—FV×4Æ&vT—FVĞ¢°¢V&Æ–2FVfVÇD—FV×4Æ&vT—FVÒ†–çB–æFW‚Â7G&–æræÖRÂ7G&–ær¶–æBÂ&ööÂ—47F—fR¢°¢–æFW‚Ò–æFWƒ°¢æÖRÒæÖS°¢¶–æBÒ¶–æC°¢—47F—fRÒ—47F—fS°¢Ğ ¢V&Æ–2–çB–æFW‚²vWC²6WC²Ğ ¢V&Æ–27G&–æræÖR²vWC²6WC²Ğ ¢V&Æ–27G&–ær¶–æB²vWC²6WC²Ğ ¢V&Æ–2&ööÂ—47F—fR²vWC²6WC²Ğ¢Ğ ¢V&Æ–26VÆVB6Æ72FVfVÇD—FV×4æöFP¢°¢V&Æ–27G&–æræÖR²vWC²6WC²ÒÒ7G&–æräV×G“° ¢V&Æ–2ö'6W'f&ÆT6öÆÆV7F–öãÄFVfVÇD—FV×4æöFSâ6†–ÆG&Vâ²vWC²ÒĞ¢æWrö'6W'f&ÆT6öÆÆV7F–öãÄFVfVÇD—FV×4æöFSâ‚“°¢Ğ ¢V&Æ–26VÆVB6Æ72FVfVÇD—FV×5&÷f–FW%6÷W&6P¢°¢V&Æ–27G&–ær7&VFUFW‡B‚¢°¢&WGW&â$FVfVÇB—FVÒö&¦V7B&÷f–FW"FW‡B#°¢Ğ¢Ğ ¢V&Æ–27FF–26Æ72FVfVÇD—FV×46ö×÷6—FU&÷f–FW ¢°¢V&Æ–27FF–2ö'6W'f&ÆT6öÆÆV7F–öãÇ7G&–æsâ—FV×2²vWC²ÒĞ¢æWrö'6W'f&ÆT6öÆÆV7F–öãÇ7G&–æsà¢°¢$FVfVÇB—FVÒ6ö×÷6—FRÇ†"À¢$FVfVÇB—FVÒ6ö×÷6—FR&WF"À¢Ó°¢Ğ ¢V&Æ–26VÆVB6Æ72FVfVÇD—FV×5f–WtÖöFVÂ¢”æ÷F–g•&÷W'G”6†ævV@¢°¢&—fFR7G&–ær÷7FGW2Ò$FVfVÇB—FVÒ&–æF–ær&VG’#°¢&—fFR7G&–æsòö÷F–öæÅ7FGW3°¢&—fFR&ööÂö—4f÷&Ô÷F–öäVæ&ÆVBÒG'VS°¢&—fFRF÷V&ÆRöf÷&Õ&öw&W72Ò#Rã°¢&—fFRFFUF–ÖSò÷6VÆV7FVDFFRÒæWrFFUF–ÖRƒ##bÂbÂ#B“°¢&—fFRFVfVÇD—FV×4—FVÒ÷6VÆV7FVD—FVÓ°¢&—fFRFVfVÇD—FV×4Æ&vT—FVÒ÷6VÆV7FVDÆ&vT—FVÓ° ¢V&Æ–2WfVçB&÷W'G”6†ævVDWfVçD†æFÆW"&÷W'G”6†ævVBÒFVÆVvFR²Ó° ¢V&Æ–2ö'6W'f&ÆT6öÆÆV7F–öãÄFVfVÇD—FV×4—FVÓâ—FV×2²vWC²ÒĞ¢æWrö'6W'f&ÆT6öÆÆV7F–öãÄFVfVÇD—FV×4—FVÓà¢°¢æWrFVfVÇD—FV×4—FVĞ¢°¢æÖRÒ$FVfVÇB—FVÒÇ†"À¢¶–æBÒ$g&ÖWv÷&²"À¢—47F—fRÒG'VRÀ¢ÒÀ¢æWrFVfVÇD—FV×4—FVĞ¢°¢æÖRÒ$FVfVÇB—FVÒ&WF"À¢¶–æBÒ$FF"À¢—47F—fRÒfÇ6RÀ¢ÒÀ¢Ó° ¢V&Æ–2ö'6W'f&ÆT6öÆÆV7F–öãÄFVfVÇD—FV×4Æ&vT—FVÓâÆ&vT—FV×2²vWC²ÒĞ¢7&VFTÆ&vT—FV×2‚“° ¢V&Æ–2ö'6W'f&ÆT6öÆÆV7F–öãÄFVfVÇD—FV×4æöFSâæöFW2²vWC²ÒĞ¢æWrö'6W'f&ÆT6öÆÆV7F–öãÄFVfVÇD—FV×4æöFSà¢°¢æWrFVfVÇD—FV×4æöFP¢°¢æÖRÒ$FVfVÇB—FVÒ&ö÷B"À¢6†–ÆG&VâĞ¢°¢æWrFVfVÇD—FV×4æöFR²æÖRÒ$FVfVÇB—FVÒ6†–ÆB"ÒÀ¢ÒÀ¢ÒÀ¢Ó° ¢V&Æ–2FVfVÇD—FV×5f–WtÖöFVÂ‚¢°¢÷6VÆV7FVD—FVÒÒ—FV×5³Ó°¢÷6VÆV7FVDÆ&vT—FVÒÒÆ&vT—FV×5³Ó°¢Ğ ¢V&Æ–27G&–ær7FGW0¢°¢vWBÓâ÷7FGW3°¢6W@¢°¢–b‡7G&–æräWVÇ2…÷7FGW2ÂfÇVRÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢&WGW&ã°¢Ğ ¢÷7FGW2ÒfÇVS°¢öå&÷W'G”6†ævVB‚“°¢Ğ¢Ğ ¢V&Æ–2FVfVÇD—FV×4—FVÒ6VÆV7FVD—FVĞ¢°¢vWBÓâ÷6VÆV7FVD—FVÓ°¢6W@¢°¢–b…&VfW&Væ6TWVÇ2…÷6VÆV7FVD—FVÒÂfÇVR’¢°¢&WGW&ã°¢Ğ ¢÷6VÆV7FVD—FVÒÒfÇVS°¢öå&÷W'G”6†ævVB‚“°¢Ğ¢Ğ ¢V&Æ–2FVfVÇD—FV×4Æ&vT—FVÒ6VÆV7FVDÆ&vT—FVĞ¢°¢vWBÓâ÷6VÆV7FVDÆ&vT—FVÓ°¢6W@¢°¢–b…&VfW&Væ6TWVÇ2…÷6VÆV7FVDÆ&vT—FVÒÂfÇVR’¢°¢&WGW&ã°¢Ğ ¢÷6VÆV7FVDÆ&vT—FVÒÒfÇVS°¢öå&÷W'G”6†ævVB‚“°¢Ğ¢Ğ ¢V&Æ–27G&–æsò÷F–öæÅ7FGW0¢°¢vWBÓâö÷F–öæÅ7FGW3°¢6W@¢°¢–b‡7G&–æräWVÇ2…ö÷F–öæÅ7FGW2ÂfÇVRÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢&WGW&ã°¢Ğ ¢ö÷F–öæÅ7FGW2ÒfÇVS°¢öå&÷W'G”6†ævVB‚“°¢Ğ¢Ğ ¢V&Æ–2&ööÂ—4f÷&Ô÷F–öäVæ&ÆV@¢°¢vWBÓâö—4f÷&Ô÷F–öäVæ&ÆVC°¢6W@¢°¢–b…ö—4f÷&Ô÷F–öäVæ&ÆVBÓÒfÇVR¢°¢&WGW&ã°¢Ğ ¢ö—4f÷&Ô÷F–öäVæ&ÆVBÒfÇVS°¢öå&÷W'G”6†ævVB‚“°¢Ğ¢Ğ ¢V&Æ–2F÷V&ÆRf÷&Õ&öw&W70¢°¢vWBÓâöf÷&Õ&öw&W73°¢6W@¢°¢–b„ÖF‚ä'2…öf÷&Õ&öw&W72ÒfÇVR’Âã¢°¢&WGW&ã°¢Ğ ¢öf÷&Õ&öw&W72ÒfÇVS°¢öå&÷W'G”6†ævVB‚“°¢Ğ¢Ğ ¢V&Æ–2FFUF–ÖSò6VÆV7FVDFFP¢°¢vWBÓâ÷6VÆV7FVDFFS°¢6W@¢°¢–b„çVÆÆ&ÆRäWVÇ2…÷6VÆV7FVDFFRÂfÇVR’¢°¢&WGW&ã°¢Ğ ¢÷6VÆV7FVDFFRÒfÇVS°¢öå&÷W'G”6†ævVB‚“°¢Ğ¢Ğ ¢&—fFRfö–Böå&÷W'G”6†ævVB…´6ÆÆW$ÖVÖ&W$æÖUÒ7G&–ær&÷W'G”æÖRÒ""¢°¢&÷W'G”6†ævVB‡F†—2ÂæWr&÷W'G”6†ævVDWfVçD&w2‡&÷W'G”æÖR’“°¢Ğ ¢&—fFR7FF–2ö'6W'f&ÆT6öÆÆV7F–öãÄFVfVÇD—FV×4Æ&vT—FVÓâ7&VFTÆ&vT—FV×2‚¢°¢f"—FV×2ÒæWrö'6W'f&ÆT6öÆÆV7F–öãÄFVfVÇD—FV×4Æ&vT—FVÓâ‚“°¢7G&–æuµÒ¶–æG2Ğ¢°¢$g&ÖWv÷&²"À¢%&VæFW&–ær"À¢%FööÆ¶—B"À¢%4D²"À¢Ó° ¢f÷"†–çB’Ò²’Âó²’²²¢°¢–çB–æFW‚Ò’²°¢—FV×2äFB€¢æWrFVfVÇD—FV×4Æ&vT—FVÒ€¢–æFW‚À¢B$W‡FW&æÂFVfVÇB&÷r¶–æFWƒ£Ò"À¢¶–æG5¶’R¶–æG2äÆVæwF…ÒÀ¢’R"ÓÒ’“°¢Ğ ¢&WGW&â—FV×3°¢Ğ¢Ğ ¢V&Æ–26VÆVB6Æ72FVfVÇD—FV×4—FVÔæÖT6öçfW'FW"¢•fÇVT6öçfW'FW ¢°¢V&Æ–2ö&¦V7B6öçfW'B†ö&¦V7BfÇVRÂG—RF&vWEG—RÂö&¦V7B&ÖWFW"Â7VÇGW&T–æfò7VÇGW&R¢°¢&WGW&âfÇVR—2FVfVÇD—FV×4—FVÒ—FVÒòB%6VÆV7FVC¢¶—FVÒäæÖWÒ"¢7G&–æräV×G“°¢Ğ ¢V&Æ–2ö&¦V7B6öçfW'D&6²†ö&¦V7BfÇVRÂG—RF&vWEG—RÂö&¦V7B&ÖWFW"Â7VÇGW&T–æfò7VÇGW&R¢°¢&WGW&â&–æF–æräFôæ÷F†–æs°¢Ğ¢Ğ ¢V&Æ–26VÆVB6Æ72FVfVÇD—FV×57FGW56VÆV7F–öä6öçfW'FW"¢”×VÇF•fÇVT6öçfW'FW ¢°¢V&Æ–2ö&¦V7B6öçfW'B†ö&¦V7EµÒfÇVW2ÂG—RF&vWEG—RÂö&¦V7B&ÖWFW"Â7VÇGW&T–æfò7VÇGW&R¢°¢f"7FGW2ÒfÇVW2äÆVæwF‚âòfÇVW5³Ò27G&–ær¢çVÆÃ°¢f"6VÆV7FVDæÖRÒfÇVW2äÆVæwF‚âòfÇVW5³Ò27G&–ær¢çVÆÃ°¢&WGW&âB$6ö×÷6—FS¢·7FGW2óò7G&–æräV×G—Òò·6VÆV7FVDæÖRóò7G&–æräV×G—Ò#°¢Ğ ¢V&Æ–2ö&¦V7EµÒ6öçfW'D&6²†ö&¦V7BfÇVRÂG—UµÒF&vWEG—W2Âö&¦V7B&ÖWFW"Â7VÇGW&T–æfò7VÇGW&R¢°¢f"&W7VÇG2ÒæWrö&¦V7E·F&vWEG—W2äÆVæwF…Ó°¢'&’äf–ÆÂ‡&W7VÇG2Â&–æF–æräFôæ÷F†–ær“°¢&WGW&â&W7VÇG3°¢Ğ¢Ğ ¢V&Æ–26VÆVB6Æ72FVfVÇD—FV×4æÖUFV×ÆFU6VÆV7F÷"¢FFFV×ÆFU6VÆV7F÷ ¢°¢V&Æ–2÷fW'&–FRFFFV×ÆFR6VÆV7EFV×ÆFR†ö&¦V7B—FVÒÂFWVæFVæ7”ö&¦V7B6öçF–æW"¢°¢–b†—FVÒ—2FVfVÇD—FV×4—FVÒ6VÆV7FVD—FVÒbb6öçF–æW"—2g&ÖWv÷&´VÆVÖVçBVÆVÖVçB¢°¢7G&–ær&W6÷W&6T¶W’Ò6VÆV7FVD—FVÒäæÖRä6öçF–ç2‚&&WF"Â7G&–æt6ö×&—6öâä÷&F–æÄ–væ÷&T66R¢ò$FVfVÇD—FV×56VÆV7FVD&WFFV×ÆFR ¢¢$FVfVÇD—FV×56VÆV7FVDÇ†FV×ÆFR#°¢&WGW&â&WV—&UG—SÄFFFV×ÆFSâ€¢VÆVÖVçBäf–æE&W6÷W&6R‡&W6÷W&6T¶W’’À¢B&FVfVÇBÖ—FVÒFV×ÆFR6VÆV7F÷"&W6÷W&6R·&W6÷W&6T¶W—Ò"“°¢Ğ ¢&WGW&â&6Rå6VÆV7EFV×ÆFR†—FVÒÂ6öçF–æW"“°¢Ğ ¢&—fFR7FF–2B&WV—&UG—SÅCâ†ö&¦V7BfÇVRÂ7G&–ærFW67&—F–öâ¢°¢&WGW&âfÇVR—2BG—V@¢òG—V@¢¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$W‡V7FVB¶FW67&—F–öçÒFò&R·G—Vöb…B’ägVÆÄæÖWÒâ"“°¢Ğ¢Ğ ¢V&Æ–26VÆVB6Æ72FVfVÇD—FV×5&WV—&VEFW‡E'VÆR¢fÆ–FF–öå'VÆP¢°¢V&Æ–2÷fW'&–FRfÆ–FF–öå&W7VÇBfÆ–FFR†ö&¦V7BfÇVRÂ7VÇGW&T–æfò7VÇGW&T–æfò¢°¢&WGW&â7G&–ærä—4çVÆÄ÷%v†—FU76R‡fÇVR27G&–ær¢òæWrfÆ–FF–öå&W7VÇB†fÇ6RÂ$FVfVÇB—FVÒFW‡B—2&WV—&VBâ"¢¢fÆ–FF–öå&W7VÇBåfÆ–E&W7VÇC°¢Ğ¢Ğ ¢V&Æ–2'F–Â6Æ72Ö–åv–æF÷r¢v–æF÷p¢°¢V&Æ–27FF–2&÷WFVET”6öÖÖæBFVfVÇD—FV×46öÖÖæB²vWC²ÒĞ¢æWr&÷WFVET”6öÖÖæB€¢$FVfVÇB—FV×26öÖÖæB"À¢æÖVöb„FVfVÇD—FV×46öÖÖæB’À¢G—Vöb„Ö–åv–æF÷r’“° ¢&—fFR6öç7B7G&–ærFVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öäVçf—&öæÖVçEf&–&ÆRĞ¢%$ôuUõueôU…DU$äÅôDTdTÅEôÄ•dUôtTôÔUE%•õdÄ”DDR#°¢&—fFR6öç7B–çBFVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öäÖ„GFV×G2Ò°¢&—fFR7FF–2&VFöæÇ’F–ÖU7âFVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öå&WG'”FVÆ’Ğ¢F–ÖU7âäg&öÔÖ–ÆÆ—6V6öæG2ƒb“°¢&—fFR&ööÂöFVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öå7F'FVC° ¢V&Æ–2–çBÆöFVD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–2–çB'WGFöä6Æ–6´6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–2–çB6öÖÖæDW†V7WF–öä6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–2–çBWfVçE6WGFW$6Æ–6´6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–2–çBW‡æFW$W‡æFVD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–2–çBW‡æFW$6öÆÆ6VD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–2–çBÖVçT6Æ–6´6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–2–çBÖVçT6†V6¶VD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–2–çBÖVçUVæ6†V6¶VD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–2–çB6öçFW‡DÖVçT6Æ–6´6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–2–çB6öçFW‡DÖVçT6†V6¶VD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–2–çB6öçFW‡DÖVçUVæ6†V6¶VD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–2–çB&F–ô'WGFöä6†V6¶VD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–27G&–ærÆ7E&F–ô'WGFöä6†V6¶VDæÖR²vWC²&—fFR6WC²ÒÒ7G&–æräV×G“° ¢V&Æ–2–çB77v÷&D6†ævVD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–27G&–ærÆ7E77v÷&EfÇVR²vWC²&—fFR6WC²ÒÒ7G&–æräV×G“° ¢V&Æ–27G&–ærÆ7D6öÖÖæE&ÖWFW"²vWC²&—fFR6WC²ÒÒ7G&–æräV×G“° ¢V&Æ–2–çB&–æF–æuF&vWEWFFVD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–27G&–ærÆ7D&–æF–æuF&vWEWFFVDæÖR²vWC²&—fFR6WC²ÒÒ7G&–æräV×G“° ¢V&Æ–27G&–ærÆ7D&–æF–æuF&vWEWFFVE&÷W'G”æÖR²vWC²&—fFR6WC²ÒÒ7G&–æräV×G“° ¢V&Æ–2–çB&–æF–æu6÷W&6UWFFVD6÷VçB²vWC²&—fFR6WC²Ğ ¢V&Æ–27G&–ærÆ7D&–æF–æu6÷W&6UWFFVDæÖR²vWC²&—fFR6WC²ÒÒ7G&–æräV×G“° ¢V&Æ–27G&–ærÆ7D&–æF–æu6÷W&6UWFFVE&÷W'G”æÖR²vWC²&—fFR6WC²ÒÒ7G&–æräV×G“° ¢V&Æ–2FVfVÇD—FV×5f–WtÖöFVÂf–WtÖöFVÂ²vWC²ÒÒæWrFVfVÇD—FV×5f–WtÖöFVÂ‚“° ¢V&Æ–2Ö–åv–æF÷r‚¢°¢–æ—F–Æ—¦T6ö×öæVçB‚“°¢FF6öçFW‡BÒf–WtÖöFVÃ°¢–b„Vçf—&öæÖVçBävWDVçf—&öæÖVçEf&–&ÆR„FVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öäVçf—&öæÖVçEf&–&ÆR’ÓÒ#"¢°¢ÆöFVB³ÒöäFVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öäÆöFVC°¢7F'DFVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öä–e&WV—&VB‚“°¢Ğ¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öäÆöFVB†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢7F'DFVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öä–e&WV—&VB‚“°¢Ğ ¢&—fFRfö–B7F'DFVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öä–e&WV—&VB‚¢°¢–b…öFVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öå7F'FVBÇÀ¢Vçf—&öæÖVçBävWDVçf—&öæÖVçEf&–&ÆR„FVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öäVçf—&öæÖVçEf&–&ÆR’Ò#"¢°¢&WGW&ã°¢Ğ ¢öFVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öå7F'FVBÒG'VS°¢òÒF6²å'Vâ€¢7–æ2‚’Óà¢°¢G'¢°¢v—BfÆ–FFU&WV—&VDFVfVÇD—FV×4Æ—fTvVöÖWG'”7–æ2‚’ä6öæf–wW&Tv—B†fÇ6R“°¢Ğ¢6F6‚„W†6WF–öâW‚¢°¢6öç6öÆRäW'&÷"åw&—FTÆ–æR†W‚“°¢Vçf—&öæÖVçBäW†—Bƒ“°¢Ğ¢Ò“°¢Ğ ¢&—fFR7–æ2F6²fÆ–FFU&WV—&VDFVfVÇD—FV×4Æ—fTvVöÖWG'”7–æ2‚¢°¢f÷"†–çBGFV×BÒ²GFV×BÂFVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öäÖ„GFV×G3²GFV×B²²¢°¢v—BF6²äFVÆ’„FVfVÇD—FV×4Æ—fTvVöÖWG'•fÆ–FF–öå&WG'”FVÆ’’ä6öæf–wW&Tv—B†fÇ6R“°¢–b‚&ôwUwdF–væ÷7F–72åG'”vWEv–æF÷t†÷7B‡F†—2Â÷WBf"Æ—fT†÷7B’ÇÀ¢Æ—fT†÷7BÓÒçVÆÂ¢°¢6öçF–çVS°¢Ğ ¢–b‚Æ—fT†÷7Bä†5&W6VçFVDg&ÖR¢°¢v¶TFVfVÇD—FV×4Æ—fT†÷7B‚“°¢6öçF–çVS°¢Ğ ¢v¶TFVfVÇD—FV×4Æ—fT†÷7B‚“°¢7G&–ær7FGW2Òv—BF—7F6†W"ä–çfö¶T7–æ2€¢fÆ–FFTFVfVÇD—FV×4Æ—fU&VæFW%7W&f6TvVöÖWG'”6÷&RÀ¢F—7F6†W%&–÷&—G’å6VæB“°¢6öç6öÆRåw&—FTÆ–æR‚B$W‡FW&æÂ4D²FVfVÇBÖ—FVÒ†÷7BÆ—fRvVöÖWG'’fÆ–FF–öâ7V66VVFVC¢·7FGW7Òâ"“°¢Vçf—&öæÖVçBäW†—Bƒ“°¢&WGW&ã°¢Ğ ¢6öç6öÆRäW'&÷"åw&—FTÆ–æR€¢$W‡V7FVBF†RW‡FW&æÂ4D²FVfVÇBÖ—FVÒ†÷7BFò&W6VçB7F&ÆR&ôuRg&ÖR&Vf÷&RÆ—fRvVöÖWG'’fÆ–FF–öââ"“°¢Vçf—&öæÖVçBäW†—Bƒ“°¢Ğ ¢&—fFR7G&–ærfÆ–FFTFVfVÇD—FV×4Æ—fU&VæFW%7W&f6TvVöÖWG'”6÷&R‚¢°¢–b‚&ôwUwdF–væ÷7F–72åG'”vWE&VæFW%7W&f6TvVöÖWG'’‡F†—2Â÷WBf"vVöÖWG'’’¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡V7FVBFVfVÇBÖ—FVÒÆ—fR&ôuRub†÷7BvVöÖWG'’â"“°¢Ğ ¢&WV—&R†vVöÖWG'’äÆöv–6Åv–GF‚ÓÒ#cRÂ$W‡V7FVBFVfVÇBÖ—FVÒÆ—fR&ôuRubÆöv–6Âv–GF‚â"“°¢&WV—&R†vVöÖWG'’äÆöv–6Ä†V–v‡BÓÒCRÂ$W‡V7FVBFVfVÇBÖ—FVÒÆ—fR&ôuRubÆöv–6Â†V–v‡Bâ"“°¢–b†vVöÖWG'’å—†VÅv–GF‚ÂvVöÖWG'’äÆöv–6Åv–GF‚ÇÂvVöÖWG'’å—†VÄ†V–v‡BÂvVöÖWG'’äÆöv–6Ä†V–v‡B¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVBFVfVÇBÖ—FVÒÆ—fR&ôuRub—†VÇ2Fò6÷fW"Æöv–6Â6öçFVçBÂ'WBv÷BÆöv–6Â¶vVöÖWG'’äÆöv–6Åv–GF‡×‡¶vVöÖWG'’äÆöv–6Ä†V–v‡GÒæB—†VÇ2¶vVöÖWG'’å—†VÅv–GF‡×‡¶vVöÖWG'’å—†VÄ†V–v‡GÒâ"“°¢Ğ ¢–b†vVöÖWG'’åf–Ww÷'E‚ÒÇÀ¢vVöÖWG'’åf–Ww÷'E’ÒÇÀ¢vVöÖWG'’åf–Ww÷'Ev–GF‚ÒvVöÖWG'’å—†VÅv–GF‚ÇÀ¢vVöÖWG'’åf–Ww÷'D†V–v‡BÒvVöÖWG'’å—†VÄ†V–v‡B¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVBFVfVÇBÖ—FVÒÆ—fR&ôuRubf–Ww÷'BFòW6RF†RgVÆÂ‡—6–6ÂF&vWBÂ'WBv÷Bf–Ww÷'B¶vVöÖWG'’åf–Ww÷'Ev–GF‡×‡¶vVöÖWG'’åf–Ww÷'D†V–v‡GÔ¶vVöÖWG'’åf–Ww÷'E‡ÒÇ¶vVöÖWG'’åf–Ww÷'E—Òf÷"—†VÇ2¶vVöÖWG'’å—†VÅv–GF‡×‡¶vVöÖWG'’å—†VÄ†V–v‡GÒâ"“°¢Ğ ¢&WGW&âB&Æöv–6Â¶vVöÖWG'’äÆöv–6Åv–GF‡×‡¶vVöÖWG'’äÆöv–6Ä†V–v‡GÒÂ—†VÇ2¶vVöÖWG'’å—†VÅv–GF‡×‡¶vVöÖWG'’å—†VÄ†V–v‡GÒÂf–Ww÷'B¶vVöÖWG'’åf–Ww÷'Ev–GF‡×‡¶vVöÖWG'’åf–Ww÷'D†V–v‡GÔ¶vVöÖWG'’åf–Ww÷'E‡ÒÇ¶vVöÖWG'’åf–Ww÷'E—ÒÂG’¶vVöÖWG'’äG•66ÆS£â227Ò#°¢Ğ ¢&—fFRfö–Bv¶TFVfVÇD—FV×4Æ—fT†÷7B‚¢°¢&ôwUwdF–væ÷7F–72åG'•&WVW7E&VæFW"‡F†—2“°¢&ôwUwdF–væ÷7F–72åG'•v¶TæF—fTÆö÷‡F†—2“°¢Ğ ¢V&Æ–2fö–BfÆ–FFTFVfVÇD—FV×5'Vâ‚¢°¢&WV—&R„FVfVÇD—FV×5F—FÆUFW‡BåFW‡BÓÒ$FVfVÇB—FVÒ&W6÷W&6RFW‡B"Â$W‡V7FVBFVfVÇBÖ—FVÒ7FF–5&W6÷W&6RFW‡Bâ"“°¢f"f÷&Vw&÷VæBÒ&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ„FVfVÇD—FV×5F—FÆUFW‡Bäf÷&Vw&÷VæBÂ&FVfVÇBÖ—FVÒG–æÖ–5&W6÷W&6R''W6‚"“°¢&WV—&R€¢f÷&Vw&÷VæBä6öÆ÷"ÓÒ6öÆ÷"äg&öÕ&v"ƒƒ32ÂƒSRÂƒsr’À¢$W‡V7FVBFVfVÇBÖ—FVÒG–æÖ–5&W6÷W&6R''W6‚6öÆ÷"â"“°¢Æ–6F–öâä7W'&VçBå&W6÷W&6W5²$FVfVÇD—FV×4''W6‚%ÒĞ¢æWr6öÆ–D6öÆ÷$''W6‚„6öÆ÷"äg&öÕ&v"ƒƒƒ‚Âƒ#"Âƒ’“°¢G&–äF—7F6†W"‚“°¢f"WFFVDf÷&Vw&÷VæBÒ&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ€¢FVfVÇD—FV×5F—FÆUFW‡Bäf÷&Vw&÷VæBÀ¢&FVfVÇBÖ—FVÒWFFVBG–æÖ–5&W6÷W&6R''W6‚"“°¢&WV—&R€¢WFFVDf÷&Vw&÷VæBä6öÆ÷"ÓÒ6öÆ÷"äg&öÕ&v"ƒƒƒ‚Âƒ#"Âƒ’À¢$W‡V7FVBFVfVÇBÖ—FVÒG–æÖ–5&W6÷W&6R''W6‚–çfÆ–FF–öââ"“°¢&WV—&R€¢FVfVÇD—FV×4F–7F–öæ'•FW‡BåFW‡BÓÒ$FVfVÇB—FVÒF–7F–öæ'’FW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&W6÷W&6RF–7F–öæ'’FW‡Bâ"“°¢f"F–7F–öæ'”f÷&Vw&÷VæBÒ&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ€¢FVfVÇD—FV×4F–7F–öæ'•FW‡Bäf÷&Vw&÷VæBÀ¢&FVfVÇBÖ—FVÒ&W6÷W&6RF–7F–öæ'’''W6‚"“°¢&WV—&R€¢F–7F–öæ'”f÷&Vw&÷VæBä6öÆ÷"ÓÒ6öÆ÷"äg&öÕ&v"ƒƒCBÂƒsrÂƒ“’’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&W6÷W&6RF–7F–öæ'’''W6‚6öÆ÷"â"“°¢Æ–6F–öâä7W'&VçBå&W6÷W&6W5²$FVfVÇD—FV×4F–7F–öæ'”''W6‚%ÒĞ¢æWr6öÆ–D6öÆ÷$''W6‚„6öÆ÷"äg&öÕ&v"ƒƒ#"Âƒƒ‚ÂƒCB’“°¢G&–äF—7F6†W"‚“°¢f"WFFVDF–7F–öæ'”f÷&Vw&÷VæBÒ&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ€¢FVfVÇD—FV×4F–7F–öæ'•FW‡Bäf÷&Vw&÷VæBÀ¢&FVfVÇBÖ—FVÒWFFVB&W6÷W&6RF–7F–öæ'’''W6‚"“°¢&WV—&R€¢WFFVDF–7F–öæ'”f÷&Vw&÷VæBä6öÆ÷"ÓÒ6öÆ÷"äg&öÕ&v"ƒƒ#"Âƒƒ‚ÂƒCB’À¢$W‡V7FVBFVfVÇBÖ—FVÒG–æÖ–5&W6÷W&6R''W6‚–çfÆ–FF–öââ"“°¢f"FVfVÇD—FV×4g&VW¦&ÆT''W6‚Ò&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ€¢&W6÷W&6W5²$FVfVÇD—FV×4g&VW¦&ÆT''W6‚%ÒÀ¢&FVfVÇBÖ—FVÒg&VW¦&ÆR''W6‚&W6÷W&6R"“°¢&WV—&R€¢FVfVÇD—FV×4g&VW¦&ÆT''W6‚ä6öÆ÷"ÓÒ6öÆ÷"äg&öÕ&v"ƒƒT"Âƒ„2Âƒt¢bbÖF‚ä'2†FVfVÇD—FV×4g&VW¦&ÆT''W6‚ä÷6—G’ÒãsR’Âã¢bbFVfVÇD—FV×4g&VW¦&ÆT''W6‚ä6äg&VW¦RÀ¢$W‡V7FVBFVfVÇBÖ—FVÒg&VW¦&ÆR''W6‚ÖWFFFâ"“°¢–b‚FVfVÇD—FV×4g&VW¦&ÆT''W6‚ä—4g&÷¦Vâ¢°¢FVfVÇD—FV×4g&VW¦&ÆT''W6‚äg&VW¦R‚“°¢Ğ ¢&WV—&R€¢FVfVÇD—FV×4g&VW¦&ÆT''W6‚ä—4g&÷¦VâÀ¢$W‡V7FVBFVfVÇBÖ—FVÒg&VW¦&ÆR''W6‚g&÷¦Vâ7FFRâ"“°¢f"FVfVÇD—FV×4g&VW¦&ÆT6ÆöæRÒFVfVÇD—FV×4g&VW¦&ÆT''W6‚ä6ÆöæR‚“°¢FVfVÇD—FV×4g&VW¦&ÆT6ÆöæRä÷6—G’Òã33°¢&WV—&R€¢FVfVÇD—FV×4g&VW¦&ÆT6ÆöæRä—4g&÷¦Và¢bbÖF‚ä'2†FVfVÇD—FV×4g&VW¦&ÆT6ÆöæRä÷6—G’Òã32’Âã¢bbÖF‚ä'2†FVfVÇD—FV×4g&VW¦&ÆT''W6‚ä÷6—G’ÒãsR’ÂãÀ¢$W‡V7FVBFVfVÇBÖ—FVÒg&VW¦&ÆR''W6‚6ÆöæR×WF&–Æ—G’â"“°¢f"FVfVÇD—FV×5Vç6†&VD''W6„Ò&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ€¢&W6÷W&6W5²$FVfVÇD—FV×5Vç6†&VD''W6‚%ÒÀ¢&FVfVÇBÖ—FVÒƒ¥6†&VCÖfÇ6Rf—'7B''W6‚"“°¢f"FVfVÇD—FV×5Vç6†&VD''W6„"Ò&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ€¢&W6÷W&6W5²$FVfVÇD—FV×5Vç6†&VD''W6‚%ÒÀ¢&FVfVÇBÖ—FVÒƒ¥6†&VCÖfÇ6R6V6öæB''W6‚"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2†FVfVÇD—FV×5Vç6†&VD''W6„ÂFVfVÇD—FV×5Vç6†&VD''W6„"¢bbFVfVÇD—FV×5Vç6†&VD''W6„ä6öÆ÷"ÓÒ6öÆ÷"äg&öÕ&v"ƒ„3BÂƒTÂƒ$"¢bbFVfVÇD—FV×5Vç6†&VD''W6„"ä6öÆ÷"ÓÒ6öÆ÷"äg&öÕ&v"ƒ„3BÂƒTÂƒ$"’À¢$W‡V7FVBFVfVÇBÖ—FVÒƒ¥6†&VCÖfÇ6R&W6÷W&6RÆöö·Wâ"“°¢&WV—&R€¢FVfVÇD—FV×4Æ–'&'•&W6÷W&6UFW‡BåFW‡BÓÒ$FVfVÇB—FVÒÆ–'&'’&W6÷W&6RFW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’6ö×öæVçB&W6÷W&6RFW‡Bâ"“°¢f"Æ–'&'•&W6÷W&6Tf÷&Vw&÷VæBÒ&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ€¢FVfVÇD—FV×4Æ–'&'•&W6÷W&6UFW‡Bäf÷&Vw&÷VæBÀ¢&FVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’6ö×öæVçB&W6÷W&6R''W6‚"“°¢&WV—&R€¢Æ–'&'•&W6÷W&6Tf÷&Vw&÷VæBä6öÆ÷"ÓÒ6öÆ÷"äg&öÕ&v"ƒƒsbÂƒSBÂƒ3"’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’6ö×öæVçB&W6÷W&6R''W6‚6öÆ÷"â"“°¢f"FVfVÇD—FV×4–ÖvU6÷W&6RÒ&WV—&UG—SÄ&—FÖ6÷W&6Sâ€¢FVfVÇD—FV×4–ÖvRå6÷W&6RÀ¢&FVfVÇBÖ—FVÒ„ÔÂ&W6÷W&6R–ÖvR6÷W&6R"“°¢&WV—&R€¢FVfVÇD—FV×4–ÖvU6÷W&6Rå—†VÅv–GF‚ÓÒ ¢bbFVfVÇD—FV×4–ÖvU6÷W&6Rå—†VÄ†V–v‡BÓÒ ¢bbFVfVÇD—FV×4–ÖvU6÷W&6Räf÷&ÖBÓÒ—†VÄf÷&ÖG2ä&w&3"À¢$W‡V7FVBFVfVÇBÖ—FVÒ„ÔÂ&W6÷W&6R–ÖvRÖWFFFâ"“°¢'—FUµÒ–ÖvU—†VÇ2ÒæWr'—FU³eÓ°¢FVfVÇD—FV×4–ÖvU6÷W&6Rä6÷•—†VÇ2†–ÖvU—†VÇ2Â‚Â“°¢&WV—&R€¢–ÖvU—†VÇ5³%ÒÓÒ„dbbb–ÖvU—†VÇ5³UÒÓÒ„dbÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ„ÔÂ&W6÷W&6R–ÖvR—†VÇ2â"“°¢f"–ÖvT''W6‚Ò&WV—&UG—SÄ–ÖvT''W6ƒâ€¢FVfVÇD—FV×4–ÖvT''W6…&V7FævÆRäf–ÆÂÀ¢&FVfVÇBÖ—FVÒ–ÖvT''W6‚f–ÆÂ"“°¢f"–ÖvT''W6…6÷W&6RÒ&WV—&UG—SÄ&—FÖ6÷W&6Sâ€¢–ÖvT''W6‚ä–ÖvU6÷W&6RÀ¢&FVfVÇBÖ—FVÒ–ÖvT''W6‚6÷W&6R"“°¢&WV—&R€¢–ÖvT''W6…6÷W&6Rå—†VÅv–GF‚ÓÒ ¢bb–ÖvT''W6…6÷W&6Rå—†VÄ†V–v‡BÓÒ ¢bb–ÖvT''W6…6÷W&6Räf÷&ÖBÓÒ—†VÄf÷&ÖG2ä&w&3"À¢$W‡V7FVBFVfVÇBÖ—FVÒ–ÖvT''W6‚–ÖvRÖWFFFâ"“°¢'—FUµÒ–ÖvT''W6…—†VÇ2ÒæWr'—FU³eÓ°¢–ÖvT''W6…6÷W&6Rä6÷•—†VÇ2†–ÖvT''W6…—†VÇ2Â‚Â“°¢&WV—&R€¢–ÖvT''W6…—†VÇ5³UÒÓÒ„dbbb–ÖvT''W6…—†VÇ5³UÒÓÒ„dbÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ–ÖvT''W6‚–ÖvR—†VÇ2â"“°¢&WV—&R€¢FVfVÇD—FV×47W'6÷%F&vWBä7W'6÷"—2æ÷BçVÆÂÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ„ÔÂ7W'6÷"&W6÷W&6Râ"“°¢f"7W'6÷%&W6÷W&6T–æfòÒÆ–6F–öâävWE&W6÷W&6U7G&VÒ€¢æWrW&’‚$76WG2ôFVfVÇD—FV×47W'6÷"æ7W""ÂW&”¶–æBå&VÆF—fR’“°¢&WV—&R€¢7W'6÷%&W6÷W&6T–æfóòå7G&VÒ—2æ÷BçVÆÂÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ7W'6÷"6²&W6÷W&6R7G&VÒâ"“°¢W6–ær†7W'6÷%&W6÷W&6T–æfòå7G&VÒ¢W6–ær‡f"7W'6÷"ÒæWr7W'6÷"†7W'6÷%&W6÷W&6T–æfòå7G&VÒ’¢°¢&WV—&R†7W'6÷"—2æ÷BçVÆÂÂ$W‡V7FVBFVfVÇBÖ—FVÒ7W'6÷"7G&VÒÆöBâ"“°¢Ğ¢&WV—&R€¢FVfVÇD—FV×5&–6…FW‡D&÷‚ä—5&VDöæÇ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚ÖWFFFâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×46÷•&–6…FW‡D'WGFöâä6öÖÖæBÂÆ–6F–öä6öÖÖæG2ä6÷’¢bb&VfW&Væ6TWVÇ2„FVfVÇD—FV×46÷•&–6…FW‡D'WGFöâä6öÖÖæEF&vWBÂFVfVÇD—FV×5&–6…FW‡D&÷‚¢bb&VfW&Væ6TWVÇ2„FVfVÇD—FV×57FU&–6…FW‡D'WGFöâä6öÖÖæBÂÆ–6F–öä6öÖÖæG2å7FR¢bb&VfW&Væ6TWVÇ2„FVfVÇD—FV×57FU&–6…FW‡D'WGFöâä6öÖÖæEF&vWBÂFVfVÇD—FV×5&–6…FW‡D&÷‚’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚„ÔÂ6öÖÖæBÖWFFFâ"“°¢f"&–6„Fö7VÖVçBÒ&WV—&UG—SÄfÆ÷tFö7VÖVçCâ€¢FVfVÇD—FV×5&–6…FW‡D&÷‚äFö7VÖVçBÀ¢&FVfVÇBÖ—FVÒ&–6…FW‡D&÷‚fÆ÷tFö7VÖVçB"“°¢&WV—&R€¢&–6„Fö7VÖVçBä&Æö6·2ä6÷VçBãÒ2À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚fÆ÷tFö7VÖVçB&Æö6²ÖWFFFâ"“°¢f"&–6…&w&‚Ò&WV—&UG—SÅ&w&ƒâ€¢&–6„Fö7VÖVçBä&Æö6·2äf—'7D&Æö6²À¢&FVfVÇBÖ—FVÒ&–6…FW‡D&÷‚&w&‚"“°¢f"&–6…'VâÒ&–6…&w&‚ä–æÆ–æW0¢äöeG—SÅ'Vãâ‚¢äf—'7D÷$FVfVÇB‡'VâÓâ'VâåFW‡Bä6öçF–ç2‚$FVfVÇB—FVÒ"Â7G&–æt6ö×&—6öâä÷&F–æÂ’“°¢f"&–6„&öÆBÒ&–6…&w&‚ä–æÆ–æW0¢äöeG—SÄ&öÆCâ‚¢äf—'7D÷$FVfVÇB‚“°¢f"&–6„—FÆ–2Ò&–6…&w&‚ä–æÆ–æW0¢äöeG—SÄ—FÆ–3â‚¢äf—'7D÷$FVfVÇB‚“°¢&WV—&R€¢&–6…'Vâ—2æ÷BçVÆÂbb&–6„&öÆB—2æ÷BçVÆÂbb&–6„—FÆ–2—2æ÷BçVÆÂÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚&w&‚–æÆ–æRG—W2â"“°¢&–6„&öÆBÒ&WV—&UG—SÄ&öÆCâ€¢&–6„&öÆBÀ¢&FVfVÇBÖ—FVÒ&–6…FW‡D&÷‚&öÆB–æÆ–æR"“°¢f"&–6„&öÆE'VâÒ&WV—&UG—SÅ'Vãâ€¢&–6„&öÆBä–æÆ–æW2äf—'7D–æÆ–æRÀ¢&FVfVÇBÖ—FVÒ&–6…FW‡D&÷‚&öÆB'Vâ"“°¢&–6„—FÆ–2Ò&WV—&UG—SÄ—FÆ–3â€¢&–6„—FÆ–2À¢&FVfVÇBÖ—FVÒ&–6…FW‡D&÷‚—FÆ–2–æÆ–æR"“°¢f"&–6„—FÆ–5'VâÒ&WV—&UG—SÅ'Vãâ€¢&–6„—FÆ–2ä–æÆ–æW2äf—'7D–æÆ–æRÀ¢&FVfVÇBÖ—FVÒ&–6…FW‡D&÷‚—FÆ–2'Vâ"“°¢&WV—&R€¢&–6…'VâåFW‡Bä6öçF–ç2‚$FVfVÇB—FVÒ"Â7G&–æt6ö×&—6öâä÷&F–æÂ¢bb&–6„&öÆE'VâåFW‡BÓÒ'&–6‚FW‡B ¢bb&–6„—FÆ–5'VâåFW‡BÓÒ"—FÆ–2FW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚–æÆ–æRFW‡Bâ"“°¢f"&–6„Æ—7BÒ&WV—&UG—SÅ7—7FVÒåv–æF÷w2äFö7VÖVçG2äÆ—7Câ€¢&–6…&w&‚äæW‡D&Æö6²À¢&FVfVÇBÖ—FVÒ&–6…FW‡D&÷‚Æ—7B&Æö6²"“°¢&WV—&R€¢&–6„Æ—7BäÖ&¶W%7G–ÆRÓÒFW‡DÖ&¶W%7G–ÆRäFV6–ÖÂÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚Æ—7BÖ&¶W"ÖWFFFâ"“°¢f"&–6„Æ—7D—FVÒÒ&WV—&UG—SÄÆ—7D—FVÓâ€¢&–6„Æ—7BäÆ—7D—FV×2äf—'7DÆ—7D—FVÒÀ¢&FVfVÇBÖ—FVÒ&–6…FW‡D&÷‚Æ—7D—FVÒ"“°¢f"&–6„Æ—7E&w&‚Ò&WV—&UG—SÅ&w&ƒâ€¢&–6„Æ—7D—FVÒä&Æö6·2äf—'7D&Æö6²À¢&FVfVÇBÖ—FVÒ&–6…FW‡D&÷‚Æ—7D—FVÒ&w&‚"“°¢f"&–6„Æ—7E'VâÒ&WV—&UG—SÅ'Vãâ€¢&–6„Æ—7E&w&‚ä–æÆ–æW2äf—'7D–æÆ–æRÀ¢&FVfVÇBÖ—FVÒ&–6…FW‡D&÷‚Æ—7D—FVÒ'Vâ"“°¢f"&–6„&Æö6µV’Ò&WV—&UG—SÄ&Æö6µT”6öçF–æW#â€¢&–6„Æ—7BäæW‡D&Æö6²À¢&FVfVÇBÖ—FVÒ&–6…FW‡D&÷‚&Æö6µT”6öçF–æW""“°¢f"&–6„&Æö6µFW‡BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢&–6„&Æö6µV’ä6†–ÆBÀ¢&FVfVÇBÖ—FVÒ&–6…FW‡D&÷‚&Æö6µT”6öçF–æW"6†–ÆB"“°¢&WV—&R€¢&–6„Æ—7E'VâåFW‡BÓÒ$FVfVÇB—FVÒÆ—7BVçG'’ ¢bb&–6„&Æö6µFW‡BåFW‡BÓÒ$FVfVÇB—FVÒ&Æö6²T’"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚Æ—7BæB&Æö6²T’6öçFVçBâ"“°¢7G&–ær&–6„Fö7VÖVçEFW‡BÒæWrFW‡E&ævR‡&–6„Fö7VÖVçBä6öçFVçE7F'BÂ&–6„Fö7VÖVçBä6öçFVçDVæB’åFW‡C°¢&WV—&R€¢&–6„Fö7VÖVçEFW‡Bä6öçF–ç2‚$FVfVÇB—FVÒ"Â7G&–æt6ö×&—6öâä÷&F–æÂ¢bb&–6„Fö7VÖVçEFW‡Bä6öçF–ç2‚'&–6‚FW‡B"Â7G&–æt6ö×&—6öâä÷&F–æÂ¢bb&–6„Fö7VÖVçEFW‡Bä6öçF–ç2‚$FVfVÇB—FVÒÆ—7BVçG'’"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚FW‡E&ævRFW‡Bâ"“°¢FW‡E6VÆV7F–öâ&–6…6VÆV7F–öâÒFVfVÇD—FV×5&–6…FW‡D&÷‚å6VÆV7F–öã°¢&–6…6VÆV7F–öâå6VÆV7B‡&–6…'Vâä6öçFVçE7F'BÂ&–6…'Vâä6öçFVçDVæB“°¢&WV—&R€¢&–6…6VÆV7F–öâåFW‡Bä6öçF–ç2‚$FVfVÇB—FVÒ"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚6öÖÖæB6VÆV7F–öâFW‡Bâ"“°¢&WV—&R€¢VF—F–æt6öÖÖæG2åFövvÆT&öÆBä6äW†V7WFR†çVÆÂÂFVfVÇD—FV×5&–6…FW‡D&÷‚’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚FövvÆT&öÆB6öÖÖæBf–Æ&–Æ—G’â"“°¢VF—F–æt6öÖÖæG2åFövvÆT&öÆBäW†V7WFR†çVÆÂÂFVfVÇD—FV×5&–6…FW‡D&÷‚“°¢&WV—&R€¢WVÇ2„föçEvV–v‡G2ä&öÆBÂ&–6…6VÆV7F–öâävWE&÷W'G•fÇVR…FW‡DVÆVÖVçBäföçEvV–v‡E&÷W'G’’’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚FövvÆT&öÆBÆ–VBvV–v‡Bâ"“°¢VF—F–æt6öÖÖæG2åFövvÆT&öÆBäW†V7WFR†çVÆÂÂFVfVÇD—FV×5&–6…FW‡D&÷‚“°¢&WV—&R€¢WVÇ2„föçEvV–v‡G2äæ÷&ÖÂÂ&–6…6VÆV7F–öâävWE&÷W'G•fÇVR…FW‡DVÆVÖVçBäföçEvV–v‡E&÷W'G’’’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚FövvÆT&öÆB&W7F÷&VBvV–v‡Bâ"“°¢&WV—&R€¢Æ–6F–öä6öÖÖæG2ä6÷’ä6äW†V7WFR†çVÆÂÂFVfVÇD—FV×5&–6…FW‡D&÷‚’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚6÷’6öÖÖæBf–Æ&–Æ—G’â"“°¢Æ–6F–öä6öÖÖæG2ä6÷’äW†V7WFR†çVÆÂÂFVfVÇD—FV×5&–6…FW‡D&÷‚“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢6Æ—&ö&Bä6öçF–ç5FW‡B‚¢bb6Æ—&ö&BävWEFW‡B‚’ä6öçF–ç2‚$FVfVÇB—FVÒ"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚6÷–VB6Æ—&ö&BFW‡Bâ"“°¢FW‡Eö–çFW"&–6…7FU÷6—F–öâĞ¢&–6…&w&‚ä6öçFVçDVæBävWD–ç6W'F–öå÷6—F–öâ„Æöv–6ÄF—&V7F–öâä&6·v&B¢óò&–6…&w&‚ä6öçFVçDVæC°¢&–6…6VÆV7F–öâå6VÆV7B‡&–6…7FU÷6—F–öâÂ&–6…7FU÷6—F–öâ“°¢6Æ—&ö&Bå6WEFW‡B‚"FVfVÇB7FVB&–6‚FW‡B"“°¢&WV—&R€¢Æ–6F–öä6öÖÖæG2å7FRä6äW†V7WFR†çVÆÂÂFVfVÇD—FV×5&–6…FW‡D&÷‚’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚7FR6öÖÖæBf–Æ&–Æ—G’â"“°¢Æ–6F–öä6öÖÖæG2å7FRäW†V7WFR†çVÆÂÂFVfVÇD—FV×5&–6…FW‡D&÷‚“°¢G&–äF—7F6†W"‚“°¢&–6„Fö7VÖVçEFW‡BÒæWrFW‡E&ævR‡&–6„Fö7VÖVçBä6öçFVçE7F'BÂ&–6„Fö7VÖVçBä6öçFVçDVæB’åFW‡C°¢&WV—&R€¢&–6„Fö7VÖVçEFW‡Bä6öçF–ç2‚&FVfVÇB7FVB&–6‚FW‡B"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–6…FW‡D&÷‚7FVB6Æ—&ö&BFW‡Bâ"“°¢6Æ—&ö&Bä6ÆV"‚“°¢&WV—&R€¢FVfVÇD—FV×4fÆ÷tFö7VÖVçE67&öÆÅf–WvW"åfW'F–6Å67&öÆÄ&%f—6–&–Æ—G’ÓÒ67&öÆÄ&%f—6–&–Æ—G’äWFòÀ¢$W‡V7FVBFVfVÇBÖ—FVÒfÆ÷tFö7VÖVçE67&öÆÅf–WvW"ÖWFFFâ"“°¢f"67&öÆÄFö7VÖVçBÒ&WV—&UG—SÄfÆ÷tFö7VÖVçCâ€¢FVfVÇD—FV×4fÆ÷tFö7VÖVçE67&öÆÅf–WvW"äFö7VÖVçBÀ¢&FVfVÇBÖ—FVÒfÆ÷tFö7VÖVçE67&öÆÅf–WvW"Fö7VÖVçB"“°¢&WV—&R€¢67&öÆÄFö7VÖVçBåvUFF–ærÓÒæWrF†–6¶æW72ƒR’bb67&öÆÄFö7VÖVçBä&Æö6·2ä6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒfÆ÷tFö7VÖVçE67&öÆÅf–WvW"Fö7VÖVçBÖWFFFâ"“°¢7G&–ær67&öÆÄFö7VÖVçEFW‡BÒæWrFW‡E&ævR‡67&öÆÄFö7VÖVçBä6öçFVçE7F'BÂ67&öÆÄFö7VÖVçBä6öçFVçDVæB’åFW‡C°¢&WV—&R€¢67&öÆÄFö7VÖVçEFW‡Bä6öçF–ç2‚$FVfVÇB—FVÒ67&öÆÂFö7VÖVçB"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒfÆ÷tFö7VÖVçE67&öÆÅf–WvW"FW‡E&ævRFW‡Bâ"“°¢&WV—&R€¢7VÆÄ6†V6²ävWD—4Væ&ÆVB„FVfVÇD—FV×57VÆÄ6†V6µFW‡D&÷‚¢bbFVfVÇD—FV×57VÆÄ6†V6µFW‡D&÷‚å7VÆÄ6†V6²ä—4Væ&ÆV@¢bbFVfVÇD—FV×57VÆÄ6†V6µFW‡D&÷‚å7VÆÄ6†V6²å7VÆÆ–æu&Vf÷&ÒÓÒ7VÆÆ–æu&Vf÷&Òå&TæE÷7G&Vf÷&ÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ7VÆÄ6†V6²ÖWFFFâ"“°¢f"FVfVÇD—FV×47W7FöÔF–7F–öæ&–W2Ò7VÆÄ6†V6²ävWD7W7FöÔF–7F–öæ&–W2„FVfVÇD—FV×57VÆÄ6†V6µFW‡D&÷‚“°¢&WV—&R€¢FVfVÇD—FV×47W7FöÔF–7F–öæ&–W2ä6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ7VÆÄ6†V6²–æ—F–Â7W7FöÒF–7F–öæ'’6÷VçBâ"“°¢FVfVÇD—FV×47W7FöÔF–7F–öæ&–W2äFB†æWrW&’‚&FVfVÇBÖ—FV×2Ö7W7FöÒæÆW‚"ÂW&”¶–æBå&VÆF—fR’“°¢&WV—&R€¢FVfVÇD—FV×47W7FöÔF–7F–öæ&–W2ä6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ7VÆÄ6†V6²7W7FöÒF–7F–öæ'’FBâ"“°¢FVfVÇD—FV×47W7FöÔF–7F–öæ&–W2äFB†æWrW&’‚&FVfVÇBÖ—FV×2Ö7W7FöÒæÆW‚"ÂW&”¶–æBå&VÆF—fR’“°¢&WV—&R€¢FVfVÇD—FV×47W7FöÔF–7F–öæ&–W2ä6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ7VÆÄ6†V6²GWÆ–6FR7W7FöÒF–7F–öæ'’7W&W76–öââ"“°¢&WV—&R€¢FVfVÇD—FV×57VÆÄ6†V6µFW‡D&÷‚ävWE7VÆÆ–ætW'&÷"ƒ’—2çVÆÀ¢bbFVfVÇD—FV×57VÆÄ6†V6µFW‡D&÷‚ävWE7VÆÆ–ætW'&÷%7F'Bƒ’ÓÒÓ¢bbFVfVÇD—FV×57VÆÄ6†V6µFW‡D&÷‚ävWE7VÆÆ–ætW'&÷$ÆVæwF‚ƒ’ÓÒ ¢bbFVfVÇD—FV×57VÆÄ6†V6µFW‡D&÷‚ävWDæW‡E7VÆÆ–ætW'&÷$6†&7FW$–æFW‚ƒÂÆöv–6ÄF—&V7F–öâäf÷'v&B’ÓÒÓÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ7VÆÄ6†V6²æòÖ÷7VÆÆ–ærW'&÷"VW&–W2â"“°¢FVfVÇD—FV×47W7FöÔF–7F–öæ&–W2ä6ÆV"‚“°¢7VÆÄ6†V6²å6WD—4Væ&ÆVB„FVfVÇD—FV×57VÆÄ6†V6µFW‡D&÷‚ÂfÇ6R“°¢&WV—&R€¢FVfVÇD—FV×57VÆÄ6†V6µFW‡D&÷‚å7VÆÄ6†V6²ä—4Væ&ÆVBÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ7VÆÄ6†V6²F—6&ÆVBÖWFFFâ"“°¢&WV—&R€¢FVfVÇD—FV×4w&–Bå&÷tFVf–æ—F–öç2ä6÷VçBÓÒ"bbFVfVÇD—FV×4w&–Bä6öÇVÖäFVf–æ—F–öç2ä6÷VçBÓÒ"À¢$W‡V7FVBFVfVÇBÖ—FVÒw&–B&÷ræB6öÇVÖâFVf–æ—F–öç2â"“°¢&WV—&R€¢w&–BävWE&÷r„FVfVÇD—FV×4w&–D&÷VæEFW‡B’ÓÒbbw&–BävWD6öÇVÖâ„FVfVÇD—FV×4w&–D&÷VæEFW‡B’ÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒw&–BGF6†VB&÷ræB6öÇVÖââ"“°¢&WV—&R€¢FVfVÇD—FV×4w&–D÷&–v–åFW‡BåFW‡BÓÒ$FVfVÇB—FVÒw&–B÷&–v–â"À¢$W‡V7FVBFVfVÇBÖ—FVÒw&–B6†–ÆBFW‡Bâ"“°¢&WV—&R€¢FVfVÇD—FV×4Fö6µæVÂäÆ7D6†–ÆDf–ÆÂÓÒfÇ6RÀ¢$W‡V7FVBFVfVÇBÖ—FVÒFö6µæVÂÆ7D6†–ÆDf–ÆÂÖWFFFâ"“°¢&WV—&R€¢Fö6µæVÂävWDFö6²„FVfVÇD—FV×4Fö6¶VEFW‡B’ÓÒFö6²äÆVgBÀ¢$W‡V7FVBFVfVÇBÖ—FVÒFö6µæVÂGF6†VBFö6²â"“°¢&WV—&R€¢FVfVÇD—FV×4Fö6¶VEFW‡BåFW‡BÓÒ$FVfVÇB—FVÒFö6²ÆVgB"À¢$W‡V7FVBFVfVÇBÖ—FVÒFö6µæVÂ6†–ÆBFW‡Bâ"“°¢&WV—&R€¢FVfVÇD—FV×46çf2åv–GF‚ÓÒƒbbFVfVÇD—FV×46çf2ä†V–v‡BÓÒ#BÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6çf26—¦RÖWFFFâ"“°¢&WV—&R€¢6çf2ävWDÆVgB„FVfVÇD—FV×46çf46†–ÆB’ÓÒ"bb6çf2ävWEF÷„FVfVÇD—FV×46çf46†–ÆB’ÓÒbÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6çf2GF6†VB÷6—F–öââ"“°¢&WV—&R€¢FVfVÇD—FV×46çf46†–ÆBåFW‡BÓÒ$FVfVÇB—FVÒ6çf2"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6çf26†–ÆBFW‡Bâ"“°¢&WV—&R€¢FVfVÇD—FV×5Væ–f÷&Ôw&–Bå&÷w2ÓÒbbFVfVÇD—FV×5Væ–f÷&Ôw&–Bä6öÇVÖç2ÓÒ"bbFVfVÇD—FV×5Væ–f÷&Ôw&–Bä6†–ÆG&Vâä6÷VçBÓÒ"À¢$W‡V7FVBFVfVÇBÖ—FVÒVæ–f÷&Ôw&–BÖWFFFæB6†–ÆG&Vââ"“°¢&WV—&R€¢FVfVÇD—FV×5G&ç6f÷&ÖVEFW‡BåFW‡BÓÒ$FVfVÇB—FVÒG&ç6f÷&ÖVB ¢bbFVfVÇD—FV×5G&ç6f÷&ÖVEFW‡Bå&VæFW%G&ç6f÷&Ô÷&–v–âÓÒæWrö–çBƒãRÂãR’À¢$W‡V7FVBFVfVÇBÖ—FVÒG&ç6f÷&ÒF&vWBÖWFFFâ"“°¢f"&VæFW%G&ç6f÷&Ôw&÷WÒ&WV—&UG—SÅG&ç6f÷&Ôw&÷Wâ€¢FVfVÇD—FV×5G&ç6f÷&ÖVEFW‡Bå&VæFW%G&ç6f÷&ÒÀ¢&FVfVÇBÖ—FVÒ&VæFW%G&ç6f÷&Òw&÷W"“°¢&WV—&R€¢&VæFW%G&ç6f÷&Ôw&÷Wä6†–ÆG&Vâä6÷VçBÓÒ2À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VæFW%G&ç6f÷&Òw&÷W6†–ÆB6÷VçBâ"“°¢f"66ÆUG&ç6f÷&ÒÒ&WV—&UG—SÅ66ÆUG&ç6f÷&Óâ€¢&VæFW%G&ç6f÷&Ôw&÷Wä6†–ÆG&Vå³ÒÀ¢&FVfVÇBÖ—FVÒ&VæFW%G&ç6f÷&Ò66ÆR"“°¢f"&÷FFUG&ç6f÷&ÒÒ&WV—&UG—SÅ&÷FFUG&ç6f÷&Óâ€¢&VæFW%G&ç6f÷&Ôw&÷Wä6†–ÆG&Vå³ÒÀ¢&FVfVÇBÖ—FVÒ&VæFW%G&ç6f÷&Ò&÷FFR"“°¢f"G&ç6ÆFUG&ç6f÷&ÒÒ&WV—&UG—SÅG&ç6ÆFUG&ç6f÷&Óâ€¢&VæFW%G&ç6f÷&Ôw&÷Wä6†–ÆG&Vå³%ÒÀ¢&FVfVÇBÖ—FVÒ&VæFW%G&ç6f÷&ÒG&ç6ÆFR"“°¢f"Æ–÷WEG&ç6f÷&ÒÒ&WV—&UG—SÅ6¶WuG&ç6f÷&Óâ€¢FVfVÇD—FV×5G&ç6f÷&ÖVEFW‡BäÆ–÷WEG&ç6f÷&ÒÀ¢&FVfVÇBÖ—FVÒÆ–÷WEG&ç6f÷&Ò6¶Wr"“°¢&WV—&R€¢ÖF‚ä'2‡66ÆUG&ç6f÷&Òå66ÆU‚Òã#R’Âã¢bbÖF‚ä'2‡66ÆUG&ç6f÷&Òå66ÆU’ÒãsR’Âã¢bbÖF‚ä'2‡&÷FFUG&ç6f÷&ÒäævÆRÒRã’Âã¢bbÖF‚ä'2‡G&ç6ÆFUG&ç6f÷&Òå‚Ò2ã’Âã¢bbÖF‚ä'2‡G&ç6ÆFUG&ç6f÷&Òå’ÒBã’Âã¢bbÖF‚ä'2†Æ–÷WEG&ç6f÷&ÒäævÆU‚ÒRã’Âã¢bbÖF‚ä'2†Æ–÷WEG&ç6f÷&ÒäævÆU’’ÂãÀ¢$W‡V7FVBFVfVÇBÖ—FVÒG&ç6f÷&ÒfÇVW2â"“°¢&WV—&R€¢WVÇ2„FVfVÇD—FV×57FGW4Æ&VÂä6öçFVçBÂ%õ7FGW2"¢bb&VfW&Væ6TWVÇ2„FVfVÇD—FV×57FGW4Æ&VÂåF&vWBÂFVfVÇD—FV×4VF—F&ÆU7FGW5FW‡D&÷‚’À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&VÂF&vWBÖWFFFâ"“°¢&WV—&R€¢WVÇ2„FVfVÇD—FV×4w&÷W&÷‚ä†VFW"Â$FVfVÇB—FVÒw&÷W"¢bb&VfW&Væ6TWVÇ2„FVfVÇD—FV×4w&÷W&÷‚ä6öçFVçBÂFVfVÇD—FV×4w&÷W&÷…FW‡B¢bbFVfVÇD—FV×4w&÷W&÷…FW‡BåFW‡BÓÒ$w&÷W¢FVfVÇB—FVÒ&–æF–ær&VG’"À¢$W‡V7FVBFVfVÇBÖ—FVÒw&÷W&÷‚†VFW"æB&÷VæB6öçFVçBâ"“°¢&WV—&R€¢FVfVÇD—FV×567&öÆÅf–WvW"ä†÷&—¦öçFÅ67&öÆÄ&%f—6–&–Æ—G’ÓÒ67&öÆÄ&%f—6–&–Æ—G’äF—6&ÆV@¢bbFVfVÇD—FV×567&öÆÅf–WvW"åfW'F–6Å67&öÆÄ&%f—6–&–Æ—G’ÓÒ67&öÆÄ&%f—6–&–Æ—G’äWFğ¢bbFVfVÇD—FV×567&öÆÅf–WvW"ä6ä6öçFVçE67&öÆÀ¢bb&VfW&Væ6TWVÇ2„FVfVÇD—FV×567&öÆÅf–WvW"ä6öçFVçBÂFVfVÇD—FV×567&öÆÅf–WvW%FW‡B¢bbFVfVÇD—FV×567&öÆÅf–WvW%FW‡BåFW‡BÓÒ$FVfVÇB—FVÒ67&öÆÂf–WvW"6öçFVçB"À¢$W‡V7FVBFVfVÇBÖ—FVÒ67&öÆÅf–WvW"ÖWFFFæB6öçFVçBâ"“°¢&WV—&R€¢FVfVÇD—FV×46†V6´&÷‚ä—46†V6¶VBÓÒG'VRbbf–WtÖöFVÂä—4f÷&Ô÷F–öäVæ&ÆVBÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6†V6´&÷‚–æ—F–Â&–æF–ærâ"“°¢FVfVÇD—FV×46†V6´&÷‚ä—46†V6¶VBÒfÇ6S°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×46†V6´&÷‚ä—46†V6¶VBÓÒfÇ6Rbbf–WtÖöFVÂä—4f÷&Ô÷F–öäVæ&ÆVBÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6†V6´&÷‚Gvò×v’&–æF–ærFòWFFR6÷W&6Râ"“°¢f–WtÖöFVÂä—4f÷&Ô÷F–öäVæ&ÆVBÒG'VS°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×46†V6´&÷‚ä—46†V6¶VBÓÒG'VRÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6†V6´&÷‚&–æF–ærFòö'6W'fR6÷W&6RWFFRâ"“°¢&WV—&R€¢FVfVÇD—FV×5&F–õæVÂä÷&–VçFF–öâÓÒ÷&–VçFF–öâä†÷&—¦öçFÂÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ&F–ô'WGFöâæVÂ÷&–VçFF–öââ"“°¢&WV—&R€¢WVÇ2„FVfVÇD—FV×4f—'7E&F–ô'WGFöâäw&÷WæÖRÂ$FVfVÇD—FV×4ÖöFR"¢bbWVÇ2„FVfVÇD—FV×56V6öæE&F–ô'WGFöâäw&÷WæÖRÂ$FVfVÇD—FV×4ÖöFR"’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&F–ô'WGFöâw&÷WÖWFFFâ"“°¢&WV—&R€¢FVfVÇD—FV×4f—'7E&F–ô'WGFöâä—46†V6¶VBÓÒG'VRbbFVfVÇD—FV×56V6öæE&F–ô'WGFöâä—46†V6¶VBÓÒfÇ6RÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ&F–ô'WGFöâ–æ—F–Âw&÷W7FFRâ"“°¢–çB&F–ô6†V6¶VD&Vf÷&RÒ&F–ô'WGFöä6†V6¶VD6÷VçC°¢FVfVÇD—FV×56V6öæE&F–ô'WGFöâä—46†V6¶VBÒG'VS°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×4f—'7E&F–ô'WGFöâä—46†V6¶VBÓÒfÇ6RbbFVfVÇD—FV×56V6öæE&F–ô'WGFöâä—46†V6¶VBÓÒG'VRÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ&F–ô'WGFöâw&÷WW†6ÇW6—f—G’â"“°¢&WV—&R€¢&F–ô'WGFöä6†V6¶VD6÷VçBÓÒ&F–ô6†V6¶VD&Vf÷&R²¢bbÆ7E&F–ô'WGFöä6†V6¶VDæÖRÓÒæÖVöb„FVfVÇD—FV×56V6öæE&F–ô'WGFöâ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&F–ô'WGFöâ6†V6¶VB†æFÆW"â"“°¢&WV—&R€¢FVfVÇD—FV×56Æ–FW"äÖ–æ–×VÒÓÒ ¢bbFVfVÇD—FV×56Æ–FW"äÖ†–×VÒÓÒ ¢bbFVfVÇD—FV×56Æ–FW"åF–6´g&WVVæ7’ÓÒ ¢bbFVfVÇD—FV×56Æ–FW"ä—56æFõF–6´Væ&ÆVBÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ–FW"&ævRÖWFFFâ"“°¢&WV—&R€¢ÖF‚ä'2„FVfVÇD—FV×56Æ–FW"åfÇVRÒ#Rã’Âã¢bbÖF‚ä'2„FVfVÇD—FV×5&öw&W74&"åfÇVRÒ#Rã’ÂãÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ&ævR6öçG&öÇ2–æ—F–Â&–æF–ærâ"“°¢FVfVÇD—FV×56Æ–FW"åfÇVRÒsã°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢ÖF‚ä'2…f–WtÖöFVÂäf÷&Õ&öw&W72Òsã’Âã¢bbÖF‚ä'2„FVfVÇD—FV×5&öw&W74&"åfÇVRÒsã’ÂãÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ–FW"Gvò×v’&–æF–ærFòWFFR&öw&W72â"“°¢f–WtÖöFVÂäf÷&Õ&öw&W72ÒCã°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢ÖF‚ä'2„FVfVÇD—FV×56Æ–FW"åfÇVRÒCã’Âã¢bbÖF‚ä'2„FVfVÇD—FV×5&öw&W74&"åfÇVRÒCã’ÂãÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ&ævR6öçG&öÇ2Fòö'6W'fR6÷W&6RWFFRâ"“°¢&WV—&R€¢FVfVÇD—FV×577v÷&D&÷‚äÖ„ÆVæwF‚ÓÒ3"bbFVfVÇD—FV×577v÷&D&÷‚å77v÷&D6†"ÓÒr¢rÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ77v÷&D&÷‚ÖWFFFâ"“°¢–çB77v÷&D6†ævVD&Vf÷&RÒ77v÷&D6†ævVD6÷VçC°¢FVfVÇD—FV×577v÷&D&÷‚å77v÷&BÒ&FVfVÇB×6V7&WB#°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢77v÷&D6†ævVD6÷VçBÓÒ77v÷&D6†ævVD&Vf÷&R²¢bbÆ7E77v÷&EfÇVRÓÒ&FVfVÇB×6V7&WB"À¢$W‡V7FVBFVfVÇBÖ—FVÒ77v÷&D&÷‚6†ævVB†æFÆW"â"“°¢&WV—&R€¢FVfVÇD—FV×46ÆVæF"å6VÆV7F–öäÖöFRÓÒ6ÆVæF%6VÆV7F–öäÖöFRå6–ævÆTFFP¢bbFVfVÇD—FV×46ÆVæF"å6VÆV7FVDFFRÓÒæWrFFUF–ÖRƒ##bÂbÂ#B¢bbFVfVÇD—FV×46ÆVæF"äF—7Æ”FFRÓÒæWrFFUF–ÖRƒ##bÂbÂ#B’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6ÆVæF"–æ—F–ÂFFR&–æF–ærâ"“°¢&WV—&R€¢FVfVÇD—FV×4FFU–6¶W"å6VÆV7FVDFFRÓÒæWrFFUF–ÖRƒ##bÂbÂ#B¢bbFVfVÇD—FV×4FFU–6¶W"äF—7Æ”FFU7F'BÓÒæWrFFUF–ÖRƒ##bÂÂ¢bbFVfVÇD—FV×4FFU–6¶W"äF—7Æ”FFTVæBÓÒæWrFFUF–ÖRƒ##bÂ"Â3¢bbFVfVÇD—FV×4FFU–6¶W"å6VÆV7FVDFFTf÷&ÖBÓÒFFU–6¶W$f÷&ÖBäÆöærÀ¢$W‡V7FVBFVfVÇBÖ—FVÒFFU–6¶W"–æ—F–ÂFFR&–æF–ærâ"“°¢FVfVÇD—FV×4FFU–6¶W"å6VÆV7FVDFFRÒæWrFFUF–ÖRƒ##bÂrÂ“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢f–WtÖöFVÂå6VÆV7FVDFFRÓÒæWrFFUF–ÖRƒ##bÂrÂ¢bbFVfVÇD—FV×46ÆVæF"å6VÆV7FVDFFRÓÒæWrFFUF–ÖRƒ##bÂrÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒFFU–6¶W"Gvò×v’6VÆV7FVBÖFFR&–æF–ærâ"“°¢f–WtÖöFVÂå6VÆV7FVDFFRÒæWrFFUF–ÖRƒ##bÂ‚ÂR“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×4FFU–6¶W"å6VÆV7FVDFFRÓÒæWrFFUF–ÖRƒ##bÂ‚ÂR¢bbFVfVÇD—FV×46ÆVæF"å6VÆV7FVDFFRÓÒæWrFFUF–ÖRƒ##bÂ‚ÂR’À¢$W‡V7FVBFVfVÇBÖ—FVÒFFR6öçG&öÇ2Fòö'6W'fR6÷W&6RWFFRâ"“°¢f"FööÅF—Ò&WV—&UG—SÅFööÅF—â€¢FVfVÇD—FV×5÷W÷væW$'WGFöâåFööÅF—À¢&FVfVÇBÖ—FVÒFööÅF—"“°¢f"FööÅF—FW‡BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢FööÅF—ä6öçFVçBÀ¢&FVfVÇBÖ—FVÒFööÅF—6öçFVçB"“°¢&WV—&R€¢FööÅF—FW‡BåFW‡BÓÒ$FVfVÇB—FVÒFööÇF—6öçFVçB"À¢$W‡V7FVBFVfVÇBÖ—FVÒFööÅF—6öçFVçBâ"“°¢&WV—&R€¢FVfVÇD—FV×57FæFÆöæU÷WåÆ6VÖVçBÓÒÆ6VÖVçDÖöFRä&÷GFöĞ¢bb&VfW&Væ6TWVÇ2„FVfVÇD—FV×57FæFÆöæU÷WåÆ6VÖVçEF&vWBÂFVfVÇD—FV×5÷W÷væW$'WGFöâ¢bbFVfVÇD—FV×57FæFÆöæU÷Wå7F—4÷VâÓÒfÇ6RÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ÷WÆ6VÖVçBÖWFFFâ"“°¢&WV—&R€¢FVfVÇD—FV×57FæFÆöæU÷Wä—4÷VâÓÒfÇ6RÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ÷W–æ—F–Â6Æ÷6VB7FFRâ"“°¢FVfVÇD—FV×57FæFÆöæU÷Wä—4÷VâÒG'VS°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×57FæFÆöæU÷Wä—4÷VâÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ÷WFò÷VâF‡&÷Vv‚÷'F&ÆR÷W6W'f–6Râ"“°¢f"÷WFW‡BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢FVfVÇD—FV×57FæFÆöæU÷Wä6†–ÆB—2&÷&FW"÷W&÷&FW"ò÷W&÷&FW"ä6†–ÆB¢çVÆÂÀ¢&FVfVÇBÖ—FVÒ÷W6öçFVçB"“°¢&WV—&R€¢÷WFW‡BåFW‡BÓÒ$FVfVÇB—FVÒ7FæFÆöæR÷W6öçFVçB"À¢$W‡V7FVBFVfVÇBÖ—FVÒ÷W6öçFVçBâ"“°¢FVfVÇD—FV×57FæFÆöæU÷Wä—4÷VâÒfÇ6S°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×57FæFÆöæU÷Wä—4÷VâÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ÷WFò6Æ÷6RF‡&÷Vv‚÷'F&ÆR÷W6W'f–6Râ"“°¢&WV—&R€¢FVfVÇD—FV×4ÖVçRä—FV×2ä6÷VçBÓÒbb&VfW&Væ6TWVÇ2„FVfVÇD—FV×4ÖVçRä—FV×5³ÒÂFVfVÇD—FV×5&ö÷DÖVçT—FVÒ’À¢$W‡V7FVBFVfVÇBÖ—FVÒÖVçR&ö÷B—FVÒâ"“°¢&WV—&R€¢WVÇ2„FVfVÇD—FV×5&ö÷DÖVçT—FVÒä†VFW"Â%ôf–ÆR"’bbFVfVÇD—FV×5&ö÷DÖVçT—FVÒä—FV×2ä6÷VçBÓÒBÀ¢$W‡V7FVBFVfVÇBÖ—FVÒÖVçT—FVÒ&ö÷BÖWFFFâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×46öÖÖæDÖVçT—FVÒä6öÖÖæBÂFVfVÇD—FV×46öÖÖæB¢bbWVÇ2„FVfVÇD—FV×46öÖÖæDÖVçT—FVÒä6öÖÖæE&ÖWFW"Â&ÖVçRÖ6öÖÖæB"’À¢$W‡V7FVBFVfVÇBÖ—FVÒÖVçT—FVÒ6öÖÖæBÖWFFFâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×5&ö÷DÖVçT—FVÒä—FV×5³ÒÂFVfVÇD—FV×4ÖVçU6W&F÷"’À¢$W‡V7FVBFVfVÇBÖ—FVÒÖVçR6W&F÷"ÖWFFFâ"“°¢FVfVÇD—FV×46Æ–6´ÖVçT—FVÒå&—6TWfVçB†æWr&÷WFVDWfVçD&w2„ÖVçT—FVÒä6Æ–6´WfVçB’“°¢&WV—&R€¢ÖVçT6Æ–6´6÷VçBÓÒbbWVÇ2„FVfVÇD—FV×46Æ–6´ÖVçT—FVÒåFrÂ&FVfVÇBÖ—FVÒÖVçR6Æ–6¶VB"’À¢$W‡V7FVBFVfVÇBÖ—FVÒÖVçT—FVÒ6Æ–6²†æFÆW"â"“°¢FVfVÇD—FV×46†V6¶&ÆTÖVçT—FVÒä—46†V6¶VBÒG'VS°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×46†V6¶&ÆTÖVçT—FVÒä—46†V6¶VBbbÖVçT6†V6¶VD6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒÖVçT—FVÒ6†V6¶VB†æFÆW"â"“°¢FVfVÇD—FV×46†V6¶&ÆTÖVçT—FVÒä—46†V6¶VBÒfÇ6S°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×46†V6¶&ÆTÖVçT—FVÒä—46†V6¶VBbbÖVçUVæ6†V6¶VD6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒÖVçT—FVÒVæ6†V6¶VB†æFÆW"â"“°¢&WV—&R€¢FVfVÇD—FV×5FööÄ&%G&’åFööÄ&'2ä6÷VçBÓÒbb&VfW&Væ6TWVÇ2„FVfVÇD—FV×5FööÄ&%G&’åFööÄ&'5³ÒÂFVfVÇD—FV×5FööÄ&"’À¢$W‡V7FVBFVfVÇBÖ—FVÒFööÄ&%G&’FööÆ&"&Vv—7G&F–öââ"“°¢&WV—&R€¢FVfVÇD—FV×5FööÄ&"ä—FV×2ä6÷VçBÓÒ2À¢$W‡V7FVBFVfVÇBÖ—FVÒFööÄ&"—FVÒ6÷VçBâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×5FööÄ&$6öÖÖæD'WGFöâä6öÖÖæBÂFVfVÇD—FV×46öÖÖæB¢bbWVÇ2„FVfVÇD—FV×5FööÄ&$6öÖÖæD'WGFöâä6öÖÖæE&ÖWFW"Â'FööÆ&"Ö6öÖÖæB"’À¢$W‡V7FVBFVfVÇBÖ—FVÒFööÄ&"6öÖÖæBÖWFFFâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×5FööÄ&"ä—FV×5³ÒÂFVfVÇD—FV×5FööÄ&%6W&F÷"¢bbFVfVÇD—FV×5FööÄ&%FövvÆRä—46†V6¶VBÓÒG'VRÀ¢$W‡V7FVBFVfVÇBÖ—FVÒFööÄ&"6W&F÷"æBFövvÆRÖWFFFâ"“°¢&WV—&R€¢FVfVÇD—FV×57FGW4&"ä—FV×2ä6÷VçBÓÒ¢bb&VfW&Væ6TWVÇ2„FVfVÇD—FV×57FGW4&"ä—FV×5³ÒÂFVfVÇD—FV×57FGW4&$—FVÒ¢bbFVfVÇD—FV×57FGW4&%FW‡BåFW‡BÓÒ%7FGW3¢FVfVÇB—FVÒ&–æF–ær&VG’"À¢$W‡V7FVBFVfVÇBÖ—FVÒ7FGW4&"&÷VæB6öçFVçBâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×46öçFW‡DÖVçT÷væW"ä6öçFW‡DÖVçRÂFVfVÇD—FV×46öçFW‡DÖVçR’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçFW‡DÖVçR÷væW"ÖWFFFâ"“°¢&WV—&R€¢FVfVÇD—FV×46öçFW‡DÖVçRä—FV×2ä6÷VçBÓÒBÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçFW‡DÖVçR—FVÒ6÷VçBâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×46öçFW‡D6öÖÖæDÖVçT—FVÒä6öÖÖæBÂFVfVÇD—FV×46öÖÖæB¢bbWVÇ2„FVfVÇD—FV×46öçFW‡D6öÖÖæDÖVçT—FVÒä6öÖÖæE&ÖWFW"Â&6öçFW‡BÖ6öÖÖæB"’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçFW‡DÖVçR6öÖÖæBÖWFFFâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×46öçFW‡DÖVçRä—FV×5³ÒÂFVfVÇD—FV×46öçFW‡DÖVçU6W&F÷"’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçFW‡DÖVçR6W&F÷"ÖWFFFâ"“°¢FVfVÇD—FV×46öçFW‡D6Æ–6´ÖVçT—FVÒå&—6TWfVçB†æWr&÷WFVDWfVçD&w2„ÖVçT—FVÒä6Æ–6´WfVçB’“°¢&WV—&R€¢6öçFW‡DÖVçT6Æ–6´6÷VçBÓÒbbWVÇ2„FVfVÇD—FV×46öçFW‡D6Æ–6´ÖVçT—FVÒåFrÂ&FVfVÇBÖ—FVÒ6öçFW‡BÖVçR6Æ–6¶VB"’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçFW‡DÖVçR6Æ–6²†æFÆW"â"“°¢FVfVÇD—FV×46öçFW‡D6†V6¶&ÆTÖVçT—FVÒä—46†V6¶VBÒG'VS°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×46öçFW‡D6†V6¶&ÆTÖVçT—FVÒä—46†V6¶VBbb6öçFW‡DÖVçT6†V6¶VD6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçFW‡DÖVçR6†V6¶VB†æFÆW"â"“°¢FVfVÇD—FV×46öçFW‡D6†V6¶&ÆTÖVçT—FVÒä—46†V6¶VBÒfÇ6S°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×46öçFW‡D6†V6¶&ÆTÖVçT—FVÒä—46†V6¶VBbb6öçFW‡DÖVçUVæ6†V6¶VD6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçFW‡DÖVçRVæ6†V6¶VB†æFÆW"â"“°¢&WV—&R€¢FVfVÇD—FV×5F$6öçG&öÂå6VÆV7FVD–æFW‚ÓÒbb&VfW&Væ6TWVÇ2„FVfVÇD—FV×5F$6öçG&öÂå6VÆV7FVD—FVÒÂFVfVÇD—FV×4FWF–Ç5F"’À¢$W‡V7FVBFVfVÇBÖ—FVÒF$6öçG&öÂ–æ—F–Â6VÆV7F–öââ"“°¢&WV—&R€¢WVÇ2„FVfVÇD—FV×4÷fW'f–WuF"ä†VFW"Â$÷fW'f–Wr"’bbWVÇ2„FVfVÇD—FV×4FWF–Ç5F"ä†VFW"Â$FWF–Ç2"’À¢$W‡V7FVBFVfVÇBÖ—FVÒF$—FVÒ†VFW'2â"“°¢FVfVÇD—FV×5F$6öçG&öÂåWFFTÆ–÷WB‚“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×4FWF–Ç5F%FW‡BåFW‡BÓÒ%F#¢FVfVÇB—FVÒ&–æF–ær&VG’"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6VÆV7FVBF$—FVÒ&–æF–ærFW‡Bâ"“°¢FVfVÇD—FV×5F$6öçG&öÂå6VÆV7FVD—FVÒÒFVfVÇD—FV×4÷fW'f–WuF#°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×5F$6öçG&öÂå6VÆV7FVD–æFW‚ÓÒbb&VfW&Væ6TWVÇ2„FVfVÇD—FV×5F$6öçG&öÂå6VÆV7FVD—FVÒÂFVfVÇD—FV×4÷fW'f–WuF"’À¢$W‡V7FVBFVfVÇBÖ—FVÒF$6öçG&öÂ&öw&ÖÖF–26VÆV7F–öââ"“°¢FVfVÇD—FV×5F$6öçG&öÂå6VÆV7FVD—FVÒÒFVfVÇD—FV×4FWF–Ç5F#°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×5F$6öçG&öÂå6VÆV7FVD–æFW‚ÓÒbb&VfW&Væ6TWVÇ2„FVfVÇD—FV×5F$6öçG&öÂå6VÆV7FVD—FVÒÂFVfVÇD—FV×4FWF–Ç5F"’À¢$W‡V7FVBFVfVÇBÖ—FVÒF$6öçG&öÂ6VÆV7F–öâ&W7F÷&Râ"“°¢&WV—&R€¢FVfVÇD—FV×4W‡æFW"ä—4W‡æFVBbbWVÇ2„FVfVÇD—FV×4W‡æFW"ä†VFW"Â$FVfVÇB—FVÒW‡æFW""’À¢$W‡V7FVBFVfVÇBÖ—FVÒW‡æFW"–æ—F–Â7FFRâ"“°¢–çBW‡æFW$W‡æFVD&Vf÷&RÒW‡æFW$W‡æFVD6÷VçC°¢–çBW‡æFW$6öÆÆ6VD&Vf÷&RÒW‡æFW$6öÆÆ6VD6÷VçC°¢FVfVÇD—FV×4W‡æFW"ä—4W‡æFVBÒG'VS°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×4W‡æFW"ä—4W‡æFVBbbW‡æFW$W‡æFVD6÷VçBÓÒW‡æFW$W‡æFVD&Vf÷&R²À¢$W‡V7FVBFVfVÇBÖ—FVÒW‡æFW"W‡æFVBWfVçBâ"“°¢&WV—&R€¢FVfVÇD—FV×4W‡æFW%FW‡BåFW‡BÓÒ$FVfVÇB—FVÒW‡æFW"6öçFVçB"À¢$W‡V7FVBFVfVÇBÖ—FVÒW‡æFW"6öçFVçBFW‡Bâ"“°¢FVfVÇD—FV×4W‡æFW"ä—4W‡æFVBÒfÇ6S°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×4W‡æFW"ä—4W‡æFVBbbW‡æFW$6öÆÆ6VD6÷VçBÓÒW‡æFW$6öÆÆ6VD&Vf÷&R²À¢$W‡V7FVBFVfVÇBÖ—FVÒW‡æFW"6öÆÆ6VBWfVçBâ"“°¢&WV—&R€¢6öæf–wW&F–öäÖævW"ä6WGF–æw5²$FVfVÇD—FV×56Fµ6WGF–ær%ÒÓÒ$FVfVÇB—FVÒ4D²6öæf–rfÇVR"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öæf–r6WGF–ærâ"“°¢fÆ–FFTFVfVÇD—FV×4ÖW76vT&÷‚‚“°¢fÆ–FFTFVfVÇD—FV×5&–çDF–Æör‚“°¢fÆ–FFTFVfVÇD—FV×4f–ÆTF–Æöw2‚“°¢fÆ–FFTFVfVÇD—FV×46Æ—&ö&B‚“°¢&WV—&R€¢FVfVÇD—FV×5æVÂä6F–öâÓÒ$FVfVÇB—FVÒæVÂ6F–öâ"À¢$W‡V7FVBFVfVÇBÖ—FVÒW6W$6öçG&öÂFWVæFVæ7’&÷W'G’fÇVRâ"“°¢&WV—&R€¢FVfVÇD—FV×5æVÂä6F–öåFW‡BÓÒ$FVfVÇB—FVÒæVÂ6F–öâ"À¢$W‡V7FVBFVfVÇBÖ—FVÒW6W$6öçG&öÂVÆVÖVçDæÖR&–æF–ærâ"“°¢&WV—&R€¢FVfVÇD—FV×4Æ–'&'•æVÂä6F–öâÓÒ$FVfVÇB—FVÒ&VfW&Væ6VBÆ–'&'’6F–öâ"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’FWVæFVæ7’&÷W'G’fÇVRâ"“°¢&WV—&R€¢FVfVÇD—FV×4Æ–'&'•æVÂä6F–öåFW‡BÓÒ$FVfVÇB—FVÒ&VfW&Væ6VBÆ–'&'’6F–öâ"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’VÆVÖVçDæÖR&–æF–ærâ"“°¢FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂäÇ•FV×ÆFR‚“°¢f"F†VÖU&ö÷BÒ&WV—&UG—SÄ&÷&FW#â€¢FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂåFV×ÆFRäf–æDæÖR€¢$FVfVÇD—FV×4Æ–'&'•F†VÖU&ö÷B"À¢FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂ’À¢&FVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’F†VÖR&ö÷B"“°¢f"F†VÖUFW‡BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂåFV×ÆFRäf–æDæÖR€¢$FVfVÇD—FV×4Æ–'&'•F†VÖUFW‡B"À¢FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂ’À¢&FVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’F†VÖRFW‡B"“°¢f"F†VÖT&6¶w&÷VæBÒ&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ€¢F†VÖU&ö÷Bä&6¶w&÷VæBÀ¢&FVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’F†VÖR&6¶w&÷VæB"“°¢f"F†VÖTf÷&Vw&÷VæBÒ&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ€¢F†VÖUFW‡Bäf÷&Vw&÷VæBÀ¢&FVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’F†VÖRf÷&Vw&÷VæB"“°¢&WV—&R€¢F†VÖUFW‡BåFW‡BÓÒ$FVfVÇB—FVÒÆ–'&'’F†VÖRFW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’F†VÖRFV×ÆFT&–æF–ærFW‡Bâ"“°¢&WV—&R€¢F†VÖT&6¶w&÷VæBä6öÆ÷"ÓÒ6öÆ÷"äg&öÕ&v"ƒƒCRÂƒcrÂƒƒ’’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’F†VÖR&6¶w&÷VæBâ"“°¢&WV—&R€¢F†VÖTf÷&Vw&÷VæBä6öÆ÷"ÓÒ6öÆ÷"äg&öÕ&v"ƒƒ"Âƒ3BÂƒSb’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’F†VÖRf÷&Vw&÷VæBâ"“°¢&WV—&R€¢FVfVÇD—FV×4g&ÖRå6÷W&6SòåFõ7G&–ær‚’ä6öçF–ç2‚$FVfVÇD—FV×5vRç†ÖÂ"Â7G&–æt6ö×&—6öâä÷&F–æÄ–væ÷&T66R’ÓÒG'VRÀ¢$W‡V7FVBFVfVÇBÖ—FVÒg&ÖR6÷W&6Râ"“°¢FVfVÇD—FV×4g&ÖRåWFFTÆ–÷WB‚“°¢V×F—7F6†W%VçF–Â€¢‚’ÓâFVfVÇD—FV×4g&ÖRä6öçFVçB—2FVfVÇD—FV×5vRÀ¢&FVfVÇBÖ—FVÒ6ö×–ÆVBvRg&ÖR6öçFVçB"“°¢f"FVfVÇD—FV×5vRÒ&WV—&UG—SÄFVfVÇD—FV×5vSâ€¢FVfVÇD—FV×4g&ÖRä6öçFVçBÀ¢&FVfVÇBÖ—FVÒ6ö×–ÆVBvRg&ÖR6öçFVçB"“°¢&WV—&R€¢FVfVÇD—FV×5vRåvUFW‡BÓÒ$FVfVÇB—FVÒ6ö×–ÆVBvRFW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×–ÆVBvRFW‡Bâ"“°¢f"ÆöFVEæVÂÒ&WV—&UG—SÄFVfVÇD—FV×5æVÃâ€¢Æ–6F–öâäÆöD6ö×öæVçB€¢æWrW&’€¢"ôW‡FW&æÅ6F´FVfVÇD—FV×4¶6ö×öæVçBôFVfVÇD—FV×5æVÂç†ÖÂ"À¢W&”¶–æBå&VÆF—fR’’À¢&FVfVÇBÖ—FVÒÆ–6F–öâäÆöD6ö×öæVçBæVÂ"“°¢ÆöFVEæVÂä6F–öâÒ$FVfVÇB—FVÒÆöFVBæVÂ6F–öâ#°¢ÆöFVEæVÂåWFFTÆ–÷WB‚“°¢&WV—&R€¢ÆöFVEæVÂä6F–öåFW‡BÓÒ$FVfVÇB—FVÒÆöFVBæVÂ6F–öâ"À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ–6F–öâäÆöD6ö×öæVçBVÆVÖVçDæÖR&–æF–ærâ"“°¢f"ÆöFVDÆ–'&'•æVÂÒ&WV—&UG—SÄFVfVÇD—FV×4Æ–'&'•æVÃâ€¢Æ–6F–öâäÆöD6ö×öæVçB€¢æWrW&’€¢"ôW‡FW&æÅ6F´FVfVÇD—FV×4Æ–'&'“¶6ö×öæVçBôFVfVÇD—FV×4Æ–'&'•æVÂç†ÖÂ"À¢W&”¶–æBå&VÆF—fR’’À¢&FVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’Æ–6F–öâäÆöD6ö×öæVçBæVÂ"“°¢ÆöFVDÆ–'&'•æVÂä6F–öâÒ$FVfVÇB—FVÒÆöFVBÆ–'&'’æVÂ6F–öâ#°¢ÆöFVDÆ–'&'•æVÂåWFFTÆ–÷WB‚“°¢&WV—&R€¢ÆöFVDÆ–'&'•æVÂä6F–öåFW‡BÓÒ$FVfVÇB—FVÒÆöFVBÆ–'&'’æVÂ6F–öâ"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’Æ–6F–öâäÆöD6ö×öæVçBVÆVÖVçDæÖR&–æF–ærâ"“°¢f"ÆöFVDÆ–'&'•vRÒ&WV—&UG—SÄFVfVÇD—FV×4Æ–'&'•vSâ€¢Æ–6F–öâäÆöD6ö×öæVçB€¢æWrW&’€¢"ôW‡FW&æÅ6F´FVfVÇD—FV×4Æ–'&'“¶6ö×öæVçBôFVfVÇD—FV×4Æ–'&'•vRç†ÖÂ"À¢W&”¶–æBå&VÆF—fR’’À¢&FVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’Æ–6F–öâäÆöD6ö×öæVçBvR"“°¢&WV—&R€¢ÆöFVDÆ–'&'•vRåvUFW‡BÓÒ$FVfVÇB—FVÒÆ–'&'’6ö×–ÆVBvRFW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’6ö×–ÆVBvRFW‡Bâ"“°¢&WV—&R€¢G'”f–æE&W6÷W&6R†æWrFFFV×ÆFT¶W’‡G—Vöb„FVfVÇD—FV×4—FVÒ’’’—2FFFV×ÆFRÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ–×Æ–6—BFFFV×ÆFR&W6÷W&6RÆöö·Wâ"“°¢FVfVÇD—FV×4–×Æ–6—EFV×ÆFU&W6VçFW"äÇ•FV×ÆFR‚“°¢FVfVÇD—FV×4–×Æ–6—EFV×ÆFU&W6VçFW"åWFFTÆ–÷WB‚“°¢f"–×Æ–6—EFV×ÆFUFW‡BÒf–æEf—7VÄFW66VæFçD'”æÖSÅFW‡D&Æö6³â€¢FVfVÇD—FV×4–×Æ–6—EFV×ÆFU&W6VçFW"À¢$FVfVÇD—FV×4–×Æ–6—EFV×ÆFUFW‡B"“°¢&WV—&R€¢–×Æ–6—EFV×ÆFUFW‡B—2æ÷BçVÆÂÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ–×Æ–6—BFFFV×ÆFRf—7VÂG&VRâ"“°¢&WV—&R€¢–×Æ–6—EFV×ÆFUFW‡BåFW‡BÓÒ%FV×ÆFS¢FVfVÇB—FVÒFV×ÆFRFF"À¢$W‡V7FVBFVfVÇBÖ—FVÒ–×Æ–6—BFFFV×ÆFR&–æF–ærFW‡Bâ"“°¢&WV—&R€¢7G&–æräWVÇ2€¢–×Æ–6—EFV×ÆFUFW‡BåFr27G&–ærÀ¢&FVfVÇBÖ—FVÒ–×Æ–6—BFV×ÆFR"À¢7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ–×Æ–6—BFFFV×ÆFRFW‡BFrâ"“°¢f"ö&¦V7E&÷f–FW"Ò&WV—&UG—SÄö&¦V7DFF&÷f–FW#â€¢f–æE&W6÷W&6R‚$FVfVÇD—FV×4ö&¦V7DFF&÷f–FW""’À¢&FVfVÇBÖ—FVÒö&¦V7DFF&÷f–FW"&W6÷W&6R"“°¢&WV—&R‚ö&¦V7E&÷f–FW"ä—47–æ6‡&öæ÷W2Â$W‡V7FVBFVfVÇBÖ—FVÒö&¦V7DFF&÷f–FW"7–æ6‡&öæ÷W2ÖöFRâ"“°¢&WV—&R€¢ö&¦V7E&÷f–FW"äÖWF†öDæÖRÓÒ$7&VFUFW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒö&¦V7DFF&÷f–FW"ÖWF†öBæÖRâ"“°¢&WV—&R€¢ö&¦V7E&÷f–FW"äö&¦V7EG—RÓÒG—Vöb„FVfVÇD—FV×5&÷f–FW%6÷W&6R’À¢$W‡V7FVBFVfVÇBÖ—FVÒö&¦V7DFF&÷f–FW"ö&¦V7BG—Râ"“°¢&WV—&R€¢7G&–æräWVÇ2†ö&¦V7E&÷f–FW"äFF27G&–ærÂ$FVfVÇB—FVÒö&¦V7B&÷f–FW"FW‡B"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒö&¦V7DFF&÷f–FW"FFâ"“°¢&WV—&R€¢FVfVÇD—FV×4ö&¦V7E&÷f–FW%FW‡BåFW‡BÓÒ$FVfVÇB—FVÒö&¦V7B&÷f–FW"FW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒö&¦V7DFF&÷f–FW"&÷VæBFW‡Bâ"“°¢f"ö&¦V7E&÷f–FW$&–æF–ærÒFVfVÇD—FV×4ö&¦V7E&÷f–FW%FW‡BävWD&–æF–ætW‡&W76–öâ…FW‡D&Æö6²åFW‡E&÷W'G’¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡V7FVBFVfVÇBÖ—FVÒö&¦V7DFF&÷f–FW"FW‡B&–æF–ærâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2†ö&¦V7E&÷f–FW$&–æF–ærå&VçD&–æF–ærå6÷W&6RÂö&¦V7E&÷f–FW"’À¢$W‡V7FVBFVfVÇBÖ—FVÒö&¦V7DFF&÷f–FW"&–æF–ær6÷W&6Râ"“°¢f"†ÖÅ&÷f–FW"Ò&WV—&UG—SÅ†ÖÄFF&÷f–FW#â€¢f–æE&W6÷W&6R‚$FVfVÇD—FV×5†ÖÄFF&÷f–FW""’À¢&FVfVÇBÖ—FVÒ†ÖÄFF&÷f–FW"&W6÷W&6R"“°¢&WV—&R‚†ÖÅ&÷f–FW"ä—47–æ6‡&öæ÷W2Â$W‡V7FVBFVfVÇBÖ—FVÒ†ÖÄFF&÷f–FW"7–æ6‡&öæ÷W2ÖöFRâ"“°¢&WV—&R€¢†ÖÅ&÷f–FW"å…F‚ÓÒ"öFVfVÇD—FV×2ö—FVÒ"À¢$W‡V7FVBFVfVÇBÖ—FVÒ†ÖÄFF&÷f–FW"…F‚â"“°¢&WV—&R€¢FVfVÇD—FV×5†ÖÅ&÷f–FW%FW‡BåFW‡BÓÒ$FVfVÇB—FVÒ„ÔÂ&÷f–FW"FW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ†ÖÄFF&÷f–FW"&÷VæBFW‡Bâ"“°¢f"†ÖÅ&÷f–FW$&–æF–ærÒFVfVÇD—FV×5†ÖÅ&÷f–FW%FW‡BävWD&–æF–ætW‡&W76–öâ…FW‡D&Æö6²åFW‡E&÷W'G’¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡V7FVBFVfVÇBÖ—FVÒ†ÖÄFF&÷f–FW"FW‡B&–æF–ærâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2‡†ÖÅ&÷f–FW$&–æF–ærå&VçD&–æF–ærå6÷W&6RÂ†ÖÅ&÷f–FW"’À¢$W‡V7FVBFVfVÇBÖ—FVÒ†ÖÄFF&÷f–FW"&–æF–ær6÷W&6Râ"“°¢&WV—&R€¢†ÖÅ&÷f–FW$&–æF–ærå&VçD&–æF–ærå…F‚ÓÒ$æÖR"À¢$W‡V7FVBFVfVÇBÖ—FVÒ†ÖÄFF&÷f–FW"&–æF–ær…F‚â"“° ¢&WV—&R€¢&VfW&Væ6TWVÇ2„FF6öçFW‡BÂf–WtÖöFVÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒFF6öçFW‡BFòW6RF†Rf–WrÖöFVÂâ"“°¢&WV—&R€¢FVfVÇD—FV×4&÷VæE7FGW5FW‡BåFW‡BÓÒ$FVfVÇB—FVÒ&–æF–ær&VG’"À¢$W‡V7FVBFVfVÇBÖ—FVÒFW‡D&Æö6²&–æF–ærFò&VBF†Rf–WrÖÖöFVÂ7FGW2â"“°¢&WV—&R€¢FVfVÇD—FV×5F&vWEWFFVE7FGW5FW‡BåFW‡BÓÒ$FVfVÇB—FVÒ&–æF–ær&VG’"À¢$W‡V7FVBFVfVÇBÖ—FVÒF&vWEWFFVB&–æF–ærFò&VBF†Rf–WrÖÖöFVÂ7FGW2â"“°¢&WV—&R€¢&–æF–æuF&vWEWFFVD6÷VçBãÒ¢bbÆ7D&–æF–æuF&vWEWFFVDæÖRÓÒæÖVöb„FVfVÇD—FV×5F&vWEWFFVE7FGW5FW‡B¢bbÆ7D&–æF–æuF&vWEWFFVE&÷W'G”æÖRÓÒæÖVöb…FW‡D&Æö6²åFW‡B’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–æF–æråF&vWEWFFVBFòf—&Rf÷"–æ—F–ÂF&vWBG&ç6fW"â"“°¢&WV—&R€¢FVfVÇD—FV×4w&–D&÷VæEFW‡BåFW‡BÓÒ$w&–C¢FVfVÇB—FVÒ&–æF–ær&VG’"À¢$W‡V7FVBFVfVÇBÖ—FVÒw&–B6†–ÆB&–æF–ærFò&VBF†Rf–WrÖÖöFVÂ7FGW2â"“°¢&WV—&R€¢FVfVÇD—FV×4VF—F&ÆU7FGW5FW‡D&÷‚åFW‡BÓÒ$FVfVÇB—FVÒ&–æF–ær&VG’"À¢$W‡V7FVBFVfVÇBÖ—FVÒFW‡D&÷‚&–æF–ærFò&VBF†Rf–WrÖÖöFVÂ7FGW2â"“°¢&WV—&R€¢FVfVÇD—FV×56÷W&6UWFFVE7FGW5FW‡D&÷‚åFW‡BÓÒ$FVfVÇB—FVÒ&–æF–ær&VG’"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6÷W&6UWFFVB&–æF–ærFò&VBF†Rf–WrÖÖöFVÂ7FGW2â"“°¢&WV—&R€¢FVfVÇD—FV×5fÆ–FFVE7FGW5FW‡D&÷‚åFW‡BÓÒ$FVfVÇB—FVÒ&–æF–ær&VG’"À¢$W‡V7FVBFVfVÇBÖ—FVÒfÆ–FFVBFW‡D&÷‚&–æF–ærFò&VBF†Rf–WrÖÖöFVÂ7FGW2â"“°¢&WV—&R€¢FVfVÇD—FV×4f÷&ÖGFVE7FGW5FW‡BåFW‡BÓÒ$f÷&ÖGFVC¢FVfVÇB—FVÒ&–æF–ær&VG’"À¢$W‡V7FVBFVfVÇBÖ—FVÒf÷&ÖGFVB&–æF–ærFò&VBF†Rf–WrÖÖöFVÂ7FGW2â"“°¢&WV—&R€¢FVfVÇD—FV×4fÆÆ&6µ7FGW5FW‡BåFW‡BÓÒ$FVfVÇB—FVÒfÆÆ&6²fÇVR"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–æF–ærfÆÆ&6µfÇVRâ"“°¢&WV—&R€¢FVfVÇD—FV×5F&vWDçVÆÅ7FGW5FW‡BåFW‡BÓÒ$FVfVÇB—FVÒF&vWBÖçVÆÂfÇVR"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–æF–ærF&vWDçVÆÅfÇVRâ"“°¢&WV—&R€¢FVfVÇD—FV×5&–÷&—G•7FGW5FW‡BåFW‡BÓÒ%&–÷&—G“¢FVfVÇB—FVÒ&–æF–ær&VG’"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–÷&—G”&–æF–ærfÆÆ&6²Fò&VBF†Rf–WrÖÖöFVÂ7FGW2â"“°¢&WV—&R€¢FVfVÇD—FV×56VÆd&–æF–æuFW‡BåFW‡BÓÒ%6VÆc¢FVfVÇB—FVÒ6VÆb6÷W&6R"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VÆF—fU6÷W&6R6VÆb&–æF–ærâ"“°¢&WV—&R€¢FVfVÇD—FV×4æ6W7F÷$&–æF–æuFW‡BåFW‡BÓÒ$æ6W7F÷#¢FVfVÇB—FVÒæ6W7F÷"6÷W&6R"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VÆF—fU6÷W&6Ræ6W7F÷%G—R&–æF–ærâ"“°¢FVfVÇD—FV×56VÆd&–æF–æuFW‡BåFrÒ$FVfVÇB—FVÒ6VÆbWFFVB#°¢FVfVÇD—FV×4æ6W7F÷$&–æF–æt&÷&FW"åFrÒ$FVfVÇB—FVÒæ6W7F÷"WFFVB#°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×56VÆd&–æF–æuFW‡BåFW‡BÓÒ%6VÆc¢FVfVÇB—FVÒ6VÆbWFFVB"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VÆF—fU6÷W&6R6VÆb&–æF–ær&Vg&W6‚â"“°¢&WV—&R€¢FVfVÇD—FV×4æ6W7F÷$&–æF–æuFW‡BåFW‡BÓÒ$æ6W7F÷#¢FVfVÇB—FVÒæ6W7F÷"WFFVB"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&VÆF—fU6÷W&6Ræ6W7F÷%G—R&–æF–ær&Vg&W6‚â"“°¢&WV—&R€¢7G&–æräWVÇ2„FVfVÇD—FV×5G&–vvW&VE7FGW5FW‡BåFr27G&–ærÂ&FVfVÇBÖ—FVÒG&–vvW"–æ7F—fR"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒFFG&–vvW"Fò7F'B–æ7F—fRâ"“°¢&WV—&R€¢7G&–æräWVÇ2€¢FVfVÇD—FV×5&÷W'G•G&–vvW&VEFW‡BåFr27G&–ærÀ¢&FVfVÇBÖ—FVÒ&÷W'G’G&–vvW"–æ7F—fR"À¢7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&÷W'G’G&–vvW"Fò7F'B–æ7F—fRâ"“°¢&WV—&R€¢FVfVÇD—FV×4&6VDöåFW‡BåFW‡BÓÒ$FVfVÇB—FVÒ&6VBÖöâFW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&6VDöâ7G–ÆRFW&—fVB6WGFW"â"“°¢&WV—&R€¢7G&–æräWVÇ2€¢FVfVÇD—FV×4&6VDöåFW‡BåFr27G&–ærÀ¢&FVfVÇBÖ—FVÒ&6VDöâ&6R6WGFW""À¢7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&6VDöâ7G–ÆR&6R6WGFW"â"“°¢f"&6VDöäf÷&Vw&÷VæBÒ&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ€¢FVfVÇD—FV×4&6VDöåFW‡Bäf÷&Vw&÷VæBÀ¢&FVfVÇBÖ—FVÒ&6VDöâ7G–ÆRf÷&Vw&÷VæB"“°¢&WV—&R€¢&6VDöäf÷&Vw&÷VæBä6öÆ÷"ÓÒ6öÆ÷"äg&öÕ&v"ƒƒ#"ÂƒSRÂƒsr’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&6VDöâ7G–ÆR–æ†W&—FVB''W6‚â"“°¢FVfVÇD—FV×5FV×ÆFVD'WGFöâäÇ•FV×ÆFR‚“°¢f"FV×ÆFVD'WGFöå&ö÷BÒ&WV—&UG—SÄ&÷&FW#â€¢FVfVÇD—FV×5FV×ÆFVD'WGFöâåFV×ÆFRäf–æDæÖR€¢$FVfVÇD—FV×5FV×ÆFT'WGFöå&ö÷B"À¢FVfVÇD—FV×5FV×ÆFVD'WGFöâ’À¢&FVfVÇBÖ—FVÒ6öçG&öÅFV×ÆFR&ö÷B"“°¢f"FV×ÆFVD'WGFöä6öçFVçBÒ&WV—&UG—SÄ6öçFVçE&W6VçFW#â€¢FVfVÇD—FV×5FV×ÆFVD'WGFöâåFV×ÆFRäf–æDæÖR€¢$FVfVÇD—FV×5FV×ÆFT'WGFöä6öçFVçB"À¢FVfVÇD—FV×5FV×ÆFVD'WGFöâ’À¢&FVfVÇBÖ—FVÒ6öçG&öÅFV×ÆFR6öçFVçB&W6VçFW""“°¢f"FV×ÆFVD'WGFöä&6¶w&÷VæBÒ&WV—&UG—SÅ6öÆ–D6öÆ÷$''W6ƒâ€¢FV×ÆFVD'WGFöå&ö÷Bä&6¶w&÷VæBÀ¢&FVfVÇBÖ—FVÒ6öçG&öÅFV×ÆFRFV×ÆFT&–æF–ær&6¶w&÷VæB"“°¢&WV—&R€¢FV×ÆFVD'WGFöä&6¶w&÷VæBä6öÆ÷"ÓÒ6öÆ÷"äg&öÕ&v"ƒ„S‚Â„cÂ„db’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçG&öÅFV×ÆFRFV×ÆFT&–æF–ær&6¶w&÷VæBâ"“°¢&WV—&R€¢7G&–æräWVÇ2‡FV×ÆFVD'WGFöä6öçFVçBä6öçFVçB27G&–ærÂ$FVfVÇB—FVÒFV×ÆFVB'WGFöâ"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçG&öÅFV×ÆFRFV×ÆFT&–æF–ær6öçFVçBâ"“°¢&WV—&R€¢7G&–æräWVÇ2‡FV×ÆFVD'WGFöå&ö÷BåFr27G&–ærÂ&FVfVÇBÖ—FVÒFV×ÆFRVæ&ÆVB"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçG&öÅFV×ÆFRG&–vvW"Fò7F'B–æ7F—fRâ"“°¢&WV—&R€¢FVfVÇD—FV×46öçfW'FVE6VÆV7F–öåFW‡BåFW‡BÓÒ%6VÆV7FVC¢FVfVÇB—FVÒÇ†"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçfW'FW"&–æF–ærFò&VBF†R6VÆV7FVB—FVÒâ"“°¢&WV—&R€¢FVfVÇD—FV×56VÆV7FVEFV×ÆFT6öçG&öÂä6öçFVçEFV×ÆFU6VÆV7F÷"—2FVfVÇD—FV×4æÖUFV×ÆFU6VÆV7F÷"À¢$W‡V7FVBFVfVÇBÖ—FVÒFFFV×ÆFU6VÆV7F÷"ÖWFFFâ"“°¢FVfVÇD—FV×56VÆV7FVEFV×ÆFT6öçG&öÂäÇ•FV×ÆFR‚“°¢FVfVÇD—FV×56VÆV7FVEFV×ÆFT6öçG&öÂåWFFTÆ–÷WB‚“°¢V×F—7F6†W%VçF–Â€¢‚’Óâf–æEf—7VÄFW66VæFçD'”æÖSÅFW‡D&Æö6³â€¢FVfVÇD—FV×56VÆV7FVEFV×ÆFT6öçG&öÂÀ¢$FVfVÇD—FV×56VÆV7FVDÇ†FV×ÆFUFW‡B"’—2æ÷BçVÆÂÀ¢&FVfVÇBÖ—FVÒFFFV×ÆFU6VÆV7F÷"Ç†f—7VÂG&VR"“°¢f"6VÆV7FVDÇ†FV×ÆFUFW‡BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢f–æEf—7VÄFW66VæFçD'”æÖSÅFW‡D&Æö6³â€¢FVfVÇD—FV×56VÆV7FVEFV×ÆFT6öçG&öÂÀ¢$FVfVÇD—FV×56VÆV7FVDÇ†FV×ÆFUFW‡B"’À¢&FVfVÇBÖ—FVÒFFFV×ÆFU6VÆV7F÷"Ç†FW‡B"“°¢&WV—&R€¢6VÆV7FVDÇ†FV×ÆFUFW‡BåFW‡BÓÒ%6VÆV7FVBÇ†¢FVfVÇB—FVÒÇ†"À¢$W‡V7FVBFVfVÇBÖ—FVÒFFFV×ÆFU6VÆV7F÷"Ç†FV×ÆFR&–æF–ærâ"“°¢&WV—&R€¢FVfVÇD—FV×4×VÇF”&–æF–æuFW‡BåFW‡BÓÒ$6ö×÷6—FS¢FVfVÇB—FVÒ&–æF–ær&VG’òFVfVÇB—FVÒÇ†"À¢$W‡V7FVBFVfVÇBÖ—FVÒ×VÇF”&–æF–ær6öçfW'FW"Fò&VB7FGW2æB6VÆV7F–öââ"“°¢&WV—&R€¢FVfVÇD—FV×4VF—F&ÆU7FGW5FW‡D&÷‚ävWD&–æF–ætW‡&W76–öâ…FW‡D&÷‚åFW‡E&÷W'G’’—2æ÷BçVÆÂÀ¢$W‡V7FVBFVfVÇBÖ—FVÒFW‡D&÷‚Gvò×v’&–æF–ærW‡&W76–öââ"“°¢&WV—&R€¢FVfVÇD—FV×5fÆ–FFVE7FGW5FW‡D&÷‚ävWD&–æF–ætW‡&W76–öâ…FW‡D&÷‚åFW‡E&÷W'G’’—2æ÷BçVÆÂÀ¢$W‡V7FVBFVfVÇBÖ—FVÒfÆ–FFVBFW‡D&÷‚&–æF–ærW‡&W76–öââ"“°¢f"6÷W&6UWFFVD&–æF–ærÒ&WV—&UG—SÄ&–æF–ætW‡&W76–öãâ€¢FVfVÇD—FV×56÷W&6UWFFVE7FGW5FW‡D&÷‚ävWD&–æF–ætW‡&W76–öâ…FW‡D&÷‚åFW‡E&÷W'G’’À¢&FVfVÇBÖ—FVÒ6÷W&6UWFFVBFW‡D&÷‚&–æF–ærW‡&W76–öâ"“°¢–çBF&vWEWFFVD6÷VçD&Vf÷&U7FGW46†ævRÒ&–æF–æuF&vWEWFFVD6÷VçC°¢f–WtÖöFVÂå7FGW2Ò$FVfVÇB—FVÒ&–æF–ærWFFVB#°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×4&÷VæE7FGW5FW‡BåFW‡BÓÒ$FVfVÇB—FVÒ&–æF–ærWFFVB"À¢$W‡V7FVBFVfVÇBÖ—FVÒFW‡D&Æö6²&–æF–ærFòö'6W'fR”æ÷F–g•&÷W'G”6†ævVBâ"“°¢&WV—&R€¢FVfVÇD—FV×5F&vWEWFFVE7FGW5FW‡BåFW‡BÓÒ$FVfVÇB—FVÒ&–æF–ærWFFVB ¢bb&–æF–æuF&vWEWFFVD6÷VçBâF&vWEWFFVD6÷VçD&Vf÷&U7FGW46†ævRÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ&–æF–æråF&vWEWFFVBFòf—&RgFW"6÷W&6Ræ÷F–f–6F–öââ"“°¢&WV—&R€¢FVfVÇD—FV×4w&–D&÷VæEFW‡BåFW‡BÓÒ$w&–C¢FVfVÇB—FVÒ&–æF–ærWFFVB"À¢$W‡V7FVBFVfVÇBÖ—FVÒw&–B6†–ÆB&–æF–ærFòö'6W'fR”æ÷F–g•&÷W'G”6†ævVBâ"“°¢&WV—&R€¢FVfVÇD—FV×4w&÷W&÷…FW‡BåFW‡BÓÒ$w&÷W¢FVfVÇB—FVÒ&–æF–ærWFFVB"À¢$W‡V7FVBFVfVÇBÖ—FVÒw&÷W&÷‚6öçFVçB&–æF–ærFòö'6W'fR”æ÷F–g•&÷W'G”6†ævVBâ"“°¢&WV—&R€¢FVfVÇD—FV×4VF—F&ÆU7FGW5FW‡D&÷‚åFW‡BÓÒ$FVfVÇB—FVÒ&–æF–ærWFFVB"À¢$W‡V7FVBFVfVÇBÖ—FVÒFW‡D&÷‚&–æF–ærFòö'6W'fR”æ÷F–g•&÷W'G”6†ævVBâ"“°¢&WV—&R€¢FVfVÇD—FV×5fÆ–FFVE7FGW5FW‡D&÷‚åFW‡BÓÒ$FVfVÇB—FVÒ&–æF–ærWFFVB"À¢$W‡V7FVBFVfVÇBÖ—FVÒfÆ–FFVBFW‡D&÷‚&–æF–ærFòö'6W'fR”æ÷F–g•&÷W'G”6†ævVBâ"“°¢&WV—&R€¢FVfVÇD—FV×4f÷&ÖGFVE7FGW5FW‡BåFW‡BÓÒ$f÷&ÖGFVC¢FVfVÇB—FVÒ&–æF–ærWFFVB"À¢$W‡V7FVBFVfVÇBÖ—FVÒf÷&ÖGFVB&–æF–ærFòö'6W'fR”æ÷F–g•&÷W'G”6†ævVBâ"“°¢&WV—&R€¢FVfVÇD—FV×5&–÷&—G•7FGW5FW‡BåFW‡BÓÒ%&–÷&—G“¢FVfVÇB—FVÒ&–æF–ærWFFVB"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–÷&—G”&–æF–ærfÆÆ&6²Fòö'6W'fR”æ÷F–g•&÷W'G”6†ævVBâ"“°¢&WV—&R€¢FVfVÇD—FV×4FWF–Ç5F%FW‡BåFW‡BÓÒ%F#¢FVfVÇB—FVÒ&–æF–ærWFFVB"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6VÆV7FVBF$—FVÒ&–æF–ærFòö'6W'fR”æ÷F–g•&÷W'G”6†ævVBâ"“°¢&WV—&R€¢FVfVÇD—FV×57FGW4&%FW‡BåFW‡BÓÒ%7FGW3¢FVfVÇB—FVÒ&–æF–ærWFFVB"À¢$W‡V7FVBFVfVÇBÖ—FVÒ7FGW4&"&–æF–ærFòö'6W'fR”æ÷F–g•&÷W'G”6†ævVBâ"“°¢f–WtÖöFVÂä÷F–öæÅ7FGW2Ò$FVfVÇB—FVÒ÷F–öæÂ7FGW2#°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×5F&vWDçVÆÅ7FGW5FW‡BåFW‡BÓÒ$FVfVÇB—FVÒ÷F–öæÂ7FGW2"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–æF–ærF&vWDçVÆÅfÇVRFò6ÆV"gFW"6÷W&6RWFFRâ"“°¢&WV—&R€¢7G&–æräWVÇ2„FVfVÇD—FV×5G&–vvW&VE7FGW5FW‡BåFr27G&–ærÂ&FVfVÇBÖ—FVÒG&–vvW"–æ7F—fR"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒFFG&–vvW"Fò&VÖ–â–æ7F—fRf÷"æöâÖÖF6†–ær7FGW2â"“°¢&WV—&R€¢FVfVÇD—FV×4×VÇF”&–æF–æuFW‡BåFW‡BÓÒ$6ö×÷6—FS¢FVfVÇB—FVÒ&–æF–ærWFFVBòFVfVÇB—FVÒÇ†"À¢$W‡V7FVBFVfVÇBÖ—FVÒ×VÇF”&–æF–ær6öçfW'FW"Fòö'6W'fR7FGW26†ævW2â"“°¢–çB6÷W&6UWFFVD6÷VçD&Vf÷&UFW‡D&÷…WFFRÒ&–æF–æu6÷W&6UWFFVD6÷VçC°¢FVfVÇD—FV×56÷W&6UWFFVE7FGW5FW‡D&÷‚åFW‡BÒ$FVfVÇB—FVÒ6÷W&6R×WFFVB6÷W&6R#°¢6÷W&6UWFFVD&–æF–æråWFFU6÷W&6R‚“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢f–WtÖöFVÂå7FGW2ÓÒ$FVfVÇB—FVÒ6÷W&6R×WFFVB6÷W&6R ¢bb&–æF–æu6÷W&6UWFFVD6÷VçBâ6÷W&6UWFFVD6÷VçD&Vf÷&UFW‡D&÷…WFFP¢bbÆ7D&–æF–æu6÷W&6UWFFVDæÖRÓÒæÖVöb„FVfVÇD—FV×56÷W&6UWFFVE7FGW5FW‡D&÷‚¢bbÆ7D&–æF–æu6÷W&6UWFFVE&÷W'G”æÖRÓÒæÖVöb…FW‡D&÷‚åFW‡B’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–æF–ærå6÷W&6UWFFVBFòf—&RgFW"W‡Æ–6—B6÷W&6RG&ç6fW"â"“°¢FVfVÇD—FV×4VF—F&ÆU7FGW5FW‡D&÷‚åFW‡BÒ$FVfVÇB—FVÒFW‡B&÷‚6÷W&6R#°¢FVfVÇD—FV×4VF—F&ÆU7FGW5FW‡D&÷‚ävWD&–æF–ætW‡&W76–öâ…FW‡D&÷‚åFW‡E&÷W'G’“òåWFFU6÷W&6R‚“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢f–WtÖöFVÂå7FGW2ÓÒ$FVfVÇB—FVÒFW‡B&÷‚6÷W&6R"À¢$W‡V7FVBFVfVÇBÖ—FVÒFW‡D&÷‚Gvò×v’&–æF–ærFòWFFRF†Rf–WrÖöFVÂâ"“°¢&WV—&R€¢FVfVÇD—FV×4&÷VæE7FGW5FW‡BåFW‡BÓÒ$FVfVÇB—FVÒFW‡B&÷‚6÷W&6R"À¢$W‡V7FVBFVfVÇBÖ—FVÒFW‡D&÷‚6÷W&6RWFFRFò&Vg&W6‚6–&Æ–ær&–æF–ærâ"“°¢&WV—&R€¢FVfVÇD—FV×4f÷&ÖGFVE7FGW5FW‡BåFW‡BÓÒ$f÷&ÖGFVC¢FVfVÇB—FVÒFW‡B&÷‚6÷W&6R"À¢$W‡V7FVBFVfVÇBÖ—FVÒFW‡D&÷‚6÷W&6RWFFRFò&Vg&W6‚f÷&ÖGFVB&–æF–ærâ"“°¢&WV—&R€¢FVfVÇD—FV×5&–÷&—G•7FGW5FW‡BåFW‡BÓÒ%&–÷&—G“¢FVfVÇB—FVÒFW‡B&÷‚6÷W&6R"À¢$W‡V7FVBFVfVÇBÖ—FVÒFW‡D&÷‚6÷W&6RWFFRFò&Vg&W6‚&–÷&—G”&–æF–ærâ"“°¢&WV—&R€¢FVfVÇD—FV×5fÆ–FFVE7FGW5FW‡D&÷‚åFW‡BÓÒ$FVfVÇB—FVÒFW‡B&÷‚6÷W&6R"À¢$W‡V7FVBFVfVÇBÖ—FVÒFW‡D&÷‚6÷W&6RWFFRFò&Vg&W6‚fÆ–FFVB&–æF–ærâ"“°¢&WV—&R€¢FVfVÇD—FV×4×VÇF”&–æF–æuFW‡BåFW‡BÓÒ$6ö×÷6—FS¢FVfVÇB—FVÒFW‡B&÷‚6÷W&6RòFVfVÇB—FVÒÇ†"À¢$W‡V7FVBFVfVÇBÖ—FVÒFW‡D&÷‚6÷W&6RWFFRFò&Vg&W6‚×VÇF”&–æF–ærâ"“°¢&WV—&R€¢FVfVÇD—FV×4Æ—7D&÷‚ä—FV×2ä6÷VçBÓÒ"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öâ&–æF–ær—FVÒ6÷VçBâ"“°¢f"Æ—7D—FV×5æVÅFV×ÆFRÒ&WV—&UG—SÄ—FV×5æVÅFV×ÆFSâ€¢FVfVÇD—FV×4Æ—7D&÷‚ä—FV×5æVÂÀ¢&FVfVÇBÖ—FVÒÆ—7D&÷‚—FV×5æVÅFV×ÆFR"“°¢f"Æ—7D—FV×5æVÅ&ö÷BÒ&WV—&UG—SÅ7F6µæVÃâ€¢Æ—7D—FV×5æVÅFV×ÆFRäÆöD6öçFVçB‚’À¢&FVfVÇBÖ—FVÒÆ—7D&÷‚—FV×5æVÅFV×ÆFR&ö÷B"“°¢&WV—&R€¢Æ—7D—FV×5æVÅ&ö÷Bä÷&–VçFF–öâÓÒ÷&–VçFF–öâä†÷&—¦öçFÂÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ—FV×5æVÅFV×ÆFRÖWFFFâ"“°¢&WV—&R€¢FVfVÇD—FV×4Æ—7D&÷‚ä—FVÔ6öçF–æW%7G–ÆR—2æ÷BçVÆÂÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ—FVÔ6öçF–æW%7G–ÆRÖWFFFâ"“°¢FVfVÇD—FV×4Æ—7D&÷‚äÇ•FV×ÆFR‚“°¢FVfVÇD—FV×4Æ—7D&÷‚åWFFTÆ–÷WB‚“°¢V×F—7F6†W%VçF–Â€¢‚’ÓâFVfVÇD—FV×4Æ—7D&÷‚ä—FVÔ6öçF–æW$vVæW&F÷"ä6öçF–æW$g&öÔ–æFW‚ƒ’—2Æ—7D&÷„—FVÒÀ¢&FVfVÇBÖ—FVÒvVæW&FVBÆ—7D&÷„—FVÒ6öçF–æW""“°¢f"Æ—7D&÷„—FVÒÒ&WV—&UG—SÄÆ—7D&÷„—FVÓâ€¢FVfVÇD—FV×4Æ—7D&÷‚ä—FVÔ6öçF–æW$vVæW&F÷"ä6öçF–æW$g&öÔ–æFW‚ƒ’À¢&FVfVÇBÖ—FVÒvVæW&FVBÆ—7D&÷„—FVÒ6öçF–æW""“°¢&WV—&R€¢Æ—7D&÷„—FVÒåFF–ærÓÒæWrF†–6¶æW72ƒ2’À¢$W‡V7FVBFVfVÇBÖ—FVÒ—FVÔ6öçF–æW%7G–ÆRFF–ærâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2†Æ—7D&÷„—FVÒä6öçFVçBÂf–WtÖöFVÂä—FV×5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒvVæW&FVBÆ—7D&÷„—FVÒ6öçFVçBâ"“°¢f"6÷'FVD—FV×2Ò&WV—&UG—SÄ6öÆÆV7F–öåf–Wu6÷W&6Sâ€¢f–æE&W6÷W&6R‚$FVfVÇD—FV×56÷'FVD—FV×2"’À¢&FVfVÇBÖ—FVÒ6÷'FVB6öÆÆV7F–öåf–Wu6÷W&6R"“°¢&WV—&R€¢6÷'FVD—FV×2å6÷'DFW67&—F–öç2ä6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6R6÷'B6÷VçBâ"“°¢&WV—&R€¢6÷'FVD—FV×2å6÷'DFW67&—F–öç5³Òå&÷W'G”æÖRÓÒ$æÖR"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6R6÷'B&÷W'G’â"“°¢&WV—&R€¢6÷'FVD—FV×2å6÷'DFW67&—F–öç5³ÒäF—&V7F–öâÓÒÆ—7E6÷'DF—&V7F–öâäFW66VæF–ærÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6R6÷'BF—&V7F–öââ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2‡6÷'FVD—FV×2åf–WrÂFVfVÇD—FV×56÷'FVDÆ—7D&÷‚ä—FV×56÷W&6R’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6÷'FVBÆ—7D&÷‚FòW6R6öÆÆV7F–öåf–Wu6÷W&6Rf–Wrâ"“°¢&WV—&R€¢FVfVÇD—FV×56÷'FVDÆ—7D&÷‚ä—57–æ6‡&öæ—¦VEv—F„7W'&VçD—FVÒÓÒG'VRÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6÷'FVBÆ—7D&÷‚7W'&VçBÖ—FVÒ7–æ6‡&öæ—¦F–öââ"“°¢f"6÷'FVEf–Wt—FV×2Ò6÷'FVD—FV×2åf–Wrä67CÆö&¦V7Câ‚’åFô'&’‚“°¢&WV—&R€¢6÷'FVEf–Wt—FV×2äÆVæwF‚ÓÒ"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6R6÷'FVB—FVÒ6÷VçBâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2‡6÷'FVEf–Wt—FV×5³ÒÂf–WtÖöFVÂä—FV×5³Ò¢bb&VfW&Væ6TWVÇ2‡6÷'FVEf–Wt—FV×5³ÒÂf–WtÖöFVÂä—FV×5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6R6÷'FVB÷&FW"â"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×56÷'FVDÆ—7D&÷‚ä—FV×5³ÒÂf–WtÖöFVÂä—FV×5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6÷'FVBÆ—7D&÷‚f—'7B—FVÒâ"“°¢FVfVÇD—FV×56÷'FVDÆ—7D&÷‚å6VÆV7FVD–æFW‚Ò°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢&VfW&Væ6TWVÇ2‡6÷'FVD—FV×2åf–Wrä7W'&VçD—FVÒÂf–WtÖöFVÂä—FV×5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6R7W'&VçB—FVÒg&öÒ6VÆV7F–öââ"“°¢&WV—&R€¢6÷'FVD—FV×2åf–WräÖ÷fT7W'&VçEFõ÷6—F–öâƒ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6R7W'&VçBÖ÷fR&W7VÇBâ"“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×56÷'FVDÆ—7D&÷‚å6VÆV7FVD—FVÒÂf–WtÖöFVÂä—FV×5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6÷'FVBÆ—7D&÷‚6VÆV7F–öâFòföÆÆ÷r7W'&VçB—FVÒâ"“°¢f"Æ—7Ef–Wtw&–BÒ&WV—&UG—SÄw&–Ef–Wsâ€¢FVfVÇD—FV×4Æ—7Ef–Wråf–WrÀ¢&FVfVÇBÖ—FVÒÆ—7Ef–Wrw&–Ef–Wr"“°¢&WV—&R€¢Æ—7Ef–Wtw&–Bä6öÇVÖç2ä6÷VçBÓÒ"À¢$W‡V7FVBFVfVÇBÖ—FVÒw&–Ef–Wr6öÇVÖâ6÷VçBâ"“°¢f"æÖT6öÇVÖä&–æF–ærÒ&WV—&UG—SÄ&–æF–æsâ€¢Æ—7Ef–Wtw&–Bä6öÇVÖç5³ÒäF—7Æ”ÖVÖ&W$&–æF–ærÀ¢&FVfVÇBÖ—FVÒw&–Ef–WræÖR6öÇVÖâ&–æF–ær"“°¢f"¶–æD6öÇVÖä&–æF–ærÒ&WV—&UG—SÄ&–æF–æsâ€¢Æ—7Ef–Wtw&–Bä6öÇVÖç5³ÒäF—7Æ”ÖVÖ&W$&–æF–ærÀ¢&FVfVÇBÖ—FVÒw&–Ef–Wr¶–æB6öÇVÖâ&–æF–ær"“°¢&WV—&R€¢WVÇ2†Æ—7Ef–Wtw&–Bä6öÇVÖç5³Òä†VFW"Â$æÖR"¢bbæÖT6öÇVÖä&–æF–æråF‚åF‚ÓÒ$æÖR"À¢$W‡V7FVBFVfVÇBÖ—FVÒw&–Ef–WræÖR6öÇVÖâ&–æF–ærâ"“°¢&WV—&R€¢WVÇ2†Æ—7Ef–Wtw&–Bä6öÇVÖç5³Òä†VFW"Â$¶–æB"¢bb¶–æD6öÇVÖä&–æF–æråF‚åF‚ÓÒ$¶–æB"À¢$W‡V7FVBFVfVÇBÖ—FVÒw&–Ef–Wr¶–æB6öÇVÖâ&–æF–ærâ"“°¢&WV—&R€¢FVfVÇD—FV×4Æ—7Ef–Wrä—FV×2ä6÷VçBÓÒ"À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ—7Ef–Wr—FVÒ6÷VçBâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×4Æ—7Ef–Wrå6VÆV7FVD—FVÒÂf–WtÖöFVÂä—FV×5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ—7Ef–Wr6VÆV7FVB—FVÒâ"“°¢&WV—&R€¢FVfVÇD—FV×4FFw&–BäWFôvVæW&FT6öÇVÖç2À¢$W‡V7FVBFVfVÇBÖ—FVÒFFw&–BW‡Æ–6—B6öÇVÖç2â"“°¢&WV—&R€¢FVfVÇD—FV×4FFw&–Bä6öÇVÖç2ä6÷VçBÓÒ2À¢$W‡V7FVBFVfVÇBÖ—FVÒFFw&–B6öÇVÖâ6÷VçBâ"“°¢f"FFw&–DæÖT6öÇVÖâÒ&WV—&UG—SÄFFw&–EFW‡D6öÇVÖãâ€¢FVfVÇD—FV×4FFw&–Bä6öÇVÖç5³ÒÀ¢&FVfVÇBÖ—FVÒFFw&–BæÖR6öÇVÖâ"“°¢f"FFw&–D¶–æD6öÇVÖâÒ&WV—&UG—SÄFFw&–EFW‡D6öÇVÖãâ€¢FVfVÇD—FV×4FFw&–Bä6öÇVÖç5³ÒÀ¢&FVfVÇBÖ—FVÒFFw&–B¶–æB6öÇVÖâ"“°¢f"FFw&–D7F—fT6öÇVÖâÒ&WV—&UG—SÄFFw&–D6†V6´&÷„6öÇVÖãâ€¢FVfVÇD—FV×4FFw&–Bä6öÇVÖç5³%ÒÀ¢&FVfVÇBÖ—FVÒFFw&–B7F—fR6öÇVÖâ"“°¢f"FFw&–DæÖT&–æF–ærÒ&WV—&UG—SÄ&–æF–æsâ€¢FFw&–DæÖT6öÇVÖâä&–æF–ærÀ¢&FVfVÇBÖ—FVÒFFw&–BæÖR&–æF–ær"“°¢f"FFw&–D¶–æD&–æF–ærÒ&WV—&UG—SÄ&–æF–æsâ€¢FFw&–D¶–æD6öÇVÖâä&–æF–ærÀ¢&FVfVÇBÖ—FVÒFFw&–B¶–æB&–æF–ær"“°¢f"FFw&–D7F—fT&–æF–ærÒ&WV—&UG—SÄ&–æF–æsâ€¢FFw&–D7F—fT6öÇVÖâä&–æF–ærÀ¢&FVfVÇBÖ—FVÒFFw&–B7F—fR&–æF–ær"“°¢&WV—&R€¢WVÇ2†FFw&–DæÖT6öÇVÖâä†VFW"Â$æÖR"¢bbFFw&–DæÖT&–æF–æråF‚åF‚ÓÒ$æÖR"À¢$W‡V7FVBFVfVÇBÖ—FVÒFFw&–BæÖR6öÇVÖâ&–æF–ærâ"“°¢&WV—&R€¢WVÇ2†FFw&–D¶–æD6öÇVÖâä†VFW"Â$¶–æB"¢bbFFw&–D¶–æD&–æF–æråF‚åF‚ÓÒ$¶–æB"À¢$W‡V7FVBFVfVÇBÖ—FVÒFFw&–B¶–æB6öÇVÖâ&–æF–ærâ"“°¢&WV—&R€¢WVÇ2†FFw&–D7F—fT6öÇVÖâä†VFW"Â$7F—fR"¢bbFFw&–D7F—fT&–æF–æråF‚åF‚ÓÒ$—47F—fR"À¢$W‡V7FVBFVfVÇBÖ—FVÒFFw&–B7F—fR6öÇVÖâ&–æF–ærâ"“°¢&WV—&R€¢FVfVÇD—FV×4FFw&–Bä—FV×2ä6÷VçBÓÒ"À¢$W‡V7FVBFVfVÇBÖ—FVÒFFw&–B—FVÒ6÷VçBâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×4FFw&–Bå6VÆV7FVD—FVÒÂf–WtÖöFVÂä—FV×5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒFFw&–B6VÆV7FVB—FVÒâ"“°¢fÆ–FFTFVfVÇD—FV×4Æ&vTFFw&–B‚“°¢f"w&÷WVD—FV×2Ò&WV—&UG—SÄ6öÆÆV7F–öåf–Wu6÷W&6Sâ€¢f–æE&W6÷W&6R‚$FVfVÇD—FV×4w&÷WVD—FV×2"’À¢&FVfVÇBÖ—FVÒw&÷WVB6öÆÆV7F–öåf–Wu6÷W&6R"“°¢&WV—&R€¢w&÷WVD—FV×2äw&÷WFW67&—F–öç2ä6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6Rw&÷W6÷VçBâ"“°¢f"w&÷WFW67&—F–öâÒ&WV—&UG—SÅ&÷W'G”w&÷WFW67&—F–öãâ€¢w&÷WVD—FV×2äw&÷WFW67&—F–öç5³ÒÀ¢&FVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6Rw&÷WFW67&—F–öâ"“°¢&WV—&R€¢w&÷WFW67&—F–öâå&÷W'G”æÖRÓÒ$¶–æB"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6Rw&÷W&÷W'G’â"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2†w&÷WVD—FV×2åf–WrÂFVfVÇD—FV×4w&÷WVDÆ—7D&÷‚ä—FV×56÷W&6R’À¢$W‡V7FVBFVfVÇBÖ—FVÒw&÷WVBÆ—7D&÷‚FòW6R6öÆÆV7F–öåf–Wu6÷W&6Rf–Wrâ"“°¢&WV—&R€¢FVfVÇD—FV×4w&÷WVDÆ—7D&÷‚äw&÷W7G–ÆRä6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒw&÷WVBÆ—7D&÷‚w&÷W7G–ÆR6÷VçBâ"“°¢f"w&÷W†VFW%FV×ÆFRÒ&WV—&UG—SÄFFFV×ÆFSâ€¢f–æE&W6÷W&6R‚$FVfVÇD—FV×4w&÷W†VFW%FV×ÆFR"’À¢&FVfVÇBÖ—FVÒw&÷W7G–ÆR†VFW"FV×ÆFR"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2†w&÷W†VFW%FV×ÆFRÂFVfVÇD—FV×4w&÷WVDÆ—7D&÷‚äw&÷W7G–ÆU³Òä†VFW%FV×ÆFR’À¢$W‡V7FVBFVfVÇBÖ—FVÒw&÷WVBÆ—7D&÷‚†VFW"FV×ÆFRâ"“°¢f"w&÷W2Òw&÷WVD—FV×2åf–Wräw&÷W0¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6Rw&÷W2â"“°¢&WV—&R€¢w&÷W2ä6÷VçBÓÒ"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6R–æ—F–Âw&÷W6÷VçBâ"“°¢f"g&ÖWv÷&´w&÷WÒ&WV—&UG—SÄ6öÆÆV7F–öåf–Wtw&÷Wâ€¢w&÷W2ä67CÆö&¦V7Câ‚’äf—'7B†w&÷WÓâWVÇ2‚‚„6öÆÆV7F–öåf–Wtw&÷W–w&÷W’äæÖRÂ$g&ÖWv÷&²"’’À¢&FVfVÇBÖ—FVÒg&ÖWv÷&²w&÷W"“°¢f"FFw&÷WÒ&WV—&UG—SÄ6öÆÆV7F–öåf–Wtw&÷Wâ€¢w&÷W2ä67CÆö&¦V7Câ‚’äf—'7B†w&÷WÓâWVÇ2‚‚„6öÆÆV7F–öåf–Wtw&÷W–w&÷W’äæÖRÂ$FF"’’À¢&FVfVÇBÖ—FVÒFFw&÷W"“°¢&WV—&R€¢g&ÖWv÷&´w&÷Wä—FVÔ6÷VçBÓÒbbFFw&÷Wä—FVÔ6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6R–æ—F–Âw&÷W—FVÒ6÷VçG2â"“°¢f"w&÷W†VFW%&ö÷BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢w&÷W†VFW%FV×ÆFRäÆöD6öçFVçB‚’À¢&FVfVÇBÖ—FVÒw&÷W7G–ÆR†VFW"&ö÷B"“°¢w&÷W†VFW%&ö÷BäFF6öçFW‡BÒg&ÖWv÷&´w&÷W°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢w&÷W†VFW%&ö÷BåFW‡BÓÒ$FVfVÇBw&÷W¢g&ÖWv÷&²"À¢$W‡V7FVBFVfVÇBÖ—FVÒw&÷W7G–ÆR†VFW"&–æF–ærâ"“°¢f"6ö×÷6—FT—FV×2Ò&WV—&UG—SÄ6ö×÷6—FT6öÆÆV7F–öãâ€¢FVfVÇD—FV×46ö×÷6—FTÆ—7D&÷‚ä—FV×56÷W&6RÀ¢&FVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ6÷W&6R"“°¢&WV—&R€¢6ö×÷6—FT—FV×2ä6÷VçBÓÒ2À¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ6÷W&6R'B6÷VçBâ"“°¢&WV—&R€¢WVÇ2†6ö×÷6—FT—FV×5³ÒÂ$FVfVÇB—FVÒ6ö×÷6—FR†VFW""’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ7FF–2—FVÒâ"“°¢f"6ö×÷6—FT6öçF–æW"Ò&WV—&UG—SÄ6öÆÆV7F–öä6öçF–æW#â€¢6ö×÷6—FT—FV×5³ÒÀ¢&FVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ6öçF–æW""“°¢&WV—&R€¢&VfW&Væ6TWVÇ2†6ö×÷6—FT6öçF–æW"ä6öÆÆV7F–öâÂFVfVÇD—FV×46ö×÷6—FU&÷f–FW"ä—FV×2’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ7FF–26÷W&6R—FV×2â"“°¢f"6ö×÷6—FT–æÆ–æT—FVÒÒ&WV—&UG—SÄÆ—7D&÷„—FVÓâ€¢6ö×÷6—FT—FV×5³%ÒÀ¢&FVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ–æÆ–æR—FVÒ"“°¢&WV—&R€¢WVÇ2†6ö×÷6—FT–æÆ–æT—FVÒä6öçFVçBÂ$FVfVÇB—FVÒ6ö×÷6—FR–æÆ–æR6öçF–æW""’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ–æÆ–æR—FVÒ6öçFVçBâ"“°¢FVfVÇD—FV×46ö×÷6—FTÆ—7D&÷‚åWFFTÆ–÷WB‚“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×46ö×÷6—FTÆ—7D&÷‚ä—FV×2ä6÷VçBÓÒBÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ–æ—F–ÂfÆGFVæVB—FVÒ6÷VçBâ"“°¢&WV—&R€¢WVÇ2„FVfVÇD—FV×46ö×÷6—FTÆ—7D&÷‚ä—FV×5³ÒÂ$FVfVÇB—FVÒ6ö×÷6—FR†VFW""’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ–æ—F–Â7FF–2—FVÒâ"“°¢&WV—&R€¢WVÇ2„FVfVÇD—FV×46ö×÷6—FTÆ—7D&÷‚ä—FV×5³ÒÂFVfVÇD—FV×46ö×÷6—FU&÷f–FW"ä—FV×5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâf—'7B6÷W&6R—FVÒâ"“°¢&WV—&R€¢WVÇ2„FVfVÇD—FV×46ö×÷6—FTÆ—7D&÷‚ä—FV×5³%ÒÂFVfVÇD—FV×46ö×÷6—FU&÷f–FW"ä—FV×5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ6V6öæB6÷W&6R—FVÒâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×46ö×÷6—FTÆ—7D&÷‚ä—FV×5³5ÒÂ6ö×÷6—FT–æÆ–æT—FVÒ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ–æ—F–Â–æÆ–æR—FVÒâ"“°¢FVfVÇD—FV×46ö×÷6—FU&÷f–FW"ä—FV×2äFB‚$FVfVÇB—FVÒ6ö×÷6—FRvÖÖ"“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×46ö×÷6—FTÆ—7D&÷‚ä—FV×2ä6÷VçBÓÒRÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ6öÆÆV7F–öâÖ6†ævRfÆGFVæVB—FVÒ6÷VçBâ"“°¢&WV—&R€¢WVÇ2„FVfVÇD—FV×46ö×÷6—FTÆ—7D&÷‚ä—FV×5³5ÒÂFVfVÇD—FV×46ö×÷6—FU&÷f–FW"ä—FV×5³%Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ6öÆÆV7F–öâÖ6†ævRVæFVB6÷W&6R—FVÒâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×46ö×÷6—FTÆ—7D&÷‚ä—FV×5³EÒÂ6ö×÷6—FT–æÆ–æT—FVÒ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6ö×÷6—FT6öÆÆV7F–öâ6öÆÆV7F–öâÖ6†ævR–æÆ–æR—FVÒâ"“°¢f"æöFUFV×ÆFRÒ&WV—&UG—SÄ†–W&&6†–6ÄFFFV×ÆFSâ€¢f–æE&W6÷W&6R‚$FVfVÇD—FV×4æöFUFV×ÆFR"’À¢&FVfVÇBÖ—FVÒ†–W&&6†–6ÄFFFV×ÆFR"“°¢f"æöFT—FV×56÷W&6RÒ&WV—&UG—SÄ&–æF–æsâ€¢æöFUFV×ÆFRä—FV×56÷W&6RÀ¢&FVfVÇBÖ—FVÒ†–W&&6†–6ÄFFFV×ÆFR—FV×56÷W&6R&–æF–ær"“°¢&WV—&R€¢æöFT—FV×56÷W&6RåF‚åF‚ÓÒ$6†–ÆG&Vâ"À¢$W‡V7FVBFVfVÇBÖ—FVÒ†–W&&6†–6ÄFFFV×ÆFR—FV×56÷W&6RF‚â"“°¢f"æöFUFV×ÆFU&ö÷BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢æöFUFV×ÆFRäÆöD6öçFVçB‚’À¢&FVfVÇBÖ—FVÒ†–W&&6†–6ÄFFFV×ÆFR&ö÷B"“°¢æöFUFV×ÆFU&ö÷BäFF6öçFW‡BÒf–WtÖöFVÂäæöFW5³Ó°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢æöFUFV×ÆFU&ö÷BåFW‡BÓÒ$æöFS¢FVfVÇB—FVÒ&ö÷B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ†–W&&6†–6ÄFFFV×ÆFR&–æF–ærFW‡Bâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×5G&VUf–Wrä—FVÕFV×ÆFRÂæöFUFV×ÆFR’À¢$W‡V7FVBFVfVÇBÖ—FVÒG&VUf–Wr—FVÒFV×ÆFRâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×5G&VUf–Wrä—FV×56÷W&6RÂf–WtÖöFVÂäæöFW2’À¢$W‡V7FVBFVfVÇBÖ—FVÒG&VUf–Wr—FV×56÷W&6Râ"“°¢&WV—&R€¢FVfVÇD—FV×5G&VUf–Wrä—FV×2ä6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒG&VUf–Wr&ö÷B—FVÒ6÷VçBâ"“°¢FVfVÇD—FV×5G&VUf–WräÇ•FV×ÆFR‚“°¢FVfVÇD—FV×5G&VUf–WråWFFTÆ–÷WB‚“°¢V×F—7F6†W%VçF–Â€¢‚’ÓâFVfVÇD—FV×5G&VUf–Wrä—FVÔ6öçF–æW$vVæW&F÷"ä6öçF–æW$g&öÔ—FVÒ…f–WtÖöFVÂäæöFW5³Ò’—2G&VUf–Wt—FVÒÀ¢&FVfVÇBÖ—FVÒvVæW&FVBG&VUf–Wr&ö÷B6öçF–æW""“°¢f"&ö÷EG&VT—FVÒÒ&WV—&UG—SÅG&VUf–Wt—FVÓâ€¢FVfVÇD—FV×5G&VUf–Wrä—FVÔ6öçF–æW$vVæW&F÷"ä6öçF–æW$g&öÔ—FVÒ…f–WtÖöFVÂäæöFW5³Ò’À¢&FVfVÇBÖ—FVÒvVæW&FVBG&VUf–Wr&ö÷B6öçF–æW""“°¢&WV—&R€¢&VfW&Væ6TWVÇ2‡&ö÷EG&VT—FVÒä†VFW"Âf–WtÖöFVÂäæöFW5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒG&VUf–Wr&ö÷B†VFW"â"“°¢&ö÷EG&VT—FVÒä—4W‡æFVBÒG'VS°¢&ö÷EG&VT—FVÒåWFFTÆ–÷WB‚“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢&ö÷EG&VT—FVÒä—FV×2ä6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒG&VUf–Wr6†–ÆB—FVÒ6÷VçBâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2‡&ö÷EG&VT—FVÒä—FV×5³ÒÂf–WtÖöFVÂäæöFW5³Òä6†–ÆG&Vå³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒG&VUf–Wr6†–ÆB—FVÒâ"“°¢&WV—&R€¢FVfVÇD—FV×46öÖ&ô&÷‚ä—FV×2ä6÷VçBÓÒ"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6VÆV7F÷"&–æF–ær—FVÒ6÷VçBâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×46öÖ&ô&÷‚å6VÆV7FVD—FVÒÂf–WtÖöFVÂå6VÆV7FVD—FVÒ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6VÆV7F÷"&–æF–ær–æ—F–Â6VÆV7FVB—FVÒâ"“°¢&WV—&R€¢f–WtÖöFVÂå6VÆV7FVD—FVÒäæÖRÓÒ$FVfVÇB—FVÒÇ†"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6VÆV7F÷"&–æF–ær–æ—F–Âf–WrÖÖöFVÂ—FVÒâ"“°¢f–WtÖöFVÂä—FV×2äFB€¢æWrFVfVÇD—FV×4—FVĞ¢°¢æÖRÒ$FVfVÇB—FVÒvÖÖ"À¢¶–æBÒ$g&ÖWv÷&²"À¢—47F—fRÒG'VRÀ¢Ò“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢FVfVÇD—FV×4Æ—7D&÷‚ä—FV×2ä6÷VçBÓÒ2À¢$W‡V7FVBFVfVÇBÖ—FVÒö'6W'f&ÆT6öÆÆV7F–öâWFFRâ"“°¢&WV—&R€¢FVfVÇD—FV×46öÖ&ô&÷‚ä—FV×2ä6÷VçBÓÒ2À¢$W‡V7FVBFVfVÇBÖ—FVÒ6VÆV7F÷"6öÆÆV7F–öâWFFRâ"“°¢f"VæFVD—FVÒÒ&WV—&UG—SÄFVfVÇD—FV×4—FVÓâ€¢FVfVÇD—FV×4Æ—7D&÷‚ä—FV×5³%ÒÀ¢&FVfVÇBÖ—FVÒVæFVB6öÆÆV7F–öâ—FVÒ"“°¢&WV—&R€¢VæFVD—FVÒäæÖRÓÒ$FVfVÇB—FVÒvÖÖ"À¢$W‡V7FVBFVfVÇBÖ—FVÒVæFVB6öÆÆV7F–öâ—FVÒæÖRâ"“°¢f"&Vg&W6†VE6÷'FVEf–Wt—FV×2Ò6÷'FVD—FV×2åf–Wrä67CÆö&¦V7Câ‚’åFô'&’‚“°¢&WV—&R€¢&Vg&W6†VE6÷'FVEf–Wt—FV×2äÆVæwF‚ÓÒ2À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6R6öÆÆV7F–öâWFFR6÷VçBâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2‡&Vg&W6†VE6÷'FVEf–Wt—FV×5³ÒÂVæFVD—FVÒ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6R6öÆÆV7F–öâWFFR6÷'FVB—FVÒâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×56÷'FVDÆ—7D&÷‚ä—FV×5³ÒÂVæFVD—FVÒ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6÷'FVBÆ—7D&÷‚6öÆÆV7F–öâWFFRâ"“°¢&WV—&R€¢FVfVÇD—FV×4Æ—7Ef–Wrä—FV×2ä6÷VçBÓÒ2À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ—7Ef–Wr6öÆÆV7F–öâWFFRâ"“°¢&WV—&R€¢FVfVÇD—FV×4FFw&–Bä—FV×2ä6÷VçBÓÒ2À¢$W‡V7FVBFVfVÇBÖ—FVÒFFw&–B6öÆÆV7F–öâWFFRâ"“°¢w&÷W2Òw&÷WVD—FV×2åf–Wräw&÷W0¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6Rw&÷W2gFW"WFFRâ"“°¢g&ÖWv÷&´w&÷WÒ&WV—&UG—SÄ6öÆÆV7F–öåf–Wtw&÷Wâ€¢w&÷W2ä67CÆö&¦V7Câ‚’äf—'7B†w&÷WÓâWVÇ2‚‚„6öÆÆV7F–öåf–Wtw&÷W–w&÷W’äæÖRÂ$g&ÖWv÷&²"’’À¢&FVfVÇBÖ—FVÒg&ÖWv÷&²w&÷WgFW"WFFR"“°¢&WV—&R€¢g&ÖWv÷&´w&÷Wä—FVÔ6÷VçBÓÒ"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÆÆV7F–öåf–Wu6÷W&6R6öÆÆV7F–öâÖ6†ævRw&÷W—FVÒ6÷VçBâ"“°¢f–WtÖöFVÂå6VÆV7FVD—FVÒÒf–WtÖöFVÂä—FV×5³Ó°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×46öÖ&ô&÷‚å6VÆV7FVD—FVÒÂf–WtÖöFVÂä—FV×5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6VÆV7F÷"&–æF–ærFòö'6W'fRf–WrÖÖöFVÂ6VÆV7F–öââ"“°¢&WV—&R€¢FVfVÇD—FV×46öçfW'FVE6VÆV7F–öåFW‡BåFW‡BÓÒ%6VÆV7FVC¢FVfVÇB—FVÒ&WF"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçfW'FW"&–æF–ærFòö'6W'fRf–WrÖÖöFVÂ6VÆV7F–öââ"“°¢FVfVÇD—FV×56VÆV7FVEFV×ÆFT6öçG&öÂåWFFTÆ–÷WB‚“°¢V×F—7F6†W%VçF–Â€¢‚’Óâf–æEf—7VÄFW66VæFçD'”æÖSÅFW‡D&Æö6³â€¢FVfVÇD—FV×56VÆV7FVEFV×ÆFT6öçG&öÂÀ¢$FVfVÇD—FV×56VÆV7FVD&WFFV×ÆFUFW‡B"’—2æ÷BçVÆÂÀ¢&FVfVÇBÖ—FVÒFFFV×ÆFU6VÆV7F÷"&WFf—7VÂG&VR"“°¢f"6VÆV7FVD&WFFV×ÆFUFW‡BÒ&WV—&UG—SÅFW‡D&Æö6³â€¢f–æEf—7VÄFW66VæFçD'”æÖSÅFW‡D&Æö6³â€¢FVfVÇD—FV×56VÆV7FVEFV×ÆFT6öçG&öÂÀ¢$FVfVÇD—FV×56VÆV7FVD&WFFV×ÆFUFW‡B"’À¢&FVfVÇBÖ—FVÒFFFV×ÆFU6VÆV7F÷"&WFFW‡B"“°¢&WV—&R€¢6VÆV7FVD&WFFV×ÆFUFW‡BåFW‡BÓÒ%6VÆV7FVB&WF¢FVfVÇB—FVÒ&WF"À¢$W‡V7FVBFVfVÇBÖ—FVÒFFFV×ÆFU6VÆV7F÷"&WFFV×ÆFR&–æF–ærâ"“°¢&WV—&R€¢FVfVÇD—FV×4×VÇF”&–æF–æuFW‡BåFW‡BÓÒ$6ö×÷6—FS¢FVfVÇB—FVÒFW‡B&÷‚6÷W&6RòFVfVÇB—FVÒ&WF"À¢$W‡V7FVBFVfVÇBÖ—FVÒ×VÇF”&–æF–ær6öçfW'FW"Fòö'6W'fRf–WrÖÖöFVÂ6VÆV7F–öââ"“°¢FVfVÇD—FV×46öÖ&ô&÷‚å6VÆV7FVD—FVÒÒf–WtÖöFVÂä—FV×5³%Ó°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢&VfW&Væ6TWVÇ2…f–WtÖöFVÂå6VÆV7FVD—FVÒÂf–WtÖöFVÂä—FV×5³%Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6VÆV7F÷"Gvò×v’&–æF–ærFòWFFRF†Rf–WrÖöFVÂâ"“°¢&WV—&R€¢FVfVÇD—FV×46öçfW'FVE6VÆV7F–öåFW‡BåFW‡BÓÒ%6VÆV7FVC¢FVfVÇB—FVÒvÖÖ"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçfW'FW"&–æF–ærFòö'6W'fR6öçG&öÂ6VÆV7F–öââ"“°¢&WV—&R€¢FVfVÇD—FV×4×VÇF”&–æF–æuFW‡BåFW‡BÓÒ$6ö×÷6—FS¢FVfVÇB—FVÒFW‡B&÷‚6÷W&6RòFVfVÇB—FVÒvÖÖ"À¢$W‡V7FVBFVfVÇBÖ—FVÒ×VÇF”&–æF–ær6öçfW'FW"Fòö'6W'fR6öçG&öÂ6VÆV7F–öââ"“° ¢f"fÆ–FFVE7FGW4&–æF–ærÒ&WV—&UG—SÄ&–æF–ætW‡&W76–öãâ€¢FVfVÇD—FV×5fÆ–FFVE7FGW5FW‡D&÷‚ävWD&–æF–ætW‡&W76–öâ…FW‡D&÷‚åFW‡E&÷W'G’’À¢&FVfVÇBÖ—FVÒfÆ–FFVBFW‡D&÷‚&–æF–ærW‡&W76–öâ"“°¢FVfVÇD—FV×5fÆ–FFVE7FGW5FW‡D&÷‚åFW‡BÒ7G&–æräV×G“°¢fÆ–FFVE7FGW4&–æF–æråWFFU6÷W&6R‚“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢fÆ–FF–öâävWD†4W'&÷"„FVfVÇD—FV×5fÆ–FFVE7FGW5FW‡D&÷‚’À¢$W‡V7FVBFVfVÇBÖ—FVÒfÆ–FF–öâ'VÆRFò&V¦V7BV×G’FW‡Bâ"“°¢&WV—&R€¢f–WtÖöFVÂå7FGW2ÓÒ$FVfVÇB—FVÒFW‡B&÷‚6÷W&6R"À¢$W‡V7FVBFVfVÇBÖ—FVÒfÆ–FF–öâf–ÇW&RFò&W6W'fRF†R6÷W&6RfÇVRâ"“°¢FVfVÇD—FV×5fÆ–FFVE7FGW5FW‡D&÷‚åFW‡BÒ$FVfVÇB—FVÒfÆ–FFVB6÷W&6R#°¢fÆ–FFVE7FGW4&–æF–æråWFFU6÷W&6R‚“°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢fÆ–FF–öâävWD†4W'&÷"„FVfVÇD—FV×5fÆ–FFVE7FGW5FW‡D&÷‚’À¢$W‡V7FVBFVfVÇBÖ—FVÒfÆ–FF–öâ'VÆRFò66WBæöâÖV×G’FW‡Bâ"“°¢&WV—&R€¢f–WtÖöFVÂå7FGW2ÓÒ$FVfVÇB—FVÒfÆ–FFVB6÷W&6R"À¢$W‡V7FVBFVfVÇBÖ—FVÒfÆ–FF–öâ7V66W72FòWFFRF†R6÷W&6RfÇVRâ"“°¢&WV—&R€¢FVfVÇD—FV×4&÷VæE7FGW5FW‡BåFW‡BÓÒ$FVfVÇB—FVÒfÆ–FFVB6÷W&6R"À¢$W‡V7FVBFVfVÇBÖ—FVÒfÆ–FF–öâ7V66W72Fò&Vg&W6‚6–&Æ–ær&–æF–ærâ"“°¢&WV—&R€¢7G&–æräWVÇ2„FVfVÇD—FV×5G&–vvW&VE7FGW5FW‡BåFr27G&–ærÂ&FVfVÇBÖ—FVÒG&–vvW"7F—fR"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒFFG&–vvW"Fò7F—fFRgFW"fÆ–FFVB6÷W&6RWFFRâ"“°¢f–WtÖöFVÂå7FGW2Ò$FVfVÇB—FVÒG&–vvW"&W6WB#°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢7G&–æräWVÇ2„FVfVÇD—FV×5G&–vvW&VE7FGW5FW‡BåFr27G&–ærÂ&FVfVÇBÖ—FVÒG&–vvW"–æ7F—fR"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒFFG&–vvW"FòW†—BgFW"6÷W&6R6†ævRâ"“°¢FVfVÇD—FV×5&÷W'G•G&–vvW&VEFW‡Bä—4Væ&ÆVBÒfÇ6S°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢7G&–æräWVÇ2€¢FVfVÇD—FV×5&÷W'G•G&–vvW&VEFW‡BåFr27G&–ærÀ¢&FVfVÇBÖ—FVÒ&÷W'G’G&–vvW"7F—fR"À¢7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&÷W'G’G&–vvW"Fò7F—fFRgFW"&÷W'G’6†ævRâ"“°¢FVfVÇD—FV×5&÷W'G•G&–vvW&VEFW‡Bä—4Væ&ÆVBÒG'VS°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢7G&–æräWVÇ2€¢FVfVÇD—FV×5&÷W'G•G&–vvW&VEFW‡BåFr27G&–ærÀ¢&FVfVÇBÖ—FVÒ&÷W'G’G&–vvW"–æ7F—fR"À¢7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&÷W'G’G&–vvW"FòW†—BgFW"&÷W'G’&W6WBâ"“°¢FVfVÇD—FV×5FV×ÆFVD'WGFöâä—4Væ&ÆVBÒfÇ6S°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢7G&–æräWVÇ2‡FV×ÆFVD'WGFöå&ö÷BåFr27G&–ærÂ&FVfVÇBÖ—FVÒFV×ÆFRF—6&ÆVB"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçG&öÅFV×ÆFRG&–vvW"Fò7F—fFRgFW"&÷W'G’6†ævRâ"“°¢FVfVÇD—FV×5FV×ÆFVD'WGFöâä—4Væ&ÆVBÒG'VS°¢G&–äF—7F6†W"‚“°¢&WV—&R€¢7G&–æräWVÇ2‡FV×ÆFVD'WGFöå&ö÷BåFr27G&–ærÂ&FVfVÇBÖ—FVÒFV×ÆFRVæ&ÆVB"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öçG&öÅFV×ÆFRG&–vvW"FòW†—BgFW"&÷W'G’&W6WBâ"“° ¢f"6öÖÖæD&–æF–ærÒ&WV—&UG—SÄ6öÖÖæD&–æF–æsâ€¢6öÖÖæD&–æF–æw5³ÒÀ¢&FVfVÇBÖ—FVÒ6öÖÖæB&–æF–ær"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2†6öÖÖæD&–æF–ærä6öÖÖæBÂFVfVÇD—FV×46öÖÖæB’À¢$W‡V7FVBFVfVÇBÖ—FVÒv–æF÷rä6öÖÖæD&–æF–æw26öÖÖæBâ"“°¢f"¶W”&–æF–ærÒ&WV—&UG—SÄ¶W”&–æF–æsâ€¢–çWD&–æF–æw5³ÒÀ¢&FVfVÇBÖ—FVÒ¶W’&–æF–ær"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2†¶W”&–æF–ærä6öÖÖæBÂFVfVÇD—FV×46öÖÖæB’À¢$W‡V7FVBFVfVÇBÖ—FVÒ¶W”&–æF–ær6öÖÖæBâ"“°¢&WV—&R€¢7G&–æräWVÇ2†¶W”&–æF–ærä6öÖÖæE&ÖWFW"27G&–ærÂ&–çWBÖ&–æF–ær"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ¶W”&–æF–ær6öÖÖæB&ÖWFW"â"“°¢&WV—&R€¢¶W”&–æF–ærä¶W’ÓÒ¶W’ä"bb¶W”&–æF–æräÖöF–f–W'2ÓÒÖöF–f–W$¶W—2ä6öçG&öÂÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ¶W”&–æF–ærvW7GW&Râ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×46öÖÖæD'WGFöâä6öÖÖæBÂFVfVÇD—FV×46öÖÖæB’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÖÖæB'WGFöâ6öÖÖæBâ"“°¢&WV—&R€¢7G&–æräWVÇ2„FVfVÇD—FV×46öÖÖæD'WGFöâä6öÖÖæE&ÖWFW"27G&–ærÂ&'WGFöâÖ6öÖÖæB"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6öÖÖæB'WGFöâ&ÖWFW"â"“°¢&WV—&R€¢FVfVÇD—FV×46öÖÖæBä6äW†V7WFR‚&'WGFöâÖ6öÖÖæB"ÂF†—2’À¢$W‡V7FVBFVfVÇBÖ—FVÒ&÷WFVB6öÖÖæB6äW†V7WFRâ"“°¢FVfVÇD—FV×46öÖÖæBäW†V7WFR„FVfVÇD—FV×46öÖÖæD'WGFöâä6öÖÖæE&ÖWFW"ÂF†—2“°¢&WV—&R€¢6öÖÖæDW†V7WF–öä6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ&÷WFVB6öÖÖæBW†V7WF–öââ"“°¢&WV—&R€¢Æ7D6öÖÖæE&ÖWFW"ÓÒ&'WGFöâÖ6öÖÖæB"À¢$W‡V7FVBFVfVÇBÖ—FVÒ&÷WFVB6öÖÖæB&ÖWFW"â"“° ¢FVfVÇD—FV×4WfVçE6WGFW$'WGFöâå&—6TWfVçB†æWr&÷WFVDWfVçD&w2„'WGFöâä6Æ–6´WfVçB’“°¢&WV—&R€¢WfVçE6WGFW$6Æ–6´6÷VçBÓÒÀ¢$W‡V7FVBFVfVÇBÖ—FVÒWfVçE6WGFW"&÷WFVB†æFÆW"W†V7WF–öââ"“°¢&WV—&R€¢7G&–æräWVÇ2„FVfVÇD—FV×4WfVçE6WGFW$'WGFöâåFr27G&–ærÂ&FVfVÇBÖ—FVÒWfVçB6WGFW"6Æ–6¶VB"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒWfVçE6WGFW"†æFÆW"7FFRâ"“° ¢FVfVÇD—FV×4'WGFöâå&—6TWfVçB†æWr&÷WFVDWfVçD&w2„'WGFöâä6Æ–6´WfVçB’“°¢&WV—&R„'WGFöä6Æ–6´6÷VçBÓÒÂ$W‡V7FVBFVfVÇBÖ—FVÒ6ö×–ÆVB6Æ–6²†æFÆW"â"“°¢&WV—&R€¢7G&–æräWVÇ2„FVfVÇD—FV×4'WGFöâåFr27G&–ærÂ&FVfVÇBÖ—FVÒ6Æ–6¶VB"Â7G&–æt6ö×&—6öâä÷&F–æÂ’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ–6²†æFÆW"FòWFFR'WGFöâ7FFRâ"“°¢Ğ ¢&—fFRfö–BfÆ–FFTFVfVÇD—FV×4Æ&vTFFw&–B‚¢°¢&WV—&R€¢FVfVÇD—FV×4Æ&vTFFw&–BäWFôvVæW&FT6öÇVÖç2À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–BW‡Æ–6—B6öÇVÖç2â"“°¢&WV—&R€¢FVfVÇD—FV×4Æ&vTFFw&–BäVæ&ÆU&÷uf—'GVÆ—¦F–öà¢bbFVfVÇD—FV×4Æ&vTFFw&–BäVæ&ÆT6öÇVÖåf—'GVÆ—¦F–öà¢bbf—'GVÆ—¦–æuæVÂävWD—5f—'GVÆ—¦–ær„FVfVÇD—FV×4Æ&vTFFw&–B¢bbf—'GVÆ—¦–æuæVÂävWEf—'GVÆ—¦F–öäÖöFR„FVfVÇD—FV×4Æ&vTFFw&–B’ÓÒf—'GVÆ—¦F–öäÖöFRå&V7–6Æ–æp¢bbf—'GVÆ—¦–æuæVÂävWE67&öÆÅVæ—B„FVfVÇD—FV×4Æ&vTFFw&–B’ÓÒ67&öÆÅVæ—Bå—†VÀ¢bb67&öÆÅf–WvW"ävWD6ä6öçFVçE67&öÆÂ„FVfVÇD—FV×4Æ&vTFFw&–B’À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–Bf—'GVÆ—¦F–öâÖWFFFâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×4Æ&vTFFw&–Bä—FV×56÷W&6RÂf–WtÖöFVÂäÆ&vT—FV×2’À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–B—FVÒ6÷W&6Râ"“°¢&WV—&R€¢FVfVÇD—FV×4Æ&vTFFw&–Bä—FV×2ä6÷VçBÓÒóÀ¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–B—FVÒ6÷VçBâ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×4Æ&vTFFw&–Bå6VÆV7FVD—FVÒÂf–WtÖöFVÂäÆ&vT—FV×5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–B6VÆV7FVB—FVÒâ"“°¢&WV—&R€¢FVfVÇD—FV×4Æ&vTFFw&–Bä6öÇVÖç2ä6÷VçBÓÒBÀ¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–B6öÇVÖâ6÷VçBâ"“°¢f"–æFW„6öÇVÖâÒ&WV—&UG—SÄFFw&–EFW‡D6öÇVÖãâ€¢FVfVÇD—FV×4Æ&vTFFw&–Bä6öÇVÖç5³ÒÀ¢&FVfVÇBÖ—FVÒÆ&vRFFw&–B–æFW‚6öÇVÖâ"“°¢f"æÖT6öÇVÖâÒ&WV—&UG—SÄFFw&–EFW‡D6öÇVÖãâ€¢FVfVÇD—FV×4Æ&vTFFw&–Bä6öÇVÖç5³ÒÀ¢&FVfVÇBÖ—FVÒÆ&vRFFw&–BæÖR6öÇVÖâ"“°¢f"¶–æD6öÇVÖâÒ&WV—&UG—SÄFFw&–EFW‡D6öÇVÖãâ€¢FVfVÇD—FV×4Æ&vTFFw&–Bä6öÇVÖç5³%ÒÀ¢&FVfVÇBÖ—FVÒÆ&vRFFw&–B¶–æB6öÇVÖâ"“°¢f"7F—fT6öÇVÖâÒ&WV—&UG—SÄFFw&–D6†V6´&÷„6öÇVÖãâ€¢FVfVÇD—FV×4Æ&vTFFw&–Bä6öÇVÖç5³5ÒÀ¢&FVfVÇBÖ—FVÒÆ&vRFFw&–B7F—fR6öÇVÖâ"“°¢f"–æFW„&–æF–ærÒ&WV—&UG—SÄ&–æF–æsâ€¢–æFW„6öÇVÖâä&–æF–ærÀ¢&FVfVÇBÖ—FVÒÆ&vRFFw&–B–æFW‚&–æF–ær"“°¢f"æÖT&–æF–ærÒ&WV—&UG—SÄ&–æF–æsâ€¢æÖT6öÇVÖâä&–æF–ærÀ¢&FVfVÇBÖ—FVÒÆ&vRFFw&–BæÖR&–æF–ær"“°¢f"¶–æD&–æF–ærÒ&WV—&UG—SÄ&–æF–æsâ€¢¶–æD6öÇVÖâä&–æF–ærÀ¢&FVfVÇBÖ—FVÒÆ&vRFFw&–B¶–æB&–æF–ær"“°¢f"7F—fT&–æF–ærÒ&WV—&UG—SÄ&–æF–æsâ€¢7F—fT6öÇVÖâä&–æF–ærÀ¢&FVfVÇBÖ—FVÒÆ&vRFFw&–B7F—fR&–æF–ær"“°¢&WV—&R€¢WVÇ2†–æFW„6öÇVÖâä†VFW"Â$–æFW‚"¢bb–æFW„&–æF–æråF‚åF‚ÓÒ$–æFW‚"À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–B–æFW‚6öÇVÖâ&–æF–ærâ"“°¢&WV—&R€¢WVÇ2†æÖT6öÇVÖâä†VFW"Â$æÖR"¢bbæÖT&–æF–æråF‚åF‚ÓÒ$æÖR"À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–BæÖR6öÇVÖâ&–æF–ærâ"“°¢&WV—&R€¢WVÇ2†¶–æD6öÇVÖâä†VFW"Â$¶–æB"¢bb¶–æD&–æF–æråF‚åF‚ÓÒ$¶–æB"À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–B¶–æB6öÇVÖâ&–æF–ærâ"“°¢&WV—&R€¢WVÇ2†7F—fT6öÇVÖâä†VFW"Â$7F—fR"¢bb7F—fT&–æF–æråF‚åF‚ÓÒ$—47F—fR"À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–B7F—fR6öÇVÖâ&–æF–ærâ"“° ¢FVfVÇD—FV×4Æ&vTFFw&–BäÇ•FV×ÆFR‚“°¢FVfVÇD—FV×4Æ&vTFFw&–BåWFFTÆ–÷WB‚“°¢G&–äF—7F6†W"‚“°¢f"67&öÆÅf–WvW"Ò&WV—&UG—SÅ67&öÆÅf–WvW#â€¢f–æEf—7VÄFW66VæFçCÅ67&öÆÅf–WvW#â„FVfVÇD—FV×4Æ&vTFFw&–B’À¢&FVfVÇBÖ—FVÒÆ&vRFFw&–B67&öÆÅf–WvW""“°¢&WV—&R€¢67&öÆÅf–WvW"å67&öÆÆ&ÆT†V–v‡BâÀ¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–B67&öÆÆ&ÆR†V–v‡Bâ"“°¢fÆ–FFTFVfVÇD—FV×4Æ&vTFFw&–E&VÆ—¦VE&÷w2‚&–æ—F–Â"“° ¢FVfVÇD—FV×4Æ&vTFFw&–Bå67&öÆÄ–çFõf–Wr…f–WtÖöFVÂäÆ&vT—FV×5µf–WtÖöFVÂäÆ&vT—FV×2ä6÷VçBÒÒ“°¢G&–äF—7F6†W"‚“°¢FVfVÇD—FV×4Æ&vTFFw&–BåWFFTÆ–÷WB‚“°¢67&öÆÅf–WvW"åWFFTÆ–÷WB‚“°¢&WV—&R€¢67&öÆÅf–WvW"åfW'F–6Äöfg6WBâÀ¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–BfW'F–6Âöfg6WBgFW"Æ&vR67&öÆÂâ"“°¢fÆ–FFTFVfVÇD—FV×4Æ&vTFFw&–E&VÆ—¦VE&÷w2‚&gFW"Æ&vR67&öÆÂ"“°¢&WV—&R€¢&VfW&Væ6TWVÇ2„FVfVÇD—FV×4Æ&vTFFw&–Bå6VÆV7FVD—FVÒÂf–WtÖöFVÂäÆ&vT—FV×5³Ò’À¢$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–B6VÆV7F–öâFò&VÖ–â7F&ÆRgFW"Æ&vR67&öÆÂâ"“° ¢FVfVÇD—FV×4Æ&vTFFw&–Bå67&öÆÄ–çFõf–Wr…f–WtÖöFVÂäÆ&vT—FV×5³Ò“°¢G&–äF—7F6†W"‚“°¢FVfVÇD—FV×4Æ&vTFFw&–BåWFFTÆ–÷WB‚“°¢Ğ ¢&—fFRfö–BfÆ–FFTFVfVÇD—FV×4Æ&vTFFw&–E&VÆ—¦VE&÷w2‡7G&–ær†6R¢°¢–çB&VÆ—¦VE&÷w2Ò6÷VçEf—7VÄFW66VæFçG3ÄFFw&–E&÷sâ„FVfVÇD—FV×4Æ&vTFFw&–B“°¢&WV—&R€¢&VÆ—¦VE&÷w2âÀ¢B$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–B&VÆ—¦VB&÷w2GW&–ær·†6WÒâ"“°¢&WV—&R€¢&VÆ—¦VE&÷w2ÂSÀ¢B$W‡V7FVBFVfVÇBÖ—FVÒÆ&vRFFw&–B&VÆ—¦VB&÷w2Fò7F’f—'GVÆ—¦VBGW&–ær·†6WÒÂf÷VæB·&VÆ—¦VE&÷w7Òâ"“°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×5v–æF÷tÆöFVB†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢ÆöFVD6÷VçB²³°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×46öÖÖæD6äW†V7WFR†ö&¦V7B6VæFW"Â6äW†V7WFU&÷WFVDWfVçD&w2R¢°¢Rä6äW†V7WFRÒG'VS°¢Rä†æFÆVBÒG'VS°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×46öÖÖæDW†V7WFVB†ö&¦V7B6VæFW"ÂW†V7WFVE&÷WFVDWfVçD&w2R¢°¢6öÖÖæDW†V7WF–öä6÷VçB²³°¢Æ7D6öÖÖæE&ÖWFW"ÒRå&ÖWFW"27G&–æróò7G&–æräV×G“°¢Rä†æFÆVBÒG'VS°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×4'WGFöä6Æ–6²†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢'WGFöä6Æ–6´6÷VçB²³°¢FVfVÇD—FV×4'WGFöâåFrÒ&FVfVÇBÖ—FVÒ6Æ–6¶VB#°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×4WfVçE6WGFW$6Æ–6²†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢WfVçE6WGFW$6Æ–6´6÷VçB²³°¢FVfVÇD—FV×4WfVçE6WGFW$'WGFöâåFrÒ&FVfVÇBÖ—FVÒWfVçB6WGFW"6Æ–6¶VB#°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×4W‡æFW$W‡æFVB†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢W‡æFW$W‡æFVD6÷VçB²³°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×4W‡æFW$6öÆÆ6VB†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢W‡æFW$6öÆÆ6VD6÷VçB²³°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×4ÖVçT—FVÔ6Æ–6²†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢ÖVçT6Æ–6´6÷VçB²³°¢FVfVÇD—FV×46Æ–6´ÖVçT—FVÒåFrÒ&FVfVÇBÖ—FVÒÖVçR6Æ–6¶VB#°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×4ÖVçT—FVÔ6†V6¶VB†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢ÖVçT6†V6¶VD6÷VçB²³°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×4ÖVçT—FVÕVæ6†V6¶VB†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢ÖVçUVæ6†V6¶VD6÷VçB²³°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×46öçFW‡DÖVçT—FVÔ6Æ–6²†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢6öçFW‡DÖVçT6Æ–6´6÷VçB²³°¢FVfVÇD—FV×46öçFW‡D6Æ–6´ÖVçT—FVÒåFrÒ&FVfVÇBÖ—FVÒ6öçFW‡BÖVçR6Æ–6¶VB#°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×46öçFW‡DÖVçT—FVÔ6†V6¶VB†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢6öçFW‡DÖVçT6†V6¶VD6÷VçB²³°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×46öçFW‡DÖVçT—FVÕVæ6†V6¶VB†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢6öçFW‡DÖVçUVæ6†V6¶VD6÷VçB²³°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×5&F–ô'WGFöä6†V6¶VB†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢&F–ô'WGFöä6†V6¶VD6÷VçB²³°¢Æ7E&F–ô'WGFöä6†V6¶VDæÖRÒ‡6VæFW"2g&ÖWv÷&´VÆVÖVçB“òäæÖRóò7G&–æräV×G“°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×577v÷&D6†ævVB†ö&¦V7B6VæFW"Â&÷WFVDWfVçD&w2R¢°¢77v÷&D6†ævVD6÷VçB²³°¢Æ7E77v÷&EfÇVRÒFVfVÇD—FV×577v÷&D&÷‚å77v÷&C°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×4&–æF–æuF&vWEWFFVB†ö&¦V7B6VæFW"ÂFFG&ç6fW$WfVçD&w2R¢°¢&–æF–æuF&vWEWFFVD6÷VçB²³°¢Æ7D&–æF–æuF&vWEWFFVDæÖRÒ†RåF&vWDö&¦V7B2g&ÖWv÷&´VÆVÖVçB“òäæÖRóò7G&–æräV×G“°¢Æ7D&–æF–æuF&vWEWFFVE&÷W'G”æÖRÒRå&÷W'G“òäæÖRóò7G&–æräV×G“°¢Ğ ¢&—fFRfö–BöäFVfVÇD—FV×4&–æF–æu6÷W&6UWFFVB†ö&¦V7B6VæFW"ÂFFG&ç6fW$WfVçD&w2R¢°¢&–æF–æu6÷W&6UWFFVD6÷VçB²³°¢Æ7D&–æF–æu6÷W&6UWFFVDæÖRÒ†RåF&vWDö&¦V7B2g&ÖWv÷&´VÆVÖVçB“òäæÖRóò7G&–æräV×G“°¢Æ7D&–æF–æu6÷W&6UWFFVE&÷W'G”æÖRÒRå&÷W'G“òäæÖRóò7G&–æräV×G“°¢Ğ ¢&—fFR7FF–2fö–BG&–äF—7F6†W"‚¢°¢f"g&ÖRÒæWrF—7F6†W$g&ÖR‚“°¢F—7F6†W"ä7W'&VçDF—7F6†W"ä&Vv–ä–çfö¶R€¢F—7F6†W%&–÷&—G’äÆ–6F–öä–FÆRÀ¢æWr7F–öâ‚‚’Óâg&ÖRä6öçF–çVRÒfÇ6R’“°¢F—7F6†W"åW6„g&ÖR†g&ÖR“°¢Ğ ¢&—fFR7FF–2fö–BV×F—7F6†W%VçF–Â„gVæ3Æ&ööÃâ6öæF—F–öâÂ7G&–ærFW67&—F–öâ¢°¢f"FVFÆ–æRÒFFUF–ÖRåWF4æ÷r²F–ÖU7âäg&öÕ6V6öæG2ƒ"“°¢v†–ÆR‚6öæF—F–öâ‚’¢°¢G&–äF—7F6†W"‚“°¢–b†6öæF—F–öâ‚’¢°¢&WGW&ã°¢Ğ ¢–b„FFUF–ÖRåWF4æ÷rãÒFVFÆ–æR¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B%F–ÖVB÷WBv—F–ærf÷"¶FW67&—F–öçÒâ"“°¢Ğ¢Ğ¢Ğ ¢&—fFR7FF–2Còf–æEf—7VÄFW66VæFçD'”æÖSÅCâ„FWVæFVæ7”ö&¦V7B&ö÷BÂ7G&–æræÖR¢v†W&RB¢g&ÖWv÷&´VÆVÖVç@¢°¢–çB6†–ÆD6÷VçBÒf—7VÅG&VT†VÇW"ävWD6†–ÆG&Vä6÷VçB‡&ö÷B“°¢f÷"†–çB’Ò²’Â6†–ÆD6÷VçC²’²²¢°¢FWVæFVæ7”ö&¦V7B6†–ÆBÒf—7VÅG&VT†VÇW"ävWD6†–ÆB‡&ö÷BÂ’“°¢–b†6†–ÆB—2BG—VBbb7G&–æräWVÇ2‡G—VBäæÖRÂæÖRÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢&WGW&âG—VC°¢Ğ ¢CòæW7FVBÒf–æEf—7VÄFW66VæFçD'”æÖSÅCâ†6†–ÆBÂæÖR“°¢–b†æW7FVB—2æ÷BçVÆÂ¢°¢&WGW&âæW7FVC°¢Ğ¢Ğ ¢&WGW&âçVÆÃ°¢Ğ ¢&—fFR7FF–2Còf–æEf—7VÄFW66VæFçCÅCâ„FWVæFVæ7”ö&¦V7B&ö÷B¢v†W&RB¢FWVæFVæ7”ö&¦V7@¢°¢–çB6†–ÆD6÷VçBÒf—7VÅG&VT†VÇW"ävWD6†–ÆG&Vä6÷VçB‡&ö÷B“°¢f÷"†–çB’Ò²’Â6†–ÆD6÷VçC²’²²¢°¢FWVæFVæ7”ö&¦V7B6†–ÆBÒf—7VÅG&VT†VÇW"ävWD6†–ÆB‡&ö÷BÂ’“°¢–b†6†–ÆB—2BG—VB¢°¢&WGW&âG—VC°¢Ğ ¢CòæW7FVBÒf–æEf—7VÄFW66VæFçCÅCâ†6†–ÆB“°¢–b†æW7FVB—2æ÷BçVÆÂ¢°¢&WGW&âæW7FVC°¢Ğ¢Ğ ¢&WGW&âçVÆÃ°¢Ğ ¢&—fFR7FF–2–çB6÷VçEf—7VÄFW66VæFçG3ÅCâ„FWVæFVæ7”ö&¦V7B&ö÷B¢v†W&RB¢FWVæFVæ7”ö&¦V7@¢°¢–çB6÷VçBÒ°¢–çB6†–ÆD6÷VçBÒf—7VÅG&VT†VÇW"ävWD6†–ÆG&Vä6÷VçB‡&ö÷B“°¢f÷"†–çB’Ò²’Â6†–ÆD6÷VçC²’²²¢°¢FWVæFVæ7”ö&¦V7B6†–ÆBÒf—7VÅG&VT†VÇW"ävWD6†–ÆB‡&ö÷BÂ’“°¢–b†6†–ÆB—2B¢°¢6÷VçB²³°¢Ğ ¢6÷VçB³Ò6÷VçEf—7VÄFW66VæFçG3ÅCâ†6†–ÆB“°¢Ğ ¢&WGW&â6÷VçC°¢Ğ ¢&—fFRfö–BfÆ–FFTFVfVÇD—FV×4ÖW76vT&÷‚‚¢°¢G—R6W'f–6UG—RÒG—Vöb„ÖW76vT&÷‚’ä76VÖ&Ç’ävWEG—R€¢%7—7FVÒåv–æF÷w2å÷'F&ÆTÖW76vT&÷…6W'f–6R"À¢F‡&÷töäW'&÷#¢fÇ6R¢óòF‡&÷ræWrG—TÆöDW†6WF–öâ‚%7—7FVÒåv–æF÷w2å÷'F&ÆTÖW76vT&÷…6W'f–6R"“°¢f"—4Væ&ÆVE&÷W'G’Ò6W'f–6UG—RävWE&÷W'G’€¢$—4Væ&ÆVB"À¢&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢óòF‡&÷ræWrÖ—76–ætÖVÖ&W$W†6WF–öâ‡6W'f–6UG—RägVÆÄæÖRÂ$—4Væ&ÆVB"“°¢–b‚÷W&F–æu7—7FVÒä—5v–æF÷w2‚’¢°¢&WV—&R€¢†&ööÂ’†—4Væ&ÆVE&÷W'G’ävWEfÇVR†çVÆÂ’óòfÇ6R’À¢$W‡V7FVBFVfVÇBÖ—FVÒ÷'F&ÆRÖW76vT&÷‚6W'f–6RFò&RVæ&ÆVBâ"“°¢Ğ ¢”F—7÷6&ÆSò&Vv—7G&F–öâÒ&Vv—7FW$FVfVÇD—FV×4FWFW&Ö–æ—7F–4ÖW76vT&÷‚‡6W'f–6UG—R“°¢G'¢°¢f"æô÷væW%&W7VÇBÒÖW76vT&÷‚å6†÷r€¢&FVfVÇBÖ—FVÒ4D²ÖW76vR"À¢&FVfVÇBÖ—FVÒ4D²6F–öâ"À¢ÖW76vT&÷„'WGFöâå–W4æô6æ6VÂÀ¢ÖW76vT&÷„–ÖvRåv&æ–ærÀ¢ÖW76vT&÷…&W7VÇBäæòÀ¢ÖW76vT&÷„÷F–öç2äæöæR“°¢&WV—&R€¢æô÷væW%&W7VÇBÓÒÖW76vT&÷…&W7VÇBäæòÀ¢$W‡V7FVBFVfVÇBÖ—FVÒÖW76vT&÷‚æòÖ÷væW"FVfVÇB&W7VÇBâ"“° ¢f"÷væW%&W7VÇBÒÖW76vT&÷‚å6†÷r€¢F†—2À¢&FVfVÇBÖ—FVÒ4D²÷væW"ÖW76vR"À¢&FVfVÇBÖ—FVÒ4D²÷væW"6F–öâ"À¢ÖW76vT&÷„'WGFöâäô´6æ6VÂÀ¢ÖW76vT&÷„–ÖvRä–æf÷&ÖF–öâÀ¢ÖW76vT&÷…&W7VÇBäæöæRÀ¢ÖW76vT&÷„÷F–öç2äæöæR“°¢&WV—&R€¢÷væW%&W7VÇBÓÒÖW76vT&÷…&W7VÇBäô²À¢$W‡V7FVBFVfVÇBÖ—FVÒÖW76vT&÷‚÷væW"fÆÆ&6²&W7VÇBâ"“°¢Ğ¢f–æÆÇ¢°¢&Vv—7G&F–öãòäF—7÷6R‚“°¢Ğ¢Ğ ¢&—fFR7FF–2”F—7÷6&ÆSò&Vv—7FW$FVfVÇD—FV×4FWFW&Ö–æ—7F–4ÖW76vT&÷‚…G—R6W'f–6UG—R¢°¢f"&Vv—7FW$ÖWF†öBÒ6W'f–6UG—RävWDÖWF†öB€¢%&Vv—7FW""À¢&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2À¢&–æFW#¢çVÆÂÀ¢G—W3¢æWuµÒ²G—Vöb„gVæ3Æö&¦V7BÂö&¦V7Câ’ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡6W'f–6UG—RägVÆÄæÖRÂ%&Vv—7FW""“° ¢&WGW&â&Vv—7FW$ÖWF†öBä–çfö¶R€¢çVÆÂÀ¢æWrö&¦V7EµÒ²„gVæ3Æö&¦V7BÂö&¦V7Câ•6†÷tFVfVÇD—FV×4FWFW&Ö–æ—7F–4ÖW76vT&÷‚Ò’2”F—7÷6&ÆS°¢Ğ ¢&—fFR7FF–2ö&¦V7B6†÷tFVfVÇD—FV×4FWFW&Ö–æ—7F–4ÖW76vT&÷‚†ö&¦V7B&WVW7B¢°¢&WGW&â&VDFVfVÇD—FV×5÷'F&ÆU&WVW7E7G&–ær‡&WVW7BÂ$fÆÆ&6µ&W7VÇB"“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFTFVfVÇD—FV×5&–çDF–Æör‚¢°¢–b„÷W&F–æu7—7FVÒä—5v–æF÷w2‚’¢°¢&WGW&ã°¢Ğ ¢f"&–çDF–ÆörÒæWr&–çDF–Æöp¢°¢W6W%vU&ævTVæ&ÆVBÒG'VRÀ¢6VÆV7FVEvW4Væ&ÆVBÒG'VRÀ¢7W'&VçEvTVæ&ÆVBÒG'VRÀ¢vU&ævU6VÆV7F–öâÒvU&ævU6VÆV7F–öâåW6W%vW2À¢vU&ævRÒæWrvU&ævRƒ’ÂB’À¢Ö–åvRÒ"À¢Ö…vRÒ ¢Ó° ¢&WV—&R‡&–çDF–ÆöråW6W%vU&ævTVæ&ÆVBÂ$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–ÆörW6W"vR&ævRVæ&ÆVBâ"“°¢&WV—&R‡&–çDF–Æörå6VÆV7FVEvW4Væ&ÆVBÂ$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–Æör6VÆV7FVBvW2Væ&ÆVBâ"“°¢&WV—&R‡&–çDF–Æörä7W'&VçEvTVæ&ÆVBÂ$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–Æör7W'&VçBvRVæ&ÆVBâ"“°¢&WV—&R€¢&–çDF–ÆöråvU&ævU6VÆV7F–öâÓÒvU&ævU6VÆV7F–öâåW6W%vW2À¢$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–ÆörvR&ævR6VÆV7F–öââ"“°¢&WV—&R‡&–çDF–ÆöråvU&ævRåvTg&öÒÓÒBÂ$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–Æöræ÷&ÖÆ—¦VBvR&ævR7F'Bâ"“°¢&WV—&R‡&–çDF–ÆöråvU&ævRåvUFòÓÒ’Â$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–Æöræ÷&ÖÆ—¦VBvR&ævRVæBâ"“°¢&WV—&R‡&–çDF–ÆöräÖ–åvRÓÒ'RÂ$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–ÆörÖ–æ–×VÒvRâ"“°¢&WV—&R‡&–çDF–ÆöräÖ…vRÓÒ'RÂ$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–ÆörÖ†–×VÒvRâ"“°¢&WV—&R‡&–çDF–Æörå&–çEVWVR—2çVÆÂÂ$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–Æör÷'F&ÆR&–çBVWVRâ"“°¢&WV—&R‡&–çDF–Æörå&–çEF–6¶WB—2æ÷BçVÆÂÂ$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–Æör÷'F&ÆR&–çBF–6¶WBâ"“°¢&WV—&R€¢ÖF‚ä'2‡&–çDF–Æörå&–çF&ÆT&Vv–GF‚Òƒbã’ÂãÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–Æör÷'F&ÆR&–çF&ÆRv–GF‚â"“°¢&WV—&R€¢ÖF‚ä'2‡&–çDF–Æörå&–çF&ÆT&V†V–v‡BÒSbã’ÂãÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–Æör÷'F&ÆR&–çF&ÆR†V–v‡Bâ"“°¢&WV—&R€¢&–çDF–Æörå6†÷tF–Æör‚’ävWEfÇVT÷$FVfVÇB‡G'VR’ÓÒfÇ6RÀ¢$W‡V7FVBFVfVÇBÖ—FVÒ&–çDF–Æör÷'F&ÆRF–Æör&W7VÇBâ"“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFTFVfVÇD—FV×46Æ—&ö&B‚¢°¢6Æ—&ö&Bä6ÆV"‚“°¢&WV—&R‚6Æ—&ö&Bä6öçF–ç5FW‡B‚’Â$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&B–æ—F–ÂFW‡B7FFRâ"“° ¢6Æ—&ö&Bå6WEFW‡B‚&FVfVÇBÖ—FVÒ4D²6Æ—&ö&BFW‡B"“°¢&WV—&R„6Æ—&ö&Bä6öçF–ç5FW‡B‚’Â$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&BFW‡B7FFRgFW"6WEFW‡Bâ"“°¢&WV—&R€¢6Æ—&ö&BävWEFW‡B‚’ÓÒ&FVfVÇBÖ—FVÒ4D²6Æ—&ö&BFW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&BvWEFW‡Bâ"“°¢f"FW‡DFFö&¦V7BÒ6Æ—&ö&BävWDFFö&¦V7B‚¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&BFFö&¦V7Bâ"“°¢&WV—&R€¢WVÇ2‡FW‡DFFö&¦V7BävWDFF„FFf÷&ÖG2åVæ–6öFUFW‡B’Â&FVfVÇBÖ—FVÒ4D²6Æ—&ö&BFW‡B"’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&BFFö&¦V7BVæ–6öFRFW‡Bâ"“°¢&WV—&R„6Æ—&ö&Bä—47W'&VçB‡FW‡DFFö&¦V7B’Â$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&B7W'&VçBFW‡BFFö&¦V7Bâ"“°¢6Æ—&ö&BäfÇW6‚‚“°¢&WV—&R€¢6Æ—&ö&BävWEFW‡B‚’ÓÒ&FVfVÇBÖ—FVÒ4D²6Æ—&ö&BFW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&BfÇW6†VBFW‡Bâ"“° ¢f"7W7FöÔFFö&¦V7BÒæWrFFö&¦V7B‚“°¢7W7FöÔFFö&¦V7Bå6WDFF€¢FFf÷&ÖG2åVæ–6öFUFW‡BÀ¢&FVfVÇBÖ—FVÒ4D²FFö&¦V7BFW‡B"À¢WFô6öçfW'C¢fÇ6R“°¢7W7FöÔFFö&¦V7Bå6WDFF€¢$FVfVÇD—FV×47W7FöÔf÷&ÖB"À¢&FVfVÇBÖ—FVÒ4D²7W7FöÒ–ÆöB"À¢WFô6öçfW'C¢fÇ6R“°¢6Æ—&ö&Bå6WDFFö&¦V7B†7W7FöÔFFö&¦V7BÂ6÷“¢G'VR“°¢&WV—&R„6Æ—&ö&Bä6öçF–ç5FW‡B‚’Â$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&BFFö&¦V7BFW‡B7FFRâ"“°¢&WV—&R€¢6Æ—&ö&BävWEFW‡B‚’ÓÒ&FVfVÇBÖ—FVÒ4D²FFö&¦V7BFW‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&BFFö&¦V7BFW‡Bâ"“°¢&WV—&R€¢WVÇ2„6Æ—&ö&BävWDFF‚$FVfVÇD—FV×47W7FöÔf÷&ÖB"’Â&FVfVÇBÖ—FVÒ4D²7W7FöÒ–ÆöB"’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&B7W7FöÒFFf÷&ÖBâ"“°¢f"7W'&VçDFFö&¦V7BÒ&WV—&UG—SÄFFö&¦V7Câ€¢6Æ—&ö&BävWDFFö&¦V7B‚’À¢&FVfVÇBÖ—FVÒ6Æ—&ö&B7W'&VçBFFö&¦V7BgFW"6WDFFö&¦V7B"“°¢&WV—&R€¢7W'&VçDFFö&¦V7BävWDFF&W6VçB‚$FVfVÇD—FV×47W7FöÔf÷&ÖB"ÂWFô6öçfW'C¢fÇ6R’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&B7W7FöÒf÷&ÖB&W6VçBâ"“°¢&WV—&R€¢WVÇ2€¢7W'&VçDFFö&¦V7BävWDFF‚$FVfVÇD—FV×47W7FöÔf÷&ÖB"ÂWFô6öçfW'C¢fÇ6R’À¢&FVfVÇBÖ—FVÒ4D²7W7FöÒ–ÆöB"’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&B7W7FöÒFFö&¦V7B–ÆöBâ"“°¢&WV—&R€¢7W'&VçDFFö&¦V7BåG'”vWDFF€¢$FVfVÇD—FV×47W7FöÔf÷&ÖB"À¢WFô6öçfW'C¢fÇ6RÀ¢÷WB7G&–ærG—VD7W7FöÕ–ÆöB’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&BG—VB7W7FöÒFF&WG&–WfÂ7FFRâ"“°¢&WV—&R€¢G—VD7W7FöÕ–ÆöBÓÒ&FVfVÇBÖ—FVÒ4D²7W7FöÒ–ÆöB"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&BG—VB7W7FöÒFF&WG&–WfÂâ"“°¢&WV—&R€¢6Æ—&ö&Bä—47W'&VçB†7W'&VçDFFö&¦V7B’À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&B6WDFFö&¦V7B7W'&VçB7FFRâ"“° ¢f"f–ÆTG&÷Æ—7BÒæWr7G&–æt6öÆÆV7F–öà¢°¢"÷F×öFVfVÇBÖ—FVÒÖÇ†çG‡B"À¢"÷F×öFVfVÇBÖ—FVÒÖ&WFçG‡B ¢Ó°¢6Æ—&ö&Bå6WDf–ÆTG&÷Æ—7B†f–ÆTG&÷Æ—7B“°¢&WV—&R„6Æ—&ö&Bä6öçF–ç4f–ÆTG&÷Æ—7B‚’Â$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&Bf–ÆRÖG&÷7FFRâ"“°¢f"&÷VæEG&—f–ÆTG&÷Æ—7BÒ6Æ—&ö&BävWDf–ÆTG&÷Æ—7B‚“°¢&WV—&R‡&÷VæEG&—f–ÆTG&÷Æ—7Bä6÷VçBÓÒ"Â$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&Bf–ÆRÖG&÷6÷VçBâ"“°¢&WV—&R€¢&÷VæEG&—f–ÆTG&÷Æ—7E³ÒÓÒ"÷F×öFVfVÇBÖ—FVÒÖÇ†çG‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&Bf—'7Bf–ÆRÖG&÷—FVÒâ"“°¢&WV—&R€¢&÷VæEG&—f–ÆTG&÷Æ—7E³ÒÓÒ"÷F×öFVfVÇBÖ—FVÒÖ&WFçG‡B"À¢$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&B6V6öæBf–ÆRÖG&÷—FVÒâ"“° ¢6Æ—&ö&Bä6ÆV"‚“°¢&WV—&R‚6Æ—&ö&Bä6öçF–ç5FW‡B‚’Â$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&B6ÆV&VBFW‡B7FFRâ"“°¢&WV—&R‚6Æ—&ö&Bä6öçF–ç4f–ÆTG&÷Æ—7B‚’Â$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&B6ÆV&VBf–ÆRÖG&÷7FFRâ"“°¢&WV—&R„6Æ—&ö&BävWEFW‡B‚’ÓÒ7G&–æräV×G’Â$W‡V7FVBFVfVÇBÖ—FVÒ6Æ—&ö&B6ÆV&VBFW‡Bâ"“°¢Ğ ¢&—fFRfö–BfÆ–FFTFVfVÇD—FV×4f–ÆTF–Æöw2‚¢°¢G—R6W'f–6UG—RÒG—Vöb„÷Väf–ÆTF–Æör’ä76VÖ&Ç’ävWEG—R€¢$Ö–7&÷6ögBåv–ã3"å÷'F&ÆTf–ÆTF–Æöu6W'f–6R"À¢F‡&÷töäW'&÷#¢fÇ6R¢óòF‡&÷ræWrG—TÆöDW†6WF–öâ‚$Ö–7&÷6ögBåv–ã3"å÷'F&ÆTf–ÆTF–Æöu6W'f–6R"“°¢f"—4Væ&ÆVE&÷W'G’Ò6W'f–6UG—RävWE&÷W'G’€¢$—4Væ&ÆVB"À¢&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢óòF‡&÷ræWrÖ—76–ætÖVÖ&W$W†6WF–öâ‡6W'f–6UG—RägVÆÄæÖRÂ$—4Væ&ÆVB"“°¢–b‚÷W&F–æu7—7FVÒä—5v–æF÷w2‚’¢°¢&WV—&R€¢†&ööÂ’†—4Væ&ÆVE&÷W'G’ävWEfÇVR†çVÆÂ’óòfÇ6R’À¢$W‡V7FVBFVfVÇBÖ—FVÒ÷'F&ÆRf–ÆRF–Æör6W'f–6RFò&RVæ&ÆVBâ"“°¢Ğ ¢f"&Vv—7FW$ÖWF†öBÒ6W'f–6UG—RävWDÖWF†öB€¢%&Vv—7FW""À¢&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2À¢&–æFW#¢çVÆÂÀ¢G—W3¢æWuµÒ²G—Vöb„gVæ3Æö&¦V7BÂ7G&–æsóâ’ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡6W'f–6UG—RägVÆÄæÖRÂ%&Vv—7FW""“° ¢7G&–ærFV×F—&V7F÷'’ÒF‚ä6öÖ&–æR€¢F‚ävWEFV×F‚‚’À¢'&öwR×wbÖFVfVÇBÖ—FV×2Öf–ÆRÖF–ÆörÒ"²wV–BäæWtwV–B‚’åFõ7G&–ær‚$â"’“°¢F—&V7F÷'’ä7&VFTF—&V7F÷'’‡FV×F—&V7F÷'’“°¢7G&–ær÷VåF‚ÒF‚ä6öÖ&–æR‡FV×F—&V7F÷'’Â&÷VâçG‡B"“°¢7G&–ær6fUF…v—F†÷WDW‡FVç6–öâÒF‚ä6öÖ&–æR‡FV×F—&V7F÷'’Â'6fVB"“°¢7G&–ær6fUF‚Ò6fUF…v—F†÷WDW‡FVç6–öâ²"çG‡B#°¢f–ÆRåw&—FTÆÅFW‡B†÷VåF‚Â&FVfVÇBÖ—FVÒ4D²f–ÆRF–Æör"“° ¢–çB&WVW7D6÷VçBÒ°¢f"6VVä¶–æG2ÒæWrÆ—7CÇ7G&–æsâ‚“°¢gVæ3Æö&¦V7BÂ7G&–æsóâ†æFÆW"Ò&WVW7BÓà¢°¢7G&–ær¶–æBÒ&VDFVfVÇD—FV×5÷'F&ÆU&WVW7E7G&–ær‡&WVW7BÂ$¶–æB"“°¢6VVä¶–æG2äFB†¶–æB“°¢&WVW7D6÷VçB²³° ¢&WGW&â¶–æB7v—F6€¢°¢%6fTf–ÆR"Óâ6fUF…v—F†÷WDW‡FVç6–öâÀ¢%–6´föÆFW""ÓâFV×F—&V7F÷'’À¢òÓâ÷VåF€¢Ó°¢Ó° ¢”F—7÷6&ÆSò&Vv—7G&F–öâÒçVÆÃ°¢G'¢°¢&Vv—7G&F–öâÒ„”F—7÷6&ÆSò—&Vv—7FW$ÖWF†öBä–çfö¶R†çVÆÂÂæWrö&¦V7EµÒ²†æFÆW"Ò“° ¢f"÷VäF–ÆörÒæWr÷Väf–ÆTF–Æöp¢°¢f–ÇFW"Ò%FW‡Bf–ÆW2‚¢çG‡B—Â¢çG‡GÄÆÂf–ÆW2‚¢â¢—Â¢â¢ ¢Ó°¢&WV—&R†÷VäF–Æörå6†÷tF–Æör‚’ÓÒG'VRÂ$W‡V7FVBFVfVÇBÖ—FVÒ÷Väf–ÆTF–Æör&W7VÇBâ"“°¢&WV—&R†÷VäF–Æöräf–ÆTæÖRÓÒ÷VåF‚Â$W‡V7FVBFVfVÇBÖ—FVÒ÷Väf–ÆTF–Æörf–ÆTæÖRâ"“°¢&WV—&R†÷VäF–Æörå6fTf–ÆTæÖRÓÒ&÷VâçG‡B"Â$W‡V7FVBFVfVÇBÖ—FVÒ÷Väf–ÆTF–Æör6fTf–ÆTæÖRâ"“° ¢f"6fTF–ÆörÒæWr6fTf–ÆTF–Æöp¢°¢FVfVÇDW‡BÒ'G‡B"À¢÷fW'w&—FU&ö×BÒfÇ6P¢Ó°¢&WV—&R‡6fTF–Æörå6†÷tF–Æör‡F†—2’ÓÒG'VRÂ$W‡V7FVBFVfVÇBÖ—FVÒ÷væW"6fTf–ÆTF–Æör&W7VÇBâ"“°¢&WV—&R‡6fTF–Æöräf–ÆTæÖRÓÒ6fUF‚Â$W‡V7FVBFVfVÇBÖ—FVÒ÷væW"6fTf–ÆTF–Æörf–ÆTæÖRâ"“°¢&WV—&R‡6fTF–Æörå6fTf–ÆTæÖRÓÒ'6fVBçG‡B"Â$W‡V7FVBFVfVÇBÖ—FVÒ÷væW"6fTf–ÆTF–Æör6fTf–ÆTæÖRâ"“° ¢f"föÆFW$F–ÆörÒæWr÷VäföÆFW$F–Æör‚“°¢&WV—&R†föÆFW$F–Æörå6†÷tF–Æör‡F†—2’ÓÒG'VRÂ$W‡V7FVBFVfVÇBÖ—FVÒ÷væW"÷VäföÆFW$F–Æör&W7VÇBâ"“°¢&WV—&R†föÆFW$F–ÆöräföÆFW$æÖRÓÒFV×F—&V7F÷'’Â$W‡V7FVBFVfVÇBÖ—FVÒ÷væW"÷VäföÆFW$F–ÆörföÆFW$æÖRâ"“°¢&WV—&R€¢föÆFW$F–Æörå6fTföÆFW$æÖRÓÒF‚ävWDf–ÆTæÖR‡FV×F—&V7F÷'’’À¢$W‡V7FVBFVfVÇBÖ—FVÒ÷væW"÷VäföÆFW$F–Æör6fTföÆFW$æÖRâ"“° ¢&WV—&R‡&WVW7D6÷VçBÓÒ2Â$W‡V7FVBFVfVÇBÖ—FVÒf–ÆRF–Æör&WVW7B6÷VçBâ"“°¢&WV—&R‡6VVä¶–æG5³ÒÓÒ$÷Väf–ÆR"Â$W‡V7FVBFVfVÇBÖ—FVÒf–ÆRF–Æör÷Vâ&WVW7B¶–æBâ"“°¢&WV—&R‡6VVä¶–æG5³ÒÓÒ%6fTf–ÆR"Â$W‡V7FVBFVfVÇBÖ—FVÒf–ÆRF–Æör6fR&WVW7B¶–æBâ"“°¢&WV—&R‡6VVä¶–æG5³%ÒÓÒ%–6´föÆFW""Â$W‡V7FVBFVfVÇBÖ—FVÒf–ÆRF–ÆörföÆFW"&WVW7B¶–æBâ"“°¢Ğ¢f–æÆÇ¢°¢&Vv—7G&F–öãòäF—7÷6R‚“°¢F—&V7F÷'’äFVÆWFR‡FV×F—&V7F÷'’Â&V7W'6—fS¢G'VR“°¢Ğ¢Ğ ¢&—fFR7FF–27G&–ær&VDFVfVÇD—FV×5÷'F&ÆU&WVW7E7G&–ær†ö&¦V7B&WVW7BÂ7G&–ær&÷W'G”æÖR¢°¢&WGW&â&WVW7BävWEG—R‚¢ävWE&÷W'G’‡&÷W'G”æÖRÂ&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢òävWEfÇVR‡&WVW7B¢òåFõ7G&–ær‚¢óò7G&–æräV×G“°¢Ğ ¢&—fFR7FF–2B&WV—&UG—SÅCâ†ö&¦V7CòfÇVRÂ7G&–ærFW67&—F–öâ¢°¢&WGW&âfÇVR—2BG—V@¢òG—V@¢¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$W‡V7FVB¶FW67&—F–öçÒFò&R·G—Vöb…B’ägVÆÄæÖWÒâ"“°¢Ğ ¢&—fFR7FF–2fö–B&WV—&R†&ööÂ6öæF—F–öâÂ7G&–ærÖW76vR¢°¢–b‚6öæF—F–öâ¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ†ÖW76vR“°¢Ğ¢Ğ¢Ğ¢"""“° ¢&WGW&â&ö¦V7EFƒ°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFTW‡FW&æÅ&ö¦V7E6†R‡7G&–ærv÷&µ&ö÷B¢°¢7G&–ær&ö¦V7BÒf–ÆRå&VDÆÅFW‡B…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ76VÖ&Ç”æÖRÂ76VÖ&Ç”æÖR²"æ77&ö¢"’“°¢7G&–ærÆ–'&'•&ö¦V7BÒf–ÆRå&VDÆÅFW‡B…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂÆ–'&'”76VÖ&Ç”æÖRÂÆ–'&'”76VÖ&Ç”æÖR²"æ77&ö¢"’“°¢7G&–ærÆö6Æ—¦F–öå&ö¦V7BÒf–ÆRå&VDÆÅFW‡B…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂÆö6Æ—¦F–öä76VÖ&Ç”æÖRÂÆö6Æ—¦F–öä76VÖ&Ç”æÖR²"æ77&ö¢"’“° ¢76W'D6öçF–ç2†&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ$Æ–'&Uubå6F²÷µ6FµfW'6–öçÕÂ#â"Â&W‡FW&æÂ4D²"“°¢76W'DFöW4æ÷D6öçF–â†&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ'´÷&–v–æÅwe6F·ÕÂ#â"Â&W‡FW&æÂ÷&–v–æÂ4D²"“°¢76W'DFöW4æ÷D6öçF–â†&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ'´÷&–v–æÅv–æF÷w4FW6·F÷we6F·ÕÂ#â"Â&W‡FW&æÂ÷&–v–æÂv–æF÷w4FW6·F÷4D²"“°¢76W'D6öçF–ç2†&ö¦V7BÂB#Ä76VÖ&Ç”æÖSç´÷WGWD76VÖ&Ç”æÖWÓÂô76VÖ&Ç”æÖSâ"Â&W‡FW&æÂ7W7FöÒ76VÖ&Ç’æÖR"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#Ä÷WGWEG—Såv–äW†SÂô÷WGWEG—Sâ"Â&W‡FW&æÂ÷WGWBG—R"“°¢76W'D6öçF–ç2†&ö¦V7BÂB#ÅF&vWDg&ÖWv÷&³ç´W‡FW&æÄF&vWDg&ÖWv÷&·ÓÂõF&vWDg&ÖWv÷&³â"Â&W‡FW&æÂv–æF÷w2F&vWBg&ÖWv÷&²"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#ÄVæ&ÆTFVfVÇD—FV×3æfÇ6SÂôVæ&ÆTFVfVÇD—FV×3â"Â&W‡FW&æÂW‡Æ–6—B—FVÒÖöFR"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#ÅW6UucçG'VSÂõW6Uucâ"Â&W‡FW&æÂub&÷W'G’"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#Ä6ö×–ÆR–æ6ÇVFSÕÂ"¢¢ò¢æ75Â"óâ"Â&W‡FW&æÂW‡Æ–6—B6ö×–ÆR—FV×2"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#ÄÆ–6F–öäFVf–æ—F–öâ–æ6ÇVFSÕÂ$ç†ÖÅÂ"óâ"Â&W‡FW&æÂW‡Æ–6—BÆ–6F–öâFVf–æ—F–öâ—FVÒ"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#ÅvR–æ6ÇVFSÕÂ"¢¢ò¢ç†ÖÅÂ"W†6ÇVFSÕÂ$ç†ÖÅÂ"óâ"Â&W‡FW&æÂW‡Æ–6—BvR—FV×2"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#ÄæöæR–æ6ÇVFSÕÂ$æ6öæf–uÂ"óâ"Â&W‡FW&æÂW‡Æ–6—B6öæf–r—FVÒ"“°¢76W'D6öçF–ç2†&ö¦V7BÂB#Å&ö¦V7E&VfW&Væ6R–æ6ÇVFSÕÂ"ââ÷´Æ–'&'”76VÖ&Ç”æÖWÒ÷´Æ–'&'”76VÖ&Ç”æÖWÒæ77&ö¥Â"óâ"Â&W‡FW&æÂ&ö¦V7B&VfW&Væ6R"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#Å6¶vU&VfW&Væ6R–æ6ÇVFSÕÂ$W‡FVæFVBåwbåFööÆ¶—EÂ"fW'6–öãÕÂ#Rãã%Â"óâ"Â&W‡FW&æÂ†6VVBFööÆ¶—B6¶vR&VfW&Væ6R"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#Å&W6÷W&6R–æ6ÇVFSÕÂ$76WG2ôW‡FW&æÅ&W6÷W&6RçG‡EÂ"óâ"Â&W‡FW&æÂub&W6÷W&6R—FVÒ"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#Å&W6÷W&6R–æ6ÇVFSÕÂ$76WG2ôW‡FW&æÄ–ÖvRçæuÂ"óâ"Â&W‡FW&æÂub–ÖvR&W6÷W&6R—FVÒ"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#Å7Æ6…67&VVâ–æ6ÇVFSÕÂ$76WG2ôW‡FW&æÅ7Æ6‚çæuÂ"óâ"Â&W‡FW&æÂub7Æ6‚—FVÒ"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#Ä6öçFVçB–æ6ÇVFSÕÂ$76WG2ôW‡FW&æÄ6öçFVçBçG‡EÂ#â"Â&W‡FW&æÂ6öçFVçB—FVÒ"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#Ä6÷•Fô÷WGWDF—&V7F÷'“å&W6W'fTæWvW7CÂô6÷•Fô÷WGWDF—&V7F÷'“â"Â&W‡FW&æÂ6öçFVçB÷WGWBÖWFFF"“°¢76W'D6öçF–ç2†&ö¦V7BÂ#ÅF&vWEFƒä76WG2ôW‡FW&æÄ6öçFVçBçG‡CÂõF&vWEFƒâ"Â&W‡FW&æÂ6öçFVçBF&vWBF‚ÖWFFF"“°¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ$Æ–'&Uubå6F²÷µ6FµfW'6–öçÕÂ#â"Â&W‡FW&æÂÆ–'&'’4D²"“°¢76W'DFöW4æ÷D6öçF–â†Æ–'&'•&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ'´÷&–v–æÅwe6F·ÕÂ#â"Â&W‡FW&æÂÆ–'&'’÷&–v–æÂ4D²"“°¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂB#Ä76VÖ&Ç”æÖSç´Æ–'&'”÷WGWD76VÖ&Ç”æÖWÓÂô76VÖ&Ç”æÖSâ"Â&W‡FW&æÂÆ–'&'’7W7FöÒ76VÖ&Ç’æÖR"“°¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂB#ÅF&vWDg&ÖWv÷&·3ç´W‡FW&æÄF&vWDg&ÖWv÷&·ÓÂõF&vWDg&ÖWv÷&·3â"Â&W‡FW&æÂÆ–'&'’v–æF÷w2F&vWBg&ÖWv÷&·2"“°¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂ#ÄVæ&ÆTFVfVÇD—FV×3æfÇ6SÂôVæ&ÆTFVfVÇD—FV×3â"Â&W‡FW&æÂÆ–'&'’W‡Æ–6—B—FVÒÖöFR"“°¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂ#ÅW6UucçG'VSÂõW6Uucâ"Â&W‡FW&æÂÆ–'&'’ub&÷W'G’"“°¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂ#Ä6ö×–ÆR–æ6ÇVFSÕÂ$W‡FW&æÅæVÂç†ÖÂæ75Â"óâ"Â&W‡FW&æÂÆ–'&'’W‡Æ–6—BæVÂ6öFR—FVÒ"“°¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂ#Ä6ö×–ÆR–æ6ÇVFSÕÂ$W‡FW&æÅF†VÖVD6öçG&öÂæ75Â"óâ"Â&W‡FW&æÂÆ–'&'’W‡Æ–6—BF†VÖVB6öçG&öÂ6öFR—FVÒ"“°¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂ#Ä6ö×–ÆR–æ6ÇVFSÕÂ%&÷W'F–W2ô76VÖ&Ç”–æfòæ75Â"óâ"Â&W‡FW&æÂÆ–'&'’W‡Æ–6—BF†VÖT–æfò6öFR—FVÒ"“°¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂ#ÅvR–æ6ÇVFSÕÂ$W‡FW&æÅæVÂç†ÖÅÂ"óâ"Â&W‡FW&æÂÆ–'&'’W‡Æ–6—BW6W"Ö6öçG&öÂvR—FVÒ"“°¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂ#ÅvR–æ6ÇVFSÕÂ%F†VÖW2ôvVæW&–2ç†ÖÅÂ"óâ"Â&W‡FW&æÂÆ–'&'’W‡Æ–6—BvVæW&–2F†VÖRvR—FVÒ"“°¢76W'D6öçF–ç2†Æö6Æ—¦F–öå&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ$Æ–'&Uubå6F²÷µ6FµfW'6–öçÕÂ#â"Â&W‡FW&æÂÆö6Æ—¦F–öâ4D²"“°¢76W'DFöW4æ÷D6öçF–â†Æö6Æ—¦F–öå&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ'´÷&–v–æÅwe6F·ÕÂ#â"Â&W‡FW&æÂÆö6Æ—¦F–öâ÷&–v–æÂ4D²"“°¢76W'D6öçF–ç2†Æö6Æ—¦F–öå&ö¦V7BÂB#ÅF&vWDg&ÖWv÷&³ç´W‡FW&æÄF&vWDg&ÖWv÷&·ÓÂõF&vWDg&ÖWv÷&³â"Â&W‡FW&æÂÆö6Æ—¦F–öâv–æF÷w2F&vWBg&ÖWv÷&²"“°¢76W'D6öçF–ç2†Æö6Æ—¦F–öå&ö¦V7BÂ#ÅW6UucçG'VSÂõW6Uucâ"Â&W‡FW&æÂÆö6Æ—¦F–öâub&÷W'G’"“°¢76W'D6öçF–ç2†Æö6Æ—¦F–öå&ö¦V7BÂ#ÄÆö6Æ—¦F–öäF—&V7F—fW5FôÆö4f–ÆSäÆÃÂôÆö6Æ—¦F–öäF—&V7F—fW5FôÆö4f–ÆSâ"Â&W‡FW&æÂÆö6Æ—¦F–öâF—&V7F—fR÷WGWB"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂÆ–'&'”76VÖ&Ç”æÖRÂ%&÷W'F–W2"Â$76VÖ&Ç”–æfòæ72"’Â&W‡FW&æÂ4D²Æ–'&'’F†VÖT–æfò6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂÆ–'&'”76VÖ&Ç”æÖRÂ%F†VÖW2"Â$vVæW&–2ç†ÖÂ"’Â&W‡FW&æÂ4D²Æ–'&'’vVæW&–2ç†ÖÂ6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂÆö6Æ—¦F–öä76VÖ&Ç”æÖRÂ$Æö6Æ—¦VEf–Wrç†ÖÂ"’Â&W‡FW&æÂ4D²Æö6Æ—¦F–öâ„ÔÂ6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ76VÖ&Ç”æÖRÂ$76WG2"Â$W‡FW&æÅ&W6÷W&6RçG‡B"’Â&W‡FW&æÂ4D²ub&W6÷W&6R6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ76VÖ&Ç”æÖRÂ$76WG2"Â$W‡FW&æÄ–ÖvRçær"’Â&W‡FW&æÂ4D²ub–ÖvR&W6÷W&6R6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ76VÖ&Ç”æÖRÂ$76WG2"Â$W‡FW&æÅ7Æ6‚çær"’Â&W‡FW&æÂ4D²ub7Æ6‚6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ76VÖ&Ç”æÖRÂ$76WG2"Â$W‡FW&æÄ6öçFVçBçG‡B"’Â&W‡FW&æÂ4D²6÷–VB6öçFVçB6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ76VÖ&Ç”æÖRÂ$æ6öæf–r"’Â&W‡FW&æÂ4D²6öæf–r6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ76VÖ&Ç”æÖRÂ$W‡FW&æÅ&W6÷W&6W2ç†ÖÂ"’Â&W‡FW&æÂ4D²ÖW&vVB&W6÷W&6RF–7F–öæ'’6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ76VÖ&Ç”æÖRÂ$W‡FW&æÅvRç†ÖÂ"’Â&W‡FW&æÂ4D²6ö×–ÆVBvR6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ76VÖ&Ç”æÖRÂ$W‡FW&æÅ6V6öæEvRç†ÖÂ"’Â&W‡FW&æÂ4D²6V6öæB6ö×–ÆVBvR6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ76VÖ&Ç”æÖRÂ$W‡FW&æÄÆöD6ö×öæVçEf–Wrç†ÖÂ"’Â&W‡FW&æÂ4D²ÆöD6ö×öæVçBW6W"Ö6öçG&öÂ6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ76VÖ&Ç”æÖRÂ$W‡FW&æÄÖçVÄÆöD6ö×öæVçEf–Wrç†ÖÂ"’Â&W‡FW&æÂ4D²ÖçVÂÆöD6ö×öæVçBW6W"Ö6öçG&öÂ6÷W&6R"“° ¢76W'DFöW4æ÷D6öçF–â†&ö¦V7BÂ%&ôwUwe&VfW&Væ6TÖöFR"Â&W‡FW&æÂÆö6Â'F–f7BÖöFR"“°¢76W'DFöW4æ÷D6öçF–â†&ö¦V7BÂ%&ôwUwdÖævVE&VfW&Væ6U&ö÷B"Â&W‡FW&æÂÖævVB'F–f7B&ö÷B"“°¢76W'DFöW4æ÷D6öçF–â†&ö¦V7BÂ%&ôwU&VfW&Væ6U&ö÷B"Â&W‡FW&æÂ&ôuR'F–f7B&ö÷B"“°¢76W'DFöW4æ÷D6öçF–â†Æ–'&'•&ö¦V7BÂ%&ôwUwe&VfW&Væ6TÖöFR"Â&W‡FW&æÂÆ–'&'’Æö6Â'F–f7BÖöFR"“°¢76W'DFöW4æ÷D6öçF–â†Æ–'&'•&ö¦V7BÂ%&ôwUwdÖævVE&VfW&Væ6U&ö÷B"Â&W‡FW&æÂÆ–'&'’ÖævVB'F–f7B&ö÷B"“°¢76W'DFöW4æ÷D6öçF–â†Æ–'&'•&ö¦V7BÂ%&ôwU&VfW&Væ6U&ö÷B"Â&W‡FW&æÂÆ–'&'’&ôuR'F–f7B&ö÷B"“°¢76W'DFöW4æ÷D6öçF–â†Æö6Æ—¦F–öå&ö¦V7BÂ%&ôwUwe&VfW&Væ6TÖöFR"Â&W‡FW&æÂÆö6Æ—¦F–öâÆö6Â'F–f7BÖöFR"“°¢76W'DFöW4æ÷D6öçF–â†Æö6Æ—¦F–öå&ö¦V7BÂ%&ôwUwdÖævVE&VfW&Væ6U&ö÷B"Â&W‡FW&æÂÆö6Æ—¦F–öâÖævVB'F–f7B&ö÷B"“°¢76W'DFöW4æ÷D6öçF–â†Æö6Æ—¦F–öå&ö¦V7BÂ%&ôwU&VfW&Væ6U&ö÷B"Â&W‡FW&æÂÆö6Æ—¦F–öâ&ôuR'F–f7B&ö÷B"“° ¢–b„f–ÆRäW†—7G2…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ$F—&V7F÷'’ä'V–ÆBç&÷2"’’ÇÀ¢f–ÆRäW†—7G2…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ$F—&V7F÷'’ä'V–ÆBçF&vWG2"’’¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡FW&æÂ4D²6Öö¶R×W7Bæ÷B&VÇ’öâvVæW&FVBF—&V7F÷'’ä'V–ÆBç&÷2÷"F—&V7F÷'’ä'V–ÆBçF&vWG2f–ÆW2â"“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFTW‡FW&æÄ6VçG&Å6¶vTÖævVÖVçE&ö¦V7E6†R‡7G&–ær&ö¦V7EF‚¢°¢7G&–ær&ö÷BÒF‚ävWDF—&V7F÷'”æÖR‡&ö¦V7EF‚¢óòF‡&÷ræWr&wVÖVçDW†6WF–öâ‚%&ö¦V7BF‚†2æòF—&V7F÷'’â"ÂæÖVöb‡&ö¦V7EF‚’“°¢7G&–ærv÷&µ&ö÷BÒF—&V7F÷'’ävWE&VçB†&ö÷B“òägVÆÄæÖP¢óòF‡&÷ræWr&wVÖVçDW†6WF–öâ‚%&ö¦V7BF‚†2æò&VçBF—&V7F÷'’â"ÂæÖVöb‡&ö¦V7EF‚’“°¢7G&–ær&ö¦V7BÒf–ÆRå&VDÆÅFW‡B‡&ö¦V7EF‚“°¢7G&–ær6VçG&Å6¶vW2Òf–ÆRå&VDÆÅFW‡B…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂ$F—&V7F÷'’å6¶vW2ç&÷2"’“° ¢76W'D6öçF–ç2‡&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ$Æ–'&Uubå6F²÷µ6FµfW'6–öçÕÂ#â"Â&W‡FW&æÂ5Ò4D²"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ'´÷&–v–æÅwe6F·ÕÂ#â"Â&W‡FW&æÂ5Ò÷&–v–æÂ4D²"“°¢76W'D6öçF–ç2‡&ö¦V7BÂB#Ä76VÖ&Ç”æÖSç´6VçG&Å6¶vTÖævVÖVçD÷WGWD76VÖ&Ç”æÖWÓÂô76VÖ&Ç”æÖSâ"Â&W‡FW&æÂ5Ò7W7FöÒ76VÖ&Ç’æÖR"“°¢76W'D6öçF–ç2‡&ö¦V7BÂB#ÅF&vWDg&ÖWv÷&³ç´W‡FW&æÄF&vWDg&ÖWv÷&·ÓÂõF&vWDg&ÖWv÷&³â"Â&W‡FW&æÂ5Òv–æF÷w2F&vWBg&ÖWv÷&²"“°¢76W'D6öçF–ç2‡&ö¦V7BÂ#ÅW6UucçG'VSÂõW6Uucâ"Â&W‡FW&æÂ5Òub&÷W'G’"“°¢76W'D6öçF–ç2‡&ö¦V7BÂ#Å6¶vU&VfW&Væ6R–æ6ÇVFSÕÂ%7—7FVÒå&V7F—fUÂ"óâ"Â&W‡FW&æÂ5ÒVçfW'6–öæVB6¶vR&VfW&Væ6R"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂ%fW'6–öãÒ"Â&W‡FW&æÂ5Ò&ö¦V7B6¶vRfW'6–öâÖWFFF"“°¢76W'D6öçF–ç2†6VçG&Å6¶vW2Â#ÄÖævU6¶vUfW'6–öç46VçG&ÆÇ“çG'VSÂôÖævU6¶vUfW'6–öç46VçG&ÆÇ“â"Â&W‡FW&æÂ5Ò6VçG&Â6¶vRÖævVÖVçB7v—F6‚"“°¢76W'D6öçF–ç2†6VçG&Å6¶vW2Â#Å6¶vUfW'6–öâ–æ6ÇVFSÕÂ%7—7FVÒå&V7F—fUÂ"fW'6–öãÕÂ#bããÂ"óâ"Â&W‡FW&æÂ5Ò6VçG&Â6¶vRfW'6–öâ"“°¢76W'DFöW4æ÷D6öçF–â†6VçG&Å6¶vW2Â$Æ–'&UubåG&ç7÷'B"Â&W‡FW&æÂ5Ò6VçG&ÂubG&ç7÷'BfW'6–öâ"“°¢76W'DFöW4æ÷D6öçF–â†6VçG&Å6¶vW2Â$Æ–'&Uubå&ôuR"Â&W‡FW&æÂ5Ò6VçG&Âub'&–FvRfW'6–öâ"“°¢76W'DFöW4æ÷D6öçF–â†6VçG&Å6¶vW2Â%&ôuRäF—&V7E‚"Â&W‡FW&æÂ5Ò6VçG&Â&ôuRF—&V7E‚fW'6–öâ"“°¢76W'DFöW4æ÷D6öçF–â†6VçG&Å6¶vW2Â%6–Æ²ääUBåvV$uR"Â&W‡FW&æÂ5Ò6VçG&Â6–Æ²vV$uRfW'6–öâ"“° ¢7G&–ær÷WGWD76VÖ&Ç’ÒF‚ä6öÖ&–æR€¢&ö÷BÀ¢&&–â"À¢$FV'Vr"À¢W‡FW&æÄF&vWDg&ÖWv÷&²À¢6VçG&Å6¶vTÖævVÖVçD÷WGWD76VÖ&Ç”æÖR²"æFÆÂ"“°¢&WV—&Tf–ÆR†÷WGWD76VÖ&Ç’Â&W‡FW&æÂ5Ò÷WGWB76VÖ&Ç’"“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFTW‡FW&æÄFVfVÇD—FV×5&ö¦V7E6†R‡7G&–ærv÷&µ&ö÷B¢°¢7G&–ær&ö¦V7EF‚ÒF‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂFVfVÇD—FV×476VÖ&Ç”æÖR²"æ77&ö¢"“°¢7G&–ærÆ–'&'•&ö¦V7EF‚ÒF‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖR²"æ77&ö¢"“°¢7G&–ær&ö¦V7BÒf–ÆRå&VDÆÅFW‡B‡&ö¦V7EF‚“°¢7G&–ærÆ–'&'•&ö¦V7BÒf–ÆRå&VDÆÅFW‡B†Æ–'&'•&ö¦V7EF‚“° ¢76W'D6öçF–ç2‡&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ$Æ–'&Uubå6F²÷µ6FµfW'6–öçÕÂ#â"Â&W‡FW&æÂFVfVÇBÖ—FVÒ4D²"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ'´÷&–v–æÅwe6F·ÕÂ#â"Â&W‡FW&æÂFVfVÇBÖ—FVÒ÷&–v–æÂ4D²"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ'´÷&–v–æÅv–æF÷w4FW6·F÷we6F·ÕÂ#â"Â&W‡FW&æÂFVfVÇBÖ—FVÒ÷&–v–æÂv–æF÷w4FW6·F÷4D²"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂ#Ä76VÖ&Ç”æÖSâ"Â&W‡FW&æÂFVfVÇBÖ—FVÒ7W7FöÒ76VÖ&Ç’æÖR"“°¢76W'D6öçF–ç2‡&ö¦V7BÂ#Ä÷WGWEG—Såv–äW†SÂô÷WGWEG—Sâ"Â&W‡FW&æÂFVfVÇBÖ—FVÒ÷WGWBG—R"“°¢76W'D6öçF–ç2‡&ö¦V7BÂB#ÅF&vWDg&ÖWv÷&³ç´W‡FW&æÄF&vWDg&ÖWv÷&·ÓÂõF&vWDg&ÖWv÷&³â"Â&W‡FW&æÂFVfVÇBÖ—FVÒv–æF÷w2F&vWBg&ÖWv÷&²"“°¢76W'D6öçF–ç2‡&ö¦V7BÂ#ÅW6UucçG'VSÂõW6Uucâ"Â&W‡FW&æÂFVfVÇBÖ—FVÒub&÷W'G’"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂ#ÄVæ&ÆTFVfVÇD—FV×3æfÇ6SÂôVæ&ÆTFVfVÇD—FV×3â"Â&W‡FW&æÂFVfVÇBÖ—FVÒFVfVÇB—FVÒ÷BÖ÷WB"“°¢76W'D6öçF–ç2‡&ö¦V7BÂB#Å&ö¦V7E&VfW&Væ6R–æ6ÇVFSÕÂ"ââ÷´FVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖWÒ÷´FVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖWÒæ77&ö¥Â"óâ"Â&W‡FW&æÂFVfVÇBÖ—FVÒ&ö¦V7B&VfW&Væ6R"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂ#Ä6ö×–ÆR–æ6ÇVFSÒ"Â&W‡FW&æÂFVfVÇBÖ—FVÒW‡Æ–6—B6ö×–ÆR—FV×2"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂ#ÄÆ–6F–öäFVf–æ—F–öâ–æ6ÇVFSÒ"Â&W‡FW&æÂFVfVÇBÖ—FVÒW‡Æ–6—BÆ–6F–öâFVf–æ—F–öâ—FVÒ"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂ#ÅvR–æ6ÇVFSÒ"Â&W‡FW&æÂFVfVÇBÖ—FVÒW‡Æ–6—BvR—FVÒ"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂ#ÄæöæR–æ6ÇVFSÕÂ$æ6öæf–uÂ""Â&W‡FW&æÂFVfVÇBÖ—FVÒW‡Æ–6—B6öæf–r—FVÒ"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂ%&ôwUwe&VfW&Væ6TÖöFR"Â&W‡FW&æÂFVfVÇBÖ—FVÒÆö6Â'F–f7BÖöFR"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂ%&ôwUwdÖævVE&VfW&Væ6U&ö÷B"Â&W‡FW&æÂFVfVÇBÖ—FVÒÖævVB'F–f7B&ö÷B"“°¢76W'DFöW4æ÷D6öçF–â‡&ö¦V7BÂ%&ôwU&VfW&Væ6U&ö÷B"Â&W‡FW&æÂFVfVÇBÖ—FVÒ&ôuR'F–f7B&ö÷B"“° ¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ$Æ–'&Uubå6F²÷µ6FµfW'6–öçÕÂ#â"Â&W‡FW&æÂFVfVÇBÖ—FVÒÆ–'&'’4D²"“°¢76W'DFöW4æ÷D6öçF–â†Æ–'&'•&ö¦V7BÂB#Å&ö¦V7B6F³ÕÂ'´÷&–v–æÅwe6F·ÕÂ#â"Â&W‡FW&æÂFVfVÇBÖ—FVÒÆ–'&'’÷&–v–æÂ4D²"“°¢76W'DFöW4æ÷D6öçF–â†Æ–'&'•&ö¦V7BÂ#Ä76VÖ&Ç”æÖSâ"Â&W‡FW&æÂFVfVÇBÖ—FVÒÆ–'&'’7W7FöÒ76VÖ&Ç’æÖR"“°¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂB#ÅF&vWDg&ÖWv÷&³ç´W‡FW&æÄF&vWDg&ÖWv÷&·ÓÂõF&vWDg&ÖWv÷&³â"Â&W‡FW&æÂFVfVÇBÖ—FVÒÆ–'&'’v–æF÷w2F&vWBg&ÖWv÷&²"“°¢76W'D6öçF–ç2†Æ–'&'•&ö¦V7BÂ#ÅW6UucçG'VSÂõW6Uucâ"Â&W‡FW&æÂFVfVÇBÖ—FVÒÆ–'&'’ub&÷W'G’"“°¢76W'DFöW4æ÷D6öçF–â†Æ–'&'•&ö¦V7BÂ#ÄVæ&ÆTFVfVÇD—FV×3æfÇ6SÂôVæ&ÆTFVfVÇD—FV×3â"Â&W‡FW&æÂFVfVÇBÖ—FVÒÆ–'&'’FVfVÇB—FVÒ÷BÖ÷WB"“°¢76W'DFöW4æ÷D6öçF–â†Æ–'&'•&ö¦V7BÂ#Ä6ö×–ÆR–æ6ÇVFSÒ"Â&W‡FW&æÂFVfVÇBÖ—FVÒÆ–'&'’W‡Æ–6—B6ö×–ÆR—FV×2"“°¢76W'DFöW4æ÷D6öçF–â†Æ–'&'•&ö¦V7BÂ#ÅvR–æ6ÇVFSÒ"Â&W‡FW&æÂFVfVÇBÖ—FVÒÆ–'&'’W‡Æ–6—BvR—FVÒ"“°¢76W'DFöW4æ÷D6öçF–â†Æ–'&'•&ö¦V7BÂ%&ôwUwe&VfW&Væ6TÖöFR"Â&W‡FW&æÂFVfVÇBÖ—FVÒÆ–'&'’Æö6Â'F–f7BÖöFR"“°¢76W'DFöW4æ÷D6öçF–â†Æ–'&'•&ö¦V7BÂ%&ôwUwdÖævVE&VfW&Væ6U&ö÷B"Â&W‡FW&æÂFVfVÇBÖ—FVÒÆ–'&'’ÖævVB'F–f7B&ö÷B"“°¢76W'DFöW4æ÷D6öçF–â†Æ–'&'•&ö¦V7BÂ%&ôwU&VfW&Væ6U&ö÷B"Â&W‡FW&æÂFVfVÇBÖ—FVÒÆ–'&'’&ôuR'F–f7B&ö÷B"“° ¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ$ç†ÖÂ"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒç†ÖÂ6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ$ç†ÖÂæ72"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒç†ÖÂæ726÷W&6R"“°¢7G&–ærFVfVÇD—FV×46öæf–rÒf–ÆRå&VDÆÅFW‡B€¢F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ$æ6öæf–r"’“°¢76W'D6öçF–ç2€¢FVfVÇD—FV×46öæf–rÀ¢$FVfVÇD—FV×56Fµ6WGF–ær"À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ6öæf–r6WGF–ær¶W’"“°¢76W'D6öçF–ç2€¢FVfVÇD—FV×46öæf–rÀ¢$FVfVÇB—FVÒ4D²6öæf–rfÇVR"À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ6öæf–r6WGF–ærfÇVR"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ$æ6öæf–r"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ6öæf–r6÷W&6R"“°¢7G&–ærFVfVÇD—FV×4&W6÷W&6W2Òf–ÆRå&VDÆÅFW‡B€¢F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ%&W6÷W&6W2"Â$FVfVÇD—FV×4&W6÷W&6W2ç†ÖÂ"’“°¢76W'D6öçF–ç2€¢FVfVÇD—FV×4&W6÷W&6W2À¢$FVfVÇD—FV×4F–7F–öæ'•FW‡B"À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ&W6÷W&6RF–7F–öæ'’FW‡B"“°¢76W'D6öçF–ç2€¢FVfVÇD—FV×4&W6÷W&6W2À¢$FVfVÇD—FV×4F–7F–öæ'”''W6‚"À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ&W6÷W&6RF–7F–öæ'’''W6‚"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ%&W6÷W&6W2"Â$FVfVÇD—FV×4&W6÷W&6W2ç†ÖÂ"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ&W6÷W&6RF–7F–öæ'’6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ$Ö–åv–æF÷rç†ÖÂ"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÖ–åv–æF÷rç†ÖÂ6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ$Ö–åv–æF÷rç†ÖÂæ72"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÖ–åv–æF÷rç†ÖÂæ726÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ$76WG2"Â$FVfVÇD—FV×47W'6÷"æ7W""’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ7W'6÷"&W6÷W&6R6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ$FVfVÇD—FV×5æVÂç†ÖÂ"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒW6W$6öçG&öÂ„ÔÂ6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ$FVfVÇD—FV×5æVÂç†ÖÂæ72"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒW6W$6öçG&öÂ6öFR6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ$FVfVÇD—FV×5vRç†ÖÂ"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒvR„ÔÂ6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖRÂ$FVfVÇD—FV×5vRç†ÖÂæ72"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒvR6öFR6÷W&6R"“°¢7G&–ærFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”–æfòÒf–ÆRå&VDÆÅFW‡B€¢F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ%&÷W'F–W2"Â$76VÖ&Ç”–æfòæ72"’“°¢7G&–ærFVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂÒf–ÆRå&VDÆÅFW‡B€¢F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ$FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂæ72"’“°¢7G&–ærFVfVÇD—FV×4Æ–'&'”vVæW&–5F†VÖRÒf–ÆRå&VDÆÅFW‡B€¢F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ%F†VÖW2"Â$vVæW&–2ç†ÖÂ"’“°¢76W'D6öçF–ç2€¢FVfVÇD—FV×4Æ–'&'”76VÖ&Ç”–æfòÀ¢%¶76VÖ&Ç“¢F†VÖT–æfò…&W6÷W&6TF–7F–öæ'”Æö6F–öâäæöæRÂ&W6÷W&6TF–7F–öæ'”Æö6F–öâå6÷W&6T76VÖ&Ç’•Ò"À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’F†VÖT–æfò6÷W&6R"“°¢76W'D6öçF–ç2€¢FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂÀ¢$FVfVÇE7G–ÆT¶W•&÷W'G’ä÷fW'&–FTÖWFFF"À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’FVfVÇB7G–ÆR¶W’"“°¢76W'D6öçF–ç2€¢FVfVÇD—FV×4Æ–'&'”vVæW&–5F†VÖRÀ¢$FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂ"À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’vVæW&–2ç†ÖÂF†VÖVB6öçG&öÂ7G–ÆR"“°¢76W'D6öçF–ç2€¢FVfVÇD—FV×4Æ–'&'”vVæW&–5F†VÖRÀ¢$FVfVÇD—FV×4Æ–'&'•F†VÖUFW‡B"À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’vVæW&–2ç†ÖÂFV×ÆFRFW‡B"“°¢7G&–ærFVfVÇD—FV×4Æ–'&'•&W6÷W&6W2Òf–ÆRå&VDÆÅFW‡B€¢F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ%&W6÷W&6W2"Â$FVfVÇD—FV×4Æ–'&'•&W6÷W&6W2ç†ÖÂ"’“°¢76W'D6öçF–ç2€¢FVfVÇD—FV×4Æ–'&'•&W6÷W&6W2À¢$FVfVÇD—FV×4Æ–'&'•&W6÷W&6UFW‡B"À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’6ö×öæVçB&W6÷W&6RF–7F–öæ'’FW‡B"“°¢76W'D6öçF–ç2€¢FVfVÇD—FV×4Æ–'&'•&W6÷W&6W2À¢$FVfVÇD—FV×4Æ–'&'•&W6÷W&6T''W6‚"À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’6ö×öæVçB&W6÷W&6RF–7F–öæ'’''W6‚"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ$FVfVÇD—FV×4Æ–'&'•æVÂç†ÖÂ"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’W6W$6öçG&öÂ„ÔÂ6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ$FVfVÇD—FV×4Æ–'&'•æVÂç†ÖÂæ72"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’W6W$6öçG&öÂ6öFR6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ$FVfVÇD—FV×4Æ–'&'•vRç†ÖÂ"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’vR„ÔÂ6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ$FVfVÇD—FV×4Æ–'&'•vRç†ÖÂæ72"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’vR6öFR6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ%&÷W'F–W2"Â$76VÖ&Ç”–æfòæ72"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’F†VÖT–æfò6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ$FVfVÇD—FV×4Æ–'&'•F†VÖVD6öçG&öÂæ72"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’F†VÖVB6öçG&öÂ6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ%F†VÖW2"Â$vVæW&–2ç†ÖÂ"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’vVæW&–2ç†ÖÂ6÷W&6R"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR‡v÷&µ&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ%&W6÷W&6W2"Â$FVfVÇD—FV×4Æ–'&'•&W6÷W&6W2ç†ÖÂ"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒÆ–'&'’6ö×öæVçB&W6÷W&6RF–7F–öæ'’6÷W&6R"“°¢Ğ ¢&—fFR7FF–27G&–ær7v—F6…we6F´öæÇ’‡7G&–æræ÷&ÖÅwe&ö¦V7BÂ7G&–ærFW67&—F–öâ¢°¢&WGW&â7v—F6…we6F´öæÇ’†æ÷&ÖÅwe&ö¦V7BÂ÷&–v–æÅwe6F²ÂFW67&—F–öâ“°¢Ğ ¢&—fFR7FF–27G&–ær7v—F6…we6F´öæÇ’‡7G&–æræ÷&ÖÅwe&ö¦V7BÂ7G&–ær÷&–v–æÅ6F´æÖRÂ7G&–ærFW67&—F–öâ¢°¢7G&–ær÷&–v–æÅ6F²ÒB#Å&ö¦V7B6F³ÕÂ'¶÷&–v–æÅ6F´æÖWÕÂ#â#°¢7G&–ær&ôwU6F²ÒB#Å&ö¦V7B6F³ÕÂ$Æ–'&Uubå6F²÷µ6FµfW'6–öçÕÂ#â#° ¢76W'D6öçF–ç2†æ÷&ÖÅwe&ö¦V7BÂ÷&–v–æÅ6F²ÂB'¶FW67&—F–öçÒ÷&–v–æÂub4D²"“°¢7G&–ær7v—F6†VE&ö¦V7BÒæ÷&ÖÅwe&ö¦V7Bå&WÆ6R†÷&–v–æÅ6F²Â&ôwU6F²Â7G&–æt6ö×&—6öâä÷&F–æÂ“°¢7G&–ær&WfW'FVE&ö¦V7BÒ7v—F6†VE&ö¦V7Bå&WÆ6R‡&ôwU6F²Â÷&–v–æÅ6F²Â7G&–æt6ö×&—6öâä÷&F–æÂ“° ¢–b‚7G&–æräWVÇ2†æ÷&ÖÅwe&ö¦V7BÂ&WfW'FVE&ö¦V7BÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B%F†R¶FW67&—F–öçÒ&ö¦V7B6†ævVBÖ÷&RF†â—G2&ö÷B4D²GW&–ær&ôuR4D²7v—F6†–ærâ"“°¢Ğ ¢&WGW&â7v—F6†VE&ö¦V7C°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFTW‡FW&æÄÆö6Æ—¦F–öäF—&V7F—fW2‡7G&–ærv÷&µ&ö÷B¢°¢7G&–ærö&¥&ö÷BÒF‚ä6öÖ&–æR‡v÷&µ&ö÷BÂÆö6Æ—¦F–öä76VÖ&Ç”æÖRÂ&ö&¢"“°¢&WV—&TF—&V7F÷'’†ö&¥&ö÷BÂ&W‡FW&æÂ4D²Æö6Æ—¦F–öâö&¢F—&V7F÷'’"“° ¢7G&–æuµÒÆö4f–ÆW2ÒF—&V7F÷'¢äVçVÖW&FTf–ÆW2†ö&¥&ö÷BÂ"¢æÆö2"Â6V&6„÷F–öâäÆÄF—&V7F÷&–W2¢ä÷&FW$'’‡7FF–2F‚ÓâF‚Â7G&–æt6ö×&W"ä÷&F–æÂ¢åFô'&’‚“°¢–b†Æö4f–ÆW2äÆVæwF‚ÓÒ¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$W‡V7FVBW‡FW&æÂ4D²Ö&·W6ö×–ÆF–öâFò&öGV6RÆö6Æ—¦F–öâæÆö2f–ÆW2â"“°¢Ğ ¢7G&–ærÆö6Æ—¦VEf–WtÆö2ÒÆö4f–ÆW2äf—'7D÷$FVfVÇB€¢7FF–2F‚Óâ7G&–æräWVÇ2…F‚ävWDf–ÆTæÖR‡F‚’Â$Æö6Æ—¦VEf–WræÆö2"Â7G&–æt6ö×&—6öâä÷&F–æÄ–væ÷&T66R’¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢$W‡V7FVBW‡FW&æÂ4D²Ö&·W6ö×–ÆF–öâFò&öGV6RÆö6Æ—¦VEf–WræÆö2â"“°¢7G&–ærÆö5FW‡BÒf–ÆRå&VDÆÅFW‡B†Æö6Æ—¦VEf–WtÆö2“° ¢76W'D6öçF–ç2†Æö5FW‡BÂ$W‡FW&æÄÆö6Æ—¦F–öå&ö÷B"Â&W‡FW&æÂ4D²Æö6Æ—¦F–öâ&ö÷BF—&V7F—fRV–B"“°¢76W'D6öçF–ç2†Æö5FW‡BÂ$W‡FW&æÄÆö6Æ—¦F–öåFW‡B"Â&W‡FW&æÂ4D²Æö6Æ—¦F–öâFW‡BF—&V7F—fRV–B"“°¢76W'D6öçF–ç2†Æö5FW‡BÂ$W‡FW&æÂÆö6Æ—¦F–öâ&ö÷B6öÖÖVçB"Â&W‡FW&æÂ4D²Æö6Æ—¦F–öâ&ö÷B6öÖÖVçB÷WGWB"“°¢76W'D6öçF–ç2†Æö5FW‡BÂ$W‡FW&æÂÆö6Æ—¦F–öâFW‡B6öÖÖVçB"Â&W‡FW&æÂ4D²Æö6Æ—¦F–öâFW‡B6öÖÖVçB÷WGWB"“°¢76W'D6öçF–ç2†Æö5FW‡BÂ%&VF&ÆR"Â&W‡FW&æÂ4D²Æö6Æ—¦F–öâ&VF&ÆRGG&–'WFR÷WGWB"“°¢76W'D6öçF–ç2†Æö5FW‡BÂ$ÖöF–f–&ÆR"Â&W‡FW&æÂ4D²Æö6Æ—¦F–öâÖöF–f–&ÆRGG&–'WFR÷WGWB"“°¢76W'D6öçF–ç2†Æö5FW‡BÂ%VæÖöF–f–&ÆR"Â&W‡FW&æÂ4D²Æö6Æ—¦F–öâVæÖöF–f–&ÆRGG&–'WFR÷WGWB"“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFTW‡FW&æÄ÷WGWB‡7G&–ær÷WGWE&ö÷BÂ7G&–ær6¶vTfVVB¢°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ÷WGWD76VÖ&Ç”æÖR²"æFÆÂ"’Â&W‡FW&æÂ4D²76VÖ&Ç’"“°¢&WV—&Tf–ÆR€¢F‚ä6öÖ&–æR†÷WGWE&ö÷BÂvWD†÷7Df–ÆTæÖR„÷WGWD76VÖ&Ç”æÖR’’À¢&W‡FW&æÂ4D²†÷7B"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂÆ–'&'”÷WGWD76VÖ&Ç”æÖR²"æFÆÂ"’Â&W‡FW&æÂ4D²Æ–'&'’76VÖ&Ç’"“°¢7G&–ær6÷–VD6öçFVçEF‚ÒF‚ä6öÖ&–æR†÷WGWE&ö÷BÂ$76WG2"Â$W‡FW&æÄ6öçFVçBçG‡B"“°¢&WV—&Tf–ÆR†6÷–VD6öçFVçEF‚Â&W‡FW&æÂ4D²6÷–VB6öçFVçB÷WGWB"“°¢76W'DWVÂ€¢$W‡FW&æÂ4D²6÷–VB6öçFVçBFW‡B"À¢f–ÆRå&VDÆÅFW‡B†6÷–VD6öçFVçEF‚’À¢&W‡FW&æÂ4D²6÷–VB6öçFVçB÷WGWBFW‡B"“°¢7G&–ær6öæf–uF‚ÒF‚ä6öÖ&–æR†÷WGWE&ö÷BÂ÷WGWD76VÖ&Ç”æÖR²"æFÆÂæ6öæf–r"“°¢&WV—&Tf–ÆR†6öæf–uF‚Â&W‡FW&æÂ4D²6öæf–r÷WGWB"“°¢7G&–ær6öæf–rÒf–ÆRå&VDÆÅFW‡B†6öæf–uF‚“°¢76W'D6öçF–ç2†6öæf–rÂ#Æ6WGF–æw3â"Â&W‡FW&æÂ4D²6öæf–r÷WGWB6WGF–æw26V7F–öâ"“°¢76W'D6öçF–ç2†6öæf–rÂ$W‡FW&æÅ6F´6WGF–ær"Â&W‡FW&æÂ4D²6öæf–r÷WGWB6WGF–ær¶W’"“°¢76W'D6öçF–ç2†6öæf–rÂ$W‡FW&æÂ4D²6öæf–rfÇVR"Â&W‡FW&æÂ4D²6öæf–r÷WGWB6WGF–ærfÇVR"“° ¢f÷&V6‚‡7G&–ær76VÖ&Ç”æÖR–â5÷&WV—&VEwe'VçF–ÖT76VÖ&Æ–W0¢ä6öæ6B‡5÷&WV—&VE&ôwU'VçF–ÖT76VÖ&Æ–W2¢ä6öæ6B‡5÷&WV—&VE6–Æ´æWE'VçF–ÖT76VÖ&Æ–W2¢ä6öæ6B‡5÷&WV—&VE7W÷'E'VçF–ÖT76VÖ&Æ–W2’¢°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ76VÖ&Ç”æÖR²"æFÆÂ"’ÂB&W‡FW&æÂ4D²÷WGWB76WBw¶76VÖ&Ç”æÖWÒæFÆÂr"“°¢Ğ ¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%†6VVBåwbåFööÆ¶—BæFÆÂ"’Â&W‡FW&æÂ4D²÷WGWB†6VVBFööÆ¶—B76WB"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%†6VVBåwbäfÆöäFö6²æFÆÂ"’Â&W‡FW&æÂ4D²÷WGWBfÆöäFö6²76WB"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%†6VVBåwbäfÆöäFö6²åF†VÖW2äW&òæFÆÂ"’Â&W‡FW&æÂ4D²÷WGWBfÆöäFö6²W&òF†VÖR76WB"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%†6VVBåwbäfÆöäFö6²åF†VÖW2äÖWG&òæFÆÂ"’Â&W‡FW&æÂ4D²÷WGWBfÆöäFö6²ÖWG&òF†VÖR76WB"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%†6VVBåwbäfÆöäFö6²åF†VÖW2åe3#æFÆÂ"’Â&W‡FW&æÂ4D²÷WGWBfÆöäFö6²e3#F†VÖR76WB"“° ¢f÷&V6‚‡7G&–ær76VÖ&Ç”æÖR–â5÷&WV—&VE&ôwU'VçF–ÖT76VÖ&Æ–W2¢°¢fÆ–FFT÷WGWD76VÖ&Ç”ÖF6†W4Æö6Å6¶vR€¢÷WGWE&ö÷BÀ¢6¶vTfVVBÀ¢vWE6¶vT–Df÷%'VçF–ÖT76VÖ&Ç’†76VÖ&Ç”æÖR’À¢76VÖ&Ç”æÖRÀ¢&æWCã"“°¢Ğ ¢f÷&V6‚‡7G&–ær76VÖ&Ç”æÖR–â5÷&WV—&VEwe'VçF–ÖT76VÖ&Æ–W2¢°¢fÆ–FFT÷WGWD76VÖ&Ç”ÖF6†W4Æö6Å6¶vR€¢÷WGWE&ö÷BÀ¢6¶vTfVVBÀ¢$Æ–'&UubåG&ç7÷'B"À¢76VÖ&Ç”æÖRÀ¢&æWCã"“°¢Ğ ¢&WV—&Tç”f–ÆR†÷WGWE&ö÷BÂvWDæF—fT76WD6æF–FFW2‚'vwR"’Â&W‡FW&æÂ4D²÷WGWBæF—fRvV$uR'VçF–ÖR76WB"“°¢&WV—&Tç”f–ÆR†÷WGWE&ö÷BÂvWDæF—fT76WD6æF–FFW2‚&vÆgr"’Â&W‡FW&æÂ4D²÷WGWBæF—fRtÄer'VçF–ÖR76WB"“° ¢7G&–ærFW4§6öâÒf–ÆRå&VDÆÅFW‡B…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ÷WGWD76VÖ&Ç”æÖR²"æFW2æ§6öâ"’“°¢76W'D6öçF–ç2†FW4§6öâÂ$Æ–'&UubåG&ç7÷'B"Â&W‡FW&æÂ4D²ubG&ç7÷'B6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ$Æ–'&Uubå&ôuR"Â&W‡FW&æÂ4D²&ôuRub6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ$Æ–'&Uubä–çFW&÷"Â&W‡FW&æÂ4D²&ôuRub–çFW&÷6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ%&ôuRäF—&V7E‚"Â&W‡FW&æÂ4D²&ôuRF—&V7E‚6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ%&ôuRä6ö×WFR"Â&W‡FW&æÂ4D²&ôuR6ö×WFR6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ%&ôuRåG&ç7–ÆW""Â&W‡FW&æÂ4D²&ôuRG&ç7–ÆW"6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ$÷VäföçE6†'"Â&W‡FW&æÂ4D²÷VäföçE6†'6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ%7F$–ÖvU6†'"Â&W‡FW&æÂ4D²7F$–ÖvU6†'6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ$W‡FVæFVBåwbåFööÆ¶—B"Â&W‡FW&æÂ4D²†6VVBFööÆ¶—B6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ%†6VVBåwbäfÆöäFö6²"Â&W‡FW&æÂ4D²fÆöäFö6²76VÖ&Ç’FWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂÆ–'&'”÷WGWD76VÖ&Ç”æÖRÂ&W‡FW&æÂ4D²&VfW&Væ6VBÆ–'&'’FWVæFVæ7’"“° ¢fÆ–FFU&ôwT†”G•&VæFW%7W&f6R†÷WGWE&ö÷B“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFTW‡FW&æÄFVfVÇD—FV×4÷WGWB‡7G&–ær÷WGWE&ö÷B¢°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖR²"æFÆÂ"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ76VÖ&Ç’"“°¢&WV—&Tf–ÆR€¢F‚ä6öÖ&–æR†÷WGWE&ö÷BÂvWD†÷7Df–ÆTæÖR„FVfVÇD—FV×476VÖ&Ç”æÖR’’À¢&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ†÷7B"“°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖR²"æFÆÂ"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’76VÖ&Ç’"“°¢7G&–ær6öæf–uF‚ÒF‚ä6öÖ&–æR†÷WGWE&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖR²"æFÆÂæ6öæf–r"“°¢&WV—&Tf–ÆR†6öæf–uF‚Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ6öæf–r÷WGWB"“°¢7G&–ær6öæf–rÒf–ÆRå&VDÆÅFW‡B†6öæf–uF‚“°¢76W'D6öçF–ç2†6öæf–rÂ$FVfVÇD—FV×56Fµ6WGF–ær"Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ6öæf–r÷WGWB6WGF–ær¶W’"“°¢76W'D6öçF–ç2†6öæf–rÂ$FVfVÇB—FVÒ4D²6öæf–rfÇVR"Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ6öæf–r÷WGWB6WGF–ærfÇVR"“° ¢f÷&V6‚‡7G&–ær76VÖ&Ç”æÖR–â5÷&WV—&VEwe'VçF–ÖT76VÖ&Æ–W0¢ä6öæ6B‡5÷&WV—&VE&ôwU'VçF–ÖT76VÖ&Æ–W2¢ä6öæ6B‡5÷&WV—&VE6–Æ´æWE'VçF–ÖT76VÖ&Æ–W2¢ä6öæ6B‡5÷&WV—&VE7W÷'E'VçF–ÖT76VÖ&Æ–W2’¢°¢&WV—&Tf–ÆR…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ76VÖ&Ç”æÖR²"æFÆÂ"’ÂB&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ÷WGWB76WBw¶76VÖ&Ç”æÖWÒæFÆÂr"“°¢Ğ ¢&WV—&Tç”f–ÆR†÷WGWE&ö÷BÂvWDæF—fT76WD6æF–FFW2‚'vwR"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ÷WGWBæF—fRvV$uR'VçF–ÖR76WB"“°¢&WV—&Tç”f–ÆR†÷WGWE&ö÷BÂvWDæF—fT76WD6æF–FFW2‚&vÆgr"’Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ÷WGWBæF—fRtÄer'VçF–ÖR76WB"“° ¢7G&–ærFW4§6öâÒf–ÆRå&VDÆÅFW‡B…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂFVfVÇD—FV×476VÖ&Ç”æÖR²"æFW2æ§6öâ"’“°¢76W'D6öçF–ç2†FW4§6öâÂ$Æ–'&UubåG&ç7÷'B"Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒubG&ç7÷'B6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ$Æ–'&Uubå&ôuR"Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ&ôuRub6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ$Æ–'&Uubä–çFW&÷"Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ&ôuRub–çFW&÷6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ%&ôuRäF—&V7E‚"Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ&ôuRF—&V7E‚6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ%&ôuRä6ö×WFR"Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ&ôuR6ö×WFR6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ%&ôuRåG&ç7–ÆW""Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ&ôuRG&ç7–ÆW"6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ$÷VäföçE6†'"Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ÷VäföçE6†'6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂ%7F$–ÖvU6†'"Â&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ7F$–ÖvU6†'6¶vRFWVæFVæ7’"“°¢76W'D6öçF–ç2†FW4§6öâÂFVfVÇD—FV×4Æ–'&'”76VÖ&Ç”æÖRÂ&W‡FW&æÂ4D²FVfVÇBÖ—FVÒ&VfW&Væ6VBÆ–'&'’FWVæFVæ7’"“°¢Ğ ¢&—fFR7FF–27G&–ærvWD†÷7Df–ÆTæÖR‡7G&–ær76VÖ&Ç”æÖR¢°¢&WGW&â÷W&F–æu7—7FVÒä—5v–æF÷w2‚¢ò76VÖ&Ç”æÖR²"æW†R ¢¢76VÖ&Ç”æÖS°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFT÷WGWD76VÖ&Ç”ÖF6†W4Æö6Å6¶vR€¢7G&–ær÷WGWE&ö÷BÀ¢7G&–ær6¶vTfVVBÀ¢7G&–ær6¶vT–BÀ¢7G&–ær76VÖ&Ç•6–×ÆTæÖRÀ¢7G&–ærF&vWDg&ÖWv÷&²¢°¢7G&–ær÷WGWEF‚ÒF‚ä6öÖ&–æR†÷WGWE&ö÷BÂ76VÖ&Ç•6–×ÆTæÖR²"æFÆÂ"“°¢7G&–ær6¶vUfW'6–öâÒvWE6¶vUfW'6–öâ‡6¶vT–B“°¢7G&–ær6¶vUF‚ÒF‚ä6öÖ&–æR‡6¶vTfVVBÂB'·6¶vT–GÒç·6¶vUfW'6–öçÒæçW¶r"“°¢7G&–ær6¶vTVçG'”æÖRÒB&Æ–"÷·F&vWDg&ÖWv÷&·Ò÷¶76VÖ&Ç•6–×ÆTæÖWÒæFÆÂ#° ¢&WV—&Tf–ÆR†÷WGWEF‚ÂB&W‡FW&æÂ4D²÷WGWB76WBw¶76VÖ&Ç•6–×ÆTæÖWÒæFÆÂr"“°¢&WV—&Tf–ÆR‡6¶vUF‚ÂB'·6¶vT–GÒÆö6Â6¶vR"“° ¢W6–ær¦—&6†—fR6¶vRÒ¦—f–ÆRä÷Vå&VB‡6¶vUF‚“°¢¦—&6†—fTVçG'’VçG'’Ò&WV—&U6¶vTVçG'’€¢6¶vRÀ¢6¶vTVçG'”æÖRÀ¢B'·6¶vT–GÒ÷¶76VÖ&Ç•6–×ÆTæÖWÒ'VçF–ÖR76VÖ&Ç’"“° ¢W6–ær7G&VÒ6¶vU7G&VÒÒVçG'’ä÷Vâ‚“°¢7G&–ær6¶vT†6‚Ò6ö×WFU7G&VÕ6†#Sb‡6¶vU7G&VÒ“°¢7G&–ær÷WGWD†6‚Ò6ö×WFTf–ÆU6†#Sb†÷WGWEF‚“°¢76W'DWVÂ€¢6¶vT†6‚À¢÷WGWD†6‚À¢B&W‡FW&æÂ4D²÷WGWB¶76VÖ&Ç•6–×ÆTæÖWÒæFÆÂÖF6†W2Æö6Â·6¶vT–GÒ6¶vR"“°¢Ğ ¢&—fFR7FF–2fö–BfÆ–FFU&ôwT†”G•&VæFW%7W&f6R‡7G&–ær÷WGWE&ö÷B¢°¢f"ÆöD6öçFW‡BÒæWr76VÖ&Ç”ÆöD6öçFW‡B‚%&ôuRubW‡FW&æÂ4D²÷WGWBfÆ–FF–öâ"Â—46öÆÆV7F–&ÆS¢G'VR“°¢ÆöD6öçFW‡Bå&W6öÇf–ær³Ò…òÂ76VÖ&Ç”æÖR’Óà¢°¢7G&–æsò76VÖ&Ç”æÖUFW‡BÒ76VÖ&Ç”æÖRäæÖS°¢–b‡7G&–ærä—4çVÆÄ÷$V×G’†76VÖ&Ç”æÖUFW‡B’¢°¢&WGW&âçVÆÃ°¢Ğ ¢7G&–ær6æF–FFRÒF‚ä6öÖ&–æR†÷WGWE&ö÷BÂ76VÖ&Ç”æÖUFW‡B²"æFÆÂ"“°¢&WGW&âf–ÆRäW†—7G2†6æF–FFR’òÆöD6öçFW‡BäÆöDg&öÔ76VÖ&Ç•F‚†6æF–FFR’¢çVÆÃ°¢Ó°¢ÆöD6öçFW‡Bå&W6öÇf–æuVæÖævVDFÆÂ³Ò…òÂVæÖævVDFÆÄæÖR’Óà¢°¢f÷&V6‚‡7G&–ær6æF–FFR–âvWEVæÖævVDFÆÄ6æF–FFW2‡VæÖævVDFÆÄæÖR’¢°¢7G&–ærF‚ÒF‚ä6öÖ&–æR†÷WGWE&ö÷BÂ6æF–FFR“°¢–b„f–ÆRäW†—7G2‡F‚’bbæF—fTÆ–'&'’åG'”ÆöB‡F‚Â÷WB–çEG"†æFÆR’¢°¢&WGW&â†æFÆS°¢Ğ¢Ğ ¢&WGW&â–çEG"å¦W&ó°¢Ó° ¢G'¢°¢76VÖ&Ç’&ôwUwbÒÆöD6öçFW‡BäÆöDg&öÔ76VÖ&Ç•F‚…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%&ôuRåwbæFÆÂ"’“°¢76VÖ&Ç’&ôwT&6¶VæBÒÆöD6öçFW‡BäÆöDg&öÔ76VÖ&Ç•F‚…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%&ôuRä&6¶VæBæFÆÂ"’“°¢76VÖ&Ç’&ôwU66VæRÒÆöD6öçFW‡BäÆöDg&öÔ76VÖ&Ç•F‚…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%&ôuRå66VæRæFÆÂ"’“°¢76VÖ&Ç’&ôwUfV7F÷"ÒÆöD6öçFW‡BäÆöDg&öÔ76VÖ&Ç•F‚…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%&ôuRåfV7F÷"æFÆÂ"’“°¢76VÖ&Ç’6–Æ´æWDÖF‡2ÒÆöD6öçFW‡BäÆöDg&öÔ76VÖ&Ç•F‚…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%6–Æ²ääUBäÖF‡2æFÆÂ"’“°¢76VÖ&Ç’6–Æ´æWEvV$wRÒÆöD6öçFW‡BäÆöDg&öÔ76VÖ&Ç•F‚…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%6–Æ²ääUBåvV$uRæFÆÂ"’“°¢76VÖ&Ç’v–æF÷w4&6RÒÆöD6öçFW‡BäÆöDg&öÔ76VÖ&Ç•F‚…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%v–æF÷w4&6RæFÆÂ"’“°¢76VÖ&Ç’&W6VçFF–öä6÷&RÒÆöD6öçFW‡BäÆöDg&öÔ76VÖ&Ç•F‚…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%&W6VçFF–öä6÷&RæFÆÂ"’“°¢76VÖ&Ç’&W6VçFF–öäg&ÖWv÷&²ÒÆöD6öçFW‡BäÆöDg&öÔ76VÖ&Ç•F‚…F‚ä6öÖ&–æR†÷WGWE&ö÷BÂ%&W6VçFF–öäg&ÖWv÷&²æFÆÂ"’“° ¢G—RF—7Æ•66ÆU&W6öÇfW%G—RÒvWE&WV—&VEG—R‡&ôwT&6¶VæBÂ%&ôuRä&6¶VæBäF—7Æ•66ÆU&W6öÇfW""“°¢76W'DF—7Æ•66ÆU&W6öÇfW"†F—7Æ•66ÆU&W6öÇfW%G—RÂ&W‡FW&æÂ4D²"“° ¢G—Rv–æF÷t†÷7EG—RÒvWE&WV—&VEG—R‡&ôwUwbÂ%7—7FVÒåv–æF÷w2äÖVF–å&ôuRå&ôwUwev–æF÷t†÷7B"“°¢76W'E6¶vVE&WF–æ7F'GW&W6—¦T¶VW4Æöv–6Å7W&f6R€¢v–æF÷t†÷7EG—RÀ¢F—7Æ•66ÆU&W6öÇfW%G—RÀ¢6–Æ´æWDÖF‡2À¢&W‡FW&æÂ4D²"“°¢76W'E&÷W'G•G—R‡v–æF÷t†÷7EG—RÂ%v–GF‚"ÂG—Vöb†–çB’Â&W‡FW&æÂ4D²&ôuRub†÷7BÆöv–6Âv–GF‚&÷W'G’"“°¢76W'E&÷W'G•G—R‡v–æF÷t†÷7EG—RÂ$†V–v‡B"ÂG—Vöb†–çB’Â&W‡FW&æÂ4D²&ôuRub†÷7BÆöv–6Â†V–v‡B&÷W'G’"“°¢76W'E&÷W'G•G—R‡v–æF÷t†÷7EG—RÂ$ÆVgB"ÂG—Vöb†–çCò’Â&W‡FW&æÂ4D²&ôuRub†÷7BÆVgB&÷W'G’"“°¢76W'E&÷W'G•G—R‡v–æF÷t†÷7EG—RÂ%F÷"ÂG—Vöb†–çCò’Â&W‡FW&æÂ4D²&ôuRub†÷7BF÷&÷W'G’"“°¢76W'E&÷W'G•G—R‡v–æF÷t†÷7EG—RÂ%F÷Ö÷7B"ÂG—Vöb†&ööÂ’Â&W‡FW&æÂ4D²&ôuRub†÷7BF÷Ö÷7B&÷W'G’"“°¢G—Rv–æF÷t&÷&FW%G—RÒvWE&WV—&VEG—R‡&ôwUwbÂ%7—7FVÒåv–æF÷w2äÖVF–å&ôuRå&ôwUwev–æF÷t&÷&FW""“°¢76W'E&÷W'G•G—R‡v–æF÷t†÷7EG—RÂ%v–æF÷t&÷&FW""Âv–æF÷t&÷&FW%G—RÂ&W‡FW&æÂ4D²&ôuRub†÷7Bv–æF÷r&÷&FW"&÷W'G’"“° ¢ÖWF†öD–æfò6WD6Æ–VçE6—¦RÒv–æF÷t†÷7EG—RävWDÖWF†öB€¢%6WD6Æ–VçE6—¦R"À¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·G—Vöb†–çB’ÂG—Vöb†–çB•ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡v–æF÷t†÷7EG—RägVÆÄæÖRÂ%6WD6Æ–VçE6—¦R"“°¢76W'DWVÂƒ"Â6WD6Æ–VçE6—¦RävWE&ÖWFW'2‚’äÆVæwF‚Â&W‡FW&æÂ4D²&ôuRub†÷7B6Æ–VçB×6—¦RÖWF†öB&ÖWFW"6÷VçB"“° ¢ÖWF†öD–æfò6WE÷6—F–öâÒv–æF÷t†÷7EG—RävWDÖWF†öB€¢%6WE÷6—F–öâ"À¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·G—Vöb†–çB’ÂG—Vöb†–çB•ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡v–æF÷t†÷7EG—RägVÆÄæÖRÂ%6WE÷6—F–öâ"“°¢76W'DWVÂƒ"Â6WE÷6—F–öâävWE&ÖWFW'2‚’äÆVæwF‚Â&W‡FW&æÂ4D²&ôuRub†÷7B÷6—F–öâÖWF†öB&ÖWFW"6÷VçB"“° ¢ÖWF†öD–æfò6WEF÷Ö÷7BÒv–æF÷t†÷7EG—RävWDÖWF†öB€¢%6WEF÷Ö÷7B"À¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·G—Vöb†&ööÂ•ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡v–æF÷t†÷7EG—RägVÆÄæÖRÂ%6WEF÷Ö÷7B"“°¢76W'DWVÂƒÂ6WEF÷Ö÷7BävWE&ÖWFW'2‚’äÆVæwF‚Â&W‡FW&æÂ4D²&ôuRub†÷7BF÷Ö÷7BÖWF†öB&ÖWFW"6÷VçB"“° ¢ÖWF†öD–æfò6WEv–æF÷t&÷&FW"Òv–æF÷t†÷7EG—RävWDÖWF†öB€¢%6WEv–æF÷t&÷&FW""À¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·v–æF÷t&÷&FW%G—UÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡v–æF÷t†÷7EG—RägVÆÄæÖRÂ%6WEv–æF÷t&÷&FW""“°¢76W'DWVÂƒÂ6WEv–æF÷t&÷&FW"ävWE&ÖWFW'2‚’äÆVæwF‚Â&W‡FW&æÂ4D²&ôuRub†÷7Bv–æF÷r&÷&FW"ÖWF†öB&ÖWFW"6÷VçB"“° ¢G—R÷'F&ÆU&W6VçFF–öå6÷W&6UG—RÒvWE&WV—&VEG—R‡&W6VçFF–öä6÷&RÂ%7—7FVÒåv–æF÷w2å÷'F&ÆU&W6VçFF–öå6÷W&6R"“°¢ÖWF†öD–æfò6WE÷'F&ÆT6Æ–VçE6—¦RÒ÷'F&ÆU&W6VçFF–öå6÷W&6UG—RävWDÖWF†öB€¢%6WD6Æ–VçE6—¦R"À¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2äæöåV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·G—Vöb†F÷V&ÆR’ÂG—Vöb†F÷V&ÆR•ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡÷'F&ÆU&W6VçFF–öå6÷W&6UG—RägVÆÄæÖRÂ%6WD6Æ–VçE6—¦R"“°¢76W'DWVÂ‡G—Vöb‡fö–B’Â6WE÷'F&ÆT6Æ–VçE6—¦Rå&WGW&åG—RÂ&W‡FW&æÂ4D²÷'F&ÆR&W6VçFF–öâ6÷W&6R6Æ–VçB×6—¦R&WGW&âG—R"“° ¢G—R÷'F&ÆT7F—fF–öåG—RÒvWE&WV—&VEG—R‡&W6VçFF–öäg&ÖWv÷&²Â%7—7FVÒåv–æF÷w2å÷'F&ÆUv–æF÷t7F—fF–öå6W'f–6R"“°¢ÖWF†öD–æfò6WE÷'F&ÆU÷6—F–öâÒ÷'F&ÆT7F—fF–öåG—RävWDÖWF†öB€¢%6WE÷6—F–öâ"À¢&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2äæöåV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·G—Vöb†ö&¦V7B’ÂG—Vöb†F÷V&ÆR’ÂG—Vöb†F÷V&ÆR•ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡÷'F&ÆT7F—fF–öåG—RägVÆÄæÖRÂ%6WE÷6—F–öâ"“°¢76W'DWVÂ‡G—Vöb‡fö–B’Â6WE÷'F&ÆU÷6—F–öâå&WGW&åG—RÂ&W‡FW&æÂ4D²÷'F&ÆRv–æF÷r7F—fF–öâ÷6—F–öâ&WGW&âG—R"“° ¢ÖWF†öD–æfò6WE÷'F&ÆUF÷Ö÷7BÒ÷'F&ÆT7F—fF–öåG—RävWDÖWF†öB€¢%6WEF÷Ö÷7B"À¢&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2äæöåV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·G—Vöb†ö&¦V7B’ÂG—Vöb†&ööÂ•ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡÷'F&ÆT7F—fF–öåG—RägVÆÄæÖRÂ%6WEF÷Ö÷7B"“°¢76W'DWVÂ‡G—Vöb‡fö–B’Â6WE÷'F&ÆUF÷Ö÷7Bå&WGW&åG—RÂ&W‡FW&æÂ4D²÷'F&ÆRv–æF÷r7F—fF–öâF÷Ö÷7B&WGW&âG—R"“° ¢G—R6ö×÷6—F–öåF&vWEG—RÒvWE&WV—&VEG—R‡&ôwUwbÂ%7—7FVÒåv–æF÷w2äÖVF–å&ôuRå&ôwUwd6ö×÷6—F–öåF&vWB"“°¢G—R&VæFW%F&vWEf–Ww÷'EG—RÒvWE&WV—&VEG—R‡&ôwU66VæRÂ%&ôuRå66VæRå&VæFW%F&vWEf–Ww÷'B"“°¢ÖWF†öD–æfò6ö×÷6—F–öå&VæFW"Òf–æDÖWF†öD'•&ÖWFW$æÖW2€¢6ö×÷6—F–öåF&vWEG—RÀ¢%&VæFW""À¢²&Æöv–6Åv–GF‚"Â&Æöv–6Ä†V–v‡B"Â'—†VÅv–GF‚"Â'—†VÄ†V–v‡B"Â&G•66ÆR"Â'F&vWEf–Wr%Ò“°¢76W'E&ÖWFW%G—W2€¢6ö×÷6—F–öå&VæFW"À¢·G—Vöb‡V–çB’ÂG—Vöb‡V–çB’ÂG—Vöb‡V–çB’ÂG—Vöb‡V–çB’ÂG—Vöb†fÆöB•ÒÀ¢&W‡FW&æÂ4D²&ôuRub6ö×÷6—F–öâ&VæFW"Æöv–6Â÷‡—6–6Â7W&f6R"“°¢76W'DWVÂ‡G'VRÂ6ö×÷6—F–öå&VæFW"ävWE&ÖWFW'2‚•³UÒå&ÖWFW%G—Rä—5ö–çFW"Â&W‡FW&æÂ4D²&ôuRub6ö×÷6—F–öâ&VæFW"F&vWBf–Wrö–çFW""“°¢ÖWF†öD–æfò6ö×÷6—F–öåf–Ww÷'E&VæFW"Òf–æDÖWF†öD'•&ÖWFW$æÖW2€¢6ö×÷6—F–öåF&vWEG—RÀ¢%&VæFW""À¢²&Æöv–6Åv–GF‚"Â&Æöv–6Ä†V–v‡B"Â'—†VÅv–GF‚"Â'—†VÄ†V–v‡B"Â'&VæFW%F&vWEf–Ww÷'B"Â&G•66ÆR"Â'F&vWEf–Wr%Ò“°¢76W'E&ÖWFW%G—W2€¢6ö×÷6—F–öåf–Ww÷'E&VæFW"À¢·G—Vöb‡V–çB’ÂG—Vöb‡V–çB’ÂG—Vöb‡V–çB’ÂG—Vöb‡V–çB’Â&VæFW%F&vWEf–Ww÷'EG—RÂG—Vöb†fÆöB•ÒÀ¢&W‡FW&æÂ4D²&ôuRub6ö×÷6—F–öâ&VæFW"f–Ww÷'B7W&f6R"“°¢76W'DWVÂ‡G'VRÂ6ö×÷6—F–öåf–Ww÷'E&VæFW"ävWE&ÖWFW'2‚•³eÒå&ÖWFW%G—Rä—5ö–çFW"Â&W‡FW&æÂ4D²&ôuRub6ö×÷6—F–öâ&VæFW"f–Ww÷'BF&vWBf–Wrö–çFW""“°¢ÖWF†öD–æfò†÷7E&W6VçBÒf–æDÖWF†öD'•&ÖWFW$æÖW2€¢v–æF÷t†÷7EG—RÀ¢%&W6VçB"À¢²&Æöv–6Åv–GF‚"Â&Æöv–6Ä†V–v‡B"Â'—†VÅv–GF‚"Â'—†VÄ†V–v‡B"Â'f–Ww÷'E‚"Â'f–Ww÷'E’"Â'f–Ww÷'Ev–GF‚"Â'f–Ww÷'D†V–v‡B"Â&G•66ÆR%Ò“°¢76W'DÖWF†öD6ÆÇ57V6–f–4ÖWF†öB€¢†÷7E&W6VçBÀ¢6ö×÷6—F–öåf–Ww÷'E&VæFW"À¢&W‡FW&æÂ4D²&ôuRub†÷7B&W6VçBf–Ww÷'B&VæFW"÷fW&ÆöB"“°¢ÖWF†öD–æfò7–æ6‡&öæ—¦TvVöÖWG'’Òf–æDÖWF†öD'•&ÖWFW$æÖW2€¢v–æF÷t†÷7EG—RÀ¢%7–æ6‡&öæ—¦U÷'F&ÆU&W6VçFF–öå6÷W&6TvVöÖWG'’"À¢²&vVöÖWG'’%Ò“°¢76W'DÖWF†öD6ÆÇ4ÖWF†öB€¢7–æ6‡&öæ—¦TvVöÖWG'’À¢v–æF÷t†÷7EG—RägVÆÄæÖRóò7G&–æräV×G’À¢%WFFU÷'F&ÆU&W6VçFF–öå6÷W&6T6Æ–VçE6—¦R"À¢&W‡FW&æÂ4D²&ôuRub†÷7B÷'F&ÆR6÷W&6RÆöv–6Â×6—¦R7–æ6‡&öæ—¦F–öâ"“°¢76W'DÖWF†öD6ÆÇ4ÖWF†öB€¢7–æ6‡&öæ—¦TvVöÖWG'’À¢v–æF÷t†÷7EG—RägVÆÄæÖRóò7G&–æräV×G’À¢%WFFU÷'F&ÆU&W6VçFF–öå6÷W&6TG•66ÆR"À¢&W‡FW&æÂ4D²&ôuRub†÷7B÷'F&ÆR6÷W&6RE’7–æ6‡&öæ—¦F–öâ"“°¢ÖWF†öD–æfò&W6öÇfT66†VDÆöv–6ÄF–ÖVç6–öâÒf–æDÖWF†öD'•&ÖWFW$æÖW2€¢v–æF÷t†÷7EG—RÀ¢%&W6öÇfT66†VDÆöv–6Ä6Æ–VçDF–ÖVç6–öâ"À¢²'÷'F&ÆU&W6VçFF–öå6÷W&6TF–ÖVç6–öâ"Â'&WVW7FVDÆöv–6ÄF–ÖVç6–öâ"Â&7W'&VçD6Æ–VçDF–ÖVç6–öâ%Ò“°¢76W'DWVÂ€¢G—Vöb†–çB’À¢&W6öÇfT66†VDÆöv–6ÄF–ÖVç6–öâå&WGW&åG—RÀ¢&W‡FW&æÂ4D²&ôuRub†÷7BG—VBÆöv–6Â×6—¦R66†R&WGW&âG—R"“°¢ÖWF†öD–æfò&W6öÇfTÖöæ—F÷$G•66ÆRÒf–æDÖWF†öD'•&ÖWFW$æÖW2€¢v–æF÷t†÷7EG—RÀ¢%&W6öÇfTÖöæ—F÷$G•66ÆUv—F…ÆFf÷&ÔfÆÆ&6²"À¢²&Ööæ—F÷$G•66ÆR"Â'ÆFf÷&ÔG•66ÆU&÷f–FW"%Ò“°¢76W'DÖWF†öD6ÆÇ4ÖWF†öB€¢&W6öÇfTÖöæ—F÷$G•66ÆRÀ¢F—7Æ•66ÆU&W6öÇfW%G—RägVÆÄæÖRóò7G&–æräV×G’À¢%&W6öÇfTF—7Æ•66ÆUv—F…ÆFf÷&ÔfÆÆ&6²"À¢&W‡FW&æÂ4D²&ôuRub†÷7BFVÆVvFW2F—7Æ’×66ÆRfÆÆ&6²Fò&ôuR&6¶VæB"“° ¢G—R6ö×÷6—F÷%G—RÒvWE&WV—&VEG—R‡&ôwU66VæRÂ%&ôuRå66VæRä6ö×÷6—F÷""“°¢G—Rf—7VÅG—RÒvWE&WV—&VEG—R‡&ôwU66VæRÂ%&ôuRå66VæRåf—7VÂ"“°¢ÖWF†öD–æfò6ö×÷6—F÷%&VæFW%66VæRÒf–æDÖWF†öD'•&ÖWFW$æÖW2€¢6ö×÷6—F÷%G—RÀ¢%&VæFW%66VæR"À¢²'&ö÷B"Â&Æöv–6Åv–GF‚"Â&Æöv–6Ä†V–v‡B"Â'&VæFW%F&vWEv–GF‚"Â'&VæFW%F&vWD†V–v‡B"Â&G•66ÆR"Â'F&vWEf–Wr%Ò“°¢76W'E&ÖWFW%G—W2€¢6ö×÷6—F÷%&VæFW%66VæRÀ¢·f—7VÅG—RÂG—Vöb‡V–çB’ÂG—Vöb‡V–çB’ÂG—Vöb‡V–çB’ÂG—Vöb‡V–çB’ÂG—Vöb†fÆöB•ÒÀ¢&W‡FW&æÂ4D²&ôuR6ö×÷6—F÷"&VæFW"Æöv–6Â÷‡—6–6Â7W&f6R"“°¢76W'DWVÂ‡G'VRÂ6ö×÷6—F÷%&VæFW%66VæRävWE&ÖWFW'2‚•³eÒå&ÖWFW%G—Rä—5ö–çFW"Â&W‡FW&æÂ4D²&ôuR6ö×÷6—F÷"&VæFW"F&vWBf–Wrö–çFW""“°¢ÖWF†öD–æfò6ö×÷6—F÷%f–Ww÷'E&VæFW%66VæRÒf–æDÖWF†öD'•&ÖWFW$æÖW2€¢6ö×÷6—F÷%G—RÀ¢%&VæFW%66VæR"À¢²'&ö÷B"Â&Æöv–6Åv–GF‚"Â&Æöv–6Ä†V–v‡B"Â'&VæFW%F&vWEv–GF‚"Â'&VæFW%F&vWD†V–v‡B"Â'&VæFW%F&vWEf–Ww÷'B"Â&G•66ÆR"Â'F&vWEf–Wr%Ò“°¢76W'E&ÖWFW%G—W2€¢6ö×÷6—F÷%f–Ww÷'E&VæFW%66VæRÀ¢·f—7VÅG—RÂG—Vöb‡V–çB’ÂG—Vöb‡V–çB’ÂG—Vöb‡V–çB’ÂG—Vöb‡V–çB’Â&VæFW%F&vWEf–Ww÷'EG—RÂG—Vöb†fÆöB•ÒÀ¢&W‡FW&æÂ4D²&ôuR6ö×÷6—F÷"&VæFW"f–Ww÷'B7W&f6R"“°¢76W'DWVÂ‡G'VRÂ6ö×÷6—F÷%f–Ww÷'E&VæFW%66VæRävWE&ÖWFW'2‚•³uÒå&ÖWFW%G—Rä—5ö–çFW"Â&W‡FW&æÂ4D²&ôuR6ö×÷6—F÷"&VæFW"f–Ww÷'BF&vWBf–Wrö–çFW""“°¢76W'DÖWF†öD6ÆÇ57V6–f–4ÖWF†öB€¢6ö×÷6—F–öå&VæFW"À¢6ö×÷6—F–öåf–Ww÷'E&VæFW"À¢&W‡FW&æÂ4D²&ôuRub6ö×÷6—F–öâ&VæFW"FVÆVvFW2Fòf–Ww÷'B&VæFW"7W&f6R"“°¢76W'DÖWF†öD6ÆÇ57V6–f–4ÖWF†öB€¢6ö×÷6—F–öåf–Ww÷'E&VæFW"À¢6ö×÷6—F÷%f–Ww÷'E&VæFW%66VæRÀ¢&W‡FW&æÂ4D²&ôuRub6ö×÷6—F–öâF&vWBf÷'v&G2f–Ww÷'B&VæFW"7W&f6R"“°¢76W'E&÷W'G”vWGFW%&VfW&Væ6W4f–VÆB€¢6ö×÷6—F÷%G—RÀ¢$7W'&VçD6çf5—†VÅ‚"À¢%öW‡Æ–6—E&VæFW%F&vWEf–Ww÷'B"À¢&W‡FW&æÂ4D²&ôuR6ö×÷6—F÷"6çf2—†VÂ‚f–Ww÷'B÷&–v–â"“°¢76W'E&÷W'G”vWGFW%&VfW&Væ6W4f–VÆB€¢6ö×÷6—F÷%G—RÀ¢$7W'&VçD6çf5—†VÅ’"À¢%öW‡Æ–6—E&VæFW%F&vWEf–Ww÷'B"À¢&W‡FW&æÂ4D²&ôuR6ö×÷6—F÷"6çf2—†VÂ’f–Ww÷'B÷&–v–â"“°¢76W'E&÷W'G”vWGFW%&VfW&Væ6W4f–VÆB€¢6ö×÷6—F÷%G—RÀ¢$7W'&VçD6çf5—†VÅv–GF‚"À¢%öW‡Æ–6—E&VæFW%F&vWEv–GF‚"À¢&W‡FW&æÂ4D²&ôuR6ö×÷6—F÷"6çf2—†VÂv–GF‚W‡Æ–6—B&VæFW"F&vWB"“°¢76W'E&÷W'G”vWGFW%&VfW&Væ6W4f–VÆB€¢6ö×÷6—F÷%G—RÀ¢$7W'&VçD6çf5—†VÄ†V–v‡B"À¢%öW‡Æ–6—E&VæFW%F&vWD†V–v‡B"À¢&W‡FW&æÂ4D²&ôuR6ö×÷6—F÷"6çf2—†VÂ†V–v‡BW‡Æ–6—B&VæFW"F&vWB"“°¢ÖWF†öD–æfò6ö×÷6—F÷%‡—6–6Å&VæFW%66VæRÒf–æDÖWF†öD'•&ÖWFW$æÖW2€¢6ö×÷6—F÷%G—RÀ¢%&VæFW%66VæR"À¢²'&ö÷B"Â'v–GF‚"Â&†V–v‡B"Â'F&vWEf–Wr%Ò“°¢ÖWF†öD–æfò6ö×÷6—F÷%‡—6–6Å&VæFW%66VæT6÷&RÒf–æDÖWF†öD'•&ÖWFW$æÖW2€¢6ö×÷6—F÷%G—RÀ¢%&VæFW%66VæT6÷&R"À¢²'&ö÷B"Â'v–GF‚"Â&†V–v‡B"Â'F&vWEf–Wr%Ò“°¢ÖWF†öD–æfòÇ•&VæFW%75f–Ww÷'BÒf–æDÖWF†öD'”æÖTæE&ÖWFW$6÷VçB€¢6ö×÷6—F÷%G—RÀ¢$Ç•&VæFW%75f–Ww÷'B"À¢B“°¢76W'DÖWF†öD6ÆÇ57V6–f–4ÖWF†öB€¢6ö×÷6—F÷%‡—6–6Å&VæFW%66VæRÀ¢6ö×÷6—F÷%‡—6–6Å&VæFW%66VæT6÷&RÀ¢&W‡FW&æÂ4D²&ôuR6ö×÷6—F÷"‡—6–6Â&VæFW"FVÆVvFW2FòF†R&WG'–&ÆR&VæFW"6÷&R"“°¢76W'DÖWF†öD6ÆÇ4ÖWF†öB€¢6ö×÷6—F÷%‡—6–6Å&VæFW%66VæT6÷&RÀ¢6ö×÷6—F÷%G—RägVÆÄæÖRóò7G&–æräV×G’À¢$Ç•&VæFW%75f–Ww÷'B"À¢&W‡FW&æÂ4D²&ôuR6ö×÷6—F÷"&VæFW"72f–Ww÷'BÆ–6F–öâ"“°¢76W'DÖWF†öD6ÆÇ4ÖWF†öB€¢Ç•&VæFW%75f–Ww÷'BÀ¢%&ôuRä&6¶VæBä•vV$wT’"À¢%&VæFW%74Væ6öFW%6WEf–Ww÷'B"À¢&W‡FW&æÂ4D²&ôuR6ö×÷6—F÷"&6¶VæBÖ–æFWVæFVçB‡—6–6Â&VæFW"F&vWBf–Ww÷'B"“°¢76W'E&WF–æVEwdÆ–W%W6W4Æöv–6Ä&÷VæG4æD–FVçF—G•66ÆR‡&ôwUwbÂ&ôwU66VæRÂ&W‡FW&æÂ4D²"“°¢76W'E6¶vVD†–v„G•&WF–æVEwe—†VÇ4f–ÆÅ‡—6–6ÅF&vWB€¢÷WGWE&ö÷BÀ¢&ôwUwbÀ¢&ôwT&6¶VæBÀ¢&W6VçFF–öä6÷&RÀ¢&W6VçFF–öäg&ÖWv÷&²À¢v–æF÷w4&6RÀ¢6–Æ´æWEvV$wRÀ¢&W‡FW&æÂ4D²"“°¢76W'E6¶vVDö&¦V7E&VæFW$FF&V7FævÆTf–ÆÇ5‡—6–6ÅF&vWB€¢÷WGWE&ö÷BÀ¢&ôwUwbÀ¢&ôwT&6¶VæBÀ¢&W6VçFF–öä6÷&RÀ¢v–æF÷w4&6RÀ¢6–Æ´æWEvV$wRÀ¢&W‡FW&æÂ4D²"“°¢Ğ¢f–æÆÇ¢°¢ÆöD6öçFW‡BåVæÆöB‚“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B76W'DF—7Æ•66ÆU&W6öÇfW"…G—RF—7Æ•66ÆU&W6öÇfW%G—RÂ7G&–ærFW67&—F–öå&Vf—‚¢°¢ÖWF†öD–æfò&W6öÇfUv–æF÷tF—7Æ•66ÆRÒf–æDÖWF†öD'•&ÖWFW$æÖW2€¢F—7Æ•66ÆU&W6öÇfW%G—RÀ¢%&W6öÇfUv–æF÷tF—7Æ•66ÆR"À¢²'v–æF÷r"Â&Ööæ—F÷$G•66ÆR%Ò“°¢76W'DWVÂ‡G—Vöb†F÷V&ÆR’Â&W6öÇfUv–æF÷tF—7Æ•66ÆRå&WGW&åG—RÂB'¶FW67&—F–öå&Vf—‡Ò&ôuR&6¶VæBv–æF÷rF—7Æ’×66ÆR&WGW&âG—R"“°¢76W'DWVÂƒ"Â&W6öÇfUv–æF÷tF—7Æ•66ÆRävWE&ÖWFW'2‚’äÆVæwF‚ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuR&6¶VæBv–æF÷rF—7Æ’×66ÆR&ÖWFW"6÷VçB"“° ¢ÖWF†öD–æfòæ÷&ÖÆ—¦TF—7Æ•66ÆRÒF—7Æ•66ÆU&W6öÇfW%G—RävWDÖWF†öB€¢$æ÷&ÖÆ—¦TF—7Æ•66ÆR"À¢&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2åV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·G—Vöb†F÷V&ÆR•ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ†F—7Æ•66ÆU&W6öÇfW%G—RägVÆÄæÖRÂ$æ÷&ÖÆ—¦TF—7Æ•66ÆR"“°¢76W'DWVÂƒãÂ–çfö¶U&WV—&VB†æ÷&ÖÆ—¦TF—7Æ•66ÆRÂ³ãÒ’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuR&6¶VæB–çfÆ–BF—7Æ’×66ÆRæ÷&ÖÆ—¦F–öâ"“°¢76W'DWVÂƒãRÂ–çfö¶U&WV—&VB†æ÷&ÖÆ—¦TF—7Æ•66ÆRÂ³ãUÒ’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuR&6¶VæBfÆ–BF—7Æ’×66ÆRæ÷&ÖÆ—¦F–öâ"“° ¢ÖWF†öD–æfò&W6öÇfTF—7Æ•66ÆUv—F…ÆFf÷&ÔfÆÆ&6²ÒF—7Æ•66ÆU&W6öÇfW%G—RävWDÖWF†öB€¢%&W6öÇfTF—7Æ•66ÆUv—F…ÆFf÷&ÔfÆÆ&6²"À¢&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2åV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·G—Vöb†F÷V&ÆR’ÂG—Vöb„gVæ3ÆF÷V&ÆSóâ•ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ†F—7Æ•66ÆU&W6öÇfW%G—RägVÆÄæÖRÂ%&W6öÇfTF—7Æ•66ÆUv—F…ÆFf÷&ÔfÆÆ&6²"“°¢76W'DWVÂ€¢"ãÀ¢–çfö¶U&WV—&VB‡&W6öÇfTF—7Æ•66ÆUv—F…ÆFf÷&ÔfÆÆ&6²Â³ãÂæWrgVæ3ÆF÷V&ÆSóâ‚‚’Óâ"ã•Ò’À¢B'¶FW67&—F–öå&Vf—‡Ò&ôuR&6¶VæBæF—fRF—7Æ’×66ÆRfÆÆ&6²"“°¢76W'DWVÂ€¢ãRÀ¢–çfö¶U&WV—&VB‡&W6öÇfTF—7Æ•66ÆUv—F…ÆFf÷&ÔfÆÆ&6²Â³ãRÂæWrgVæ3ÆF÷V&ÆSóâ‚‚’Óâ"ã•Ò’À¢B'¶FW67&—F–öå&Vf—‡Ò&ôuR&6¶VæBÖöæ—F÷"F—7Æ’×66ÆR&V6VFVæ6R"“°¢Ğ ¢&—fFR7FF–2ö&¦V7B–çfö¶U&WV—&VB„ÖWF†öD–æfòÖWF†öBÂö&¦V7CõµÒ&ÖWFW'2¢°¢&WGW&âÖWF†öBä–çfö¶R†çVÆÂÂ&ÖWFW'2¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$W‡V7FVB¶ÖWF†öBäFV6Æ&–æuG—SòägVÆÄæÖWÒç¶ÖWF†öBäæÖWÒFò&WGW&âfÇVRâ"“°¢Ğ ¢&—fFR7FF–2fö–B76W'E6¶vVE&WF–æ7F'GW&W6—¦T¶VW4Æöv–6Å7W&f6R€¢G—Rv–æF÷t†÷7EG—RÀ¢G—RF—7Æ•66ÆU&W6öÇfW%G—RÀ¢76VÖ&Ç’6–Æ´æWDÖF‡2À¢7G&–ærFW67&—F–öå&Vf—‚¢°¢G—Rv–æF÷t÷F–öç5G—RÒvWE&WV—&VEG—R‡v–æF÷t†÷7EG—Rä76VÖ&Ç’Â%7—7FVÒåv–æF÷w2äÖVF–å&ôuRå&ôwUwev–æF÷t÷F–öç2"“°¢G—RfV7F÷#$D–çEG—RÒvWE&WV—&VEG—R‡6–Æ´æWDÖF‡2Â%6–Æ²ääUBäÖF‡2åfV7F÷#$F"’äÖ¶TvVæW&–5G—R‡G—Vöb†–çB’“°¢ö&¦V7B÷F–öç2Ò7&VFR‡v–æF÷t÷F–öç5G—R“°¢6WE&÷W'G’†÷F–öç2Â%v–GF‚"ÂC#“°¢6WE&÷W'G’†÷F–öç2Â$†V–v‡B"ÂƒC“°¢ö&¦V7B†÷7BÒ7&VFR‡v–æF÷t†÷7EG—RÂ÷F–öç2“° ¢G'¢°¢ÖWF†öD–æfò&W6öÇfTF—7Æ•66ÆRÒF—7Æ•66ÆU&W6öÇfW%G—RävWDÖWF†öB€¢%&W6öÇfTF—7Æ•66ÆUv—F…ÆFf÷&ÔfÆÆ&6²"À¢&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2åV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·G—Vöb†F÷V&ÆR’ÂG—Vöb„gVæ3ÆF÷V&ÆSóâ•ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ†F—7Æ•66ÆU&W6öÇfW%G—RägVÆÄæÖRÂ%&W6öÇfTF—7Æ•66ÆUv—F…ÆFf÷&ÔfÆÆ&6²"“°¢ö&¦V7BG•66ÆRÒ–çfö¶U&WV—&VB‡&W6öÇfTF—7Æ•66ÆRÂ³ãÂæWrgVæ3ÆF÷V&ÆSóâ‚‚’Óâ"ã•Ò“°¢76W'DWVÂƒ"ãÂG•66ÆRÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ7F'GWF—7Æ’66ÆR"“° ¢ö&¦V7BæF—fTÆöv–6Å6—¦RÒ7&VFR‡fV7F÷#$D–çEG—RÂC#ÂƒC“°¢ö&¦V7B&WF–æg&ÖV'VffW%6—¦RÒ7&VFR‡fV7F÷#$D–çEG—RÂƒCÂcƒ“°¢ÖWF†öD–æfòWFFTæF—fU&W6—¦RÒv–æF÷t†÷7EG—RävWDÖWF†öB€¢%WFFT6Æ–VçE6—¦Tg&öÔæF—fU&W6—¦R"À¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2äæöåV&Æ–2Â&–æF–ætfÆw2åV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·fV7F÷#$D–çEG—RÂfV7F÷#$D–çEG—RÂG—Vöb†F÷V&ÆR•ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡v–æF÷t†÷7EG—RägVÆÄæÖRÂ%WFFT6Æ–VçE6—¦Tg&öÔæF—fU&W6—¦R"“°¢–çfö¶TÖWF†öB‡WFFTæF—fU&W6—¦RÂ†÷7BÂæF—fTÆöv–6Å6—¦RÂ&WF–æg&ÖV'VffW%6—¦RÂG•66ÆR“°¢76W'DWVÂƒC#ÂvWE&÷W'G’††÷7BÂ%v–GF‚"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ7F'GWÆöv–6Â†÷7Bv–GF‚"“°¢76W'DWVÂƒƒCÂvWE&÷W'G’††÷7BÂ$†V–v‡B"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ7F'GWÆöv–6Â†÷7B†V–v‡B"“° ¢6WDf–VÆB††÷7BÂ%ö6Æ–VçEv–GF‚"ÂƒC“°¢6WDf–VÆB††÷7BÂ%ö6Æ–VçD†V–v‡B"Âcƒ“°¢6WDf–VÆB††÷7BÂ%÷&WVW7FVDÆöv–6Ä6Æ–VçEv–GF‚"ÂƒC“°¢6WDf–VÆB††÷7BÂ%÷&WVW7FVDÆöv–6Ä6Æ–VçD†V–v‡B"Âcƒ“°¢–çfö¶TÖWF†öB‡WFFTæF—fU&W6—¦RÂ†÷7BÂæF—fTÆöv–6Å6—¦RÂ&WF–æg&ÖV'VffW%6—¦RÂG•66ÆR“°¢76W'DWVÂƒC#ÂvWE&÷W'G’††÷7BÂ%v–GF‚"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æöÆÇWFVBÖ66†RÆöv–6Â†÷7Bv–GF‚"“°¢76W'DWVÂƒƒCÂvWE&÷W'G’††÷7BÂ$†V–v‡B"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æöÆÇWFVBÖ66†RÆöv–6Â†÷7B†V–v‡B"“° ¢ÖWF†öD–æfò&W6öÇfTvVöÖWG'’Òv–æF÷t†÷7EG—RävWDÖWF†öB€¢%&W6öÇfU&VæFW%7W&f6TvVöÖWG'’"À¢&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2äæöåV&Æ–2Â&–æF–ætfÆw2åV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·G—Vöb†–çB’ÂG—Vöb†–çB’ÂfV7F÷#$D–çEG—RÂG—Vöb†F÷V&ÆR•ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡v–æF÷t†÷7EG—RägVÆÄæÖRÂ%&W6öÇfU&VæFW%7W&f6TvVöÖWG'’"“°¢ö&¦V7BvVöÖWG'’Ò–çfö¶TÖWF†öB‡&W6öÇfTvVöÖWG'’ÂçVÆÂÂC#ÂƒCÂ&WF–æg&ÖV'VffW%6—¦RÂG•66ÆR¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ&VæFW"7W&f6RvVöÖWG'’v2çVÆÂâ"“°¢76W'DWVÂƒC#RÂvWE&÷W'G’†vVöÖWG'’Â$Æöv–6Åv–GF‚"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ&VæFW"Æöv–6Âv–GF‚"“°¢76W'DWVÂƒƒCRÂvWE&÷W'G’†vVöÖWG'’Â$Æöv–6Ä†V–v‡B"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ&VæFW"Æöv–6Â†V–v‡B"“°¢76W'DWVÂƒƒCRÂvWE&÷W'G’†vVöÖWG'’Â%—†VÅv–GF‚"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ&VæFW"—†VÂv–GF‚"“°¢76W'DWVÂƒcƒRÂvWE&÷W'G’†vVöÖWG'’Â%—†VÄ†V–v‡B"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ&VæFW"—†VÂ†V–v‡B"“°¢76W'DWVÂƒRÂvWE&÷W'G’†vVöÖWG'’Â%f–Ww÷'E‚"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ&VæFW"f–Ww÷'B‚"“°¢76W'DWVÂƒRÂvWE&÷W'G’†vVöÖWG'’Â%f–Ww÷'E’"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ&VæFW"f–Ww÷'B’"“°¢76W'DWVÂƒƒCRÂvWE&÷W'G’†vVöÖWG'’Â%f–Ww÷'Ev–GF‚"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ&VæFW"f–Ww÷'Bv–GF‚"“°¢76W'DWVÂƒcƒRÂvWE&÷W'G’†vVöÖWG'’Â%f–Ww÷'D†V–v‡B"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ&VæFW"f–Ww÷'B†V–v‡B"“°¢76W'DWVÂƒ"ãÂvWE&÷W'G’†vVöÖWG'’Â$G•66ÆR"’ÂB'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æ&VæFW"E’66ÆR"“°¢Ğ¢f–æÆÇ¢°¢††÷7B2”F—7÷6&ÆR“òäF—7÷6R‚“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B76W'E&WF–æVEwdÆ–W%W6W4Æöv–6Ä&÷VæG4æD–FVçF—G•66ÆR€¢76VÖ&Ç’&ôwUwbÀ¢76VÖ&Ç’&ôwU66VæRÀ¢7G&–ærFW67&—F–öå&Vf—‚¢°¢G—RG&v–ætg&ÖUG—RÒvWE&WV—&VEG—R‡&ôwUwbÂ%7—7FVÒåv–æF÷w2äÖVF–å&ôuRå&ôwUwdG&v–ætg&ÖR"“°¢G—R6öçF–æW%f—7VÅG—RÒvWE&WV—&VEG—R‡&ôwU66VæRÂ%&ôuRå66VæRä6öçF–æW%f—7VÂ"“°¢G—RG&v–æuf—7VÅG—RÒvWE&WV—&VEG—R‡&ôwU66VæRÂ%&ôuRå66VæRäG&v–æuf—7VÂ"“°¢ö&¦V7B66VæU&ö÷BÒ7&VFR†6öçF–æW%f—7VÅG—R“°¢ö&¦V7B&WF–æVE&ö÷BÒ7&VFR†6öçF–æW%f—7VÅG—R“°¢ö&¦V7BfÆE&ö÷BÒ7&VFR†G&v–æuf—7VÅG—R“°¢ö&¦V7Bg&ÖRÒ7&VFR€¢G&v–ætg&ÖUG—RÀ¢66VæU&ö÷BÀ¢&WF–æVE&ö÷BÀ¢fÆE&ö÷BÀ¢ƒCRÀ¢cƒRÀ¢çVÆÂÀ¢çVÆÂÀ¢G'VRÀ¢çVÆÂÀ¢C#RÀ¢ƒCRÀ¢"ãÀ¢"ãÀ¢çVÆÂ“° ¢76W'DWVÂƒC#RÂvWE&÷W'G’†g&ÖRÂ$Æöv–6Åv–GF‚"’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuRubG&v–ærg&ÖRÆöv–6Âv–GF‚"“°¢76W'DWVÂƒƒCRÂvWE&÷W'G’†g&ÖRÂ$Æöv–6Ä†V–v‡B"’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuRubG&v–ærg&ÖRÆöv–6Â†V–v‡B"“°¢76W'DWVÂƒƒCRÂvWE&÷W'G’†g&ÖRÂ%—†VÅv–GF‚"’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuRubG&v–ærg&ÖR—†VÂv–GF‚"“°¢76W'DWVÂƒcƒRÂvWE&÷W'G’†g&ÖRÂ%—†VÄ†V–v‡B"’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuRubG&v–ærg&ÖR—†VÂ†V–v‡B"“°¢76W'DWVÂƒ"ãÂvWE&÷W'G’†g&ÖRÂ$G•66ÆU‚"’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuRubG&v–ærg&ÖRE’66ÆR‚"“°¢76W'DWVÂƒ"ãÂvWE&÷W'G’†g&ÖRÂ$G•66ÆU’"’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuRubG&v–ærg&ÖRE’66ÆR’"“°¢76W'DWVÂ†æWrfV7F÷#"ƒC#bÂƒCb’ÂvWE&÷W'G’‡66VæU&ö÷BÂ%6—¦R"’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuR66VæR&ö÷BÆöv–6Â6—¦R"“°¢76W'DWVÂ†æWrfV7F÷#"ƒC#bÂƒCb’ÂvWE&÷W'G’‡&WF–æVE&ö÷BÂ%6—¦R"’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuR&WF–æVBubÆ–W"Æöv–6Â6—¦R"“°¢76W'DWVÂ†æWrfV7F÷#"ƒC#bÂƒCb’ÂvWE&÷W'G’†fÆE&ö÷BÂ%6—¦R"’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuRfÆBubÆ–W"Æöv–6Â6—¦R"“°¢76W'DWVÂ…fV7F÷#2äöæRÂvWE&÷W'G’‡&WF–æVE&ö÷BÂ%66ÆR"’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuR&WF–æVBubÆ–W"–FVçF—G’66ÆR"“°¢76W'DWVÂ…fV7F÷#"å¦W&òÂvWE&÷W'G’‡&WF–æVE&ö÷BÂ%&VæFW%G&ç6f÷&Ô÷&–v–â"’ÂB'¶FW67&—F–öå&Vf—‡Ò&ôuR&WF–æVBubÆ–W"G&ç6f÷&Ò÷&–v–â"“°¢Ğ ¢&—fFR7FF–2fö–B76W'E6¶vVD†–v„G•&WF–æVEwe—†VÇ4f–ÆÅ‡—6–6ÅF&vWB€¢7G&–æræF—fT76WE&ö÷BÀ¢76VÖ&Ç’&ôwUwbÀ¢76VÖ&Ç’&ôwT&6¶VæBÀ¢76VÖ&Ç’&W6VçFF–öä6÷&RÀ¢76VÖ&Ç’&W6VçFF–öäg&ÖWv÷&²À¢76VÖ&Ç’v–æF÷w4&6RÀ¢76VÖ&Ç’6–Æ´æWEvV$wRÀ¢7G&–ærFW67&—F–öå&Vf—‚¢°¢G—R6ö×÷6—F–öåF&vWEG—RÒvWE&WV—&VEG—R‡&ôwUwbÂ%7—7FVÒåv–æF÷w2äÖVF–å&ôuRå&ôwUwd6ö×÷6—F–öåF&vWB"“°¢G—R&WF–æVE6–æµG—RÒvWE&WV—&VEG—R‡&ôwUwbÂ%7—7FVÒåv–æF÷w2äÖVF–å&ôuRä6ö×÷6—F–öâå&ôwU&WF–æVD6ö×÷6—F–öä6öÖÖæE6–æ²"“°¢G—RwUFW‡GW&UG—RÒvWE&WV—&VEG—R‡&ôwT&6¶VæBÂ%&ôuRä&6¶VæBäwUFW‡GW&R"“°¢G—RwUFW‡GW&TÇ†ÖöFUG—RÒvWE&WV—&VEG—R‡&ôwT&6¶VæBÂ%&ôuRä&6¶VæBäwUFW‡GW&TÇ†ÖöFR"“°¢G—RwUFW‡GW&TF–ÖVç6–öåG—RÒvWE&WV—&VEG—R‡&ôwT&6¶VæBÂ%&ôuRä&6¶VæBäwUFW‡GW&TF–ÖVç6–öâ"“°¢G—RFW‡GW&Tf÷&ÖEG—RÒvWE&WV—&VEG—R‡6–Æ´æWEvV$wRÂ%6–Æ²ääUBåvV$uRåFW‡GW&Tf÷&ÖB"“°¢G—RFW‡GW&UW6vUG—RÒvWE&WV—&VEG—R‡6–Æ´æWEvV$wRÂ%6–Æ²ääUBåvV$uRåFW‡GW&UW6vR"“°¢ö&¦V7Bwef—7VÂÒ7&VFU&VEwef—7VÂ‡&W6VçFF–öä6÷&RÂ&W6VçFF–öäg&ÖWv÷&²Âv–æF÷w4&6R“° ¢&VÆöDæF—fT76WB†æF—fT76WE&ö÷BÂ'vwR"ÂB'¶FW67&—F–öå&Vf—‡ÒvV$uRæF—fR'VçF–ÖR"“°¢W6–ær”F—7÷6&ÆR7W'&VçDF—&V7F÷'’ÒW6„7W'&VçDF—&V7F÷'’†æF—fT76WE&ö÷B“°¢ö&¦V7B&v&…Væ÷&ÒÒVçVÒå'6R‡FW‡GW&Tf÷&ÖEG—RÂ%&v&…Væ÷&Ò"“°¢ö&¦V7B&VæFW%F&vWEW6vRÒ6öÖ&–æTVçVÔfÆw2€¢FW‡GW&UW6vUG—RÀ¢VçVÒå'6R‡FW‡GW&UW6vUG—RÂ%&VæFW$GF6†ÖVçB"’À¢VçVÒå'6R‡FW‡GW&UW6vUG—RÂ$6÷•7&2"’“°¢ö&¦V7B7G&–v‡DÇ†ÖöFRÒVçVÒå'6R†wUFW‡GW&TÇ†ÖöFUG—RÂ%7G&–v‡B"“°¢ö&¦V7BF–ÖVç6–öã$BÒVçVÒå'6R†wUFW‡GW&TF–ÖVç6–öåG—RÂ$F–ÖVç6–öã$B"“°¢ö&¦V7BF&vWBÒ–çfö¶U7FF–2†6ö×÷6—F–öåF&vWEG—RÂ$7&VFT†VFÆW72"Â&v&…Væ÷&Ò“°¢ö&¦V7BFW‡GW&RÒ7&VFR€¢wUFW‡GW&UG—RÀ¢vWE&÷W'G’‡F&vWBÂ$6öçFW‡B"’À¢ƒCRÀ¢cƒRÀ¢&v&…Væ÷&ÒÀ¢&VæFW%F&vWEW6vRÀ¢B'¶FW67&—F–öå&Vf—‡Ò6¶vVB†”E’g&ÖV'VffW"F&vWB"À¢RÀ¢7G&–v‡DÇ†ÖöFRÀ¢RÀ¢RÀ¢F–ÖVç6–öã$B“° ¢G'¢°¢ö&¦V7Bg&ÖRÒ–çfö¶R€¢F&vWBÀ¢$&Vv–äG&v–ætg&ÖR"À¢ƒCRÀ¢cƒRÀ¢G'VRÀ¢C#RÀ¢ƒCRÀ¢"ãÀ¢"ã“°¢ö&¦V7B6–æ²Ò7&VFR€¢&WF–æVE6–æµG—RÀ¢g&ÖRÀ¢vWE&÷W'G’‡F&vWBÂ$6öçFW‡B"’À¢vWE&÷W'G’‡F&vWBÂ%f–Ww÷'C4EFW‡GW&T66†R"’“°¢G'¢°¢–çfö¶R‡F&vWBÂ%&WÆ•f—7VÅ7V'G&VR"Âwef—7VÂÂ6–æ²ÂçVÆÂÂçVÆÂ“°¢Ğ¢f–æÆÇ¢°¢‡6–æ²2”F—7÷6&ÆR“òäF—7÷6R‚“°¢Ğ ¢ÖWF†öD–æfò&VæFW"Òf–æDÖWF†öD'•&ÖWFW$æÖW2€¢6ö×÷6—F–öåF&vWEG—RÀ¢%&VæFW""À¢²&Æöv–6Åv–GF‚"Â&Æöv–6Ä†V–v‡B"Â'—†VÅv–GF‚"Â'—†VÄ†V–v‡B"Â&G•66ÆR"Â'F&vWEf–Wr%Ò“°¢–çfö¶TÖWF†öB€¢&VæFW"À¢F&vWBÀ¢C#RÀ¢ƒCRÀ¢ƒCRÀ¢cƒRÀ¢&bÀ¢vWE&÷W'G’‡FW‡GW&RÂ%f–WuG""’“° ¢'—FUµÒ—†VÇ2Ò†'—FUµÒ”–çfö¶R‡FW‡GW&RÂ%&VE—†VÇ2"ÂR“°¢76W'E&v&—†VÄ—5&VB€¢—†VÇ2À¢v–GFƒ¢ƒCÀ¢ƒ¢#À¢“¢#À¢B'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æVBub†”E’WW"ÖÆVgB—†VÂ"“°¢76W'E&v&—†VÄ—5&VB€¢—†VÇ2À¢v–GFƒ¢ƒCÀ¢ƒ¢sƒÀ¢“¢ScÀ¢B'¶FW67&—F–öå&Vf—‡Ò6¶vVB&WF–æVBub†”E’Æ÷vW"×&–v‡B—†VÂ"“°¢Ğ¢f–æÆÇ¢°¢‡FW‡GW&R2”F—7÷6&ÆR“òäF—7÷6R‚“°¢‡F&vWB2”F—7÷6&ÆR“òäF—7÷6R‚“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B76W'E6¶vVDö&¦V7E&VæFW$FF&V7FævÆTf–ÆÇ5‡—6–6ÅF&vWB€¢7G&–æræF—fT76WE&ö÷BÀ¢76VÖ&Ç’&ôwUwbÀ¢76VÖ&Ç’&ôwT&6¶VæBÀ¢76VÖ&Ç’&W6VçFF–öä6÷&RÀ¢76VÖ&Ç’v–æF÷w4&6RÀ¢76VÖ&Ç’6–Æ´æWEvV$wRÀ¢7G&–ærFW67&—F–öå&Vf—‚¢°¢G—R6ö×÷6—F–öåF&vWEG—RÒvWE&WV—&VEG—R‡&ôwUwbÂ%7—7FVÒåv–æF÷w2äÖVF–å&ôuRå&ôwUwd6ö×÷6—F–öåF&vWB"“°¢G—RwUFW‡GW&UG—RÒvWE&WV—&VEG—R‡&ôwT&6¶VæBÂ%&ôuRä&6¶VæBäwUFW‡GW&R"“°¢G—RwUFW‡GW&TÇ†ÖöFUG—RÒvWE&WV—&VEG—R‡&ôwT&6¶VæBÂ%&ôuRä&6¶VæBäwUFW‡GW&TÇ†ÖöFR"“°¢G—RwUFW‡GW&TF–ÖVç6–öåG—RÒvWE&WV—&VEG—R‡&ôwT&6¶VæBÂ%&ôuRä&6¶VæBäwUFW‡GW&TF–ÖVç6–öâ"“°¢G—RFW‡GW&Tf÷&ÖEG—RÒvWE&WV—&VEG—R‡6–Æ´æWEvV$wRÂ%6–Æ²ääUBåvV$uRåFW‡GW&Tf÷&ÖB"“°¢G—RFW‡GW&UW6vUG—RÒvWE&WV—&VEG—R‡6–Æ´æWEvV$wRÂ%6–Æ²ääUBåvV$uRåFW‡GW&UW6vR"“°¢G—R6öÆ–D6öÆ÷$''W6…G—RÒvWE&WV—&VEG—R‡&W6VçFF–öä6÷&RÂ%7—7FVÒåv–æF÷w2äÖVF–å6öÆ–D6öÆ÷$''W6‚"“°¢G—R6öÆ÷%G—RÒvWE&WV—&VEG—R‡&W6VçFF–öä6÷&RÂ%7—7FVÒåv–æF÷w2äÖVF–ä6öÆ÷""“°¢G—R&V7EG—RÒvWE&WV—&VEG—R‡v–æF÷w4&6RÂ%7—7FVÒåv–æF÷w2å&V7B"“°¢ö&¦V7B&VBÒ–çfö¶U7FF–2†6öÆ÷%G—RÂ$g&öÕ&v""Â†'—FR“„dbÂ†'—FR“ƒÂ†'—FR“ƒ“°¢ö&¦V7B&VD''W6‚Ò7&VFR‡6öÆ–D6öÆ÷$''W6…G—RÂ&VB“°¢ö&¦V7B&V7FævÆRÒ7&VFR‡&V7EG—RÂãÂãÂC#ãÂƒCã“° ¢&VÆöDæF—fT76WB†æF—fT76WE&ö÷BÂ'vwR"ÂB'¶FW67&—F–öå&Vf—‡ÒvV$uRæF—fR'VçF–ÖR"“°¢W6–ær”F—7÷6&ÆR7W'&VçDF—&V7F÷'’ÒW6„7W'&VçDF—&V7F÷'’†æF—fT76WE&ö÷B“°¢ö&¦V7B&v&…Væ÷&ÒÒVçVÒå'6R‡FW‡GW&Tf÷&ÖEG—RÂ%&v&…Væ÷&Ò"“°¢ö&¦V7B&VæFW%F&vWEW6vRÒ6öÖ&–æTVçVÔfÆw2€¢FW‡GW&UW6vUG—RÀ¢VçVÒå'6R‡FW‡GW&UW6vUG—RÂ%&VæFW$GF6†ÖVçB"’À¢VçVÒå'6R‡FW‡GW&UW6vUG—RÂ$6÷•7&2"’“°¢ö&¦V7B7G&–v‡DÇ†ÖöFRÒVçVÒå'6R†wUFW‡GW&TÇ†ÖöFUG—RÂ%7G&–v‡B"“°¢ö&¦V7BF–ÖVç6–öã$BÒVçVÒå'6R†wUFW‡GW&TF–ÖVç6–öåG—RÂ$F–ÖVç6–öã$B"“°¢ö&¦V7BF&vWBÒ–çfö¶U7FF–2†6ö×÷6—F–öåF&vWEG—RÂ$7&VFT†VFÆW72"Â&v&…Væ÷&Ò“°¢ö&¦V7BFW‡GW&RÒ7&VFR€¢wUFW‡GW&UG—RÀ¢vWE&÷W'G’‡F&vWBÂ$6öçFW‡B"’À¢ƒCRÀ¢cƒRÀ¢&v&…Væ÷&ÒÀ¢&VæFW%F&vWEW6vRÀ¢B'¶FW67&—F–öå&Vf—‡Ò6¶vVBö&¦V7B&VæFW"ÖFF†”E’g&ÖV'VffW"F&vWB"À¢RÀ¢7G&–v‡DÇ†ÖöFRÀ¢RÀ¢RÀ¢F–ÖVç6–öã$B“° ¢G'¢°¢ö&¦V7Bg&ÖRÒ–çfö¶R€¢F&vWBÀ¢$&Vv–äG&v–ætg&ÖR"À¢ƒCRÀ¢cƒRÀ¢G'VRÀ¢C#RÀ¢ƒCRÀ¢"ãÀ¢"ã“°¢ö&¦V7BG&v–æt6öçFW‡BÒ–çfö¶R†g&ÖRÂ$÷Väö&¦V7E&VæFW$FF6–æ´6öçFW‡B"ÂæWrö&¦V7B‚’ÂçVÆÂ“°¢G'¢°¢–çfö¶Tö&¦V7DG&u&V7FævÆR†G&v–æt6öçFW‡BÂ&VD''W6‚ÂçVÆÂÂ&V7FævÆR“°¢Ğ¢f–æÆÇ¢°¢†G&v–æt6öçFW‡B2”F—7÷6&ÆR“òäF—7÷6R‚“°¢Ğ ¢ÖWF†öD–æfò&VæFW"Òf–æDÖWF†öD'•&ÖWFW$æÖW2€¢6ö×÷6—F–öåF&vWEG—RÀ¢%&VæFW""À¢²&Æöv–6Åv–GF‚"Â&Æöv–6Ä†V–v‡B"Â'—†VÅv–GF‚"Â'—†VÄ†V–v‡B"Â&G•66ÆR"Â'F&vWEf–Wr%Ò“°¢–çfö¶TÖWF†öB€¢&VæFW"À¢F&vWBÀ¢C#RÀ¢ƒCRÀ¢ƒCRÀ¢cƒRÀ¢&bÀ¢vWE&÷W'G’‡FW‡GW&RÂ%f–WuG""’“° ¢'—FUµÒ—†VÇ2Ò†'—FUµÒ”–çfö¶R‡FW‡GW&RÂ%&VE—†VÇ2"ÂR“°¢76W'E&v&—†VÄ—5&VB€¢—†VÇ2À¢v–GFƒ¢ƒCÀ¢ƒ¢#À¢“¢#À¢B'¶FW67&—F–öå&Vf—‡Ò6¶vVBö&¦V7B&VæFW"ÖFFub†”E’WW"ÖÆVgB—†VÂ"“°¢76W'E&v&—†VÄ—5&VB€¢—†VÇ2À¢v–GFƒ¢ƒCÀ¢ƒ¢sƒÀ¢“¢ScÀ¢B'¶FW67&—F–öå&Vf—‡Ò6¶vVBö&¦V7B&VæFW"ÖFFub†”E’Æ÷vW"×&–v‡B—†VÂ"“°¢Ğ¢f–æÆÇ¢°¢‡FW‡GW&R2”F—7÷6&ÆR“òäF—7÷6R‚“°¢‡F&vWB2”F—7÷6&ÆR“òäF—7÷6R‚“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B–çfö¶Tö&¦V7DG&u&V7FævÆR†ö&¦V7BG&v–æt6öçFW‡BÂö&¦V7Cò''W6‚Âö&¦V7CòVâÂö&¦V7B&V7FævÆR¢°¢ÖWF†öD–æfòG&u&V7FævÆRÒG&v–æt6öçFW‡BävWEG—R‚’ävWDÖWF†öB€¢$G&u&V7FævÆR"À¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2À¢&–æFW#¢çVÆÂÀ¢·G—Vöb†ö&¦V7B’ÂG—Vöb†ö&¦V7B’ÂG—Vöb†ö&¦V7B•ÒÀ¢ÖöF–f–W'3¢çVÆÂ¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ†G&v–æt6öçFW‡BävWEG—R‚’ägVÆÄæÖRÂ$G&u&V7FævÆR†ö&¦V7BÂö&¦V7BÂö&¦V7B’"“°¢–çfö¶TÖWF†öB†G&u&V7FævÆRÂG&v–æt6öçFW‡BÂ''W6‚ÂVâÂ&V7FævÆR“°¢Ğ ¢&—fFR7FF–2ö&¦V7B7&VFU&VEwef—7VÂ€¢76VÖ&Ç’&W6VçFF–öä6÷&RÀ¢76VÖ&Ç’&W6VçFF–öäg&ÖWv÷&²À¢76VÖ&Ç’v–æF÷w4&6R¢°¢G—R&÷&FW%G—RÒvWE&WV—&VEG—R‡&W6VçFF–öäg&ÖWv÷&²Â%7—7FVÒåv–æF÷w2ä6öçG&öÇ2ä&÷&FW""“°¢G—R6öÆ÷%G—RÒvWE&WV—&VEG—R‡&W6VçFF–öä6÷&RÂ%7—7FVÒåv–æF÷w2äÖVF–ä6öÆ÷""“°¢G—R6öÆ–D6öÆ÷$''W6…G—RÒvWE&WV—&VEG—R‡&W6VçFF–öä6÷&RÂ%7—7FVÒåv–æF÷w2äÖVF–å6öÆ–D6öÆ÷$''W6‚"“°¢G—R&V7EG—RÒvWE&WV—&VEG—R‡v–æF÷w4&6RÂ%7—7FVÒåv–æF÷w2å&V7B"“°¢G—R6—¦UG—RÒvWE&WV—&VEG—R‡v–æF÷w4&6RÂ%7—7FVÒåv–æF÷w2å6—¦R"“°¢ö&¦V7B&VBÒ–çfö¶U7FF–2†6öÆ÷%G—RÂ$g&öÕ&v""Â†'—FR“„dbÂ†'—FR“ƒÂ†'—FR“ƒ“°¢ö&¦V7B&÷&FW"Ò7&VFR†&÷&FW%G—R“°¢6WE&÷W'G’†&÷&FW"Â%v–GF‚"ÂC#ã“°¢6WE&÷W'G’†&÷&FW"Â$†V–v‡B"ÂƒCã“°¢6WE&÷W'G’†&÷&FW"Â$&6¶w&÷VæB"Â7&VFR‡6öÆ–D6öÆ÷$''W6…G—RÂ&VB’“°¢–çfö¶Ufö–B†&÷&FW"Â$ÖV7W&R"Â7&VFR‡6—¦UG—RÂC#ãÂƒCã’“°¢–çfö¶Ufö–B†&÷&FW"Â$'&ævR"Â7&VFR‡&V7EG—RÂãÂãÂC#ãÂƒCã’“°¢–çfö¶Ufö–B†&÷&FW"Â%WFFTÆ–÷WB"“°¢&WGW&â&÷&FW#°¢Ğ ¢&—fFR7FF–2fö–B&VÆöDæF—fT76WB‡7G&–ær&ö÷BÂ7G&–ær76WDæÖRÂ7G&–ærFW67&—F–öâ¢°¢f÷&V6‚‡7G&–ær6æF–FFR–âvWDæF—fT76WD6æF–FFW2†76WDæÖR’¢°¢7G&–ærF‚ÒF‚ä6öÖ&–æR‡&ö÷BÂ6æF–FFR“°¢–b„f–ÆRäW†—7G2‡F‚’bbæF—fTÆ–'&'’åG'”ÆöB‡F‚Â÷WBò’¢°¢&WGW&ã°¢Ğ¢Ğ ¢F‡&÷ræWrf–ÆTæ÷Df÷VæDW†6WF–öâ‚B$6÷VÆBæ÷BÆöB¶FW67&—F–öçÒg&öÒw·&ö÷GÒrâ"“°¢Ğ ¢&—fFR7FF–2”F—7÷6&ÆRW6„7W'&VçDF—&V7F÷'’‡7G&–ærF‚¢°¢&WGW&âæWr7W'&VçDF—&V7F÷'•66÷R‡F‚“°¢Ğ ¢&—fFR6VÆVB6Æ727W'&VçDF—&V7F÷'•66÷R¢”F—7÷6&ÆP¢°¢&—fFR&VFöæÇ’7G&–ærö÷&–v–æÄF—&V7F÷'“° ¢V&Æ–27W'&VçDF—&V7F÷'•66÷R‡7G&–ærF‚¢°¢ö÷&–v–æÄF—&V7F÷'’ÒVçf—&öæÖVçBä7W'&VçDF—&V7F÷'“°¢Vçf—&öæÖVçBä7W'&VçDF—&V7F÷'’ÒFƒ°¢Ğ ¢V&Æ–2fö–BF—7÷6R‚¢°¢Vçf—&öæÖVçBä7W'&VçDF—&V7F÷'’Òö÷&–v–æÄF—&V7F÷'“°¢Ğ¢Ğ ¢&—fFR7FF–2ö&¦V7B6öÖ&–æTVçVÔfÆw2…G—RVçVÕG—RÂ&×2ö&¦V7EµÒfÇVW2¢°¢VÆöær6öÖ&–æVBÒ°¢f÷&V6‚†ö&¦V7BfÇVR–âfÇVW2¢°¢6öÖ&–æVBÃÒ6öçfW'BåFõT–çCcB‡fÇVRÂ7VÇGW&T–æfòä–çf&–çD7VÇGW&R“°¢Ğ ¢&WGW&âVçVÒåFôö&¦V7B†VçVÕG—RÂ6öÖ&–æVB“°¢Ğ ¢&—fFR7FF–2fö–B76W'E&v&—†VÄ—5&VB€¢'—FUµÒ—†VÇ2À¢–çBv–GF‚À¢–çB‚À¢–çB’À¢7G&–ærFW67&—F–öâ¢°¢–çB–æFW‚Ò‚‡’¢v–GF‚’²‚’¢C°¢–b†–æFW‚ÂÇÂ–æFW‚²2ãÒ—†VÇ2äÆVæwF‚¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$W‡V7FVB¶FW67&—F–öçÒ—†VÂ–æFW‚Fò&R–ç6–FRF†R&VF&6²'VffW"â"“°¢Ğ ¢'—FR"Ò—†VÇ5¶–æFW…Ó°¢'—FRrÒ—†VÇ5¶–æFW‚²Ó°¢'—FR"Ò—†VÇ5¶–æFW‚²%Ó°¢'—FRÒ—†VÇ5¶–æFW‚²5Ó°¢–b‡"Â##ÇÂrâcÇÂ"âcÇÂÒ#SR¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒFò&R&VBÂ'WBf÷VæB$t$‡·'ÒÂ¶wÒÂ¶'ÒÂ¶Ò’â"“°¢Ğ¢Ğ ¢&—fFR7FF–27G&–ær'Vå&ö6W72‡7G&–ærf–ÆTæÖRÂ7G&–ærv÷&¶–ætF—&V7F÷'’Â&×27G&–æuµÒ&wVÖVçG2¢°¢&WGW&â'Vå&ö6W72†f–ÆTæÖRÂv÷&¶–ætF—&V7F÷'’ÂVçf—&öæÖVçC¢çVÆÂÂ&wVÖVçG2“°¢Ğ ¢&—fFR7FF–27G&–ær&W6öÇfTF÷DæWD†÷7B‡7G&–ær&Wõ&ö÷B¢°¢7G&–ærF÷FæWDf–ÆTæÖRÒ'VçF–ÖT–æf÷&ÖF–öâä—4õ5ÆFf÷&Ò„õ5ÆFf÷&Òåv–æF÷w2¢ò&F÷FæWBæW†R ¢¢&F÷FæWB#°¢7G&–ær&WôÆö6Ä†÷7BÒF‚ä6öÖ&–æR‡&Wõ&ö÷BÂ"æF÷FæWB"ÂF÷FæWDf–ÆTæÖR“°¢–b„f–ÆRäW†—7G2‡&WôÆö6Ä†÷7B’¢°¢&WGW&â&WôÆö6Ä†÷7C°¢Ğ ¢7G&–æsòF÷FæWE&ö÷BÒVçf—&öæÖVçBävWDVçf—&öæÖVçEf&–&ÆR‚$DõDäUEõ$ôõB"“°¢–b‚7G&–ærä—4çVÆÄ÷%v†—FU76R†F÷FæWE&ö÷B’¢°¢7G&–ærF÷FæWE&ö÷D†÷7BÒF‚ä6öÖ&–æR†F÷FæWE&ö÷BÂF÷FæWDf–ÆTæÖR“°¢–b„f–ÆRäW†—7G2†F÷FæWE&ö÷D†÷7B’¢°¢&WGW&âF÷FæWE&ö÷D†÷7C°¢Ğ¢Ğ ¢&WGW&â&F÷FæWB#°¢Ğ ¢&—fFR7FF–27G&–ær'Vå&ö6W72€¢7G&–ærf–ÆTæÖRÀ¢7G&–ærv÷&¶–ætF—&V7F÷'’À¢•&VDöæÇ”F–7F–öæ'“Ç7G&–ærÂ7G&–æsãòVçf—&öæÖVçBÀ¢&×27G&–æuµÒ&wVÖVçG2¢°¢f"7F'D–æfòÒæWr&ö6W757F'D–æfò†f–ÆTæÖR¢°¢v÷&¶–ætF—&V7F÷'’Òv÷&¶–ætF—&V7F÷'’À¢&VF—&V7E7FæF&D÷WGWBÒG'VRÀ¢&VF—&V7E7FæF&DW'&÷"ÒG'VRÀ¢W6U6†VÆÄW†V7WFRÒfÇ6P¢Ó° ¢7F'D–æfòäVçf—&öæÖVçE²$DõDäUEõ$ôÄÅôdõ%t$B%ÒÒ$Ö¦÷"#°¢–b†Vçf—&öæÖVçB—2æ÷BçVÆÂ¢°¢f÷&V6‚‡f"—FVÒ–âVçf—&öæÖVçB¢°¢7F'D–æfòäVçf—&öæÖVçE¶—FVÒä¶W•ÒÒ—FVÒåfÇVS°¢Ğ¢Ğ ¢f÷&V6‚‡7G&–ær&wVÖVçB–â&wVÖVçG2¢°¢7F'D–æfòä&wVÖVçDÆ—7BäFB†&wVÖVçB“°¢Ğ ¢W6–ær&ö6W72&ö6W72Ò&ö6W72å7F'B‡7F'D–æfò¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$f–ÆVBFò7F'Bw¶f–ÆTæÖWÒrâ"“°¢7G&–ær7FæF&D÷WGWBÒ&ö6W72å7FæF&D÷WGWBå&VEFôVæB‚“°¢7G&–ær7FæF&DW'&÷"Ò&ö6W72å7FæF&DW'&÷"å&VEFôVæB‚“°¢&ö6W72åv—Df÷$W†—B‚“° ¢7G&–ær÷WGWBÒ7FæF&D÷WGWB²7FæF&DW'&÷#°¢–b‡&ö6W72äW†—D6öFRÒ¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$6öÖÖæBw¶f–ÆTæÖWÒ·7G&–ærä¦ö–â‚""Â&wVÖVçG2—Òrf–ÆVBv—F‚W†—B6öFR·&ö6W72äW†—D6öFWÒç´Vçf—&öæÖVçBäæWtÆ–æW×¶÷WGWGÒ"“°¢Ğ ¢&WGW&â÷WGWC°¢Ğ ¢&—fFR7FF–27G&–ær'Vä†÷7DÆ—fUfÆ–FF–öå&ö&R€¢7G&–ærf–ÆTæÖRÀ¢7G&–ærv÷&¶–ætF—&V7F÷'’À¢7G&–ærVçf—&öæÖVçEf&–&ÆRÀ¢7G&–ær7V66W74Ö&¶W"À¢7G&–ærFW67&—F–öâÀ¢&×27G&–æuµÒ&wVÖVçG2¢°¢f"7F'D–æfòÒæWr&ö6W757F'D–æfò†f–ÆTæÖR¢°¢v÷&¶–ætF—&V7F÷'’Òv÷&¶–ætF—&V7F÷'’À¢&VF—&V7E7FæF&D÷WGWBÒG'VRÀ¢&VF—&V7E7FæF&DW'&÷"ÒG'VRÀ¢W6U6†VÆÄW†V7WFRÒfÇ6P¢Ó°¢7F'D–æfòäVçf—&öæÖVçE²$DõDäUEõ$ôÄÅôdõ%t$B%ÒÒ$Ö¦÷"#°¢7F'D–æfòäVçf—&öæÖVçE¶Vçf—&öæÖVçEf&–&ÆUÒÒ##° ¢f÷&V6‚‡7G&–ær&wVÖVçB–â&wVÖVçG2¢°¢7F'D–æfòä&wVÖVçDÆ—7BäFB†&wVÖVçB“°¢Ğ ¢f"÷WGWBÒæWr7G&–æt'V–ÆFW"‚“°¢ö&¦V7B÷WGWDvFRÒæWr‚“°¢W6–ærf"&ö6W72ÒæWr&ö6W70¢°¢7F'D–æfòÒ7F'D–æfòÀ¢Væ&ÆU&—6–ætWfVçG2ÒG'VP¢Ó°¢&ö6W72ä÷WGWDFF&V6V—fVB³Ò…òÂR’ÓâVæE&ö6W74÷WGWB†÷WGWBÂ÷WGWDvFRÂRäFF“°¢&ö6W72äW'&÷$FF&V6V—fVB³Ò…òÂR’ÓâVæE&ö6W74÷WGWB†÷WGWBÂ÷WGWDvFRÂRäFF“° ¢–b‚&ö6W72å7F'B‚’¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$f–ÆVBFò7F'Bw¶f–ÆTæÖWÒrf÷"¶FW67&—F–öçÒâ"“°¢Ğ ¢&ö6W72ä&Vv–ä÷WGWE&VDÆ–æR‚“°¢&ö6W72ä&Vv–äW'&÷%&VDÆ–æR‚“° ¢G'¢°¢f"F–ÖV÷WBÒF–ÖU7âäg&öÕ6V6öæG2ƒ#R“°¢7F÷vF6‚7F÷vF6‚Ò7F÷vF6‚å7F'DæWr‚“°¢v†–ÆR‡7F÷vF6‚äVÆ6VBÂF–ÖV÷WB¢°¢7G&–ær6æ6†÷C°¢Æö6²†÷WGWDvFR¢°¢6æ6†÷BÒ÷WGWBåFõ7G&–ær‚“°¢Ğ ¢–b‡6æ6†÷Bä6öçF–ç2‡7V66W74Ö&¶W"Â7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢&ö6W72åv—Df÷$W†—B…F–ÖU7âäg&öÕ6V6öæG2ƒ"’“°¢&WGW&â6æ6†÷C°¢Ğ ¢–b‡&ö6W72ä†4W†—FVB¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒFò&–çBw·7V66W74Ö&¶W'Òr&Vf÷&RW†—F–ærv—F‚6öFR·&ö6W72äW†—D6öFWÒç´Vçf—&öæÖVçBäæWtÆ–æW×·6æ6†÷GÒ"“°¢Ğ ¢F‡&VBå6ÆVWƒS“°¢Ğ ¢7G&–ærF–ÖVD÷WD÷WGWC°¢Æö6²†÷WGWDvFR¢°¢F–ÖVD÷WD÷WGWBÒ÷WGWBåFõ7G&–ær‚“°¢Ğ ¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B%F–ÖVB÷WBv—F–ærf÷"¶FW67&—F–öçÒFò&–çBw·7V66W74Ö&¶W'Òrç´Vçf—&öæÖVçBäæWtÆ–æW×·F–ÖVD÷WD÷WGWGÒ"“°¢Ğ¢f–æÆÇ¢°¢7F÷Æ—fU&ö&U&ö6W72‡&ö6W72“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–BVæE&ö6W74÷WGWB…7G&–æt'V–ÆFW"÷WGWBÂö&¦V7B÷WGWDvFRÂ7G&–æsòFF¢°¢–b†FFÓÒçVÆÂ¢°¢&WGW&ã°¢Ğ ¢Æö6²†÷WGWDvFR¢°¢÷WGWBäVæDÆ–æR†FF“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B7F÷Æ—fU&ö&U&ö6W72…&ö6W72&ö6W72¢°¢–b‡&ö6W72ä†4W†—FVB¢°¢&WGW&ã°¢Ğ ¢G'¢°¢&ö6W72ä¶–ÆÂ†VçF—&U&ö6W75G&VS¢G'VR“°¢Ğ¢6F6‚„–çfÆ–D÷W&F–öäW†6WF–öâ¢°¢Ğ ¢&ö6W72åv—Df÷$W†—BƒS“°¢Ğ ¢&—fFR7FF–27G&–æuµÒvWDæF—fT76WD6æF–FFW2‡7G&–ær76WDæÖR¢°¢&WGW&â76WDæÖR7v—F6€¢°¢'vwR"v†Vâ÷W&F–æu7—7FVÒä—5v–æF÷w2‚’Óâ²'vwUöæF—fRæFÆÂ%ÒÀ¢'vwR"v†Vâ÷W&F–æu7—7FVÒä—4Ö4õ2‚’Óâ²&Æ–'vwUöæF—fRæG–Æ–"%ÒÀ¢'vwR"Óâ²&Æ–'vwUöæF—fRç6ò%ÒÀ¢&vÆgr"v†Vâ÷W&F–æu7—7FVÒä—5v–æF÷w2‚’Óâ²&vÆgs2æFÆÂ%ÒÀ¢&vÆgr"v†Vâ÷W&F–æu7—7FVÒä—4Ö4õ2‚’Óâ²&Æ–&vÆgrã2æG–Æ–"%ÒÀ¢&vÆgr"Óâ²&Æ–&vÆgrç6òã2%ÒÀ¢òÓâF‡&÷ræWr&wVÖVçD÷WDöe&ævTW†6WF–öâ†æÖVöb†76WDæÖR’Â76WDæÖRÂçVÆÂ¢Ó°¢Ğ ¢&—fFR7FF–2”VçVÖW&&ÆSÇ7G&–æsâvWEVæÖævVDFÆÄ6æF–FFW2‡7G&–ærVæÖævVDFÆÄæÖR¢°¢––VÆB&WGW&âF‚ävWDf–ÆTæÖR‡VæÖævVDFÆÄæÖR“° ¢–b‡VæÖævVDFÆÄæÖRä6öçF–ç2‚&vÆgr"Â7G&–æt6ö×&—6öâä÷&F–æÄ–væ÷&T66R’¢°¢f÷&V6‚‡7G&–ær6æF–FFR–âvWDæF—fT76WD6æF–FFW2‚&vÆgr"’¢°¢––VÆB&WGW&â6æF–FFS°¢Ğ¢Ğ ¢–b‡VæÖævVDFÆÄæÖRä6öçF–ç2‚'vwR"Â7G&–æt6ö×&—6öâä÷&F–æÄ–væ÷&T66R’¢°¢f÷&V6‚‡7G&–ær6æF–FFR–âvWDæF—fT76WD6æF–FFW2‚'vwR"’¢°¢––VÆB&WGW&â6æF–FFS°¢Ğ¢Ğ ¢7G&–æræÖUv—F†÷WDW‡FVç6–öâÒF‚ävWDf–ÆTæÖUv—F†÷WDW‡FVç6–öâ‡VæÖævVDFÆÄæÖR“°¢–b„÷W&F–æu7—7FVÒä—5v–æF÷w2‚’¢°¢––VÆB&WGW&âæÖUv—F†÷WDW‡FVç6–öâ²"æFÆÂ#°¢Ğ¢VÇ6R–b„÷W&F–æu7—7FVÒä—4Ö4õ2‚’¢°¢––VÆB&WGW&â&Æ–""²æÖUv—F†÷WDW‡FVç6–öâ²"æG–Æ–"#°¢Ğ¢VÇ6P¢°¢––VÆB&WGW&â&Æ–""²æÖUv—F†÷WDW‡FVç6–öâ²"ç6ò#°¢Ğ¢Ğ ¢&—fFR7FF–27G&–ærf–æE&Wõ&ö÷B‚¢°¢7G&–æsòF—&V7F÷'’Ò6öçFW‡Bä&6TF—&V7F÷'“°¢v†–ÆR‚7G&–ærä—4çVÆÄ÷$V×G’†F—&V7F÷'’’¢°¢–b„f–ÆRäW†—7G2…F‚ä6öÖ&–æR†F—&V7F÷'’Â$Ö–7&÷6ögBäF÷FæWBåwbç6Æâ"’’b`¢F—&V7F÷'’äW†—7G2…F‚ä6öÖ&–æR†F—&V7F÷'’Â'6¶v–ær"Â%&ôuRåwbå6F²"’’¢°¢&WGW&âF—&V7F÷'“°¢Ğ ¢F—&V7F÷'’ÒF—&V7F÷'’ävWE&VçB†F—&V7F÷'’“òägVÆÄæÖS°¢Ğ ¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚$6÷VÆBæ÷BÆö6FR&W÷6—F÷'’&ö÷Bâ"“°¢Ğ ¢&—fFR7FF–2fö–Bw&—FTf–ÆR‡7G&–ærF‚Â7G&–ær6öçFVçG2¢°¢F—&V7F÷'’ä7&VFTF—&V7F÷'’…F‚ävWDF—&V7F÷'”æÖR‡F‚’óòF‡&÷ræWr&wVÖVçDW†6WF–öâ‚%F‚†2æòF—&V7F÷'’â"ÂæÖVöb‡F‚’’“°¢f–ÆRåw&—FTÆÅFW‡B‡F‚Â6öçFVçG2“°¢Ğ ¢&—fFR7FF–2fö–B&WV—&TF—&V7F÷'’‡7G&–ærF‚Â7G&–ærFW67&—F–öâ¢°¢–b‚F—&V7F÷'’äW†—7G2‡F‚’¢°¢F‡&÷ræWrF—&V7F÷'”æ÷Df÷VæDW†6WF–öâ‚B$Ö—76–ær¶FW67&—F–öçÓ¢·F‡Ò"“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B&WV—&Tf–ÆR‡7G&–ærF‚Â7G&–ærFW67&—F–öâ¢°¢–b‚f–ÆRäW†—7G2‡F‚’¢°¢F‡&÷ræWrf–ÆTæ÷Df÷VæDW†6WF–öâ‚B$Ö—76–ær¶FW67&—F–öçÓ¢·F‡Ò"ÂF‚“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B&WV—&Tç”f–ÆR‡7G&–ær&ö÷BÂ”VçVÖW&&ÆSÇ7G&–æsâ6æF–FFW2Â7G&–ærFW67&—F–öâ¢°¢f÷&V6‚‡7G&–ær6æF–FFR–â6æF–FFW2¢°¢–b„f–ÆRäW†—7G2…F‚ä6öÖ&–æR‡&ö÷BÂ6æF–FFR’’¢°¢&WGW&ã°¢Ğ¢Ğ ¢F‡&÷ræWrf–ÆTæ÷Df÷VæDW†6WF–öâ‚B$Ö—76–ær¶FW67&—F–öçÒVæFW"·&ö÷GÒâ"“°¢Ğ ¢&—fFR7FF–27G&–ær&VE6¶vTVçG'’…¦—&6†—fR6¶vRÂ7G&–ærVçG'”æÖRÂ7G&–ærFW67&—F–öâ¢°¢W6–ær7G&VÒ7G&VÒÒ&WV—&U6¶vTVçG'’‡6¶vRÂVçG'”æÖRÂFW67&—F–öâ’ä÷Vâ‚“°¢W6–ærf"&VFW"ÒæWr7G&VÕ&VFW"‡7G&VÒ“°¢&WGW&â&VFW"å&VEFôVæB‚“°¢Ğ ¢&—fFR7FF–2¦—&6†—fTVçG'’&WV—&U6¶vTVçG'’…¦—&6†—fR6¶vRÂ7G&–ærVçG'”æÖRÂ7G&–ærFW67&—F–öâ¢°¢¦—&6†—fTVçG'“òVçG'’Ò6¶vRävWDVçG'’†VçG'”æÖR“°¢–b†VçG'’—2çVÆÂ¢°¢F‡&÷ræWrf–ÆTæ÷Df÷VæDW†6WF–öâ‚B$Ö—76–ær¶FW67&—F–öçÒ6¶vRVçG'“¢¶VçG'”æÖWÒ"ÂVçG'”æÖR“°¢Ğ ¢&WGW&âVçG'“°¢Ğ ¢&—fFR7FF–276VÖ&Ç”æÖR&VE6¶vT76VÖ&Ç”æÖR…¦—&6†—fTVçG'’VçG'’Â7G&–ærFW67&—F–öâ¢°¢7G&–ærFV×F‚ÒF‚ä6öÖ&–æR€¢F‚ävWEFV×F‚‚’À¢'&öwR×wb×6F²×6¶vRÒ"²wV–BäæWtwV–B‚’åFõ7G&–ær‚$â"’²"æFÆÂ"“° ¢G'¢°¢W6–ær…7G&VÒ6÷W&6RÒVçG'’ä÷Vâ‚’¢W6–ær„f–ÆU7G&VÒFW7F–æF–öâÒf–ÆRä7&VFR‡FV×F‚’¢°¢6÷W&6Rä6÷•Fò†FW7F–æF–öâ“°¢Ğ ¢&WGW&â76VÖ&Ç”æÖRävWD76VÖ&Ç”æÖR‡FV×F‚“°¢Ğ¢6F6‚„W†6WF–öâW‚’v†Vâ†W‚—2&D–ÖvTf÷&ÖDW†6WF–öâ÷"f–ÆTÆöDW†6WF–öâ÷"f–ÆTæ÷Df÷VæDW†6WF–öâ¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$6÷VÆBæ÷B&VB¶FW67&—F–öçÒ–FVçF—G’g&öÒ6¶vRVçG'’w¶VçG'’ägVÆÄæÖWÒrâ"ÂW‚“°¢Ğ¢f–æÆÇ¢°¢–b„f–ÆRäW†—7G2‡FV×F‚’¢°¢f–ÆRäFVÆWFR‡FV×F‚“°¢Ğ¢Ğ¢Ğ ¢&—fFR7FF–27G&–ær6ö×WFTf–ÆU6†#Sb‡7G&–ærF‚¢°¢W6–ærf–ÆU7G&VÒ7G&VÒÒf–ÆRä÷Vå&VB‡F‚“°¢&WGW&â6ö×WFU7G&VÕ6†#Sb‡7G&VÒ“°¢Ğ ¢&—fFR7FF–27G&–ær6ö×WFU7G&VÕ6†#Sb…7G&VÒ7G&VÒ¢°¢W6–ærf"6†#SbÒ4„#Sbä7&VFR‚“°¢&WGW&â6öçfW'BåFô†W…7G&–ær‡6†#Sbä6ö×WFT†6‚‡7G&VÒ’“°¢Ğ ¢&—fFR7FF–27G&–ærvWEV&Æ–4¶W•Fö¶Vâ„76VÖ&Ç”æÖR–FVçF—G’¢°¢'—FUµÓòV&Æ–4¶W•Fö¶VâÒ–FVçF—G’ävWEV&Æ–4¶W•Fö¶Vâ‚“°¢&WGW&âV&Æ–4¶W•Fö¶Vâ—2çVÆÂÇÂV&Æ–4¶W•Fö¶VâäÆVæwF‚ÓÒ ¢ò7G&–æräV×G¢¢7G&–ærä6öæ6B‡V&Æ–4¶W•Fö¶Vâå6VÆV7B‡fÇVRÓâfÇVRåFõ7G&–ær‚'ƒ""’’“°¢Ğ ¢&—fFR7FF–2fö–B76W'Dæõ6¶vTVçG'•&Vf—‚…¦—&6†—fR6¶vRÂ7G&–ær&Vf—‚Â7G&–ærFW67&—F–öâ¢°¢–b‡6¶vRäVçG&–W2äç’†VçG'’ÓâVçG'’ägVÆÄæÖRå7F'G5v—F‚‡&Vf—‚Â7G&–æt6ö×&—6öâä÷&F–æÄ–væ÷&T66R’’¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$W‡V7FVB¶FW67&—F–öçÒFò&R'6VçBâ"“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B76W'D6öçF–ç2‡7G&–ærfÇVRÂ7G&–ærW‡V7FVBÂ7G&–ærFW67&—F–öâ¢°¢–b‚fÇVRä6öçF–ç2†W‡V7FVBÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$W‡V7FVB¶FW67&—F–öçÒFò6öçF–âw¶W‡V7FVGÒrâ"“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B76W'DFöW4æ÷D6öçF–â‡7G&–ærfÇVRÂ7G&–ærVæW‡V7FVBÂ7G&–ærFW67&—F–öâ¢°¢–b‡fÇVRä6öçF–ç2‡VæW‡V7FVBÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$W‡V7FVB¶FW67&—F–öçÒæ÷BFò6öçF–âw·VæW‡V7FVGÒrâ"“°¢Ğ¢Ğ ¢&—fFR7FF–2G—RvWE&WV—&VEG—R„76VÖ&Ç’76VÖ&Ç’Â7G&–ærG—TæÖR¢°¢&WGW&â76VÖ&Ç’ävWEG—R‡G—TæÖRÂF‡&÷töäW'&÷#¢G'VR’¢óòF‡&÷ræWrG—TÆöDW†6WF–öâ‡G—TæÖR“°¢Ğ ¢&—fFR7FF–2ö&¦V7B7&VFR…G—RG—RÂ&×2ö&¦V7CõµÒ&w2¢°¢&WGW&â7F—fF÷"ä7&VFT–ç7Fæ6R€¢G—RÀ¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2À¢&–æFW#¢çVÆÂÀ¢&w2À¢7VÇGW&S¢çVÆÂ¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$6÷VÆBæ÷B7&VFRw·G—RägVÆÄæÖWÒrâ"“°¢Ğ ¢&—fFR7FF–2ö&¦V7BvWE&÷W'G’†ö&¦V7B–ç7Fæ6RÂ7G&–ær&÷W'G”æÖR¢°¢&÷W'G”–æfò&÷W'G’Ò–ç7Fæ6RävWEG—R‚’ävWE&÷W'G’€¢&÷W'G”æÖRÀ¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢óòF‡&÷ræWrÖ—76–ætÖVÖ&W$W†6WF–öâ†–ç7Fæ6RävWEG—R‚’ägVÆÄæÖRÂ&÷W'G”æÖR“°¢&WGW&â&÷W'G’ävWEfÇVR†–ç7Fæ6R¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B%&÷W'G’w·&÷W'G”æÖWÒr&WGW&æVBçVÆÂâ"“°¢Ğ ¢&—fFR7FF–2fö–B6WE&÷W'G’†ö&¦V7B–ç7Fæ6RÂ7G&–ær&÷W'G”æÖRÂö&¦V7CòfÇVR¢°¢&÷W'G”–æfò&÷W'G’Ò–ç7Fæ6RävWEG—R‚’ävWE&÷W'G’€¢&÷W'G”æÖRÀ¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢óòF‡&÷ræWrÖ—76–ætÖVÖ&W$W†6WF–öâ†–ç7Fæ6RävWEG—R‚’ägVÆÄæÖRÂ&÷W'G”æÖR“°¢&÷W'G’å6WEfÇVR†–ç7Fæ6RÂfÇVR“°¢Ğ ¢&—fFR7FF–2fö–B6WDf–VÆB†ö&¦V7B–ç7Fæ6RÂ7G&–ærf–VÆDæÖRÂö&¦V7CòfÇVR¢°¢f–VÆD–æfòf–VÆBÒ–ç7Fæ6RävWEG—R‚’ävWDf–VÆB€¢f–VÆDæÖRÀ¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2äæöåV&Æ–2¢óòF‡&÷ræWrÖ—76–ætf–VÆDW†6WF–öâ†–ç7Fæ6RävWEG—R‚’ägVÆÄæÖRÂf–VÆDæÖR“°¢f–VÆBå6WEfÇVR†–ç7Fæ6RÂfÇVR“°¢Ğ ¢&—fFR7FF–2ö&¦V7B–çfö¶R†ö&¦V7B–ç7Fæ6RÂ7G&–ærÖWF†öDæÖRÂ&×2ö&¦V7CõµÒ&w2¢°¢ÖWF†öD–æfòÖWF†öBÒvWD6ö×F–&ÆTÖWF†öB†–ç7Fæ6RävWEG—R‚’ÂÖWF†öDæÖRÂ&w2¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ†–ç7Fæ6RävWEG—R‚’ägVÆÄæÖRÂÖWF†öDæÖR“° ¢&WGW&â–çfö¶TÖWF†öB†ÖWF†öBÂ–ç7Fæ6RÂ&w2¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$ÖWF†öBw¶ÖWF†öDæÖWÒr&WGW&æVBçVÆÂâ"“°¢Ğ ¢&—fFR7FF–2fö–B–çfö¶Ufö–B†ö&¦V7B–ç7Fæ6RÂ7G&–ærÖWF†öDæÖRÂ&×2ö&¦V7CõµÒ&w2¢°¢ÖWF†öD–æfòÖWF†öBÒvWD6ö×F–&ÆTÖWF†öB†–ç7Fæ6RävWEG—R‚’ÂÖWF†öDæÖRÂ&w2¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ†–ç7Fæ6RävWEG—R‚’ägVÆÄæÖRÂÖWF†öDæÖR“° ¢–çfö¶TÖWF†öB†ÖWF†öBÂ–ç7Fæ6RÂ&w2“°¢Ğ ¢&—fFR7FF–2ö&¦V7B–çfö¶U7FF–2…G—RG—RÂ7G&–ærÖWF†öDæÖRÂ&×2ö&¦V7CõµÒ&w2¢°¢ÖWF†öD–æfòÖWF†öBÒvWD6ö×F–&ÆU7FF–4ÖWF†öB‡G—RÂÖWF†öDæÖRÂ&w2¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡G—RägVÆÄæÖRÂÖWF†öDæÖR“° ¢&WGW&â–çfö¶TÖWF†öB†ÖWF†öBÂçVÆÂÂ&w2¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$ÖWF†öBw¶ÖWF†öDæÖWÒr&WGW&æVBçVÆÂâ"“°¢Ğ ¢&—fFR7FF–2ö&¦V7Cò–çfö¶TÖWF†öB„ÖWF†öD–æfòÖWF†öBÂö&¦V7Cò–ç7Fæ6RÂ&×2ö&¦V7CõµÒ&w2¢°¢G'¢°¢&WGW&âÖWF†öBä–çfö¶R†–ç7Fæ6RÂ&w2“°¢Ğ¢6F6‚…F&vWD–çfö6F–öäW†6WF–öâW‚’v†Vâ†W‚ä–ææW$W†6WF–öâ—2æ÷BçVÆÂ¢°¢7—7FVÒå'VçF–ÖRäW†6WF–öå6W'f–6W2äW†6WF–öäF—7F6„–æfòä6GW&R†W‚ä–ææW$W†6WF–öâ’åF‡&÷r‚“°¢F‡&÷s°¢Ğ¢Ğ ¢&—fFR7FF–2ÖWF†öD–æfóòvWD6ö×F–&ÆTÖWF†öB…G—RG—RÂ7G&–ærÖWF†öDæÖRÂö&¦V7CõµÒ&w2¢°¢&WGW&âG—RävWDÖWF†öG2„&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢åv†W&R†ÖWF†öBÓâ7G&–æräWVÇ2†ÖWF†öBäæÖRÂÖWF†öDæÖRÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢åv†W&R†ÖWF†öBÓâ&ÖWFW'4ÖF6‚†ÖWF†öBävWE&ÖWFW'2‚’Â&w2’¢äf—'7D÷$FVfVÇB‚“°¢Ğ ¢&—fFR7FF–2ÖWF†öD–æfóòvWD6ö×F–&ÆU7FF–4ÖWF†öB…G—RG—RÂ7G&–ærÖWF†öDæÖRÂö&¦V7CõµÒ&w2¢°¢&WGW&âG—RävWDÖWF†öG2„&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢åv†W&R†ÖWF†öBÓâ7G&–æräWVÇ2†ÖWF†öBäæÖRÂÖWF†öDæÖRÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢åv†W&R†ÖWF†öBÓâ&ÖWFW'4ÖF6‚†ÖWF†öBävWE&ÖWFW'2‚’Â&w2’¢äf—'7D÷$FVfVÇB‚“°¢Ğ ¢&—fFR7FF–2&ööÂ&ÖWFW'4ÖF6‚…&ÖWFW$–æfõµÒ&ÖWFW'2Âö&¦V7CõµÒ&w2¢°¢–b‡&ÖWFW'2äÆVæwF‚Ò&w2äÆVæwF‚¢°¢&WGW&âfÇ6S°¢Ğ ¢f÷"†–çB’Ò²’Â&ÖWFW'2äÆVæwFƒ²’²²¢°¢ö&¦V7Cò&rÒ&w5¶•Ó°¢–b†&r—2çVÆÂ¢°¢–b‡&ÖWFW'5¶•Òå&ÖWFW%G—Rä—5fÇVUG—Rb`¢çVÆÆ&ÆRävWEVæFW&Ç––æuG—R‡&ÖWFW'5¶•Òå&ÖWFW%G—R’—2çVÆÂ¢°¢&WGW&âfÇ6S°¢Ğ ¢6öçF–çVS°¢Ğ ¢–b‚&ÖWFW'5¶•Òå&ÖWFW%G—Rä—476–væ&ÆTg&öÒ†&rävWEG—R‚’’¢°¢&WGW&âfÇ6S°¢Ğ¢Ğ ¢&WGW&âG'VS°¢Ğ ¢&—fFR7FF–2fö–B76W'DWVÂ†ö&¦V7BW‡V7FVBÂö&¦V7B7GVÂÂ7G&–ærFW67&—F–öâ¢°¢–b‚ö&¦V7BäWVÇ2†W‡V7FVBÂ7GVÂ’¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$W‡V7FVB¶FW67&—F–öçÒFò&Rw¶W‡V7FVGÒrÂ'WBf÷VæBw¶7GVÇÒrâ"“°¢Ğ¢Ğ ¢&—fFR7FF–2fö–B76W'E&÷W'G•G—R…G—RG—RÂ7G&–ær&÷W'G”æÖRÂG—RW‡V7FVEG—RÂ7G&–ærFW67&—F–öâ¢°¢&÷W'G”–æfò&÷W'G’ÒG—RävWE&÷W'G’€¢&÷W'G”æÖRÀ¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢óòF‡&÷ræWrÖ—76–ætÖVÖ&W$W†6WF–öâ‡G—RägVÆÄæÖRÂ&÷W'G”æÖR“° ¢–b‡&÷W'G’å&÷W'G•G—RÒW‡V7FVEG—R¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒG—RFò&Rw¶W‡V7FVEG—RägVÆÄæÖWÒrÂ'WBf÷VæBw·&÷W'G’å&÷W'G•G—RägVÆÄæÖWÒrâ"“°¢Ğ¢Ğ ¢&—fFR7FF–2ÖWF†öD–æfòf–æDÖWF†öD'•&ÖWFW$æÖW2…G—RG—RÂ7G&–ærÖWF†öDæÖRÂ7G&–æuµÒ&ÖWFW$æÖW2¢°¢ÖWF†öD–æfóòÖWF†öBÒG—P¢ävWDÖWF†öG2„&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢åv†W&R†ÖWF†öBÓâ7G&–æräWVÇ2†ÖWF†öBäæÖRÂÖWF†öDæÖRÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢äf—'7D÷$FVfVÇB†ÖWF†öBÓà¢°¢&ÖWFW$–æfõµÒ&ÖWFW'2ÒÖWF†öBävWE&ÖWFW'2‚“°¢&WGW&â&ÖWFW'2äÆVæwF‚ÓÒ&ÖWFW$æÖW2äÆVæwF‚b`¢&ÖWFW'0¢å6VÆV7B‡&ÖWFW"Óâ&ÖWFW"äæÖRóò7G&–æräV×G’¢å6WVVæ6TWVÂ‡&ÖWFW$æÖW2Â7G&–æt6ö×&W"ä÷&F–æÂ“°¢Ò“° ¢&WGW&âÖWF†öBóòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ€¢G—RägVÆÄæÖRÀ¢B'¶ÖWF†öDæÖWÒ‡·7G&–ærä¦ö–â‚"Â"Â&ÖWFW$æÖW2—Ò’"“°¢Ğ ¢&—fFR7FF–2ÖWF†öD–æfòf–æDÖWF†öD'”æÖTæE&ÖWFW$6÷VçB…G—RG—RÂ7G&–ærÖWF†öDæÖRÂ–çB&ÖWFW$6÷VçB¢°¢ÖWF†öD–æfóòÖWF†öBÒG—P¢ävWDÖWF†öG2„&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2å7FF–2Â&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢åv†W&R†ÖWF†öBÓâ7G&–æräWVÇ2†ÖWF†öBäæÖRÂÖWF†öDæÖRÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢äf—'7D÷$FVfVÇB†ÖWF†öBÓâÖWF†öBävWE&ÖWFW'2‚’äÆVæwF‚ÓÒ&ÖWFW$6÷VçB“° ¢&WGW&âÖWF†öBóòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡G—RägVÆÄæÖRÂB'¶ÖWF†öDæÖWÒ÷·&ÖWFW$6÷VçGÒ"“°¢Ğ ¢&—fFR7FF–2fö–B76W'E&ÖWFW%G—W2„ÖWF†öD–æfòÖWF†öBÂG—UµÒW‡V7FVE&ÖWFW%G—W2Â7G&–ærFW67&—F–öâ¢°¢&ÖWFW$–æfõµÒ&ÖWFW'2ÒÖWF†öBävWE&ÖWFW'2‚“°¢–b‡&ÖWFW'2äÆVæwF‚ÂW‡V7FVE&ÖWFW%G—W2äÆVæwF‚¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒFò†fRBÆV7B¶W‡V7FVE&ÖWFW%G—W2äÆVæwF‡Ò&ÖWFW'2Â'WBf÷VæB·&ÖWFW'2äÆVæwF‡Òâ"“°¢Ğ ¢f÷"†–çB’Ò²’ÂW‡V7FVE&ÖWFW%G—W2äÆVæwFƒ²’²²¢°¢–b‡&ÖWFW'5¶•Òå&ÖWFW%G—RÒW‡V7FVE&ÖWFW%G—W5¶•Ò¢°¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒ&ÖWFW"w·&ÖWFW'5¶•ÒäæÖWÒrG—RFò&Rw¶W‡V7FVE&ÖWFW%G—W5¶•ÒägVÆÄæÖWÒrÂ'WBf÷VæBw·&ÖWFW'5¶•Òå&ÖWFW%G—RägVÆÄæÖWÒrâ"“°¢Ğ¢Ğ¢Ğ ¢&—fFR7FF–2fö–B76W'E&÷W'G”vWGFW%&VfW&Væ6W4f–VÆB€¢G—RG—RÀ¢7G&–ær&÷W'G”æÖRÀ¢7G&–ærf–VÆDæÖRÀ¢7G&–ærFW67&—F–öâ¢°¢&÷W'G”–æfò&÷W'G’ÒG—RävWE&÷W'G’€¢&÷W'G”æÖRÀ¢&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2åV&Æ–2Â&–æF–ætfÆw2äæöåV&Æ–2¢óòF‡&÷ræWrÖ—76–ætÖVÖ&W$W†6WF–öâ‡G—RägVÆÄæÖRÂ&÷W'G”æÖR“°¢ÖWF†öD–æfòvWGFW"Ò&÷W'G’ävWDÖWF†ö@¢óòF‡&÷ræWrÖ—76–ætÖWF†öDW†6WF–öâ‡G—RägVÆÄæÖRÂ&÷W'G”æÖR²"ævWB"“°¢f–VÆD–æfòf–VÆBÒG—RävWDf–VÆB†f–VÆDæÖRÂ&–æF–ætfÆw2ä–ç7Fæ6RÂ&–æF–ætfÆw2äæöåV&Æ–2¢óòF‡&÷ræWrÖ—76–ætf–VÆDW†6WF–öâ‡G—RägVÆÄæÖRÂf–VÆDæÖR“°¢'—FUµÒ–ÂÒvWGFW"ävWDÖWF†öD&öG’‚“òävWD”Ä4'—FT'&’‚¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$W‡V7FVB¶FW67&—F–öçÒvWGFW"”Ââ"“°¢'—FUµÒf–VÆEFö¶VâÒ&—D6öçfW'FW"ävWD'—FW2†f–VÆBäÖWFFFFö¶Vâ“° ¢f÷"†–çB’Ò²’ÃÒ–ÂäÆVæwF‚Òf–VÆEFö¶VâäÆVæwFƒ²’²²¢°¢–b†–Âä57â†’Âf–VÆEFö¶VâäÆVæwF‚’å6WVVæ6TWVÂ†f–VÆEFö¶Vâ’¢°¢&WGW&ã°¢Ğ¢Ğ ¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$W‡V7FVB¶FW67&—F–öçÒvWGFW"Fò&VfW&Væ6Rw¶f–VÆDæÖWÒrâ"“°¢Ğ ¢&—fFR7FF–2fö–B76W'DÖWF†öD6ÆÇ4ÖWF†öB€¢ÖWF†öD–æfòÖWF†öBÀ¢7G&–ær6ÆÆVDFV6Æ&–æuG—TgVÆÄæÖRÀ¢7G&–ær6ÆÆVDÖWF†öDæÖRÀ¢7G&–ærFW67&—F–öâ¢°¢'—FUµÒ–ÂÒÖWF†öBävWDÖWF†öD&öG’‚“òävWD”Ä4'—FT'&’‚¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$W‡V7FVB¶FW67&—F–öçÒÖWF†öB”Ââ"“°¢G—UµÒG—T&wVÖVçG2ÒÖWF†öBäFV6Æ&–æuG—SòävWDvVæW&–4&wVÖVçG2‚’óòG—RäV×G•G—W3°¢G—UµÒÖWF†öD&wVÖVçG2ÒÖWF†öBävWDvVæW&–4&wVÖVçG2‚“° ¢f÷"†–çB’Ò²’ÃÒ–ÂäÆVæwF‚ÒS²’²²¢°¢–b†–Å¶•ÒÒƒ#‚bb–Å¶•ÒÒƒdb¢°¢6öçF–çVS°¢Ğ ¢–çBFö¶VâÒ&—D6öçfW'FW"åFô–çC3"†–ÂÂ’²“°¢ÖWF†öD&6Sò6ÆÆVDÖWF†öC°¢G'¢°¢6ÆÆVDÖWF†öBÒÖWF†öBäÖöGVÆRå&W6öÇfTÖWF†öB‡Fö¶VâÂG—T&wVÖVçG2ÂÖWF†öD&wVÖVçG2“°¢Ğ¢6F6‚„&wVÖVçDW†6WF–öâ¢°¢6öçF–çVS°¢Ğ ¢–b†6ÆÆVDÖWF†öBÒçVÆÂb`¢7G&–æräWVÇ2†6ÆÆVDÖWF†öBäæÖRÂ6ÆÆVDÖWF†öDæÖRÂ7G&–æt6ö×&—6öâä÷&F–æÂ’b`¢7G&–æräWVÇ2†6ÆÆVDÖWF†öBäFV6Æ&–æuG—SòägVÆÄæÖRÂ6ÆÆVDFV6Æ&–æuG—TgVÆÄæÖRÂ7G&–æt6ö×&—6öâä÷&F–æÂ’¢°¢&WGW&ã°¢Ğ¢Ğ ¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒw¶ÖWF†öBäFV6Æ&–æuG—SòägVÆÄæÖWÒç¶ÖWF†öBäæÖWÒrFò6ÆÂw¶6ÆÆVDFV6Æ&–æuG—TgVÆÄæÖWÒç¶6ÆÆVDÖWF†öDæÖWÒrâ"“°¢Ğ ¢&—fFR7FF–2fö–B76W'DÖWF†öD6ÆÇ57V6–f–4ÖWF†öB€¢ÖWF†öD–æfòÖWF†öBÀ¢ÖWF†öD–æfò6ÆÆVDÖWF†öBÀ¢7G&–ærFW67&—F–öâ¢°¢'—FUµÒ–ÂÒÖWF†öBävWDÖWF†öD&öG’‚“òävWD”Ä4'—FT'&’‚¢óòF‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚B$W‡V7FVB¶FW67&—F–öçÒÖWF†öB”Ââ"“°¢G—UµÒG—T&wVÖVçG2ÒÖWF†öBäFV6Æ&–æuG—SòävWDvVæW&–4&wVÖVçG2‚’óòG—RäV×G•G—W3°¢G—UµÒÖWF†öD&wVÖVçG2ÒÖWF†öBävWDvVæW&–4&wVÖVçG2‚“° ¢f÷"†–çB’Ò²’ÃÒ–ÂäÆVæwF‚ÒS²’²²¢°¢–b†–Å¶•ÒÒƒ#‚bb–Å¶•ÒÒƒdb¢°¢6öçF–çVS°¢Ğ ¢–çBFö¶VâÒ&—D6öçfW'FW"åFô–çC3"†–ÂÂ’²“°¢ÖWF†öD&6Sò&W6öÇfVDÖWF†öC°¢G'¢°¢&W6öÇfVDÖWF†öBÒÖWF†öBäÖöGVÆRå&W6öÇfTÖWF†öB‡Fö¶VâÂG—T&wVÖVçG2ÂÖWF†öD&wVÖVçG2“°¢Ğ¢6F6‚„&wVÖVçDW†6WF–öâ¢°¢6öçF–çVS°¢Ğ ¢–b‡&W6öÇfVDÖWF†öB—2ÖWF†öD–æfò&W6öÇfVD–æfòb`¢&W6öÇfVD–æfòäÖöGVÆRÓÒ6ÆÆVDÖWF†öBäÖöGVÆRb`¢&W6öÇfVD–æfòäÖWFFFFö¶VâÓÒ6ÆÆVDÖWF†öBäÖWFFFFö¶Vâ¢°¢&WGW&ã°¢Ğ¢Ğ ¢F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ€¢B$W‡V7FVB¶FW67&—F–öçÒw¶ÖWF†öBäFV6Æ&–æuG—SòägVÆÄæÖWÒç¶ÖWF†öBäæÖWÒrFò6ÆÂw¶6ÆÆVDÖWF†öBäFV6Æ&–æuG—SòägVÆÄæÖWÒç¶6ÆÆVDÖWF†öBäæÖWÒrv—F‚F†RfÆ–FFVB÷fW&ÆöBâ"“°¢Ğ ¢&—fFR&VFöæÇ’&V6÷&B7G'V7B6¶vT76VÖ&Ç”W‡V7FF–öâ€¢7G&–ær6¶vT–BÀ¢7G&–ær76VÖ&Ç•6–×ÆTæÖRÀ¢7G&–ærF&vWDg&ÖWv÷&²À¢7G&–ærV&Æ–4¶W•Fö¶Väw&÷W“°§Ğ 