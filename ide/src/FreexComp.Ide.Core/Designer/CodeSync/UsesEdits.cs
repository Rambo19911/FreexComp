using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Editing;
using TextChange = FreexComp.Compiler.Syntax.TextChange;

namespace FreexComp.Ide.Core.Designer.CodeSync;

/// <summary>
/// The interface <c>uses</c> clause (code-sync.md §5, DSG-CS-051 … 054) through <c>SyntaxEdits.AddUsesEntry</c>
/// (api.md CMP-257). Units are only ever added.
/// </summary>
public static class UsesEdits
{
    /// <summary>
    /// Makes every unit of <paramref name="units"/> present in the interface clause, in order, as one code-editor undo
    /// group: appended after the last entry (wrapped at <see cref="FormUnit.RightMargin"/>), or in a new clause after the
    /// <c>interface</c> line. A unit already named there (full dotted name, case-insensitive) is skipped.
    /// </summary>
    public static IReadOnlyList<TextEditRecord> EnsureUnits(FormUnit unit, IReadOnlyList<string> units)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(units);
        var info = unit.RequireFormClass();
        var records = new List<TextEditRecord>();
        using (unit.UndoGroup())
        {
            foreach (var name in units)
            {
                if (SyntaxEdits.UsesContains(info.Unit, name, interfaceSection: true))
                {
                    continue;
                }

                var options = unit.Options(info);
                var change = SyntaxEdits.AddUsesEntry(info.Unit, name, interfaceSection: true, options);
                if (info.UsesClause is not { Units.Count: > 0 } && !NextTokenFollowsBlankLine(info))
                {
                    change = change with { NewText = change.NewText + options.LineEnding }; // DSG-CS-053
                }

                records.AddRange(unit.Apply([change]));
                info = unit.RequireFormClass();
            }
        }

        return records;
    }

    /// <summary>DSG-CS-053: the line before the first token after <c>interface</c> is blank (and is not the keyword's line).</summary>
    private static bool NextTokenFollowsBlankLine(FormClassInfo info)
    {
        var text = info.Tree.Text;
        var keyword = info.Unit.InterfaceSection.InterfaceKeyword;
        var keywordLine = text.Lines.GetLinePosition(keyword.Span.End).Line;
        var previous = text.Lines.GetLinePosition(keyword.GetNextToken().Span.Start).Line - 1;
        return previous > keywordLine && text.Text[text.Lines.GetLineSpan(previous).Start..text.Lines.GetLineSpan(previous).End].Trim(' ', '\t').Length == 0;
    }
}
