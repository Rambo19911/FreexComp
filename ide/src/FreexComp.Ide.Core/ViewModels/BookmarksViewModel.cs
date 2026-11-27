using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ToolWindows;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Bookmarks (tool-windows.md §18, UI-TW-1500…1507; F11-045): every bookmark of the session scope with its number, name,
/// file, line and preview text, live with <see cref="BookmarkService.Changed"/>; Enter / double-click goes to it, Delete
/// removes the selected ones, F2 renames in place. Owns <c>bookmarks.*</c> and <c>search.bookmark.addNamed</c> (IDE-ED-117).
/// </summary>
public sealed class BookmarksViewModel : DebugGridViewModel
{
    public const string ToolName = "Bookmarks";

    private readonly BookmarkService _bookmarks;
    private readonly IToolWindowService _toolWindows;
    private string _shown = "";

    public BookmarksViewModel(ICommandService commands, BookmarkService bookmarks, IToolWindowService toolWindows)
        : base(commands)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        ArgumentNullException.ThrowIfNull(toolWindows);
        _bookmarks = bookmarks;
        _toolWindows = toolWindows;
        bookmarks.Changed += (_, _) => Update();
        Update();
    }

    /// <summary>tool-windows.md §1.2: bottom dock, hidden by default (in no profile), P8.</summary>
    public ToolWindowDefinition Definition => new(ToolName, "Cmd_view_bookmarks", ToolGroupId.Bottom, 8, () => this);

    public override IReadOnlyList<DebugGridColumn> Columns { get; } =
    [
        new("Number", Strings.DebugView_Column_Number, 32, AlignRight: true, Sortable: true),
        new("Name", Strings.DebugView_Column_Name, 160, Sortable: true),
        new("File", Strings.DebugView_Column_File, 160, Sortable: true),
        new("Line", Strings.DebugView_Column_Line, 56, AlignRight: true, Sortable: true),
        new("Text", Strings.DebugView_Column_Text, 0),
    ];

    /// <summary>UI-TW-1502.</summary>
    public override bool MultiSelect => true;

    /// <summary>UI-TW-1505.</summary>
    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands { get; } =
        [new("bookmarks.delete", "x"), new("search.bookmark.clearAll", "x-all")];

    /// <summary>UI-TW-1505.</summary>
    public override IReadOnlyList<string?> ContextCommands { get; } =
        ["bookmarks.goToSource", null, "bookmarks.rename", "bookmarks.delete", null, "search.bookmark.clearAll"];

    /// <summary>The bookmarks of the selected rows, in row order.</summary>
    public IReadOnlyList<BookmarkInfo> SelectedBookmarks => [.. SelectedRows.Select(r => r.Tag).OfType<BookmarkInfo>()];

    public void RegisterCommands()
    {
        Commands.RegisterHandler("bookmarks.goToSource", new Handler(() => SelectedBookmarks.Count > 0, () => GoToSourceAsync(SelectedBookmarks[0])));
        Commands.RegisterHandler("bookmarks.rename", new Handler(() => SelectedRows.Count == 1, () =>
        {
            BeginRename(SelectedRows[0]);
            return Task.CompletedTask;
        }));
        Commands.RegisterHandler("bookmarks.delete", new Handler(() => SelectedBookmarks.Count > 0, () =>
        {
            DeleteSelected();
            return Task.CompletedTask;
        }));
        Commands.RegisterHandler("search.bookmark.addNamed", new Handler(() => _bookmarks.Editor is not null, () =>
        {
            AddNamed();
            return Task.CompletedTask;
        }));
    }

    /// <summary>UI-TW-1501: as <c>search.bookmark.goTo&lt;N&gt;</c> for the bookmark; the editor gets the focus.</summary>
    public async Task GoToSourceAsync(BookmarkInfo bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        await _bookmarks.GoToAsync(bookmark);
        ReturnFocusToDocument();
    }

    /// <summary>UI-TW-1504: a double-click on the <c>Name</c> cell renames; elsewhere it is the default action (UI-TW-1501).</summary>
    public override Task ActivateAsync(DebugGridRow row, string? column)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Tag is not BookmarkInfo bookmark)
            return Task.CompletedTask;
        if (column == "Name")
        {
            BeginRename(row);
            return Task.CompletedTask;
        }

        return GoToSourceAsync(bookmark);
    }

    protected override void OnSortChanged() => Update();

    /// <summary>
    /// IDE-ED-117: the caret line's bookmark (added unnumbered when there is none), the window shown without taking the
    /// focus, the bookmark's <c>Name</c> cell in edit mode.
    /// </summary>
    private void AddNamed()
    {
        if (_bookmarks.Editor is not { } editor)
            return;
        var bookmark = _bookmarks.Ensure(editor, editor.ViewState.Caret.Line);
        _toolWindows.Show(ToolName, focus: false);
        if (Rows.FirstOrDefault(r => r.Tag is BookmarkInfo b && b.Id == bookmark.Id) is { } row)
            BeginRename(row);
    }

    /// <summary>UI-TW-1504: Enter commits (IDE-ED-116), Escape cancels, focus loss commits.</summary>
    private void BeginRename(DebugGridRow row)
    {
        if (row.Tag is not BookmarkInfo bookmark)
            return;
        Select(row.Key);
        Edit = new DebugGridEdit(row.Key, "Name", bookmark.Name ?? "", text =>
        {
            Edit = null;
            _bookmarks.Rename(bookmark, text);
            return Task.FromResult<string?>(null);
        }, () => Edit = null)
        {
            IdSuffix = ".Name.Edit",
            IsCode = false,
            CommitsOnFocusLoss = true,
        };
    }

    /// <summary>UI-TW-1502: removes the selected bookmarks without confirmation; the next row (else the previous) is selected.</summary>
    private void DeleteSelected()
    {
        var selected = SelectedBookmarks;
        var first = Rows.ToList().FindIndex(r => r.Tag is BookmarkInfo b && selected.Any(s => s.Id == b.Id));
        _bookmarks.Remove(selected);
        if (Rows.Count > 0)
            Select(Rows[Math.Clamp(first, 0, Rows.Count - 1)].Key);
    }

    /// <summary>
    /// UI-TW-1500, UI-TW-1503: the rows in the bookmarks' order (path, then line) or the sort column's; the selection follows
    /// its bookmarks when their lines (and so their keys) change.
    /// </summary>
    private void Update()
    {
        var all = _bookmarks.All;
        if (all.Count == 0)
            SetEmpty(Strings.Msg_Bookmarks_Empty, Strings.Msg_Bookmarks_Empty_Secondary); // UI-TW-1506
        else
            SetEmpty(null);

        // UI-TW-1503: <file name>:<line>; equal names in different folders get @<n> in path order from the second on.
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<(BookmarkInfo Bookmark, DebugGridRow Row)>();
        foreach (var bookmark in all)
        {
            var file = Path.GetFileName(bookmark.Path);
            var location = file + ":" + bookmark.Line.ToString(CultureInfo.InvariantCulture);
            var count = seen[location] = seen.GetValueOrDefault(location) + 1;
            var key = count == 1 ? location : location + "@" + count.ToString(CultureInfo.InvariantCulture);
            var line = bookmark.Line.ToString(CultureInfo.CurrentCulture);
            DebugGridCell[] cells =
            [
                new(bookmark.Number?.ToString(CultureInfo.CurrentCulture) ?? ""),
                new(bookmark.Name ?? ""),
                new(file, ToolTip: bookmark.Path),
                new(line),
                new(bookmark.Text, DebugCellStyle.Code),
            ];
            rows.Add((bookmark, new DebugGridRow(key, cells)
            {
                Tag = bookmark,
                AutomationName = bookmark.Name is null ? file + ":" + line : file + ":" + line + ", " + bookmark.Name,
            }));
        }

        var sorted = Sorted(rows).Select(r => r.Row).ToList();
        var shown = string.Join("\n", sorted.Select(r => r.Key + "\t" + string.Join("\t", r.Cells.Select(c => c.Text + "\t" + c.ToolTip))));
        if (shown == _shown)
            return; // nothing to redraw (an open in-place edit keeps what was typed); the rows' bookmarks are found by id

        _shown = shown;
        var selectedIds = SelectedRows.Select(r => r.Tag).OfType<BookmarkInfo>().Select(b => b.Id).ToHashSet();
        var selectedKeys = sorted.Where(r => r.Tag is BookmarkInfo b && selectedIds.Contains(b.Id)).Select(r => r.Key).ToList();
        if (!selectedKeys.SequenceEqual(SelectedKeys))
            SelectedKeys = selectedKeys;
        SetRows(sorted);
    }

    private IEnumerable<(BookmarkInfo Bookmark, DebugGridRow Row)> Sorted(List<(BookmarkInfo Bookmark, DebugGridRow Row)> rows)
    {
        IOrderedEnumerable<(BookmarkInfo Bookmark, DebugGridRow Row)>? sorted = SortColumn switch
        {
            // Numbered first, by number; unnumbered after (in path order).
            "Number" => rows.OrderBy(r => r.Bookmark.Number ?? int.MaxValue),
            "Name" => rows.OrderBy(r => r.Bookmark.Name ?? "", StringComparer.CurrentCultureIgnoreCase),
            // File name, then line numerically.
            "File" => rows.OrderBy(r => Path.GetFileName(r.Bookmark.Path), StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Bookmark.Line),
            "Line" => rows.OrderBy(r => r.Bookmark.Line),
            _ => null,
        };
        return sorted is null ? rows : SortDescending ? sorted.Reverse() : sorted;
    }
}
