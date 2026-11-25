using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Popups;

/// <summary>
/// Move to Folder (<c>project.moveFile</c>, tool-windows UI-TW-124, IDE-PM-127): one row per target — the project root
/// (id <c>$project</c>, labelled with the project name), then every folder key without its trailing slash in tree order.
/// Typing filters by substring ignoring case; Enter moves the selection there; Escape closes. The same popup with
/// another title picks the platform of <c>project.addPlatform</c> (UI-TW-135, IDE-PLAT-013).
/// </summary>
public sealed class MoveToFolderViewModel : QuickPickViewModel
{
    private readonly IReadOnlyList<(string Key, string Label)> _targets;
    private readonly Func<string, Task> _move;
    private readonly string _title;

    /// <param name="targets">Row key (<c>$project</c> or a folder key) and label, in tree order.</param>
    /// <param name="move">Moves the selection into the row with the given key.</param>
    /// <param name="title">The popup's title; null = <c>Move to Folder</c>.</param>
    public MoveToFolderViewModel(IReadOnlyList<(string Key, string Label)> targets, Func<string, Task> move, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(move);
        _targets = targets;
        _move = move;
        _title = title ?? Strings.QuickPick_MoveToFolder_Title;
        Refresh();
    }

    public override string Name => "QuickPick";

    public override string AccessibleName => _title;

    public override string? Title => _title;

    public override (string Text, string Detail) CellNames => ("Name", "Detail");

    public override async Task CommitAsync()
    {
        if (SelectedItem is not { IsEnabled: true, Tag: string key })
            return;
        Close(); // focus is restored first, as for the other quick picks
        await _move(key);
    }

    protected override IReadOnlyList<QuickPickItem> Filter(string filter) =>
        [.. _targets
            .Select(t => (t, At: filter.Length == 0 ? 0 : t.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase)))
            .Where(x => x.At >= 0)
            .Select(x => new QuickPickItem(x.t.Key, x.t.Label, "", [.. Enumerable.Range(x.At, filter.Length)], true, x.t.Key))];

    protected override string NoMatch(string filter) => Strings.Msg_Find_NoMatches;
}
