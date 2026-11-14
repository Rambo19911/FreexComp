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