using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Region 5 of the main window (main-window.md §1, §3). Holds only the empty state so far; the document list
/// arrives with <c>IDocumentService</c> (I-035).
/// </summary>
public sealed class DocumentAreaViewModel : ObservableObject
{
    /// <summary>Commands named by the empty-state hints, in the order of <c>Msg.Editor.NoDocuments_Secondary</c>.</summary>
    private static readonly string[] HintCommands = ["file.newFile", "file.open", "search.goToFile"];
    private const string HintSeparator = " · ";

    private readonly ICommandService _commands;
    private readonly IKeymapService _keymap;

    public DocumentAreaViewModel(ICommandService commands, IKeymapService keymap)
    {
        _commands = commands;
        _keymap = keymap;
        foreach (var id in HintCommands)
        {
            _commands.Get(id).CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(EmptyHints));
        }

        _keymap.Changed += (_, _) => OnPropertyChanged(nameof(EmptyHints));
    }

    /// <summary>True when no document is open (UI-WIN-002).</summary>
    public bool IsEmpty => true;

    public string EmptyHeading => Strings.Msg_Editor_NoDocuments;

    /// <summary>
    /// UI-ED-080: the parts of the secondary line whose command is functional and has a gesture, with the current
    /// gestures; empty when no part qualifies.
    /// </summary>
    public string EmptyHints
    {
        get
        {
            var parts = Strings.Msg_Editor_NoDocuments_Secondary.Split(HintSeparator);
            var shown = new List<string>();
            for (var i = 0; i < HintCommands.Length && i < parts.Length; i++)
            {
                if (_commands.CanExecute(HintCommands[i]) && _keymap.GetGestures(HintCommands[i]) is [var gesture, ..])
                {
                    shown.Add(parts[i].Replace("{" + i.ToString(CultureInfo.InvariantCulture) + "}", gesture.ToString(), StringComparison.Ordinal));
                }
            }

            return string.Join(HintSeparator, shown);
        }
    }
}
