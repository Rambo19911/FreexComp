using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Text;

/// <summary>
/// Simple <see cref="ITextBuffer"/> over a string with its own undo stack (IDE-ARCH-030): Core tests and files loaded
/// without a view. Line starts are recomputed lazily after a change.
/// </summary>
public sealed class StringTextBuffer : ITextBuffer
{
    private readonly record struct Change(int Offset, string Removed, string Inserted);

    private readonly LineEndingKind _defaultLineEnding;
    private readonly List<List<Change>> _undo = [];
    private readonly List<List<Change>> _redo = [];
    private readonly List<Anchor> _anchors = [];
    private string _text;
    private int[]? _lineStarts;
    private List<Change>? _group;
    private int _groupDepth;
    private bool _replaying;
    // Undo-stack depth of the save point; -1 when it can no longer be reached.
    private int _savePoint;

    public StringTextBuffer(string text = "", LineEndingKind defaultLineEnding = LineEndingKind.CrLf)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _defaultLineEnding = defaultLineEnding;
    }

    public string Text => _text;
    public int Length => _text.Length;
    public int LineCount => LineStarts.Length;
    public long Version { get; private set; }
    public bool IsReadOnly { get; set; }
    public LineEndingKind LineEnding => TextBufferRules.DominantLineEnding(_text, _defaultLineEnding);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsAtSavePoint => _savePoint == _undo.Count;

    public event EventHandler<TextChangedEventArgs>? Changed;

    public string GetText(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > _text.Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return _text.Substring(offset, length);
    }

    public TextPosition GetPosition(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _text.Length);
        var starts = LineStarts;
        int index = Array.BinarySearch(starts, offset);
        if (index < 0) index = ~index - 1;
        return new TextPosition(index + 1, offset - starts[index] + 1);
    }

    public int GetOffset(int line, int column)
    {
        var (offset, length, _) = GetLine(line);
        return offset + Math.Clamp(column - 1, 0, length);
    }

    public (int Offset, int Length, int DelimiterLength) GetLine(int line)
    {
        var starts = LineStarts;
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(line, starts.Length);
        int start = starts[line - 1];
        if (line == starts.Length) return (start, _text.Length - start, 0);
        int next = starts[line];
        int delimiter = next - start >= 2 && _text[next - 1] == '\n' && _text[next - 2] == '\r' ? 2 : 1;
        return (start, next - start - delimiter, delimiter);
    }

    public void ApplyEdits(IReadOnlyList<TextEdit> edits, string? undoGroupTitle = null)
    {
        ThrowIfReadOnly();
        var sorted = TextBufferRules.SortForApply(edits, _text.Length);
        using (OpenUndoGroup(undoGroupTitle))
        {
            foreach (var e in sorted)
            {
                if (e.Length == 0 && e.NewText.Length == 0) continue;
                Apply(new Change(e.Offset, _text.Substring(e.Offset, e.Length), e.NewText));
            }
        }
    }

    public void ReplaceAll(string text, bool resetUndo)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (resetUndo && _groupDepth > 0)
            throw new InvalidOperationException("Cannot reset the undo history while an undo group is open.");
        if (text != _text)
        {
            using (OpenUndoGroup())
                Apply(new Change(0, _text, text));
        }
        if (resetUndo)
        {
            _undo.Clear();
            _redo.Clear();
            _savePoint = 0;
        }
    }

    public ITextAnchor CreateAnchor(int offset, bool movesWithInsertionBefore)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _text.Length);
        var anchor = new Anchor(offset, movesWithInsertionBefore);
        _anchors.Add(anchor);
        return anchor;
    }

    public void Undo()
    {
        ThrowIfReadOnly();
        ThrowIfGroupOpen();
        if (_undo.Count == 0) throw new InvalidOperationException("Nothing to undo.");
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(step); // before the replay: Changed handlers see the new save-point state
        Replay(step.AsEnumerable().Reverse().Select(c => new Change(c.Offset, c.Inserted, c.Removed)));
    }

    public void Redo()
    {
        ThrowIfReadOnly();
        ThrowIfGroupOpen();
        if (_redo.Count == 0) throw new InvalidOperationException("Nothing to redo.");
        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(step); // before the replay: Changed handlers see the new save-point state
        Replay(step);
    }

    public void MarkSavePoint() => _savePoint = _undo.Count;

    public IDisposable OpenUndoGroup(string? title = null)
    {
        if (_groupDepth++ == 0) _group = [];
        return new GroupScope(this);
    }

    private void CloseUndoGroup()
    {
        if (--_groupDepth > 0) return;
        var group = _group!;
        _group = null;
        if (group.Count == 0) return;
        if (_savePoint > _undo.Count) _savePoint = -1; // the save point was in the discarded redo history
        _redo.Clear();
        _undo.Add(group);
    }

    private void Replay(IEnumerable<Change> changes)
    {
        _replaying = true;
        try
        {
            foreach (var c in changes) Apply(c);
        }
        finally
        {
            _replaying = false;
        }
    }

    private void Apply(Change c)
    {
        _text = string.Concat(_text.AsSpan(0, c.Offset), c.Inserted, _text.AsSpan(c.Offset + c.Removed.Length));
        _lineStarts = null;
        foreach (var anchor in _anchors) anchor.OnChange(c.Offset, c.Removed.Length, c.Inserted.Length);
        _anchors.RemoveAll(a => a.IsDeleted);
        if (!_replaying) _group!.Add(c);
        Version++;
        Changed?.Invoke(this, new TextChangedEventArgs(c.Offset, c.Removed.Length, c.Inserted));
    }

    private int[] LineStarts => _lineStarts ??= ComputeLineStarts(_text);

    private static int[] ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            else if (ch != '\r' && ch != '\n') continue;
            starts.Add(i + 1);
        }
        return [.. starts];
    }

    private void ThrowIfReadOnly()
    {
        if (IsReadOnly) throw new InvalidOperationException("The buffer is read-only.");
    }

    private void ThrowIfGroupOpen()
    {
        if (_groupDepth > 0) throw new InvalidOperationException("Cannot undo or redo while an undo group is open.");
    }

    private sealed class GroupScope(StringTextBuffer owner) : IDisposable
    {
        private StringTextBuffer? _owner = owner;

        public void Dispose()
        {
            _owner?.CloseUndoGroup();
            _owner = null;
        }
    }

    private sealed class Anchor(int offset, bool movesWithInsertion) : ITextAnchor
    {
        private int _offset = offset;

        public bool IsDeleted { get; private set; }

        public int Offset => IsDeleted ? throw new InvalidOperationException("The anchor was deleted.") : _offset;

        public void OnChange(int offset, int removed, int inserted)
        {
            if (_offset < offset) return;
            if (removed == 0)
            {
                // Pure insertion: an anchor exactly at it follows its insertion rule.
                if (_offset > offset || movesWithInsertion) _offset += inserted;
                return;
            }
            // Removal or replacement (AvaloniaEdit's rule): at the start stays, strictly inside is deleted, at or
            // after the end shifts (so an anchor at the end lands after the inserted text).
            if (_offset == offset) return;
            if (_offset < offset + removed)
            {
                IsDeleted = true;
                return;
            }
            _offset += inserted - removed;
        }
    }
}
