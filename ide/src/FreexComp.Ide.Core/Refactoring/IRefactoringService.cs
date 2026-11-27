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

    /// <summary>The busy state or the history changed (an operation started or ended, the history was cleared).</summary>
    event EventHandler? Changed;
}
