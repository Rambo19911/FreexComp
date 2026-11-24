using System.Text.Json;
using System.Text.Json.Nodes;
using FreexComp.Build;

namespace FreexComp.Ide.Core.Projects;

/// <summary>
/// The configuration operations of the IDE dialogs on a working copy of the project model (phase8-configurations.md §3,
/// §4): pure functions returning a new <see cref="Project"/> that <c>ProjectSerializer.Save</c> accepts (IDE-CFG-018).
/// Rename and detach are model operations of the build library (<see cref="ProjectConfigurations"/>). Operations that add
/// or remove an entry pass the model through its canonical text, so that the entries keep the order in which they were
/// made and no entry of a removed configuration survives.
/// </summary>
public static class ConfigurationEdits
{
    /// <summary>The folder a configuration uses when it has no <c>outputDir</c> of its own.</summary>
    public static string DefaultOutputDir(string name) => "bin/" + name;

    /// <summary>The effective values of <paramref name="name"/> (IDE-CFG-001); null for a name the project does not have.</summary>
    public static EffectiveConfiguration? Effective(Project project, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(name);
        return project.Configurations.ContainsKey(name) ? ConfigurationResolver.Resolve(project, name) : null;
    }

    /// <summary>IDE-CFG-006: appends a configuration derived from <paramref name="parent"/> that has no value of its own.</summary>
    public static Project AddDerived(Project project, string name, string parent)
    {
        ArgumentNullException.ThrowIfNull(project);
        Require(project, parent);
        RequireNew(project, name);
        var derived = new ProjectConfiguration(0, false, [], DefaultOutputDir(name)) { Inherits = parent, InheritsOptimization = true, InheritsDebugInfo = true };
        return Reparse(project with { Configurations = project.Configurations.Add(name, derived) });
    }

    /// <summary>
    /// IDE-CFG-007: appends a copy of <paramref name="source"/> — its own values, its <c>inherits</c> link and its unknown keys;
    /// the output folder follows the new name when the source used its default <c>bin/&lt;source&gt;</c>.
    /// </summary>
    public static Project AddCopy(Project project, string name, string source)
    {
        ArgumentNullException.ThrowIfNull(project);
        Require(project, source);
        RequireNew(project, name);
        var root = JsonNode.Parse(ProjectSerializer.Save(project))!.AsObject();
        var configurations = root["configurations"]!.AsObject();
        var entry = configurations[source]!.DeepClone().AsObject();
        if (entry["outputDir"] is JsonValue folder && folder.GetValueKind() == JsonValueKind.String
            && folder.GetValue<string>().Replace('\\', '/') == DefaultOutputDir(source))
        {
            entry["outputDir"] = DefaultOutputDir(name);
        }

        configurations[name] = entry;
        return Parse(project, root.ToJsonString());
    }

    /// <summary>Removes a configuration nobody inherits from; the first remaining one (file order) becomes active when it was the active one.</summary>
    /// <exception cref="ArgumentException">Unknown name, the last configuration, or another configuration inherits from it.</exception>
    public static Project Remove(Project project, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        Require(project, name);
        if (project.Configurations.Count == 1)
            throw new ArgumentException("A project needs at least one configuration.", nameof(name));
        if (!ProjectConfigurations.ChildrenOf(project, name).IsEmpty)
            throw new ArgumentException($"Configuration '{name}' is inherited.", nameof(name));
        var remaining = project.ConfigurationNames.Remove(name);
        return Reparse(project with
        {
            Configurations = project.Configurations.Remove(name),
            ActiveConfiguration = project.ActiveConfiguration == name ? remaining[0] : project.ActiveConfiguration,
        });
    }

    /// <summary>
    /// IDE-CFG-016: <paramref name="name"/> inherits from <paramref name="parent"/>. The values it has itself stay its own —
    /// for a former root configuration all of them.
    /// </summary>
    /// <exception cref="ArgumentException">Unknown name, or the link would form a cycle.</exception>
    public static Project SetParent(Project project, string name, string parent)
    {
        ArgumentNullException.ThrowIfNull(project);
        var configuration = Require(project, name);
        Require(project, parent);
        if (parent == name || ProjectConfigurations.DescendantsOf(project, name).Contains(parent))
            throw new ArgumentException($"Configuration '{name}' cannot inherit from '{parent}'.", nameof(parent));
        var linked = configuration.Inherits is null
            ? configuration with { Inherits = parent, InheritsOptimization = false, InheritsDebugInfo = false }
            : configuration with { Inherits = parent };
        return project with { Configurations = project.Configurations.SetItem(name, linked) };
    }

    private static Project Reparse(Project project) => Parse(project, ProjectSerializer.Save(project));

    private static Project Parse(Project project, string text) =>
        ProjectSerializer.ParseProject(project.FilePath, text) is { Value: { } parsed, Diagnostics.IsEmpty: true }
            ? parsed with { FilePath = project.FilePath }
            : throw new InvalidOperationException("The working copy of the project is not a valid project file.");

    private static ProjectConfiguration Require(Project project, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return project.Configurations.TryGetValue(name, out var configuration)
            ? configuration
            : throw new ArgumentException($"Unknown configuration '{name}'.", nameof(name));
    }

    private static void RequireNew(Project project, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (project.Configurations.ContainsKey(name))
            throw new ArgumentException($"Configuration '{name}' already exists.", nameof(name));
    }
}
