using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// A Tool Palette icon (tool-palette.md DSG-TP-009, ui/designer.md UI-DSG-122): the component's SVG (24 × 24 view box,
/// <c>path</c>, <c>rect</c> and <c>circle</c> shapes drawn in <c>currentColor</c>, UIF-953) scaled to the control, or the
/// generic component glyph when the item has no icon or its file cannot be read. <c>currentColor</c> is the icon colour
/// <c>text.primary</c> (<c>text.disabled</c> when disabled, UI-TOK-041).
/// </summary>
public sealed class PaletteIcon : Control
{
    private const double ViewBox = 24;
    private const string DisabledToken = "text.disabled";

    // The generic component glyph: an original outline (a box with an inner square), used as the fallback.
    private static readonly Shape[] Generic =
    [
        new("M5 5H19V19H5Z", false, true, 1.5),
        new("M9 9H15V15H9Z", false, true, 1.5),
    ];

    private readonly Shape[] _shapes;
    private readonly List<(Geometry Geometry, Shape Shape)> _geometry = [];

    /// <param name="svg">The icon file's bytes; null or unusable: the generic glyph (<paramref name="failed"/> is then called for bytes that did not parse).</param>
    public PaletteIcon(byte[]? svg, Action? failed = null)
    {
        var shapes = svg is null ? null : TryParse(svg);
        if (svg is not null && shapes is null)
        {
            failed?.Invoke();
        }

        IsGeneric = shapes is null;
        _shapes = shapes ?? Generic;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        foreach (var shape in _shapes)
        {
            _geometry.Add((Geometry.Parse(shape.Data), shape));
        }
    }

    static PaletteIcon()
    {
        AffectsRender<PaletteIcon>(IsEffectivelyEnabledProperty);
    }

    /// <summary>The generic glyph is shown (no icon, or it could not be read).</summary>
    public bool IsGeneric { get; }

    protected override Size MeasureOverride(Size availableSize) => new(ViewBox, ViewBox);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var brush = this.TryFindResource(IconCatalog.BrushKey(IsEffectivelyEnabled ? IconCatalog.DefaultToken : DisabledToken), ActualThemeVariant, out var value)
            ? value as IBrush
            : null;
        using (context.PushTransform(Matrix.CreateScale(Bounds.Width / ViewBox, Bounds.Height / ViewBox)))
        {
            foreach (var (geometry, shape) in _geometry)
            {
                context.DrawGeometry(shape.Filled ? brush : null,
                    shape.Stroked ? new Pen(brush, shape.StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round) : null, geometry);
            }
        }
    }

    /// <summary>The shapes of the SVG as path data (rectangles and circles converted); null when the file is not usable.</summary>
    private static Shape[]? TryParse(byte[] svg)
    {
        try
        {
            using var stream = new MemoryStream(svg);
            var root = XDocument.Load(stream).Root;
            XNamespace ns = "http://www.w3.org/2000/svg";
            if (root is null || root.Name != ns + "svg")
            {
                return null;
            }

            var shapes = new List<Shape>();
            foreach (var element in root.Elements())
            {
                var data = element.Name.LocalName switch
                {
                    "path" => (string?)element.Attribute("d"),
                    "rect" => RectData(element),
                    "circle" => CircleData(element),
                    _ => null,
                };
                if (data is null)
                {
                    return null; // an element we cannot draw: the generic glyph rather than half an icon
                }

                var fill = Inherited(element, root, "fill") ?? "black";
                var stroke = Inherited(element, root, "stroke") ?? "none";
                var width = Number(Inherited(element, root, "stroke-width") ?? "1");
                shapes.Add(new Shape(data, fill != "none", stroke != "none", width));
            }

            foreach (var shape in shapes)
            {
                _ = Geometry.Parse(shape.Data); // validates the path data
            }

            return shapes.Count == 0 ? null : [.. shapes];
        }
        catch (Exception e) when (e is System.Xml.XmlException or FormatException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    private static string? Inherited(XElement element, XElement root, string name) =>
        (string?)element.Attribute(name) ?? (string?)root.Attribute(name);

    private static double Number(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static double Attr(XElement e, string name) => e.Attribute(name) is { } a ? Number(a.Value) : 0;

    /// <summary>A rectangle (with <c>rx</c>/<c>ry</c> rounded corners) as path data.</summary>
    private static string RectData(XElement e)
    {
        var (x, y, w, h) = (Attr(e, "x"), Attr(e, "y"), Attr(e, "width"), Attr(e, "height"));
        var rx = Math.Min(e.Attribute("rx") is not null ? Attr(e, "rx") : Attr(e, "ry"), w / 2);
        var ry = Math.Min(e.Attribute("ry") is not null ? Attr(e, "ry") : rx, h / 2);
        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        if (rx <= 0 || ry <= 0)
        {
            return $"M{F(x)} {F(y)}H{F(x + w)}V{F(y + h)}H{F(x)}Z";
        }

        return $"M{F(x + rx)} {F(y)}H{F(x + w - rx)}A{F(rx)} {F(ry)} 0 0 1 {F(x + w)} {F(y + ry)}V{F(y + h - ry)}"
            + $"A{F(rx)} {F(ry)} 0 0 1 {F(x + w - rx)} {F(y + h)}H{F(x + rx)}A{F(rx)} {F(ry)} 0 0 1 {F(x)} {F(y + h - ry)}"
            + $"V{F(y + ry)}A{F(rx)} {F(ry)} 0 0 1 {F(x + rx)} {F(y)}Z";
    }

    /// <summary>A circle as two arcs.</summary>
    private static string CircleData(XElement e)
    {
        var (cx, cy, r) = (Attr(e, "cx"), Attr(e, "cy"), Attr(e, "r"));
        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        return $"M{F(cx - r)} {F(cy)}A{F(r)} {F(r)} 0 1 0 {F(cx + r)} {F(cy)}A{F(r)} {F(r)} 0 1 0 {F(cx - r)} {F(cy)}Z";
    }

    private sealed record Shape(string Data, bool Filled, bool Stroked, double StrokeWidth);
}
