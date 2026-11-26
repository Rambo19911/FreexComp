using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Editing;

/// <summary>
/// The only way to mutate a <see cref="FormDocument"/> (DSG-FD-001, form-document.md §7). Every applied change returns
/// its exact inverse, re-validates the document (DSG-FD-070), increments <see cref="FormDocument.Version"/> once and
/// raises <see cref="FormDocument.Changed"/> once (DSG-FD-084).
/// </summary>
public sealed class FormEditor(FormDocument document, ComponentCatalog catalog)
{
    private static readonly string[] Geometry = ["Left", "Top", "Width", "Height", "ClientWidth", "ClientHeight", "DesignLeft", "DesignTop"];

    /// <summary>Applies one change and returns its inverse.</summary>
    public FormChange Apply(FormChange change) => Apply([change])[0];

    /// <summary>Applies the changes in order as one batch (one <c>Changed</c> event); returns their inverses in reverse order.</summary>
    /// <exception cref="InvalidFormChangeException">A change is invalid; the changes applied before it are undone.</exception>
    public IReadOnlyList<FormChange> Apply(IReadOnlyList<FormChange> changes)
    {
        var effects = new Effects();
        var inverses = new List<FormChange>(changes.Count);
        try
        {
            foreach (var change in changes)
            {
                inverses.Add(ApplyOne(change, effects));
            }
        }
        catch (InvalidFormChangeException)
        {
            for (var i = inverses.Count - 1; i >= 0; i--)
            {
                ApplyOne(inverses[i], new Effects());
            }

            throw;
        }

        inverses.Reverse();
        FormValidator.Validate(document, catalog);
        document.RaiseChanged(new FormChangedEventArgs([.. effects.Names], effects.Flags));
        return inverses;
    }

    private FormChange ApplyOne(FormChange change, Effects effects) => change switch
    {
        SetProperty set => ChangeProperty(set.ObjectName, set.Path, set.Value, set.Restore, effects),
        RemoveProperty remove => ChangeProperty(remove.ObjectName, remove.Path, null, remove.Restore, effects),
        AddObject add => Add(add, effects),
        RemoveObject remove => Remove(remove, effects),
        MoveObject move => Move(move, effects),
        RenameObject rename => Rename(rename, effects),
        RetypeObject retype => Retype(retype, effects),
        _ => throw new InvalidFormChangeException("Unknown change " + change),
    };

    /// <summary>DSG-CS-046: only the root's type name changes (the metadata it is bound to stays).</summary>
    private FormChange Retype(RetypeObject change, Effects effects)
    {
        var obj = document.FindByName(change.ObjectName) ?? throw NotFound(change.ObjectName);
        if (obj.Parent is not null)
        {
            throw new InvalidFormChangeException($"Only the type name of the form root can change, not that of {obj.Name}");
        }

        CheckIdentifier(change.NewTypeName);
        var old = obj.TypeName;
        obj.TypeName = change.NewTypeName;
        effects.Name(obj.Name);
        effects.Flags |= FormChangeFlags.Name;
        return new RetypeObject(obj.Name, old);
    }

    // ---- objects (DSG-FD-081 … 085) ------------------------------------------------------------------------------

    private FormChange Add(AddObject change, Effects effects)
    {
        var parent = Editable(change.ParentName);
        RejectInsideInstance(parent, "added to");
        if (change.Index < 0 || change.Index > parent.Children.Count)
        {
            throw new InvalidFormChangeException($"Index {change.Index} is outside 0..{parent.Children.Count} of {parent.Name}");
        }

        // DSG-FD-132, 133: in a descendant, new components follow the inherited children of their container.
        var index = Math.Max(change.Index, InheritedCount(parent));

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CheckNewNames(change.Data, names);
        var obj = change.Data.Original is { Parent: null } original ? original
            : change.Data.Kind == FormObjectKind.Inline && document.Frames is not null
                && FrameCopies.BuildInstance(change.Data, document, catalog) is { } instance ? instance
            : Build(change.Data, parentPreserved: false);
        parent.InsertChild(index, obj);
        document.Register(obj);
        effects.Structure(obj.Name, parent.Name);
        return new RemoveObject(obj.Name);
    }

    private void CheckNewNames(FormObjectData data, HashSet<string> names)
    {
        CheckIdentifier(data.Name);
        if (document.FindByName(data.Name) is not null || !names.Add(data.Name))
        {
            throw new InvalidFormChangeException($"A component named {data.Name} already exists");
        }

        if (data.Kind == FormObjectKind.Inline)
        {
            return; // DSG-FD-108: the blocks of an instance name components of the frame, not of the file
        }

        foreach (var child in data.Children)
        {
            CheckNewNames(child, names);
        }
    }

    private FormChange Remove(RemoveObject change, Effects effects)
    {
        var obj = document.Find(change.ObjectName) ?? throw NotFound(change.ObjectName);
        RejectInherited(obj, "removed");
        var parent = MovableParent(obj);
        var data = FormObjectData.Capture(obj) with { Original = obj };
        var index = parent.RemoveChild(obj);
        document.Unregister(obj);
        effects.Structure(obj.Name, parent.Name);
        return new AddObject(parent.Name, index, data);
    }

    private FormChange Move(MoveObject change, Effects effects)
    {
        var obj = document.Find(change.ObjectName) ?? throw NotFound(change.ObjectName);
        RejectInherited(obj, "moved");
        var oldParent = MovableParent(obj);
        var newParent = Editable(change.NewParentName);
        RejectInsideInstance(newParent, "moved into");
        for (var ancestor = newParent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, obj))
            {
                throw new InvalidFormChangeException($"{obj.Name} cannot be moved into itself or its descendant {newParent.Name}");
            }
        }

        var count = newParent.Children.Count - (ReferenceEquals(newParent, oldParent) ? 1 : 0);
        if (change.NewIndex < 0 || change.NewIndex > count)
        {
            throw new InvalidFormChangeException($"Index {change.NewIndex} is outside 0..{count} of {newParent.Name}");
        }

        // DSG-FD-133: a new child is not placed before the inherited children. An inverse recorded before a re-merge in
        // which the ancestor added children to the container is clamped instead (DSG-FD-134: the undo stack stays valid).
        var inherited = InheritedCount(newParent);
        if (change.NewIndex < inherited && !change.IsInverse)
        {
            throw new InvalidFormChangeException($"{obj.Name} cannot be placed before the inherited components of {newParent.Name}");
        }

        var oldIndex = oldParent.RemoveChild(obj);
        newParent.InsertChild(Math.Max(change.NewIndex, inherited), obj);
        effects.Structure(obj.Name, oldParent.Name, newParent.Name);
        effects.Flags |= FormChangeFlags.Geometry;
        return new MoveObject(obj.Name, oldParent.Name, oldIndex) { IsInverse = true };
    }

    /// <summary>The number of inherited children of <paramref name="parent"/>: they come first (DSG-FD-133).</summary>
    private static int InheritedCount(FormObject parent)
    {
        var count = 0;
        while (count < parent.Children.Count && parent.Children[count].IsInherited)
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// DSG-FD-109, 132: the structure of inherited objects is fixed: in a frame instance and in a descendant they are not
    /// removed, moved, reordered or renamed (cards F05-006, F05-007 "Out of scope").
    /// </summary>
    private static void RejectInherited(FormObject obj, string what)
    {
        if (!obj.IsInherited)
        {
            return;
        }

        throw new InvalidFormChangeException(obj.InheritedFrame is null && obj.InheritedSource is { } source
            ? $"{obj.QualifiedName} is inherited from {source.ClassName} and cannot be {what}"
            : $"{obj.QualifiedName} is part of a frame and cannot be {what}");
    }

    /// <summary>
    /// DSG-FD-109, 132: nothing is added into a frame instance or into an object inside one (<c>F0110</c>); the root of a
    /// descendant and its other inherited objects accept new components.
    /// </summary>
    private static void RejectInsideInstance(FormObject parent, string what)
    {
        if (!parent.AcceptsNewChildren)
        {
            throw new InvalidFormChangeException($"Components cannot be {what} frame instance {parent.QualifiedName}");
        }
    }

    /// <summary>The parent of an object that may be removed or moved: not the root, not inside a preserved object.</summary>
    private static FormObject MovableParent(FormObject obj) =>
        obj.Parent is not { } parent ? throw new InvalidFormChangeException("The root object cannot be removed or moved")
        : parent.IsPreserved ? throw new InvalidFormChangeException($"{obj.Name} is part of the preserved object {parent.Name}")
        : parent;

    private FormChange Rename(RenameObject change, Effects effects)
    {
        var obj = Editable(change.OldName);
        if (!change.FollowsAncestor)
        {
            RejectInherited(obj, "renamed");
        }
        else if (!obj.IsInherited || obj.Parent is null)
        {
            throw new InvalidFormChangeException($"{obj.QualifiedName} is no inherited component"); // D-137, D-139
        }

        // D-139: an inherited object inside a frame instance is renamed within the instance's scope (DSG-FD-108).
        var scope = obj.Scope;
        CheckIdentifier(change.NewName);
        if ((scope is null ? document.FindByName(change.NewName) : scope.FindInScope(change.NewName)) is { } other && !ReferenceEquals(other, obj))
        {
            throw new InvalidFormChangeException($"A component named {change.NewName} already exists");
        }

        var oldName = obj.Name;
        var oldQualifiedName = obj.QualifiedName;
        List<(FormObject Owner, ImmutableArray<PropertySlot> Slots)> before = [];
        if (!change.RestoreReferences.IsDefault)
        {
            foreach (var (ownerName, slots) in change.RestoreReferences)
            {
                var owner = document.Find(ownerName) ?? throw NotFound(ownerName);
                before.Add((owner, Snapshot(owner, slots.Select(s => s.Path))));
                Restore(owner, slots);
            }
        }
        else
        {
            // DSG-FD-082: the references that name the renamed object, plain or as any part of a qualified name; handler names
            // stay. D-139, D-141: each reference is resolved by the scope rules of its owner's lines (RTL-1093, RTL-1104).
            foreach (var owner in document.AllObjects().Where(o => !o.IsPreserved && o.Info is not null).ToList())
            {
                var updates = new List<FormProperty>();
                foreach (var property in owner.Properties)
                {
                    if (!property.IsPreserved && property.Info is { } info
                        && RenameReferences(property.Value, info, owner, obj, change.NewName) is { } renamed)
                    {
                        updates.Add(new FormProperty(property.Path, renamed, info, null, null, default));
                    }
                }

                if (updates.Count > 0)
                {
                    before.Add((owner, Snapshot(owner, updates.Select(u => u.Path))));
                    foreach (var update in updates)
                    {
                        Store(owner, update.Path, update);
                    }
                }
            }
        }

        document.Rename(obj, change.NewName);
        effects.Name(oldQualifiedName);
        effects.Name(obj.QualifiedName);
        foreach (var (owner, _) in before)
        {
            effects.Name(owner.QualifiedName);
        }

        effects.Flags |= FormChangeFlags.Name;

        // DSG-FD-134: the owners by their names after this rename, so that the inverse still applies after a re-merge has
        // replaced the objects of a descendant.
        return new RenameObject(obj.QualifiedName, oldName)
        {
            RestoreReferences = [.. before.Select(b => (b.Owner.QualifiedName, b.Slots))],
            FollowsAncestor = change.FollowsAncestor,
        };
    }

    /// <summary>
    /// The value of a line of <paramref name="owner"/> with the references that resolve to <paramref name="renamed"/> renamed
    /// (a plain name, or the part of a qualified name that reaches it: first, middle or last), or null when it has none.
    /// </summary>
    private FormValue? RenameReferences(FormValue value, PropertyInfo info, FormObject owner, FormObject renamed, string newName)
    {
        switch (value)
        {
            case IdentValue ident when info.Kind == PropertyKind.Component && ReferenceEquals(ResolvePlain(owner, ident.Name), renamed):
                return new IdentValue(newName);
            case QualifiedIdentValue { Parts: var parts } when info.Kind == PropertyKind.Component && PartNaming(owner, parts, renamed) is { } index:
                return new QualifiedIdentValue(parts.SetItem(index, newName));
            case CollectionValue collection when info.ItemClass is { } itemClass && catalog.FindType(itemClass) is { Kind: TypeKind.Object } itemType:
                var changed = false;
                var items = collection.Items.Select(item => new CollectionItem([.. item.Properties.Select(p =>
                {
                    if (itemType.Properties.FirstOrDefault(i => Same(i.Name, p.Path)) is { } itemInfo
                        && RenameReferences(p.Value, itemInfo, owner, renamed, newName) is { } itemValue)
                    {
                        changed = true;
                        return (p.Path, itemValue, (string?)null);
                    }

                    return p;
                })])).ToList();
                return changed ? new CollectionValue([.. items]) : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// The scope the lines of <paramref name="owner"/> resolve names in first (RTL-1093): an instance's own lines and the
    /// lines of the inherited objects of its scope; null for the file scope (also the inherited objects of a descendant's
    /// file scope, DSG-FD-126).
    /// </summary>
    private static FormObject? ReferenceScope(FormObject owner) => owner.IsFrameInstance ? owner : owner.IsInherited ? owner.Scope : null;

    /// <summary>
    /// The object a plain name in a line of <paramref name="owner"/> names (RTL-1093, RTL-1104): the innermost scope (its
    /// instance by name, then its components), then the file scope (the root by name included).
    /// </summary>
    private FormObject? ResolvePlain(FormObject owner, string name) =>
        ReferenceScope(owner) is { } scope && (Same(scope.Name, name) ? scope : scope.FindInScope(name)) is { } inScope
            ? inScope
            : document.FindByName(name);

    /// <summary>
    /// The index of the part of the qualified name <paramref name="parts"/> in a line of <paramref name="owner"/> that reaches
    /// <paramref name="target"/>, or null. The first part is resolved like a plain name (this file's root name names the
    /// root; a frame's header name is no name of a placing file, also in an instance's lines: RTL-1104); each further part is
    /// a component of the root (the file scope) or of an instance's scope (DSG-FD-108).
    /// </summary>
    private int? PartNaming(FormObject owner, ImmutableArray<string> parts, FormObject target)
    {
        var current = ResolvePlain(owner, parts[0]);
        for (var i = 0; current is not null; i++)
        {
            if (ReferenceEquals(current, target))
            {
                return i;
            }

            if (i + 1 == parts.Length)
            {
                break;
            }

            current = ReferenceEquals(current, document.Root) ? document.FindByName(parts[i + 1])
                : current.IsFrameInstance ? current.FindInScope(parts[i + 1])
                : null;
        }

        return null;
    }

    /// <summary>form-format.md §1: <c>[A-Za-z_][A-Za-z0-9_]*</c>, and usable as a field name (not a reserved word).</summary>
    private static void CheckIdentifier(string name)
    {
        var valid = name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_')
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && !SyntaxFacts.IsReservedWord(name);
        if (!valid)
        {
            throw new InvalidFormChangeException($"'{name}' is not a valid component name");
        }
    }

    /// <summary>A new object tree from a snapshot, bound to the catalog like the parser binds it (DSG-FD-026).</summary>
    private FormObject Build(FormObjectData data, bool parentPreserved)
    {
        var info = catalog.Find(data.TypeName);
        var preserved = parentPreserved || data.PreservedText is not null || data.Kind != FormObjectKind.Object || info is null;
        var obj = new FormObject(data.Kind, data.Name, data.TypeName, info, default)
        {
            Catalog = catalog,
            IsPreserved = preserved,
            PreservedText = parentPreserved ? null : data.PreservedText,
        };
        foreach (var property in data.Properties)
        {
            var eventInfo = info is not null && !property.Path.Contains('.', StringComparison.Ordinal) ? info.FindEvent(property.Path) : null;
            var propertyInfo = info is not null && eventInfo is null ? catalog.FindPropertyPath(info, property.Path) : null;
            var rawText = property.RawText is { } raw && (!property.RawLines.IsDefault || !raw.Contains('\n', StringComparison.Ordinal)) ? raw : null;
            obj.AddProperty(new FormProperty(property.Path, property.Value, propertyInfo, eventInfo, rawText, default, rawText is null ? default : property.RawLines)
            {
                IsPreserved = !preserved && ((propertyInfo is null && eventInfo is null) || property.Value is RawValue),
            });
        }

        foreach (var child in data.Children)
        {
            obj.AddChild(Build(child, preserved));
        }

        return obj;
    }

    private static InvalidFormChangeException NotFound(string name) => new($"Component {name} not found");

    // ---- properties (DSG-FD-080, DSG-FD-043, form-format.md §2.2.3/§2.2.4, DSG-FD-012) --------------------------

    /// <param name="value">The new value, or null to remove the property (reset to default).</param>
    private FormChange ChangeProperty(string objectName, string path, FormValue? value, ImmutableArray<PropertySlot> restore, Effects effects)
    {
        var obj = Editable(objectName);
        var info = obj.Info!;
        var plan = new List<(string Path, FormProperty? Property)>();
        if (!restore.IsDefault)
        {
            Resolve(obj, info, path);
            effects.Touch(obj, restore.Select(s => s.Path), catalog);
            var current = Snapshot(obj, restore.Select(s => s.Path));
            Restore(obj, restore);
            return Inverse(obj, path, current);
        }

        var (canonical, propertyInfo, eventInfo) = Resolve(obj, info, path);
        FormProperty? stored = null;
        // DSG-FD-109, 110: an override equal to the frame's value is removed (events: a handler of the form is always stored).
        if (value is not null && (eventInfo is not null || IsStoredValue(obj, canonical, propertyInfo!, value)))
        {
            stored = new FormProperty(ExistingPath(obj, canonical), value, propertyInfo, eventInfo, null, default);
        }

        plan.Add((canonical, stored));
        if (eventInfo is null)
        {
            PlanPairs(obj, info, canonical, value, plan);
            PlanLegacyClientSize(obj, canonical, value, plan);
        }

        // "stored": false lines read from the file are kept until the object is edited, then dropped (DSG-FD-043);
        // a legacy root size follows DSG-FD-012 instead.
        foreach (var property in obj.Properties)
        {
            if (property.Info is { } read && !PropertyAccess.IsStoredPath(obj, property.Path, read) && !(obj.Parent is null && IsLegacySize(property.Path)) && !plan.Any(p => Same(p.Path, property.Path)))
            {
                plan.Add((property.Path, null));
            }
        }

        var before = Snapshot(obj, plan.Select(p => p.Path));
        foreach (var (planPath, property) in plan)
        {
            Store(obj, planPath, property);
        }

        effects.Touch(obj, plan.Select(p => p.Path), catalog);
        return Inverse(obj, canonical, before);
    }

    /// <summary>
    /// False when the value is not written: equal to the default (except DSG-FD-044) or <c>"stored": false</c>. For an
    /// inherited object the "default" is the inherited value (DSG-FD-109); for a frame instance the value of the frame's
    /// root, except the bounds (always written) and <c>TabOrder</c> (the metadata default, form-format.md §2.3; DSG-FD-110).
    /// </summary>
    private bool IsStoredValue(FormObject obj, string path, PropertyInfo info, FormValue value)
    {
        if (!PropertyAccess.IsStoredPath(obj, path, info))
        {
            return false;
        }

        if (IsAlwaysWritten(obj, path))
        {
            return true;
        }

        var ownTabOrder = obj.IsFrameInstance && !obj.IsInherited && Same(path, "TabOrder");
        if (obj.InheritedFrom is not null && !ownTabOrder && obj.InheritedValue(path) is { } inherited)
        {
            return !PropertyAccess.SameValue(catalog, info, value, inherited);
        }

        return !PropertyAccess.IsDefault(catalog, info, value);
    }

    /// <summary>form-format.md §2.2.4: Font.* / Color / ShowHint and the Parent… flag that governs them.</summary>
    private void PlanPairs(FormObject obj, ComponentInfo info, string path, FormValue? value, List<(string, FormProperty?)> plan)
    {
        if (FlagOf(path) is { } flag && value is not null && catalog.FindPropertyPath(info, flag) is { } flagInfo)
        {
            var off = new IdentValue("False");
            plan.Add((flagInfo.Name, IsStoredValue(obj, flagInfo.Name, flagInfo, off) ? new FormProperty(flagInfo.Name, off, flagInfo, null, null, default) : null));
            return;
        }

        if (IsFlag(path) && catalog.FindPropertyPath(info, path) is { } pathInfo)
        {
            var effective = value ?? PropertyAccess.DefaultValue(catalog, pathInfo);
            if (effective is IdentValue { Name: var name } && name.Equals("True", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var dependent in obj.Properties.Where(p => Same(FlagOf(p.Path) ?? "", path)).Select(p => p.Path).ToList())
                {
                    plan.Add((dependent, null));
                }
            }
        }
    }

    /// <summary>DSG-FD-012: the first client-size change of a root that stores only Width/Height writes both client values.</summary>
    private static void PlanLegacyClientSize(FormObject obj, string path, FormValue? value, List<(string, FormProperty?)> plan)
    {
        if (obj.Parent is not null || value is null || !(Same(path, "ClientWidth") || Same(path, "ClientHeight"))
            || !(obj.FindProperty("Width") is not null || obj.FindProperty("Height") is not null))
        {
            return;
        }

        var other = Same(path, "ClientWidth") ? "ClientHeight" : "ClientWidth";
        if (obj.FindProperty(other) is null && obj.Info!.FindProperty(other) is { } otherInfo)
        {
            var bounds = PropertyAccess.GetBounds(obj);
            var derived = other == "ClientWidth" ? bounds.Width : bounds.Height;
            plan.Add((other, new FormProperty(other, new IntegerValue(derived, false), otherInfo, null, null, default)));
        }

        plan.Add(("Width", null));
        plan.Add(("Height", null));
    }

    private static string? FlagOf(string path) =>
        path.StartsWith("Font.", StringComparison.OrdinalIgnoreCase) ? "ParentFont"
        : Same(path, "Color") ? "ParentColor"
        : Same(path, "ShowHint") ? "ParentShowHint"
        : null;

    private static bool IsFlag(string path) => Same(path, "ParentFont") || Same(path, "ParentColor") || Same(path, "ParentShowHint");

    private static bool IsLegacySize(string path) => Same(path, "Width") || Same(path, "Height");

    /// <summary>
    /// DSG-FD-044: geometry of controls, the root's frame values, the tray position of a non-visual root child; a frame
    /// root's bounds (DSG-FD-117); nothing for inherited objects (DSG-FD-114).
    /// </summary>
    private static bool IsAlwaysWritten(FormObject obj, string path)
    {
        if (obj.IsInherited || obj.IsInheritedRoot)
        {
            return false; // DSG-FD-114, 128: overrides only
        }

        string[] always = PropertyAccess.IsFrameRoot(obj) ? ["Left", "Top", "Width", "Height"]
            : obj.Parent is null ? ["Left", "Top", "ClientWidth", "ClientHeight", "PixelsPerInch"]
            : obj.Info!.Kind == ComponentKind.NonVisual ? obj.Parent.Parent is null ? ["DesignLeft", "DesignTop"] : []
            : ["Left", "Top", "Width", "Height"];
        return always.Any(a => Same(a, path));
    }

    /// <summary>Metadata casing of a property path or event name; throws for unknown paths and object properties.</summary>
    private (string Path, PropertyInfo? Info, EventInfo? Event) Resolve(FormObject obj, ComponentInfo info, string path)
    {
        if (!path.Contains('.', StringComparison.Ordinal) && info.FindEvent(path) is { } ev)
        {
            return (ev.Name, null, ev);
        }

        var parts = path.Split('.');
        var names = new List<string>(parts.Length);
        PropertyInfo? current = null;
        for (var i = 0; i < parts.Length; i++)
        {
            current = i == 0
                ? catalog.FindPropertyPath(info, parts[0])
                : catalog.SubProperties(current!).FirstOrDefault(p => Same(p.Name, parts[i]));
            if (current is null)
            {
                throw new InvalidFormChangeException($"Property {path} does not exist on {obj.TypeName}");
            }

            names.Add(current.Name);
        }

        return current!.Kind == PropertyKind.Object
            ? throw new InvalidFormChangeException($"{path} is an object property of {obj.TypeName}; only its sub-properties have values")
            : (string.Join('.', names), current, null);
    }

    /// <summary>DSG-FD-002: a stored property keeps its declared casing when its value is replaced.</summary>
    private static string ExistingPath(FormObject obj, string path) => obj.FindProperty(path)?.Path ?? path;

    internal FormObject Editable(string objectName)
    {
        var obj = document.Find(objectName) ?? throw NotFound(objectName);
        return obj.IsPreserved || obj.Info is null ? throw new InvalidFormChangeException($"Component {objectName} is preserved and read-only") : obj;
    }

    // ---- snapshots ---------------------------------------------------------------------------------------------

    /// <summary>Every stored line of the given paths (or their absence), with list positions.</summary>
    private static ImmutableArray<PropertySlot> Snapshot(FormObject obj, IEnumerable<string> paths)
    {
        var slots = ImmutableArray.CreateBuilder<PropertySlot>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var found = false;
            for (var i = 0; i < obj.Properties.Count; i++)
            {
                if (Same(obj.Properties[i].Path, path))
                {
                    slots.Add(new PropertySlot(path, i, obj.Properties[i]));
                    found = true;
                }
            }

            if (!found)
            {
                slots.Add(new PropertySlot(path, -1, null));
            }
        }

        return slots.ToImmutable();
    }

    /// <summary>Puts the snapshot back exactly: removes the current lines of its paths, re-inserts the old ones in place.</summary>
    private static void Restore(FormObject obj, ImmutableArray<PropertySlot> slots)
    {
        foreach (var path in slots.Select(s => s.Path).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            RemoveAll(obj, path);
        }

        foreach (var slot in slots.Where(s => s.Property is not null).OrderBy(s => s.Index))
        {
            obj.InsertProperty(slot.Index, slot.Property!);
        }
    }

    /// <summary>The inverse of a property change: <c>SetProperty</c> of the old value when it was stored, else <c>RemoveProperty</c>.</summary>
    private static FormChange Inverse(FormObject obj, string path, ImmutableArray<PropertySlot> before) =>
        before.FirstOrDefault(s => Same(s.Path, path) && s.Property is not null).Property is { } old
            ? new SetProperty(obj.QualifiedName, old.Path, old.Value) { Restore = before }
            : new RemoveProperty(obj.QualifiedName, path) { Restore = before };

    /// <summary>Replaces every line of <paramref name="path"/> by <paramref name="property"/> (at the first old position), or removes them.</summary>
    private static void Store(FormObject obj, string path, FormProperty? property)
    {
        var at = RemoveAll(obj, path);
        if (property is not null)
        {
            obj.InsertProperty(at < 0 ? obj.Properties.Count : at, property);
        }
    }

    private static int RemoveAll(FormObject obj, string path)
    {
        var first = -1;
        for (var i = obj.Properties.Count - 1; i >= 0; i--)
        {
            if (Same(obj.Properties[i].Path, path))
            {
                obj.RemovePropertyAt(i);
                first = i;
            }
        }

        return first;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>What a batch affected (DSG-FD-084).</summary>
    private sealed class Effects
    {
        public List<string> Names { get; } = [];

        public FormChangeFlags Flags { get; set; }

        public void Structure(params string[] names)
        {
            foreach (var name in names)
            {
                Name(name);
            }

            Flags |= FormChangeFlags.Structure;
        }

        public void Name(string name)
        {
            if (!Names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                Names.Add(name);
            }
        }

        /// <summary>Records property paths of <paramref name="obj"/>: events, layout-affecting values (geometry) or appearance.</summary>
        public void Touch(FormObject obj, IEnumerable<string> paths, ComponentCatalog catalog)
        {
            Name(obj.QualifiedName);
            foreach (var path in paths)
            {
                if (obj.Info!.FindEvent(path) is not null && !path.Contains('.', StringComparison.Ordinal))
                {
                    Flags |= FormChangeFlags.Events;
                }
                else if (Geometry.Any(g => Same(g, path)) || catalog.FindPropertyPath(obj.Info, path) is { AffectsLayout: true })
                {
                    Flags |= FormChangeFlags.Geometry;
                }
                else
                {
                    Flags |= FormChangeFlags.Appearance;
                }
            }
        }
    }
}
