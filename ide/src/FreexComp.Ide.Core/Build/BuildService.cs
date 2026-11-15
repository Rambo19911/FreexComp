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

    public BuildService(IBuildEngine engine, IProjectService projects, IDocumentService documents, ISettingsService settings,
        IdeStateMachine state, IToolchainLocator toolchain, IOutputService output, MessagesService messages, IClock clock,
        IUiDispatcher dispatcher, IDialogService dialogs, IStatusService status, ILog log)
    {
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

    public async Task<BuildResult?> BuildAsync(BuildKind kind, bool continueToRun = false)
    {
        if (kind == BuildKind.CheckSyntax)
            throw new ArgumentException("Check Syntax does not use the build engine (IDE-P3-055).", nameof(kind));
        if (_projects.Project is not { } project || _state.Current.State != IdeState.Idle)
            return null;

        // IDE-P3-012: the toolchain first; nothing is saved when it is missing.
        var toolchain = _toolchain.CheckForBuild(needsLlvm: true);
        if (!toolchain.Success)
        {
            if (await _dialogs.ShowMessageAsync(new MessageRequest(toolchain.MessageId!, [.. toolchain.Args])) == "OpenOptions")
                _ = OpenToolchainOptions?.Invoke();
            return null;
        }

        if (kind != BuildKind.Clean && !await SaveBeforeBuildAsync())
            return null;
        if (_projects.Project is null || _state.Current.State != IdeState.Idle)
            return null;

        if (!_state.TryTransition(new IdeStateInfo(IdeState.Building, kind, false, null, null)))
            return null;
        BuildResult? result = null;
        try
        {
            result = await RunEngineAsync(project, kind);
            return result;
        }
        finally
        {
            // IDE-P3-037: Idle in every case, except a finished build that a run continues from (IDE-P3-002).
            if (_state.Current.State == IdeState.Building && (!continueToRun || result is null || result.Canceled))
                _state.TryTransition(IdeStateInfo.Idle);
        }
    }

    /// <summary>
    /// <c>project.build</c>, <c>project.rebuild</c>, <c>project.clean</c> (need a project) and <c>project.cancelBuild</c>;
    /// the IDE state decides the rest (IDE-P3-003).
    /// </summary>
    public void RegisterCommands(Commands.ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("project.build", new Handler(() => _projects.Project is not null, () => BuildAsync(BuildKind.Build)));
        commands.RegisterHandler("project.rebuild", new Handler(() => _projects.Project is not null, () => BuildAsync(BuildKind.Rebuild)));
        commands.RegisterHandler("project.clean", new Handler(() => _projects.Project is not null, () => BuildAsync(BuildKind.Clean)));
        commands.RegisterHandler("project.checkSyntax", new Handler(() => _projects.Project is not null && CheckSyntax is not null, CheckSyntaxAsync));
        commands.RegisterHandler("project.cancelBuild", new Handler(() => _cts is not null && !_state.Current.CancelRequested, () =>
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
        }
    }

    /// <summary>Opens Options › Toolchain Paths (the <c>Open Options</c> button of <c>Msg.Build.ToolchainMissing</c>).</summary>
    public Func<Task>? OpenToolchainOptions { get; set; }

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
    public async Task<bool> SaveBeforeBuildAsync()
    {
        var mode = _settings.Get(SettingsCatalog.EnvironmentAutoSaveBeforeBuild);
        if (mode == "none")
            return true;
        var set = _documents.Documents
            .Where(d => d.IsModified && d.FilePath is { } path && _projects.IsMember(path))
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

    private async Task<BuildResult?> RunEngineAsync(ProjectModel project, BuildKind kind)
    {
        if (_settings.Get(SettingsCatalog.OutputClearOnBuild))
            _output.Clear(OutputChannels.Build);
        _messages.BeginBuild();
        _internalError = false;
        ShowBuildOutput?.Invoke();
        if (_projects.Definition is { Events: var events } && (events.PreBuild.Length > 0 || events.PostBuild.Length > 0))
            _output.Append(OutputChannels.Build, Strings.Msg_Project_EventsIgnored, OutputKind.Ide); // IDE-P3-036

        var paths = _toolchain.Resolve();
        var request = new BuildRequest
        {
            ProjectPath = project.FilePath,
            Configuration = project.ActiveConfiguration,
            Platform = project.Platforms.Count > 0 ? project.Platforms[0] : null,
            Target = kind switch
            {
                BuildKind.Rebuild => BuildTarget.Rebuild,
                BuildKind.Clean => BuildTarget.Clean,
                _ => BuildTarget.Build,
            },
            Verbose = _settings.Get(SettingsCatalog.BuildShowCommandLines),
            ToolchainDirectory = paths.LlvmBinDir,
            RtlDirectory = paths.RtlDir,
            ExtraSearchPaths = paths.FrameworkDir is { } framework ? [Path.Combine(framework, "src")] : [],
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

        Finish(_internalError && !result!.Success ? Strings.Msg_Build_InternalError : Summary(result!, kind, project.Name),
            success: result!.Success);
        if (result.Errors > 0 && !result.Canceled && _settings.Get(SettingsCatalog.MessagesAutoShowOnError))
            ShowMessages?.Invoke();
        return result;
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
                    break;
                case BuildEvent.Diagnostic { Value: var d }:
                    _output.Append(OutputChannels.Build, d.ToText(), d.Severity == DiagnosticSeverity.Error ? OutputKind.Error : OutputKind.Normal);
                    if (d.Code != "E9010") // IDE-P3-034: the cancellation diagnostic is not a row
                        diagnostics.Add(d);
                    _internalError |= d.Code == "E0001";
                    break;
                case BuildEvent.CommandLine c:
                    _output.Append(OutputChannels.Build, Format(Strings.Msg_Build_CommandLine, c.Text), OutputKind.Command);
                    break;
                case BuildEvent.OutputLine o:
                    _output.Append(OutputChannels.Build, o.Text, o.Source == OutputSource.Info ? OutputKind.Ide : OutputKind.Normal);
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

    private sealed class Handler(Func<bool> canExecute, Func<Task> execute) : Commands.ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();
    }
}
