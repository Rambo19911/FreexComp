using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// Binds expressions of routine bodies and initialisers to typed <see cref="BoundExpression"/>s (integer, Boolean,
/// char and real operands: LANG-220…230, LANG-300…306). Operands are wrapped in implicit conversions to the type the
/// operation is done in; constant sub-expressions are folded by the <see cref="ConstantFolder"/> (LANG-340). An
/// operand of the error type gives the error type without further diagnostics (CMP-135).
/// </summary>
internal sealed partial class ExpressionBinder
{
    private readonly BuiltInTypes _types;
    private Scope _scope;
    private readonly DiagnosticBag _diagnostics;
    private ConstantFolder _folder;
    private readonly Conversions _conversions = new();
    private readonly RoutineSymbol? _routine;

    /// <param name="routine">The routine whose body is bound; null at unit level.</param>
    public ExpressionBinder(BuiltInTypes types, Scope scope, DiagnosticBag diagnostics, RoutineSymbol? routine = null)
    {
        _types = types;
        _scope = scope;
        _diagnostics = diagnostics;
        _routine = routine;
        _folder = new ConstantFolder(types, scope, diagnostics);
    }

    /// <summary>
    /// LANG-977 (C-417): counts the type queries and the type-query dependent constants bound so far; a constant expression bound
    /// between two readings that differ is type-query dependent (its syntax contains a type query or names such a constant).
    /// </summary>
    internal int TypeQueryUses { get; private set; }

    /// <summary>Binds with <paramref name="scope"/> (a block scope inside the current one, LANG-821's handler variable) as the scope.</summary>
    public T InScope<T>(Scope scope, Func<T> bind)
    {
        var (outerScope, outerFolder) = (_scope, _folder);
        _scope = scope;
        _folder = new ConstantFolder(_types, scope, _diagnostics);
        try
        {
            return bind();
        }
        finally
        {
            (_scope, _folder) = (outerScope, outerFolder);
        }
    }

    /// <summary>Control variables (variables or value parameters) of the enclosing <c>for</c> loops (LANG-361 rule 6).</summary>
    public HashSet<Symbol> ForVariables { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>The hidden <c>with</c> pointers to a <c>const</c> record parameter or typed constant: read-only targets (C-198).</summary>
    public HashSet<Symbol> ReadOnlyWith { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Variables, parameters and <c>Result</c> of enclosing routines referenced here, in first-reference order (CMP-345).</summary>
    public List<Symbol> CapturedVariables { get; } = [];

    /// <summary>Variables whose address (or the address of a part) is taken with <c>@</c>/<c>Addr</c> (CMP-325 rule 4, C-062).</summary>
    public HashSet<Symbol> AddressTakenVariables { get; } = [];

    /// <summary>Records <paramref name="symbol"/> as captured when it belongs to an enclosing routine (LANG-450).</summary>
    public void NoteReference(Symbol symbol)
    {
        if (_routine != null && symbol is VariableSymbol { IsGlobal: false, ContainingSymbol: not RoutineSymbol } bodyLocal)
        {
            bodyLocal.IsCapturedBodyLocal = true;               // a unit-level body's inline variable used by an anonymous method
            if (!CapturedVariables.Contains(symbol))
            {
                CapturedVariables.Add(symbol);                  // C-308: it may move to a block frame (ClosureInfo.ResolveMixedCaptures)
            }
        }

        if (_routine != null && symbol is (VariableSymbol { IsGlobal: false } or ParameterSymbol)
            && symbol.ContainingSymbol is RoutineSymbol owner && !ReferenceEquals(owner, _routine) && !CapturedVariables.Contains(symbol))
        {
            CapturedVariables.Add(symbol);
            if (symbol is ParameterSymbol { HighParameter: { } high })
            {
                CapturedVariables.Add(high);                    // an open array is captured with its High companion (C-106)
            }
        }
    }

    /// <summary>
    /// <paramref name="syntax"/> in a context of type <paramref name="expected"/>: a set constructor takes that set type
    /// (LANG-251); a <c>+</c> in a dynamic-array context passes it to its constructor operands (LANG-275 rule 3); anything else
    /// binds as without context.
    /// </summary>
    public BoundExpression BindExpression(ExpressionSyntax syntax, TypeSymbol? expected) =>
        InterfaceAsGuid(syntax, expected) is { } guid ? guid :                    // LANG-680 (C-299)
        expected is DynamicArrayTypeSymbol arrayContext && Unparenthesized(syntax) is BinaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.PlusToken } plus
            ? BindBinary(plus, arrayContext) :                                    // LANG-275 rule 3 (C-390)
        expected is DynamicArrayTypeSymbol concatContext && ConcatIntrinsicCall(syntax) is var (concat, intrinsic) && concat is not null
            ? BindStringFunction(concat, intrinsic!, concatContext) :             // LANG-279 (C-391): Concat([…], […]) in that context
        Unparenthesized(syntax) is SetConstructorSyntax constructor
            ? expected switch
            {
                SetTypeSymbol { ElementType: not null } set => BindSetConstructor(constructor, set),
                DynamicArrayTypeSymbol array when constructor.Elements.All(e => e.UpperBound is null) => BindArrayConstructor(constructor, array, array.ElementType),
                OpenArrayTypeSymbol { IsArrayOfConst: true } array when constructor.Elements.All(e => e.UpperBound is null) => BindVarRecConstructor(constructor, array),
                OpenArrayTypeSymbol array when constructor.Elements.All(e => e.UpperBound is null) => BindArrayConstructor(constructor, array, array.ElementType),
                _ => BindExpression(syntax),
            }
            : expected is ProceduralTypeSymbol procedural
                ? BindProceduralValue(syntax, procedural)
                : expected is MethodPointerTypeSymbol methodPointer
                    ? BindMethodPointerValue(syntax, methodPointer)
                    : expected is InterfaceTypeSymbol { IsMethodReference: true } reference
                        ? BindReferenceValue(syntax, reference)
                        : BindExpression(syntax);

    /// <summary>
    /// LANG-680 (C-299): <paramref name="syntax"/> where a <c>TGUID</c> (<paramref name="expected"/>) is required — the identifier of an
    /// interface type denotes its GUID (E2655 when it has none). Null when <paramref name="expected"/> is not <c>TGUID</c> or the
    /// expression names no interface type; the caller then binds as usual.
    /// </summary>
    private BoundExpression? InterfaceAsGuid(ExpressionSyntax syntax, TypeSymbol? expected)
    {
        if (!GuidValues.IsGuidType(expected) || InterfaceNamedBy(syntax) is not { } intf)
        {
            return null;
        }

        if (intf.Guid is null)
        {
            _diagnostics.Report(syntax, "E2655", intf.Name);
            return Error(syntax);
        }

        return new BoundInterfaceGuidExpression(syntax, intf, expected!);
    }

    /// <summary>A generic type of the name (any arity) is in scope: <paramref name="name"/> is no generic method's name.</summary>
    private bool NamesGenericType(GenericNameSyntax name)
    {
        var text = name.Identifier.Value as string ?? name.Identifier.Text;
        return Enumerable.Range(0, 16).Any(n => _scope.Lookup(GenericTypeSymbol.KeyOf(text, n)) is GenericTypeSymbol);
    }

    /// <summary>The (ordinary) interface type an expression names — <c>IFoo</c>, <c>U.IFoo</c>; null otherwise, nothing reported.</summary>
    private InterfaceTypeSymbol? InterfaceNamedBy(ExpressionSyntax syntax)
    {
        var inner = Unparenthesized(syntax);
        return (inner is IdentifierNameSyntax or MemberAccessExpressionSyntax || inner is GenericNameSyntax generic && NamesGenericType(generic))
            && NamedType(inner) is InterfaceTypeSymbol { IsMethodReference: false } intf ? intf : null;          // LANG-784 (C-346): IBox<Integer>
    }

    /// <summary>
    /// LANG-481 (C-103): <paramref name="syntax"/> where a method pointer of <paramref name="type"/> is expected — <c>Obj.Method</c>,
    /// <c>Method</c> inside a method (on <c>Self</c>), <c>TClass.ClassMethod</c> / <c>ClassRef.ClassMethod</c> (data = the class), each
    /// also with <c>@</c>; anything else (another method pointer, <c>nil</c>, a call) binds without the implicit call.
    /// </summary>
    private BoundExpression BindMethodPointerValue(ExpressionSyntax syntax, MethodPointerTypeSymbol type)
    {
        var inner = Unparenthesized(syntax);
        var addressOf = inner is AddressOfExpressionSyntax at ? at : null;
        var target = addressOf is null ? inner : Unparenthesized(addressOf.Operand);
        ImmutableArray<RoutineSymbol> candidates = [];
        BoundExpression? self = null;
        ClassTypeSymbol? owner = null;
        IdentifierNameSyntax? name = null;
        switch (target)
        {
            case IdentifierNameSyntax { Identifier: { IsMissing: false } plainToken } plainName
                when _scope.For(plainName).LookupCandidates(plainToken.Value as string ?? plainToken.Text) is [var routine and not MethodSymbol, ..] routines
                    && !(addressOf is null && routines.Length == 1 && routine is { ReturnType: { } plainResult, Parameters.IsEmpty: true }
                        && _conversions.Classify(plainResult, type).Exists):
                _diagnostics.Report(syntax, "E2010", Signatures.Display(routine), type.Name);       // a global routine has no Data
                return Error(syntax);
            case IdentifierNameSyntax { Identifier: { IsMissing: false } withToken } withName
                when _withs.Count > 0
                    && _scope.For(withName).LookupCandidates(withToken.Value as string ?? withToken.Text) is [MethodSymbol { ContainingInterface: null } first, ..] withMethods
                    && WithOf(withToken.Value as string ?? withToken.Text, first) is { Type: ClassTypeSymbol withClass } with:
                (candidates, name, owner) = (withMethods, withName, withClass);           // C-223: a method of a with receiver
                self = WithReceiver(withName, with);
                break;
            case IdentifierNameSyntax { Identifier: { IsMissing: false } token } identifier
                when _scope.For(identifier).LookupCandidates(token.Value as string ?? token.Text) is [MethodSymbol { ContainingInterface: null }, ..] methods
                    && CurrentMethod is { SelfParameter: not null, ContainingSymbol: ClassTypeSymbol } current:
                (candidates, name, owner) = (methods, identifier, current.ContainingType);
                self = SelfOf(current, identifier);
                break;
            case MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier.IsMissing: false } memberName } member:
            {
                var memberText = memberName.Identifier.Value as string ?? memberName.Identifier.Text;
                if (_scope.Unqualified(member.Expression) is IdentifierNameSyntax { Identifier: { IsMissing: false } q } qualifier
                    && _scope.For(qualifier).Lookup(q.Value as string ?? q.Text) is ClassTypeSymbol @class)
                {
                    owner = @class;
                    self = new BoundClassReferenceExpression(qualifier, @class, @class.Reference);
                }
                else if (_scope.Unqualified(member.Expression) is not IdentifierNameSyntax { Identifier: var u } unit
                    || _scope.For(unit).Lookup(u.Value as string ?? u.Text) is not (UnitSymbol or TypeSymbol))
                {
                    var receiver = BindExpression(member.Expression);
                    if (receiver.HasErrors)
                    {
                        return Error(syntax, receiver);
                    }

                    if (receiver.Type is InterfaceTypeSymbol intf && InterfaceCandidates(intf, memberText) is { IsEmpty: false } methods)
                    {
                        return BindInterfaceMethodPointer(syntax, methods, receiver, type);
                    }

                    (owner, self) = receiver.Type switch
                    {
                        ClassTypeSymbol c => (c, receiver),
                        ClassRefTypeSymbol r => (r.Class, receiver),
                        _ => (null, null),
                    };
                }

                if (owner is not null)
                {
                    candidates = MemberCandidates(owner, memberText);
                    name = memberName;
                }

                break;
            }
        }

        if (candidates.IsEmpty || !candidates.All(c => c is MethodSymbol) || self is null
            || addressOf is null && candidates is [{ ReturnType: { } result, Parameters.IsEmpty: true } only]
                && !Signatures.Compatible(only, type.Signature) && _conversions.Classify(result, type).Exists)
        {
            return BindOperand(syntax);                                         // not a method, or a method call yielding the value
        }

        var compatible = candidates.Cast<MethodSymbol>().Where(m => Signatures.Compatible(m, type.Signature)).ToList();
        if (compatible.FirstOrDefault() is not { } method)
        {
            if (candidates.Length > 1)
            {
                _diagnostics.Report(syntax, "E2131", candidates[0].Name, type.Name);
            }
            else
            {
                _diagnostics.Report(syntax, "E2010", Signatures.Display(candidates[0]) + (candidates[0] is MethodSymbol { MethodKind: MethodKind.ClassStatic } ? "" : " of object"), type.Name);
            }

            return Error(syntax, self);
        }

        // instance methods need an instance; class methods take the class (an instance's VMT); static methods, constructors and
        // destructors have no method pointer (E2010)
        var onClass = self.Type is ClassRefTypeSymbol;
        if (method.MethodKind is MethodKind.ClassStatic or MethodKind.Constructor or MethodKind.Destructor || onClass && !method.IsClassMethod)
        {
            _diagnostics.Report(syntax, "E2010", Signatures.Display(method) + (method.MethodKind == MethodKind.ClassStatic ? "" : " of object"), type.Name);
            return Error(syntax, self);
        }

        if (!CheckAccess(name!, method, method.ContainingType, owner))
        {
            return Error(syntax, self);
        }

        if (method.IsClassMethod && !onClass)
        {
            self = ClassOf(syntax, self);
        }

        CheckHintDirectives(method, syntax);                                    // LANG-472 (C-232): a method reference
        return new BoundMethodPointerExpression(syntax, method, self, method.IsDispatched, type);
    }

    /// <summary>
    /// C-207: <c>Intf.M</c> where a method pointer is expected — Data = the interface reference (not counted), Code = its IMT slot,
    /// the thunk that adjusts <c>Self</c> (CMP-372), so a call <c>Code(Data, args)</c> reaches the implementing method.
    /// </summary>
    private BoundExpression BindInterfaceMethodPointer(ExpressionSyntax syntax, ImmutableArray<RoutineSymbol> candidates, BoundExpression receiver,
        MethodPointerTypeSymbol type)
    {
        if (candidates.Any(c => c is MethodSymbol { DispatchFlags: not 0 }))
        {
            _diagnostics.Report(syntax, "E2660", "@");                              // LANG-697: no procedural value of a dispatch member
            return Error(syntax, receiver);
        }

        if (candidates.Cast<MethodSymbol>().FirstOrDefault(m => Signatures.Compatible(m, type.Signature)) is { } method)
        {
            CheckHintDirectives(method, syntax);                                // LANG-472 (C-232)
            return new BoundMethodPointerExpression(syntax, method, receiver, isVirtual: true, type);
        }

        if (candidates is [{ ReturnType: { } result, Parameters.IsEmpty: true }] && _conversions.Classify(result, type).Exists)
        {
            return BindOperand(syntax);                                             // a call yielding the method pointer
        }

        _diagnostics.Report(syntax, candidates.Length > 1 ? "E2131" : "E2010",
            candidates.Length > 1 ? candidates[0].Name : Signatures.Display(candidates[0]) + " of object", type.Name);
        return Error(syntax, receiver);
    }

    // ------------------------------------------------------------------ procedural values (LANG-480…484, C-161)

    /// <summary>
    /// <paramref name="syntax"/> where a value of the procedural type <paramref name="type"/> is expected: <c>Routine</c> or
    /// <c>@Routine</c> is the routine's address (LANG-480; a parameterless function whose result fits the target is called
    /// instead), anything else is bound without the implicit call (LANG-482: <c>p1 := p2</c> copies).
    /// </summary>
    private BoundExpression BindProceduralValue(ExpressionSyntax syntax, ProceduralTypeSymbol type)
    {
        var inner = Unparenthesized(syntax);
        var addressOf = inner is AddressOfExpressionSyntax at ? at : null;
        if (_scope.Unqualified(addressOf is null ? inner : Unparenthesized(addressOf.Operand)) is IdentifierNameSyntax { Identifier: { IsMissing: false } token } name
            && _scope.For(name).LookupCandidates(token.Value as string ?? token.Text) is [var routine, ..] candidates)
        {
            var called = addressOf is null && candidates.Length == 1 && routine is { ReturnType: { } result, Parameters.IsEmpty: true }
                && !Signatures.Compatible(routine, type.Signature) && _conversions.Classify(result, type).Exists;
            if (!called)
            {
                return RoutineValue(syntax, name, candidates, type);
            }
        }

        // LANG-480 (C-102): TClass.StaticMethod / @TClass.StaticMethod — a class static method of a class named by the qualifier
        if (Unparenthesized(addressOf is null ? inner : Unparenthesized(addressOf.Operand)) is MemberAccessExpressionSyntax
            {
                Name: IdentifierNameSyntax { Identifier: { IsMissing: false } memberToken } memberName,
            } member
            && _scope.Unqualified(member.Expression) is IdentifierNameSyntax { Identifier: { IsMissing: false } classToken } className
            && _scope.For(className).Lookup(classToken.Value as string ?? classToken.Text) is ClassTypeSymbol @class
            && MemberCandidates(@class, memberToken.Value as string ?? memberToken.Text) is { IsEmpty: false } methods
            && !(addressOf is null && methods is [{ ReturnType: { } methodResult, Parameters.IsEmpty: true } only]
                && !Signatures.Compatible(only, type.Signature) && _conversions.Classify(methodResult, type).Exists))
        {
            // other methods are reported by RoutineValue (E2010: no plain procedural value)
            return RoutineValue(syntax, memberName, methods, type);
        }

        // Obj.Method of an instance: a method pointer, not a plain procedural value (E2010, C-103)
        if (Unparenthesized(addressOf is null ? inner : Unparenthesized(addressOf.Operand)) is MemberAccessExpressionSyntax
            {
                Name: IdentifierNameSyntax { Identifier: { IsMissing: false } instanceMember },
            } access
            && (_scope.Unqualified(access.Expression) is not IdentifierNameSyntax { Identifier: var head } headName
                || _scope.For(headName).Lookup(head.Value as string ?? head.Text) is VariableSymbol or ParameterSymbol or FieldSymbol)
            && BindExpression(access.Expression) is { HasErrors: false, Type: ClassTypeSymbol instanceClass } instance
            && MemberCandidates(instanceClass, instanceMember.Value as string ?? instanceMember.Text) is [MethodSymbol { MethodKind: not MethodKind.ClassStatic } first, ..] instanceMethods
            && !(addressOf is null && instanceMethods.Length == 1 && first is { ReturnType: { } instanceResult, Parameters.IsEmpty: true }
                && _conversions.Classify(instanceResult, type).Exists))
        {
            _diagnostics.Report(syntax, "E2010", Signatures.Display(first) + " of object", type.Name);
            return Error(syntax, instance);
        }

        return BindOperand(syntax);
    }

    /// <summary>
    /// The address of a global routine or a <c>class static</c> method as a value of <paramref name="type"/> (LANG-480, 483; E2130
    /// for a nested routine, LANG-451); of an overload set the one compatible with the type (E2131 when none, C-102). Other
    /// methods are no plain procedural values (E2010).
    /// </summary>
    private BoundExpression RoutineValue(ExpressionSyntax syntax, IdentifierNameSyntax name, ImmutableArray<RoutineSymbol> candidates, ProceduralTypeSymbol type)
    {
        var routine = candidates[0];
        if (candidates.Length > 1)
        {
            if (candidates.FirstOrDefault(c => Signatures.Compatible(c, type.Signature) && c is not MethodSymbol { MethodKind: not MethodKind.ClassStatic }) is not { } match)
            {
                _diagnostics.Report(syntax, "E2131", routine.Name, type.Name);
                return Error(syntax);
            }

            routine = match;
        }

        if (routine is MethodSymbol { MethodKind: not MethodKind.ClassStatic })
        {
            _diagnostics.Report(syntax, "E2010", Signatures.Display(routine) + " of object", type.Name);     // a method needs 'of object' (LANG-481)
            return Error(syntax);
        }

        if (routine.IsNested)
        {
            _diagnostics.Report(name, "E2130", routine.Name);
            return Error(syntax);
        }

        if (!Signatures.Compatible(routine, type.Signature))
        {
            _diagnostics.Report(syntax, "E2010", Signatures.Display(routine), type.Name);
            return Error(syntax);
        }

        CheckHintDirectives(routine, name);                                     // LANG-472 (C-232): a procedural reference
        return new BoundAddressOfExpression(syntax, operand: null, routine, type);
    }

    /// <summary>
    /// LANG-482: a procedural variable (or field, element…) of a parameterless function type in a value position is called;
    /// routine values and call results are left alone.
    /// </summary>
    private static BoundExpression Called(BoundExpression value) =>
        value is not (BoundAddressOfExpression or BoundCallExpression or BoundErrorExpression)
        && SignatureOf(value.Type) is { ReturnType: not null, Parameters.IsEmpty: true } signature
            ? new BoundCallExpression(value.Syntax, signature, [], value)
            : value;

    /// <summary>The signature called through a value of <paramref name="type"/>: a plain procedural type or a method pointer (C-103).</summary>
    private static RoutineSymbol? SignatureOf(TypeSymbol type) => type switch
    {
        ProceduralTypeSymbol procedural => procedural.Signature,
        MethodPointerTypeSymbol method => method.Signature,
        InterfaceTypeSymbol { IsMethodReference: true } reference => reference.Invoke,     // LANG-724 (C-120)
        _ => null,
    };

    /// <summary><c>target(args)</c>: a call through a procedural value, else E2900 "routine call".</summary>
    private BoundExpression CallThrough(InvocationExpressionSyntax invocation, BoundExpression target) =>
        target.HasErrors ? Error(invocation, target)
        : SignatureOf(target.Type) is { } type ? IndirectCall(invocation, target, type, invocation.ArgumentList)
        : Unsupported(invocation, "routine call");

    /// <summary>
    /// LANG-484 <c>target(args)</c> through a procedural value (arguments checked like a direct call; E2034 for a procedure type
    /// in an expression).
    /// </summary>
    private BoundExpression IndirectCall(SyntaxNode call, BoundExpression target, RoutineSymbol type, ArgumentListSyntax? argumentList)
    {
        var (signature, arguments) = BindCall(call, [type], argumentList);
        if (signature is null)
        {
            return Error(call, [target, .. arguments]);
        }

        if (signature.ReturnType is null)
        {
            _diagnostics.Report(call, "E2034");
            return Error(call, [target, .. arguments]);
        }

        return new BoundCallExpression(call, signature, arguments, target);
    }

    /// <summary>
    /// LANG-484 as a statement: <c>p</c>, <c>p(args)</c>, <c>r.F(args)</c>, <c>A[i](args)</c>, <c>F(x)(args)</c> when the callee is
    /// a procedural value. Null when the statement does not start with a value (routine, intrinsic, type or unknown names:
    /// the caller binds those); E2060 / E2900 "routine call" when the callee is a value of another type.
    /// </summary>
    public BoundStatement? BindIndirectCallStatement(ExpressionStatementSyntax statement)
    {
        var invocation = statement.Expression as InvocationExpressionSyntax;
        var callee = invocation?.Expression ?? statement.Expression;
        if (_scope.Unqualified(callee) is IdentifierNameSyntax { Identifier: var token } name
            && (token.IsMissing || _scope.For(name).Lookup(token.Value as string ?? token.Text) is null or RoutineSymbol or IntrinsicSymbol or TypeSymbol))
        {
            return null;
        }

        var target = BindOperand(callee);
        if (target.HasErrors)
        {
            return new BoundErrorStatement(statement);
        }

        if (SignatureOf(target.Type) is { } type)
        {
            var (signature, arguments) = BindCall(statement.Expression, [type], invocation?.ArgumentList);
            return signature is null ? new BoundErrorStatement(statement) : new BoundCallStatement(statement, signature, arguments, target);
        }

        if (invocation is not null)
        {
            Unsupported(invocation, "routine call");
            return new BoundErrorStatement(statement);
        }

        _diagnostics.Report(statement.Expression, "E2060", target.Type.Name);                 // LANG-352
        return new BoundExpressionStatement(statement, target);
    }

    /// <summary>
    /// LANG-259 / LANG-280 rule 12 (C-067): <c>[e1, …]</c> in a dynamic-array context; every element is bound in the context of
    /// the element type (so nested constructors work) and converted to it. A range element keeps the set meaning (E2010 later).
    /// </summary>
    private BoundExpression BindArrayConstructor(SetConstructorSyntax syntax, TypeSymbol array, TypeSymbol elementType) =>
        BindArrayElements(syntax, [.. syntax.Elements.Select(e => e.Expression)], array, elementType);

    /// <summary>
    /// LANG-271 (C-388): <c>D.Create(e1, …)</c> of a named dynamic-array type — the value <c>[e1, …]</c> of type <paramref name="array"/>,
    /// every argument bound and converted like a constructor element (no argument list or <c>()</c>: <c>nil</c> of that type).
    /// </summary>
    private BoundExpression BindDynArrayCreate(SyntaxNode syntax, ArgumentListSyntax? argumentList, DynamicArrayTypeSymbol array)
    {
        var arguments = argumentList?.Arguments ?? default;
        var expressions = ImmutableArray.CreateBuilder<ExpressionSyntax>(arguments.Count);
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].WidthColonToken is { } colon)
            {
                _diagnostics.Report(colon, "E2915");
                return Error(syntax);
            }

            expressions.Add(arguments[i].Expression);
        }

        return BindArrayElements(syntax, expressions.MoveToImmutable(), array, array.ElementType);
    }

    private BoundExpression BindArrayElements(SyntaxNode syntax, ImmutableArray<ExpressionSyntax> syntaxes, TypeSymbol array, TypeSymbol elementType)
    {
        var elements = ImmutableArray.CreateBuilder<BoundExpression>(syntaxes.Length);
        foreach (var element in syntaxes)
        {
            var value = BindExpression(element, elementType);
            elements.Add(value.HasErrors ? value : ConvertForAssignment(value, elementType, element));
        }

        var built = elements.MoveToImmutable();
        return built.Any(e => e.HasErrors) ? Error(syntax, [.. built]) : new BoundArrayConstructor(syntax, built, array);
    }

    /// <summary>
    /// LANG-433 (C-107): <c>[e1, …]</c> for an <c>array of const</c> parameter — each element a value of a kind rtl-abi.md §9.10 maps
    /// to a <c>VType</c> (E2010 against <c>array of const</c> otherwise), kept in its own type for the lowering.
    /// </summary>
    private BoundExpression BindVarRecConstructor(SetConstructorSyntax syntax, OpenArrayTypeSymbol array)
    {
        var elements = ImmutableArray.CreateBuilder<BoundExpression>(syntax.Elements.Count);
        foreach (var element in syntax.Elements)
        {
            var value = BindExpression(element.Expression);
            if (!value.HasErrors && VarRecs.VTypeOf(value.Type, value.ConstantValue) is null)
            {
                _diagnostics.Report(element.Expression, "E2010", value.Type.Name, array.Name);
                value = Error(element.Expression, value);
            }

            elements.Add(value);
        }

        var built = elements.MoveToImmutable();
        return built.Any(e => e.HasErrors) ? Error(syntax, [.. built]) : new BoundArrayConstructor(syntax, built, array);
    }

    internal static ExpressionSyntax Unparenthesized(ExpressionSyntax syntax)
    {
        while (syntax is ParenthesizedExpressionSyntax parenthesized)
        {
            syntax = parenthesized.Expression;
        }

        return syntax;
    }

    /// <summary><paramref name="syntax"/> as a value (a parameterless function-typed procedural variable is called, LANG-482).</summary>
    public BoundExpression BindExpression(ExpressionSyntax syntax) => Called(BindOperand(syntax));

    /// <summary>
    /// <paramref name="syntax"/> without the implicit call of a procedural value (LANG-482): assignment targets, <c>@</c>,
    /// <c>Assigned</c>, comparisons with <c>nil</c>, typecast operands and callees.
    /// </summary>
    public BoundExpression BindOperand(ExpressionSyntax syntax)
    {
        if (syntax.IsMissing)
        {
            return Error(syntax);
        }

        switch (syntax)
        {
            case SetConstructorSyntax constructor:
                return BindSetConstructor(constructor, expected: null);
            case LiteralExpressionSyntax literal:
                return BindLiteral(literal);
            case IdentifierNameSyntax name:
                return BindName(name);
            case GenericNameSyntax generic:
                switch (GenericType(generic))
                {
                    case ClassTypeSymbol classType:
                        return new BoundClassReferenceExpression(generic, classType, classType.Reference);     // LANG-652 (C-116)
                    case ErrorTypeSymbol:
                        return Error(generic);
                    case var type:
                        _diagnostics.Report(generic, "E2009", type.Name);
                        return Error(generic);
                }

            case ParenthesizedExpressionSyntax parenthesized:
                return BindOperand(parenthesized.Expression);
            case UnaryExpressionSyntax unary:
                return BindUnary(unary);
            case BinaryExpressionSyntax binary:
                return BindBinary(binary);
            case InvocationExpressionSyntax invocation:
                return BindInvocation(invocation);
            case AnonymousMethodExpressionSyntax anonymous:
                return BindAnonymousMethod(anonymous);                          // LANG-720 (C-120)
            case AddressOfExpressionSyntax addressOf:
                return BindAddressOf(addressOf);
            case DereferenceExpressionSyntax dereference:
                return BindDereference(dereference, untyped: false);
            case ElementAccessExpressionSyntax elementAccess:
                return BindElementAccess(elementAccess);
            case StringCastExpressionSyntax cast:
            {
                // string(x) (LANG-290 row 7, C-063)
                var operand = BindExpression(cast.Expression);
                return operand.HasErrors ? Error(cast, operand) : StringTypecast(cast, _types.String, operand);
            }

            case InheritedExpressionSyntax inherited:
                return InheritedProperty(inherited)                                                                 // C-270
                    ?? MethodCallExpression(inherited, BindInherited(inherited, inherited, argumentList: null, bare: false));    // LANG-624
            case PredefinedTypeExpressionSyntax predefined:
                _diagnostics.Report(predefined, "E2009", predefined.Keyword.Text);
                return Error(syntax);
            case MemberAccessExpressionSyntax memberAccess when _scope.Unqualified(memberAccess) is IdentifierNameSyntax qualified:
                return BindName(qualified);                                     // U.Name (LANG-311, 538)
            case MemberAccessExpressionSyntax memberAccess:
            {
                // TEnum.Element (LANG-210), record fields (LANG-260); class qualifications arrive later
                var member = _folder.LookupEnumMember(memberAccess, out var isEnumAccess);
                return member is not null ? new BoundLiteral(memberAccess, member.Value)
                    : isEnumAccess ? Error(syntax)
                    : BindMemberAccess(memberAccess);
            }
            default:
                return Unsupported(syntax, DeclarationBinder.FeatureName(syntax.Kind));
        }
    }

    /// <summary>
    /// A condition of <c>if</c>/<c>while</c>/<c>repeat</c> (LANG-354/360): Boolean family, converted to <c>Boolean</c>
    /// (LANG-215); E2061 otherwise.
    /// </summary>
    public BoundExpression BindCondition(ExpressionSyntax syntax)
    {
        var condition = BindExpression(syntax);
        if (condition.HasErrors)
        {
            return condition;
        }

        if (condition.Type is not BooleanTypeSymbol)
        {
            _diagnostics.Report(syntax, "E2061", condition.Type.Name);
            return Error(syntax, condition);
        }

        return Convert(condition, _types.Boolean);
    }

    /// <summary>
    /// <paramref name="value"/> converted for assignment to <paramref name="destination"/> (LANG-280/351); E2010 / E2012
    /// at <paramref name="at"/> and an error expression when it is not assignment-compatible.
    /// </summary>
    public BoundExpression ConvertForAssignment(BoundExpression value, TypeSymbol destination, SyntaxNode at)
    {
        if (value.HasErrors || destination.TypeKind == TypeKind.Error || ReferenceEquals(value.Type, destination))
        {
            return value;
        }
        if ((value.Type is RecordTypeSymbol || destination is RecordTypeSymbol) && Operators.Conversion(value.Type, destination, _conversions) is { } implicitOperator)
        {
            return ConversionCall(at, value, implicitOperator);                 // LANG-740 (C-123), LANG-280 rule 13
        }

        if (value.ConstantValue is { } constant)
        {
            if (_conversions.ConvertConstant(constant, destination, at, _diagnostics) is not { } converted)
            {
                return Error(at, value);
            }

            // a character constant converts to AnsiChar although Char does not (LANG-262)
            var kind = _conversions.Classify(value.Type, destination).Kind;
            return new BoundConversionExpression(value.Syntax, value, destination,
                kind == ConversionKind.None ? ConversionKind.ImplicitNumeric : kind, isExplicit: false, converted);
        }

        var conversion = _conversions.Classify(value.Type, destination);
        if (!conversion.Exists)
        {
            _conversions.ReportIncompatible(value.Type, destination, at, _diagnostics);
            return Error(at, value);
        }

        if (Conversions.IsNarrowing(value.Type, destination))
        {
            _diagnostics.Report(at, "W1020", value.Type.Name, destination.Name);     // LANG-245 (not for constants)
        }

        return new BoundConversionExpression(value.Syntax, value, destination, conversion.Kind, isExplicit: false, constantValue: null);
    }

    // ------------------------------------------------------------------ factors

    private BoundExpression BindLiteral(LiteralExpressionSyntax literal)
    {

        return _folder.FoldLiteral(literal) is { } value ? new BoundLiteral(literal, value) : Error(literal);
    }

    private BoundExpression BindName(IdentifierNameSyntax name)
    {
        var found = LookupName(name);
        if (found is not null && _withs.Count > 0 && WithOf(name.Identifier.Value as string ?? name.Identifier.Text, found) is { } with)
        {
            return WithMember(name, with, found);                                   // LANG-375 (C-109)
        }

        switch (found)
        {
            case null:
                return Error(name);
            case VariableSymbol { ContainingSymbol: ClassTypeSymbol or RecordTypeSymbol or HelperTypeSymbol } classVar:
                return ClassVariable(name, name, classVar, CurrentMethod?.Owner);      // LANG-615 inside a method
            case VariableSymbol variable:
                NoteReference(variable);
                CheckCapture(variable, name);
                return new BoundVariableExpression(name, variable);
            case PropertySymbol property:
                return ImplicitProperty(name, property);                            // LANG-641 inside a method
            case ParameterSymbol parameter:
                NoteReference(parameter);
                CheckCapture(parameter, name);
                return new BoundParameterExpression(name, parameter);
            case FieldSymbol field:
                return ImplicitField(name, field);                                  // a member of Self (LANG-621)
            case MethodSymbol:
                return ImplicitMethodCall(name, name, argumentList: null);
            case RoutineSymbol:
                return CallExpression(name, name, argumentList: null);             // LANG-312: parameterless call
            case ConstantSymbol constant when NestedNames.ReportInaccessibleNestedValue(_scope, name, constant, _diagnostics):
                return Error(name);                                                 // LANG-774 (C-361): an inherited nested constant
            case ConstantSymbol { IsTyped: false, Value: { } value } constant:
                TypeQueryUses += constant.IsTypeQueryDependent ? 1 : 0;         // LANG-977 (C-417)
                return new BoundConstantExpression(name, constant, value);
            case ConstantSymbol { IsTyped: true, Storage: { } storage }:
                return new BoundVariableExpression(name, storage);             // LANG-344: a read-only variable
            case ConstantSymbol:
                return Error(name);                                     // its declaration already reported the error
            case EnumMemberSymbol member when NestedNames.ReportInaccessibleNestedValue(_scope, name, member, _diagnostics):
                return Error(name);                                                 // LANG-774 (C-363): an element of an inherited nested enumeration
            case EnumMemberSymbol member:
                return new BoundLiteral(name, member.Value);
            case ClassTypeSymbol classType:
                return new BoundClassReferenceExpression(name, classType, classType.Reference);   // LANG-652: a class name as a value
            case TypeSymbol type:
                _diagnostics.Report(name, "E2009", type.Name);
                return Error(name);
            case IntrinsicSymbol { Intrinsic: IntrinsicKind.Eof or IntrinsicKind.Eoln } textFunction:
                return new BoundIntrinsicCallExpression(name, textFunction, [], _types.Boolean, constantValue: null);    // of Input (C-066)
            case IntrinsicSymbol { IsMachineFunction: true } machine:
                return MachineFunction(name, machine);
            case IntrinsicSymbol { IsFunction: true } function:
                _diagnostics.Report(name, "E2110", function.Name);
                return Error(name);
            case IntrinsicSymbol:
                _diagnostics.Report(name, "E2034");                     // procedure-like intrinsics have no value
                return Error(name);
            case var symbol:
                return Unsupported(name, symbol.Kind.ToString().ToLowerInvariant() + " reference");
        }
    }

    /// <summary>
    /// LANG-265 <c>@x</c>: the address (type <c>Pointer</c>) of a variable reference — variable, parameter, field, array element,
    /// dereference — or the code address of a routine (E2130 for a nested one, LANG-451; overloaded routines: M2.5).
    /// E2033 for anything else.
    /// </summary>
    private BoundExpression BindAddressOf(AddressOfExpressionSyntax addressOf) => AddressOf(addressOf, addressOf.Operand);

    /// <summary><c>@operand</c> / <c>Addr(operand)</c> bound at <paramref name="syntax"/>.</summary>
    private BoundExpression AddressOf(SyntaxNode syntax, ExpressionSyntax operandSyntax)
    {
        if (_scope.Unqualified(Unparenthesized(operandSyntax)) is IdentifierNameSyntax { Identifier: { IsMissing: false } token } name
            && _scope.For(name).LookupCandidates(token.Value as string ?? token.Text) is [var routine, ..])
        {
            if (routine.IsNested)
            {
                _diagnostics.Report(name, "E2130", routine.Name);
                return Error(syntax);
            }

            CheckHintDirectives(routine, name);                                 // LANG-472 (C-232): @Routine
            return new BoundAddressOfExpression(syntax, operand: null, routine, _types.Pointer);
        }

        if (_scope.Unqualified(Unparenthesized(operandSyntax)) is IdentifierNameSyntax { Identifier: { IsMissing: false } intrinsicToken } intrinsicName
            && _scope.For(intrinsicName).Lookup(intrinsicToken.Value as string ?? intrinsicToken.Text) is IntrinsicSymbol intrinsic)
        {
            _diagnostics.Report(intrinsicName, "E2910", intrinsic.Name);     // intrinsics.md §0 (C-067): intrinsics have no address
            return Error(syntax);
        }

        if (Unparenthesized(operandSyntax) is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier: { IsMissing: false } memberToken } } memberAccess
            && Unparenthesized(memberAccess.Expression) is IdentifierNameSyntax { Identifier: { IsMissing: false } receiverToken } receiverName
            && _scope.For(receiverName).Lookup(receiverToken.Value as string ?? receiverToken.Text) switch
            {
                VariableSymbol v => v.Type,
                ParameterSymbol p => p.Type,
                FieldSymbol f => f.Type,
                _ => null,
            } is InterfaceTypeSymbol { IsDispatchInterface: true } dispatch
            && dispatch.Methods.Any(m => IdentifierComparer.Instance.Equals(m.Name, memberToken.Value as string ?? memberToken.Text)))
        {
            _diagnostics.Report(syntax, "E2660", "@");                      // com-interfaces.md LANG-697: a dispatch member has no address
            return Error(syntax);
        }

        var operand = BindOperand(operandSyntax);                    // @p of a procedural variable: its address (LANG-482)
        if (operand.HasErrors)
        {
            return Error(syntax, operand);
        }

        if (!IsAddressable(operand) || IsPropertyRead(operand))
        {
            _diagnostics.Report(operandSyntax, "E2033");                    // also @Prop (LANG-645)
            return Error(syntax, operand);
        }

        switch (BoundIndexExpression.RootOf(operand))
        {
            case BoundVariableExpression { Variable: var v } when ForVariables.Contains(v):
            case BoundParameterExpression { Parameter: var p } when ForVariables.Contains(p):
                _diagnostics.Report(operandSyntax, "E2053");                    // LANG-361 rule 6, LANG-576 (C-386): @v of a loop variable
                return Error(syntax, operand);
        }

        if (BoundIndexExpression.RootOf(operand) is BoundVariableExpression { Variable: var variable })
        {
            AddressTakenVariables.Add(variable);
        }

        return new BoundAddressOfExpression(syntax, operand, routine: null, _types.Pointer);
    }

    /// <summary>A variable reference whose storage has an address (constants and values have none; typed constants do).</summary>
    internal static bool IsAddressable(BoundExpression expression) =>
        BoundIndexExpression.RootOf(expression) is BoundVariableExpression or BoundParameterExpression or BoundDereferenceExpression or BoundFieldAccessExpression;

    /// <summary>
    /// LANG-265 <c>p^</c>: the variable of the pointed type; E2018 when <c>p</c> is no typed pointer. <paramref name="untyped"/>
    /// (an untyped <c>var</c>/<c>const</c> argument such as <c>FillChar(P^, …)</c>) also accepts the untyped <c>Pointer</c>,
    /// giving a byte-typed variable. <paramref name="typecast"/> (the operand of <c>T(p^)</c>, C-257) accepts it as an untyped
    /// variable, which the cast then reinterprets like an untyped parameter.
    /// </summary>
    internal BoundExpression BindDereference(DereferenceExpressionSyntax syntax, bool untyped, bool typecast = false)
    {
        var pointer = BindExpression(syntax.Expression);
        if (pointer.HasErrors)
        {
            return Error(syntax, pointer);
        }

        return pointer.Type switch
        {
            PointerTypeSymbol { PointedType: { } target } when pointer.Type != _types.Nil => new BoundDereferenceExpression(syntax, pointer, target),
            PointerTypeSymbol when untyped && pointer.Type != _types.Nil => new BoundDereferenceExpression(syntax, pointer, _types.Byte),
            PointerTypeSymbol when typecast && pointer.Type != _types.Nil => new BoundDereferenceExpression(syntax, pointer, _types.Untyped),
            _ => ReportPointerRequired(syntax, pointer),
        };
    }

    private BoundErrorExpression ReportPointerRequired(SyntaxNode syntax, BoundExpression operand)
    {
        _diagnostics.Report(syntax, "E2018");
        return Error(syntax, operand);
    }

    /// <summary>
    /// An argument for an untyped <c>var</c>/<c>const</c> parameter (<c>FillChar</c>, <c>Move</c>): a variable reference of any
    /// type, also <c>p^</c> of an untyped pointer; E2033 when it has no address.
    /// </summary>
    internal BoundExpression BindUntypedArgument(ExpressionSyntax syntax)
    {
        var operand = Unparenthesized(syntax) is DereferenceExpressionSyntax dereference ? BindDereference(dereference, untyped: true) : BindExpression(syntax);
        if (!operand.HasErrors && !IsAddressable(operand))
        {
            _diagnostics.Report(syntax, "E2033");
            return Error(syntax, operand);
        }

        return operand;
    }

    /// <summary>Pointer arithmetic is allowed for <c>PChar</c>/<c>PAnsiChar</c> and under <c>{$POINTERMATH ON}</c> at <paramref name="at"/> (LANG-247).</summary>
    private static bool AllowsPointerMath(TypeSymbol type, SyntaxToken at) =>
        type is PointerTypeSymbol pointer && (pointer.AllowsPointerMath || at.DirectiveState.PointerMath);

    /// <summary>
    /// LANG-255/258 <c>a[i, j]</c> = <c>a[i][j]</c> on static arrays: each index an ordinal compatible with the index type
    /// (E2010 at the index; it keeps its own type), a constant index inside the bounds (E2032 at the index). E2017 at the
    /// indexed expression when it is no array, E2019 at the first extra index; string indexing arrives with C-061.
    /// </summary>
    private BoundExpression BindElementAccess(ElementAccessExpressionSyntax syntax)
    {
        // an array property named by syntax.Expression takes the first indexes (LANG-642)
        var saved = (_indexedBy, _indexesTaken);
        (_indexedBy, _indexesTaken) = (syntax, 0);
        var result = BindExpression(syntax.Expression);
        var taken = _indexesTaken;
        (_indexedBy, _indexesTaken) = saved;
        if (result.HasErrors)
        {
            return Error(syntax, result);
        }

        var first = true;
        for (var i = taken; i < syntax.Indexes.Count; i++)
        {
            var indexSyntax = syntax.Indexes[i];
            if (((result.Type as ClassTypeSymbol)?.DefaultProperty ?? (result.Type as InterfaceTypeSymbol)?.DefaultProperty) is { } defaultProperty)
            {
                // LANG-642: Obj[i] = Obj.Items[i]
                if (i + defaultProperty.Parameters.Length > syntax.Indexes.Count)
                {
                    _diagnostics.Report(syntax, "E2110", defaultProperty.Name);
                    return Error(syntax, result);
                }

                result = DefaultPropertyAccess(syntax, i, defaultProperty, result);
                if (result.HasErrors)
                {
                    return result;
                }

                i += defaultProperty.Parameters.Length - 1;
                first = true;
                continue;
            }

            if (result.Type is PointerTypeSymbol { PointedType: ArrayTypeSymbol pointedArray } && !AllowsPointerMath(result.Type, syntax.FirstToken()))
            {
                result = new BoundDereferenceExpression(syntax.Expression, result, pointedArray);   // p[i] = p^[i] (extended syntax)
            }
            else if (result.Type is PointerTypeSymbol pointer && AllowsPointerMath(pointer, syntax.FirstToken()) && pointer != _types.Nil)
            {
                // LANG-247: p[i] = (p + i)^, 0-based
                first = false;
                var offset = BindExpression(indexSyntax);
                if (offset.HasErrors)
                {
                    return Error(syntax, result, offset);
                }

                if (offset.Type is not IntegerTypeSymbol || pointer.PointedType is null)
                {
                    if (pointer.PointedType is null)
                    {
                        _diagnostics.Report(syntax.Expression, "E2018");
                    }
                    else
                    {
                        _conversions.ReportIncompatible(offset.Type, _types.NativeInt, indexSyntax, _diagnostics);
                    }

                    return Error(syntax, result, offset);
                }

                var element = new BoundPointerArithmeticExpression(syntax, result, Convert(offset, _types.NativeInt), subtract: false, difference: false, pointer);
                result = new BoundDereferenceExpression(syntax, element, pointer.PointedType);
                continue;
            }

            if (first && result.Type is StringTypeSymbol indexed)
            {
                // LANG-241: s[i] is a Char variable when s is one, 1-based; the index is an integer (E2010 at it). Byte strings
                // give AnsiChar; a ShortString is indexed 0..capacity, s[0] being its length byte (C-063)
                first = false;
                var position = BindExpression(indexSyntax);
                if (position.HasErrors)
                {
                    return Error(syntax, result, position);
                }

                if (position.Type is not IntegerTypeSymbol)
                {
                    _conversions.ReportIncompatible(position.Type, _types.Integer, indexSyntax, _diagnostics);
                    return Error(syntax, result, position);
                }

                result = new BoundIndexExpression(syntax, result, position, indexed.Variant == StringVariant.Unicode ? _types.Char : _types.AnsiChar);
                continue;
            }

            if (result.Type is DynamicArrayTypeSymbol or OpenArrayTypeSymbol)
            {
                // LANG-257 / LANG-431: a[i] is an element variable, 0-based; the index is an integer (E2010 at it), C-064, C-106
                var elementType = OpenArrayTypeSymbol.ElementOf(result.Type)!;
                first = false;
                var position = BindExpression(indexSyntax);
                if (position.HasErrors)
                {
                    return Error(syntax, result, position);
                }

                if (position.Type is not IntegerTypeSymbol)
                {
                    _conversions.ReportIncompatible(position.Type, _types.Integer, indexSyntax, _diagnostics);
                    return Error(syntax, result, position);
                }

                result = new BoundIndexExpression(syntax, result, position, elementType);
                continue;
            }

            if (result.Type is not ArrayTypeSymbol array)
            {
                if (!first)
                {
                    _diagnostics.Report(indexSyntax, "E2019", syntax.Expression.ToString().Trim());
                }
                else
                {
                    _diagnostics.Report(syntax.Expression, "E2017");
                }

                return Error(syntax, result);
            }

            first = false;
            var index = BindExpression(indexSyntax);
            if (index.HasErrors)
            {
                return Error(syntax, result, index);
            }

            if (!index.Type.IsOrdinal || !_conversions.Classify(index.Type, array.IndexType).Exists)
            {
                _conversions.ReportIncompatible(index.Type, array.IndexType, indexSyntax, _diagnostics);
                return Error(syntax, result, index);
            }

            if (index.ConstantValue is { } constant)
            {
                var ordinal = constant.Value is bool b ? (b ? BigInteger.One : BigInteger.Zero) : (BigInteger)constant.Value!;
                if (ordinal < array.Low || ordinal > array.High)
                {
                    _diagnostics.Report(indexSyntax, "E2032", OrdinalText(ordinal, constant.Type),
                        OrdinalText(array.Low, array.IndexType), OrdinalText(array.High, array.IndexType));
                    return Error(syntax, result, index);
                }
            }

            result = new BoundIndexExpression(syntax, result, index, array.ElementType);
        }

        return result;
    }

    /// <summary>
    /// LANG-260 <c>r.F</c>: E2005 (record type, field name) at the name when the record has no such field, E2006 (the
    /// qualifier's source text) at the qualifier when it is no record. Unit-qualified names arrive with units (LANG-311).
    /// </summary>
    private BoundExpression BindMemberAccess(MemberAccessExpressionSyntax syntax)
    {
        switch (NamedType(syntax))
        {
            case ErrorTypeSymbol:
                return Error(syntax);
            case ClassTypeSymbol classType:
                return new BoundClassReferenceExpression(syntax, classType, classType.Reference);  // LANG-652, 784 (C-345): TList<Integer>.TEnumerator
            case { } type:
                _diagnostics.Report(syntax, "E2009", type.Name);                // a type is not a value
                return Error(syntax);
        }

        var value = BindMemberCall(syntax, syntax, argumentList: null, out var call);
        return call is { } method ? MethodCallExpression(syntax, method) : value!;
    }

    /// <summary>The record (or <c>^record</c>) part of <see cref="BindMemberAccess"/>, the qualifier already bound.</summary>
    private BoundExpression RecordMember(MemberAccessExpressionSyntax syntax, BoundExpression record)
    {
        if (record.Type is PointerTypeSymbol { PointedType: RecordTypeSymbol pointed })
        {
            record = new BoundDereferenceExpression(syntax.Expression, record, pointed);       // p.F = p^.F (extended syntax)
        }

        if (record.Type is not RecordTypeSymbol recordType || syntax.Name is not IdentifierNameSyntax)
        {
            _diagnostics.Report(syntax.Expression, "E2006", syntax.Expression.ToString().Trim());
            return Error(syntax, record);
        }

        var name = syntax.Name.Identifier.Value as string ?? syntax.Name.Identifier.Text;
        if (recordType.GetMembers(name).FirstOrDefault() is not FieldSymbol field)
        {
            _diagnostics.Report(syntax.Name, "E2005", recordType.Name, name);
            return Error(syntax, record);
        }

        return new BoundFieldAccessExpression(syntax, record, field);
    }

    /// <summary>An ordinal as written in source: <c>'a'</c>, <c>True</c>, <c>1</c>, an enumeration element by name (<c>Red</c>).</summary>
    private static string OrdinalText(BigInteger value, TypeSymbol type) => type switch
    {
        EnumTypeSymbol e when e.EnumMembers.FirstOrDefault(m => m.Ordinal == value) is { } member => member.Name,
        BooleanTypeSymbol => new ConstantValue(!value.IsZero, type).ToDisplayString(),
        _ => new ConstantValue(value, type).ToDisplayString(),
    };

    /// <summary>The symbol a name binds to (LANG-310); E2003 and null when undeclared, null for a parser-inserted name.</summary>
    private Symbol? LookupName(IdentifierNameSyntax name)
    {
        var token = name.Identifier;
        if (token.IsMissing)
        {
            return null;
        }

        var text = token.Value as string ?? token.Text;
        var symbol = _scope.For(name).Lookup(text);
        if (symbol is null && _routine is { ReturnType: null } && IdentifierComparer.Instance.Equals(text, "Result"))
        {
            _diagnostics.Report(name, "E2132");
        }
        else if (symbol is null && CurrentMethod is null && IdentifierComparer.Instance.Equals(text, "Self"))
        {
            _diagnostics.Report(name, "E2134");                                   // LANG-621
        }
        else if (symbol is null && DeclarationBinder.SmallestGenericArity(_scope.For(name), text) is { } arity)
        {
            _diagnostics.Report(name, "E2703", text, arity);                    // LANG-781 (C-345): TBox.Create where only TBox<T> exists
        }
        else if (symbol is null)
        {
            _diagnostics.ReportUndeclared(name, text);
        }
        else if (EnclosingInstanceMember(symbol) is { } enclosing && (_withs.Count == 0 || WithOf(text, symbol) is null))
        {
            _diagnostics.Report(name, "E2707", symbol.Name, TypeMembers.DisplayName(enclosing));   // LANG-773 (C-344)
            return null;
        }
        else
        {
            HintChecks.CheckName(_diagnostics, _types, _scope, name, text, symbol);                // LANG-472 (C-232)
        }

        return symbol;
    }

    /// <summary>
    /// LANG-773 (C-344): an instance member (field, method, property) of a type enclosing the current method's type, found by simple
    /// name — it has no object there. Returns that enclosing type; null for anything else.
    /// </summary>
    private TypeSymbol? EnclosingInstanceMember(Symbol symbol)
    {
        var isInstanceMember = symbol switch
        {
            FieldSymbol => true,
            MethodSymbol method => !method.IsClassMethod && method.ContainingInterface is null,
            PropertySymbol property => !property.IsClassProperty,
            _ => false,
        };
        if (!isInstanceMember || CurrentMethod?.Owner is not { } current || symbol.ContainingSymbol is not TypeSymbol owner
            || TypeMembers.SelfAndAncestors(current).Contains(owner))
        {
            return null;
        }

        return TypeMembers.EnclosingTypes(current).FirstOrDefault(e => TypeMembers.SelfAndAncestors(e).Contains(owner));
    }

    /// <summary>
    /// LANG-472 (C-127, C-232): a use outside its own unit of a symbol declared <c>deprecated</c> / <c>platform</c> /
    /// <c>experimental</c> — a call of a routine or method, a routine or method taken as a procedural value (<c>P := R</c>,
    /// <c>@R</c>), a variable or constant named — is W1050 / W1051 / W1052 at the symbol's name in <paramref name="at"/>.
    /// </summary>
    private void CheckHintDirectives(Symbol symbol, SyntaxNode at)
    {
        if (symbol.Hints is { } hints && !ReferenceEquals(symbol.DeclaringUnit, CurrentUnit))
        {
            _diagnostics.ReportHints(HintChecks.NameIn(at), symbol.Name, hints);
        }
    }

    /// <summary>
    /// <paramref name="syntax"/> where a variable is required (an assignment target, a <c>var</c> argument, the operand of
    /// <c>Inc</c>/<c>Dec</c>/<c>Include</c>/<c>Exclude</c>): there a cast <c>T(v)</c> of a variable is a variable typecast even when
    /// a value cast would apply (LANG-290 last row; E2041 for managed types and different sizes).
    /// </summary>
    public BoundExpression BindVariable(ExpressionSyntax syntax) =>
        Unparenthesized(syntax) is InvocationExpressionSyntax invocation ? BindInvocation(invocation, variableContext: true) : BindOperand(syntax);

    /// <summary>A typecast <c>T(x)</c> or a function call; intrinsics are bound from C-029 on.</summary>
    private BoundExpression BindInvocation(InvocationExpressionSyntax invocation, bool variableContext = false)
    {
        if (ImplicitGenericMethod(invocation, invocation.Expression, invocation.ArgumentList) is { } genericCall)
        {
            return MethodCallExpression(invocation, genericCall);                          // C-222: Max<Integer>(…) inside the class
        }

        if (_scope.Unqualified(invocation.Expression) is not IdentifierNameSyntax name)
        {
            switch (Unparenthesized(invocation.Expression))
            {
                case MemberAccessExpressionSyntax or GenericNameSyntax when TypeNamedBy(invocation.Expression) is { } namedTarget:
                    // C-311: TOuter.TInner(x) (LANG-770); LANG-785 (C-346): TBox<Integer>(o), U.TPair<Byte, Byte>(w) are typecasts;
                    // LANG-774 (C-357): E2610 for a nested type this code may not access
                    return BindTypecastInvocation(invocation, namedTarget, variableContext);
                case MemberAccessExpressionSyntax member when !(_scope.Unqualified(member.Expression) is IdentifierNameSyntax { Identifier: { IsMissing: false } q } qualifier
                                                                && _scope.For(qualifier).Lookup(q.Value as string ?? q.Text) is EnumTypeSymbol):
                {
                    // o.M(x), TFoo.Create(x) (CMP-367); r.F(x) through a procedural field (LANG-484)
                    var value = BindMemberCall(member, invocation, invocation.ArgumentList, out var call);
                    return call is { } method ? MethodCallExpression(invocation, method)
                        : value is BoundArrayConstructor ? value                            // LANG-271 (C-388): D.Create(…) is a value
                        : CallThrough(invocation, value!);
                }

                case InheritedExpressionSyntax inherited:
                    return MethodCallExpression(invocation, BindInherited(invocation, inherited, invocation.ArgumentList, bare: false));
            }

            return CallThrough(invocation, BindOperand(invocation.Expression));        // A[i](x), F(x)(y) (LANG-484)
        }

        var symbol = LookupName(name);
        if (symbol is null)
        {
            return Error(invocation);
        }

        if (symbol is MethodSymbol)
        {
            return ImplicitMethodCall(invocation, name, invocation.ArgumentList);          // also a with designator's method (C-109)
        }

        if (symbol is FieldSymbol)
        {
            // a procedural field of a with designator, or of Self inside a method (C-225)
            return CallThrough(invocation, BindOperand(invocation.Expression));
        }

        if (symbol is VariableSymbol or ParameterSymbol or ConstantSymbol or PropertySymbol)
        {
            return CallThrough(invocation, BindOperand(invocation.Expression));        // p(x) (LANG-484)
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (symbol is IntrinsicSymbol { IsFunction: true } function)
        {
            return BindIntrinsicFunction(invocation, function);
        }

        if (symbol is IntrinsicSymbol)
        {
            _diagnostics.Report(invocation, "E2034");
            return Error(invocation);
        }

        if (symbol is RoutineSymbol)
        {
            return CallExpression(invocation, name, invocation.ArgumentList);
        }

        if (symbol is not TypeSymbol target)
        {
            return Unsupported(invocation, "routine call");
        }

        return BindTypecastInvocation(invocation, target, variableContext);
    }

    /// <summary>A typecast <c>T(x)</c> once the callee is known to name the type <paramref name="target"/> (LANG-290).</summary>
    private BoundExpression BindTypecastInvocation(InvocationExpressionSyntax invocation, TypeSymbol target, bool variableContext)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1)
        {
            return Unsupported(invocation, "typecast");
        }

        if (arguments[0].WidthColonToken is { } colon)
        {
            _diagnostics.Report(colon, "E2915");
            return Error(invocation);
        }

        // LANG-272 (C-388): D([…]) builds the constructor in D's context (as D.Create(…)), S([…]) the set constructor of S
        if (Unparenthesized(arguments[0].Expression) is SetConstructorSyntax constructor && target.TypeKind != TypeKind.Error)
        {
            switch (target)
            {
                case DynamicArrayTypeSymbol array when constructor.Elements.All(e => e.UpperBound is null):
                    return BindArrayConstructor(constructor, array, array.ElementType);
                // a constructor that does not fit S element-wise keeps the former value cast (same size only), so no cast changes meaning
                case SetTypeSymbol { ElementType: not null } set when ProbeBinder(new DiagnosticBag()) is var probe
                                                                    && !probe.BindSetConstructor(constructor, set).HasErrors && probe._diagnostics.Count == 0:
                    return BindSetConstructor(constructor, set);
            }
        }

        // the operand of a cast to a non-procedural type is a value (LANG-482); Pointer(p) / NativeInt(p) cast the procedural value itself
        // C-257: T(p^) with p an untyped Pointer is an untyped variable at p, cast like an untyped parameter (C-202)
        var asOperand = target is ProceduralTypeSymbol or PointerTypeSymbol || target is IntegerTypeSymbol { Bits: 64 };
        var operand = Unparenthesized(arguments[0].Expression) is DereferenceExpressionSyntax dereference
            ? BindDereference(dereference, untyped: false, typecast: true)
            : BindOperand(arguments[0].Expression);
        operand = asOperand ? operand : Called(operand);
        return operand.HasErrors || target.TypeKind == TypeKind.Error ? Error(invocation, operand) : BindTypecast(invocation, target, operand, variableContext);
    }

    /// <summary>
    /// A function-like intrinsic (intrinsics.md §1); folded when its argument is constant (LANG-340), and always for
    /// <c>Low</c>/<c>High</c>/<c>SizeOf</c> except <c>High</c> of a string variable (its length: M2.2).
    /// </summary>
    private BoundExpression BindIntrinsicFunction(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic)
    {
        if (intrinsic.Intrinsic is IntrinsicKind.Assigned or IntrinsicKind.Addr or IntrinsicKind.Ptr)
        {
            return BindPointerFunction(invocation, intrinsic);
        }

        if (intrinsic.IsAtomic)
        {
            return BindAtomic(invocation, intrinsic);
        }

        if (intrinsic.IsStringFunction)
        {
            return BindStringFunction(invocation, intrinsic);
        }

        if (intrinsic.Intrinsic is IntrinsicKind.Eof or IntrinsicKind.Eoln)
        {
            return BindTextFunction(invocation, intrinsic);
        }

        if (intrinsic.Intrinsic == IntrinsicKind.Slice)
        {
            return BindSlice(invocation, intrinsic);
        }

        if (intrinsic.IsMachineFunction)
        {
            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                _diagnostics.Report(invocation.ArgumentList.Arguments[0], "E2111", intrinsic.Name);
                return Error(invocation);
            }

            return MachineFunction(invocation, intrinsic);
        }

        if (!_folder.CheckSingleArgument(invocation, intrinsic))
        {
            return Error(invocation);
        }

        var syntax = invocation.ArgumentList.Arguments[0].Expression;
        if (intrinsic.Intrinsic == IntrinsicKind.TypeInfo)
        {
            return BindTypeInfo(invocation, intrinsic, syntax);
        }

        if (intrinsic.Intrinsic == IntrinsicKind.Default)
        {
            return BindDefault(invocation, syntax);
        }

        if (intrinsic.IsTypeQuery)
        {
            return BindTypeQuery(invocation, intrinsic, syntax);
        }

        if (intrinsic.Intrinsic is IntrinsicKind.Low or IntrinsicKind.High or IntrinsicKind.SizeOf or IntrinsicKind.Length)
        {
            // the operand is never evaluated (LANG-903), so it captures nothing (lead decision C-029)
            var captured = CapturedVariables.Count;
            var (type, argument) = BindUnevaluated(syntax, out var references);
            var operandCaptures = CapturedVariables.GetRange(captured, CapturedVariables.Count - captured);
            CapturedVariables.RemoveRange(captured, CapturedVariables.Count - captured);
            if (type.TypeKind == TypeKind.Error)
            {
                return Error(invocation);
            }

            var value = _folder.FoldTypeIntrinsic(invocation, intrinsic, type, argument?.ConstantValue, argument is null, out var needsRuntime);
            if (needsRuntime && type is StringTypeSymbol or DynamicArrayTypeSymbol or OpenArrayTypeSymbol)
            {
                // Length/High of a string value: its length at run time (ABI-023, C-060; byte strings in bytes, C-063); the
                // operand is evaluated
                CapturedVariables.AddRange(operandCaptures.Where(v => !CapturedVariables.Contains(v)));
                CheckDeferredCaptures(references);                                 // LANG-970 (C-413): only now a reference
                return new BoundIntrinsicCallExpression(invocation, intrinsic, [argument!], _types.Integer, constantValue: null);
            }

            return value is null
                ? Error(invocation)
                : new BoundIntrinsicCallExpression(invocation, intrinsic, argument is null ? [] : [argument], value.Type, value);
        }

        var operand = BindExpression(syntax);
        if (operand.HasErrors || _folder.IntrinsicResultType(intrinsic, operand.Type, syntax) is not { } resultType)
        {
            return Error(invocation, operand);
        }

        if (intrinsic.Intrinsic is IntrinsicKind.Trunc or IntrinsicKind.Round or IntrinsicKind.Int or IntrinsicKind.Frac or IntrinsicKind.Sqrt)
        {
            operand = Convert(operand, _types.Double);                      // (X: Double), C-059
        }

        // LANG-340: Int, Frac and Sqrt are not folded (not constant expressions)
        if (operand.ConstantValue is { } constant && intrinsic.Intrinsic is not (IntrinsicKind.Int or IntrinsicKind.Frac or IntrinsicKind.Sqrt))
        {
            return _folder.FoldIntrinsic(invocation, intrinsic, constant, resultType) is { } folded
                ? new BoundIntrinsicCallExpression(invocation, intrinsic, [operand], folded.Type, folded)
                : Error(invocation, operand);
        }

        if (intrinsic.Intrinsic is IntrinsicKind.Abs or IntrinsicKind.Sqr && resultType is IntegerTypeSymbol)
        {
            operand = Convert(operand, resultType);
        }

        return new BoundIntrinsicCallExpression(invocation, intrinsic, [operand], resultType, constantValue: null);
    }

    /// <summary>
    /// LANG-431 (C-106): <c>Slice(A, N)</c> = the first <c>N</c> elements of a static, dynamic or open array <c>A</c>, a value of the
    /// open array type over its element type — so it is accepted only by an open array parameter (E2010 elsewhere). E2017 when
    /// <c>A</c> is no array; <c>N</c> converts to <c>Integer</c>. <c>N</c> is not checked against the length.
    /// </summary>
    private BoundExpression BindSlice(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 2)
        {
            if (arguments.Count < 2)
            {
                _diagnostics.Report(invocation, "E2110", intrinsic.Name);
            }
            else
            {
                _diagnostics.Report(arguments[2], "E2111", intrinsic.Name);
            }

            return Error(invocation);
        }

        var array = BindExpression(arguments[0].Expression);
        var count = BindExpression(arguments[1].Expression);
        if (array.HasErrors || count.HasErrors)
        {
            return Error(invocation, array, count);
        }

        if (OpenArrayTypeSymbol.ElementOf(array.Type) is not { } element)
        {
            _diagnostics.Report(arguments[0].Expression, "E2017");
            return Error(invocation, array, count);
        }

        count = ConvertForAssignment(count, _types.Integer, arguments[1].Expression);
        if (array.Type is ArrayTypeSymbol @static && count.ConstantValue?.Value is BigInteger n && (n < 0 || n > @static.Length))
        {
            // C-198: a constant count outside 0..Length of a static array
            _diagnostics.Report(arguments[1].Expression, "E2032", n.ToString(CultureInfo.InvariantCulture), "0", @static.Length.ToString(CultureInfo.InvariantCulture));
            return Error(invocation, array, count);
        }

        var type = array.Type is OpenArrayTypeSymbol { IsArrayOfConst: true } ofConst ? ofConst : _types.OpenArrayOf(element);   // C-107
        return count.HasErrors
            ? Error(invocation, array, count)
            : new BoundIntrinsicCallExpression(invocation, intrinsic, [array, count], type, constantValue: null);
    }

    /// <summary>
    /// LANG-946 (C-066): <c>Eof</c>/<c>Eoln[(var F: Text)]: Boolean</c> — no argument means <c>Input</c>; the argument is an
    /// assignable <c>Text</c> variable (E2111 for more arguments, E2010 for another type).
    /// </summary>
    /// <summary>C-173 / C-174: <c>_ReturnAddress: Pointer</c> (ABI-172) and <c>_GetMXCSR: Cardinal</c> (ABI-105), never constant.</summary>
    private BoundIntrinsicCallExpression MachineFunction(SyntaxNode syntax, IntrinsicSymbol intrinsic) =>
        new(syntax, intrinsic, [], intrinsic.Intrinsic == IntrinsicKind.ReturnAddress ? _types.Pointer : _types.Cardinal, constantValue: null);

    private BoundExpression BindTextFunction(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count > 1)
        {
            _diagnostics.Report(arguments[1], "E2111", intrinsic.Name);
            return Error(invocation);
        }

        if (arguments.Count == 0)
        {
            return new BoundIntrinsicCallExpression(invocation, intrinsic, [], _types.Boolean, constantValue: null);
        }

        var syntax = arguments[0].Expression;
        var file = BindVariable(syntax);
        if (file.HasErrors)
        {
            return Error(invocation, file);
        }

        if (file.Type is not TextTypeSymbol)
        {
            _diagnostics.Report(syntax, "E2010", file.Type.Name, _types.Text.Name);
            return Error(invocation, file);
        }

        return CheckVariableReference(file, syntax)
            ? new BoundIntrinsicCallExpression(invocation, intrinsic, [file], _types.Boolean, constantValue: null)
            : Error(invocation, file);
    }

    /// <summary>
    /// intrinsics.md: <c>Addr(x)</c> = <c>@x</c>; <c>Assigned(p)</c> = <c>p &lt;&gt; nil</c> for a pointer (procedural types: C-161);
    /// <c>Ptr(n)</c> turns an integer into a <c>Pointer</c>. One argument (E2110 / E2111), E2010 for a wrong type.
    /// </summary>
    private BoundExpression BindPointerFunction(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1)
        {
            _diagnostics.Report(arguments.Count == 0 ? invocation : arguments[1], arguments.Count == 0 ? "E2110" : "E2111", intrinsic.Name);
            return Error(invocation);
        }

        var syntax = arguments[0].Expression;
        if (intrinsic.Intrinsic == IntrinsicKind.Addr)
        {
            return AddressOf(invocation, syntax);
        }

        var operand = intrinsic.Intrinsic == IntrinsicKind.Assigned ? BindOperand(syntax) : BindExpression(syntax);     // Assigned(p) never calls (LANG-482)
        if (operand.HasErrors)
        {
            return Error(invocation, operand);
        }

        if (intrinsic.Intrinsic == IntrinsicKind.Assigned)
        {
            if (operand.Type is DynamicArrayTypeSymbol or ProceduralTypeSymbol or MethodPointerTypeSymbol or InterfaceTypeSymbol)
            {
                // C-120: an interface (a method reference, LANG-724) is assigned when it is not nil
                // a method pointer is assigned when its Code is not nil (LANG-481, C-103)
                // intrinsics.md §1 (C-067): a dynamic array is assigned when its block reference is not nil (LANG-257)
                var reference = new BoundConversionExpression(syntax, operand, _types.Pointer, ConversionKind.Explicit, isExplicit: true, constantValue: null);
                return new BoundIntrinsicCallExpression(invocation, intrinsic, [reference], _types.Boolean, constantValue: null);
            }

            if (operand.Type is not PointerTypeSymbol)
            {
                _diagnostics.Report(syntax, "E2010", operand.Type.Name, _types.Pointer.Name);
                return Error(invocation, operand);
            }

            return new BoundIntrinsicCallExpression(invocation, intrinsic, [Convert(operand, _types.Pointer)], _types.Boolean, constantValue: null);
        }

        if (operand.Type is not IntegerTypeSymbol)
        {
            _diagnostics.Report(syntax, "E2010", operand.Type.Name, _types.NativeUInt.Name);
            return Error(invocation, operand);
        }

        return new BoundIntrinsicCallExpression(invocation, intrinsic, [Convert(operand, _types.NativeUInt)], _types.Pointer, constantValue: null);
    }

    /// <summary>
    /// ABI-172 / intrinsics.md (C-168): <c>AtomicIncrement</c>/<c>AtomicDecrement(var Target[; N])</c>,
    /// <c>AtomicExchange(var Target; Value)</c>, <c>AtomicCmpExchange(var Target; NewValue, Comparand)</c>. <c>Target</c> is an
    /// assignable variable of a 32-/64-bit integer type or a pointer type (E2010 otherwise); <c>N</c> is any integer expression
    /// (kept unconverted; a typed pointer moves by <c>N</c> elements like <c>Inc</c>); the values are converted to the target's
    /// type. The result has the target's type. Arguments: target first, then in source order.
    /// </summary>
    private BoundExpression BindAtomic(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic)
    {
        var arguments = invocation.ArgumentList.Arguments;
        var (min, max) = intrinsic.Intrinsic switch
        {
            IntrinsicKind.AtomicIncrement or IntrinsicKind.AtomicDecrement => (1, 2),
            IntrinsicKind.AtomicExchange => (2, 2),
            _ => (3, 3),
        };
        if (arguments.Count < min)
        {
            _diagnostics.Report(invocation, "E2110", intrinsic.Name);
            return Error(invocation);
        }

        if (arguments.Count > max)
        {
            _diagnostics.Report(arguments[max], "E2111", intrinsic.Name);
            return Error(invocation);
        }

        foreach (var argument in arguments)
        {
            if (argument.WidthColonToken is { } colon)
            {
                _diagnostics.Report(colon, "E2915");
                return Error(invocation);
            }
        }

        var syntax = arguments[0].Expression;
        var target = BindVariable(syntax);
        if (target.HasErrors || !CheckVariableReference(target, syntax))
        {
            return Error(invocation, target);
        }

        if (target.Type is not (IntegerTypeSymbol { Size: 4 or 8 } or PointerTypeSymbol))
        {
            _diagnostics.Report(syntax, "E2010", target.Type.Name, _types.Integer.Name);
            return Error(invocation, target);
        }

        if (AccessAlignment(target) < target.Type.Alignment)
        {
            _diagnostics.Report(syntax, "E2042");                       // C-067/C-181: no lock-free access (CMP-612)
            return Error(invocation, target);
        }

        var operands = ImmutableArray.CreateBuilder<BoundExpression>(arguments.Count);
        operands.Add(target);
        var counted = intrinsic.Intrinsic is IntrinsicKind.AtomicIncrement or IntrinsicKind.AtomicDecrement;
        for (var k = 1; k < arguments.Count; k++)
        {
            var valueSyntax = arguments[k].Expression;
            var value = counted ? BindExpression(valueSyntax) : BindExpression(valueSyntax, target.Type);
            if (value.HasErrors)
            {
                return Error(invocation, [.. operands, value]);
            }

            if (counted && value.Type is not IntegerTypeSymbol)
            {
                _diagnostics.Report(valueSyntax, "E2010", value.Type.Name, _types.Integer.Name);
                return Error(invocation, [.. operands, value]);
            }

            value = counted ? value : ConvertForAssignment(value, target.Type, valueSyntax);
            if (value.HasErrors)
            {
                return Error(invocation, [.. operands, value]);
            }

            operands.Add(value);
        }

        return new BoundIntrinsicCallExpression(invocation, intrinsic, operands.ToImmutable(), target.Type, constantValue: null);
    }

    /// <summary>
    /// The alignment an access to the variable <paramref name="variable"/> may assume — the binder's mirror of the emitter's
    /// CMP-309 rule: inside records <c>min(field alignment, packing)</c>, inside static arrays and variable typecasts the
    /// enclosing storage's alignment; anything else (variables, dereferences, dynamic-array elements) its type's alignment.
    /// </summary>
    private static int AccessAlignment(BoundExpression variable) => variable switch
    {
        BoundFieldAccessExpression { Record.Type: RecordTypeSymbol record } field =>
            Math.Min(Math.Min(field.Type.Alignment, record.Packing), AccessAlignment(field.Record)),
        BoundIndexExpression { Array.Type: ArrayTypeSymbol } index => Math.Min(index.Type.Alignment, AccessAlignment(index.Array)),
        BoundVariableTypecastExpression typecast => Math.Min(typecast.Type.Alignment, AccessAlignment(typecast.Operand)),
        _ => variable.Type.Alignment,
    };

    /// <summary>
    /// intrinsics.md §1 (C-062) on UnicodeStrings: <c>Copy(s, index[, count]): string</c>, <c>Pos(sub, s[, offset]): Integer</c>,
    /// <c>Concat(s1, …): string</c> (bound as the <c>+</c> chain it equals; one argument is that string), <c>UpCase(c: Char): Char</c>.
    /// String operands accept characters (LANG-245); integer operands convert to <c>Integer</c>. Other string kinds and dynamic
    /// arrays: E2900 (C-063, C-064); other types E2010.
    /// </summary>
    private BoundExpression BindStringFunction(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic, DynamicArrayTypeSymbol? arrayContext = null)
    {
        var arguments = invocation.ArgumentList.Arguments;
        var (min, max) = intrinsic.Intrinsic switch
        {
            IntrinsicKind.Copy => (1, 3),
            IntrinsicKind.Pos => (2, 3),
            IntrinsicKind.Concat => (1, int.MaxValue),
            _ => (1, 1),
        };
        if (arguments.Count < min)
        {
            _diagnostics.Report(invocation, "E2110", intrinsic.Name);
            return Error(invocation);
        }

        if (arguments.Count > max)
        {
            _diagnostics.Report(arguments[max], "E2111", intrinsic.Name);
            return Error(invocation);
        }

        foreach (var argument in arguments)
        {
            if (argument.WidthColonToken is { } colon)
            {
                _diagnostics.Report(colon, "E2915");
                return Error(invocation);
            }
        }

        if (intrinsic.Intrinsic == IntrinsicKind.Concat)
        {
            return BindConcat(invocation, intrinsic, arrayContext);
        }

        var operands = ImmutableArray.CreateBuilder<BoundExpression>(arguments.Count);
        if (intrinsic.Intrinsic == IntrinsicKind.Copy)
        {
            var source = BindExpression(arguments[0].Expression);
            if (source.HasErrors)
            {
                return Error(invocation, source);
            }

            if (source.Type is DynamicArrayTypeSymbol)
            {
                // intrinsics.md: Copy(a[, index[, count]]) of a dynamic array is a unique copy of the same type (C-064)
                operands.Add(source);
                for (var k = 1; k < arguments.Count; k++)
                {
                    var count = ConvertForAssignment(BindExpression(arguments[k].Expression), _types.NativeInt, arguments[k].Expression);
                    if (count.HasErrors)
                    {
                        return Error(invocation, [.. operands, count]);
                    }

                    operands.Add(count);
                }

                return new BoundIntrinsicCallExpression(invocation, intrinsic, operands.ToImmutable(), source.Type, constantValue: null);
            }

            if (arguments.Count < 2)
            {
                _diagnostics.Report(invocation, "E2110", intrinsic.Name);
                return Error(invocation, source);
            }
        }

        for (var k = 0; k < arguments.Count; k++)
        {
            var syntax = arguments[k].Expression;
            var stringOperand = intrinsic.Intrinsic switch
            {
                IntrinsicKind.Copy => k == 0,
                IntrinsicKind.Pos => k < 2,
                _ => false,
            };
            var value = BindExpression(syntax);
            value = value.HasErrors ? value
                : intrinsic.Intrinsic == IntrinsicKind.Copy && k == 0 && Strings.IsAnsi(value.Type) ? value     // _AStrCopy (C-063)
                : stringOperand ? StringOperand(value, syntax, intrinsic.Name)
                : intrinsic.Intrinsic == IntrinsicKind.UpCase ? CharOperand(value, syntax)
                : ConvertForAssignment(value, _types.Integer, syntax);
            if (value.HasErrors)
            {
                return Error(invocation, [.. operands, value]);
            }

            operands.Add(value);
        }

        switch (intrinsic.Intrinsic)
        {
            case IntrinsicKind.Copy:
                return new BoundIntrinsicCallExpression(invocation, intrinsic, operands.ToImmutable(), Strings.IsAnsi(operands[0].Type) ? operands[0].Type : _types.String,
                    constantValue: null);
            case IntrinsicKind.Pos:
                return new BoundIntrinsicCallExpression(invocation, intrinsic, operands.ToImmutable(), _types.Integer, constantValue: null);
            default:
                return new BoundIntrinsicCallExpression(invocation, intrinsic, operands.ToImmutable(), _types.Char, constantValue: null);
        }
    }

    /// <summary>
    /// <c>Concat(e1, …)</c> (intrinsics.md, LANG-279, C-391). Bracket constructors are bound last, in the dynamic-array context of the
    /// call or else of the first dynamic-array argument (LANG-275 rules 2, 3), <c>nil</c> takes the context's type. With a
    /// dynamic-array operand the call is the <c>+</c> chain of LANG-275 (E2011/E2044 as there; one argument: a
    /// <see cref="BoundIntrinsicCallExpression"/> that is a new copy, CMP-1417); otherwise it is the string function, the left-nested
    /// <c>+</c> of its UnicodeString operands (LANG-243; one argument is that string).
    /// </summary>
    private BoundExpression BindConcat(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic, DynamicArrayTypeSymbol? arrayContext)
    {
        var arguments = invocation.ArgumentList.Arguments;
        var operands = new BoundExpression?[arguments.Count];
        for (var k = 0; k < arguments.Count; k++)
        {
            if (Unparenthesized(arguments[k].Expression) is SetConstructorSyntax)
            {
                continue;
            }

            var value = BindExpression(arguments[k].Expression);
            if (value.HasErrors)
            {
                return Error(invocation, [.. operands.OfType<BoundExpression>(), value]);
            }

            operands[k] = value;
        }

        var arrayType = arrayContext ?? operands.Select(o => o?.Type).OfType<DynamicArrayTypeSymbol>().FirstOrDefault();
        for (var k = 0; k < arguments.Count; k++)
        {
            var syntax = arguments[k].Expression;
            var value = operands[k] is { } bound
                ? arrayContext is not null && bound.Type == _types.Nil ? ConvertForAssignment(bound, arrayContext, syntax) : bound
                : BindExpression(syntax, arrayType);
            if (value.HasErrors)
            {
                return Error(invocation, [.. operands.OfType<BoundExpression>(), value]);
            }

            operands[k] = value;
        }

        if (!operands.Any(o => o!.Type is DynamicArrayTypeSymbol))
        {
            BoundExpression? text = null;
            for (var k = 0; k < arguments.Count; k++)
            {
                var value = StringOperand(operands[k]!, arguments[k].Expression, intrinsic.Name);
                if (value.HasErrors)
                {
                    return Error(invocation, value);
                }

                text = text is null ? value : new BoundBinaryExpression(invocation, BoundBinaryOperatorKind.Add, text, value, _types.String, _types.String, constantValue: null);
            }

            return text!;
        }

        if (operands.Length == 1)
        {
            return new BoundIntrinsicCallExpression(invocation, intrinsic, [operands[0]!], operands[0]!.Type, constantValue: null);
        }

        var chain = operands[0]!;
        for (var k = 1; k < operands.Length && !chain.HasErrors; k++)
        {
            chain = JoinArrays(invocation, arguments[k].Expression, arguments[k - 1].Expression, arguments[k].Expression, chain, operands[k]!, "+");
        }

        return chain;
    }

    /// <summary><paramref name="syntax"/> when it is a call of the intrinsic <c>Concat</c> (looked up without diagnostics), else null.</summary>
    private (InvocationExpressionSyntax? Call, IntrinsicSymbol? Intrinsic) ConcatIntrinsicCall(ExpressionSyntax syntax) =>
        Unparenthesized(syntax) is InvocationExpressionSyntax invocation
        && _scope.Unqualified(invocation.Expression) is IdentifierNameSyntax { Identifier: { IsMissing: false } identifier } name
        && _scope.For(name).Lookup(identifier.Value as string ?? identifier.Text) is IntrinsicSymbol { Intrinsic: IntrinsicKind.Concat } concat
            ? (invocation, concat)
            : (null, null);

    /// <summary>
    /// LANG-277 (C-391): the <c>Source</c> of <c>Insert(Source, var A, Index)</c> for a dynamic array <paramref name="target"/>. The
    /// array form: <c>nil</c> (converted to the target's type), a bracket constructor whose elements all fit the element type (bound
    /// as the target's type), or a dynamic array whose element type is the target's (unconverted); otherwise the element form, the
    /// source converted to the element type (E2010).
    /// </summary>
    internal BoundExpression BindInsertSource(ExpressionSyntax syntax, DynamicArrayTypeSymbol target)
    {
        if (Unparenthesized(syntax) is SetConstructorSyntax && ProbeBinder(new DiagnosticBag()) is var probe
            && probe.BindExpression(syntax, target) is { HasErrors: false, Type: DynamicArrayTypeSymbol } && probe._diagnostics.Count == 0)
        {
            return BindExpression(syntax, target);
        }

        var value = BindExpression(syntax, target.ElementType);
        if (value.HasErrors)
        {
            return value;
        }

        if (value.Type == _types.Nil)
        {
            return ConvertForAssignment(value, target, syntax);
        }

        return IsArrayInsertSource(value.Type, target) ? value : ConvertForAssignment(value, target.ElementType, syntax);
    }

    /// <summary>LANG-277: a source of this type is inserted element-wise (the array form) into <paramref name="target"/>.</summary>
    internal static bool IsArrayInsertSource(TypeSymbol source, DynamicArrayTypeSymbol target) =>
        source is DynamicArrayTypeSymbol array && Pointers.SameType(array.ElementType, target.ElementType);

    /// <summary>
    /// A UnicodeString operand of a string intrinsic (C-062): a UnicodeString or a character (converted, LANG-245); other string
    /// kinds E2900 (C-063); anything else E2010.
    /// </summary>
    internal BoundExpression StringOperand(BoundExpression value, ExpressionSyntax syntax, string intrinsic)
    {
        if (IsStringOperand(value.Type))
        {
            return AsString(value);                                     // other string kinds and PChar convert (C-063)
        }

        _diagnostics.Report(syntax, "E2010", value.Type.Name, _types.String.Name);
        return Error(syntax, value);
    }

    /// <summary>The <c>Char</c> operand of <c>UpCase</c> (an <c>AnsiChar</c> widens, LANG-245); E2010 for anything else.</summary>
    private BoundExpression CharOperand(BoundExpression value, ExpressionSyntax syntax)
    {
        if (value.Type is CharTypeSymbol)
        {
            return ConvertForAssignment(value, _types.Char, syntax);
        }

        _diagnostics.Report(syntax, "E2010", value.Type.Name, _types.Char.Name);
        return Error(syntax, value);
    }

    /// <summary>
    /// CMP-407 (C-112): <c>TypeInfo(T): Pointer</c> — the address of the RTTI block of the type <c>T</c> (ABI-140 d); an operand that
    /// is not a type name is E2002. Never constant.
    /// </summary>
    private BoundExpression BindTypeInfo(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic, ExpressionSyntax syntax)
    {
        var captured = CapturedVariables.Count;
        var (type, argument) = BindUnevaluated(syntax, out _);
        CapturedVariables.RemoveRange(captured, CapturedVariables.Count - captured);
        if (type.TypeKind == TypeKind.Error || argument is { HasErrors: true })
        {
            return Error(invocation);
        }

        if (argument is not null || type is UntypedTypeSymbol or OpenArrayTypeSymbol or TextTypeSymbol)
        {
            _diagnostics.Report(syntax, "E2002", syntax.ToString().Trim());
            return Error(invocation);
        }

        return new BoundIntrinsicCallExpression(invocation, intrinsic, [], _types.Pointer, constantValue: null, typeOperand: type);
    }

    /// <summary>
    /// LANG-713 (C-116): <c>Default(T)</c> — the zero value of the type <c>T</c> (0, False, 0.0, <c>nil</c>, <c>''</c>, the empty set, a
    /// zeroed record or static array), as a constant of that type; an operand that is not a type name is E2002.
    /// </summary>
    private BoundExpression BindDefault(InvocationExpressionSyntax invocation, ExpressionSyntax syntax)
    {
        var captured = CapturedVariables.Count;
        var (type, argument) = BindUnevaluated(syntax, out _);
        CapturedVariables.RemoveRange(captured, CapturedVariables.Count - captured);
        if (type.TypeKind == TypeKind.Error || argument is { HasErrors: true })
        {
            return Error(invocation);
        }

        if (argument is not null)
        {
            _diagnostics.Report(syntax, "E2002", syntax.ToString().Trim());
            return Error(invocation);
        }

        object? zero = type switch
        {
            BooleanTypeSymbol => false,
            IntegerTypeSymbol or CharTypeSymbol or EnumTypeSymbol or SetTypeSymbol => BigInteger.Zero,
            RealTypeSymbol => 0.0,
            StringTypeSymbol => "",
            PointerTypeSymbol or ClassTypeSymbol or ClassRefTypeSymbol or InterfaceTypeSymbol or DynamicArrayTypeSymbol or ProceduralTypeSymbol
                or MethodPointerTypeSymbol => null,
            RecordTypeSymbol or ArrayTypeSymbol => ImmutableArray<ConstantValue?>.Empty,
            _ => type,                                                              // no zero constant (method pointers…)
        };
        return ReferenceEquals(zero, type) ? Unsupported(invocation, "Default of " + type.Name) : new BoundLiteral(invocation, new ConstantValue(zero, type));
    }

    /// <summary>
    /// LANG-972…976 (C-416): a type query, folded here (CMP-1474) — it never reaches lowering as a call. <c>IsManagedType</c>,
    /// <c>HasWeakRef</c> and <c>GetTypeKind</c> take a type (E2002 otherwise, as <c>Default</c>) and are the constants of
    /// <see cref="ConstantFolder.FoldTypeQuery"/>; with an operand that depends on a type parameter of an open instance they are
    /// values of their result type and no constants. <c>IsConstValue(X)</c> binds <c>X</c> for its diagnostics and never evaluates
    /// it (LANG-903: no code, no call, no capture): <c>True</c> exactly when <c>X</c> is a constant expression; a type name is E2009.
    /// </summary>
    private BoundExpression BindTypeQuery(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic, ExpressionSyntax syntax)
    {
        TypeQueryUses++;                                                        // LANG-977 (C-417)
        var captured = CapturedVariables.Count;
        var (type, argument) = BindUnevaluated(syntax, out _);
        CapturedVariables.RemoveRange(captured, CapturedVariables.Count - captured);
        if (type.TypeKind == TypeKind.Error || argument is { HasErrors: true })
        {
            return Error(invocation);
        }

        if (intrinsic.Intrinsic == IntrinsicKind.IsConstValue)
        {
            if (argument is null)
            {
                _diagnostics.Report(syntax, "E2009", type.Name);
                return Error(invocation);
            }

            return new BoundIntrinsicCallExpression(invocation, intrinsic, [argument], _types.Boolean,
                new ConstantValue(argument.ConstantValue is not null, _types.Boolean));
        }

        if (argument is not null)
        {
            _diagnostics.Report(syntax, "E2002", syntax.ToString().Trim());
            return Error(invocation);
        }

        return _folder.TypeQueryResultType(invocation, intrinsic) is { } resultType
            ? new BoundIntrinsicCallExpression(invocation, intrinsic, [], resultType, _folder.FoldTypeQuery(intrinsic, type, resultType), typeOperand: type)
            : Error(invocation);
    }

    /// <summary>The argument of <c>Low</c>/<c>High</c>/<c>SizeOf</c>: a type name (bound argument null) or an expression.</summary>
    private (TypeSymbol Type, BoundExpression? Argument) BindTypeOrValue(ExpressionSyntax syntax)
    {
        if (syntax is PredefinedTypeExpressionSyntax)
        {
            return (_types.String, null);
        }

        if (_scope.Unqualified(syntax) is IdentifierNameSyntax { Identifier: { IsMissing: false } token } name
            && _scope.For(name).Lookup(token.Value as string ?? token.Text) is TypeSymbol type)
        {
            HintChecks.CheckName(_diagnostics, _types, _scope, name, token.Value as string ?? token.Text, type);    // LANG-472 (C-232)
            return (type, null);
        }

        if (syntax is GenericNameSyntax or MemberAccessExpressionSyntax && TypeNamedBy(syntax) is { } named)
        {
            // C-116: SizeOf(TPair<K, V>), Default(TList<T>); LANG-770: SizeOf(TOuter.TInner); LANG-780: SizeOf(U.TBox<Integer>);
            // LANG-774 (C-356): E2610 for a nested type this code may not access
            return (named.TypeKind == TypeKind.Error || IncompleteInstance(syntax, named) ? _types.Error : named, null);
        }

        var argument = BindExpression(syntax);
        return (argument.Type, argument);
    }

    /// <summary>
    /// LANG-290. Value casts: ordinal → ordinal (folded for constants), ordinal → pointer, pointer → 8-byte integer, pointer →
    /// pointer, dynamic array → pointer or 8-byte integer (C-178), integer → real and real → real (converting; C-057 decision: real → real converts like the implicit
    /// conversion). A cast from a real to anything else is E2040. Otherwise a cast of a variable reference is a variable
    /// typecast (<see cref="BoundVariableTypecastExpression"/>) when both types have the same size and neither is managed (E2041
    /// otherwise); any other cast is E2040. In a <paramref name="variableContext"/> a variable operand always gives a variable
    /// typecast. Casts involving strings arrive with the string tasks (E2900).
    /// </summary>
    private BoundExpression BindTypecast(InvocationExpressionSyntax node, TypeSymbol target, BoundExpression operand, bool variableContext)
    {
        var source = operand.Type;
        if (!ReferenceEquals(source, target) && (source is RecordTypeSymbol || target is RecordTypeSymbol) && !variableContext
            && (Operators.Conversion(source, target, _conversions, "Explicit") ?? Operators.Conversion(source, target, _conversions)) is { } conversion)
        {
            return ConversionCall(node, operand, conversion);                   // LANG-731 Explicit, else Implicit (C-123)
        }

        if ((source is StringTypeSymbol || target is StringTypeSymbol) && !(variableContext && IsAddressable(operand)))
        {
            return StringTypecast(node, target, operand);
        }

        if (variableContext && IsAddressable(operand))
        {
            return VariableTypecast(node, target, operand);
        }

        // C-326 (LANG-245, LANG-290): a character constant cast to PChar / PAnsiChar is the static one-character literal,
        // like a longer string literal — not the character code as an address
        if (source is CharTypeSymbol && operand.ConstantValue is { } character
            && (Strings.IsCharPointer(target, wide: true) || Strings.IsCharPointer(target, wide: false)))
        {
            return _conversions.ConvertConstant(character, target, node, _diagnostics) is { } data
                ? new BoundConversionExpression(node, operand, target, ConversionKind.Explicit, isExplicit: true, data)
                : Error(node, operand);
        }

        if (source is RealTypeSymbol && target is not RealTypeSymbol)
        {
            _diagnostics.Report(node, "E2040", source.Name, target.Name);
            return Error(node, operand);
        }

        var isValueCast = (source, target) switch
        {
            _ when source.IsOrdinal && target.IsOrdinal => true,
            (_, PointerTypeSymbol) when source.IsOrdinal => true,
            (PointerTypeSymbol, IntegerTypeSymbol { Bits: 64 }) => true,
            (PointerTypeSymbol, PointerTypeSymbol) => true,
            (DynamicArrayTypeSymbol, IntegerTypeSymbol { Bits: 64 } or PointerTypeSymbol) => true,     // C-178: the block reference, no reference counting
            (ProceduralTypeSymbol, IntegerTypeSymbol { Bits: 64 } or PointerTypeSymbol or ProceduralTypeSymbol) => true,  // LANG-290: the code address
            (PointerTypeSymbol, ProceduralTypeSymbol) => true,
            (ClassTypeSymbol, ClassTypeSymbol or PointerTypeSymbol or IntegerTypeSymbol { Bits: 64 }) => true,     // LANG-290: the reference (C-080)
            (PointerTypeSymbol, ClassTypeSymbol) => true,
            (InterfaceTypeSymbol, IntegerTypeSymbol { Bits: 64 } or PointerTypeSymbol) => true,      // types.md row 244 (C-098): the reference, uncounted
            (ClassRefTypeSymbol, ClassRefTypeSymbol or PointerTypeSymbol or IntegerTypeSymbol { Bits: 64 }) => true,  // LANG-290 (C-086)
            (PointerTypeSymbol, ClassRefTypeSymbol) => true,
            (PointerTypeSymbol { PointedType: null }, InterfaceTypeSymbol { IsMethodReference: false }) => true,   // LANG-682 (C-299): a borrowed reference
            (_, ClassRefTypeSymbol) when source.IsOrdinal => true,
            (_, ClassTypeSymbol) when source.IsOrdinal => true,                 // LANG-290 row 2 (C-261): also a non-variable operand
            (IntegerTypeSymbol or RealTypeSymbol, RealTypeSymbol) => true,
            _ => false,
        };
        if (!isValueCast)
        {
            if (IsAddressable(operand) && target is not InterfaceTypeSymbol)        // LANG-682: IFoo(x) of anything but a Pointer is E2040
            {
                return VariableTypecast(node, target, operand);
            }

            _diagnostics.Report(node, "E2040", source.Name, target.Name);
            return Error(node, operand);
        }

        ConstantValue? constant = null;                                 // integer → real is no constant expression (LANG-340, lead decision C-024)
        if (operand.ConstantValue is { } value)
        {
            if (source.IsOrdinal && target.IsOrdinal)
            {
                constant = _folder.FoldTypecast(node, target, value);
                if (constant is null)
                {
                    return Error(node, operand);
                }
            }
            else if (source is PointerTypeSymbol && target is PointerTypeSymbol or ProceduralTypeSymbol or ClassTypeSymbol && value.Value is null)
            {
                constant = new ConstantValue(null, target);             // nil of the target pointer type
            }
        }

        return new BoundConversionExpression(node, operand, target, ConversionKind.Explicit, isExplicit: true, constant);
    }

    /// <summary>
    /// LANG-290 rows 3, 4, 7 and 8 (C-063): <c>T(x)</c> to a string type converts like the implicit LANG-245 conversions (also from
    /// <c>PChar</c>, without W1020); <c>PChar(s)</c> / <c>PAnsiChar(a)</c> / <c>Pointer(s)</c> of a long string point to its data
    /// (a literal: the static literal); a long string to a 64-bit integer reinterprets the reference. Anything else is a
    /// variable typecast of a variable (E2041 for managed types) or E2040.
    /// </summary>
    private BoundExpression StringTypecast(SyntaxNode node, TypeSymbol target, BoundExpression operand)
    {
        var source = operand.Type;
        if (target is StringTypeSymbol && (source is StringTypeSymbol or CharTypeSymbol || _conversions.Classify(source, target).Exists))
        {
            if (operand.ConstantValue is { } constant)
            {
                return _conversions.ConvertConstant(constant, target, node, _diagnostics) is { } converted
                    ? new BoundConversionExpression(node, operand, target, ConversionKind.ImplicitString, isExplicit: true, converted)
                    : Error(node, operand);
            }

            return ReferenceEquals(source, target)
                ? operand
                : new BoundConversionExpression(node, operand, target, ConversionKind.ImplicitString, isExplicit: true, constantValue: null);
        }

        // C-369 (LANG-133): a string literal or constant cast to PAnsiChar is its static ANSI data, as the implicit conversion gives
        var toData = source is StringTypeSymbol { Variant: not StringVariant.Short } && target is PointerTypeSymbol pointer
            && (pointer.PointedType is null && pointer != _types.Nil
                || Strings.IsCharPointer(target, wide: true) && Strings.IsUnicode(source)
                || Strings.IsCharPointer(target, wide: false) && (Strings.IsAnsi(source) || operand.ConstantValue is not null));
        var toInteger = source is StringTypeSymbol { Variant: not StringVariant.Short } && target is IntegerTypeSymbol { Bits: 64 };
        if ((toData || toInteger) && operand.ConstantValue is { } literal)
        {
            if (toData && target != _types.Pointer && _conversions.ConvertConstant(literal, target, node, _diagnostics) is { } data)
            {
                return new BoundConversionExpression(node, operand, target, ConversionKind.Explicit, isExplicit: true, data);
            }
        }
        else if (toData || toInteger)
        {
            return new BoundConversionExpression(node, operand, target, ConversionKind.Explicit, isExplicit: true, constantValue: null);
        }

        if (IsAddressable(operand))
        {
            return VariableTypecast(node, target, operand);
        }

        _diagnostics.Report(node, "E2040", source.Name, target.Name);
        return Error(node, operand);
    }

    /// <summary>LANG-290 last row: same size, neither type managed (E2041 at the cast otherwise).</summary>
    private BoundExpression VariableTypecast(SyntaxNode node, TypeSymbol target, BoundExpression operand)
    {
        if (operand.Type is UntypedTypeSymbol && target.Size >= 0)
        {
            return new BoundVariableTypecastExpression(node, operand, target);      // C-202: an untyped parameter as any type
        }

        if (operand.Type.IsManaged || target.IsManaged || operand.Type.Size != target.Size || target.Size < 0)
        {
            _diagnostics.Report(node, "E2041");
            return Error(node, operand);
        }

        return new BoundVariableTypecastExpression(node, operand, target);
    }

    // ------------------------------------------------------------------ calls (LANG-420, 421, 441)

    /// <summary>A call in an expression: functions only (E2034 for a procedure).</summary>
    private BoundExpression CallExpression(SyntaxNode call, IdentifierNameSyntax name, ArgumentListSyntax? argumentList)
    {
        var (routine, arguments) = BindCall(call, name, argumentList);
        if (routine is null)
        {
            return Error(call, [.. arguments]);
        }

        if (routine.ReturnType is null)
        {
            _diagnostics.Report(call, "E2034");
            return Error(call, [.. arguments]);
        }

        return new BoundCallExpression(call, routine, arguments);
    }

    /// <summary>
    /// Binds the arguments of a call of the routine(s) <paramref name="name"/> denotes and picks the routine: the only
    /// candidate, or the only applicable one of an overload set (LANG-441; ranking by conversion cost, LANG-442, arrives
    /// with M2.5). Routine is null after an error (reported once); the bound arguments are returned either way.
    /// </summary>
    public (RoutineSymbol? Routine, ImmutableArray<BoundExpression> Arguments) BindCall(SyntaxNode call, IdentifierNameSyntax name, ArgumentListSyntax? argumentList)
    {
        var token = name.Identifier;
        return BindCall(call, _scope.For(name).LookupCandidates(token.Value as string ?? token.Text), argumentList);
    }

    /// <summary>As above for the given candidates (one procedural type's signature for a call through a value).</summary>
    private (RoutineSymbol? Routine, ImmutableArray<BoundExpression> Arguments) BindCall(SyntaxNode call, ImmutableArray<RoutineSymbol> candidates, ArgumentListSyntax? argumentList)
    {
        var syntaxes = argumentList?.Arguments ?? default;
        var builder = ImmutableArray.CreateBuilder<BoundExpression>(syntaxes.Count);
        var hasErrors = false;
        List<ConstructorForms>? forms = null;                                    // LANG-273 (C-388)
        for (var i = 0; i < syntaxes.Count; i++)
        {
            var argument = syntaxes[i];
            if (argument.WidthColonToken is { } colon)
            {
                _diagnostics.Report(colon, "E2915");
                hasErrors = true;
            }

            // a var/out parameter of the only candidate is a variable context (variable typecasts, LANG-290)
            var byReference = candidates.Length == 1 && i < candidates[0].Parameters.Length && candidates[0].Parameters[i].IsByReference;
            // a dynamic-array value parameter of the only candidate gives an array constructor its type (LANG-259), a
            // procedural one makes a routine name its address (LANG-480)
            var expected = candidates.Length == 1 && i < candidates[0].Parameters.Length && candidates[0].Parameters[i].Type is DynamicArrayTypeSymbol or ProceduralTypeSymbol or MethodPointerTypeSymbol
                    or InterfaceTypeSymbol { IsMethodReference: true }
                ? candidates[0].Parameters[i].Type
                : OpenArrayContext(candidates, i, constructor: Unparenthesized(argument.Expression) is SetConstructorSyntax)
                    ?? ProceduralContext(candidates, i, argument.Expression);
            // an untyped parameter (C-202) also takes p^ of an untyped pointer (a byte variable, as for Move)
            var untyped = byReference && candidates[0].Parameters[i].IsUntyped && Unparenthesized(argument.Expression) is DereferenceExpressionSyntax;
            // LANG-680 (C-299): an interface type identifier is its GUID for a value or const TGUID parameter of any candidate
            expected ??= InterfaceNamedBy(argument.Expression) is null ? null
                : candidates.Where(c => i < c.Parameters.Length && !c.Parameters[i].IsByReference && GuidValues.IsGuidType(c.Parameters[i].Type))
                    .Select(c => c.Parameters[i].Type).FirstOrDefault();
            if (ConstructorFormsAt(candidates, i, argument.Expression, expected) is { } constructorForms)
            {
                (forms ??= []).Add(constructorForms);
                builder.Add(constructorForms.Placeholder);
                continue;
            }

            var bound = untyped ? BindDereference((DereferenceExpressionSyntax)Unparenthesized(argument.Expression), untyped: true)
                : byReference ? BindVariable(argument.Expression) : BindExpression(argument.Expression, expected);
            hasErrors |= bound.HasErrors;
            builder.Add(bound);
        }

        var arguments = builder.ToImmutable();
        if (hasErrors || candidates.IsEmpty)
        {
            return (null, arguments);
        }

        var routine = candidates[0];
        if (candidates.Length > 1)
        {
            var resolver = new OverloadResolver(_conversions);
            Func<RoutineSymbol, ImmutableArray<BoundExpression>?> argumentsOf = forms is null ? _ => arguments : c => ArgumentsFor(c, arguments, forms);
            if (forms is not null && SetOrArrayAmbiguity(resolver.Applicable(candidates, argumentsOf, IsVariableArgument), forms, syntaxes, candidates[0].Name))
            {
                return (null, arguments);
            }

            var resolution = resolver.Resolve(candidates, argumentsOf, IsVariableArgument);
            if (resolution.Routine is null)
            {
                if (resolution.Tied.IsEmpty)
                {
                    _diagnostics.Report(call, "E2123", routine.Name);
                }
                else
                {
                    _diagnostics.Report(call, "E2122", [.. resolution.Tied.Where(c => !c.Locations.IsEmpty).Select(c => new RelatedLocation(c.Locations[0], "candidate"))], routine.Name);
                }

                return (null, arguments);
            }

            routine = resolution.Routine;
            if (forms is not null)
            {
                // the probes only decided the candidate: the chosen form is bound once for real (captures, address-taken variables)
                var final = arguments.ToBuilder();
                foreach (var form in forms)
                {
                    final[form.Index] = BindExpression(syntaxes[form.Index].Expression, FormType(routine, form.Index) ?? form.Expected);
                }

                arguments = final.ToImmutable();
            }
        }

        CheckHintDirectives(routine, call);                                     // LANG-472 (C-127)
        return CheckArguments(call, routine, syntaxes, arguments);
    }

    /// <summary>
    /// LANG-273 (C-388): a bracket constructor argument at <see cref="Index"/> of an overloaded call where a candidate has a value or
    /// <c>const</c> dynamic-array parameter: its binding in the usual context (<see cref="Default"/>, null when that fails) and in
    /// each dynamic-array or open-array parameter type of a candidate there (<see cref="ByType"/>, null when the elements do not fit).
    /// </summary>
    private sealed record ConstructorForms(int Index, TypeSymbol? Expected, BoundExpression? Default, Dictionary<TypeSymbol, BoundExpression?> ByType)
    {
        /// <summary>The argument kept in the list until a candidate is chosen (a form that bound, so the call is not reported early).</summary>
        public BoundExpression Placeholder => Default ?? ByType.Values.First(v => v is not null)!;
    }

    /// <summary>
    /// The <see cref="ConstructorForms"/> of argument <paramref name="index"/>, probed on scratch binders; null when the argument is no
    /// constructor (or holds an anonymous method, which is bound once), no candidate has a dynamic-array parameter there, or no form
    /// binds (the argument is then bound as usual and reports its errors).
    /// </summary>
    private ConstructorForms? ConstructorFormsAt(ImmutableArray<RoutineSymbol> candidates, int index, ExpressionSyntax syntax, TypeSymbol? expected)
    {
        if (candidates.Length < 2 || Unparenthesized(syntax) is not SetConstructorSyntax constructor || constructor.Elements.Any(e => e.UpperBound is not null)
            || constructor.DescendantNodes().OfType<AnonymousMethodExpressionSyntax>().Any()
            || !candidates.Any(c => FormType(c, index) is DynamicArrayTypeSymbol))
        {
            return null;
        }

        BoundExpression? Probe(TypeSymbol? type)
        {
            var probe = new DiagnosticBag();
            var bound = ProbeBinder(probe).BindExpression(syntax, type);
            return bound.HasErrors || probe.Count != 0 ? null : bound;
        }

        var byType = new Dictionary<TypeSymbol, BoundExpression?>(ReferenceEqualityComparer.Instance);
        foreach (var type in candidates.Select(c => FormType(c, index)).OfType<TypeSymbol>().Distinct())
        {
            byType[type] = Probe(type);
        }

        var forms = new ConstructorForms(index, expected, Probe(expected), byType);
        return forms.Default is null && byType.Values.All(v => v is null) ? null : forms;
    }

    /// <summary>The value/<c>const</c> dynamic-array or open-array parameter type of <paramref name="candidate"/> at <paramref name="index"/>, else null.</summary>
    private static TypeSymbol? FormType(RoutineSymbol candidate, int index) =>
        index < candidate.Parameters.Length && candidate.Parameters[index] is { IsByReference: false, Type: DynamicArrayTypeSymbol or OpenArrayTypeSymbol } parameter
            ? parameter.Type
            : null;

    /// <summary>The arguments as <paramref name="candidate"/> sees them: each constructor in its parameter's form; null = not applicable.</summary>
    private static ImmutableArray<BoundExpression>? ArgumentsFor(RoutineSymbol candidate, ImmutableArray<BoundExpression> arguments, List<ConstructorForms> forms)
    {
        var builder = arguments.ToBuilder();
        foreach (var form in forms)
        {
            if ((FormType(candidate, form.Index) is { } type ? form.ByType[type] : form.Default) is not { } value)
            {
                return null;
            }

            builder[form.Index] = value;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// LANG-273 (C-388): E2043 when the applicable candidates take one constructor argument both as a set and as a dynamic array
    /// (whatever the costs); true after reporting. The two types are named in candidate order.
    /// </summary>
    private bool SetOrArrayAmbiguity(ImmutableArray<RoutineSymbol> applicable, List<ConstructorForms> forms, SeparatedSyntaxList<ArgumentSyntax> syntaxes, string name)
    {
        foreach (var form in forms)
        {
            var kinds = applicable.Where(c => form.Index < c.Parameters.Length && !c.Parameters[form.Index].IsByReference
                                              && c.Parameters[form.Index].Type is SetTypeSymbol or DynamicArrayTypeSymbol)
                .Select(c => c.Parameters[form.Index].Type).ToList();
            if (kinds.FirstOrDefault(t => t is SetTypeSymbol) is { } set && kinds.FirstOrDefault(t => t is DynamicArrayTypeSymbol) is { } array)
            {
                var (first, second) = kinds.IndexOf(set) < kinds.IndexOf(array) ? (set, array) : (array, set);
                _diagnostics.Report(syntaxes[form.Index].Expression, "E2043", name, first.Name, second.Name);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// LANG-430/259 (C-106): the open array type that gives an argument <c>[…]</c> at position <paramref name="index"/> its meaning —
    /// when every candidate with such a parameter has a value/<c>const</c> open array parameter there of one open array type (so
    /// <c>Format('%d', [x])</c> works for an overload set); null otherwise.
    /// </summary>
    private static OpenArrayTypeSymbol? OpenArrayContext(ImmutableArray<RoutineSymbol> candidates, int index, bool constructor)
    {
        // C-216: a constructor argument is never applicable to a parameter that is no array or set, so those candidates do not count
        var types = candidates.Where(c => index < c.Parameters.Length).Select(c => c.Parameters[index])
            .Where(p => !constructor || p.Type is OpenArrayTypeSymbol or SetTypeSymbol or DynamicArrayTypeSymbol or ArrayTypeSymbol).ToList();
        return types.Count > 0 && types.All(p => !p.IsByReference && ReferenceEquals(p.Type, types[0].Type)) ? types[0].Type as OpenArrayTypeSymbol : null;
    }

    /// <summary>
    /// C-221 (LANG-442, 480, 481): for an overload set, the procedural or method-pointer parameter type at <paramref name="index"/> of a
    /// candidate under which the argument binds as a value of that type (a routine or method name taken as its address, as for a single
    /// candidate); tried in candidate order on a scratch binder. Null when no candidate has such a parameter there, or none takes the
    /// argument (it then binds as usual, e.g. a parameterless function call for another overload).
    /// </summary>
    private TypeSymbol? ProceduralContext(ImmutableArray<RoutineSymbol> candidates, int index, ExpressionSyntax argument)
    {
        if (candidates.Length < 2 || Unparenthesized(argument) is AnonymousMethodExpressionSyntax)
        {
            return null;                                                        // an anonymous method is bound once (it makes symbols)
        }

        foreach (var type in candidates.Where(c => index < c.Parameters.Length && !c.Parameters[index].IsByReference)
                     .Select(c => c.Parameters[index].Type).Where(t => t is ProceduralTypeSymbol or MethodPointerTypeSymbol).Distinct())
        {
            var probe = new DiagnosticBag();
            var bound = ProbeBinder(probe).BindExpression(argument, type);
            if (!bound.HasErrors && probe.Count == 0 && _conversions.Classify(bound.Type, type).Exists)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>A scratch binder for probing (C-221) that sees the same <c>with</c> receivers (C-223).</summary>
    private ExpressionBinder ProbeBinder(DiagnosticBag diagnostics)
    {
        var probe = new ExpressionBinder(_types, _scope, diagnostics, _routine) { CurrentInline = CurrentInline };
        probe._withs.AddRange(_withs);
        return probe;
    }

    /// <summary>LANG-420/421: count, then each argument against its parameter.</summary>
    private (RoutineSymbol? Routine, ImmutableArray<BoundExpression> Arguments) CheckArguments(SyntaxNode call, RoutineSymbol routine,
        SeparatedSyntaxList<ArgumentSyntax> syntaxes, ImmutableArray<BoundExpression> arguments)
    {
        var parameters = routine.Parameters;
        if (arguments.Length < parameters.Length && parameters.Skip(arguments.Length).Any(p => p.DefaultValue is null))
        {
            _diagnostics.Report(call, "E2110", routine.Name);
            return (null, arguments);
        }

        if (arguments.Length > parameters.Length)
        {
            _diagnostics.Report(syntaxes[parameters.Length], "E2111", routine.Name);
            return (null, arguments);
        }

        var converted = ImmutableArray.CreateBuilder<BoundExpression>(arguments.Length);
        var ok = true;
        for (var i = 0; i < arguments.Length; i++)
        {
            var (parameter, argument, syntax) = (parameters[i], arguments[i], syntaxes[i].Expression);
            if (parameter.IsByReference)
            {
                ok &= CheckReferenceArgument(argument, parameter, syntax);
                converted.Add(argument);
            }
            else
            {
                var value = ConvertForAssignment(argument, parameter.Type, syntax);
                ok &= !value.HasErrors;
                converted.Add(value);
            }
        }

        // LANG-422 (C-105): omitted trailing arguments take the defaults, already converted to the parameter types
        converted.AddRange(parameters.Skip(arguments.Length).Select(p => (BoundExpression)new BoundLiteral(call, p.DefaultValue!)));
        return (ok ? routine : null, converted.ToImmutable());
    }

    /// <summary>LANG-421 / LANG-281: an assignable variable reference of the identical type.</summary>
    private bool CheckReferenceArgument(BoundExpression argument, ParameterSymbol parameter, ExpressionSyntax syntax)
    {
        if (parameter.Type.TypeKind == TypeKind.Error)
        {
            return true;
        }

        if (parameter is { IsUntyped: true, Mode: ParameterMode.Const })
        {
            // C-202: any variable reference (typed constants and const parameters included); values have no address
            if (!IsAddressable(argument) || IsPropertyRead(argument))
            {
                _diagnostics.Report(syntax, "E2033");
                return false;
            }

            return true;
        }

        if (!CheckVariableReference(argument, syntax))
        {
            return false;
        }

        if (parameter.IsUntyped)
        {
            return true;                                                // C-202: a variable of any type
        }

        if (parameter.Type is OpenArrayTypeSymbol ? !_conversions.Classify(argument.Type, parameter.Type).Exists : !Pointers.SameType(argument.Type, parameter.Type))
        {
            _diagnostics.Report(syntax, "E2010", argument.Type.Name, parameter.Type.Name);
            return false;
        }

        return true;
    }

    /// <summary>
    /// LANG-421: <paramref name="argument"/> is an assignable variable reference (a <c>var</c>/<c>out</c> argument, the
    /// operand of <c>Inc</c>/<c>Dec</c>); E2053 / E2112 / E2113 at <paramref name="syntax"/> otherwise.
    /// </summary>
    public bool CheckVariableReference(BoundExpression argument, ExpressionSyntax syntax)
    {
        if (IsPropertyRead(argument))
        {
            _diagnostics.Report(syntax, "E2113");                       // LANG-645: properties are not variables
            return false;
        }

        switch (BoundIndexExpression.RootOf(argument))
        {
            case BoundVariableExpression { Variable: var v } when ForVariables.Contains(v):
            case BoundParameterExpression { Parameter: var p } when ForVariables.Contains(p):
                _diagnostics.Report(syntax, "E2053");                   // LANG-361 rule 6
                return false;
            case BoundVariableExpression { Variable.IsReadOnly: true }:
            case BoundDereferenceExpression { Pointer: BoundVariableExpression { Variable: var with } } when ReadOnlyWith.Contains(with):
                _diagnostics.Report(syntax, "E2112");                   // a typed constant (LANG-344), a const record via with (C-198)
                return false;
            case BoundVariableExpression:
            case BoundDereferenceExpression:
            case BoundFieldAccessExpression:
            case BoundParameterExpression { Parameter.Mode: not ParameterMode.Const }:
                break;
            case BoundParameterExpression:
            case { ConstantValue: not null }:
                _diagnostics.Report(syntax, "E2112");
                return false;
            default:
                _diagnostics.Report(syntax, "E2113");
                return false;
        }

        return true;
    }

    /// <summary>An argument a <c>var</c>/<c>out</c> parameter accepts (LANG-421), without diagnostics (overload resolution).</summary>
    private bool IsVariableArgument(BoundExpression argument) =>
        !IsPropertyRead(argument)
        && BoundIndexExpression.RootOf(argument) is BoundVariableExpression or BoundDereferenceExpression or BoundFieldAccessExpression or BoundParameterExpression { Parameter.Mode: not ParameterMode.Const };

    // ------------------------------------------------------------------ unary operators (LANG-227)

    private BoundExpression BindUnary(UnaryExpressionSyntax unary)
    {
        var operand = BindExpression(unary.Operand);
        if (operand.HasErrors)
        {
            return Error(unary, operand);
        }

        var op = unary.OperatorToken;
        var kind = op.Kind switch
        {
            SyntaxKind.MinusToken => BoundUnaryOperatorKind.Negate,
            SyntaxKind.PlusToken => BoundUnaryOperatorKind.Identity,
            _ => BoundUnaryOperatorKind.Not,
        };
        if (operand.Type is RecordTypeSymbol && UserUnary(unary, kind, operand) is { } user)
        {
            return user;                                                        // LANG-731 (C-123)
        }

        if (operand.ConstantValue is { } value)
        {
            return _folder.FoldUnary(op, value) is { } folded
                ? new BoundUnaryExpression(unary, kind, operand, folded.Type, folded)
                : Error(unary, operand);
        }

        switch (kind, operand.Type)
        {
            case (BoundUnaryOperatorKind.Negate or BoundUnaryOperatorKind.Identity, IntegerTypeSymbol integer):
            {
                // -Cardinal is Int64; -UInt64 stays UInt64 with W1010
                var type = kind == BoundUnaryOperatorKind.Negate && integer.Bits == 32 && !integer.IsSigned ? _types.Int64 : ComputationType(integer);
                if (kind == BoundUnaryOperatorKind.Negate && ReferenceEquals(type, _types.UInt64))
                {
                    _diagnostics.Report(op, "W1010");
                }

                return new BoundUnaryExpression(unary, kind, Convert(operand, type), type, constantValue: null);
            }

            case (BoundUnaryOperatorKind.Negate or BoundUnaryOperatorKind.Identity, RealTypeSymbol):
            case (BoundUnaryOperatorKind.Not, BooleanTypeSymbol):
                return new BoundUnaryExpression(unary, kind, operand, operand.Type, constantValue: null);

            case (BoundUnaryOperatorKind.Not, IntegerTypeSymbol integer):
            {
                // complement in the operand's own type for 8/16-bit types, else in its computation type
                var type = integer.Bits < 32 ? integer : ComputationType(integer);
                return new BoundUnaryExpression(unary, kind, Convert(operand, type), type, constantValue: null);
            }

            default:
                _diagnostics.Report(op, "E2011", op.Text, $"'{operand.Type.Name}'");
                return Error(unary, operand);
        }
    }

    // ------------------------------------------------------------------ binary operators (LANG-221…226, 301…305)

    /// <summary>
    /// A binary operation; <paramref name="arrayContext"/> is the expected dynamic-array type of a <c>+</c> (LANG-275 rule 3): two
    /// constructor operands are bound in it, and it passes on to the operands so that a chain <c>[1] + [2] + [3]</c> keeps it.
    /// </summary>
    private BoundExpression BindBinary(BinaryExpressionSyntax binary, DynamicArrayTypeSymbol? arrayContext = null)
    {
        var op = binary.OperatorToken;
        if (op.Kind is SyntaxKind.IsKeyword or SyntaxKind.AsKeyword)
        {
            return BindTypeTest(binary, isAs: op.Kind == SyntaxKind.AsKeyword);       // LANG-650, 651
        }

        BoundExpression left, right;
        // a set constructor takes the set type of the other operand (LANG-251), a dynamic-array constructor the other operand's array
        // type (LANG-275 rule 2); the element of 'in' gives no set context
        if (Unparenthesized(binary.Right) is SetConstructorSyntax && Unparenthesized(binary.Left) is not SetConstructorSyntax && op.Kind != SyntaxKind.InKeyword)
        {
            left = BindExpression(binary.Left, arrayContext);
            right = BindExpression(binary.Right, left.Type);
        }
        else if (Unparenthesized(binary.Left) is SetConstructorSyntax && Unparenthesized(binary.Right) is not SetConstructorSyntax)
        {
            right = BindExpression(binary.Right, arrayContext);
            left = BindExpression(binary.Left, right.Type);
        }
        else if (arrayContext is not null)
        {
            left = BindExpression(binary.Left, arrayContext);                   // LANG-275 rule 3: [...] + [...] in a dynamic-array context
            right = BindExpression(binary.Right, arrayContext);
        }
        else if (op.Kind is SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken && InterfaceNamedBy(binary.Right) is not null
            && InterfaceNamedBy(binary.Left) is null)
        {
            left = Called(BindOperand(binary.Left));                            // LANG-680 (C-299): G = IFoo
            right = BindExpression(binary.Right, left.Type);
        }
        else if (op.Kind is SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken && InterfaceNamedBy(binary.Left) is not null
            && InterfaceNamedBy(binary.Right) is null)
        {
            right = Called(BindOperand(binary.Right));                          // IFoo = G
            left = BindExpression(binary.Left, right.Type);
        }
        else if (op.Kind is SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken)
        {
            // LANG-482: p = nil, p <> q compare the procedural values; anything else compares the results of calling them
            left = BindOperand(binary.Left);
            right = BindOperand(binary.Right);
            if (!IsProceduralComparison(left.Type, right.Type))
            {
                left = Called(left);
                right = Called(right);
            }
        }
        else
        {
            left = BindExpression(binary.Left);
            right = BindExpression(binary.Right);
        }

        if (left.HasErrors || right.HasErrors)
        {
            return Error(binary, left, right);
        }

        if ((left.Type is RecordTypeSymbol || right.Type is RecordTypeSymbol) && UserBinary(binary, left, right) is { } user)
        {
            return user;                                                        // LANG-732 (C-123)
        }

        return BindBinaryOperation(binary, left, right);
    }

    /// <summary>The built-in binary operator on bound operands (LANG-221…226, 301…305).</summary>
    private BoundExpression BindBinaryOperation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var op = binary.OperatorToken;
        if (op.Kind == SyntaxKind.PlusToken && (left.Type is DynamicArrayTypeSymbol || right.Type is DynamicArrayTypeSymbol))
        {
            return ArrayConcatenation(binary, left, right);                    // LANG-275 (C-390), before folding: [] is a constant
        }

        if (left.ConstantValue is { } l && right.ConstantValue is { } r)
        {
            return _folder.FoldBinary(op, l, r) is { } folded
                ? new BoundBinaryExpression(binary, OperatorKind(op), left, right, folded.Type, folded.Type, folded)
                : Error(binary, left, right);
        }

        var (lt, rt) = (left.Type, right.Type);
        if (op.Kind == SyntaxKind.InKeyword)
        {
            return BindIn(binary, left, right);
        }

        if (lt is SetTypeSymbol || rt is SetTypeSymbol)
        {
            return SetOperation(binary, left, right);
        }

        switch (op.Kind)
        {
            case SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.AsteriskToken:
                if (op.Kind == SyntaxKind.PlusToken && (lt is StringTypeSymbol || rt is StringTypeSymbol || (lt is CharTypeSymbol && rt is CharTypeSymbol)))
                {
                    return Concatenation(binary, left, right);
                }

                if (lt is IntegerTypeSymbol && rt is IntegerTypeSymbol)
                {
                    return IntegerOperation(binary, left, right);
                }

                if (op.Kind != SyntaxKind.AsteriskToken && (lt is PointerTypeSymbol || rt is PointerTypeSymbol))
                {
                    return PointerArithmetic(binary, left, right);
                }

                return IsNumeric(lt) && IsNumeric(rt) ? RealOperation(binary, left, right) : Inapplicable(binary, left, right);

            case SyntaxKind.SlashToken:
                return IsNumeric(lt) && IsNumeric(rt) ? RealOperation(binary, left, right) : Inapplicable(binary, left, right);

            case SyntaxKind.DivKeyword or SyntaxKind.ModKeyword:
                return lt is IntegerTypeSymbol && rt is IntegerTypeSymbol ? IntegerOperation(binary, left, right) : Inapplicable(binary, left, right);

            case SyntaxKind.AndKeyword or SyntaxKind.OrKeyword or SyntaxKind.XorKeyword:
                if (lt is BooleanTypeSymbol && rt is BooleanTypeSymbol)
                {
                    return BooleanOperation(binary, left, right);
                }

                return lt is IntegerTypeSymbol && rt is IntegerTypeSymbol ? IntegerOperation(binary, left, right) : Inapplicable(binary, left, right);

            case SyntaxKind.ShlKeyword or SyntaxKind.ShrKeyword:
                if (lt is IntegerTypeSymbol shifted && rt is IntegerTypeSymbol)
                {
                    var type = ComputationType(shifted);
                    return new BoundBinaryExpression(binary, OperatorKind(op), Convert(left, type), Convert(right, type), type, type, constantValue: null);
                }

                return Inapplicable(binary, left, right);

            case SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken or SyntaxKind.LessThanToken
                or SyntaxKind.GreaterThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanEqualsToken:
                return Relational(binary, left, right);

            default:
                return Unsupported(op, binary, $"operator '{op.Text}'");      // in, is, as: sets and classes (M2.2+)
        }
    }

    /// <summary>
    /// LANG-247 (CMP-342): <c>p + n</c>, <c>n + p</c>, <c>p - n</c> (the pointer's type) and <c>p - q</c> of two compatible pointers
    /// (a <c>NativeInt</c> element count), for <c>PChar</c>-like pointers and under <c>{$POINTERMATH ON}</c> (the untyped
    /// <c>Pointer</c> counts bytes there); E2011 otherwise.
    /// </summary>
    private BoundExpression PointerArithmetic(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var op = binary.OperatorToken;
        var subtract = op.Kind == SyntaxKind.MinusToken;
        if (left.Type is PointerTypeSymbol pointer && AllowsPointerMath(pointer, op) && pointer != _types.Nil)
        {
            if (right.Type is IntegerTypeSymbol)
            {
                return new BoundPointerArithmeticExpression(binary, left, Convert(right, _types.NativeInt), subtract, difference: false, pointer);
            }

            if (subtract && right.Type is PointerTypeSymbol other && other != _types.Nil && Pointers.Compatible(other, pointer)
                && Pointers.ElementSize(pointer) == Pointers.ElementSize(other))
            {
                return new BoundPointerArithmeticExpression(binary, left, right, subtract: true, difference: true, _types.NativeInt);
            }
        }
        else if (!subtract && left.Type is IntegerTypeSymbol && right.Type is PointerTypeSymbol rightPointer && AllowsPointerMath(rightPointer, op)
            && rightPointer != _types.Nil)
        {
            return new BoundPointerArithmeticExpression(binary, right, Convert(left, _types.NativeInt), subtract: false, difference: false, rightPointer);
        }

        return Inapplicable(binary, left, right);
    }

    /// <summary>LANG-221/222: both operands in the combined computation type; W1010 for signed mixed with UInt64.</summary>
    private BoundBinaryExpression IntegerOperation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var (lt, rt) = AdaptedComputationTypes(left, right);
        var type = Combine(lt, rt, out var mixesSigns);
        if (mixesSigns)
        {
            _diagnostics.Report(binary.OperatorToken, "W1010");
        }

        return new BoundBinaryExpression(binary, OperatorKind(binary.OperatorToken), Convert(left, type), Convert(right, type), type, type, constantValue: null);
    }

    /// <summary>An operand of a UnicodeString operation (LANG-243/244): a UnicodeString or a character.</summary>
    private static bool IsUnicodeOperand(TypeSymbol type) => type is StringTypeSymbol { Variant: StringVariant.Unicode } or CharTypeSymbol;

    /// <summary>LANG-243: an operand beside a string: any string kind, a character, <c>PChar</c> or <c>PAnsiChar</c>.</summary>
    private static bool IsStringOperand(TypeSymbol type) =>
        type is StringTypeSymbol or CharTypeSymbol || Strings.IsCharPointer(type, wide: true) || Strings.IsCharPointer(type, wide: false);

    /// <summary>
    /// LANG-243/244 (C-063): the string kind two operands are combined in — <c>Ansi</c> when both are of the AnsiString family,
    /// <c>Short</c> when both are short strings, else <c>Unicode</c>. A string or character constant takes the other operand's kind.
    /// </summary>
    private static StringVariant StringOperandKind(BoundExpression left, BoundExpression right)
    {
        static StringVariant? Kind(BoundExpression e) =>
            e.ConstantValue is not null && e.Type is StringTypeSymbol or CharTypeSymbol ? null
            : e.Type is StringTypeSymbol s ? s.Variant
            : StringVariant.Unicode;

        var (l, r) = (Kind(left), Kind(right));
        return (l ?? r ?? StringVariant.Unicode) == (r ?? l ?? StringVariant.Unicode) ? l ?? r ?? StringVariant.Unicode : StringVariant.Unicode;
    }

    /// <summary>The AnsiString-family type of a byte-string operation: the left operand's when it is one, else the right one's.</summary>
    private static TypeSymbol AnsiTypeOf(BoundExpression left, BoundExpression right) =>
        Strings.IsAnsi(left.Type) && left.ConstantValue is null ? left.Type : right.Type;

    /// <summary>The short-string operand type a constant converts to (the other operand's).</summary>
    private static TypeSymbol ShortTypeOf(BoundExpression left, BoundExpression right) =>
        Strings.IsShort(left.Type) && left.ConstantValue is null ? left.Type : right.Type;

    /// <summary>A constant converted to <paramref name="type"/>; a short-string variable stays as it is (no copy for a comparison).</summary>
    private BoundExpression AsType(BoundExpression operand, TypeSymbol type) =>
        operand.ConstantValue is null ? operand : ConvertForAssignment(operand, type, operand.Syntax);

    /// <summary>
    /// LANG-243 <c>+</c> with a string operand (C-061/C-063): two AnsiString-family operands give the left one's type
    /// (<c>_AStrCat</c>); two short strings a <c>ShortString</c> (built as a UnicodeString concatenation converted back, which
    /// truncates at 255 bytes); anything else a UnicodeString of the operands converted to it. E2011 for other operand types.
    /// </summary>
    private BoundExpression Concatenation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        if (!IsStringOperand(left.Type) || !IsStringOperand(right.Type))
        {
            return Inapplicable(binary, left, right);
        }

        switch (StringOperandKind(left, right))
        {
            case StringVariant.Ansi:
            {
                var type = AnsiTypeOf(left, right);
                return new BoundBinaryExpression(binary, BoundBinaryOperatorKind.Add, AsType(left, type), AsType(right, type), type, type, constantValue: null);
            }

            case StringVariant.Short:
            {
                var unicode = new BoundBinaryExpression(binary, BoundBinaryOperatorKind.Add, AsString(left), AsString(right), _types.String, _types.String, constantValue: null);
                return new BoundConversionExpression(binary, unicode, _types.ShortString, ConversionKind.ImplicitString, isExplicit: false, constantValue: null);
            }

            default:
                return new BoundBinaryExpression(binary, BoundBinaryOperatorKind.Add, AsString(left), AsString(right), _types.String, _types.String, constantValue: null);
        }
    }

    /// <summary>A UnicodeString or character operand as a UnicodeString (LANG-245; a constant character folds to a string constant).</summary>
    private BoundExpression AsString(BoundExpression operand) => ConvertForAssignment(operand, _types.String, operand.Syntax);

    /// <summary>LANG-226/230: Single when both are Single (or a Single and an integer constant exact in Single), else Double.</summary>
    private BoundBinaryExpression RealOperation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var type = RealOperandType(left, right, binary.OperatorToken.Kind);
        return new BoundBinaryExpression(binary, OperatorKind(binary.OperatorToken), Convert(left, type), Convert(right, type), type, type, constantValue: null);
    }

    /// <summary>
    /// LANG-305: <c>and</c>/<c>or</c> short-circuit under <c>{$B-}</c>; C-style Booleans are normalised (LANG-215). LANG-205: two
    /// operands of one distinct Boolean type yield that type, anything else <c>Boolean</c>.
    /// </summary>
    private BoundBinaryExpression BooleanOperation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var op = binary.OperatorToken;
        var kind = OperatorKind(op);
        if (!op.DirectiveState.BoolEval)
        {
            kind = kind switch
            {
                BoundBinaryOperatorKind.And => BoundBinaryOperatorKind.ConditionalAnd,
                BoundBinaryOperatorKind.Or => BoundBinaryOperatorKind.ConditionalOr,
                _ => kind,
            };
        }

        var boolean = _types.Boolean;
        return new BoundBinaryExpression(binary, kind, Convert(left, boolean), Convert(right, boolean), boolean,
            ConstantFolder.LogicalResultType(left.Type, right.Type, boolean), constantValue: null);
    }

    /// <summary>LANG-301…302: integers (mathematically), reals, Booleans, chars of one kind.</summary>
    private BoundExpression Relational(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var kind = OperatorKind(binary.OperatorToken);
        var boolean = _types.Boolean;
        var (lt, rt) = (left.Type, right.Type);
        if (lt is StringTypeSymbol || rt is StringTypeSymbol)
        {
            // LANG-244: ordinal comparison; two byte strings of one kind compare as such (_AStrCmp / _SStrCmp), everything else as
            // UnicodeStrings (a character operand becomes a 1-char string, C-061/C-063)
            if (!IsStringOperand(lt) || !IsStringOperand(rt))
            {
                return Inapplicable(binary, left, right);
            }

            return StringOperandKind(left, right) switch
            {
                StringVariant.Ansi => new BoundBinaryExpression(binary, kind, AsType(left, AnsiTypeOf(left, right)), AsType(right, AnsiTypeOf(left, right)),
                    AnsiTypeOf(left, right), boolean, constantValue: null),
                StringVariant.Short => new BoundBinaryExpression(binary, kind, AsType(left, ShortTypeOf(left, right)), AsType(right, ShortTypeOf(left, right)),
                    _types.ShortString, boolean, constantValue: null),
                _ => new BoundBinaryExpression(binary, kind, AsString(left), AsString(right), _types.String, boolean, constantValue: null),
            };
        }

        if (lt is DynamicArrayTypeSymbol || rt is DynamicArrayTypeSymbol)
        {
            // LANG-257: = and <> compare references; the same type, or nil (C-064)
            var sameOrNil = ReferenceEquals(lt, rt) || lt is DynamicArrayTypeSymbol && right.Type == _types.Nil || rt is DynamicArrayTypeSymbol && left.Type == _types.Nil;
            if (sameOrNil && kind is BoundBinaryOperatorKind.Equal or BoundBinaryOperatorKind.NotEqual)
            {
                return new BoundBinaryExpression(binary, kind, left, right, lt is DynamicArrayTypeSymbol ? lt : rt, boolean, constantValue: null);
            }

            return Inapplicable(binary, left, right);
        }

        if (lt is MethodPointerTypeSymbol || rt is MethodPointerTypeSymbol)
        {
            // LANG-481 (C-103): = and <> against nil test the Code part
            if ((lt is MethodPointerTypeSymbol && right.Type == _types.Nil || rt is MethodPointerTypeSymbol && left.Type == _types.Nil)
                && kind is BoundBinaryOperatorKind.Equal or BoundBinaryOperatorKind.NotEqual)
            {
                BoundExpression Code(BoundExpression e) => e.Type is MethodPointerTypeSymbol
                    ? new BoundConversionExpression(e.Syntax, e, _types.Pointer, ConversionKind.Explicit, isExplicit: true, constantValue: null)
                    : e;
                return new BoundBinaryExpression(binary, kind, Code(left), Code(right), _types.Pointer, boolean, constantValue: null);
            }

            // C-207: two method pointers of compatible types compare Code and Data
            if (lt is MethodPointerTypeSymbol l && rt is MethodPointerTypeSymbol r && Signatures.Compatible(l.Signature, r.Signature)
                && kind is BoundBinaryOperatorKind.Equal or BoundBinaryOperatorKind.NotEqual)
            {
                return new BoundBinaryExpression(binary, kind, left, right, lt, boolean, constantValue: null);
            }

            return Inapplicable(binary, left, right);
        }

        if (lt is InterfaceTypeSymbol || rt is InterfaceTypeSymbol)
        {
            // LANG-666 (C-100): = and <> compare the interface pointers (any two interfaces), or with nil
            var comparable = lt is InterfaceTypeSymbol && (rt is InterfaceTypeSymbol || right.Type == _types.Nil) || rt is InterfaceTypeSymbol && left.Type == _types.Nil;
            if (comparable && kind is BoundBinaryOperatorKind.Equal or BoundBinaryOperatorKind.NotEqual)
            {
                return new BoundBinaryExpression(binary, kind, left, right, lt is InterfaceTypeSymbol ? lt : rt, boolean, constantValue: null);
            }

            return Inapplicable(binary, left, right);
        }

        if ((lt is ClassTypeSymbol or ClassRefTypeSymbol && Conversions.IsUntypedPointer(rt) || rt is ClassTypeSymbol or ClassRefTypeSymbol && Conversions.IsUntypedPointer(lt))
            && kind is BoundBinaryOperatorKind.Equal or BoundBinaryOperatorKind.NotEqual)
        {
            // C-251 (Q-LANG-10.1): a class instance or class reference compared with a Pointer compares the addresses
            return new BoundBinaryExpression(binary, kind, Convert(left, _types.Pointer), Convert(right, _types.Pointer), _types.Pointer, boolean, constantValue: null);
        }

        if (lt is ClassTypeSymbol || rt is ClassTypeSymbol)
        {
            // LANG-600: = and <> compare references of related classes, or with nil (C-080)
            var comparable = (lt, rt) switch
            {
                (ClassTypeSymbol l, ClassTypeSymbol r) => l.InheritsFrom(r) || r.InheritsFrom(l),
                (ClassTypeSymbol, _) => right.Type == _types.Nil,
                _ => left.Type == _types.Nil,
            };
            if (comparable && kind is BoundBinaryOperatorKind.Equal or BoundBinaryOperatorKind.NotEqual)
            {
                return new BoundBinaryExpression(binary, kind, left, right, lt is ClassTypeSymbol ? lt : rt, boolean, constantValue: null);
            }

            return Inapplicable(binary, left, right);
        }

        if (lt is ClassRefTypeSymbol || rt is ClassRefTypeSymbol)
        {
            // LANG-652: = and <> compare class references of related classes, or with nil (C-086)
            var comparable = (lt, rt) switch
            {
                (ClassRefTypeSymbol l, ClassRefTypeSymbol r) => l.Class.InheritsFrom(r.Class) || r.Class.InheritsFrom(l.Class),
                (ClassRefTypeSymbol, _) => right.Type == _types.Nil,
                _ => left.Type == _types.Nil,
            };
            if (comparable && kind is BoundBinaryOperatorKind.Equal or BoundBinaryOperatorKind.NotEqual)
            {
                return new BoundBinaryExpression(binary, kind, left, right, lt is ClassRefTypeSymbol ? lt : rt, boolean, constantValue: null);
            }

            return Inapplicable(binary, left, right);
        }

        if (lt is ProceduralTypeSymbol || rt is ProceduralTypeSymbol)
        {
            // LANG-480 (C-161): = and <> compare code addresses of compatible procedural values, or with nil
            if (IsProceduralComparison(lt, rt) && kind is BoundBinaryOperatorKind.Equal or BoundBinaryOperatorKind.NotEqual)
            {
                return new BoundBinaryExpression(binary, kind, left, right, lt is ProceduralTypeSymbol ? lt : rt, boolean, constantValue: null);
            }

            return Inapplicable(binary, left, right);
        }

        if (lt is IntegerTypeSymbol && rt is IntegerTypeSymbol)
        {
            var (l, r) = AdaptedComputationTypes(left, right);
            var type = Combine(l, r, out var mixesSigns);
            if (mixesSigns)
            {
                // LANG-302: signed vs unsigned 64-bit compares mathematically, without a warning
                return new BoundBinaryExpression(binary, kind, Convert(left, Signed64(l)), Convert(right, Signed64(r)), type, boolean, constantValue: null);
            }

            return new BoundBinaryExpression(binary, kind, Convert(left, type), Convert(right, type), type, boolean, constantValue: null);
        }

        if (IsNumeric(lt) && IsNumeric(rt))
        {
            var type = RealOperandType(left, right, binary.OperatorToken.Kind);
            return new BoundBinaryExpression(binary, kind, Convert(left, type), Convert(right, type), type, boolean, constantValue: null);
        }

        if (lt is BooleanTypeSymbol && rt is BooleanTypeSymbol)
        {
            return new BoundBinaryExpression(binary, kind, Convert(left, boolean), Convert(right, boolean), boolean, boolean, constantValue: null);
        }

        if (lt is PointerTypeSymbol lp && rt is PointerTypeSymbol rp && Pointers.Compatible(lp, rp))
        {
            // LANG-266/267: = and <> for compatible pointers (nil included); ordering where pointer arithmetic is allowed (LANG-247)
            var ordering = kind is not (BoundBinaryOperatorKind.Equal or BoundBinaryOperatorKind.NotEqual);
            if (!ordering || AllowsPointerMath(lp, binary.OperatorToken) && AllowsPointerMath(rp, binary.OperatorToken))
            {
                var pointer = _types.Pointer;
                return new BoundBinaryExpression(binary, kind, Convert(left, pointer), Convert(right, pointer), pointer, boolean, constantValue: null);
            }
        }

        if (lt is EnumTypeSymbol && rt is EnumTypeSymbol)
        {
            // LANG-303: one enumeration (subranges compare in their enumeration)
            if (!Ordinals.SameHost(lt, rt))
            {
                _diagnostics.Report(binary.OperatorToken, "E2010", lt.Name, rt.Name);
                return Error(binary, left, right);
            }

            var host = Ordinals.Host(lt);
            return new BoundBinaryExpression(binary, kind, Convert(left, host), Convert(right, host), host, boolean, constantValue: null);
        }

        if (lt is CharTypeSymbol && rt is CharTypeSymbol)
        {
            // one char kind (LANG-280 rule 4, subranges in their char type); a character constant converts when its code
            // fits the other kind
            var type = Ordinals.SameHost(lt, rt) ? Ordinals.Host(lt)
                : FitsCharType(left, rt) ? Ordinals.Host(rt)
                : FitsCharType(right, lt) ? Ordinals.Host(lt)
                : null;
            if (type != null)
            {
                return new BoundBinaryExpression(binary, kind, Convert(left, type), Convert(right, type), type, boolean, constantValue: null);
            }
        }

        return Inapplicable(binary, left, right);
    }

    /// <summary>Two compatible procedural types (LANG-483), or one procedural type and <c>nil</c>.</summary>
    private bool IsProceduralComparison(TypeSymbol left, TypeSymbol right) => (left, right) switch
    {
        (ProceduralTypeSymbol l, ProceduralTypeSymbol r) => Signatures.Compatible(l.Signature, r.Signature),
        (ProceduralTypeSymbol, _) => right == _types.Nil,
        (_, ProceduralTypeSymbol) => left == _types.Nil,
        _ => false,
    };

    private TypeSymbol Signed64(TypeSymbol computationType) =>
        ((IntegerTypeSymbol)computationType).IsSigned ? _types.Int64 : _types.UInt64;

    private static bool FitsCharType(BoundExpression expression, TypeSymbol type) =>
        expression.ConstantValue?.Value is BigInteger code && code <= ((CharTypeSymbol)type).MaxValue;

    private BoundErrorExpression Inapplicable(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var op = binary.OperatorToken;
        _diagnostics.Report(op, "E2011", op.Text, $"'{left.Type.Name}' and '{right.Type.Name}'");
        return Error(binary, left, right);
    }

    /// <summary>
    /// LANG-275 rules 2, 4–6 (C-390): <c>L + R</c> with a dynamic-array operand. Two dynamic arrays with identical element types join
    /// (the left operand's type; <c>nil</c> becomes the empty array of the other operand's type); a static array, an open-array or
    /// array-of-const parameter beside a dynamic array is E2044 at that operand; everything else E2011. The value is a new array
    /// (LANG-276), lowered as one <c>_DynArrayCat</c> per chain (CMP-1417).
    /// </summary>
    private BoundExpression ArrayConcatenation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right) =>
        JoinArrays(binary, binary.OperatorToken, binary.Left, binary.Right, left, right, binary.OperatorToken.Text);

    /// <summary>
    /// The typing of <see cref="ArrayConcatenation"/> for <c>left + right</c> (also a step of an array <c>Concat</c>, LANG-279): the
    /// node <paramref name="syntax"/>, E2011 at <paramref name="at"/> naming <paramref name="op"/>, E2044 at the operand's syntax.
    /// </summary>
    private BoundExpression JoinArrays(SyntaxNode syntax, SyntaxNodeOrToken at, ExpressionSyntax leftSyntax, ExpressionSyntax rightSyntax,
        BoundExpression left, BoundExpression right, string op)
    {
        if (left.Type is DynamicArrayTypeSymbol && right.Type == _types.Nil)
        {
            right = ConvertForAssignment(right, left.Type, rightSyntax);
        }
        else if (right.Type is DynamicArrayTypeSymbol && left.Type == _types.Nil)
        {
            left = ConvertForAssignment(left, right.Type, leftSyntax);
        }

        if (left.Type is DynamicArrayTypeSymbol l && right.Type is DynamicArrayTypeSymbol r && Pointers.SameType(l.ElementType, r.ElementType))
        {
            return new BoundBinaryExpression(syntax, BoundBinaryOperatorKind.Add, left, right, l, l, constantValue: null);
        }

        var (other, operand) = left.Type is DynamicArrayTypeSymbol ? (right, rightSyntax) : (left, leftSyntax);
        if (other.Type is ArrayTypeSymbol or OpenArrayTypeSymbol)
        {
            _diagnostics.Report(operand, "E2044", other.Type.Name);
            return Error(syntax, left, right);
        }

        var message = $"'{left.Type.Name}' and '{right.Type.Name}'";
        if (at.AsNode() is { } node)
        {
            _diagnostics.Report(node, "E2011", op, message);
        }
        else
        {
            _diagnostics.Report(at.AsToken(), "E2011", op, message);
        }

        return Error(syntax, left, right);
    }

    private static BoundBinaryOperatorKind OperatorKind(SyntaxToken op) => op.Kind switch
    {
        SyntaxKind.PlusToken => BoundBinaryOperatorKind.Add,
        SyntaxKind.MinusToken => BoundBinaryOperatorKind.Subtract,
        SyntaxKind.AsteriskToken => BoundBinaryOperatorKind.Multiply,
        SyntaxKind.SlashToken => BoundBinaryOperatorKind.Divide,
        SyntaxKind.DivKeyword => BoundBinaryOperatorKind.IntegerDivide,
        SyntaxKind.ModKeyword => BoundBinaryOperatorKind.Modulo,
        SyntaxKind.AndKeyword => BoundBinaryOperatorKind.And,
        SyntaxKind.OrKeyword => BoundBinaryOperatorKind.Or,
        SyntaxKind.XorKeyword => BoundBinaryOperatorKind.Xor,
        SyntaxKind.ShlKeyword => BoundBinaryOperatorKind.Shl,
        SyntaxKind.ShrKeyword => BoundBinaryOperatorKind.Shr,
        SyntaxKind.EqualsToken => BoundBinaryOperatorKind.Equal,
        SyntaxKind.LessThanGreaterThanToken => BoundBinaryOperatorKind.NotEqual,
        SyntaxKind.LessThanToken => BoundBinaryOperatorKind.Less,
        SyntaxKind.GreaterThanToken => BoundBinaryOperatorKind.Greater,
        SyntaxKind.LessThanEqualsToken => BoundBinaryOperatorKind.LessOrEqual,
        SyntaxKind.GreaterThanEqualsToken => BoundBinaryOperatorKind.GreaterOrEqual,
        SyntaxKind.InKeyword => BoundBinaryOperatorKind.In,
        _ => throw new InvalidOperationException($"Not a binary operator: {op.Kind}"),
    };

    // ------------------------------------------------------------------ sets (LANG-250…252)

    /// <summary>
    /// <c>+ - *</c> and <c>= &lt;&gt; &lt;= &gt;=</c> on compatible sets (E2010 otherwise), done in <see cref="Sets.Combine"/>;
    /// <c>&lt; &gt;</c> and a set with a non-set are E2011.
    /// </summary>
    private BoundExpression SetOperation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var op = binary.OperatorToken;
        if (left.Type is not SetTypeSymbol l || right.Type is not SetTypeSymbol r
            || op.Kind is not (SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.AsteriskToken or SyntaxKind.EqualsToken
                or SyntaxKind.LessThanGreaterThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanEqualsToken))
        {
            return Inapplicable(binary, left, right);
        }

        if (Conversions.AnsiCharSetConstant(left.ConstantValue, r) is { } leftAnsi)
        {
            left = new BoundLiteral(left.Syntax, leftAnsi);                          // C-200: ['a'] + S with S a set of AnsiChar
            l = r;
        }
        else if (Conversions.AnsiCharSetConstant(right.ConstantValue, l) is { } rightAnsi)
        {
            right = new BoundLiteral(right.Syntax, rightAnsi);
            r = l;
        }

        if (!Sets.Compatible(l, r))
        {
            _diagnostics.Report(op, "E2010", l.Name, r.Name);
            return Error(binary, left, right);
        }

        var type = Sets.Combine(l, r);
        var result = op.Kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.AsteriskToken ? type : _types.Boolean;
        return new BoundBinaryExpression(binary, OperatorKind(op), Convert(left, type), Convert(right, type), type, result, constantValue: null);
    }

    /// <summary>LANG-252 <c>x in S</c>: an ordinal of the set's element type (E2010 at <c>in</c>); the element is not converted.</summary>
    private BoundExpression BindIn(BinaryExpressionSyntax binary, BoundExpression element, BoundExpression set)
    {
        if (set.Type is not SetTypeSymbol type || !element.Type.IsOrdinal)
        {
            return Inapplicable(binary, element, set);
        }

        if (element.ConstantValue is { } constant && ConstantFolder.IsAnsiCharConstant(constant, type))
        {
            // LANG-262 (C-200): the character constant becomes an AnsiChar of the set's base type
            element = new BoundLiteral(element.Syntax, new ConstantValue(constant.Value, Ordinals.Host(type.ElementType!)));
        }

        if (!Sets.AcceptsElement(type, element.Type))
        {
            _diagnostics.Report(binary.OperatorToken, "E2010", element.Type.Name, type.ElementType!.Name);
            return Error(binary, element, set);
        }

        return new BoundBinaryExpression(binary, BoundBinaryOperatorKind.In, element, set, type, _types.Boolean, constantValue: null);
    }

    /// <summary>
    /// LANG-251 <c>[a, b..c]</c> of type <paramref name="expected"/>, or without context of its natural type
    /// (<see cref="ConstantFolder.NaturalSetType"/>). Constant elements are checked against the element type (E2013, E2010,
    /// E2012); other elements must be ordinals of a compatible type and stay unconverted (the lowerer guards or checks them).
    /// </summary>
    private BoundExpression BindSetConstructor(SetConstructorSyntax syntax, SetTypeSymbol? expected)
    {
        var elements = ImmutableArray.CreateBuilder<BoundSetElement>(syntax.Elements.Count);
        var children = new List<BoundExpression>();
        foreach (var element in syntax.Elements)
        {
            var low = BindExpression(element.Expression);
            var high = element.UpperBound is { } upper ? BindExpression(upper) : null;
            children.Add(low);
            if (high != null)
            {
                children.Add(high);
            }

            elements.Add(new BoundSetElement(low, high));
        }

        if (children.Any(c => c.HasErrors))
        {
            return Error(syntax, [.. children]);
        }

        if (children.Count == 0)
        {
            var empty = expected ?? _types.EmptySet;
            return new BoundSetConstructor(syntax, [], empty, new ConstantValue(BigInteger.Zero, empty));
        }

        var ordinals = children.Select(c => c.ConstantValue is { } value && c.Type.IsOrdinal ? OrdinalOf(value) : (BigInteger?)null).ToList();
        var constant = ordinals.All(o => o is not null);
        var type = expected ?? _folder.NaturalSetType(children[0].Type, constant ? ordinals.Min() : null, constant ? ordinals.Max() : null);
        var ok = true;
        foreach (var child in children)
        {
            ok &= child.ConstantValue is { } value ? CheckSetElement(type, value, child.Syntax) : CheckSetElement(type, child, child.Syntax);
        }

        if (!ok)
        {
            return Error(syntax, [.. children]);
        }

        // LANG-262 (C-200): character constants in a set of AnsiChar are AnsiChar elements
        BoundExpression Element(BoundExpression e) => e.ConstantValue is { } v && ConstantFolder.IsAnsiCharConstant(v, type)
            ? new BoundLiteral(e.Syntax, new ConstantValue(v.Value, Ordinals.Host(type.ElementType!))) : e;
        var built = elements.Select(e => new BoundSetElement(Element(e.Low), e.High is null ? null : Element(e.High))).ToImmutableArray();
        var mask = constant
            ? new ConstantValue(ConstantFolder.SetMask(type, built.Select(e => (OrdinalOf(e.Low.ConstantValue!), OrdinalOf((e.High ?? e.Low).ConstantValue!)))), type)
            : null;
        return new BoundSetConstructor(syntax, built, type, mask);
    }

    /// <summary>A constant set element: an ordinal in the element type's range (E2013, E2010, E2012 at <paramref name="at"/>).</summary>
    public bool CheckSetElement(SetTypeSymbol set, ConstantValue value, SyntaxNode at) => _folder.CheckSetElement(set, value, at);

    /// <summary>A run-time set element: an ordinal of a type the set accepts (E2013, E2010 at <paramref name="at"/>).</summary>
    public bool CheckSetElement(SetTypeSymbol set, BoundExpression element, SyntaxNode at)
    {
        if (!element.Type.IsOrdinal)
        {
            _diagnostics.Report(at, "E2013");
            return false;
        }

        if (!Sets.AcceptsElement(set, element.Type))
        {
            _diagnostics.Report(at, "E2010", element.Type.Name, set.ElementType!.Name);
            return false;
        }

        return true;
    }

    private static BigInteger OrdinalOf(ConstantValue value) => value.Value is bool b ? (b ? BigInteger.One : BigInteger.Zero) : (BigInteger)value.Value!;

    // ------------------------------------------------------------------ typing rules

    /// <summary>LANG-222: Integer, Cardinal, Int64 or UInt64.</summary>
    private TypeSymbol ComputationType(IntegerTypeSymbol type) => _folder.ComputationTypeSymbol(type);

    /// <summary>LANG-222: an integer constant takes the other operand's computation type when its value fits there.</summary>
    private (TypeSymbol Left, TypeSymbol Right) AdaptedComputationTypes(BoundExpression left, BoundExpression right)
    {
        var lt = ComputationType((IntegerTypeSymbol)left.Type);
        var rt = ComputationType((IntegerTypeSymbol)right.Type);
        // C-208: never narrowed from 64 to 32 bits — a constant typed Int64/UInt64 (Int64(86400000)) keeps its width
        if (left.ConstantValue?.Value is BigInteger l && Fits(l, rt) && rt.Size >= lt.Size)
        {
            lt = rt;
        }
        else if (right.ConstantValue?.Value is BigInteger r && Fits(r, lt) && lt.Size >= rt.Size)
        {
            rt = lt;
        }

        return (lt, rt);
    }

    /// <summary>LANG-221 table; <paramref name="mixesSigns"/> = a signed operand meets UInt64 (W1010).</summary>
    private TypeSymbol Combine(TypeSymbol left, TypeSymbol right, out bool mixesSigns)
    {
        mixesSigns = false;
        if (ReferenceEquals(left, right))
        {
            return left;
        }

        if (ReferenceEquals(left, _types.UInt64) || ReferenceEquals(right, _types.UInt64))
        {
            var other = (IntegerTypeSymbol)(ReferenceEquals(left, _types.UInt64) ? right : left);
            mixesSigns = other.IsSigned;
            return _types.UInt64;
        }

        return _types.Int64;                                            // Integer x Cardinal, or one side Int64
    }

    /// <summary>
    /// LANG-226/230; C-218: <c>+</c>, <c>-</c> and comparisons of Currency with Currency or integer operands compute in Currency
    /// (exact scaled Int64), every other operation with a Currency operand in Double.
    /// </summary>
    private TypeSymbol RealOperandType(BoundExpression left, BoundExpression right, SyntaxKind op)
    {
        static bool IsCurrency(TypeSymbol type) => type is RealTypeSymbol { RealKind: RealKind.Currency };
        if (IsCurrency(left.Type) || IsCurrency(right.Type))
        {
            var exact = op is not (SyntaxKind.AsteriskToken or SyntaxKind.SlashToken)
                && left.Type is IntegerTypeSymbol or RealTypeSymbol { RealKind: RealKind.Currency }
                && right.Type is IntegerTypeSymbol or RealTypeSymbol { RealKind: RealKind.Currency };
            return exact ? _types.Currency : _types.Double;
        }

        return IsSingleOperand(left, right) && IsSingleOperand(right, left) ? _types.Single : _types.Double;
    }

    /// <summary>Single, or an integer constant exactly representable in Single beside a Single operand (LANG-230).</summary>
    private static bool IsSingleOperand(BoundExpression operand, BoundExpression other) => operand.Type switch
    {
        RealTypeSymbol { RealKind: RealKind.Single } => true,
        IntegerTypeSymbol when other.Type is RealTypeSymbol { RealKind: RealKind.Single } && operand.ConstantValue?.Value is BigInteger value
            => new BigInteger((float)value) == value,
        _ => false,
    };

    private static bool IsNumeric(TypeSymbol type) => type is IntegerTypeSymbol or RealTypeSymbol;

    private static bool Fits(BigInteger value, TypeSymbol type) =>
        type is IntegerTypeSymbol integer && value >= integer.Min && value <= integer.Max;

    // ------------------------------------------------------------------ conversions and errors

    /// <summary>An implicit conversion of <paramref name="expression"/> to <paramref name="type"/> (none when it already has it).</summary>
    private static BoundExpression Convert(BoundExpression expression, TypeSymbol type)
    {
        if (ReferenceEquals(expression.Type, type))
        {
            return expression;
        }

        var constant = expression.ConstantValue is { } value ? ConvertConstant(value, type) : null;
        return new BoundConversionExpression(expression.Syntax, expression, type, ConversionKind.ImplicitNumeric, isExplicit: false, constant);
    }

    private static ConstantValue ConvertConstant(ConstantValue value, TypeSymbol type) => type switch
    {
        RealTypeSymbol { RealKind: RealKind.Single } => new ConstantValue((double)(float)RealValue(value), type),
        RealTypeSymbol { RealKind: RealKind.Currency } => new ConstantValue(Conversions.CurrencyValue(RealValue(value)), type),
        RealTypeSymbol => new ConstantValue(RealValue(value), type),
        _ => new ConstantValue(value.Value, type),
    };

    private static double RealValue(ConstantValue value) => value.Value is BigInteger i ? (double)i : (double)value.Value!;

    private BoundErrorExpression Error(SyntaxNode syntax, params BoundExpression[] children) =>
        new(syntax, _types.Error, [.. children]);

    /// <summary>E2900 for a construct of a later milestone (nothing when the syntax is already erroneous).</summary>
    private BoundErrorExpression Unsupported(SyntaxNode syntax, string feature)
    {
        if (!syntax.ContainsDiagnostics)
        {
            _diagnostics.Report(syntax, "E2900", feature);
        }

        return Error(syntax);
    }

    private BoundErrorExpression Unsupported(SyntaxToken op, BinaryExpressionSyntax binary, string feature)
    {
        _diagnostics.Report(op, "E2900", feature);
        return Error(binary);
    }
}
