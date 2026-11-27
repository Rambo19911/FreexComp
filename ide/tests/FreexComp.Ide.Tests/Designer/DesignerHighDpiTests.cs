using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Tests.Views;
using DesignRect = FreexComp.Designer.Rect;
using Point = Avalonia.Point;

namespace FreexComp.Ide.Tests.Designer;

/// <summary>
/// The design surface at render scales 100 %, 150 % and 200 % (ide/phase8/highdpi.md §2, DSG-RND-027 … 029; D-152):
/// the device pixels the real renderer draws — control bounds, the thickness of the chrome — and hit testing of a grab
/// handle on the pixels drawn for it. Every test runs its body in the Skia process of the test (<see cref="SkiaProcess"/>).
/// <see cref="ChromeThicknessIsPhysical"/> also writes the window at each scale to <c>artifacts/visual/designer-highdpi/</c>.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class DesignerHighDpiTests
{
    private static readonly string Unit = DesignerViewTests.Unit.ReplaceLineEndings("\n")
        .Replace("  Classes, Controls, Forms, StdCtrls;\n", "  Classes, Controls, Forms, StdCtrls, ExtCtrls;\n", StringComparison.Ordinal)
        .Replace("    GreetButton: TButton;\n", "    GreetButton: TButton;\n    InfoPanel: TPanel;\n    InnerButton: TButton;\n", StringComparison.Ordinal);

    /// <summary>A button, an edit and a panel with a child; odd coordinates, so that at 150 % edges fall on half device pixels.</summary>
    private const string FormText = """
        object MainForm: TMainForm
          Left = 200
          Top = 120
          Caption = 'MainForm'
          ClientWidth = 400
          ClientHeight = 300
          PixelsPerInch = 96
          object NameEdit: TEdit
            Left = 17
            Top = 16
            Width = 200
            Height = 23
            TabOrder = 0
          end
          object GreetButton: TButton
            Left = 232
            Top = 15
            Width = 75
            Height = 25
            Caption = '&Greet'
            TabOrder = 1
            OnClick = GreetButtonClick
          end
          object InfoPanel: TPanel
            Left = 23
            Top = 81
            Width = 185
            Height = 105
            TabOrder = 2
            object InnerButton: TButton
              Left = 9
              Top = 11
              Width = 75
              Height = 25
              Caption = 'Inner'
              TabOrder = 0
            end
          end
        end

        """;

    private static readonly string[] Controls = ["InnerButton", "InfoPanel", "NameEdit", "GreetButton"];

    private sealed record Session(HeadlessHarness H, FormDocumentViewModel Document, DesignSurfaceControl Control)
    {
        public DesignSurfaceController Surface => Document.Designer.Surface!;

        public double Scale => H.Window.RenderScaling;

        public FormObject Object(string name) => Document.Designer.Model!.FindByName(name)!;

        public DesignRect Bounds(string name) => Surface.Geometry.GetBounds(Object(name));

        public int Width(string name) => (int)((IntegerValue)PropertyAccess.GetEffective(Object(name), "Width")).Value;

        public void Select(string name)
        {
            Surface.Selection.Select(Object(name));
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>A surface point (DIP) in main-window coordinates.</summary>
        private Point At(double x, double y) => Control.TranslatePoint(new Point(x, y), H.Window)!.Value;

        public void Down(double x, double y) => Run(() => H.Window.MouseDown(At(x, y), MouseButton.Left));

        public void Move(double x, double y) => Run(() => H.Window.MouseMove(At(x, y)));

        public void Up(double x, double y) => Run(() => H.Window.MouseUp(At(x, y), MouseButton.Left));

        /// <summary>The window comes to a monitor of another scale (UI-TOK-069): everything is drawn again at it.</summary>
        public void SetScale(double scale)
        {
            Run(() => H.Window.SetRenderScaling(scale));
            Assert.Equal(scale, Scale);
        }

        private static void Run(Action action)
        {
            action();
            Dispatcher.UIThread.RunJobs();
        }

        public Frame Capture() => new(H.Window, Control);
    }

    /// <summary>The device pixels of the design surface in a rendered frame of the window; coordinates from the surface's origin.</summary>
    private sealed class Frame
    {
        private readonly int[] _pixels;
        private readonly int _stride;
        private readonly bool _rgba;
        private readonly int _originX;
        private readonly int _originY;

        public Frame(Window window, Control surface)
        {
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("No frame was rendered: not in the Skia process of the test (SkiaProcess).");
            var scale = window.RenderScaling;
            var origin = surface.TranslatePoint(default, window)!.Value * scale;

            // Layout rounding puts the surface on a whole device pixel at every scale (the snapping relies on it).
            Assert.True(Math.Abs(origin.X - Math.Round(origin.X)) < 1e-6 && Math.Abs(origin.Y - Math.Round(origin.Y)) < 1e-6,
                $"the design surface starts at device {origin}");
            (_originX, _originY) = ((int)Math.Round(origin.X), (int)Math.Round(origin.Y));
            Width = Math.Min(frame.PixelSize.Width - _originX, (int)Math.Floor(surface.Bounds.Width * scale));
            Height = Math.Min(frame.PixelSize.Height - _originY, (int)Math.Floor(surface.Bounds.Height * scale));
            using var locked = frame.Lock();
            _rgba = locked.Format == PixelFormat.Rgba8888;
            Assert.True(_rgba || locked.Format == PixelFormat.Bgra8888, locked.Format.ToString());
            _stride = frame.PixelSize.Width;
            _pixels = new int[_stride * frame.PixelSize.Height];
            for (var y = 0; y < frame.PixelSize.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(locked.Address + y * locked.RowBytes, _pixels, y * _stride, _stride);
        }

        public int Width { get; }

        public int Height { get; }

        public Rgba this[int x, int y]
        {
            get
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height)
                    Assert.Fail($"({x}, {y}) is outside the surface {Width} × {Height}");
                var p = _pixels[(_originY + y) * _stride + _originX + x];
                var (b0, b1, b2) = ((byte)p, (byte)(p >> 8), (byte)(p >> 16));
                return _rgba ? new Rgba(b0, b1, b2) : new Rgba(b2, b1, b0);
            }
        }

        /// <summary>The smallest rectangle (left, top, right, bottom; right and bottom exclusive) around the pixels of <paramref name="area"/> that match.</summary>
        public (int Left, int Top, int Right, int Bottom)? Extent((int Left, int Top, int Right, int Bottom) area, Func<int, int, bool> match)
        {
            (int L, int T, int R, int B)? box = null;
            for (var y = Math.Max(0, area.Top); y < Math.Min(Height, area.Bottom); y++)
            {
                for (var x = Math.Max(0, area.Left); x < Math.Min(Width, area.Right); x++)
                {
                    if (match(x, y))
                    {
                        box = box is { } b ? (Math.Min(b.L, x), Math.Min(b.T, y), Math.Max(b.R, x + 1), Math.Max(b.B, y + 1)) : (x, y, x + 1, y + 1);
                    }
                }
            }

            return box;
        }

        /// <summary>The smallest rectangle around the pixels that differ from <paramref name="other"/>.</summary>
        public (int Left, int Top, int Right, int Bottom)? Changed(Frame other)
        {
            Assert.Equal((_originX, _originY, Width, Height, _stride), (other._originX, other._originY, other.Width, other.Height, other._stride));
            return Extent((0, 0, Width, Height), (x, y) => _pixels[(_originY + y) * _stride + _originX + x] != other._pixels[(_originY + y) * _stride + _originX + x]);
        }
    }

    private static void Files(HarnessOptions o)
    {
        o.InitialFiles["MainForm.pas"] = Unit.ReplaceLineEndings("\r\n");
        o.InitialFiles["MainForm.form"] = FormText.ReplaceLineEndings("\r\n");
        o.Settings["designer.snapToGrid"] = false;
        o.WindowSize = new Size(1040, 720); // every frame is rendered whole: a smaller window, less to render
    }

    private static async Task<Session> OpenDesignAsync(HeadlessHarness h)
    {
        var document = await DesignerViewTests.OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        var control = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        Assert.False(document.Designer.Surface!.IsReadOnly);
        Dispatcher.UIThread.RunJobs();

        // The whole form is in view (the tests read its pixels).
        var view = Assert.IsType<ScrollViewer>(control.FindAncestorOfType<ScrollViewer>());
        var frame = document.Designer.Surface!.Geometry.FrameRect;
        Assert.True(view.Offset == default && view.Viewport.Width >= frame.Right && view.Viewport.Height >= frame.Bottom,
            $"the form frame {frame} is not inside the view {view.Viewport} at {view.Offset}");
        return new Session(h, document, control);
    }

    /// <summary>Rounds half up: the device pixel edge of a DIP edge (DSG-RND-027).</summary>
    private static int Snap(double value) => (int)Math.Floor(value + 0.5);

    /// <summary>
    /// The device-pixel bounds of every sample control as drawn at <paramref name="scale"/>: the control is deleted and
    /// the pixels that change are its own (the form alone is selected in every frame, so the selection chrome does not
    /// change). The controls are back afterwards (undo).
    /// </summary>
    private static Dictionary<string, (int Left, int Top, int Right, int Bottom)> DrawnBounds(Session s, double scale,
        Dictionary<string, DesignRect>? logical = null)
    {
        s.SetScale(scale);
        var drawn = new Dictionary<string, (int, int, int, int)>();
        s.Select("MainForm");
        var before = s.Capture();
        foreach (var name in Controls)
        {
            logical?.Add(name, s.Bounds(name));
            s.Select(name);
            Assert.True(s.Surface.OnKey(new KeyInput(DesignKey.Delete, InputModifiers.None)), "Delete " + name);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(s.Document.Designer.Model!.FindByName(name));
            s.Select("MainForm");
            var after = s.Capture();
            drawn[name] = Assert.NotNull(after.Changed(before));
            before = after;
        }

        foreach (var name in Controls.Reverse())
        {
            s.Surface.Execute(Commands.Undo);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(s.Document.Designer.Model!.FindByName(name));
        }

        return drawn;
    }

    /// <summary>
    /// DSG-RND-027 (F11-007 acceptance 1): a button, an edit and a panel with a child are drawn at 200 % on exactly twice
    /// the device-pixel bounds they have at 100 % — where the bounds are the stored ones, pixel for pixel.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-RND-027")]
    public async Task BoundsAtScale2AreExactlyDouble()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(DesignerHighDpiTests));
            return;
        }

        await using var h = await HeadlessHarness.StartAsync(Files);
        var s = await OpenDesignAsync(h);
        var logical = new Dictionary<string, DesignRect>();
        var at1 = DrawnBounds(s, 1.0, logical);
        var at2 = DrawnBounds(s, 2.0);
        Assert.Equal(Controls.Order(), at1.Keys.Order());
        foreach (var name in Controls)
        {
            var b = logical[name];
            Assert.Equal((name, (b.Left, b.Top, b.Right, b.Bottom)), (name, at1[name]));
            var (l, t, r, bottom) = at1[name];
            Assert.Equal((name, (2 * l, 2 * t, 2 * r, 2 * bottom)), (name, at2[name]));
        }
    }

    /// <summary>
    /// DSG-RND-027: at 150 % every edge of a control lies on the device pixel its DIP edge rounds to (half a pixel
    /// rounds up) — a whole pixel, not one blended over two.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-RND-027")]
    public async Task BoundsAt150AreOnWholeDevicePixels()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(DesignerHighDpiTests));
            return;
        }

        await using var h = await HeadlessHarness.StartAsync(Files);
        var s = await OpenDesignAsync(h);
        var logical = new Dictionary<string, DesignRect>();
        var drawn = DrawnBounds(s, 1.5, logical);
        foreach (var name in Controls)
        {
            var b = logical[name];
            Assert.Equal((name, (Snap(b.Left * 1.5), Snap(b.Top * 1.5), Snap(b.Right * 1.5), Snap(b.Bottom * 1.5))), (name, drawn[name]));
        }

        // The sample has edges on half device pixels (odd DIP), which is what this test is about.
        Assert.Contains(Controls, name => logical[name].Left % 2 == 1 && logical[name].Top % 2 == 1);
    }

    /// <summary>
    /// DSG-RND-028: the chrome keeps its physical size — the selection frame line and a guideline are
    /// max(1, round(1 · s)) device pixels thick, a grab handle round(7 · s) device pixels square (the default
    /// <c>designer.handleSize</c>), each made of whole pixels of its own colour: at 100, 125, 150 and 200 % (UI-TOK-062),
    /// in one window whose scale changes (UI-TOK-069).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-RND-028")]
    [Trait("Req", "DSG-RND-027")]
    public async Task ChromeThicknessIsPhysical()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(DesignerHighDpiTests));
            return;
        }

        await using var h = await HeadlessHarness.StartAsync(Files);
        var s = await OpenDesignAsync(h);
        Chrome(s, 1.0, line: 1, handle: 7);
        Chrome(s, 1.25, line: 1, handle: 9);
        Chrome(s, 1.5, line: 2, handle: 11);
        Chrome(s, 2.0, line: 2, handle: 14);
    }

    private static void Chrome(Session s, double scale, int line, int handle)
    {
        s.SetScale(scale);
        var p = s.Surface.Palette;
        Assert.NotEqual(p.HandleBorder, p.HandleFill);
        var b = s.Bounds("GreetButton");
        s.Select("GreetButton");
        var frame = s.Capture();
        bool IsHandle(int x, int y) => frame[x, y] == p.HandleBorder || frame[x, y] == p.HandleFill;

        // The selection frame: its top line crossed between the NW and the N handle, its left line between NW and W.
        var column = Snap((b.Left + 20) * scale);
        var top = frame.Extent((column, Snap((b.Top - 4) * scale), column + 1, Snap((b.Top + 6) * scale)), (x, y) => frame[x, y] == p.HandleBorder);
        Assert.Equal((Snap(b.Top * scale), Snap(b.Top * scale) + line), (top?.Top, top?.Bottom));
        var row = Snap((b.Top + 6) * scale);
        var left = frame.Extent((Snap((b.Left - 4) * scale), row, Snap((b.Left + 6) * scale), row + 1), (x, y) => frame[x, y] == p.HandleBorder);
        Assert.Equal((Snap(b.Left * scale), Snap(b.Left * scale) + line), (left?.Left, left?.Right));

        // The NW handle, centred on the frame's corner pixel: above the frame's top line it is the only thing in its
        // colours (so the rows there give its width), left of the frame's left line likewise (its height).
        var around = (Left: Snap((b.Left - 8) * scale), Top: Snap((b.Top - 8) * scale), Right: Snap((b.Left + 8) * scale), Bottom: Snap((b.Top + 8) * scale));
        var above = Assert.NotNull(frame.Extent(around with { Bottom = Snap(b.Top * scale) }, IsHandle));
        var beside = Assert.NotNull(frame.Extent(around with { Right = Snap(b.Left * scale) }, IsHandle));
        Assert.Equal((handle, handle), (above.Right - above.Left, beside.Bottom - beside.Top));
        Assert.Equal((Snap((b.Left - 3) * scale), Snap((b.Top - 3) * scale)), (above.Left, above.Top));
        for (var y = above.Top; y < above.Bottom; y++)
        {
            for (var x = above.Left; x < above.Right; x++)
            {
                // Whole pixels: the border ring is `line` pixels of the border colour, the rest the fill.
                var ring = x - above.Left < line || above.Right - x <= line || y - above.Top < line;
                Assert.True(frame[x, y] == (ring ? p.HandleBorder : p.HandleFill), $"handle pixel ({x}, {y}) is {frame[x, y]}");
            }
        }

        // A guideline: the button dragged until its top edge is in line with the edit's (16); crossed in the gap between the two.
        var edit = s.Bounds("NameEdit");
        var (cx, cy) = (b.Left + 37, b.Top + 12);
        s.Down(cx, cy);
        s.Move(cx + 20, cy);
        s.Move(cx + 40, cy + (edit.Top - b.Top));
        Assert.Equal(InteractionState.Moving, s.Surface.State);
        var guide = Assert.Single(s.Surface.Guidelines, g => g is { Kind: GuidelineKind.Edge, Axis: SnapAxis.Y });
        var rect = Assert.NotNull(s.Surface.GuidelineRect(guide));
        Assert.Equal((edit.Top, 1), (rect.Top, rect.Height));
        frame = s.Capture();
        VisualSnapshot.Capture(s.H.Window, "designer-highdpi", "light"); // for the visual check: selection, handles, guideline, grid
        var gap = Snap((edit.Right + 6) * scale);
        Assert.True(edit.Right + 6 < b.Left + 40 && rect.Left <= edit.Right && rect.Right >= b.Left + 40);
        var drawn = frame.Extent((gap, Snap((rect.Top - 4) * scale), gap + 1, Snap((rect.Top + 5) * scale)), (x, y) => frame[x, y] == p.GuidelineEdge);
        Assert.Equal((Snap(rect.Top * scale), Snap(rect.Top * scale) + line), (drawn?.Top, drawn?.Bottom));

        // The drag is given up: the button is where it was for the next scale.
        Assert.True(s.Surface.OnKey(new KeyInput(DesignKey.Escape, InputModifiers.None)));
        s.Up(cx + 40, cy + (edit.Top - b.Top));
        Assert.Equal((InteractionState.Idle, b), (s.Surface.State, s.Bounds("GreetButton")));
    }

    /// <summary>
    /// DSG-SUR-010, DSG-RND-029 (F11-007 acceptance 3): at 150 % a press on the centre of the SE handle and a press on
    /// the outermost device pixel drawn for it both resize the button by the drag distance; a press one DIP outside the
    /// 11 × 11 hit square does not.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-RND-029")]
    [Trait("Req", "DSG-SUR-010")]
    public async Task HandleHitAt150()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(DesignerHighDpiTests));
            return;
        }

        const double Scale = 1.5;
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            Files(o);
            o.Settings["designer.useGuidelines"] = false;
        });
        var s = await OpenDesignAsync(h);
        s.SetScale(Scale);
        var p = s.Surface.Palette;
        s.Select("GreetButton");
        Assert.Equal(75, s.Width("GreetButton"));

        // The centre of the SE handle: the frame's bottom-right pixel.
        var b = s.Bounds("GreetButton");
        var (cx, cy) = (b.Right - 1, b.Bottom - 1);
        s.Down(cx, cy);
        s.Move(cx + 5, cy);
        Assert.Equal(InteractionState.Resizing, s.Surface.State);
        s.Move(cx + 10, cy);
        s.Up(cx + 10, cy);
        Assert.Equal((85, 25), (s.Width("GreetButton"), s.Bounds("GreetButton").Height));

        // The outermost device pixel drawn for the handle (bottom-right of the pixels in the handle's border colour
        // around the corner), pressed at its centre.
        b = s.Bounds("GreetButton");
        (cx, cy) = (b.Right - 1, b.Bottom - 1);
        var frame = s.Capture();
        var drawn = Assert.NotNull(frame.Extent((Snap((cx - 8) * Scale), Snap((cy - 8) * Scale), Snap((cx + 9) * Scale), Snap((cy + 9) * Scale)),
            (x, y) => frame[x, y] == p.HandleBorder));
        var (px, py) = ((drawn.Right - 0.5) / Scale, (drawn.Bottom - 0.5) / Scale);
        Assert.True(px > cx + 3 && py > cy + 3, $"the handle's outermost pixel is at ({px}, {py}), its centre at ({cx}, {cy})");
        s.Down(px, py);
        s.Move(px + 5, py);
        Assert.Equal(InteractionState.Resizing, s.Surface.State);
        s.Move(px + 10, py);
        s.Up(px + 10, py);
        Assert.Equal((95, 25), (s.Width("GreetButton"), s.Bounds("GreetButton").Height));

        // One DIP outside the hit square (centre ± 5): nothing is resized.
        b = s.Bounds("GreetButton");
        (cx, cy) = (b.Right - 1, b.Bottom - 1);
        s.Down(cx + 6, cy + 6);
        s.Move(cx + 11, cy + 6);
        s.Move(cx + 16, cy + 6);
        Assert.NotEqual(InteractionState.Resizing, s.Surface.State);
        s.Up(cx + 16, cy + 6);
        Assert.Equal((95, 25), (s.Width("GreetButton"), s.Bounds("GreetButton").Height));
    }
}
