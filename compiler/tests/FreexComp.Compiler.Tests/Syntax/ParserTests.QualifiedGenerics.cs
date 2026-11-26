namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// C-345 (generics.md LANG-776, 778, 780; grammar <c>typeId</c>, <c>qualStart</c>, <c>typeNamePath</c>): generic segments anywhere in
/// qualified type names, designator prefixes and method implementation headings.
/// </summary>
public sealed partial class ParserTests
{
    private static readonly string IntegerArgs = Args("(NamedType (IdentifierName Integer))");

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-780")]
    [InlineData("type", "U.TBox<Integer>", "(NamedType (QualifiedName (IdentifierName U) . (GenericName TBox {I})))")]
    [InlineData("type", "A.B.TBox<Integer>", "(NamedType (QualifiedName (QualifiedName (IdentifierName A) . (IdentifierName B)) . (GenericName TBox {I})))")]
    [InlineData("type", "TOuter.TBox<Integer>.TNode",
        "(NamedType (QualifiedName (QualifiedName (IdentifierName TOuter) . (GenericName TBox {I})) . (IdentifierName TNode)))")]
    [InlineData("type", "TA<Integer>.TB<string>",
        "(NamedType (QualifiedName (GenericName TA {I}) . (GenericName TB (TypeArgumentList < (StringType string) >))))")]
    [InlineData("expression", "U.TBox<Integer>.Create",
        "(MemberAccessExpression (MemberAccessExpression (IdentifierName U) . (GenericName TBox {I})) . (IdentifierName Create))")]
    [InlineData("expression", "TOuter.TBox<Integer>.Make(1)",
        "(InvocationExpression (MemberAccessExpression (MemberAccessExpression (IdentifierName TOuter) . (GenericName TBox {I})) . (IdentifierName Make)) "
        + "(ArgumentList ( (Argument (LiteralExpression 1)) )))")]
    [InlineData("expression", "U.TList<Integer>.TEnumerator.Create",
        "(MemberAccessExpression (MemberAccessExpression (MemberAccessExpression (IdentifierName U) . (GenericName TList {I})) . (IdentifierName TEnumerator)) "
        + ". (IdentifierName Create))")]
    [InlineData("expression", "TA<Integer>.TB<string>.Create",
        "(MemberAccessExpression (MemberAccessExpression (GenericName TA {I}) . (GenericName TB (TypeArgumentList < (StringType string) >))) . (IdentifierName Create))")]
    public void Generics_QualifiedGenericTypeNames(string position, string text, string expected)
    {
        Assert.Equal(expected.Replace("{I}", IntegerArgs, StringComparison.Ordinal), position == "type" ? DumpType(text) : DumpExpression(text));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-776")]
    [Trait("Req", "LANG-778")]
    public void Generics_NestedTypeMethodImplementationHeadings()
    {
        var t = Args("(NamedType (IdentifierName T))");
        var u = Args("(NamedType (IdentifierName U))");
        Assert.Equal(
            "(RoutineDeclaration (RoutineHeading procedure (QualifiedName (QualifiedName (IdentifierName TOuter) . "
            + $"(GenericName TBox {t})) . (IdentifierName Run))) ; (Block (CompoundStatement begin end)) ;) "
            + $"(RoutineDeclaration (RoutineHeading function (QualifiedName (QualifiedName (GenericName TA {t}) . (GenericName TB {u})) . "
            + "(IdentifierName M)) : (NamedType (IdentifierName T))) ; (Block (CompoundStatement begin end)) ;) "
            + $"(RoutineDeclaration (RoutineHeading function (QualifiedName (QualifiedName (GenericName TList {t}) . (IdentifierName TEnumerator)) . "
            + "(IdentifierName MoveNext)) : (NamedType (IdentifierName Boolean))) ; (Block (CompoundStatement begin end)) ;) "
            + $"(RoutineDeclaration (RoutineHeading function (QualifiedName (QualifiedName (QualifiedName (IdentifierName TOuter) . (GenericName TBox {t})) . "
            + $"(IdentifierName TInner)) . (IdentifierName Map)) {Params(Param("K"))} : (NamedType (IdentifierName K))) ; (Block (CompoundStatement begin end)) ;)",
            DumpDeclarations("procedure TOuter.TBox<T>.Run; begin end; function TA<T>.TB<U>.M: T; begin end; "
                             + "function TList<T>.TEnumerator.MoveNext: Boolean; begin end; function TOuter.TBox<T>.TInner.Map<K>: K; begin end;"));
    }
}
