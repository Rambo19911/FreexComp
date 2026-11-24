using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService;

/// <summary>Item and symbol kinds shared by completion and the structure view (language-service.md §4, §7).</summary>
public enum CompletionItemKind
{
    Keyword, Unit, Type, Class, Interface, Enum, EnumMember, Constant, Variable,
    Parameter, Field, Property, Method, Routine, Snippet, Directive, Label,
}

/// <summary>A node of the structure view (CMP-743).</summary>
public sealed record DocumentSymbol(string Name, string Detail, CompletionItemKind Kind, TextSpan Span, TextSpan SelectionSpan,
                                    ImmutableArray<DocumentSymbol> Children);

/// <summary>Navigation (language-service.md §7).</summary>
public static partial class NavigationService
{
    /// <summary>
    /// CMP-743, purely syntactic: compilation unit → (per section) <c>uses</c> node → <c>interface</c> / <c>implementation</c>
    /// sections → types (class members grouped under one node per visibility section), constants, variables, routines (nested
    /// routines as children). A program lists its uses node and declarations directly under the program node.
    /// </summary>
    public static ImmutableArray<DocumentSymbol> GetDocumentSymbols(Document doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return GetDocumentSymbols(doc.SyntaxTree);
    }

    /// <inheritdoc cref="GetDocumentSymbols(Document)"/>
    public static ImmutableArray<DocumentSymbol> GetDocumentSymbols(SyntaxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        switch (tree.Root)
        {
            case UnitSyntax unit:
            {
                var children = ImmutableArray.CreateBuilder<DocumentSymbol>();
                children.Add(Section(unit.InterfaceSection.InterfaceKeyword, unit.InterfaceSection, unit.InterfaceSection.UsesClause, unit.InterfaceSection.Declarations));
                children.Add(Section(unit.ImplementationSection.ImplementationKeyword, unit.ImplementationSection, unit.ImplementationSection.UsesClause,
                                     unit.ImplementationSection.Declarations));
                return [new DocumentSymbol(unit.Name.ToString(), "", CompletionItemKind.Unit, unit.Span, unit.Name.Span, children.ToImmutable())];
            }

            case ProgramSyntax program:
            {
                var children = ImmutableArray.CreateBuilder<DocumentSymbol>();
                if (program.UsesClause is { } uses)
                {
                    children.Add(Uses(uses));
                }

                AddDeclarations(children, program.Block.Declarations);
                var name = program.Name?.Identifier.Text ?? System.IO.Path.GetFileNameWithoutExtension(tree.Text.Path);
                var selection = program.Name?.Span ?? new TextSpan(program.Span.Start, 0);
                return [new DocumentSymbol(name, "", CompletionItemKind.Unit, program.Span, selection, children.ToImmutable())];
            }

            case LibrarySyntax library:
            {
                var children = ImmutableArray.CreateBuilder<DocumentSymbol>();
                if (library.UsesClause is { } uses)
                {
                    children.Add(Uses(uses));
                }

                AddDeclarations(children, library.Block.Declarations);
                return [new DocumentSymbol(library.Name.ToString(), "", CompletionItemKind.Unit, library.Span, library.Name.Span, children.ToImmutable())];
            }

            default:
                return [];
        }
    }

    private static DocumentSymbol Section(SyntaxToken keyword, SyntaxNode section, UsesClauseSyntax? uses, SyntaxList<SyntaxNode> declarations)
    {
        var children = ImmutableArray.CreateBuilder<DocumentSymbol>();
        if (uses is not null)
        {
            children.Add(Uses(uses));
        }

        AddDeclarations(children, declarations);
        return new DocumentSymbol(keyword.Text.ToLowerInvariant(), "", CompletionItemKind.Keyword, section.Span, keyword.Span, children.ToImmutable());
    }

    private static DocumentSymbol Uses(UsesClauseSyntax uses)
    {
        ImmutableArray<DocumentSymbol> units = [.. uses.Units.Where(u => !u.Name.IsMissing).Select(u => Leaf(u.Name.ToString(), "", CompletionItemKind.Unit, u.Span, u.Name.Span))];
        return new DocumentSymbol("uses", string.Join(", ", units.Select(u => u.Name)), CompletionItemKind.Unit, uses.Span, uses.UsesKeyword.Span, units);
    }

    private static void AddDeclarations(ImmutableArray<DocumentSymbol>.Builder into, IEnumerable<SyntaxNode> declarations)
    {
        foreach (var declaration in declarations)
        {
            switch (declaration)
            {
                case TypeSectionSyntax types:
                    foreach (var type in types.Declarations)
                    {
                        into.Add(Type(type));
                    }

                    break;
                case ConstSectionSyntax constants:
                    foreach (var constant in constants.Declarations.Where(c => !c.Identifier.IsMissing))
                    {
                        into.Add(Leaf(constant.Identifier.Text, constant.Type is null ? "" : Collapse(constant.Type.ToString()), CompletionItemKind.Constant,
                                      constant.Span, constant.Identifier.Span));
                    }

                    break;
                case VarSectionSyntax variables:
                    foreach (var variable in variables.Declarations)
                    {
                        AddNames(into, variable.Identifiers, Collapse(variable.Type.ToString()), CompletionItemKind.Variable, variable.Span);
                    }

                    break;
                case RoutineDeclarationSyntax routine:
                    into.Add(Routine(routine));
                    break;
            }
        }
    }

    private static DocumentSymbol Routine(RoutineDeclarationSyntax routine)
    {
        var children = ImmutableArray.CreateBuilder<DocumentSymbol>();
        if (routine.Block is { } block)
        {
            foreach (var nested in block.Declarations.OfType<RoutineDeclarationSyntax>())
            {
                children.Add(Routine(nested));
            }
        }

        var heading = routine.Heading;
        var kind = heading.Name is QualifiedNameSyntax ? CompletionItemKind.Method : CompletionItemKind.Routine;
        return new DocumentSymbol(heading.Name.ToString(), Collapse(heading.ToString()), kind, routine.Span, heading.Name.Span, children.ToImmutable());
    }

    private static DocumentSymbol Type(TypeDeclarationSyntax declaration)
    {
        var children = ImmutableArray.CreateBuilder<DocumentSymbol>();
        CompletionItemKind kind;
        string detail;
        switch (declaration.Type)
        {
            case ClassTypeSyntax @class:
                kind = CompletionItemKind.Class;
                detail = "class" + (@class.Heritage is { } heritage ? Collapse(heritage.ToString()) : "");
                AddMembers(children, @class.Members);
                break;
            case InterfaceTypeSyntax @interface:
                kind = CompletionItemKind.Interface;
                detail = "interface" + (@interface.Heritage is { } parents ? Collapse(parents.ToString()) : "");
                AddMembers(children, @interface.Members);
                break;
            case RecordTypeSyntax record:
                kind = CompletionItemKind.Type;
                detail = "record";
                AddMembers(children, record.Members);
                break;
            case HelperTypeSyntax helper:
                kind = CompletionItemKind.Type;
                detail = Collapse(helper.Keyword.Text + " helper for " + helper.ExtendedType);
                AddMembers(children, helper.Members);
                break;
            case EnumTypeSyntax @enum:
                kind = CompletionItemKind.Enum;
                detail = "";
                foreach (var member in @enum.Members.Where(m => !m.Identifier.IsMissing))
                {
                    children.Add(Leaf(member.Identifier.Text, "", CompletionItemKind.EnumMember, member.Span, member.Identifier.Span));
                }

                break;
            default:
                kind = CompletionItemKind.Type;
                detail = Collapse(declaration.Type.ToString());
                break;
        }

        return new DocumentSymbol(declaration.Identifier.Text, detail, kind, declaration.Span, declaration.Identifier.Span, children.ToImmutable());
    }

    private static void AddMembers(ImmutableArray<DocumentSymbol>.Builder into, IEnumerable<SyntaxNode> members)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case VisibilitySectionSyntax section:
                {
                    var grouped = ImmutableArray.CreateBuilder<DocumentSymbol>();
                    AddMembers(grouped, section.Members);
                    var name = section.StrictKeyword is { } strict ? strict.Text.ToLowerInvariant() + " " + section.VisibilityKeyword.Text.ToLowerInvariant()
                                                                   : section.VisibilityKeyword.Text.ToLowerInvariant();
                    into.Add(new DocumentSymbol(name, "", CompletionItemKind.Keyword, section.Span, section.VisibilityKeyword.Span, grouped.ToImmutable()));
                    break;
                }

                case FieldDeclarationSyntax field:
                    AddNames(into, field.Identifiers, Collapse(field.Type.ToString()), CompletionItemKind.Field, field.Span);
                    break;
                case ClassVarSectionSyntax classVars:
                    foreach (var field in classVars.Fields)
                    {
                        AddNames(into, field.Identifiers, Collapse(field.Type.ToString()), CompletionItemKind.Field, field.Span);
                    }

                    break;
                case MethodDeclarationSyntax method when !method.Heading.Name.IsMissing:
                    into.Add(Leaf(method.Heading.Name.ToString(), Collapse(method.Heading.ToString()), CompletionItemKind.Method, method.Span, method.Heading.Name.Span));
                    break;
                case PropertyDeclarationSyntax property when !property.Identifier.IsMissing:
                    into.Add(Leaf(property.Identifier.Text, property.Type is null ? "" : Collapse(property.Type.ToString()), CompletionItemKind.Property,
                                  property.Span, property.Identifier.Span));
                    break;
                case TypeSectionSyntax or ConstSectionSyntax:
                    AddDeclarations(into, [member]);
                    break;
            }
        }
    }

    private static void AddNames(ImmutableArray<DocumentSymbol>.Builder into, SeparatedSyntaxList<IdentifierNameSyntax> names, string detail,
                                 CompletionItemKind kind, TextSpan span)
    {
        foreach (var name in names.Where(n => !n.Identifier.IsMissing))
        {
            into.Add(Leaf(name.Identifier.Text, detail, kind, span, name.Identifier.Span));
        }
    }

    private static DocumentSymbol Leaf(string name, string detail, CompletionItemKind kind, TextSpan span, TextSpan selection) =>
        new(name, detail, kind, span, selection, []);

    /// <summary>The text with every run of whitespace (line breaks included) replaced by one space.</summary>
    internal static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
