using System.Globalization;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// Evaluates constant expressions (LANG-340…343). Integers fold in unbounded precision and are then typed as
/// literals are (LANG-126/341); reals fold in <see cref="double"/>. Returns null when the expression is not
/// constant or erroneous: the error is then already reported (syntax errors are the parser's, nothing more is said).
/// </summary>
internal sealed class ConstantFolder
{
    private readonly BuiltInTypes _types;
    private readonly Scope _scope;
    private readonly DiagnosticBag _diagnostics;

    public ConstantFolder(BuiltInTypes types, Scope scope, DiagnosticBag diagnostics)
    {
        _types = types;
        _scope = scope;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Set for a <c>{$IF}</c> condition (LANG-862): <c>Defined(NAME)</c> asks it, <c>Declared(Ident)</c> is evaluated, and an
    /// undeclared name is E1203.
    /// </summary>
    internal Func<string, bool>? IsDefined { get; init; }

    /// <summary>
    /// Binds a generic segment <c>G&lt;…&gt;</c> of a name (LANG-780, C-353: <c>SizeOf(G&lt;X&gt;)</c>, <c>G&lt;X&gt;.C</c>); without it a generic
    /// name is not constant. Each segment is bound once per folder (its diagnostics are reported once).
    /// </summary>
    internal NestedNames.GenericSegment? Generic
    {
        get => _generic is null ? null : Segment;
        init => _generic = value;
    }

    /// <summary>
    /// LANG-977 (C-417): counts the type queries and the type-query dependent constants folded so far; an expression folded to a
    /// constant between two readings that differ is type-query dependent.
    /// </summary>
    internal int TypeQueryUses { get; private set; }

    /// <summary>The value of the true constant <paramref name="constant"/> named in a constant expression (counted for LANG-977).</summary>
    private ConstantValue? ValueOf(ConstantSymbol constant)
    {
        if (constant.IsTypeQueryDependent)
        {
            TypeQueryUses++;
        }

        return constant.Value;
    }

    private readonly NestedNames.GenericSegment? _generic;
    private readonly Dictionary<GenericNameSyntax, TypeSymbol> _segments = new(ReferenceEqualityComparer.Instance);

    private TypeSymbol Segment(GenericNameSyntax name, Scope? scope, TypeSymbol? outer)
    {
        if (!_segments.TryGetValue(name, out var type))
        {
            type = _generic!(name, scope, outer);
            _segments[name] = type;
        }

        return type;
    }

    /// <summary>
    /// LANG-783 (C-353): an instance of a forward generic named in a constant expression before the generic's completion: E2007
    /// at <paramref name="at"/> and true.
    /// </summary>
    internal bool IncompleteInstance(SyntaxNode at, TypeSymbol type)
    {
        if (type is not (ClassTypeSymbol { IsForward: true, IsGenericInstance: true } or InterfaceTypeSymbol { IsForward: true, IsGenericInstance: true }))
        {
            return false;
        }

        _diagnostics.Report(at, "E2007", type.Name);
        return true;
    }

    public ConstantValue? Fold(ExpressionSyntax expression)
    {
        if (expression.IsMissing)
        {
            return null;
        }

        switch (expression)
        {
            case LiteralExpressionSyntax literal:
                return FoldLiteral(literal);
            case IdentifierNameSyntax name:
                return FoldName(name);
            case ParenthesizedExpressionSyntax parenthesized:
                return Fold(parenthesized.Expression);
            case UnaryExpressionSyntax unary:
                return FoldUnary(unary);
            case BinaryExpressionSyntax binary:
                return FoldBinary(binary);
            case InvocationExpressionSyntax invocation:
                return FoldInvocation(invocation);
            case MemberAccessExpressionSyntax memberAccess when _scope.Unqualified(memberAccess) is IdentifierNameSyntax qualified:
                return FoldName(qualified);                                     // U.Name (LANG-311, 538)
            case MemberAccessExpressionSyntax memberAccess when (Generic is not null || memberAccess.Expression is MemberAccessExpressionSyntax)
                && NestedNames.AccessibleTypeNamedBy(_scope, memberAccess.Expression, Generic, _diagnostics, _types) is { } outer
                && (outer is ErrorTypeSymbol || IncompleteInstance(memberAccess.Expression, outer)):
                return null;                                                    // C-353: reported at the generic segment; C-360: E2610 at an inaccessible one
            case MemberAccessExpressionSyntax memberAccess when HelperConstant(memberAccess) is { } helperConstant:
                return NestedNames.ReportInaccessibleNestedValue(_scope, memberAccess.Name, helperConstant, _diagnostics) ? null
                    : helperConstant is EnumMemberSymbol helperElement ? helperElement.Value
                    : ValueOf((ConstantSymbol)helperConstant);                  // LANG-998 (d) (C-444): Integer.MaxValue, string.Empty
            case MemberAccessExpressionSyntax memberAccess when NestedNames.NestedMember(_scope, memberAccess, Generic) is { } nested:
                if (NestedNames.ReportInaccessibleNestedValue(_scope, memberAccess.Name, nested, _diagnostics))
                {
                    return null;                                                // LANG-774 (C-361): the constant's section; C-363: the enumeration's
                }

                return nested switch                                            // LANG-770 (C-122): TOuter.Limit
                {
                    ConstantSymbol { IsTyped: false } constant => ValueOf(constant),
                    EnumMemberSymbol element => element.Value,
                    _ => NotConstant(memberAccess),
                };
            case MemberAccessExpressionSyntax memberAccess:
            {
                var member = LookupEnumMember(memberAccess, out var isEnumAccess);
                return member?.Value ?? (isEnumAccess ? null : NotConstant(memberAccess));
            }

            case SetConstructorSyntax constructor:
                return FoldSetConstructor(constructor);
            default:
                if (!expression.ContainsDiagnostics)
                {
                    _diagnostics.Report(expression, "E2050");
                }

                return null;
        }
    }

    // ------------------------------------------------------------------ factors

    internal ConstantValue? FoldLiteral(LiteralExpressionSyntax literal)
    {
        var token = literal.Token;
        switch (token.Kind)
        {
            case SyntaxKind.IntegerLiteralToken:
                var value = token.Value switch { ulong u => new BigInteger(u), long l => new BigInteger(l), _ => BigInteger.Zero };
                // LANG-126: a hex literal $80000000…$FFFFFFFF is Cardinal
                if (token.Text.StartsWith('$') && value >= 0x8000_0000L && value <= 0xFFFF_FFFFL)
                {
                    return new ConstantValue(value, _types.Cardinal);
                }

                return new ConstantValue(value, IntegerTypeFor(value)!);
            case SyntaxKind.RealLiteralToken:
                return new ConstantValue(token.Value is double d ? d : 0.0, _types.Double);
            case SyntaxKind.StringLiteralToken:
                // LANG-133: one code unit is a character literal
                var text = token.Value as string ?? "";
                return text.Length == 1 ? new ConstantValue(new BigInteger(text[0]), _types.Char) : new ConstantValue(text, _types.String);
            case SyntaxKind.NilKeyword:
                return new ConstantValue(null, _types.Nil);             // LANG-266
            default:
                return null;
        }
    }

    private ConstantValue? FoldName(IdentifierNameSyntax name)
    {
        var symbol = LookupName(name);
        switch (symbol)
        {
            case null:
                return null;
            case ConstantSymbol { IsTyped: false } constant:
                return NestedNames.ReportInaccessibleNestedValue(_scope, name, constant, _diagnostics) ? null : ValueOf(constant);   // LANG-774 (C-361): inherited
            case EnumMemberSymbol member:
                return NestedNames.ReportInaccessibleNestedValue(_scope, name, member, _diagnostics) ? null : member.Value;     // LANG-774 (C-363): inherited
            case TypeSymbol type:
                _diagnostics.Report(name, "E2009", type.Name);
                return null;
            case IntrinsicSymbol { IsFunction: true } intrinsic:
                _diagnostics.Report(name, "E2110", intrinsic.Name);
                return null;
            default:
                _diagnostics.Report(name, "E2050");
                return null;
        }
    }

    /// <summary>The symbol a name binds to; E2003 (and null) when undeclared, null for a parser-inserted name.</summary>
    private Symbol? LookupName(IdentifierNameSyntax name)
    {
        var token = name.Identifier;
        if (token.IsMissing)
        {
            return null;
        }

        var text = token.Value as string ?? token.Text;
        var symbol = _scope.For(name).Lookup(text);
        if (symbol is null && IsDefined != null)
        {
            _diagnostics.Report(name, "E1203", text);
        }
        else if (symbol is null)
        {
            _diagnostics.ReportUndeclared(name, text);
        }
        else
        {
            HintChecks.CheckName(_diagnostics, _types, _scope, name, text, symbol);                // LANG-472 (C-232)
        }

        return symbol;
    }

    /// <summary>
    /// <c>TEnum.Element</c> (LANG-210; the only qualified constant before records and units): E2005 for a missing element.
    /// <paramref name="isEnumAccess"/> is false (and nothing is reported) when the qualifier is not an enumeration type.
    /// </summary>
    internal EnumMemberSymbol? LookupEnumMember(MemberAccessExpressionSyntax access, out bool isEnumAccess)
    {
        // the enumeration type may itself be unit-qualified: U.TEnum.Element
        // ... or nested in a class or record, or a nested alias: TOuter.TEnum.Element (LANG-770, C-311)
        // LANG-774 (C-363): every segment of the path obeys its visibility (E2610 at the first inaccessible one), also in statements
        var named = access.Name is IdentifierNameSyntax ? NestedNames.AccessibleTypeNamedBy(_scope, access.Expression, Generic, _diagnostics, _types) : null;
        if (named is ErrorTypeSymbol)
        {
            isEnumAccess = true;                                                // reported
            return null;
        }

        var type = named as EnumTypeSymbol;
        isEnumAccess = type is not null;
        if (type is null)
        {
            return null;
        }

        var name = access.Name.Identifier;
        if (name.IsMissing)
        {
            return null;
        }

        var text = name.Value as string ?? name.Text;
        if (type.GetMembers(text).FirstOrDefault() is EnumMemberSymbol member)
        {
            return member;
        }

        if (EnumHelperDeclares(type, text))
        {
            isEnumAccess = false;                                               // TColor.Parse: bound as a member access
            return null;
        }

        _diagnostics.Report(access.Name, "E2005", type.Name, text);
        return null;
    }

    /// <summary>
    /// LANG-998 (d) (C-444): <c>T.Name</c> — <c>T</c> a type name or the keyword <c>string</c> — names a true constant (or an element
    /// of an enumeration nested) in the active helper of <c>T</c> or of an ancestor level (LANG-992); an element of an enumeration
    /// <c>T</c> comes first. Null otherwise.
    /// </summary>
    private Symbol? HelperConstant(MemberAccessExpressionSyntax access)
    {
        if (!_types.HasHelpers || access.Name is not IdentifierNameSyntax { Identifier: { IsMissing: false } token })
        {
            return null;
        }

        var type = access.Expression is PredefinedTypeExpressionSyntax ? _types.String : NestedNames.TypeNamedBy(_scope, access.Expression, Generic);
        var name = token.Value as string ?? token.Text;
        if (type is null or HelperTypeSymbol || type is EnumTypeSymbol && type.GetMembers(name).FirstOrDefault() is EnumMemberSymbol
            || ExpressionBinder.HelperFor(_scope, type, name) is not { } helper)
        {
            return null;
        }

        var member = helper.SelfAndAncestors().SelectMany(h => h.GetMembers(name, includeInherited: false)).FirstOrDefault();
        return member is ConstantSymbol { IsTyped: false } or EnumMemberSymbol ? member : null;
    }

    /// <summary>
    /// LANG-990 (b) (C-442): <c>TEnum.Name</c> names a member of the active helper of the enumeration (<c>TColor.Parse</c>) — no
    /// element has the name (an element comes first) and the helper declares it.
    /// </summary>
    internal bool EnumHelperDeclares(EnumTypeSymbol type, string name) =>
        _types.HasHelpers && type.GetMembers(name).FirstOrDefault() is not EnumMemberSymbol
        && _scope.ActiveHelper(type) is { } helper && helper.GetMembers(name).Any();

    /// <summary>A typecast <c>T(v)</c> between ordinal types, or a foldable intrinsic (LANG-340).</summary>
    private ConstantValue? FoldInvocation(InvocationExpressionSyntax invocation)
    {
        if (_scope.Unqualified(invocation.Expression) is not IdentifierNameSyntax name)
        {
            // C-357 (LANG-785): a callee that names a type — TOuter.TEn(1), TK<Integer>.TEn(1), U.TOuter.TEn(1) — is a typecast
            return CalleeType(invocation.Expression) switch
            {
                null => NotConstant(invocation),
                ErrorTypeSymbol => null,                                        // reported at the name
                var type when IncompleteInstance(invocation.Expression, type) => null,
                var type => FoldTypecastCall(invocation, type),
            };
        }

        if (IsDefined != null && (name.Identifier.Value as string ?? name.Identifier.Text) is var function
            && (IdentifierComparer.Instance.Equals(function, "Defined") || IdentifierComparer.Instance.Equals(function, "Declared")))
        {
            return FoldDirectiveFunction(invocation, IdentifierComparer.Instance.Equals(function, "Defined"));
        }

        var symbol = LookupName(name);
        if (symbol is null)
        {
            return null;
        }

        if (symbol is IntrinsicSymbol { IsFunction: true } intrinsic)
        {
            return FoldIntrinsicCall(invocation, intrinsic);
        }

        return symbol is TypeSymbol target ? FoldTypecastCall(invocation, target) : NotConstant(invocation);
    }

    /// <summary>A typecast <c>T(v)</c> to <paramref name="target"/> of a constant <c>v</c> (LANG-290, 340).</summary>
    private ConstantValue? FoldTypecastCall(InvocationExpressionSyntax invocation, TypeSymbol target)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1 || arguments[0].WidthColonToken != null)
        {
            return NotConstant(invocation);
        }

        var operand = Fold(arguments[0].Expression);
        return operand is null ? null : FoldTypecast(invocation, target, operand);
    }

    /// <summary>
    /// C-357: the type a qualified, nested or generic callee names (LANG-770, 780, 785; LANG-774 visibility); null when it names
    /// none. A generic segment that is not a generic type (a generic routine <c>Max&lt;Integer&gt;</c>) is not bound here.
    /// </summary>
    private TypeSymbol? CalleeType(ExpressionSyntax callee)
    {
        while (callee is ParenthesizedExpressionSyntax parenthesized)
        {
            callee = parenthesized.Expression;
        }

        return callee switch
        {
            GenericNameSyntax generic when !NamesGenericType(_scope, generic) => null,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } access
                when UnitQualifiers.Qualifier(_scope, access.Expression) is { } unit && !NamesGenericType(unit.Qualified, generic) => null,
            MemberAccessExpressionSyntax or GenericNameSyntax => NestedNames.AccessibleTypeNamedBy(_scope, callee, Generic, _diagnostics, _types),
            _ => null,
        };

        static bool NamesGenericType(Scope scope, GenericNameSyntax name) =>
            scope.Lookup(GenericTypeSymbol.KeyOf(name.Identifier.Value as string ?? name.Identifier.Text, name.TypeArgumentList.Arguments.Count)) is GenericTypeSymbol;
    }

    /// <summary>LANG-862: <c>Defined(NAME)</c> (a conditional symbol) or <c>Declared(Ident)</c> (visible here; <c>Unit.Ident</c> allowed).</summary>
    private ConstantValue? FoldDirectiveFunction(InvocationExpressionSyntax invocation, bool defined)
    {
        var arguments = invocation.ArgumentList.Arguments;
        var argument = arguments.Count == 1 && arguments[0].WidthColonToken is null ? arguments[0].Expression : null;
        bool? value = (defined, argument) switch
        {
            (true, IdentifierNameSyntax symbol) => IsDefined!(NameOf(symbol)),
            (false, IdentifierNameSyntax identifier) => _scope.Lookup(NameOf(identifier)) != null,
            (false, MemberAccessExpressionSyntax { Name: IdentifierNameSyntax member } access) when UnitQualifiers.Qualifier(_scope, access.Expression) is { } unit =>
                unit.Qualified.Lookup(NameOf(member)) != null,
            (false, MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax or MemberAccessExpressionSyntax, Name: IdentifierNameSyntax }) =>
                false,                                                  // Unit.Ident of a unit not used here
            _ => null,
        };
        if (value is null)
        {
            _diagnostics.Report(invocation.ArgumentList, "E1206", invocation.ArgumentList.ToString());
            return null;
        }

        return new ConstantValue(value.Value, _types.Boolean);

        static string NameOf(IdentifierNameSyntax name) => name.Identifier.Value as string ?? name.Identifier.Text;
    }

    /// <summary>An ordinal typecast of a constant (LANG-290 row 1), a string constant to a character pointer (C-369); E2040 at <paramref name="node"/> otherwise.</summary>
    internal ConstantValue? FoldTypecast(SyntaxNode node, TypeSymbol target, ConstantValue operand)
    {
        if ((Strings.IsCharPointer(target, wide: true) || Strings.IsCharPointer(target, wide: false))
            && (operand.Value is string || operand.Type is CharTypeSymbol && operand.Value is BigInteger))
        {
            // C-369 (LANG-133, LANG-290): a string or character constant cast to PChar / PAnsiChar is its static zero-terminated data
            return new ConstantValue(operand.Value as string ?? ((char)(int)(BigInteger)operand.Value!).ToString(), target);
        }

        if (target is PointerTypeSymbol { Name: not "nil" } && operand.Type is IntegerTypeSymbol && operand.Value is BigInteger address)
        {
            // C-185 (LANG-340): integer → pointer is a constant address (pointer-sized, unsigned); 0 is nil
            var wrapped = Wrap(address, _types.Target.PointerSize * 8, isSigned: false);
            return new ConstantValue(wrapped.IsZero ? null : wrapped, target);
        }

        if (OrdinalOf(operand) is not { } ordinal || !target.IsOrdinal)
        {
            _diagnostics.Report(node, "E2040", operand.Type.Name, target.Name);
            return null;
        }

        if (operand.Type is BooleanTypeSymbol { IsCStyle: true } && target is not BooleanTypeSymbol)
        {
            ordinal = -ordinal;                                         // LANG-215 (C-313): True of a C-style Boolean is all ones
        }

        return target switch
        {
            IntegerTypeSymbol integer => new ConstantValue(Wrap(ordinal, integer.Bits, integer.IsSigned), target),
            CharTypeSymbol or EnumTypeSymbol => new ConstantValue(Wrap(ordinal, (int)target.Size * 8, isSigned: false), target),
            _ => new ConstantValue(!ordinal.IsZero, target),
        };
    }

    private ConstantValue? NotConstant(ExpressionSyntax expression)
    {
        if (!expression.ContainsDiagnostics)
        {
            _diagnostics.Report(expression, "E2050");
        }

        return null;
    }

    // ------------------------------------------------------------------ operators

    private ConstantValue? FoldUnary(UnaryExpressionSyntax unary)
    {
        var operand = Fold(unary.Operand);
        return operand is null ? null : FoldUnary(unary.OperatorToken, operand);
    }

    /// <summary>A unary operator on a constant; diagnostics (E2011, E2051) at <paramref name="op"/>.</summary>
    internal ConstantValue? FoldUnary(SyntaxToken op, ConstantValue operand)
    {
        switch (op.Kind, operand.Value)
        {
            case (SyntaxKind.MinusToken, BigInteger i) when IsInteger(operand):
                return Integer(-i, op);
            case (SyntaxKind.PlusToken, BigInteger i) when IsInteger(operand):
                return Integer(i, op);
            case (SyntaxKind.MinusToken, double d):
                return new ConstantValue(-d, _types.Double);
            case (SyntaxKind.PlusToken, double d):
                return new ConstantValue(d, _types.Double);
            case (SyntaxKind.NotKeyword, bool b):
                return new ConstantValue(!b, operand.Type);
            case (SyntaxKind.NotKeyword, BigInteger i) when operand.Type is IntegerTypeSymbol type:
                // LANG-227: complement in the operand's own type for 8/16-bit types, else in its computation type
                var (bits, signed) = type.Bits < 32 ? (type.Bits, type.IsSigned) : ComputationType(type);
                return Integer(Wrap(~i, bits, signed), op);
            default:
                _diagnostics.Report(op, "E2011", op.Text, $"'{operand.Type.Name}'");
                return null;
        }
    }

    private ConstantValue? FoldBinary(BinaryExpressionSyntax binary)
    {
        var left = Fold(binary.Left);
        var right = Fold(binary.Right);
        return left is null || right is null ? null : FoldBinary(binary.OperatorToken, left, right);
    }

    /// <summary>A binary operator on constants; diagnostics (E2011, E2051, E2052) at <paramref name="op"/>.</summary>
    internal ConstantValue? FoldBinary(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        if (left.Type is PointerTypeSymbol || right.Type is PointerTypeSymbol)
        {
            // nil = nil / nil <> nil (LANG-266); no other operator applies to pointer constants
            if (left.Value is null && right.Value is null && op.Kind is SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken
                && left.Type is PointerTypeSymbol && right.Type is PointerTypeSymbol)
            {
                return new ConstantValue(op.Kind == SyntaxKind.EqualsToken, _types.Boolean);
            }

            _diagnostics.Report(op, "E2011", op.Text, $"'{left.Type.Name}' and '{right.Type.Name}'");
            return null;
        }

        var result = op.Kind == SyntaxKind.InKeyword ? In(op, left, right)
            : left.Type is SetTypeSymbol || right.Type is SetTypeSymbol ? SetOperation(op, left, right)
            : op.Kind switch
        {
            SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.AsteriskToken => Arithmetic(op, left, right),
            SyntaxKind.SlashToken => Divide(op, left, right),
            SyntaxKind.DivKeyword or SyntaxKind.ModKeyword => IntegerDivide(op, left, right),
            SyntaxKind.AndKeyword or SyntaxKind.OrKeyword or SyntaxKind.XorKeyword => Logical(op, left, right),
            SyntaxKind.ShlKeyword or SyntaxKind.ShrKeyword => Shift(op, left, right),
            SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken or SyntaxKind.LessThanToken
                or SyntaxKind.GreaterThanToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.GreaterThanEqualsToken
                => Relational(op, left, right),
            _ => Inapplicable(op, left, right),
        };
        return result.Error ? null : result.Value ?? Inapplicable(op, left, right).Value;
    }

    /// <summary>Value = null and Error = false means "operands not applicable" (the caller reports E2011).</summary>
    private readonly record struct Folded(ConstantValue? Value, bool Error)
    {
        public static readonly Folded NotApplicable = new(null, false);
        public static readonly Folded Failed = new(null, true);

        public static implicit operator Folded(ConstantValue? value) => value is null ? Failed : new(value, false);
    }

    private Folded Arithmetic(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        if (left.Value is BigInteger l && right.Value is BigInteger r && IsInteger(left) && IsInteger(right))
        {
            return Integer(op.Kind switch { SyntaxKind.PlusToken => l + r, SyntaxKind.MinusToken => l - r, _ => l * r }, op);
        }

        if (RealOf(left) is { } a && RealOf(right) is { } b && (left.Value is double || right.Value is double))
        {
            return Real(op.Kind switch { SyntaxKind.PlusToken => a + b, SyntaxKind.MinusToken => a - b, _ => a * b }, op);
        }

        if (op.Kind == SyntaxKind.PlusToken && StringOf(left) is { } s && StringOf(right) is { } t)
        {
            return new ConstantValue(s + t, _types.String);
        }

        return Folded.NotApplicable;
    }

    private Folded Divide(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        if (RealOf(left) is not { } a || RealOf(right) is not { } b)
        {
            return Folded.NotApplicable;
        }

        if (b == 0)
        {
            _diagnostics.Report(op, "E2052");
            return Folded.Failed;
        }

        return Real(a / b, op);
    }

    private Folded IntegerDivide(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        if (left.Value is not BigInteger l || right.Value is not BigInteger r || !IsInteger(left) || !IsInteger(right))
        {
            return Folded.NotApplicable;
        }

        if (r.IsZero)
        {
            _diagnostics.Report(op, "E2052");
            return Folded.Failed;
        }

        // LANG-225: div truncates toward zero, mod has the sign of the dividend (BigInteger semantics)
        return Integer(op.Kind == SyntaxKind.DivKeyword ? BigInteger.Divide(l, r) : BigInteger.Remainder(l, r), op);
    }

    /// <summary>
    /// LANG-205: the type of a Boolean <c>and</c>/<c>or</c>/<c>xor</c> — the operands' type when both are the same distinct
    /// Boolean type with the representation of <c>Boolean</c>; else <paramref name="boolean"/>.
    /// </summary>
    internal static TypeSymbol LogicalResultType(TypeSymbol left, TypeSymbol right, TypeSymbol boolean) =>
        ReferenceEquals(left, right) && left is BooleanTypeSymbol { IsCStyle: false } ? left : boolean;

    private Folded Logical(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        if (left.Value is bool a && right.Value is bool b)
        {
            return new ConstantValue(op.Kind switch { SyntaxKind.AndKeyword => a && b, SyntaxKind.OrKeyword => a || b, _ => a ^ b },
                LogicalResultType(left.Type, right.Type, _types.Boolean));
        }

        if (left.Value is BigInteger l && right.Value is BigInteger r && IsInteger(left) && IsInteger(right))
        {
            return Integer(op.Kind switch { SyntaxKind.AndKeyword => l & r, SyntaxKind.OrKeyword => l | r, _ => l ^ r }, op);
        }

        return Folded.NotApplicable;
    }

    /// <summary>LANG-224: in the left operand's computation type, count modulo its width, <c>shr</c> zero-filling.</summary>
    private Folded Shift(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        if (left.Type is not IntegerTypeSymbol type || right.Value is not BigInteger r || !IsInteger(right))
        {
            return Folded.NotApplicable;
        }

        var (bits, signed) = ComputationType(type);
        var count = (int)(((r % bits) + bits) % bits);
        var pattern = Wrap((BigInteger)left.Value!, bits, isSigned: false);
        var shifted = op.Kind == SyntaxKind.ShlKeyword ? pattern << count : pattern >> count;
        return Integer(Wrap(shifted, bits, signed), op);
    }

    private Folded Relational(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        int comparison;
        if (left.Type is EnumTypeSymbol && right.Type is EnumTypeSymbol && !Ordinals.SameHost(left.Type, right.Type))
        {
            _diagnostics.Report(op, "E2010", left.Type.Name, right.Type.Name);       // LANG-303
            return Folded.Failed;
        }

        if (left.Value is BigInteger l && right.Value is BigInteger r && IsInteger(left) == IsInteger(right)
            && (IsInteger(left) || Ordinals.SameHost(left.Type, right.Type)))
        {
            comparison = l.CompareTo(r);             // integers (LANG-302: mathematical values), chars, enumerations
        }
        else if (RealOf(left) is { } a && RealOf(right) is { } b)
        {
            comparison = a.CompareTo(b);
        }
        else if (left.Value is bool x && right.Value is bool y)
        {
            comparison = x.CompareTo(y);
        }
        else if (StringOf(left) is { } s && StringOf(right) is { } t)
        {
            comparison = string.CompareOrdinal(s, t);
        }
        else
        {
            return Folded.NotApplicable;
        }

        var result = op.Kind switch
        {
            SyntaxKind.EqualsToken => comparison == 0,
            SyntaxKind.LessThanGreaterThanToken => comparison != 0,
            SyntaxKind.LessThanToken => comparison < 0,
            SyntaxKind.GreaterThanToken => comparison > 0,
            SyntaxKind.LessThanEqualsToken => comparison <= 0,
            _ => comparison >= 0,
        };
        return new ConstantValue(result, _types.Boolean);
    }

    /// <summary>LANG-252 on two constant sets (bit masks): <c>+ - *</c>, <c>= &lt;&gt; &lt;= &gt;=</c>; E2010 for incompatible sets.</summary>
    private Folded SetOperation(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        if (left.Type is not SetTypeSymbol l || right.Type is not SetTypeSymbol r)
        {
            return Folded.NotApplicable;
        }

        if (!Sets.Compatible(l, r))
        {
            _diagnostics.Report(op, "E2010", l.Name, r.Name);
            return Folded.Failed;
        }

        var type = Sets.Combine(l, r);
        var all = (BigInteger.One << type.Capacity) - 1;
        var (a, b) = ((BigInteger)left.Value! & all, (BigInteger)right.Value! & all);
        return op.Kind switch
        {
            SyntaxKind.PlusToken => new ConstantValue(a | b, type),
            SyntaxKind.AsteriskToken => new ConstantValue(a & b, type),
            SyntaxKind.MinusToken => new ConstantValue(a & ~b & all, type),
            SyntaxKind.EqualsToken => new ConstantValue(a == b, _types.Boolean),
            SyntaxKind.LessThanGreaterThanToken => new ConstantValue(a != b, _types.Boolean),
            SyntaxKind.LessThanEqualsToken => new ConstantValue((a & ~b).IsZero, _types.Boolean),
            SyntaxKind.GreaterThanEqualsToken => new ConstantValue((b & ~a).IsZero, _types.Boolean),
            _ => Folded.NotApplicable,
        };
    }

    /// <summary>LANG-252 <c>x in S</c>: False when <c>Ord(x)</c> is outside the set's capacity; E2010 for an element of another type.</summary>
    private Folded In(SyntaxToken op, ConstantValue element, ConstantValue set)
    {
        if (set.Type is not SetTypeSymbol type || OrdinalOf(element) is not { } ordinal || !element.Type.IsOrdinal)
        {
            return Folded.NotApplicable;
        }

        if (!Sets.AcceptsElement(type, element.Type) && !IsAnsiCharConstant(element, type))
        {
            _diagnostics.Report(op, "E2010", element.Type.Name, type.ElementType!.Name);
            return Folded.Failed;
        }

        var member = ordinal >= 0 && ordinal < type.Capacity && !((BigInteger)set.Value! >> (int)ordinal).IsEven;
        return new ConstantValue(member, _types.Boolean);
    }

    // ------------------------------------------------------------------ set constructors (LANG-251)

    /// <summary>
    /// A constructor of constants without context: its natural type (<see cref="NaturalSetType"/>) and bit mask. E2013 for a
    /// non-ordinal element, E2010 for elements of different types, E2012 for an ordinal outside the natural element type.
    /// </summary>
    private ConstantValue? FoldSetConstructor(SetConstructorSyntax constructor)
    {
        var ranges = new List<(ConstantValue Low, ConstantValue High, SyntaxNode At)>();
        foreach (var element in constructor.Elements)
        {
            var low = Fold(element.Expression);
            var high = element.UpperBound is { } upper ? Fold(upper) : low;
            if (low is null || high is null)
            {
                return null;
            }

            ranges.Add((low, high, element));
        }

        if (ranges.Count == 0)
        {
            return new ConstantValue(BigInteger.Zero, _types.EmptySet);
        }

        var ordinals = ranges.SelectMany(r => new[] { r.Low, r.High }).Select(OrdinalOf).ToList();
        var first = ranges[0].Low.Type;
        var type = NaturalSetType(first, ordinals.All(o => o is not null) ? ordinals.Min() : null, ordinals.All(o => o is not null) ? ordinals.Max() : null);
        foreach (var (low, high, at) in ranges)
        {
            if (!CheckSetElement(type, low, at) || !CheckSetElement(type, high, at))
            {
                return null;
            }
        }

        return new ConstantValue(SetMask(type, ranges.Select(r => (OrdinalOf(r.Low)!.Value, OrdinalOf(r.High)!.Value))), type);
    }

    /// <summary>
    /// LANG-262 (C-200): a <c>Char</c> constant ≤ #255 is an <c>AnsiChar</c> constant where a <c>set of AnsiChar</c> (or of an
    /// <c>AnsiChar</c> subrange) expects an element: a constructor element, the left operand of <c>in</c>.
    /// </summary>
    internal static bool IsAnsiCharConstant(ConstantValue value, SetTypeSymbol set) =>
        set.ElementType is CharTypeSymbol { IsWide: false } && value.Type is CharTypeSymbol { IsWide: true } && value.Value is BigInteger code && code >= 0 && code <= 255;

    /// <summary>E2013 / E2010 / E2012 at <paramref name="at"/> unless <paramref name="value"/> is an ordinal of the set's element type range.</summary>
    internal bool CheckSetElement(SetTypeSymbol set, ConstantValue value, SyntaxNode at)
    {
        if (!value.Type.IsOrdinal || OrdinalOf(value) is not { } ordinal)
        {
            _diagnostics.Report(at, "E2013");
            return false;
        }

        if (!Sets.AcceptsElement(set, value.Type) && !IsAnsiCharConstant(value, set))
        {
            _diagnostics.Report(at, "E2010", value.Type.Name, set.ElementType!.Name);
            return false;
        }

        if (set.ElementType is { } element && Ordinals.Bounds(element) is { } bounds && (ordinal < bounds.Low || ordinal > bounds.High))
        {
            _diagnostics.Report(at, "E2012", value.ToDisplayString(), element.Name);
            return false;
        }

        return true;
    }

    /// <summary>
    /// LANG-251 type of a constructor without context, from its first element's type: the enumeration (of a subrange), Boolean,
    /// and for integers and chars with constant elements in 0..255 the subrange <c>0..max</c> (a small set when max &lt; 64),
    /// otherwise <c>Byte</c> / a char subrange <c>#0..#255</c> (a big set; the spec's <c>set of 0..255</c>).
    /// </summary>
    internal SetTypeSymbol NaturalSetType(TypeSymbol element, BigInteger? min, BigInteger? max)
    {
        var host = Ordinals.Host(element);
        var top = min is { } lo && max is { } hi && lo >= 0 && hi <= 255 ? hi : 255;
        TypeSymbol elementType = host switch
        {
            IntegerTypeSymbol => top == 255 && max is null ? _types.Byte
                : new IntegerTypeSymbol($"0..{top}", (IntegerTypeSymbol)_types.Byte, BigInteger.Zero, top, containingSymbol: null, locations: []),
            CharTypeSymbol c => new CharTypeSymbol($"#0..#{top}", c, 0, (int)top, containingSymbol: null, locations: []),
            _ => host,
        };
        return new SetTypeSymbol("set of " + elementType.Name, elementType, containingSymbol: null, locations: []);
    }

    /// <summary>The bits of the ranges' ordinals inside the set's capacity (empty ranges contribute nothing).</summary>
    internal static BigInteger SetMask(SetTypeSymbol set, IEnumerable<(BigInteger Low, BigInteger High)> ranges)
    {
        var mask = BigInteger.Zero;
        foreach (var (low, high) in ranges)
        {
            for (var v = BigInteger.Max(low, 0); v <= BigInteger.Min(high, set.Capacity - 1); v++)
            {
                mask |= BigInteger.One << (int)v;
            }
        }

        return mask;
    }

    private Folded Inapplicable(SyntaxToken op, ConstantValue left, ConstantValue right)
    {
        _diagnostics.Report(op, "E2011", op.Text, $"'{left.Type.Name}' and '{right.Type.Name}'");
        return Folded.Failed;
    }

    // ------------------------------------------------------------------ intrinsics (intrinsics.md §1, LANG-340)

    /// <summary>
    /// The argument count of a function-like intrinsic call: exactly one, without width specifier (E2110 at the call,
    /// E2111 at the first extra argument, E2915 at the colon).
    /// </summary>
    internal bool CheckSingleArgument(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0)
        {
            _diagnostics.Report(invocation, "E2110", intrinsic.Name);
            return false;
        }

        if (arguments.Count > 1)
        {
            _diagnostics.Report(arguments[1], "E2111", intrinsic.Name);
            return false;
        }

        if (arguments[0].WidthColonToken is { } colon)
        {
            _diagnostics.Report(colon, "E2915");
            return false;
        }

        return true;
    }

    private ConstantValue? FoldIntrinsicCall(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic)
    {
        if (intrinsic.IsNeverConstant)
        {
            return NotConstant(invocation);                          // not in the LANG-340 list
        }

        if (!CheckSingleArgument(invocation, intrinsic))
        {
            return null;
        }

        var argument = invocation.ArgumentList.Arguments[0].Expression;
        if (intrinsic.IsTypeQuery)
        {
            return FoldTypeQueryCall(invocation, intrinsic, argument);
        }

        if (intrinsic.Intrinsic is IntrinsicKind.Low or IntrinsicKind.High or IntrinsicKind.SizeOf or IntrinsicKind.Length)
        {
            // a type name, a variable (only its type matters, LANG-903) or a constant
            ConstantValue? value = null;
            TypeSymbol type;
            var isTypeName = false;
            if (argument is PredefinedTypeExpressionSyntax)
            {
                (type, isTypeName) = (_types.String, true);
            }
            else if (argument is MemberAccessExpressionSyntax or GenericNameSyntax && NestedNames.AccessibleTypeNamedBy(_scope, argument, Generic, _diagnostics, _types) is { } nestedType)
            {
                // LANG-770: SizeOf(TOuter.TInner); LANG-784 (C-353): SizeOf(G<X>), High(U.G<X>.TEn), SizeOf(TOuter.TInner<X>);
                // LANG-774 (C-356): E2610 for a nested type this code may not access
                if (nestedType is ErrorTypeSymbol || IncompleteInstance(argument, nestedType))
                {
                    return null;
                }

                (type, isTypeName) = (nestedType, true);
            }
            else if (_scope.Unqualified(argument) is IdentifierNameSyntax name && LookupName(name) is (TypeSymbol or VariableSymbol or ParameterSymbol) and var symbol)
            {
                (type, isTypeName) = symbol switch
                {
                    TypeSymbol t => (t, true),
                    VariableSymbol v => (v.Type, false),
                    _ => (((ParameterSymbol)symbol).Type, false),
                };
            }
            else
            {
                value = Fold(argument);
                if (value is null)
                {
                    return null;
                }

                type = value.Type;
            }

            var result = FoldTypeIntrinsic(invocation, intrinsic, type, value, isTypeName, out var needsRuntime);
            return needsRuntime ? NotConstant(invocation) : result;
        }

        var operand = Fold(argument);
        if (operand is null || IntrinsicResultType(intrinsic, operand.Type, argument) is not { } resultType)
        {
            return null;
        }

        // LANG-340 lists Trunc and Round, not Int, Frac, Sqrt
        return intrinsic.Intrinsic is IntrinsicKind.Int or IntrinsicKind.Frac or IntrinsicKind.Sqrt
            ? NotConstant(invocation)
            : FoldIntrinsic(invocation, intrinsic, operand, resultType);
    }

    /// <summary>
    /// LANG-972…976 (C-416): a type query in a constant expression. The operand of <c>IsManagedType</c>, <c>HasWeakRef</c> and
    /// <c>GetTypeKind</c> is a type (E2002 otherwise). The operand of <c>IsConstValue</c> is folded for its diagnostics only: "not a
    /// constant expression" (E2050) is the answer <c>False</c> and is not reported; a type name is E2009.
    /// </summary>
    private ConstantValue? FoldTypeQueryCall(InvocationExpressionSyntax invocation, IntrinsicSymbol intrinsic, ExpressionSyntax argument)
    {
        TypeQueryUses++;                                                        // LANG-977 (C-417)
        var type = TypeOperand(argument, out var failed);
        if (failed)
        {
            return null;
        }

        if (intrinsic.Intrinsic == IntrinsicKind.IsConstValue)
        {
            if (type is not null)
            {
                _diagnostics.Report(argument, "E2009", type.Name);
                return null;
            }

            var reported = _diagnostics.Count;
            var value = Fold(argument);
            return _diagnostics.RemoveSince(reported, "E2050") ? null : new ConstantValue(value is not null, _types.Boolean);
        }

        if (type is null)
        {
            // an undeclared name is E2003 (LookupName); anything else that names no type is E2002, as for Default (LANG-713)
            if (_scope.Unqualified(argument) is not IdentifierNameSyntax name || LookupName(name) is not null)
            {
                _diagnostics.Report(argument, "E2002", argument.ToString().Trim());
            }

            return null;
        }

        if (TypeQueryResultType(invocation, intrinsic) is not { } resultType)
        {
            return null;
        }

        return FoldTypeQuery(intrinsic, type, resultType) ?? NotConstant(invocation);
    }

    /// <summary>
    /// The type a type operand names — <c>string</c>, a type identifier, or a qualified, nested or instance name (LANG-784) — or null
    /// when it names none (nothing is reported then); <paramref name="failed"/> after a diagnostic at a nested or generic name.
    /// </summary>
    private TypeSymbol? TypeOperand(ExpressionSyntax argument, out bool failed)
    {
        failed = false;
        if (argument is PredefinedTypeExpressionSyntax)
        {
            return _types.String;
        }

        if (argument is MemberAccessExpressionSyntax or GenericNameSyntax && NestedNames.AccessibleTypeNamedBy(_scope, argument, Generic, _diagnostics, _types) is { } named)
        {
            failed = named is ErrorTypeSymbol || IncompleteInstance(argument, named);
            return failed ? null : named;
        }

        if (_scope.Unqualified(argument) is IdentifierNameSyntax { Identifier: { IsMissing: false } token } name
            && _scope.For(name).Lookup(token.Value as string ?? token.Text) is TypeSymbol type)
        {
            HintChecks.CheckName(_diagnostics, _types, _scope, name, token.Value as string ?? token.Text, type);    // LANG-472, as LookupName
            return type;
        }

        return null;
    }

    /// <summary>
    /// The result type of a type query: <c>Boolean</c>, and for <c>GetTypeKind</c> the enumeration <c>TTypeKind</c> that unit
    /// <c>System</c> declares (LANG-974) — null after E2003 at <paramref name="at"/> when the <c>System</c> in use declares none.
    /// </summary>
    internal TypeSymbol? TypeQueryResultType(SyntaxNode at, IntrinsicSymbol intrinsic)
    {
        if (intrinsic.Intrinsic != IntrinsicKind.GetTypeKind)
        {
            return _types.Boolean;
        }

        for (var scope = _scope; scope is not null; scope = scope.Parent)
        {
            if (scope.LookupLocal("TTypeKind") is EnumTypeSymbol { BaseType: null, ContainingSymbol: UnitSymbol unit } kinds
                && string.Equals(unit.Name, "System", StringComparison.OrdinalIgnoreCase))
            {
                return kinds;
            }
        }

        _diagnostics.ReportUndeclared(at, "TTypeKind");
        return null;
    }

    /// <summary>
    /// CMP-1474 (C-416): the constant <c>IsManagedType</c>, <c>HasWeakRef</c> or <c>GetTypeKind</c> of <paramref name="type"/> is —
    /// ABI-010 managed-ness, <c>False</c> (no type holds a weak reference yet: LANG-763, card F10-021), and the <c>Kind</c> byte of
    /// the type's RTTI block (<see cref="RttiKind"/>). Null when <paramref name="type"/> depends on a type parameter of an open
    /// instance: the query is not a constant there (LANG-976).
    /// </summary>
    internal ConstantValue? FoldTypeQuery(IntrinsicSymbol intrinsic, TypeSymbol type, TypeSymbol resultType) =>
        TypeParameterTypeSymbol.IsIn(type) ? null : intrinsic.Intrinsic switch
        {
            IntrinsicKind.IsManagedType => new ConstantValue(type.IsManaged, resultType),
            IntrinsicKind.HasWeakRef => new ConstantValue(false, resultType),
            _ => new ConstantValue(new BigInteger(RttiKind.Of(type)), resultType),
        };

    /// <summary>
    /// The result type of a value intrinsic (<c>Succ</c> … <c>Swap</c>, not <c>Low</c>/<c>High</c>/<c>SizeOf</c>) applied to
    /// an operand of type <paramref name="operand"/>; null after E2013 / E2010 at <paramref name="at"/> (the argument).
    /// </summary>
    internal TypeSymbol? IntrinsicResultType(IntrinsicSymbol intrinsic, TypeSymbol operand, SyntaxNode at)
    {
        if (operand.TypeKind == TypeKind.Error)
        {
            return null;
        }

        switch (intrinsic.Intrinsic)
        {
            case IntrinsicKind.Succ or IntrinsicKind.Pred or IntrinsicKind.Ord:
                if (!operand.IsOrdinal)
                {
                    _diagnostics.Report(at, "E2013");
                    return null;
                }

                return intrinsic.Intrinsic == IntrinsicKind.Ord ? OrdType(operand) : operand;

            case IntrinsicKind.Abs or IntrinsicKind.Sqr when operand is RealTypeSymbol { RealKind: not RealKind.Currency }:
                return operand;

            case IntrinsicKind.Abs when operand is RealTypeSymbol { RealKind: RealKind.Currency }:
                return operand;                                                 // C-218: exact, on the scaled Int64

            // intrinsics.md: (X: Double) — integer and real operands convert to Double (LANG-231)
            case IntrinsicKind.Trunc or IntrinsicKind.Round or IntrinsicKind.Int or IntrinsicKind.Frac or IntrinsicKind.Sqrt:
                if (operand is not (IntegerTypeSymbol or RealTypeSymbol))
                {
                    _diagnostics.Report(at, "E2010", operand.Name, _types.Double.Name);
                    return null;
                }

                return intrinsic.Intrinsic is IntrinsicKind.Trunc or IntrinsicKind.Round ? _types.Int64 : _types.Double;
        }

        if (operand is not IntegerTypeSymbol integer)
        {
            _diagnostics.Report(at, "E2010", operand.Name, _types.Integer.Name);
            return null;
        }

        return intrinsic.Intrinsic switch
        {
            IntrinsicKind.Chr => _types.Char,
            IntrinsicKind.Odd => _types.Boolean,
            IntrinsicKind.Lo or IntrinsicKind.Hi => _types.Byte,
            IntrinsicKind.Swap => _types.Word,
            _ => ComputationTypeSymbol(integer),                     // Abs, Sqr (LANG-222)
        };
    }

    /// <summary>
    /// Folds a value intrinsic on a constant operand already checked by <see cref="IntrinsicResultType"/>. E2012 when
    /// <c>Succ</c>/<c>Pred</c> leave the operand type (at <paramref name="call"/>) or the operand of <c>Chr</c> exceeds
    /// <c>Char</c> (at the argument); E2051 for overflow (at <paramref name="call"/>).
    /// </summary>
    internal ConstantValue? FoldIntrinsic(InvocationExpressionSyntax call, IntrinsicSymbol intrinsic, ConstantValue operand, TypeSymbol resultType)
    {
        var value = operand.Value;
        switch (intrinsic.Intrinsic)
        {
            case IntrinsicKind.Succ or IntrinsicKind.Pred:
            {
                var ordinal = OrdinalOf(operand)!.Value + (intrinsic.Intrinsic == IntrinsicKind.Succ ? 1 : -1);
                var (min, max) = OrdinalBounds(operand.Type);
                if (ordinal < min || ordinal > max)
                {
                    _diagnostics.Report(call, "E2012", ordinal.ToString(CultureInfo.InvariantCulture), operand.Type.Name);
                    return null;
                }

                return operand.Type is BooleanTypeSymbol ? new ConstantValue(!ordinal.IsZero, operand.Type) : new ConstantValue(ordinal, operand.Type);
            }

            case IntrinsicKind.Ord:
                return new ConstantValue(OrdinalOf(operand)!.Value, resultType);

            case IntrinsicKind.Abs or IntrinsicKind.Sqr when value is double d:
                return Real(intrinsic.Intrinsic == IntrinsicKind.Abs ? Math.Abs(d) : d * d, call);

            case IntrinsicKind.Abs:
                return Integer(BigInteger.Abs((BigInteger)value!), call);

            case IntrinsicKind.Trunc or IntrinsicKind.Round:
            {
                // toward zero / to nearest with ties to even (intrinsics.md); E2051 outside Int64
                var result = value switch
                {
                    BigInteger exact => exact,
                    double d when intrinsic.Intrinsic == IntrinsicKind.Trunc && double.IsFinite(d) => new BigInteger(Math.Truncate(d)),
                    double d when double.IsFinite(d) => new BigInteger(Math.Round(d, MidpointRounding.ToEven)),
                    _ => BigInteger.MinusOne << 64,                                              // NaN/INF: out of range
                };
                if (result < long.MinValue || result > long.MaxValue)
                {
                    _diagnostics.Report(call, "E2051");
                    return null;
                }

                return new ConstantValue(result, _types.Int64);
            }

            case IntrinsicKind.Sqr:
                return Integer((BigInteger)value! * (BigInteger)value, call);
        }

        var n = (BigInteger)value!;
        var bits = Wrap(n, 64, isSigned: false);
        switch (intrinsic.Intrinsic)
        {
            case IntrinsicKind.Chr:
                if (n < 0 || n > 0xFFFF)
                {
                    _diagnostics.Report(call.ArgumentList.Arguments[0].Expression, "E2012", n.ToString(CultureInfo.InvariantCulture), _types.Char.Name);
                    return null;
                }

                return new ConstantValue(n, _types.Char);
            case IntrinsicKind.Odd:
                return new ConstantValue(!n.IsEven, _types.Boolean);
            case IntrinsicKind.Lo:
                return new ConstantValue(bits & 0xFF, _types.Byte);
            case IntrinsicKind.Hi:
                return new ConstantValue((bits >> 8) & 0xFF, _types.Byte);
            default:                                                    // Swap: exchange the low two bytes
                return new ConstantValue(((bits & 0xFF) << 8) | ((bits >> 8) & 0xFF), _types.Word);
        }
    }

    /// <summary>
    /// <c>Low</c>/<c>High</c>/<c>SizeOf</c> of <paramref name="type"/> (a type name when <paramref name="isTypeName"/>,
    /// else the type of a value; <paramref name="value"/> when that is constant). Null after E2911 at
    /// <paramref name="call"/>, or with <paramref name="needsRuntime"/> for <c>High</c> of a non-constant string (its length).
    /// </summary>
    internal ConstantValue? FoldTypeIntrinsic(InvocationExpressionSyntax call, IntrinsicSymbol intrinsic, TypeSymbol type, ConstantValue? value,
        bool isTypeName, out bool needsRuntime)
    {
        needsRuntime = false;
        if (type.TypeKind == TypeKind.Error)
        {
            return null;
        }

        var high = intrinsic.Intrinsic == IntrinsicKind.High;
        switch (intrinsic.Intrinsic, type)
        {
            case (IntrinsicKind.SizeOf, OpenArrayTypeSymbol):
                _diagnostics.Report(call, "E2116");                             // LANG-431 (C-106)
                return null;
            case (IntrinsicKind.Length or IntrinsicKind.High, OpenArrayTypeSymbol):
                needsRuntime = true;                                            // the High companion (C-106)
                return null;
            case (IntrinsicKind.Low, OpenArrayTypeSymbol):
                return new ConstantValue(BigInteger.Zero, _types.Integer);
            case (IntrinsicKind.SizeOf, _) when type.Size >= 0:
                return new ConstantValue(new BigInteger(type.Size), _types.Integer);
            case (IntrinsicKind.SizeOf, _):
                break;

            // static arrays: Length = element count, Low/High = the index type's bounds (intrinsics.md §1)
            case (IntrinsicKind.Length, ArrayTypeSymbol array):
                return new ConstantValue(new BigInteger(array.Length), _types.Integer);
            case (IntrinsicKind.Length, StringTypeSymbol) when !isTypeName && value?.Value is string text:
                return new ConstantValue(new BigInteger(Strings.ConstantLength(text, value.Type)), _types.Integer);
            case (IntrinsicKind.Length, StringTypeSymbol) when !isTypeName:
                needsRuntime = true;
                return null;
            case (IntrinsicKind.Length or IntrinsicKind.High, DynamicArrayTypeSymbol) when !isTypeName:
                needsRuntime = true;                                            // ABI-041 (C-064)
                return null;
            case (IntrinsicKind.Low, DynamicArrayTypeSymbol) when !isTypeName:
                return new ConstantValue(BigInteger.Zero, _types.Integer);
            case (IntrinsicKind.Length, _):
                break;
            case (_, ArrayTypeSymbol array):
                var bound = high ? array.High : array.Low;
                return array.IndexType is BooleanTypeSymbol ? new ConstantValue(!bound.IsZero, array.IndexType) : new ConstantValue(bound, array.IndexType);
            case (_, BooleanTypeSymbol):
                return new ConstantValue(high, type);
            case (_, IntegerTypeSymbol or CharTypeSymbol or EnumTypeSymbol):
            {
                var (min, max) = OrdinalBounds(type);
                return new ConstantValue(high ? max : min, type);
            }

            // ShortString type: 0 / capacity; a string value: 1 / Length
            case (_, StringTypeSymbol { Variant: StringVariant.Short } s) when isTypeName:
                return new ConstantValue(new BigInteger(high ? s.MaxLength : 0), _types.Integer);
            case (_, StringTypeSymbol) when !isTypeName && !high:
                return new ConstantValue(BigInteger.One, _types.Integer);
            case (_, StringTypeSymbol) when !isTypeName && value?.Value is string text:
                return new ConstantValue(new BigInteger(Strings.ConstantLength(text, value.Type)), _types.Integer);
            case (_, StringTypeSymbol) when !isTypeName:
                needsRuntime = true;
                return null;
        }

        _diagnostics.Report(call, "E2911", intrinsic.Name);
        return null;
    }

    /// <summary>Ord: 64-bit integers keep their type, Cardinal widens to Int64, everything else is Integer.</summary>
    private TypeSymbol OrdType(TypeSymbol operand) => operand switch
    {
        IntegerTypeSymbol { Bits: 64 } => operand,
        IntegerTypeSymbol { Bits: 32, IsSigned: false } => _types.Int64,
        _ => _types.Integer,
    };

    /// <summary>LANG-222: Integer, Cardinal, Int64 or UInt64.</summary>
    internal TypeSymbol ComputationTypeSymbol(IntegerTypeSymbol type) => ComputationType(type) switch
    {
        (32, true) => _types.Integer,
        (32, false) => _types.Cardinal,
        (_, true) => _types.Int64,
        _ => _types.UInt64,
    };

    private static (BigInteger Min, BigInteger Max) OrdinalBounds(TypeSymbol type) => Ordinals.Bounds(type) ?? (BigInteger.Zero, BigInteger.One);

    // ------------------------------------------------------------------ values

    private ConstantValue? Integer(BigInteger value, SyntaxNode at)
    {
        if (IntegerTypeFor(value) is { } type)
        {
            return new ConstantValue(value, type);
        }

        _diagnostics.Report(at, "E2051");
        return null;
    }

    private ConstantValue? Real(double value, SyntaxNode at)
    {
        if (double.IsFinite(value))
        {
            return new ConstantValue(value, _types.Double);
        }

        _diagnostics.Report(at, "E2051");
        return null;
    }

    /// <summary>LANG-341: the first of Integer, Int64, UInt64 holding the value; E2051 outside both 64-bit ranges.</summary>
    private ConstantValue? Integer(BigInteger value, SyntaxToken op)
    {
        if (IntegerTypeFor(value) is { } type)
        {
            return new ConstantValue(value, type);
        }

        _diagnostics.Report(op, "E2051");
        return null;
    }

    private ConstantValue? Real(double value, SyntaxToken op)
    {
        if (double.IsFinite(value))
        {
            return new ConstantValue(value, _types.Double);
        }

        _diagnostics.Report(op, "E2051");
        return null;
    }

    private TypeSymbol? IntegerTypeFor(BigInteger value)
    {
        foreach (var type in (ReadOnlySpan<TypeSymbol>)[_types.Integer, _types.Int64, _types.UInt64])
        {
            var integer = (IntegerTypeSymbol)type;
            if (value >= integer.Min && value <= integer.Max)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>LANG-222 computation type: (bits, signed) of Integer, Cardinal, Int64 or UInt64.</summary>
    internal static (int Bits, bool Signed) ComputationType(IntegerTypeSymbol type) =>
        type.Bits < 32 || (type.Bits == 32 && type.IsSigned) ? (32, true) : (type.Bits, type.IsSigned);

    /// <summary>Two's complement truncation to <paramref name="bits"/>.</summary>
    internal static BigInteger Wrap(BigInteger value, int bits, bool isSigned)
    {
        var modulus = BigInteger.One << bits;
        var unsigned = ((value % modulus) + modulus) % modulus;
        return isSigned && unsigned >= modulus >> 1 ? unsigned - modulus : unsigned;
    }

    private static bool IsInteger(ConstantValue value) => value.Type is IntegerTypeSymbol;

    private static BigInteger? OrdinalOf(ConstantValue value) => value.Value switch
    {
        BigInteger i => i,
        bool b => b ? BigInteger.One : BigInteger.Zero,
        _ => null,
    };

    private static double? RealOf(ConstantValue value) => value.Value switch
    {
        double d => d,
        BigInteger i when IsInteger(value) => (double)i,
        _ => null,
    };

    /// <summary>Strings and characters (a character constant is usable as a string, LANG-343).</summary>
    private static string? StringOf(ConstantValue value) => value.Value switch
    {
        string s => s,
        BigInteger i when value.Type is CharTypeSymbol => ((char)(int)i).ToString(),
        _ => null,
    };
}
