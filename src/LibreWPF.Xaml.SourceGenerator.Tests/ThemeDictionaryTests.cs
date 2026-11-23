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
            public class FrameworkContentElement : DependencyObject { }
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
        namespace System.Windows.Markup
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ContentPropertyAttribute : System.Attribute { public ContentPropertyAttribute(string name) { } }
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
            [System.Windows.Markup.ContentProperty("Child")]
            public class Border : FrameworkElement
            {
                public static readonly DependencyProperty BackgroundProperty = new();
                public System.Windows.Media.Brush Background { get; set; }
                public FrameworkElement Child { get; set; }
            }
            public class ColumnDefinition : System.Windows.FrameworkContentElement { }
            public class ColumnDefinitionCollection : System.Collections.Generic.List<ColumnDefinition> { }
            public class ContentPresenter : FrameworkElement { }
            public class RowDefinition : System.Windows.DependencyObject { }
            public class RowDefinitionCollection : System.Collections.Generic.List<RowDefinition> { }
            public class Grid : FrameworkElement
            {
                public RowDefinitionCollection RowDefinitions { get; } = new();
                public ColumnDefinitionCollection ColumnDefinitions { get; } = new();
            }
            public class Thumb : FrameworkElement { }
            public class Track : FrameworkElement { public Thumb Thumb { get; set; } }
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
    public void StyleAndTemplateResources_AreGenerated_AndVisibleToWhatIsBuiltAfterThem()
    {
        var xaml = Header + """
            <Style TargetType="{x:Type Button}">
              <Style.Resources><SolidColorBrush x:Key="local" Color="Red" /></Style.Resources>
              <Setter Property="Background" Value="{StaticResource local}" />
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="{x:Type Button}">
                    <ControlTemplate.Resources><SolidColorBrush x:Key="b" Color="Blue" /></ControlTemplate.Resources>
                    <Border Background="{StaticResource b}" />
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            </ResourceDictionary>
            """;

        var (code, diagnostics) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
        Assert.Contains(".Resources[\"local\"] = ", code);
        Assert.Contains(".Resources[\"b\"] = ", code);
        Assert.Contains("__scopes.Add(", code);                     // the resources being built are visible to what follows
        // A key that exists only on the style / the template instance is looked up when it is applied, not when the theme is built.
        Assert.Contains("new global::System.Windows.DynamicResourceExtension(\"local\")", code);
        Assert.Contains(".SetResourceReference(global::System.Windows.Controls.Border.BackgroundProperty, \"b\");", code);
        Assert.DoesNotContain("this.FindResource(\"local\")", code);
        Assert.DoesNotContain("this.FindResource(\"b\")", code);
    }

    [Fact]
    public void AResourceDoesNotSeeItselfInItsOwnDictionary_ABasedOnOfTheSameKeyIsTheOuterOne()
    {
        // <Style x:Key="{x:Type Button}" BasedOn="{StaticResource {x:Type Button}}"> inside a .Resources refers to the outer definition.
        var xaml = Header + """
            <Style TargetType="{x:Type Button}">
              <Style.Resources>
                <Style x:Key="{x:Type Button}" TargetType="{x:Type Button}" BasedOn="{StaticResource {x:Type Button}}" />
              </Style.Resources>
            </Style>
            </ResourceDictionary>
            """;

        var (code, _) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.Contains("BasedOn = (global::System.Windows.Style)this.FindResource(typeof(global::System.Windows.Controls.Button))", code);
        Assert.DoesNotContain("DynamicResourceExtension(typeof(", code);
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
    public void ATriggerFamily_IsGenerated_WithItsConditionsAndSetters()
    {
        var xaml = Header + """
            <Style TargetType="{x:Type Button}">
              <Style.Triggers>
                <MultiTrigger>
                  <MultiTrigger.Conditions>
                    <Condition Property="IsMouseOver" Value="True" />
                    <Condition Property="Padding" Value="1" />
                  </MultiTrigger.Conditions>
                  <Setter Property="Background" Value="#FF0000" />
                </MultiTrigger>
                <DataTrigger Binding="{Binding A}" Value="1">
                  <Setter Property="Padding" Value="2" />
                </DataTrigger>
                <MultiDataTrigger>
                  <MultiDataTrigger.Conditions>
                    <Condition Binding="{Binding A}" Value="1" />
                    <Condition Binding="{Binding B}" Value="2" />
                  </MultiDataTrigger.Conditions>
                  <MultiDataTrigger.Setters>
                    <Setter Property="Padding" Value="3" />
                  </MultiDataTrigger.Setters>
                </MultiDataTrigger>
              </Style.Triggers>
            </Style>
            </ResourceDictionary>
            """;

        var (code, diagnostics) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
        Assert.Contains("new global::System.Windows.MultiTrigger();", code);
        Assert.Contains(".Conditions.Add(new global::System.Windows.Condition { Property = global::System.Windows.UIElement.IsMouseOverProperty, Value = true });", code);
        Assert.Contains("new global::System.Windows.DataTrigger { Binding = new global::System.Windows.Data.Binding(\"A\"), Value = \"1\" };", code);
        Assert.Contains("new global::System.Windows.MultiDataTrigger();", code);
        Assert.Contains(".Conditions.Add(new global::System.Windows.Condition { Binding = new global::System.Windows.Data.Binding(\"B\"), Value = \"2\" });", code);
        Assert.Equal(3, code.Split(["Triggers.Add("], StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void EventTriggersAndTriggerActions_AreGenerated()
    {
        var xaml = Header + """
            <Style TargetType="{x:Type Button}">
              <Style.Triggers>
                <EventTrigger RoutedEvent="Button.Click">
                  <BeginStoryboard />
                </EventTrigger>
                <Trigger Property="IsMouseOver" Value="True">
                  <Trigger.EnterActions><BeginStoryboard /></Trigger.EnterActions>
                  <Trigger.ExitActions><BeginStoryboard /></Trigger.ExitActions>
                </Trigger>
                <DataTrigger Value="1">
                  <DataTrigger.Binding><Binding Path="A"><Binding.RelativeSource><RelativeSource Mode="Self" /></Binding.RelativeSource></Binding></DataTrigger.Binding>
                </DataTrigger>
              </Style.Triggers>
            </Style>
            </ResourceDictionary>
            """;

        var (code, _) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.Contains("new global::System.Windows.EventTrigger { RoutedEvent = global::System.Windows.Controls.Button.ClickEvent };", code);
        Assert.Contains(".Actions.Add(", code);
        Assert.Contains(".EnterActions.Add(", code);
        Assert.Contains(".ExitActions.Add(", code);
        Assert.Contains("Binding = new global::System.Windows.Data.Binding(\"A\") { RelativeSource = new global::System.Windows.Data.RelativeSource(global::System.Windows.Data.RelativeSourceMode.Self) }", code);
    }

    [Fact]
    public void ABindingToTheTemplatedParent_IsCompiled_WithTheDependencyPropertyItself()
    {
        // A descriptor of our own could not tell the binding when a dependency property changes; the DependencyProperty can.
        var xaml = Header + """
            <Style TargetType="{x:Type Button}">
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="{x:Type Button}">
                    <Border Background="{Binding Path=Background, RelativeSource={RelativeSource TemplatedParent}}" />
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            </ResourceDictionary>
            """;

        var (code, diagnostics) = GenerateXaml(xaml, ("MSBuildProjectDirectory", "/src/Theme"));

        Assert.DoesNotContain(diagnostics, d => d.Id is "LWXSG0007" or "LWXSG0008");
        Assert.Contains("new global::System.Windows.PropertyPath(\"(0)\", global::System.Windows.Controls.Control.BackgroundProperty)", code);
        Assert.DoesNotContain("__XsgCompiledProperty", code);
    }

    private static (string Code, ImmutableArray<Diagnostic> Diagnostics) Template(string content) =>
        GenerateXaml(Header + """
            <Style TargetType="{x:Type Button}">
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="{x:Type Button}">
            """ + content + """
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            </ResourceDictionary>
            """, ("MSBuildProjectDirectory", "/src/Theme"));

    [Fact]
    public void AFactoryNode_IsCreatedByCode_NotByActivator()
    {
        var (code, _) = Template("<Border />");

        Assert.Contains(".SetCreator(static () => new global::System.Windows.Controls.Border());", code);
    }

    [Fact]
    public void TheContentPropertyWrittenAsAnElement_IsAFactoryChild_NotInitializerCode()
    {
        // <Border.Child> is the template's own visual child: named, bound and targetable like any other factory child.
        var (code, diagnostics) = Template("<Border><Border.Child><ContentPresenter x:Name=\"presenter\" /></Border.Child></Border>");

        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
        Assert.Contains(".Name = \"presenter\";", code);
        Assert.Contains(".AppendChild(", code);
        Assert.DoesNotContain(".SetInitializer(", code);
    }

    [Fact]
    public void AnElementValuedProperty_IsAFactoryChildWithAnAttacher()
    {
        // <Track.Thumb> is a CLR property, not a dependency property: the node is still a template node (a trigger can target "Thumb"),
        // and the factory is told how to attach it.
        var (code, diagnostics) = Template("<Track><Track.Thumb><Thumb x:Name=\"Thumb\" /></Track.Thumb></Track>");

        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
        Assert.Contains(".Name = \"Thumb\";", code);
        Assert.Contains(".SetAttacher((__p, __c) => ((global::System.Windows.Controls.Track)__p).Thumb = (global::System.Windows.Controls.Thumb)__c);", code);
        Assert.DoesNotContain(".SetInitializer(", code);
    }

    [Fact]
    public void ACollectionOfElements_IsFactoryChildren_WithAnAttacherThatAddsThem()
    {
        var (code, diagnostics) = Template("<Grid><Grid.ColumnDefinitions><ColumnDefinition x:Name=\"c0\" /><ColumnDefinition /></Grid.ColumnDefinitions></Grid>");

        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
        Assert.Equal(2, code.Split([".ColumnDefinitions.Add((global::System.Windows.Controls.ColumnDefinition)__c)"], StringSplitOptions.None).Length - 1);
        Assert.Contains(".Name = \"c0\";", code);
    }

    [Fact]
    public void LibreWpfXsgExcludeTargetTypes_LeavesOutTheStylesOfThoseTypes_AndSaysSo()
    {
        var (code, diagnostics) = GenerateXaml(Header + """
            <Style TargetType="{x:Type Button}"><Setter Property="Padding" Value="1" /></Style>
            <Style TargetType="{x:Type Border}" />
            </ResourceDictionary>
            """, ("MSBuildProjectDirectory", "/src/Theme"), ("LibreWpfXsgExcludeTargetTypes", "Button,SomethingElse"));

        Assert.DoesNotContain("this[typeof(global::System.Windows.Controls.Button)]", code);
        Assert.Contains("this[typeof(global::System.Windows.Controls.Border)]", code);
        var info = Assert.Single(diagnostics, d => d.Id == "LWXSG0009");
        Assert.Equal(DiagnosticSeverity.Info, info.Severity);
    }

    [Fact]
    public void ACollectionPropertyElementInATemplate_GoesIntoTheFactoryInitializer_NotIntoSetValue()
    {
        // Grid.RowDefinitions has no static RowDefinitionsProperty field and holds several items: it is filled by code that runs on
        // every instance the factory creates (FrameworkElementFactory.SetInitializer).
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
        Assert.DoesNotContain(diagnostics, d => d.Id == "LWXSG0008");
        Assert.Contains(".SetInitializer(", code);
        Assert.Contains("(global::System.Windows.Controls.Grid)", code);
        Assert.Contains(".RowDefinitions.Clear();", code);
        Assert.Equal(2, code.Split([".RowDefinitions.Add("], StringSplitOptions.None).Length - 1);
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
