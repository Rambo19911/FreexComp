using System.Collections.Immutable;
using System.Text;

namespace FreexComp.Build;

/// <summary>The result of <see cref="EventMacros.Expand"/>.</summary>
/// <param name="Text">The command with every known macro reference replaced and every <c>$$(</c> turned into <c>$(</c>.</param>
/// <param name="UnknownNames">The names of the references that have no value, as written, in order of first appearance (each once, ignoring case); such a reference stays in <paramref name="Text"/> as it was.</param>
public sealed record MacroExpansion(string Text, ImmutableArray<string> UnknownNames);

/// <summary>
/// The macros of build event commands (build-events.md CMP-1256…1258): <c>$(Name)</c> with a name matching
/// <c>[A-Za-z_][A-Za-z0-9_]*</c>, compared ignoring case; <c>$$(</c> is a literal <c>$(</c>. The only implementation: the build
/// engine and the IDE both use it.
/// </summary>
public static class EventMacros
{
    /// <summary>The macros that exist, in the order they are shown.</summary>
    public static ImmutableArray<string> Names { get; } = ["ProjectDir", "ProjectName", "Config", "Platform", "OutputDir", "OutputPath"];

    /// <summary>
    /// The values for one project build (CMP-1257); paths are full, with <c>\</c>, without a trailing separator except a drive root.
    /// The keys compare ignoring case.
    /// </summary>
    /// <param name="configuration">The configuration being built, as spelled in the project file.</param>
    /// <param name="platform">The effective target id.</param>
    /// <exception cref="ArgumentException">The project has no file path or the configuration does not exist.</exception>
    public static ImmutableDictionary<string, string> Values(Project project, string configuration, string platform)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(platform);
        var output = FullPath(ProjectOptions.GetOutputPath(project, configuration, platform));     // CMP-1371: of that platform
        return ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase,
        [
            KeyValuePair.Create("ProjectDir", FullPath(project.Directory)),
            KeyValuePair.Create("ProjectName", project.Name),
            KeyValuePair.Create("Config", configuration),
            KeyValuePair.Create("Platform", platform),
            KeyValuePair.Create("OutputDir", FullPath(Path.GetDirectoryName(output) ?? output)),
            KeyValuePair.Create("OutputPath", output),
        ]);
    }

    /// <summary>
    /// One left-to-right pass over <paramref name="command"/> (CMP-1258): an inserted value is not scanned again and nothing is
    /// quoted, escaped or trimmed. Any other use of <c>$</c> is left unchanged.
    /// </summary>
    /// <param name="values">Macro name → value; a name is looked up ignoring case whatever comparer the dictionary has.</param>
    public static MacroExpansion Expand(string command, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(values);
        var text = new StringBuilder(command.Length);
        var unknown = ImmutableArray.CreateBuilder<string>();
        var i = 0;
        while (i < command.Length)
        {
            if (command[i] != '$')
            {
                text.Append(command[i++]);
            }
            else if (string.CompareOrdinal(command, i, "$$(", 0, 3) == 0)
            {
                text.Append("$(");                                                     // the escape: never the start of a reference
                i += 3;
            }
            else if (ReferenceAt(command, i) is { } name)
            {
                var reference = command.AsSpan(i, name.Length + 3);
                if (Lookup(values, name) is { } value)
                {
                    text.Append(value);
                }
                else
                {
                    text.Append(reference);
                    if (!unknown.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        unknown.Add(name);
                    }
                }

                i += reference.Length;
            }
            else
            {
                text.Append(command[i++]);
            }
        }

        return new MacroExpansion(text.ToString(), unknown.ToImmutable());
    }

    /// <summary>The name of the reference <c>$(Name)</c> that starts at <paramref name="at"/>, or null when there is none.</summary>
    private static string? ReferenceAt(string command, int at)
    {
        if (at + 1 >= command.Length || command[at + 1] != '(')
        {
            return null;
        }

        var end = at + 2;
        while (end < command.Length && (char.IsAsciiLetterOrDigit(command[end]) || command[end] == '_'))
        {
            end++;
        }

        return end > at + 2 && end < command.Length && command[end] == ')' && !char.IsAsciiDigit(command[at + 2]) ? command[(at + 2)..end] : null;
    }

    private static string? Lookup(IReadOnlyDictionary<string, string> values, string name)
    {
        if (values.TryGetValue(name, out var exact))
        {
            return exact;
        }

        foreach (var (key, value) in values)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>As <see cref="Path.GetFullPath(string)"/>, without a trailing separator except for a drive root.</summary>
    private static string FullPath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.Length > (Path.GetPathRoot(full)?.Length ?? 0) ? full.TrimEnd('\\') : full;
    }
}
