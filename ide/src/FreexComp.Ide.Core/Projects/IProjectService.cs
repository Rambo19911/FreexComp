using FreexComp.Ide.Core.Models;

namespace FreexComp.Ide.Core.Projects;

/// <summary>
/// The open project (architecture.md §4, project-manager.md): at most one at a time (IDE-PM-002). UI thread only.
/// Lives outside <c>Services</c> next to its file access (<see cref="ProjectFileAccess"/>).
/// </summary>
public interface IProjectService
{
    /// <summary>The open project; null without one.</summary>
    ProjectModel? Project { get; }

    /// <summary>A project was opened or closed, or the open one changed.</summary>
    event EventHandler? ProjectChanged;

    /// <summary>
    /// IDE-PM-011: loads and validates <paramref name="path"/> (failures: <c>Msg.Project.OpenFailed</c> with
    /// <c>Open as Text</c>, <c>Msg.Project.NewerFormat</c>), closes the current project (IDE-PM-010; canceled → nothing
    /// changes), then opens the main source. True when the project is open afterwards.
    /// </summary>
    Task<bool> OpenAsync(string path);

    /// <summary>
    /// <c>file.closeProject</c> (IDE-PM-014): one unsaved prompt over the project file and the member documents
    /// (UI-DLG-063), then closes them; non-member documents stay open. False when canceled. True without a project.
    /// </summary>
    Task<bool> CloseAsync();

    /// <summary>
    /// True when <paramref name="path"/> is the project file or a file the project lists (main source, <c>files</c>
    /// entries and their forms). Decided by path, so a member saved under another name stops being one (IDE-PM-080).
    /// </summary>
    bool IsMember(string? path);

    /// <summary>
    /// Removes the entries with the given project paths (as stored, '/') from <c>files</c> without asking; a path equal
    /// to an entry's <c>form</c> drops only that form reference. The main source is never removed (IDE-PM-060). The
    /// project is saved at once (IDE-PM-004); a failed save is reported and nothing changes. True when something was
    /// removed and saved. Files stay on disk; open documents stay open.
    /// </summary>
    Task<bool> RemoveFromProjectAsync(IReadOnlyList<string> projectPaths);

    /// <summary>
    /// <c>project.addNewUnit</c> / <c>file.newUnit</c> with a project (IDE-PM-040…042): creates <c>Unit&lt;N&gt;.pas</c> with
    /// the skeleton in the selected Project Manager folder (else the main source's folder), appends it to <c>files</c>,
    /// saves the project and opens the unit with the caret on the empty line after <c>implementation</c>. False without
    /// a project or on a reported failure.
    /// </summary>
    Task<bool> AddNewUnitAsync();

    /// <summary>
    /// IDE-PM-050/051: adds the files by reference — <c>.form</c> files are refused (<c>Status_AddFormViaUnit</c>), files
    /// already in the project skipped (<c>Msg.Project.FileAlreadyInProject</c>), duplicate unit names refused
    /// (<c>Msg.Project.DuplicateUnitName</c>); a unit with its <c>{$FORM}</c> companion gets <c>form</c>. Saved at once, not
    /// opened. Returns the number added.
    /// </summary>
    Task<int> AddExistingFilesAsync(IReadOnlyList<string> paths);

    /// <summary>Files were added (project paths as stored); the Project Manager selects their rows (IDE-PM-033).</summary>
    event EventHandler<IReadOnlyList<string>>? FilesAdded;

    /// <summary>The absolute path of a project path ('/', relative to the project directory or absolute); null without a project.</summary>
    string? ResolvePath(string projectPath);

    /// <summary><c>project.viewSource</c> (IDE-PM-015): opens and activates the main source. False on failure (reported).</summary>
    Task<bool> ViewSourceAsync();

    /// <summary>The new-project templates enumerated at start-up (IDE-PM-020), <c>console-pascal</c> first.</summary>
    IReadOnlyList<ProjectTemplate> Templates { get; }

    /// <summary>
    /// UI-DLG-031 / IDE-PM-023: closes the current project (unsaved prompt; canceled → <see cref="NewProjectOutcome.Canceled"/>),
    /// re-validates, creates <c>&lt;Location&gt;\&lt;Name&gt;\</c> from the template, then opens the project with the main
    /// source active and the caret on the first line after <c>begin</c>. Any failure removes everything this call created
    /// and shows <c>Msg.Project.CreateFailed</c>.
    /// </summary>
    Task<NewProjectOutcome> CreateProjectAsync(NewProjectRequest request);
}
