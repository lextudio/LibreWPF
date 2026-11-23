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

            var pathCode = CompiledPathCode(path, parts, owner, isDataContext, value);
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

        private static readonly SymbolDisplayFormat s_typeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

        private static string? CompiledPathCode(string path, Dictionary<string, string> parts, XElement? owner, bool isDataContext, string markup)
        {
            var context = s_context;
            if (context is null || !context.Features.CompiledBindings || owner is null)
            {
                return null;
            }

            var scope = FindDataTypeScope(owner, isDataContext);
            if (scope is null)
            {
                return null;
            }

            // Not about the DataContext, or nothing to reflect over: the stock binding is already the right thing.
            if (parts.ContainsKey("ElementName") || parts.ContainsKey("RelativeSource") || parts.ContainsKey("Source") || parts.ContainsKey("XPath"))
            {
                return null;
            }
            if (path.Length == 0 || path == ".")
            {
                return null;
            }

            var (scopeElement, dataTypeText) = scope.Value;
            var rootType = ResolveDataType(dataTypeText, scopeElement, context.Compilation);
            if (rootType is null)
            {
                Report(context, s_dataTypeNotFound, scopeElement, dataTypeText);
                return null;
            }

            var segments = path.Split('.');
            if (segments.Any(segment => !s_identifier.IsMatch(segment.Trim())))
            {
                Report(context, s_bindingNotCompiled, owner, markup, "only a dotted path of property names is compiled; indexers and attached properties are not");
                return null;
            }

            var steps = new List<string>();
            ITypeSymbol current = rootType;
            for (var i = 0; i < segments.Length; i++)
            {
                var name = segments[i].Trim();
                var property = FindProperty(current, name, context);
                if (property is null)
                {
                    Report(context, s_bindingPropertyNotFound, owner, current.ToDisplayString(), name, path, dataTypeText);
                    return null;
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
                current = property.Type;
            }

            context.UsesCompiledProperty = true;
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

            var className = "__XsgResourceDictionary_" + Regex.Replace(resourcePath, "[^A-Za-z0-9_]", "_");
            var nameTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var el in root.Descendants())
            {
                var na = el.Attribute(XName.Get("Name", XamlNamespace));
                if (na is not null && !nameTypes.ContainsKey(na.Value))
                {
                    nameTypes[na.Value] = ResolveTypeName(el, defaultNs) ?? "System.Object";
                }
            }

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
            foreach (var kvp in nameTypes)
            {
                sb.AppendLine($"        private global::{kvp.Value}? {kvp.Key};");
            }
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
            sb.AppendLine("        // StaticResource inside the dictionary: what this dictionary already holds, else the application's resources.");
            sb.AppendLine("        private object FindResource(object key)");
            sb.AppendLine("        {");
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
            sb.AppendLine("    }");
            sb.AppendLine("}");

            spc.AddSource(className + ".g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// Children of a root ResourceDictionary (<c>this</c>): merged dictionaries and keyed (or implicitly type-keyed) resources.
        /// </summary>
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

                var cVar = GenerateElement(sb, child, defaultNs, sourcePath, indent, ref localId);
                var key = child.Attribute(XName.Get("Key", XamlNamespace));
                if (key is not null)
                {
                    // {x:Type ...} / {x:Static ...} keys are typed keys, not the text of the markup.
                    sb.AppendLine($"{pad}this[{ResourceKeyCode(key.Value, child, defaultNs)}] = {cVar};");
                    continue;
                }

                var typeKey = child.Attribute("TargetType") ?? child.Attribute("DataType");
                if (typeKey is not null)
                {
                    var t = typeKey.Value.Trim();
                    if (t.StartsWith("{x:Type", StringComparison.Ordinal))
                    {
                        t = t.Substring("{x:Type".Length).Trim().TrimEnd('}').Trim();
                    }
                    var resolved = ResolveTypeNameInScope(t, child, defaultNs);
                    // A Style is keyed by its TargetType; a DataTemplate (and HierarchicalDataTemplate) by a DataTemplateKey, not by the
                    // bare type: WPF's implicit data template lookup asks for DataTemplateKey(type).
                    var byDataType = typeKey.Name.LocalName == "DataType" && child.Attribute("TargetType") is null;
                    sb.AppendLine(byDataType
                        ? $"{pad}this[new global::System.Windows.DataTemplateKey(typeof(global::{resolved}))] = {cVar};"
                        : $"{pad}this[typeof(global::{resolved})] = {cVar};");
                }
                else
                {
                    sb.AppendLine($"{pad}// resource without x:Key skipped: {child.Name.LocalName}");
                    ReportNotGenerated(child, $"the resource <{child.Name.LocalName}> without x:Key or TargetType/DataType");
                }
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
