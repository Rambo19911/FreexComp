using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Menus;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>How a cell of a debug tool window is drawn (tool-windows.md §10–§15); the view maps it to tokens.</summary>
public enum DebugCellStyle
{
    /// <summary><c>type.body</c>, <c>text.primary</c>.</summary>
    Normal,

    /// <summary><c>type.codeSmall</c>, <c>text.primary</c> (values, conditions, times).</summary>
    Code,

    /// <summary><c>text.tertiary</c> (no source, not available, placeholders).</summary>
    Tertiary,

    /// <summary><c>status.error</c> text.</summary>
    Error,

    /// <summary><c>status.warning</c> text.</summary>
    Warning,

    /// <summary><c>type.bodyStrong</c> with <c>status.error</c> foreground: a value changed since the last stop (UI-TW-1002).</summary>
    Changed,
}

/// <summary>A column of a debug tool window's grid; <paramref name="Width"/> 0 = the remaining width (<c>*</c>).</summary>
public sealed record DebugGridColumn(string Id, string Header, double Width, bool AlignRight = false, bool Sortable = false);

/// <summary>
/// One cell: its text, or a glyph of the editor's glyph margin (<paramref name="Glyph"/>), or a check box
/// (<paramref name="Checked"/>); <paramref name="Secondary"/> is appended in <c>text.tertiary</c>.
/// </summary>
public sealed record DebugGridCell(string Text, DebugCellStyle Style = DebugCellStyle.Normal, string? ToolTip = null, LineGlyphKind? Glyph = null,
    bool? Checked = null, string? Secondary = null)
{
    public static readonly DebugGridCell Empty = new("");
}

/// <summary>
/// One row of a debug tool window: <see cref="Key"/> is the row key of its specification (<c>Tool.&lt;Name&gt;.Row.&lt;key&gt;</c>),
/// <see cref="Cells"/> are in column order. A tree row has a <see cref="Level"/> and, when it can be expanded,
/// <see cref="IsExpanded"/>.
/// </summary>
public sealed class DebugGridRow(string key, IReadOnlyList<DebugGridCell> cells)
{
    public string Key { get; } = key;

    public IReadOnlyList<DebugGridCell> Cells { get; } = cells;

    /// <summary>The automation name of the row (with its suffixes ", current", ", changed").</summary>
    public string AutomationName { get; init; } = "";

    /// <summary>UI-TW-803: drawn with <c>status.warning.bg</c> and <c>type.bodyStrong</c>.</summary>
    public bool IsHighlighted { get; init; }

    public int Level { get; init; }

    /// <summary>Null = not expandable.</summary>
    public bool? IsExpanded { get; init; }

    /// <summary>What the row stands for, for its view-model.</summary>
    public object? Tag { get; init; }
}

/// <summary>
/// An in-place editor in a cell (UI-TW-1004, UI-TW-1103): a text input pre-filled with <see cref="Text"/>; Enter
/// commits, Escape cancels; a failed commit leaves it open with <see cref="Error"/> under it.
/// </summary>
public sealed class DebugGridEdit(string rowKey, string column, string text, Func<string, Task<string?>> commit, Action cancel) : ObservableObject
{
    private string? _error;

    public string RowKey { get; } = rowKey;

    public string Column { get; } = column;

    public string Text { get; } = text;

    /// <summary>The message of the failed commit; null = none.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>The last commit started (completed when none).</summary>
    public Task Commit { get; private set; } = Task.CompletedTask;

    /// <summary>Enter: commits <paramref name="text"/>; the edit closes on success and shows the message otherwise.</summary>
    public Task CommitAsync(string text) => Commit = RunAsync(text);

    /// <summary>Escape.</summary>
    public void Cancel() => cancel();

    /// <summary>The commit function reports a failure as its result; it does not fault (the view starts this and does not await it).</summary>
    private async Task RunAsync(string text) => Error = await commit(text);
}

/// <summary>
/// Base of the debug tool windows that show rows (tool-windows.md §10–§15): columns, rows with their keys, selection,
/// the default action, the context menu, sorting and the in-place editor. The view draws it; nothing here knows it.
/// </summary>
public abstract class DebugGridViewModel : DebugToolWindowViewModel, ICommandTarget
{
    private IReadOnlyList<DebugGridRow> _rows = [];
    private IReadOnlyList<string> _selectedKeys = [];
    private DebugGridEdit? _edit;

    protected DebugGridViewModel(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        Commands = commands;
    }

    protected ICommandService Commands { get; }

    public abstract IReadOnlyList<DebugGridColumn> Columns { get; }

    /// <summary>True when several rows can be selected (Breakpoints, Watches, Event Log).</summary>
    public virtual bool MultiSelect => false;

    /// <summary>The first column is a tree column: rows are indented by their level and show an expander.</summary>
    public virtual bool IsTree => false;

    public IReadOnlyList<DebugGridRow> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    /// <summary>The keys of the selected rows; the view writes the user's selection here.</summary>
    public IReadOnlyList<string> SelectedKeys
    {
        get => _selectedKeys;
        set
        {
            if (_selectedKeys.SequenceEqual(value))
                return;
            _selectedKeys = [.. value];
            OnPropertyChanged();
            Commands.InvalidateCanExecute();
        }
    }

    public IReadOnlyList<DebugGridRow> SelectedRows => [.. _rows.Where(r => _selectedKeys.Contains(r.Key))];

    /// <summary>The view-model changed the selection: the view selects <see cref="SelectedKeys"/> and scrolls the first into view.</summary>
    public event EventHandler? SelectionRequested;

    /// <summary>The view scrolls to the last row (UI-TW-1300).</summary>
    public event EventHandler? ScrollToEndRequested;

    /// <summary>The in-place editor shown now; null = none.</summary>
    public DebugGridEdit? Edit
    {
        get => _edit;
        protected set => SetProperty(ref _edit, value);
    }

    /// <summary>The sort column; null = natural order (UI-TW-011).</summary>
    public string? SortColumn { get; private set; }

    public bool SortDescending { get; private set; }

    /// <summary>The latest refresh of the rows from the debug session (completed when none is under way).</summary>
    public Task Pending { get; protected set; } = Task.CompletedTask;

    /// <summary>The context menu of the window's specification (null = separator).</summary>
    public virtual IReadOnlyList<string?> ContextCommands => [];

    public IReadOnlyList<MenuItemModel> BuildContextMenu() =>
        [.. ContextCommands.Select(id => id is null
            ? MenuItemModel.Separator()
            : MenuItemModel.ForCommand(Commands.Get(id).Title, Commands.Get(id), null, MenuToggleKind.None))];

    /// <summary>The default action of a row (double-click; Enter runs the window's command): <paramref name="column"/> is the cell hit, if any.</summary>
    public virtual Task ActivateAsync(DebugGridRow row, string? column) => Task.CompletedTask;

    /// <summary>A click on the row's expander or check box; Space on the row.</summary>
    public virtual void Toggle(DebugGridRow row)
    {
    }

    /// <summary>UI-TW-011: a click on a sortable header cycles ascending → descending → unsorted; another column starts ascending.</summary>
    public void CycleSort(string columnId)
    {
        if (Columns.FirstOrDefault(c => c.Id == columnId) is not { Sortable: true })
            return;
        if (SortColumn != columnId)
            (SortColumn, SortDescending) = (columnId, false);
        else if (!SortDescending)
            SortDescending = true;
        else
            SortColumn = null;
        OnPropertyChanged(nameof(SortColumn));
        OnSortChanged();
    }

    protected virtual void OnSortChanged()
    {
    }

    public virtual bool CanExecute(string commandId, string? argument) => false;

    public virtual Task<bool> ExecuteAsync(string commandId, string? argument) => Task.FromResult(false);

    protected void SetRows(IReadOnlyList<DebugGridRow> rows)
    {
        Rows = rows;
        var kept = _selectedKeys.Where(k => rows.Any(r => r.Key == k)).ToList();
        if (kept.Count != _selectedKeys.Count)
            SelectedKeys = kept;
        else
            Commands.InvalidateCanExecute();
    }

    /// <summary>Selects the rows and tells the view.</summary>
    protected void Select(params string[] keys)
    {
        SelectedKeys = keys;
        SelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    protected void RequestScrollToEnd() => ScrollToEndRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>A command of a debug tool window.</summary>
    protected sealed class Handler(Func<bool> canExecute, Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();
    }
}
