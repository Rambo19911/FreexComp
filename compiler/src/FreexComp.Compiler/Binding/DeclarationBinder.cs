using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>Unit-level symbols of one compilation unit (CMP-113).</summary>
/// <param name="InterfaceScope">The interface scope of a unit; null for a program.</param>
/// <param name="Scope">The innermost unit-level scope: a program's scope or a unit's implementation scope.</param>
internal sealed record DeclarationBindingResult(
    UnitSymbol Unit,
    Scope? InterfaceScope,
    Scope Scope,
    ImmutableArray<Diagnostic> Diagnostics);

/// <summary>
/// Binds declaration sections into a scope (CMP-113): creates the declared symbols in source order and reports
/// declaration errors. One instance per scope being filled.
/// </summary>
internal sealed class DeclarationBinder
{
    private readonly BuiltInTypes _types;
    private readonly Scope _scope;
    private readonly Symbol _container;
    private readonly DiagnosticBag _diagnostics;
    private readonly Scope? _sharedScope;
    private readonly ConstantFolder _folder;
    private readonly Conversions _conversions = new();
    private readonly List<RoutineSymbol> _awaitingDefinition = [];

    /// <param name="sharedScope">
    /// A scope whose names also count as declared in <paramref name="scope"/> for E2004: a unit's interface scope when
    /// binding its implementation (LANG-518).
    /// </param>
    public DeclarationBinder(BuiltInTypes types, Scope scope, Symbol container, DiagnosticBag diagnostics, Scope? sharedScope = null)
    {
        _types = types;
        _scope = scope;
        _container = container;
        _diagnostics = diagnostics;
        _sharedScope = sharedScope;
        _folder = new ConstantFolder(types, scope, diagnostics);
    }

    /// <summary>A unit's interface section: its routines are defined in the implementation (E2100 is checked there).</summary>
    public bool IsInterfaceSection { get; init; }

    /// <summary>Unit and program level declarations are global; routine-level ones are locals.</summary>
    private bool IsGlobalLevel => _scope.Kind == ScopeKind.Unit;

    /// <summary>
    /// The unit-level scopes of a program (one scope) or unit (interface scope, then implementation scope inside it;
    /// LANG-310), chained to the built-in scope. Uses clauses and System are bound by later tasks.
    /// </summary>
    public static DeclarationBindingResult BindCompilationUnit(SyntaxTree tree, BuiltInTypes types) =>
        BindCompilationUnit(tree, types, new DiagnosticBag());

    /// <param name="diagnostics">Shared with the bodies of the unit level (one E2003 set, CMP-507).</param>
    /// <param name="usesScope">
    /// The scope between the built-ins and the unit's own scope (LANG-310): the interface scope of the implicitly used
    /// <c>System</c> (LANG-511); null = the built-ins directly (unit <c>System</c> itself, white-box tests).
    /// </param>
    public static DeclarationBindingResult BindCompilationUnit(SyntaxTree tree, BuiltInTypes types, DiagnosticBag diagnostics, Scope? usesScope = null)
    {
        var outer = usesScope ?? types.Scope;
        switch (tree.Root)
        {
            case UnitSyntax unit:
            {
                var symbol = new UnitSymbol(NameOf(unit.Name), isProgram: false, [DiagnosticBag.LocationOf(unit.Name)]);
                var interfaceScope = new Scope(ScopeKind.Unit, outer, symbol);
                var implementationScope = new Scope(ScopeKind.Unit, interfaceScope, symbol);
                symbol.InterfaceMembers = new DeclarationBinder(types, interfaceScope, symbol, diagnostics) { IsInterfaceSection = true }
                    .BindDeclarations(unit.InterfaceSection.Declarations);
                symbol.ImplementationMembers = new DeclarationBinder(types, implementationScope, symbol, diagnostics, interfaceScope)
                    .BindDeclarations(unit.ImplementationSection.Declarations);
                foreach (var routine in symbol.InterfaceMembers.OfType<RoutineSymbol>().Where(r => r.AwaitsDefinition))
                {
                    diagnostics.Report(routine.Declaration.Heading.Name, "E2100", routine.Name);     // LANG-400
                }

                return new DeclarationBindingResult(symbol, interfaceScope, implementationScope, diagnostics.ToImmutable());
            }

            case ProgramSyntax program:
            {
                var symbol = program.Name is { IsMissing: false } name
                    ? new UnitSymbol(NameOf(name), isProgram: true, [DiagnosticBag.LocationOf(name)])
                    : new UnitSymbol(Path.GetFileNameWithoutExtension(tree.Text.Path), isProgram: true, []);
                var scope = new Scope(ScopeKind.Unit, outer, symbol);
                symbol.ImplementationMembers = new DeclarationBinder(types, scope, symbol, diagnostics)
                    .BindDeclarations(program.Block.Declarations);
                return new DeclarationBindingResult(symbol, null, scope, diagnostics.ToImmutable());
            }

            default:
            {
                var symbol = new UnitSymbol(Path.GetFileNameWithoutExtension(tree.Text.Path), isProgram: false, []);
                return new DeclarationBindingResult(symbol, null, new Scope(ScopeKind.Unit, outer, symbol), diagnostics.ToImmutable());
            }
        }
    }

    /// <summary>Binds the sections in order; returns the declared symbols in declaration order.</summary>
    public ImmutableArray<Symbol> BindDeclarations(SyntaxList<SyntaxNode> declarations)
    {
        var declared = ImmutableArray.CreateBuilder<Symbol>();
        foreach (var declaration in declarations)
        {
            switch (declaration)
            {
                case ConstSectionSyntax constSection:
                    BindConstSection(constSection, declared);
                    break;
                case VarSectionSyntax varSection:
                    BindVarSection(varSection, declared);
                    break;
                case RoutineDeclarationSyntax routine:
                    BindRoutine(routine, declared);
                    break;
            }
        }

        ReportUnsatisfied();
        return declared.ToImmutable();
    }

    private void BindConstSection(ConstSectionSyntax section, ImmutableArray<Symbol>.Builder declared)
    {
        var isResourceString = section.Keyword.Kind == SyntaxKind.ResourceStringKeyword;
        foreach (var declaration in section.Declarations)
        {
            var identifier = declaration.Identifier;
            if (identifier.IsMissing)
            {
                continue;
            }

            ConstantSymbol symbol;
            if (declaration.Type != null)
            {
                // LANG-344 typed constants arrive with M2.2
                _diagnostics.Report(identifier, "E2900", "typed constant");
                symbol = Constant(identifier, _types.Error, value: null, isTyped: true);
            }
            else
            {
                var value = declaration.Value is ExpressionSyntax expression ? _folder.Fold(expression) : NotConstant(declaration.Value);
                if (isResourceString && value != null)
                {
                    value = AsResourceString(value, declaration.Value);
                }

                symbol = Constant(identifier, value?.Type ?? _types.Error, value, isTyped: false);
            }

            Declare(identifier, symbol, declared);
        }
    }

    private void BindVarSection(VarSectionSyntax section, ImmutableArray<Symbol>.Builder declared)
    {
        if (section.Keyword.Kind == SyntaxKind.ThreadVarKeyword)
        {
            _diagnostics.Report(section.Keyword, "E2900", "threadvar");     // LANG-345, M2.4
        }

        foreach (var declaration in section.Declarations)
        {
            var type = BindType(declaration.Type);
            var initializer = declaration.Initializer is { } node ? BindInitializer(declaration, node, type) : null;
            var linkName = declaration.PublicDirective is { } publicDirective ? BindVariablePublicName(declaration, publicDirective) : null;
            foreach (var name in declaration.Identifiers)
            {
                var identifier = name.Identifier;
                if (!identifier.IsMissing)
                {
                    Declare(identifier, new VariableSymbol(NameOf(identifier), type, IsGlobalLevel, _container,
                        [DiagnosticBag.LocationOf(identifier)], initializer) { ExplicitLinkName = linkName }, declared);
                }
            }
        }
    }

    /// <summary>LANG-470 <c>var x: T; public name 'sym';</c>: one global variable only (else E2900 / E1101).</summary>
    private string? BindVariablePublicName(VarDeclarationSyntax declaration, DirectiveSyntax directive)
    {
        if (!IsGlobalLevel)
        {
            _diagnostics.Report(directive.Keyword, "E2900", "public name on a local variable");
            return null;
        }

        if (declaration.Identifiers.Count > 1)
        {
            _diagnostics.Report(directive.Keyword, "E1101", ";", directive.Keyword.Text);
            return null;
        }

        return directive.NameValue is { } value ? FoldString(value) : null;
    }

    /// <summary>grammar.md varDecl: an initialiser only for one global variable; a constant expression (LANG-522).</summary>
    private ConstantValue? BindInitializer(VarDeclarationSyntax declaration, SyntaxNode initializer, TypeSymbol type)
    {
        if (!IsGlobalLevel)
        {
            _diagnostics.Report(initializer, "E2207");
            return null;
        }

        if (declaration.Identifiers.Count > 1)
        {
            _diagnostics.Report(declaration.EqualsToken!.Value, "E1101", ";", "=");
            return null;
        }

        if (initializer is not ExpressionSyntax expression)
        {
            _diagnostics.Report(initializer, "E2900", "structured initializer");   // array/record initialisers, M2.2
            return null;
        }

        // converted to the variable's type (LANG-280), so the value carries that type
        var value = _folder.Fold(expression);
        return value is null || type.TypeKind == TypeKind.Error ? value : _conversions.ConvertConstant(value, type, expression, _diagnostics);
    }

    // ------------------------------------------------------------------ routines (LANG-400…403, 440, 460…473)

    /// <summary>
    /// A routine declaration: either the definition of an earlier <c>forward</c>/interface declaration of this scope
    /// (LANG-401; completes that symbol) or a new routine, possibly joining an overload set (LANG-440).
    /// </summary>
    private void BindRoutine(RoutineDeclarationSyntax declaration, ImmutableArray<Symbol>.Builder declared)
    {
        var heading = declaration.Heading;
        if (!IsSupportedHeading(heading, out var identifier))
        {
            return;
        }

        var name = NameOf(identifier);
        var directives = BindDirectives(declaration.Directives);
        var existing = (_sharedScope?.LookupRoutinesLocal(name) ?? []).AddRange(_scope.LookupRoutinesLocal(name));
        if (declaration.Block != null && FindDeclarationToDefine(heading, identifier, existing) is { } target)
        {
            target.Definition = declaration;
            target.DefinitionScope = _scope;
            target.DefinitionScopeVisibleCount = _scope.Count;
            target.AddLocation(DiagnosticBag.LocationOf(identifier));
            return;
        }

        if (declaration.Block != null && existing.Any(r => r.IsExternal))
        {
            _diagnostics.Report(identifier, "E2104", name);                    // LANG-463: an external routine has no body
            return;
        }

        var routine = new RoutineSymbol(name, _container, [DiagnosticBag.LocationOf(identifier)], declaration)
        {
            IsOverload = directives.IsOverload,
            IsExternal = directives.IsExternal,
            IsInline = directives.IsInline,
            IsNoReturn = directives.IsNoReturn,
            IsDeprecated = directives.IsDeprecated,
            LibraryName = directives.LibraryName,
            ExplicitLinkName = directives.LinkName ?? (directives.IsExternal ? name : null),
        };
        BindSignature(routine, heading, identifier, report: true);
        if (declaration.Block != null)
        {
            routine.Definition = declaration;
            routine.DefinitionScope = _scope;
        }

        if (DeclareRoutine(identifier, routine, existing))
        {
            routine.DefinitionScopeVisibleCount = _scope.Count;
            declared.Add(routine);
            if (routine.AwaitsDefinition && !IsInterfaceSection)
            {
                _awaitingDefinition.Add(routine);
            }
        }
    }

    /// <summary>Plain procedures and functions; methods, operators, class routines and generic routines come later.</summary>
    private bool IsSupportedHeading(RoutineHeadingSyntax heading, out SyntaxToken identifier)
    {
        identifier = default;
        if (heading.Name is not IdentifierNameSyntax { Identifier: var token } || token.IsMissing)
        {
            if (heading.Name is not IdentifierNameSyntax)
            {
                _diagnostics.Report(heading.Name, "E2900", "method implementation");          // M2.4
            }

            return false;
        }

        identifier = token;
        var unsupported = heading.ClassKeyword != null ? "class routine"
            : heading.Keyword.Kind is not (SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword) ? heading.Keyword.Text.ToLowerInvariant()
            : heading.TypeParameterList != null ? "generic routine"
            : null;
        if (unsupported != null)
        {
            _diagnostics.Report(heading, "E2900", unsupported);
            return false;
        }

        return true;
    }

    private sealed record RoutineDirectives(bool IsForward, bool IsOverload, bool IsExternal, bool IsInline, bool IsNoReturn,
        bool IsDeprecated, string? LibraryName, string? LinkName);

    /// <summary>LANG-460…473: calling conventions, <c>assembler</c> and hints are accepted; method directives are E1114.</summary>
    private RoutineDirectives BindDirectives(SyntaxList<DirectiveSyntax> directives)
    {
        bool forward = false, overload = false, external = false, inline = false, noReturn = false, deprecated = false;
        string? library = null, linkName = null;
        foreach (var directive in directives)
        {
            switch (directive.Keyword.ContextualKind)
            {
                case SyntaxKind.ForwardKeyword:
                    forward = true;
                    break;
                case SyntaxKind.OverloadKeyword:
                    overload = true;
                    break;
                case SyntaxKind.InlineKeyword:
                    inline = true;
                    break;
                case SyntaxKind.NoReturnKeyword:
                    noReturn = true;
                    break;
                case SyntaxKind.DeprecatedKeyword:
                    deprecated = true;
                    break;
                case SyntaxKind.ExternalKeyword:
                    external = true;
                    if (directive.DelayedKeyword is { } delayed)
                    {
                        _diagnostics.Report(delayed, "E2900", "delayed");
                    }

                    library = directive.Value is { } lib ? FoldString(lib) : null;
                    linkName = directive.NameValue is { } import ? FoldString(import) : null;
                    break;
                case SyntaxKind.PublicKeyword:
                    linkName = directive.NameValue is { } exported ? FoldString(exported) : null;
                    break;
                case SyntaxKind.VirtualKeyword or SyntaxKind.DynamicKeyword or SyntaxKind.OverrideKeyword or SyntaxKind.AbstractKeyword
                    or SyntaxKind.FinalKeyword or SyntaxKind.ReintroduceKeyword or SyntaxKind.StaticKeyword or SyntaxKind.MessageKeyword:
                    _diagnostics.Report(directive.Keyword, "E1114", directive.Keyword.Text);
                    break;
            }
        }

        return new RoutineDirectives(forward, overload, external, inline, noReturn, deprecated, library, linkName);
    }

    /// <summary>A constant string expression (library and link names); E2010 for another constant.</summary>
    private string? FoldString(ExpressionSyntax expression)
    {
        switch (_folder.Fold(expression))
        {
            case { Value: string text }:
                return text;
            case { Value: System.Numerics.BigInteger code, Type: CharTypeSymbol }:
                return ((char)(int)code).ToString();
            case { } other:
                _diagnostics.Report(expression, "E2010", other.Type.Name, _types.String.Name);
                return null;
            default:
                return null;
        }
    }

    /// <summary>Parameters (LANG-420 table) and result type of a new routine; also creates <c>Result</c> (LANG-410).</summary>
    private void BindSignature(RoutineSymbol routine, RoutineHeadingSyntax heading, SyntaxToken identifier, bool report)
    {
        if (heading.Keyword.Kind == SyntaxKind.FunctionKeyword)
        {
            if (heading.ResultType is { } resultType)
            {
                routine.ReturnType = BindType(resultType);
            }
            else
            {
                _diagnostics.Report(identifier, "E2102");
                routine.ReturnType = _types.Error;
            }

            routine.ResultVariable = new VariableSymbol("Result", routine.ReturnType, isGlobal: false, routine, []);
        }
        else if (heading.ResultType is { } procedureResult && report)
        {
            _diagnostics.Report(procedureResult, "E2103");
        }

        var names = new Scope(ScopeKind.Routine, parent: null, routine);
        if (routine.ResultVariable is { } result)
        {
            names.TryDeclare(result, out _);
        }

        var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>();
        foreach (var parameter in heading.ParameterList?.Parameters ?? default)
        {
            var mode = parameter.Modifier?.ContextualKind switch
            {
                SyntaxKind.ConstKeyword => ParameterMode.Const,
                SyntaxKind.VarKeyword => ParameterMode.Var,
                SyntaxKind.OutKeyword => ParameterMode.Out,
                _ => ParameterMode.Value,
            };
            var type = parameter.Type is { } typeSyntax ? BindType(typeSyntax) : _types.Error;
            if (parameter.Type is null && mode != ParameterMode.Value && report)
            {
                _diagnostics.Report(parameter, "E2900", "untyped parameter");                  // M2.2
            }

            if (parameter.DefaultValue is { } defaultValue && report)
            {
                _diagnostics.Report(defaultValue, "E2900", "default parameter value");         // LANG-422, M2.5
            }

            foreach (var name in parameter.Identifiers)
            {
                var token = name.Identifier;
                if (token.IsMissing)
                {
                    continue;
                }

                if (parameter.Type is null && mode == ParameterMode.Value && report)
                {
                    _diagnostics.Report(token, "E2118", NameOf(token));
                }

                var symbol = new ParameterSymbol(NameOf(token), type, mode, parameters.Count, routine, [DiagnosticBag.LocationOf(token)]);
                if (!names.TryDeclare(symbol, out var previous) && report)
                {
                    _diagnostics.ReportRedeclared(token, NameOf(token), previous!);
                }

                parameters.Add(symbol);
            }
        }

        routine.Parameters = parameters.ToImmutable();
    }

    /// <summary>
    /// LANG-401: the undefined declaration a definition completes. With its parameter list omitted it completes the
    /// only undefined routine of the name; otherwise the one with the same parameter modes and types (names and result
    /// type must match too, E2101). A mismatch against a single non-overloaded declaration is E2101 and still completes
    /// it (no E2100 cascade); otherwise the definition declares a new routine.
    /// </summary>
    private RoutineSymbol? FindDeclarationToDefine(RoutineHeadingSyntax heading, SyntaxToken identifier, ImmutableArray<RoutineSymbol> existing)
    {
        var undefined = existing.Where(r => r.AwaitsDefinition).ToList();
        if (undefined.Count == 0)
        {
            return null;
        }

        if (heading.ParameterList is null && undefined.Count == 1)
        {
            var only = undefined[0];
            var omitted = heading.ResultType is null || ReferenceEquals(BindType(heading.ResultType), only.ReturnType);
            if (!omitted || (heading.Keyword.Kind == SyntaxKind.FunctionKeyword) != (only.ReturnType != null))
            {
                _diagnostics.Report(identifier, "E2101", NameOf(identifier));
            }

            return only;
        }

        var probe = new RoutineSymbol(NameOf(identifier), _container, [], undefined[0].Declaration);
        BindSignature(probe, heading, identifier, report: false);
        var match = undefined.FirstOrDefault(r => SameParameterTypes(r, probe));
        if (match is null)
        {
            if (undefined.Count == 1 && !undefined[0].IsOverload)
            {
                _diagnostics.Report(identifier, "E2101", NameOf(identifier));
                return undefined[0];
            }

            return null;
        }

        var sameNames = match.Parameters.Select(p => p.Name).SequenceEqual(probe.Parameters.Select(p => p.Name), IdentifierComparer.Instance);
        if (!sameNames || !ReferenceEquals(match.ReturnType, probe.ReturnType))
        {
            _diagnostics.Report(identifier, "E2101", NameOf(identifier));
        }

        return match;
    }

    private static bool SameParameterTypes(RoutineSymbol a, RoutineSymbol b) =>
        a.Parameters.Length == b.Parameters.Length
        && a.Parameters.Zip(b.Parameters).All(p => p.First.Mode == p.Second.Mode && ReferenceEquals(p.First.Type, p.Second.Type));

    /// <summary>LANG-440: a name shared only by routines that all carry <c>overload</c> and differ in their parameters.</summary>
    private bool DeclareRoutine(SyntaxToken identifier, RoutineSymbol routine, ImmutableArray<RoutineSymbol> existing)
    {
        var name = NameOf(identifier);
        if (existing.IsEmpty)
        {
            var previous = _sharedScope?.LookupLocal(name);
            if (previous is null && _scope.TryDeclare(routine, out previous))
            {
                return true;
            }

            _diagnostics.ReportRedeclared(identifier, name, previous!);
            return false;
        }

        if (!routine.IsOverload || existing.Any(r => !r.IsOverload))
        {
            _diagnostics.Report(identifier, "E2120", name);
            return false;
        }

        if (existing.Any(r => SameParameterTypes(r, routine)))
        {
            _diagnostics.Report(identifier, "E2121", name);
            return false;
        }

        if (_scope.LookupLocal(name) is null)
        {
            _scope.TryDeclare(routine, out _);                                  // overloads an interface routine
        }
        else
        {
            _scope.AddOverload(routine);
        }

        return true;
    }

    /// <summary>LANG-400/460: E2100 for each forward routine of this declaration part left without a definition.</summary>
    private void ReportUnsatisfied()
    {
        foreach (var routine in _awaitingDefinition.Where(r => r.AwaitsDefinition))
        {
            _diagnostics.Report(routine.Declaration.Heading.Name, "E2100", routine.Name);
        }
    }

    /// <summary>The type a type syntax denotes; the error type (after a diagnostic) when it cannot be bound.</summary>
    private TypeSymbol BindType(TypeSyntax type)
    {
        if (type.IsMissing)
        {
            return _types.Error;
        }

        switch (type)
        {
            case NamedTypeSyntax { Name: IdentifierNameSyntax name }:
                return BindTypeName(name);
            case NamedTypeSyntax { Name: QualifiedNameSyntax }:
                return Unsupported(type, "unit-qualified name");        // LANG-311, with units
            case NamedTypeSyntax:
                return Unsupported(type, "generic type");
            case StringTypeSyntax { Length: null }:
                return _types.String;
            case StringTypeSyntax:
                return Unsupported(type, "short string type");          // M2.2
            default:
                return Unsupported(type, FeatureName(type.Kind));        // type constructors arrive with M2.2+
        }
    }

    private TypeSymbol BindTypeName(IdentifierNameSyntax name)
    {
        var identifier = name.Identifier;
        if (identifier.IsMissing)
        {
            return _types.Error;
        }

        var text = NameOf(identifier);
        switch (_scope.Lookup(text))
        {
            case null:
                _diagnostics.ReportUndeclared(name, text);
                return _types.Error;
            case TypeSymbol type when ReferenceEquals(type, _types.Currency):
                return Unsupported(name, "Currency");                   // LANG-200, Phase 8+
            case TypeSymbol type:
                return type;
            default:
                _diagnostics.Report(name, "E2002", text);
                return _types.Error;
        }
    }

    private TypeSymbol Unsupported(SyntaxNode node, string feature)
    {
        _diagnostics.Report(node, "E2900", feature);
        return _types.Error;
    }

    /// <summary>"ArrayType" gives "array type".</summary>
    internal static string FeatureName(SyntaxKind kind) =>
        string.Concat(kind.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    /// <summary>LANG-346: string true constants only (a character constant becomes a one-character string).</summary>
    private ConstantValue? AsResourceString(ConstantValue value, SyntaxNode node)
    {
        switch (value.Value)
        {
            case string:
                return value;
            case System.Numerics.BigInteger code when value.Type is CharTypeSymbol:
                return new ConstantValue(((char)(int)code).ToString(), _types.String);
            default:
                _diagnostics.Report(node, "E2010", value.Type.Name, _types.String.Name);
                return null;
        }
    }

    private ConstantValue? NotConstant(SyntaxNode node)
    {
        if (!node.ContainsDiagnostics)
        {
            _diagnostics.Report(node, "E2050");
        }

        return null;
    }

    private ConstantSymbol Constant(SyntaxToken identifier, TypeSymbol type, ConstantValue? value, bool isTyped) =>
        new(NameOf(identifier), type, value, isTyped, _container, [DiagnosticBag.LocationOf(identifier)]);

    /// <summary>Adds the symbol to the scope; E2004 when the scope already declares the name (LANG-402, LANG-518).</summary>
    private void Declare(SyntaxToken identifier, Symbol symbol, ImmutableArray<Symbol>.Builder declared)
    {
        var previous = _sharedScope?.LookupLocal(symbol.Name);
        if (previous is null && _scope.TryDeclare(symbol, out previous))
        {
            declared.Add(symbol);
        }
        else
        {
            _diagnostics.ReportRedeclared(identifier, NameOf(identifier), previous!);
        }
    }

    private static string NameOf(SyntaxToken identifier) => identifier.Value as string ?? identifier.Text;

    private static string NameOf(NameSyntax name) =>
        string.Join(".", name.DescendantTokens().Where(t => t.Kind == SyntaxKind.IdentifierToken).Select(NameOf));
}
