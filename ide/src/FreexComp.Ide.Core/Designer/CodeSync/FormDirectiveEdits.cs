using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Designer.Editing;

namespace FreexComp.Ide.Core.Designer.CodeSync;

/// <summary>The <c>{$FORM}</c> directive of the unit (code-sync.md §3, DSG-CS-020 … 022).</summary>
public static class FormDirectiveEdits
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Makes the implementation section name <paramref name="formFileName"/> (no directory, e.g. <c>MainForm.form</c>):
    /// a directive with another argument gets only its argument replaced; a missing one is inserted on its own line after
    /// the <c>implementation</c> line (after an implementation <c>uses</c> clause), with one blank line before and after,
    /// existing blank lines reused. Two or more directives are not touched. Needs state <c>Ok</c>.
    /// </summary>
    public static IReadOnlyList<TextEditRecord> EnsureFormDirective(FormUnit unit, string formFileName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(formFileName);
        var info = unit.RequireFormClass();
        var implementation = info.Unit.ImplementationSection;
        var directives = info.Tree.Directives
            .Where(d => d.IsActive && Names.Equals(d.Name, "FORM") && d.Span.Start >= implementation.ImplementationKeyword.Span.Start)
            .ToList();
        var text = info.Tree.Text;
        switch (directives.Count)
        {
            case 1 when Names.Equals(directives[0].Argument, formFileName):
                return [];
            case 1:
                return unit.Apply([ReplaceArgument(text.Text, directives[0], formFileName)]);
            case > 1:
                return [];
        }

        var le = unit.Options(info).LineEnding;
        var anchor = implementation.UsesClause?.Span.End ?? implementation.ImplementationKeyword.Span.End;
        var line = text.Lines.GetLinePosition(anchor).Line;
        var prefix = "";
        int insertAt;
        if (IsBlank(text, line + 1))
        {
            insertAt = LineStart(text, line + 2); // the blank line before is reused
        }
        else
        {
            insertAt = LineStart(text, line + 1);
            prefix = insertAt == text.Text.Length && text.Text is not [.., '\n'] ? le + le : le;
        }

        var suffix = IsBlank(text, text.Lines.GetLinePosition(insertAt).Line) && insertAt < text.Text.Length ? "" : le;
        return unit.Apply([new TextChange(new TextSpan(insertAt, 0), prefix + "{$FORM " + formFileName + "}" + le + suffix)]);
    }

    /// <summary>The argument inside <c>{$FORM …}</c> / <c>(*$FORM …*)</c>: before the closer and its preceding whitespace.</summary>
    private static TextChange ReplaceArgument(string text, DirectiveTriviaInfo directive, string formFileName)
    {
        var closer = text[directive.Span.Start] == '{' ? 1 : 2;
        var end = directive.Span.End - closer;
        while (end > directive.Span.Start && text[end - 1] is ' ' or '\t' or '\r' or '\n')
        {
            end--;
        }

        return directive.Argument.Length == 0
            ? new TextChange(new TextSpan(end, 0), " " + formFileName)
            : new TextChange(new TextSpan(end - directive.Argument.Length, directive.Argument.Length), formFileName);
    }

    private static int LineStart(FreexComp.Compiler.SourceText text, int line) =>
        line < text.Lines.LineCount ? text.Lines.GetLineSpan(line).Start : text.Text.Length;

    private static bool IsBlank(FreexComp.Compiler.SourceText text, int line)
    {
        if (line >= text.Lines.LineCount)
        {
            return false;
        }

        var span = text.Lines.GetLineSpan(line);
        for (var i = span.Start; i < span.End; i++)
        {
            if (text.Text[i] is not (' ' or '\t' or '\r' or '\n'))
            {
                return false;
            }
        }

        return true;
    }
}
