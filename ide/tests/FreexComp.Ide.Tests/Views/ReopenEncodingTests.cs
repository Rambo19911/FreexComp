using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class ReopenEncodingTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-074")]
    [Trait("Req", "IDE-P1-090")]
    public async Task ReloadsWithChosenEncoding()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var path = Path.Combine(h.TempDir, "Legacy.pas");
        File.WriteAllBytes(path, [0x43, 0x61, 0x66, 0xE9, 0x0D, 0x0A]); // "Café" in Windows-1252: not valid UTF-8
        var document = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(path)).Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(FileEncoding.Windows1252, document.Encoding);

        // The Encoding info bar offers the dialog; auto-detection chose the current encoding, so UTF-8 is proposed.
        h.Click("InfoBar.Encoding.Action.ReopenWithEncoding");
        var dlg = await h.WaitForDialogAsync("ReopenEncoding");
        Assert.Equal("Reopen with Encoding", dlg.Window.Title);
        Assert.Equal((440.0, false), (dlg.Window.Width, dlg.Window.CanResize));
        Assert.Equal("Legacy.pas is currently read as: Western (Windows-1252)", dlg.Find<TextBlock>("Current").Text);
        var combo = dlg.Find<ComboBox>("Encoding");
        Assert.Equal(["UTF-8", "UTF-8 with BOM", "UTF-16 LE", "UTF-16 BE", "Western (Windows-1252)"],
            combo.Items.Cast<object>().Select(i => i is ComboBoxItem c ? c.Content as string : i as string));
        Assert.Equal(0, combo.SelectedIndex);
        Assert.False(dlg.TryFind<Control>("Warning")?.IsEffectivelyVisible ?? false);
        Assert.Equal("Reopen", Avalonia.Automation.AutomationProperties.GetName(dlg.Find<Button>("Ok")));

        // Escape changes nothing.
        dlg.Press("Escape");
        Assert.Equal("Café\r\n", document.Buffer.Text);
        Assert.Equal(FileEncoding.Windows1252, document.Encoding);

        // Reopen as UTF-8: the invalid byte becomes U+FFFD; the next save uses UTF-8; nothing to undo; not modified.
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        Dispatcher.UIThread.RunJobs();
        h.Click("InfoBar.Encoding.Action.ReopenWithEncoding");
        dlg = await h.WaitForDialogAsync("ReopenEncoding");
        Assert.True(dlg.Find<Control>("Warning").IsEffectivelyVisible); // modified: the changes will be discarded
        Assert.Equal("Discard Changes and Reopen", Avalonia.Automation.AutomationProperties.GetName(dlg.Find<Button>("Ok")));
        dlg.Click("Ok");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        // The file is re-read off the UI thread after the dialog closed (IDE-ARCH-081): wait for the reload itself.
        await h.WaitForAsync(() => document.Encoding == FileEncoding.Utf8, "reopened as UTF-8");
        Assert.Equal("Caf\uFFFD\r\n", document.Buffer.Text);
        Assert.Equal(FileEncoding.Utf8, document.Encoding);
        Assert.False(document.IsModified);
        Assert.False(document.Buffer.CanUndo);
        Assert.Null(h.TryFind<Control>("InfoBar.Encoding"));
        Assert.Equal("UTF-8", h.Status.Field("Encoding"));

        // From the status bar: detection (Windows-1252) differs from the current encoding, so it is proposed.
        var reopening = h.Services.Documents.ReopenWithEncodingAsync(document);
        dlg = await h.WaitForDialogAsync("ReopenEncoding");
        combo = dlg.Find<ComboBox>("Encoding");
        Assert.Equal(4, combo.SelectedIndex);
        combo.SelectedIndex = 2; // UTF-16 LE
        Dispatcher.UIThread.RunJobs();
        dlg.Press("Enter");
        await h.WaitForAsync(() => reopening.IsCompleted, "reopened");
        Assert.True(await reopening);
        Assert.Equal(FileEncoding.Utf16Le, document.Encoding);
        Assert.Equal("UTF-16 LE", h.Status.Field("Encoding"));
    }
}
