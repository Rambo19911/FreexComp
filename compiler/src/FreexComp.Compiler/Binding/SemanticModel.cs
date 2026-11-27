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
    private readonly List<(int Start, int End, Scope Scope)> _inlineScopes = [];
    private readonly List<TypeSymbol> _openInstances = [];

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

            foreach (var symbol in routine.Scope.Symbols)
            {
                Declare(symbol);
            }

            Walk(routine.Body);
        }

        BindOpenBodies(bodies);
    }

    /// <summary>
    /// C-355: the method implementations in this file of its generics' open instances (nested types too) that no bound instance body
    /// covers here, bound on the open instance (<see cref="DeclarationBinder.BindOpen"/>; their diagnostics are dropped) and indexed like
    /// the other bodies, so that code insight works in a generic's methods before (and without) any use of it.
    /// </summary>
    private void BindOpenBodies(Func<SyntaxNode, bool>? filter)
    {
        var covered = _bodies.Select(b => b.Span).ToHashSet();
        var pending = new Queue<RoutineSymbol>(_openInstances.SelectMany(TypeMembers.SelfAndNestedTypes).SelectMany(t => TypeMembers.Methods(t))
            .Where(m => m.Definition is { } definition && ReferenceEquals(definition.SyntaxTree, SyntaxTree) && (filter is null || filter(definition))
                        && covered.Add(definition.Span)));
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
    public ImmutableArray<Symbol> LookupSymbols(int position, string? name = null, TypeSymbol? container = null)
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

            symbols = members.Where(m => IsAccessible(position, m));
        }
        else
        {
            symbols = scope.VisibleSymbols();
        }

        // CMP-1207: the synthesised delegating methods of `implements` are not names a program can use
        return [.. symbols.Where(s => s is not MethodSymbol { DelegateProperty: not null } && (name is null || IdentifierComparer.Instance.Equals(s.Name, name))).Distinct()];
    }

    /// <summary>The routine, type or unit containing <paramref name="position"/> (innermost).</summary>
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
    /// CMP-729 (c, d; C-392): for a call of a generic method, the instance the call binds to; else, per generic method of the name, the
    /// instance for the written type arguments, or for those the arguments typed so far infer (a parameter of a type parameter's type takes
    /// its argument's type), else the open instance (its type parameters as types). Empty when the callee names no generic method.
    /// </summary>
    internal ImmutableArray<MethodSymbol> GetGenericMethodCandidates(InvocationExpressionSyntax call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (GetSymbolInfo(call.Expression).Symbol is MethodSymbol { GenericMethodDefinition: not null } bound)
        {
            return [bound];
        }

        var (owner, nameSyntax) = call.Expression switch
        {
            MemberAccessExpressionSyntax access => (ContainerOf(access.Expression) as TypeSymbol, access.Name),
            SimpleNameSyntax simple => (EnclosingType(call.Span.Start), simple),
            _ => ((TypeSymbol?)null, (SimpleNameSyntax?)null),
        };
        if (owner is null || nameSyntax is null)
        {
            return [];
        }

        var written = nameSyntax is GenericNameSyntax generic
            ? generic.TypeArgumentList.Arguments.Select(a => a is StringTypeSyntax { Length: null } ? _result.Types.String : GetSymbolInfo(a).Symbol as TypeSymbol).ToArray()
            : null;
        var arguments = call.ArgumentList.Arguments.ToList();
        return [.. TypeMembers.SelfAndAncestors(owner).SelectMany(t => (IEnumerable<GenericMethodSymbol>)DeclarationBinder.GenericMethodsOf(t))
            .Where(g => IdentifierComparer.Instance.Equals(g.Name, NameOf(nameSyntax.Identifier)) && (written is null || g.ParameterNames.Length == written.Length))
            .Select(g => (written is not null ? Instance(g, written) : Inferred(g, arguments)) ?? DeclarationBinder.OpenInstanceOf(_result.Types, g))
            .OfType<MethodSymbol>()];
    }

    /// <summary>CMP-729 (d): the instance for the type arguments the typed arguments give; null while one stays open.</summary>
    private MethodSymbol? Inferred(GenericMethodSymbol generic, IReadOnlyList<ArgumentSyntax> arguments)
    {
        if (DeclarationBinder.OpenInstanceOf(_result.Types, generic) is not { } open)
        {
            return null;
        }

        var inferred = new TypeSymbol?[generic.ParameterNames.Length];
        for (var i = 0; i < arguments.Count && i < open.Parameters.Length; i++)
        {
            var index = open.GenericMethodArguments.IndexOf(open.Parameters[i].Type);
            if (index >= 0 && !arguments[i].Expression.IsMissing && GetTypeInfo(arguments[i].Expression).Type is { TypeKind: not TypeKind.Error } type)
            {
                inferred[index] ??= type;
            }
        }

        return Instance(generic, inferred);
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

    /// <summary>Visibility rules of LANG-611 for a member seen from <paramref name="position"/> in this file.</summary>
    public bool IsAccessible(int position, Symbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        var owner = symbol.ContainingSymbol as TypeSymbol;
        var sameUnit = ReferenceEquals(symbol.ContainingUnit ?? owner?.ContainingUnit, Unit);
        var context = EnclosingType(position);
        bool Inside(bool descendants) =>
            context is not null && owner is not null
            && (ReferenceEquals(context, owner) || TypeMembers.EnclosingTypes(context).Any(t => ReferenceEquals(t, owner))
                || descendants && context.InheritsFrom(owner));
        return symbol.Visibility switch
        {
            Visibility.Private => sameUnit,
            Visibility.StrictPrivate => Inside(descendants: false),
            Visibility.Protected => sameUnit || Inside(descendants: true),
            Visibility.StrictProtected => Inside(descendants: true),
            _ => true,
        };
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

        if (inline is not null)
        {
            return inline;
        }

        if (InnermostBody(position) is { } body)
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
                     && PropertyNamed(type is ClassRefTypeSymbol classRef ? classRef.Class : type, NameOf(token)) is { } property)
            {
                symbol = property;
            }

            _references.TryAdd(token.Span.Start, symbol);
        }
    }

    /// <summary>Indexes the label of a labelled statement or <c>goto</c> — an identifier or a number (LANG-380, C-321) — as a reference.</summary>
    private void ReferenceLabel(ExpressionSyntax label, LabelSymbol symbol)
    {
        var token = label.FirstToken();
        if (SyntaxFacts.IsNameToken(token) && !token.IsMissing && IsOwn(token))
        {
            _references.TryAdd(token.Span.Start, symbol);
        }
    }

    private static PropertySymbol? PropertyNamed(TypeSymbol type, string name) =>
        type is PointerTypeSymbol { PointedType: { } pointed } ? PropertyNamed(pointed, name) : type.GetMembers(name).OfType<PropertySymbol>().FirstOrDefault();

    private void Walk(BoundNode? node)
    {
        if (node is null)
        {
            return;
        }

        if (node is BoundExpression expression)
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
                Reference(call.Syntax, call.Routine, call.Receiver?.Self?.Type);
                Walk(call.Target);
                Walk(call.Receiver?.Self);
                WalkAll(call.Arguments);
                break;
            case BoundCallStatement call:
                Reference(call.Syntax, call.Routine, call.Receiver?.Self?.Type);
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
