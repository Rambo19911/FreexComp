using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// The inline rename session of the document (IDE-REFAC-050…055, I-381): its occurrences are one linked field of the
/// document's field mode (IDE-ED-065), the primary occurrence first; all edits of a session are one undo step; a cancel
/// takes that step back (<see cref="ITextBuffer.Undo"/> + <see cref="ITextBuffer.ClearRedo"/>).
/// </summary>
public partial class TextDocumentViewModel
{
    private const string IndentCommandId = "edit.indent";

    private InlineRenameSession? _renameSession;

    /// <summary>The active inline rename session of this document, or null (IDE-REFAC-050).</summary>
    public InlineRenameSession? RenameSession => _renameSession;

    /// <summary>A session started or ended, or its state changed (IDE-REFAC-050).</summary>
    public event EventHandler? RenameSessionChanged;

    /// <summary>
    /// IDE-REFAC-042: the document is in block-selection mode (a column block is selected). The editor has no multiple
    /// carets, so this is the whole of "block selection or several carets".
    /// </summary>
    public bool HasBlockSelection => Block is not null;

    /// <summary>UI-ED-160: the occurrences of the active session (start, end), the primary first; empty without one.</summary>
    public IReadOnlyList<(int Start, int End)> RenameFields =>
        _renameSession is not null && _fieldMode is { IsActive: true } mode ? mode.Occurrences(0) : [];

    /// <summary>The typed name: the primary occurrence's text.</summary>
    internal string RenamePrimaryText => RenameFields is [var (start, end), ..] ? Buffer.GetText(start, end - start) : "";

    /// <summary>The task of the last <c>Enter</c> / <c>Tab</c> of a session (tests await it, I-210).</summary>
    public Task RenameCommitting { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// IDE-REFAC-050: a template field mode ends first; the occurrences become one linked field, the one that contains
    /// <paramref name="position"/> (or ends at it) is the primary occurrence and is selected whole.
    /// </summary>
    internal void BeginRenameSession(InlineRenameSession session, IReadOnlyList<(int Start, int End)> spans, int position)
    {
        EndFieldMode();
        var primary = Math.Max(0, spans.ToList().FindIndex(s => s.Start <= position && position <= s.End));
        _fieldMode = new TemplateFieldMode([spans[primary], .. spans.Where((_, i) => i != primary)]);
        _renameSession = session;
        session.Changed += (_, _) => RenameSessionChanged?.Invoke(this, EventArgs.Empty);
        SelectField(0);
        TemplateFieldsChanged?.Invoke(this, EventArgs.Empty);
        RenameSessionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The fields of the session end (the session itself raises its change).</summary>
    internal void EndRenameFields()
    {
        if (_renameSession is null)
            return;
        _renameSession = null;
        _fieldMode?.Deactivate();
        _fieldMode = null;
        TemplateFieldsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// IDE-REFAC-054: ends the fields and takes the session's undo step back (its redo is cleared); the caret goes to
    /// <paramref name="caret"/>, else it stays where it is, moved with the restored text. True when text was restored.
    /// </summary>
    internal bool UndoRenameEdits(int? caret)
    {
        if (_renameSession is not { } session || _fieldMode is not { } mode)
            return false;
        var edited = mode.LastEditVersion >= 0 && mode.LastEditVersion == Buffer.Version && Buffer.CanUndo;
        var fields = mode.Occurrences(0);
        var now = CaretOffset;
        EndRenameFields();
        if (edited)
        {
            var oldLength = session.OldName.Length;
            var moved = now;
            foreach (var (start, end) in fields)
            {
                if (end <= now && !(start <= now && now <= end))
                    moved += oldLength - (end - start);
                else if (start <= now && now <= end)
                    moved += Math.Min(now - start, oldLength) - (now - start);
            }

            var readOnly = Buffer.IsReadOnly;
            Buffer.IsReadOnly = false;
            try
            {
                UndoStep();
                Buffer.ClearRedo();
                _redoSelections.Clear();
            }
            finally
            {
                Buffer.IsReadOnly = readOnly;
            }

            Selection = TextSelection.At(Math.Clamp(caret ?? moved, 0, Buffer.Length));
        }
        else if (caret is { } at)
        {
            Selection = TextSelection.At(Math.Clamp(at, 0, Buffer.Length));
        }

        OnPropertyChanged(nameof(IsModified));
        return edited;
    }

    /// <summary>
    /// IDE-REFAC-054: the commands that keep a session — character input, deleting, cut / copy / paste of a selection, and
    /// the session's own keys (<c>edit.undo</c> cancels it, Tab = <c>edit.indent</c> commits it). Every other command
    /// cancels it first.
    /// </summary>
    public bool KeepsRenameSession(string commandId) => commandId switch
    {
        UndoCommandId or IndentCommandId or PasteCommandId or DeleteCommandId => true,
        CutCommandId or CopyCommandId => !Selection.IsEmpty,
        _ => false,
    };

    /// <summary>The keys of a session (IDE-REFAC-053, 054): Enter / Tab commit, Escape cancels; false = not a session key.</summary>
    private bool HandleRenameKey(string gesture)
    {
        if (_renameSession is not { } session)
            return false;
        switch (gesture)
        {
            case "Enter" or "Tab":
                RenameCommitting = session.CommitAsync();
                return true;
            case "Escape":
                session.Cancel(restoreCaret: true);
                return true;
            case "Shift+Enter":
                return true; // IDE-REFAC-051: no line break in a name
            case "Shift+Tab":
                session.Cancel(); // another editor command: the session is cancelled first
                return false;
            default:
                return false;
        }
    }

    /// <summary>The caret or the selection left every occurrence (IDE-REFAC-054).</summary>
    private bool LeftRenameFields(TemplateFieldMode mode)
    {
        var caret = OffsetOf(ViewState.Caret);
        if (!mode.Contains(caret))
            return true;
        if (ViewState.SelectionAnchor is not { } anchor)
            return false;
        var start = Math.Min(caret, OffsetOf(anchor));
        var end = Math.Max(caret, OffsetOf(anchor));
        return !mode.Occurrences(0).Any(o => o.Start <= start && end <= o.End);
    }
}
