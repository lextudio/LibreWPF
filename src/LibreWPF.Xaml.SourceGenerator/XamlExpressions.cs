#nullable enable
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace LibreWPF.Xaml.SourceGenerator
{
    /// <summary>
    /// C# expressions in XAML attribute values, written the way .NET MAUI writes them
    /// (dotnet/maui <c>docs/specs/XamlCSharpExpressions.md</c>): a bare <c>{Items.Count}</c>, an explicit
    /// <c>{= Foo}</c>, <c>{this.Foo}</c>, single-quoted strings, <c>AND OR LT GT LTE GTE</c> operator words,
    /// <c>$'...'</c> interpolation and, for an event, a lambda <c>{(s, e) =&gt; Count++}</c>.
    /// All of the syntax lives here so a change in MAUI's (still preview) rules stays local.
    /// Stage 1 evaluates every expression as plain C# in the generated class (MAUI's "local access" and
    /// "static invocation"); resolving a bare path as a binding against <c>x:DataType</c> is a later stage.
    /// </summary>
    internal static class XamlExpressions
    {
        // Unprefixed markup extensions of the presentation namespace. A value that names one is a markup
        // extension and never an expression (MAUI: the extension wins on a bare-name match). A prefixed
        // name, {x:Static} or {local:Foo}, is always a markup extension.
        private static readonly string[] s_markupExtensions =
        {
            "Binding", "MultiBinding", "PriorityBinding", "StaticResource", "DynamicResource", "TemplateBinding",
            "RelativeSource", "ComponentResourceKey", "ThemeDictionary",
        };

        private static readonly Regex s_leadingName = new Regex(@"^(?<prefix>[A-Za-z_][\w]*:)?(?<name>[A-Za-z_][\w.]*)", RegexOptions.Compiled);
        private static readonly Regex s_lambda = new Regex(@"^(async\s+)?(\([^()]*\)|[A-Za-z_]\w*)\s*=>", RegexOptions.Compiled);

        /// <summary>
        /// Whether <paramref name="value"/> (an attribute value, already XML-unescaped) is a C# expression, and its text
        /// between the braces. <c>{}</c> (the escape), a markup extension and anything that is not a braced value are not.
        /// </summary>
        public static bool TryGetExpression(string value, out string expression)
        {
            expression = string.Empty;
            var text = value.Trim();
            if (text.Length < 3 || text[0] != '{' || text[text.Length - 1] != '}' || text[1] == '}')
            {
                return false;
            }

            var inner = text.Substring(1, text.Length - 2).Trim();
            if (inner.Length == 0)
            {
                return false;
            }

            if (inner[0] == '=')
            {
                expression = inner.Substring(1).Trim();
                return expression.Length > 0;
            }

            var match = s_leadingName.Match(inner);
            if (match.Success)
            {
                if (match.Groups["prefix"].Success)
                {
                    return false;
                }

                var name = match.Groups["name"].Value;
                if (name.EndsWith("Extension", StringComparison.Ordinal))
                {
                    name = name.Substring(0, name.Length - "Extension".Length);
                }

                if (Array.IndexOf(s_markupExtensions, name) >= 0)
                {
                    return false;
                }
            }

            expression = inner;
            return true;
        }

        /// <summary>A lambda such as <c>(s, e) =&gt; Count++</c> or <c>sender =&gt; Save()</c>: the value of an event attribute.</summary>
        public static bool IsLambda(string expression) => s_lambda.IsMatch(expression);

        public static bool IsAsyncLambda(string expression)
        {
            var match = s_lambda.Match(expression);
            return match.Success && match.Groups[1].Success;
        }

        /// <summary>The C# for an expression: operator words, single-quoted strings and <c>$'...'</c> interpolation rewritten.</summary>
        public static string ToCSharp(string expression)
        {
            var output = new StringBuilder(expression.Length + 8);
            var i = 0;
            while (i < expression.Length)
            {
                var c = expression[i];
                if (c == '"')
                {
                    i = CopyDoubleQuoted(expression, i, output);
                }
                else if (c == '\'')
                {
                    i = ConvertSingleQuoted(expression, i, output);
                }
                else if (c == '$' && i + 1 < expression.Length && expression[i + 1] == '\'')
                {
                    i = ConvertInterpolated(expression, i, output);
                }
                else if (char.IsLetter(c) && (i == 0 || !IsWordPart(expression[i - 1])))
                {
                    i = CopyWordOrAlias(expression, i, output);
                }
                else
                {
                    output.Append(c);
                    i++;
                }
            }

            return output.ToString();
        }

        private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.';

        // AND OR LT GT LTE GTE are aliases only as a whole word with white space on both sides (MAUI), in any case.
        private static int CopyWordOrAlias(string text, int start, StringBuilder output)
        {
            var end = start;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
            {
                end++;
            }

            var word = text.Substring(start, end - start);
            var spaced = start > 0 && char.IsWhiteSpace(text[start - 1]) && end < text.Length && char.IsWhiteSpace(text[end]);
            if (spaced && Alias(word) is { } symbol)
            {
                output.Append(symbol);
            }
            else
            {
                output.Append(word);
            }

            return end;
        }

        private static string? Alias(string word)
        {
            switch (word.ToUpperInvariant())
            {
                case "AND": return "&&";
                case "OR": return "||";
                case "LT": return "<";
                case "GT": return ">";
                case "LTE": return "<=";
                case "GTE": return ">=";
                default: return null;
            }
        }

        // A string already written with double quotes (allowed: the value was XML-unescaped, or came from CDATA).
        private static int CopyDoubleQuoted(string text, int start, StringBuilder output)
        {
            output.Append('"');
            var i = start + 1;
            while (i < text.Length)
            {
                var c = text[i];
                output.Append(c);
                i++;
                if (c == '\\' && i < text.Length)
                {
                    output.Append(text[i]);
                    i++;
                }
                else if (c == '"')
                {
                    break;
                }
            }

            return i;
        }

        // 'text' becomes "text"; \' is an apostrophe; a double quote inside is escaped.
        private static int ConvertSingleQuoted(string text, int start, StringBuilder output)
        {
            output.Append('"');
            var i = start + 1;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    var next = text[i + 1];
                    output.Append(next == '\'' ? "'" : "\\" + next);
                    i += 2;
                    continue;
                }

                i++;
                if (c == '\'')
                {
                    break;
                }

                output.Append(c == '"' ? "\\\"" : c.ToString());
            }

            output.Append('"');
            return i;
        }

        // $'text {hole} text' becomes $"text {hole} text"; each hole is converted like an expression, and a hole that is a
        // whole conditional is parenthesised (the first top-level ':' of a hole otherwise starts a format specifier, CS8361).
        private static int ConvertInterpolated(string text, int start, StringBuilder output)
        {
            output.Append("$\"");
            var i = start + 2;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    var next = text[i + 1];
                    output.Append(next == '\'' ? "'" : "\\" + next);
                    i += 2;
                }
                else if (c == '{' && i + 1 < text.Length && text[i + 1] == '{')
                {
                    output.Append("{{");
                    i += 2;
                }
                else if (c == '{')
                {
                    i = ConvertHole(text, i, output);
                }
                else if (c == '\'')
                {
                    i++;
                    break;
                }
                else
                {
                    output.Append(c == '"' ? "\\\"" : c.ToString());
                    i++;
                }
            }

            output.Append('"');
            return i;
        }

        private static int ConvertHole(string text, int start, StringBuilder output)
        {
            var depth = 0;
            var i = start + 1;
            var holeStart = i;
            var formatAt = -1;
            var questionAt = -1;
            var colonAt = -1;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\'' || c == '"')
                {
                    i = SkipQuoted(text, i, c);
                    continue;
                }

                if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                }
                else if (c == ')' || c == ']')
                {
                    depth--;
                }
                else if (c == '}')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }
                else if (depth == 0 && c == '?' && questionAt < 0)
                {
                    var next = i + 1 < text.Length ? text[i + 1] : '\0';
                    if (next != '?' && next != '.' && next != '[')
                    {
                        questionAt = i;
                    }
                }
                else if (depth == 0 && c == ':')
                {
                    if (questionAt >= 0 && colonAt < 0)
                    {
                        colonAt = i;
                    }
                    else if (questionAt < 0 && formatAt < 0)
                    {
                        formatAt = i;
                    }
                }

                i++;
            }

            var code = text.Substring(holeStart, (formatAt >= 0 ? formatAt : i) - holeStart);
            var format = formatAt >= 0 ? text.Substring(formatAt, i - formatAt) : string.Empty;
            var converted = ToCSharp(code.Trim());
            output.Append('{');
            output.Append(questionAt >= 0 && colonAt >= 0 ? "(" + converted + ")" : converted);
            output.Append(format);
            output.Append('}');
            return i < text.Length ? i + 1 : i;
        }

        private static int SkipQuoted(string text, int start, char quote)
        {
            var i = start + 1;
            while (i < text.Length)
            {
                if (text[i] == '\\')
                {
                    i += 2;
                    continue;
                }

                if (text[i] == quote)
                {
                    return i + 1;
                }

                i++;
            }

            return i;
        }
    }
}
