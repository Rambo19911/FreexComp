using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Property reads and writes (LANG-641…647, CMP-367 last row, C-085): resolved in the binder to field accesses and accessor calls.
internal sealed partial class ExpressionBinder
{
    /// <summary>The whole target of the assignment being bound: a property there is written, not read.</summary>
    private ExpressionSyntax? _assignmentTarget;

    /// <summary>The element access whose indexed expression is being bound: an array property there takes its indexes.</summary>
    private ElementAccessExpressionSyntax? _indexedBy;

    /// <summary>How many of <see cref="_indexedBy"/>'s indexes an array property took.</summary>
    private int _indexesTaken;

    /// <summary>
    /// The target of <c>target := value</c>: a variable reference, or a <see cref="BoundPropertyAccessExpression"/> when the
    /// target as a whole is a property (resolved by <see cref="BindPropertyAssignment"/>).
    /// </summary>
    public BoundExpression BindAssignmentTarget(ExpressionSyntax syntax)
    {
        var saved = _assignmentTarget;
        _assignmentTarget = Unparenthesized(syntax);
        try
        {
            return BindVariable(syntax);
        }
        finally
        {
            _assignmentTarget = saved;
        }
    }

    /// <summary>
    /// LANG-641: a write through the <c>write</c> accessor — a store into the field path or a setter call with the index
    /// arguments, the <c>index</c> value and the converted value; E2633 without accessor.
    /// </summary>
    public BoundStatement BindPropertyAssignment(AssignmentStatementSyntax assignment, BoundPropertyAccessExpression target)
    {
        var property = target.Property;
        var value = BindExpression(assignment.Value, property.Type);
        if (!property.IsWritable)
        {
            _diagnostics.Report(assignment.Target, "E2633");
            return new BoundErrorStatement(assignment);
        }

        var converted = ConvertForAssignment(value, property.Type, assignment.Value);
        if (converted.HasErrors)
        {
            return new BoundErrorStatement(assignment);
        }

        if (property.WriteMethod is not { } setter)
        {
            return new BoundAssignmentStatement(assignment, FieldPath(target.Syntax, property, property.WritePath, target.Self, marked: false), converted);
        }

        if (target.IsInherited && setter.IsAbstract)
        {
            _diagnostics.Report(assignment.Target, "E2626", setter.Name);              // LANG-624
            return new BoundErrorStatement(assignment);
        }

        return new BoundCallStatement(assignment, setter, [.. AccessorArguments(target.Syntax, property, target.Indexes), converted])
        {
            Receiver = AccessorReceiver(setter, target.Self, target.IsInherited),
        };
    }

    /// <summary>
    /// LANG-624 (C-270) <c>inherited Prop</c>: the property <c>Prop</c> of the nearest ancestor declaring that name, on <c>Self</c>
    /// (or of the class for a class property) with its accessor methods called statically; null when the ancestors' nearest
    /// member of that name is no property (the caller binds a method call).
    /// </summary>
    private BoundExpression? InheritedProperty(InheritedExpressionSyntax inherited)
    {
        if (inherited.Name is not { Identifier: var token } name
            || CurrentMethod is not { Owner: ClassTypeSymbol { Parent: { } parent } owner } current
            || parent.GetMembers(token.Value as string ?? token.Text).FirstOrDefault() is not PropertySymbol property)
        {
            return null;
        }

        if (!CheckAccess(name, property, property.Owner, owner))
        {
            return Error(inherited);
        }

        BoundExpression? self = null;
        if (!property.IsClassProperty)
        {
            if (!HasInstanceSelf(current))
            {
                _diagnostics.Report(name, "E2608", property.Name);
                return Error(inherited);
            }

            self = SelfOf(current, inherited);
        }

        return PropertyAccess(inherited, property, self, inherited: true);
    }

    /// <summary>A property named inside a method (LANG-621): of <c>Self</c>, or of the class for a class property.</summary>
    private BoundExpression ImplicitProperty(IdentifierNameSyntax name, PropertySymbol property)
    {
        var current = CurrentMethod;
        if (!CheckAccess(name, property, property.Owner, current?.Owner))
        {
            return Error(name);
        }

        BoundExpression? self = null;
        if (!property.IsClassProperty)
        {
            if (current is null || !HasInstanceSelf(current))
            {
                _diagnostics.Report(name, "E2608", property.Name);
                return Error(name);
            }

            self = SelfOf(current, name);
        }

        return PropertyAccess(name, property, self);
    }

    /// <summary>
    /// A property of <paramref name="self"/> (an instance, a class reference or null for class properties) at <paramref name="syntax"/>:
    /// an array property takes the indexes of the enclosing element access (E2110 when there is none or too few); the whole
    /// assignment target becomes a <see cref="BoundPropertyAccessExpression"/>, anything else a read (E2632 without <c>read</c>).
    /// </summary>
    private BoundExpression PropertyAccess(ExpressionSyntax syntax, PropertySymbol property, BoundExpression? self, bool inherited = false)
    {
        SyntaxNode at = syntax;
        var indexes = ImmutableArray<BoundExpression>.Empty;
        if (property.IsArrayProperty)
        {
            if (_indexedBy is not { } element || !ReferenceEquals(Unparenthesized(element.Expression), syntax)
                || element.Indexes.Count < property.Parameters.Length)
            {
                _diagnostics.Report(syntax, "E2110", property.Name);
                return Error(syntax);
            }

            _indexedBy = null;
            _indexesTaken = property.Parameters.Length;
            at = element;
            var bound = BindPropertyIndexes(property, [.. element.Indexes.Take(property.Parameters.Length)]);
            if (bound.IsDefault)
            {
                return Error(element);
            }

            indexes = bound;
            if (element.Indexes.Count == property.Parameters.Length && ReferenceEquals(_assignmentTarget, element))
            {
                return new BoundPropertyAccessExpression(element, property, self, indexes) { IsInherited = inherited };
            }
        }
        else if (ReferenceEquals(_assignmentTarget, syntax))
        {
            return new BoundPropertyAccessExpression(syntax, property, self, indexes) { IsInherited = inherited };
        }

        return PropertyRead(at, property, self, indexes, inherited);
    }

    /// <summary>
    /// LANG-642 <c>Obj[i]</c>: the default array property of <paramref name="self"/>'s class with the indexes of
    /// <paramref name="element"/> from <paramref name="start"/> on (the caller checked there are enough).
    /// </summary>
    private BoundExpression DefaultPropertyAccess(ElementAccessExpressionSyntax element, int start, PropertySymbol property, BoundExpression self)
    {
        var count = property.Parameters.Length;
        if (property.ContainingInterface is null && !CheckAccess(element, property, property.Owner, self.Type))
        {
            return Error(element, self);
        }

        var indexes = BindPropertyIndexes(property, [.. element.Indexes.Skip(start).Take(count)]);
        if (indexes.IsDefault)
        {
            return Error(element, self);
        }

        if (start + count == element.Indexes.Count && ReferenceEquals(_assignmentTarget, element))
        {
            return new BoundPropertyAccessExpression(element, property, self, indexes);
        }

        return PropertyRead(element, property, self, indexes);
    }

    /// <summary>The index arguments of an array property, each converted to its parameter type; default after an error.</summary>
    private ImmutableArray<BoundExpression> BindPropertyIndexes(PropertySymbol property, ImmutableArray<ExpressionSyntax> syntaxes)
    {
        var builder = ImmutableArray.CreateBuilder<BoundExpression>(syntaxes.Length);
        var ok = true;
        for (var i = 0; i < syntaxes.Length; i++)
        {
            var value = ConvertForAssignment(BindExpression(syntaxes[i], property.Parameters[i].Type), property.Parameters[i].Type, syntaxes[i]);
            ok &= !value.HasErrors;
            builder.Add(value);
        }

        return ok ? builder.MoveToImmutable() : default;
    }

    /// <summary>LANG-641 read: the field path, or a getter call with the index arguments and the <c>index</c> value.</summary>
    private BoundExpression PropertyRead(SyntaxNode syntax, PropertySymbol property, BoundExpression? self, ImmutableArray<BoundExpression> indexes,
        bool inherited = false)
    {
        if (!property.IsReadable)
        {
            _diagnostics.Report(syntax, "E2632");
            return Error(syntax);
        }

        if (property.ReadMethod is not { } getter)
        {
            return FieldPath(syntax, property, property.ReadPath, self, marked: true);
        }

        if (inherited && getter.IsAbstract)
        {
            _diagnostics.Report(syntax, "E2626", getter.Name);                         // LANG-624
            return Error(syntax);
        }

        return new BoundCallExpression(syntax, getter, AccessorArguments(syntax, property, indexes)) { Receiver = AccessorReceiver(getter, self, inherited) };
    }

    /// <summary>The field accessor path on <paramref name="self"/>; a read is marked with the property (not a variable, LANG-645).</summary>
    private BoundExpression FieldPath(SyntaxNode syntax, PropertySymbol property, ImmutableArray<Symbol> path, BoundExpression? self, bool marked)
    {
        BoundExpression result = path[0] switch
        {
            VariableSymbol classVar => new BoundVariableExpression(syntax, classVar) { Property = marked && path.Length == 1 ? property : null },
            var field => new BoundFieldAccessExpression(syntax, self!, (FieldSymbol)field) { Property = marked && path.Length == 1 ? property : null },
        };
        for (var i = 1; i < path.Length; i++)
        {
            result = new BoundFieldAccessExpression(syntax, result, (FieldSymbol)path[i]) { Property = marked && i == path.Length - 1 ? property : null };
        }

        return result;
    }

    /// <summary>LANG-642/643: index arguments, then the <c>index</c> value.</summary>
    private ImmutableArray<BoundExpression> AccessorArguments(SyntaxNode syntax, PropertySymbol property, ImmutableArray<BoundExpression> indexes) =>
        property.Index is { } index ? [.. indexes, new BoundLiteral(syntax, new ConstantValue(new BigInteger(index), _types.Integer))] : indexes;

    /// <summary>
    /// The receiver of an accessor method call on <paramref name="self"/>: virtual accessors dispatch (LANG-622) except through
    /// <c>inherited Prop</c> (LANG-624).
    /// </summary>
    private BoundReceiver? AccessorReceiver(MethodSymbol accessor, BoundExpression? self, bool inherited = false) =>
        accessor.SelfParameter is null || self is null ? null : Dispatched(accessor, self, inherited);

    /// <summary>LANG-645: a property read (field accessor) is no variable for <c>var</c> arguments, <c>@</c>, <c>Inc</c>.</summary>
    internal static bool IsPropertyRead(BoundExpression expression)
    {
        while (true)
        {
            switch (expression)
            {
                case BoundFieldAccessExpression { Property: not null }:
                case BoundVariableExpression { Property: not null }:
                    return true;
                case BoundFieldAccessExpression { Record.Type: ClassTypeSymbol }:
                    return false;                                       // a field of the object a reference points to
                case BoundFieldAccessExpression field:
                    expression = field.Record;
                    break;
                case BoundIndexExpression index:
                    expression = index.Array;
                    break;
                case BoundVariableTypecastExpression typecast:
                    expression = typecast.Operand;
                    break;
                default:
                    return false;
            }
        }
    }
}
