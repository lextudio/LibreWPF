using System.Collections.Immutable;
using LibreWPF.Xaml.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>
/// The features of the XSG path that a project can switch off: <c>LibreWpfSimplerXaml</c> and <c>LibreWpfCSharpExpressions</c>. They
/// reach the generator as <c>build_property.*</c> (the SDK's XamlSourceGeneratorFeatures.targets writes them; see
/// the MSBuild tests in XsgFeatureSwitchesMsBuildTests). The project kinds this serves, all of them XSG projects (a BAML project never runs the
/// generator):
///   2. old-style XAML (xmlns declared) in a project that enabled XSG;
///   3. new-style XAML (no xmlns) in a project that enabled XSG.
/// With a feature on, a document that needs it is generated; with it off, the document is refused with a diagnostic that says which switch
/// to set, and nothing is generated for it. A host that passes no properties keeps every feature on.
/// </summary>
public class XamlSourceGeneratorFeatureTests
{
    // Old style (every namespace declared), with and without a C# expression.
    private const string OldStyle = """
        <Window x:Class="Sample.MainWindow" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Grid>
            <TextBlock x:Name="Status" Text="plain" />
          </Grid>
        </Window>
        """;

    private const string OldStyleWithExpression = """
        <Window x:Class="Sample.MainWindow" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Grid>
            <TextBlock x:Name="Status" Text="{= 'ready'}" />
          </Grid>
        </Window>
        """;

    // New style (nothing declared), with and without a C# expression.
    private const string NewStyle = """
        <Window x:Class="Sample.MainWindow">
          <Grid>
            <TextBlock x:Name="Status" Text="plain" />
          </Grid>
        </Window>
        """;

    private const string NewStyleWithExpression = """
        <Window x:Class="Sample.MainWindow">
          <Grid>
            <TextBlock x:Name="Status" Text="{= 'ready'}" />
          </Grid>
        </Window>
        """;

    /// <summary>The generator run over one XAML file with the given <c>build_property.*</c> values.</summary>
    private static (int Generated, string[] Errors, ImmutableArray<Diagnostic> Diagnostics) Run(string xaml, params (string Name, string Value)[] properties)
    {
        var compilation = CSharpCompilation.Create("Sample",
            [CSharpSyntaxTree.ParseText("namespace Sample { public partial class MainWindow { } }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var options = new Options(properties.ToDictionary(p => "build_property." + p.Name, p => p.Value));
        var driver = CSharpGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new InMemoryText("/src/MainWindow.xaml", xaml)], optionsProvider: options);
        var result = driver.RunGenerators(compilation).GetRunResult();
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToArray();
        return (result.GeneratedTrees.Length, errors, result.Diagnostics);
    }

    // ---- both features on (the overall switch's default) -----------------------------------------------------------------------

    [Theory]
    [InlineData(OldStyle)]
    [InlineData(OldStyleWithExpression)]
    [InlineData(NewStyle)]
    [InlineData(NewStyleWithExpression)]
    public void WithEveryFeatureOn_EveryKindOfDocumentIsGenerated(string xaml)
    {
        var (generated, errors, _) = Run(xaml, ("LibreWpfSimplerXaml", "true"), ("LibreWpfCSharpExpressions", "true"));

        Assert.Equal(1, generated);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(OldStyle)]
    [InlineData(OldStyleWithExpression)]
    [InlineData(NewStyle)]
    [InlineData(NewStyleWithExpression)]
    public void AHostThatPassesNoProperties_KeepsEveryFeatureOn(string xaml)
    {
        var (generated, errors, _) = Run(xaml);

        Assert.Equal(1, generated);
        Assert.Empty(errors);
    }

    // ---- Simpler XAML off ------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(NewStyle)]
    [InlineData(NewStyleWithExpression)]
    public void WithSimplerXamlOff_ANewStyleDocumentIsRefusedAndNothingIsGenerated(string xaml)
    {
        var (generated, errors, diagnostics) = Run(xaml, ("LibreWpfSimplerXaml", "false"), ("LibreWpfCSharpExpressions", "true"));

        Assert.Equal(0, generated);
        Assert.Equal(["LWXSG0003"], errors);
        var message = diagnostics.Single(d => d.Id == "LWXSG0003").GetMessage();
        Assert.Contains("/src/MainWindow.xaml", message);
        Assert.Contains("LibreWpfSimplerXaml", message);
    }

    [Theory]
    [InlineData(OldStyle)]
    [InlineData(OldStyleWithExpression)]
    public void WithSimplerXamlOff_AnOldStyleDocumentIsStillGenerated(string xaml)
    {
        // A document that declares its own namespaces does not need the feature.
        var (generated, errors, _) = Run(xaml, ("LibreWpfSimplerXaml", "false"), ("LibreWpfCSharpExpressions", "true"));

        Assert.Equal(1, generated);
        Assert.Empty(errors);
    }

    // ---- C# expressions off ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(OldStyleWithExpression)]
    [InlineData(NewStyleWithExpression)]
    public void WithCSharpExpressionsOff_ACsValueIsRefusedAndNothingIsGenerated(string xaml)
    {
        var (generated, errors, diagnostics) = Run(xaml, ("LibreWpfSimplerXaml", "true"), ("LibreWpfCSharpExpressions", "false"));

        Assert.Equal(0, generated);
        Assert.Equal(["LWXSG0004"], errors);
        var message = diagnostics.Single(d => d.Id == "LWXSG0004").GetMessage();
        Assert.Contains("/src/MainWindow.xaml(3,", message);
        Assert.Contains("{= 'ready'}".Replace("&quot;", "\""), message);
        Assert.Contains("LibreWpfCSharpExpressions", message);
    }

    [Theory]
    [InlineData(OldStyle)]
    [InlineData(NewStyle)]
    public void WithCSharpExpressionsOff_ADocumentWithoutOneIsStillGenerated(string xaml)
    {
        var (generated, errors, _) = Run(xaml, ("LibreWpfSimplerXaml", "true"), ("LibreWpfCSharpExpressions", "false"));

        Assert.Equal(1, generated);
        Assert.Empty(errors);
    }

    [Fact]
    public void EveryCsValueIsReported_NotOnlyTheFirst()
    {
        var xaml = """
            <Window x:Class="Sample.MainWindow">
              <Grid>
                <TextBlock Text="{1 + 1}" />
                <TextBlock Text="{2 + 2}" />
              </Grid>
            </Window>
            """;

        var (_, errors, _) = Run(xaml, ("LibreWpfCSharpExpressions", "false"));

        Assert.Equal(["LWXSG0004", "LWXSG0004"], errors);
    }

    // ---- both off --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void WithBothOff_AnOldStyleDocumentWithoutExpressionsIsGenerated_AndOtherwiseTheFirstMissingFeatureIsReported()
    {
        var both = new[] { ("LibreWpfSimplerXaml", "false"), ("LibreWpfCSharpExpressions", "false") };

        Assert.Equal(1, Run(OldStyle, both).Generated);
        Assert.Equal(["LWXSG0004"], Run(OldStyleWithExpression, both).Errors);
        Assert.Equal(["LWXSG0003"], Run(NewStyle, both).Errors);
        // A new-style document with an expression cannot be read at all without Simpler XAML, so that is what is reported.
        Assert.Equal(["LWXSG0003"], Run(NewStyleWithExpression, both).Errors);
    }

    // ---- the value of a switch -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    [InlineData("  false  ", false)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("", true)]
    [InlineData("anything else", true)]
    public void OnlyAnExplicitFalseSwitchesAFeatureOff(string value, bool expectedOn)
    {
        var (generated, _, _) = Run(NewStyle, ("LibreWpfSimplerXaml", value));

        Assert.Equal(expectedOn ? 1 : 0, generated);
    }

    [Fact]
    public void TheTwoFeaturesAreIndependent()
    {
        Assert.Equal(1, Run(NewStyleWithExpression, ("LibreWpfSimplerXaml", "true"), ("LibreWpfCSharpExpressions", "true")).Generated);
        Assert.Equal(0, Run(NewStyleWithExpression, ("LibreWpfSimplerXaml", "false"), ("LibreWpfCSharpExpressions", "true")).Generated);
        Assert.Equal(0, Run(NewStyleWithExpression, ("LibreWpfSimplerXaml", "true"), ("LibreWpfCSharpExpressions", "false")).Generated);
    }

    [Fact]
    public void TheOverallSwitchPropertyIsNotWhatTheGeneratorReadsItsFeaturesFrom()
    {
        // UseLibreWpfXamlSourceGenerator decides whether XAML reaches the generator at all (and the SDK targets derive the feature
        // switches from it); the generator itself reads only the feature switches.
        var (generated, _, _) = Run(NewStyle, ("UseLibreWpfXamlSourceGenerator", "false"));

        Assert.Equal(1, generated);
    }

    private sealed class Options(Dictionary<string, string> global) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(global);

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
