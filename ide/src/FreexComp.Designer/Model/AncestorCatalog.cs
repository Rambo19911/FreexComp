namespace FreexComp.Designer.Model;

/// <summary>
/// One designed class the designer can inherit from (frames-designer.md §2, form-inheritance-designer.md §2): a frame that
/// <c>inline</c> objects resolve against or an ancestor of a descendant document — the class, the unit that declares it,
/// its <c>.form</c> path and the parsed document (parsed with the same catalogs, so nested frames and ancestors resolve).
/// </summary>
public sealed record AncestorDefinition(string ClassName, string UnitName, string FormPath, FormDocument Document);

/// <summary>The classes named by <see cref="IAncestorCatalog.Changed"/>: definitions added, changed or removed.</summary>
public sealed class AncestorCatalogChangedEventArgs(IReadOnlyList<string> classNames) : EventArgs
{
    public IReadOnlyList<string> ClassNames { get; } = classNames;
}

/// <summary>The frames or ancestors known to a document (DSG-FD-102, DSG-FD-122; the project implementation is D-120's).</summary>
public interface IAncestorCatalog
{
    /// <summary>Sorted by <see cref="AncestorDefinition.ClassName"/>, ordinal ignore-case.</summary>
    IReadOnlyList<AncestorDefinition> All { get; }

    /// <summary>The definition of <paramref name="className"/> (ASCII case-insensitive), or null.</summary>
    AncestorDefinition? Find(string className);

    /// <summary>Raised after definitions were added, changed or removed.</summary>
    event EventHandler<AncestorCatalogChangedEventArgs>? Changed;
}

/// <summary>
/// An in-memory catalog of frames or ancestors (DSG-FD-102): definitions are set and removed explicitly, and an applied change of a
/// definition's document raises <see cref="Changed"/> for its class (as the project catalog does, DSG-FD-104). While
/// <see cref="Changed"/> is being raised for a class, further changes of that class raise nothing, so classes whose
/// definitions refer to each other cannot notify each other forever.
/// </summary>
public sealed class AncestorCatalog : IAncestorCatalog
{
    private readonly SortedDictionary<string, AncestorDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FormDocument, string> _documents = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _raising = new(StringComparer.OrdinalIgnoreCase);
    private List<AncestorDefinition>? _all;

    public AncestorCatalog(params IEnumerable<AncestorDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            Add(definition);
        }
    }

    /// <summary>A catalog without frames that never changes: every <c>inline</c> object is an unknown class (<c>F0101</c>).</summary>
    public static IAncestorCatalog Empty { get; } = new EmptyAncestorCatalog();

    public IReadOnlyList<AncestorDefinition> All => _all ??= [.. _definitions.Values];

    /// <summary>
    /// DSG-SUR-232, DSG-TP-063: the class that would repeat if an instance of <paramref name="frameClass"/> were placed in a
    /// document of <paramref name="rootClass"/> (the frame is that class or contains it, directly or through other frames);
    /// null when the instance can be placed.
    /// </summary>
    public static string? RepeatedClass(IAncestorCatalog frames, string frameClass, string? rootClass)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(frameClass);
        return InheritedInstances.RepeatedClass(frames, frameClass, rootClass);
    }

    public event EventHandler<AncestorCatalogChangedEventArgs>? Changed;

    public AncestorDefinition? Find(string className) => _definitions.GetValueOrDefault(className);

    /// <summary>Adds or replaces the definition of its class and raises <see cref="Changed"/> for it.</summary>
    public void Set(AncestorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Detach(definition.ClassName);
        Add(definition);
        Raise(definition.ClassName);
    }

    /// <summary>Removes the definition of <paramref name="className"/> (raises <see cref="Changed"/> when there was one).</summary>
    public void Remove(string className)
    {
        if (Detach(className))
        {
            Raise(className);
        }
    }

    private void Add(AncestorDefinition definition)
    {
        _definitions[definition.ClassName] = definition;
        _all = null;
        if (_documents.TryAdd(definition.Document, definition.ClassName))
        {
            definition.Document.Changed += OnDocumentChanged;
        }
    }

    private bool Detach(string className)
    {
        if (!_definitions.Remove(className, out var old))
        {
            return false;
        }

        _all = null;
        if (_documents.Remove(old.Document))
        {
            old.Document.Changed -= OnDocumentChanged;
        }

        return true;
    }

    private void OnDocumentChanged(object? sender, FormChangedEventArgs e)
    {
        if (sender is FormDocument document && _documents.TryGetValue(document, out var className))
        {
            Raise(className);
        }
    }

    private void Raise(string className)
    {
        if (!_raising.Add(className))
        {
            return;
        }

        try
        {
            Changed?.Invoke(this, new AncestorCatalogChangedEventArgs([className]));
        }
        finally
        {
            _raising.Remove(className);
        }
    }

    private sealed class EmptyAncestorCatalog : IAncestorCatalog
    {
        public IReadOnlyList<AncestorDefinition> All => [];

        public AncestorDefinition? Find(string className) => null;

        public event EventHandler<AncestorCatalogChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
