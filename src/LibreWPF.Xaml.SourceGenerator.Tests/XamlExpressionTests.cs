using System.Collections.Immutable;
using LibreWPF.Xaml.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>
/// C# expressions in the syntax of .NET MAUI (dotnet/maui docs/specs/XamlCSharpExpressions.md): bare <c>{Foo}</c>, explicit
/// <c>{= Foo}</c>, single-quoted strings, operator words, <c>$'...'</c> interpolation and event lambdas. Stage 1 evaluates an
/// expression as plain C# in the generated class; the checks read the generated text because WPF is not referenced here.
/// </summary>
public class XamlExpressionTests
{
    private static (string Code, string[] Errors) Run(string attributes, string element = "TextBlock")
    {
        var xaml = $$"""
            <Window x:Class="Sample.MainWindow">
              <Grid>
                <{{element}} x:Name="Target" {{attributes}} />
              </Grid>
            </Window>
            """;
        var compilation = CSharpCompilation.Create("Sample",
            [CSharpSyntaxTree.ParseText("namespace Sample { public partial class MainWindow { } }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new InMemoryText("/src/MainWindow.xaml", xaml)]);
        var result = driver.RunGenerators(compilation).GetRunResult();
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToArray();
        return (result.GeneratedTrees.Length == 0 ? "" : result.GeneratedTrees[0].ToString(), errors);
    }

    private static string Text(string value) => Run($"Text=\"{value}\"").Code;

    [Theory]
    [InlineData("{Title}", "Text = Title;")]
    [InlineData("{= Title}", "Text = Title;")]
    [InlineData("{this.Title}", "Text = this.Title;")]
    [InlineData("{Items.Count}", "Text = Items.Count;")]
    [InlineData("{Math.Max(A, B)}", "Text = Math.Max(A, B);")]
    [InlineData("{!IsActive}", "Text = !IsActive;")]
    public void ABracedValueThatIsNotAMarkupExtension_IsEvaluatedAsCSharp(string value, string expected)
    {
        Assert.Contains(expected, Text(value));
    }

    [Theory]
    [InlineData("{Binding Name}")]
    [InlineData("{StaticResource Key}")]
    [InlineData("{DynamicResource Key}")]
    [InlineData("{x:Static Foo.Bar}")]
    [InlineData("{local:Anything}")]
    public void AMarkupExtension_IsNeverAnExpression(string value)
    {
        Assert.DoesNotContain("Text = Binding;", Text(value));
        Assert.DoesNotContain("Text = StaticResource;", Text(value));
        Assert.DoesNotContain("Text = DynamicResource;", Text(value));
        Assert.DoesNotContain("Text = Anything;", Text(value));
    }

    [Fact]
    public void AnExplicitEquals_ForcesAnExpressionEvenWhenTheNameIsAMarkupExtension()
    {
        Assert.Contains("Text = Binding;", Text("{= Binding}"));
    }

    [Fact]
    public void TheEscape_IsNotAnExpression()
    {
        // The value stays a string literal; it is not turned into C#. (Dropping the "{}" prefix is a separate, older gap.)
        Assert.DoesNotContain("Text = Braces", Text("{}{Braces}"));
        Assert.DoesNotContain("Text = {Braces}", Text("{}{Braces}"));
    }

    [Theory]
    [InlineData("{= Name ?? 'Unknown'}", "Name ?? \"Unknown\"")]
    [InlineData("{= IsVip ? 'Gold' : 'Standard'}", "IsVip ? \"Gold\" : \"Standard\"")]
    [InlineData("{= 'it\\'s'}", "\"it's\"")]
    public void SingleQuotedStrings_BecomeDoubleQuoted(string value, string expected)
    {
        Assert.Contains(expected, Text(value));
    }

    [Theory]
    [InlineData("{= IsLoaded AND HasData}", "IsLoaded && HasData")]
    [InlineData("{= Count LT 100}", "Count < 100")]
    [InlineData("{= Count gt 0}", "Count > 0")]
    [InlineData("{= A LTE B OR C GTE D}", "A <= B || C >= D")]
    public void OperatorWords_AreAliases_InAnyCase(string value, string expected)
    {
        Assert.Contains(expected, Text(value));
    }

    [Fact]
    public void AnOperatorWord_InsideAString_OrAnIdentifier_IsLeftAlone()
    {
        var code = Text("{= 'AND' + ANDROID}");
        Assert.Contains("\"AND\" + ANDROID", code);
    }

    [Theory]
    [InlineData("{$'{First} {Last}'}", "$\"{First} {Last}\"")]
    [InlineData("{$'Total: {Price:C2}'}", "$\"Total: {Price:C2}\"")]
    [InlineData("{$'{Qty}x {Name ?? 'none'}'}", "$\"{Qty}x {Name ?? \"none\"}\"")]
    public void Interpolation_IsConvertedHoleByHole(string value, string expected)
    {
        Assert.Contains(expected, Text(value));
    }

    [Fact]
    public void ATernaryThatIsTheWholeHole_IsParenthesised()
    {
        Assert.Contains("$\"You clicked {Count} {(Count == 1 ? \"time\" : \"times\")}.\"",
            Text("{$'You clicked {Count} {Count == 1 ? 'time' : 'times'}.'}"));
    }

    [Fact]
    public void AFormatSpecifier_AfterAnExpression_IsNotMistakenForATernary()
    {
        Assert.Contains("{Price * Qty:F2}", Text("{$'{Price * Qty:F2}'}"));
    }

    [Fact]
    public void AnEventLambda_IsAttachedAsWritten_WhenTheEventIsUnknown()
    {
        var (code, errors) = Run("Click=\"{(s, e) => Count++}\"", "Button");
        Assert.Empty(errors);
        Assert.Contains(".Click += (s, e) => Count++;", code);
    }

    [Fact]
    public void AnEventLambda_OnAnyEventName_IsAnEvent()
    {
        var (code, errors) = Run("Custom=\"{sender => Save()}\"", "Button");
        Assert.Empty(errors);
        Assert.Contains(".Custom += sender => Save();", code);
    }

    [Fact]
    public void AnAsyncLambda_IsRefused()
    {
        var (code, errors) = Run("Click=\"{async (s, e) => await Save()}\"", "Button");
        Assert.Equal(["LWXSG0011"], errors);
        Assert.Equal("", code);
    }

    [Fact]
    public void TheGeneratedCode_KeepsTheSourceLine()
    {
        Assert.Contains("#line", Text("{Title}"));
    }

    private sealed class InMemoryText(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
