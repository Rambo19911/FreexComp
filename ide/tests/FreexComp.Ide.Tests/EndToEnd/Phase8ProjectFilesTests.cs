using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// Card F09-054 acceptance with the real build engine and toolchain (phase8-project-files.md §9): file operations of the
/// Project Manager leave a project that still builds and runs.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class Phase8ProjectFilesTests
{
    /// <summary>
    /// Card acceptance 3 / IDE-PM-125: a unit is moved into a new sub-folder through the Project Manager (New Folder,
    /// in-place naming, move); no source text is edited, <c>searchPaths</c> is untouched, and the project builds and runs
    /// as before because the build finds units through the directories of the project's files.
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-PM-125")]
    [Trait("Req", "IDE-PM-111")]
    [Trait("Req", "IDE-PM-123")]
    public async Task E2E_Phase8_MoveUnitIntoSubfolder_ProjectStillBuilds()
    {
        const string Program = "program Hello;\r\n\r\nuses Greeter;\r\n\r\nbegin\r\n  SayHello;\r\nend.\r\n";
        const string Unit = "unit Greeter;\r\n\r\ninterface\r\n\r\nprocedure SayHello;\r\n\r\nimplementation\r\n\r\nprocedure SayHello;\r\nbegin\r\n  WriteLn('moved and built');\r\nend;\r\n\r\nend.\r\n";
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.RealBuild = true;
            o.InitialFiles[@"Hello\Hello.pproj"] = """
                { "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas",
                  "files": [ { "path": "Hello.pas" }, { "path": "Greeter.pas" } ] }
                """;
            o.InitialFiles[@"Hello\Hello.pas"] = Program;
            o.InitialFiles[@"Hello\Greeter.pas"] = Unit;
        });
        var toolchain = h.Services.Toolchain.Resolve();
        if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null)
            Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips
        string Abs(string relative) => Path.Combine(h.TempDir, "Hello", relative);
        Assert.True(await h.Services.Projects.OpenAsync(Abs("Hello.pproj")));
        var pm = (ProjectManagerViewModel)h.Services.ToolWindows.Get(ProjectManagerViewModel.ToolName)!;
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        string Output() => h.TryFind<TextEditor>("Tool.Output.Text")?.Document.Text ?? "";

        async Task BuildAndRunAsync(int run)
        {
            // The Output pane may be cleared by the build or appended to: this run has ended when it shows one more exit
            // line than the part of it that was there before (none of it, when it was cleared).
            static int Exits(string text) => text.Split("Program exited with code").Length - 1;
            var before = Output();
            string Diagnostics() => $"state {h.Services.State.Current.State}; messages: "
                + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}"))
                + "; log: " + string.Join(" | ", h.Log.Lines.Where(l => l.StartsWith("ERROR ", StringComparison.Ordinal)))
                + "; output: " + Output();
            await h.ExecuteAsync("run.startWithoutDebugging"); // build with the real engine, then run
            try
            {
                await h.WaitForAsync(
                    () => Exits(Output()) > (Output().StartsWith(before, StringComparison.Ordinal) ? Exits(before) : 0) || h.Services.Messages.ErrorCount > 0,
                    $"build and run {run} finished");
            }
            catch (HarnessAssertionException e)
            {
                Assert.Fail(e.Message + " — " + Diagnostics());
            }

            Assert.True(h.Services.Messages.ErrorCount == 0, Diagnostics());
            await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "idle after the run");
            Assert.Contains("moved and built", Output()[(Output().StartsWith(before, StringComparison.Ordinal) ? before.Length : 0)..], StringComparison.Ordinal);
            Assert.Contains("Program exited with code 0 after ", Output(), StringComparison.Ordinal);
        }

        // Before: the project builds and runs.
        await BuildAndRunAsync(1);

        // New Folder under the root, named in place; the unit is moved into it.
        Assert.True(h.Services.ToolWindows.Show(ProjectManagerViewModel.ToolName, focus: true));
        Dispatcher.UIThread.RunJobs();
        pm.Select(pm.Find(ProjectManagerViewModel.RootKey)!);
        await h.ExecuteAsync("project.newFolder");
        var folder = Assert.Single(pm.Selection);
        Assert.Equal("NewFolder1/", folder.Key);
        folder.Rename!.NewName = "lib";
        Assert.True(await folder.Rename.CommitAsync());
        Assert.True(await pm.MoveAsync([pm.Find("Greeter.pas")!], pm.Find("lib/")!));
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "check after the move");

        Assert.True(File.Exists(Abs(@"lib\Greeter.pas")));
        Assert.False(File.Exists(Abs("Greeter.pas")));
        Assert.Equal(["Hello.pas", "lib/Greeter.pas"], h.Services.Projects.Project!.Files.Select(f => f.Path));
        Assert.Empty(h.Services.Projects.Definition!.SearchPaths); // IDE-PM-125: never edited by a move
        Assert.Equal(Program, File.ReadAllText(Abs("Hello.pas"))); // no source text is edited
        Assert.Equal(Unit, File.ReadAllText(Abs(@"lib\Greeter.pas")));

        // After: it builds and runs without any other change.
        File.Delete(Abs(@"bin\Debug\Hello.exe"));
        await BuildAndRunAsync(2);
        Assert.True(File.Exists(Abs(@"bin\Debug\Hello.exe")));
        Assert.Empty(h.Messages.Requests);
    }
}
