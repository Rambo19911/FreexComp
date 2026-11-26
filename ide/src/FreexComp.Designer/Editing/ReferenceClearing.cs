using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Editing;

/// <summary>
/// The changes that set every component reference to one of a set of components to <c>nil</c> (design-surface.md
/// DSG-SUR-131: delete): the plain names and qualified names (any part: first, middle or last) that resolve to a removed
/// component by the scope rules of the line's owner (<see cref="ReferenceLookup"/>; D-142), also inside collection items.
/// Owners in the set and preserved properties are left alone.
/// </summary>
public static class ReferenceClearing
{
    public static IReadOnlyList<FormChange> Changes(FormDocument document, IReadOnlySet<FormObject> removed)
    {
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

                switch (Cleared(document, ReferenceLookup.Scope(owner), owner.Catalog, property.Value, info, removed))
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
    private static FormValue? Cleared(FormDocument document, FormObject? scope, ComponentCatalog catalog, FormValue value, PropertyInfo info,
        IReadOnlySet<FormObject> removed)
    {
        switch (value)
        {
            case IdentValue { Name: var name } when info.Kind == PropertyKind.Component
                && ReferenceLookup.ResolvePlain(document, scope, name) is { } target && removed.Contains(target):
                return value;
            case QualifiedIdentValue { Parts: var parts } when info.Kind == PropertyKind.Component
                && ReferenceLookup.PartReaching(document, scope, parts, removed.Contains) is not null:
                return value;
            case CollectionValue collection when info.ItemClass is { } itemClass && catalog.FindType(itemClass) is { Kind: TypeKind.Object } itemType:
                var changed = false;
                var items = collection.Items.Select(item => new CollectionItem([.. item.Properties.Where(p =>
                {
                    var hit = itemType.Properties.FirstOrDefault(i => i.Name.Equals(p.Path, StringComparison.OrdinalIgnoreCase)) is { } itemInfo
                        && Cleared(document, scope, catalog, p.Value, itemInfo, removed) is { } r && r is not CollectionValue; // nested collections: not handled
                    changed |= hit;
                    return !hit;
                })])).ToList();
                return changed ? new CollectionValue([.. items]) : null;
            default:
                return null;
        }
    }
}
