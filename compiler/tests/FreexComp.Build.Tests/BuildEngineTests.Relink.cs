using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>C-294 (CMP-155, CMP-1142): a build that ends between its back-end jobs and the link leaves nothing the next build takes as linked.</summary>
public sealed partial class BuildEngineTests
{
    private const string GreetingPath = @"C:\p\Greeting.pas";

    private static string GreetingUnit(string text) =>
        $"unit Greeting; interface function Text: string; implementation function Text: string; begin Result := '{text}'; end; end.";

    /// <summary>
    /// A unit is edited and the build is cancelled when that unit's compile job — the only job — has ended successfully, before the
    /// link: the unit's <c>.obj</c> and <c>.pcu</c> are complete and current, the module is the one linked from the old object. The
    /// next build compiles nothing and must link; the one after it is up to date. For a program and for a library.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "CMP-155")]
    [Trait("Req", "CMP-1142")]
    public async Task CancelBeforeLink_NextBuildRelinks(bool library)
    {
        var w = library
            ? new BuildWorkspace(
                "library Hello; uses Greeting; function Size: Integer; stdcall; begin Result := Length(Text) end; exports Size; begin end.",
                BuildWorkspace.ProjectJson("Hello", extra: "\"appType\": \"library\""))
            : new BuildWorkspace("program Hello; uses Greeting; begin Writeln(Text) end.");
        var module = library ? Dll : BuildWorkspace.Exe;
        w.Fs.Add(GreetingPath, GreetingUnit("hi"));
        var (first, firstLog) = await w.RunAsync(w.Request());
        Assert.True(first.Success, string.Join("\n", firstLog.Lines));
        Assert.True(w.Fs.FileExists(module));
        var linksBefore = w.Tools.Calls.Count(c => c == "lld-link.exe");

        // the edit, and a build cancelled when the only compile job has finished
        w.Fs.Add(GreetingPath, GreetingUnit("ho"));
        using var cts = new CancellationTokenSource();
        var runner = new JobRunner(w.Tools) { Finished = job => cts.Cancel() };
        var cancelledLog = new EventLog();
        var cancelled = await new BuildEngine(w.Fs, runner).RunAsync(w.Request(), cancelledLog, cts.Token).WaitAsync(SafetyNet, TestContext.Current.CancellationToken);

        Assert.True(cancelled.Canceled, string.Join("\n", cancelledLog.Lines));
        Assert.Equal(["Greeting"], runner.Started);
        Assert.Equal(["Greeting"], runner.Ended);
        Assert.Equal(linksBefore, w.Tools.Calls.Count(c => c == "lld-link.exe"));                 // cancelled before the link
        Assert.True(w.Fs.FileExists(@"C:\p\obj\Debug\Greeting.obj") && w.Fs.FileExists(@"C:\p\obj\Debug\Greeting.pcu"));   // the job's outputs are kept

        // the next build has nothing to compile, but the module was linked from the old object
        var (next, nextLog) = await w.RunAsync(w.Request());

        Assert.True(next.Success, string.Join("\n", nextLog.Lines));
        Assert.False(next.UpToDate);
        Assert.Equal(["Linking " + Path.GetFileName(module)], nextLog.Info);
        Assert.Equal(linksBefore + 1, w.Tools.Calls.Count(c => c == "lld-link.exe"));
        Assert.True(w.Fs.FileExists(module));

        var (again, againLog) = await w.RunAsync(w.Request());
        Assert.True(again.UpToDate);
        Assert.Equal(["Hello: up to date"], againLog.Info);
    }
}
