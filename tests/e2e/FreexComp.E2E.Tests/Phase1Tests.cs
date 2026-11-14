using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FreexComp.Build;
using FreexComp.Ide.Tests;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using CompilerFileSystem = FreexComp.Compiler.PhysicalFileSystem;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace FreexComp.E2E.Tests;

/// <summary>
/// The Phase 1 exit check (ROADMAP §3, docs/spec/ide/testing.md §5.5): phase1-shell.md §14 steps 1–10 through the
/// headless shell with the real file system, the real project serializer and the real lexer/classifier — no fakes
/// for the compiler area's parts. The scenario body is shared with <see cref="Phase1AcceptanceTests"/>.
/// </summary>
[Trait("Phase", "1")]
[Trait("Kind", "E2E")]
[Trait("Category", "Integration")]
public sealed class Phase1Tests
{
    [AvaloniaFact(Timeout = 30000)]
    public async Task E2E_Phase1_CreateEditSaveReopenProject()
    {
        using var temp = new TempDirectory();
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Settings.Clear(); // step 1: an empty user directory
            o.Root = temp.Combine("ide");
        });
        Assert.StartsWith(temp.Path, h.TempDir, StringComparison.OrdinalIgnoreCase);

        var pproj = await Phase1Scenario.CreateEditSaveReopenAsync(h);

        // The project file the IDE wrote loads with the build engine's own reader, without diagnostics.
        var loaded = ProjectSerializer.LoadProject(pproj, CompilerFileSystem.Instance);
        Assert.Empty(loaded.Diagnostics);
        var project = Assert.IsType<Project>(loaded.Value);
        Assert.Equal("Hello", project.Name);
        Assert.Equal(["Hello.pas", "Unit1.pas"], project.Files.Select(f => f.Path).Order(StringComparer.Ordinal));
    }
}
