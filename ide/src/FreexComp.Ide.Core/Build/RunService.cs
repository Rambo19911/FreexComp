using System.ComponentModel;
using System.Globalization;
using FreexComp.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Build;

/// <summary>Run, stop and restart of the open project's program (phase3-build-run.md §8, §9). UI thread only.</summary>
public interface IRunService
{
    /// <summary>
    /// IDE-P3-080/081: runnable check, toolchain check, save, build; then the program starts (state Running). False when
    /// nothing was started.
    /// </summary>
    Task<bool> StartAsync(bool withDebugger = false);

    /// <summary>
    /// IDE-PM-192: <paramref name="projectPath"/> is an available listed member of the project group that is runnable,
    /// and the IDE is Idle. Synchronous (the state of a context-menu item).
    /// </summary>
    bool CanStartMember(string? projectPath);

    /// <summary>
    /// IDE-PM-193…198: <c>run.startWithoutDebugging</c> for a listed member of the project group — the sequence of
    /// <see cref="StartAsync"/> with the member's own values; the active project does not change. The path of the
    /// loaded project is <see cref="StartAsync"/>. False when nothing was started.
    /// </summary>
    Task<bool> StartMemberAsync(string projectPath);

    /// <summary>IDE-P3-090/091: terminates the process tree; Idle afterwards in every case.</summary>
    Task StopAsync();

    /// <summary>
    /// IDE-P3-092, IDE-PM-197: stop, then the start sequence the session was started with, for the run target the
    /// program was started for (the loaded project or a listed member).
    /// </summary>
    Task<bool> RestartAsync();

    /// <summary>IDE-P3-093: while a program runs, asks <c>Msg.Run.StopOnExit</c> / <c>StopOnClose</c> and stops it; false = canceled.</summary>
    Task<bool> ConfirmStopAsync(bool exiting);

    /// <summary>Exit code of the last program that ended by itself (<c>run.lastExitCode</c>).</summary>
    int? LastExitCode { get; }
}

/// <inheritdoc cref="IRunService"/>
public sealed class RunService : IRunService
{
    private const string Category = "run";
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(10);

    private readonly BuildService _build;
    private readonly IProjectService _projects;
    private readonly ISettingsService _settings;
    private readonly IdeStateMachine _state;
    private readonly IToolchainLocator _toolchain;
    private readonly IOutputService _output;
    private readonly IProcessLauncher _launcher;
    private readonly List<(Task<int> Exited, Task Posted)> _exitPosts = [];
    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private bool _debugNoticeShown;
    private readonly ILog _log;
    private Session? _session;
    private readonly Lock _hostGate = new(); // the waiting-host slot is shared with the debug console, which runs on session threads
    private IRunningProcess? _waitingHost; // IDE-P3-089: a console host whose program ended, still at its key prompt
    private bool _lastWithDebugger;
    private string? _lastMember; // IDE-PM-197: the listed member the last start was for; null = the loaded project

    public RunService(BuildService build, IProjectService projects, ISettingsService settings, IdeStateMachine state, IToolchainLocator toolchain,
        IOutputService output, IProcessLauncher launcher, IFileSystem fileSystem, IClock clock, IUiDispatcher dispatcher, IDialogService dialogs,
        IStatusService status, ILog log)
    {
        _build = build;
        _projects = projects;
        _settings = settings;
        _state = state;
        _toolchain = toolchain;
        _output = output;
        _launcher = launcher;
        _fileSystem = fileSystem;
        _clock = clock;
        _dispatcher = dispatcher;
        _dialogs = dialogs;
        _status = status;
        _log = log;
    }

    public int? LastExitCode { get; private set; }

    /// <summary>The project group (IDE-PM-190…198; set by the service graph). Null: only the loaded project is run.</summary>
    public IProjectGroupService? Group { get; set; }

    /// <summary>The running program's process (tests, the debugger later); null when nothing runs.</summary>
    public IRunningProcess? Process => _session?.Process;

    /// <summary>A started program ended (by itself or stopped).</summary>
    public event EventHandler? ProgramEnded;

    /// <summary>Called when a captured program starts and <c>output.autoShowOnRun</c> is set (UI-TW-501).</summary>
    public Action? ShowProgramOutput { get; set; }

    /// <summary>Opens the Run Parameters dialog (the <c>Run Parameters…</c> button of <c>Msg.Run.WorkingDirMissing</c>).</summary>
    public Func<Task>? OpenRunParameters { get; set; }

    /// <param name="debugger">
    /// Phase 6: <c>run.start</c>, <c>run.stop</c> and <c>run.restart</c> belong to the debug service, which passes them on
    /// to this service while no debug session exists; only <c>run.startWithoutDebugging</c> is registered here.
    /// </param>
    public void RegisterCommands(ICommandService commands, bool debugger = false)
    {
        ArgumentNullException.ThrowIfNull(commands);
        // IDE-PM-190, 192: without an argument the loaded project (menu, tool bar, shortcut); the context menu of a
        // listed root gives the member's path — that start needs the Idle state and asks no restart question.
        commands.RegisterHandler("run.startWithoutDebugging", new Handler(
            member => IsListedTarget(member) ? CanStartMember(member) : _projects.Project is not null,
            member => IsListedTarget(member) ? StartMemberAsync(member!) : StartCommandAsync(false)));
        if (debugger)
            return;
        commands.RegisterHandler("run.start", new Handler(_ => _projects.Project is not null, _ => StartWithoutDebuggerNoticeAsync()));
        commands.RegisterHandler("run.stop", new Handler(_ => _session is not null, _ => StopAsync()));
        commands.RegisterHandler("run.restart", new Handler(_ => _session is not null, _ => RestartAsync()));
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// IDE-PM-192: the command argument names a project other than the loaded one (a target that is the active project's
    /// own path behaves as no argument).
    /// </summary>
    internal bool IsListedTarget(string? projectPath) =>
        !string.IsNullOrEmpty(projectPath) && !Same(Path.GetFullPath(projectPath), _projects.Project?.FilePath);

    /// <summary>The loaded project as a run target; null without one.</summary>
    public RunTarget? ActiveTarget() => _projects.Project is { } project ? TargetOf(project) : null;

    private RunTarget TargetOf(ProjectModel project) => new(project.FilePath, project.Name, project.Directory, project.AppType,
        project.ActiveConfiguration, project.ActivePlatform, _projects.Definition, IsListedMember: false);

    /// <summary>
    /// The available member <paramref name="projectPath"/> of the group as a run target, with what the group knows of it
    /// now (IDE-PM-194); null when it is no available member.
    /// </summary>
    public RunTarget? MemberTarget(string? projectPath)
    {
        if (string.IsNullOrEmpty(projectPath))
            return null;
        var path = Path.GetFullPath(projectPath);
        return Group?.Members.FirstOrDefault(m => m.IsAvailable && Same(m.FilePath, path)) is { Definition: { } definition } member
            ? new RunTarget(member.FilePath, member.Name, Path.GetDirectoryName(member.FilePath)!, definition.AppType,
                member.Configuration!, member.Platform!, definition, IsListedMember: true)
            : null;
    }

    /// <summary>IDE-PM-194: <paramref name="target"/> as it is now; null when it is a listed member that is no longer available.</summary>
    private RunTarget? Now(RunTarget target) => target.IsListedMember
        ? MemberTarget(target.ProjectPath)
        : _projects.Project is { } project && Same(project.FilePath, target.ProjectPath) ? TargetOf(project) : target;

    /// <summary><c>Msg.Run.MemberUnavailable</c> for the member <paramref name="projectPath"/> (the file name when it cannot be read).</summary>
    internal void ShowMemberUnavailable(string projectPath)
    {
        var name = Group?.Members.FirstOrDefault(m => Same(m.FilePath, projectPath))?.Name ?? Path.GetFileNameWithoutExtension(projectPath);
        _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Run_MemberUnavailable, name), StatusDuration);
    }

    /// <summary>A program started without the debugger is running.</summary>
    public bool IsRunning => _session is not null;

    /// <summary>
    /// IDE-TGT-013: the host application of the open library project — <c>run.hostApplication</c> resolved against the
    /// project directory; null when the project is no library or names none.
    /// </summary>
    public string? HostApplication => ActiveTarget()?.HostApplication;

    /// <summary>IDE-TGT-014: <see cref="HostApplication"/> when that file exists (what a library is debugged through), else null.</summary>
    public string? ExistingHostApplication => ActiveTarget() is { } target ? ExistingHost(target) : null;

    /// <summary>IDE-TGT-014 for any run target: the host application of a library when that file exists, else null.</summary>
    public string? ExistingHost(RunTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.HostApplication is { } host && _fileSystem.FileExists(host) ? host : null;
    }

    public async Task<bool> StartAsync(bool withDebugger = false) =>
        ActiveTarget() is { } target && await StartAsync(target, withDebugger);

    public bool CanStartMember(string? projectPath) =>
        _state.Current.State == IdeState.Idle && IsListedTarget(projectPath) && MemberTarget(projectPath) is { IsRunnable: true };

    public async Task<bool> StartMemberAsync(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        if (!IsListedTarget(projectPath))
            return await StartAsync();
        // IDE-PM-192: a path that is not an available member does nothing.
        return MemberTarget(projectPath) is { } target && await StartAsync(target, withDebugger: false);
    }

    /// <summary>IDE-P3-080/081, IDE-PM-193: the start sequence for <paramref name="target"/>, with the target's own values.</summary>
    private async Task<bool> StartAsync(RunTarget target, bool withDebugger)
    {
        if (_state.Current.State != IdeState.Idle)
            return false;
        // IDE-TGT-013: a library is runnable iff it names a host application; that program is what is started.
        if (!target.IsRunnable)
        {
            _status.ShowMessage(Strings.Msg_Run_NotRunnable, StatusDuration);
            return false;
        }

        _lastWithDebugger = withDebugger;
        _lastMember = target.IsListedMember ? target.ProjectPath : null;
        return await BuildThenAsync(target, async (built, output) =>
        {
            if (built.AppType is "console" or "gui")
                return await LaunchAsync(built, output);
            if (built.HostApplication is not { } host)
            {
                _status.ShowMessage(Strings.Msg_Run_NotRunnable, StatusDuration); // the project file saved by the build names no host any more
                return false;
            }

            if (!_fileSystem.FileExists(host))
            {
                await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Run.NoExecutable", [host]));
                return false;
            }

            return await LaunchAsync(built, host);
        });
    }

    /// <summary>
    /// IDE-P3-080/081, the part shared with a start under the debugger, for any run target (IDE-PM-193): saves and
    /// builds — one build request for the target's project —, offers the previous executable after a failed build, then
    /// calls <paramref name="launch"/> while the state is still Building (IDE-P3-002); the state is Idle afterwards
    /// unless the launch moved on. <paramref name="launch"/> gets the target as it is after the build returned
    /// (IDE-PM-194: the run parameters the build saved) and the executable. A listed member that is no longer available
    /// at that point: nothing starts, status <c>Msg.Run.MemberUnavailable</c>.
    /// </summary>
    public async Task<bool> BuildThenAsync(RunTarget target, Func<RunTarget, string, Task<bool>> launch)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(launch);
        var result = target.IsListedMember
            ? await _build.BuildMemberAsync(target.ProjectPath, BuildKind.Build, continueToRun: true)
            : await _build.BuildAsync(BuildKind.Build, continueToRun: true);
        try
        {
            if (result is null || result.Canceled)
                return false;
            if (Now(target) is not { } built)
            {
                ShowMemberUnavailable(target.ProjectPath);
                return false;
            }

            string? exe;
            if (result.Success)
            {
                exe = result.OutputPath;
                if (exe is null || !_fileSystem.FileExists(exe))
                {
                    await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Run.NoExecutable", [exe ?? PreviousExecutable(built) ?? ""]));
                    return false;
                }
            }
            else
            {
                // IDE-P3-080, IDE-PM-195: the program of an earlier build of the target's active configuration.
                exe = PreviousExecutable(built);
                if (exe is null || !_fileSystem.FileExists(exe)
                    || await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Run.BuildFailedRunPrevious", [])) != "RunLastBuild")
                {
                    return false;
                }
            }

            return await launch(built, exe);
        }
        finally
        {
            if (_state.Current.State == IdeState.Building)
                _state.TryTransition(IdeStateInfo.Idle);
        }
    }

    public async Task StopAsync()
    {
        if (_session is not { } session)
            return;
        session.Stopping = true;
        bool killed;
        string reason = "";
        try
        {
            killed = await session.Process.KillTreeAsync();
        }
        catch (Win32Exception e)
        {
            killed = false;
            reason = e.Message;
        }

        if (_session != session)
            return; // it ended by itself meanwhile
        _session = null;
        if (killed)
        {
            session.Process.Dispose();
            _output.Append(OutputChannels.Program, Strings.Msg_Run_Terminated, OutputKind.Ide);
            _status.ShowMessage(Strings.Msg_Run_Terminated, StatusDuration);
            _log.Info(Category, Strings.Msg_Run_Terminated);
            Leave();
        }
        else
        {
            // IDE-P3-091: forced Idle; the job handle is leaked deliberately (closing it would retry the kill).
            _log.Error(Category, "The program could not be stopped: " + reason);
            _state.ForceIdle();
            ProgramEnded?.Invoke(this, EventArgs.Empty);
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Run.KillFailed", [reason]));
        }
    }

    public async Task<bool> RestartAsync()
    {
        await StopAsync();
        // IDE-PM-197: the run target the program was started for, not the active project.
        if (_lastMember is not { } member || !IsListedTarget(member))
            return await StartAsync(_lastWithDebugger);
        if (MemberTarget(member) is { } target)
            return await StartAsync(target, _lastWithDebugger);
        ShowMemberUnavailable(member);
        return false;
    }

    public async Task<bool> ConfirmStopAsync(bool exiting)
    {
        if (_session is { } session)
        {
            var id = exiting ? "Msg.Run.StopOnExit" : "Msg.Run.StopOnClose";
            var answer = await _dialogs.ShowMessageAsync(new MessageRequest(id, [Path.GetFileName(session.ExePath)]));
            if (answer != (exiting ? "StopAndExit" : "StopAndClose"))
                return false;
            await StopAsync();
        }

        CloseWaitingHost(); // IDE-P3-089: project close / IDE exit, without asking
        return true;
    }

    /// <summary>
    /// IDE-P3-089: closes the job of a console host still waiting at its key prompt (its window closes). Any thread; also
    /// at every debug start (IDE-P3-101).
    /// </summary>
    internal void CloseWaitingHost()
    {
        IRunningProcess? host;
        lock (_hostGate)
        {
            host = _waitingHost;
            _waitingHost = null;
        }

        host?.Dispose();
    }

    /// <summary>
    /// IDE-P3-088 / IDE-P3-097: <paramref name="host"/>, whose program ended, becomes the one waiting host (a previous one
    /// is closed). Any thread: the debug console hands its window over from a session thread.
    /// </summary>
    internal void AdoptWaitingHost(IRunningProcess host)
    {
        IRunningProcess? previous;
        lock (_hostGate)
        {
            previous = _waitingHost;
            _waitingHost = host;
        }

        if (previous is not null && !ReferenceEquals(previous, host))
            previous.Dispose();
    }

    /// <summary>The launcher of the program runs, for the debug console (IDE-P3-097).</summary>
    internal IProcessLauncher Launcher => _launcher;

    /// <summary>
    /// The output file of the active configuration on the active platform (IDE-P3-081 fallback; IDE-PLAT-005:
    /// <c>ProjectOptions.GetOutputPath</c>, <c>.dll</c> for a library, IDE-TGT-011).
    /// </summary>
    public string? PreviousExecutable(ProjectModel project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return PreviousExecutable(TargetOf(project));
    }

    /// <summary>
    /// The output file of the target's active configuration on its platform (IDE-PM-195; a listed member: its own
    /// definition, configuration and platform).
    /// </summary>
    public static string? PreviousExecutable(RunTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Definition is not { } definition || ConfigurationEdits.Effective(definition, target.Configuration) is null)
            return null;
        return Path.GetFullPath(FreexComp.Build.ProjectOptions.GetOutputPath(definition, target.Configuration, target.Platform));
    }

    /// <summary>"0", or "-1073741819 (0xC0000005)" when the code is above 255 or negative (states-and-messages.md).</summary>
    public static string FormatExitCode(int code) => code is > 255 or < 0
        ? string.Create(CultureInfo.InvariantCulture, $"{code} (0x{unchecked((uint)code):X8})")
        : code.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// UI-MENU-020 / IDE-P3-086: until the debugger exists (Phases 3–5) <c>run.start</c> runs like
    /// <c>run.startWithoutDebugging</c>; the status bar says so once per IDE session.
    /// </summary>
    private Task StartWithoutDebuggerNoticeAsync()
    {
        if (!_debugNoticeShown)
        {
            _debugNoticeShown = true;
            _status.ShowMessage(Strings.Msg_Debug_NotAvailable, StatusDuration);
        }

        return StartCommandAsync(withDebugger: false);
    }

    /// <summary><c>run.start</c> / <c>run.startWithoutDebugging</c>: asks to restart while a program runs, else starts.</summary>
    public async Task StartCommandAsync(bool withDebugger)
    {
        if (_state.Current.State == IdeState.Running && _session is { } session)
        {
            // IDE-P3-004: Restart instead.
            var answer = await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Run.AlreadyRunning", [Path.GetFileName(session.ExePath)]));
            if (answer == "Restart")
                await RestartAsync();
            return;
        }

        await StartAsync(withDebugger);
    }

    /// <summary>
    /// The working directory of a start of <paramref name="target"/>: its <c>run.workingDir</c> against its own
    /// directory, else the executable's folder. A missing one shows <c>Msg.Run.WorkingDirMissing</c> and gives null.
    /// IDE-PM-198: a missing one of a listed member shows <c>Msg.Run.MemberWorkingDirMissing</c> (the Run Parameters
    /// dialog is the active project's).
    /// </summary>
    public async Task<string?> ResolveWorkingDirAsync(RunTarget target, string exe)
    {
        ArgumentNullException.ThrowIfNull(target);
        var run = target.Run;
        var workingDir = string.IsNullOrWhiteSpace(run.WorkingDir)
            ? Path.GetDirectoryName(exe)!
            : Path.GetFullPath(Path.Combine(target.Directory, run.WorkingDir.Replace('/', '\\')));
        if (_fileSystem.DirectoryExists(workingDir))
            return workingDir;
        if (target.IsListedMember)
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Run.MemberWorkingDirMissing", [target.Name, workingDir]));
        else if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Run.WorkingDirMissing", [workingDir])) == "RunParameters")
            _ = OpenRunParameters?.Invoke();
        return null;
    }

    private async Task<bool> LaunchAsync(RunTarget target, string exe)
    {
        var run = target.Run;
        if (await ResolveWorkingDirAsync(target, exe) is not { } workingDir)
            return false;

        // IDE-P3-082: console programs follow run.consoleMode; GUI programs are always captured without a console.
        // IDE-TGT-013: the host of a library is started like a program of its own — no console host, nothing captured.
        var external = target.AppType == "console" && _settings.Get(SettingsCatalog.RunConsoleMode) == "external";
        var request = new ProcessStartRequest(exe, run.Arguments, workingDir) { Capture = !external && target.AppType != "library", ConsoleHost = external };
        if (_settings.Get(SettingsCatalog.OutputClearOnRun))
            _output.Clear(OutputChannels.Program);

        CloseWaitingHost(); // IDE-P3-089: at most one waiting console window
        var session = new Session(exe, _clock.Now) { ConsoleHost = external };
        IRunningProcess process;
        try
        {
            process = _launcher.Start(request, line => _output.Append(OutputChannels.Program, line.Text,
                line.IsError ? OutputKind.Error : OutputKind.Normal));
        }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException)
        {
            _log.Warning(Category, $"Cannot start {exe}: {e.Message}");
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Run.StartFailed", [exe, e.Message]));
            return false;
        }

        session.Process = process;
        _session = session;
        if (!_state.TryTransition(new IdeStateInfo(IdeState.Running, null, false, exe, process.ProcessId)))
            _state.ForceIdle();
        var started = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Run_Started, exe, run.Arguments, process.ProcessId);
        _output.Append(OutputChannels.Program, started, OutputKind.Ide);
        _log.Info(Category, started);
        if (external)
            _output.Append(OutputChannels.Program, Strings.Run_ExternalConsoleNote, OutputKind.Ide);
        else if (_settings.Get(SettingsCatalog.OutputAutoShowOnRun))
            ShowProgramOutput?.Invoke();

        var exited = process.Exited;
        var posted = exited.ContinueWith(t => _dispatcher.Post(() => OnExited(session, t)), TaskScheduler.Default);
        lock (_exitPosts)
            _exitPosts.Add((exited, posted));
        return true;
    }

    /// <summary>
    /// I-210: every program that has exited has had its exit posted to the UI dispatcher (a running one does not count);
    /// a test settles the exits of the programs it ran before it ends.
    /// </summary>
    public bool ExitsPosted
    {
        get
        {
            lock (_exitPosts)
            {
                _exitPosts.RemoveAll(p => p.Posted.IsCompleted);
                return _exitPosts.TrueForAll(p => !p.Exited.IsCompleted);
            }
        }
    }

    /// <summary>Completes once the exits of the programs that have exited so far are posted (see <see cref="ExitsPosted"/>).</summary>
    public Task WhenExitsPostedAsync()
    {
        lock (_exitPosts)
            return Task.WhenAll(_exitPosts.Where(p => p.Exited.IsCompleted).Select(p => p.Posted));
    }

    private void OnExited(Session session, Task<int> exited)
    {
        if (_session != session || session.Stopping)
            return; // stopped: StopAsync reports it
        _session = null;
        if (session.ConsoleHost && !exited.IsCompletedSuccessfully)
        {
            // IDE-P3-088: the host ended (window closed) or its pipe broke before "exited".
            session.Process.Dispose();
            _output.Append(OutputChannels.Program, Strings.Msg_Run_Terminated, OutputKind.Ide);
            _status.ShowMessage(Strings.Msg_Run_Terminated, StatusDuration);
            _log.Info(Category, Strings.Msg_Run_Terminated);
            Leave();
            return;
        }

        var code = exited.IsCompletedSuccessfully ? exited.Result : -1;
        LastExitCode = code;
        var seconds = (_clock.Now - session.StartedAt).TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture);
        var text = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Run_Exited, FormatExitCode(code), seconds);
        _output.Append(OutputChannels.Program, text, OutputKind.Ide);
        _status.ShowMessage(text, StatusDuration);
        _log.Info(Category, text);
        if (session.ConsoleHost)
            AdoptWaitingHost(session.Process); // IDE-P3-088: the window stays open at its key prompt
        else
            session.Process?.Dispose(); // IDE-P3-084: closing the job ends any descendant still alive
        Leave();
    }

    private void Leave()
    {
        if (_state.Current.State == IdeState.Running)
            _state.TryTransition(IdeStateInfo.Idle);
        ProgramEnded?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Session(string exePath, DateTimeOffset startedAt)
    {
        public string ExePath { get; } = exePath;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public IRunningProcess Process { get; set; } = null!;

        public bool Stopping { get; set; }

        /// <summary>Started through the console host (IDE-P3-087): <c>Exited</c> = its <c>exited</c> message.</summary>
        public bool ConsoleHost { get; init; }
    }

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
