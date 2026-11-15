using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests;

/// <summary>Phase 3 acceptance with fakes (I-127, phase3-build-run.md §11 steps 1–6).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class Phase3AcceptanceTests
{
    /// <summary>The unit of the test project <c>HelloErr</c>: the undeclared identifier <c>Foo</c> at line 12, column 5.</summary>
    private const string Unit = "unit Unit1;\r\ninterface\r\nprocedure Run;\r\nimplementation\r\nvar\r\n  Count: Integer;\r\nprocedure Run;\r\n" +
                                "begin\r\n  Count := 0;\r\n  Count := Count + 1;\r\n  // set the value\r\n    Foo := 1;\r\nend;\r\nend.\r\n";

    /// <summary>The text of the Output window (a run shows it with the program channel, UI-TW-501).</summary>
    private static string OutputText(HeadlessHarness h) => h.TryFind<TextEditor>("Tool.Output.Text")?.Document.Text ?? "";

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-065")]
    [Trait("Req", "IDE-P3-066")]
    public async Task BuildErrorNavigateFixRun()
    {
        // The product is at Phase 3: no test override of the implemented phase.
        Assert.Equal(3, ServiceGraph.ImplementedPhase);
        await using var h = await BuildScenario.StartAsync(o => o.InitialFiles[@"Hello\Unit1.pas"] = Unit, implementedPhase: null);
        var unitPath = BuildScenario.UnitPath(h);
        h.Build.Enqueue(FakeBuildEngine.Failure(BuildScenario.ProjectPath(h),
            FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier 'Foo'", unitPath, 12, 5, 3)));

        // 2. Ctrl+Shift+B: Building, then Idle with one error.
        var states = new List<IdeState>();
        h.Services.State.Changed += (_, _) => states.Add(h.Services.State.Current.State);
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => states.Contains(IdeState.Building) && h.Services.State.Current.State == IdeState.Idle, "build finished");
        Dispatcher.UIThread.RunJobs();
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
        h.Press("Ctrl+F5");
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Running, "running");
        Assert.Contains("    Count := 1;", File.ReadAllText(unitPath), StringComparison.Ordinal);
        await h.WaitForAsync(() => OutputText(h).Contains("Program started", StringComparison.Ordinal), "started line");
        var process = h.Processes.Last;
        process.WriteStdOut("Hello");
        await h.WaitForAsync(() => OutputText(h).Split('\n').Any(l => l.TrimEnd('\r') == "Hello"), "program output");
        process.Exit(0);
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "exited");
        await h.WaitForAsync(() => h.Status.Message?.StartsWith("Program exited with code 0 after ", StringComparison.Ordinal) == true, "exit status");

        // 6. Ctrl+F5 then Shift+F5 while Running: the tree is killed once; "Program stopped".
        h.Press("Ctrl+F5");
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Running && h.Processes.Started.Count == 2, "running again");
        h.Press("Shift+F5");
        await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle, "stopped");
        Assert.Equal(1, h.Processes.Last.KillCount);
        await h.WaitForAsync(() => OutputText(h).Contains(Strings.Msg_Run_Terminated, StringComparison.Ordinal), "stopped line");
    }
}
