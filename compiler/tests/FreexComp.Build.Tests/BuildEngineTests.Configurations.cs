using FreexComp.Compiler;
using FreexComp.Tests.Shared;

namespace FreexComp.Build.Tests;

/// <summary>configurations.md CMP-1162 (task C-298): a change in an ancestor changes the options of the derived configuration.</summary>
public sealed partial class BuildEngineTests
{
    /// <summary>
    /// With the pinned toolchain: the program prints a text chosen by <c>{$IFDEF}</c>. Changing the parent's define changes the
    /// output of the derived build; an unrelated edit of a sibling configuration leaves it up to date.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1162")]
    public async Task ParentDefineChange_RebuildsDerivedConfiguration()
    {
        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            static string ProjectText(string parentDefine, int siblingOptimization) => $$"""
                {
                  "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
                  "configurations": {
                    "Base": { "optimization": 0, "debugInfo": false, "defines": ["{{parentDefine}}"], "outputDir": "bin/Base" },
                    "Derived": { "inherits": "Base", "defines": ["OWN"] },
                    "Sibling": { "inherits": "Base", "optimization": {{siblingOptimization}}, "defines": ["SIBLING"] }
                  },
                  "activeConfiguration": "Base"
                }
                """;
            File.WriteAllText(Path.Combine(dir, "Hello.pas"), """
                program Hello;
                begin
                  {$IFDEF ONE}Writeln('one');{$ENDIF}
                  {$IFDEF TWO}Writeln('two');{$ENDIF}
                  {$IFDEF OWN}Writeln('own');{$ENDIF}
                  {$IFDEF SIBLING}Writeln('sibling');{$ENDIF}
                end.
                """);
            var exe = Path.Combine(dir, "bin", "Derived", "Hello.exe");

            async Task<(BuildResult Result, EventLog Log, List<string> Output)> BuildAndRunAsync(string project)
            {
                File.WriteAllText(Path.Combine(dir, "Hello.pproj"), project);
                var log = new EventLog();
                var result = await new BuildEngine(PhysicalFileSystem.Instance).RunAsync(
                    new BuildRequest
                    {
                        ProjectPath = Path.Combine(dir, "Hello.pproj"),
                        Configuration = "derived",                                      // CMP-842: matched case-insensitively
                        ToolchainDirectory = bin,
                        RtlDirectory = Path.Combine(RepoPaths.Root, "rtl", "src"),
                    },
                    log,
                    TestContext.Current.CancellationToken);
                Assert.True(result.Success, string.Join("\n", log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText())));
                var output = new List<string>();
                Assert.Equal(0, await NativeRun.RunAsync(exe, dir, output.Add, _ => { }));
                return (result, log, output);
            }

            var first = await BuildAndRunAsync(ProjectText("ONE", 0));
            Assert.Equal(["one", "own"], first.Output);
            Assert.Contains("Compiling Hello", first.Log.Info);
            Assert.True(File.Exists(Path.Combine(dir, "obj", "Derived", "Hello.obj")));
            Assert.Equal(Path.Combine(dir, "bin", "Derived", "Hello.exe"), first.Result.OutputPath, ignoreCase: true);

            // nothing changed: up to date
            var again = await BuildAndRunAsync(ProjectText("ONE", 0));
            Assert.True(again.Result.UpToDate);
            Assert.Equal(["Hello: up to date"], again.Log.Info);

            // the parent's define changes: the derived configuration is rebuilt and prints the other text
            var changed = await BuildAndRunAsync(ProjectText("TWO", 0));
            Assert.False(changed.Result.UpToDate);
            Assert.Contains("Compiling Hello", changed.Log.Info);
            Assert.Contains("Linking Hello.exe", changed.Log.Info);
            Assert.Equal(["two", "own"], changed.Output);

            // an unrelated edit of a sibling configuration: up to date, same program
            var sibling = await BuildAndRunAsync(ProjectText("TWO", 2));
            Assert.True(sibling.Result.UpToDate);
            Assert.Equal(["Hello: up to date"], sibling.Log.Info);
            Assert.Equal(["two", "own"], sibling.Output);
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }
}
