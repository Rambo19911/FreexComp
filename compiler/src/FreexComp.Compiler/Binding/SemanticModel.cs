using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Binding;

/// <summary>What a name or expression refers to (api.md §6).</summary>
public readonly record struct SymbolInfo(Symbol? Symbol, ImmutableArray<Symbol> CandidateSymbols, CandidateReason Reason);

public enum CandidateReason { None, NotFound, Ambiguous, Inaccessible, OverloadResolutionFailure, NotAValue, NotAType }

/// <summary>The type of an expression before (<see cref="Type"/>) and after (<see cref="ConvertedType"/>) implicit conversion.</summary>
public readonly record struct TypeInfo(TypeSymbol? Type, TypeSymbol? ConvertedType, Conversion Conversion);

/// <summary>
/// The semantic view of one source file of a <see cref="Compilation"/> (api.md §6, C-129). Built from the compilation's bound
/// units: a reference index (identifier token → symbol) over the bound bodies, a declaration index from the symbols' locations,
/// and scope lookups for names the bodies do not cover (declarations, uses entries). Thread-safe after construction; never
/// throws for malformed code (CMP-202).
/// </summary>
public sealed class SemanticModel
{
    private readonly BindingResult _result;
    private readonly ImmutableArray<Diagnostic> _diagnostics;
    private readonly Func<string, UnitSymbol?> _findUnit;
    private readonly Dictionary<int, Symbol> _declared = [];
    private readonly Dictionary<int, Symbol> _references = [];
    private readonly Dictionary<SyntaxNode, (BoundExpression Outer, BoundExpression Inner)> _expressions = new(ReferenceEqualityComparer.Instance);
    private readonly List<(TextSpan Span, BoundRoutineBody Body)> _bodies = [];

    /// <summary>
    /// CMP-1473 (C-415): the anonymous methods written in this file, by the span of their expression. They are scopes
    /// (<see cref="ScopeAt"/>) but no enclosing symbols: <see cref="GetEnclosingSymbol"/> stays the routine they are written in.
    /// </summary>
    private readonly List<(TextSpan Span, BoundRoutineBody Body)> _anonymousBodies = [];
    private readonly List<(int Start, int End, Scope Scope)> _inlineScopes = [];
    private readonly List<TypeSymbol> _openInstances = [];

    /// <summary>C-422: the types declared in this file that are no generics; their generic methods' bodies are bound open (<see cref="BindOpenBodies"/>).</summary>
    private readonly List<TypeSymbol> _plainTypes = [];

    /// <summary>
    /// CMP-1476 (C-417): the method definitions in this file with an instance body that did not bind a statement (LANG-977 rule 3: a
    /// type-query condition pruned it). Their open bodies are bound too, for the code insight of those statements.
    /// </summary>
    private readonly HashSet<TextSpan> _prunedDefinitions = [];

    /// <summary>The body being walked did not bind a pruned statement.</summary>
    private bool _unboundPruned;

    /// <summary>
    /// While the open bodies of <see cref="_prunedDefinitions"/> are walked: the expressions the instance bodies bound. They keep
    /// what the instance says (the type argument, not the type parameter); the open body only adds what was pruned.
    /// </summary>
    private HashSet<SyntaxNode>? _boundByInstances;

    /// <summary>
    /// With <see cref="_boundByInstances"/>: the symbols indexed so far by kind and declaration site in this file — first what the
    /// bound bodies refer to, then what is declared (<see cref="Canonical"/>).
    /// </summary>
    private Dictionary<(SymbolKind Kind, int Start), Symbol>? _indexedBySite;

    /// <param name="bodies">C-244: for a position model, the bodies it binds (null: all); C-355 binds open method bodies only among them.</param>
    internal SemanticModel(Compilation compilation, SyntaxTree tree, BindingResult result, ImmutableArray<Diagnostic> diagnostics,
                           Func<string, UnitSymbol?> findUnit, Func<SyntaxNode, bool>? bodies = null)
    {
        Compilation = compilation;
        SyntaxTree = tree;
        _result = result;
        _diagnostics = diagnostics;
        _findUnit = findUnit;
        IndexDeclarations();
        foreach (var body in result.Bodies)
        {
            Walk(body);
        }

        foreach (var routine in result.Routines)
        {
            if (routine.Routine.Definition is { } definition && ReferenceEquals(definition.SyntaxTree, tree))
            {
                _bodies.Add((definition.Span, routine));
            }

            AddAnonymousBody(routine);
            foreach (var symbol in routine.Scope.Symbols)
            {
                Declare(symbol);
            }

            _unboundPruned = false;
            Walk(routine.Body);
            if (_unboundPruned)
            {
                var outermost = routine.Routine;
                while (ClosureInfo.LexicalParent(outermost) is { } parent)
                {
                    outermost = parent;                                         // a nested routine or anonymous method: its method is bound open
                }

                if (outermost.Definition is { } method && ReferenceEquals(method.SyntaxTree, tree))
                {
                    _prunedDefinitions.Add(method.Span);
                }
            }
        }

        BindOpenBodies(bodies);
    }

    /// <summary>
    /// C-355: the method implementations in this file of its generics' open instances (nested types too) that no bound instance body
    /// covers here, bound on the open instance (<see cref="DeclarationBinder.BindOpen"/>; their diagnostics are dropped) and indexed like
    /// the other bodies, so that code insight works in a generic's methods before (and without) any use of it. C-422: the same for
    /// the generic methods implemented in this file — of its types that are no generics and of its generics' open instances — on
    /// the generic method's open instance (each type parameter stands for itself, with its constraints).
    /// </summary>
    private void BindOpenBodies(Func<SyntaxNode, bool>? filter)
    {
        // CMP-1476 (C-417): a definition whose instance bodies pruned a statement is not covered for that statement
        var covered = _bodies.Select(b => b.Span).Where(s => !_prunedDefinitions.Contains(s)).ToHashSet();
        if (_prunedDefinitions.Count > 0)
        {
            _boundByInstances = new HashSet<SyntaxNode>(_expressions.Keys, ReferenceEqualityComparer.Instance);
            _indexedBySite = [];
            foreach (var symbol in _references.OrderBy(r => r.Key).Select(r => r.Value).Concat(_declared.OrderBy(d => d.Key).Select(d => d.Value)))
            {
                foreach (var location in symbol.Locations)
                {
                    if (location.Span.Length > 0 && PathUtil.AreEqual(location.Path, SyntaxTree.Text.Path))
                    {
                        _indexedBySite.TryAdd((symbol.Kind, location.Span.Start), symbol);
                    }
                }
            }
        }
        var pending = new Queue<RoutineSymbol>(_openInstances.SelectMany(TypeMembers.SelfAndNestedTypes).SelectMany(t => TypeMembers.Methods(t))
            .Where(m => m.Definition is { } definition && ReferenceEquals(definition.SyntaxTree, SyntaxTree) && (filter is null || filter(definition))
                        && covered.Add(definition.Span)));
        foreach (var generic in _plainTypes.Concat(_openInstances).SelectMany(TypeMembers.SelfAndNestedTypes).SelectMany(t => DeclarationBinder.GenericMethodsOf(t)))
        {
            if (generic.Implementation?.Declaration is { } implemented && ReferenceEquals(implemented.SyntaxTree, SyntaxTree) && (filter is null || filter(implemented))
                && !covered.Contains(implemented.Span)
                && DeclarationBinder.OpenInstanceOf(_result.Types, generic) is { Definition: { } definition } open && covered.Add(definition.Span))
            {
                pending.Enqueue(open);
            }
        }

        using (DeclarationBinder.BindOpen())
        {
            while (pending.TryDequeue(out var routine))
            {
                BoundRoutineBody body;
                try
                {
                    body = Binder.BindRoutineBody(_result, routine);
                }
                catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
                {
                    continue;                                                   // CMP-202: code insight never fails on a type parameter
                }

                foreach (var bound in body.AnonymousBodies.Prepend(body))
                {
                    if (bound.Routine.Definition is { } definition && ReferenceEquals(definition.SyntaxTree, SyntaxTree))
                    {
                        _bodies.Add((definition.Span, bound));
                    }

                    AddAnonymousBody(bound);
                    foreach (var symbol in bound.Scope.Symbols)
                    {
                        Declare(symbol);
                    }

                    Walk(bound.Body);
                    foreach (var nested in bound.NestedRoutines.Where(r => r.Definition is not null))
                    {
                        pending.Enqueue(nested);
                    }
                }
            }
        }

        _boundByInstances = null;
        _indexedBySite = null;
    }

    /// <summary>
    /// CMP-1473 (C-415): notes the body of an anonymous method written in this file (its bound block is the statement part of the
    /// expression; the synthesised bodies of a method reference made from a routine or method have no such block).
    /// </summary>
    private void AddAnonymousBody(BoundRoutineBody body)
    {
        if (body.Routine is MethodSymbol { IsAnonymous: true }
            && body.Body.Syntax.Parent is BlockSyntax { Parent: AnonymousMethodExpressionSyntax anonymous } block && ReferenceEquals(block.Body, body.Body.Syntax)
            && ReferenceEquals(anonymous.SyntaxTree, SyntaxTree))
        {
            _anonymousBodies.Add((anonymous.Span, body));
        }
    }

    public Compilation Compilation { get; }

    public SyntaxTree SyntaxTree { get; }

    public UnitSymbol Unit => _result.Unit;

    /// <summary>CMP-230: identifier, member access, invocation (the chosen overload), type syntax, uses entry; null for directives.</summary>
    public SymbolInfo GetSymbolInfo(SyntaxNode node, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        ct.ThrowIfCancellationRequested();
        var symbol = node switch
        {
            UsedUnitSyntax used => _findUnit(UnitBinder.NameOf(used.Name)),
            NamedTypeSyntax named => GetSymbolInfo(named.Name, ct).Symbol,
            LiteralExpressionSyntax literal => SyntaxFacts.IsLabelNumber(literal.FirstToken()) ? Resolve(node, literal.FirstToken()) : null,   // C-321
            _ => NameToken(node) is { Kind: SyntaxKind.IdentifierToken or SyntaxKind.InheritedKeyword } token ? Resolve(node, token) : null,
        };
        if (symbol is null && node is UsedUnitSyntax or NamedTypeSyntax or NameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
            && !node.IsMissing)
        {
            return new SymbolInfo(null, [], CandidateReason.NotFound);
        }

        return new SymbolInfo(symbol, [], CandidateReason.None);
    }

    public TypeInfo GetTypeInfo(SyntaxNode expression, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ct.ThrowIfCancellationRequested();
        if (_expressions.TryGetValue(expression, out var bound))
        {
            var conversion = bound.Outer is BoundConversionExpression converted
                ? new Conversion(converted.ConversionKind, !converted.IsExplicit, true)
                : new Conversion(ConversionKind.Identity, true, true);
            return new TypeInfo(bound.Inner.Type, bound.Outer.Type, conversion);
        }

        return GetSymbolInfo(expression, ct).Symbol switch
        {
            TypeSymbol type => new TypeInfo(type, type, new Conversion(ConversionKind.Identity, true, true)),
            VariableSymbol v => new TypeInfo(v.Type, v.Type, new Conversion(ConversionKind.Identity, true, true)),
            ParameterSymbol p => new TypeInfo(p.Type, p.Type, new Conversion(ConversionKind.Identity, true, true)),
            FieldSymbol f => new TypeInfo(f.Type, f.Type, new Conversion(ConversionKind.Identity, true, true)),
            _ => default,
        };
    }

    /// <summary>The symbol declared by a declaration node (api.md §6 list) or the unit heading (the root).</summary>
    public Symbol? GetDeclaredSymbol(SyntaxNode declaration, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ct.ThrowIfCancellationRequested();
        if (declaration is CompilationUnitSyntax)
        {
            return Unit;
        }

        SyntaxToken token = declaration switch
        {
            TypeDeclarationSyntax type => type.Identifier,
            RoutineDeclarationSyntax routine => LastIdentifier(routine.Heading.Name),
            MethodDeclarationSyntax method => LastIdentifier(method.Heading.Name),
            RoutineHeadingSyntax heading => LastIdentifier(heading.Name),
            VarDeclarationSyntax variable when variable.Identifiers.Count > 0 => variable.Identifiers[0].Identifier,
            FieldDeclarationSyntax field when field.Identifiers.Count > 0 => field.Identifiers[0].Identifier,
            ParameterSyntax parameter when parameter.Identifiers.Count > 0 => parameter.Identifiers[0].Identifier,
            PropertyDeclarationSyntax property => property.Identifier,
            EnumMemberSyntax member => member.Identifier,
            ConstDeclarationSyntax constant => constant.Identifier,
            _ => default,
        };
        return token.Kind == SyntaxKind.None ? null : GetDeclaredSymbol(token, ct);
    }

    /// <summary>The symbol whose declaration (or forward / implementation heading) names <paramref name="identifier"/>.</summary>
    public Symbol? GetDeclaredSymbol(SyntaxToken identifier, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return SyntaxFacts.IsNameToken(identifier) && !identifier.IsMissing && _declared.TryGetValue(identifier.Span.Start, out var symbol)
            ? symbol
            : null;
    }

    public ConstantValue? GetConstantValue(SyntaxNode expression, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ct.ThrowIfCancellationRequested();
        return _expressions.TryGetValue(expression, out var bound) ? bound.Outer.ConstantValue ?? bound.Inner.ConstantValue
            : GetSymbolInfo(expression, ct).Symbol is ConstantSymbol constant ? constant.Value
            : null;
    }

    /// <summary>
    /// CMP-232 / LANG-310: everything visible at <paramref name="position"/>, innermost scope first, shadowed names omitted (an
    /// overload set stays whole); with <paramref name="name"/> only that name. With <paramref name="container"/>: the members of
    /// that type accessible at the position, inherited ones and those of its active helper included.
    /// </summary>
    public ImmutableArray<Symbol> LookupSymbols(int position, string? name = null, TypeSymbol? container = null) =>
        LookupSymbols(position, name, container, receiver: container);

    /// <summary>
    /// CMP-1507 (C-450): the symbol a name gives in the unit's scope — the names visible at the end of its implementation section,
    /// where the binder resolves the class names of an embedded form (LANG-670).
    /// </summary>
    internal Symbol? LookupInUnitScope(string name) => _result.Scope.Lookup(name);

    /// <summary>C-401: the members of <paramref name="container"/> accessible through <c>Self</c> (<c>inherited</c> in a descendant's method).</summary>
    internal ImmutableArray<Symbol> LookupInheritedSymbols(int position, TypeSymbol container) => LookupSymbols(position, null, container, receiver: null);

    private ImmutableArray<Symbol> LookupSymbols(int position, string? name, TypeSymbol? container, TypeSymbol? receiver)
    {
        var scope = ScopeAt(position);
        IEnumerable<Symbol> symbols;
        if (container is not null)
        {
            var members = new List<Symbol>();
            var seen = new HashSet<string>(IdentifierComparer.Instance);
            var types = new List<TypeSymbol>();
            if (scope.ActiveHelper(container) is { } helper)
            {
                types.AddRange(TypeMembers.SelfAndAncestors(helper));
            }

            // C-358: a type parameter of an open instance has the members of its constraints
            foreach (var start in container is TypeParameterTypeSymbol parameter ? parameter.MemberTypes(_result.Types) : [container])
            {
                for (var type = start; type is not null; type = Inherited(type))
                {
                    types.Add(type);
                }
            }

            foreach (var type in types)
            {
                var own = type.Members.Where(m => !seen.Contains(m.Name)).ToList();
                members.AddRange(own);
                seen.UnionWith(own.Select(m => m.Name));
            }

            symbols = members.Where(m => IsAccessible(position, m, receiver));
        }
        else
        {
            symbols = scope.VisibleSymbols();
        }

        // CMP-1207: the synthesised delegating methods of `implements` are not names a program can use
        return [.. symbols.Where(s => s is not MethodSymbol { DelegateProperty: not null } && (name is null || IdentifierComparer.Instance.Equals(s.Name, name))).Distinct()];
    }

    /// <summary>
    /// The routine, type or unit containing <paramref name="position"/> (innermost). Inside an anonymous method: the routine it is
    /// written in (the members reached through <c>Self</c> there are that routine's), or the unit for one in a unit-level body.
    /// </summary>
    public Symbol? GetEnclosingSymbol(int position)
    {
        if (InnermostBody(position) is { } body)
        {
            return body.Routine;
        }

        var token = SyntaxTree.FindToken(position);
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is TypeDeclarationSyntax type && GetDeclaredSymbol(type) is { } declared)
            {
                return declared;
            }
        }

        return Unit;
    }

    /// <summary>
    /// CMP-1473 (C-415): the result type of the function whose body contains <paramref name="position"/> — of the innermost
    /// anonymous method when the position is inside one (null in an anonymous procedure), else of the enclosing routine
    /// (null for a procedure, a constructor and outside routines). What <c>Exit(</c> expects there (CMP-1426).
    /// </summary>
    internal TypeSymbol? GetEnclosingResultType(int position)
    {
        var routine = InnermostBodyOrAnonymous(position).Body?.Routine;
        return routine is null or MethodSymbol { MethodKind: MethodKind.Constructor } ? null : routine.ReturnType;
    }

    /// <summary>This file's diagnostics as <c>opc --check</c> reports them (parse, binder, flow), optionally only those in <paramref name="span"/>.</summary>
    public ImmutableArray<Diagnostic> GetDiagnostics(TextSpan? span = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return span is { } range ? [.. _diagnostics.Where(d => d.Location.Span.IntersectsWith(range) || d.Location.Span.Start == range.Start)] : _diagnostics;
    }

    /// <summary>
    /// LANG-441: the routines an invocation's callee name can denote at the call (for signature help) — for a class member the overload set
    /// over the inheritance chain, accessible at the call, without class constructors (CMP-729 a); for <c>inherited Name(</c> the ancestor's
    /// (CMP-729 b).
    /// </summary>
    public ImmutableArray<RoutineSymbol> GetOverloadCandidates(InvocationExpressionSyntax call)
    {
        ArgumentNullException.ThrowIfNull(call);
        switch (call.Expression)
        {
            case SimpleNameSyntax simple:
                return ScopeAt(call.Span.Start).LookupCandidates(NameOf(simple.Identifier));
            case MemberAccessExpressionSyntax access:
                var container = ContainerOf(access.Expression);
                var name = NameOf(access.Name.Identifier);
                return container switch
                {
                    ClassTypeSymbol type => Accessible(call, ExpressionBinder.MemberCandidates(type, name)),
                    TypeSymbol type => [.. type.GetMembers(name).OfType<RoutineSymbol>()],
                    UnitSymbol unit => [.. unit.InterfaceMembers.OfType<RoutineSymbol>().Where(r => IdentifierComparer.Instance.Equals(r.Name, name))],
                    _ => [],
                };
            case InheritedExpressionSyntax { Name: { } inherited }:
                var method = Enclosing(GetEnclosingSymbol(call.Span.Start)).OfType<MethodSymbol>().FirstOrDefault();
                return (method?.Owner as ClassTypeSymbol)?.Parent is { } parent
                    ? Accessible(call, ExpressionBinder.MemberCandidates(parent, NameOf(inherited.Identifier)))
                    : [];
            default:
                return [];
        }
    }

    /// <summary>
    /// CMP-729 (c, d; C-392), CMP-1478 (C-419): the generic methods a call's name can mean (LANG-980 rule 1: as the binder collects
    /// them, with a type-argument list those of that number of type parameters) — each as the instance the call binds to, else as the
    /// instance for the written type arguments, or for those the arguments typed so far infer (LANG-979, as the binder infers them),
    /// else as its open instance (its type parameters as types). Empty when the callee names no generic method.
    /// </summary>
    internal ImmutableArray<MethodSymbol> GetGenericMethodCandidates(InvocationExpressionSyntax call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var bound = GetSymbolInfo(call.Expression).Symbol is MethodSymbol { GenericMethodDefinition: not null } instance ? instance : null;
        var (owner, nameSyntax) = call.Expression switch
        {
            MemberAccessExpressionSyntax access => (ContainerOf(access.Expression) as TypeSymbol, access.Name),
            SimpleNameSyntax simple => (EnclosingType(call.Span.Start), simple),
            _ => ((TypeSymbol?)null, (SimpleNameSyntax?)null),
        };
        if (owner is null || nameSyntax is null)
        {
            return bound is null ? [] : [bound];
        }

        var written = nameSyntax is GenericNameSyntax generic
            ? generic.TypeArgumentList.Arguments.Select(a => a is StringTypeSyntax { Length: null } ? _result.Types.String : GetSymbolInfo(a).Symbol as TypeSymbol).ToArray()
            : null;
        var arguments = call.ArgumentList.Arguments.ToList();
        ImmutableArray<MethodSymbol> candidates = [.. ExpressionBinder.MethodsNamed(owner, NameOf(nameSyntax.Identifier)).Generics
            .Where(g => written is null || g.ParameterNames.Length == written.Length)
            .Select(g => ReferenceEquals(bound?.GenericMethodDefinition, g) ? bound
                : (written is not null ? Instance(g, written) : Inferred(g, arguments)) ?? DeclarationBinder.OpenInstanceOf(_result.Types, g))
            .OfType<MethodSymbol>()];
        return bound is null || candidates.Contains(bound) ? candidates : [bound];
    }

    /// <summary>
    /// CMP-729 (d), CMP-1477 (1), CMP-1478: the instance for the type arguments the arguments typed so far infer — by the binder's own
    /// unification (<see cref="GenericInference"/>, LANG-979), so <c>var</c>, open-array, dynamic-array and instance-typed parameters
    /// infer as in the compiler; null while a type parameter stays open or two arguments disagree.
    /// </summary>
    private MethodSymbol? Inferred(GenericMethodSymbol generic, IReadOnlyList<ArgumentSyntax> arguments)
    {
        if (DeclarationBinder.OpenInstanceOf(_result.Types, generic) is not { } open)
        {
            return null;
        }

        var inferred = GenericInference.Infer(open, i => i < arguments.Count ? InferenceArgument(arguments[i].Expression) : default);
        return inferred is { } types ? Instance(generic, [.. types]) : null;
    }

    /// <summary>
    /// LANG-979 rule 1 for an argument as the model knows it: its natural type (a literal adapts, C-426); for a bracket constructor
    /// those of its elements.
    /// </summary>
    private GenericInference.Argument InferenceArgument(ExpressionSyntax expression)
    {
        GenericInference.Argument Value(ExpressionSyntax syntax) =>
            !syntax.IsMissing && GetTypeInfo(syntax).Type is { TypeKind: not TypeKind.Error } type
                ? GenericInference.Argument.Value(_result.Types, type, GetConstantValue(syntax))
                : default;

        return ExpressionBinder.Unparenthesized(expression) switch
        {
            AnonymousMethodExpressionSyntax or LiteralExpressionSyntax { Token.Kind: SyntaxKind.NilKeyword } => default,
            SetConstructorSyntax constructor =>
                GenericInference.Argument.Constructor([.. constructor.Elements.Select(e => e.UpperBound is null ? Value(e.Expression) : default)]),
            _ => Value(expression),
        };
    }

    private MethodSymbol? Instance(GenericMethodSymbol generic, TypeSymbol?[] arguments) =>
        arguments.Length == 0 || arguments.Any(a => a is null or { TypeKind: TypeKind.Error })
            ? null
            : DeclarationBinder.OpenInstanceOf(_result.Types, generic, [.. arguments!]);

    private ImmutableArray<RoutineSymbol> Accessible(SyntaxNode at, ImmutableArray<RoutineSymbol> routines) =>
        [.. routines.Where(r => IsAccessible(at.Span.Start, r))];

    private static IEnumerable<Symbol> Enclosing(Symbol? symbol)
    {
        for (; symbol is not null; symbol = symbol.ContainingSymbol)
        {
            yield return symbol;
        }
    }

    /// <summary>Visibility rules of LANG-610 for a member seen from <paramref name="position"/> in this file (through <c>Self</c>).</summary>
    public bool IsAccessible(int position, Symbol symbol) => IsAccessible(position, symbol, receiver: null);

    /// <summary>
    /// LANG-610 as the binder checks it (<c>ExpressionBinder.CheckAccess</c>): through a receiver whose static type is
    /// <paramref name="receiver"/> (null: the enclosing type, implicit <c>Self</c>), (strict) protected members open to a descendant's
    /// code only when the receiver is that descendant or one of its descendants, and protected ones to a unit declaring a descendant D
    /// when the receiver is D or one of its descendants (C-260; C-401).
    /// </summary>
    private bool IsAccessible(int position, Symbol symbol, TypeSymbol? receiver)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        var owner = symbol.ContainingSymbol as TypeSymbol;
        var sameUnit = ReferenceEquals(symbol.ContainingUnit ?? owner?.ContainingUnit, Unit);
        var context = EnclosingType(position);
        var selves = context is null ? [] : new[] { context }.Concat(TypeMembers.EnclosingTypes(context)).ToList();
        var own = owner is not null && selves.Any(t => ReferenceEquals(t, owner));
        var through = receiver ?? context;
        var descendant = !own && owner is not null && selves.Any(t => t.InheritsFrom(owner) && through is not null && (ReferenceEquals(through, t) || through.InheritsFrom(t)));
        return symbol.Visibility switch
        {
            Visibility.Private => sameUnit,
            Visibility.StrictPrivate => own,
            Visibility.Protected => sameUnit || own || descendant || owner is not null && LocalDescendant(owner, through),
            Visibility.StrictProtected => own || descendant,
            _ => true,
        };
    }

    /// <summary>C-260: a class of this unit between <paramref name="receiver"/> (included) and <paramref name="owner"/> (excluded).</summary>
    private bool LocalDescendant(TypeSymbol owner, TypeSymbol? receiver)
    {
        for (var type = receiver as ClassTypeSymbol; type is not null && !ReferenceEquals(type, owner); type = type.Parent)
        {
            if (ReferenceEquals(TypeMembers.Outermost(type).ContainingSymbol, Unit) && type.InheritsFrom(owner))
            {
                return true;
            }
        }

        return false;
    }

    public DirectiveState GetDirectiveState(int position) => SyntaxTree.FindToken(position).DirectiveState;

    // ---------------------------------------------------------------------------------------------------------- resolution

    private Symbol? Resolve(SyntaxNode node, SyntaxToken token)
    {
        if (_references.TryGetValue(token.Span.Start, out var referenced))
        {
            return referenced;
        }

        if (_declared.TryGetValue(token.Span.Start, out var declared))
        {
            return declared;
        }

        var name = NameOf(token);
        var nameNode = token.Parent;
        // the right side of a member access or qualified name: a member of what the left side denotes
        SyntaxNode? left = nameNode?.Parent switch
        {
            MemberAccessExpressionSyntax access when ReferenceEquals(access.Name, nameNode) => access.Expression,
            QualifiedNameSyntax qualified when ReferenceEquals(qualified.Right, nameNode) => qualified.Left,
            _ => null,
        };
        if (nameNode is GenericNameSyntax generic && GenericType(generic, left, name) is { } genericType)
        {
            return genericType;
        }

        if (GenericMethodOfHeading(nameNode, left) is { } genericMethod)
        {
            return genericMethod;
        }

        if (left is not null)
        {
            return ContainerOf(left) switch
            {
                UnitSymbol unit => unit.InterfaceMembers.FirstOrDefault(m => IdentifierComparer.Instance.Equals(m.Name, name)),
                TypeSymbol type => LookupSymbols(token.Span.Start, name, type).FirstOrDefault() ?? type.GetMembers(name).FirstOrDefault(),
                _ => null,
            };
        }

        if (nameNode?.Parent is QualifiedNameSyntax && _findUnit(UnitBinder.NameOf(nameNode.Parent)) is { } dottedUnit)
        {
            return dottedUnit;
        }

        // C-451: the target of a method resolution clause (LANG-661) is the method the interface method is mapped to
        if (nameNode?.Parent is MethodResolutionSyntax resolution && ReferenceEquals(resolution.Target, nameNode)
            && resolution.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } resolvingType
            && GetDeclaredSymbol(resolvingType.Identifier) is ClassTypeSymbol resolvingClass)
        {
            var mapped = GetSymbolInfo(resolution.InterfaceMethod).Symbol as MethodSymbol;
            return (mapped is null ? null : DeclarationBinder.FindImplementation(resolvingClass, name, mapped))
                ?? resolvingClass.SelfAndAncestors().SelectMany(c => c.Methods).FirstOrDefault(m => IdentifierComparer.Instance.Equals(m.Name, name));
        }

        // C-139: the accessor of a property (read / write / stored) is a member of the property's type
        if (nameNode?.FirstAncestorOrSelf<PropertySpecifierSyntax>() is { Keyword.ContextualKind: SyntaxKind.ReadKeyword or SyntaxKind.WriteKeyword or SyntaxKind.StoredKeyword } specifier
            && specifier.FirstAncestorOrSelf<PropertyDeclarationSyntax>() is { } property
            && GetDeclaredSymbol(property.Identifier) is PropertySymbol ownerProperty)
        {
            Symbol? accessor = specifier.Keyword.ContextualKind switch
            {
                SyntaxKind.ReadKeyword => ownerProperty.ReadMethod ?? ownerProperty.ReadPath.FirstOrDefault(),
                SyntaxKind.WriteKeyword => ownerProperty.WriteMethod ?? ownerProperty.WritePath.FirstOrDefault(),
                _ => ownerProperty.StoredField,
            };
            if (accessor is not null && IdentifierComparer.Instance.Equals(accessor.Name, name))
            {
                return accessor;
            }

            if ((LookupSymbols(token.Span.Start, name, ownerProperty.Owner).FirstOrDefault() ?? ownerProperty.Owner.GetMembers(name).FirstOrDefault()) is { } member)
            {
                return member;
            }
        }

        if (left is null && MethodTypeParameter(nameNode, name) is { } methodTypeParameter)
        {
            return methodTypeParameter;
        }

        // C-355: in a type's declaration (a field's type, a method heading) a nested type or constant of that type or of an enclosing
        // one (LANG-770); a generic stands for its representative instance
        if (left is null && GetEnclosingSymbol(token.Span.Start) is TypeSymbol enclosing)
        {
            for (Symbol? type = enclosing; type is TypeSymbol outer; type = outer.ContainingSymbol)
            {
                var members = Representative(outer) ?? outer;
                if (LookupSymbols(token.Span.Start, name, members).FirstOrDefault(m => m is TypeSymbol or ConstantSymbol) is { } member)
                {
                    return member;
                }

                // C-421: a type parameter of the generic is its argument in that instance (in the open one: itself, with its constraints)
                if (members is TypeSymbol { GenericDefinition: GenericTypeSymbol definition, TypeArguments: var typeArguments }
                    && definition.ParameterNames.IndexOf(name, 0, IdentifierComparer.Instance) is >= 0 and var index && index < typeArguments.Length)
                {
                    return typeArguments[index];
                }
            }
        }

        return ScopeAt(node.Span.Start).Lookup(name) ?? _findUnit(name);
    }

    /// <summary>
    /// What the left side of a member access denotes: a unit, a type (also the type of a value), or null. A generic definition
    /// (<c>TList&lt;T&gt;</c> in a method implementation heading) stands for its <see cref="Representative"/> instance.
    /// </summary>
    private Symbol? ContainerOf(SyntaxNode left)
    {
        if (left is NameSyntax name && _findUnit(UnitBinder.NameOf(name)) is { } unit && GetSymbolInfo(left).Symbol is null or UnitSymbol)
        {
            return unit;
        }

        return GetSymbolInfo(left).Symbol switch
        {
            TypeSymbol type => Representative(type) ?? type,
            UnitSymbol u => u,
            _ => GetTypeInfo(left).Type,
        };
    }

    /// <summary>
    /// C-421: a type parameter of a generic method named in that method's heading — in its list, a parameter type or the result
    /// type, of the declaration in the type or of the implementation — is the type standing for it in the method's open instance,
    /// which carries its constraints (hover). Null for any other name.
    /// </summary>
    private TypeSymbol? MethodTypeParameter(SyntaxNode? nameNode, string name)
    {
        if (nameNode?.FirstAncestorOrSelf<RoutineHeadingSyntax>() is not { TypeParameterList: { } list } heading
            || !list.Parameters.SelectMany(p => p.Identifiers).Any(i => IdentifierComparer.Instance.Equals(NameOf(i.Identifier), name))
            || (heading.Name is QualifiedNameSyntax qualified ? ContainerOf(qualified.Left) as TypeSymbol : EnclosingType(heading.Span.Start)) is not { } owner)
        {
            return null;
        }

        var method = DeclarationBinder.GenericMethodsOf(Representative(owner) ?? owner).FirstOrDefault(g =>
            ReferenceEquals(g.Declaration.Heading, heading) || ReferenceEquals(g.Implementation?.Declaration.Heading, heading));
        if (method is null || DeclarationBinder.OpenInstanceOf(_result.Types, method) is not { } open)
        {
            return null;
        }

        var index = method.ParameterNames.IndexOf(name, 0, IdentifierComparer.Instance);
        return index >= 0 && index < open.GenericMethodArguments.Length ? open.GenericMethodArguments[index] : null;
    }

    /// <summary>
    /// CMP-1478 (C-419): the name in the heading of a generic method — its declaration in the type or its implementation
    /// (<c>TOut.Show&lt;T&gt;</c>) — is that generic method, as its open instance (the same declaration as every instance a call binds
    /// to); never a non-generic method of the same name, with which it forms one overload set (LANG-980). Null for any other name.
    /// </summary>
    private MethodSymbol? GenericMethodOfHeading(SyntaxNode? nameNode, SyntaxNode? left)
    {
        var heading = nameNode?.Parent switch
        {
            RoutineHeadingSyntax declared when ReferenceEquals(declared.Name, nameNode) => declared,
            QualifiedNameSyntax { Parent: RoutineHeadingSyntax defined } qualified when ReferenceEquals(qualified.Right, nameNode) && ReferenceEquals(defined.Name, qualified) => defined,
            _ => null,
        };
        if (nameNode is null || heading?.TypeParameterList is null
            || (left is null ? EnclosingType(nameNode.Span.Start) : ContainerOf(left) as TypeSymbol) is not { } owner)
        {
            return null;
        }

        var method = DeclarationBinder.GenericMethodsOf(owner).FirstOrDefault(g =>
            ReferenceEquals(g.Declaration.Heading, heading) || ReferenceEquals(g.Implementation?.Declaration.Heading, heading));
        return method is null ? null : DeclarationBinder.OpenInstanceOf(_result.Types, method);
    }

    /// <summary>
    /// CMP-1394 (C-347): the generic type a generic name segment names — by name and arity in the scope, in a unit qualifier's interface
    /// or among the generics nested in a type qualifier — as the instance for its type arguments when they are types and that instance
    /// exists, else the definition; null when no generic type has that name and arity (a generic routine, an error).
    /// </summary>
    private TypeSymbol? GenericType(GenericNameSyntax generic, SyntaxNode? left, string name)
    {
        var arity = generic.TypeArgumentList.Arguments.Count;
        IEnumerable<Symbol?> candidates = left is null
            ? [ScopeAt(generic.Span.Start).Lookup(GenericTypeSymbol.KeyOf(name, arity))]
            : ContainerOf(left) switch
            {
                UnitSymbol unit => unit.InterfaceMembers,
                TypeSymbol type => TypeMembers.MemberNestedGenerics(type, name),            // C-354: inherited too
                _ => [],
            };
        if (candidates.OfType<GenericTypeSymbol>().FirstOrDefault(g => IdentifierComparer.Instance.Equals(g.Name, name) && g.ParameterNames.Length == arity)
            is not { } definition)
        {
            return null;
        }

        var arguments = generic.TypeArgumentList.Arguments
            .Select(a => a is StringTypeSyntax { Length: null } ? _result.Types.String : GetSymbolInfo(a).Symbol as TypeSymbol).ToArray();
        return arguments.All(a => a is not null) && (definition.Instances.TryGetValue([.. arguments!], out var instance)
            || definition.OpenInstances.TryGetValue([.. arguments!], out instance)) ? instance : definition;          // C-355: TStack<T> in its methods
    }

    /// <summary>
    /// C-347: the instance standing for a generic definition, or null when it is no generic. C-355: its open instance (over its own type
    /// parameters); only when that cannot be made (no declaration text) the first instance made, if any.
    /// </summary>
    private TypeSymbol? Representative(TypeSymbol type) => type is GenericTypeSymbol generic
        ? DeclarationBinder.OpenInstanceOf(_result.Types, generic) ?? generic.Instances.Values.FirstOrDefault()
        : null;

    /// <summary>The identifier naming what a node refers to: a name, a member access's member, an invocation's callee.</summary>
    private static SyntaxToken NameToken(SyntaxNode node) => node switch
    {
        SimpleNameSyntax simple => simple.Identifier,
        QualifiedNameSyntax qualified => qualified.Right.Identifier,
        MemberAccessExpressionSyntax access => access.Name.Identifier,
        InvocationExpressionSyntax invocation => NameToken(invocation.Expression),
        ExpressionStatementSyntax statement => NameToken(statement.Expression),
        InheritedStatementSyntax inheritedStatement => NameToken(inheritedStatement.Expression),
        InheritedExpressionSyntax inherited => inherited.Name?.Identifier ?? inherited.InheritedKeyword,      // C-396: the ancestor's member, not a lookup of the name here
        _ => default,
    };

    /// <summary>An <c>inherited</c> callee, also as the callee of an invocation or a statement.</summary>
    private static bool IsInheritedCall(SyntaxNode node) => node switch
    {
        InheritedExpressionSyntax => true,
        InvocationExpressionSyntax invocation => IsInheritedCall(invocation.Expression),
        ExpressionStatementSyntax statement => IsInheritedCall(statement.Expression),
        InheritedStatementSyntax => true,
        _ => false,
    };

    private static SyntaxToken LastIdentifier(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier,
        SimpleNameSyntax simple => simple.Identifier,
        _ => default,
    };

    private static TypeSymbol? Inherited(TypeSymbol type) => type switch
    {
        ClassTypeSymbol c => c.Parent,
        InterfaceTypeSymbol i => i.Parent,
        HelperTypeSymbol h => h.Parent,
        _ => null,
    };

    private static string NameOf(SyntaxToken identifier) => identifier.Value as string ?? identifier.Text;

    private Scope ScopeAt(int position)
    {
        // LANG-572: inside the declaration scope of an inline name, the innermost such scope (the one declared last); its
        // parents are the enclosing lists' scopes, then the routine's or the unit's
        Scope? inline = null;
        var inlineStart = -1;
        foreach (var (start, end, scope) in _inlineScopes)
        {
            if (start <= position && position <= end && start > inlineStart)
            {
                (inline, inlineStart) = (scope, start);
            }
        }

        // CMP-1473 (C-415): the innermost body, an anonymous method included. An inline scope that began before that body is a
        // scope around it (the anonymous method is written inside the inline name's scope; it is among the body's parents)
        var (span, body) = InnermostBodyOrAnonymous(position);
        if (inline is not null && (body is null || inlineStart >= span.Start))
        {
            return inline;
        }

        if (body is not null)
        {
            return body.Scope;
        }

        if (SyntaxTree.Root is UnitSyntax unit && unit.InterfaceSection.FullSpan.Contains(position) && _result.InterfaceScope is { } interfaceScope)
        {
            return interfaceScope;
        }

        return _result.Scope;
    }

    private BoundRoutineBody? InnermostBody(int position) =>
        _bodies.Where(b => b.Span.Contains(position)).OrderBy(b => b.Span.Length).Select(b => b.Body).FirstOrDefault();

    /// <summary>CMP-1473 (C-415): the innermost body containing <paramref name="position"/>, an anonymous method included, with its span.</summary>
    private (TextSpan Span, BoundRoutineBody? Body) InnermostBodyOrAnonymous(int position) =>
        _bodies.Concat(_anonymousBodies).Where(b => b.Span.Contains(position)).OrderBy(b => b.Span.Length).Select(b => (b.Span, (BoundRoutineBody?)b.Body)).FirstOrDefault();

    private TypeSymbol? EnclosingType(int position)
    {
        for (var symbol = GetEnclosingSymbol(position); symbol is not null; symbol = symbol.ContainingSymbol)
        {
            if (symbol is TypeSymbol type)
            {
                return type;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------------------- indexes

    private bool IsOwn(SyntaxToken token) => token.File is null || PathUtil.AreEqual(token.File.Path, SyntaxTree.Text.Path);

    private void IndexDeclarations()
    {
        var seen = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
        void Visit(Symbol symbol)
        {
            if (!seen.Add(symbol))
            {
                return;
            }

            var own = Declare(symbol);
            switch (symbol)
            {
                case UnitSymbol unit:
                    foreach (var member in unit.InterfaceMembers.Concat(unit.ImplementationMembers))
                    {
                        Visit(member);
                    }

                    break;
                case TypeSymbol type when own:
                    // CMP-1400 (C-377): class constructors and destructors are no members (LANG-787) but their headings are declarations
                    foreach (var member in type.Members.Concat(TypeMembers.ClassConstructors(type)))
                    {
                        Visit(member);
                    }

                    // CMP-1394 (C-347): the members of a generic's declaration (nested types, their methods) are those of an instance
                    if (type is not GenericTypeSymbol)
                    {
                        _plainTypes.Add(type);
                    }

                    if (Representative(type) is { } instance)
                    {
                        if (type is GenericTypeSymbol { OpenInstance: { } open } && ReferenceEquals(open, instance))
                        {
                            _openInstances.Add(open);                           // C-355: its method bodies are bound on it
                        }

                        foreach (var member in instance.Members.Concat(TypeMembers.ClassConstructors(instance)))
                        {
                            Visit(member);
                        }
                    }

                    break;
                case RoutineSymbol routine:
                    foreach (var parameter in routine.Parameters)
                    {
                        Visit(parameter);
                    }

                    break;
            }
        }

        Visit(Unit);
    }

    /// <summary>Indexes the symbol's declaration sites in this file (by the first and the last identifier of a dotted name).</summary>
    private bool Declare(Symbol symbol)
    {
        var any = false;
        foreach (var location in symbol.Locations)
        {
            if (!PathUtil.AreEqual(location.Path, SyntaxTree.Text.Path) || location.Span.Length == 0)
            {
                continue;
            }

            any = true;
            _declared.TryAdd(location.Span.Start, symbol);
            var last = SyntaxTree.FindToken(location.Span.End - 1);
            if (last.Kind == SyntaxKind.IdentifierToken && last.Span.Start > location.Span.Start)
            {
                _declared.TryAdd(last.Span.Start, symbol);
            }
        }

        return any;
    }

    /// <summary>
    /// Indexes the name token of <paramref name="syntax"/> as a reference to <paramref name="symbol"/>. A typed constant's storage is
    /// reported as the constant; an accessor (field or method) reached through a property name on <paramref name="receiver"/> as
    /// the property.
    /// </summary>
    private void Reference(SyntaxNode syntax, Symbol? symbol, TypeSymbol? receiver = null)
    {
        if (symbol is null)
        {
            return;
        }

        // C-396: the implicit Self and forwarded parameters of an inherited call share its syntax; its name is the ancestor's member
        if (symbol is ParameterSymbol && IsInheritedCall(syntax))
        {
            return;
        }

        symbol = Canonical(symbol);

        var token = NameToken(syntax);
        if (token.Kind is SyntaxKind.IdentifierToken or SyntaxKind.InheritedKeyword && !token.IsMissing && IsOwn(token))
        {
            if (symbol is VariableSymbol { TypedConstant: { } constant })
            {
                symbol = constant;
            }
            else if (symbol is MethodSymbol { DispatchFlags: > 1, ContainingInterface: { } dispatch }
                     && PropertyNamed(dispatch, NameOf(token)) is { } dispatchProperty)
            {
                symbol = dispatchProperty;                                      // CMP-1211: the hidden get / put member of a dispatch property
            }
            else if (symbol is FieldSymbol or MethodSymbol && !IdentifierComparer.Instance.Equals(symbol.Name, NameOf(token))
                     && (receiver ?? symbol.ContainingSymbol as TypeSymbol) is { } type
                     && ((symbol.ContainingSymbol is HelperTypeSymbol helper ? PropertyNamed(helper, NameOf(token)) : null)
                         ?? PropertyNamed(type is ClassRefTypeSymbol classRef ? classRef.Class : type, NameOf(token))) is { } property)
            {
                // C-442: a helper's property is no member of the receiver's type — it is found in the helper of its accessor method
                symbol = property;
            }

            _references.TryAdd(token.Span.Start, symbol);
        }
    }

    /// <summary>
    /// Indexes the callee name of a call. A call through a value (<paramref name="target"/>: a procedural variable, a method
    /// pointer, a method reference — LANG-484, LANG-724) names that value, not the signature of its type: the name is indexed when
    /// the target is walked (CMP-1473 (2), C-415: a captured method reference is one symbol, also where it is called).
    /// </summary>
    private void ReferenceCallee(SyntaxNode syntax, RoutineSymbol routine, BoundExpression? target, TypeSymbol? receiver)
    {
        if (target is null)
        {
            Reference(syntax, routine, receiver);
        }
    }

    /// <summary>Indexes the label of a labelled statement or <c>goto</c> — an identifier or a number (LANG-380, C-321) — as a reference.</summary>
    private void ReferenceLabel(ExpressionSyntax label, LabelSymbol symbol)
    {
        var token = label.FirstToken();
        if (SyntaxFacts.IsNameToken(token) && !token.IsMissing && IsOwn(token))
        {
            _references.TryAdd(token.Span.Start, Canonical(symbol));
        }
    }

    /// <summary>
    /// CMP-1476 (C-417): while open bodies add what instance bodies pruned, a symbol declared in this file is the one the instance
    /// bodies already refer to (else the one declared) at one of its declaration sites — the instance's local, parameter or member,
    /// not the open instance's twin — so that the references inside and outside a pruned statement are references to one symbol.
    /// Otherwise <paramref name="symbol"/> itself.
    /// </summary>
    private Symbol Canonical(Symbol symbol)
    {
        if (_indexedBySite is null)
        {
            return symbol;
        }

        foreach (var location in symbol.Locations)
        {
            if (location.Span.Length > 0 && PathUtil.AreEqual(location.Path, SyntaxTree.Text.Path)
                && _indexedBySite.TryGetValue((symbol.Kind, location.Span.Start), out var known)
                && IdentifierComparer.Instance.Equals(known.Name, symbol.Name))
            {
                return known;
            }
        }

        return symbol;
    }

    private static PropertySymbol? PropertyNamed(TypeSymbol type, string name) =>
        type is PointerTypeSymbol { PointedType: { } pointed } ? PropertyNamed(pointed, name) : type.GetMembers(name).OfType<PropertySymbol>().FirstOrDefault();

    private void Walk(BoundNode? node)
    {
        if (node is null)
        {
            return;
        }

        if (node is BoundExpression expression && _boundByInstances?.Contains(node.Syntax) != true)
        {
            _expressions[node.Syntax] = _expressions.TryGetValue(node.Syntax, out var known) ? (known.Outer, expression) : (expression, expression);
        }

        switch (node)
        {
            case BoundErrorExpression error:
                WalkAll(error.Children);
                break;
            case BoundConstantExpression constant:
                Reference(constant.Syntax, constant.Constant);
                break;
            case BoundVariableExpression variable:
                Reference(variable.Syntax, (Symbol?)variable.Property ?? variable.Variable, variable.Variable.ContainingSymbol as TypeSymbol);
                break;
            case BoundParameterExpression parameter:
                Reference(parameter.Syntax, parameter.Parameter);
                break;
            case BoundConversionExpression conversion:
                Walk(conversion.Operand);
                break;
            case BoundUnaryExpression unary:
                Walk(unary.Operand);
                break;
            case BoundBinaryExpression binary:
                Walk(binary.Left);
                Walk(binary.Right);
                break;
            case BoundSetConstructor set:
                foreach (var element in set.Elements)
                {
                    Walk(element.Low);
                    Walk(element.High);
                }

                break;
            case BoundArrayConstructor array:
                WalkAll(array.Elements);
                break;
            case BoundIndexExpression index:
                Walk(index.Array);
                Walk(index.Index);
                break;
            case BoundVariableTypecastExpression cast:
                Walk(cast.Operand);
                break;
            case BoundFieldAccessExpression field:
                Reference(field.Syntax, (Symbol?)field.Property ?? field.Field, field.Record.Type);
                WalkReceiver(field.Record, field.Syntax);
                break;
            case BoundTypeTestExpression test:
                if (test.Syntax is BinaryExpressionSyntax { Right: { } target })
                {
                    Reference(target, test.Target);
                }

                Walk(test.Operand);
                break;
            case BoundPropertyAccessExpression property:
                Reference(property.Syntax, property.Property);
                WalkReceiver(property.Self, property.Syntax);
                WalkAll(property.Indexes);
                break;
            case BoundAddressOfExpression address:
                if (address.Syntax is AddressOfExpressionSyntax { } at && address.Routine is { } routine)
                {
                    Reference(at.ChildNodes().FirstOrDefault() ?? at, routine);
                }

                Walk(address.Operand);
                break;
            case BoundDereferenceExpression dereference:
                Walk(dereference.Pointer);
                break;
            case BoundPointerArithmeticExpression arithmetic:
                Walk(arithmetic.Pointer);
                Walk(arithmetic.Offset);
                break;
            case BoundCallExpression call:
                ReferenceCallee(call.Syntax, call.Routine, call.Target, call.Receiver?.Self?.Type);
                Walk(call.Target);
                Walk(call.Receiver?.Self);
                WalkAll(call.Arguments);
                break;
            case BoundCallStatement call:
                ReferenceCallee(call.Syntax, call.Routine, call.Target, call.Receiver?.Self?.Type);
                Walk(call.Target);
                Walk(call.Receiver?.Self);
                WalkAll(call.Arguments);
                break;
            case BoundClassReferenceExpression classReference:
                Reference(classReference.Syntax, classReference.Class);
                break;
            case BoundInterfaceGuidExpression guid:
                Reference(guid.Syntax, guid.Interface);                              // LANG-680 (C-299)
                break;
            case BoundMethodPointerExpression pointer:
                Reference(pointer.Syntax, pointer.Method);
                Walk(pointer.Self);
                break;
            case BoundIntrinsicCallExpression intrinsic:
                Reference(intrinsic.Syntax, intrinsic.Intrinsic);
                WalkAll(intrinsic.Arguments);
                break;
            case BoundIntrinsicCallStatement intrinsic:
                Reference(intrinsic.Syntax, intrinsic.Intrinsic);
                WalkAll(intrinsic.Arguments);
                break;
            case BoundWriteStatement write:
                Reference(write.Syntax, write.Intrinsic);
                Walk(write.File);
                foreach (var argument in write.Arguments)
                {
                    Walk(argument.Value);
                    Walk(argument.Width);
                    Walk(argument.Decimals);
                }

                break;
            case BoundBlock block:
                WalkAll(block.Statements);
                break;
            case BoundAssignmentStatement assignment:
                Walk(assignment.Target);
                Walk(assignment.Value);
                break;
            case BoundExpressionStatement statement:
                Walk(statement.Expression);
                break;
            case BoundIfStatement @if:
                Walk(@if.Condition);
                Walk(@if.Then);
                Walk(@if.Else);
                break;
            case BoundPrunedStatement pruned:
                // LANG-977 (C-417): the condition, every case label, the kept statement and — outside generics — the checked ones
                Walk(pruned.Condition);
                foreach (var label in pruned.Labels)
                {
                    Walk(label.Low);
                    Walk(label.High);
                }

                Walk(pruned.Kept);
                WalkAll(pruned.Checked);
                _unboundPruned |= pruned.HasUnbound;
                break;
            case BoundWhileStatement @while:
                Walk(@while.Condition);
                Walk(@while.Body);
                break;
            case BoundRepeatStatement repeat:
                Walk(repeat.Body);
                Walk(repeat.Condition);
                break;
            case BoundForStatement @for:
                Walk(@for.Variable);
                Walk(@for.Start);
                Walk(@for.Finish);
                Walk(@for.Body);
                break;
            case BoundCaseStatement @case:
                Walk(@case.Selector);
                foreach (var arm in @case.Arms)
                {
                    foreach (var label in arm.Labels)
                    {
                        Walk(label.Low);
                        Walk(label.High);
                    }

                    Walk(arm.Body);
                }

                Walk(@case.Else);
                break;
            case BoundExitStatement exit:
                Walk(exit.Value);
                break;
            case BoundRaiseStatement raise:
                Walk(raise.Exception);
                Walk(raise.Address);
                break;
            case BoundTryExceptStatement tryExcept:
                Walk(tryExcept.Body);
                foreach (var handler in tryExcept.Handlers)
                {
                    if (handler.Variable is { } variable)
                    {
                        Declare(variable);
                    }

                    Walk(handler.Body);
                }

                Walk(tryExcept.Else);
                break;
            case BoundTryFinallyStatement tryFinally:
                Walk(tryFinally.Body);
                Walk(tryFinally.Finally);
                break;
            case BoundWithStatement with:
                Walk(with.Value);
                Walk(with.Body);
                break;
            case BoundErrorStatement error:
                Walk(error.Expression);
                break;
            case BoundLocalsStatement locals:
                foreach (var local in locals.Locals)
                {
                    Declare(local);
                }

                if (locals.DeclarationScope is { } declarationScope && ReferenceEquals(locals.Syntax.SyntaxTree, SyntaxTree))
                {
                    foreach (var symbol in declarationScope.Symbols)
                    {
                        Declare(symbol);                    // also the true constants of an inline const
                    }

                    _inlineScopes.Add((locals.ScopeRange.Start, locals.ScopeRange.End, declarationScope));
                }

                Walk(locals.Body);
                break;
            case BoundHaltStatement halt:
                Walk(halt.ExitCode);
                break;
            case BoundLabelStatement label:
                Declare(label.Label.Symbol);                                    // C-318: the label section's name
                ReferenceLabel(((LabeledStatementSyntax)label.Syntax).Label, label.Label.Symbol);
                Walk(label.Statement);
                break;
            case BoundGotoStatement jump:
                Declare(jump.Label.Symbol);
                ReferenceLabel(((GotoStatementSyntax)jump.Syntax).Label, jump.Label.Symbol);
                break;
        }
    }

    /// <summary>
    /// The receiver of a member access; C-358: an implicit one (<c>Self</c> or a <c>with</c> receiver, made at the member's own name) is no
    /// expression of its own, so the name keeps the member's type (completion after <c>Shape.</c> for a field <c>Shape</c>).
    /// </summary>
    private void WalkReceiver(BoundExpression? receiver, SyntaxNode member)
    {
        if (receiver is not null && !ReferenceEquals(receiver.Syntax, member))
        {
            Walk(receiver);
        }
    }

    private void WalkAll<T>(ImmutableArray<T> nodes)
        where T : BoundNode
    {
        foreach (var node in nodes.IsDefault ? [] : nodes)
        {
            Walk(node);
        }
    }
}
