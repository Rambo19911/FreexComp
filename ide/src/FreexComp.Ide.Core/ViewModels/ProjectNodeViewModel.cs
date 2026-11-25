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
/// stored file path, the form path; the virtual External folder is <c>$external/</c>). In a project group the root of a
/// listed or unavailable member is <c>$project:&lt;name&gt;</c> and the rows below it are prefixed <c>&lt;name&gt;:</c> (UI-TW-128).
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
    private bool _isDropTarget;
    private string? _busyName;
    private bool _isDragged;

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

    /// <summary>IDE-TGT-017: the root of a library project (icon variant <c>project.library</c>).</summary>
    public bool IsLibrary { get; init; }

    /// <summary>
    /// The row belongs to a listed or unavailable member of the project group (IDE-PM-152): it is shown only — no
    /// modified marker, no file-management command (IDE-PM-163).
    /// </summary>
    public bool IsListed { get; init; }

    /// <summary>The root of an unavailable member (UI-TW-126): disabled text, warning overlay, no badge, no children.</summary>
    public bool IsUnavailable { get; init; }

    /// <summary>The root of the active project of a group with further members: its screen-reader name says so (UI-TW-125).</summary>
    public bool AnnounceActive { get; init; }

    /// <summary>The row's tooltip: the full path (UI-TW-111); for the root of a library project the DLL path (IDE-TGT-017).</summary>
    public string ToolTipText
    {
        get => field ?? FullPath;
        init;
    }

    /// <summary>The <c>&lt;activeConfiguration&gt; · &lt;platform&gt;</c> badge of the root (UI-TW-100); null elsewhere.</summary>
    public string? Badge { get; init; }

    /// <summary>A file row: file or form child (multi-selectable, UI-TW-109).</summary>
    public bool IsFile => Kind is ProjectNodeKind.File or ProjectNodeKind.Form;

    /// <summary>The 16 px glyph (UI-TW-104); the missing and main-source overlays are separate flags.</summary>
    public string IconName => Kind switch
    {
        ProjectNodeKind.Project => IsLibrary ? "project.library" : "project",
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

    /// <summary>
    /// UI-TW-118: the file name typed for the unit rename that waits for the language service; the row shows it in
    /// <c>text.secondary</c> with the busy ring, the editor closed. Null otherwise.
    /// </summary>
    public string? BusyName
    {
        get => _busyName;
        set => SetProperty(ref _busyName, value);
    }

    /// <summary>The target of the drag in progress (UI-TW-121): <c>accent.subtle</c> fill, 1 px <c>accent</c> outline.</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (SetProperty(ref _isDropTarget, value))
                OnPropertyChanged(nameof(AutomationName));
        }
    }

    /// <summary>The row is being dragged (UI-TW-120): drawn at 50 % opacity.</summary>
    public bool IsDragged
    {
        get => _isDragged;
        set => SetProperty(ref _isDragged, value);
    }

    /// <summary>Listed in the project, absent on disk (UI-TW-107).</summary>
    public bool IsMissing
    {
        get => _isMissing;
        set
        {
            if (SetProperty(ref _isMissing, value))
                OnPropertyChanged(nameof(AutomationName));
        }
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

    /// <summary>UI-TW-105 / UI-A11Y-021: the automation name carries ", missing" (status icon) and ", modified".</summary>
    public string AutomationName =>
        Name + (IsMissing ? Resources.Strings.ProjectManager_MissingSuffix : "") + (IsModified ? Resources.Strings.ProjectManager_ModifiedSuffix : "")
        + (AnnounceActive ? Resources.Strings.ProjectManager_ActiveProjectSuffix : "") // UI-TW-125
        + (IsDropTarget ? Resources.Strings.ProjectManager_DropTargetSuffix : ""); // UI-TW-122

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
