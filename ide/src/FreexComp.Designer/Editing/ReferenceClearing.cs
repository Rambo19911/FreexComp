using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Editing;

/// <summary>
/// The changes that set every component reference to one of a set of components to <c>nil</c> (design-surface.md
/// DSG-SUR-131: delete; DSG-SUR-123: references a paste cannot resolve): plain names, <c>Form.Component</c> references
/// of this form, and references inside collection items. Owners in the set and preserved properties are left alone.
/// </summary>
public static class ReferenceClearing
{
    public static IReadOnlyList<FormChange> Changes(FormDocument document, IReadOnlySet<FormObject> removed)
    {
        var names = new HashSet<string>(removed.Select(o => o.Name), StringComparer.OrdinalIgnoreCase);
        var rootName = document.Root.Name;
        var changes = new List<FormChange>();
        foreach (var owner in document.AllObjects())
        {
            if (removed.Contains(owner) || owner.IsPreserved || owner.Info is null)
            {
                continue;
            }

            foreach (var property in owner.Properties)
            {
                if (property.IsPreserved || property.Info is not { } info)
                {
                    continue;
                }

                switch (Cleared(owner.Catalog, property.Value, info, names, rootName))
                {
                    case null:
                        break;
                    case CollectionValue collection:
                        changes.Add(new SetProperty(owner.QualifiedName, property.Path, collection));
                        break;
                    default:
                        changes.Add(new RemoveProperty(owner.QualifiedName, property.Path)); // the default of a reference is nil
                        break;
                }
            }
        }

        return changes;
    }

    /// <summary>Null: no reference to a removed component; a <see cref="CollectionValue"/>: the items without them; else: clear the property.</summary>
    private static FormValue? Cleared(ComponentCatalog catalog, FormValue value, PropertyInfo info, HashSet<string> names, string rootName)
    {
        switch (value)
        {
            case IdentValue { Name: var name } when info.Kind == PropertyKind.Component && names.Contains(name):
                return value;
            case QualifiedIdentValue { Parts: [var form, var component] } when info.Kind == PropertyKind.Component
                && form.Equals(rootName, StringComparison.OrdinalIgnoreCase) && names.Contains(component):
                return value;
            case CollectionValue collection when info.ItemClass is { } itemClass && catalog.FindType(itemClass) is { Kind: TypeKind.Object } itemType:
                var changed = false;
                var items = collection.Items.Select(item => new CollectionItem([.. item.Properties.Where(p =>
                {
                    var hit = itemType.Properties.FirstOrDefault(i => i.Name.Equals(p.Path, StringComparison.OrdinalIgnoreCase)) is { } itemInfo
                        && Cleared(catalog, p.Value, itemInfo, names, rootName) is { } r && r is not CollectionValue; // nested collections: not handled
                    changed |= hit;
                    return !hit;
                })])).ToList();
                return changed ? new CollectionValue([.. items]) : null;
            default:
                return null;
        }
    }
}
