using System.Collections.Immutable;

namespace FreexComp.Designer.Catalog;

/// <summary>Metadata <c>kind</c> of a component (component-metadata.md §2.1).</summary>
public enum ComponentKind { Control, WinControl, Container, NonVisual, Form }

/// <summary>How a property's value is shaped (component-metadata.md §3).</summary>
public enum PropertyKind { Simple, Enum, Set, Object, Component, Collection }

/// <summary>Kind of an entry under <c>types</c> (component-metadata.md §4).</summary>
public enum TypeKind { Enum, Set, Object }

/// <summary>The <c>palette</c> object of a component; <see cref="Icon"/> is resolved against the metadata file.</summary>
public sealed record PaletteInfo(string Page, int Order, string? Icon);

/// <summary>One designer verb (component-metadata.md §7).</summary>
public sealed record DesignerVerb(string Id, string Caption, string Command, string? Arg);

/// <summary>
/// A published non-event property. <see cref="Default"/> holds the JSON default converted to <c>long</c>, <c>double</c>,
/// <c>bool</c>, <c>string</c>, <c>ImmutableArray&lt;string&gt;</c> (sets, string lists) or null (<c>null</c> / none);
/// <see cref="HasDefault"/> tells whether the key was present.
/// </summary>
public sealed record PropertyInfo(string Name, string Type, PropertyKind Kind, object? Default, bool HasDefault,
    string Category, string? Initial, bool ReadOnly, bool Hidden, string? Editor, double? Min, double? Max,
    bool AffectsLayout, string? ItemClass, string? Description, string DeclaringClass,
    bool Stored, bool Localizable, IReadOnlyDictionary<string, object>? EditorOptions);

/// <summary>A published event (component-metadata.md §5); <see cref="Signature"/> is the signature type name.</summary>
public sealed record EventInfo(string Name, string Signature, bool IsDefault, string DeclaringClass);

/// <summary>An enum, set or object class declared under <c>types</c>; object properties are flattened ancestor-first.</summary>
public sealed record TypeInfo(string Name, TypeKind Kind, ImmutableArray<string> Values, string? ElementType,
    string? Ancestor, ImmutableArray<PropertyInfo> Properties);

/// <summary>How the component key <c>baseline</c> places the text baseline (component-metadata.md §2.1; DSG-SUR-207).</summary>
public enum BaselineAlign { None, Top, Center }

/// <summary>
/// The design-time text baseline of a component from its metadata (DSG-SUR-207, 208): <c>"none"</c>, top with an inset
/// (0 … 255), or centred. <see cref="Inset"/> is 0 unless <see cref="Align"/> is <see cref="BaselineAlign.Top"/>.
/// </summary>
public sealed record BaselineRule(BaselineAlign Align, int Inset = 0)
{
    /// <summary>
    /// DSG-SUR-208: the baseline below the control's top edge — top: <c>inset + Ascent</c>; center:
    /// <c>(height − LineHeight) div 2 + Ascent</c>; none: null (no baseline).
    /// </summary>
    public int? Offset(Rendering.TextMetrics metrics, int height) => Align switch
    {
        BaselineAlign.Top => Inset + metrics.Ascent,
        BaselineAlign.Center => (height - metrics.LineHeight) / 2 + metrics.Ascent,
        _ => null,
    };
}

/// <summary>A component class with its flattened (ancestor-first) property and event lists (form-document.md §6).</summary>
public sealed record ComponentInfo(string ClassName, string? Ancestor, string Unit, bool IsAbstract,
    ComponentKind Kind, PaletteInfo? Palette, Size DefaultSize, string NamePrefix, bool AcceptsChildren,
    string RendererId, ImmutableArray<PropertyInfo> Properties, ImmutableArray<EventInfo> Events,
    ImmutableArray<string> ChildClasses, ImmutableArray<string> ParentClasses,
    ImmutableArray<DesignerVerb> DesignerVerbs, string? ComponentEditor)
{
    /// <summary>
    /// The effective <c>baseline</c> key (DSG-SUR-207): the class's own value, else the nearest ancestor's; null when
    /// the key is absent in the whole chain — then the design renderer decides (DSG-SUR-209).
    /// </summary>
    public BaselineRule? Baseline { get; init; }

    /// <summary>The flattened property named <paramref name="name"/> (ASCII case-insensitive), or null.</summary>
    public PropertyInfo? FindProperty(string name) =>
        Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The flattened event named <paramref name="name"/> (ASCII case-insensitive), or null.</summary>
    public EventInfo? FindEvent(string name) =>
        Events.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}
