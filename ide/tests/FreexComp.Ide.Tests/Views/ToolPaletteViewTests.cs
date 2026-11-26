using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
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
    [Trait("Req", "UI-TW-350")]
    [Trait("Req", "UI-TW-353")]
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
        Assert.Equal(["Page.Standard", "Item.Frames", "Item.TMainMenu", "Item.TPopupMenu", "Item.TLabel", "Item.TEdit", "Item.TMemo",
            "Item.TButton", "Item.TCheckBox", "Item.TRadioButton", "Item.TListBox", "Item.TComboBox", "Item.TGroupBox", "Item.TPanel"], Shown(h).Take(14));
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

    private static IEnumerable<Avalonia.Automation.Peers.AutomationPeer> Descendants(Avalonia.Automation.Peers.AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            yield return child;
            foreach (var below in Descendants(child))
                yield return below;
        }
    }

    /// <summary>
    /// I-213 (smoke launch): palette pages and items are UI Automation control elements carrying their AutomationIds and
    /// names (UI-A11Y-020, UI-A11Y-030, UI-DSG-121/122), reachable from the list, so an external UIA client finds them.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-030")]
    [Trait("Req", "UI-A11Y-020")]
    public async Task ItemsHaveAutomationPeers()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        await OpenDesignAsync(h);
        await h.ExecuteAsync("view.toolPalette");
        var list = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(h.Find<Control>("Tool.ToolPalette.List"));
        Assert.True(list.IsControlElement());
        Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.List, list.GetAutomationControlType());
        Assert.Equal("Tool.ToolPalette.List", list.GetAutomationId());

        var shown = Shown(h);
        Assert.Contains("Item.TButton", shown);
        var peers = Descendants(list).Where(p => p.IsControlElement() && !string.IsNullOrEmpty(p.GetAutomationId()))
            .ToDictionary(p => p.GetAutomationId()!, StringComparer.Ordinal); // ids are unique (UI-A11Y-031)
        foreach (var key in shown)
        {
            Assert.True(peers.TryGetValue("Tool.ToolPalette." + key, out var peer), $"no control element for {key}");
            var isItem = key.StartsWith("Item.", StringComparison.Ordinal);
            Assert.Equal(isItem ? Avalonia.Automation.Peers.AutomationControlType.ListItem : Avalonia.Automation.Peers.AutomationControlType.Group,
                peer.GetAutomationControlType());
            Assert.Equal(key[(key.IndexOf('.', StringComparison.Ordinal) + 1)..], peer.GetName());
        }
    }

    /// <summary>
    /// I-219: a Tool Palette item supports the UI Automation Invoke pattern; invoking it is a click — it arms the class
    /// for placement, and invoking the armed item disarms it.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-030")]
    public async Task ItemsSupportInvoke()
    {
        await using var h = await HeadlessHarness.StartAsync(Phase5);
        var document = await OpenDesignAsync(h);
        await h.ExecuteAsync("view.toolPalette");
        Avalonia.Automation.Provider.IInvokeProvider Invoker() =>
            Assert.IsAssignableFrom<Avalonia.Automation.Provider.IInvokeProvider>(
                Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(h.Find<Control>(Item + "TButton"))
                    .GetProvider<Avalonia.Automation.Provider.IInvokeProvider>());

        Invoker().Invoke();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("TButton", document.Designer.Host.ArmedPaletteClass);
        Assert.Contains("armed", h.Find<Control>(Item + "TButton").Classes);

        Invoker().Invoke();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(document.Designer.Host.ArmedPaletteClass);
    }

    // ---- frames (DSG-TP-061…065, UI-DSG-137, 138; D-120) ----

    private const string Picker = "Tool.ToolPalette.FramePicker";

    private static readonly Lazy<FreexComp.Designer.Catalog.ComponentCatalog> FrameMetadata = new(() =>
        FreexComp.Designer.Catalog.CatalogLoader.Load([Path.Combine(AppContext.BaseDirectory, "TestData", "metadata", "index.json")]).Catalog);

    private static string FrameUnit(string name, string uses, params string[] fields) => string.Join("\r\n",
        [$"unit {name}Unit;", "", "interface", "", "uses", $"  Classes, Controls, Forms{uses};", "", "type", $"  T{name} = class(TFrame)",
            .. fields.Select(f => "    " + f), "  end;", "", "implementation", "", $"{{$FORM {name}Unit.form}}", "", "initialization",
            $"  RegisterClass(T{name});", "", "end.", ""]);

    /// <summary>A gui project P with a main form and, with <paramref name="frames"/>, three frame units (TOuterFrame holds a TAddressFrame).</summary>
    private static Action<HarnessOptions> FrameProject(bool frames) => o =>
    {
        var files = "{ \"path\": \"P.pas\" }, { \"path\": \"MainForm.pas\", \"form\": \"MainForm.form\" }";
        if (frames)
        {
            files += ", { \"path\": \"AddressFrameUnit.pas\", \"form\": \"AddressFrameUnit.form\" }"
                + ", { \"path\": \"FooterFrameUnit.pas\", \"form\": \"FooterFrameUnit.form\" }"
                + ", { \"path\": \"OuterFrameUnit.pas\", \"form\": \"OuterFrameUnit.form\" }";
            o.InitialFiles[@"P\AddressFrameUnit.pas"] = FrameUnit("AddressFrame", ", StdCtrls", "StreetLabel: TLabel;");
            o.InitialFiles[@"P\AddressFrameUnit.form"] = "object AddressFrame: TAddressFrame\n  Left = 0\n  Top = 0\n  Width = 320\n  Height = 120\n"
                + "  object StreetLabel: TLabel\n    Left = 8\n    Top = 8\n    Caption = 'Street'\n  end\nend\n";
            o.InitialFiles[@"P\FooterFrameUnit.pas"] = FrameUnit("FooterFrame", "");
            o.InitialFiles[@"P\FooterFrameUnit.form"] = "object FooterFrame: TFooterFrame\n  Left = 0\n  Top = 0\n  Width = 300\n  Height = 40\nend\n";
            o.InitialFiles[@"P\OuterFrameUnit.pas"] = FrameUnit("OuterFrame", ", AddressFrameUnit", "Address1: TAddressFrame;");
            o.InitialFiles[@"P\OuterFrameUnit.form"] = "object OuterFrame: TOuterFrame\n  Left = 0\n  Top = 0\n  Width = 340\n  Height = 140\n"
                + "  inline Address1: TAddressFrame\n    Left = 8\n    Top = 8\n    Width = 320\n    Height = 120\n  end\nend\n";
        }

        o.InitialFiles[@"P\P.pproj"] = "{ \"formatVersion\": 1, \"name\": \"P\", \"appType\": \"gui\", \"mainSource\": \"P.pas\", \"files\": [ " + files + " ] }";
        o.InitialFiles[@"P\P.pas"] = "program P;\r\n\r\nuses\r\n  Forms, MainForm;\r\n\r\nbegin\r\nend.\r\n";
        o.InitialFiles[@"P\MainForm.pas"] = string.Join("\r\n", "unit MainForm;", "", "interface", "", "uses", "  Classes, Controls, Forms;", "", "type",
            "  TMainForm = class(TForm)", "  end;", "", "var", "  MainForm: TMainForm;", "", "implementation", "", "{$FORM MainForm.form}", "", "end.", "");
        o.InitialFiles[@"P\MainForm.form"] = "object MainForm: TMainForm\n  Left = 200\n  Top = 120\n  Caption = 'Main'\n  ClientWidth = 640\n"
            + "  ClientHeight = 400\n  PixelsPerInch = 96\nend\n";
    };

    /// <summary>Opens the project (once) and <paramref name="unit"/> in the Design view with the Tool Palette shown.</summary>
    private static async Task<FormDocumentViewModel> OpenFrameDesignAsync(HeadlessHarness h, string unit, bool project = true)
    {
        ((DocumentService)h.Services.Documents).DesignCatalog = FrameMetadata.Value;
        if (project)
        {
            Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "P", "P.pproj")));
        }

        var document = Assert.IsType<FormDocumentViewModel>((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "P", unit))).Value);
        Dispatcher.UIThread.RunJobs();
        h.Click(AutomationIds.DocumentViewTab(unit, "Design"));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        await h.ExecuteAsync("view.toolPalette");
        return document;
    }

    /// <summary>The picker's frame rows, their class names in order.</summary>
    private static List<string> PickerRows(HeadlessHarness h) =>
        [.. h.FindAll(Picker + ".").Distinct().Select(c => AutomationIds.Get(c)![(Picker.Length + 1)..]).Where(id => id != "Empty")]; // the overlay layer lists the popup content twice

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-061")]
    [Trait("Req", "DSG-TP-062")]
    [Trait("Req", "DSG-TP-063")]
    [Trait("Req", "DSG-TP-064")]
    [Trait("Req", "DSG-TP-065")]
    [Trait("Req", "DSG-TP-066")]
    [Trait("Req", "DSG-CS-112")]
    [Trait("Req", "UI-DSG-137")]
    [Trait("Req", "UI-DSG-138")]
    public async Task FramesItem_PickerListsFramesWithoutCycles_PlacesInstanceWithFieldAndUses()
    {
        await using var h = await HeadlessHarness.StartAsync(FrameProject(frames: true));
        var document = await OpenFrameDesignAsync(h, "MainForm.pas");
        var designer = document.Designer;

        // UI-DSG-137: the first item of page Standard, the IDE icon frame, the text "Frames…", its own tooltip.
        Assert.Equal(["Page.Standard", "Item.Frames", "Item.TMainMenu"], Shown(h).Take(3));
        var item = h.Find<Control>(Item + "Frames");
        Assert.Equal(Strings.Palette_FramesItem, Name(h, "Frames"));
        Assert.Single(item.GetVisualDescendants().OfType<Icons.IdeIcon>(), i => i.IconName == "frame");
        Assert.Equal(Strings.Palette_FramesItemTip, ToolTip.GetTip(item));
        Assert.DoesNotContain("disabled", item.Classes);

        // DSG-TP-063: a click opens the picker under the item: the project's frames sorted by class, name and unit; nothing armed.
        h.Click(Item + "Frames");
        Assert.True(h.Find<Control>(Picker).IsEffectivelyVisible);
        Assert.Equal(["TAddressFrame", "TFooterFrame", "TOuterFrame"], PickerRows(h));
        Assert.Equal(["FooterFrame", "FooterFrameUnit"],
            h.Find<Control>(Picker + ".TFooterFrame").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Null(designer.Host.ArmedPaletteClass);

        // DSG-TP-065: Escape closes the picker without arming.
        h.Press("Escape");
        Assert.Null(h.TryFind<Control>(Picker));
        Assert.Null(designer.Host.ArmedPaletteClass);

        // UI-DSG-138: Down moves the highlight, Enter chooses; DSG-TP-064: the frame is armed single-shot.
        h.Click(Item + "Frames");
        h.Press("Down");
        Assert.Contains("current", h.Find<Control>(Picker + ".TFooterFrame").Classes);
        h.Press("Enter");
        Assert.Null(h.TryFind<Control>(Picker));
        Assert.Equal("TFooterFrame", designer.Host.ArmedPaletteClass);
        Assert.False(designer.Host.ArmedSticky);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_PlacementArmed, "TFooterFrame"), h.Status.Message);

        // A click on the surface places a resolved instance of the frame's size; DSG-CS-112: a field and the frame's unit in uses.
        var control = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        var client = designer.Surface!.Geometry.FormClientRect;
        var at = control.TranslatePoint(new Point(client.Left + 16, client.Top + 200), h.Window)!.Value;
        h.Window.MouseDown(at, MouseButton.Left);
        h.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var instance = designer.Model!.FindByName("FooterFrame1")!;
        Assert.Equal((FormObjectKind.Inline, "TFooterFrame"), (instance.Kind, instance.TypeName));
        Assert.Equal((16, 200, 300, 40), (Int(instance, "Left"), Int(instance, "Top"), Int(instance, "Width"), Int(instance, "Height")));
        Assert.Null(designer.Host.ArmedPaletteClass);
        Assert.Contains("    FooterFrame1: TFooterFrame;\r\n", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("  Classes, Controls, Forms, FooterFrameUnit;\r\n", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("  inline FooterFrame1: TFooterFrame\n", designer.CurrentFormText(), StringComparison.Ordinal);

        // DSG-TP-062: designing a frame, the picker leaves out the frame itself and every frame that contains it.
        await OpenFrameDesignAsync(h, "AddressFrameUnit.pas", project: false);
        h.Click(Item + "Frames");
        Assert.Equal(["TFooterFrame"], PickerRows(h));
        h.Press("Escape");
        await OpenFrameDesignAsync(h, "OuterFrameUnit.pas", project: false);
        h.Click(Item + "Frames");
        Assert.Equal(["TAddressFrame", "TFooterFrame"], PickerRows(h));

        // DSG-TP-065: another view closes the picker.
        h.Click(AutomationIds.DocumentViewTab("OuterFrameUnit.pas", "Code"));
        Assert.Null(h.TryFind<Control>(Picker));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-061")]
    [Trait("Req", "DSG-TP-063")]
    [Trait("Req", "UI-DSG-138")]
    public async Task FramesItem_NoFrames_Placeholder()
    {
        // Without a project the item is shown disabled and opens nothing (DSG-TP-061).
        await using (var none = await HeadlessHarness.StartAsync(FrameProject(frames: false)))
        {
            await OpenFrameDesignAsync(none, "MainForm.pas", project: false);
            Assert.Contains("disabled", none.Find<Control>(Item + "Frames").Classes);
            none.Click(Item + "Frames");
            Assert.Null(none.TryFind<Control>(Picker));
        }

        await using var h = await HeadlessHarness.StartAsync(FrameProject(frames: false));
        var document = await OpenFrameDesignAsync(h, "MainForm.pas");
        Assert.DoesNotContain("disabled", h.Find<Control>(Item + "Frames").Classes);

        // A project without frames: the picker shows Msg.Palette.NoFrames as its only row; Enter chooses nothing.
        h.Click(Item + "Frames");
        Assert.True(h.Find<Control>(Picker).IsEffectivelyVisible);
        Assert.Empty(PickerRows(h));
        var empty = h.Find<TextBlock>(Picker + ".Empty");
        Assert.Equal(Strings.Msg_Palette_NoFrames, empty.Text);
        h.Press("Enter");
        Assert.Null(document.Designer.Host.ArmedPaletteClass);
        h.Press("Escape");
        Assert.Null(h.TryFind<Control>(Picker));
        Assert.Null(document.Designer.Host.ArmedPaletteClass);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-064")]
    [Trait("Req", "UI-DSG-138")]
    public async Task FramesItem_FirstClickAfterPickerPlaces()
    {
        await using var h = await HeadlessHarness.StartAsync(FrameProject(frames: true));
        var document = await OpenFrameDesignAsync(h, "MainForm.pas");
        var designer = document.Designer;

        // D-123: the frame is chosen with the mouse as in the real IDE: move onto the row, press (chooses and closes the
        // picker), release where the picker was.
        h.Click(Item + "Frames");
        var row = PointIn(h, h.Find<Control>(Picker + ".TFooterFrame"), 0.5, 0.5);
        h.Window.MouseMove(row);
        Dispatcher.UIThread.RunJobs();
        h.Window.MouseDown(row, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<Control>(Picker));
        Assert.Equal("TFooterFrame", designer.Host.ArmedPaletteClass);
        h.Window.MouseUp(row, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_PlacementArmed, "TFooterFrame"), h.Status.Message);

        // One click on the form places the instance (DSG-TP-064 single-shot).
        var control = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        var client = designer.Surface!.Geometry.FormClientRect;
        var at = control.TranslatePoint(new Point(client.Left + 24, client.Top + 160), h.Window)!.Value;
        h.Window.MouseMove(at);
        Dispatcher.UIThread.RunJobs();
        h.Window.MouseDown(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        h.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var instance = designer.Model!.FindByName("FooterFrame1");
        Assert.NotNull(instance);
        Assert.Equal((24, 160), (Int(instance, "Left"), Int(instance, "Top")));
        Assert.Null(designer.Host.ArmedPaletteClass);
        Assert.NotEqual(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_PlacementArmed, "TFooterFrame"), h.Status.Message);

        // The same through the search box (DSG-TP-033: Enter on the Frames item opens the picker, the search stays).
        h.Click("Tool.ToolPalette.Filter");
        h.Type("Fram");
        h.Press("Enter");
        Assert.Equal(["TAddressFrame", "TFooterFrame", "TOuterFrame"], PickerRows(h));
        row = PointIn(h, h.Find<Control>(Picker + ".TAddressFrame"), 0.5, 0.5);
        h.Window.MouseMove(row);
        Dispatcher.UIThread.RunJobs();
        h.Window.MouseDown(row, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        h.Window.MouseUp(row, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("TAddressFrame", designer.Host.ArmedPaletteClass);
        at = control.TranslatePoint(new Point(client.Left + 300, client.Top + 40), h.Window)!.Value;
        h.Window.MouseMove(at);
        Dispatcher.UIThread.RunJobs();
        h.Window.MouseDown(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        h.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(designer.Model!.FindByName("AddressFrame1"));
        Assert.Null(designer.Host.ArmedPaletteClass);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-063")]
    [Trait("Req", "DSG-TP-065")]
    public async Task FramesItem_OpeningPickerDisarmsArmedClass()
    {
        await using var h = await HeadlessHarness.StartAsync(FrameProject(frames: true));
        var document = await OpenFrameDesignAsync(h, "MainForm.pas");
        var designer = document.Designer;
        var control = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        var client = designer.Surface!.Geometry.FormClientRect;
        void AssertIdle()
        {
            Assert.Null(designer.Host.ArmedPaletteClass);
            Assert.DoesNotContain(h.FindAll(Item), c => c.Classes.Contains("armed"));
            Assert.True(string.IsNullOrEmpty(h.Status.Message), h.Status.Message);
        }

        // D-123: opening the picker disarms the armed class (the arrow state, the idle status) like choosing any other item.
        h.Click(Item + "TLabel");
        Assert.Equal("TLabel", designer.Host.ArmedPaletteClass);
        h.Click(Item + "Frames");
        Assert.True(h.Find<Control>(Picker).IsEffectivelyVisible);
        AssertIdle();

        // A click on the form while the picker is open only closes it (light dismiss, consumed): nothing is placed.
        var at = control.TranslatePoint(new Point(client.Left + 40, client.Top + 120), h.Window)!.Value;
        h.Window.MouseMove(at);
        Dispatcher.UIThread.RunJobs();
        h.Window.MouseDown(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        h.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<Control>(Picker));
        Assert.Null(designer.Model!.FindByName("Label1"));
        Assert.Empty(designer.Model.Root.Children);
        AssertIdle();

        // Escape closes the picker with nothing armed.
        h.Click(Item + "TLabel");
        h.Click(Item + "Frames");
        h.Press("Escape");
        Assert.Null(h.TryFind<Control>(Picker));
        AssertIdle();

        // Choosing a frame arms that frame.
        h.Click(Item + "TLabel");
        h.Click(Item + "Frames");
        h.Press("Enter");
        Assert.Equal("TAddressFrame", designer.Host.ArmedPaletteClass);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_PlacementArmed, "TAddressFrame"), h.Status.Message);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-138")]
    public async Task FramesItem_PickerTypeToJump()
    {
        await using var h = await HeadlessHarness.StartAsync(FrameProject(frames: true));
        var document = await OpenFrameDesignAsync(h, "MainForm.pas");
        h.Click(Item + "Frames");
        string Current() => Assert.Single(PickerRows(h), r => h.Find<Control>(Picker + "." + r).Classes.Contains("current"));
        Assert.Equal("TAddressFrame", Current());

        // UI-DSG-138 type-ahead on the shown name (the class without its T), either case; the next row after the
        // highlighted one, wrapping; a character no name starts with changes nothing.
        h.Type("o");
        Assert.Equal("TOuterFrame", Current());
        h.Type("F");
        Assert.Equal("TFooterFrame", Current());
        h.Type("a");
        Assert.Equal("TAddressFrame", Current());
        h.Type("t");
        Assert.Equal("TAddressFrame", Current());
        h.Type("f");
        Assert.Equal("TFooterFrame", Current());
        h.Type("f");
        Assert.Equal("TFooterFrame", Current());
        Assert.Null(document.Designer.Host.ArmedPaletteClass);

        // Enter chooses the row type-ahead reached.
        h.Press("Enter");
        Assert.Null(h.TryFind<Control>(Picker));
        Assert.Equal("TFooterFrame", document.Designer.Host.ArmedPaletteClass);
    }

    private static Point PointIn(HeadlessHarness h, Control control, double fx, double fy) =>
        control.TranslatePoint(new Point(control.Bounds.Width * fx, control.Bounds.Height * fy), h.Window)!.Value;
}
