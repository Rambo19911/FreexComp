using System.Text;
using FreexComp.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Services;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Projects;

/// <inheritdoc />
/// <remarks>
/// Design (IDE-PM-151): one loaded project plus a list. The loaded project stays in <see cref="ProjectService"/>, so
/// every way of opening or closing a project that goes through it keeps working: this service follows
/// <see cref="IProjectService.ProjectChanged"/> (a project opened alone becomes an implicit group of one, a closed
/// project closes the group) and steps in only for what a group adds.
/// </remarks>
public sealed partial class ProjectGroupService : IProjectGroupService
{
    private const string LogCategory = "project";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ProjectService _projects;
    private readonly IdeFileSystem _fileSystem;
    private readonly IDocumentService _documents;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly ILog _log;
    private readonly List<ProjectGroupMember> _members = [];
    private ICommandService? _commands;

    /// <summary>The group file as loaded or last written (unknown keys survive through <c>with</c>); null for an implicit group.</summary>
    private ProjectGroup? _group;
    private string? _activePath;

    /// <summary>This service runs a close / open sequence of its own: project changes are not followed meanwhile.</summary>
    private bool _switching;

    public ProjectGroupService(ProjectService projects, IdeFileSystem fileSystem, IDocumentService documents, IDialogService dialogs,
        IStatusService status, ILog log)
    {
        _projects = projects;
        _fileSystem = fileSystem;
        _documents = documents;
        _dialogs = dialogs;
        _status = status;
        _log = log;
        projects.Group = this;
        projects.ProjectChanged += (_, _) => OnProjectChanged();
        // IDE-PM-152: a member's project file saved by the IDE (as a document) is read again.
        documents.DocumentSaved += (_, e) =>
        {
            if (e.Document.FilePath is { } path && IndexOf(path) >= 0)
                Refresh();
        };
    }

    /// <summary>Records an opened saved group (IDE-PM-157; set by the service graph).</summary>
    public IRecentItemsService? RecentItems { get; set; }

    /// <summary>
    /// The path of the member whose root row is the single selection of the focused Project Manager, else null
    /// (IDE-PM-160; set by the service graph).
    /// </summary>
    public Func<string?>? SelectedRoot { get; set; }

    public IReadOnlyList<ProjectGroupMember> Members => _members;

    public ProjectGroupMember? ActiveMember => _activePath is null ? null : _members.FirstOrDefault(m => Same(m.FilePath, _activePath));

    public string? FilePath => _group?.FilePath;

    public bool IsOpen => _group is not null || _members.Count > 0;

    public event EventHandler? Changed;

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler("project.setActive", new Handler(
            argument => CanSetActive(argument ?? SelectedRoot?.Invoke()),
            argument => (argument ?? SelectedRoot?.Invoke()) is { } path ? SetActiveAsync(path) : Task.CompletedTask));
        RegisterManageCommands(commands);
    }

    public async Task<bool> OpenAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        if (_group is not null && Same(_group.FilePath, fullPath))
            return true;

        // The file first, so that a group that cannot be read changes nothing (as IDE-PM-011 for a project).
        var load = ProjectSerializer.LoadGroup(fullPath, new CompilerFileSystemAdapter(_fileSystem));
        if (load.Value is not { } group || !load.Diagnostics.IsEmpty)
        {
            var failure = ProjectFileAccess.OpenFailed(load.Diagnostics[0]);
            if (await _dialogs.ShowMessageAsync(new MessageRequest(failure.MessageId!, failure.Args)) == "OpenAsText")
            {
                var opened = await _documents.OpenAsync(fullPath);
                if (opened.NeedsMessage)
                    await _dialogs.ShowMessageAsync(new MessageRequest(opened.MessageId!, opened.Args));
            }

            return false;
        }

        if (!await CloseAsync()) // IDE-PM-158; canceled → nothing changes
            return false;

        _switching = true;
        try
        {
            _group = group;
            _members.AddRange(group.Projects.Select(p => Load(ResolveStored(group, p))));
            // IDE-PM-154 (4): activeProject when it is an available member, else the first available one (in memory only).
            var stored = group.ActiveProject.Length == 0 ? null : ResolveStored(group, group.ActiveProject);
            var active = _members.FirstOrDefault(m => m.IsAvailable && stored is not null && Same(m.FilePath, stored))
                ?? _members.FirstOrDefault(m => m.IsAvailable);
            if (active is not null && await _projects.OpenMemberAsync(active.FilePath, recordRecent: false))
                _activePath = active.FilePath;
            SyncActiveMember();
        }
        finally
        {
            _switching = false;
        }

        RecentItems?.Add(RecentKind.Project, fullPath); // IDE-PM-157
        RecentItems?.SetOpenAtExit(fullPath, true);
        OnChanged();
        return true;
    }

    public async Task<bool> CloseAsync()
    {
        if (!IsOpen)
            return true;
        if (_projects.Project is not null)
            return await _projects.CloseAsync(); // asks ConfirmCloseAsync first; the list follows the closed project
        if (!await ConfirmCloseAsync())
            return false;
        Clear();
        OnChanged();
        return true;
    }

    /// <summary>
    /// Asked by <see cref="ProjectService.CloseAsync"/> before anything else (IDE-PM-156, IDE-PM-158): the question about
    /// an unsaved group; nothing to ask while this service switches the loaded project itself. False cancels the close.
    /// </summary>
    internal Task<bool> ConfirmCloseAsync() => _switching ? Task.FromResult(true) : ConfirmUnsavedGroupAsync();

    public bool CanSetActive(string? projectPath) =>
        projectPath is not null && IndexOf(projectPath) is >= 0 and var index && _members[index].IsAvailable
        && !Same(_members[index].FilePath, _activePath);

    public async Task<bool> SetActiveAsync(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        Refresh(); // the file may have changed since the row was drawn
        if (!CanSetActive(projectPath))
            return false;
        var target = _members[IndexOf(projectPath)].FilePath;
        var previous = _activePath;
        var done = false;
        _switching = true;
        try
        {
            // IDE-PM-161: the outgoing project by IDE-PM-014 steps 2…6 (Cancel leaves everything as it was).
            if (!await _projects.CloseAsync())
                return false;
            _activePath = null;
            // IDE-PM-155: the active member of a saved group is written before the incoming project is loaded, so a
            // failed write is undone by loading the outgoing project again.
            if (await WriteActiveAsync(target) && await _projects.OpenMemberAsync(target, recordRecent: _group is null))
            {
                _activePath = target;
                done = true;
            }
            else if (previous is not null && await _projects.OpenMemberAsync(previous, recordRecent: _group is null))
            {
                _activePath = previous;
                await WriteActiveAsync(previous, quiet: true);
            }

            SyncActiveMember();
        }
        finally
        {
            _switching = false;
        }

        ReloadListed();
        OnChanged();
        return done;
    }

    public void Refresh()
    {
        if (ReloadListed())
            OnChanged();
    }

    /// <summary>Re-reads every member that is not loaded; true when one of them differs from what was known.</summary>
    private bool ReloadListed()
    {
        var changed = false;
        for (var i = 0; i < _members.Count; i++)
        {
            if (Same(_members[i].FilePath, _activePath))
                continue;
            var read = Load(_members[i].FilePath);
            changed |= Signature(read) != Signature(_members[i]);
            _members[i] = read;
        }

        return changed;
    }

    /// <summary>What the Project Manager shows of a member: a change of anything else needs no redraw.</summary>
    private static string Signature(ProjectGroupMember member) => string.Join('\n',
    [
        member.Reason ?? "", member.Name, member.Configuration ?? "", member.Platform ?? "", member.Definition?.AppType ?? "",
        member.Definition?.MainSource ?? "", .. member.Files.Select(f => f.Path + "|" + f.Form),
    ]);

    /// <summary>IDE-PM-152: a listed member read with the serializer; a file that the IDE could not open is unavailable.</summary>
    private ProjectGroupMember Load(string path)
    {
        var load = ProjectFileAccess.Load(path, _fileSystem);
        if (load.Project is { } project)
        {
            var valid = ProjectService.Validate(project);
            return valid.Success ? new(path, project, null) : new(path, null, valid.Args[^1]);
        }

        return new(path, null, load.Diagnostic?.Message ?? load.Error.Args[^1]);
    }

    /// <summary>The project service opened, changed or closed its project outside a sequence of this service.</summary>
    private void OnProjectChanged()
    {
        if (_switching)
            return;
        var path = _projects.Project?.FilePath;
        if (path is null)
        {
            if (!IsOpen)
                return;
            Clear(); // IDE-PM-158: closing the project closes the group
        }
        else if (Same(path, _activePath))
        {
            SyncActiveMember(); // the loaded project was edited or reloaded: the Project Manager follows the project service
            return;
        }
        else
        {
            // IDE-PM-153: a project opened alone is an implicit group of one.
            Clear();
            _members.Add(new(path, _projects.Definition, null));
            _activePath = path;
        }

        OnChanged();
    }

    /// <summary>The active member's entry carries the loaded project's definition.</summary>
    private void SyncActiveMember()
    {
        if (_activePath is not null && IndexOf(_activePath) is >= 0 and var index && _projects.Definition is { } definition)
            _members[index] = new(_members[index].FilePath, definition, null);
    }

    private void Clear()
    {
        if (_group is { } group)
            RecentItems?.SetOpenAtExit(group.FilePath, false); // IDE-PM-157: closed by the user, not reopened at the next start-up
        _group = null;
        _members.Clear();
        _activePath = null;
    }

    /// <summary>
    /// IDE-PM-155: writes <c>activeProject</c> of a saved group (nothing for an implicit one). A failed write shows
    /// <c>Msg.Group.SaveFailed</c> unless <paramref name="quiet"/> and leaves the loaded value as it was.
    /// </summary>
    private async Task<bool> WriteActiveAsync(string memberPath, bool quiet = false)
    {
        if (_group is not { } group)
            return true;
        var stored = group.Projects[IndexOf(memberPath)];
        return stored == group.ActiveProject || await WriteAsync(group with { ActiveProject = stored }, quiet);
    }

    /// <summary>The atomic write of a project file, for the group; on success <paramref name="group"/> is the loaded value.</summary>
    private async Task<bool> WriteAsync(ProjectGroup group, bool quiet = false)
    {
        try
        {
            AtomicFileWriter.Write(_fileSystem, group.FilePath, Utf8.GetBytes(ProjectSerializer.Save(group)));
            _group = group;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, $"{group.FilePath}: not written", e);
            if (!quiet)
                await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Group.SaveFailed", [Path.GetFileName(group.FilePath), e.Message]));
            return false;
        }
    }

    private static string ResolveStored(ProjectGroup group, string stored) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(group.FilePath)!, stored.Replace('/', Path.DirectorySeparatorChar)));

    private int IndexOf(string path)
    {
        var full = Path.GetFullPath(path);
        return _members.FindIndex(m => Same(m.FilePath, full));
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _commands?.InvalidateCanExecute();
    }

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
