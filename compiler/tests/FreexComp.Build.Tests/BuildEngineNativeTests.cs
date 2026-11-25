using FreexComp.Compiler;
using FreexComp.Compiler.Targets;
using FreexComp.Tests.Shared;

namespace FreexComp.Build.Tests;

/// <summary>Build-engine behaviour proven with the pinned toolchain on disk and the built programs run.</summary>
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
public sealed class BuildEngineNativeTests
{
    /// <summary>
    /// platform-model.md §7 (C-340): CMP-1010 and CMP-1366 end to end. The second platform is the baseline target under another id
    /// with one more predefined symbol, installed through the engine's test seam (CMP-1380); one source, two executables in
    /// their own directories, which print different lines.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1010")]
    [Trait("Req", "CMP-1366")]
    [Trait("Req", "CMP-1371")]
    public async Task Platforms_TwoDescriptors_BothExecutablesRunAndDiffer()
    {
        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var other = TargetInfo.Win64 with
        {
            Id = "test64",
            DisplayName = "Test 64",
            PredefinedSymbols = [.. TargetInfo.Win64.PredefinedSymbols, "TESTPLATFORM"],
        };
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Hello.pproj"), """
                {
                  "formatVersion": 1,
                  "name": "Hello",
                  "mainSource": "Hello.pas",
                  "platforms": ["win64", "test64"],
                  "activePlatform": "test64"
                }
                """);
            File.WriteAllText(Path.Combine(dir, "Hello.pas"), """
                program Hello;
                begin
                  {$IFDEF TESTPLATFORM}Writeln('test platform');{$ELSE}Writeln('baseline platform');{$ENDIF}
                end.
                """);

            async Task<(BuildResult Result, EventLog Log)> BuildAsync(string? platform)
            {
                var log = new EventLog();
                var result = await new BuildEngine(PhysicalFileSystem.Instance) { InstalledTargets = [TargetInfo.Win64, other] }.RunAsync(
                    new BuildRequest
                    {
                        ProjectPath = Path.Combine(dir, "Hello.pproj"),
                        Platform = platform,
                        ToolchainDirectory = bin,
                        RtlDirectory = Path.Combine(RepoPaths.Root, "rtl", "src"),
                    },
                    log,
                    TestContext.Current.CancellationToken);
                Assert.True(result.Success, string.Join("\n", log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText())));
                return (result, log);
            }

            async Task<List<string>> RunAsync(string exe)
            {
                var output = new List<string>();
                Assert.Equal(0, await NativeRun.RunAsync(exe, dir, output.Add, _ => { }));
                return output;
            }

            // the baseline platform, named; then the active one
            var baselineExe = Path.Combine(dir, "bin", "Debug", "Hello.exe");
            var otherExe = Path.Combine(dir, "bin", "test64", "Debug", "Hello.exe");
            var baseline = await BuildAsync("win64");
            Assert.Equal(baselineExe, baseline.Result.OutputPath, ignoreCase: true);
            var baselineBytes = File.ReadAllBytes(baselineExe);
            var active = await BuildAsync(null);
            Assert.Equal("test64", active.Log.Events.OfType<BuildEvent.Started>().Single().Platform);
            Assert.Equal(otherExe, active.Result.OutputPath, ignoreCase: true);
            Assert.Contains("Compiling Hello", active.Log.Info);                          // nothing taken from the other platform's cache

            // both executables exist in their own directories, with their own intermediates, and the first one is unchanged
            Assert.True(File.Exists(baselineExe));
            Assert.True(File.Exists(otherExe));
            Assert.True(File.Exists(Path.Combine(dir, "obj", "Debug", "Hello.obj")));
            Assert.True(File.Exists(Path.Combine(dir, "obj", "test64", "Debug", "Hello.obj")));
            Assert.Equal(baselineBytes, File.ReadAllBytes(baselineExe));

            // they run and print different lines
            Assert.Equal(["baseline platform"], await RunAsync(baselineExe));
            Assert.Equal(["test platform"], await RunAsync(otherExe));

            // each is up to date afterwards
            Assert.True((await BuildAsync("win64")).Result.UpToDate);
            Assert.True((await BuildAsync("test64")).Result.UpToDate);
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }
}
