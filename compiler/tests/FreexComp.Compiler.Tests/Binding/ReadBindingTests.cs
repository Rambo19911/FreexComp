using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary><c>Read</c>/<c>Readln</c>, <c>Eof</c>/<c>Eoln</c> (task C-066: intrinsics.md LANG-940…946).</summary>
[Trait("Phase", "2")]
public sealed class ReadBindingTests
{
    private const string Vars =
        "var i: Integer; b: Byte; c: Char; a: AnsiChar; f: Boolean; u: UInt64; d: Double; g: Single; s: string; t: Text; " +
        "sa: AnsiString; ss: ShortString; p: Pointer; r: record x: Integer end; k: array[1..2] of Integer; ";

    private static readonly int First = "program T; ".Length + Vars.Length + "begin ".Length + 1;

    private static string Body(string statements)
    {
        var (result, _) = Bind("program T; " + Vars + "begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(Assert.Single(result.Bodies));
    }

    private static (string[] Actual, int Column) Errors(string statement)
    {
        var column = First + statement.IndexOf('^', StringComparison.Ordinal);
        return (BindDiagnostics("program T; " + Vars + "begin " + statement.Replace("^", "", StringComparison.Ordinal) + " end."), column);
    }

    /// <summary>LANG-940: the file first when given, then the variables (integers, reals, Char, string), unconverted.</summary>
    [Theory]
    [Trait("Req", "LANG-940")]
    [InlineData("Readln", "(Readln)")]
    [InlineData("Read(i, b, u)", "(Read i:Integer b:Byte u:UInt64)")]
    [InlineData("Readln(t, d, g, c, s)", "(Readln t:Text d:Double g:Single c:Char s:string)")]
    [InlineData("Read(t)", "(Read t:Text)")]
    [InlineData("Read(r.x, k[i], s[2])", "(Read (. r:record x: Integer end x):Integer ([] k:array[1..2] of Integer i:Integer):Integer ([] s:string 2:Integer):Char)")]
    public void ReadArguments(string statement, string expected) => Assert.Equal("(block " + expected + ")", Body(statement));

    /// <summary>LANG-946: <c>Eof</c>/<c>Eoln</c> without an argument (Input) or with a <c>Text</c> variable; Boolean.</summary>
    [Theory]
    [Trait("Req", "LANG-946")]
    [InlineData("f := Eof", "(:= f:Boolean (Eof:Boolean))")]
    [InlineData("f := Eoln()", "(:= f:Boolean (Eoln:Boolean))")]
    [InlineData("f := Eof(t) or not Eoln(t)", "(:= f:Boolean (ConditionalOr:Boolean (Eof:Boolean t:Text) (Not:Boolean (Eoln:Boolean t:Text))))")]
    public void TextFunctions(string statement, string expected) => Assert.Equal("(block " + expected + ")", Body(statement));

    [Theory]
    [Trait("Req", "LANG-940")]
    [InlineData("Read(^f)", "E2914: Type 'Boolean' is not allowed in Read/Readln")]
    [InlineData("Read(i, ^a)", "E2914: Type 'AnsiChar' is not allowed in Read/Readln")]
    [InlineData("Readln(^sa)", "E2914: Type 'AnsiString' is not allowed in Read/Readln")]
    [InlineData("Readln(^ss)", "E2914: Type 'ShortString' is not allowed in Read/Readln")]
    [InlineData("Read(^p)", "E2914: Type 'Pointer' is not allowed in Read/Readln")]
    [InlineData("Read(i, ^t)", "E2914: Type 'Text' is not allowed in Read/Readln")]
    [InlineData("Read(^r)", "E2914: Type 'record x: Integer end' is not allowed in Read/Readln")]
    [InlineData("Read(i^:2)", "E2915: Width specifiers are only allowed in Write, Writeln and Str")]
    [InlineData("Read(^1)", "E2112: Constant object cannot be passed as var parameter")]
    [InlineData("Read(^i + 1)", "E2113: Variable required")]
    [InlineData("f := Eof(^i)", "E2010: Incompatible types: 'Integer' and 'Text'")]
    [InlineData("f := Eoln(t, ^t)", "E2111: Too many actual parameters for 'Eoln'")]
    public void Errors_AreReportedOnce(string statement, string expected)
    {
        var (actual, column) = Errors(statement);
        var code = expected[..5];
        Assert.Equal([$"{code}(1,{column}){expected[5..]}"], actual);
    }
}
