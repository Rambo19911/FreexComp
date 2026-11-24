using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// Evaluate/Modify with a paused program (I-145; dialogs.md §21, the debug-session interface §6.1): evaluation in the
/// selected frame and assignment to a modifiable result, with the debug views following — over the scripted adapter
/// and the demo program's memory.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class EvaluateDialogTests
{
    private static void SetText(DialogDriver dialog, string field, string text)
    {
        dialog.Find<TextBox>(field).Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-210")]
    public async Task EvaluatesAndModifies()
    {
        await using var h = await DebugScenario.StartDemoAsync();
        var demo = DebugScenario.DemoPath(h);
        var debug = h.Services.Debug!;
        var locals = h.Services.DebugShell!.Locals;
        h.DebugSessions.UseScenario($$"""
            { {{DebugDemo.Data()}}, "steps": [
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("entry")}} ] },
              {{DebugDemo.AtStop(demo)}},
              {{DebugDemo.Scopes}} ] }
            """);
        debug.Watches.Add("Total * 2");
        h.Press("F11");
        await DebugScenario.WaitForStopAsync(h, DebugDemo.StopLine);

        // Ctrl+F7 with the caret in "Total" on the stop line: the dialog opens with the word at the caret and evaluates
        // at once (UI-DLG-210) — value and type from the session.
        var editor = DebugScenario.EditorOf(h, demo);
        var line = editor.Document.GetLineByNumber(DebugDemo.StopLine);
        editor.TextArea.Caret.Offset = line.Offset + editor.Document.GetText(line).IndexOf("Total", StringComparison.Ordinal) + 2;
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        var opening = h.Services.Commands.ExecuteAsync("run.evaluate");
        var dialog = await h.WaitForDialogAsync("Evaluate");
        Assert.Equal("Total", dialog.Find<TextBox>("Expression").Text);
        await h.WaitForAsync(() => dialog.Find<TextBox>("Result").Text == "24", "the value");
        Assert.Equal("Integer", dialog.Find<TextBlock>("Type").Text);
        Assert.DoesNotContain("error", dialog.Find<TextBox>("Result").Classes);

        // A modifiable scalar: New value is enabled; Modify needs a value.
        Assert.True(dialog.Find<TextBox>("NewValue").IsEffectivelyEnabled);
        Assert.False(dialog.Find<Button>("Modify").IsEffectivelyEnabled);

        // A value that cannot be assigned: Msg.Debug.ModifyFailed under New value; the result stays.
        SetText(dialog, "NewValue", "abc");
        Assert.True(dialog.Find<Button>("Modify").IsEffectivelyEnabled);
        dialog.Click("Modify");
        await h.WaitForAsync(() => dialog.ErrorText("NewValue") is not null, "the failure");
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Debug_ModifyFailed, "Undeclared identifier 'abc'"), dialog.ErrorText("NewValue"));
        Assert.Equal("24", dialog.Find<TextBox>("Result").Text);
        Assert.Empty(h.DebugSessions.SentRequests("writeMemory"));

        // Modify (Enter in New value) assigns, evaluates again and refreshes Locals and Watches.
        SetText(dialog, "NewValue", "Total + 6");
        dialog.Find<TextBox>("NewValue").Focus();
        dialog.Press("Enter");
        await h.WaitForAsync(() => dialog.Find<TextBox>("Result").Text == "30", "the new value");
        Assert.Null(dialog.ErrorText("NewValue"));
        Assert.Single(h.DebugSessions.SentRequests("writeMemory"));
        await DebugScenario.SettleAsync(h);
        Assert.Equal("30", locals.Rows.Single(r => r.Key == "Total").Cells[1].Text);
        Assert.Equal("60", h.Services.DebugShell.Watches.Rows[0].Cells[1].Text);

        // Structured values show their one-line text; they and strings are not modifiable in Phase 6.
        foreach (var (expression, value, type) in new[] { ("P", "(X: 3; Y: 4)", "TPoint"), ("S", "'Héllo'", "string") })
        {
            SetText(dialog, "Expression", expression);
            dialog.Click("Evaluate");
            await h.WaitForAsync(() => dialog.Find<TextBox>("Result").Text == value, "the value of " + expression);
            Assert.Equal(type, dialog.Find<TextBlock>("Type").Text);
            Assert.False(dialog.Find<TextBox>("NewValue").IsEffectivelyEnabled);
            Assert.False(dialog.Find<Button>("Modify").IsEffectivelyEnabled);
        }

        // Enter in Expression is Evaluate; a failure shows the evaluator's message as the result (Msg.Debug.EvalFailed).
        SetText(dialog, "Expression", "Foo + 1");
        dialog.Find<TextBox>("Expression").Focus();
        dialog.Press("Enter");
        await h.WaitForAsync(() => dialog.Find<TextBox>("Result").Text == "Undeclared identifier 'Foo'", "the evaluator's message");
        Assert.Contains("error", dialog.Find<TextBox>("Result").Classes);
        Assert.Equal("", dialog.Find<TextBlock>("Type").Text);
        Assert.False(dialog.Find<TextBox>("NewValue").IsEffectivelyEnabled);
        Assert.True(dialog.Window.IsVisible); // Enter did not close the dialog

        // A member path is a modifiable location too.
        SetText(dialog, "Expression", "P.Y");
        dialog.Click("Evaluate");
        await h.WaitForAsync(() => dialog.Find<TextBox>("Result").Text == "4", "the value of P.Y");
        Assert.True(dialog.Find<TextBox>("NewValue").IsEffectivelyEnabled);

        // Add Watch adds the expression and keeps the dialog open; Close (Escape) closes it; the program is still paused.
        dialog.Click("AddWatch");
        Assert.Equal(["Total * 2", "P.Y"], debug.Watches.Expressions);
        Assert.True(dialog.Window.IsVisible);
        dialog.Press("Escape");
        await h.WaitForAsync(() => !dialog.Window.IsVisible, "dialog closed");
        await opening;
        await DebugScenario.SettleAsync(h);
        Assert.Equal(IdeState.Paused, h.Services.State.Current.State);

        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());
    }
}
