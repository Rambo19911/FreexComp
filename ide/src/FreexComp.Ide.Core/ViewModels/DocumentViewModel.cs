using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Models;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>An open document (architecture.md §5): one tab of the document area. UI thread only.</summary>
public abstract class DocumentViewModel : ObservableObject
{
    private string? _titleSuffix;

    public DocumentId Id { get; } = DocumentId.New();

    /// <summary>Tab text: the file name with extension, or the untitled name (UI-ED-071).</summary>
    public abstract string Title { get; }

    /// <summary>
    /// The shortest distinguishing parent folder(s) when another open document has the same file name (UI-ED-071,
    /// IDE-P1-037), e.g. <c>lib</c> or <c>p\lib</c>; null when the name is unique. Set by the document service.
    /// </summary>
    public string? TitleSuffix
    {
        get => _titleSuffix;
        internal set => SetProperty(ref _titleSuffix, value);
    }

    /// <summary>Absolute normalized path; null for untitled and non-file documents.</summary>
    public abstract string? FilePath { get; }

    public abstract bool IsModified { get; }

    public abstract bool IsReadOnly { get; }

    private bool _isDeletedOnDisk;

    /// <summary>
    /// The document's file was deleted or moved away outside the IDE (IDE-P1-115): the tab title is drawn struck through
    /// (UI-ED-074). Set by the file-change service; cleared when the file is back or saved again.
    /// </summary>
    public bool IsDeletedOnDisk
    {
        get => _isDeletedOnDisk;
        internal set => SetProperty(ref _isDeletedOnDisk, value);
    }

    public virtual bool CanClose => true;

    /// <summary>At most this many info bars are visible; further ones queue (UI-ED-092).</summary>
    public const int MaxVisibleInfoBars = 3;

    /// <summary>All info bars, newest first, queued ones included (UI-ED-092).</summary>
    public ObservableCollection<InfoBarViewModel> InfoBars { get; } = [];

    /// <summary>
    /// The shown info bars, newest on top: the <see cref="MaxVisibleInfoBars"/> oldest of <see cref="InfoBars"/>;
    /// newer ones wait until a shown bar closes (UI-ED-092).
    /// </summary>
    public ObservableCollection<InfoBarViewModel> VisibleInfoBars { get; } = [];

    /// <summary>Raised when keyboard focus should move to the first button of the top-most info bar (UI-ED-094).</summary>
    public event EventHandler? InfoBarFocusRequested;

    /// <summary>Target of Focus-scoped commands while this document's view has focus (commands.md §3).</summary>
    public abstract ICommandTarget? CommandTarget { get; }

    /// <summary>
    /// The text whose editor the visible view of this document shows (I-302): what the active-editor commands
    /// (Go to Line, find, bookmarks, breakpoints, code insight, navigation history) and the status-bar editor fields act
    /// on; null when no text editor is shown (menus.md UI-MENU-010 "text").
    /// </summary>
    public virtual TextDocumentViewModel? VisibleText => null;

    /// <summary>
    /// Shows <paramref name="bar"/> on top (or queues it when <see cref="MaxVisibleInfoBars"/> are shown); a bar of the
    /// same kind is replaced in its place (UI-ED-092).
    /// </summary>
    public void ShowInfoBar(InfoBarViewModel bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        var index = IndexOfInfoBar(bar.Kind);
        if (index >= 0)
            InfoBars[index] = bar;
        else
            InfoBars.Insert(0, bar);
        SyncVisibleInfoBars();
    }

    public void RemoveInfoBar(string kind)
    {
        var index = IndexOfInfoBar(kind);
        if (index < 0)
            return;
        InfoBars.RemoveAt(index);
        SyncVisibleInfoBars();
    }

    /// <summary>Asks the view to focus the top-most info bar; false when none is shown (UI-ED-094).</summary>
    public bool RequestInfoBarFocus()
    {
        if (VisibleInfoBars.Count == 0)
            return false;
        InfoBarFocusRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private int IndexOfInfoBar(string kind)
    {
        for (var i = 0; i < InfoBars.Count; i++)
        {
            if (InfoBars[i].Kind == kind)
                return i;
        }

        return -1;
    }

    private void SyncVisibleInfoBars()
    {
        // UI-ED-144: a top-most bar is always shown, first; the others fill the remaining places, oldest first shown.
        var top = InfoBars.Where(b => b.IsTopMost).ToList();
        var rest = InfoBars.Where(b => !b.IsTopMost).ToList();
        var wanted = top.Concat(rest.Skip(Math.Max(0, rest.Count - Math.Max(0, MaxVisibleInfoBars - top.Count)))).ToList();
        if (wanted.SequenceEqual(VisibleInfoBars))
            return;
        VisibleInfoBars.Clear();
        foreach (var bar in wanted)
            VisibleInfoBars.Add(bar);
    }
}
