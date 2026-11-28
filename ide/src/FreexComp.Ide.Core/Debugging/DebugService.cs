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

    /// <summary>
    /// IDE-LIB-020: the open project's "Step Into Library Source" choice (session value <c>debug.stepIntoLibrarySource</c>);
    /// read when a debug session starts (IDE-LIB-022). False without a project.
    /// </summary>
    bool StepIntoLibrarySource { get; set; }

    /// <summary><see cref="StepIntoLibrarySource"/> changed.</summary>
    event EventHandler? StepIntoLibrarySourceChanged;

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

    /// <summary>
    /// IDE-PM-192: <paramref name="projectPath"/> is an available listed member of the project group that can be
    /// started under the debugger (a console or gui program, or a library whose host application file exists), and the
    /// IDE is Idle. Synchronous (the state of a context-menu item).
    /// </summary>
    bool CanStartMember(string? projectPath);

    /// <summary>
    /// IDE-PM-199: <c>run.start</c> for a listed member of the project group — the sequence of <see cref="StartAsync"/>
    /// with the member's own values (its question about debug information, its build, a session for its output); the
    /// active project does not change. The path of the loaded project is <see cref="StartAsync"/>. False when no session
    /// was started.
    /// </summary>
    Task<bool> StartMemberAsync(string projectPath);

    /// <summary>Terminates the session; completes when the IDE is Idle again.</summary>
    Task StopAsync();

    /// <summary>
    /// IDE-P3-092, IDE-PM-197: ends the session, then starts again under the debugger for the run target the session
    /// was started for (the loaded project or a listed member).
    /// </summary>
    Task<bool> RestartAsync();

    /// <summary>Evaluates in the selected frame; the session must be paused.</summary>
    Task<EvaluateResult> EvaluateAsync(string expression, EvaluateContext context);

    /// <summary>Assigns in the selected frame; the session must be paused.</summary>
    Task<VariableInfo> SetValueAsync(string evaluateName, string valueExpression);

    /// <summary>DBG-429 through the session (Text Viewer, UI-DLG-323): the details of a text handle; the session must be paused.</summary>
    Task<TextDetails> GetTextDetailsAsync(VariableHandle text, CancellationToken ct = default);

    /// <summary>DBG-430 through the session (UI-DLG-324): one chunk of a text from <paramref name="start"/>; the session must be paused.</summary>
    Task<TextChunk> ReadTextAsync(VariableHandle text, long start, int count, CancellationToken ct = default);

    /// <summary>The debuggee's memory (the Text Viewer's hex rows of 1-byte texts, UI-DLG-326); the session must be paused.</summary>
    Task<byte[]> ReadMemoryAsync(ulong address, int count, CancellationToken ct = default);

    /// <summary>
    /// UI-TW-1008, UI-DLG-323: shows the Text Viewer for <paramref name="value"/> (its <c>Text</c> handle is set) titled
    /// with <paramref name="name"/>; completes when the dialog is closed.
    /// </summary>
    Task ViewTextAsync(string name, VariableInfo value);

    /// <summary>Makes <paramref name="frame"/> the selected frame (UI-TW-901); frame 0 or null returns to the execution point.</summary>
    void SelectFrame(StackFrameInfo? frame);

    /// <summary>The thread whose frames the views show (UI-TW-1201): the thread of the stop until another is switched to; null while not paused.</summary>
    int? CurrentThreadId { get; }

    /// <summary>What the views of a paused program depend on; null while not paused.</summary>
    DebugContext? Context { get; }

    /// <summary>The stop, the current thread or the selected frame changed (also to "not paused").</summary>
    event EventHandler? ContextChanged;

    /// <summary>
    /// The innermost frames of the current thread (at most <see cref="DebugService.FramePageSize"/>), asked once per stop
    /// and thread; null while not paused or when they cannot be read.
    /// </summary>
    Task<StackTraceResult?> GetFramesAsync();

    /// <summary>The next page of the current thread's frames from <paramref name="startFrame"/> (UI-TW-902); null when it cannot be read.</summary>
    Task<StackTraceResult?> GetMoreFramesAsync(int startFrame);

    /// <summary>The frame the views evaluate in: the selected frame, else the top frame of the current thread; null when unknown.</summary>
    Task<StackFrameInfo?> GetEvaluationFrameAsync();

    /// <summary>UI-TW-901: selects the frame and shows its line in the editor (frame 0: the execution point).</summary>
    Task GoToFrameAsync(StackFrameInfo frame);

    /// <summary>UI-TW-1201: makes the thread the current thread; Call Stack, Locals, Watches and the editor follow. Only while paused.</summary>
    Task SwitchThreadAsync(int threadId);

    /// <summary>A value of the paused program was assigned: every visible value is read again (DBG-413).</summary>
    event EventHandler? ValuesChanged;

    /// <summary>A thread of the debuggee started or exited (UI-TW-1200).</summary>
    event EventHandler<ThreadChangedEventArgs>? ThreadChanged;

    /// <summary>The Event Log's rows since the last session start (UI-TW-1300), oldest first.</summary>
    IReadOnlyList<DebugLogEntry> Log { get; }

    /// <summary>A row was appended to <see cref="Log"/> (the oldest row may have been dropped for it), or (null) the log was cleared.</summary>
    event EventHandler<DebugLogEntry?>? LogChanged;

    void ClearLog();

    /// <summary>
    /// Registers work a debug view started against the session (reading frames, values): execution control and the end of
    /// the session wait for it, so a request is never abandoned half-way. Returns <paramref name="work"/>.
    /// </summary>
    Task Track(Task work);
}

/// <inheritdoc cref="IDebugService"/>
public sealed partial class DebugService : IDebugService
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
    private readonly HashSet<Task> _work = [];
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
        settings.Changed += OnSettingChanged;
    }

    public IDebugSession? Session => _active?.Session;

    public bool IsPaused => _active is not null && _stop is not null;

    public StopInfo? CurrentStop => _stop;

    public StackFrameInfo? SelectedFrame => _selectedFrame;

    public WatchList Watches { get; } = new();

    /// <summary>IDE-LIB-022: the library roots a session started with <see cref="StepIntoLibrarySource"/> treats as user code.</summary>
    public ILibrarySourceService? LibrarySource { get; init; }

    public bool StepIntoLibrarySource
    {
        get => _stepIntoLibrarySource;
        set
        {
            if (_stepIntoLibrarySource == value)
                return;
            _stepIntoLibrarySource = value;
            StepIntoLibrarySourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _stepIntoLibrarySource;

    public event EventHandler? StepIntoLibrarySourceChanged;

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

    /// <summary>The clipboard of the Text Viewer's Copy (UI-DLG-327); null = Copy does nothing.</summary>
    public Platform.IClipboardService? Clipboard { get; set; }

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

    public Task<bool> StartAsync(bool stopOnEntry = false) => StartAsync(stopOnEntry, null);

    /// <summary>
    /// As <see cref="StartAsync(bool)"/>; <paramref name="runTo"/> is "Run to Cursor" without a session (DBG-232): the
    /// program starts with a temporary breakpoint on that line, which goes at the first stop.
    /// </summary>
    public Task<bool> StartAsync(bool stopOnEntry, (string Path, int Line)? runTo) => StartAsync(stopOnEntry, runTo, stopAtMainBody: false);

    /// <summary>
    /// DBG-225: Step Over / Step Into as the first command — the program starts and stops on the first statement of its
    /// main block (<c>LaunchRequest.StopAtMainBody</c>; at its entry when the program has no type information).
    /// </summary>
    public Task<bool> StartAtMainBodyAsync() => StartAsync(stopOnEntry: false, null, stopAtMainBody: true);

    private async Task<bool> StartAsync(bool stopOnEntry, (string Path, int Line)? runTo, bool stopAtMainBody) =>
        _run.ActiveTarget() is { } target && await StartAsync(target, stopOnEntry, runTo, stopAtMainBody);

    /// <summary>IDE-TGT-014: <paramref name="target"/> can be started under the debugger — a program, or a library whose host application is a file.</summary>
    private bool IsDebuggable(RunTarget target) => target.AppType is "console" or "gui" || _run.ExistingHost(target) is not null;

    public bool CanStartMember(string? projectPath) =>
        _state.Current.State == IdeState.Idle && _active is null && _run.IsListedTarget(projectPath)
        && _run.MemberTarget(projectPath) is { } target && IsDebuggable(target);

    public async Task<bool> StartMemberAsync(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        if (!_run.IsListedTarget(projectPath))
            return await StartAsync();
        // IDE-PM-192: a path that is not an available member does nothing.
        return _run.MemberTarget(projectPath) is { } target && await StartAsync(target, stopOnEntry: false, null, stopAtMainBody: false);
    }

    /// <summary>
    /// IDE-P3-086, IDE-PM-199: the start under the debugger for <paramref name="target"/>, with the target's own values —
    /// the loaded project or a listed member of the project group.
    /// </summary>
    private async Task<bool> StartAsync(RunTarget target, bool stopOnEntry, (string Path, int Line)? runTo, bool stopAtMainBody)
    {
        if (_state.Current.State != IdeState.Idle || _active is not null)
            return false;
        // IDE-TGT-014: a library runs inside its host application; that program is what the session starts.
        if (!IsDebuggable(target))
        {
            _status.ShowMessage(Strings.Msg_Run_NotRunnable, StatusDuration);
            return false;
        }

        // IDE-P3-086: without debug information breakpoints cannot stop.
        if (target.Definition is { } definition && ConfigurationEdits.Effective(definition, target.Configuration) is { DebugInfo: false })
        {
            // IDE-PM-199 (2): the question of a listed member has no Project Options button — that dialog is the active project's.
            var answer = await _dialogs.ShowMessageAsync(target.IsListedMember
                ? new MessageRequest("Msg.Debug.MemberNoDebugInfo", [target.Name, target.Configuration])
                : new MessageRequest("Msg.Debug.NoDebugInfo", [target.Configuration]));
            if (answer == "ProjectOptions" && !target.IsListedMember)
                _ = OpenProjectOptions?.Invoke();
            if (answer != "RunAnyway")
                return false;
        }

        // IDE-PM-194: the target as it is after the build (the run parameters the build saved).
        return await _run.BuildThenAsync(target, async (built, output) =>
        {
            if (built.AppType is "console" or "gui")
                return await LaunchAsync(built, output, [], stopOnEntry, runTo, stopAtMainBody);
            if (_run.ExistingHost(built) is { } host)
                return await LaunchAsync(built, host, [output], stopOnEntry, runTo, stopAtMainBody);
            _status.ShowMessage(Strings.Msg_Run_NotRunnable, StatusDuration); // the project file saved by the build names no host that exists
            return false;
        });
    }

    /// <summary>
    /// <c>Run Without Debugging</c> of <c>Msg.Debug.StartFailed</c>: the run target the session was for (IDE-PM-199 (5)) —
    /// <paramref name="member"/> is the project file of a listed member, null the loaded project.
    /// </summary>
    private Task<bool> RunWithoutDebuggingAsync(string? member) => member is null ? _run.StartAsync() : _run.StartMemberAsync(member);

    public async Task StopAsync()
    {
        if (_active is not { } active)
            return;
        active.StoppedByUser = true;
        await WhenViewsSettledAsync(); // the views' requests are answered first
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
        var (stopOnEntry, stopAtMainBody, member) = (_active?.StopOnEntry ?? false, _active?.StopAtMainBody ?? false, _active?.Member);
        await StopAsync();
        // IDE-PM-197: the run target the session was started for, not the active project.
        if (member is null || !_run.IsListedTarget(member))
            return await StartAsync(stopOnEntry, null, stopAtMainBody);
        if (_run.MemberTarget(member) is { } target)
            return await StartAsync(target, stopOnEntry, null, stopAtMainBody);
        _run.ShowMemberUnavailable(member);
        return false;
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
        await WhenViewsSettledAsync();
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
        catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
        {
            return null; // the session is gone: the condition cannot be checked now
        }
    }

    /// <summary>No session exists and no breakpoint request is under way.</summary>
    public bool IsIdle => _active is null && _breakpoints.WhenSyncedAsync().IsCompleted && _work.Count == 0;

    public Task<EvaluateResult> EvaluateAsync(string expression, EvaluateContext context) =>
        _active is { } active && _stop is not null
            ? active.Session.EvaluateAsync(expression, FrameId, context)
            : Task.FromException<EvaluateResult>(new DebuggerException(DebugErrorCode.InvalidState, Strings.Msg_Debug_NotPaused));

    public Task<TextDetails> GetTextDetailsAsync(VariableHandle text, CancellationToken ct = default) =>
        _active is { } active && _stop is not null
            ? active.Session.GetTextDetailsAsync(text, ct)
            : Task.FromException<TextDetails>(new DebuggerException(DebugErrorCode.InvalidState, Strings.Msg_Debug_NotPaused));

    public Task<TextChunk> ReadTextAsync(VariableHandle text, long start, int count, CancellationToken ct = default) =>
        _active is { } active && _stop is not null
            ? active.Session.ReadTextAsync(text, start, count, ct)
            : Task.FromException<TextChunk>(new DebuggerException(DebugErrorCode.InvalidState, Strings.Msg_Debug_NotPaused));

    public Task<byte[]> ReadMemoryAsync(ulong address, int count, CancellationToken ct = default) =>
        _active is { } active && _stop is not null
            ? active.Session.ReadMemoryAsync(address, count, ct)
            : Task.FromException<byte[]>(new DebuggerException(DebugErrorCode.InvalidState, Strings.Msg_Debug_NotPaused));

    public async Task ViewTextAsync(string name, VariableInfo value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Text is null || !IsPaused)
            return;
        await _dialogs.ShowAsync(new TextViewerViewModel(this, Clipboard, UiState, name, value));
    }

    public async Task<VariableInfo> SetValueAsync(string evaluateName, string valueExpression)
    {
        if (_active is not { } active || _stop is null)
            throw new DebuggerException(DebugErrorCode.InvalidState, Strings.Msg_Debug_NotPaused);
        var value = await active.Session.SetValueAsync(evaluateName, valueExpression, FrameId);
        if (_active == active)
            ValuesChanged?.Invoke(this, EventArgs.Empty); // DBG-413: other nodes may alias the same memory
        return value;
    }

    public void SelectFrame(StackFrameInfo? frame)
    {
        var selected = frame is { Index: > 0 } && _stop is not null ? frame : null;
        if (_selectedFrame == selected)
            return;
        _selectedFrame = selected;
        RefreshDecorations();
        StateChanged?.Invoke(this, EventArgs.Empty);
        ContextChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        // IDE-TGT-014: a library is debugged through its host application, when it names one that exists; without it
        // (and for a package) nothing can be started under the debugger; breakpoints stay.
        bool Debuggable() => _projects.Project is { AppType: "console" or "gui" } || _run.ExistingHostApplication is not null;
        bool Idle() => _state.Current.State == IdeState.Idle;
        bool Paused() => _state.Current.State == IdeState.Paused && _active is not null;

        // menus.md §7; the state part of each condition is what a shortcut reports (UI-CMD-012).
        // IDE-PM-190, 192: without an argument the loaded project (menu, tool bar, shortcut; Continue while paused); the
        // context menu of a listed root gives the member's path — that start needs the Idle state.
        commands.RegisterHandler("run.start", new Handler(() => _active is not null || Debuggable(), () => true, StartCommandAsync)
        {
            Title = () => Paused() ? Strings.Button_Continue : null,
            MenuLabel = () => Paused() ? Strings.Menu_run_continue : null,
            IsMember = _run.IsListedTarget,
            CanStartMember = CanStartMember,
            StartMember = StartMemberAsync,
        });
        commands.RegisterHandler("run.stop", new Handler(() => _active is not null || _run.IsRunning, () => true,
            () => _active is not null ? StopAsync() : _run.StopAsync()));
        commands.RegisterHandler("run.restart", new Handler(() => _active is not null || _run.IsRunning, () => true,
            () => _active is not null ? RestartAsync() : _run.RestartAsync()));
        commands.RegisterHandler("run.pause", new Handler(() => _active is not null, () => _state.Current.State == IdeState.DebugRunning,
            () => ControlAsync(s => s.PauseAsync())));
        commands.RegisterHandler("run.stepOver", new Handler(() => Paused() || Debuggable(), () => Paused() || Idle(),
            () => Paused() ? ControlAsync(s => s.StepOverAsync(StepThread)) : StartAtMainBodyAsync()));
        commands.RegisterHandler("run.stepInto", new Handler(() => Paused() || Debuggable(), () => Paused() || Idle(),
            () => Paused() ? ControlAsync(s => s.StepIntoAsync(StepThread)) : StartAtMainBodyAsync()));
        commands.RegisterHandler("run.stepOut", new Handler(() => _active is not null, Paused,
            () => ControlAsync(s => s.StepOutAsync(StepThread))));
        // menus.md §7: text and (paused or (project and runnable and idle)); idle starts with a temporary breakpoint (DBG-232).
        commands.RegisterHandler("run.runToCursor", new Handler(() => EditorFile is not null && (_active is not null || Debuggable()), () => Paused() || Idle(), () =>
            EditorFile is not { } editor ? Task.CompletedTask
                : Paused() ? ControlAsync(s => s.RunToLineAsync(editor.FilePath!, editor.ViewState.Caret.Line))
                : StartAsync(stopOnEntry: false, (editor.FilePath!, editor.ViewState.Caret.Line))));
        // IDE-LIB-020 / IDE-LIB-021: a project and no debug session; the value is read when a session starts.
        commands.RegisterHandler("run.toggleLibraryStepping", new Handler(() => _projects.Project is not null, () => _active is null, () =>
        {
            StepIntoLibrarySource = !StepIntoLibrarySource;
            return Task.CompletedTask;
        })
        {
            Checked = () => StepIntoLibrarySource,
        });
        StepIntoLibrarySourceChanged += (_, _) => commands.InvalidateCanExecute();
        commands.RegisterHandler("run.showExecutionPoint", new Handler(() => _active is not null, Paused, ShowExecutionPointAsync));
        // tool-windows.md §12, §13: from a Locals or Watches row the commands take the row's expression.
        commands.RegisterHandler("run.evaluate", new Handler(() => _active is not null, Paused, () =>
            _dialogs.ShowAsync(new EvaluateViewModel(this, UiState, ToolExpression?.Invoke() ?? ExpressionAtCaret()))));
        commands.RegisterHandler("run.addWatch", new Handler(() => ToolExpression?.Invoke() is not null || _documents.ActiveDocument?.VisibleText is not null, () => true, () =>
        {
            if (Watches.Add(ToolExpression?.Invoke() ?? ExpressionAtCaret()) >= 0) // UI-TW-1103: directly, without the dialog
                ShowWatches?.Invoke();
            return Task.CompletedTask;
        }));
        commands.RegisterHandler("watches.add", new Handler(() => true, () => true, async () =>
        {
            if (AddWatchInPlace?.Invoke() == true)
                return; // UI-TW-1103: inside the Watches tool window the new row is edited in place
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
        if (_documents.ActiveDocument?.VisibleText is not { } document) // I-302: the editor of the visible view
            return "";
        var selection = document.Selection;
        var (start, end) = selection.IsEmpty ? EditorOperations.WordAt(document.Buffer, selection.Caret) : (selection.Start, selection.End);
        var text = document.Buffer.GetText(start, end - start);
        return text.Contains('\n', StringComparison.Ordinal) ? "" : text.Trim();
    }

    /// <summary>DBG-255: the step commands name the current thread — the thread of the stop until another was switched to.</summary>
    private int StepThread => _threadId ?? _stop!.ThreadId;

    /// <summary>
    /// <c>run.runToCursor</c>: the editor of the active document's visible view when it can hold breakpoints, a saved
    /// Object Pascal text (I-302: not a form's Text view, nothing in its Design view).
    /// </summary>
    private TextDocumentViewModel? EditorFile =>
        _documents.ActiveDocument?.VisibleText is { FilePath: not null } text && text.Language.Id == "pascal" ? text : null;

    /// <summary><c>run.start</c>: Continue while paused; the restart question while a program runs without the debugger; else start.</summary>
    private Task StartCommandAsync()
    {
        if (_state.Current.State == IdeState.Paused && _active is not null)
            return ControlAsync(s => s.ContinueAsync());
        return _run.IsRunning ? _run.StartCommandAsync(withDebugger: false) : StartAsync();
    }

    /// <summary>
    /// An execution-control request; a session that cannot take it now (it is ending) is not an error to report.
    /// DBG-224: one request at a time — while one is not yet answered (a key that repeats), and after it was answered for
    /// the stop it resumed (the session runs already, the IDE state follows with the session's next event), further
    /// requests are ignored: the commands are meant to be disabled then.
    /// </summary>
    private async Task ControlAsync(Func<IDebugSession, Task> request)
    {
        if (_active is not { } active || active.Controlling)
            return;
        var stop = _stop;
        if (stop is not null && ReferenceEquals(stop, active.ResumedStop))
            return;
        active.Controlling = true;
        try
        {
            await WhenViewsSettledAsync(); // the views' requests of this stop are answered first
            if (_active != active)
                return;
            await request(active.Session);
            active.ResumedStop = stop;
        }
        catch (DebuggerException e) when (e.Code == DebugErrorCode.InvalidState)
        {
            // The session changed state between the command's check and the request (it stopped, or ended, meanwhile):
            // the IDE state follows with the event that is on its way; nothing for the user to act on.
            _log.Info(Category, "An execution-control request came too late: " + e.Message);
        }
        catch (Exception e) when (e is DebuggerException or ObjectDisposedException)
        {
            _log.Warning(Category, e.Message);
            _status.ShowMessage(e.Message, StatusDuration);
        }
        finally
        {
            active.Controlling = false;
        }
    }

    private async Task ShowExecutionPointAsync()
    {
        if (_stop?.Location is not { } location)
            return;
        if ((await _documents.OpenAsync(location.Path)).Value is TextDocumentViewModel document)
            _navigation.GoToLine(document, Math.Clamp(location.Line, 1, document.Buffer.LineCount), Math.Max(1, location.Column));
    }

    /// <summary>
    /// Starts <paramref name="exe"/> under the debugger. <paramref name="libraries"/>: the library the project builds when
    /// <paramref name="exe"/> is its host application (<c>LaunchRequest.Libraries</c>: its type information is loaded and
    /// breakpoints in its units bind when the host loads it); empty for a program project.
    /// </summary>
    private async Task<bool> LaunchAsync(RunTarget target, string exe, IReadOnlyList<string> libraries, bool stopOnEntry, (string Path, int Line)? runTo,
        bool stopAtMainBody)
    {
        // IDE-PM-199 (4): every value is the run target's own — the loaded project's or the listed member's.
        if (await _run.ResolveWorkingDirAsync(target, exe) is not { } workingDir)
            return false;
        var run = target.Run;
        // The effective search paths of the target's active configuration (CMP-1160), as the build uses them.
        var searchPaths = (target.Definition is { } definition ? ConfigurationEdits.Effective(definition, target.Configuration)?.SearchPaths : null)?.Select(p => Path.GetFullPath(Path.Combine(target.Directory, p.Replace('/', '\\')))) ?? [];
        var options = new DebugSessionOptions
        {
            AdapterPath = Path.Combine(_toolchain.Resolve().LlvmBinDir ?? "", AdapterFileName),
            // IDE-LIB-022: with the library-stepping choice the library roots count as user code too.
            UserSourceRoots = [.. ((string[])[target.Directory, .. searchPaths, .. StepIntoLibrarySource && LibrarySource is { } library ? library.Roots : []])
                .Distinct(StringComparer.OrdinalIgnoreCase)],
        };
        var active = new Active(_factory.Create(options), exe, _clock.Now, stopOnEntry)
        {
            StopAtMainBody = stopAtMainBody,
            Member = target.IsListedMember ? target.ProjectPath : null,
        };
        var session = active.Session;
        _active = active;

        // DBG-420: events are raised serially on the session's thread; each is applied on the UI thread, in that order.
        session.StateChanged += (_, e) => _dispatcher.Post(() => OnStateChanged(active, e));
        session.Stopped += (_, e) => _dispatcher.Post(() => OnStopped(active, e));
        session.Continued += (_, _) => _dispatcher.Post(() => OnContinued(active));
        session.Output += (_, e) => _dispatcher.Post(() => OnOutput(active, e));
        session.Terminated += (_, e) => _dispatcher.Post(() => _ = ObserveAsync(FinishAsync(active, e)));
        session.ModuleLoaded += (_, e) => _dispatcher.Post(() => OnModuleLoaded(active, e));
        session.ThreadChanged += (_, e) => _dispatcher.Post(() => OnThreadChanged(active, e));
        var breakpoints = _breakpoints.AttachSession(session, runTo);
        ClearLog(); // UI-TW-1300: cleared when a new session starts

        if (_settings.Get(SettingsCatalog.OutputClearOnRun))
        {
            _output.Clear(OutputChannels.Program);
            _output.Clear(OutputChannels.Debugger);
        }

        if (!_state.TryTransition(new IdeStateInfo(IdeState.DebugRunning, null, false, exe, null)))
            _state.ForceIdle();
        SessionChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await session.LaunchAsync(new LaunchRequest
            {
                Program = exe,
                Arguments = SplitArguments(run.Arguments),
                WorkingDirectory = workingDir,
                StopOnEntry = stopOnEntry,
                StopAtMainBody = stopAtMainBody,
                Breakpoints = breakpoints,
                Exceptions = ExceptionSettingsNow(),
                Libraries = libraries,
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
                return await RunWithoutDebuggingAsync(active.Member);
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
        // The program runs again: the stop goes with the state, so "Debugging" never shows together with a stop.
        var left = LeaveStop();
        if (_state.Current.State != IdeState.DebugRunning)
            _state.TryTransition(new IdeStateInfo(IdeState.DebugRunning, null, false, active.ExePath, active.Session.ProcessId));
        if (e.NewState == DebugState.Running && !active.StartLogged)
        {
            // The debug-session interface §6.1: kind Process at the first Running (the process id is known then).
            active.StartLogged = true;
            AppendLog(DebugLogKind.Process, string.Format(CultureInfo.CurrentCulture, Strings.DebugLog_ProcessStarted,
                Path.GetFileName(active.ExePath), active.Session.ProcessId ?? 0));
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        if (left)
            ContextChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The program is no longer stopped: the stop, the selected frame and the execution line go. False when it was not stopped.</summary>
    private bool LeaveStop()
    {
        if (_stop is null)
            return false;
        _stop = null;
        ResetContext(null);
        RefreshDecorations();
        return true;
    }

    private void OnStopped(Active active, StopInfo stop)
    {
        if (_active != active || active.Finishing)
            return;
        _stop = stop;
        ResetContext(stop.ThreadId);
        _breakpoints.ClearTemporary(); // a "Run to Cursor" start ends at the first stop, whatever stopped
        LogStop(stop);
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
        ContextChanged?.Invoke(this, EventArgs.Empty);
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
        if (_active != active || _stop != stop)
            return;
        Stopped?.Invoke(this, stop);
        if (stop is { Reason: StopReason.Exception, Exception: { } exception })
            await NotifyExceptionAsync(active, stop, exception); // UI-DLG-230: the editor shows the line before the dialog
    }

    private void OnContinued(Active active)
    {
        if (_active != active || active.Finishing || !LeaveStop())
            return; // already applied with the state change that precedes this event (DBG-421)
        StateChanged?.Invoke(this, EventArgs.Empty);
        ContextChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>DBG-424: text arrives in arbitrary chunks; complete lines go to the channel of their category.</summary>
    private void OnOutput(Active active, OutputEventArgs e)
    {
        if (e.Category is OutputCategory.LogPoint or OutputCategory.Debugger)
        {
            // The debug-session interface §6.1: log points are Breakpoint rows, the debugger's own messages Debugger rows —
            // each event is a message of its own, whether or not it ends with a line break.
            foreach (var line in e.Text.Split('\n'))
            {
                if (line.TrimEnd('\r') is { Length: > 0 } row)
                    AppendLog(e.Category == OutputCategory.LogPoint ? DebugLogKind.Breakpoint : DebugLogKind.Debugger, row);
            }
        }

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
                AppendLog(DebugLogKind.Process, string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_ProcessExited,
                    active.Session.ProcessId ?? 0, RunService.FormatExitCode(terminated.ExitCode ?? 0)));
                break;
            case { Cause: TerminationCause.TerminatedByUser }:
                Report(Strings.Msg_Run_Terminated);
                AppendLog(DebugLogKind.Process, Strings.Msg_Run_Terminated);
                break;
            case { Cause: TerminationCause.AdapterCrashed }:
                crash = terminated.Message ?? "";
                _log.Warning(Category, "The debug adapter ended unexpectedly: " + crash);
                AppendLog(DebugLogKind.Debugger, crash);
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
            ResetContext(null);
            RemoveSourceChangedBars();
            RefreshDecorations();
            if (_state.Current.IsDebugging && !_state.TryTransition(IdeStateInfo.Idle))
                _state.ForceIdle();
            SessionChanged?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
            ContextChanged?.Invoke(this, EventArgs.Empty);
        }

        active.Ended.TrySetResult();
        if (crash is not null && await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Debug.StartFailed", [crash])) == "RunWithoutDebugging")
            await RunWithoutDebuggingAsync(active.Member);
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
        document.Buffer.Changed += (_, _) => OnSourceEdited(document);
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

        /// <summary>The session was started by a step (DBG-225); a restart starts the same way.</summary>
        public bool StopAtMainBody { get; init; }

        /// <summary>
        /// IDE-PM-197: the project file of the listed member of the project group the session was started for; null = the
        /// loaded project. A restart, and the run without the debugger after a failed start, are for the same target.
        /// </summary>
        public string? Member { get; init; }

        /// <summary><c>run.stop</c> asked for the end of the session.</summary>
        public bool StoppedByUser { get; set; }

        /// <summary>The end of the session is being applied (or was).</summary>
        public bool Finishing { get; set; }

        /// <summary>Completes when the session is disposed and the IDE is Idle again.</summary>
        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The Event Log has the row of the program's start.</summary>
        public bool StartLogged { get; set; }

        /// <summary>An execution-control request is under way (DBG-224).</summary>
        public bool Controlling { get; set; }

        /// <summary>The stop the last answered execution-control request resumed from; requests for it are over.</summary>
        public StopInfo? ResumedStop { get; set; }

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
            if ((owner._selectedFrame ?? owner._threadFrame) is { } frame && IsAt(frame.Location, path, line))
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

        /// <summary>The check mark of a toggle command; null = never checked.</summary>
        public Func<bool>? Checked { get; init; }

        /// <summary>
        /// IDE-PM-190: the argument names a listed member of the project group as the command's target (<c>run.start</c>
        /// from the context menu of its root); null = the command takes no target.
        /// </summary>
        public Func<string?, bool>? IsMember { get; init; }

        /// <summary>IDE-PM-192: the command can run for that member now.</summary>
        public Func<string?, bool>? CanStartMember { get; init; }

        /// <summary>IDE-PM-199: runs the command for that member.</summary>
        public Func<string, Task>? StartMember { get; init; }

        private bool ForMember(string? argument) => IsMember?.Invoke(argument) ?? false;

        public bool IsChecked(string? argument) => Checked?.Invoke() ?? false;

        public bool CanExecute(string? argument) => ForMember(argument) ? CanStartMember!(argument) : available() && stateAllows();

        public bool IsBlockedByIdeState(string? argument) => !ForMember(argument) && available() && !stateAllows();

        public Task ExecuteAsync(string? argument) => ForMember(argument) ? StartMember!(argument!) : execute();

        public string? TitleOverride => Title?.Invoke();

        public string? MenuLabelOverride => MenuLabel?.Invoke();
    }
}
