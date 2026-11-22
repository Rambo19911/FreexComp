namespace FreexComp.Designer.Tests;

/// <summary>Golden-file comparison (DSG-TEST-010/011): LF-normalised actual text against the file's exact bytes.</summary>
internal static class Golden
{
    private const string UpdateVariable = "FXC_UPDATE_GOLDENS";

    /// <summary>
    /// Compares <paramref name="actual"/> (line ends normalised to LF) with the golden file; with
    /// <c>FXC_UPDATE_GOLDENS=1</c> the golden in the source tree is rewritten instead (never in CI).
    /// </summary>
    public static void Check(string goldenPathInOutput, string actual) =>
        Check(goldenPathInOutput, actual, Environment.GetEnvironmentVariable);

    /// <summary>
    /// <see cref="Check(string, string)"/> reading the environment through <paramref name="environment"/>. Update mode
    /// with <c>CI</c> set (any non-empty value) fails without writing (DSG-TEST-011, TESTING.md rule 5).
    /// </summary>
    internal static void Check(string goldenPathInOutput, string actual, Func<string, string?> environment)
    {
        actual = actual.ReplaceLineEndings("\n");
        if (environment(UpdateVariable) == "1")
        {
            if (!string.IsNullOrEmpty(environment("CI")))
            {
                Assert.Fail($"{UpdateVariable}=1 is not allowed when CI is set: goldens are never rewritten in CI ({goldenPathInOutput})");
            }

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
