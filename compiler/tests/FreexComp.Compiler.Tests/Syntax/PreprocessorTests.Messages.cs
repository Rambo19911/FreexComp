using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>{$MESSAGE} (LANG-865).</summary>
public sealed partial class PreprocessorTests
{
    [Theory]
    [Trait("Req", "LANG-865")]
    [InlineData("{$MESSAGE HINT 'Check this'}", "H1100", DiagnosticSeverity.Hint, "Check this")]
    [InlineData("{$MESSAGE WARN 'Careful'}", "W1100", DiagnosticSeverity.Warning, "Careful")]
    [InlineData("{$MESSAGE ERROR 'Broken'}", "E1210", DiagnosticSeverity.Error, "Broken")]
    [InlineData("{$MESSAGE FATAL 'Stop'}", "E1211", DiagnosticSeverity.Error, "Stop")]
    [InlineData("{$MESSAGE 'Plain'}", "H1100", DiagnosticSeverity.Hint, "Plain")]
    [InlineData("{$message warn 'lower'}", "W1100", DiagnosticSeverity.Warning, "lower")]
    [InlineData("{$MESSAGE WARN 'It''s'}", "W1100", DiagnosticSeverity.Warning, "It's")]
    [InlineData("{$MESSAGE WARN ''}", "W1100", DiagnosticSeverity.Warning, "")]
    [InlineData("{$MESSAGE HINT unquoted text}", "H1100", DiagnosticSeverity.Hint, "unquoted text")]
    [InlineData("(*$MESSAGE ERROR 'In parens'*)", "E1210", DiagnosticSeverity.Error, "In parens")]
    public void Messages_ReportTheTextWithTheKindsCode(string directive, string code, DiagnosticSeverity severity, string message)
    {
        var d = Assert.Single(Diagnose("x " + directive));

        Assert.Equal(code, d.Code);
        Assert.Equal(severity, d.Severity);
        Assert.Equal(message, d.Message);
        Assert.Equal(new TextSpan(2, directive.Length), d.Location.Span);
    }

    [Fact]
    [Trait("Req", "LANG-865")]
    public void Messages_InInactiveBranch_AreNotReported()
    {
        Assert.Empty(Diagnose("{$IFDEF NOPE}{$MESSAGE ERROR 'no'}{$ENDIF}"));
        Assert.Equal(["E1210"], Diagnose("{$IFNDEF NOPE}{$MESSAGE ERROR 'yes'}{$ENDIF}").Select(d => d.Code));
    }

    [Fact]
    [Trait("Req", "LANG-865")]
    public void Messages_FollowWarningControl()
    {
        Assert.Empty(Diagnose("{$WARNINGS OFF}{$MESSAGE WARN 'w'}"));
        Assert.Empty(Diagnose("{$HINTS OFF}{$MESSAGE 'h'}"));
        Assert.Equal(["E1210"], Diagnose("{$WARNINGS OFF}{$HINTS OFF}{$MESSAGE ERROR 'e'}").Select(d => d.Code));
        Assert.Equal(
            DiagnosticSeverity.Error,
            Assert.Single(Diagnose("{$WARN USER_MESSAGE ERROR}{$MESSAGE WARN 'w'}")).Severity);
    }

    [Fact]
    [Trait("Req", "LANG-865")]
    public void Messages_InvalidKind_IsE1206()
    {
        Assert.Equal(["E1206"], Diagnose("{$MESSAGE LOUD 'x'}").Select(d => d.Code));
    }

    [Fact]
    [Trait("Req", "LANG-865")]
    public void Messages_FatalStillLexesTheRestOfTheFile()
    {
        Assert.Equal(["a", "b"], Tokenize("a {$MESSAGE FATAL 'x'} b").SkipLast(1).Select(t => t.Text));
    }
}
