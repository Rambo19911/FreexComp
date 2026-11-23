using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Platform;

/// <summary>The dialog service over real windows (IDE-ARCH-054, UI-DLG-008, UI-A11Y-008; D-112).</summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class WindowDialogServiceTests
{
    /// <summary>
    /// D-112: a message box shown while a modal dialog is open is modal to that dialog (its owner), and when it closes the
    /// dialog is active again with the focus on the element that had it — not the main window.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-008")]
    [Trait("Req", "UI-DLG-008")]
    public async Task NestedMessageBoxReturnsFocusToOwnerDialog()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "a");
        await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "A.pas"));
        var running = h.Services.Commands.ExecuteAsync("window.documents");
        var dialog = await h.WaitForDialogAsync("Documents");
        dialog.Find<Avalonia.Controls.Control>("List").Focus();
        Dispatcher.UIThread.RunJobs();
        var focused = dialog.FocusedAutomationId;
        Assert.StartsWith("Dialog.Documents.", focused, StringComparison.Ordinal);

        // Which window was activated last (headless windows do not deactivate each other).
        var activated = new List<string>();
        h.Window.Activated += (_, _) => activated.Add("main");
        dialog.Window.Activated += (_, _) => activated.Add("dialog");

        // A message box from inside the dialog's work: owned by the dialog, not by the main window.
        var answer = h.Services.Dialogs.ShowMessageAsync(new MessageRequest("Msg.MenuDesigner.DeleteWithSubItems", ["File", "2"]));
        var box = await h.WaitForDialogAsync("Message");
        var owner = box.Window.Owner;
        box.Click("Cancel");
        Assert.Equal("Cancel", await answer);
        try
        {
            Assert.Same(dialog.Window, owner);

            // The dialog is active again, with its focus where it was.
            await h.WaitForAsync(() => dialog.Window.IsActive, "the owner dialog active");
            Assert.Equal("dialog", activated.LastOrDefault());
            Assert.Equal(focused, dialog.FocusedAutomationId);
        }
        catch
        {
            dialog.Window.Close(); // the failure itself is reported, not the dialog left open
            throw;
        }

        dialog.Press("Escape");
        await h.WaitForAsync(() => running.IsCompleted, "dialog closed");
        await running;
    }
}
