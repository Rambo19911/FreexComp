using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Popups;

/// <summary>
/// The Command Palette (dialogs.md §14, UI-DLG-141…143): every catalog command that takes no argument, is of an implemented
/// phase and whose scope applies to the element focused when the palette opened; rows <c>&lt;Category&gt;: &lt;Title&gt;</c>
/// with the first gesture; disabled commands are listed but not run. Empty input lists the 10 most recently run palette
/// commands first (UI state <c>dialogs.CommandPalette.history.recent</c>). Enter closes the popup — the view restores the
/// focus — and then runs the command.
/// </summary>
public sealed class CommandPaletteViewModel : QuickPickViewModel
{
    public const string StateName = "CommandPalette";
    public const int RecentCount = 10;

    private readonly ICommandService _commands;
    private readonly ILayoutService _layout;
    private readonly List<(string Id, string Text)> _entries = [];

    public CommandPaletteViewModel(ICommandService commands, ILayoutService layout, FocusState origin, int implementedPhase)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(origin);
        _commands = commands;
        _layout = layout;
        foreach (var definition in commands.Definitions.Where(d => !d.TakesArgument && d.Phase <= implementedPhase && Applies(d, origin)))
        {
            var category = Strings.ResourceManager.GetString("CmdCategory_" + definition.Category, CultureInfo.CurrentCulture) ?? definition.Category;
            _entries.Add((definition.Id, category + ": " + commands.Get(definition.Id).Title));
        }

        Refresh();
    }

    public override string Name => StateName;

    public override string AccessibleName => Strings.Cmd_search_commandPalette;

    public override (string Text, string Detail) CellNames => ("Title", "Gesture");

    public override async Task CommitAsync()
    {
        if (SelectedItem is not { } item || !_commands.CanExecute(item.Id))
            return; // UI-DLG-141: a disabled command cannot be run
        Close(); // UI-DLG-143: focus is restored first
        _layout.AddDialogHistory(StateName, "recent", item.Id);
        await _commands.ExecuteAsync(item.Id);
    }

    protected override IReadOnlyList<QuickPickItem> Filter(string filter)
    {
        if (filter.Length == 0)
        {
            var recent = _layout.GetDialogHistory(StateName, "recent").Take(RecentCount).ToList();
            var ordered = recent.Select(id => _entries.FindIndex(e => e.Id == id)).Where(i => i >= 0)
                .Concat(Enumerable.Range(0, _entries.Count).Where(i => !recent.Contains(_entries[i].Id)));
            return [.. ordered.Select(i => Item(_entries[i], []))];
        }

        return [.. _entries
            .Select((e, order) => (e, order, match: FuzzyMatch.Match(e.Text, filter, FuzzyMatch.IsCommandWordStart)))
            .Where(x => x.match is not null)
            .OrderBy(x => x.match!.Value.Tier).ThenBy(x => x.e.Text.Length).ThenBy(x => x.order)
            .Select(x => Item(x.e, x.match!.Value.Positions))];
    }

    protected override string NoMatch(string filter) => string.Format(CultureInfo.CurrentCulture, Strings.Msg_Palette_NoCommand, filter);

    private QuickPickItem Item((string Id, string Text) entry, IReadOnlyList<int> highlights) =>
        new(entry.Id, entry.Text, _commands.Get(entry.Id).GestureText, highlights, _commands.CanExecute(entry.Id), null);

    /// <summary>UI-DLG-141: the command's scope can apply to the element that had focus.</summary>
    private static bool Applies(CommandDefinition definition, FocusState origin) => definition.Scopes.Any(scope => scope switch
    {
        CommandScope.Global or CommandScope.Focus => true,
        CommandScope.ToolList => origin.Scope == CommandScope.ToolList
                                 && string.Equals(origin.ToolWindowName, definition.Category, StringComparison.OrdinalIgnoreCase),
        _ => origin.Scope == scope,
    });
}
