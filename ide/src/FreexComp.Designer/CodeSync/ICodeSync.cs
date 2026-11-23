using FreexComp.Designer.Editing;

namespace FreexComp.Designer.CodeSync;

/// <summary>
/// What the design surface asks of code synchronization (code-sync.md §1). Declared here because the designer cannot
/// reference the IDE core (DSG-ARCH-001); implemented by the code-sync service in the IDE core, a fake in tests. Only
/// the members the surface uses so far; the other edit members join with the code-sync tasks.
/// </summary>
public interface ICodeSync
{
    /// <summary>DSG-CS-031: what the form class body (and its known ancestors) declares under <paramref name="name"/>.</summary>
    MemberLookup LookupMember(string name);

    /// <summary>
    /// The methods of the form class whose signature matches the event signature type <paramref name="eventSignature"/>
    /// (DSG-CS-050), sorted by name.
    /// </summary>
    IReadOnlyList<string> GetCompatibleMethods(string eventSignature);

    /// <summary>
    /// code-sync.md §6.1: creates the declaration and the implementation stub of handler <paramref name="methodName"/> with
    /// the signature text <paramref name="signatureText"/> (e.g. <c>procedure(Sender: TObject)</c>); returns the text edits
    /// applied (for the undo unit, code-sync.md §10).
    /// </summary>
    IReadOnlyList<TextEditRecord> CreateHandler(string methodName, string signatureText);

    /// <summary>
    /// code-sync.md §4.2 and §5 (DSG-TP-053, DSG-SUR-127): adds the fields of new components to the form class and ensures
    /// the units of their classes in the interface <c>uses</c> clause, as one code-editor undo group; returns the text edits
    /// applied (for the undo unit).
    /// </summary>
    /// <exception cref="CodeSyncException">The edits would add syntax errors; nothing was changed (DSG-CS-004).</exception>
    IReadOnlyList<TextEditRecord> AddFields(IReadOnlyList<CodeSyncField> fields);

    /// <summary>
    /// code-sync.md §4.3 (DSG-SUR-131): removes the fields of deleted components from the form class as one code-editor
    /// undo group; missing names are skipped. Returns the text edits applied (for the undo unit). Default: nothing.
    /// </summary>
    /// <exception cref="CodeSyncException">The edits would add syntax errors; nothing was changed (DSG-CS-004).</exception>
    IReadOnlyList<TextEditRecord> RemoveFields(IReadOnlyList<string> names) => [];

    /// <summary>DSG-CS-063: shows the Code view with the caret in the body of <paramref name="methodName"/>.</summary>
    void NavigateToHandler(string methodName);

    /// <summary>DSG-CS-071, 075: an event property referencing the method was lost with its deleted component. Default: nothing.</summary>
    void NoteHandlerCandidate(string methodName)
    {
    }

    /// <summary>
    /// DSG-CS-091 (a): the first mutating designer action after open or after a Text-view model replacement repairs the
    /// form class first; returns the edits of that repair (empty: nothing to repair, already done, or not possible) for the
    /// action's undo unit. Default: nothing to repair.
    /// </summary>
    IReadOnlyList<TextEditRecord> RepairBeforeEdit() => [];

    /// <summary>False when the form has no unit (code-sync.md §8 <c>NoUnit</c>): no handlers can be created or shown.</summary>
    bool IsUnitAvailable => true;

    /// <summary>
    /// DSG-CS-101 (designer undo): reverts, in reverse order and as one code-editor undo group, each of
    /// <paramref name="edits"/> whose tracked range still holds exactly its inserted text; the others are skipped. False
    /// when at least one was skipped (the caller shows <c>Msg.Designer.UndoCodeSkipped</c>).
    /// </summary>
    bool RevertEdits(IReadOnlyList<TextEditRecord> edits);

    /// <summary>
    /// DSG-CS-102 (designer redo): runs the logical operations again against the current text, as one code-editor undo
    /// group, and returns the fresh (tracked) records.
    /// </summary>
    /// <exception cref="CodeSyncException">The edits would add syntax errors; nothing was changed (DSG-CS-004).</exception>
    IReadOnlyList<TextEditRecord> Execute(IReadOnlyList<CodeSyncOp> operations);
}

/// <summary>
/// DSG-CS-004: an edit would have added parser errors to the unit and was rolled back; <see cref="Reason"/> is the first
/// new diagnostic's message (<c>{0}</c> of <c>Msg.Designer.CodeSyncConflict</c>).
/// </summary>
public sealed class CodeSyncException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

/// <summary>One field to add: component name, class name and the unit declaring the class (DSG-CS-051; empty = none).</summary>
public sealed record CodeSyncField(string Name, string TypeName, string Unit);

/// <summary>A logical code-sync operation of an undo unit, re-executed on redo (DSG-CS-102).</summary>
public abstract record CodeSyncOp;

public sealed record AddFieldsOp(IReadOnlyList<CodeSyncField> Fields) : CodeSyncOp;

public sealed record RemoveFieldsOp(IReadOnlyList<string> Names) : CodeSyncOp;

public sealed record RenameFieldOp(string OldName, string NewName) : CodeSyncOp;

/// <summary><see cref="Signature"/>: the event signature type name or a procedural signature text.</summary>
public sealed record CreateHandlerOp(string MethodName, string Signature) : CodeSyncOp;

public sealed record RenameHandlerOp(string OldName, string NewName) : CodeSyncOp;

public sealed record RenameFormClassAndVariableOp(string OldFormName, string NewFormName) : CodeSyncOp;

/// <summary>DSG-CS-091 (a): the repair that ran before the unit's action (re-run on redo against the then current form).</summary>
public sealed record RepairOp : CodeSyncOp;

/// <summary>code-sync.md §9: report only (no edits), or repair the form class.</summary>
public enum ReconcileMode { CheckOnly, Repair }

/// <summary>The kind of form-class member a name refers to (code-sync.md §1 <c>LookupMember</c>).</summary>
public enum MemberLookup { None, Field, Method, Other }
