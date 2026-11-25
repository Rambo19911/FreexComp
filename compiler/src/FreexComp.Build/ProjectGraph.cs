using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.Build;

/// <summary>
/// The projects one request builds, in build order (CMP-821). <see cref="Problems"/> non-null = nothing is built and these
/// diagnostics are reported for the requested file (unreadable group, dependency cycle E9006).
/// </summary>
internal sealed record ProjectPlan(
    ImmutableArray<Diagnostic>? Problems,
    ImmutableArray<string> Order,
    IReadOnlyDictionary<string, LoadResult<Project>> Loads,
    IReadOnlyDictionary<string, List<string>> Dependencies,
    string Last);

/// <summary>Loads a project or group with everything it depends on and orders it; shared by the build engine and <see cref="ProjectTrust"/>.</summary>
internal static class ProjectGraph
{
    /// <summary>
    /// CMP-821: <paramref name="path"/> (a project), or the projects of the group <paramref name="path"/> (or
    /// <paramref name="projectInGroup"/>; for <paramref name="run"/> the active project), and every project they depend on, in
    /// topological order of <c>dependencies</c>; ties by the group's order, then discovery order, the requested / active project
    /// last (CMP-807). A cycle is E9006.
    /// </summary>
    /// <param name="path">Normalised absolute path of a <c>.pproj</c> or <c>.pgroup</c>.</param>
    public static ProjectPlan Plan(IFileSystem fs, string path, string? projectInGroup, bool run)
    {
        if (!path.EndsWith(".pgroup", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(fs, path, [path], [], path);
        }

        var groupLoad = ProjectSerializer.LoadGroup(path, fs);
        if (groupLoad.Value is not { } group || HasErrors(groupLoad.Diagnostics))
        {
            return Failed(groupLoad.Diagnostics, path);
        }

        var directory = Path.GetDirectoryName(path)!;
        var listed = group.Projects.Select(p => Resolve(directory, p)).ToList();
        var requested = projectInGroup is not null ? Resolve(directory, projectInGroup)
            : run ? Resolve(directory, group.ActiveProject)
            : null;
        var last = requested ?? Resolve(directory, group.ActiveProject);
        return Plan(fs, path, requested is null ? listed : [requested], listed, last);
    }

    /// <summary>
    /// <paramref name="roots"/> and every project they depend on, in topological order; ties by <paramref name="listed"/>, then
    /// discovery order, <paramref name="last"/> last.
    /// </summary>
    private static ProjectPlan Plan(IFileSystem fs, string path, List<string> roots, List<string> listed, string last)
    {
        var loads = new Dictionary<string, LoadResult<Project>>(PathUtil.Comparer);
        var dependencies = new Dictionary<string, List<string>>(PathUtil.Comparer);
        var discovered = new List<string>();
        void Visit(string project)
        {
            if (loads.ContainsKey(project))
            {
                return;
            }

            var load = ProjectSerializer.LoadProject(project, fs);
            loads[project] = load;
            discovered.Add(project);
            var projectDirectory = Path.GetDirectoryName(project)!;
            dependencies[project] = load.Value is { } p && !HasErrors(load.Diagnostics)
                ? [.. p.Dependencies.Select(d => Resolve(projectDirectory, d))]
                : [];
            foreach (var dependency in dependencies[project])
            {
                Visit(dependency);
            }
        }

        foreach (var root in roots)
        {
            Visit(root);
        }

        int Position(string project) => listed.FindIndex(p => PathUtil.AreEqual(p, project)) is var i and >= 0 ? i : listed.Count + discovered.IndexOf(project);
        int Rank(string project) => PathUtil.AreEqual(project, last) ? int.MaxValue : Position(project);

        var remaining = new HashSet<string>(discovered, PathUtil.Comparer);
        var order = new List<string>();
        while (remaining.Count > 0
               && remaining.Where(p => !dependencies[p].Any(remaining.Contains)).OrderBy(Rank).FirstOrDefault() is { } ready)
        {
            remaining.Remove(ready);
            order.Add(ready);
        }

        if (remaining.Count > 0)
        {
            var cycle = FindCycle(remaining.OrderBy(Position).First(), dependencies, remaining);
            return Failed([Diagnostic.Create("E9006", Location.ForFile(path), string.Join(" -> ", cycle))], last);
        }

        return new ProjectPlan(null, [.. order], loads, dependencies, last);
    }

    private static ProjectPlan Failed(ImmutableArray<Diagnostic> problems, string last) =>
        new(problems, [], new Dictionary<string, LoadResult<Project>>(), new Dictionary<string, List<string>>(), last);

    private static bool HasErrors(IEnumerable<Diagnostic> diagnostics) => diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>A dependency cycle through the part of <paramref name="within"/> reachable from <paramref name="start"/>: <c>A, B, …, A</c>.</summary>
    private static List<string> FindCycle(string start, Dictionary<string, List<string>> dependencies, HashSet<string> within)
    {
        var stack = new List<string>();
        var done = new HashSet<string>(PathUtil.Comparer);
        List<string>? Walk(string node)
        {
            var onStack = stack.FindIndex(p => PathUtil.AreEqual(p, node));
            if (onStack >= 0)
            {
                return [.. stack[onStack..], node];
            }

            if (!done.Add(node))
            {
                return null;
            }

            stack.Add(node);
            foreach (var next in dependencies[node].Where(within.Contains))
            {
                if (Walk(next) is { } cycle)
                {
                    return cycle;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            return null;
        }

        return Walk(start) ?? [start];
    }

    /// <summary>A project path (relative to <paramref name="directory"/> or absolute, '/' or '\') → normalised absolute path.</summary>
    private static string Resolve(string directory, string path) =>
        PathUtil.Normalize(Path.IsPathFullyQualified(path) ? path : Path.Combine(directory, path.Replace('/', '\\')));
}
