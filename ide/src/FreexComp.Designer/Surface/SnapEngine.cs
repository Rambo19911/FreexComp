using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

public enum SnapAxis { X, Y }

/// <summary>The guideline kinds of design-surface.md DSG-SUR-072 (center lines are edge guidelines of priority 2).</summary>
public enum GuidelineKind { Baseline, Edge, Center, Margin }

/// <summary>A guideline the snapping chose (DSG-SUR-074; drawn per UI-DSG-050 … 054).</summary>
/// <param name="Axis">X: a vertical line at <paramref name="Coordinate"/>; Y: a horizontal one.</param>
/// <param name="Coordinate">The reference coordinate (where the snapped edge, center or baseline now is) in the client coordinates of <paramref name="Parent"/>.</param>
/// <param name="Reference">The sibling snapped to; null for the parent's client rectangle.</param>
public sealed record Guideline(SnapAxis Axis, GuidelineKind Kind, int Coordinate, FormObject Parent, FormObject? Reference)
{
    /// <summary>
    /// DSG-SUR-204: the distance that produced a <see cref="GuidelineKind.Margin"/> guideline (the setting, the sum of two
    /// margins, or a parent padding plus a margin); 0 for the other kinds. It describes the guideline and is not part of
    /// its identity: two guidelines at the same place are equal.
    /// </summary>
    public int Spacing { get; init; }

    public bool Equals(Guideline? other) => other is not null && Axis == other.Axis && Kind == other.Kind
        && Coordinate == other.Coordinate && Equals(Parent, other.Parent) && Equals(Reference, other.Reference);

    public override int GetHashCode() => HashCode.Combine(Axis, Kind, Coordinate, Parent, Reference);
}

/// <summary>
/// DSG-SUR-204: the effective <c>Margins</c> of a control in design units and, per side, whether the value is declared
/// (differs from the metadata default, so it is stored in the form). <see cref="Default"/>: 3 on every side, nothing
/// declared — also what a control without a <c>Margins</c> property has.
/// </summary>
public readonly record struct SnapMargins(int Left, int Top, int Right, int Bottom,
    bool LeftDeclared = false, bool TopDeclared = false, bool RightDeclared = false, bool BottomDeclared = false)
{
    public static readonly SnapMargins Default = new(3, 3, 3, 3);
}

/// <summary>DSG-SUR-204: the effective <c>Padding</c> of a parent in design units; all 0 for a parent without the property.</summary>
public readonly record struct SnapPadding(int Left, int Top, int Right, int Bottom);

/// <summary>The snapping settings of one drag event (design-surface.md §6); <see cref="None"/> while Alt is held (DSG-SUR-070).</summary>
public sealed record SnapOptions(bool SnapToGrid, int GridSizeX, int GridSizeY, bool UseGuidelines, int Distance, int Margin)
{
    public static readonly SnapOptions None = new(false, 8, 8, false, 0, 0);

    /// <summary>The live settings; <see cref="None"/> with <paramref name="alt"/>.</summary>
    public static SnapOptions From(IDesignerSettings settings, bool alt) => alt ? None
        : new(settings.SnapToGrid, Math.Max(1, settings.GridSizeX), Math.Max(1, settings.GridSizeY), settings.UseGuidelines,
            settings.GuidelineSnapDistance, settings.GuidelineMargin);
}

/// <summary>The adjustment snapping adds to a drag, per axis, and the guidelines it chose (X first).</summary>
public readonly record struct SnapResult(int Dx, int Dy, IReadOnlyList<Guideline> Guidelines);

/// <summary>A guideline reference: a sibling's bounds and absolute text baseline (null: no text) in parent client coordinates.</summary>
public readonly record struct SnapReference(FormObject Object, Rect Bounds, int? Baseline)
{
    /// <summary>The sibling's margins (DSG-SUR-200).</summary>
    public SnapMargins Margins { get; init; } = SnapMargins.Default;
}

/// <summary>
/// Where a drag snaps (DSG-SUR-072): the (target) parent, its client size and the reference siblings in creation order
/// (visual, not dragged, not preserved).
/// </summary>
public sealed record SnapTarget(FormObject Parent, int ClientWidth, int ClientHeight, IReadOnlyList<SnapReference> Siblings)
{
    /// <summary>The parent's padding (DSG-SUR-201).</summary>
    public SnapPadding Padding { get; init; }

    /// <summary>The margins of the anchor control: the moved or resized one (DSG-SUR-200, 201, 205).</summary>
    public SnapMargins Margins { get; init; } = SnapMargins.Default;
}

/// <summary>The edges a resize drags (DSG-SUR-075, DSG-SUR-081).</summary>
[Flags]
public enum SnapEdges { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

/// <summary>
/// Snapping of move, resize and placement drags (design-surface.md §6): per axis, a guideline within reach wins, else the
/// grid, else the raw value (DSG-SUR-071 … 079). Pure: all positions are in the client coordinates of the (target) parent.
/// The distance of a margin guideline is computed per candidate from the margins and the padding the inputs carry
/// (DSG-SUR-200 … 203); with nothing declared it is <see cref="SnapOptions.Margin"/> as before.
/// </summary>
public static class SnapEngine
{
    /// <summary>DSG-SUR-073: siblings farther than this on the other axis are not references.</summary>
    public const int ReferenceReach = 64;

    private const int BaselinePriority = 0;
    private const int EdgePriority = 1;
    private const int CenterPriority = 2;
    private const int MarginPriority = 3;

    /// <summary>DSG-SUR-076: the nearest multiple of <paramref name="grid"/>, halves up (<c>floor((v + g/2) / g) × g</c>).</summary>
    public static int ToGrid(int value, int grid) => (int)Math.Floor((value + grid / 2.0) / grid) * grid;

    /// <summary>
    /// A move: <paramref name="candidate"/> is the anchor control's bounds after the raw delta; both edges and the center
    /// of an axis are candidates, on Y also the text baseline (<paramref name="baselineOffset"/> from the control's top,
    /// null: none). <paramref name="snapX"/> / <paramref name="snapY"/> false leave that axis alone.
    /// </summary>
    public static SnapResult SnapMove(Rect candidate, int? baselineOffset, SnapTarget target, SnapOptions options, bool snapX = true,
        bool snapY = true)
    {
        var r = candidate;
        var gx = snapX ? Choose(Candidates(r, target, options, SnapAxis.X, SnapEdges.Left | SnapEdges.Right, true, null)) : null;
        var gy = snapY ? Choose(Candidates(r, target, options, SnapAxis.Y, SnapEdges.Top | SnapEdges.Bottom, true, baselineOffset)) : null;
        var dx = !snapX ? 0 : gx is { } x ? x.D : options.SnapToGrid ? ToGrid(r.Left, options.GridSizeX) - r.Left : 0;
        var dy = !snapY ? 0 : gy is { } y ? y.D : options.SnapToGrid ? ToGrid(r.Top, options.GridSizeY) - r.Top : 0;
        return new SnapResult(dx, dy, Lines(gx, gy));
    }

    /// <summary>
    /// A resize: <paramref name="candidate"/> is the bounds from the raw handle position; only the <paramref name="dragged"/>
    /// edges are candidates (no center lines, no baseline, DSG-SUR-075); the grid rounds the dragged edge (DSG-SUR-076).
    /// The result is what to add to the dragged edge of each axis (0 for an axis without a dragged edge).
    /// </summary>
    public static SnapResult SnapResize(Rect candidate, SnapEdges dragged, SnapTarget target, SnapOptions options)
    {
        var r = candidate;
        var xEdges = dragged & (SnapEdges.Left | SnapEdges.Right);
        var yEdges = dragged & (SnapEdges.Top | SnapEdges.Bottom);
        var gx = xEdges == 0 ? null : Choose(Candidates(r, target, options, SnapAxis.X, xEdges, false, null));
        var gy = yEdges == 0 ? null : Choose(Candidates(r, target, options, SnapAxis.Y, yEdges, false, null));
        var ex = xEdges.HasFlag(SnapEdges.Left) ? r.Left : r.Right;
        var ey = yEdges.HasFlag(SnapEdges.Top) ? r.Top : r.Bottom;
        var dx = xEdges == 0 ? 0 : gx is { } x ? x.D : options.SnapToGrid ? ToGrid(ex, options.GridSizeX) - ex : 0;
        var dy = yEdges == 0 ? 0 : gy is { } y ? y.D : options.SnapToGrid ? ToGrid(ey, options.GridSizeY) - ey : 0;
        return new SnapResult(dx, dy, Lines(gx, gy));
    }

    /// <summary>DSG-SUR-079: a tray item's position snaps to the grid only.</summary>
    public static (int X, int Y) SnapTray((int X, int Y) position, SnapOptions options) => options.SnapToGrid
        ? (ToGrid(position.X, options.GridSizeX), ToGrid(position.Y, options.GridSizeY))
        : position;

    private readonly record struct Candidate(int D, int Priority, int OtherAxisGap, int Order, Guideline Line);

    private static IReadOnlyList<Guideline> Lines(Candidate? x, Candidate? y)
    {
        var lines = new List<Guideline>(2);
        if (x is { } cx)
        {
            lines.Add(cx.Line);
        }

        if (y is { } cy)
        {
            lines.Add(cy.Line);
        }

        return lines;
    }

    /// <summary>DSG-SUR-074: the smallest |d| within reach; ties: priority, then the gap on the other axis, then creation order.</summary>
    private static Candidate? Choose(List<Candidate> candidates) => candidates.Count == 0 ? null : candidates
        .OrderBy(c => Math.Abs(c.D)).ThenBy(c => c.Priority).ThenBy(c => c.OtherAxisGap).ThenBy(c => c.Order)
        .First();

    /// <summary>The guideline candidates of one axis within reach (DSG-SUR-072, DSG-SUR-073), written for X, mirrored for Y.</summary>
    private static List<Candidate> Candidates(Rect r, SnapTarget target, SnapOptions options, SnapAxis axis, SnapEdges edges,
        bool center, int? baselineOffset)
    {
        var list = new List<Candidate>();
        if (!options.UseGuidelines)
        {
            return list;
        }

        var x = axis == SnapAxis.X;
        var (lo, hi) = x ? (r.Left, r.Right) : (r.Top, r.Bottom);   // R on this axis
        var (oLo, oHi) = x ? (r.Top, r.Bottom) : (r.Left, r.Right); // R on the other axis
        var low = edges.HasFlag(x ? SnapEdges.Left : SnapEdges.Top);
        var high = edges.HasFlag(x ? SnapEdges.Right : SnapEdges.Bottom);
        var mid = (lo + hi) / 2;
        var g = options.Margin;
        var (rLow, rHigh) = Sides(target.Margins, x); // R's margins on its low (left / top) and high (right / bottom) side
        var (pLow, pHigh) = x ? (target.Padding.Left, target.Padding.Right) : (target.Padding.Top, target.Padding.Bottom);

        void Add(int moving, int reference, GuidelineKind kind, int priority, int gap, int order, FormObject? obj, int spacing = 0)
        {
            if (Math.Abs(reference - moving) <= options.Distance)
            {
                list.Add(new Candidate(reference - moving, priority, gap, order,
                    new Guideline(axis, kind, reference, target.Parent, obj) { Spacing = spacing }));
            }
        }

        // DSG-SUR-200: the two facing margins when one of them is declared, else the setting.
        int Between((int Value, bool Declared) a, (int Value, bool Declared) b) => a.Declared || b.Declared ? a.Value + b.Value : g;

        // DSG-SUR-201: the parent's padding plus R's declared margin when either exists, else the setting.
        int Inside(int padding, (int Value, bool Declared) a) => padding != 0 || a.Declared ? padding + (a.Declared ? a.Value : 0) : g;

        for (var i = 0; i < target.Siblings.Count; i++)
        {
            var s = target.Siblings[i];
            var b = s.Bounds;
            var (sLo, sHi) = x ? (b.Left, b.Right) : (b.Top, b.Bottom);
            var (sOLo, sOHi) = x ? (b.Top, b.Bottom) : (b.Left, b.Right);
            if (!(sOLo - ReferenceReach < oHi && sOHi + ReferenceReach > oLo))
            {
                continue; // DSG-SUR-073: too far away on the other axis
            }

            var gap = Math.Max(0, Math.Max(sOLo - oHi, oLo - sOHi));
            var (sLow, sHigh) = Sides(s.Margins, x);
            if (low)
            {
                var m = Between(rLow, sHigh);
                Add(lo, sLo, GuidelineKind.Edge, EdgePriority, gap, i, s.Object);
                Add(lo, sHi, GuidelineKind.Edge, EdgePriority, gap, i, s.Object);
                Add(lo, sHi + m, GuidelineKind.Margin, MarginPriority, gap, i, s.Object, m);
            }

            if (high)
            {
                var m = Between(rHigh, sLow);
                Add(hi, sLo, GuidelineKind.Edge, EdgePriority, gap, i, s.Object);
                Add(hi, sHi, GuidelineKind.Edge, EdgePriority, gap, i, s.Object);
                Add(hi, sLo - m, GuidelineKind.Margin, MarginPriority, gap, i, s.Object, m);
            }

            if (center)
            {
                Add(mid, (sLo + sHi) / 2, GuidelineKind.Center, CenterPriority, gap, i, s.Object);
            }

            if (baselineOffset is { } offset && s.Baseline is { } baseline)
            {
                Add(r.Top + offset, baseline, GuidelineKind.Baseline, BaselinePriority, gap, i, s.Object);
            }
        }

        // The parent's client rectangle (0, 0, clientW, clientH) is always a reference; on a full tie it comes after the siblings.
        var size = x ? target.ClientWidth : target.ClientHeight;
        if (low)
        {
            var m = Inside(pLow, rLow);
            Add(lo, m, GuidelineKind.Margin, MarginPriority, 0, int.MaxValue, null, m);
        }

        if (high)
        {
            var m = Inside(pHigh, rHigh);
            Add(hi, size - m, GuidelineKind.Margin, MarginPriority, 0, int.MaxValue, null, m);
        }

        if (center)
        {
            Add(mid, size / 2, GuidelineKind.Center, CenterPriority, 0, int.MaxValue, null);
        }

        return list;
    }

    /// <summary>The margins of the low (left / top) and the high (right / bottom) side of an axis.</summary>
    private static ((int Value, bool Declared) Low, (int Value, bool Declared) High) Sides(SnapMargins m, bool x) => x
        ? ((m.Left, m.LeftDeclared), (m.Right, m.RightDeclared))
        : ((m.Top, m.TopDeclared), (m.Bottom, m.BottomDeclared));
}
