using System.Collections.Immutable;
using LibreWPF.Xaml.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>
/// Runs the generator on XAML text and checks the generated C#. WPF is not referenced, so these
/// tests check generated text, not that it compiles against WPF; the OpenDevelop XSG fixture
/// (tests/fixtures/XsgLocalFeedFixture) compiles and runs it against LibreWPF.
/// </summary>
public class XamlSourceGeneratorTests
{
    private const string Window = """
        <Window x:Class="Sample.MainWindow"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Grid>
            <TextBlock x:Name="Status" Text="{cs: &quot;ready&quot;}" />
            <TextBlock x:Name="Other" Text="plain" />
          </Grid>
        </Window>
        """;

    private static string Generate(string xaml, string path = "/src/MainWindow.xaml")
    {
        var compilation = CSharpCompilation.Create("Sample",
            [CSharpSyntaxTree.ParseText("namespace Sample { public partial class MainWindow { } }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new InMemoryText(path, xaml)]);
        var result = driver.RunGenerators(compilation).GetRunResult();
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return Assert.Single(result.GeneratedTrees).ToString();
    }

    [Fact]
    public void XName_IsAFieldTheElementNameAndANameScopeEntry()
    {
        var code = Generate(Window);
        Assert.Contains("this.Status = ", code);
        Assert.Contains(".Name = \"Status\";", code);
        Assert.Contains(".Name = \"Other\";", code);
        Assert.Contains("NameScope.SetNameScope(", code);
        Assert.Contains(".RegisterName(\"Status\", ", code);
    }

    [Fact]
    public void XName_IsUnregisteredBeforeRegistering_SoInitializeComponentCanRunAgain()
    {
        var code = Generate(Window);
        var unregister = code.IndexOf(".UnregisterName(\"Status\")", StringComparison.Ordinal);
        var register = code.IndexOf(".RegisterName(\"Status\", ", StringComparison.Ordinal);
        Assert.True(unregister >= 0 && unregister < register, code);
    }

    [Fact]
    public void XName_WithAQuote_IsEscapedInTheNameLiterals()
    {
        // Not a valid WPF name, but the literal must still be well-formed C#.
        var code = Generate(Window.Replace("x:Name=\"Other\"", "x:Name=\"Oth&quot;er\""));
        Assert.Contains(".Name = \"Oth\\\"er\";", code);
    }

    [Fact]
    public void HotReloadHandler_AndLineDirectives_AreEmitted()
    {
        var code = Generate(Window);
        Assert.Contains("MetadataUpdateHandler(typeof(global::Sample.MainWindow.__XsgHotReload))", code);
        Assert.Contains("#line 5 \"/src/MainWindow.xaml\"", code);
    }

    [Fact]
    public void ElementLocals_AreStableAcrossRuns()
    {
        // EnC compares the regenerated method with the launch one; random local names would turn
        // every regeneration into a whole-method edit.
        Assert.Equal(Generate(Window), Generate(Window));
    }

    private sealed class InMemoryText(string path, string text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
