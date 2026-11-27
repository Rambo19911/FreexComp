using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using static FreexComp.Ide.Core.Tests.Designer.DesignerFixture;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// The tab control at design time (design-renderers.md DSG-RND-023 … 025; D-148): a tab click writes <c>TabIndex</c>, a
/// <c>Tabs</c> edit keeps <c>TabIndex</c> valid in the same undo unit, the palette item comes from metadata alone.
/// </summary>
[Trait("Phase", "8")]
public sealed class TabControlDesignTests
{
    private static readonly string TabUnit = Unit
        .Replace("  Classes, Controls, Forms, StdCtrls, ExtCtrls;", "  Classes, Controls, Forms, StdCtrls, ExtCtrls, ComCtrls;", StringComparison.Ordinal)
        .Replace("    Panel1: TPanel;", "    Panel1: TPanel;\r\n    Strip: TTabControl;\r\n    Inner: TPanel;", StringComparison.Ordinal);

    private static string TabForm(string stripProperties)
    {
        var at = Form.TrimEnd().LastIndexOf("end", StringComparison.Ordinal);
        return Form[..at] + "  object Strip: TTabControl\n    Left = 8\n    Top = 150\n    Width = 289\n    Height = 140\n"
            + string.Concat(stripProperties.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => "    " + l + "\n"))
            + "    TabOrder = 4\n    object Inner: TPanel\n      Left = 4\n      Top = 25\n      Width = 281\n      Height = 111\n"
            + "      Align = alClient\n      TabOrder = 0\n    end\n  end\n" + Form[at..];
    }

    private const string ThreeTabs = "TabIndex = 0\nTabs = (\n  'A'\n  'B'\n  'C'\n)";

    /// <summary>
    /// DSG-RND-023: a click on tab <c>i</c> of a tab control writes <c>TabIndex = i</c> in one undo unit "Property" — at
    /// every position — and leaves the selection as it was (no child, not the control); a click on the selected tab changes
    /// nothing; undo restores the index.
    /// </summary>
    [Theory(Timeout = 30000)]
    [InlineData("tpTop")]
    [InlineData("tpBottom")]
    [InlineData("tpLeft")]
    [InlineData("tpRight")]
    [Trait("Req", "DSG-RND-023")]
    public Task ClickOnTabStoresTabIndex(string position) => TestUiThread.RunAsync(async () =>
    {
        var f = new DesignerFixture();
        var designer = (await f.OpenAsync(TabUnit, TabForm(ThreeTabs + "\nTabPosition = " + position))).Designer;
        var surface = designer.Surface!;
        var strip = Object(designer, "Strip");
        void ClickTab(int index)
        {
            var ctx = RenderContext.FromLayout(strip, surface.Layout);
            var tab = TabControlRenderer.Tabs(ctx, surface.MeasureCanvas).Single(t => t.Index == index).Tab;
            var b = surface.Geometry.GetBounds(strip);
            var (x, y) = (b.Left + tab.Left + tab.Width / 2, b.Top + tab.Top + tab.Height / 2);
            surface.OnPointer(new PointerInput(PointerKind.Down, x, y, PointerButton.Left, InputModifiers.None));
            surface.OnPointer(new PointerInput(PointerKind.Up, x, y, PointerButton.Left, InputModifiers.None));
        }

        Select(designer, "NameEdit");
        ClickTab(2);
        Assert.Equal("2", Stored(designer, "Strip", "TabIndex"));
        Assert.Equal((1, UndoTitles.Property), (designer.Undo.UndoCount, designer.Undo.UndoTitleKey));
        Assert.Equal(["NameEdit"], surface.Selection.Items.Select(o => o.Name));
        Assert.Equal(2, RenderContext.FromLayout(strip, surface.Layout).TabStrip!.SelectedTab);

        // The selected tab: no unit. Another one: a second unit; the strip itself was never selected.
        ClickTab(2);
        Assert.Equal(1, designer.Undo.UndoCount);
        ClickTab(1);
        Assert.Equal(("1", 2), (Stored(designer, "Strip", "TabIndex"), designer.Undo.UndoCount));
        Assert.Equal(["NameEdit"], surface.Selection.Items.Select(o => o.Name));

        surface.Execute("edit.undo");
        Assert.Equal("2", Stored(designer, "Strip", "TabIndex"));
        surface.Execute("edit.undo");
        Assert.Equal("0", Stored(designer, "Strip", "TabIndex"));
    });

    /// <summary>
    /// DSG-RND-024: the strings-editor commit of <c>Tabs</c> writes the <c>TabIndex</c> change in the same undo unit — the
    /// auto-select of a first list (no <c>TabIndex</c> line), <c>count − 1</c> for an index beyond the new list, the line
    /// removed with an emptied list; any other edit leaves <c>TabIndex</c> as it is. One undo restores both lines.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-RND-024")]
    public Task EditingTabsKeepsTabIndexValid() => TestUiThread.RunAsync(async () =>
    {
        var dialogs = new FakeDialogService();
        var f = new DesignerFixture(o => o.Dialogs = dialogs);
        var designer = (await f.OpenAsync(TabUnit, TabForm(""))).Designer;
        var inspector = new ObjectInspectorViewModel(f.Services.Status, f.Services.Settings, dialogs);
        inspector.Bind(designer);
        Select(designer, "Strip");
        async Task EditTabs(string text)
        {
            dialogs.Script(StringsEditorViewModel.DialogName, d =>
            {
                var strings = Assert.IsType<StringsEditorViewModel>(d);
                strings.Text = text;
                return strings.PressDefaultAsync();
            });
            inspector.Grid.SetCurrent("Tabs");
            await inspector.EditStringsAsync();
        }

        string Tabs() => Object(designer, "Strip").FindProperty("Tabs")?.Value is StringListValue l ? string.Join(",", l.Lines) : "";

        // A first list without a TabIndex line: TabIndex = 0 in the same unit.
        Assert.Null(Stored(designer, "Strip", "TabIndex"));
        await EditTabs("A\r\nB\r\nC\r\nD");
        Assert.Equal(("A,B,C,D", "0", 1), (Tabs(), Stored(designer, "Strip", "TabIndex"), designer.Undo.UndoCount));

        // An index inside the new list is kept.
        designer.Surface!.Commit(UndoTitles.Property, [new FreexComp.Designer.Editing.SetProperty("Strip", "TabIndex", new IntegerValue(3, false))]);
        await EditTabs("A\r\nB\r\nC\r\nD\r\nE");
        Assert.Equal(("3", 3), (Stored(designer, "Strip", "TabIndex"), designer.Undo.UndoCount));

        // Beyond the new list: count − 1.
        await EditTabs("A\r\nB");
        Assert.Equal(("A,B", "1", 4), (Tabs(), Stored(designer, "Strip", "TabIndex"), designer.Undo.UndoCount));

        // Emptied: both lines removed in one unit.
        await EditTabs("");
        Assert.Equal(("", null, 5), (Tabs(), Stored(designer, "Strip", "TabIndex"), designer.Undo.UndoCount));

        // One undo brings both lines back; the layout follows (tab 1 of two is selected again).
        designer.Surface!.Execute("edit.undo");
        Assert.Equal(("A,B", "1", 4), (Tabs(), Stored(designer, "Strip", "TabIndex"), designer.Undo.UndoCount));
        Assert.Equal(1, RenderContext.FromLayout(Object(designer, "Strip"), designer.Surface.Layout).TabStrip!.SelectedTab);

        // No selected tab (-1, the default: no line) and a list that was not empty: no auto-select, TabIndex stays -1.
        designer.Surface.Commit(UndoTitles.Property, [new FreexComp.Designer.Editing.SetProperty("Strip", "TabIndex", new IntegerValue(-1, false))]);
        Assert.Null(Stored(designer, "Strip", "TabIndex"));
        var units = designer.Undo.UndoCount;
        await EditTabs("A\r\nB\r\nC");
        Assert.Equal(("A,B,C", null, units + 1), (Tabs(), Stored(designer, "Strip", "TabIndex"), designer.Undo.UndoCount));

        // Another class's TStrings row is not touched by the rule.
        Assert.Null(FreexComp.Designer.Editing.TabControlRules.TabIndexAfterTabsEdit(Object(designer, "Panel1"), [], ["A"]));
    });

    /// <summary>
    /// DSG-RND-025: <c>TTabControl</c> is on the Win32 page before <c>TPageControl</c> from the framework metadata alone; a
    /// tab control placed from the palette has no tabs and no <c>TabIndex</c> line.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-RND-025")]
    public Task PaletteHasTabControlBeforePageControl() => TestUiThread.RunAsync(async () =>
    {
        var framework = CatalogLoader.Load([Path.Combine(DataDesignTests.FrameworkDesignDir, "index.json")]);
        Assert.Empty(framework.Problems);
        var win32 = new ToolPaletteViewModel(framework.Catalog).Pages.Single(p => p.Name == "Win32").Items.Select(i => i.ClassName).ToList();
        Assert.Equal(["TTabControl", "TPageControl"], win32.Take(2));

        var f = new DesignerFixture();
        var document = await f.OpenAsync();
        var designer = document.Designer;
        var palette = new ToolPaletteViewModel(CodeSyncTests.Catalog);
        palette.Bind(designer);
        var client = designer.Surface!.Geometry.FormClientRect;
        palette.Click("TTabControl");
        designer.Surface.OnPointer(new PointerInput(PointerKind.Down, client.Left + 60, client.Top + 150, PointerButton.Left, InputModifiers.None));
        designer.Surface.OnPointer(new PointerInput(PointerKind.Up, client.Left + 60, client.Top + 150, PointerButton.Left, InputModifiers.None));
        var placed = designer.Model!.FindByName("TabControl1");
        Assert.NotNull(placed);
        Assert.Null(placed.FindProperty("Tabs"));
        Assert.Null(placed.FindProperty("TabIndex"));
        Assert.Equal((289, 193), (placed.FindProperty("Width")?.Value is IntegerValue { Value: var w } ? (int)w : 0,
            placed.FindProperty("Height")?.Value is IntegerValue { Value: var h } ? (int)h : 0));
    });
}
