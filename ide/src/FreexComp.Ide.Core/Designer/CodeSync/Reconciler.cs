using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Ide.Core.Designer.CodeSync;

/// <summary>The checks of code-sync.md §9.</summary>
public enum ReconcileIssueKind
{
    /// <summary>A component without field (repaired: field added, unit ensured).</summary>
    MissingField,

    /// <summary>A managed-section field of a catalog class that names no component (repaired: removed).</summary>
    OrphanField,

    /// <summary>A field typed differently from its component (never auto-fixed, DSG-CS-035).</summary>
    FieldTypeMismatch,

    /// <summary>An event property naming a method the class lacks, or one declared without implementation (repaired: stub).</summary>
    MissingHandler,

    /// <summary>An event property naming a member whose signature does not fit the event (never auto-fixed).</summary>
    IncompatibleHandler,

    /// <summary>The <c>{$FORM}</c> directive is missing or names another file (repaired, §3); <see cref="ReconcileIssue.Name"/> = the file name.</summary>
    FormDirective,
}

/// <summary>
/// One finding of <see cref="Reconciler.Reconcile"/>: <see cref="Name"/> is the field, method or form file name;
/// <see cref="Diagnostic"/> the form diagnostic <c>F0104</c> / <c>F0108</c> of an event property (form-format.md §5);
/// <see cref="Mismatch"/> the declared and expected type of a <see cref="ReconcileIssueKind.FieldTypeMismatch"/>.
/// </summary>
public sealed record ReconcileIssue(ReconcileIssueKind Kind, string Name, FormDiagnostic? Diagnostic = null, FieldMismatch? Mismatch = null);

/// <summary>The issues found before any repair and the text edits a repair applied (one code-editor undo group).</summary>
public sealed record ReconcileResult(IReadOnlyList<ReconcileIssue> Issues, IReadOnlyList<TextEditRecord> Edits);

/// <summary>
/// code-sync.md §9: compares the form with its form class and, in <see cref="ReconcileMode.Repair"/>, repairs the class.
/// Nothing is reported or edited unless the state is <c>Ok</c>.
/// </summary>
public static class Reconciler
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Issues in this order: fields of the components (form order: missing or mismatching), orphan fields (declaration
    /// order), event properties (form order), the directive. Repair adds the missing fields and the units of their classes
    /// (DSG-CS-051), removes the orphans, fixes the directive and creates the missing handler stubs, as one undo group.
    /// </summary>
    public static ReconcileResult Reconcile(FormUnit unit, FormDocument form, ComponentCatalog catalog, string formFileName, ReconcileMode mode)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(formFileName);
        if (unit.Locate().FormClass is not { } info)
        {
            return new ReconcileResult([], []);
        }

        var issues = new List<ReconcileIssue>();
        var addFields = new List<(string Name, string TypeName)>();
        var components = form.AllObjects().Where(o => o.Parent is not null).ToList();
        foreach (var component in components)
        {
            switch (SyntaxEdits.FindMember(info.Class, component.Name))
            {
                case null:
                    issues.Add(new ReconcileIssue(ReconcileIssueKind.MissingField, component.Name));
                    addFields.Add((component.Name, FieldEdits.StripUnit(component.TypeName)));
                    break;
                case FieldDeclarationSyntax field when !Names.Equals(FieldEdits.TypeIdentifier(field.Type), FieldEdits.StripUnit(component.TypeName)):
                    var declared = field.Identifiers.First(i => Names.Equals(i.Identifier.Text, component.Name)).Identifier.Text;
                    issues.Add(new ReconcileIssue(ReconcileIssueKind.FieldTypeMismatch, declared,
                        Mismatch: new FieldMismatch(declared, field.Type.ToString(), FieldEdits.StripUnit(component.TypeName))));
                    break;
            }
        }

        var componentNames = components.Select(c => c.Name).ToHashSet(Names);
        var orphans = info.Fields
            .Where(f => !componentNames.Contains(f.Name) && catalog.Find(FieldEdits.TypeIdentifier(f.Declaration.Type)) is not null)
            .Select(f => f.Name)
            .ToList();
        issues.AddRange(orphans.Select(name => new ReconcileIssue(ReconcileIssueKind.OrphanField, name)));

        var stubs = new List<(string Name, string Signature)>();
        foreach (var obj in form.AllObjects())
        {
            foreach (var property in obj.Properties)
            {
                if (property is not { EventInfo: { } ev, Value: IdentValue { Name: var method } })
                {
                    continue;
                }

                var signature = catalog.FindEventSignature(ev.Signature) ?? ev.Signature;
                switch (SyntaxEdits.FindMember(info.Class, method))
                {
                    case null:
                        issues.Add(new ReconcileIssue(ReconcileIssueKind.MissingHandler, method,
                            form.PropertyWarning(obj, property, "F0104", $"Method {method} not found in {info.ClassName}")));
                        AddStub(stubs, method, signature);
                        break;
                    case MethodDeclarationSyntax declaration when HandlerEdits.SignatureMatches(declaration, signature):
                        if (!info.Implementations.ContainsKey(method) && !stubs.Any(s => Names.Equals(s.Name, method)))
                        {
                            issues.Add(new ReconcileIssue(ReconcileIssueKind.MissingHandler, method));
                            AddStub(stubs, method, signature);
                        }

                        break;
                    default:
                        issues.Add(new ReconcileIssue(ReconcileIssueKind.IncompatibleHandler, method,
                            form.PropertyWarning(obj, property, "F0108", $"Method {method} is not compatible with event {property.Path}")));
                        break;
                }
            }
        }

        var directive = FormDirectiveEdits.NeedsRepair(info, formFileName);
        if (directive)
        {
            issues.Add(new ReconcileIssue(ReconcileIssueKind.FormDirective, formFileName));
        }

        if (mode == ReconcileMode.CheckOnly)
        {
            return new ReconcileResult(issues, []);
        }

        var edits = new List<TextEditRecord>();
        using (unit.UndoGroup())
        {
            if (addFields.Count > 0)
            {
                edits.AddRange(FieldEdits.AddFields(unit, addFields).Edits);
                var units = addFields.Select(f => catalog.Find(f.TypeName)?.Unit).OfType<string>().Distinct(Names).ToList();
                edits.AddRange(UsesEdits.EnsureUnits(unit, units));
            }

            if (orphans.Count > 0)
            {
                edits.AddRange(FieldEdits.RemoveFields(unit, orphans));
            }

            if (directive)
            {
                edits.AddRange(FormDirectiveEdits.EnsureFormDirective(unit, formFileName));
            }

            foreach (var (name, signature) in stubs)
            {
                edits.AddRange(HandlerEdits.CreateHandler(unit, name, signature));
            }
        }

        return new ReconcileResult(issues, edits);
    }

    /// <summary>Each method once; an event whose signature is unknown or not procedural gets no stub.</summary>
    private static void AddStub(List<(string Name, string Signature)> stubs, string name, string signature)
    {
        if (HandlerEdits.ParseSignature(signature) is not null && !stubs.Any(s => Names.Equals(s.Name, name)))
        {
            stubs.Add((name, signature));
        }
    }
}
