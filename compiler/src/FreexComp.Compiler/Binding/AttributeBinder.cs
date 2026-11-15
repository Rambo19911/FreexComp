using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// Attributes (LANG-760…763, C-126): every <c>[…]</c> of a unit is resolved once its declarations are bound. <c>Name</c> is a class
/// descending from <c>TCustomAttribute</c>, else <c>NameAttribute</c>; <c>Weak</c>/<c>Unsafe</c>/<c>Volatile</c> are recognised and
/// ignored (H2010), <c>Ref</c> is accepted; anything else is W1070 and ignored. Arguments are checked against the constructors.
/// Attributes of a unit-level class are kept in its RTTI (<see cref="ClassTypeSymbol.Attributes"/>): one thunk function each that
/// creates the instance; all others are discarded after the check.
/// </summary>
internal static class AttributeBinder
{
    private static readonly string[] Ignored = ["Weak", "Unsafe", "Volatile"];

    /// <summary>Resolves the attributes of <paramref name="tree"/>; returns the bound thunk bodies of class attributes.</summary>
    public static ImmutableArray<BoundRoutineBody> Bind(SyntaxTree tree, UnitSymbol unit, Scope scope, BuiltInTypes types, DiagnosticBag diagnostics)
    {
        var lists = tree.Root.DescendantNodes().OfType<AttributeListSyntax>().ToList();
        if (lists.Count == 0)
        {
            return [];
        }

        var root = scope.Lookup("TCustomAttribute") as ClassTypeSymbol;
        var thunks = ImmutableArray.CreateBuilder<BoundRoutineBody>();
        var perClass = new Dictionary<ClassTypeSymbol, List<(ClassTypeSymbol, RoutineSymbol)>>();
        foreach (var list in lists)
        {
            var owner = list.Parent is TypeDeclarationSyntax { Identifier: { IsMissing: false } identifier, TypeParameterList: null } declaration
                && declaration.Type is ClassTypeSyntax
                && scope.Lookup(identifier.Value as string ?? identifier.Text) is ClassTypeSymbol @class && ReferenceEquals(@class.ContainingSymbol, unit)
                    ? @class : null;
            foreach (var attribute in list.Attributes)
            {
                if (Resolve(attribute, scope, root, diagnostics) is not { } type)
                {
                    continue;
                }

                var binder = new ExpressionBinder(types, scope, diagnostics);
                var construct = binder.BindAttributeConstructor(attribute, type);
                if (construct.HasErrors || owner is null)
                {
                    continue;                                                   // LANG-762: checked, then discarded
                }

                var classAttributes = perClass.TryGetValue(owner, out var existing) ? existing : perClass[owner] = [];
                var thunk = new RoutineSymbol(owner.Name + "#attr" + (classAttributes.Count + 1), unit, [], declaration: null!) { ReturnType = root };
                thunk.ResultVariable = new VariableSymbol("Result", root!, isGlobal: false, thunk, []);
                var thunkScope = new Scope(ScopeKind.Routine, parent: null, thunk);
                thunkScope.TryDeclare(thunk.ResultVariable, out _);
                var body = new BoundBlock(attribute, [new BoundAssignmentStatement(attribute, new BoundVariableExpression(attribute, thunk.ResultVariable),
                    new BoundConversionExpression(attribute, construct, root!, ConversionKind.ImplicitReference, isExplicit: false, constantValue: null))]);
                thunks.Add(new BoundRoutineBody(thunk, thunkScope, body, [], [], []));
                classAttributes.Add((type, thunk));
            }
        }

        foreach (var (type, attributes) in perClass)
        {
            type.Attributes = [.. attributes];
        }

        return thunks.ToImmutable();
    }

    /// <summary>The attribute class named by <paramref name="attribute"/>, or null after H2010 / W1070.</summary>
    private static ClassTypeSymbol? Resolve(AttributeSyntax attribute, Scope scope, ClassTypeSymbol? root, DiagnosticBag diagnostics)
    {
        if (attribute.Name.ContainsDiagnostics || attribute.Name.IsMissing)
        {
            return null;                                                                // E1119 was reported
        }

        if (attribute.Name is not IdentifierNameSyntax { Identifier: { IsMissing: false } token } name)
        {
            diagnostics.Report(attribute.Name, "W1070", attribute.Name.ToString().Trim());
            return null;
        }

        var text = token.Value as string ?? token.Text;
        if (Ignored.Contains(text, IdentifierComparer.Instance))
        {
            diagnostics.Report(attribute, "H2010", text);                                // LANG-763: Phase 8+ semantics
            return null;
        }

        if (IdentifierComparer.Instance.Equals(text, "Ref"))
        {
            return null;                                                                // LANG-763: accepted
        }

        foreach (var candidate in new[] { text, text + "Attribute" })
        {
            if (root is not null && scope.For(name).Lookup(candidate) is ClassTypeSymbol type && type.InheritsFrom(root))
            {
                return type;
            }
        }

        diagnostics.Report(attribute, "W1070", text);                                    // LANG-760: unknown, ignored
        return null;
    }
}
