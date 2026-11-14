using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-062: the string intrinsics <c>SetLength Copy Delete Insert Pos Concat UpCase Str Val</c> on UnicodeStrings (intrinsics.md §1,
/// §4): argument forms, conversions, errors; none is constant (LANG-340).
/// </summary>
[Trait("Phase", "2")]
public sealed class StringIntrinsicBindingTests
{
    private const string Vars = "var s, t: string; c: Char; a: AnsiChar; i: Integer; b: Byte; w: Word; q: Int64; u: UInt64; d: Double; f: Single; bo: Boolean; p: Pointer; ";

    private static string Body(string statements)
    {
        var (result, _) = Bind("program T; " + Vars + "begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(Assert.Single(result.Bodies));
    }

    private static string[] Codes(string text) => [.. BindDiagnostics(text).Select(d => d[..d.IndexOf('(')])];

    private static string[] Errors(string statements) => Codes("program T; " + Vars + "begin " + statements + " end.");

    [Fact]
    [Trait("Req", "LANG-900")]
    public void Procedures_ArgumentsInParameterOrder() =>
        Assert.Equal(
            "(block (SetLength s:string 3:Integer) (SetLength s:string (conv Integer q:Int64)) (Delete s:string 2:Integer i:Integer) " +
            "(Insert 'ab':string s:string i:Integer) (Insert (conv string c:Char) s:string 1:Integer) (Insert t:string s:string (conv Integer b:Byte)))",
            Body("SetLength(s, 3); SetLength(s, q); Delete(s, 2, i); Insert('ab', s, i); Insert(c, s, 1); Insert(t, s, b)"));

    /// <summary>LANG-951: <c>Str(x[:w[:d]], s)</c> keeps the value unconverted, then the string, then width and decimals as Integer.</summary>
    [Fact]
    [Trait("Req", "LANG-951")]
    public void Str_OfBooleansStringsAndCharacters_AsStrings() =>
        Assert.Equal("(block (Str bo:Boolean s:string) (Str t:string s:string 3:Integer) (Str (conv string c:Char) s:string) (Str (conv string a:AnsiChar) s:string))",
            Body("Str(bo, s); Str(t:3, s); Str(c, s); Str(a, s)"));

    [Fact]
    [Trait("Req", "LANG-951")]
    public void Str_ValueStringWidthDecimals() =>
        Assert.Equal(
            "(block (Str i:Integer s:string) (Str q:Int64 s:string 5:Integer) (Str u:UInt64 s:string) (Str d:Double s:string 8:Integer 2:Integer) " +
            "(Str f:Single s:string i:Integer) (Str 42:Integer s:string))",
            Body("Str(i, s); Str(q:5, s); Str(u, s); Str(d:8:2, s); Str(f:i, s); Str(42, s)"));

    /// <summary>LANG-950: <c>Val(s, v, code)</c>: a string (a char converts), an integer or real variable, an integer variable.</summary>
    [Fact]
    [Trait("Req", "LANG-950")]
    public void Val_StringVariableCode() =>
        Assert.Equal(
            "(block (Val s:string i:Integer i:Integer) (Val 'x':string d:Double w:Word) (Val (conv string c:Char) b:Byte i:Integer) (Val s:string u:UInt64 q:Int64))",
            Body("Val(s, i, i); Val('x', d, w); Val(c, b, i); Val(s, u, q)"));

    [Fact]
    [Trait("Req", "LANG-900")]
    public void Functions_TypesAndArguments() =>
        Assert.Equal(
            "(block (:= t:string (Copy:string s:string 2:Integer 3:Integer)) (:= t:string (Copy:string s:string i:Integer)) " +
            "(:= i:Integer (Pos:Integer 'b':string s:string)) (:= i:Integer (Pos:Integer (conv string c:Char) s:string (conv Integer q:Int64))) " +
            "(:= c:Char (UpCase:Char c:Char)) (:= c:Char (UpCase:Char (conv Char a:AnsiChar))) (:= c:Char (UpCase:Char 'q':Char)))",
            Body("t := Copy(s, 2, 3); t := Copy(s, i); i := Pos('b', s); i := Pos(c, s, q); c := UpCase(c); c := UpCase(a); c := UpCase('q')"));

    /// <summary>intrinsics.md: <c>Concat</c> is the same as <c>+</c> (a single argument is that string).</summary>
    [Fact]
    [Trait("Req", "LANG-243")]
    public void Concat_IsConcatenation() =>
        Assert.Equal(
            "(block (:= t:string (Add:string (Add:string s:string 'x':string) (conv string c:Char))) (:= t:string s:string) (:= t:string (conv string c:Char)))",
            Body("t := Concat(s, 'x', c); t := Concat(s); t := Concat(c)"));

    [Theory]
    [Trait("Req", "LANG-900")]
    [InlineData("SetLength(s)", "E2110")]
    [InlineData("SetLength(s, 1, 2)", "E2111")]
    [InlineData("SetLength(i, 1)", "E2010")]
    [InlineData("SetLength(s, d)", "E2010")]
    [InlineData("SetLength('x', 1)", "E2112")]
    [InlineData("Delete(s, 1)", "E2110")]
    [InlineData("Delete(s + t, 1, 1)", "E2113")]
    [InlineData("Insert(i, s, 1)", "E2010")]
    [InlineData("Insert(s, t + s, 1)", "E2113")]
    [InlineData("Str(p, s)", "E2010")]
    [InlineData("Str(i:2:1, s)", "E2912")]
    [InlineData("Str(i, d)", "E2010")]
    [InlineData("Str(i, s:2)", "E2915")]
    [InlineData("Str(i)", "E2110")]
    [InlineData("Val(i, i, i)", "E2010")]
    [InlineData("Val(s, bo, i)", "E2010")]
    [InlineData("Val(s, i, d)", "E2010")]
    [InlineData("Val(s, i, 3)", "E2112")]
    [InlineData("Val(s, i)", "E2110")]
    [InlineData("t := Copy(s)", "E2110")]
    [InlineData("t := Copy(s, 1, 2, 3)", "E2111")]
    [InlineData("t := Copy(i, 1, 2)", "E2010")]
    [InlineData("i := Pos(s)", "E2110")]
    [InlineData("i := Pos(s, i)", "E2010")]
    [InlineData("t := Concat()", "E2110")]
    [InlineData("t := Concat(s, i)", "E2010")]
    [InlineData("c := UpCase(s)", "E2010")]
    [InlineData("c := UpCase(c, c)", "E2111")]
    [InlineData("i := Copy(s, 1, 1)", "E2010")]
    [InlineData("Copy(s, 1, 1)", "E2060")]
    [InlineData("t := SetLength(s, 1)", "E2034")]
    public void Errors_OneEach(string statement, string expected) => Assert.Equal([expected], Errors(statement));

    [Theory]
    [Trait("Req", "LANG-340")]
    [InlineData("Copy('abc', 1, 2)")]
    [InlineData("Pos('b', 'abc')")]
    [InlineData("Concat('a', 'b')")]
    [InlineData("UpCase('a')")]
    public void Functions_AreNotConstant(string expression) =>
        Assert.Equal(["E2050"], Codes("program T; const K = " + expression + "; begin end."));
}
