using System.Text;

namespace FreexComp.Ide.Core.Services;

/// <summary>How a line of a help document is shown (UI-MENU-032).</summary>
public enum HelpLineKind
{
    /// <summary>Body text.</summary>
    Text,

    /// <summary>A heading line (<c>#</c> … <c>######</c> followed by a space); its marks are kept.</summary>
    Heading,

    /// <summary>The header row of an aligned table.</summary>
    TableHeader,

    /// <summary>Another row of an aligned table.</summary>
    TableRow,
}

/// <summary>The text a help document shows: lines end in CR LF; <paramref name="Lines"/> has the kind of every line of <paramref name="Text"/>.</summary>
public sealed record HelpPageText(string Text, IReadOnlyList<HelpLineKind> Lines);

/// <summary>
/// UI-MENU-032: derives the text of a help document from the page's Markdown. Only tables are rewritten: consecutive
/// lines starting with <c>|</c> whose second line is a delimiter row (<c>-</c>, <c>:</c>, <c>|</c>, spaces; at least one
/// <c>-</c>) become rows without the delimiter row, the cells trimmed and every column padded with spaces to its widest
/// cell, two spaces between columns. A cell ends at a <c>|</c>; <c>\|</c> is a literal <c>|</c> of the cell. A row with
/// fewer cells than another gets empty ones. Every other line is kept as it is.
/// </summary>
public static class HelpPageFormatter
{
    private const string ColumnGap = "  ";

    public static HelpPageText Format(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var source = markdown.ReplaceLineEndings("\n").Split('\n');
        var text = new List<string>(source.Length);
        var kinds = new List<HelpLineKind>(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            if (IsRow(source[i]) && i + 1 < source.Length && IsDelimiterRow(source[i + 1]))
            {
                var end = i + 2;
                while (end < source.Length && IsRow(source[end]))
                    end++;
                List<string[]> rows = [Cells(source[i])];
                for (var r = i + 2; r < end; r++)
                    rows.Add(Cells(source[r]));
                AppendTable(rows, text, kinds);
                i = end;
            }
            else
            {
                text.Add(source[i]);
                kinds.Add(IsHeading(source[i]) ? HelpLineKind.Heading : HelpLineKind.Text);
                i++;
            }
        }

        return new HelpPageText(string.Join("\r\n", text), kinds);
    }

    /// <summary>A heading line: one to six <c>#</c> followed by a space.</summary>
    public static bool IsHeading(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var marks = 0;
        while (marks < line.Length && line[marks] == '#')
            marks++;
        return marks is >= 1 and <= 6 && marks < line.Length && line[marks] == ' ';
    }

    private static bool IsRow(string line) => line.StartsWith('|');

    private static bool IsDelimiterRow(string line) =>
        IsRow(line) && line.Contains('-') && line.All(c => c is '-' or ':' or '|' or ' ' or '\t');

    /// <summary>The trimmed cells of a row: the text between its <c>|</c> marks (the one closing the row is optional).</summary>
    private static string[] Cells(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        for (var i = 1; i < line.Length; i++)
        {
            if (line[i] == '\\' && i + 1 < line.Length && line[i + 1] == '|')
            {
                cell.Append('|');
                i++;
            }
            else if (line[i] == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                cell.Append(line[i]);
            }
        }

        if (cell.ToString().Trim() is { Length: > 0 } last)
            cells.Add(last);
        return [.. cells];
    }

    private static void AppendTable(List<string[]> rows, List<string> text, List<HelpLineKind> kinds)
    {
        var widths = new int[rows.Max(r => r.Length)];
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
                widths[c] = Math.Max(widths[c], row[c].Length);
        }

        foreach (var row in rows)
        {
            var line = new StringBuilder();
            for (var c = 0; c < widths.Length; c++)
            {
                if (c > 0)
                    line.Append(ColumnGap);
                line.Append((c < row.Length ? row[c] : string.Empty).PadRight(widths[c]));
            }

            text.Add(line.ToString().TrimEnd());
            kinds.Add(ReferenceEquals(row, rows[0]) ? HelpLineKind.TableHeader : HelpLineKind.TableRow);
        }
    }
}
