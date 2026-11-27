using FreexComp.Designer.Catalog;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Tests.Layout;

namespace FreexComp.Designer.Tests.Catalog;

/// <summary>
/// D-088 (testing.md §5.4): the real framework metadata (<c>ui-framework/design/</c>) loads without problems and agrees
/// with what the designer implements — renderer ids, designer verbs and component editors (controls-standard.md §29.1),
/// the property names the verbs and editors use, and the palette icons.
/// </summary>
public sealed class MetadataE2ETests
{
    private static string Index => Path.Combine(LayoutHarness.RepoRoot, "ui-framework", "design", "index.json");

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Kind", "E2E")]
    [Trait("Req", "DSG-RND-025")]
    public void RealFrameworkMetadataLoadsWithoutErrors()
    {
        Assert.True(File.Exists(Index), "Framework metadata not found: " + Index);
        var result = CatalogLoader.Load([Index]);
        Assert.Empty(result.Problems); // no Msg.Designer.MetadataError
        var catalog = result.Catalog;
        Assert.Equal(["Standard", "Additional", "Win32", "System", "Dialogs"], catalog.PalettePages);

        // Every renderer id resolves to its own renderer; only abstract classes may stay on the generic one (DSG-RND-008).
        var renderers = RendererRegistry.CreateDefault();
        foreach (var component in catalog.All.Where(c => c.ClassName != ComponentCatalog.RootClassName))
        {
            Assert.True(renderers.Resolve(component.RendererId).Id == component.RendererId,
                $"{component.ClassName}: renderer '{component.RendererId}' is not implemented");
            Assert.True(component.IsAbstract || component.RendererId != "generic", $"{component.ClassName}: concrete class drawn by the generic renderer");
        }

        // The Phase 4 palette (controls-standard.md): every creatable class has a page and an existing SVG icon.
        var creatable = catalog.All.Where(c => c.Palette is not null).ToList();
        Assert.Equal(36, creatable.Count);   // 34 of Phase 4 + TRichEdit (U-144) + TTabControl (U-173)
        foreach (var component in creatable)
        {
            Assert.Contains(component.Palette!.Page, catalog.PalettePages);
            Assert.NotNull(component.Palette.Icon);
            Assert.True(File.Exists(component.Palette.Icon), $"{component.ClassName}: icon {component.Palette.Icon} missing");
            Assert.EndsWith(".svg", component.Palette.Icon, StringComparison.OrdinalIgnoreCase);
        }

        // DSG-RND-025: the tab control comes from metadata alone — on Win32 directly before the page control (the palette
        // sorts by order, ties by class name), drawn by its own renderer, with Tabs and TabIndex for the design-time rules.
        var win32 = creatable.Where(c => c.Palette!.Page == "Win32").OrderBy(c => c.Palette!.Order).ThenBy(c => c.ClassName, StringComparer.Ordinal)
            .Select(c => c.ClassName).ToList();
        Assert.Equal(win32.IndexOf("TPageControl") - 1, win32.IndexOf("TTabControl"));
        Assert.Equal(0, win32.IndexOf("TTabControl"));
        var tabControl = catalog.Find("TTabControl")!;
        Assert.Equal(("tabcontrol", "TCustomTabControl"), (tabControl.RendererId, tabControl.Ancestor));
        Assert.True(catalog.IsAssignable("TPageControl", "TCustomTabControl"));
        Assert.Equal(("TStrings", "strings"), (tabControl.FindProperty("Tabs")!.Type, tabControl.FindProperty("Tabs")!.Editor));
        Assert.Equal(-1L, Convert.ToInt64(tabControl.FindProperty("TabIndex")!.Default, System.Globalization.CultureInfo.InvariantCulture));

        // Designer verbs and component editors: controls-standard.md §29.1, the complete list.
        static string Verbs(ComponentInfo c) => string.Join("; ", c.DesignerVerbs.Select(v => $"{v.Id}|{v.Caption}|{v.Command}|{v.Arg}"));
        var withVerbs = catalog.All.Where(c => !c.IsAbstract && (c.DesignerVerbs.Length > 0 || c.ComponentEditor is not null))
            .ToDictionary(c => c.ClassName, c => (Verbs(c), c.ComponentEditor));
        Assert.Equal(
            new Dictionary<string, (string, string?)>
            {
                ["TActionList"] = ("actionListEditor|&Action List Editor…|designer.editComponent|", "childlist"),
                ["TMainMenu"] = ("menuDesigner|&Menu Designer…|designer.editComponent|", "menu"),
                ["TPopupMenu"] = ("menuDesigner|&Menu Designer…|designer.editComponent|", "menu"),
                ["TPageControl"] = ("newPage|New &Page|designer.newChild|TTabSheet; deletePage|&Delete Page|designer.deletePage|; "
                    + "nextPage|Ne&xt Page|designer.nextPage|; previousPage|Pre&vious Page|designer.previousPage|", null),
                ["TToolBar"] = ("newButton|New &Button|designer.newChild|TToolButton; "
                    + "newSeparator|New &Separator|designer.newChild|TToolButton;Style=tbsSeparator;Width=8", null),
                ["TStatusBar"] = ("editPanels|&Panels Editor…|designer.editCollection|Panels", null),
                ["TListView"] = ("editColumns|&Columns Editor…|designer.editCollection|Columns", null),
            }.OrderBy(e => e.Key),
            withVerbs.OrderBy(e => e.Key));

        // The owners the verbs of a selected child are found through (parentClasses / childClasses, design-surface.md §11.6).
        Assert.Equal(["TPageControl"], catalog.Find("TTabSheet")!.ParentClasses);
        Assert.Equal(["TToolBar"], catalog.Find("TToolButton")!.ParentClasses);
        Assert.Equal(["TMenu", "TMenuItem"], catalog.Find("TMenuItem")!.ParentClasses);
        Assert.Equal(["TActionList"], catalog.Find("TAction")!.ParentClasses);
        Assert.Equal(["TTabSheet"], catalog.Find("TPageControl")!.ChildClasses);

        // The properties the verbs, editors and renderers name.
        foreach (var (className, property) in (ReadOnlySpan<(string, string)>)[
            ("TPageControl", "ActivePage"), ("TTabSheet", "Caption"), ("TTabSheet", "TabVisible"), ("TToolButton", "Style"),
            ("TStatusBar", "Panels"), ("TListView", "Columns"), ("TMenuItem", "Caption"), ("TMenuItem", "ShortCut"),
            ("TAction", "Caption"), ("TAction", "Category"), ("TForm", "ClientWidth"), ("TForm", "ClientHeight"),
            ("TPanel", "BevelOuter"), ("TPanel", "BorderWidth"), ("TScrollBox", "BorderStyle")])
        {
            Assert.True(catalog.Find(className)?.FindProperty(property) is not null, $"{className}.{property} missing");
        }

        Assert.Equal("collection", catalog.Find("TStatusBar")!.FindProperty("Panels")!.Editor);
        Assert.Equal("TStatusPanel", catalog.Find("TStatusBar")!.FindProperty("Panels")!.ItemClass);
        Assert.Equal("TListColumn", catalog.Find("TListView")!.FindProperty("Columns")!.ItemClass);
        Assert.Equal(["OnExecute", "OnHint", "OnUpdate"], catalog.Find("TAction")!.Events.Select(e => e.Name));
        Assert.True(catalog.Find("TAction")!.FindEvent("OnExecute")!.IsDefault);
    }
}
