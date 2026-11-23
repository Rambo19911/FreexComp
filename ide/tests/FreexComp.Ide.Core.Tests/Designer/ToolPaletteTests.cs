using System.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Ide.Core.Designer;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>Palette pages over several metadata roots (tool-palette.md DSG-TP-021, component-metadata.md §1; D-095).</summary>
[Trait("Phase", "5")]
public sealed class ToolPaletteTests
{
    [Fact]
    [Trait("Req", "DSG-TP-021")]
    [Trait("Req", "DSG-TP-002")]
    public void PagesFollowPalettePagesOrder()
    {
        static string Component(string cls, string page, int order) =>
            "{ \"class\": \"" + cls + "\", \"ancestor\": \"TComponent\", \"kind\": \"nonvisual\", \"palette\": { \"page\": \""
            + page + "\", \"order\": " + order + " } }";

        static string File(string unit, params string[] components) =>
            "{ \"formatVersion\": 1, \"unit\": \"" + unit + "\", \"components\": [" + string.Join(", ", components) + "] }";

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // The framework root lists a page that no component uses (it is not shown).
            [@"C:\ui\index.json"] = """{ "formatVersion": 1, "palettePages": ["Standard", "Additional", "Win32"], "files": ["Ui.components.json"] }""",
            [@"C:\ui\Ui.components.json"] = File("Ui",
                Component("TButtonLike", "Standard", 1), Component("TShapeLike", "Additional", 1), Component("TOddOne", "Beta", 1)),
            // The data root repeats a page in another casing (ignored) and adds its own pages after the framework's.
            [@"C:\data\index.json"] = """{ "formatVersion": 1, "palettePages": ["Data Access", "standard", "Data Controls"], "files": ["Db.components.json"] }""",
            [@"C:\data\Db.components.json"] = File("Db",
                Component("TGridLike", "Data Controls", 1), Component("TQueryLike", "Data Access", 1), Component("TMore", "Standard", 2),
                Component("TFirstOdd", "Alpha", 1)),
        };
        var result = CatalogLoader.Load([@"C:\ui\index.json", @"C:\data\index.json"],
            path => files.TryGetValue(path, out var text) ? Encoding.UTF8.GetBytes(text) : null);
        Assert.Empty(result.Problems);
        Assert.Equal(["Standard", "Additional", "Win32", "Data Access", "Data Controls"], result.Catalog.PalettePages);

        var palette = new ToolPaletteViewModel(result.Catalog);

        // Listed pages in the roots' order (used ones only), then the unlisted pages sorted by name.
        Assert.Equal(["Standard", "Additional", "Data Access", "Data Controls", "Alpha", "Beta"], palette.Pages.Select(p => p.Name));
        Assert.Equal(["TButtonLike", "TMore"], palette.Pages[0].Items.Select(i => i.ClassName)); // one page across roots

        // Without palettePages every page is unlisted: sorted by name.
        var bare = new ComponentCatalog(result.Catalog.All.Skip(1), [], new Dictionary<string, string>(), []);
        Assert.Equal(["Additional", "Alpha", "Beta", "Data Access", "Data Controls", "Standard"],
            new ToolPaletteViewModel(bare).Pages.Select(p => p.Name));
    }
}
