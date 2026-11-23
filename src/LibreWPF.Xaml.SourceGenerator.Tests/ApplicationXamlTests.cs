using System.Collections.Immutable;
using LibreWPF.Xaml.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>
/// What a real application's XAML needs beyond a window and controls, with no BAML to load anything from at run time: App.xaml with a
/// StartupUri, a merged dictionary written as Source="...", and the root object of each XAML file being creatable by its resource path.
/// </summary>
public class ApplicationXamlTests
{
    private const string Header = """
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        """;

    // Just enough of WPF for the generator to see the types it asks about, and the code-behind classes of the XAML files.
    private const string Source = """
        namespace System.Windows
        {
            public class Application { public System.Uri StartupUri { get; set; } public ResourceDictionary Resources { get; set; } }
            public class ResourceDictionary { }
            public class Window { }
        }
        namespace Sample
        {
            public partial class MainWindow : System.Windows.Window { public MainWindow() { } }
            public partial class App : System.Windows.Application { public App() { } }
            public partial class NeedsArguments : System.Windows.Window { public NeedsArguments(int unused) { } }
            public abstract partial class Abstract : System.Windows.Window { }
        }
        """;

    private static (string Code, ImmutableArray<Diagnostic> Diagnostics) Generate(string xaml, string path = "/src/MainWindow.xaml")
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("Sample", [CSharpSyntaxTree.ParseText(Source)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new InMemoryText(path, xaml)], optionsProvider: new Options());
        var result = driver.RunGenerators(compilation).GetRunResult();
        return (string.Join("\n", result.GeneratedTrees.Select(t => t.ToString())), result.Diagnostics);
    }

    [Fact]
    public void TheRootObjectOfAXamlFile_IsRegisteredUnderItsResourcePath()
    {
        var (code, _) = Generate($"<Window x:Class=\"Sample.MainWindow\" {Header} />");

        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", code);
        Assert.Contains("RegisterComponent(\"Sample\", \"mainwindow.xaml\", static () => new global::Sample.MainWindow());", code);
    }

    [Fact]
    public void TheResourcePath_IsRelativeToTheProject_LowerCased_WithSlashes()
    {
        var (code, _) = Generate($"<Window x:Class=\"Sample.MainWindow\" {Header} />", "/src/Views/Main.xaml");

        Assert.Contains("RegisterComponent(\"Sample\", \"views/main.xaml\", ", code);
    }

    [Theory]
    [InlineData("Sample.NeedsArguments")]   // no parameterless constructor: nothing can create it
    [InlineData("Sample.Abstract")]         // cannot be instantiated
    [InlineData("Sample.DoesNotExist")]     // no code-behind class in the compilation
    public void ARootObjectThatCannotBeCreated_IsNotRegistered(string className)
    {
        var (code, _) = Generate($"<Window x:Class=\"{className}\" {Header} />");

        Assert.DoesNotContain("RegisterComponent", code);
    }

    [Fact]
    public void TheApplication_IsNotAComponent()
    {
        var (code, _) = Generate($"<Application x:Class=\"Sample.App\" {Header} />", "/src/App.xaml");

        Assert.DoesNotContain("RegisterComponent", code);
    }

    [Fact]
    public void AUriValue_IsATypedUri_ForStartupUri()
    {
        var (code, diagnostics) = Generate($"<Application x:Class=\"Sample.App\" {Header} StartupUri=\"MainWindow.xaml\" />", "/src/App.xaml");

        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
        Assert.Contains("this.StartupUri = new global::System.Uri(\"MainWindow.xaml\", global::System.UriKind.RelativeOrAbsolute);", code);
    }

    private static string MergedFrom(string source) => $$"""
        <Application x:Class="Sample.App" {{Header}}>
          <Application.Resources>
            <ResourceDictionary>
              <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="{{source}}" />
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
          </Application.Resources>
        </Application>
        """;

    [Fact]
    public void ASourceDictionary_GetsALoaderThatFailsClearlyWhenTheDictionaryIsNotCompiledIn()
    {
        var (code, diagnostics) = Generate(MergedFrom("Styles/Brushes.xaml"), "/src/App.xaml");

        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
        Assert.Contains("private static global::System.Windows.ResourceDictionary __XsgLoadDictionary(string assembly, string path)", code);
        Assert.Contains("XamlSourceGeneratedResources.TryCreateDictionary(assembly, path, out var dictionary)", code);
        Assert.Contains("throw new global::System.IO.FileNotFoundException(", code);
    }

    [Theory]
    [InlineData("Styles/Brushes.xaml", "/src/App.xaml", "Sample", "styles/brushes.xaml")]                          // relative to the file's folder (the project root here)
    [InlineData("/Styles/Brushes.xaml", "/src/Views/Page.xaml", "Sample", "styles/brushes.xaml")]                  // rooted
    [InlineData("pack://application:,,,/Styles/Brushes.xaml", "/src/Views/Page.xaml", "Sample", "styles/brushes.xaml")]
    [InlineData("pack://application:,,,/Other;component/Dictionaries/A.xaml", "/src/App.xaml", "Other", "dictionaries/a.xaml")]
    [InlineData("pack://application:,,,/Other;v1.2.3.4;component/A.xaml", "/src/App.xaml", "Other", "a.xaml")]
    public void ASourceUri_IsResolvedAtBuildTime(string source, string file, string assembly, string path)
    {
        var (code, _) = Generate(MergedFrom(source), file);

        Assert.Contains($"__XsgLoadDictionary(\"{assembly}\", \"{path}\")", code);
    }

    private sealed class Options : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(new() { ["build_property.MSBuildProjectDirectory"] = "/src" });
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Values([]);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Values([]);
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
