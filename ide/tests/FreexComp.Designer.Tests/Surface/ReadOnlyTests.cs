using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Read-only forms and preserved items on the design surface (design-surface.md DSG-SUR-003, 004; D-055).</summary>
public sealed class ReadOnlyTests
{
    private const string Form = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object P1: TPanel
            Left = 200
            Top = 100
            Width = 185
            Height = 105
            Caption = 'P1'
            TabOrder = 0
          end
          object B1: TButton
            Left = 8
            Top = 8
            Width = 75
            Height = 25
            Caption = 'B1'
            TabOrder = 1
          end
          object B2: TButton
            Left = 8
            Top = 48
            Width = 75
            Height = 25
            Caption = 'B2'
            TabOrder = 2
          end
          object Timer1: TTimer
            DesignLeft = 8
            DesignTop = 8
          end
        end

        """;

    // An object of an unknown class (preserved, F0101) with readable bounds, next to an ordinary control.
    private const string WithPreserved = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object Chart1: TChart
            Left = 8
            Top = 8
            Width = 200
            Height = 120
            Title.Text = (
              'Sales'
            )
          end
          object OkButton: TButton
            Left = 240
            Top = 8
            Width = 75
            Height = 25
            Caption = 'OK'
            TabOrder = 0
          end
        end

        """;

    private static readonly string[] Mutating =
    [
        "edit.cut", "edit.paste", "edit.delete", "edit.undo", "edit.redo", "designer.alignLeft", "designer.sameSize",
        "designer.alignToGrid", "designer.bringToFront", "designer.sendToBack", "designer.tabOrder", "designer.createDefaultHandler",
    ];

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-003")]
    public void ReadOnlyFormAllowsOnlySelectionAndCopy()
    {
        using var h = DesignerHarness.FromText(Form);
        h.Select("B1");
        h.Key(DesignKey.Right); // one unit, so that undo would be possible
        h.ExecuteUndo();
        Assert.True(h.Controller.CanExecute("edit.redo"));
        var original = h.Text;

        h.Controller.IsReadOnly = true;
        Assert.True(h.Controller.IsReadOnly);

        // Selection, lasso, the selection commands, Copy and the context menu work.
        h.Click("B1");
        h.Click("B2", modifiers: InputModifiers.Shift);
        Assert.Equal(["B1", "B2"], h.Selected);
        foreach (var command in Mutating)
        {
            Assert.False(h.Controller.CanExecute(command), command);
        }

        Assert.True(h.Controller.CanExecute("edit.copy"));
        h.Execute("edit.copy");
        Assert.Contains("object B2: TButton", h.Host.Clipboard.GetData("Text"));
        Assert.False(h.Controller.CanExecute("edit.paste"));
        h.PressAt(h.Controller.Geometry.FormClientToSurface((4, 4)));
        h.MoveTo(h.Controller.Geometry.FormClientToSurface((100, 60)));
        h.Release();
        Assert.Equal(["B1", "B2"], h.Selected); // the lasso selected both again
        h.Execute("designer.selectNext");
        Assert.Equal(["Timer1"], h.Selected);
        h.Execute("edit.selectAll");
        Assert.Equal(["Timer1"], h.Selected);
        Assert.True(h.Key(DesignKey.Escape));
        Assert.Equal(["F"], h.Selected);
        h.RightClick("P1");
        Assert.Single(h.Host.ContextMenus);
        Assert.Equal(["P1"], h.Selected);

        // Mutating gestures are ignored without a message.
        h.Press("P1");
        h.MoveBy(40, 40);
        h.Release();
        h.PressHandle("P1", HandleKind.SE);
        h.MoveBy(40, 40);
        h.Release();
        h.Press("P1");
        h.MoveBy(40, 40);
        h.Release(InputModifiers.Ctrl);
        Assert.False(h.Key(DesignKey.Right));
        Assert.False(h.Key(DesignKey.Right, InputModifiers.Shift));
        Assert.False(h.Key(DesignKey.Delete));
        Assert.False(h.Key(DesignKey.Enter));
        Assert.False(h.Key(DesignKey.Up, InputModifiers.Ctrl | InputModifiers.Shift));
        Assert.False(h.Controller.OnTextInput("x"));
        h.PressAt(h.SurfacePoint("B1"), clickCount: 2);
        h.Release();
        h.Execute("edit.delete");
        h.Execute("designer.tabOrder");
        Assert.Equal(InteractionState.Idle, h.Controller.State);

        // Placement is refused silently; the item stays armed.
        h.Host.Arm("TButton");
        h.ClickAt(h.Controller.Geometry.FormClientToSurface((300, 20)));
        h.Controller.PlaceAtDefaultPosition("TButton");
        Assert.Equal("TButton", h.Host.ArmedPaletteClass);
        Assert.Equal(0, h.Host.DisarmCount);
        h.Host.Disarm();

        Assert.Equal(original, h.Text);
        Assert.Empty(h.Host.Statuses);
        Assert.Empty(h.Host.PropertyEdits);
        Assert.Empty(h.Host.CodeSync.CreatedHandlers);
        Assert.Equal(0, h.Undo.UndoCount);

        // Becoming read-only during a drag cancels it (code-sync.md DSG-CS-081); editable again afterwards.
        h.Controller.IsReadOnly = false;
        h.Press("B1");
        h.MoveBy(40, 40);
        Assert.Equal(InteractionState.Moving, h.Controller.State);
        h.Controller.IsReadOnly = true;
        Assert.Equal(InteractionState.Idle, h.Controller.State);
        h.Release();
        Assert.Equal(new Rect(8, 8, 75, 25), h.Bounds("B1"));
        Assert.Equal(original, h.Text);
        h.Controller.IsReadOnly = false;
        Assert.True(h.Controller.CanExecute("edit.delete"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-004")]
    public void PreservedItemCannotBeEditedButCanBeDeleted()
    {
        using var h = DesignerHarness.FromText(WithPreserved, allowDiagnostics: true);
        var original = h.Text;
        var chart = h.Object("Chart1");
        Assert.True(chart.IsPreserved);

        // It can be selected (rendered at its bounds).
        h.Click("Chart1");
        Assert.Equal(["Chart1"], h.Selected);

        // Moving, resizing and editing are refused with Msg.Designer.ReadOnlyItem.
        void AssertRefused(Action attempt)
        {
            var before = h.Host.Statuses.Count;
            attempt();
            Assert.Equal(before + 1, h.Host.Statuses.Count);
            Assert.Equal(("Msg.Designer.ReadOnlyItem", "Chart1"), (h.Host.Statuses[^1].Id, (string)h.Host.Statuses[^1].Args[0]));
        }

        AssertRefused(() =>
        {
            h.Press("Chart1");
            h.MoveBy(40, 40);
            h.Release();
        });
        AssertRefused(() =>
        {
            h.PressHandle("Chart1", HandleKind.SE);
            h.MoveBy(40, 40);
            h.Release();
        });
        AssertRefused(() => h.Key(DesignKey.Right));
        AssertRefused(() => h.Key(DesignKey.Down, InputModifiers.Shift));
        AssertRefused(() => Assert.True(h.Controller.OnTextInput("x")));
        AssertRefused(() => Assert.True(h.Key(DesignKey.Enter)));
        AssertRefused(() =>
        {
            h.PressAt(h.SurfacePoint("Chart1"), clickCount: 2);
            h.Release();
        });
        Assert.False(h.Controller.CanExecute("designer.createDefaultHandler"));
        Assert.Equal(new Rect(8, 8, 200, 120), h.Layout.GetBounds(chart));
        Assert.Equal(original, h.Text);
        Assert.Empty(h.Host.PropertyEdits);
        Assert.Empty(h.Host.CodeSync.CreatedHandlers);

        // In a multi-selection the others still move.
        h.Select("Chart1", "OkButton");
        h.Key(DesignKey.Right);
        Assert.Equal(new Rect(248, 8, 75, 25), h.Bounds("OkButton"));
        Assert.Equal(("Msg.Designer.ReadOnlyItem", "Chart1"), (h.Host.Statuses[^1].Id, (string)h.Host.Statuses[^1].Args[0]));
        h.ExecuteUndo();

        // Copy (its preserved text), z-order and delete work.
        h.Select("Chart1");
        h.Execute("edit.copy");
        Assert.Contains("object Chart1: TChart", h.Host.Clipboard.GetData("Text"));
        Assert.Contains("'Sales'", h.Host.Clipboard.GetData("Text"));
        Assert.True(h.Controller.CanExecute("designer.bringToFront"));
        h.Execute("designer.bringToFront");
        Assert.Equal(["OkButton", "Chart1"], h.ChildNames("F"));
        h.ExecuteUndo();
        Assert.True(h.Controller.CanExecute("edit.delete"));
        h.Key(DesignKey.Delete);
        Assert.Null(h.Document.FindByName("Chart1"));
        Assert.Equal(["F"], h.Selected);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
        Assert.Equal(["Chart1"], h.Selected);
    }

    /// <summary>
    /// DSG-FD-027 (found with D-078, the first view that measures every opened form): a root of a class the catalog does
    /// not know (an unknown class, or an empty catalog when no metadata was found) is preserved; the surface still lays
    /// out, hit-tests and draws the frame instead of failing on the unknown size properties.
    /// </summary>
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-027")]
    public void UnknownRootClassStillDrawsTheFrame()
    {
        var catalog = FreexComp.Designer.Catalog.ComponentCatalog.Empty; // DSG-TP-004: no metadata found
        var document = FreexComp.Designer.Text.FormParser.Parse("""
            object MainForm: TMainForm
              Caption = 'MainForm'
              object B1: TButton
                Left = 8
                Top = 8
              end
            end

            """, "MainForm.form", catalog).Document!;
        Assert.True(document.Root.IsPreserved);
        var controller = new DesignSurfaceController(document, new FreexComp.Designer.Editing.FormEditor(document, catalog),
            new FreexComp.Designer.Editing.DesignerUndoStack(), catalog,
            new FreexComp.Designer.Layout.LayoutEngine(catalog, FreexComp.Designer.Rendering.RendererRegistry.CreateDefault()),
            new FakeDesignerSettings(), new FakeDesignerHost(), new ManualTimeProvider());

        // Without metadata there is no default client size: the stored one, else 0 × 0 (the caption only).
        var g = controller.Geometry;
        Assert.Equal(new Rect(16, 16, 2, 32), g.FrameRect);
        var canvas = new FreexComp.Designer.Rendering.RecordingCanvas();
        controller.Render(canvas, new Rect(0, 0, 800, 600));
        Assert.NotEmpty(canvas.Ops);
        Assert.Equal(CursorKind.Arrow, controller.GetCursor(100, 100));
    }
}
