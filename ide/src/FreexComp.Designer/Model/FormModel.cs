using FreexComp.Compiler.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Model;

/// <summary>The keyword of an object header.</summary>
public enum FormObjectKind { Object, Inherited, Inline }

/// <summary>What a change affected (DSG-FD-084), so that views repaint only what is needed.</summary>
[Flags]
public enum FormChangeFlags { None = 0, Geometry = 1, Appearance = 2, Structure = 4, Name = 8, Events = 16 }

public sealed class FormChangedEventArgs(IReadOnlyList<string> objectNames, FormChangeFlags flags) : EventArgs
{
    public IReadOnlyList<string> ObjectNames { get; } = objectNames;

    public FormChangeFlags Flags { get; } = flags;
}

/// <summary>A parsed <c>.form</c> file (form-document.md §1). Mutated only through the designer's editor (DSG-FD-001).</summary>
public sealed class FormDocument
{
    private readonly Dictionary<string, FormObject> _byName = new(StringComparer.OrdinalIgnoreCase);
    private List<FormDiagnostic> _diagnostics = [];

    internal FormDocument(FormObject root, string lineEnding)
    {
        Root = root;
        LineEnding = lineEnding;
        foreach (var obj in AllObjects())
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

    /// <summary>The object named <paramref name="name"/> anywhere in the tree (ASCII case-insensitive, O(1)).</summary>
    public FormObject? FindByName(string name) => _byName.GetValueOrDefault(name);

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

    internal void SetDiagnostics(IEnumerable<FormDiagnostic> diagnostics) => _diagnostics = [.. diagnostics];

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

    /// <summary>Null = unknown class (F0101).</summary>
    public ComponentInfo? Info { get; }

    /// <summary>True: unknown class, <see cref="Kind"/> other than <c>Object</c>, or inside a preserved object.</summary>
    public bool IsPreserved { get; internal set; }

    /// <summary>Verbatim source lines of a preserved object (LF, original indent removed).</summary>
    public string? PreservedText { get; internal set; }

    /// <summary>Span in the text it was parsed from (for diagnostics).</summary>
    public TextSpan SourceSpan { get; internal set; }

    /// <summary>The stored property with this path (ASCII case-insensitive, e.g. <c>Font.Size</c>), or null.</summary>
    public FormProperty? FindProperty(string path) =>
        _properties.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));

    internal void AddProperty(FormProperty property) => _properties.Add(property);

    internal void AddChild(FormObject child)
    {
        child.Parent = this;
        _children.Add(child);
    }
}

/// <summary>One stored property line (form-document.md §1).</summary>
public sealed class FormProperty
{
    internal FormProperty(string path, FormValue value, PropertyInfo? info, EventInfo? eventInfo, string? rawText, TextSpan sourceSpan)
    {
        Path = path;
        Value = value;
        Info = info;
        EventInfo = eventInfo;
        RawText = rawText;
        SourceSpan = sourceSpan;
    }

    /// <summary><c>Caption</c>, <c>Font.Size</c> – declared casing.</summary>
    public string Path { get; }

    public FormValue Value { get; }

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
}
