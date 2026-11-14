using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>Operator overloading on records (LANG-731…740, C-123): the operator table and the lookup of declared operators.</summary>
internal static class Operators
{
    /// <summary>LANG-731: every operator name with its arity (the canonical spelling is the key).</summary>
    public static readonly ImmutableDictionary<string, int> Arity = new Dictionary<string, int>
    {
        ["Implicit"] = 1, ["Explicit"] = 1, ["Negative"] = 1, ["Positive"] = 1, ["LogicalNot"] = 1, ["Inc"] = 1, ["Dec"] = 1,
        ["Add"] = 2, ["Subtract"] = 2, ["Multiply"] = 2, ["Divide"] = 2, ["IntDivide"] = 2, ["Modulus"] = 2,
        ["LeftShift"] = 2, ["RightShift"] = 2, ["LogicalAnd"] = 2, ["LogicalOr"] = 2, ["LogicalXor"] = 2,
        ["BitwiseAnd"] = 2, ["BitwiseOr"] = 2, ["BitwiseXor"] = 2, ["Equal"] = 2, ["NotEqual"] = 2, ["GreaterThan"] = 2,
        ["GreaterThanOrEqual"] = 2, ["LessThan"] = 2, ["LessThanOrEqual"] = 2, ["In"] = 2,
    }.ToImmutableDictionary(IdentifierComparer.Instance);

    /// <summary>LANG-741: custom managed record operators (Phase 8+, E2900).</summary>
    public static bool IsManagedRecordOperator(string name) =>
        name.Equals("Initialize", StringComparison.OrdinalIgnoreCase) || name.Equals("Finalize", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Assign", StringComparison.OrdinalIgnoreCase);

    /// <summary>The operator names for a binary operator token, preferred first (LANG-731: Logical* before Bitwise*).</summary>
    public static ImmutableArray<string> BinaryNames(SyntaxKind op) => op switch
    {
        SyntaxKind.PlusToken => ["Add"],
        SyntaxKind.MinusToken => ["Subtract"],
        SyntaxKind.AsteriskToken => ["Multiply"],
        SyntaxKind.SlashToken => ["Divide"],
        SyntaxKind.DivKeyword => ["IntDivide"],
        SyntaxKind.ModKeyword => ["Modulus"],
        SyntaxKind.ShlKeyword => ["LeftShift"],
        SyntaxKind.ShrKeyword => ["RightShift"],
        SyntaxKind.AndKeyword => ["LogicalAnd", "BitwiseAnd"],
        SyntaxKind.OrKeyword => ["LogicalOr", "BitwiseOr"],
        SyntaxKind.XorKeyword => ["LogicalXor", "BitwiseXor"],
        SyntaxKind.EqualsToken => ["Equal"],
        SyntaxKind.LessThanGreaterThanToken => ["NotEqual"],
        SyntaxKind.GreaterThanToken => ["GreaterThan"],
        SyntaxKind.GreaterThanEqualsToken => ["GreaterThanOrEqual"],
        SyntaxKind.LessThanToken => ["LessThan"],
        SyntaxKind.LessThanEqualsToken => ["LessThanOrEqual"],
        SyntaxKind.InKeyword => ["In"],
        _ => [],
    };

    /// <summary>LANG-732: the operators named <paramref name="name"/> declared in the record types among <paramref name="types"/>.</summary>
    public static ImmutableArray<RoutineSymbol> Find(string name, params TypeSymbol[] types) =>
        [.. types.OfType<RecordTypeSymbol>().Distinct().SelectMany(r => r.Methods)
            .Where(m => m.OperatorName is { } op && IdentifierComparer.Instance.Equals(op, name))];

    /// <summary>
    /// LANG-740: the <c>Implicit</c> (or <paramref name="name"/>) operator of the source or destination record converting
    /// <paramref name="source"/> to exactly <paramref name="destination"/> — its parameter is the source type, or (no user conversion
    /// chained) takes the source through a built-in conversion; an exact parameter wins. Null when there is none.
    /// </summary>
    public static MethodSymbol? Conversion(TypeSymbol source, TypeSymbol destination, Conversions conversions, string name = "Implicit")
    {
        var candidates = Find(name, source, destination).Cast<MethodSymbol>()
            .Where(m => ReferenceEquals(m.ReturnType, destination) && m.Parameters is [{ IsByReference: false }])
            .ToList();
        return candidates.FirstOrDefault(m => ReferenceEquals(m.Parameters[0].Type, source))
            ?? candidates.FirstOrDefault(m => source is not RecordTypeSymbol && m.Parameters[0].Type is not RecordTypeSymbol
                && conversions.Classify(source, m.Parameters[0].Type).Exists);
    }

    /// <summary>The <c>Implicit</c> operators of a record type that convert from it (to try a built-in operator, LANG-732).</summary>
    public static IEnumerable<MethodSymbol> ConversionsFrom(TypeSymbol source) =>
        Find("Implicit", source).Cast<MethodSymbol>().Where(m => m.Parameters is [var p] && ReferenceEquals(p.Type, source) && m.ReturnType is not RecordTypeSymbol);
}
