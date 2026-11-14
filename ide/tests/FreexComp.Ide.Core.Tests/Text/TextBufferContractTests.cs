using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Text;

/// <summary>
/// The behaviour every <see cref="ITextBuffer"/> implementation must have (architecture.md §5.1, IDE-ARCH-030).
/// Derived classes create the implementation under test: <c>StringTextBufferContractTests</c> here and
/// <c>AvaloniaEditTextBufferContractTests</c> in FreexComp.Ide.Tests (this file is linked there). Buffers are
/// created inside each test method, on the thread that runs it.
/// </summary>
[Trait("Phase", "1")]
public abstract class TextBufferContractTests
{
    protected abstract ITextBuffer CreateBuffer(string text, LineEndingKind defaultLineEnding = LineEndingKind.CrLf);

    private static List<TextChangedEventArgs> Record(ITextBuffer buffer)
    {
        var changes = new List<TextChangedEventArgs>();
        buffer.Changed += (_, e) => changes.Add(e);
        return changes;
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-032")]
    public void ApplyEditsIsOneUndoStep()
    {
        var b = CreateBuffer("one two three");
        var changes = Record(b);
        long v0 = b.Version;
        Assert.False(b.CanUndo);

        // Any order, non-overlapping: applied by descending offset, one Changed event per edit.
        b.ApplyEdits([new TextEdit(0, 3, "1"), new TextEdit(8, 5, "3"), new TextEdit(4, 3, "2")], "Renumber");
        Assert.Equal("1 2 3", b.Text);
        Assert.Equal(5, b.Length);
        Assert.Equal([8, 4, 0], changes.Select(c => c.Offset));
        Assert.Equal([5, 3, 3], changes.Select(c => c.RemovedLength));
        Assert.Equal(["3", "2", "1"], changes.Select(c => c.InsertedText));
        Assert.Equal(v0 + 3, b.Version);
        Assert.True(b.CanUndo);

        changes.Clear();
        b.Undo();
        Assert.Equal("one two three", b.Text);
        Assert.Equal(3, changes.Count);
        Assert.False(b.CanUndo);
        Assert.True(b.CanRedo);

        b.Redo();
        Assert.Equal("1 2 3", b.Text);
        Assert.False(b.CanRedo);

        // An explicit group with several edits (nested groups merge into the outermost) is one step too.
        using (b.OpenUndoGroup("outer"))
        {
            b.ApplyEdits([new TextEdit(0, 0, "a")]);
            using (b.OpenUndoGroup())
                b.ApplyEdits([new TextEdit(b.Length, 0, "z")]);
        }
        Assert.Equal("a1 2 3z", b.Text);
        b.Undo();
        Assert.Equal("1 2 3", b.Text);
        b.Undo();
        Assert.Equal("one two three", b.Text);

        // An empty group adds no step.
        using (b.OpenUndoGroup()) { }
        Assert.False(b.CanUndo);

        // Overlapping, same-offset or out-of-range edits are rejected and change nothing.
        b.Redo();
        changes.Clear();
        Assert.Throws<ArgumentException>(() => b.ApplyEdits([new TextEdit(0, 3, "x"), new TextEdit(2, 1, "y")]));
        Assert.Throws<ArgumentException>(() => b.ApplyEdits([new TextEdit(1, 0, "x"), new TextEdit(1, 1, "y")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.ApplyEdits([new TextEdit(4, 2, "x")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.ApplyEdits([new TextEdit(-1, 0, "x")]));
        Assert.Equal("1 2 3", b.Text);
        Assert.Empty(changes);

        // A new edit after an undo clears the redo stack.
        b.Undo();
        b.ApplyEdits([new TextEdit(0, 0, ">")]);
        Assert.False(b.CanRedo);

        // ReplaceAll: one undo step, or a fresh history (at the save point) when resetUndo is set.
        b.ReplaceAll("new", resetUndo: false);
        Assert.Equal("new", b.Text);
        b.Undo();
        Assert.Equal(">one two three", b.Text);
        b.ReplaceAll("loaded", resetUndo: true);
        Assert.Equal("loaded", b.Text);
        Assert.False(b.CanUndo);
        Assert.False(b.CanRedo);
        Assert.True(b.IsAtSavePoint);

        // Read-only buffers reject edits.
        b.IsReadOnly = true;
        Assert.Throws<InvalidOperationException>(() => b.ApplyEdits([new TextEdit(0, 0, "x")]));
        Assert.Equal("loaded", b.Text);
        b.IsReadOnly = false;
        b.ApplyEdits([new TextEdit(0, 0, "x")]);
        Assert.Equal("xloaded", b.Text);
    }

    [Fact(Timeout = 30000)]
    public void AnchorsTrackEdits()
    {
        var b = CreateBuffer("0123456789");
        var stays = b.CreateAnchor(5, movesWithInsertionBefore: false);
        var moves = b.CreateAnchor(5, movesWithInsertionBefore: true);
        var before = b.CreateAnchor(2, movesWithInsertionBefore: false);
        var inside = b.CreateAnchor(7, movesWithInsertionBefore: false);
        var atStart = b.CreateAnchor(6, movesWithInsertionBefore: false);
        var atEnd = b.CreateAnchor(9, movesWithInsertionBefore: true);
        var last = b.CreateAnchor(10, movesWithInsertionBefore: false);

        // Insertion exactly at the anchor: only the "moves" anchor goes after the inserted text.
        b.ApplyEdits([new TextEdit(5, 0, "ab")]);   // 01234ab56789
        Assert.Equal(5, stays.Offset);
        Assert.Equal(7, moves.Offset);
        Assert.Equal(2, before.Offset);
        Assert.Equal(9, inside.Offset);
        Assert.Equal(8, atStart.Offset);
        Assert.Equal(11, atEnd.Offset);
        Assert.Equal(12, last.Offset);

        // Deletion [8, 11): anchors strictly inside are deleted; those at its start or end survive at its start.
        b.ApplyEdits([new TextEdit(8, 3, "")]);     // 01234ab59
        Assert.False(atStart.IsDeleted);
        Assert.Equal(8, atStart.Offset);
        Assert.True(inside.IsDeleted);
        Assert.Throws<InvalidOperationException>(() => inside.Offset);
        Assert.False(atEnd.IsDeleted);
        Assert.Equal(8, atEnd.Offset);
        Assert.Equal(9, last.Offset);

        // Replacement (removal > 0; AvaloniaEdit's rule): an anchor at the range's start stays there and one at its
        // end goes after the inserted text, whatever their insertion rule.
        b.ApplyEdits([new TextEdit(7, 1, "XYZ")]);  // 01234abXYZ9
        Assert.Equal(10, atStart.Offset); // at 8 = end of [7,8)
        Assert.Equal(10, atEnd.Offset);   // at 8 = end of [7,8)
        Assert.Equal(7, moves.Offset);    // at 7 = start of [7,8)
        Assert.Equal(5, stays.Offset);
        Assert.Equal(11, last.Offset);
        Assert.Equal(2, before.Offset);

        // Undo moves anchors back through the inverse changes; a deleted anchor stays deleted.
        b.Undo();
        b.Undo();
        Assert.Equal("01234ab56789", b.Text);
        Assert.True(inside.IsDeleted);
        Assert.Equal(12, last.Offset);
        Assert.Equal(2, before.Offset);

        Assert.Throws<ArgumentOutOfRangeException>(() => b.CreateAnchor(13, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.CreateAnchor(-1, false));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-022")]
    [Trait("Req", "IDE-P1-094")]
    public void SavePointTracksUndoRedo()
    {
        var b = CreateBuffer("abc");
        Assert.True(b.IsAtSavePoint);

        b.ApplyEdits([new TextEdit(3, 0, "d")]);
        Assert.False(b.IsAtSavePoint);
        b.Undo();
        Assert.True(b.IsAtSavePoint);
        b.Redo();
        Assert.False(b.IsAtSavePoint);

        b.MarkSavePoint();                          // saved "abcd"; the undo stack is kept
        Assert.True(b.IsAtSavePoint);
        Assert.True(b.CanUndo);
        b.Undo();
        Assert.False(b.IsAtSavePoint);              // undoing past the save point
        b.Redo();
        Assert.True(b.IsAtSavePoint);               // returning to it

        b.ApplyEdits([new TextEdit(4, 0, "e")]);
        b.ApplyEdits([new TextEdit(5, 0, "f")]);
        b.Undo();
        b.Undo();
        Assert.True(b.IsAtSavePoint);

        // Undo below the save point, then a new edit: the save point can no longer be reached.
        b.Undo();
        Assert.Equal("abc", b.Text);
        b.ApplyEdits([new TextEdit(3, 0, "d")]);
        Assert.Equal("abcd", b.Text);
        Assert.False(b.IsAtSavePoint);
        b.Undo();
        Assert.False(b.IsAtSavePoint);

        b.ReplaceAll("abcd", resetUndo: false);
        Assert.False(b.IsAtSavePoint);
        b.ReplaceAll("abcd", resetUndo: true);
        Assert.True(b.IsAtSavePoint);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-022")]
    public void SavePointIsCurrentInsideChangedDuringUndoRedo()
    {
        // The modified marker reads IsAtSavePoint from Changed; it must already reflect the undo/redo.
        var b = CreateBuffer("abc");
        b.MarkSavePoint();
        b.ApplyEdits([new TextEdit(0, 0, "x"), new TextEdit(3, 0, "y")]);
        var seen = new List<bool>();
        b.Changed += (_, _) => seen.Add(b.IsAtSavePoint);

        b.Undo();
        Assert.Equal([true, true], seen);
        seen.Clear();
        b.Redo();
        Assert.Equal([false, false], seen);

        // A new edit made at the save point has already left it inside Changed (the change tracker reads it, I-046).
        b.Undo();
        seen.Clear();
        b.ApplyEdits([new TextEdit(0, 0, "z")]);
        Assert.Equal([false], seen);
    }

    [Fact(Timeout = 30000)]
    public void PositionOffsetConversions()
    {
        // Line breaks are CRLF, LF and a lone CR (AvaloniaEdit's line model); columns count UTF-16 units.
        const string text = "ab\r\nc\U0001F600d\nxyz\rlast";
        var b = CreateBuffer(text);
        Assert.Equal(text, b.Text);
        Assert.Equal(text.Length, b.Length);
        Assert.Equal(4, b.LineCount);

        Assert.Equal((0, 2, 2), b.GetLine(1));
        Assert.Equal((4, 4, 1), b.GetLine(2));      // c + surrogate pair + d
        Assert.Equal((9, 3, 1), b.GetLine(3));
        Assert.Equal((13, 4, 0), b.GetLine(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.GetLine(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.GetLine(5));

        Assert.Equal(new TextPosition(1, 1), b.GetPosition(0));
        Assert.Equal(new TextPosition(1, 3), b.GetPosition(2));     // before CR
        Assert.Equal(new TextPosition(2, 1), b.GetPosition(4));
        Assert.Equal(new TextPosition(2, 4), b.GetPosition(7));     // after the surrogate pair
        Assert.Equal(new TextPosition(3, 4), b.GetPosition(12));
        Assert.Equal(new TextPosition(4, 5), b.GetPosition(17));    // end of text
        Assert.Throws<ArgumentOutOfRangeException>(() => b.GetPosition(18));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.GetPosition(-1));

        Assert.Equal(0, b.GetOffset(1, 1));
        Assert.Equal(7, b.GetOffset(2, 4));
        Assert.Equal(8, b.GetOffset(2, 5));
        Assert.Equal(8, b.GetOffset(2, 99));        // column clamped to the line end
        Assert.Equal(4, b.GetOffset(2, 0));         // and to the line start
        Assert.Equal(17, b.GetOffset(4, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.GetOffset(5, 1));
        for (int offset = 0; offset <= b.Length; offset++)
        {
            if (offset == 3) continue;                // between CR and LF: not a caret position
            var p = b.GetPosition(offset);
            Assert.Equal(offset, b.GetOffset(p.Line, p.Column));
        }

        Assert.Equal("c\U0001F600d", b.GetText(4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.GetText(15, 3));

        // Line model follows edits; a text ending in a break has an empty last line.
        b.ApplyEdits([new TextEdit(b.Length, 0, "\r\n")]);
        Assert.Equal(5, b.LineCount);
        Assert.Equal((19, 0, 0), b.GetLine(5));
        b.ReplaceAll("", resetUndo: true);
        Assert.Equal(1, b.LineCount);
        Assert.Equal((0, 0, 0), b.GetLine(1));
        Assert.Equal(new TextPosition(1, 1), b.GetPosition(0));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-075")]
    public void LineEndingIsDominantKind()
    {
        Assert.Equal(LineEndingKind.CrLf, CreateBuffer("a\r\nb\nc\r\n").LineEnding);
        Assert.Equal(LineEndingKind.CrLf, CreateBuffer("a\r\nb\n").LineEnding);   // equal counts → CRLF
        Assert.Equal(LineEndingKind.Lf, CreateBuffer("a\nb\nc\r\n").LineEnding);
        Assert.Equal(LineEndingKind.Lf, CreateBuffer("a\rb\nc").LineEnding);      // a lone CR is not counted
        Assert.Equal(LineEndingKind.Lf, CreateBuffer("no break", LineEndingKind.Lf).LineEnding);
        Assert.Equal(LineEndingKind.CrLf, CreateBuffer("", LineEndingKind.CrLf).LineEnding);

        var b = CreateBuffer("a\nb", LineEndingKind.CrLf);
        Assert.Equal(LineEndingKind.Lf, b.LineEnding);
        b.ApplyEdits([new TextEdit(1, 1, "\r\n")]);
        Assert.Equal(LineEndingKind.CrLf, b.LineEnding);
    }
}
