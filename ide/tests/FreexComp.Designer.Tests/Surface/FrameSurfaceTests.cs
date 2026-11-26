using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;
using static FreexComp.Designer.Tests.Support.FrameFixtures;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>The design surface over frame instances (frames-designer.md §6, DSG-SUR-225…233; D-119).</summary>
public sealed class FrameSurfaceTests
{
    /// <summary>A form with one instance of <c>TAddressFrame</c> (which holds a nested <c>TCityFrame</c>) and an own panel.</summary>
    private const string OneInstance = """
        object CustomerForm: TCustomerForm
          Left = 200
          Top = 120
          Caption = 'Customer'
          ClientWidth = 640
          ClientHeight = 420
          PixelsPerInch = 96
          inline BillingAddress: TAddressFrame
            Left = 8
            Top = 8
            Width = 320
            Height = 180
            TabOrder = 0
          end
          object OwnPanel: TPanel
            Left = 340
            Top = 8
            Width = 280
            Height = 180
            TabOrder = 1
          end
        end

        """;

    private static DesignerHarness Open(string text = OneInstance, AncestorCatalog? frames = null)
    {
        var h = DesignerHarness.FromText(text, "customer.form", catalog: Components, frames: frames ?? AddressFrames());
        h.Settings.NoSnapping();
        return h;
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-225")]
    [Trait("Req", "DSG-SUR-226")]
    public void HitTestSelectsInheritedChild_MoveStoresOverride()
    {
        using var h = Open();

        // DSG-SUR-225: the deepest inherited control under the pointer; the instance's own area selects the instance.
        h.Click("BillingAddress.StreetEdit");
        Assert.Equal(["BillingAddress.StreetEdit"], h.Selected);
        h.Click("BillingAddress.CityLabel");
        Assert.Equal(["BillingAddress.CityLabel"], h.Selected);
        h.Click("BillingAddress.CityPicker.CityEdit");
        Assert.Equal(["BillingAddress.CityPicker.CityEdit"], h.Selected);
        h.Click("BillingAddress", (300, 170));
        Assert.Equal(["BillingAddress"], h.Selected);

        // designer.selectParent walks inherited parents up to the instance.
        h.Click("BillingAddress.CityLabel");
        h.Execute(Commands.SelectParent);
        Assert.Equal(["BillingAddress.CityPanel"], h.Selected);
        h.Execute(Commands.SelectParent);
        Assert.Equal(["BillingAddress"], h.Selected);

        // DSG-SUR-226: a drag inside the parent stores the new position as an override (no Width / Height lines).
        var original = h.Text;
        h.Press("BillingAddress.StreetEdit");
        h.MoveBy(8, 16);
        h.Release();
        Assert.Equal(["BillingAddress.StreetEdit"], h.Selected);
        Assert.Equal("BillingAddress", h.Parent("BillingAddress.StreetEdit"));
        Assert.Contains("    inherited StreetEdit: TEdit\n      Left = 72\n      Top = 20\n    end\n", h.Text, StringComparison.Ordinal);

        // Dragged over the form's own panel it stays in its parent (no re-parenting out of the instance).
        h.Press("BillingAddress.StreetEdit");
        h.MoveTo(h.SurfacePoint("OwnPanel"));
        Assert.Null(h.Controller.DropTarget);
        h.Release();
        Assert.Equal("BillingAddress", h.Parent("BillingAddress.StreetEdit"));
        Assert.Empty(h.ChildNames("OwnPanel"));

        // Keyboard: Ctrl+arrow moves by 1 px, Shift+Alt+arrow resizes by 1 px; both are overrides.
        h.Execute(Commands.Undo);
        h.Execute(Commands.Undo);
        Assert.Equal(original, h.Text);
        h.Select("BillingAddress.CityPanel");
        Assert.True(h.Key(DesignKey.Right, InputModifiers.Ctrl));
        Assert.True(h.Key(DesignKey.Down, InputModifiers.Shift | InputModifiers.Alt));
        Assert.Contains("    inherited CityPanel: TPanel\n      Left = 9\n      Height = 65\n    end\n", h.Text, StringComparison.Ordinal);

        // A handle resize of an inherited control stores Width / Height only.
        h.Execute(Commands.Undo);
        h.Execute(Commands.Undo);
        h.Select("BillingAddress.StreetEdit");
        h.PressHandle("BillingAddress.StreetEdit", HandleKind.E);
        h.MoveBy(10, 0);
        h.Release();
        Assert.Contains("    inherited StreetEdit: TEdit\n      Width = 250\n    end\n", h.Text, StringComparison.Ordinal);
        Assert.Equal((64, 4), h.Position("BillingAddress.StreetEdit"));
        h.Execute(Commands.Undo);
        Assert.Equal(original, h.Text);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-227")]
    [Trait("Req", "DSG-SUR-228")]
    public void StructureCommandsRefused_StatusMessage_NoDropTarget()
    {
        using var h = Open();
        var original = h.Text;
        (string, object[]) Fixed(string frameClass) => (StatusIds.FrameChildFixed, [frameClass]);

        // Delete, Cut and the z-order commands are disabled on an inherited control; the key shows the status message.
        h.Select("BillingAddress.StreetEdit");
        foreach (var command in (string[])[Commands.Delete, Commands.Cut, Commands.BringToFront, Commands.SendToBack])
        {
            Assert.False(h.Controller.CanExecute(command), command);
        }

        Assert.True(h.Controller.CanExecute(Commands.Copy));
        Assert.True(h.Key(DesignKey.Delete));
        Assert.True(h.Key(DesignKey.Up, InputModifiers.Ctrl | InputModifiers.Shift));
        h.Execute(Commands.Cut);
        Assert.Equal([Fixed("TAddressFrame"), Fixed("TAddressFrame"), Fixed("TAddressFrame")], h.Host.Statuses.Select(s => (s.Id, s.Args)),
            new StatusComparer());
        Assert.Equal(original, h.Text);
        Assert.False(h.Controller.DeleteObjects([h.Object("BillingAddress.StreetEdit")], null));

        // A control of the nested instance names its own frame; a mixed selection disables the commands for all.
        h.Host.Statuses.Clear();
        h.Select("BillingAddress.CityPicker.CityEdit");
        Assert.True(h.Key(DesignKey.Delete));
        Assert.Equal([Fixed("TCityFrame")], h.Host.Statuses.Select(s => (s.Id, s.Args)), new StatusComparer());
        h.Select("OwnPanel", "BillingAddress.StreetLabel");
        Assert.False(h.Controller.CanExecute(Commands.Delete));
        Assert.False(h.Controller.CanExecute(Commands.Cut));

        // Paste into an inherited container or next to an inherited control is refused with the message.
        h.Select("OwnPanel");
        h.Execute(Commands.Copy);
        h.Host.Statuses.Clear();
        h.Select("BillingAddress.CityPanel");
        h.Execute(Commands.Paste);
        h.Select("BillingAddress.StreetEdit");
        h.Execute(Commands.Paste);
        Assert.Equal([Fixed("TAddressFrame"), Fixed("TAddressFrame")], h.Host.Statuses.Select(s => (s.Id, s.Args)), new StatusComparer());
        Assert.Equal(original, h.Text);

        // An instance and its inherited controls are never drop targets: a drag over them goes to the instance's parent.
        h.Press("OwnPanel");
        h.MoveTo(h.SurfacePoint("BillingAddress.CityPanel"));
        Assert.Null(h.Controller.DropTarget);
        h.Release();
        Assert.Equal("CustomerForm", h.Parent("OwnPanel"));
        h.Execute(Commands.Undo);
        h.Host.Arm("TButton");
        h.Click("BillingAddress.CityPanel", (200, 30));
        Assert.Equal("CustomerForm", h.Parent("Button1"));
        Assert.DoesNotContain(h.Object("BillingAddress.CityPanel").Children, c => c.Name == "Button1");
        h.Execute(Commands.Undo);
        Assert.Equal(original, h.Text);

        // DSG-SUR-228: an instance is an ordinary component — moved, deleted (its field only), pasted beside itself.
        h.Select("BillingAddress");
        Assert.True(h.Controller.CanExecute(Commands.Delete));
        h.Execute(Commands.Delete);
        Assert.Equal(["BillingAddress"], Assert.Single(h.Host.CodeSync.RemovedFields));
        Assert.Null(h.Document.FindByName("BillingAddress"));
        Assert.DoesNotContain("BillingAddress", h.Text, StringComparison.Ordinal);
        h.Execute(Commands.Undo);
        Assert.Equal(original, h.Text);
        Assert.True(h.Object("BillingAddress").IsFrameInstance);
        Assert.Equal("Street", Effective(h.Object("BillingAddress.StreetLabel"), "Caption"));
        h.Press("BillingAddress", (300, 170));
        h.MoveBy(0, 200);
        h.Release();
        Assert.Equal((8, 208), h.Position("BillingAddress"));
        Assert.True(h.Object("BillingAddress").IsFrameInstance);
    }

    /// <summary>An instance with own lines (one non-geometry), an overridden label and a nested override; an own panel.</summary>
    private const string Overridden = """
        object CustomerForm: TCustomerForm
          Left = 200
          Top = 120
          Caption = 'Customer'
          ClientWidth = 640
          ClientHeight = 420
          PixelsPerInch = 96
          inline BillingAddress: TAddressFrame
            Left = 8
            Top = 8
            Width = 320
            Height = 180
            Color = clYellow
            TabOrder = 0
            inherited StreetLabel: TLabel
              Left = 12
              Caption = 'Billing street'
            end
            inherited CityPicker: TCityFrame
              inherited CityEdit: TEdit
                Width = 150
              end
            end
          end
          object OwnPanel: TPanel
            Left = 340
            Top = 8
            Width = 280
            Height = 180
            TabOrder = 1
          end
        end

        """;

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-229")]
    public void RevertToInherited_ChildAndInstance_OneUndoUnit()
    {
        using var h = Open(Overridden);
        var original = h.Text;
        Assert.Equal(Overridden, original);

        // Disabled without inherited objects, and on inherited objects / instances that override nothing.
        h.Select("OwnPanel");
        Assert.False(h.Controller.CanExecute(Commands.RevertToInherited));
        h.Select("BillingAddress.StreetEdit");
        Assert.False(h.Controller.CanExecute(Commands.RevertToInherited));
        using (var plain = Open())
        {
            plain.Select("BillingAddress");
            Assert.False(plain.Controller.CanExecute(Commands.RevertToInherited));
        }

        // An inherited child: all its own overrides go, nothing else; one unit, the selection kept.
        h.Select("OwnPanel", "BillingAddress.StreetLabel");
        Assert.True(h.Controller.CanExecute(Commands.RevertToInherited));
        h.Execute(Commands.RevertToInherited);
        Assert.Equal(original.Replace("""
                inherited StreetLabel: TLabel
                  Left = 12
                  Caption = 'Billing street'
                end

            """, "", StringComparison.Ordinal), h.Text);
        Assert.Equal(["OwnPanel", "BillingAddress.StreetLabel"], h.Selected);
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal(UndoTitles.RevertToInherited, h.Undo.UndoTitleKey);
        Assert.Equal("Street", Effective(h.Object("BillingAddress.StreetLabel"), "Caption"));
        Assert.Equal((8, 8), h.Position("BillingAddress.StreetLabel"));
        h.Execute(Commands.Undo);
        Assert.Equal(original, h.Text);

        // An instance: every override block and its own lines except Left, Top, Width, Height and TabOrder.
        h.Select("BillingAddress", "BillingAddress.StreetLabel");
        h.Execute(Commands.RevertToInherited);
        Assert.Contains("""
              inline BillingAddress: TAddressFrame
                Left = 8
                Top = 8
                Width = 320
                Height = 180
                TabOrder = 0
              end
              object OwnPanel: TPanel
            """, h.Text, StringComparison.Ordinal);
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.False(h.Controller.CanExecute(Commands.RevertToInherited));
        Assert.Equal("100", Effective(h.Object("BillingAddress.CityPicker.CityEdit"), "Width"));
        h.Execute(Commands.Undo);
        Assert.Equal(original, h.Text);
        Assert.Equal("150", Effective(h.Object("BillingAddress.CityPicker.CityEdit"), "Width"));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-230")]
    [Trait("Req", "DSG-SUR-234")]
    public void CopyInstanceAndCopyInheritedChildren_Paste()
    {
        using var h = Open(Overridden);
        var original = h.Text;

        // Copy of an instance: the inline text with its override blocks.
        h.Select("BillingAddress");
        h.Execute(Commands.Copy);
        var instanceText = """
            inline BillingAddress: TAddressFrame
              Left = 8
              Top = 8
              Width = 320
              Height = 180
              Color = clYellow
              TabOrder = 0
              inherited StreetLabel: TLabel
                Left = 12
                Caption = 'Billing street'
              end
              inherited CityPicker: TCityFrame
                inherited CityEdit: TEdit
                  Width = 150
                end
              end
            end

            """;
        Assert.Equal(instanceText, h.Host.Clipboard.GetData(ClipboardFormats.Text));

        // Pasted beside itself (an instance is no paste target): a resolved instance, a new name, the inherited names kept,
        // one field of the frame class with the frame's unit.
        h.Execute(Commands.Paste);
        Assert.Equal(["BillingAddress1"], h.Selected);
        var copy = h.Object("BillingAddress1");
        Assert.True(copy.IsFrameInstance);
        Assert.Equal("CustomerForm", h.Parent("BillingAddress1"));
        Assert.Equal((16, 16), h.Position("BillingAddress1"));
        Assert.Equal("Billing street", Effective(h.Object("BillingAddress1.StreetLabel"), "Caption"));
        Assert.Equal("150", Effective(h.Object("BillingAddress1.CityPicker.CityEdit"), "Width"));
        Assert.Equal([new CodeSyncField("BillingAddress1", "TAddressFrame", "AddressFrameUnit")], Assert.Single(h.Host.CodeSync.AddedFields));
        Assert.Contains("  inline BillingAddress1: TAddressFrame\n    Left = 16\n    Top = 16\n", h.Text, StringComparison.Ordinal);

        // Undo / redo rebuild the resolved instance from its snapshot.
        h.Execute(Commands.Undo);
        Assert.Equal(original, h.Text);
        h.Execute(Commands.Redo);
        Assert.True(h.Object("BillingAddress1").IsFrameInstance);
        Assert.Equal("Billing street", Effective(h.Object("BillingAddress1.StreetLabel"), "Caption"));
        h.Execute(Commands.Undo);

        // Into a form whose catalog does not know the frame: a preserved F0101 instance (shown read-only).
        using (var other = DesignerHarness.FromText(OneInstance.Replace("  inline BillingAddress: TAddressFrame\n    Left = 8\n    Top = 8\n"
                       + "    Width = 320\n    Height = 180\n    TabOrder = 0\n  end\n", "", StringComparison.Ordinal), "other.form",
                   allowDiagnostics: true, catalog: Components, frames: new AncestorCatalog()))
        {
            other.Host.Clipboard.SetData(new Dictionary<string, string> { [ClipboardFormats.Text] = instanceText });
            other.Execute(Commands.Paste);
            var preserved = other.Object("BillingAddress");
            Assert.True(preserved.IsPreserved);
            Assert.False(preserved.IsFrameInstance);
            Assert.Equal(FormObjectKind.Inline, preserved.Kind);
            Assert.Contains("    inherited StreetLabel: TLabel\n      Left = 12\n", other.Text, StringComparison.Ordinal);
        }

        // Copy of inherited controls alone: ordinary objects with their effective values and inherited children.
        h.Select("BillingAddress.StreetLabel", "BillingAddress.CityPanel");
        h.Execute(Commands.Copy);
        Assert.Equal("""
            object StreetLabel: TLabel
              Left = 12
              Top = 8
              Width = 40
              Height = 15
              Caption = 'Billing street'
            end
            object CityPanel: TPanel
              Left = 8
              Top = 40
              Width = 296
              Height = 64
              TabOrder = 1
              object CityLabel: TLabel
                Left = 8
                Top = 8
                Width = 24
                Height = 15
                Caption = 'City'
              end
            end

            """, h.Host.Clipboard.GetData(ClipboardFormats.Text));
        h.Select("OwnPanel");
        h.Execute(Commands.Paste);
        Assert.Equal(["StreetLabel", "CityPanel"], h.Selected);
        Assert.Equal("OwnPanel", h.Parent("StreetLabel"));
        Assert.False(h.Object("CityLabel").IsInherited);
        Assert.Equal("CityPanel", h.Parent("CityLabel"));
        Assert.Equal("Billing street", Effective(h.Object("StreetLabel"), "Caption"));
        Assert.Equal("Billing street", Effective(h.Object("BillingAddress.StreetLabel"), "Caption"));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-232")]
    public void CycleRefused()
    {
        // The address frame document: placing an instance of itself, or of a frame that contains it, is refused.
        var frames = AddressFrames();
        var outer = """
            object OuterFrame: TOuterFrame
              Left = 0
              Top = 0
              Width = 400
              Height = 300
              inline Address: TAddressFrame
                Left = 0
                Top = 0
                Width = 320
                Height = 180
              end
            end

            """;
        AddFrame(frames, outer, "OuterFrameUnit");
        using var h = DesignerHarness.FromText(AddressFrame, "AddressFrameUnit.form", catalog: Components, frames: frames,
            rootAncestorClass: "TFrame");
        h.Settings.NoSnapping();
        var original = h.Text;
        foreach (var frameClass in (string[])["TAddressFrame", "TOuterFrame"])
        {
            h.Host.Statuses.Clear();
            h.Host.Clipboard.SetText($"inline Copy: {frameClass}\n  Left = 0\n  Top = 0\n  Width = 50\n  Height = 50\nend\n");
            h.Execute(Commands.Paste);
            Assert.Equal((StatusIds.FrameCycle, frameClass), Assert.Single(h.Host.Statuses) is var (id, args) ? (id, args[0]) : default);
            Assert.Equal(original, h.Text);
        }

        // A frame that does not contain this one is placed as a resolved instance.
        h.Host.Clipboard.SetText("inline Picker2: TCityFrame\n  Left = 0\n  Top = 0\n  Width = 200\n  Height = 60\nend\n");
        h.Execute(Commands.Paste);
        Assert.True(h.Object("Picker2").IsFrameInstance);
        Assert.Equal("Picker2", h.Selected.Single());
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DSG-134")]
    [Trait("Req", "UI-DSG-139")]
    public void PreservedInstanceDrawnAsUnknownBox_ResolvedOneOutlined()
    {
        static List<DrawOp> Ops(DesignerHarness h)
        {
            var canvas = new RecordingCanvas();
            h.Controller.Render(canvas, new Rect(0, 0, 1000, 800));
            return [.. canvas.Ops];
        }

        // A resolved instance and the nested instance its frame holds: a dashed outline in designer.handle.secondary each.
        using (var h = Open())
        {
            var outline = $"{h.Controller.Palette.HandleSecondary} 1 Dash";
            Assert.Equal(2, Ops(h).Count(op => op.Kind == "DrawRect" && op.Text.EndsWith(outline, StringComparison.Ordinal)));
        }

        // An instance of a frame the catalog does not know (F0101): the unknown-class box (dashed status.error border,
        // "Name: TClassName"), no instance outline.
        using (var h = DesignerHarness.FromText(OneInstance, "customer.form", allowDiagnostics: true, catalog: Components, frames: new AncestorCatalog()))
        {
            Assert.True(h.Object("BillingAddress").IsPreserved);
            var ops = Ops(h);
            var error = h.Controller.Palette.Renderers.Error;
            Assert.Contains(ops, op => op.Kind == "DrawRect" && op.Text.EndsWith($"{error} 1 Dash", StringComparison.Ordinal));
            Assert.Contains(ops, op => op.Kind == "DrawText" && op.Text.StartsWith("DrawText \"BillingAddress: TAddressFrame\"", StringComparison.Ordinal));
            Assert.DoesNotContain(ops, op => op.Kind == "DrawRect" && op.Text.EndsWith($"{h.Controller.Palette.HandleSecondary} 1 Dash", StringComparison.Ordinal));
        }
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-TP-064")]
    [Trait("Req", "DSG-CS-112")]
    public void ArmedFramePlacesResolvedInstance_FieldOfFrameClass()
    {
        using var h = Open();
        var client = h.Controller.Geometry.FormClientRect;

        // An armed frame class places an instance at the click with the definition's size, named after the class.
        h.Host.Arm("TAddressFrame");
        h.ClickAt(((int)client.Left + 340, (int)client.Top + 200));
        var placed = h.Object("AddressFrame1");
        Assert.Equal((FormObjectKind.Inline, "TAddressFrame"), (placed.Kind, placed.TypeName));
        Assert.True(placed.IsFrameInstance); // resolved at once: the frame's children are inherited into it
        Assert.Contains(placed.Children, c => c.IsInherited && c.Name == "StreetEdit");
        Assert.Equal(["AddressFrame1"], h.Selected);
        Assert.Null(h.Host.ArmedPaletteClass); // single-shot
        Assert.Contains("  inline AddressFrame1: TAddressFrame\n    Left = 340\n    Top = 200\n    Width = 320\n    Height = 180\n", h.Text,
            StringComparison.Ordinal);

        // DSG-CS-112: one field of the frame class from the frame's unit (its uses entry), none for the inherited objects.
        Assert.Equal([new CodeSyncField("AddressFrame1", "TAddressFrame", "AddressFrameUnit")], Assert.Single(h.Host.CodeSync.AddedFields));

        // Undo removes the instance in one step.
        h.ExecuteUndo();
        Assert.Null(h.Document.FindByName("AddressFrame1"));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-121")]
    [Trait("Req", "UI-DSG-140")]
    public void FrameRootDrawnWithEdge_NoCaption()
    {
        using var h = DesignerHarness.FromText(AddressFrame, "AddressFrameUnit.form", catalog: Components, frames: AddressFrames(),
            rootAncestorClass: "TFrame");
        var canvas = new RecordingCanvas();
        h.Controller.Render(canvas, new Rect(0, 0, 1000, 800));
        var frame = h.Controller.Geometry.FrameRect;

        // UI-DSG-140: the frame rectangle in its Color with a 1 px border.default edge; no caption bar, no window glyphs.
        Assert.Equal(0, h.Controller.Geometry.CaptionRect.Height);
        var edge = $"DrawRect {frame.Left},{frame.Top},{frame.Width},{frame.Height} {h.Controller.Palette.BorderDefault} 1 Solid";
        Assert.Contains(canvas.Ops, op => op.Text == edge);
        Assert.DoesNotContain(canvas.Ops, op => op.Kind == "DrawText" && op.Text.Contains("\"AddressFrame\"", StringComparison.Ordinal));
    }

    /// <summary>A frame with a hidden edit and a status bar with one panel (D-125).</summary>
    private const string StatusFrame = """
        object StatusFrame: TStatusFrame
          Left = 0
          Top = 0
          Width = 300
          Height = 100
          object HiddenEdit: TEdit
            Left = 8
            Top = 8
            Width = 120
            Height = 23
            TabOrder = 0
            Visible = False
          end
          object Status: TStatusBar
            Left = 0
            Top = 77
            Width = 300
            Height = 23
            Panels = <
              item
                Text = 'Ready'
                Width = 120
              end
            >
          end
        end

        """;

    private const string StatusForm = """
        object StatusForm: TStatusForm
          Left = 200
          Top = 120
          Caption = 'Status'
          ClientWidth = 640
          ClientHeight = 420
          PixelsPerInch = 96
          inline Footer: TStatusFrame
            Left = 8
            Top = 8
            Width = 300
            Height = 100
            TabOrder = 0
          end
        end

        """;

    private static DesignerHarness OpenStatusForm()
    {
        var frames = new AncestorCatalog();
        AddFrame(frames, StatusFrame, "StatusFrameUnit");
        return Open(StatusForm, frames);
    }

    private static List<DrawOp> RenderOps(DesignerHarness h)
    {
        var canvas = new RecordingCanvas();
        h.Controller.Render(canvas, new Rect(0, 0, 1000, 800));
        return [.. canvas.Ops];
    }

    /// <summary>
    /// D-125 (DSG-FD-106, UI-DSG §10): an inherited control whose frame stores <c>Visible = False</c> is not drawn as a
    /// visible control in an instance: it gets the hatch of a hidden control, as in the frame's own document.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-106")]
    [Trait("Req", "DSG-RND-007")]
    public void InheritedHiddenControlDrawnHatched()
    {
        using var h = OpenStatusForm();
        var hidden = h.Object("Footer.HiddenEdit");
        Assert.True(hidden.IsInherited);
        Assert.Null(hidden.FindProperty("Visible")); // the instance stores nothing: the frame's line applies
        var b = h.Controller.Geometry.GetBounds(hidden);

        Assert.Contains(RenderOps(h), op => op.Kind == "FillHatch" && op.Extent is { } e && e.Left == b.Left && e.Top == b.Top);
    }

    /// <summary>
    /// D-125: a collection-based control of the frame (a status bar's <c>Panels</c>) shows the frame's items in an instance
    /// that stores no collection of its own.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-106")]
    public void InheritedCollectionItemsShownInInstance()
    {
        using var h = OpenStatusForm();
        Assert.Null(h.Object("Footer.Status").FindProperty("Panels"));

        Assert.Contains(RenderOps(h), op => op.Kind == "DrawText" && op.Text.StartsWith("DrawText \"Ready\"", StringComparison.Ordinal));
    }

    /// <summary>A frame whose page control stores <c>ActivePage = S2</c>, and an edit with an unknown property (D-128).</summary>
    private const string PagesFrame = """
        object PagesFrame: TPagesFrame
          Left = 0
          Top = 0
          Width = 300
          Height = 200
          object Book: TPageControl
            Left = 0
            Top = 0
            Width = 300
            Height = 160
            ActivePage = S2
            TabOrder = 0
            object S1: TTabSheet
              Caption = 'First'
            end
            object S2: TTabSheet
              Caption = 'Second'
            end
            object S3: TTabSheet
              Caption = 'Third'
            end
          end
          object OddEdit: TEdit
            Left = 8
            Top = 168
            Width = 120
            Height = 23
            TabOrder = 1
            NoSuchProperty = 1
          end
        end

        """;

    private const string PagesForm = """
        object PagesForm: TPagesForm
          Left = 200
          Top = 120
          Caption = 'Pages'
          ClientWidth = 640
          ClientHeight = 420
          PixelsPerInch = 96
          inline Wizard: TPagesFrame
            Left = 8
            Top = 8
            Width = 300
            Height = 200
            TabOrder = 0
          end
        end

        """;

    private static DesignerHarness OpenPagesForm()
    {
        var frames = new AncestorCatalog();
        AddFrame(frames, PagesFrame, "PagesFrameUnit");
        return Open(PagesForm, frames);
    }

    /// <summary>
    /// D-128 (DSG-FD-106, DSG-SUR-194): in an instance the page verbs read the effective <c>ActivePage</c> — the frame's
    /// <c>S2</c> while the instance stores none: going back to <c>S2</c> removes the override, and the first page (the
    /// default without any line) is stored, since removing the line would show the frame's <c>S2</c>.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-106")]
    [Trait("Req", "DSG-SUR-194")]
    public void ActivePageChange_UsesInheritedValue()
    {
        using var h = OpenPagesForm();
        var book = h.Object("Wizard.Book");
        Assert.Null(book.FindProperty("ActivePage"));
        Assert.Equal("S2", PageControlRenderer.ActiveSheet(book)!.Name);
        var original = h.Text;

        h.Select("Wizard.Book");
        h.Execute(Commands.NextPage);
        Assert.Equal("S3", PageControlRenderer.ActiveSheet(book)!.Name);
        Assert.Equal(new IdentValue("S3"), book.FindProperty("ActivePage")?.Value);

        // Back to the frame's page: the override goes, the instance writes nothing for the page control again.
        h.Execute(Commands.PreviousPage);
        Assert.Equal("S2", PageControlRenderer.ActiveSheet(book)!.Name);
        Assert.Null(book.FindProperty("ActivePage"));
        Assert.Equal(original, h.Text);

        // The first page is stored as an override: without the line the frame's S2 would be active.
        h.Execute(Commands.PreviousPage);
        Assert.Equal(["Wizard.S1"], h.Selected);
        Assert.Equal("S1", PageControlRenderer.ActiveSheet(book)!.Name);
        Assert.Equal(new IdentValue("S1"), book.FindProperty("ActivePage")?.Value);
    }

    /// <summary>
    /// D-128 (UI-DSG §10, F0102): the warning triangle of an inherited object also reflects an unreadable line of its frame
    /// definition (here an unknown property), which the instance does not store itself.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-106")]
    public void InheritedWarningFromDefinition()
    {
        using var h = OpenPagesForm();
        var odd = h.Object("Wizard.OddEdit");
        Assert.True(odd.IsInherited);
        Assert.DoesNotContain(odd.Properties, p => p.IsPreserved); // the instance stores nothing of its own
        var b = h.Controller.Geometry.GetBounds(odd);

        // ui/designer.md §10: the 12 px triangle at (2, 2) of the control in the warning colour.
        static string Triangle(Rect r, SurfacePalette p) =>
            $"FillPolygon {r.Left + 2},{r.Top + 14} {r.Left + 14},{r.Top + 14} {r.Left + 8},{r.Top + 2} {p.Warning}";
        var ops = RenderOps(h);
        Assert.Contains(ops, op => op.Text == Triangle(b, h.Controller.Palette));
        var book = h.Controller.Geometry.GetBounds(h.Object("Wizard.Book")); // a clean inherited object gets none
        Assert.DoesNotContain(ops, op => op.Text == Triangle(book, h.Controller.Palette));
    }

    /// <summary>Status entries compared by id and argument values.</summary>
    private sealed class StatusComparer : IEqualityComparer<(string Id, object[] Args)>
    {
        public bool Equals((string Id, object[] Args) x, (string Id, object[] Args) y) => x.Id == y.Id && x.Args.SequenceEqual(y.Args);

        public int GetHashCode((string Id, object[] Args) obj) => obj.Id.GetHashCode(StringComparison.Ordinal);
    }
}
