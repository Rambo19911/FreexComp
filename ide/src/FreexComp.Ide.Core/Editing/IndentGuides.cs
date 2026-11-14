using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Editing;

/// <summary>Indentation guides (IDE-ED-073, UI-ED-056): where they are drawn, in visual columns (tabs expanded).</summary>
public static class IndentGuides
{
    /// <summary>
    /// For each line <paramref name="firstLine"/>…<paramref name="lastLine"/> (1-based) the 0-based visual columns of
    /// its guides: every multiple of <paramref name="tabWidth"/> strictly less than the line's indentation. A blank
    /// (empty or whitespace-only) line takes the smaller indentation of the nearest non-blank lines above and below
    /// (a missing neighbour counts as 0).
    /// </summary>
    public static IReadOnlyList<int>[] Compute(ITextBuffer buffer, int firstLine, int lastLine, int tabWidth)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        tabWidth = Math.Max(1, tabWidth);
        firstLine = Math.Max(1, firstLine);
        lastLine = Math.Min(buffer.LineCount, lastLine);
        var result = new IReadOnlyList<int>[Math.Max(0, lastLine - firstLine + 1)];
        for (int line = firstLine; line <= lastLine; line++)
        {
            var indent = Indentation(buffer, line, tabWidth) ?? Math.Min(Neighbour(buffer, line, -1, tabWidth), Neighbour(buffer, line, +1, tabWidth));
            var columns = new List<int>();
            for (int column = 0; column < indent; column += tabWidth)
                columns.Add(column);
            result[line - firstLine] = columns;
        }

        return result;
    }

    /// <summary>
    /// The guide of the innermost block containing <paramref name="caret"/> (IDE-ED-052 pairing): the opener line's
    /// indentation over the lines between opener and closer; null outside every block or for a block without inner lines.
    /// </summary>
    public static ActiveGuide? Active(ITextBuffer buffer, CodeTokens tokens, int caret, int tabWidth)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.BlockAt(caret) is not var (opener, closer))
            return null;
        int first = buffer.GetPosition(opener.Offset).Line, last = buffer.GetPosition(closer.Offset).Line;
        if (last - first < 2)
            return null;
        return new ActiveGuide(Indentation(buffer, first, Math.Max(1, tabWidth)) ?? 0, first + 1, last - 1);
    }

    /// <summary>The visual width of the line's leading whitespace; null for a blank line.</summary>
    private static int? Indentation(ITextBuffer buffer, int line, int tabWidth)
    {
        var (offset, length, _) = buffer.GetLine(line);
        var text = buffer.GetText(offset, length);
        int width = 0;
        foreach (var c in text)
        {
            if (c == ' ')
                width++;
            else if (c == '\t')
                width += tabWidth - width % tabWidth;
            else
                return width;
        }

        return null;
    }

    private static int Neighbour(ITextBuffer buffer, int line, int step, int tabWidth)
    {
        for (int l = line + step; l >= 1 && l <= buffer.LineCount; l += step)
        {
            if (Indentation(buffer, l, tabWidth) is { } indent)
                return indent;
        }

        return 0;
    }
}
