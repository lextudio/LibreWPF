using System.Reflection;
using LibreWPF.Xaml.SourceGenerator;
using Microsoft.CodeAnalysis;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>The Visual Basic output is the generator's C# graph printed as Visual Basic: these tests pin the printer on the shapes it emits.</summary>
public class VisualBasicTranslationTests
{
    private static string Translate(string body)
    {
        var csharp = "namespace Sample { public partial class Page : Base { internal global::System.Windows.Controls.Button? ok; public void InitializeComponent() {" + body + "} } }";
        var method = typeof(XamlSourceGenerator).GetMethod("TryTranslateToVisualBasic", BindingFlags.NonPublic | BindingFlags.Static)!;
        var arguments = new object?[] { csharp, null, null };
        var success = (bool)method.Invoke(null, arguments)!;
        Assert.True(success, (string?)arguments[2]);
        return (string)arguments[1]!;
    }

    [Fact]
    public void QualifiedNames_Use_Global_WithoutBrackets()
    {
        var vb = Translate("var b = new global::System.Windows.Controls.Button(); b.Margin = new global::System.Windows.Thickness(1d, 2d, 3d, 4d); b.Visibility = global::System.Windows.Visibility.Collapsed;");
        Assert.Contains("Dim b = New Global.System.Windows.Controls.Button()", vb);
        Assert.Contains("b.Margin = New Global.System.Windows.Thickness(1.0R, 2.0R, 3.0R, 4.0R)", vb);
        Assert.Contains("b.Visibility = Global.System.Windows.Visibility.Collapsed", vb);
        Assert.DoesNotContain("[Global]", vb);
        Assert.Contains("Namespace Global.Sample", vb);
        Assert.Contains("Inherits Base", vb);
    }

    [Fact]
    public void ExtensionCall_AndConverterCast_KeepGlobalPlain()
    {
        var vb = Translate("var g = new global::System.Windows.Controls.GroupBox(); __Apply(g, global::System.Windows.Controls.ContentControl.ContentProperty, \"Content\", new global::A.B.LocalizeExtension(\"k\")); g.Tag = (global::System.Windows.Data.IMultiValueConverter)this.FindResource(\"c\");");
        Assert.DoesNotContain("[Global]", vb);
    }

    [Fact]
    public void Events_Casts_TypeOf_AndDeferredNames()
    {
        var vb = Translate("""
            var b = new global::System.Windows.Controls.Button();
            b.Click -= this.OnClick;
            b.Click += this.OnClick;
            this.ok = b;
            b.Tag = (global::System.Windows.Style)this.FindResource("k");
            b.Name = "a\"b\n";
            b.DataContext = typeof(global::System.String);
            global::System.Action? deferred = null;
            deferred += () => b.Tag = this.ok;
            deferred?.Invoke();
            if (this.FindName("n") != null) this.UnregisterName("n");
            b.Padding = new global::System.Windows.Thickness { Left = 1d };
            """);
        Assert.Contains("RemoveHandler b.Click, AddressOf Me.OnClick", vb);
        Assert.Contains("AddHandler b.Click, AddressOf Me.OnClick", vb);
        Assert.Contains("Me.ok = b", vb);
        Assert.Contains("CType(Me.FindResource(\"k\"), Global.System.Windows.Style)", vb);
        Assert.Contains("\"a\"\"b\" & vbLf", vb);
        Assert.Contains("GetType(Global.System.", vb);
        Assert.Contains("Dim deferred As Global.System.Action = Nothing", vb);
        Assert.Contains("DirectCast([Delegate].Combine(deferred, Sub() b.Tag = Me.ok), System.Action)", vb);
        Assert.Contains("Call deferred?.Invoke()", vb);
        Assert.Contains("If Me.FindName(\"n\") IsNot Nothing Then", vb);
        Assert.Contains("New Global.System.Windows.Thickness() With {.Left = 1.0R}", vb);
    }
}

public class VisualBasicGeneratorTests
{
    [Fact]
    public void AVisualBasicProject_GetsVisualBasicOutput()
    {
        const string vb = """
            Namespace System.Windows.Markup
                Public Class XamlSourceGeneratedResources
                    Public Shared Sub RegisterComponent(a As String, b As String, f As System.Func(Of Object))
                    End Sub
                End Class
            End Namespace
            Namespace System.Windows
                Public Class DependencyObject
                End Class
                Public Class FrameworkElement
                    Inherits DependencyObject
                    Public Property Name As String
                    Public Function FindName(n As String) As Object
                        Return Nothing
                    End Function
                    Public Sub RegisterName(n As String, o As Object)
                    End Sub
                    Public Sub UnregisterName(n As String)
                    End Sub
                End Class
            End Namespace
            Namespace System.Windows.Controls
                Public Class UserControl
                    Inherits System.Windows.FrameworkElement
                End Class
                Public Class TextBox
                    Inherits System.Windows.FrameworkElement
                    Public Property Text As String
                End Class
                Public Class Grid
                    Inherits System.Windows.FrameworkElement
                    Public ReadOnly Property Children As New System.Collections.Generic.List(Of Object)
                End Class
            End Namespace
            Namespace Panels
                Partial Public Class Picker
                End Class
            End Namespace
            """;
        var compilation = Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation.Create("Sample",
            [Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(vb)],
            [Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location), Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
             Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location)],
            new Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, rootNamespace: "Sample"));
        var driver = Microsoft.CodeAnalysis.VisualBasic.VisualBasicGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new MemoryText("/src/Picker.xaml", "<UserControl x:Class=\"Panels.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><Grid><TextBox x:Name=\"box\" Text=\"hi\" /></Grid></UserControl>")]);
        var result = driver.RunGenerators(compilation).GetRunResult();
        var text = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        Assert.True(text.Contains("Partial Class Picker") && text.Contains("Namespace Global.Sample.Panels") && text.Contains("Call __Grid_1.Children.Add(") && text.Contains("Friend WithEvents box As Global.System.Windows.Controls.TextBox") && text.Contains("Me.box = ") && !text.Contains("[Global]") && !text.Contains("afterNameScope") && !result.Diagnostics.Any(d => d.Id == "LWXSG1001"), text + string.Join("\n", result.Diagnostics));
    }

    [Fact]
    public void AProjectsMarkupExtension_InAVisualBasicProject_GetsTheCannedVisualBasicHelper()
    {
        const string vb = """
            Namespace Global.System.Windows.Markup
                Public Class XamlSourceGeneratedResources
                    Public Shared Sub RegisterComponent(a As String, b As String, f As System.Func(Of Object))
                    End Sub
                End Class
                Public MustInherit Class MarkupExtension
                    Public MustOverride Function ProvideValue(serviceProvider As System.IServiceProvider) As Object
                End Class
                Public Interface IProvideValueTarget
                    ReadOnly Property TargetObject As Object
                    ReadOnly Property TargetProperty As Object
                End Interface
            End Namespace
            Namespace Global.System.Windows
                Public Class DependencyObject
                    Public Sub SetValue(p As DependencyProperty, v As Object)
                    End Sub
                End Class
                Public Class DependencyProperty
                    Public ReadOnly Property PropertyType As System.Type
                End Class
                Public Class FrameworkElement
                    Inherits DependencyObject
                    Public Property Name As String
                    Public Function FindName(n As String) As Object
                        Return Nothing
                    End Function
                    Public Sub RegisterName(n As String, o As Object)
                    End Sub
                    Public Sub UnregisterName(n As String)
                    End Sub
                End Class
            End Namespace
            Namespace Global.System.Windows.Data
                Public Class BindingBase
                End Class
                Public Class BindingOperations
                    Public Shared Sub SetBinding(d As System.Windows.DependencyObject, p As System.Windows.DependencyProperty, b As BindingBase)
                    End Sub
                End Class
            End Namespace
            Namespace Global.System.Windows.Controls
                Public Class UserControl
                    Inherits System.Windows.FrameworkElement
                End Class
                Public Class TextBox
                    Inherits System.Windows.FrameworkElement
                    Public Property Text As String
                    Public Shared ReadOnly TextProperty As System.Windows.DependencyProperty
                End Class
                Public Class Grid
                    Inherits System.Windows.FrameworkElement
                    Public ReadOnly Property Children As New System.Collections.Generic.List(Of Object)
                End Class
            End Namespace
            Namespace Panels
                Public Class HelloExtension
                    Inherits System.Windows.Markup.MarkupExtension
                    Public Sub New(who As String)
                    End Sub
                    Public Overrides Function ProvideValue(serviceProvider As System.IServiceProvider) As Object
                        Return "hi"
                    End Function
                End Class
                Partial Public Class Picker
                End Class
            End Namespace
            """;
        var compilation = Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation.Create("Sample",
            [Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(vb)],
            [Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location), Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
             Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location)],
            new Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, rootNamespace: "Sample"));
        var driver = Microsoft.CodeAnalysis.VisualBasic.VisualBasicGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new MemoryText("/src/Picker.xaml", "<UserControl x:Class=\"Panels.Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:p=\"clr-namespace:Sample.Panels\"><Grid><TextBox Text=\"{p:Hello World}\" /></Grid></UserControl>")]);
        var result = driver.RunGenerators(compilation).GetRunResult();
        var text = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        Assert.True(!result.Diagnostics.Any(d => d.Id == "LWXSG1001") && text.Contains("Private Shared Sub __XsgApplyExtension") && text.Contains("Call __XsgApplyExtension("),
            text + string.Join("\n", result.Diagnostics));
    }

    [Fact]
    public void AnXClassThatMatchesTwoClasses_IsAnError_NotAGuess()
    {
        const string vb = """
            Namespace A
                Partial Public Class Picker
                End Class
            End Namespace
            Namespace B
                Partial Public Class Picker
                End Class
            End Namespace
            """;
        var compilation = Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation.Create("Sample",
            [Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(vb)],
            [Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary, rootNamespace: "Sample"));
        var driver = Microsoft.CodeAnalysis.VisualBasic.VisualBasicGeneratorDriver.Create([new XamlSourceGenerator().AsSourceGenerator()],
            additionalTexts: [new MemoryText("/src/Picker.xaml", "<UserControl x:Class=\"Picker\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />")]);
        var result = driver.RunGenerators(compilation).GetRunResult();
        Assert.Contains(result.Diagnostics, d => d.Id == "LWXSG1001" && d.GetMessage().Contains("more than one class"));
    }

    private sealed class MemoryText(string path, string text) : Microsoft.CodeAnalysis.AdditionalText
    {
        public override string Path => path;

        public override Microsoft.CodeAnalysis.Text.SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => Microsoft.CodeAnalysis.Text.SourceText.From(text);
    }
}
