using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>C-154: request overrides and result helpers (build-engine.md CMP-805, CMP-807).</summary>
[Trait("Phase", "3")]
public sealed class BuildRequestOverrideTests
{
    private const string UsesUV = "program Hello; uses U, V; begin Writeln(Who, Other) end.";

    [Fact]
    [Trait("Req", "CMP-805")]
    public async Task ExtraSearchPaths_AreSearchedAfterTheProjectSearchPaths()
    {
        var w = new BuildWorkspace(UsesUV, BuildWorkspace.ProjectJson("Hello", extra: """ "searchPaths": ["lib"]"""));
        w.Fs.Add(@"C:\p\lib\U.pas", "unit U; interface const Who = 'project'; implementation end.");
        w.Fs.Add(@"C:\extra\U.pas", "unit U; interface const Who = ; implementation end.");          // never read: lib wins
        w.Fs.Add(@"C:\extra\V.pas", "unit V; interface const Other = 'extra'; implementation end.");

        var (without, withoutLog) = await w.RunAsync(w.Request());
        var (with, withLog) = await w.RunAsync(w.Request() with { ExtraSearchPaths = [@"C:\extra"] });

        Assert.False(without.Success);
        Assert.Contains(withoutLog.Events.OfType<BuildEvent.Diagnostic>(), d => d.Value.Code == "E2202" && d.Value.Message.Contains("'V'", StringComparison.Ordinal));
        Assert.True(with.Success, string.Join("\n", withLog.Lines));
        Assert.Equal(["Compiling Hello", "Compiling System", "Compiling U", "Compiling V"], withLog.Info[..^1].Order(StringComparer.Ordinal));
        Assert.Equal("Linking Hello.exe", withLog.Info[^1]);
    }

    [Fact]
    [Trait("Req", "CMP-805")]
    public async Task OpenFiles_OverrideTheDiskContent_ForThisBuildOnly()
    {
        var w = new BuildWorkspace("program Hello; begin Foo := 1 end.");

        var (edited, _) = await w.RunAsync(w.Request() with { OpenFiles = [new SourceText(BuildWorkspace.MainPath, BuildWorkspace.HelloProgram)] });
        var (disk, diskLog) = await w.RunAsync(w.Request());

        Assert.True(edited.Success);
        Assert.Equal("program Hello; begin Foo := 1 end.", w.Fs.ReadAllText(BuildWorkspace.MainPath));
        Assert.False(disk.Success);
        Assert.Contains("diagnostic E2003", diskLog.Lines);
    }

    [Fact]
    [Trait("Req", "CMP-805")]
    public async Task OpenFiles_MainSourceOnlyInAnEditorBuffer_IsNotE9004()
    {
        var w = new BuildWorkspace();
        w.Fs.Delete(BuildWorkspace.MainPath);

        var (result, log) = await w.RunAsync(w.Request() with { OpenFiles = [new SourceText(BuildWorkspace.MainPath, BuildWorkspace.HelloProgram)] });

        Assert.True(result.Success, string.Join("\n", log.Lines));
    }

    [Theory]
    [Trait("Req", "CMP-805")]
    [InlineData("win32")]
    [InlineData("linux64")]
    public async Task Platform_OtherThanWin64_IsE9002(string platform)
    {
        var w = new BuildWorkspace();

        var (result, log) = await w.RunAsync(w.Request() with { Platform = platform });

        Assert.False(result.Success);
        Assert.Equal($"started Hello.pproj Debug {platform} Build", log.Lines[0]);
        Assert.Equal($@"C:\p\Hello.pproj(0,0): error E9002: Invalid project file: unsupported platform '{platform}'", log.Events.OfType<BuildEvent.Diagnostic>().Single().Value.ToText());
        Assert.Empty(w.Tools.Calls);
    }

    [Fact]
    [Trait("Req", "CMP-805")]
    public async Task Platform_Null_IsTheFirstEntryOfTheProjectsPlatforms()
    {
        var w = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: """ "platforms": ["win32", "win64"]"""));

        var (result, log) = await w.RunAsync(w.Request());
        var (explicitWin64, _) = await w.RunAsync(w.Request() with { Platform = "win64" });

        Assert.False(result.Success);
        Assert.Equal("started Hello.pproj Debug win32 Build", log.Lines[0]);
        Assert.True(explicitWin64.Success);
    }

    [Fact]
    [Trait("Req", "CMP-805")]
    public async Task RtlDirectory_Override_ReplacesTheDefaultLookup()
    {
        var w = new BuildWorkspace();

        var (without, withoutLog) = await w.RunAsync(w.Request() with { RtlDirectory = null });
        var (with, _) = await w.RunAsync(w.Request());

        Assert.False(without.Success);
        Assert.Contains("diagnostic E2202", withoutLog.Lines);                       // System not found by the default lookup
        Assert.True(with.Success);
    }

    [Fact]
    [Trait("Req", "CMP-805")]
    public async Task ToolchainDirectory_Override_ReplacesTheDefaultLookup()
    {
        var w = new BuildWorkspace();

        var (with, _) = await w.RunAsync(w.Request());
        var (without, withoutLog) = await w.RunAsync(w.Request() with { ToolchainDirectory = null, Target = BuildTarget.Rebuild });

        Assert.True(with.Success);
        Assert.False(without.Success);
        Assert.Contains("diagnostic E4001", withoutLog.Lines);
    }

    [Fact]
    [Trait("Req", "CMP-807")]
    public async Task UpToDate_IsSuccessAndEveryProjectUpToDate()
    {
        var w = new BuildWorkspace();

        var (first, _) = await w.RunAsync(w.Request());
        var (second, _) = await w.RunAsync(w.Request());
        w.Fs.Add(BuildWorkspace.MainPath, "program Hello; begin Foo := 1 end.");
        var (failed, _) = await w.RunAsync(w.Request());

        Assert.False(first.UpToDate);
        Assert.True(second.UpToDate);
        Assert.False(failed.UpToDate);
    }

    [Fact]
    [Trait("Req", "CMP-807")]
    public async Task OutputPath_IsThatOfTheLastProject()
    {
        var w = new BuildWorkspace();

        var (built, _) = await w.RunAsync(w.Request());
        w.Fs.Add(BuildWorkspace.MainPath, "program Hello; begin Foo := 1 end.");
        var (failed, _) = await w.RunAsync(w.Request());

        Assert.Equal(BuildWorkspace.Exe, built.OutputPath);
        Assert.Null(failed.OutputPath);
    }
}
