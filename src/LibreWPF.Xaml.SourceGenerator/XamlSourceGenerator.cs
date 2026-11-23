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
using Microsoft.CodeAnalysis.Text;

namespace LibreWPF.Xaml.SourceGenerator
{
    /// <summary>
    /// Source generator that turns XAML files into generated InitializeComponent() code,
    /// mirroring the MAUI/WXSG approach. Enabled via MSBuild property
    /// <c>UseLibreWpfXamlSourceGenerator</c> or env var <c>LIBREWPF_USE_XSG</c>.
    /// </summary>
    [Generator]
    public sealed class XamlSourceGenerator : IIncrementalGenerator
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

            context.RegisterSourceOutput(xamlFiles, static (spc, file) =>
            {
                try
                {
                    Generate(spc, file);
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
            });
        }

        private static void Generate(SourceProductionContext spc, AdditionalText file)
        {
            var text = file.GetText(spc.CancellationToken);
            if (text is null)
            {
                return;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Parse(InjectImplicitXmlns(text.ToString()), LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
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

            var root = doc.Root;
            if (root is null)
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
                return; // no x:Class => no generated partial class
            }

            var fullClassName = classAttr.Value;
            var lastDot = fullClassName.LastIndexOf('.');
            var ns = lastDot >= 0 ? fullClassName.Substring(0, lastDot) : string.Empty;
            var className = lastDot >= 0 ? fullClassName.Substring(lastDot + 1) : fullClassName;

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
            GenerateRootElement(sb, root, defaultNs, sourcePath, 12, ref localId);

            sb.AppendLine("            // End generated object graph");
            sb.AppendLine("        }");

            // x:Code: inline C# members (MAUI-style) — emit members only (usings hoisted above).
            if (xCodeMembers.Length > 0)
            {
                sb.AppendLine(xCodeMembers.ToString());
            }

            sb.AppendLine("    }");

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
        private static string GenerateTemplate(StringBuilder sb, XElement el, string typeName, string defaultNs, string sourcePath, int indent, ref int localId)
        {
            var pad = new string(' ', indent);
            var varName = $"__{el.Name.LocalName}_{++localId}";
            sb.AppendLine($"{pad}var {varName} = new global::{typeName}();");

            // TargetType / DataType attribute
            foreach (var attr in el.Attributes())
            {
                var attrNs = attr.Name.NamespaceName;
                var attrLocal = attr.Name.LocalName;
                if (attrNs == "http://www.w3.org/2000/xmlns/" || attrLocal == "xmlns")
                {
                    continue;
                }
                if (attrNs.Contains("xaml") && (attrLocal == "Class" || attrLocal == "Name" || attrLocal == "Key"))
                {
                    continue;
                }
                if (attrLocal == "DataType" || attrLocal == "TargetType")
                {
                    var v = attr.Value.Trim();
                    if (v.StartsWith("{x:Type ", StringComparison.Ordinal))
                    {
                        var t = v.Substring("{x:Type".Length).Trim().TrimEnd('}');
                        var resolved = ResolveTypeNameFromName(t, defaultNs) ?? t;
                        sb.AppendLine($"{pad}{varName}.{attrLocal} = typeof(global::{resolved});");
                    }
                    else if (v.StartsWith("{x:Type}", StringComparison.Ordinal))
                    {
                        // skip
                    }
                    else
                    {
                        var resolved = ResolveTypeNameFromName(v, defaultNs) ?? v;
                        sb.AppendLine($"{pad}{varName}.{attrLocal} = typeof(global::{resolved});");
                    }
                    continue;
                }
                var propCode = ConvertAttributeValue(attr.Value, attrNs, attrLocal, typeName);
                if (propCode is null)
                {
                    continue;
                }
                sb.AppendLine($"{pad}{varName}.{attrLocal} = {propCode};");
            }

            // VisualTree: first child element becomes the factory root.
            var children = el.Elements().ToList();
            if (children.Count > 0)
            {
                var rootChild = children[0];
                var childType = ResolveTypeName(rootChild, defaultNs);
                if (childType is not null)
                {
                    var factoryVar = $"__fef_{++localId}";
                    sb.AppendLine($"{pad}var {factoryVar} = new global::System.Windows.FrameworkElementFactory(typeof(global::{childType}));");
                    // Set properties on factory
                    foreach (var a in rootChild.Attributes())
                    {
                        var aNs = a.Name.NamespaceName;
                        var aLocal = a.Name.LocalName;
                        if (aNs == "http://www.w3.org/2000/xmlns/" || aLocal == "xmlns")
                        {
                            continue;
                        }
                        if (aNs.Contains("xaml") && (aLocal == "Class" || aLocal == "Name" || aLocal == "Key"))
                        {
                            continue;
                        }
                        var v = a.Value;
                        if (v.StartsWith("{Binding", StringComparison.Ordinal))
                        {
                            var pm = System.Text.RegularExpressions.Regex.Match(v, @"Path\s*=\s*([^,}]+)");
                            var path = pm.Success ? pm.Groups[1].Value.Trim() : "";
                            if (string.IsNullOrEmpty(path))
                            {
                                var pm2 = System.Text.RegularExpressions.Regex.Match(v, @"\{\s*Binding\s+([^,}]+)");
                                path = pm2.Success ? pm2.Groups[1].Value.Trim() : "";
                            }
                            sb.AppendLine($"{pad}{factoryVar}.SetBinding(global::{childType}.{aLocal}Property, new global::System.Windows.Data.Binding(\"{path}\"));");
                        }
                        else if (v.StartsWith("{StaticResource ", StringComparison.Ordinal))
                        {
                            var key = v.Substring("{StaticResource".Length).Trim().TrimEnd('}').Trim();
                            sb.AppendLine($"{pad}{factoryVar}.SetValue(global::{childType}.{aLocal}Property, global::System.Windows.FrameworkElement.FindResource(\"{key}\"));");
                        }
                        else
                        {
                            var pc = ConvertAttributeValue(v, aNs, aLocal, childType);
                            if (pc is not null)
                            {
                                sb.AppendLine($"{pad}{factoryVar}.SetValue(global::{childType}.{aLocal}Property, {pc});");
                            }
                        }
                    }
                    sb.AppendLine($"{pad}{varName}.VisualTree = {factoryVar};");
                }
            }

            return varName;
        }

        private static string GenerateElement(StringBuilder sb, XElement el, string defaultNs, string sourcePath, int indent, ref int localId)
        {
            var pad = new string(' ', indent);
            var lineInfo = el as IXmlLineInfo;
            if (lineInfo != null && lineInfo.HasLineInfo())
            {
                var escapedPath = sourcePath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.AppendLine($"#line {lineInfo.LineNumber} \"{escapedPath}\"");
            }
            var typeName = ResolveTypeName(el, defaultNs);
            if (typeName is null)
            {
                sb.AppendLine($"{pad}// Unknown type: {el.Name.LocalName}");
                return "null!";
            }

            // DataTemplate / ControlTemplate: build a FrameworkElementFactory tree.
            if (typeName.Contains("Template"))
            {
                return GenerateTemplate(sb, el, typeName, defaultNs, sourcePath, indent, ref localId);
            }

            var varName = $"__{el.Name.LocalName}_{++localId}";
            sb.AppendLine($"{pad}var {varName} = new global::{typeName}();");

            // Properties from attributes (excluding xmlns, x:Class, x:Name)
            foreach (var attr in el.Attributes())
            {
                var attrNs = attr.Name.NamespaceName;
                var attrLocal = attr.Name.LocalName;

                // Skip namespace declarations (xmlns, xmlns:x, ...).
                if (attrNs == "http://www.w3.org/2000/xmlns/" || attrLocal == "xmlns")
                {
                    continue;
                }

                if (attrNs.Contains("xaml") && (attrLocal == "Class" || attrLocal == "Name" || attrLocal == "Key"))
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
                    sb.AppendLine($"{pad}{varName}.SetResourceReference(global::{typeName}.{attrLocal}Property, \"{key}\");");
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
                if (attrLocal == "Source" && !attr.Name.LocalName.Contains("."))
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

                var propCode = ConvertAttributeValue(value, attrNs, attrLocal, typeName);
                if (propCode is null)
                {
                    continue;
                }

                // attached property? (Owner.Property)
                if (attr.Name.LocalName.Contains("."))
                {
                    var parts = attr.Name.LocalName.Split('.');
                    var ownerType = ResolveTypeNameFromName(parts[0], defaultNs) ?? parts[0];
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
                        var bindCode = ConvertAttributeValue(value, attrNs, propName, typeName);
                        sb.AppendLine($"{pad}{varName}.SetBinding(global::{ownerType}.{propName}Property, {bindCode});");
                    }
                    else
                    {
                        var attachedCode = ConvertAttributeValue(value, attrNs, propName, typeName);
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
                    else
                    {
                        sb.AppendLine($"{pad}{varName}.SetBinding(global::{typeName}.{attrLocal}Property, {propCode});");
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
            if (nameAttr != null)
            {
                sb.AppendLine($"{pad}this.{nameAttr.Value} = {varName};");
                // As BAML does: x:Name is also the element's Name and an entry in the root's
                // NameScope, so FindName, ElementName bindings and tooling lookups by name work.
                // Unregister first: Hot Reload re-runs InitializeComponent on the live instance.
                var nameLiteral = EscapeString(nameAttr.Value);
                sb.AppendLine($"{pad}if ((object){varName} is global::System.Windows.FrameworkElement __fe_{varName}) __fe_{varName}.Name = \"{nameLiteral}\";");
                sb.AppendLine($"{pad}else if ((object){varName} is global::System.Windows.FrameworkContentElement __fce_{varName}) __fce_{varName}.Name = \"{nameLiteral}\";");
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
                        sb.AppendLine($"{pad}{varName}.{propName} = {cVar};");
                    }
                    else if (peChildren.Count > 1)
                    {
                        // Implicit ResourceDictionary
                        var rdVar = $"__rd_{++localId}";
                        sb.AppendLine($"{pad}var {rdVar} = new global::System.Windows.ResourceDictionary();");
                        foreach (var c in peChildren)
                        {
                            var cVar = GenerateElement(sb, c, defaultNs, sourcePath, indent, ref localId);
                            var keyAttr = c.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key" && a.Name.NamespaceName.Contains("xaml"));
                            if (keyAttr is null)
                            {
                                keyAttr = c.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key");
                            }
                            if (keyAttr != null)
                            {
                                sb.AppendLine($"{pad}{rdVar}[\"{keyAttr.Value}\"] = {cVar};");
                            }
                            else
                            {
                                var typeKey = c.Attributes().FirstOrDefault(a =>
                                    a.Name.LocalName == "TargetType" || a.Name.LocalName == "DataType");
                                if (typeKey != null)
                                {
                                    var t = typeKey.Value.Trim();
                                    if (t.StartsWith("{x:Type ", StringComparison.Ordinal))
                                    {
                                        t = t.Substring("{x:Type".Length).Trim().TrimEnd('}');
                                    }
                                    sb.AppendLine($"{pad}{rdVar}[typeof(global::{t})] = {cVar};");
                                }
                                else
                                {
                                    sb.AppendLine($"{pad}// resource without x:Key skipped: {c.Name.LocalName}");
                                }
                            }
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
                        var keyAttr = child.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key" && a.Name.NamespaceName.Contains("xaml"));
                        if (keyAttr is null)
                        {
                            keyAttr = child.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key");
                        }
                        if (keyAttr != null)
                        {
                            sb.AppendLine($"{pad}{varName}[\"{keyAttr.Value}\"] = {cVar};");
                        }
                        else
                        {
                            var typeKey = child.Attributes().FirstOrDefault(a =>
                                a.Name.LocalName == "TargetType" || a.Name.LocalName == "DataType");
                            if (typeKey != null)
                            {
                                var t = typeKey.Value.Trim();
                                if (t.StartsWith("{x:Type ", StringComparison.Ordinal))
                                {
                                    t = t.Substring("{x:Type".Length).Trim().TrimEnd('}');
                                }
                                sb.AppendLine($"{pad}{varName}[typeof(global::{t})] = {cVar};");
                            }
                            else
                            {
                                sb.AppendLine($"{pad}// resource without x:Key skipped: {child.Name.LocalName}");
                            }
                        }
                    }
                }
                else
                {
                    // Determine content property: Children for panels, Content for ContentControl, Items for ItemsControl
                    var contentProp = InferContentProperty(typeName);
                    if (childElements.Count > 0)
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
            return null;
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
                if (attrNs == "http://www.w3.org/2000/xmlns/" || attrLocal == "xmlns")
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

                var propCode = ConvertAttributeValue(value, attrNs, attrLocal, typeName);
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
                            var keyAttr = c.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key" && a.Name.NamespaceName.Contains("xaml"));
                            if (keyAttr is null)
                            {
                                keyAttr = c.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key");
                            }
                            if (keyAttr != null)
                            {
                                sb.AppendLine($"{pad}{rdVar}[\"{keyAttr.Value}\"] = {cVar};");
                            }
                            else
                            {
                                var typeKey = c.Attributes().FirstOrDefault(a =>
                                    a.Name.LocalName == "TargetType" || a.Name.LocalName == "DataType");
                                if (typeKey != null)
                                {
                                    var t = typeKey.Value.Trim();
                                    if (t.StartsWith("{x:Type ", StringComparison.Ordinal))
                                    {
                                        t = t.Substring("{x:Type".Length).Trim().TrimEnd('}');
                                    }
                                    sb.AppendLine($"{pad}{rdVar}[typeof(global::{t})] = {cVar};");
                                }
                                else
                                {
                                    sb.AppendLine($"{pad}{rdVar}.Add({cVar});");
                                }
                            }
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

        private static string? ConvertAttributeValue(string value, string attrNs, string attrLocal, string? elementTypeName = null)
        {
            value = value.Trim();
            if (value.StartsWith("{cs:", StringComparison.Ordinal))
            {
                // extract expression
                var expr = value.Substring(4).Trim();
                if (expr.EndsWith("}"))
                {
                    expr = expr.Substring(0, expr.Length - 1).Trim();
                }
                return expr; // embed verbatim
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
                // basic: new Binding("path")
                var pathMatch = Regex.Match(value, @"Path\s*=\s*([^,}]+)");
                var path = pathMatch.Success ? pathMatch.Groups[1].Value.Trim() : "";
                if (string.IsNullOrEmpty(path))
                {
                    // positional
                    var posMatch = Regex.Match(value, @"\{\s*Binding\s+([^,}]+)");
                    path = posMatch.Success ? posMatch.Groups[1].Value.Trim() : "";
                }
                return $"new global::System.Windows.Data.Binding(\"{path}\")";
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
