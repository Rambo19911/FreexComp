using System.Text;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Tests.Designer.CodeSync;

/// <summary>The code-sync test units of the shared test data (designer testing.md §2, <c>TestData/units</c>).</summary>
internal static class TestUnits
{
    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "TestData", "units");

    /// <summary>The exact text of a unit (line ends as stored).</summary>
    public static string Read(string name) => File.ReadAllText(Path.Combine(Directory, name), Encoding.UTF8);

    /// <summary>The golden <c>units/expected/&lt;Case&gt;.pas</c>, byte-exact (DSG-TEST-010).</summary>
    public static string Expected(string caseName) => Read(Path.Combine("expected", caseName + ".pas"));

    /// <summary>An in-memory buffer over a test unit.</summary>
    public static StringTextBuffer Buffer(string name) => new(Read(name));

    /// <summary>
    /// Compares <paramref name="actual"/> with the golden byte-exact; with <c>FXC_UPDATE_GOLDENS=1</c> the golden is
    /// rewritten in the source tree instead (DSG-TEST-011).
    /// </summary>
    public static void AssertGolden(string caseName, string actual)
    {
        if (Environment.GetEnvironmentVariable("FXC_UPDATE_GOLDENS") == "1")
        {
            var source = Path.Combine(SourceDirectory(), "expected", caseName + ".pas");
            File.WriteAllText(source, actual, new UTF8Encoding(false));
            return;
        }

        Assert.Equal(Expected(caseName), actual);
    }

    private static string SourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FreexComp.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("solution root not found"), "ide", "tests", "TestData", "units");
    }
}
