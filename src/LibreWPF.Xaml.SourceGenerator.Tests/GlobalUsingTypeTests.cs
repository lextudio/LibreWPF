using LibreWPF.Xaml.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>
/// Simpler XAML as in .NET MAUI: a type that lives in a namespace of the project's own <c>global using</c> directives needs no
/// <c>xmlns</c> declaration. Aliases and <c>global using static</c> are not namespaces and expose nothing.
/// </summary>
public class GlobalUsingTypeTests
{
    private const string Types = """
        namespace System.Windows.Markup
        {
            public static class XamlSourceGeneratedResources { public static void RegisterComponent(string a, string b, System.Func<object> f) { } }
        }
        namespace System.Windows
        {
            public class DependencyObject { }
            public class FrameworkElement : DependencyObject { public object FindResource(object key) => null; }
            public class Window : FrameworkElement { }
        }
        namespace System.Windows.Controls
        {
            public class Panel : System.Windows.FrameworkElement { public System.Collections.Generic.List<object> Children { get; } = new(); }
            public class Grid : Panel { }
        }
        namespace Sample.Controls { public class Fancy : System.Windows.FrameworkElement { public string Caption { get; set; } } }
        namespace Sample.Other { public class Hidden : System.Windows.FrameworkElement { } }
        namespace Sample { public partial class MainWindow : System.Windows.Window { } }
        """;

    private static (string Code, string[] Errors) Run(string usings, string body)
    {
        var xaml = $"<Window x:Class=\"Sample.MainWindow\"><Grid>{body}</Grid></Window>";
        var compilation = CSharpCompilation.Create("Sample", [CSharpSyntaxTree.ParseText(usings + Types)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new InMemoryText("/src/MainWindow.xaml", xaml)], optionsProvider: new Options());
        var result = driver.RunGenerators(compilation).GetRunResult();
        return (string.Join("\n", result.GeneratedTrees.Select(t => t.ToString())),
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToArray());
    }

    [Fact]
    public void ATypeOfAGlobalUsingNamespace_NeedsNoXmlns()
    {
        var (code, errors) = Run("global using Sample.Controls;\n", "<Fancy Caption=\"hi\" />");
        Assert.Empty(errors);
        Assert.Contains("new global::Sample.Controls.Fancy()", code);
    }

    [Fact]
    public void WithoutTheGlobalUsing_TheTypeIsNotFound()
    {
        var (code, _) = Run("", "<Fancy />");
        Assert.DoesNotContain("global::Sample.Controls.Fancy", code);
    }

    [Fact]
    public void AnOrdinaryUsing_AnAlias_AndAStaticUsing_ExposeNothing()
    {
        var (code, _) = Run("using Sample.Controls;\nglobal using Alias = Sample.Controls;\nglobal using static Sample.Other.Hidden;\n", "<Fancy /><Hidden />");
        Assert.DoesNotContain("global::Sample.Controls.Fancy", code);
        Assert.DoesNotContain("global::Sample.Other.Hidden", code);
    }

    [Fact]
    public void ThePresentationNamespaceWins_OverAGlobalUsing()
    {
        var (code, errors) = Run("global using Sample.Controls;\n", "<Grid />");
        Assert.Empty(errors);
        Assert.Contains("new global::System.Windows.Controls.Grid()", code);
    }

    private sealed class Options : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values();
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Values();
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Values();
    }

    private sealed class Values : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) { value = null!; return false; }
    }

    private sealed class InMemoryText(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
