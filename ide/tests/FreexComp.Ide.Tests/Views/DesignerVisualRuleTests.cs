using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Designer.Inspector;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// Visual rules of the designer tool windows (ui/designer.md §10 … §12; D-087): info bars, the Object Inspector's tabs,
/// toggles, name and value cells and category rows, the palette tooltip; and the headless user directory (DSG-TEST-005).
/// </summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class DesignerVisualRuleTests
{
    private const string Prefix = "Tool.ObjectInspector";

    private static string Row(string path) => Prefix + ".Row." + path;

    private static ObjectInspectorViewModel Inspector(HeadlessHarness h) => (ObjectInspectorViewModel)h.Find<Control>(Prefix).DataContext!;

    private static async Task<FormDocumentViewModel> OpenDesignAsync(HeadlessHarness h, string select)
    {
        var document = await ToolPaletteViewTests.OpenDesignAsync(h);
        var model = document.Designer.Model!;
        document.Designer.Surface!.Selection.Select(model.FindByName(select)!);
        Dispatcher.UIThread.RunJobs();
        return document;
    }

    private static object Resource(HeadlessHarness h, string key) =>
        h.Window.TryFindResource(key, h.Window.ActualThemeVariant, out var value) && value is not null ? value : throw new InvalidOperationException(key);

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Color Token(HeadlessHarness h, string key) => BrushColor((IBrush)Resource(h, key));

    /// <summary>The name cell (column 0) of a property row.</summary>
    private static StackPanel NameCell(HeadlessHarness h, string path) => (StackPanel)((Grid)h.Find<Border>(Row(path)).Child!).Children[0];

    private static TextBlock NameText(HeadlessHarness h, string path) => (TextBlock)NameCell(h, path).Children[1];

    /// <summary>The value cell (column 2) of a property row.</summary>
    private static Border ValueCell(HeadlessHarness h, string path) => (Border)((Grid)h.Find<Border>(Row(path)).Child!).Children[2];

    private static TextBlock ValueText(HeadlessHarness h, string path) => h.Find<TextBlock>(Row(path) + ".Value");

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-090")]
    public async Task InfoBarUsesTheSeverityBackgroundAndTheBarMetrics()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            ToolPaletteViewTests.Phase5(o);
            o.InitialFiles["MainForm.form"] = DesignerViewTests.FormText.ReplaceLineEndings("\n")
                .Replace("    TabOrder = 0\n", "    TabOrder = 0\n    Shade = 3\n", StringComparison.Ordinal).ReplaceLineEndings("\r\n");
        });
        await ToolPaletteViewTests.OpenDesignAsync(h);

        // A warning bar (one unknown property): status.warning.bg, 1 px border.default at the bottom, the bar height.
        var bar = h.Window.GetVisualDescendants().OfType<InfoBarView>().Single(b => b.IsEffectivelyVisible);
        Assert.Equal(AutomationIds.InfoBar("FormErrors"), AutomationIds.Get(bar));
        Assert.Equal(Token(h, "FxcStatusWarningBgBrush"), BrushColor(bar.Background));
        Assert.Equal(Token(h, "FxcBorderDefaultBrush"), BrushColor(bar.BorderBrush));
        Assert.Equal(new Thickness(0, 0, 0, 1), bar.BorderThickness);
        Assert.Equal(30d, bar.MinHeight);
        Assert.Equal((double)Resource(h, "FxcMetricInfoBarHeight"), bar.MinHeight);
        Assert.True(bar.Bounds.Height >= 30, $"height {bar.Bounds.Height}");

        // Content: a 16 px severity icon, space.4 (8 DIP) before the text in type.body, text.primary.
        var text = h.Find<TextBlock>(AutomationIds.InfoBar("FormErrors") + ".Text");
        var icon = bar.GetVisualDescendants().OfType<Control>().First(c => c.Width == 16);
        Assert.Equal(8d, icon.Margin.Right);
        Assert.True(icon.Bounds.Right <= text.Bounds.Left || icon.TranslatePoint(default, bar)!.Value.X < text.TranslatePoint(default, bar)!.Value.X);
        Assert.Equal(Token(h, "FxcTextPrimaryBrush"), BrushColor(text.Foreground));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-102")]
    public async Task PageTabsAndArrangementTogglesAreStyledAndExclusive()
    {
        await using var h = await HeadlessHarness.StartAsync(ToolPaletteViewTests.Phase5);
        await OpenDesignAsync(h, "GreetButton");
        var properties = h.Find<Button>(Prefix + ".Tab.Properties");
        var events = h.Find<Button>(Prefix + ".Tab.Events");
        var byCategory = h.Find<ToggleButton>(Prefix + ".ToolBar.objectInspector.toggleCategorized:category");
        var byName = h.Find<ToggleButton>(Prefix + ".ToolBar.objectInspector.toggleCategorized:name");

        // The active tab is bodyStrong with the tab.active.indicator border; the other is plain body without one.
        void AssertActive(Button active, Button inactive)
        {
            Assert.Equal(FontWeight.SemiBold, active.FontWeight);
            Assert.Equal(Token(h, "FxcTabActiveIndicatorBrush"), BrushColor(active.BorderBrush));
            Assert.Equal(FontWeight.Normal, inactive.FontWeight);
            Assert.Equal(Colors.Transparent, BrushColor(inactive.BorderBrush));
        }

        AssertActive(properties, events);
        h.Click(Prefix + ".Tab.Events");
        Assert.Equal(InspectorPage.Events, Inspector(h).Page);
        AssertActive(events, properties);
        h.Click(Prefix + ".Tab.Properties");
        AssertActive(properties, events);

        // Two 22 × 22 toggles, exactly one checked, following the arrangement.
        Assert.All([byCategory, byName], t => Assert.Equal((22d, 22d), (t.Width, t.Height)));
        Assert.True(byCategory.IsChecked == true && byName.IsChecked == false);
        h.Click(Prefix + ".ToolBar.objectInspector.toggleCategorized:name");
        Assert.True(Inspector(h).IsArrangedByName);
        Assert.True(byName.IsChecked == true && byCategory.IsChecked == false);
        h.Click(Prefix + ".ToolBar.objectInspector.toggleCategorized:name"); // the checked one stays checked
        Assert.True(byName.IsChecked == true && byCategory.IsChecked == false);
        h.Click(Prefix + ".ToolBar.objectInspector.toggleCategorized:category");
        Assert.False(Inspector(h).IsArrangedByName);
        Assert.True(byCategory.IsChecked == true && byName.IsChecked == false);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-104")]
    [Trait("Req", "UI-DSG-108")]
    public async Task NameCellsIndentByLevelAndCategoryRowsUseTheHeaderStyle()
    {
        await using var h = await HeadlessHarness.StartAsync(ToolPaletteViewTests.Phase5);
        var d = await OpenDesignAsync(h, "GreetButton");
        var inspector = Inspector(h);

        // Level 0: 4 DIP; the expander takes the 16 DIP before the text (blank for a plain row).
        Assert.Equal(4d, NameCell(h, "Caption").Margin.Left);
        var fontExpander = (TextBlock)NameCell(h, "Font").Children[0];
        Assert.Equal(16d, fontExpander.Width);
        Assert.Equal("▸", fontExpander.Text);
        Assert.Equal(16d, ((TextBlock)NameCell(h, "Caption").Children[0]).Width);
        Assert.Equal("", ((TextBlock)NameCell(h, "Caption").Children[0]).Text);

        // Expanded: the nested row is one level deeper (4 + 16); the chevron turns.
        inspector.Expand("Font");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("▾", ((TextBlock)NameCell(h, "Font").Children[0]).Text);
        Assert.Equal(20d, NameCell(h, "Font.Name").Margin.Left);
        Assert.Equal(4d, NameCell(h, "Font").Margin.Left); // category rows are not a level

        // type.body, text.primary; a read-only property in text.secondary.
        Assert.Contains("body", NameText(h, "Caption").Classes);
        Assert.Equal(Token(h, "FxcTextPrimaryBrush"), BrushColor(NameText(h, "Caption").Foreground));
        Assert.Equal(Token(h, "FxcTextPrimaryBrush"), BrushColor(NameText(h, "Name").Foreground));

        // UI-DSG-108: category rows span both columns on bg.header, chevron + name in bodyStrong; a collapsed one stays.
        var categories = h.FindAll(Prefix + ".Category.").OfType<Border>().ToList();
        Assert.NotEmpty(categories);
        foreach (var category in categories)
        {
            Assert.Equal(Token(h, "FxcBgHeaderBrush"), BrushColor(category.Background));
            var text = Assert.IsType<TextBlock>(category.Child);
            Assert.Contains("bodyStrong", text.Classes);
            Assert.StartsWith("▾ ", text.Text, StringComparison.Ordinal);
            Assert.Equal(h.Find<Border>(Row("Caption")).Bounds.Width, category.Bounds.Width);
        }

        var first = AutomationIds.Get(categories[0])![(Prefix + ".Category.").Length..];
        inspector.ToggleCategory(first);
        Dispatcher.UIThread.RunJobs();
        var collapsed = h.Find<Border>(Prefix + ".Category." + first);
        Assert.StartsWith("▸ " + first, ((TextBlock)collapsed.Child!).Text, StringComparison.Ordinal);
        Assert.Equal(Token(h, "FxcBgHeaderBrush"), BrushColor(collapsed.Background));

        // Name is read-only in a multi-selection (DSG-OI-036): its name in text.secondary.
        var model = d.Designer.Model!;
        d.Designer.Surface!.Selection.Select([model.FindByName("GreetButton")!, model.FindByName("NameEdit")!]);
        Dispatcher.UIThread.RunJobs();
        Assert.True(inspector.Rows.Single(r => r.Path == "Name").IsReadOnly);
        Assert.Equal(Token(h, "FxcTextSecondaryBrush"), BrushColor(NameText(h, "Name").Foreground));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-105")]
    public async Task ValueCellsShowStoredValuesStrongAndMixedValuesBlank()
    {
        await using var h = await HeadlessHarness.StartAsync(ToolPaletteViewTests.Phase5);
        var d = await OpenDesignAsync(h, "GreetButton");

        // Padding 4; a stored value (differs from the default) in bodyStrong, a default one in body.
        Assert.Equal(4d, ValueCell(h, "Caption").Padding.Left);
        Assert.Equal(FontWeight.SemiBold, ValueText(h, "Caption").FontWeight);
        Assert.Equal(FontWeight.Normal, ValueText(h, "Cursor").FontWeight);
        Assert.Equal(Token(h, "FxcTextPrimaryBrush"), BrushColor(ValueText(h, "Caption").Foreground));

        // A multi-selection with differing values shows the cell blank; equal values are shown.
        var model = d.Designer.Model!;
        d.Designer.Surface!.Selection.Select([model.FindByName("GreetButton")!, model.FindByName("NameEdit")!]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, d.Designer.Surface.Selection.Items.Count);
        Assert.Equal("", ValueText(h, "Left").Text);
        Assert.Equal("", ValueText(h, "TabOrder").Text);
        Assert.Equal("True", ValueText(h, "Visible").Text);
        Assert.Equal(Token(h, "FxcTextSecondaryBrush"), BrushColor(ValueText(h, "Name").Foreground)); // read-only here
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-126")]
    public async Task PaletteItemTooltipShowsClassAndUnitAfterTheDelay()
    {
        await using var h = await HeadlessHarness.StartAsync(ToolPaletteViewTests.Phase5);
        await ToolPaletteViewTests.OpenDesignAsync(h);
        var item = h.Find<Control>("Tool.ToolPalette.Item.TButton");

        Assert.Equal(600, ToolTip.GetShowDelay(item));
        var tip = Assert.IsType<StackPanel>(ToolTip.GetTip(item));
        Assert.Equal(2, tip.Children.Count);
        var name = Assert.IsType<TextBlock>(tip.Children[0]);
        var unit = Assert.IsType<TextBlock>(tip.Children[1]);
        Assert.Equal("TButton", name.Text);
        Assert.Contains("bodyStrong", name.Classes);
        Assert.Equal("unit StdCtrls", unit.Text);
        Assert.Contains("caption", unit.Classes);

        // Shown: the unit line resolves text.secondary.
        ToolTip.SetIsOpen(item, true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Token(h, "FxcTextSecondaryBrush"), BrushColor(unit.Foreground));
        ToolTip.SetIsOpen(item, false);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-030")]
    public async Task SelectingFromOutsideScrollsThePrimaryIntoView()
    {
        // A form wider and taller than the view, with a button near its bottom-right corner.
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            ToolPaletteViewTests.Phase5(o);
            o.InitialFiles["MainForm.pas"] = DesignerViewTests.Unit.Replace("GreetButton: TButton;", "GreetButton: TButton;\n    FarButton: TButton;", StringComparison.Ordinal)
                .ReplaceLineEndings("\r\n");
            var form = DesignerViewTests.FormText.ReplaceLineEndings("\n").Replace("  ClientWidth = 400\n  ClientHeight = 300\n", "  ClientWidth = 2400\n  ClientHeight = 1600\n", StringComparison.Ordinal);
            var at = form.TrimEnd().LastIndexOf("end", StringComparison.Ordinal);
            o.InitialFiles["MainForm.form"] = (form[..at]
                + "  object FarButton: TButton\n    Left = 2300\n    Top = 1500\n    Width = 75\n    Height = 25\n    TabOrder = 2\n  end\n" + form[at..]).ReplaceLineEndings("\r\n");
        });
        var d = await OpenDesignAsync(h, "NameEdit");
        var surface = d.Designer.Surface!;
        var scroller = h.Find<Control>(AutomationIds.Designer("MainForm")).FindAncestorOfType<ScrollViewer>()!;
        Assert.Equal(new Vector(0, 0), scroller.Offset);
        var far = surface.Geometry.GetBounds(d.Designer.Model!.FindByName("FarButton")!);
        var expected = new Vector(far.Left + far.Width + 8 - scroller.Viewport.Width, far.Top + far.Height + 8 - scroller.Viewport.Height);
        Assert.True(expected.X > 0 && expected.Y > 0, $"view {scroller.Viewport}");

        // The Object Inspector's instance selector: the minimum scroll that shows the button with an 8 DIP margin.
        var inspector = Inspector(h);
        inspector.SelectComponent(inspector.ComponentItems.ToList().IndexOf("FarButton: TButton"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["FarButton"], surface.Selection.Items.Select(o => o.Name));
        Assert.Equal(expected, scroller.Offset);

        // Back at NameEdit (top-left, outside the view now): its top-left minus the margin, clamped at 0.
        inspector.SelectComponent(inspector.ComponentItems.ToList().IndexOf("NameEdit: TEdit"));
        Dispatcher.UIThread.RunJobs();
        var edit = surface.Geometry.GetBounds(d.Designer.Model!.FindByName("NameEdit")!);
        var home = new Vector(Math.Max(0, edit.Left - 8), Math.Max(0, edit.Top - 8));
        Assert.Equal(home, scroller.Offset);

        // The Structure tool window selects from outside too.
        var structure = Assert.IsType<StructureViewModel>(h.Services.ToolWindows.Get(StructureViewModel.ToolName));
        structure.SelectRows([structure.Rows.Single(r => r.Node.Key == "Form/FarButton")]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expected, scroller.Offset);

        // A primary already visible with the margin does not scroll.
        scroller.Offset = new Vector(0, 0);
        Dispatcher.UIThread.RunJobs();
        inspector.SelectComponent(inspector.ComponentItems.ToList().IndexOf("GreetButton: TButton"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["GreetButton"], surface.Selection.Items.Select(o => o.Name));
        Assert.Equal(new Vector(0, 0), scroller.Offset);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-083")]
    public async Task TrayTooltipOffersTheComponentEditor()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            DesignerViewTests.EditorFiles(o);
            o.ToolWindows.Clear();
        });
        var d = await ToolPaletteViewTests.OpenDesignAsync(h);
        var control = h.Find<DesignSurfaceControl>(AutomationIds.Designer("MainForm"));
        var g = d.Designer.Surface!.Geometry;
        void MoveTo(string name)
        {
            var tile = g.GetTrayTile(d.Designer.Model!.FindByName(name)!);
            h.Window.MouseMove(control.TranslatePoint(new Point(tile.Left + 16, tile.Top + 16), h.Window)!.Value);
            Dispatcher.UIThread.RunJobs();
        }

        IReadOnlyList<string?> Lines() =>
            ToolTip.GetTip(control) is StackPanel panel ? [.. panel.Children.OfType<TextBlock>().Select(t => t.Text)] : [];

        // The menu (componentEditor "menu"): the class name, then "Double-click to edit".
        MoveTo("MainMenu1");
        Assert.Equal(["TMainMenu", Strings.Designer_TrayEditHint], Lines());
        Assert.Equal("Double-click to edit", Strings.Designer_TrayEditHint);
        Assert.Contains("bodyStrong", ((StackPanel)ToolTip.GetTip(control)!).Children[0].Classes);

        // The action list (componentEditor "childlist") likewise; over the form: no tooltip.
        MoveTo("ActionList1");
        Assert.Equal(["TActionList", Strings.Designer_TrayEditHint], Lines());
        var frame = g.FrameRect;
        h.Window.MouseMove(control.TranslatePoint(new Point(frame.Left + 100, frame.Top + 120), h.Window)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(ToolTip.GetTip(control));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "DSG-TEST-005")]
    public async Task HeadlessTestsWriteSettingsOnlyToTheTempUserDirectory()
    {
        await using var h = await HeadlessHarness.StartAsync(ToolPaletteViewTests.Phase5);
        var d = await OpenDesignAsync(h, "GreetButton");

        // The harness user directory is a fresh folder under the temp path, and the IDE's paths point into it.
        var temp = Path.GetFullPath(Path.GetTempPath());
        Assert.StartsWith(temp, h.UserDir, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(h.UserDir, h.Services.UserPaths.RoamingDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(h.UserDir, h.Services.UserPaths.LocalDirectory, StringComparison.OrdinalIgnoreCase);

        // A designer setting changed and flushed lands in that folder; nothing is written outside the test root.
        Assert.IsType<SettingsDesignerSettings>(d.Designer.Settings);
        h.Services.Settings.Set(SettingsCatalog.DesignerGridSizeX, 4);
        await h.Services.Settings.FlushAsync();
        Assert.Equal(4, d.Designer.Settings.GridSizeX);
        var writes = h.FileSystem.Calls.Where(c => c.StartsWith("Write", StringComparison.Ordinal) || c.StartsWith("Overwrite", StringComparison.Ordinal)
            || c.StartsWith("Replace", StringComparison.Ordinal) || c.StartsWith("Move", StringComparison.Ordinal)).ToList();
        Assert.Contains(writes, c => c.Contains(Path.Combine(h.UserDir, "roaming"), StringComparison.OrdinalIgnoreCase));
        Assert.All(writes, c => Assert.Contains(h.Root, c, StringComparison.OrdinalIgnoreCase));
    }
}
