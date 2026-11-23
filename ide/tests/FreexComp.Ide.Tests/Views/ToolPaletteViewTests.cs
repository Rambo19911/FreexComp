using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;
using Point = Avalonia.Point;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Tool Palette tool window (ui/designer.md §12, tool-palette.md; D-083): search with highlight, arming by click and
/// placing on the surface, the sticky visuals, the palette drag, the list / icon view and the empty state.
/// </summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class ToolPaletteViewTests
{
    private const string Item = "Tool.ToolPalette.Item.";
    private const string Page = "Tool.ToolPalette.Page.";

    /// <summary>The form files at Phase 5 with the real Object Inspector and Tool Palette.</summary>
    internal static void Phase5(HarnessOptions o)
    {
        DesignerViewTests.Files(o);
        o.ImplementedPhase = 5;
    }

    internal static async Task<FormDocumentViewModel> OpenDesignAsync(HeadlessHarness h)
    {
        var document = await DesignerViewTests.OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        return document;
    }

    /// <summary>The ids of the visible page headers and items, in order.</summary>
    private static List<string> Shown(HeadlessHarness h) =>
        [.. h.FindAll("Tool.ToolPalette.").Where(c => c.IsEffectivelyVisible).Select(c => AutomationIds.Get(c)!)
            .Where(id => id.StartsWith(Item, StringComparison.Ordinal) || id.StartsWith(Page, StringComparison.Ordinal))
            .Select(id => id["Tool.ToolPalette.".Length..])];

    /// <summary>The bold parts of an item's class name (UI-DSG-124).</summary>
    private static List<string> Bold(HeadlessHarness h, string className)
    {
        var text = h.Find<Control>(Item + className).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Inlines is { Count: > 0 });
        return [.. text.Inlines!.OfType<Run>().Where(r => r.FontWeight >= FontWeight.SemiBold).Select(r => r.Text!)];
    }

    private static string Name(HeadlessHarness h, string className) =>
        string.Concat(h.Find<Control>(Item + className).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Inlines is { Count: > 0 })
            .Inlines!.OfType<Run>().Select(r => r.Text));

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-120")]
    [Trait("Req", "UI-DSG-121")]
    [Trait("Req", "UI-DSG-122")]
    [Trait("Req", "UI-DSG-124")]
    [Trait("Req", "UI-TW-351")]
    [Trait("Req", "DSG-TP-022")]
    [Trait("Req", "DSG-TP-023")]
    [Trait("Req", "DSG-TP-030")]
    [Trait("Req", "DSG-TP-032")]
    public async Task SearchFiltersAndHighlights()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        await OpenDesignAsync(h);

        // Showing the tool window focuses the search box (placeholder "Search components"); all pages in metadata order.
        await h.ExecuteAsync("view.toolPalette");
        var search = h.Find<TextBox>("Tool.ToolPalette.Filter");
        Assert.True(search.IsFocused);
        Assert.Equal(Strings.ToolPalette_SearchPlaceholder, search.PlaceholderText);
        Assert.Equal(["Page.Standard", "Item.TMainMenu", "Item.TPopupMenu", "Item.TLabel", "Item.TEdit", "Item.TMemo", "Item.TButton",
            "Item.TCheckBox", "Item.TRadioButton", "Item.TListBox", "Item.TComboBox", "Item.TGroupBox", "Item.TPanel"], Shown(h).Take(13));
        Assert.Equal(["Page.Standard", "Page.Additional", "Page.Win32", "Page.System", "Page.Dialogs"], Shown(h).Where(k => k.StartsWith("Page.", StringComparison.Ordinal)));
        Assert.Equal(22, h.Find<Control>(Page + "Standard").Bounds.Height);
        Assert.Equal(28, h.Find<Control>(Item + "TButton").Bounds.Height);
        Assert.Equal("TButton", Name(h, "TButton"));
        Assert.Empty(Bold(h, "TButton"));

        // Typing filters on every keystroke: pages with matches stay, items starting with the text first, the match bold.
        h.Type("button");
        Assert.Equal(["Page.Standard", "Item.TButton", "Item.TRadioButton"], Shown(h));
        Assert.Equal(["Button"], Bold(h, "TButton"));
        Assert.Equal(["Button"], Bold(h, "TRadioButton"));
        Assert.Equal("TRadioButton", Name(h, "TRadioButton"));
        Assert.True(h.Find<Button>("Tool.ToolPalette.Filter.Clear").IsVisible);

        // Several words: each must match, each is bold.
        search.Text = "radio but";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Page.Standard", "Item.TRadioButton"], Shown(h));
        Assert.Equal(["RadioBut"], Bold(h, "TRadioButton")); // adjacent matches merge

        // A collapsed page with matches is shown expanded; without a match: Msg.Palette.FilterEmpty.
        search.Text = "";
        Dispatcher.UIThread.RunJobs();
        h.Find<Control>(Page + "Dialogs").BringIntoView(); // the last page: scrolled to, like a user would
        Dispatcher.UIThread.RunJobs();
        h.Click(Page + "Dialogs");
        Assert.DoesNotContain("Item.TOpenDialog", Shown(h));
        Assert.Equal(["Dialogs"], h.Services.Settings.Get(SettingsCatalog.ToolPaletteCollapsedPages));
        search.Focus();
        h.Type("dialog");
        Assert.Equal(["Page.Dialogs", "Item.TOpenDialog", "Item.TSaveDialog"], Shown(h));
        h.Type("zzz");
        Assert.Empty(Shown(h));
        var filterEmpty = h.Find<TextBlock>("Tool.ToolPalette.FilterEmpty");
        Assert.True(filterEmpty.IsEffectivelyVisible);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Palette_FilterEmpty, "dialogzzz"), filterEmpty.Text);

        // The clear button empties the search; the stored collapsed state is back.
        h.Click("Tool.ToolPalette.Filter.Clear");
        Assert.Equal("", search.Text);
        Assert.False(filterEmpty.IsEffectivelyVisible);
        Assert.DoesNotContain("Item.TOpenDialog", Shown(h));
        Assert.Contains("Page.Dialogs", Shown(h));

        // Icon view (toolPalette.view = icons): 32 × 32 cells; the search highlight is the tooltip's business there.
        await h.ExecuteAsync("toolPalette.toggleView");
        Assert.Equal("icons", h.Services.Settings.Get(SettingsCatalog.ToolPaletteView));
        var cell = h.Find<Control>(Item + "TButton");
        Assert.Equal(new Size(32, 32), cell.Bounds.Size);
        Assert.True(Assert.IsType<PaletteIcon>(cell.GetVisualDescendants().OfType<PaletteIcon>().Single()).IsGeneric); // the test metadata has no icons
        await h.ExecuteAsync("toolPalette.toggleView");
        Assert.Equal(28, h.Find<Control>(Item + "TButton").Bounds.Height);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-040")]
    [Trait("Req", "DSG-TP-041")]
    [Trait("Req", "DSG-TP-044")]
    [Trait("Req", "DSG-TP-054")]
    [Trait("Req", "DSG-TP-060")]
    [Trait("Req", "UI-DSG-123")]
    public async Task ClickArmsThenSurfaceClickPlaces()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var document = await OpenDesignAsync(h);
        var designer = document.Designer;
        var control = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        Point At(double x, double y) => control.TranslatePoint(new Point(x, y), h.Window)!.Value;
        var client = designer.Surface!.Geometry.FormClientRect;

        // A click arms the item: the armed style (bg.selected, accent bar), the status message, the crosshair on the surface.
        h.Click(Item + "TButton");
        Assert.Equal("TButton", designer.Host.ArmedPaletteClass);
        Assert.False(designer.Host.ArmedSticky);
        Assert.Contains("armed", h.Find<Control>(Item + "TButton").Classes);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_PlacementArmed, "TButton"), h.Status.Message);
        h.Window.MouseMove(At(client.Left + 100, client.Top + 150));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(CursorKind.Cross, control.CursorKind);

        // A click on the surface places the button there (grid-snapped, default size), selects it, disarms, focuses the surface.
        h.Window.MouseDown(At(client.Left + 101, client.Top + 149), MouseButton.Left);
        h.Window.MouseUp(At(client.Left + 101, client.Top + 149), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var button = designer.Model!.FindByName("Button1")!;
        Assert.Equal((104, 152, 75, 25), (Int(button, "Left"), Int(button, "Top"), Int(button, "Width"), Int(button, "Height")));
        Assert.Equal(["Button1"], designer.Surface!.Selection.Items.Select(o => o.Name));
        Assert.Null(designer.Host.ArmedPaletteClass);
        Assert.DoesNotContain("armed", h.Find<Control>(Item + "TButton").Classes);
        Assert.True(control.IsKeyboardFocusWithin);
        Assert.Contains("    Button1: TButton;\r\n", document.Buffer.Text, StringComparison.Ordinal);

        // Shift+click arms sticky: the pin glyph; it stays armed after a placement; a click on the item disarms.
        var label = h.Find<Control>(Item + "TLabel");
        var center = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), h.Window)!.Value;
        h.Window.MouseDown(center, MouseButton.Left, RawInputModifiers.Shift);
        h.Window.MouseUp(center, MouseButton.Left, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.True(designer.Host.ArmedSticky);
        Assert.Single(h.Find<Control>(Item + "TLabel").GetVisualDescendants().OfType<Icons.IdeIcon>(), i => i.IconName == "pin");
        h.Window.MouseDown(At(client.Left + 16, client.Top + 200), MouseButton.Left);
        h.Window.MouseUp(At(client.Left + 16, client.Top + 200), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(designer.Model.FindByName("Label1"));
        Assert.Equal("TLabel", designer.Host.ArmedPaletteClass);
        h.Click(Item + "TLabel");
        Assert.Null(designer.Host.ArmedPaletteClass);

        // Escape in the palette disarms.
        h.Click(Item + "TEdit");
        Assert.Equal("TEdit", designer.Host.ArmedPaletteClass);
        h.Press("Escape");
        Assert.Null(designer.Host.ArmedPaletteClass);

        // Palette drag (DSG-TP-060): the ghost of the default size follows, snapped; the release places there.
        var memo = h.Find<Control>(Item + "TMemo");
        var start = memo.TranslatePoint(new Point(memo.Bounds.Width / 2, memo.Bounds.Height / 2), h.Window)!.Value;
        h.Window.MouseDown(start, MouseButton.Left);
        h.Window.MouseMove(start + new Point(0, -10));
        h.Window.MouseMove(At(client.Left + 203, client.Top + 61));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new FreexComp.Designer.Rect(client.Left + 200, client.Top + 64, 185, 89), designer.Surface.PlacementRect);
        h.Window.MouseUp(At(client.Left + 203, client.Top + 61), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var memo1 = designer.Model.FindByName("Memo1")!;
        Assert.Equal((200, 64), (Int(memo1, "Left"), Int(memo1, "Top")));
        Assert.Null(designer.Surface.PlacementRect);
        Assert.Null(designer.Host.ArmedPaletteClass);
        Assert.Equal(["Memo1"], designer.Surface.Selection.Items.Select(o => o.Name));

        // A drag released outside the surface does nothing (another item: a second press on the same spot is a double-click).
        var count = designer.Model.AllObjects().Count();
        label = h.Find<Control>(Item + "TLabel");
        start = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), h.Window)!.Value;
        h.Window.MouseDown(start, MouseButton.Left);
        h.Window.MouseMove(start + new Point(0, -10));
        h.Window.MouseMove(start + new Point(0, -20));
        h.Window.MouseUp(start + new Point(0, -20), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(count, designer.Model.AllObjects().Count());
        Assert.Null(designer.Host.ArmedPaletteClass);
    }

    private static int Int(FormObject obj, string property) => (int)((IntegerValue)PropertyAccess.GetEffective(obj, property)).Value;

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-125")]
    [Trait("Req", "UI-TW-352")]
    [Trait("Req", "DSG-TP-024")]
    public async Task EmptyStateWithoutDesigner()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        await DesignerViewTests.OpenFormAsync(h);
        await h.ExecuteAsync("view.toolPalette");

        // The Code view: the empty state instead of the list, the search box disabled.
        var empty = h.Find<TextBlock>("Tool.ToolPalette.Empty");
        Assert.True(empty.IsEffectivelyVisible);
        Assert.Equal(Strings.Msg_Palette_NoForm, empty.Text);
        Assert.False(h.Find<TextBox>("Tool.ToolPalette.Filter").IsEnabled);
        Assert.Empty(Shown(h));

        // The Design view: the list, enabled.
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        Assert.False(h.Find<TextBlock>("Tool.ToolPalette.Empty").IsEffectivelyVisible);
        Assert.True(h.Find<TextBox>("Tool.ToolPalette.Filter").IsEnabled);
        Assert.Contains("Item.TButton", Shown(h));

        // The Text view: empty again.
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Text"));
        Assert.True(h.Find<TextBlock>("Tool.ToolPalette.Empty").IsEffectivelyVisible);
        Assert.Empty(Shown(h));
    }
}
