using FreexComp.Designer.CodeSync;
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
    /// The fields of every removed component leave the form class through code sync in the same unit (code-sync.md §4.3);
    /// when code sync refuses (DSG-CS-004) nothing is deleted and <c>Msg.Designer.CodeSyncConflict</c> is shown.
    /// </summary>
    private void Delete(string title = UndoTitles.Delete)
    {
        var members = Selection.GetEffectiveSet().Where(o => o.Parent is not null).ToList();
        if (members.Count == 0)
        {
            return;
        }

        var removed = new HashSet<FormObject>(ReferenceEqualityComparer.Instance);
        var names = new List<string>();
        foreach (var member in members)
        {
            foreach (var obj in Subtree(member))
            {
                if (removed.Add(obj))
                {
                    names.Add(obj.Name);
                }
            }
        }

        RepairFirst();
        IReadOnlyList<TextEditRecord> edits = [];
        if (_host.CodeSync.IsUnitAvailable)
        {
            try
            {
                edits = _host.CodeSync.RemoveFields(names);
            }
            catch (CodeSyncException e)
            {
                _host.SetStatus(StatusIds.CodeSyncConflict, e.Reason);
                return;
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
        var undo = ApplyEdit(changes);
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        Selection.Select(after ?? Document.Root);
        PushUnit(new DesignerUndoUnit(title, [.. changes, .. commitRedo], [.. commitUndo, .. undo], edits, before,
            Selection.ToSnapshot()) { CodeOps = edits.Count > 0 ? [new RemoveFieldsOp(names)] : [] });
    }

    private static IEnumerable<FormObject> Subtree(FormObject obj) => obj.Children.SelectMany(Subtree).Prepend(obj);
}
