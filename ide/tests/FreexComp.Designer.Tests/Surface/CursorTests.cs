using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>The pointer cursor the surface asks for (ui/designer.md UI-DSG-036, UI-DSG-034, design-surface.md DSG-SUR-057, 084; D-078).</summary>
public sealed class CursorTests
{
    private const string Form = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object B1: TButton
            Left = 40
            Top = 40
            Width = 75
            Height = 25
            TabOrder = 0
          end
          object Top1: TPanel
            Left = 0
            Top = 0
            Width = 400
            Height = 30
            Align = alTop
            TabOrder = 1
          end
        end

        """;

    private static CursorKind At(DesignerHarness h, (int X, int Y) p) => h.Controller.GetCursor(p.X, p.Y);

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "UI-DSG-036")]
    [Trait("Req", "UI-DSG-034")]
    [Trait("Req", "DSG-SUR-084")]
    public void CursorFollowsHitAndState()
    {
        using var h = DesignerHarness.FromText(Form);

        // Unselected control and the form background: arrow. Selected control body: SizeAll; its handles: size cursors.
        Assert.Equal(CursorKind.Arrow, At(h, h.SurfacePoint("B1", (10, 10))));
        Assert.Equal(CursorKind.Arrow, At(h, h.Controller.Geometry.FormClientToSurface((300, 200))));
        h.Select("B1");
        Assert.Equal(CursorKind.SizeAll, At(h, h.SurfacePoint("B1", (30, 10))));
        Assert.Equal(CursorKind.SizeNWSE, At(h, h.HandlePoint("B1", HandleKind.NW)));
        Assert.Equal(CursorKind.SizeNWSE, At(h, h.HandlePoint("B1", HandleKind.SE)));
        Assert.Equal(CursorKind.SizeNESW, At(h, h.HandlePoint("B1", HandleKind.NE)));
        Assert.Equal(CursorKind.SizeNESW, At(h, h.HandlePoint("B1", HandleKind.SW)));
        Assert.Equal(CursorKind.SizeNS, At(h, h.HandlePoint("B1", HandleKind.N)));
        Assert.Equal(CursorKind.SizeWE, At(h, h.HandlePoint("B1", HandleKind.E)));

        // An aligned control: handles of fixed edges are inert (arrow), the free edge sizes.
        h.Select("Top1");
        Assert.Equal(CursorKind.SizeNS, At(h, h.HandlePoint("Top1", HandleKind.S)));
        Assert.Equal(CursorKind.Arrow, At(h, h.HandlePoint("Top1", HandleKind.N)));

        // The form's E/S/SE handles when it is the sole selection.
        h.Select("F");
        var frame = h.Controller.Geometry.FrameRect;
        Assert.Equal(CursorKind.SizeNWSE, At(h, (frame.Right - 1, frame.Bottom - 1)));

        // Lock Controls: arrows over handles and bodies (UI-DSG-034).
        h.Select("B1");
        h.Settings.LockControls = true;
        Assert.Equal(CursorKind.Arrow, At(h, h.HandlePoint("B1", HandleKind.SE)));
        Assert.Equal(CursorKind.Arrow, At(h, h.SurfacePoint("B1", (30, 10))));
        h.Settings.LockControls = false;

        // During a move drag: SizeAll, and the copy badge exactly while Ctrl is held (DSG-SUR-057).
        h.Press("B1", (30, 10));
        h.MoveBy(20, 0);
        Assert.Equal(CursorKind.SizeAll, At(h, (0, 0)));
        Assert.False(h.Controller.CopyBadge);
        h.MoveBy(4, 0, InputModifiers.Ctrl);
        Assert.True(h.Controller.CopyBadge);
        h.CaptureLost();
        Assert.False(h.Controller.CopyBadge);

        // During a resize drag: the dragged handle's cursor wherever the pointer is.
        h.PressHandle("B1", HandleKind.E);
        h.MoveBy(30, 0);
        Assert.Equal(CursorKind.SizeWE, At(h, (0, 0)));
        h.CaptureLost();

        // An armed palette item: crosshair everywhere on the surface.
        h.Host.Arm("TButton");
        Assert.Equal(CursorKind.Cross, At(h, h.SurfacePoint("B1", (30, 10))));
        Assert.Equal(CursorKind.Cross, At(h, (2, 2)));
    }
}
