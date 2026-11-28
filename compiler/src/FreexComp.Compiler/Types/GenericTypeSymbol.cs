using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Types;

/// <summary>
/// A generic type definition <c>TList&lt;T&gt; = class … end</c> (LANG-700, C-116). It is no usable type itself: every use names
/// type arguments and gets the instance for them (<see cref="Instances"/>, one per distinct argument list, LANG-710). Instances are
/// made by binding <see cref="Declaration"/> again in <see cref="DeclaringScope"/> with the type parameters declared as the
/// arguments (template model, LANG-711: names not depending on a type parameter resolve in the declaring unit's scope).
/// It is declared in its scope under <see cref="ScopeName"/> (name and arity), so that <c>TFoo</c>, <c>TFoo&lt;T&gt;</c> and
/// <c>TFoo&lt;A, B&gt;</c> coexist (LANG-703).
/// </summary>
public sealed class GenericTypeSymbol : TypeSymbol
{
    internal GenericTypeSymbol(string name, ImmutableArray<string> parameterNames, TypeDeclarationSyntax? declaration, Scope? declaringScope,
        int visibleCount, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(TypeKind.GenericDefinition, name, size: -1, alignment: 1, containingSymbol, locations)
    {
        ParameterNames = parameterNames;
        Declaration = declaration;
        DeclaringScope = declaringScope;
        VisibleCount = visibleCount;
    }

    /// <summary>The type parameter names in declaration order.</summary>
    public ImmutableArray<string> ParameterNames { get; }

    /// <summary>LANG-702 (C-118): the constraints of each type parameter (bound with the declaration).</summary>
    internal ImmutableArray<TypeParameterConstraints> Constraints { get; set; } = [];

    /// <summary>
    /// The declaration (null for a definition imported from a <c>.pcu</c> without its body text, C-119); for a forward declaration
    /// <c>TNode&lt;T&gt; = class;</c> the forward until its completion replaces it (LANG-782, C-346).
    /// </summary>
    internal TypeDeclarationSyntax? Declaration { get; set; }

    /// <summary>
    /// LANG-782/783 (C-346): declared by <c>TNode&lt;T&gt; = class;</c> / <c>IFoo&lt;T&gt; = interface;</c> and not completed yet. Its
    /// instances are incomplete classes (interfaces) until then: <see cref="PendingInstances"/>, completed with the declaration.
    /// </summary>
    internal bool IsForward { get; set; }

    /// <summary>The instances made while <see cref="IsForward"/>, each with its first instantiation site and using unit.</summary>
    internal List<(TypeSymbol Instance, ImmutableArray<TypeSymbol> Arguments, SyntaxNode Site, UnitSymbol? Unit)> PendingInstances { get; } = [];

    /// <summary>The scope the generic is declared in; instances are bound below it.</summary>
    internal Scope? DeclaringScope { get; }

    /// <summary>The symbols of <see cref="DeclaringScope"/> visible to the declaration (LANG-402 declare-before-use).</summary>
    internal int VisibleCount { get; set; }

    /// <summary>The instances made so far, by argument list (identity of the argument types).</summary>
    internal Dictionary<ImmutableArray<TypeSymbol>, TypeSymbol> Instances { get; } = new(ArgumentsComparer.Instance);

    /// <summary>
    /// C-355: the instances made while the language service binds an open instance (<see cref="OpenInstance"/>, its method bodies):
    /// over <see cref="TypeParameterTypeSymbol"/> arguments, and every other instance those name. Kept apart from <see cref="Instances"/>,
    /// which code generation, the <c>.pcu</c> writer and debug information walk, so none of them is ever seen by a program.
    /// </summary>
    internal Dictionary<ImmutableArray<TypeSymbol>, TypeSymbol> OpenInstances { get; } = new(ArgumentsComparer.Instance);

    /// <summary>
    /// C-355: the instance over the generic's own type parameters (each a <see cref="TypeParameterTypeSymbol"/>), made on demand for the
    /// language service (<c>DeclarationBinder.OpenInstanceOf</c>); it stands for the definition inside its own declaration and method
    /// implementations. Null until made, or when it cannot be (no declaration text, a forward declaration).
    /// </summary>
    internal TypeSymbol? OpenInstance { get; set; }

    /// <summary>C-355: <see cref="OpenInstance"/> was attempted (it is made at most once).</summary>
    internal bool OpenInstanceAttempted { get; set; }

    /// <summary>Method implementations <c>procedure TList&lt;T&gt;.Add(…)</c> found so far, applied to every instance.</summary>
    internal List<GenericMethodImplementation> Implementations { get; } = [];

    public override bool IsGenericDefinition => true;

    /// <summary>The name the definition is declared under: <c>TList`1</c>.</summary>
    internal string ScopeName => KeyOf(Name, ParameterNames.Length);

    internal static string KeyOf(string name, int arity) => name + "`" + arity;

    /// <summary><c>type TList&lt;T&gt; = …</c> (CMP-731).</summary>
    public override string ToDisplayString() => $"type {Name}<{string.Join(", ", ParameterNames)}>";

    internal sealed class ArgumentsComparer : IEqualityComparer<ImmutableArray<TypeSymbol>>
    {
        public static readonly ArgumentsComparer Instance = new();

        public bool Equals(ImmutableArray<TypeSymbol> x, ImmutableArray<TypeSymbol> y) =>
            x.Length == y.Length && x.Zip(y).All(p => ReferenceEquals(p.First, p.Second));

        public int GetHashCode(ImmutableArray<TypeSymbol> arguments)
        {
            var hash = new HashCode();
            foreach (var argument in arguments)
            {
                hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(argument));
            }

            return hash.ToHashCode();
        }
    }
}

/// <summary>
/// C-355: a type parameter standing for itself, the argument of a generic's open instance (<see cref="GenericTypeSymbol.OpenInstance"/>).
/// It exists for the language service only: it has no members of its own and the size of a pointer (so that layouts of the open instance
/// hold), and no program, <c>.pcu</c>, IR or debug information ever names it. C-358: code insight offers the members of its constraints.
/// </summary>
internal sealed class TypeParameterTypeSymbol : TypeSymbol
{
    internal TypeParameterTypeSymbol(string name, Targets.TargetInfo target, TypeParameterConstraints? constraints = null)
        : base(TypeKind.TypeParameter, name, target.PointerSize, target.PointerAlign)
    {
        Constraints = constraints ?? TypeParameterConstraints.None;
    }

    /// <summary>C-358: the constraints of the type parameter it stands for (LANG-702).</summary>
    internal TypeParameterConstraints Constraints { get; }

    /// <summary>
    /// C-358: the types whose members code insight offers for it — its class and interface constraints, else <c>TObject</c> for a
    /// <c>class</c> or <c>constructor</c> constraint (which offers <c>Create</c>); empty without such a constraint.
    /// </summary>
    internal IEnumerable<TypeSymbol> MemberTypes(BuiltInTypes types) =>
        Constraints.Types.Any(t => t is ClassTypeSymbol) || !(Constraints.Class || Constraints.Constructor) || types.TObject is not { } root
            ? Constraints.Types.Where(t => t is ClassTypeSymbol or InterfaceTypeSymbol)
            : Constraints.Types.Where(t => t is InterfaceTypeSymbol).Prepend(root);

    /// <summary>
    /// C-421: <c>type T</c>, then its constraints in the order they are checked (LANG-984) — <c>type T: class, constructor, TBase,
    /// IGreeter</c> — for hover.
    /// </summary>
    public override string ToDisplayString()
    {
        var items = new List<string>();
        if (Constraints.Class)
        {
            items.Add("class");
        }

        if (Constraints.Record)
        {
            items.Add("record");
        }

        if (Constraints.Constructor)
        {
            items.Add("constructor");
        }

        items.AddRange(Constraints.TypeTexts);
        return items.Count == 0 ? base.ToDisplayString() : base.ToDisplayString() + ": " + string.Join(", ", items);
    }

    /// <summary>
    /// C-416 (LANG-976): <paramref name="type"/> depends on a type parameter of an open instance — it is one, an instance with one
    /// among its type arguments, a type nested in such an instance, or an array, pointer, set or class reference made of one.
    /// </summary>
    internal static bool IsIn(TypeSymbol? type) => IsIn(type, new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance));

    private static bool IsIn(TypeSymbol? type, HashSet<TypeSymbol> seen)
    {
        if (type is null || !seen.Add(type))
        {
            return false;                                                       // a type reaching itself (a pointer to its own record)
        }

        for (Symbol? symbol = type; symbol is not null; symbol = symbol.ContainingSymbol)
        {
            if (symbol is TypeParameterTypeSymbol || symbol is TypeSymbol { TypeArguments: var arguments } && arguments.Any(a => IsIn(a, seen)))
            {
                return true;
            }
        }

        return type switch
        {
            ArrayTypeSymbol array => IsIn(array.ElementType, seen),
            DynamicArrayTypeSymbol array => IsIn(array.ElementType, seen),
            OpenArrayTypeSymbol array => IsIn(array.ElementType, seen),
            SetTypeSymbol set => IsIn(set.ElementType, seen),
            PointerTypeSymbol pointer => IsIn(pointer.PointedType, seen),
            ClassRefTypeSymbol reference => IsIn(reference.Class, seen),
            _ => false,
        };
    }
}

/// <summary>
/// One method implementation of a generic type: its declaration, the type parameter names its heading uses (LANG-704), and the
/// scope (with its visible count) it was found in.
/// </summary>
/// <summary>
/// LANG-702: <c>class</c>, <c>record</c>, <c>constructor</c> and class or interface types a type argument must satisfy; <see cref="Texts"/>
/// names each as written (keywords first) for E2704.
/// </summary>
internal sealed record TypeParameterConstraints(bool Class, bool Record, bool Constructor, ImmutableArray<TypeSymbol> Types, ImmutableArray<string> TypeTexts)
{
    public static readonly TypeParameterConstraints None = new(false, false, false, [], []);
}

internal sealed record GenericMethodImplementation(RoutineDeclarationSyntax Declaration, SyntaxToken Identifier, ImmutableArray<string> ParameterNames,
    Scope Scope, int VisibleCount, Symbol Container)
{
    /// <summary>LANG-776 (C-344): the nested types after the generic in the heading's path, <c>TList&lt;T&gt;.TEnumerator.MoveNext</c>.</summary>
    public ImmutableArray<SimpleNameSyntax> NestedPath { get; init; } = [];
}