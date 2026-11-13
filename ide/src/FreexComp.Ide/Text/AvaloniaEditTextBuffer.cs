using AvaloniaEdit.Document;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using ITextAnchor = FreexComp.Ide.Core.Text.ITextAnchor;

namespace FreexComp.Ide.Text;

/// <summary>
/// <see cref="ITextBuffer"/> over an AvaloniaEdit <see cref="TextDocument"/> and its <see cref="UndoStack"/>
/// (IDE-ARCH-030). The editor view binds to <see cref="Document"/>; changes typed there reach <see cref="Changed"/>
/// and <see cref="Version"/> like every other change.
/// <para>
/// The save point is tracked here, not by <see cref="UndoStack.IsOriginalFile"/> alone: after undoing below the
/// marked position and making a new edit, AvaloniaEdit 12.0.0 reports the original file again although the text
/// differs. That needs to know undo from redo, so undo and redo must go through <see cref="Undo"/> /
/// <see cref="Redo"/> (the editor view routes them to <c>edit.undo</c>/<c>edit.redo</c>), never through
/// <c>UndoStack</c> directly.
/// </para>
/// </summary>
public sealed class AvaloniaEditTextBuffer : ITextBuffer
{
    /// <summary>Where the current undo position is relative to the save point.</summary>
    private enum SavePointSide { At, Above, Below, Lost }

    private readonly LineEndingKind _defaultLineEnding;
    private SavePointSide _side;
    private bool _replaying;
    private readonly List<Core.Text.TextChangedEventArgs> _replayed = [];
    private int _groupDepth;

    public AvaloniaEditTextBuffer(string text, LineEndingKind defaultLineEnding)
    {
        ArgumentNullException.ThrowIfNull(text);
        _defaultLineEnding = defaultLineEnding;
        Document = new TextDocument(text);
        MarkSavePoint();
        Document.Changed += OnDocumentChanged;
    }

    /// <summary>The wrapped document (<c>TextEditor.Document</c> of the code editor view).</summary>
    public TextDocument Document { get; }

    public string Text => Document.Text;
    public int Length => Document.TextLength;
    public int LineCount => Document.LineCount;
    public long Version { get; private set; }
    public bool IsReadOnly { get; set; }
    public LineEndingKind LineEnding => TextBufferRules.DominantLineEnding(Document.Text, _defaultLineEnding);
    public bool CanUndo => Document.UndoStack.CanUndo;
    public bool CanRedo => Document.UndoStack.CanRedo;
    public bool IsAtSavePoint => _side == SavePointSide.At;

    public event EventHandler<Core.Text.TextChangedEventArgs>? Changed;

    public string GetText(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > Document.TextLength - length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return Document.GetText(offset, length);
    }

    public TextPosition GetPosition(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, Document.TextLength);
        var line = Document.GetLineByOffset(offset);
        return new TextPosition(line.LineNumber, offset - line.Offset + 1);
    }

    public int GetOffset(int line, int column)
    {
        var (offset, length, _) = GetLine(line);
        return offset + Math.Clamp(column - 1, 0, length);
    }

    public (int Offset, int Length, int DelimiterLength) GetLine(int line)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(line, Document.LineCount);
        var l = Document.GetLineByNumber(line);
        return (l.Offset, l.Length, l.DelimiterLength);
    }

    public void ApplyEdits(IReadOnlyList<TextEdit> edits, string? undoGroupTitle = null)
    {
        ThrowIfReadOnly();
        var sorted = TextBufferRules.SortForApply(edits, Document.TextLength);
        using (OpenUndoGroup(undoGroupTitle))
        {
            foreach (var e in sorted)
            {
                if (e.Length == 0 && e.NewText.Length == 0) continue;
                Document.Replace(e.Offset, e.Length, e.NewText);
            }
        }
    }

    public void ReplaceAll(string text, bool resetUndo)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (resetUndo && _groupDepth > 0)
            throw new InvalidOperationException("Cannot reset the undo history while an undo group is open.");
        if (text != Document.Text)
            Document.Replace(0, Document.TextLength, text);
        if (resetUndo)
        {
            Document.UndoStack.ClearAll();
            MarkSavePoint();
        }
    }

    public ITextAnchor CreateAnchor(int offset, bool movesWithInsertionBefore)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, Document.TextLength);
        var anchor = Document.CreateAnchor(offset);
        anchor.MovementType = movesWithInsertionBefore ? AnchorMovementType.AfterInsertion : AnchorMovementType.BeforeInsertion;
        anchor.SurviveDeletion = false;
        return new Anchor(anchor);
    }

    public void Undo()
    {
        ThrowIfReadOnly();
        if (!Document.UndoStack.CanUndo) throw new InvalidOperationException("Nothing to undo.");
        Replay(Document.UndoStack.Undo, () =>
        {
            if (_side != SavePointSide.Lost)
                _side = Document.UndoStack.IsOriginalFile ? SavePointSide.At
                    : _side is SavePointSide.At or SavePointSide.Below ? SavePointSide.Below : SavePointSide.Above;
        });
    }

    public void Redo()
    {
        ThrowIfReadOnly();
        if (!Document.UndoStack.CanRedo) throw new InvalidOperationException("Nothing to redo.");
        Replay(Document.UndoStack.Redo, () =>
        {
            if (_side != SavePointSide.Lost)
                _side = Document.UndoStack.IsOriginalFile ? SavePointSide.At
                    : _side is SavePointSide.At or SavePointSide.Above ? SavePointSide.Above : SavePointSide.Below;
        });
    }

    public void MarkSavePoint()
    {
        Document.UndoStack.MarkAsOriginalFile();
        _side = SavePointSide.At;
    }

    public IDisposable OpenUndoGroup(string? title = null)
    {
        Document.UndoStack.StartUndoGroup(title);
        _groupDepth++;
        return new GroupScope(this);
    }

    private void CloseUndoGroup()
    {
        _groupDepth--;
        Document.UndoStack.EndUndoGroup();
    }

    /// <summary>
    /// Runs an undo/redo, then <paramref name="updateSavePoint"/>, and only then raises the replayed
    /// <see cref="Changed"/> events (in order), so listeners see the new <see cref="IsAtSavePoint"/> — AvaloniaEdit
    /// updates <see cref="UndoStack.IsOriginalFile"/> only after the document changed.
    /// </summary>
    private void Replay(Action undoOrRedo, Action updateSavePoint)
    {
        _replaying = true;
        try
        {
            undoOrRedo();
        }
        finally
        {
            _replaying = false;
            updateSavePoint();
        }

        var replayed = _replayed.ToList();
        _replayed.Clear();
        foreach (var change in replayed)
        {
            Changed?.Invoke(this, change);
        }
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        // A new edit (not undo/redo) leaves the save point; made below it, the save point can never be reached again.
        if (!_replaying)
            _side = _side is SavePointSide.Below or SavePointSide.Lost ? SavePointSide.Lost : SavePointSide.Above;
        Version++;
        var change = new Core.Text.TextChangedEventArgs(e.Offset, e.RemovalLength, e.InsertedText.Text);
        if (_replaying)
            _replayed.Add(change);
        else
            Changed?.Invoke(this, change);
    }

    private void ThrowIfReadOnly()
    {
        if (IsReadOnly) throw new InvalidOperationException("The buffer is read-only.");
    }

    private sealed class GroupScope(AvaloniaEditTextBuffer owner) : IDisposable
    {
        private AvaloniaEditTextBuffer? _owner = owner;

        public void Dispose()
        {
            _owner?.CloseUndoGroup();
            _owner = null;
        }
    }

    private sealed class Anchor(TextAnchor anchor) : ITextAnchor
    {
        public int Offset => anchor.IsDeleted ? throw new InvalidOperationException("The anchor was deleted.") : anchor.Offset;
        public bool IsDeleted => anchor.IsDeleted;
    }
}

/// <summary>Creates <see cref="AvaloniaEditTextBuffer"/>s (production <see cref="ITextBufferFactory"/>).</summary>
public sealed class AvaloniaEditTextBufferFactory : ITextBufferFactory
{
    public ITextBuffer Create(string text, LineEndingKind defaultLineEnding) => new AvaloniaEditTextBuffer(text, defaultLineEnding);
}
