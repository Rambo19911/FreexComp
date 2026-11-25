using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.LanguageService;

public enum ReferenceKind { Declaration, Read, Write, Call, TypeReference, Implementation }

public readonly record struct ReferenceLocation(Location Location, ReferenceKind Kind);

public readonly record struct DocumentHighlight(TextSpan Span, ReferenceKind Kind);

public sealed record WorkspaceSymbol(string Name, string ContainerName, CompletionItemKind Kind, Location Location);

/// <summary>Find references, document highlights, workspace symbols (CMP-742, 744, C-138).</summary>
public static partial class NavigationService
{
    private static readonly SyntaxKind[] PairKeywords =
    [
        SyntaxKind.BeginKeyword, SyntaxKind.EndKeyword, SyntaxKind.TryKeyword, SyntaxKind.CaseKeyword, SyntaxKind.RepeatKeyword,
        SyntaxKind.UntilKeyword, SyntaxKind.RecordKeyword, SyntaxKind.ClassKeyword,
    ];

    /// <summary>
    /// CMP-742: every identifier of the compilation's source units (<c>.pcu</c>-only units skipped) that binds to the symbol under the
    /// caret — for a method its whole override chain, for a property its redeclarations. Symbols are matched by <see cref="SymbolKey"/>
    /// so that units taken over from an earlier snapshot match (C-242). Declarations first, then by path and position.
    /// </summary>
    public static ImmutableArray<ReferenceLocation> FindReferences(Document doc, int position, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (QuickInfoService.SymbolAt(doc.SemanticModel, position) is not ({ } symbol, _))
        {
            return [];
        }

        var related = Related(doc.Compilation, symbol);
        var references = new List<ReferenceLocation>();
        foreach (var path in doc.Compilation.SourceUnitPaths)
        {
            ct.ThrowIfCancellationRequested();
            var model = PathUtil.AreEqual(path, doc.Path) ? doc.SemanticModel : doc.Compilation.GetSemanticModel(path);
            foreach (var (token, kind) in Occurrences(model, related, ct))
            {
                references.Add(new ReferenceLocation(LocationOf(model.SyntaxTree, token.Span), kind));
            }
        }

        return [.. references.OrderBy(r => r.Kind == ReferenceKind.Declaration ? 0 : 1).ThenBy(r => r.Location.Path, PathUtil.Comparer)
            .ThenBy(r => r.Location.Span.Start)];
    }

    /// <summary>CMP-744: the symbol's occurrences in this document; on a block keyword, the keyword pair.</summary>
    public static ImmutableArray<DocumentHighlight> GetDocumentHighlights(Document doc, int position, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var model = doc.SemanticModel;
        var token = model.SyntaxTree.FindToken(Math.Clamp(position, 0, model.SyntaxTree.Text.Text.Length));
        if (!token.Span.Contains(position) && position > 0)
        {
            token = model.SyntaxTree.FindToken(position - 1);
        }

        if (PairKeywords.Contains(token.Kind) && IndentationService.GetMatchingBracket(model.SyntaxTree, token.Span.Start) is { } partner)
        {
            return [.. new[] { token.Span, partner }.OrderBy(s => s.Start).Select(s => new DocumentHighlight(s, ReferenceKind.Read))];
        }

        if (QuickInfoService.SymbolAt(model, position) is not ({ } symbol, _))
        {
            return [];
        }

        return [.. Occurrences(model, Related(doc.Compilation, symbol), ct).Select(o => new DocumentHighlight(o.Token.Span, o.Kind))];
    }

    /// <summary>
    /// Declared symbols of the workspace's current snapshot whose name contains <paramref name="query"/> (case-insensitive): exact
    /// matches first, then prefixes, then the others; at most <paramref name="limit"/>.
    /// </summary>
    public static ImmutableArray<WorkspaceSymbol> FindSymbols(Workspace ws, string query, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ws);
        ArgumentNullException.ThrowIfNull(query);
        var compilation = ws.CurrentCompilation;
        var found = new List<(int Rank, WorkspaceSymbol Symbol)>();
        var seen = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
        void Visit(Symbol symbol, string container)
        {
            if (!seen.Add(symbol))
            {
                return;
            }

            var index = symbol.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && Sites(compilation, symbol) is { IsEmpty: false } sites)
            {
                var rank = symbol.Name.Length == query.Length ? 0 : index == 0 ? 1 : 2;
                found.Add((rank, new WorkspaceSymbol(symbol.Name, container, SymbolKinds.Of(symbol), sites[0])));
            }

            if (symbol is TypeSymbol type and (ClassTypeSymbol or RecordTypeSymbol or InterfaceTypeSymbol or EnumTypeSymbol))
            {
                foreach (var member in type.Members)
                {
                    Visit(member, container + "." + type.Name);
                }
            }
        }

        foreach (var unit in compilation.UnitSymbols)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var member in unit.InterfaceMembers.Concat(unit.ImplementationMembers))
            {
                Visit(member, unit.Name);
            }
        }

        return [.. found.OrderBy(f => f.Rank).ThenBy(f => f.Symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Symbol.ContainerName, StringComparer.OrdinalIgnoreCase).Take(Math.Max(0, limit)).Select(f => f.Symbol)];
    }

    /// <summary>The symbol and what counts as the same for references: a method's override chain, a property's redeclarations.</summary>
    internal static List<Symbol> Related(Compilation compilation, Symbol symbol)
    {
        var related = new List<Symbol> { symbol };
        switch (symbol)
        {
            case MethodSymbol { ContainingInterface: null } method when method.IsVirtual || method.IsOverride:
                var root = method;
                while (root.OverriddenMethod is { } overridden)
                {
                    root = overridden;
                }

                related.Add(root);
                related.AddRange(AllTypes(compilation).OfType<ClassTypeSymbol>().SelectMany(c => c.Methods).Where(m => Overrides(m, root)));
                break;
            case PropertySymbol { ContainingSymbol: ClassTypeSymbol owner } property:
                related.AddRange(AllTypes(compilation).OfType<ClassTypeSymbol>()
                    .Where(c => c.SelfAndAncestors().Any(a => SymbolKey.AreSame(a, owner)) || owner.SelfAndAncestors().Any(a => SymbolKey.AreSame(a, c)))
                    .SelectMany(c => c.Properties).Where(p => IdentifierComparer.Instance.Equals(p.Name, property.Name)));
                break;
        }

        return related;
    }

    /// <summary>The identifiers of the model's own file that bind to one of <paramref name="related"/>, in text order, with their kind.</summary>
    internal static IEnumerable<(SyntaxToken Token, ReferenceKind Kind)> Occurrences(SemanticModel model, List<Symbol> related, CancellationToken ct)
    {
        var names = related.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var token in model.SyntaxTree.Root.OwnTokens())
        {
            if (!SyntaxFacts.IsNameToken(token) || token.IsMissing || !names.Contains(SyntaxFacts.NameOf(token)))
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();
            if (QuickInfoService.SymbolOf(model, token) is not { } bound || !related.Any(r => SymbolKey.AreSame(r, bound)))
            {
                continue;
            }

            yield return (token, KindOf(model, token, bound));
        }
    }

    private static ReferenceKind KindOf(SemanticModel model, SyntaxToken token, Symbol symbol)
    {
        if (ReferenceEquals(model.GetDeclaredSymbol(token), symbol))
        {
            var site = symbol.Locations.IndexOf(symbol.Locations.FirstOrDefault(l => PathUtil.AreEqual(l.Path, model.SyntaxTree.Text.Path)
                                                                                    && l.Span.Start <= token.Span.Start && token.Span.End <= l.Span.End));
            return symbol is RoutineSymbol && site > 0 ? ReferenceKind.Implementation : ReferenceKind.Declaration;
        }

        // the whole designator the name ends: B.Count is the target of B.Count := …
        SyntaxNode node = token.Parent!;
        while (node.Parent is MemberAccessExpressionSyntax access && ReferenceEquals(access.Name, node)
               || node.Parent is QualifiedNameSyntax qualified && ReferenceEquals(qualified.Right, node))
        {
            node = node.Parent;
        }

        if (node.Parent is AssignmentStatementSyntax assignment && ReferenceEquals(assignment.Target, node)
            || node.Parent is ForStatementSyntax loop && ReferenceEquals(loop.Variable, node))
        {
            return ReferenceKind.Write;
        }

        return symbol switch
        {
            RoutineSymbol or IntrinsicSymbol => ReferenceKind.Call,
            TypeSymbol => ReferenceKind.TypeReference,
            _ => ReferenceKind.Read,
        };
    }

    private static Location LocationOf(SyntaxTree tree, TextSpan span)
    {
        var lines = tree.Text.Lines;
        return new Location(tree.Text.Path, span, new LinePositionSpan(lines.GetLinePosition(span.Start), lines.GetLinePosition(span.End)));
    }
}
