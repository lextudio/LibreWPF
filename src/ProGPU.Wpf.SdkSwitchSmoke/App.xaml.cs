using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace ProGPU.Wpf.SdkSwitchSmoke;

public partial class App : Application
{
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

    private static void AssertEqual(object expected, object? actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{description} expected '{expected}' but got '{actual}'. Rebuild the package-mode SDK smoke output.");
        }
    }
}
