using Avalonia.Headless.XUnit;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The dialog base window (dialogs.md §1).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class DialogWindowTests
{
    private static async Task<(double Width, double Height)> OpenAndCloseAsync(HeadlessHarness h, (double Width, double Height)? resizeTo = null)
    {
        var running = h.Services.Commands.ExecuteAsync("window.documents");
        var dlg = await h.WaitForDialogAsync("Documents");
        var opened = (dlg.Window.Width, dlg.Window.Height);
        if (resizeTo is { } size)
        {
            dlg.Window.Width = size.Width;
            dlg.Window.Height = size.Height;
        }

        dlg.Press("Escape");
        await h.WaitForAsync(() => running.IsCompleted, "dialog closed");
        await running;
        return opened;
    }

    /// <summary>
    /// UI-DLG-005 / IDE-ARCH-064: a resizable dialog (Documents, 600 × 380) opens at its stated size, remembers the size it
    /// had when it closed in <c>dialogs.&lt;Name&gt;.size</c>, never opens smaller than the stated size nor larger than the
    /// working area minus 40, and treats an invalid entry as missing.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-005")]
    [Trait("Req", "IDE-ARCH-064")]
    public async Task ResizableDialogRemembersSize()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "a");
        await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "A.pas"));
        var layout = h.Services.Layout;
        Assert.Null(layout.GetDialogValue("Documents", "size"));

        Assert.Equal((600.0, 380.0), await OpenAndCloseAsync(h, resizeTo: (720, 450)));
        Assert.Equal("720,450", layout.GetDialogValue("Documents", "size"));
        Assert.Equal((720.0, 450.0), await OpenAndCloseAsync(h));

        layout.SetDialogValue("Documents", "size", "100,100");
        Assert.Equal((600.0, 380.0), await OpenAndCloseAsync(h));

        layout.SetDialogValue("Documents", "size", "wide,tall");
        Assert.Equal((600.0, 380.0), await OpenAndCloseAsync(h));

        var screen = h.Window.Screens.ScreenFromWindow(h.Window) ?? h.Window.Screens.Primary;
        Assert.NotNull(screen);
        var area = screen.WorkingArea.ToRect(screen.Scaling);
        layout.SetDialogValue("Documents", "size", "20000,20000");
        Assert.Equal((area.Width - 40, area.Height - 40), await OpenAndCloseAsync(h));
    }
}
