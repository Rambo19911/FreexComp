using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Tab-sheet activation by clicking a tab (design-renderers.md DSG-RND-017; D-093), with the framework metadata.</summary>
public sealed class TabSheetTests
{
    private const string Text = """
        object Form1: TForm
          ClientWidth = 400
          ClientHeight = 300
          object Book: TPageControl
            Left = 8
            Top = 8
            Width = 289
            Height = 193
            object S1: TTabSheet
              Caption = '&First'
            end
            object S2: TTabSheet
              Caption = 'Second'
              object B: TButton
                Left = 10
                Top = 10
                Width = 75
                Height = 25
                Caption = 'B'
              end
            end
            object S3: TTabSheet
              Caption = 'Third'
            end
          end
        end

        """;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-RND-017")]
    public void ClickingATabActivatesTheSheetInOneUndoUnit()
    {
        using var h = DesignerHarness.FromText(Text, catalog: Layout.LayoutHarness.Catalog);
        (int X, int Y) Tab(string sheet)
        {
            var ctx = RenderContext.FromLayout(h.Object("Book"), h.Layout);
            var tab = PageControlRenderer.Tabs(ctx, h.Controller.MeasureCanvas).Single(t => t.Sheet.Name == sheet).Tab;
            return (tab.Left + tab.Width / 2, tab.Top + tab.Height / 2);
        }

        Assert.True(h.Layout.GetVisible(h.Object("S1")));
        Assert.False(h.Layout.GetVisible(h.Object("S2")));

        // A click on the second tab selects S2 and stores ActivePage: one unit "Property".
        h.Click("Book", Tab("S2"));
        Assert.Equal(["S2"], h.Selected);
        Assert.Contains("    ActivePage = S2\n", h.Text, StringComparison.Ordinal);
        Assert.Equal((1, UndoTitles.Property), (h.Undo.UndoCount, h.Undo.UndoTitleKey));
        Assert.True(h.Layout.GetVisible(h.Object("S2")));
        Assert.False(h.Layout.GetVisible(h.Object("S1")));

        // The active sheet's children are hit-testable now.
        h.Click("B", (5, 5));
        Assert.Equal(["B"], h.Selected);

        // Clicking the active tab only selects the sheet; no unit.
        h.Click("Book", Tab("S2"));
        Assert.Equal(["S2"], h.Selected);
        Assert.Equal(1, h.Undo.UndoCount);

        // The first sheet is the default: its tab removes the ActivePage line.
        h.Click("Book", Tab("S1"));
        Assert.Equal(["S1"], h.Selected);
        Assert.DoesNotContain("ActivePage", h.Text, StringComparison.Ordinal);
        Assert.Equal(2, h.Undo.UndoCount);

        // Undo restores the line and the selection of the unit; redo removes it again.
        h.ExecuteUndo();
        Assert.Contains("    ActivePage = S2\n", h.Text, StringComparison.Ordinal);
        Assert.Equal(["S2"], h.Selected);
        Assert.True(h.Layout.GetVisible(h.Object("S2")));
        h.ExecuteRedo();
        Assert.DoesNotContain("ActivePage", h.Text, StringComparison.Ordinal);
        Assert.Equal(["S1"], h.Selected);

        // The strip beyond the last tab is the page control itself.
        h.Click("Book", (280, 10));
        Assert.Equal(["Book"], h.Selected);
        Assert.Equal(2, h.Undo.UndoCount);
    }

    /// <summary>
    /// DSG-RND-023 (D-148): the tab click works at every <c>TabPosition</c> and <c>Style</c> with two rows (columns): the hit
    /// test uses the laid-out tab rectangles of DSG-RND-020, so a tab of the outer row selects its sheet, and after the rows
    /// move (the selected row goes innermost with <c>tsTabs</c>) the next click still finds the tab under the pointer.
    /// </summary>
    [Theory]
    [InlineData("tpTop", "tsTabs")]
    [InlineData("tpBottom", "tsTabs")]
    [InlineData("tpLeft", "tsTabs")]
    [InlineData("tpRight", "tsTabs")]
    [InlineData("tpTop", "tsButtons")]
    [InlineData("tpBottom", "tsButtons")]
    [InlineData("tpLeft", "tsFlatButtons")]
    [InlineData("tpRight", "tsFlatButtons")]
    [InlineData("tpTop", "tsFlatButtons")]
    [InlineData("tpBottom", "tsFlatButtons")]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-RND-023")]
    [Trait("Req", "DSG-RND-020")]
    public void ClickOnTabSelectsSheetAtEveryPosition(string position, string style)
    {
        var sheets = string.Concat(Enumerable.Range(1, 6).Select(i => $"    object S{i}: TTabSheet\n      Caption = 'Sheet {i}'\n    end\n"));
        using var h = DesignerHarness.FromText(
            "object Form1: TForm\n  ClientWidth = 400\n  ClientHeight = 300\n  object Book: TPageControl\n    Left = 8\n    Top = 8\n"
            + $"    Width = 289\n    Height = 193\n    MultiLine = True\n    Style = {style}\n    TabPosition = {position}\n    TabWidth = 60\n"
            + sheets + "  end\nend\n", catalog: Layout.LayoutHarness.Catalog);
        (int X, int Y) Centre(string sheet)
        {
            var ctx = RenderContext.FromLayout(h.Object("Book"), h.Layout);
            Assert.Equal(2, ctx.TabStrip!.RowCount);
            var tab = PageControlRenderer.Tabs(ctx, h.Controller.MeasureCanvas).Single(t => t.Sheet.Name == sheet).Tab;
            return (tab.Left + tab.Width / 2, tab.Top + tab.Height / 2);
        }

        var units = 0;
        foreach (var sheet in (string[])["S6", "S2", "S5", "S4", "S1", "S3"])
        {
            h.Click("Book", Centre(sheet));
            units++;
            Assert.Equal([sheet], h.Selected);
            Assert.True(h.Layout.GetVisible(h.Object(sheet)), $"{sheet} is the active page");
            Assert.Equal(units, h.Undo.UndoCount);
            Assert.Equal(sheet == "S1" ? null : sheet, PropertyAccess.ReferencedName(h.Object("Book"), "ActivePage"));
        }
    }

    /// <summary>
    /// UI-DSG-084 (D-113): over a tab the cursor is the arrow and the hovered tab gets a 1 px handle-border underline; the
    /// selected sheet's outline is the page rectangle, without resize handles.
    /// </summary>
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "UI-DSG-084")]
    public void TabsAreHoveredWithAnUnderlineAndTheSheetOutlineHasNoHandles()
    {
        using var h = DesignerHarness.FromText(Text, catalog: Layout.LayoutHarness.Catalog);
        var book = h.Controller.Geometry.GetBounds(h.Object("Book"));
        Rect Tab(string sheet)
        {
            var ctx = RenderContext.FromLayout(h.Object("Book"), h.Layout);
            var t = PageControlRenderer.Tabs(ctx, h.Controller.MeasureCanvas).Single(x => x.Sheet.Name == sheet).Tab;
            return new Rect(book.Left + t.Left, book.Top + t.Top, t.Width, t.Height);
        }

        RecordingCanvas Render()
        {
            var canvas = new RecordingCanvas();
            h.Controller.Render(canvas, new Rect(0, 0, 2000, 2000));
            return canvas;
        }

        void Hover(double x, double y) => h.Controller.OnPointer(new PointerInput(PointerKind.Move, x, y, PointerButton.None, InputModifiers.None));
        string Underline(Rect tab) => $"FillRect {tab.Left},{tab.Bottom - 1},{tab.Width},1 {h.Controller.Palette.HandleBorder}";

        // The page control selected: over its body the move cursor; over a tab the arrow.
        h.Click("Book", (280, 10));
        Assert.Equal(["Book"], h.Selected);
        var second = Tab("S2");
        Assert.Equal(CursorKind.SizeAll, h.Controller.GetCursor(book.Left + 280, book.Top + 10)); // the strip beyond the tabs
        Assert.Equal(CursorKind.Arrow, h.Controller.GetCursor(second.Left + 3, second.Top + 3));

        // Hovering a tab underlines it (one tab at a time); leaving the strip removes the underline.
        var invalidated = 0;
        h.Controller.Invalidated += (_, _) => invalidated++;
        Hover(second.Left + 3, second.Top + 3);
        Assert.Equal(1, invalidated);
        Assert.Contains(Render().Ops, op => op.Text == Underline(second));
        Hover(second.Left + 4, second.Top + 3); // same tab: nothing to redraw
        Assert.Equal(1, invalidated);
        var third = Tab("S3");
        Hover(third.Left + 3, third.Top + 3);
        var ops = Render().Ops;
        Assert.Contains(ops, op => op.Text == Underline(third));
        Assert.DoesNotContain(ops, op => op.Text == Underline(second));
        Hover(book.Left + 150, book.Top + 100);
        ops = Render().Ops;
        Assert.DoesNotContain(ops, op => op.Text == Underline(Tab("S1")) || op.Text == Underline(second) || op.Text == Underline(third));

        // The selected sheet: its outline is the page rectangle, and no handle is drawn or hit.
        h.Click("Book", (second.Left - book.Left + 3, second.Top - book.Top + 3));
        Assert.Equal(["S2"], h.Selected);
        var page = h.Controller.Geometry.GetBounds(h.Object("S2"));
        ops = Render().Ops;
        Assert.Contains(ops, op => op.Kind == "DrawRect" && op.Text.StartsWith($"DrawRect {page.Left},{page.Top},{page.Width},{page.Height} {h.Controller.Palette.HandleBorder}", StringComparison.Ordinal));
        Assert.Empty(new HitTester(h.Controller.Geometry).GetHandleCenters(h.Object("S2")));
        Assert.Equal(CursorKind.Arrow, h.Controller.GetCursor(page.Right - 1, page.Bottom - 1));
    }

    /// <summary>
    /// Only inactive tab sheets are hidden (DSG-RND-017): an invisible tool button — also computed geometry — is drawn and
    /// hit in design mode like every hidden control (layout-engine.md DSG-LAY-005).
    /// </summary>
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-RND-017")]
    [Trait("Req", "DSG-LAY-005")]
    public void InvisibleToolButtonsAreDrawnAndHit()
    {
        using var h = DesignerHarness.FromText("""
            object Form1: TForm
              ClientWidth = 400
              ClientHeight = 300
              object Bar: TToolBar
                Width = 400
                object T1: TToolButton
                end
                object T2: TToolButton
                  Visible = False
                end
              end
            end

            """, catalog: Layout.LayoutHarness.Catalog);
        Assert.False(h.Layout.GetVisible(h.Object("T2")));

        h.Click("T2", (5, 5));
        Assert.Equal(["T2"], h.Selected);

        var canvas = new RecordingCanvas();
        h.Controller.Render(canvas, new Rect(0, 0, 800, 600));
        var t2 = h.Controller.Geometry.GetBounds(h.Object("T2"));
        Assert.Contains(canvas.Ops, op => op.Kind == "DrawRect" && op.Extent is { } e
            && e.Left >= t2.Left && e.Right <= t2.Right && e.Top >= t2.Top && e.Bottom <= t2.Bottom); // its image placeholder
    }
}
