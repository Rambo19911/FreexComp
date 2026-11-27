using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Rendering;

/// <summary>
/// Device-pixel snapping of the design canvas (ide/phase8/highdpi.md DSG-RND-027, 028; D-152): the rule
/// (<see cref="DeviceSnap"/>) and the snapped operation stream of a <see cref="RecordingCanvas"/> with a render scale.
/// </summary>
[Trait("Phase", "8")]
public sealed class DeviceSnapTests
{
    private static readonly Rgba Ink = new(0, 0, 0);

    /// <summary>The device pixels an operation paints: its extent times the scale (whole numbers when snapped).</summary>
    private static (double Left, double Top, double Right, double Bottom) Device(DrawOp op, double scale)
    {
        var e = Assert.NotNull(op.Extent);
        return (Math.Round(e.Left * scale, 9), Math.Round(e.Top * scale, 9), Math.Round(e.Right * scale, 9), Math.Round(e.Bottom * scale, 9));
    }

    [Fact]
    [Trait("Req", "DSG-RND-027")]
    public void LinesSnapAt150()
    {
        // The rule: a 1 DIP line is max(1, round(1 · 1.5)) = 2 device pixels wide and starts at round(row · 1.5), halves up.
        var snap = new DeviceSnap(1.5);
        Assert.Equal(2, snap.Pixels(1));
        Assert.Equal((5, 2), snap.Row(3.5, 1));    // pixel row 3: 4.5 → device rows 5 and 6
        Assert.Equal((6, 2), snap.Row(4.5, 1));    // pixel row 4: 6 → device rows 6 and 7
        Assert.Equal((5, 2), snap.Column(3.5, 1));
        Assert.Equal(new PixelBox(2, 2, 6, 6), snap.Box(1, 1, 3, 3)); // edges 1.5 → 2 and 6 → 6
        Assert.Equal(1, new DeviceSnap(1.25).Pixels(1));
        Assert.Equal(3, new DeviceSnap(1.25).Pixels(2));

        // A 1 DIP HLine on row 3 from column 2 to 10: device rows 5 … 6, columns 3 … 14; in DIP its centre is
        // (5 + 2 / 2) / 1.5 = 4 and its pen 2 / 1.5.
        var canvas = new RecordingCanvas(1.5);
        canvas.HLine(2, 10, 3, Ink);
        Assert.Equal((3, 5, 15, 7), Device(canvas.Ops[0], 1.5));
        Assert.Equal("DrawLine 2,4,10,4 #000000FF 1.333 Solid", canvas.Ops[0].Text);

        // A VLine on column 3 from row 1 to 4: device columns 5 … 6, rows 2 … 5.
        canvas.VLine(3, 1, 4, Ink);
        Assert.Equal((5, 2, 7, 6), Device(canvas.Ops[1], 1.5));

        // A 2 DIP line between rows 3 and 4 (centre 4): 3 device rows from round(3 · 1.5) = 5.
        canvas.DrawLine(2, 4, 10, 4, Ink, 2);
        Assert.Equal((3, 5, 15, 8), Device(canvas.Ops[2], 1.5));

        // Rect (1, 1, 3, 3): edges at device 2 and 6, filled and stroked alike; the 1 DIP outline is a 2 pixel ring inside.
        canvas.FillRect(new Rect(1, 1, 3, 3), Ink);
        canvas.DrawRect(new Rect(1, 1, 3, 3), Ink);
        Assert.Equal((2, 2, 6, 6), Device(canvas.Ops[3], 1.5));
        Assert.Equal((2, 2, 6, 6), Device(canvas.Ops[4], 1.5));
        Assert.EndsWith(" #000000FF 1.333 Solid", canvas.Ops[4].Text, StringComparison.Ordinal);

        // Adjacent rects share one device edge (4.5 → 5 for both): no seam, no overlap.
        canvas.FillRect(new Rect(1, 1, 2, 3), Ink);
        canvas.FillRect(new Rect(3, 1, 4, 3), Ink);
        Assert.Equal(Device(canvas.Ops[5], 1.5).Right, Device(canvas.Ops[6], 1.5).Left);
        Assert.Equal(5, Device(canvas.Ops[6], 1.5).Left);

        // Snapping is absolute: the same rect under a translation lands on the same device pixels.
        using (canvas.PushTranslate(1, 1))
        {
            canvas.FillRect(new Rect(0, 0, 3, 3), Ink);
            Assert.Equal((2, 2, 6, 6), Device(canvas.Ops[^1], 1.5));
            canvas.HLine(1, 9, 2, Ink);
            Assert.Equal((3, 5, 15, 7), Device(canvas.Ops[^1], 1.5));
        }

        // A clip is a rect: its edges snap like the fills it bounds.
        using (canvas.PushClip(new Rect(1, 1, 3, 3)))
        {
            canvas.FillRect(new Rect(0, 0, 10, 10), Ink);
            Assert.Equal((2, 2, 6, 6), Device(canvas.Ops[^1], 1.5));
        }

        // Diagonals are not DSG-RND-001 lines: unchanged.
        canvas.DrawLine(0.5, 0.5, 6.5, 6.5, Ink);
        Assert.Equal("DrawLine 0.5,0.5,6.5,6.5 #000000FF 1 Solid", canvas.Ops[^1].Text);
    }

    [Fact]
    [Trait("Req", "DSG-RND-028")]
    public void FixedSizeRectsKeepTheirDevicePixelSize()
    {
        // A 7 DIP handle square is round(7 · 1.5) = 11 device pixels wherever it is; its rect edges alone would give 10 or 11.
        var snap = new DeviceSnap(1.5);
        Assert.Equal(new[] { 10, 11 }, Enumerable.Range(0, 8).Select(x => snap.Box(x, 0, 7, 7).Width).Distinct().Order());
        Assert.All(Enumerable.Range(0, 8), x =>
        {
            var box = snap.Box(x, x, 7, 7, fixedSize: true);
            Assert.Equal((11, 11), (box.Width, box.Height));
            Assert.Equal((snap.Box(x, x, 7, 7).Left, snap.Box(x, x, 7, 7).Top), (box.Left, box.Top)); // the corner is an edge
        });
        Assert.All(Enumerable.Range(0, 8), x => Assert.Equal(2, snap.Box(x, 0, 1, 1, fixedSize: true).Width)); // a grid dot

        var canvas = new RecordingCanvas(1.5);
        using (canvas.PushFixedSize())
        {
            canvas.FillRect(new Rect(1, 1, 7, 7), Ink);
            canvas.DrawRect(new Rect(1, 1, 7, 7), Ink);
        }

        canvas.FillRect(new Rect(1, 1, 7, 7), Ink);
        Assert.Equal(3, canvas.Ops.Count); // the scope itself is not an operation
        Assert.Equal((2, 2, 13, 13), Device(canvas.Ops[0], 1.5)); // from round(1 · 1.5) = 2, 11 pixels
        Assert.Equal((2, 2, 13, 13), Device(canvas.Ops[1], 1.5));
        Assert.Equal((2, 2, 12, 12), Device(canvas.Ops[2], 1.5)); // after the scope: edges again (8 · 1.5 = 12)

        // At whole scales a fixed-size rect is the rect itself.
        foreach (var scale in (double[])[1.0, 2.0])
        {
            Assert.All(Enumerable.Range(0, 8), x =>
                Assert.Equal(new DeviceSnap(scale).Box(x, 0, 7, 7), new DeviceSnap(scale).Box(x, 0, 7, 7, fixedSize: true)));
        }
    }

    private const string Form = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object P1: TPanel
            Left = 201
            Top = 99
            Width = 185
            Height = 105
            TabOrder = 0
            object Inner: TButton
              Left = 7
              Top = 9
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
          object E1: TEdit
            Left = 9
            Top = 8
            Width = 121
            Height = 23
            TabOrder = 2
          end
          object Hidden: TEdit
            Left = 96
            Top = 241
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

    /// <summary>
    /// The surface states that draw every kind of chrome: a multi-selection with grid, a move drag with guidelines and
    /// the drag tip, a lasso, the form alone (its resize handles) and tab-order mode.
    /// </summary>
    private static IEnumerable<(string State, Func<double?, string> Render)> States(DesignerHarness h)
    {
        string Render(double? scale)
        {
            var canvas = scale is { } s ? new RecordingCanvas(s) : new RecordingCanvas();
            h.Controller.Render(canvas, new Rect(0, 0, 2000, 2000));
            return canvas.ToText();
        }

        h.Select("E1", "B1");
        yield return ("selection", Render);

        h.Settings.SnapToGrid = false;
        h.Select("B1");
        h.Press("B1");
        h.MoveBy(0, -40);
        h.MoveBy(1, -40); // left edges of B1 and E1 in line
        Assert.NotEmpty(h.Guidelines);
        yield return ("move with guidelines", Render);
        h.Key(DesignKey.Escape);

        h.PressAt(h.Controller.Geometry.FormClientToSurface((10, 60)));
        h.MoveBy(50, 40);
        yield return ("lasso", Render);
        h.Key(DesignKey.Escape);

        h.Select("F");
        yield return ("form selected", Render);

        h.Execute(Commands.TabOrder);
        yield return ("tab order", Render);
        h.Controller.ExitTabOrderMode();
    }

    [Fact]
    [Trait("Req", "DSG-RND-027")]
    public void IdentityAt100And200()
    {
        using var h = DesignerHarness.FromText(Form);
        var states = 0;
        foreach (var (state, render) in States(h))
        {
            states++;
            var unsnapped = render(null);
            Assert.Contains("DrawLine ", unsnapped, StringComparison.Ordinal);
            Assert.True(unsnapped == render(1.0), state + ": the stream snapped at 1.0 differs from the unsnapped one");
            Assert.True(unsnapped == render(2.0), state + ": the stream snapped at 2.0 differs from the unsnapped one");
            Assert.False(unsnapped == render(1.5), state + ": nothing is snapped at 1.5"); // the comparison sees snapping
        }

        Assert.Equal(5, states);
    }

    /// <summary>At 1.5 every rect and every row or column line of the whole surface lies on whole device pixels.</summary>
    [Fact]
    [Trait("Req", "DSG-RND-027")]
    [Trait("Req", "DSG-RND-028")]
    public void SurfaceStreamAt150IsOnDevicePixels()
    {
        using var h = DesignerHarness.FromText(Form);
        var p = h.Controller.Palette;
        var (dots, handles) = (0, 0);
        foreach (var (state, _) in States(h))
        {
            var canvas = new RecordingCanvas(1.5);
            h.Controller.Render(canvas, new Rect(0, 0, 2000, 2000));
            var checkedOps = 0;
            foreach (var op in canvas.Ops.Where(o => o.Kind is "FillRect" or "DrawRect" || (o.Kind == "DrawLine" && IsAxisAligned(o))))
            {
                if (op.Extent is not { } e)
                {
                    continue; // clipped away
                }

                checkedOps++;
                foreach (var edge in (double[])[e.Left, e.Top, e.Right, e.Bottom])
                {
                    var device = edge * 1.5;
                    Assert.True(Math.Abs(device - Math.Round(device)) < 1e-9, $"{state}: {op.Text} has an edge at device {device}");
                }
            }

            Assert.True(checkedOps > 20, $"{state}: only {checkedOps} operations checked");

            // DSG-RND-028: every handle is 11 × 11 and every grid dot 2 × 2 device pixels, whatever its position.
            var handleBorders = new[] { p.HandleBorder.ToString(), p.HandleSecondary.ToString() };
            foreach (var op in canvas.Ops.Where(o => o.Extent is not null))
            {
                var (l, t, r, b) = Device(op, 1.5);
                if (op.Kind == "FillRect" && op.Text.EndsWith(" " + p.GridDot, StringComparison.Ordinal))
                {
                    Assert.True((r - l, b - t) == (2, 2), $"{state}: grid dot {op.Text} is {r - l} × {b - t}");
                    dots++;
                }
                else if (op.Kind == "DrawRect" && handleBorders.Any(c => op.Text.EndsWith($" {c} 1.333 Solid", StringComparison.Ordinal)) && r - l < 20)
                {
                    Assert.True((r - l, b - t) == (11, 11), $"{state}: handle {op.Text} is {r - l} × {b - t}");
                    handles++;
                }
            }
        }

        // Seen: the grid of the form and the panel in four states; 4 + 8 handles of the two selected controls, 8 of the
        // dragged one, 8 with the lasso, 3 of the form.
        Assert.True(dots > 4000, $"{dots} grid dots");
        Assert.Equal(4 + 8 + 8 + 8 + 3, handles);

        static bool IsAxisAligned(DrawOp op)
        {
            var c = op.Text.Split(' ')[1].Split(',');
            return c[0] == c[2] || c[1] == c[3];
        }
    }
}
