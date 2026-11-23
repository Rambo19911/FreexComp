using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Tests.Rendering;

/// <summary>Golden-op cases, client rects, baselines and the bounds safety test (design-renderers.md §4, §6).</summary>
public sealed partial class RendererTests
{
    /// <summary>
    /// One render case: <paramref name="ClassName"/> <c>TForm</c> renders the root form with client size W × H; any other
    /// class renders child <c>X</c>. <paramref name="Renderer"/> also overrides the metadata id (generic, unknown).
    /// <paramref name="Framework"/> cases (§4.16, D-093) use the shipped framework metadata: <c>X</c> at (8, 8) with
    /// <paramref name="Children"/> (object blocks at column 0) is laid out and object <paramref name="Target"/> is drawn
    /// from the layout (<see cref="RenderContext.FromLayout"/>), or at the given size when one is given.
    /// </summary>
    private sealed record Case(string Renderer, string Name, string ClassName, int W, int H, string Properties = "",
        bool Framework = false, string Children = "", string Target = "X")
    {
        public string Id => $"{Renderer}-{Name}";

        public RenderContext Context(int? width = null, int? height = null)
        {
            var (w, h) = (width ?? W, height ?? H);
            if (Framework)
            {
                var form = Layout.LayoutHarness.Form(400, 300, Layout.LayoutHarness.Obj("X", ClassName, 8, 8, W, H, Properties, Children));
                var target = form.FindByName(Target)!;
                return width is null && height is null
                    ? RenderContext.FromLayout(target, Layout.LayoutHarness.Engine.Compute(form))
                    : new RenderContext(target, w, h);
            }

            if (ClassName == "TForm")
            {
                var root = RenderHarness.Parse($"object F: TForm\n  ClientWidth = {W}\n  ClientHeight = {H}\n"
                    + string.Concat(Properties.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => "  " + l.Trim() + "\n"))
                    + "end\n").Root;
                return new RenderContext(root, w, h);
            }

            var obj = RenderHarness.Control(ClassName, W, H, Properties);
            var info = obj.Info is { } known && known.RendererId != Renderer ? known with { RendererId = Renderer } : null;
            return new RenderContext(obj, w, h, info: info);
        }

        public IDesignRenderer Resolve() => RenderHarness.Registry.Resolve(Renderer);
    }

    private const string ThreeLines = "Lines = (\n'First line'\n'Second line'\n'Third line'\n)";

    private static readonly Case[] Cases =
    [
        new("form", "default", "TForm", 400, 300, "Caption = 'Form1'"),
        new("form", "color-long-caption", "TForm", 240, 120,
            "Caption = 'A caption that is much too long for the caption bar of this form'\nColor = clWindow"),
        new("form", "narrow", "TForm", 100, 20, "Caption = 'Narrow'"),
        new("label", "default", "TLabel", 34, 15, "Caption = 'Label1'"),
        new("label", "empty", "TLabel", 34, 15, "Caption = ''"),
        new("label", "opaque", "TLabel", 60, 15, "Caption = 'Opaque'\nTransparent = False\nParentColor = False\nColor = clWindow"),
        new("label", "right-center", "TLabel", 80, 30, "Caption = 'Right'\nAlignment = taRightJustify\nLayout = tlCenter"),
        new("label", "center-bottom-wrap", "TLabel", 80, 50,
            "Caption = 'Wrapped'#13#10'second'\nAlignment = taCenter\nLayout = tlBottom\nWordWrap = True"),
        new("label", "first-line-only", "TLabel", 80, 30, "Caption = 'One'#13#10'Two'"),
        new("label", "no-accel", "TLabel", 40, 15, "Caption = '&File'\nShowAccelChar = False"),
        new("label", "disabled", "TLabel", 40, 15, "Caption = 'Off'\nEnabled = False"),
        new("button", "default", "TButton", 75, 25, "Caption = '&OK'"),
        new("button", "is-default", "TButton", 75, 25, "Caption = 'Save'\nDefault = True"),
        new("button", "disabled", "TButton", 75, 25, "Caption = 'Off'\nEnabled = False"),
        new("button", "word-wrap", "TButton", 75, 40, "Caption = 'Two'#13#10'lines'\nWordWrap = True"),
        new("button", "long-text", "TButton", 40, 25, "Caption = 'A long button caption'"),
        new("edit", "default", "TEdit", 121, 23, "Text = 'Some text'"),
        new("edit", "password", "TEdit", 121, 23, "Text = 'secret'\nPasswordChar = '*'"),
        new("edit", "no-border", "TEdit", 121, 23, "Text = 'Flat'\nBorderStyle = bsNone"),
        new("edit", "right-colored", "TEdit", 121, 23, "Text = 'Right'\nAlignment = taRightJustify\nColor = clYellow"),
        new("edit", "disabled", "TEdit", 121, 23, "Text = 'Off'\nEnabled = False"),
        new("memo", "default", "TMemo", 185, 89, ThreeLines),
        new("memo", "no-wrap", "TMemo", 185, 40, ThreeLines + "\nWordWrap = False"),
        new("memo", "vertical", "TMemo", 185, 89, ThreeLines + "\nScrollBars = ssVertical"),
        new("memo", "horizontal", "TMemo", 185, 89, ThreeLines + "\nScrollBars = ssHorizontal\nWordWrap = False"),
        new("memo", "both", "TMemo", 185, 89, ThreeLines + "\nScrollBars = ssBoth"),
        new("memo", "disabled-no-border", "TMemo", 185, 89, ThreeLines + "\nEnabled = False\nBorderStyle = bsNone"),
        new("checkbox", "default", "TCheckBox", 97, 17, "Caption = '&Check'"),
        new("checkbox", "checked", "TCheckBox", 97, 17, "Caption = 'On'\nChecked = True"),
        new("checkbox", "grayed", "TCheckBox", 97, 17, "Caption = 'Grayed'\nChecked = True\nState = cbGrayed"),
        new("checkbox", "glyph-right", "TCheckBox", 97, 17, "Caption = 'Left text'\nAlignment = taLeftJustify\nChecked = True"),
        new("checkbox", "disabled", "TCheckBox", 97, 17, "Caption = 'Off'\nChecked = True\nEnabled = False"),
        new("radiobutton", "default", "TRadioButton", 113, 17, "Caption = '&Radio'"),
        new("radiobutton", "checked", "TRadioButton", 113, 17, "Caption = 'On'\nChecked = True"),
        new("radiobutton", "glyph-right", "TRadioButton", 113, 17, "Caption = 'Left text'\nAlignment = taLeftJustify"),
        new("radiobutton", "disabled", "TRadioButton", 113, 17, "Caption = 'Off'\nChecked = True\nEnabled = False"),
        new("listbox", "empty", "TListBox", 121, 97),
        new("listbox", "items", "TListBox", 121, 97, "Items = (\n'Apple'\n'Banana'\n'Cherry'\n)"),
        new("listbox", "overflow", "TListBox", 121, 40, "Items = (\n'One'\n'Two'\n'Three'\n'Four'\n)"),
        new("listbox", "disabled-no-border", "TListBox", 121, 50, "Items = (\n'One'\n)\nEnabled = False\nBorderStyle = bsNone"),
        new("combobox", "drop-down", "TComboBox", 145, 23, "Text = 'Typed'"),
        new("combobox", "drop-down-list", "TComboBox", 145, 23, "Items = (\n'Zero'\n'One'\n)\nItemIndex = 1\nStyle = csDropDownList"),
        new("combobox", "owner-draw", "TComboBox", 145, 23, "Items = (\n'Zero'\n)\nItemIndex = 5\nStyle = csOwnerDrawFixed"),
        new("combobox", "simple", "TComboBox", 145, 80, "Text = 'Typed'\nItems = (\n'Zero'\n'One'\n)\nStyle = csSimple"),
        new("combobox", "disabled", "TComboBox", 145, 23, "Text = 'Off'\nEnabled = False"),
        new("groupbox", "default", "TGroupBox", 185, 105, "Caption = '&Group'"),
        new("groupbox", "no-caption", "TGroupBox", 185, 105, "Caption = ''"),
        new("groupbox", "own-color-font", "TGroupBox", 185, 105,
            "Caption = 'Big'\nParentColor = False\nColor = clWindow\nParentFont = False\nFont.Height = -20"),
        new("groupbox", "disabled", "TGroupBox", 185, 105, "Caption = 'Off'\nEnabled = False"),
        new("panel", "default", "TPanel", 185, 41, "Caption = 'Panel1'"),
        new("panel", "single", "TPanel", 185, 41, "Caption = 'Single'\nBorderStyle = bsSingle"),
        new("panel", "bevels", "TPanel", 200, 100,
            "Caption = 'Bevels'\nBevelInner = bvLowered\nBevelWidth = 2\nBorderWidth = 3"),
        new("panel", "flat-top-left", "TPanel", 185, 41,
            "Caption = '&Top left'\nBevelOuter = bvNone\nAlignment = taLeftJustify\nVerticalAlignment = taAlignTop"),
        new("panel", "space-bottom-right", "TPanel", 185, 41,
            "Caption = 'Bottom'\nBevelOuter = bvSpace\nAlignment = taRightJustify\nVerticalAlignment = taAlignBottom"),
        new("panel", "no-caption-padding", "TPanel", 185, 41,
            "Caption = 'Hidden'\nShowCaption = False\nPadding.Left = 4\nColor = clHighlight"),
        new("image", "default", "TImage", 105, 105),
        new("image", "small", "TImage", 40, 15),
        new("nonvisual", "timer", "TTimer", 0, 0),
        new("generic", "default", "TStatusBar", 300, 19),
        new("generic", "with-caption", "TButton", 120, 60, "Caption = 'Shown below'"),
        new("unknown", "default", "TMystery", 120, 40),

        // §4.16 (D-093), with the framework metadata.
        new("scrollbar", "horizontal", "TScrollBar", 121, 17, Framework: true),
        new("scrollbar", "vertical", "TScrollBar", 17, 121, "Kind = sbVertical", Framework: true),
        new("scrollbar", "disabled", "TScrollBar", 121, 17, "Enabled = False", Framework: true),
        new("statictext", "default", "TStaticText", 65, 17, "Caption = '&Static'\nAutoSize = False", Framework: true),
        new("statictext", "sunken-right", "TStaticText", 90, 25,
            "Caption = 'Sunken'\nBorderStyle = sbsSunken\nAlignment = taRightJustify\nAutoSize = False", Framework: true),
        new("statictext", "single-small", "TStaticText", 10, 10, "Caption = 'Measured'\nBorderStyle = sbsSingle", Framework: true),
        new("shape", "rectangle", "TShape", 65, 65, Framework: true),
        new("shape", "circle", "TShape", 80, 50, "Shape = stCircle\nBrush.Color = clYellow\nPen.Color = clRed", Framework: true),
        new("shape", "square", "TShape", 80, 50, "Shape = stSquare", Framework: true),
        new("bevel", "box", "TBevel", 50, 50, Framework: true),
        new("bevel", "bottom-line", "TBevel", 120, 10, "Shape = bsBottomLine", Framework: true),
        new("splitter", "default", "TSplitter", 3, 100, "Align = alNone", Framework: true),
        new("splitter", "beveled-horizontal", "TSplitter", 120, 6, "Align = alNone\nBeveled = True", Framework: true),
        new("scrollbox", "empty", "TScrollBox", 185, 41, Framework: true),
        new("scrollbox", "both-bars", "TScrollBox", 185, 80, Framework: true,
            Children: "object Wide: TPanel\n  Left = 0\n  Top = 0\n  Width = 400\n  Height = 200\nend\n"),
        new("scrollbox", "no-border-vertical", "TScrollBox", 185, 80, "BorderStyle = bsNone", Framework: true,
            Children: "object Tall: TPanel\n  Left = 0\n  Top = 0\n  Width = 100\n  Height = 200\nend\n"),
        new("pagecontrol", "first-active", "TPageControl", 289, 193, Framework: true, Children: Sheets),
        new("pagecontrol", "second-active", "TPageControl", 289, 193, "ActivePage = S2", Framework: true, Children: Sheets),
        new("pagecontrol", "hidden-tab-fixed-width", "TPageControl", 289, 193, "TabWidth = 70\nTabHeight = 24", Framework: true,
            Children: HiddenSecondSheet),
        new("pagecontrol", "no-sheets", "TPageControl", 289, 193, Framework: true),
        new("tabsheet", "default", "TPageControl", 289, 193, Framework: true, Children: Sheets, Target: "S1"),
        new("stringgrid", "default", "TStringGrid", 320, 120, Framework: true),
        new("stringgrid", "no-fixed-small-cells", "TStringGrid", 200, 80,
            "FixedCols = 0\nFixedRows = 0\nDefaultColWidth = 30\nDefaultRowHeight = 15", Framework: true),
        new("trackbar", "default", "TTrackBar", 150, 45, "Position = 3", Framework: true),
        new("trackbar", "vertical-max", "TTrackBar", 45, 150, "Orientation = trVertical\nPosition = 10", Framework: true),
        new("progressbar", "half", "TProgressBar", 150, 17, "Position = 50", Framework: true),
        new("progressbar", "vertical", "TProgressBar", 17, 150, "Orientation = pbVertical\nPosition = 25", Framework: true),
        new("treeview", "default", "TTreeView", 121, 97, Framework: true),
        new("listview", "icons", "TListView", 250, 150, Framework: true),
        new("listview", "report", "TListView", 250, 150,
            "ViewStyle = vsReport\nColumns = <\nitem\nCaption = 'Name'\nWidth = 100\nend\nitem\nCaption = 'Size'\nAlignment = taRightJustify\nend\n>",
            Framework: true),
        new("statusbar", "panels", "TStatusBar", 300, 19,
            "Panels = <\nitem\nText = 'Ready'\nWidth = 120\nend\nitem\nText = 'Ln 1'\nend\n>", Framework: true),
        new("statusbar", "simple-no-grip", "TStatusBar", 300, 19, "SimplePanel = True\nSimpleText = 'Simple'\nSizeGrip = False", Framework: true),
        new("toolbar", "default", "TToolBar", 300, 29, Framework: true, Children: Buttons),
        new("toolbutton", "image-only", "TToolBar", 300, 29, Framework: true, Children: Buttons, Target: "B1"),
        new("toolbutton", "separator", "TToolBar", 300, 29, Framework: true, Children: Buttons, Target: "Sep"),
        new("toolbutton", "captions", "TToolBar", 300, 40, "ShowCaptions = True\nButtonHeight = 36\nButtonWidth = 50",
            Framework: true, Children: Buttons, Target: "B1"),
        new("toolbutton", "list-down", "TToolBar", 300, 29, "ShowCaptions = True\nList = True\nButtonWidth = 60",
            Framework: true, Children: DownButtons, Target: "B1"),
    ];

    private const string Sheets = "object S1: TTabSheet\n  Caption = '&First'\nend\nobject S2: TTabSheet\n  Caption = 'Second'\nend\n"
        + "object S3: TTabSheet\n  Caption = 'Third tab'\nend\n";

    private const string HiddenSecondSheet = "object S1: TTabSheet\n  Caption = '&First'\nend\nobject S2: TTabSheet\n  Caption = 'Second'\n"
        + "  TabVisible = False\nend\nobject S3: TTabSheet\n  Caption = 'Third tab'\nend\n";

    private const string Buttons = "object B1: TToolButton\n  Caption = '&Open'\nend\nobject Sep: TToolButton\n  Style = tbsSeparator\n  Width = 8\nend\n"
        + "object B2: TToolButton\n  Caption = 'Save'\nend\n";

    private const string DownButtons = "object B1: TToolButton\n  Caption = '&Open'\n  Down = True\nend\nobject B2: TToolButton\n  Caption = 'Save'\nend\n";

    public static TheoryData<string> CaseIds => [.. Cases.Select(c => c.Id)];

    [Theory]
    [MemberData(nameof(CaseIds))]
    [Trait("Phase", "5")]
    [Trait("Kind", "Golden")]
    [Trait("Req", "DSG-RND-001")]
    [Trait("Req", "DSG-RND-002")]
    [Trait("Req", "DSG-RND-013")]
    [Trait("Req", "DSG-RND-014")]
    public void MatchesGoldenOps(string caseId)
    {
        var c = Cases.Single(x => x.Id == caseId);
        var ctx = c.Context();
        var canvas = new RecordingCanvas();
        c.Resolve().Render(ctx, canvas);
        Golden.Check(TestPaths.Combine($"renderers/{c.Id}.ops.txt"), canvas.ToText());
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-RND-004")]
    public void ClientRectsMatchSpec()
    {
        (Rect Client, Rect Align) Rects(Case c)
        {
            var ctx = c.Context();
            var renderer = c.Resolve();
            Assert.Equal(ctx.ClientRect, renderer.GetClientRect(ctx));
            return (renderer.GetClientRect(ctx), ctx.AlignRect);
        }

        // §4.1 form: below the 30 px design caption, inside the 1 px border.
        Assert.Equal((new Rect(1, 31, 400, 300), new Rect(1, 31, 400, 300)), Rects(new("form", "", "TForm", 400, 300)));

        // §4.10 / layout.md E26: client = bounds; align rect (2, 17)–(183, 103); Padding insets it further.
        Assert.Equal((new Rect(0, 0, 185, 105), new Rect(2, 17, 181, 86)), Rects(new("groupbox", "", "TGroupBox", 185, 105)));
        Assert.Equal((new Rect(0, 0, 185, 105), new Rect(5, 19, 177, 82)),
            Rects(new("groupbox", "", "TGroupBox", 185, 105, "Padding.Left = 3\nPadding.Top = 2\nPadding.Right = 1\nPadding.Bottom = 2")));
        Assert.Equal(new Rect(2, 25, 181, 78), Rects(new("groupbox", "", "TGroupBox", 185, 105, "ParentFont = False\nFont.Height = -20")).Align);

        // §4.11 / layout.md E25: bevels inset only the align rect; bsSingle moves the client origin to (1, 1).
        Assert.Equal((new Rect(0, 0, 200, 100), new Rect(1, 1, 198, 98)), Rects(new("panel", "", "TPanel", 200, 100)));
        Assert.Equal((new Rect(0, 0, 200, 100), new Rect(5, 5, 190, 90)),
            Rects(new("panel", "", "TPanel", 200, 100, "BevelInner = bvLowered\nBorderWidth = 3")));
        Assert.Equal((new Rect(0, 0, 200, 100), new Rect(0, 0, 200, 100)), Rects(new("panel", "", "TPanel", 200, 100, "BevelOuter = bvNone")));
        Assert.Equal((new Rect(1, 1, 198, 98), new Rect(2, 2, 196, 96)), Rects(new("panel", "", "TPanel", 200, 100, "BorderStyle = bsSingle")));
        Assert.Equal((new Rect(0, 0, 200, 100), new Rect(3, 1, 196, 98)), Rects(new("panel", "", "TPanel", 200, 100, "Padding.Left = 2")));
        Assert.Equal((new Rect(0, 0, 4, 4), new Rect(4, 4, 0, 0)),
            Rects(new("panel", "", "TPanel", 4, 4, "BorderWidth = 3"))); // clamped, never negative

        // Non-containers and the generic box: the bounds (DSG-RND-004 default, layout.md UIF-215 "every other").
        Assert.Equal((new Rect(0, 0, 75, 25), new Rect(0, 0, 75, 25)), Rects(new("button", "", "TButton", 75, 25)));
        Assert.Equal(new Rect(0, 0, 300, 19), Rects(new("generic", "", "TStatusBar", 300, 19)).Client);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-RND-012")]
    public void BaselinesMatchSpec()
    {
        int? Baseline(Case c) => c.Resolve().GetBaseline(c.Context());

        // RecordingCanvas metrics at 12 px: Ascent 10, Descent 3, LineHeight 15 (DSG-RND-012).
        Assert.Equal(10, Baseline(new("label", "", "TLabel", 34, 15)));
        Assert.Equal((30 - 15) / 2 + 10, Baseline(new("label", "", "TLabel", 80, 30, "Layout = tlCenter")));
        Assert.Equal(30 - 3, Baseline(new("label", "", "TLabel", 80, 30, "Layout = tlBottom")));
        Assert.Equal(15, Baseline(new("label", "", "TLabel", 80, 30, "ParentFont = False\nFont.Height = -18"))); // round(18 × 0.83)
        Assert.Equal(15, Baseline(new("button", "", "TButton", 75, 25)));
        Assert.Equal(14, Baseline(new("edit", "", "TEdit", 121, 23)));
        Assert.Equal(12, Baseline(new("memo", "", "TMemo", 185, 89)));
        Assert.Equal(11, Baseline(new("checkbox", "", "TCheckBox", 97, 17)));
        Assert.Equal(11, Baseline(new("radiobutton", "", "TRadioButton", 113, 17)));
        Assert.Equal(11, Baseline(new("listbox", "", "TListBox", 121, 97)));
        Assert.Equal(14, Baseline(new("combobox", "", "TComboBox", 145, 23)));
        Assert.Equal(14, Baseline(new("combobox", "", "TComboBox", 145, 80, "Style = csSimple"))); // the edit row
        Assert.Equal(10, Baseline(new("groupbox", "", "TGroupBox", 185, 105)));

        // Panel: as label, inside the align rect (center default).
        Assert.Equal(1 + (39 - 15) / 2 + 10, Baseline(new("panel", "", "TPanel", 185, 41)));
        Assert.Equal(1 + 10, Baseline(new("panel", "", "TPanel", 185, 41, "VerticalAlignment = taAlignTop")));
        Assert.Equal(40 - 3, Baseline(new("panel", "", "TPanel", 185, 41, "VerticalAlignment = taAlignBottom")));

        // No text baseline.
        foreach (var c in new Case[] { new("form", "", "TForm", 400, 300), new("image", "", "TImage", 105, 105),
                     new("nonvisual", "", "TTimer", 32, 32), new("generic", "", "TStatusBar", 300, 19), new("unknown", "", "TMystery", 50, 20) })
        {
            Assert.Null(Baseline(c));
        }
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-RND-005")]
    public void NeverDrawsOutsideBounds()
    {
        var random = new Random(20261123);
        var sizes = new List<(int W, int H)> { (0, 0), (1, 1), (0, 25), (25, 0), (2, 2), (5, 3), (13, 13), (17, 17), (300, 4), (4, 300) };
        for (var i = 0; i < 25; i++)
        {
            sizes.Add((random.Next(0, 260), random.Next(0, 160)));
        }

        foreach (var c in Cases)
        {
            foreach (var (w, h) in sizes)
            {
                var canvas = new RecordingCanvas();
                c.Resolve().Render(c.Context(w, h), canvas);
                var (limitW, limitH) = c.Renderer switch
                {
                    "form" => (w + 2, h + 32),                                       // client size + decoration (§4.1)
                    "nonvisual" => (NonVisualRenderer.TileSize, NonVisualRenderer.TileSize), // the tile (§4.13)
                    _ => (w, h),
                };
                if (c.Renderer is not ("form" or "nonvisual") && (w == 0 || h == 0))
                {
                    Assert.Empty(canvas.Ops); // §5: nothing is drawn for a zero size
                }

                foreach (var op in canvas.Ops)
                {
                    if (op.Extent is { } e)
                    {
                        Assert.True(e.Left >= 0 && e.Top >= 0 && e.Right <= limitW && e.Bottom <= limitH,
                            $"{c.Id} at {w} x {h}: '{op.Text}' paints {e} outside (0, 0, {limitW}, {limitH})");
                    }
                }
            }
        }
    }
}
