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

        var signature = Catalog.FindEventSignature(ev.Signature) ?? ev.Signature;

        // DSG-SUR-244, DSG-CS-120: an ancestor's handler M — the descendant's method M (created as the hiding stub when the
        // class lacks it, in one "Event" unit), no form line.
        if (AncestorFormHandler(Document, obj, ev.Name) is { } inherited)
        {
            if (code.LookupMember(inherited) == MemberLookup.Method || Commit(UndoTitles.Event, [],
                    () => code.CreateInheritedHandler(inherited, signature), [new CreateInheritedHandlerOp(inherited, signature)]))
            {
                code.NavigateToHandler(inherited);
            }

            return;
        }

        RepairFirst(); // DSG-CS-091 (a): before the handler is created
        var compatible = code.GetCompatibleMethods(ev.Signature);
        var name = DefaultHandlerName(obj, ev, compatible);
        IReadOnlyList<TextEditRecord> edits = [];
        if (!compatible.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                edits = code.CreateHandler(name, signature);
            }
            catch (CodeSyncException e)
            {
                _host.ShowErrorDialog(StatusIds.CodeSyncConflict, e.Reason); // DSG-CS-004: nothing is changed
                return;
            }
        }

        var selection = Selection.ToSnapshot();
        FormChange[] changes = [new SetProperty(obj.QualifiedName, ev.Name, new IdentValue(name))];
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
    /// that name exists that is not a compatible method, digits 2, 3, … are appended. DSG-CS-122: in a descendant no handler
    /// name of the ancestor chain is taken.
    /// </summary>
    private string DefaultHandlerName(FormObject obj, EventInfo ev, IReadOnlyList<string> compatible)
    {
        var eventPart = ev.Name.StartsWith("On", StringComparison.OrdinalIgnoreCase) ? ev.Name[2..] : ev.Name;
        var stem = HandlerComponentPart(obj) + eventPart;
        var taken = AncestorHandlerNames(Document); // DSG-CS-122
        bool Usable(string name) => !taken.Contains(name) && (compatible.Contains(name, StringComparer.OrdinalIgnoreCase)
            || (_host.CodeSync.LookupMember(name) == MemberLookup.None && Document.FindByName(name) is null));

        var candidate = stem;
        for (var n = 2; !Usable(candidate); n++)
        {
            candidate = stem + n;
        }

        return candidate;
    }

    /// <summary>
    /// The component part of a default handler name: <c>Form</c> for the root, <c>&lt;Instance&gt;&lt;Component&gt;</c> for
    /// an inherited object (DSG-CS-113: <c>BillingAddress.StreetEdit</c> → <c>BillingAddressStreetEdit</c>), else the name.
    /// </summary>
    public static string HandlerComponentPart(FormObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.Parent is null ? "Form" : obj.QualifiedName.Replace(".", "", StringComparison.Ordinal);
    }

    /// <summary>
    /// DSG-CS-120, DSG-OI-125: the method an ancestor form assigns to <paramref name="eventName"/> of <paramref name="obj"/> —
    /// the inherited root or an inherited component outside every frame instance of a descendant, with no line of its own
    /// for the event — when the level storing it belongs to the document's ancestor chain; null otherwise (no handler, an
    /// own line, a handler of a frame, or a component inside an instance: DSG-OI-120 applies there).
    /// </summary>
    public static string? AncestorFormHandler(FormDocument document, FormObject obj, string eventName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(eventName);
        if (!(obj.IsInheritedRoot || ShowsInheritedMarker(obj)) || obj.FindProperty(eventName) is not null
            || obj.InheritedHandler(eventName) is not { } handler)
        {
            return null;
        }

        for (var level = document.Ancestor; level is not null; level = level.Document.Ancestor)
        {
            if (string.Equals(level.ClassName, handler.Frame.ClassName, StringComparison.OrdinalIgnoreCase))
            {
                return handler.Method;
            }
        }

        return null;
    }

    /// <summary>
    /// DSG-CS-122: the handler names stored at the levels of a descendant's ancestor chain (the event values of every object's
    /// <see cref="FormObject.InheritedFrom"/> chain in the merged model); empty for a document that is no descendant.
    /// </summary>
    public static IReadOnlySet<string> AncestorHandlerNames(FormDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.Ancestor is null)
        {
            return names;
        }

        foreach (var obj in document.AllObjects())
        {
            for (var level = obj.InheritedFrom; level is not null; level = level.InheritedFrom)
            {
                foreach (var property in level.Properties)
                {
                    if (property is { IsEvent: true, Value: IdentValue { Name: var method } })
                    {
                        names.Add(method);
                    }
                }
            }
        }

        return names;
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

        if (Selection.Primary.Info?.ComponentEditor is not null)
        {
            // DSG-SUR-196: a class with a component editor opens it instead of the default handler.
            if (Selection.Items.Count != 1 || !CanExecute(Commands.EditComponent, null))
            {
                return false;
            }

            Execute(Commands.EditComponent);
            return true;
        }

        if (!CanExecute(Commands.CreateDefaultHandler))
        {
            return false;
        }

        Execute(Commands.CreateDefaultHandler);
        return true;
    }
}
