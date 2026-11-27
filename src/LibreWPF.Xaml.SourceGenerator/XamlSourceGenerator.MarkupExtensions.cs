#nullable enable
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
        // A project's own markup extension written as an attribute value, `{core:Localize ${res:Foo}}` or `{sd:OptionBinding Type.Prop, Converter=...}`.
        // The extension is constructed in the generated code (arguments converted to the constructor's and the properties' CLR types at build time),
        // and __XsgApplyExtension asks it for its value with an IServiceProvider that has the IProvideValueTarget, as the XAML reader does.
        // Null (the value stays as it was written) when the type is not a MarkupExtension of the compilation or an argument cannot be converted.
        private static string? CustomMarkupExtensionCode(string value, XElement el)
        {
            var context = s_context;
            var text = value.Trim();
            if (context is null || text.Length < 3 || text[0] != '{' || text[text.Length - 1] != '}')
            {
                return null;
            }

            var body = text.Substring(1, text.Length - 2).Trim();
            var space = IndexOfWhitespace(body);
            var written = space < 0 ? body : body.Substring(0, space);
            var rest = space < 0 ? string.Empty : body.Substring(space + 1).Trim();
            if (written.IndexOf(':') <= 0 || written.StartsWith("x:", StringComparison.Ordinal))
            {
                return null;
            }

            INamedTypeSymbol? type = null;
            foreach (var candidate in new[] { written + "Extension", written })
            {
                var resolved = ResolveTypeNameInScope(candidate, el, PresentationNamespace);
                if (TypeSymbol(resolved) is { } symbol && DerivesFrom(symbol, "System.Windows.Markup.MarkupExtension"))
                {
                    type = symbol;
                    break;
                }
            }

            if (type is null)
            {
                return null;
            }

            var positional = new List<string>();
            var named = new List<(string Name, string Value)>();
            foreach (var part in SplitTopLevel(rest))
            {
                var eq = IndexOfTopLevelEquals(part);
                if (eq > 0)
                {
                    named.Add((part.Substring(0, eq).Trim(), part.Substring(eq + 1).Trim()));
                }
                else if (part.Length > 0)
                {
                    positional.Add(part.Trim());
                }
            }

            var constructor = type.InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == positional.Count)
                .OrderBy(c => c.Parameters.Count(p => p.Type.SpecialType != SpecialType.System_String)).FirstOrDefault();
            if (constructor is null)
            {
                return null;
            }

            var arguments = new List<string>();
            for (var i = 0; i < positional.Count; i++)
            {
                if (LiteralFor(constructor.Parameters[i].Type, positional[i], el) is not { } argument)
                {
                    return null;
                }
                arguments.Add(argument);
            }

            var initializers = new List<string>();
            foreach (var (name, v) in named)
            {
                var property = FindSettableProperty(type, name);
                if (property is null || LiteralFor(property.Type, v, el) is not { } code)
                {
                    return null;
                }
                initializers.Add($"{name} = {code}");
            }

            context.UsesMarkupExtensions = true;
            var init = initializers.Count == 0 ? string.Empty : " { " + string.Join(", ", initializers) + " }";
            return $"new {type.ToDisplayString(s_typeFormat)}({string.Join(", ", arguments)}){init}";
        }

        private static IPropertySymbol? FindSettableProperty(INamedTypeSymbol type, string name)
        {
            for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            {
                if (t.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(p => p.SetMethod is { DeclaredAccessibility: Accessibility.Public }) is { } property)
                {
                    return property;
                }
            }

            return null;
        }

        private static bool DerivesFrom(ITypeSymbol type, string fullName)
        {
            for (var t = type; t is not null; t = t.BaseType)
            {
                if (t.ToDisplayString() == fullName)
                {
                    return true;
                }
            }

            return false;
        }

        // The C# for a markup argument written as text, in the type the constructor or property takes. Null when that type is not one of these.
        private static string? LiteralFor(ITypeSymbol type, string written, XElement el)
        {
            var text = Unquote(written);
            // {StaticResource key} in an argument: the resource of the page (the converter of an OptionBinding), cast to the parameter's type.
            if (written.StartsWith("{StaticResource", StringComparison.Ordinal) && written.EndsWith("}", StringComparison.Ordinal))
            {
                var key = written.Substring("{StaticResource".Length).TrimOneBrace();
                return $"({type.ToDisplayString(s_typeFormat)}){ResourceLookup(key, el)}";
            }
            if (written.Trim() == "{x:Null}")
            {
                return "null";
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_String:
                case SpecialType.System_Object:
                    return "\"" + EscapeString(text) + "\"";
                case SpecialType.System_Boolean:
                    return bool.TryParse(text, out var flag) ? (flag ? "true" : "false") : null;
                case SpecialType.System_Int32:
                    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i32) ? i32.ToString(CultureInfo.InvariantCulture) : null;
                case SpecialType.System_Double:
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d.ToString("R", CultureInfo.InvariantCulture) + "d" : null;
            }

            if (type.TypeKind == TypeKind.Enum && type.GetMembers().OfType<IFieldSymbol>().Any(f => f.Name == text))
            {
                return type.ToDisplayString(s_typeFormat) + "." + text;
            }

            if (type.ToDisplayString() == "System.Type")
            {
                var inner = text.StartsWith("{x:Type", StringComparison.Ordinal) ? text.Substring("{x:Type".Length).Trim().TrimEnd('}').Trim() : text;
                return $"typeof(global::{ResolveTypeNameInScope(inner, el, PresentationNamespace)})";
            }

            return null;
        }

        private static int IndexOfWhitespace(string s)
        {
            for (var i = 0; i < s.Length; i++)
            {
                if (char.IsWhiteSpace(s[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        // Commas outside nested braces and quotes: `${res:A,B}` and `'a,b'` are one argument.
        private static List<string> SplitTopLevel(string s)
        {
            var parts = new List<string>();
            var depth = 0;
            char quote = '\0';
            var start = 0;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                }
                else if (c == '\'' || c == '"') quote = c;
                else if (c == '{') depth++;
                else if (c == '}') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(s.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }

            parts.Add(s.Substring(start).Trim());
            return parts;
        }

        private static int IndexOfTopLevelEquals(string s)
        {
            var depth = 0;
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}') depth--;
                else if (s[i] == '=' && depth == 0) return i;
                else if (s[i] == '\'' || s[i] == '"') return -1;
            }

            return -1;
        }

        private static void AppendMarkupExtensionHelperIfUsed(StringBuilder sb)
        {
            if (s_context is not { UsesMarkupExtensions: true })
            {
                return;
            }

            sb.AppendLine("""
                        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
                        private static void __XsgApplyExtension(object target, global::System.Windows.DependencyProperty? property, string name, global::System.Windows.Markup.MarkupExtension extension)
                        {
                            var clr = property is null ? target.GetType().GetProperty(name) : null;
                            var provider = new __XsgExtensionServiceProvider(target, property is not null ? property : clr);
                            var value = extension.ProvideValue(provider);
                            var dependencyObject = target as global::System.Windows.DependencyObject;
                            if (property is not null && dependencyObject is not null)
                            {
                                if (value is global::System.Windows.Data.BindingBase binding)
                                {
                                    global::System.Windows.Data.BindingOperations.SetBinding(dependencyObject, property, binding);
                                    return;
                                }
                                if (value is string text && !property.PropertyType.IsAssignableFrom(typeof(string)))
                                {
                                    value = global::System.ComponentModel.TypeDescriptor.GetConverter(property.PropertyType).ConvertFromInvariantString(text);
                                }
                                dependencyObject.SetValue(property, value);
                            }
                            else if (clr is not null)
                            {
                                if (value is string text && !clr.PropertyType.IsAssignableFrom(typeof(string)))
                                {
                                    value = global::System.ComponentModel.TypeDescriptor.GetConverter(clr.PropertyType).ConvertFromInvariantString(text);
                                }
                                clr.SetValue(target, value);
                            }
                        }

                        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
                        // target/property: the owner of the collection the value goes into and the collection itself (what the XAML reader reports): a Binding that
                        // is an item of MultiBinding.Bindings is accepted by Binding.ProvideValue only then.
                        private static object? __XsgProvideValue(global::System.Windows.Markup.MarkupExtension extension, object? target = null, object? property = null) => extension.ProvideValue(new __XsgExtensionServiceProvider(target ?? extension, property));

                        private sealed class __XsgExtensionServiceProvider : global::System.IServiceProvider, global::System.Windows.Markup.IProvideValueTarget
                        {
                            public __XsgExtensionServiceProvider(object target, object? property) { TargetObject = target; TargetProperty = property; }
                            public object TargetObject { get; }
                            public object? TargetProperty { get; }
                            public object? GetService(global::System.Type serviceType) => serviceType == typeof(global::System.Windows.Markup.IProvideValueTarget) ? this : null;
                        }
                """);
        }
    }
}
