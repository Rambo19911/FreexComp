using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Anonymous methods (LANG-720…725, CMP-380; C-120, C-121).
internal sealed partial class ExpressionBinder
{
    /// <summary>The closure of a unit-level body (its owner has no routine symbol to carry it).</summary>
    private ClosureInfo? _unitClosure;

    /// <summary>The innermost inline declaration whose scope is being bound (C-308: where an anonymous method is written).</summary>
    public InlineScope? CurrentInline { get; set; }

    /// <summary>The bodies of the anonymous methods bound here, inner ones included (each after the method containing it).</summary>
    public List<BoundRoutineBody> AnonymousBodies { get; } = [];

    /// <summary>The closure of the body being bound (its routine's, or the unit-level body's), if it has anonymous methods.</summary>
    public ClosureInfo? OwnClosureInfo => _routine?.Closure ?? _unitClosure;

    /// <summary>Hands over (and forgets) the closure of a unit-level body, so the next body bound by this binder gets its own.</summary>
    public ClosureInfo? TakeUnitClosure()
    {
        var closure = _unitClosure;
        _unitClosure = null;
        return closure;
    }

    /// <summary>
    /// CMP-380: once the owner's body is bound, its closure gets a field for each of its variables, parameters (and <c>Self</c>) that
    /// an anonymous method inside it references (at any depth); <c>Result</c> cannot be captured yet (E2900).
    /// </summary>
    public void CompleteClosure()
    {
        if (OwnClosureInfo is not { IsComplete: false } closure)
        {
            return;
        }

        var captured = new List<Symbol>();
        if (_routine is not null)
        {
            // C-230: a captured Result is copied out at exit (LoweringContext.CopyCapturedResult)
            captured.AddRange(AnonymousBodies.SelectMany(b => b.CapturedVariables).Where(s => ReferenceEquals(s.ContainingSymbol, _routine)).Distinct());
        }

        closure.Complete(_types, captured);
    }

    /// <summary>
    /// LANG-720…722: <c>procedure(…) begin … end</c> / <c>function(…): T begin … end</c> — the method <c>Anon&lt;n&gt;</c> of this body's closure
    /// class, bound as a body nested in the current scope; the value is the closure's interface <c>$I&lt;n&gt;</c> for it (one closure per
    /// activation, so evaluating the expression twice gives equal references).
    /// </summary>
    private BoundExpression BindAnonymousMethod(AnonymousMethodExpressionSyntax syntax)
    {
        // without System (white-box binding) the method is still bound (its diagnostics come first), then reported as unsupported
        var root = _types.TInterfacedObject as ClassTypeSymbol;
        var reported = _diagnostics.Count;
        var closure = OwnClosure(syntax);
        var number = closure.Sites.Count + 1;
        var location = DiagnosticBag.LocationOf(syntax.Keyword);
        var method = new MethodSymbol("Anon" + number, closure.Class, [location], declaration: null!, MethodKind.Ordinary, Visibility.Private)
        {
            IsAnonymous = true,
            AnonymousParent = _routine,
            AnonymousFrame = closure,
            IsReferenced = true,
        };
        method.SelfParameter = new ParameterSymbol("Self", closure.Class, ParameterMode.Value, -1, method, []);
        var signature = new DeclarationBinder(_types, _scope, method, _diagnostics);
        signature.BindAnonymousSignature(method, syntax.Keyword, syntax.ParameterList, syntax.ResultType, declaresResult: true);
        var intf = signature.NewMethodReference(closure.Class.Name + ".$I" + number, closure.Class.ContainingSymbol, [location]);
        intf.IsAnonymousMethod = true;
        intf.Invoke.Parameters = method.Parameters;
        intf.Invoke.ReturnType = method.ReturnType;
        closure.Methods.Add((method, intf));
        closure.Sites.Add((method, intf, CurrentInline));

        var scope = new Scope(ScopeKind.Routine, _scope, method);
        if (method.ResultVariable is { } result)
        {
            scope.TryDeclare(result, out _);
        }

        foreach (var parameter in method.Parameters)
        {
            scope.TryDeclare(parameter, out _);
        }

        var locals = new DeclarationBinder(_types, scope, method, _diagnostics).BindDeclarations(syntax.Block.Declarations);
        var binder = new BodyBinder(_types, scope, _diagnostics, method);
        binder.AddReadOnlyVariables(ForVariables);                      // LANG-721: a captured for variable is read-only
        var body = binder.BindBody(syntax.Block.Body, syntax.Block.Body.Statements, syntax.Block.Declarations);
        binder.Expressions.CompleteClosure();
        AnonymousBodies.Add(new BoundRoutineBody(method, scope, body, [.. locals.OfType<RoutineSymbol>()], [.. binder.CapturedVariables], [])
        {
            AddressTaken = [.. binder.AddressTakenVariables],
        });
        AnonymousBodies.AddRange(binder.Expressions.AnonymousBodies);
        if (root is null || _types.IInterface is null)
        {
            return _diagnostics.Count == reported ? Unsupported(syntax, "anonymous method without System.TInterfacedObject") : Error(syntax);
        }

        return new BoundConversionExpression(syntax, new BoundVariableExpression(syntax, closure.Local), intf, ConversionKind.ImplicitReference,
            isExplicit: false, constantValue: null);
    }

    /// <summary>The closure of the body being bound, made with its first anonymous method (CMP-380).</summary>
    private ClosureInfo OwnClosure(SyntaxNode syntax)
    {
        if (OwnClosureInfo is { } existing)
        {
            return existing;
        }

        var closure = ClosureInfo.Create(_routine, GenericUses.Current ?? CurrentUnit!, _types, syntax);
        if (_routine is null)
        {
            _unitClosure = closure;
        }
        else
        {
            _routine.Closure = closure;
        }

        return closure;
    }

    /// <summary>
    /// LANG-721: a variable or parameter of an enclosing routine referenced from an anonymous method — <c>var</c>/<c>out</c>, open-array
    /// and untyped parameters cannot be captured (E2720).
    /// </summary>
    private void CheckCapture(Symbol symbol, SyntaxNode at)
    {
        if (_routine is null || symbol.ContainingSymbol is not RoutineSymbol owner || ReferenceEquals(owner, _routine))
        {
            return;
        }

        var anonymous = false;
        for (var routine = _routine; routine is not null && !ReferenceEquals(routine, owner); routine = ClosureInfo.LexicalParent(routine))
        {
            anonymous |= routine is MethodSymbol { IsAnonymous: true };
        }

        // C-230: a capture through nested routines as well is moved to the owner's closure after binding (ClosureInfo.ResolveMixedCaptures)
        if (anonymous && symbol is ParameterSymbol { Mode: ParameterMode.Var or ParameterMode.Out } or ParameterSymbol { IsOpenArray: true } or ParameterSymbol { IsUntyped: true })
        {
            _diagnostics.Report(at, "E2720", symbol.Name);
        }
    }

    /// <summary>
    /// LANG-723/724: <paramref name="syntax"/> where a value of the method reference type <paramref name="type"/> is expected — a
    /// parameterless reference (or function) is not called when its own value fits.
    /// </summary>
    private BoundExpression BindReferenceValue(ExpressionSyntax syntax, InterfaceTypeSymbol type)
    {
        // without System (white-box binding, the formatter's file-only model) there is no forwarder ancestor: the name binds as a value
        if (Unparenthesized(syntax) is not AnonymousMethodExpressionSyntax && type.Invoke.Parameters.All(p => !p.IsOpenArray && !p.IsUntyped)
            && _types.TInterfacedObject is ClassTypeSymbol && RoutineReference(syntax, type) is { } forwarded)
        {
            return forwarded;                                                   // LANG-485 (C-121)
        }

        var operand = BindOperand(syntax);
        return operand.HasErrors || _conversions.Classify(operand.Type, type).Exists ? operand : Called(operand);
    }

    /// <summary>
    /// LANG-485 / CMP-380 (C-121): a global routine or <c>class static</c> method (as a procedural value), or <c>Obj.Method</c> (as a method
    /// pointer, <c>Obj</c> not counted) where a method reference is expected — wrapped in a forwarder object whose <c>Invoke</c> calls it.
    /// Null when <paramref name="syntax"/> is no such name (or names a function to call), so it binds as a value.
    /// </summary>
    private BoundExpression? RoutineReference(ExpressionSyntax syntax, InterfaceTypeSymbol type)
    {
        foreach (var ofObject in new[] { false, true })
        {
            var probeType = ForwardedType(type, ofObject);
            var probe = new DiagnosticBag();
            var bound = ProbeBinder(probe).BindExpression(syntax, probeType);
            if (probe.ToImmutable() is [{ Code: "E2130" }])
            {
                // C-230: a nested function whose result fits the reference type is called (LANG-482), not converted
                var called = new DiagnosticBag();
                var result = ProbeBinder(called).BindOperand(syntax);
                return called.Count == 0 && !result.HasErrors && _conversions.Classify(result.Type, type).Exists
                    ? null
                    : BindExpression(syntax, probeType);                        // LANG-725: a nested routine (E2130 reported)
            }

            if (bound.HasErrors || probe.Count != 0 || !ReferenceEquals(bound.Type, probeType))
            {
                continue;
            }

            var forwarder = Forwarder(type, ofObject, syntax);
            var value = BindExpression(syntax, forwarder.Parameters[0].Type);
            return value.HasErrors ? value : new BoundCallExpression(syntax, forwarder, [value]);
        }

        return null;
    }

    /// <summary>A procedural (or method pointer) type with the signature of <paramref name="type"/>'s <c>Invoke</c>.</summary>
    private static TypeSymbol ForwardedType(InterfaceTypeSymbol type, bool ofObject)
    {
        TypeSymbol procedural;
        RoutineSymbol signature;
        if (ofObject)
        {
            var method = new MethodPointerTypeSymbol(type.Target, type.Name + ".$method", type.ContainingSymbol, []);
            (procedural, signature) = (method, method.Signature);
        }
        else
        {
            var plain = new ProceduralTypeSymbol(type.Target, type.Name + ".$routine", type.ContainingSymbol, []);
            (procedural, signature) = (plain, plain.Signature);
        }

        signature.Parameters = type.Invoke.Parameters;
        signature.ReturnType = type.Invoke.ReturnType;
        return procedural;
    }

    /// <summary>
    /// The unit's forwarder for <paramref name="type"/>: class <c>#Forward&lt;n&gt;</c> (a <c>TInterfacedObject</c> implementing the
    /// reference type) with field <c>F</c> and method <c>Invoke</c> calling <c>F</c>, and the function <c>#forward&lt;n&gt;(F): type</c> making
    /// one; both bound here as synthesised bodies (added to <see cref="AnonymousBodies"/>).
    /// </summary>
    private RoutineSymbol Forwarder(InterfaceTypeSymbol type, bool ofObject, SyntaxNode syntax)
    {
        var unit = GenericUses.Current ?? CurrentUnit!;
        lock (unit.Closures)
        {
            if (unit.Forwarders.TryGetValue((type, ofObject), out var existing))
            {
                return existing;
            }
        }

        var root = (ClassTypeSymbol)_types.TInterfacedObject!;
        ClassTypeSymbol @class;
        int number;
        lock (unit.Closures)
        {
            number = unit.Forwarders.Count + 1;
            @class = new ClassTypeSymbol(_types.Target, "#Forward" + number, unit, [])
            {
                IsForward = false,
                Parent = root,
                VirtualSlots = root.VirtualSlots,
                IsTypeInfo = root.IsTypeInfo,
                IsReferenced = true,
            };
            unit.Closures.Add(@class);
        }

        var fieldType = ForwardedType(type, ofObject);
        var alignment = Math.Max(1, Math.Min(fieldType.Alignment, 8));
        var offset = (root.InstanceSize + alignment - 1) / alignment * alignment;
        var field = new FieldSymbol("F", fieldType, offset, @class, [], Visibility.Private) { IsReferenced = true };
        var interfaceOffset = (offset + fieldType.Size + 7) / 8 * 8;
        @class.Fields = [field];
        @class.InstanceSize = interfaceOffset + 8;

        // Invoke: [Result :=] Self.F(parameters)
        var invoke = new MethodSymbol("Invoke", @class, [], declaration: null!, MethodKind.Ordinary, Visibility.Private) { IsReferenced = true };
        invoke.SelfParameter = new ParameterSymbol("Self", @class, ParameterMode.Value, -1, invoke, []);
        invoke.Parameters = [.. type.Invoke.Parameters.Select(p => new ParameterSymbol(p.Name, p.Type, p.Mode, p.Ordinal, invoke, []))];
        invoke.ReturnType = type.Invoke.ReturnType;
        var invokeScope = new Scope(ScopeKind.Routine, parent: null, invoke);
        if (invoke.ReturnType is { } resultType)
        {
            invoke.ResultVariable = new VariableSymbol("Result", resultType, isGlobal: false, invoke, []);
            invokeScope.TryDeclare(invoke.ResultVariable, out _);
        }

        foreach (var parameter in invoke.Parameters)
        {
            invokeScope.TryDeclare(parameter, out _);
        }

        var signature = fieldType is MethodPointerTypeSymbol m ? m.Signature : ((ProceduralTypeSymbol)fieldType).Signature;
        var target = new BoundFieldAccessExpression(syntax, new BoundParameterExpression(syntax, invoke.SelfParameter), field);
        ImmutableArray<BoundExpression> arguments = [.. invoke.Parameters.Select(p => (BoundExpression)new BoundParameterExpression(syntax, p))];
        BoundStatement forward = invoke.ResultVariable is { } result
            ? new BoundAssignmentStatement(syntax, new BoundVariableExpression(syntax, result), new BoundCallExpression(syntax, signature, arguments, target))
            : new BoundCallStatement(syntax, signature, arguments, target);
        @class.Methods = [invoke];
        var implementations = type.AllMethods.Take(type.AllMethods.Length - 1)
            .Select(i => DeclarationBinder.FindImplementation(@class, i.Name, i) ?? i).Append(invoke).ToImmutableArray();
        @class.Interfaces = [new ImplementedInterface(type, interfaceOffset, implementations)];

        // #forward<n>(F): type — o := Create; o.F := F; Result := o
        var maker = new RoutineSymbol("#forward" + number, unit, [], declaration: null!);
        var input = new ParameterSymbol("F", fieldType, ParameterMode.Value, 0, maker, []);
        maker.Parameters = [input];
        maker.ReturnType = type;
        maker.ResultVariable = new VariableSymbol("Result", type, isGlobal: false, maker, []);
        var instance = new VariableSymbol("Instance", @class, isGlobal: false, maker, []);
        var makerScope = new Scope(ScopeKind.Routine, parent: null, maker);
        makerScope.TryDeclare(maker.ResultVariable, out _);
        makerScope.TryDeclare(input, out _);
        makerScope.TryDeclare(instance, out _);
        var create = root.SelfAndAncestors().SelectMany(c => c.Methods)
            .First(c => c is { MethodKind: MethodKind.Constructor, Parameters.IsEmpty: true } && IdentifierComparer.Instance.Equals(c.Name, "Create"));
        var construct = new BoundCallExpression(syntax, create, [], type: @class)
        {
            Receiver = new BoundReceiver(new BoundClassReferenceExpression(syntax, @class, @class.Reference), IsVirtual: false, Flag: 1),
        };
        var made = new BoundBlock(syntax,
        [
            new BoundAssignmentStatement(syntax, new BoundVariableExpression(syntax, instance), construct),
            new BoundAssignmentStatement(syntax, new BoundFieldAccessExpression(syntax, new BoundVariableExpression(syntax, instance), field),
                new BoundParameterExpression(syntax, input)),
            new BoundAssignmentStatement(syntax, new BoundVariableExpression(syntax, maker.ResultVariable),
                new BoundConversionExpression(syntax, new BoundVariableExpression(syntax, instance), type, ConversionKind.ImplicitReference, isExplicit: false, constantValue: null)),
        ]);

        AnonymousBodies.Add(new BoundRoutineBody(invoke, invokeScope, new BoundBlock(syntax, [forward]), [], [], []));
        AnonymousBodies.Add(new BoundRoutineBody(maker, makerScope, made, [], [], []));
        lock (unit.Closures)
        {
            unit.Forwarders[(type, ofObject)] = maker;
        }

        return maker;
    }
}
