using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;

namespace FreexComp.Ide.Core.Designer.CodeSync;

/// <summary>code-sync.md §8: whether the designer can edit the unit.</summary>
public enum CodeSyncState { Ok, UnitHasErrors, FormClassNotFound, NoUnit }

/// <summary>One name of a field declaration in the managed section (a multi-name declaration gives one entry per name).</summary>
public sealed record FormClassField(string Name, string TypeText, FieldDeclarationSyntax Declaration);

/// <summary>
/// A method declared anywhere in the form class. <see cref="Section"/>: <c>""</c> for the managed (default) section, else
/// the visibility text as written in lower case (<c>private</c>, <c>strict protected</c>, <c>published</c> …).
/// <see cref="Signature"/>: the heading without the name, e.g. <c>procedure(Sender: TObject)</c>.
/// </summary>
public sealed record FormClassMethod(string Name, string Signature, string Section, MethodDeclarationSyntax Declaration);

/// <summary>DSG-CS-014: what code sync knows about the form class of a unit (one parse; nodes of that tree).</summary>
public sealed record FormClassInfo(
    SyntaxTree Tree,
    UnitSyntax Unit,
    TypeDeclarationSyntax Declaration,
    ClassTypeSyntax Class,
    string ClassName,
    string? AncestorName,
    IReadOnlyList<FormClassField> Fields,
    IReadOnlyList<FormClassMethod> Methods,
    IReadOnlyDictionary<string, RoutineDeclarationSyntax> Implementations,
    UsesClauseSyntax? UsesClause,
    DirectiveTriviaInfo? FormDirective,
    VarDeclarationSyntax? FormVariable);

/// <summary>The result of <see cref="FormClassLocator.Locate"/>: the state, the class when it is <c>Ok</c>, the first parser error.</summary>
public sealed record FormClassLocation(CodeSyncState State, FormClassInfo? FormClass, Diagnostic? FirstError);

/// <summary>Locates the form class of a unit on its syntax tree (code-sync.md §2, DSG-CS-011 … 014).</summary>
public static class FormClassLocator
{
    /// <summary>The absolute path a buffer without a file is parsed under (the compiler requires one).</summary>
    public const string PlaceholderPath = @"C:\Unit.pas";

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>Parses <paramref name="text"/> (no file system: <c>{$I}</c> files are not read) and locates the form class.</summary>
    public static FormClassLocation Locate(string text, string rootTypeName, string path = PlaceholderPath) =>
        Locate(SyntaxTree.Parse(SourceText.From(path, text)), rootTypeName);

    /// <summary>
    /// DSG-CS-011: any parser diagnostic of severity error → <c>UnitHasErrors</c>. DSG-CS-012: the first complete class type
    /// declaration of the interface section named as <paramref name="rootTypeName"/> (unit qualifier stripped), else
    /// <c>FormClassNotFound</c>.
    /// </summary>
    public static FormClassLocation Locate(SyntaxTree tree, string rootTypeName)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(rootTypeName);
        if (tree.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is { } error)
        {
            return new FormClassLocation(CodeSyncState.UnitHasErrors, null, error);
        }

        var className = rootTypeName[(rootTypeName.LastIndexOf('.') + 1)..];
        if (tree.Root is not UnitSyntax unit || SyntaxEdits.FindClass(unit, className) is not { Type: ClassTypeSyntax cls } declaration)
        {
            return new FormClassLocation(CodeSyncState.FormClassNotFound, null, null);
        }

        var name = declaration.Identifier.Text;
        var fields = SyntaxEdits.GetDefaultSectionMembers(cls).OfType<FieldDeclarationSyntax>()
            .SelectMany(f => f.Identifiers.Select(i => new FormClassField(i.Identifier.Text, f.Type.ToString(), f)))
            .ToList();
        var methods = new List<FormClassMethod>();
        foreach (var member in cls.Members)
        {
            if (member is VisibilitySectionSyntax section)
            {
                var sectionText = (section.StrictKeyword is { } strict ? strict.Text + " " : "") + section.VisibilityKeyword.Text;
                methods.AddRange(section.Members.OfType<MethodDeclarationSyntax>().Select(m => Method(m, sectionText.ToLowerInvariant())));
            }
            else if (member is MethodDeclarationSyntax method)
            {
                methods.Add(Method(method, ""));
            }
        }

        var implementations = new Dictionary<string, RoutineDeclarationSyntax>(Names);
        foreach (var routine in unit.ImplementationSection.Declarations.OfType<RoutineDeclarationSyntax>())
        {
            if (routine.Heading.Name is QualifiedNameSyntax { Left: SimpleNameSyntax qualifier } qualified
                && Names.Equals(qualifier.Identifier.Text, name))
            {
                implementations.TryAdd(qualified.Right.Identifier.Text, routine);
            }
        }

        var variable = unit.InterfaceSection.Declarations.OfType<VarSectionSyntax>().SelectMany(s => s.Declarations)
            .FirstOrDefault(v => v.Type is NamedTypeSyntax { Name: IdentifierNameSyntax type } && Names.Equals(type.Identifier.Text, name));
        var info = new FormClassInfo(tree, unit, declaration, cls, name, cls.Heritage?.Types.FirstOrDefault()?.ToString(), fields, methods,
            implementations, unit.InterfaceSection.UsesClause, SyntaxEdits.FindDirective(unit.ImplementationSection, "FORM"), variable);
        return new FormClassLocation(CodeSyncState.Ok, info, null);
    }

    private static FormClassMethod Method(MethodDeclarationSyntax method, string section)
    {
        var heading = method.Heading;
        var name = heading.Name is SimpleNameSyntax simple ? simple.Identifier.Text : heading.Name.ToString();
        var signature = heading.Keyword.Text.ToLowerInvariant() + (heading.ParameterList?.ToString() ?? "")
            + (heading.ResultType is { } result ? ": " + result : "");
        return new FormClassMethod(name, signature, section, method);
    }
}
