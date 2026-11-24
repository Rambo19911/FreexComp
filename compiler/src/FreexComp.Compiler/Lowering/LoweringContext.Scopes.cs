using System.Collections.Immutable;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;

namespace FreexComp.Compiler.Lowering;

// CMP-1180…1182 (C-306): managed inline variables are finalised when their declaration scope is left (LANG-560…565).
internal sealed partial class LoweringContext
{
    /// <summary>
    /// The next inline declaration of the body's outermost statement list: its variables keep the routine epilogue (LANG-565).
    /// A finally part has none: its list is a scope of the enclosing routine, where the variables live.
    /// </summary>
    private BoundLocalsStatement? _outermostTail;

    /// <summary>Declarations whose managed variables an enclosing region of the same statement list already finalises (CMP-1181).</summary>
    private readonly HashSet<BoundLocalsStatement> _sharedRegion = new(ReferenceEqualityComparer.Instance);

    private readonly HashSet<VariableSymbol> _scopeFinalised = new(ReferenceEqualityComparer.Instance);

    /// <summary>The inner one-pass loop <c>Continue</c> leaves instead of jumping to the step of the loop (a <c>repeat</c> with a region).</summary>
    private readonly Dictionary<LLoopLabel, LLoopLabel> _continueTargets = [];

    /// <summary>Whether <paramref name="variable"/> is finalised by its declaration scope, so not by the body's cleanup scope (CMP-1180).</summary>
    public bool IsScopeFinalised(VariableSymbol? variable) => variable is not null && _scopeFinalised.Contains(variable);

    /// <summary>Whether <paramref name="target"/> is the storage of a variable its declaration scope finalises.</summary>
    private bool IsScopeFinalised(LExpression target) => target switch
    {
        LLocalRef local => IsScopeFinalised(local.Local.Variable),
        LGlobalRef global => IsScopeFinalised(global.Variable),
        LOuterRef outer => IsScopeFinalised(outer.Variable as VariableSymbol),
        _ => false,
    };

    /// <summary>The inline declaration that ends the statement list <paramref name="list"/> (its scope is the rest of that list), or null.</summary>
    private static BoundLocalsStatement? ListTail(BoundStatement? list) =>
        list is BoundBlock { Statements: [.., BoundLocalsStatement { ScopeKind: LocalsScopeKind.List } tail] } ? tail : null;

    /// <summary><paramref name="first"/> and the later inline declarations of the same statement list, in source order.</summary>
    private static List<BoundLocalsStatement> ListChain(BoundLocalsStatement? first)
    {
        var chain = new List<BoundLocalsStatement>();
        for (var declaration = first; declaration is not null; declaration = ListTail(declaration.Body))
        {
            chain.Add(declaration);
        }

        return chain;
    }

    /// <summary>
    /// A managed variable its scope finalises. One captured by an anonymous method is not: it lives in the closure (a unit-level
    /// body's in its global) for as long as the closure may use it (LANG-566).
    /// </summary>
    private static bool FinalisedAtScopeExit(VariableSymbol variable) =>
        variable.Type.IsManaged && !variable.IsCapturedBodyLocal
        && !(variable.ContainingSymbol is RoutineSymbol { Closure: { } closure } && closure.Fields.ContainsKey(variable));

    private void DeclareScopeLocals(BoundLocalsStatement locals, bool finalised)
    {
        foreach (var local in locals.Locals)
        {
            if (finalised && FinalisedAtScopeExit(local))
            {
                _scopeFinalised.Add(local);
            }

            HandlerVariable(local);                                     // C-111: declared before the body uses them
        }
    }

    /// <summary>The finally part of a region: the variables are cleared in reverse declaration order (LANG-563); empty storage stays empty.</summary>
    private LBlock ScopeCleanup(BoundLocalsStatement at, IEnumerable<VariableSymbol> variables) =>
        new([.. variables.Reverse().Select(v => ManagedRewriter.Clear(this, Lower(new BoundVariableExpression(at.Syntax, v))))]);

    /// <summary>
    /// CMP-1180: a declaration scope with managed variables becomes <c>try body finally clear them end</c>, so every way out of the
    /// scope runs the finalisation; the consecutive declarations of one statement list share the region that starts at the first
    /// managed one (CMP-1181); the outermost list of the body has none (LANG-565); hidden statement temporaries keep routine lifetime.
    /// </summary>
    private LStatement LocalsScope(BoundLocalsStatement locals)
    {
        if (locals.ScopeKind == LocalsScopeKind.Hidden)
        {
            DeclareScopeLocals(locals, finalised: false);
            return Lower(locals.Body);
        }

        if (ReferenceEquals(locals, _outermostTail))
        {
            _outermostTail = ListTail(locals.Body);
            DeclareScopeLocals(locals, finalised: false);
            return Lower(locals.Body);
        }

        if (_sharedRegion.Remove(locals) || !locals.Locals.Any(FinalisedAtScopeExit))
        {
            DeclareScopeLocals(locals, finalised: true);
            return Lower(locals.Body);
        }

        var chain = locals.ScopeKind == LocalsScopeKind.List ? ListChain(locals) : [locals];
        _sharedRegion.UnionWith(chain.Skip(1));
        DeclareScopeLocals(locals, finalised: true);
        var body = Lower(locals.Body);
        return new LTryFinally(body, ScopeCleanup(locals, chain.SelectMany(l => l.Locals).Where(FinalisedAtScopeExit)));
    }

    /// <summary>
    /// LANG-560: the <c>until</c> condition belongs to the scope of the list's declarations, so one region spans the statements and
    /// the condition of an iteration: <c>loop { try { once { body }; done := condition } finally clear end } step: if done break</c>
    /// (<c>Continue</c> leaves the one-pass loop). Null when the list declares nothing its scope finalises.
    /// </summary>
    private LStatement? RepeatWithRegion(BoundRepeatStatement loop)
    {
        var chain = ListChain(ListTail(loop.Body));
        var finalised = chain.SelectMany(l => l.Locals).Where(FinalisedAtScopeExit).ToImmutableArray();
        if (finalised.IsEmpty)
        {
            return null;
        }

        _sharedRegion.UnionWith(chain);
        var label = NewLoop("repeat");
        var iteration = NewLoop("repeat.scope");
        _continueTargets.Add(label, iteration);
        var body = LowerLoopBody(label, loop.Body);
        var done = new LLocalRef(NewTemp(Types.Boolean));
        var once = new LLoop(iteration, new LBlock([body, new LBreak(iteration)]), step: null);
        var region = new LTryFinally(new LBlock([once, new LAssign(done, Lower(loop.Condition))]), ScopeCleanup(chain[0], finalised));
        return new LLoop(label, region, new LIf(done, new LBreak(label), @else: null));
    }
}
