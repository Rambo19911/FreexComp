using System.Collections.Immutable;
using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Inspector;

/// <summary>
/// The working copy of the collection editor (object-inspector.md §5.7, DSG-OI-111 … 115): the items of one
/// <c>"kind": "collection"</c> property as detached item objects of the item class, edited by the embedded grid
/// (<see cref="Apply"/>), by <see cref="Add"/>, <see cref="Delete"/> and the moves; <see cref="CommitChange"/> is the one
/// change OK applies to the form. The form is never touched by this class.
/// </summary>
public sealed class CollectionEditorModel
{
    private readonly ComponentCatalog _catalog;
    private readonly List<FormObject> _items = [];
    private readonly ImmutableArray<(string Path, FormValue Value)>[] _original;
    private int _next;

    /// <exception cref="ArgumentException"><paramref name="propertyName"/> is not a collection property of the component.</exception>
    public CollectionEditorModel(ComponentCatalog catalog, FormObject component, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(propertyName);
        _catalog = catalog;
        Component = component;
        Property = component.Info?.FindProperty(propertyName) is { Kind: PropertyKind.Collection } property
            ? property
            : throw new ArgumentException($"{propertyName} is not a collection property of {component.Name}", nameof(propertyName));
        var type = Property.ItemClass is { } itemClass ? catalog.FindType(itemClass) : null;
        ItemClass = Property.ItemClass ?? "";
        ItemInfo = new ComponentInfo(ItemClass, null, "", false, ComponentKind.Control, null, default, "", false, "",
            type is { Kind: TypeKind.Object } ? type.Properties : [], [], [], [], [], null);
        var stored = component.FindProperty(Property.Name)?.Value as CollectionValue;
        foreach (var item in stored?.Items ?? [])
        {
            var obj = NewItem();
            foreach (var (path, value, raw) in item.Properties)
            {
                obj.AddProperty(new FormProperty(path, value, catalog.FindPropertyPath(ItemInfo, path), null, raw, default));
            }

            _items.Add(obj);
        }

        _original = [.. _items.Select(Values)];
        IsStored = stored is not null;
    }

    /// <summary>The component whose property is edited.</summary>
    public FormObject Component { get; }

    /// <summary>The collection property.</summary>
    public PropertyInfo Property { get; }

    /// <summary>The item class (<c>itemClass</c>).</summary>
    public string ItemClass { get; }

    /// <summary>The item class as a component description: its properties in declaration order, nothing else.</summary>
    public ComponentInfo ItemInfo { get; }

    /// <summary>The items of the working copy in collection order; each is a detached object of <see cref="ItemInfo"/>.</summary>
    public IReadOnlyList<FormObject> Items => _items;

    /// <summary>The working copy differs from the stored collection (property paths and values; value text ignored).</summary>
    public bool IsModified => _items.Count != _original.Length || _items.Where((o, i) => !Values(o).SequenceEqual(_original[i])).Any();

    private bool IsStored { get; }

    /// <summary>DSG-OI-111: <c>{index} – {text}</c>; text = the first non-empty string property, else the class name without <c>T</c>.</summary>
    public string ItemText(int index)
    {
        var item = _items[index];
        var text = ItemInfo.Properties
            .Where(p => p.Kind == PropertyKind.Simple && p.Type.Equals("string", StringComparison.OrdinalIgnoreCase))
            .Select(p => Effective(item, p) is StringValue { Value: { Length: > 0 } s } ? s : null)
            .FirstOrDefault(s => s is not null)
            ?? (ItemClass.StartsWith('T') ? ItemClass[1..] : ItemClass);
        return string.Create(CultureInfo.InvariantCulture, $"{index} – {text}");
    }

    /// <summary>DSG-OI-112 Add: a new item with no stored properties after the last one; returns its index.</summary>
    public int Add()
    {
        _items.Add(NewItem());
        return _items.Count - 1;
    }

    /// <summary>DSG-OI-112 Delete: removes the items; returns the index to select (the following item, else the previous; −1 when empty).</summary>
    public int Delete(IReadOnlyCollection<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        var valid = indices.Where(i => i >= 0 && i < _items.Count).Distinct().Order().ToList();
        if (valid.Count == 0)
        {
            return _items.Count == 0 ? -1 : 0;
        }

        foreach (var index in Enumerable.Reverse(valid))
        {
            _items.RemoveAt(index);
        }

        var first = valid[0];
        return _items.Count == 0 ? -1 : Math.Min(first, _items.Count - 1);
    }

    public bool CanMove(int index, int delta) => index >= 0 && index < _items.Count && index + delta >= 0 && index + delta < _items.Count;

    /// <summary>DSG-OI-112 Move Up (−1) / Move Down (+1); returns the new index.</summary>
    public int Move(int index, int delta)
    {
        if (!CanMove(index, delta))
        {
            return index;
        }

        (_items[index], _items[index + delta]) = (_items[index + delta], _items[index]);
        return index + delta;
    }

    /// <summary>
    /// DSG-OI-113: applies the grid's changes (<see cref="SetProperty"/> / <see cref="RemoveProperty"/> naming an item object)
    /// to the working copy. An edited value loses its original text; untouched values keep theirs (DSG-OI-115).
    /// </summary>
    public void Apply(IReadOnlyList<FormChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var change in changes)
        {
            switch (change)
            {
                case SetProperty set when Find(set.ObjectName) is { } item:
                    Remove(item, set.Path);
                    item.AddProperty(new FormProperty(set.Path, set.Value, _catalog.FindPropertyPath(ItemInfo, set.Path), null, null, default));
                    break;
                case RemoveProperty remove when Find(remove.ObjectName) is { } item:
                    Remove(item, remove.Path);
                    break;
                default:
                    throw new ArgumentException("not a change of an item: " + change, nameof(changes));
            }
        }
    }

    /// <summary>The working copy as a value (item properties in their current order; the writer orders them, DSG-OI-115).</summary>
    public CollectionValue ToValue() =>
        new([.. _items.Select(o => new CollectionItem([.. o.Properties.Select(p => (p.Path, p.Value, p.RawText))]))]);

    /// <summary>
    /// DSG-OI-114: the change OK applies — <c>SetProperty</c> with the working copy, <c>RemoveProperty</c> when it is empty
    /// (the default), null when nothing changed.
    /// </summary>
    public FormChange? CommitChange()
    {
        if (!IsModified)
        {
            return null;
        }

        if (_items.Count == 0)
        {
            return IsStored ? new RemoveProperty(Component.Name, Property.Name) : null;
        }

        return new SetProperty(Component.Name, Property.Name, ToValue());
    }

    private FormObject NewItem() =>
        new(FormObjectKind.Object, "Item" + (_next++).ToString(CultureInfo.InvariantCulture), ItemClass, ItemInfo, default) { Catalog = _catalog };

    private FormObject? Find(string name) => _items.FirstOrDefault(o => o.Name == name);

    private static void Remove(FormObject item, string path)
    {
        for (var i = item.Properties.Count - 1; i >= 0; i--)
        {
            if (item.Properties[i].Path.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                item.RemovePropertyAt(i);
            }
        }
    }

    private FormValue? Effective(FormObject item, PropertyInfo info) =>
        item.FindProperty(info.Name)?.Value ?? (info.HasDefault ? PropertyAccess.DefaultValue(_catalog, info) : null);

    private static ImmutableArray<(string Path, FormValue Value)> Values(FormObject item) =>
        [.. item.Properties.Select(p => (p.Path, p.Value))];
}
