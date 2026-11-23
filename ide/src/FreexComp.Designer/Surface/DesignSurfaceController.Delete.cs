using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>Delete (design-surface.md §10.4, DSG-SUR-130 … 132).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>DSG-SUR-130: something other than the form is selected; not while a drag or tab-order mode is active. Lock Controls does not matter.</summary>
    private bool CanDelete => !Selection.IsFormOnly && !IsPointerActive && State != InteractionState.TabOrder;

    /// <summary>
    /// DSG-SUR-130 … 132: removes the effective set (DSG-SUR-024) with its subtrees in one "Delete" unit, which also clears
    /// the references to every removed component (DSG-SUR-131), renumbers the remaining focusable siblings (DSG-SUR-096)
    /// and stores the re-flowed layout (DSG-LAY-040). The selection becomes the nearest remaining ancestor of the primary.
    /// Removing the fields from the form class belongs to code sync (code-sync.md §4.3), not wired here yet.
    /// </summary>
    private void Delete()
    {
        var members = Selection.GetEffectiveSet().Where(o => o.Parent is not null).ToList();
        if (members.Count == 0)
        {
            return;
        }

        var removed = new HashSet<FormObject>(ReferenceEqualityComparer.Instance);
        foreach (var member in members)
        {
            foreach (var obj in Subtree(member))
            {
                removed.Add(obj);
            }
        }

        var changes = new List<FormChange>(ReferenceClearing.Changes(Document, removed));
        foreach (var parent in members.Where(TabOrderRules.IsFocusable).Select(m => m.Parent!).Distinct())
        {
            changes.AddRange(TabOrderRules.Renumber(parent, members));
        }

        changes.AddRange(members.Select(m => new RemoveObject(m.Name)));

        var after = Selection.Primary.Parent;
        while (after is not null && removed.Contains(after))
        {
            after = after.Parent;
        }

        var before = Selection.ToSnapshot();
        var undo = Editor.Apply(changes);
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        Selection.Select(after ?? Document.Root);
        Undo.Push(new DesignerUndoUnit(UndoTitles.Delete, [.. changes, .. commitRedo], [.. commitUndo, .. undo], [], before,
            Selection.ToSnapshot()));
    }

    private static IEnumerable<FormObject> Subtree(FormObject obj) => obj.Children.SelectMany(Subtree).Prepend(obj);
}
