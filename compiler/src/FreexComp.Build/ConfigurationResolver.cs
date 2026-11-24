using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FreexComp.Build;

/// <summary>
/// The values a configuration is built with (configurations.md CMP-1158, CMP-1160): its own values merged with those of its
/// ancestors and of the project level.
/// </summary>
/// <param name="Name">The configuration.</param>
/// <param name="Chain">Root ancestor first, the configuration itself last.</param>
/// <param name="Optimization">The root's value, replaced by each derived member that has its own.</param>
/// <param name="DebugInfo">As <paramref name="Optimization"/>.</param>
/// <param name="Defines">Project level first, then the chain; no <c>"!"</c> entries.</param>
/// <param name="SearchPaths">Project-relative as stored; project level first, then the chain; no <c>"!"</c> entries.</param>
/// <param name="OutputDir">Never inherited: the configuration's own value, else <c>bin/&lt;name&gt;</c>.</param>
/// <param name="Rtti"><c>broad</c> | <c>published</c>: the last member of the chain that sets it, else the project's, else <c>broad</c>.</param>
public sealed record EffectiveConfiguration(
    string Name,
    ImmutableArray<string> Chain,
    int Optimization,
    bool DebugInfo,
    ImmutableArray<string> Defines,
    ImmutableArray<string> SearchPaths,
    string OutputDir,
    string Rtti);

/// <summary>Resolves a configuration of a project to its effective values (configurations.md §3, §4).</summary>
public static class ConfigurationResolver
{
    /// <summary>The reset marker (CMP-1153): as the first entry of a configuration's list it discards what was accumulated before.</summary>
    internal const string ResetMarker = "!";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The effective values of <paramref name="configuration"/> (exact name).</summary>
    /// <exception cref="ArgumentException">The name is unknown, a parent is missing, or the <c>inherits</c> links form a cycle.</exception>
    public static EffectiveConfiguration Resolve(Project project, string configuration)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(configuration);
        var chain = ChainOf(project, configuration);

        var root = project.Configurations[chain[0]];
        var optimization = root.Optimization;
        var debugInfo = root.DebugInfo;
        string? rtti = null;
        var defines = new List<string>(project.Defines);
        var searchPaths = new List<string>(project.SearchPaths);
        for (var i = 0; i < chain.Length; i++)
        {
            var member = project.Configurations[chain[i]];
            if (i > 0 && !member.InheritsOptimization)
            {
                optimization = member.Optimization;
            }

            if (i > 0 && !member.InheritsDebugInfo)
            {
                debugInfo = member.DebugInfo;
            }

            rtti = member.Rtti ?? rtti;
            Append(defines, member.Defines);
            Append(searchPaths, member.SearchPaths);
        }

        return new EffectiveConfiguration(
            configuration,
            chain,
            optimization,
            debugInfo,
            [.. defines],
            [.. searchPaths],
            project.Configurations[configuration].OutputDir,
            rtti ?? project.Rtti ?? "broad");
    }

    /// <summary>Canonical JSON of the effective values: LF, 2-space indent, members in declaration order, final newline.</summary>
    public static string ToJson(EffectiveConfiguration effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            void Strings(string name, ImmutableArray<string> values)
            {
                w.WriteStartArray(name);
                foreach (var value in values)
                {
                    w.WriteStringValue(value);
                }

                w.WriteEndArray();
            }

            w.WriteStartObject();
            w.WriteString("name", effective.Name);
            Strings("chain", effective.Chain);
            w.WriteNumber("optimization", effective.Optimization);
            w.WriteBoolean("debugInfo", effective.DebugInfo);
            Strings("defines", effective.Defines);
            Strings("searchPaths", effective.SearchPaths);
            w.WriteString("outputDir", effective.OutputDir);
            w.WriteString("rtti", effective.Rtti);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>The chain of <paramref name="configuration"/>, root ancestor first.</summary>
    /// <exception cref="ArgumentException">Unknown name, missing parent or cycle.</exception>
    internal static ImmutableArray<string> ChainOf(Project project, string configuration)
    {
        if (!project.Configurations.ContainsKey(configuration))
        {
            throw new ArgumentException($"Unknown configuration '{configuration}'", nameof(configuration));
        }

        var chain = new List<string> { configuration };
        for (var parent = project.Configurations[configuration].Inherits; parent is not null; parent = project.Configurations[parent].Inherits)
        {
            if (!project.Configurations.ContainsKey(parent))
            {
                throw new ArgumentException($"Configuration '{chain[^1]}' inherits from unknown configuration '{parent}'", nameof(project));
            }

            if (chain.Contains(parent, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    "Circular configuration inheritance: " + string.Join(" -> ", chain.Skip(chain.IndexOf(parent)).Append(parent)), nameof(project));
            }

            chain.Add(parent);
        }

        chain.Reverse();
        return [.. chain];
    }

    /// <summary>One member's list (CMP-1160): a leading reset marker empties what was accumulated, then the entries are appended.</summary>
    internal static void Append(List<string> accumulated, ImmutableArray<string> own)
    {
        var skip = 0;
        if (!own.IsDefaultOrEmpty && own[0] == ResetMarker)
        {
            accumulated.Clear();
            skip = 1;
        }

        if (!own.IsDefault)
        {
            accumulated.AddRange(own.Skip(skip).Where(entry => entry != ResetMarker));
        }
    }
}
