using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Refactoring;

/// <summary>
/// The refactoring engine (refactoring spec §4, F13-003): applies workspace edits all or nothing. UI thread only.
/// </summary>
public interface IRefactoringService
{
    /// <summary>IDE-REFAC-012: true while an operation is between its start and its result.</summary>
    bool IsBusy { get; }

    /// <summary>
    /// Applies <paramref name="edit"/> (IDE-REFAC-001…012): normalisation and validation, the checks, the preview decided
    /// by <paramref name="preview"/>, the checks again, then the closed files are written and the open buffers edited —
    /// each buffer as one undo step, not saved; the <c>.form</c> of an open form document receives its text through the
    /// designer (IDE-REFAC-011). Either every file has its new text afterwards or none has. Messages and
    /// the status text are shown here; the result says how it ended. <paramref name="ct"/> cancels until the first
    /// write (outcome <see cref="RefactoringOutcome.Canceled"/>); after it the operation runs to its end.
    /// </summary>
    /// <exception cref="InvalidOperationException">An operation is running (<see cref="IsBusy"/>).</exception>
    Task<RefactoringResult> ApplyAsync(WorkspaceEdit edit, PreviewMode preview = PreviewMode.Auto, CancellationToken ct = default);

    /// <summary>
    /// IDE-REFAC-020: an applied operation is on the history. The history is in memory, last in first out, limited in
    /// number and size, and cleared when the project closes or another one is loaded.
    /// </summary>
    bool CanUndo { get; }

    /// <summary>The title of the newest operation of the history (the one <see cref="UndoAsync"/> takes back); null without one.</summary>
    string? UndoTitle { get; }

    /// <summary>
    /// IDE-REFAC-021: takes the newest operation back in every file. A file that has the text the operation gave it is
    /// restored — a buffer in which nothing happened since by its own undo, any other buffer by one new undo step, a form
    /// through the designer, a closed file on disk (byte for byte when nothing rewrote it); a file that already has its
    /// earlier text is left alone; any other state of a file blocks the undo: nothing changes and the operation stays
    /// on the history. The result: <see cref="RefactoringOutcome.Applied"/> with the number of restored files as
    /// <see cref="RefactoringResult.FileCount"/> (edit count 0), <see cref="RefactoringOutcome.NoChanges"/> when every
    /// file was back already, <see cref="RefactoringOutcome.Blocked"/> or <see cref="RefactoringOutcome.Failed"/>.
    /// Messages and the status text are shown here. There is no redo.
    /// </summary>
    /// <exception cref="InvalidOperationException">An operation is running (<see cref="IsBusy"/>), or nothing can be undone.</exception>
    Task<RefactoringResult> UndoAsync();

    /// <summary>
    /// IDE-REFAC-030: adds a refactoring. It is offered as a quick action where it is applicable, and when its
    /// <see cref="IRefactoring.CommandId"/> names a command that is available it becomes that command's handler — enabled
    /// in a writable text view while no operation runs and the refactoring is applicable. Called while the IDE is
    /// composed, before the commands without a handler get their stubs.
    /// </summary>
    /// <exception cref="InvalidOperationException">A refactoring with this id is registered, or the command has a handler.</exception>
    void Register(IRefactoring refactoring);

    /// <summary>IDE-REFAC-032: adds a source of quick actions; the providers are asked in the order they were registered.</summary>
    void Register(IQuickActionProvider provider);

    /// <summary>
    /// IDE-REFAC-031: runs <paramref name="refactoring"/> at the selection of the active document's visible text view: its
    /// changes are computed and applied with <see cref="PreviewMode.Auto"/>. A null result (the user cancelled) or no
    /// writable text view: <see cref="RefactoringOutcome.Canceled"/>, nothing shown; a refusal
    /// (<see cref="RefactoringException"/>) or a file that cannot be read while the changes are built: a message and
    /// <see cref="RefactoringOutcome.Failed"/>. The engine is busy from the start to the result.
    /// </summary>
    /// <exception cref="InvalidOperationException">An operation is running (<see cref="IsBusy"/>).</exception>
    Task<RefactoringResult> RunAsync(IRefactoring refactoring);

    /// <summary>
    /// IDE-REFAC-032: the quick actions for <paramref name="selection"/> of <paramref name="document"/> — what the
    /// providers offer, then one action per registered refactoring that is applicable; all fixes before all
    /// refactorings, each in the order received, an action with the id and title of an earlier one left out, at most
    /// twenty. A provider that throws is logged and skipped. The task is cancelled by <paramref name="ct"/>, by a newer
    /// request for the same document and when the document closes. The engine is not busy meanwhile.
    /// </summary>
    Task<IReadOnlyList<QuickAction>> GetQuickActionsAsync(TextDocumentViewModel document, TextSelection selection, CancellationToken ct);

    /// <summary>
    /// IDE-REFAC-047 c: runs <paramref name="action"/> — a dialog of the refactoring, or an operation of another service
    /// that shows dialogs of its own — without the "working" text in the status bar: the text is taken away and does not
    /// come back while the action runs; when it ends, also by an exception, the 400 ms after which the text is shown
    /// start again. The engine stays busy. Only for the refactoring or quick action whose changes are being computed.
    /// </summary>
    /// <exception cref="InvalidOperationException">No operation is computing its changes.</exception>
    Task<T> WithoutWorkingTextAsync<T>(Func<Task<T>> action);

    /// <summary>
    /// IDE-REFAC-047 d: the files that keep <paramref name="edit"/> from being applied now, each with the first reason
    /// that applies to it (IDE-REFAC-006), in path order; empty when it may be applied. Edits that change nothing are
    /// not judged. Nothing is shown and nothing changes; the engine does not become busy, and the question may be asked
    /// at any time, also while an operation runs.
    /// </summary>
    Task<IReadOnlyList<BlockedFile>> CheckAsync(WorkspaceEdit edit);

    /// <summary>
    /// IDE-REFAC-047 e: shows the changes of <paramref name="edit"/> in the Preview dialog's confirm mode (UI-DLG-387:
    /// every change or none, with <paramref name="note"/> as a line of text when it is given) and returns whether the
    /// user chose Apply. Nothing is applied or recorded — the caller carries the changes out by other means. An edit that
    /// cannot be applied is reported like <see cref="ApplyAsync"/> reports it (an invalid edit, blocked files) and gives
    /// false; an edit without changes gives true without a dialog. Only for the refactoring or quick action whose
    /// changes are being computed.
    /// </summary>
    /// <exception cref="InvalidOperationException">No operation is computing its changes.</exception>
    Task<bool> ConfirmAsync(WorkspaceEdit edit, string? note);

    /// <summary>The busy state or the history changed (an operation started or ended, the history was cleared).</summary>
    event EventHandler? Changed;
}
