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

    private static DesignerHarness Open(string text = OneInstance, FrameCatalog? frames = null)
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
                   allowDiagnostics: true, catalog: Components, frames: new FrameCatalog()))
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
        using (var h = DesignerHarness.FromText(OneInstance, "customer.form", allowDiagnostics: true, catalog: Components, frames: new FrameCatalog()))
        {
            Assert.True(h.Object("BillingAddress").IsPreserved);
            var ops = Ops(h);
            var error = h.Controller.Palette.Renderers.Error;
            Assert.Contains(ops, op => op.Kind == "DrawRect" && op.Text.EndsWith($"{error} 1 Dash", StringComparison.Ordinal));
            Assert.Contains(ops, op => op.Kind == "DrawText" && op.Text.StartsWith("DrawText \"BillingAddress: TAddressFrame\"", StringComparison.Ordinal));
            Assert.DoesNotContain(ops, op => op.Kind == "DrawRect" && op.Text.EndsWith($"{h.Controller.Palette.HandleSecondary} 1 Dash", StringComparison.Ordinal));
        }
    }

    /// <summary>Status entries compared by id and argument values.</summary>
    private sealed class StatusComparer : IEqualityComparer<(string Id, object[] Args)>
    {
        public bool Equals((string Id, object[] Args) x, (string Id, object[] Args) y) => x.Id == y.Id && x.Args.SequenceEqual(y.Args);

        public int GetHashCode((string Id, object[] Args) obj) => obj.Id.GetHashCode(StringComparison.Ordinal);
    }
}
