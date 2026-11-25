namespace FreexComp.Build.Tests;

/// <summary>platform-model.md CMP-1373 (task C-340): the platform of each project of a group or dependency build.</summary>
public sealed partial class GroupBuildTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1373")]
    public async Task Platforms_EachProjectUsesItsOwnActivePlatform_RequestPlatformNotListedIsE9012()
    {
        BuildWorkspace Bed()
        {
            var w = new BuildWorkspace { Installed = TestPlatforms.Both };
            AddProject(w, "A", extra: "\"platforms\": [\"win64\", \"test32\"], \"activePlatform\": \"test32\"");
            AddProject(w, "B");                                                                 // lists the baseline platform only
            AddProject(w, "C", extra: "\"platforms\": [\"test32\", \"win64\"]");                // no active platform: the first listed
            AddGroup(w, "A", "A", "B", "C");
            return w;
        }

        static Dictionary<string, string> Platforms(EventLog log) =>
            log.Events.OfType<BuildEvent.Started>().ToDictionary(s => Path.GetFileNameWithoutExtension(s.ProjectPath), s => s.Platform);
        static Dictionary<string, string?> Outputs(BuildResult result) =>
            result.Projects.ToDictionary(p => Path.GetFileNameWithoutExtension(p.ProjectPath), p => p.OutputPath);

        // without a platform in the request every project is built for its own effective platform
        var w = Bed();
        var (result, log) = await w.RunAsync(w.Request(path: GroupPath));
        Assert.True(result.Success, string.Join("\n", log.Lines));
        Assert.Equal(new() { ["A"] = "test32", ["B"] = "win64", ["C"] = "test32" }, Platforms(log));
        Assert.Equal(
            new() { ["A"] = @"C:\g\A\bin\test32\Debug\A.exe", ["B"] = @"C:\g\B\bin\Debug\B.exe", ["C"] = @"C:\g\C\bin\test32\Debug\C.exe" },
            Outputs(result));
        Assert.All(Outputs(result).Values, output => Assert.True(w.Fs.FileExists(output!)));
        Assert.False(w.Fs.FileExists(@"C:\g\A\bin\Debug\A.exe"));
        Assert.Equal(@"C:\g\A\bin\test32\Debug\A.exe", result.OutputPath);

        // a platform in the request is used for every project; the member that does not list it fails with E9012, the others are built
        var requested = Bed();
        var (other, otherLog) = await requested.RunAsync(requested.Request(path: GroupPath) with { Platform = "test32" });
        Assert.False(other.Success);
        Assert.Equal(new() { ["A"] = "test32", ["B"] = "test32", ["C"] = "test32" }, Platforms(otherLog));
        Assert.Equal(@"C:\g\B\B.pproj(0,0): error E9012: Platform 'test32' is not available for project 'B' (available: win64)",
            otherLog.Events.OfType<BuildEvent.Diagnostic>().Single().Value.ToText());
        Assert.Equal(new() { ["A"] = true, ["B"] = false, ["C"] = true }, other.Projects.ToDictionary(p => Path.GetFileNameWithoutExtension(p.ProjectPath), p => p.Success));
        Assert.Empty(requested.Fs.EnumerateDirectories(@"C:\g\B"));                          // nothing built for it

        // a platform every member lists: all are built for it, whatever their active platform is
        var baseline = Bed();
        var (all, allLog) = await baseline.RunAsync(baseline.Request(path: GroupPath) with { Platform = "win64" });
        Assert.True(all.Success, string.Join("\n", allLog.Lines));
        Assert.Equal(new() { ["A"] = "win64", ["B"] = "win64", ["C"] = "win64" }, Platforms(allLog));
        Assert.Equal(
            new() { ["A"] = @"C:\g\A\bin\Debug\A.exe", ["B"] = @"C:\g\B\bin\Debug\B.exe", ["C"] = @"C:\g\C\bin\Debug\C.exe" },
            Outputs(all));

        // a dependency is built first, for its own platform, when the project alone is built
        var dependent = new BuildWorkspace { Installed = TestPlatforms.Both };
        AddProject(dependent, "App", ["../Lib/Lib.pproj"], extra: "\"platforms\": [\"win64\", \"test32\"], \"activePlatform\": \"test32\"");
        AddProject(dependent, "Lib");
        var (app, appLog) = await dependent.RunAsync(dependent.Request(path: @"C:\g\App\App.pproj"));
        Assert.True(app.Success, string.Join("\n", appLog.Lines));
        Assert.Equal(["Lib", "App"], BuiltOrder(appLog));
        Assert.Equal(new() { ["Lib"] = "win64", ["App"] = "test32" }, Platforms(appLog));
        Assert.Equal(new() { ["Lib"] = @"C:\g\Lib\bin\Debug\Lib.exe", ["App"] = @"C:\g\App\bin\test32\Debug\App.exe" }, Outputs(app));

        // with the platform named, the dependency that does not list it fails and the dependant is skipped (CMP-821)
        var (named, namedLog) = await dependent.RunAsync(dependent.Request(BuildTarget.Rebuild, @"C:\g\App\App.pproj") with { Platform = "test32" });
        Assert.False(named.Success);
        Assert.Equal("E9012", namedLog.Events.OfType<BuildEvent.Diagnostic>().Single().Value.Code);
        Assert.Contains("App: skipped because Lib failed", namedLog.Info);
    }
}
