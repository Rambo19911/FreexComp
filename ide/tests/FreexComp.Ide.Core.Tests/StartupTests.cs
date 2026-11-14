using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Tests;

/// <summary>The command line before any window exists (phase1-shell.md §2).</summary>
[Trait("Phase", "1")]
public sealed class StartupTests
{
    /// <summary>The usage block of phase1-shell.md §2.</summary>
    private const string Usage = "FreexComp.exe [<path> …] [--new-window] [--user-dir <dir>] [--safe] [--version] [--help]";

    private static (int? Code, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = StartupOptions.Parse(args, @"C:\w").HandleInformationOptions(output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    [Trait("Req", "IDE-P1-013")]
    [Trait("Req", "IDE-P1-010")]
    [Trait("Req", "IDE-P1-014")]
    public void VersionAndHelpExitWithoutWindow()
    {
        var nl = Environment.NewLine;
        Assert.Equal(Usage, Strings.App_Usage);

        // --version: "FreexComp <version>" on stdout, exit code 0; --help: the usage block. Paths do not matter.
        Assert.Matches(@"^\d+\.\d+", StartupOptions.Version);
        Assert.Equal((0, $"FreexComp {StartupOptions.Version}{nl}", ""), Run("--version"));
        Assert.Equal((0, $"FreexComp {StartupOptions.Version}{nl}", ""), Run("a.pas", "--VERSION"));
        Assert.Equal((0, Usage + nl, ""), Run("--help"));
        Assert.Equal((0, Usage + nl, ""), Run("--version", "--help"));

        // An unknown option (or --user-dir without its value): the usage on stderr, exit code 1.
        Assert.Equal((1, "", Usage + nl), Run("--bogus"));
        Assert.Equal((1, "", Usage + nl), Run("a.pas", "--help", "--user-dir"));

        // Otherwise the IDE starts: paths made absolute against the working directory, --new-window ignored.
        Assert.Equal((null, "", ""), Run("a.pas", "--safe", "--new-window", "--user-dir", @"C:\u"));
        var options = StartupOptions.Parse(["sub/a.pas", "--Safe", @"D:\p\P.pproj", "--new-window"], @"C:\w");
        Assert.Equal([@"C:\w\sub\a.pas", @"D:\p\P.pproj"], options.Paths);
        Assert.True(options.Safe);
        Assert.Null(options.UnknownOption);
        Assert.False(StartupOptions.Parse(["a.pas"], @"C:\w").Safe);
    }
}
