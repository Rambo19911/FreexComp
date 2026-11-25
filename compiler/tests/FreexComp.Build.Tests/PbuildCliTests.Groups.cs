namespace FreexComp.Build.Tests;

/// <summary>C-337, C-338: groups and dependencies through the <c>pbuild</c> command line (group-build.md CMP-1345, 1349, 1351).</summary>
public sealed partial class PbuildCliTests
{
    /// <summary>Adds <c>C:\p\&lt;name&gt;\&lt;name&gt;.pproj</c> with a program <c>&lt;name&gt;.pas</c> that depends on the members named.</summary>
    private static void AddMember(BuildWorkspace w, string name, string? program = null, params string[] dependencies)
    {
        var deps = string.Join(", ", dependencies.Select(d => $"\"../{d}/{d}.pproj\""));
        w.Fs.Add($@"C:\p\{name}\{name}.pproj", BuildWorkspace.ProjectJson(name, name + ".pas", $""" "dependencies": [{deps}]"""));
        w.Fs.Add($@"C:\p\{name}\{name}.pas", program ?? $"program {name}; begin Writeln('{name}') end.");
    }

    /// <summary>Adds <c>C:\p\G.pgroup</c>; the last project is the active one.</summary>
    private static void AddMemberGroup(BuildWorkspace w, params string[] projects) =>
        w.Fs.Add(@"C:\p\G.pgroup", $$"""{ "formatVersion": 1, "projects": [{{string.Join(", ", projects.Select(p => $"\"{p}/{p}.pproj\""))}}], "activeProject": "{{projects[^1]}}/{{projects[^1]}}.pproj" }""");

    private static string MemberExe(string name) => $@"C:\p\{name}\bin\Debug\{name}.exe";

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1349")]
    public void Group_Clean_RemovesOutputsOfEveryProject()
    {
        var w = new BuildWorkspace();
        AddMember(w, "One");
        AddMember(w, "Two", null, "One");
        AddMemberGroup(w, "One", "Two");
        Assert.Equal(0, Run(w, "G.pgroup").ExitCode);
        Assert.True(w.Fs.FileExists(MemberExe("One")) && w.Fs.FileExists(MemberExe("Two")));
        var calls = w.Tools.Calls.Length;

        var (code, output, err) = Run(w, "G.pgroup", "--target", "clean");

        Assert.Equal(0, code);
        Assert.Matches("^Build succeeded " + Summary, Assert.Single(output));
        Assert.Empty(err);
        Assert.False(w.Fs.FileExists(MemberExe("One")));
        Assert.False(w.Fs.FileExists(MemberExe("Two")));
        Assert.Empty(w.Fs.EnumerateFiles(@"C:\p\One\obj\Debug", "*"));
        Assert.Empty(w.Fs.EnumerateFiles(@"C:\p\Two\obj\Debug", "*"));
        Assert.Equal(calls, w.Tools.Calls.Length);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1345")]
    public void StopOnFailureOption_SetsTheSwitch_AndHelpNamesIt()
    {
        static BuildWorkspace Workspace()
        {
            var w = new BuildWorkspace();
            AddMember(w, "Bad", "program Bad; begin Foo := 1 end.");
            AddMember(w, "Good");
            AddMemberGroup(w, "Bad", "Good");
            return w;
        }

        // default: the project that does not depend on the failed one is built
        var plain = Workspace();
        var (plainCode, plainOutput, _) = Run(plain, "G.pgroup");
        Assert.Equal(1, plainCode);
        Assert.True(plain.Fs.FileExists(MemberExe("Good")));
        Assert.DoesNotContain(plainOutput, l => l.Contains("skipped", StringComparison.Ordinal));

        var w = Workspace();
        var (code, output, _) = Run(w, "G.pgroup", "--stop-on-failure");

        Assert.Equal(1, code);                                                           // CMP-841: exit codes unchanged
        Assert.False(w.Fs.FileExists(MemberExe("Good")));
        Assert.Equal("Good: skipped because the build stopped after Bad failed", output[^2]);
        Assert.Matches(@"^Build failed \(1 errors, 0 warnings, 0 hints, \d+\.\d\d s\)$", output[^1]);
        Assert.DoesNotContain("lld-link.exe", w.Tools.Calls);

        var json = Workspace();
        var (jsonCode, jsonOutput, _) = Run(json, "G.pgroup", "--stop-on-failure", "--diagnostics", "json");
        Assert.Equal(1, jsonCode);
        Assert.Contains("""{"event":"output","source":"info","text":"Good: skipped because the build stopped after Bad failed"}""", jsonOutput);
        Assert.Matches("""^\{"event":"finished","project":"C:\\\\p\\\\Good\\\\Good.pproj","success":false,"upToDate":false,"errors":0,"warnings":0,"hints":0,"ms":\d+\}$""", jsonOutput[^2]);

        var help = Run(w, "--help").Out;
        Assert.Contains("  --stop-on-failure     stop at the first project that fails (default: build the projects that do not depend on it)", help);
        Assert.Contains(help, l => l.Contains("[--stop-on-failure]", StringComparison.Ordinal));
        Assert.Equal("  --version | --help", help[^1]);
    }

    /// <summary>C-338 (CMP-1351): <c>pbuild A.pproj</c> builds the chain A → B → C as C, B, A; nothing is started the second time.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1351")]
    public void Dependencies_ChainBuildsInOrder_SecondRunStartsNoProcess()
    {
        var w = new BuildWorkspace();
        AddMember(w, "A", null, "B");
        AddMember(w, "B", null, "C");
        AddMember(w, "C");

        var (code, output, _) = Run(w, @"A\A.pproj", "--diagnostics", "json");

        Assert.Equal(0, code);
        Assert.Equal(
            [
                """{"event":"started","project":"C:\\p\\C\\C.pproj","configuration":"Debug","platform":"win64","target":"build"}""",
                """{"event":"started","project":"C:\\p\\B\\B.pproj","configuration":"Debug","platform":"win64","target":"build"}""",
                """{"event":"started","project":"C:\\p\\A\\A.pproj","configuration":"Debug","platform":"win64","target":"build"}""",
            ],
            output.Where(l => l.StartsWith("""{"event":"started",""", StringComparison.Ordinal)));
        Assert.True(w.Fs.FileExists(MemberExe("A")) && w.Fs.FileExists(MemberExe("B")) && w.Fs.FileExists(MemberExe("C")));
        var calls = w.Tools.Calls.Length;
        Assert.Equal(3, w.Tools.Calls.Count(c => c == "lld-link.exe"));

        var (secondCode, second, _) = Run(w, @"A\A.pproj");

        Assert.Equal(0, secondCode);
        Assert.Equal(["C: up to date", "B: up to date", "A: up to date"], second[..^1]);
        Assert.Matches("^Build succeeded " + Summary, second[^1]);
        Assert.Equal(calls, w.Tools.Calls.Length);                                       // no process for any of the three
    }
}
