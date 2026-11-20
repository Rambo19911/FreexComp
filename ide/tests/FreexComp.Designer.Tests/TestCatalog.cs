using FreexComp.Designer.Catalog;

namespace FreexComp.Designer.Tests;

/// <summary>The catalog of the test component metadata (testing.md §2), loaded once.</summary>
internal static class TestCatalog
{
    private static readonly Lazy<ComponentCatalog> Loaded = new(() =>
    {
        var result = CatalogLoader.Load([TestPaths.MetadataIndex]);
        Assert.Empty(result.Problems);
        return result.Catalog;
    });

    public static ComponentCatalog Instance => Loaded.Value;
}
