using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Undo grouping of typed input (IDE-P1-091), one step per command (IDE-P1-092) and caret/selection restoration on
/// undo and redo (IDE-P1-093). Every user edit runs through <see cref="Act"/>, which opens one undo group (a continued
/// one while typing goes on) and remembers the selection before and after the step. Undo and redo always go through
/// <see cref="ITextBuffer.Undo"/> / <see cref="ITextBuffer.Redo"/> (IDE-ARCH-030).
/// </summary>
public partial class TextDocumentViewModel
{
    /// <summary>A pause this long without typing ends the typing step (IDE-P1-091).</summary>
    public static readonly TimeSpan TypingPause = TimeSpan.FromSeconds(1);

    private enum EditKind { None, Typing, Backspace, Delete }

    private readonly record struct SelectionState(TextPosition? Anchor, TextPosition Caret, bool Rectangular);

    /// <summary>The selections around one undo step; <see cref="Version"/> = buffer version right after it (or after its redo/undo).</summary>
    private sealed record StepSelections(SelectionState Before, SelectionState After, long Version);

    private readonly List<StepSelections> _undoSelections = [];
    private readonly List<StepSelections> _redoSelections = [];
    private int _actionDepth;
    private EditKind _lastKind;
    private long _lastVersion;
    private int _lastCaret;
    private DateTimeOffset _lastTime;
    private bool _lastWhitespace;

    /// <summary>The clock of the typing pause; null (documents outside the document service) ignores pauses.</summary>
    public IClock? Clock { get; internal set; }

    /// <summary>The view scrolls the caret into view (IDE-P1-093: after undo and redo).</summary>
    public event EventHandler? RevealCaretRequested;

    private SelectionState CurrentSelection => new(ViewState.SelectionAnchor, ViewState.Caret, ViewState.RectangularSelection);

    private void RestoreSelection(SelectionState state)
    {
        ViewState.RectangularSelection = false;
        ViewState.SelectionAnchor = state.Anchor;
        ViewState.Caret = state.Caret;
        ViewState.RectangularSelection = state.Rectangular;
    }

    /// <summary>
    /// A user edit: <paramref name="action"/> runs inside one undo group (joining the previous step when
    /// <paramref name="continues"/>); when it changed the text, the selections before and after are kept for undo/redo.
    /// Nested calls belong to the outer edit.
    /// </summary>
    private bool Act(bool continues, Func<bool> action)
    {
        if (_actionDepth > 0)
            return action();
        continues |= _renameSession is not null && ContinuesFieldEdits; // IDE-REFAC-051: a session is one undo step
        var before = CurrentSelection;
        var version = Buffer.Version;
        bool result;
        _actionDepth++;
        try
        {
            using (continues ? Buffer.OpenContinuedUndoGroup() : Buffer.OpenUndoGroup())
            {
                result = action();
                SyncTemplateMirrors(); // IDE-ED-065: mirrors follow in the same step
            }
        }
        finally
        {
            _actionDepth--;
        }

        if (Buffer.Version != version)
        {
            RecordStep(before, version, continues);
            _renameSession?.OnEdited(); // IDE-REFAC-052
        }

        return result;
    }

    private void RecordStep(SelectionState before, long versionBefore, bool continued)
    {
        _redoSelections.Clear();
        var after = CurrentSelection;
        if (continued && _undoSelections.Count > 0 && _undoSelections[^1].Version == versionBefore)
            _undoSelections[^1] = _undoSelections[^1] with { After = after, Version = Buffer.Version };
        else
            _undoSelections.Add(new StepSelections(before, after, Buffer.Version));
    }

    /// <summary>A command edit (IDE-P1-092): one undo step of its own, never joined with typing.</summary>
    private bool Command(Func<bool> action)
    {
        var result = Act(continues: false, action);
        EndEdit(EditKind.None, whitespace: false);
        return result;
    }

    /// <summary>IDE-P1-091: whether an edit of <paramref name="kind"/> at <paramref name="caret"/> joins the current step.</summary>
    private bool ContinuesGroup(EditKind kind, int caret, bool whitespace) =>
        ContinuesFieldEdits // IDE-ED-065: field edits until field mode ends are one step
        || _lastKind == kind && Buffer.Version == _lastVersion && caret == _lastCaret && !Buffer.IsAtSavePoint
        && (Clock is null || Clock.Now - _lastTime < TypingPause)
        && (_lastWhitespace || !whitespace); // a space after a word starts the next step

    /// <summary>After an edit: what may continue it (<see cref="EditKind.None"/>: nothing).</summary>
    private void EndEdit(EditKind kind, bool whitespace)
    {
        _lastKind = kind;
        _lastVersion = Buffer.Version;
        _lastCaret = OffsetOf(ViewState.Caret);
        _lastTime = Clock?.Now ?? default;
        _lastWhitespace = whitespace;
    }

    /// <summary>A caret move that is not part of an edit ends the typing step.</summary>
    private void OnCaretMovedForUndo()
    {
        if (_actionDepth == 0 && OffsetOf(ViewState.Caret) != _lastCaret)
            _lastKind = EditKind.None;
    }

    /// <summary>
    /// Backspace or Delete of one character at the caret (IDE-P1-091: consecutive presses join one step);
    /// <paramref name="deleted"/> is the text the key removes.
    /// </summary>
    private bool DeleteKey(EditKind kind, string deleted, Func<bool> action)
    {
        var whitespace = deleted.Length > 0 && deleted.All(char.IsWhiteSpace);
        var caret = OffsetOf(ViewState.Caret);
        var done = Act(ContinuesGroup(kind, caret, whitespace), action);
        EndEdit(done ? kind : EditKind.None, whitespace);
        return done;
    }

    /// <summary>
    /// <c>edit.delete</c>: the selection, else the character after the caret (a CRLF pair or a surrogate pair as one);
    /// refused on a read-only document.
    /// </summary>
    public bool DeleteForward()
    {
        var selection = Selection;
        if (!selection.IsEmpty || Block is not null)
            return DeleteSelection();
        var caret = selection.Caret;
        var next = Buffer.GetText(caret, Math.Min(2, Buffer.Length - caret));
        if (next.Length == 0)
            return false;
        var length = next is "\r\n" || (next.Length == 2 && char.IsSurrogatePair(next[0], next[1])) ? 2 : 1;
        return DeleteKey(EditKind.Delete, next[..length], () =>
        {
            if (!TryInsertText(caret, length, ""))
                return false;
            Selection = TextSelection.At(caret);
            return true;
        });
    }

    /// <summary>IDE-P1-093: undo, then the selection as it was before the step.</summary>
    private void UndoStep()
    {
        _lastKind = EditKind.None;
        var entry = _undoSelections.Count > 0 && _undoSelections[^1].Version == Buffer.Version ? _undoSelections[^1] : null;
        if (entry is null)
            ForgetSelections(); // a step made elsewhere (save trimming, an external tool): nothing to restore
        else
            _undoSelections.RemoveAt(_undoSelections.Count - 1);
        Buffer.Undo();
        if (entry is not null)
        {
            RestoreSelection(entry.Before);
            _redoSelections.Add(entry with { Version = Buffer.Version });
        }

        RevealCaretRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>IDE-P1-093: redo, then the selection as it was after the step.</summary>
    private void RedoStep()
    {
        _lastKind = EditKind.None;
        var entry = _redoSelections.Count > 0 && _redoSelections[^1].Version == Buffer.Version ? _redoSelections[^1] : null;
        if (entry is null)
            ForgetSelections();
        else
            _redoSelections.RemoveAt(_redoSelections.Count - 1);
        Buffer.Redo();
        if (entry is not null)
        {
            RestoreSelection(entry.After);
            _undoSelections.Add(entry with { Version = Buffer.Version });
        }

        RevealCaretRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ForgetSelections()
    {
        _undoSelections.Clear();
        _redoSelections.Clear();
    }
}
