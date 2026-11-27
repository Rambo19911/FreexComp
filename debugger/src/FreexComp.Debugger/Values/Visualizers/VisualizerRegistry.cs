using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Values.Visualizers;

/// <summary>
/// The ordered visualiser table (DBG-501) with its name patterns (DBG-502). Validated when it is built: an invalid
/// pattern, a row with neither pattern nor kinds, or an id that occurs twice is an <see cref="ArgumentException"/>.
/// </summary>
internal sealed class VisualizerRegistry
{
    /// <summary>The table built into the library: the string visualiser first (DBG-515).</summary>
    public static VisualizerRegistry BuiltIn { get; } = new([StringVisualizer.Registration]);

    /// <summary>Selection groups within one candidate type, in this order (DBG-503).</summary>
    private enum Group { ExactName, GenericBase, KindsOnly }

    private readonly (VisualizerRegistration Registration, Group Group, string? Name)[] _rows;

    public VisualizerRegistry(IEnumerable<VisualizerRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        Registrations = [.. registrations];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        _rows = new (VisualizerRegistration, Group, string?)[Registrations.Count];
        for (var i = 0; i < Registrations.Count; i++)
        {
            var registration = Registrations[i];
            var id = registration.Visualizer.Id;
            if (!ids.Add(id))
                throw new ArgumentException($"Visualizer id '{id}' occurs more than once.", nameof(registrations));
            _rows[i] = registration.NamePattern switch
            {
                null when registration.Kinds is null =>
                    throw new ArgumentException($"Visualizer '{id}' has neither a name pattern nor kinds.", nameof(registrations)),
                null => (registration, Group.KindsOnly, null),
                var pattern => Parse(id, pattern) is { } genericBase
                    ? (registration, Group.GenericBase, genericBase)
                    : (registration, Group.ExactName, pattern),
            };
        }
    }

    public IReadOnlyList<VisualizerRegistration> Registrations { get; }

    public bool IsEmpty => _rows.Length == 0;

    /// <summary>The generic base of a <c>Base&lt;*&gt;</c> pattern; null for an exact name; an invalid pattern throws.</summary>
    private static string? Parse(string id, string pattern)
    {
        var star = pattern.IndexOf('*', StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(pattern) || (star >= 0 && (star != pattern.Length - 2 || !pattern.EndsWith("<*>", StringComparison.Ordinal)
            || pattern.Length == 3 || string.IsNullOrWhiteSpace(pattern[..^3]))))
            throw new ArgumentException($"Visualizer '{id}' has an invalid name pattern '{pattern}'.", "registrations");
        return star >= 0 ? pattern[..^3] : null;
    }

    /// <summary>
    /// DBG-503: for each candidate type in turn, the exact names, then the generic bases, then the kinds-only rows, each
    /// group in table order; the first row that holds, may be used (<paramref name="usable"/>) and whose
    /// <see cref="IValueVisualizer.Matches"/> is true.
    /// </summary>
    public VisualizerRegistration? Select(IEnumerable<PdiType> candidates, Func<VisualizerRegistration, PdiType, bool> usable)
    {
        foreach (var type in candidates)
        {
            foreach (var group in (ReadOnlySpan<Group>)[Group.ExactName, Group.GenericBase, Group.KindsOnly])
            {
                foreach (var (registration, rowGroup, name) in _rows)
                {
                    if (rowGroup == group && Holds(registration, rowGroup, name, type) && usable(registration, type))
                        return registration;
                }
            }
        }

        return null;
    }

    private static bool Holds(VisualizerRegistration registration, Group group, string? name, PdiType type) =>
        (registration.Kinds is null || registration.Kinds.Contains(type.Kind))
        && (registration.Unit is null || string.Equals(registration.Unit, type.Unit, StringComparison.OrdinalIgnoreCase))
        && group switch
        {
            Group.ExactName => string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase),
            Group.GenericBase => IsInstanceOf(type.Name, name!),
            _ => true,
        };

    /// <summary>
    /// DBG-502: <paramref name="name"/> is <paramref name="genericBase"/> followed by an argument list whose closing
    /// <c>&gt;</c> (the one that closes the first <c>&lt;</c>) ends the name.
    /// </summary>
    internal static bool IsInstanceOf(string name, string genericBase)
    {
        if (name.Length < genericBase.Length + 3 || !name.StartsWith(genericBase + "<", StringComparison.OrdinalIgnoreCase))
            return false;
        var depth = 0;
        for (var i = genericBase.Length; i < name.Length; i++)
        {
            if (name[i] == '<')
                depth++;
            else if (name[i] == '>' && --depth == 0)
                return i == name.Length - 1;
        }

        return false;
    }
}
