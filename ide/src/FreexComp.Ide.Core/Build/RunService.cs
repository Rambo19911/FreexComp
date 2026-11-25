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

    /// <summary>IDE-P3-090/091: terminates the process tree; Idle afterwards in every case.</summary>
    Task StopAsync();

    /// <summary>IDE-P3-092: stop, then the start sequence the session was started with.</summary>
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
    private IRunningProcess? _waitingHost; // IDE-P3-089: a console host whose program ended, still at its key prompt
    private bool _lastWithDebugger;

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
        commands.RegisterHandler("run.startWithoutDebugging", new Handler(() => _projects.Project is not null, () => StartCommandAsync(false)));
        if (debugger)
            return;
        commands.RegisterHandler("run.start", new Handler(() => _projects.Project is not null, StartWithoutDebuggerNoticeAsync));
        commands.RegisterHandler("run.stop", new Handler(() => _session is not null, StopAsync));
        commands.RegisterHandler("run.restart", new Handler(() => _session is not null, RestartAsync));
    }

    /// <summary>A program started without the debugger is running.</summary>
    public bool IsRunning => _session is not null;

    /// <summary>
    /// IDE-TGT-013: the host application of the open library project — <c>run.hostApplication</c> resolved against the
    /// project directory; null when the project is no library or names none.
    /// </summary>
    public string? HostApplication =>
        _projects.Project is { AppType: "library" } && _projects.Definition is { } definition ? ProjectOptions.GetHostApplicationPath(definition) : null;

    /// <summary>IDE-TGT-014: <see cref="HostApplication"/> when that file exists (what a library is debugged through), else null.</summary>
    public string? ExistingHostApplication => HostApplication is { } host && _fileSystem.FileExists(host) ? host : null;

    public async Task<bool> StartAsync(bool withDebugger = false)
    {
        if (_projects.Project is not { } project || _state.Current.State != IdeState.Idle)
            return false;
        // IDE-TGT-013: a library is runnable iff it names a host application; that program is what is started.
        var host = HostApplication;
        if (project.AppType is not ("console" or "gui") && host is null)
        {
            _status.ShowMessage(Strings.Msg_Run_NotRunnable, StatusDuration);
            return false;
        }

        _lastWithDebugger = withDebugger;
        if (host is null)
            return await BuildThenAsync(project, exe => LaunchAsync(project, exe));
        return await BuildThenAsync(project, async _ =>
        {
            if (!_fileSystem.FileExists(host))
            {
                await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Run.NoExecutable", [host]));
                return false;
            }

            return await LaunchAsync(project, host);
        });
    }

    /// <summary>
    /// IDE-P3-080/081, the part shared with a start under the debugger: saves and builds, offers the previous executable
    /// after a failed build, then calls <paramref name="launch"/> with the executable while the state is still Building
    /// (IDE-P3-002); the state is Idle afterwards unless the launch moved on.
    /// </summary>
    public async Task<bool> BuildThenAsync(ProjectModel project, Func<string, Task<bool>> launch)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(launch);
        var result = await _build.BuildAsync(BuildKind.Build, continueToRun: true);
        try
        {
            if (result is null || result.Canceled)
                return false;
            string? exe;
            if (result.Success)
            {
                exe = result.OutputPath;
                if (exe is null || !_fileSystem.FileExists(exe))
                {
                    await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Run.NoExecutable", [exe ?? PreviousExecutable(project) ?? ""]));
                    return false;
                }
            }
            else
            {
                exe = PreviousExecutable(project);
                if (exe is null || !_fileSystem.FileExists(exe)
                    || await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Run.BuildFailedRunPrevious", [])) != "RunLastBuild")
                {
                    return false;
                }
            }

            return await launch(exe);
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
        return await StartAsync(_lastWithDebugger);
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

    /// <summary>IDE-P3-089: closes the job of a console host still waiting at its key prompt (its window closes).</summary>
    private void CloseWaitingHost()
    {
        _waitingHost?.Dispose();
        _waitingHost = null;
    }

    /// <summary>
    /// The output file of the active configuration on the active platform (IDE-P3-081 fallback; IDE-PLAT-005:
    /// <c>ProjectOptions.GetOutputPath</c>, <c>.dll</c> for a library, IDE-TGT-011).
    /// </summary>
    public string? PreviousExecutable(ProjectModel project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var definition = _projects.Definition;
        if (definition is null || ConfigurationEdits.Effective(definition, project.ActiveConfiguration) is null)
            return null;
        return Path.GetFullPath(FreexComp.Build.ProjectOptions.GetOutputPath(definition, project.ActiveConfiguration, project.ActivePlatform));
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
    /// The working directory of a start: <c>run.workingDir</c> against the project directory, else the executable's folder.
    /// A missing one shows <c>Msg.Run.WorkingDirMissing</c> and gives null.
    /// </summary>
    public async Task<string?> ResolveWorkingDirAsync(ProjectModel project, string exe)
    {
        ArgumentNullException.ThrowIfNull(project);
        var run = _projects.Definition?.Run ?? new RunSettings("", "");
        var workingDir = string.IsNullOrWhiteSpace(run.WorkingDir)
            ? Path.GetDirectoryName(exe)!
            : Path.GetFullPath(Path.Combine(project.Directory, run.WorkingDir.Replace('/', '\\')));
        if (_fileSystem.DirectoryExists(workingDir))
            return workingDir;
        if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Run.WorkingDirMissing", [workingDir])) == "RunParameters")
            _ = OpenRunParameters?.Invoke();
        return null;
    }

    private async Task<bool> LaunchAsync(ProjectModel project, string exe)
    {
        var run = _projects.Definition?.Run ?? new RunSettings("", "");
        if (await ResolveWorkingDirAsync(project, exe) is not { } workingDir)
            return false;

        // IDE-P3-082: console programs follow run.consoleMode; GUI programs are always captured without a console.
        // IDE-TGT-013: the host of a library is started like a program of its own — no console host, nothing captured.
        var external = project.AppType == "console" && _settings.Get(SettingsCatalog.RunConsoleMode) == "external";
        var request = new ProcessStartRequest(exe, run.Arguments, workingDir) { Capture = !external && project.AppType != "library", ConsoleHost = external };
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
            _waitingHost = session.Process; // IDE-P3-088: the window stays open at its key prompt
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

    private sealed class Handler(Func<bool> canExecute, Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();
    }
}
