using System.Collections.ObjectModel;
using System.Globalization;
using FreexComp.Ide.Core.Menus;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Region 5 of the main window (main-window.md §1, §3): the open documents and the empty state. The tab strip view
/// arrives with I-042.
/// </summary>
public sealed class DocumentAreaViewModel : ObservableObject
{
    /// <summary>Commands named by the empty-state hints, in the order of <c>Msg.Editor.NoDocuments_Secondary</c>.</summary>
    private static readonly string[] HintCommands = ["file.newFile", "file.open", "search.goToFile"];
    private const string HintSeparator = " · ";

    private readonly ICommandService _commands;
    private readonly IKeymapService _keymap;

    public DocumentAreaViewModel(ICommandService commands, IKeymapService keymap, IDocumentService documents)
    {
        _commands = commands;
        _keymap = keymap;
        Documents = documents;
        ((System.Collections.Specialized.INotifyCollectionChanged)documents.Documents).CollectionChanged +=
            (_, _) => OnPropertyChanged(nameof(IsEmpty));
        foreach (var id in HintCommands)
        {
            _commands.Get(id).CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(EmptyHints));
        }

        _keymap.Changed += (_, _) => OnPropertyChanged(nameof(EmptyHints));

        ((System.Collections.Specialized.INotifyCollectionChanged)documents.Documents).CollectionChanged += (_, _) => SyncTabs();
        documents.ActiveDocumentChanged += (_, _) =>
        {
            foreach (var tab in Tabs)
            {
                tab.IsActive = tab.Document == documents.ActiveDocument;
            }

            OnPropertyChanged(nameof(ActiveDocument));
        };
        TabContextMenu = [.. TabMenuCommands.Select(id => id is null
            ? MenuItemModel.Separator()
            : MenuItemModel.ForCommand(TabLabels.TryGetValue(id, out var label) ? label : MenuLabels.For(new CommandMenuNode(id)), commands.Get(id), null,
                MenuToggleKind.None))];
        SyncTabs();
    }

    /// <summary>Tab-menu labels that differ from the main menu's (local-history.md UI-TW-1620: <c>Show &amp;History</c>).</summary>
    private static readonly Dictionary<string, string> TabLabels = new(StringComparer.Ordinal) { ["view.history"] = Strings.TabMenu_view_history };

    /// <summary>UI-ED-079: the tab context menu; null = separator.</summary>
    private static readonly string?[] TabMenuCommands =
    [
        "file.close", "file.closeOthers", "file.closeAll", null, "file.save", "file.saveAs", "file.revert", "file.compareWithSaved", "view.history", null,
        "file.copyPath", "file.showInExplorer", null, "window.splitRight", "window.splitDown",
    ];

    private long _openSequence;

    public IDocumentService Documents { get; }

    /// <summary>The tab strip (editor.md §8), in tab order.</summary>
    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = [];

    /// <summary>The document whose view the area shows.</summary>
    public DocumentViewModel? ActiveDocument => Documents.ActiveDocument;

    /// <summary>UI-ED-079: the commands of a tab's context menu (they act on the active document).</summary>
    public IReadOnlyList<MenuItemModel> TabContextMenu { get; }

    /// <summary>UI-ED-078: the overflow menu runs this with the document id.</summary>
    public IdeCommand ActivateDocumentCommand => _commands.Get("window.activateDocument");

    /// <summary>UI-ED-078: the overflow menu entries, in tab order; modified ones are prefixed <c>● </c>.</summary>
    public IReadOnlyList<(DocumentTabViewModel Tab, string Label)> OverflowEntries =>
        [.. Tabs.Select(t => (t, (t.IsModified ? "● " : "") + t.Title))];

    /// <summary>UI-ED-076 left click; UI-ED-079 a right click activates before the context menu opens.</summary>
    public void Activate(DocumentTabViewModel tab) => Documents.Activate(tab.Document);

    /// <summary>
    /// UI-ED-076: a tab dragged to insertion position <paramref name="insertAt"/> (0 = before the first tab, Count =
    /// after the last) moves there in tab order.
    /// </summary>
    public void MoveTab(DocumentTabViewModel tab, int insertAt)
    {
        var from = Tabs.IndexOf(tab);
        if (from >= 0)
            Documents.Move(tab.Document, insertAt > from ? insertAt - 1 : insertAt);
    }

    /// <summary>UI-ED-073 close button, UI-ED-076 middle click: closes with the unsaved-changes prompt.</summary>
    public Task<bool> CloseAsync(DocumentTabViewModel tab) => Documents.CloseAsync(tab.Document);

    /// <summary>UI-ED-076: a double click on the empty strip area runs <c>file.newFile</c>.</summary>
    public Task NewFileAsync() => _commands.CanExecute("file.newFile") ? _commands.ExecuteAsync("file.newFile") : Task.CompletedTask;

    /// <summary>Keeps <see cref="Tabs"/> equal to the document list, reusing existing tab view-models.</summary>
    private void SyncTabs()
    {
        var existing = Tabs.ToDictionary(t => t.Document);
        var wanted = Documents.Documents.Select(d => existing.TryGetValue(d, out var tab) ? tab : CreateTab(d)).ToList();
        for (var i = Tabs.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Tabs[i]))
                Tabs.RemoveAt(i);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var at = Tabs.IndexOf(wanted[i]);
            if (at < 0)
                Tabs.Insert(i, wanted[i]);
            else if (at != i)
                Tabs.Move(at, i);
        }

        UpdateOccurrences();
    }

    private DocumentTabViewModel CreateTab(DocumentViewModel document)
    {
        var tab = new DocumentTabViewModel(document, ++_openSequence) { IsActive = document == Documents.ActiveDocument };
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentTabViewModel.Title))
                UpdateOccurrences();
        };
        return tab;
    }

    private void UpdateOccurrences()
    {
        foreach (var group in Tabs.GroupBy(t => t.Title, StringComparer.OrdinalIgnoreCase))
        {
            var n = 1;
            foreach (var tab in group.OrderBy(t => t.OpenSequence))
            {
                tab.Occurrence = n++;
            }
        }
    }

    /// <summary>True when no document is open (UI-WIN-002).</summary>
    public bool IsEmpty => Documents.Documents.Count == 0;

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
