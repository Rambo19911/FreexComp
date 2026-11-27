using System.Text;

namespace FreexComp.Ide.Core.Diff;

/// <summary>A line of a text and the line break that ends it (empty for a last line without one).</summary>
internal readonly record struct DiffTextLine(string Text, string Break);

/// <summary>IDE-DIFF-001/002/006: line splitting, comparison keys and line-break kinds.</summary>
internal static class DiffText
{
    public const string Mixed = "Mixed";

    /// <summary>IDE-DIFF-001: split at <c>\r\n</c>, <c>\n</c>, <c>\r</c>; a final line without a break is a line; an empty text has none.</summary>
    public static List<DiffTextLine> Split(string text)
    {
        var lines = new List<DiffTextLine>();
        var start = 0;
        while (start < text.Length)
        {
            var at = text.AsSpan(start).IndexOfAny('\r', '\n');
            if (at < 0)
            {
                lines.Add(new DiffTextLine(text[start..], ""));
                break;
            }

            at += start;
            var lineBreak = text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n' ? "\r\n" : text[at] == '\r' ? "\r" : "\n";
            lines.Add(new DiffTextLine(text[start..at], lineBreak));
            start = at + lineBreak.Length;
        }

        return lines;
    }

    /// <summary>IDE-DIFF-002: leading and trailing whitespace removed, inner runs collapsed to one space.</summary>
    public static string CollapseWhitespace(string text)
    {
        var trimmed = text.AsSpan().Trim();
        var s = new StringBuilder(trimmed.Length);
        var inRun = false;
        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!inRun)
                    s.Append(' ');
                inRun = true;
            }
            else
            {
                s.Append(c);
                inRun = false;
            }
        }

        return s.ToString();
    }

    /// <summary>The most frequent line break (ties: CRLF, LF, CR), or null for a text without one.</summary>
    public static string? MostFrequentBreak(List<DiffTextLine> lines)
    {
        var (crlf, lf, cr) = Count(lines);
        if (crlf + lf + cr == 0)
            return null;
        return crlf >= lf && crlf >= cr ? "\r\n" : lf >= cr ? "\n" : "\r";
    }

    /// <summary>IDE-DIFF-006: <c>CRLF</c>, <c>LF</c>, <c>CR</c> when one kind has ≥ 90 % of the breaks, else <see cref="Mixed"/>; null without breaks.</summary>
    public static string? BreakKind(List<DiffTextLine> lines)
    {
        var (crlf, lf, cr) = Count(lines);
        var total = crlf + lf + cr;
        if (total == 0)
            return null;
        var most = MostFrequentBreak(lines);
        var count = most switch { "\r\n" => crlf, "\n" => lf, _ => cr };
        if (count * 10 < total * 9)
            return Mixed;
        return most switch { "\r\n" => "CRLF", "\n" => "LF", _ => "CR" };
    }

    private static (int CrLf, int Lf, int Cr) Count(List<DiffTextLine> lines)
    {
        int crlf = 0, lf = 0, cr = 0;
        foreach (var line in lines)
        {
            switch (line.Break)
            {
                case "\r\n":
                    crlf++;
                    break;
                case "\n":
                    lf++;
                    break;
                case "\r":
                    cr++;
                    break;
            }
        }

        return (crlf, lf, cr);
    }
}
