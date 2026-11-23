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
