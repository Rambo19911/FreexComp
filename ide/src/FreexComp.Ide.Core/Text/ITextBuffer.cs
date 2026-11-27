using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Text;

/// <summary>A replacement of <see cref="Length"/> UTF-16 units at <see cref="Offset"/> by <see cref="NewText"/>.</summary>
public readonly record struct TextEdit(int Offset, int Length, string NewText);

/// <summary>1-based line and 1-based column (UTF-16 units).</summary>
public readonly record struct TextPosition(int Line, int Column);

/// <summary>One change of a buffer: <see cref="RemovedLength"/> units at <see cref="Offset"/> replaced by <see cref="InsertedText"/>.</summary>
public sealed class TextChangedEventArgs(int offset, int removedLength, string insertedText) : EventArgs
{
    public int Offset { get; } = offset;
    public int RemovedLength { get; } = removedLength;
    public string InsertedText { get; } = insertedText;
}

/// <summary>
/// A position that follows edits (AvaloniaEdit's rules). A pure insertion exactly at the anchor moves it only when
/// it was created with <c>movesWithInsertionBefore</c>. For a removal or replacement of [start, end): an anchor at
/// start stays, one strictly inside is deleted, one at end or later shifts (an anchor at end lands after the inserted
/// text), whatever its insertion rule. <see cref="Offset"/> of a deleted anchor throws
/// <see cref="InvalidOperationException"/>. Undo does not revive a deleted anchor.
/// </summary>
public interface ITextAnchor
{
    int Offset { get; }
    bool IsDeleted { get; }
}

/// <summary>
/// Text of one document (architecture.md §5.1). Implementations: <see cref="StringTextBuffer"/> (Core) and
/// <c>AvaloniaEditTextBuffer</c> (FreexComp.Ide); both pass <c>TextBufferContractTests</c>. UI thread only (IDE-ARCH-033).
/// Lines end at CRLF, LF or a lone CR (the editor's line model).
/// </summary>
public interface ITextBuffer
{
    /// <summary>Snapshot of the whole text; O(n).</summary>
    string Text { get; }

    /// <summary>
    /// The text as it is now, to be read later: taking the snapshot does not copy the text (IDE-P1-128), calling the
    /// result yields it (O(n) for the editor's buffer) and is allowed on any thread, any number of times.
    /// </summary>
    Func<string> CreateSnapshot();
    int Length { get; }
    int LineCount { get; }
    string GetText(int offset, int length);
    TextPosition GetPosition(int offset);

    /// <summary>Offset of a 1-based position; the column is clamped to the line (1 … length + 1).</summary>
    int GetOffset(int line, int column);

    (int Offset, int Length, int DelimiterLength) GetLine(int line);

    /// <summary>Dominant line ending: CRLF if CRLF count ≥ lone-LF count; the default given at creation when there is neither.</summary>
    LineEndingKind LineEnding { get; }

    /// <summary>+1 per change (one per <see cref="Changed"/> event).</summary>
    long Version { get; }

    /// <summary>When set, <see cref="ApplyEdits"/>, <see cref="Undo"/> and <see cref="Redo"/> throw <see cref="InvalidOperationException"/>.</summary>
    bool IsReadOnly { get; set; }

    /// <summary>Non-overlapping edits in any order, applied by descending offset as one undo step (IDE-ARCH-032).</summary>
    void ApplyEdits(IReadOnlyList<TextEdit> edits, string? undoGroupTitle = null);

    /// <summary>Replaces the whole text: one undo step, or (<paramref name="resetUndo"/>) a cleared history at the save point.</summary>
    void ReplaceAll(string text, bool resetUndo);

    /// <summary><paramref name="movesWithInsertionBefore"/>: text inserted exactly at the anchor puts it after that text.</summary>
    ITextAnchor CreateAnchor(int offset, bool movesWithInsertionBefore);

    bool CanUndo { get; }
    bool CanRedo { get; }
    void Undo();
    void Redo();
    void MarkSavePoint();
    bool IsAtSavePoint { get; }

    /// <summary>Changes until disposal form one undo step; nested groups merge into the outermost.</summary>
    IDisposable OpenUndoGroup(string? title = null);

    /// <summary>
    /// As <see cref="OpenUndoGroup"/>, but the changes join the previous undo step (typing grouping, IDE-P1-091) when
    /// the last thing that happened was closing that step: not after an undo, a redo, <see cref="MarkSavePoint"/> or a
    /// reset of the history.
    /// </summary>
    IDisposable OpenContinuedUndoGroup();

    event EventHandler<TextChangedEventArgs>? Changed;
}
