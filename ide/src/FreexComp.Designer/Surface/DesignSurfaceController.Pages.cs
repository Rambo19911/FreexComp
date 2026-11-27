using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Surface;

/// <summary>Tab-sheet activation by clicking a tab of a page control (design-renderers.md DSG-RND-017; D-093).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>
    /// DSG-RND-017: a plain left press on a tab of a <c>pagecontrol</c> selects that sheet and, when it is not the
    /// design-active one, stores <c>ActivePage</c> — removing the line when the sheet is the default (the frame
    /// definition's, else the first tab-visible one; D-128) — in one undo unit "Property". The tabs are those the renderer
    /// draws, measured with <see cref="MeasureCanvas"/>.
    /// False when the press is not on a tab (the normal press handling applies).
    /// </summary>
    private bool TryActivateTab(HitResult hit, PointerInput input)
    {
        if (input.Modifiers != InputModifiers.None || TabAt(hit, input.X, input.Y) is not { } found)
        {
            return false;
        }

        var (pageControl, sheet) = found;

        ResetPress();
        if (!ReferenceEquals(sheet, PageControlRenderer.ActiveSheet(pageControl)) && !IsReadOnly)
        {
            FormChange change = ReferenceEquals(sheet, PageControlRenderer.DefaultSheet(pageControl))
                ? new RemoveProperty(pageControl.QualifiedName, "ActivePage")
                : new SetProperty(pageControl.QualifiedName, "ActivePage", new IdentValue(sheet.Name));
            Commit(UndoTitles.Property, [change], select: sheet);
            return true;
        }

        Selection.Select(sheet);
        return true;
    }

    /// <summary>
    /// DSG-LAY-071: the text extent of a raw tab caption (with its <c>&amp;</c> characters, as the native control measures
    /// it) for the automatic tab lengths of the layout, measured with <see cref="MeasureCanvas"/> in the tab control's font.
    /// </summary>
    private int MeasureTabCaption(FormObject tabControl, string caption) =>
        MeasureCanvas.MeasureText(caption, new RenderContext(tabControl, 0, 0).Font).Width;

    // UI-DSG-084: the tab under the pointer (its page control and sheet); null when the pointer is not over a tab.
    private (FormObject PageControl, FormObject Sheet)? _hoveredTab;

    /// <summary>The tab of a <c>pagecontrol</c> at a surface point when <paramref name="hit"/> is that page control.</summary>
    private (FormObject PageControl, FormObject Sheet)? TabAt(HitResult hit, double surfaceX, double surfaceY)
    {
        if (hit is not { Kind: HitKind.Control, Object: { Info.RendererId: "pagecontrol" } pageControl })
        {
            return null;
        }

        var bounds = Geometry.GetBounds(pageControl);
        var (x, y) = SurfaceGeometry.Round(surfaceX - bounds.Left, surfaceY - bounds.Top);
        var ctx = RenderContext.FromLayout(pageControl, Layout, Palette.Renderers);
        var sheet = PageControlRenderer.Tabs(ctx, MeasureCanvas)
            .Where(t => t.Tab.Left <= x && x < t.Tab.Right && t.Tab.Top <= y && y < t.Tab.Bottom && t.Tab.Left < ctx.Width)
            .Select(t => t.Sheet)
            .FirstOrDefault();
        return sheet is null ? null : (pageControl, sheet);
    }

    /// <summary>UI-DSG-084: the tab under a pointer at rest (no button, no drag) — the arrow cursor and the underline.</summary>
    private (FormObject PageControl, FormObject Sheet)? TabAt(double x, double y) =>
        TabAt(Current.HitTester.HitTest(x, y, [], handleSize: _settings.HandleSize), x, y);

    /// <summary>UI-DSG-084: a pointer move without a press updates the hovered tab (redrawn when it changes).</summary>
    private void UpdateHover(PointerInput input)
    {
        var tab = TabAt(input.X, input.Y);
        if (tab?.Sheet != _hoveredTab?.Sheet)
        {
            _hoveredTab = tab;
            Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>UI-DSG-084: a 1 px <c>designer.handle.border</c> line along the bottom of the hovered tab.</summary>
    private void DrawHoveredTab(IDesignCanvas canvas)
    {
        if (_hoveredTab is not { } hovered || !Layout.HasBounds(hovered.PageControl) || hovered.Sheet.Parent != hovered.PageControl)
        {
            return;
        }

        var bounds = Geometry.GetBounds(hovered.PageControl);
        var ctx = RenderContext.FromLayout(hovered.PageControl, Layout, Palette.Renderers);
        foreach (var (sheet, tab) in PageControlRenderer.Tabs(ctx, MeasureCanvas))
        {
            if (ReferenceEquals(sheet, hovered.Sheet) && tab.Left < ctx.Width)
            {
                canvas.FillRect(new Rect(bounds.Left + tab.Left, bounds.Top + tab.Bottom - 1, Math.Min(tab.Width, ctx.Width - tab.Left), 1), Palette.HandleBorder);
            }
        }
    }
}
