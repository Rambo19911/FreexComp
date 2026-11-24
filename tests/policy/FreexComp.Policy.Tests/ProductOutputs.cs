using System.Text.RegularExpressions;

namespace FreexComp.Policy.Tests;

/// <summary>
/// The product projects of the solution and their build outputs (NET-010): every project whose path is
/// <c>&lt;area&gt;/src/&lt;name&gt;/&lt;name&gt;.csproj</c>, read from the solution file, built with this test project's
/// configuration and target framework.
/// </summary>
internal static partial class ProductOutputs
{
    [GeneratedRegex("""^Project\("\{[^}]+\}"\) = "([^"]+)", "([^"]+\.csproj)",""", RegexOptions.Multiline)]
    private static partial Regex ProjectLine();

    /// <summary>(project name, output directory) of every product project, ordered by name.</summary>
    public static IReadOnlyList<(string Name, string OutputDirectory)> All()
    {
        var own = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var framework = own.Name;
        var configuration = own.Parent!.Name;
        var result = new List<(string, string)>();
        foreach (Match match in ProjectLine().Matches(File.ReadAllText(RepoPaths.Combine("FreexComp.sln"))))
        {
            var name = match.Groups[1].Value;
            var parts = match.Groups[2].Value.Split('\\', '/');
            if (parts.Length == 4 && parts[1] == "src" && parts[2] == name && parts[3] == name + ".csproj")
                result.Add((name, RepoPaths.Combine(parts[0], "src", name, "bin", configuration, framework)));
        }

        return [.. result.OrderBy(p => p.Item1, StringComparer.Ordinal)];
    }

    public static string OutputDirectory(string project) => All().Single(p => p.Name == project).OutputDirectory;
}
