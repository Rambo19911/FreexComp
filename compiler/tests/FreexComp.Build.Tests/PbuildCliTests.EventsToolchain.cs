using FreexComp.Compiler;
using FreexComp.PBuild;

namespace FreexComp.Build.Tests;

/// <summary>
/// C-329: the acceptance of card F09-007 (and the second half of F09-008's) end to end — <c>pbuild</c> in process with the
/// pinned toolchain and the real command runner, each test in its own temporary directory. The commands are harmless
/// (<c>echo</c> into a file of that directory, <c>exit /b</c>); the trust list is a file of that directory or none, never the
/// user's.
/// </summary>
public sealed partial class PbuildCliTests
{
    /// <summary>Runs <paramref name="test"/> with a project <c>Hello</c> (given <c>events</c>) in a new temporary directory.</summary>
    private static void WithRealProject(string events, Action<string, Func<string[], (int Code, string[] Lines)>> test)
    {
        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Hello.pproj"), BuildWorkspace.ProjectJson("Hello", extra: $"\"events\": {events}"));
            File.WriteAllText(Path.Combine(dir, "Hello.pas"), BuildWorkspace.HelloProgram);
            test(dir, args =>
            {
                var stdout = new StringWriter { NewLine = "\n" };
                var code = PbuildCommand.Run(
                    args,
                    new PbuildEnvironment(dir, stdout, new StringWriter(), PhysicalFileSystem.Instance)
                    {
                        ToolchainDirectory = bin,
                        RtlDirectory = Path.Combine(RepoPaths.Root, "rtl", "src"),
                        TrustFilePath = Path.Combine(dir, "user", "trust.json"),         // does not exist: nothing is trusted
                    },
                    TestContext.Current.CancellationToken);
                return (code, stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
            });
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }

    /// <summary>Card F09-007 acceptance 1.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1261")]
    [Trait("Req", "CMP-1271")]
    [Trait("Req", "CMP-1278")]
    public void Events_PreBuildGeneratesFileBeforeCompile() =>
        WithRealProject("""{ "preBuild": ["echo hello> gen.txt"] }""", (dir, pbuild) =>
        {
            var (code, lines) = pbuild(["Hello.pproj", "--trust-events", "--verbose"]);

            Assert.True(code == 0, string.Join("\n", lines));
            Assert.Equal("hello\r\n", File.ReadAllText(Path.Combine(dir, "gen.txt")));       // written in the project directory
            Assert.Equal(["Running pre-build command 1 of 1", "> echo hello> gen.txt"], lines[..2]);
            var echo = Array.IndexOf(lines, "> echo hello> gen.txt");
            var firstCompilerLine = Array.FindIndex(lines, l => l.StartsWith("> ", StringComparison.Ordinal) && l.Contains("clang", StringComparison.OrdinalIgnoreCase));
            Assert.True(firstCompilerLine > echo, string.Join("\n", lines));
            Assert.True(File.Exists(Path.Combine(dir, "bin", "Debug", "Hello.exe")));
            Assert.False(File.Exists(Path.Combine(dir, "user", "trust.json")));
        });

    /// <summary>Card F09-007 acceptance 2.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1266")]
    [Trait("Req", "CMP-1271")]
    [Trait("Req", "CMP-1278")]
    public void Events_PostBuildExitCode3_E9020ExitsOne() =>
        WithRealProject("""{ "postBuild": ["exit /b 3"] }""", (dir, pbuild) =>
        {
            var (code, lines) = pbuild(["Hello.pproj", "--trust-events"]);

            Assert.True(code == 1, string.Join("\n", lines));
            Assert.Equal(
                ["Running post-build command 1 of 1",
                 Path.Combine(dir, "Hello.pproj") + "(0,0): error E9020: Build event failed: post-build command 1 ended with exit code 3: exit /b 3"],
                lines[^3..^1]);
            Assert.StartsWith("Build failed (1 errors, ", lines[^1], StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(dir, "bin", "Debug", "Hello.exe")));       // the output the build produced stays
        });

    /// <summary>Card F09-008 acceptance, second half: <c>--trust-events</c> runs the commands, <c>--no-events</c> does not, neither → E9021.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1276")]
    [Trait("Req", "CMP-1278")]
    public void Events_TrustEventsRunsThem_NoEventsDoesNot() =>
        WithRealProject("""{ "preBuild": ["echo ran> marker.txt"] }""", (dir, pbuild) =>
        {
            var marker = Path.Combine(dir, "marker.txt");

            // neither option: the gate — nothing runs, nothing is built
            var (gated, gatedLines) = pbuild(["Hello.pproj"]);
            Assert.True(gated == 1, string.Join("\n", gatedLines));
            Assert.Contains("error E9021: ", gatedLines[0], StringComparison.Ordinal);
            Assert.False(File.Exists(marker));
            Assert.False(Directory.Exists(Path.Combine(dir, "bin")));

            // --no-events: built, the command is not run
            var (skipped, skippedLines) = pbuild(["Hello.pproj", "--no-events"]);
            Assert.True(skipped == 0, string.Join("\n", skippedLines));
            Assert.False(File.Exists(marker));
            Assert.True(File.Exists(Path.Combine(dir, "bin", "Debug", "Hello.exe")));

            // --trust-events: the command is run
            var (trusted, trustedLines) = pbuild(["Hello.pproj", "--trust-events"]);
            Assert.True(trusted == 0, string.Join("\n", trustedLines));
            Assert.Equal("ran\r\n", File.ReadAllText(marker));
            Assert.False(Directory.Exists(Path.Combine(dir, "user")));                       // no trust list was written
        });
}
