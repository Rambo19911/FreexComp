using FreexComp.Compiler;
using FreexComp.PBuild;
using FreexComp.Tests.Shared;

namespace FreexComp.Build.Tests;

/// <summary>configurations.md CMP-1154, CMP-1161…1163 (task C-298): <c>pbuild --config &lt;any name&gt;</c> with derived configurations.</summary>
public sealed partial class PbuildCliTests
{
    private const string ChainProject = """
        {
          "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
          "configurations": {
            "Debug":        { "optimization": 0, "debugInfo": true,  "defines": ["DEBUG"],   "outputDir": "bin/Debug" },
            "Release":      { "optimization": 2, "debugInfo": false, "defines": ["RELEASE"], "outputDir": "bin/Release" },
            "Release-Demo": { "inherits": "Release", "defines": ["DEMO"] },
            "Profiling":    { "inherits": "Release-Demo", "debugInfo": true, "defines": ["!", "PROFILE"] }
          }
        }
        """;

    private const string ChainProgram = """
        program Hello;
        begin
          {$IFDEF RELEASE}Writeln('RELEASE');{$ENDIF}
          {$IFDEF DEMO}Writeln('DEMO');{$ENDIF}
          {$IFDEF DEBUG}Writeln('DEBUG');{$ENDIF}
          {$IFDEF PROFILE}Writeln('PROFILE');{$ENDIF}
          Writeln('done');
        end.
        """;

    /// <summary>
    /// With the pinned toolchain: <c>--config release-demo</c> (matched case-insensitively) builds into <c>bin/Release-Demo</c> and
    /// <c>obj/Release-Demo</c>; <c>--verbose</c> shows the inherited <c>-O2</c> on the compiler command line and the Info line of
    /// CMP-1163; the inherited and the own define reach the program.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1161")]
    [Trait("Req", "CMP-1162")]
    [Trait("Req", "CMP-1163")]
    public async Task Config_DerivedConfiguration_BuildsIntoItsOwnFolders()
    {
        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Hello.pproj"), ChainProject);
            File.WriteAllText(Path.Combine(dir, "Hello.pas"), ChainProgram);

            (int Code, string[] Lines) Pbuild(params string[] args)
            {
                var stdout = new StringWriter { NewLine = "\n" };
                var code = PbuildCommand.Run(
                    args,
                    new PbuildEnvironment(dir, stdout, new StringWriter(), PhysicalFileSystem.Instance)
                    {
                        ToolchainDirectory = bin,
                        RtlDirectory = Path.Combine(RepoPaths.Root, "rtl", "src"),
                    },
                    TestContext.Current.CancellationToken);
                return (code, stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }

            var (code, lines) = Pbuild("Hello.pproj", "--config", "release-demo", "--verbose");

            Assert.True(code == 0, string.Join("\n", lines));
            Assert.Equal("Configuration Release-Demo inherits Release", lines[0]);
            var exe = Path.Combine(dir, "bin", "Release-Demo", "Hello.exe");
            Assert.True(File.Exists(exe));
            Assert.True(File.Exists(Path.Combine(dir, "obj", "Release-Demo", "Hello.obj")));
            Assert.False(Directory.Exists(Path.Combine(dir, "bin", "Release")));
            Assert.False(Directory.Exists(Path.Combine(dir, "obj", "Release")));
            var compile = Assert.Single(lines, l => l.StartsWith("> ", StringComparison.Ordinal) && l.Contains(@"\Hello.ll ", StringComparison.Ordinal));
            Assert.Contains(" -O2 ", compile, StringComparison.Ordinal);
            Assert.Contains(Path.Combine(dir, "obj", "Release-Demo", "Hello.obj"), compile, StringComparison.Ordinal);
            var output = new List<string>();
            Assert.Equal(0, await NativeRun.RunAsync(exe, dir, output.Add, _ => { }));
            Assert.Equal(["RELEASE", "DEMO", "done"], output);

            // two levels, JSON form: the line is an output event after `started`; the reset marker dropped the inherited defines
            (code, lines) = Pbuild("Hello.pproj", "--config", "PROFILING", "--verbose", "--diagnostics", "json");
            Assert.True(code == 0, string.Join("\n", lines));
            Assert.StartsWith("{\"event\":\"started\",", lines[0], StringComparison.Ordinal);
            Assert.Contains("\"configuration\":\"Profiling\"", lines[0], StringComparison.Ordinal);
            Assert.Equal("""{"event":"output","source":"info","text":"Configuration Profiling inherits Release-Demo <- Release"}""", lines[1]);
            output.Clear();
            Assert.Equal(0, await NativeRun.RunAsync(Path.Combine(dir, "bin", "Profiling", "Hello.exe"), dir, output.Add, _ => { }));
            Assert.Equal(["PROFILE", "done"], output);
            Assert.True(File.Exists(Path.Combine(dir, "bin", "Profiling", "Hello.pdb")));        // debugInfo of its own, -O2 inherited

            // a root configuration, and a derived one without --verbose: no such line
            (code, lines) = Pbuild("Hello.pproj", "--config", "Release", "--verbose");
            Assert.True(code == 0, string.Join("\n", lines));
            Assert.DoesNotContain(lines, l => l.StartsWith("Configuration ", StringComparison.Ordinal));
            (code, lines) = Pbuild("Hello.pproj", "--config", "Release-Demo", "--target", "rebuild");
            Assert.True(code == 0, string.Join("\n", lines));
            Assert.DoesNotContain(lines, l => l.StartsWith("Configuration ", StringComparison.Ordinal));
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }

    /// <summary>CMP-1154 (card acceptance 2): a cycle is E9015 from the loader — exit code 1 before anything is built.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Unit")]
    [Trait("Req", "CMP-1154")]
    public void Config_Cycle_ExitCode1_NothingBuilt()
    {
        var json = """{ "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "configurations": { "Debug": { "inherits": "Fast" }, "Fast": { "inherits": "Debug" } } }""";
        var w = new BuildWorkspace(projectJson: json);

        var (code, output, err) = Run(w, "Hello.pproj", "--config", "Fast", "--verbose");

        Assert.Equal(1, code);
        Assert.Equal(
            $@"C:\p\Hello.pproj(1,{json.IndexOf("\"Fast\" }", StringComparison.Ordinal) + 1}): error E9015: Circular configuration inheritance: Debug -> Fast -> Debug",
            output[0]);
        Assert.Matches(@"^Build failed \(1 errors, 0 warnings, 0 hints, \d+\.\d\d s\)$", output[^1]);
        Assert.Equal(2, output.Length);
        Assert.Empty(err);
        Assert.Empty(w.Tools.Calls);
        Assert.Empty(w.Fs.EnumerateFiles(@"C:\p\obj", "*"));
        Assert.Empty(w.Fs.EnumerateFiles(@"C:\p\bin", "*"));
        Assert.Equal(json, w.Fs.ReadAllText(BuildWorkspace.ProjectPath));

        // the same for an unknown parent (E9011), also without --config
        var unknown = new BuildWorkspace(projectJson:
            """{ "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "configurations": { "Debug": { "inherits": "Base" } } }""");
        (code, output, _) = Run(unknown, "Hello.pproj");
        Assert.Equal(1, code);
        Assert.EndsWith("error E9011: Configuration 'Debug' inherits from unknown configuration 'Base'", output[0], StringComparison.Ordinal);
        Assert.Empty(unknown.Tools.Calls);

        // the help names the option for any configuration
        Assert.Contains("[--config <name>]", PbuildCommand.HelpText, StringComparison.Ordinal);
        Assert.Contains("  --config <name>       any configuration of the project, matched case-insensitively", PbuildCommand.HelpText, StringComparison.Ordinal);
    }
}
