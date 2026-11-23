using System.Collections.Immutable;

namespace FreexComp.Designer.Editing;

/// <summary>
/// One text edit applied to the unit's code buffer by code sync (code-sync.md §1, §10): offsets valid at the time the
/// edit was applied, in application order. Declared here because <see cref="DesignerUndoUnit"/> holds them and this
/// assembly cannot reference the IDE core (DSG-ARCH-001).
/// </summary>
public sealed record TextEditRecord(int Offset, string RemovedText, string InsertedText);

/// <summary>The selected component names (form-document.md §8, DSG-FD-092); <paramref name="Primary"/> is the primary one.</summary>
public sealed record SelectionSnapshot(ImmutableArray<string> Names, string? Primary)
{
    public static SelectionSnapshot Empty { get; } = new([], null);
}

/// <summary>One undoable designer interaction (granularity: design-surface.md §12, object-inspector.md §9).</summary>
public sealed class DesignerUndoUnit(string titleKey, IReadOnlyList<FormChange> redo, IReadOnlyList<FormChange> undo,
    IReadOnlyList<TextEditRecord> codeEdits, SelectionSnapshot selectionBefore, SelectionSnapshot selectionAfter)
{
    /// <summary>Resource key of the Edit-menu title, e.g. <c>Undo_Designer_Move</c> (DSG-SUR-172).</summary>
    public string TitleKey { get; } = titleKey;

    /// <summary>The argument of the title, e.g. the class name of "Add {ClassName}" (design-surface.md §12); null when none.</summary>
    public string? TitleArgument { get; init; }

    /// <summary>The forward changes.</summary>
    public IReadOnlyList<FormChange> Redo { get; } = redo;

    /// <summary>The inverses, already in apply order.</summary>
    public IReadOnlyList<FormChange> Undo { get; } = undo;

    /// <summary>Code-sync edits (may be empty).</summary>
    public IReadOnlyList<TextEditRecord> CodeEdits { get; } = codeEdits;

    public SelectionSnapshot SelectionBefore { get; } = selectionBefore;

    public SelectionSnapshot SelectionAfter { get; } = selectionAfter;
}

/// <summary>
/// The designer's undo history (form-document.md §8): at most <see cref="MaxDepth"/> units, the oldest dropped
/// (DSG-FD-090), and a save point that tells whether the form buffer is modified (DSG-FD-091). The stack only keeps the
/// units; the caller applies <see cref="DesignerUndoUnit.Undo"/> / <see cref="DesignerUndoUnit.Redo"/> through the editor.
/// </summary>
public sealed class DesignerUndoStack
{
    /// <summary>DSG-FD-090: the stack depth.</summary>
    public const int MaxDepth = 500;

    // Oldest first; the redo list holds the next unit to redo last.
    private readonly LinkedList<DesignerUndoUnit> _undo = new();
    private readonly Stack<DesignerUndoUnit> _redo = new();

    // A state is identified by the newest applied unit, or by this token for the state below all units. Dropping the
    // oldest unit makes the state after it the new bottom, so a save point below a dropped unit is never reached again.
    private object _bottom = new();

    public DesignerUndoStack() => SavePoint = _bottom;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    /// <summary>The title resource key of the unit <see cref="Undo"/> would revert (DSG-SUR-172: Edit menu "Undo Move"); null when empty.</summary>
    public string? UndoTitleKey => _undo.Last?.Value.TitleKey;

    /// <summary>The state marked by <see cref="MarkSaved"/>; a new stack starts at its save point (an unmodified buffer).</summary>
    public object? SavePoint { get; private set; }

    /// <summary>The buffer's modified flag is the negation of this (DSG-FD-091).</summary>
    public bool IsAtSavePoint => ReferenceEquals(SavePoint, Current);

    private object Current => _undo.Last?.Value ?? _bottom;

    /// <summary>Adds a unit whose changes were just applied; clears the redo list and drops the oldest beyond the depth.</summary>
    public void Push(DesignerUndoUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        _redo.Clear();
        _undo.AddLast(unit);
        while (_undo.Count > MaxDepth)
        {
            _bottom = _undo.First!.Value;
            _undo.RemoveFirst();
        }
    }

    /// <summary>The unit <see cref="Undo"/> would revert, or null.</summary>
    public DesignerUndoUnit? Peek => _undo.Last?.Value;

    /// <summary>
    /// Replaces the newest unit by <paramref name="unit"/> (merging keyboard nudges, design-surface.md DSG-SUR-107); a save
    /// point at the replaced unit is not carried over (the buffer differs from the saved one).
    /// </summary>
    public void ReplaceLast(DesignerUndoUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_undo.Last is not { } last)
        {
            throw new InvalidOperationException("no unit to replace");
        }

        _redo.Clear();
        last.Value = unit;
    }

    /// <summary>The unit to undo (moved to the redo list), or null.</summary>
    public DesignerUndoUnit? Undo()
    {
        if (_undo.Last is not { } last)
        {
            return null;
        }

        _undo.RemoveLast();
        _redo.Push(last.Value);
        return last.Value;
    }

    /// <summary>The unit to redo (moved back to the undo list), or null.</summary>
    public DesignerUndoUnit? Redo()
    {
        if (!_redo.TryPop(out var unit))
        {
            return null;
        }

        _undo.AddLast(unit);
        return unit;
    }

    /// <summary>DSG-FD-093: forgets every unit (model replaced); the save point is lost until the next save.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _bottom = new object();
    }

    public void MarkSaved() => SavePoint = Current;
}
