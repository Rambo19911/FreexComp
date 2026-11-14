using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>
/// C-063: the AnsiString family, short strings, <c>PChar</c> and char arrays (types.md LANG-200, LANG-243…247, LANG-290):
/// conversions with W1020, string kinds of <c>+</c> and comparisons, indexing, <c>string[N]</c> types and typecasts.
/// </summary>
[Trait("Phase", "2")]
public sealed class ByteStringBindingTests
{
    private const string Vars =
        "var s: string; a, b: AnsiString; u: UTF8String; h: ShortString; n: string[10]; c: Char; ac: AnsiChar; p: PChar; pa: PAnsiChar; " +
        "buf: array[0..3] of Char; i: Integer; f: Boolean; ";

    private static string Body(string statements)
    {
        var (result, _) = Bind("program T; " + Vars + "begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(Assert.Single(result.Bodies));
    }

    private static string[] Diagnostics(string statements) => BindDiagnostics("program T; " + Vars + "begin " + statements + " end.");

    /// <summary>LANG-245: every string kind, characters, <c>PChar</c>/<c>PAnsiChar</c> and char arrays convert implicitly.</summary>
    [Fact]
    [Trait("Req", "LANG-245")]
    public void Conversions_AreImplicit() =>
        Assert.Equal(
            "(block (:= s:string (conv string a:AnsiString)) (:= s:string (conv string h:ShortString)) (:= s:string (conv string p:PChar)) " +
            "(:= s:string (conv string pa:PAnsiChar)) (:= s:string (conv string buf:array[0..3] of Char)) (:= a:AnsiString (conv AnsiString pa:PAnsiChar)) " +
            "(:= a:AnsiString (conv AnsiString u:UTF8String)) (:= a:AnsiString (conv AnsiString ac:AnsiChar)) (:= n:string[10] (conv string[10] h:ShortString)))",
            Body("s := a; s := h; s := p; s := pa; s := buf; a := pa; a := u; a := ac; n := h"));

    /// <summary>LANG-245: UnicodeString → byte strings warns (W1020 at the value), constants and explicit casts do not.</summary>
    [Fact]
    [Trait("Req", "LANG-245")]
    public void Narrowing_WarnsW1020() =>
        Assert.Equal(
            ["W1020(1,200): Implicit string conversion with potential data loss from 'string' to 'AnsiString'",
             "W1020(1,208): Implicit string conversion with potential data loss from 'string' to 'string[10]'"],
            Diagnostics("a := s; n := s; a := 'lit'; n := 'lit'; a := AnsiString(s); h := ShortString(s)"));

    /// <summary>LANG-245: <c>PChar</c> → byte strings, string → <c>PChar</c> implicitly and <c>PAnsiChar</c> → short strings are E2010.</summary>
    [Theory]
    [Trait("Req", "LANG-245")]
    [InlineData("h := p", "E2010(1,200): Incompatible types: 'PChar' and 'ShortString'")]
    [InlineData("p := s", "E2010(1,200): Incompatible types: 'string' and 'PChar'")]
    [InlineData("a := buf", "E2010(1,200): Incompatible types: 'array[0..3] of Char' and 'AnsiString'")]
    public void Conversions_Errors(string statement, string expected) => Assert.Equal([expected], Diagnostics(statement));

    /// <summary>LANG-243/244: one byte-string kind stays that kind (a constant adapts), mixed kinds are UnicodeStrings.</summary>
    [Fact]
    [Trait("Req", "LANG-243")]
    [Trait("Req", "LANG-244")]
    public void Operators_TakeTheCommonKind() =>
        Assert.Equal(
            "(block (:= a:AnsiString (Add:AnsiString a:AnsiString 'x':AnsiString)) (:= s:string (Add:string (conv string a:AnsiString) s:string)) " +
            "(:= h:ShortString (conv ShortString (Add:string (conv string h:ShortString) (conv string n:string[10])))) " +
            "(:= f:Boolean (Less:Boolean a:AnsiString b:AnsiString)) (:= f:Boolean (Equal:Boolean n:string[10] 'q':string[10])) " +
            "(:= f:Boolean (Equal:Boolean (conv string u:UTF8String) s:string)))",
            Body("a := a + 'x'; s := a + s; h := h + n; f := a < b; f := n = 'q'; f := u = s"));

    /// <summary>LANG-241 for byte strings: <c>a[i]</c> and <c>h[i]</c> are <c>AnsiChar</c> variables.</summary>
    [Fact]
    [Trait("Req", "LANG-241")]
    public void Index_IsAnAnsiCharVariable() =>
        Assert.Equal("(block (:= ac:AnsiChar ([] a:AnsiString i:Integer):AnsiChar) (:= ([] h:ShortString 0:Integer):AnsiChar ac:AnsiChar))",
            Body("ac := a[i]; h[0] := ac"));

    /// <summary>LANG-290: string-kind casts convert, <c>PChar(s)</c>/<c>PAnsiChar(a)</c>/<c>Pointer(s)</c> point to the data.</summary>
    [Fact]
    [Trait("Req", "LANG-290")]
    public void Typecasts_ConvertOrPointToTheData() =>
        Assert.Equal(
            "(block (:= a:AnsiString (cast AnsiString s:string)) (:= s:string (cast string p:PChar)) (:= s:string (cast string a:AnsiString)) " +
            "(:= p:PChar (cast PChar s:string)) (:= pa:PAnsiChar (cast PAnsiChar a:AnsiString)) (:= p:PChar 'lit':PChar))",
            Body("a := AnsiString(s); s := string(p); s := string(a); p := PChar(s); pa := PAnsiChar(a); p := 'lit'"));

    /// <summary>LANG-290: <c>PAnsiChar</c> of a UnicodeString variable is a managed variable typecast (E2041); <c>PChar</c> of a short string value E2040.</summary>
    [Theory]
    [Trait("Req", "LANG-290")]
    [InlineData("pa := PAnsiChar(s)", "E2041(1,201): Variable typecast not allowed for managed types or types of different size")]
    [InlineData("p := PChar(h + 'x')", "E2040(1,200): Invalid typecast from 'ShortString' to 'PChar'")]
    public void Typecasts_Errors(string statement, string expected) => Assert.Equal([expected], Diagnostics(statement));

    /// <summary>LANG-200: <c>string[N]</c> has N+1 bytes; N outside 1..255 is E2012; <c>Length</c> of byte-string constants counts bytes.</summary>
    [Fact]
    [Trait("Req", "LANG-200")]
    public void ShortStringTypes_SizeAndRange()
    {
        Assert.Equal("(block (:= i:Integer 11:Integer) (:= i:Integer 256:Integer) (:= i:Integer 255:Integer) (:= i:Integer 3:Integer))",
            Body("i := SizeOf(n); i := SizeOf(h); i := High(ShortString); i := Length(AnsiString('äb'))"));
        Assert.Equal(["E2012(1,15): Constant value 0 is out of range for type '1..255'"], BindDiagnostics("var X: string[0]; begin end."));
    }

    /// <summary>
    /// C-067: <c>Delete</c>, the <c>Insert</c> target and the <c>Str</c> target accept short strings and the AnsiString family
    /// (edited through a UnicodeString, UTF-16 indexes); <c>SetLength</c> accepts a short string (sets its length byte). No E2900.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-246")]
    public void StringEdits_OfByteStrings() =>
        Assert.Equal(
            "(block (Delete n:string[10] 1:Integer 2:Integer) (Delete a:AnsiString i:Integer 1:Integer) (Insert 'x':string h:ShortString 1:Integer) " +
            "(Insert s:string u:UTF8String i:Integer) (Str i:Integer n:string[10]) (Str i:Integer a:AnsiString 4:Integer) (SetLength n:string[10] 3:Integer) " +
            "(SetLength h:ShortString i:Integer))",
            Body("Delete(n, 1, 2); Delete(a, i, 1); Insert('x', h, 1); Insert(s, u, i); Str(i, n); Str(i:4, a); SetLength(n, 3); SetLength(h, i)"));
}
