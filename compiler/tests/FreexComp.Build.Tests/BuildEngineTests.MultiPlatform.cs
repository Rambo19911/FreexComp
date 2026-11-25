using System.Collections.Immutable;

namespace FreexComp.Build.Tests;

/// <summary>
/// platform-model.md §4 (CMP-1364, 1365, 1371, 1372, 1380, task C-340): builds of a project with two platforms. The second
/// platform is the test descriptor <see cref="TestPlatforms.Test32"/>, installed through the engine's test seam; the registry
/// itself has one target.
/// </summary>
public sealed partial class BuildEngineTests
{
    /// <summary>The project <c>Hello</c> listing <c>win64</c> and <c>test32</c>, in a workspace whose engines have both installed.</summary>
    private static BuildWorkspace TwoPlatformWorkspace(string extra = "", string program = BuildWorkspace.HelloProgram) =>
        new(program, BuildWorkspace.ProjectJson("Hello", extra: "\"platforms\": [\"win64\", \"test32\"]" + (extra.Length > 0 ? ", " + extra : "")))
        {
            Installed = TestPlatforms.Both,
        };

    /// <summary>Every file below <paramref name="directories"/> with its bytes and its time, in path order.</summary>
    private static ImmutableArray<string> Snapshot(BuildWorkspace w, params string[] directories)
    {
        var files = new List<string>();
        void Add(string directory)
        {
            files.AddRange(w.Fs.EnumerateFiles(directory, "*")
                .Select(f => $"{f} {Convert.ToHexString(w.Fs.ReadAllBytes(f))} {w.Fs.GetLastWriteTimeUtc(f):O}"));
            foreach (var child in w.Fs.EnumerateDirectories(directory))
            {
                Add(child);
            }
        }

        foreach (var directory in directories)
        {
            Add(directory);
        }

        return [.. files.Order(StringComparer.Ordinal)];
    }

    private static string[] Names(BuildWorkspace w, string directory) =>
        [.. w.Fs.EnumerateFiles(directory, "*").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];

    /// <summary>Card F03-003 acceptance 2, with the test descriptor as the second platform.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1366")]
    [Trait("Req", "CMP-1371")]
    [Trait("Req", "CMP-1380")]
    public async Task Platforms_BuildBoth_ExecutablesAndIntermediatesApart()
    {
        var w = TwoPlatformWorkspace();

        // the baseline platform: the directories the project always had
        var (first, firstLog) = await w.RunAsync(w.Request() with { Platform = "win64" });
        Assert.True(first.Success, string.Join("\n", firstLog.Lines));
        Assert.Equal(@"C:\p\bin\Debug\Hello.exe", first.OutputPath);
        Assert.Equal([@"C:\p\bin\Debug"], w.Fs.EnumerateDirectories(@"C:\p\bin"));
        Assert.Equal([@"C:\p\obj\Debug"], w.Fs.EnumerateDirectories(@"C:\p\obj"));
        var baseline = Snapshot(w, @"C:\p\bin\Debug", @"C:\p\obj\Debug");
        Assert.Contains(baseline, f => f.StartsWith(@"C:\p\bin\Debug\Hello.exe ", StringComparison.Ordinal));
        Assert.Contains(baseline, f => f.StartsWith(@"C:\p\obj\Debug\Hello.pcu ", StringComparison.Ordinal));

        // the other platform: its own output and object directories; nothing of the first platform's cache is taken
        var (second, secondLog) = await w.RunAsync(w.Request() with { Platform = "test32" });
        Assert.True(second.Success, string.Join("\n", secondLog.Lines));
        Assert.False(second.UpToDate);
        Assert.Equal(["Compiling System", "Compiling Hello", "Linking Hello.exe"], secondLog.Info);
        Assert.Equal(@"C:\p\bin\test32\Debug\Hello.exe", second.OutputPath);
        Assert.True(w.Fs.FileExists(@"C:\p\bin\Debug\Hello.exe"));
        Assert.True(w.Fs.FileExists(@"C:\p\bin\test32\Debug\Hello.exe"));
        Assert.Equal([@"C:\p\bin\Debug", @"C:\p\bin\test32"], w.Fs.EnumerateDirectories(@"C:\p\bin"));
        Assert.Equal([@"C:\p\obj\Debug", @"C:\p\obj\test32"], w.Fs.EnumerateDirectories(@"C:\p\obj"));
        Assert.Equal([@"C:\p\obj\test32\Debug"], w.Fs.EnumerateDirectories(@"C:\p\obj\test32"));
        Assert.Equal(Names(w, @"C:\p\obj\Debug"), Names(w, @"C:\p\obj\test32\Debug"));
        Assert.Contains(@"/out:C:\p\bin\test32\Debug\Hello.exe", w.Fs.ReadAllText(@"C:\p\obj\test32\Debug\link.rsp").Split('\n').Select(l => l.Trim('"')));

        // the first platform's files are byte-identical (and untouched) after the second platform's build
        Assert.Equal(baseline, Snapshot(w, @"C:\p\bin\Debug", @"C:\p\obj\Debug"));

        // a second build of each is up to date, in any order, and starts no tool
        var other = Snapshot(w, @"C:\p\bin\test32", @"C:\p\obj\test32");
        var calls = w.Tools.Calls.Length;
        foreach (var platform in new[] { "win64", "test32", "win64", "test32" })
        {
            var (again, againLog) = await w.RunAsync(w.Request() with { Platform = platform });
            Assert.True(again.Success, string.Join("\n", againLog.Lines));
            Assert.True(again.UpToDate, platform);
            Assert.Equal(["Hello: up to date"], againLog.Info);
        }

        // (an up-to-date build writes the debug-information sidecar again, with the same bytes: times are not compared here)
        static IEnumerable<string> WithoutTimes(IEnumerable<string> snapshot) => snapshot.Select(f => f[..f.LastIndexOf(' ')]);
        Assert.Equal(calls, w.Tools.Calls.Length);
        Assert.Equal(WithoutTimes(baseline), WithoutTimes(Snapshot(w, @"C:\p\bin\Debug", @"C:\p\obj\Debug")));
        Assert.Equal(WithoutTimes(other), WithoutTimes(Snapshot(w, @"C:\p\bin\test32", @"C:\p\obj\test32")));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1364")]
    [Trait("Req", "CMP-1371")]
    public async Task Platforms_ActiveIsTheDefault_RequestOverrides_StartedEventNamesIt()
    {
        // the active platform is the default, although it is not the first listed one
        var w = TwoPlatformWorkspace("\"activePlatform\": \"test32\"");
        var (byDefault, defaultLog) = await w.RunAsync(w.Request());
        Assert.True(byDefault.Success, string.Join("\n", defaultLog.Lines));
        Assert.Equal("started Hello.pproj Debug test32 Build", defaultLog.Lines[0]);
        Assert.Equal("test32", defaultLog.Events.OfType<BuildEvent.Started>().Single().Platform);
        Assert.Equal(@"C:\p\bin\test32\Debug\Hello.exe", byDefault.OutputPath);
        Assert.Equal(@"C:\p\bin\test32\Debug\Hello.exe", Assert.Single(byDefault.Projects).OutputPath);
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));

        // the request overrides it, for every target
        var (requested, requestedLog) = await w.RunAsync(w.Request(BuildTarget.Rebuild) with { Platform = "win64" });
        Assert.True(requested.Success, string.Join("\n", requestedLog.Lines));
        Assert.Equal("started Hello.pproj Debug win64 Rebuild", requestedLog.Lines[0]);
        Assert.Equal(BuildWorkspace.Exe, requested.OutputPath);
        w.Tools.Program = (_, _, _) => (["ran"], [], 0);
        var (run, runLog) = await w.RunAsync(w.Request(BuildTarget.Run) with { Platform = "win64" });
        Assert.Equal("started Hello.pproj Debug win64 Run", runLog.Lines[0]);
        Assert.Equal(0, run.RunExitCode);
        Assert.Equal(BuildWorkspace.Exe, Assert.Single(w.Tools.Programs).FileName);

        // Run without a request starts the active platform's program
        var (runDefault, runDefaultLog) = await w.RunAsync(w.Request(BuildTarget.Run));
        Assert.Equal("started Hello.pproj Debug test32 Run", runDefaultLog.Lines[0]);
        Assert.Equal(0, runDefault.RunExitCode);
        Assert.Equal(@"C:\p\bin\test32\Debug\Hello.exe", w.Tools.Programs[^1].FileName);

        // without an active platform: the first listed one
        var first = TwoPlatformWorkspace();
        var (_, firstLog) = await first.RunAsync(first.Request());
        Assert.Equal("started Hello.pproj Debug win64 Build", firstLog.Lines[0]);
        var reversed = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: "\"platforms\": [\"test32\", \"win64\"]")) { Installed = TestPlatforms.Both };
        var (reversedResult, reversedLog) = await reversed.RunAsync(reversed.Request());
        Assert.Equal("started Hello.pproj Debug test32 Build", reversedLog.Lines[0]);
        Assert.Equal(@"C:\p\bin\test32\Debug\Hello.exe", reversedResult.OutputPath);

        // the same project on an installation without the second platform: E9016 names what is installed there
        var elsewhere = new BuildWorkspace(projectJson: w.Fs.ReadAllText(BuildWorkspace.ProjectPath));
        var (missing, missingLog) = await elsewhere.RunAsync(elsewhere.Request());
        Assert.False(missing.Success);
        Assert.Empty(elsewhere.Tools.Calls);
        Assert.Equal(@"C:\p\Hello.pproj(0,0): error E9016: Platform 'test32' is not installed (installed: win64)",
            missingLog.Events.OfType<BuildEvent.Diagnostic>().Single().Value.ToText());

        // and the list of an installation with both
        var unlistedInstalled = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: "\"platforms\": [\"win64\", \"amiga\"]")) { Installed = TestPlatforms.Both };
        var (amiga, amigaLog) = await unlistedInstalled.RunAsync(unlistedInstalled.Request() with { Platform = "amiga" });
        Assert.False(amiga.Success);
        Assert.Equal(@"C:\p\Hello.pproj(0,0): error E9016: Platform 'amiga' is not installed (installed: win64, test32)",
            amigaLog.Events.OfType<BuildEvent.Diagnostic>().Single().Value.ToText());
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1365")]
    [Trait("Req", "CMP-1371")]
    public async Task Platforms_OptionsReachCompilerAndLink()
    {
        const string Program = """
            program Hello;
            uses
              {$IFDEF LEGACY}Legacy,{$ENDIF}
              {$IFDEF CPUX86}Narrow,{$ENDIF}
              Common;
            begin
              {$IFDEF LEGACY}LegacyCall;{$ENDIF}
              Writeln('hi')
            end.
            """;
        const string Options = "\"platformOptions\": { \"test32\": { \"defines\": [\"LEGACY\"], \"searchPaths\": [\"lib32\"], \"libraryPaths\": [\"implib32\"] } }";
        var w = TwoPlatformWorkspace(Options, Program);
        w.Fs.Add(@"C:\p\lib32\Legacy.pas", "unit Legacy;\ninterface\nprocedure LegacyCall; stdcall; external 'legacy.dll';\nimplementation\nend.\n");
        w.Fs.Add(@"C:\p\implib32\legacy.lib", "a ready-made import library");
        w.Fs.Add(@"C:\p\Narrow.pas", "unit Narrow;\ninterface\nimplementation\nend.\n");
        w.Fs.Add(@"C:\p\Common.pas", "unit Common;\ninterface\nimplementation\nend.\n");
        static string[] Rsp(BuildWorkspace w, string objectDirectory) => w.Fs.ReadAllText(objectDirectory + @"\link.rsp").Split('\n');
        static string[] Compiled(EventLog log) =>
            [.. log.Info.Where(l => l.StartsWith("Compiling ", StringComparison.Ordinal)).Select(l => l["Compiling ".Length..]).Order(StringComparer.Ordinal)];

        // the platform without options: the unit under {$IFDEF LEGACY} is not compiled, its directory is not searched
        var (baseline, baselineLog) = await w.RunAsync(w.Request() with { Platform = "win64" });
        Assert.True(baseline.Success, string.Join("\n", baselineLog.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText())));
        Assert.Equal(["Common", "Hello", "System"], Compiled(baselineLog));
        Assert.DoesNotContain("Legacy.ll", Names(w, @"C:\p\obj\Debug"));
        Assert.DoesNotContain(Rsp(w, @"C:\p\obj\Debug"), l => l.Contains("legacy", StringComparison.OrdinalIgnoreCase));

        // the platform that defines it: the define reaches the compiler, the search path finds the unit, the library path the
        // import library; the predefined symbols are those of the descriptor (CMP-1010), independent of the lists
        var (other, otherLog) = await w.RunAsync(w.Request() with { Platform = "test32" });
        Assert.True(other.Success, string.Join("\n", otherLog.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText())));
        Assert.Equal(["Common", "Hello", "Legacy", "Narrow", "System"], Compiled(otherLog));
        Assert.Contains("Legacy.ll", Names(w, @"C:\p\obj\test32\Debug"));
        Assert.Contains(@"C:\p\implib32\legacy.lib", Rsp(w, @"C:\p\obj\test32\Debug").Select(l => l.Trim('"')), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Legacy.ll", Names(w, @"C:\p\obj\Debug"));

        // without the search path the unit is not found on that platform
        var noPath = TwoPlatformWorkspace("\"platformOptions\": { \"test32\": { \"defines\": [\"LEGACY\"] } }", Program);
        noPath.Fs.Add(@"C:\p\lib32\Legacy.pas", "unit Legacy;\ninterface\nimplementation\nend.\n");
        noPath.Fs.Add(@"C:\p\Narrow.pas", "unit Narrow;\ninterface\nimplementation\nend.\n");
        noPath.Fs.Add(@"C:\p\Common.pas", "unit Common;\ninterface\nimplementation\nend.\n");
        var (failed, failedLog) = await noPath.RunAsync(noPath.Request() with { Platform = "test32" });
        Assert.False(failed.Success);
        Assert.Contains(failedLog.Events.OfType<BuildEvent.Diagnostic>(), d => d.Value.Message.Contains("Legacy", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1372")]
    public async Task Platforms_CleanRemovesOnlyTheEffectivePlatform()
    {
        var w = TwoPlatformWorkspace("\"activePlatform\": \"test32\"");
        Assert.True((await w.RunAsync(w.Request() with { Platform = "win64" })).Result.Success);
        Assert.True((await w.RunAsync(w.Request())).Result.Success);
        var baseline = Snapshot(w, @"C:\p\bin\Debug", @"C:\p\obj\Debug");
        Assert.NotEmpty(baseline);
        Assert.NotEmpty(Snapshot(w, @"C:\p\bin\test32", @"C:\p\obj\test32"));

        // Clean without a request: the active platform's output and object directory, nothing of the other platform
        var (cleaned, cleanLog) = await w.RunAsync(w.Request(BuildTarget.Clean));
        Assert.True(cleaned.Success, string.Join("\n", cleanLog.Lines));
        Assert.Equal("started Hello.pproj Debug test32 Clean", cleanLog.Lines[0]);
        Assert.Empty(Snapshot(w, @"C:\p\bin\test32", @"C:\p\obj\test32"));
        Assert.Equal(baseline, Snapshot(w, @"C:\p\bin\Debug", @"C:\p\obj\Debug"));

        // Rebuild = that clean, then build: again only the effective platform
        var (rebuilt, rebuildLog) = await w.RunAsync(w.Request(BuildTarget.Rebuild));
        Assert.True(rebuilt.Success, string.Join("\n", rebuildLog.Lines));
        Assert.False(rebuilt.UpToDate);
        Assert.Equal(@"C:\p\bin\test32\Debug\Hello.exe", rebuilt.OutputPath);
        Assert.Equal(baseline, Snapshot(w, @"C:\p\bin\Debug", @"C:\p\obj\Debug"));
        var other = Snapshot(w, @"C:\p\bin\test32", @"C:\p\obj\test32");
        Assert.Contains(other, f => f.StartsWith(@"C:\p\bin\test32\Debug\Hello.exe ", StringComparison.Ordinal));

        // the baseline platform, named in the request
        var (cleanedBaseline, _) = await w.RunAsync(w.Request(BuildTarget.Clean) with { Platform = "win64" });
        Assert.True(cleanedBaseline.Success);
        Assert.Empty(Snapshot(w, @"C:\p\bin\Debug", @"C:\p\obj\Debug"));
        Assert.Equal(other, Snapshot(w, @"C:\p\bin\test32", @"C:\p\obj\test32"));

        // the cleaned platform is built again in full; the other one is still up to date
        var (again, againLog) = await w.RunAsync(w.Request() with { Platform = "win64" });
        Assert.Equal(["Compiling System", "Compiling Hello", "Linking Hello.exe"], againLog.Info);
        Assert.True(again.Success);
        Assert.True((await w.RunAsync(w.Request())).Result.UpToDate);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1371")]
    public async Task Platforms_EventMacrosCarryEffectiveValues()
    {
        const string Events = "\"events\": { \"preBuild\": [\"pre $(Platform)|$(Config)|$(OutputDir)|$(OutputPath)\"], \"postBuild\": [\"post $(OutputPath)\"] }";
        var outputExists = new List<bool>();
        BuildWorkspace Bed(string extra)
        {
            var bed = TwoPlatformWorkspace(extra + ", " + Events);
            bed.Commands.Behavior = (command, _) =>
            {
                if (command.StartsWith("post ", StringComparison.Ordinal))
                {
                    outputExists.Add(bed.Fs.FileExists(command["post ".Length..]));       // the linker wrote the file the macro names
                }

                return ([], [], 0);
            };
            return bed;
        }

        var w = Bed("\"activePlatform\": \"test32\"");

        // the active platform
        var (active, activeLog) = await w.RunAsync(w.Request() with { Events = EventsPolicy.Run });
        Assert.True(active.Success, string.Join("\n", activeLog.Lines));
        Assert.Equal(
            [@"pre test32|Debug|C:\p\bin\test32\Debug|C:\p\bin\test32\Debug\Hello.exe", @"post C:\p\bin\test32\Debug\Hello.exe"],
            w.Commands.Commands);
        Assert.Equal(@"C:\p\bin\test32\Debug\Hello.exe", active.OutputPath);

        // the requested platform, another configuration
        var (requested, requestedLog) = await w.RunAsync(w.Request() with { Events = EventsPolicy.Run, Platform = "win64", Configuration = "Release" });
        Assert.True(requested.Success, string.Join("\n", requestedLog.Lines));
        Assert.Equal(
            [@"pre win64|Release|C:\p\bin\Release|C:\p\bin\Release\Hello.exe", @"post C:\p\bin\Release\Hello.exe"],
            w.Commands.Commands[2..]);
        Assert.Equal(@"C:\p\bin\Release\Hello.exe", requested.OutputPath);

        // an output directory of the platform's own (CMP-1366 rule 1) is what the macros, the linker and the result name
        var own = Bed("\"platformOptions\": { \"test32\": { \"outputDir\": \"out/$(Platform)/$(Config)\" } }");
        var (overridden, overriddenLog) = await own.RunAsync(own.Request() with { Events = EventsPolicy.Run, Platform = "test32" });
        Assert.True(overridden.Success, string.Join("\n", overriddenLog.Lines));
        Assert.Equal(
            [@"pre test32|Debug|C:\p\out\test32\Debug|C:\p\out\test32\Debug\Hello.exe", @"post C:\p\out\test32\Debug\Hello.exe"],
            own.Commands.Commands);
        Assert.Equal(@"C:\p\out\test32\Debug\Hello.exe", overridden.OutputPath);
        Assert.True(own.Fs.FileExists(@"C:\p\out\test32\Debug\Hello.exe"));

        // every post-build command found the output it was given
        Assert.Equal([true, true, true], outputExists);
    }
}
