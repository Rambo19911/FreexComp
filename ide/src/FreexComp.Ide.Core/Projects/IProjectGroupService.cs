using FreexComp.Build;

namespace FreexComp.Ide.Core.Projects;

/// <summary>
/// One project of the group (IDE-PM-151): the full path of its <c>.pproj</c> and, when the file could be read, what it
/// says. A member whose file is missing or invalid is an <em>unavailable member</em> with the <see cref="Reason"/>
/// (IDE-PM-152).
/// </summary>
/// <param name="FilePath">Full path of the <c>.pproj</c>.</param>
/// <param name="Definition">The project as read by <c>FreexComp.Build</c>; null for an unavailable member.</param>
/// <param name="Reason">Why the member is unavailable (the diagnostic text); null for an available one.</param>
public sealed record ProjectGroupMember(string FilePath, Project? Definition, string? Reason)
{
    public bool IsAvailable => Definition is not null;

    /// <summary>The project name; for an unavailable member the file name without extension (UI-TW-126).</summary>
    public string Name => Definition?.Name ?? Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>The active configuration (the first one when the stored name is unknown, as IDE-PM-012); null when unavailable.</summary>
    public string? Configuration => Definition is not { } d ? null
        : d.Configurations.ContainsKey(d.ActiveConfiguration) || d.ConfigurationNames.Length == 0 ? d.ActiveConfiguration : d.ConfigurationNames[0];

    /// <summary>The platform shown in the badge; null when unavailable.</summary>
    public string? Platform => Definition is null ? null : Definition.Platforms.FirstOrDefault() ?? "win64";

    /// <summary>The <c>files</c> entries as stored; empty when unavailable.</summary>
    public IReadOnlyList<ProjectFile> Files => Definition is null ? [] : Definition.Files;
}

/// <summary>
/// The project group (project-manager.md, IDE-PM-151…169): an ordered list of member projects of which exactly one — the
/// <see cref="ActiveMember"/> — is loaded in <see cref="IProjectService"/>; the others are listed members. A project
/// opened alone is an implicit group of one (<see cref="FilePath"/> null, IDE-PM-153). A group that has a file is
/// written at once after every change (IDE-PM-155). UI thread only.
/// </summary>
public interface IProjectGroupService
{
    /// <summary>The members in group order; empty when nothing is open.</summary>
    IReadOnlyList<ProjectGroupMember> Members { get; }

    /// <summary>The member loaded in <see cref="IProjectService"/>; null without one (nothing open, or no available member).</summary>
    ProjectGroupMember? ActiveMember { get; }

    /// <summary>The <c>.pgroup</c> file; null for an implicit group and when nothing is open.</summary>
    string? FilePath { get; }

    /// <summary>A project or a group is open (a saved group may have no member).</summary>
    bool IsOpen { get; }

    /// <summary>The members, their order, the active member or the file changed, or a listed member was re-read.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// IDE-PM-154: opens the <c>.pgroup</c> <paramref name="path"/> — the file is loaded (failure:
    /// <c>Msg.Project.OpenFailed</c> with the parser's text and <c>Open as Text</c>), the current group is closed
    /// (canceled → nothing changes), the members are listed and the active one is opened. True when the group is open.
    /// </summary>
    Task<bool> OpenAsync(string path);

    /// <summary><c>file.closeProject</c> (IDE-PM-158): closes the active project by IDE-PM-014, then the list. False when canceled.</summary>
    Task<bool> CloseAsync();

    /// <summary>IDE-PM-160: <paramref name="projectPath"/> is an available member other than the active one.</summary>
    bool CanSetActive(string? projectPath);

    /// <summary>
    /// <c>project.setActive</c> (IDE-PM-161, 162): closes the loaded project (one unsaved prompt; canceled → nothing
    /// changes), opens <paramref name="projectPath"/> with its session and writes the active member of a saved group.
    /// True when that member is the active one afterwards.
    /// </summary>
    Task<bool> SetActiveAsync(string projectPath);

    /// <summary>IDE-PM-152: re-reads the listed members from disk; <see cref="Changed"/> when one of them differs.</summary>
    void Refresh();

    /// <summary>IDE-PM-156: an implicit group with two or more members — closing it asks <c>Msg.Group.SaveQuestion</c> first.</summary>
    bool IsUnsaved { get; }

    /// <summary>
    /// <c>project.addExistingProject</c> (IDE-PM-164) without the picker: each file that is already a member is skipped
    /// (status <c>Msg.Group.AlreadyMember</c>), the others are appended in the given order; the active project does not
    /// change. Returns the number added (0 when the group file could not be written).
    /// </summary>
    Task<int> AddExistingProjectsAsync(IReadOnlyList<string> paths);

    /// <summary>
    /// <c>project.addNewProject</c> (IDE-PM-165): the New Project dialog; the created project is appended and then
    /// activated (a canceled activation leaves it added and listed). True when a project was added.
    /// </summary>
    Task<bool> AddNewProjectAsync();

    /// <summary>
    /// <c>project.removeFromGroup</c> (IDE-PM-166): removes the member from the list; no file is touched, nothing is asked.
    /// The active project is closed first (canceled → nothing removed) and the next available member activated. The last
    /// member of an implicit group: as <see cref="CloseAsync"/>. True when removed.
    /// </summary>
    Task<bool> RemoveAsync(string projectPath);

    /// <summary>IDE-PM-168: the member can move by <paramref name="delta"/> positions (-1 up, 1 down).</summary>
    bool CanMove(string? projectPath, int delta);

    /// <summary><c>project.moveProjectUp</c> / <c>project.moveProjectDown</c> (IDE-PM-168).</summary>
    Task<bool> MoveAsync(string projectPath, int delta);

    /// <summary>IDE-PM-168 by dragging (UI-TW-127): the member gets the position <paramref name="index"/> of the list. False when nothing moves.</summary>
    Task<bool> MoveToAsync(string projectPath, int index);

    /// <summary>
    /// <c>file.saveGroupAs</c> (IDE-PM-167): writes the group to <paramref name="path"/> (null: the save picker; canceled →
    /// false) with paths relative to that file; the group is then a saved group there. A failed write shows
    /// <c>Msg.Group.SaveFailed</c>.
    /// </summary>
    Task<bool> SaveAsAsync(string? path = null);
}
