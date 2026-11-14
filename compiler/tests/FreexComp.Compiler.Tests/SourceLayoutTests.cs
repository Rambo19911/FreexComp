using System.Text.RegularExpressions;

namespace FreexComp.Compiler.Tests;

/// <summary>C-187 (gate N9): every compiler source file declares a type named after the file.</summary>
[Trait("Phase", "2")]
public sealed partial class SourceLayoutTests
{
    /// <summary>Files that group many small types by topic (syntax node families, bound/lowered node sets, entry points).</summary>
    private static readonly HashSet<string> GroupFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "FileSystem.cs", "BoundNodes.cs", "LoweredNodes.cs", "ConditionEvaluation.cs", "Program.cs",
    };

    [Fact]
    public void EveryFile_DeclaresATypeNamedAfterIt()
    {
        var src = Path.Combine(RepoPaths.Root, "compiler", "src");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}Nodes{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !GroupFiles.Contains(Path.GetFileName(f)))
            .Where(f =>
            {
                var name = Path.GetFileName(f).Split('.')[0];
                return !TypeDeclaration().Matches(File.ReadAllText(f)).Any(m => m.Groups[1].Value == name);
            })
            .Select(f => Path.GetRelativePath(src, f))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Empty(offenders);
    }

    [GeneratedRegex(@"\b(?:class|struct|record|interface|enum)\s+(?:struct\s+|class\s+)?(\w+)")]
    private static partial Regex TypeDeclaration();
}
