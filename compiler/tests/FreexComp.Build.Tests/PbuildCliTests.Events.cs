namespace FreexComp.Build.Tests;

/// <summary>C-328: <c>pbuild</c> and the build event commands (build-events.md CMP-1278), in process with the recording fake runner.</summary>
public sealed partial class PbuildCliTests
{
    /// <summary>The project of the worked example of build-events.md §7, in <c>C:\p</c>.</summary>
    private static BuildWorkspace WorkedExample()
    {
        var w = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra:
            """ "events": { "preBuild": ["echo hello> gen.txt"], "postBuild": ["copy /y \"$(OutputPath)\" \"$(ProjectDir)\\deploy\""] }"""));
        w.Commands.Behavior = (command, _) => (command.StartsWith("copy", StringComparison.Ordinal) ? ["        1 file(s) copied."] : [], [], 0);
        return w;
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1264")]
    [Trait("Req", "CMP-1278")]
    public void Events_VerboseShowsOrderAndExpandedCommands()
    {
        var w = WorkedExample();

        var (code, output, _) = Run(w, "Hello.pproj", "--trust-events", "--verbose");
        var (again, second, _) = Run(w, "Hello.pproj", "--trust-events", "--verbose");
        var (quietCode, quiet, _) = Run(w, "Hello.pproj", "--trust-events");

        Assert.Equal(0, code);
        // the pre-build block first, then the compiler and linker lines as before, the post-build block, the summary
        Assert.Equal(["Running pre-build command 1 of 1", "> echo hello> gen.txt"], output[..2]);
        var link = Array.IndexOf(output, "fake lld-link done");
        Assert.Equal("Compiling System", output[2]);
        Assert.Contains("Linking Hello.exe", output[2..link]);
        Assert.All(output[2..link], l => Assert.True(l.StartsWith("> C:\\", StringComparison.Ordinal) || l.StartsWith("Compiling ", StringComparison.Ordinal) || l.StartsWith("Linking ", StringComparison.Ordinal), l));
        Assert.Equal(
            ["Running post-build command 1 of 1", @"> copy /y ""C:\p\bin\Debug\Hello.exe"" ""C:\p\deploy""", "        1 file(s) copied."],
            output[(link + 1)..^1]);
        Assert.Matches(@"^Build succeeded \(0 errors, 0 warnings, 0 hints, \d+\.\d\d s\)$", output[^1]);

        // the second build: the two blocks again and no "up to date" line
        Assert.Equal(0, again);
        Assert.Equal(
            ["Running pre-build command 1 of 1", "> echo hello> gen.txt",
             "Running post-build command 1 of 1", @"> copy /y ""C:\p\bin\Debug\Hello.exe"" ""C:\p\deploy""", "        1 file(s) copied."],
            second[..^1]);

        // without --verbose the expanded text is not shown
        Assert.Equal(0, quietCode);
        Assert.Equal(["Running pre-build command 1 of 1", "Running post-build command 1 of 1", "        1 file(s) copied."], quiet[..^1]);
        Assert.Equal(6, w.Commands.Calls.Length);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1278")]
    public void Events_FailingCommandExitsOne()
    {
        var w = WorkedExample();
        w.Commands.Behavior = (command, _) => ([], command.StartsWith("copy", StringComparison.Ordinal) ? ["The system cannot find the path specified."] : [], command.StartsWith("copy", StringComparison.Ordinal) ? 3 : 0);

        var (code, output, _) = Run(w, "Hello.pproj", "--trust-events");

        Assert.Equal(1, code);
        Assert.Equal(
            ["Running post-build command 1 of 1", "The system cannot find the path specified.",
             @"C:\p\Hello.pproj(0,0): error E9020: Build event failed: post-build command 1 ended with exit code 3: copy /y ""$(OutputPath)"" ""$(ProjectDir)\deploy"""],
            output[^4..^1]);
        Assert.Matches("^Build failed " + Summary, output[^1]);
        Assert.True(w.Fs.FileExists(BuildWorkspace.Exe));

        // E9022 and E9023 exit with 1 as well, and nothing runs
        var invalid = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: """ "events": { "preBuild": ["echo $(Nope)", "a\nb"] }"""));
        var (invalidCode, invalidOutput, _) = Run(invalid, "Hello.pproj", "--trust-events");
        Assert.Equal(1, invalidCode);
        Assert.Equal(
            [@"C:\p\Hello.pproj(0,0): error E9022: Unknown macro '$(Nope)' in pre-build command 1 (known: ProjectDir, ProjectName, Config, Platform, OutputDir, OutputPath)",
             @"C:\p\Hello.pproj(0,0): error E9023: Build event not run: pre-build command 2 contains a control character"],
            invalidOutput[..^1]);
        Assert.Empty(invalid.Commands.Calls);
        Assert.Empty(invalid.Tools.Calls);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1278")]
    public void Events_JsonModeReportsOutputAndCommandLines()
    {
        var w = WorkedExample();
        w.Commands.Behavior = (command, _) => command.StartsWith("copy", StringComparison.Ordinal) ? ([], ["Access is denied."], 1) : (["made \"gen.txt\""], [], 0);

        var (code, output, _) = Run(w, "Hello.pproj", "--trust-events", "--verbose", "--diagnostics", "json");

        Assert.Equal(1, code);
        Assert.All(output, l => System.Text.Json.JsonDocument.Parse(l).Dispose());
        Assert.Equal(
            ["""{"event":"started","project":"C:\\p\\Hello.pproj","configuration":"Debug","platform":"win64","target":"build"}""",
             """{"event":"output","source":"info","text":"Running pre-build command 1 of 1"}""",
             """{"event":"commandLine","text":"echo hello> gen.txt"}""",
             """{"event":"output","source":"tool","text":"made \"gen.txt\""}""",
             """{"event":"output","source":"info","text":"Compiling System"}"""],
            output[..5]);
        var post = Array.IndexOf(output, """{"event":"output","source":"info","text":"Running post-build command 1 of 1"}""");
        Assert.True(post > 5);
        Assert.Equal("""{"event":"commandLine","text":"copy /y \"C:\\p\\bin\\Debug\\Hello.exe\" \"C:\\p\\deploy\""}""", output[post + 1]);
        Assert.Equal("""{"event":"output","source":"tool","text":"Access is denied."}""", output[post + 2]);
        Assert.Contains("\"severity\":\"error\",\"code\":\"E9020\"", output[post + 3], StringComparison.Ordinal);     // an ordinary diagnostic object
        Assert.Contains("post-build command 1 ended with exit code 1", output[post + 3], StringComparison.Ordinal);
        Assert.Matches("""^\{"event":"finished","project":"C:\\\\p\\\\Hello.pproj","success":false,"upToDate":false,"errors":1,"warnings":0,"hints":0,"ms":\d+\}$""", output[post + 4]);
        Assert.Matches("""^\{"summary":\{"errors":1,"warnings":0,"hints":0,"output":null,"ms":\d+\}\}$""", output[^1]);
    }
}
