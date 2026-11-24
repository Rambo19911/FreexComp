using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;
using FreexComp.Compiler.Units;

namespace FreexComp.LanguageService;

/// <summary>A fix offered for diagnostics in a range: an id (<c>declare-variable</c>, …), a title and its edits.</summary>
public sealed record CodeAction(string Id, string Title, ImmutableArray<DocumentEdit> Edits);

/// <summary>Text changes for one file (against its current text; they never overlap).</summary>
public sealed record DocumentEdit(string Path, ImmutableArray<TextChange> Changes);

/// <summary>
/// language-service.md §11, the initial set: <c>declare-variable</c> (E2003 in a routine body, assignment target or
/// <c>for</c> control variable, with the type of the assigned value / start value), <c>add-uses</c> (E2003 when exactly one
/// unit on the search path exports the name), <c>complete-class</c> (E2605: every missing method implementation of the
/// class) and <c>implement-interface</c> (E2651: declarations in the class's <c>public</c> section plus implementations).
/// Edits are made with <see cref="SyntaxEdits"/> on the semantic model's tree (C-142).
/// </summary>
public static class CodeActionService
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    public static ImmutableArray<CodeAction> GetCodeActions(Document doc, TextSpan range, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var model = doc.SemanticModel;
        var diagnostics = model.GetDiagnostics(ct: ct)
            .Where(d => d.Location.HasPosition && PathUtil.AreEqual(d.Location.Path, doc.Path))
            .ToImmutableArray();
        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var unitExports = new Lazy<IReadOnlyList<(string Unit, HashSet<string> Names)>>(() => SearchPathUnits(doc, ct));
        foreach (var diagnostic in diagnostics.Where(d => Intersects(d.Location.Span, range)))
        {
            ct.ThrowIfCancellationRequested();
            var action = diagnostic.Code switch
            {
                "E2003" => [DeclareVariable(model, diagnostic), AddUses(model, diagnostic, unitExports)],
                "E2605" => [CompleteClass(model, diagnostic, diagnostics)],
                "E2651" => [ImplementInterface(model, diagnostic, ct)],
                _ => Array.Empty<CodeAction?>(),
            };
            foreach (var candidate in action.OfType<CodeAction>())
            {
                if (!actions.Any(a => a.Id == candidate.Id && a.Title == candidate.Title))
                {
                    actions.Add(candidate);
                }
            }
        }

        return actions.ToImmutable();
    }

    private static bool Intersects(TextSpan a, TextSpan b) => a.Start <= b.End && b.Start <= a.End;

    private static CodeAction Action(string id, string title, SemanticModel model, params TextChange[] changes) =>
        new(id, title, [new DocumentEdit(model.SyntaxTree.Text.Path, [.. changes.Where(c => !c.IsEmpty).OrderBy(c => c.Span.Start)])]);

    // ------------------------------------------------------------------ declare-variable

    private static CodeAction? DeclareVariable(SemanticModel model, Diagnostic diagnostic)
    {
        var tree = model.SyntaxTree;
        var token = tree.FindToken(diagnostic.Location.Span.Start);
        if (token.Parent is not IdentifierNameSyntax name)
        {
            return null;
        }

        var routine = name.FirstAncestorOrSelf<RoutineDeclarationSyntax>();
        if (routine?.Block is not { } block || !block.Body.Span.Contains(name.Span.Start))
        {
            return null;
        }

        var value = name.Parent switch
        {
            AssignmentStatementSyntax assignment when assignment.Target == name => assignment.Value,
            ForStatementSyntax loop when loop.Variable == name && loop.VarKeyword is null => loop.Start,
            _ => null,
        };
        var type = value is null ? null : model.GetTypeInfo(value).Type;
        if (type is null || type.TypeKind == TypeKind.Error || !IsTypeName(type.Name))
        {
            return null;
        }

        var text = tree.Text;
        var lineEnding = SyntaxEdits.DetectLineEnding(text.Text);
        var declaration = name.Identifier.Text + ": " + type.Name + ";";
        TextChange change;
        if (block.Declarations.OfType<VarSectionSyntax>().LastOrDefault() is { } section && section.Declarations.Count > 0)
        {
            var last = section.Declarations[^1];
            var line = text.Lines.GetLineSpan(text.Lines.GetLinePosition(last.Span.End).Line);
            change = new TextChange(new TextSpan(line.End, 0), lineEnding + SyntaxEdits.IndentOf(text, last.Span.Start) + declaration);
        }
        else
        {
            var begin = block.Body.FirstToken().Span.Start;
            var lineStart = text.Lines.GetLineSpan(text.Lines.GetLinePosition(begin).Line).Start;
            var indent = SyntaxEdits.IndentOf(text, routine.Span.Start);
            change = SyntaxEdits.IsBlank(text.Text, lineStart, begin)
                ? new TextChange(new TextSpan(lineStart, 0), indent + "var" + lineEnding + indent + "  " + declaration + lineEnding)
                : new TextChange(new TextSpan(begin, 0), "var " + declaration + " ");
        }

        return Action("declare-variable", $"Declare variable '{name.Identifier.Text}: {type.Name}'", model, change);
    }

    private static bool IsTypeName(string name) =>
        name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c is '_' or '.');

    // ------------------------------------------------------------------ add-uses

    private static CodeAction? AddUses(SemanticModel model, Diagnostic diagnostic, Lazy<IReadOnlyList<(string Unit, HashSet<string> Names)>> exports)
    {
        var tree = model.SyntaxTree;
        var token = tree.FindToken(diagnostic.Location.Span.Start);
        if (token.Kind != SyntaxKind.IdentifierToken)
        {
            return null;
        }

        var identifier = token.Text.TrimStart('&');
        var units = exports.Value.Where(u => u.Names.Contains(identifier)).Select(u => u.Unit).Distinct(Names).ToList();
        if (units.Count != 1)
        {
            return null;
        }

        var root = tree.Root;
        var inInterface = root is UnitSyntax unit && token.Span.Start < unit.ImplementationSection.ImplementationKeyword.Span.Start;
        var change = SyntaxEdits.AddUsesEntry(root, units[0], inInterface || root is ProgramSyntax,
            new EditOptions(LineEnding: SyntaxEdits.DetectLineEnding(tree.Text.Text)));
        return change.IsEmpty ? null : Action("add-uses", $"Add '{units[0]}' to the uses clause", model, change);
    }

    /// <summary>The units in the compilation's search directories (LANG-510 steps 3–5) with the names their interfaces declare.</summary>
    private static List<(string Unit, HashSet<string> Names)> SearchPathUnits(Document doc, CancellationToken ct)
    {
        var compilation = doc.Compilation;
        var options = compilation.Options;
        var resolver = new UnitResolver(compilation.FileSystem, options.MainFile, options.SearchPaths, options.RtlDirectory, [], target: options.Target);
        var result = new List<(string, HashSet<string>)>();
        var seen = new HashSet<string>(Names);
        foreach (var directory in resolver.Directories)
        {
            IEnumerable<string> files;
            try
            {
                files = compilation.FileSystem.EnumerateFiles(directory, "*.pas").Order(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (PathUtil.AreEqual(file, doc.Path) || !compilation.TryGetSyntaxTree(file, out var tree)
                    || tree.Root is not UnitSyntax unit)
                {
                    continue;
                }

                var unitName = SyntaxEdits.DottedName(unit.Name);
                if (Names.Equals(unitName, "System") || !seen.Add(unitName))
                {
                    continue;
                }

                result.Add((unitName, InterfaceNames(unit)));
            }
        }

        return result;
    }

    private static HashSet<string> InterfaceNames(UnitSyntax unit)
    {
        var names = new HashSet<string>(Names);
        foreach (var declaration in unit.InterfaceSection.Declarations)
        {
            switch (declaration)
            {
                case TypeSectionSyntax types:
                    foreach (var type in types.Declarations)
                    {
                        names.Add(type.Identifier.Text);
                        if (type.Type is EnumTypeSyntax enumeration)
                        {
                            names.UnionWith(enumeration.DescendantNodes().OfType<EnumMemberSyntax>().Select(m => m.Identifier.Text));
                        }
                    }

                    break;
                case ConstSectionSyntax constants:
                    names.UnionWith(constants.Declarations.Select(c => c.Identifier.Text));
                    break;
                case VarSectionSyntax variables:
                    names.UnionWith(variables.Declarations.SelectMany(v => v.Identifiers).Select(i => i.Identifier.Text));
                    break;
                case RoutineDeclarationSyntax { Heading.Name: SimpleNameSyntax routine }:
                    names.Add(routine.Identifier.Text);
                    break;
            }
        }

        names.RemoveWhere(n => n.Length == 0);
        return [.. names.Select(n => n.TrimStart('&'))];
    }

    // ------------------------------------------------------------------ complete-class

    private static CodeAction? CompleteClass(SemanticModel model, Diagnostic diagnostic, ImmutableArray<Diagnostic> all)
    {
        if (model.SyntaxTree.Root is not { } root
            || model.SyntaxTree.FindToken(diagnostic.Location.Span.Start).Parent?.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } type)
        {
            return null;
        }

        var missing = all.Where(d => d.Code == "E2605" && type.Span.Contains(d.Location.Span.Start))
            .Select(d => model.SyntaxTree.FindToken(d.Location.Span.Start).Parent?.FirstAncestorOrSelf<MethodDeclarationSyntax>())
            .OfType<MethodDeclarationSyntax>()
            .Distinct()
            .OrderBy(m => m.Span.Start)
            .ToList();
        if (missing.Count == 0)
        {
            return null;
        }

        var className = type.Identifier.Text;
        var lineEnding = SyntaxEdits.DetectLineEnding(model.SyntaxTree.Text.Text);
        var bodies = missing.Select(m => Implementation(m.Heading, className, lineEnding));
        var change = SyntaxEdits.InsertMethodImplementation(root, className, string.Join(lineEnding + lineEnding, bodies));
        return Action("complete-class", $"Complete class '{className}'", model, change);
    }

    /// <summary><c>&lt;heading with TClass.Name&gt;; begin end;</c> on three lines.</summary>
    private static string Implementation(RoutineHeadingSyntax heading, string className, string lineEnding)
    {
        var text = heading.SyntaxTree.Text.Text;
        var name = heading.Name.Span;
        var qualified = text[heading.Span.Start..name.Start] + className + "." + text[name.Start..heading.Span.End];
        return qualified + ";" + lineEnding + "begin" + lineEnding + "end;";
    }

    // ------------------------------------------------------------------ implement-interface

    private static CodeAction? ImplementInterface(SemanticModel model, Diagnostic diagnostic, CancellationToken ct)
    {
        var token = model.SyntaxTree.FindToken(diagnostic.Location.Span.Start);
        var root = model.SyntaxTree.Root;
        if (token.Parent?.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } type
            || model.GetDeclaredSymbol(type, ct) is not ClassTypeSymbol cls)
        {
            return null;
        }

        var implemented = cls.Interfaces.FirstOrDefault(i => Names.Equals(i.Interface.Name, token.Text));
        if (implemented is null)
        {
            return null;
        }

        var all = implemented.Interface.AllMethods;
        var missing = new List<MethodSymbol>();
        for (var i = 0; i < all.Length && i < implemented.Implementations.Length; i++)
        {
            if (ReferenceEquals(implemented.Implementations[i], all[i]))
            {
                missing.Add(all[i]);
            }
        }

        if (missing.Count == 0 || missing.Any(m => m.MethodDeclaration is null))
        {
            return null;
        }

        var lineEnding = SyntaxEdits.DetectLineEnding(model.SyntaxTree.Text.Text);
        var declarations = missing.Select(m => m.MethodDeclaration.Heading.ToString() + ";").ToList();
        var className = type.Identifier.Text;
        var bodies = missing.Select(m => Implementation(m.MethodDeclaration.Heading, className, lineEnding));
        return Action("implement-interface", $"Implement interface '{implemented.Interface.Name}'", model,
            SyntaxEdits.InsertMembers(type, declarations, Visibility.Public),
            SyntaxEdits.InsertMethodImplementation(root, className, string.Join(lineEnding + lineEnding, bodies)));
    }
}
