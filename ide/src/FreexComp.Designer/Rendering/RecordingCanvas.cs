using System.Globalization;
using System.Text;

namespace FreexComp.Designer.Rendering;

/// <summary>
/// One recorded drawing operation. <see cref="Text"/> is its golden line (design-renderers.md §6);
/// <see cref="Extent"/> is the area it may paint, in absolute canvas coordinates (translations applied) and
/// intersected with the clip in force, as (left, top, right, bottom); null when nothing can be painted.
/// </summary>
public sealed record DrawOp(string Kind, string Text, (double Left, double Top, double Right, double Bottom)? Extent);

/// <summary>
/// The test canvas (DSG-RND-003): records an ordered list of <see cref="DrawOp"/> and measures text with the
/// deterministic rule (6 px per character at 12 px, scaled linearly, bold × 1.1, rounded up) and the metrics of
/// DSG-RND-012.
/// </summary>
public sealed class RecordingCanvas : IDesignCanvas
{
    private readonly List<DrawOp> _ops = [];
    private readonly Stack<(double X, double Y)> _offsets = new([(0.0, 0.0)]);
    private readonly Stack<(double L, double T, double R, double B)> _clips =
        new([(double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity)]);

    public IReadOnlyList<DrawOp> Ops => _ops;

    /// <summary>The golden text: one op per line, LF.</summary>
    public string ToText() => string.Concat(_ops.Select(op => op.Text + "\n"));

    /// <summary>The metrics rule of DSG-RND-012 (also the default metrics of <see cref="RenderContext"/>).</summary>
    public static TextMetrics Metrics(TextStyle style) => new(
        (int)Math.Round(style.SizePx * 0.83, MidpointRounding.AwayFromZero),
        (int)Math.Round(style.SizePx * 0.25, MidpointRounding.AwayFromZero),
        (int)Math.Round(style.SizePx * 1.25, MidpointRounding.AwayFromZero));

    /// <summary>The width rule of DSG-RND-003 for one line of text.</summary>
    public static int MeasureWidth(string text, TextStyle style)
    {
        var width = 6.0 * text.Length * style.SizePx / 12.0 * (style.Bold ? 1.1 : 1.0);
        return (int)Math.Ceiling(Math.Round(width, 6)); // the rounding absorbs binary noise (6 × 1.1 ≠ 6.6 exactly)
    }

    public void FillRect(Rect r, Rgba color) => Add("FillRect", $"{R(r)} {color}", RectExtent(r));

    public void DrawRect(Rect r, Rgba color, double thickness = 1, LineStyle style = LineStyle.Solid) =>
        Add("DrawRect", $"{R(r)} {color} {N(thickness)} {style}", RectExtent(r));

    public void DrawLine(double x1, double y1, double x2, double y2, Rgba color, double thickness = 1, LineStyle style = LineStyle.Solid)
    {
        // Butt caps: the stroke is the segment widened by half the thickness on both sides; a zero-length line paints nothing.
        var length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        (double, double, double, double)? extent = null;
        if (length > 0)
        {
            var nx = Math.Abs(-(y2 - y1) / length * thickness / 2);
            var ny = Math.Abs((x2 - x1) / length * thickness / 2);
            extent = (Math.Min(x1, x2) - nx, Math.Min(y1, y2) - ny, Math.Max(x1, x2) + nx, Math.Max(y1, y2) + ny);
        }

        Add("DrawLine", $"{N(x1)},{N(y1)},{N(x2)},{N(y2)} {color} {N(thickness)} {style}", extent);
    }

    public void FillEllipse(Rect r, Rgba color) => Add("FillEllipse", $"{R(r)} {color}", RectExtent(r));

    public void DrawEllipse(Rect r, Rgba color, double thickness = 1) =>
        Add("DrawEllipse", $"{R(r)} {color} {N(thickness)}", RectExtent(r));

    public void FillPolygon(IReadOnlyList<Point> points, Rgba color) =>
        Add("FillPolygon", $"{P(points)} {color}", PointsExtent(points, 0));

    public void DrawPolyline(IReadOnlyList<Point> points, Rgba color, double thickness = 1) =>
        Add("DrawPolyline", $"{P(points)} {color} {N(thickness)}", PointsExtent(points, thickness / 2));

    public void FillHatch(Rect r, Rgba color, double spacing) =>
        Add("FillHatch", $"{R(r)} {color} {N(spacing)}", RectExtent(r));

    public void DrawText(string text, Rect bounds, TextStyle style, Rgba color, HAlign h = HAlign.Left, VAlign v = VAlign.Center,
        bool wordWrap = false, bool ellipsis = false, bool mnemonic = false, TextRotation rotation = TextRotation.None)
    {
        var flags = new StringBuilder();
        foreach (var (on, name) in new[] { (style.Bold, "bold"), (style.Italic, "italic"), (style.Underline, "underline"),
                     (style.StrikeOut, "strikeout"), (wordWrap, "wrap"), (ellipsis, "ellipsis"), (mnemonic, "mnemonic"),
                     (rotation == TextRotation.Up, "up"), (rotation == TextRotation.Down, "down") })
        {
            if (on)
            {
                flags.Append(' ').Append(name);
            }
        }

        Add("DrawText", $"{Quote(text)} {R(bounds)} {Quote(style.Family)} {N(style.SizePx)} {color} {h} {v}{flags}", RectExtent(bounds));
    }

    public Size MeasureText(string text, TextStyle style, double maxWidth = double.PositiveInfinity)
    {
        var lineHeight = Metrics(style).LineHeight;
        var width = 0;
        var lines = 0;
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var w = MeasureWidth(line, style);
            if (maxWidth >= 1 && w > maxWidth)
            {
                lines += (int)Math.Ceiling(w / maxWidth);
                w = (int)Math.Floor(maxWidth);
            }
            else
            {
                lines++;
            }

            width = Math.Max(width, w);
        }

        return new Size(width, lines * lineHeight);
    }

    public TextMetrics GetMetrics(TextStyle style) => Metrics(style);

    public void DrawIcon(string iconKey, Rect bounds) => Add("DrawIcon", $"{Quote(iconKey)} {R(bounds)}", RectExtent(bounds));

    public IDisposable PushClip(Rect r)
    {
        Add("PushClip", R(r), null);
        var (x, y) = _offsets.Peek();
        var c = _clips.Peek();
        _clips.Push((Math.Max(c.L, r.Left + x), Math.Max(c.T, r.Top + y), Math.Min(c.R, r.Right + x), Math.Min(c.B, r.Bottom + y)));
        return new Pop(() =>
        {
            _clips.Pop();
            Add("PopClip", "", null);
        });
    }

    public IDisposable PushTranslate(double dx, double dy)
    {
        Add("PushTranslate", $"{N(dx)},{N(dy)}", null);
        var (x, y) = _offsets.Peek();
        _offsets.Push((x + dx, y + dy));
        return new Pop(() =>
        {
            _offsets.Pop();
            Add("PopTranslate", "", null);
        });
    }

    public IDisposable PushOpacity(double opacity)
    {
        Add("PushOpacity", N(opacity), null);
        return new Pop(() => Add("PopOpacity", "", null));
    }

    private void Add(string kind, string args, (double L, double T, double R, double B)? local)
    {
        (double, double, double, double)? extent = null;
        if (local is { } e)
        {
            var (x, y) = _offsets.Peek();
            var c = _clips.Peek();
            var l = Math.Max(e.L + x, c.L);
            var t = Math.Max(e.T + y, c.T);
            var r = Math.Min(e.R + x, c.R);
            var b = Math.Min(e.B + y, c.B);
            extent = r > l && b > t ? (l, t, r, b) : null;
        }

        _ops.Add(new DrawOp(kind, args.Length == 0 ? kind : kind + " " + args, extent));
    }

    private static (double, double, double, double) RectExtent(Rect r) => (r.Left, r.Top, r.Right, r.Bottom);

    private static (double, double, double, double)? PointsExtent(IReadOnlyList<Point> points, double half) =>
        points.Count == 0 ? null
            : (points.Min(p => p.X) - half, points.Min(p => p.Y) - half, points.Max(p => p.X) + half, points.Max(p => p.Y) + half);

    private static string R(Rect r) => string.Create(CultureInfo.InvariantCulture, $"{r.Left},{r.Top},{r.Width},{r.Height}");

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string P(IReadOnlyList<Point> points) => string.Join(" ", points.Select(p => $"{N(p.X)},{N(p.Y)}"));

    private static string Quote(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                < ' ' => $"\\x{(int)ch:X2}",
                _ => ch.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    private sealed class Pop(Action action) : IDisposable
    {
        private Action? _action = action;

        public void Dispose()
        {
            _action?.Invoke();
            _action = null;
        }
    }
}
