using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-168: <c>AtomicIncrement</c>, <c>AtomicDecrement</c>, <c>AtomicExchange</c>, <c>AtomicCmpExchange</c> (intrinsics.md, ABI-172) on
/// 32-/64-bit integer and pointer variables; usable as values and as statements; never constant (LANG-340).
/// </summary>
[Trait("Phase", "2")]
public sealed class AtomicBindingTests
{
    private const string Vars = "type PInt = ^Integer; var i: Integer; u: Cardinal; q: Int64; b: Byte; w: Word; d: Double; p: Pointer; t: PInt; ";

    private static string Body(string statements)
    {
        var (result, _) = Bind("program T; " + Vars + "begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(Assert.Single(result.Bodies));
    }

    /// <summary>The codes of the diagnostics only (positions are covered by the other intrinsic suites).</summary>
    private static string[] Codes(string text) => [.. BindDiagnostics(text).Select(d => d[..d.IndexOf('(')])];

    private static string[] Errors(string statements) => Codes("program T; " + Vars + "begin " + statements + " end.");

    [Fact]
    [Trait("Req", "ABI-172")]
    public void Atomics_TypeAsTheirTarget() =>
        Assert.Equal(
            "(block (:= i:Integer (AtomicIncrement:Integer i:Integer)) (:= q:Int64 (AtomicDecrement:Int64 q:Int64 i:Integer)) " +
            "(:= u:Cardinal (AtomicExchange:Cardinal u:Cardinal 3:Cardinal)) (:= p:Pointer (AtomicCmpExchange:Pointer p:Pointer nil:Pointer p:Pointer)) " +
            "(:= t:PInt (AtomicIncrement:PInt t:PInt 2:Integer)))",
            Body("i := AtomicIncrement(i); q := AtomicDecrement(q, i); u := AtomicExchange(u, 3); p := AtomicCmpExchange(p, nil, p); " +
                 "t := AtomicIncrement(t, 2)"));

    [Fact]
    [Trait("Req", "ABI-172")]
    public void Atomics_AreAllowedAsStatements() =>
        Assert.Equal(
            "(block (expr (AtomicIncrement:Integer i:Integer)) (expr (AtomicExchange:Int64 q:Int64 5:Int64)))",
            Body("AtomicIncrement(i); AtomicExchange(q, 5)"));

    [Theory]
    [Trait("Req", "LANG-900")]
    [InlineData("i := AtomicIncrement(b)", "E2010")]
    [InlineData("i := AtomicIncrement(w)", "E2010")]
    [InlineData("d := AtomicExchange(d, 1)", "E2010")]
    [InlineData("i := AtomicIncrement(i, 1.5)", "E2010")]
    [InlineData("i := AtomicExchange(i, 'x')", "E2010")]
    [InlineData("i := AtomicIncrement(5)", "E2112")]
    [InlineData("i := AtomicIncrement(i + 1)", "E2113")]
    [InlineData("i := AtomicIncrement()", "E2110")]
    [InlineData("i := AtomicExchange(i)", "E2110")]
    [InlineData("i := AtomicCmpExchange(i, 1)", "E2110")]
    [InlineData("i := AtomicIncrement(i, 1, 2)", "E2111")]
    [InlineData("i := AtomicCmpExchange(i, 1, 2, 3)", "E2111")]
    [InlineData("i := AtomicIncrement", "E2110")]
    public void Atomics_Errors(string statement, string expected) => Assert.Equal([expected], Errors(statement));

    [Fact]
    [Trait("Req", "LANG-340")]
    public void Atomics_AreNotConstant() =>
        Assert.Equal(["E2050"], Codes("program T; var i: Integer; const K = AtomicExchange(i, 1); begin end."));

    [Fact]
    [Trait("Req", "LANG-900")]
    public void Atomics_ConstParameterIsE2112() =>
        Assert.Equal(["E2112"], Codes("program T; procedure P(const a: Integer); begin AtomicIncrement(a) end; begin end."));
}
