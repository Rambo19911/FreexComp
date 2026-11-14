using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>directives.md §1 and §5: switch table, DirectiveState per token, unknown directives (LANG-850…853, 880).</summary>
public sealed partial class PreprocessorTests
{
    /// <summary>The DirectiveState carried by the first token whose text is <paramref name="token"/>.</summary>
    private static DirectiveState StateOf(string text, string token) =>
        Tokenize(text).First(t => t.Text == token).DirectiveState;

    private static string[] Codes(string text, params string[] defines) => [.. Diagnose(text, defines).Select(d => d.Code)];

    [Fact]
    [Trait("Req", "LANG-852")]
    public void Switches_DefaultState_MatchesTheTable()
    {
        var s = StateOf("x", "x");

        Assert.Equal(DirectiveState.Default, s);
        Assert.False(s.RangeChecks);
        Assert.False(s.OverflowChecks);
        Assert.False(s.BoolEval);
        Assert.True(s.Assertions);
        Assert.True(s.IoChecks);
        Assert.False(s.TypeInfo);
        Assert.Equal(8, s.Align);
        Assert.Equal(1, s.MinEnumSize);
        Assert.False(s.PointerMath);
        Assert.False(s.ScopedEnums);
        Assert.True(s.WarningsOn);
        Assert.True(s.HintsOn);
        Assert.Empty(s.WarnOverrides);
        Assert.Null(s.Rtti);
        Assert.Equal(DirectiveState.Default, default(SyntaxToken).DirectiveState);
    }

    [Fact]
    [Trait("Req", "LANG-852")]
    public void Switches_TakeEffectFromTheirPosition()
    {
        const string text = "a {$R+} b {$R-} c";

        Assert.False(StateOf(text, "a").RangeChecks);
        Assert.True(StateOf(text, "b").RangeChecks);
        Assert.False(StateOf(text, "c").RangeChecks);
    }

    [Fact]
    [Trait("Req", "LANG-852")]
    public void Switches_UnchangedState_IsTheSameInstance()
    {
        var tokens = Tokenize("a b {$WARNINGS ON} c {$R+} d e");

        Assert.Same(tokens[0].DirectiveState, tokens[1].DirectiveState);
        Assert.Same(tokens[3].DirectiveState, tokens[4].DirectiveState);
    }

    [Theory]
    [Trait("Req", "LANG-850")]
    [InlineData("{$R+}", nameof(DirectiveState.RangeChecks), true)]
    [InlineData("{$RANGECHECKS ON}", nameof(DirectiveState.RangeChecks), true)]
    [InlineData("{$rangechecks on}", nameof(DirectiveState.RangeChecks), true)]
    [InlineData("{$Q+}", nameof(DirectiveState.OverflowChecks), true)]
    [InlineData("{$OVERFLOWCHECKS ON}", nameof(DirectiveState.OverflowChecks), true)]
    [InlineData("{$B+}", nameof(DirectiveState.BoolEval), true)]
    [InlineData("{$BOOLEVAL ON}", nameof(DirectiveState.BoolEval), true)]
    [InlineData("{$C-}", nameof(DirectiveState.Assertions), false)]
    [InlineData("{$ASSERTIONS OFF}", nameof(DirectiveState.Assertions), false)]
    [InlineData("{$I-}", nameof(DirectiveState.IoChecks), false)]
    [InlineData("{$IOCHECKS OFF}", nameof(DirectiveState.IoChecks), false)]
    [InlineData("{$M+}", nameof(DirectiveState.TypeInfo), true)]
    [InlineData("{$TYPEINFO ON}", nameof(DirectiveState.TypeInfo), true)]
    [InlineData("{$POINTERMATH ON}", nameof(DirectiveState.PointerMath), true)]
    [InlineData("{$SCOPEDENUMS ON}", nameof(DirectiveState.ScopedEnums), true)]
    [InlineData("{$WARNINGS OFF}", nameof(DirectiveState.WarningsOn), false)]
    [InlineData("{$HINTS OFF}", nameof(DirectiveState.HintsOn), false)]
    [InlineData("(*$R+*)", nameof(DirectiveState.RangeChecks), true)]
    public void Switches_BooleanSwitch_SetsItsState(string directive, string property, bool value)
    {
        var state = StateOf(directive + " x", "x");

        Assert.Empty(Diagnose(directive));
        Assert.Equal(value, typeof(DirectiveState).GetProperty(property)!.GetValue(state));
    }

    [Fact]
    [Trait("Req", "LANG-850")]
    public void Switches_CombinedShortSwitches_AllApply()
    {
        var state = StateOf("{$R+,Q+,B+,C-} x", "x");

        Assert.Equal((true, true, true, false), (state.RangeChecks, state.OverflowChecks, state.BoolEval, state.Assertions));
    }

    [Theory]
    [InlineData("{$A1}", 1)]
    [InlineData("{$A2}", 2)]
    [InlineData("{$A4}", 4)]
    [InlineData("{$A8}", 8)]
    [InlineData("{$A-}", 1)]
    [InlineData("{$A+}", 8)]
    [InlineData("{$ALIGN 4}", 4)]
    [InlineData("{$ALIGN ON}", 8)]
    [InlineData("{$ALIGN OFF}", 1)]
    public void Switches_Align(string directive, int align)
    {
        Assert.Empty(Diagnose(directive));
        Assert.Equal(align, StateOf(directive + " x", "x").Align);
    }

    [Theory]
    [InlineData("{$Z1}", 1)]
    [InlineData("{$Z2}", 2)]
    [InlineData("{$Z4}", 4)]
    [InlineData("{$MINENUMSIZE 4}", 4)]
    public void Switches_MinEnumSize(string directive, int size)
    {
        Assert.Empty(Diagnose(directive));
        Assert.Equal(size, StateOf(directive + " x", "x").MinEnumSize);
    }

    [Theory]
    [Trait("Req", "LANG-880")]
    [InlineData("{$RANGECHECKS YES}")]
    [InlineData("{$RANGECHECKS}")]
    [InlineData("{$BOOLEVAL +}")]
    [InlineData("{$ALIGN 3}")]
    [InlineData("{$A3}")]
    [InlineData("{$MINENUMSIZE 8}")]
    [InlineData("{$Z8}")]
    [InlineData("{$WARNINGS MAYBE}")]
    [InlineData("{$APPTYPE SERVICE}")]
    [InlineData("{$WARN W1001 SOMETIMES}")]
    [InlineData("{$WARN NO_SUCH_WARNING ON}")]
    public void Switches_InvalidArgument_IsE1206(string directive)
    {
        var d = Assert.Single(Diagnose(directive));

        Assert.Equal("E1206", d.Code);
        Assert.Equal(new TextSpan(0, directive.Length), d.Location.Span);
    }

    [Fact]
    [Trait("Req", "LANG-880")]
    public void Switches_InvalidArgument_LeavesTheStateUnchanged()
    {
        Assert.False(StateOf("{$RANGECHECKS YES} x", "x").RangeChecks);
    }

    [Theory]
    [InlineData("{$H-}", "E1205", "Directive 'H-' is not supported")]
    [InlineData("{$LONGSTRINGS OFF}", "E1205", "Directive 'LONGSTRINGS OFF' is not supported")]
    [InlineData("{$ZEROBASEDSTRINGS ON}", "E1205", "Directive 'ZEROBASEDSTRINGS ON' is not supported")]
    [InlineData("{$J+}", "W1002", "Directive 'J+' is not supported and was ignored")]
    [InlineData("{$WRITEABLECONST ON}", "W1002", "Directive 'WRITEABLECONST ON' is not supported and was ignored")]
    [InlineData("{$T+}", "W1002", "Directive 'T+' is not supported and was ignored")]
    [InlineData("{$TYPEDADDRESS ON}", "W1002", "Directive 'TYPEDADDRESS ON' is not supported and was ignored")]
    [InlineData("{$X-}", "W1002", "Directive 'X-' is not supported and was ignored")]
    [InlineData("{$EXTENDEDSYNTAX OFF}", "W1002", "Directive 'EXTENDEDSYNTAX OFF' is not supported and was ignored")]
    [InlineData("{$RTTI EXPLICIT METHODS([])}", "W1002", "Directive 'RTTI EXPLICIT METHODS([])' is not supported and was ignored")]
    [InlineData("{$R Resources.res}", "H1010", "Resource files are not supported yet; directive ignored")]
    [InlineData("{$RESOURCE 'My File.res'}", "H1010", "Resource files are not supported yet; directive ignored")]
    [InlineData("{$PUSHOPT}", "W1001", "Unknown directive 'PUSHOPT'")]
    [InlineData("{$POPOPT}", "W1001", "Unknown directive 'POPOPT'")]
    [InlineData("{$NOSUCHTHING 1}", "W1001", "Unknown directive 'NOSUCHTHING'")]
    [InlineData("{$}", "W1001", "Unknown directive ''")]
    public void Switches_UnsupportedOrUnknown_IsDiagnosedAsSpecified(string directive, string code, string message)
    {
        var d = Assert.Single(Diagnose("x " + directive));

        Assert.Equal(code, d.Code);
        Assert.Equal(message, d.Message);
        Assert.Equal(new TextSpan(2, directive.Length), d.Location.Span);
    }

    [Theory]
    [InlineData("{$H+}")]
    [InlineData("{$LONGSTRINGS ON}")]
    [InlineData("{$J-}")]
    [InlineData("{$T-}")]
    [InlineData("{$X+}")]
    [InlineData("{$ZEROBASEDSTRINGS OFF}")]
    [InlineData("{$O+}")]
    [InlineData("{$OPTIMIZATION OFF}")]
    [InlineData("{$D-}")]
    [InlineData("{$L+}")]
    [InlineData("{$Y-}")]
    [InlineData("{$DEBUGINFO ON}")]
    [InlineData("{$LOCALSYMBOLS ON}")]
    [InlineData("{$REFERENCEINFO OFF}")]
    [InlineData("{$W-}")]
    [InlineData("{$U+}")]
    [InlineData("{$G+}")]
    [InlineData("{$P+}")]
    [InlineData("{$V-}")]
    [InlineData("{$N+}")]
    [InlineData("{$E-}")]
    [InlineData("{$F+}")]
    [InlineData("{$K-}")]
    [InlineData("{$S-}")]
    [InlineData("{$APPTYPE CONSOLE}")]
    [InlineData("{$APPTYPE GUI}")]
    [InlineData("{$WARN W1041 OFF}")]
    [InlineData("{$WARN USE_BEFORE_DEF ERROR}")]
    [InlineData("{$REGION 'Helpers'}{$ENDREGION}")]
    [InlineData("{$FORM MainForm.form}")]
    [InlineData("{$RTTI BROAD}")]
    [InlineData("{$RTTI PUBLISHED}")]
    [InlineData("{$RTTI DEFAULT}")]
    [InlineData("{$L helper.obj}")]
    [InlineData("{$LINK 'my lib.def'}")]
    [InlineData("{$MINSTACKSIZE 65536}")]
    [InlineData("{$MAXSTACKSIZE 4194304}")]
    [InlineData("{$IMAGEBASE $400000}")]
    [InlineData("{$DESCRIPTION 'x'}")]
    [InlineData("{$LIBPREFIX 'lib'}")]
    [InlineData("{$LIBSUFFIX '64'}")]
    [InlineData("{$LIBVERSION '1'}")]
    [InlineData("{$WEAKPACKAGEUNIT ON}")]
    [InlineData("{$DENYPACKAGEUNIT ON}")]
    [InlineData("{$DESIGNONLY}")]
    [InlineData("{$RUNONLY}")]
    [InlineData("{$IMPLICITBUILD OFF}")]
    [InlineData("{$HPPEMIT 'x'}")]
    [InlineData("{$EXTERNALSYM Foo}")]
    [InlineData("{$NODEFINE Foo}")]
    [InlineData("{$NOINCLUDE Foo}")]
    [InlineData("{$OBJTYPENAME Foo 'x'}")]
    [InlineData("{$STACKFRAMES ON}")]
    [InlineData("{$SAFEDIVIDE OFF}")]
    [InlineData("{$VARSTRINGCHECKS ON}")]
    [InlineData("{$OPENSTRINGS ON}")]
    [InlineData("{$EXTENSION 'dll'}")]
    [InlineData("{$INLINE AUTO}")]
    [InlineData("{$EXCESSPRECISION OFF}")]
    [InlineData("{$HIGHCHARUNICODE ON}")]
    [InlineData("{$TEXTBLOCK NATIVE}")]
    public void Switches_AcceptedDirective_HasNoDiagnostic(string directive)
    {
        Assert.Empty(Diagnose(directive));
    }

    [Fact]
    [Trait("Req", "LANG-853")]
    public void Switches_UnknownDirective_IsIgnored()
    {
        Assert.Equal(DirectiveState.Default, StateOf("{$NOSUCHTHING ON} x", "x"));
    }

    [Fact]
    public void Switches_InInactiveBranch_DoNotApply()
    {
        Assert.False(StateOf("{$IFDEF NOPE}{$R+}{$ENDIF} x", "x").RangeChecks);
        Assert.Empty(Diagnose("{$IFDEF NOPE}{$NOSUCHTHING}{$H-}{$ENDIF}"));
    }

    [Fact]
    public void Switches_IfOpt_FollowsTheLongForm()
    {
        Assert.Equal(["a"], Active("{$RANGECHECKS ON}{$IFOPT R+} a {$ELSE} b {$ENDIF}"));
        Assert.Equal(["b"], Active("{$ALIGN 1}{$IFOPT A+} a {$ELSE} b {$ENDIF}"));
    }

    [Fact]
    public void Switches_AppType_DefinesOrUndefinesConsole()
    {
        Assert.Equal(["a"], Active("{$APPTYPE CONSOLE}{$IFDEF CONSOLE} a {$ENDIF}"));
        Assert.Equal(["b"], Active("{$APPTYPE GUI}{$IFDEF CONSOLE} a {$ELSE} b {$ENDIF}", "CONSOLE"));
    }

    [Theory]
    [InlineData("{$RTTI PUBLISHED} x", "published")]
    [InlineData("{$RTTI broad} x", "broad")]
    [InlineData("{$RTTI PUBLISHED}{$RTTI DEFAULT} x", null)]
    public void Switches_Rtti_SetsTheScope(string text, string? rtti)
    {
        Assert.Equal(rtti, StateOf(text, "x").Rtti);
    }

    /// <summary>C-169 / LANG-882: the scope holds until the next <c>{$RTTI}</c>; an unknown argument is W1002 and keeps the scope in force.</summary>
    [Fact]
    [Trait("Req", "LANG-882")]
    public void RttiDirective()
    {
        const string Text = "a {$RTTI PUBLISHED} b {$RTTI ALL} c {$RTTI BROAD} d {$RTTI DEFAULT} e";

        Assert.Equal([null, "published", "published", "broad", null], new[] { "a", "b", "c", "d", "e" }.Select(t => StateOf(Text, t).Rtti));
        Assert.Equal("W1002", Assert.Single(Diagnose(Text)).Code);
    }

    // ------------------------------------------------------------------ warning control (CMP-505)

    [Fact]
    [Trait("Req", "CMP-505")]
    public void Switches_WarningsOff_SuppressesLaterWarnings()
    {
        Assert.Empty(Codes("{$WARNINGS OFF}{$NOSUCH}"));
        Assert.Equal(["W1001"], Codes("{$NOSUCH}{$WARNINGS OFF}"));
        Assert.Equal(["W1001"], Codes("{$WARNINGS OFF}{$WARNINGS ON}{$NOSUCH}"));
    }

    [Fact]
    [Trait("Req", "CMP-505")]
    public void Switches_HintsOff_SuppressesLaterHints()
    {
        Assert.Empty(Codes("{$HINTS OFF}{$R x.res}"));
        Assert.Equal(["H1010"], Codes("{$R x.res}{$HINTS OFF}"));
    }

    [Theory]
    [Trait("Req", "CMP-505")]
    [InlineData("{$WARN W1001 OFF}{$NOSUCH}", new string[0])]
    [InlineData("{$WARN UNKNOWN_DIRECTIVE OFF}{$NOSUCH}", new string[0])]
    [InlineData("{$warn unknown_directive off}{$NOSUCH}", new string[0])]
    [InlineData("{$WARN W1001 OFF}{$WARN W1001 ON}{$NOSUCH}", new[] { "W1001" })]
    [InlineData("{$WARN W1002 OFF}{$NOSUCH}", new[] { "W1001" })]
    [InlineData("{$WARN RESOURCE_IGNORED OFF}{$R x.res}", new string[0])]
    public void Switches_Warn_TurnsOneDiagnosticOnOrOff(string text, string[] codes)
    {
        Assert.Equal(codes, Codes(text));
    }

    [Fact]
    [Trait("Req", "CMP-505")]
    public void Switches_WarnError_PromotesToError()
    {
        var d = Assert.Single(Diagnose("{$WARN UNKNOWN_DIRECTIVE ERROR}{$NOSUCH}"));

        Assert.Equal("W1001", d.Code);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal(@"C:\p\test.pas(1,32): error W1001: Unknown directive 'NOSUCH'", d.ToText());
    }

    [Fact]
    [Trait("Req", "CMP-505")]
    public void Switches_WarnOverrides_AreKeyedByCode()
    {
        var state = StateOf("{$WARN USE_BEFORE_DEF OFF}{$WARN w1042 ERROR} x", "x");

        Assert.Equal(WarnSetting.Off, state.WarnOverrides["W1041"]);
        Assert.Equal(WarnSetting.Error, state.WarnOverrides["W1042"]);
    }

    [Fact]
    public void Switches_WarnOverride_BeatsWarningsOff()
    {
        Assert.Equal(["W1001"], Codes("{$WARNINGS OFF}{$WARN W1001 ERROR}{$NOSUCH}"));
    }

    // ------------------------------------------------------------------ regions (W1004)

    [Theory]
    [InlineData("{$REGION 'a'}{$REGION 'b'}{$ENDREGION}{$ENDREGION}", new int[0])]
    [InlineData("{$REGION 'a'} x", new[] { 0 })]
    [InlineData("x {$ENDREGION}", new[] { 2 })]
    [InlineData("{$REGION}{$REGION}{$ENDREGION}", new[] { 0 })]
    public void Switches_UnbalancedRegion_IsW1004(string text, int[] at)
    {
        var diagnostics = Diagnose(text);

        Assert.All(diagnostics, d => Assert.Equal("W1004", d.Code));
        Assert.Equal(at, diagnostics.Select(d => d.Location.Span.Start));
    }
}
