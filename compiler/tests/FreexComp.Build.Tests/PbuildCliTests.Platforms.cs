using FreexComp.PBuild;

namespace FreexComp.Build.Tests;

/// <summary>platform-model.md CMP-1370, CMP-1374 (task C-340): <c>pbuild</c> and the platforms of a project.</summary>
public sealed partial class PbuildCliTests
{
    private const string StartedHello = """{"event":"started","project":"C:\\p\\Hello.pproj","configuration":"Debug","platform":"PLATFORM","target":"build"}""";

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1374")]
    public void Platform_DefaultIsTheActivePlatform_HelpSaysSo()
    {
        // without --platform: the project's active platform, here not the first listed one
        var w = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: "\"platforms\": [\"amiga\", \"win64\"], \"activePlatform\": \"win64\""));
        var (code, output, _) = Run(w, "Hello.pproj", "--diagnostics", "json");
        Assert.Equal(0, code);
        Assert.Equal(StartedHello.Replace("PLATFORM", "win64", StringComparison.Ordinal), output[0]);
        Assert.True(w.Fs.FileExists(BuildWorkspace.Exe));

        // without an active platform: the first listed one, as before
        var first = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: "\"platforms\": [\"amiga\", \"win64\"]"));
        var (_, firstOutput, _) = Run(first, "Hello.pproj", "--diagnostics", "json");
        Assert.Equal(StartedHello.Replace("PLATFORM", "amiga", StringComparison.Ordinal), firstOutput[0]);

        // --platform overrides the active platform
        var (_, named, _) = Run(w, "Hello.pproj", "--platform", "amiga", "--diagnostics", "json");
        Assert.Equal(StartedHello.Replace("PLATFORM", "amiga", StringComparison.Ordinal), named[0]);

        // "all" is an ordinary id (there is no --platform all): a platform the project does not list
        var (all, lines, _) = Run(w, "Hello.pproj", "--platform", "all");
        Assert.Equal(1, all);
        Assert.Contains(@"C:\p\Hello.pproj(0,0): error E9012: Platform 'all' is not available for project 'Hello' (available: amiga, win64)", lines);

        // a group: each project its own active platform
        var group = new BuildWorkspace();
        group.Fs.Add(@"C:\p\A\A.pproj", BuildWorkspace.ProjectJson("A", "A.pas", "\"platforms\": [\"amiga\", \"win64\"], \"activePlatform\": \"win64\""));
        group.Fs.Add(@"C:\p\A\A.pas", "program A; begin end.");
        group.Fs.Add(@"C:\p\All.pgroup", """{ "formatVersion": 1, "projects": ["Hello.pproj", "A/A.pproj"] }""");
        var (groupCode, groupOutput, _) = Run(group, "All.pgroup", "--diagnostics", "json");
        Assert.Equal(0, groupCode);
        Assert.Equal(2, groupOutput.Count(l => l.StartsWith("""{"event":"started",""", StringComparison.Ordinal) && l.Contains("\"platform\":\"win64\"", StringComparison.Ordinal)));

        // the help names the rule
        Assert.Contains("  --platform <id>       target id (default: the project's active platform)", PbuildCommand.HelpText.ReplaceLineEndings("\n").Split('\n'));
        Assert.DoesNotContain("first entry", PbuildCommand.HelpText, StringComparison.Ordinal);
        var (_, help, _) = Run(w, "--help");
        Assert.Contains("  --platform <id>       target id (default: the project's active platform)", help);
    }

    /// <summary>Card F03-003 acceptance 4 through the command line.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1370")]
    [Trait("Req", "CMP-1374")]
    public void Platform_NotInstalled_ExitOne_ListsInstalled()
    {
        const string Error = @"C:\p\Hello.pproj(0,0): error E9016: Platform 'amiga' is not installed (installed: win64)";
        var w = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: "\"platforms\": [\"win64\", \"amiga\"], \"activePlatform\": \"amiga\""));

        // the active platform is not installed: the error with the installed platforms, exit code 1, nothing built
        var (code, output, _) = Run(w, "Hello.pproj");
        Assert.Equal(1, code);
        Assert.Equal(Error, output[0]);
        Assert.Matches(@"^Build failed \(1 errors, 0 warnings, 0 hints, \d+\.\d\d s\)$", output[^1]);
        Assert.Equal(2, output.Length);
        Assert.Empty(w.Tools.Calls);
        Assert.Empty(w.Fs.EnumerateDirectories(@"C:\p"));

        // the same when it is named, for every target, and as JSON
        foreach (var target in new[] { "build", "rebuild", "clean", "run" })
        {
            var (named, lines, _) = Run(w, "Hello.pproj", "--platform", "amiga", "--target", target);
            Assert.Equal(1, named);
            Assert.Equal(Error, lines[0]);
        }

        var (jsonCode, json, _) = Run(w, "Hello.pproj", "--diagnostics", "json");
        Assert.Equal(1, jsonCode);
        Assert.Equal(StartedHello.Replace("PLATFORM", "amiga", StringComparison.Ordinal), json[0]);
        Assert.Contains(json, l => l.StartsWith("""{"file":"C:\\p\\Hello.pproj","line":0,"col":0,""", StringComparison.Ordinal) && l.Contains("\"code\":\"E9016\"", StringComparison.Ordinal));
        Assert.Empty(w.Tools.Calls);

        // the installed platform of the same project builds
        var (built, _, _) = Run(w, "Hello.pproj", "--platform", "win64");
        Assert.Equal(0, built);
        Assert.True(w.Fs.FileExists(BuildWorkspace.Exe));
    }
}
