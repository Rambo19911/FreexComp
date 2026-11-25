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

    /// <summary>A list item (I-266): body text; wrapped, it continues under its text (<see cref="HelpPageFormatter.ListItemTextStart"/>).</summary>
    ListItem,
}

/// <summary>The text a help document shows: lines end in CR LF; <paramref name="Lines"/> has the kind of every line of <paramref name="Text"/>.</summary>
public sealed record HelpPageText(string Text, IReadOnlyList<HelpLineKind> Lines);

/// <summary>
/// UI-MENU-032: derives the text of a help document from the page's Markdown. Tables: consecutive
/// lines starting with <c>|</c> whose second line is a delimiter row (<c>-</c>, <c>:</c>, <c>|</c>, spaces; at least one
/// <c>-</c>) become rows without the delimiter row, the cells trimmed and every column padded with spaces to its widest
/// cell, two spaces between columns. A cell ends at a <c>|</c>; <c>\|</c> is a literal <c>|</c> of the cell. A row with
/// fewer cells than another gets empty ones. Paragraphs (I-266): consecutive plain lines become one line, joined with
/// one space (the view wraps it); a list item starts a paragraph of its own and the plain lines after it join it. Blank
/// lines, heading lines, other lines starting with <c>|</c> and lines indented by four or more spaces (or a tab) are
/// kept as they are and end the paragraph before them.
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
        var paragraph = -1; // the index in text of the paragraph the next plain line joins; -1: none
        while (i < source.Length)
        {
            var line = source[i];
            if (IsRow(line) && i + 1 < source.Length && IsDelimiterRow(source[i + 1]))
            {
                var end = i + 2;
                while (end < source.Length && IsRow(source[end]))
                    end++;
                List<string[]> rows = [Cells(line)];
                for (var r = i + 2; r < end; r++)
                    rows.Add(Cells(source[r]));
                AppendTable(rows, text, kinds);
                i = end;
                paragraph = -1;
                continue;
            }

            i++;
            if (string.IsNullOrWhiteSpace(line) || IsHeading(line) || IsRow(line) || IsIndentedCode(line))
            {
                text.Add(line);
                kinds.Add(IsHeading(line) ? HelpLineKind.Heading : HelpLineKind.Text);
                paragraph = -1;
            }
            else if (ListItemTextStart(line) > 0 || paragraph < 0)
            {
                paragraph = text.Count;
                text.Add(line.TrimEnd());
                kinds.Add(ListItemTextStart(line) > 0 ? HelpLineKind.ListItem : HelpLineKind.Text);
            }
            else
            {
                text[paragraph] += " " + line.Trim();
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

    /// <summary>
    /// The column (0-based) at which the text of a list item starts, 0 when the line is no list item. A list item is at
    /// most three spaces, a marker (<c>-</c>, <c>*</c>, <c>+</c>, or one to nine digits and <c>.</c> or <c>)</c>), at
    /// least one space, and text.
    /// </summary>
    public static int ListItemTextStart(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var i = 0;
        while (i < line.Length && line[i] == ' ')
            i++;
        if (i > 3 || i == line.Length)
            return 0;
        if (line[i] is '-' or '*' or '+')
        {
            i++;
        }
        else
        {
            var digits = i;
            while (i < line.Length && char.IsAsciiDigit(line[i]))
                i++;
            if (i == digits || i - digits > 9 || i == line.Length || line[i] is not ('.' or ')'))
                return 0;
            i++;
        }

        var text = i;
        while (text < line.Length && line[text] == ' ')
            text++;
        return text > i && text < line.Length ? text : 0;
    }

    /// <summary>A line indented by four or more spaces, or by a tab.</summary>
    private static bool IsIndentedCode(string line) => line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith('\t');

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
