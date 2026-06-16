using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Windows.Media.ProGPU;
using ProGpuContainerVisual = global::ProGPU.Scene.ContainerVisual;
using ProGpuDrawingContext = global::ProGPU.Scene.DrawingContext;
using ProGpuRenderCommand = global::ProGPU.Scene.RenderCommand;
using ProGpuRenderCommandType = global::ProGPU.Scene.RenderCommandType;
using ProGpuVisual = global::ProGPU.Scene.Visual;

internal static class Program
{
    private const string ProviderTypeName = "System.Windows.Media.RenderDataDrawingContextSinkProvider";
    private const string SinkInterfaceTypeName = "System.Windows.Media.IRenderDataDrawingContextSink";

    private static int Main()
    {
        try
        {
            string repoRoot = FindRepoRoot();
            string presentationCorePath = FindRealPresentationCoreAssembly(repoRoot);
            RunHarness(repoRoot, presentationCorePath);
            Console.WriteLine("Real PresentationCore render-data provider registration succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RunHarness(string repoRoot, string presentationCorePath)
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var frame = target.BeginDrawingFrame(64, 32);

        var loadContext = new WpfAssemblyLoadContext(repoRoot, presentationCorePath);
        Assembly presentationCore = loadContext.LoadFromAssemblyPath(presentationCorePath);

        Type providerType = GetRequiredType(presentationCore, ProviderTypeName);
        Type drawingVisualType = GetRequiredType(presentationCore, "System.Windows.Media.DrawingVisual");
        Type sinkInterfaceType = GetRequiredType(presentationCore, SinkInterfaceTypeName);

        MethodInfo createSink = providerType.GetMethod(
            "CreateSink",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(providerType.FullName, "CreateSink");

        if (!WpfRenderDataSinkProviderBridge.TryRegisterRenderDataSinkProvider(
                presentationCore,
                frame,
                imageSourceAdapter: null,
                out IDisposable? registration) ||
            registration == null)
        {
            throw new InvalidOperationException("Failed to register ProGPU object sink factory against real PresentationCore.");
        }

        object ownerVisual = RuntimeHelpers.GetUninitializedObject(drawingVisualType);

        using (registration)
        {
            object sink = createSink.Invoke(null, new[] { ownerVisual })
                ?? throw new InvalidOperationException("Real PresentationCore provider returned a null sink.");

            if (frame.ObjectRenderDataSinkContextCount != 1 ||
                frame.DrawingContextCount != 1 ||
                !ReferenceEquals(frame.LastOwnerVisual, ownerVisual))
            {
                throw new InvalidOperationException(
                    $"Expected one object sink, one drawing context, and the owner visual; got object sinks={frame.ObjectRenderDataSinkContextCount}, drawing contexts={frame.DrawingContextCount}, owner={frame.LastOwnerVisual}.");
            }

            MethodInfo pushOpacity = sinkInterfaceType.GetMethod(
                "PushOpacity",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(double) },
                modifiers: null)
                ?? throw new MissingMethodException(sinkInterfaceType.FullName, "PushOpacity");

            MethodInfo pop = sinkInterfaceType.GetMethod(
                "Pop",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null)
                ?? throw new MissingMethodException(sinkInterfaceType.FullName, "Pop");

            MethodInfo close = sinkInterfaceType.GetMethod(
                "Close",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null)
                ?? throw new MissingMethodException(sinkInterfaceType.FullName, "Close");

            pushOpacity.Invoke(sink, new object[] { 0.5 });
            pop.Invoke(sink, Array.Empty<object>());
            close.Invoke(sink, Array.Empty<object>());
        }

        if (target.RootVisual.Context.Commands.Count != 0)
        {
            throw new InvalidOperationException(
                $"Expected real provider RenderOpen commands to use the retained WPF owner branch, but the flat root received {target.RootVisual.Context.Commands.Count} commands.");
        }

        ProGpuContainerVisual retainedFrameRoot = GetSingleContainerChild(
            target.RetainedWpfVisualRoot,
            "retained WPF frame root");
        ProGpuVisual ownerBranch = GetSingleChild(
            retainedFrameRoot,
            "real provider owner branch");

        if (!target.RetainedVisualBranchMap.TryGetVisuals(ownerVisual, out IReadOnlyList<ProGpuVisual> ownerVisuals) ||
            ownerVisuals.Count != 1 ||
            !ReferenceEquals(ownerVisuals[0], ownerBranch))
        {
            throw new InvalidOperationException("Real PresentationCore owner visual was not mapped to the retained ProGPU owner branch.");
        }

        IReadOnlyList<ProGpuRenderCommand> commands = GetRetainedCommands(ownerBranch);
        if (commands.Count != 2 ||
            commands[0].Type != ProGpuRenderCommandType.PushOpacity ||
            commands[1].Type != ProGpuRenderCommandType.PopOpacity)
        {
            throw new InvalidOperationException(
                $"Expected retained owner branch PushOpacity/PopOpacity commands after real sink dispatch, got {commands.Count} commands.");
        }

        object restoredOwnerVisual = RuntimeHelpers.GetUninitializedObject(drawingVisualType);
        object? restoredSink = createSink.Invoke(null, new[] { restoredOwnerVisual });
        if (restoredSink != null)
        {
            throw new InvalidOperationException("Real PresentationCore sink provider did not restore to null after disposing registration.");
        }

        loadContext.Unload();
    }

    private static ProGpuContainerVisual GetSingleContainerChild(ProGpuContainerVisual parent, string description)
    {
        ProGpuVisual visual = GetSingleChild(parent, description);
        return visual as ProGpuContainerVisual
            ?? throw new InvalidOperationException($"Expected {description} to be a container visual, got {visual.GetType().FullName}.");
    }

    private static ProGpuVisual GetSingleChild(ProGpuContainerVisual parent, string description)
    {
        if (parent.Children.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one {description}, got {parent.Children.Count} children.");
        }

        return parent.Children[0];
    }

    private static IReadOnlyList<ProGpuRenderCommand> GetRetainedCommands(ProGpuVisual visual)
    {
        PropertyInfo contextProperty = visual.GetType().GetProperty(
            "Context",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Retained owner branch type '{visual.GetType().FullName}' does not expose a drawing context.");

        ProGpuDrawingContext context = contextProperty.GetValue(visual) as ProGpuDrawingContext
            ?? throw new InvalidOperationException(
                $"Retained owner branch type '{visual.GetType().FullName}' exposed an unexpected context value.");

        return context.Commands;
    }

    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)
            ?? throw new TypeLoadException($"Could not load '{typeName}' from '{assembly.FullName}'.");
    }

    private static string FindRealPresentationCoreAssembly(string repoRoot)
    {
        string artifactsRoot = Path.Combine(repoRoot, "artifacts", "bin", "PresentationCore");
        if (!Directory.Exists(artifactsRoot))
        {
            throw new DirectoryNotFoundException($"Real PresentationCore artifacts directory was not found: {artifactsRoot}");
        }

        string[] candidates = Directory.GetFiles(
            artifactsRoot,
            "PresentationCore.dll",
            SearchOption.AllDirectories);

        string? selected = candidates
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}net11.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return selected
            ?? throw new FileNotFoundException("Could not locate a net11.0 real PresentationCore.dll artifact.", artifactsRoot);
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
                "PresentationCore",
                "PresentationCore.csproj");

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
        private readonly string _presentationCorePath;
        private readonly AssemblyDependencyResolver _resolver;

        public WpfAssemblyLoadContext(string repoRoot, string presentationCorePath)
            : base(isCollectible: true)
        {
            _repoRoot = repoRoot;
            _presentationCorePath = presentationCorePath;
            _resolver = new AssemblyDependencyResolver(presentationCorePath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, "PresentationCore", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationCorePath);
            }

            string artifactAssemblyPath = Path.Combine(
                _repoRoot,
                "artifacts",
                "bin",
                assemblyName.Name ?? string.Empty,
                "Debug",
                "net11.0",
                $"{assemblyName.Name}.dll");

            if (File.Exists(artifactAssemblyPath))
            {
                return LoadFromAssemblyPath(artifactAssemblyPath);
            }

            string? resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath == null ? null : LoadFromAssemblyPath(resolvedPath);
        }
    }
}
