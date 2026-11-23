namespace FreexComp.Ide.Core.Debugging;

/// <summary>
/// The user's watch expressions in creation order (tool-windows.md §13, UI-TW-1100). UI thread only.
/// </summary>
public sealed class WatchList
{
    private readonly List<string> _expressions = [];

    public IReadOnlyList<string> Expressions => _expressions;

    public event EventHandler? Changed;

    /// <summary>Appends the trimmed expression; an empty one is ignored. Returns its index, or -1.</summary>
    public int Add(string expression)
    {
        var text = expression?.Trim() ?? "";
        if (text.Length == 0)
            return -1;
        _expressions.Add(text);
        Changed?.Invoke(this, EventArgs.Empty);
        return _expressions.Count - 1;
    }

    /// <summary>Replaces all expressions (the session's list).</summary>
    public void Load(IEnumerable<string> expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        _expressions.Clear();
        _expressions.AddRange(expressions.Select(e => e.Trim()).Where(e => e.Length > 0));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
