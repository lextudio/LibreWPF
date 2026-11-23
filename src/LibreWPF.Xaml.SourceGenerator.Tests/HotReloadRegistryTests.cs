using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using LibreWPF.Xaml.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>
/// Compiles and runs the generated Debug Hot Reload handler against minimal stand-ins for the WPF types the
/// generated code touches, so the weak instance registry is checked by behaviour rather than by text: a window
/// that was dropped is pruned and not re-initialized, a live one is, an unrelated update is ignored, and running
/// <c>InitializeComponent</c> again does not register the same window twice.
/// </summary>
public class HotReloadRegistryTests
{
    private const string Xaml = """
        <Window x:Class="Sample.MainWindow"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Grid>
            <TextBlock x:Name="Status" Text="{cs: &quot;ready&quot;}" />
          </Grid>
        </Window>
        """;

    // Only what the generated InitializeComponent and handler use. Dispatcher runs the callback at once and
    // counts it, which is how the test sees which windows were re-initialized.
    private const string WpfStandIns = """
        namespace System.Windows.Threading
        {
            public enum DispatcherPriority { Normal }
            public sealed class Dispatcher
            {
                public static int Invocations;
                public object BeginInvoke(DispatcherPriority priority, System.Delegate method)
                {
                    Invocations++;
                    method.DynamicInvoke();
                    return null;
                }
            }
        }
        namespace System.Windows
        {
            public class DependencyObject { public System.Windows.Threading.Dispatcher Dispatcher { get; } = new(); }
            public class FrameworkElement : DependencyObject { public string Name { get; set; } }
            public class FrameworkContentElement : DependencyObject { public string Name { get; set; } }
            public class Window : FrameworkElement { public string Title { get; set; } public object Content { get; set; } }
            public sealed class NameScope
            {
                private readonly System.Collections.Generic.Dictionary<string, object> names = new();
                private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, NameScope> scopes = new();
                public static NameScope GetNameScope(DependencyObject o) => scopes.TryGetValue(o, out var s) ? s : null;
                public static void SetNameScope(DependencyObject o, NameScope s) => scopes.AddOrUpdate(o, s);
                public object FindName(string name) => names.TryGetValue(name, out var v) ? v : null;
                public void RegisterName(string name, object o) => names.Add(name, o);
                public void UnregisterName(string name) => names.Remove(name);
            }
        }
        namespace System.Windows.Controls
        {
            public class Panel : FrameworkElement { public System.Collections.Generic.List<object> Children { get; } = new(); }
            public class Grid : Panel { }
            public class TextBlock : FrameworkElement { public string Text { get; set; } }
        }
        namespace Sample
        {
            public partial class MainWindow : System.Windows.Window
            {
                public MainWindow() => InitializeComponent();
            }
        }
        """;

    [Fact]
    public void Registry_ReinitializesOnlyLiveWindows_AndPrunesCollectedOnes()
    {
        var context = new AssemblyLoadContext("xsg-registry", isCollectible: true);
        try {
            var assembly = context.LoadFromStream(new MemoryStream(Compile()));
            var windowType = assembly.GetType("Sample.MainWindow", throwOnError: true)!;
            var handler = windowType.GetNestedType("__XsgHotReload", BindingFlags.NonPublic)!;
            var update = handler.GetMethod("UpdateApplication", BindingFlags.Static | BindingFlags.NonPublic)!;
            var instances = (System.Collections.IList)handler.GetField("s_instances", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var invocations = assembly.GetType("System.Windows.Threading.Dispatcher", throwOnError: true)!
                .GetField("Invocations", BindingFlags.Static | BindingFlags.Public)!;

            var live = Activator.CreateInstance(windowType)!;
            CreateAndDrop(windowType);
            Assert.Equal(2, instances.Count);
            CollectGarbage();

            // An update to some other type leaves every window alone.
            update.Invoke(null, [new[] { typeof(object) }]);
            Assert.Equal(0, (int)invocations.GetValue(null)!);

            // A null update (the runtime could not say which types changed) re-initializes the live window
            // only, and the rerun of InitializeComponent does not register it a second time.
            update.Invoke(null, [null]);
            Assert.Equal(1, (int)invocations.GetValue(null)!);
            Assert.Single(instances);

            // So does an update naming the window's own type.
            update.Invoke(null, [new[] { windowType }]);
            Assert.Equal(2, (int)invocations.GetValue(null)!);
            Assert.Single(instances);
            GC.KeepAlive(live);
        } finally {
            context.Unload();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateAndDrop(Type windowType) => Activator.CreateInstance(windowType);

    private static void CollectGarbage()
    {
        for (var i = 0; i < 3; i++) {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static byte[] Compile()
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: ["DEBUG"]);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Sample",
            [CSharpSyntaxTree.ParseText(WpfStandIns, parse)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Disable));
        var driver = CSharpGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new InMemoryText("/src/MainWindow.xaml", Xaml)], parseOptions: parse);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var generatorDiagnostics);
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        using var image = new MemoryStream();
        var emit = generated.Emit(image);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return image.ToArray();
    }

    private sealed class InMemoryText(string path, string text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
