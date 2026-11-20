namespace FreexComp.Designer.Tests;

/// <summary>Golden-file comparison (DSG-TEST-010/011): LF-normalised actual text against the file's exact bytes.</summary>
internal static class Golden
{
    private const string UpdateVariable = "FXC_UPDATE_GOLDENS";

    /// <summary>
    /// Compares <paramref name="actual"/> (line ends normalised to LF) with the golden file; with
    /// <c>FXC_UPDATE_GOLDENS=1</c> the golden in the source tree is rewritten instead (never in CI).
    /// </summary>
    public static void Check(string goldenPathInOutput, string actual)
    {
        actual = actual.ReplaceLineEndings("\n");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(SourcePath(goldenPathInOutput), actual);
            return;
        }

        Assert.True(File.Exists(goldenPathInOutput), $"golden file missing: {goldenPathInOutput}");
        Assert.Equal(File.ReadAllText(goldenPathInOutput), actual);
    }

    /// <summary>Maps a copied TestData path back to ide/tests/TestData in the source tree.</summary>
    private static string SourcePath(string outputPath)
    {
        var relative = Path.GetRelativePath(TestPaths.Root, outputPath);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "ide", "tests", "TestData");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(candidate, relative);
            }
        }

        throw new InvalidOperationException("ide/tests/TestData not found above " + AppContext.BaseDirectory);
    }
}
