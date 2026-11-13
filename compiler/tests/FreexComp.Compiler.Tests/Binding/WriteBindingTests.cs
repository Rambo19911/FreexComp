using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary><c>Write</c>/<c>Writeln</c> with integer, Boolean, char and string-constant arguments and <c>:w</c> (intrinsics.md LANG-910…922).</summary>
[Trait("Phase", "2")]
public sealed class WriteBindingTests
{
    private const string Vars =
        "var i: Integer; b: Byte; c: Char; a: AnsiChar; f: Boolean; lb: LongBool; q: Int64; u: UInt64; d: Double; s: string; t: Text; p: Pointer; pc: PChar; ";

    // "program T; " + Vars + "begin " puts the first statement at this column
    private static readonly int First = "program T; ".Length + Vars.Length + "begin ".Length + 1;

    private static string Body(string statements)
    {
        var (result, _) = Bind("program T; " + Vars + "begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(Assert.Single(result.Bodies));
    }

    /// <summary>The diagnostics of one statement; <c>^</c> in <paramref name="statement"/> marks the expected column and is removed.</summary>
    private static (string[] Actual, int Column) Errors(string statement)
    {
        var column = First + statement.IndexOf('^', StringComparison.Ordinal);
        return (BindDiagnostics("program T; " + Vars + "begin " + statement.Replace("^", "", StringComparison.Ordinal) + " end."), column);
    }

    [Theory]
    [Trait("Req", "LANG-910")]
    [InlineData("Writeln", "(Writeln)")]
    [InlineData("Write(i, ' ', f, c)", "(Write i:Integer ' ':Char f:Boolean c:Char)")]
    [InlineData("Writeln(lb, a, q, u, b)", "(Writeln lb:LongBool a:AnsiChar q:Int64 u:UInt64 b:Byte)")]
    [InlineData("Writeln('ab', 1 + 2, True, 'x')", "(Writeln 'ab':string 3:Integer True:Boolean 'x':Char)")]
    [InlineData("Writeln(t, i)", "(Writeln (file t:Text) i:Integer)")]
    [InlineData("Write(t)", "(Write (file t:Text))")]
    public void WriteArguments(string statement, string expected) => Assert.Equal("(block " + expected + ")", Body(statement));

    [Theory]
    [Trait("Req", "LANG-915")]
    [InlineData("Writeln('ab':5, i:b)", "(Writeln (: 'ab':string 5:Integer) (: i:Integer (conv Integer b:Byte)))")]
    [InlineData("Write(t, True:6, -7:1)", "(Write (file t:Text) (: True:Boolean 6:Integer) (: -7:Integer 1:Integer))")]
    [InlineData("Write(c:i - 1)", "(Write (: c:Char (Subtract:Integer i:Integer 1:Integer)))")]
    public void WidthSpecifiers_AreIntegers(string statement, string expected) => Assert.Equal("(block " + expected + ")", Body(statement));

    [Theory]
    [Trait("Req", "LANG-911")]
    [InlineData("Write(i:2^:1)", "E2912: Decimals specifier ':d' is only allowed for real values")]
    [InlineData("Write(^p)", "E2913: Type 'Pointer' is not allowed in Write/Writeln")]
    [InlineData("Write(i, ^t)", "E2913: Type 'Text' is not allowed in Write/Writeln")]
    [InlineData("Write(^t:3)", "E2913: Type 'Text' is not allowed in Write/Writeln")]
    [InlineData("Write(i:^'x')", "E2010: Incompatible types: 'Char' and 'Integer'")]
    [InlineData("Write(i:^1.5)", "E2010: Incompatible types: 'Double' and 'Integer'")]
    [InlineData("Write(^d)", "E2900: Feature not supported: Write of a real value")]
    [InlineData("Write(^d:0:2)", "E2900: Feature not supported: Write of a real value")]
    [InlineData("Write(^s)", "E2900: Feature not supported: Write of a string variable")]
    [InlineData("Write(^pc)", "E2900: Feature not supported: Write of a PChar")]
    [InlineData("Write(^x)", "E2003: Undeclared identifier 'x'")]
    [InlineData("i := ^Writeln", "E2034: Expression has no value (procedure call used as a value)")]
    public void Errors_AreReportedOnce(string statement, string expected)
    {
        var (actual, column) = Errors(statement);
        var code = expected[..5];
        Assert.Equal([$"{code}(1,{column}){expected[5..]}"], actual);
    }

    [Fact]
    [Trait("Req", "LANG-910")]
    public void Write_CanBeHiddenByAUserRoutine() =>
        Assert.Equal("(block (call Write 1:Integer))",
            BoundDump.Of(Assert.Single(Bind("program T; procedure Write(x: Integer); begin end; begin Write(1) end.").Result.Bodies)));
}
