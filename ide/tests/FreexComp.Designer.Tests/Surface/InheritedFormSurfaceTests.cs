using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;
using static FreexComp.Designer.Tests.Support.FrameFixtures;
using static FreexComp.Designer.Tests.Support.InheritanceFixtures;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>The design surface over descendant (inherited) forms (form-inheritance-designer.md §5, DSG-SUR-237…245; D-131).</summary>
[Trait("Phase", "8")]
public sealed class InheritedFormSurfaceTests
{
    /// <summary>
    /// An ancestor with an instance of <c>TAddressFrame</c>, a panel too small for the marker and a timer; its descendant
    /// adds an instance of its own and a button.
    /// </summary>
    private const string FrameDialog = """
        object FrameDialog: TFrameDialog
          Left = 200
          Top = 120
          Caption = 'Frames'
          ClientWidth = 640
          ClientHeight = 420
          PixelsPerInch = 96
          inline Address: TAddressFrame
            Left = 8
            Top = 8
            Width = 320
            Height = 180
            TabOrder = 0
          end
          object Tiny: TPanel
            Left = 400
            Top = 8
            Width = 10
            Height = 10
            TabOrder = 1
          end
          object Clock: TTimer
            DesignLeft = 8
            DesignTop = 8
          end
        end

        """;

    private const string FrameLeaf = """
        inherited FrameLeaf: TFrameLeaf
          inline Shipping: TAddressFrame
            Left = 8
            Top = 200
            Width = 320
            Height = 180
            TabOrder = 2
          end
          object LeafButton: TButton
            Left = 400
            Top = 380
            Width = 75
            Height = 25
            Caption = 'OK'
            TabOrder = 3
          end
        end

        """;

    /// <summary><see cref="InheritanceFixtures.MidDialog"/> opened as a descendant of <see cref="InheritanceFixtures.BaseDialog"/>.</summary>
    private static DesignerHarness Open(string text = MidDialog)
    {
        var ancestors = new FrameCatalog();
        AddLevel(ancestors, BaseDialog, "BaseDialogUnit", "TForm");
        var h = DesignerHarness.FromText(text, "MidDialogUnit.form", catalog: Components, rootAncestorClass: "TBaseDialog", ancestors: ancestors);
        Assert.True(h.Document.Root.IsInheritedRoot);
        h.Settings.NoSnapping();
        return h;
    }

    /// <summary><see cref="FrameLeaf"/> opened as a descendant of <see cref="FrameDialog"/> with the address frames known.</summary>
    private static DesignerHarness OpenFrameLeaf()
    {
        var frames = AddressFrames();
        var ancestors = new FrameCatalog();
        AddLevel(ancestors, FrameDialog, "FrameDialogUnit", "TForm", frames);
        var h = DesignerHarness.FromText(FrameLeaf, "FrameLeafUnit.form", catalog: Components, frames: frames, rootAncestorClass: "TFrameDialog",
            ancestors: ancestors);
        Assert.True(h.Document.Root.IsInheritedRoot);
        h.Settings.NoSnapping();
        return h;
    }

    private static (string Id, string Args) Status(string id, params object[] args) => (id, string.Join(",", args));

    private static List<(string Id, string Args)> Statuses(DesignerHarness h) => [.. h.Host.Statuses.Select(s => Status(s.Id, s.Args))];

    private static readonly (string, string) InheritedFixed = Status(StatusIds.InheritedComponentFixed, "TBaseDialog");

    [Fact]
    [Trait("Req", "DSG-SUR-238")]
    [Trait("Req", "DSG-SUR-237")]
    public void DeleteInheritedRejected_StatusMessage_FileUnchanged()
    {
        using var h = Open();
        var original = h.Text;
        Assert.Equal(MidDialog, original);
        var version = h.Document.Version;

        // An inherited button: edit.delete is disabled; the Delete key and the command show the message and delete nothing.
        h.Select("OkButton");
        Assert.False(h.Controller.CanExecute(Commands.Delete));
        Assert.True(h.Key(DesignKey.Delete));
        h.Execute(Commands.Delete);
        Assert.False(h.Controller.DeleteObjects([h.Object("OkButton")], null));

        // A selection that mixes a new and an inherited component: refused for all of them.
        h.Select("NameEdit", "CancelButton");
        Assert.False(h.Controller.CanExecute(Commands.Delete));
        Assert.True(h.Key(DesignKey.Delete));
        h.Execute(Commands.Delete);
        Assert.False(h.Controller.DeleteObjects([h.Object("NameEdit"), h.Object("CancelButton")], null));

        Assert.Equal(Enumerable.Repeat(InheritedFixed, 6), Statuses(h));
        Assert.Equal(original, h.Text);
        Assert.Equal(version, h.Document.Version);
        Assert.Equal(0, h.Undo.UndoCount);
        Assert.True(h.Undo.IsAtSavePoint);
        Assert.NotNull(h.Document.FindByName("OkButton"));
        Assert.NotNull(h.Document.FindByName("NameEdit"));
        Assert.Empty(h.Host.CodeSync.RemovedFields);

        // A new component alone is deleted as usual (its field goes).
        h.Select("NameEdit");
        Assert.True(h.Controller.CanExecute(Commands.Delete));
        h.Execute(Commands.Delete);
        Assert.Null(h.Document.FindByName("NameEdit"));
        Assert.Equal(["NameEdit"], Assert.Single(h.Host.CodeSync.RemovedFields));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
    }

    [Fact]
    [Trait("Req", "DSG-SUR-237")]
    [Trait("Req", "DSG-SUR-238")]
    [Trait("Req", "DSG-SUR-239")]
    public void RenameCutReparentZOrderRefused_MoveStoresOverride()
    {
        using var h = Open();
        var original = h.Text;

        // Cut and the z-order commands are disabled on an inherited control, Copy is not; key and command show the message.
        h.Select("OkButton");
        foreach (var command in (string[])[Commands.Cut, Commands.BringToFront, Commands.SendToBack])
        {
            Assert.False(h.Controller.CanExecute(command), command);
        }

        Assert.True(h.Controller.CanExecute(Commands.Copy));
        Assert.True(h.Key(DesignKey.Up, InputModifiers.Ctrl | InputModifiers.Shift));
        Assert.True(h.Key(DesignKey.Down, InputModifiers.Ctrl | InputModifiers.Shift));
        h.Execute(Commands.Cut);
        Assert.Equal(Enumerable.Repeat(InheritedFixed, 3), Statuses(h));

        // Rename is rejected by the editor (the inspector's Name row is read-only, DSG-OI-124).
        Assert.Throws<InvalidFormChangeException>(() => h.Controller.Commit(UndoTitles.Rename, [new RenameObject("OkButton", "AcceptButton")]));
        Assert.Equal(original, h.Text);
        Assert.Equal(0, h.Undo.UndoCount);

        // DSG-SUR-239: hit testing and selectParent as ordinary controls (the real parent).
        h.Click("OkButton");
        Assert.Equal(["OkButton"], h.Selected);
        h.Execute(Commands.SelectParent);
        Assert.Equal(["ButtonPanel"], h.Selected);
        h.Execute(Commands.SelectParent);
        Assert.Equal(["MidDialog"], h.Selected);

        // A drag stores the position as an override; over another container it stays in its parent (no re-parenting).
        h.Press("OkButton");
        h.MoveBy(-8, 0);
        h.Release();
        Assert.Equal(["OkButton"], h.Selected);
        Assert.Equal((222, 8), h.Position("OkButton"));
        Assert.Contains("    inherited OkButton: TButton\n      Left = 222\n      Caption = 'Apply'\n    end\n", h.Text, StringComparison.Ordinal);
        h.Press("CancelButton");
        h.MoveTo(h.SurfacePoint("NameEdit"));
        Assert.Null(h.Controller.DropTarget);
        h.Release();
        Assert.Equal("ButtonPanel", h.Parent("CancelButton"));
        h.ExecuteUndo();
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // The keyboard moves and resizes by overrides; a handle resize stores Width only.
        h.Select("CancelButton");
        Assert.True(h.Key(DesignKey.Right, InputModifiers.Ctrl));
        Assert.Contains("    inherited CancelButton: TButton\n      Left = 316\n    end\n", h.Text, StringComparison.Ordinal);
        h.ExecuteUndo();
        h.PressHandle("CancelButton", HandleKind.E);
        h.MoveBy(5, 0);
        h.Release();
        Assert.Contains("    inherited CancelButton: TButton\n      Width = 80\n    end\n", h.Text, StringComparison.Ordinal);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // The inherited panel is a drop target: a new component dragged into it is re-parented (after the inherited children).
        h.Press("NameEdit");
        h.MoveTo(h.SurfacePoint("ButtonPanel", (150, 20)));
        Assert.Same(h.Object("ButtonPanel"), h.Controller.DropTarget);
        h.Release();
        Assert.Equal(["OkButton", "CancelButton", "HelpButton", "NameEdit"], h.ChildNames("ButtonPanel"));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
    }

    [Fact]
    [Trait("Req", "DSG-SUR-238")]
    public void InheritedInstanceRefusals_NameTheirSource()
    {
        using var h = OpenFrameLeaf();
        var original = h.Text;

        // An inherited instance and a component inside it: structural refusals name the ancestor form.
        h.Select("Address");
        Assert.True(h.Key(DesignKey.Delete));
        h.Select("Address.StreetEdit");
        Assert.True(h.Key(DesignKey.Delete));

        // A component inside an instance this form creates keeps the frame message.
        h.Select("Shipping.StreetEdit");
        Assert.True(h.Key(DesignKey.Delete));

        // "No new children": pasting into an inherited instance names the frame.
        h.Select("LeafButton");
        h.Execute(Commands.Copy);
        h.Select("Address.CityPanel");
        h.Execute(Commands.Paste);
        Assert.Equal([Status(StatusIds.InheritedComponentFixed, "TFrameDialog"), Status(StatusIds.InheritedComponentFixed, "TFrameDialog"),
            Status(StatusIds.FrameChildFixed, "TAddressFrame"), Status(StatusIds.FrameChildFixed, "TAddressFrame")], Statuses(h));
        Assert.Equal(original, h.Text);
    }

    /// <summary>
    /// <see cref="InheritanceFixtures.MidDialog"/> with an override that moves the inherited panel to the end of the root's tab
    /// sequence.
    /// </summary>
    private static readonly string PanelLast = MidDialog.Replace("  inherited ButtonPanel: TPanel\n", "  inherited ButtonPanel: TPanel\n    TabOrder = 3\n",
        StringComparison.Ordinal);

    [Fact]
    [Trait("Req", "DSG-SUR-240")]
    [Trait("Req", "DSG-SUR-096")]
    public void PlaceAndPasteIntoInheritedContainer_TabOrderOfNewOnly()
    {
        using var h = Open();
        var original = h.Text;

        // A click into the inherited panel places a new button there, after the inherited and the new children; its tab
        // position follows the focusable siblings; no TabOrder line for the inherited buttons.
        h.Host.Arm("TButton");
        h.Click("ButtonPanel", (100, 8));
        Assert.Equal("ButtonPanel", h.Parent("Button1"));
        Assert.Equal(["OkButton", "CancelButton", "HelpButton", "Button1"], h.ChildNames("ButtonPanel"));
        Assert.Equal("3", Effective(h.Object("Button1"), "TabOrder"));
        Assert.Equal([new CodeSyncField("Button1", "TButton", "StdCtrls")], Assert.Single(h.Host.CodeSync.AddedFields));
        Assert.Contains("""
              inherited ButtonPanel: TPanel
                inherited OkButton: TButton
                  Caption = 'Apply'
                end
                object HelpButton: TButton
            """, h.Text, StringComparison.Ordinal);
        Assert.Contains("      TabOrder = 2\n    end\n    object Button1: TButton\n", h.Text, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(h.Text, "TabOrder = 3"));

        // Paste into the inherited panel (selected as the target): after the others, the next tab position.
        h.Select("NameEdit");
        h.Execute(Commands.Copy);
        h.Select("ButtonPanel");
        h.Execute(Commands.Paste);
        Assert.Equal(["NameEdit1"], h.Selected);
        Assert.Equal("ButtonPanel", h.Parent("NameEdit1"));
        Assert.Equal("4", Effective(h.Object("NameEdit1"), "TabOrder"));

        // A paste with an inherited control selected goes into its parent (the inherited panel).
        h.Select("CancelButton");
        h.Execute(Commands.Paste);
        Assert.Equal("ButtonPanel", h.Parent("NameEdit2"));
        Assert.Equal("5", Effective(h.Object("NameEdit2"), "TabOrder"));

        // Deleting a new sibling renumbers the new ones only.
        h.Select("HelpButton");
        h.Execute(Commands.Delete);
        Assert.Equal(["2", "3", "4"], new[] { "Button1", "NameEdit1", "NameEdit2" }.Select(n => Effective(h.Object(n), "TabOrder")));
        Assert.DoesNotContain("inherited OkButton: TButton\n      Caption = 'Apply'\n      TabOrder", h.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("inherited CancelButton", h.Text, StringComparison.Ordinal);
        while (h.Undo.CanUndo)
        {
            h.ExecuteUndo();
        }

        Assert.Equal(original, h.Text);

        // An inherited sibling whose tab position is out of sequence keeps it: only new siblings are renumbered.
        using var moved = Open(PanelLast);
        moved.Host.Arm("TEdit");
        moved.Click("MidDialog", (300, 60));
        Assert.Equal("MidDialog", moved.Parent("Edit1"));
        Assert.Equal(("0", "3", "2"), (Effective(moved.Object("NameEdit"), "TabOrder"), Effective(moved.Object("ButtonPanel"), "TabOrder"),
            Effective(moved.Object("Edit1"), "TabOrder")));
        Assert.Contains("  inherited ButtonPanel: TPanel\n    TabOrder = 3\n", moved.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "DSG-SUR-241")]
    public void SendToBackKeepsNewAfterInherited()
    {
        using var h = Open();
        h.Host.Arm("TButton");
        h.Click("ButtonPanel", (100, 8));
        Assert.Equal(["OkButton", "CancelButton", "HelpButton", "Button1"], h.ChildNames("ButtonPanel"));
        h.Host.Statuses.Clear();

        // A new component sent to back stops directly after the inherited children, with the status message.
        h.Select("Button1");
        Assert.True(h.Controller.CanExecute(Commands.SendToBack));
        h.Execute(Commands.SendToBack);
        Assert.Equal(["OkButton", "CancelButton", "Button1", "HelpButton"], h.ChildNames("ButtonPanel"));
        Assert.Equal([Status(StatusIds.InheritedOrderKept)], Statuses(h));
        Assert.Equal(UndoTitles.Order, h.Undo.UndoTitleKey);
        Assert.Contains("    end\n    object Button1: TButton\n", h.Text, StringComparison.Ordinal);
        Assert.True(h.Text.IndexOf("object Button1", StringComparison.Ordinal) < h.Text.IndexOf("object HelpButton", StringComparison.Ordinal));

        // There it cannot go further back: the command stays enabled (not its container's first child, amended DSG-SUR-241,
        // D-134); the clamp leaves it in place, so only the message is shown — no change, no undo unit.
        var text = h.Text;
        var units = h.Undo.UndoCount;
        h.Host.Statuses.Clear();
        Assert.True(h.Controller.CanExecute(Commands.SendToBack));
        h.Execute(Commands.SendToBack);
        Assert.Equal(["OkButton", "CancelButton", "Button1", "HelpButton"], h.ChildNames("ButtonPanel"));
        Assert.Equal([Status(StatusIds.InheritedOrderKept)], Statuses(h));
        Assert.Equal((text, units), (h.Text, h.Undo.UndoCount));
        Assert.True(h.Controller.CanExecute(Commands.BringToFront));

        // Bring to front works as usual.
        h.Execute(Commands.BringToFront);
        Assert.Equal(["OkButton", "CancelButton", "HelpButton", "Button1"], h.ChildNames("ButtonPanel"));
        h.ExecuteUndo();
        h.ExecuteUndo();
        Assert.Equal(["OkButton", "CancelButton", "HelpButton", "Button1"], h.ChildNames("ButtonPanel"));

        // At the root the new edit already follows the inherited components: enabled, the message only.
        h.Select("NameEdit");
        h.Host.Statuses.Clear();
        text = h.Text;
        units = h.Undo.UndoCount;
        Assert.True(h.Controller.CanExecute(Commands.SendToBack));
        h.Execute(Commands.SendToBack);
        Assert.Equal([Status(StatusIds.InheritedOrderKept)], Statuses(h));
        Assert.Equal((text, units), (h.Text, h.Undo.UndoCount));

        // A new component that is its container's first child (no inherited sibling before it) stays disabled.
        h.Host.Arm("TPanel");
        h.Click("MidDialog", (220, 60));
        h.Host.Arm("TButton");
        h.Click("Panel1", (8, 8));
        h.Select("Button2");
        Assert.Equal(["Button2"], h.ChildNames("Panel1"));
        Assert.False(h.Controller.CanExecute(Commands.SendToBack));
    }

    /// <summary>
    /// DSG-FD-027, DSG-FD-127 (D-134 item 5): a read-only descendant (<c>F0112</c>) whose root stores no size is drawn at the
    /// metadata's default client size — the size an empty form of the metadata's form class has — instead of a 0 × 0
    /// frame; a size it stores is kept, per dimension.
    /// </summary>
    [Fact]
    [Trait("Req", "DSG-FD-027")]
    [Trait("Req", "DSG-FD-127")]
    public void ReadOnlyDescendant_SizelessRoot_DrawnAtMetadataDefaultClientSize()
    {
        static DesignerHarness ReadOnly(string lines) => DesignerHarness.FromText("inherited MidForm2: TMidForm2\n" + lines + "end\n", "MidForm2.form",
            allowDiagnostics: true, catalog: Components, rootAncestorClass: "TMidForm", ancestors: new FrameCatalog());

        // The metadata's default client size: the defaultSize of its form class (400 × 300, as the new-form template, DSG-CS-111).
        var size = Components.All.First(c => c.Kind == FreexComp.Designer.Catalog.ComponentKind.Form).DefaultSize;
        Assert.Equal((400, 300), (size.Width, size.Height));
        static (int, int) Client(DesignerHarness d) => (d.Controller.Geometry.FormClientRect.Width, d.Controller.Geometry.FormClientRect.Height);

        using var h = ReadOnly("");
        Assert.Equal(["F0112"], h.Document.Diagnostics.Select(d => d.Code));
        Assert.True(h.Document.Root.IsPreserved);
        Assert.Equal((400, 300), Client(h));
        Assert.Equal(new Rect(16, 16, 402, 332), h.Controller.Geometry.FrameRect);
        var canvas = new FreexComp.Designer.Rendering.RecordingCanvas();
        h.Controller.Render(canvas, new Rect(0, 0, 1200, 900));
        Assert.NotEmpty(canvas.Ops);

        using var sized = ReadOnly("  ClientWidth = 300\n");
        Assert.True(sized.Document.Root.IsPreserved);
        Assert.Equal((300, 300), Client(sized));
    }

    /// <summary>
    /// DSG-FD-134, DSG-FD-027 (D-135), with the framework metadata (its first form class is the abstract base of the
    /// form class): a descendant (level 3) with a new button inside an inherited panel selected loses its ancestor from
    /// the ancestor catalog. It is merged again into a read-only <c>F0112</c> document; no selection handler sees a
    /// component without bounds (the selection is chosen again among the placeable components, else the root), and the
    /// read-only root is drawn at the metadata's default client size (the form class's <c>defaultSize</c>), as on a fresh open.
    /// </summary>
    [Fact]
    [Trait("Req", "DSG-FD-134")]
    [Trait("Req", "DSG-FD-027")]
    public void Remerge_AncestorRemovedWithNewComponentSelected_RootSelectedAtDefaultClientSize()
    {
        var catalog = FreexComp.Designer.Tests.Layout.LayoutHarness.Catalog;
        var ancestors = new FrameCatalog();
        FrameDefinition Level(string text, string unit, string ancestor)
        {
            var parsed = FreexComp.Designer.Text.FormParser.Parse(text, unit + ".form", catalog, ancestor, null, ancestors);
            Assert.Empty(parsed.Diagnostics);
            var definition = new FrameDefinition(parsed.Document!.Root.TypeName, unit, unit + ".form", parsed.Document);
            ancestors.Set(definition);
            return definition;
        }

        Level(BaseDialog, "BaseDialogUnit", "TForm");
        Level(MidDialog, "MidDialogUnit", "TBaseDialog");
        const string Leaf = """
            inherited MidDialog2: TMidDialog2
              inherited ButtonPanel: TPanel
                object Button1: TButton
                  Left = 100
                  Top = 8
                  Width = 75
                  Height = 25
                  Caption = 'Button1'
                  TabOrder = 3
                end
              end
            end

            """;
        using var h = DesignerHarness.FromText(Leaf, "MidDialog2.form", allowDiagnostics: true, catalog: catalog, rootAncestorClass: "TMidDialog",
            ancestors: ancestors);
        Assert.Empty(h.Document.Diagnostics);
        Assert.True(h.Document.Root.IsInheritedRoot);
        h.Select("Button1");
        Assert.True(h.Controller.Geometry.IsControl(h.Selection.Primary));
        var seen = new List<string>();
        h.Selection.Changed += (_, _) =>
        {
            _ = h.Controller.Geometry.GetBounds(h.Selection.Primary); // as the view brings the selection into view
            seen.Add(h.Selection.Primary.Name);
        };

        ancestors.Remove("TMidDialog");
        Assert.Equal(["F0112"], h.Document.Diagnostics.Select(d => d.Code));
        Assert.True(h.Document.Root.IsPreserved);
        Assert.NotNull(h.Document.FindByName("Button1"));
        Assert.False(h.Controller.Layout.HasBounds(h.Document.FindByName("Button1")!));
        Assert.Equal(["MidDialog2"], seen);
        Assert.Equal([h.Document.Root], h.Selection.Items);

        var size = catalog.Find("TForm")!.DefaultSize;
        Assert.Equal((400, 300), (size.Width, size.Height));
        Assert.Equal((400, 300), (h.Controller.Geometry.FormClientRect.Width, h.Controller.Geometry.FormClientRect.Height));

        // The same document opened fresh read-only: the same size.
        using var fresh = DesignerHarness.FromText(Leaf, "MidDialog2.form", allowDiagnostics: true, catalog: catalog, rootAncestorClass: "TMidDialog",
            ancestors: new FrameCatalog());
        Assert.Equal(["F0112"], fresh.Document.Diagnostics.Select(d => d.Code));
        Assert.Equal((400, 300), (fresh.Controller.Geometry.FormClientRect.Width, fresh.Controller.Geometry.FormClientRect.Height));
    }

    [Fact]
    [Trait("Req", "DSG-SUR-242")]
    public void RevertToInherited_ControlAndRoot_OneUndoUnit()
    {
        using var h = Open();
        var original = h.Text;

        // Disabled on new components and on inherited ones without an override.
        h.Select("HelpButton");
        Assert.False(h.Controller.CanExecute(Commands.RevertToInherited));
        h.Select("CancelButton");
        Assert.False(h.Controller.CanExecute(Commands.RevertToInherited));

        // An inherited control: its overrides go; the panel's block stays for the new child.
        h.Select("OkButton", "HelpButton");
        Assert.True(h.Controller.CanExecute(Commands.RevertToInherited));
        h.Execute(Commands.RevertToInherited);
        Assert.Equal(original.Replace("    inherited OkButton: TButton\n      Caption = 'Apply'\n    end\n", "", StringComparison.Ordinal), h.Text);
        Assert.Equal("OK", Effective(h.Object("OkButton"), "Caption"));
        Assert.Equal(["OkButton", "HelpButton"], h.Selected);
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal(UndoTitles.RevertToInherited, h.Undo.UndoTitleKey);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // The inherited root: its own overrides go, not those of its children; new children stay.
        h.Select("MidDialog");
        Assert.True(h.Controller.CanExecute(Commands.RevertToInherited));
        h.Execute(Commands.RevertToInherited);
        Assert.Equal(original.Replace("  Caption = 'Mid'\n", "", StringComparison.Ordinal), h.Text);
        Assert.Equal("Base", Effective(h.Document.Root, "Caption"));
        Assert.Equal("Apply", Effective(h.Object("OkButton"), "Caption"));
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.False(h.Controller.CanExecute(Commands.RevertToInherited));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
        Assert.Equal("Mid", Effective(h.Document.Root, "Caption"));
    }

    [Fact]
    [Trait("Req", "DSG-SUR-243")]
    public void CopyInheritedPastesIndependentComponents()
    {
        using var h = Open();
        var original = h.Text;

        // The inherited panel with its inherited and new children leaves as ordinary objects with their effective values.
        h.Select("ButtonPanel");
        h.Execute(Commands.Copy);
        Assert.Equal("""
            object ButtonPanel: TPanel
              Left = 0
              Top = 160
              Width = 400
              Height = 40
              TabOrder = 0
              object OkButton: TButton
                Left = 230
                Top = 8
                Width = 75
                Height = 25
                Caption = 'Apply'
                TabOrder = 0
                OnClick = OkButtonClick
              end
              object CancelButton: TButton
                Left = 315
                Top = 8
                Width = 75
                Height = 25
                Caption = 'Cancel'
                TabOrder = 1
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

            """, h.Host.Clipboard.GetData(ClipboardFormats.Text));
        Assert.Equal(original, h.Text);

        // Cut is refused (DSG-SUR-238).
        h.Execute(Commands.Cut);
        Assert.Equal([InheritedFixed], Statuses(h));
        Assert.Equal(original, h.Text);

        // Pasted on the form: independent new components, renamed (the inherited names are taken), with their fields.
        h.Select("MidDialog");
        h.Execute(Commands.Paste);
        Assert.Equal(["ButtonPanel1"], h.Selected);
        var copy = h.Object("ButtonPanel1");
        Assert.False(copy.IsInherited);
        Assert.Equal(FormObjectKind.Object, copy.Kind);
        Assert.Equal(["OkButton1", "CancelButton1", "HelpButton1"], h.ChildNames("ButtonPanel1"));
        Assert.All(copy.Children, c => Assert.False(c.IsInherited));
        Assert.Equal("Apply", Effective(h.Object("OkButton1"), "Caption"));
        Assert.Equal(["ButtonPanel1", "OkButton1", "CancelButton1", "HelpButton1"], Assert.Single(h.Host.CodeSync.AddedFields).Select(f => f.Name));
        Assert.Contains("  object ButtonPanel1: TPanel\n", h.Text, StringComparison.Ordinal);

        // The copies are ordinary: they can be deleted; the originals are untouched.
        h.Select("OkButton1");
        Assert.True(h.Controller.CanExecute(Commands.Delete));
        Assert.Equal("Apply", Effective(h.Object("OkButton"), "Caption"));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
    }

    [Fact]
    [Trait("Req", "UI-DSG-141")]
    public void InheritedMarkerDrawn_NotOnRootTinyOrFrameChildren()
    {
        static List<DrawOp> Ops(DesignerHarness h)
        {
            var canvas = new RecordingCanvas();
            h.Controller.Render(canvas, new Rect(0, 0, 1200, 1000));
            return [.. canvas.Ops];
        }

        static string Marker(DesignerHarness h, Rect b) =>
            $"FillPolygon {b.Left},{b.Top} {b.Left + 6},{b.Top} {b.Left},{b.Top + 6} {h.Controller.Palette.HandleSecondary}";

        // The inherited controls of a descendant: one marker each at the top-left corner; none on the root or new ones.
        using (var h = Open())
        {
            var ops = Ops(h);
            var markers = ops.Where(op => op.Kind == "FillPolygon" && op.Text.EndsWith($" {h.Controller.Palette.HandleSecondary}", StringComparison.Ordinal))
                .Select(op => op.Text).ToList();
            Assert.Equal(new[] { "TitleLabel", "ButtonPanel", "OkButton", "CancelButton" }.Select(n => Marker(h, G(h, n))).Order(), markers.Order());

            // Below the selection outline and handles.
            h.Select("OkButton");
            ops = Ops(h);
            var marker = ops.FindIndex(op => op.Text == Marker(h, G(h, "OkButton")));
            var outline = ops.FindIndex(op => op.Kind == "DrawRect" && op.Text.StartsWith($"DrawRect {G(h, "OkButton").Left},", StringComparison.Ordinal)
                && op.Text.Contains(h.Controller.Palette.HandleBorder.ToString(), StringComparison.Ordinal));
            Assert.True(marker >= 0 && outline > marker, $"marker {marker}, outline {outline}");
        }

        // An inherited instance: its outline and a marker; not on its components, not on a control below 12 px, not on a new
        // instance's components; a tray tile of an inherited non-visual component gets one too.
        using (var h = OpenFrameLeaf())
        {
            var ops = Ops(h);
            var secondary = h.Controller.Palette.HandleSecondary;
            var markers = ops.Where(op => op.Kind == "FillPolygon" && op.Text.EndsWith($" {secondary}", StringComparison.Ordinal))
                .Select(op => op.Text).ToList();
            var tile = h.Controller.Geometry.GetTrayTile(h.Object("Clock"));
            Assert.Equal([Marker(h, G(h, "Address")), Marker(h, tile)], markers);
            var b = G(h, "Address");
            Assert.Contains(ops, op => op.Text == $"DrawRect {b.Left},{b.Top},{b.Width},{b.Height} {secondary} 1 Dash");
        }
    }

    private static Rect G(DesignerHarness h, string name) => h.Controller.Geometry.GetBounds(h.Object(name));

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
