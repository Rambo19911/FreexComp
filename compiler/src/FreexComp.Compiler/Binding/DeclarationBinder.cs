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

    /// <summary>Unit and program level declarations are global; routine-level ones are locals.</summary>
    private bool IsGlobalLevel => _scope.Kind == ScopeKind.Unit;

    /// <summary>
    /// The unit-level scopes of a program (one scope) or unit (interface scope, then implementation scope inside it;
    /// LANG-310), chained to the built-in scope. Uses clauses and System are bound by later tasks.
    /// </summary>
    public static DeclarationBindingResult BindCompilationUnit(SyntaxTree tree, BuiltInTypes types)
    {
        var diagnostics = new DiagnosticBag();
        switch (tree.Root)
        {
            case UnitSyntax unit:
            {
                var symbol = new UnitSymbol(NameOf(unit.Name), isProgram: false, [DiagnosticBag.LocationOf(unit.Name)]);
                var interfaceScope = new Scope(ScopeKind.Unit, types.Scope, symbol);
                var implementationScope = new Scope(ScopeKind.Unit, interfaceScope, symbol);
                symbol.InterfaceMembers = new DeclarationBinder(types, interfaceScope, symbol, diagnostics)
                    .BindDeclarations(unit.InterfaceSection.Declarations);
                symbol.ImplementationMembers = new DeclarationBinder(types, implementationScope, symbol, diagnostics, interfaceScope)
                    .BindDeclarations(unit.ImplementationSection.Declarations);
                return new DeclarationBindingResult(symbol, interfaceScope, implementationScope, diagnostics.ToImmutable());
            }

            case ProgramSyntax program:
            {
                var symbol = program.Name is { IsMissing: false } name
                    ? new UnitSymbol(NameOf(name), isProgram: true, [DiagnosticBag.LocationOf(name)])
                    : new UnitSymbol(Path.GetFileNameWithoutExtension(tree.Text.Path), isProgram: true, []);
                var scope = new Scope(ScopeKind.Unit, types.Scope, symbol);
                symbol.ImplementationMembers = new DeclarationBinder(types, scope, symbol, diagnostics)
                    .BindDeclarations(program.Block.Declarations);
                return new DeclarationBindingResult(symbol, null, scope, diagnostics.ToImmutable());
            }

            default:
            {
                var symbol = new UnitSymbol(Path.GetFileNameWithoutExtension(tree.Text.Path), isProgram: false, []);
                return new DeclarationBindingResult(symbol, null, new Scope(ScopeKind.Unit, types.Scope, symbol), diagnostics.ToImmutable());
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
            }
        }

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
            var initializer = declaration.Initializer is { } node ? BindInitializer(declaration, node) : null;
            foreach (var name in declaration.Identifiers)
            {
                var identifier = name.Identifier;
                if (!identifier.IsMissing)
                {
                    Declare(identifier, new VariableSymbol(NameOf(identifier), type, IsGlobalLevel, _container,
                        [DiagnosticBag.LocationOf(identifier)], initializer), declared);
                }
            }
        }
    }

    /// <summary>grammar.md varDecl: an initialiser only for one global variable; a constant expression (LANG-522).</summary>
    private ConstantValue? BindInitializer(VarDeclarationSyntax declaration, SyntaxNode initializer)
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

        // assignment compatibility with the variable's type is checked from C-025 on
        return _folder.Fold(expression);
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
    private static string FeatureName(SyntaxKind kind) =>
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
