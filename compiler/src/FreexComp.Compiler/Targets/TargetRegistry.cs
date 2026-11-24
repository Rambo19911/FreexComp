using System.Collections.Immutable;

namespace FreexComp.Compiler.Targets;

/// <summary>The closed, immutable set of targets that can be named on a command line or in a project file (CMP-1004).</summary>
public static class TargetRegistry
{
    public static ImmutableArray<TargetInfo> BuiltIn { get; } = [TargetInfo.Win64];

    /// <summary>The baseline target.</summary>
    public static TargetInfo Default => TargetInfo.Win64;

    /// <summary>The ids of <see cref="BuiltIn"/> in registry order, joined with <c>", "</c> (messages, <c>opc --version</c>).</summary>
    internal static string IdList => string.Join(", ", BuiltIn.Select(t => t.Id));

    /// <summary>
    /// CMP-1020: the object directory of <paramref name="configuration"/> below <paramref name="directory"/> — <c>obj\&lt;config&gt;</c>
    /// for the baseline target (unchanged), <c>obj\&lt;target id&gt;\&lt;config&gt;</c> for any other, so caches never mix targets.
    /// </summary>
    internal static string ObjectDirectory(string directory, TargetInfo target, string configuration) =>
        ReferenceEquals(target, Default) || target.Id == Default.Id
            ? Path.Combine(directory, "obj", configuration)
            : Path.Combine(directory, "obj", target.Id, configuration);

    /// <summary>Ordinal, case-sensitive.</summary>
    public static bool TryGet(string id, out TargetInfo target)
    {
        foreach (var candidate in BuiltIn)
        {
            if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                target = candidate;
                return true;
            }
        }

        target = Default;
        return false;
    }
}
