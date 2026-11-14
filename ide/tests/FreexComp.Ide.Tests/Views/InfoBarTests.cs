using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class InfoBarTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-090")]
    [Trait("Req", "UI-ED-091")]
    [Trait("Req", "UI-ED-092")]
    [Trait("Req", "UI-ED-093")]
    [Trait("Req", "UI-ED-094")]
    [Trait("Req", "UI-ED-095")]
    public async Task StackingAndReplacement()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var path = Path.Combine(h.TempDir, "Legacy.pas");
        // "  Café" CRLF "x" LF in Windows-1252: not UTF-8 (Encoding), mixed line endings, read-only.
        File.WriteAllBytes(path, [0x20, 0x20, 0x43, 0x61, 0x66, 0xE9, 0x0D, 0x0A, 0x78, 0x0A]);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var document = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(path)).Value);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.Legacy.pas");
        Assert.True(editor.TextArea.Focus());
        Dispatcher.UIThread.RunJobs();

        // Newest on top; each bar has its severity icon (UI-ED-090/091).
        Assert.Equal(["InfoBar.ReadOnly", "InfoBar.Encoding", "InfoBar.MixedLineEndings"], BarsTopToBottom(h));
        Assert.Equal("info", IconOf(h, "ReadOnly"));
        Assert.Equal("warning", IconOf(h, "Encoding"));
        Assert.Equal("info", IconOf(h, "MixedLineEndings"));

        // A fourth bar queues and does not take focus (UI-ED-092/094); a newer one of the same kind replaces it.
        document.ShowInfoBar(Bar("first"));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(h.TryFind<Control>("InfoBar.ExternalChange"));
        Assert.Equal(4, document.InfoBars.Count);
        document.ShowInfoBar(Bar("second"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, document.InfoBars.Count);
        Assert.Single(document.InfoBars, b => b.Kind == "ExternalChange");
        Assert.Equal("Editor.Legacy.pas", h.FocusedAutomationId);

        // Closing a visible bar shows the queued one on top; a non-dismissible kind has no × (UI-ED-093).
        h.Click("InfoBar.Encoding.Dismiss");
        Assert.Equal(["InfoBar.ExternalChange", "InfoBar.ReadOnly", "InfoBar.MixedLineEndings"], BarsTopToBottom(h));
        Assert.Equal("second", h.Find<TextBlock>("InfoBar.ExternalChange.Text").Text);
        Assert.Null(h.TryFind<Control>("InfoBar.ExternalChange.Dismiss"));
        Assert.Equal("warning", IconOf(h, "ExternalChange"));

        // Keyboard (UI-ED-094): Shift+Tab from the text area reaches the top-most bar's first button, Tab walks the
        // buttons and returns to the text area; Escape returns at once. The read-only text is untouched.
        Assert.True(editor.TextArea.Focus());
        h.Press("Shift+Tab");
        Assert.Equal("InfoBar.ExternalChange.Action.Reload", h.FocusedAutomationId);
        string[] order =
        [
            "InfoBar.ReadOnly.Action.MakeWritable", "InfoBar.ReadOnly.Dismiss",
            "InfoBar.MixedLineEndings.Action.ConvertToCrlf", "InfoBar.MixedLineEndings.Action.ConvertToLf",
            "InfoBar.MixedLineEndings.Action.LeaveAsIs", "InfoBar.MixedLineEndings.Dismiss", "Editor.Legacy.pas",
        ];
        foreach (var expected in order)
        {
            h.Press("Tab");
            Assert.Equal(expected, h.FocusedAutomationId);
        }

        h.Press("Shift+Tab");
        h.Press("Tab");
        Assert.Equal("InfoBar.ReadOnly.Action.MakeWritable", h.FocusedAutomationId);
        h.Press("Escape");
        Assert.Equal("Editor.Legacy.pas", h.FocusedAutomationId);
        Assert.Equal("  Café\r\nx\n", document.Buffer.Text);

        // Writable: Shift+Tab outdents while there is indentation to remove, then reaches the bars.
        h.Click("InfoBar.ReadOnly.Action.MakeWritable");
        await h.WaitForAsync(() => !document.IsReadOnly, "writable");
        Assert.Equal(["InfoBar.ExternalChange", "InfoBar.MixedLineEndings"], BarsTopToBottom(h));
        Assert.True(editor.TextArea.Focus());
        h.Press("Shift+Tab");
        Assert.Equal("Café\r\nx\n", document.Buffer.Text);
        Assert.Equal("Editor.Legacy.pas", h.FocusedAutomationId);
        h.Press("Shift+Tab");
        Assert.Equal("InfoBar.ExternalChange.Action.Reload", h.FocusedAutomationId);
        h.Press("Escape");
        Assert.Equal("Editor.Legacy.pas", h.FocusedAutomationId);
    }

    private static InfoBarViewModel Bar(string text) =>
        new("ExternalChange", InfoBarSeverity.Warning, text, [new InfoBarAction("Reload", "Reload", () => Task.CompletedTask)], dismiss: null);

    private static string[] BarsTopToBottom(HeadlessHarness h) =>
        [.. h.FindAll("InfoBar.")
            .Where(c => c is Ide.Views.InfoBarView && c.IsEffectivelyVisible)
            .OrderBy(c => c.TranslatePoint(default, h.Window)!.Value.Y)
            .Select(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c)!)];

    private static string? IconOf(HeadlessHarness h, string kind) =>
        h.Find<Control>("InfoBar." + kind).GetVisualDescendants().OfType<IdeIcon>().Single().IconName;
}
