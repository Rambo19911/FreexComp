using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using FreexComp.Designer.Rendering;
using FreexComp.Ide.Icons;
using DesignPoint = FreexComp.Designer.Rendering.Point;
using DesignRect = FreexComp.Designer.Rect;
using DesignSize = FreexComp.Designer.Size;

namespace FreexComp.Ide.Designer;

/// <summary>
/// <see cref="IDesignCanvas"/> over an Avalonia <see cref="DrawingContext"/> (design-renderers.md §1; D-078). Coordinates are
/// DIP; line styles: dash 2 on / 2 off, dot 1 on / 2 off. Icons come from the IDE icon set (<c>builtin:&lt;name&gt;</c> or a
/// name), coloured by <paramref name="brushForToken"/>; an unknown icon draws the generic component glyph (an outline square).
/// </summary>
public sealed class AvaloniaDesignCanvas(DrawingContext context, Func<string, IBrush?> brushForToken) : IDesignCanvas
{
    private const string GenericIconToken = "text.tertiary";
    private static readonly ImmutableDashStyle Dash = new([2, 2], 0);
    private static readonly ImmutableDashStyle Dot = new([1, 2], 0);

    private readonly Dictionary<Rgba, IBrush> _brushes = [];

    public void FillRect(DesignRect r, Rgba color) => context.FillRectangle(Brush(color), ToRect(r));

    public void DrawRect(DesignRect r, Rgba color, double thickness = 1, LineStyle style = LineStyle.Solid)
    {
        var half = thickness / 2;
        var rect = new Rect(r.Left + half, r.Top + half, Math.Max(0, r.Width - thickness), Math.Max(0, r.Height - thickness));
        context.DrawRectangle(null, Pen(color, thickness, style), rect);
    }

    public void DrawLine(double x1, double y1, double x2, double y2, Rgba color, double thickness = 1, LineStyle style = LineStyle.Solid) =>
        context.DrawLine(Pen(color, thickness, style), new Avalonia.Point(x1, y1), new Avalonia.Point(x2, y2));

    public void FillEllipse(DesignRect r, Rgba color) =>
        context.DrawEllipse(Brush(color), null, new Avalonia.Point(r.Left + r.Width / 2.0, r.Top + r.Height / 2.0), r.Width / 2.0, r.Height / 2.0);

    public void DrawEllipse(DesignRect r, Rgba color, double thickness = 1)
    {
        var half = thickness / 2;
        context.DrawEllipse(null, Pen(color, thickness, LineStyle.Solid), new Avalonia.Point(r.Left + r.Width / 2.0, r.Top + r.Height / 2.0),
            Math.Max(0, r.Width / 2.0 - half), Math.Max(0, r.Height / 2.0 - half));
    }

    public void FillPolygon(IReadOnlyList<DesignPoint> points, Rgba color)
    {
        if (points.Count >= 3)
        {
            context.DrawGeometry(Brush(color), null, new PolylineGeometry(points.Select(ToPoint), isFilled: true));
        }
    }

    public void DrawPolyline(IReadOnlyList<DesignPoint> points, Rgba color, double thickness = 1)
    {
        if (points.Count >= 2)
        {
            context.DrawGeometry(null, Pen(color, thickness, LineStyle.Solid), new PolylineGeometry(points.Select(ToPoint), isFilled: false));
        }
    }

    public void FillHatch(DesignRect r, Rgba color, double spacing)
    {
        if (r.Width <= 0 || r.Height <= 0 || spacing <= 0)
        {
            return;
        }

        var pen = Pen(color, 1, LineStyle.Solid);
        using (context.PushClip(ToRect(r)))
        {
            for (var d = 0.0; d < r.Width + r.Height; d += spacing)
            {
                context.DrawLine(pen, new Avalonia.Point(r.Left + d, r.Top), new Avalonia.Point(r.Left + d - r.Height, r.Bottom));
            }
        }
    }

    public void DrawText(string text, DesignRect bounds, TextStyle style, Rgba color, HAlign h = HAlign.Left, VAlign v = VAlign.Center,
        bool wordWrap = false, bool ellipsis = false, bool mnemonic = false)
    {
        var (shown, underline) = mnemonic ? StripMnemonic(text) : (text, -1);
        var formatted = Format(shown, style, Brush(color));
        formatted.TextAlignment = h switch { HAlign.Center => TextAlignment.Center, HAlign.Right => TextAlignment.Right, _ => TextAlignment.Left };
        formatted.MaxTextWidth = Math.Max(0, bounds.Width);
        if (!wordWrap)
        {
            formatted.MaxLineCount = 1;
        }

        if (ellipsis)
        {
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        if (underline >= 0)
        {
            formatted.SetTextDecorations(TextDecorations.Underline, underline, 1);
        }

        var y = v switch
        {
            VAlign.Center => bounds.Top + (bounds.Height - formatted.Height) / 2,
            VAlign.Bottom => bounds.Bottom - formatted.Height,
            _ => bounds.Top,
        };
        using (context.PushClip(ToRect(bounds)))
        {
            context.DrawText(formatted, new Avalonia.Point(bounds.Left, y));
        }
    }

    public DesignSize MeasureText(string text, TextStyle style, double maxWidth = double.PositiveInfinity)
    {
        var formatted = Format(text, style, Brushes.Black);
        if (!double.IsInfinity(maxWidth))
        {
            formatted.MaxTextWidth = Math.Max(0, maxWidth);
        }

        return new DesignSize((int)Math.Ceiling(formatted.WidthIncludingTrailingWhitespace), (int)Math.Ceiling(formatted.Height));
    }

    public TextMetrics GetMetrics(TextStyle style)
    {
        var formatted = Format("Xg", style, Brushes.Black);
        var lineHeight = (int)Math.Ceiling(formatted.Height);
        var ascent = (int)Math.Round(formatted.Baseline);
        return new TextMetrics(ascent, Math.Max(0, lineHeight - ascent), lineHeight);
    }

    public void DrawIcon(string iconKey, DesignRect bounds)
    {
        var builtin = iconKey.StartsWith("builtin:", StringComparison.Ordinal);
        var name = builtin ? iconKey["builtin:".Length..] : iconKey;

        // D-121 (UI-DSG-062): a metadata icon is the SVG file the component's palette entry names, drawn as the Tool
        // Palette draws it (DSG-TP-009) in the icon colour; a file that is not usable gives the generic glyph.
        if (!builtin && Views.PaletteIcon.Load(iconKey) is { } svg)
        {
            Views.PaletteIcon.Draw(context, svg, brushForToken(IconCatalog.DefaultToken), ToRect(bounds));
            return;
        }

        if (!IconCatalog.TryGet(name, out var icon))
        {
            if (brushForToken(GenericIconToken) is { } generic)
            {
                var inset = bounds.Width / 4.0;
                context.DrawRectangle(null, new Pen(generic, IconCatalog.StrokeThickness),
                    new Rect(bounds.Left + inset, bounds.Top + inset, bounds.Width - 2 * inset, bounds.Height - 2 * inset));
            }

            return;
        }

        using (context.PushTransform(Matrix.CreateScale(bounds.Width / 16.0, bounds.Height / 16.0) * Matrix.CreateTranslation(bounds.Left, bounds.Top)))
        {
            foreach (var layer in icon.Layers)
            {
                if (brushForToken(layer.ColorToken) is not { } brush)
                {
                    continue;
                }

                if (layer.IsFilled)
                {
                    context.DrawGeometry(brush, null, layer.Geometry);
                }
                else
                {
                    context.DrawGeometry(null, new Pen(brush, IconCatalog.StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), layer.Geometry);
                }
            }
        }
    }

    public IDisposable PushClip(DesignRect r) => context.PushClip(ToRect(r));

    public IDisposable PushTranslate(double dx, double dy) => context.PushTransform(Matrix.CreateTranslation(dx, dy));

    public IDisposable PushOpacity(double opacity) => context.PushOpacity(opacity);

    /// <summary>DSG-RND-002: <c>&amp;x</c> underlines x, <c>&amp;&amp;</c> is a literal ampersand.</summary>
    internal static (string Text, int Underline) StripMnemonic(string text)
    {
        var result = new System.Text.StringBuilder(text.Length);
        var underline = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '&' && i + 1 < text.Length)
            {
                i++;
                if (text[i] != '&' && underline < 0)
                {
                    underline = result.Length;
                }
            }

            result.Append(text[i]);
        }

        return (result.ToString(), underline);
    }

    private static FormattedText Format(string text, TextStyle style, IBrush brush)
    {
        var typeface = new Typeface(new FontFamily(style.Family), style.Italic ? FontStyle.Italic : FontStyle.Normal,
            style.Bold ? FontWeight.Bold : FontWeight.Normal);
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, Math.Max(1, style.SizePx), brush);
        if (style.Underline || style.StrikeOut)
        {
            var decorations = new TextDecorationCollection();
            if (style.Underline)
            {
                decorations.AddRange(TextDecorations.Underline);
            }

            if (style.StrikeOut)
            {
                decorations.AddRange(TextDecorations.Strikethrough);
            }

            formatted.SetTextDecorations(decorations);
        }

        return formatted;
    }

    private IBrush Brush(Rgba c)
    {
        if (!_brushes.TryGetValue(c, out var brush))
        {
            _brushes[c] = brush = new ImmutableSolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        }

        return brush;
    }

    private IPen Pen(Rgba color, double thickness, LineStyle style) =>
        new ImmutablePen((IImmutableBrush)Brush(color), thickness, style switch { LineStyle.Dash => Dash, LineStyle.Dot => Dot, _ => null });

    private static Rect ToRect(DesignRect r) => new(r.Left, r.Top, r.Width, r.Height);

    private static Avalonia.Point ToPoint(DesignPoint p) => new(p.X, p.Y);
}
