using FreexComp.Ide.Core.Models;

namespace FreexComp.Ide.Core.Projects;

/// <summary>
/// The open project (architecture.md §4, project-manager.md): at most one at a time (IDE-PM-002). UI thread only.
/// Lives outside <c>Services</c> next to its file access (<see cref="ProjectFileAccess"/>).
/// </summary>
/// <summary>The <c>files</c> entry being renamed and the file name typed for it (UI-TW-118).</summary>
public sealed record PendingUnitRename(string ProjectPath, string NewName);

public interface IProjectService
{
    /// <summary>The open project; null without one.</summary>
    ProjectModel? Project { get; }

    /// <summary>The open project as read by <c>FreexComp.Build</c> (run settings, configurations); null without one.</summary>
    FreexComp.Build.Project? Definition { get; }

    /// <summary>The installed platforms (IDE-PLAT-001); labels and the code model's descriptor come from it.</summary>
    IPlatformCatalog PlatformCatalog => BuiltInPlatformCatalog.Instance;

    /// <summary>
    /// IDE-PLAT-013: appends the installed platform <paramref name="id"/> to <c>platforms</c> (one project mutation,
    /// IDE-PM-004); the active platform does not change. False when it is listed already, not installed, or the save failed.
    /// </summary>
    Task<bool> AddPlatformAsync(string id);

    /// <summary>
    /// IDE-PLAT-014: removes the listed platform <paramref name="id"/> — not the only one — with its
    /// <c>platformOptions</c> entry, after <c>Msg.Platform.RemoveConfirm</c> when it has one; when it was the active
    /// platform the first remaining one becomes active. Nothing on disk is deleted. False when nothing was removed.
    /// </summary>
    Task<bool> RemovePlatformAsync(string id);

    /// <summary>
    /// UI-DLG-081 <c>IProjectService.ApplyOptions</c>: replaces the open project's definition with
    /// <paramref name="workingCopy"/> (edited from <see cref="Definition"/> with <c>with</c>, so keys the dialogs do not
    /// show survive) and saves the <c>.pproj</c> (IDE-PM-004). False when the save failed (reported) or no project is open.
    /// </summary>
    /// <param name="saveFailed">
    /// Reports a failed save in place of <c>Msg.File.SaveFailed</c>; it gets the system's error text (IDE-PM-178).
    /// </param>
    Task<bool> ApplyOptionsAsync(FreexComp.Build.Project workingCopy, Func<string, Task>? saveFailed = null);

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
    /// <c>project.removeFile</c> (IDE-PM-060): a target list with the main source shows <c>Msg.Project.RemoveMainSource</c>
    /// and changes nothing; otherwise <c>Msg.Project.RemoveFileConfirm</c> / <c>RemoveFilesConfirm</c>, then
    /// <see cref="RemoveFromProjectAsync"/>. True when removed and saved.
    /// </summary>
    Task<bool> RemoveFilesAsync(IReadOnlyList<string> projectPaths);

    /// <summary>
    /// <c>project.deleteFile</c> (IDE-PM-061): the main-source rule of <see cref="RemoveFilesAsync"/>, then
    /// <c>Msg.Project.DeleteFileConfirm</c> listing every file (a unit with its form), then each file and its companion
    /// form go to the Recycle Bin; their open documents close without a prompt. A file that cannot be recycled shows
    /// <c>Msg.Project.DeleteFailed</c> and keeps its entry; the deleted entries are removed and the project saved.
    /// A file already missing on disk counts as deleted. True when every target was deleted and the project saved.
    /// </summary>
    Task<bool> DeleteFilesAsync(IReadOnlyList<string> projectPaths);

    /// <summary>
    /// IDE-PM-070 / UI-DLG-071 for renaming the <c>files</c> entry <paramref name="projectPath"/> to the file name
    /// <paramref name="newName"/>, in this order: a unit needs an identifier (not a reserved word) with <c>.pas</c> →
    /// <c>Msg.Project.RenameInvalid</c>, another file empty or with invalid characters → <c>Msg.NewFile.NameInvalid</c>;
    /// the file or its companion form exists → <c>Msg.Project.RenameExists</c> [name]; another unit of that name →
    /// <c>Msg.Project.DuplicateUnitName</c> [unit]. An unchanged name and a change of letter case only are valid.
    /// With <paramref name="newFile"/> (the first naming after <see cref="AddNewFileAsync"/>, IDE-PM-119) a name ending
    /// in <c>.pas</c> fails with <c>Msg.Project.NewFileIsUnit</c> and one ending in <c>.form</c> with the text of
    /// <c>Status_AddFormViaUnit</c>.
    /// </summary>
    OperationResult ValidateRename(string projectPath, string newName, bool newFile = false);

    /// <summary>
    /// <c>project.renameFile</c> (IDE-PM-071…073), all-or-nothing: saves the open document when modified (failure
    /// aborts), renames the file and its companion form, saves the project, then (for units) replaces the name in the
    /// <c>unit</c>/<c>program</c> heading when <paramref name="updateHeading"/> and in the <c>{$FORM}</c> directive,
    /// in the open buffer or the file; the open document follows the new path. Any failure undoes the steps done and
    /// shows <c>Msg.Project.RenameFailed</c>. Success: status <c>Msg.Project.RenameUnitNote</c> when a heading was
    /// updated, <see cref="FileRenamed"/>. An invalid name returns false without a message; an unchanged one true.
    /// </summary>
    Task<bool> RenameFileAsync(string projectPath, string newName, bool updateHeading = true);

    /// <summary>A file was renamed (its new project path); the Project Manager selects its row (IDE-PM-033).</summary>
    event EventHandler<string>? FileRenamed;

    /// <summary>
    /// UI-TW-118: the unit rename that waits for the language service (IDE-PM-130); null otherwise. While it is set the
    /// rename, remove and delete commands are disabled and <see cref="RenameFileAsync"/> returns false.
    /// </summary>
    PendingUnitRename? PendingRename { get; }

    event EventHandler? PendingRenameChanged;

    /// <summary>
    /// <c>project.addNewUnit</c> / <c>file.newUnit</c> with a project (IDE-PM-040…042): creates <c>Unit&lt;N&gt;.pas</c> with
    /// the skeleton in the selected Project Manager folder (else the main source's folder), appends it to <c>files</c>,
    /// saves the project and opens the unit with the caret on the empty line after <c>implementation</c>. False without
    /// a project or on a reported failure.
    /// </summary>
    Task<bool> AddNewUnitAsync();

    /// <summary>
    /// <c>project.addNewForm</c> / <c>file.newForm</c> (code-sync.md §11, DSG-CS-110): creates the unit and form of the new
    /// form template (<c>MainForm</c> for the project's first form, else <c>Form&lt;N&gt;</c>) in the folder
    /// <see cref="AddNewUnitAsync"/> uses, appends <c>{ path, form }</c> to <c>files</c>, saves the project and opens the
    /// unit in the Design view. False without a project or on a reported failure.
    /// </summary>
    Task<bool> AddNewFormAsync();

    /// <summary>
    /// <c>project.addNewFile</c> (IDE-PM-118, IDE-PM-119): creates the empty file <c>File&lt;N&gt;.txt</c> in the folder
    /// <see cref="AddNewUnitAsync"/> uses, appends it to <c>files</c>, saves the project, selects the row and starts its
    /// in-place naming. The file is not opened. False without a project or on a reported failure.
    /// </summary>
    Task<bool> AddNewFileAsync();

    /// <summary>
    /// IDE-PM-121…125: moves the <c>files</c> entries <paramref name="projectPaths"/> (a unit with its companion form)
    /// into <paramref name="targetDirectory"/> inside the project directory, all or nothing. Entries outside the project
    /// directory or already in the target directory are skipped. Validation first (<c>Msg.Project.MoveExists</c>; a path
    /// over 240 characters), then: modified open documents saved, files moved on disk, <c>files[].path</c>,
    /// <c>files[].form</c> and <c>mainSource</c> rewritten, the project saved, open documents follow
    /// (<see cref="FileRenamed"/> per file), status <c>Msg.Project.MovedOne</c> / <c>MovedMany</c>. A failure moves
    /// everything back, restores the project and shows <c>Msg.Project.MoveFailed</c>. Source text is never edited.
    /// Returns the new project paths of the moved entries; empty when nothing was moved.
    /// </summary>
    Task<IReadOnlyList<string>> MoveFilesAsync(IReadOnlyList<string> projectPaths, string targetDirectory);

    /// <summary>The absolute path of the <c>form</c> entry of the project unit <paramref name="unitPath"/>; null when none.</summary>
    string? FormPathOf(string unitPath);

    /// <summary>
    /// IDE-PM-050/051: adds the files by reference — <c>.form</c> files are refused (<c>Status_AddFormViaUnit</c>), files
    /// already in the project skipped (<c>Msg.Project.FileAlreadyInProject</c>), duplicate unit names refused
    /// (<c>Msg.Project.DuplicateUnitName</c>); a unit with its <c>{$FORM}</c> companion gets <c>form</c>. Saved at once, not
    /// opened. Returns the number added. With <paramref name="reportFirstDuplicateOnly"/> only the first duplicate unit
    /// name is reported (IDE-PM-147).
    /// </summary>
    Task<int> AddExistingFilesAsync(IReadOnlyList<string> paths, bool reportFirstDuplicateOnly = false);

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
