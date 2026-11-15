using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Tests.ViewModels;

/// <summary>IDE-P3-075 / UI-TW-503: the one place that reads <c>&lt;abs-path&gt;(&lt;line&gt;,&lt;col&gt;)</c> from Output text.</summary>
[Trait("Phase", "3")]
public sealed class OutputLocationTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-075")]
    public void ParsesAbsolutePathsLineColumnAndSeverity()
    {
        var diag = OutputLocation.TryParse(@"C:\Work\Hello\Unit1.pas(12,5): error E2003: Undeclared identifier 'Foo'");
        Assert.Equal(new OutputLocation(@"C:\Work\Hello\Unit1.pas", 12, 5, 0, 23, "error"), diag);

        var inText = OutputLocation.TryParse(@"note: see C:\Program Files (x86)\Lib\A.pas(3,14) for details");
        Assert.Equal(@"C:\Program Files (x86)\Lib\A.pas", inText!.Path);
        Assert.Equal((3, 14, 10, null), (inText.Line, inText.Column, inText.PathStart, inText.Severity));

        Assert.Equal(@"\\server\share\B.pas", OutputLocation.TryParse(@"\\server\share\B.pas(1,1): hint H2077: x")!.Path);
        Assert.Null(OutputLocation.TryParse("Unit1.pas(12,5): error E2003: relative paths are not navigable"));
        Assert.Null(OutputLocation.TryParse("Build succeeded"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-TW-502")]
    public void ColorTokensFollowChannelKindAndSeverity()
    {
        Assert.Equal("status.warning", OutputViewModel.ColorToken(OutputChannels.Build, new(@"C:\a.pas(1,2): warning W1002: x", OutputKind.Normal)));
        Assert.Equal("status.hint", OutputViewModel.ColorToken(OutputChannels.Build, new(@"C:\a.pas(1,2): hint H1: x", OutputKind.Normal)));
        Assert.Equal("text.primary", OutputViewModel.ColorToken(OutputChannels.Program, new(@"C:\a.pas(1,2): warning W1002: x", OutputKind.Normal)));
        Assert.Equal("text.tertiary", OutputViewModel.ColorToken(OutputChannels.Build, new("> clang.exe ...", OutputKind.Command)));
        Assert.Equal("status.success", OutputViewModel.ColorToken(OutputChannels.Build, new("Build succeeded", OutputKind.Success)));
        Assert.Equal("status.error", OutputViewModel.ColorToken(OutputChannels.Build, new("Build failed: 1 error", OutputKind.Failure)));
        Assert.Equal("status.error", OutputViewModel.ColorToken(OutputChannels.Program, new("oops", OutputKind.Error)));
        Assert.Equal("text.tertiary", OutputViewModel.ColorToken(OutputChannels.Program, new("Program started", OutputKind.Ide)));
    }
}
