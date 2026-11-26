using System.Collections.Immutable;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Editing;

/// <summary>
/// The objects a component reference in a line of a <see cref="FormDocument"/> names, by the scope rules of the line's
/// owner (RTL-1093, RTL-1104, DSG-FD-108): used by rename (D-141), delete and paste (D-142).
/// </summary>
internal static class ReferenceLookup
{
    /// <summary>
    /// The scope the lines of <paramref name="owner"/> resolve names in first (RTL-1093): an instance's own lines and the
    /// lines of the inherited objects of its scope; null for the file scope (also the inherited objects of a descendant's
    /// file scope, DSG-FD-126).
    /// </summary>
    public static FormObject? Scope(FormObject owner) => owner.IsFrameInstance ? owner : owner.IsInherited ? owner.Scope : null;

    /// <summary>
    /// The object a plain name in a line whose reference scope is <paramref name="scope"/> names (RTL-1093, RTL-1104): the
    /// innermost scope (its instance by name, then its components), then the file scope (the root by name included).
    /// </summary>
    public static FormObject? ResolvePlain(FormDocument document, FormObject? scope, string name) =>
        ResolveInScope(scope, name) ?? document.FindByName(name);

    /// <summary>The object a plain name names in the innermost scope <paramref name="scope"/>: the instance by name, then its components.</summary>
    public static FormObject? ResolveInScope(FormObject? scope, string name) =>
        scope is null ? null : Same(scope.Name, name) ? scope : scope.FindInScope(name);

    /// <summary>
    /// The index of the first part of the qualified name <paramref name="parts"/> (in a line whose reference scope is
    /// <paramref name="scope"/>) that reaches an object <paramref name="match"/> accepts, or null. The first part is
    /// resolved like a plain name (this file's root name names the root; a frame's header name is no name of a placing
    /// file, also in an instance's lines: RTL-1104); each further part is a component of the root (the file scope) or of
    /// an instance's scope (DSG-FD-108).
    /// </summary>
    public static int? PartReaching(FormDocument document, FormObject? scope, ImmutableArray<string> parts, Func<FormObject, bool> match)
    {
        var i = 0;
        foreach (var current in Walk(document, scope, parts))
        {
            if (match(current))
            {
                return i;
            }

            i++;
        }

        return null;
    }

    /// <summary>The object the whole qualified name <paramref name="parts"/> names, or null when a part does not resolve.</summary>
    public static FormObject? ResolveQualified(FormDocument document, FormObject? scope, ImmutableArray<string> parts)
    {
        var resolved = Walk(document, scope, parts).ToList();
        return resolved.Count == parts.Length ? resolved[^1] : null;
    }

    /// <summary>The object each part of <paramref name="parts"/> reaches, in order, up to the first part that does not resolve.</summary>
    private static IEnumerable<FormObject> Walk(FormDocument document, FormObject? scope, ImmutableArray<string> parts)
    {
        var current = ResolvePlain(document, scope, parts[0]);
        for (var i = 0; current is not null; i++)
        {
            yield return current;
            if (i + 1 == parts.Length)
            {
                yield break;
            }

            current = ReferenceEquals(current, document.Root) ? document.FindByName(parts[i + 1])
                : current.IsFrameInstance ? current.FindInScope(parts[i + 1])
                : null;
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
