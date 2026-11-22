using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Layout;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>
/// <see cref="SurfaceGeometry"/> and <see cref="HitTester"/> (design-surface.md §2, DSG-SUR-001, DSG-SUR-010 … 012; D-034).
/// Surface coordinates: the frame's outer corner is (16, 16), the form client origin (17, 47).
/// </summary>
public sealed class HitTestTests
{
    // Panel1 at surface (25, 55)–(225, 155); Btn (35, 65)–(110, 90); Over (85, 75)–(160, 100) is a later sibling.
    private const string Text = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object Panel1: TPanel
            Left = 8
            Top = 8
            Width = 200
            Height = 100
            object Btn: TButton
              Left = 10
              Top = 10
              Width = 75
              Height = 25
            end
            object Over: TButton
              Left = 60
              Top = 20
              Width = 75
              Height = 25
            end
            object Wide: TButton
              Left = 150
              Top = 60
              Width = 100
              Height = 20
            end
          end
          object Framed: TPanel
            Left = 220
            Top = 150
            Width = 100
            Height = 80
            BorderStyle = bsSingle
            object Edge: TButton
              Left = -1
              Top = 0
              Width = 30
              Height = 20
            end
          end
          object Zero: TButton
            Left = 300
            Top = 10
            Width = 0
            Height = 20
          end
          object Hidden: TButton
            Left = 300
            Top = 50
            Width = 50
            Height = 20
            Visible = False
          end
          object Tiny: TButton
            Left = 8
            Top = 250
            Width = 4
            Height = 4
          end
          object Timer1: TTimer
            DesignLeft = 8
            DesignTop = 8
          end
        end

        """;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-001")]
    [Trait("Req", "DSG-SUR-010")]
    public void OrderHandlesTrayControlsFormBackdrop()
    {
        var (form, hit) = Load();
        var g = hit.Geometry;
        Assert.Equal(new Rect(16, 16, 402, 332), g.FrameRect);
        Assert.Equal(new Rect(17, 47, 400, 300), g.FormClientRect);
        Assert.Equal(new Rect(16, 356, 402, 66), g.TrayRect);
        Assert.Equal(new Rect(35, 65, 75, 25), g.GetBounds(O(form, "Btn")));
        Assert.Equal((35, 65), g.ParentClientToSurface(O(form, "Panel1"), (10, 10)));
        Assert.Equal((10, 10), g.SurfaceToParentClient(O(form, "Panel1"), (35, 65)));
        Assert.Equal((25, 365), g.TrayToSurface((8, 8)));

        IReadOnlyList<FormObject> btn = [O(form, "Btn")];
        IReadOnlyList<FormObject> root = [form.Root];

        // (1) the primary's handle wins over the later sibling covering it.
        Assert.Equal(new HitResult(HitKind.Handle, O(form, "Btn"), HandleKind.SE), hit.HitTest(109, 89, btn));
        // (1) overlapping squares of a 4 × 4 control: SE before NW (and no mid-edge handles below 21 px).
        var tiny = O(form, "Tiny");
        Assert.Equal(HandleKind.SE, hit.HitTest(26, 298, [tiny]).Handle);
        Assert.Equal(4, hit.GetHandleSquares(tiny).Count);
        Assert.Equal(8, hit.GetHandleSquares(O(form, "Panel1")).Count);
        // (2) form handles only when the form is the sole selection.
        Assert.Equal(new HitResult(HitKind.FormHandle, form.Root, HandleKind.SE), hit.HitTest(417, 347, root));
        Assert.Equal(new HitResult(HitKind.FormHandle, form.Root, HandleKind.E), hit.HitTest(417, 181, root));
        Assert.Equal(HitKind.FormFrame, hit.HitTest(417, 347, btn).Kind);
        // (3) tray items.
        Assert.Equal(new HitResult(HitKind.TrayItem, O(form, "Timer1")), hit.HitTest(40.4, 379.5, root));
        // (4) controls: children before parents, later siblings before earlier ones.
        Assert.Equal(new HitResult(HitKind.Control, O(form, "Btn")), hit.HitTest(100, 70, btn));
        Assert.Equal(new HitResult(HitKind.Control, O(form, "Over")), hit.HitTest(100, 80, btn));
        Assert.Equal(new HitResult(HitKind.Control, O(form, "Panel1")), hit.HitTest(200, 140, root));
        // (5) form client area, (6) caption and border.
        Assert.Equal(new HitResult(HitKind.FormClient, form.Root), hit.HitTest(350, 250, btn));
        Assert.Equal(new HitResult(HitKind.FormFrame, form.Root), hit.HitTest(100, 30, btn));
        Assert.Equal(new HitResult(HitKind.FormFrame, form.Root), hit.HitTest(16, 200, btn));
        // (7) tray background, (8) backdrop.
        Assert.Equal(new HitResult(HitKind.Tray, null), hit.HitTest(300, 380, root));
        Assert.Equal(new HitResult(HitKind.Backdrop, null), hit.HitTest(600, 600, root));
        Assert.Equal(new HitResult(HitKind.Backdrop, null), hit.HitTest(5, 5, root));
        Assert.Equal(new HitResult(HitKind.Backdrop, null), hit.HitTest(15.4, 30, root)); // rounds to 15 (DSG-SUR-002)
        Assert.Equal(new HitResult(HitKind.FormFrame, form.Root), hit.HitTest(15.5, 30, root)); // rounds to 16
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-010")]
    [Trait("Req", "DSG-SUR-011")]
    public void ChildrenClippedToAncestors()
    {
        var (form, hit) = Load();
        IReadOnlyList<FormObject> root = [form.Root];

        // Wide spans (175, 115)–(275, 135); its parent Panel1 ends at x = 225.
        Assert.Equal(O(form, "Wide"), hit.HitTest(200, 120, root).Object);
        Assert.Equal(new HitResult(HitKind.FormClient, form.Root), hit.HitTest(250, 120, root));

        // Framed (bsSingle) has its client origin at (1, 1): Edge at client x = -1 covers the border column, which is not hit.
        Assert.Equal(new Rect(238, 198, 98, 78), hit.Geometry.GetClientRect(O(form, "Framed")));
        Assert.Equal(new Rect(237, 198, 30, 20), hit.Geometry.GetBounds(O(form, "Edge")));
        Assert.Equal(O(form, "Framed"), hit.HitTest(237, 200, root).Object);
        Assert.Equal(O(form, "Edge"), hit.HitTest(240, 200, root).Object);

        // A zero-width control is not hit-testable; an invisible one is.
        Assert.Equal(new HitResult(HitKind.FormClient, form.Root), hit.HitTest(317, 60, root));
        Assert.Equal(new HitResult(HitKind.Control, O(form, "Hidden")), hit.HitTest(330, 100, root));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-012")]
    public void AltClickSelectsBehind()
    {
        var (form, hit) = Load();
        IReadOnlyList<FormObject> root = [form.Root];
        var (btn, panel) = (O(form, "Btn"), O(form, "Panel1"));

        Assert.Equal(new HitResult(HitKind.Control, panel), hit.HitTest(100, 70, root, alt: true));
        Assert.Equal(new HitResult(HitKind.FormClient, form.Root), hit.HitTest(100, 70, root, alt: true, altPrevious: panel));
        Assert.Equal(new HitResult(HitKind.Control, btn), hit.HitTest(100, 70, root, alt: true, altPrevious: form.Root));
        Assert.Equal(new HitResult(HitKind.Control, panel), hit.HitTest(100, 70, root, alt: true, altPrevious: btn));

        // Only the form at the point: Alt has nothing behind to reach.
        Assert.Equal(new HitResult(HitKind.FormClient, form.Root), hit.HitTest(350, 250, root, alt: true));
        // Without Alt the front-most control.
        Assert.Equal(new HitResult(HitKind.Control, btn), hit.HitTest(100, 70, root));
    }

    private static (FormDocument Form, HitTester Hit) Load()
    {
        // The framework metadata: the layout engine recognises containers by their framework ancestry (DSG-LAY-055).
        var form = LayoutHarness.Parse(Text);
        var engine = LayoutHarness.Engine;
        return (form, new HitTester(new SurfaceGeometry(form, engine.Compute(form), engine.Renderers)));
    }

    private static FormObject O(FormDocument form, string name) => form.FindByName(name)!;
}
