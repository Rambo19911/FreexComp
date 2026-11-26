using System.Globalization;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ToolWindows;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// The value tree of Locals and Watches (tool-windows.md §12, §13; the debug-session interface §6.1): rows of
/// <see cref="VariableInfo"/> with lazily fetched children in pages of 100 (UI-TW-1001), the changed-value highlight
/// against the previous stop in the same frame (UI-TW-1002), expansion kept by variable path (UI-TW-1003), in-place
/// modification of <c>CanModify</c> rows (UI-TW-1004) and value copy (UI-TW-1005).
/// </summary>
public abstract class VariableTreeViewModel : DebugGridViewModel
{
    /// <summary>UI-TW-1001: children are fetched, and long arrays shown, in ranges of 100 (<c>ValueFormatOptions.ChildPageSize</c>).</summary>
    public const int PageSize = 100;

    private readonly IClipboardService _clipboard;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private Dictionary<string, string> _lastValues = new(StringComparer.Ordinal);
    private Dictionary<string, string>? _previous;
    private string? _identity;
    private DebugContext? _shown;
    private int _generation;

    protected VariableTreeViewModel(ICommandService commands, IDebugService debug, IClipboardService clipboard)
        : base(commands)
    {
        ArgumentNullException.ThrowIfNull(debug);
        Debug = debug;
        _clipboard = clipboard;
        debug.ContextChanged += (_, _) => OnContextChanged();
        debug.SessionChanged += (_, _) => OnContextChanged();
        debug.ValuesChanged += (_, _) => Reload(); // DBG-413: every visible value is read again
    }

    protected IDebugService Debug { get; }

    protected List<Node> Roots { get; } = [];

    public override bool IsTree => true;

    /// <summary>The expression of the selected row (<c>VariableInfo.EvaluateName</c>), for Add Watch and Evaluate/Modify; null when none.</summary>
    public string? SelectedExpression => SelectedRows is [{ Tag: Node { Info.EvaluateName: { Length: > 0 } name } }] ? name : null;

    /// <summary>The selected row can be modified in place (<c>VariableInfo.CanModify</c>).</summary>
    public bool CanModifySelected => Debug.IsPaused && SelectedRows is [{ Tag: Node { Info: { CanModify: true, EvaluateName: not null } } }];

    public bool CanCopySelected => SelectedRows is [{ Tag: Node }];

    /// <summary>UI-TW-1004: edits the value of the selected row in place; rows that are not modifiable do not enter edit mode.</summary>
    public void BeginModifySelected()
    {
        if (CanModifySelected && SelectedRows is [{ Tag: Node node } row])
            BeginModify(row.Key, node);
    }

    /// <summary>UI-TW-1005: copies the <c>Value</c> text of the selected row.</summary>
    public Task CopySelectedValueAsync() =>
        SelectedRows is [{ } row] ? _clipboard.SetTextAsync(row.Cells[1].Text) : Task.CompletedTask;

    /// <summary>UI-TW-1004: double-click on <c>Value</c> modifies; on <c>Name</c> (and Enter) it toggles the expansion.</summary>
    public override Task ActivateAsync(DebugGridRow row, string? column)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Tag is not Node node)
            return Task.CompletedTask;
        if (column == "Value")
        {
            if (Debug.IsPaused && node.Info is { CanModify: true, EvaluateName: not null })
                BeginModify(row.Key, node);
        }
        else
        {
            Toggle(row);
        }

        return Task.CompletedTask;
    }

    public override void Toggle(DebugGridRow row)
    {
        if (row?.Tag is not Node { CanExpand: true } node)
            return;
        if (node.IsExpanded)
        {
            node.IsExpanded = false;
            _expanded.Remove(node.Key);
            BuildRows();
        }
        else
        {
            Pending = Debug.Track(ExpandAsync(node, _generation));
        }
    }

    /// <summary>Ctrl+C (<c>edit.copy</c>) copies the value as <c>locals.copyValue</c> does.</summary>
    public override bool CanExecute(string commandId, string? argument) => commandId == "edit.copy" && CanCopySelected;

    public override async Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        if (!CanExecute(commandId, argument))
            return false;
        await CopySelectedValueAsync();
        return true;
    }

    /// <summary>The roots of the tree for the paused program (<paramref name="frame"/> = the frame the views evaluate in).</summary>
    protected abstract Task<List<Node>> LoadRootsAsync(IDebugSession session, StackFrameInfo? frame);

    /// <summary>The roots while the program is not paused.</summary>
    protected virtual List<Node> IdleRoots() => [];

    /// <summary>Rows after the tree's (the Watches placeholder).</summary>
    protected virtual IEnumerable<DebugGridRow> TrailingRows() => [];

    protected virtual void UpdateEmptyState() => SetEmptyUnlessPaused(Debug);

    /// <summary>The values are read again for the same stop and frame (a value was assigned; the watch list changed).</summary>
    protected void Reload()
    {
        if (Debug.Context is null)
        {
            ShowIdle();
            return;
        }

        Pending = Debug.Track(LoadAsync(Debug.Context, sameContext: true));
    }

    private void OnContextChanged()
    {
        var context = Debug.Context;
        if (context == _shown && context is not null)
            return;
        if (_shown is not null)
            _lastValues = Collect(); // what the next stop in the same frame is compared with
        _shown = context;
        Edit = null;
        if (context is null)
        {
            ShowIdle();
            return;
        }

        // UI-TW-013: the empty state follows the debug state at once; the rows follow when the session has answered.
        UpdateEmptyState();
        Pending = Debug.Track(LoadAsync(context, sameContext: false));
    }

    private void ShowIdle()
    {
        _generation++;
        Roots.Clear();
        Roots.AddRange(IdleRoots());
        BuildRows();
    }

    private async Task LoadAsync(DebugContext? context, bool sameContext)
    {
        var generation = ++_generation;
        if (Debug.Session is not { } session)
            return;
        List<Node> roots;
        StackFrameInfo? frame;
        try
        {
            frame = await Debug.GetEvaluationFrameAsync();
            if (generation != _generation || Debug.Context != context)
                return;
            roots = await LoadRootsAsync(session, frame);
        }
        catch (Exception e) when (e is DebuggerException or ObjectDisposedException or OperationCanceledException)
        {
            roots = []; // the program runs again, the session ends, or the frame is gone
            frame = null;
        }

        if (generation != _generation)
            return;
        if (!sameContext)
        {
            // UI-TW-1002 / UI-TW-1003: highlight and expansion live while the frame's routine stays the same.
            var identity = frame is null ? null : frame.Name + "#" + frame.Index.ToString(CultureInfo.InvariantCulture);
            if (identity is null || identity != _identity)
            {
                _previous = null;
                if (frame?.Name != NameOf(_identity))
                    _expanded.Clear();
            }
            else
            {
                _previous = _lastValues;
            }

            _identity = identity;
        }

        Roots.Clear();
        Roots.AddRange(roots);
        foreach (var root in roots)
            MarkChanged(root);
        BuildRows();
        foreach (var root in roots.Where(r => r.CanExpand && _expanded.Contains(r.Key)))
            await ExpandAsync(root, generation);
    }

    private static string? NameOf(string? identity) => identity?[..identity.LastIndexOf('#')];

    private async Task ExpandAsync(Node node, int generation)
    {
        node.IsExpanded = true;
        _expanded.Add(node.Key);
        if (node.Children is null)
        {
            node.IsLoading = true;
            BuildRows(); // UI-TW-1001: a "Loading…" child row meanwhile
            List<Node> children;
            try
            {
                children = await LoadChildrenAsync(node);
            }
            catch (Exception e) when (e is DebuggerException or ObjectDisposedException or OperationCanceledException)
            {
                children = [];
            }

            if (generation != _generation)
                return;
            node.IsLoading = false;
            node.Children = children;
            foreach (var child in children)
                MarkChanged(child);
        }

        BuildRows();
        foreach (var child in node.Children.Where(c => c.CanExpand && _expanded.Contains(c.Key)).ToList())
            await ExpandAsync(child, generation);
    }

    private async Task<List<Node>> LoadChildrenAsync(Node node)
    {
        if (Debug.Session is not { } session)
            return [];
        if (node.Range is { } range)
            return ToNodes(await session.GetChildrenAsync(range.Handle, range.Start, range.Count), node.PathKey, node.Level + 1);
        if (node.Info is not { Children: { } handle } info)
            return [];
        if (info.ChildCount <= PageSize)
            return ToNodes(await session.GetChildrenAsync(handle), node.PathKey, node.Level + 1);

        // UI-TW-1001: more than 100 elements are shown in ranges of 100 as intermediate expandable rows.
        var ranges = new List<Node>();
        for (var start = 0; start < info.ChildCount; start += PageSize)
        {
            var count = Math.Min(PageSize, info.ChildCount - start);
            var name = string.Create(CultureInfo.InvariantCulture, $"[{start}..{start + count - 1}]");
            ranges.Add(new Node(node.PathKey + name, node.PathKey, name, node.Level + 1) { Range = (handle, start, count) });
        }

        return ranges;
    }

    /// <summary>UI-TW-1007: the key of a child is its parent's path with <c>.Name</c> or <c>[n]</c> appended.</summary>
    protected static List<Node> ToNodes(IReadOnlyList<VariableInfo> values, string? parentKey, int level) =>
        [.. values.Select(v =>
        {
            var key = parentKey is null ? v.Name : parentKey + (v.Name.StartsWith('[') ? v.Name : "." + v.Name);
            return new Node(key, key, v.Name, level) { Info = v };
        })];

    private void MarkChanged(Node node) =>
        node.Changed = node.Info is { } info && _previous is not null && _previous.TryGetValue(node.Key, out var old) && old != info.Value;

    private Dictionary<string, string> Collect()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(IEnumerable<Node> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Info is { } info)
                    values[node.Key] = info.Value;
                if (node.Children is { } children)
                    Add(children);
            }
        }

        Add(Roots);
        return values;
    }

    private void BeginModify(string rowKey, Node node)
    {
        var name = node.Info!.EvaluateName!;
        Edit = new DebugGridEdit(rowKey, "Value", node.Info.Value, text =>
        {
            // The whole commit is work of this view (I-221): execution control and the end of the session wait for it.
            var commit = ModifyAsync(name, text);
            _ = Debug.Track(commit);
            return commit;
        }, () => Edit = null);
    }

    /// <summary>Assigns <paramref name="text"/> to <paramref name="name"/>: null and the editor closed, or the message that keeps it open. Never faults.</summary>
    private async Task<string?> ModifyAsync(string name, string text)
    {
        try
        {
            await Debug.SetValueAsync(name, text.Trim());
        }
        catch (DebuggerException e)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_ModifyFailed, e.Message); // the input stays open
        }
        catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
        {
            // The session is gone (it was disposed under the open editor): nothing is left to modify; the view follows it.
        }

        Edit = null;
        return null;
    }

    protected void BuildRows()
    {
        UpdateEmptyState();
        var rows = new List<DebugGridRow>();
        void Add(IEnumerable<Node> nodes)
        {
            foreach (var node in nodes)
            {
                rows.Add(RowOf(node));
                if (!node.IsExpanded)
                    continue;
                if (node.IsLoading)
                {
                    rows.Add(new DebugGridRow(node.Key + ".$loading",
                        [new(Strings.Msg_Debug_Loading, DebugCellStyle.Tertiary), DebugGridCell.Empty, DebugGridCell.Empty])
                    {
                        Level = node.Level + 1,
                        AutomationName = Strings.Msg_Debug_Loading,
                    });
                }

                if (node.Children is { } children)
                    Add(children);
            }
        }

        Add(Roots);
        rows.AddRange(TrailingRows());
        SetRows(rows);
    }

    private static DebugGridRow RowOf(Node node)
    {
        var value = node switch
        {
            { Error: { } error } => new DebugGridCell(error, DebugCellStyle.Error), // Msg.Debug.EvalFailed
            { NotAvailable: true } => new DebugGridCell(Strings.Msg_Debug_WatchNotAvailable, DebugCellStyle.Tertiary),
            { Info: { Kind: ValueKind.Error } unreadable } => new DebugGridCell(Strings.Msg_Debug_Unavailable, DebugCellStyle.Tertiary, unreadable.Value), // UI-TW-1006
            { Info: { } info } => new DebugGridCell(info.Value, node.Changed ? DebugCellStyle.Changed : DebugCellStyle.Code),
            _ => DebugGridCell.Empty,
        };
        return new DebugGridRow(node.Key, [new(node.Name), value, new(node.Info is { Kind: not ValueKind.Error } typed ? typed.TypeName : "")])
        {
            Tag = node,
            Level = node.Level,
            IsExpanded = node.CanExpand ? node.IsExpanded : null,
            AutomationName = node.Name + (node.Changed ? Strings.DebugRow_Changed : ""),
        };
    }

    /// <summary>One row of the tree. <see cref="PathKey"/> is what its children's keys start with (a range row passes its parent's on).</summary>
    protected sealed class Node(string key, string? pathKey, string name, int level)
    {
        public string Key { get; } = key;

        public string? PathKey { get; } = pathKey;

        public string Name { get; } = name;

        public int Level { get; } = level;

        public VariableInfo? Info { get; init; }

        /// <summary>The evaluator's message of a watch that cannot be evaluated.</summary>
        public string? Error { get; init; }

        /// <summary>A watch while the program is not paused.</summary>
        public bool NotAvailable { get; init; }

        /// <summary>A range of the elements of its parent's value.</summary>
        public (VariableHandle Handle, int Start, int Count)? Range { get; init; }

        public List<Node>? Children { get; set; }

        public bool IsExpanded { get; set; }

        public bool IsLoading { get; set; }

        public bool Changed { get; set; }

        public bool CanExpand => Range is not null || Info is { Children: not null };
    }
}

/// <summary>
/// Locals (tool-windows.md §12): parameters, local variables, <c>Self</c> and <c>Result</c> of the selected frame — the
/// children of the <c>Arguments</c> scope, then of the <c>Locals</c> scope, in the order returned; only while paused.
/// </summary>
public sealed class LocalsViewModel : VariableTreeViewModel
{
    public const string ToolName = "Locals";

    public LocalsViewModel(ICommandService commands, IDebugService debug, IClipboardService clipboard)
        : base(commands, debug, clipboard)
    {
        BuildRows();
    }

    public ToolWindowDefinition Definition => Define(ToolName, "Cmd_view_locals", ToolGroupId.RightUpper, this);

    public override IReadOnlyList<DebugGridColumn> Columns { get; } =
    [
        new("Name", Strings.DebugView_Column_Name, 180), new("Value", Strings.DebugView_Column_Value, 0), new("Type", Strings.DebugView_Column_Type, 140),
    ];

    public override IReadOnlyList<string?> ContextCommands { get; } = ["locals.modifyValue", "locals.copyValue", null, "run.addWatch", "run.evaluate"];

    protected override async Task<List<Node>> LoadRootsAsync(IDebugSession session, StackFrameInfo? frame)
    {
        if (frame is null)
            return [];
        var values = new List<VariableInfo>();
        foreach (var scope in (await session.GetScopesAsync(frame.Id)).Where(s => s.Kind is ScopeKind.Arguments or ScopeKind.Locals))
            values.AddRange(await session.GetChildrenAsync(scope.Handle));
        return ToNodes(values, null, 0);
    }
}

/// <summary>
/// Watches (tool-windows.md §13): the user's expressions, evaluated in the selected frame after every stop and frame
/// change (<c>EvaluateContext.Watch</c>); <c>&lt;not available&gt;</c> while not paused; a trailing placeholder row adds
/// an expression in place.
/// </summary>
public sealed class WatchesViewModel : VariableTreeViewModel
{
    public const string ToolName = "Watches";

    /// <summary>The key of the trailing placeholder row (<c>Tool.Watches.Row.$new</c>).</summary>
    public const string NewKey = "$new";

    private readonly WatchList _watches;
    private readonly IDialogService _dialogs;
    private bool _adding;

    public WatchesViewModel(ICommandService commands, IDebugService debug, IClipboardService clipboard, IDialogService dialogs)
        : base(commands, debug, clipboard)
    {
        _watches = debug.Watches;
        _dialogs = dialogs;
        _watches.Changed += (_, _) => Reload();
        Reload();
    }

    public ToolWindowDefinition Definition => Define(ToolName, "Cmd_view_watches", ToolGroupId.RightUpper, this);

    /// <summary>The field history of the Watch dialog (UI-DLG-009); null = none.</summary>
    public Layout.ILayoutService? UiState { get; set; }

    public override IReadOnlyList<DebugGridColumn> Columns { get; } =
    [
        new("Expression", Strings.DebugView_Column_Expression, 180), new("Value", Strings.DebugView_Column_Value, 0), new("Type", Strings.DebugView_Column_Type, 140),
    ];

    public override bool MultiSelect => true;

    public override bool ShowsContentWhenEmpty => true;

    // tool-windows.md §13 names no icons: those of the same commands of Breakpoints, and pencil for Edit (I-223).
    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands { get; } =
    [
        new("watches.add", "plus"), new("watches.edit", "pencil"), new("watches.delete", "x"), new("watches.deleteAll", "x-all"),
    ];

    public override IReadOnlyList<string?> ContextCommands { get; } =
    [
        "watches.add", "watches.edit", "watches.delete", "watches.deleteAll", null, "locals.modifyValue", "locals.copyValue", null, "run.evaluate",
    ];

    /// <summary>The indexes of the selected top-level watches.</summary>
    private List<int> SelectedIndexes =>
        [.. SelectedRows.Where(r => r.Tag is Node { Level: 0 }).Select(r => int.Parse(r.Key, CultureInfo.InvariantCulture))];

    public void RegisterCommands()
    {
        Commands.RegisterHandler("watches.edit", new Handler(() => SelectedIndexes.Count == 1, EditSelectedAsync));
        Commands.RegisterHandler("watches.delete", new Handler(() => SelectedIndexes.Count > 0, () =>
        {
            _watches.RemoveAt(SelectedIndexes); // UI-TW-1105
            return Task.CompletedTask;
        }));
        Commands.RegisterHandler("watches.deleteAll", new Handler(() => _watches.Expressions.Count > 0, () =>
        {
            _watches.Clear(); // without confirmation
            return Task.CompletedTask;
        }));
    }

    /// <summary>
    /// UI-TW-1103 (<c>watches.add</c> inside the tool window): appends an empty row whose <c>Expression</c> cell is in edit
    /// mode; Enter commits (an empty expression removes the row), Escape cancels and removes it.
    /// </summary>
    public void BeginAdd()
    {
        if (_adding)
            return;
        _adding = true;
        var key = _watches.Expressions.Count.ToString(CultureInfo.InvariantCulture);
        BuildRows();
        Edit = new DebugGridEdit(key, "Expression", "", text =>
        {
            _adding = false;
            Edit = null;
            if (_watches.Add(text) < 0)
                BuildRows();
            return Task.FromResult<string?>(null);
        }, () =>
        {
            _adding = false;
            Edit = null;
            BuildRows();
        });
    }

    /// <summary>UI-TW-1103 (<c>watches.edit</c>): in place while the tool window has focus, else with the Edit Watch dialog (UI-DLG-200).</summary>
    public async Task EditSelectedAsync()
    {
        if (SelectedIndexes is not [var index])
            return;
        if (IsActive)
        {
            BeginEdit(index);
        }
        else if (await _dialogs.ShowAsync(new WatchViewModel(UiState, _watches.Expressions[index], edit: true)) is { } expression)
        {
            _watches.Replace(index, expression);
        }
    }

    private void BeginEdit(int index)
    {
        var key = index.ToString(CultureInfo.InvariantCulture);
        Edit = new DebugGridEdit(key, "Expression", _watches.Expressions[index], text =>
        {
            Edit = null;
            _watches.Replace(index, text);
            return Task.FromResult<string?>(null);
        }, () => Edit = null);
    }

    /// <summary>UI-TW-1104: the placeholder row starts <c>watches.add</c>; double-click on an <c>Expression</c> cell edits it (UI-TW-1103).</summary>
    public override Task ActivateAsync(DebugGridRow row, string? column)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Key == NewKey)
        {
            BeginAdd();
            return Task.CompletedTask;
        }

        if (column == "Expression" && row.Tag is Node { Level: 0 })
        {
            BeginEdit(int.Parse(row.Key, CultureInfo.InvariantCulture));
            return Task.CompletedTask;
        }

        return base.ActivateAsync(row, column);
    }

    protected override void UpdateEmptyState()
    {
        if (_watches.Expressions.Count == 0 && !_adding)
            SetEmpty(Strings.Msg_Debug_WatchesEmpty, Strings.Msg_Debug_WatchesEmpty_Secondary);
        else
            SetEmpty(null);
    }

    /// <summary>UI-TW-1101: while not paused or without a session every value is <c>&lt;not available&gt;</c> and the type empty.</summary>
    protected override List<Node> IdleRoots() =>
        [.. _watches.Expressions.Select((e, i) => new Node(i.ToString(CultureInfo.InvariantCulture), i.ToString(CultureInfo.InvariantCulture), e, 0) { NotAvailable = true })];

    protected override async Task<List<Node>> LoadRootsAsync(IDebugSession session, StackFrameInfo? frame)
    {
        var roots = new List<Node>();
        var expressions = _watches.Expressions.ToList();
        for (var i = 0; i < expressions.Count; i++)
        {
            var key = i.ToString(CultureInfo.InvariantCulture); // UI-TW-1106
            try
            {
                var value = (await Debug.EvaluateAsync(expressions[i], EvaluateContext.Watch)).Value;
                roots.Add(new Node(key, key, expressions[i], 0) { Info = value });
            }
            catch (DebuggerException e) when (e.Code is DebugErrorCode.ExpressionSyntax or DebugErrorCode.ExpressionEvaluation)
            {
                roots.Add(new Node(key, key, expressions[i], 0) { Error = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_EvalFailed, e.Message) });
            }
        }

        return roots;
    }

    protected override IEnumerable<DebugGridRow> TrailingRows()
    {
        if (_adding)
        {
            yield return new DebugGridRow(_watches.Expressions.Count.ToString(CultureInfo.InvariantCulture), [DebugGridCell.Empty, DebugGridCell.Empty, DebugGridCell.Empty])
            {
                AutomationName = Strings.Msg_Debug_WatchPlaceholder,
            };
        }

        yield return new DebugGridRow(NewKey, [new(Strings.Msg_Debug_WatchPlaceholder, DebugCellStyle.Tertiary), DebugGridCell.Empty, DebugGridCell.Empty])
        {
            AutomationName = Strings.Msg_Debug_WatchPlaceholder,
        };
    }
}
