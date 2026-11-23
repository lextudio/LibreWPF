using System.Collections.Immutable;
using LibreWPF.Xaml.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>
/// Styles and control templates in an XSG theme dictionary, generated against the types the compilation can see. The stand-ins below
/// declare the WPF members the generator looks up (the static <c>XProperty</c> fields, CLR property types, enums), so the test checks
/// that dependency properties resolve to their declaring type and that literals become typed values at build time.
/// </summary>
public class ThemeDictionaryTests
{
    private const string StandIns = """
        namespace System.Windows
        {
            public class DependencyProperty { }
            public class DependencyObject { }
            public class DataTemplate { }
            public class DataTemplateKey { public DataTemplateKey(object type) { } }
            public enum HorizontalAlignment { Left, Center, Right, Stretch }
            public struct Thickness { public Thickness(double u) { } public Thickness(double l, double t, double r, double b) { } }
            public class UIElement : DependencyObject { public static readonly DependencyProperty IsMouseOverProperty = new(); public bool IsMouseOver { get; } }
            public class FrameworkElement : UIElement
            {
                public static readonly DependencyProperty MarginProperty = new();
                public Thickness Margin { get; set; }
                public static readonly DependencyProperty HorizontalAlignmentProperty = new();
                public HorizontalAlignment HorizontalAlignment { get; set; }
            }
            public class Window : System.Windows.Controls.ContentControl { }
            public class SystemColors { public static object WindowBrushKey => null; }
        }
        namespace System.Windows.Media
        {
            public class Brush { }
            public class SolidColorBrush : Brush { }
        }
        namespace System.Windows.Controls
        {
            public class Control : FrameworkElement
            {
                public static readonly DependencyProperty BackgroundProperty = new();
                public System.Windows.Media.Brush Background { get; set; }
                public static readonly DependencyProperty PaddingProperty = new();
                public Thickness Padding { get; set; }
                public static readonly DependencyProperty HorizontalContentAlignmentProperty = new();
                public HorizontalAlignment HorizontalContentAlignment { get; set; }
            }
            public class ContentControl : Control { }
            public class Border : FrameworkElement
            {
                public static readonly DependencyProperty BackgroundProperty = new();
                public System.Windows.Media.Brush Background { get; set; }
            }
            public class ContentPresenter : FrameworkElement { }
            public class RowDefinition { }
            public class Grid : FrameworkElement { public System.Collections.Generic.List<RowDefinition> RowDefinitions { get; } = new(); }
            public class Button : ContentControl { }
        }
        """;

    private const string Theme = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="{x:Type Button}">
            <Setter Property="Background" Value="{DynamicResource {x:Static SystemColors.WindowBrushKey}}" />
            <Setter Property="Padding" Value="4,2" />
            <Setter Property="HorizontalContentAlignment" Value="Center" />
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="{x:Type Button}">
                  <Border x:Name="border" Background="{TemplateBinding Background}">
                    <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Left" />
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="border" Property="Background" Value="#FF3C7FB1" />
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """;

    private static (string Code, ImmutableArray<Diagnostic> Diagnostics) Generate(params (string Name, string Value)[] properties) =>
        GenerateXaml(Theme, properties);

    private static (string Code, ImmutableArray<Diagnostic> Diagnostics) GenerateXaml(string xaml, params (string Name, string Value)[] properties)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("Theme", [CSharpSyntaxTree.ParseText(StandIns)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var options = new Options(properties.ToDictionary(p => "build_property." + p.Name, p => p.Value));
        var driver = CSharpGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new InMemoryText("/src/Theme/Themes/Aero2.NormalColor.xaml", xaml)], optionsProvider: options);
        var result = driver.RunGenerators(compilation).GetRunResult();
        return (string.Join("\n", result.GeneratedTrees.Select(t => t.ToString())), result.Diagnostics);
    }

    [Fact]
    public void SettersAreTypedAgainstTheDeclaringTypeOfEachProperty()
    {
        var (code, diagnostics) = Generate(("MSBuildProjectDirectory", "/src/Theme"));
        Assert.Empty(diagnostics);
        Assert.Contains("TargetType = typeof(global::System.Windows.Controls.Button);", code);
        Assert.Contains("new global::System.Windows.Setter(global::System.Windows.Controls.Control.BackgroundProperty, new global::System.Windows.DynamicResourceExtension(global::System.Windows.SystemColors.WindowBrushKey))", code);
        Assert.Contains("new global::System.Windows.Setter(global::System.Windows.Controls.Control.PaddingProperty, new global::System.Windows.Thickness(4d, 2d, 4d, 2d))", code);
        Assert.Contains("new global::System.Windows.Setter(global::System.Windows.Controls.Control.HorizontalContentAlignmentProperty, global::System.Windows.HorizontalAlignment.Center)", code);
        Assert.Contains("this[typeof(global::System.Windows.Controls.Button)] = ", code);
    }

    [Fact]
    public void ATemplateIsAWholeFactoryTree_WithTemplateBindingsAndNamedTriggerSetters()
    {
        var (code, _) = Generate();
        Assert.Contains("new global::System.Windows.FrameworkElementFactory(typeof(global::System.Windows.Controls.Border));", code);
        Assert.Contains(".Name = \"border\";", code);
        Assert.Contains("SetValue(global::System.Windows.Controls.Border.BackgroundProperty, new global::System.Windows.TemplateBindingExtension(global::System.Windows.Controls.Control.BackgroundProperty));", code);
        Assert.Contains("SetValue(global::System.Windows.FrameworkElement.MarginProperty, new global::System.Windows.TemplateBindingExtension(global::System.Windows.Controls.Control.PaddingProperty));", code);
        Assert.Contains("SetValue(global::System.Windows.FrameworkElement.HorizontalAlignmentProperty, global::System.Windows.HorizontalAlignment.Left);", code);
        Assert.Contains(".AppendChild(", code);
        Assert.Contains("new global::System.Windows.Trigger { Property = global::System.Windows.UIElement.IsMouseOverProperty, Value = true }", code);
        Assert.Contains("new global::System.Windows.Setter(global::System.Windows.Controls.Border.BackgroundProperty, new global::System.Windows.Media.SolidColorBrush(global::System.Windows.Media.Color.FromArgb(255, 60, 127, 177)), \"border\")", code);
    }

    [Fact]
    public void AThemeRegistersUnderItsResourcePath_AndUnderAnotherAssemblyNameWhenAsked()
    {
        var (own, _) = Generate(("MSBuildProjectDirectory", "/src/Theme"));
        Assert.Contains("ResourcePath = \"themes/aero2.normalcolor.xaml\"", own);
        Assert.Contains("RegisterDictionary(typeof(", own);

        var (standIn, _) = Generate(("MSBuildProjectDirectory", "/src/Theme"), ("LibreWpfXsgResourceAssemblyName", "PresentationFramework.Aero2"));
        Assert.Contains("RegisterDictionary(\"PresentationFramework.Aero2\", ResourcePath, ", standIn);
    }

    // ---- nothing is left out quietly ------------------------------------------------------------------------------------------

    private const string Header = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
        """;

    [Fact]
    public void AThemeOfHandledConstructsOnly_HasNoNotGeneratedDiagnostic()
    {
        var (_, diagnostics) = Generate(("MSBuildProjectDirectory", "/src/Theme"));

        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
    }

    [Theory]
    [InlineData("<Style TargetType=\"{x:Type Button}\"><Style.Triggers><MultiTrigger><MultiTrigger.Conditions><Condition Property=\"IsMouseOver\" Value=\"True\" /></MultiTrigger.Conditions></MultiTrigger></Style.Triggers></Style>", "MultiTrigger")]
    [InlineData("<Style TargetType=\"{x:Type Button}\"><Style.Triggers><DataTrigger Binding=\"{Binding A}\" Value=\"1\" /></Style.Triggers></Style>", "DataTrigger")]
    [InlineData("<Style TargetType=\"{x:Type Button}\"><Style.Resources><SolidColorBrush x:Key=\"b\" Color=\"Red\" /></Style.Resources></Style>", "Style.Resources")]
    [InlineData("<Style TargetType=\"{x:Type Button}\"><EventSetter Event=\"Click\" Handler=\"OnClick\" /></Style>", "EventSetter")]
    [InlineData("<Border />", "without x:Key")]
    public void AConstructTheGeneratorDoesNotHandleYet_IsALocatedDiagnostic_NotJustAComment(string content, string named)
    {
        var (_, diagnostics) = GenerateXaml(Header + content + "</ResourceDictionary>", ("MSBuildProjectDirectory", "/src/Theme"));

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "LWXSG0008");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        var message = diagnostic.GetMessage();
        Assert.Contains(named, message);
        Assert.Contains("/src/Theme/Themes/Aero2.NormalColor.xaml(", message); // file and line, like every other XSG diagnostic
    }

    [Fact]
    public void APropertyElementOfTheElementItself_IsStillGenerated_AndNotReported()
    {
        var xaml = Header + """
            <Style TargetType="{x:Type Button}">
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="{x:Type Button}">
                    <Border><Border.Background><SolidColorBrush Color="Red" /></Border.Background></Border>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            </ResourceDictionary>
            """;

        var (code, diagnostics) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.Contains("Border.BackgroundProperty", code);
        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
    }

    [Fact]
    public void VisualStateGroupsInATemplate_AreADiagnostic()
    {
        // An attached-property element (a dotted element name) is not a child element: it used to be dropped without a word.
        var xaml = Header + """
            <Style TargetType="{x:Type Button}">
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="{x:Type Button}">
                    <Border x:Name="root">
                      <VisualStateManager.VisualStateGroups>
                        <VisualStateGroup x:Name="CommonStates"><VisualState x:Name="MouseOver" /></VisualStateGroup>
                      </VisualStateManager.VisualStateGroups>
                    </Border>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            </ResourceDictionary>
            """;

        var (_, diagnostics) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.Contains(diagnostics, d => d.Id == "LWXSG0008" && d.GetMessage().Contains("VisualStateManager.VisualStateGroups"));
    }

    [Fact]
    public void ATemplateWithResources_IsADiagnostic()
    {
        var xaml = Header + """
            <Style TargetType="{x:Type Button}">
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="{x:Type Button}">
                    <ControlTemplate.Resources><SolidColorBrush x:Key="b" Color="Red" /></ControlTemplate.Resources>
                    <Border />
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            </ResourceDictionary>
            """;

        var (_, diagnostics) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.Contains(diagnostics, d => d.Id == "LWXSG0008" && d.GetMessage().Contains("template resources"));
    }

    [Fact]
    public void AnXTypeKey_IsATypeKey_NotTheTextOfTheMarkup()
    {
        // x:Key="{x:Type Button}" is the implicit style key; emitted as a quoted string it registered the resource under a wrong key.
        var xaml = Header + "<Border x:Key=\"{x:Type Button}\" />" + "</ResourceDictionary>";

        var (code, _) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.Contains("this[typeof(global::System.Windows.Controls.Button)] =", code);
        Assert.DoesNotContain("{x:Type", code);
    }

    [Fact]
    public void ADataTemplateWithoutAKey_IsKeyedByADataTemplateKey_AndAStyleByItsType()
    {
        var xaml = Header + "<DataTemplate DataType=\"{x:Type Button}\"><Border /></DataTemplate>"
            + "<Style TargetType=\"{x:Type Button}\" />" + "</ResourceDictionary>";

        var (code, _) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.Contains("this[new global::System.Windows.DataTemplateKey(typeof(global::System.Windows.Controls.Button))] =", code);
        const string styleKey = "this[typeof(global::System.Windows.Controls.Button)] =";
        Assert.Equal(1, code.Split([styleKey], StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ACollectionPropertyElementInATemplate_IsReported_NotSetAsAMissingDependencyProperty()
    {
        // Grid.RowDefinitions has no static RowDefinitionsProperty field and holds several items: SetValue(RowDefinitionsProperty, first
        // item) would not compile (and would be wrong if it did).
        var xaml = Header + """
            <Style TargetType="{x:Type Button}">
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="{x:Type Button}">
                    <Grid><Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions></Grid>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            </ResourceDictionary>
            """;

        var (code, diagnostics) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.DoesNotContain("RowDefinitionsProperty", code);
        Assert.Contains(diagnostics, d => d.Id == "LWXSG0008" && d.GetMessage().Contains("Grid.RowDefinitions"));
    }

    [Fact]
    public void ATypeOfTheGlobalNamespace_HasNoLeadingDotInItsName()
    {
        var xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:g="clr-namespace:;assembly=Theme">
            <Border x:Key="{x:Type g:Foo}" />
            </ResourceDictionary>
            """;

        var (code, _) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.Contains("this[typeof(global::Foo)] =", code);
        Assert.DoesNotContain("global::.", code);
    }

    [Fact]
    public void APlainTextKey_IsStillAString()
    {
        var xaml = Header + "<Border x:Key=\"plain\" />" + "</ResourceDictionary>";

        var (code, _) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.Contains("this[\"plain\"] =", code);
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
