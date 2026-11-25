using System.Globalization;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Build;

/// <summary>Build, rebuild and clean of the open project (phase3-build-run.md §5). UI thread only.</summary>
public interface IBuildService
{
    /// <summary>
    /// IDE-P3-030/032: toolchain check, save before build (Build/Rebuild), state Building, engine in-process on a background
    /// thread, Messages and Output, summary, state back to Idle. With <paramref name="continueToRun"/> the state stays
    /// Building after the engine returned so that a run can follow without passing Idle (IDE-P3-002); the caller then
    /// leaves Building. Null when the build did not start (no project, toolchain missing, save aborted, not Idle).
    /// </summary>
    Task<BuildResult?> BuildAsync(BuildKind kind, bool continueToRun = false);

    /// <summary>
    /// IDE-PM-170: build, rebuild or clean of one member of the project group — one request for its <c>.pproj</c>; the
    /// active project does not change. The path of the loaded project is <see cref="BuildAsync"/>. Null when the build
    /// did not start (not an available member, or as <see cref="BuildAsync"/>).
    /// </summary>
    Task<BuildResult?> BuildMemberAsync(string projectPath, BuildKind kind);

    /// <summary>
    /// IDE-PM-171 <c>project.buildAll</c>, <c>project.rebuildAll</c>, <c>project.cleanAll</c>: one request for the group
    /// file (every project with its own active configuration and platform). An unsaved group is saved first through
    /// <c>file.saveGroupAs</c>; canceled → no build, status <c>Msg.Group.SaveToBuildAll</c>. Null when the build did not
    /// start (fewer than two members, or as <see cref="BuildAsync"/>).
    /// </summary>
    Task<BuildResult?> BuildGroupAsync(BuildKind kind);

    /// <summary>IDE-P3-050: requests cancellation of the running build; the state stays Building until the engine returns.</summary>
    void Cancel();

    /// <summary>The summary text of the last build (IDE-P3-034); null before the first.</summary>
    string? ResultText { get; }

    /// <summary>A build finished (<see cref="ResultText"/> changed).</summary>
    event EventHandler? BuildFinished;
}

/// <inheritdoc cref="IBuildService"/>
public sealed class BuildService : IBuildService
{
    private const string Category = "build";

    /// <summary>IDE-P3-051: how long a canceled build may take before it is abandoned.</summary>
    public static readonly TimeSpan CancelTimeout = TimeSpan.FromSeconds(10);

    private readonly IBuildEngine _engine;
    private readonly IProjectService _projects;
    private readonly IDocumentService _documents;
    private readonly ISettingsService _settings;
    private readonly IdeStateMachine _state;
    private readonly IToolchainLocator _toolchain;
    private readonly IOutputService _output;
    private readonly MessagesService _messages;
    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly ILog _log;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _forced;
    private IDisposable? _cancelTimer;
    private bool _internalError;
    private bool _eventsSkipped;
    private string? _lockedOutput;

    /// <summary>The <c>Finished</c> events of the request in progress, one per planned project (IDE-PM-173).</summary>
    private readonly List<BuildEvent.Finished> _finished = [];
    private readonly IProcessLauncher _processes;
    private readonly ITrustService _trust;

    public BuildService(IBuildEngine engine, IProjectService projects, IDocumentService documents, ISettingsService settings,
        IdeStateMachine state, IToolchainLocator toolchain, IOutputService output, MessagesService messages, IClock clock,
        IUiDispatcher dispatcher, IDialogService dialogs, IStatusService status, ILog log, IProcessLauncher processes,
        ITrustService trust)
    {
        _processes = processes;
        _trust = trust;
        _engine = engine;
        _projects = projects;
        _documents = documents;
        _settings = settings;
        _state = state;
        _toolchain = toolchain;
        _output = output;
        _messages = messages;
        _clock = clock;
        _dispatcher = dispatcher;
        _dialogs = dialogs;
        _status = status;
        _log = log;
    }

    public string? ResultText { get; private set; }

    public event EventHandler? BuildFinished;

    /// <summary>Called after a build with at least one error (UI-WIN-016 / UI-TW-409: activate Messages).</summary>
    public Action? ShowMessages { get; set; }

    /// <summary>Called when a build starts (UI-TW-501: Output shows the build channel).</summary>
    public Action? ShowBuildOutput { get; set; }

    /// <summary>The project group (IDE-PM-170, 171; set by the service graph). Null: only the loaded project is built.</summary>
    public IProjectGroupService? Group { get; set; }

    /// <summary>What a request builds: the loaded project, a listed member of the group, or the group file.</summary>
    private enum Scope { Active, Member, Group }

    /// <summary>
    /// The subject of one request. <paramref name="IsOpen"/>: it is still what the IDE has open (asked again after every
    /// wait); <paramref name="Saves"/>: the file documents that save-before-build covers.
    /// </summary>
    private sealed record Subject(string Path, string Name, string? Configuration, string? Platform, Func<bool> IsOpen, Func<string, bool> Saves);

    public Task<BuildResult?> BuildAsync(BuildKind kind, bool continueToRun = false) => BuildAsync(Scope.Active, null, kind, continueToRun);

    public Task<BuildResult?> BuildMemberAsync(string projectPath, BuildKind kind)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        return Same(projectPath, _projects.Project?.FilePath) ? BuildAsync(kind) : BuildAsync(Scope.Member, projectPath, kind, false);
    }

    public Task<BuildResult?> BuildGroupAsync(BuildKind kind) => BuildAsync(Scope.Group, null, kind, false);

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private ProjectGroupMember? AvailableMember(string? path) =>
        path is null ? null : Group?.Members.FirstOrDefault(m => m.IsAvailable && Same(m.FilePath, Path.GetFullPath(path)));

    /// <summary>IDE-PM-171: Build All needs a group of two or more members.</summary>
    private bool HasGroupToBuild => Group is { Members.Count: >= 2 };

    private bool CanBuild(Scope scope, string? memberPath) => scope switch
    {
        Scope.Member => AvailableMember(memberPath) is not null,
        Scope.Group => HasGroupToBuild,
        _ => _projects.Project is not null,
    };

    private Subject? Resolve(Scope scope, string? memberPath)
    {
        switch (scope)
        {
            case Scope.Member when AvailableMember(memberPath) is { Definition: { } definition } member:
                // Save-before-build of a listed member: its own files and its project file (as IDE-P3-020 for the loaded project).
                var directory = Path.GetDirectoryName(member.FilePath)!;
                var files = member.Files.SelectMany(f => new[] { f.Path, f.Form }).OfType<string>()
                    .Select(f => Path.GetFullPath(Path.Combine(directory, f.Replace('/', Path.DirectorySeparatorChar))))
                    .Append(member.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return new Subject(member.FilePath, member.Name, member.Configuration, definition.Platforms.FirstOrDefault(),
                    () => AvailableMember(member.FilePath) is not null, files.Contains);
            case Scope.Group when HasGroupToBuild && Group!.FilePath is { } groupPath:
                // IDE-PM-171: every project uses its own active values; the set of all modified file documents is saved.
                return new Subject(groupPath, Path.GetFileNameWithoutExtension(groupPath), null, null,
                    () => HasGroupToBuild && Same(Group?.FilePath, groupPath), _ => true);
            case Scope.Active when _projects.Project is { } project:
                return new Subject(project.FilePath, project.Name, project.ActiveConfiguration,
                    project.Platforms.Count > 0 ? project.Platforms[0] : null, () => Same(_projects.Project?.FilePath, project.FilePath),
                    _projects.IsMember);
            default:
                return null;
        }
    }

    private async Task<BuildResult?> BuildAsync(Scope scope, string? memberPath, BuildKind kind, bool continueToRun)
    {
        var result = await BuildOnceAsync(scope, memberPath, kind, continueToRun);
        if (result is not { Success: false, Canceled: false } || _lockedOutput is not { } exe)
            return result;

        // IDE-P3-035: the output exe is held by a program started outside the IDE (E4008).
        if (_state.Current.State == IdeState.Building)
            _state.TryTransition(IdeStateInfo.Idle); // a run waiting on this build (IDE-P3-002) does not continue
        if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Build.OutputLocked", [Path.GetFileName(exe)])) != "StopAndBuild")
            return result;
        if (!await _processes.TerminateByPathAsync(exe))
            _log.Error(Category, "The program holding " + exe + " could not be terminated."); // the repeated build reports E4008 again
        return await BuildAsync(scope, memberPath, kind, continueToRun);
    }

    private async Task<BuildResult?> BuildOnceAsync(Scope scope, string? memberPath, BuildKind kind, bool continueToRun)
    {
        _lockedOutput = null;
        if (kind == BuildKind.CheckSyntax)
            throw new ArgumentException("Check Syntax does not use the build engine (IDE-P3-055).", nameof(kind));
        if (!CanBuild(scope, memberPath) || _state.Current.State != IdeState.Idle || _trust.IsPromptOpen)
            return null; // IDE-TRUST-013: the state is still Idle while the question is open; no second build starts behind it

        // IDE-P3-012: the toolchain first; nothing is saved when it is missing.
        var toolchain = _toolchain.CheckForBuild(needsLlvm: true);
        if (!toolchain.Success)
        {
            if (await _dialogs.ShowMessageAsync(new MessageRequest(toolchain.MessageId!, [.. toolchain.Args])) == "OpenOptions")
                _ = OpenToolchainOptions?.Invoke();
            return null;
        }

        // IDE-PM-171: the engine builds a group from its file, so an unsaved group is saved first (every target).
        if (scope == Scope.Group && Group is { IsUnsaved: true } unsaved && !await unsaved.SaveAsAsync())
        {
            _status.ShowMessage(Strings.Msg_Group_SaveToBuildAll, TimeSpan.FromSeconds(5));
            return null;
        }

        if (Resolve(scope, memberPath) is not { } subject)
            return null;
        if (kind != BuildKind.Clean && !await SaveBeforeBuildAsync(subject.Saves))
            return null;
        if (!subject.IsOpen() || _state.Current.State != IdeState.Idle)
            return null;

        // IDE-TRUST-003: after the save (the project file on disk is the one the engine reads), before Building.
        var events = EventsPolicy.RequireTrust;
        if (kind != BuildKind.Clean)
        {
            var trust = await _trust.CheckBeforeBuildAsync(subject.Path, subject.Name); // IDE-PM-174: per project of the request
            if (!trust.Proceed)
            {
                // IDE-TRUST-009: nothing is built or run; Messages and Output keep what they show.
                _status.ShowMessage(Strings.Msg_Trust_BuildCancelled, TimeSpan.FromSeconds(5));
                _output.Append(OutputChannels.Build, Strings.Msg_Trust_BuildCancelled, OutputKind.Ide);
                return null;
            }

            events = trust.Events;
            if (!subject.IsOpen() || _state.Current.State != IdeState.Idle)
                return null;
        }

        if (!_state.TryTransition(new IdeStateInfo(IdeState.Building, kind, false, null, null)))
            return null;
        BuildResult? result = null;
        var active = BeginActive();
        try
        {
            result = await RunEngineAsync(subject, kind, events);
            return result;
        }
        finally
        {
            // IDE-P3-037: Idle in every case, except a finished build that a run continues from (IDE-P3-002).
            if (_state.Current.State == IdeState.Building && (!continueToRun || result is null || result.Canceled))
                _state.TryTransition(IdeStateInfo.Idle);
            EndActive(active);
        }
    }

    /// <summary>
    /// <c>project.build</c>, <c>project.rebuild</c>, <c>project.clean</c> (need a project) and <c>project.cancelBuild</c>;
    /// the IDE state decides the rest (IDE-P3-003).
    /// </summary>
    public void RegisterCommands(Commands.ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        // IDE-PM-170: without an argument the loaded project (menu, tool bar, shortcut); the context menu of a root row
        // gives the member's path.
        void Register(string id, BuildKind kind) => commands.RegisterHandler(id, new Handler(
            member => CanBuild(member is null ? Scope.Active : Scope.Member, member),
            member => member is null ? BuildAsync(kind) : BuildMemberAsync(member, kind)));
        Register("project.build", BuildKind.Build);
        Register("project.rebuild", BuildKind.Rebuild);
        Register("project.clean", BuildKind.Clean);
        commands.RegisterHandler("project.buildAll", new Handler(_ => HasGroupToBuild, _ => BuildGroupAsync(BuildKind.Build))); // IDE-PM-171
        commands.RegisterHandler("project.rebuildAll", new Handler(_ => HasGroupToBuild, _ => BuildGroupAsync(BuildKind.Rebuild)));
        commands.RegisterHandler("project.cleanAll", new Handler(_ => HasGroupToBuild, _ => BuildGroupAsync(BuildKind.Clean)));
        commands.RegisterHandler("project.checkSyntax", new Handler(_ => _projects.Project is not null && CheckSyntax is not null, _ => CheckSyntaxAsync()));
        commands.RegisterHandler("project.cancelBuild", new Handler(_ => _cts is not null && !_state.Current.CancelRequested, _ =>
        {
            Cancel();
            return Task.CompletedTask;
        }));
    }

    /// <summary>The project diagnostics with the open buffers (<c>CodeModelService</c>, I-110; set by the service graph).</summary>
    public Func<CancellationToken, Task<System.Collections.Immutable.ImmutableArray<Diagnostic>>>? CheckSyntax { get; set; }

    /// <summary>
    /// IDE-P3-055 <c>project.checkSyntax</c>: state Building (kind CheckSyntax), the program's diagnostics from the
    /// language service with the unsaved buffers, reported to Messages and Output like a build's; nothing is saved or
    /// written and no toolchain check is made. Cancellable with <c>project.cancelBuild</c>.
    /// </summary>
    public async Task CheckSyntaxAsync()
    {
        if (_projects.Project is null || CheckSyntax is not { } check || _state.Current.State != IdeState.Idle)
            return;
        if (!_state.TryTransition(new IdeStateInfo(IdeState.Building, BuildKind.CheckSyntax, false, null, null)))
            return;
        var active = BeginActive();
        try
        {
            if (_settings.Get(SettingsCatalog.OutputClearOnBuild))
                _output.Clear(OutputChannels.Build);
            _messages.BeginBuild();
            ShowBuildOutput?.Invoke();
            using var cts = new CancellationTokenSource();
            _cts = cts;
            _forced = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics;
            try
            {
                var work = check(cts.Token);
                if (await Task.WhenAny(work, _forced.Task) != work)
                    throw new OperationCanceledException();
                diagnostics = await work;
            }
            catch (OperationCanceledException)
            {
                Finish(Strings.Msg_Build_Canceled, success: false);
                return;
            }
            catch (Exception e)
            {
                _log.Error(Category, "Check Syntax failed.", e);
                _output.Append(OutputChannels.Build, e.ToString(), OutputKind.Error);
                _messages.AddError(Strings.Msg_Build_InternalError);
                Finish(Strings.Msg_Build_InternalError, success: false);
                ShowMessages?.Invoke();
                return;
            }
            finally
            {
                _cancelTimer?.Dispose();
                _cancelTimer = null;
                _cts = null;
            }

            foreach (var d in diagnostics)
                _output.Append(OutputChannels.Build, d.ToText(), d.Severity == DiagnosticSeverity.Error ? OutputKind.Error : OutputKind.Normal);
            _messages.AddDiagnostics([.. diagnostics]);
            var errors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            Finish(errors switch
            {
                0 => Strings.Msg_Build_SyntaxOk,
                1 => Strings.Msg_Build_FailedOne,
                _ => Format(Strings.Msg_Build_FailedMany, errors),
            }, success: errors == 0);
            if (errors > 0 && _settings.Get(SettingsCatalog.MessagesAutoShowOnError))
                ShowMessages?.Invoke();
        }
        finally
        {
            if (_state.Current.State == IdeState.Building)
                _state.TryTransition(IdeStateInfo.Idle);
            EndActive(active);
        }
    }

    /// <summary>Opens Options › Toolchain Paths (the <c>Open Options</c> button of <c>Msg.Build.ToolchainMissing</c>).</summary>
    public Func<Task>? OpenToolchainOptions { get; set; }

    /// <summary>How long <c>file.exit</c> waits for a canceled build (phase3-build-run.md §2, footnote of the availability table).</summary>
    public static readonly TimeSpan ExitCancelWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <c>file.exit</c> while Building (after the unsaved-changes prompt): cancels the build and waits for it to end, at
    /// most <see cref="ExitCancelWait"/>. Returns at once when no build runs.
    /// </summary>
    public async Task CancelForExitAsync()
    {
        if (_active is not { } active)
            return;
        Cancel();
        var timeout = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (_clock.Schedule(ExitCancelWait, () => timeout.TrySetResult()))
            await Task.WhenAny(active.Task, timeout.Task);
    }

    /// <summary>Completes when the build or Check Syntax in progress has ended; null when none runs.</summary>
    private TaskCompletionSource? _active;

    private TaskCompletionSource BeginActive() =>
        _active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    private void EndActive(TaskCompletionSource active)
    {
        if (_active == active)
            _active = null;
        active.TrySetResult();
    }

    public void Cancel()
    {
        if (_cts is null || !_state.RequestCancel())
            return;
        _cts.Cancel();
        _cancelTimer = _clock.Schedule(CancelTimeout, () =>
        {
            // IDE-P3-051: the engine did not return; abandon it.
            _log.Error(Category, "The build engine did not return within 10 s of the cancellation; the build was abandoned.");
            _forced?.TrySetResult(true);
        });
    }

    /// <summary>IDE-P3-020…022: saves the member documents per <c>environment.autoSaveBeforeBuild</c>; false = do not build.</summary>
    public Task<bool> SaveBeforeBuildAsync() => SaveBeforeBuildAsync(_projects.IsMember);

    private async Task<bool> SaveBeforeBuildAsync(Func<string, bool> saves)
    {
        var mode = _settings.Get(SettingsCatalog.EnvironmentAutoSaveBeforeBuild);
        if (mode == "none")
            return true;
        var set = _documents.Documents
            .Where(d => d.IsModified && d.FilePath is { } path && saves(path))
            .OrderBy(d => string.Equals(d.FilePath, _projects.Project?.FilePath, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();
        if (set.Count == 0)
            return true;
        void Aborted() => _status.ShowMessage(Strings.Msg_Build_SaveFailedAbort, TimeSpan.FromSeconds(5));
        if (mode == "prompt")
            return await _documents.PromptSaveChangesAsync(set, Aborted); // the question's Cancel itself shows nothing
        foreach (var document in set)
        {
            if (!await _documents.SaveAsync(document))
            {
                Aborted();
                return false;
            }
        }

        return true;
    }

    private async Task<BuildResult?> RunEngineAsync(Subject subject, BuildKind kind, EventsPolicy events)
    {
        _finished.Clear();
        if (_settings.Get(SettingsCatalog.OutputClearOnBuild))
            _output.Clear(OutputChannels.Build);
        _messages.BeginBuild();
        _internalError = false;
        ShowBuildOutput?.Invoke();
        _eventsSkipped = events == EventsPolicy.Skip;

        var paths = _toolchain.Resolve();
        var request = new BuildRequest
        {
            ProjectPath = subject.Path,
            Configuration = subject.Configuration,
            Platform = subject.Platform,
            StopOnFirstFailure = !_settings.Get(SettingsCatalog.BuildContinueAfterFailure), // IDE-PM-172
            Target = kind switch
            {
                BuildKind.Rebuild => BuildTarget.Rebuild,
                BuildKind.Clean => BuildTarget.Clean,
                _ => BuildTarget.Build,
            },
            Verbose = _settings.Get(SettingsCatalog.BuildShowCommandLines),
            MaxParallelism = _settings.Get(SettingsCatalog.BuildJobs), // IDE-TGT-002: 0 = the engine decides (project key, then processors)
            ToolchainDirectory = paths.LlvmBinDir,
            RtlDirectory = paths.RtlDir,
            // IDE-P3-010: the framework and (P7) the data-access directory contribute <dir>\src, in that order.
            ExtraSearchPaths = [.. new[] { paths.FrameworkDir, paths.DataDir }.OfType<string>().Select(dir => Path.Combine(dir, "src"))],
            Events = events, // IDE-TRUST-002: RequireTrust, or Skip for this one build; never Run
            TrustStore = _trust.Store,
        };

        using var cts = new CancellationTokenSource();
        _cts = cts;
        _forced = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pump = new BuildEventPump(_clock, _dispatcher, Deliver);
        var engineTask = Task.Run(() => _engine.RunAsync(request, pump, cts.Token), CancellationToken.None);
        BuildResult? result = null;
        Exception? failure = null;
        try
        {
            var done = await Task.WhenAny(engineTask, _forced.Task);
            if (done == engineTask)
                result = await engineTask;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cts.IsCancellationRequested)
        {
            failure = e; // IDE-P3-037
        }
        catch (OperationCanceledException)
        {
            result = null; // an engine that throws on cancel instead of returning
        }
        finally
        {
            _cancelTimer?.Dispose();
            _cancelTimer = null;
            _cts = null;
        }

        if (result is null && failure is null)
        {
            // Abandoned (IDE-P3-051) or threw OperationCanceledException: canceled.
            pump.Abandon();
            _ = engineTask.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            Finish(Strings.Msg_Build_Canceled, success: false);
            return null;
        }

        pump.Drain();
        if (failure is not null)
        {
            _log.Error(Category, "The build engine failed.", failure);
            _output.Append(OutputChannels.Build, failure.ToString(), OutputKind.Error);
            _messages.AddError(Strings.Msg_Build_InternalError);
            Finish(Strings.Msg_Build_InternalError, success: false);
            ShowMessages?.Invoke();
            return null;
        }

        // I-225: the library goes beside the loaded project's program (the deployer reads that project's units).
        if (result is { Canceled: false } && kind != BuildKind.Clean
            && result.Projects.LastOrDefault(p => Same(p.ProjectPath, _projects.Project?.FilePath)) is { Success: true, OutputPath: { } built })
            await DeployDatabaseLibraryAsync(built);
        Finish(_internalError && !result!.Success ? Strings.Msg_Build_InternalError
            : GroupSummary(result!) ?? Summary(result!, kind, subject.Name), success: result!.Success);
        if (result.Errors > 0 && !result.Canceled && _settings.Get(SettingsCatalog.MessagesAutoShowOnError))
            ShowMessages?.Invoke();
        return result;
    }

    /// <summary>Puts the database engine library beside a built program that needs it (I-225; set by the service graph).</summary>
    public Func<string, Task<LibraryDeployResult>>? DeployDatabaseLibrary { get; set; }

    /// <summary>
    /// I-225: after a successful Build or Rebuild, before the summary. A copy is one Output line; a library that was not
    /// found or could not be copied is an Output line and a warning row (the program builds, but cannot open a database).
    /// </summary>
    private async Task DeployDatabaseLibraryAsync(string outputPath)
    {
        if (DeployDatabaseLibrary is not { } deploy)
            return;
        LibraryDeployResult outcome;
        try
        {
            outcome = await deploy(outputPath);
        }
        catch (Exception e)
        {
            _log.Error(Category, "The database engine library could not be deployed.", e);
            outcome = new LibraryDeployResult(LibraryDeployStatus.Failed, e.Message);
        }

        const string Name = DatabaseLibraryDeployer.FileName;
        switch (outcome.Status)
        {
            case LibraryDeployStatus.Copied:
                _output.Append(OutputChannels.Build, Format(Strings.Msg_Build_DatabaseLibraryCopied, Name), OutputKind.Ide);
                break;
            case LibraryDeployStatus.Missing:
                Warn(Format(Strings.Msg_Build_DatabaseLibraryMissing, Name));
                break;
            case LibraryDeployStatus.Failed:
                Warn(Format(Strings.Msg_Build_DatabaseLibraryCopyFailed, Name, outcome.Detail ?? ""));
                break;
        }

        void Warn(string text)
        {
            _output.Append(OutputChannels.Build, text, OutputKind.Error);
            _messages.AddWarning(text);
        }
    }

    private void Deliver(IReadOnlyList<BuildEvent> batch)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var e in batch)
        {
            switch (e)
            {
                case BuildEvent.Started s:
                    FlushDiagnostics(diagnostics);
                    _output.Append(OutputChannels.Build, Format(Strings.Msg_Build_StartedHeader, Path.GetFileNameWithoutExtension(s.ProjectPath),
                        s.Configuration, s.Platform), OutputKind.Ide);
                    if (_eventsSkipped)
                    {
                        // IDE-TRUST-007: directly after the (first) started header.
                        _eventsSkipped = false;
                        _output.Append(OutputChannels.Build, Strings.Msg_Trust_EventsSkipped, OutputKind.Ide);
                    }

                    break;
                case BuildEvent.Diagnostic { Value: var d }:
                    _output.Append(OutputChannels.Build, d.ToText(), d.Severity == DiagnosticSeverity.Error ? OutputKind.Error : OutputKind.Normal);
                    if (d.Code != "E9010") // IDE-P3-034: the cancellation diagnostic is not a row
                        diagnostics.Add(d);
                    _internalError |= d.Code == "E0001";
                    if (d.Code == "E4008")
                        _lockedOutput ??= d.Arguments.Length > 0 && d.Arguments[0] is string locked ? locked : d.Location.Path;
                    break;
                case BuildEvent.CommandLine c:
                    _output.Append(OutputChannels.Build, Format(Strings.Msg_Build_CommandLine, c.Text), OutputKind.Command);
                    break;
                case BuildEvent.OutputLine o:
                    _output.Append(OutputChannels.Build, o.Text, o.Source == OutputSource.Info ? OutputKind.Ide : OutputKind.Normal);
                    break;
                case BuildEvent.Finished f:
                    _finished.Add(f);
                    break;
            }
        }

        FlushDiagnostics(diagnostics);
    }

    private void FlushDiagnostics(List<Diagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
            return;
        _messages.AddDiagnostics(diagnostics);
        diagnostics.Clear();
    }

    private void Finish(string text, bool success)
    {
        _output.Append(OutputChannels.Build, text, success ? OutputKind.Success : OutputKind.Failure);
        _messages.AddSummary(text, success ? MessageSeverity.Info : MessageSeverity.Error);
        ResultText = text;
        BuildFinished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// IDE-PM-173: the summary of a request that built more than one project of which at least one failed or was skipped —
    /// the counts and the names of the skipped projects; null for every other request. A skipped project is one that
    /// finished without success and without an error of its own (CMP-1341).
    /// </summary>
    private string? GroupSummary(BuildResult result)
    {
        if (result.Canceled || _finished.Count < 2 || _finished.All(f => f.Success))
            return null;
        var skipped = _finished.Where(f => f is { Success: false, Errors: 0 }).Select(f => Path.GetFileNameWithoutExtension(f.ProjectPath)).ToList();
        var succeeded = _finished.Count(f => f.Success);
        var format = Strings.Msg_Build_GroupSummary;
        if (skipped.Count == 0 && format.IndexOf(": {3}", StringComparison.Ordinal) is >= 0 and var names)
            format = format[..names]; // "when {2} = 0 the text ends after '{2} skipped'"
        return Format(format, succeeded, _finished.Count - succeeded - skipped.Count, skipped.Count, string.Join(", ", skipped));
    }

    /// <summary>IDE-P3-034: the summary text of a build result.</summary>
    public static string Summary(BuildResult result, BuildKind kind, string projectName)
    {
        ArgumentNullException.ThrowIfNull(result);
        var seconds = result.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture);
        if (result.Canceled)
            return Strings.Msg_Build_Canceled;
        if (!result.Success)
        {
            if (result.Projects.Any(p => p.Diagnostics.Any(d => d.Code == "E0001")))
                return Strings.Msg_Build_InternalError;
            return result.Errors == 1 ? Strings.Msg_Build_FailedOne : Format(Strings.Msg_Build_FailedMany, result.Errors);
        }

        if (kind == BuildKind.Clean)
            return Format(Strings.Msg_Build_Cleaned, projectName);
        if (result.UpToDate)
            return Format(Strings.Msg_Build_UpToDate, projectName);
        return result.Warnings switch
        {
            0 => Format(Strings.Msg_Build_Succeeded, seconds),
            1 => Format(Strings.Msg_Build_SucceededWithWarningsOne, seconds),
            _ => Format(Strings.Msg_Build_SucceededWithWarningsMany, seconds, result.Warnings),
        };
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute) : Commands.ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
