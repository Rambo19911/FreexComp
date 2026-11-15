using FreexComp.Compiler.Syntax;
using FreexComp.Ide.Core.Text;
using CompilerText = FreexComp.Compiler.Text;

namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// IDE-ED-024 block completion (I-120): Enter after an opener whose closer is missing in the syntax tree adds the closer
/// line(s) below the new line, at the opener line's indentation.
/// </summary>
public static class BlockCompletion
{
    /// <summary>
    /// <paramref name="enter"/> (the plain Enter edit at an empty <paramref name="selection"/>) extended by the closer lines,
    /// or null when the old line does not end with an opener or the tree has its closer.
    /// </summary>
    public static EditResult? Extend(ITextBuffer buffer, TextSelection selection, EditResult enter, string path, string newLine)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(enter);
        if (!selection.IsEmpty || enter.Edits.Count != 1)
            return null;
        var caret = selection.Caret;
        var position = buffer.GetPosition(caret);
        var (lineOffset, lineLength, _) = buffer.GetLine(position.Line);
        var line = buffer.GetText(lineOffset, lineLength);
        var column = caret - lineOffset;
        if (line[column..].Trim(' ', '\t').Length > 0)
            return null; // the old line must end at the caret
        var last = line[..column].TrimEnd(' ', '\t').Length;
        if (last == 0)
            return null;

        var text = buffer.Text;
        var tree = SyntaxTree.Parse(FreexComp.Compiler.SourceText.From(CompilerText.PathUtil.Normalize(path), text));
        var token = tree.FindToken(lineOffset + last - 1);
        if (token.Span.End != lineOffset + last || token.IsMissing)
            return null; // a comment or an unfinished token ends the line
        if (Closers(token) is not { } closers || !HasMissingCloser(token))
            return null;

        if (closers[^1] == "end;" && ElseFollows(text, caret))
            closers[^1] = "end";
        var indent = line[..(line.Length - line.TrimStart(' ', '\t').Length)];
        var edit = enter.Edits[0];
        var added = string.Concat(closers.Select(c => newLine + indent + c));
        return new EditResult([edit with { NewText = edit.NewText + added }], enter.Selection);
    }

    /// <summary>The closer lines of the opener ending the line, or null when it is no opener.</summary>
    private static string[]? Closers(SyntaxToken token) => token.Kind switch
    {
        SyntaxKind.BeginKeyword or SyntaxKind.RecordKeyword => ["end;"],
        SyntaxKind.TryKeyword => ["finally", "end;"],
        SyntaxKind.RepeatKeyword => ["until ;"],
        SyntaxKind.OfKeyword when token.Parent is CaseStatementSyntax => ["end;"],
        _ when IsTypeHeading(token) => ["end;"],
        _ => null,
    };

    /// <summary><c>= class</c>, <c>= interface</c> or their heritage list ending the line.</summary>
    private static bool IsTypeHeading(SyntaxToken token) => token.Kind switch
    {
        SyntaxKind.ClassKeyword or SyntaxKind.InterfaceKeyword => token.Parent is ClassTypeSyntax or InterfaceTypeSyntax,
        SyntaxKind.CloseParenToken => token.Parent is HeritageSyntax { Parent: ClassTypeSyntax or InterfaceTypeSyntax },
        _ => false,
    };

    /// <summary>
    /// The opener has no matching closer: its node or an enclosing one lacks its <c>end</c> / <c>until</c> (an unmatched
    /// opener takes the next closer, so the shortage shows further out).
    /// </summary>
    private static bool HasMissingCloser(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            foreach (var child in node.ChildNodesAndTokens())
            {
                if (child.IsToken && child.AsToken() is { IsMissing: true, Kind: SyntaxKind.EndKeyword or SyntaxKind.UntilKeyword })
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The next word after <paramref name="offset"/> is <c>else</c> (read from the text: an <c>else</c> the parser could
    /// not attach is skipped trivia in the tree).
    /// </summary>
    private static bool ElseFollows(string text, int offset)
    {
        var p = offset;
        while (p < text.Length && char.IsWhiteSpace(text[p]))
            p++;
        return string.Compare(text, p, "else", 0, 4, StringComparison.OrdinalIgnoreCase) == 0
            && (p + 4 == text.Length || !(char.IsLetterOrDigit(text[p + 4]) || text[p + 4] == '_'));
    }
}
