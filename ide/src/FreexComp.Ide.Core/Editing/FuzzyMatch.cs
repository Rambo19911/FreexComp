namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// Case-insensitive subsequence matching with the ranking of dialogs.md UI-DLG-131/142: tier 0 = prefix, 1 = word starts
/// (every matched character starts a word or directly follows the previous matched one, the first starts a word),
/// 2 = contiguous substring, 3 = any other subsequence. Lower tiers rank first.
/// </summary>
public static class FuzzyMatch
{
    /// <summary>The best tier and the matched character positions, or null when <paramref name="query"/> is no subsequence.</summary>
    public static (int Tier, IReadOnlyList<int> Positions)? Match(string candidate, string query, Func<string, int, bool> isWordStart)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(isWordStart);
        if (query.Length == 0)
            return (0, []);
        if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return (0, Range(0, query.Length));
        var positions = new int[query.Length];
        if (WordStarts(candidate, query, isWordStart, 0, 0, positions))
            return (1, positions);
        var index = candidate.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
            return (2, Range(index, query.Length));
        var at = 0;
        for (var i = 0; i < query.Length; i++)
        {
            while (at < candidate.Length && !Same(candidate[at], query[i]))
                at++;
            if (at == candidate.Length)
                return null;
            positions[i] = at++;
        }

        return (3, positions);
    }

    /// <summary>File names (UI-DLG-131): the start, after <c>_</c> or <c>.</c>, or at a lower-to-upper case change.</summary>
    public static bool IsFileNameWordStart(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        return index == 0 || text[index - 1] is '_' or '.' || char.IsUpper(text[index]) && char.IsLower(text[index - 1]);
    }

    /// <summary>Command texts (UI-DLG-142): the start, after a space or <c>:</c>.</summary>
    public static bool IsCommandWordStart(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        return index == 0 || text[index - 1] is ' ' or ':';
    }

    private static bool WordStarts(string candidate, string query, Func<string, int, bool> isWordStart, int queryIndex, int from, int[] positions)
    {
        if (queryIndex == query.Length)
            return true;
        for (var i = from; i < candidate.Length; i++)
        {
            if (!Same(candidate[i], query[queryIndex]))
                continue;
            var contiguous = queryIndex > 0 && i == positions[queryIndex - 1] + 1;
            if (!contiguous && !isWordStart(candidate, i))
                continue;
            positions[queryIndex] = i;
            if (WordStarts(candidate, query, isWordStart, queryIndex + 1, i + 1, positions))
                return true;
        }

        return false;
    }

    private static bool Same(char a, char b) => char.ToUpperInvariant(a) == char.ToUpperInvariant(b);

    private static int[] Range(int start, int count) => [.. Enumerable.Range(start, count)];
}
