using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Go to Line dialog (I-059, dialogs.md §12, UI-DLG-120).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class GoToLineDialogTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-120")]
    public async Task AcceptsLineAndLineColumn()
    {
        var text = new StringBuilder();
        for (int i = 1; i <= 200; i++)
            text.Append(i % 2 == 0 ? "  " : "").Append("Line").Append(i).Append(";\r\n");
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = text.ToString());
        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "A.pas"))).Success);
        Dispatcher.UIThread.RunJobs();
        var editor = h.Find<TextEditor>("Editor.A.pas");
        editor.TextArea.Focus();
        editor.TextArea.Caret.Line = 3;
        Dispatcher.UIThread.RunJobs();

        async Task<DialogDriver> OpenAsync()
        {
            h.Press("Ctrl+G");
            return await h.WaitForDialogAsync("GoToLine");
        }

        void SetLine(DialogDriver dlg, string value)
        {
            dlg.Find<TextBox>("Line").Text = value;
            Dispatcher.UIThread.RunJobs();
        }

        // 320 wide, not resizable; the field holds the caret line, selected; the label names the range.
        var dlg = await OpenAsync();
        Assert.Equal(("Go to Line", 320.0, false), (dlg.Window.Title, dlg.Window.Width, dlg.Window.CanResize));
        var line = dlg.Find<TextBox>("Line");
        await h.WaitForAsync(() => dlg.FocusedAutomationId == "Dialog.GoToLine.Line", "field focused");
        Assert.Equal(("3", 0, 1), (line.Text, line.SelectionStart, line.SelectionEnd));
        Assert.Equal("_Line (1–201):", dlg.Window.GetVisualDescendants().OfType<Label>().Single().Content);
        Assert.Equal("Go", AutomationProperties.GetName(dlg.Find<Button>("Ok")));

        // Anything but L or L:C with L in 1…n is refused with the message; Go is disabled meanwhile.
        var outOfRange = string.Format(Strings.Msg_GoToLine_OutOfRange, 201);
        foreach (var bad in new[] { "abc", "0", "202", "12:", "" })
        {
            SetLine(dlg, bad);
            Assert.Equal(outOfRange, dlg.ErrorText("Line"));
            Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled, bad);
        }

        // L:C — Enter goes there, clears the selection, closes and returns the focus to the editor.
        SetLine(dlg, " 120:3 ");
        Assert.Null(dlg.ErrorText("Line"));
        Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        editor.Select(0, 4);
        dlg.Press("Enter");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        Assert.Equal((120, 3, 0), (editor.TextArea.Caret.Line, editor.TextArea.Caret.Column, editor.SelectionLength));
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.A.pas", "editor focused");

        // The line is scrolled to one third of the viewport.
        var scroller = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
        var textView = editor.TextArea.TextView;
        var top = textView.GetVisualTopByDocumentLine(120) - scroller.Offset.Y;
        Assert.InRange(top, scroller.Viewport.Height / 3 - textView.DefaultLineHeight, scroller.Viewport.Height / 3 + textView.DefaultLineHeight);

        // L,C via the Go button; L alone → the first non-blank character.
        dlg = await OpenAsync();
        Assert.Equal("120", dlg.Find<TextBox>("Line").Text);
        SetLine(dlg, "20,2");
        dlg.Click("Ok");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        Assert.Equal((20, 2), (editor.TextArea.Caret.Line, editor.TextArea.Caret.Column));
        dlg = await OpenAsync();
        SetLine(dlg, "8");
        dlg.Press("Enter");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        Assert.Equal((8, 3), (editor.TextArea.Caret.Line, editor.TextArea.Caret.Column));

        // A column beyond the line goes to its end; Escape changes nothing.
        dlg = await OpenAsync();
        SetLine(dlg, "9:999");
        dlg.Press("Enter");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        Assert.Equal((9, 7), (editor.TextArea.Caret.Line, editor.TextArea.Caret.Column));
        dlg = await OpenAsync();
        SetLine(dlg, "1");
        dlg.Press("Escape");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
        Assert.Equal((9, 7), (editor.TextArea.Caret.Line, editor.TextArea.Caret.Column));
    }
}
