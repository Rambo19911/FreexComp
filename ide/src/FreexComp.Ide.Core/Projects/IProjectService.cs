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
