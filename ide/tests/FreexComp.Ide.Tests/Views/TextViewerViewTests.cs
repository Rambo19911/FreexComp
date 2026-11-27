using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Text Viewer window (I-310; visualizers.md §5.2, dialogs.md §32): opened by View Text on a Locals row of the
/// paused demo program (the scripted adapter answers the text reads from the demo's memory) — the details line, the
/// read-only text, the two check boxes, the status live region and the buttons, with the AutomationIds of UI-DLG-328.
/// </summary>
[Trait("Phase", "6")] // a session over the scripted adapter (IDE-TEST-006); the test itself is Phase 8
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class TextViewerViewTests
{
    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-328")]
    [Trait("Req", "UI-DLG-326")]
    [Trait("Req", "UI-DLG-327")]
    public async Task RendersDetailsTextCheckBoxesAndButtons_AutomationIds()
    {
        await using var h = await DebugScenario.StartDemoAsync();
        var demo = DebugScenario.DemoPath(h);
        var locals = h.Services.DebugShell!.Locals;
        var clipboard = (FakeClipboard)h.Services.Clipboard;
        h.DebugSessions.UseScenario($$"""
            { {{DebugDemo.Data()}}, "steps": [
              {{DebugDemo.FirstStep(demo)}},
              {{DebugDemo.AtStop(demo)}} ] }
            """);
        h.Press("F11");
        await DebugScenario.WaitForStopAsync(h, DebugDemo.StopLine);
        await DebugScenario.SettleAsync(h);

        // The Locals context menu offers View Text for the string row.
        h.Click("Tool.Locals.Row.S.Name");
        var item = Assert.Single(locals.BuildContextMenu(), m => m.CommandId == "locals.viewText");
        Assert.Equal(Strings.Cmd_locals_viewText, item.Label);
        Assert.True(item.IsEnabled);
        var opening = h.Services.Commands.ExecuteAsync("locals.viewText");
        var dialog = await h.WaitForDialogAsync(TextViewerViewModel.DialogName);
        var model = (TextViewerViewModel)dialog.Window.Model;
        await h.WaitForAsync(() => !model.IsLoading, "the text");
        Dispatcher.UIThread.RunJobs();

        // UI-DLG-328: every element has its AutomationId; the text's automation name is the title; Status is a polite live region.
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.TextViewer_Title, "S"), dialog.Window.Title);
        var details = dialog.Find<TextBlock>("Details");
        var text = dialog.Find<TextEditor>("Text");
        var wrap = dialog.Find<CheckBox>("Wrap");
        var hex = dialog.Find<CheckBox>("Hex");
        var status = dialog.Find<TextBlock>("Status");
        var copy = dialog.Find<Button>("Copy");
        var close = dialog.Find<Button>("Close");
        Assert.Equal(dialog.Window.Title, AutomationProperties.GetName(text));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
        Assert.Empty(AutomationIdChecks.ControlsWithoutId(dialog.Window));
        Assert.Empty(AutomationIdChecks.DuplicateIds(dialog.Window));

        // The details of the debugger; the text it read; the end state (whole text: empty status).
        Assert.StartsWith("string · " + string.Format(CultureInfo.CurrentCulture, Strings.TextViewer_Length, 5) + " · ", details.Text, StringComparison.Ordinal);
        Assert.EndsWith(" · UTF-16 · $" + model.TextDetails!.PayloadAddress!.Value.ToString("X16", CultureInfo.InvariantCulture), details.Text, StringComparison.Ordinal);
        Assert.Contains("caption", details.Classes);
        Assert.Equal("Héllo", text.Document.Text);
        Assert.True(text.IsReadOnly);
        Assert.Equal("", status.Text);
        Assert.Equal((true, false), (wrap.IsChecked, hex.IsChecked));
        Assert.True(text.WordWrap);
        Assert.Equal(ScrollBarVisibility.Disabled, text.HorizontalScrollBarVisibility);
        Assert.True(copy.IsEffectivelyEnabled);
        Assert.Equal(Strings.Button_Close, AutomationProperties.GetName(close));

        // Hex: the offset, five UTF-16 units and the characters; back to the text.
        dialog.Click("Hex");
        Assert.Equal("00000000 0048 00E9 006C 006C 006F                 Héllo", text.Document.Text);
        dialog.Click("Hex");
        Assert.Equal("Héllo", text.Document.Text);

        // Wrap off: a horizontal scroll bar, kept in the UI state.
        dialog.Click("Wrap");
        Assert.False(text.WordWrap);
        Assert.Equal(ScrollBarVisibility.Auto, text.HorizontalScrollBarVisibility);
        Assert.Equal("false", h.Services.Layout.GetDialogValue(TextViewerViewModel.DialogName, "wrap"));

        // Copy: the whole text; Escape closes; the program is still paused.
        dialog.Click("Copy");
        Assert.Equal("Héllo", clipboard.Text);
        dialog.Press("Escape");
        await h.WaitForAsync(() => !dialog.Window.IsVisible, "dialog closed");
        await opening;
        await DebugScenario.SettleAsync(h);
        Assert.Equal(IdeState.Paused, h.Services.State.Current.State);

        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        await DebugScenario.WaitForScenarioCompleteAsync(h); // I-301: the line may be read after the session ended
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());
    }
}
