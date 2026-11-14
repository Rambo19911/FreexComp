using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Class properties (LANG-641…647, C-085): accessors, array and default properties, index, class properties, redeclaration.
internal sealed partial class DeclarationBinder
{
    /// <summary>
    /// The properties of a class body, bound after its fields and methods so that accessors may be declared anywhere in the
    /// class. A property without type redeclares the nearest inherited one (LANG-613, 646; E2637 when there is none).
    /// </summary>
    private ImmutableArray<PropertySymbol> BindProperties(ClassTypeSymbol type, List<(PropertyDeclarationSyntax Syntax, Visibility Visibility)> properties,
        Dictionary<string, Symbol> names)
    {
        var bound = ImmutableArray.CreateBuilder<PropertySymbol>();
        foreach (var (syntax, visibility) in properties)
        {
            if (syntax.Identifier.IsMissing)
            {
                continue;
            }

            var name = NameOf(syntax.Identifier);
            var property = new PropertySymbol(name, type, [DiagnosticBag.LocationOf(syntax.Identifier)], visibility)
            {
                IsClassProperty = syntax.ClassKeyword is not null,
            };
            if (!names.TryAdd(name, property))
            {
                _diagnostics.ReportRedeclared(syntax.Identifier, name, names[name]);
                continue;
            }

            if (BindProperty(type, property, syntax))
            {
                if (property.IsDefault && bound.Any(p => p.IsDefault))
                {
                    _diagnostics.Report(syntax.DefaultKeyword!.Value, "E2635", type.Name);                  // LANG-642
                    property.IsDefault = false;
                }

                bound.Add(property);
            }
        }

        return bound.ToImmutable();
    }

    /// <summary>Type, index parameters and specifiers of one property; false after an error that leaves no usable property.</summary>
    private bool BindProperty(ClassTypeSymbol type, PropertySymbol property, PropertyDeclarationSyntax syntax)
    {
        if (syntax.Type is null)
        {
            if (type.Parent?.GetMembers(property.Name).FirstOrDefault() is not PropertySymbol inherited)
            {
                _diagnostics.Report(syntax.Identifier, "E2637", property.Name);                            // LANG-646
                return false;
            }

            property.Type = inherited.Type;
            property.Parameters = inherited.Parameters;
            property.Index = inherited.Index;
            property.IsDefault = inherited.IsDefault;
            property.ReadPath = inherited.ReadPath;
            property.ReadMethod = inherited.ReadMethod;
            property.WritePath = inherited.WritePath;
            property.WriteMethod = inherited.WriteMethod;
        }
        else
        {
            property.Type = BindType(syntax.Type);
            property.Parameters = BindParameters(syntax.Parameters, property, new Scope(ScopeKind.Routine, parent: null, property), report: true);
            if (property.Type.TypeKind == TypeKind.Error || property.Parameters.Any(p => p.Type.TypeKind == TypeKind.Error))
            {
                return false;
            }
        }

        if (syntax.DefaultKeyword is { } defaultKeyword)
        {
            if (!property.IsArrayProperty)
            {
                _diagnostics.Report(defaultKeyword, "E2900", "default on a non-array property");          // LANG-642 (spec gap)
                return false;
            }

            property.IsDefault = true;
        }

        var ok = true;
        var index = syntax.Specifiers.FirstOrDefault(s => s.Keyword.ContextualKind == SyntaxKind.IndexKeyword);
        if (index?.Value is { } indexValue)
        {
            ok &= BindPropertyIndex(property, indexValue);
        }

        foreach (var specifier in syntax.Specifiers)
        {
            switch (specifier.Keyword.ContextualKind)
            {
                case SyntaxKind.ReadKeyword when specifier.Value is { } value:
                    property.ReadPath = [];
                    property.ReadMethod = null;
                    ok &= BindAccessor(type, property, value, write: false);
                    break;
                case SyntaxKind.WriteKeyword when specifier.Value is { } value:
                    property.WritePath = [];
                    property.WriteMethod = null;
                    ok &= BindAccessor(type, property, value, write: true);
                    break;
                case SyntaxKind.ImplementsKeyword:
                    Unsupported(specifier.Keyword, "implements");                                          // LANG-661, M2.5
                    ok = false;
                    break;
            }
        }

        // LANG-644: stored, default c and nodefault only feed RTTI (M2.5)
        if (ok && !property.IsReadable && !property.IsWritable)
        {
            _diagnostics.Report(syntax.Identifier, "E2631", property.Name);                                // LANG-641
            return false;
        }

        return ok;
    }

    /// <summary>LANG-643: <c>index N</c> with an integer constant.</summary>
    private bool BindPropertyIndex(PropertySymbol property, ExpressionSyntax value)
    {
        if (_folder.Fold(value) is not { } constant)
        {
            return false;
        }

        if (constant.Value is not BigInteger number || number < int.MinValue || number > int.MaxValue)
        {
            _diagnostics.Report(value, "E2014", "Integer");
            return false;
        }

        property.Index = (int)number;
        return true;
    }

    /// <summary>
    /// A <c>read</c>/<c>write</c> accessor (LANG-641/642/643/647): a field path of the property's type — not for array properties
    /// (E2634) — or a method with the index parameters, then <c>Index</c>, then (setter) the value; E2636 when it does not fit.
    /// Instance properties take instance fields and methods, class properties <c>class var</c>s and static class methods.
    /// </summary>
    private bool BindAccessor(ClassTypeSymbol type, PropertySymbol property, ExpressionSyntax value, bool write)
    {
        var text = value.ToString().Trim();
        var path = ImmutableArray.CreateBuilder<Symbol>();
        var methods = ResolveAccessor(type, value, path, property.Type.Name);
        if (methods is null)
        {
            return false;
        }

        if (methods.Count == 0)
        {
            if (property.IsArrayProperty)
            {
                _diagnostics.Report(value, "E2634");
                return false;
            }

            var first = path[0];
            var fieldType = path[^1] switch { FieldSymbol f => f.Type, VariableSymbol v => v.Type, _ => null };
            if (first is FieldSymbol == property.IsClassProperty || !ReferenceEquals(fieldType, property.Type))
            {
                _diagnostics.Report(value, "E2636", text, property.Type.Name);
                return false;
            }

            if (write)
            {
                property.WritePath = path.ToImmutable();
            }
            else
            {
                property.ReadPath = path.ToImmutable();
            }

            return true;
        }

        return MatchAccessor(property, methods, value, write);
    }

    /// <summary>The accessor method among <paramref name="methods"/> with the index parameters, <c>Index</c>, then (setter) the value; E2636 otherwise.</summary>
    private bool MatchAccessor(PropertySymbol property, List<MethodSymbol> methods, ExpressionSyntax value, bool write)
    {
        var text = value.ToString().Trim();
        List<TypeSymbol> expected = [.. property.Parameters.Select(p => p.Type)];
        if (property.Index is not null)
        {
            expected.Add(_types.Integer);
        }

        if (write)
        {
            expected.Add(property.Type);
        }

        var kind = property.IsClassProperty ? MethodKind.ClassStatic : MethodKind.Ordinary;
        var method = methods.FirstOrDefault(m => m.MethodKind == kind
            && (write ? m.ReturnType is null : ReferenceEquals(m.ReturnType, property.Type))
            && m.Parameters.Length == expected.Count
            && m.Parameters.Zip(expected).All(p => p.First.Mode is ParameterMode.Value or ParameterMode.Const && ReferenceEquals(p.First.Type, p.Second)));
        if (method is null)
        {
            _diagnostics.Report(value, "E2636", text, property.Type.Name);
            return false;
        }

        if (write)
        {
            property.WriteMethod = method;
        }
        else
        {
            property.ReadMethod = method;
        }

        return true;
    }

    /// <summary>
    /// The member an accessor names: the methods of that name (nearest declaring class, overloads over the chain) or, with an
    /// empty result, the field path added to <paramref name="path"/>. Null after a reported error.
    /// </summary>
    private List<MethodSymbol>? ResolveAccessor(ClassTypeSymbol type, ExpressionSyntax value, ImmutableArray<Symbol>.Builder path, string propertyType)
    {
        switch (value)
        {
            case IdentifierNameSyntax { Identifier: { IsMissing: false } token } name:
            {
                var text = NameOf(token);
                var methods = new List<MethodSymbol>();
                foreach (var owner in type.SelfAndAncestors())
                {
                    if (owner.Fields.FirstOrDefault(f => IdentifierComparer.Instance.Equals(f.Name, text)) is { } field)
                    {
                        if (methods.Count > 0)
                        {
                            break;
                        }

                        path.Add(field);
                        return methods;
                    }

                    if (owner.ClassVariables.FirstOrDefault(v => IdentifierComparer.Instance.Equals(v.Name, text)) is { } classVar)
                    {
                        if (methods.Count > 0)
                        {
                            break;
                        }

                        path.Add(classVar);
                        return methods;
                    }

                    var own = owner.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, text)).ToList();
                    methods.AddRange(own);
                    if (own.Count > 0 && own.Any(m => !m.IsOverload))
                    {
                        break;
                    }
                }

                if (methods.Count == 0)
                {
                    _diagnostics.ReportUndeclared(name, text);
                    return null;
                }

                return methods;
            }

            case QualifiedNameSyntax { Right: IdentifierNameSyntax { Identifier: { IsMissing: false } fieldToken } right } qualified:
            {
                var inner = ResolveAccessor(type, qualified.Left, path, propertyType);
                if (inner is null)
                {
                    return null;
                }

                var record = path.Count == 0 ? null : (path[^1] switch { FieldSymbol f => f.Type, VariableSymbol v => v.Type, _ => null }) as RecordTypeSymbol;
                if (inner.Count > 0 || record is null)
                {
                    _diagnostics.Report(qualified.Left, "E2006", qualified.Left.ToString().Trim());
                    return null;
                }

                var fieldName = NameOf(fieldToken);
                if (record.GetMembers(fieldName).FirstOrDefault() is not FieldSymbol recordField)
                {
                    _diagnostics.Report(right, "E2005", record.Name, fieldName);
                    return null;
                }

                path.Add(recordField);
                return inner;
            }

            default:
                _diagnostics.Report(value, "E2636", value.ToString().Trim(), propertyType);
                return null;
        }
    }
}
