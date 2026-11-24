using FreexComp.Compiler;
using FreexComp.Compiler.Targets;

namespace FreexComp.Build.Tests;

/// <summary>targets.md CMP-1019, CMP-1020 (task C-283): platform validation and the object directory per target.</summary>
public sealed partial class BuildEngineTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1019")]
    public async Task Platform_UnknownInProject_E9002()
    {
        const string Json = """
            {
              "formatVersion": 1,
              "name": "Hello",
              "mainSource": "Hello.pas",
              "platforms": ["win64", "amiga"]
            }
            """;
        var w = new BuildWorkspace(projectJson: Json.ReplaceLineEndings("\n"));

        var (result, log) = await w.RunAsync(w.Request());

        // rule 1: every entry must be a registered id — reported by the loader at the JSON token of the entry (line 5, column 26)
        Assert.False(result.Success);
        Assert.Equal(@"C:\p\Hello.pproj(5,26): error E9002: Invalid project file: unknown platform 'amiga'",
            log.Events.OfType<BuildEvent.Diagnostic>().Single().Value.ToText());
        Assert.Empty(w.Tools.Calls);

        // the loader reports it on its own too (the IDE opens projects through it); ids are case-sensitive
        var load = ProjectSerializer.LoadProject(BuildWorkspace.ProjectPath, w.Fs);
        Assert.Null(load.Value);
        Assert.Equal("E9002", Assert.Single(load.Diagnostics).Code);
        w.Fs.Add(BuildWorkspace.ProjectPath, BuildWorkspace.ProjectJson("Hello", extra: """ "platforms": ["Win64"]"""));
        Assert.Equal("Invalid project file: unknown platform 'Win64'", Assert.Single(ProjectSerializer.LoadProject(BuildWorkspace.ProjectPath, w.Fs).Diagnostics).Message);

        // every registered id is accepted; without the key the project has the baseline target
        w.Fs.Add(BuildWorkspace.ProjectPath, BuildWorkspace.ProjectJson("Hello", extra: """ "platforms": ["win64"]"""));
        Assert.Equal(["win64"], ProjectSerializer.LoadProject(BuildWorkspace.ProjectPath, w.Fs).Value!.Platforms);
        w.Fs.Add(BuildWorkspace.ProjectPath, BuildWorkspace.ProjectJson("Hello"));
        Assert.Equal([TargetRegistry.Default.Id], ProjectSerializer.LoadProject(BuildWorkspace.ProjectPath, w.Fs).Value!.Platforms);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1019")]
    public async Task Platform_NotListed_E9012()
    {
        var w = new BuildWorkspace();

        var (result, log) = await w.RunAsync(w.Request() with { Platform = "win32" });

        // rule 2: at (0,0) of the project file, Success = false, nothing built; Started still names the requested platform
        Assert.False(result.Success);
        Assert.Equal("started Hello.pproj Debug win32 Build", log.Lines[0]);
        Assert.Equal(@"C:\p\Hello.pproj(0,0): error E9012: Platform 'win32' is not available for project 'Hello' (available: win64)",
            log.Events.OfType<BuildEvent.Diagnostic>().Single().Value.ToText());
        Assert.Equal(1, result.Errors);
        Assert.Empty(w.Tools.Calls);
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));

        // the listed platform builds, named or by default (the first entry), and Started carries the id (rule 3)
        var (named, namedLog) = await w.RunAsync(w.Request() with { Platform = "win64" });
        Assert.True(named.Success, string.Join("\n", namedLog.Lines));
        Assert.Equal("started Hello.pproj Debug win64 Build", namedLog.Lines[0]);
        var (byDefault, defaultLog) = await w.RunAsync(w.Request(BuildTarget.Rebuild));
        Assert.True(byDefault.Success, string.Join("\n", defaultLog.Lines));
        Assert.Equal("started Hello.pproj Debug win64 Rebuild", defaultLog.Lines[0]);

        // a group: the check is made per project, and the project that fails it counts as failed (CMP-821)
        w.Fs.Add(@"C:\p\All.pgroup", """{ "formatVersion": 1, "projects": ["Hello.pproj"] }""");
        var (group, groupLog) = await w.RunAsync(w.Request(path: @"C:\p\All.pgroup") with { Platform = "nosuch" });
        Assert.False(group.Success);
        Assert.Equal("E9012", groupLog.Events.OfType<BuildEvent.Diagnostic>().Single().Value.Code);
        Assert.False(Assert.Single(group.Projects).Success);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1020")]
    public async Task ObjectDirectory_BaselineUnchanged_OtherTargetOwnFolder()
    {
        var w = new BuildWorkspace();
        var ct = TestContext.Current.CancellationToken;
        static string[] Under(BuildWorkspace w, string directory) => [.. w.Fs.EnumerateFiles(directory, "*").Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

        // the baseline target: obj\<config>, as before the card
        var (baseline, baselineLog) = await w.RunAsync(w.Request());
        Assert.True(baseline.Success, string.Join("\n", baselineLog.Lines));
        Assert.Contains("Hello.ll", Under(w, @"C:\p\obj\Debug"));
        Assert.Contains("Hello.pcu", Under(w, @"C:\p\obj\Debug"));
        Assert.Equal([@"C:\p\obj\Debug"], w.Fs.EnumerateDirectories(@"C:\p\obj"));
        var baselineFiles = Under(w, @"C:\p\obj\Debug");

        // another target (a descriptor outside the registry, through the engine's test seam): obj\<target id>\<config>
        var fake = TargetInfo.Win64 with { Id = "fake32", PredefinedSymbols = ["MSWINDOWS", "WINDOWS", "WIN32", "CPUX86", "CPU32BITS"] };
        var engine = new BuildEngine(w.Fs, w.Tools) { TargetOverride = registered => ReferenceEquals(registered, TargetInfo.Win64) ? fake : registered };
        var log = new EventLog();
        var other = await engine.RunAsync(w.Request(), log, ct);
        Assert.True(other.Success, string.Join("\n", log.Lines));
        Assert.Equal("started Hello.pproj Debug win64 Build", log.Lines[0]);                  // the event names the platform id of the project
        Assert.Equal([@"C:\p\obj\Debug", @"C:\p\obj\fake32"], w.Fs.EnumerateDirectories(@"C:\p\obj"));
        Assert.Equal(baselineFiles, Under(w, @"C:\p\obj\fake32\Debug"));
        Assert.Equal(baselineFiles, Under(w, @"C:\p\obj\Debug"));                             // the baseline's cache is untouched
        Assert.False(other.Projects[0].UpToDate);                                             // nothing of the other target's cache was taken

        // the options mapping is the single place that decides it
        var project = ProjectSerializer.LoadProject(BuildWorkspace.ProjectPath, w.Fs).Value!;
        Assert.Equal(@"C:\p\obj\Debug", ProjectOptions.ToCompilationOptions(project, "Debug").ObjectDirectory);
        Assert.Equal(@"C:\p\obj\Debug", ProjectOptions.ToCompilationOptions(project, "Debug", TargetInfo.Win64).ObjectDirectory);
        Assert.Same(TargetInfo.Win64, ProjectOptions.ToCompilationOptions(project, "Debug").Target);
        var mapped = ProjectOptions.ToCompilationOptions(project, "Release", fake);
        Assert.Equal(@"C:\p\obj\fake32\Release", mapped.ObjectDirectory);
        Assert.Same(fake, mapped.Target);

        // the bare compiler: ./obj/<target id>/Debug|Release for another target when no object directory is given
        var compilation = Compilation.Create(new CompilationOptions { MainFile = BuildWorkspace.MainPath, RtlDirectory = BuildWorkspace.Rtl, Target = fake }, [], w.Fs);
        Assert.True(compilation.Emit(new EmitOptions { Stage = EmitStage.Ll }, ct).Success);
        Assert.Contains("Hello.ll", Under(w, @"C:\p\obj\fake32\Debug"));

        // CMP-832: Clean accepts both shapes and removes only the folder of its own target
        var cleanLog = new EventLog();
        var cleaned = await engine.RunAsync(w.Request(BuildTarget.Clean), cleanLog, ct);
        Assert.True(cleaned.Success, string.Join("\n", cleanLog.Lines));
        Assert.Empty(w.Fs.EnumerateFiles(@"C:\p\obj\fake32\Debug", "*"));
        Assert.Equal(baselineFiles, Under(w, @"C:\p\obj\Debug"));
        var (cleanedBaseline, _) = await w.RunAsync(w.Request(BuildTarget.Clean));
        Assert.True(cleanedBaseline.Success);
        Assert.Empty(w.Fs.EnumerateFiles(@"C:\p\obj\Debug", "*"));
    }
}
