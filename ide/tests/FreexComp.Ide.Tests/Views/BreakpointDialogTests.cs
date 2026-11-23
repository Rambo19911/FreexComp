using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Breakpoint, Watch and Evaluate dialogs (I-136; dialogs.md §19–§21): views, validation, and evaluation through the
/// debug service with a session over the scripted adapter.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class BreakpointDialogTests
{
    private static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static void SetText(DialogDriver dialog, string field, string text)
    {
        dialog.Find<TextBox>(field).Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task CloseAsync(HeadlessHarness h, DialogDriver dialog, Task opening, string button)
    {
        dialog.Click(button);
        await h.WaitForAsync(() => !dialog.Window.IsVisible, $"dialog {dialog.Name} closed");
        await opening;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-190")]
    [Trait("Req", "UI-DLG-191")]
    public async Task ValidatesConditionAndPassCount()
    {
        await using var h = await DebugScenario.StartAsync();
        var unitPath = DebugScenario.UnitPath(h);
        var (document, editor, probe) = await DebugScenario.OpenAsync(h, unitPath);
        editor.TextArea.Caret.Offset = editor.Document.GetLineByNumber(4).Offset;
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        var breakpoints = h.Services.Breakpoints!;

        // Add Breakpoint: the active document's path and caret line, pass count 0, enabled.
        var opening = h.Services.Commands.ExecuteAsync("run.addBreakpoint");
        var dlg = await h.WaitForDialogAsync("Breakpoint");
        Assert.Equal(("Add Breakpoint", 520.0, false), (dlg.Window.Title, dlg.Window.Width, dlg.Window.CanResize));
        Assert.Equal((unitPath, "4", "", "0"),
            (dlg.Find<TextBox>("File").Text, dlg.Find<TextBox>("Line").Text, dlg.Find<TextBox>("Condition").Text, dlg.Find<TextBox>("PassCount").Text));
        Assert.True(dlg.Find<CheckBox>("Enabled").IsChecked);
        Assert.Equal(Strings.Msg_Breakpoint_PassCountNone, dlg.Find<TextBlock>("PassCount.Hint").Text);
        Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);

        // Pass count: an integer from 0 to 1 000 000; the text beside it explains the value.
        foreach (var bad in (string[])["abc", "-1", "1000001", ""])
        {
            SetText(dlg, "PassCount", bad);
            Assert.Equal(Strings.Msg_Breakpoint_PassCountRange, dlg.ErrorText("PassCount"));
            Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        }

        SetText(dlg, "PassCount", "5");
        Assert.Null(dlg.ErrorText("PassCount"));
        Assert.Equal(F(Strings.Msg_Breakpoint_PassCountSome, 5), dlg.Find<TextBlock>("PassCount.Hint").Text);
        Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);

        // Line: 1 … the file's line count (the open document's).
        foreach (var bad in (string[])["0", "x", "99"])
        {
            SetText(dlg, "Line", bad);
            Assert.Equal(F(Strings.Msg_GoToLine_OutOfRange, document.Buffer.LineCount), dlg.ErrorText("Line"));
            Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        }

        SetText(dlg, "Line", "6");
        Assert.Null(dlg.ErrorText("Line"));

        // File: must exist.
        foreach (var bad in (string[])["", Path.Combine(h.TempDir, @"Hello\Missing.pas")])
        {
            SetText(dlg, "File", bad);
            Assert.Equal(Strings.Msg_Breakpoint_FileMissing, dlg.ErrorText("File"));
            Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        }

        // The file picker fills the field.
        h.FileDialogs.NextOpen(unitPath);
        dlg.Click("File.Browse");
        await h.WaitForAsync(() => dlg.Find<TextBox>("File").Text == unitPath, "the picked file");
        Assert.Null(dlg.ErrorText("File"));

        // Without a paused session the condition is not checked; OK adds the breakpoint.
        SetText(dlg, "Condition", "Foo1 >");
        await CloseAsync(h, dlg, opening, "Ok");
        var breakpoint = Assert.Single(breakpoints.All);
        Assert.Equal((unitPath, 6, true, "Foo1 >", new HitCountSpec(HitCountMode.GreaterOrEqual, 6)),
            (breakpoint.Path, breakpoint.Line, breakpoint.Enabled, breakpoint.Condition, breakpoint.HitCount));
        Assert.Equal("BreakpointConditional", probe.GlyphAt(6));

        // UI-DLG-190: Add on a location that has a breakpoint updates that breakpoint.
        opening = h.Services.Commands.ExecuteAsync("run.addBreakpoint");
        dlg = await h.WaitForDialogAsync("Breakpoint");
        SetText(dlg, "Line", "6");
        SetText(dlg, "Condition", "");
        await CloseAsync(h, dlg, opening, "Ok");
        Assert.Same(breakpoint, Assert.Single(breakpoints.All));
        Assert.Equal((null, null), (breakpoint.Condition, breakpoint.HitCount));
        Assert.Equal("Breakpoint", probe.GlyphAt(6));

        // Cancel changes nothing.
        opening = h.Services.Commands.ExecuteAsync("run.addBreakpoint");
        dlg = await h.WaitForDialogAsync("Breakpoint");
        SetText(dlg, "Line", "2");
        dlg.Press("Escape");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog canceled");
        await opening;
        Assert.Single(breakpoints.All);

        // While a session is paused, OK evaluates the condition once; a failure blocks OK (Msg.Debug.ConditionInvalid).
        DebugScenario.WriteTypeInfo(h);
        h.DebugSessions.UseScenario($$"""
            { "steps": [ { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 6 } ] } },
                         {{DebugScenarios.StopAtStart(unitPath, 5)}},
                         { "on": "setBreakpoints", "respond": { "breakpoints": [] } } ] }
            """);
        await h.ExecuteAsync("run.start");
        await DebugScenario.WaitForStateAsync(h, IdeState.Paused);
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == unitPath, "the editor shows the stop");
        editor = DebugScenario.EditorOf(h, unitPath);
        editor.TextArea.Caret.Offset = editor.Document.GetLineByNumber(6).Offset;
        Dispatcher.UIThread.RunJobs();

        // Breakpoint Properties: File and Line are read-only.
        Assert.True(h.Services.Commands.CanExecute("run.breakpointProperties"));
        opening = h.Services.Commands.ExecuteAsync("run.breakpointProperties");
        dlg = await h.WaitForDialogAsync("Breakpoint");
        Assert.Equal("Breakpoint Properties", dlg.Window.Title);
        Assert.Equal((unitPath, true, "6", true), (dlg.Find<TextBox>("File").Text, dlg.Find<TextBox>("File").IsReadOnly,
            dlg.Find<TextBox>("Line").Text, dlg.Find<TextBox>("Line").IsReadOnly));
        Assert.Null(dlg.TryFind<Button>("File.Browse"));
        SetText(dlg, "Condition", "1 +");
        dlg.Click("Ok");
        await h.WaitForAsync(() => dlg.ErrorText("Condition") is not null, "the condition's error");
        Assert.StartsWith(F(Strings.Msg_Debug_ConditionInvalid, ""), dlg.ErrorText("Condition"), StringComparison.Ordinal);
        Assert.True(dlg.Window.IsVisible);
        Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        Assert.Null(breakpoint.Condition);

        // Editing the condition clears the message; a condition that evaluates is accepted and goes to the session.
        SetText(dlg, "Condition", "1 + 1 = 2");
        Assert.Null(dlg.ErrorText("Condition"));
        dlg.Find<CheckBox>("Enabled").IsChecked = false;
        await CloseAsync(h, dlg, opening, "Ok");
        Assert.Equal(("1 + 1 = 2", false), (breakpoint.Condition, breakpoint.Enabled));
        await h.WaitForAsync(() => h.Services.Breakpoints!.WhenSyncedAsync().IsCompleted && breakpoint.State == BreakpointState.Disabled, "the change sent to the session");
        Assert.Equal("BreakpointConditionalDisabled", DebugScenario.ProbeOf(h, unitPath).GlyphAt(6));

        await h.ExecuteAsync("run.stop");
        Assert.Equal(IdeState.Idle, h.Services.State.Current.State);
    }

    /// <summary>dialogs.md §20, §21: the Watch dialog adds a watch; Evaluate/Modify evaluates in the paused session and shows errors.</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-200")]
    [Trait("Req", "UI-DLG-210")]
    public async Task WatchAndEvaluateDialogs()
    {
        await using var h = await DebugScenario.StartAsync();
        var unitPath = DebugScenario.UnitPath(h);
        var (_, editor, _) = await DebugScenario.OpenAsync(h, unitPath);
        editor.TextArea.Caret.Offset = BuildScenario.UnitText.IndexOf("Foo1", StringComparison.Ordinal) + 1;
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        var debug = h.Services.Debug!;

        // Add Watch (from outside the Watches window): the word at the caret; an empty expression disables OK.
        var opening = h.Services.Commands.ExecuteAsync("watches.add");
        var dlg = await h.WaitForDialogAsync("Watch");
        Assert.Equal(("Add Watch", 440.0, false), (dlg.Window.Title, dlg.Window.Width, dlg.Window.CanResize));
        Assert.Equal("Foo1", dlg.Find<TextBox>("Expression").Text);
        SetText(dlg, "Expression", "   ");
        Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        SetText(dlg, "Expression", " Foo1 + 1 ");
        await CloseAsync(h, dlg, opening, "Ok");
        Assert.Equal(["Foo1 + 1"], debug.Watches.Expressions);
        // UI-DLG-200: the Watches window is shown, focus stays in the editor.
        Assert.True(h.Find<Control>(AutomationIds.ToolTab("Watches")).IsEffectivelyVisible);
        Assert.Equal("Editor.Unit1.pas", h.FocusedAutomationId);

        // Shift+F9 adds the word at the caret directly.
        h.Press("Shift+F9");
        Assert.Equal(["Foo1 + 1", "Foo1"], debug.Watches.Expressions);

        // Evaluate/Modify needs a paused program.
        Assert.False(h.Services.Commands.CanExecute("run.evaluate"));
        DebugScenario.WriteTypeInfo(h);
        h.DebugSessions.UseScenario($$"""{ "steps": [ {{DebugScenarios.StopAtStart(unitPath, 5)}} ] }""");
        await h.ExecuteAsync("run.start");
        await DebugScenario.WaitForStateAsync(h, IdeState.Paused);
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == unitPath, "the editor shows the stop");
        editor = DebugScenario.EditorOf(h, unitPath);
        editor.TextArea.Caret.Offset = editor.Document.GetLineByNumber(1).Offset; // "unit": not an expression worth evaluating
        editor.TextArea.ClearSelection();
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.True(h.Services.Commands.CanExecute("run.evaluate"));
        opening = h.Services.Commands.ExecuteAsync("run.evaluate");
        dlg = await h.WaitForDialogAsync("Evaluate");
        Assert.Equal(("Evaluate/Modify", true), (dlg.Window.Title, dlg.Window.CanResize));
        Assert.Equal((560.0, 360.0), (dlg.Window.MinWidth, dlg.Window.MinHeight));

        // An expression is evaluated in the session (EvaluateContext.Console): value and type.
        SetText(dlg, "Expression", "2 + 3 * 4");
        Assert.True(dlg.Find<Button>("Evaluate").IsEffectivelyEnabled);
        dlg.Click("Evaluate");
        await h.WaitForAsync(() => dlg.Find<TextBox>("Result").Text == "14", "the value");
        Assert.Equal("Integer", dlg.Find<TextBlock>("Type").Text);
        Assert.False(dlg.Find<TextBox>("NewValue").IsEffectivelyEnabled); // a constant is not a modifiable location
        Assert.False(dlg.Find<Button>("Modify").IsEffectivelyEnabled);

        // A failure shows the evaluator's message in the result box (Msg.Debug.EvalFailed).
        SetText(dlg, "Expression", "2 +");
        dlg.Click("Evaluate");
        await h.WaitForAsync(() => dlg.Find<TextBox>("Result").Text is { Length: > 0 } text && text != "14", "the evaluator's message");
        Assert.Contains("error", dlg.Find<TextBox>("Result").Classes);
        Assert.Equal("", dlg.Find<TextBlock>("Type").Text);

        // An empty expression cannot be evaluated or watched; Add Watch keeps the dialog open.
        SetText(dlg, "Expression", "");
        Assert.False(dlg.Find<Button>("Evaluate").IsEffectivelyEnabled);
        Assert.False(dlg.Find<Button>("AddWatch").IsEffectivelyEnabled);
        SetText(dlg, "Expression", "Length(S)");
        dlg.Click("AddWatch");
        Assert.Equal(["Foo1 + 1", "Foo1", "Length(S)"], debug.Watches.Expressions);
        Assert.True(dlg.Window.IsVisible);

        await CloseAsync(h, dlg, opening, "Close");
        await h.ExecuteAsync("run.stop");
        Assert.Equal(IdeState.Idle, h.Services.State.Current.State);
    }
}
