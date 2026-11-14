using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// Builds the uses chains of a compilation unit (LANG-310, 515) for the directive evaluator: quietly, without
/// diagnostics (the real binding reports them); entries that cannot be resolved (yet) are left out.
/// </summary>
internal interface IUsesScopes
{
    BuiltInTypes Types { get; }

    /// <summary>The root of every chain: the built-ins, then the interface of <c>System</c> when there is one.</summary>
    Scope Root { get; }

    /// <summary>The interfaces of the units of <paramref name="clause"/> on top of <paramref name="parent"/>, the last entry innermost.</summary>
    Scope Chain(UsesClauseSyntax? clause, Scope parent);
}

/// <summary>
/// The binder side of LANG-862: a <c>{$IF}</c> condition is a Boolean constant expression in the scope at the
/// directive. That scope is rebuilt from the tokens before the directive: they are parsed as a (truncated)
/// compilation unit and its unit-level declarations bound, so constants, types, variables and routines declared
/// earlier in the unit are visible (declarations local to an enclosing routine are not). <c>CompilerVersion</c>
/// (1.0, Double) is visible unless the unit declares that name. Diagnostics of the truncated unit are dropped.
/// The truncated unit is bound in its uses chain (C-069): built-ins → <c>System</c> interface → the units of the uses
/// clauses written before the directive (implementation-uses only from the implementation section on) → the truncated
/// unit. Without a chain provider (unit <c>System</c> itself, syntax-only tests) the built-ins are the parent.
/// </summary>
internal sealed class DirectiveConditionEvaluator : IConditionEvaluator
{
    private readonly IUsesScopes? _uses;

    private DirectiveConditionEvaluator(IUsesScopes? uses) => _uses = uses;

    public static DirectiveConditionEvaluator Instance { get; } = new(null);

    /// <summary>Parse options whose <c>{$IF}</c> conditions are evaluated by the binder.</summary>
    public static ParseOptions WithBinder(ParseOptions options) => options with { ConditionEvaluator = Instance };

    /// <summary>As <see cref="WithBinder(ParseOptions)"/>, in the scope chain of the bound <paramref name="system"/> (null = built-ins only).</summary>
    public static ParseOptions WithBinder(ParseOptions options, BindingResult? system) =>
        WithBinder(options, system is null ? null : new SystemOnly(system));

    /// <summary>As <see cref="WithBinder(ParseOptions)"/>, in the uses chains <paramref name="uses"/> builds (null = built-ins only).</summary>
    public static ParseOptions WithBinder(ParseOptions options, IUsesScopes? uses) =>
        options with { ConditionEvaluator = uses is null ? Instance : new DirectiveConditionEvaluator(uses) };

    public bool Evaluate(ConditionRequest request)
    {
        var types = _uses?.Types ?? new BuiltInTypes();
        var scope = new Scope(ScopeKind.Block, ScopeAt(request.PrecedingTokens, types));
        if (scope.Lookup("CompilerVersion") is null)
        {
            scope.TryDeclare(new ConstantSymbol("CompilerVersion", types.Double, new ConstantValue(1.0, types.Double), isTyped: false, null, []), out _);
        }

        var diagnostics = new DiagnosticBag();
        var value = new ConstantFolder(types, scope, diagnostics) { IsDefined = request.IsDefined }.Fold(request.Condition);
        foreach (var diagnostic in diagnostics.ToImmutable())
        {
            request.Report(diagnostic);
        }

        switch (value?.Value)
        {
            case bool result:
                return result;
            case null:
                return false;                                                   // already reported
            default:
                request.Report(Diagnostic.Create("E2061", DiagnosticBag.LocationOf(request.Condition), value.Type.Name));
                return false;
        }
    }

    private Scope ScopeAt(IReadOnlyList<SyntaxToken> tokens, BuiltInTypes types)
    {
        var root = _uses?.Root ?? types.Scope;
        if (tokens.Count == 0)
        {
            return root;
        }

        var main = tokens[0].File!;
        var last = tokens[^1];
        var end = SyntaxToken.Missing(SyntaxKind.EndOfFileToken, last.File == main ? last.Span.End : main.Text.Length, file: main);
        var unit = new Parser(main, [.. tokens, end]).ParseCompilationUnit();
        var tree = new SyntaxTree(main, unit, [], []);
        var bag = new DiagnosticBag();
        var (interfaceClause, implementation) = unit switch
        {
            UnitSyntax u => (u.InterfaceSection.UsesClause, u.ImplementationSection is { ImplementationKeyword.IsMissing: false } section ? section : null),
            ProgramSyntax p => (p.UsesClause, null),
            _ => (null, null),
        };
        var interfaceUses = _uses?.Chain(interfaceClause, root) ?? root;
        var interfacePart = DeclarationBinder.BindInterface(tree, types, bag, interfaceUses);
        var implementationUses = implementation is not null && _uses is not null ? _uses.Chain(implementation.UsesClause, interfaceUses) : interfaceUses;
        return DeclarationBinder.BindImplementation(tree, interfacePart, types, bag, implementationUses).Scope;
    }

    /// <summary>The chain of a unit bound against <c>System</c> only (C-046 behaviour; uses entries are not resolved).</summary>
    private sealed class SystemOnly(BindingResult system) : IUsesScopes
    {
        public BuiltInTypes Types => system.Types;

        public Scope Root { get; } = Binder.SystemScope(system.Types, system);

        public Scope Chain(UsesClauseSyntax? clause, Scope parent) => parent;
    }
}
