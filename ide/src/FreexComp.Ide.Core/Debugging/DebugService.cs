using System.Globalization;
using System.Text;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Debugging;

/// <summary>
/// The IDE side of a debug session (the debug-session interface §6, §6.1): starts the open project's program under the
/// debugger, maps the session state to the IDE state, routes its output and keeps what the views share — the current
/// stop, the selected call frame and the watch list. Session events arrive on the session's thread and are applied on
/// the UI thread in their order. UI thread only.
/// </summary>
public interface IDebugService
{
    /// <summary>The running session; null when none exists.</summary>
    IDebugSession? Session { get; }

    /// <summary>The program is stopped in the debugger (<see cref="CurrentStop"/> is set).</summary>
    bool IsPaused { get; }

    StopInfo? CurrentStop { get; }

    /// <summary>The call frame the views evaluate in, when it is not the top frame; null = the top frame.</summary>
    StackFrameInfo? SelectedFrame { get; }

    /// <summary>The user's watch expressions (UI-TW-1100).</summary>
    WatchList Watches { get; }

    /// <summary>A session started or ended.</summary>
    event EventHandler? SessionChanged;

    /// <summary>The session's state, the stop or the selected frame changed.</summary>
    event EventHandler? StateChanged;

    /// <summary>The program stopped; raised after the state, the execution line and the editor were updated.</summary>
    event EventHandler<StopInfo>? Stopped;

    /// <summary>
    /// Builds the open project and starts its program under the debugger (IDE-P3-086: asks <c>Msg.Debug.NoDebugInfo</c>
    /// first when the configuration has no debug information). False when no session was started.
    /// </summary>
    Task<bool> StartAsync(bool stopOnEntry = false);

    /// <summary>Terminates the session; completes when the IDE is Idle again.</summary>
    Task StopAsync();

    Task<bool> RestartAsync();

    /// <summary>Evaluates in the selected frame; the session must be paused.</summary>
    Task<EvaluateResult> EvaluateAsync(string expression, EvaluateContext context);

    /// <summary>Assigns in the selected frame; the session must be paused.</summary>
    Task<VariableInfo> SetValueAsync(string evaluateName, string valueExpression);

    /// <summary>Makes <paramref name="frame"/> the selected frame (UI-TW-901); frame 0 or null returns to the execution point.</summary>
    void SelectFrame(StackFrameInfo? frame);
}

/// <inheritdoc cref="IDebugService"/>
public sealed class DebugService : IDebugService
{
    /// <summary>The debug adapter in the LLVM tools folder (IDE-P3-010).</summary>
    public const string AdapterFileName = "lldb-dap.exe";

    private const string Category = "debug";
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(10);

    private readonly IDebugSessionFactory _factory;
    private readonly RunService _run;
    private readonly IProjectService _projects;
    private readonly ISettingsService _settings;
    private readonly IdeStateMachine _state;
    private readonly IToolchainLocator _toolchain;
    private readonly IOutputService _output;
    private readonly IBreakpointService _breakpoints;
    private readonly IDocumentService _documents;
    private readonly INavigationService _navigation;
    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly ILog _log;
    private readonly Dictionary<TextDocumentViewModel, ExecutionGlyphs> _decorated = [];
    private Active? _active;
    private StopInfo? _stop;
    private StackFrameInfo? _selectedFrame;

    public DebugService(IDebugSessionFactory factory, RunService run, IProjectService projects, ISettingsService settings, IdeStateMachine state,
        IToolchainLocator toolchain, IOutputService output, IBreakpointService breakpoints, IDocumentService documents, INavigationService navigation,
        IClock clock, IUiDispatcher dispatcher, IDialogService dialogs, IStatusService status, ILog log)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(documents);
        _factory = factory;
        _run = run;
        _projects = projects;
        _settings = settings;
        _state = state;
        _toolchain = toolchain;
        _output = output;
        _breakpoints = breakpoints;
        _documents = documents;
        _navigation = navigation;
        _clock = clock;
        _dispatcher = dispatcher;
        _dialogs = dialogs;
        _status = status;
        _log = log;
        foreach (var document in documents.Documents.OfType<TextDocumentViewModel>())
            Decorate(document);
        documents.DocumentOpened += (_, e) =>
        {
            if (e.Document is TextDocumentViewModel text)
                Decorate(text);
        };
        documents.DocumentClosed += (_, e) =>
        {
            if (e.Document is TextDocumentViewModel text && _decorated.Remove(text, out var glyphs))
                text.Decorations.Remove(glyphs);
        };
    }

    public IDebugSession? Session => _active?.Session;

    public bool IsPaused => _active is not null && _stop is not null;

    public StopInfo? CurrentStop => _stop;

    public StackFrameInfo? SelectedFrame => _selectedFrame;

    public WatchList Watches { get; } = new();

    public event EventHandler? SessionChanged;

    public event EventHandler? StateChanged;

    public event EventHandler<StopInfo>? Stopped;

    /// <summary>Opens Project Options (the <c>Project Options…</c> button of <c>Msg.Debug.NoDebugInfo</c>).</summary>
    public Func<Task>? OpenProjectOptions { get; set; }

    /// <summary>
    /// UI-TW-501 with <c>output.autoShowOnRun</c>: the Output window switches to the <c>program</c> channel when the
    /// program starts. Its tab is not activated: the bottom dock keeps the arrangement of the layout profile (in the
    /// Debug profile Breakpoints is the active tab, main-window.md §2).
    /// </summary>
    public Action? SelectProgramOutput { get; set; }

    /// <summary>Shows the Watches tool window without moving focus (UI-DLG-200).</summary>
    public Action? ShowWatches { get; set; }

    /// <summary>The field history of the Watch and Evaluate dialogs (UI-DLG-009); null = none.</summary>
    public Layout.ILayoutService? UiState { get; set; }

    /// <summary>The debug-session interface §6.1: the IDE state of a session state.</summary>
    public static IdeState Map(DebugState state) => state switch
    {
        DebugState.Starting or DebugState.Running or DebugState.Terminating => IdeState.DebugRunning,
        DebugState.Stopped => IdeState.Paused,
        _ => IdeState.Idle,
    };

    /// <summary>The debug-session interface §6.1: the Output channel and line kind of an output category.</summary>
    public static (string Channel, OutputKind Kind) Route(OutputCategory category) => category switch
    {
        OutputCategory.Stdout => (OutputChannels.Program, OutputKind.Normal),
        OutputCategory.Stderr => (OutputChannels.Program, OutputKind.Error),
        _ => (OutputChannels.Debugger, OutputKind.Normal),
    };

    /// <summary>
    /// <c>run.arguments</c> (one command-line tail, verbatim) as the argument list the session takes: split at unquoted
    /// white space; <c>"</c> quotes, <c>\"</c> is a quote, backslashes are literal unless they precede a quote.
    /// </summary>
    public static IReadOnlyList<string> SplitArguments(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        var result = new List<string>();
        var current = new StringBuilder();
        var inArgument = false;
        var quoted = false;
        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (c == '\\')
            {
                var run = 0;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    run++;
                    i++;
                }

                inArgument = true;
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', run / 2);
                    if (run % 2 == 1)
                        current.Append('"');
                    else
                        quoted = !quoted;
                }
                else
                {
                    current.Append('\\', run);
                    i--;
                }
            }
            else if (c == '"')
            {
                inArgument = true;
                quoted = !quoted;
            }
            else if (!quoted && c is ' ' or '\t')
            {
                if (inArgument)
                    result.Add(current.ToString());
                current.Clear();
                inArgument = false;
            }
            else
            {
                inArgument = true;
                current.Append(c);
            }
        }

        if (inArgument)
            result.Add(current.ToString());
        return result;
    }

    public async Task<bool> StartAsync(bool stopOnEntry = false)
    {
        if (_projects.Project is not { } project || _state.Current.State != IdeState.Idle || _active is not null)
            return false;
        if (project.AppType is not ("console" or "gui"))
        {
            _status.ShowMessage(Strings.Msg_Run_NotRunnable, StatusDuration);
            return false;
        }

        // IDE-P3-086: without debug information breakpoints cannot stop.
        if (_projects.Definition is { } definition && definition.Configurations.TryGetValue(project.ActiveConfiguration, out var configuration)
            && !configuration.DebugInfo)
        {
            var answer = await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Debug.NoDebugInfo", [project.ActiveConfiguration]));
            if (answer == "ProjectOptions")
                _ = OpenProjectOptions?.Invoke();
            if (answer != "RunAnyway")
                return false;
        }

        return await _run.BuildThenAsync(project, exe => LaunchAsync(project, exe, stopOnEntry));
    }

    public async Task StopAsync()
    {
        if (_active is not { } active)
            return;
        active.StoppedByUser = true;
        try
        {
            await active.Session.TerminateAsync();
        }
        catch (Exception e) when (e is DebuggerException or ObjectDisposedException)
        {
            _log.Warning(Category, "Terminating the debug session failed: " + e.Message);
            await FinishAsync(active, null);
        }

        await active.Ended.Task; // the Terminated event has been applied
    }

    public async Task<bool> RestartAsync()
    {
        var stopOnEntry = _active?.StopOnEntry ?? false;
        await StopAsync();
        return await StartAsync(stopOnEntry);
    }

    /// <summary>IDE-P3-093: while a session exists, asks <c>Msg.Run.StopOnExit</c> / <c>StopOnClose</c> and ends it; false = canceled.</summary>
    public async Task<bool> ConfirmStopAsync(bool exiting)
    {
        if (_active is not { } active)
            return true;
        var id = exiting ? "Msg.Run.StopOnExit" : "Msg.Run.StopOnClose";
        var answer = await _dialogs.ShowMessageAsync(new MessageRequest(id, [Path.GetFileName(active.ExePath)]));
        if (answer != (exiting ? "StopAndExit" : "StopAndClose"))
            return false;
        await StopAsync();
        return true;
    }

    /// <summary>
    /// Ends the session, if any, and completes once nothing of it is left: the adapter is gone, every event was applied and
    /// every breakpoint request answered. For the IDE's exit and for tests, which may not leave work behind.
    /// </summary>
    public async Task ShutdownAsync()
    {
        await StopAsync();
        await _breakpoints.WhenSyncedAsync();
    }

    /// <summary>
    /// dialogs.md §19 <c>Condition</c>: while paused the expression is evaluated once; the result is the evaluator's
    /// message, or null when it is valid or no session is paused (then it is not checked).
    /// </summary>
    public async Task<string?> ConditionProblemAsync(string condition)
    {
        if (!IsPaused)
            return null;
        try
        {
            await EvaluateAsync(condition, EvaluateContext.Watch);
            return null;
        }
        catch (DebuggerException e)
        {
            return e.Message;
        }
    }

    /// <summary>No session exists and no breakpoint request is under way.</summary>
    public bool IsIdle => _active is null && _breakpoints.WhenSyncedAsync().IsCompleted;

    public Task<EvaluateResult> EvaluateAsync(string expression, EvaluateContext context) =>
        _active is { } active && _stop is not null
            ? active.Session.EvaluateAsync(expression, _selectedFrame?.Id, context)
            : Task.FromException<EvaluateResult>(new DebuggerException(DebugErrorCode.InvalidState, Strings.Msg_Debug_NotPaused));

    public Task<VariableInfo> SetValueAsync(string evaluateName, string valueExpression) =>
        _active is { } active && _stop is not null
            ? active.Session.SetValueAsync(evaluateName, valueExpression, _selectedFrame?.Id)
            : Task.FromException<VariableInfo>(new DebuggerException(DebugErrorCode.InvalidState, Strings.Msg_Debug_NotPaused));

    public void SelectFrame(StackFrameInfo? frame)
    {
        var selected = frame is { Index: > 0 } && _stop is not null ? frame : null;
        if (_selectedFrame == selected)
            return;
        _selectedFrame = selected;
        RefreshDecorations();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        bool Project() => _projects.Project is not null;
        bool Idle() => _state.Current.State == IdeState.Idle;
        bool Paused() => _state.Current.State == IdeState.Paused && _active is not null;

        // menus.md §7; the state part of each condition is what a shortcut reports (UI-CMD-012).
        commands.RegisterHandler("run.start", new Handler(Project, () => true, StartCommandAsync)
        {
            Title = () => Paused() ? Strings.Button_Continue : null,
            MenuLabel = () => Paused() ? Strings.Menu_run_continue : null,
        });
        commands.RegisterHandler("run.stop", new Handler(() => _active is not null || _run.IsRunning, () => true,
            () => _active is not null ? StopAsync() : _run.StopAsync()));
        commands.RegisterHandler("run.restart", new Handler(() => _active is not null || _run.IsRunning, () => true,
            () => _active is not null ? RestartAsync() : _run.RestartAsync()));
        commands.RegisterHandler("run.pause", new Handler(() => _active is not null, () => _state.Current.State == IdeState.DebugRunning,
            () => ControlAsync(s => s.PauseAsync())));
        commands.RegisterHandler("run.stepOver", new Handler(() => Paused() || Project(), () => Paused() || Idle(),
            () => Paused() ? ControlAsync(s => s.StepOverAsync(_stop!.ThreadId)) : StartAsync(stopOnEntry: true)));
        commands.RegisterHandler("run.stepInto", new Handler(() => Paused() || Project(), () => Paused() || Idle(),
            () => Paused() ? ControlAsync(s => s.StepIntoAsync(_stop!.ThreadId)) : StartAsync(stopOnEntry: true)));
        commands.RegisterHandler("run.stepOut", new Handler(() => _active is not null, Paused,
            () => ControlAsync(s => s.StepOutAsync(_stop!.ThreadId))));
        commands.RegisterHandler("run.runToCursor", new Handler(() => _active is not null && EditorFile is not null, Paused, () =>
            EditorFile is { } editor ? ControlAsync(s => s.RunToLineAsync(editor.FilePath!, editor.ViewState.Caret.Line)) : Task.CompletedTask));
        commands.RegisterHandler("run.showExecutionPoint", new Handler(() => _active is not null, Paused, ShowExecutionPointAsync));
        commands.RegisterHandler("run.evaluate", new Handler(() => _active is not null, Paused, () =>
            _dialogs.ShowAsync(new EvaluateViewModel(this, UiState, ExpressionAtCaret()))));
        commands.RegisterHandler("run.addWatch", new Handler(() => _documents.ActiveDocument is TextDocumentViewModel, () => true, () =>
        {
            if (Watches.Add(ExpressionAtCaret()) >= 0) // UI-TW-1103: directly, without the dialog
                ShowWatches?.Invoke();
            return Task.CompletedTask;
        }));
        commands.RegisterHandler("watches.add", new Handler(() => true, () => true, async () =>
        {
            // UI-DLG-200: invoked from outside the Watches tool window.
            if (await _dialogs.ShowAsync(new WatchViewModel(UiState, ExpressionAtCaret(), edit: false)) is { } expression && Watches.Add(expression) >= 0)
                ShowWatches?.Invoke();
        }));
        StateChanged += (_, _) => commands.InvalidateCanExecute();
        SessionChanged += (_, _) => commands.InvalidateCanExecute();
    }

    /// <summary>The selection, else the word at the caret, of the active text document (dialogs.md §20, §21 default).</summary>
    public string ExpressionAtCaret()
    {
        if (_documents.ActiveDocument is not TextDocumentViewModel document)
            return "";
        var selection = document.Selection;
        var (start, end) = selection.IsEmpty ? EditorOperations.WordAt(document.Buffer, selection.Caret) : (selection.Start, selection.End);
        var text = document.Buffer.GetText(start, end - start);
        return text.Contains('\n', StringComparison.Ordinal) ? "" : text.Trim();
    }

    private TextDocumentViewModel? EditorFile => _documents.ActiveDocument as TextDocumentViewModel is { FilePath: not null } text ? text : null;

    /// <summary><c>run.start</c>: Continue while paused; the restart question while a program runs without the debugger; else start.</summary>
    private Task StartCommandAsync()
    {
        if (_state.Current.State == IdeState.Paused && _active is not null)
            return ControlAsync(s => s.ContinueAsync());
        return _run.IsRunning ? _run.StartCommandAsync(withDebugger: false) : StartAsync();
    }

    /// <summary>An execution-control request; a session that cannot take it now (it is ending) is not an error to report.</summary>
    private async Task ControlAsync(Func<IDebugSession, Task> request)
    {
        if (_active is not { } active)
            return;
        try
        {
            await request(active.Session);
        }
        catch (Exception e) when (e is DebuggerException or ObjectDisposedException)
        {
            _log.Warning(Category, e.Message);
            _status.ShowMessage(e.Message, StatusDuration);
        }
    }

    private async Task ShowExecutionPointAsync()
    {
        if (_stop?.Location is not { } location)
            return;
        if ((await _documents.OpenAsync(location.Path)).Value is TextDocumentViewModel document)
            _navigation.GoToLine(document, Math.Clamp(location.Line, 1, document.Buffer.LineCount), Math.Max(1, location.Column));
    }

    private async Task<bool> LaunchAsync(ProjectModel project, string exe, bool stopOnEntry)
    {
        if (await _run.ResolveWorkingDirAsync(project, exe) is not { } workingDir)
            return false;
        var run = _projects.Definition?.Run ?? new FreexComp.Build.RunSettings("", "");
        var searchPaths = _projects.Definition?.SearchPaths.Select(p => Path.GetFullPath(Path.Combine(project.Directory, p.Replace('/', '\\')))) ?? [];
        var options = new DebugSessionOptions
        {
            AdapterPath = Path.Combine(_toolchain.Resolve().LlvmBinDir ?? "", AdapterFileName),
            UserSourceRoots = [project.Directory, .. searchPaths],
        };
        var active = new Active(_factory.Create(options), exe, _clock.Now, stopOnEntry);
        var session = active.Session;
        _active = active;

        // DBG-420: events are raised serially on the session's thread; each is applied on the UI thread, in that order.
        session.StateChanged += (_, e) => _dispatcher.Post(() => OnStateChanged(active, e));
        session.Stopped += (_, e) => _dispatcher.Post(() => OnStopped(active, e));
        session.Continued += (_, _) => _dispatcher.Post(() => OnContinued(active));
        session.Output += (_, e) => _dispatcher.Post(() => OnOutput(active, e));
        session.Terminated += (_, e) => _dispatcher.Post(() => _ = ObserveAsync(FinishAsync(active, e)));
        var breakpoints = _breakpoints.AttachSession(session);

        if (_settings.Get(SettingsCatalog.OutputClearOnRun))
        {
            _output.Clear(OutputChannels.Program);
            _output.Clear(OutputChannels.Debugger);
        }

        if (!_state.TryTransition(new IdeStateInfo(IdeState.DebugRunning, null, false, exe, null)))
            _state.ForceIdle();
        SessionChanged?.Invoke(this, EventArgs.Empty);
        var breakOnExceptions = _settings.Get(SettingsCatalog.DebuggerBreakOnExceptions);
        try
        {
            await session.LaunchAsync(new LaunchRequest
            {
                Program = exe,
                Arguments = SplitArguments(run.Arguments),
                WorkingDirectory = workingDir,
                StopOnEntry = stopOnEntry,
                Breakpoints = breakpoints,
                Exceptions = new ExceptionSettings
                {
                    BreakOnLanguageExceptions = breakOnExceptions,
                    BreakOnOsExceptions = breakOnExceptions,
                    IgnoredClasses = _settings.Get(SettingsCatalog.DebuggerIgnoredExceptions),
                },
            });
        }
        catch (Exception e) when (e is DebuggerException or OperationCanceledException or ObjectDisposedException)
        {
            // DBG-402: the session is Terminated; Msg.Debug.StartFailed offers the run without the debugger.
            _log.Warning(Category, $"Cannot debug {exe}: {e.Message}");
            await FinishAsync(active, null);
            if (active.StoppedByUser)
                return false; // run.stop while the program was starting: reported as terminated
            if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Debug.StartFailed", [e.Message])) == "RunWithoutDebugging")
                return await _run.StartAsync();
            return false;
        }

        var started = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Run_Started, exe, run.Arguments, session.ProcessId ?? 0);
        _output.Append(OutputChannels.Program, started, OutputKind.Ide);
        _log.Info(Category, started);
        if (_settings.Get(SettingsCatalog.OutputAutoShowOnRun))
            SelectProgramOutput?.Invoke();
        return true;
    }

    private void OnStateChanged(Active active, StateChangedEventArgs e)
    {
        // Stopped and Terminated are applied by their own events, which carry the stop and the cause (DBG-421).
        if (_active != active || active.Finishing || Map(e.NewState) != IdeState.DebugRunning)
            return;
        if (_state.Current.State != IdeState.DebugRunning)
            _state.TryTransition(new IdeStateInfo(IdeState.DebugRunning, null, false, active.ExePath, active.Session.ProcessId));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnStopped(Active active, StopInfo stop)
    {
        if (_active != active || active.Finishing)
            return;
        _stop = stop;
        _selectedFrame = null;
        if (_state.Current.State != IdeState.Paused)
        {
            _state.TryTransition(new IdeStateInfo(IdeState.Paused, null, false, active.ExePath, active.Session.ProcessId)
            {
                PausedFile = stop.Location?.Path,
                PausedLine = stop.Location?.Line ?? 0,
            });
        }

        RefreshDecorations();
        _status.ShowMessage(stop.Description, StatusDuration);
        StateChanged?.Invoke(this, EventArgs.Empty);
        _ = ObserveAsync(NavigateThenRaiseAsync(active, stop));
    }

    /// <summary>Work started from a session event has no caller to report to: a failure goes to the log.</summary>
    private async Task ObserveAsync(Task work)
    {
        try
        {
            await work;
        }
        catch (Exception e)
        {
            _log.Error(Category, "Applying a debug session event failed: " + e.Message, e);
        }
    }

    private async Task NavigateThenRaiseAsync(Active active, StopInfo stop)
    {
        await ShowExecutionPointAsync();
        if (_active == active && _stop == stop)
            Stopped?.Invoke(this, stop);
    }

    private void OnContinued(Active active)
    {
        if (_active != active || active.Finishing)
            return;
        _stop = null;
        _selectedFrame = null;
        RefreshDecorations();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>DBG-424: text arrives in arbitrary chunks; complete lines go to the channel of their category.</summary>
    private void OnOutput(Active active, OutputEventArgs e)
    {
        if (!active.Partial.TryGetValue(e.Category, out var partial))
            active.Partial[e.Category] = partial = new StringBuilder();
        partial.Append(e.Text);
        var text = partial.ToString();
        var end = text.LastIndexOf('\n');
        if (end < 0)
            return;
        partial.Remove(0, end + 1);
        var (channel, kind) = Route(e.Category);
        _output.Append(channel, text[..end], kind);
    }

    /// <summary>
    /// The end of a session, once: what is left of its output, the report of <paramref name="terminated"/> (none after a
    /// failed launch, which reports itself), the session disposed, Idle. Later calls wait for the first.
    /// </summary>
    private async Task FinishAsync(Active active, TerminatedEventArgs? terminated)
    {
        if (active.Finishing)
        {
            await active.Ended.Task;
            return;
        }

        active.Finishing = true;
        foreach (var (category, partial) in active.Partial.Where(p => p.Value.Length > 0))
        {
            var (channel, kind) = Route(category);
            _output.Append(channel, partial.ToString(), kind);
        }

        string? crash = null;
        switch (terminated)
        {
            case { Cause: TerminationCause.Exited }:
                var seconds = (_clock.Now - active.StartedAt).TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture);
                Report(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Run_Exited, RunService.FormatExitCode(terminated.ExitCode ?? 0), seconds));
                break;
            case { Cause: TerminationCause.TerminatedByUser }:
                Report(Strings.Msg_Run_Terminated);
                break;
            case { Cause: TerminationCause.AdapterCrashed }:
                crash = terminated.Message ?? "";
                _log.Warning(Category, "The debug adapter ended unexpectedly: " + crash);
                break;
        }

        try
        {
            await active.Session.DisposeAsync(); // DBG-407: no event is raised afterwards
        }
        catch (Exception e) when (e is DebuggerException or ObjectDisposedException)
        {
            _log.Warning(Category, "Disposing the debug session failed: " + e.Message);
        }

        _breakpoints.DetachSession();
        if (_active == active)
        {
            _active = null;
            _stop = null;
            _selectedFrame = null;
            RefreshDecorations();
            if (_state.Current.IsDebugging && !_state.TryTransition(IdeStateInfo.Idle))
                _state.ForceIdle();
            SessionChanged?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        active.Ended.TrySetResult();
        if (crash is not null && await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Debug.StartFailed", [crash])) == "RunWithoutDebugging")
            await _run.StartAsync();
    }

    private void Report(string text)
    {
        _output.Append(OutputChannels.Program, text, OutputKind.Ide);
        _status.ShowMessage(text, StatusDuration);
        _log.Info(Category, text);
    }

    private void Decorate(TextDocumentViewModel document)
    {
        if (_decorated.ContainsKey(document))
            return;
        var glyphs = new ExecutionGlyphs(this, document);
        _decorated[document] = glyphs;
        document.Decorations.Add(glyphs);
    }

    private void RefreshDecorations()
    {
        foreach (var glyphs in _decorated.Values)
            glyphs.Raise();
    }

    /// <summary>One debug session with what the IDE keeps for it.</summary>
    private sealed class Active(IDebugSession session, string exePath, DateTimeOffset startedAt, bool stopOnEntry)
    {
        public IDebugSession Session { get; } = session;

        public string ExePath { get; } = exePath;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public bool StopOnEntry { get; } = stopOnEntry;

        /// <summary><c>run.stop</c> asked for the end of the session.</summary>
        public bool StoppedByUser { get; set; }

        /// <summary>The end of the session is being applied (or was).</summary>
        public bool Finishing { get; set; }

        /// <summary>Completes when the session is disposed and the IDE is Idle again.</summary>
        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The text after the last line break, per category.</summary>
        public Dictionary<OutputCategory, StringBuilder> Partial { get; } = [];
    }

    /// <summary>
    /// editor.md §3: the execution arrow on the line of the stop and the hollow arrow on the line of a selected call frame
    /// that is not the top frame, in one open document.
    /// </summary>
    private sealed class ExecutionGlyphs(DebugService owner, TextDocumentViewModel document) : IEditorDecorationProvider
    {
        public event EventHandler? Changed;

        public IReadOnlyList<LineGlyph> GetLineGlyphs(int line)
        {
            if (owner._stop is not { } stop || document.FilePath is not { } path)
                return [];
            var glyphs = new List<LineGlyph>(2);
            if (IsAt(stop.Location, path, line))
                glyphs.Add(new LineGlyph(LineGlyphKind.Execution));
            if (owner._selectedFrame is { } frame && IsAt(frame.Location, path, line))
                glyphs.Add(new LineGlyph(LineGlyphKind.Frame, ToolTip: frame.Name));
            return glyphs;
        }

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

        private static bool IsAt(SourceLocation? location, string path, int line) =>
            location is not null && location.Line == line && string.Equals(location.Path, path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A command of menus.md §7: <paramref name="available"/> is the part of its condition that is not the IDE state,
    /// <paramref name="stateAllows"/> the state part (a shortcut blocked only by it reports <c>Msg.Status.CommandUnavailable</c>).
    /// </summary>
    private sealed class Handler(Func<bool> available, Func<bool> stateAllows, Func<Task> execute) : ICommandHandler
    {
        public Func<string?>? Title { get; init; }

        public Func<string?>? MenuLabel { get; init; }

        public bool CanExecute(string? argument) => available() && stateAllows();

        public bool IsBlockedByIdeState(string? argument) => available() && !stateAllows();

        public Task ExecuteAsync(string? argument) => execute();

        public string? TitleOverride => Title?.Invoke();

        public string? MenuLabelOverride => MenuLabel?.Invoke();
    }
}
