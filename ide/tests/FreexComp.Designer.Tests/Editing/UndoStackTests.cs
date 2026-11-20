using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Editing;

/// <summary><see cref="DesignerUndoStack"/> (form-document.md §8; D-021).</summary>
public sealed class UndoStackTests
{
    private static readonly ComponentCatalog Catalog = TestCatalog.Instance;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-091")]
    [Trait("Req", "DSG-FD-093")]
    public void UndoRedoAndSavePoint()
    {
        var document = FormParser.Parse(File.ReadAllText(TestPaths.Combine("forms/canonical/hello.form")), "hello.form", Catalog).Document!;
        var editor = new FormEditor(document, Catalog);
        var stack = new DesignerUndoStack();
        var original = FormWriter.Write(document);

        // A fresh stack is at its save point (an unmodified buffer) and has nothing to undo.
        Assert.Equal((false, false, true), (stack.CanUndo, stack.CanRedo, stack.IsAtSavePoint));
        Assert.Null(stack.Undo());
        Assert.Null(stack.Redo());

        var move = Unit(editor, "Undo_Designer_Move", new SetProperty("GreetButton", "Left", new IntegerValue(240, false)));
        stack.Push(move);
        Assert.Equal((true, false, false), (stack.CanUndo, stack.CanRedo, stack.IsAtSavePoint));
        var afterMove = FormWriter.Write(document);

        var rename = Unit(editor, "Undo_Designer_Rename", new RenameObject("GreetButton", "OkButton"),
            new SetProperty("OkButton", "Caption", new StringValue("OK")));
        stack.Push(rename);
        stack.MarkSaved(); // saved with both units applied
        Assert.True(stack.IsAtSavePoint);
        Assert.NotNull(stack.SavePoint);
        var saved = FormWriter.Write(document);

        // Undo returns the newest unit; the caller applies its inverses (already in apply order).
        var undone = stack.Undo();
        Assert.Same(rename, undone);
        editor.Apply(undone!.Undo);
        Assert.Equal(afterMove, FormWriter.Write(document));
        Assert.Equal((true, true, false), (stack.CanUndo, stack.CanRedo, stack.IsAtSavePoint));
        Assert.Equal("GreetButton", Assert.Single(undone.SelectionBefore.Names));
        Assert.Equal("OkButton", undone.SelectionAfter.Primary);

        Assert.Same(move, stack.Undo());
        editor.Apply(move.Undo);
        Assert.Equal(original, FormWriter.Write(document));
        Assert.Equal((false, true, false), (stack.CanUndo, stack.CanRedo, stack.IsAtSavePoint));

        // Redo in the original order; back at the save point the buffer is unmodified again.
        Assert.Same(move, stack.Redo());
        editor.Apply(move.Redo);
        Assert.Same(rename, stack.Redo());
        editor.Apply(rename.Redo);
        Assert.Equal(saved, FormWriter.Write(document));
        Assert.Equal((true, false, true), (stack.CanUndo, stack.CanRedo, stack.IsAtSavePoint));

        // A new unit after an undo clears the redo list; the saved state is then unreachable (modified until saved).
        editor.Apply(stack.Undo()!.Undo);
        stack.Push(Unit(editor, "Undo_Designer_Property", new SetProperty("GreetButton", "Caption", new StringValue("Hi"))));
        Assert.False(stack.CanRedo);
        Assert.False(stack.IsAtSavePoint);
        editor.Apply(stack.Undo()!.Undo);
        Assert.False(stack.IsAtSavePoint); // the state after "move" was never saved
        stack.MarkSaved();
        Assert.True(stack.IsAtSavePoint);
        stack.Redo();
        Assert.False(stack.IsAtSavePoint);
        stack.Undo();
        Assert.True(stack.IsAtSavePoint);
        Assert.Equal(2, stack.UndoCount + stack.RedoCount);

        // DSG-FD-093 / DSG-FD-091: clearing (model replaced) loses the save point; modified until the next save.
        stack.Clear();
        Assert.Equal((false, false, false), (stack.CanUndo, stack.CanRedo, stack.IsAtSavePoint));
        stack.MarkSaved();
        Assert.True(stack.IsAtSavePoint);
        Assert.Empty(move.CodeEdits);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-090")]
    [Trait("Req", "DSG-FD-091")]
    public void DropsOldestBeyondLimit()
    {
        var stack = new DesignerUndoStack();
        var units = Enumerable.Range(0, DesignerUndoStack.MaxDepth + 3).Select(i => Unit("Undo_Designer_Move", i)).ToList();
        stack.MarkSaved(); // saved before any change
        foreach (var unit in units)
        {
            stack.Push(unit);
        }

        // DSG-FD-090: 500 units at most; the oldest ones are dropped.
        Assert.Equal(DesignerUndoStack.MaxDepth, stack.UndoCount);
        var undone = new List<DesignerUndoUnit>();
        while (stack.Undo() is { } unit)
        {
            undone.Add(unit);
        }

        Assert.Equal(units.Skip(3).Reverse(), undone);
        Assert.Equal(DesignerUndoStack.MaxDepth, stack.RedoCount);

        // DSG-FD-091: the dropped save point (before the first unit) can no longer be reached: the buffer stays modified.
        Assert.False(stack.IsAtSavePoint);

        // A save point that is the oldest remaining state is still recognised after units below it were dropped.
        var second = new DesignerUndoStack();
        foreach (var unit in units.Take(3))
        {
            second.Push(unit);
        }

        second.Undo();
        second.Undo();
        second.MarkSaved(); // saved with only units[0] applied
        second.Redo();
        second.Redo();
        foreach (var unit in units.Skip(3).Take(DesignerUndoStack.MaxDepth - 2))
        {
            second.Push(unit); // drops units[0]: the saved state is now the bottom of the stack
        }

        Assert.Equal(DesignerUndoStack.MaxDepth, second.UndoCount);
        var undoCount = 0;
        while (second.Undo() is not null)
        {
            undoCount++;
        }

        Assert.Equal(DesignerUndoStack.MaxDepth, undoCount);
        Assert.True(second.IsAtSavePoint);
    }

    /// <summary>A unit whose changes were just applied through <paramref name="editor"/>.</summary>
    private static DesignerUndoUnit Unit(FormEditor editor, string titleKey, params FormChange[] changes)
    {
        var before = changes[0] switch { RenameObject r => r.OldName, SetProperty p => p.ObjectName, _ => "" };
        var after = changes.OfType<RenameObject>().Select(c => c.NewName).DefaultIfEmpty(before).First();
        var inverses = editor.Apply(changes);
        return new DesignerUndoUnit(titleKey, changes, inverses, [], new SelectionSnapshot([before], before), new SelectionSnapshot([after], after));
    }

    private static DesignerUndoUnit Unit(string titleKey, int i) =>
        new(titleKey, [new SetProperty("B", "Left", new IntegerValue(i, false))], [new SetProperty("B", "Left", new IntegerValue(i - 1, false))],
            [], SelectionSnapshot.Empty, SelectionSnapshot.Empty);
}
