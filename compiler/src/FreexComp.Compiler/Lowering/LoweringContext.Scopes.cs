using System.Collections.Immutable;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

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
    /// A managed variable its scope finalises. One captured by an anonymous method is not: it lives in a frame for as long as a
    /// closure may use it — an inline variable of a statement list in the list's block frame, which the scope releases instead
    /// (LANG-566, CMP-1183; <see cref="ScopeFinalised"/>), a <c>for var</c> variable in the closure of the body (a unit-level
    /// body's in its global).
    /// </summary>
    private static bool FinalisedAtScopeExit(VariableSymbol variable) =>
        variable.Type.IsManaged && !variable.IsCapturedBodyLocal && ClosureInfo.FrameOf(variable) is null;

    /// <summary>
    /// What leaving the scope of <paramref name="locals"/> finalises, in declaration order: the counted reference to the block
    /// frame the declaration creates (CMP-1183: a managed inline variable of the scope) and its managed variables outside frames.
    /// </summary>
    private static IEnumerable<VariableSymbol> ScopeFinalised(BoundLocalsStatement locals) =>
        (locals.Inline?.Creates is { } frame ? [frame.Keep] : Enumerable.Empty<VariableSymbol>()).Concat(locals.Locals.Where(FinalisedAtScopeExit));

    /// <summary>Declares the storage of the scope's variables; the statements that make its block frame, if it creates one (C-308).</summary>
    private ImmutableArray<LStatement> DeclareScopeLocals(BoundLocalsStatement locals, bool finalised)
    {
        ImmutableArray<LStatement> prologue = [];
        if (locals.Inline?.Creates is { } frame)
        {
            if (finalised)
            {
                _scopeFinalised.Add(frame.Keep);
            }

            if (locals.ScopeKind == LocalsScopeKind.Loop)
            {
                // CMP-1410, 1411 (C-387): a captured for var variable — the frame is made at the start of every iteration; until
                // then the frame slot is nil (CMP-1412: the debugger reads no stale instance of an earlier execution of the loop)
                var instance = HandlerVariable(frame.Local);
                HandlerVariable(frame.Keep);
                _iterationFrames.Add(locals.Locals.Single(), frame);
                prologue = [new LAssign(instance, new LConst(new ConstantValue(null, Types.Nil), frame.Class))];
            }
            else
            {
                prologue = BlockPrologue(frame);
            }
        }

        foreach (var local in locals.Locals)
        {
            if (local.BlockFrame is not null)
            {
                continue;                                               // a field of the block frame
            }

            if (finalised && FinalisedAtScopeExit(local))
            {
                _scopeFinalised.Add(local);
            }

            HandlerVariable(local);                                     // C-111: declared before the body uses them
        }

        return prologue;
    }

    /// <summary>
    /// CMP-1410, 1411 (C-387): the <c>for var</c> variables whose loop scope is being lowered and whose per-iteration block frame
    /// is not made yet — taken by the loop's counted driver (<see cref="ForRewriter"/>) or by the store of a <c>for…in</c> element.
    /// </summary>
    private readonly Dictionary<VariableSymbol, ClosureInfo> _iterationFrames = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// CMP-1410, 1411: when <paramref name="variable"/> is a captured <c>for var</c> variable of the loop being lowered, the
    /// statements that make its iteration's block frame (<see cref="BlockCreation"/>), once; null otherwise.
    /// </summary>
    internal ImmutableArray<LStatement>? TakeIterationFrame(BoundExpression variable)
    {
        if (variable is not BoundVariableExpression { Variable: var symbol } || !_iterationFrames.Remove(symbol, out var frame))
        {
            return null;
        }

        return BlockCreation(frame, Reach(frame.Local), Reach(frame.Keep));
    }

    /// <summary>
    /// CMP-1410: the hidden, uncaptured driver <c>$for&lt;m&gt;</c> of a counted loop over a captured variable (<c>&lt;m&gt;</c>: the
    /// number of the loop's block frame), in the routine frame (a unit-level body: an internal global).
    /// </summary>
    internal BoundVariableExpression ForDriver(BoundExpression variable)
    {
        var number = ((BoundVariableExpression)variable).Variable.BlockFrame!.Local.Name["$block".Length..];
        var driver = new VariableSymbol("$for" + number, variable.Type, isGlobal: false, (Symbol?)Routine ?? Unit!, []);
        HandlerVariable(driver);
        return new BoundVariableExpression(variable.Syntax, driver);
    }

    /// <summary>The scope's body after the statements that make its block frame.</summary>
    private LStatement ScopeBody(ImmutableArray<LStatement> prologue, BoundStatement body) =>
        prologue.IsEmpty ? Lower(body) : new LBlock([.. prologue, Lower(body)]);

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
            return ScopeBody(DeclareScopeLocals(locals, finalised: false), locals.Body);
        }

        if (ReferenceEquals(locals, _outermostTail))
        {
            _outermostTail = ListTail(locals.Body);
            return ScopeBody(DeclareScopeLocals(locals, finalised: false), locals.Body);
        }

        if (_sharedRegion.Remove(locals) || !ScopeFinalised(locals).Any())
        {
            return ScopeBody(DeclareScopeLocals(locals, finalised: true), locals.Body);
        }

        var chain = locals.ScopeKind == LocalsScopeKind.List ? ListChain(locals) : [locals];
        _sharedRegion.UnionWith(chain.Skip(1));
        var prologue = DeclareScopeLocals(locals, finalised: true);
        var region = new LTryFinally(Lower(locals.Body), ScopeCleanup(locals, chain.SelectMany(ScopeFinalised)));
        return prologue.IsEmpty ? region : new LBlock([.. prologue, region]);
    }

    /// <summary>
    /// LANG-560: the <c>until</c> condition belongs to the scope of the list's declarations, so one region spans the statements and
    /// the condition of an iteration: <c>loop { try { once { body }; done := condition } finally clear end } step: if done break</c>
    /// (<c>Continue</c> leaves the one-pass loop). Null when the list declares nothing its scope finalises.
    /// </summary>
    private LStatement? RepeatWithRegion(BoundRepeatStatement loop)
    {
        var chain = ListChain(ListTail(loop.Body));
        var finalised = chain.SelectMany(ScopeFinalised).ToImmutableArray();
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
