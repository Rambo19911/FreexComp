using System.Globalization;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Debugging;

/// <summary>What the views of a paused program depend on: the stop, the thread shown and the selected frame (0 = top).</summary>
public readonly record struct DebugContext(int Epoch, int ThreadId, int FrameIndex);

/// <summary>The kinds of the Event Log (tool-windows.md §15; source events: the debug-session interface §6.1).</summary>
public enum DebugLogKind
{
    Process,
    Module,
    Thread,
    Breakpoint,
    Exception,
    Output,
    Debugger,
}

/// <summary>
/// One row of the Event Log: <paramref name="Index"/> counts from the last clear (UI-TW-1303);
/// <paramref name="Location"/> is where a Breakpoint or Exception row navigates to (UI-TW-1302).
/// </summary>
public sealed record DebugLogEntry(int Index, DateTimeOffset Time, DebugLogKind Kind, string Message, SourceLocation? Location = null);

/// <summary>
/// The inspection side of the debug service (I-141…I-145): the current thread and its frames, the Event Log, the
/// exception notification, the exception settings, value tooltips, the source-changed info bar and the work the debug
/// views have under way.
/// </summary>
public sealed partial class DebugService
{
    /// <summary>UI-TW-902: frames are asked for in pages of 200.</summary>
    public const int FramePageSize = 200;

    /// <summary>UI-TW-1300: the Event Log keeps at most 10 000 rows; the oldest are dropped.</summary>
    public const int MaxLogRows = 10_000;

    /// <summary>The kind of the info bar <c>Msg.Debug.SourceChanged</c> (<c>InfoBar.SourceChanged</c>).</summary>
    public const string SourceChangedKind = "SourceChanged";

    private readonly List<DebugLogEntry> _logRows = [];
    private int _logSequence;
    private readonly HashSet<TextDocumentViewModel> _sourceChanged = [];
    private int? _threadId;
    private StackFrameInfo? _threadFrame;
    private Task<StackTraceResult?>? _frames;

    public int? CurrentThreadId => _stop is null ? null : _threadId;

    public DebugContext? Context => _stop is { } stop && _threadId is { } thread ? new DebugContext(stop.Epoch, thread, _selectedFrame?.Index ?? 0) : null;

    public event EventHandler? ContextChanged;

    public event EventHandler? ValuesChanged;

    public IReadOnlyList<DebugLogEntry> Log => _logRows;

    public event EventHandler<DebugLogEntry?>? LogChanged;

    /// <summary>The expression of the selected row of the focused Locals or Watches window; null when neither has focus.</summary>
    public Func<string?>? ToolExpression { get; set; }

    /// <summary>UI-TW-1103: starts a new row in the focused Watches window; false when it does not have focus.</summary>
    public Func<bool>? AddWatchInPlace { get; set; }

    /// <summary>The frame id evaluations use: null = the top frame of the thread that stopped (DBG-412).</summary>
    private int? FrameId => (_selectedFrame ?? _threadFrame)?.Id;

    public Task Track(Task work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (work.IsCompleted)
            return work;
        _work.Add(work);
        _ = ForgetAsync(work);
        return work;
    }

    private async Task ForgetAsync(Task work)
    {
        try
        {
            await work;
        }
        catch (Exception e) when (e is DebuggerException or ObjectDisposedException or OperationCanceledException)
        {
            // The session's answer to the request: its owner shows it (a value that cannot be assigned, a program that runs again).
        }
        catch (Exception e)
        {
            _log.Error(Category, "A debug view's request failed: " + e.Message, e);
        }
        finally
        {
            _work.Remove(work);
        }
    }

    /// <summary>A debug view has a request under way.</summary>
    public bool HasViewWork => _work.Count > 0;

    /// <summary>Completes once no view has a request under way (work started meanwhile included).</summary>
    public async Task WhenViewsSettledAsync()
    {
        while (_work.Count > 0)
        {
            try
            {
                await Task.WhenAll([.. _work]);
            }
            catch (Exception e)
            {
                _log.Info(Category, "A debug view's request ended with: " + e.Message); // reported by its own observer
            }
        }
    }

    public Task<StackTraceResult?> GetFramesAsync()
    {
        if (_active is not { } active || _stop is null || _threadId is not { } thread)
            return Task.FromResult<StackTraceResult?>(null);
        return _frames ??= FetchFramesAsync(active.Session, thread, 0);
    }

    public Task<StackTraceResult?> GetMoreFramesAsync(int startFrame) =>
        _active is { } active && _stop is not null && _threadId is { } thread
            ? FetchFramesAsync(active.Session, thread, startFrame)
            : Task.FromResult<StackTraceResult?>(null);

    private async Task<StackTraceResult?> FetchFramesAsync(IDebugSession session, int thread, int start)
    {
        try
        {
            return await session.GetStackTraceAsync(thread, start, FramePageSize);
        }
        catch (Exception e) when (e is DebuggerException or ObjectDisposedException or OperationCanceledException)
        {
            _log.Info(Category, "The call stack cannot be read: " + e.Message); // the program runs again, or the session ends
            return null;
        }
    }

    public async Task<StackFrameInfo?> GetEvaluationFrameAsync()
    {
        if (_selectedFrame is { } selected)
            return selected;
        var context = Context;
        var frames = await GetFramesAsync();
        return Context == context && frames is { Frames.Count: > 0 } ? frames.Frames[0] : null;
    }

    public async Task GoToFrameAsync(StackFrameInfo frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_stop is null)
            return;
        SelectFrame(frame);
        if (frame.Index == 0 && _threadId == _stop.ThreadId)
            await ShowExecutionPointAsync();
        else
            await ShowAsync(frame.Location);
    }

    public async Task SwitchThreadAsync(int threadId)
    {
        if (_active is not { } active || _stop is not { } stop || _threadId == threadId)
            return;
        (_threadId, _selectedFrame, _threadFrame, _frames) = (threadId, null, null, null);
        if (threadId != stop.ThreadId)
        {
            // Evaluations name the frame: without one the session takes the top frame of the thread that stopped.
            var frames = await GetFramesAsync();
            if (_active != active || _stop != stop || _threadId != threadId)
                return;
            _threadFrame = frames is { Frames.Count: > 0 } ? frames.Frames[0] : null;
        }

        RefreshDecorations();
        StateChanged?.Invoke(this, EventArgs.Empty);
        ContextChanged?.Invoke(this, EventArgs.Empty);
        if (threadId == stop.ThreadId)
            await ShowExecutionPointAsync();
        else
            await ShowAsync(_threadFrame?.Location);
    }

    private async Task ShowAsync(SourceLocation? location)
    {
        if (location is null)
            return;
        if ((await _documents.OpenAsync(location.Path)).Value is TextDocumentViewModel document)
            _navigation.GoToLine(document, Math.Clamp(location.Line, 1, document.Buffer.LineCount), Math.Max(1, location.Column));
    }

    /// <summary>A stop, a continue or the end of the session: the thread shown is the one that stopped, its top frame is selected.</summary>
    private void ResetContext(int? threadId) => (_threadId, _selectedFrame, _threadFrame, _frames) = (threadId, null, null, null);

    // ---- Event Log (tool-windows.md §15) ----

    public void ClearLog()
    {
        _logSequence = 0;
        if (_logRows.Count == 0)
            return;
        _logRows.Clear();
        LogChanged?.Invoke(this, null);
    }

    /// <summary>Appends a row; beyond <see cref="MaxLogRows"/> the oldest row is dropped (UI-TW-1300).</summary>
    private void AppendLog(DebugLogKind kind, string message, SourceLocation? location = null)
    {
        var entry = new DebugLogEntry(_logSequence++, _clock.Now, kind, message, location);
        _logRows.Add(entry);
        if (_logRows.Count > MaxLogRows)
            _logRows.RemoveAt(0);
        LogChanged?.Invoke(this, entry);
    }

    private void LogStop(StopInfo stop)
    {
        if (stop is { Reason: StopReason.Exception, Exception: { } exception })
        {
            var (name, message) = Describe(exception);
            AppendLog(DebugLogKind.Exception, string.Format(CultureInfo.CurrentCulture, Strings.DebugLog_Exception, name, message), stop.Location);
        }
        else
        {
            AppendLog(DebugLogKind.Breakpoint, stop.Description, stop.Location);
        }
    }

    private void OnModuleLoaded(Active active, ModuleEventArgs e)
    {
        if (_active == active && !active.Finishing)
            AppendLog(DebugLogKind.Module, string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_ModuleLoaded, e.Path.Length > 0 ? e.Path : e.Name));
    }

    private void OnThreadChanged(Active active, ThreadChangedEventArgs e)
    {
        if (_active != active || active.Finishing)
            return;
        AppendLog(DebugLogKind.Thread, string.Format(CultureInfo.CurrentCulture,
            e.Change == ThreadChange.Started ? Strings.Msg_Debug_ThreadStarted : Strings.Msg_Debug_ThreadExited, e.ThreadId));
        ThreadChanged?.Invoke(this, e);
    }

    /// <summary>A thread of the debuggee started or exited (UI-TW-1200).</summary>
    public event EventHandler<ThreadChangedEventArgs>? ThreadChanged;

    // ---- exceptions (dialogs.md §23, settings debugger.*) ----

    /// <summary>The debug-session interface §6.1: the class text and the message of the exception dialog and the Event Log.</summary>
    public static (string ClassName, string Message) Describe(ExceptionInfo exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Kind == ExceptionKind.Language
            ? (exception.ClassName, exception.Message)
            : (exception.Message, "$" + exception.OsCode.ToString("X8", CultureInfo.InvariantCulture));
    }

    /// <summary>The debug-session interface §6.1: <c>debugger.breakOnExceptions</c> and <c>debugger.ignoredExceptions</c> as the session takes them.</summary>
    public ExceptionSettings ExceptionSettingsNow()
    {
        var breakOnExceptions = _settings.Get(SettingsCatalog.DebuggerBreakOnExceptions);
        return new ExceptionSettings
        {
            BreakOnLanguageExceptions = breakOnExceptions,
            BreakOnOsExceptions = breakOnExceptions,
            IgnoredClasses = _settings.Get(SettingsCatalog.DebuggerIgnoredExceptions),
        };
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (_active is not { Finishing: false } active
            || (e.Key != SettingsCatalog.DebuggerBreakOnExceptions.Name && e.Key != SettingsCatalog.DebuggerIgnoredExceptions.Name))
            return;
        Track(SendExceptionSettingsAsync(active.Session));
    }

    private async Task SendExceptionSettingsAsync(IDebugSession session)
    {
        try
        {
            await session.SetExceptionSettingsAsync(ExceptionSettingsNow());
        }
        catch (Exception e) when (e is DebuggerException or ObjectDisposedException or OperationCanceledException)
        {
            _log.Info(Category, "The exception settings were not sent: " + e.Message); // the session is ending
        }
    }

    /// <summary>UI-DLG-230, UI-DLG-231: the program is paused at the raise location; Break stays, Continue resumes.</summary>
    private async Task NotifyExceptionAsync(Active active, StopInfo stop, ExceptionInfo exception)
    {
        var (name, message) = Describe(exception);
        var check = new MessageCheck(Strings.Debug_Exception_Check);
        var answer = await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Debug.Exception", [name, message]) { Check = check });
        if (check.IsChecked && exception.ClassName.Length > 0)
        {
            var ignored = _settings.Get(SettingsCatalog.DebuggerIgnoredExceptions);
            if (!ignored.Contains(exception.ClassName, StringComparer.OrdinalIgnoreCase))
                _settings.Set(SettingsCatalog.DebuggerIgnoredExceptions, [.. ignored, exception.ClassName]); // the change is sent to the session
        }

        if (answer == "Continue" && _active == active && _stop == stop)
            await ControlAsync(s => s.ContinueAsync());
    }

    // ---- value tooltips (editor.md UI-ED-130) ----

    /// <summary>
    /// The value under the pointer at <paramref name="offset"/> of <paramref name="document"/> while paused
    /// (<c>debugger.evaluateTooltips</c>, <c>EvaluateContext.Hover</c>): the identifier or the full designator up to the
    /// hovered token, from the syntax tree (DBG-258, <see cref="HoverExpression"/>), with the hovered token's range and
    /// an expandable value node; null when there is none — errors are not shown (DBG-412).
    /// </summary>
    public async Task<HoverValue?> HoverValueAsync(TextDocumentViewModel document, int offset)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsPaused || !_settings.Get(SettingsCatalog.DebuggerEvaluateTooltips) || offset < 0 || offset >= document.Buffer.Length)
            return null;
        if (HoverExpression.At(document.FilePath ?? "", document.Buffer.Text, offset) is not { } hovered)
            return null;
        var evaluation = EvaluateAsync(hovered.Expression, EvaluateContext.Hover);
        _ = Track(evaluation);
        try
        {
            var value = (await evaluation).Value;
            return value.Kind == ValueKind.Error ? null
                : new HoverValue(hovered.Start, hovered.Length, HoverValueNode.Root(hovered.Expression, value, ReadHoverChildrenAsync));
        }
        catch (Exception e) when (e is DebuggerException or ObjectDisposedException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The children of a tooltip's value node, read in the session they belong to; tracked like every view's request.</summary>
    private Task<IReadOnlyList<VariableInfo>> ReadHoverChildrenAsync(VariableHandle handle, int start, int count)
    {
        if (_active is not { Finishing: false } active || !IsPaused)
            return Task.FromException<IReadOnlyList<VariableInfo>>(new ObjectDisposedException(nameof(IDebugSession)));
        var children = active.Session.GetChildrenAsync(handle, start, count);
        _ = Track(children);
        return children;
    }

    // ---- Msg.Debug.SourceChanged (IDE-P3-006) ----

    /// <summary>
    /// A file edited while a debug session exists gets the info bar, once per session (closing it with × keeps it away
    /// until the next session): its lines may no longer match the program that runs.
    /// </summary>
    private void OnSourceEdited(TextDocumentViewModel document)
    {
        if (_active is not { Finishing: false } || document.FilePath is null || !_sourceChanged.Add(document))
            return;
        document.ShowInfoBar(new InfoBarViewModel(SourceChangedKind, InfoBarSeverity.Warning, Strings.Msg_Debug_SourceChanged,
            [new InfoBarAction("RebuildAndRestart", Strings.Button_RebuildAndRestart, () => RestartAsync())],
            bar => document.RemoveInfoBar(bar.Kind)));
    }

    private void RemoveSourceChangedBars()
    {
        foreach (var document in _sourceChanged)
            document.RemoveInfoBar(SourceChangedKind);
        _sourceChanged.Clear();
    }
}
