using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Surface;

/// <summary>Tab-sheet activation by clicking a tab of a page control (design-renderers.md DSG-RND-017; D-093).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>
    /// DSG-RND-017: a plain left press on a tab of a <c>pagecontrol</c> selects that sheet and, when it is not the
    /// design-active one, stores <c>ActivePage</c> — removing the line when the sheet is the default (the first tab-visible
    /// one) — in one undo unit "Property". The tabs are those the renderer draws, measured with <see cref="MeasureCanvas"/>.
    /// False when the press is not on a tab (the normal press handling applies).
    /// </summary>
    private bool TryActivateTab(HitResult hit, PointerInput input)
    {
        if (hit is not { Kind: HitKind.Control, Object: { Info.RendererId: "pagecontrol" } pageControl }
            || input.Modifiers != InputModifiers.None)
        {
            return false;
        }

        var bounds = Geometry.GetBounds(pageControl);
        var (x, y) = SurfaceGeometry.Round(input.X - bounds.Left, input.Y - bounds.Top);
        var ctx = RenderContext.FromLayout(pageControl, Layout, Palette.Renderers);
        var sheet = PageControlRenderer.Tabs(ctx, MeasureCanvas)
            .Where(t => t.Tab.Left <= x && x < t.Tab.Right && t.Tab.Top <= y && y < t.Tab.Bottom && t.Tab.Left < ctx.Width)
            .Select(t => t.Sheet)
            .FirstOrDefault();
        if (sheet is null)
        {
            return false;
        }

        ResetPress();
        if (!ReferenceEquals(sheet, PageControlRenderer.ActiveSheet(pageControl)) && !IsReadOnly)
        {
            FormChange change = ReferenceEquals(sheet, PageControlRenderer.DefaultSheet(pageControl))
                ? new RemoveProperty(pageControl.Name, "ActivePage")
                : new SetProperty(pageControl.Name, "ActivePage", new IdentValue(sheet.Name));
            Commit(UndoTitles.Property, [change], select: sheet);
            return true;
        }

        Selection.Select(sheet);
        return true;
    }
}
