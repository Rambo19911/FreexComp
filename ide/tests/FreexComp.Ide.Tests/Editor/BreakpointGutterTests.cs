using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>Breakpoints in the editor (I-134): F9, Ctrl+F9, Ctrl+Shift+F9 and the glyph-margin click; glyph states via the probe.</summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
public sealed class BreakpointGutterTests
{
    private static T Margin<T>(TextEditor editor) where T : AbstractMargin => editor.TextArea.LeftMargins.OfType<T>().Single();

    private static void ClickLine(HeadlessHarness h, TextEditor editor, Control margin, int line)
    {
        var lineHeight = editor.TextArea.TextView.DefaultLineHeight;
        var point = margin.TranslatePoint(new Point(margin.Bounds.Width / 2, (line - 0.5) * lineHeight), h.Window)!.Value;
        h.Window.MouseDown(point, MouseButton.Left);
        h.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void CaretTo(TextEditor editor, int line)
    {
        editor.TextArea.Caret.Offset = editor.Document.GetLineByNumber(line).Offset;
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-012")]
    [Trait("Req", "UI-ED-020")]
    [Trait("Req", "UI-TW-802")]
    public async Task ToggleByKeyboardAndClick()
    {
        await using var h = await DebugScenario.StartAsync(o => o.InitialFiles[@"Hello\Notes.txt"] = "one\r\ntwo\r\nthree\r\n");
        var unitPath = DebugScenario.UnitPath(h);
        var (document, editor, probe) = await DebugScenario.OpenAsync(h, unitPath);
        var breakpoints = h.Services.Breakpoints!;

        // F9 on the caret line: an enabled breakpoint with its line background; F9 again removes it.
        CaretTo(editor, 6);
        Assert.Null(probe.GlyphAt(6));
        h.Press("F9");
        Assert.Equal("Breakpoint", probe.GlyphAt(6));
        Assert.Equal("editor.breakpoint.line.bg", probe.LineBackgroundAt(6));
        var breakpoint = Assert.Single(breakpoints.All);
        Assert.Equal((unitPath, 6, true), (breakpoint.Path, breakpoint.Line, breakpoint.Enabled));

        // Ctrl+F9 disables and enables it: ring, no line background.
        h.Press("Ctrl+F9");
        Assert.Equal("BreakpointDisabled", probe.GlyphAt(6));
        Assert.NotEqual("editor.breakpoint.line.bg", probe.LineBackgroundAt(6));
        Assert.False(breakpoint.Enabled);
        h.Press("Ctrl+F9");
        Assert.Equal("Breakpoint", probe.GlyphAt(6));
        h.Press("F9");
        Assert.Null(probe.GlyphAt(6));
        Assert.Empty(breakpoints.All);
        Assert.False(h.Services.Commands.CanExecute("run.toggleBreakpointEnabled")); // the caret line has no breakpoint

        // UI-ED-012: a click in the glyph margin toggles a breakpoint on that line; the caret stays.
        var glyphs = Margin<GlyphMargin>(editor);
        ClickLine(h, editor, glyphs, 3);
        Assert.Equal("Breakpoint", probe.GlyphAt(3));
        Assert.Equal(6, document.ViewState.Caret.Line);
        ClickLine(h, editor, glyphs, 5);
        Assert.Equal([3, 5], breakpoints.All.Select(b => b.Line));
        ClickLine(h, editor, glyphs, 3);
        Assert.Null(probe.GlyphAt(3));
        Assert.Equal([5], breakpoints.All.Select(b => b.Line));

        // A click on a line number selects the line; it does not toggle.
        ClickLine(h, editor, Margin<FreexComp.Ide.Editor.LineNumberMargin>(editor), 2);
        Assert.Null(probe.GlyphAt(2));
        Assert.Equal([5], breakpoints.All.Select(b => b.Line));

        // The glyph follows its line when lines are inserted above.
        editor.Document.Insert(0, "// comment\r\n");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(probe.GlyphAt(5));
        Assert.Equal("Breakpoint", probe.GlyphAt(6));
        Assert.Equal([6], breakpoints.All.Select(b => b.Line));

        // menus.md §7: only Object Pascal text takes breakpoints.
        var (_, notes, notesProbe) = await DebugScenario.OpenAsync(h, Path.Combine(h.TempDir, @"Hello\Notes.txt"));
        CaretTo(notes, 2);
        Assert.False(h.Services.Commands.CanExecute("run.toggleBreakpoint"));
        h.Press("F9");
        ClickLine(h, notes, Margin<GlyphMargin>(notes), 2);
        Assert.Null(notesProbe.GlyphAt(2));
        Assert.Single(breakpoints.All);

        // Ctrl+Shift+F9 with one breakpoint deletes it; with more it asks Msg.Debug.DeleteAllBreakpoints first.
        h.Press("Ctrl+Shift+F9");
        Assert.Empty(breakpoints.All);
        Assert.False(h.Services.Commands.CanExecute("run.deleteAllBreakpoints"));
        breakpoints.Toggle(unitPath, 2);
        breakpoints.Toggle(unitPath, 4);
        h.Messages.Answer("Msg.Debug.DeleteAllBreakpoints", "Cancel");
        h.Press("Ctrl+Shift+F9");
        await h.WaitForAsync(() => h.Messages.Requests.Any(r => r.MessageId == "Msg.Debug.DeleteAllBreakpoints"), "the delete-all question");
        Assert.Equal(["2"], h.Messages.Requests.Single(r => r.MessageId == "Msg.Debug.DeleteAllBreakpoints").Args);
        Assert.Equal(2, breakpoints.All.Count);
        h.Messages.Answer("Msg.Debug.DeleteAllBreakpoints", "Delete");
        h.Press("Ctrl+Shift+F9");
        await h.WaitForAsync(() => breakpoints.All.Count == 0, "all breakpoints deleted");
        Assert.Equal(IdeState.Idle, h.Services.State.Current.State);
    }
}
