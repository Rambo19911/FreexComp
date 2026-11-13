namespace FreexComp.Ide.Core.Models;

/// <summary>
/// The main window's <c>window</c> section of <c>uistate.json</c> (IDE-ARCH-060): the <b>normal</b> bounds in whole DIPs
/// and whether the window is maximized.
/// </summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized);

/// <summary>The working area of one monitor, in DIPs.</summary>
public readonly record struct WorkArea(double X, double Y, double Width, double Height);

/// <summary>Where the main window opens (main-window.md UI-WIN-052, UI-WIN-053, §7).</summary>
public static class WindowPlacementRules
{
    public const int DefaultWidth = 1440;
    public const int DefaultHeight = 900;
    public const int MinWidth = 800;
    public const int MinHeight = 500;

    /// <summary>Saved bounds are kept when at least this many DIPs (both ways) of the title-bar area are on a monitor.</summary>
    public const double VisibleExtent = 100;

    /// <summary>
    /// The placement to open with: the saved one when its title-bar area (the top <see cref="VisibleExtent"/> DIPs of
    /// the bounds) overlaps a current work area by at least 100 × 100 DIP; otherwise — and on first start — the default
    /// size centerd on the primary monitor. The maximized flag is kept either way; sizes are at least the minimum.
    /// </summary>
    public static WindowPlacement Restore(WindowPlacement? saved, IReadOnlyList<WorkArea> workAreas, WorkArea primary)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        if (saved is null)
        {
            return Default(primary, maximized: false);
        }

        var placement = saved with { Width = Math.Max(MinWidth, saved.Width), Height = Math.Max(MinHeight, saved.Height) };
        return workAreas.Any(area => TitleBarVisible(placement, area)) ? placement : Default(primary, saved.Maximized);
    }

    /// <summary>UI-WIN-053: <c>min(1440, 90 %)</c> × <c>min(900, 90 %)</c> of the work area, centerd on it.</summary>
    public static WindowPlacement Default(WorkArea primary, bool maximized)
    {
        var width = (int)Math.Min(DefaultWidth, Math.Floor(primary.Width * 0.9));
        var height = (int)Math.Min(DefaultHeight, Math.Floor(primary.Height * 0.9));
        var x = (int)Math.Round(primary.X + ((primary.Width - width) / 2), MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(primary.Y + ((primary.Height - height) / 2), MidpointRounding.AwayFromZero);
        return new WindowPlacement(x, y, width, height, maximized);
    }

    private static bool TitleBarVisible(WindowPlacement placement, WorkArea area)
    {
        var width = Math.Min(placement.X + placement.Width, area.X + area.Width) - Math.Max(placement.X, area.X);
        var height = Math.Min(placement.Y + VisibleExtent, area.Y + area.Height) - Math.Max(placement.Y, area.Y);
        return width >= VisibleExtent && height >= VisibleExtent;
    }
}
