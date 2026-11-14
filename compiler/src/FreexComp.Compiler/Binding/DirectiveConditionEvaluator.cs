using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// The binder side of LANG-862: a <c>{$IF}</c> condition is a Boolean constant expression in the scope at the
/// directive. That scope is rebuilt from the tokens before the directive: they are parsed as a (truncated)
/// compilation unit and its unit-level declarations bound, so constants, types, variables and routines declared
/// earlier in the unit are visible (declarations local to an enclosing routine are not). <c>CompilerVersion</c>
/// (1.0, Double) is visible unless the unit declares that name. Diagnostics of the truncated unit are dropped.
/// With a bound <c>System</c> (C-046) its interface scope sits between the built-ins and the truncated unit, as in the
/// real binding (LANG-310, 511); without one (unit <c>System</c> itself, syntax-only tests) the built-ins are the parent.
/// </summary>
internal sealed class DirectiveConditionEvaluator : IConditionEvaluator
{
    private readonly BindingResult? _system;

    private DirectiveConditionEvaluator(BindingResult? system) => _system = system;

    public static DirectiveConditionEvaluator Instance { get; } = new(null);

    /// <summary>Parse options whose <c>{$IF}</c> conditions are evaluated by the binder.</summary>
    public static ParseOptions WithBinder(ParseOptions options) => options with { ConditionEvaluator = Instance };

    /// <summary>As <see cref="WithBinder(ParseOptions)"/>, in the scope chain of the bound <paramref name="system"/> (null = built-ins only).</summary>
    public static ParseOptions WithBinder(ParseOptions options, BindingResult? system) =>
        options with { ConditionEvaluator = system is null ? Instance : new DirectiveConditionEvaluator(system) };

    public bool Evaluate(ConditionRequest request)
    {
        var types = _system?.Types ?? new BuiltInTypes();
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
        var uses = _system?.InterfaceScope;
        if (tokens.Count == 0)
        {
            return uses ?? types.Scope;
        }

        var main = tokens[0].File!;
        var last = tokens[^1];
        var end = SyntaxToken.Missing(SyntaxKind.EndOfFileToken, last.File == main ? last.Span.End : main.Text.Length, file: main);
        var root = new Parser(main, [.. tokens, end]).ParseCompilationUnit();
        var tree = new SyntaxTree(main, root, [], []);
        return DeclarationBinder.BindCompilationUnit(tree, types, new DiagnosticBag(), uses).Scope;
    }
}
