using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>LANG-133 / LANG-290 (C-369): explicit casts of string literals and string constants to the character pointers.</summary>
[Trait("Phase", "8")]
public sealed class StringLiteralCastTests
{
    /// <summary>
    /// LANG-133 / LANG-290 (C-369): <c>PAnsiChar('literal')</c> and <c>PChar('literal')</c> of a multi-character literal (also an
    /// empty literal, a folded concatenation and a string constant; in a statement and in a typed constant) are the pointer to the static zero-terminated data — ANSI
    /// data for <c>PAnsiChar</c>, as the implicit conversion gives — never E2040.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-290")]
    public void StringLiteralToPAnsiCharAndPChar()
    {
        var (result, _) = Bind(
            "program T; const S = 'hello'; CA: PAnsiChar = PAnsiChar('abc'); CW: PChar = PChar('abc'); CK: PAnsiChar = PAnsiChar(S); var p: PChar; pa: PAnsiChar; pw: PWideChar; " +
            "procedure TakeA(x: PAnsiChar); begin end; procedure TakeW(x: PChar); begin end; " +
            "begin pa := PAnsiChar('abc'); p := PChar('abc'); pw := PWideChar('abc'); pa := PAnsiChar(S); p := PChar(S); " +
            "pa := PAnsiChar(''); pa := PAnsiChar('ab' + 'cd'); TakeA(PAnsiChar('xyz')); TakeW(PChar('xyz')) end.");
        Assert.Empty(Format(result.Diagnostics));
        var dump = BoundDump.Of(result.Bodies[^1]);
        Assert.StartsWith(
            "(block (:= pa:PAnsiChar 'abc':PAnsiChar) (:= p:PChar 'abc':PChar) (:= pw:PChar 'abc':PChar) (:= pa:PAnsiChar 'hello':PAnsiChar) " +
            "(:= p:PChar 'hello':PChar) (:= pa:PAnsiChar '':PAnsiChar) (:= pa:PAnsiChar 'abcd':PAnsiChar) ",
            dump, StringComparison.Ordinal);
    }
}
