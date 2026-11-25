using System.Globalization;
using FreexComp.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Projects;

/// <summary>Managing the group (IDE-PM-164…169) and the unsaved group (IDE-PM-156).</summary>
public sealed partial class ProjectGroupService
{
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    /// <summary>The pickers of <c>project.addExistingProject</c> and <c>file.saveGroupAs</c> (set by the service graph).</summary>
    public FilePrompts? Prompts { get; set; }

    public bool IsUnsaved => _group is null && _members.Count >= 2;

    private void RegisterManageCommands(ICommandService commands)
    {
        string? Target(string? argument) => argument ?? SelectedRoot?.Invoke();
        commands.RegisterHandler("project.addExistingProject", new Handler(_ => IsOpen && Prompts is not null, _ => PickAndAddExistingAsync()));
        commands.RegisterHandler("project.addNewProject", new Handler(_ => IsOpen && _projects.Templates.Count > 0, _ => AddNewProjectAsync()));
        commands.RegisterHandler("project.removeFromGroup", new Handler(
            argument => Target(argument) is { } path && IndexOf(path) >= 0,
            argument => Target(argument) is { } path ? RemoveAsync(path) : Task.CompletedTask));
        commands.RegisterHandler("project.moveProjectUp", new Handler(
            argument => CanMove(Target(argument), -1),
            argument => Target(argument) is { } path ? MoveAsync(path, -1) : Task.CompletedTask));
        commands.RegisterHandler("project.moveProjectDown", new Handler(
            argument => CanMove(Target(argument), 1),
            argument => Target(argument) is { } path ? MoveAsync(path, 1) : Task.CompletedTask));
        commands.RegisterHandler("file.saveGroupAs", new Handler(_ => IsOpen && Prompts is not null, _ => SaveAsAsync()));
    }

    private async Task PickAndAddExistingAsync()
    {
        var folder = _projects.Project?.Directory ?? (_group is { } group ? Path.GetDirectoryName(group.FilePath) : null);
        var picked = await Prompts!.AddExistingProjectAsync(folder);
        if (picked.Count > 0)
            await AddExistingProjectsAsync(picked);
    }

    public async Task<int> AddExistingProjectsAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!IsOpen)
            return 0;
        var next = _members.ToList();
        foreach (var path in paths.Select(Path.GetFullPath))
        {
            if (next.FirstOrDefault(m => Same(m.FilePath, path)) is { } member)
            {
                // IDE-PM-164: a file that is already a member is skipped.
                _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Group_AlreadyMember, member.Name), StatusDuration);
                continue;
            }

            next.Add(Load(path));
        }

        var added = next.Count - _members.Count;
        return added > 0 && await CommitAsync(next) ? added : 0;
    }

    public async Task<bool> AddNewProjectAsync()
    {
        if (!IsOpen)
            return false;
        string? created = null;
        await _projects.RunNewProjectDialogAsync(async request =>
        {
            created = await _projects.CreateProjectFilesAsync(request);
            return created is null ? NewProjectOutcome.Failed : NewProjectOutcome.Created;
        });
        if (created is null || await AddExistingProjectsAsync([created]) == 0)
            return false;
        await SetActiveAsync(created); // IDE-PM-165: canceled → the project stays added and listed
        return true;
    }

    public async Task<bool> RemoveAsync(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        var index = IndexOf(projectPath);
        if (index < 0)
            return false;
        if (_group is null && _members.Count == 1)
            return await CloseAsync(); // IDE-PM-166: the last member of an implicit group

        var removed = _members[index];
        var next = _members.ToList();
        next.RemoveAt(index);
        if (!Same(removed.FilePath, _activePath))
            return await CommitAsync(next);

        // The active project: closed first (cancel → nothing removed), then the next available member is activated —
        // the following one, else the preceding one.
        var successor = next.Skip(index).FirstOrDefault(m => m.IsAvailable) ?? next.Take(index).LastOrDefault(m => m.IsAvailable);
        _switching = true;
        try
        {
            if (!await _projects.CloseAsync())
                return false;
            _activePath = null;
            if (successor is not null && await _projects.OpenMemberAsync(successor.FilePath, recordRecent: _group is null))
                _activePath = successor.FilePath;
            if (!await CommitAsync(next, notify: false))
            {
                // IDE-PM-155: the write failed — the list is as it was and the removed project is loaded again.
                if (_projects.Project is null || await _projects.CloseAsync())
                    _activePath = await _projects.OpenMemberAsync(removed.FilePath, recordRecent: false) ? removed.FilePath : null;
                SyncActiveMember();
                return false;
            }

            SyncActiveMember();
        }
        finally
        {
            _switching = false;
            ReloadListed();
            OnChanged();
        }

        return true;
    }

    public bool CanMove(string? projectPath, int delta) =>
        projectPath is not null && IndexOf(projectPath) is >= 0 and var index && index + delta >= 0 && index + delta < _members.Count;

    public Task<bool> MoveAsync(string projectPath, int delta)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        return CanMove(projectPath, delta) ? MoveToAsync(projectPath, IndexOf(projectPath) + delta) : Task.FromResult(false);
    }

    public async Task<bool> MoveToAsync(string projectPath, int index)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        var from = IndexOf(projectPath);
        if (from < 0 || index < 0 || index >= _members.Count || index == from)
            return false;
        var next = _members.ToList();
        var member = next[from];
        next.RemoveAt(from);
        next.Insert(index, member);
        return await CommitAsync(next);
    }

    public async Task<bool> SaveAsAsync(string? path = null)
    {
        if (!IsOpen)
            return false;
        if (path is null)
        {
            // IDE-PM-167: next to the first member, "<name of the first member>Group.pgroup".
            var first = _members.FirstOrDefault();
            var folder = first is not null ? Path.GetDirectoryName(first.FilePath) : Path.GetDirectoryName(_group!.FilePath);
            path = Prompts is null ? null : await Prompts.SaveGroupAsAsync(folder, (first?.Name ?? Path.GetFileNameWithoutExtension(_group!.FilePath)) + "Group.pgroup");
            if (path is null)
                return false;
        }

        var fullPath = Path.GetFullPath(path);
        var previous = _group;
        // Every unknown key of the file the group was loaded from goes to the new file; the old file is not changed.
        var saved = (previous ?? new ProjectGroup(1, [], "", fullPath)) with { FormatVersion = 1, FilePath = fullPath };
        if (!await WriteAsync(WithMembers(saved, _members)))
            return false;
        if (previous is not null && !Same(previous.FilePath, fullPath))
            RecentItems?.SetOpenAtExit(previous.FilePath, false);
        RecentItems?.Add(RecentKind.Project, fullPath); // IDE-PM-157
        RecentItems?.SetOpenAtExit(fullPath, true);
        OnChanged();
        return true;
    }

    /// <summary>
    /// IDE-PM-156: closing an unsaved group asks <c>Msg.Group.SaveQuestion</c> — <c>Save…</c> runs
    /// <c>file.saveGroupAs</c> (cancel there cancels), <c>Don't Save</c> goes on, <c>Cancel</c> cancels.
    /// </summary>
    private async Task<bool> ConfirmUnsavedGroupAsync()
    {
        if (!IsUnsaved)
            return true;
        return await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Group.SaveQuestion", [])) switch
        {
            "Save" => await SaveAsAsync(),
            "DontSave" => true,
            _ => false,
        };
    }

    /// <summary>
    /// The members become <paramref name="next"/>: a saved group is written at once (IDE-PM-155; a failed write shows
    /// <c>Msg.Group.SaveFailed</c> and nothing changes), an implicit one only changes in memory (IDE-PM-169).
    /// </summary>
    private async Task<bool> CommitAsync(List<ProjectGroupMember> next, bool notify = true)
    {
        if (_group is { } group && !await WriteAsync(WithMembers(group, next)))
            return false;
        _members.Clear();
        _members.AddRange(next);
        if (!notify)
            return true;
        ReloadListed(); // IDE-PM-152: after every group operation
        OnChanged();
        return true;
    }

    /// <summary><paramref name="group"/> with <c>projects</c> and <c>activeProject</c> for <paramref name="members"/>.</summary>
    private ProjectGroup WithMembers(ProjectGroup group, IReadOnlyList<ProjectGroupMember> members)
    {
        // An entry that was there before keeps its text when the file stays where it is.
        var known = _group is { } loaded && Same(loaded.FilePath, group.FilePath)
            ? loaded.Projects.ToDictionary(p => ResolveStored(loaded, p), p => p, StringComparer.OrdinalIgnoreCase)
            : [];
        string Stored(string path) => known.TryGetValue(path, out var text) ? text : StoredPath(group.FilePath, path);
        var active = members.FirstOrDefault(m => Same(m.FilePath, _activePath));
        return group with { Projects = [.. members.Select(m => Stored(m.FilePath))], ActiveProject = active is null ? "" : Stored(active.FilePath) };
    }

    /// <summary>IDE-PM-167: relative to the group file's directory with <c>/</c>; a member on another drive: its absolute path.</summary>
    private static string StoredPath(string groupFile, string projectPath) =>
        Path.GetRelativePath(Path.GetDirectoryName(groupFile)!, projectPath).Replace('\\', '/');
}
