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

    /// <summary>Replaces the expression at <paramref name="index"/>; an empty one removes it.</summary>
    public void Replace(int index, string expression)
    {
        var text = expression?.Trim() ?? "";
        if (index < 0 || index >= _expressions.Count || _expressions[index] == text)
            return;
        if (text.Length == 0)
            _expressions.RemoveAt(index);
        else
            _expressions[index] = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes the expressions at <paramref name="indexes"/>.</summary>
    public void RemoveAt(IEnumerable<int> indexes)
    {
        ArgumentNullException.ThrowIfNull(indexes);
        var removed = false;
        foreach (var index in indexes.Distinct().OrderDescending().Where(i => i >= 0 && i < _expressions.Count))
        {
            _expressions.RemoveAt(index);
            removed = true;
        }

        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_expressions.Count == 0)
            return;
        _expressions.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
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
