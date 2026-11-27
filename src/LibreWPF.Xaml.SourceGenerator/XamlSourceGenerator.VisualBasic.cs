#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LibreWPF.Xaml.SourceGenerator
{
    // The generator builds the object graph as C#. A Visual Basic project gets the same graph as Visual Basic: the C# text is parsed back
    // and printed by VbPrinter, which handles exactly the constructs the generator emits (declarations, member access, object creation
    // with initializers, casts, event wiring, a few lambdas). Anything else is reported (LWXSG1001) instead of being guessed.
    public sealed partial class XamlSourceGenerator
    {
        private static readonly DiagnosticDescriptor s_notTranslatableToVb = new DiagnosticDescriptor(
            "LWXSG1001",
            "XAML construct not available for Visual Basic",
            "{0}: {1}",
            "LibreWPF.Xaml",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static bool IsVisualBasic => s_context?.Compilation.Language == LanguageNames.VisualBasic;

        // Output that is printed from the C# text by a language printer: no pattern matching, no helpers the printer lacks.
        private static bool IsTranslatedOutput => IsVisualBasic || s_context?.Features.TargetsFSharp == true;

        // The class an x:Class names in a Visual Basic project: the type with exactly that name, else the one type whose full name ends with it (Visual Basic
        // writes x:Class relative to the project's root namespace). Two candidates are an error: binding the generated part to the wrong class would be silent.
        private static INamedTypeSymbol? FindVisualBasicClass(string relativeName)
        {
            var suffix = "." + relativeName;
            var matches = new List<INamedTypeSymbol>();
            var stack = new Stack<INamespaceOrTypeSymbol>();
            stack.Push(s_context!.Compilation.Assembly.GlobalNamespace);
            while (stack.Count > 0)
            {
                foreach (var member in stack.Pop().GetMembers())
                {
                    if (member is INamespaceSymbol nested)
                    {
                        stack.Push(nested);
                    }
                    else if (member is INamedTypeSymbol type && type.TypeKind == TypeKind.Class)
                    {
                        var name = type.ToDisplayString();
                        if (string.Equals(name, relativeName, StringComparison.OrdinalIgnoreCase))
                        {
                            return type;
                        }

                        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        {
                            matches.Add(type);
                        }
                    }
                }
            }

            if (matches.Count > 1)
            {
                s_context.Diagnostics.Add(Diagnostic.Create(s_notTranslatableToVb, Location.None, s_context.SourcePath,
                    $"x:Class '{relativeName}' matches more than one class ({string.Join(", ", matches.Select(m => m.ToDisplayString()))}): write the full name"));
                return null;
            }

            return matches.Count == 1 ? matches[0] : null;
        }

        private static bool TryTranslateToVisualBasic(string csharp, out string visualBasic, out string error)
        {
            var tree = CSharpSyntaxTree.ParseText(csharp, new CSharpParseOptions(preprocessorSymbols: Array.Empty<string>()));
            try
            {
                visualBasic = new VbPrinter().Print((CompilationUnitSyntax)tree.GetRoot());
                error = string.Empty;
                return true;
            }
            catch (NotSupportedException ex)
            {
                visualBasic = string.Empty;
                error = ex.Message;
                return false;
            }
        }

        private sealed class VbPrinter
        {
            private static readonly HashSet<string> s_keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AddHandler", "AddressOf", "Alias", "And", "AndAlso", "As", "Boolean", "ByRef", "Byte", "ByVal", "Call", "Case", "Catch", "CBool", "CByte",
                "CChar", "CDate", "CDbl", "CDec", "Char", "CInt", "Class", "CLng", "CObj", "Const", "Continue", "CSByte", "CShort", "CSng", "CStr", "CType",
                "CUInt", "CULng", "CUShort", "Date", "Decimal", "Declare", "Default", "Delegate", "Dim", "DirectCast", "Do", "Double", "Each", "Else",
                "ElseIf", "End", "EndIf", "Enum", "Erase", "Error", "Event", "Exit", "False", "Finally", "For", "Friend", "Function", "Get", "GetType",
                "GetXMLNamespace", "Global", "GoSub", "GoTo", "Handles", "If", "Implements", "Imports", "In", "Inherits", "Integer", "Interface", "Is",
                "IsNot", "Let", "Lib", "Like", "Long", "Loop", "Me", "Mod", "Module", "MustInherit", "MustOverride", "MyBase", "MyClass", "Namespace",
                "Narrowing", "New", "Next", "Not", "Nothing", "NotInheritable", "NotOverridable", "Object", "Of", "On", "Operator", "Option", "Optional",
                "Or", "OrElse", "Overloads", "Overridable", "Overrides", "ParamArray", "Partial", "Private", "Property", "Protected", "Public",
                "RaiseEvent", "ReadOnly", "ReDim", "REM", "RemoveHandler", "Resume", "Return", "SByte", "Select", "Set", "Shadows", "Shared", "Short",
                "Single", "Static", "Step", "Stop", "String", "Structure", "Sub", "SyncLock", "Then", "Throw", "To", "True", "Try", "TryCast", "TypeOf",
                "UInteger", "ULong", "UShort", "Using", "Variant", "Wend", "When", "While", "Widening", "With", "WithEvents", "WriteOnly", "Xor",
            };

            // The run-time half of a project's own markup extensions (see __XsgApplyExtension in the C# output), written once in Visual Basic.
            private const string MarkupExtensionHelper = """
                Private Shared Sub __XsgApplyExtension(target As Object, [property] As Global.System.Windows.DependencyProperty, name As String, extension As Global.System.Windows.Markup.MarkupExtension)
                    Dim clr As Global.System.Reflection.PropertyInfo = Nothing
                    If [property] Is Nothing Then clr = target.GetType().GetProperty(name)
                    Dim provider = New __XsgExtensionServiceProvider(target, If([property] IsNot Nothing, CObj([property]), CObj(clr)))
                    Dim value As Object = extension.ProvideValue(provider)
                    Dim dependencyObject = TryCast(target, Global.System.Windows.DependencyObject)
                    If [property] IsNot Nothing AndAlso dependencyObject IsNot Nothing Then
                        Dim binding = TryCast(value, Global.System.Windows.Data.BindingBase)
                        If binding IsNot Nothing Then
                            Global.System.Windows.Data.BindingOperations.SetBinding(dependencyObject, [property], binding)
                            Return
                        End If
                        Dim text = TryCast(value, String)
                        If text IsNot Nothing AndAlso Not [property].PropertyType.IsAssignableFrom(GetType(String)) Then
                            value = Global.System.ComponentModel.TypeDescriptor.GetConverter([property].PropertyType).ConvertFromInvariantString(text)
                        End If
                        dependencyObject.SetValue([property], value)
                    ElseIf clr IsNot Nothing Then
                        Dim text = TryCast(value, String)
                        If text IsNot Nothing AndAlso Not clr.PropertyType.IsAssignableFrom(GetType(String)) Then
                            value = Global.System.ComponentModel.TypeDescriptor.GetConverter(clr.PropertyType).ConvertFromInvariantString(text)
                        End If
                        clr.SetValue(target, value)
                    End If
                End Sub

                Private Shared Function __XsgProvideValue(extension As Global.System.Windows.Markup.MarkupExtension, Optional target As Object = Nothing, Optional [property] As Object = Nothing) As Object
                    Return extension.ProvideValue(New __XsgExtensionServiceProvider(If(target, extension), [property]))
                End Function

                Private NotInheritable Class __XsgExtensionServiceProvider
                    Implements Global.System.IServiceProvider, Global.System.Windows.Markup.IProvideValueTarget
                    Public Sub New(target As Object, [property] As Object)
                        _target = target
                        _property = [property]
                    End Sub
                    Private ReadOnly _target As Object
                    Private ReadOnly _property As Object
                    Public ReadOnly Property TargetObject As Object Implements Global.System.Windows.Markup.IProvideValueTarget.TargetObject
                        Get
                            Return _target
                        End Get
                    End Property
                    Public ReadOnly Property TargetProperty As Object Implements Global.System.Windows.Markup.IProvideValueTarget.TargetProperty
                        Get
                            Return _property
                        End Get
                    End Property
                    Public Function GetService(serviceType As Global.System.Type) As Object Implements Global.System.IServiceProvider.GetService
                        If serviceType Is GetType(Global.System.Windows.Markup.IProvideValueTarget) Then Return Me
                        Return Nothing
                    End Function
                End Class
                """;

            private readonly StringBuilder _out = new StringBuilder();
            private int _indent;

            public string Print(CompilationUnitSyntax unit)
            {
                _out.AppendLine("' <auto-generated by LibreWPF.Xaml.SourceGenerator />");
                _out.AppendLine("Option Strict Off");
                _out.AppendLine("Option Infer On");
                _out.AppendLine("Option Explicit On");
                foreach (var member in unit.Members)
                {
                    Member(member);
                }

                return _out.ToString();
            }

            private void Line(string text) => _out.Append(' ', _indent * 4).AppendLine(text);

            private void Member(MemberDeclarationSyntax member)
            {
                switch (member)
                {
                    case BaseNamespaceDeclarationSyntax ns:
                        Line("Namespace Global." + Name(ns.Name));
                        _indent++;
                        foreach (var inner in ns.Members)
                        {
                            Member(inner);
                        }
                        _indent--;
                        Line("End Namespace");
                        break;
                    case ClassDeclarationSyntax cls when cls.Identifier.Text.StartsWith("__XsgComponent_", StringComparison.Ordinal):
                        // A module initializer registering the component by its resource path: Visual Basic has none.
                        break;
                    case ClassDeclarationSyntax cls:
                        Class(cls);
                        break;
                    default:
                        throw new NotSupportedException($"the member {member.Kind()} has no Visual Basic form");
                }
            }

            private void Class(ClassDeclarationSyntax cls)
            {
                var access = AccessOf(cls.Modifiers, "Public");
                Line($"{access} Partial Class {Identifier(cls.Identifier.Text)}");
                _indent++;
                if (cls.BaseList is { Types.Count: > 0 } baseList)
                {
                    Line("Inherits " + Type(baseList.Types[0].Type));
                }

                foreach (var member in cls.Members)
                {
                    switch (member)
                    {
                        case FieldDeclarationSyntax field:
                            foreach (var variable in field.Declaration.Variables)
                            {
                                // BAML declares named elements WithEvents: VB code-behind uses `Handles Button1.Click`. (Not the bool _contentLoaded.)
                                var withEvents = variable.Identifier.Text != "_contentLoaded" && field.Declaration.Type is not PredefinedTypeSyntax ? "WithEvents " : string.Empty;
                                Line($"{AccessOf(field.Modifiers, "Private")} {withEvents}{Identifier(variable.Identifier.Text)} As {Type(field.Declaration.Type)}");
                            }
                            break;
                        case MethodDeclarationSyntax { Identifier.Text: "InitializeComponent" } method:
                            Line($"{AccessOf(method.Modifiers, "Public")} Sub InitializeComponent()");
                            _indent++;
                            foreach (var statement in method.Body!.Statements)
                            {
                                Statement(statement);
                            }
                            _indent--;
                            Line("End Sub");
                            break;
                        case MethodDeclarationSyntax { Identifier.Text: "__XsgApplyExtension" }:
                            foreach (var helperLine in MarkupExtensionHelper.Split('\n'))
                            {
                                Line(helperLine.TrimEnd('\r'));
                            }
                            break;
                        case MethodDeclarationSyntax { Identifier.Text: "__XsgProvideValue" }:
                        case ClassDeclarationSyntax { Identifier.Text: "__XsgExtensionServiceProvider" }:
                            // part of the canned helper printed with __XsgApplyExtension
                            break;
                        default:
                            throw new NotSupportedException($"the helper member {member.Kind()} {(member as MethodDeclarationSyntax)?.Identifier.Text ?? (member as BaseTypeDeclarationSyntax)?.Identifier.Text} of the generated class is not available for Visual Basic");
                    }
                }

                _indent--;
                Line("End Class");
            }

            private static string AccessOf(SyntaxTokenList modifiers, string fallback)
            {
                var text = string.Join(" ", modifiers.Select(m => m.Text));
                return text switch
                {
                    "public" => "Public",
                    "internal" => "Friend",
                    "private" => "Private",
                    "protected" => "Protected",
                    "protected internal" => "Protected Friend",
                    _ => fallback,
                };
            }

            private void Statement(StatementSyntax statement)
            {
                switch (statement)
                {
                    case LocalDeclarationStatementSyntax local:
                        foreach (var variable in local.Declaration.Variables)
                        {
                            var init = variable.Initializer is null ? string.Empty : " = " + Expression(variable.Initializer.Value);
                            Line(local.Declaration.Type.IsVar
                                ? $"Dim {Identifier(variable.Identifier.Text)}{init}"
                                : $"Dim {Identifier(variable.Identifier.Text)} As {Type(local.Declaration.Type)}{init}");
                        }
                        break;
                    case ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment }:
                        Assignment(assignment);
                        break;
                    case ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call }:
                        Line("Call " + Expression(call));
                        break;
                    case ExpressionStatementSyntax { Expression: ConditionalAccessExpressionSyntax access }:
                        Line("Call " + Expression(access));
                        break;
                    case IfStatementSyntax { Else: null } ifStatement:
                        Line($"If {Expression(ifStatement.Condition)} Then");
                        _indent++;
                        if (ifStatement.Statement is BlockSyntax block)
                        {
                            foreach (var inner in block.Statements)
                            {
                                Statement(inner);
                            }
                        }
                        else
                        {
                            Statement(ifStatement.Statement);
                        }
                        _indent--;
                        Line("End If");
                        break;
                    default:
                        throw new NotSupportedException($"the statement {statement.Kind()} ({Short(statement)}) has no Visual Basic form");
                }
            }

            private void Assignment(AssignmentExpressionSyntax assignment)
            {
                var left = assignment.Left;
                switch (assignment.Kind())
                {
                    case SyntaxKind.SimpleAssignmentExpression:
                        Line($"{Expression(left)} = {Expression(assignment.Right)}");
                        break;
                    case SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression when left is IdentifierNameSyntax identifier:
                    {
                        // A delegate variable (the deferred name references), not an event.
                        var method = assignment.IsKind(SyntaxKind.AddAssignmentExpression) ? "Combine" : "Remove";
                        Line($"{Identifier(identifier.Identifier.Text)} = DirectCast([Delegate].{method}({Identifier(identifier.Identifier.Text)}, {Expression(assignment.Right)}), System.Action)");
                        break;
                    }
                    case SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression:
                        Line($"{(assignment.IsKind(SyntaxKind.AddAssignmentExpression) ? "AddHandler" : "RemoveHandler")} {Expression(left)}, {Handler(assignment.Right)}");
                        break;
                    default:
                        throw new NotSupportedException($"the assignment {assignment.Kind()} has no Visual Basic form");
                }
            }

            private string Handler(ExpressionSyntax expression) =>
                expression is MemberAccessExpressionSyntax or IdentifierNameSyntax ? "AddressOf " + Expression(expression) : Expression(expression);

            private string Expression(ExpressionSyntax expression)
            {
                switch (expression)
                {
                    case LiteralExpressionSyntax literal:
                        return Literal(literal);
                    case ThisExpressionSyntax:
                        return "Me";
                    case ConditionalAccessExpressionSyntax access:
                        return Expression(access.Expression) + "?" + Binding(access.WhenNotNull);
                    case MemberBindingExpressionSyntax binding:
                        return Binding(binding);
                    case IdentifierNameSyntax identifier:
                        return Identifier(identifier.Identifier.Text);
                    case PredefinedTypeSyntax predefined:
                        return Type(predefined);
                    case AliasQualifiedNameSyntax or QualifiedNameSyntax or GenericNameSyntax:
                        return Type(expression);
                    case MemberAccessExpressionSyntax member:
                        return Expression(member.Expression) + "." + Identifier(member.Name.Identifier.Text) + GenericArguments(member.Name);
                    case InvocationExpressionSyntax call:
                        return $"{Expression(call.Expression)}({Arguments(call.ArgumentList)})";
                    case ElementAccessExpressionSyntax element:
                        return $"{Expression(element.Expression)}({Arguments(element.ArgumentList)})";
                    case ObjectCreationExpressionSyntax creation:
                        return Creation(creation);
                    case CastExpressionSyntax cast:
                        return cast.Type is PredefinedTypeSyntax { Keyword.Text: "object" }
                            ? $"CObj({Expression(cast.Expression)})"
                            : $"CType({Expression(cast.Expression)}, {Type(cast.Type)})";
                    case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AsExpression } asExpression:
                        return $"TryCast({Expression(asExpression.Left)}, {Type((TypeSyntax)asExpression.Right)})";
                    case TypeOfExpressionSyntax typeOf:
                        return $"GetType({Type(typeOf.Type)})";
                    case ParenthesizedExpressionSyntax parenthesized:
                        return "(" + Expression(parenthesized.Expression) + ")";
                    case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed:
                        return Expression(suppressed.Operand);
                    case PrefixUnaryExpressionSyntax prefix:
                        return prefix.Kind() switch
                        {
                            SyntaxKind.LogicalNotExpression => "Not " + Expression(prefix.Operand),
                            SyntaxKind.UnaryMinusExpression => "-" + Expression(prefix.Operand),
                            _ => throw new NotSupportedException($"the operator {prefix.OperatorToken.Text} has no Visual Basic form"),
                        };
                    case BinaryExpressionSyntax binary:
                        return Binary(binary);
                    case ConditionalExpressionSyntax conditional:
                        return $"If({Expression(conditional.Condition)}, {Expression(conditional.WhenTrue)}, {Expression(conditional.WhenFalse)})";
                    case ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 0 } lambda:
                        return Lambda(lambda.Body);
                    case DefaultExpressionSyntax:
                        return "Nothing";
                    default:
                        throw new NotSupportedException($"the expression {expression.Kind()} ({Short(expression)}) has no Visual Basic form");
                }
            }

            // The `.Member` / `.Method(args)` part of `a?.Member`.
            private string Binding(ExpressionSyntax whenNotNull) => whenNotNull switch
            {
                MemberBindingExpressionSyntax member => "." + Identifier(member.Name.Identifier.Text),
                InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax member } call => "." + Identifier(member.Name.Identifier.Text) + "(" + Arguments(call.ArgumentList) + ")",
                _ => throw new NotSupportedException($"the conditional access {whenNotNull.Kind()}"),
            };

            private string Lambda(CSharpSyntaxNode body)
            {
                switch (body)
                {
                    case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                        return $"Sub() {Expression(assignment.Left)} = {Expression(assignment.Right)}";
                    case InvocationExpressionSyntax call:
                        return $"Sub() {Expression(call)}";
                    case ExpressionSyntax value:
                        return $"Function() {Expression(value)}";
                    default:
                        throw new NotSupportedException("a lambda with a block body has no Visual Basic form here");
                }
            }

            private string Binary(BinaryExpressionSyntax binary)
            {
                var left = Expression(binary.Left);
                var right = Expression(binary.Right);
                var isNull = binary.Right.IsKind(SyntaxKind.NullLiteralExpression);
                return binary.Kind() switch
                {
                    SyntaxKind.EqualsExpression => isNull ? $"{left} Is Nothing" : $"{left} = {right}",
                    SyntaxKind.NotEqualsExpression => isNull ? $"{left} IsNot Nothing" : $"{left} <> {right}",
                    SyntaxKind.LogicalAndExpression => $"{left} AndAlso {right}",
                    SyntaxKind.LogicalOrExpression => $"{left} OrElse {right}",
                    SyntaxKind.AddExpression => $"{left} + {right}",
                    SyntaxKind.SubtractExpression => $"{left} - {right}",
                    SyntaxKind.MultiplyExpression => $"{left} * {right}",
                    SyntaxKind.DivideExpression => $"{left} / {right}",
                    SyntaxKind.BitwiseOrExpression => $"{left} Or {right}",
                    SyntaxKind.BitwiseAndExpression => $"{left} And {right}",
                    _ => throw new NotSupportedException($"the operator {binary.OperatorToken.Text} has no Visual Basic form"),
                };
            }

            private string Creation(ObjectCreationExpressionSyntax creation)
            {
                var text = $"New {Type(creation.Type)}({(creation.ArgumentList is null ? string.Empty : Arguments(creation.ArgumentList))})";
                if (creation.Initializer is null)
                {
                    return text;
                }

                if (creation.Initializer.IsKind(SyntaxKind.CollectionInitializerExpression))
                {
                    return text + " From {" + string.Join(", ", creation.Initializer.Expressions.Select(Expression)) + "}";
                }

                var members = creation.Initializer.Expressions.Select(e => e is AssignmentExpressionSyntax assignment
                    ? $".{Identifier(((IdentifierNameSyntax)assignment.Left).Identifier.Text)} = {Expression(assignment.Right)}"
                    : throw new NotSupportedException("an object initializer entry that is not an assignment"));
                return text + " With {" + string.Join(", ", members) + "}";
            }

            private string Arguments(BaseArgumentListSyntax list) => string.Join(", ", list.Arguments.Select(a => Expression(a.Expression)));

            private string Literal(LiteralExpressionSyntax literal)
            {
                switch (literal.Kind())
                {
                    case SyntaxKind.NullLiteralExpression:
                    case SyntaxKind.DefaultLiteralExpression:
                        return "Nothing";
                    case SyntaxKind.TrueLiteralExpression:
                        return "True";
                    case SyntaxKind.FalseLiteralExpression:
                        return "False";
                    case SyntaxKind.StringLiteralExpression:
                        return StringLiteral((string)literal.Token.Value!);
                    case SyntaxKind.CharacterLiteralExpression:
                        return "\"" + ((char)literal.Token.Value!).ToString().Replace("\"", "\"\"") + "\"C";
                    case SyntaxKind.NumericLiteralExpression:
                        return literal.Token.Value switch
                        {
                            double d => NumberText(d.ToString("R", CultureInfo.InvariantCulture)) + "R",
                            float f => NumberText(f.ToString("R", CultureInfo.InvariantCulture)) + "F",
                            long l => l.ToString(CultureInfo.InvariantCulture) + "L",
                            int i => i.ToString(CultureInfo.InvariantCulture),
                            decimal m => m.ToString(CultureInfo.InvariantCulture) + "D",
                            uint u => u.ToString(CultureInfo.InvariantCulture) + "UI",
                            _ => throw new NotSupportedException($"the number {literal.Token.Text}"),
                        };
                    default:
                        throw new NotSupportedException($"the literal {literal.Kind()}");
                }
            }

            private static string NumberText(string text) => text.IndexOfAny(new[] { '.', 'E' }) >= 0 || text is "NaN" or "Infinity" ? text : text + ".0";

            private static string StringLiteral(string value)
            {
                // Quotes double; control characters leave the literal (VB has no escapes in strings).
                var builder = new StringBuilder();
                var inLiteral = false;
                void Open() { if (!inLiteral) { if (builder.Length > 0) builder.Append(" & "); builder.Append('"'); inLiteral = true; } }
                void Close() { if (inLiteral) { builder.Append('"'); inLiteral = false; } }
                foreach (var c in value)
                {
                    switch (c)
                    {
                        case '"': Open(); builder.Append("\"\""); break;
                        case '\r': Close(); if (builder.Length > 0) builder.Append(" & "); builder.Append("vbCr"); break;
                        case '\n': Close(); if (builder.Length > 0) builder.Append(" & "); builder.Append("vbLf"); break;
                        case '\t': Close(); if (builder.Length > 0) builder.Append(" & "); builder.Append("vbTab"); break;
                        default: Open(); builder.Append(c); break;
                    }
                }

                Close();
                return builder.Length == 0 ? "\"\"" : builder.ToString();
            }

            // `global` from C# text; `Global` from a type the VB compilation displayed itself (ToDisplayString of a VB-loaded symbol prints Global.System...).
            private static string Identifier(string name) => name.Equals("Global", StringComparison.OrdinalIgnoreCase) ? "Global" : s_keywords.Contains(name) ? "[" + name + "]" : name.StartsWith("@", StringComparison.Ordinal) ? "[" + name.Substring(1) + "]" : name;

            private string Name(NameSyntax name) => Type(name);

            private string GenericArguments(SimpleNameSyntax name) =>
                name is GenericNameSyntax generic ? "(Of " + string.Join(", ", generic.TypeArgumentList.Arguments.Select(Type)) + ")" : string.Empty;

            private string Type(ExpressionSyntax type)
            {
                switch (type)
                {
                    case PredefinedTypeSyntax predefined:
                        return predefined.Keyword.Kind() switch
                        {
                            SyntaxKind.ObjectKeyword => "Object",
                            SyntaxKind.StringKeyword => "String",
                            SyntaxKind.BoolKeyword => "Boolean",
                            SyntaxKind.IntKeyword => "Integer",
                            SyntaxKind.LongKeyword => "Long",
                            SyntaxKind.DoubleKeyword => "Double",
                            SyntaxKind.FloatKeyword => "Single",
                            SyntaxKind.ByteKeyword => "Byte",
                            SyntaxKind.CharKeyword => "Char",
                            SyntaxKind.DecimalKeyword => "Decimal",
                            SyntaxKind.VoidKeyword => throw new NotSupportedException("void"),
                            _ => throw new NotSupportedException(predefined.Keyword.Text),
                        };
                    case NullableTypeSyntax nullable:
                        return Type(nullable.ElementType); // `T?` of a reference type is T; the generated code never uses a nullable value type
                    case AliasQualifiedNameSyntax alias:
                        return "Global." + Type(alias.Name);
                    case QualifiedNameSyntax qualified:
                        return Type(qualified.Left) + "." + Type(qualified.Right);
                    case GenericNameSyntax generic:
                        return Identifier(generic.Identifier.Text) + GenericArguments(generic);
                    case IdentifierNameSyntax identifier:
                        return Identifier(identifier.Identifier.Text);
                    default:
                        throw new NotSupportedException($"the type {type.Kind()} ({Short(type)}) has no Visual Basic form");
                }
            }

            private static string Short(SyntaxNode node)
            {
                var text = node.ToString().Replace("\r", " ").Replace("\n", " ");
                return text.Length > 70 ? text.Substring(0, 70) + "..." : text;
            }
        }
    }
}
