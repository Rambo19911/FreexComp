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
    /// <summary>The stored value, or the metadata default. Throws when the path is unknown or names an object property.</summary>
    public static FormValue GetEffective(FormObject obj, string path)
    {
        if (obj.FindProperty(path) is { } stored)
        {
            return stored.Value;
        }

        var info = Resolve(obj, path);
        return info.Kind == PropertyKind.Object
            ? throw new ArgumentException($"{path} is an object property of {obj.TypeName}; only its sub-properties have values", nameof(path))
            : DefaultValue(obj.Catalog, info);
    }

    public static bool IsStored(FormObject obj, string path) => obj.FindProperty(path) is not null;

    /// <summary>True when <paramref name="value"/> equals the metadata default of the path (comparison table §2.1).</summary>
    public static bool IsDefault(FormObject obj, string path, FormValue value) => IsDefault(obj.Catalog, Resolve(obj, path), value);

    /// <summary>The §2.1 default comparison for a known property.</summary>
    public static bool IsDefault(ComponentCatalog catalog, PropertyInfo info, FormValue value)
    {
        switch (info.Kind)
        {
            case PropertyKind.Object:
                return false; // never stored itself
            case PropertyKind.Component:
                return value is IdentValue { Name: var n } && n.Equals("nil", StringComparison.OrdinalIgnoreCase);
            case PropertyKind.Collection:
                return value is CollectionValue { Items.Length: 0 };
            case PropertyKind.Enum:
                return value is IdentValue ident && DefaultValue(catalog, info) is IdentValue d
                    && ident.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase);
            case PropertyKind.Set:
                return value is SetValue set && DefaultValue(catalog, info) is SetValue defaultSet
                    && set.Elements.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(defaultSet.Elements);
        }

        var defaultValue = DefaultValue(catalog, info);
        switch (info.Type.ToLowerInvariant())
        {
            case "integer" or "int64" or "tshortcut":
                return value is IntegerValue i && i.Value == ((IntegerValue)defaultValue).Value;
            case "tcolor" or "tcursor":
                return SameNamedOrNumeric(info.Type, value, defaultValue);
            case "double":
                var expected = ((FloatValue)defaultValue).Value;
                return value is FloatValue f ? f.Value.Equals(expected) : value is IntegerValue iv && ((double)iv.Value).Equals(expected);
            case "boolean":
                return value is IdentValue b && b.Name.Equals(((IdentValue)defaultValue).Name, StringComparison.OrdinalIgnoreCase);
            case "tstrings":
                return value is StringListValue list && list.Lines.SequenceEqual(((StringListValue)defaultValue).Lines);
            default: // string, Char
                return value is StringValue s && string.Equals(s.Value, ((StringValue)defaultValue).Value, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The effective integer value (Left/Top/Width/Height and similar); a non-integer stored value counts as 0, and so does an
    /// unstored one of an object whose class the catalog does not know (no default to take, DSG-FD-027).
    /// </summary>
    public static int GetInt(FormObject obj, string path) =>
        (obj.Info is null && !IsStored(obj, path) ? null : GetEffective(obj, path)) is IntegerValue { Value: var v } ? unchecked((int)v) : 0;

    /// <summary>
    /// Left, Top, Width, Height; the root: (0, 0, ClientWidth, ClientHeight), where a root that stores only
    /// Width/Height shows Width − 16 × Height − 39 (DSG-FD-012).
    /// </summary>
    public static Rect GetBounds(FormObject obj)
    {
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
