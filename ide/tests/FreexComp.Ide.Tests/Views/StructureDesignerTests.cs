using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Structure tool window for a form in the Design view (tool-windows.md UI-TW-207, 209; D-086): the component tree
/// mirrors the form and its selection both ways; Enter focuses the surface; Delete deletes the selected components.
/// </summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class StructureDesignerTests
{
    private const string Row = "Tool.Structure.Row.Form/";

    private const string Unit = """
        unit MainForm;

        interface

        uses
          Classes, Controls, Forms, StdCtrls, ExtCtrls;

        type
          TMainForm = class(TForm)
            Timer1: TTimer;
            NameEdit: TEdit;
            Panel1: TPanel;
            Caption1: TLabel;
            GreetButton: TButton;
          private
          public
          end;

        var
          MainForm: TMainForm;

        implementation

        {$FORM MainForm.form}

        end.

        """;

    // Timer1 (non-visual) is created first, Gadget1 has a class the catalog does not know (preserved, read-only).
    private const string FormText = """
        object MainForm: TMainForm
          Left = 200
          Top = 120
          Caption = 'MainForm'
          ClientWidth = 400
          ClientHeight = 300
          PixelsPerInch = 96
          object Timer1: TTimer
          end
          object NameEdit: TEdit
            Left = 16
            Top = 16
            Width = 200
            Height = 23
            TabOrder = 0
          end
          object Panel1: TPanel
            Left = 16
            Top = 56
            Width = 200
            Height = 100
            TabOrder = 1
            object Caption1: TLabel
              Left = 8
              Top = 8
              Width = 40
              Height = 15
              Caption = 'Hello'
            end
          end
          object Gadget1: TGadget
            Left = 240
          end
          object GreetButton: TButton
            Left = 232
            Top = 15
            Width = 75
            Height = 25
            Caption = '&Greet'
            TabOrder = 2
          end
        end

        """;

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-207")]
    [Trait("Req", "UI-TW-209")]
    [Trait("Req", "IDE-ED-132")]
    public async Task TreeMirrorsFormAndSelection()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["MainForm.pas"] = Unit.ReplaceLineEndings("\r\n");
            o.InitialFiles["MainForm.form"] = FormText.ReplaceLineEndings("\r\n");
        });
        var document = await ToolPaletteViewTests.OpenDesignAsync(h);
        var designer = document.Designer;
        var surface = designer.Surface!;
        var structure = Assert.IsType<StructureViewModel>(h.Services.ToolWindows.Get(StructureViewModel.ToolName));
        Dispatcher.UIThread.RunJobs();

        // The form, then the components in creation order; the non-visual timer after its visual siblings; the row text
        // "Name: TClass"; containers expanded with their children.
        Assert.True(structure.IsDesign);
        Assert.Equal(
            ["Form/MainForm", "Form/NameEdit", "Form/Panel1", "Form/Caption1", "Form/Gadget1", "Form/GreetButton", "Form/Timer1"],
            structure.Rows.Select(r => r.Node.Key));
        Assert.Equal([0, 1, 1, 2, 1, 1, 1], structure.Rows.Select(r => r.Depth));
        Assert.Equal("Panel1: TPanel", structure.Rows[2].Node.Text);
        Assert.True(h.Find<Control>(Row + "Timer1").IsEffectivelyVisible);
        Assert.Equal(["settings"], Overlays(h, "Timer1"));
        Assert.Equal(["warning"], Overlays(h, "Gadget1"));
        Assert.Empty(Overlays(h, "NameEdit"));
        Assert.Same(Resource(h, "FxcTextDisabledBrush"), Text(h, "Gadget1").Foreground);
        Assert.Same(Resource(h, "FxcTextPrimaryBrush"), Text(h, "NameEdit").Foreground);

        // Surface → tree: a multi-selection on the surface selects the same rows.
        surface.Selection.Set(["NameEdit", "GreetButton"]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Form/NameEdit", "Form/GreetButton"], structure.SelectedRows.Select(r => r.Node.Key));
        Assert.Equal("Form/GreetButton", structure.SelectedRow?.Node.Key);
        var list = h.Find<ListBox>("Tool.Structure.List");
        Assert.Equal(["Form/NameEdit", "Form/GreetButton"], list.SelectedItems!.OfType<StructureRow>().Select(r => r.Node.Key).Order(StringComparer.Ordinal).Reverse());

        // Tree → surface: a click selects that component; Ctrl+click adds one (the last one primary).
        h.Click(Row + "Panel1");
        Assert.Equal(["Panel1"], surface.Selection.Items.Select(o => o.Name));
        var caption = h.Find<Control>(Row + "Caption1");
        var at = caption.TranslatePoint(new Avalonia.Point(caption.Bounds.Width / 2, caption.Bounds.Height / 2), h.Window)!.Value;
        h.Window.MouseDown(at, MouseButton.Left, RawInputModifiers.Control);
        h.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Panel1", "Caption1"], surface.Selection.Items.Select(o => o.Name));
        Assert.Equal("Caption1", surface.Selection.Primary.Name);
        Assert.Equal(["Form/Panel1", "Form/Caption1"], structure.SelectedRows.Select(r => r.Node.Key));

        // Delete in the tree deletes the selected components (as on the surface); the tree follows the model.
        h.Click(Row + "GreetButton");
        Assert.True(list.IsKeyboardFocusWithin);
        h.Press("Delete");
        Assert.Null(designer.Model!.FindByName("GreetButton"));
        Assert.DoesNotContain("GreetButton: TButton;", document.Buffer.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(structure.Rows, r => r.Node.Key == "Form/GreetButton");
        Assert.Null(h.TryFind<Control>(Row + "GreetButton"));

        // Enter (the default action) moves focus to the design surface.
        h.Click(Row + "NameEdit");
        Assert.Equal(["NameEdit"], surface.Selection.Items.Select(o => o.Name));
        h.Press("Enter");
        Assert.True(h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm")).IsKeyboardFocusWithin);

        // A component added on the surface appears in the tree, selected.
        surface.PlaceAtDefaultPosition("TButton");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(structure.Rows, r => r.Node.Key == "Form/Button1");
        Assert.Equal(["Form/Button1"], structure.SelectedRows.Select(r => r.Node.Key));

        // The Code view: the unit's declarations again.
        h.Press("F7");
        Assert.False(structure.IsDesign);
        await h.WaitForAsync(() => structure.Rows.Any(r => r.Node.Key == "Types"), "the unit's structure");
        Assert.DoesNotContain(structure.Rows, r => r.Node.Key.StartsWith(StructureViewModel.DesignKeyPrefix, StringComparison.Ordinal));
    }

    private static List<string> Overlays(HeadlessHarness h, string name) =>
        [.. h.Find<Control>(Row + name).GetVisualDescendants().OfType<IdeIcon>().Select(i => i.IconName ?? "")
            .Where(n => n is "settings" or "warning")];

    private static TextBlock Text(HeadlessHarness h, string name) =>
        h.Find<Control>(Row + name).GetVisualDescendants().OfType<TextBlock>().Last();

    private static object? Resource(HeadlessHarness h, string key) =>
        h.Window.TryFindResource(key, h.Window.ActualThemeVariant, out var value) ? value : null;
}
