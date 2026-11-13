namespace FreexComp.LanguageService.Tests;

internal static class RepoPaths
{
    /// <summary>The directory containing FreexComp.sln, found by walking up from the test output folder.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>
    /// Path of the repository's <c>docs</c> folder. Skips the calling test when it is absent (product-only
    /// checkout; TESTING.md, trait <c>Category=Spec</c>). Call only from tests carrying that trait.
    /// </summary>
    public static string RequireDocs()
    {
        var docs = Path.Combine(Root, "docs");
        if (!Directory.Exists(docs))
        {
            Assert.Skip("docs/ not present (product-only checkout)");
        }

        return docs;
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreexComp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("FreexComp.sln not found above " + AppContext.BaseDirectory);
    }
}
