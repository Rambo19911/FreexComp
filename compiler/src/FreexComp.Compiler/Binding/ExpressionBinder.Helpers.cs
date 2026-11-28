using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Member lookup through class and record helpers (LANG-752, 753; C-124).
internal sealed partial class ExpressionBinder
{
    /// <summary>
    /// LANG-752: the helper whose member <paramref name="name"/> a member access on a value of <paramref name="type"/> finds — the
    /// active helper of the type (LANG-753) when it declares the name, else (classes) the same for each ancestor until a type of
    /// the chain declares the name itself; null when the type's own members apply.
    /// </summary>
    private HelperTypeSymbol? HelperFor(TypeSymbol type, string name)
    {
        if (!_types.HasHelpers)
        {
            return null;
        }

        foreach (var level in TypeMembers.SelfAndAncestors(type))
        {
            if (_scope.ActiveHelper(level) is { } helper && helper.SelfAndAncestors().Any(h => h.GetMembers(name, includeInherited: false).Any()))
            {
                return helper;
            }

            if (level.GetMembers(name, includeInherited: false).Any())
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>The methods named <paramref name="name"/> of a helper and the helpers it inherits (nearest first, LANG-441).</summary>
    private static ImmutableArray<RoutineSymbol> HelperCandidates(HelperTypeSymbol helper, string name)
    {
        var candidates = ImmutableArray.CreateBuilder<RoutineSymbol>();
        foreach (var owner in helper.SelfAndAncestors())
        {
            var methods = owner.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, name)).ToList();
            candidates.AddRange(methods.Where(m => !candidates.Any(c => SameParameters(c, m))));
            if (methods.Count > 0 && methods.Any(m => !m.IsOverload))
            {
                break;
            }
        }

        return candidates.ToImmutable();
    }

    /// <summary>
    /// <c>x.Name</c> found in <paramref name="helper"/> (LANG-752): a method (sets <paramref name="methodCall"/>), property, class var or
    /// constant; <paramref name="instance"/> is the helped value, null when <c>x</c> names the helped type (static members only).
    /// </summary>
    private BoundExpression? HelperMember(MemberAccessExpressionSyntax syntax, SyntaxNode call, ArgumentListSyntax? argumentList,
        HelperTypeSymbol helper, BoundExpression? instance, string name, out MethodCall? methodCall)
    {
        methodCall = null;
        var candidates = HelperCandidates(helper, name);
        if (!candidates.IsEmpty)
        {
            methodCall = BindMethodCall(call, candidates, instance is null ? ReceiverKind.ClassName : ReceiverKind.Instance, instance, null, argumentList);
            return null;
        }

        switch (helper.GetMembers(name).FirstOrDefault())
        {
            case PropertySymbol property when instance is not null || property.IsClassProperty:
                return CheckAccess(syntax.Name, property, property.Owner, helper.ExtendedType)
                    ? PropertyAccess(syntax, property, property.IsClassProperty ? null : instance) : Error(syntax);
            case VariableSymbol classVar:
                return ClassVariable(syntax, syntax.Name, classVar, helper.ExtendedType);
            case { } nested when NestedValue(syntax, nested) is { } value:
                return value;
            default:
                _diagnostics.Report(syntax.Name, "E2607");
                return instance is null ? Error(syntax) : Error(syntax, instance);
        }
    }

    // ------------------------------------------------------------------ receivers of record-helper methods (LANG-996, C-443)

    /// <summary>
    /// LANG-996 (b): the record-helper instance method whose <c>Self</c> <paramref name="expression"/> is a part of — <c>Self</c>
    /// itself or a path from it through record fields, elements of static arrays and of strings and variable typecasts, also
    /// inside a routine nested in the method; null otherwise. A dereference, an object reference and an element of a dynamic
    /// array end the path: what lies behind them is other storage.
    /// </summary>
    internal static MethodSymbol? SelfPartOf(BoundExpression expression)
    {
        while (true)
        {
            switch (expression)
            {
                case BoundParameterExpression { Parameter: { ContainingSymbol: MethodSymbol { IsClassMethod: false, ContainingSymbol: HelperTypeSymbol { IsClassHelper: false } } method } self }
                    when ReferenceEquals(method.SelfParameter, self):
                    return method;
                case BoundFieldAccessExpression { Record.Type: not ClassTypeSymbol } field:
                    expression = field.Record;
                    break;
                case BoundIndexExpression { Array.Type: not (DynamicArrayTypeSymbol or PointerTypeSymbol) } index:
                    expression = index.Array;
                    break;
                case BoundVariableTypecastExpression typecast:
                    expression = typecast.Operand;
                    break;
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// LANG-996 (b) rules 1, 2, 3 and 5: <paramref name="target"/> is written, passed by reference, has its address taken or is a
    /// <c>with</c> designator — a write use of <c>Self</c> when it is a part of the <c>Self</c> of a record-helper method.
    /// </summary>
    public void NoteSelfWrite(BoundExpression target)
    {
        if (_types.HasHelpers && SelfPartOf(target) is { } method)
        {
            _diagnostics.SelfUses.Add(new SelfUse(method, Callee: null));
        }
    }

    private enum HelperReceiverKind { Location, ReadOnlyLocation, Constant, Value }

    /// <summary>
    /// LANG-996 (a): the kind of a receiver, tested in the order read-only location (a <c>const</c> parameter, a typed constant,
    /// the control variable of an enclosing loop, the target of a read-only <c>with</c>, a field or element of these), constant,
    /// location (what LANG-421 accepts as a <c>var</c> argument), value (anything else; a property read is a value).
    /// </summary>
    private HelperReceiverKind KindOfReceiver(BoundExpression receiver)
    {
        if (IsPropertyRead(receiver))
        {
            return HelperReceiverKind.Value;
        }

        switch (BoundIndexExpression.RootOf(receiver))
        {
            case BoundVariableExpression { Variable: var v } when ForVariables.Contains(v):
            case BoundParameterExpression { Parameter: var p } when ForVariables.Contains(p):
            case BoundVariableExpression { Variable.IsReadOnly: true }:
            case BoundDereferenceExpression { Pointer: BoundVariableExpression { Variable: var with } } when ReadOnlyWith.Contains(with):
            case BoundParameterExpression { Parameter.Mode: ParameterMode.Const }:
                return HelperReceiverKind.ReadOnlyLocation;
        }

        return receiver.ConstantValue is not null ? HelperReceiverKind.Constant
            : IsVariableArgument(receiver) ? HelperReceiverKind.Location
            : HelperReceiverKind.Value;
    }

    /// <summary>
    /// LANG-996: the receiver of an instance method of a record helper, called at <paramref name="at"/> (a method call or a property
    /// access). On a part of the calling helper method's own <c>Self</c> it is a write use of that <c>Self</c> when
    /// <paramref name="method"/> is mutating (rule 4). On a read-only location or a constant the call is E2752 at the member
    /// name when <paramref name="method"/> is mutating — recorded, as that is known only once every body is bound. A constant
    /// and a plain value are passed in a temporary. Other receivers pass unchanged.
    /// </summary>
    private BoundReceiver? HelperReceiver(SyntaxNode at, MethodSymbol method, BoundReceiver? receiver)
    {
        if (receiver?.Self is not { HasErrors: false } self || method.IsClassMethod || method.ContainingSymbol is not HelperTypeSymbol { IsClassHelper: false })
        {
            return receiver;
        }

        if (SelfPartOf(self) is { } caller)
        {
            _diagnostics.SelfUses.Add(new SelfUse(caller, method));
            return receiver;
        }

        var kind = KindOfReceiver(self);
        if (kind is HelperReceiverKind.ReadOnlyLocation or HelperReceiverKind.Constant)
        {
            _diagnostics.ReadOnlyCalls.Add(new ReadOnlyCall(method, Diagnostic.Create("E2752", DiagnosticBag.LocationOf(MemberName(at)), method.Name)));
        }

        return kind is HelperReceiverKind.Constant or HelperReceiverKind.Value ? receiver with { Temporary = true } : receiver;
    }

    /// <summary>The member name of a call or property access: <c>M</c> of <c>X.M</c>, <c>X.M(…)</c>, <c>X.P[…]</c>; the node itself otherwise.</summary>
    private static SyntaxNode MemberName(SyntaxNode at) => at switch
    {
        MemberAccessExpressionSyntax access => access.Name,
        InvocationExpressionSyntax { Expression: var callee } => MemberName(callee),
        ElementAccessExpressionSyntax { Expression: var indexed } => MemberName(indexed),
        ParenthesizedExpressionSyntax { Expression: var inner } => MemberName(inner),
        _ => at,
    };

    // ------------------------------------------------------------------ for…in enumerator pattern (LANG-362, C-125)

    /// <summary>The parameterless public methods named <paramref name="name"/> of a class, record or interface type.</summary>
    private static MethodSymbol? EnumeratorMethod(TypeSymbol type, string name)
    {
        ImmutableArray<RoutineSymbol> candidates = type switch
        {
            ClassTypeSymbol c => MemberCandidates(c, name),
            InterfaceTypeSymbol i => InterfaceCandidates(i, name),
            RecordTypeSymbol r => [.. r.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, name))],
            _ => [],
        };
        return candidates.OfType<MethodSymbol>().FirstOrDefault(m => m.Parameters.IsEmpty && !m.IsClassMethod
            && m.Visibility is Visibility.Public or Visibility.Published);
    }

    /// <summary>A readable, non-indexed public property named <paramref name="name"/> of a class, record or interface type.</summary>
    private static PropertySymbol? EnumeratorProperty(TypeSymbol type, string name)
    {
        IEnumerable<PropertySymbol> properties = type switch
        {
            ClassTypeSymbol c => c.SelfAndAncestors().SelectMany(a => a.Properties),
            InterfaceTypeSymbol i => i.SelfAndAncestors().SelectMany(a => a.Properties),
            RecordTypeSymbol r => r.Properties,
            _ => [],
        };
        return properties.FirstOrDefault(p => IdentifierComparer.Instance.Equals(p.Name, name) && !p.IsArrayProperty && !p.IsClassProperty
            && p.IsReadable && p.Visibility is Visibility.Public or Visibility.Published);
    }

    /// <summary><c>receiver.Name</c> as a value: a parameterless function call or a property read; null when there is none.</summary>
    public BoundExpression? EnumeratorCall(SyntaxNode at, BoundExpression receiver, string name)
    {
        if (EnumeratorMethod(receiver.Type, name) is { ReturnType: not null } method)
        {
            return Receiver(at, method, ReceiverKind.Instance, receiver, null) is { } self
                ? new BoundCallExpression(at, method, []) { Receiver = self }
                : null;
        }

        return EnumeratorProperty(receiver.Type, name) is { } property ? PropertyRead(at, property, receiver, []) : null;
    }

    /// <summary><c>receiver.Name;</c> as a statement (<c>Free</c> of a class enumerator); null when there is no such method.</summary>
    public BoundStatement? EnumeratorStatement(SyntaxNode at, BoundExpression receiver, string name) =>
        EnumeratorMethod(receiver.Type, name) is { } method && Receiver(at, method, ReceiverKind.Instance, receiver, null) is { } self
            ? new BoundCallStatement(at, method, []) { Receiver = self }
            : null;

    /// <summary>The type of <c>Current</c> of the enumerator <c>GetEnumerator</c> of <paramref name="collection"/> returns; null when none.</summary>
    public TypeSymbol? EnumeratorElementType(TypeSymbol collection)
    {
        if (EnumeratorMethod(collection, "GetEnumerator")?.ReturnType is not { } enumerator)
        {
            return null;
        }

        return EnumeratorMethod(enumerator, "Current")?.ReturnType ?? EnumeratorProperty(enumerator, "Current")?.Type;
    }
}