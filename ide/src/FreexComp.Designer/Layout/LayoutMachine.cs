using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Layout;

/// <summary>
/// The layout procedures of ui-framework layout.md, executed on a <see cref="LayoutNode"/> tree exactly as the run-time
/// framework executes them on its controls in dump mode (96 DPI, no handles, scroll positions 0): <c>SetBounds</c>
/// (UIF-210), <c>ConstrainSize</c> (UIF-240, UIF-250), the align pass (UIF-222), anchors (UIF-231), baselines
/// (UIF-232), scroll bars (UIF-291), the class layouts of page controls and tool bars, the load sequence (UIF-300) and
/// scaling (UIF-280, UIF-283). Every rule cites its source; the run-time units are the reference implementation.
/// </summary>
internal sealed class LayoutMachine(LayoutNode root, LayoutMode mode)
{
    private const int ScrollBarSize = 17;   // MulDiv(17, 96, 96), UIF-215
    private int _scaling;

    public LayoutNode Root { get; } = root;

    public LayoutMode Mode { get; } = mode;

    /// <summary>UIF-221 participation; design mode treats every control as visible (DSG-LAY-005).</summary>
    private bool Shown(LayoutNode node) => Mode == LayoutMode.Design || node.Visible;

    // ------------------------------------------------------------------ load sequence (UIF-300 steps 1, 3, 5)

    /// <summary>
    /// Step 1 is done by the reader (<see cref="LayoutReader"/>); this runs <c>Loaded</c> children first, root last
    /// (RTL-495): baselines, the intrinsic rules applied once (U-054), the page control's own pass, then the root's
    /// top-down <c>RealignTree</c>.
    /// </summary>
    public void Load() => Loaded(Root);

    private void Loaded(LayoutNode node)
    {
        foreach (var child in node.Children)
        {
            Loaded(child);
        }

        if (node.Kind == NodeKind.PageControl)
        {
            ActivatePage(node); // TPageControl.Loaded: ChangeActivePage before inherited Loaded (UIF-771)
        }

        node.Loading = false;
        CaptureBaseline(node); // UIF-232 case 4
        if (node.Intrinsic != IntrinsicRule.None)
        {
            SetBounds(node, node.Left, node.Top, node.Width, node.Height, explicitChange: true); // AdjustHeight
        }

        if (node.Parent is null)
        {
            RealignTree(node);
        }

        if (node.Kind == NodeKind.PageControl)
        {
            Realign(node);
        }
    }

    /// <summary>UIF-771: <c>ActivePage</c> if it names a tab-visible page of this control, else the first such page.</summary>
    private static void ActivatePage(LayoutNode pageControl)
    {
        var pages = pageControl.Children.Where(c => c.Kind == NodeKind.TabSheet).ToList();
        var active = pages.FirstOrDefault(p => p.TabVisible && pageControl.ActivePageName is { } n
            && string.Equals(p.Object.Name, n, StringComparison.OrdinalIgnoreCase)) ?? pages.FirstOrDefault(p => p.TabVisible);
        foreach (var page in pages)
        {
            page.Visible = ReferenceEquals(page, active);
        }
    }

    // ------------------------------------------------------------------ explicit changes (steps, interactive resize)

    /// <summary>An explicit bounds change (property setter, UIF-212).</summary>
    public void SetBoundsExplicit(LayoutNode node, int left, int top, int width, int height) =>
        SetBounds(node, left, top, width, height, explicitChange: true);

    /// <summary><c>ClientWidth := V</c> ≡ <c>Width := Width + (V − ClientWidth)</c> (UIF-217); the root's bounds are its client size.</summary>
    public void SetClientWidth(LayoutNode node, int value) =>
        SetBoundsExplicit(node, node.Left, node.Top, node.Width + (value - ClientSize(node).Width), node.Height);

    public void SetClientHeight(LayoutNode node, int value) =>
        SetBoundsExplicit(node, node.Left, node.Top, node.Width, node.Height + (value - ClientSize(node).Height));

    /// <summary><c>TControl.SetAlign</c>: baseline re-capture, parent pass.</summary>
    public void SetAlign(LayoutNode node, AlignKind value)
    {
        if (node.Align == value)
        {
            return;
        }

        node.Align = value;
        CaptureBaseline(node);
        RequestAlign(node);
    }

    /// <summary><c>TControl.SetVisible</c>: parent pass.</summary>
    public void SetVisible(LayoutNode node, bool value)
    {
        if (node.Visible == value)
        {
            return;
        }

        node.Visible = value;
        RequestAlign(node);
    }

    /// <summary><c>TControl.SetAlignWithMargins</c>: parent pass.</summary>
    public void SetAlignWithMargins(LayoutNode node, bool value)
    {
        if (node.AlignWithMargins == value)
        {
            return;
        }

        node.AlignWithMargins = value;
        RequestAlign(node);
    }

    // ------------------------------------------------------------------ SetBounds / ConstrainSize (UIF-210, UIF-240)

    private void SetBounds(LayoutNode node, int left, int top, int width, int height, bool explicitChange)
    {
        width = Math.Max(width, 0);
        height = Math.Max(height, 0);
        (width, height) = ConstrainSize(node, width, height);
        if (left == node.Left && top == node.Top && width == node.Width && height == node.Height)
        {
            return;
        }

        var sizeChanged = width != node.Width || height != node.Height;
        (node.Left, node.Top, node.Width, node.Height) = (left, top, width, height);
        var explicitOutsideLoading = explicitChange && !node.Loading;
        if (explicitOutsideLoading)
        {
            CaptureBaseline(node);
        }

        if (sizeChanged && node.IsWinControl)
        {
            Realign(node);
        }

        if (explicitOutsideLoading)
        {
            RequestAlign(node);
        }
    }

    /// <summary>UIF-240 with the intrinsic rules of UIF-250 (inactive while loading, U-054); the form's own constraints are ignored (UIF-245).</summary>
    private static (int Width, int Height) ConstrainSize(LayoutNode node, int width, int height)
    {
        if (!node.Loading)
        {
            var font = Math.Abs(node.EffectiveFontHeight);
            switch (node.Intrinsic)
            {
                case IntrinsicRule.Edit when node.AutoSize:
                    height = font + (node.BorderSingle ? 11 : 3);
                    break;
                case IntrinsicRule.ComboBox when !node.ComboSimple:
                    height = font + 11;
                    break;
            }
        }

        if (node.Parent is null)
        {
            return (width, height);
        }

        if (node.MaxWidth > 0 && width > node.MaxWidth)
        {
            width = node.MaxWidth;
        }

        if (node.MinWidth > 0 && width < node.MinWidth)
        {
            width = node.MinWidth;
        }

        if (node.MaxHeight > 0 && height > node.MaxHeight)
        {
            height = node.MaxHeight;
        }

        if (node.MinHeight > 0 && height < node.MinHeight)
        {
            height = node.MinHeight;
        }

        return (width, height);
    }

    // ------------------------------------------------------------------ client size, align rect (UIF-215 … 220)

    /// <summary>The client size (UIF-215).</summary>
    public static (int Width, int Height) ClientSize(LayoutNode node)
    {
        if (node.Parent is null)
        {
            return (node.Width, node.Height);
        }

        switch (node.Kind)
        {
            case NodeKind.Panel when node.BorderSingle:
                return (Math.Max(0, node.Width - 2), Math.Max(0, node.Height - 2));
            case NodeKind.ScrollBox:
                var inset = 2 * ScrollBoxInset(node);
                return (Math.Max(0, node.Width - inset - (node.VertShown ? ScrollBarSize : 0)),
                    Math.Max(0, node.Height - inset - (node.HorzShown ? ScrollBarSize : 0)));
            case NodeKind.Grid when node.BorderSingle:
                return (Math.Max(0, node.Width - 4), Math.Max(0, node.Height - 4));
            default:
                return (node.Width, node.Height);
        }
    }

    /// <summary>The client origin relative to the control's top-left (UIF-215 last column).</summary>
    public static (int X, int Y) ClientOrigin(LayoutNode node) => node.Parent is null ? (0, 0) : node.Kind switch
    {
        NodeKind.Panel when node.BorderSingle => (1, 1),
        NodeKind.ScrollBox => (ScrollBoxInset(node), ScrollBoxInset(node)),
        NodeKind.Grid when node.BorderSingle => (2, 2),
        _ => (0, 0),
    };

    private static int ScrollBoxInset(LayoutNode node) => node.BorderSingle ? 2 : 0;

    /// <summary>UIF-220: client rect → <c>AdjustClientRect</c> (UIF-218) → <c>Padding</c>; (left, top, right, bottom) in client coordinates.</summary>
    public static (int Left, int Top, int Right, int Bottom) AlignRect(LayoutNode node)
    {
        var (w, h) = ClientSize(node);
        int l = 0, t = 0, r = w, b = h;
        switch (node.Kind)
        {
            case NodeKind.Panel when node.Parent is not null:
                var i = ContainerRects.PanelInset(node.BorderWidth, node.BevelWidth, node.BevelOuter, node.BevelInner);
                (l, t, r, b) = (l + i, t + i, r - i, b - i);
                break;
            case NodeKind.GroupBox when node.Parent is not null:
                const int s = ContainerRects.GroupBoxInset;
                (l, t, r, b) = (l + s, t + ContainerRects.GroupBoxCaptionHeight(node.EffectiveFontHeight), r - s, b - s);
                break;
        }

        return (l + node.PaddingLeft, t + node.PaddingTop, r - node.PaddingRight, b - node.PaddingBottom);
    }

    /// <summary>UIF-219: <c>(4, TabH + 6, Width − 4, Height − 4)</c>, extents clamped at 0.</summary>
    private static (int Left, int Top, int Width, int Height) PageRect(LayoutNode pageControl)
    {
        var tabH = pageControl.TabHeight > 0 ? pageControl.TabHeight : Math.Abs(pageControl.EffectiveFontHeight) + 9;
        int l = 4, t = tabH + 4 + 2, r = pageControl.Width - 4, b = pageControl.Height - 4;
        return (l, t, Math.Max(r, l) - l, Math.Max(b, t) - t);
    }

    // ------------------------------------------------------------------ layout pass (UIF-222 … 226)

    private void RequestAlign(LayoutNode node)
    {
        if (node.Parent is { } parent)
        {
            AlignControl(parent);
        }
    }

    public void Realign(LayoutNode node) => AlignControl(node);

    /// <summary>UIF-300 step 5: a pass on every windowed control, parents before children.</summary>
    public void RealignTree(LayoutNode node)
    {
        Realign(node);
        foreach (var child in node.Children)
        {
            if (child.IsWinControl)
            {
                RealignTree(child);
            }
        }
    }

    /// <summary>UIF-225, UIF-226: deferred while loading or scaling; ignored while the same parent's pass runs.</summary>
    private void AlignControl(LayoutNode node)
    {
        if (node.Loading || _scaling > 0 || node.Aligning)
        {
            return;
        }

        node.Aligning = true;
        try
        {
            AlignControls(node);
        }
        finally
        {
            node.Aligning = false;
        }
    }

    private void AlignControls(LayoutNode node)
    {
        if (node.Kind == NodeKind.ToolBar)
        {
            FlowToolBar(node);
            return;
        }

        if (node.Kind == NodeKind.ScrollBox)
        {
            UpdateScrollBars(node);
        }

        var r = AlignRect(node);
        for (var phase = AlignKind.Top; phase <= AlignKind.Client; phase++)
        {
            var list = new List<(int Key, LayoutNode Child)>();
            foreach (var child in node.Children)
            {
                if (child.Align == phase && Shown(child))
                {
                    list.Add((AlignKey(child, phase), child));
                }
            }

            foreach (var (_, child) in list.OrderBy(e => e.Key)) // stable: ties keep Controls[] order
            {
                Place(child, phase, ref r);
            }
        }

        foreach (var child in node.Children)
        {
            if (child.Align == AlignKind.None)
            {
                ApplyAnchors(node, child);
            }
        }

        if (node.Kind == NodeKind.PageControl)
        {
            // TPageControl.AlignControls: every page gets the page rectangle by an explicit SetBounds (UIF-772).
            var (pl, pt, pw, ph) = PageRect(node);
            foreach (var page in node.Children.Where(c => c.Kind == NodeKind.TabSheet).ToList())
            {
                SetBounds(page, pl, pt, pw, ph, explicitChange: true);
            }
        }
    }

    private static int AlignKey(LayoutNode c, AlignKind phase) => phase switch
    {
        AlignKind.Top => c.Top,
        AlignKind.Bottom => -(c.Top + c.Height),
        AlignKind.Left => c.Left,
        AlignKind.Right => -(c.Left + c.Width),
        _ => 0,
    };

    /// <summary>UIF-222 <c>Place(C, Phase, R)</c>.</summary>
    private void Place(LayoutNode c, AlignKind phase, ref (int Left, int Top, int Right, int Bottom) r)
    {
        var (ml, mt, mr, mb) = c.AlignWithMargins ? (c.MarginLeft, c.MarginTop, c.MarginRight, c.MarginBottom) : (0, 0, 0, 0);
        var aw = Math.Max(0, r.Right - r.Left - ml - mr);
        var ah = Math.Max(0, r.Bottom - r.Top - mt - mb);
        var (w, h) = phase switch
        {
            AlignKind.Top or AlignKind.Bottom => (aw, c.Height),
            AlignKind.Left or AlignKind.Right => (c.Width, ah),
            _ => (aw, ah),
        };
        (w, h) = ConstrainSize(c, w, h);
        switch (phase)
        {
            case AlignKind.Top:
                SetBounds(c, r.Left + ml, r.Top + mt, w, h, explicitChange: false);
                r.Top += h + mt + mb;
                break;
            case AlignKind.Bottom:
                SetBounds(c, r.Left + ml, r.Bottom - mb - h, w, h, explicitChange: false);
                r.Bottom -= h + mt + mb;
                break;
            case AlignKind.Left:
                SetBounds(c, r.Left + ml, r.Top + mt, w, h, explicitChange: false);
                r.Left += w + ml + mr;
                break;
            case AlignKind.Right:
                SetBounds(c, r.Right - mr - w, r.Top + mt, w, h, explicitChange: false);
                r.Right -= w + ml + mr;
                break;
            default:
                SetBounds(c, r.Left + ml, r.Top + mt, w, h, explicitChange: false);
                break;
        }
    }

    // ------------------------------------------------------------------ anchors (UIF-230 … 234)

    /// <summary>UIF-232: baseline := bounds and the parent's current client size ((0, 0) without a parent, UIF-233).</summary>
    private static void CaptureBaseline(LayoutNode node)
    {
        (node.BaseLeft, node.BaseTop, node.BaseWidth, node.BaseHeight) = (node.Left, node.Top, node.Width, node.Height);
        (node.BaseParentWidth, node.BaseParentHeight) = node.Parent is { } p ? ClientSize(p) : (0, 0);
    }

    private static void CaptureBaselines(LayoutNode node)
    {
        CaptureBaseline(node);
        foreach (var child in node.Children)
        {
            if (child.IsWinControl)
            {
                CaptureBaselines(child);
            }
            else
            {
                CaptureBaseline(child);
            }
        }
    }

    /// <summary>UIF-231.</summary>
    private void ApplyAnchors(LayoutNode parent, LayoutNode c)
    {
        var (pw, ph) = ClientSize(parent);
        var (l, w) = Axis(c.AkLeft, c.AkRight, c.BaseLeft, c.BaseWidth, c.BaseParentWidth, pw);
        var (t, h) = Axis(c.AkTop, c.AkBottom, c.BaseTop, c.BaseHeight, c.BaseParentHeight, ph);
        (w, h) = ConstrainSize(c, w, h);
        SetBounds(c, l, t, w, h, explicitChange: false);
    }

    private static (int Position, int Size) Axis(bool near, bool far, int b, int s, int baseParent, int parent)
    {
        if (near)
        {
            return far ? (b, Math.Max(0, s + parent - baseParent)) : (b, s);
        }

        if (far)
        {
            return (b + parent - baseParent, s);
        }

        return baseParent > 0 ? (MulDiv(b + s / 2, parent, baseParent) - s / 2, s) : (b, s);
    }

    // ------------------------------------------------------------------ class layouts

    /// <summary><c>controls-standard.md</c> UIF-800: tool-bar flow by explicit <c>SetBounds</c> of the visible children.</summary>
    private void FlowToolBar(LayoutNode bar)
    {
        var x = bar.Indent;
        const int y = 2;
        foreach (var c in bar.Children.ToList())
        {
            if (!Shown(c))
            {
                continue;
            }

            int w;
            if (c.Kind == NodeKind.ToolButton)
            {
                w = c.Spacer ? c.Width : bar.ButtonWidth;
                SetBounds(c, x, y, w, bar.ButtonHeight, explicitChange: true);
            }
            else
            {
                w = c.Width;
                SetBounds(c, x, y, w, c.Height, explicitChange: true);
            }

            x += w;
        }
    }

    /// <summary>UIF-290 … 292 with scroll positions 0 (UIF-293).</summary>
    private void UpdateScrollBars(LayoutNode box)
    {
        int rangeH, rangeV;
        if (box.AutoScroll)
        {
            rangeH = 0;
            rangeV = 0;
            foreach (var c in box.Children)
            {
                if (c.Align != AlignKind.None || !Shown(c))
                {
                    continue;
                }

                rangeH = Math.Max(rangeH, c.AkRight ? c.Left + c.MinWidth : c.Left + c.Width);
                rangeV = Math.Max(rangeV, c.AkBottom ? c.Top + c.MinHeight : c.Top + c.Height);
            }
        }
        else
        {
            (rangeH, rangeV) = (box.HorzRange, box.VertRange);
        }

        var inset = 2 * ScrollBoxInset(box);
        var w0 = Math.Max(0, box.Width - inset);
        var h0 = Math.Max(0, box.Height - inset);
        var hBar = box.HorzBarVisible && rangeH > w0;
        var vBar = box.VertBarVisible && rangeV > h0;
        if (hBar && !vBar)
        {
            vBar = box.VertBarVisible && rangeV > h0 - ScrollBarSize;
        }

        if (vBar && !hBar)
        {
            hBar = box.HorzBarVisible && rangeH > w0 - ScrollBarSize;
        }

        (box.HorzShown, box.VertShown) = (hBar, vBar);
    }

    // ------------------------------------------------------------------ scaling (UIF-280 … 283)

    /// <summary><c>ScaleBy(M, D)</c> on the root (the <c>scale</c> step, UIF-330): <c>ChangeScale</c>, baselines, <c>RealignTree</c>.</summary>
    public void ScaleBy(int m, int d)
    {
        if (m == d || m <= 0 || d <= 0)
        {
            return;
        }

        ChangeScale(Root, m, d);
        CaptureBaselines(Root);
        RealignTree(Root);
    }

    private void ChangeScale(LayoutNode node, int m, int d)
    {
        _scaling++;
        try
        {
            switch (node.Kind) // class overrides that run before the inherited part
            {
                case NodeKind.ScrollBox:
                    node.HorzRange = MulDiv(node.HorzRange, m, d);
                    node.VertRange = MulDiv(node.VertRange, m, d);
                    break;
                case NodeKind.Panel:
                    node.BorderWidth = MulDiv(node.BorderWidth, m, d);
                    break;
                case NodeKind.ToolBar:
                    node.ButtonHeight = MulDiv(node.ButtonHeight, m, d);
                    node.ButtonWidth = MulDiv(node.ButtonWidth, m, d);
                    node.Indent = MulDiv(node.Indent, m, d);
                    break;
            }

            if (node.IsWinControl)
            {
                (node.PaddingLeft, node.PaddingTop, node.PaddingRight, node.PaddingBottom) = (MulDiv(node.PaddingLeft, m, d),
                    MulDiv(node.PaddingTop, m, d), MulDiv(node.PaddingRight, m, d), MulDiv(node.PaddingBottom, m, d));
            }

            (node.MinWidth, node.MinHeight, node.MaxWidth, node.MaxHeight) = (MulDiv(node.MinWidth, m, d),
                MulDiv(node.MinHeight, m, d), MulDiv(node.MaxWidth, m, d), MulDiv(node.MaxHeight, m, d));
            (node.MarginLeft, node.MarginTop, node.MarginRight, node.MarginBottom) = (MulDiv(node.MarginLeft, m, d),
                MulDiv(node.MarginTop, m, d), MulDiv(node.MarginRight, m, d), MulDiv(node.MarginBottom, m, d));
            if (node.OwnFont || node.Parent is null)
            {
                node.FontHeight = MulDiv(node.FontHeight, m, d);
            }

            if (node.Parent is null)
            {
                SetBounds(node, node.Left, node.Top, MulDiv(node.Width, m, d), MulDiv(node.Height, m, d), explicitChange: false);
            }
            else
            {
                // Edges (UIF-280 step 1).
                var nl = MulDiv(node.Left, m, d);
                var nt = MulDiv(node.Top, m, d);
                SetBounds(node, nl, nt, MulDiv(node.Left + node.Width, m, d) - nl, MulDiv(node.Top + node.Height, m, d) - nt,
                    explicitChange: false);
            }

            if (node.IsWinControl)
            {
                foreach (var child in node.Children)
                {
                    ChangeScale(child, m, d);
                }
            }

            if (node.Kind == NodeKind.PageControl)
            {
                node.TabHeight = MulDiv(node.TabHeight, m, d);
            }
        }
        finally
        {
            _scaling--;
        }
    }

    /// <summary><c>MulDiv</c> of layout.md §1: 64-bit, rounding half away from zero.</summary>
    public static int MulDiv(int a, int b, int c)
    {
        var p = (long)a * b;
        return (int)(p >= 0 ? (p + c / 2) / c : (p - c / 2) / c);
    }
}
