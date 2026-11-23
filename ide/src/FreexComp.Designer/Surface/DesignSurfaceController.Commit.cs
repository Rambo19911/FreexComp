using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;

namespace FreexComp.Designer.Surface;

/// <summary>
/// The undo units of the surface and of the Object Inspector (form-document.md §8, object-inspector.md §9) and the repair
/// before the first mutating action (code-sync.md DSG-CS-091 a).
/// </summary>
public sealed partial class DesignSurfaceController
{
    // The repair edits made before the action that is being built; they join that action's undo unit.
    private List<TextEditRecord> _repairEdits = [];

    /// <summary>
    /// An Object Inspector commit (object-inspector.md §9) as one undo unit: the repair of DSG-CS-091 (a) when due, then
    /// <paramref name="code"/> (the code-sync edits, re-run on redo through <paramref name="codeOps"/>), then
    /// <paramref name="changes"/>, the measured auto-size of controls whose text-size properties changed (DSG-SUR-088) and the
    /// layout commit (DSG-LAY-040). False when the form is read-only or code sync
    /// refused the edit (DSG-CS-004: <c>Msg.Designer.CodeSyncConflict</c>; nothing is changed then).
    /// </summary>
    /// <exception cref="InvalidFormChangeException">A change is invalid (a caller error); the code edits were reverted.</exception>
    public bool Commit(string titleKey, IReadOnlyList<FormChange> changes, Func<IReadOnlyList<TextEditRecord>>? code = null,
        IReadOnlyList<CodeSyncOp>? codeOps = null)
    {
        ArgumentNullException.ThrowIfNull(titleKey);
        ArgumentNullException.ThrowIfNull(changes);
        if (IsReadOnly)
        {
            return false;
        }

        RepairFirst();
        IReadOnlyList<TextEditRecord> edits = [];
        if (code is not null)
        {
            try
            {
                edits = code();
            }
            catch (CodeSyncException e)
            {
                _host.SetStatus(StatusIds.CodeSyncConflict, e.Reason);
                return false;
            }
        }

        var before = Selection.ToSnapshot();
        IReadOnlyList<FormChange> undo;
        try
        {
            undo = Editor.Apply(changes);
        }
        catch (InvalidFormChangeException)
        {
            _host.CodeSync.RevertEdits(edits);
            throw;
        }

        var (sizeRedo, sizeUndo) = ApplyAutoSize(AutoSizeTargets(changes)); // DSG-SUR-088
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        PushUnit(new DesignerUndoUnit(titleKey, [.. changes, .. sizeRedo, .. commitRedo], [.. commitUndo, .. sizeUndo, .. undo], edits, before,
            Selection.ToSnapshot())
        {
            CodeOps = codeOps ?? [],
        });
        return true;
    }

    /// <summary>
    /// DSG-TP-053, DSG-SUR-127, DSG-SUR-057: the repair when due, then the fields of the components about to be created
    /// (<paramref name="components"/>, each with its subtree, pre-order) and the units of their classes through code sync;
    /// <paramref name="edits"/> and <paramref name="ops"/> belong into the action's undo unit. Without a unit nothing is
    /// added. False when code sync refused the edit (DSG-CS-004: <c>Msg.Designer.CodeSyncConflict</c>): the components
    /// are not created then.
    /// </summary>
    private bool AddFieldsFor(IEnumerable<FormObjectData> components, out IReadOnlyList<TextEditRecord> edits, out IReadOnlyList<CodeSyncOp> ops)
    {
        edits = [];
        ops = [];
        RepairFirst();
        var fields = new List<CodeSyncField>();
        void Collect(FormObjectData data)
        {
            var typeName = data.TypeName[(data.TypeName.LastIndexOf('.') + 1)..]; // DSG-CS-030: unit qualifier stripped
            fields.Add(new CodeSyncField(data.Name, typeName, Catalog.Find(data.TypeName)?.Unit ?? ""));
            foreach (var child in data.Children)
            {
                Collect(child);
            }
        }

        foreach (var component in components)
        {
            Collect(component);
        }

        if (fields.Count == 0 || !_host.CodeSync.IsUnitAvailable)
        {
            return true;
        }

        try
        {
            edits = _host.CodeSync.AddFields(fields);
        }
        catch (CodeSyncException e)
        {
            _host.SetStatus(StatusIds.CodeSyncConflict, e.Reason);
            return false;
        }

        ops = edits.Count > 0 ? [new AddFieldsOp(fields)] : [];
        return true;
    }

    /// <summary>DSG-CS-091 (a): asks code sync for the pending repair; its edits wait for the next pushed unit.</summary>
    private void RepairFirst()
    {
        if (_host.CodeSync.RepairBeforeEdit() is { Count: > 0 } edits)
        {
            _repairEdits.AddRange(edits);
        }
    }

    /// <summary>A mutation of a surface action: the repair first (when due), then the changes.</summary>
    private IReadOnlyList<FormChange> ApplyEdit(IReadOnlyList<FormChange> changes)
    {
        RepairFirst();
        return Editor.Apply(changes);
    }

    /// <summary>Pushes the unit of an action, holding the repair edits made before it; returns the unit pushed.</summary>
    private DesignerUndoUnit PushUnit(DesignerUndoUnit unit)
    {
        unit = WithRepair(unit);
        Undo.Push(unit);
        return unit;
    }

    /// <summary>The unit with the pending repair edits in front of its own code edits (redo re-runs the repair first).</summary>
    private DesignerUndoUnit WithRepair(DesignerUndoUnit unit)
    {
        if (_repairEdits.Count == 0)
        {
            return unit;
        }

        var repair = _repairEdits;
        _repairEdits = [];
        return new DesignerUndoUnit(unit.TitleKey, unit.Redo, unit.Undo, [.. repair, .. unit.CodeEdits], unit.SelectionBefore,
            unit.SelectionAfter)
        {
            TitleArgument = unit.TitleArgument,
            CodeOps = [new RepairOp(), .. unit.CodeOps],
        };
    }
}
