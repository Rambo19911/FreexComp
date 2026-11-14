using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Prefix type-ahead of lists, trees and grids (UI-A11Y-007): typed characters build a prefix that is reset after a
/// pause of <see cref="ResetAfter"/>; the next row whose text starts with it (case-insensitive) is chosen. Typing the
/// same character again moves on to the next row starting with it. One instance per list; UI thread only.
/// </summary>
public sealed class TypeAheadSearch(IClock clock)
{
    public static readonly TimeSpan ResetAfter = TimeSpan.FromSeconds(1);

    private string _prefix = "";
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    /// <summary>The prefix typed so far (empty after a pause).</summary>
    public string Prefix => clock.Now - _last >= ResetAfter ? "" : _prefix;

    /// <summary>
    /// Adds <paramref name="text"/> to the prefix and returns the index of the row to select, or -1 when no row matches
    /// (the selection stays). <paramref name="current"/> is the selected row's index, -1 for none.
    /// </summary>
    public int Type(string text, IReadOnlyList<string> rows, int current)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(rows);
        _prefix = Prefix + text;
        _last = clock.Now;
        if (_prefix.Length == 0 || rows.Count == 0)
            return -1;

        // The row that already matches the longer prefix stays chosen; a repeated single character cycles.
        var repeated = _prefix.All(c => char.ToUpperInvariant(c) == char.ToUpperInvariant(_prefix[0]));
        var found = _prefix.Length > 1 ? Find(rows, _prefix, Math.Max(current, 0)) : -1;
        if (found < 0 && (repeated || _prefix.Length == 1))
            found = Find(rows, _prefix[..1], current + 1);
        return found;
    }

    private static int Find(IReadOnlyList<string> rows, string prefix, int start)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var index = (start + i) % rows.Count;
            if (rows[index].StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
                return index;
        }

        return -1;
    }
}
