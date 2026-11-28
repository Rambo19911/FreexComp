using System.Globalization;
using FreexComp.Compiler.Syntax;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Refactoring;

/// <summary>How the occurrences of an inline rename are drawn (UI-ED-160, 161).</summary>
public enum RenameFieldState
{
    /// <summary>No session.</summary>
    None,

    /// <summary>A session without a known problem: the primary occurrence is outlined with <c>border.focus</c>.</summary>
    Editing,

    /// <summary>The typed name has a problem (IDE-REFAC-052): the outline is <c>status.error</c>.</summary>
    Problem,
}

/// <summary>
/// The inline rename of a local (IDE-REFAC-050…055, I-381): the occurrences are one linked field of the document's field
/// mode; the typed name is checked 200 ms after the last change; <c>Enter</c> / <c>Tab</c> commit after the check, a cancel
/// takes the session's edits back (<c>Undo</c> + <c>ClearRedo</c>). The engine is not busy during a session. UI thread.
/// </summary>
public sealed class InlineRenameSession
{
    /// <summary>The pause after the last change before the typed name is checked (IDE-REFAC-052).</summary>
    public static readonly TimeSpan CheckDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>How long the result of a session stays in the status bar (IDE-REFAC-053, 054).</summary>
    public static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5);

    private readonly Func<string, CancellationToken, Task<RenamePlan>> _plan;
    private readonly Action<RenamePlan, string>? _committed;
    private RenamePlan? _lastPlan;       // the plan of the last check that succeeded, for _resultVersion
    private readonly IClock _clock;
    private readonly IStatusService _status;
    private IDisposable? _sticky;
    private IDisposable? _timer;
    private CancellationTokenSource? _request;
    private long _checkedVersion = -1;   // the buffer version the last check was started for
    private long _resultVersion = -1;    // the buffer version the last check answered for
    private Task _checking = Task.CompletedTask;

    internal InlineRenameSession(TextDocumentViewModel document, Document source, int position, string oldName, int startCaret,
        Func<string, CancellationToken, Task<RenamePlan>> plan, IClock clock, IStatusService status, Action<RenamePlan, string>? committed = null)
    {
        Document = document;
        Source = source;
        Position = position;
        OldName = oldName;
        StartCaret = startCaret;
        _plan = plan;
        _clock = clock;
        _status = status;
        _committed = committed;
    }

    /// <summary>The document of the session.</summary>
    public TextDocumentViewModel Document { get; }

    /// <summary>The snapshot the session was started on (the checks plan on it).</summary>
    public Document Source { get; }

    /// <summary>The target's position in <see cref="Source"/>.</summary>
    public int Position { get; }

    /// <summary>The name before the session.</summary>
    public string OldName { get; }

    /// <summary>The caret offset when the session started (Escape and <c>edit.undo</c> return to it).</summary>
    public int StartCaret { get; }

    /// <summary>True until the session ends (committed, cancelled or ended by a change from outside).</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>The problem of the typed name (IDE-REFAC-052), or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>UI-ED-160, 161: how the occurrences are drawn.</summary>
    public RenameFieldState State => !IsActive ? RenameFieldState.None : Problem is null ? RenameFieldState.Editing : RenameFieldState.Problem;

    /// <summary>The typed name: the primary occurrence's text.</summary>
    public string TypedName => Document.RenamePrimaryText;

    /// <summary>The running check, or the last one (tests await it so that no work outlives them, I-210).</summary>
    public Task Checking => _checking;

    /// <summary>The state or the problem changed, or the session ended.</summary>
    public event EventHandler? Changed;

    private static string Format(string resource, params object[] args) => string.Format(CultureInfo.CurrentCulture, resource, args);

    /// <summary>IDE-REFAC-050: the sticky hint.</summary>
    internal void Started() => ShowSticky(Format(Strings.Msg_Rename_InlineHint, OldName));

    private void ShowSticky(string text)
    {
        var previous = _sticky;
        _sticky = _status.ShowStickyMessage(text);
        previous?.Dispose();
    }

    /// <summary>IDE-REFAC-052: an edit of the session; what was known is dropped and the 200 ms start again.</summary>
    internal void OnEdited()
    {
        if (!IsActive)
            return;
        _timer?.Dispose();
        _request?.Cancel();
        _timer = _clock.Schedule(CheckDelay, () => _ = StartCheck());
        SetProblem(null);
    }

    private void SetProblem(string? problem)
    {
        var changed = Problem != problem;
        Problem = problem;
        if (!changed)
            return;
        ShowSticky(problem ?? Format(Strings.Msg_Rename_InlineHint, OldName));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The problem of a name without the language service: empty, reserved, not an identifier; null = none.</summary>
    private static string? Validate(string name)
    {
        if (SyntaxFacts.IsReservedWord(name))
            return Format(Strings.Msg_Rename_NameReserved, name);
        if (name.Length == 0 || !SyntaxFacts.IsValidIdentifier(name))
            return Strings.Msg_Rename_NameInvalid;
        return null;
    }

    /// <summary>Starts the check of the present text (once per buffer version) and answers its task.</summary>
    private Task StartCheck()
    {
        _timer?.Dispose();
        _timer = null;
        var version = Document.Buffer.Version;
        if (!IsActive || _checkedVersion == version)
            return _checking;
        _checkedVersion = version;
        var name = TypedName;
        if (string.Equals(name, OldName, StringComparison.Ordinal))
            return _checking = Answer(version, null);
        if (Validate(name) is { } invalid)
            return _checking = Answer(version, invalid);
        _request?.Cancel();
        _request = new CancellationTokenSource();
        return _checking = CheckAsync(version, name, _request.Token);
    }

    private Task Answer(long version, string? problem, RenamePlan? plan = null)
    {
        _resultVersion = version;
        _lastPlan = plan;
        SetProblem(problem);
        return Task.CompletedTask;
    }

    private async Task CheckAsync(long version, string name, CancellationToken ct)
    {
        string? problem;
        RenamePlan? plan = null;
        try
        {
            plan = await _plan(name, ct);
            problem = plan.Result.Success ? null : plan.Result.Conflicts.IsEmpty ? plan.Result.Error ?? "" : plan.Result.Conflicts[0].Message;
        }
        catch (OperationCanceledException)
        {
            return; // a newer edit or the end of the session
        }
        catch (IOException e)
        {
            problem = Format(Strings.Reason_Rename_NoCodeModel, e.Message);
        }

        // IDE-ARCH-083: a result for an older buffer version is dropped.
        if (IsActive && version == Document.Buffer.Version)
            _ = Answer(version, problem, problem is null ? plan : null);
    }

    /// <summary>
    /// IDE-REFAC-053: <c>Enter</c> / <c>Tab</c>. The old name → the session ends; a known problem → nothing; a pending check is
    /// asked at once and the commit continues with its result for the present version while the session is still active.
    /// </summary>
    internal async Task CommitAsync()
    {
        if (!IsActive)
            return;
        var version = Document.Buffer.Version;
        if (string.Equals(TypedName, OldName, StringComparison.Ordinal))
        {
            End();
            return;
        }

        if (_resultVersion != version)
            await StartCheck();
        if (!IsActive || _resultVersion != version || Document.Buffer.Version != version || Problem is not null)
            return; // changed meanwhile (Enter is pressed again), or a problem stays shown
        var count = Document.RenameFields.Count;
        var newName = TypedName;
        var plan = _lastPlan;
        End();
        _status.ShowMessage(count == 1 ? Format(Strings.Msg_Rename_InlineDoneOne, OldName, newName)
            : Format(Strings.Msg_Rename_InlineDone, OldName, newName, count), StatusDuration);
        if (plan is not null)
            _committed?.Invoke(plan, newName); // IDE-REFAC-061: the unrenamed occurrences of the routine
    }

    /// <summary>
    /// IDE-REFAC-054: the session's edits are taken back (text, undo history and save point as before the session); for
    /// Escape and <c>edit.undo</c> (<paramref name="restoreCaret"/>) the caret returns to its offset at the start.
    /// </summary>
    public void Cancel(bool restoreCaret = false)
    {
        if (!IsActive)
            return;
        Stop();
        if (Document.UndoRenameEdits(restoreCaret ? StartCaret : null))
            _status.ShowMessage(Format(Strings.Msg_Rename_InlineCanceled, OldName), StatusDuration);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The session ends as it is (committed, or a change from outside replaced the text).</summary>
    internal void End()
    {
        if (!IsActive)
            return;
        Stop();
        Document.EndRenameFields();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Stop()
    {
        IsActive = false;
        _timer?.Dispose();
        _timer = null;
        _request?.Cancel();
        _sticky?.Dispose();
        _sticky = null;
    }
}

/// <summary>
/// The inline sessions of the IDE (IDE-REFAC-050, 054): starts them for <see cref="RenameRefactoring"/> and cancels the active
/// one before any other command, when the engine becomes busy and when its document stops being the active text.
/// </summary>
public sealed class InlineRenameController
{
    private readonly IDocumentService _documents;
    private readonly RenamePlanner _planner;
    private readonly IClock _clock;
    private readonly IStatusService _status;

    public InlineRenameController(ICommandService commands, IRefactoringService engine, IDocumentService documents, RenamePlanner planner,
        IClock clock, IStatusService status)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(status);
        _documents = documents;
        _planner = planner;
        _clock = clock;
        _status = status;
        commands.Executing += (_, e) =>
        {
            if (Active is { } session && !session.Document.KeepsRenameSession(e.CommandId))
                session.Cancel();
        };
        engine.Changed += (_, _) =>
        {
            if (engine.IsBusy)
                Active?.Cancel();
        };
        documents.ActiveDocumentChanged += (_, _) =>
        {
            if (Active is { } session && !ReferenceEquals(documents.ActiveDocument?.VisibleText, session.Document))
                session.Cancel();
        };
    }

    /// <summary>The active session, or null.</summary>
    public InlineRenameSession? Active { get; private set; }

    /// <summary>IDE-REFAC-050: a session for the target <paramref name="info"/> at <paramref name="position"/> of <paramref name="source"/>.</summary>
    internal InlineRenameSession Start(TextDocumentViewModel document, Document source, int position, RenameInfo info)
    {
        Active?.Cancel();
        var session = new InlineRenameSession(document, source, position, info.CurrentName, document.CaretOffset,
            (name, ct) => _planner.PlanAsync(new RenameRequest(source, position, info.CurrentName, name, Overloads: false, Forms: false), ct),
            _clock, _status,
            (plan, newName) => _planner.ReportUnrenamed(plan.Result, info.CurrentName, newName, plan.Edit?.Title ?? "", plan.Edit?.Files ?? []));
        session.Changed += (_, _) =>
        {
            if (!session.IsActive && ReferenceEquals(Active, session))
                Active = null;
        };
        Active = session;
        document.BeginRenameSession(session, [.. info.LocalSpans.Select(s => (s.Start, s.End))], position);
        session.Started();
        return session;
    }
}
