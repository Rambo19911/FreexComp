namespace FreexComp.Designer.Rendering;

/// <summary>A rectangle of whole device pixels; right and bottom are exclusive.</summary>
public readonly record struct PixelBox(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>
/// Device-pixel snapping of the design canvas (DSG-RND-027, 028) at render scale <paramref name="Scale"/>, for geometry
/// drawn under a translation of (<paramref name="OffsetX"/>, <paramref name="OffsetY"/>) DIP: snapping is absolute, so the
/// same surface position gives the same device pixels whatever the translation in force. Half a device pixel rounds up.
/// At a whole scale, DSG-RND-001 geometry (integer rect edges, lines at pixel centres) comes back unchanged.
/// </summary>
public readonly record struct DeviceSnap(double Scale, double OffsetX = 0, double OffsetY = 0)
{
    private static int Round(double value) => (int)Math.Floor(value + 0.5);

    /// <summary>The device pixels of a line thickness or of a size that stays physical: <c>max(1, round(dip · s))</c>.</summary>
    public int Pixels(double dip) => Math.Max(1, Round(dip * Scale));

    /// <summary>
    /// The device pixels of a rect: every edge at <c>round(edge · s)</c>, so adjacent rects share one device edge
    /// (DSG-RND-027). With <paramref name="fixedSize"/> the top-left corner is snapped like that and the size is
    /// <see cref="Pixels"/> of the width and height, whatever the position (DSG-RND-028).
    /// </summary>
    public PixelBox Box(double left, double top, double width, double height, bool fixedSize = false)
    {
        var (l, t) = (Round((OffsetX + left) * Scale), Round((OffsetY + top) * Scale));
        return fixedSize
            ? new PixelBox(l, t, l + (width > 0 ? Pixels(width) : 0), t + (height > 0 ? Pixels(height) : 0))
            : new PixelBox(l, t, Round((OffsetX + left + width) * Scale), Round((OffsetY + top + height) * Scale));
    }

    /// <summary>
    /// A horizontal line with its centre at <paramref name="centerY"/>: <see cref="Pixels"/> device rows from the row its
    /// upper edge rounds to (a 1 DIP line on pixel row y, centre y + 0.5: from <c>round(y · s)</c>).
    /// </summary>
    public (int First, int Count) Row(double centerY, double thickness) => (Round((OffsetY + centerY - thickness / 2) * Scale), Pixels(thickness));

    /// <summary>A vertical line with its centre at <paramref name="centerX"/>: the device columns, as <see cref="Row"/>.</summary>
    public (int First, int Count) Column(double centerX, double thickness) => (Round((OffsetX + centerX - thickness / 2) * Scale), Pixels(thickness));

    /// <summary>The filled rect in DIP under the translation, on the device pixels of <see cref="Box"/>.</summary>
    public (double Left, double Top, double Width, double Height) Fill(Rect r, bool fixedSize = false)
    {
        var box = Box(r.Left, r.Top, r.Width, r.Height, fixedSize);
        return (X(box.Left), Y(box.Top), box.Width / Scale, box.Height / Scale);
    }

    /// <summary>
    /// The outline of a rect drawn inside it: the outer rect of <see cref="Fill"/> and the pen width
    /// (<see cref="Pixels"/> of <paramref name="thickness"/>), in DIP.
    /// </summary>
    public (double Left, double Top, double Width, double Height, double Pen) Outline(Rect r, double thickness, bool fixedSize = false)
    {
        var (left, top, width, height) = Fill(r, fixedSize);
        return (left, top, width, height, Pixels(thickness) / Scale);
    }

    /// <summary>
    /// A line in DIP under the translation. A horizontal or vertical one (DSG-RND-001: on a pixel row or column) lies on
    /// the device pixels of <see cref="Row"/> / <see cref="Column"/> with its ends on snapped edges; any other line is
    /// returned unchanged.
    /// </summary>
    public (double X1, double Y1, double X2, double Y2, double Pen) Line(double x1, double y1, double x2, double y2, double thickness)
    {
        if (y1 == y2 && x1 != x2)
        {
            var (first, count) = Row(y1, thickness);
            var y = Y(first + count / 2.0);
            return (X(Round((OffsetX + x1) * Scale)), y, X(Round((OffsetX + x2) * Scale)), y, count / Scale);
        }

        if (x1 == x2 && y1 != y2)
        {
            var (first, count) = Column(x1, thickness);
            var x = X(first + count / 2.0);
            return (x, Y(Round((OffsetY + y1) * Scale)), x, Y(Round((OffsetY + y2) * Scale)), count / Scale);
        }

        return (x1, y1, x2, y2, thickness);
    }

    private double X(double device) => device / Scale - OffsetX;

    private double Y(double device) => device / Scale - OffsetY;
}
