using System.Collections.Immutable;
using FreexComp.Compiler.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Model;

/// <summary>The keyword of an object header.</summary>
public enum FormObjectKind { Object, Inherited, Inline }

/// <summary>What a change affected (DSG-FD-084), so that views repaint only what is needed.</summary>
[Flags]
public enum FormChangeFlags { None = 0, Geometry = 1, Appearance = 2, Structure = 4, Name = 8, Events = 16 }

public sealed class FormChangedEventArgs(IReadOnlyList<string> objectNames, FormChangeFlags flags, bool isRemerge = false) : EventArgs
{
    /// <summary>The objects affected; inherited objects of frame instances by their qualified names.</summary>
    public IReadOnlyList<string> ObjectNames { get; } = objectNames;

    public FormChangeFlags Flags { get; } = flags;

    /// <summary>
    /// True when frame instances were merged again with changed definitions (DSG-FD-111): the file model is unchanged, so
    /// the document is not modified and its undo stack stays valid.
    /// </summary>
    public bool IsRemerge { get; } = isRemerge;
}

/// <summary>A parsed <c>.form</c> file (form-document.md §1). Mutated only through the designer's editor (DSG-FD-001).</summary>
public sealed class FormDocument
{
    private readonly Dictionary<string, FormObject> _byName = new(StringComparer.OrdinalIgnoreCase);
    private List<FormDiagnostic> _diagnostics = [];

    /// <param name="fileScope">The objects that own their names, in file order (the parser's file scope); null: every object.</param>
    internal FormDocument(FormObject root, string lineEnding, IEnumerable<FormObject>? fileScope = null)
    {
        Root = root;
        LineEnding = lineEnding;
        foreach (var obj in fileScope ?? AllObjects().Where(o => !o.IsInherited))
        {
            _byName.TryAdd(obj.Name, obj); // DSG-FD-003: the first of duplicate names (F0106) owns the name
        }
    }

    /// <summary>The root object; never null after a successful parse.</summary>
    public FormObject Root { get; }

    /// <summary>Parse and validation results.</summary>
    public IReadOnlyList<FormDiagnostic> Diagnostics => _diagnostics;

    /// <summary>The line end of the parsed text (<c>"\n"</c> or <c>"\r\n"</c>); the writer always uses LF.</summary>
    public string LineEnding { get; }

    /// <summary>Incremented by every applied change.</summary>
    public int Version { get; private set; }

    /// <summary>Raised after each applied change.</summary>
    public event EventHandler<FormChangedEventArgs>? Changed;

    /// <summary>
    /// The object named <paramref name="name"/> in the file scope (ASCII case-insensitive, O(1)): the root, ordinary
    /// objects and frame instances, not the inherited objects inside instances (DSG-FD-108).
    /// </summary>
    public FormObject? FindByName(string name) => _byName.GetValueOrDefault(name);

    /// <summary>
    /// An object by its qualified name (DSG-FD-108): a file-scope name, then one name per frame scope, e.g.
    /// <c>BillingAddress.StreetEdit</c> or <c>BillingAddress.CityPicker.CityEdit</c>. A plain name is looked up like
    /// <see cref="FindByName"/>.
    /// </summary>
    public FormObject? FindByQualifiedName(string qualifiedName)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        var parts = qualifiedName.Split('.');
        var obj = FindByName(parts[0]);
        for (var i = 1; i < parts.Length && obj is not null; i++)
        {
            obj = obj.IsFrameInstance ? obj.FindInScope(parts[i]) : null;
        }

        return obj;
    }

    /// <summary>The frames instances were resolved with (null: none; <c>inline</c> objects are then <c>F0107</c>).</summary>
    public IAncestorCatalog? Frames { get; private set; }

    /// <summary>The weak link to <see cref="Frames"/>' <c>Changed</c> event (DSG-FD-111).</summary>
    internal InstanceSubscription? InstanceLink { get; private set; }

    /// <summary>The definition a resolved descendant document inherits from (DSG-FD-125); null for other documents.</summary>
    public AncestorDefinition? Ancestor => Root.IsInheritedRoot ? Root.InheritedSource : null;

    /// <summary>The ancestor catalog the document was parsed with (DSG-FD-122); null: none.</summary>
    public IAncestorCatalog? Ancestors { get; private set; }

    /// <summary>The ancestor class the root was parsed with (code sync's <c>class(…)</c>); a re-merge parses with it again.</summary>
    internal string? RootAncestorClass { get; private set; }

    /// <summary>The weak link to <see cref="Ancestors"/>' <c>Changed</c> event (DSG-FD-134).</summary>
    internal AncestorSubscription? AncestorLink { get; private set; }

    /// <summary>
    /// True for a document whose root is declared <c>inherited</c> and that follows an ancestor catalog — resolved or
    /// shown read-only (<c>F0112</c>) until its ancestor is listed: it is re-merged from its root (DSG-FD-134).
    /// </summary>
    internal bool FollowsAncestor => AncestorLink is not null && Root.Kind == FormObjectKind.Inherited;

    internal void AttachFrames(IAncestorCatalog? frames, ComponentCatalog catalog, IAncestorCatalog? ancestors = null, string? rootAncestorClass = null)
    {
        Frames = frames;
        Ancestors = ancestors;
        RootAncestorClass = rootAncestorClass;
        if (frames is not null)
        {
            InstanceLink = new InstanceSubscription(this, frames, catalog);
        }

        if (ancestors is not null && rootAncestorClass is not null)
        {
            AncestorLink = new AncestorSubscription(this, ancestors, catalog);
        }
    }

    /// <summary>
    /// DSG-FD-134: true when one of <paramref name="changed"/> is a class of the ancestor chain (the class the root was
    /// parsed with, then each resolved level's) or a frame contained anywhere in the merged tree.
    /// </summary>
    internal bool DependsOn(IReadOnlyCollection<string> changed)
    {
        bool Listed(string? className) => className is not null && changed.Contains(className, StringComparer.OrdinalIgnoreCase);

        if (Listed(RootAncestorClass))
        {
            return true;
        }

        var seen = new HashSet<FormDocument>(ReferenceEqualityComparer.Instance) { this };
        for (var level = Ancestor; level is not null && seen.Add(level.Document); level = level.Document.Ancestor)
        {
            if (Listed(level.ClassName) || Listed(level.Document.RootAncestorClass))
            {
                return true;
            }
        }

        return ContainsFrame(changed);
    }

    /// <summary>True when an instance anywhere in the tree (inherited copies included) is of one of <paramref name="changed"/>.</summary>
    internal bool ContainsFrame(IReadOnlyCollection<string> changed) =>
        AllObjects().Any(o => (o.Frame?.ClassName ?? (o.Kind == FormObjectKind.Inline ? o.TypeName : null)) is { } frame
            && changed.Contains(frame, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// DSG-FD-134: takes over the tree of <paramref name="parsed"/> (this document's text parsed again): the root object
    /// stays and receives the new contents, names, parser diagnostics and line map.
    /// </summary>
    internal void ReplaceTree(FormDocument parsed)
    {
        Root.ReplaceContents(parsed.Root);
        Root.SourceSpan = parsed.Root.SourceSpan;
        Root.HeaderSpan = parsed.Root.HeaderSpan;
        FormObject Map(FormObject obj) => ReferenceEquals(obj, parsed.Root) ? Root : obj;
        _byName.Clear();
        foreach (var (name, obj) in parsed._byName)
        {
            _byName[name] = Map(obj);
        }

        ParseDiagnostics.Clear();
        ParseDiagnostics.AddRange(parsed.ParseDiagnostics.Select(d => d with { Owner = Map(d.Owner) }));
        Lines = parsed.Lines;
    }

    /// <summary>Every object in pre-order, the root first.</summary>
    public IEnumerable<FormObject> AllObjects()
    {
        var pending = new Stack<FormObject>([Root]);
        while (pending.TryPop(out var obj))
        {
            yield return obj;
            for (var i = obj.Children.Count - 1; i >= 0; i--)
            {
                pending.Push(obj.Children[i]);
            }
        }
    }

    /// <summary>The path the text was parsed from (diagnostics name it).</summary>
    internal string SourcePath { get; init; } = "";

    /// <summary>Line map of the last parsed text (validation diagnostics point into it, DSG-FD-071).</summary>
    internal SourceLines Lines { get; set; } = SourceLines.Empty;

    /// <summary>The parser's semantic diagnostics with the items they are about (kept by validation while those exist).</summary>
    internal List<ParseDiagnostic> ParseDiagnostics { get; } = [];

    internal void SetDiagnostics(IEnumerable<FormDiagnostic> diagnostics) => _diagnostics = [.. diagnostics];

    /// <summary>
    /// A warning at the value of <paramref name="property"/> of <paramref name="obj"/> in the last parsed text: the form
    /// diagnostics code sync reports (<c>F0104</c>, <c>F0108</c>; form-format.md §5, code-sync.md §9).
    /// </summary>
    public FormDiagnostic PropertyWarning(FormObject obj, FormProperty property, string code, string message)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(property);
        var (line, col) = Lines.LineCol(property.ValueSpan.Start);
        var (endLine, endCol) = Lines.LineCol(property.ValueSpan.End);
        return new FormDiagnostic(code, FreexComp.Compiler.DiagnosticSeverity.Warning, message, SourcePath, line, col, endLine, endCol, obj.Name, property.Path);
    }

    /// <summary>Adds the names of a subtree that joins the tree (an existing owner of a name keeps it).</summary>
    internal void Register(FormObject subtree)
    {
        foreach (var obj in subtree.Subtree().Where(o => !o.IsInherited))
        {
            _byName.TryAdd(obj.Name, obj);
        }
    }

    /// <summary>Removes the names a subtree that leaves the tree owns.</summary>
    internal void Unregister(FormObject subtree)
    {
        foreach (var obj in subtree.Subtree())
        {
            if (_byName.TryGetValue(obj.Name, out var owner) && ReferenceEquals(owner, obj))
            {
                _byName.Remove(obj.Name);
            }
        }
    }

    internal void Rename(FormObject obj, string newName)
    {
        if (_byName.TryGetValue(obj.Name, out var owner) && ReferenceEquals(owner, obj))
        {
            _byName.Remove(obj.Name);
        }

        obj.Name = newName;
        if (obj.Scope is null)
        {
            _byName[newName] = obj; // DSG-FD-108: the names of an instance scope are not file names (D-139)
        }
    }

    /// <summary>The object of a plain or qualified name (<see cref="FindByQualifiedName"/>).</summary>
    internal FormObject? Find(string name) => name.Contains('.', StringComparison.Ordinal) ? FindByQualifiedName(name) : FindByName(name);

    internal void RaiseChanged(FormChangedEventArgs args)
    {
        Version++;
        Changed?.Invoke(this, args);
    }
}

/// <summary>One object of the tree (form-document.md §1).</summary>
public sealed class FormObject
{
    private readonly List<FormProperty> _properties = [];
    private readonly List<FormObject> _children = [];
    private readonly List<(FormObject Block, string? After)> _preservedBlocks = [];

    internal FormObject(FormObjectKind kind, string name, string typeName, ComponentInfo? info, TextSpan sourceSpan)
    {
        Kind = kind;
        Name = name;
        TypeName = typeName;
        Info = info;
        SourceSpan = sourceSpan;
    }

    public FormObjectKind Kind { get; }

    /// <summary>Declared casing.</summary>
    public string Name { get; internal set; }

    /// <summary>Declared casing, e.g. <c>TButton</c>.</summary>
    public string TypeName { get; internal set; }

    public FormObject? Parent { get; private set; }

    /// <summary>File order as read.</summary>
    public IReadOnlyList<FormProperty> Properties => _properties;

    /// <summary>Creation order.</summary>
    public IReadOnlyList<FormObject> Children => _children;

    /// <summary>Null = unknown class (F0101). A resolved frame instance: the class of the frame's root (DSG-FD-106).</summary>
    public ComponentInfo? Info { get; internal set; }

    /// <summary>The frame definition of a resolved instance (an <c>inline</c> object, or a nested instance inside one).</summary>
    public AncestorDefinition? Frame { get; internal set; }

    /// <summary>True for a resolved frame instance: the scope of its inherited objects (DSG-FD-106, 108).</summary>
    public bool IsFrameInstance => Frame is not null;

    /// <summary>True for a component of a frame shown inside an instance (its override block is written, not the object).</summary>
    public bool IsInherited { get; internal set; }

    /// <summary>
    /// The object of the frame definition this one shows (inherited objects), or the definition's root (an instance); the
    /// source of every value that is not stored here.
    /// </summary>
    public FormObject? InheritedFrom { get; internal set; }

    /// <summary>True when this inherited object or instance stores a line for <paramref name="path"/> (an override).</summary>
    public bool IsOverridden(string path) => InheritedFrom is not null && FindProperty(path) is not null;

    /// <summary>
    /// DSG-OI-120, DSG-FD-135: the handler an earlier level assigns to <paramref name="eventName"/> of this inherited
    /// object, instance or inherited root — the method and the definition (frame or ancestor form) whose file stores it
    /// (the nearest level of the definition chain that stores one); null when no level assigns one.
    /// </summary>
    public (AncestorDefinition Frame, string Method)? InheritedHandler(string eventName)
    {
        for (var o = this; o.InheritedFrom is { } source; o = source)
        {
            if (o.MergedFrom() is not { } level)
            {
                return null;
            }

            if (source.FindProperty(eventName)?.Value is IdentValue { Name: var method })
            {
                return (level, method);
            }
        }

        return null;
    }

    /// <summary>
    /// The definition whose file stores <see cref="InheritedFrom"/>: the one merged at the nearest merge point at or
    /// above this object — an instance this file creates (not an inherited copy of one) or the root of a descendant.
    /// </summary>
    private AncestorDefinition? MergedFrom()
    {
        for (var p = this; p is not null; p = p.Parent)
        {
            if (p.IsInheritedRoot)
            {
                return p._ancestor;
            }

            if (p.IsFrameInstance && !p.IsInherited)
            {
                return p.Frame;
            }
        }

        return null;
    }

    private AncestorDefinition? _ancestor;

    /// <summary>True for the root of a resolved descendant document (DSG-FD-125): it inherits from the ancestor's root.</summary>
    public bool IsInheritedRoot => _ancestor is not null;

    /// <summary>
    /// DSG-FD-135: the definition of the level that supplies this object — for an inherited object the frame of its
    /// instance, else the document's ancestor; for an inherited root the ancestor; null for other objects.
    /// </summary>
    public AncestorDefinition? InheritedSource
    {
        get
        {
            if (IsInheritedRoot)
            {
                return _ancestor;
            }

            if (!IsInherited)
            {
                return null;
            }

            if (InheritedFrame is { } frame)
            {
                return frame;
            }

            var root = this;
            while (root.Parent is { } parent)
            {
                root = parent;
            }

            return root._ancestor;
        }
    }

    /// <summary>DSG-FD-132: false for a frame instance and every object inside one; new components go anywhere else.</summary>
    public bool AcceptsNewChildren => !IsFrameInstance && Scope is null;

    /// <summary>Makes this root a resolved descendant of <paramref name="ancestor"/> (DSG-FD-125).</summary>
    internal void SetAncestor(AncestorDefinition ancestor)
    {
        _ancestor = ancestor;
        InheritedFrom = ancestor.Document.Root;
        Info = ancestor.Document.Root.Info;
    }

    /// <summary>
    /// The definition's effective value of <paramref name="path"/> (stored there, or the metadata default); for an event the
    /// definition's handler or null. Null for objects that inherit nothing.
    /// </summary>
    public FormValue? InheritedValue(string path)
    {
        if (InheritedFrom is not { } definition)
        {
            return null;
        }

        for (var o = definition; o is not null; o = o.InheritedFrom)
        {
            if (o.FindProperty(path) is { } stored)
            {
                return stored.Value;
            }
        }

        if (definition.Info is not { } info || (!path.Contains('.', StringComparison.Ordinal) && info.FindEvent(path) is not null))
        {
            return null;
        }

        return definition.Catalog.FindPropertyPath(info, path) is { Kind: not PropertyKind.Object } property
            ? PropertyAccess.DefaultValue(definition.Catalog, property)
            : null;
    }

    /// <summary>
    /// The name that reaches this object from the file scope: its own name, or for an inherited object the qualified
    /// name of its scope instance plus its name (<c>BillingAddress.StreetEdit</c>).
    /// </summary>
    public string QualifiedName => IsInherited && Scope is { } scope ? scope.QualifiedName + "." + Name : Name;

    /// <summary>The frame an inherited object comes from (the frame of its scope instance); null for other objects.</summary>
    public AncestorDefinition? InheritedFrame => IsInherited ? Scope?.Frame : null;

    /// <summary>The instance whose scope an inherited object belongs to (the nearest instance above it), else null.</summary>
    internal FormObject? Scope
    {
        get
        {
            for (var p = Parent; p is not null; p = p.Parent)
            {
                if (p.IsFrameInstance)
                {
                    return p;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Preserved blocks read inside an instance or an override block (<c>F0109</c>, <c>F0110</c>, DSG-FD-107): kept as
    /// verbatim text and written back where they were read; they are not children and take no part in layout.
    /// </summary>
    public IReadOnlyList<FormObject> PreservedBlocks => [.. _preservedBlocks.Select(b => b.Block)];

    /// <summary>The blocks with the name of the inherited child whose block preceded each in the file (null: first).</summary>
    internal IReadOnlyList<(FormObject Block, string? After)> PreservedBlockSlots => _preservedBlocks;

    internal void AddPreservedBlock(FormObject block, string? after)
    {
        block.Parent = this;
        _preservedBlocks.Add((block, after));
    }

    /// <summary>
    /// The inherited object named <paramref name="name"/> in this instance's scope: its descendants, not looking into
    /// nested instances (they are scopes of their own) — the components the frame owns.
    /// </summary>
    internal FormObject? FindInScope(string name)
    {
        var pending = new Stack<FormObject>(_children);
        while (pending.TryPop(out var obj))
        {
            if (string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return obj;
            }

            if (!obj.IsFrameInstance)
            {
                foreach (var child in obj._children)
                {
                    pending.Push(child);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Takes over the contents of <paramref name="other"/> (a re-parsed copy of this instance, DSG-FD-111, or of this
    /// document's root, DSG-FD-134).
    /// </summary>
    internal void ReplaceContents(FormObject other)
    {
        _ancestor = other._ancestor;
        Info = other.Info;
        Frame = other.Frame;
        InheritedFrom = other.InheritedFrom;
        IsPreserved = other.IsPreserved;
        PreservedText = other.PreservedText;
        Catalog = other.Catalog;
        _properties.Clear();
        _properties.AddRange(other._properties);
        foreach (var child in _children)
        {
            child.Parent = null;
        }

        _children.Clear();
        foreach (var child in other._children.ToList())
        {
            AddChild(child);
        }

        _preservedBlocks.Clear();
        foreach (var (block, after) in other._preservedBlocks)
        {
            AddPreservedBlock(block, after);
        }
    }

    /// <summary>True: unknown class, <see cref="Kind"/> other than <c>Object</c>, or inside a preserved object.</summary>
    public bool IsPreserved { get; internal set; }

    /// <summary>Verbatim source lines of a preserved object (LF, original indent removed).</summary>
    public string? PreservedText { get; internal set; }

    /// <summary>Span in the text it was parsed from (for diagnostics).</summary>
    public TextSpan SourceSpan { get; internal set; }

    /// <summary>The header from the keyword to the end of the type name (position of parent diagnostics).</summary>
    internal TextSpan HeaderSpan { get; set; }

    /// <summary>The catalog the object was bound with (sub-property and type lookups).</summary>
    internal ComponentCatalog Catalog { get; set; } = ComponentCatalog.Empty;

    /// <summary>The stored property with this path (ASCII case-insensitive, e.g. <c>Font.Size</c>), or null.</summary>
    public FormProperty? FindProperty(string path) =>
        _properties.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));

    internal void AddProperty(FormProperty property) => _properties.Add(property);

    internal void InsertProperty(int index, FormProperty property) => _properties.Insert(Math.Clamp(index, 0, _properties.Count), property);

    internal void RemovePropertyAt(int index) => _properties.RemoveAt(index);

    internal void AddChild(FormObject child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    internal void InsertChild(int index, FormObject child)
    {
        child.Parent = this;
        _children.Insert(index, child);
    }

    /// <summary>Detaches <paramref name="child"/>; returns its former index.</summary>
    internal int RemoveChild(FormObject child)
    {
        var index = _children.IndexOf(child);
        _children.RemoveAt(index);
        child.Parent = null;
        return index;
    }

    /// <summary>This object and its descendants in pre-order.</summary>
    internal IEnumerable<FormObject> Subtree()
    {
        yield return this;
        foreach (var child in _children)
        {
            foreach (var descendant in child.Subtree())
            {
                yield return descendant;
            }
        }
    }
}

/// <summary>A parser diagnostic about <paramref name="Owner"/> (and <paramref name="Property"/> when about a property line).</summary>
internal sealed record ParseDiagnostic(FormDiagnostic Diagnostic, FormObject Owner, FormProperty? Property);

/// <summary>One stored property line (form-document.md §1).</summary>
public sealed class FormProperty
{
    internal FormProperty(string path, FormValue value, PropertyInfo? info, EventInfo? eventInfo, string? rawText, TextSpan sourceSpan,
        ImmutableArray<(int Level, string Text)> rawLines = default)
    {
        RawLines = rawLines.IsDefault && rawText is not null ? [(0, rawText)] : rawLines;
        Path = path;
        Value = value;
        Info = info;
        EventInfo = eventInfo;
        RawText = rawText;
        SourceSpan = sourceSpan;
    }

    /// <summary><c>Caption</c>, <c>Font.Size</c> – declared casing.</summary>
    public string Path { get; }

    public FormValue Value { get; internal set; }

    /// <summary>The metadata of a non-event property (for a dotted path: the sub-property); null when unknown or an event.</summary>
    public PropertyInfo? Info { get; }

    /// <summary>The metadata of an event property, or null.</summary>
    public EventInfo? EventInfo { get; }

    public bool IsEvent => EventInfo is not null;

    /// <summary>Unknown property or F0103; the value is kept as raw text.</summary>
    public bool IsPreserved { get; internal set; }

    /// <summary>Exact value text as read (all lines, LF), null for values set by the designer.</summary>
    public string? RawText { get; }

    public TextSpan SourceSpan { get; }

    /// <summary>The value text of <see cref="SourceSpan"/> (position of value diagnostics).</summary>
    internal TextSpan ValueSpan { get; init; }

    /// <summary>
    /// The non-blank lines of <see cref="RawText"/>, trimmed, each with its nesting level relative to the property line
    /// (in units of 2 spaces), so that the writer re-indents an untouched value canonically (DSG-FD-046). Default when
    /// <see cref="RawText"/> is null.
    /// </summary>
    internal ImmutableArray<(int Level, string Text)> RawLines { get; }
}
