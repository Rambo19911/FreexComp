using System.Collections.Immutable;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Editing;

/// <summary>
/// D-140: rewrites undo units of a descendant to the new name of an inherited object that follows its ancestor's rename
/// (<see cref="DesignerUndoStack.FollowRename"/>). Object names in changes and selections are qualified or plain names: the
/// renamed one and the names below it (the components of a renamed instance) take the new name. A component reference
/// (plain or qualified) is rewritten when it resolves to the renamed object by the scope rules of its owner's lines
/// (<see cref="ReferenceLookup"/>) in <paramref name="document"/> as it is before the rename.
/// </summary>
internal sealed class UndoRename(FormDocument document, FormObject renamed, string oldName, string newName)
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>The renamed object's qualified name after the rename.</summary>
    private readonly string _newQualified = oldName.LastIndexOf('.') is var dot and >= 0 ? oldName[..(dot + 1)] + newName : newName;

    public DesignerUndoUnit Unit(DesignerUndoUnit unit) =>
        new(unit.TitleKey, Changes(unit.Redo), Changes(unit.Undo), unit.CodeEdits, Selection(unit.SelectionBefore), Selection(unit.SelectionAfter))
        {
            TitleArgument = unit.TitleArgument,
            CodeOps = unit.CodeOps,
            Linked = unit.Linked,
        };

    private List<FormChange> Changes(IReadOnlyList<FormChange> changes) => [.. changes.Select(Change)];

    private FormChange Change(FormChange change) => change switch
    {
        SetProperty set => set with
        {
            ObjectName = Name(set.ObjectName),
            Value = Value(set.Value, ScopeOf(set.ObjectName)),
            Restore = Slots(set.Restore, ScopeOf(set.ObjectName)),
        },
        RemoveProperty remove => remove with { ObjectName = Name(remove.ObjectName), Restore = Slots(remove.Restore, ScopeOf(remove.ObjectName)) },
        AddObject add => add with { ParentName = Name(add.ParentName), Data = Data(add.Data) },
        RemoveObject remove => remove with { ObjectName = Name(remove.ObjectName) },
        MoveObject move => move with { ObjectName = Name(move.ObjectName), NewParentName = Name(move.NewParentName) },
        RenameObject rename => rename with
        {
            OldName = Name(rename.OldName),
            RestoreReferences = rename.RestoreReferences.IsDefault ? default
                : [.. rename.RestoreReferences.Select(r => (Name(r.Owner), Slots(r.Slots, ScopeOf(r.Owner))))],
        },
        RetypeObject retype => retype with { ObjectName = Name(retype.ObjectName) },
        _ => change,
    };

    private SelectionSnapshot Selection(SelectionSnapshot selection) =>
        new([.. selection.Names.Select(Name)], selection.Primary is null ? null : Name(selection.Primary));

    /// <summary>The renamed object's name and the qualified names below it take the new name; other names stay.</summary>
    private string Name(string name)
    {
        if (Names.Equals(name, oldName))
        {
            return _newQualified;
        }

        return name.Length > oldName.Length && name[oldName.Length] == '.' && name.StartsWith(oldName, StringComparison.OrdinalIgnoreCase)
            ? _newQualified + name[oldName.Length..]
            : name;
    }

    /// <summary>The reference scope of the lines of the object named <paramref name="owner"/> (file scope when it is not in the document).</summary>
    private FormObject? ScopeOf(string owner) => document.Find(owner) is { } obj ? ReferenceLookup.Scope(obj) : null;

    private FormValue Value(FormValue value, FormObject? scope)
    {
        switch (value)
        {
            case IdentValue ident when ReferenceEquals(ReferenceLookup.ResolvePlain(document, scope, ident.Name), renamed):
                return new IdentValue(newName);
            case QualifiedIdentValue { Parts: var parts }
                when ReferenceLookup.PartReaching(document, scope, parts, o => ReferenceEquals(o, renamed)) is { } index:
                return new QualifiedIdentValue(parts.SetItem(index, newName));
            case CollectionValue collection:
                var changed = false;
                var items = collection.Items.Select(item => new CollectionItem([.. item.Properties.Select(p =>
                {
                    var itemValue = Value(p.Value, scope);
                    if (ReferenceEquals(itemValue, p.Value))
                    {
                        return p;
                    }

                    changed = true;
                    return (p.Path, itemValue, (string?)null);
                })])).ToList();
                return changed ? new CollectionValue([.. items]) : value;
            default:
                return value;
        }
    }

    /// <summary>Restore slots with their references rewritten (a rewritten line loses its raw text).</summary>
    private ImmutableArray<PropertySlot> Slots(ImmutableArray<PropertySlot> slots, FormObject? scope)
    {
        if (slots.IsDefault)
        {
            return slots;
        }

        return [.. slots.Select(slot => slot.Property is { IsPreserved: false, IsEvent: false } property
            && Value(property.Value, scope) is var value && !ReferenceEquals(value, property.Value)
                ? slot with { Property = new FormProperty(property.Path, value, property.Info, null, null, default) }
                : slot)];
    }

    /// <summary>
    /// A snapshot of new objects (they belong to the file scope, DSG-FD-108) with their references rewritten; a rewritten
    /// snapshot is built anew on apply instead of putting the detached original back. The blocks of an instance name
    /// components of its frame and stay.
    /// </summary>
    private FormObjectData Data(FormObjectData data)
    {
        var changed = false;
        var properties = data.Properties.Select(p =>
        {
            var value = Value(p.Value, null);
            if (ReferenceEquals(value, p.Value))
            {
                return p;
            }

            changed = true;
            return new FormPropertyData(p.Path, value);
        }).ToImmutableArray();
        var children = data.Kind == FormObjectKind.Inline ? data.Children : [.. data.Children.Select(child =>
        {
            var rewritten = Data(child);
            changed |= !ReferenceEquals(rewritten, child);
            return rewritten;
        })];
        return changed ? data with { Properties = properties, Children = children, Original = null } : data;
    }
}
