using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Tests.Fixtures.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class DialogTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-004")]
    [Trait("Req", "UI-A11Y-008")]
    public async Task EnterAndEscapeMapToDefaultAndCancel()
    {
        await using var h = await HeadlessHarness.StartAsync();

        // Enter in a single-line field = the default button.
        var vm = new SampleDialogViewModel();
        var result = h.Services.Dialogs.ShowAsync(vm);
        var dlg = await h.WaitForDialogAsync("Sample");
        Assert.Equal("Sample", dlg.Window.Title);
        dlg.Type("Name", "abc");
        dlg.Press("Enter");
        Assert.Equal("abc", await result);

        // Escape = Cancel.
        result = h.Services.Dialogs.ShowAsync(new SampleDialogViewModel());
        dlg = await h.WaitForDialogAsync("Sample");
        dlg.Type("Name", "abc");
        dlg.Press("Escape");
        Assert.Null(await result);

        // Enter in a multi-line field types a line break; Enter on another button presses that button.
        vm = new SampleDialogViewModel();
        result = h.Services.Dialogs.ShowAsync(vm);
        dlg = await h.WaitForDialogAsync("Sample");
        dlg.Type("Name", "abc");
        dlg.Press("Tab");
        Assert.Equal("Dialog.Sample.Notes", dlg.FocusedAutomationId);
        dlg.Press("Enter");
        Assert.True(dlg.Window.IsVisible);
        Assert.Contains('\n', dlg.Find<TextBox>("Notes").Text ?? "");
        dlg.Press("Tab");
        dlg.Press("Tab");
        dlg.Press("Tab");
        Assert.Equal("Dialog.Sample.Cancel", dlg.FocusedAutomationId);
        dlg.Press("Enter");
        Assert.Null(await result);

        // The title-bar close equals Escape.
        result = h.Services.Dialogs.ShowAsync(new SampleDialogViewModel());
        dlg = await h.WaitForDialogAsync("Sample");
        dlg.Type("Name", "abc");
        dlg.Window.Close();
        Assert.Null(await result);

        // Default action first, then Cancel (UI-DLG-003); dialog windows are not in the task bar and not resizable.
        result = h.Services.Dialogs.ShowAsync(new SampleDialogViewModel());
        dlg = await h.WaitForDialogAsync("Sample");
        Assert.True(dlg.Find<Button>("Ok").Bounds.X < dlg.Find<Button>("Cancel").Bounds.X);
        Assert.Contains("accent", dlg.Find<Button>("Ok").Classes);
        Assert.False(dlg.Window.ShowInTaskbar);
        Assert.False(dlg.Window.CanResize);
        Assert.Equal(WindowStartupLocation.CenterOwner, dlg.Window.WindowStartupLocation);
        dlg.Press("Escape");
        await result;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-001")]
    [Trait("Req", "UI-DLG-010")]
    [Trait("Req", "UI-A11Y-014")]
    public async Task ModalDialogIsOwnedAndMainWindowShortcutsWait()
    {
        await using var h = await HeadlessHarness.StartAsync();
        // Without a dialog Ctrl+N (file.new) opens New File.
        h.Press("Ctrl+N");
        var newFile = await h.WaitForDialogAsync("NewFile");
        newFile.Press("Escape");
        await h.WaitForAsync(() => !h.Window.OwnedWindows.Any(w => w.IsVisible), "New File closed");

        var result = h.Services.Dialogs.ShowAsync(new SampleDialogViewModel());
        var dlg = await h.WaitForDialogAsync("Sample");
        Assert.Same(h.Window, dlg.Window.Owner);
        Assert.False(dlg.Window.CanMinimize);
        Assert.False(dlg.Window.CanMaximize);
        Assert.False(dlg.Window.ShowInTaskbar);
        Assert.Equal(WindowStartupLocation.CenterOwner, dlg.Window.WindowStartupLocation);

        // Ctrl+N (file.new: the New File dialog) reaches the main window while the dialog is open: nothing happens.
        h.Press("Ctrl+N");
        Assert.Equal([dlg.Window], h.Window.OwnedWindows.Where(w => w.IsVisible));

        dlg.Press("Escape");
        await result;

        Dispatcher.UIThread.RunJobs();
        var open = h.Window.OwnedWindows.Where(w => w.IsVisible).Select(w => w.Title).ToList();
        Assert.True(open.Count == 0, string.Join(", ", open)); // the key was not queued for later either
    }

    /// <summary>
    /// A <see cref="Core.Services.MessageRequest"/> may limit the message's buttons (in definition order); UI-DLG-040:
    /// the New File dialog's write failure offers only Retry and Cancel.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-040")]
    public async Task MessageButtonsLimited()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles[@"Hello\Hello.pproj"] = """{ "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas" }""";
            o.InitialFiles[@"Hello\Hello.pas"] = "program Hello;\nbegin\nend.\n";
        });
        static string[] Buttons(DialogDriver dlg) =>
            [.. dlg.FindAll("Dialog.Message.Button.").OrderBy(c => Avalonia.VisualExtensions.TranslatePoint(c, default, dlg.Window)!.Value.X).Select(c => AutomationIds.Get(c)!)];

        // The request names the buttons: the others of the definition are not shown; the first one is the default.
        var answer = h.Services.Dialogs.ShowMessageAsync(new Core.Services.MessageRequest("Msg.File.SaveFailed", [@"C:\p\A.pas", "denied"],
            Buttons: ["Cancel", "Retry"]));
        var message = await h.WaitForDialogAsync("Message");
        Assert.Equal(["Dialog.Message.Button.Retry", "Dialog.Message.Button.Cancel"], Buttons(message));
        message.Press("Enter");
        Assert.Equal("Retry", await answer);
        Assert.Throws<ArgumentException>(() => new Core.Dialogs.MessageBoxViewModel(
            new Core.Services.MessageRequest("Msg.File.SaveFailed", ["a", "b"], Buttons: ["Overwrite"]), h.Services.Clipboard));

        // UI-DLG-040: New File's write failure shows Msg.File.SaveFailed with Retry and Cancel only; the dialog stays open.
        var dir = Path.Combine(h.TempDir, "Hello");
        Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(dir, "Hello.pproj")));
        var running = h.Services.Commands.ExecuteAsync("file.newFile");
        var dlg = await h.WaitForDialogAsync("NewFile");
        dlg.Find<TextBox>("Name").Text = "Fails";
        Dispatcher.UIThread.RunJobs();
        h.FileSystem.FailWhen = call => call.StartsWith("WriteAllBytesDurable", StringComparison.Ordinal) && call.Contains("Fails.pas", StringComparison.Ordinal);
        dlg.Click("Ok");
        message = await h.WaitForDialogAsync("Message");
        Assert.Equal(["Dialog.Message.Button.Retry", "Dialog.Message.Button.Cancel"], Buttons(message));
        message.Press("Escape");
        await h.WaitForAsync(() => h.Messages.Unanswered.Count == 0, "message closed");
        Assert.True(dlg.Window.IsVisible);
        h.FileSystem.FailWhen = null;
        dlg.Press("Escape");
        await running;
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-008")]
    [Trait("Req", "UI-DLG-004")]
    public async Task FocusIsTrappedAndRestored()
    {
        await using var h = await HeadlessHarness.StartAsync();
        h.Find<Control>(AutomationIds.DocumentArea).Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);

        var result = h.Services.Dialogs.ShowAsync(new SampleDialogViewModel());
        var dlg = await h.WaitForDialogAsync("Sample");
        Assert.Equal("Dialog.Sample.Name", dlg.FocusedAutomationId); // the first editable field
        dlg.Type("Name", "x");

        // Tab cycles through the dialog in visual order, buttons last, and wraps; Shift+Tab goes back.
        var order = new List<string?>();
        for (var i = 0; i < 5; i++)
        {
            dlg.Press("Tab");
            order.Add(dlg.FocusedAutomationId);
        }

        Assert.Equal(["Dialog.Sample.Notes", "Dialog.Sample.Option", "Dialog.Sample.Ok", "Dialog.Sample.Cancel", "Dialog.Sample.Name"], order);
        dlg.Press("Shift+Tab");
        Assert.Equal("Dialog.Sample.Cancel", dlg.FocusedAutomationId);

        dlg.Press("Escape");
        await result;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId); // focus returns to where it was
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-006")]
    public async Task DefaultButtonDisabledWhileInvalid()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var vm = new SampleDialogViewModel();
        var result = h.Services.Dialogs.ShowAsync(vm);
        var dlg = await h.WaitForDialogAsync("Sample");
        var ok = dlg.Find<Button>("Ok");

        Assert.False(ok.IsEnabled);
        dlg.Press("Enter"); // nothing happens while invalid
        Assert.True(dlg.Window.IsVisible);

        dlg.Type("Name", "a");
        Assert.True(ok.IsEnabled);
        dlg.Press("Backspace");
        Assert.False(ok.IsEnabled);
        dlg.Press("Enter");
        Assert.True(dlg.Window.IsVisible);

        dlg.Type("Name", "b");
        dlg.Press("Enter");
        Assert.Equal("b", await result);
    }
}
