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
        if (_rawClosureAccess || ClosureInfo.FrameOf(symbol) is not { } frame)
        {
            return null;
        }

        return FieldOf(FrameInstance(frame), frame.Fields[symbol]);
    }

    private LExpression FieldOf(LExpression instance, FieldSymbol field) =>
        new LDeref(new LPointerOffset(instance, Constant(field.Offset, Types.Int64), Types.Byte, Types.Pointer), field.Type);

    /// <summary>
    /// The instance of the frame <paramref name="target"/> (the closure of a body, or a block frame of it — CMP-1183, C-308) seen
    /// from this body: its hidden local when this body (or a routine nested in it without an anonymous method between) owns it,
    /// else the <c>Self</c> of the nearest anonymous method and then the <c>$outer</c> chain of the frames in between.
    /// </summary>
    private LExpression FrameInstance(ClosureInfo target)
    {
        LExpression current;
        ClosureInfo? at;
        for (var routine = Routine; ; routine = ClosureInfo.LexicalParent(routine))
        {
            if (ReferenceEquals(routine, target.Owner))
            {
                return Reach(target.Local);
            }

            if (routine is null)
            {
                throw new InvalidOperationException($"'{target.Class.Name}' does not enclose '{Routine?.Name}'.");
            }

            if (routine is MethodSymbol { IsAnonymous: true, SelfParameter: { } self } anonymous)
            {
                current = Reach(self);
                at = anonymous.AnonymousFrame;
                break;
            }
        }

        while (!ReferenceEquals(at, target))
        {
            if (at is null)
            {
                throw new InvalidOperationException($"No closure chain to '{target.Class.Name}'.");
            }

            if (at.Links.TryGetValue(target, out var link))
            {
                return FieldOf(current, link.Plain);                            // C-230: a nested routine's closure links to the target
            }

            current = FieldOf(current, at.Outer ?? throw new InvalidOperationException($"No closure chain to '{target.Class.Name}'."));
            at = at.Enclosing;
        }

        return current;
    }

    /// <summary>C-230: <c>Result := closure.Result</c> into the routine's own result storage (a hidden result: the caller's).</summary>
    private LStatement CopyCapturedResult(VariableSymbol result, SyntaxNode syntax)
    {
        LExpression target;
        _rawClosureAccess = true;
        try
        {
            target = Lower(new BoundVariableExpression(syntax, result));
        }
        finally
        {
            _rawClosureAccess = false;
        }

        var value = new BoundVariableExpression(syntax, result);
        return result.Type.IsManaged ? ManagedRewriter.Assign(this, target, value) : new LAssign(target, Lower(value));
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

    /// <summary>The parameterless <c>Create</c> a frame class inherits, called on the class: a new zeroed instance.</summary>
    private BoundCallExpression NewFrame(ClosureInfo frame)
    {
        var create = frame.Class.SelfAndAncestors().SelectMany(c => c.Methods)
            .First(m => m is { MethodKind: MethodKind.Constructor, Parameters.IsEmpty: true } && IdentifierComparer.Instance.Equals(m.Name, "Create"));
        return new BoundCallExpression(frame.Syntax, create, [], type: frame.Class)
        {
            Receiver = new BoundReceiver(new BoundClassReferenceExpression(frame.Syntax, frame.Class, frame.Class.Reference), IsVirtual: false, Flag: 1),
        };
    }

    /// <summary>
    /// CMP-1183 (C-308): the first captured declaration of a statement list makes the list's block frame, at each execution:
    /// <c>$block := TBlock.Create</c>, <c>$blockRef := $block</c> (counted: the previous execution's instance survives only while
    /// closures hold it), <c>$outer</c>/<c>$outerRef</c> := the frame the list runs on. The captured variables are its fields,
    /// empty in a new instance (LANG-562, LANG-566).
    /// </summary>
    private ImmutableArray<LStatement> BlockPrologue(ClosureInfo frame) => BlockCreation(frame, HandlerVariable(frame.Local), HandlerVariable(frame.Keep));

    /// <summary>The statements of <see cref="BlockPrologue"/> on the declared storage <paramref name="local"/>/<paramref name="keep"/>.</summary>
    private ImmutableArray<LStatement> BlockCreation(ClosureInfo frame, LExpression local, LExpression keep)
    {
        var syntax = frame.Syntax;
        var root = (InterfaceTypeSymbol)Types.IInterface!;
        var enclosing = frame.Enclosing!;
        return
        [
            new LAssign(local, Lower(NewFrame(frame))),
            ManagedRewriter.Assign(this, keep, new BoundConversionExpression(syntax, new BoundVariableExpression(syntax, frame.Local), root,
                ConversionKind.ImplicitReference, isExplicit: false, constantValue: null)),
            new LAssign(FieldOf(local, frame.Outer!), FrameInstance(enclosing)),
            ManagedRewriter.Assign(this, FieldOf(local, frame.OuterRef!),
                ObjectToInterface(FrameInstance(enclosing), enclosing.Class.FindInterface(root)!.Offset, root)),
        ];
    }

    /// <summary>
    /// <c>$closure := TClosure.Create</c>, <c>$closureRef := $closure</c> (counted, released at exit); for the closure of an anonymous
    /// method <c>$outer</c>/<c>$outerRef</c> := its <c>Self</c>; then the captured parameters (and <c>Self</c>) are copied in.
    /// </summary>
    private ImmutableArray<LStatement> ClosurePrologue(ClosureInfo closure)
    {
        var syntax = closure.Syntax;
        var local = Routine is null ? HandlerVariable(closure.Local) : new LLocalRef(DeclareLocal(closure.Local));
        var keep = Routine is null ? HandlerVariable(closure.Keep) : new LLocalRef(DeclareLocal(closure.Keep));
        var construct = NewFrame(closure);
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

        foreach (var (target, (plain, counted)) in closure.Links)
        {
            // C-230: the enclosing routine's closure (C-308: or a block frame of it), kept alive by this one
            statements.Add(new LAssign(FieldOf(local, plain), FrameInstance(target)));
            statements.Add(ManagedRewriter.Assign(this, FieldOf(local, counted),
                ObjectToInterface(FrameInstance(target), target.Class.FindInterface(root)!.Offset, root)));
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
