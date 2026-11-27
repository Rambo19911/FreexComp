using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// LANG-979 (C-418): type-argument inference for a call of a generic method by unification of its declared parameter types with the
/// argument types. The one implementation for the binder (<c>ExpressionBinder.GenericMethodCandidates</c>) and the semantic model
/// (<c>SemanticModel.GetGenericMethodCandidates</c>), so the compiler and code insight never infer differently (CMP-1477 (1)).
/// The declared parameter types are those of the generic method's open instance (its heading bound with each type parameter as a
/// <see cref="TypeParameterTypeSymbol"/>, <see cref="DeclarationBinder.OpenInstanceOf(BuiltInTypes, GenericMethodSymbol)"/>).
/// </summary>
internal static class GenericInference
{
    /// <summary>
    /// What one argument of the call gives the inference (LANG-979 rule 1): its argument type; for a bracket constructor the natural
    /// types of its elements, each examined alone (empty when one has none); nothing (<c>default</c>) for a missing argument, <c>nil</c>,
    /// an anonymous method or an argument that does not bind.
    /// </summary>
    internal readonly record struct Argument(TypeSymbol? Type, ImmutableArray<TypeSymbol> Elements)
    {
        public static Argument Of(TypeSymbol type) => new(type, default);

        public static Argument Constructor(ImmutableArray<TypeSymbol> elements) => new(null, elements);
    }

    /// <summary>
    /// The natural type of an argument (LANG-222 as LANG-714 applies it): an integer constant is <c>Integer</c> when it fits, else
    /// <c>Int64</c>; a real constant is <c>Double</c>; otherwise the type of the expression.
    /// </summary>
    public static TypeSymbol Natural(BuiltInTypes types, TypeSymbol type, ConstantValue? constant) => constant?.Value switch
    {
        BigInteger value when type is IntegerTypeSymbol => value >= int.MinValue && value <= int.MaxValue ? types.Integer : types.Int64,
        double when type is RealTypeSymbol => types.Double,
        _ => type,
    };

    /// <summary><paramref name="type"/> is, or is built from, one of <paramref name="typeParameters"/>: an argument for it can bind one.</summary>
    public static bool Mentions(TypeSymbol type, ImmutableArray<TypeSymbol> typeParameters) =>
        typeParameters.Contains(type, ReferenceEqualityComparer.Instance)
        || OpenArrayTypeSymbol.ElementOf(type) is { } element && Mentions(element, typeParameters)
        || type.TypeArguments.Any(a => Mentions(a, typeParameters));

    /// <summary>A procedural, method-pointer or method-reference type: a routine name or an anonymous method is a value of it only in its context.</summary>
    public static bool IsProceduralLike(TypeSymbol type) =>
        type is ProceduralTypeSymbol or MethodPointerTypeSymbol or InterfaceTypeSymbol { IsMethodReference: true };

    /// <summary>
    /// LANG-979 rules 2…4: the type arguments of the generic method whose open instance is <paramref name="open"/> for the arguments
    /// <paramref name="argumentAt"/> gives by parameter position; null when a type parameter stays unbound or is bound to two types
    /// that are not identical (LANG-206). Neither the result type nor the receiver takes part.
    /// </summary>
    public static ImmutableArray<TypeSymbol>? Infer(MethodSymbol open, Func<int, Argument> argumentAt)
    {
        var typeParameters = open.GenericMethodArguments;
        var bound = new TypeSymbol?[typeParameters.Length];
        for (var i = 0; i < open.Parameters.Length; i++)
        {
            var parameter = open.Parameters[i].Type;
            var argument = argumentAt(i);
            if (argument.Type is { TypeKind: not TypeKind.Error } type)
            {
                if (!Unify(parameter, type, typeParameters, bound))
                {
                    return null;
                }
            }
            else if (parameter is OpenArrayTypeSymbol { IsArrayOfConst: false } openArray && argument.Elements is { IsDefaultOrEmpty: false } elements
                     && elements.All(e => Pointers.SameType(e, elements[0])))
            {
                // a bracket constructor with at least one element, all of one natural type E: array of P' against E
                if (!Unify(openArray.ElementType, elements[0], typeParameters, bound))
                {
                    return null;
                }
            }
        }

        return bound.Any(t => t is null) ? null : [.. bound!];
    }

    /// <summary>The table of LANG-979 rule 2; false on a conflict (rule 3). A parameter type that does not fit binds nothing.</summary>
    private static bool Unify(TypeSymbol parameter, TypeSymbol argument, ImmutableArray<TypeSymbol> typeParameters, TypeSymbol?[] bound)
    {
        var index = typeParameters.IndexOf(parameter, 0, typeParameters.Length, ReferenceEqualityComparer.Instance);
        if (index >= 0)
        {
            if (bound[index] is { } earlier)
            {
                return Pointers.SameType(earlier, argument);
            }

            bound[index] = argument;
            return true;
        }

        if (!Mentions(parameter, typeParameters))
        {
            return true;
        }

        switch (parameter)
        {
            case OpenArrayTypeSymbol { IsArrayOfConst: false } open:
                return OpenArrayTypeSymbol.ElementOf(argument) is not { } element || Unify(open.ElementType, element, typeParameters, bound);
            case DynamicArrayTypeSymbol dynamic:
                return argument is not DynamicArrayTypeSymbol array || Unify(dynamic.ElementType, array.ElementType, typeParameters, bound);
        }

        if (parameter.GenericDefinition is not { } definition || InstanceOf(definition, parameter, argument) is not { } instance
            || instance.TypeArguments.Length != parameter.TypeArguments.Length)
        {
            return true;
        }

        for (var j = 0; j < parameter.TypeArguments.Length; j++)
        {
            if (!Unify(parameter.TypeArguments[j], instance.TypeArguments[j], typeParameters, bound))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The instance of the generic <paramref name="definition"/> the argument type gives for the parameter type: the type itself; for a
    /// class the nearest ancestor that is an instance; for an interface the only instance among the interfaces the argument type is,
    /// descends from or implements. Null when there is none (or, for an interface, more than one).
    /// </summary>
    private static TypeSymbol? InstanceOf(TypeSymbol definition, TypeSymbol parameter, TypeSymbol argument)
    {
        if (ReferenceEquals(argument.GenericDefinition, definition))
        {
            return argument;
        }

        switch (parameter)
        {
            case ClassTypeSymbol when argument is ClassTypeSymbol @class:
                return @class.SelfAndAncestors().FirstOrDefault(c => ReferenceEquals(c.GenericDefinition, definition));
            case InterfaceTypeSymbol { IsMethodReference: false }:
                var interfaces = argument switch
                {
                    InterfaceTypeSymbol intf => intf.SelfAndAncestors(),
                    ClassTypeSymbol implementing => implementing.SelfAndAncestors().SelectMany(c => c.Interfaces).SelectMany(i => i.Interface.SelfAndAncestors()),
                    _ => Enumerable.Empty<InterfaceTypeSymbol>(),
                };
                var instances = interfaces.Where(i => ReferenceEquals(i.GenericDefinition, definition)).Distinct<TypeSymbol>(ReferenceEqualityComparer.Instance).ToList();
                return instances.Count == 1 ? instances[0] : null;
            default:
                return null;
        }
    }
}
