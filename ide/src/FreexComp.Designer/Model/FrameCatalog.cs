namespace FreexComp.Designer.Model;

/// <summary>
/// One frame the designer can resolve <c>inline</c> objects against (frames-designer.md §2): the frame class, the unit
/// that declares it, its <c>.form</c> path and the parsed frame document (parsed with the same frame catalog, so nested
/// frames resolve).
/// </summary>
public sealed record FrameDefinition(string ClassName, string UnitName, string FormPath, FormDocument Document);

/// <summary>The frames named by <see cref="IFrameCatalog.Changed"/>: definitions added, changed or removed.</summary>
public sealed class FrameCatalogChangedEventArgs(IReadOnlyList<string> classNames) : EventArgs
{
    public IReadOnlyList<string> ClassNames { get; } = classNames;
}

/// <summary>The frames known to a document (DSG-FD-102; the project implementation is D-120's).</summary>
public interface IFrameCatalog
{
    /// <summary>Sorted by <see cref="FrameDefinition.ClassName"/>, ordinal ignore-case.</summary>
    IReadOnlyList<FrameDefinition> All { get; }

    /// <summary>The definition of <paramref name="className"/> (ASCII case-insensitive), or null.</summary>
    FrameDefinition? Find(string className);

    /// <summary>Raised after definitions were added, changed or removed.</summary>
    event EventHandler<FrameCatalogChangedEventArgs>? Changed;
}

/// <summary>
/// An in-memory frame catalog (DSG-FD-102): definitions are set and removed explicitly, and an applied change of a
/// definition's document raises <see cref="Changed"/> for its class (as the project catalog does, DSG-FD-104). While
/// <see cref="Changed"/> is being raised for a class, further changes of that class raise nothing, so frames whose
/// definitions refer to each other cannot notify each other forever.
/// </summary>
public sealed class FrameCatalog : IFrameCatalog
{
    private readonly SortedDictionary<string, FrameDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FormDocument, string> _documents = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _raising = new(StringComparer.OrdinalIgnoreCase);
    private List<FrameDefinition>? _all;

    public FrameCatalog(params IEnumerable<FrameDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            Add(definition);
        }
    }

    /// <summary>A catalog without frames that never changes: every <c>inline</c> object is an unknown class (<c>F0101</c>).</summary>
    public static IFrameCatalog Empty { get; } = new EmptyFrameCatalog();

    public IReadOnlyList<FrameDefinition> All => _all ??= [.. _definitions.Values];

    /// <summary>
    /// DSG-SUR-232, DSG-TP-063: the class that would repeat if an instance of <paramref name="frameClass"/> were placed in a
    /// document of <paramref name="rootClass"/> (the frame is that class or contains it, directly or through other frames);
    /// null when the instance can be placed.
    /// </summary>
    public static string? RepeatedClass(IFrameCatalog frames, string frameClass, string? rootClass)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(frameClass);
        return FrameInstances.RepeatedClass(frames, frameClass, rootClass);
    }

    public event EventHandler<FrameCatalogChangedEventArgs>? Changed;

    public FrameDefinition? Find(string className) => _definitions.GetValueOrDefault(className);

    /// <summary>Adds or replaces the definition of its class and raises <see cref="Changed"/> for it.</summary>
    public void Set(FrameDefinition definition)
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

    private void Add(FrameDefinition definition)
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
            Changed?.Invoke(this, new FrameCatalogChangedEventArgs([className]));
        }
        finally
        {
            _raising.Remove(className);
        }
    }

    private sealed class EmptyFrameCatalog : IFrameCatalog
    {
        public IReadOnlyList<FrameDefinition> All => [];

        public FrameDefinition? Find(string className) => null;

        public event EventHandler<FrameCatalogChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
