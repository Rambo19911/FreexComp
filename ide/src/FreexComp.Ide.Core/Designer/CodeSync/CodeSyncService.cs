using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;

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

    public ComponentCatalog Catalog { get; } = catalog ?? throw new ArgumentNullException(nameof(catalog));

    /// <summary>code-sync.md §8, computed from a fresh parse.</summary>
    public CodeSyncState State => Unit.Locate().State;

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

    /// <summary>code-sync.md §6.1: declaration and stub (only the missing parts); the name becomes a candidate (DSG-CS-071).</summary>
    public IReadOnlyList<TextEditRecord> CreateHandler(string methodName, string signatureText)
    {
        var edits = HandlerEdits.CreateHandler(Unit, methodName, Catalog.FindEventSignature(signatureText) ?? signatureText);
        _candidates.Add(methodName);
        return edits;
    }

    /// <summary>DSG-CS-066; a candidate keeps being one under its new name.</summary>
    public IReadOnlyList<TextEditRecord> RenameHandler(string oldName, string newName)
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
