using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>UnicodeString expressions (types.md LANG-241…245): characters, conversions from characters, string-valued operands.</summary>
[Trait("Phase", "2")]
public sealed class StringBindingTests
{
    private const string Vars = "var i: Integer; q: Int64; c: Char; a: AnsiChar; s, t: string; d: Double; ";

    private static string Body(string statements)
    {
        var (result, _) = Bind("program T; " + Vars + "begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(Assert.Single(result.Bodies));
    }

    private static string[] Errors(string declarations, string statements) =>
        BindDiagnostics("program T; " + Vars + declarations + " begin " + statements + " end.");

    /// <summary>LANG-241: <c>s[i]</c> is a <c>Char</c> variable (readable and assignable), indexed by any integer type.</summary>
    [Fact]
    [Trait("Req", "LANG-241")]
    public void Index_IsACharVariable() =>
        Assert.Equal("(block (:= c:Char ([] s:string i:Integer):Char) (:= ([] s:string q:Int64):Char c:Char))", Body("c := s[i]; s[q] := c"));

    /// <summary>LANG-241: the index is an integer (E2010 at it); a second index is E2019 (a Char is not indexable).</summary>
    [Fact]
    [Trait("Req", "LANG-241")]
    public void Index_Errors() =>
        Assert.Equal(
            ["E2010(1,99): Incompatible types: 'Char' and 'Integer'", "E2010(1,110): Incompatible types: 'Double' and 'Integer'",
             "E2019(1,124): Too many indexes for 's'"],
            Errors("", "c := s[c]; c := s[d]; c := s[1, 2]"));

    /// <summary>A character of a <c>const</c> parameter cannot be assigned (it is no variable, like the parameter).</summary>
    [Fact]
    [Trait("Req", "LANG-241")]
    public void Index_OfAConstParameterIsNotAssignable() =>
        Assert.Single(BindDiagnostics("program T; procedure P(const s: string); begin s[1] := 'x' end; begin end."));

    /// <summary>LANG-243: <c>+</c> of UnicodeStrings and characters (also <c>Char + Char</c>) is a UnicodeString; constants fold.</summary>
    [Fact]
    [Trait("Req", "LANG-243")]
    public void Concatenation_IsAUnicodeString() =>
        Assert.Equal(
            "(block (:= s:string (Add:string s:string t:string)) (:= s:string (Add:string (Add:string s:string 'x':string) (conv string c:Char))) " +
            "(:= s:string (Add:string (conv string c:Char) (conv string a:AnsiChar))) (:= s:string 'abc':string))",
            Body("s := s + t; s := s + 'x' + c; s := c + a; s := 'ab' + 'c'"));

    /// <summary>LANG-244: relational operators on UnicodeStrings and characters compare as strings (a Boolean); two chars stay chars.</summary>
    [Fact]
    [Trait("Req", "LANG-244")]
    public void Comparison_OfStringsIsBoolean() =>
        Assert.Equal(
            "(block (:= f:Boolean (Less:Boolean s:string t:string)) (:= f:Boolean (Equal:Boolean s:string (conv string c:Char))) " +
            "(:= f:Boolean (GreaterOrEqual:Boolean 'a':string s:string)) (:= f:Boolean (NotEqual:Boolean c:Char 'z':Char)) (:= f:Boolean True:Boolean))",
            BodyWith("f: Boolean;", "f := s < t; f := s = c; f := 'a' >= s; f := c <> 'z'; f := 'ab' < 'abc'"));

    /// <summary>LANG-243/244: other operand types are not concatenated or compared with strings (one error each).</summary>
    [Fact]
    [Trait("Req", "LANG-243")]
    public void StringOperators_RejectOtherOperands() =>
        Assert.Equal(2, Errors("f: Boolean;", "s := s + i; f := s = d").Length);

    private static string BodyWith(string declarations, string statements)
    {
        var (result, _) = Bind("program T; " + Vars + declarations + " begin " + statements + " end.");
        Assert.Empty(Format(result.Diagnostics));
        return BoundDump.Of(Assert.Single(result.Bodies));
    }

    /// <summary>LANG-245: a character becomes a 1-char string (an implicit conversion; a constant one folds to a string constant).</summary>
    [Fact]
    [Trait("Req", "LANG-245")]
    public void Char_ConvertsToString() =>
        Assert.Equal("(block (:= s:string (conv string c:Char)) (:= s:string (conv string a:AnsiChar)) (:= s:string 'x':string))", Body("s := c; s := a; s := 'x'"));
}
