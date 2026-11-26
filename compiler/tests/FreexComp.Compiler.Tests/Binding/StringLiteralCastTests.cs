using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Lowering;
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

    /// <summary>
    /// LANG-245 / ABI-027 (C-371): a literal with characters outside 7-bit ASCII converted to <c>PAnsiChar</c> — implicitly, by cast
    /// and in a typed constant, also a one-character literal — points to the same bytes an <c>AnsiString</c> conversion gives: the
    /// product's ANSI code page is UTF-8 (65001), so it shares the one AnsiString-family literal (code page 65001) of that text with
    /// <c>a := '…'</c>; an unmappable unit (a lone surrogate) becomes U+FFFD as in <c>_AStrFromUStr</c>. LANG-262 (byte value of a
    /// character literal) is the <c>AnsiChar</c> rule and does not apply to the pointer.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-245")]
    [Trait("Req", "ABI-027")]
    public void NonAnsiLiteralToPAnsiChar_UsesTheAnsiConversion()
    {
        var (result, _) = Bind(
            "program T; const CA: PAnsiChar = PAnsiChar('é€'); var pa: PAnsiChar; a: AnsiString; " +
            "begin pa := 'é€'; pa := PAnsiChar('é€'); a := 'é€'; pa := 'é'; pa := PAnsiChar('é'); a := 'é'; " +
            "pa := PAnsiChar(#$D800'x'); a := #$D800'x' end.");
        Assert.Empty(Format(result.Diagnostics));
        var writer = new LlvmTextWriter();
        Assert.Null(IrEmitter.EmitUnit(Lowerer.Lower(result), writer));
        var ir = writer.GetText();

        // one literal per text, each used by the PAnsiChar conversions and by the AnsiString assignment: the same bytes
        string[] literals = [.. ir.Split('\n').Where(l => l.StartsWith("@.str.", StringComparison.Ordinal))];
        string[] bytes =
        [
            "[6 x i8] [i8 -61, i8 -87, i8 -30, i8 -126, i8 -84, i8 0]",     // U+00E9 U+20AC: C3 A9 E2 82 AC
            "[3 x i8] [i8 -61, i8 -87, i8 0]",                              // U+00E9: C3 A9 (not the AnsiChar byte E9 of LANG-262)
            "[5 x i8] [i8 -17, i8 -65, i8 -67, i8 120, i8 0]",              // lone surrogate: U+FFFD (EF BF BD), then 'x'
        ];
        Assert.Equal(bytes.Length, literals.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            Assert.Contains("i16 -535, i16 1, i32 0, " + bytes[i] + " }", literals[i], StringComparison.Ordinal);    // code page 65001
        }

        Assert.Contains("@\"T.CA\" = internal constant ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [6 x i8] }, ptr @.str.0, i32 0, i32 5)", ir, StringComparison.Ordinal);
        Assert.Contains("call void @\"System._AStrAsg\"(ptr @\"T.a\", ptr getelementptr inbounds ({ i32, i32, i16, i16, i32, [5 x i8] }, ptr @.str.2, i32 0, i32 5))", ir, StringComparison.Ordinal);
    }
}
