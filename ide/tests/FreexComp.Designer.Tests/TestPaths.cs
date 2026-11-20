namespace FreexComp.Designer.Tests;

/// <summary>Locations of the shared test data copied to the output directory (testing.md §2).</summary>
internal static class TestPaths
{
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "TestData");

    public static string Metadata { get; } = Path.Combine(Root, "metadata");

    public static string MetadataIndex { get; } = Path.Combine(Metadata, "index.json");

    public static string Forms { get; } = Path.Combine(Root, "forms");

    public static string Combine(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
}
