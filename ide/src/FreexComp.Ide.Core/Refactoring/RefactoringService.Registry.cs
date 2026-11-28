using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Refactoring;

/// <summary>
/// The registry of the engine (IDE-REFAC-030…034): refactorings with their commands, the providers of quick actions,
/// and <c>edit.quickActions</c> with its menu at the caret.
/// </summary>
public sealed partial class RefactoringService
{
    /// <summary>The command that lists the quick actions of the caret position (IDE-REFAC-034).</summary>
    public const string QuickActionsCommandId = "edit.quickActions";

    /// <summary>IDE-REFAC-032: a position offers at most this many quick actions.</summary>
    internal const int MaxQuickActions = 20;

    private readonly List<IRefactoring> _refactorings = [];
    private readonly List<IQuickActionProvider> _providers = [];
    private readonly Dictionary<TextDocumentViewModel, CancellationTokenSource> _collecting = []; // one request per document
    private ICommandService? _commands;
    private int _implementedPhase;
    private WorkingStatus? _computing; // IDE-REFAC-047 c: the "working" text of the operation whose changes are being computed
    private int _pauses;               // … and the number of running actions that keep it away

    /// <summary>The command is catalogued and available in this composition (<c>Phase8Features</c>).</summary>
    private bool IsAvailable(string commandId) => _commands is { } commands
        && commands.Definitions.FirstOrDefault(d => d.Id == commandId) is { } definition
        && Phase8Features.IsAvailable(commandId, definition.Phase, _implementedPhase);

    public void Register(IRefactoring refactoring)
    {
        ArgumentNullException.ThrowIfNull(refactoring);
        if (_refactorings.Exists(r => r.Id == refactoring.Id))
            throw new InvalidOperationException($"Refactoring '{refactoring.Id}' is already registered.");
        // A command that is not available keeps the stub of its phase (IDE-P8-004).
        if (refactoring.CommandId is { } commandId && IsAvailable(commandId))
            _commands!.RegisterHandler(commandId, new RefactoringHandler(this, refactoring));
        _refactorings.Add(refactoring);
    }

    public void Register(IQuickActionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Add(provider);
    }

    /// <summary>The active document's visible view when it is a text view that can be edited.</summary>
    private TextDocumentViewModel? ActiveWritableText() =>
        _documents.ActiveDocument?.VisibleText is { Buffer.IsReadOnly: false } text ? text : null;

    /// <summary>
    /// IDE-REFAC-031: the context with the language-service document of the file, after the code model was brought up to
    /// date with the open buffers. UI thread.
    /// </summary>
    private RefactoringContext ContextOf(TextDocumentViewModel document, TextSelection selection)
    {
        var source = document.FilePath is { } path && document.Language.Id == "pascal" && _codeModel.Synchronize() is not null
            ? _codeModel.Workspace.GetDocument(path)
            : null;
        return new RefactoringContext(document, selection, source);
    }

    public Task<RefactoringResult> RunAsync(IRefactoring refactoring)
    {
        ArgumentNullException.ThrowIfNull(refactoring);
        if (IsBusy)
            throw new InvalidOperationException("A refactoring operation is running.");
        if (ActiveWritableText() is not { } document)
            return Task.FromResult(Result(RefactoringOutcome.Canceled));
        var context = ContextOf(document, document.Selection);
        return ComputeAndApplyAsync(refactoring.Title, ct => refactoring.ComputeAsync(context, ct));
    }

    /// <summary>
    /// IDE-REFAC-031: one operation from the computation of its changes to its result — busy, with the "working" text.
    /// Null changes: cancelled, nothing shown. A refusal: <c>Msg.Refactoring.Refused</c> with <paramref name="title"/> and
    /// the reason. A file that could not be read while the changes were built: <c>Msg.Refactoring.ApplyFailed</c> with the
    /// file and the system's reason. Any other exception is the caller's (an internal error).
    /// </summary>
    private async Task<RefactoringResult> ComputeAndApplyAsync(string title, Func<CancellationToken, Task<WorkspaceEdit?>> compute)
    {
        var working = new WorkingStatus(_clock, _status, Format(Strings.Msg_Refactoring_Working, title));
        SetBusy(true);
        try
        {
            working.Start();
            WorkspaceEdit? edit;
            (_computing, _pauses) = (working, 0);
            try
            {
                edit = await compute(CancellationToken.None);
            }
            catch (RefactoringException e)
            {
                working.Stop();
                await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Refactoring.Refused", [title, e.Message]));
                return Result(RefactoringOutcome.Failed, error: e.Message);
            }
            catch (WorkspaceEditReadException e)
            {
                _log.Warning(LogCategory, $"{title}: {e.Path} cannot be read: {e.Message}");
                working.Stop();
                await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Refactoring.ApplyFailed", [title, e.Path, e.Message]));
                return Result(RefactoringOutcome.Failed, error: e.Message);
            }
            catch (OperationCanceledException)
            {
                return Result(RefactoringOutcome.Canceled);
            }
            finally
            {
                _computing = null;
            }

            return edit is null ? Result(RefactoringOutcome.Canceled) : await ApplyCoreAsync(edit, PreviewMode.Auto, working, CancellationToken.None);
        }
        finally
        {
            working.Stop();
            SetBusy(false);
        }
    }

    public async Task<T> WithoutWorkingTextAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var working = _computing ?? throw new InvalidOperationException("No refactoring is computing its changes.");
        _pauses++;
        working.Stop();
        try
        {
            return await action();
        }
        finally
        {
            // The 400 ms start again when the outermost action of this computation ends; an action that outlives its
            // computation starts nothing.
            if (ReferenceEquals(_computing, working) && --_pauses == 0)
                working.Start();
        }
    }

    /// <summary>
    /// The confirmation of IDE-REFAC-047 e: shows the changes of a checked edit with a note and returns whether the user
    /// chose Apply. The service graph sets it to the dialog <c>RefactoringPreview</c> in its confirm mode (UI-DLG-387).
    /// Null: every edit that may be applied is confirmed.
    /// </summary>
    internal Func<WorkspaceEdit, string?, Task<bool>>? Confirm { get; set; }

    public async Task<bool> ConfirmAsync(WorkspaceEdit edit, string? note)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (_computing is null)
            throw new InvalidOperationException("No refactoring is computing its changes.");
        var normalized = edit.Normalize();
        if (normalized.Files.IsEmpty)
            return true; // nothing to look at
        if (normalized.FindDefect() is { } defect)
        {
            LogDefect(edit.Title, defect);
            await WithoutWorkingTextAsync(() => _dialogs.ShowMessageAsync(new MessageRequest("Msg.Refactoring.Failed", [edit.Title])));
            return false;
        }

        var plan = await JudgeAsync(normalized);
        if (plan.Blocked.Count > 0)
        {
            await WithoutWorkingTextAsync(() => _dialogs.ShowMessageAsync(new MessageRequest("Msg.Refactoring.Blocked", [edit.Title, BlockedLines(plan.Blocked)])));
            return false;
        }

        return Confirm is not { } show || await WithoutWorkingTextAsync(() => show(normalized, note));
    }

    public async Task<IReadOnlyList<QuickAction>> GetQuickActionsAsync(TextDocumentViewModel document, TextSelection selection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_collecting.TryGetValue(document, out var older))
            older.Cancel(); // a newer request for the document replaces the one that is still collecting
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _collecting[document] = request;
        try
        {
            var context = ContextOf(document, selection);
            var offered = new List<QuickAction>();
            foreach (var provider in _providers)
            {
                try
                {
                    offered.AddRange(await provider.GetActionsAsync(context, request.Token));
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _log.Warning(LogCategory, $"Quick-action provider {provider.GetType().Name} failed: {e.Message}", e);
                }

                request.Token.ThrowIfCancellationRequested();
            }

            // A refactoring judges without a source (no query); it computes with the one of this request.
            var asked = context with { Source = null };
            offered.AddRange(_refactorings.Where(r => r.IsApplicable(asked))
                .Select(r => new QuickAction(r.Id, r.Title, QuickActionKind.Refactoring, token => r.ComputeAsync(context, token))));

            // All fixes, then all refactorings, each in the order received; the same id and title once; at most twenty.
            return [.. offered.OrderBy(a => a.Kind == QuickActionKind.Fix ? 0 : 1).DistinctBy(a => (a.Id, a.Title)).Take(MaxQuickActions)];
        }
        finally
        {
            if (_collecting.TryGetValue(document, out var current) && current == request)
                _collecting.Remove(document);
        }
    }

    /// <summary>
    /// IDE-REFAC-034: the quick actions of the selection of the active text view as a menu at its caret; without one the
    /// status bar says so.
    /// </summary>
    private async Task ShowQuickActionsAsync()
    {
        if (IsBusy || ActiveWritableText() is not { } document)
            return;
        var actions = await GetQuickActionsAsync(document, document.Selection, CancellationToken.None); // cancelled: the command ends silently
        if (!ReferenceEquals(ActiveWritableText(), document))
            return; // another view is in front since
        if (actions.Count == 0)
            _status.ShowMessage(Strings.Msg_QuickActions_None, StatusDuration);
        else
            document.ShowCaretMenu(QuickActionMenu(actions));
    }

    /// <summary>
    /// One item per action with its title as the label, a separator between the fixes and the refactorings; the element
    /// id is <c>Context.quickAction.&lt;Id&gt;</c>, numbered from <c>#2</c> for further actions with the same id.
    /// </summary>
    private List<MenuItemModel> QuickActionMenu(IReadOnlyList<QuickAction> actions)
    {
        var items = new List<MenuItemModel>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (i > 0 && actions[i - 1].Kind != action.Kind)
                items.Add(MenuItemModel.Separator());
            var number = seen[action.Id] = seen.GetValueOrDefault(action.Id) + 1;
            items.Add(MenuItemModel.ForTitledAction("quickAction." + action.Id + (number > 1 ? "#" + number : ""), action.Title, () => Choose(action)));
        }

        return items;
    }

    /// <summary>
    /// A chosen quick action: its changes are computed and applied as one operation (IDE-REFAC-034). Started by a menu
    /// item, so nothing awaits it: an unexpected exception reaches the UI thread's handler (IDE-ARCH-091).
    /// </summary>
    private async void Choose(QuickAction action)
    {
        if (!IsBusy)
            await ComputeAndApplyAsync(action.Title, action.ComputeAsync);
    }

    /// <summary>IDE-REFAC-030: the handler of the command a refactoring is bound to.</summary>
    private sealed class RefactoringHandler(RefactoringService owner, IRefactoring refactoring) : ICommandHandler
    {
        public bool CanExecute(string? argument) => !owner.IsBusy && owner.ActiveWritableText() is { } document
            && refactoring.IsApplicable(new RefactoringContext(document, document.Selection, null));

        public Task ExecuteAsync(string? argument) => CanExecute(argument) ? owner.RunAsync(refactoring) : Task.CompletedTask;
    }

    private sealed class QuickActionsHandler(RefactoringService owner) : ICommandHandler
    {
        public bool CanExecute(string? argument) => !owner.IsBusy && owner.ActiveWritableText() is not null;

        public Task ExecuteAsync(string? argument) => owner.ShowQuickActionsAsync();
    }
}
