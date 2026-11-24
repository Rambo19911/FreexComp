using AvaloniaEdit;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>
/// Card F02-004 in the IDE with the real build engine, toolchain and debug adapter (library-source.md §3): Step Into at a
/// call of a run-time library routine enters the library's source only with "Step Into Library Source" on. Shared by
/// <see cref="Phase8LibraryTests"/> and the cross-area test of the lead-owned E2E project.
/// </summary>
public static class LibrarySteppingScenario
{
    private const string Source = """
        program LibStep;

        uses
          SysUtils;

        var
          S, T: string;

        begin
          S := 'step';
          T := UpperCase(S);
          WriteLn(T);
        end.

        """;

    /// <summary>
    /// A console program calls <c>UpperCase</c> of SysUtils, with a breakpoint on the line of the call. With the toggle off
    /// (the default) Step Into steps over the call and stays in the program's file (IDE-LIB-022). With it on, Step Into
    /// stops in <c>SysUtils.pas</c> under the run-time library root; that file is the active document, protected and
    /// read-only (IDE-LIB-002, IDE-LIB-023), and the top frame of the call stack is the library routine. Must be called on
    /// the headless UI thread; a missing toolchain fails.
    /// </summary>
    public static async Task StepIntoSysUtilsAsync()
    {
        var source = Source.Replace("\r\n", "\n", StringComparison.Ordinal);
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.RealBuild = true;
            o.InitialFiles[@"LibStep\LibStep.pproj"] = """
                { "formatVersion": 1, "name": "LibStep", "appType": "console", "mainSource": "LibStep.pas",
                  "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } },
                  "files": [ { "path": "LibStep.pas" } ] }
                """;
            o.InitialFiles[@"LibStep\LibStep.pas"] = source;
        });
        var toolchain = h.Services.Toolchain.Resolve();
        if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null)
            Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips

        var program = Path.Combine(h.TempDir, @"LibStep\LibStep.pas");
        var callLine = source.Split('\n').ToList().FindIndex(l => l.Contains("UpperCase(S)", StringComparison.Ordinal)) + 1;
        var debug = h.Services.Debug!;
        string Output() => h.TryFind<TextEditor>("Tool.Output.Text")?.Document.Text ?? "";
        string Diagnostics() => $"state {h.Services.State.Current.State} at {h.Services.State.Current.PausedFile}({h.Services.State.Current.PausedLine}); messages: "
            + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}"))
            + "; boxes: " + string.Join(" | ", h.Messages.Requests.Select(r => $"{r.MessageId}({string.Join(", ", r.Args)})"))
            + "; log: " + string.Join(" | ", h.Log.Lines.Where(l => !l.StartsWith("INFO ", StringComparison.Ordinal)))
            + "; debugger: " + string.Join(" | ", h.Services.Output.GetLines(Core.Build.OutputChannels.Debugger).Select(l => l.Text))
            + "; output: " + Output();
        async Task WaitAsync(Func<bool> condition, string step)
        {
            try
            {
                await h.WaitForAsync(condition, step);
            }
            catch (HarnessAssertionException e)
            {
                Assert.Fail(e.Message + " — " + Diagnostics());
            }
        }

        // Waits for the stop that follows <paramref name="command"/>: paused somewhere else than before, or the session over.
        async Task<IdeStateInfo> StopAfterAsync(string command, string step)
        {
            var before = h.Services.State.Current;
            await h.ExecuteAsync(command);
            await WaitAsync(
                () => h.Services.State.Current is var now
                    && ((now.State == IdeState.Paused && (now.PausedFile, now.PausedLine) != (before.PausedFile, before.PausedLine))
                        || (now.State == IdeState.Idle && debug.IsIdle)),
                step);
            Assert.True(h.Services.State.Current.State == IdeState.Paused, step + " — " + Diagnostics());
            await WaitAsync(() => !debug.HasViewWork, "the debug views' requests");
            return h.Services.State.Current;
        }

        async Task ContinueToEndAsync()
        {
            await h.ExecuteAsync("run.start");
            await WaitAsync(() => h.Services.State.Current.State == IdeState.Idle && debug.IsIdle, "the end of the program");
        }

        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, @"LibStep\LibStep.pproj")));
        h.Services.Breakpoints!.Toggle(program, callLine);

        // Off (the default): Step Into at the call steps over it — the next line of the program.
        Assert.False(debug.StepIntoLibrarySource);
        var stop = await StopAfterAsync("run.start", "the stop at the breakpoint");
        Assert.Equal((program.ToUpperInvariant(), callLine), (stop.PausedFile?.ToUpperInvariant(), stop.PausedLine));
        stop = await StopAfterAsync("run.stepInto", "the stop after Step Into with library stepping off");
        Assert.Equal((program.ToUpperInvariant(), callLine + 1), (stop.PausedFile?.ToUpperInvariant(), stop.PausedLine));
        Assert.DoesNotContain(h.Services.Documents.Documents, d => h.Services.LibrarySource.IsLibrarySource(d.FilePath ?? ""));
        await ContinueToEndAsync();

        // On: the same Step Into stops in the library routine.
        await h.ExecuteAsync("run.toggleLibraryStepping");
        Assert.True(debug.StepIntoLibrarySource);
        stop = await StopAfterAsync("run.start", "the stop at the breakpoint with library stepping on");
        Assert.Equal((program.ToUpperInvariant(), callLine), (stop.PausedFile?.ToUpperInvariant(), stop.PausedLine));
        stop = await StopAfterAsync("run.stepInto", "the stop in the library source");
        var unit = Path.Combine(h.Services.LibrarySource.Roots[0], "SysUtils.pas");
        Assert.True(string.Equals(unit, stop.PausedFile, StringComparison.OrdinalIgnoreCase), Diagnostics());
        Assert.True(h.Services.LibrarySource.IsLibrarySource(stop.PausedFile!), Diagnostics());

        // The stop location is open as a protected (read-only) library document.
        await WaitAsync(() => string.Equals(h.Services.Documents.ActiveDocument?.FilePath, unit, StringComparison.OrdinalIgnoreCase), "the editor shows the library file");
        var document = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.True(document.IsLibraryProtected);
        Assert.True(document.Buffer.IsReadOnly);
        Assert.Equal(TextDocumentViewModel.LibrarySourceInfoBarKind, document.VisibleInfoBars[0].Kind);

        // The call stack: the library routine on top, called from the program's line.
        var frames = debug.GetFramesAsync();
        await WaitAsync(() => frames.IsCompleted, "the call stack");
        var stack = (await frames)!.Frames;
        Assert.Equal("UpperCase", stack[0].Name);
        Assert.Equal(unit, stack[0].Location?.Path, ignoreCase: true);
        Assert.Equal((program.ToUpperInvariant(), callLine), (stack[1].Location?.Path.ToUpperInvariant(), stack[1].Location?.Line));

        await ContinueToEndAsync();
        h.Clock.Advance(TimeSpan.FromMilliseconds(50)); // batched Output reaches its channel (IDE-P3-070)
        Assert.True(h.Services.Output.GetLines(Core.Build.OutputChannels.Program).Any(l => l.Text == "STEP"), Diagnostics());
        Assert.Empty(h.Messages.Requests);
    }
}
