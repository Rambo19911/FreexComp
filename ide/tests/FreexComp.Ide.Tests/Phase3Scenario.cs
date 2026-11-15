using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests;

/// <summary>
/// The Phase 3 acceptance scenario (phase3-build-run.md §11 steps 1–6, <c>run.consoleMode = output</c>): build the project
/// <c>HelloErr</c>, navigate to its error, follow an edit, fix, run, then run again and stop. Runs against the fakes
/// (<see cref="Phase3AcceptanceTests"/>) or, on a harness started with <see cref="HarnessOptions.RealBuild"/>, against the
/// real build engine, compiler and toolchain and a real process (the cross-area <c>E2E_Phase3_BuildErrorNavigateFixRun</c>).
/// </summary>
public static class Phase3Scenario
{
    /// <summary>The unit of <c>HelloErr</c>: the undeclared identifier <c>Foo</c> at line 12, column 5.</summary>
    public const string UnitText = "unit Unit1;\r\ninterface\r\nprocedure Run;\r\nimplementation\r\nvar\r\n  Count: Integer;\r\nprocedure Run;\r\n" +
                                   "begin\r\n  Count := 0;\r\n  Count := Count + 1;\r\n  // set the value\r\n    Foo := 1;\r\nend;\r\nend.\r\n";

    /// <summary>
    /// The program of <c>HelloErr</c>: writes <c>Hello</c> and exits with 0, or, while <see cref="WaitFlag"/> exists next to
    /// the executable, keeps running until it is stopped (step 6 with a real process).
    /// </summary>
    public const string ProgramText = "program HelloErr;\r\n\r\nuses\r\n  SysUtils, Unit1;\r\n\r\nbegin\r\n  Run;\r\n  WriteLn('Hello');\r\n" +
                                      "  if FileExists(ExtractFilePath(ParamStr(0)) + 'wait.flag') then\r\n    while True do\r\n      Sleep(50);\r\nend.\r\n";

    public const string WaitFlag = "wait.flag";

    public static string ProjectPath(HeadlessHarness h) => Path.Combine(h.TempDir, @"HelloErr\HelloErr.pproj");

    public static string UnitPath(HeadlessHarness h) => Path.Combine(h.TempDir, @"HelloErr\Unit1.pas");

    public static string OutputDir(HeadlessHarness h) => Path.Combine(h.TempDir, @"HelloErr\bin\Debug");

    /// <summary>
    /// Step 1: a harness at the product's phase with <c>HelloErr</c> open. <paramref name="realBuild"/> false = the fake engine
    /// and launcher over an empty toolchain; true = <see cref="HarnessOptions.RealBuild"/> (the toolchain is searched from the
    /// test assembly's folder). <paramref name="configure"/> runs last (e.g. <see cref="HarnessOptions.Root"/>).
    /// </summary>
    public static async Task<HeadlessHarness> StartAsync(bool realBuild, Action<HarnessOptions>? configure = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Root = root;
            o.ImplementedPhase = null;
            o.RealBuild = realBuild;
            if (!realBuild)
            {
                var work = Path.Combine(root, "work");
                foreach (var tool in (string[])[@"tc\llvm\clang.exe", @"tc\llvm\lld-link.exe", @"tc\llvm\llvm-dlltool.exe", @"tc\rtl\System.pas"])
                    o.InitialFiles[tool] = "";
                o.Settings["toolchain.llvmBinDir"] = Path.Combine(work, @"tc\llvm");
                o.Settings["toolchain.rtlDir"] = Path.Combine(work, @"tc\rtl");
            }

            o.InitialFiles[@"HelloErr\HelloErr.pproj"] = """
                { "formatVersion": 1, "name": "HelloErr", "appType": "console", "mainSource": "HelloErr.pas",
                  "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                      "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release" } },
                  "files": [ { "path": "HelloErr.pas" }, { "path": "Unit1.pas" } ] }
                """;
            o.InitialFiles[@"HelloErr\HelloErr.pas"] = ProgramText;
            o.InitialFiles[@"HelloErr\Unit1.pas"] = UnitText;
            o.InitialFiles[@"HelloErr\bin\Debug\.keep"] = "";
            configure?.Invoke(o);
        });
        h.Build.DefaultOutputPath = Path.Combine(OutputDir(h), "HelloErr.exe");
        if (!await h.Services.Projects.OpenAsync(ProjectPath(h)))
            throw new HarnessAssertionException("HelloErr.pproj did not open");
        return h;
    }

    /// <summary>Steps 2–6 on a harness from <see cref="StartAsync"/>.</summary>
    public static async Task BuildErrorNavigateFixRunAsync(HeadlessHarness h)
    {
        Assert.Equal(3, ServiceGraph.ImplementedPhase); // the product is at Phase 3: no test override of the implemented phase
        var real = h.RealBuild;
        // I-205: every wait, on the real compiler, linker and program too, is bounded by the test's own timeout.
        if (real)
        {
            var toolchain = h.Services.Toolchain.Resolve();
            if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null)
                Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips
        }

        var unitPath = UnitPath(h);
        if (!real)
            h.Build.Enqueue(FakeBuildEngine.Failure(ProjectPath(h),
                FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier 'Foo'", unitPath, 12, 5, 3)));

        // 2. Ctrl+Shift+B: Building, then Idle with one error.
        var states = new List<IdeState>();
        h.Services.State.Changed += (_, _) => states.Add(h.Services.State.Current.State);
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => states.Contains(IdeState.Building) && h.Services.State.Current.State == IdeState.Idle, "build finished");
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Services.Messages.ErrorCount == 1, "Messages: " + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}")));
        Assert.Equal(Strings.Msg_Build_FailedOne, h.Status.State);
        await h.WaitForAsync(() => h.TryFind<TextBlock>("Tool.Messages.Row.0.Code")?.Text == "E2003", "error row");
        Assert.Equal(MessagesViewModel.ToolName, h.Services.Layout.Layout.Bottom.ActiveTool);
        Assert.StartsWith("⊗ 1", h.Status.Field("Diagnostics"), StringComparison.Ordinal);

        // 3. F8: Unit1.pas at 12:5, editor focused.
        h.Press("F8");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Unit1.pas", "editor focused");
        var editor = h.Find<TextEditor>("Editor.Unit1.pas");
        var start = editor.Document.GetLocation(editor.SelectionStart);
        Assert.Equal((12, 5), (start.Line, start.Column));

        // 4. A new line above line 12; F8 wraps to the same message, which followed the edit.
        editor.Document.Insert(editor.Document.GetLineByNumber(12).Offset, "\r\n");
        Dispatcher.UIThread.RunJobs();
        h.Press("F8");
        await h.WaitForAsync(() => editor.Document.GetLocation(editor.SelectionStart).Line == 13, "anchor followed the edit");
        Assert.Equal(5, editor.Document.GetLocation(editor.SelectionStart).Column);

        // 5. Fix, Ctrl+F5: saved automatically, built, running; output and exit code.
        var foo = editor.Document.Text.IndexOf("Foo", StringComparison.Ordinal);
        editor.Document.Replace(foo, 3, "Count");
        Dispatcher.UIThread.RunJobs();
        var mark = states.Count;
        h.Press("Ctrl+F5");
        if (real)
        {
            // The real program ends at once: its exit line in Output (the status message lasts 5 s of the manual clock,
            // which runs fast while the harness waits).
            await h.WaitForAsync(() => states.Skip(mark).Contains(IdeState.Building)
                    && (OutputText(h).Contains("Program exited with code", StringComparison.Ordinal)
                        || (h.Services.State.Current.State == IdeState.Idle && h.Services.Messages.ErrorCount > 0)),
                "build and run finished");
            Assert.True(h.Services.Messages.ErrorCount == 0, "Build errors: " + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}")));
            Assert.Contains("    Count := 1;", File.ReadAllText(unitPath), StringComparison.Ordinal);
            await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "exited");
            Assert.Contains("Program started", OutputText(h), StringComparison.Ordinal);
            Assert.Contains("Hello", OutputLines(h));
            Assert.Contains("Program exited with code 0 after ", OutputText(h), StringComparison.Ordinal);
        }
        else
        {
            await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Running, "running");
            Assert.Contains("    Count := 1;", File.ReadAllText(unitPath), StringComparison.Ordinal);
            await h.WaitForAsync(() => OutputText(h).Contains("Program started", StringComparison.Ordinal), "started line");
            var process = h.Processes.Last;
            process.WriteStdOut("Hello");
            await h.WaitForAsync(() => OutputLines(h).Contains("Hello"), "program output");
            process.Exit(0);
            await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "exited");
            await h.WaitForAsync(() => h.Status.Message?.StartsWith("Program exited with code 0 after ", StringComparison.Ordinal) == true, "exit status");
        }

        // 6. Ctrl+F5 then Shift+F5 while Running: the tree is killed once; "Program stopped".
        if (real)
            await File.WriteAllTextAsync(Path.Combine(OutputDir(h), WaitFlag), "");
        h.Press("Ctrl+F5");
        if (real)
            await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Running && OutputLines(h).Contains("Hello")
                && !OutputText(h).Contains(Strings.Msg_Run_Terminated, StringComparison.Ordinal), "running again (waiting at the flag)");
        else
            await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Running && h.Processes.Started.Count == 2, "running again");
        h.Press("Shift+F5");
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "stopped");
        if (!real)
            Assert.Equal(1, h.Processes.Last.KillCount);
        await h.WaitForAsync(() => OutputText(h).Contains(Strings.Msg_Run_Terminated, StringComparison.Ordinal), "stopped line");
    }

    /// <summary>The text of the Output window (a run shows it with the program channel, UI-TW-501).</summary>
    private static string OutputText(HeadlessHarness h) => h.TryFind<TextEditor>("Tool.Output.Text")?.Document.Text ?? "";

    private static List<string> OutputLines(HeadlessHarness h) => [.. OutputText(h).Split('\n').Select(l => l.TrimEnd('\r'))];
}
