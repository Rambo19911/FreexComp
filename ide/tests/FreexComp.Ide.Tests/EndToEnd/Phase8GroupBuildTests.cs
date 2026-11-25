using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// Card F09-009 acceptance 3 end to end, with the real build engine and toolchain (project-manager.md IDE-PM-171…173):
/// a group of three projects on disk, the second with a syntax error.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class Phase8GroupBuildTests
{
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-PM-171")]
    [Trait("Req", "IDE-PM-172")]
    [Trait("Req", "IDE-PM-173")]
    public async Task E2E_Phase8_GroupBuildAll_StopsAtTheFailingProject()
    {
        static void Project(HarnessOptions o, string name, string body)
        {
            o.InitialFiles[$@"{name}\{name}.pproj"] = $$"""
                { "formatVersion": 1, "name": "{{name}}", "appType": "console", "mainSource": "{{name}}.pas",
                  "files": [ { "path": "{{name}}.pas" } ] }
                """;
            o.InitialFiles[$@"{name}\{name}.pas"] = $"program {name};\r\n\r\nbegin\r\n  {body}\r\nend.\r\n";
        }

        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.RealBuild = true;
            Project(o, "Alpha", "WriteLn('alpha');");
            Project(o, "Beta", "WriteLn('beta'"); // syntax error: the call is not closed
            Project(o, "Gamma", "WriteLn('gamma');");
            // The active project is built last among independent projects: the order is Alpha, Beta, Gamma.
            o.InitialFiles["All.pgroup"] =
                """{ "formatVersion": 1, "projects": ["Alpha/Alpha.pproj", "Beta/Beta.pproj", "Gamma/Gamma.pproj"], "activeProject": "Gamma/Gamma.pproj" }""";
        });
        var toolchain = h.Services.Toolchain.Resolve();
        if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null)
            Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips
        string Exe(string name) => Path.Combine(h.TempDir, name, "bin", "Debug", name + ".exe");
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "All.pgroup")));
        var pm = (ProjectManagerViewModel)h.Services.ToolWindows.Get(ProjectManagerViewModel.ToolName)!;
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        Assert.Equal("Gamma", h.Services.Projects.Project!.Name);
        string Output() => string.Join(" | ", h.Services.Output.GetLines(OutputChannels.Build).Select(l => l.Text));
        string Diagnostics() => $"state {h.Services.State.Current.State}; result: {h.Services.Build.ResultText}; messages: "
            + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}"))
            + "; log: " + string.Join(" | ", h.Log.Lines.Where(l => l.StartsWith("ERROR ", StringComparison.Ordinal)))
            + "; output: " + Output();

        async Task BuildAllAsync(string expectedSummary)
        {
            var finished = 0;
            void OnFinished(object? sender, EventArgs e) => finished++;
            h.Services.Build.BuildFinished += OnFinished;
            try
            {
                Assert.True(h.Services.Commands.CanExecute("project.buildAll"));
                await h.ExecuteAsync("project.buildAll");
                await h.WaitForAsync(() => finished > 0 && h.Services.State.Current.State == IdeState.Idle, "Build All finished");
            }
            catch (HarnessAssertionException e)
            {
                Assert.Fail(e.Message + " — " + Diagnostics());
            }
            finally
            {
                h.Services.Build.BuildFinished -= OnFinished;
            }

            Assert.True(h.Services.Build.ResultText == expectedSummary, Diagnostics());
            await h.WaitForAsync(() => Output().Contains(expectedSummary, StringComparison.Ordinal), "the summary is in Output");
        }

        // Default (build.continueAfterFailure off): Alpha is built, Beta fails, Gamma is not started.
        await BuildAllAsync("1 succeeded, 1 failed, 1 skipped: Gamma");
        Assert.Contains("Gamma: skipped because the build stopped after Beta failed", Output(), StringComparison.Ordinal);
        Assert.True(h.Services.Messages.ErrorCount >= 1, Diagnostics());
        Assert.All(h.Services.Messages.Items.Where(i => i.Severity == MessageSeverity.Error && !i.IsSummary),
            i => Assert.EndsWith("Beta.pas", i.Location?.FilePath ?? "Beta.pas", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Exe("Alpha")), Diagnostics());
        Assert.False(File.Exists(Exe("Beta")));
        Assert.False(File.Exists(Exe("Gamma")));
        Assert.Equal("Gamma", h.Services.Projects.Project!.Name); // the active project did not change; nothing was run
        Assert.DoesNotContain("Program exited", Output(), StringComparison.Ordinal);

        // "Continue with the other projects when a project fails": Gamma is built although Beta failed.
        h.Services.Settings.Set(SettingsCatalog.BuildContinueAfterFailure, true);
        Dispatcher.UIThread.RunJobs();
        await BuildAllAsync("2 succeeded, 1 failed, 0 skipped");
        Assert.DoesNotContain("Gamma: skipped", Output(), StringComparison.Ordinal);
        Assert.True(File.Exists(Exe("Gamma")), Diagnostics());
        Assert.False(File.Exists(Exe("Beta")));
        Assert.Empty(h.Messages.Requests);
    }
}
