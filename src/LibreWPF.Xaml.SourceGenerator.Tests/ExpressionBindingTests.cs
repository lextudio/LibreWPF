using System.Collections.Immutable;
using LibreWPF.Xaml.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>
/// The order in which a simple path <c>{Name}</c> is resolved (the rules of .NET MAUI's C# expressions): a Binding against
/// <c>x:DataType</c> when the target is a dependency property and the DataContext type has the name, otherwise a member of the page,
/// otherwise a static; <c>{.Name}</c> is the DataContext explicitly and <c>{this.Name}</c> the page. A name on both is an error.
/// </summary>
public class ExpressionBindingTests
{
    private const string Types = """
        namespace System.Windows.Markup
        {
            public static class XamlSourceGeneratedResources { public static void RegisterComponent(string a, string b, System.Func<object> f) { } }
        }
        namespace System.Windows
        {
            public class DependencyProperty { }
            public class DependencyObject { }
            public class FrameworkElement : DependencyObject
            {
                public void SetBinding(DependencyProperty dp, System.Windows.Data.BindingBase binding) { }
                public object FindResource(object key) => null;
            }
            public class Window : FrameworkElement { public string Title { get; set; } }
            public sealed class PropertyPath { public PropertyPath(string path, params object[] parameters) { } }
        }
        namespace System.Windows.Data
        {
            public abstract class BindingBase { }
            public enum BindingMode { TwoWay, OneWay, OneTime, OneWayToSource, Default }
            public class MultiBinding : BindingBase { }
            public interface IMultiValueConverter { }
            public class Binding : BindingBase
            {
                public Binding() { }
                public Binding(string path) { }
                public System.Windows.PropertyPath Path { get; set; }
            }
        }
        namespace System.Windows.Controls
        {
            public class Panel : FrameworkElement { public System.Collections.Generic.List<object> Children { get; } = new(); }
            public class Grid : Panel { }
            public class TextBlock : FrameworkElement
            {
                public static readonly DependencyProperty TextProperty = new();
                public string Text { get; set; }
                public string Tag2 { get; set; }
            }
        }
        namespace Sample
        {
            public class User { public string Name { get; set; } public string Email { get; set; } public int Age { get; set; } public int? Score { get; set; } public Address Home { get; set; } }
            public class Address { public string City { get; set; } }
            public partial class MainWindow : System.Windows.Window
            {
                public string Greeting => "hi";
                public string Email => "page@example.com";
            }
        }
        """;

    private static (string Code, string[] Errors) Run(string attributes, bool dataType = true)
    {
        var xaml = $$"""
            <Window x:Class="Sample.MainWindow" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:Sample"{{(dataType ? " x:DataType=\"local:User\"" : "")}}>
              <Grid>
                <TextBlock {{attributes}} />
              </Grid>
            </Window>
            """;
        var compilation = CSharpCompilation.Create("Sample", [CSharpSyntaxTree.ParseText(Types)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new InMemoryText("/src/MainWindow.xaml", xaml)], optionsProvider: new Options());
        var result = driver.RunGenerators(compilation).GetRunResult();
        return (string.Join("\n", result.GeneratedTrees.Select(t => t.ToString())),
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToArray());
    }

    [Fact]
    public void ANameTheDataTypeHas_IsABinding()
    {
        var (code, errors) = Run("Text=\"{Name}\"");
        Assert.Empty(errors);
        Assert.Contains("new global::System.Windows.Data.Binding()", code);
        Assert.Contains("Path = new global::System.Windows.PropertyPath(\"(0)\"", code);
        Assert.DoesNotContain("Text = Name;", code);
    }

    [Fact]
    public void ADottedPath_IsACompiledBinding_LikeBindingMarkup()
    {
        var (code, errors) = Run("Text=\"{Home.City}\"");
        Assert.Empty(errors);
        Assert.Contains("PropertyPath(\"(0).(1)\"", code);
    }

    [Fact]
    public void ANullConditionalAccess_IsNotADataContextChain()
    {
        // `?.Name` continues the expression before it; `Name` here is not the DataContext's.
        var (code, errors) = Run("Text=\"{= Greeting?.Name}\"");
        Assert.Empty(errors);
        Assert.Contains("Greeting?.Name", code);
        Assert.DoesNotContain("Greeting?__v", code);
    }

    [Fact]
    public void ACompoundExpression_OverANullableValueType_UsesACastNotAPattern()
    {
        // `values[0] is int? __t0` is illegal C# (CS8116); a cast inside the try is not.
        var (code, errors) = Run("Text=\"{Score + 1}\"");
        Assert.Empty(errors);
        Assert.Contains("(int?)values[0]!", code);
        Assert.DoesNotContain("is int?", code);
    }

    [Fact]
    public void ALeadingDotCompoundWithoutADataType_IsReportedNotEmitted()
    {
        var (code, errors) = Run("Text=\"{.Email + '!'}\"", dataType: false);
        Assert.Equal(["LWXSG0013"], errors);
        Assert.DoesNotContain(".Email + \"!\"", code);
    }

    [Fact]
    public void ANameOnlyThePageHas_IsPlainCSharp()
    {
        var (code, errors) = Run("Text=\"{Greeting}\"");
        Assert.Empty(errors);
        Assert.Contains("Text = Greeting;", code);
        Assert.DoesNotContain("Binding", code.Replace("System.Windows.Data", ""));
    }

    [Fact]
    public void ANameOnBothThePageAndTheDataType_IsAnError()
    {
        var (code, errors) = Run("Text=\"{Email}\"");
        Assert.Equal(["LWXSG0012"], errors);
        // The error stops nothing else: the value falls back to the page's member.
        Assert.Contains("Text = Email;", code);
    }

    [Fact]
    public void ThisDot_IsThePage_EvenWhenTheDataTypeHasTheName()
    {
        var (code, errors) = Run("Text=\"{this.Email}\"");
        Assert.Empty(errors);
        Assert.Contains("Text = this.Email;", code);
    }

    [Fact]
    public void ALeadingDot_IsTheDataContext_EvenWhenThePageHasTheName()
    {
        var (code, errors) = Run("Text=\"{.Email}\"");
        Assert.Empty(errors);
        Assert.Contains("PropertyPath(\"(0)\"", code);
        Assert.DoesNotContain("Text = Email;", code);
    }

    [Fact]
    public void ALeadingDot_WithoutAnXDataType_IsAReflectiveBinding()
    {
        var (code, errors) = Run("Text=\"{.Name}\"", dataType: false);
        Assert.Empty(errors);
        Assert.Contains("new global::System.Windows.Data.Binding(\"Name\")", code);
    }

    [Fact]
    public void WithoutAnXDataType_ABareNameIsPlainCSharp()
    {
        var (code, errors) = Run("Text=\"{Greeting}\"", dataType: false);
        Assert.Empty(errors);
        Assert.Contains("Text = Greeting;", code);
    }

    [Fact]
    public void AClrProperty_TakesTheValue_NotABinding()
    {
        var (code, errors) = Run("Tag2=\"{Name}\"");
        Assert.Empty(errors);
        Assert.Contains("Tag2 = Name;", code);
    }

    [Fact]
    public void ACompoundExpression_OverADataContextName_IsAMultiBindingWithAConverter()
    {
        var (code, errors) = Run("Text=\"{Age + 1}\"");
        Assert.Empty(errors);
        Assert.Contains("new global::System.Windows.Data.MultiBinding { Mode = global::System.Windows.Data.BindingMode.OneWay,", code); // TwoWay-by-default targets (TextBox.Text) would call ConvertBack
        Assert.Contains("return (object?)(__v0 + 1);", code);
        // The evaluation is a method (Hot Reload updates a method in place, but loses an unmatched lambda), named in order.
        Assert.Contains("private object? __XsgExpression_0(object?[] values)", code);
        Assert.Contains("new __XsgExpressionConverter(__XsgExpression_0)", code);
        Assert.DoesNotContain("values => {", code);
        Assert.Contains("var __v0 = (int)values[0]!;", code);
        Assert.Contains("PropertyPath(\"(0)\"", code);
        Assert.Contains("private sealed class __XsgExpressionConverter : global::System.Windows.Data.IMultiValueConverter", code);
        Assert.Contains("values[0] == global::System.Windows.DependencyProperty.UnsetValue", code);
        // A MultiBinding rejects a result that is not of the property's type (an int for Text), so the converter converts it.
        Assert.Contains("global::System.Convert.ToString(result, culture)", code);
        Assert.DoesNotContain("Text = ", code);
    }

    [Fact]
    public void AStringExpression_ReadsEveryNameThroughItsOwnBinding()
    {
        var (code, errors) = Run("Text=\"{$'{Name} is {Age:D3}'}\"");
        Assert.Empty(errors);
        Assert.Contains("return (object?)($\"{__v0} is {__v1:D3}\");", code);
        Assert.Equal(2, code.Split("new global::System.Windows.Data.Binding()").Length - 1);
    }

    [Fact]
    public void AMemberOfAName_StaysAMemberAccessOnTheValue()
    {
        var (code, errors) = Run("Text=\"{Name.ToUpper()}\"");
        Assert.Empty(errors);
        Assert.Contains("return (object?)(__v0.ToUpper());", code);
    }

    [Fact]
    public void ADottedPathIsOneBinding_AndRepeatedNamesShareIt()
    {
        var (code, errors) = Run("Text=\"{Home.City + Home.City + Age}\"");
        Assert.Empty(errors);
        Assert.Contains("return (object?)(__v0 + __v0 + __v1);", code);
        Assert.Equal(2, code.Split("new global::System.Windows.Data.Binding()").Length - 1);
    }

    [Fact]
    public void APageNameAndADataContextName_CanShareAnExpression()
    {
        var (code, errors) = Run("Text=\"{Greeting + Name}\"");
        Assert.Empty(errors);
        Assert.Contains("return (object?)(Greeting + __v0);", code);
    }

    [Fact]
    public void ALeadingDot_InACompoundExpression_NamesTheDataContext()
    {
        var (code, errors) = Run("Text=\"{.Email + '!'}\"");
        Assert.Empty(errors);
        Assert.Contains("__v0 + \"!\"", code);
    }

    [Fact]
    public void ANameOnBoth_InACompoundExpression_IsReportedAndStaysThePages()
    {
        var (code, errors) = Run("Text=\"{Email + '!'}\"");
        Assert.Equal(["LWXSG0012"], errors);
        Assert.Contains("Text = Email + \"!\";", code);
    }

    [Fact]
    public void ACompoundExpressionWithoutADataContextName_IsPlainCSharp()
    {
        var (code, errors) = Run("Text=\"{Greeting + '!'}\"");
        Assert.Empty(errors);
        Assert.Contains("Text = Greeting + \"!\";", code);
    }

    [Fact]
    public void ACompoundExpression_OnAClrProperty_IsPlainCSharp()
    {
        var (code, _) = Run("Tag2=\"{Name + '!'}\"");
        Assert.Contains("Tag2 = Name + \"!\";", code);
        Assert.DoesNotContain("MultiBinding", code);
    }

    [Fact]
    public void AStringLiteral_IsNotMistakenForNames()
    {
        var (code, errors) = Run("Text=\"{Age + 'Name Age'}\"");
        Assert.Empty(errors);
        Assert.Contains("return (object?)(__v0 + \"Name Age\");", code);
        Assert.Equal(1, code.Split("new global::System.Windows.Data.Binding()").Length - 1);
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
