using System.Collections.Immutable;

namespace FreexComp.Build.Tests;

/// <summary>C-146: project groups and dependencies (build-engine.md CMP-821, 822; CMP-807 for groups; CMP-833 Run of a group).</summary>
[Trait("Phase", "3")]
public sealed partial class GroupBuildTests
{
    private const string GroupPath = @"C:\g\All.pgroup";

    /// <summary>Adds <c>C:\g\&lt;name&gt;\&lt;name&gt;.pproj</c> with a program <c>&lt;name&gt;.pas</c>; <paramref name="dependencies"/> are written as given.</summary>
    private static void AddProject(BuildWorkspace w, string name, string[]? dependencies = null, string? program = null, string extra = "")
    {
        var deps = string.Join(", ", (dependencies ?? []).Select(d => System.Text.Json.JsonSerializer.Serialize(d)));
        var json = BuildWorkspace.ProjectJson(name, name + ".pas", $""" "dependencies": [{deps}]""" + (extra.Length > 0 ? ", " + extra : ""));
        w.Fs.Add($@"C:\g\{name}\{name}.pproj", json);
        w.Fs.Add($@"C:\g\{name}\{name}.pas", program ?? $"program {name}; begin Writeln('{name}') end.");
    }

    private static void AddGroup(BuildWorkspace w, string active, params string[] projects) =>
        w.Fs.Add(GroupPath, $$"""{ "formatVersion": 1, "projects": [{{string.Join(", ", projects.Select(p => $"\"{p}/{p}.pproj\""))}}], "activeProject": "{{active}}/{{active}}.pproj" }""");

    private static ImmutableArray<string> BuiltOrder(EventLog log) =>
        [.. log.Events.OfType<BuildEvent.Started>().Select(s => Path.GetFileNameWithoutExtension(s.ProjectPath))];

    [Fact]
    [Trait("Req", "CMP-821")]
    public async Task Group_BuildsInDependencyOrder_TiesByListedOrder()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../LibA/LibA.pproj"]);
        AddProject(w, "LibB");
        AddProject(w, "LibA");
        AddProject(w, "Tool");
        AddGroup(w, "Tool", "App", "LibB", "LibA", "Tool");

        var (result, log) = await w.RunAsync(w.Request(path: GroupPath));

        Assert.True(result.Success, string.Join("\n", log.Lines));
        Assert.Equal(["LibB", "LibA", "App", "Tool"], BuiltOrder(log));
        Assert.Equal([@"C:\g\LibB\LibB.pproj", @"C:\g\LibA\LibA.pproj", @"C:\g\App\App.pproj", @"C:\g\Tool\Tool.pproj"], result.Projects.Select(p => p.ProjectPath));
        Assert.Equal(@"C:\g\Tool\bin\Debug\Tool.exe", result.OutputPath);
    }

    [Theory]
    [Trait("Req", "CMP-807")]
    [InlineData("A", new[] { "C", "B", "A" })]
    [InlineData("C", new[] { "B", "A", "C" })]
    public async Task Group_ActiveProjectIsBuiltLastAmongIndependentProjects(string active, string[] expected)
    {
        var w = new BuildWorkspace();
        AddProject(w, "C");
        AddProject(w, "B");
        AddProject(w, "A");
        AddGroup(w, active, "C", "B", "A");

        var (result, log) = await w.RunAsync(w.Request(path: GroupPath));

        Assert.Equal(expected, BuiltOrder(log));
        Assert.Equal($@"C:\g\{active}\bin\Debug\{active}.exe", result.OutputPath);
    }

    [Fact]
    [Trait("Req", "CMP-800")]
    public async Task Group_ProjectsAreNeverInterleaved_AndThereIsNoGroupEvent()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../Lib/Lib.pproj"]);
        AddProject(w, "Lib");
        AddGroup(w, "App", "App", "Lib");

        var (_, log) = await w.RunAsync(w.Request(path: GroupPath) with { Verbose = true });

        string? current = null;
        foreach (var e in log.Events)
        {
            var project = e switch
            {
                BuildEvent.Started s => s.ProjectPath,
                BuildEvent.Diagnostic d => d.ProjectPath,
                BuildEvent.CommandLine c => c.ProjectPath,
                BuildEvent.OutputLine o => o.ProjectPath,
                BuildEvent.Finished f => f.ProjectPath,
                _ => throw new InvalidOperationException(),
            };
            if (e is BuildEvent.Started)
            {
                Assert.Null(current);
                current = project;
            }

            Assert.Equal(current, project);
            if (e is BuildEvent.Finished)
            {
                current = null;
            }
        }

        Assert.Equal(2, log.Events.OfType<BuildEvent.Started>().Count());
        Assert.DoesNotContain(log.Events, e => e is BuildEvent.Started s && s.ProjectPath == GroupPath);
    }

    [Fact]
    [Trait("Req", "CMP-821")]
    public async Task Group_DependencyNotListedInTheGroup_IsStillBuilt()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../Hidden/Hidden.pproj"]);
        AddProject(w, "Hidden");
        AddGroup(w, "App", "App");

        var (result, log) = await w.RunAsync(w.Request(path: GroupPath));

        Assert.True(result.Success);
        Assert.Equal(["Hidden", "App"], BuiltOrder(log));
    }

    [Fact]
    [Trait("Req", "CMP-821")]
    public async Task Group_Cycle_IsE9006_AndNothingIsBuilt()
    {
        var w = new BuildWorkspace();
        AddProject(w, "A", ["../B/B.pproj"]);
        AddProject(w, "B", ["../C/C.pproj"]);
        AddProject(w, "C", ["../A/A.pproj"]);
        AddProject(w, "Free");
        AddGroup(w, "A", "Free", "A", "B", "C");

        var (result, log) = await w.RunAsync(w.Request(path: GroupPath));

        Assert.False(result.Success);
        Assert.Equal(
            ["started All.pgroup Debug win64 Build", "diagnostic E9006", "finished All.pgroup success=False upToDate=False 1/0/0"],
            log.Lines);
        Assert.Equal(@"C:\g\All.pgroup(0,0): error E9006: Circular project dependency: C:\g\A\A.pproj -> C:\g\B\B.pproj -> C:\g\C\C.pproj -> C:\g\A\A.pproj",
            log.Events.OfType<BuildEvent.Diagnostic>().Single().Value.ToText());
        Assert.Empty(w.Tools.Calls);
    }

    [Fact]
    [Trait("Req", "CMP-821")]
    public async Task Group_FailedDependency_SkipsItsDependants_OthersStillBuild()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../Lib/Lib.pproj"]);
        AddProject(w, "Top", ["../App/App.pproj"]);
        AddProject(w, "Lib", program: "program Lib; begin Foo := 1 end.");
        AddProject(w, "Other");
        AddGroup(w, "Top", "Top", "App", "Lib", "Other");

        var (result, log) = await w.RunAsync(w.Request(path: GroupPath));

        Assert.False(result.Success);
        Assert.Equal(["Lib", "App", "Other", "Top"], BuiltOrder(log));
        var lines = log.Lines;
        var app = lines.IndexOf("started App.pproj Debug win64 Build");
        Assert.Equal(
            ["started App.pproj Debug win64 Build", "info App: skipped because Lib failed", "finished App.pproj success=False upToDate=False 0/0/0"],
            lines[app..(app + 3)]);
        Assert.Equal(
            ["started Top.pproj Debug win64 Build", "info Top: skipped because App failed", "finished Top.pproj success=False upToDate=False 0/0/0"],
            lines[^3..]);
        Assert.Equal([false, false, true, false], result.Projects.Select(p => p.Success));
        Assert.Empty(result.Projects[1].Diagnostics);
        Assert.Equal(1, result.Errors);
        Assert.True(w.Fs.FileExists(@"C:\g\Other\bin\Debug\Other.exe"));
        Assert.False(w.Fs.FileExists(@"C:\g\App\bin\Debug\App.exe"));
    }

    [Fact]
    [Trait("Req", "CMP-821")]
    public async Task Group_ProjectInGroup_BuildsOnlyThatProjectAndItsDependencies()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../Lib/Lib.pproj"]);
        AddProject(w, "Lib");
        AddProject(w, "Other");
        AddGroup(w, "Other", "App", "Lib", "Other");

        var (result, log) = await w.RunAsync(w.Request(path: GroupPath) with { ProjectInGroup = "App/App.pproj" });

        Assert.True(result.Success);
        Assert.Equal(["Lib", "App"], BuiltOrder(log));
        Assert.Equal(@"C:\g\App\bin\Debug\App.exe", result.OutputPath);
    }

    [Fact]
    [Trait("Req", "CMP-822")]
    public async Task Dependency_UnitsAreNotOnTheDependantsSearchPath()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../Lib/Lib.pproj"], program: "program App; uses LibUnit; begin end.");
        AddProject(w, "Lib", program: "program Lib; uses LibUnit; begin end.");
        w.Fs.Add(@"C:\g\Lib\LibUnit.pas", "unit LibUnit; interface implementation end.");
        AddGroup(w, "App", "App", "Lib");

        var (result, log) = await w.RunAsync(w.Request(path: GroupPath));

        Assert.Equal([true, false], result.Projects.Select(p => p.Success));
        Assert.Contains(result.Projects[1].Diagnostics, d => d.Code == "E2202");
        Assert.Equal(["Lib", "App"], BuiltOrder(log));
    }

    [Fact]
    [Trait("Req", "CMP-833")]
    public async Task Group_Run_BuildsAndRunsOnlyTheActiveProject()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../Lib/Lib.pproj"]);
        AddProject(w, "Lib");
        AddProject(w, "Other");
        AddGroup(w, "App", "App", "Lib", "Other");
        w.Tools.Program = (exe, _, _) => ([Path.GetFileName(exe)], [], 5);

        var (result, log) = await w.RunAsync(w.Request(BuildTarget.Run, GroupPath));

        Assert.Equal(["Lib", "App"], BuiltOrder(log));
        Assert.Equal(["started Lib.pproj Debug win64 Build", "started App.pproj Debug win64 Run"], log.Lines.Where(l => l.StartsWith("started", StringComparison.Ordinal)));
        Assert.Equal(@"C:\g\App\bin\Debug\App.exe", Assert.Single(w.Tools.Programs).FileName);
        Assert.Equal(5, result.RunExitCode);
        Assert.Contains("programstdout App.exe", log.Lines);
    }

    [Fact]
    [Trait("Req", "CMP-807")]
    public async Task SingleProjectBuildsItsDependenciesFirst()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../LibA/LibA.pproj", "../LibB/LibB.pproj"]);
        AddProject(w, "LibA", ["../Base/Base.pproj"]);
        AddProject(w, "LibB");
        AddProject(w, "Base");
        AddProject(w, "Unrelated");

        var (result, log) = await w.RunAsync(w.Request(path: @"C:\g\App\App.pproj"));

        Assert.True(result.Success, string.Join("\n", log.Lines));
        Assert.Equal(["Base", "LibA", "LibB", "App"], BuiltOrder(log));
        Assert.Equal(@"C:\g\App\bin\Debug\App.exe", result.OutputPath);
        Assert.True(w.Fs.FileExists(@"C:\g\Base\bin\Debug\Base.exe"));
        Assert.False(w.Fs.FileExists(@"C:\g\Unrelated\bin\Debug\Unrelated.exe"));

        // Run: the dependencies are built, only the requested project runs
        w.Tools.Program = (exe, _, _) => ([Path.GetFileName(exe)], [], 3);
        var (run, runLog) = await w.RunAsync(w.Request(BuildTarget.Run, @"C:\g\App\App.pproj"));

        Assert.Equal(
            ["started Base.pproj Debug win64 Build", "started LibA.pproj Debug win64 Build", "started LibB.pproj Debug win64 Build", "started App.pproj Debug win64 Run"],
            runLog.Lines.Where(l => l.StartsWith("started", StringComparison.Ordinal)));
        Assert.Equal(@"C:\g\App\bin\Debug\App.exe", Assert.Single(w.Tools.Programs).FileName);
        Assert.Equal(3, run.RunExitCode);
    }

    [Fact]
    [Trait("Req", "CMP-804")]
    public async Task Group_MissingGroupFile_IsE9003_InABracketOfTheGroupFile()
    {
        var w = new BuildWorkspace();

        var (result, log) = await w.RunAsync(w.Request(path: @"C:\g\Nope.pgroup"));

        Assert.False(result.Success);
        Assert.Equal(["started Nope.pgroup Debug win64 Build", "diagnostic E9003", "finished Nope.pgroup success=False upToDate=False 1/0/0"], log.Lines);
        Assert.Equal(@"C:\g\Nope.pgroup", Assert.Single(result.Projects).ProjectPath);
    }

    [Fact]
    [Trait("Req", "CMP-821")]
    public async Task Group_MissingMemberProject_IsE9003_DependantsSkipped()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../Gone/Gone.pproj"]);
        AddGroup(w, "App", "App");

        var (result, log) = await w.RunAsync(w.Request(path: GroupPath));

        Assert.False(result.Success);
        Assert.Equal(
            ["started Gone.pproj Debug win64 Build", "diagnostic E9003", "finished Gone.pproj success=False upToDate=False 1/0/0",
             "started App.pproj Debug win64 Build", "info App: skipped because Gone failed", "finished App.pproj success=False upToDate=False 0/0/0"],
            log.Lines);
    }

    [Fact]
    [Trait("Req", "CMP-807")]
    public async Task Group_UpToDate_WhenEveryProjectIsUpToDate()
    {
        var w = new BuildWorkspace();
        AddProject(w, "App", ["../Lib/Lib.pproj"]);
        AddProject(w, "Lib");
        AddGroup(w, "App", "App", "Lib");
        await w.RunAsync(w.Request(path: GroupPath));

        var (result, log) = await w.RunAsync(w.Request(path: GroupPath));

        Assert.True(result.UpToDate);
        Assert.Equal(["Lib: up to date", "App: up to date"], log.Info);
    }
}
