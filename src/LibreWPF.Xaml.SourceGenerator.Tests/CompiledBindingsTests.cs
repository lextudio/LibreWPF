using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using LibreWPF.Xaml.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>
/// Compiled bindings (<c>LibreWpfCompiledBindings</c>) and loose ResourceDictionaries (themes) on the XSG path.
///
/// A compiled binding is a stock <c>Binding</c> whose <c>PropertyPath</c> carries one generated PropertyDescriptor per step, the
/// early-bound accessor WPF's PropertyPathWorker already accepts: it reads, writes and listens through the descriptor, so the
/// engine never reflects over the source type. The runtime tests compile the generated code against small WPF stand-ins (the real
/// PropertyDescriptor from the BCL) and drive the descriptors the way the binding engine does.
/// </summary>
public class CompiledBindingsTests
{
    private const string ViewModels = """
        namespace Sample.Vm
        {
            public class Person : System.ComponentModel.INotifyPropertyChanged
            {
                private string _name = "Ada";
                public string Name { get => _name; set { _name = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Name))); } }
                public int Age { get; } = 36;
                public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            }
            public class MainVm
            {
                public Person Person { get; set; } = new Person();
                public System.Collections.Generic.List<Person> People { get; } = new();
            }
        }
        """;

    private const string Window = """
        <Window x:Class="Sample.MainWindow"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vm="clr-namespace:Sample.Vm"
                x:DataType="vm:MainVm">
          <Grid>
            <TextBox Text="{Binding Person.Name, Mode=TwoWay}" />
            <TextBlock Text="{Binding Path=Person.Age}" />
          </Grid>
        </Window>
        """;

    // ---- generated code -------------------------------------------------------------------------------------------------------

    [Fact]
    public void UnderXDataType_APropertyPathIsCompiledToTypedDescriptors()
    {
        var (code, diagnostics) = Generate(Window);
        Assert.Empty(diagnostics);
        Assert.Contains("new global::System.Windows.PropertyPath(\"(0).(1)\", new __XsgCompiledProperty<global::Sample.Vm.MainVm, global::Sample.Vm.Person>(\"Person\"", code);
        Assert.Contains("new __XsgCompiledProperty<global::Sample.Vm.Person, string>(\"Name\", static o => o.Name!, static (o, v) => o.Name = v)", code);
        Assert.Contains("Mode = global::System.Windows.Data.BindingMode.TwoWay", code);
        Assert.Contains("private sealed class __XsgCompiledProperty<TOwner, TValue>", code);
        Assert.DoesNotContain("Binding(\"Person.Name\")", code);
        Assert.DoesNotContain(".DataType =", code); // a directive, never a property
    }

    [Fact]
    public void AReadOnlyProperty_HasNoSetter()
    {
        var (code, _) = Generate(Window);
        Assert.Contains("new __XsgCompiledProperty<global::Sample.Vm.Person, int>(\"Age\", static o => o.Age!, null)", code);
    }

    [Fact]
    public void WithCompiledBindingsOff_EveryBindingIsTheReflectiveOne()
    {
        var (code, diagnostics) = Generate(Window, ("LibreWpfCompiledBindings", "false"));
        Assert.Empty(diagnostics);
        Assert.Contains("new global::System.Windows.Data.Binding(\"Person.Name\")", code);
        Assert.DoesNotContain("__XsgCompiledProperty", code);
    }

    [Fact]
    public void WithoutXDataType_NothingIsCompiledAndNothingIsReported()
    {
        var (code, diagnostics) = Generate(Window.Replace("x:DataType=\"vm:MainVm\"", ""));
        Assert.Empty(diagnostics);
        Assert.DoesNotContain("__XsgCompiledProperty", code);
    }

    [Fact]
    public void AWrongPath_IsABuildErrorAtTheBinding()
    {
        var (_, diagnostics) = Generate(Window.Replace("Person.Name", "Person.Nmae"));
        var error = Assert.Single(diagnostics);
        Assert.Equal("LWXSG0006", error.Id);
        Assert.Contains("MainWindow.xaml(7,", error.GetMessage());
        Assert.Contains("'Nmae'", error.GetMessage());
    }

    [Fact]
    public void AnUnknownDataType_IsABuildError()
    {
        var (_, diagnostics) = Generate(Window.Replace("vm:MainVm", "vm:Missing"));
        Assert.Contains(diagnostics, d => d.Id == "LWXSG0005");
    }

    [Fact]
    public void APathThatCannotBeCompiled_FallsBackWithAWarning()
    {
        var (code, diagnostics) = Generate(Window.Replace("Person.Name, Mode=TwoWay", "People[0].Name"));
        Assert.Equal("LWXSG0007", Assert.Single(diagnostics).Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostics[0].Severity);
        Assert.Contains("Binding(\"People[0].Name\")", code);
    }

    [Fact]
    public void CompileBindingsFalse_TurnsCompilationOffBelowIt()
    {
        var (code, diagnostics) = Generate(Window.Replace("<TextBox ", "<TextBox x:CompileBindings=\"False\" "));
        Assert.Empty(diagnostics);
        Assert.Contains("Binding(\"Person.Name\")", code);
        Assert.Contains("__XsgCompiledProperty<global::Sample.Vm.Person, int>", code); // the sibling still compiles
        Assert.DoesNotContain(".CompileBindings", code);
    }

    [Fact]
    public void ADataTemplate_ScopesItsChildrenToItsDataType_AndATemplateWithoutOneEndsTheScope()
    {
        const string xaml = """
            <Window x:Class="Sample.MainWindow"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:vm="clr-namespace:Sample.Vm"
                    x:DataType="vm:MainVm">
              <Window.Resources>
                <DataTemplate x:Key="typed" DataType="{x:Type vm:Person}"><TextBlock Text="{Binding Name}" /></DataTemplate>
                <DataTemplate x:Key="untyped"><TextBlock Text="{Binding Whatever}" /></DataTemplate>
              </Window.Resources>
            </Window>
            """;
        var (code, diagnostics) = Generate(xaml);
        Assert.Empty(diagnostics);
        Assert.Contains("__XsgCompiledProperty<global::Sample.Vm.Person, string>(\"Name\"", code);
        Assert.Contains("Binding(\"Whatever\")", code);
    }

    [Fact]
    public void AnElementThatSetsItsOwnDataContext_EndsTheScope_ButItsDataContextBindingIsCompiled()
    {
        const string xaml = """
            <Window x:Class="Sample.MainWindow"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:vm="clr-namespace:Sample.Vm"
                    x:DataType="vm:MainVm">
              <Grid>
                <TextBlock DataContext="{Binding Person}" Text="{Binding Name}" />
                <TextBlock DataContext="{Binding Person}" x:DataType="vm:Person" Text="{Binding Name}" />
              </Grid>
            </Window>
            """;
        var (code, diagnostics) = Generate(xaml);
        Assert.Empty(diagnostics);
        Assert.Contains("Binding(\"Name\")", code);                                               // first TextBlock: scope ended
        Assert.Contains("__XsgCompiledProperty<global::Sample.Vm.Person, string>(\"Name\"", code);  // second: own x:DataType
        Assert.Contains("__XsgCompiledProperty<global::Sample.Vm.MainVm, global::Sample.Vm.Person>(\"Person\"", code);
    }

    // ---- run time -------------------------------------------------------------------------------------------------------------

    private const string WpfStandIns = """
        namespace System.Windows
        {
            public class DependencyProperty { public static readonly object UnsetValue = new object(); }
            public class DependencyObject { }
            public class FrameworkElement : DependencyObject
            {
                public static System.Collections.Generic.List<System.Windows.Data.BindingBase> Bindings = new();
                public void SetBinding(DependencyProperty dp, System.Windows.Data.BindingBase binding) => Bindings.Add(binding);
                public object FindResource(object key) => null;
            }
            public class Window : FrameworkElement { public object Content { get; set; } }
            public sealed class PropertyPath
            {
                public PropertyPath(string path, params object[] parameters) { Path = path; PathParameters = parameters; }
                public string Path { get; }
                public object[] PathParameters { get; }
            }
        }
        namespace System.Windows.Data
        {
            public enum BindingMode { TwoWay, OneWay, OneTime, OneWayToSource, Default }
            public abstract class BindingBase { }
            public class Binding : BindingBase
            {
                public Binding() { }
                public Binding(string path) { Path = new System.Windows.PropertyPath(path); }
                public System.Windows.PropertyPath Path { get; set; }
                public BindingMode Mode { get; set; }
            }
        }
        namespace System.Windows.Controls
        {
            public class Panel : FrameworkElement { public System.Collections.Generic.List<object> Children { get; } = new(); }
            public class Grid : Panel { }
            public class TextBlock : FrameworkElement { public static readonly DependencyProperty TextProperty = new(); }
            public class TextBox : FrameworkElement { public static readonly DependencyProperty TextProperty = new(); }
        }
        namespace Sample
        {
            public partial class MainWindow : System.Windows.Window { public MainWindow() => InitializeComponent(); }
        }
        """;

    [Fact]
    public void TheDescriptors_ReadWriteAndNotifyWithoutReflection()
    {
        var context = new AssemblyLoadContext("xsg-compiled-bindings", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(Compile(Window)));
            Activator.CreateInstance(assembly.GetType("Sample.MainWindow", throwOnError: true)!);
            var bindings = (IList)assembly.GetType("System.Windows.FrameworkElement")!.GetField("Bindings")!.GetValue(null)!;
            Assert.Equal(2, bindings.Count);

            PropertyDescriptor[] StepsOf(object binding)
            {
                var path = binding.GetType().GetProperty("Path")!.GetValue(binding)!;
                return ((object[])path.GetType().GetProperty("PathParameters")!.GetValue(path)!).Cast<PropertyDescriptor>().ToArray();
            }

            var nameSteps = StepsOf(bindings[0]!);
            var ageSteps = StepsOf(bindings[1]!);
            var vmType = assembly.GetType("Sample.Vm.MainVm", throwOnError: true)!;
            var vm = Activator.CreateInstance(vmType)!;

            // What PropertyPathWorker does per level: GetValue on the item, listen by the descriptor's Name.
            Assert.Equal(["Person", "Name"], nameSteps.Select(s => s.Name));
            Assert.Equal(vmType, nameSteps[0].ComponentType);
            Assert.Equal(typeof(string), nameSteps[1].PropertyType);
            var person = nameSteps[0].GetValue(vm)!;
            Assert.Equal("Ada", nameSteps[1].GetValue(person));

            // TwoWay write-back: the setter runs, the source raises its own PropertyChanged, ValueChanged subscribers hear it.
            var heard = new List<string>();
            ((INotifyPropertyChanged)person).PropertyChanged += (_, e) => heard.Add("inpc:" + e.PropertyName);
            nameSteps[1].AddValueChanged(person, (_, _) => heard.Add("descriptor"));
            Assert.False(nameSteps[1].IsReadOnly);
            nameSteps[1].SetValue(person, "Grace");
            Assert.Equal("Grace", nameSteps[1].GetValue(person));
            Assert.Equal(["inpc:Name", "descriptor"], heard);

            // A get-only property is read-only, so a TwoWay binding fails the way the reflective path does.
            Assert.True(ageSteps[1].IsReadOnly);
            Assert.Equal(36, ageSteps[1].GetValue(person));
            Assert.Throws<InvalidOperationException>(() => ageSteps[1].SetValue(person, 1));

            // An item of the wrong type is not an exception: it is "no value", as a missing property is for the engine.
            Assert.Same(assembly.GetType("System.Windows.DependencyProperty")!.GetField("UnsetValue")!.GetValue(null), nameSteps[1].GetValue(vm));
        }
        finally
        {
            context.Unload();
        }
    }

    // ---- loose ResourceDictionary (themes) ------------------------------------------------------------------------------------

    private const string Generic = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="Accent" Color="Red" />
        </ResourceDictionary>
        """;

    private const string DictionaryStandIns = """
        namespace System.Windows
        {
            public class ResourceDictionary : System.Collections.Generic.Dictionary<object, object>
            {
                public System.Collections.Generic.List<ResourceDictionary> MergedDictionaries { get; } = new();
                public bool Contains(object key) => ContainsKey(key);
            }
            public class Application { public static Application Current => null; public object TryFindResource(object key) => null; }
            public class ResourceReferenceKeyNotFoundException : System.Exception { public ResourceReferenceKeyNotFoundException(string m, object k) : base(m) { } }
        }
        namespace System.Windows.Media
        {
            public struct Color { }
            public static class Colors { public static Color Red => default; }
            public class SolidColorBrush { public Color Color { get; set; } }
        }
        namespace System.Windows.Markup
        {
            public static class XamlSourceGeneratedResources
            {
                public static readonly System.Collections.Generic.Dictionary<string, System.Func<System.Windows.ResourceDictionary>> Registered = new();
                public static void RegisterDictionary(System.Reflection.Assembly assembly, string path, System.Func<System.Windows.ResourceDictionary> factory) => Registered[path] = factory;
            }
        }
        """;

    [Fact]
    public void ALooseResourceDictionary_IsGeneratedAndRegisteredUnderItsThemePath()
    {
        var (code, diagnostics) = Generate(Generic, path: "/src/Lib/Themes/Generic.xaml", properties: [("MSBuildProjectDirectory", "/src/Lib")]);
        Assert.Empty(diagnostics);
        Assert.Contains("class __XsgResourceDictionary_themes_generic_xaml : global::System.Windows.ResourceDictionary", code);
        Assert.Contains("ResourcePath = \"themes/generic.xaml\"", code);
        Assert.Contains("this[\"Accent\"] = ", code);

        var context = new AssemblyLoadContext("xsg-themes", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(Compile(Generic, DictionaryStandIns, "/src/Lib/Themes/Generic.xaml", ("MSBuildProjectDirectory", "/src/Lib"))));
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
            var registered = (IDictionary)assembly.GetType("System.Windows.Markup.XamlSourceGeneratedResources")!.GetField("Registered")!.GetValue(null)!;
            var factory = (Delegate)registered["themes/generic.xaml"]!;
            var dictionary = (IDictionary)factory.DynamicInvoke()!;
            Assert.Equal("System.Windows.Media.SolidColorBrush", dictionary["Accent"]!.GetType().FullName);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void AResourceDictionaryPath_ComesFromTheLinkWhenThereIsOne()
    {
        var (code, _) = Generate(Generic, path: "/elsewhere/Generic.xaml", fileProperties: [("build_metadata.AdditionalFiles.Link", "Themes\\Generic.xaml")]);
        Assert.Contains("ResourcePath = \"themes/generic.xaml\"", code);
    }

    // ---- helpers --------------------------------------------------------------------------------------------------------------

    // ---- binding properties: generated, or reported, never dropped quietly ------------------------------------------------------

    private static string WindowWith(string binding) => $$"""
        <Window x:Class="Sample.MainWindow"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vm="clr-namespace:Sample.Vm"
                x:DataType="vm:MainVm">
          <Grid><TextBlock Text="{{binding}}" /></Grid>
        </Window>
        """;

    [Fact]
    public void ARelativeSourceAncestorTypeWithAPrefix_ResolvesThroughTheElementsNamespaces()
    {
        // Resolved against the fixed presentation table only, local:Foo became typeof(global::local:Foo): the file did not compile.
        var (code, diagnostics) = Generate(WindowWith("{Binding Name, RelativeSource={RelativeSource FindAncestor, AncestorType={x:Type vm:Person}}}"));

        Assert.Contains("typeof(global::Sample.Vm.Person)", code);
        Assert.DoesNotContain("global::vm:", code);
        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
    }

    [Fact]
    public void ARelativeSourceAncestorTypeOfThePresentationNamespace_StillResolves()
    {
        var (code, _) = Generate(WindowWith("{Binding Width, RelativeSource={RelativeSource FindAncestor, AncestorType={x:Type Grid}}}"));

        Assert.Contains("typeof(global::System.Windows.Controls.Grid)", code);
    }

    [Fact]
    public void ASourceOfAStaticResource_IsGenerated()
    {
        var (code, diagnostics) = Generate(WindowWith("{Binding Name, Source={StaticResource person}}"));

        Assert.Contains("Source = this.FindResource(\"person\")", code);
        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
    }

    [Fact]
    public void AConverterCulture_IsGenerated()
    {
        var (code, diagnostics) = Generate(WindowWith("{Binding Person.Age, ConverterCulture=en-US}"));

        Assert.Contains("ConverterCulture = global::System.Globalization.CultureInfo.GetCultureInfo(\"en-US\")", code);
        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
    }

    [Fact]
    public void XStaticBindingProperties_AreGenerated_AsTypedStaticMembers()
    {
        var (code, diagnostics) = Generate(WindowWith("{Binding Person.Name, Converter={x:Static vm:Conv.Instance}, ConverterParameter={x:Static vm:Conv.Parameter}}"));

        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
        Assert.Contains("Converter = (global::System.Windows.Data.IValueConverter)global::Sample.Vm.Conv.Instance", code);
        Assert.Contains("ConverterParameter = global::Sample.Vm.Conv.Parameter", code);
    }

    [Fact]
    public void ARelativeSourceWithAKnownAncestorType_IsCompiledAgainstThatType()
    {
        var (code, diagnostics) = Generate(WindowWith("{Binding Name, RelativeSource={RelativeSource AncestorType={x:Type vm:Person}}}"));

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("new __XsgCompiledProperty<global::Sample.Vm.Person, string>(\"Name\"", code);
        Assert.Contains("RelativeSource = new global::System.Windows.Data.RelativeSource(global::System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(global::Sample.Vm.Person), 1)", code);
    }

    [Theory]
    [InlineData("{Binding Person.Name, Source={x:Reference other}}", "Source")]
    [InlineData("{Binding Person.Name, Delay=soon}", "Delay")]
    [InlineData("{Binding Person.Name, NoSuchProperty=1}", "NoSuchProperty")]
    public void ABindingPropertyTheGeneratorCannotWrite_IsAWarning_NotQuietlyDropped(string binding, string property)
    {
        var (_, diagnostics) = Generate(WindowWith(binding));

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "LWXSG0008");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(property, diagnostic.GetMessage());
    }

    private static (string Code, ImmutableArray<Diagnostic> Diagnostics) Generate(string xaml, params (string Name, string Value)[] properties) =>
        Generate(xaml, "/src/MainWindow.xaml", properties, []);

    private static (string Code, ImmutableArray<Diagnostic> Diagnostics) Generate(string xaml, string path,
        (string Name, string Value)[]? properties = null, (string Name, string Value)[]? fileProperties = null)
    {
        var compilation = CSharpCompilation.Create("Sample",
            [CSharpSyntaxTree.ParseText(ViewModels), CSharpSyntaxTree.ParseText("namespace Sample { public partial class MainWindow { } }")],
            References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = CreateDriver(xaml, path, properties ?? [], fileProperties ?? []).RunGenerators(compilation).GetRunResult();
        return (string.Join("\n", result.GeneratedTrees.Select(t => t.ToString())), result.Diagnostics);
    }

    private static byte[] Compile(string xaml, string standIns = WpfStandIns, string path = "/src/MainWindow.xaml", params (string Name, string Value)[] properties)
    {
        var compilation = CSharpCompilation.Create("Sample",
            [CSharpSyntaxTree.ParseText(standIns), CSharpSyntaxTree.ParseText(ViewModels)], References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Disable));
        CreateDriver(xaml, path, properties, []).RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        using var image = new MemoryStream();
        var emit = generated.Emit(image);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return image.ToArray();
    }

    private static CSharpGeneratorDriver CreateDriver(string xaml, string path, (string Name, string Value)[] properties, (string Name, string Value)[] fileProperties) =>
        CSharpGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new InMemoryText(path, xaml)],
            optionsProvider: new Options(
                properties.ToDictionary(p => p.Name.StartsWith("build_", StringComparison.Ordinal) ? p.Name : "build_property." + p.Name, p => p.Value),
                fileProperties.ToDictionary(p => p.Name, p => p.Value)));

    private static IEnumerable<MetadataReference> References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));

    private sealed class Options(Dictionary<string, string> global, Dictionary<string, string> file) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(global);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Values([]);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Values(file);
    }

    private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }

    private sealed class InMemoryText(string path, string text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
