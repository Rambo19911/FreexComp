using System.Globalization;
using FreexComp.Debugger;

namespace FreexComp.Ide.Core.Debugging;

/// <summary>DBG-258: the value tooltip of the hovered token's range (<paramref name="Offset"/>, <paramref name="Length"/>).</summary>
public sealed record HoverValue(int Offset, int Length, HoverValueNode Node);

/// <summary>
/// DBG-258: a node of the value tooltip — the evaluated expression, expandable like any row of Locals or Watches
/// (UI-TW-1001: children are read when the node is expanded, more than 100 in ranges of 100). <see cref="Key"/> is the
/// path of UI-TW-1007: the expression, then <c>.Name</c> or <c>[n]</c> per level.
/// </summary>
public sealed class HoverValueNode
{
    /// <summary>Reads the children of a value (a page of them: start, count); faults when the session cannot answer.</summary>
    public delegate Task<IReadOnlyList<VariableInfo>> ChildrenReader(VariableHandle handle, int start, int count);

    private readonly ChildrenReader _read;
    private readonly Action _changed;
    private readonly (VariableHandle Handle, int Start, int Count)? _range;
    private Task _loading = Task.CompletedTask;

    private HoverValueNode(string key, string name, VariableInfo? info, int level, ChildrenReader read, Action changed,
        (VariableHandle, int, int)? range = null)
    {
        Key = key;
        Name = name;
        Info = info;
        Level = level;
        _read = read;
        _changed = changed;
        _range = range;
    }

    /// <summary>The root node of a tooltip: <paramref name="expression"/> evaluated to <paramref name="value"/>.</summary>
    public static HoverValueNode Root(string expression, VariableInfo value, ChildrenReader read)
    {
        ArgumentNullException.ThrowIfNull(value);
        HoverValueNode? root = null;
        root = new HoverValueNode(expression, expression, value, 0, read, () => root!.Changed?.Invoke(root, EventArgs.Empty));
        return root;
    }

    public string Key { get; }

    /// <summary>The expression (root), the field or element name (<c>X</c>, <c>[0]</c>), or a range (<c>[0..99]</c>).</summary>
    public string Name { get; }

    /// <summary>The value; null for a range node.</summary>
    public VariableInfo? Info { get; }

    public int Level { get; }

    public bool CanExpand => _range is not null || Info is { Children: not null, ChildCount: > 0 };

    public bool IsExpanded { get; private set; }

    /// <summary>Null until read the first time; empty when they could not be read.</summary>
    public IReadOnlyList<HoverValueNode>? Children { get; private set; }

    /// <summary>The tree changed (a node expanded, collapsed or got its children); raised on the root only.</summary>
    public event EventHandler? Changed;

    /// <summary>The last expansion started (completed when none).</summary>
    public Task Loading => _loading;

    /// <summary>Expands (reading the children the first time) or collapses the node.</summary>
    public Task ToggleAsync()
    {
        if (!CanExpand)
            return Task.CompletedTask;
        IsExpanded = !IsExpanded;
        _changed();
        if (IsExpanded && Children is null)
            _loading = LoadAsync();
        return _loading;
    }

    private async Task LoadAsync()
    {
        try
        {
            Children = await ReadAsync();
        }
        catch (Exception e) when (e is DebuggerException or ObjectDisposedException or OperationCanceledException)
        {
            Children = []; // the program runs again or the session ended: nothing to show
        }

        _changed();
    }

    private async Task<IReadOnlyList<HoverValueNode>> ReadAsync()
    {
        if (_range is { } range)
            return Nodes(await _read(range.Handle, range.Start, range.Count));
        if (Info is not { Children: { } handle } info)
            return [];
        if (info.ChildCount <= ViewModels.VariableTreeViewModel.PageSize)
            return Nodes(await _read(handle, 0, 0));

        var ranges = new List<HoverValueNode>();
        for (var start = 0; start < info.ChildCount; start += ViewModels.VariableTreeViewModel.PageSize)
        {
            var count = Math.Min(ViewModels.VariableTreeViewModel.PageSize, info.ChildCount - start);
            var name = string.Create(CultureInfo.InvariantCulture, $"[{start}..{start + count - 1}]");
            ranges.Add(new HoverValueNode(Key + name, name, null, Level + 1, _read, _changed, (handle, start, count)));
        }

        return ranges;
    }

    private List<HoverValueNode> Nodes(IReadOnlyList<VariableInfo> values) =>
        [.. values.Select(v => new HoverValueNode(_range is null ? Path(Key, v.Name) : Path(ParentKey, v.Name), v.Name, v, Level + 1, _read, _changed))];

    /// <summary>A range node's children belong to the value the range is of (UI-TW-1007: <c>Nums[100]</c>, not <c>Nums[100..199][100]</c>).</summary>
    private string ParentKey => Key[..Key.LastIndexOf('[')];

    private static string Path(string parent, string name) => parent + (name.StartsWith('[') ? name : "." + name);
}
