// Licensed to the .NET Foundation under one or more agreements.
// LibreWPF XAML Source Generator: styles, setters, triggers and template trees, typed against the compilation.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace LibreWPF.Xaml.SourceGenerator
{
    public sealed partial class XamlSourceGenerator
    {
        // The CLR namespaces the WPF presentation XML namespace maps to (XmlnsDefinition of PresentationFramework/Core/WindowsBase).
        private static readonly string[] s_presentationClrNamespaces =
        {
            "System.Windows", "System.Windows.Controls", "System.Windows.Controls.Primitives", "System.Windows.Documents",
            "System.Windows.Shapes", "System.Windows.Media", "System.Windows.Media.Animation", "System.Windows.Media.Imaging",
            "System.Windows.Media.Effects", "System.Windows.Media.Media3D", "System.Windows.Data", "System.Windows.Input",
            "System.Windows.Navigation", "System.Windows.Markup", "System.Windows.Automation", "System.Windows.Shell",
            "System.Windows.Media.TextFormatting",
        };

        private static INamedTypeSymbol? TypeSymbol(string? fullName) =>
            fullName is null ? null : s_context?.Compilation.GetTypeByMetadataName(fullName);

        // A presentation-namespace element or type name the fixed table does not list, resolved against the compilation.
        private static string? ResolvePresentationTypeSemantically(string localName)
        {
            if (s_context is null || localName.Length == 0 || localName.Contains("."))
            {
                return null;
            }

            foreach (var ns in s_presentationClrNamespaces)
            {
                var candidate = ns + "." + localName;
                if (s_context.Compilation.GetTypeByMetadataName(candidate) is not null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string StripXType(string text)
        {
            text = text.Trim();
            if (text.StartsWith("{x:Type", StringComparison.Ordinal))
            {
                text = text.Substring("{x:Type".Length).Trim().TrimEnd('}').Trim();
            }
            return text;
        }

        // ---- dependency properties --------------------------------------------------------------------------------------------

        /// <summary>
        /// The C# for a dependency property written as <c>Prop</c> (on <paramref name="elementType"/>) or <c>Owner.Prop</c>, and the
        /// CLR type of its values. The owner is the type that declares the static <c>PropProperty</c> field (TextBlock.Foreground is
        /// TextElement.ForegroundProperty's AddOwner, declared on TextBlock; Control.Background is declared on Control).
        /// </summary>
        private static (string Code, ITypeSymbol? Type, string Name) ResolveDependencyProperty(string elementType, string property, XElement scope, string defaultNs)
        {
            var owner = elementType;
            var name = property.Trim();
            var dot = name.LastIndexOf('.');
            if (dot > 0)
            {
                owner = ResolveTypeNameInScope(name.Substring(0, dot), scope, defaultNs);
                name = name.Substring(dot + 1);
            }

            var start = TypeSymbol(owner);
            string declaring = owner;
            IFieldSymbol? field = null;
            for (var t = start; t is not null; t = t.BaseType)
            {
                field = t.GetMembers(name + "Property").OfType<IFieldSymbol>().FirstOrDefault(f => f.IsStatic);
                if (field is not null)
                {
                    declaring = t.ToDisplayString().Replace("global::", "");
                    break;
                }
            }

            ITypeSymbol? type = null;
            for (var t = start; t is not null && type is null; t = t.BaseType)
            {
                type = t.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(p => !p.IsStatic && !p.IsIndexer)?.Type;
            }
            if (type is null && start is not null)
            {
                // Attached property: static GetProp(DependencyObject) on the owner.
                for (var t = start; t is not null && type is null; t = t.BaseType)
                {
                    type = t.GetMembers("Get" + name).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1)?.ReturnType;
                }
            }

            return ($"global::{declaring}.{name}Property", type, name);
        }

        // ---- values -----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// C# for an attribute value assigned to a property of <paramref name="type"/>: markup extensions that have a code form, then
        /// literals converted at build time by the target type, so no TypeConverter runs at run time.
        /// </summary>
        private static string ValueCode(string raw, ITypeSymbol? type, XElement scope, string defaultNs, string? templateTarget)
        {
            var value = raw.Trim();
            if (value.StartsWith("{", StringComparison.Ordinal) && !value.StartsWith("{}", StringComparison.Ordinal))
            {
                var markup = MarkupValueCode(value, scope, defaultNs, templateTarget);
                if (markup is not null)
                {
                    return markup;
                }
            }
            else if (value.StartsWith("{}", StringComparison.Ordinal))
            {
                value = value.Substring(2);
            }

            return LiteralCode(value, type, scope, defaultNs);
        }

        private static string? MarkupValueCode(string value, XElement scope, string defaultNs, string? templateTarget)
        {
            var inner = value.Substring(1, value.Length - 2).Trim();
            var space = inner.IndexOf(' ');
            var head = space < 0 ? inner : inner.Substring(0, space);
            var arg = space < 0 ? string.Empty : inner.Substring(space + 1).Trim();
            switch (head)
            {
                case "x:Null":
                    return "null";
                case "x:Static":
                    return StaticCode(arg, scope, defaultNs);
                case "x:Type":
                    return $"typeof(global::{ResolveTypeNameInScope(arg, scope, defaultNs)})";
                case "DynamicResource":
                    return $"new global::System.Windows.DynamicResourceExtension({ResourceKeyCode(arg, scope, defaultNs)})";
                case "StaticResource":
                    return $"this.FindResource({ResourceKeyCode(arg, scope, defaultNs)})";
                case "TemplateBinding":
                {
                    var property = arg.StartsWith("Property=", StringComparison.Ordinal) ? arg.Substring("Property=".Length) : arg;
                    var (code, _, _) = ResolveDependencyProperty(templateTarget ?? "System.Windows.Controls.Control", property, scope, defaultNs);
                    return $"new global::System.Windows.TemplateBindingExtension({code})";
                }
                case "Binding":
                    return BindingCode(value, scope, false);
                default:
                    return null;
            }
        }

        private static string StaticCode(string member, XElement scope, string defaultNs)
        {
            member = member.Trim();
            if (member.StartsWith("Member=", StringComparison.Ordinal))
            {
                member = member.Substring("Member=".Length);
            }
            var dot = member.LastIndexOf('.');
            if (dot < 0)
            {
                return member;
            }
            return $"global::{ResolveTypeNameInScope(member.Substring(0, dot), scope, defaultNs)}.{member.Substring(dot + 1)}";
        }

        private static string ResourceKeyCode(string key, XElement scope, string defaultNs)
        {
            key = key.Trim();
            if (key.StartsWith("ResourceKey=", StringComparison.Ordinal))
            {
                key = key.Substring("ResourceKey=".Length).Trim();
            }
            if (key.StartsWith("{x:Static", StringComparison.Ordinal))
            {
                return StaticCode(key.Substring("{x:Static".Length).TrimEnd('}').Trim(), scope, defaultNs);
            }
            if (key.StartsWith("{x:Type", StringComparison.Ordinal))
            {
                return $"typeof(global::{ResolveTypeNameInScope(StripXType(key), scope, defaultNs)})";
            }
            return $"\"{EscapeString(key)}\"";
        }

        private static readonly Dictionary<string, string> s_staticValueClasses = new(StringComparer.Ordinal)
        {
            ["System.Windows.FontWeight"] = "System.Windows.FontWeights",
            ["System.Windows.FontStyle"] = "System.Windows.FontStyles",
            ["System.Windows.FontStretch"] = "System.Windows.FontStretches",
            ["System.Windows.Input.Cursor"] = "System.Windows.Input.Cursors",
            ["System.Windows.Media.Color"] = "System.Windows.Media.Colors",
        };

        private static string LiteralCode(string value, ITypeSymbol? type, XElement scope, string defaultNs)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                type = nullable.TypeArguments[0];
            }

            var inv = CultureInfo.InvariantCulture;
            var typeName = type?.ToDisplayString().Replace("global::", "");
            switch (type?.SpecialType)
            {
                case SpecialType.System_String:
                    return $"\"{EscapeString(value)}\"";
                case SpecialType.System_Boolean:
                    return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
                case SpecialType.System_Double:
                case SpecialType.System_Single:
                {
                    var suffix = type.SpecialType == SpecialType.System_Single ? "f" : "d";
                    if (string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "NaN", StringComparison.OrdinalIgnoreCase))
                    {
                        return type.SpecialType == SpecialType.System_Single ? "float.NaN" : "double.NaN";
                    }
                    if (value == "Infinity") return "double.PositiveInfinity";
                    return double.TryParse(value, NumberStyles.Float, inv, out var d) ? d.ToString("R", inv) + suffix : $"\"{EscapeString(value)}\"";
                }
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_Int16:
                case SpecialType.System_Byte:
                    return long.TryParse(value, NumberStyles.Integer, inv, out var l) ? l.ToString(inv) : $"\"{EscapeString(value)}\"";
                case SpecialType.System_Object:
                    return $"\"{EscapeString(value)}\"";
            }

            if (type is { TypeKind: TypeKind.Enum })
            {
                var members = value.Split(',').Select(v => $"global::{typeName}.{v.Trim()}");
                return string.Join(" | ", members);
            }

            switch (typeName)
            {
                case "System.Windows.Media.Brush":
                case "System.Windows.Media.SolidColorBrush":
                    if (value.StartsWith("#", StringComparison.Ordinal) && ColorCode(value) is { } hexBrush)
                    {
                        return $"new global::System.Windows.Media.SolidColorBrush({hexBrush})";
                    }
                    return $"global::System.Windows.Media.Brushes.{value}";
                case "System.Windows.Media.Color":
                    return value.StartsWith("#", StringComparison.Ordinal) && ColorCode(value) is { } hex ? hex : $"global::System.Windows.Media.Colors.{value}";
                case "System.Windows.Thickness":
                    return FourDoubles(value, "global::System.Windows.Thickness");
                case "System.Windows.CornerRadius":
                    return FourDoubles(value, "global::System.Windows.CornerRadius");
                case "System.Windows.GridLength":
                    if (string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase)) return "global::System.Windows.GridLength.Auto";
                    if (value.EndsWith("*", StringComparison.Ordinal))
                    {
                        var star = value.Length == 1 ? "1" : value.TrimEnd('*');
                        return $"new global::System.Windows.GridLength({star}d, global::System.Windows.GridUnitType.Star)";
                    }
                    return $"new global::System.Windows.GridLength({value}d)";
                case "System.Windows.Media.FontFamily":
                    return $"new global::System.Windows.Media.FontFamily(\"{EscapeString(value)}\")";
                case "System.Windows.Point":
                case "System.Windows.Size":
                case "System.Windows.Vector":
                {
                    var p = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return p.Length == 2 ? $"new global::{typeName}({p[0]}d, {p[1]}d)" : $"\"{EscapeString(value)}\"";
                }
                case "System.Windows.Duration":
                    return TimeSpan.TryParse(value, inv, out var duration)
                        ? $"new global::System.Windows.Duration(global::System.TimeSpan.FromTicks({duration.Ticks}))"
                        : "global::System.Windows.Duration.Automatic";
                case "System.TimeSpan":
                    return TimeSpan.TryParse(value, inv, out var span) ? $"global::System.TimeSpan.FromTicks({span.Ticks})" : "default";
                case "System.Type":
                    return $"typeof(global::{ResolveTypeNameInScope(StripXType(value), scope, defaultNs)})";
                case "System.Windows.DependencyProperty":
                    return $"\"{EscapeString(value)}\"";
            }

            if (typeName is not null && s_staticValueClasses.TryGetValue(typeName, out var statics))
            {
                return $"global::{statics}.{value}";
            }

            return $"\"{EscapeString(value)}\"";
        }

        private static string? ColorCode(string hex)
        {
            var h = hex.TrimStart('#');
            if (h.Length == 3) h = "F" + h;
            if (h.Length == 4) h = string.Concat(h.Select(c => new string(c, 2)));
            if (h.Length == 6) h = "FF" + h;
            if (h.Length != 8 || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                return null;
            }
            return $"global::System.Windows.Media.Color.FromArgb({(argb >> 24) & 0xFF}, {(argb >> 16) & 0xFF}, {(argb >> 8) & 0xFF}, {argb & 0xFF})";
        }

        private static string FourDoubles(string value, string type)
        {
            var parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim() + "d").ToArray();
            return parts.Length switch
            {
                1 => $"new {type}({parts[0]})",
                2 => $"new {type}({parts[0]}, {parts[1]}, {parts[0]}, {parts[1]})",
                4 => $"new {type}({string.Join(", ", parts)})",
                _ => $"new {type}()",
            };
        }

        // ---- styles -----------------------------------------------------------------------------------------------------------

        private static string GenerateStyle(StringBuilder sb, XElement el, string defaultNs, string sourcePath, int indent, ref int localId)
        {
            var pad = new string(' ', indent);
            var v = $"__Style_{++localId}";
            sb.AppendLine($"{pad}var {v} = new global::System.Windows.Style();");

            var targetType = "System.Windows.FrameworkElement";
            var target = el.Attribute("TargetType");
            if (target is not null)
            {
                targetType = ResolveTypeNameInScope(StripXType(target.Value), el, defaultNs);
                sb.AppendLine($"{pad}{v}.TargetType = typeof(global::{targetType});");
            }

            var basedOn = el.Attribute("BasedOn");
            if (basedOn is not null)
            {
                sb.AppendLine($"{pad}{v}.BasedOn = (global::System.Windows.Style){ValueCode(basedOn.Value, null, el, defaultNs, null)};");
            }

            foreach (var child in el.Elements())
            {
                var local = child.Name.LocalName;
                if (local == "Style.Setters")
                {
                    foreach (var setter in child.Elements())
                    {
                        EmitSetter(sb, $"{v}.Setters", setter, targetType, null, defaultNs, sourcePath, indent, ref localId);
                    }
                }
                else if (local == "Style.Triggers")
                {
                    EmitTriggers(sb, $"{v}.Triggers", child, targetType, null, defaultNs, sourcePath, indent, ref localId);
                }
                else if (local == "Style.Resources")
                {
                    sb.AppendLine($"{pad}// Style.Resources is not generated yet");
                    ReportNotGenerated(child, "Style.Resources");
                }
                else if (!local.Contains("."))
                {
                    EmitSetter(sb, $"{v}.Setters", child, targetType, null, defaultNs, sourcePath, indent, ref localId);
                }
            }

            return v;
        }

        /// <summary>A Setter (or EventSetter-free Setter) added to <paramref name="collection"/>.</summary>
        private static void EmitSetter(StringBuilder sb, string collection, XElement setter, string targetType, Dictionary<string, string>? namedTypes,
            string defaultNs, string sourcePath, int indent, ref int localId)
        {
            var pad = new string(' ', indent);
            if (setter.Name.LocalName != "Setter")
            {
                sb.AppendLine($"{pad}// {setter.Name.LocalName} is not generated yet");
                ReportNotGenerated(setter, setter.Name.LocalName);
                return;
            }

            var property = setter.Attribute("Property")?.Value;
            if (property is null)
            {
                return;
            }

            var targetName = setter.Attribute("TargetName")?.Value;
            var ownerType = targetName is not null && namedTypes is not null && namedTypes.TryGetValue(targetName, out var named) ? named : targetType;
            var (dp, type, _) = ResolveDependencyProperty(ownerType, property, setter, defaultNs);

            string valueCode;
            var valueAttr = setter.Attribute("Value");
            if (valueAttr is not null)
            {
                valueCode = ValueCode(valueAttr.Value, type, setter, defaultNs, targetType);
            }
            else
            {
                var valueElement = setter.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter.Value")?.Elements().FirstOrDefault();
                if (valueElement is null)
                {
                    return;
                }
                valueCode = GenerateElement(sb, valueElement, defaultNs, sourcePath, indent, ref localId);
            }

            var nameArg = targetName is null ? string.Empty : $", \"{EscapeString(targetName)}\"";
            sb.AppendLine($"{pad}{collection}.Add(new global::System.Windows.Setter({dp}, {valueCode}{nameArg}));");
        }

        private static void EmitTriggers(StringBuilder sb, string collection, XElement triggers, string targetType, Dictionary<string, string>? namedTypes,
            string defaultNs, string sourcePath, int indent, ref int localId)
        {
            var pad = new string(' ', indent);
            foreach (var trigger in triggers.Elements())
            {
                if (trigger.Name.LocalName != "Trigger")
                {
                    sb.AppendLine($"{pad}// {trigger.Name.LocalName} is not generated yet");
                    ReportNotGenerated(trigger, trigger.Name.LocalName);
                    continue;
                }

                var property = trigger.Attribute("Property")?.Value;
                if (property is null)
                {
                    continue;
                }

                var sourceName = trigger.Attribute("SourceName")?.Value;
                var ownerType = sourceName is not null && namedTypes is not null && namedTypes.TryGetValue(sourceName, out var named) ? named : targetType;
                var (dp, type, _) = ResolveDependencyProperty(ownerType, property, trigger, defaultNs);
                var t = $"__Trigger_{++localId}";
                sb.AppendLine($"{pad}var {t} = new global::System.Windows.Trigger {{ Property = {dp}, Value = {ValueCode(trigger.Attribute("Value")?.Value ?? string.Empty, type, trigger, defaultNs, targetType)} }};");
                if (sourceName is not null)
                {
                    sb.AppendLine($"{pad}{t}.SourceName = \"{EscapeString(sourceName)}\";");
                }
                foreach (var setter in trigger.Elements())
                {
                    if (setter.Name.LocalName == "Trigger.Setters")
                    {
                        foreach (var s in setter.Elements())
                        {
                            EmitSetter(sb, $"{t}.Setters", s, targetType, namedTypes, defaultNs, sourcePath, indent, ref localId);
                        }
                    }
                    else
                    {
                        EmitSetter(sb, $"{t}.Setters", setter, targetType, namedTypes, defaultNs, sourcePath, indent, ref localId);
                    }
                }
                sb.AppendLine($"{pad}{collection}.Add({t});");
            }
        }

        // ---- templates --------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A ControlTemplate / DataTemplate / ItemsPanelTemplate: its whole visual tree as FrameworkElementFactory nodes (typed
        /// SetValue, TemplateBinding, DynamicResource as resource references, bindings), plus Trigger/Setter triggers.
        /// </summary>
        private static string GenerateFactoryTemplate(StringBuilder sb, XElement el, string typeName, string defaultNs, string sourcePath, int indent, ref int localId)
        {
            var pad = new string(' ', indent);
            var v = $"__{el.Name.LocalName}_{++localId}";
            sb.AppendLine($"{pad}var {v} = new global::{typeName}();");

            string? templateTarget = null;
            foreach (var attr in el.Attributes())
            {
                if (attr.Name.NamespaceName.Length != 0 || attr.Name.LocalName.StartsWith("xmlns", StringComparison.Ordinal))
                {
                    continue;
                }
                if (attr.Name.LocalName is "TargetType" or "DataType")
                {
                    var resolved = ResolveTypeNameInScope(StripXType(attr.Value), el, defaultNs);
                    sb.AppendLine($"{pad}{v}.{attr.Name.LocalName} = typeof(global::{resolved});");
                    if (attr.Name.LocalName == "TargetType")
                    {
                        templateTarget = resolved;
                    }
                }
            }

            templateTarget ??= typeName.EndsWith("ControlTemplate", StringComparison.Ordinal) ? "System.Windows.Controls.Control" : "System.Windows.FrameworkElement";

            var namedTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var d in el.Descendants())
            {
                var name = d.Attribute(XName.Get("Name", XamlNamespace)) ?? d.Attribute("Name");
                if (name is not null && !d.Name.LocalName.Contains(".") && ResolveTypeName(d, defaultNs) is { } t)
                {
                    namedTypes[name.Value] = t;
                }
            }

            XElement? triggers = null;
            foreach (var child in el.Elements())
            {
                var local = child.Name.LocalName;
                if (local.EndsWith(".Triggers", StringComparison.Ordinal))
                {
                    triggers = child;
                }
                else if (local.EndsWith(".Resources", StringComparison.Ordinal))
                {
                    sb.AppendLine($"{pad}// template resources are not generated yet");
                    ReportNotGenerated(child, "template resources (" + local + ")");
                }
                else if (!local.Contains("."))
                {
                    var root = FactoryNode(sb, child, defaultNs, sourcePath, pad, ref localId, templateTarget);
                    if (root is not null)
                    {
                        sb.AppendLine($"{pad}{v}.VisualTree = {root};");
                    }
                }
            }

            if (triggers is not null)
            {
                EmitTriggers(sb, $"{v}.Triggers", triggers, templateTarget, namedTypes, defaultNs, sourcePath, indent, ref localId);
            }

            return v;
        }

        private static string? FactoryNode(StringBuilder sb, XElement el, string defaultNs, string sourcePath, string pad, ref int localId, string templateTarget)
        {
            var type = ResolveTypeName(el, defaultNs);
            if (type is null)
            {
                sb.AppendLine($"{pad}// Unknown type in template: {el.Name.LocalName}");
                return null;
            }

            var f = $"__fef_{++localId}";
            sb.AppendLine($"{pad}var {f} = new global::System.Windows.FrameworkElementFactory(typeof(global::{type}));");

            foreach (var attr in el.Attributes())
            {
                var ns = attr.Name.NamespaceName;
                var local = attr.Name.LocalName;
                if (ns == "http://www.w3.org/2000/xmlns/" || local == "xmlns" || IsCompiledBindingDirective(attr))
                {
                    continue;
                }
                if (ns == XamlNamespace || (ns.Length == 0 && local == "Name"))
                {
                    if (local == "Name")
                    {
                        sb.AppendLine($"{pad}{f}.Name = \"{EscapeString(attr.Value)}\";");
                    }
                    continue;
                }

                var value = attr.Value.Trim();
                var (dp, propertyType, name) = ResolveDependencyProperty(type, local, el, defaultNs);
                if (value.StartsWith("{Binding", StringComparison.Ordinal))
                {
                    sb.AppendLine($"{pad}{f}.SetBinding({dp}, {BindingCode(value, el, name == "DataContext")});");
                }
                else if (value.StartsWith("{DynamicResource", StringComparison.Ordinal))
                {
                    var key = value.Substring("{DynamicResource".Length, value.Length - "{DynamicResource".Length - 1).Trim();
                    sb.AppendLine($"{pad}{f}.SetResourceReference({dp}, {ResourceKeyCode(key, el, defaultNs)});");
                }
                else
                {
                    sb.AppendLine($"{pad}{f}.SetValue({dp}, {ValueCode(value, propertyType, el, defaultNs, templateTarget)});");
                }
            }

            foreach (var child in el.Elements())
            {
                var local = child.Name.LocalName;
                if (local.Contains("."))
                {
                    // Only a property of this very element (or a base class of it) is generated: an attached property, such as
                    // VisualStateManager.VisualStateGroups, would otherwise be set on the wrong owner or dropped without a word.
                    // It must also be a dependency property with ONE value element: a CLR collection property (Grid.RowDefinitions,
                    // Panel.Children, ItemsControl.Items) has no static XxxProperty field to SetValue, and a collection needs every
                    // child added, not the first one assigned.
                    var propertyName = local.Substring(local.IndexOf('.') + 1);
                    if (!IsPropertyOfTypeOrBase(type, local.Substring(0, local.IndexOf('.')))
                        || !IsDependencyProperty(type, propertyName)
                        || child.Elements().Count() > 1)
                    {
                        ReportNotGenerated(child, local);
                        continue;
                    }

                    // <Border.Background><SolidColorBrush .../></Border.Background>: a value object, built once and shared.
                    var valueElement = child.Elements().FirstOrDefault();
                    if (valueElement is null)
                    {
                        continue;
                    }
                    var (dp, _, _) = ResolveDependencyProperty(type, local.Substring(local.IndexOf('.') + 1), el, defaultNs);
                    var valueVar = GenerateElement(sb, valueElement, defaultNs, sourcePath, pad.Length, ref localId);
                    sb.AppendLine($"{pad}{f}.SetValue({dp}, {valueVar});");
                    continue;
                }

                var node = FactoryNode(sb, child, defaultNs, sourcePath, pad, ref localId, templateTarget);
                if (node is not null)
                {
                    sb.AppendLine($"{pad}{f}.AppendChild({node});");
                }
            }

            var text = el.Nodes().OfType<XText>().Select(t => t.Value).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (text is not null && !el.Elements().Any())
            {
                var contentProperty = ContentPropertyOf(type) ?? "Content";
                var (dp, _, _) = ResolveDependencyProperty(type, contentProperty, el, defaultNs);
                sb.AppendLine($"{pad}{f}.SetValue({dp}, \"{EscapeString(text.Trim())}\");");
            }

            return f;
        }

        /// <summary>Whether <paramref name="type"/> (or a base class) declares the static <c>NameProperty</c> field; true when the type cannot be resolved.</summary>
        private static bool IsDependencyProperty(string type, string name)
        {
            var start = TypeSymbol(type);
            if (start is null)
            {
                return true;
            }

            for (var t = start; t is not null; t = t.BaseType)
            {
                if (t.GetMembers(name + "Property").OfType<IFieldSymbol>().Any(f => f.IsStatic))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether <paramref name="ownerName"/> names <paramref name="type"/> or one of its base classes.</summary>
        private static bool IsPropertyOfTypeOrBase(string type, string ownerName)
        {
            for (var t = TypeSymbol(type); t is not null; t = t.BaseType)
            {
                if (t.Name == ownerName)
                {
                    return true;
                }
            }

            // An unresolvable type cannot be judged: keep the existing behaviour rather than report a construct that may be fine.
            return TypeSymbol(type) is null;
        }

        private static string? ContentPropertyOf(string type)
        {
            for (var t = TypeSymbol(type); t is not null; t = t.BaseType)
            {
                var attribute = t.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "ContentPropertyAttribute");
                if (attribute is { ConstructorArguments.Length: 1 } && attribute.ConstructorArguments[0].Value is string name)
                {
                    return name;
                }
            }
            return null;
        }
    }
}
