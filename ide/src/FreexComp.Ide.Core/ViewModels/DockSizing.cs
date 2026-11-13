namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Effective dock sizes for the space available (main-window.md §7, UI-WIN-060): when the window shrinks, space is
/// taken first from the document area down to its minimum, then from the right dock, then the left dock, then the
/// bottom dock, each down to its minimum. Stored sizes are not changed; the result is only what is laid out.
/// </summary>
public static class DockSizing
{
    public const double DocumentMinWidth = 320;
    public const double DocumentMinHeight = 160;
    public const double SideDockMinWidth = 160;
    public const double BottomDockMinHeight = 80;

    /// <summary>Widths of the left and right dock (0 when not shown); <paramref name="splitter"/> per shown side dock.</summary>
    public static (double Left, double Right) Widths(double available, double left, double right, bool leftShown, bool rightShown, double splitter)
    {
        left = leftShown ? left : 0;
        right = rightShown ? right : 0;
        var deficit = left + right + (leftShown ? splitter : 0) + (rightShown ? splitter : 0) + DocumentMinWidth - available;
        if (deficit > 0 && rightShown)
        {
            var take = Math.Min(deficit, Math.Max(0, right - SideDockMinWidth));
            right -= take;
            deficit -= take;
        }

        if (deficit > 0 && leftShown)
        {
            left -= Math.Min(deficit, Math.Max(0, left - SideDockMinWidth));
        }

        return (left, right);
    }

    /// <summary>Height of the bottom dock (0 when not shown).</summary>
    public static double BottomHeight(double available, double bottom, bool shown, double splitter)
    {
        if (!shown)
        {
            return 0;
        }

        var deficit = bottom + splitter + DocumentMinHeight - available;
        return deficit > 0 ? bottom - Math.Min(deficit, Math.Max(0, bottom - BottomDockMinHeight)) : bottom;
    }
}
