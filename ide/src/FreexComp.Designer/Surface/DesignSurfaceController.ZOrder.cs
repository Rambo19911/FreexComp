using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>Bring to front and send to back (design-surface.md §11.3, DSG-SUR-150 … 152; DSG-SUR-241).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>
    /// The changes that move every selected non-root, non-tray component to the end (<paramref name="front"/>) or the
    /// beginning of its parent's children, keeping the selected ones' relative order; empty when all are already there.
    /// DSG-SUR-241: in a container with inherited children the beginning is directly after them (<paramref name="kept"/>:
    /// a parent has inherited children, so the selected ones stopped there).
    /// </summary>
    private List<FormChange> ZOrderChanges(bool front, out bool kept)
    {
        var g = Geometry;
        var changes = new List<FormChange>();
        kept = false;
        var members = Selection.Items.Where(o => o.Parent is not null && !g.IsTrayItem(o)).ToList();
        foreach (var group in members.GroupBy(o => o.Parent!, ReferenceEqualityComparer.Instance))
        {
            var parent = (FormObject)group.Key!;
            var inherited = front ? 0 : parent.Children.TakeWhile(c => c.IsInherited).Count();
            var selected = parent.Children.Where(c => group.Contains(c, ReferenceEqualityComparer.Instance)).ToList();
            var others = parent.Children.Where(c => !selected.Contains(c, ReferenceEqualityComparer.Instance)).ToList();
            List<FormObject> wanted = front ? [.. others, .. selected] : [.. others.Take(inherited), .. selected, .. others.Skip(inherited)];
            if (wanted.SequenceEqual(parent.Children, ReferenceEqualityComparer.Instance))
            {
                continue;
            }

            // Applied one after the other: to the end in current order, or to the start in reverse order.
            kept |= inherited > 0;
            changes.AddRange(front
                ? selected.Select(c => (FormChange)new MoveObject(c.QualifiedName, parent.QualifiedName, parent.Children.Count - 1))
                : Enumerable.Reverse(selected).Select(c => (FormChange)new MoveObject(c.QualifiedName, parent.QualifiedName, inherited)));
        }

        return changes;
    }

    /// <summary>DSG-SUR-150: enabled when the order would change; allowed for preserved components and while locked.</summary>
    private bool CanZOrder(bool front) => !IsPointerActive && State != InteractionState.TabOrder && ZOrderChanges(front, out _).Count > 0;

    /// <summary>
    /// DSG-SUR-150 … 152: one "Order" unit with the reordering (TabOrder untouched) and the layout commit — a tie of
    /// aligned siblings is broken by creation order, so the parent may re-flow. DSG-SUR-241: a send to back that stopped
    /// after inherited children shows <c>Msg.Designer.InheritedOrderKept</c>.
    /// </summary>
    private void ZOrder(bool front)
    {
        var changes = ZOrderChanges(front, out var kept);
        var selection = Selection.ToSnapshot();
        var undo = ApplyEdit(changes);
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        PushUnit(new DesignerUndoUnit(UndoTitles.Order, [.. changes, .. commitRedo], [.. commitUndo, .. undo], [], selection, selection));
        if (kept)
        {
            _host.SetStatus(StatusIds.InheritedOrderKept);
        }
    }
}
