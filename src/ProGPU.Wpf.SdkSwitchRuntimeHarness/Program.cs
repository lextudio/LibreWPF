using System.Collections;
using System.Reflection;
using System.Runtime.Loader;

internal static class Program
{
    private const string SmokeAssemblyName = "ProGPU.Wpf.SdkSwitchSmoke";
    private const string AppTypeName = "ProGPU.Wpf.SdkSwitchSmoke.App";
    private const string MainWindowTypeName = "ProGPU.Wpf.SdkSwitchSmoke.MainWindow";

    [STAThread]
    private static int Main()
    {
        try
        {
            string repoRoot = FindRepoRoot();
            string smokeAssemblyPath = Path.Combine(
                repoRoot,
                "artifacts",
                "bin",
                SmokeAssemblyName,
                "Debug",
                "net11.0",
                SmokeAssemblyName + ".dll");
            string wpfRoot = Path.Combine(repoRoot, "artifacts", "progpu-wpf-sdk-smoke", "wpf");
            string proGpuRoot = Path.Combine(repoRoot, "artifacts", "progpu-wpf-sdk-smoke", "progpu");

            RequireFile(smokeAssemblyPath, "SDK switch smoke assembly");
            RequireDirectory(wpfRoot, "ported WPF artifact root");
            RequireDirectory(proGpuRoot, "ProGPU artifact root");

            using var loadContext = new SdkSmokeLoadContext(repoRoot, smokeAssemblyPath, wpfRoot, proGpuRoot);
            Assembly smokeAssembly = loadContext.LoadFromAssemblyPath(smokeAssemblyPath);

            object app = Create(smokeAssembly, AppTypeName);
            InvokeVoid(app, "InitializeComponent");
            ValidateApp(app);

            object window = Create(smokeAssembly, MainWindowTypeName);
            ValidateWindow(window);

            TryInvoke(app, "Shutdown");

            Console.WriteLine("ProGPU WPF SDK switch runtime smoke succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void ValidateApp(object app)
    {
        AssertEqual("MainWindow.xaml", GetProperty(app, "StartupUri").ToString() ?? string.Empty, "startup URI");

        object resources = GetProperty(app, "Resources");
        object accentBrush = Invoke(app, "TryFindResource", "SmokeAccentBrush");
        AssertType(accentBrush, "System.Windows.Media.SolidColorBrush", "application accent brush");
        AssertEqual("#FF356D9E", GetProperty(accentBrush, "Color").ToString() ?? string.Empty, "application accent brush color");
        AssertAtLeast(1, GetCount(GetProperty(resources, "Keys")), "application resource key count");
    }

    private static void ValidateWindow(object window)
    {
        AssertAssignableTo(window, "System.Windows.Window", "SDK smoke main window");
        AssertEqual("ProGPU WPF SDK Smoke", GetProperty(window, "Title"), "window title");
        AssertEqual(320.0, GetProperty(window, "Width"), "window width");
        AssertEqual(180.0, GetProperty(window, "Height"), "window height");

        object message = Invoke(window, "FindName", "Message");
        AssertType(message, "System.Windows.Controls.TextBlock", "message element");
        AssertEqual("ProGPU WPF SDK switch smoke", GetProperty(message, "Text"), "message text");

        object actionButton = Invoke(window, "FindName", "ActionButton");
        AssertType(actionButton, "System.Windows.Controls.Button", "action button");
        AssertEqual("ProGPU WPF SDK switch smoke", GetProperty(actionButton, "Content"), "button bound content");
    }

    private static object Create(Assembly assembly, string typeName)
    {
        Type type = assembly.GetType(typeName, throwOnError: true)!;
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Could not create '{typeName}'.");
    }

    private static object Invoke(object instance, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleMethod(instance.GetType(), methodName, args)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        try
        {
            return method.Invoke(instance, args)
                ?? throw new InvalidOperationException($"Method '{methodName}' returned null.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void InvokeVoid(object instance, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleMethod(instance.GetType(), methodName, args)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        try
        {
            method.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void TryInvoke(object instance, string methodName)
    {
        MethodInfo? method = GetCompatibleMethod(instance.GetType(), methodName, Array.Empty<object?>());
        method?.Invoke(instance, null);
    }

    private static MethodInfo? GetCompatibleMethod(Type type, string methodName, object?[] args)
    {
        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .Where(method => ParametersMatch(method.GetParameters(), args))
            .OrderBy(method => GetDeclaringTypeDistance(type, method.DeclaringType))
            .FirstOrDefault();
    }

    private static bool ParametersMatch(ParameterInfo[] parameters, object?[] args)
    {
        if (parameters.Length != args.Length)
        {
            return false;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            object? arg = args[i];
            if (arg is null)
            {
                if (parameters[i].ParameterType.IsValueType &&
                    Nullable.GetUnderlyingType(parameters[i].ParameterType) is null)
                {
                    return false;
                }

                continue;
            }

            if (!parameters[i].ParameterType.IsAssignableFrom(arg.GetType()))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetDeclaringTypeDistance(Type actualType, Type? declaringType)
    {
        int distance = 0;
        for (Type? type = actualType; type is not null; type = type.BaseType)
        {
            if (type == declaringType)
            {
                return distance;
            }

            distance++;
        }

        return int.MaxValue;
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

    private static int GetCount(object collection)
    {
        if (collection is ICollection nonGenericCollection)
        {
            return nonGenericCollection.Count;
        }

        PropertyInfo? countProperty = collection.GetType().GetProperty(
            "Count",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (countProperty?.GetValue(collection) is object count)
        {
            return Convert.ToInt32(count);
        }

        throw new MissingMemberException(collection.GetType().FullName, "Count");
    }

    private static void AssertType(object value, string expectedTypeName, string description)
    {
        if (!string.Equals(value.GetType().FullName, expectedTypeName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{description}: expected type '{expectedTypeName}', actual '{value.GetType().FullName}'.");
        }
    }

    private static void AssertAssignableTo(object value, string expectedBaseTypeName, string description)
    {
        for (Type? type = value.GetType(); type is not null; type = type.BaseType)
        {
            if (string.Equals(type.FullName, expectedBaseTypeName, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"{description}: expected assignable to '{expectedBaseTypeName}', actual '{value.GetType().FullName}'.");
    }

    private static void AssertEqual(object expected, object actual, string description)
    {
        if (!object.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{description}: expected '{expected}', actual '{actual}'.");
        }
    }

    private static void AssertAtLeast(int expectedMinimum, object actualValue, string description)
    {
        int actual = Convert.ToInt32(actualValue);
        if (actual < expectedMinimum)
        {
            throw new InvalidOperationException($"{description}: expected at least {expectedMinimum}, actual {actual}.");
        }
    }

    private static void RequireFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{description} was not found.", path);
        }
    }

    private static void RequireDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"{description} was not found: {path}");
        }
    }

    private static string FindRepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "global.json")) &&
                Directory.Exists(Path.Combine(directory, "src", "Microsoft.DotNet.Wpf")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the WPF repository root.");
    }

    private sealed class SdkSmokeLoadContext : AssemblyLoadContext, IDisposable
    {
        private readonly string _repoRoot;
        private readonly string _wpfRoot;
        private readonly string _proGpuRoot;
        private readonly string _smokeAssemblyPath;
        private readonly AssemblyDependencyResolver _resolver;

        public SdkSmokeLoadContext(string repoRoot, string smokeAssemblyPath, string wpfRoot, string proGpuRoot)
            : base(isCollectible: true)
        {
            _repoRoot = repoRoot;
            _smokeAssemblyPath = smokeAssemblyPath;
            _wpfRoot = wpfRoot;
            _proGpuRoot = proGpuRoot;
            _resolver = new AssemblyDependencyResolver(typeof(Program).Assembly.Location);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? assemblyPath = TryResolveAssemblyPath(assemblyName);
            return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
        }

        private string? TryResolveAssemblyPath(AssemblyName assemblyName)
        {
            string fileName = assemblyName.Name + ".dll";
            string? path = assemblyName.Name switch
            {
                SmokeAssemblyName => _smokeAssemblyPath,
                "WindowsBase" or "System.Xaml" or "PresentationCore" or "PresentationFramework" or "PresentationUI" or "ReachFramework" or "System.Printing" =>
                    Path.Combine(_wpfRoot, fileName),
                "ProGPU.Wpf" or "ProGPU.Backend" or "ProGPU.Scene" or "ProGPU.Vector" or "ProGPU.Text" =>
                    Path.Combine(_proGpuRoot, fileName),
                _ => null
            };

            if (path is not null && File.Exists(path))
            {
                return path;
            }

            path = TryFindArtifactAssembly(assemblyName.Name, "net11.0")
                ?? TryFindArtifactAssembly(assemblyName.Name, "net10.0")
                ?? _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null && File.Exists(path) ? path : null;
        }

        private string? TryFindArtifactAssembly(string? assemblyName, string targetFramework)
        {
            if (string.IsNullOrEmpty(assemblyName))
            {
                return null;
            }

            string path = Path.Combine(
                _repoRoot,
                "artifacts",
                "bin",
                assemblyName,
                "Debug",
                targetFramework,
                assemblyName + ".dll");
            return File.Exists(path) ? path : null;
        }

        public void Dispose()
        {
            Unload();
        }
    }
}
