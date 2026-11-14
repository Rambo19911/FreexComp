using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-111: <c>for x in e do s</c> over arrays, strings and sets (expressions-statements.md LANG-362).</summary>
[Trait("Phase", "2")]
public sealed class ForInBindingTests
{
    private const string Prelude = """
        program T;
        type TE = (A, B, C); TS = set of TE; TR = record x: Integer end;
        var f: array[1..3] of Integer; d: array of Byte; s: string; e: TE; es: TS; i: Integer; ch: Char; r: TR; w: Word;

        """;

    private static string[] Diagnostics(string statements) => BindDiagnostics(Prelude + "begin " + statements + " end.");

    /// <summary>LANG-362: every kind of collection binds; the variable takes element types it is assignment-compatible with.</summary>
    [Theory]
    [Trait("Req", "LANG-362")]
    [InlineData("for i in f do Write(i)")]
    [InlineData("for i in d do Break")]
    [InlineData("for w in d do Continue")]
    [InlineData("for ch in s do Write(ch)")]
    [InlineData("for ch in 'abc' do Write(ch)")]
    [InlineData("for e in es do Write(Ord(e))")]
    [InlineData("for e in [A, C] do Write(Ord(e))")]
    public void Collections_Bind(string statement) => Assert.Empty(Diagnostics(statement));

    /// <summary>LANG-362 errors: assigning the loop variable (E2053), a collection that cannot be enumerated (E2069), element types
    /// the variable does not take (E2010).</summary>
    [Theory]
    [Trait("Req", "LANG-362")]
    [InlineData("for i in f do i := 1", "E2053(4,21): Left side cannot be assigned to")]
    [InlineData("for i in r do Write(i)", "E2069(4,16): Type 'TR' cannot be enumerated with for-in")]
    [InlineData("for i in i do Write(i)", "E2069(4,16): Type 'Integer' cannot be enumerated with for-in")]
    [InlineData("for ch in f do Write(ch)", "E2010(4,17): Incompatible types: 'Integer' and 'Char'")]
    public void Errors(string statement, string expected) => Assert.Equal([expected], Diagnostics(statement));
}
