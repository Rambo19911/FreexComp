using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests;

/// <summary>
/// I-128 (phase3-build-run.md §11, real-toolchain scenario): a <c>console-pascal</c> project built by the real build engine
/// with the LLVM and RTL found by the automatic toolchain search of the checkout, run as a real process with
/// <c>run.consoleMode = output</c>.
/// </summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
[Trait("Category", "Toolchain")]
public sealed class ToolchainAcceptanceTests
{
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-P3-080")]
    [Trait("Req", "IDE-P3-084")]
    public async Task BuildAndRunConsoleHello()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.RealBuild = true);
        var toolchain = h.Services.Toolchain.Resolve(); // the automatic search from the IDE folder (IDE-P3-010)
        if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null)
            Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips

        Assert.Equal(NewProjectOutcome.Created, await h.Services.Projects.CreateProjectAsync(new NewProjectRequest("console-pascal", "Hello", h.TempDir)));
        Assert.NotNull(h.Services.Projects.Project);

        h.Press("Ctrl+F5"); // build with the real engine, then run
        string Output() => h.TryFind<TextEditor>("Tool.Output.Text")?.Document.Text ?? "";
        // The exit line of Output (the status message lasts 5 s of the manual clock, which runs fast while the program runs).
        await h.WaitForAsync(() => Output().Contains("Program exited with code", StringComparison.Ordinal) || h.Services.Messages.ErrorCount > 0,
            "build and run finished"); // I-205: bounded by the test's own timeout (120 s)
        Assert.True(h.Services.Messages.ErrorCount == 0,
            "Build errors: " + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}"))
            + " Log: " + string.Join(" | ", h.Log.Lines.Where(l => l.StartsWith("ERROR ", StringComparison.Ordinal))));
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "idle after the run");
        Assert.Contains("Program exited with code 0 after ", Output(), StringComparison.Ordinal);
        Assert.Contains("Hello, world", Output(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(h.TempDir, "Hello", "bin", "Debug", "Hello.exe")));
    }
}
