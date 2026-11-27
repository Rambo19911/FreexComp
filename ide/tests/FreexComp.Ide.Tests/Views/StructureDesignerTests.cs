using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.EndToEnd;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

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

    /// <summary>
    /// UI-TW-1610 (D-150): a component row shows its palette icon (16 × 16) in place of the symbol-kind icon, the form root
    /// the <c>form</c> glyph, a component without a usable icon (unknown class, missing file) the generic glyph; the gear
    /// and warning overlays stay.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-1610")]
    public async Task RowsShowPaletteIcons()
    {
        await using var h = await StartWithFrameworkMetadataAsync();
        await OpenDesignAsync(h);
        var structure = Assert.IsType<StructureViewModel>(h.Services.ToolWindows.Get(StructureViewModel.ToolName));
        Dispatcher.UIThread.RunJobs();
        Assert.True(structure.IsDesign);

        // The root: the form glyph, no palette icon.
        var root = h.Find<Control>(Row + "MainForm");
        Assert.Equal(["form"], root.GetVisualDescendants().OfType<IdeIcon>().Where(i => i.Classes.Contains("symbol")).Select(i => i.IconName));
        Assert.Empty(root.GetVisualDescendants().OfType<PaletteIcon>());

        // Known components: their own palette icon, 16 × 16, no symbol-kind icon.
        foreach (var name in new[] { "NameEdit", "Panel1", "Caption1", "GreetButton", "Timer1" })
        {
            var row = h.Find<Control>(Row + name);
            var icon = Assert.Single(row.GetVisualDescendants().OfType<PaletteIcon>());
            Assert.False(icon.IsGeneric, name + " shows the generic glyph");
            Assert.Equal((16.0, 16.0), (icon.Bounds.Width, icon.Bounds.Height));
            Assert.DoesNotContain(row.GetVisualDescendants().OfType<IdeIcon>(), i => i.Classes.Contains("symbol"));
        }

        // The unknown class: the generic glyph with the warning overlay; the non-visual timer keeps its gear.
        Assert.True(Assert.Single(h.Find<Control>(Row + "Gadget1").GetVisualDescendants().OfType<PaletteIcon>()).IsGeneric);
        Assert.Equal(["warning"], Overlays(h, "Gadget1"));
        Assert.Equal(["settings"], Overlays(h, "Timer1"));

        // A palette icon path that does not exist (or is not an SVG): the generic glyph, no message.
        Assert.True(PaletteIcon.FromFile(Path.Combine(h.TempDir, "Missing.svg")).IsGeneric);
        Assert.True(PaletteIcon.FromFile(null).IsGeneric);
    }

    /// <summary>
    /// UI-TW-1614 (D-150): the Design view's context menu has, after <c>edit.delete</c>, a separator, then
    /// <c>designer.bringToFront</c> and <c>designer.sendToBack</c> with the surface's enabled rules (the order sub-menu is
    /// not repeated, so every item id stays unique).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-1614")]
    public async Task ContextMenuHasOrderEntries()
    {
        await using var h = await StartWithFrameworkMetadataAsync();
        var document = await OpenDesignAsync(h);
        var surface = document.Designer.Surface!;
        var list = h.Find<ListBox>("Tool.Structure.List");

        (bool Front, bool Back) Order(string name)
        {
            h.RightClick(Row + name);
            Assert.Equal([name], surface.Selection.Items.Select(o => o.Name));
            var menu = Assert.IsType<ContextMenu>(list.ContextMenu);
            Assert.True(menu.IsOpen);
            var top = menu.Items.Select(i => i is MenuItem m ? AutomationIds.Get(m) ?? "" : "-").ToList();
            var delete = top.IndexOf("Context.edit.delete");
            Assert.True(delete >= 0, string.Join(", ", top));
            Assert.Equal(["-", "Context.designer.bringToFront", "Context.designer.sendToBack"], top.Skip(delete + 1).Take(3));
            var all = Items(menu.Items.OfType<MenuItem>()).Select(AutomationIds.Get).ToList();
            Assert.Equal(all.Count, all.Distinct().Count());
            var items = menu.Items.OfType<MenuItem>().ToList();
            var front = items.Single(i => AutomationIds.Get(i) == "Context.designer.bringToFront").IsEffectivelyEnabled;
            var back = items.Single(i => AutomationIds.Get(i) == "Context.designer.sendToBack").IsEffectivelyEnabled;
            Assert.Equal(surface.CanExecute(FreexComp.Designer.Surface.Commands.BringToFront), front);
            Assert.Equal(surface.CanExecute(FreexComp.Designer.Surface.Commands.SendToBack), back);
            menu.Close();
            Dispatcher.UIThread.RunJobs();
            return (front, back);
        }

        // The last created control is already in front; the form has no order.
        Assert.Equal((false, true), Order("GreetButton"));
        Assert.Equal((true, true), Order("NameEdit")); // the timer (created first) is the form's first child
        Assert.Equal((false, false), Order("MainForm"));

        // The entry runs the command: NameEdit moves to the front of the form's children.
        h.RightClick(Row + "NameEdit");
        var bring = Items(Assert.IsType<ContextMenu>(list.ContextMenu).Items.OfType<MenuItem>()).Single(i => AutomationIds.Get(i) == "Context.designer.bringToFront");
        list.ContextMenu!.Close();
        bring.Command!.Execute(bring.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("NameEdit", document.Designer.Model!.Root.Children[^1].Name);
    }

    // D-151: a second timer (non-visual siblings to reorder); no unknown class, so every row can be dragged.
    private const string DragUnit = """
        unit MainForm;

        interface

        uses
          Classes, Controls, Forms, StdCtrls, ExtCtrls;

        type
          TMainForm = class(TForm)
            Timer1: TTimer;
            Timer2: TTimer;
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

    private const string DragForm = """
        object MainForm: TMainForm
          Left = 200
          Top = 120
          Caption = 'MainForm'
          ClientWidth = 400
          ClientHeight = 300
          PixelsPerInch = 96
          object Timer1: TTimer
          end
          object Timer2: TTimer
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

    /// <summary>
    /// UI-TW-1611, 1613 (D-151, acceptance 1): the button's row dragged onto the middle of the panel's row (the row
    /// highlighted while hovering) makes it the panel's last child: its form position converted to the panel's client
    /// coordinates and clamped into its client rect, its tab order renumbered, the block written inside the panel's in
    /// the <c>.form</c> text; the document is modified, the button stays selected, the unit's code is unchanged.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-1611")]
    [Trait("Req", "UI-TW-1613")]
    public async Task DragButtonOntoPanelReparentsAndSerialises()
    {
        await using var h = await StartDragAsync();
        var document = await OpenDesignAsync(h);
        var designer = document.Designer;
        var surface = designer.Surface!;
        var structure = Structure(h);
        var model = designer.Model!;
        var button = model.FindByName("GreetButton")!;
        var panel = model.FindByName("Panel1")!;
        var bounds = surface.Geometry.GetBounds(button);
        var client = surface.Geometry.GetClientRect(panel);
        var unit = document.Buffer.Text;
        Assert.False(document.IsModified);

        DragRow(h, "GreetButton", "Panel1", 0.5, release: false);
        Assert.True(structure.IsDragging);
        Assert.Equal((StructureDropPosition.Into, true), (structure.DropTarget!.Position, structure.DropTarget.IsValid));
        var view = View(h);
        Assert.NotNull(view.HighlightedRow);
        Assert.Same(Resource(h, "FxcBgSelectedInactiveBrush"), view.HighlightedRow!.Background);
        Assert.Same(Resource(h, "FxcAccentDefaultBrush"), view.HighlightedRow.BorderBrush);
        Assert.False(view.DropLine.IsVisible);
        Release(h, "Panel1", 0.5);

        Assert.False(structure.IsDragging);
        Assert.Same(panel, button.Parent);
        Assert.Equal(["Caption1", "GreetButton"], panel.Children.Select(c => c.Name));
        var left = Math.Clamp(bounds.Left - client.Left, 0, Math.Max(0, client.Width - bounds.Width));
        var top = Math.Clamp(bounds.Top - client.Top, 0, Math.Max(0, client.Height - bounds.Height));
        Assert.Equal(0, top); // above the panel: clamped to its top edge
        var text = designer.CurrentFormText().ReplaceLineEndings("\n");
        Assert.Contains($$"""
                object Caption1: TLabel
                  Left = 8
                  Top = 8
                  Width = 40
                  Height = 15
                  Caption = 'Hello'
                end
                object GreetButton: TButton
                  Left = {{left}}
                  Top = {{top}}
                  Width = 75
                  Height = 25
                  Caption = '&Greet'
                  TabOrder = 0
                end
              end
            end
            """.ReplaceLineEndings("\n"), text, StringComparison.Ordinal);
        Assert.True(document.IsModified);
        Assert.Equal(["GreetButton"], surface.Selection.Items.Select(o => o.Name));
        Assert.Equal(unit, document.Buffer.Text);

        // The tree follows: the button is a row under the panel, still selected.
        Assert.Equal(
            ["Form/MainForm", "Form/NameEdit", "Form/Panel1", "Form/Caption1", "Form/GreetButton", "Form/Timer1", "Form/Timer2"],
            structure.Rows.Select(r => r.Node.Key));
        Assert.Equal(2, structure.Rows.Single(r => r.Node.Key == "Form/GreetButton").Depth);
        Assert.Equal(["Form/GreetButton"], structure.SelectedRows.Select(r => r.Node.Key));
    }

    /// <summary>
    /// UI-TW-1613 (D-151): a re-parented control stays visible — its position, converted to the new parent's client
    /// coordinates, is clamped so that the whole control lies inside the client rect (<c>0 ≤ Left ≤ ClientWidth − Width</c>,
    /// <c>0 ≤ Top ≤ ClientHeight − Height</c>); a control larger than the client rect goes to 0.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-1613")]
    public async Task ReparentedControlStaysInsideParent()
    {
        // The button lies right of and above the panel: it ends in the panel's top-right corner, entirely inside it.
        await using (var h = await StartDragAsync())
        {
            var designer = (await OpenDesignAsync(h)).Designer;
            var surface = designer.Surface!;
            var button = designer.Model!.FindByName("GreetButton")!;
            var panel = designer.Model.FindByName("Panel1")!;
            var client = surface.Geometry.GetClientRect(panel);
            Assert.True(client.Width > 75 && client.Height > 25, client.ToString());

            DragRow(h, "GreetButton", "Panel1", 0.5);
            Assert.Same(panel, button.Parent);
            Assert.Equal((client.Width - 75, 0), (Prop(panel, "GreetButton", "Left"), Prop(panel, "GreetButton", "Top")));
            var bounds = surface.Geometry.GetBounds(button);
            client = surface.Geometry.GetClientRect(panel);
            Assert.True(bounds.Left >= client.Left && bounds.Right <= client.Right && bounds.Top >= client.Top && bounds.Bottom <= client.Bottom,
                $"{bounds} not inside {client}");
        }

        // Wider than the panel's client rect: Left goes to 0.
        await using (var h = await StartDragAsync(DragForm.Replace("Width = 75", "Width = 300", StringComparison.Ordinal)))
        {
            var designer = (await OpenDesignAsync(h)).Designer;
            var panel = designer.Model!.FindByName("Panel1")!;
            Assert.True(designer.Surface!.Geometry.GetClientRect(panel).Width < 300);

            DragRow(h, "GreetButton", "Panel1", 0.5);
            Assert.Same(panel, designer.Model.FindByName("GreetButton")!.Parent);
            Assert.Equal((0, 0), (Prop(panel, "GreetButton", "Left"), Prop(panel, "GreetButton", "Top")));
        }
    }

    /// <summary>
    /// UI-TW-1612 (D-151, acceptance 2): over a component that cannot hold controls the "no" cursor shows; the drop there
    /// changes nothing and shows <c>Msg.Structure.DropRefused</c> in the status bar.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-1612")]
    public async Task DropOntoNonContainerRefused()
    {
        await using var h = await StartDragAsync();
        var document = await OpenDesignAsync(h);
        var designer = document.Designer;
        var structure = Structure(h);
        var before = designer.CurrentFormText();
        var undo = designer.Undo.UndoCount;

        DragRow(h, "GreetButton", "NameEdit", 0.5, release: false);
        Assert.Equal((StructureDropPosition.Into, false), (structure.DropTarget!.Position, structure.DropTarget.IsValid));
        var list = h.Find<ListBox>("Tool.Structure.List");
        Assert.NotNull(list.Cursor);
        Assert.Null(View(h).HighlightedRow);
        Assert.False(View(h).DropLine.IsVisible);
        Release(h, "NameEdit", 0.5);

        Assert.Null(list.Cursor);
        Assert.Equal(before, designer.CurrentFormText());
        Assert.Equal(undo, designer.Undo.UndoCount);
        Assert.False(document.IsModified);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Core.Resources.Strings.Msg_Structure_DropRefused, "NameEdit"),
            h.Status.Message);

        // A control cannot go into itself either.
        DragRow(h, "Panel1", "Caption1", 0.5, release: false);
        Assert.False(structure.DropTarget!.IsValid);
        Release(h, "Caption1", 0.5);
        Assert.Equal(before, designer.CurrentFormText());
    }

    /// <summary>
    /// UI-TW-1611, 1613 (D-151): the upper / lower quarter of a row is before / after it in its parent (the insertion line
    /// shows it); the dragged items keep their relative order; a press on a row of a multi-selection drags all of it.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-1611")]
    [Trait("Req", "UI-TW-1613")]
    public async Task DragBetweenRowsReorders()
    {
        await using var h = await StartDragAsync();
        var document = await OpenDesignAsync(h);
        var designer = document.Designer;
        var structure = Structure(h);
        var root = designer.Model!.Root;

        // NameEdit after GreetButton (lower quarter): the insertion line under the button's row.
        DragRow(h, "NameEdit", "GreetButton", 0.9, release: false);
        Assert.Equal((StructureDropPosition.After, true), (structure.DropTarget!.Position, structure.DropTarget.IsValid));
        var line = View(h).DropLine;
        Assert.True(line.IsVisible);
        Assert.Equal(2, line.Bounds.Height);
        Assert.Same(Resource(h, "FxcAccentDefaultBrush"), line.Background);
        var buttonRow = Container(h, "GreetButton");
        var rowBottom = buttonRow.TranslatePoint(new Point(0, buttonRow.Bounds.Height), View(h))!.Value.Y;
        Assert.InRange(line.TranslatePoint(new Point(0, 1), View(h))!.Value.Y, rowBottom - 1, rowBottom + 1);
        Release(h, "GreetButton", 0.9);
        Assert.Equal(["Timer1", "Timer2", "Panel1", "GreetButton", "NameEdit"], root.Children.Select(c => c.Name));
        Assert.Equal((16, 16), (Prop(root, "NameEdit", "Left"), Prop(root, "NameEdit", "Top"))); // same parent: the position stays
        Assert.Contains("    TabOrder = 2\r\n  end\r\n  object NameEdit: TEdit\r\n".ReplaceLineEndings("\n"),
            designer.CurrentFormText().ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // Both buttons selected, a press on one of them, then before Panel1 (upper quarter): both move, in document order.
        designer.Surface!.Selection.Set(["NameEdit", "GreetButton"]);
        Dispatcher.UIThread.RunJobs();
        DragRow(h, "NameEdit", "Panel1", 0.1, release: false);
        Assert.Equal((StructureDropPosition.Before, true), (structure.DropTarget!.Position, structure.DropTarget.IsValid));
        Release(h, "Panel1", 0.1);
        Assert.Equal(["Timer1", "Timer2", "GreetButton", "NameEdit", "Panel1"], root.Children.Select(c => c.Name));
        Assert.Equal(["GreetButton", "NameEdit"], designer.Surface.Selection.Items.Select(o => o.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// UI-TW-1612 (D-151): a non-visual component keeps its parent (the form) — a drop into a container is refused with
    /// <c>Msg.Structure.NonVisualStaysInParent</c> — and is only reordered among its siblings, its tray position unchanged.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-1612")]
    public async Task NonVisualOnlyReorders()
    {
        await using var h = await StartDragAsync();
        var document = await OpenDesignAsync(h);
        var designer = document.Designer;
        var structure = Structure(h);
        var root = designer.Model!.Root;
        var before = designer.CurrentFormText();
        var tray = (Prop(root, "Timer2", "DesignLeft"), Prop(root, "Timer2", "DesignTop"));

        var refused = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            Core.Resources.Strings.Msg_Structure_NonVisualStaysInParent, "Timer2", "MainForm");
        DragRow(h, "Timer2", "Panel1", 0.5, release: false);
        Assert.False(structure.DropTarget!.IsValid);
        Release(h, "Panel1", 0.5);
        Assert.Equal(refused, h.Status.Message);
        DragRow(h, "Timer2", "Caption1", 0.1, release: false); // before a control inside the panel: the panel's list
        Assert.False(structure.DropTarget!.IsValid);
        Release(h, "Caption1", 0.1);
        Assert.Equal(refused, h.Status.Message);
        Assert.Equal(before, designer.CurrentFormText());

        // Before Timer1: the first child of the form; the tree lists the timers after the controls, in the new order.
        DragRow(h, "Timer2", "Timer1", 0.1, release: false);
        Assert.Equal((StructureDropPosition.Before, true), (structure.DropTarget!.Position, structure.DropTarget.IsValid));
        Release(h, "Timer1", 0.1);
        Assert.Equal(["Timer2", "Timer1", "NameEdit", "Panel1", "GreetButton"], root.Children.Select(c => c.Name));
        Assert.Equal(["Form/Timer2", "Form/Timer1"], structure.Rows.Skip(5).Select(r => r.Node.Key));
        var after = designer.CurrentFormText();
        Assert.True(after.IndexOf("object Timer2", StringComparison.Ordinal) < after.IndexOf("object Timer1", StringComparison.Ordinal));
        Assert.Equal(before.Length, after.Length); // only the two blocks swapped
        Assert.Equal(tray, (Prop(root, "Timer2", "DesignLeft"), Prop(root, "Timer2", "DesignTop")));
    }

    /// <summary>
    /// UI-TW-1613 (D-151): a drop is one undo step <c>Move in Structure</c> — undo restores the parent, position and
    /// <c>.form</c> text, redo repeats it; a drop at the item's own position records nothing.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-1613")]
    public async Task MoveInStructureIsOneUndoStep()
    {
        await using var h = await StartDragAsync();
        var document = await OpenDesignAsync(h);
        var designer = document.Designer;
        var model = designer.Model!;
        var original = designer.CurrentFormText();
        var count = designer.Undo.UndoCount;

        // At its own place (before itself): nothing recorded.
        DragRow(h, "NameEdit", "NameEdit", 0.1);
        Assert.Equal(count, designer.Undo.UndoCount);
        Assert.Equal(original, designer.CurrentFormText());

        DragRow(h, "GreetButton", "Panel1", 0.5);
        Assert.Equal((count + 1, FreexComp.Designer.Surface.UndoTitles.MoveInStructure), (designer.Undo.UndoCount, designer.Undo.UndoTitleKey));
        var moved = designer.CurrentFormText();
        Assert.NotEqual(original, moved);

        await h.ExecuteAsync("edit.undo");
        Assert.Equal(original, designer.CurrentFormText());
        Assert.Same(model.Root, model.FindByName("GreetButton")!.Parent);
        Assert.Equal((232, 15), (Prop(model.Root, "GreetButton", "Left"), Prop(model.Root, "GreetButton", "Top")));
        Assert.Equal(count, designer.Undo.UndoCount);

        await h.ExecuteAsync("edit.redo");
        Assert.Equal(moved, designer.CurrentFormText());
        Assert.Equal("Panel1", model.FindByName("GreetButton")!.Parent!.Name);
    }

    /// <summary>UI-TW-1612 (D-151): Escape during the drag cancels it — no target, no highlight, nothing changes on release.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-1612")]
    public async Task EscapeCancelsDrag()
    {
        await using var h = await StartDragAsync();
        var document = await OpenDesignAsync(h);
        var designer = document.Designer;
        var structure = Structure(h);
        var before = designer.CurrentFormText();

        DragRow(h, "GreetButton", "Panel1", 0.5, release: false);
        Assert.True(structure.IsDragging);
        Assert.NotNull(View(h).HighlightedRow);
        h.Press("Escape");
        Assert.False(structure.IsDragging);
        Assert.Null(structure.DropTarget);
        Assert.Null(View(h).HighlightedRow);
        Assert.Null(h.Find<ListBox>("Tool.Structure.List").Cursor);
        Release(h, "Panel1", 0.5);

        Assert.Equal(before, designer.CurrentFormText());
        Assert.Equal("MainForm", designer.Model!.FindByName("GreetButton")!.Parent!.Name);
        Assert.False(document.IsModified);
    }

    private static Task<HeadlessHarness> StartDragAsync(string form = DragForm) =>
        HeadlessHarness.StartAsync(o =>
        {
            o.ComponentMetadata = false;
            o.Settings["toolchain.frameworkDesignDir"] = LayoutRoundTrip.FrameworkDesignDir;
            o.InitialFiles["MainForm.pas"] = DragUnit.ReplaceLineEndings("\r\n");
            o.InitialFiles["MainForm.form"] = form.ReplaceLineEndings("\r\n");
        });

    private static StructureViewModel Structure(HeadlessHarness h) =>
        Assert.IsType<StructureViewModel>(h.Services.ToolWindows.Get(StructureViewModel.ToolName));

    private static StructureView View(HeadlessHarness h) => h.Window.GetVisualDescendants().OfType<StructureView>().Single();

    private static ListBoxItem Container(HeadlessHarness h, string name) => h.Find<Control>(Row + name).FindAncestorOfType<ListBoxItem>()!;

    /// <summary>A point in the row of <paramref name="name"/> at <paramref name="fraction"/> of its height (window coordinates).</summary>
    private static Point RowPoint(HeadlessHarness h, string name, double fraction)
    {
        var item = Container(h, name);
        return item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height * fraction), h.Window)!.Value;
    }

    /// <summary>Presses on the row of <paramref name="from"/>, moves past the drag threshold, then over <paramref name="to"/>.</summary>
    private static void DragRow(HeadlessHarness h, string from, string to, double fraction, bool release = true)
    {
        var start = RowPoint(h, from, 0.5);
        h.Window.MouseDown(start, MouseButton.Left);
        h.Window.MouseMove(start + new Point(6, 0), RawInputModifiers.LeftMouseButton);
        h.Window.MouseMove(RowPoint(h, to, fraction), RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        if (release)
            Release(h, to, fraction);
    }

    private static void Release(HeadlessHarness h, string at, double fraction)
    {
        h.Window.MouseUp(RowPoint(h, at, fraction), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static int Prop(FreexComp.Designer.Model.FormObject root, string name, string property) =>
        FreexComp.Designer.Model.PropertyAccess.GetInt(Find(root, name)!, property);

    private static FreexComp.Designer.Model.FormObject? Find(FreexComp.Designer.Model.FormObject obj, string name) =>
        obj.Name == name ? obj : obj.Children.Select(c => Find(c, name)).FirstOrDefault(o => o is not null);

    /// <summary>A harness with the shipped framework metadata (palette icons) and the test form.</summary>
    private static Task<HeadlessHarness> StartWithFrameworkMetadataAsync() =>
        HeadlessHarness.StartAsync(o =>
        {
            o.ComponentMetadata = false;
            o.Settings["toolchain.frameworkDesignDir"] = LayoutRoundTrip.FrameworkDesignDir;
            o.InitialFiles["MainForm.pas"] = Unit.ReplaceLineEndings("\r\n");
            o.InitialFiles["MainForm.form"] = FormText.ReplaceLineEndings("\r\n");
        });

    /// <summary>Opens the test form in the Design view with the harness's (shipped) catalog.</summary>
    private static async Task<FormDocumentViewModel> OpenDesignAsync(HeadlessHarness h)
    {
        var session = await Phase5Scenario.OpenDesignAsync(h, Path.Combine(h.TempDir, "MainForm.pas"));
        Dispatcher.UIThread.RunJobs();
        return session.Document;
    }

    private static IEnumerable<MenuItem> Items(IEnumerable<MenuItem> items) =>
        items.SelectMany(i => new[] { i }.Concat(Items(i.Items.OfType<MenuItem>())));

    private static List<string> Overlays(HeadlessHarness h, string name) =>
        [.. h.Find<Control>(Row + name).GetVisualDescendants().OfType<IdeIcon>().Select(i => i.IconName ?? "")
            .Where(n => n is "settings" or "warning")];

    private static TextBlock Text(HeadlessHarness h, string name) =>
        h.Find<Control>(Row + name).GetVisualDescendants().OfType<TextBlock>().Last();

    private static object? Resource(HeadlessHarness h, string key) =>
        h.Window.TryFindResource(key, h.Window.ActualThemeVariant, out var value) ? value : null;
}
