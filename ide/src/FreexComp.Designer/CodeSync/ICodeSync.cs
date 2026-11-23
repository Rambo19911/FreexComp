namespace FreexComp.Designer.CodeSync;

/// <summary>
/// What the design surface asks of code synchronization (code-sync.md §1). Declared here because the designer cannot
/// reference the IDE core (DSG-ARCH-001); implemented by the code-sync service in the IDE core, a fake in tests. Only
/// the queries the surface uses so far; the edit members join with the code-sync tasks.
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
}

/// <summary>The kind of form-class member a name refers to (code-sync.md §1 <c>LookupMember</c>).</summary>
public enum MemberLookup { None, Field, Method, Other }
