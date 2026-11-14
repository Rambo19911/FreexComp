using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// Binds expressions of routine bodies and initialisers to typed <see cref="BoundExpression"/>s (integer, Boolean,
/// char and real operands: LANG-220…230, LANG-300…306). Operands are wrapped in implicit conversions to the type the
/// operation is done in; constant sub-expressions are folded by the <see cref="ConstantFolder"/> (LANG-340). An
/// operand of the error type gives the error type without further diagnostics (CMP-135).
/// </summary>
internal sealed class ExpressionBinder
{
    private readonly BuiltInTypes _types;
    private readonly Scope _scope;
    private readonly DiagnosticBag _diagnostics;
    private readonly ConstantFolder _folder;
    private readonly Conversions _conversions = new();
    private readonly RoutineSymbol? _routine;

    /// <param name="routine">The routine whose body is bound; null at unit level.</param>
    public ExpressionBinder(BuiltInTypes types, Scope scope, DiagnosticBag diagnostics, RoutineSymbol? routine = null)
    {
        _types = types;
        _scope = scope;
        _diagnostics = diagnostics;
        _routine = routine;
        _folder = new ConstantFolder(types, scope, diagnostics);
    }

    /// <summary>Control variables (variables or value parameters) of the enclosing <c>for</c> loops (LANG-361 rule 6).</summary>
    public HashSet<Symbol> ForVariables { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Variables, parameters and <c>Result</c> of enclosing routines referenced here, in first-reference order (CMP-345).</summary>
    public List<Symbol> CapturedVariables { get; } = [];

    /// <summary>Records <paramref name="symbol"/> as captured when it belongs to an enclosing routine (LANG-450).</summary>
    public void NoteReference(Symbol symbol)
    {
        if (_routine != null && symbol is (VariableSymbol { IsGlobal: false } or ParameterSymbol)
            && symbol.ContainingSymbol is RoutineSymbol owner && !ReferenceEquals(owner, _routine) && !CapturedVariables.Contains(symbol))
        {
            CapturedVariables.Add(symbol);
        }
    }

    /// <summary>
    /// <paramref name="syntax"/> in a context of type <paramref name="expected"/>: a set constructor takes that set type
    /// (LANG-251); anything else binds as without context.
    /// </summary>
    public BoundExpression BindExpression(ExpressionSyntax syntax, TypeSymbol? expected) =>
        Unparenthesized(syntax) is SetConstructorSyntax constructor && expected is SetTypeSymbol { ElementType: not null } set
            ? BindSetConstructor(constructor, set)
            : BindExpression(syntax);

    private static ExpressionSyntax Unparenthesized(ExpressionSyntax syntax)
    {
        while (syntax is ParenthesizedExpressionSyntax parenthesized)
        {
            syntax = parenthesized.Expression;
        }

        return syntax;
    }

    public BoundExpression BindExpression(ExpressionSyntax syntax)
    {
        if (syntax.IsMissing)
        {
            return Error(syntax);
        }

        switch (syntax)
        {
            case SetConstructorSyntax constructor:
                return BindSetConstructor(constructor, expected: null);
            case LiteralExpressionSyntax literal:
                return BindLiteral(literal);
            case IdentifierNameSyntax name:
                return BindName(name);
            case ParenthesizedExpressionSyntax parenthesized:
                return BindExpression(parenthesized.Expression);
            case UnaryExpressionSyntax unary:
                return BindUnary(unary);
            case BinaryExpressionSyntax binary:
                return BindBinary(binary);
            case InvocationExpressionSyntax invocation:
                return BindInvocation(invocation);
            case AddressOfExpressionSyntax addressOf:
                return BindAddressOf(addressOf);
            case DereferenceExpressionSyntax dereference:
                return BindDereference(dereference, untyped: false);
            case ElementAccessExpressionSyntax elementAccess:
                return BindElementAccess(elementAccess);
            case PredefinedTypeExpressionSyntax predefined:
                _diagnostics.Report(predefined, "E2009", predefined.Keyword.Text);
                return Error(syntax);
            case MemberAccessExpressionSyntax memberAccess:
            {
                // TEnum.Element (LANG-210), record fields (LANG-260); unit and class qualifications arrive later
                var member = _folder.LookupEnumMember(memberAccess, out var isEnumAccess);
                return member is not null ? new BoundLiteral(memberAccess, member.Value)
                    : isEnumAccess ? Error(syntax)
                    : BindMemberAccess(memberAccess);
            }
            default:
                return Unsupported(syntax, DeclarationBinder.FeatureName(syntax.Kind));
        }
    }

    /// <summary>
    /// A condition of <c>if</c>/<c>while</c>/<c>repeat</c> (LANG-354/360): Boolean family, converted to <c>Boolean</c>
    /// (LANG-215); E2061 otherwise.
    /// </summary>
    public BoundExpression BindCondition(ExpressionSyntax syntax)
    {
        var condition = BindExpression(syntax);
        if (condition.HasErrors)
        {
            return condition;
        }

        if (condition.Type is not BooleanTypeSymbol)
        {
            _diagnostics.Report(syntax, "E2061", condition.Type.Name);
            return Error(syntax, condition);
        }

        return Convert(condition, _types.Boolean);
    }

    /// <summary>
    /// <paramref name="value"/> converted for assignment to <paramref name="destination"/> (LANG-280/351); E2010 / E2012
    /// at <paramref name="at"/> and an error expression when it is not assignment-compatible.
    /// </summary>
    public BoundExpression ConvertForAssignment(BoundExpression value, TypeSymbol destination, SyntaxNode at)
    {
        if (value.HasErrors || destination.TypeKind == TypeKind.Error || ReferenceEquals(value.Type, destination))
        {
            return value;
        }

        if (value.ConstantValue is { } constant)
        {
            if (_conversions.ConvertConstant(constant, destination, at, _diagnostics) is not { } converted)
            {
                return Error(at, value);
            }

            // a character constant converts to AnsiChar although Char does not (LANG-262)
            var kind = _conversions.Classify(value.Type, destination).Kind;
            return new BoundConversionExpression(value.Syntax, value, destination,
                kind == ConversionKind.None ? ConversionKind.ImplicitNumeric : kind, isExplicit: false, converted);
        }

        var conversion = _conversions.Classify(value.Type, destination);
        if (!conversion.Exists)
        {
            _conversions.ReportIncompatible(value.Type, destination, at, _diagnostics);
            return Error(at, value);
        }

        return new BoundConversionExpression(value.Syntax, value, destination, conversion.Kind, isExplicit: false, constantValue: null);
    }

    // ------------------------------------------------------------------ factors

    private BoundExpression BindLiteral(LiteralExpressionSyntax literal)
    {

        return _folder.FoldLiteral(literal) is { } value ? new BoundLiteral(literal, value) : Error(literal);
    }

    private BoundExpression BindName(IdentifierNameSyntax name)
    {
        switch (LookupName(name))
        {
            case null:
                return Error(name);
            case VariableSymbol variable:
                NoteReference(variable);
                return new BoundVariableExpression(name, variable);
            case ParameterSymbol parameter:
                NoteReference(parameter);
                return new BoundParameterExpression(name, parameter);
            case RoutineSymbol:
                return CallExpression(name, name, argumentList: null);             // LANG-312: parameterless call
            case ConstantSymbol { IsTyped: false, Value: { } value } constant:
                return new BoundConstantExpression(name, constant, value);
            case ConstantSymbol { IsTyped: true, Storage: { } storage }:
                return new BoundVariableExpression(name, storage);             // LANG-344: a read-only variable
            case ConstantSymbol:
                return Error(name);                                     // its declaration already reported the error
            case EnumMemberSymbol member:
                return new BoundLiteral(name, member.Value);
            case TypeSymbol type:
                _diagnostics.Report(name, "E2009", type.Name);
                return Error(name);
            case IntrinsicSymbol { IsFunction: true } function:
                _diagnostics.Report(name, "E2110", function.Name);
                return Error(name);
            case IntrinsicSymbol:
                _diagnostics.Report(name, "E2034");                     // procedure-like intrinsics have no value
                return Error(name);
            case var symbol:
                return Unsupported(name, symbol.Kind.ToString().ToLowerInvariant() + " reference");
        }
    }

    /// <summary>
    /// LANG-265 <c>@x</c>: the address (type <c>Pointer</c>) of a variable reference — variable, parameter, field, array element,
    /// dereference — or the code address of a routine (E2130 for a nested one, LANG-451; overloaded routines: M2.5).
    /// E2033 for anything else.
    /// </summary>
    private BoundExpression BindAddressOf(AddressOfExpressionSyntax addressOf) => AddressOf(addressOf, addressOf.Operand);

    /// <summary><c>@operand</c> / <c>Addr(operand)</c> bound at <paramref name="syntax"/>.</summary>
    private BoundExpression AddressOf(SyntaxNode syntax, ExpressionSyntax operandSyntax)
    {
        if (Unparenthesized(operandSyntax) is IdentifierNameSyntax { Identifier: { IsMissing: false } token } name
            && _scope.LookupCandidates(token.Value as string ?? token.Text) is [var routine, ..])
        {
            if (routine.IsNested)
            {
                _diagnostics.Report(name, "E2130", routine.Name);
                return Error(syntax);
            }

            return new BoundAddressOfExpression(syntax, operand: null, routine, _types.Pointer);
        }

        var operand = BindExpression(operandSyntax);
        if (operand.HasErrors)
        {
            return Error(syntax, operand);
        }

        if (!IsAddressable(operand))
        {
            _diagnostics.Report(operandSyntax, "E2033");
            return Error(syntax, operand);
        }

        return new BoundAddressOfExpression(syntax, operand, routine: null, _types.Pointer);
    }

    /// <summary>A variable reference whose storage has an address (constants and values have none; typed constants do).</summary>
    internal static bool IsAddressable(BoundExpression expression) =>
        BoundIndexExpression.RootOf(expression) is BoundVariableExpression or BoundParameterExpression or BoundDereferenceExpression;

    /// <summary>
    /// LANG-265 <c>p^</c>: the variable of the pointed type; E2018 when <c>p</c> is no typed pointer. <paramref name="untyped"/>
    /// (an untyped <c>var</c>/<c>const</c> argument such as <c>FillChar(P^, …)</c>) also accepts the untyped <c>Pointer</c>,
    /// giving a byte-typed variable.
    /// </summary>
    internal BoundExpression BindDereference(DereferenceExpressionSyntax syntax, bool untyped)
    {
        var pointer = BindExpression(syntax.Expression);
        if (pointer.HasErrors)
        {
            return Error(syntax, pointer);
        }

        return pointer.Type switch
        {
            PointerTypeSymbol { PointedType: { } target } when pointer.Type != _types.Nil => new BoundDereferenceExpression(syntax, pointer, target),
            PointerTypeSymbol when untyped && pointer.Type != _types.Nil => new BoundDereferenceExpression(syntax, pointer, _types.Byte),
            _ => ReportPointerRequired(syntax, pointer),
        };
    }

    private BoundErrorExpression ReportPointerRequired(SyntaxNode syntax, BoundExpression operand)
    {
        _diagnostics.Report(syntax, "E2018");
        return Error(syntax, operand);
    }

    /// <summary>
    /// An argument for an untyped <c>var</c>/<c>const</c> parameter (<c>FillChar</c>, <c>Move</c>): a variable reference of any
    /// type, also <c>p^</c> of an untyped pointer; E2033 when it has no address.
    /// </summary>
    internal BoundExpression BindUntypedArgument(ExpressionSyntax syntax)
    {
        var operand = Unparenthesized(syntax) is DereferenceExpressionSyntax dereference ? BindDereference(dereference, untyped: true) : BindExpression(syntax);
        if (!operand.HasErrors && !IsAddressable(operand))
        {
            _diagnostics.Report(syntax, "E2033");
            return Error(syntax, operand);
        }

        return operand;
    }

    /// <summary>Pointer arithmetic is allowed for <c>PChar</c>/<c>PAnsiChar</c> and under <c>{$POINTERMATH ON}</c> at <paramref name="at"/> (LANG-247).</summary>
    private static bool AllowsPointerMath(TypeSymbol type, SyntaxToken at) =>
        type is PointerTypeSymbol pointer && (pointer.AllowsPointerMath || at.DirectiveState.PointerMath);

    /// <summary>
    /// LANG-255/258 <c>a[i, j]</c> = <c>a[i][j]</c> on static arrays: each index an ordinal compatible with the index type
    /// (E2010 at the index; it keeps its own type), a constant index inside the bounds (E2032 at the index). E2017 at the
    /// indexed expression when it is no array, E2019 at the first extra index; string indexing arrives with C-061.
    /// </summary>
    private BoundExpression BindElementAccess(ElementAccessExpressionSyntax syntax)
    {
        var result = BindExpression(syntax.Expression);
        if (result.HasErrors)
        {
            return Error(syntax, result);
        }

        var first = true;
        foreach (var indexSyntax in syntax.Indexes)
        {
            if (result.Type is PointerTypeSymbol { PointedType: ArrayTypeSymbol pointedArray } && !AllowsPointerMath(result.Type, syntax.FirstToken()))
            {
                result = new BoundDereferenceExpression(syntax.Expression, result, pointedArray);   // p[i] = p^[i] (extended syntax)
            }
            else if (result.Type is PointerTypeSymbol pointer && AllowsPointerMath(pointer, syntax.FirstToken()) && pointer != _types.Nil)
            {
                // LANG-247: p[i] = (p + i)^, 0-based
                first = false;
                var offset = BindExpression(indexSyntax);
                if (offset.HasErrors)
                {
                    return Error(syntax, result, offset);
                }

                if (offset.Type is not IntegerTypeSymbol || pointer.PointedType is null)
                {
                    if (pointer.PointedType is null)
                    {
                        _diagnostics.Report(syntax.Expression, "E2018");
                    }
                    else
                    {
                        _conversions.ReportIncompatible(offset.Type, _types.NativeInt, indexSyntax, _diagnostics);
                    }

                    return Error(syntax, result, offset);
                }

                var element = new BoundPointerArithmeticExpression(syntax, result, Convert(offset, _types.NativeInt), subtract: false, difference: false, pointer);
                result = new BoundDereferenceExpression(syntax, element, pointer.PointedType);
                continue;
            }

            if (result.Type is not ArrayTypeSymbol array)
            {
                if (!first)
                {
                    _diagnostics.Report(indexSyntax, "E2019", syntax.Expression.ToString().Trim());
                }
                else if (result.Type is StringTypeSymbol)
                {
                    return Unsupported(syntax, "string indexing");                  // C-061
                }
                else
                {
                    _diagnostics.Report(syntax.Expression, "E2017");
                }

                return Error(syntax, result);
            }

            first = false;
            var index = BindExpression(indexSyntax);
            if (index.HasErrors)
            {
                return Error(syntax, result, index);
            }

            if (!index.Type.IsOrdinal || !_conversions.Classify(index.Type, array.IndexType).Exists)
            {
                _conversions.ReportIncompatible(index.Type, array.IndexType, indexSyntax, _diagnostics);
                return Error(syntax, result, index);
            }

            if (index.ConstantValue is { } constant)
            {
                var ordinal = constant.Value is bool b ? (b ? BigInteger.One : BigInteger.Zero) : (BigInteger)constant.Value!;
                if (ordinal < array.Low || ordinal > array.High)
                {
                    _diagnostics.Report(indexSyntax, "E2032", OrdinalText(ordinal, constant.Type),
                        OrdinalText(array.Low, array.IndexType), OrdinalText(array.High, array.IndexType));
                    return Error(syntax, result, index);
                }
            }

            result = new BoundIndexExpression(syntax, result, index, array.ElementType);
        }

        return result;
    }

    /// <summary>
    /// LANG-260 <c>r.F</c>: E2005 (record type, field name) at the name when the record has no such field, E2006 (the
    /// qualifier's source text) at the qualifier when it is no record. Unit-qualified names arrive with units (LANG-311).
    /// </summary>
    private BoundExpression BindMemberAccess(MemberAccessExpressionSyntax syntax)
    {
        if (syntax.Expression is IdentifierNameSyntax { Identifier: { IsMissing: false } token }
            && _scope.Lookup(token.Value as string ?? token.Text) is UnitSymbol)
        {
            return Unsupported(syntax, DeclarationBinder.FeatureName(syntax.Kind));
        }

        var record = BindExpression(syntax.Expression);
        if (record.HasErrors || syntax.Name.Identifier.IsMissing)
        {
            return Error(syntax, record);
        }

        if (record.Type is PointerTypeSymbol { PointedType: RecordTypeSymbol pointed })
        {
            record = new BoundDereferenceExpression(syntax.Expression, record, pointed);       // p.F = p^.F (extended syntax)
        }

        if (record.Type is not RecordTypeSymbol recordType || syntax.Name is not IdentifierNameSyntax)
        {
            _diagnostics.Report(syntax.Expression, "E2006", syntax.Expression.ToString().Trim());
            return Error(syntax, record);
        }

        var name = syntax.Name.Identifier.Value as string ?? syntax.Name.Identifier.Text;
        if (recordType.GetMembers(name).FirstOrDefault() is not FieldSymbol field)
        {
            _diagnostics.Report(syntax.Name, "E2005", recordType.Name, name);
            return Error(syntax, record);
        }

        return new BoundFieldAccessExpression(syntax, record, field);
    }

    /// <summary>An ordinal as written in source: <c>'a'</c>, <c>True</c>, <c>1</c>, an enumeration element by name (<c>Red</c>).</summary>
    private static string OrdinalText(BigInteger value, TypeSymbol type) => type switch
    {
        EnumTypeSymbol e when e.EnumMembers.FirstOrDefault(m => m.Ordinal == value) is { } member => member.Name,
        BooleanTypeSymbol => new ConstantValue(!value.IsZero, type).ToDisplayString(),
        _ => new ConstantValue(value, type).ToDisplayString(),
    };

    /// <summary>The symbol a name binds to (LANG-310); E2003 and null when undeclared, null for a parser-inserted name.</summary>
    private Symbol? LookupName(IdentifierNameSyntax name)
    {
        var token = name.Identifier;
        if (token.IsMissing)
        {
            return null;
        }

        var text = token.Value as string ?? token.Text;
        var symbol = _scope.Lookup(text);
        if (symbol is null && _routine is { ReturnType: null } && IdentifierComparer.Instance.Equals(text, "Result"))
        {
            _diagnostics.Report(name, "E2132");
        }
        else if (symbol is null)
        {
            _diagnostics.ReportUndeclared(name, text);
        }

        return symbol;
    }

    /// <summary>A typecast <c>T(x)</c> or a function call; intrinsics are bound from C-029 on.</summary>
    private BoundExpression BindInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not IdentifierNameSyntax name)
        {
            return Unsupported(invocation, "routine call");
        }

        var symbol = LookupName(name);
        if (symbol is null)
        {
            return Error(invocation);
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (symbol is IntrinsicSymbol { IsFunction: true } function)
        {
            return BindIntrinsicFunction(invocation, function);
        }

        if (symbol is IntrinsicSymbol)
        {
            _diagnostics.Report(invocation, "E2034");
            return Error(invocation);
        }

        if (symbol is RoutineSymbol)
        {
            return CallExpression(invocation, name, invocation.ArgumentList);
        }

        if (symbol is not TypeSymbol target)
        {
            return Unsupported(invocation, "routine call");
        }

        if (arguments.Count != 1)
        {
            return Unsupported(invocation, "typecast");
        }

        if (arguments[0].WidthColonToken is { } colon)
        {
            _diagnostics.Report(colon, "E2915");
            return Error(invocation);
        }

        var operand = BindExpression(arguments[0].Expression);
        return operand.HasErrors || target.TypeKind == TypeKind.Error ? Error(invocation, operand) : BindTypecast(invocation, target, operand);
    }

    /// <summary>
    /// A function-like intrinsic (intrinsics.md §1); folded when its argument is constant (LANG-340), and always for
    /// <c>Low</c>/<c>High</c>/<c>SizeOf</c> except <c>High</c> of a string variable (its length: M2.2).
    /// </summary>
    private BoundExpression BindIntrinsicFunction(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic)
    {
        if (intrinsic.Intrinsic is IntrinsicKind.Assigned or IntrinsicKind.Addr or IntrinsicKind.Ptr)
        {
            return BindPointerFunction(invocation, intrinsic);
        }

        if (!_folder.CheckSingleArgument(invocation, intrinsic))
        {
            return Error(invocation);
        }

        var syntax = invocation.ArgumentList.Arguments[0].Expression;
        if (intrinsic.Intrinsic is IntrinsicKind.Low or IntrinsicKind.High or IntrinsicKind.SizeOf or IntrinsicKind.Length)
        {
            // the operand is never evaluated (LANG-903), so it captures nothing (lead decision C-029)
            var captured = CapturedVariables.Count;
            var (type, argument) = BindTypeOrValue(syntax);
            CapturedVariables.RemoveRange(captured, CapturedVariables.Count - captured);
            if (type.TypeKind == TypeKind.Error)
            {
                return Error(invocation);
            }

            var value = _folder.FoldTypeIntrinsic(invocation, intrinsic, type, argument?.ConstantValue, argument is null, out var needsRuntime);
            if (needsRuntime)
            {
                return Unsupported(invocation, intrinsic.Name + " of a string variable");    // string length, C-060/C-062
            }

            return value is null
                ? Error(invocation)
                : new BoundIntrinsicCallExpression(invocation, intrinsic, argument is null ? [] : [argument], value.Type, value);
        }

        var operand = BindExpression(syntax);
        if (operand.HasErrors || _folder.IntrinsicResultType(intrinsic, operand.Type, syntax) is not { } resultType)
        {
            return Error(invocation, operand);
        }

        if (operand.ConstantValue is { } constant)
        {
            return _folder.FoldIntrinsic(invocation, intrinsic, constant, resultType) is { } folded
                ? new BoundIntrinsicCallExpression(invocation, intrinsic, [operand], folded.Type, folded)
                : Error(invocation, operand);
        }

        if (intrinsic.Intrinsic is IntrinsicKind.Abs or IntrinsicKind.Sqr && resultType is IntegerTypeSymbol)
        {
            operand = Convert(operand, resultType);
        }

        return new BoundIntrinsicCallExpression(invocation, intrinsic, [operand], resultType, constantValue: null);
    }

    /// <summary>
    /// intrinsics.md: <c>Addr(x)</c> = <c>@x</c>; <c>Assigned(p)</c> = <c>p &lt;&gt; nil</c> for a pointer (procedural types: C-161);
    /// <c>Ptr(n)</c> turns an integer into a <c>Pointer</c>. One argument (E2110 / E2111), E2010 for a wrong type.
    /// </summary>
    private BoundExpression BindPointerFunction(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1)
        {
            _diagnostics.Report(arguments.Count == 0 ? invocation : arguments[1], arguments.Count == 0 ? "E2110" : "E2111", intrinsic.Name);
            return Error(invocation);
        }

        var syntax = arguments[0].Expression;
        if (intrinsic.Intrinsic == IntrinsicKind.Addr)
        {
            return AddressOf(invocation, syntax);
        }

        var operand = BindExpression(syntax);
        if (operand.HasErrors)
        {
            return Error(invocation, operand);
        }

        if (intrinsic.Intrinsic == IntrinsicKind.Assigned)
        {
            if (operand.Type is not PointerTypeSymbol)
            {
                _diagnostics.Report(syntax, "E2010", operand.Type.Name, _types.Pointer.Name);
                return Error(invocation, operand);
            }

            return new BoundIntrinsicCallExpression(invocation, intrinsic, [Convert(operand, _types.Pointer)], _types.Boolean, constantValue: null);
        }

        if (operand.Type is not IntegerTypeSymbol)
        {
            _diagnostics.Report(syntax, "E2010", operand.Type.Name, _types.NativeUInt.Name);
            return Error(invocation, operand);
        }

        return new BoundIntrinsicCallExpression(invocation, intrinsic, [Convert(operand, _types.NativeUInt)], _types.Pointer, constantValue: null);
    }

    /// <summary>The argument of <c>Low</c>/<c>High</c>/<c>SizeOf</c>: a type name (bound argument null) or an expression.</summary>
    private (TypeSymbol Type, BoundExpression? Argument) BindTypeOrValue(ExpressionSyntax syntax)
    {
        if (syntax is PredefinedTypeExpressionSyntax)
        {
            return (_types.String, null);
        }

        if (syntax is IdentifierNameSyntax { Identifier: { IsMissing: false } token } && _scope.Lookup(token.Value as string ?? token.Text) is TypeSymbol type)
        {
            return (type, null);
        }

        var argument = BindExpression(syntax);
        return (argument.Type, argument);
    }

    /// <summary>LANG-290: ordinal → ordinal and integer → real; real → anything is E2040; the rest arrives with C-057.</summary>
    private BoundExpression BindTypecast(InvocationExpressionSyntax node, TypeSymbol target, BoundExpression operand)
    {
        var source = operand.Type;
        if (source is RealTypeSymbol)
        {
            _diagnostics.Report(node, "E2040", source.Name, target.Name);
            return Error(node, operand);
        }

        ConstantValue? constant;
        if (source.IsOrdinal && target.IsOrdinal)
        {
            constant = operand.ConstantValue is { } value ? _folder.FoldTypecast(node, target, value) : null;
        }
        else if (source is IntegerTypeSymbol && target is RealTypeSymbol)
        {
            constant = null;                                            // not a constant expression (LANG-340, lead decision C-024)
        }
        else
        {
            return Unsupported(node, "typecast");
        }

        return new BoundConversionExpression(node, operand, target, ConversionKind.Explicit, isExplicit: true, constant);
    }

    // ------------------------------------------------------------------ calls (LANG-420, 421, 441)

    /// <summary>A call in an expression: functions only (E2034 for a procedure).</summary>
    private BoundExpression CallExpression(SyntaxNode call, IdentifierNameSyntax name, ArgumentListSyntax? argumentList)
    {
        var (routine, arguments) = BindCall(call, name, argumentList);
        if (routine is null)
        {
            return Error(call, [.. arguments]);
        }

        if (routine.ReturnType is null)
        {
            _diagnostics.Report(call, "E2034");
            return Error(call, [.. arguments]);
        }

        return new BoundCallExpression(call, routine, arguments);
    }

    /// <summary>
    /// Binds the arguments of a call of the routine(s) <paramref name="name"/> denotes and picks the routine: the only
    /// candidate, or the only applicable one of an overload set (LANG-441; ranking by conversion cost, LANG-442, arrives
    /// with M2.5). Routine is null after an error (reported once); the bound arguments are returned either way.
    /// </summary>
    public (RoutineSymbol? Routine, ImmutableArray<BoundExpression> Arguments) BindCall(SyntaxNode call, IdentifierNameSyntax name, ArgumentListSyntax? argumentList)
    {
        var token = name.Identifier;
        var candidates = _scope.LookupCandidates(token.Value as string ?? token.Text);
        var syntaxes = argumentList?.Arguments ?? default;
        var builder = ImmutableArray.CreateBuilder<BoundExpression>(syntaxes.Count);
        var hasErrors = false;
        foreach (var argument in syntaxes)
        {
            if (argument.WidthColonToken is { } colon)
            {
                _diagnostics.Report(colon, "E2915");
                hasErrors = true;
            }

            var bound = BindExpression(argument.Expression);
            hasErrors |= bound.HasErrors;
            builder.Add(bound);
        }

        var arguments = builder.ToImmutable();
        if (hasErrors || candidates.IsEmpty)
        {
            return (null, arguments);
        }

        var routine = candidates[0];
        if (candidates.Length > 1)
        {
            var applicable = candidates.Where(c => IsApplicable(c, arguments)).ToList();
            if (applicable.Count != 1)
            {
                if (applicable.Count == 0)
                {
                    _diagnostics.Report(call, "E2123", routine.Name);
                }
                else
                {
                    _diagnostics.Report(call, "E2900", "overload resolution between compatible candidates");   // LANG-442, M2.5
                }

                return (null, arguments);
            }

            routine = applicable[0];
        }

        return CheckArguments(call, routine, syntaxes, arguments);
    }

    /// <summary>LANG-420/421: count, then each argument against its parameter.</summary>
    private (RoutineSymbol? Routine, ImmutableArray<BoundExpression> Arguments) CheckArguments(SyntaxNode call, RoutineSymbol routine,
        SeparatedSyntaxList<ArgumentSyntax> syntaxes, ImmutableArray<BoundExpression> arguments)
    {
        var parameters = routine.Parameters;
        if (arguments.Length < parameters.Length)
        {
            _diagnostics.Report(call, "E2110", routine.Name);
            return (null, arguments);
        }

        if (arguments.Length > parameters.Length)
        {
            _diagnostics.Report(syntaxes[parameters.Length], "E2111", routine.Name);
            return (null, arguments);
        }

        var converted = ImmutableArray.CreateBuilder<BoundExpression>(arguments.Length);
        var ok = true;
        for (var i = 0; i < arguments.Length; i++)
        {
            var (parameter, argument, syntax) = (parameters[i], arguments[i], syntaxes[i].Expression);
            if (parameter.IsByReference)
            {
                ok &= CheckReferenceArgument(argument, parameter, syntax);
                converted.Add(argument);
            }
            else
            {
                var value = ConvertForAssignment(argument, parameter.Type, syntax);
                ok &= !value.HasErrors;
                converted.Add(value);
            }
        }

        return (ok ? routine : null, converted.ToImmutable());
    }

    /// <summary>LANG-421 / LANG-281: an assignable variable reference of the identical type.</summary>
    private bool CheckReferenceArgument(BoundExpression argument, ParameterSymbol parameter, ExpressionSyntax syntax)
    {
        if (parameter.Type.TypeKind == TypeKind.Error)
        {
            return true;
        }

        if (!CheckVariableReference(argument, syntax))
        {
            return false;
        }

        if (!Pointers.SameType(argument.Type, parameter.Type))
        {
            _diagnostics.Report(syntax, "E2010", argument.Type.Name, parameter.Type.Name);
            return false;
        }

        return true;
    }

    /// <summary>
    /// LANG-421: <paramref name="argument"/> is an assignable variable reference (a <c>var</c>/<c>out</c> argument, the
    /// operand of <c>Inc</c>/<c>Dec</c>); E2053 / E2112 / E2113 at <paramref name="syntax"/> otherwise.
    /// </summary>
    public bool CheckVariableReference(BoundExpression argument, ExpressionSyntax syntax)
    {
        switch (BoundIndexExpression.RootOf(argument))
        {
            case BoundVariableExpression { Variable: var v } when ForVariables.Contains(v):
            case BoundParameterExpression { Parameter: var p } when ForVariables.Contains(p):
                _diagnostics.Report(syntax, "E2053");                   // LANG-361 rule 6
                return false;
            case BoundVariableExpression { Variable.IsReadOnly: true }:
                _diagnostics.Report(syntax, "E2112");                   // a typed constant (LANG-344)
                return false;
            case BoundVariableExpression:
            case BoundDereferenceExpression:
            case BoundParameterExpression { Parameter.Mode: not ParameterMode.Const }:
                break;
            case BoundParameterExpression:
            case { ConstantValue: not null }:
                _diagnostics.Report(syntax, "E2112");
                return false;
            default:
                _diagnostics.Report(syntax, "E2113");
                return false;
        }

        return true;
    }

    /// <summary>Count fits and every argument is compatible with its parameter (LANG-280/281), without diagnostics.</summary>
    private bool IsApplicable(RoutineSymbol candidate, ImmutableArray<BoundExpression> arguments)
    {
        if (candidate.Parameters.Length != arguments.Length)
        {
            return false;
        }

        for (var i = 0; i < arguments.Length; i++)
        {
            var (parameter, argument) = (candidate.Parameters[i], arguments[i]);
            var compatible = parameter.IsByReference
                ? BoundIndexExpression.RootOf(argument) is BoundVariableExpression or BoundDereferenceExpression or BoundParameterExpression { Parameter.Mode: not ParameterMode.Const }
                  && Pointers.SameType(argument.Type, parameter.Type)
                : ReferenceEquals(argument.Type, parameter.Type)
                  || (argument.ConstantValue is { } constant
                      ? _conversions.ConvertConstant(constant, parameter.Type, argument.Syntax, new DiagnosticBag()) != null
                      : _conversions.Classify(argument.Type, parameter.Type).Exists);
            if (!compatible)
            {
                return false;
            }
        }

        return true;
    }

    // ------------------------------------------------------------------ unary operators (LANG-227)

    private BoundExpression BindUnary(UnaryExpressionSyntax unary)
    {
        var operand = BindExpression(unary.Operand);
        if (operand.HasErrors)
        {
            return Error(unary, operand);
        }

        var op = unary.OperatorToken;
        var kind = op.Kind switch
        {
            SyntaxKind.MinusToken => BoundUnaryOperatorKind.Negate,
            SyntaxKind.PlusToken => BoundUnaryOperatorKind.Identity,
            _ => BoundUnaryOperatorKind.Not,
        };

        if (operand.ConstantValue is { } value)
        {
            return _folder.FoldUnary(op, value) is { } folded
                ? new BoundUnaryExpression(unary, kind, operand, folded.Type, folded)
                : Error(unary, operand);
        }

        switch (kind, operand.Type)
        {
            case (BoundUnaryOperatorKind.Negate or BoundUnaryOperatorKind.Identity, IntegerTypeSymbol integer):
            {
                // -Cardinal is Int64; -UInt64 stays UInt64 with W1010
                var type = kind == BoundUnaryOperatorKind.Negate && integer.Bits == 32 && !integer.IsSigned ? _types.Int64 : ComputationType(integer);
                if (kind == BoundUnaryOperatorKind.Negate && ReferenceEquals(type, _types.UInt64))
                {
                    _diagnostics.Report(op, "W1010");
                }

                return new BoundUnaryExpression(unary, kind, Convert(operand, type), type, constantValue: null);
            }

            case (BoundUnaryOperatorKind.Negate or BoundUnaryOperatorKind.Identity, RealTypeSymbol):
            case (BoundUnaryOperatorKind.Not, BooleanTypeSymbol):
                return new BoundUnaryExpression(unary, kind, operand, operand.Type, constantValue: null);

            case (BoundUnaryOperatorKind.Not, IntegerTypeSymbol integer):
            {
                // complement in the operand's own type for 8/16-bit types, else in its computation type
                var type = integer.Bits < 32 ? integer : ComputationType(integer);
                return new BoundUnaryExpression(unary, kind, Convert(operand, type), type, constantValue: null);
            }

            default:
                _diagnostics.Report(op, "E2011", op.Text, $"'{operand.Type.Name}'");
                return Error(unary, operand);
        }
    }

    // ------------------------------------------------------------------ binary operators (LANG-221…226, 301…305)

    private BoundExpression BindBinary(BinaryExpressionSyntax binary)
    {
        var op = binary.OperatorToken;
        BoundExpression left, right;
        // a set constructor takes the set type of the other operand (LANG-251); the element of 'in' gives no set context
        if (Unparenthesized(binary.Right) is SetConstructorSyntax && Unparenthesized(binary.Left) is not SetConstructorSyntax && op.Kind != SyntaxKind.InKeyword)
        {
            left = BindExpression(binary.Left);
            right = BindExpression(binary.Right, left.Type);
        }
        else if (Unparenthesized(binary.Left) is SetConstructorSyntax && Unparenthesized(binary.Right) is not SetConstructorSyntax)
        {
            right = BindExpression(binary.Right);
            left = BindExpression(binary.Left, right.Type);
        }
        else
        {
            left = BindExpression(binary.Left);
            right = BindExpression(binary.Right);
        }

        if (left.HasErrors || right.HasErrors)
        {
            return Error(binary, left, right);
        }

        if (left.ConstantValue is { } l && right.ConstantValue is { } r)
        {
            return _folder.FoldBinary(op, l, r) is { } folded
                ? new BoundBinaryExpression(binary, OperatorKind(op), left, right, folded.Type, folded.Type, folded)
                : Error(binary, left, right);
        }

        var (lt, rt) = (left.Type, right.Type);
        if (op.Kind == SyntaxKind.InKeyword)
        {
            return BindIn(binary, left, right);
        }

        if (lt is SetTypeSymbol || rt is SetTypeSymbol)
        {
            return SetOperation(binary, left, right);
        }

        switch (op.Kind)
        {
            case SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.AsteriskToken:
                if (op.Kind == SyntaxKind.PlusToken && (lt is StringTypeSymbol || rt is StringTypeSymbol || (lt is CharTypeSymbol && rt is CharTypeSymbol)))
                {
                    return Unsupported(op, binary, "string expression");      // concatenation, LANG-243 (M2.2)
                }

                if (lt is IntegerTypeSymbol && rt is IntegerTypeSymbol)
                {
                    return IntegerOperation(binary, left, right);
                }

                if (op.Kind != SyntaxKind.AsteriskToken && (lt is PointerTypeSymbol || rt is PointerTypeSymbol))
                {
                    return PointerArithmetic(binary, left, right);
                }

                return IsNumeric(lt) && IsNumeric(rt) ? RealOperation(binary, left, right) : Inapplicable(binary, left, right);

            case SyntaxKind.SlashToken:
                return IsNumeric(lt) && IsNumeric(rt) ? RealOperation(binary, left, right) : Inapplicable(binary, left, right);

            case SyntaxKind.DivKeyword or SyntaxKind.ModKeyword:
                return lt is IntegerTypeSymbol && rt is IntegerTypeSymbol ? IntegerOperation(binary, left, right) : Inapplicable(binary, left, right);

            case SyntaxKind.AndKeyword or SyntaxKind.OrKeyword or SyntaxKind.XorKeyword:
                if (lt is BooleanTypeSymbol && rt is BooleanTypeSymbol)
                {
                    return BooleanOperation(binary, left, right);
                }

                return lt is IntegerTypeSymbol && rt is IntegerTypeSymbol ? IntegerOperation(binary, left, right) : Inapplicable(binary, left, right);

            case SyntaxKind.ShlKeyword or SyntaxKind.ShrKeyword:
                if (lt is IntegerTypeSymbol shifted && rt is IntegerTypeSymbol)
                {
                    var type = ComputationType(shifted);
                    return new BoundBinaryExpression(binary, OperatorKind(op), Convert(left, type), Convert(right, type), type, type, constantValue: null);
                }

                return Inapplicable(binary, left, right);

            case SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken or SyntaxKind.LessThanToken
                or SyntaxKind.GreaterThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanEqualsToken:
                return Relational(binary, left, right);

            default:
                return Unsupported(op, binary, $"operator '{op.Text}'");      // in, is, as: sets and classes (M2.2+)
        }
    }

    /// <summary>
    /// LANG-247 (CMP-342): <c>p + n</c>, <c>n + p</c>, <c>p - n</c> (the pointer's type) and <c>p - q</c> of two compatible pointers
    /// (a <c>NativeInt</c> element count), for <c>PChar</c>-like pointers and under <c>{$POINTERMATH ON}</c> (the untyped
    /// <c>Pointer</c> counts bytes there); E2011 otherwise.
    /// </summary>
    private BoundExpression PointerArithmetic(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var op = binary.OperatorToken;
        var subtract = op.Kind == SyntaxKind.MinusToken;
        if (left.Type is PointerTypeSymbol pointer && AllowsPointerMath(pointer, op) && pointer != _types.Nil)
        {
            if (right.Type is IntegerTypeSymbol)
            {
                return new BoundPointerArithmeticExpression(binary, left, Convert(right, _types.NativeInt), subtract, difference: false, pointer);
            }

            if (subtract && right.Type is PointerTypeSymbol other && other != _types.Nil && Pointers.Compatible(other, pointer)
                && Pointers.ElementSize(pointer) == Pointers.ElementSize(other))
            {
                return new BoundPointerArithmeticExpression(binary, left, right, subtract: true, difference: true, _types.NativeInt);
            }
        }
        else if (!subtract && left.Type is IntegerTypeSymbol && right.Type is PointerTypeSymbol rightPointer && AllowsPointerMath(rightPointer, op)
            && rightPointer != _types.Nil)
        {
            return new BoundPointerArithmeticExpression(binary, right, Convert(left, _types.NativeInt), subtract: false, difference: false, rightPointer);
        }

        return Inapplicable(binary, left, right);
    }

    /// <summary>LANG-221/222: both operands in the combined computation type; W1010 for signed mixed with UInt64.</summary>
    private BoundBinaryExpression IntegerOperation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var (lt, rt) = AdaptedComputationTypes(left, right);
        var type = Combine(lt, rt, out var mixesSigns);
        if (mixesSigns)
        {
            _diagnostics.Report(binary.OperatorToken, "W1010");
        }

        return new BoundBinaryExpression(binary, OperatorKind(binary.OperatorToken), Convert(left, type), Convert(right, type), type, type, constantValue: null);
    }

    /// <summary>LANG-226/230: Single when both are Single (or a Single and an integer constant exact in Single), else Double.</summary>
    private BoundBinaryExpression RealOperation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var type = RealOperandType(left, right);
        return new BoundBinaryExpression(binary, OperatorKind(binary.OperatorToken), Convert(left, type), Convert(right, type), type, type, constantValue: null);
    }

    /// <summary>LANG-305: <c>and</c>/<c>or</c> short-circuit under <c>{$B-}</c>; C-style Booleans are normalised (LANG-215).</summary>
    private BoundBinaryExpression BooleanOperation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var op = binary.OperatorToken;
        var kind = OperatorKind(op);
        if (!op.DirectiveState.BoolEval)
        {
            kind = kind switch
            {
                BoundBinaryOperatorKind.And => BoundBinaryOperatorKind.ConditionalAnd,
                BoundBinaryOperatorKind.Or => BoundBinaryOperatorKind.ConditionalOr,
                _ => kind,
            };
        }

        var boolean = _types.Boolean;
        return new BoundBinaryExpression(binary, kind, Convert(left, boolean), Convert(right, boolean), boolean, boolean, constantValue: null);
    }

    /// <summary>LANG-301…302: integers (mathematically), reals, Booleans, chars of one kind.</summary>
    private BoundExpression Relational(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var kind = OperatorKind(binary.OperatorToken);
        var boolean = _types.Boolean;
        var (lt, rt) = (left.Type, right.Type);
        if (lt is StringTypeSymbol || rt is StringTypeSymbol)
        {
            return Unsupported(binary.OperatorToken, binary, "string expression");    // LANG-244 (M2.2)
        }

        if (lt is IntegerTypeSymbol && rt is IntegerTypeSymbol)
        {
            var (l, r) = AdaptedComputationTypes(left, right);
            var type = Combine(l, r, out var mixesSigns);
            if (mixesSigns)
            {
                // LANG-302: signed vs unsigned 64-bit compares mathematically, without a warning
                return new BoundBinaryExpression(binary, kind, Convert(left, Signed64(l)), Convert(right, Signed64(r)), type, boolean, constantValue: null);
            }

            return new BoundBinaryExpression(binary, kind, Convert(left, type), Convert(right, type), type, boolean, constantValue: null);
        }

        if (IsNumeric(lt) && IsNumeric(rt))
        {
            var type = RealOperandType(left, right);
            return new BoundBinaryExpression(binary, kind, Convert(left, type), Convert(right, type), type, boolean, constantValue: null);
        }

        if (lt is BooleanTypeSymbol && rt is BooleanTypeSymbol)
        {
            return new BoundBinaryExpression(binary, kind, Convert(left, boolean), Convert(right, boolean), boolean, boolean, constantValue: null);
        }

        if (lt is PointerTypeSymbol lp && rt is PointerTypeSymbol rp && Pointers.Compatible(lp, rp))
        {
            // LANG-266/267: = and <> for compatible pointers (nil included); ordering where pointer arithmetic is allowed (LANG-247)
            var ordering = kind is not (BoundBinaryOperatorKind.Equal or BoundBinaryOperatorKind.NotEqual);
            if (!ordering || AllowsPointerMath(lp, binary.OperatorToken) && AllowsPointerMath(rp, binary.OperatorToken))
            {
                var pointer = _types.Pointer;
                return new BoundBinaryExpression(binary, kind, Convert(left, pointer), Convert(right, pointer), pointer, boolean, constantValue: null);
            }
        }

        if (lt is EnumTypeSymbol && rt is EnumTypeSymbol)
        {
            // LANG-303: one enumeration (subranges compare in their enumeration)
            if (!Ordinals.SameHost(lt, rt))
            {
                _diagnostics.Report(binary.OperatorToken, "E2010", lt.Name, rt.Name);
                return Error(binary, left, right);
            }

            var host = Ordinals.Host(lt);
            return new BoundBinaryExpression(binary, kind, Convert(left, host), Convert(right, host), host, boolean, constantValue: null);
        }

        if (lt is CharTypeSymbol && rt is CharTypeSymbol)
        {
            // one char kind (LANG-280 rule 4, subranges in their char type); a character constant converts when its code
            // fits the other kind
            var type = Ordinals.SameHost(lt, rt) ? Ordinals.Host(lt)
                : FitsCharType(left, rt) ? Ordinals.Host(rt)
                : FitsCharType(right, lt) ? Ordinals.Host(lt)
                : null;
            if (type != null)
            {
                return new BoundBinaryExpression(binary, kind, Convert(left, type), Convert(right, type), type, boolean, constantValue: null);
            }
        }

        return Inapplicable(binary, left, right);
    }

    private TypeSymbol Signed64(TypeSymbol computationType) =>
        ((IntegerTypeSymbol)computationType).IsSigned ? _types.Int64 : _types.UInt64;

    private static bool FitsCharType(BoundExpression expression, TypeSymbol type) =>
        expression.ConstantValue?.Value is BigInteger code && code <= ((CharTypeSymbol)type).MaxValue;

    private BoundErrorExpression Inapplicable(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var op = binary.OperatorToken;
        _diagnostics.Report(op, "E2011", op.Text, $"'{left.Type.Name}' and '{right.Type.Name}'");
        return Error(binary, left, right);
    }

    private static BoundBinaryOperatorKind OperatorKind(SyntaxToken op) => op.Kind switch
    {
        SyntaxKind.PlusToken => BoundBinaryOperatorKind.Add,
        SyntaxKind.MinusToken => BoundBinaryOperatorKind.Subtract,
        SyntaxKind.AsteriskToken => BoundBinaryOperatorKind.Multiply,
        SyntaxKind.SlashToken => BoundBinaryOperatorKind.Divide,
        SyntaxKind.DivKeyword => BoundBinaryOperatorKind.IntegerDivide,
        SyntaxKind.ModKeyword => BoundBinaryOperatorKind.Modulo,
        SyntaxKind.AndKeyword => BoundBinaryOperatorKind.And,
        SyntaxKind.OrKeyword => BoundBinaryOperatorKind.Or,
        SyntaxKind.XorKeyword => BoundBinaryOperatorKind.Xor,
        SyntaxKind.ShlKeyword => BoundBinaryOperatorKind.Shl,
        SyntaxKind.ShrKeyword => BoundBinaryOperatorKind.Shr,
        SyntaxKind.EqualsToken => BoundBinaryOperatorKind.Equal,
        SyntaxKind.LessThanGreaterThanToken => BoundBinaryOperatorKind.NotEqual,
        SyntaxKind.LessThanToken => BoundBinaryOperatorKind.Less,
        SyntaxKind.GreaterThanToken => BoundBinaryOperatorKind.Greater,
        SyntaxKind.LessThanEqualsToken => BoundBinaryOperatorKind.LessOrEqual,
        SyntaxKind.GreaterThanEqualsToken => BoundBinaryOperatorKind.GreaterOrEqual,
        SyntaxKind.InKeyword => BoundBinaryOperatorKind.In,
        _ => throw new InvalidOperationException($"Not a binary operator: {op.Kind}"),
    };

    // ------------------------------------------------------------------ sets (LANG-250…252)

    /// <summary>
    /// <c>+ - *</c> and <c>= &lt;&gt; &lt;= &gt;=</c> on compatible sets (E2010 otherwise), done in <see cref="Sets.Combine"/>;
    /// <c>&lt; &gt;</c> and a set with a non-set are E2011.
    /// </summary>
    private BoundExpression SetOperation(BinaryExpressionSyntax binary, BoundExpression left, BoundExpression right)
    {
        var op = binary.OperatorToken;
        if (left.Type is not SetTypeSymbol l || right.Type is not SetTypeSymbol r
            || op.Kind is not (SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.AsteriskToken or SyntaxKind.EqualsToken
                or SyntaxKind.LessThanGreaterThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanEqualsToken))
        {
            return Inapplicable(binary, left, right);
        }

        if (!Sets.Compatible(l, r))
        {
            _diagnostics.Report(op, "E2010", l.Name, r.Name);
            return Error(binary, left, right);
        }

        var type = Sets.Combine(l, r);
        var result = op.Kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.AsteriskToken ? type : _types.Boolean;
        return new BoundBinaryExpression(binary, OperatorKind(op), Convert(left, type), Convert(right, type), type, result, constantValue: null);
    }

    /// <summary>LANG-252 <c>x in S</c>: an ordinal of the set's element type (E2010 at <c>in</c>); the element is not converted.</summary>
    private BoundExpression BindIn(BinaryExpressionSyntax binary, BoundExpression element, BoundExpression set)
    {
        if (set.Type is not SetTypeSymbol type || !element.Type.IsOrdinal)
        {
            return Inapplicable(binary, element, set);
        }

        if (!Sets.AcceptsElement(type, element.Type))
        {
            _diagnostics.Report(binary.OperatorToken, "E2010", element.Type.Name, type.ElementType!.Name);
            return Error(binary, element, set);
        }

        return new BoundBinaryExpression(binary, BoundBinaryOperatorKind.In, element, set, type, _types.Boolean, constantValue: null);
    }

    /// <summary>
    /// LANG-251 <c>[a, b..c]</c> of type <paramref name="expected"/>, or without context of its natural type
    /// (<see cref="ConstantFolder.NaturalSetType"/>). Constant elements are checked against the element type (E2013, E2010,
    /// E2012); other elements must be ordinals of a compatible type and stay unconverted (the lowerer guards or checks them).
    /// </summary>
    private BoundExpression BindSetConstructor(SetConstructorSyntax syntax, SetTypeSymbol? expected)
    {
        var elements = ImmutableArray.CreateBuilder<BoundSetElement>(syntax.Elements.Count);
        var children = new List<BoundExpression>();
        foreach (var element in syntax.Elements)
        {
            var low = BindExpression(element.Expression);
            var high = element.UpperBound is { } upper ? BindExpression(upper) : null;
            children.Add(low);
            if (high != null)
            {
                children.Add(high);
            }

            elements.Add(new BoundSetElement(low, high));
        }

        if (children.Any(c => c.HasErrors))
        {
            return Error(syntax, [.. children]);
        }

        if (children.Count == 0)
        {
            var empty = expected ?? _types.EmptySet;
            return new BoundSetConstructor(syntax, [], empty, new ConstantValue(BigInteger.Zero, empty));
        }

        var ordinals = children.Select(c => c.ConstantValue is { } value && c.Type.IsOrdinal ? OrdinalOf(value) : (BigInteger?)null).ToList();
        var constant = ordinals.All(o => o is not null);
        var type = expected ?? _folder.NaturalSetType(children[0].Type, constant ? ordinals.Min() : null, constant ? ordinals.Max() : null);
        var ok = true;
        foreach (var child in children)
        {
            ok &= child.ConstantValue is { } value ? CheckSetElement(type, value, child.Syntax) : CheckSetElement(type, child, child.Syntax);
        }

        if (!ok)
        {
            return Error(syntax, [.. children]);
        }

        var built = elements.MoveToImmutable();
        var mask = constant
            ? new ConstantValue(ConstantFolder.SetMask(type, built.Select(e => (OrdinalOf(e.Low.ConstantValue!), OrdinalOf((e.High ?? e.Low).ConstantValue!)))), type)
            : null;
        return new BoundSetConstructor(syntax, built, type, mask);
    }

    /// <summary>A constant set element: an ordinal in the element type's range (E2013, E2010, E2012 at <paramref name="at"/>).</summary>
    public bool CheckSetElement(SetTypeSymbol set, ConstantValue value, SyntaxNode at) => _folder.CheckSetElement(set, value, at);

    /// <summary>A run-time set element: an ordinal of a type the set accepts (E2013, E2010 at <paramref name="at"/>).</summary>
    public bool CheckSetElement(SetTypeSymbol set, BoundExpression element, SyntaxNode at)
    {
        if (!element.Type.IsOrdinal)
        {
            _diagnostics.Report(at, "E2013");
            return false;
        }

        if (!Sets.AcceptsElement(set, element.Type))
        {
            _diagnostics.Report(at, "E2010", element.Type.Name, set.ElementType!.Name);
            return false;
        }

        return true;
    }

    private static BigInteger OrdinalOf(ConstantValue value) => value.Value is bool b ? (b ? BigInteger.One : BigInteger.Zero) : (BigInteger)value.Value!;

    // ------------------------------------------------------------------ typing rules

    /// <summary>LANG-222: Integer, Cardinal, Int64 or UInt64.</summary>
    private TypeSymbol ComputationType(IntegerTypeSymbol type) => _folder.ComputationTypeSymbol(type);

    /// <summary>LANG-222: an integer constant takes the other operand's computation type when its value fits there.</summary>
    private (TypeSymbol Left, TypeSymbol Right) AdaptedComputationTypes(BoundExpression left, BoundExpression right)
    {
        var lt = ComputationType((IntegerTypeSymbol)left.Type);
        var rt = ComputationType((IntegerTypeSymbol)right.Type);
        if (left.ConstantValue?.Value is BigInteger l && Fits(l, rt))
        {
            lt = rt;
        }
        else if (right.ConstantValue?.Value is BigInteger r && Fits(r, lt))
        {
            rt = lt;
        }

        return (lt, rt);
    }

    /// <summary>LANG-221 table; <paramref name="mixesSigns"/> = a signed operand meets UInt64 (W1010).</summary>
    private TypeSymbol Combine(TypeSymbol left, TypeSymbol right, out bool mixesSigns)
    {
        mixesSigns = false;
        if (ReferenceEquals(left, right))
        {
            return left;
        }

        if (ReferenceEquals(left, _types.UInt64) || ReferenceEquals(right, _types.UInt64))
        {
            var other = (IntegerTypeSymbol)(ReferenceEquals(left, _types.UInt64) ? right : left);
            mixesSigns = other.IsSigned;
            return _types.UInt64;
        }

        return _types.Int64;                                            // Integer x Cardinal, or one side Int64
    }

    private TypeSymbol RealOperandType(BoundExpression left, BoundExpression right) =>
        IsSingleOperand(left, right) && IsSingleOperand(right, left) ? _types.Single : _types.Double;

    /// <summary>Single, or an integer constant exactly representable in Single beside a Single operand (LANG-230).</summary>
    private static bool IsSingleOperand(BoundExpression operand, BoundExpression other) => operand.Type switch
    {
        RealTypeSymbol { RealKind: RealKind.Single } => true,
        IntegerTypeSymbol when other.Type is RealTypeSymbol { RealKind: RealKind.Single } && operand.ConstantValue?.Value is BigInteger value
            => new BigInteger((float)value) == value,
        _ => false,
    };

    private static bool IsNumeric(TypeSymbol type) => type is IntegerTypeSymbol or RealTypeSymbol { RealKind: not RealKind.Currency };

    private static bool Fits(BigInteger value, TypeSymbol type) =>
        type is IntegerTypeSymbol integer && value >= integer.Min && value <= integer.Max;

    // ------------------------------------------------------------------ conversions and errors

    /// <summary>An implicit conversion of <paramref name="expression"/> to <paramref name="type"/> (none when it already has it).</summary>
    private static BoundExpression Convert(BoundExpression expression, TypeSymbol type)
    {
        if (ReferenceEquals(expression.Type, type))
        {
            return expression;
        }

        var constant = expression.ConstantValue is { } value ? ConvertConstant(value, type) : null;
        return new BoundConversionExpression(expression.Syntax, expression, type, ConversionKind.ImplicitNumeric, isExplicit: false, constant);
    }

    private static ConstantValue ConvertConstant(ConstantValue value, TypeSymbol type) => type switch
    {
        RealTypeSymbol { RealKind: RealKind.Single } => new ConstantValue((double)(float)RealValue(value), type),
        RealTypeSymbol => new ConstantValue(RealValue(value), type),
        _ => new ConstantValue(value.Value, type),
    };

    private static double RealValue(ConstantValue value) => value.Value is BigInteger i ? (double)i : (double)value.Value!;

    private BoundErrorExpression Error(SyntaxNode syntax, params BoundExpression[] children) =>
        new(syntax, _types.Error, [.. children]);

    /// <summary>E2900 for a construct of a later milestone (nothing when the syntax is already erroneous).</summary>
    private BoundErrorExpression Unsupported(SyntaxNode syntax, string feature)
    {
        if (!syntax.ContainsDiagnostics)
        {
            _diagnostics.Report(syntax, "E2900", feature);
        }

        return Error(syntax);
    }

    private BoundErrorExpression Unsupported(SyntaxToken op, BinaryExpressionSyntax binary, string feature)
    {
        _diagnostics.Report(op, "E2900", feature);
        return Error(binary);
    }
}
