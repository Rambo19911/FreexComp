using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The exception notification (I-145; dialogs.md §23, the debug-session interface §6.1): the message box
/// <c>Msg.Debug.Exception</c> at an exception stop, its check box and buttons, and the <c>debugger.*</c> settings as the
/// session's <c>ExceptionSettings</c> — with a session over the scripted adapter whose program raises exceptions.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class ExceptionDialogTests
{
    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-DLG-230")]
    [Trait("Req", "UI-DLG-231")]
    [Trait("Req", "UI-DLG-026")]
    public async Task IgnoreAddsClassToSettings()
    {
        await using var h = await DebugScenario.StartAsync();
        var unitPath = DebugScenario.UnitPath(h);
        var debug = h.Services.Debug!;
        var settings = h.Services.Settings;
        File.WriteAllText(Path.ChangeExtension(BuildScenario.ExePath(h), ".pdi"), DebugRaises.TypeInfo(unitPath));
        // EFirst is raised twice, then ESecond.
        h.DebugSessions.UseScenario($$"""
            { {{DebugRaises.Capabilities}}, {{DebugRaises.Memory}}, "steps": [
              {{DebugRaises.HookSet}},
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugRaises.HookHit}} ] },
              {{DebugRaises.AtRaiseStop(unitPath, 5, DebugRaises.FirstSlot)}},
              {{DebugScenarios.Continue(DebugRaises.HookHit)}},
              {{DebugRaises.AtRaise(unitPath, 5, DebugRaises.FirstSlot)}},
              {{DebugScenarios.Continue(DebugRaises.HookHit)}},
              {{DebugRaises.AtRaiseStop(unitPath, 6, DebugRaises.SecondSlot)}},
              { "on": "setFunctionBreakpoints", "expectArguments": { "breakpoints": [] }, "respond": { "breakpoints": [] } },
              {{DebugScenarios.Continue()}} ] }
            """);

        // The debug-session interface §6.1: the settings as the launch request's ExceptionSettings.
        Assert.True(settings.Get(SettingsCatalog.DebuggerBreakOnExceptions));
        Assert.Equal(["EAbort"], settings.Get(SettingsCatalog.DebuggerIgnoredExceptions));
        Assert.Equal(new ExceptionSettings().IgnoredClasses, debug.ExceptionSettingsNow().IgnoredClasses);
        Assert.Equal((true, true), (debug.ExceptionSettingsNow().BreakOnLanguageExceptions, debug.ExceptionSettingsNow().BreakOnOsExceptions));

        // UI-DLG-230: the program raises EFirst — it is paused at the raise location and the editor shows the execution
        // line before the dialog appears: a warning box with the class, the message, the check box, Break and Continue.
        h.Press("F5");
        var dialog = await h.WaitForDialogAsync("Message");
        Assert.Equal(IdeState.Paused, h.Services.State.Current.State);
        Assert.Equal(new ExceptionInfo(ExceptionKind.Language, "EFirst", "first", 0, 0x10000), debug.CurrentStop!.Exception);
        Assert.Equal(unitPath, h.Services.Documents.ActiveDocument?.FilePath);
        Assert.Equal("Execution", DebugScenario.ProbeOf(h, unitPath).GlyphAt(5));
        Assert.Equal("Msg.Debug.Exception", h.Messages.Requests[^1].MessageId);
        Assert.Equal(["EFirst", "first"], h.Messages.Requests[^1].Args);
        Assert.Equal(Strings.App_ProductName, dialog.Window.Title);
        Assert.Equal(Strings.MessageBox_Kind_Warning, AutomationProperties.GetName(dialog.Find<Control>("Icon")));
        Assert.Equal("The program raised exception EFirst:", dialog.Find<TextBlock>("Text").Text);
        var message = dialog.Find<SelectableTextBlock>("Text.Code"); // UI-DLG-231: type.codeSmall, selectable
        Assert.Equal("first", message.Text);
        Assert.Contains("codeSmall", message.Classes);
        var check = dialog.Find<CheckBox>("Check");
        Assert.Equal(("Do not stop on this exception class again", false), (check.Content, check.IsChecked));
        Assert.Equal(Strings.Debug_Exception_Check, check.Content);
        Assert.Equal([("Break", true, false), ("Continue", false, true)], dialog.Window.Model.Buttons.Select(b => (b.Id, b.IsDefault, b.IsCancel))); // Break default, Continue = Escape
        Assert.NotNull(dialog.Find<Button>("Button.Break"));

        // UI-DLG-231: checking the box adds the class to debugger.ignoredExceptions; Continue resumes the program.
        check.IsChecked = true;
        dialog.Click("Button.Continue");
        await h.WaitForAsync(() => !dialog.Window.IsVisible, "the dialog closed");
        Assert.Equal(["EAbort", "EFirst"], settings.Get(SettingsCatalog.DebuggerIgnoredExceptions));
        Assert.Equal(["EAbort", "EFirst"], debug.ExceptionSettingsNow().IgnoredClasses);

        // The second raise of EFirst does not stop (the session got the new list: it continues by itself); ESecond does.
        dialog = await h.WaitForDialogAsync("Message");
        Assert.Equal(["ESecond", "second"], h.Messages.Requests[^1].Args);
        Assert.Equal(2, h.Messages.Requests.Count);
        Assert.Equal(2, h.DebugSessions.SentRequests("continue").Count); // the Continue button, then the ignored raise
        Assert.Equal(6, debug.CurrentStop!.Location!.Line);
        Assert.Equal(["EFirst: first", "ESecond: second"], debug.Log.Where(e => e.Kind == DebugLogKind.Exception).Select(e => e.Message));

        // The class is added regardless of the button chosen; Break stays paused.
        dialog.Find<CheckBox>("Check").IsChecked = true;
        dialog.Click("Button.Break");
        await h.WaitForAsync(() => !dialog.Window.IsVisible, "the dialog closed");
        await DebugScenario.SettleAsync(h);
        Assert.Equal(["EAbort", "EFirst", "ESecond"], settings.Get(SettingsCatalog.DebuggerIgnoredExceptions));
        Assert.Equal(IdeState.Paused, h.Services.State.Current.State);
        Assert.Equal(2, h.DebugSessions.SentRequests("continue").Count);
        Assert.Equal("Execution", DebugScenario.ProbeOf(h, unitPath).GlyphAt(6));

        // debugger.breakOnExceptions goes to the running session at once (SetExceptionSettingsAsync): the function
        // breakpoint on the raise routine is taken away.
        Assert.Single(h.DebugSessions.SentRequests("setFunctionBreakpoints"));
        settings.Set(SettingsCatalog.DebuggerBreakOnExceptions, false);
        await h.WaitForAsync(() => h.DebugSessions.SentRequests("setFunctionBreakpoints").Count == 2, "the exception settings sent");
        await DebugScenario.SettleAsync(h);
        Assert.Equal("""{"breakpoints":[]}""", h.DebugSessions.SentArguments("setFunctionBreakpoints")[^1]);
        Assert.Equal((false, false), (debug.ExceptionSettingsNow().BreakOnLanguageExceptions, debug.ExceptionSettingsNow().BreakOnOsExceptions));

        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Dispatcher.UIThread.RunJobs();
        await DebugScenario.WaitForScenarioCompleteAsync(h); // I-301: the line may be read after the session ended
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());

        // An operating-system exception: the class text is its description, the message its code in hexadecimal.
        Assert.Equal(("Access violation", "$C0000005"), DebugService.Describe(new ExceptionInfo(ExceptionKind.Os, "", "Access violation", 0xC0000005, 0x7FF6A1B21234)));
    }
}
