using System.Globalization;
using System.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The Tool Palette's view-model (tool-palette.md §3–§5; D-077).</summary>
[Trait("Phase", "5")]
public sealed class ToolPaletteViewModelTests
{
    /// <summary>A catalog from in-memory metadata: ties in <c>order</c>, pages not listed in <c>palettePages</c>, items that are not offered.</summary>
    private static ComponentCatalog OrderCatalog()
    {
        static string Component(string cls, string? page, int order = 0, string extra = "") =>
            "{ \"class\": \"" + cls + "\", \"ancestor\": \"TComponent\", \"kind\": \"nonvisual\"" + extra
            + (page is null ? "" : ", \"palette\": { \"page\": \"" + page + "\", \"order\": " + order.ToString(CultureInfo.InvariantCulture) + " }")
            + ", \"properties\": [], \"events\": [] }";

        var files = new Dictionary<string, string>
        {
            [@"C:\m\index.json"] = """{ "formatVersion": 1, "palettePages": ["Standard", "Additional"], "files": ["A.components.json"] }""",
            [@"C:\m\A.components.json"] = "{ \"formatVersion\": 1, \"unit\": \"A\", \"components\": ["
                + string.Join(", ",
                    Component("TBeta", "Standard", 20), Component("TAlpha", "Standard", 20), Component("TFirst", "Standard", 10),
                    Component("TExtra", "Additional", 5), Component("TZed", "Zeta", 1), Component("TOther", "Others", 1),
                    Component("TAbstract", "Standard", 1, ", \"abstract\": true"), Component("THidden", null),
                    Component("TFormLike", "Standard", 1, ", \"kind\": \"form\"").Replace("\"kind\": \"nonvisual\", ", "", StringComparison.Ordinal))
                + "], \"eventSignatures\": {} }",
        };
        var result = CatalogLoader.Load([@"C:\m\index.json"], path => files.TryGetValue(path, out var text) ? Encoding.UTF8.GetBytes(text) : null);
        Assert.Empty(result.Problems);
        return result.Catalog;
    }

    private static IEnumerable<string> Classes(PalettePage page) => page.Items.Select(i => i.ClassName);

    [Fact]
    [Trait("Req", "DSG-TP-020")]
    [Trait("Req", "DSG-TP-021")]
    public void PagesAndOrder()
    {
        var palette = new ToolPaletteViewModel(OrderCatalog());

        // Listed pages first in index order, then the others by name; items by order, ties by class name.
        Assert.Equal(["Standard", "Additional", "Others", "Zeta"], palette.Pages.Select(p => p.Name));
        Assert.Equal(["TFirst", "TAlpha", "TBeta"], Classes(palette.Pages[0]));
        Assert.Equal(["TExtra"], Classes(palette.Pages[1]));
        Assert.DoesNotContain(palette.Pages.SelectMany(p => p.Items), i => i.ClassName is "TAbstract" or "THidden" or "TFormLike");
        Assert.Equal("Alpha", palette.Pages[0].Items[1].DisplayName);

        // The test metadata: the framework's page order.
        palette.SetCatalog(CodeSyncTests.Catalog);
        Assert.Equal(["Standard", "Additional", "Win32", "System", "Dialogs"], palette.Pages.Select(p => p.Name));
        Assert.Equal(["TMainMenu", "TPopupMenu", "TLabel", "TEdit", "TMemo", "TButton", "TCheckBox", "TRadioButton", "TListBox", "TComboBox",
            "TGroupBox", "TPanel"], Classes(palette.Pages[0]));

        // DSG-TP-022: collapsing keeps the page with no visible items.
        palette.TogglePage("Standard");
        Assert.False(palette.Pages[0].IsExpanded);
        Assert.Contains("Standard", palette.CollapsedPages);
        palette.TogglePage("Standard");
        Assert.True(palette.Pages[0].IsExpanded);
    }

    [Fact]
    [Trait("Req", "DSG-TP-031")]
    [Trait("Req", "DSG-TP-032")]
    public void SearchSubstringPrefixFirst()
    {
        var palette = new ToolPaletteViewModel(CodeSyncTests.Catalog);
        palette.TogglePage("Standard");

        // Substring of the class name with or without T; items starting with the text first; pages without matches hidden;
        // a collapsed page with matches is shown expanded without changing its stored state.
        palette.SearchText = "button";
        Assert.Equal(["Standard"], palette.Pages.Select(p => p.Name));
        Assert.True(palette.Pages[0].IsExpanded);
        Assert.Contains("Standard", palette.CollapsedPages);
        Assert.Equal(["TButton", "TRadioButton"], Classes(palette.Pages[0]));
        Assert.Equal("TButton", palette.CurrentItem?.ClassName);

        palette.SearchText = "  TEDIT ";
        Assert.Equal(["TEdit"], palette.Pages.SelectMany(Classes));

        // Every word must match.
        palette.SearchText = "radio but";
        Assert.Equal(["TRadioButton"], palette.Pages.SelectMany(Classes));
        Assert.Equal("TRadioButton", palette.CurrentItem?.ClassName);

        palette.SearchText = "dialog";
        Assert.Equal(["Dialogs"], palette.Pages.Select(p => p.Name));
        Assert.Equal(["TOpenDialog", "TSaveDialog"], Classes(palette.Pages[0]));

        palette.SearchText = "zzz";
        Assert.True(palette.IsFilterEmpty);
        Assert.Empty(palette.Pages);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Palette_FilterEmpty, "zzz"), palette.FilterEmptyText);
        Assert.Null(palette.CurrentItem);

        palette.SearchText = "";
        Assert.False(palette.IsFilterEmpty);
        Assert.False(palette.Pages[0].IsExpanded);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-040")]
    [Trait("Req", "DSG-TP-041")]
    public Task ArmDisarmSticky() => TestUiThread.RunAsync(() => ArmDisarmStickyOnUiThread()); // I-209

    private async Task ArmDisarmStickyOnUiThread()
    {
        var f = new DesignerFixture();
        var document = await f.OpenAsync();
        var designer = document.Designer;
        var palette = new ToolPaletteViewModel(CodeSyncTests.Catalog);
        palette.Bind(designer);
        var surface = designer.Surface!;
        var client = surface.Geometry.FormClientRect;
        void ClickSurface(int x, int y)
        {
            surface.OnPointer(new PointerInput(PointerKind.Down, client.Left + x, client.Top + y, PointerButton.Left, InputModifiers.None));
            surface.OnPointer(new PointerInput(PointerKind.Up, client.Left + x, client.Top + y, PointerButton.Left, InputModifiers.None));
        }

        // Click arms (single-shot); clicking it again disarms; another item arms that one instead; Escape disarms.
        palette.Click("TButton");
        Assert.Equal("TButton", palette.ArmedClass);
        Assert.False(palette.IsSticky);
        Assert.Equal("TButton", designer.Host.ArmedPaletteClass);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_PlacementArmed, "TButton"), f.Services.Status.Message);
        palette.Click("TButton");
        Assert.Null(palette.ArmedClass);
        Assert.Null(designer.Host.ArmedPaletteClass);
        palette.Click("TButton");
        palette.Click("TEdit");
        Assert.Equal("TEdit", palette.ArmedClass);
        Assert.True(palette.Escape());
        Assert.Null(palette.ArmedClass);

        // Single-shot: a placement on the surface creates the component and disarms.
        palette.Click("TEdit");
        ClickSurface(300, 200);
        Assert.NotNull(designer.Model!.FindByName("Edit1"));
        Assert.Null(palette.ArmedClass);

        // Shift+click arms sticky: it stays armed after each placement; a right-click on the surface disarms.
        palette.Click("TLabel", shift: true);
        Assert.True(palette.IsSticky);
        Assert.True(designer.Host.ArmedSticky);
        ClickSurface(300, 240);
        ClickSurface(300, 270);
        Assert.NotNull(designer.Model.FindByName("Label1"));
        Assert.NotNull(designer.Model.FindByName("Label2"));
        Assert.Equal("TLabel", palette.ArmedClass);
        surface.OnPointer(new PointerInput(PointerKind.Down, client.Left + 5, client.Top + 5, PointerButton.Right, InputModifiers.None));
        Assert.Null(palette.ArmedClass);

        // Switching the view disarms; Enter/double-click places at the default position without arming.
        palette.Click("TButton");
        Assert.True(document.ShowView(DocumentView.Code));
        Assert.Null(palette.ArmedClass);
        Assert.True(document.ShowView(DocumentView.Design));
        palette.Place("TMemo");
        Assert.NotNull(designer.Model.FindByName("Memo1"));
        Assert.Null(palette.ArmedClass);

        // Unbinding (another document) disarms too.
        palette.Click("TButton");
        palette.Bind(null);
        Assert.Null(palette.ArmedClass);
        Assert.Null(designer.Host.ArmedPaletteClass);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-024")]
    public Task DisabledWithoutDesignView() => TestUiThread.RunAsync(() => DisabledWithoutDesignViewOnUiThread()); // I-209

    private async Task DisabledWithoutDesignViewOnUiThread()
    {
        var palette = new ToolPaletteViewModel(CodeSyncTests.Catalog);
        Assert.False(palette.IsEnabled);
        Assert.False(palette.HasForm);
        Assert.Equal(Strings.Msg_Palette_NoForm, palette.NoFormText);
        palette.Click("TButton");
        Assert.Null(palette.ArmedClass);

        // A form document in its Code view: no form to place on.
        var f = new DesignerFixture();
        var document = await f.OpenAsync(design: false);
        palette.Bind(document.Designer);
        Assert.False(palette.IsEnabled);
        palette.Click("TButton");
        Assert.Null(palette.ArmedClass);

        // The Design view enables it.
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.True(palette.IsEnabled);
        Assert.True(palette.AreItemsEnabled);

        // A read-only form (the unit has a syntax error): the list is shown, the items are disabled.
        var broken = new DesignerFixture();
        var readOnly = await broken.OpenAsync(DesignerFixture.Unit.Replace("  public\r\n  end;", "  public\r\n  end", StringComparison.Ordinal));
        Assert.True(readOnly.Designer.Surface!.IsReadOnly);
        palette.Bind(readOnly.Designer);
        Assert.True(palette.HasForm);
        Assert.False(palette.AreItemsEnabled);
        Assert.False(palette.IsEnabled);
        palette.Click("TButton");
        Assert.Null(palette.ArmedClass);
        palette.Place("TButton");
        Assert.Null(readOnly.Designer.Model!.FindByName("Button1"));
    }
}
