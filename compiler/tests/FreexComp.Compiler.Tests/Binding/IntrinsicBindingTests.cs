using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>The M2.1 intrinsics in bodies and in constant expressions (intrinsics.md §1, LANG-900…903, LANG-340).</summary>
[Trait("Phase", "2")]
public sealed class IntrinsicBindingTests
{
    private const string Vars = "var i: Integer; b: Byte; c: Char; f: Boolean; d: Double; u: Cardinal; q: Int64; s: string; t: Text; w: Word; ";

    /// <summary>The dump of <c>program T; {Vars} begin {statements} end.</c>, which must bind without diagnostics.</summary>
    private static string Body(string statements)
    {
        var (result, _) = Bind("program T; " + Vars + "begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(Assert.Single(result.Bodies));
    }

    private static string[] Errors(string statements) => BindDiagnostics("program T; " + Vars + "begin " + statements + " end.");

    /// <summary>The value of <c>const K = {expression};</c> as "value:Type".</summary>
    private static string Constant(string expression)
    {
        var (result, _) = Bind("program T; " + Vars + "const K = " + expression + "; begin end.");
        Assert.Empty(Format(result.Diagnostics));
        var constant = result.Unit.ImplementationMembers.OfType<ConstantSymbol>().Single(x => x.Name == "K");
        return $"{constant.Value!.ToDisplayString()}:{constant.Type.Name}";
    }

    private static string[] ConstantErrors(string expression) => BindDiagnostics("program T; " + Vars + "const K = " + expression + "; begin end.");

    // ------------------------------------------------------------------ Inc, Dec (LANG-901)

    [Fact]
    [Trait("Req", "LANG-901")]
    public void M21IncDec_AllForms() =>
        Assert.Equal("(block (Inc i:Integer) (Inc b:Byte 2:Integer) (Dec c:Char) (Dec q:Int64 i:Integer) (Inc f:Boolean))",
            Body("Inc(i); Inc(b, 2); Dec(c); Dec(q, i); Inc(f)"));

    [Theory]
    [Trait("Req", "LANG-901")]
    [InlineData("Inc(5)", "E2112(1,131): Constant object cannot be passed as var parameter")]
    [InlineData("Inc(i + 1)", "E2113(1,131): Variable required")]
    [InlineData("Inc(d)", "E2013(1,131): Ordinal type required")]
    [InlineData("Inc(i, 1.5)", "E2010(1,134): Incompatible types: 'Double' and 'Integer'")]
    [InlineData("Inc", "E2110(1,127): Not enough actual parameters for 'Inc'")]
    [InlineData("Inc(i, 1, 2)", "E2111(1,137): Too many actual parameters for 'Inc'")]
    [InlineData("i := Inc(i)", "E2034(1,132): Expression has no value (procedure call used as a value)")]
    [InlineData("for i := 1 to 2 do Inc(i)", "E2053(1,150): Left side cannot be assigned to")]
    [InlineData("Inc(i:2)", "E2915(1,132): Width specifiers are only allowed in Write, Writeln and Str")]
    public void M21IncDec_Errors(string statement, string expected) => Assert.Equal([expected], Errors(statement));

    [Fact]
    [Trait("Req", "LANG-901")]
    public void M21Inc_ConstParameter_IsE2112() =>
        Assert.Equal(["E2112(1,53): Constant object cannot be passed as var parameter"],
            BindDiagnostics("program T; procedure P(const a: Integer); begin Inc(a) end; begin end."));

    // ------------------------------------------------------------------ Succ, Pred, Ord, Chr, Odd

    [Fact]
    [Trait("Req", "LANG-340")]
    public void M21SuccPredOrdChrOdd_InBodies() =>
        Assert.Equal(
            "(block (:= i:Integer (Succ:Integer i:Integer)) (:= c:Char (Pred:Char c:Char)) (:= i:Integer (Ord:Integer c:Char)) " +
            "(:= q:Int64 (Ord:Int64 q:Int64)) (:= q:Int64 (Ord:Int64 u:Cardinal)) (:= c:Char (Chr:Char b:Byte)) (:= f:Boolean (Odd:Boolean i:Integer)) " +
            "(:= f:Boolean (Succ:Boolean f:Boolean)))",
            Body("i := Succ(i); c := Pred(c); i := Ord(c); q := Ord(q); q := Ord(u); c := Chr(b); f := Odd(i); f := Succ(f)"));

    [Theory]
    [Trait("Req", "LANG-340")]
    [InlineData("Succ(5)", "6:Integer")]
    [InlineData("Pred('b')", "'a':Char")]
    [InlineData("Succ(False)", "True:Boolean")]
    [InlineData("Ord('A')", "65:Integer")]
    [InlineData("Ord(True)", "1:Integer")]
    [InlineData("Ord(Int64(5))", "5:Int64")]
    [InlineData("Chr(66)", "'B':Char")]
    [InlineData("Odd(7)", "True:Boolean")]
    [InlineData("Odd(-4)", "False:Boolean")]
    [InlineData("Succ(Byte(254))", "255:Byte")]
    public void M21SuccPredOrdChrOdd_Fold(string expression, string expected) => Assert.Equal(expected, Constant(expression));

    [Theory]
    [Trait("Req", "LANG-900")]
    [InlineData("Succ(1.5)", "E2013(1,136): Ordinal type required")]
    [InlineData("Ord('ab')", "E2013(1,135): Ordinal type required")]
    [InlineData("Chr('a')", "E2010(1,135): Incompatible types: 'Char' and 'Integer'")]
    [InlineData("Odd(True)", "E2010(1,135): Incompatible types: 'Boolean' and 'Integer'")]
    [InlineData("Succ(Byte(255))", "E2012(1,131): Constant value 256 is out of range for type 'Byte'")]
    [InlineData("Pred(Low(Integer))", "E2012(1,131): Constant value -2147483649 is out of range for type 'Integer'")]
    [InlineData("Chr(65536)", "E2012(1,135): Constant value 65536 is out of range for type 'Char'")]
    [InlineData("Succ", "E2110(1,131): Not enough actual parameters for 'Succ'")]
    [InlineData("Ord(1, 2)", "E2111(1,138): Too many actual parameters for 'Ord'")]
    [InlineData("Succ(i)", "E2050(1,136): Constant expression expected")]
    [InlineData("Inc(i)", "E2050(1,131): Constant expression expected")]
    public void M21Intrinsic_ConstantErrors(string expression, string expected) => Assert.Equal([expected], ConstantErrors(expression));

    // ------------------------------------------------------------------ Abs, Sqr

    [Fact]
    [Trait("Req", "LANG-340")]
    public void M21AbsSqr_InBodies() =>
        Assert.Equal(
            "(block (:= i:Integer (Abs:Integer i:Integer)) (:= q:Int64 (conv Int64 (Abs:Integer (conv Integer b:Byte)))) (:= d:Double (Abs:Double d:Double)) " +
            "(:= q:Int64 (Sqr:Int64 q:Int64)) (:= u:Cardinal (Sqr:Cardinal u:Cardinal)) (:= d:Double (Sqr:Double d:Double)))",
            Body("i := Abs(i); q := Abs(b); d := Abs(d); q := Sqr(q); u := Sqr(u); d := Sqr(d)"));

    [Theory]
    [Trait("Req", "LANG-340")]
    [InlineData("Abs(-5)", "5:Integer")]
    [InlineData("Abs(Low(Integer))", "2147483648:Int64")]
    [InlineData("Abs(-2.5)", "2.5:Double")]
    [InlineData("Sqr(-3)", "9:Integer")]
    [InlineData("Sqr(100000)", "10000000000:Int64")]
    [InlineData("Sqr(1.5)", "2.25:Double")]
    public void M21AbsSqr_Fold(string expression, string expected) => Assert.Equal(expected, Constant(expression));

    [Theory]
    [Trait("Req", "LANG-900")]
    [InlineData("i := Abs(c)", "E2010(1,136): Incompatible types: 'Char' and 'Integer'")]
    [InlineData("i := Sqr(f)", "E2010(1,136): Incompatible types: 'Boolean' and 'Integer'")]
    public void M21AbsSqr_Errors(string statement, string expected) => Assert.Equal([expected], Errors(statement));

    [Fact]
    [Trait("Req", "LANG-341")]
    public void M21Sqr_ConstantOverflow_IsE2051() =>
        Assert.Equal(["E2051(1,131): Constant expression overflow"], ConstantErrors("Sqr(Int64(5000000000))"));

    // ------------------------------------------------------------------ Trunc, Round, Int, Frac, Sqrt (C-059)

    /// <summary>intrinsics.md: the operand converts to <c>Double</c> (integers and <c>Single</c> too); Trunc/Round give Int64, the others Double.</summary>
    [Fact]
    [Trait("Req", "LANG-231")]
    public void RealFunctions_InBodies() =>
        Assert.Equal(
            "(block (:= q:Int64 (Trunc:Int64 d:Double)) (:= q:Int64 (Round:Int64 d:Double)) (:= d:Double (Int:Double d:Double)) " +
            "(:= d:Double (Frac:Double d:Double)) (:= d:Double (Sqrt:Double d:Double)) (:= i:Integer (conv Integer (Trunc:Int64 (conv Double i:Integer)))) " +
            "(:= d:Double (Sqrt:Double (conv Double q:Int64))) (:= d:Double (Sqrt:Double 2.0:Double)) (:= q:Int64 3:Int64))",
            Body("q := Trunc(d); q := Round(d); d := Int(d); d := Frac(d); d := Sqrt(d); i := Trunc(i); d := Sqrt(q); d := Sqrt(2); q := Round(2.5 + 0.5)"));

    [Fact]
    [Trait("Req", "LANG-230")]
    public void RealFunctions_SingleOperandConvertsToDouble() =>
        Assert.Equal("(block (:= q:Int64 (Round:Int64 (conv Double s:Single))) (:= s:Single (conv Single (Frac:Double (conv Double s:Single)))))",
            BoundDump.Of(Assert.Single(Bind("program T; var s: Single; q: Int64; begin q := Round(s); s := Frac(s) end.").Result.Bodies)));

    /// <summary>LANG-340: Trunc and Round fold (toward zero / ties to even); Int, Frac and Sqrt are not constant expressions.</summary>
    [Theory]
    [Trait("Req", "LANG-340")]
    [InlineData("Trunc(2.7)", "2:Int64")]
    [InlineData("Trunc(-2.7)", "-2:Int64")]
    [InlineData("Trunc(7)", "7:Int64")]
    [InlineData("Round(2.5)", "2:Int64")]
    [InlineData("Round(3.5)", "4:Int64")]
    [InlineData("Round(-2.5)", "-2:Int64")]
    [InlineData("Round(-2.51)", "-3:Int64")]
    [InlineData("Round(1e18)", "1000000000000000000:Int64")]
    public void RealFunctions_Fold(string expression, string expected) => Assert.Equal(expected, Constant(expression));

    [Theory]
    [Trait("Req", "LANG-340")]
    [InlineData("Int(2.5)", "E2050(1,131): Constant expression expected")]
    [InlineData("Frac(2.5)", "E2050(1,131): Constant expression expected")]
    [InlineData("Sqrt(4)", "E2050(1,131): Constant expression expected")]
    [InlineData("Trunc(1e19)", "E2051(1,131): Constant expression overflow")]
    [InlineData("Round(-1e19)", "E2051(1,131): Constant expression overflow")]
    [InlineData("Trunc('a')", "E2010(1,137): Incompatible types: 'Char' and 'Double'")]
    public void RealFunctions_ConstantErrors(string expression, string expected) => Assert.Equal([expected], ConstantErrors(expression));

    [Theory]
    [Trait("Req", "LANG-900")]
    [InlineData("q := Trunc(c)", "E2010(1,138): Incompatible types: 'Char' and 'Double'")]
    [InlineData("d := Sqrt(f)", "E2010(1,137): Incompatible types: 'Boolean' and 'Double'")]
    [InlineData("i := Round(d)", null)]
    [InlineData("d := Trunc(d)", null)]
    [InlineData("q := Int(d)", "E2010(1,132): Incompatible types: 'Double' and 'Int64'")]
    public void RealFunctions_Errors(string statement, string? expected) => Assert.Equal(expected is null ? [] : [expected], Errors(statement));

    // ------------------------------------------------------------------ Low, High, SizeOf (LANG-902, 903)

    [Theory]
    [Trait("Req", "LANG-340")]
    [InlineData("High(Integer)", "2147483647:Integer")]
    [InlineData("Low(Integer)", "-2147483648:Integer")]
    [InlineData("High(Byte)", "255:Byte")]
    [InlineData("Low(ShortInt)", "-128:ShortInt")]
    [InlineData("High(UInt64)", "18446744073709551615:UInt64")]
    [InlineData("High(Char)", "'￿':Char")]
    [InlineData("Low(AnsiChar)", "#0:AnsiChar")]
    [InlineData("High(Boolean)", "True:Boolean")]
    [InlineData("Low(LongBool)", "False:LongBool")]
    [InlineData("High(i)", "2147483647:Integer")]
    [InlineData("Low(w)", "0:Word")]
    [InlineData("High(ShortString)", "255:Integer")]
    [InlineData("Low(ShortString)", "0:Integer")]
    [InlineData("High('abc')", "3:Integer")]
    [InlineData("Low('abc')", "1:Integer")]
    [InlineData("High(5)", "2147483647:Integer")]
    [InlineData("SizeOf(Integer)", "4:Integer")]
    [InlineData("SizeOf(Int64)", "8:Integer")]
    [InlineData("SizeOf(Char)", "2:Integer")]
    [InlineData("SizeOf(Boolean)", "1:Integer")]
    [InlineData("SizeOf(WordBool)", "2:Integer")]
    [InlineData("SizeOf(Double)", "8:Integer")]
    [InlineData("SizeOf(string)", "8:Integer")]
    [InlineData("SizeOf(ShortString)", "256:Integer")]
    [InlineData("SizeOf(Pointer)", "8:Integer")]
    [InlineData("SizeOf(Text)", "624:Integer")]
    [InlineData("SizeOf(w)", "2:Integer")]
    [InlineData("SizeOf(d)", "8:Integer")]
    [InlineData("High(Integer) - Low(Byte) + SizeOf(i)", "2147483651:Int64")]
    public void M21LowHighSizeOf_Fold(string expression, string expected) => Assert.Equal(expected, Constant(expression));

    [Fact]
    [Trait("Req", "LANG-903")]
    public void M21LowHighSizeOf_InBodies_AreConstants() =>
        Assert.Equal("(block (:= q:Int64 2147483647:Int64) (:= i:Integer 8:Integer) (:= i:Integer 1:Integer) (:= b:Byte 0:Byte))",
            Body("q := High(i); i := SizeOf(q * 2); i := Low(s); b := Low(Byte)"));

    [Theory]
    [Trait("Req", "LANG-902")]
    [InlineData("High(Double)", "E2911(1,131): 'High' cannot be applied to this type")]
    [InlineData("Low(string)", "E2911(1,131): 'Low' cannot be applied to this type")]
    [InlineData("High(1.5)", "E2911(1,131): 'High' cannot be applied to this type")]
    [InlineData("Low(Pointer)", "E2911(1,131): 'Low' cannot be applied to this type")]
    [InlineData("High(s)", "E2050(1,131): Constant expression expected")]
    [InlineData("SizeOf(Undefined)", "E2003(1,138): Undeclared identifier 'Undefined'")]
    public void M21LowHighSizeOf_ConstantErrors(string expression, string expected) => Assert.Equal([expected], ConstantErrors(expression));

    [Fact]
    [Trait("Req", "LANG-902")]
    public void M21High_OfAStringVariable_WaitsForLength() =>
        Assert.Equal(["E2900(1,132): Feature not supported: High of a string variable"], Errors("i := High(s)"));

    // ------------------------------------------------------------------ Lo, Hi, Swap

    [Fact]
    [Trait("Req", "LANG-340")]
    public void M21LoHiSwap_InBodies() =>
        Assert.Equal("(block (:= b:Byte (Lo:Byte i:Integer)) (:= b:Byte (Hi:Byte w:Word)) (:= w:Word (Swap:Word w:Word)))",
            Body("b := Lo(i); b := Hi(w); w := Swap(w)"));

    [Theory]
    [Trait("Req", "LANG-340")]
    [InlineData("Lo($1234)", "52:Byte")]
    [InlineData("Hi($1234)", "18:Byte")]
    [InlineData("Hi(-1)", "255:Byte")]
    [InlineData("Swap($1234)", "13330:Word")]
    [InlineData("Swap($ABCD1234)", "13330:Word")]
    public void M21LoHiSwap_Fold(string expression, string expected) => Assert.Equal(expected, Constant(expression));

    [Fact]
    [Trait("Req", "LANG-900")]
    public void M21LoHiSwap_RequireIntegers() =>
        Assert.Equal(["E2010(1,135): Incompatible types: 'Char' and 'Integer'"], Errors("b := Lo(c)"));

    // ------------------------------------------------------------------ RunError

    [Fact]
    [Trait("Req", "LANG-900")]
    public void M21RunError_WithAndWithoutCode() =>
        Assert.Equal("(block (RunError) (RunError 201:Integer) (RunError (conv Integer b:Byte)))", Body("RunError; RunError(201); RunError(b)"));

    [Theory]
    [Trait("Req", "LANG-900")]
    [InlineData("RunError(1, 2)", "E2111(1,139): Too many actual parameters for 'RunError'")]
    [InlineData("RunError('x')", "E2010(1,136): Incompatible types: 'Char' and 'Integer'")]
    public void M21RunError_Errors(string statement, string expected) => Assert.Equal([expected], Errors(statement));

    // ------------------------------------------------------------------ general

    [Fact]
    [Trait("Req", "LANG-900")]
    public void M21FunctionIntrinsic_AsAStatement_IsE2060() =>
        Assert.Equal(["E2060(1,127): Statement expected, but expression of type 'Integer' found"], Errors("Succ(i)"));

    [Fact]
    [Trait("Req", "LANG-900")]
    public void M21Intrinsic_CanBeHiddenByAUserSymbol() =>
        Assert.Equal("(block (:= i:Integer (call Succ:Integer i:Integer)))",
            BoundDump.Of(Assert.Single(Bind("program T; var i: Integer; function Succ(x: Integer): Integer; begin Result := x end; begin i := Succ(i) end.").Result.Bodies)));

    [Fact]
    [Trait("Req", "LANG-900")]
    public void M21Intrinsics_AreDeclaredInTheBuiltInScope()
    {
        var types = new FreexComp.Compiler.Types.BuiltInTypes();
        foreach (var name in (string[])["Inc", "Dec", "Succ", "Pred", "Ord", "Chr", "Odd", "Abs", "Sqr", "Low", "High", "SizeOf", "Lo", "Hi", "Swap", "RunError"])
        {
            Assert.IsType<IntrinsicSymbol>(types.Scope.LookupLocal(name));
        }
    }

    // ------------------------------------------------------------------ Assert (intrinsics.md §1, C-057)

    [Fact]
    [Trait("Req", "LANG-900")]
    public void Assert_ConditionAndOptionalConstantMessage() =>
        Assert.Equal("(block (Assert (Greater:Boolean i:Integer 0:Integer)) (Assert f:Boolean 'no f':string) (Assert True:Boolean))",
            Body("Assert(i > 0); Assert(f, 'no f'); Assert(ByteBool(True))"));

    [Theory]
    [Trait("Req", "LANG-900")]
    [InlineData("Assert(i)", "E2010(1,134): Incompatible types: 'Integer' and 'Boolean'")]
    [InlineData("Assert(f, 3)", "E2010(1,137): Incompatible types: 'Integer' and 'string'")]
    [InlineData("Assert(f, s)", "E2900(1,137): Feature not supported: Assert message that is not a constant")]
    [InlineData("Assert", "E2110(1,127): Not enough actual parameters for 'Assert'")]
    [InlineData("Assert(f, 'a', 1)", "E2111(1,142): Too many actual parameters for 'Assert'")]
    [InlineData("i := Assert(f)", "E2034(1,132): Expression has no value (procedure call used as a value)")]
    public void Assert_Errors(string statement, string expected) => Assert.Equal([expected], Errors(statement));
}
