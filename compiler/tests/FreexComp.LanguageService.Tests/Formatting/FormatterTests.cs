using FreexComp.LanguageService.Formatting;

namespace FreexComp.LanguageService.Tests.Formatting;

/// <summary>formatter.md §2 / §5 (C-404): refusal, disabled text, <c>format:off</c>, line endings, meaning and idempotence on hand-made inputs.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class FormatterTests
{
    [Theory]
    [Trait("Req", "CMP-1438")]
    [InlineData("program P;\nbegin\n  Writeln(1)\n  Writeln(2);\nend.\n", "(4,3): error E1101")]
    [InlineData("program P;\nbegin\n  Writeln('abc);\nend.\n", "(3,11): error E1020")]
    [InlineData("program P;\nbegin\n  { never closed\nend.\n", "error E10")]
    [InlineData("program P;\nbegin\n  if True then Writeln(1);\n  else Writeln(2);\nend.\n", "error E1105")]
    public void RefusesSyntaxErrors(string text, string reason)
    {
        var result = FormatterHarness.Run(text);

        Assert.True(result.Refused);
        Assert.Contains(reason, result.RefusalReason, StringComparison.Ordinal);
        Assert.Empty(result.Edits);
    }

    [Fact]
    [Trait("Req", "CMP-1439")]
    public void DisabledTextKeptBytes()
    {
        const string disabled = "\t  odd\tindent ;;  begin  \n\n      {$IFDEF NESTED} still disabled {$ENDIF}\n  end end\n";
        var text = "program P;\nbegin\n      {$IFDEF NEVER_DEFINED}   \n" + disabled + "            {$ELSE}\nWriteln(1);\n {$ENDIF}\nend.\n";

        var formatted = FormatterHarness.Format(text);

        Assert.Equal(
            "program P;\nbegin\n  {$IFDEF NEVER_DEFINED}\n" + disabled + "  {$ELSE}\n  Writeln(1);\n  {$ENDIF}\nend.\n",
            formatted);
    }

    [Fact]
    [Trait("Req", "CMP-1439")]
    public void DisabledTextInsideALineIsKept()
    {
        const string text = "program P;\nbegin\n  Writeln(1 {$IFDEF NEVER_DEFINED}+  2 {$ENDIF}  );\nend.\n";

        Assert.Equal("program P;\nbegin\n  Writeln(1 {$IFDEF NEVER_DEFINED}+  2 {$ENDIF});\nend.\n", FormatterHarness.Format(text));
    }

    [Fact]
    [Trait("Req", "CMP-1444")]
    public void FormatOffRegionKept()
    {
        const string region = "   // FORMAT:OFF\n  X:=1;   Y :=2 ;\n\n\n\t if X=Y then Z:=3;\n// format:on   \n";
        var text = "program P;\nvar X, Y, Z: Integer;\nbegin\nX:=0;\n" + region + "Y:=0;\nend.\n";

        var formatted = FormatterHarness.Format(text);

        Assert.Equal("program P;\nvar\n  X, Y, Z: Integer;\nbegin\n  X := 0;\n" + region + "  Y := 0;\nend.\n", formatted);
    }

    [Fact]
    [Trait("Req", "CMP-1444")]
    public void FormatOffWithoutOnLastsToTheEnd()
    {
        const string rest = "{ Format:Off }\nX:=1;\n  end.\n\n\n";
        var text = "program P;\nvar X: Integer;\nbegin\nX:=0;\n" + rest;

        Assert.Equal("program P;\nvar\n  X: Integer;\nbegin\n  X := 0;\n" + rest, FormatterHarness.Format(text));
    }

    [Fact]
    [Trait("Req", "CMP-1444")]
    public void FormatSwitchNotAloneOnItsLineIsAComment()
    {
        const string text = "program P;\nbegin\nWriteln(1); // format:off\nWriteln( 2 );\n{ format:off } Writeln( 3 );\nend.\n";

        Assert.Equal("program P;\nbegin\n  Writeln(1); // format:off\n  Writeln(2);\n  { format:off } Writeln(3);\nend.\n", FormatterHarness.Format(text));
    }

    [Theory]
    [Trait("Req", "CMP-1446")]
    [InlineData("\r\n", "\r\n", true)]
    [InlineData("\n", "\n", true)]
    [InlineData("\r\n", "\r\n", false)]
    [InlineData("\n", "\n", false)]
    public void LineEndingsAndFinalBreakKept(string eol, string expectedEol, bool finalBreak)
    {
        var text = string.Join(eol, "program P;", "begin", "Writeln(1); Writeln(2);", "", "", "end.") + (finalBreak ? eol + eol : "");

        var formatted = FormatterHarness.Format(text);

        var expected = string.Join(expectedEol, "program P;", "begin", "  Writeln(1);", "  Writeln(2);", "end.") + (finalBreak ? expectedEol : "");
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [Trait("Req", "CMP-1446")]
    [InlineData("program P;\r\nbegin\nWriteln(1);\r\nend.\r\n", "\r\n")]          // more CRLF
    [InlineData("program P;\nbegin\r\nWriteln(1);\nend.\n", "\n")]                // more LF
    [InlineData("program P;\nbegin\r\nWriteln(1);\r\nend.\n", "\r\n")]            // equal: CRLF
    public void TheDominantLineEndingIsUsed(string text, string eol)
    {
        Assert.Equal($"program P;{eol}begin{eol}  Writeln(1);{eol}end.{eol}", FormatterHarness.Format(text));
    }

    [Fact]
    [Trait("Req", "CMP-1446")]
    public void FormattedTextIsUnchanged()
    {
        const string text = "program P;\n\nbegin\n  Writeln(1);\nend.\n";

        var result = FormatterHarness.Run(text);

        Assert.False(result.Refused);
        Assert.Empty(result.Edits);
    }

    [Fact]
    [Trait("Req", "CMP-1437")]
    public void EditsAreSmallSortedAndOnlyTouchWhitespace()
    {
        const string text = "program P;\nvar  A :Integer;\nbegin\nA:=1;   Writeln( A );\nend.\n";

        var result = FormatterHarness.Run(text);

        Assert.All(result.Edits, e => Assert.True(text.AsSpan(e.Span.Start, e.Span.Length).Trim().IsEmpty && e.NewText.Trim().Length == 0,
            $"edit '{text.Substring(e.Span.Start, e.Span.Length)}' -> '{e.NewText}' touches more than whitespace"));
        Assert.Equal("program P;\nvar\n  A: Integer;\nbegin\n  A := 1;\n  Writeln(A);\nend.\n", FormatterHarness.Apply(text, result));
    }

    /// <summary>Constructs where comments, directives and token pairs meet: the meaning is kept and a second run changes nothing.</summary>
    [Theory]
    [Trait("Req", "CMP-1437")]
    [Trait("Req", "CMP-1445")]
    [InlineData("program P;\nbegin\n  X := {c}1;\n  Foo(A{c},B);\n  Bar((*c*)A)\nend.\n")]
    [InlineData("program P;\nbegin\n  if A then\n  begin\n  end // why\n  else\n  begin\n  end;\nend.\n")]
    [InlineData("program P;\nbegin\n  if A then // then\n  begin\n    X;\n  end\n  // before else\n  else\n    Y\n  // before semicolon\n  ;\nend.\n")]
    [InlineData("program P;\nbegin\n  X := A { multi\n    line } + B; { another\n  one } Y := 2;\nend.\n")]
    [InlineData("program P;\nbegin\n  X := 1 {$IFDEF A} + 2 {$ELSE} + 3 {$ENDIF};\n  {$IFDEF A}Y := 1;{$ENDIF}\nend.\n")]
    [InlineData("program P;\ntype\n  TFoo<T>=class end;\n  TBar = TFoo<TFoo<Integer>>;\nbegin\n  if TFoo<Integer>.Create<>nil then;\nend.\n")]
    [InlineData("program P;\nlabel 10, Done;\nbegin\n10: X := 1;\n  goto Done;\nDone:\nend.\n")]
    [InlineData("program P;\nbegin\n  Run(procedure begin X; end, function(A: Integer): Integer begin Result := A; end);\nend.\n")]
    [InlineData("program P;\ntype\n  TRec = record\n    case Integer of\n      0: (A: Integer; case B: Byte of 1: (C: Word));\n      1: (D: Double)\n  end;\nbegin\nend.\n")]
    [InlineData("program P;\nuses\n  A in 'a.pas', B in 'sub\\b.pas';\nbegin\n  S := 'a'#13#10'b' + #9;\n  X := -(-1) - -1 + not not B;\n  P := @Q^; Y := P^.X^ + A[1][2];\n  Writeln(X:5:2, Y:3);\nend.\n")]
    [InlineData("program P;\nbegin\nend.\n// after the end\n{ and more }")]
    [InlineData("program P;\nbegin\nend.\n\n\n")]
    [InlineData("unit U;\ninterface\ntype\n  TC = class\n  public type\n    TInner = Integer;\n  private const\n    K = 1;\n  public\n    [Weak] FOwner: TObject;\n    property Items[I: Integer]: Integer read Get; default;\n    property Flags: TFlags read F default [];\n  end;\nimplementation\nend.\n")]
    [InlineData("program P;\nbegin\n  case X of\n    1, 2,\n    3: ;\n    4: begin end\n  else ;\n  end;\n  try except end;\n  try finally end;\n  repeat until True;\nend.\n")]
    [InlineData("program P;\nbegin\n\twhile\tA\tdo\t;\n  with A, B do begin end;\n  for var I := 1 to 2 do ;\n  for var S in L do Writeln(S);\n  raise E at Addr;\nend.\n")]
    [InlineData("{ header }\n\n\n// second\nprogram P;  // trailing\n{$APPTYPE CONSOLE}\nbegin end.")]
    public void TrickyInputsKeepMeaningAndAreStable(string text)
    {
        foreach (var style in new[] { FormatProfile.Default, FormatProfile.Default with { Begin = BeginStyle.SameLine, IndentSize = 3, MaxBlankLines = 0 } })
        {
            var once = FormatterHarness.Format(text, style);
            Assert.Equal(FormatterHarness.Stream(text), FormatterHarness.Stream(once));
            Assert.Equal(once, FormatterHarness.Format(once, style));
            Assert.DoesNotContain(" \n", once, StringComparison.Ordinal);
        }
    }
}
