using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>Card F01-003 in the IDE with the real build engine and toolchain (phase8-build-options.md §4).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class Phase8LibraryTests
{
    /// <summary>
    /// IDE-TGT-016: a project created from the template <c>library-pascal</c> builds without diagnostics and produces
    /// <c>&lt;name&gt;.dll</c> and <c>&lt;name&gt;.lib</c>; without a host application it is not runnable (IDE-TGT-013).
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-TGT-016")]
    [Trait("Req", "IDE-TGT-015")]
    [Trait("Req", "IDE-TGT-011")]
    public async Task E2E_Phase8_LibraryTemplateBuildsDll()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.RealBuild = true);
        var toolchain = h.Services.Toolchain.Resolve();
        if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null)
            Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips

        Assert.Equal(NewProjectOutcome.Created, await h.Services.Projects.CreateProjectAsync(new NewProjectRequest("library-pascal", "Mathlib", h.TempDir)));
        var dir = Path.Combine(h.TempDir, "Mathlib");
        Assert.Equal("library", h.Services.Projects.Project!.AppType);
        Assert.StartsWith("library Mathlib;", File.ReadAllText(Path.Combine(dir, "Mathlib.pas")), StringComparison.Ordinal);

        string Output() => h.TryFind<TextEditor>("Tool.Output.Text")?.Document.Text ?? "";
        string Diagnostics() => $"state {h.Services.State.Current.State}; messages: "
            + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}"))
            + "; log: " + string.Join(" | ", h.Log.Lines.Where(l => l.StartsWith("ERROR ", StringComparison.Ordinal)))
            + "; output: " + Output();
        var dll = Path.Combine(dir, @"bin\Debug\Mathlib.dll");
        await h.ExecuteAsync("project.build");
        try
        {
            await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle && (File.Exists(dll) || h.Services.Messages.ErrorCount > 0),
                "the build finished");
        }
        catch (HarnessAssertionException e)
        {
            Assert.Fail(e.Message + " — " + Diagnostics());
        }

        Assert.True(h.Services.Messages.Items.All(i => string.IsNullOrEmpty(i.Code)), Diagnostics()); // zero diagnostics: only the build summary line
        Assert.True(File.Exists(dll), Diagnostics());
        Assert.True(File.Exists(Path.Combine(dir, @"bin\Debug\Mathlib.lib")), Diagnostics());
        h.Clock.Advance(TimeSpan.FromMilliseconds(50)); // batched Output reaches its channel (IDE-P3-070)
        Assert.Contains(h.Services.Output.GetLines(Core.Build.OutputChannels.Build), l => l.Text.Contains("Linking Mathlib.dll", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(dir, @"bin\Debug\Mathlib.exe")));

        // The Project Manager shows the DLL the build wrote; Run has nothing to start without a host application.
        var root = h.Services.ProjectManager.Find(Core.ViewModels.ProjectManagerViewModel.RootKey)!;
        Assert.Equal((true, dll), (root.IsLibrary, root.ToolTipText));
        Assert.False(await h.Services.Run.StartAsync());
        Assert.Equal(Core.Resources.Strings.Msg_Run_NotRunnable, h.Services.Status.Message);
        Assert.Empty(h.Messages.Requests);
    }
}
