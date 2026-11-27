using FreexComp.Compiler.Text;
using FreexComp.LanguageService.Formatting;

namespace FreexComp.LanguageService.Tests.Formatting;

/// <summary>
/// formatter.md §2 / §3 / §5 (C-404, C-405): refusal, disabled text, <c>format:off</c>, line endings, meaning and idempotence on
/// hand-made inputs; profiles, ranges and letter case.
/// </summary>
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

    // ------------------------------------------------------------------ C-405: profiles (CMP-1452)

    [Fact]
    [Trait("Req", "CMP-1452")]
    public void ProfileSearchUpwardsFirstWins()
    {
        var root = NewTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a", "b", "c"));
            Directory.CreateDirectory(Path.Combine(root, "x"));
            File.WriteAllText(Path.Combine(root, ".pasformat"), """{ "indentSize": 4, "begin": "sameLine" }""");
            File.WriteAllText(Path.Combine(root, "a", ".pasformat"), """{ "indentSize": 3 }""");

            var nested = PascalFormatter.LoadProfile(Path.Combine(root, "a", "b", "c", "Unit1.pas"));
            var beside = PascalFormatter.LoadProfile(Path.Combine(root, "a", "Unit1.pas"));
            var other = PascalFormatter.LoadProfile(Path.Combine(root, "x", "Unit1.pas"));

            // the nearest file is used alone: nothing of the outer one (begin) is merged in
            Assert.Equal(FormatProfile.Default with { IndentSize = 3, SourcePath = Path.Combine(root, "a", ".pasformat") }, nested);
            Assert.Equal(nested, beside);
            Assert.Equal(FormatProfile.Default with { IndentSize = 4, Begin = BeginStyle.SameLine, SourcePath = Path.Combine(root, ".pasformat") }, other);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "CMP-1452")]
    public void ProfileBadValuesFallBack()
    {
        var root = NewTempDirectory();
        try
        {
            var path = Path.Combine(root, ".pasformat");
            File.WriteAllText(path, """
                {
                  // comments and a trailing comma are accepted
                  "indentSize": 0, "begin": "SameLine", "keywordCase": "upper", "directiveCase": 3,
                  "identifiersAsDeclared": "no", "maxLineLength": 39, "uses": "fill", "maxBlankLines": 6,
                  "spaceAroundOperators": false, "spaceAfterComma": null, "spaceAroundAssign": 1,
                  "spaceBeforeColonInDecl": true, "unknownKey": [1, 2], "IndentSize": 5,
                }
                """);

            var profile = PascalFormatter.LoadProfile(Path.Combine(root, "Unit1.pas"));

            Assert.Equal(FormatProfile.Default with
            {
                Keywords = KeywordCase.Upper, Uses = UsesWrap.Fill, SpaceAroundOperators = false, SpaceBeforeColonInDecl = true, SourcePath = path,
            }, profile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [Trait("Req", "CMP-1452")]
    [InlineData("""{ "indentSize": 1, "maxLineLength": 40, "maxBlankLines": 0 }""", 1, 40, 0)]
    [InlineData("""{ "indentSize": 8, "maxLineLength": 400, "maxBlankLines": 5 }""", 8, 400, 5)]
    [InlineData("""{ "indentSize": 9, "maxLineLength": 401, "maxBlankLines": -1 }""", 2, 120, 1)]
    [InlineData("""{ "indentSize": 2.5, "maxLineLength": 0, "maxBlankLines": "2" }""", 2, 0, 1)]
    public void ProfileNumberBounds(string json, int indent, int maxLine, int maxBlank)
    {
        var profile = FormatProfile.Parse(json, "p")!;

        Assert.Equal((indent, maxLine, maxBlank), (profile.IndentSize, profile.MaxLineLength, profile.MaxBlankLines));
    }

    [Theory]
    [Trait("Req", "CMP-1452")]
    [InlineData("indentSize: 4")]
    [InlineData("""{ "indentSize": 4 """)]
    [InlineData("[ { \"indentSize\": 4 } ]")]
    [InlineData("")]
    public void ProfileNotJsonUsesDefault(string content)
    {
        var root = NewTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, ".pasformat"), """{ "indentSize": 4 }""");
            File.WriteAllText(Path.Combine(root, "sub", ".pasformat"), content);

            var profile = PascalFormatter.LoadProfile(Path.Combine(root, "sub", "Unit1.pas"));

            // the nearest file is not JSON: the built-in style (not the outer file), with no source path (IDE-ED-155 reports it)
            Assert.Equal(FormatProfile.Default, profile);
            Assert.Null(profile.SourcePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ------------------------------------------------------------------ C-405: ranges (CMP-1453)

    private const string RangeText =
        "program P;\nvar\n  A, B: Integer;\nbegin\nA:=1;\nif A>0 then\nB:=2;\n  Writeln( A,\nB );\nWRITELN(A)  ;\nend.\n";

    [Theory]
    [Trait("Req", "CMP-1453")]
    [InlineData("A>0", "B:=2", "program P;\nvar\n  A, B: Integer;\nbegin\nA:=1;\n  if A > 0 then\n    B := 2;\n  Writeln( A,\nB );\nWRITELN(A)  ;\nend.\n")]
    [InlineData("( A", "( A", "program P;\nvar\n  A, B: Integer;\nbegin\nA:=1;\nif A>0 then\nB:=2;\n  Writeln(A,\n    B);\nWRITELN(A)  ;\nend.\n")]
    [InlineData("1;", "B );", "program P;\nvar\n  A, B: Integer;\nbegin\n  A := 1;\n  if A > 0 then\n    B := 2;\n  Writeln(A,\n    B);\nWRITELN(A)  ;\nend.\n")]
    [InlineData("WRITELN", "  ;", "program P;\nvar\n  A, B: Integer;\nbegin\nA:=1;\nif A>0 then\nB:=2;\n  Writeln( A,\nB );\n  Writeln(A);\nend.\n")]
    public void RangeWidensToStatements(string from, string to, string expected)
    {
        var start = RangeText.IndexOf(from, StringComparison.Ordinal) + 1;
        var end = RangeText.IndexOf(to, StringComparison.Ordinal) + to.Length - 1;
        var range = TextSpan.FromBounds(start, Math.Max(start, end));

        var result = FormatterHarness.Run(RangeText, range: range);

        Assert.False(result.Refused, result.RefusalReason);
        var formatted = FormatterHarness.Apply(RangeText, result);
        Assert.Equal(expected, formatted);
        Assert.Equal(FormatterHarness.Stream(RangeText), FormatterHarness.Stream(formatted));
    }

    [Fact]
    [Trait("Req", "CMP-1453")]
    public void RangeLeavesOutsideUntouched()
    {
        const string before = "unit U;\ninterface\nPROCEDURE   Run;\nimplementation\nprocedure Run;\nvar   X ,Y :integer;\nbegin\n  X:=1;   Y:=2;\n";
        const string inside = "    IF X=Y THEN BEGIN\n  writeln( X ) ; end\n";
        const string after = "  ELSE   Y:=3;\n\n\n  X:=Y;   \nend;\nend.\n";
        var text = before + inside + after;
        var range = new TextSpan(before.Length + 6, 1);       // in "X=Y"

        var result = FormatterHarness.Run(text, range: range);

        // the if statement (with its else part) is the item; only its lines change, the indentation of its first line included
        var formatted = FormatterHarness.Apply(text, result);
        var expectedInside = "  if X = Y then\n  begin\n    Writeln(X);\n  end\n  else Y := 3;\n";
        Assert.Equal(before + expectedInside + after["  ELSE   Y:=3;\n".Length..], formatted);
        var itemStart = before.Length;
        var itemEnd = before.Length + inside.Length + "  ELSE   Y:=3;".Length;
        Assert.All(result.Edits, e => Assert.True(e.Span.Start >= itemStart && e.Span.End <= itemEnd, $"edit at {e.Span.Start} outside {itemStart}..{itemEnd}"));

        // a range over the whole text formats like no range
        var whole = FormatterHarness.Run(text, range: new TextSpan(0, text.Length));
        Assert.Equal(FormatterHarness.Format(text), FormatterHarness.Apply(text, whole));
    }

    // ------------------------------------------------------------------ C-405: identifiers (CMP-1449)

    /// <summary>
    /// The file is bound without <c>System</c>: a routine passed where a method reference is expected used to crash the binder
    /// (no forwarder ancestor); it binds as a value now and the identifiers get their declared case.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-1449")]
    public void IdentifiersAsDeclaredWithARoutineReference()
    {
        const string text = "program P;\ntype\n  TProc = reference to procedure;\nprocedure Foo;\nbegin\nend;\nvar\n  Callback: TProc;\nbegin\n  callback := foo;\nend.\n";

        var formatted = FormatterHarness.Format(text);

        Assert.Equal(text.Replace("callback := foo", "Callback := Foo"), formatted);
    }

    [Fact]
    [Trait("Req", "CMP-1448")]
    [Trait("Req", "CMP-1449")]
    public void CaseEditsTouchOnlyLetterCase()
    {
        const string text = "PROGRAM P;\nVAR\n  Count: integer;\nBEGIN\n  COUNT := 1; { BEGIN } Writeln('BEGIN', count);\nEND.\n";

        var result = FormatterHarness.Run(text);

        var formatted = FormatterHarness.Apply(text, result);
        Assert.Equal("program P;\nvar\n  Count: Integer;\nbegin\n  Count := 1; { BEGIN }\n  Writeln('BEGIN', Count);\nend.\n", formatted);
        Assert.All(result.Edits, e =>
        {
            var old = text.Substring(e.Span.Start, e.Span.Length);
            Assert.True(old.Trim().Length == 0 || string.Equals(old, e.NewText, StringComparison.OrdinalIgnoreCase), $"edit '{old}' -> '{e.NewText}'");
        });
    }

    [Fact]
    [Trait("Req", "CMP-1448")]
    [Trait("Req", "CMP-1444")]
    public void FormatOffRegionKeepsCase()
    {
        const string region = "// format:off\nIF X THEN WRITELN(x); {$ifdef A}{$endif}\n// format:on\n";
        var text = "program P;\nvar X: Boolean;\nBEGIN\n" + region + "IF x THEN WRITELN(x); {$ifdef A}{$endif}\nEND.\n";

        Assert.Equal("program P;\nvar\n  X: Boolean;\nbegin\n" + region + "  if X then Writeln(X); {$IFDEF A}{$ENDIF}\nend.\n", FormatterHarness.Format(text));
    }

    private static string NewTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
