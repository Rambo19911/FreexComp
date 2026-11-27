using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Surface;

/// <summary>
/// Tab clicks at design time (design-renderers.md DSG-RND-017, DSG-RND-023; D-093, D-148): a tab of a page control
/// activates its sheet, a tab of a tab control sets <c>TabIndex</c>.
/// </summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>A tab under the pointer: its control, its index among the visible tabs, the sheet of a page control.</summary>
    private sealed record TabHit(FormObject Control, int Index, FormObject? Sheet, Rect Tab);

    /// <summary>
    /// DSG-RND-017, DSG-RND-023: a plain left press on a tab (every position, row and style; hit test on the rectangles of
    /// DSG-RND-020). On a <c>pagecontrol</c> it selects that sheet and, when it is not the design-active one, stores
    /// <c>ActivePage</c> — removing the line when the sheet is the default (the frame definition's, else the first
    /// tab-visible one; D-128) — in one undo unit "Property". On a <c>tabcontrol</c> tab <c>i</c> other than the selected one
    /// writes <c>TabIndex = i</c> in one undo unit "Property"; the selection is left as it is. False when the press is not on
    /// a tab (the normal press handling applies).
    /// </summary>
    private bool TryActivateTab(HitResult hit, PointerInput input)
    {
        if (input.Modifiers != InputModifiers.None || TabAt(hit, input.X, input.Y) is not { } found)
        {
            return false;
        }

        ResetPress();
        if (found.Sheet is not { } sheet)
        {
            var current = new RenderContext(found.Control, 0, 0).Get("TabIndex", -1);
            if (found.Index != current && !IsReadOnly)
            {
                Commit(UndoTitles.Property, [new SetProperty(found.Control.QualifiedName, "TabIndex", new IntegerValue(found.Index, false))]);
            }

            return true;
        }

        var pageControl = found.Control;
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

    // UI-DSG-084: the tab under the pointer; null when the pointer is not over a tab.
    private TabHit? _hoveredTab;

    /// <summary>The tab of a <c>pagecontrol</c> or <c>tabcontrol</c> at a surface point when <paramref name="hit"/> is that control.</summary>
    private TabHit? TabAt(HitResult hit, double surfaceX, double surfaceY)
    {
        if (hit is not { Kind: HitKind.Control, Object: { Info.RendererId: "pagecontrol" or "tabcontrol" } control })
        {
            return null;
        }

        var bounds = Geometry.GetBounds(control);
        var (x, y) = SurfaceGeometry.Round(surfaceX - bounds.Left, surfaceY - bounds.Top);
        static bool Inside(Rect r, int x, int y) => r.Left <= x && x < r.Right && r.Top <= y && y < r.Bottom;
        var ctx = RenderContext.FromLayout(control, Layout, Palette.Renderers);
        if (x < 0 || y < 0 || x >= ctx.Width || y >= ctx.Height)
        {
            return null;
        }

        if (control.Info.RendererId == "tabcontrol")
        {
            return TabControlRenderer.Tabs(ctx, MeasureCanvas).Where(t => Inside(t.Tab, x, y))
                .Select(t => new TabHit(control, t.Index, null, t.Tab)).FirstOrDefault();
        }

        return PageControlRenderer.Tabs(ctx, MeasureCanvas).Select((t, i) => (t.Sheet, t.Tab, Index: i)).Where(t => Inside(t.Tab, x, y))
            .Select(t => new TabHit(control, t.Index, t.Sheet, t.Tab)).FirstOrDefault();
    }

    /// <summary>UI-DSG-084: the tab under a pointer at rest (no button, no drag) — the arrow cursor and the underline.</summary>
    private TabHit? TabAt(double x, double y) =>
        TabAt(Current.HitTester.HitTest(x, y, [], handleSize: _settings.HandleSize), x, y);

    /// <summary>UI-DSG-084: a pointer move without a press updates the hovered tab (redrawn when it changes).</summary>
    private void UpdateHover(PointerInput input)
    {
        var tab = TabAt(input.X, input.Y);
        if (tab?.Control != _hoveredTab?.Control || tab?.Index != _hoveredTab?.Index || tab?.Sheet != _hoveredTab?.Sheet)
        {
            _hoveredTab = tab;
            Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>UI-DSG-084: a 1 px <c>designer.handle.border</c> line along the bottom of the hovered tab.</summary>
    private void DrawHoveredTab(IDesignCanvas canvas)
    {
        if (_hoveredTab is not { } hovered || !Layout.HasBounds(hovered.Control) || (hovered.Sheet is { } s && s.Parent != hovered.Control))
        {
            return;
        }

        var bounds = Geometry.GetBounds(hovered.Control);
        var ctx = RenderContext.FromLayout(hovered.Control, Layout, Palette.Renderers);
        var tab = hovered.Sheet is { } sheet
            ? PageControlRenderer.Tabs(ctx, MeasureCanvas).Where(t => ReferenceEquals(t.Sheet, sheet)).Select(t => (Rect?)t.Tab).FirstOrDefault()
            : TabControlRenderer.Tabs(ctx, MeasureCanvas).Where(t => t.Index == hovered.Index).Select(t => (Rect?)t.Tab).FirstOrDefault();
        if (tab is not { } r || r.Left >= ctx.Width || r.Bottom > ctx.Height || r.Bottom < 1)
        {
            return;
        }

        canvas.FillRect(new Rect(bounds.Left + r.Left, bounds.Top + r.Bottom - 1, Math.Min(r.Width, ctx.Width - r.Left), 1), Palette.HandleBorder);
    }
}
