using System.Globalization;
using System.Text;
using FreexComp.Compiler.Syntax;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Designer.CodeSync;

/// <summary>
/// Event handlers of the form class (code-sync.md §5 DSG-CS-050, §6, §7) through <c>SyntaxEdits</c> (api.md CMP-253 …
/// 256). Edits need state <c>Ok</c> (DSG-CS-003), parse the current text for each step and apply all steps of one call
/// as one code-editor undo group (DSG-CS-001, 002).
/// </summary>
public static class HandlerEdits
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// DSG-CS-050: same kind, same number of parameters, per parameter the same modifier and type identifier (case- and
    /// whitespace-insensitive, unit qualifiers stripped), for functions the same result type; names are ignored. Class
    /// methods, constructors, destructors and <c>overload</c> methods never match. An unparsable signature matches nothing.
    /// </summary>
    public static bool SignatureMatches(MethodDeclarationSyntax method, string signatureText)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(signatureText);
        var heading = method.Heading;
        if (heading.ClassKeyword is not null || Kind(heading.Keyword) is not { } kind
            || method.Directives.Any(d => Names.Equals(d.Keyword.Text, "overload"))
            || ParseSignature(signatureText) is not { } signature || !Names.Equals(kind, signature.Keyword.Text))
        {
            return false;
        }

        return Parameters(heading.ParameterList).SequenceEqual(Parameters(signature.ParameterList))
               && Names.Equals(TypeKey(heading.ResultType), TypeKey(signature.ResultType));
    }

    /// <summary>The methods (any section) of the class matching <paramref name="signatureText"/>, sorted by name.</summary>
    public static IReadOnlyList<string> GetCompatibleMethods(FormClassInfo info, string signatureText)
    {
        ArgumentNullException.ThrowIfNull(info);
        return [.. info.Methods.Where(m => SignatureMatches(m.Declaration, signatureText)).Select(m => m.Name).Order(Names)];
    }

    /// <summary>
    /// DSG-CS-060 … 062, 064: the declaration after the last method of the managed section (else after its last field,
    /// else at its start) and the implementation stub after the last method implementation of the class (else after the
    /// <c>{$FORM}</c> line, the implementation <c>uses</c>, the <c>implementation</c> line). An existing declaration or
    /// implementation of that name is kept, so only the missing part is added.
    /// </summary>
    /// <exception cref="ArgumentException">The signature is not a procedural type.</exception>
    /// <exception cref="InvalidOperationException">The state is not <c>Ok</c>.</exception>
    /// <exception cref="CodeSyncException">The name is another kind of member (D-065, <c>Msg.Designer.NameConflictsWithMember</c>).</exception>
    public static IReadOnlyList<TextEditRecord> CreateHandler(FormUnit unit, string methodName, string signatureText)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(methodName);
        ArgumentNullException.ThrowIfNull(signatureText);
        var signature = ParseSignature(signatureText) ?? throw new ArgumentException("Not a procedural signature: " + signatureText, nameof(signatureText));
        var kind = signature.Keyword.Text.ToLowerInvariant();
        var tail = ParameterText(signature.ParameterList) + (signature.ResultType is { } result ? ": " + Format(result) : "") + ";";
        var info = unit.RequireFormClass();
        var records = new List<TextEditRecord>();
        using (unit.UndoGroup())
        {
            switch (SyntaxEdits.FindMember(info.Class, methodName))
            {
                case null:
                    records.AddRange(unit.Apply([SyntaxEdits.AddMethodDeclaration(info.Declaration, kind + " " + methodName + tail, unit.Options(info))]));
                    info = unit.RequireFormClass();
                    break;
                case MethodDeclarationSyntax:
                    break;
                default:
                    throw new CodeSyncException(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_NameConflictsWithMember, info.ClassName, methodName));
            }

            if (!info.Implementations.ContainsKey(methodName))
            {
                records.AddRange(unit.Apply([ImplementationStub(unit, info, kind + " " + info.ClassName + "." + methodName + tail)]));
            }
        }

        return records;
    }

    /// <summary>
    /// DSG-CS-063 (last sentence): when the class declares <paramref name="methodName"/> but the unit has no implementation,
    /// the stub of DSG-CS-061/062 is added (an undo group of its own); otherwise nothing.
    /// </summary>
    public static IReadOnlyList<TextEditRecord> EnsureImplementation(FormUnit unit, string methodName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var info = unit.RequireFormClass();
        if (info.Implementations.ContainsKey(methodName) || SyntaxEdits.FindMember(info.Class, methodName) is not MethodDeclarationSyntax method)
        {
            return [];
        }

        var heading = method.Heading;
        var text = heading.Keyword.Text.ToLowerInvariant() + " " + info.ClassName + "." + ((SimpleNameSyntax)heading.Name).Identifier.Text
                   + ParameterText(heading.ParameterList) + (heading.ResultType is { } result ? ": " + Format(result) : "") + ";";
        return unit.Apply([ImplementationStub(unit, info, text)]);
    }

    /// <summary>
    /// DSG-CS-063 caret: (a) the first comment or token after <c>begin</c> when the body has one; (b) else the end of the
    /// first whitespace-only line between the <c>begin</c> and <c>end</c> lines; (c) else the start of <c>end</c>. Null when
    /// the unit has no implementation with a body for the method.
    /// </summary>
    public static int? CaretOffset(FormClassInfo info, string methodName)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!info.Implementations.TryGetValue(methodName, out var implementation) || implementation.Block is not { Body: var body })
        {
            return null;
        }

        var begin = body.BeginKeyword;
        var next = begin.GetNextToken();
        foreach (var trivia in begin.TrailingTrivia.Concat(next.LeadingTrivia))
        {
            if (trivia.Kind is not (SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia))
            {
                return trivia.Span.Start;
            }
        }

        if (next.Span.Start != body.EndKeyword.Span.Start)
        {
            return next.Span.Start;
        }

        var lines = info.Tree.Text.Lines;
        var beginLine = lines.GetLinePosition(begin.Span.End).Line;
        var endLine = lines.GetLinePosition(body.EndKeyword.Span.Start).Line;
        return endLine - beginLine >= 2 ? lines.GetLineSpan(beginLine + 1).End : body.EndKeyword.Span.Start;
    }

    /// <summary>DSG-CS-066: the name in the class declaration and in the implementation heading; calls are not changed.</summary>
    public static IReadOnlyList<TextEditRecord> RenameHandler(FormUnit unit, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var info = unit.RequireFormClass();
        return unit.Apply(SyntaxEdits.RenameMethod(info.Unit, info.ClassName, oldName, newName));
    }

    /// <summary>
    /// DSG-CS-070: the implementation body has no statements, comments or local declarations; a declaration without
    /// implementation is empty too. A name the class does not declare or implement is not empty.
    /// </summary>
    public static bool IsHandlerEmpty(FormClassInfo info, string methodName)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.Implementations.TryGetValue(methodName, out var implementation)
            ? SyntaxEdits.IsEmptyMethodBody(implementation)
            : SyntaxEdits.FindMember(info.Class, methodName) is MethodDeclarationSyntax;
    }

    /// <summary>
    /// DSG-CS-072 (b … d): removes each of <paramref name="candidates"/> that the class declares as a method, that is empty
    /// and that <paramref name="referenced"/> does not contain — declaration line(s), implementation and one adjacent blank
    /// line — as one code-editor undo group. Returns the edits and the removed names.
    /// </summary>
    public static (IReadOnlyList<TextEditRecord> Edits, IReadOnlyList<string> Removed) RemoveEmptyHandlers(
        FormUnit unit, IEnumerable<string> candidates, ISet<string> referenced)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(referenced);
        var info = unit.RequireFormClass();
        var records = new List<TextEditRecord>();
        var removed = new List<string>();
        using (unit.UndoGroup())
        {
            foreach (var name in candidates)
            {
                if (referenced.Contains(name) || SyntaxEdits.FindMember(info.Class, name) is not MethodDeclarationSyntax || !IsHandlerEmpty(info, name))
                {
                    continue;
                }

                records.AddRange(unit.Apply(SyntaxEdits.RemoveMethod(info.Unit, info.ClassName, name)));
                removed.Add(name);
                info = unit.RequireFormClass();
            }
        }

        return (records, removed);
    }

    /// <summary>DSG-CS-062: <c>heading⏎begin⏎IU⏎end;</c> at the DSG-CS-061 position.</summary>
    private static TextChange ImplementationStub(FormUnit unit, FormClassInfo info, string headingText)
    {
        var options = unit.Options(info);
        var le = options.LineEnding;
        return SyntaxEdits.InsertMethodImplementation(info.Unit, info.ClassName, headingText + le + "begin" + le + options.IndentUnit + le + "end;");
    }

    /// <summary>A signature text such as <c>procedure(Sender: TObject)</c> as a procedural type, or null.</summary>
    internal static ProceduralTypeSyntax? ParseSignature(string text)
    {
        var type = SyntaxFactory.ParseNode<TypeSyntax>(text);
        return type is ProceduralTypeSyntax procedural && !type.SyntaxTree.Diagnostics.Any() && procedural.ReferenceKeyword is null
            ? procedural
            : null;
    }

    /// <summary><c>procedure</c> or <c>function</c> (lower case); null for constructors, destructors and operators.</summary>
    private static string? Kind(SyntaxToken keyword) =>
        keyword.Text.ToLowerInvariant() is "procedure" or "function" ? keyword.Text.ToLowerInvariant() : null;

    /// <summary>One (modifier, type key) per declared parameter name.</summary>
    private static IEnumerable<(string Modifier, string Type)> Parameters(ParameterListSyntax? list) =>
        list is null
            ? []
            : list.Parameters.SelectMany(p => p.Identifiers.Select(_ => ((p.Modifier?.Text ?? "").ToLowerInvariant(), TypeKey(p.Type).ToLowerInvariant())));

    /// <summary>The compared type text: the last identifier of a named type, else its tokens without whitespace.</summary>
    private static string TypeKey(TypeSyntax? type) => type switch
    {
        null => "",
        NamedTypeSyntax { Name: QualifiedNameSyntax q } => q.Right.Identifier.Text,
        NamedTypeSyntax { Name: SimpleNameSyntax s } => s.Identifier.Text,
        _ => string.Concat(type.DescendantTokens().Select(t => t.Text)),
    };

    /// <summary>The parameter list text with parentheses, normalised (DSG-CS-060); empty when there are no parameters.</summary>
    private static string ParameterText(ParameterListSyntax? list) =>
        list is { Parameters.Count: > 0 } ? Format(list) : "";

    /// <summary>
    /// The tokens of <paramref name="node"/> with single spaces after <c>:</c>, <c>;</c> and <c>,</c>, around <c>=</c> and
    /// between two words, and no other whitespace.
    /// </summary>
    private static string Format(SyntaxNode node)
    {
        var text = new StringBuilder();
        string? previous = null;
        foreach (var token in node.DescendantTokens())
        {
            var current = token.Text;
            if (previous is not null && (previous is ":" or ";" or "," or "=" || current == "=" || (IsWord(previous) && IsWord(current))))
            {
                text.Append(' ');
            }

            text.Append(current);
            previous = current;
        }

        return text.ToString();
    }

    private static bool IsWord(string token) => token.Length > 0 && (char.IsLetterOrDigit(token[^1]) || token[^1] == '_')
                                                && (char.IsLetterOrDigit(token[0]) || token[0] is '_' or '&' or '\'' or '#' or '$');
}
