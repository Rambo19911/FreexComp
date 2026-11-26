using System.Globalization;
using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Designer.CodeSync;

/// <summary>
/// The code-sync service of one form document (code-sync.md §1): implements the designer's <see cref="ICodeSync"/> over the
/// unit's text buffer and keeps the in-memory set of handler candidates (DSG-CS-071).
/// </summary>
/// <param name="unit">The unit side of the form document.</param>
/// <param name="catalog">The component metadata: event signature texts and the ancestors' published members.</param>
/// <param name="showCodeAt">
/// Activates the Code view of the document with the caret at the given offset and the caret line one third from the
/// top (DSG-CS-063); null when there is no view.
/// </param>
public sealed class CodeSyncService(FormUnit unit, ComponentCatalog catalog, Action<int>? showCodeAt = null) : ICodeSync
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>DSG-CS-031: members every form has whatever the catalog knows.</summary>
    private static readonly string[] FixedMembers =
    [
        "Name", "Tag", "Owner", "Parent", "Components", "ComponentCount", "Controls", "ControlCount", "Free", "Create", "Destroy",
        "ClassName", "Show", "Hide", "Close", "Handle", "Canvas", "Font", "Caption",
    ];

    private readonly HashSet<string> _candidates = new(Names);

    public FormUnit Unit { get; } = unit ?? throw new ArgumentNullException(nameof(unit));

    /// <remarks>Replaced when the metadata is reloaded (DSG-TP-008).</remarks>
    public ComponentCatalog Catalog { get; set; } = catalog ?? throw new ArgumentNullException(nameof(catalog));

    /// <summary>False for a form opened without its unit (code-sync.md §8 <c>NoUnit</c>): every edit is refused.</summary>
    public bool HasUnit { get; init; } = true;

    /// <summary><see cref="ICodeSync.IsUnitAvailable"/>.</summary>
    public bool IsUnitAvailable => HasUnit;

    /// <summary>The form model the repairs of DSG-CS-091 (a) reconcile against (set by the form document).</summary>
    public Func<FormDocument?>? Form { get; set; }

    /// <summary>
    /// DSG-CS-091 (a): set after open and after a Text-view model replacement; the next mutating designer action repairs
    /// the form class first (<see cref="RepairBeforeEdit"/>) and clears it.
    /// </summary>
    public bool RepairPending { get; set; }

    /// <summary>code-sync.md §8, computed from a fresh parse.</summary>
    public CodeSyncState State => HasUnit ? Unit.Locate().State : CodeSyncState.NoUnit;

    /// <summary>DSG-CS-091 (a): see <see cref="ICodeSync.RepairBeforeEdit"/>; a refused repair (DSG-CS-004) edits nothing.</summary>
    public IReadOnlyList<TextEditRecord> RepairBeforeEdit()
    {
        if (!RepairPending)
        {
            return [];
        }

        RepairPending = false;
        if (Form?.Invoke() is not { } form || State != CodeSyncState.Ok)
        {
            return [];
        }

        try
        {
            return Reconcile(form, ReconcileMode.Repair).Edits;
        }
        catch (CodeSyncException)
        {
            return [];
        }
    }

    /// <summary>DSG-CS-071: the per-document, not persisted set of handler names the designer touched in this session.</summary>
    public IReadOnlySet<string> HandlerCandidates => _candidates;

    /// <summary>Setting <c>designer.removeEmptyHandlersOnSave</c> (default true, DSG-CS-076).</summary>
    public bool RemoveEmptyHandlersOnSave { get; set; } = true;

    /// <summary>
    /// DSG-CS-031: a field, method or other member (property, any section) of the form class; else a published property or
    /// event of the root class as the catalog knows it, or one of the fixed names → <see cref="MemberLookup.Other"/>.
    /// </summary>
    public MemberLookup LookupMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var info = Unit.Locate().FormClass;
        if (info is not null)
        {
            switch (SyntaxEdits.FindMember(info.Class, name))
            {
                case FieldDeclarationSyntax:
                    return MemberLookup.Field;
                case MethodDeclarationSyntax:
                    return MemberLookup.Method;
                case not null:
                    return MemberLookup.Other;
            }
        }

        var root = Catalog.Find(info?.ClassName ?? Unit.FormTypeName) ?? (info?.AncestorName is { } ancestor ? Catalog.Find(ancestor) : null);
        var inherited = root is not null && (root.FindProperty(name) is not null || root.FindEvent(name) is not null);
        return inherited || FixedMembers.Contains(name, Names) ? MemberLookup.Other : MemberLookup.None;
    }

    /// <summary>
    /// DSG-CS-050: the methods of the form class compatible with the event signature type <paramref name="eventSignature"/>
    /// (resolved through the catalog; a procedural text is used as is), sorted by name; empty unless the state is <c>Ok</c>.
    /// </summary>
    public IReadOnlyList<string> GetCompatibleMethods(string eventSignature)
    {
        ArgumentNullException.ThrowIfNull(eventSignature);
        return Unit.Locate().FormClass is { } info
            ? HandlerEdits.GetCompatibleMethods(info, Catalog.FindEventSignature(eventSignature) ?? eventSignature)
            : [];
    }

    /// <summary>
    /// The form file the <c>{$FORM}</c> directive names (DSG-CS-020); default: the unit's base name + <c>.form</c>.
    /// </summary>
    public string FormFileName { get; set; } = System.IO.Path.GetFileNameWithoutExtension(unit.Path) + ".form";

    /// <summary>
    /// code-sync.md §6.1: declaration and stub (only the missing parts); the name becomes a candidate (DSG-CS-071).
    /// </summary>
    /// <exception cref="CodeSyncException">The edit would add syntax errors (DSG-CS-004); nothing was changed.</exception>
    public IReadOnlyList<TextEditRecord> CreateHandler(string methodName, string signatureText) =>
        Run(() => CreateHandlerCore(methodName, signatureText));

    /// <summary>DSG-CS-120, 121: the hiding method of an ancestor's handler (stub with <c>inherited;</c>); a candidate too (DSG-CS-071).</summary>
    /// <exception cref="CodeSyncException">The edit would add syntax errors (DSG-CS-004); nothing was changed.</exception>
    public IReadOnlyList<TextEditRecord> CreateInheritedHandler(string methodName, string signatureText) =>
        Run(() => CreateHandlerCore(methodName, signatureText, callInherited: true));

    /// <summary>DSG-CS-066; a candidate keeps being one under its new name.</summary>
    public IReadOnlyList<TextEditRecord> RenameHandler(string oldName, string newName) => Run(() => RenameHandlerCore(oldName, newName));

    /// <summary>
    /// code-sync.md §4.2 and §5: the fields, then the units of their classes in the interface <c>uses</c> clause, as one
    /// code-editor undo group.
    /// </summary>
    public AddFieldsResult AddFields(IReadOnlyList<(string Name, string TypeName, string Unit)> fields) =>
        Run(() => AddFieldsCore(fields));

    /// <summary><see cref="ICodeSync.AddFields"/>: the edits of <see cref="AddFields(IReadOnlyList{ValueTuple{string, string, string}})"/>.</summary>
    IReadOnlyList<TextEditRecord> ICodeSync.AddFields(IReadOnlyList<CodeSyncField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return AddFields([.. fields.Select(f => (f.Name, f.TypeName, f.Unit))]).Edits;
    }

    /// <summary>code-sync.md §4.3.</summary>
    public IReadOnlyList<TextEditRecord> RemoveFields(IReadOnlyList<string> names) => Run(() => FieldEdits.RemoveFields(Unit, names));

    /// <summary>code-sync.md §4.4 DSG-CS-045.</summary>
    public FieldRenameResult RenameField(string oldName, string newName) => Run(() => FieldEdits.RenameField(Unit, oldName, newName));

    /// <summary>DSG-CS-035 <c>Fix Declaration</c>: the field's declared type becomes <paramref name="typeName"/>.</summary>
    public IReadOnlyList<TextEditRecord> FixFieldType(string name, string typeName) => Run(() => FieldEdits.FixFieldType(Unit, name, typeName));

    /// <summary>DSG-CS-046.</summary>
    public FormRenameResult RenameFormClassAndVariable(string oldFormName, string newFormName) =>
        Run(() => FieldEdits.RenameFormClassAndVariable(Unit, oldFormName, newFormName));

    /// <summary>code-sync.md §9 against <see cref="FormFileName"/>; a repair is one code-editor undo group.</summary>
    public ReconcileResult Reconcile(FormDocument form, ReconcileMode mode)
    {
        ArgumentNullException.ThrowIfNull(form);
        return mode == ReconcileMode.CheckOnly || State != CodeSyncState.Ok
            ? Reconciler.Reconcile(Unit, form, Catalog, FormFileName, mode)
            : Run(() => Reconciler.Reconcile(Unit, form, Catalog, FormFileName, mode));
    }

    /// <summary>DSG-CS-101: see <see cref="ICodeSync.RevertEdits"/>.</summary>
    public bool RevertEdits(IReadOnlyList<TextEditRecord> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return Unit.Revert(edits);
    }

    /// <summary>DSG-CS-102: see <see cref="ICodeSync.Execute"/>.</summary>
    public IReadOnlyList<TextEditRecord> Execute(IReadOnlyList<CodeSyncOp> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        foreach (var rename in operations.OfType<RenameFormClassAndVariableOp>().Take(1))
        {
            FollowFormClass(rename.OldFormName);
        }

        return Run(() => operations.SelectMany(ExecuteCore).ToList());
    }

    private IReadOnlyList<TextEditRecord> ExecuteCore(CodeSyncOp operation) => operation switch
    {
        AddFieldsOp add => AddFieldsCore([.. add.Fields.Select(f => (f.Name, f.TypeName, f.Unit))]).Edits,
        RemoveFieldsOp remove => FieldEdits.RemoveFields(Unit, remove.Names),
        RenameFieldOp rename => FieldEdits.RenameField(Unit, rename.OldName, rename.NewName).Edits,
        CreateHandlerOp create => CreateHandlerCore(create.MethodName, create.Signature),
        CreateInheritedHandlerOp create => CreateHandlerCore(create.MethodName, create.Signature, callInherited: true),
        RenameHandlerOp rename => RenameHandlerCore(rename.OldName, rename.NewName),
        RenameFormClassAndVariableOp rename => FieldEdits.RenameFormClassAndVariable(Unit, rename.OldFormName, rename.NewFormName).Edits,
        RepairOp => Form?.Invoke() is { } form ? Reconciler.Reconcile(Unit, form, Catalog, FormFileName, ReconcileMode.Repair).Edits : [],
        _ => throw new ArgumentException("Unknown code-sync operation: " + operation, nameof(operation)),
    };

    /// <summary>
    /// A redo of a form rename runs after the form's type name changed already: the class is still found under the old
    /// name <c>T&lt;OldFormName&gt;</c> (DSG-CS-046).
    /// </summary>
    private void FollowFormClass(string oldFormName)
    {
        if (Unit.Locate().State != CodeSyncState.FormClassNotFound)
        {
            return;
        }

        var current = Unit.FormTypeName;
        Unit.FormTypeName = "T" + oldFormName;
        if (Unit.Locate().State != CodeSyncState.Ok)
        {
            Unit.FormTypeName = current;
        }
    }

    private AddFieldsResult AddFieldsCore(IReadOnlyList<(string Name, string TypeName, string Unit)> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var added = FieldEdits.AddFields(Unit, [.. fields.Select(f => (f.Name, f.TypeName))]);
        var units = fields.Select(f => f.Unit).Where(u => u.Length > 0).Distinct(Names).ToList();
        return units.Count == 0 ? added : added with { Edits = [.. added.Edits, .. UsesEdits.EnsureUnits(Unit, units)] };
    }

    private IReadOnlyList<TextEditRecord> CreateHandlerCore(string methodName, string signatureText, bool callInherited = false)
    {
        var edits = HandlerEdits.CreateHandler(Unit, methodName, Catalog.FindEventSignature(signatureText) ?? signatureText, callInherited);
        _candidates.Add(methodName);

        // DSG-CS-055: the unit of the signature's types when the metadata names it (eventSignatureUnits); else nothing.
        return Catalog.FindEventSignatureUnit(signatureText) is { } unit ? [.. edits, .. UsesEdits.EnsureUnits(Unit, [unit])] : edits;
    }

    private IReadOnlyList<TextEditRecord> RenameHandlerCore(string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(oldName);
        ArgumentNullException.ThrowIfNull(newName);
        var edits = HandlerEdits.RenameHandler(Unit, oldName, newName);
        if (_candidates.Remove(oldName))
        {
            _candidates.Add(newName);
        }

        return edits;
    }

    /// <summary>
    /// DSG-CS-001 … 004: runs an edit (state <c>Ok</c> required) as one code-editor undo group; when the unit then has
    /// parser errors (it had none: state <c>Ok</c>), the group is undone and <see cref="CodeSyncException"/> names the
    /// first one. A step that found the unit broken half-way counts the same. Every refusal is a
    /// <see cref="CodeSyncException"/> (DSG-CS-102, I-220): a state other than <c>Ok</c> (an edit inside the DSG-CS-010
    /// window after the unit broke) and a name taken by another kind of member included; nothing is changed then.
    /// </summary>
    private T Run<T>(Func<T> edit)
    {
        if (Unit.Locate() is { State: not CodeSyncState.Ok } location)
        {
            throw new CodeSyncException(StateReason(location));
        }

        var version = Unit.Buffer.Version;
        T result = default!;
        Exception? failure = null;
        using (Unit.UndoGroup())
        {
            try
            {
                result = edit();
            }
            catch (Exception e) when (e is InvalidOperationException or CodeSyncException)
            {
                failure = e;
            }
        }

        if (Unit.Buffer.Version != version && (failure is not null || Unit.Locate().FirstError is not null))
        {
            var after = Unit.Locate();
            var reason = after.FirstError?.Message ?? Reason(failure!, after);
            Unit.Buffer.Undo();
            throw new CodeSyncException(reason);
        }

        return failure is null ? result : throw new CodeSyncException(Reason(failure, Unit.Locate()));
    }

    /// <summary>The <c>{0}</c> of <c>Msg.Designer.CodeSyncConflict</c> for a refused step.</summary>
    private string Reason(Exception failure, FormClassLocation location) =>
        failure is CodeSyncException refused ? refused.Reason : StateReason(location);

    /// <summary>Why the unit cannot be edited: its first syntax error, else the form class it lacks (code-sync.md §8).</summary>
    private string StateReason(FormClassLocation location) =>
        location.FirstError?.Message ?? string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_FormClassNotFound, Unit.FormTypeName);

    /// <summary>DSG-CS-070.</summary>
    public bool IsHandlerEmpty(string methodName)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        return Unit.Locate().FormClass is { } info && HandlerEdits.IsHandlerEmpty(info, methodName);
    }

    /// <summary>DSG-CS-071: an event property referencing the method was cleared, reassigned or lost with its component.</summary>
    public void NoteHandlerCandidate(string methodName)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        _candidates.Add(methodName);
    }

    /// <summary>
    /// DSG-CS-072 … 076, run by save: removes every candidate declared in the form class that is empty and that no event
    /// property of <paramref name="form"/> references, as one code-editor undo group; removed names leave the set. Returns
    /// the edits (the caller shows <c>Msg.Designer.HandlersRemovedOne</c> / <c>Many</c> for the removed count).
    /// </summary>
    public IReadOnlyList<TextEditRecord> RemoveEmptyUnreferencedHandlers(FormDocument form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!RemoveEmptyHandlersOnSave || _candidates.Count == 0)
        {
            return [];
        }

        var referenced = form.AllObjects().SelectMany(o => o.Properties)
            .Where(p => p.IsEvent)
            .Select(p => p.Value)
            .OfType<IdentValue>()
            .Select(v => v.Name)
            .ToHashSet(Names);
        var (edits, removed) = HandlerEdits.RemoveEmptyHandlers(Unit, [.. _candidates.Order(Names)], referenced);
        foreach (var name in removed)
        {
            _candidates.Remove(name);
        }

        return edits;
    }

    /// <summary>
    /// DSG-CS-063: creates the missing implementation stub of a declared method first (an undo group of its own), then shows
    /// the Code view at the caret position. A method the unit neither declares nor implements, or a state other than
    /// <c>Ok</c>, does nothing. Not part of any designer undo unit (DSG-CS-104).
    /// </summary>
    public void NavigateToHandler(string methodName)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        if (Unit.Locate().State != CodeSyncState.Ok)
        {
            return;
        }

        HandlerEdits.EnsureImplementation(Unit, methodName);
        if (Unit.Locate().FormClass is { } info && HandlerEdits.CaretOffset(info, methodName) is { } offset)
        {
            showCodeAt?.Invoke(offset);
        }
    }
}
