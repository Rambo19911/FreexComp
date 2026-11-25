using System.Collections.Immutable;

namespace FreexComp.Build.Tests;

/// <summary>
/// C-337: stop at the first failed project (group-build.md CMP-1340…1344) and the named proofs of clean / rebuild of a group
/// (CMP-1349).
/// </summary>
public sealed partial class GroupBuildTests
{
    private const string Broken = "begin Foo := 1 end.";

    /// <summary>Four projects planned as <c>First, Bad, Dep, Other</c>: <c>Bad</c> does not compile, <c>Dep</c> depends on it, <c>Other</c> on nothing.</summary>
    private static BuildWorkspace FourWithOneFailing()
    {
        var w = new BuildWorkspace();
        AddProject(w, "First");
        AddProject(w, "Bad", program: "program Bad; " + Broken);
        AddProject(w, "Dep", ["../Bad/Bad.pproj"]);
        AddProject(w, "Other");
        AddGroup(w, "Other", "First", "Bad", "Dep", "Other");
        return w;
    }

    private static string ExeOf(string name) => $@"C:\g\{name}\bin\Debug\{name}.exe";

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1341")]
    [Trait("Req", "CMP-1342")]
    [Trait("Req", "CMP-1344")]
    public async Task StopOnFirstFailure_StartsNothingAfterTheFailure_ReportsSkipped()
    {
        // what the first two projects alone start
        var reference = new BuildWorkspace();
        AddProject(reference, "First");
        AddProject(reference, "Bad", program: "program Bad; " + Broken);
        AddGroup(reference, "Bad", "First", "Bad");
        await reference.RunAsync(reference.Request(path: GroupPath));
        var w = FourWithOneFailing();

        var (result, log) = await w.RunAsync(w.Request(path: GroupPath) with { StopOnFirstFailure = true });

        Assert.False(result.Success);
        Assert.Equal(["First", "Bad", "Dep", "Other"], BuiltOrder(log));                 // every planned project exactly once
        Assert.Equal(reference.Tools.Calls.Order(StringComparer.Ordinal), w.Tools.Calls.Order(StringComparer.Ordinal));
        Assert.Equal(1, w.Tools.Calls.Count(c => c == "lld-link.exe"));
        var lines = log.Lines;
        Assert.Equal(
            [
                "started Dep.pproj Debug win64 Build", "info Dep: skipped because the build stopped after Bad failed", "finished Dep.pproj success=False upToDate=False 0/0/0",
                "started Other.pproj Debug win64 Build", "info Other: skipped because the build stopped after Bad failed", "finished Other.pproj success=False upToDate=False 0/0/0",
            ],
            lines[^6..]);
        Assert.Equal(4, log.Events.OfType<BuildEvent.Finished>().Count());
        Assert.Equal(
            [@"C:\g\First\First.pproj", @"C:\g\Bad\Bad.pproj", @"C:\g\Dep\Dep.pproj", @"C:\g\Other\Other.pproj"],
            result.Projects.Select(p => p.ProjectPath));
        Assert.Equal([true, false, false, false], result.Projects.Select(p => p.Success));
        Assert.All(result.Projects[2..], p =>
        {
            Assert.False(p.UpToDate);
            Assert.Null(p.OutputPath);
            Assert.Empty(p.Diagnostics);
        });
        Assert.NotEmpty(result.Projects[1].Diagnostics);
        Assert.Equal(1, result.Errors);
        Assert.True(w.Fs.FileExists(ExeOf("First")));
        Assert.False(w.Fs.FileExists(ExeOf("Other")));
        Assert.False(w.Fs.FileExists(ExeOf("Dep")));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1340")]
    public async Task StopOnFirstFailure_Off_IsTodaysBehaviour()
    {
        var w = FourWithOneFailing();
        var request = w.Request(path: GroupPath);
        Assert.False(request.StopOnFirstFailure);
        Assert.False(new BuildRequest { ProjectPath = GroupPath }.StopOnFirstFailure);

        var (result, log) = await w.RunAsync(request);

        Assert.False(result.Success);
        Assert.Equal(["First", "Bad", "Dep", "Other"], BuiltOrder(log));
        Assert.Equal(["Dep: skipped because Bad failed"], log.Info.Where(l => l.Contains("skipped", StringComparison.Ordinal)));
        Assert.Equal([true, false, false, true], result.Projects.Select(p => p.Success));
        Assert.True(w.Fs.FileExists(ExeOf("Other")));
        Assert.Equal(ExeOf("Other"), result.Projects[3].OutputPath);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1343")]
    public async Task StopOnFirstFailure_AppliesToCleanAndRebuild()
    {
        var w = new BuildWorkspace();
        AddProject(w, "A");
        AddProject(w, "B");
        AddProject(w, "C");
        AddGroup(w, "C", "A", "B", "C");
        Assert.True((await w.RunAsync(w.Request(path: GroupPath))).Result.Success);
        var calls = w.Tools.Calls.Length;

        // Clean: the output of the first project is held by another process (E4008)
        var locked = new OutputLockedTests.LockedFileSystem(w.Fs, ExeOf("A"));
        var (clean, cleanLog) = await w.RunAsync(w.Request(BuildTarget.Clean, GroupPath) with { StopOnFirstFailure = true }, fs: locked);

        Assert.False(clean.Success);
        Assert.Equal(
            [
                "started A.pproj Debug win64 Clean", "diagnostic E4008", "finished A.pproj success=False upToDate=False 1/0/0",
                "started B.pproj Debug win64 Clean", "info B: skipped because the build stopped after A failed", "finished B.pproj success=False upToDate=False 0/0/0",
                "started C.pproj Debug win64 Clean", "info C: skipped because the build stopped after A failed", "finished C.pproj success=False upToDate=False 0/0/0",
            ],
            cleanLog.Lines);
        Assert.True(w.Fs.FileExists(ExeOf("B")));                                        // nothing of a skipped project is removed
        Assert.True(w.Fs.FileExists(ExeOf("C")));
        Assert.True(w.Fs.FileExists(@"C:\g\B\obj\Debug\B.obj"));

        // the same clean without the switch goes on with the other projects
        var (cleanAll, _) = await w.RunAsync(w.Request(BuildTarget.Clean, GroupPath), fs: locked);
        Assert.Equal([false, true, true], cleanAll.Projects.Select(p => p.Success));
        Assert.False(w.Fs.FileExists(ExeOf("B")));
        Assert.Equal(calls, w.Tools.Calls.Length);                                       // Clean starts no process

        // Rebuild: the first project no longer compiles
        Assert.True((await w.RunAsync(w.Request(path: GroupPath))).Result.Success);
        w.Fs.Add(@"C:\g\A\A.pas", "program A; " + Broken);
        var links = w.Tools.Calls.Count(c => c == "lld-link.exe");

        var (rebuild, rebuildLog) = await w.RunAsync(w.Request(BuildTarget.Rebuild, GroupPath) with { StopOnFirstFailure = true });

        Assert.False(rebuild.Success);
        Assert.Equal(
            ["started A.pproj Debug win64 Rebuild", "started B.pproj Debug win64 Rebuild", "started C.pproj Debug win64 Rebuild"],
            rebuildLog.Lines.Where(l => l.StartsWith("started", StringComparison.Ordinal)));
        Assert.Equal(
            ["B: skipped because the build stopped after A failed", "C: skipped because the build stopped after A failed"],
            rebuildLog.Info.Where(l => l.Contains("skipped", StringComparison.Ordinal)));
        Assert.Equal(links, w.Tools.Calls.Count(c => c == "lld-link.exe"));
        Assert.True(w.Fs.FileExists(ExeOf("B")));                                        // a skipped project is not cleaned either
        Assert.Equal([false, false, false], rebuild.Projects.Select(p => p.Success));
    }

    /// <summary>CMP-1343: the build part of <c>Run</c> — the program is not started and the requested project is reported as skipped.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1343")]
    public async Task StopOnFirstFailure_AppliesToTheBuildPartOfRun()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../Lib/Lib.pproj", "../Lib2/Lib2.pproj"]);
        AddProject(w, "Lib", program: "program Lib; " + Broken);
        AddProject(w, "Lib2");
        AddGroup(w, "App", "App", "Lib", "Lib2");

        var (result, log) = await w.RunAsync(w.Request(BuildTarget.Run, GroupPath) with { StopOnFirstFailure = true });

        Assert.False(result.Success);
        Assert.Equal(
            ["Lib2: skipped because the build stopped after Lib failed", "App: skipped because the build stopped after Lib failed"],
            log.Info.Where(l => l.Contains("skipped", StringComparison.Ordinal)));
        Assert.Contains("started App.pproj Debug win64 Run", log.Lines);
        Assert.Empty(w.Tools.Programs);
        Assert.Null(result.RunExitCode);
        Assert.False(w.Fs.FileExists(ExeOf("Lib2")));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1349")]
    public async Task Group_Clean_RemovesOutputsOfEveryProject()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../Lib/Lib.pproj"]);
        AddProject(w, "Lib");
        AddProject(w, "Tool");
        AddGroup(w, "App", "App", "Lib", "Tool");
        Assert.True((await w.RunAsync(w.Request(path: GroupPath))).Result.Success);
        ImmutableArray<string> names = ["Lib", "Tool", "App"];
        Assert.All(names, n => Assert.True(w.Fs.FileExists(ExeOf(n)) && w.Fs.FileExists($@"C:\g\{n}\obj\Debug\{n}.obj")));
        w.Fs.Add(@"C:\g\Tool\obj\Release\Tool.obj", "keep");                             // another configuration is not touched
        var calls = w.Tools.Calls.Length;

        var (result, log) = await w.RunAsync(w.Request(BuildTarget.Clean, GroupPath));

        Assert.True(result.Success, string.Join("\n", log.Lines));
        Assert.Equal(
            [
                "started Lib.pproj Debug win64 Clean", "finished Lib.pproj success=True upToDate=False 0/0/0",
                "started Tool.pproj Debug win64 Clean", "finished Tool.pproj success=True upToDate=False 0/0/0",
                "started App.pproj Debug win64 Clean", "finished App.pproj success=True upToDate=False 0/0/0",
            ],
            log.Lines);
        Assert.All(names, n =>
        {
            Assert.False(w.Fs.FileExists(ExeOf(n)));
            Assert.Empty(w.Fs.EnumerateFiles($@"C:\g\{n}\obj\Debug", "*"));
            Assert.Empty(w.Fs.EnumerateDirectories($@"C:\g\{n}\obj\Debug"));
            Assert.True(w.Fs.FileExists($@"C:\g\{n}\{n}.pas"));
        });
        Assert.True(w.Fs.FileExists(@"C:\g\Tool\obj\Release\Tool.obj"));
        Assert.Equal(calls, w.Tools.Calls.Length);                                       // Clean starts no process
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1349")]
    public async Task Group_Rebuild_RebuildsEveryProjectInOrder()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../Lib/Lib.pproj"]);
        AddProject(w, "Lib");
        AddProject(w, "Tool");
        AddGroup(w, "App", "App", "Lib", "Tool");
        Assert.True((await w.RunAsync(w.Request(path: GroupPath))).Result.Success);
        Assert.True((await w.RunAsync(w.Request(path: GroupPath))).Result.UpToDate);
        var links = w.Tools.Calls.Count(c => c == "lld-link.exe");

        var (result, log) = await w.RunAsync(w.Request(BuildTarget.Rebuild, GroupPath));

        Assert.True(result.Success, string.Join("\n", log.Lines));
        Assert.False(result.UpToDate);
        Assert.Equal(
            ["started Lib.pproj Debug win64 Rebuild", "started Tool.pproj Debug win64 Rebuild", "started App.pproj Debug win64 Rebuild"],
            log.Lines.Where(l => l.StartsWith("started", StringComparison.Ordinal)));
        Assert.Equal(["Linking Lib.exe", "Linking Tool.exe", "Linking App.exe"], log.Info.Where(l => l.StartsWith("Linking", StringComparison.Ordinal)));
        Assert.Equal(["Compiling Lib", "Compiling Tool", "Compiling App"], log.Info.Where(l => l.StartsWith("Compiling", StringComparison.Ordinal) && l != "Compiling System"));
        Assert.Equal(links + 3, w.Tools.Calls.Count(c => c == "lld-link.exe"));
        Assert.Equal([false, false, false], result.Projects.Select(p => p.UpToDate));
        Assert.Equal([ExeOf("Lib"), ExeOf("Tool"), ExeOf("App")], result.Projects.Select(p => p.OutputPath));
        Assert.DoesNotContain(log.Info, l => l.EndsWith("up to date", StringComparison.Ordinal));
    }
}
