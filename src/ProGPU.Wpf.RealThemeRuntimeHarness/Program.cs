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
        object richTextBoxStyle = GetDictionaryValue(themeDictionary, "DefaultRichTextBoxStyle");

        SetProperty(window, "Style", windowStyle);

        object content = GetProperty(window, "Content");
        object children = GetProperty(content, "Children");
        object richTextBox = GetCollectionItem(children, 2);
        SetProperty(richTextBox, "Style", richTextBoxStyle);

        object button = Create(presentationFramework, "System.Windows.Controls.Button");
        SetProperty(button, "Content", "themed button smoke");
        SetProperty(button, "Style", buttonStyle);
        AddToCollection(children, button);

        AssertSame(windowStyle, GetProperty(window, "Style"), "Window Fluent style");
        AssertSame(buttonStyle, GetProperty(button, "Style"), "Button Fluent style");
        AssertSame(richTextBoxStyle, GetProperty(richTextBox, "Style"), "RichTextBox Fluent style");
        AssertSame(buttonStyle, Invoke(application, "TryFindResource", "AccentButtonStyle"), "application Fluent resource lookup");
    }

    private static void ValidateThemedRuntimeState(object window, object application, object themeDictionary)
    {
        object content = GetProperty(window, "Content");
        object children = GetProperty(content, "Children");
        AssertCollectionCount(children, expectedMinimum: 11, "themed stack panel children");

        object button = GetCollectionItem(children, 10);
        object richTextBox = GetCollectionItem(children, 2);

        AssertType(GetDictionaryValue(themeDictionary, "DefaultWindowStyle"), "System.Windows.Style", "DefaultWindowStyle");
        AssertType(GetDictionaryValue(themeDictionary, "AccentButtonStyle"), "System.Windows.Style", "AccentButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultRichTextBoxStyle"), "System.Windows.Style", "DefaultRichTextBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "WindowTemplateKey"), "System.Windows.Controls.ControlTemplate", "WindowTemplateKey");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultControlContextMenu"), "System.Windows.Controls.ContextMenu", "DefaultControlContextMenu");

        AssertStyleTarget(GetProperty(window, "Style"), "System.Windows.Window", "Window Fluent style target");
        AssertStyleTarget(GetProperty(button, "Style"), "System.Windows.Controls.Button", "Button Fluent style target");
        AssertStyleTarget(GetProperty(richTextBox, "Style"), "System.Windows.Controls.RichTextBox", "RichTextBox Fluent style target");

        Invoke(window, "ApplyTemplate");
        Invoke(button, "ApplyTemplate");
        Invoke(richTextBox, "ApplyTemplate");

        AssertType(GetProperty(window, "Template"), "System.Windows.Controls.ControlTemplate", "Window template");
        AssertType(GetProperty(button, "Template"), "System.Windows.Controls.ControlTemplate", "Button template");
        AssertType(GetProperty(richTextBox, "Template"), "System.Windows.Controls.ControlTemplate", "RichTextBox template");
        AssertStyleHasSetter(GetProperty(richTextBox, "Style"), "ContextMenu", "RichTextBox Fluent context-menu setter");
        AssertEqual("themed button smoke", GetProperty(button, "Content"), "themed button content");

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
        object countValue =
            collection is Array array ? array.Length :
            collection is ICollection nonGenericCollection ? nonGenericCollection.Count :
            GetProperty(collection, "Count");
        int count = Convert.ToInt32(countValue);
        if (count < expectedMinimum)
        {
            throw new InvalidOperationException($"Expected {description} to contain at least {expectedMinimum} items, got {count}.");
        }
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
