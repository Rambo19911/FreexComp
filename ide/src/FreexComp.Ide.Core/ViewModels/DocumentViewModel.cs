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

    public virtual bool CanClose => true;

    /// <summary>Info bars, newest first (UI-ED-092).</summary>
    public ObservableCollection<InfoBarViewModel> InfoBars { get; } = [];

    /// <summary>Target of Focus-scoped commands while this document's view has focus (commands.md §3).</summary>
    public abstract ICommandTarget? CommandTarget { get; }

    /// <summary>Shows <paramref name="bar"/> on top, replacing a bar of the same kind (UI-ED-092).</summary>
    public void ShowInfoBar(InfoBarViewModel bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        RemoveInfoBar(bar.Kind);
        InfoBars.Insert(0, bar);
    }

    public void RemoveInfoBar(string kind)
    {
        for (var i = InfoBars.Count - 1; i >= 0; i--)
        {
            if (InfoBars[i].Kind == kind)
            {
                InfoBars.RemoveAt(i);
            }
        }
    }
}
