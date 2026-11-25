using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.Build;

/// <summary>Questions about the <c>dependencies</c> of projects for editors (group-build.md CMP-1346…1348); the build engine's own graph code.</summary>
public static class ProjectDependencies
{
    /// <summary>
    /// The cycle that results when the project at <paramref name="projectPath"/> gets exactly these dependencies; null when there
    /// is none. Every other project is read from <paramref name="fs"/>; a dependency file that is missing or invalid is a leaf.
    /// Starts no process and writes nothing.
    /// </summary>
    /// <param name="projectPath">Absolute path of the project; its file need not exist and its saved dependencies are not used.</param>
    /// <param name="dependencies">Relative to the project's directory or absolute, as in the file.</param>
    /// <returns>Absolute, normalized paths, first == last (the text of E9006 joins them with <c>" -&gt; "</c>).</returns>
    public static ImmutableArray<string>? FindCycle(IFileSystem fs, string projectPath, IEnumerable<string> dependencies)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(dependencies);
        return ProjectGraph.FindCycle(fs, PathUtil.Normalize(projectPath), [.. dependencies]);
    }
}
