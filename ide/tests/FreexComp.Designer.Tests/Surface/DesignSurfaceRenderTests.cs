using System.Globalization;
using System.Text;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Drawing the design surface (design-surface.md §14, ui/designer.md §2 … §7, §10; D-057) on a <see cref="RecordingCanvas"/>.</summary>
public sealed class DesignSurfaceRenderTests
{
    private static readonly Rect Viewport = new(0, 0, 2000, 2000);

    private const string Form = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object P1: TPanel
            Left = 200
            Top = 100
            Width = 185
            Height = 105
            TabOrder = 0
            object Inner: TButton
              Left = 8
              Top = 8
              Width = 75
              Height = 25
              TabOrder = 0
            end
          end
          object B1: TButton
            Left = 8
            Top = 200
            Width = 75
            Height = 25
            TabOrder = 1
          end
          object B2: TButton
            Left = 96
            Top = 240
            Width = 75
            Height = 25
            TabOrder = 2
          end
          object Hidden: TEdit
            Left = 8
            Top = 8
            Width = 121
            Height = 23
            TabOrder = 3
            Visible = False
          end
          object Timer1: TTimer
            DesignLeft = 8
            DesignTop = 8
          end
        end

        """;

    private static RecordingCanvas Render(DesignerHarness h, Rect? viewport = null)
    {
        var canvas = new RecordingCanvas();
        h.Controller.Render(canvas, viewport ?? Viewport);
        return canvas;
    }

    private static string Hex(Rgba c) => c.ToString();

    /// <summary>The index of the first op matching <paramref name="match"/>; fails naming <paramref name="what"/>.</summary>
    private static int First(RecordingCanvas canvas, string what, Func<DrawOp, bool> match)
    {
        for (var i = 0; i < canvas.Ops.Count; i++)
        {
            if (match(canvas.Ops[i]))
            {
                return i;
            }
        }

        Assert.Fail($"no op for {what} in:\n{canvas.ToText()}");
        return -1;
    }

    private static int Last(RecordingCanvas canvas, Func<DrawOp, bool> match)
    {
        for (var i = canvas.Ops.Count - 1; i >= 0; i--)
        {
            if (match(canvas.Ops[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>An op of <paramref name="kind"/> in <paramref name="color"/> whose painted extent starts at (x, y).</summary>
    private static Func<DrawOp, bool> At(string kind, Rgba color, double x, double y) =>
        op => op.Kind == kind && op.Text.Contains(Hex(color), StringComparison.Ordinal) && op.Extent is { } e && e.Left == x && e.Top == y;

    /// <summary>A filled 7 × 7 handle square (<c>designer.handle.fill</c> is also the colour of captions and edit fields).</summary>
    private static Func<DrawOp, bool> IsHandleFill(SurfacePalette p) =>
        op => op.Kind == "FillRect" && op.Text.EndsWith($",7,7 {Hex(p.HandleFill)}", StringComparison.Ordinal);

    private static Func<DrawOp, bool> Colored(string kind, Rgba color) =>
        op => op.Kind == kind && op.Text.Contains(Hex(color), StringComparison.Ordinal);

    /// <summary>Asserts that <paramref name="expected"/> occur in this order as consecutive ops.</summary>
    private static void AssertBlock(RecordingCanvas canvas, params string[] expected)
    {
        var lines = canvas.Ops.Select(o => o.Text).ToList();
        for (var i = 0; i + expected.Length <= lines.Count; i++)
        {
            if (lines.Skip(i).Take(expected.Length).SequenceEqual(expected))
            {
                return;
            }
        }

        Assert.Fail($"block not found:\n{string.Join('\n', expected)}\nin:\n{canvas.ToText()}");
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-180")]
    [Trait("Req", "DSG-SUR-181")]
    [Trait("Req", "DSG-RND-007")]
    [Trait("Req", "UI-DSG-021")]
    [Trait("Req", "UI-DSG-041")]
    [Trait("Req", "UI-DSG-060")]
    public void DrawOrderMatchesSpec()
    {
        using var h = DesignerHarness.FromText(Form);
        var p = h.Controller.Palette;
        var form = p.Renderers;
        h.Select("B2", "B1");
        var canvas = Render(h);
        var g = h.Controller.Geometry;

        // Backdrop, the frame, the form client, its grid, the children back to front (each subtree before the next sibling,
        // the panel's own grid after its background and before its child), state overlays, the tray, then the overlays.
        var backdrop = First(canvas, "backdrop", op => op.Text == $"FillRect 0,0,2000,2000 {Hex(p.Backdrop)}");
        Assert.Equal(0, backdrop);
        var frame = First(canvas, "frame", At("DrawRect", form.BorderDark, 16, 16));
        var client = First(canvas, "form client", At("FillRect", form.Face, 17, 47));
        var formGrid = First(canvas, "form grid", At("FillRect", p.GridDot, 17, 47));
        var p1 = g.GetBounds(h.Object("P1"));
        var p1Client = g.GetClientRect(h.Object("P1"));
        var panel = First(canvas, "P1", At("FillRect", form.Face, p1.Left, p1.Top));
        var panelGrid = First(canvas, "P1 grid", At("FillRect", p.GridDot, p1Client.Left, p1Client.Top));
        var inner = g.GetBounds(h.Object("Inner"));
        var innerOp = First(canvas, "Inner", op => op.Extent is { } e && e.Left == inner.Left && e.Top == inner.Top && op.Kind == "FillRect"
            && !op.Text.Contains(Hex(p.GridDot), StringComparison.Ordinal));
        var b1 = g.GetBounds(h.Object("B1"));
        var b1Op = First(canvas, "B1", op => op.Extent is { } e && e.Left == b1.Left && e.Top == b1.Top && op.Kind == "FillRect"
            && !op.Text.Contains(Hex(p.GridDot), StringComparison.Ordinal));
        var hidden = g.GetBounds(h.Object("Hidden"));
        var hatch = First(canvas, "hatch over Hidden", op => op.Kind == "FillHatch" && op.Extent is { } e && e.Left == hidden.Left
            && op.Text.EndsWith(" #6D6D6D59 6", StringComparison.Ordinal));
        var tray = First(canvas, "tray", At("FillRect", p.Panel, g.TrayRect.Left, g.TrayRect.Top));
        var tile = g.GetTrayTile(h.Object("Timer1"));
        var timer = First(canvas, "Timer1 tile", At("FillRect", form.TileBackground, tile.Left, tile.Top));
        var timerName = First(canvas, "Timer1 name", op => op.Kind == "DrawText" && op.Text.StartsWith("DrawText \"Timer1\"", StringComparison.Ordinal));
        var secondary = First(canvas, "secondary outline", op => op.Text.StartsWith($"DrawRect {R(g.GetBounds(h.Object("B2")))} {Hex(p.HandleSecondary)} 1 Dash", StringComparison.Ordinal));
        var primary = First(canvas, "primary outline", op => op.Text == $"DrawRect {R(b1)} {Hex(p.HandleBorder)} 1 Solid");
        int[] order = [backdrop, frame, client, formGrid, panel, panelGrid, innerOp, b1Op, hatch, tray, timer, timerName, secondary, primary];
        Assert.Equal(order.Order(), order);

        // During a move with re-parenting and a guideline: drop target, secondary, primary, guideline, drag tip — in that order.
        h.Settings.SnapToGrid = false;
        var innerLeft = inner.Left;
        h.Press("B1");
        var start = h.SurfacePoint("B1");
        var (dx, dy) = (innerLeft + 2 - b1.Left, p1Client.Top + 52 - b1.Top);
        h.MoveTo((start.X + dx / 2, start.Y + dy / 2));
        h.MoveTo((start.X + dx, start.Y + dy));
        Assert.Equal(h.Object("P1"), h.Controller.DropTarget);
        Assert.Contains(h.Guidelines, l => l.Axis == SnapAxis.X && l.Kind == GuidelineKind.Edge);
        canvas = Render(h);
        var drop = First(canvas, "drop target", op => op.Text == $"DrawRect {R(p1)} {Hex(p.DropTarget)} 2 Solid");
        secondary = First(canvas, "secondary outline", Colored("DrawRect", p.HandleSecondary));
        primary = First(canvas, "primary outline", op => op.Kind == "DrawRect" && op.Text.EndsWith($"{Hex(p.HandleBorder)} 1 Solid", StringComparison.Ordinal)
            && op.Extent is { } e && e.Right - e.Left == 75);
        var guide = First(canvas, "edge guideline", Colored("DrawLine", p.GuidelineEdge));
        var pointer = (X: start.X + dx, Y: start.Y + dy);
        var tipBox = First(canvas, "drag tip box", At("FillRect", p.Raised, pointer.X + 12, pointer.Y + 12));
        var tipText = First(canvas, "drag tip text", op => op.Kind == "DrawText" && op.Text.Contains("\"Cascadia Mono\" 12", StringComparison.Ordinal));
        order = [Last(canvas, op => op.Kind == "FillRect" && op.Text.Contains(Hex(p.Panel), StringComparison.Ordinal)), drop, secondary, primary, guide, tipBox, tipText];
        Assert.Equal(order.Order(), order);

        // The drag tip shows the anchor's new Left, Top in its (new) parent, 12 DIP right of and below the pointer.
        var moved = h.Controller.Layout.GetBounds(h.Object("B1"));
        Assert.Contains($"\"{moved.Left}, {moved.Top}\"", canvas.Ops[tipText].Text, StringComparison.Ordinal);
        Assert.Equal(20.0, canvas.Ops[tipBox].Extent!.Value.Bottom - canvas.Ops[tipBox].Extent!.Value.Top);
        h.Key(DesignKey.Escape);

        // A lasso is drawn in the overlay layer, fill then dashed border, clipped to the container's client area.
        h.PressAt(g.FormClientToSurface((10, 60)));
        h.MoveBy(50, 40);
        canvas = Render(h);
        var lasso = First(canvas, "lasso", op => Colored("DrawRect", p.RubberBandBorder)(op) && op.Text.EndsWith(" Dash", StringComparison.Ordinal));
        var lassoFill = First(canvas, "lasso fill", Colored("FillRect", p.RubberBandFill));
        Assert.True(Last(canvas, Colored("FillRect", p.Panel)) < lassoFill && lassoFill < lasso, canvas.ToText());
        Assert.Equal((27.0, 107.0, 77.0, 147.0), canvas.Ops[lasso].Extent);
        h.Key(DesignKey.Escape);

        // Tab-order mode: badges last, no handles, no grid.
        h.Execute(Commands.TabOrder);
        canvas = Render(h);
        var badge = First(canvas, "badge", Colored("FillRect", p.TabBadgeBackground));
        Assert.True(badge > Last(canvas, Colored("FillRect", p.Panel)));
        Assert.DoesNotContain(canvas.Ops, op => IsHandleFill(p)(op));
        Assert.DoesNotContain(canvas.Ops, op => Colored("FillRect", p.GridDot)(op));
        h.Controller.ExitTabOrderMode();

        // Render is side-effect free (DSG-SUR-181): the same state draws the same ops; the model is untouched.
        var text = h.Text;
        Assert.Equal(Render(h).ToText(), Render(h).ToText());
        Assert.Equal(text, h.Text);
    }

    private static string R(Rect r) => string.Create(CultureInfo.InvariantCulture, $"{r.Left},{r.Top},{r.Width},{r.Height}");

    private static string Square(int cx, int cy, int size = 7) => R(new Rect(cx - size / 2, cy - size / 2, size, size));

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "UI-DSG-030")]
    [Trait("Req", "UI-DSG-031")]
    [Trait("Req", "UI-DSG-032")]
    [Trait("Req", "UI-DSG-033")]
    [Trait("Req", "UI-DSG-034")]
    [Trait("Req", "UI-DSG-014")]
    [Trait("Req", "UI-DSG-038")]
    [Trait("Req", "UI-DSG-063")]
    public void SelectionHandlesPrimaryAndSecondary()
    {
        using var h = DesignerHarness.FromFile("forms/canonical/hello.form");
        var p = h.Controller.Palette;
        var (border, fill, secondary) = (Hex(p.HandleBorder), Hex(p.HandleFill), Hex(p.HandleSecondary));

        // Primary GreetButton (249, 62, 75 × 25): outline on the bounds and eight filled handles centred on the outline
        // pixels (x 249 / 286 / 323, y 62 / 74 / 86). Secondary NameEdit (33, 63, 200 × 23): dashed outline, four hollow corners.
        h.Select("NameEdit", "GreetButton");
        var canvas = Render(h);
        AssertBlock(canvas,
            $"DrawRect 33,63,200,23 {secondary} 1 Dash",
            $"DrawRect {Square(33, 63)} {secondary} 1 Solid",
            $"DrawRect {Square(232, 63)} {secondary} 1 Solid",
            $"DrawRect {Square(232, 85)} {secondary} 1 Solid",
            $"DrawRect {Square(33, 85)} {secondary} 1 Solid");
        var primary = new List<string> { $"DrawRect 249,62,75,25 {border} 1 Solid" };
        foreach (var (x, y) in new[] { (249, 62), (286, 62), (323, 62), (323, 74), (323, 86), (286, 86), (249, 86), (249, 74) })
        {
            primary.Add($"FillRect {Square(x, y)} {fill}");
            primary.Add($"DrawRect {Square(x, y)} {border} 1 Solid");
        }

        AssertBlock(canvas, [.. primary]);

        // The handle size follows designer.handleSize.
        h.Settings.HandleSize = 9;
        AssertBlock(Render(h), $"FillRect {Square(249, 62, 9)} {fill}", $"DrawRect {Square(249, 62, 9)} {border} 1 Solid");
        h.Settings.HandleSize = 7;

        // Lock Controls: the primary outline and the handle borders use designer.handle.locked.
        h.Settings.LockControls = true;
        var locked = Hex(p.HandleLocked);
        AssertBlock(Render(h), $"DrawRect 249,62,75,25 {locked} 1 Solid", $"FillRect {Square(249, 62)} {fill}", $"DrawRect {Square(249, 62)} {locked} 1 Solid");
        h.Settings.LockControls = false;

        // The form as the only selection: three handles E, S, SE on the frame (16, 16, 402 × 272), no outline.
        h.Select("MainForm");
        canvas = Render(h);
        AssertBlock(canvas,
            $"FillRect {Square(417, 151)} {fill}", $"DrawRect {Square(417, 151)} {border} 1 Solid",
            $"FillRect {Square(417, 287)} {fill}", $"DrawRect {Square(417, 287)} {border} 1 Solid",
            $"FillRect {Square(216, 287)} {fill}", $"DrawRect {Square(216, 287)} {border} 1 Solid");
        Assert.Equal(3, canvas.Ops.Count(IsHandleFill(p)));
        Assert.DoesNotContain(canvas.Ops, op => op.Text.StartsWith("DrawRect 16,16,402,272 " + border, StringComparison.Ordinal));

        // Narrow / low controls lose the mid-edge handles of that axis (< 3 × handle size).
        using var small = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 200
              ClientHeight = 100
              object C: TCheckBox
                Left = 10
                Top = 10
                Width = 16
                Height = 17
                TabOrder = 0
              end
            end

            """);
        small.Select("C");
        Assert.Equal(4, Render(small).Ops.Count(IsHandleFill(p)));

        // A tray item: outline around the 32 × 32 tile with four corner handles; a preserved item: secondary outline, no handles.
        using var tray = DesignerHarness.FromFile("forms/canonical/nonvisual.form");
        var item = tray.Document.Root.Children.First(c => tray.Controller.Geometry.IsTrayItem(c));
        var tile = tray.Controller.Geometry.GetTrayTile(item);
        tray.Select(item.Name);
        canvas = Render(tray);
        AssertBlock(canvas,
            $"DrawRect {R(tile)} {border} 1 Solid",
            $"FillRect {Square(tile.Left, tile.Top)} {fill}", $"DrawRect {Square(tile.Left, tile.Top)} {border} 1 Solid",
            $"FillRect {Square(tile.Right - 1, tile.Top)} {fill}", $"DrawRect {Square(tile.Right - 1, tile.Top)} {border} 1 Solid",
            $"FillRect {Square(tile.Right - 1, tile.Bottom - 1)} {fill}", $"DrawRect {Square(tile.Right - 1, tile.Bottom - 1)} {border} 1 Solid",
            $"FillRect {Square(tile.Left, tile.Bottom - 1)} {fill}", $"DrawRect {Square(tile.Left, tile.Bottom - 1)} {border} 1 Solid");

        using var preserved = DesignerHarness.FromText("""
            object F: TForm
              ClientWidth = 200
              ClientHeight = 100
              object X: TFancyWidget
                Left = 10
                Top = 10
                Width = 50
                Height = 30
              end
            end

            """, allowDiagnostics: true);
        preserved.Select("X");
        canvas = Render(preserved);
        Assert.Contains(canvas.Ops, op => op.Text == $"DrawRect 27,57,50,30 {secondary} 1 Solid");
        Assert.DoesNotContain(canvas.Ops, op => IsHandleFill(p)(op));

        // A read-only form draws every selection in the read-only style (UI-DSG-092).
        h.Select("NameEdit", "GreetButton");
        h.Controller.IsReadOnly = true;
        canvas = Render(h);
        Assert.Contains(canvas.Ops, op => op.Text == $"DrawRect 249,62,75,25 {secondary} 1 Solid");
        Assert.Contains(canvas.Ops, op => op.Text == $"DrawRect 33,63,200,23 {secondary} 1 Solid");
        Assert.DoesNotContain(canvas.Ops, op => IsHandleFill(p)(op));
        Assert.DoesNotContain(canvas.Ops, op => Colored("FillRect", p.GridDot)(op));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "UI-DSG-020")]
    [Trait("Req", "UI-DSG-021")]
    [Trait("Req", "UI-DSG-022")]
    [Trait("Req", "UI-DSG-023")]
    public void GridDotsOnContainers()
    {
        using var h = DesignerHarness.FromText(Form);
        var dot = h.Controller.Palette.GridDot;
        var g = h.Controller.Geometry;
        HashSet<(double, double)> Dots(RecordingCanvas c) =>
            c.Ops.Where(Colored("FillRect", dot)).Select(op => (op.Extent!.Value.Left, op.Extent!.Value.Top)).ToHashSet();

        // Every client coordinate with x mod 8 = 0 and y mod 8 = 0, on the form (400 × 300: 50 × 38 dots) and on the panel
        // relative to the panel's own client origin; every dot is 1 × 1.
        var canvas = Render(h);
        Assert.All(canvas.Ops.Where(Colored("FillRect", dot)), op => Assert.EndsWith($",1,1 {Hex(dot)}", op.Text, StringComparison.Ordinal));
        var dots = Dots(canvas);
        var form = g.FormClientRect;
        var panel = g.GetClientRect(h.Object("P1"));
        var expected = new List<(double, double)>();
        for (var x = 0; x < form.Width; x += 8)
        {
            for (var y = 0; y < form.Height; y += 8)
            {
                expected.Add((form.Left + x, form.Top + y));
            }
        }

        var panelDots = 0;
        for (var x = 0; x < panel.Width; x += 8)
        {
            for (var y = 0; y < panel.Height; y += 8)
            {
                expected.Add((panel.Left + x, panel.Top + y));
                panelDots++;
            }
        }

        Assert.Equal(expected.Count, canvas.Ops.Count(Colored("FillRect", dot)));
        Assert.True(dots.SetEquals(expected), $"{expected.Except(dots).Count()} missing, {dots.Except(expected).Count()} unexpected");
        Assert.Equal(50 * 38 + panelDots, expected.Count);
        Assert.True(panelDots > 0);

        // The grid sizes follow the settings; below 4 DIP every second position is drawn (never closer than 4 DIP).
        h.Settings.GridSizeX = 10;
        h.Settings.GridSizeY = 2;
        dots = Dots(Render(h));
        Assert.Contains((form.Left + 10.0, form.Top + 4.0), dots);
        Assert.DoesNotContain((form.Left + 10.0, form.Top + 2.0), dots);
        Assert.DoesNotContain((form.Left + 5.0, form.Top + 4.0), dots);
        h.Settings.GridSizeX = 8;
        h.Settings.GridSizeY = 8;

        // Only inside the viewport (the dots are clipped to it anyway; fewer are issued).
        var part = Render(h, new Rect(0, 0, 100, 100));
        Assert.All(Dots(part), d => Assert.True(d.Item1 < 100 && d.Item2 < 100));
        Assert.NotEmpty(Dots(part));

        // No grid when designer.showGrid is off, in tab-order mode, or on a read-only form.
        h.Settings.ShowGrid = false;
        Assert.Empty(Dots(Render(h)));
        h.Settings.ShowGrid = true;
        h.Execute(Commands.TabOrder);
        Assert.Empty(Dots(Render(h)));
        h.Controller.ExitTabOrderMode();
        h.Controller.IsReadOnly = true;
        Assert.Empty(Dots(Render(h)));
    }

    /// <summary>DSG-SUR-182: spec budget for rendering a form of 500 controls on the recording canvas.</summary>
    private const double RenderBudgetMilliseconds = 16;

    /// <summary>DSG-SUR-182: the CI factor on the spec budget.</summary>
    private const double CiFactor = 5;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Category", "Performance")]
    [Trait("Req", "DSG-SUR-182")]
    public void Renders500ControlsWithinBudget()
    {
        using var h = DesignerHarness.FromText(LargeForm(panels: 10, controlsPerPanel: 49));
        Assert.Equal(500, h.Document.AllObjects().Count(o => o.Parent is not null));
        h.Select("P0C0", "P9C48");
        var viewport = new Rect(0, 0, 1280, 1024);
        var ops = Render(h, viewport).Ops.Count; // computes and caches the layout of this model version (DSG-SUR-181)

        var fastest = PerformanceBudget.MinThreadCpu(() => h.Controller.Render(new RecordingCanvas(), viewport));

        Assert.True(ops > 500, $"{ops} ops");
        var budget = PerformanceBudget.Of(RenderBudgetMilliseconds * CiFactor, typeof(DesignSurfaceController).Assembly);
        Assert.True(budget.IsMetBy(fastest), budget.Describe("Rendering 500 controls took", fastest));
    }

    /// <summary>10 panels with 49 buttons, edits, check boxes and labels each, on a 1200 × 900 form.</summary>
    private static string LargeForm(int panels, int controlsPerPanel)
    {
        var s = new StringBuilder("object Large: TForm\n  ClientWidth = 1200\n  ClientHeight = 900\n");
        for (var p = 0; p < panels; p++)
        {
            s.Append(CultureInfo.InvariantCulture,
                $"  object Panel{p}: TPanel\n    Left = {8 + p % 4 * 296}\n    Top = {8 + p / 4 * 296}\n    Width = 288\n    Height = 288\n    TabOrder = {p}\n");
            var tab = 0;
            for (var c = 0; c < controlsPerPanel; c++)
            {
                var (x, y) = (2 + c % 3 * 95, 2 + c / 3 * 17);
                var (cls, extra) = (c % 4) switch
                {
                    0 => ("TButton", "      Caption = 'Button'\n"),
                    1 => ("TEdit", "      Text = 'Edit'\n"),
                    2 => ("TCheckBox", "      Caption = 'Check'\n"),
                    _ => ("TLabel", "      Caption = 'Label'\n"),
                };
                var tabLine = cls == "TLabel" ? "" : $"      TabOrder = {tab++}\n";
                s.Append(CultureInfo.InvariantCulture,
                    $"    object P{p}C{c}: {cls}\n      Left = {x}\n      Top = {y}\n      Width = 90\n      Height = {(cls == "TEdit" ? 23 : 15)}\n{extra}{tabLine}    end\n");
            }

            s.Append("  end\n");
        }

        return s.Append("end\n").ToString();
    }
}
