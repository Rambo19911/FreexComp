using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>
/// D-094: measured auto-size (design-surface.md DSG-SUR-088, design-renderers.md DSG-RND-016). The designer stores the
/// renderer's preferred size as explicit bounds, in the same undo unit, when an auto-size control is created or pasted or
/// its caption/font is edited; otherwise stored bounds stay. Measuring uses the deterministic rule of DSG-RND-003
/// (6 px per character at 12 px, line height 1.25 × size).
/// </summary>
public sealed class AutoSizeTests
{
    private const string Form = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object L1: TLabel
            Left = 100
            Top = 10
            Width = 34
            Height = 15
            Caption = 'L1'
          end
          object Right: TLabel
            Left = 100
            Top = 40
            Width = 34
            Height = 15
            Alignment = taRightJustify
            Caption = 'R'
          end
          object Fixed: TLabel
            Left = 100
            Top = 70
            Width = 34
            Height = 15
            AutoSize = False
            Caption = 'F'
          end
          object P1: TPanel
            Left = 200
            Top = 100
            Width = 185
            Height = 105
            TabOrder = 0
            object TopLabel: TLabel
              Left = 1
              Top = 1
              Width = 183
              Height = 15
              Align = alTop
              Caption = 'T'
            end
          end
        end

        """;

    private static bool Set(DesignerHarness h, string name, string path, FormValue value) =>
        h.Controller.Commit("Undo_Designer_Property", [new SetProperty(name, path, value)]);

    private static Rect Stored(DesignerHarness h, string name) => PropertyAccess.GetBounds(h.Object(name));

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-088")]
    [Trait("Req", "DSG-RND-016")]
    public void CaptionEditStoresMeasuredBounds()
    {
        using var h = DesignerHarness.FromText(Form);
        var original = h.Text;

        // A caption edit stores the measured size in the edit's own undo unit; undo restores both.
        Assert.True(Set(h, "L1", "Caption", new StringValue("Hello world")));
        Assert.Equal(new Rect(100, 10, 66, 15), Stored(h, "L1"));
        Assert.Equal(new Rect(100, 10, 66, 15), h.Bounds("L1"));
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
        h.ExecuteRedo();
        Assert.Equal(new Rect(100, 10, 66, 15), Stored(h, "L1"));

        // A font edit measures with the new font (24 px: 12 px per character, line height 30); the mnemonic is not counted.
        Assert.True(Set(h, "L1", "Font.Height", new IntegerValue(-24, false)));
        Assert.Equal(new Rect(100, 10, 132, 30), Stored(h, "L1"));
        Assert.True(Set(h, "L1", "Caption", new StringValue("&Name")));
        Assert.Equal(new Rect(100, 10, 48, 30), Stored(h, "L1"));
        Assert.True(Set(h, "L1", "ShowAccelChar", new IdentValue("False")));
        Assert.Equal(new Rect(100, 10, 60, 30), Stored(h, "L1"));

        // An empty caption: zero width, one line high.
        Assert.True(Set(h, "L1", "Caption", new StringValue("")));
        Assert.Equal(new Rect(100, 10, 0, 30), Stored(h, "L1"));

        // taRightJustify keeps the right edge (layout.md UIF-255).
        Assert.True(Set(h, "Right", "Caption", new StringValue("Hello")));
        Assert.Equal(new Rect(104, 40, 30, 15), Stored(h, "Right"));

        // AutoSize = False keeps the bounds; switching it to True measures.
        Assert.True(Set(h, "Fixed", "Caption", new StringValue("Hello world")));
        Assert.Equal(new Rect(100, 70, 34, 15), Stored(h, "Fixed"));
        Assert.True(Set(h, "Fixed", "AutoSize", new IdentValue("True")));
        Assert.Equal(new Rect(100, 70, 66, 15), Stored(h, "Fixed"));

        // An aligned label takes only the dimension its Align leaves free: alTop keeps the width of the align rect.
        Assert.True(Set(h, "TopLabel", "Font.Height", new IntegerValue(-24, false)));
        Assert.Equal(new Rect(1, 1, 183, 30), Stored(h, "TopLabel"));

        // A property that does not affect the text size measures nothing.
        Assert.True(h.Controller.Commit("Undo_Designer_Property", [new SetProperty("Right", "Width", new IntegerValue(80, false))]));
        Assert.True(Set(h, "Right", "Tag", new IntegerValue(5, false)));
        Assert.Equal(new Rect(104, 40, 80, 15), Stored(h, "Right"));

        // A label created by a palette click gets its measured caption size ("Label1": 36 × 15, not the default 34 × 15).
        h.Host.Arm("TLabel");
        h.ClickAt(h.Controller.Geometry.FormClientToSurface((13, 141)));
        Assert.Equal(new Rect(16, 144, 36, 15), Stored(h, "Label1"));
        Assert.Equal("Undo_Designer_Add", h.Undo.UndoTitleKey);

        // A pasted label is measured too.
        h.Select("Right");
        h.Execute(Commands.Copy);
        h.Select("F");
        h.Execute(Commands.Paste);
        var pasted = Assert.Single(h.Selected);
        Assert.Equal((30, 15), (Stored(h, pasted).Width, Stored(h, pasted).Height));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-088")]
    public void ManualResizeIsKept()
    {
        using var h = DesignerHarness.FromText(Form);
        h.Settings.NoSnapping();
        h.Select("L1");

        // Resize by handle stores the new size; it is not re-measured.
        h.PressHandle("L1", HandleKind.SE);
        h.MoveBy(20, 10);
        h.Release();
        Assert.Equal(new Rect(100, 10, 54, 25), Stored(h, "L1"));

        // Edits that do not change the text keep it, and so does moving the label.
        Assert.True(Set(h, "L1", "Tag", new IntegerValue(1, false)));
        Assert.True(Set(h, "L1", "Layout", new IdentValue("tlCenter")));
        Assert.True(Set(h, "L1", "Left", new IntegerValue(120, false)));
        Assert.Equal(new Rect(120, 10, 54, 25), Stored(h, "L1"));

        // Until the next caption edit.
        Assert.True(Set(h, "L1", "Caption", new StringValue("Hello")));
        Assert.Equal(new Rect(120, 10, 30, 15), Stored(h, "L1"));
    }
}
