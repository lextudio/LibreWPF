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

        /// <summary>
        /// The C# for a literal (or x:Static / x:Null / x:Type / StaticResource) attribute value assigned to the settable CLR property
        /// <paramref name="name"/> of <paramref name="elementType"/>, or null when the property's type cannot be resolved, the value is
        /// another markup extension, or it is a binding (those keep their own handling).
        /// </summary>
        private static string? TypedPropertyValue(string raw, string elementType, string name, XElement scope, string defaultNs)
        {
            IPropertySymbol? property = null;
            for (var t = TypeSymbol(elementType); t is not null && property is null; t = t.BaseType)
            {
                property = t.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(p => !p.IsStatic && !p.IsIndexer);
            }

            if (property is not { SetMethod: { } setter } || setter.DeclaredAccessibility != Accessibility.Public)
            {
                return null;
            }

            var value = raw.Trim();
            if (value.StartsWith("{", StringComparison.Ordinal) && !value.StartsWith("{}", StringComparison.Ordinal))
            {
                var inner = value.Substring(1).TrimStart();
                if (inner.StartsWith("x:Static", StringComparison.Ordinal) || inner.StartsWith("x:Null", StringComparison.Ordinal) || inner.StartsWith("x:Type", StringComparison.Ordinal))
                {
                    return MarkupValueCode(value, scope, defaultNs, null);
                }
                if (inner.StartsWith("StaticResource", StringComparison.Ordinal) && MarkupValueCode(value, scope, defaultNs, null) is { } find)
                {
                    // A key that exists only on the instance is looked up lazily by the caller (SetResourceReference), not cast here.
                    return find.StartsWith("new global::System.Windows.DynamicResourceExtension", StringComparison.Ordinal)
                        ? null
                        : $"({property.Type.ToDisplayString(s_typeFormat)}){find}";
                }
                return null;
            }

            return ValueCode(raw, property.Type, scope, defaultNs, null);
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
                    // A key of a .Resources of an enclosing element exists only on the instance: look it up when the value is applied.
                    return IsLocalResourceKey(arg, scope)
                        ? $"new global::System.Windows.DynamicResourceExtension({ResourceKeyCode(arg, scope, defaultNs)})"
                        : $"this.FindResource({ResourceKeyCode(arg, scope, defaultNs)})";
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
            if (key.StartsWith("{ComponentResourceKey", StringComparison.Ordinal))
            {
                // {ComponentResourceKey TypeInTargetAssembly={x:Type T}, ResourceId=Id}, or the two as positional arguments
                var body = key.Substring("{ComponentResourceKey".Length).TrimEnd('}').Trim();
                var named = ParseBinding("{Binding " + body + "}");   // the same "a, b=c" argument syntax
                var type = named.TryGetValue("TypeInTargetAssembly", out var t) ? t : named.TryGetValue("Path", out var first) ? first : null;
                var id = named.TryGetValue("ResourceId", out var r) ? r : null;
                if (type is not null && id is not null)
                {
                    return $"new global::System.Windows.ComponentResourceKey(typeof(global::{ResolveTypeNameInScope(StripXType(type), scope, defaultNs)}), {ResourceKeyCode(id, scope, defaultNs)})";
                }
                ReportNotGenerated(scope, "a ComponentResourceKey without TypeInTargetAssembly and ResourceId");
            }
            return $"\"{EscapeString(key)}\"";
        }

        private static readonly Dictionary<string, string> s_staticValueClasses = new(StringComparer.Ordinal)
        {
            ["System.Windows.FontWeight"] = "System.Windows.FontWeights",
            ["System.Windows.FontStyle"] = "System.Windows.FontStyles",
            ["System.Windows.FontStretch"] = "System.Windows.FontStretches",
            ["System.Windows.TextDecorationCollection"] = "System.Windows.TextDecorations",
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
                var enumMembers = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst).Select(f => f.Name).ToList();
                var members = value.Split(',').Select(v =>
                {
                    var name = v.Trim();
                    return $"global::{typeName}.{enumMembers.FirstOrDefault(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase)) ?? name}";
                });
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
                    return $"global::System.Windows.Media.Brushes.{StaticMemberName("System.Windows.Media.Brushes", value)}";
                case "System.Windows.Media.Color":
                    return value.StartsWith("#", StringComparison.Ordinal) && ColorCode(value) is { } hex ? hex : $"global::System.Windows.Media.Colors.{StaticMemberName("System.Windows.Media.Colors", value)}";
                case "System.Windows.Thickness":
                    return FourDoubles(value, "global::System.Windows.Thickness");
                case "System.Windows.CornerRadius":
                    return FourDoubles(value, "global::System.Windows.CornerRadius");
                case "System.Windows.Media.Transform":
                case "System.Windows.Media.MatrixTransform":
                {
                    // The Transform converter reads "m11,m12,m21,m22,offsetX,offsetY" (a Matrix) and "Identity".
                    if (string.Equals(value.Trim(), "Identity", StringComparison.OrdinalIgnoreCase))
                    {
                        return "global::System.Windows.Media.Transform.Identity";
                    }
                    var m = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (m.Length == 6 && m.All(n => double.TryParse(n, NumberStyles.Float, inv, out _)))
                    {
                        return $"new global::System.Windows.Media.MatrixTransform({string.Join(", ", m.Select(n => double.Parse(n, NumberStyles.Float, inv).ToString("R", inv) + "d"))})";
                    }
                    break;
                }
                case "System.Windows.Media.Animation.KeySpline":
                    return FourDoubles(value, "global::System.Windows.Media.Animation.KeySpline");
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
                case "System.Windows.Media.Geometry":
                    return $"global::System.Windows.Media.Geometry.Parse(\"{EscapeString(value)}\")";
                case "System.Windows.Point":
                case "System.Windows.Size":
                case "System.Windows.Vector":
                {
                    var p = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return p.Length == 2 ? $"new global::{typeName}({p[0]}d, {p[1]}d)" : $"\"{EscapeString(value)}\"";
                }
                case "System.Windows.Media.DoubleCollection":
                {
                    var numbers = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return numbers.All(n => double.TryParse(n, NumberStyles.Float, inv, out _))
                        ? $"new global::System.Windows.Media.DoubleCollection(new double[] {{ {string.Join(", ", numbers.Select(n => n + "d"))} }})"
                        : $"\"{EscapeString(value)}\"";
                }
                case "System.Windows.Media.PointCollection":
                {
                    var numbers = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (numbers.Length % 2 != 0 || !numbers.All(n => double.TryParse(n, NumberStyles.Float, inv, out _)))
                    {
                        return $"\"{EscapeString(value)}\"";
                    }
                    var points = Enumerable.Range(0, numbers.Length / 2).Select(i => $"new global::System.Windows.Point({numbers[2 * i]}d, {numbers[2 * i + 1]}d)");
                    return $"new global::System.Windows.Media.PointCollection(new global::System.Windows.Point[] {{ {string.Join(", ", points)} }})";
                }
                case "System.Windows.Input.ICommand":
                case "System.Windows.Input.RoutedCommand":
                case "System.Windows.Input.RoutedUICommand":
                {
                    // CommandConverter: Owner.Command names a static property of a command class (NavigationCommands.BrowseBack).
                    var dot = value.LastIndexOf('.');
                    if (dot > 0)
                    {
                        var ownerType = TypeSymbol(ResolveTypeNameInScope(value.Substring(0, dot), scope, defaultNs));
                        var memberName = value.Substring(dot + 1);
                        if (ownerType?.GetMembers(memberName).Any(m => m.IsStatic) == true)
                        {
                            return $"{ownerType.ToDisplayString(s_typeFormat)}.{memberName}";
                        }
                    }
                    break;
                }
                case "char":
                    if (value.Length == 1)
                    {
                        return value[0] == '\'' || value[0] == '\\' ? $"'\\{value[0]}'" : $"'{value}'";
                    }
                    break;
                case "System.Windows.Media.Animation.RepeatBehavior":
                {
                    var repeat = "global::System.Windows.Media.Animation.RepeatBehavior";
                    if (string.Equals(value, "Forever", StringComparison.OrdinalIgnoreCase)) return repeat + ".Forever";
                    if (value.EndsWith("x", StringComparison.OrdinalIgnoreCase) && double.TryParse(value.TrimEnd('x', 'X'), NumberStyles.Float, inv, out var count))
                    {
                        return $"new {repeat}({count.ToString("R", inv)}d)";
                    }
                    return TimeSpan.TryParse(value, inv, out var every) ? $"new {repeat}(global::System.TimeSpan.FromTicks({every.Ticks}))" : $"\"{EscapeString(value)}\"";
                }
                case "System.Windows.Media.ImageSource":
                case "System.Windows.Media.Imaging.BitmapSource":
                case "System.Windows.Media.Imaging.BitmapImage":
                    // ImageSourceConverter at build time: an image file addressed by a (pack) URI, read when it is first needed.
                    return $"new global::System.Windows.Media.Imaging.BitmapImage(new global::System.Uri(\"{EscapeString(value)}\", global::System.UriKind.RelativeOrAbsolute))";
                case "System.Uri":
                    return $"new global::System.Uri(\"{EscapeString(value)}\", global::System.UriKind.RelativeOrAbsolute)";
                case "System.Windows.PropertyPath":
                    return $"new global::System.Windows.PropertyPath(\"{EscapeString(value)}\")";
                case "System.Windows.Media.Animation.KeyTime":
                {
                    var keyTime = "global::System.Windows.Media.Animation.KeyTime";
                    if (string.Equals(value, "Uniform", StringComparison.OrdinalIgnoreCase)) return keyTime + ".Uniform";
                    if (string.Equals(value, "Paced", StringComparison.OrdinalIgnoreCase)) return keyTime + ".Paced";
                    if (value.EndsWith("%", StringComparison.Ordinal) && double.TryParse(value.TrimEnd('%'), NumberStyles.Float, inv, out var percent))
                    {
                        return $"{keyTime}.FromPercent({(percent / 100).ToString("R", inv)}d)";
                    }
                    return TimeSpan.TryParse(value, inv, out var at) ? $"{keyTime}.FromTimeSpan(global::System.TimeSpan.FromTicks({at.Ticks}))" : $"\"{EscapeString(value)}\"";
                }
                case "System.Windows.Rect":
                {
                    var r = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return r.Length == 4 ? $"new global::System.Windows.Rect({r[0]}d, {r[1]}d, {r[2]}d, {r[3]}d)"
                        : string.Equals(value, "Empty", StringComparison.OrdinalIgnoreCase) ? "global::System.Windows.Rect.Empty" : $"\"{EscapeString(value)}\"";
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

            // "None" is the empty collection; TextDecorations has no member of that name.
            if (typeName == "System.Windows.TextDecorationCollection" && string.Equals(value.Trim(), "None", StringComparison.OrdinalIgnoreCase))
            {
                return "new global::System.Windows.TextDecorationCollection()";
            }
            if (typeName is not null && s_staticValueClasses.TryGetValue(typeName, out var statics))
            {
                return $"global::{statics}.{StaticMemberName(statics, value)}";
            }

            // A text for a value of a type this generator cannot convert would be assigned as a string and fail when the style is sealed
            // (or worse, silently): say so at the line.
            if (type is not null && type.SpecialType is not (SpecialType.System_Object or SpecialType.System_String))
            {
                ReportNotGenerated(scope, $"the text '{value}' for a value of type {typeName} (its TypeConverter is not known to the generator)");
            }

            return $"\"{EscapeString(value)}\"";
        }

        // The control a TemplateBinding inside <paramref name="scope"/> reads: the TargetType of the nearest enclosing template.
        private static string TemplateTargetOf(XElement scope, string defaultNs)
        {
            for (var e = scope; e is not null; e = e.Parent)
            {
                if (e.Name.LocalName.EndsWith("Template", StringComparison.Ordinal) && e.Attribute("TargetType") is { } target)
                {
                    return ResolveTypeNameInScope(StripXType(target.Value), e, defaultNs);
                }
                if (e.Name.LocalName.EndsWith("DataTemplate", StringComparison.Ordinal))
                {
                    return "System.Windows.Controls.ContentPresenter";
                }
            }
            return "System.Windows.Controls.Control";
        }

        // {TemplateBinding P} for an object the template factory does not create: Binding(RelativeSource TemplatedParent, Path = P).
        private static string TemplateBindingAsBinding(string markup, XElement scope, string defaultNs)
        {
            var inner = markup.Trim().TrimStart('{').TrimEnd('}').Trim().Substring("TemplateBinding".Length).Trim();
            if (inner.StartsWith("Property=", StringComparison.Ordinal))
            {
                inner = inner.Substring("Property=".Length);
            }
            var source = ResolveDependencyProperty(TemplateTargetOf(scope, defaultNs), inner, scope, defaultNs).Code;
            return "new global::System.Windows.Data.Binding { RelativeSource = global::System.Windows.Data.RelativeSource.TemplatedParent, Path = new global::System.Windows.PropertyPath(" + source + ") }";
        }

        // The spelling of a static member written in any case in XAML (the converters ignore case): Brushes.white -> Brushes.White.
        private static string StaticMemberName(string typeName, string written)
        {
            var type = TypeSymbol(typeName);
            var name = type?.GetMembers().Where(m => m.IsStatic && (m is IPropertySymbol || m is IFieldSymbol))
                .Select(m => m.Name).FirstOrDefault(m => string.Equals(m, written, StringComparison.OrdinalIgnoreCase));
            return name ?? written;
        }

        // <Binding A="x" B="y"/> -> {Binding A=x, B=y}; <Binding.RelativeSource><RelativeSource Mode="Self"/></Binding.RelativeSource> -> RelativeSource={RelativeSource Mode=Self}
        private static string ElementAsBindingMarkup(XElement el, bool rulesAreGenerated = false)
        {
            var items = el.Attributes()
                .Where(a => a.Name.NamespaceName.Length == 0 && !a.Name.LocalName.StartsWith("xmlns", StringComparison.Ordinal))
                .Select(a => a.Name.LocalName + "=" + a.Value).ToList();
            foreach (var child in el.Elements().Where(c => c.Name.LocalName.StartsWith("Binding.", StringComparison.Ordinal)))
            {
                var inner = child.Elements().FirstOrDefault();
                if (inner is { Name.LocalName: "RelativeSource" })
                {
                    items.Add("RelativeSource={RelativeSource " + string.Join(", ", inner.Attributes().Where(a => a.Name.NamespaceName.Length == 0).Select(a => a.Name.LocalName + "=" + a.Value)) + "}");
                }
                else if (!(rulesAreGenerated && child.Name.LocalName == "Binding.ValidationRules"))
                {
                    // (The caller that creates a Binding object adds its ValidationRules itself; the other callers drop them, so they are reported.)
                    ReportNotGenerated(child, child.Name.LocalName);
                }
            }
            return "{Binding " + string.Join(", ", items) + "}";
        }

        // Button.Click / Click -> global::System.Windows.Controls.Button.ClickEvent (the static RoutedEvent field, on the type or a base type)
        private static string RoutedEventCode(string written, XElement scope, string defaultNs, string fallbackOwner)
        {
            var dot = written.LastIndexOf('.');
            var owner = dot > 0 ? ResolveTypeNameInScope(written.Substring(0, dot), scope, defaultNs) : fallbackOwner;
            var name = dot > 0 ? written.Substring(dot + 1) : written;
            for (var t = TypeSymbol(owner); t is not null; t = t.BaseType)
            {
                var field = t.GetMembers(name + "Event").OfType<IFieldSymbol>().FirstOrDefault(f => f.IsStatic);
                if (field is not null)
                {
                    return $"{field.ContainingType.ToDisplayString(s_typeFormat)}.{name}Event";
                }
            }
            return $"global::{owner}.{name}Event";
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
            var parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => string.Equals(p.Trim(), "Auto", StringComparison.OrdinalIgnoreCase) ? "double.NaN" : p.Trim() + "d").ToArray();
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
                    EmitResourceChildren(sb, $"{v}.Resources", child.Elements().ToList(), defaultNs, sourcePath, indent, ref localId);
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
                var kind = trigger.Name.LocalName;
                string? t = null;
                switch (kind)
                {
                    case "Trigger":
                    {
                        var property = trigger.Attribute("Property")?.Value;
                        if (property is null)
                        {
                            continue;
                        }
                        var sourceName = trigger.Attribute("SourceName")?.Value;
                        var (dp, type, _) = ResolveDependencyProperty(OwnerOf(sourceName, namedTypes, targetType), property, trigger, defaultNs);
                        t = $"__Trigger_{++localId}";
                        sb.AppendLine($"{pad}var {t} = new global::System.Windows.Trigger {{ Property = {dp}, Value = {ValueCode(trigger.Attribute("Value")?.Value ?? string.Empty, type, trigger, defaultNs, targetType)} }};");
                        if (sourceName is not null)
                        {
                            sb.AppendLine($"{pad}{t}.SourceName = \"{EscapeString(sourceName)}\";");
                        }
                        break;
                    }
                    case "EventTrigger":
                    {
                        var routedEvent = trigger.Attribute("RoutedEvent")?.Value;
                        if (routedEvent is null)
                        {
                            ReportNotGenerated(trigger, "an EventTrigger without RoutedEvent");
                            continue;
                        }
                        var sourceName = trigger.Attribute("SourceName")?.Value;
                        t = $"__EventTrigger_{++localId}";
                        sb.AppendLine($"{pad}var {t} = new global::System.Windows.EventTrigger {{ RoutedEvent = {RoutedEventCode(routedEvent, trigger, defaultNs, OwnerOf(sourceName, namedTypes, targetType))} }};");
                        if (sourceName is not null)
                        {
                            sb.AppendLine($"{pad}{t}.SourceName = \"{EscapeString(sourceName)}\";");
                        }
                        break;
                    }
                    case "DataTrigger":
                    {
                        t = $"__DataTrigger_{++localId}";
                        sb.AppendLine($"{pad}var {t} = new global::System.Windows.DataTrigger {{ Binding = {ConditionBinding(trigger, defaultNs, targetType)}, Value = {ValueCode(trigger.Attribute("Value")?.Value ?? string.Empty, null, trigger, defaultNs, targetType)} }};");
                        break;
                    }
                    case "MultiTrigger":
                    case "MultiDataTrigger":
                    {
                        t = $"__{kind}_{++localId}";
                        sb.AppendLine($"{pad}var {t} = new global::System.Windows.{kind}();");
                        foreach (var conditions in trigger.Elements().Where(e => e.Name.LocalName == kind + ".Conditions"))
                        {
                            foreach (var condition in conditions.Elements().Where(c => c.Name.LocalName == "Condition"))
                            {
                                EmitCondition(sb, $"{t}.Conditions", condition, kind == "MultiDataTrigger", targetType, namedTypes, defaultNs, indent);
                            }
                        }
                        break;
                    }
                    default:
                        sb.AppendLine($"{pad}// {kind} is not generated yet");
                        ReportNotGenerated(trigger, kind);
                        continue;
                }

                foreach (var child in trigger.Elements())
                {
                    var local = child.Name.LocalName;
                    if (local == kind + ".Setters")
                    {
                        foreach (var setter in child.Elements())
                        {
                            EmitSetter(sb, $"{t}.Setters", setter, targetType, namedTypes, defaultNs, sourcePath, indent, ref localId);
                        }
                    }
                    else if (local == "Setter")
                    {
                        EmitSetter(sb, $"{t}.Setters", child, targetType, namedTypes, defaultNs, sourcePath, indent, ref localId);
                    }
                    else if (local == kind + ".Conditions" || local == kind + ".Binding")
                    {
                        // handled above
                    }
                    else if (local == kind + ".EnterActions" || local == kind + ".ExitActions" || local == "EventTrigger.Actions")
                    {
                        var actions = local.Substring(local.LastIndexOf('.') + 1);
                        foreach (var action in child.Elements())
                        {
                            var actionVar = GenerateElement(sb, action, defaultNs, sourcePath, indent, ref localId);
                            sb.AppendLine($"{pad}{t}.{actions}.Add({actionVar});");
                        }
                    }
                    else if (kind == "EventTrigger" && !local.Contains("."))
                    {
                        // EventTrigger's content property is Actions.
                        var actionVar = GenerateElement(sb, child, defaultNs, sourcePath, indent, ref localId);
                        sb.AppendLine($"{pad}{t}.Actions.Add({actionVar});");
                    }
                    else
                    {
                        sb.AppendLine($"{pad}// {local} is not generated yet");
                        ReportNotGenerated(child, local);
                    }
                }
                sb.AppendLine($"{pad}{collection}.Add({t});");
            }
        }

        private static string OwnerOf(string? sourceName, Dictionary<string, string>? namedTypes, string targetType) =>
            sourceName is not null && namedTypes is not null && namedTypes.TryGetValue(sourceName, out var named) ? named : targetType;

        private static string ConditionBinding(XElement element, string defaultNs, string targetType)
        {
            var binding = element.Attribute("Binding")?.Value;
            if (binding is null)
            {
                // <DataTrigger.Binding><Binding Path="..."/></DataTrigger.Binding>
                var asElement = element.Elements().FirstOrDefault(e => e.Name.LocalName == element.Name.LocalName + ".Binding")?.Elements().FirstOrDefault();
                if (asElement is not null && asElement.Name.LocalName == "Binding")
                {
                    return BindingCode(ElementAsBindingMarkup(asElement), asElement, false);
                }
                ReportNotGenerated(element, "a data condition without a Binding attribute or a Binding element");
                return "null";
            }
            return ValueCode(binding, null, element, defaultNs, targetType);
        }

        // A Condition of a MultiTrigger (Property [+ SourceName]) or of a MultiDataTrigger (Binding).
        private static void EmitCondition(StringBuilder sb, string collection, XElement condition, bool data, string targetType,
            Dictionary<string, string>? namedTypes, string defaultNs, int indent)
        {
            var pad = new string(' ', indent);
            if (data)
            {
                var value = ValueCode(condition.Attribute("Value")?.Value ?? string.Empty, null, condition, defaultNs, targetType);
                sb.AppendLine($"{pad}{collection}.Add(new global::System.Windows.Condition {{ Binding = {ConditionBinding(condition, defaultNs, targetType)}, Value = {value} }});");
                return;
            }

            var property = condition.Attribute("Property")?.Value;
            if (property is null)
            {
                return;
            }
            var sourceName = condition.Attribute("SourceName")?.Value;
            var (dp, type, _) = ResolveDependencyProperty(OwnerOf(sourceName, namedTypes, targetType), property, condition, defaultNs);
            var source = sourceName is null ? string.Empty : $", SourceName = \"{EscapeString(sourceName)}\"";
            sb.AppendLine($"{pad}{collection}.Add(new global::System.Windows.Condition {{ Property = {dp}, Value = {ValueCode(condition.Attribute("Value")?.Value ?? string.Empty, type, condition, defaultNs, targetType)}{source} }});");
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

            // A TemplateBinding names a property of the templated parent: the control for a ControlTemplate, the ContentPresenter that
            // hosts the content for a DataTemplate.
            templateTarget ??= typeName.EndsWith("ControlTemplate", StringComparison.Ordinal) ? "System.Windows.Controls.Control"
                : typeName.EndsWith("DataTemplate", StringComparison.Ordinal) ? "System.Windows.Controls.ContentPresenter"
                : "System.Windows.FrameworkElement";

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
                    EmitResourceChildren(sb, $"{v}.Resources", child.Elements().ToList(), defaultNs, sourcePath, indent, ref localId);
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

            // Created by code, not by Activator.CreateInstance (NativeAOT keeps no constructor nobody calls).
            if (TypeSymbol(type) is { IsAbstract: false } created
                && created.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
            {
                sb.AppendLine($"{pad}{f}.SetCreator(static () => new global::{type}());");
            }

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
                // theme:ScrollChrome.ScrollGlyph="...": an attached property written with a prefix names its owner through that prefix.
                var written = ns.StartsWith("clr-namespace:", StringComparison.Ordinal) && el.GetPrefixOfNamespace(attr.Name.Namespace) is { Length: > 0 } writtenPrefix
                    ? writtenPrefix + ":" + local : local;
                var (dp, propertyType, name) = ResolveDependencyProperty(type, written, el, defaultNs);
                if (value.StartsWith("{Binding", StringComparison.Ordinal))
                {
                    sb.AppendLine($"{pad}{f}.SetBinding({dp}, {BindingCode(value, el, name == "DataContext")});");
                }
                else if (value.StartsWith("{DynamicResource", StringComparison.Ordinal))
                {
                    var key = value.Substring("{DynamicResource".Length, value.Length - "{DynamicResource".Length - 1).Trim();
                    sb.AppendLine($"{pad}{f}.SetResourceReference({dp}, {ResourceKeyCode(key, el, defaultNs)});");
                }
                else if (value.StartsWith("{StaticResource", StringComparison.Ordinal)
                         && IsLocalResourceKey(value.Substring("{StaticResource".Length, value.Length - "{StaticResource".Length - 1).Trim(), el))
                {
                    // Defined in a .Resources of an enclosing element of this very template: it exists only on the instance, so it is
                    // looked up there when the node is instantiated (what BAML's deferred StaticResource does).
                    var key = value.Substring("{StaticResource".Length, value.Length - "{StaticResource".Length - 1).Trim();
                    sb.AppendLine($"{pad}{f}.SetResourceReference({dp}, {ResourceKeyCode(key, el, defaultNs)});");
                }
                else
                {
                    sb.AppendLine($"{pad}{f}.SetValue({dp}, {ValueCode(value, propertyType, el, defaultNs, templateTarget)});");
                }
            }

            var initializerBody = new StringBuilder();
            var initializerParameter = $"__o_{++localId}";
            var initializerOwner = $"__x_{localId}";
            foreach (var child in el.Elements())
            {
                var local = child.Name.LocalName;
                if (local.Contains("."))
                {
                    var propertyName = local.Substring(local.IndexOf('.') + 1);
                    var ownProperty = IsPropertyOfTypeOrBase(type, local.Substring(0, local.IndexOf('.')));

                    // <Border.Child><Grid .../></Border.Child>: the content property written as a property element. Its elements are the
                    // template's own visual children, so they are factory nodes (named, TemplateBound, triggered), like any other child.
                    if (ownProperty && propertyName == ContentPropertyOf(type) && child.Elements().Any()
                        && child.Elements().All(c => IsFrameworkElementType(ResolveTypeName(c, defaultNs))))
                    {
                        foreach (var content in child.Elements())
                        {
                            var contentNode = FactoryNode(sb, content, defaultNs, sourcePath, pad, ref localId, templateTarget);
                            if (contentNode is not null)
                            {
                                sb.AppendLine($"{pad}{f}.AppendChild({contentNode});");
                            }
                        }
                        continue;
                    }

                    var clr = ownProperty ? FindClrProperty(TypeSymbol(type), propertyName) : null;

                    // <Track.Thumb><Thumb x:Name="Thumb"/></Track.Thumb>: an element-valued CLR property (not a dependency property, not the
                    // content property). The element is still a node of the template (named, bound, targeted by triggers); the factory is
                    // told how to attach it to its parent.
                    // <Grid.RowDefinitions><RowDefinition x:Name="r0" Height="{DynamicResource ...}"/></Grid.RowDefinitions>: a collection property
                    // of elements. Each item is a factory node, added to the collection when it is created: it can be named (targeted by a
                    // trigger or animation) and its resource references and bindings are applied once it is in the tree, which code run
                    // while the item is still detached could not do.
                    if (ownProperty && clr is not null && IsCollectionType(clr.Type) && child.Elements().Any()
                        && child.Elements().All(c => IsFrameworkElementType(ResolveTypeName(c, defaultNs))))
                    {
                        foreach (var item in child.Elements())
                        {
                            var itemType = ResolveTypeName(item, defaultNs)!;
                            var itemNode = FactoryNode(sb, item, defaultNs, sourcePath, pad, ref localId, templateTarget);
                            if (itemNode is not null)
                            {
                                sb.AppendLine($"{pad}{itemNode}.SetAttacher((__p, __c) => ((global::{type})__p).{propertyName}.Add((global::{itemType})__c));");
                                sb.AppendLine($"{pad}{f}.AppendChild({itemNode});");
                            }
                        }
                        continue;
                    }

                    if (ownProperty && clr is { SetMethod: { DeclaredAccessibility: Accessibility.Public } } && !IsDependencyProperty(type, propertyName)
                        && IsFrameworkElementType(clr.Type.ToDisplayString()) && child.Elements().Count() == 1
                        && IsAssignableChild(child.Elements().First(), clr.Type, defaultNs))
                    {
                        var attached = FactoryNode(sb, child.Elements().First(), defaultNs, sourcePath, pad, ref localId, templateTarget);
                        if (attached is not null)
                        {
                            sb.AppendLine($"{pad}{attached}.SetAttacher((__p, __c) => ((global::{type})__p).{propertyName} = (global::{clr.Type.ToDisplayString()})__c);");
                            sb.AppendLine($"{pad}{f}.AppendChild({attached});");
                        }
                        continue;
                    }
                    if (ownProperty && IsDependencyProperty(type, propertyName) && child.Elements().Count() == 1
                        && clr is not null && !IsCollectionType(clr.Type) && clr.Type.ToDisplayString() != "System.Windows.ResourceDictionary")
                    {
                        // <Border.Background><SolidColorBrush .../></Border.Background>: a value object for a dependency property, built once and shared.
                        var (dp, _, _) = ResolveDependencyProperty(type, propertyName, el, defaultNs);
                        var valueVar = GenerateElement(sb, child.Elements().First(), defaultNs, sourcePath, pad.Length, ref localId);
                        sb.AppendLine($"{pad}{f}.SetValue({dp}, {valueVar});");
                    }
                    else if (!TryEmitPropertyElement(initializerBody, initializerOwner, type, child, defaultNs, sourcePath, pad.Length + 4, ref localId))
                    {
                        // Neither a dependency-property value nor something the factory's initializer can fill in.
                        ReportNotGenerated(child, local + " in a template");
                    }
                    continue;
                }

                var node = FactoryNode(sb, child, defaultNs, sourcePath, pad, ref localId, templateTarget);
                if (node is not null)
                {
                    sb.AppendLine($"{pad}{f}.AppendChild({node});");
                }
            }

            if (initializerBody.Length > 0)
            {
                sb.AppendLine($"{pad}{f}.SetInitializer({initializerParameter} =>");
                sb.AppendLine($"{pad}{{");
                sb.AppendLine($"{pad}    var {initializerOwner} = (global::{type}){initializerParameter};");
                sb.Append(initializerBody);
                sb.AppendLine($"{pad}}});");
            }

            var text = el.Nodes().OfType<XText>().Select(t => t.Value).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (text is not null && !el.Elements().Any())
            {
                var contentProperty = ContentPropertyOf(type) ?? "Content";
                if (contentProperty == "Inlines") contentProperty = "Text"; // text in a TextBlock is its Text (Inlines is not a dependency property)
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

        // ---- members by shape --------------------------------------------------------------------------------------------------

        // The static XProperty field declared on the type or a base type, or null.
        private static IFieldSymbol? FindDependencyPropertyField(ITypeSymbol? type, string name)
        {
            for (var t = type; t is not null; t = t.BaseType)
            {
                var field = t.GetMembers(name + "Property").OfType<IFieldSymbol>().FirstOrDefault(f => f.IsStatic);
                if (field is not null)
                {
                    return field;
                }
            }
            return null;
        }

        private static IPropertySymbol? FindClrProperty(ITypeSymbol? type, string name)
        {
            for (var t = type; t is not null; t = t.BaseType)
            {
                var property = t.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(p => !p.IsStatic && !p.IsIndexer);
                if (property is not null)
                {
                    return property;
                }
            }
            return null;
        }

        private static bool HasMethod(ITypeSymbol type, string name, int parameters)
        {
            for (var t = type; t is not null; t = t.BaseType)
            {
                if (t.GetMembers(name).OfType<IMethodSymbol>().Any(m => !m.IsStatic && m.Parameters.Length == parameters && m.DeclaredAccessibility == Accessibility.Public))
                {
                    return true;
                }
            }
            return type.AllInterfaces.Any(i => i.GetMembers(name).OfType<IMethodSymbol>().Any(m => m.Parameters.Length == parameters));
        }

        // A collection WPF fills from child elements: enumerable with a public one-argument Add (not a string).
        private static bool IsCollectionType(ITypeSymbol? type) =>
            type is not null && type.SpecialType != SpecialType.System_String && type.TypeKind != TypeKind.Array
            && type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable)
            && HasMethod(type, "Add", 1);

        private static bool IsAssignableChild(XElement child, ITypeSymbol propertyType, string defaultNs)
        {
            var childType = TypeSymbol(ResolveTypeName(child, defaultNs));
            return childType is not null && s_context!.Compilation is Microsoft.CodeAnalysis.CSharp.CSharpCompilation csharp && csharp.ClassifyConversion(childType, propertyType).IsImplicit;
        }

        /// <summary>Child elements put into a property: its collection filled with Add (cleared first so a re-run is idempotent), or the single value assigned.</summary>
        private static void EmitIntoProperty(StringBuilder sb, string owner, IPropertySymbol property, List<XElement> children, string defaultNs,
            string sourcePath, int indent, ref int localId)
        {
            var pad = new string(' ', indent);
            var access = $"{owner}.{property.Name}";
            // <Foo.X><MultiBinding .../></Foo.X>: the binding is applied to the dependency property; it is not the property's value.
            if (children.Count == 1 && children[0].Name.LocalName is "Binding" or "MultiBinding" or "PriorityBinding"
                && FindDependencyPropertyField(property.ContainingType, property.Name) is { } bindingTarget)
            {
                var binding = GenerateElement(sb, children[0], defaultNs, sourcePath, indent, ref localId);
                sb.AppendLine($"{pad}global::System.Windows.Data.BindingOperations.SetBinding({owner}, {bindingTarget.ContainingType.ToDisplayString(s_typeFormat)}.{property.Name}Property, {binding});");
                return;
            }
            if (children.Count == 1 && property.SetMethod is { DeclaredAccessibility: Accessibility.Public } && IsAssignableChild(children[0], property.Type, defaultNs))
            {
                var only = GenerateElement(sb, children[0], defaultNs, sourcePath, indent, ref localId);
                sb.AppendLine($"{pad}{access} = {only};");
                return;
            }

            if (IsCollectionType(property.Type))
            {
                if (HasMethod(property.Type, "Clear", 0))
                {
                    sb.AppendLine($"{pad}{access}.Clear();");
                }
                foreach (var child in children)
                {
                    var childVar = GenerateElement(sb, child, defaultNs, sourcePath, indent, ref localId);
                    sb.AppendLine($"{pad}{access}.Add({childVar});");
                }
                return;
            }

            if (children.Count == 1 && property.SetMethod is not null)
            {
                var only = GenerateElement(sb, children[0], defaultNs, sourcePath, indent, ref localId);
                sb.AppendLine($"{pad}{access} = {only};");
            }
        }

        /// <summary><c>&lt;Owner.Prop&gt;...&lt;/Owner.Prop&gt;</c> decided by the type of the property (collection or single value, or element text).</summary>
        private static bool TryEmitPropertyElement(StringBuilder sb, string owner, string elementType, XElement propertyElement, string defaultNs,
            string sourcePath, int indent, ref int localId)
        {
            var name = propertyElement.Name.LocalName;
            var property = FindClrProperty(TypeSymbol(elementType), name.Substring(name.LastIndexOf('.') + 1));
            if (property is null)
            {
                return TryEmitAttachedPropertyElement(sb, owner, propertyElement, defaultNs, sourcePath, indent, ref localId);
            }

            var pad = new string(' ', indent);
            var children = propertyElement.Elements().ToList();
            if (property.Type.ToDisplayString() == "System.Windows.ResourceDictionary")
            {
                if (children.Count == 1 && ResolveTypeName(children[0], defaultNs) == "System.Windows.ResourceDictionary")
                {
                    return false; // an explicit <ResourceDictionary>: assigned by the general path
                }
                EmitResourceChildren(sb, $"{owner}.{property.Name}", children, defaultNs, sourcePath, indent, ref localId);
                return true;
            }

            if (children.Count == 0)
            {
                var text = propertyElement.Value.Trim();
                if (text.Length == 0 || property.SetMethod is not { DeclaredAccessibility: Accessibility.Public })
                {
                    return false;
                }
                sb.AppendLine($"{pad}{owner}.{property.Name} = {ValueCode(text, property.Type, propertyElement, defaultNs, null)};");
                return true;
            }

            EmitIntoProperty(sb, owner, property, children, defaultNs, sourcePath, indent, ref localId);
            return true;
        }

        // Is <paramref name="key"/> (the text written after {StaticResource) the x:Key of a resource in the .Resources of an element enclosing
        // <paramref name="scope"/>, or of the Style / template that holds it? Such a resource exists on the instance (or the style), not in the theme dictionary.
        private static bool IsLocalResourceKey(string key, XElement scope)
        {
            key = key.Trim();
            for (var a = scope; a is not null; a = a.Parent)
            {
                foreach (var resources in a.Elements().Where(e => e.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)))
                {
                    // A resource cannot refer to itself or to one defined after it in the same dictionary (the key is not there yet when
                    // it is read): if the reference is inside one of these resources, only the ones before that resource count.
                    foreach (var r in resources.Elements())
                    {
                        if (scope.AncestorsAndSelf().Any(e => ReferenceEquals(e, r)))
                        {
                            break;
                        }
                        if ((r.Attribute(XName.Get("Key", XamlNamespace)) ?? r.Attribute("Key"))?.Value.Trim() == key)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // x:Key'd (or implicitly TargetType/DataType-keyed) children added to a ResourceDictionary expression.
        private static void EmitResourceChildren(StringBuilder sb, string dictionary, List<XElement> children, string defaultNs, string sourcePath, int indent, ref int localId)
        {
            var pad = new string(' ', indent);
            // In a generated theme dictionary the resources being built are visible to the ones after them (BAML's lexical scope): the
            // dictionary class looks them up in __scopes before its own entries.
            var scoped = s_context is { LooseDictionary: true };
            if (scoped)
            {
                sb.AppendLine($"{pad}__scopes.Add({dictionary});");
            }
            foreach (var child in children)
            {
                var value = GenerateElement(sb, child, defaultNs, sourcePath, indent, ref localId);
                EmitKeyedResource(sb, dictionary, child, value, defaultNs, pad);
            }
            if (scoped)
            {
                sb.AppendLine($"{pad}__scopes.RemoveAt(__scopes.Count - 1);");
            }
        }

        /// <summary>
        /// One resource added to a dictionary: by its x:Key (a string, or a typed key written {x:Type}/{x:Static}/{ComponentResourceKey}), or
        /// implicitly: a Style by its TargetType, a DataTemplate by a DataTemplateKey (WPF's implicit data template lookup asks for
        /// DataTemplateKey(type), not the bare type).
        /// </summary>
        private static void EmitKeyedResource(StringBuilder sb, string dictionary, XElement child, string value, string defaultNs, string pad)
        {
            var key = child.Attribute(XName.Get("Key", XamlNamespace)) ?? child.Attribute("Key");
            if (key is not null)
            {
                sb.AppendLine($"{pad}{dictionary}[{ResourceKeyCode(key.Value, child, defaultNs)}] = {value};");
                return;
            }

            var typeKey = child.Attribute("TargetType") ?? child.Attribute("DataType");
            if (typeKey is null)
            {
                sb.AppendLine($"{pad}// resource without x:Key skipped: {child.Name.LocalName}");
                ReportNotGenerated(child, $"the resource <{child.Name.LocalName}> without x:Key or TargetType/DataType");
                return;
            }

            var resolved = ResolveTypeNameInScope(StripXType(typeKey.Value), child, defaultNs);
            var byDataType = typeKey.Name.LocalName == "DataType" && child.Attribute("TargetType") is null;
            sb.AppendLine(byDataType
                ? $"{pad}{dictionary}[new global::System.Windows.DataTemplateKey(typeof(global::{resolved}))] = {value};"
                : $"{pad}{dictionary}[typeof(global::{resolved})] = {value};");
        }

        // <VisualStateManager.VisualStateGroups>: the collection the static Get{Prop} returns gets the children added, or Set{Prop} gets the value.
        private static bool TryEmitAttachedPropertyElement(StringBuilder sb, string owner, XElement propertyElement, string defaultNs, string sourcePath, int indent, ref int localId)
        {
            var local = propertyElement.Name.LocalName;
            var dot = local.LastIndexOf('.');
            var ownerType = TypeSymbol(ResolveTypeNameInScope(local.Substring(0, dot), propertyElement, defaultNs));
            var name = local.Substring(dot + 1);
            if (ownerType is null)
            {
                return false;
            }

            var pad = new string(' ', indent);
            var children = propertyElement.Elements().ToList();
            var ownerName = ownerType.ToDisplayString(s_typeFormat);
            var getter = ownerType.GetMembers("Get" + name).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);
            var setter = ownerType.GetMembers("Set" + name).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 2);

            if (getter is not null && IsCollectionType(getter.ReturnType))
            {
                foreach (var child in children)
                {
                    var childVar = GenerateElement(sb, child, defaultNs, sourcePath, indent, ref localId);
                    sb.AppendLine($"{pad}{ownerName}.Get{name}({owner}).Add({childVar});");
                }
                return true;
            }

            if (setter is not null && children.Count == 1)
            {
                var childVar = GenerateElement(sb, children[0], defaultNs, sourcePath, indent, ref localId);
                sb.AppendLine($"{pad}{ownerName}.Set{name}({owner}, {childVar});");
                return true;
            }

            return false;
        }

        /// <summary>The child elements of <paramref name="typeName"/> by its [ContentProperty] (or, without one, by being a collection itself).</summary>
        private static bool TryEmitSymbolContent(StringBuilder sb, string owner, string typeName, List<XElement> children, string defaultNs,
            string sourcePath, int indent, ref int localId)
        {
            var type = TypeSymbol(typeName);
            if (type is null)
            {
                return false;
            }

            var contentProperty = ContentPropertyOf(typeName);
            if (contentProperty is null)
            {
                if (!IsCollectionType(type))
                {
                    return false;
                }
                var pad = new string(' ', indent);
                foreach (var child in children)
                {
                    var childVar = GenerateElement(sb, child, defaultNs, sourcePath, indent, ref localId);
                    sb.AppendLine($"{pad}{owner}.Add({childVar});");
                }
                return true;
            }

            var property = FindClrProperty(type, contentProperty);
            if (property is null)
            {
                return false;
            }
            EmitIntoProperty(sb, owner, property, children, defaultNs, sourcePath, indent, ref localId);
            return true;
        }

        private static readonly HashSet<string> s_textLiteralTypes = new(StringComparer.Ordinal)
        {
            "System.Windows.Thickness", "System.Windows.CornerRadius", "System.Windows.GridLength", "System.Windows.Point", "System.Windows.Size",
            "System.Windows.Vector", "System.Windows.Rect", "System.Windows.Media.Color", "System.Windows.Media.Geometry", "System.Windows.Media.StreamGeometry",
            "System.Windows.Media.FontFamily", "System.Windows.Duration", "System.TimeSpan",
        };

        // An element with only text and no attributes whose type is a value written in the converter's text form.
        private static string? TextOnlyLiteral(XElement el, string typeName, string defaultNs)
        {
            var symbol = TypeSymbol(typeName);
            var primitive = symbol is { SpecialType: SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_Int16
                or SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Single or SpecialType.System_Double
                or SpecialType.System_String };  // exactly the primitives LiteralCode converts
            if ((!s_textLiteralTypes.Contains(typeName) && !primitive && symbol is not { TypeKind: TypeKind.Enum }) || el.Elements().Any())
            {
                return null;
            }
            var text = el.Value.Trim();
            // <sys:String x:Key="K"/> is the empty string (string has no parameterless constructor).
            if (text.Length == 0 && symbol is { SpecialType: SpecialType.System_String }
                && !el.Attributes().Any(a => a.Name.NamespaceName != "http://www.w3.org/2000/xmlns/" && a.Name.LocalName != "xmlns" && a.Name.NamespaceName != XamlNamespace))
            {
                return "\"\"";
            }
            if (text.Length == 0 || el.Attributes().Any(a => a.Name.NamespaceName != "http://www.w3.org/2000/xmlns/" && a.Name.LocalName != "xmlns" && a.Name.NamespaceName != XamlNamespace))
            {
                return null;
            }
            return LiteralCode(text, TypeSymbol(typeName), el, defaultNs);
        }

        private static bool IsNamedElement(XElement element) =>
            (element.Attribute(XName.Get("Name", XamlNamespace)) ?? element.Attribute("Name")) is not null;

        // A FrameworkElement or FrameworkContentElement: what a FrameworkElementFactory can create as a node.
        private static bool IsFrameworkElementType(string? type)
        {
            for (var t = TypeSymbol(type); t is not null; t = t.BaseType)
            {
                if (t.ToDisplayString() is "System.Windows.FrameworkElement" or "System.Windows.FrameworkContentElement")
                {
                    return true;
                }
            }
            return false;
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
