using System.Collections.Immutable;

namespace FreexComp.Designer.Catalog;

/// <summary>
/// The loaded component metadata (form-document.md §6). Lookups are ASCII case-insensitive and ignore a unit
/// qualifier. The built-in root class <c>TComponent</c> (component-metadata.md §2.1) is always present.
/// </summary>
public sealed class ComponentCatalog
{
    /// <summary>Name of the built-in root class of every component chain.</summary>
    public const string RootClassName = "TComponent";

    private readonly Dictionary<string, ComponentInfo> _components = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TypeInfo> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _signatures = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="components">Flattened components in load order; a duplicate class name keeps the first.</param>
    public ComponentCatalog(IEnumerable<ComponentInfo> components, IEnumerable<TypeInfo> types,
        IReadOnlyDictionary<string, string> eventSignatures, IEnumerable<string> palettePages)
    {
        var all = new List<ComponentInfo> { BuiltInRoot };
        _components[RootClassName] = BuiltInRoot;
        foreach (var component in components)
        {
            if (_components.TryAdd(component.ClassName, component))
            {
                all.Add(component);
            }
        }

        foreach (var type in types)
        {
            _types.TryAdd(type.Name, type);
        }

        foreach (var (name, text) in eventSignatures)
        {
            _signatures.TryAdd(name, text);
        }

        All = all;
        PalettePages = [.. palettePages];
    }

    /// <summary>
    /// The built-in <c>TComponent</c>: abstract, contributes the property <c>Tag</c> (the pseudo-property <c>Name</c>
    /// is the object header, never a property).
    /// </summary>
    public static ComponentInfo BuiltInRoot { get; } = new(RootClassName, null, "Classes", true, ComponentKind.NonVisual,
        null, default, "Component", false, "generic",
        [
            new PropertyInfo("Tag", "Int64", PropertyKind.Simple, 0L, true, "Data", null, false, false, null, null, null,
                false, null, null, RootClassName, true, false, null),
        ],
        [], [], [], [], null);

    /// <summary>A catalog with only the built-in <c>TComponent</c> (DSG-TP-004); declared after <see cref="BuiltInRoot"/>, which it uses.</summary>
    public static ComponentCatalog Empty { get; } = new([], [], new Dictionary<string, string>(), []);

    /// <summary>Every component, the built-in root first, then in load order.</summary>
    public IReadOnlyList<ComponentInfo> All { get; }

    /// <summary>Tool Palette page order (component-metadata.md §1: the roots' lists concatenated).</summary>
    public ImmutableArray<string> PalettePages { get; }

    public ComponentInfo? Find(string className) => _components.GetValueOrDefault(StripUnit(className));

    public TypeInfo? FindType(string typeName) => _types.GetValueOrDefault(StripUnit(typeName));

    /// <summary>The text of a signature type, e.g. <c>procedure(Sender: TObject)</c>.</summary>
    public string? FindEventSignature(string name) => _signatures.GetValueOrDefault(name);

    /// <summary>True when <paramref name="fromClass"/> is <paramref name="toClass"/> or one of its metadata descendants.</summary>
    public bool IsAssignable(string fromClass, string toClass)
    {
        var target = Find(toClass);
        if (target is null)
        {
            return false;
        }

        // The loader rejects cycles; the step limit only guards hand-built catalogs.
        var steps = 0;
        for (var current = Find(fromClass); current is not null && steps <= _components.Count; current = current.Ancestor is { } a ? Find(a) : null, steps++)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }
        }

        return false;
    }

    private static string StripUnit(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }
}
