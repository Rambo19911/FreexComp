using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Constant expressions and true-constant declarations (expressions-statements.md LANG-340…343, LANG-126, LANG-129).</summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-340")]
public sealed class ConstantFolderTests
{
    /// <summary>Binds <c>const X = expr;</c> in a program and returns "Type value" of X (Pascal literal syntax).</summary>
    private static string Fold(string expression)
    {
        var (result, _) = Bind($"const X = {expression}; begin end.");
        Assert.Equal([], Format(result.Diagnostics));
        var x = Assert.IsType<ConstantSymbol>(result.Scope.LookupLocal("X"));
        Assert.Same(x.Type, x.Value!.Type);
        return $"{x.Type.Name} {x.Value.ToDisplayString()}";
    }

    private static string[] FoldErrors(string expression) => BindDiagnostics($"const X = {expression}; begin end.");

    // ------------------------------------------------------------------ literals (LANG-126, 127, 133, 343)

    [Theory]
    [Trait("Req", "LANG-126")]
    [InlineData("0", "Integer 0")]
    [InlineData("2147483647", "Integer 2147483647")]
    [InlineData("2147483648", "Int64 2147483648")]
    [InlineData("9223372036854775807", "Int64 9223372036854775807")]
    [InlineData("9223372036854775808", "UInt64 9223372036854775808")]
    [InlineData("18446744073709551615", "UInt64 18446744073709551615")]
    [InlineData("$7FFFFFFF", "Integer 2147483647")]
    [InlineData("$80000000", "Cardinal 2147483648")]
    [InlineData("$FFFFFFFF", "Cardinal 4294967295")]
    [InlineData("$00FFFFFFFF", "Cardinal 4294967295")]
    [InlineData("$100000000", "Int64 4294967296")]
    [InlineData("$FFFFFFFFFFFFFFFF", "UInt64 18446744073709551615")]
    [InlineData("%101", "Integer 5")]
    [InlineData("&17", "Integer 15")]
    [InlineData("1_000", "Integer 1000")]
    public void IntegerLiteral_TakesTheFirstFittingType(string literal, string expected) => Assert.Equal(expected, Fold(literal));

    [Theory]
    [Trait("Req", "LANG-129")]
    [InlineData("-2147483648", "Integer -2147483648")]
    [InlineData("-2147483649", "Int64 -2147483649")]
    [InlineData("-9223372036854775808", "Int64 -9223372036854775808")]
    [InlineData("-$80000000", "Integer -2147483648")]
    [InlineData("+5", "Integer 5")]
    [InlineData("- -5", "Integer 5")]
    public void Negation_IsFoldedBeforeTyping(string expression, string expected) => Assert.Equal(expected, Fold(expression));

    [Theory]
    [Trait("Req", "LANG-343")]
    [InlineData("'abc'", "string 'abc'")]
    [InlineData("''", "string ''")]
    [InlineData("'it''s'", "string 'it''s'")]
    [InlineData("'a'", "Char 'a'")]
    [InlineData("#65", "Char 'A'")]
    [InlineData("#13", "Char #13")]
    [InlineData("''''", "Char ''''")]
    [InlineData("'a'#13#10'b'", "string 'a'#13#10'b'")]
    [InlineData("1.5", "Double 1.5")]
    [InlineData("1e3", "Double 1000.0")]
    [InlineData("True", "Boolean True")]
    [InlineData("false", "Boolean False")]
    public void Literal_HasTheTypeOfItsExpression(string literal, string expected) => Assert.Equal(expected, Fold(literal));

    // ------------------------------------------------------------------ integer operators (LANG-341, 221, 224, 225, 227)

    [Theory]
    [Trait("Req", "LANG-341")]
    [InlineData("1 + 2 * 3", "Integer 7")]
    [InlineData("(1 + 2) * 3", "Integer 9")]
    [InlineData("10 - 3 - 2", "Integer 5")]
    [InlineData("7 div 2", "Integer 3")]
    [InlineData("-7 div 2", "Integer -3")]
    [InlineData("-7 mod 2", "Integer -1")]
    [InlineData("7 mod -2", "Integer 1")]
    [InlineData("2147483647 + 1", "Int64 2147483648")]
    [InlineData("-2147483647 - 1", "Integer -2147483648")]
    [InlineData("9223372036854775807 * 2", "UInt64 18446744073709551614")]
    [InlineData("18446744073709551615 - 18446744073709551615", "Integer 0")]
    [InlineData("$FFFFFFFF + 0", "Int64 4294967295")]
    [InlineData("MaxInt + 1", "Int64 2147483648")]
    public void IntegerArithmetic_IsUnboundedThenTyped(string expression, string expected) => Assert.Equal(expected, Fold(expression));

    [Theory]
    [Trait("Req", "LANG-341")]
    [InlineData("18446744073709551615 + 1", 32)]
    [InlineData("-9223372036854775807 - 2", 32)]
    [InlineData("4294967296 * 4294967296", 22)]
    [InlineData("-9223372036854775809", 11)]
    public void IntegerOverflow_OutsideBoth64BitRanges_IsE2051(string expression, int column) =>
        Assert.Equal([$"E2051(1,{column}): Constant expression overflow"], FoldErrors(expression));

    [Theory]
    [Trait("Req", "LANG-342")]
    [InlineData("1 div 0", 13)]
    [InlineData("1 mod (2 - 2)", 13)]
    [InlineData("1 / 0", 13)]
    [InlineData("1.5 / 0.0", 15)]
    public void DivisionByZero_IsE2052(string expression, int column) =>
        Assert.Equal([$"E2052(1,{column}): Division by zero in constant expression"], FoldErrors(expression));

    [Theory]
    [Trait("Req", "LANG-224")]
    [Trait("Req", "LANG-227")]
    [InlineData("6 and 3", "Integer 2")]
    [InlineData("6 or 3", "Integer 7")]
    [InlineData("6 xor 3", "Integer 5")]
    [InlineData("(-1) and $FF", "Integer 255")]
    [InlineData("-1 and $FF", "Integer -1")]          // grammar.md simpleExpr: the sign covers the whole first term
    [InlineData("not 0", "Integer -1")]
    [InlineData("not 5", "Integer -6")]
    [InlineData("not Byte(0)", "Integer 255")]
    [InlineData("not Word(1)", "Integer 65534")]
    [InlineData("not Cardinal(0)", "Int64 4294967295")]
    [InlineData("not $FFFFFFFF", "Integer 0")]
    [InlineData("1 shl 4", "Integer 16")]
    [InlineData("1 shl 31", "Integer -2147483648")]
    [InlineData("1 shl 32", "Integer 1")]
    [InlineData("(-1) shr 28", "Integer 15")]
    [InlineData("$FFFFFFFF shr 4", "Integer 268435455")]
    [InlineData("Int64(1) shl 40", "Int64 1099511627776")]
    [InlineData("Int64(1) shl 64", "Integer 1")]
    [InlineData("UInt64(1) shl 63", "UInt64 9223372036854775808")]
    public void BitwiseOperators_FollowTheOperandTypes(string expression, string expected) => Assert.Equal(expected, Fold(expression));

    // ------------------------------------------------------------------ Boolean, relational, real, string

    [Theory]
    [InlineData("True and False", "Boolean False")]
    [InlineData("True or False", "Boolean True")]
    [InlineData("True xor True", "Boolean False")]
    [InlineData("not True", "Boolean False")]
    [InlineData("1 < 2", "Boolean True")]
    [InlineData("2 <= 1", "Boolean False")]
    [InlineData("2 = 2.0", "Boolean True")]
    [InlineData("3 <> 3", "Boolean False")]
    [InlineData("-1 < 18446744073709551615", "Boolean True")]
    [InlineData("'a' < 'b'", "Boolean True")]
    [InlineData("'abc' = 'abc'", "Boolean True")]
    [InlineData("'ab' < 'b'", "Boolean True")]
    [InlineData("'a' = 'a'", "Boolean True")]
    [InlineData("'b' > 'ab'", "Boolean True")]
    [InlineData("False < True", "Boolean True")]
    [InlineData("(1 < 2) = True", "Boolean True")]
    public void BooleanAndRelationalOperators(string expression, string expected) => Assert.Equal(expected, Fold(expression));

    [Theory]
    [Trait("Req", "LANG-226")]
    [InlineData("1 / 2", "Double 0.5")]
    [InlineData("1.5 + 1", "Double 2.5")]
    [InlineData("2 * 0.25", "Double 0.5")]
    [InlineData("-1.5", "Double -1.5")]
    [InlineData("4 / 2", "Double 2.0")]
    [InlineData("Pi * 2", "Double 6.283185307179586")]
    public void RealArithmetic_UsesDouble(string expression, string expected) => Assert.Equal(expected, Fold(expression));

    [Fact]
    public void RealOverflow_IsE2051() =>
        Assert.Equal(["E2051(1,17): Constant expression overflow"], FoldErrors("1e308 * 10"));

    [Theory]
    [Trait("Req", "LANG-343")]
    [InlineData("'ab' + 'c'", "string 'abc'")]
    [InlineData("'a' + 'b'", "string 'ab'")]
    [InlineData("'' + ''", "string ''")]
    [InlineData("#13 + #10", "string #13#10")]
    public void StringConcatenation(string expression, string expected) => Assert.Equal(expected, Fold(expression));

    // ------------------------------------------------------------------ typecasts between ordinal types

    [Theory]
    [InlineData("Byte(300)", "Byte 44")]
    [InlineData("ShortInt(200)", "ShortInt -56")]
    [InlineData("Word(-1)", "Word 65535")]
    [InlineData("Cardinal(-1)", "Cardinal 4294967295")]
    [InlineData("Integer($FFFFFFFF)", "Integer -1")]
    [InlineData("Int64(-1)", "Int64 -1")]
    [InlineData("UInt64(-1)", "UInt64 18446744073709551615")]
    [InlineData("LongInt(5)", "Integer 5")]
    [InlineData("Integer('A')", "Integer 65")]
    [InlineData("Char(66)", "Char 'B'")]
    [InlineData("AnsiChar(321)", "AnsiChar 'A'")]
    [InlineData("Boolean(1)", "Boolean True")]
    [InlineData("Boolean(0)", "Boolean False")]
    [InlineData("Integer(True)", "Integer 1")]
    [InlineData("LongBool(1)", "LongBool True")]
    [InlineData("integer(byte(256))", "Integer 0")]
    public void OrdinalTypecasts_WrapToTheTargetType(string expression, string expected) => Assert.Equal(expected, Fold(expression));

    [Theory]
    [InlineData("Double(5)", "E2040(1,11): Invalid typecast from 'Integer' to 'Double'")]
    [InlineData("Integer(1.5)", "E2040(1,11): Invalid typecast from 'Double' to 'Integer'")]
    [InlineData("Integer('ab')", "E2040(1,11): Invalid typecast from 'string' to 'Integer'")]
    public void NonOrdinalTypecasts_AreE2040(string expression, string expected) =>
        Assert.Equal([expected], FoldErrors(expression));

    // ------------------------------------------------------------------ names (LANG-340: previously declared true constants)

    [Fact]
    public void PreviouslyDeclaredConstants_CanBeUsed()
    {
        var (result, _) = Bind("const A = 2; b = a * 10; C = Byte(5); D = C; E = 'x'; F = E + E; begin end.");

        Assert.Empty(result.Diagnostics);
        string Show(string name)
        {
            var c = Assert.IsType<ConstantSymbol>(result.Scope.LookupLocal(name));
            return $"{c.Type.Name} {c.Value!.ToDisplayString()} {c.IsTyped}";
        }

        Assert.Equal("Integer 20 False", Show("B"));
        Assert.Equal("Byte 5 False", Show("D"));
        Assert.Equal("string 'xx' False", Show("F"));
    }

    [Fact]
    public void ConstantSymbol_RecordsItsDeclaration()
    {
        var (result, tree) = Bind("program P;\nconst\n  Answer = 42;\nbegin end.");

        var answer = Assert.IsType<ConstantSymbol>(result.Scope.LookupLocal("answer"));

        Assert.Equal("Answer", answer.Name);
        Assert.Same(result.Unit, answer.ContainingSymbol);
        Assert.Equal("P.Answer", answer.QualifiedName);
        var location = Assert.Single(answer.Locations);
        Assert.Equal((TestPath, 2, 2), (location.Path, location.LineSpan.Start.Line, location.LineSpan.Start.Column));
        Assert.Equal("Answer", tree.Text.Text.Substring(location.Span.Start, location.Span.Length));
        Assert.Equal("const Answer = 42", answer.ToDisplayString());
    }

    [Fact]
    public void Constant_ShadowsABuiltIn()
    {
        var (result, _) = Bind("const MaxInt = 10; X = MaxInt * 2; begin end.");

        Assert.Empty(result.Diagnostics);
        Assert.Equal("20", ((ConstantSymbol)result.Scope.LookupLocal("X")!).Value!.ToDisplayString());
    }

    [Fact]
    public void UnitConstants_BindInInterfaceAndImplementation()
    {
        var (result, _) = Bind("unit U; interface const A = 1; implementation const B = A + 1; end.");

        Assert.Empty(result.Diagnostics);
        Assert.Equal("2", ((ConstantSymbol)result.Scope.Lookup("B")!).Value!.ToDisplayString());
        Assert.NotNull(result.InterfaceScope!.LookupLocal("A"));
        Assert.Null(result.InterfaceScope.LookupLocal("B"));
        Assert.Equal(["A"], result.Unit.InterfaceMembers.Select(s => s.Name));
        Assert.Equal(["B"], result.Unit.ImplementationMembers.Select(s => s.Name));
    }

    // ------------------------------------------------------------------ errors

    [Theory]
    [InlineData("1 + 'a'", "E2011(1,13): Operator '+' not applicable to operand type(s) 'Integer' and 'Char'")]
    [InlineData("'a' - 'b'", "E2011(1,15): Operator '-' not applicable to operand type(s) 'Char' and 'Char'")]
    [InlineData("True + 1", "E2011(1,16): Operator '+' not applicable to operand type(s) 'Boolean' and 'Integer'")]
    [InlineData("not 'a'", "E2011(1,11): Operator 'not' not applicable to operand type(s) 'Char'")]
    [InlineData("-True", "E2011(1,11): Operator '-' not applicable to operand type(s) 'Boolean'")]
    [InlineData("1.5 div 2", "E2011(1,15): Operator 'div' not applicable to operand type(s) 'Double' and 'Integer'")]
    [InlineData("True and 1", "E2011(1,16): Operator 'and' not applicable to operand type(s) 'Boolean' and 'Integer'")]
    [InlineData("1 = 'a'", "E2011(1,13): Operator '=' not applicable to operand type(s) 'Integer' and 'Char'")]
    [InlineData("1 in 2", "E2011(1,13): Operator 'in' not applicable to operand type(s) 'Integer' and 'Integer'")]
    public void InapplicableOperator_IsE2011(string expression, string expected) => Assert.Equal([expected], FoldErrors(expression));

    [Theory]
    [InlineData("Missing + 1", "E2003(1,11): Undeclared identifier 'Missing'")]
    [InlineData("X + 1", "E2003(1,11): Undeclared identifier 'X'")]
    [InlineData("Integer", "E2009(1,11): Variable or constant expected but type 'Integer' found")]
    public void Names_MustBePreviouslyDeclaredConstants(string expression, string expected) =>
        Assert.Equal([expected], FoldErrors(expression));

    [Fact]
    public void Errors_DoNotCascade()
    {
        var (result, _) = Bind("const A = 1 div 0; B = A + 1; C = B * 2; begin end.");

        Assert.Equal(["E2052(1,13): Division by zero in constant expression"], Format(result.Diagnostics));
        var b = Assert.IsType<ConstantSymbol>(result.Scope.LookupLocal("B"));
        Assert.Null(b.Value);
    }

    [Fact]
    public void SyntaxErrors_GiveNoBinderDiagnostics()
    {
        var tree = FreexComp.Compiler.Syntax.SyntaxTree.Parse(SourceText.From(TestPath, "const A = 1 + ; B = (; begin end."));
        Assert.NotEmpty(tree.Diagnostics);

        var result = Compiler.Binding.DeclarationBinder.BindCompilationUnit(tree, new FreexComp.Compiler.Types.BuiltInTypes());

        Assert.Empty(result.Diagnostics);
    }

    /// <summary>C-056 / LANG-266: <c>nil</c> is a constant (it was E2900 before pointers existed); <c>nil = nil</c> folds.</summary>
    [Theory]
    [InlineData("nil")]
    [InlineData("nil = nil")]
    public void NilConstants_Fold(string expression) =>
        Assert.Empty(FoldErrors(expression));

    /// <summary>C-050: set constants fold (they were E2900 until M2.2); details in SetBindingTests.</summary>
    [Fact]
    [Trait("Req", "LANG-251")]
    public void SetConstant_Folds() => Assert.Empty(FoldErrors("[1, 2]"));

    [Fact]
    [Trait("Req", "LANG-344")]
    public void TypedConstant_IsNotAConstantExpression()
    {
        // C-054 replaced the interim E2900: a typed constant binds (read-only storage) but cannot be folded (LANG-340)
        var (result, _) = Bind("const T: Integer = 5; U = T; begin end.");

        Assert.Equal(["E2050(1,27): Constant expression expected"], Format(result.Diagnostics));
        var constant = Assert.IsType<ConstantSymbol>(result.Scope.LookupLocal("T"));
        Assert.Equal((true, "5"), (constant.IsTyped, constant.Value!.ToDisplayString()));
    }

    [Fact]
    [Trait("Req", "LANG-346")]
    public void ResourceString_IsAStringConstant()
    {
        var (result, _) = Bind("resourcestring S = 'Hello'; C = 'x'; N = 5; begin end.");

        Assert.Equal(["E2010(1,42): Incompatible types: 'Integer' and 'string'"], Format(result.Diagnostics));
        Assert.Equal("string 'Hello'", $"{((ConstantSymbol)result.Scope.LookupLocal("S")!).Type.Name} {((ConstantSymbol)result.Scope.LookupLocal("S")!).Value}");
        Assert.Equal("string 'x'", $"{((ConstantSymbol)result.Scope.LookupLocal("C")!).Type.Name} {((ConstantSymbol)result.Scope.LookupLocal("C")!).Value}");
    }

    [Fact]
    [Trait("Req", "LANG-402")]
    public void RedeclaredConstant_IsE2004()
    {
        Assert.Equal(["E2004(1,14): Identifier redeclared: 'a'"], BindDiagnostics("const A = 1; a = 2; begin end."));
    }

    /// <summary>C-185 (LANG-340): an integer → pointer typecast is a constant (<c>IDC_ARROW = PWideChar(32512)</c>); 0 is nil.</summary>
    [Theory]
    [Trait("Req", "LANG-340")]
    [InlineData("PWideChar(32512)", "PChar 32512")]          // PWideChar is an alias of PChar
    [InlineData("PChar($7F00)", "PChar 32512")]
    [InlineData("Pointer(1)", "Pointer 1")]
    [InlineData("Pointer(0)", "Pointer nil")]
    [InlineData("PAnsiChar(-1)", "PAnsiChar 18446744073709551615")]
    [InlineData("Pointer(5000000000)", "Pointer 5000000000")]
    public void IntegerToPointerCast(string expression, string expected) => Assert.Equal(expected, Fold(expression));

    /// <summary>
    /// C-357 (LANG-290, LANG-785): a typecast whose callee is a qualified, nested or generic type name folds like <c>TEn(1)</c> — in a
    /// constant declaration, an array bound and a case label.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-290")]
    [Trait("Req", "LANG-785")]
    [InlineData("TOuter.TEn(1)", "TOuter.TEn")]
    [InlineData("TK<Integer>.TEn(1)", "TK<Integer>.TEn")]
    [InlineData("P.TOuter.TEn(1)", "TOuter.TEn")]
    [InlineData("P.TK<Integer>.TEn(1)", "TK<Integer>.TEn")]
    [InlineData("TD.TEn(1)", "TK<Integer>.TEn")]
    [InlineData("TOuter.TSmall(300)", "Byte")]                   // a nested alias of Byte
    public void QualifiedTypecast_Folds(string expression, string type)
    {
        var (result, _) = Bind("""
            program P;
            type
              TOuter = class
              public
                type
                  TEn = (eA, eB, eC);
                  TSmall = Byte;
              end;
              TK<T> = class
              public
                type TEn = (kA, kB, kC);
              end;
              TD = class(TK<Integer>)
              end;
            const
              C = {E};
            type
              TArr = array[0..Ord({E})] of Byte;
            var
              N: Integer;
            begin
              case N of
                Ord({E}): N := 0;
              end;
            end.
            """.Replace("{E}", expression));

        Assert.Equal([], Format(result.Diagnostics).Where(d => !d.StartsWith('H') && !d.StartsWith('W')));
        var constant = Assert.IsType<ConstantSymbol>(result.Scope.LookupLocal("C"));
        var expected = expression.EndsWith("(300)") ? 44 : 1;                   // Byte(300) wraps (LANG-290 row 1)
        Assert.Equal((type, new BigInteger(expected)), (TypeMembers.DisplayName(constant.Type), constant.Value!.Value));
        Assert.Same(constant.Type, constant.Value.Type);
        Assert.Equal(new BigInteger(expected), Assert.IsType<ArrayTypeSymbol>(result.Scope.LookupLocal("TArr")).High);
    }
}
