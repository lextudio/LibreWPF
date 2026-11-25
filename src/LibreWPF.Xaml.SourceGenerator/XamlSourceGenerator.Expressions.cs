#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace LibreWPF.Xaml.SourceGenerator
{
    public sealed partial class XamlSourceGenerator
    {
        private static readonly HashSet<string> s_expressionKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "this", "new", "true", "false", "null", "typeof", "nameof", "default", "is", "as", "await", "sizeof", "checked", "unchecked", "base",
        };

        // MAUI's compound expressions: `{Age + 1}`, `{$'{Name} is {Age}'}`, `{Name.ToUpper()}`. Every name the DataContext type (x:DataType) has
        // is read through a Binding; the expression is evaluated by a generated converter over those values (a MultiBinding), and is
        // one-way. Names of the page and statics stay C# in the converter. Returns null when no name is the DataContext's, or the
        // target cannot take a binding, so the value stays plain C#.
        private static string? CompoundBindingCode(string expression, XElement? owner, string attrLocal, string? elementTypeName)
        {
            var context = s_context;
            // A simple path was decided by BindingExpressionCode (a binding, a conflict or plain C#); it is not compound.
            if (System.Text.RegularExpressions.Regex.IsMatch(expression, @"^\.?[A-Za-z_]\w*(\.[A-Za-z_]\w*)*$"))
            {
                return null;
            }

            if (context is null || owner is null || elementTypeName is null || attrLocal.Contains(".") || XamlExpressions.IsLambda(expression)
                || FindDependencyPropertyField(TypeSymbol(elementTypeName), attrLocal) is null
                || FindDataTypeScope(owner, false) is not { } scope
                || ResolveDataType(scope.DataType, scope.Element, context.Compilation) is not { } root)
            {
                return null;
            }

            var code = XamlExpressions.ToCSharp(expression);
            var chains = ScanChains(code, 0, code.Length, new List<(int Start, int Length)>());
            var paths = new List<(string Path, ITypeSymbol Type)>();
            var replacements = new List<(int Start, int Length, string With)>();
            foreach (var (start, length) in chains)
            {
                var chain = code.Substring(start, length);
                var explicitDataContext = chain.StartsWith(".", StringComparison.Ordinal);
                var segments = (explicitDataContext ? chain.Substring(1) : chain).Split('.');
                if (segments.Length == 0 || s_expressionKeywords.Contains(segments[0]))
                {
                    continue;
                }

                // A single name followed by '(' is a method of the page, not a property.
                var after = start + length;
                while (after < code.Length && char.IsWhiteSpace(code[after]))
                {
                    after++;
                }

                if (segments.Length == 1 && after < code.Length && code[after] == '(')
                {
                    continue;
                }

                if (FindProperty(root, segments[0], context) is null)
                {
                    continue;
                }

                if (!explicitDataContext && context.Within is { } page && PageDeclaresMember(page, segments[0]))
                {
                    Report(context, s_memberOnPageAndDataContext, owner, expression, segments[0], page.Name);
                    continue;
                }

                // As many steps as are properties; the rest (Name.Length, Name.ToUpper) stay member accesses on the value.
                ITypeSymbol current = root;
                var resolved = new List<string>();
                foreach (var segment in segments)
                {
                    if (FindProperty(current, segment, context) is not { } property)
                    {
                        break;
                    }

                    current = property.Type;
                    resolved.Add(segment);
                }

                if (!IsNameable(current))
                {
                    return null;
                }

                var path = string.Join(".", resolved);
                var index = paths.FindIndex(p => p.Path == path);
                if (index < 0)
                {
                    paths.Add((path, current));
                    index = paths.Count - 1;
                }

                var consumed = (explicitDataContext ? 1 : 0) + path.Length;
                replacements.Add((start, consumed, "__v" + index));
            }

            if (paths.Count == 0)
            {
                return null;
            }

            var body = new StringBuilder(code);
            foreach (var (start, length, with) in replacements.OrderByDescending(r => r.Start))
            {
                body.Remove(start, length).Insert(start, with);
            }

            context.UsesExpressionConverter = true;
            var locals = string.Concat(paths.Select((p, i) =>
                $"var __v{i} = ({p.Type.ToDisplayString(s_typeFormat)})values[{i}]!; "));
            var guard = string.Join(" || ", paths.Select((_, i) => $"values[{i}] == global::System.Windows.DependencyProperty.UnsetValue"));
            var bindings = string.Join(", ", paths.Select(p => BindingCode("{Binding " + p.Path + "}", owner, false)));

            // The evaluation is a method of the class, not a lambda in InitializeComponent: Hot Reload (EnC) updates a method in place,
            // but marks a lambda it cannot match as deleted, and the old element tree still calls its converter while it is replaced.
            var method = "__XsgExpression_" + context.ExpressionMethodCount++;
            var line = owner is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;
            var escapedPath = context.SourcePath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            context.ExpressionMembers.AppendLine(line > 0 ? $"#line {line} \"{escapedPath}\"" : string.Empty);
            context.ExpressionMembers.AppendLine($"        private object? {method}(object?[] values)");
            context.ExpressionMembers.AppendLine("        {");
            context.ExpressionMembers.AppendLine($"            if ({guard}) {{ return global::System.Windows.DependencyProperty.UnsetValue; }}");
            context.ExpressionMembers.AppendLine("            try");
            context.ExpressionMembers.AppendLine("            {");
            context.ExpressionMembers.AppendLine("                " + locals.TrimEnd());
            context.ExpressionMembers.AppendLine($"                return (object?)({body});");
            context.ExpressionMembers.AppendLine("            }");
            context.ExpressionMembers.AppendLine("            catch (global::System.Exception) { return global::System.Windows.DependencyProperty.UnsetValue; }");
            context.ExpressionMembers.AppendLine("        }");
            if (line > 0)
            {
                context.ExpressionMembers.AppendLine("#line default");
            }

            return $"new global::System.Windows.Data.MultiBinding {{ Mode = global::System.Windows.Data.BindingMode.OneWay, Converter = new __XsgExpressionConverter({method}), Bindings = {{ {bindings} }} }}";
        }

        // The identifier chains of C# code (`Name`, `User.Home.City`, a leading `.Name`) outside string and character literals; the holes of an
        // interpolated string are code. A chain after `.`, `)` or `]` continues something that is not a DataContext name and is skipped.
        private static List<(int Start, int Length)> ScanChains(string code, int from, int to, List<(int Start, int Length)> chains)
        {
            var i = from;
            var previousSignificant = '\0';
            while (i < to)
            {
                var c = code[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '$' && i + 1 < to && code[i + 1] == '"')
                {
                    i = ScanInterpolated(code, i, to, chains);
                    previousSignificant = '"';
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    i = SkipLiteral(code, i, to, c);
                    previousSignificant = c;
                    continue;
                }

                var startsChain = char.IsLetter(c) || c == '_' || (c == '.' && i + 1 < to && (char.IsLetter(code[i + 1]) || code[i + 1] == '_'));
                if (startsChain && (c != '.' || (!(i > from && code[i - 1] == '?') && previousSignificant != ')' && previousSignificant != ']' && !char.IsLetterOrDigit(previousSignificant) && previousSignificant != '_')))
                {
                    var j = i;
                    if (code[j] == '.')
                    {
                        j++;
                    }

                    while (j < to)
                    {
                        while (j < to && (char.IsLetterOrDigit(code[j]) || code[j] == '_'))
                        {
                            j++;
                        }

                        if (j + 1 < to && code[j] == '.' && (char.IsLetter(code[j + 1]) || code[j + 1] == '_'))
                        {
                            j++;
                            continue;
                        }

                        break;
                    }

                    // A chain that follows `.`, `)` or `]` was reached by the branch above and is not scanned here.
                    if (!(previousSignificant == '.' && c != '.'))
                    {
                        chains.Add((i, j - i));
                    }

                    previousSignificant = 'a';
                    i = j;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    while (i < to && (char.IsLetterOrDigit(code[i]) || code[i] == '.' || code[i] == '_'))
                    {
                        i++;
                    }

                    previousSignificant = '0';
                    continue;
                }

                previousSignificant = c;
                i++;
            }

            return chains;
        }

        private static int SkipLiteral(string code, int open, int to, char quote)
        {
            var i = open + 1;
            while (i < to)
            {
                if (code[i] == '\\')
                {
                    i += 2;
                    continue;
                }

                if (code[i] == quote)
                {
                    return i + 1;
                }

                i++;
            }

            return to;
        }

        private static int ScanInterpolated(string code, int open, int to, List<(int Start, int Length)> chains)
        {
            var i = open + 2;
            while (i < to)
            {
                var c = code[i];
                if (c == '\\')
                {
                    i += 2;
                }
                else if (c == '"')
                {
                    return i + 1;
                }
                else if (c == '{' && i + 1 < to && code[i + 1] == '{')
                {
                    i += 2;
                }
                else if (c == '{')
                {
                    var depth = 0;
                    var j = i + 1;
                    var holeEnd = to;
                    var formatAt = -1;
                    while (j < to)
                    {
                        var d = code[j];
                        if (d == '"' || d == '\'')
                        {
                            j = SkipLiteral(code, j, to, d);
                            continue;
                        }

                        if (d == '(' || d == '[' || d == '{')
                        {
                            depth++;
                        }
                        else if (d == ')' || d == ']')
                        {
                            depth--;
                        }
                        else if (d == '}')
                        {
                            if (depth == 0)
                            {
                                holeEnd = j;
                                break;
                            }

                            depth--;
                        }
                        else if (d == ':' && depth == 0 && formatAt < 0)
                        {
                            formatAt = j;
                        }

                        j++;
                    }

                    ScanChains(code, i + 1, formatAt >= 0 ? formatAt : holeEnd, chains);
                    i = holeEnd + 1;
                }
                else
                {
                    i++;
                }
            }

            return to;
        }

        private static void AppendExpressionConverterIfUsed(StringBuilder sb)
        {
            if (s_context is not { UsesExpressionConverter: true })
            {
                return;
            }

            sb.AppendLine("""
                        [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
                        private sealed class __XsgExpressionConverter : global::System.Windows.Data.IMultiValueConverter
                        {
                            private readonly global::System.Func<object?[], object?> _evaluate;
                            public __XsgExpressionConverter(global::System.Func<object?[], object?> evaluate) => _evaluate = evaluate;
                            // A MultiBinding does not convert the converter's result to the property's type (a Binding does): an int for Text would be rejected.
                            public object? Convert(object?[] values, global::System.Type targetType, object? parameter, global::System.Globalization.CultureInfo culture)
                            {
                                var result = _evaluate(values);
                                if (result is null || targetType is null || targetType.IsInstanceOfType(result) || result == global::System.Windows.DependencyProperty.UnsetValue)
                                {
                                    return result;
                                }

                                if (targetType == typeof(string))
                                {
                                    return global::System.Convert.ToString(result, culture);
                                }

                                try
                                {
                                    var converter = global::System.ComponentModel.TypeDescriptor.GetConverter(targetType);
                                    if (converter.CanConvertFrom(result.GetType()))
                                    {
                                        return converter.ConvertFrom(null, culture, result);
                                    }

                                    return global::System.Convert.ChangeType(result, global::System.Nullable.GetUnderlyingType(targetType) ?? targetType, culture);
                                }
                                catch (global::System.Exception)
                                {
                                    return global::System.Windows.DependencyProperty.UnsetValue;
                                }
                            }
                            public object?[] ConvertBack(object? value, global::System.Type[] targetTypes, object? parameter, global::System.Globalization.CultureInfo culture)
                                => throw new global::System.NotSupportedException("A C# expression is one-way.");
                        }
                """);
        }
    }
}
