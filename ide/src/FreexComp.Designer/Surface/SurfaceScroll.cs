namespace FreexComp.Designer.Surface;

/// <summary>The visible part of the surface: the scroll offset and the view size (DIP).</summary>
public readonly record struct SurfaceViewport(double OffsetX, double OffsetY, double Width, double Height);

/// <summary>Scrolling a selection made from outside the surface into view (design-surface.md DSG-SUR-030).</summary>
public static class SurfaceScroll
{
    /// <summary>The margin kept around the primary's bounds.</summary>
    public const int Margin = 8;

    /// <summary>
    /// DSG-SUR-030: the new offset that scrolls the minimum distance to make <paramref name="bounds"/> (surface coordinates)
    /// fully visible with <see cref="Margin"/>; on an axis where it does not fit, its top-left (minus the margin) is aligned.
    /// Offsets never go below 0.
    /// </summary>
    public static (double X, double Y) EnsureVisible(SurfaceViewport view, Rect bounds) =>
        (Axis(view.OffsetX, view.Width, bounds.Left, bounds.Width), Axis(view.OffsetY, view.Height, bounds.Top, bounds.Height));

    private static double Axis(double offset, double size, int start, int length)
    {
        var low = start - Margin;
        var high = start + length + Margin;
        if (high - low > size || low < offset)
        {
            return Math.Max(0, low);
        }

        return high > offset + size ? high - size : offset;
    }
}
