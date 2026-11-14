using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Tests.Editing;

[Trait("Phase", "1")]
public sealed class LineChangeTrackerTests
{
    private const LineChangeState N = LineChangeState.None, M = LineChangeState.Modified, S = LineChangeState.Saved;

    private static LineChangeState[] States(ILineChangeSource source, ITextBuffer buffer) =>
        [.. Enumerable.Range(1, buffer.LineCount).Select(source.StateOf)];

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-045")]
    [Trait("Req", "IDE-ED-046")]
    [Trait("Req", "UI-ED-041")]
    public void StatesAcrossEditSaveUndo()
    {
        var buffer = new StringTextBuffer("a\nb\nc\nd");
        var tracker = new LineChangeTracker(buffer);
        int changed = 0;
        tracker.Changed += (_, _) => changed++;
        Assert.Equal([N, N, N, N], States(tracker, buffer));

        // A changed character marks its line; an inserted line is marked, the line it was inserted before is not.
        buffer.ApplyEdits([new TextEdit(2, 1, "B")]);
        Assert.Equal([N, M, N, N], States(tracker, buffer));
        Assert.True(changed > 0);
        buffer.ApplyEdits([new TextEdit(buffer.GetLine(4).Offset, 0, "x\n")]);
        Assert.Equal("a\nB\nc\nx\nd", buffer.Text);
        Assert.Equal([N, M, N, M, N], States(tracker, buffer));

        // Save: every Modified line becomes Saved.
        buffer.MarkSavePoint();
        tracker.MarkSaved();
        Assert.Equal([N, S, N, S, N], States(tracker, buffer));

        // Deleting a line marks the line after the deletion.
        buffer.ApplyEdits([new TextEdit(0, 2, "")]);
        Assert.Equal("B\nc\nx\nd", buffer.Text);
        Assert.Equal([M, N, S, N], States(tracker, buffer));

        // Undo back to the save point restores the marks of the save point.
        buffer.Undo();
        Assert.True(buffer.IsAtSavePoint);
        Assert.Equal([N, S, N, S, N], States(tracker, buffer));

        // Undo below the save point: the touched line becomes Modified (not the state it had before).
        buffer.Undo();
        Assert.Equal("a\nB\nc\nd", buffer.Text);
        Assert.Equal([N, S, N, M], States(tracker, buffer));

        // Redo returns to the save point.
        buffer.Redo();
        Assert.True(buffer.IsAtSavePoint);
        Assert.Equal([N, S, N, S, N], States(tracker, buffer));

        // A changed line break marks its line; a multi-line replacement marks every line it produces.
        buffer.ApplyEdits([new TextEdit(5, 1, "\r\n")]);
        Assert.Equal([N, S, M, S, N], States(tracker, buffer));
        buffer.ApplyEdits([new TextEdit(0, 4, "1\n2\n3\n")]);
        Assert.Equal("1\n2\n3\nc\r\nx\nd", buffer.Text);
        Assert.Equal([M, M, M, M, S, N], States(tracker, buffer));

        // Lines outside the document have no mark; Reset clears every mark.
        Assert.Equal(N, tracker.StateOf(0));
        Assert.Equal(N, tracker.StateOf(99));
        tracker.Reset();
        Assert.Equal([N, N, N, N, N, N], States(tracker, buffer));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-045")]
    [Trait("Req", "UI-ED-041")]
    public void DocumentSaveAndRevertDriveTheTracker()
    {
        var buffer = new StringTextBuffer("one\ntwo\n");
        var document = new TextDocumentViewModel(buffer, @"C:\p\a.txt", null, new PlainTextLanguage(), FileEncoding.Utf8, LineEndingKind.Lf);
        Assert.IsType<LineChangeTracker>(document.LineChanges);

        Assert.True(document.TryInsertText(0, 0, "x"));
        Assert.Equal([M, N, N], States(document.LineChanges, buffer));
        document.MarkSaved(@"C:\p\a.txt", new PlainTextLanguage());
        Assert.Equal([S, N, N], States(document.LineChanges, buffer));

        Assert.True(document.TryInsertText(buffer.GetLine(2).Offset, 0, "y"));
        Assert.Equal([S, M, N], States(document.LineChanges, buffer));

        // Revert / reload resets every mark (IDE-ED-045).
        document.Reload("one\ntwo\nthree\n");
        Assert.Equal([N, N, N, N], States(document.LineChanges, buffer));
    }
}
