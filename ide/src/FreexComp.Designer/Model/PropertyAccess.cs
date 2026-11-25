using System.Collections.Immutable;
using System.Globalization;
using FreexComp.Designer.Catalog;

namespace FreexComp.Designer.Model;

/// <summary>
/// Typed access to the properties of a form object (form-document.md §2): the model holds only stored properties; an
/// absent property has its metadata default, or without one the type zero value (DSG-FD-004, DSG-FD-010).
/// </summary>
public static class PropertyAccess
{
    /// <summary>
    /// The stored value, else for an inherited object or a frame instance the frame definition's effective value
    /// (DSG-FD-106), else the metadata default. Throws when the path is unknown or names an object property.
    /// </summary>
    public static FormValue GetEffective(FormObject obj, string path)
    {
        for (var o = obj; o is not null; o = o.InheritedFrom)
        {
            if (o.FindProperty(path) is { } stored)
            {
                return stored.Value;
            }
        }

        var info = Resolve(obj, path);
        return info.Kind == PropertyKind.Object
            ? throw new ArgumentException($"{path} is an object property of {obj.TypeName}; only its sub-properties have values", nameof(path))
            : DefaultValue(obj.Catalog, info);
    }

    public static bool IsStored(FormObject obj, string path) => obj.FindProperty(path) is not null;

    /// <summary>
    /// False when <c>"stored": false</c> applies to <paramref name="path"/>: its own metadata (<paramref name="info"/>) or,
    /// for a dotted path, the object property it belongs to (component-metadata.md §3, DSG-FD-043).
    /// </summary>
    public static bool IsStoredPath(FormObject obj, string path, PropertyInfo info)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(info);
        var dot = path.IndexOf('.');
        return info.Stored && (dot < 0 || obj.Info?.FindProperty(path[..dot]) is not { Stored: false });
    }

    /// <summary>True when <paramref name="value"/> equals the metadata default of the path (comparison table §2.1).</summary>
    public static bool IsDefault(FormObject obj, string path, FormValue value) => IsDefault(obj.Catalog, Resolve(obj, path), value);

    /// <summary>The §2.1 default comparison for a known property.</summary>
    public static bool IsDefault(ComponentCatalog catalog, PropertyInfo info, FormValue value) =>
        info.Kind != PropertyKind.Object && SameValue(catalog, info, value, DefaultValue(catalog, info));

    /// <summary>
    /// True when two values of a property are equal by the comparison of §2.1 (used against the metadata default, and
    /// against the inherited value of a frame's object, DSG-FD-109).
    /// </summary>
    public static bool SameValue(ComponentCatalog catalog, PropertyInfo info, FormValue value, FormValue other)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(other);
        var defaultValue = other;
        switch (info.Kind)
        {
            case PropertyKind.Object:
                return false; // never stored itself
            case PropertyKind.Component:
                return value is IdentValue a && other is IdentValue b ? a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase) : value.Equals(other);
            case PropertyKind.Collection:
                return value.Equals(other);
            case PropertyKind.Enum:
                return value is IdentValue ident && other is IdentValue d && ident.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase);
            case PropertyKind.Set:
                return value is SetValue set && other is SetValue otherSet
                    && set.Elements.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(otherSet.Elements);
        }

        switch (info.Type.ToLowerInvariant())
        {
            case "integer" or "int64" or "tshortcut":
                return value is IntegerValue i && defaultValue is IntegerValue di && i.Value == di.Value;
            case "tcolor" or "tcursor":
                return SameNamedOrNumeric(info.Type, value, defaultValue);
            case "double":
                double? expected = defaultValue is FloatValue df ? df.Value : defaultValue is IntegerValue dl ? dl.Value : null;
                return expected is { } x && (value is FloatValue f ? f.Value.Equals(x) : value is IntegerValue iv && ((double)iv.Value).Equals(x));
            case "boolean":
                return value is IdentValue b && defaultValue is IdentValue db && b.Name.Equals(db.Name, StringComparison.OrdinalIgnoreCase);
            case "tstrings":
                return value is StringListValue list && defaultValue is StringListValue dlist && list.Lines.SequenceEqual(dlist.Lines);
            default: // string, Char
                return value is StringValue s && defaultValue is StringValue ds && string.Equals(s.Value, ds.Value, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// DSG-FD-117: the root of a frame document — a root whose class is of kind <c>container</c> — is a visual control
    /// (bounds <c>Left</c>, <c>Top</c>, <c>Width</c>, <c>Height</c>; no client size).
    /// </summary>
    public static bool IsFrameRoot(FormObject obj) => obj.Parent is null && !obj.IsPreserved && obj.Info?.Kind == ComponentKind.Container;

    /// <summary>
    /// The effective integer value (Left/Top/Width/Height and similar); a non-integer stored value counts as 0, and so does an
    /// unstored one of an object whose class the catalog does not know (no default to take, DSG-FD-027).
    /// </summary>
    public static int GetInt(FormObject obj, string path) =>
        (obj.Info is null && !IsStored(obj, path) ? null : GetEffective(obj, path)) is IntegerValue { Value: var v } ? unchecked((int)v) : 0;

    /// <summary>
    /// Left, Top, Width, Height; the root: (0, 0, ClientWidth, ClientHeight), where a root that stores only
    /// Width/Height shows Width − 16 × Height − 39 (DSG-FD-012). A frame root: (0, 0, Width, Height), a size it does not
    /// store being the class's default size (DSG-FD-117).
    /// </summary>
    public static Rect GetBounds(FormObject obj)
    {
        if (IsFrameRoot(obj))
        {
            var size = obj.Info!.DefaultSize;
            return new Rect(0, 0, IsStored(obj, "Width") ? GetInt(obj, "Width") : size.Width, IsStored(obj, "Height") ? GetInt(obj, "Height") : size.Height);
        }

        if (obj.Parent is null)
        {
            var width = !IsStored(obj, "ClientWidth") && obj.FindProperty("Width") is { Value: IntegerValue w } ? (int)w.Value - 16 : GetInt(obj, "ClientWidth");
            var height = !IsStored(obj, "ClientHeight") && obj.FindProperty("Height") is { Value: IntegerValue h } ? (int)h.Value - 39 : GetInt(obj, "ClientHeight");
            return new Rect(0, 0, width, height);
        }

        return new Rect(GetInt(obj, "Left"), GetInt(obj, "Top"), GetInt(obj, "Width"), GetInt(obj, "Height"));
    }

    /// <summary>The metadata default as a value, or the type zero value without one (DSG-FD-010).</summary>
    public static FormValue DefaultValue(ComponentCatalog catalog, PropertyInfo info)
    {
        var d = info.HasDefault ? info.Default : null;
        switch (info.Kind)
        {
            case PropertyKind.Component:
                return new IdentValue("nil");
            case PropertyKind.Collection:
                return new CollectionValue([]);
            case PropertyKind.Object:
                throw new ArgumentException($"{info.Name} is an object property", nameof(info));
            case PropertyKind.Enum:
                return new IdentValue(d as string ?? catalog.FindType(info.Type)?.Values.FirstOrDefault() ?? "");
            case PropertyKind.Set:
                return new SetValue(d is ImmutableArray<string> members ? members : []);
        }

        switch (info.Type.ToLowerInvariant())
        {
            case "integer" or "int64" or "tshortcut":
                return new IntegerValue(d is long l ? l : d is double x ? (long)x : 0, false);
            case "tcolor" or "tcursor":
                return d switch
                {
                    long n => new IntegerValue(n, false),
                    string s when s.StartsWith('$') => new IntegerValue(
                        unchecked((int)uint.Parse(s.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)), true),
                    string s => new IdentValue(s),
                    _ => new IntegerValue(0, false),
                };
            case "double":
                return new FloatValue(d is double dd ? dd : d is long ll ? ll : 0.0);
            case "boolean":
                return new IdentValue(d is true ? "True" : "False");
            case "tstrings":
                return new StringListValue(d is ImmutableArray<string> lines ? lines : []);
            case "char":
                return new StringValue(d is string { Length: > 0 } c ? c : "\0"); // "" is #0 (component-metadata.md §3)
            default:
                return new StringValue(d as string ?? "");
        }
    }

    private static bool SameNamedOrNumeric(string type, FormValue value, FormValue defaultValue)
    {
        if (value is IdentValue a && defaultValue is IdentValue b && a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Numeric(type, value) is { } x && Numeric(type, defaultValue) is { } y && x == y;
    }

    private static long? Numeric(string type, FormValue value) => value switch
    {
        IntegerValue i => i.Value,
        IdentValue ident when NamedValues.TryGet(type, ident.Name, out var v) => v,
        _ => null,
    };

    private static PropertyInfo Resolve(FormObject obj, string path) =>
        (obj.Info is { } info ? obj.Catalog.FindPropertyPath(info, path) : null)
        ?? throw new ArgumentException($"Unknown property {path} of {obj.TypeName}", nameof(path));
}
