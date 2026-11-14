using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Documents;

/// <summary>
/// <c>help.keyboardShortcuts</c> (UI-MENU-030): a read-only plain-text document generated from the current keymap —
/// columns Category, Command, Shortcut (every gesture of the command, displayed one first), sorted by category then
/// title; columns are padded with spaces and separated by at least two.
/// </summary>
public static class KeyboardShortcutsDocument
{
    public const string Kind = "keyboardShortcuts";

    private const string ColumnGap = "  ";

    public static void RegisterCommands(ICommandService commands, IDocumentService documents, IKeymapService keymap)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("help.keyboardShortcuts", new Handler(() =>
            documents.ShowGenerated(Kind, Strings.Cmd_help_keyboardShortcuts, Build(keymap))));
    }

    public static string Build(IKeymapService keymap)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        var rows = CommandCatalog.All
            .Select(c => (Command: c, Gestures: keymap.GetGestures(c.Id)))
            .Where(r => r.Gestures.Count > 0)
            .Select(r => new[] { CategoryOf(r.Command), TitleOf(r.Command), string.Join(", ", r.Gestures) })
            .OrderBy(r => r[0], comparer)
            .ThenBy(r => r[1], comparer)
            .Prepend([Strings.KeyboardShortcuts_Column_Category, Strings.KeyboardShortcuts_Column_Command, Strings.KeyboardShortcuts_Column_Shortcut])
            .ToList();
        var widths = Enumerable.Range(0, 2).Select(c => rows.Max(r => r[c].Length)).ToArray();
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.Append(row[0].PadRight(widths[0])).Append(ColumnGap)
                .Append(row[1].PadRight(widths[1])).Append(ColumnGap)
                .Append(row[2]).Append(Environment.NewLine);
        }

        return text.ToString();
    }

    private static string CategoryOf(CommandDefinition command) =>
        Strings.ResourceManager.GetString("CmdCategory_" + command.Category, CultureInfo.CurrentCulture) ?? command.Category;

    private static string TitleOf(CommandDefinition command) =>
        Strings.ResourceManager.GetString(command.TitleKey, CultureInfo.CurrentCulture) ?? command.Id;

    private sealed class Handler(Action execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument)
        {
            execute();
            return Task.CompletedTask;
        }
    }
}
