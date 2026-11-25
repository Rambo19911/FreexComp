using System.Collections.Immutable;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Toolchain;

/// <summary>Inputs of the CMP-600 lookup that come from the process environment.</summary>
/// <param name="FreexCompHome"><c>%FREEXCOMP_HOME%</c>, or null.</param>
/// <param name="ExecutableDirectory">Directory of the running executable (<c>opc.exe</c>, the IDE), or null.</param>
/// <param name="PathVariable"><c>%PATH%</c>, or null.</param>
internal sealed record ToolchainEnvironment(string? FreexCompHome, string? ExecutableDirectory, string? PathVariable)
{
    public static ToolchainEnvironment Current => new(
        Environment.GetEnvironmentVariable("FREEXCOMP_HOME"),
        AppContext.BaseDirectory,
        Environment.GetEnvironmentVariable("PATH"));
}

/// <summary>The located LLVM bin directory (normalised, CMP-205) and its required executables.</summary>
internal sealed record ToolchainPaths(string Directory)
{
    public string Clang => Path.Combine(Directory, "clang.exe");

    public string LldLink => Path.Combine(Directory, "lld-link.exe");

    public string DllTool => Path.Combine(Directory, "llvm-dlltool.exe");

    /// <summary>CMP-1300: the resource tool; not a required tool, looked for when a resource script is compiled.</summary>
    public string ResourceTool => Path.Combine(Directory, ToolchainLocator.ResourceToolName);
}

/// <summary>CMP-600: tool directory resolution, first hit wins.</summary>
internal static class ToolchainLocator
{
    public static readonly ImmutableArray<string> RequiredTools = ["clang.exe", "lld-link.exe", "llvm-dlltool.exe"];

    /// <summary>CMP-1300: file name of the resource tool.</summary>
    public const string ResourceToolName = "llvm-rc.exe";

    /// <summary>What compiling a resource script alone needs of the directory (the tool <c>prc</c>): the preprocessor.</summary>
    public static readonly ImmutableArray<string> PreprocessorOnly = ["clang.exe"];

    /// <summary>
    /// An explicit directory (<c>--toolchain</c>) is authoritative: tools missing there are E4001 without falling
    /// back. Otherwise the first candidate directory containing every required tool wins:
    /// <c>$FREEXCOMP_HOME\third_party\llvm\bin</c>, then <c>&lt;dir&gt;\third_party\llvm\bin</c> for the executable's
    /// directory and each ancestor, then each absolute <c>PATH</c> entry. <paramref name="requiredTools"/>: null →
    /// <see cref="RequiredTools"/>.
    /// </summary>
    public static (ToolchainPaths? Paths, Diagnostic? Diagnostic) Locate(
        IFileSystem fs, string explicitDirectory, ToolchainEnvironment environment, Location location, ImmutableArray<string>? requiredTools = null)
    {
        var required = requiredTools ?? RequiredTools;
        if (!string.IsNullOrEmpty(explicitDirectory))
        {
            var dir = PathUtil.Normalize(explicitDirectory);
            var missing = required.Where(t => !fs.FileExists(Path.Combine(dir, t))).ToList();
            return missing.Count == 0
                ? (new ToolchainPaths(dir), null)
                : (null, Diagnostic.Create("E4001", location, string.Join(", ", missing), dir));
        }

        var searched = new List<string>();
        foreach (var candidate in Candidates(environment))
        {
            if (searched.Contains(candidate, PathUtil.Comparer))
            {
                continue;
            }

            searched.Add(candidate);
            if (required.All(t => fs.FileExists(Path.Combine(candidate, t))))
            {
                return (new ToolchainPaths(candidate), null);
            }
        }

        return (null, Diagnostic.Create("E4001", location, string.Join(", ", required), string.Join("; ", searched)));
    }

    private static IEnumerable<string> Candidates(ToolchainEnvironment environment)
    {
        const string Relative = @"third_party\llvm\bin";
        if (Absolute(environment.FreexCompHome) is { } home)
        {
            yield return PathUtil.Normalize(Path.Combine(home, Relative));
        }

        if (Absolute(environment.ExecutableDirectory) is { } exeDir)
        {
            for (var dir = PathUtil.Normalize(exeDir); dir is not null; dir = Path.GetDirectoryName(dir))
            {
                yield return PathUtil.Normalize(Path.Combine(dir, Relative));
            }
        }

        foreach (var entry in (environment.PathVariable ?? "").Split(';'))
        {
            if (Absolute(entry.Trim().Trim('"')) is { } dir)
            {
                yield return PathUtil.Normalize(dir);
            }
        }
    }

    private static string? Absolute(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) ? path : null;
}
