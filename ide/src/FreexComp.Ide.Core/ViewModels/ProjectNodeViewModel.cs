using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>The kind of a Project Manager row (tool-windows.md §2).</summary>
public enum ProjectNodeKind
{
    Project,
    Folder,
    File,

    /// <summary>The companion <c>.form</c> child of a unit (UI-TW-103).</summary>
    Form,
}

/// <summary>
/// One row of the Project Manager tree. <see cref="Key"/> is the UI-TW-114 row key (<c>$project</c>, <c>lib/</c>, the
/// stored file path, the form path; the virtual External folder is <c>$external/</c>).
/// </summary>
public sealed class ProjectNodeViewModel : ObservableObject
{
    private bool _isExpanded;
    private bool _isMissing;
    private bool _isModified;
    private bool _isActiveDocument;
    private bool _isVisibleInTree = true;
    private int _matchStart = -1;
    private int _matchLength;
    private Dialogs.RenameFileViewModel? _rename;

    public ProjectNodeViewModel(ProjectNodeKind kind, string key, string name, string fullPath, ProjectNodeViewModel? parent)
    {
        Kind = kind;
        Key = key;
        Name = name;
        FullPath = fullPath;
        Parent = parent;
    }

    public ProjectNodeKind Kind { get; }

    public string Key { get; }

    /// <summary>The label: project name, folder name or file name.</summary>
    public string Name { get; }

    /// <summary>Absolute path of the file, folder or <c>.pproj</c>; the tooltip (UI-TW-111, UI-TW-107).</summary>
    public string FullPath { get; }

    public ProjectNodeViewModel? Parent { get; }

    public ObservableCollection<ProjectNodeViewModel> Children { get; } = [];

    /// <summary>The project's <c>mainSource</c> (icon with the play corner, UI-TW-104).</summary>
    public bool IsMainSource { get; init; }

    /// <summary>The <c>&lt;activeConfiguration&gt; · &lt;platform&gt;</c> badge of the root (UI-TW-100); null elsewhere.</summary>
    public string? Badge { get; init; }

    /// <summary>A file row: file or form child (multi-selectable, UI-TW-109).</summary>
    public bool IsFile => Kind is ProjectNodeKind.File or ProjectNodeKind.Form;

    /// <summary>The 16 px glyph (UI-TW-104); the missing and main-source overlays are separate flags.</summary>
    public string IconName => Kind switch
    {
        ProjectNodeKind.Project => "project",
        ProjectNodeKind.Folder => IsExpanded ? "open-folder" : "folder",
        ProjectNodeKind.Form => "form",
        _ => Path.GetExtension(Name).ToLowerInvariant() switch
        {
            ".pas" or ".pp" or ".inc" => "unit",
            ".form" => "form",
            _ => "file",
        },
    };

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
                OnPropertyChanged(nameof(IconName));
        }
    }

    /// <summary>The in-place rename editor while the label is being edited (UI-TW-112); null otherwise.</summary>
    public Dialogs.RenameFileViewModel? Rename
    {
        get => _rename;
        set => SetProperty(ref _rename, value);
    }

    /// <summary>Listed in the project, absent on disk (UI-TW-107).</summary>
    public bool IsMissing
    {
        get => _isMissing;
        set => SetProperty(ref _isMissing, value);
    }

    /// <summary>The open document of this file has unsaved changes; on the root: the project file (UI-TW-105).</summary>
    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (SetProperty(ref _isModified, value))
                OnPropertyChanged(nameof(AutomationName));
        }
    }

    /// <summary>The row of the active document is drawn bold (UI-TW-106).</summary>
    public bool IsActiveDocument
    {
        get => _isActiveDocument;
        set => SetProperty(ref _isActiveDocument, value);
    }

    /// <summary>False while the filter hides the row (UI-TW-009).</summary>
    public bool IsVisibleInTree
    {
        get => _isVisibleInTree;
        set => SetProperty(ref _isVisibleInTree, value);
    }

    /// <summary>Start of the filter match in <see cref="Name"/>, -1 without one (drawn with <c>editor.findMatch.bg</c>).</summary>
    public int MatchStart
    {
        get => _matchStart;
        private set => SetProperty(ref _matchStart, value);
    }

    public int MatchLength
    {
        get => _matchLength;
        private set => SetProperty(ref _matchLength, value);
    }

    /// <summary>UI-TW-105: the automation name carries ", modified".</summary>
    public string AutomationName => IsModified ? Name + Resources.Strings.ProjectManager_ModifiedSuffix : Name;

    internal void SetMatch(int start, int length)
    {
        MatchLength = length;
        MatchStart = start;
    }

    /// <summary>This node and all descendants, depth first.</summary>
    public IEnumerable<ProjectNodeViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var node in child.SelfAndDescendants())
                yield return node;
        }
    }
}
