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
    public const double RightGroupMinHeight = 80;

    /// <summary>
    /// UI-WIN-005: a dragged side-dock width, <paramref name="min"/> (160, or more for a wide tool bar, I-215) …
    /// <paramref name="available"/> − 320 − <paramref name="otherSide"/> − <paramref name="splitters"/> (the minimum wins
    /// when there is no room).
    /// </summary>
    public static double ClampSide(double value, double available, double otherSide, double splitters, double min = SideDockMinWidth) =>
        Math.Clamp(value, min, Math.Max(min, available - DocumentMinWidth - otherSide - splitters));

    /// <summary>
    /// I-215: the minimum width of a side dock: 160, or the widest tool bar of its windows (buttons + minimum filter
    /// width, <see cref="ToolWindowViewModel.ToolBarMinWidth"/>), so that a tool bar never overlaps; I-219: or the
    /// largest <see cref="ToolWindowViewModel.ContentMinWidth"/>.
    /// </summary>
    public static double SideMinWidth(IEnumerable<ToolWindowViewModel> windows) =>
        windows.Select(w => Math.Max(w.ToolBarMinWidth, w.ContentMinWidth)).Append(SideDockMinWidth).Max();

    /// <summary>UI-WIN-005: a dragged bottom-dock height, 80 … <paramref name="available"/> − 160 − <paramref name="splitter"/>.</summary>
    public static double ClampBottom(double value, double available, double splitter) =>
        Math.Clamp(value, BottomDockMinHeight, Math.Max(BottomDockMinHeight, available - DocumentMinHeight - splitter));

    /// <summary>§7: the upper group's share of the right dock, so that both groups keep 80 DIP; 0.5 when that cannot be.</summary>
    public static double ClampSplit(double split, double height, double splitter)
    {
        var usable = height - splitter;
        if (usable < 2 * RightGroupMinHeight)
        {
            return 0.5;
        }

        var min = RightGroupMinHeight / usable;
        return Math.Clamp(split, min, 1 - min);
    }

    /// <summary>Widths of the left and right dock (0 when not shown); <paramref name="splitter"/> per shown side dock.</summary>
    public static (double Left, double Right) Widths(double available, double left, double right, bool leftShown, bool rightShown, double splitter,
        double leftMin = SideDockMinWidth, double rightMin = SideDockMinWidth)
    {
        left = leftShown ? Math.Max(left, leftMin) : 0;
        right = rightShown ? Math.Max(right, rightMin) : 0;
        var deficit = left + right + (leftShown ? splitter : 0) + (rightShown ? splitter : 0) + DocumentMinWidth - available;
        if (deficit > 0 && rightShown)
        {
            var take = Math.Min(deficit, Math.Max(0, right - rightMin));
            right -= take;
            deficit -= take;
        }

        if (deficit > 0 && leftShown)
        {
            left -= Math.Min(deficit, Math.Max(0, left - leftMin));
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
