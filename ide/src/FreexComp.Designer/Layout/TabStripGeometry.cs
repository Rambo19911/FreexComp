namespace FreexComp.Designer.Layout;

/// <summary><c>TTabPosition</c>: the strip side of a tab control.</summary>
public enum TabPosition { Top, Bottom, Left, Right }

/// <summary><c>TTabStyle</c>: tabs, buttons or flat buttons.</summary>
public enum TabStyle { Tabs, Buttons, FlatButtons }

/// <summary>
/// The tab-strip geometry of a <c>TCustomTabControl</c> descendant at 96 DPI — the C# mirror of ui-framework
/// <c>phase8/tab-control.md</c> UIF-1497 … 1501 (row count, <c>RowOf</c>, the rows on the opposite side <c>K</c>, the
/// display rectangle; DSG-LAY-070) and the tab rectangles of DSG-RND-020. Pure integer arithmetic on its inputs; the
/// tabs are the visible ones in order, given by their lengths along the strip.
/// </summary>
public sealed class TabStripGeometry
{
    /// <summary>Frame inset <c>P</c>.</summary>
    public const int FrameInset = 4;

    /// <summary>Horizontal padding <c>Px</c> on each side of a tab's text.</summary>
    public const int TextPadding = 6;

    /// <summary>Image gap <c>Ig</c> after a tab's image.</summary>
    public const int ImageGap = 6;

    /// <summary>Minimum automatic tab length <c>MinW</c>.</summary>
    public const int MinTabLength = 48;

    private readonly int[] _rowOf;

    /// <param name="width">The control's <c>Width</c>.</param>
    /// <param name="height">The control's <c>Height</c>.</param>
    /// <param name="tabHeight">The tab thickness <c>TabH</c> (<see cref="TabThickness"/>).</param>
    /// <param name="tabLengths">The length of every visible tab, in order (<see cref="TabLength"/>).</param>
    /// <param name="position"><c>TabPosition</c>.</param>
    /// <param name="style"><c>Style</c>.</param>
    /// <param name="multiLine"><c>MultiLine</c> as stored (left and right tabs are multi-line anyway).</param>
    /// <param name="scrollOpposite"><c>ScrollOpposite</c> as stored.</param>
    /// <param name="selectedTab">The selected visible tab; −1 for none.</param>
    public TabStripGeometry(int width, int height, int tabHeight, IReadOnlyList<int> tabLengths,
        TabPosition position = TabPosition.Top, TabStyle style = TabStyle.Tabs, bool multiLine = false,
        bool scrollOpposite = false, int selectedTab = -1)
    {
        ArgumentNullException.ThrowIfNull(tabLengths);
        (Width, Height, TabHeight, Position, Style) = (width, height, tabHeight, position, style);
        Lengths = [.. tabLengths];
        SelectedTab = selectedTab >= 0 && selectedTab < Lengths.Count ? selectedTab : -1;
        EffectiveMultiLine = multiLine || position is TabPosition.Left or TabPosition.Right;
        ScrollOppositeInEffect = scrollOpposite && style == TabStyle.Tabs && EffectiveMultiLine; // UIF-1492
        _rowOf = PackRows();
        RowCount = _rowOf.Length == 0 ? 0 : _rowOf[^1] + 1;
        OppositeRows = ScrollOppositeInEffect && SelectedTab >= 0 ? _rowOf[SelectedTab] : 0;
        DisplayRect = ComputeDisplayRect();
        TabRects = ComputeTabRects();
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary><c>TabH</c>: every tab's thickness across the strip.</summary>
    public int TabHeight { get; }

    public TabPosition Position { get; }

    public TabStyle Style { get; }

    /// <summary>The visible tabs' lengths along the strip.</summary>
    public IReadOnlyList<int> Lengths { get; }

    /// <summary>The selected visible tab, −1 for none.</summary>
    public int SelectedTab { get; }

    /// <summary><c>MultiLine</c> or a vertical strip (§2).</summary>
    public bool EffectiveMultiLine { get; }

    /// <summary><c>TCS_SCROLLOPPOSITE</c> is sent: <c>ScrollOpposite</c>, <c>tsTabs</c> and effective multi-line (UIF-1492).</summary>
    public bool ScrollOppositeInEffect { get; }

    /// <summary><c>R</c> of UIF-1499 (0 without visible tabs).</summary>
    public int RowCount { get; }

    /// <summary><c>RowOf(i)</c> of UIF-1499 for every visible tab.</summary>
    public IReadOnlyList<int> RowOf => _rowOf;

    /// <summary><c>K</c> of UIF-1500: rows 0 … K − 1 lie on the side opposite the strip side.</summary>
    public int OppositeRows { get; }

    /// <summary>UIF-1501, client coordinates of the control.</summary>
    public Rect DisplayRect { get; }

    /// <summary>DSG-RND-020: the item rectangle of every visible tab, in order, in client coordinates.</summary>
    public IReadOnlyList<Rect> TabRects { get; }

    /// <summary>Along spacing <c>Sa</c> (UIF-1497, as built at U-176): 0, 3, 10 for the three styles at every position.</summary>
    public int AlongSpacing => Style switch
    {
        TabStyle.Tabs => 0,
        TabStyle.Buttons => 3,
        _ => 10,
    };

    /// <summary>Row spacing <c>Sr</c>.</summary>
    public int RowSpacing => Style == TabStyle.Tabs ? 0 : 3;

    /// <summary>Row end allowance <c>E</c>.</summary>
    public int RowEndAllowance => Style == TabStyle.Tabs ? 5 : 3;

    /// <summary><c>TabH</c> (UIF-1497): <c>TabHeight</c> if &gt; 0, else <c>Abs(Font.Height) + 9</c>.</summary>
    public static int TabThickness(int tabHeight, int fontHeight) => tabHeight > 0 ? tabHeight : Math.Abs(fontHeight) + 9;

    /// <summary>
    /// UIF-1498: <paramref name="tabWidth"/> when &gt; 0, else <c>Max(MinW, Tx + 2·Px + Iw)</c>, where
    /// <paramref name="imageWidth"/> is <c>Iw</c> (<c>Images.Width + Ig</c> for a tab with an image, else 0).
    /// </summary>
    public static int TabLength(int tabWidth, int textExtent, int imageWidth) =>
        tabWidth > 0 ? tabWidth : Math.Max(MinTabLength, textExtent + 2 * TextPadding + imageWidth);

    /// <summary>
    /// UIF-1498 (U-176): the text of a caption whose extent is <c>Tx</c> — mnemonic prefixes processed left to right: an
    /// <c>&amp;</c> is dropped and the character after it kept unchanged (<c>&amp;&amp;</c> → <c>&amp;</c>,
    /// <c>&amp;&amp;&amp;</c> → <c>&amp;</c>); a trailing lone <c>&amp;</c> is dropped.
    /// </summary>
    public static string MeasuredCaption(string caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        if (!caption.Contains('&', StringComparison.Ordinal))
        {
            return caption;
        }

        var builder = new System.Text.StringBuilder(caption.Length);
        for (var i = 0; i < caption.Length; i++)
        {
            if (caption[i] == '&')
            {
                if (++i == caption.Length)
                {
                    break;
                }
            }

            builder.Append(caption[i]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// DSG-LAY-071: the headless estimate <c>Ceiling(0.55 · Abs(FontHeight) · length)</c> of a text's extent (callers pass
    /// the <see cref="MeasuredCaption"/> of a tab caption).
    /// </summary>
    public static int EstimateTextExtent(string caption, int fontHeight)
    {
        ArgumentNullException.ThrowIfNull(caption);
        return (int)Math.Ceiling(Math.Round(0.55 * Math.Abs((long)fontHeight) * caption.Length, 6));
    }

    /// <summary>The rectangle a tab is drawn in: the selected <c>tsTabs</c> tab grown by 2 on every side (DSG-RND-020).</summary>
    public Rect TabDrawRect(int index)
    {
        var r = TabRects[index];
        return index == SelectedTab && Style == TabStyle.Tabs ? new Rect(r.Left - 2, r.Top - 2, r.Width + 4, r.Height + 4) : r;
    }

    // UIF-1499: greedy packing in order.
    private int[] PackRows()
    {
        var rows = new int[Lengths.Count];
        if (!EffectiveMultiLine)
        {
            return rows;
        }

        var along = Position is TabPosition.Top or TabPosition.Bottom ? Width : Height;
        int row = 0, count = 0;
        long sum = 0;
        for (var i = 0; i < Lengths.Count; i++)
        {
            var w = Lengths[i];
            if (count > 0 && sum + w + (long)count * AlongSpacing + RowEndAllowance > along)
            {
                row++;
                count = 0;
                sum = 0;
            }

            rows[i] = row;
            sum += w;
            count++;
        }

        return rows;
    }

    private int Depth(int rows) => rows * TabHeight + Math.Max(rows - 1, 0) * RowSpacing + FrameInset;

    // UIF-1501.
    private Rect ComputeDisplayRect()
    {
        var k = OppositeRows;
        var rn = RowCount - k;
        var (l, t, r, b) = Position switch
        {
            TabPosition.Top => (FrameInset, Depth(rn), Width - FrameInset, Height - Depth(k)),
            TabPosition.Bottom => (FrameInset, Depth(k), Width - FrameInset, Height - Depth(rn)),
            TabPosition.Left => (Depth(rn), FrameInset, Width - Depth(k), Height - FrameInset),
            _ => (Depth(k), FrameInset, Width - Depth(rn), Height - FrameInset),
        };
        r = Math.Max(r, l);
        b = Math.Max(b, t);
        return new Rect(l, t, r - l, b - t);
    }

    // DSG-RND-020.
    private Rect[] ComputeTabRects()
    {
        var rects = new Rect[Lengths.Count];
        if (rects.Length == 0)
        {
            return rects;
        }

        // Across: for each row, its side (true = the strip side) and its index j from that side's edge (0 = outermost).
        var side = new bool[RowCount];
        var slot = new int[RowCount];
        foreach (var (rows, stripSide) in RowsOutwards())
        {
            // rows: innermost first on one side; slot j counts from the outermost.
            for (var n = 0; n < rows.Count; n++)
            {
                side[rows[n]] = stripSide;
                slot[rows[n]] = rows.Count - 1 - n;
            }
        }

        var isTabs = Style == TabStyle.Tabs;
        var start = isTabs || !EffectiveMultiLine ? 2 : 0;
        var along = new int[RowCount];
        Array.Fill(along, start);
        for (var i = 0; i < rects.Length; i++)
        {
            var row = _rowOf[i];
            var j = slot[row];
            var across = isTabs ? 2 + j * TabHeight : j * (TabHeight + RowSpacing);
            var a = along[row];
            along[row] += Lengths[i] + AlongSpacing;
            rects[i] = Place(a, Lengths[i], across, side[row]);
        }

        return rects;
    }

    // The rows of each side, innermost (next to the display rectangle) first.
    private IEnumerable<(List<int> Rows, bool StripSide)> RowsOutwards()
    {
        var all = Enumerable.Range(0, RowCount);
        if (Style != TabStyle.Tabs)
        {
            yield return ([.. all.Reverse()], true); // packing order, row 0 outermost
        }
        else if (ScrollOppositeInEffect)
        {
            var k = OppositeRows;
            yield return ([.. all.Skip(k)], true);              // K … R − 1, the selected row innermost
            yield return ([.. all.Take(k).Reverse()], false);   // 0 … K − 1, row 0 outermost
        }
        else if (SelectedTab >= 0)
        {
            var selectedRow = _rowOf[SelectedTab];
            yield return ([selectedRow, .. all.Reverse().Where(r => r != selectedRow)], true);
        }
        else
        {
            yield return ([.. all.Reverse()], true); // outwards from row R − 1
        }
    }

    // A tab at along offset a with length w, its row starting across from the edge of the strip side or the opposite one.
    private Rect Place(int a, int w, int across, bool stripSide)
    {
        var nearEdge = stripSide == (Position is TabPosition.Top or TabPosition.Left); // the edge at x = 0 / y = 0
        return Position is TabPosition.Top or TabPosition.Bottom
            ? new Rect(a, nearEdge ? across : Height - across - TabHeight, w, TabHeight)
            : new Rect(nearEdge ? across : Width - across - TabHeight, a, TabHeight, w);
    }
}
