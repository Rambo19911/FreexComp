using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class MessageBoxTests
{
    private static Task<string> Show(HeadlessHarness h, string messageId, params string[] args) =>
        h.Services.Dialogs.ShowMessageAsync(new MessageRequest(messageId, args));

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-024")]
    [Trait("Req", "UI-MSG-004")]
    [Trait("Req", "UI-DLG-023")]
    public async Task DestructiveConfirmationDefaultsToCancel()
    {
        await using var h = await HeadlessHarness.StartAsync();

        // Cancel listed first: Cancel is default and Escape, and no button is accent-filled.
        var answer = Show(h, "Msg.File.RevertConfirm", "Unit1.pas");
        var dlg = await h.WaitForDialogAsync("Message");
        Assert.Equal("Revert File", dlg.Window.Title);
        Assert.Equal("Discard all unsaved changes to Unit1.pas and reload it from disk?", dlg.Find<TextBlock>("Text").Text);
        Assert.Equal("Question", AutomationProperties.GetName(dlg.Find<Control>("Icon")));
        Assert.Equal("Dialog.Message.Button.Cancel", dlg.FocusedAutomationId); // no editable field: the default button
        Assert.DoesNotContain(dlg.FindAll("Dialog.Message.Button.").OfType<Button>(), b => b.Classes.Contains("accent"));
        dlg.Press("Enter");
        Assert.Equal("Cancel", await answer);

        answer = Show(h, "Msg.File.RevertConfirm", "Unit1.pas");
        dlg = await h.WaitForDialogAsync("Message");
        dlg.Press("Tab");
        Assert.Equal("Dialog.Message.Button.Revert", dlg.FocusedAutomationId);
        dlg.Press("Escape");
        Assert.Equal("Cancel", await answer);

        // A normal question: the first button is default and accent; Escape is Cancel; mnemonics choose a button.
        answer = Show(h, "Msg.File.SaveChangesOne", "Unit1.pas");
        dlg = await h.WaitForDialogAsync("Message");
        Assert.Equal("Save Changes", dlg.Window.Title);
        Assert.Contains("accent", dlg.Find<Button>("Button.Save").Classes);
        Assert.Equal(["Dialog.Message.Button.Save", "Dialog.Message.Button.DontSave", "Dialog.Message.Button.Cancel"],
            dlg.FindAll("Dialog.Message.Button.").OrderBy(c => X(dlg, AutomationIds.Get(c)!)).Select(AutomationIds.Get));
        dlg.Press("Enter");
        Assert.Equal("Save", await answer);

        answer = Show(h, "Msg.File.SaveChangesOne", "Unit1.pas");
        dlg = await h.WaitForDialogAsync("Message");
        dlg.Press("Alt+D");
        Assert.Equal("DontSave", await answer);

        answer = Show(h, "Msg.File.SaveChangesOne", "Unit1.pas");
        dlg = await h.WaitForDialogAsync("Message");
        dlg.Press("Escape");
        Assert.Equal("Cancel", await answer);

        // No Cancel: Escape and the title-bar close choose the last button.
        answer = Show(h, "Msg.File.RecentNotFound", @"C:\gone\Old.pas");
        dlg = await h.WaitForDialogAsync("Message");
        Assert.Equal(Strings.App_ProductName, dlg.Window.Title); // a question without a title resource
        dlg.Press("Escape");
        Assert.Equal("Keep", await answer);

        answer = Show(h, "Msg.File.RecentNotFound", @"C:\gone\Old.pas");
        dlg = await h.WaitForDialogAsync("Message");
        dlg.Window.Close();
        Assert.Equal("Keep", await answer);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-021")]
    [Trait("Req", "UI-DLG-022")]
    [Trait("Req", "UI-DLG-023")]
    public async Task ErrorShowsPathParagraphAndProductTitle()
    {
        await using var h = await HeadlessHarness.StartAsync();

        var answer = Show(h, "Msg.File.SaveFailed", @"C:\p\Unit1.pas", "Access is denied.");
        var dlg = await h.WaitForDialogAsync("Message");
        Assert.Equal(Strings.App_ProductName, dlg.Window.Title);
        Assert.Equal("Error", AutomationProperties.GetName(dlg.Find<Control>("Icon")));
        Assert.Equal("The file cannot be saved.", dlg.Find<TextBlock>("Text").Text);
        var path = dlg.Find<TextBlock>("Text.Path");
        Assert.Equal(@"C:\p\Unit1.pas", path.Text);
        Assert.Equal(@"C:\p\Unit1.pas", ToolTip.GetTip(path));
        Assert.Equal("Access is denied.", dlg.Find<TextBlock>("Text.Reason").Text);
        Assert.Null(dlg.TryFind<Control>("Details"));
        Assert.Null(dlg.TryFind<Control>("Button.CopyDetails"));
        Assert.Equal(440, dlg.Window.Width);
        Assert.False(dlg.Window.CanResize);
        Assert.False(dlg.Window.ShowInTaskbar);

        dlg.Click("SaveAs");
        Assert.Equal("SaveAs", await answer);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-025")]
    [Trait("Req", "UI-MSG-005")]
    public async Task DetailsExpanderAndCopy()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var clipboard = Assert.IsType<FakeClipboard>(h.Services.Clipboard);

        var answer = h.Services.Dialogs.ShowMessageAsync(new MessageRequest("Msg.App.InternalError", [], "System.Exception: boom"));
        var dlg = await h.WaitForDialogAsync("Message");
        var details = dlg.Find<Expander>("Details");
        Assert.False(details.IsExpanded);

        dlg.Click("Details");
        Assert.True(details.IsExpanded);
        var box = dlg.Find<TextBox>("Details.Text");
        Assert.True(box.IsReadOnly);
        Assert.Equal("System.Exception: boom", box.Text);

        // Copy Details copies text + details and keeps the dialog open; it is left-aligned, apart from the commit set.
        dlg.Click("CopyDetails");
        await h.WaitForAsync(() => clipboard.Text is not null, "clipboard written");
        Assert.Equal(Strings.Msg_App_InternalError + Environment.NewLine + Environment.NewLine + "System.Exception: boom", clipboard.Text);
        Assert.True(dlg.Window.IsVisible);
        Assert.True(X(dlg, "Button.CopyDetails") < X(dlg, "Button.Continue"));

        dlg.Press("Escape"); // no Cancel: the last commit button
        Assert.Equal("Continue", await answer);
    }

    private static double X(DialogDriver dlg, string id)
    {
        var control = dlg.Find<Control>(id);
        return control.TranslatePoint(default, dlg.Window)!.Value.X;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-016")]
    public async Task ScriptedAnswerShowsNoWindow()
    {
        await using var h = await HeadlessHarness.StartAsync();
        h.Messages.Answer("Msg.File.NotFound", "OK");

        Assert.Equal("OK", await Show(h, "Msg.File.NotFound", @"C:\x.pas"));
        Assert.Empty(h.Window.OwnedWindows);
    }
}
