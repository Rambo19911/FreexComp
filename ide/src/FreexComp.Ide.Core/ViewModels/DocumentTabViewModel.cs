using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// One document tab (editor.md §8): title, folder suffix, modified / read-only markers, tooltip and the tab's
/// AutomationId name. Created and ordered by <see cref="DocumentAreaViewModel"/>.
/// </summary>
public sealed class DocumentTabViewModel : ObservableObject
{
    private bool _isActive;
    private int _occurrence = 1;

    internal DocumentTabViewModel(DocumentViewModel document, long openSequence)
    {
        Document = document;
        OpenSequence = openSequence;
        document.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(DocumentViewModel.Title):
                    OnPropertyChanged(nameof(Title));
                    OnPropertyChanged(nameof(ToolTip));
                    break;
                case nameof(DocumentViewModel.TitleSuffix):
                    OnPropertyChanged(nameof(Suffix));
                    break;
                case nameof(DocumentViewModel.FilePath):
                    OnPropertyChanged(nameof(ToolTip));
                    break;
                case nameof(DocumentViewModel.IsModified):
                    OnPropertyChanged(nameof(IsModified));
                    break;
                case nameof(DocumentViewModel.IsReadOnly):
                    OnPropertyChanged(nameof(IsReadOnly));
                    OnPropertyChanged(nameof(ToolTip));
                    break;
            }
        };
    }

    public DocumentViewModel Document { get; }

    /// <summary>When the tab was opened; orders same-name tabs for <see cref="Occurrence"/>.</summary>
    internal long OpenSequence { get; }

    public string Title => Document.Title;

    /// <summary>UI-ED-071: the distinguishing folder(s) in brackets (<c>[lib]</c>); null when the name is unique.</summary>
    public string? Suffix => Document.TitleSuffix is { } suffix ? "[" + suffix + "]" : null;

    /// <summary>
    /// 1 for the first open tab with this title, n for the n-th in open order (AutomationId <c>DocumentTab.&lt;name&gt;@n</c>,
    /// accessibility.md).
    /// </summary>
    public int Occurrence
    {
        get => _occurrence;
        internal set
        {
            if (SetProperty(ref _occurrence, value))
                OnPropertyChanged(nameof(TabName));
        }
    }

    /// <summary>The AutomationId part after <c>DocumentTab.</c>: the title, plus <c>@n</c> from the second one on.</summary>
    public string TabName => Occurrence > 1 ? $"{Title}@{Occurrence}" : Title;

    public bool IsModified => Document.IsModified;

    public bool IsReadOnly => Document.IsReadOnly;

    public bool IsActive
    {
        get => _isActive;
        internal set => SetProperty(ref _isActive, value);
    }

    /// <summary>UI-ED-075: the full path (untitled: the title), second line <c>Read-only</c> when applicable.</summary>
    public string ToolTip =>
        (Document.FilePath ?? Document.Title) + (Document.IsReadOnly ? Environment.NewLine + Strings.Msg_Editor_Tip_ReadOnly : "");
}
