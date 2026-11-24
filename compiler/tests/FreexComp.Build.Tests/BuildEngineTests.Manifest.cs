using FreexComp.Compiler;
using FreexComp.Tests.Shared;

namespace FreexComp.Build.Tests;

/// <summary>app-manifest.md (CMP-1100, CMP-1113, CMP-1114; task C-296): the project's <c>manifest</c> key through the build engine.</summary>
public sealed partial class BuildEngineTests
{
    /// <summary>CMP-1114: a custom manifest file that does not exist is E0002 with its path; the units are compiled, nothing is linked.</summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1114")]
    [InlineData("""{ "customFile": "res/missing.manifest" }""")]
    [InlineData("\"res/missing.manifest\"")]
    public async Task MissingCustomManifest_E0002(string manifest)
    {
        var w = new BuildWorkspace(projectJson: BuildWorkspace.ProjectJson("Hello", extra: "\"manifest\": " + manifest));

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success);
        Assert.Equal(
            [@"C:\p\res\missing.manifest(0,0): error E0002: File not found: 'C:\p\res\missing.manifest'"],
            log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText()));
        Assert.DoesNotContain("lld-link.exe", w.Tools.Calls);
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));
        Assert.False(w.Fs.FileExists(@"C:\p\obj\Debug\link.rsp"));

        // the file appears: the next build links it
        w.Fs.Add(@"C:\p\res\missing.manifest", "<assembly/>");
        (result, log) = await w.RunAsync(w.Request());
        Assert.True(result.Success, string.Join("\n", log.Lines));
        Assert.Contains(@"/manifestinput:C:\p\res\missing.manifest", w.Fs.ReadAllText(@"C:\p\obj\Debug\link.rsp").Split('\n'));
    }

    /// <summary>CMP-1100, CMP-1113: a console project without the key links the generated console manifest; the object form sets its level.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1100")]
    [Trait("Req", "CMP-1113")]
    public async Task ManifestAbsent_ConsoleProject_LinksTheGeneratedManifest()
    {
        var w = new BuildWorkspace();

        var (result, log) = await w.RunAsync(w.Request());

        Assert.True(result.Success, string.Join("\n", log.Lines));
        var text = w.Fs.ReadAllText(@"C:\p\obj\Debug\app.manifest");
        Assert.Contains("<longPathAware", text, StringComparison.Ordinal);
        Assert.Contains("level=\"asInvoker\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Common-Controls", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dpiAware", text, StringComparison.Ordinal);
        var rsp = w.Fs.ReadAllText(@"C:\p\obj\Debug\link.rsp").Split('\n');
        Assert.Contains(@"/manifestinput:C:\p\obj\Debug\app.manifest", rsp);
        Assert.DoesNotContain(rsp, l => l.StartsWith("/manifestuac", StringComparison.Ordinal));

        // the object form: only the link runs again, and the linker is told the level its merger would otherwise contradict
        w.Fs.Add(BuildWorkspace.ProjectPath, BuildWorkspace.ProjectJson("Hello", extra: """ "manifest": { "executionLevel": "highestAvailable" } """.Trim()));
        var clangBefore = w.Tools.Calls.Count(c => c == "clang.exe");
        (result, log) = await w.RunAsync(w.Request());
        Assert.True(result.Success, string.Join("\n", log.Lines));
        Assert.Equal(["Linking Hello.exe"], log.Info);
        Assert.Equal(clangBefore, w.Tools.Calls.Count(c => c == "clang.exe"));
        Assert.Contains("level=\"highestAvailable\"", w.Fs.ReadAllText(@"C:\p\obj\Debug\app.manifest"), StringComparison.Ordinal);
        Assert.Contains("/manifestuac:level='highestAvailable'", w.Fs.ReadAllText(@"C:\p\obj\Debug\link.rsp").Split('\n'));
    }

    /// <summary>
    /// Card F03-001 acceptance 3, with the pinned toolchain: the project built with the default manifest and with
    /// <c>"executionLevel": "requireAdministrator"</c> — the two embedded manifests differ in that attribute only. The second
    /// executable is never started (it would raise an elevation prompt).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Kind", "Native")]
    [Trait("Category", "Toolchain")]
    [Trait("Req", "CMP-1104")]
    [Trait("Req", "CMP-1113")]
    public async Task ManifestExecutionLevel_RequireAdministrator_ChangesOnlyThatElement()
    {
        var bin = Path.Combine(RepoPaths.Root, "third_party", "llvm", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "clang.exe")), $"Pinned toolchain missing in {bin} (run scripts/bootstrap.ps1).");
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Hello.pas"), BuildWorkspace.HelloProgram);
            var exe = Path.Combine(dir, "bin", "Debug", "Hello.exe");

            async Task<string> BuildAsync(string extra)
            {
                File.WriteAllText(Path.Combine(dir, "Hello.pproj"), BuildWorkspace.ProjectJson("Hello", extra: extra));
                var log = new EventLog();
                var result = await new BuildEngine(PhysicalFileSystem.Instance).RunAsync(
                    new BuildRequest
                    {
                        ProjectPath = Path.Combine(dir, "Hello.pproj"),
                        ToolchainDirectory = bin,
                        RtlDirectory = Path.Combine(RepoPaths.Root, "rtl", "src"),
                    },
                    log,
                    TestContext.Current.CancellationToken);
                Assert.True(result.Success, string.Join("\n", log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText())));
                return Assert.IsType<string>(PeManifest.Read(exe));
            }

            var standard = await BuildAsync("");
            var elevated = await BuildAsync(""" "manifest": { "executionLevel": "requireAdministrator" } """.Trim());

            Assert.Contains("requireAdministrator", elevated, StringComparison.Ordinal);
            Assert.DoesNotContain("requireAdministrator", standard, StringComparison.Ordinal);
            Assert.NotEqual(PeManifest.Canonical(standard), PeManifest.Canonical(elevated));
            Assert.Equal(
                PeManifest.Canonical(standard),
                PeManifest.Canonical(elevated).Replace("level=\"requireAdministrator\"", "level=\"asInvoker\"", StringComparison.Ordinal));
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }
}
