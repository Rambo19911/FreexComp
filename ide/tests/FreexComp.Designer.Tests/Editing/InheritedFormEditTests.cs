using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Text;
using static FreexComp.Designer.Tests.Support.FrameFixtures;
using static FreexComp.Designer.Tests.Support.InheritanceFixtures;

namespace FreexComp.Designer.Tests.Editing;

/// <summary>Editing descendant form documents and following their ancestors (form-inheritance-designer.md §4; D-130).</summary>
public sealed class InheritedFormEditTests
{
    private static readonly FormObjectData NewButton = new(FormObjectKind.Object, "NewButton", "TButton",
    [
        new FormPropertyData("Left", new IntegerValue(100, false)), new FormPropertyData("Top", new IntegerValue(8, false)),
        new FormPropertyData("Width", new IntegerValue(75, false)), new FormPropertyData("Height", new IntegerValue(25, false)),
    ], []);

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-132")]
    [Trait("Req", "DSG-FD-133")]
    public void OverrideEqualToInheritedRemoved_StructureChangesRejected_AddIntoInheritedContainer()
    {
        var frames = AddressFrames();
        var ancestors = new AncestorCatalog();
        AddLevel(ancestors, BaseDialog, "BaseDialogUnit", "TForm");
        var text = MidDialog.Replace("""
              object NameEdit: TEdit
            """, """
              inline Addr: TAddressFrame
                Left = 8
                Top = 8
                Width = 320
                Height = 180
                TabOrder = 2
              end
              object NameEdit: TEdit
            """, StringComparison.Ordinal);
        var document = ParseWith(text, "TBaseDialog", frames, ancestors);
        Assert.Empty(document.Diagnostics);
        Assert.Equal(text, FormWriter.Write(document));
        var editor = new FormEditor(document, Components);
        var ok = document.FindByName("OkButton")!;

        // Overrides of inherited objects and of the inherited root: equal to the inherited value → removed.
        editor.Apply(new SetProperty("OkButton", "Caption", new StringValue("OK")));
        Assert.False(ok.IsOverridden("Caption"));
        editor.Apply(new SetProperty("MidDialog", "Caption", new StringValue("Base")));
        Assert.False(document.Root.IsOverridden("Caption"));
        editor.Apply(new SetProperty("MidDialog", "Left", new IntegerValue(200, false))); // root geometry: not always written
        editor.Apply(new SetProperty("MidDialog", "ClientWidth", new IntegerValue(400, false)));
        Assert.Empty(document.Root.Properties);
        var moved = editor.Apply(new SetProperty("OkButton", "Left", new IntegerValue(100, false)));
        Assert.Equal(("100", true), (Effective(ok, "Left"), ok.IsOverridden("Left")));
        editor.Apply(moved);
        Assert.False(ok.IsOverridden("Left"));
        editor.Apply(new SetProperty("OkButton", "Caption", new StringValue("Apply")));
        editor.Apply(new SetProperty("MidDialog", "Caption", new StringValue("Mid")));
        Assert.Equal(text, FormWriter.Write(document));

        // Structure changes of inherited objects and additions into frame instances are rejected and change nothing.
        var version = document.Version;
        FormChange[] rejected =
        [
            new RemoveObject("OkButton"),
            new RemoveObject("ButtonPanel"),
            new RenameObject("CancelButton", "CloseButton"),
            new MoveObject("OkButton", "MidDialog", 0),
            new MoveObject("CancelButton", "ButtonPanel", 0),
            new MoveObject("TitleLabel", "ButtonPanel", 2),
            new MoveObject("HelpButton", "ButtonPanel", 1), // DSG-FD-133: a new child stays after the inherited ones
            new MoveObject("NameEdit", "MidDialog", 0),
            new MoveObject("NameEdit", "ButtonPanel", 1),
            new AddObject("Addr", 0, NewButton),
            new AddObject("Addr.CityPanel", 0, NewButton),
            new MoveObject("NameEdit", "Addr", 0),
            new RemoveObject("Addr.StreetLabel"),
        ];
        foreach (var change in rejected)
        {
            Assert.Throws<InvalidFormChangeException>(() => editor.Apply(change));
        }

        Assert.Equal((text, version), (FormWriter.Write(document), document.Version));

        // New components go into the inherited root and into inherited containers, after the inherited children.
        var panel = document.FindByName("ButtonPanel")!;
        var undoAdd = editor.Apply(new AddObject("ButtonPanel", 0, NewButton));
        Assert.Equal(["OkButton", "CancelButton", "NewButton", "HelpButton"], panel.Children.Select(c => c.Name));
        Assert.Contains("""
                end
                object NewButton: TButton
                  Left = 100
                  Top = 8
                  Width = 75
                  Height = 25
                end
                object HelpButton: TButton
            """, FormWriter.Write(document), StringComparison.Ordinal);
        editor.Apply(undoAdd);
        Assert.Equal(text, FormWriter.Write(document));
        editor.Apply(new AddObject("MidDialog", 1, NewButton with { Name = "RootButton" }));
        Assert.Equal(["TitleLabel", "ButtonPanel", "RootButton", "Addr", "NameEdit"], document.Root.Children.Select(c => c.Name));
        Assert.True(document.FindByName("RootButton")!.AcceptsNewChildren);

        // New components move freely after the inherited children; the root may be renamed.
        editor.Apply(new MoveObject("NameEdit", "ButtonPanel", 3));
        Assert.Equal(["OkButton", "CancelButton", "HelpButton", "NameEdit"], panel.Children.Select(c => c.Name));
        editor.Apply(new MoveObject("NameEdit", "ButtonPanel", 2));
        Assert.Equal(["OkButton", "CancelButton", "NameEdit", "HelpButton"], panel.Children.Select(c => c.Name));
        editor.Apply(new MoveObject("NameEdit", "MidDialog", 4));
        editor.Apply(new RemoveObject("RootButton"));
        editor.Apply(new RenameObject("MidDialog", "OtherDialog"));
        Assert.True(document.Root.IsInheritedRoot);
        Assert.StartsWith("inherited OtherDialog: TMidDialog\n", FormWriter.Write(document), StringComparison.Ordinal);
        editor.Apply(new RenameObject("OtherDialog", "MidDialog"));
        Assert.Equal(text, FormWriter.Write(document));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-134")]
    [Trait("Req", "DSG-LAY-067")]
    public void AncestorLeftChange_MovesDescendantUnlessOverridden()
    {
        var ancestors = new AncestorCatalog();
        var baseDoc = AddLevel(ancestors, BaseDialog, "BaseDialogUnit", "TForm").Document;
        const string plainText = "inherited PlainDialog: TPlainDialog\nend\n";
        const string movedText = """
            inherited MovedDialog: TMovedDialog
              inherited ButtonPanel: TPanel
                inherited OkButton: TButton
                  Left = 100
                end
              end
            end

            """;
        var plain = ParseWith(plainText, "TBaseDialog", null, ancestors, "plain.form");
        var moved = ParseWith(movedText, "TBaseDialog", null, ancestors, "moved.form");
        var engine = new LayoutEngine(Components, RendererRegistry.CreateDefault());
        int OkLeft(FormDocument d) => engine.Compute(d, LayoutMode.Design).GetBounds(d.FindByName("OkButton")!).Left;
        Assert.Equal((230, 100), (OkLeft(plain), OkLeft(moved)));
        var events = new List<(FormDocument Document, FormChangedEventArgs Args)>();
        plain.Changed += (_, e) => events.Add((plain, e));
        moved.Changed += (_, e) => events.Add((moved, e));

        // The OK button moves in the ancestor's own designer.
        new FormEditor(baseDoc, Components).Apply(new SetProperty("OkButton", "Left", new IntegerValue(250, false)));

        // The descendant without an override follows (value and layout); the one with an override keeps it.
        Assert.Equal(("250", "100"), (Effective(plain.FindByName("OkButton")!, "Left"), Effective(moved.FindByName("OkButton")!, "Left")));
        Assert.Equal((250, 100), (OkLeft(plain), OkLeft(moved)));
        Assert.Equal("8", Effective(moved.FindByName("OkButton")!, "Top"));

        // Neither descendant is modified: one re-merge event each, the file text unchanged.
        Assert.Equal([plain, moved], events.Select(e => e.Document));
        Assert.All(events, e => Assert.Equal((FormChangeFlags.Structure, true), (e.Args.Flags, e.Args.IsRemerge)));
        Assert.Equal((plainText, movedText), (FormWriter.Write(plain), FormWriter.Write(moved)));
        Assert.Empty(plain.Diagnostics);
        Assert.Empty(moved.Diagnostics);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-134")]
    public void Remerge_AncestorRemovesRenamesAddsComponents_NotModified_UndoStillApplies()
    {
        var ancestors = new AncestorCatalog();
        var baseDef = AddLevel(ancestors, BaseDialog, "BaseDialogUnit", "TForm");
        var baseEditor = new FormEditor(baseDef.Document, Components);
        const string text = """
            inherited ChildDialog: TChildDialog
              Caption = 'Child'
              inherited TitleLabel: TLabel
                Caption = 'Child title'
              end
              inherited ButtonPanel: TPanel
                inherited OkButton: TButton
                  Caption = 'Go'
                end
                inherited CancelButton: TButton
                  Caption = 'Close'
                end
                object HelpButton: TButton
                  Left = 8
                  Top = 8
                  Width = 75
                  Height = 25
                  Caption = 'Help'
                  TabOrder = 2
                end
              end
              object NameEdit: TEdit
                Left = 8
                Top = 32
                Width = 200
                Height = 23
                TabOrder = 1
              end
            end

            """;
        var document = ParseWith(text, "TBaseDialog", null, ancestors, "child.form");
        Assert.Empty(document.Diagnostics.Select(d => d.ToString()));
        var editor = new FormEditor(document, Components);
        var undo = new DesignerUndoStack();
        var selection = new Selection(document);

        // Recorded changes before the ancestor changes: an override, a new component and its rename (which rewrites a
        // reference line of an inherited object).
        void Do(params FormChange[] changes) =>
            undo.Push(new DesignerUndoUnit("Undo", changes, editor.Apply(changes), [], selection.ToSnapshot(), selection.ToSnapshot()));
        Do(new SetProperty("OkButton", "Caption", new StringValue("Start")));
        Do(new AddObject("ChildDialog", 2, new FormObjectData(FormObjectKind.Object, "ButtonMenu", "TPopupMenu", [], [])),
            new SetProperty("OkButton", "PopupMenu", new IdentValue("ButtonMenu")));
        Do(new RenameObject("ButtonMenu", "OkMenu"));
        undo.MarkSaved();
        var saved = FormWriter.Write(document);
        Assert.Contains("      PopupMenu = OkMenu\n", saved, StringComparison.Ordinal);
        selection.Set(["OkButton", "HelpButton"]);
        var events = new List<FormChangedEventArgs>();
        document.Changed += (_, e) => events.Add(e);

        // An unrelated ancestor change: one re-merge, the text and the undo stack untouched, the selection kept by name.
        baseEditor.Apply(new SetProperty("TitleLabel", "Width", new IntegerValue(90, false)));
        Assert.Equal((FormChangeFlags.Structure, true), (Assert.Single(events).Flags, events[0].IsRemerge));
        Assert.Equal((saved, true, 3), (FormWriter.Write(document), undo.IsAtSavePoint, undo.UndoCount));
        Assert.Equal("90", Effective(document.FindByName("TitleLabel")!, "Width"));
        selection.Rebind();
        Assert.Equal([document.FindByName("OkButton")!, document.FindByName("HelpButton")!], selection.Items);

        // Every recorded change still applies after the re-merge: undo all, then redo all.
        void Undo() => editor.Apply(undo.Undo()!.Undo);
        void Redo() => editor.Apply(undo.Redo()!.Redo);
        Undo();
        Assert.Contains("      PopupMenu = ButtonMenu\n", FormWriter.Write(document), StringComparison.Ordinal);
        Undo();
        Undo();
        Assert.Equal(text, FormWriter.Write(document));
        Redo();
        Redo();
        Redo();
        Assert.Equal(saved, FormWriter.Write(document));

        // The ancestor removes one component, renames another and adds one whose name a new component here already has.
        events.Clear();
        baseEditor.Apply(new RemoveObject("CancelButton"));
        baseEditor.Apply(new RenameObject("TitleLabel", "HeadLabel"));
        baseEditor.Apply(new AddObject("BaseDialog", 2, new FormObjectData(FormObjectKind.Object, "NameEdit", "TEdit",
        [
            new FormPropertyData("Left", new IntegerValue(8, false)), new FormPropertyData("Top", new IntegerValue(60, false)),
            new FormPropertyData("Width", new IntegerValue(100, false)), new FormPropertyData("Height", new IntegerValue(23, false)),
        ], [])));
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.True(e.IsRemerge));
        Assert.Equal(saved, FormWriter.Write(document)); // nothing of the file is lost
        Assert.Equal(
        [
            "child.form(3,13): warning F0109: Component TitleLabel: TLabel is not part of TBaseDialog: shown read-only",
            "child.form(11,15): warning F0109: Component CancelButton: TButton is not part of TBaseDialog: shown read-only",
            "child.form(27,10): error F0106: A component named NameEdit already exists",
        ], document.Diagnostics.Select(d => d.ToString()));
        var root = document.Root;
        Assert.Equal(["TitleLabel"], root.PreservedBlocks.Select(b => b.Name));
        Assert.Equal(["CancelButton"], document.FindByName("ButtonPanel")!.PreservedBlocks.Select(b => b.Name));
        Assert.Equal(["HeadLabel", "ButtonPanel", "NameEdit", "OkMenu", "NameEdit"], root.Children.Select(c => c.Name));
        Assert.True(root.Children[2].IsInherited);
        Assert.True(root.Children[4].IsPreserved); // the descendant's own NameEdit: read-only until renamed
        Assert.Same(root.Children[2], document.FindByName("NameEdit"));
        Assert.Equal("Start", Effective(document.FindByName("OkButton")!, "Caption"));
        Assert.Equal(["OkButton", "HelpButton"], document.FindByName("ButtonPanel")!.Children.Select(c => c.Name));
        Undo(); // the rename of the new component still applies
        Assert.Contains("      PopupMenu = ButtonMenu\n", FormWriter.Write(document), StringComparison.Ordinal);
        Redo();
        Assert.Equal((saved, true), (FormWriter.Write(document), undo.IsAtSavePoint));

        // A renamed container: its block is preserved with the new components inside it.
        baseEditor.Apply(new RenameObject("ButtonPanel", "BottomPanel"));
        var panelBlock = Assert.Single(root.PreservedBlocks, b => b.Name == "ButtonPanel");
        Assert.Contains("object HelpButton: TButton", panelBlock.PreservedText, StringComparison.Ordinal);
        Assert.Null(document.FindByName("HelpButton"));
        Assert.Equal(saved, FormWriter.Write(document));
        baseEditor.Apply(new RenameObject("BottomPanel", "ButtonPanel"));
        Assert.Equal(["OkButton", "HelpButton"], document.FindByName("ButtonPanel")!.Children.Select(c => c.Name));

        // F0112 while the ancestor is not in the catalog; resolved again when it returns.
        events.Clear();
        ancestors.Remove("TBaseDialog");
        Assert.Equal("child.form(1,1): warning F0112: Class TChildDialog has no ancestor form: its form file cannot start with \"inherited\": shown read-only",
            Assert.Single(document.Diagnostics).ToString());
        Assert.True(document.Root.IsPreserved);
        Assert.Null(document.Ancestor);
        Assert.Equal(saved, FormWriter.Write(document));
        ancestors.Set(baseDef);
        Assert.Equal(2, events.Count);
        Assert.Same(baseDef, document.Ancestor);
        Assert.False(document.Root.IsPreserved);
        Assert.Equal(3, document.Diagnostics.Count);
        Assert.Equal(saved, FormWriter.Write(document));
        Assert.Equal("Start", Effective(document.FindByName("OkButton")!, "Caption"));

        // A move recorded before the ancestor added a child to the container: its undo lands after the inherited children.
        var moveBack = editor.Apply(new MoveObject("HelpButton", "ChildDialog", 3));
        baseEditor.Apply(new AddObject("ButtonPanel", 1, NewButton));
        editor.Apply(moveBack);
        Assert.Equal(["OkButton", "NewButton", "HelpButton"], document.FindByName("ButtonPanel")!.Children.Select(c => c.Name));
        Assert.Equal(saved, FormWriter.Write(document));
    }
}
