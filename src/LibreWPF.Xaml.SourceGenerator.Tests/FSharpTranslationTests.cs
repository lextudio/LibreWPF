using System.Reflection;
using Xunit;

namespace LibreWPF.Xaml.SourceGenerator.Tests;

/// <summary>The F# output of LibreWPF.Xaml.FSharp.Tool: the generator's C# object graph printed as F#.</summary>
public class FSharpTranslationTests
{
    private static string Print(string body)
    {
        var csharp = "namespace Sample { public partial class Page : global::System.Windows.Controls.UserControl { internal global::System.Windows.Controls.Button? ok; public void InitializeComponent() {" + body + "} } }";
        var printer = Type.GetType("LibreWPF.Xaml.FSharp.FSharpPrinter, LibreWPF.Xaml.FSharp.Tool")!;
        var method = printer.GetMethod("TryPrint", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        var arguments = new object?[] { csharp, null, null, false };
        var success = (bool)method.Invoke(null, arguments)!;
        Assert.True(success, (string?)arguments[2]);
        return (string)arguments[1]!;
    }

    [Fact]
    public void TheClass_InheritsTheRoot_DeclaresFields_AndBuildsTheTreeInItsConstructor()
    {
        var fs = Print("var b = new global::System.Windows.Controls.Button(); b.Width = 80d; this.ok = b; this.Content = b;");
        Assert.Contains("namespace Sample", fs);
        Assert.Contains("type Page() as this =", fs);
        Assert.Contains("inherit global.System.Windows.Controls.UserControl()", fs);
        Assert.Contains("do this.InitializeComponent()", fs);
        Assert.Contains("[<DefaultValue>] val mutable internal ok : global.System.Windows.Controls.Button", fs);
        Assert.Contains("let b = global.System.Windows.Controls.Button()", fs);
        Assert.Contains("b.Width <- 80.0", fs);
        Assert.Contains("this.ok <- b", fs);
    }

    [Fact]
    public void AComponentRegistration_BecomesAnAttributeOfTheClass_AndTheConstructorRegistersTheAssembly()
    {
        var csharp = "namespace Sample { public partial class Page : global::System.Windows.Controls.UserControl { public void InitializeComponent() { } }"
            + " internal static class __XsgComponent_Sample_Page { internal static void Register() { global::System.Windows.Markup.XamlSourceGeneratedResources.RegisterComponent(\"asm\", \"page.xaml\", static () => new global::Sample.Page()); } } }";
        var printer = Type.GetType("LibreWPF.Xaml.FSharp.FSharpPrinter, LibreWPF.Xaml.FSharp.Tool")!;
        var method = printer.GetMethod("TryPrint", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        var arguments = new object?[] { csharp, null, null, false };
        Assert.True((bool)method.Invoke(null, arguments)!, (string?)arguments[2]);
        var fs = (string)arguments[1]!;
        Assert.Contains("[<LibreWPF.Xaml.Generated.XsgComponent(\"asm\", \"page.xaml\", false, true)>]", fs);
        Assert.Contains("do LibreWPF.Xaml.Generated.XsgSupport.registerComponents (typeof<Page>.Assembly)", fs);
        Assert.DoesNotContain("__XsgComponent_", fs);
    }

    private static (int Exit, string Dir) RunTool(params string[] xamlTexts)
    {
        var dir = Path.Combine(Path.GetTempPath(), "xsgfs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var references = Path.Combine(dir, "refs.txt");
        // A stand-in for the WPF assemblies: just enough types for a root element.
        var stub = Path.Combine(dir, "Stub.dll");
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("Stub",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("namespace System.Windows { public class DependencyObject { } public class FrameworkElement : DependencyObject { } public class ResourceDictionary { } } namespace System.Windows.Controls { public class UserControl : System.Windows.FrameworkElement { } }")],
            [Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location), Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location)],
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using (var stream = File.Create(stub)) { Assert.True(compilation.Emit(stream).Success); }
        File.WriteAllText(references, typeof(object).Assembly.Location + "\n" + stub + "\n");
        var args = new List<string> { "--out", Path.Combine(dir, "out"), "--references", references };
        for (var i = 0; i < xamlTexts.Length; i++)
        {
            var file = Path.Combine(dir, $"p{i}.xaml");
            File.WriteAllText(file, xamlTexts[i]);
            args.Add(file);
        }
        var program = Type.GetType("LibreWPF.Xaml.FSharp.Program, LibreWPF.Xaml.FSharp.Tool")!;
        var main = program.GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)!;
        return ((int)main.Invoke(null, new object[] { args.ToArray() })!, Path.Combine(dir, "out"));
    }

    [Fact]
    public void TheTool_WithNoXamlFile_WritesOnlyTheSupportFile()
    {
        var (exit, dir) = RunTool();
        Assert.Equal(0, exit);
        Assert.Equal("0-XsgSupport.g.fs", File.ReadAllText(Path.Combine(dir, "compile.txt")).Trim());
        Assert.True(File.Exists(Path.Combine(dir, "0-XsgSupport.g.fs")));
    }

    [Fact]
    public void TheTool_TranslatesARoot_IntoTheClassFile_AndListsItInCompileOrder()
    {
        var (exit, dir) = RunTool("<UserControl x:Class=\"A.B\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />");
        Assert.Equal(0, exit);
        Assert.Equal(["0-XsgSupport.g.fs", "A_B.g.fs"], File.ReadAllLines(Path.Combine(dir, "compile.txt")));
        Assert.Contains("type B() as this =", File.ReadAllText(Path.Combine(dir, "A_B.g.fs")));
    }

    [Fact]
    public void TheTool_WritesNothing_WhenATranslationFails()
    {
        // x:Code is C#: the printer has no F# form for it, so the translation fails.
        var (exit, dir) = RunTool("<UserControl x:Class=\"A.B\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><x:Code><![CDATA[ void M() { while (true) { } } ]]></x:Code></UserControl>");
        Assert.Equal(1, exit);
        Assert.False(Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any(), "a failed run leaves no partial output");
    }

    [Fact]
    public void Events_AreWiredByNameOnTheRuntimeType()
    {
        var fs = Print("var b = new global::System.Windows.Controls.Button(); b.Click -= this.OnOk; b.Click += this.OnOk;");
        Assert.Contains("this.__wireEvent(b, \"Click\", \"OnOk\")", fs);
        Assert.DoesNotContain("-=", fs);
    }

    [Fact]
    public void NullTests_Casts_AndObjectInitializers()
    {
        var fs = Print("""
            var b = new global::System.Windows.Controls.Button();
            b.Tag = (global::System.Windows.Style)this.FindResource("k");
            if (this.FindName("n") != null) this.UnregisterName("n");
            b.Padding = new global::System.Windows.Thickness { Left = 1d };
            """);
        Assert.Contains("(this.FindResource(\"k\") :?> global.System.Windows.Style)", fs);
        Assert.Contains("if (not (isNull (this.FindName(\"n\")))) then", fs);
        Assert.Contains("global.System.Windows.Thickness(Left = 1.0)", fs);
    }

    [Fact]
    public void ADictionaryClass_HasLetFields_LazyEntries_AndTheCannedLookup()
    {
        var csharp = """
            namespace G
            {
                internal sealed partial class D : global::System.Windows.ResourceDictionary
                {
                    internal const string ResourcePath = "t.xaml";
                    public D() { InitializeComponent(); }
                    private readonly global::System.Collections.Generic.List<global::System.Windows.ResourceDictionary> __scopes = new global::System.Collections.Generic.List<global::System.Windows.ResourceDictionary>();
                    private object FindResource(object key) { return null; }
                    private void InitializeComponent()
                    {
                        this["A"] = new global::System.Windows.Markup.XamlSourceGeneratedResources.LazyEntry(__Create_1, true);
                        var d = __XsgLoadDictionary("asm", "x.xaml");
                    }
                    private object __Create_1()
                    {
                        var b = new global::System.Windows.Media.SolidColorBrush();
                        __scopes.Add(null);
                        return b;
                    }
                }
            }
            """;
        var printer = Type.GetType("LibreWPF.Xaml.FSharp.FSharpPrinter, LibreWPF.Xaml.FSharp.Tool")!;
        var method = printer.GetMethod("TryPrint", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        var arguments = new object?[] { csharp, null, null, false };
        Assert.True((bool)method.Invoke(null, arguments)!, (string?)arguments[2]);
        var fs = (string)arguments[1]!;
        Assert.Contains("let __scopes = ", fs);
        Assert.Contains("this.[\"A\"] <- global.System.Windows.Markup.XamlSourceGeneratedResources.LazyEntry(System.Func<obj>(this.__Create_1), true)", fs);
        Assert.Contains("member private this.FindResource(key: obj) : obj =", fs);
        Assert.Contains("member private this.__Create_1() : obj =", fs);
        Assert.Contains("box (b)", fs);
        Assert.Contains("LibreWPF.Xaml.Generated.XsgSupport.loadDictionary(\"asm\", \"x.xaml\")", fs);
        Assert.DoesNotContain("ResourcePath", fs);
    }

    [Fact]
    public void Lambdas_BecomeFunctions_AndAnObjectCreationIsUpcast()
    {
        var fs = Print("var f = new global::System.Windows.FrameworkElementFactory(typeof(global::System.Windows.Controls.TextBlock)); f.SetCreator(static () => new global::System.Windows.Controls.TextBlock()); var p = new global::G.Property<global::A, int>(\"n\", static o => o.X!, static (o, v) => o.X = v);");
        Assert.Contains("f.SetCreator((fun () -> (global.System.Windows.Controls.TextBlock() :> _))) |> ignore", fs);
        Assert.Contains("(fun o -> o.X)", fs);
        Assert.Contains("(fun o v -> o.X <- v)", fs);
    }
}
