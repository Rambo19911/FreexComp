using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Editing;
using TextChange = FreexComp.Compiler.Syntax.TextChange;

namespace FreexComp.Ide.Core.Designer.CodeSync;

/// <summary>DSG-CS-035: a field of that name exists with another type (<c>InfoBar.FieldTypeMismatch</c>); nothing was edited.</summary>
public sealed record FieldMismatch(string Name, string DeclaredType, string ExpectedType);

/// <summary>The edits applied by <see cref="FieldEdits.AddFields"/> and the fields left alone because of a type mismatch.</summary>
public sealed record AddFieldsResult(IReadOnlyList<TextEditRecord> Edits, IReadOnlyList<FieldMismatch> Mismatches);

/// <summary>
/// <see cref="FieldEdits.RenameField"/>: the edits, and whether other identifier tokens still spell the old name
/// (<c>Msg.Designer.RenameCodeNote</c>, DSG-CS-045).
/// </summary>
public sealed record FieldRenameResult(IReadOnlyList<TextEditRecord> Edits, bool CodeUsesOldName);

/// <summary><see cref="FieldEdits.RenameFormClassAndVariable"/>: the edits and the new class name, null when the class kept its name.</summary>
public sealed record FormRenameResult(IReadOnlyList<TextEditRecord> Edits, string? NewClassName);

/// <summary>
/// Field edits of the form class (code-sync.md §4) through <c>SyntaxEdits</c> (api.md CMP-250 … 252, 258). Every
/// method needs state <c>Ok</c> (DSG-CS-003), parses the current text for each step and applies all its steps as one
/// code-editor undo group (DSG-CS-001, 002).
/// </summary>
public static class FieldEdits
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// DSG-CS-035 … 038: adds one field per entry, in order, after the last field of the managed section (or at its
    /// start). An existing field of the same type is left as is; one of another type is reported, not edited. A name
    /// that is another kind of member is skipped (the caller only adds free names, DSG-CS-031).
    /// </summary>
    public static AddFieldsResult AddFields(FormUnit unit, IReadOnlyList<(string Name, string TypeName)> fields)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(fields);
        var info = unit.RequireFormClass();
        var records = new List<TextEditRecord>();
        var mismatches = new List<FieldMismatch>();
        using (unit.UndoGroup())
        {
            foreach (var (name, typeName) in fields)
            {
                switch (SyntaxEdits.FindMember(info.Class, name))
                {
                    case FieldDeclarationSyntax field:
                        if (!Names.Equals(TypeIdentifier(field.Type), StripUnit(typeName)))
                        {
                            var declared = field.Identifiers.First(i => Names.Equals(i.Identifier.Text, name)).Identifier.Text;
                            mismatches.Add(new FieldMismatch(declared, field.Type.ToString(), typeName));
                        }

                        continue;
                    case not null:
                        continue;
                }

                records.AddRange(unit.Apply([SyntaxEdits.AddField(info.Declaration, name, typeName, unit.Options(info))]));
                info = unit.RequireFormClass();
            }
        }

        return new AddFieldsResult(records, mismatches);
    }

    /// <summary>DSG-CS-035 <c>Fix Declaration</c>: the declared type of field <paramref name="name"/> becomes <paramref name="typeName"/>.</summary>
    public static IReadOnlyList<TextEditRecord> FixFieldType(FormUnit unit, string name, string typeName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var info = unit.RequireFormClass();
        return SyntaxEdits.FindMember(info.Class, name) is FieldDeclarationSyntax field
            ? unit.Apply([new TextChange(field.Type.Span, typeName)])
            : [];
    }

    /// <summary>
    /// DSG-CS-040 … 042: removes each named field wherever it is declared in the class; a single-name declaration as its
    /// whole line(s), in a multi-name declaration only the name and one comma. Missing names are skipped.
    /// </summary>
    public static IReadOnlyList<TextEditRecord> RemoveFields(FormUnit unit, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(names);
        var info = unit.RequireFormClass();
        var records = new List<TextEditRecord>();
        using (unit.UndoGroup())
        {
            foreach (var name in names)
            {
                var applied = unit.Apply(SyntaxEdits.RemoveField(info.Declaration, name));
                if (applied.Count > 0)
                {
                    records.AddRange(applied);
                    info = unit.RequireFormClass();
                }
            }
        }

        return records;
    }

    /// <summary>
    /// DSG-CS-131: the offset and length of the declared name of field <paramref name="name"/> in the unit's text — the
    /// identifier <see cref="RenameField"/> replaces — or null when the form class or the field is not found.
    /// </summary>
    public static (int Start, int Length)? FieldNameSpan(FormUnit unit, string name)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(name);
        if (unit.Locate().FormClass is not { } info)
        {
            return null;
        }

        var change = SyntaxEdits.RenameField(info.Declaration, name, name);
        return change.Span.Length > 0 ? (change.Span.Start, change.Span.Length) : null;
    }

    /// <summary>DSG-CS-045: only the identifier of the field declaration; references in code are not changed.</summary>
    public static FieldRenameResult RenameField(FormUnit unit, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var info = unit.RequireFormClass();
        var change = SyntaxEdits.RenameField(info.Declaration, oldName, newName);
        if (change.IsEmpty)
        {
            return new FieldRenameResult([], false);
        }

        var otherUses = info.Tree.Root.DescendantTokens()
            .Any(t => t.Kind == SyntaxKind.IdentifierToken && t.Span.Start != change.Span.Start && Names.Equals(t.Text, oldName));
        return new FieldRenameResult(unit.Apply([change]), otherUses);
    }

    /// <summary>
    /// DSG-CS-046 steps 2 and 3 (step 1, the form's root name, is the caller's): a class named <c>T</c> + old name becomes
    /// <c>T</c> + new name — declaration, implementation heading qualifiers, the global variable's type — when that name is
    /// free in the interface type section; an interface variable named as the old form name and typed as the form class is
    /// renamed. <see cref="FormUnit.FormTypeName"/> follows a class rename.
    /// </summary>
    public static FormRenameResult RenameFormClassAndVariable(FormUnit unit, string oldFormName, string newFormName)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var info = unit.RequireFormClass();
        var changes = new List<TextChange>();
        string? newClassName = null;
        var candidate = "T" + newFormName;
        if (Names.Equals(info.ClassName, "T" + oldFormName) && IsFreeTypeName(info, candidate))
        {
            changes.AddRange(SyntaxEdits.RenameClass(info.Unit, info.ClassName, candidate));
            newClassName = candidate;
        }

        foreach (var variable in info.Unit.InterfaceSection.Declarations.OfType<VarSectionSyntax>().SelectMany(s => s.Declarations))
        {
            if (variable.Type is NamedTypeSyntax { Name: IdentifierNameSyntax type } && Names.Equals(type.Identifier.Text, info.ClassName)
                && variable.Identifiers.FirstOrDefault(i => Names.Equals(i.Identifier.Text, oldFormName)) is { } identifier)
            {
                changes.Add(new TextChange(identifier.Identifier.Span, newFormName));
            }
        }

        var records = unit.Apply(changes);
        if (newClassName is not null)
        {
            unit.FormTypeName = newClassName;
        }

        return new FormRenameResult(records, newClassName);
    }

    /// <summary>No other type of the interface type section has that name (the form class itself may: a case-only change).</summary>
    private static bool IsFreeTypeName(FormClassInfo info, string name) =>
        !info.Unit.InterfaceSection.Declarations.OfType<TypeSectionSyntax>().SelectMany(s => s.Declarations)
            .Any(t => !ReferenceEquals(t, info.Declaration) && Names.Equals(t.Identifier.Text, name));

    /// <summary>The type name of a declaration as compared by DSG-CS-035: unit qualifiers stripped, whitespace ignored.</summary>
    internal static string TypeIdentifier(TypeSyntax type) => type switch
    {
        NamedTypeSyntax { Name: QualifiedNameSyntax q } => q.Right.Identifier.Text,
        NamedTypeSyntax { Name: SimpleNameSyntax s } => s.Identifier.Text,
        _ => string.Concat(type.DescendantTokens().Select(t => t.Text)),
    };

    internal static string StripUnit(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..].Trim();
}
