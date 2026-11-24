using System.Collections.Immutable;
using System.Text.Json;

namespace FreexComp.Build;

/// <summary>
/// Model operations on the configurations of a project (configurations.md CMP-1159): pure functions that return a new
/// <see cref="Project"/>. Names are compared exactly.
/// </summary>
public static class ProjectConfigurations
{
    /// <summary>
    /// Renames a configuration: the entry keeps its position in the file order and its unknown keys; every <c>inherits</c> equal to
    /// <paramref name="oldName"/> and <see cref="Project.ActiveConfiguration"/> when equal to it follow; an <c>outputDir</c> that was
    /// the default <c>bin/&lt;oldName&gt;</c> and not written in the file follows the new name, a written one is left alone.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="oldName"/> does not exist, or <paramref name="newName"/> is empty or exists.</exception>
    public static Project Rename(Project project, string oldName, string newName)
    {
        var configuration = Require(project, oldName);
        ArgumentNullException.ThrowIfNull(newName);
        if (newName.Length == 0)
        {
            throw new ArgumentException("A configuration name must not be empty", nameof(newName));
        }

        if (project.Configurations.ContainsKey(newName))
        {
            throw new ArgumentException($"Configuration '{newName}' already exists", nameof(newName));
        }

        var outputDirWritten = SourceEntry(project, oldName) is { } entry && entry.TryGetProperty("outputDir", out _);
        if (!outputDirWritten && ProjectSerializer.Slashes(configuration.OutputDir) == "bin/" + oldName)
        {
            configuration = configuration with { OutputDir = "bin/" + newName };
        }

        var configurations = ImmutableDictionary.CreateBuilder<string, ProjectConfiguration>(project.Configurations.KeyComparer);
        foreach (var (name, value) in project.Configurations)
        {
            var renamed = name == oldName ? configuration : value;
            configurations[name == oldName ? newName : name] = renamed.Inherits == oldName ? renamed with { Inherits = newName } : renamed;
        }

        return project with
        {
            Configurations = configurations.ToImmutable(),
            ActiveConfiguration = project.ActiveConfiguration == oldName ? newName : project.ActiveConfiguration,
            Source = project.Source is { } source ? RenameInSource(source, oldName, newName) : null,
        };
    }

    /// <summary>
    /// Makes a derived configuration a root configuration with the same effective values: <c>Optimization</c>, <c>DebugInfo</c> and
    /// <c>Rtti</c> (when a member of its chain set it) become own values, <c>Defines</c> and <c>SearchPaths</c> the accumulated
    /// entries of its chain without the project level (preceded by <c>"!"</c> only if a reset marker was in effect);
    /// <c>OutputDir</c> is unchanged. A root configuration is returned as it is.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> does not exist, or its chain is broken.</exception>
    public static Project Detach(Project project, string name)
    {
        var configuration = Require(project, name);
        if (configuration.Inherits is null)
        {
            return project;
        }

        var effective = ConfigurationResolver.Resolve(project, name);
        var members = effective.Chain.Select(member => project.Configurations[member]).ToList();
        return project with
        {
            Configurations = project.Configurations.SetItem(name, configuration with
            {
                Inherits = null,
                InheritsOptimization = false,
                InheritsDebugInfo = false,
                Optimization = effective.Optimization,
                DebugInfo = effective.DebugInfo,
                Rtti = members.Select(member => member.Rtti).LastOrDefault(rtti => rtti is not null),
                Defines = Accumulate(members.Select(member => member.Defines)),
                SearchPaths = Accumulate(members.Select(member => member.SearchPaths)),
            }),
        };
    }

    /// <summary>The configurations whose <c>inherits</c> is <paramref name="name"/>, in file order.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> does not exist.</exception>
    public static ImmutableArray<string> ChildrenOf(Project project, string name)
    {
        Require(project, name);
        return [.. project.ConfigurationNames.Where(candidate => project.Configurations[candidate].Inherits == name)];
    }

    /// <summary>The configurations that inherit from <paramref name="name"/> directly or through others, in file order.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> does not exist.</exception>
    public static ImmutableArray<string> DescendantsOf(Project project, string name)
    {
        Require(project, name);
        bool Descends(string candidate)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { candidate };
            for (var parent = project.Configurations[candidate].Inherits;
                 parent is not null && project.Configurations.TryGetValue(parent, out var next) && seen.Add(parent);
                 parent = next.Inherits)
            {
                if (parent == name)
                {
                    return true;
                }
            }

            return false;
        }

        return [.. project.ConfigurationNames.Where(candidate => candidate != name && Descends(candidate))];
    }

    /// <summary>CMP-1157: every <c>inherits</c> link names a configuration and no chain returns to its start.</summary>
    /// <exception cref="ArgumentException">A link names a missing configuration, or the links form a cycle.</exception>
    internal static void RequireValidLinks(Project project)
    {
        foreach (var name in project.Configurations.Keys)
        {
            ConfigurationResolver.ChainOf(project, name);
        }
    }

    private static ProjectConfiguration Require(Project project, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(name);
        return project.Configurations.TryGetValue(name, out var configuration)
            ? configuration
            : throw new ArgumentException($"Unknown configuration '{name}'", nameof(name));
    }

    /// <summary>The chain's entries of one list without the project level; a leading <c>"!"</c> when a reset marker was in effect.</summary>
    private static ImmutableArray<string> Accumulate(IEnumerable<ImmutableArray<string>> lists)
    {
        var accumulated = new List<string>();
        var reset = false;
        foreach (var own in lists)
        {
            reset |= !own.IsDefaultOrEmpty && own[0] == ConfigurationResolver.ResetMarker;
            ConfigurationResolver.Append(accumulated, own);
        }

        return reset ? [ConfigurationResolver.ResetMarker, .. accumulated] : [.. accumulated];
    }

    private static JsonElement? SourceEntry(Project project, string name) =>
        project.Source is { ValueKind: JsonValueKind.Object } source
        && source.TryGetProperty("configurations", out var configurations) && configurations.ValueKind == JsonValueKind.Object
        && configurations.TryGetProperty(name, out var entry) && entry.ValueKind == JsonValueKind.Object
            ? entry
            : null;

    /// <summary>
    /// The loaded JSON with the key of one configuration renamed in place, so that the entry keeps its position and its unknown
    /// keys (CMP-811). A stale entry that already has the new name (a configuration removed from the model) is dropped.
    /// </summary>
    private static JsonElement RenameInSource(JsonElement source, string oldName, string newName)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            return source;
        }

        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            foreach (var property in source.EnumerateObject())
            {
                if (property.Name != "configurations" || property.Value.ValueKind != JsonValueKind.Object)
                {
                    property.WriteTo(w);
                    continue;
                }

                w.WriteStartObject(property.Name);
                foreach (var entry in property.Value.EnumerateObject().Where(entry => entry.Name != newName))
                {
                    w.WritePropertyName(entry.Name == oldName ? newName : entry.Name);
                    entry.Value.WriteTo(w);
                }

                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }
}
