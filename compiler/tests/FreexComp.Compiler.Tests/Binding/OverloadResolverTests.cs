using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>LANG-441/442/443: which overload a call picks (C-104, C-180).</summary>
[Trait("Phase", "2")]
public sealed class OverloadResolverTests
{
    /// <summary>Binds <c>program T; decls var …; begin call end.</c>; the chosen routine's link name, or the diagnostics.</summary>
    private static string Chosen(string declarations, string call)
    {
        var (result, _) = Bind("program T; " + declarations +
            " var vi: Integer; vb: Byte; vw: Word; vc: Cardinal; vi64: Int64; vs1: Single; vd: Double; vch: Char; vac: AnsiChar; " +
            "vs: string; va: AnsiString; vr: RawByteString; vp: PChar; vpa: PAnsiChar; vptr: Pointer; vpi: ^Integer; " +
            "begin " + call + " end.");
        if (!result.Diagnostics.IsEmpty)
        {
            return string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
        }

        var statement = Assert.Single(result.Bodies[0].Statements);
        return Assert.IsType<BoundCallStatement>(statement).Routine.LinkName;
    }

    private const string IntInt64DoubleString =
        "procedure P(x: Integer); overload; begin end; procedure P(x: Int64); overload; begin end; " +
        "procedure P(x: Double); overload; begin end; procedure P(const x: string); overload; begin end;";

    [Theory]
    [Trait("Req", "LANG-442")]
    [InlineData("P(1)", "T.P$Integer")]                     // literal of natural type Integer: 0 beats 1 (Int64), 3 (Double)
    [InlineData("P(5000000000)", "T.P$Int64")]              // Int64 literal: Integer not applicable
    [InlineData("P(1.5)", "T.P$Double")]
    [InlineData("P('ab')", "T.P$c:string")]
    [InlineData("P('a')", "T.P$c:string")]                  // Char → string (2); no Char candidate
    [InlineData("P(vi)", "T.P$Integer")]
    [InlineData("P(vi64)", "T.P$Int64")]
    [InlineData("P(vc)", "T.P$Int64")]                       // Cardinal → Int64 widens (1), → Integer changes sign (2)
    [InlineData("P(vs1)", "T.P$Double")]                     // Single → Double (1)
    [InlineData("P(vd)", "T.P$Double")]
    [InlineData("P(vs)", "T.P$c:string")]
    [InlineData("P(va)", "T.P$c:string")]                    // string kind conversion
    public void IntegerInt64DoubleString(string call, string expected) => Assert.Equal(expected, Chosen(IntInt64DoubleString, call));

    [Theory]
    [Trait("Req", "LANG-443")]
    [InlineData("procedure Q(x: Int64); overload; begin end; procedure Q(x: Double); overload; begin end;", "Q(1)", "T.Q$Int64")]
    [InlineData("procedure Q(x: Int64); overload; begin end; procedure Q(x: Double); overload; begin end;", "Q(vi)", "T.Q$Int64")]
    [InlineData("procedure Q(x: Byte); overload; begin end; procedure Q(x: Double); overload; begin end;", "Q(1)", "T.Q$Byte")]
    [InlineData("procedure Q(x: Byte); overload; begin end; procedure Q(x: Double); overload; begin end;", "Q(300)", "T.Q$Double")]
    [InlineData("procedure Q(x: Byte); overload; begin end; procedure Q(x: Integer); overload; begin end;", "Q(vb)", "T.Q$Byte")]
    [InlineData("procedure Q(x: Word); overload; begin end; procedure Q(x: Double); overload; begin end;", "Q(vb)", "T.Q$Word")]
    [InlineData("procedure Q(x: Byte); overload; begin end; procedure Q(x: Word); overload; begin end;", "Q(vi)",
        "E2122: Ambiguous overloaded call to 'Q'")]                                   // narrowing either way (2 = 2)
    public void IntegerLiterals(string declarations, string call, string expected) => Assert.Equal(expected, Chosen(declarations, call));

    private const string IntToStrLike =
        "function S(V: Integer): string; overload; begin Result := '' end; function S(V: Int64): string; overload; begin Result := '' end; " +
        "function S(V: UInt64): string; overload; begin Result := '' end; " +
        "function H(V: Int64; D: Integer): string; overload; begin Result := '' end; function H(V: UInt64; D: Integer): string; overload; begin Result := '' end;";

    /// <summary>
    /// C-201 (types.md lead decision, LANG-442 dominance): among widening targets that all fit the source range the narrower one
    /// wins, at equal size the signed one; narrowing pairs stay ambiguous.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-442")]
    [InlineData("S(vw)", "T.S$Integer")]                    // Word → Integer and → Int64 / UInt64 all widen: the narrowest
    [InlineData("S(vb)", "T.S$Integer")]
    [InlineData("S(vc)", "T.S$Int64")]                      // Cardinal → Int64 / UInt64 (Integer narrows): equal size, signed
    [InlineData("H(vc, 8)", "T.H$Int64,Integer")]
    [InlineData("H(vw, 8)", "T.H$Int64,Integer")]
    public void IntegerWideningPrefersNarrowerThenSigned(string call, string expected) => Assert.Equal(expected, ChosenCall(IntToStrLike, call));

    /// <summary>
    /// C-254 decision (LANG-442 and the types.md widening tie-break): a 64-bit unsigned argument (<c>NativeUInt</c> on x64,
    /// <c>WPARAM</c>) against an <c>Integer</c>/<c>Int64</c> pair costs 2 on both (narrowing and signedness change; neither is
    /// a widening that fits the source range), so the call is E2122. A <c>UInt64</c> overload is the exact match (cost 0).
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-442")]
    [InlineData(false, "E2122: Ambiguous overloaded call to 'S'")]
    [InlineData(true, "T.S$UInt64")]
    public void NativeUIntArgument(bool withUInt64Overload, string expected)
    {
        var declarations = "type WPARAM = NativeUInt; " +
            "function S(V: Integer): string; overload; begin Result := '' end; function S(V: Int64): string; overload; begin Result := '' end; " +
            (withUInt64Overload ? "function S(V: UInt64): string; overload; begin Result := '' end; " : "");
        var (result, _) = Bind("program T; " + declarations + " var W: WPARAM; text: string; begin text := S(W) end.");
        if (!result.Diagnostics.IsEmpty)
        {
            Assert.Equal(expected, string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
            return;
        }

        var assignment = Assert.IsType<BoundAssignmentStatement>(Assert.Single(result.Bodies[0].Statements));
        Assert.Equal(expected, Assert.IsType<BoundCallExpression>(assignment.Value).Routine.LinkName);
    }

    /// <summary>The function <c>text := call</c> picks, or the diagnostics.</summary>
    private static string ChosenCall(string declarations, string call)
    {
        var (result, _) = Bind("program T; " + declarations + " var vb: Byte; vw: Word; vc: Cardinal; text: string; begin text := " + call + " end.");
        if (!result.Diagnostics.IsEmpty)
        {
            return string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
        }

        var assignment = Assert.IsType<BoundAssignmentStatement>(Assert.Single(result.Bodies[0].Statements));
        return Assert.IsType<BoundCallExpression>(assignment.Value).Routine.LinkName;
    }

    [Theory]
    [Trait("Req", "LANG-442")]
    [InlineData("C('a')", "T.C$Char")]
    [InlineData("C(vch)", "T.C$Char")]
    [InlineData("C('ab')", "T.C$c:string")]
    [InlineData("C(vs)", "T.C$c:string")]
    public void CharVersusString(string call, string expected) =>
        Assert.Equal(expected, Chosen("procedure C(x: Char); overload; begin end; procedure C(const x: string); overload; begin end;", call));

    /// <summary>C-180: <c>string</c> vs <c>RawByteString</c> parameters (the RTL's SysUtils overload pairs).</summary>
    [Theory]
    [Trait("Req", "LANG-442")]
    [InlineData("S(vs)", "T.S$c:string")]
    [InlineData("S('ab')", "T.S$c:string")]
    [InlineData("S(va)", "T.S$c:RawByteString")]                // byte string → byte string beats a kind conversion to string
    [InlineData("S(vr)", "T.S$c:RawByteString")]
    [InlineData("S(vp)", "T.S$c:string")]                       // PChar → UnicodeString
    public void RankingBetweenCompatibleCandidates(string call, string expected) =>
        Assert.Equal(expected, Chosen("procedure S(const x: string); overload; begin end; procedure S(const x: RawByteString); overload; begin end;", call));

    [Theory]
    [Trait("Req", "LANG-442")]
    // neither dominates; the smaller sum (1 + 1 < 0 + 3) wins
    [InlineData("procedure X(a, b: Int64); overload; begin end; procedure X(a: Integer; b: Double); overload; begin end;", "X(vi, vi)", "T.X$Int64,Int64")]
    // A dominates B
    [InlineData("procedure X(a: Integer; b: Integer); overload; begin end; procedure X(a: Int64; b: Integer); overload; begin end;", "X(vi, vi)", "T.X$Integer,Integer")]
    // var: exact type only
    [InlineData("procedure X(var a: Integer); overload; begin end; procedure X(var a: Int64); overload; begin end;", "X(vi64)", "T.X$v:Int64")]
    // pointer → Pointer (4) loses against the identical typed pointer
    [InlineData("procedure X(x: Pointer); overload; begin end; procedure X(x: PChar); overload; begin end;", "X(vp)", "T.X$PChar")]
    [InlineData("procedure X(x: Pointer); overload; begin end; procedure X(x: PChar); overload; begin end;", "X(vpi)", "T.X$Pointer")]
    // count decides first
    [InlineData("procedure X(a: Integer); overload; begin end; procedure X(a, b: Integer); overload; begin end;", "X(1, 2)", "T.X$Integer,Integer")]
    public void Ranking(string declarations, string call, string expected) => Assert.Equal(expected, Chosen(declarations, call));

    [Fact]
    [Trait("Req", "LANG-442")]
    public void Ambiguous_IsE2122_WithTheTiedCandidatesAsRelatedLocations()
    {
        var (result, _) = Bind("program T;\nprocedure X(a: Integer; b: Double); overload; begin end;\n" +
            "procedure X(a: Double; b: Integer); overload; begin end;\nbegin\n  X(1, 1)\nend.");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("E2122(5,3): Ambiguous overloaded call to 'X'", Format([diagnostic])[0]);
        Assert.Equal([(1, 10), (2, 10)], diagnostic.Related.Select(r => (r.Location.LineSpan.Start.Line, r.Location.LineSpan.Start.Column)));
        Assert.All(diagnostic.Related, r => Assert.Equal("candidate", r.Message));
    }

    [Fact]
    [Trait("Req", "LANG-441")]
    public void Methods_OverloadsAcrossTheInheritanceChain()
    {
        var (result, _) = Bind("program T; type TA = class procedure Put(x: Integer); overload; procedure Put(const x: string); overload; end; " +
            "TB = class(TA) procedure Put(x: Double); overload; end; " +
            "procedure TA.Put(x: Integer); begin end; procedure TA.Put(const x: string); begin end; procedure TB.Put(x: Double); begin end; " +
            "var o: TB; begin o.Put(1); o.Put(1.5); o.Put('x') end.");

        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal(["T.TA.Put$Integer", "T.TB.Put$Double", "T.TA.Put$c:string"],
            result.Bodies[0].Statements.Cast<BoundCallStatement>().Select(c => c.Routine.LinkName));
    }

    [Fact]
    [Trait("Req", "LANG-441")]
    public void Methods_AnOverriddenOverloadIsOneCandidate()
    {
        var (result, _) = Bind("program T; type TA = class procedure Put(x: Integer); overload; virtual; procedure Put(const x: string); overload; virtual; end; " +
            "TB = class(TA) procedure Put(x: Integer); overload; override; end; " +
            "procedure TA.Put(x: Integer); begin end; procedure TA.Put(const x: string); begin end; procedure TB.Put(x: Integer); begin end; " +
            "var o: TB; begin o.Put(1); o.Put('x') end.");

        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal(["T.TB.Put$Integer", "T.TA.Put$c:string"],
            result.Bodies[0].Statements.Cast<BoundCallStatement>().Select(c => c.Routine.LinkName));
    }

    /// <summary>
    /// C-210 (LANG-441, LANG-310): inside a method a bare call of an overloaded name binds like <c>Self.Name</c>; the ancestor
    /// method that the parameterless <c>overload; override</c> replaces is not a second candidate (no E2122), and the call stays
    /// virtual. The overload with a parameter (<c>reintroduce; overload</c>) is still found.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-441")]
    [Trait("Req", "LANG-310")]
    [InlineData("ToString", "T.TB.ToString$", true)]
    [InlineData("ToString()", "T.TB.ToString$", true)]
    [InlineData("Self.ToString", "T.TB.ToString$", true)]
    [InlineData("ToString(1)", "T.TB.ToString$Integer", false)]
    public void ImplicitSelfOverloadOverride(string call, string linkName, bool isVirtual)
    {
        var (result, _) = Bind($$"""
            program T;
            type
              TA = class function ToString: string; virtual; end;
              TB = class(TA)
                function ToString: string; overload; override;
                function ToString(N: Integer): string; reintroduce; overload;
                function Twice: string;
              end;
            function TA.ToString: string; begin Result := 'a' end;
            function TB.ToString: string; begin Result := 'b' end;
            function TB.ToString(N: Integer): string; begin Result := 'n' end;
            function TB.Twice: string; begin Result := {{call}} end;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var body = result.Routines.Single(r => r.Routine.Name == "Twice").Body;
        var assignment = Assert.IsType<BoundAssignmentStatement>(Assert.Single(body.Statements));
        var bound = Assert.IsType<BoundCallExpression>(assignment.Value);
        Assert.Equal((linkName, isVirtual), (bound.Routine.LinkName, bound.Receiver?.IsVirtual ?? false));
    }

    /// <summary>
    /// C-224 (LANG-442, lead decision 2026-11-15): among candidates tied on cost, the one declared in the more derived class wins
    /// before the fewest-defaults rule — <c>Create(ACapacity: Integer = 0)</c> beats the inherited parameterless <c>Create</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-442")]
    public void NearerClassBeatsFewerDefaults()
    {
        var (result, _) = Bind("program T; type TA = class procedure Make; overload; end; " +
            "TB = class(TA) procedure Make(N: Integer = 0); overload; end; " +
            "TC = class(TB) end; " +
            "procedure TA.Make; begin end; procedure TB.Make(N: Integer); begin end; " +
            "var a: TA; c: TC; begin c.Make; a.Make end.");

        Assert.Empty(Format(result.Diagnostics));
        Assert.Equal(["T.TB.Make$Integer", "T.TA.Make$"],
            result.Bodies[0].Statements.Cast<BoundCallStatement>().Select(c => c.Routine.LinkName));
    }

    private const string SplitJoinLike =
        "type TVarRec = record VType: Byte; VInteger: Integer end; " +              // as System declares it (rtl-abi.md §9.10), reduced
        "procedure P(const A: array of Char); overload; begin end; procedure P(const A: array of string); overload; begin end; " +
        "procedure J(const S: string; const A: array of string); overload; begin end; procedure J(const S: string; const A: array of const); overload; begin end; " +
        "type TCharSet = set of Char; procedure Q(S: TCharSet); overload; begin end; procedure Q(const A: array of string); overload; begin end;";

    /// <summary>
    /// C-458 (LANG-442, paragraph on array constructors): a bracket constructor against open arrays of different element types is
    /// bound once per type; a tie at cost 3 is broken by the element costs (<c>','</c> → <c>Char</c> beats → <c>string</c>),
    /// <c>array of const</c> loses against every typed array whose form binds, <c>[]</c> between two typed arrays stays ambiguous and a
    /// set parameter next to one open-array type keeps the set meaning, as before (LANG-273 decision 3).
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-442")]
    [InlineData("P([','])", "T.P$ca:Char")]
    [InlineData("P([', '])", "T.P$ca:string")]
    [InlineData("P([',', 'ab'])", "T.P$ca:string")]
    [InlineData("P([vch])", "T.P$ca:Char")]
    [InlineData("P([vs, vch])", "T.P$ca:string")]
    [InlineData("J(vs, ['a', 'b'])", "T.J$c:string,ca:string")]
    [InlineData("J(vs, [vs])", "T.J$c:string,ca:string")]
    [InlineData("J(vs, [1, 'a'])", "T.J$c:string,ca:const")]
    [InlineData("P([])", "E2122: Ambiguous overloaded call to 'P'")]
    [InlineData("Q(['a'])", "T.Q$T.TCharSet")]
    [InlineData("Q(['ab'])", "E2013: Ordinal type required")]          // one open-array type: the set meaning stays (LANG-273 decision 3)
    public void BracketConstructor_OpenArraysOfDifferentElementTypes(string call, string expected) => Assert.Equal(expected, Chosen(SplitJoinLike, call));
}
