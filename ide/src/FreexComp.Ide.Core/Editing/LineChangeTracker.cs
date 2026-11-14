using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// Change-bar state per line (IDE-ED-045/046, UI-ED-041). A line becomes <see cref="LineChangeState.Modified"/> when a
/// character or its line break changes or when it is inserted; deleting lines marks the line after the deletion.
/// <see cref="MarkSaved"/> turns Modified into Saved and remembers the states; whenever the buffer is back at its save
/// point after a change (undo/redo), those states return. <see cref="Reset"/> (revert, reload) clears every mark.
/// The tracker keeps each line's length (with its break) so it can tell where a change ended in the old text.
/// </summary>
public sealed class LineChangeTracker : ILineChangeSource
{
    private readonly ITextBuffer _buffer;
    private readonly List<LineChangeState> _states = [];
    private readonly List<int> _lengths = [];
    private LineChangeState[] _savedStates = [];

    public LineChangeTracker(ITextBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Fill();
        buffer.Changed += OnChanged;
    }

    public event EventHandler? Changed;

    public LineChangeState StateOf(int line) => line >= 1 && line <= _states.Count ? _states[line - 1] : LineChangeState.None;

    /// <summary>The document was saved: every Modified line becomes Saved; these states are the save point's.</summary>
    public void MarkSaved()
    {
        for (int i = 0; i < _states.Count; i++)
        {
            if (_states[i] == LineChangeState.Modified)
                _states[i] = LineChangeState.Saved;
        }

        _savedStates = [.. _states];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Revert / reload: no line has a mark.</summary>
    public void Reset()
    {
        Fill();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Fill()
    {
        _states.Clear();
        _lengths.Clear();
        for (int line = 1; line <= _buffer.LineCount; line++)
        {
            _states.Add(LineChangeState.None);
            _lengths.Add(LengthOf(line));
        }

        _savedStates = [.. _states];
    }

    private int LengthOf(int line)
    {
        var (_, length, delimiter) = _buffer.GetLine(line);
        return length + delimiter;
    }

    private void OnChanged(object? sender, TextChangedEventArgs e)
    {
        // Old lines [first, last] are replaced; the text before the change and after it is unchanged.
        int first = 0, firstStart = 0;
        while (first < _lengths.Count - 1 && firstStart + _lengths[first] <= e.Offset)
            firstStart += _lengths[first++];
        int end = e.Offset + e.RemovedLength, last = first, lastStart = firstStart;
        while (last < _lengths.Count - 1 && lastStart + _lengths[last] <= end)
            lastStart += _lengths[last++];
        bool oldEndAtLineStart = end == lastStart;

        // A break merged with the previous line's lone CR: start one line earlier.
        int newFirstLine = _buffer.GetPosition(Math.Min(e.Offset, _buffer.Length)).Line;
        if (newFirstLine - 1 < first)
        {
            first = newFirstLine - 1;
            oldEndAtLineStart = false;
        }

        int newCount = Math.Max(1, _buffer.LineCount - (_lengths.Count - (last - first + 1)));
        var newEnd = _buffer.GetPosition(Math.Min(e.Offset + e.InsertedText.Length, _buffer.Length));

        // The last replaced line is untouched when the change ended at a line start in both texts — unless the change
        // was a pure deletion, which marks the line after it (IDE-ED-045).
        bool keepLast = oldEndAtLineStart && newEnd.Column == 1 && e.InsertedText.Length > 0 && newCount > 1;
        var lastState = _states[last];
        _states.RemoveRange(first, last - first + 1);
        _lengths.RemoveRange(first, last - first + 1);
        for (int k = 0; k < newCount; k++)
        {
            int line = first + k + 1;
            _states.Insert(first + k, keepLast && k == newCount - 1 ? lastState : LineChangeState.Modified);
            _lengths.Insert(first + k, line <= _buffer.LineCount ? LengthOf(line) : 0);
        }

        // IDE-ED-046: back at the save point (undo/redo), the save point's states return.
        if (_buffer.IsAtSavePoint && _savedStates.Length == _states.Count)
        {
            _states.Clear();
            _states.AddRange(_savedStates);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
