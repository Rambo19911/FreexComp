using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>Where a drop in the Structure window's Design view puts the dragged items, relative to the row under the pointer (UI-TW-1611).</summary>
public enum StructureDropPosition
{
    /// <summary>Before the row, in the row's parent.</summary>
    Before,

    /// <summary>After the row, in the row's parent.</summary>
    After,

    /// <summary>Into the row's component, as its last children.</summary>
    Into,
}

/// <summary>The resolved drop: the new parent and the index in its current child list the items are inserted at.</summary>
public sealed record StructureDrop(FormObject Parent, int Index);

/// <summary>Why a drop in the Structure window is refused (UI-TW-1612); it selects the status message.</summary>
public enum StructureDropRefusal
{
    /// <summary>The would-be new parent cannot hold the dragged controls (<c>Msg.Structure.DropRefused</c>).</summary>
    CannotHoldControls,

    /// <summary>A dragged non-visual component would change its parent (<c>Msg.Structure.NonVisualStaysInParent</c>).</summary>
    NonVisualStaysInParent,
}

/// <summary>
/// Re-parenting and reordering from the Structure window's Design view (structure-tree.md UI-TW-1611…1613): which items can be
/// dragged, whether a drop position is valid, and the drop as one <c>Move in Structure</c> undo unit.
/// </summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>
    /// UI-TW-1611: an object can be dragged in the Structure tree unless it is the form root, unknown or read-only (preserved,
    /// <c>form-format.md</c> §5) or inherited (DSG-SUR-227, 237: no structural change).
    /// </summary>
    public static bool CanDragInStructure(FormObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.Parent is not null && obj.Info is not null && !obj.IsPreserved && !IsStructureFixed(obj);
    }

    /// <summary>
    /// UI-TW-1612: the parent and insertion index of dropping <paramref name="items"/> at <paramref name="position"/> of
    /// <paramref name="target"/> (the form root's row always means "into"), or null when the drop is refused —
    /// <paramref name="reason"/> then says why and <paramref name="refusing"/> is the component the message names: the
    /// would-be new parent (<see cref="StructureDropRefusal.CannotHoldControls"/>) or the non-visual item that would change
    /// its parent (<see cref="StructureDropRefusal.NonVisualStaysInParent"/>). Controls need a container (metadata
    /// <c>acceptsChildren</c>, not preserved, accepting new children) that is not dragged and not inside a dragged item;
    /// non-visual components keep their parent and are only reordered among their siblings.
    /// </summary>
    public StructureDrop? ResolveStructureDrop(
        IReadOnlyList<FormObject> items, FormObject target, StructureDropPosition position, out FormObject refusing, out StructureDropRefusal reason)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(target);
        if (target.Parent is null)
        {
            position = StructureDropPosition.Into;
        }

        var parent = position == StructureDropPosition.Into ? target : target.Parent!;
        refusing = parent;
        reason = StructureDropRefusal.CannotHoldControls;
        var moved = TopLevel(items);
        if (moved.Count == 0 || !moved.TrueForAll(CanDragInStructure))
        {
            return null;
        }

        foreach (var item in moved)
        {
            if (item.Info!.Kind == ComponentKind.NonVisual && !ReferenceEquals(item.Parent, parent))
            {
                (refusing, reason) = (item, StructureDropRefusal.NonVisualStaysInParent);
                return null;
            }

            if (item.Info.Kind != ComponentKind.NonVisual && !AcceptsControls(parent))
            {
                return null;
            }
        }

        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (moved.Exists(m => ReferenceEquals(m, ancestor)))
            {
                return null; // into a dragged item or one of its descendants
            }
        }

        var index = position switch
        {
            StructureDropPosition.Before => IndexIn(parent, target),
            StructureDropPosition.After => IndexIn(parent, target) + 1,
            _ => parent.Children.Count,
        };

        // DSG-FD-133: new children are not placed before the inherited ones of a descendant's container.
        return new StructureDrop(parent, Math.Max(index, parent.Children.TakeWhile(c => c.IsInherited).Count()));
    }

    /// <summary>
    /// UI-TW-1613: the drop as one <c>Move in Structure</c> unit. Re-parented controls keep their position on the form,
    /// converted to the new parent's client coordinates and clamped so that they lie inside its client rect
    /// (<c>0 ≤ Left ≤ ClientWidth − Width</c>, <c>0 ≤ Top ≤ ClientHeight − Height</c>; a larger control goes to 0); the items are placed, in their current relative order (document order), at the drop position — the child list
    /// is the creation / z-order and the <c>.form</c> order; tab orders are renumbered as on the surface (DSG-SUR-096); the
    /// selection stays. Returns false (nothing recorded) when the drop is refused or leaves everything in place.
    /// </summary>
    public bool MoveInStructure(IReadOnlyList<FormObject> items, FormObject target, StructureDropPosition position)
    {
        if (ResolveStructureDrop(items, target, position, out _, out _) is not { } drop || IsPointerActive)
        {
            return false;
        }

        var parent = drop.Parent;
        var moved = TopLevel(items);
        var remaining = parent.Children.Where(c => !moved.Contains(c, ReferenceEqualityComparer.Instance)).ToList();
        var at = parent.Children.Take(drop.Index).Count(c => !moved.Contains(c, ReferenceEqualityComparer.Instance));
        List<FormObject> wanted = [.. remaining.Take(at), .. moved, .. remaining.Skip(at)];
        var entering = moved.Where(m => !ReferenceEquals(m.Parent, parent)).ToList();
        if (entering.Count == 0 && wanted.SequenceEqual(parent.Children, ReferenceEqualityComparer.Instance))
        {
            return false; // a drop at the items' own position records nothing
        }

        var changes = new List<FormChange>();
        var g = Geometry;
        var laidOut = g.HasBounds(parent);
        var client = laidOut ? g.GetClientRect(parent) : default;
        foreach (var item in entering)
        {
            var bounds = laidOut && g.HasBounds(item)
                ? g.GetBounds(item)
                : new Rect(
                    PropertyAccess.GetInt(item, "Left") + client.Left, PropertyAccess.GetInt(item, "Top") + client.Top,
                    PropertyAccess.GetInt(item, "Width"), PropertyAccess.GetInt(item, "Height"));
            var (x, y) = (bounds.Left - client.Left, bounds.Top - client.Top);

            // The whole control inside the client rect (a larger one at 0); a parent without layout bounds only clamps at 0.
            var (right, bottom) = laidOut
                ? (Math.Max(0, client.Width - bounds.Width), Math.Max(0, client.Height - bounds.Height))
                : (int.MaxValue, int.MaxValue);
            changes.Add(new SetProperty(item.QualifiedName, "Left", new IntegerValue(Math.Clamp(x, 0, right), false)));
            changes.Add(new SetProperty(item.QualifiedName, "Top", new IntegerValue(Math.Clamp(y, 0, bottom), false)));
        }

        // The moves one after the other (each index counts after the removal, DSG-FD-085): every item right after the one
        // placed before it, the first one in front of the first remaining child that follows the drop position.
        var simulated = parent.Children.ToList();
        for (var i = 0; i < moved.Count; i++)
        {
            var item = moved[i];
            simulated.Remove(item);
            var index = i > 0 ? simulated.IndexOf(moved[i - 1]) + 1 : at < remaining.Count ? simulated.IndexOf(remaining[at]) : simulated.Count;
            simulated.Insert(index, item);
            changes.Insert(i, new MoveObject(item.QualifiedName, parent.QualifiedName, index));
        }

        // DSG-SUR-096: the old parents close their gaps, the re-parented controls take the last positions in the new one.
        foreach (var oldParent in entering.Where(TabOrderRules.IsFocusable).Select(o => o.Parent!).Distinct())
        {
            changes.AddRange(TabOrderRules.Renumber(oldParent, entering));
        }

        if (entering.Exists(TabOrderRules.IsFocusable))
        {
            changes.AddRange(TabOrderRules.Renumber(parent, appended: entering));
        }

        var selection = Selection.ToSnapshot();
        var undo = ApplyEdit(changes);
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        PushUnit(new DesignerUndoUnit(UndoTitles.MoveInStructure, [.. changes, .. commitRedo], [.. commitUndo, .. undo], [], selection, selection));
        return true;
    }

    private static int IndexIn(FormObject parent, FormObject child)
    {
        for (var i = 0; i < parent.Children.Count; i++)
        {
            if (ReferenceEquals(parent.Children[i], child))
            {
                return i;
            }
        }

        return parent.Children.Count;
    }

    /// <summary>A container that takes dragged controls (UI-TW-1612, the DSG-SUR-055 rules).</summary>
    private static bool AcceptsControls(FormObject parent) =>
        parent is { Info.AcceptsChildren: true, IsPreserved: false } && !RefusesNewChildren(parent);

    /// <summary>The items without those inside another item (they move with it), in document order.</summary>
    private List<FormObject> TopLevel(IReadOnlyList<FormObject> items)
    {
        var set = new HashSet<FormObject>(items, ReferenceEqualityComparer.Instance);
        bool InsideAnother(FormObject obj)
        {
            for (var p = obj.Parent; p is not null; p = p.Parent)
            {
                if (set.Contains(p))
                {
                    return true;
                }
            }

            return false;
        }

        var ordered = new List<FormObject>();
        void Walk(FormObject obj)
        {
            if (set.Contains(obj) && !InsideAnother(obj))
            {
                ordered.Add(obj);
            }

            foreach (var child in obj.Children)
            {
                Walk(child);
            }
        }

        Walk(Document.Root);
        return ordered;
    }
}
