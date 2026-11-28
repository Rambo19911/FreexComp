using System.Text.RegularExpressions;

namespace FreexComp.Compiler.Tests.Binding;

// LANG-993 (c), (d): identity of helped types and the helper of a generic body (phase8/helpers-completion.md §15.8; card
// F10-029), C-445.
public sealed partial class HelperBindingTests
{
    /// <summary>
    /// The helper <c>TH</c> is declared for <c>$HELPED$</c>; a variable <c>V</c> of <c>$VAR$</c> calls <c>Kind</c>. <c>$CALL$</c>
    /// is the marked member access.
    /// </summary>
    private const string IdentityProgram = """
        program P;
        type
          TMyInt = Integer;
          TMyText = string;
          TDistinct = type Integer;
          TDigit = 0..9;
          TH = record helper for $HELPED$
            function Kind: Integer;
          end;
        function TH.Kind: Integer;
        begin
          Result := 1;
        end;
        var
          V: $VAR$;
        begin
          if $CALL$ > 0 then
            Writeln;
        end.
        """;

    /// <summary>(helped type, type of the variable, binds): LANG-993 (c) — aliases share one helper, everything else is its own type.</summary>
    public static TheoryData<string, string, bool> IdentityRows() => new()
    {
        // predeclared aliases, both ways
        { "Integer", "LongInt", true },
        { "LongInt", "Integer", true },
        { "Cardinal", "LongWord", true },
        { "LongWord", "Cardinal", true },
        { "Char", "WideChar", true },
        { "WideChar", "Char", true },
        { "Double", "Real", true },
        { "Double", "Extended", true },
        { "Real", "Double", true },
        { "Extended", "Real", true },
        { "string", "UnicodeString", true },
        { "UnicodeString", "string", true },
        { "PChar", "PWideChar", true },
        { "PWideChar", "PChar", true },
        // declared aliases
        { "Integer", "TMyInt", true },
        { "TMyInt", "Integer", true },
        { "string", "TMyText", true },
        // own types
        { "Integer", "NativeInt", false },
        { "Int64", "NativeInt", false },
        { "NativeInt", "Int64", false },
        { "Cardinal", "NativeUInt", false },
        { "UInt64", "NativeUInt", false },
        { "Boolean", "ByteBool", false },
        { "ByteBool", "Boolean", false },
        { "LongBool", "WordBool", false },
        { "string", "AnsiString", false },
        { "AnsiString", "UTF8String", false },
        { "AnsiString", "RawByteString", false },
        { "string", "ShortString", false },
        { "Char", "AnsiChar", false },
        { "Single", "Double", false },
        { "Double", "Currency", false },
        { "Integer", "TDistinct", false },
        { "TDistinct", "Integer", false },
        { "Integer", "TDigit", false },
        { "Integer", "Byte", false },
        { "Integer", "SmallInt", false },
        { "Pointer", "PChar", false },
    };

    /// <summary>
    /// LANG-993 (c): a helper for a type is the helper of every alias of that type — predeclared (<c>LongInt</c> = <c>Integer</c>,
    /// <c>LongWord</c> = <c>Cardinal</c>, <c>WideChar</c> = <c>Char</c>, <c>Real</c> = <c>Extended</c> = <c>Double</c>,
    /// <c>UnicodeString</c> = <c>string</c>, <c>PWideChar</c> = <c>PChar</c>) and declared (<c>TMyInt = Integer</c>); a variable
    /// of any other type — <c>NativeInt</c>, <c>NativeUInt</c>, each member of the Boolean family, each string and character type,
    /// a distinct type, a subrange, another integer or real type — has no helper: E2006 at the receiver.
    /// </summary>
    [Theory]
    [MemberData(nameof(IdentityRows))]
    [Trait("Req", "LANG-993")]
    public void BuiltInTargets_Identity(string helped, string variable, bool binds) => AssertMarkers(
    [
        ("P.pas", IdentityProgram
            .Replace("$HELPED$", helped, StringComparison.Ordinal)
            .Replace("$VAR$", variable, StringComparison.Ordinal)
            .Replace("$CALL$", binds ? "V.Kind{=TH}" : "V{!E2006}.Kind", StringComparison.Ordinal)),
    ]);

    /// <summary>A generic whose <c>Show</c> calls <c>Who</c> on an <c>Integer</c> and on a <c>T</c>; its implementation uses <c>$USES$</c>.</summary>
    private const string GenericUnit = """
        unit UGen;
        interface
        type
          TBox<T> = class
            function Show(N: Integer; const V: T): Integer;
          end;
        implementation
        $USES$
        function TBox<T>.Show(N: Integer; const V: T): Integer;
        begin
          Result := $BODY$;
        end;
        end.
        """;

    private static string WhoUnit(string unit, string helper, int add) => $$"""
        unit {{unit}};
        interface
        type
          {{helper}} = record helper for Integer
            function Who: Integer;
          end;
        implementation
        function {{helper}}.Who: Integer;
        begin
          Result := Self + {{add}};
        end;
        end.
        """;

    private const string GenericProgram = """
        program P;
        uses UGen, UB;
        var
          B: TBox<Integer>;
        begin
          B := TBox<Integer>.Create;
          Writeln(B.Show(1, 2), 3.Who{=THB});
        end.
        """;

    /// <summary>
    /// LANG-993 (d): a member access in the body of a generic is bound, for every instance, at its place in the generic's source.
    /// (1) The helper active there (<c>THA</c>, from the implementation uses clause of the generic's unit) is the one the instance
    /// <c>TBox&lt;Integer&gt;</c> calls — for the <c>Integer</c> parameter and for the <c>T</c> one — although the instance is made
    /// in a program where another helper (<c>THB</c>) is active and <c>UA</c> is not used. (2) A helper active only where the
    /// instance is made takes no part: the error arrives as E2710 of the instance.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-993")]
    public void GenericBody_HelperOfTheGenericsSource()
    {
        (string File, string Text)[] files =
        [
            ("UA.pas", WhoUnit("UA", "THA", 100)),
            ("UB.pas", WhoUnit("UB", "THB", 200)),
            ("UGen.pas", GenericUnit.Replace("$USES$", "uses UA;", StringComparison.Ordinal).Replace("$BODY$", "N.Who{=THA} + V.Who", StringComparison.Ordinal)),
            ("P.pas", GenericProgram),
        ];
        AssertMarkers(files);
        var compilation = MarkerCompilation(files);
        var result = compilation.Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join('\n', result.Diagnostics.Select(d => d.ToText())));
        var calls = result.CompiledUnits.Where(u => u is not "UA" and not "UB")
            .SelectMany(u => Regex.Matches(compilation.FileSystem.ReadAllText(Path.Combine(@"C:\p\obj", u + ".ll")), @"call i32 @""(U[AB]\.TH[AB]\.Who)""").Select(m => $"{u}: {m.Groups[1].Value}"))
            .Order(StringComparer.Ordinal);
        Assert.Equal(["P: UA.THA.Who", "P: UA.THA.Who", "P: UB.THB.Who"], calls);

        AssertMarkers(
        [
            ("UB.pas", WhoUnit("UB", "THB", 200)),
            ("UGen.pas", GenericUnit.Replace("$USES$", "", StringComparison.Ordinal).Replace("$BODY$", "V{!E2710}.Who", StringComparison.Ordinal)),
            ("P.pas", GenericProgram),
        ]);
    }

    /// <summary>
    /// LANG-992 step 4 with LANG-990 (b) (§17.1, from C-442): <c>G.Missing</c> on a value of a function type without parameters
    /// whose active helper does not declare <c>Missing</c> calls the value and looks the name up in the result. When the result
    /// type has members or an active helper, <c>E2005</c> names the result type; when it has neither, the function type is the
    /// one type with an active helper the lookup visited: <c>E2005</c> names it (it was <c>E2006</c> at <c>G</c>).
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-992")]
    [InlineData("", "P.pas(15,13): E2005 'TGet' does not contain a member named 'Missing'")]
    [InlineData("  TIntH = record helper for Integer function Plus: Integer; end;", "P.pas(15,13): E2005 'Integer' does not contain a member named 'Missing'")]
    public void FunctionTypeValue_MissingName_IsE2005(string intHelper, string expected)
    {
        var source = $$"""
            program P;
            type
              TGet = function: Integer;
              TGetH = record helper for TGet
                function Twice: Integer;
              end;
            {{intHelper}}
            function TGetH.Twice: Integer; begin Result := Self() * 2 end;
            {{(intHelper.Length == 0 ? "" : "function TIntH.Plus: Integer; begin Result := Self + 1 end;")}}
            function One: Integer; begin Result := 1 end;
            var
              G: TGet;
            begin
              G := One;
              Writeln(G.Missing);
            end.
            """.ReplaceLineEndings("\n");
        var diagnostics = MarkerCompilation([("P.pas", source)]).GetDiagnostics(ct: TestContext.Current.CancellationToken)
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}");
        Assert.Equal([expected], diagnostics);
    }

    /// <summary>
    /// LANG-992 (b) with step 4 (§7, LANG-997 (b): "the <c>E2005</c> row is C-445's"): a type-qualified <c>T.N</c> on a type that is
    /// no class or record, whose active helper does not declare <c>N</c>, is <c>E2005</c> with the name of <c>T</c> — also
    /// <c>string.N</c>; a type without an active helper keeps <c>E2009</c> (no value).
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-992")]
    [InlineData("Integer.Missing", "P.pas(8,19): E2005 'Integer' does not contain a member named 'Missing'")]
    [InlineData("string.Missing", "P.pas(8,18): E2005 'string' does not contain a member named 'Missing'")]
    [InlineData("Byte.Missing", "P.pas(8,11): E2009 Variable or constant expected but type 'Byte' found")]
    public void TypeQualified_MissingName_IsE2005(string access, string expected)
    {
        var source = $$"""
            program P;
            type
              TIntH = record helper for Integer function Plus: Integer; end;
              TStrH = record helper for string function Len: Integer; end;
            function TIntH.Plus: Integer; begin Result := Self + 1 end;
            function TStrH.Len: Integer; begin Result := System.Length(Self) end;
            begin
              Writeln({{access}});
            end.
            """.ReplaceLineEndings("\n");
        var diagnostics = MarkerCompilation([("P.pas", source)]).GetDiagnostics(ct: TestContext.Current.CancellationToken)
            .Select(d => $"{Path.GetFileName(d.Location.Path)}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Code} {d.Message}");
        Assert.Equal([expected], diagnostics);
    }
}
