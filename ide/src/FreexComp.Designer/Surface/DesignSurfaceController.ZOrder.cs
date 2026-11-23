using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>Bring to front and send to back (design-surface.md §11.3, DSG-SUR-150 … 152).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>
    /// The changes that move every selected non-root, non-tray component to the end (<paramref name="front"/>) or the
    /// beginning of its parent's children, keeping the selected ones' relative order; empty when all are already there.
    /// </summary>
    private List<FormChange> ZOrderChanges(bool front)
    {
        var g = Geometry;
        var changes = new List<FormChange>();
        var members = Selection.Items.Where(o => o.Parent is not null && !g.IsTrayItem(o)).ToList();
        foreach (var group in members.GroupBy(o => o.Parent!, ReferenceEqualityComparer.Instance))
        {
            var parent = (FormObject)group.Key!;
            var selected = parent.Children.Where(c => group.Contains(c, ReferenceEqualityComparer.Instance)).ToList();
            var others = parent.Children.Where(c => !selected.Contains(c, ReferenceEqualityComparer.Instance));
            List<FormObject> wanted = front ? [.. others, .. selected] : [.. selected, .. others];
            if (wanted.SequenceEqual(parent.Children, ReferenceEqualityComparer.Instance))
            {
                continue;
            }

            // Applied one after the other: to the end in current order, or to the start in reverse order.
            changes.AddRange(front
                ? selected.Select(c => (FormChange)new MoveObject(c.Name, parent.Name, parent.Children.Count - 1))
                : Enumerable.Reverse(selected).Select(c => (FormChange)new MoveObject(c.Name, parent.Name, 0)));
        }

        return changes;
    }

    /// <summary>DSG-SUR-150: enabled when the order would change; allowed for preserved components and while locked.</summary>
    private bool CanZOrder(bool front) => !IsPointerActive && State != InteractionState.TabOrder && ZOrderChanges(front).Count > 0;

    /// <summary>
    /// DSG-SUR-150 … 152: one "Order" unit with the reordering (TabOrder untouched) and the layout commit — a tie of
    /// aligned siblings is broken by creation order, so the parent may re-flow.
    /// </summary>
    private void ZOrder(bool front)
    {
        var changes = ZOrderChanges(front);
        var selection = Selection.ToSnapshot();
        var undo = Editor.Apply(changes);
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        Undo.Push(new DesignerUndoUnit(UndoTitles.Order, [.. changes, .. commitRedo], [.. commitUndo, .. undo], [], selection, selection));
    }
}
