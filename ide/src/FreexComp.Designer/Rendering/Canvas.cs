using System.Globalization;

namespace FreexComp.Designer.Rendering;

/// <summary>A straight (non-premultiplied) colour (design-renderers.md §1).</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    /// <summary><c>#RRGGBBAA</c>, the form used in renderer goldens.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}{A:X2}");

    /// <summary>Parses <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
    public static Rgba Parse(string hex)
    {
        var s = hex.TrimStart('#');
        byte At(int i) => byte.Parse(s.AsSpan(i, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return new Rgba(At(0), At(2), At(4), s.Length >= 8 ? At(6) : (byte)255);
    }
}

/// <summary>A point in canvas coordinates (DIP); line points are pixel centres (DSG-RND-001).</summary>
public readonly record struct Point(double X, double Y);

public enum HAlign { Left, Center, Right }

public enum VAlign { Top, Center, Bottom }

/// <summary>Stroke pattern: <c>Dash</c> = 2 on / 2 off, <c>Dot</c> = 1 on / 2 off.</summary>
public enum LineStyle { Solid, Dash, Dot }

/// <summary>A resolved font (DSG-RND-011); <see cref="SizePx"/> is the em height in DIP.</summary>
public readonly record struct TextStyle(string Family, double SizePx, bool Bold, bool Italic, bool Underline, bool StrikeOut);

/// <summary>Vertical font metrics in DIP (DSG-RND-012).</summary>
public readonly record struct TextMetrics(int Ascent, int Descent, int LineHeight);

/// <summary>The drawing surface of design renderers (design-renderers.md §1). Coordinates are DIP = form pixels.</summary>
public interface IDesignCanvas
{
    void FillRect(Rect r, Rgba color);

    /// <summary>Strokes the outline fully inside <paramref name="r"/>; thickness 1 covers its outermost pixel ring.</summary>
    void DrawRect(Rect r, Rgba color, double thickness = 1, LineStyle style = LineStyle.Solid);

    void DrawLine(double x1, double y1, double x2, double y2, Rgba color, double thickness = 1, LineStyle style = LineStyle.Solid);

    void FillEllipse(Rect r, Rgba color);

    void DrawEllipse(Rect r, Rgba color, double thickness = 1);

    void FillPolygon(IReadOnlyList<Point> points, Rgba color);

    void DrawPolyline(IReadOnlyList<Point> points, Rgba color, double thickness = 1);

    /// <summary>45° lines, 1 px, <paramref name="spacing"/> apart.</summary>
    void FillHatch(Rect r, Rgba color, double spacing);

    /// <summary>Draws text clipped to <paramref name="bounds"/>; with <paramref name="mnemonic"/> see DSG-RND-002.</summary>
    void DrawText(string text, Rect bounds, TextStyle style, Rgba color, HAlign h = HAlign.Left, VAlign v = VAlign.Center,
        bool wordWrap = false, bool ellipsis = false, bool mnemonic = false);

    Size MeasureText(string text, TextStyle style, double maxWidth = double.PositiveInfinity);

    TextMetrics GetMetrics(TextStyle style);

    /// <summary>A palette icon or a built-in glyph key (<c>builtin:component</c>).</summary>
    void DrawIcon(string iconKey, Rect bounds);

    IDisposable PushClip(Rect r);

    IDisposable PushTranslate(double dx, double dy);

    IDisposable PushOpacity(double opacity);
}

/// <summary>Helpers every renderer uses for pixel-exact lines (DSG-RND-001).</summary>
public static class DesignCanvasExtensions
{
    /// <summary>A 1 px horizontal line covering pixels <paramref name="x1"/> … <paramref name="x2"/> − 1 of row <paramref name="y"/>.</summary>
    public static void HLine(this IDesignCanvas canvas, int x1, int x2, int y, Rgba color) =>
        canvas.DrawLine(x1, y + 0.5, x2, y + 0.5, color);

    /// <summary>A 1 px vertical line covering pixels <paramref name="y1"/> … <paramref name="y2"/> − 1 of column <paramref name="x"/>.</summary>
    public static void VLine(this IDesignCanvas canvas, int x, int y1, int y2, Rgba color) =>
        canvas.DrawLine(x + 0.5, y1, x + 0.5, y2, color);
}
