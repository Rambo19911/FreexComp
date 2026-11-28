// The diagnostics of an in-memory compilation are read synchronously; the optional token is left out on purpose.
#pragma warning disable xUnit1051

namespace FreexComp.Compiler.Tests.Binding;

// LANG-997, LANG-998 (a), (b): literals and constants as receivers (phase8/helpers-completion.md §15.7; card F10-029), C-444.
public sealed partial class HelperBindingTests
{
    /// <summary>
    /// One helper per receiver type, each with a function <c>Who</c> (the marker names the helper that binds); <c>THStr</c> also
    /// has <c>Len</c>, <c>Grow</c> (mutating) and <c>Join</c> (static); <c>THChar</c> (left out by <c>$NOCHAR$</c>) has
    /// <c>Who</c> and <c>IsDigit</c> only. <c>$S$</c> is the statement under test.
    /// </summary>
    private const string LiteralProgram = """
        program P;
        type
          TColor = (Red, Green, Blue);
          TPoint = record X, Y: Integer; end;
          THInt = record helper for Integer
          const
            Ten = 10;
          public
            function Who: string;
            function Next: Integer;
          end;
          THI64 = record helper for Int64 function Who: string; end;
          THU64 = record helper for UInt64 function Who: string; end;
          THCard = record helper for Cardinal function Who: string; end;
          THDbl = record helper for Double function Who: string; end;
          THStr = record helper for string
            function Who: string;
            function Len: Integer;
            function Grow: string;
            class function Join(const A, B: string): string; static;
          end;
          $NOCHAR$THChar = record helper for Char function Who: string; function IsDigit: Boolean; end;
          THBool = record helper for Boolean function Who: string; end;
          THColor = record helper for TColor function Who: string; end;
          THPoint = record helper for TPoint function Who: string; end;
        const
          Seven = 7;
          Big = 3000000000;
          Half = 0.5;
          Hello = 'hello';
          Letter = 'q';
          Yes = True;
          Typed: TPoint = (X: 1; Y: 2);
        function THInt.Who: string; begin Result := 'Integer'; end;
        function THInt.Next: Integer; begin Inc(Self); Result := Self; end;
        function THI64.Who: string; begin Result := 'Int64'; end;
        function THU64.Who: string; begin Result := 'UInt64'; end;
        function THCard.Who: string; begin Result := 'Cardinal'; end;
        function THDbl.Who: string; begin Result := 'Double'; end;
        function THStr.Who: string; begin Result := 'string'; end;
        function THStr.Len: Integer; begin Result := Length(Self); end;
        function THStr.Grow: string; begin Self := Self + '!'; Result := Self; end;
        class function THStr.Join(const A, B: string): string; begin Result := A + B; end;
        $NOCHAR$function THChar.Who: string; begin Result := 'Char'; end;
        $NOCHAR$function THChar.IsDigit: Boolean; begin Result := Self in ['0'..'9']; end;
        function THBool.Who: string; begin Result := 'Boolean'; end;
        function THColor.Who: string; begin Result := 'TColor'; end;
        function THPoint.Who: string; begin Result := 'TPoint'; end;
        var
          S: string;
          N: Integer;
          B: Boolean;
          Ch: Char;
        begin
          $S$;
          WriteLn(S, N, B, Ch);
        end.
        """;

    /// <summary>The rows of <see cref="LiteralReceivers"/>: what is tested, the statement, whether the helper for Char is declared.</summary>
    public static TheoryData<string, string, bool> LiteralReceiverRows() => new()
    {
        // LANG-998 (a): the type of the receiver chooses the helper
        { "an integer literal is Integer", "S := 42.Who{=THInt}", true },
        { "a hex literal that fits Integer is Integer", "S := $7F.Who{=THInt}", true },
        { "a parenthesised integer expression is Integer", "S := (1 + 2).Who{=THInt}", true },
        { "an untyped integer constant is Integer", "S := Seven.Who{=THInt}", true },
        { "an integer literal beyond Integer is Int64", "S := 3000000000.Who{=THI64}", true },
        { "an untyped constant beyond Integer is Int64", "S := Big.Who{=THI64}", true },
        { "an integer literal beyond Int64 is UInt64", "S := 9223372036854775808.Who{=THU64}", true },
        { "an 8-digit hex literal with the top bit is Cardinal", "S := $FFFFFFFF.Who{=THCard}", true },
        { "a real literal is Double", "S := 1.5.Who{=THDbl}", true },
        { "a real literal with an exponent is Double", "S := 1e3.Who{=THDbl}", true },
        { "an untyped real constant is Double", "S := Half.Who{=THDbl}", true },
        { "a string literal is string", "S := 'abc'.Who{=THStr}", true },
        { "the empty string literal is string", "N := ''.Len{=THStr}", true },
        { "a string constant is string", "N := Hello.Len{=THStr}", true },
        { "a character literal is Char", "S := 'x'.Who{=THChar}", true },
        { "a #-character literal is Char", "B := #65.IsDigit{=THChar}", true },
        { "a character constant is Char", "S := Letter.Who{=THChar}", true },
        { "True is Boolean", "S := True.Who{=THBool}", true },
        { "a named Boolean constant is Boolean", "S := Yes.Who{=THBool}", true },
        { "a folded comparison is Boolean", "S := (1 < 2).Who{=THBool}", true },
        { "an enumeration element is its enumeration", "S := Green.Who{=THColor}", true },
        { "a typed constant is its declared type", "S := Typed.Who{=THPoint}", true },
        { "a helper constant through the helped type is a receiver", "S := Integer.Ten.Who{=THInt}", true },
        { "several suffixes after a literal", "N := 'abc'.Who{=THStr}.Len{=THStr}", true },
        { "string. names a static member of the string helper", "S := string.Join{=THStr}('a', 'b')", true },
        { "a parenthesised negative literal", "S := (-5).Who{=THInt}", true },
        { "a sign before a literal receiver applies to the result", "N := -''.Len{=THStr}", true },

        // LANG-998 (b): a character constant falls back to the helper for string
        { "char fallback: a character literal without the Char member", "N := 'x'.Len{=THStr}", true },
        { "char fallback: a #-character literal", "N := #65.Len{=THStr}", true },
        { "char fallback: a character constant", "N := Letter.Len{=THStr}", true },
        { "char fallback: no helper for Char at all", "S := 'x'.Who{=THStr}", false },
        { "char fallback: the Char member comes first", "S := Letter.Who{=THChar}", true },
        { "char fallback: not for a Char variable (E2005 for Char, LANG-992 step 4)", "N := Ch.Len{!E2005}", true },

        // LANG-998 (a): constant receivers take no mutating method (E2752), the method runs on a temporary otherwise
        { "a mutating method on an integer literal", "N := 5.Next{!E2752}", true },
        { "a mutating method on a string literal", "S := 'abc'.Grow{!E2752}", true },
        { "a mutating method on a character literal through the string helper", "S := 'x'.Grow{!E2752}", true },
        { "a mutating method on a helper constant", "N := Integer.Ten.Next{!E2752}", true },
        { "a mutating method on a named constant", "N := Seven.Next{!E2752}", true },
    };

    /// <summary>
    /// LANG-998 (a), (b): the helper of a literal or constant receiver is the one of its type — <c>Integer</c>, <c>Int64</c>,
    /// <c>UInt64</c> or <c>Cardinal</c> for integers, <c>Double</c> for reals, <c>string</c>, <c>Char</c>, <c>Boolean</c>, the
    /// enumeration of an element, the declared type of a typed constant; a character constant without the member in the helper
    /// for <c>Char</c> takes the helper for <c>string</c> (a <c>Char</c> variable does not); a mutating method on a constant
    /// receiver is E2752; <c>nil</c> and a set constructor have no helper (E2006). LANG-997: the literal forms and <c>string.</c> bind.
    /// </summary>
    [Theory]
    [MemberData(nameof(LiteralReceiverRows))]
    [Trait("Req", "LANG-997")]
    [Trait("Req", "LANG-998")]
    public void LiteralReceivers(string what, string statement, bool charHelper)
    {
        _ = what;
        var source = LiteralProgram.ReplaceLineEndings("\n")
            .Replace("$NOCHAR$", charHelper ? "" : "// ", StringComparison.Ordinal)
            .Replace("$S$", statement, StringComparison.Ordinal);
        AssertMarkers([("P.pas", source)]);
    }

    /// <summary>
    /// LANG-998 (a): <c>nil</c> and a set constructor choose no helper — E2006 at the receiver (a <c>nil</c> or a set constructor takes
    /// a member access only in parentheses, LANG-997 (a)); a parenthesised <c>Char</c> variable is no constant and has the helper for
    /// <c>Char</c> only — without the member E2005 for <c>Char</c> (LANG-992 step 4, C-440).
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-998")]
    [InlineData("S := (nil).Who", "P.pas(55,8): E2006 '(nil)' is not a unit, record, class or interface; cannot qualify with '.'")]
    [InlineData("S := ([Red]).Who", "P.pas(55,8): E2006 '([Red])' is not a unit, record, class or interface; cannot qualify with '.'")]
    [InlineData("N := (Ch).Len", "P.pas(55,13): E2005 'Char' does not contain a member named 'Len'")]
    public void LiteralReceivers_NoHelper(string statement, string expected)
    {
        var source = LiteralProgram.ReplaceLineEndings("\n").Replace("$NOCHAR$", "", StringComparison.Ordinal)
            .Replace("$S$", statement, StringComparison.Ordinal);
        var diagnostics = MarkerCompilation([("P.pas", source)]).GetDiagnostics()
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}");
        Assert.Equal([expected], diagnostics);
    }
}
