using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;

namespace ProGPU.Wpf.SdkSwitchSmoke;

public partial class App : Application
{
    private const string PackageVersion = "11.0.0-dev";

    public int StartupEventCount { get; private set; }

    public int StartupArgsLength { get; private set; } = -1;

    public bool SdkOutputGuardChecked { get; private set; }

    public int ExitEventCount { get; private set; }

    public int LastExitCode { get; private set; } = -1;

    private void OnAppStartup(object sender, StartupEventArgs e)
    {
        ValidateSdkRenderSurfaceOutput();
        SdkOutputGuardChecked = true;
        StartupEventCount++;
        StartupArgsLength = e.Args.Length;
        Resources["StartupInjectedBrush"] = new SolidColorBrush(Color.FromRgb(0x7A, 0x4E, 0xB2));
        Resources["StartupInjectedText"] = "startup resource value";
    }

    private void OnAppExit(object sender, ExitEventArgs e)
    {
        ExitEventCount++;
        LastExitCode = e.ApplicationExitCode;
    }

    private static void ValidateSdkRenderSurfaceOutput()
    {
        Assembly proGpuWpf = LoadRequiredAssembly("ProGPU.Wpf");
        Assembly proGpuScene = LoadRequiredAssembly("ProGPU.Scene");
        Assembly proGpuBackend = LoadRequiredAssembly("ProGPU.Backend");
        Assembly silkNetMaths = LoadRequiredAssembly("Silk.NET.Maths");

        Type hostType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfWindowHost");
        Type compositionTargetType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfCompositionTarget");
        Type compositorType = GetRequiredType(proGpuScene, "ProGPU.Scene.Compositor");
        Type displayScaleResolverType = GetRequiredType(proGpuBackend, "ProGPU.Backend.DisplayScaleResolver");
        Type vector2DIntType = GetRequiredType(silkNetMaths, "Silk.NET.Maths.Vector2D`1").MakeGenericType(typeof(int));

        RequireMethodByParameterNames(displayScaleResolverType, "ResolveWindowDisplayScale", "window", "monitorDpiScale");
        MethodInfo resolveDisplayScale = RequireMethodByParameterNames(
            displayScaleResolverType,
            "ResolveDisplayScaleWithPlatformFallback",
            "monitorDpiScale",
            "platformDpiScaleProvider");
        MethodInfo resolveGeometry = RequireMethodByParameterNames(
            hostType,
            "ResolveRenderSurfaceGeometry",
            "clientWidth",
            "clientHeight",
            "framebufferSize",
            "monitorDpiScale");

        RequireMethodByParameterNames(
            hostType,
            "Present",
            "logicalWidth",
            "logicalHeight",
            "pixelWidth",
            "pixelHeight",
            "dpiScale");
        RequireMethodByParameterNames(hostType, "SynchronizePortablePresentationSourceGeometry", "geometry");
        RequireMethodByParameterNames(
            compositionTargetType,
            "Render",
            "logicalWidth",
            "logicalHeight",
            "pixelWidth",
            "pixelHeight",
            "dpiScale",
            "targetView");
        RequireMethodByParameterNames(
            compositorType,
            "RenderScene",
            "root",
            "logicalWidth",
            "logicalHeight",
            "renderTargetWidth",
            "renderTargetHeight",
            "dpiScale",
            "targetView");

        double dpiScale = Convert.ToDouble(
            resolveDisplayScale.Invoke(null, new object?[] { 1.0, new Func<double?>(() => 2.0) }));
        AssertEqual(2.0, dpiScale, "SDK smoke Retina display-scale fallback");

        object framebufferSize = Activator.CreateInstance(vector2DIntType, 840, 1680)
            ?? throw new InvalidOperationException("Could not create Silk.NET Retina framebuffer size.");
        object geometry = resolveGeometry.Invoke(null, new object?[] { 420, 840, framebufferSize, dpiScale })
            ?? throw new InvalidOperationException("SDK render-surface geometry returned null.");

        AssertEqual(420u, GetProperty(geometry, "LogicalWidth"), "SDK smoke Retina logical width");
        AssertEqual(840u, GetProperty(geometry, "LogicalHeight"), "SDK smoke Retina logical height");
        AssertEqual(840u, GetProperty(geometry, "PixelWidth"), "SDK smoke Retina physical width");
        AssertEqual(1680u, GetProperty(geometry, "PixelHeight"), "SDK smoke Retina physical height");
        AssertEqual(2.0, GetProperty(geometry, "DpiScale"), "SDK smoke Retina DPI scale");

        ValidateRuntimeAssetMatchesLocalPackage(proGpuWpf, "ProGPU.Wpf", "ProGPU.Wpf", "net10.0");
        ValidateRuntimeAssetMatchesLocalPackage(proGpuScene, "ProGPU.Scene", "ProGPU.Scene", "net10.0");
        ValidateRuntimeAssetMatchesLocalPackage(proGpuBackend, "ProGPU.Backend", "ProGPU.Backend", "net10.0");
    }

    private static Assembly LoadRequiredAssembly(string assemblyName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"SDK smoke output is missing required assembly '{assemblyName}'. Rebuild the package-mode smoke output.",
                ex);
        }
    }

    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)
            ?? throw new TypeLoadException($"Could not load type '{typeName}' from '{assembly.FullName}'.");
    }

    private static MethodInfo RequireMethodByParameterNames(Type type, string methodName, params string[] parameterNames)
    {
        MethodInfo? method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, methodName, StringComparison.Ordinal) &&
                candidate.GetParameters()
                    .Select(parameter => parameter.Name ?? string.Empty)
                    .SequenceEqual(parameterNames));

        return method
            ?? throw new MissingMethodException(
                type.FullName,
                $"{methodName}({string.Join(", ", parameterNames)})");
    }

    private static object? GetProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(instance)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
    }

    private static void ValidateRuntimeAssetMatchesLocalPackage(
        Assembly assembly,
        string packageId,
        string assemblySimpleName,
        string targetFramework)
    {
        if (!TryFindLocalPackageFeed(out string packageFeed))
        {
            return;
        }

        string runtimeAssemblyPath = assembly.Location;
        if (string.IsNullOrWhiteSpace(runtimeAssemblyPath) || !File.Exists(runtimeAssemblyPath))
        {
            throw new InvalidOperationException(
                $"SDK smoke output could not locate loaded assembly '{assemblySimpleName}'. Rebuild the package-mode SDK smoke output.");
        }

        string packagePath = Path.Combine(packageFeed, $"{packageId}.{PackageVersion}.nupkg");
        if (!File.Exists(packagePath))
        {
            throw new InvalidOperationException(
                $"SDK smoke output could not find local package '{packagePath}'. Repack the local SDK feed.");
        }

        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string entryName = $"lib/{targetFramework}/{assemblySimpleName}.dll";
        ZipArchiveEntry entry = package.GetEntry(entryName)
            ?? throw new InvalidOperationException(
                $"SDK smoke local package '{packageId}' is missing '{entryName}'. Repack the local SDK feed.");

        using Stream entryStream = entry.Open();
        string packageHash = ComputeStreamSha256(entryStream);
        string runtimeHash = ComputeFileSha256(runtimeAssemblyPath);
        if (!string.Equals(packageHash, runtimeHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SDK smoke loaded '{assemblySimpleName}.dll' does not match '{packageId}.{PackageVersion}.nupkg'. Rebuild the package-mode SDK smoke output.");
        }
    }

    private static bool TryFindLocalPackageFeed(out string packageFeed)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "artifacts",
                "packages",
                "Release",
                "NonShipping");
            if (Directory.Exists(candidate))
            {
                packageFeed = candidate;
                return true;
            }
        }

        packageFeed = string.Empty;
        return false;
    }

    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return ComputeStreamSha256(stream);
    }

    private static string ComputeStreamSha256(Stream stream)
    {
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AssertEqual(object expected, object? actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{description} expected '{expected}' but got '{actual}'. Rebuild the package-mode SDK smoke output.");
        }
    }
}
