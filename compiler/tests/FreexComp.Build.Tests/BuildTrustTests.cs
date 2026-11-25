using System.Collections.Immutable;

namespace FreexComp.Build.Tests;

/// <summary>C-324: the trust gate in front of project commands (project-trust.md CMP-1239…1246), in-memory file system, fake tools.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class BuildTrustTests
{
    private const string TrustFile = @"C:\user\roaming\trust.json";
    private const string Events = """ "events": { "preBuild": ["echo hi"], "postBuild": [] }""";
    private const string E9021 =
        @"C:\p\Hello.pproj(0,0): error E9021: Project 'C:\p\Hello.pproj' has build event commands and is not trusted (pbuild: --trust-events runs them, --no-events builds without them)";
    private const string W9001 = @"C:\p\Hello.pproj(0,0): warning W9001: Project events are not supported yet and were ignored";

    private static BuildWorkspace WithEvents() => new(projectJson: BuildWorkspace.ProjectJson("Hello", extra: Events));

    private static ImmutableArray<string> Diagnostics(EventLog log) => [.. log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText())];

    private static string Hash(BuildWorkspace w, string path = BuildWorkspace.ProjectPath) =>
        ProjectTrust.CommandsHash(ProjectSerializer.LoadProject(path, w.Fs).Value!);

    [Fact]
    [Trait("Req", "CMP-1239")]
    [Trait("Req", "CMP-1243")]
    [Trait("Req", "CMP-1246")]
    public async Task UntrustedProjectWithEvents_FailsWithE9021AndRunsNothing()
    {
        var w = WithEvents();

        var (noStore, noStoreLog) = await w.RunAsync(w.Request());
        var (emptyList, emptyListLog) = await w.RunAsync(w.Request() with { TrustStore = new FileTrustStore(w.Fs, TrustFile) });

        foreach (var (result, log) in new[] { (noStore, noStoreLog), (emptyList, emptyListLog) })
        {
            Assert.False(result.Success);
            Assert.Equal(1, result.Errors);
            Assert.Null(result.OutputPath);
            Assert.Equal(["started Hello.pproj Debug win64 Build", "diagnostic E9021", "finished Hello.pproj success=False upToDate=False 1/0/0"], log.Lines);
            Assert.Equal([E9021], Diagnostics(log));
        }

        Assert.Empty(w.Tools.Calls);                                            // no process was started
        Assert.Empty(w.Tools.Programs);
        Assert.Empty(w.Fs.EnumerateDirectories(@"C:\p"));                       // neither bin nor obj
        Assert.Equal([@"C:\p\Hello.pas", @"C:\p\Hello.pproj"], w.Fs.EnumerateFiles(@"C:\p", "*"));
        Assert.False(w.Fs.FileExists(TrustFile));                               // the gate never writes the list

        // before the up-to-date check and before Rebuild deletes anything: an existing output stays as it is
        Assert.True((await w.RunAsync(w.Request() with { Events = EventsPolicy.Run })).Result.Success);
        var calls = w.Tools.Calls.Length;
        var written = w.Fs.GetLastWriteTimeUtc(BuildWorkspace.Exe);
        var objects = w.Fs.EnumerateFiles(@"C:\p\obj\Debug", "*").ToList();

        var (upToDate, _) = await w.RunAsync(w.Request());
        var (rebuild, rebuildLog) = await w.RunAsync(w.Request(BuildTarget.Rebuild));

        Assert.False(upToDate.Success);
        Assert.False(upToDate.UpToDate);
        Assert.False(rebuild.Success);
        Assert.Equal([E9021], Diagnostics(rebuildLog));
        Assert.Equal(calls, w.Tools.Calls.Length);
        Assert.Equal(written, w.Fs.GetLastWriteTimeUtc(BuildWorkspace.Exe));
        Assert.Equal(objects, w.Fs.EnumerateFiles(@"C:\p\obj\Debug", "*"));
    }

    [Fact]
    [Trait("Req", "CMP-1237")]
    [Trait("Req", "CMP-1243")]
    public async Task ChangedCommandsAfterProjectTrust_FailAgain()
    {
        var w = WithEvents();
        var store = new FileTrustStore(w.Fs, TrustFile);
        store.TrustProject(BuildWorkspace.ProjectPath, Hash(w));
        var request = w.Request() with { TrustStore = store };

        var (trusted, trustedLog) = await w.RunAsync(request);
        w.Fs.Add(BuildWorkspace.ProjectPath, BuildWorkspace.ProjectJson("Hello", extra: """ "events": { "preBuild": ["echo hi", "del x"], "postBuild": [] }"""));
        var calls = w.Tools.Calls.Length;
        var (changed, changedLog) = await w.RunAsync(request);

        Assert.True(trusted.Success);
        Assert.Equal([W9001], Diagnostics(trustedLog));
        Assert.False(changed.Success);
        Assert.Equal([E9021], Diagnostics(changedLog));                         // one code, one text for Untrusted and Changed
        Assert.Equal(calls, w.Tools.Calls.Length);

        store.TrustProject(BuildWorkspace.ProjectPath, Hash(w));                // trusted again with the new commands
        Assert.True((await w.RunAsync(request)).Result.Success);
    }

    [Theory]
    [Trait("Req", "CMP-1243")]
    [Trait("Req", "CMP-1245")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TrustedFolderOrProject_PassesTheGate(bool folder)
    {
        var w = WithEvents();
        var store = new FileTrustStore(w.Fs, TrustFile);
        if (folder)
        {
            store.TrustFolder(@"C:\p");
        }
        else
        {
            store.TrustProject(BuildWorkspace.ProjectPath, Hash(w));
        }

        var (result, log) = await w.RunAsync(w.Request() with { TrustStore = store });

        Assert.True(result.Success);
        Assert.Equal([W9001], Diagnostics(log));                                // behind the gate: CMP-834, nothing executed
        Assert.True(w.Fs.FileExists(BuildWorkspace.Exe));
        Assert.Empty(w.Tools.Programs);
        Assert.Equal("finished Hello.pproj success=True upToDate=False 0/1/0", log.Lines[^1]);
    }

    [Fact]
    [Trait("Req", "CMP-1241")]
    public async Task SkipPolicy_BuildsWithoutEventsAndWithoutWarning()
    {
        var w = WithEvents();
        var text = w.Fs.ReadAllText(BuildWorkspace.ProjectPath);

        var (result, log) = await w.RunAsync(w.Request() with { Events = EventsPolicy.Skip, TrustStore = new ThrowingStore() });

        Assert.True(result.Success);
        Assert.Empty(Diagnostics(log));
        Assert.Equal(0, result.Warnings);
        Assert.Equal(["Compiling System", "Compiling Hello", "Linking Hello.exe"], log.Info);
        Assert.True(w.Fs.FileExists(BuildWorkspace.Exe));
        Assert.Empty(w.Tools.Programs);
        Assert.Equal(text, w.Fs.ReadAllText(BuildWorkspace.ProjectPath));       // the project file is not changed
    }

    [Fact]
    [Trait("Req", "CMP-1242")]
    public async Task RunPolicy_PassesWithoutReadingTheList()
    {
        var w = WithEvents();

        var (result, log) = await w.RunAsync(w.Request() with { Events = EventsPolicy.Run, TrustStore = new ThrowingStore() });

        Assert.True(result.Success);
        Assert.Equal([W9001], Diagnostics(log));
        Assert.True(w.Fs.FileExists(BuildWorkspace.Exe));
        Assert.False(w.Fs.FileExists(TrustFile));                               // nothing is added to the trust list
    }

    [Theory]
    [Trait("Req", "CMP-1240")]
    [InlineData(EventsPolicy.RequireTrust)]
    [InlineData(EventsPolicy.Run)]
    [InlineData(EventsPolicy.Skip)]
    public async Task ProjectWithoutEvents_NeverConsultsTheList(EventsPolicy policy)
    {
        var plain = new BuildWorkspace();
        var emptyLists = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: """ "events": { "preBuild": [], "postBuild": [] }"""));

        foreach (var w in new[] { plain, emptyLists })
        {
            var (result, log) = await w.RunAsync(w.Request() with { Events = policy, TrustStore = new ThrowingStore() });

            Assert.True(result.Success);
            Assert.Empty(Diagnostics(log));
            Assert.True(w.Fs.FileExists(BuildWorkspace.Exe));
        }
    }

    [Fact]
    [Trait("Req", "CMP-1239")]
    public async Task CleanIsNotGated_RunTargetIs()
    {
        var w = WithEvents();
        Assert.True((await w.RunAsync(w.Request() with { Events = EventsPolicy.Run })).Result.Success);
        var calls = w.Tools.Calls.Length;

        var (run, runLog) = await w.RunAsync(w.Request(BuildTarget.Run));
        var exeAfterRun = w.Fs.FileExists(BuildWorkspace.Exe);
        var (clean, cleanLog) = await w.RunAsync(w.Request(BuildTarget.Clean) with { TrustStore = new ThrowingStore() });

        Assert.False(run.Success);
        Assert.Null(run.RunExitCode);
        Assert.Equal([E9021], Diagnostics(runLog));
        Assert.Empty(w.Tools.Programs);                                         // the program was not started
        Assert.Equal(calls, w.Tools.Calls.Length);
        Assert.True(exeAfterRun);

        Assert.True(clean.Success);
        Assert.Empty(Diagnostics(cleanLog));
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));
    }

    [Fact]
    [Trait("Req", "CMP-1243")]
    [Trait("Req", "CMP-1244")]
    public async Task UnreadableListWarnsOnceAndTrustsNothing()
    {
        var w = new BuildWorkspace();
        AddProject(w, "Plain");
        AddProject(w, "A", extra: Events);
        AddProject(w, "B", extra: Events);
        w.Fs.Add(@"C:\g\All.pgroup", """{ "formatVersion": 1, "projects": ["Plain/Plain.pproj", "A/A.pproj", "B/B.pproj"], "activeProject": "B/B.pproj" }""");
        w.Fs.Add(TrustFile, """{ "formatVersion": 1, "folders": ["C:\\g"], """);
        var store = new CountingStore(new FileTrustStore(w.Fs, TrustFile));

        var (result, log) = await w.RunAsync(w.Request(path: @"C:\g\All.pgroup") with { TrustStore = new FileTrustStore(w.Fs, TrustFile) });
        var (_, countedLog) = await w.RunAsync(w.Request(path: @"C:\g\All.pgroup") with { TrustStore = store });

        Assert.False(result.Success);
        Assert.Equal(
            [@"C:\g\A\A.pproj(0,0): warning W9003: Trust list 'C:\user\roaming\trust.json' cannot be read (invalid JSON); no project is trusted",
             @"C:\g\A\A.pproj(0,0): error E9021: Project 'C:\g\A\A.pproj' has build event commands and is not trusted (pbuild: --trust-events runs them, --no-events builds without them)",
             @"C:\g\B\B.pproj(0,0): error E9021: Project 'C:\g\B\B.pproj' has build event commands and is not trusted (pbuild: --trust-events runs them, --no-events builds without them)"],
            Diagnostics(log));
        Assert.Equal(1, store.Reads);                                           // once per RunAsync call, on the first project with commands
        Assert.Equal(["W9003", "E9021", "E9021"], countedLog.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.Code));
        Assert.Equal((2, 1), (result.Errors, result.Warnings));
        Assert.Equal([true, false, false], result.Projects.Select(p => p.Success));     // the project without commands is built
        Assert.True(w.Fs.FileExists(@"C:\g\Plain\bin\Debug\Plain.exe"));
    }

    [Fact]
    [Trait("Req", "CMP-1243")]
    public async Task UntrustedDependencyStopsTheDependent()
    {
        var w = new BuildWorkspace();
        AddProject(w, "Lib", extra: Events);
        AddProject(w, "App", extra: """ "dependencies": ["../Lib/Lib.pproj"]""");

        var (result, log) = await w.RunAsync(w.Request(path: @"C:\g\App\App.pproj"));

        Assert.False(result.Success);
        Assert.Equal(
            ["started Lib.pproj Debug win64 Build", "diagnostic E9021", "finished Lib.pproj success=False upToDate=False 1/0/0",
             "started App.pproj Debug win64 Build", "info App: skipped because Lib failed", "finished App.pproj success=False upToDate=False 0/0/0"],
            log.Lines);
        Assert.Empty(w.Tools.Calls);
        Assert.Empty(w.Fs.EnumerateDirectories(@"C:\g\App"));
        Assert.Empty(w.Fs.EnumerateDirectories(@"C:\g\Lib"));
    }

    /// <summary>Adds <c>C:\g\&lt;name&gt;\&lt;name&gt;.pproj</c> with a program <c>&lt;name&gt;.pas</c>.</summary>
    private static void AddProject(BuildWorkspace w, string name, string extra = "")
    {
        w.Fs.Add($@"C:\g\{name}\{name}.pproj", BuildWorkspace.ProjectJson(name, name + ".pas", extra));
        w.Fs.Add($@"C:\g\{name}\{name}.pas", $"program {name}; begin Writeln('{name}') end.");
    }

    /// <summary>A trust list that must not be consulted.</summary>
    private sealed class ThrowingStore : ITrustStore
    {
        public TrustSnapshot Read() => throw new InvalidOperationException("the trust list was read");

        public void TrustProject(string projectPath, string commandsHash) => throw new InvalidOperationException("the trust list was written");

        public void TrustFolder(string folderPath) => throw new InvalidOperationException("the trust list was written");

        public bool Remove(TrustEntry entry) => throw new InvalidOperationException("the trust list was written");
    }

    private sealed class CountingStore(ITrustStore inner) : ITrustStore
    {
        public int Reads { get; private set; }

        public TrustSnapshot Read()
        {
            Reads++;
            return inner.Read();
        }

        public void TrustProject(string projectPath, string commandsHash) => inner.TrustProject(projectPath, commandsHash);

        public void TrustFolder(string folderPath) => inner.TrustFolder(folderPath);

        public bool Remove(TrustEntry entry) => inner.Remove(entry);
    }
}
