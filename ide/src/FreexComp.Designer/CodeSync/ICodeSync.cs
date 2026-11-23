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

    /// <summary>DSG-CS-063: shows the Code view with the caret in the body of <paramref name="methodName"/>.</summary>
    void NavigateToHandler(string methodName);
}

/// <summary>The kind of form-class member a name refers to (code-sync.md §1 <c>LookupMember</c>).</summary>
public enum MemberLookup { None, Field, Method, Other }
