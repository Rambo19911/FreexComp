namespace FreexComp.LanguageService.Formatting;

/// <summary>
/// CMP-1451: finds the written lines longer than <see cref="FormatProfile.MaxLineLength"/> and breaks each after the last list
/// separator (<see cref="LayoutPlan.BreakAfter"/>) that fits, again on the rest of the line while it is too long. The token after
/// a break starts its line at its continuation level (<see cref="LayoutPlan.Levels"/>, as a kept line break there would be), so
/// the next run keeps the break as it is. A line without a separator that fits stays long.
/// </summary>
internal static class LineBreaker
{
    /// <summary>Marks the breaks in <paramref name="plan"/>; false when no line needs one.</summary>
    public static bool Mark(LayoutPlan plan, FormatProfile profile, string formatted, List<LayoutWriter.Gap> gaps)
    {
        var max = profile.MaxLineLength;
        if (max <= 0)
        {
            return false;
        }

        var changed = false;
        var g = 0;
        for (var lineStart = 0; lineStart < formatted.Length;)
        {
            var eol = formatted.AsSpan(lineStart).IndexOfAny('\r', '\n');
            var lineEnd = eol < 0 ? formatted.Length : lineStart + eol;
            if (lineEnd - lineStart > max)
            {
                while (g < gaps.Count && gaps[g].NewEnd < lineStart)
                {
                    g++;
                }

                changed |= BreakLine(plan, profile, formatted, gaps, g, lineStart, lineEnd);
            }

            if (eol < 0)
            {
                break;
            }

            lineStart = lineEnd + (formatted[lineEnd] == '\r' && lineEnd + 1 < formatted.Length && formatted[lineEnd + 1] == '\n' ? 2 : 1);
        }

        return changed;
    }

    /// <summary>The breaks of one long line; <paramref name="g"/> is the first gap whose token may be on it.</summary>
    private static bool BreakLine(LayoutPlan plan, FormatProfile profile, string formatted, List<LayoutWriter.Gap> gaps, int g, int lineStart, int lineEnd)
    {
        var max = profile.MaxLineLength;
        var changed = false;
        var origin = lineStart;     // the current (rest of the) line starts here …
        var indent = 0;             // … at this column
        while (lineEnd - origin + indent > max)
        {
            var best = -1;
            for (var j = g; j + 1 < gaps.Count && gaps[j + 1].NewEnd < lineEnd; j++)
            {
                var separator = gaps[j].Token;
                var next = gaps[j + 1];
                if (gaps[j].NewEnd < origin || next.NewEnd <= origin || !plan.BreakAfter[separator] || next.Token != separator + 1
                    || plan.Rules[next.Token] != GapRule.Free)
                {
                    continue;
                }

                var end = next.NewEnd;      // the first part ends before the spaces in front of the next token
                while (end > next.NewStart && formatted[end - 1] is ' ' or '\t')
                {
                    end--;
                }

                if (end - origin + indent > max)
                {
                    break;
                }

                best = j + 1;
            }

            if (best < 0)
            {
                break;
            }

            var token = gaps[best].Token;
            plan.ForceLine(token);
            changed = true;
            origin = gaps[best].NewEnd;
            indent = Math.Max(plan.Levels[token], 0) * profile.IndentSize;
        }

        return changed;
    }
}
