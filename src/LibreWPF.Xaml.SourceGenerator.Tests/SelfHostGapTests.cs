using LibreWPF.Xaml.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>
/// Gaps found by compiling OpenDevelop's own XAML with the generator: a keyword as x:Name, a code-behind without a base class,
/// a URN xmlns mapped by [XmlnsDefinition] in a referenced assembly, and the root element as an ElementName binding source.
/// </summary>
public class SelfHostGapTests
{
    private const string Types = """
        [assembly: System.Windows.Markup.XmlnsDefinition("urn:Sample.Styles", "Sample.Hidden.Styles")]
        namespace System.Windows.Markup
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class XmlnsDefinitionAttribute : System.Attribute { public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { } }
            public abstract class MarkupExtension { public abstract object ProvideValue(System.IServiceProvider serviceProvider); }
            public class HelloExtension : MarkupExtension { public override object ProvideValue(System.IServiceProvider serviceProvider) => null; }
            public static class XamlSourceGeneratedResources { public static void RegisterComponent(string a, string b, System.Func<object> f) { } }
        }
        namespace System.Windows
        {
            public class DependencyObject { }
            public class UIElement : DependencyObject { }
            public class ResourceDictionary : DependencyObject, System.Collections.IEnumerable { public void Add(object key, object value) { } public System.Collections.IEnumerator GetEnumerator() => null; }
            public class FrameworkElement : UIElement { public object FindResource(object key) => null; public string Name { get; set; } public object Tag { get; set; } }
        }
        namespace System.Windows.Controls
        {
            public class UserControl : System.Windows.FrameworkElement { }
            public class Column : System.Windows.DependencyObject { public object CellTemplate { get; set; } }
            public class Holder : System.Windows.FrameworkElement { public System.Collections.Generic.List<object> Items { get; } = new(); public System.Windows.ResourceDictionary Resources { get; set; } }
            public class Initializable : System.ComponentModel.ISupportInitialize, System.Windows.DependencyObject { public string Path { get; set; } public void BeginInit() { } public void EndInit() { } }
            public class Label : System.Windows.FrameworkElement { public System.Windows.FrameworkElement Target { get; set; } }
            public class Grid : System.Windows.FrameworkElement { public System.Collections.Generic.List<object> Children { get; } = new(); }
            public class Border : System.Windows.FrameworkElement { public System.Windows.ResourceDictionary Resources { get; set; } }
            public class TextBox : System.Windows.FrameworkElement { public string Text { get; set; } }
        }
        namespace Sample.Hidden.Styles { public static class ResourceKeys { public static readonly string ListBoxStyle = "x"; } }
        """;

    private static (string Code, string[] Errors) Run(string codeBehind, string xaml)
    {
        var compilation = CSharpCompilation.Create("Sample", [CSharpSyntaxTree.ParseText(Types + codeBehind)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location), MetadataReference.CreateFromFile(typeof(System.ComponentModel.ISupportInitialize).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new InMemoryText("/src/Picker.xaml", xaml)], optionsProvider: new Options());
        var result = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _).GetRunResult();
        var errors = updated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToArray();
        return (string.Join("\n", result.GeneratedTrees.Select(t => t.ToString())), errors);
    }

    private const string Root = "<UserControl x:Class=\"Sample.Picker\" x:Name=\"this\">{0}</UserControl>";

    [Fact]
    public void AKeywordAsXName_IsEscapedInTheFieldAndItsAssignment()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }", string.Format(Root, "<TextBox x:Name=\"event\" />"));
        Assert.Contains("@event;", code);
        Assert.Contains("this.@event =", code);
        Assert.Contains("@this;", code);
    }

    [Fact]
    public void ACodeBehindWithoutABase_GetsTheRootElementsTypeAsItsBase()
    {
        var (code, errors) = Run("namespace Sample { public partial class Picker { } }", string.Format(Root, ""));
        Assert.Contains("partial class Picker : global::System.Windows.Controls.UserControl", code);
        Assert.DoesNotContain("CS0263", errors);
    }

    [Fact]
    public void ACodeBehindThatNamesItsBase_IsLeftAlone()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }", string.Format(Root, ""));
        Assert.DoesNotContain("partial class Picker :", code);
    }

    [Fact]
    public void AnElementPropertyWrittenAsAName_IsAssignedAfterTheElementExists()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Grid><Label Target=\"box\" /><TextBox x:Name=\"box\" /></Grid></UserControl>");
        Assert.True(code.Contains("__xsgDeferred += () =>"), code);
        Assert.Contains(".Target = this.box", code);
    }

    [Fact]
    public void AnExplicitResourceDictionaryInAPropertyElement_IsTheValue_NotAnEntry()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Grid><Border><Border.Resources><ResourceDictionary /></Border.Resources></Border></Grid></UserControl>");
        Assert.True(code.Contains(".Resources = __ResourceDictionary") && !code.Contains(".Resources.Add("), code);
    }

    [Fact]
    public void AStaticResourceOnANonElement_IsLookedUpOnTheElementThatHoldsTheResource()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Grid><Holder><Holder.Resources><Label x:Key=\"k\" /></Holder.Resources><Column CellTemplate=\"{StaticResource k}\" /></Holder></Grid></UserControl>");
        Assert.True(code.Contains(".FindResource(\"k\")") && !code.Contains("this.FindResource(\"k\")"), code);
    }

    [Fact]
    public void TextContent_CollapsesXmlWhiteSpace_ButKeepsNoBreakSpace_AndHonoursXmlSpacePreserve()
    {
        var (collapsed, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><TextBox>  a \n   b\u00A0 c </TextBox></UserControl>");
        Assert.Contains("\"a b\u00A0 c\"", collapsed.Replace("\u00A0", "\u00A0"));
        var (preserved, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xml:space=\"preserve\"><TextBox>a  b</TextBox></UserControl>");
        Assert.Contains("\"a  b\"", preserved);
    }

    [Fact]
    public void ANameWrittenAsPlainName_IsAFieldToo_SoADeferredElementReferenceCanUseIt()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Grid><Label Target=\"box\" /><TextBox Name=\"box\" /></Grid></UserControl>");
        Assert.Contains("TextBox? box;", code);
        Assert.Contains(".Target = this.box", code);
    }

    [Fact]
    public void AConverterOfATemplateBinding_IsLookedUpOnTheTemplateThatDeclaresIt()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><UserControl.Resources><ControlTemplate x:Key=\"T\" TargetType=\"UserControl\"><ControlTemplate.Resources><Label x:Key=\"conv\" /></ControlTemplate.Resources><Holder Tag=\"{TemplateBinding Tag, Converter={StaticResource conv}}\" /></ControlTemplate></UserControl.Resources></UserControl>");
        Assert.True(code.Contains(".Resources[\"conv\"]") && !code.Contains("this.FindResource(\"conv\")"), code);
    }

    [Fact]
    public void ATemplateBinding_WithTheNamedOptionsFirst_StillBindsTheProperty()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><UserControl.Resources><Label x:Key=\"conv\" /><ControlTemplate x:Key=\"T\" TargetType=\"UserControl\"><Holder Tag=\"{TemplateBinding Converter={StaticResource conv}, Property=Tag}\" /></ControlTemplate></UserControl.Resources></UserControl>");
        Assert.DoesNotContain("FromName(\"Converter", code);
        Assert.Contains("Tag", code);
        Assert.Contains("FindResource(\"conv\")", code);
    }

    [Fact]
    public void TheContentLoadedField_IsSilencedForCs0414()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }", string.Format(Root, ""));
        var disable = code.IndexOf("#pragma warning disable CS0414", System.StringComparison.Ordinal);
        var field = code.IndexOf("private bool _contentLoaded = true;", System.StringComparison.Ordinal);
        var restore = code.IndexOf("#pragma warning restore CS0414", System.StringComparison.Ordinal);
        Assert.True(disable >= 0 && field > disable && restore > field, code);
    }

    [Fact]
    public void TheRootsExplicitResourceDictionary_IsInPlaceBeforeItsEntriesAreBuilt()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><UserControl.Resources><ResourceDictionary><Label x:Key=\"conv\" /><ControlTemplate x:Key=\"T\" TargetType=\"UserControl\"><Holder Tag=\"{TemplateBinding Tag, Converter={StaticResource conv}}\" /></ControlTemplate></ResourceDictionary></UserControl.Resources></UserControl>");
        var assigned = code.IndexOf("this.Resources = __rd_", System.StringComparison.Ordinal);
        var lookup = code.IndexOf("this.FindResource(\"conv\")", System.StringComparison.Ordinal);
        Assert.True(assigned >= 0 && lookup > assigned, code);
    }

    [Fact]
    public void TheRootsResources_AreInPlaceBeforeItsEntriesAreBuilt()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><UserControl.Resources><Label x:Key=\"conv\" /><ControlTemplate x:Key=\"T\" TargetType=\"UserControl\"><Holder Tag=\"{TemplateBinding Tag, Converter={StaticResource conv}}\" /></ControlTemplate></UserControl.Resources></UserControl>");
        var assigned = code.IndexOf("this.Resources = __rd_", System.StringComparison.Ordinal);
        var lookup = code.IndexOf("this.FindResource(\"conv\")", System.StringComparison.Ordinal);
        Assert.True(assigned >= 0 && lookup > assigned, code);
    }

    [Fact]
    public void ATemplateBindings_StaticResourceConverterParameter_IsResolved_AndUnknownOptionsAreReported()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><UserControl.Resources><Label x:Key=\"c\" /><Label x:Key=\"p\" /><ControlTemplate x:Key=\"T\" TargetType=\"UserControl\"><Holder Tag=\"{TemplateBinding Tag, Converter={StaticResource c}, ConverterParameter={StaticResource p}}\" /></ControlTemplate></UserControl.Resources></UserControl>");
        Assert.Contains("ConverterParameter = this.FindResource(\"p\")", code);
    }

    [Fact]
    public void AStaticResourceWhoseKeyIsMarkup_KeepsItsKey()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Grid><Label Tag=\"{StaticResource {x:Static SystemColors.WindowBrushKey}}\" /></Grid></UserControl>");
        Assert.DoesNotContain("\"{x:Static", code);
    }

    [Fact]
    public void ADynamicResourceWhoseKeyIsMarkup_OnTheRoot_KeepsItsKey()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Tag=\"{DynamicResource {x:Static SystemColors.WindowBrushKey}}\" />");
        Assert.DoesNotContain("\"{x:Static", code);
    }

    [Fact]
    public void AMarkupExtensionElementInACollection_IsGivenItsOwnerAndTheCollectionAsTarget()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Holder><Holder.Items><HelloExtension /></Holder.Items></Holder></UserControl>");
        Assert.Matches(@"__XsgProvideValue\(__HelloExtension_\d+, __Holder_\d+, __Holder_\d+\.Items\)", code);
    }

    [Fact]
    public void AnISupportInitializeObject_IsBracketedByBeginInitAndEndInit()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><UserControl.Resources><Initializable x:Key=\"i\" Path=\"a\" /></UserControl.Resources></UserControl>");
        var begin = code.IndexOf(").BeginInit();", System.StringComparison.Ordinal);
        var set = code.IndexOf(".Path = \"a\"", System.StringComparison.Ordinal);
        var end = code.IndexOf(").EndInit();", System.StringComparison.Ordinal);
        Assert.True(begin >= 0 && set > begin && end > set, code);
    }

    [Fact]
    public void ATemplateLocalStaticResource_OnAPlainProperty_IsLookedUpOnTheTemplate()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><UserControl.Resources><ControlTemplate x:Key=\"T\" TargetType=\"UserControl\"><ControlTemplate.Resources><Label x:Key=\"k\" /></ControlTemplate.Resources><Holder><Column CellTemplate=\"{StaticResource k}\" /></Holder></ControlTemplate></UserControl.Resources></UserControl>");
        Assert.DoesNotContain("this.FindResource(\"k\")", code);
    }

    [Fact]
    public void AnInternalCodeBehind_GetsAnInternalGeneratedPart()
    {
        var (code, _) = Run("namespace Sample { internal partial class Picker : System.Windows.Controls.UserControl { } }", string.Format(Root, ""));
        Assert.Contains("internal partial class Picker", code);
        Assert.DoesNotContain("public partial class Picker", code);
    }

    [Fact]
    public void AUrnXmlnsMappedByXmlnsDefinition_ResolvesItsTypes()
    {
        var (code, errors) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { } }",
            "<UserControl x:Class=\"Sample.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:styles=\"urn:Sample.Styles\" Tag=\"{x:Static styles:ResourceKeys.ListBoxStyle}\" />");
        Assert.True(code.Contains("global::Sample.Hidden.Styles.ResourceKeys.ListBoxStyle"), string.Join(",", errors) + code);
        Assert.DoesNotContain("CS0400", errors);
    }

    [Fact]
    public void TheNamedRootElement_IsABindingSourceOfTheClassType()
    {
        var (code, _) = Run("namespace Sample { public partial class Picker : System.Windows.Controls.UserControl { public int H { get; set; } } }",
            "<UserControl x:Class=\"Sample.Picker\" x:Name=\"root\" x:DataType=\"x:Object\"><TextBox Text=\"{Binding H, ElementName=root}\" /></UserControl>");
        Assert.DoesNotContain("not compiled", code);
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
