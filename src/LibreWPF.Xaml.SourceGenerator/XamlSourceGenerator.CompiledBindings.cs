// Licensed to the .NET Foundation under one or more agreements.
// LibreWPF XAML Source Generator: compiled bindings (LibreWpfCompiledBindings) and generated loose/theme ResourceDictionaries.

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
    public sealed partial class XamlSourceGenerator
    {
        private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        private const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        // What one Generate call needs besides the XAML: the compilation (to resolve x:DataType and binding paths), the switches,
        // and the diagnostics found while generating. One per output callback, on that callback's thread.
        private sealed class GenerationContext
        {
            public GenerationContext(Compilation compilation, XsgFeatures features, string sourcePath)
            {
                Compilation = compilation;
                Features = features;
                SourcePath = sourcePath;
            }

            public Compilation Compilation { get; }

            public XsgFeatures Features { get; }

            public string SourcePath { get; }

            // The type the generated code is emitted in (x:Class), for accessibility checks; null for a loose dictionary.
            public INamedTypeSymbol? Within { get; set; }

            // Set once a compiled binding is emitted: the generated class then needs its __XsgCompiledProperty helper.
            public bool UsesCompiledProperty { get; set; }

            // A loose ResourceDictionary: generated into a class that has no x:Name fields.
            public bool LooseDictionary { get; set; }

            // The resource path of the XAML file being generated (themes/generic.xaml, mainwindow.xaml): what a relative Source URI is resolved against.
            public string ResourcePath { get; set; } = string.Empty;

            // Set once a <ResourceDictionary Source="..."> is emitted: the generated class then needs its __XsgLoadDictionary helper.
            public bool UsesDictionaryLoader { get; set; }

            public List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();
        }

        [ThreadStatic]
        private static GenerationContext? s_context;

#pragma warning disable RS2008
        private static readonly DiagnosticDescriptor s_dataTypeNotFound = new DiagnosticDescriptor(
            "LWXSG0005",
            "x:DataType type not found",
            "{0}({1},{2}): x:DataType '{3}' does not name a type the compilation can see",
            "LibreWPF.Xaml",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_bindingPropertyNotFound = new DiagnosticDescriptor(
            "LWXSG0006",
            "Compiled binding path not found",
            "{0}({1},{2}): '{3}' has no readable, accessible instance property '{4}' (binding path '{5}', x:DataType '{6}')",
            "LibreWPF.Xaml",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_bindingNotCompiled = new DiagnosticDescriptor(
            "LWXSG0007",
            "Binding not compiled",
            "{0}({1},{2}): binding '{3}' is inside an x:DataType scope but is not compiled ({4}); it uses reflection at run time and is not trim/NativeAOT safe",
            "LibreWPF.Xaml",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // A construct the generator does not handle yet. It used to be left out of the generated code with only a comment, so a
        // theme or binding quietly lost a trigger, a setter or a binding property; now it is a located diagnostic (a warning for
        // JIT apps; the NativeAOT mode of the SDK is meant to make it an error: there the missing construct would be a missing
        // behaviour with no fallback).
        private static readonly DiagnosticDescriptor s_excluded = new DiagnosticDescriptor(
            "LWXSG0009",
            "Resource excluded",
            "{0}({1},{2}): the resource for '{3}' is left out because LibreWpfXsgExcludeTargetTypes names it",
            "LibreWPF.Xaml",
            DiagnosticSeverity.Info,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor s_notGenerated = new DiagnosticDescriptor(
            "LWXSG0008",
            "Construct not generated",
            "{0}({1},{2}): {3} is not generated yet and is left out of the generated code",
            "LibreWPF.Xaml",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
#pragma warning restore RS2008

        /// <summary>Reports a construct that is left out of the generated code (see <see cref="s_notGenerated"/>).</summary>
        private static void ReportNotGenerated(XObject? at, string what)
        {
            var context = s_context;
            if (context is not null && at is not null)
            {
                Report(context, s_notGenerated, at, what);
            }
        }

        // x:DataType and x:CompileBindings are generator directives, never properties.
        private static bool IsCompiledBindingDirective(XAttribute attribute) =>
            attribute.Name.NamespaceName == XamlNamespace
            && (attribute.Name.LocalName == "DataType" || attribute.Name.LocalName == "CompileBindings");

        // ---- binding markup -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// C# for a <c>{Binding ...}</c> value. With LibreWpfCompiledBindings on and an x:DataType in scope, a plain property path is
        /// compiled: every step becomes a generated PropertyDescriptor with typed delegates, passed to WPF as an early-bound
        /// PropertyPath parameter, so the binding engine never reflects over the source type. Everything else about the binding (mode,
        /// converters, validation, templates) is the stock Binding.
        /// </summary>
        private static string BindingCode(string value, XElement? owner, bool isDataContext)
        {
            var parts = ParseBinding(value);
            parts.TryGetValue("Path", out var path);
            path = path?.Trim() ?? string.Empty;

            var initializers = new List<string>();
            foreach (var kv in parts)
            {
                var code = BindingPropertyCode(kv.Key, kv.Value, owner);
                if (code is not null)
                {
                    initializers.Add(code);
                }
                else if (kv.Key != "Path")
                {
                    ReportNotGenerated(owner, $"binding property '{kv.Key}={kv.Value}' (binding '{value}')");
                }
            }

            var pathCode = CompiledPathCode(path, parts, owner, isDataContext, value) ?? ParenthesizedPathCode(path, owner);
            if (pathCode is null && owner is not null && path.StartsWith("(", StringComparison.Ordinal) && path.Contains(":"))
            {
                // A prefix has a meaning only while the XAML is being read; the run-time path parser has no namespace context, so this one cannot resolve.
                ReportNotGenerated(owner, $"the binding path '{path}', whose prefixed member cannot be resolved at build time (an inaccessible or unknown type); the run-time path parser does not know the prefix, so the binding will not resolve");
            }
            string head;
            if (pathCode is not null)
            {
                head = "new global::System.Windows.Data.Binding()";
                initializers.Insert(0, "Path = " + pathCode);
            }
            else
            {
                head = $"new global::System.Windows.Data.Binding(\"{EscapeString(path)}\")";
            }

            return initializers.Count == 0 ? head : head + " { " + string.Join(", ", initializers) + " }";
        }

        // {Binding A.B, Mode=TwoWay, Converter={StaticResource c}} -> Path=A.B, Mode=TwoWay, Converter={StaticResource c}
        private static Dictionary<string, string> ParseBinding(string value)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var body = value.Trim();
            if (body.StartsWith("{", StringComparison.Ordinal))
            {
                body = body.Substring(1);
            }
            if (body.EndsWith("}", StringComparison.Ordinal))
            {
                body = body.Substring(0, body.Length - 1);
            }
            body = body.Trim();
            if (body.StartsWith("Binding", StringComparison.Ordinal))
            {
                body = body.Substring("Binding".Length);
            }

            var items = new List<string>();
            var depth = 0;
            var quote = '\0';
            var start = 0;
            for (var i = 0; i < body.Length; i++)
            {
                var c = body[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c is '\'' or '"') quote = c;
                else if (c == '{') depth++;
                else if (c == '}') depth--;
                else if (c == ',' && depth == 0)
                {
                    items.Add(body.Substring(start, i - start));
                    start = i + 1;
                }
            }
            items.Add(body.Substring(start));

            foreach (var raw in items)
            {
                var item = raw.Trim();
                if (item.Length == 0)
                {
                    continue;
                }
                var eq = IndexOfTopLevel(item, '=');
                if (eq < 0)
                {
                    result["Path"] = item; // positional
                }
                else
                {
                    result[item.Substring(0, eq).Trim()] = item.Substring(eq + 1).Trim();
                }
            }

            return result;
        }

        private static int IndexOfTopLevel(string text, char ch)
        {
            var depth = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}') depth--;
                else if (text[i] == ch && depth == 0) return i;
            }
            return -1;
        }

        private static string Unquote(string v)
        {
            v = v.Trim();
            if (v.Length >= 2 && (v[0] == '\'' || v[0] == '"') && v[v.Length - 1] == v[0])
            {
                v = v.Substring(1, v.Length - 2);
            }
            if (v.StartsWith("{}", StringComparison.Ordinal))
            {
                v = v.Substring(2);
            }
            return v;
        }

        private static string? BindingPropertyCode(string name, string value, XElement? owner)
        {
            // {x:Static Type.Member} for what is an object, a converter or a relative source in code.
            if (owner is not null && value.TrimStart().StartsWith("{x:Static", StringComparison.Ordinal)
                && name is "Source" or "RelativeSource" or "Converter" or "ConverterParameter" or "FallbackValue" or "TargetNullValue")
            {
                var staticNs = owner.GetDefaultNamespace().NamespaceName;
                var staticCode = StaticCode(value.Trim().Substring("{x:Static".Length).TrimEnd('}').Trim(), owner, staticNs.Length == 0 ? PresentationNamespace : staticNs);
                return name == "Converter" ? $"Converter = (global::System.Windows.Data.IValueConverter){staticCode}" : $"{name} = {staticCode}";
            }

            switch (name)
            {
                case "Path":
                    return null;
                case "Mode":
                    return $"Mode = global::System.Windows.Data.BindingMode.{value.Trim()}";
                case "UpdateSourceTrigger":
                    return $"UpdateSourceTrigger = global::System.Windows.Data.UpdateSourceTrigger.{value.Trim()}";
                case "StringFormat":
                case "ElementName":
                case "BindingGroupName":
                case "XPath":
                    return $"{name} = \"{EscapeString(Unquote(value))}\"";
                case "FallbackValue":
                case "TargetNullValue":
                case "ConverterParameter":
                    if (value.Trim() == "{x:Null}")
                    {
                        return $"{name} = null";
                    }
                    if (value.TrimStart().StartsWith("{StaticResource", StringComparison.Ordinal))
                    {
                        return $"{name} = this.FindResource(\"{EscapeString(ResourceKeyOf(value))}\")";
                    }
                    return $"{name} = \"{EscapeString(Unquote(value))}\"";
                case "Converter":
                    if (value.TrimStart().StartsWith("{StaticResource", StringComparison.Ordinal))
                    {
                        return $"Converter = (global::System.Windows.Data.IValueConverter)this.FindResource(\"{EscapeString(ResourceKeyOf(value))}\")";
                    }
                    return null;
                case "Source":
                    // A static resource is the one source that can be named without reflection.
                    return value.TrimStart().StartsWith("{StaticResource", StringComparison.Ordinal)
                        ? $"Source = this.FindResource(\"{EscapeString(ResourceKeyOf(value))}\")"
                        : null;
                case "ConverterCulture":
                    return value.TrimStart().StartsWith("{", StringComparison.Ordinal)
                        ? null
                        : $"ConverterCulture = global::System.Globalization.CultureInfo.GetCultureInfo(\"{EscapeString(Unquote(value))}\")";
                case "Delay":
                    return int.TryParse(value.Trim(), out var delay) ? $"Delay = {delay}" : null;
                case "IsAsync":
                case "NotifyOnSourceUpdated":
                case "NotifyOnTargetUpdated":
                case "NotifyOnValidationError":
                case "ValidatesOnDataErrors":
                case "ValidatesOnExceptions":
                case "ValidatesOnNotifyDataErrors":
                case "BindsDirectlyToSource":
                    return bool.TryParse(value.Trim(), out var flag) ? $"{name} = {(flag ? "true" : "false")}" : null;
                case "RelativeSource":
                    return RelativeSourceCode(value, owner);
                default:
                    return null;
            }
        }

        private static string ResourceKeyOf(string markup)
        {
            var inner = markup.Trim().TrimStart('{').TrimEnd('}').Trim();
            var space = inner.IndexOf(' ');
            return space < 0 ? string.Empty : inner.Substring(space + 1).Trim();
        }

        private static string? RelativeSourceCode(string value, XElement? owner)
        {
            var inner = value.Trim().TrimStart('{').TrimEnd('}').Trim();
            if (!inner.StartsWith("RelativeSource", StringComparison.Ordinal))
            {
                return null;
            }
            inner = inner.Substring("RelativeSource".Length).Trim();
            var items = inner.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            string mode = "FindAncestor";
            string? ancestorType = null;
            string? level = null;
            foreach (var item in items)
            {
                var eq = item.IndexOf('=');
                if (eq < 0)
                {
                    mode = item;
                    continue;
                }
                var key = item.Substring(0, eq).Trim();
                var v = item.Substring(eq + 1).Trim();
                if (key == "Mode") mode = v;
                else if (key == "AncestorType")
                {
                    var t = v.StartsWith("{x:Type", StringComparison.Ordinal) ? v.Substring("{x:Type".Length).Trim().TrimEnd('}').Trim() : v;
                    // A prefixed type (local:Foo) resolves through the element's own xmlns declarations, like every other type name.
                    ancestorType = owner is not null
                        ? ResolveTypeNameInScope(t, owner, PresentationNamespace)
                        : ResolveTypeNameFromName(t, PresentationNamespace) ?? t;
                }
                else if (key == "AncestorLevel") level = v;
            }

            if (mode == "FindAncestor")
            {
                return ancestorType is null
                    ? null
                    : $"RelativeSource = new global::System.Windows.Data.RelativeSource(global::System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(global::{ancestorType}), {level ?? "1"})";
            }
            return $"RelativeSource = new global::System.Windows.Data.RelativeSource(global::System.Windows.Data.RelativeSourceMode.{mode})";
        }

        // ---- compiled path ------------------------------------------------------------------------------------------------------

        private static readonly Regex s_identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

        // (Owner.Property) or (prefix:Owner.Property)
        private static readonly Regex s_attachedSegment = new Regex(@"^\(\s*([A-Za-z_][A-Za-z0-9_:]*)\.([A-Za-z_][A-Za-z0-9_]*)\s*\)$", RegexOptions.CultureInvariant);

        // A.B.(Owner.Prop).C split at the dots outside parentheses; null for an indexer.
        private static List<string>? SplitPath(string path)
        {
            var result = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < path.Length; i++)
            {
                var c = path[i];
                if (c == '[' || c == ']')
                {
                    return null;
                }
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == '.' && depth == 0)
                {
                    result.Add(path.Substring(start, i - start));
                    start = i + 1;
                }
            }
            result.Add(path.Substring(start));
            return result;
        }

        private static readonly SymbolDisplayFormat s_typeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

        /// <summary>
        /// The type a binding with an explicit RelativeSource or ElementName reads from, or null when it has neither. A null Type means the
        /// binding has one of them but its type cannot be told (Description says why).
        /// </summary>
        private static (ITypeSymbol? Type, string Description)? KnownSourceType(Dictionary<string, string> parts, XElement owner)
        {
            var defaultNs = owner.GetDefaultNamespace().NamespaceName;
            if (defaultNs.Length == 0)
            {
                defaultNs = PresentationNamespace;
            }

            if (parts.TryGetValue("ElementName", out var elementName))
            {
                var name = Unquote(elementName);
                var named = owner.AncestorsAndSelf().Last().Descendants().FirstOrDefault(e =>
                    !e.Name.LocalName.Contains(".") && (e.Attribute(XName.Get("Name", XamlNamespace)) ?? e.Attribute("Name"))?.Value == name);
                var type = named is null ? null : TypeSymbol(ResolveTypeName(named, defaultNs));
                return (type, type is null ? $"no element named '{name}' of a known type is declared in this XAML file" : "ElementName=" + name);
            }

            if (!parts.TryGetValue("RelativeSource", out var relative))
            {
                return null;
            }

            relative = relative.Trim();
            string? mode = null;
            string? ancestorType = null;
            if (relative.StartsWith("{x:Static", StringComparison.Ordinal))
            {
                mode = relative.Substring("{x:Static".Length).TrimEnd('}').Trim().Split('.').Last();
            }
            else if (relative.StartsWith("{RelativeSource", StringComparison.Ordinal))
            {
                var arguments = ParseBinding("{Binding " + relative.Substring("{RelativeSource".Length).TrimEnd('}') + "}");
                mode = arguments.TryGetValue("Mode", out var m) ? m : arguments.TryGetValue("Path", out var positional) ? positional : null;
                ancestorType = arguments.TryGetValue("AncestorType", out var a) ? a : null;
                mode ??= ancestorType is not null ? "FindAncestor" : null;
            }

            ITypeSymbol? resolved = null;
            switch (mode)
            {
                case "Self":
                    resolved = TypeSymbol(SelfTypeOf(owner, defaultNs));
                    break;
                case "TemplatedParent":
                    resolved = TypeSymbol(TemplateTargetOf(owner, defaultNs));
                    break;
                case "FindAncestor" when ancestorType is not null:
                    resolved = TypeSymbol(ResolveTypeNameInScope(StripXType(ancestorType), owner, defaultNs));
                    break;
            }
            return (resolved, resolved is null ? $"the type of RelativeSource '{relative}' cannot be told at build time" : "RelativeSource " + mode);
        }

        /// <summary>
        /// The element a RelativeSource Self refers to: the element that carries the binding, found by going up past the binding itself
        /// (a MultiBinding child), property elements, Setters and triggers; for a style or template that is its TargetType.
        /// </summary>
        private static string? SelfTypeOf(XElement owner, string defaultNs)
        {
            for (var e = owner; e is not null; e = e.Parent)
            {
                var local = e.Name.LocalName;
                if (local.EndsWith("Template", StringComparison.Ordinal) || local == "Style")
                {
                    var target = e.Attribute("TargetType");
                    return target is null ? null : ResolveTypeNameInScope(StripXType(target.Value), e, defaultNs);
                }
                if (local.Contains(".") || local is "Binding" or "MultiBinding" or "PriorityBinding" or "Setter" or "Condition" || local.EndsWith("Trigger", StringComparison.Ordinal))
                {
                    continue;
                }
                return ResolveTypeName(e, defaultNs);
            }
            return null;
        }

        /// <summary>
        /// A path that is one parenthesized member, <c>(Owner.Member)</c> or <c>(prefix:Owner.Member)</c>: a dependency property or a static
        /// property, whatever the source of the binding. At run time that text is resolved with the default XAML namespace only, by reflection,
        /// and a prefix (<c>base:</c>, <c>framework:</c>) cannot be resolved at all; here it is resolved against the file's namespaces. A dependency
        /// property is passed as the DependencyProperty, a static property as a static PropertyInfo (what the binding engine treats as a static
        /// source). Null when the type or member is not found, or not accessible from the generated code (e.g. an internal type of another assembly).
        /// </summary>
        private static string? ParenthesizedPathCode(string path, XElement? owner)
        {
            var context = s_context;
            if (context is null || owner is null || !path.StartsWith("(", StringComparison.Ordinal))
            {
                return null;
            }

            var match = s_attachedSegment.Match(path.Trim());
            if (!match.Success)
            {
                return null;
            }

            var defaultNs = owner.GetDefaultNamespace().NamespaceName;
            var ownerType = TypeSymbol(ResolveTypeNameInScope(match.Groups[1].Value, owner, defaultNs.Length == 0 ? PresentationNamespace : defaultNs));
            var name = match.Groups[2].Value;
            if (ownerType is null || !context.Compilation.IsSymbolAccessibleWithin(ownerType, context.Compilation.Assembly))
            {
                return null;
            }

            if (FindDependencyPropertyField(ownerType, name) is { } dependencyProperty
                && context.Compilation.IsSymbolAccessibleWithin(dependencyProperty, context.Compilation.Assembly))
            {
                return $"new global::System.Windows.PropertyPath(\"(0)\", {dependencyProperty.ContainingType.ToDisplayString(s_typeFormat)}.{name}Property)";
            }

            var staticProperty = ownerType.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(p => p.IsStatic && p.GetMethod is { DeclaredAccessibility: Accessibility.Public });
            if (staticProperty is not null)
            {
                return $"new global::System.Windows.PropertyPath(\"(0)\", typeof({ownerType.ToDisplayString(s_typeFormat)}).GetProperty(\"{name}\", global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static)!)";
            }

            return null;
        }

        private static string? CompiledPathCode(string path, Dictionary<string, string> parts, XElement? owner, bool isDataContext, string markup)
        {
            var context = s_context;
            if (context is null || !context.Features.CompiledBindings || owner is null)
            {
                return null;
            }

            // Not a path to read from the DataContext's data: nothing to reflect over, or no property to compile.
            if (parts.ContainsKey("Source") || parts.ContainsKey("XPath") || path.Length == 0 || path == ".")
            {
                return null;
            }

            ITypeSymbol? rootType;
            string dataTypeText;
            if (KnownSourceType(parts, owner) is { } explicitSource)
            {
                // RelativeSource / ElementName: the source type is known from the template's TargetType, the AncestorType or the named element.
                if (explicitSource.Type is null)
                {
                    Report(context, s_bindingNotCompiled, owner, markup, explicitSource.Description);
                    return null;
                }
                rootType = explicitSource.Type;
                dataTypeText = explicitSource.Description;
            }
            else
            {
                var scope = FindDataTypeScope(owner, isDataContext);
                if (scope is null)
                {
                    return null;
                }

                var (scopeElement, text) = scope.Value;
                dataTypeText = text;
                rootType = ResolveDataType(dataTypeText, scopeElement, context.Compilation);
                if (rootType is null)
                {
                    Report(context, s_dataTypeNotFound, scopeElement, dataTypeText);
                    return null;
                }
            }

            var segments = SplitPath(path);
            if (segments is null || segments.Any(segment => !s_identifier.IsMatch(segment.Trim()) && !s_attachedSegment.IsMatch(segment.Trim())))
            {
                Report(context, s_bindingNotCompiled, owner, markup, "only a dotted path of property names (and (Owner.Property) attached properties) is compiled; indexers are not");
                return null;
            }

            var steps = new List<string>();
            ITypeSymbol current = rootType;
            for (var i = 0; i < segments.Count; i++)
            {
                var name = segments[i].Trim();

                // (Owner.Property): an attached dependency property is the early-bound path parameter itself.
                if (s_attachedSegment.Match(name) is { Success: true } attached)
                {
                    var attachedOwner = TypeSymbol(ResolveTypeNameInScope(attached.Groups[1].Value, owner, owner.GetDefaultNamespace().NamespaceName is { Length: > 0 } ns ? ns : PresentationNamespace));
                    var attachedField = FindDependencyPropertyField(attachedOwner, attached.Groups[2].Value);
                    var getter = attachedOwner?.GetMembers("Get" + attached.Groups[2].Value).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);
                    // (MenuItem.IsSubmenuOpen) is also how an ordinary dependency property is named: its value type is the CLR property's.
                    var attachedType = getter?.ReturnType ?? FindClrProperty(attachedOwner, attached.Groups[2].Value)?.Type;
                    if (attachedField is null || attachedType is null)
                    {
                        Report(context, s_bindingNotCompiled, owner, markup, $"the attached property '{name}' is not a dependency property the compilation can see");
                        return null;
                    }
                    steps.Add($"{attachedField.ContainingType.ToDisplayString(s_typeFormat)}.{attached.Groups[2].Value}Property");
                    current = attachedType;
                    continue;
                }

                var property = FindProperty(current, name, context);
                if (property is null)
                {
                    // Behind an explicit source (RelativeSource, ElementName) a member of a base-typed value (TemplatedParent, Parent, ...) exists
                    // only on the run-time type: that is reflection by nature, not a mistake. Behind x:DataType it is a mistake.
                    if (i > 0 && KnownSourceType(parts, owner) is not null)
                    {
                        Report(context, s_bindingNotCompiled, owner, markup, $"'{name}' is a member of the run-time type of a '{current.ToDisplayString()}' value");
                        return null;
                    }
                    Report(context, s_bindingPropertyNotFound, owner, current.ToDisplayString(), name, path, dataTypeText);
                    return null;
                }

                // A dependency property goes in as the DependencyProperty itself (the early-bound path parameter WPF understands): the
                // DP system raises the change notification, which a descriptor of our own could not.
                if (FindDependencyPropertyField(current, name) is { } dependencyProperty)
                {
                    steps.Add($"{dependencyProperty.ContainingType.ToDisplayString(s_typeFormat)}.{name}Property");
                    current = property.Type;
                    continue;
                }

                if (!IsNameable(property.Type) || !IsNameable(current))
                {
                    Report(context, s_bindingNotCompiled, owner, markup, $"the type of '{name}' cannot be named in generated code");
                    return null;
                }

                var ownerType = current.ToDisplayString(s_typeFormat);
                var valueType = property.Type.ToDisplayString(s_typeFormat);
                var writable = property.SetMethod is { IsInitOnly: false } setter
                    && current.IsReferenceType
                    && IsAccessible(setter, context);
                var setterCode = writable ? $"static (o, v) => o.{name} = v" : "null";
                steps.Add($"new __XsgCompiledProperty<{ownerType}, {valueType}>(\"{name}\", static o => o.{name}!, {setterCode})");
                context.UsesCompiledProperty = true;
                current = property.Type;
            }

            var pathText = string.Join(".", Enumerable.Range(0, steps.Count).Select(i => $"({i})"));
            return $"new global::System.Windows.PropertyPath(\"{pathText}\", {string.Join(", ", steps)})";
        }

        /// <summary>
        /// The nearest x:DataType that describes the DataContext a binding on <paramref name="element"/> reads. A template boundary
        /// without its own type ends the scope (its children read the item, not the outer data), as does an element that sets its own
        /// DataContext, and x:CompileBindings="False" turns compilation off below it. A binding OF DataContext reads the parent's.
        /// </summary>
        private static (XElement Element, string DataType)? FindDataTypeScope(XElement element, bool isDataContext)
        {
            for (var e = element; e is not null; e = e.Parent)
            {
                if (e.Name.LocalName.Contains("."))
                {
                    continue; // property element
                }

                var compile = e.Attribute(XName.Get("CompileBindings", XamlNamespace));
                if (compile is not null && string.Equals(compile.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var self = ReferenceEquals(e, element) && isDataContext;
                var dataType = e.Attribute(XName.Get("DataType", XamlNamespace));
                if (dataType is not null && !self)
                {
                    return (e, dataType.Value.Trim());
                }

                var isTemplate = e.Name.LocalName.EndsWith("Template", StringComparison.Ordinal);
                if (isTemplate)
                {
                    var templateType = e.Attribute("DataType");
                    return templateType is null ? null : (e, templateType.Value.Trim());
                }

                if (!self && SetsDataContext(e))
                {
                    return null;
                }
            }

            return null;
        }

        private static bool SetsDataContext(XElement e) =>
            e.Attributes().Any(a => a.Name.LocalName == "DataContext" && (a.Name.NamespaceName.Length == 0 || a.Name.NamespaceName == PresentationNamespace))
            || e.Elements().Any(c => c.Name.LocalName.EndsWith(".DataContext", StringComparison.Ordinal));

        private static ITypeSymbol? ResolveDataType(string text, XElement scope, Compilation compilation)
        {
            text = text.Trim();
            if (text.StartsWith("{x:Type", StringComparison.Ordinal))
            {
                text = text.Substring("{x:Type".Length).Trim().TrimEnd('}').Trim();
            }

            string metadataName;
            var colon = text.IndexOf(':');
            var prefix = colon < 0 ? string.Empty : text.Substring(0, colon);
            var local = colon < 0 ? text : text.Substring(colon + 1);
            var ns = prefix.Length == 0 ? scope.GetDefaultNamespace().NamespaceName : scope.GetNamespaceOfPrefix(prefix)?.NamespaceName;

            if (ns is not null && ns.StartsWith("clr-namespace:", StringComparison.Ordinal))
            {
                var clrNs = ns.Substring("clr-namespace:".Length);
                var semi = clrNs.IndexOf(';');
                if (semi >= 0) clrNs = clrNs.Substring(0, semi);
                metadataName = clrNs.Length == 0 ? local : clrNs + "." + local;
            }
            else if (ns is not null && ns.StartsWith("using:", StringComparison.Ordinal))
            {
                metadataName = ns.Substring("using:".Length) + "." + local;
            }
            else
            {
                metadataName = ResolveTypeNameFromName(local, PresentationNamespace) ?? local;
            }

            return compilation.GetTypeByMetadataName(metadataName) ?? compilation.GetTypeByMetadataName(text);
        }

        private static IPropertySymbol? FindProperty(ITypeSymbol type, string name, GenerationContext context)
        {
            IEnumerable<ITypeSymbol> candidates = BaseTypesAndSelf(type);
            if (type.TypeKind == TypeKind.Interface)
            {
                candidates = candidates.Concat(type.AllInterfaces);
            }

            foreach (var candidate in candidates)
            {
                foreach (var member in candidate.GetMembers(name))
                {
                    if (member is IPropertySymbol { IsStatic: false, IsIndexer: false, GetMethod: { } getter } property
                        && IsAccessible(getter, context))
                    {
                        return property;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<ITypeSymbol> BaseTypesAndSelf(ITypeSymbol type)
        {
            for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            {
                yield return t;
            }
        }

        private static bool IsAccessible(ISymbol symbol, GenerationContext context) =>
            context.Within is not null
                ? context.Compilation.IsSymbolAccessibleWithin(symbol, context.Within)
                : context.Compilation.IsSymbolAccessibleWithin(symbol, context.Compilation.Assembly);

        private static bool IsNameable(ITypeSymbol type) =>
            type.TypeKind != TypeKind.Error
            && type.TypeKind != TypeKind.TypeParameter
            && !type.IsAnonymousType
            && !(type is INamedTypeSymbol named && named.TypeArguments.Any(a => !IsNameable(a)))
            && !(type is IArrayTypeSymbol array && !IsNameable(array.ElementType));

        private static void Report(GenerationContext context, DiagnosticDescriptor descriptor, XObject at, params object[] args)
        {
            var info = (IXmlLineInfo)at;
            var all = new object[args.Length + 3];
            all[0] = context.SourcePath;
            all[1] = info.HasLineInfo() ? info.LineNumber : 0;
            all[2] = info.HasLineInfo() ? info.LinePosition : 0;
            Array.Copy(args, 0, all, 3, args.Length);
            context.Diagnostics.Add(Diagnostic.Create(descriptor, Location.None, all));
        }

        /// <summary>
        /// The run-time half of a compiled binding, emitted (privately, nested) into each generated class that uses one. A
        /// PropertyDescriptor is an accessor the stock PropertyPathWorker already accepts as an early-bound path parameter: it reads
        /// and writes through the descriptor, listens to INotifyPropertyChanged by the descriptor's Name, and honours IsReadOnly for
        /// TwoWay, so no change to the binding engine is needed and nothing is looked up by reflection.
        /// </summary>
        /// <summary>
        /// Where a <c>Source</c> URI written in the XAML file at <paramref name="currentResourcePath"/> points: the assembly name (null: this
        /// assembly) and the project-relative resource path, lower-case with '/' separators. Handles relative paths (resolved against the
        /// file's directory), rooted ones (<c>/Styles/a.xaml</c>), <c>pack://application:,,,/a.xaml</c> and <c>pack://application:,,,/Other;component/a.xaml</c>.
        /// </summary>
        private static (string? Assembly, string Path) ResolveResourceUri(string source, string currentResourcePath)
        {
            var text = source.Trim().Replace('\\', '/');
            string? assembly = null;
            const string packPrefix = "pack://application:,,,";
            if (text.StartsWith(packPrefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(packPrefix.Length);
            }
            var marker = text.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                var assemblyPart = text.Substring(0, marker).TrimStart('/');
                var version = assemblyPart.IndexOf(';');
                assembly = version >= 0 ? assemblyPart.Substring(0, version) : assemblyPart;
                text = "/" + text.Substring(marker + ";component/".Length);
            }

            if (!text.StartsWith("/", StringComparison.Ordinal))
            {
                var slash = currentResourcePath.LastIndexOf('/');
                text = (slash >= 0 ? currentResourcePath.Substring(0, slash + 1) : string.Empty) + text;
            }

            // normalise ./ and ../
            var parts = new List<string>();
            foreach (var part in text.Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == ".." ) { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(part);
            }
            return (assembly, string.Join("/", parts).ToLowerInvariant());
        }

        /// <summary>
        /// <c>&lt;ResourceDictionary Source="Styles/Brushes.xaml"/&gt;</c>: the dictionary file is compiled into this assembly (or the one named
        /// by a pack URI) and registered under its resource path, so the reference is a registry lookup at build-known coordinates, not a run-time
        /// load of a BAML or XAML resource.
        /// </summary>
        private static string DictionaryFromSource(string source)
        {
            var context = s_context!;
            var (assembly, path) = ResolveResourceUri(source, context.ResourcePath);
            context.UsesDictionaryLoader = true;
            var assemblyName = assembly ?? context.Features.ResourceAssemblyName ?? context.Compilation.AssemblyName ?? string.Empty;
            return $"__XsgLoadDictionary(\"{EscapeString(assemblyName)}\", \"{EscapeString(path)}\")";
        }

        private static void AppendDictionaryLoaderIfUsed(StringBuilder sb)
        {
            if (s_context is not { UsesDictionaryLoader: true })
            {
                return;
            }

            sb.AppendLine();
            sb.AppendLine("        private static global::System.Windows.ResourceDictionary __XsgLoadDictionary(string assembly, string path)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (global::System.Windows.Markup.XamlSourceGeneratedResources.TryCreateDictionary(assembly, path, out var dictionary)) return dictionary;");
            sb.AppendLine("            throw new global::System.IO.FileNotFoundException(\"The resource dictionary '\" + path + \"' is not compiled into '\" + assembly + \"' by the XAML source generator.\", path);");
            sb.AppendLine("        }");
        }

        private static void AppendCompiledPropertyHelperIfUsed(StringBuilder sb)
        {
            if (s_context is not { UsesCompiledProperty: true })
            {
                return;
            }

            sb.AppendLine("""
                        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
                        private sealed class __XsgCompiledProperty<TOwner, TValue> : global::System.ComponentModel.PropertyDescriptor
                        {
                            private readonly global::System.Func<TOwner, TValue> _get;
                            private readonly global::System.Action<TOwner, TValue>? _set;

                            public __XsgCompiledProperty(string name, global::System.Func<TOwner, TValue> get, global::System.Action<TOwner, TValue>? set)
                                : base(name, null)
                            {
                                _get = get;
                                _set = set;
                            }

                            public override global::System.Type ComponentType => typeof(TOwner);
                            public override global::System.Type PropertyType => typeof(TValue);
                            public override bool IsReadOnly => _set is null;
                            public override bool CanResetValue(object component) => false;
                            public override void ResetValue(object component) { }
                            public override bool ShouldSerializeValue(object component) => false;

                            public override object? GetValue(object? component) =>
                                component is TOwner owner ? _get(owner) : global::System.Windows.DependencyProperty.UnsetValue;

                            public override void SetValue(object? component, object? value)
                            {
                                if (_set is null)
                                {
                                    throw new global::System.InvalidOperationException("'" + Name + "' is read-only.");
                                }
                                if (component is TOwner owner)
                                {
                                    _set(owner, (TValue)value!);
                                    OnValueChanged(component, global::System.EventArgs.Empty);
                                }
                            }
                        }
                """);
        }

        // ---- loose ResourceDictionary (themes) ----------------------------------------------------------------------------------

        // The path a loose dictionary is registered under, relative to the project and lower-cased with '/' separators, the form WPF
        // uses for theme resources ("themes/generic.xaml", "themes/aero2.normalcolor.xaml").
        private static string ResourcePathOf(AdditionalText file, AnalyzerConfigOptionsProvider provider)
        {
            var options = provider.GetOptions(file);
            string? relative = null;
            if (options.TryGetValue("build_metadata.AdditionalFiles.Link", out var link) && !string.IsNullOrWhiteSpace(link))
            {
                relative = link;
            }
            else if ((provider.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var dir)
                      || provider.GlobalOptions.TryGetValue("build_property.projectdir", out dir))
                     && !string.IsNullOrWhiteSpace(dir))
            {
                var full = file.Path.Replace('\\', '/');
                var root = dir!.Replace('\\', '/').TrimEnd('/') + "/";
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    relative = full.Substring(root.Length);
                }
            }

            if (relative is null)
            {
                var full = file.Path.Replace('\\', '/');
                var themes = full.LastIndexOf("/themes/", StringComparison.OrdinalIgnoreCase);
                relative = themes >= 0 ? full.Substring(themes + 1) : full.Substring(full.LastIndexOf('/') + 1);
            }

            return relative.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        }

        /// <summary>
        /// A ResourceDictionary file without x:Class: generate a ResourceDictionary subclass that builds its content in code, and
        /// register it with <c>System.Windows.Markup.XamlSourceGeneratedResources</c> under its resource path. WPF's theme lookup
        /// (SystemResources: themes/generic.xaml, themes/&lt;theme&gt;.xaml) asks that registry before it looks for BAML, so a
        /// control library compiled by the generator supplies its default styles without BAML or System.Xaml at run time.
        /// </summary>
        private static void GenerateLooseResourceDictionary(SourceProductionContext spc, XElement root, string resourcePath, string sourcePath, string? assemblyName)
        {
            var defaultNs = root.GetDefaultNamespace().NamespaceName;
            if (string.IsNullOrEmpty(defaultNs))
            {
                defaultNs = PresentationNamespace;
            }

            s_context!.LooseDictionary = true;
            var className = "__XsgResourceDictionary_" + Regex.Replace(resourcePath, "[^A-Za-z0-9_]", "_");
            var body = new StringBuilder();
            var localId = 0;
            GenerateRootElement(body, root, defaultNs, sourcePath, 12, ref localId);
            // Generated element code can call the (non-existent) static FrameworkElement.FindResource; here it means this dictionary.
            var bodyText = body.ToString().Replace("global::System.Windows.FrameworkElement.FindResource(", "this.FindResource(");

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated by LibreWPF.Xaml.SourceGenerator />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable CA2255 // module initializer in a library: it only registers a factory");
            sb.AppendLine("namespace LibreWPF.Xaml.Generated");
            sb.AppendLine("{");
            sb.AppendLine($"    internal sealed partial class {className} : global::System.Windows.ResourceDictionary");
            sb.AppendLine("    {");
            sb.AppendLine($"        internal const string ResourcePath = \"{EscapeString(resourcePath)}\";");
            sb.AppendLine();
            sb.AppendLine($"        public {className}()");
            sb.AppendLine("        {");
            sb.AppendLine("            InitializeComponent();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
            sb.AppendLine("        internal static void __XsgRegister()");
            sb.AppendLine("        {");
            var registerAs = assemblyName is null ? $"typeof({className}).Assembly" : $"\"{EscapeString(assemblyName)}\"";
            sb.AppendLine($"            global::System.Windows.Markup.XamlSourceGeneratedResources.RegisterDictionary({registerAs}, ResourcePath, static () => new {className}());");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        // The dictionaries of Style/template/element .Resources being built, innermost last (see EmitResourceChildren).");
            sb.AppendLine("        private readonly global::System.Collections.Generic.List<global::System.Windows.ResourceDictionary> __scopes = new global::System.Collections.Generic.List<global::System.Windows.ResourceDictionary>();");
            sb.AppendLine();
            sb.AppendLine("        // StaticResource inside the dictionary: the .Resources being built, then what this dictionary already holds, else the application's resources.");
            sb.AppendLine("        private object FindResource(object key)");
            sb.AppendLine("        {");
            sb.AppendLine("            for (var i = __scopes.Count - 1; i >= 0; i--) if (__scopes[i].Contains(key)) return __scopes[i][key];");
            sb.AppendLine("            if (this.Contains(key)) return this[key];");
            sb.AppendLine("            var app = global::System.Windows.Application.Current?.TryFindResource(key);");
            sb.AppendLine("            return app ?? throw new global::System.Windows.ResourceReferenceKeyNotFoundException(\"Cannot find resource named '\" + key + \"'.\", key);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private void InitializeComponent()");
            sb.AppendLine("        {");
            sb.Append(bodyText);
            sb.AppendLine("        }");
            AppendCompiledPropertyHelperIfUsed(sb);
            AppendDictionaryLoaderIfUsed(sb);
            sb.AppendLine("    }");
            sb.AppendLine("}");

            spc.AddSource(className + ".g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// Children of a root ResourceDictionary (<c>this</c>): merged dictionaries and keyed (or implicitly type-keyed) resources.
        /// </summary>
        // A top-level resource styling (TargetType / DataType) or keyed by ({x:Type}) a type named in LibreWpfXsgExcludeTargetTypes.
        private static bool IsExcludedResource(XElement child, string defaultNs)
        {
            var list = s_context?.Features.ExcludedTargetTypes;
            if (string.IsNullOrEmpty(list))
            {
                return false;
            }

            var excluded = new HashSet<string>(list!.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()), StringComparer.Ordinal);
            var written = child.Attribute("TargetType")?.Value ?? child.Attribute("DataType")?.Value;
            if (written is null && child.Attribute(XName.Get("Key", XamlNamespace))?.Value is { } key && key.TrimStart().StartsWith("{x:Type", StringComparison.Ordinal))
            {
                written = key;
            }
            if (written is null)
            {
                return false;
            }

            var simple = StripXType(written);
            var colon = simple.LastIndexOf(':');
            simple = colon >= 0 ? simple.Substring(colon + 1) : simple;
            var full = ResolveTypeNameInScope(StripXType(written), child, defaultNs);
            if (!excluded.Contains(simple) && !excluded.Contains(full))
            {
                return false;
            }

            if (s_context is not null)
            {
                Report(s_context, s_excluded, child, simple);
            }
            return true;
        }

        private static void GenerateRootDictionaryContent(StringBuilder sb, XElement el, string defaultNs, string sourcePath, int indent, ref int localId)
        {
            var pad = new string(' ', indent);
            foreach (var child in el.Elements())
            {
                if (child.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal))
                {
                    foreach (var md in child.Elements())
                    {
                        var mdVar = GenerateElement(sb, md, defaultNs, sourcePath, indent, ref localId);
                        sb.AppendLine($"{pad}this.MergedDictionaries.Add({mdVar});");
                    }
                    continue;
                }
                if (child.Name.LocalName.Contains("."))
                {
                    ReportNotGenerated(child, child.Name.LocalName);
                    continue;
                }

                if (IsExcludedResource(child, defaultNs))
                {
                    continue;
                }

                var cVar = GenerateElement(sb, child, defaultNs, sourcePath, indent, ref localId);
                EmitKeyedResource(sb, "this", child, cVar, defaultNs, pad);
            }
        }

        private static string ResolveTypeNameInScope(string text, XElement scope, string defaultNs)
        {
            var colon = text.IndexOf(':');
            if (colon > 0)
            {
                var ns = scope.GetNamespaceOfPrefix(text.Substring(0, colon))?.NamespaceName;
                var local = text.Substring(colon + 1);
                if (ns is not null && ns.StartsWith("clr-namespace:", StringComparison.Ordinal))
                {
                    var clrNs = ns.Substring("clr-namespace:".Length);
                    var semi = clrNs.IndexOf(';');
                    if (semi >= 0) clrNs = clrNs.Substring(0, semi);
                    // An empty CLR namespace is the global namespace: no leading dot.
                    return clrNs.Length == 0 ? local : clrNs + "." + local;
                }
                return ResolveTypeNameFromName(local, defaultNs) ?? local;
            }
            return ResolveTypeNameFromName(text, defaultNs) ?? text;
        }
    }
}
