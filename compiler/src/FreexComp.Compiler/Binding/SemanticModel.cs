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

    internal SemanticModel(Compilation compilation, SyntaxTree tree, BindingResult result, ImmutableArray<Diagnostic> diagnostics,
                           Func<string, UnitSymbol?> findUnit)
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
            _ => NameToken(node) is { Kind: SyntaxKind.IdentifierToken } token ? Resolve(node, token) : null,
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
        return identifier.Kind == SyntaxKind.IdentifierToken && !identifier.IsMissing && _declared.TryGetValue(identifier.Span.Start, out var symbol)
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

            for (var type = container; type is not null; type = Inherited(type))
            {
                types.Add(type);
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

    /// <summary>LANG-441: the routines an invocation's callee name can denote at the call (for signature help).</summary>
    public ImmutableArray<RoutineSymbol> GetOverloadCandidates(InvocationExpressionSyntax call)
    {
        ArgumentNullException.ThrowIfNull(call);
        switch (call.Expression)
        {
            case SimpleNameSyntax simple:
                return ScopeAt(call.Span.Start).LookupCandidates(NameOf(simple.Identifier));
            case MemberAccessExpressionSyntax access:
                var container = ContainerOf(access.Expression);
                return container switch
                {
                    TypeSymbol type => [.. type.GetMembers(NameOf(access.Name.Identifier)).OfType<RoutineSymbol>()],
                    UnitSymbol unit => [.. unit.InterfaceMembers.OfType<RoutineSymbol>().Where(r => IdentifierComparer.Instance.Equals(r.Name, NameOf(access.Name.Identifier)))],
                    _ => [],
                };
            default:
                return [];
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

        return ScopeAt(node.Span.Start).Lookup(name) ?? _findUnit(name);
    }

    /// <summary>What the left side of a member access denotes: a unit, a type (also the type of a value), or null.</summary>
    private Symbol? ContainerOf(SyntaxNode left)
    {
        if (left is NameSyntax name && _findUnit(UnitBinder.NameOf(name)) is { } unit && GetSymbolInfo(left).Symbol is null or UnitSymbol)
        {
            return unit;
        }

        return GetSymbolInfo(left).Symbol switch
        {
            TypeSymbol type => type,
            UnitSymbol u => u,
            _ => GetTypeInfo(left).Type,
        };
    }

    /// <summary>The identifier naming what a node refers to: a name, a member access's member, an invocation's callee.</summary>
    private static SyntaxToken NameToken(SyntaxNode node) => node switch
    {
        SimpleNameSyntax simple => simple.Identifier,
        QualifiedNameSyntax qualified => qualified.Right.Identifier,
        MemberAccessExpressionSyntax access => access.Name.Identifier,
        InvocationExpressionSyntax invocation => NameToken(invocation.Expression),
        ExpressionStatementSyntax statement => NameToken(statement.Expression),
        _ => default,
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
                    foreach (var member in type.Members)
                    {
                        Visit(member);
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

        var token = NameToken(syntax);
        if (token.Kind == SyntaxKind.IdentifierToken && !token.IsMissing && IsOwn(token))
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
                Walk(field.Record);
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
                Walk(property.Self);
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
                Reference(((LabeledStatementSyntax)label.Syntax).Label, label.Label.Symbol);
                Walk(label.Statement);
                break;
            case BoundGotoStatement jump:
                Declare(jump.Label.Symbol);
                Reference(((GotoStatementSyntax)jump.Syntax).Label, jump.Label.Symbol);
                break;
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
