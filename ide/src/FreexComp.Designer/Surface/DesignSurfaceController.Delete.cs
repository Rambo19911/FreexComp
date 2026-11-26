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
        if (members.Count > 0)
        {
            DeleteObjects(members, null, null, title);
        }
    }

    /// <summary>
    /// The delete of <paramref name="members"/> with their subtrees (DSG-SUR-130 … 132) as one unit, followed in the same
    /// unit by <paramref name="after"/>'s changes (DSG-SUR-193: the neighbour page becomes active). The selection becomes
    /// <paramref name="select"/>, or the nearest remaining ancestor of the primary when null. False when the form is
    /// read-only or code sync refused (nothing is deleted then).
    /// </summary>
    public bool DeleteObjects(IReadOnlyList<FormObject> members, FormObject? select, Func<IReadOnlyList<FormChange>>? after = null,
        string title = UndoTitles.Delete)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (IsReadOnly || members.Count == 0 || members.Any(m => m.Parent is null))
        {
            return false;
        }

        if (members.FirstOrDefault(IsStructureFixed) is { } inherited)
        {
            ReportFixed(inherited, newChildren: false); // DSG-SUR-227, 238
            return false;
        }

        var removed = new HashSet<FormObject>(ReferenceEqualityComparer.Instance);
        var names = new List<string>();
        foreach (var member in members)
        {
            foreach (var obj in Subtree(member))
            {
                if (removed.Add(obj) && !obj.IsInherited)
                {
                    names.Add(obj.Name); // DSG-CS-112: inherited objects have no fields
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
                _host.ShowErrorDialog(StatusIds.CodeSyncConflict, e.Reason);
                return false;
            }
        }

        // DSG-CS-075: the handler code of deleted components stays; their handlers become candidates (DSG-CS-071).
        foreach (var obj in removed)
        {
            foreach (var property in obj.Properties)
            {
                if (property.Value is IdentValue { Name: var method } && obj.Info?.FindEvent(property.Path) is not null)
                {
                    _host.CodeSync.NoteHandlerCandidate(method);
                }
            }
        }

        var changes = new List<FormChange>(ReferenceClearing.Changes(Document, removed));
        foreach (var parent in members.Where(TabOrderRules.IsFocusable).Select(m => m.Parent!).Distinct())
        {
            changes.AddRange(TabOrderRules.Renumber(parent, members));
        }

        changes.AddRange(members.Select(m => new RemoveObject(m.QualifiedName)));

        if (select is null)
        {
            select = Selection.Primary.Parent;
            while (select is not null && removed.Contains(select))
            {
                select = select.Parent;
            }
        }

        var before = Selection.ToSnapshot();
        var undo = new List<FormChange>(ApplyEdit(changes));
        if (after?.Invoke() is { Count: > 0 } more)
        {
            changes.AddRange(more);
            undo.InsertRange(0, ApplyEdit(more));
        }

        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        Selection.Select(select ?? Document.Root);
        PushUnit(new DesignerUndoUnit(title, [.. changes, .. commitRedo], [.. commitUndo, .. undo], edits, before,
            Selection.ToSnapshot()) { CodeOps = edits.Count > 0 ? [new RemoveFieldsOp(names)] : [] });
        return true;
    }

    private static IEnumerable<FormObject> Subtree(FormObject obj) => obj.Children.SelectMany(Subtree).Prepend(obj);
}
