using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>The default event handler command (design-surface.md §11.4, DSG-SUR-160, 161).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>DSG-SUR-160: exactly one component, not preserved, its class has events; not read-only, not during a drag or tab-order mode.</summary>
    private bool CanCreateDefaultHandler =>
        !IsReadOnly && _host.CodeSync.IsUnitAvailable && !IsPointerActive && State != InteractionState.TabOrder && Selection.Items.Count == 1
        && Selection.Primary is { IsPreserved: false, Info.Events.IsEmpty: false };

    /// <summary>
    /// DSG-SUR-160: the event marked <c>"default"</c> nearest to the class in its ancestor chain (most-derived wins); none
    /// marked → the first event of the flattened list.
    /// </summary>
    private EventInfo DefaultEvent(ComponentInfo info)
    {
        for (var cls = info; cls is not null; cls = cls.Ancestor is { } ancestor ? Catalog.Find(ancestor) : null)
        {
            if (info.Events.FirstOrDefault(e => e.IsDefault && e.DeclaringClass.Equals(cls.ClassName, StringComparison.OrdinalIgnoreCase)) is { } declared)
            {
                return declared;
            }
        }

        return info.Events.FirstOrDefault(e => e.IsDefault) ?? info.Events[0];
    }

    /// <summary>
    /// DSG-SUR-161: an assigned default event → navigation to its method. Otherwise the handler named per DSG-OI-082 is
    /// created through code sync (or, when a compatible method of that name exists, reused without a stub), assigned in
    /// one "Event" unit holding the code edits, and navigated to.
    /// </summary>
    private void CreateDefaultHandler()
    {
        var obj = Selection.Primary;
        var ev = DefaultEvent(obj.Info!);
        var code = _host.CodeSync;
        if (obj.FindProperty(ev.Name)?.Value is IdentValue { Name: var existing })
        {
            code.NavigateToHandler(existing);
            return;
        }

        RepairFirst(); // DSG-CS-091 (a): before the handler is created
        var compatible = code.GetCompatibleMethods(ev.Signature);
        var name = DefaultHandlerName(obj, ev, compatible);
        var signature = Catalog.FindEventSignature(ev.Signature) ?? ev.Signature;
        var edits = compatible.Contains(name, StringComparer.OrdinalIgnoreCase) ? [] : code.CreateHandler(name, signature);
        var selection = Selection.ToSnapshot();
        FormChange[] changes = [new SetProperty(obj.Name, ev.Name, new IdentValue(name))];
        var undo = ApplyEdit(changes);
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        PushUnit(new DesignerUndoUnit(UndoTitles.Event, [.. changes, .. commitRedo], [.. commitUndo, .. undo], edits, selection, selection)
        {
            CodeOps = edits.Count > 0 ? [new CreateHandlerOp(name, signature)] : [],
        });
        code.NavigateToHandler(name);
    }

    /// <summary>
    /// DSG-CS-102: the code part of a redone unit runs again; when that is refused (the unit changed so that the edit would
    /// add syntax errors) the form part stays redone without code edits, and reconciliation repairs the class at save.
    /// </summary>
    private IReadOnlyList<TextEditRecord> RedoCode(IReadOnlyList<CodeSyncOp> operations)
    {
        try
        {
            return _host.CodeSync.Execute(operations);
        }
        catch (CodeSyncException)
        {
            return [];
        }
    }

    /// <summary>
    /// DSG-OI-082: <c>&lt;ComponentName&gt;&lt;EventNameWithoutLeadingOn&gt;</c> (the form root: <c>Form…</c>); while a member of
    /// that name exists that is not a compatible method, digits 2, 3, … are appended.
    /// </summary>
    private string DefaultHandlerName(FormObject obj, EventInfo ev, IReadOnlyList<string> compatible)
    {
        var eventPart = ev.Name.StartsWith("On", StringComparison.OrdinalIgnoreCase) ? ev.Name[2..] : ev.Name;
        var stem = (obj.Parent is null ? "Form" : obj.Name) + eventPart;
        bool Usable(string name) => compatible.Contains(name, StringComparer.OrdinalIgnoreCase)
            || (_host.CodeSync.LookupMember(name) == MemberLookup.None && Document.FindByName(name) is null);

        var candidate = stem;
        for (var n = 2; !Usable(candidate); n++)
        {
            candidate = stem + n;
        }

        return candidate;
    }

    /// <summary>
    /// Enter and double-click (DSG-SUR-042, DSG-SUR-160): the default handler of the selected component, unless its class
    /// has a component editor (those run <c>designer.editComponent</c>, a later task); true when the command ran.
    /// </summary>
    private bool RunDefaultAction()
    {
        if (!IsReadOnly && Selection.Items.Count == 1 && ReportPreserved())
        {
            return true; // DSG-SUR-004
        }

        if (Selection.Primary.Info?.ComponentEditor is not null || !CanExecute(Commands.CreateDefaultHandler))
        {
            return false;
        }

        Execute(Commands.CreateDefaultHandler);
        return true;
    }
}
