using System.Collections.Immutable;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

// CMP-380 (C-120): variables captured by anonymous methods live in the owner's closure object.
internal sealed partial class LoweringContext
{
    /// <summary>Set while the prologue copies parameters into the closure: their own storage is meant.</summary>
    private bool _rawClosureAccess;

    /// <summary>The closure field holding <paramref name="symbol"/>, reached from this body; null when it lives elsewhere.</summary>
    private LExpression? ClosureAccess(Symbol symbol)
    {
        if (_rawClosureAccess || symbol.ContainingSymbol is not RoutineSymbol { Closure: { } closure } owner || !closure.Fields.TryGetValue(symbol, out var field))
        {
            return null;
        }

        return FieldOf(ClosureOf(owner), field);
    }

    private LExpression FieldOf(LExpression instance, FieldSymbol field) =>
        new LDeref(new LPointerOffset(instance, Constant(field.Offset, Types.Int64), Types.Byte, Types.Pointer), field.Type);

    /// <summary>
    /// The closure object of <paramref name="owner"/> seen from this body: its hidden local when this body (or a routine nested in
    /// it without an anonymous method between) is the owner, else the <c>Self</c> of the nearest anonymous method and then the
    /// <c>$outer</c> chain of the closures in between.
    /// </summary>
    private LExpression ClosureOf(RoutineSymbol owner)
    {
        LExpression current;
        RoutineSymbol? at;
        for (var routine = Routine; ; routine = ClosureInfo.LexicalParent(routine))
        {
            if (routine is null)
            {
                throw new InvalidOperationException($"'{owner.Name}' does not enclose '{Routine?.Name}'.");
            }

            if (ReferenceEquals(routine, owner))
            {
                return Reach(owner.Closure!.Local);
            }

            if (routine is MethodSymbol { IsAnonymous: true, SelfParameter: { } self } anonymous)
            {
                current = Reach(self);
                at = anonymous.AnonymousParent;
                break;
            }
        }

        while (!ReferenceEquals(at, owner))
        {
            var closure = at?.Closure ?? throw new InvalidOperationException($"No closure chain to '{owner.Name}'.");
            current = FieldOf(current, closure.Outer!);
            at = ((MethodSymbol)at).AnonymousParent;
        }

        return current;
    }

    /// <summary>A hidden local or parameter of this body, or of an enclosing one (through the frames, CMP-345).</summary>
    private LExpression Reach(Symbol symbol) => symbol switch
    {
        VariableSymbol variable when _variables.TryGetValue(variable, out var local) => new LLocalRef(local),
        VariableSymbol variable when BodyGlobals?.Exists(g => ReferenceEquals(g.Variable, variable)) == true => new LGlobalRef(variable),
        ParameterSymbol parameter when ReferenceEquals(parameter.ContainingSymbol, Routine) => new LParamRef(parameter),
        VariableSymbol variable => Outer(variable, variable.Type),
        ParameterSymbol parameter => Outer(parameter, parameter.Type),
        _ => throw new InvalidOperationException($"Cannot reach '{symbol.Name}'."),
    };

    /// <summary>
    /// <c>$closure := TClosure.Create</c>, <c>$closureRef := $closure</c> (counted, released at exit); for the closure of an anonymous
    /// method <c>$outer</c>/<c>$outerRef</c> := its <c>Self</c>; then the captured parameters (and <c>Self</c>) are copied in.
    /// </summary>
    private ImmutableArray<LStatement> ClosurePrologue(ClosureInfo closure)
    {
        var syntax = closure.Syntax;
        var local = Routine is null ? HandlerVariable(closure.Local) : new LLocalRef(DeclareLocal(closure.Local));
        var keep = Routine is null ? HandlerVariable(closure.Keep) : new LLocalRef(DeclareLocal(closure.Keep));
        var create = closure.Class.SelfAndAncestors().SelectMany(c => c.Methods)
            .First(m => m is { MethodKind: MethodKind.Constructor, Parameters.IsEmpty: true } && IdentifierComparer.Instance.Equals(m.Name, "Create"));
        var construct = new BoundCallExpression(syntax, create, [], type: closure.Class)
        {
            Receiver = new BoundReceiver(new BoundClassReferenceExpression(syntax, closure.Class, closure.Class.Reference), IsVirtual: false, Flag: 1),
        };
        var root = (InterfaceTypeSymbol)Types.IInterface!;
        BoundExpression AsInterface(BoundExpression instance) =>
            new BoundConversionExpression(syntax, instance, root, ConversionKind.ImplicitReference, isExplicit: false, constantValue: null);

        var statements = new List<LStatement>
        {
            new LAssign(local, Lower(construct)),
            ManagedRewriter.Assign(this, keep, AsInterface(new BoundVariableExpression(syntax, closure.Local))),
        };
        if (closure.Outer is { } outer && Routine is MethodSymbol { SelfParameter: { } self })
        {
            statements.Add(new LAssign(FieldOf(local, outer), new LParamRef(self)));
            statements.Add(ManagedRewriter.Assign(this, FieldOf(local, closure.OuterRef!), AsInterface(new BoundParameterExpression(syntax, self))));
        }

        _rawClosureAccess = true;
        try
        {
            foreach (var (symbol, field) in closure.Fields)
            {
                if (symbol is ParameterSymbol parameter)
                {
                    var value = new BoundParameterExpression(syntax, parameter);
                    statements.Add(parameter.Type.IsManaged ? ManagedRewriter.Assign(this, FieldOf(local, field), value)
                        : new LAssign(FieldOf(local, field), Lower(value)));
                }
            }
        }
        finally
        {
            _rawClosureAccess = false;
        }

        return [.. statements];
    }
}
