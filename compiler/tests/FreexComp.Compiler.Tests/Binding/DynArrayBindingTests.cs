using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-064: dynamic arrays (types.md LANG-206, LANG-257; intrinsics.md SetLength/Copy/Length/High/Low).</summary>
[Trait("Phase", "2")]
public sealed class DynArrayBindingTests
{
    private const string Types = "type TInts = array of Integer; TGrid = array of TInts; ";

    private const string Vars = "var a, b: TInts; g: TGrid; c: array of Integer; s: array of string; i: Integer; f: Boolean; ";

    private static string Body(string statements)
    {
        var (result, _) = Bind("program T; " + Types + Vars + "begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(Assert.Single(result.Bodies));
    }

    private static string[] Diagnostics(string statements) => BindDiagnostics("program T; " + Types + Vars + "begin " + statements + " end.");

    /// <summary>LANG-257: elements are variables (0-based, any integer index), <c>g[i, j]</c> = <c>g[i][j]</c>; assignment of the same type and <c>nil</c>.</summary>
    [Fact]
    [Trait("Req", "LANG-257")]
    public void Elements_AreVariables() =>
        Assert.Equal(
            "(block (:= ([] a:TInts i:Integer):Integer 1:Integer) (:= i:Integer ([] ([] g:TGrid 1:Integer):TInts 2:Integer):Integer) " +
            "(:= ([] s:array of string 0:Integer):string ([] s:array of string 0:Integer):string) (:= b:TInts a:TInts) (:= a:TInts nil:TInts))",
            Body("a[i] := 1; i := g[1, 2]; s[0] := s[0]; b := a; a := nil"));

    /// <summary>Length/High at run time, Low constant 0; SetLength with one length per nesting level; Copy with 1–3 arguments.</summary>
    [Fact]
    [Trait("Req", "LANG-257")]
    public void Intrinsics_LengthSetLengthCopy() =>
        Assert.Equal(
            "(block (:= i:Integer (Length:Integer a:TInts)) (:= i:Integer (High:Integer g:TGrid)) (:= i:Integer 0:Integer) " +
            "(SetLength a:TInts 3:Integer) (SetLength g:TGrid 2:Integer i:Integer) (:= b:TInts (Copy:TInts a:TInts)) " +
            "(:= b:TInts (Copy:TInts a:TInts (conv NativeInt i:Integer))) (:= b:TInts (Copy:TInts a:TInts 1:NativeInt 2:NativeInt)))",
            Body("i := Length(a); i := High(g); i := Low(a); SetLength(a, 3); SetLength(g, 2, i); b := Copy(a); b := Copy(a, i); b := Copy(a, 1, 2)"));

    /// <summary>LANG-257: <c>=</c>/<c>&lt;&gt;</c> compare references of one type or with <c>nil</c>.</summary>
    [Fact]
    [Trait("Req", "LANG-257")]
    public void Comparison_OfReferences() =>
        Assert.Equal("(block (:= f:Boolean (Equal:Boolean a:TInts b:TInts)) (:= f:Boolean (NotEqual:Boolean a:TInts nil:nil)) (:= f:Boolean (Equal:Boolean nil:nil g:TGrid)))",
            Body("f := a = b; f := a <> nil; f := nil = g"));

    /// <summary>LANG-206 (distinct anonymous types), LANG-257 errors, intrinsics.md argument counts, LANG-430 (open array parameters, M2.5).</summary>
    [Theory]
    [Trait("Req", "LANG-206")]
    [Trait("Req", "LANG-257")]
    [InlineData("a := c", "E2010(1,170): Incompatible types: 'array of Integer' and 'TInts'")]
    [InlineData("f := a < b", "E2011(1,172): Operator '<' not applicable to operand type(s) 'TInts' and 'TInts'")]
    [InlineData("f := a = c", "E2011(1,172): Operator '=' not applicable to operand type(s) 'TInts' and 'array of Integer'")]
    [InlineData("i := a[f]", "E2010(1,172): Incompatible types: 'Boolean' and 'Integer'")]
    [InlineData("SetLength(a, 1, 2)", "E2111(1,181): Too many actual parameters for 'SetLength'")]
    [InlineData("SetLength(g, 1, 2, 3)", "E2111(1,184): Too many actual parameters for 'SetLength'")]
    [InlineData("i := Length(TInts)", "E2911(1,170): 'Length' cannot be applied to this type")]
    public void Errors(string statement, string expected) => Assert.Equal([expected], Diagnostics(statement));
}
