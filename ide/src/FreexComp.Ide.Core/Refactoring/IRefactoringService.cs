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

    /// <summary>The busy state changed (an operation started or ended).</summary>
    event EventHandler? Changed;
}
