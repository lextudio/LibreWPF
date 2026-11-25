// Licensed to the .NET Foundation under one or more agreements.
// LibreWPF XAML Source Generator (parallel to the BAML/MarkupCompilePass path).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace LibreWPF.Xaml.SourceGenerator
{
    /// <summary>
    /// Source generator that turns XAML files into generated InitializeComponent() code,
    /// mirroring the MAUI/WXSG approach. Enabled via MSBuild property
    /// <c>UseLibreWpfXamlSourceGenerator</c> or env var <c>LIBREWPF_USE_XSG</c>.
    /// </summary>
    [Generator]
    public sealed partial class XamlSourceGenerator : IIncrementalGenerator
    {
        // Common WPF presentation-namespace type -> fully qualified CLR type.
        private static readonly HashSet<string> s_knownEvents = new(StringComparer.Ordinal)
        {
            "Click", "Loaded", "Unloaded", "Initialized", "SizeChanged", "MouseLeftButtonDown",
            "MouseLeftButtonUp", "MouseRightButtonDown", "MouseRightButtonUp", "MouseMove",
            "MouseEnter", "MouseLeave", "KeyDown", "KeyUp", "TextInput", "GotFocus", "LostFocus",
            "SelectionChanged", "Checked", "Unchecked", "ValueChanged", "Closed", "Opened",
            "Expanded", "Collapsed", "DragOver", "Drop", "DragEnter", "DragLeave",
        };

        private static readonly Dictionary<string, string> s_presentationTypes =
            new(StringComparer.Ordinal)
            {
                ["Application"] = "System.Windows.Application",
                ["Window"] = "System.Windows.Window",
                ["Page"] = "System.Windows.Controls.Page",
                ["UserControl"] = "System.Windows.Controls.UserControl",
                ["Grid"] = "System.Windows.Controls.Grid",
                ["StackPanel"] = "System.Windows.Controls.StackPanel",
                ["DockPanel"] = "System.Windows.Controls.DockPanel",
                ["WrapPanel"] = "System.Windows.Controls.WrapPanel",
                ["Canvas"] = "System.Windows.Controls.Canvas",
                ["Border"] = "System.Windows.Controls.Border",
                ["Button"] = "System.Windows.Controls.Button",
                ["TextBlock"] = "System.Windows.Controls.TextBlock",
                ["TextBox"] = "System.Windows.Controls.TextBox",
                ["Label"] = "System.Windows.Controls.Label",
                ["ListBox"] = "System.Windows.Controls.ListBox",
                ["ComboBox"] = "System.Windows.Controls.ComboBox",
                ["ContentControl"] = "System.Windows.Controls.ContentControl",
                ["DataGrid"] = "System.Windows.Controls.DataGrid",
                ["DataGridTextColumn"] = "System.Windows.Controls.DataGridTextColumn",
                ["DataGridCheckBoxColumn"] = "System.Windows.Controls.DataGridCheckBoxColumn",
                ["DataGridTemplateColumn"] = "System.Windows.Controls.DataGridTemplateColumn",
                ["DataGridComboBoxColumn"] = "System.Windows.Controls.DataGridComboBoxColumn",
                ["CheckBox"] = "System.Windows.Controls.CheckBox",
                ["RadioButton"] = "System.Windows.Controls.RadioButton",
                ["Slider"] = "System.Windows.Controls.Slider",
                ["ProgressBar"] = "System.Windows.Controls.ProgressBar",
                ["Image"] = "System.Windows.Controls.Image",
                ["ScrollViewer"] = "System.Windows.Controls.ScrollViewer",
                ["TabControl"] = "System.Windows.Controls.TabControl",
                ["TabItem"] = "System.Windows.Controls.TabItem",
                ["GroupBox"] = "System.Windows.Controls.GroupBox",
                ["Expander"] = "System.Windows.Controls.Expander",
                ["Rectangle"] = "System.Windows.Shapes.Rectangle",
                ["Ellipse"] = "System.Windows.Shapes.Ellipse",
                ["Line"] = "System.Windows.Shapes.Line",
                ["Path"] = "System.Windows.Shapes.Path",
                ["Polygon"] = "System.Windows.Shapes.Polygon",
                ["Polyline"] = "System.Windows.Shapes.Polyline",
                ["EllipseGeometry"] = "System.Windows.Media.EllipseGeometry",
                ["RectangleGeometry"] = "System.Windows.Media.RectangleGeometry",
                ["SolidColorBrush"] = "System.Windows.Media.SolidColorBrush",
                ["LinearGradientBrush"] = "System.Windows.Media.LinearGradientBrush",
                ["RadialGradientBrush"] = "System.Windows.Media.RadialGradientBrush",
                ["TransformGroup"] = "System.Windows.Media.TransformGroup",
                ["ScaleTransform"] = "System.Windows.Media.ScaleTransform",
                ["RotateTransform"] = "System.Windows.Media.RotateTransform",
                ["TranslateTransform"] = "System.Windows.Media.TranslateTransform",
                ["Style"] = "System.Windows.Style",
                ["DataTemplate"] = "System.Windows.DataTemplate",
                ["ControlTemplate"] = "System.Windows.Controls.ControlTemplate",
                ["ResourceDictionary"] = "System.Windows.ResourceDictionary",
                ["RowDefinition"] = "System.Windows.Controls.RowDefinition",
                ["ColumnDefinition"] = "System.Windows.Controls.ColumnDefinition",
                ["Thickness"] = "System.Windows.Thickness",
                ["CornerRadius"] = "System.Windows.CornerRadius",
                ["GridLength"] = "System.Windows.GridLength",
                ["FontFamily"] = "System.Windows.Media.FontFamily",
            };

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var xamlFiles = context.AdditionalTextsProvider
                .Where(static t => t.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));

            // The switches of the XSG features arrive as build_property.* (CompilerVisibleProperty, see
            // ProGPU.Wpf.Sdk.XamlSourceGeneratorFeatures.targets). A host that passes none keeps every feature on.
            var features = context.AnalyzerConfigOptionsProvider
                .Select(static (provider, _) => XsgFeatures.From(provider.GlobalOptions));

            // Each file with the resource path a loose ResourceDictionary is registered under (themes/generic.xaml, ...).
            var files = xamlFiles.Combine(context.AnalyzerConfigOptionsProvider)
                .Select(static (input, _) => (File: input.Left, ResourcePath: ResourcePathOf(input.Left, input.Right)));

            // Compiled bindings resolve x:DataType and binding paths against the compilation.
            context.RegisterSourceOutput(files.Combine(features).Combine(context.CompilationProvider), static (spc, input) =>
            {
                try
                {
                    s_context = new GenerationContext(input.Right, input.Left.Right, input.Left.Left.File.Path);
                    Generate(spc, input.Left.Left.File, input.Left.Right, input.Left.Left.ResourcePath);
                    foreach (var diagnostic in s_context.Diagnostics)
                    {
                        spc.ReportDiagnostic(diagnostic);
                    }
                }
                catch (Exception ex)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "LWXSG0001",
                            "XAML source generation failed",
                            ex.ToString(),
                            "LibreWPF.Xaml",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        Location.None));
                }
                finally
                {
                    s_context = null;
                }
            });
        }

        // The XSG features that can be switched off per project (LibreWpfSimplerXaml, LibreWpfCSharpExpressions).
        private readonly struct XsgFeatures : IEquatable<XsgFeatures>
        {
            public XsgFeatures(bool simplerXaml, bool csharpExpressions, bool compiledBindings, string? resourceAssemblyName = null, string? excludedTargetTypes = null, bool aotStrict = false, bool lazyDictionaries = true)
            {
                LazyDictionaries = lazyDictionaries;
                AotStrict = aotStrict;
                ResourceAssemblyName = resourceAssemblyName;
                ExcludedTargetTypes = excludedTargetTypes ?? string.Empty;
                SimplerXaml = simplerXaml;
                CSharpExpressions = csharpExpressions;
                CompiledBindings = compiledBindings;
            }

            public bool CompiledBindings { get; }

            // LibreWpfLazyDictionaries (default on): the entries of a generated resource dictionary (a theme, a Generic.xaml) are created when first asked for, each by a
            // method of its own, instead of all at once: the cost of a large theme is the JIT of one huge method, and a window uses a handful of its styles.
            public bool LazyDictionaries { get; }

            // LibreWpfXsgAotStrict (true when the project publishes with NativeAOT, see the SDK's NativeAot targets): a binding that is not compiled
            // (LWXSG0007) and a construct that is not generated (LWXSG0008) are errors, because there is no reflection or BAML fallback to run them.
            public bool AotStrict { get; }

            // LibreWpfXsgResourceAssemblyName: the assembly name loose dictionaries register under, when it is not this assembly's
            // own (an XSG-compiled theme standing in for PresentationFramework.Aero2).
            public string? ResourceAssemblyName { get; }

            // LibreWpfXsgExcludeTargetTypes: comma-separated (a ';' would start an .editorconfig comment) type names, simple or full. A loose dictionary's top-level resources whose
            // TargetType / DataType / x:Key {x:Type} names one of them are left out: a theme for a platform without a control (the
            // document viewers need System.Printing, which is Windows-only) does not style it.
            public string ExcludedTargetTypes { get; }

            public bool SimplerXaml { get; }

            public bool CSharpExpressions { get; }

            // Absent means on: the generator only runs for a project that chose the XSG path, and a host that does not pass the
            // properties gets the behaviour the generator always had. Only an explicit false switches a feature off.
            public static XsgFeatures From(AnalyzerConfigOptions options) =>
                new XsgFeatures(IsOn(options, "LibreWpfSimplerXaml"), IsOn(options, "LibreWpfCSharpExpressions"),
                    IsOn(options, "LibreWpfCompiledBindings"),
                    options.TryGetValue("build_property.LibreWpfXsgResourceAssemblyName", out var assemblyName) && !string.IsNullOrWhiteSpace(assemblyName) ? assemblyName.Trim() : null,
                    options.TryGetValue("build_property.LibreWpfXsgExcludeTargetTypes", out var excluded) ? excluded.Trim() : null,
                    options.TryGetValue("build_property.LibreWpfXsgAotStrict", out var strict) && string.Equals(strict?.Trim(), "true", StringComparison.OrdinalIgnoreCase),
                    IsOn(options, "LibreWpfLazyDictionaries"));

            private static bool IsOn(AnalyzerConfigOptions options, string name) =>
                !options.TryGetValue("build_property." + name, out var value)
                || !string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

            public bool Equals(XsgFeatures other) => SimplerXaml == other.SimplerXaml && CSharpExpressions == other.CSharpExpressions
                && CompiledBindings == other.CompiledBindings && ResourceAssemblyName == other.ResourceAssemblyName
                && ExcludedTargetTypes == other.ExcludedTargetTypes && AotStrict == other.AotStrict && LazyDictionaries == other.LazyDictionaries;

            public override bool Equals(object? obj) => obj is XsgFeatures other && Equals(other);

            public override int GetHashCode() => (SimplerXaml ? 1 : 0) | (CSharpExpressions ? 2 : 0) | (CompiledBindings ? 4 : 0);
        }

        // This project does not use analyzer release tracking (neither do LWXSG0001/0002 above).
#pragma warning disable RS2008
        private static readonly DiagnosticDescriptor s_simplerXamlOff = new DiagnosticDescriptor(
            "LWXSG0003",
            "Simpler XAML is switched off",
            "{0}: the root element declares no xmlns, which needs Simpler XAML (LibreWpfSimplerXaml). Declare the namespaces, or leave LibreWpfSimplerXaml on (it follows UseLibreWpfXamlSourceGenerator).",
            "LibreWPF.Xaml",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_csharpExpressionsOff = new DiagnosticDescriptor(
            "LWXSG0004",
            "C# expressions are switched off",
            "{0}({1},{2}): '{3}' is a C# expression, which needs LibreWpfCSharpExpressions (it follows UseLibreWpfXamlSourceGenerator)",
            "LibreWPF.Xaml",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_asyncLambda = new DiagnosticDescriptor(
            "LWXSG0011",
            "Async lambda event handlers are not supported",
            "{0}({1},{2}): '{3}' is an async lambda; an event expression cannot be async. Use a regular method as the handler.",
            "LibreWPF.Xaml",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_memberOnPageAndDataContext = new DiagnosticDescriptor(
            "LWXSG0012",
            "A name exists on both the page and the DataContext",
            "{0}({1},{2}): '{3}' uses the name '{4}', a member of both the page ({5}) and the DataContext (x:DataType); write {{this.{4}}} for the page or {{.{4}}} for the DataContext",
            "LibreWPF.Xaml",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        private static readonly DiagnosticDescriptor s_leadingDotNotBinding = new DiagnosticDescriptor(
            "LWXSG0013",
            "A leading dot is the DataContext and must be a binding",
            "{0}({1},{2}): '{3}' starts with '.', the DataContext, but is not a simple path on a bindable property or a compound expression over x:DataType names",
            "LibreWPF.Xaml",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
#pragma warning restore RS2008

        private static void Generate(SourceProductionContext spc, AdditionalText file, XsgFeatures features, string resourcePath)
        {
            var text = file.GetText(spc.CancellationToken);
            if (text is null)
            {
                return;
            }

            s_context!.ResourcePath = resourcePath;

            var source = text.ToString();
            var withNamespaces = InjectImplicitXmlns(source);
            if (!string.Equals(withNamespaces, source, StringComparison.Ordinal) && !features.SimplerXaml)
            {
                spc.ReportDiagnostic(Diagnostic.Create(s_simplerXamlOff, Location.None, file.Path));
                return;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Parse(features.SimplerXaml ? withNamespaces : source, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }
            catch (System.Xml.XmlException ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "LWXSG0002",
                        "XAML is not well-formed",
                        "{0}: {1}",
                        "LibreWPF.Xaml",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None,
                    file.Path,
                    ex.Message));
                return;
            }

            RewriteStaticResourcesOfDynamicEntries(doc);

            var root = doc.Root;
            if (root is null)
            {
                return;
            }

            if (ReportExpressionIssues(spc, file.Path, doc, features.CSharpExpressions))
            {
                return;
            }

            var classAttr = root.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"));
            if (classAttr is null)
            {
                // also try without namespace (implicit)
                classAttr = root.Attributes().FirstOrDefault(a => a.Name.LocalName == "Class" && a.Name.NamespaceName.Contains("xaml"));
            }
            if (classAttr is null)
            {
                // try any attribute named Class
                classAttr = root.Attributes().FirstOrDefault(a => a.Name.LocalName == "Class");
            }
            if (classAttr is null)
            {
                // No x:Class: a loose ResourceDictionary (themes/generic.xaml, ...) becomes a generated, registered dictionary
                // (the XSG path never produces BAML, so this is how its themes load); anything else has no generated class.
                if (root.Name.LocalName == "ResourceDictionary")
                {
                    GenerateLooseResourceDictionary(spc, root, resourcePath, file.Path, features.ResourceAssemblyName);
                }
                return;
            }

            var fullClassName = classAttr.Value;
            var lastDot = fullClassName.LastIndexOf('.');
            var ns = lastDot >= 0 ? fullClassName.Substring(0, lastDot) : string.Empty;
            var className = lastDot >= 0 ? fullClassName.Substring(lastDot + 1) : fullClassName;
            s_context!.Within = s_context.Compilation.GetTypeByMetadataName(fullClassName);

            // x:Code: collect inline C# (MAUI-style) before generating the class.
            var xCodeUsings = new List<string>();
            var xCodeMembers = new StringBuilder();
            var xCodeElements = root.Elements().Where(e =>
                e.Name.LocalName == "Code" && e.Name.NamespaceName.Contains("xaml")).ToList();
            foreach (var xc in xCodeElements)
            {
                var content = xc.Value;
                foreach (var node in xc.Nodes())
                {
                    if (node is XCData cdata)
                    {
                        content = cdata.Value;
                        break;
                    }
                }
                foreach (var line in content.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("using ", StringComparison.Ordinal) && trimmed.EndsWith(";"))
                    {
                        if (!xCodeUsings.Contains(trimmed))
                        {
                            xCodeUsings.Add(trimmed);
                        }
                    }
                    else
                    {
                        xCodeMembers.AppendLine(line);
                    }
                }
            }

            // Determine default namespace URI
            var defaultNs = root.GetDefaultNamespace().NamespaceName;
            if (string.IsNullOrEmpty(defaultNs))
            {
                defaultNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            }

            // Collect x:Name values (with their element type for typed fields).
            var nameTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var el in root.DescendantsAndSelf())
            {
                var na = el.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name" && a.Name.NamespaceName.Contains("xaml"));
                if (na is null)
                {
                    na = el.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name");
                }
                if (na is null)
                {
                    continue;
                }
                if (nameTypes.ContainsKey(na.Value))
                {
                    continue;
                }
                var t = ResolveTypeName(el, defaultNs) ?? "System.Windows.FrameworkElement";
                nameTypes[na.Value] = t;
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated by LibreWPF.Xaml.SourceGenerator />");
            sb.AppendLine("#nullable enable");
            foreach (var u in xCodeUsings)
            {
                sb.AppendLine(u);
            }
            var globalClassName = string.IsNullOrEmpty(ns) ? $"global::{className}" : $"global::{ns}.{className}";
            sb.AppendLine("#if DEBUG");
            sb.AppendLine($"[assembly: global::System.Reflection.Metadata.MetadataUpdateHandler(typeof({globalClassName}.__XsgHotReload))]");
            sb.AppendLine("#endif");
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
            }

            sb.AppendLine($"    public partial class {className}");
            sb.AppendLine("    {");
            foreach (var kvp in nameTypes)
            {
                sb.AppendLine($"        private global::{kvp.Value}? {kvp.Key};");
            }
            sb.AppendLine();
            AppendHotReloadHandler(sb, className);
            sb.AppendLine();
            sb.AppendLine("        private void InitializeComponent()");
            sb.AppendLine("        {");
            sb.AppendLine("#if DEBUG");
            sb.AppendLine("            __XsgHotReload.Register(this);");
            sb.AppendLine("#endif");
            sb.AppendLine("            // Begin generated object graph");

            var sourcePath = file.Path;

            // Generate root: the root element IS 'this' (Window/Page/UserControl).
            // Assign its properties directly and build children into this.
            // Local names are emitted into the generated C# and therefore form part of the EnC
            // baseline. They must be deterministic: a GUID here makes an unchanged XAML file
            // look like a complete rewrite to Roslyn on every build.
            var localId = 0;
            // A ResourceDictionary root with code-behind: like a loose one it has scopes, FindResource and lazy entries.
            var classDictionary = root.Name.LocalName == "ResourceDictionary" && s_context is not null;
            if (classDictionary)
            {
                s_context!.ClassDictionary = true;
            }
            var rootBody = new StringBuilder();
            GenerateRootElement(rootBody, root, defaultNs, sourcePath, 12, ref localId);
            // Generated element code can call the (non-existent) static FrameworkElement.FindResource; in a dictionary it means this dictionary.
            sb.Append(classDictionary ? rootBody.ToString().Replace("global::System.Windows.FrameworkElement.FindResource(", "this.FindResource(") : rootBody.ToString());

            sb.AppendLine("            // End generated object graph");
            sb.AppendLine("        }");
            if (classDictionary)
            {
                sb.AppendLine();
                AppendDictionaryFindResource(sb);
                if (s_context!.LazyMethods.Length > 0)
                {
                    sb.AppendLine();
                    sb.Append(s_context.LazyMethods.ToString().Replace("global::System.Windows.FrameworkElement.FindResource(", "this.FindResource("));
                }
            }

            // x:Code: inline C# members (MAUI-style) — emit members only (usings hoisted above).
            if (xCodeMembers.Length > 0)
            {
                sb.AppendLine(xCodeMembers.ToString());
            }

            if (s_context!.ExpressionMembers.Length > 0)
            {
                sb.AppendLine();
                sb.Append(s_context.ExpressionMembers.ToString());
            }

            AppendCompiledPropertyHelperIfUsed(sb);
            AppendDictionaryLoaderIfUsed(sb);

            sb.AppendLine("    }");

            // The root object of this file is registered under its resource path, so Application.StartupUri / LoadComponent(Uri) create it
            // without a BAML resource. Only when the code-behind class exists with a parameterless constructor, and not for the Application.
            if (s_context!.Within is { IsAbstract: false } component
                && ResolveTypeName(root, defaultNs) != "System.Windows.Application"
                && component.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal))
            {
                var registrar = "__XsgComponent_" + fullClassName.Replace('.', '_');
                sb.AppendLine("    #pragma warning disable CA2255 // module initializer in a library: it only registers a factory");
                sb.AppendLine($"    internal static class {registrar}");
                sb.AppendLine("    {");
                sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
                sb.AppendLine("        internal static void Register()");
                sb.AppendLine("        {");
                sb.AppendLine($"            global::System.Windows.Markup.XamlSourceGeneratedResources.RegisterComponent(\"{EscapeString(s_context.Compilation.AssemblyName ?? string.Empty)}\", \"{EscapeString(resourcePath)}\", static () => new {globalClassName}());");
                if (classDictionary)
                {
                    // A dictionary with code-behind is also a dictionary: ResourceDictionary.Source and the merged-dictionary loaders find it by path.
                    sb.AppendLine($"            global::System.Windows.Markup.XamlSourceGeneratedResources.RegisterDictionary(\"{EscapeString(s_context.Compilation.AssemblyName ?? string.Empty)}\", \"{EscapeString(resourcePath)}\", static () => new {globalClassName}());");
                }
                sb.AppendLine("        }");
                sb.AppendLine("    }");
                sb.AppendLine("    #pragma warning restore CA2255");
            }

            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine("}");
            }

            var hintName = fullClassName.Replace('.', '_') + ".g.cs";
            spc.AddSource(hintName, SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// Debug-only Route D delivery: after the runtime applies a C# metadata delta that touched this
        /// class, re-run its (idempotent) InitializeComponent on every live instance, on that
        /// instance's own dispatcher. Instances are held weakly and registered by InitializeComponent
        /// itself, so no tree walk is needed and off-tree instances are covered.
        /// </summary>
        private static void AppendHotReloadHandler(StringBuilder sb, string className)
        {
            sb.AppendLine("#if DEBUG");
            sb.AppendLine("        internal static class __XsgHotReload");
            sb.AppendLine("        {");
            sb.AppendLine("            private static readonly object s_lock = new object();");
            sb.AppendLine($"            private static readonly global::System.Collections.Generic.List<global::System.WeakReference<{className}>> s_instances = new global::System.Collections.Generic.List<global::System.WeakReference<{className}>>();");
            sb.AppendLine();
            sb.AppendLine($"            internal static void Register({className} instance)");
            sb.AppendLine("            {");
            sb.AppendLine("                lock (s_lock)");
            sb.AppendLine("                {");
            sb.AppendLine("                    for (var i = s_instances.Count - 1; i >= 0; i--)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (!s_instances[i].TryGetTarget(out var live))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            s_instances.RemoveAt(i);");
            sb.AppendLine("                        }");
            sb.AppendLine("                        else if (ReferenceEquals(live, instance))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            return;");
            sb.AppendLine("                        }");
            sb.AppendLine("                    }");
            sb.AppendLine($"                    s_instances.Add(new global::System.WeakReference<{className}>(instance));");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            internal static void ClearCache(global::System.Type[]? updatedTypes)");
            sb.AppendLine("            {");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            internal static void UpdateApplication(global::System.Type[]? updatedTypes)");
            sb.AppendLine("            {");
            sb.AppendLine($"                if (updatedTypes != null && global::System.Array.IndexOf(updatedTypes, typeof({className})) < 0)");
            sb.AppendLine("                {");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine($"                var targets = new global::System.Collections.Generic.List<{className}>();");
            sb.AppendLine("                lock (s_lock)");
            sb.AppendLine("                {");
            sb.AppendLine("                    for (var i = s_instances.Count - 1; i >= 0; i--)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (s_instances[i].TryGetTarget(out var live))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            targets.Add(live);");
            sb.AppendLine("                        }");
            sb.AppendLine("                        else");
            sb.AppendLine("                        {");
            sb.AppendLine("                            s_instances.RemoveAt(i);");
            sb.AppendLine("                        }");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("                foreach (var target in targets)");
            sb.AppendLine("                {");
            sb.AppendLine("                    var instance = target;");
            sb.AppendLine("                    instance.Dispatcher.BeginInvoke(global::System.Windows.Threading.DispatcherPriority.Normal, (global::System.Action)(() => instance.InitializeComponent()));");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("#endif");
        }

        /// <summary>
        /// Generate code for an element. Returns the variable name holding the element.
        /// </summary>
        private static string GenerateElement(StringBuilder sb, XElement el, string defaultNs, string sourcePath, int indent, ref int localId)
        {
            var pad = new string(' ', indent);
            var lineInfo = el as IXmlLineInfo;
            if (lineInfo != null && lineInfo.HasLineInfo())
            {
                var escapedPath = sourcePath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.AppendLine($"#line {lineInfo.LineNumber} \"{escapedPath}\"");
            }
            // <DynamicResource x:Key="K" ResourceKey="{x:Static SystemColors.XKey}"/> as an entry of a dictionary: a reference that is resolved when something asks for K.
            if (el.Name.LocalName == "DynamicResource" && (el.Name.NamespaceName == PresentationNamespace || el.Name.NamespaceName.Length == 0)
                && el.Attribute("ResourceKey") is { } dynamicKey && !el.Elements().Any())
            {
                var entryVar = $"__DynamicResource_{++localId}";
                sb.AppendLine($"{pad}var {entryVar} = global::System.Windows.Markup.XamlSourceGeneratedResources.DynamicResourceEntry({ResourceKeyCode(dynamicKey.Value, el, defaultNs)});");
                return entryVar;
            }

            var typeName = ResolveTypeName(el, defaultNs);
            if (typeName is null)
            {
                sb.AppendLine($"{pad}// Unknown type: {el.Name.LocalName}");
                if (s_context is { } unknownContext)
                {
                    Report(unknownContext, s_notGenerated, el, $"the element <{el.Name.LocalName}>, whose type is unknown (it became null)");
                }
                return "null!";
            }

            // <ResourceDictionary Source="Styles/Brushes.xaml"/>: a dictionary file compiled into an assembly, found by its resource path.
            if (typeName == "System.Windows.ResourceDictionary" && el.Attribute("Source") is { } dictionarySource && !el.Elements().Any())
            {
                var dictionaryVar = $"__ResourceDictionary_{++localId}";
                sb.AppendLine($"{pad}var {dictionaryVar} = {DictionaryFromSource(dictionarySource.Value)};");
                return dictionaryVar;
            }

            // <Binding Path="..." RelativeSource="{...}" /> (inside a MultiBinding): the same code as the {Binding ...} markup.
            if (typeName == "System.Windows.Data.Binding")
            {
                var bindingVar = $"__Binding_{++localId}";
                sb.AppendLine($"{pad}var {bindingVar} = {BindingCode(ElementAsBindingMarkup(el, rulesAreGenerated: true), el, false)};");
                // <Binding.ValidationRules><local:Rule/></Binding.ValidationRules>: the rules are objects of their own.
                foreach (var propertyElement in el.Elements().Where(c => c.Name.LocalName.StartsWith("Binding.", StringComparison.Ordinal)))
                {
                    if (propertyElement.Name.LocalName == "Binding.ValidationRules")
                    {
                        foreach (var rule in propertyElement.Elements())
                        {
                            var ruleVar = GenerateElement(sb, rule, defaultNs, sourcePath, indent, ref localId);
                            sb.AppendLine($"{pad}{bindingVar}.ValidationRules.Add({ruleVar});");
                        }
                    }
                }
                return bindingVar;
            }

            // <Thickness>1.5</Thickness>, <Color>#FF0000</Color>: a value written as element text, converted at build time.
            if (TextOnlyLiteral(el, typeName, defaultNs) is { } literal)
            {
                var literalVar = $"__{el.Name.LocalName}_{++localId}";
                sb.AppendLine($"{pad}var {literalVar} = {literal};");
                return literalVar;
            }

            // DataTemplate / ControlTemplate: build a FrameworkElementFactory tree.
            if (typeName.Contains("Template"))
            {
                return GenerateFactoryTemplate(sb, el, typeName, defaultNs, sourcePath, indent, ref localId);
            }

            if (typeName == "System.Windows.Style")
            {
                return GenerateStyle(sb, el, defaultNs, sourcePath, indent, ref localId);
            }

            var varName = $"__{el.Name.LocalName}_{++localId}";
            sb.AppendLine($"{pad}var {varName} = new global::{typeName}();");

            // Properties from attributes (excluding xmlns, x:Class, x:Name)
            foreach (var attr in el.Attributes())
            {
                var attrNs = attr.Name.NamespaceName;
                var attrLocal = attr.Name.LocalName;

                // Skip namespace declarations (xmlns, xmlns:x, ...).
                if (attrNs == "http://www.w3.org/2000/xmlns/" || attrLocal == "xmlns" || IsCompiledBindingDirective(attr))
                {
                    continue;
                }

                if (attrNs.Contains("xaml") && (attrLocal == "Class" || attrLocal == "Name" || attrLocal == "Key"))
                {
                    continue;
                }
                // x:Shared="false" (a new instance per lookup) is not expressible with an eager ResourceDictionary entry: the resource is shared (a lazily created entry does honour it: see LazyEntry). Say so, and never write it as a property.
                if (attrNs == XamlNamespace && attrLocal == "Shared")
                {
                    if (string.Equals(attr.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase) && !ReferenceEquals(el, s_lazyEntry) && s_context is { } sharedContext)
                    {
                        Report(sharedContext, s_notShared, attr);
                    }
                    continue;
                }
                // The other XAML-language attributes (x:Uid, x:FieldModifier, x:Subclass, ...) are not properties either.
                if (attrNs == XamlNamespace)
                {
                    continue;
                }
                if (attr.Name.LocalName.StartsWith("xmlns"))
                {
                    continue;
                }

                // x:Name already collected; skip setting as property
                if (attrNs.Contains("xaml") && attrLocal == "Name")
                {
                    continue;
                }

                // Event lambda: {(s, e) => ...}. A lambda value means an event, whatever its name.
                if (!attrLocal.Contains(".") && XamlExpressions.TryGetExpression(attr.Value, out var eventExpression) && XamlExpressions.IsLambda(eventExpression))
                {
                    AppendEventLambda(sb, pad, varName, typeName, attrLocal, XamlExpressions.ToCSharp(eventExpression));
                    continue;
                }

                // Event wiring (idempotent: -= then +=).
                if (s_knownEvents.Contains(attrLocal) && !attr.Value.StartsWith("{", StringComparison.Ordinal))
                {
                    sb.AppendLine($"{pad}{varName}.{attrLocal} -= this.{attr.Value};");
                    sb.AppendLine($"{pad}{varName}.{attrLocal} += this.{attr.Value};");
                    continue;
                }

                var value = attr.Value;

                // DynamicResource on a (potential) DependencyProperty: use SetResourceReference
                // so the value tracks resource changes (Hot Reload-friendly).
                // (Skip attached properties — handled below.)
                if (!attr.Name.LocalName.Contains(".") &&
                    value.TrimStart().StartsWith("{DynamicResource", StringComparison.Ordinal))
                {
                    var key = value.Substring(value.IndexOf(' ') + 1).Trim().TrimEnd('}').Trim();
                    // Only FrameworkElement / FrameworkContentElement have SetResourceReference; a Freezable (a brush's Color) takes the expression itself.
                    var keyCode = ResourceKeyCode(key, el, defaultNs);
                    if (HasSetResourceReference(typeName))
                    {
                        sb.AppendLine($"{pad}{varName}.SetResourceReference(global::{typeName}.{attrLocal}Property, {keyCode});");
                    }
                    else
                    {
                        sb.AppendLine($"{pad}global::System.Windows.Markup.XamlSourceGeneratedResources.SetDynamicResource({varName}, global::{typeName}.{attrLocal}Property, {keyCode});");
                    }
                    continue;
                }

                // {TemplateBinding P} on an element that is not a factory node (inside an initializer): a binding to the templated parent.
                if (!attr.Name.LocalName.Contains(".") && value.TrimStart().StartsWith("{TemplateBinding", StringComparison.Ordinal))
                {
                    var targetProperty = ResolveDependencyProperty(typeName, attrLocal, el, defaultNs).Code;
                    sb.AppendLine($"{pad}global::System.Windows.Data.BindingOperations.SetBinding({varName}, {targetProperty}, {TemplateBindingAsBinding(value, el, defaultNs)});");
                    continue;
                }

                // {StaticResource K} where K is defined in a .Resources of an enclosing element: it exists on the instance only, so a
                // dependency property takes it as a resource reference resolved through the tree.
                if (!attr.Name.LocalName.Contains(".") && value.TrimStart().StartsWith("{StaticResource", StringComparison.Ordinal)
                    && IsLocalResourceKey(value.Trim().Substring("{StaticResource".Length).TrimEnd('}').Trim(), el)
                    && FindDependencyPropertyField(TypeSymbol(typeName), attrLocal) is { } localReference)
                {
                    var referenceKey = value.Trim().Substring("{StaticResource".Length).TrimEnd('}').Trim();
                    sb.AppendLine($"{pad}{varName}.SetResourceReference({localReference.ContainingType.ToDisplayString(s_typeFormat)}.{attrLocal}Property, {ResourceKeyCode(referenceKey, el, defaultNs)});");
                    continue;
                }

                // The property's CLR type is known from the compilation: convert the value to it at build time.
                if (!attr.Name.LocalName.Contains(".") && TypedPropertyValue(value, typeName, attrLocal, el, defaultNs) is { } typedEarly)
                {
                    sb.AppendLine($"{pad}{varName}.{attrLocal} = {typedEarly};");
                    continue;
                }

                // StaticResource: cast FindResource to the property's type.
                // (Skip attached properties — handled below.)
                if (!attr.Name.LocalName.Contains(".") &&
                    value.TrimStart().StartsWith("{StaticResource", StringComparison.Ordinal))
                {
                    var key = value.Substring(value.IndexOf(' ') + 1).Trim().TrimEnd('}').Trim();
                    var castType = attrLocal switch
                    {
                        "Style" or "ItemContainerStyle" or "ItemContainerStyleSelector" => "global::System.Windows.Style",
                        "ContentTemplate" or "ItemTemplate" or "HeaderTemplate" or "FooterTemplate" or "CellTemplate" or "ItemTemplateSelector" => "global::System.Windows.DataTemplate",
                        "Template" => "global::System.Windows.Controls.ControlTemplate",
                        "Background" or "Foreground" or "BorderBrush" or "Fill" or "Stroke" => "global::System.Windows.Media.Brush",
                        "FontFamily" => "global::System.Windows.Media.FontFamily",
                        "FontSize" => "double",
                        "FontWeight" => "global::System.Windows.FontWeight",
                        "Visibility" => "global::System.Windows.Visibility",
                        _ => null,
                    };
                    // Any other property: the cast is the property's own type when the compilation knows it (MultiBinding.Converter, ...).
                    if (castType is null && FindClrProperty(TypeSymbol(typeName), attrLocal) is { } clrProperty
                        && clrProperty.Type.SpecialType != SpecialType.System_Object && IsNameable(clrProperty.Type))
                    {
                        castType = clrProperty.Type.ToDisplayString(s_typeFormat);
                    }
                    if (castType is not null)
                    {
                        sb.AppendLine($"{pad}{varName}.{attrLocal} = ({castType})this.FindResource(\"{key}\");");
                    }
                    else
                    {
                        sb.AppendLine($"{pad}{varName}.{attrLocal} = this.FindResource(\"{key}\");");
                    }
                    continue;
                }

                // Source property: BitmapImage for Image, Uri for others.
                // (A markup extension is not a path: {Binding ...} / {x:Static ...} on CollectionViewSource.Source, ... go the general way.)
                if (attrLocal == "Source" && !attr.Name.LocalName.Contains(".") && !value.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    if (typeName.Contains("Image") && !typeName.Contains("ImageSource"))
                    {
                        sb.AppendLine($"{pad}{varName}.Source = new global::System.Windows.Media.Imaging.BitmapImage(new global::System.Uri(\"{EscapeString(value)}\", global::System.UriKind.Relative));");
                    }
                    else
                    {
                        sb.AppendLine($"{pad}{varName}.Source = new global::System.Uri(\"{EscapeString(value)}\", global::System.UriKind.Relative);");
                    }
                    continue;
                }

                var propCode = ConvertAttributeValue(value, attrNs, attrLocal, typeName, el);
                if (propCode is null)
                {
                    continue;
                }

                // attached property? (Owner.Property)
                if (attr.Name.LocalName.Contains("."))
                {
                    var parts = attr.Name.LocalName.Split('.');
                    // theme:ScrollChrome.ScrollGlyph="...": the owner is named through the attribute's own namespace, not the default one.
                    var attributePrefix = attr.Name.NamespaceName.Length > 0 && attr.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal)
                        ? el.GetPrefixOfNamespace(attr.Name.Namespace) : null;
                    var ownerType = (attributePrefix is { Length: > 0 } ? ResolveTypeNameInScope(attributePrefix + ":" + parts[0], el, defaultNs) : null)
                        ?? ResolveTypeNameFromName(parts[0], defaultNs) ?? parts[0];
                    var propName = parts[1];
                    if (value.TrimStart().StartsWith("{DynamicResource", StringComparison.Ordinal))
                    {
                        var key = value.Substring(value.IndexOf(' ') + 1).Trim().TrimEnd('}').Trim();
                        sb.AppendLine($"{pad}{varName}.SetValue(global::{ownerType}.{propName}Property, {varName}.FindResource(\"{key}\"));");
                    }
                    else if (value.TrimStart().StartsWith("{StaticResource", StringComparison.Ordinal))
                    {
                        var key = value.Substring(value.IndexOf(' ') + 1).Trim().TrimEnd('}').Trim();
                        sb.AppendLine($"{pad}global::{ownerType}.Set{propName}({varName}, this.FindResource(\"{key}\"));");
                    }
                    else if (value.TrimStart().StartsWith("{Binding", StringComparison.Ordinal))
                    {
                        var bindCode = ConvertAttributeValue(value, attrNs, propName, typeName, el);
                        sb.AppendLine($"{pad}{varName}.SetBinding(global::{ownerType}.{propName}Property, {bindCode});");
                    }
                    else
                    {
                        // The attached property's value type is the second parameter of its static Set method.
                        var attachedSetter = TypeSymbol(ownerType)?.GetMembers("Set" + propName).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 2);
                        var attachedCode = attachedSetter is not null
                            ? ValueCode(value, attachedSetter.Parameters[1].Type, el, defaultNs, null)
                            : ConvertAttributeValue(value, attrNs, propName, typeName);
                        if (attachedCode is not null)
                        {
                            sb.AppendLine($"{pad}global::{ownerType}.Set{propName}({varName}, {attachedCode});");
                        }
                    }
                    continue;
                }

                // Binding: use SetBinding for DependencyProperties, direct assignment for CLR properties.
                if (propCode.Contains("global::System.Windows.Data.Binding") || propCode.Contains("new Binding"))
                {
                    if (attrLocal == "Binding" || attrLocal.EndsWith("Binding", StringComparison.Ordinal))
                    {
                        sb.AppendLine($"{pad}{varName}.{attrLocal} = {propCode};");
                    }
                    else if (IsFrameworkElementOrContentElement(typeName))
                    {
                        sb.AppendLine($"{pad}{varName}.SetBinding(global::{typeName}.{attrLocal}Property, {propCode});");
                    }
                    else
                    {
                        // Any other DependencyObject (CollectionViewSource, a Freezable, ...) has no SetBinding of its own.
                        sb.AppendLine($"{pad}global::System.Windows.Data.BindingOperations.SetBinding({varName}, global::{typeName}.{attrLocal}Property, {propCode});");
                    }
                }
                else
                {
                    sb.AppendLine($"{pad}{varName}.{attrLocal} = {propCode};");
                }
            }

            // x:Name assignment
            var nameAttr = el.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name" && a.Name.NamespaceName.Contains("xaml"));
            if (nameAttr is null)
            {
                nameAttr = el.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name");
            }
            // A loose ResourceDictionary has no fields: a plain Name (VisualState.Name, ...) is only the property assigned above.
            if (nameAttr != null && s_context is not { LooseDictionary: true })
            {
                sb.AppendLine($"{pad}this.{nameAttr.Value} = {varName};");
                // As BAML does: x:Name is also the element's Name and an entry in the root's
                // NameScope, so FindName, ElementName bindings and tooling lookups by name work.
                // Unregister first: Hot Reload re-runs InitializeComponent on the live instance.
                var nameLiteral = EscapeString(nameAttr.Value);
                sb.AppendLine($"{pad}if ((object){varName} is global::System.Windows.FrameworkElement __fe_{varName}) __fe_{varName}.Name = \"{nameLiteral}\";");
                sb.AppendLine($"{pad}else if ((object){varName} is global::System.Windows.FrameworkContentElement __fce_{varName}) __fce_{varName}.Name = \"{nameLiteral}\";");
                // VisualState, VisualStateGroup and the like are neither: their Name is a plain property (the RuntimeNameProperty), which
                // VisualStateManager.GoToState searches by.
                if (!IsFrameworkElementOrContentElement(typeName) && FindClrProperty(TypeSymbol(typeName), "Name") is { Type.SpecialType: SpecialType.System_String, SetMethod.DeclaredAccessibility: Accessibility.Public })
                {
                    sb.AppendLine($"{pad}{varName}.Name = \"{nameLiteral}\";");
                }
                sb.AppendLine($"{pad}if ((object)this is global::System.Windows.DependencyObject __scopeRoot_{varName})");
                sb.AppendLine($"{pad}{{");
                sb.AppendLine($"{pad}    var __scope_{varName} = global::System.Windows.NameScope.GetNameScope(__scopeRoot_{varName});");
                sb.AppendLine($"{pad}    if (__scope_{varName} is null) global::System.Windows.NameScope.SetNameScope(__scopeRoot_{varName}, __scope_{varName} = new global::System.Windows.NameScope());");
                sb.AppendLine($"{pad}    if (__scope_{varName}.FindName(\"{nameLiteral}\") is not null) __scope_{varName}.UnregisterName(\"{nameLiteral}\");");
                sb.AppendLine($"{pad}    __scope_{varName}.RegisterName(\"{nameLiteral}\", {varName});");
                sb.AppendLine($"{pad}}}");
            }

            // Content / children (exclude x:Code, handled separately)
            var contentElements = el.Elements().Where(e =>
                !(e.Name.LocalName == "Code" && e.Name.NamespaceName.Contains("xaml"))).ToList();
            if (contentElements.Count > 0)
            {
                // Property elements: <Foo.Bar>...</Foo.Bar> => this.Bar = ...
                var propertyElements = contentElements.Where(c => c.Name.LocalName.Contains(".")).ToList();
                var childElements = contentElements.Where(c => !c.Name.LocalName.Contains(".")).ToList();

                foreach (var pe in propertyElements)
                {
                    var propName = pe.Name.LocalName.Split('.').Last();
                    var peChildren = pe.Elements().ToList();
                    if (TryEmitPropertyElement(sb, varName, typeName, pe, defaultNs, sourcePath, indent, ref localId))
                    {
                        continue;
                    }
                    if (propName == "MergedDictionaries")
                    {
                        foreach (var md in peChildren)
                        {
                            var mdVar = GenerateElement(sb, md, defaultNs, sourcePath, indent, ref localId);
                            sb.AppendLine($"{pad}{varName}.MergedDictionaries.Add({mdVar});");
                        }
                    }
                    else if (IsCollectionProperty(propName) && peChildren.Count > 0)
                    {
                        sb.AppendLine($"{pad}{varName}.{propName}.Clear();");
                        foreach (var c in peChildren)
                        {
                            var cVar = GenerateElement(sb, c, defaultNs, sourcePath, indent, ref localId);
                            sb.AppendLine($"{pad}{varName}.{propName}.Add({cVar});");
                        }
                    }
                    else if (peChildren.Count == 1)
                    {
                        var cVar = GenerateElement(sb, peChildren[0], defaultNs, sourcePath, indent, ref localId);
                        // A binding object (<Foo.X><MultiBinding/></Foo.X>) is applied to the dependency property, not assigned to the CLR one.
                        if (peChildren[0].Name.LocalName is "Binding" or "MultiBinding" or "PriorityBinding"
                            && FindDependencyPropertyField(TypeSymbol(typeName), propName) is { } bindingTarget)
                        {
                            sb.AppendLine($"{pad}global::System.Windows.Data.BindingOperations.SetBinding({varName}, {bindingTarget.ContainingType.ToDisplayString(s_typeFormat)}.{propName}Property, {cVar});");
                        }
                        else
                        {
                            sb.AppendLine($"{pad}{varName}.{propName} = {cVar};");
                        }
                    }
                    else if (peChildren.Count > 1)
                    {
                        // Implicit ResourceDictionary
                        var rdVar = $"__rd_{++localId}";
                        sb.AppendLine($"{pad}var {rdVar} = new global::System.Windows.ResourceDictionary();");
                        foreach (var c in peChildren)
                        {
                            var cVar = GenerateElement(sb, c, defaultNs, sourcePath, indent, ref localId);
                            EmitKeyedResource(sb, rdVar, c, cVar, defaultNs, pad);
                        }
                        sb.AppendLine($"{pad}{varName}.{propName} = {rdVar};");
                    }
                }

                // ResourceDictionary: children are keyed resources.
                if (typeName.Contains("ResourceDictionary"))
                {
                    foreach (var child in childElements)
                    {
                        var cVar = GenerateElement(sb, child, defaultNs, sourcePath, indent, ref localId);
                        EmitKeyedResource(sb, varName, child, cVar, defaultNs, pad);
                    }
                }
                else
                {
                    // Determine content property: Children for panels, Content for ContentControl, Items for ItemsControl
                    var contentProp = InferContentProperty(typeName);
                    if (childElements.Count > 0 && TryEmitSymbolContent(sb, varName, typeName, childElements, defaultNs, sourcePath, indent, ref localId))
                    {
                        // generated from the type's [ContentProperty] / collection shape
                    }
                    else if (childElements.Count > 0)
                    {
                        if (contentProp == "Children")
                        {
                            sb.AppendLine($"{pad}{varName}.Children.Clear();");
                            foreach (var child in childElements)
                            {
                                var childVar = GenerateElement(sb, child, defaultNs, sourcePath, indent, ref localId);
                                sb.AppendLine($"{pad}{varName}.Children.Add({childVar});");
                            }
                        }
                        else if (contentProp == "Content")
                        {
                            if (childElements.Count == 1)
                            {
                                var childVar = GenerateElement(sb, childElements[0], defaultNs, sourcePath, indent, ref localId);
                                sb.AppendLine($"{pad}{varName}.Content = {childVar};");
                            }
                        }
                        else if (contentProp == "Items")
                        {
                            sb.AppendLine($"{pad}{varName}.Items.Clear();");
                            foreach (var child in childElements)
                            {
                                var childVar = GenerateElement(sb, child, defaultNs, sourcePath, indent, ref localId);
                                sb.AppendLine($"{pad}{varName}.Items.Add({childVar});");
                            }
                        }
                    }
                }
            }

            // Text content
            var textContent = el.Value?.Trim();
            if (!string.IsNullOrEmpty(textContent) && contentElements.Count == 0)
            {
                var cp = InferContentProperty(typeName);
                if (cp == "Text")
                {
                    sb.AppendLine($"{pad}{varName}.Text = \"{EscapeString(textContent)}\";");
                }
                else
                {
                    sb.AppendLine($"{pad}{varName}.Content = \"{EscapeString(textContent)}\";");
                }
            }

            return varName;
        }

        private static bool IsFrameworkElementOrContentElement(string typeName)
        {
            for (var t = TypeSymbol(typeName); t != null; t = t.BaseType)
            {
                if (t.ToDisplayString() is "System.Windows.FrameworkElement" or "System.Windows.FrameworkContentElement") return true;
            }
            return false;
        }

        private static bool HasSetResourceReference(string typeName) => IsFrameworkElementOrContentElement(typeName) || TypeSymbol(typeName) == null; // unknown type: the previous behaviour

        /// <summary>
        /// <c>&lt;DynamicResource x:Key="A" ResourceKey="R"/&gt;</c> is an alias: BAML stores a dynamic reference under A, so a property set from
        /// <c>{StaticResource A}</c> holds that reference (it follows R as it changes, for example the system accent colors of the Fluent theme).
        /// Within one file, such a use is written as <c>{DynamicResource R}</c>, which the generator already turns into the same reference.
        /// </summary>
        private static void RewriteStaticResourcesOfDynamicEntries(XDocument doc)
        {
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in doc.Descendants().Where(e => e.Name.LocalName == "DynamicResource" && !e.Elements().Any()))
            {
                var key = entry.Attribute(XName.Get("Key", XamlNamespace))?.Value ?? entry.Attribute("Key")?.Value;
                var target = entry.Attribute("ResourceKey")?.Value;
                if (key is not null && target is not null)
                {
                    aliases[key.Trim()] = target.Trim();
                }
            }

            if (aliases.Count == 0)
            {
                return;
            }

            foreach (var attribute in doc.Descendants().Attributes().Where(a => a.Value.Contains("StaticResource")).ToList())
            {
                var match = System.Text.RegularExpressions.Regex.Match(attribute.Value.Trim(), @"^\{\s*StaticResource\s+([^\s}]+)\s*\}$");
                if (match.Success && aliases.TryGetValue(match.Groups[1].Value, out var targetKey))
                {
                    attribute.Value = "{DynamicResource " + targetKey + "}";
                }
            }
        }

        private static string? ResolveTypeName(XElement el, string defaultNs)
        {
            var name = el.Name;
            string prefix = name.NamespaceName == defaultNs ? string.Empty : GetPrefix(el, name.NamespaceName);
            // For simplicity, use local name lookup in presentation types
            if (defaultNs == "http://schemas.microsoft.com/winfx/2006/xaml/presentation" ||
                name.NamespaceName == "http://schemas.microsoft.com/winfx/2006/xaml/presentation")
            {
                if (s_presentationTypes.TryGetValue(name.LocalName, out var full))
                {
                    return full;
                }
                if (ResolvePresentationTypeSemantically(name.LocalName) is { } semantic)
                {
                    return semantic;
                }
            }

            // clr-namespace:
            if (name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal))
            {
                var clrNs = name.NamespaceName.Substring("clr-namespace:".Length);
                // strip assembly
                var semi = clrNs.IndexOf(';');
                if (semi >= 0) clrNs = clrNs.Substring(0, semi);
                return $"{clrNs}.{name.LocalName}";
            }

            // using:
            if (name.NamespaceName.StartsWith("using:", StringComparison.Ordinal))
            {
                var clrNs = name.NamespaceName.Substring("using:".Length);
                return $"{clrNs}.{name.LocalName}";
            }

            return null;
        }

        private static string? ResolveTypeNameFromName(string name, string defaultNs)
        {
            if (s_presentationTypes.TryGetValue(name, out var full))
            {
                return full;
            }
            return ResolvePresentationTypeSemantically(name);
        }

        // Problems with C# expressions that stop the file: the feature switched off (every expression is an error, reported where it is
        // written) and an async lambda. Nothing is generated for the file.
        private static bool ReportExpressionIssues(SourceProductionContext spc, string path, XDocument doc, bool expressionsOn)
        {
            var found = false;
            foreach (var attribute in doc.Descendants().Attributes())
            {
                if (!XamlExpressions.TryGetExpression(attribute.Value, out var expression))
                {
                    continue;
                }

                var descriptor = !expressionsOn ? s_csharpExpressionsOff
                    : XamlExpressions.IsAsyncLambda(expression) ? s_asyncLambda
                    : null;
                if (descriptor is null)
                {
                    continue;
                }

                var info = (IXmlLineInfo)attribute;
                spc.ReportDiagnostic(Diagnostic.Create(
                    descriptor,
                    Location.None,
                    path,
                    info.HasLineInfo() ? info.LineNumber : 0,
                    info.HasLineInfo() ? info.LinePosition : 0,
                    attribute.Value.Trim()));
                found = true;
            }

            return found;
        }

        // Simpler XAML: a root start tag with no xmlns declaration gets the default
        // presentation and x: namespaces. This is a feature of the XSG path ONLY, applied to the XAML
        // text at build time. The BAML compiler (PresentationBuildTasks) and the run-time
        // System.Xaml.XamlXmlReader deliberately do not do it: a reader built over a caller's XmlReader
        // must keep that reader's own namespace context (see XamlXmlReaderNamespaceContextTests).
        private static string InjectImplicitXmlns(string text)
        {
            var i = 0;
            while (i < text.Length)
            {
                var lt = text.IndexOf('<', i);
                if (lt < 0 || lt + 1 >= text.Length)
                {
                    return text;
                }

                var next = text[lt + 1];
                if (next == '?' || next == '!')
                {
                    var close = string.CompareOrdinal(text, lt, "<!--", 0, 4) == 0 ? text.IndexOf("-->", lt, StringComparison.Ordinal) : text.IndexOf('>', lt);
                    if (close < 0)
                    {
                        return text;
                    }

                    i = close + 1;
                    continue;
                }

                var j = lt;
                char quote = '\0';
                while (j < text.Length && !(quote == '\0' && text[j] == '>'))
                {
                    if (quote == '\0' && (text[j] == '"' || text[j] == '\''))
                    {
                        quote = text[j];
                    }
                    else if (text[j] == quote)
                    {
                        quote = '\0';
                    }

                    j++;
                }

                if (j >= text.Length || text.IndexOf("xmlns", lt, j - lt, StringComparison.Ordinal) >= 0)
                {
                    return text;
                }

                var at = text[j - 1] == '/' ? j - 1 : j;
                return text.Substring(0, at)
                    + " xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\""
                    + text.Substring(at);
            }

            return text;
        }

        private static void GenerateRootElement(StringBuilder sb, XElement el, string defaultNs, string sourcePath, int indent, ref int localId)
        {
            var pad = new string(' ', indent);
            var lineInfo = el as IXmlLineInfo;
            if (lineInfo != null && lineInfo.HasLineInfo())
            {
                var escapedPath = sourcePath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.AppendLine($"#line {lineInfo.LineNumber} \"{escapedPath}\"");
            }
            var typeName = ResolveTypeName(el, defaultNs);

            // Properties on 'this'
            foreach (var attr in el.Attributes())
            {
                var attrNs = attr.Name.NamespaceName;
                var attrLocal = attr.Name.LocalName;
                if (attrNs == "http://www.w3.org/2000/xmlns/" || attrLocal == "xmlns" || IsCompiledBindingDirective(attr))
                {
                    continue;
                }
                if (attrNs.Contains("xaml") && (attrLocal == "Class" || attrLocal == "Name"))
                {
                    continue;
                }
                var value = attr.Value;

                if (value.TrimStart().StartsWith("{DynamicResource", StringComparison.Ordinal))
                {
                    var key = value.Substring(value.IndexOf(' ') + 1).Trim().TrimEnd('}').Trim();
                    sb.AppendLine($"{pad}this.SetResourceReference(global::{typeName ?? "System.Windows.FrameworkElement"}.{attrLocal}Property, \"{key}\");");
                    continue;
                }

                // The property's CLR type is known from the compilation: convert the value to it at build time (StartupUri, Title, ...).
                if (!attr.Name.LocalName.Contains(".") && TypedPropertyValue(value, typeName ?? "System.Windows.FrameworkElement", attrLocal, el, defaultNs) is { } typedRoot)
                {
                    sb.AppendLine($"{pad}this.{attrLocal} = {typedRoot};");
                    continue;
                }

                var propCode = ConvertAttributeValue(value, attrNs, attrLocal, typeName, el);
                if (propCode is null)
                {
                    continue;
                }

                if (propCode.Contains("global::System.Windows.Data.Binding") || propCode.Contains("new Binding"))
                {
                    sb.AppendLine($"{pad}this.SetBinding(global::{typeName ?? "System.Windows.FrameworkElement"}.{attrLocal}Property, {propCode});");
                }
                else
                {
                    sb.AppendLine($"{pad}this.{attrLocal} = {propCode};");
                }
            }

            // Content / children into this
            if (typeName is not null && typeName.Contains("ResourceDictionary"))
            {
                GenerateRootDictionaryContent(sb, el, defaultNs, sourcePath, indent, ref localId);
                return;
            }

            var contentElements = el.Elements().ToList();
            var contentProp = InferContentProperty(typeName ?? "");
            if (contentElements.Count > 0)
            {
                // Property elements: <Foo.Bar>...</Foo.Bar> => this.Bar = ...
                var propertyElements = contentElements.Where(c => c.Name.LocalName.Contains(".")).ToList();
                var childElements = contentElements.Where(c => !c.Name.LocalName.Contains(".")).ToList();

                foreach (var pe in propertyElements)
                {
                    var propName = pe.Name.LocalName.Split('.').Last();
                    var peChildren = pe.Elements().ToList();
                    if (peChildren.Count == 1 && peChildren[0].Name.LocalName == "ResourceDictionary")
                    {
                        var rdVar = GenerateElement(sb, peChildren[0], defaultNs, sourcePath, indent, ref localId);
                        sb.AppendLine($"{pad}this.{propName} = {rdVar};");
                    }
                    else if (peChildren.Count > 0)
                    {
                        // Implicit ResourceDictionary (e.g. Page.Resources containing loose resources).
                        var rdVar = $"__rd_{++localId}";
                        sb.AppendLine($"{pad}var {rdVar} = new global::System.Windows.ResourceDictionary();");
                        foreach (var c in peChildren)
                        {
                            var cVar = GenerateElement(sb, c, defaultNs, sourcePath, indent, ref localId);
                            EmitKeyedResource(sb, rdVar, c, cVar, defaultNs, pad);
                        }
                        sb.AppendLine($"{pad}this.{propName} = {rdVar};");
                    }
                }

                if (childElements.Count > 0)
                {
                if (contentProp == "Children")
                {
                    sb.AppendLine($"{pad}this.Children.Clear();");
                    foreach (var child in childElements)
                    {
                        var childVar = GenerateElement(sb, child, defaultNs, sourcePath, indent, ref localId);
                        sb.AppendLine($"{pad}this.Children.Add({childVar});");
                    }
                }
                else if (contentProp == "Content")
                {
                    if (childElements.Count == 1)
                    {
                        var childVar = GenerateElement(sb, childElements[0], defaultNs, sourcePath, indent, ref localId);
                        sb.AppendLine($"{pad}this.Content = {childVar};");
                    }
                }
                else if (contentProp == "Items")
                {
                    sb.AppendLine($"{pad}this.Items.Clear();");
                    foreach (var child in childElements)
                    {
                        var childVar = GenerateElement(sb, child, defaultNs, sourcePath, indent, ref localId);
                        sb.AppendLine($"{pad}this.Items.Add({childVar});");
                    }
                }
                }
            }
        }

        private static bool IsCollectionProperty(string propName) =>
            propName.EndsWith("Definitions", StringComparison.Ordinal) ||
            propName.EndsWith("Children", StringComparison.Ordinal) ||
            propName.EndsWith("Items", StringComparison.Ordinal) ||
            propName == "Inlines" ||
            propName == "Resources" ||
            propName == "Columns";

        private static string InferContentProperty(string typeName)
        {
            if (typeName.EndsWith("TextBlock") || typeName.EndsWith("Label"))
            {
                return "Text";
            }
            if (typeName.Contains("ItemsControl") || typeName.Contains("ListBox") || typeName.Contains("ComboBox") ||
                typeName.Contains("TabControl") || typeName.Contains("DataGrid"))
            {
                return "Items";
            }
            if (typeName.EndsWith("Panel") || typeName.Contains("Canvas") || typeName.Contains("Grid"))
            {
                return "Children";
            }
            if (typeName.Contains("ResourceDictionary"))
            {
                return "Resources";
            }
            return "Content";
        }

        // `Click="{(s, e) => Save()}"`. When the event is known to the compilation the lambda is held in a field of the event's delegate type, so a
        // rerun of InitializeComponent (Hot Reload) removes the handler it added before and the event never fires twice. Without the symbol the lambda is
        // attached as written, which is right for the fresh element tree every run builds.
        private static void AppendEventLambda(StringBuilder sb, string pad, string varName, string typeName, string eventName, string lambda)
        {
            var delegateType = s_context is null ? null : FindEventDelegateType(TypeSymbol(typeName), eventName);
            if (delegateType is null || s_context is null)
            {
                sb.AppendLine($"{pad}{varName}.{eventName} += {lambda};");
                return;
            }

            var field = $"__xsg_event_{s_context.EventLambdaCount++}";
            s_context.ExpressionMembers.AppendLine($"        private {delegateType}? {field};");
            sb.AppendLine($"{pad}if (this.{field} is not null) {varName}.{eventName} -= this.{field};");
            sb.AppendLine($"{pad}this.{field} = {lambda};");
            sb.AppendLine($"{pad}{varName}.{eventName} += this.{field};");
        }

        private static string? FindEventDelegateType(INamedTypeSymbol? type, string eventName)
        {
            for (var t = type; t is not null; t = t.BaseType)
            {
                foreach (var member in t.GetMembers(eventName))
                {
                    if (member is IEventSymbol e)
                    {
                        return e.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                }
            }

            return null;
        }

        // MAUI's resolution order for a simple path `{Name}` / `{User.Name}`: a Binding against x:DataType when the target is bindable and the
        // DataContext type has the first name; `{.Name}` is that binding explicitly (reflective when there is no x:DataType). A name that the page
        // has as well is an error (LWXSG0012) unless written `{this.Name}` or `{.Name}`. Anything else returns null: plain C#.
        private static string? BindingExpressionCode(string expression, XElement? owner, string attrLocal, string? elementTypeName)
        {
            var context = s_context;
            if (context is null || owner is null || elementTypeName is null || expression.StartsWith("this.", StringComparison.Ordinal))
            {
                return null;
            }

            var explicitDataContext = expression.StartsWith(".", StringComparison.Ordinal);
            var path = explicitDataContext ? expression.Substring(1) : expression;
            var segments = path.Split('.');
            if (segments.Length == 0 || segments.Any(segment => !s_identifier.IsMatch(segment)))
            {
                return null;
            }

            // Only a dependency property can take a Binding; a CLR property is assigned the value.
            if (attrLocal.Contains(".") || FindDependencyPropertyField(TypeSymbol(elementTypeName), attrLocal) is null)
            {
                return null;
            }

            if (!explicitDataContext)
            {
                if (FindDataTypeScope(owner, false) is not { } scope
                    || ResolveDataType(scope.DataType, scope.Element, context.Compilation) is not { } root
                    || FindProperty(root, segments[0], context) is null)
                {
                    return null;
                }

                if (context.Within is { } page && PageDeclaresMember(page, segments[0]))
                {
                    Report(context, s_memberOnPageAndDataContext, owner, expression, segments[0], page.Name);
                    return null;
                }
            }

            return BindingCode("{Binding " + path + "}", owner, false);
        }

        private static bool PageDeclaresMember(INamedTypeSymbol page, string name)
        {
            for (ITypeSymbol? type = page; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
            {
                if (type.GetMembers(name).Any(member => member is IPropertySymbol or IFieldSymbol or IMethodSymbol { MethodKind: MethodKind.Ordinary }))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? ConvertAttributeValue(string value, string attrNs, string attrLocal, string? elementTypeName = null, XElement? owner = null)
        {
            value = value.Trim();
            if (XamlExpressions.TryGetExpression(value, out var expression))
            {
                // A C# expression in MAUI's syntax. A simple path that the DataContext type (x:DataType) has is a binding; anything else is
                // plain C# evaluated once in the generated class (see XamlExpressions).
                var bound = BindingExpressionCode(expression, owner, attrLocal, elementTypeName)
                    ?? CompoundBindingCode(expression, owner, attrLocal, elementTypeName);
                if (bound is null && expression.StartsWith(".", StringComparison.Ordinal) && s_context is { } leadingDotContext && owner is not null)
                {
                    // `.Name` is the DataContext; it is not C# on its own, so a form that cannot be a binding is reported, not emitted.
                    Report(leadingDotContext, s_leadingDotNotBinding, owner, expression);
                    return "default!";
                }

                return bound ?? XamlExpressions.ToCSharp(expression);
            }

            if (value.StartsWith("{x:Type ", StringComparison.Ordinal) || value.StartsWith("{x:Type}", StringComparison.Ordinal))
            {
                var inner = value.Substring("{x:Type".Length).Trim().TrimEnd('}');
                return $"typeof(global::{inner})";
            }

            if (value == "{x:Null}")
            {
                return "null";
            }

            if (value.StartsWith("{x:Static ", StringComparison.Ordinal))
            {
                var inner = value.Substring("{x:Static".Length).Trim().TrimEnd('}');
                return inner;
            }

            if (value.StartsWith("{Binding", StringComparison.Ordinal))
            {
                return BindingCode(value, owner, attrLocal == "DataContext");
            }

            // StaticResource/DynamicResource are handled in the attribute loop.

            if (value.StartsWith("{DynamicResource ", StringComparison.Ordinal))
            {
                var key = value.Substring("{DynamicResource".Length).Trim().TrimEnd('}').Trim();
                // SetResourceReference is emitted separately; here we return a marker handled by caller.
                // For property assignment we use FindResource at load (no auto-update). Prefer SetResourceReference
                // for DependencyProperty targets, which is handled below in GenerateElement.
                return $"(global::System.Windows.FrameworkElement.FindResource(\"{key}\"))";
            }

            // x:Reference
            if (value.StartsWith("{x:Reference ", StringComparison.Ordinal))
            {
                var name = value.Substring("{x:Reference".Length).Trim().TrimEnd('}');
                return name;
            }

            // markup extension unknown: leave as string
            if (value.StartsWith("{") && value.EndsWith("}"))
            {
                return $"\"{EscapeString(value)}\" /* markup extension */";
            }

            // Color property: map named colors to Colors.Xxx.
            if (attrLocal.Contains("Color", StringComparison.Ordinal))
            {
                // Map common named colors to Colors.Xxx (verified at runtime compile).
                var knownColors = new HashSet<string>(StringComparer.Ordinal)
                {
                    "AliceBlue", "AntiqueWhite", "Aqua", "Aquamarine", "Azure", "Beige", "Bisque",
                    "Black", "BlanchedAlmond", "Blue", "BlueViolet", "Brown", "BurlyWood", "CadetBlue",
                    "Chartreuse", "Chocolate", "Coral", "CornflowerBlue", "Cornsilk", "Crimson", "Cyan",
                    "DarkBlue", "DarkCyan", "DarkGoldenrod", "DarkGray", "DarkGreen", "DarkKhaki",
                    "DarkMagenta", "DarkOliveGreen", "DarkOrange", "DarkOrchid", "DarkRed",
                    "DarkSalmon", "DarkSeaGreen", "DarkSlateBlue", "DarkSlateGray", "DarkTurquoise",
                    "DarkViolet", "DeepPink", "DeepSkyBlue", "DimGray", "DodgerBlue", "Firebrick",
                    "FloralWhite", "ForestGreen", "Fuchsia", "Gainsboro", "GhostWhite", "Gold",
                    "Goldenrod", "Gray", "Green", "GreenYellow", "Honeydew", "HotPink", "IndianRed",
                    "Indigo", "Ivory", "Khaki", "Lavender", "LavenderBlush", "LawnGreen", "LemonChiffon",
                    "LightBlue", "LightCoral", "LightCyan", "LightGoldenrodYellow", "LightGray",
                    "LightGreen", "LightPink", "LightSalmon", "LightSeaGreen", "LightSkyBlue",
                    "LightSlateGray", "LightSteelBlue", "LightYellow", "Lime", "LimeGreen", "Linen",
                    "Magenta", "Maroon", "MediumAquamarine", "MediumBlue", "MediumOrchid",
                    "MediumPurple", "MediumSeaGreen", "MediumSlateBlue", "MediumSpringGreen",
                    "MediumTurquoise", "MediumVioletRed", "MidnightBlue", "MintCream", "MistyRose",
                    "Moccasin", "NavajoWhite", "Navy", "OldLace", "Olive", "OliveDrab", "Orange",
                    "OrangeRed", "Orchid", "PaleGoldenrod", "PaleGreen", "PaleTurquoise",
                    "PaleVioletRed", "PapayaWhip", "PeachPuff", "Peru", "Pink", "Plum",
                    "PowderBlue", "Purple", "Red", "RosyBrown", "RoyalBlue", "SaddleBrown", "Salmon",
                    "SandyBrown", "SeaGreen", "SeaShell", "Sienna", "Silver", "SkyBlue", "SlateBlue",
                    "SlateGray", "Snow", "SpringGreen", "SteelBlue", "Tan", "Teal", "Thistle",
                    "Tomato", "Transparent", "Turquoise", "Violet", "Wheat", "White", "WhiteSmoke",
                    "Yellow", "YellowGreen",
                };
                if (knownColors.Contains(value))
                {
                    return $"global::System.Windows.Media.Colors.{value}";
                }
            }

            // GridLength: only convert when unambiguous (*, Auto, *-suffix, or GridLength in name).
            bool isGridLengthAttr = attrLocal.Contains("GridLength", StringComparison.Ordinal);
            bool isGridLengthValue = value == "*" || value == "Auto" || value.EndsWith("*", StringComparison.Ordinal);
            bool isGridLengthElement = (elementTypeName != null && (elementTypeName.Contains("RowDefinition") || elementTypeName.Contains("ColumnDefinition"))) &&
                (attrLocal == "Width" || attrLocal == "Height" || attrLocal == "MinWidth" || attrLocal == "MaxWidth" ||
                 attrLocal == "MinHeight" || attrLocal == "MaxHeight");
            if (isGridLengthAttr || isGridLengthValue || isGridLengthElement)
            {
                if (value == "*")
                {
                    return "new global::System.Windows.GridLength(1, global::System.Windows.GridUnitType.Star)";
                }
                if (value == "Auto")
                {
                    return "new global::System.Windows.GridLength(1, global::System.Windows.GridUnitType.Auto)";
                }
                if (value.EndsWith("*", StringComparison.Ordinal) && double.TryParse(value.TrimEnd('*'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var starVal))
                {
                    return $"new global::System.Windows.GridLength({starVal.ToString(System.Globalization.CultureInfo.InvariantCulture)}d, global::System.Windows.GridUnitType.Star)";
                }
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var glVal))
                {
                    return $"new global::System.Windows.GridLength({glVal.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
                }
            }

            // Thickness (10 or "10,20,30,40")
            if (attrLocal.Contains("Thickness", StringComparison.Ordinal) || attrLocal == "Margin" || attrLocal == "Padding" ||
                attrLocal == "BorderThickness")
            {
                var parts = value.Split(',');
                if (parts.Length == 1 && double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t1))
                {
                    return $"new global::System.Windows.Thickness({t1.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
                }
                if (parts.Length == 2 && double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t2a) &&
                    double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t2b))
                {
                    return $"new global::System.Windows.Thickness({t2a.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {t2b.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {t2a.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {t2b.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
                }
                if (parts.Length == 4)
                {
                    var nums = parts.Select(p => double.TryParse(p.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0).ToArray();
                    return $"new global::System.Windows.Thickness({nums[0].ToString(System.Globalization.CultureInfo.InvariantCulture)}, {nums[1].ToString(System.Globalization.CultureInfo.InvariantCulture)}, {nums[2].ToString(System.Globalization.CultureInfo.InvariantCulture)}, {nums[3].ToString(System.Globalization.CultureInfo.InvariantCulture)})";
                }
            }

            // CornerRadius
            if (attrLocal.Contains("CornerRadius", StringComparison.Ordinal))
            {
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cr))
                {
                    return $"new global::System.Windows.CornerRadius({cr.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
                }
            }

            // Common WPF enum properties: map named values to static fields.
            if (attrLocal == "Visibility")
            {
                return $"global::System.Windows.Visibility.{value}";
            }
            if (attrLocal == "HorizontalAlignment")
            {
                return $"global::System.Windows.HorizontalAlignment.{value}";
            }
            if (attrLocal == "VerticalAlignment")
            {
                return $"global::System.Windows.VerticalAlignment.{value}";
            }
            if (attrLocal == "TextAlignment")
            {
                return $"global::System.Windows.TextAlignment.{value}";
            }
            if (attrLocal == "Orientation")
            {
                return $"global::System.Windows.Controls.Orientation.{value}";
            }
            if (attrLocal == "Dock")
            {
                return $"global::System.Windows.Controls.Dock.{value}";
            }
            if (attrLocal == "Stretch")
            {
                return $"global::System.Windows.Media.Stretch.{value}";
            }
            if (attrLocal == "FlowDirection")
            {
                return $"global::System.Windows.FlowDirection.{value}";
            }
            if (attrLocal == "FontStyle")
            {
                return $"global::System.Windows.FontStyles.{value}";
            }
            if (attrLocal == "FontWeight")
            {
                return $"global::System.Windows.FontWeights.{value}";
            }
            if (attrLocal == "Cursor")
            {
                return $"global::System.Windows.Input.Cursors.{value}";
            }

            // Brush-valued properties: map named colors to SolidColorBrush.
            if (attrLocal == "Foreground" || attrLocal == "Background" || attrLocal == "BorderBrush" ||
                attrLocal == "Fill" || attrLocal == "Stroke" || attrLocal.Contains("Brush", StringComparison.Ordinal))
            {
                var knownColors = new HashSet<string>(StringComparer.Ordinal)
                {
                    "AliceBlue", "AntiqueWhite", "Aqua", "Aquamarine", "Azure", "Beige", "Bisque",
                    "Black", "BlanchedAlmond", "Blue", "BlueViolet", "Brown", "BurlyWood", "CadetBlue",
                    "Chartreuse", "Chocolate", "Coral", "CornflowerBlue", "Cornsilk", "Crimson", "Cyan",
                    "DarkBlue", "DarkCyan", "DarkGoldenrod", "DarkGray", "DarkGreen", "DarkKhaki",
                    "DarkMagenta", "DarkOliveGreen", "DarkOrange", "DarkOrchid", "DarkRed",
                    "DarkSalmon", "DarkSeaGreen", "DarkSlateBlue", "DarkSlateGray", "DarkTurquoise",
                    "DarkViolet", "DeepPink", "DeepSkyBlue", "DimGray", "DodgerBlue", "Firebrick",
                    "FloralWhite", "ForestGreen", "Fuchsia", "Gainsboro", "GhostWhite", "Gold",
                    "Goldenrod", "Gray", "Green", "GreenYellow", "Honeydew", "HotPink", "IndianRed",
                    "Indigo", "Ivory", "Khaki", "Lavender", "LavenderBlush", "LawnGreen", "LemonChiffon",
                    "LightBlue", "LightCoral", "LightCyan", "LightGoldenrodYellow", "LightGray",
                    "LightGreen", "LightPink", "LightSalmon", "LightSeaGreen", "LightSkyBlue",
                    "LightSlateGray", "LightSteelBlue", "LightYellow", "Lime", "LimeGreen", "Linen",
                    "Magenta", "Maroon", "MediumAquamarine", "MediumBlue", "MediumOrchid",
                    "MediumPurple", "MediumSeaGreen", "MediumSlateBlue", "MediumSpringGreen",
                    "MediumTurquoise", "MediumVioletRed", "MidnightBlue", "MintCream", "MistyRose",
                    "Moccasin", "NavajoWhite", "Navy", "OldLace", "Olive", "OliveDrab", "Orange",
                    "OrangeRed", "Orchid", "PaleGoldenrod", "PaleGreen", "PaleTurquoise",
                    "PaleVioletRed", "PapayaWhip", "PeachPuff", "Peru", "Pink", "Plum",
                    "PowderBlue", "Purple", "Red", "RosyBrown", "RoyalBlue", "SaddleBrown", "Salmon",
                    "SandyBrown", "SeaGreen", "SeaShell", "Sienna", "Silver", "SkyBlue", "SlateBlue",
                    "SlateGray", "Snow", "SpringGreen", "SteelBlue", "Tan", "Teal", "Thistle",
                    "Tomato", "Transparent", "Turquoise", "Violet", "Wheat", "White", "WhiteSmoke",
                    "Yellow", "YellowGreen",
                };
                if (knownColors.Contains(value))
                {
                    return $"new global::System.Windows.Media.SolidColorBrush(global::System.Windows.Media.Colors.{value})";
                }
            }

            // boolean
            if (bool.TryParse(value, out var b))
            {
                return b ? "true" : "false";
            }

            // numeric
            if (int.TryParse(value, out var i))
            {
                return i.ToString();
            }
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
            {
                return d.ToString(System.Globalization.CultureInfo.InvariantCulture) + "d";
            }

            // enum-like or type converter: return string literal, WPF XAML uses type converters at runtime
            // For source-gen we emit a string and rely on implicit conversion? Not safe. Emit as string with comment.
            return $"\"{EscapeString(value)}\"";
        }

        private static string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string GetPrefix(XElement el, string nsUri)
        {
            // find prefix from element's namespace declarations
            foreach (var attr in el.Attributes())
            {
                if (attr.IsNamespaceDeclaration)
                {
                    if (attr.Value == nsUri && attr.Name.LocalName != "xmlns")
                    {
                        return attr.Name.LocalName;
                    }
                }
            }
            return string.Empty;
        }
    }
}
