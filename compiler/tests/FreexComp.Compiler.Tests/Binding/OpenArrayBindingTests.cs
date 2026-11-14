using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-106: open array parameters (routines.md LANG-430…432), array constructors as arguments (LANG-259), <c>Slice</c>.</summary>
[Trait("Phase", "2")]
public sealed class OpenArrayBindingTests
{
    private const string Routines = """
        program T;
        type TFive = array[1..5] of Integer; TInts = array of Integer; TBytes = array of Byte;
        procedure C(const a: array of Integer); begin end;
        procedure V(var a: array of Integer); begin end;
        procedure W(a: array of Integer); begin end;
        procedure S(const a: array of string); begin end;
        procedure H(const a: array of Char); begin end;
        var f: TFive; d: TInts; b: TBytes; i: Integer; x: string;

        """;

    private static string Body(string statements)
    {
        var (result, _) = Bind(Routines + "begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(result.Bodies[^1]);
    }

    private static string[] Diagnostics(string statements) => BindDiagnostics(Routines + "begin " + statements + " end.");

    /// <summary>LANG-431: inside the callee the parameter is a 0-based array: elements, <c>Length</c>, <c>High</c> at run time, <c>Low</c> = 0.</summary>
    [Fact]
    [Trait("Req", "LANG-431")]
    public void InsideTheCallee_ElementsLengthHighLow()
    {
        var (result, _) = Bind("""
            program T;
            procedure P(const a: array of Integer); var i: Integer;
            begin i := a[i]; i := Length(a); i := High(a); i := Low(a) end;
            begin end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal(
            "(block (:= i:Integer ([] a:array of Integer i:Integer):Integer) (:= i:Integer (Length:Integer a:array of Integer)) " +
            "(:= i:Integer (High:Integer a:array of Integer)) (:= i:Integer 0:Integer))",
            BoundDump.Of(result.Routines.Single(r => r.Routine.Name == "P").Body));
    }

    /// <summary>LANG-430: static, dynamic and open arrays with the identical element type; constructors for value/const parameters.</summary>
    [Fact]
    [Trait("Req", "LANG-430")]
    public void Arguments_ArraysAndConstructors() =>
        Assert.Equal(
            "(block (call C (conv array of Integer f:TFive)) (call C (conv array of Integer d:TInts)) (call V f:TFive) (call V d:TInts) " +
            "(call W (array array of Integer 1:Integer i:Integer)) (call C (array array of Integer)) (call S (array array of string 'a':string x:string)))",
            Body("C(f); C(d); V(f); V(d); W([1, i]); C([]); S(['a', x])"));

    /// <summary>LANG-431: an open array is passed on, also as <c>Slice(a, n)</c> (static and dynamic arrays as well).</summary>
    [Fact]
    [Trait("Req", "LANG-431")]
    public void Slice_AndPassingOn()
    {
        var (result, _) = Bind("""
            program T;
            procedure C(const a: array of Integer); begin end;
            procedure P(const a: array of Integer); var f: array[0..3] of Integer;
            begin C(a); C(Slice(a, 2)); C(Slice(f, 1)) end;
            begin end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal(
            "(block (call C a:array of Integer) (call C (Slice:array of Integer a:array of Integer 2:Integer)) " +
            "(call C (Slice:array of Integer f:array[0..3] of Integer 1:Integer)))",
            BoundDump.Of(result.Routines.Single(r => r.Routine.Name == "P").Body));
    }

    /// <summary>LANG-430/431 errors: single variable, other element type, string for <c>array of Char</c>, constructors for <c>var</c>, SizeOf (E2116).</summary>
    [Theory]
    [Trait("Req", "LANG-430")]
    [Trait("Req", "LANG-431")]
    [InlineData("C(i)", "E2010(9,9): Incompatible types: 'Integer' and 'array of Integer'")]
    [InlineData("C(b)", "E2010(9,9): Incompatible types: 'TBytes' and 'array of Integer'")]
    [InlineData("V(b)", "E2010(9,9): Incompatible types: 'TBytes' and 'array of Integer'")]
    [InlineData("H('ab')", "E2010(9,9): Incompatible types: 'string' and 'array of Char'")]
    [InlineData("V([1])", "E2112(9,9): Constant object cannot be passed as var parameter")]
    [InlineData("C([1, 'x'])", "E2010(9,13): Incompatible types: 'Char' and 'Integer'")]
    [InlineData("C(Slice(i, 1))", "E2017(9,15): Array type required")]
    [InlineData("i := Slice(f, 1)", "E2010(9,12): Incompatible types: 'array of Integer' and 'Integer'")]
    public void Arguments_Errors(string statement, string expected) => Assert.Equal([expected], Diagnostics(statement));

    /// <summary>LANG-431: no <c>SizeOf</c> (E2116), no assignment as a whole, no <c>SetLength</c>; <c>const</c> elements are read-only.</summary>
    [Theory]
    [Trait("Req", "LANG-431")]
    [InlineData("i := SizeOf(a)", "E2116(2,92): SizeOf cannot be applied to an open array")]
    [InlineData("a := b", "E2010(2,92): Incompatible types: 'array of Integer' and 'array of Integer'")]
    [InlineData("SetLength(a, 2)", "E2010(2,97): Incompatible types: 'array of Integer' and 'string'")]
    [InlineData("c[0] := 1", "E2053(2,87): Left side cannot be assigned to")]
    public void InsideTheCallee_Errors(string statement, string expected) =>
        Assert.Equal([expected], BindDiagnostics("program T;\nprocedure P(a, b: array of Integer; const c: array of Integer); var i: Integer; begin " + statement + " end; begin end."));

    /// <summary>C-198 (d): <c>Slice(a, n)</c> of a static array with a constant <c>n</c> outside <c>0..Length(a)</c> is E2032 at <c>n</c>.</summary>
    [Theory]
    [Trait("Req", "LANG-431")]
    [InlineData("C(Slice(f, 6))", "E2032(9,18): Constant index 6 is out of bounds (0..5)")]
    [InlineData("C(Slice(f, -1))", "E2032(9,18): Constant index -1 is out of bounds (0..5)")]
    [InlineData("C(Slice(f, 5))", null)]
    [InlineData("C(Slice(d, 6))", null)]
    public void SliceConstantTooLong(string statement, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], Diagnostics(statement));

    /// <summary>C-216 (LANG-442): an array constructor is never applicable to a non-array parameter, so a mixed overload set resolves.</summary>
    [Fact]
    [Trait("Req", "LANG-442")]
    public void ConstructorWithMixedOverloads()
    {
        var (result, _) = Bind("""
            program T;
            procedure P(X: Integer); overload; begin end;
            procedure P(const A: array of string); overload; begin end;
            begin P(['r', 's']); P(7) end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        var calls = result.Bodies[^1].Statements.OfType<FreexComp.Compiler.Binding.BoundCallStatement>().Select(c => c.Routine.Parameters[0].Type.Name).ToList();
        Assert.Equal(["array of string", "Integer"], calls);
    }
}