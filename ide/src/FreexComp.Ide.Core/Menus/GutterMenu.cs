using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Menus;

/// <summary>
/// UI-ED-022: the context menu of the editor gutter. On a line with a breakpoint glyph: Enable/Disable, Properties, —,
/// Delete Breakpoint (<c>run.toggleBreakpoint</c>); elsewhere: Toggle Breakpoint, Toggle Bookmark, —, and check items
/// for the line numbers, folding markers and change bar (<c>editor.showLineNumbers</c> / <c>showFoldMargin</c> /
/// <c>showChangeBar</c>). The commands act on the caret line (the view moves the caret to the clicked line first).
/// </summary>
public static class GutterMenu
{
    public const string LineNumbersAction = "gutter.showLineNumbers";
    public const string FoldingMarkersAction = "gutter.showFoldMargin";
    public const string ChangeBarAction = "gutter.showChangeBar";

    public static IReadOnlyList<MenuItemModel> Build(IReadOnlyList<LineGlyph> glyphs, ICommandService commands, ISettingsService settings)
    {
        MenuItemModel Command(string id, string? label = null) =>
            MenuItemModel.ForCommand(label ?? MenuLabels.For(new CommandMenuNode(id)), commands.Get(id), null, MenuToggleKind.None);

        if (glyphs.Any(g => g.IsBreakpoint))
        {
            return
            [
                Command("run.toggleBreakpointEnabled"),
                Command("run.breakpointProperties", Strings.Editor_Gutter_BreakpointProperties),
                MenuItemModel.Separator(),
                Command("run.toggleBreakpoint", Strings.Editor_Gutter_DeleteBreakpoint),
            ];
        }

        MenuItemModel Toggle(string actionId, string label, SettingKey<bool> key) =>
            MenuItemModel.ForAction(actionId, label, () => settings.Set(key, !settings.Get(key)), settings.Get(key));

        return
        [
            Command("run.toggleBreakpoint"),
            Command("search.bookmark.toggle"),
            MenuItemModel.Separator(),
            Toggle(LineNumbersAction, Strings.Editor_Gutter_LineNumbers, SettingsCatalog.EditorShowLineNumbers),
            Toggle(FoldingMarkersAction, Strings.Editor_Gutter_FoldingMarkers, SettingsCatalog.EditorShowFoldMargin),
            Toggle(ChangeBarAction, Strings.Editor_Gutter_ChangeBar, SettingsCatalog.EditorShowChangeBar),
        ];
    }
}
