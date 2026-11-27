using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.LanguageService.Formatting;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>
/// The formatter commands and format-on-save (I-337, formatter.md §4): <c>edit.formatDocument</c> /
/// <c>edit.formatSelection</c> through the built-in Pascal formatter as one undo step, the refusal and no-change status
/// messages, the invalid-profile Messages row and <c>editor.formatOnSave</c>.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class FormatCommandTests
{
    /// <summary>Two routines laid out badly: statements at column 1, no spaces around <c>:=</c>, two statements on a line.</summary>
    private const string Messy =
        "unit Messy;\r\n" +
        "\r\n" +
        "interface\r\n" +
        "\r\n" +
        "procedure A;\r\n" +
        "procedure B;\r\n" +
        "\r\n" +
        "implementation\r\n" +
        "\r\n" +
        "procedure A;\r\n" +
        "var x:Integer;\r\n" +
        "begin\r\n" +
        "x:=1;writeln(x);\r\n" + // a line break and a case change at one offset (I-337 visual check)
        "if x=1 then x:=2;\r\n" +
        "end;\r\n" +
        "\r\n" +
        "procedure B;\r\n" +
        "var y:Integer;\r\n" +
        "begin\r\n" +
        "y:=1;\r\n" +
        "y:=y+1;\r\n" +
        "end;\r\n" +
        "\r\n" +
        "end.\r\n";

    /// <summary>The engine's own result with <paramref name="profile"/> (the oracle: the commands apply exactly its edits).</summary>
    private static string Formatted(string text, FormatProfile profile, (int Start, int Length)? range = null)
    {
        var result = PascalFormatter.Format(FreexComp.Compiler.SourceText.From(@"C:\work\Messy.pas", text), profile, range is { } r
            ? new FreexComp.Compiler.Text.TextSpan(r.Start, r.Length) : null, CancellationToken.None);
        Assert.False(result.Refused, result.RefusalReason);
        var formatted = text;
        foreach (var edit in result.Edits.Reverse())
            formatted = formatted[..edit.Span.Start] + edit.NewText + formatted[edit.Span.End..];
        return formatted;
    }

    private static async Task<TextDocumentViewModel> OpenAsync(HeadlessHarness h, string name = "Messy.pas")
    {
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, name));
        Assert.True(result.Success, result.MessageId);
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    private static List<MessageItem> ToolRows(HeadlessHarness h) => [.. h.Services.Messages.Items.Where(i => !i.IsSummary)];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-154")]
    [Trait("Req", "IDE-ED-156")]
    public async Task FormatDocumentIsOneUndoStep()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["Messy.pas"] = Messy;
            o.InitialFiles["Notes.txt"] = "x:=1;\r\n";
        });
        var doc = await OpenAsync(h);
        var expected = Formatted(Messy, FormatProfile.Default);
        Assert.NotEqual(Messy, expected);

        // Shift+Alt+F in the editor is the command; it is enabled for a writable Pascal document.
        Assert.Equal("edit.formatDocument", h.Services.Keymap.FindCommand(KeyGesture.Parse("Shift+Alt+F"), CommandScope.Editor, null));
        Assert.True(h.Services.Commands.Get("edit.formatDocument").CanExecute(null));

        // The caret before the second y of "y:=y+1" and a bookmark on that line follow the edits.
        var caret = Messy.IndexOf("y:=y+1", StringComparison.Ordinal) + 3;
        doc.Selection = TextSelection.At(caret);
        var line = doc.Buffer.GetPosition(caret).Line;
        var bookmark = h.Services.Bookmarks.Ensure(doc, line);
        await h.ExecuteAsync("edit.formatDocument");

        Assert.Equal(expected, doc.Buffer.Text);
        var newCaret = expected.IndexOf("y := y + 1", StringComparison.Ordinal) + 5;
        Assert.Equal(newCaret, doc.Selection.Caret);
        Assert.True(doc.Selection.IsEmpty);
        var moved = Assert.Single(h.Services.Bookmarks.All, b => b.Id == bookmark.Id);
        Assert.Equal(doc.Buffer.GetPosition(newCaret).Line, moved.Line);
        Assert.True(doc.IsModified);

        // One undo step: Ctrl+Z restores the text and the caret, Ctrl+Y formats again.
        await h.ExecuteAsync("edit.undo");
        Assert.Equal(Messy, doc.Buffer.Text);
        Assert.Equal(caret, doc.Selection.Caret);
        Assert.False(doc.Buffer.CanUndo);
        await h.ExecuteAsync("edit.redo");
        Assert.Equal(expected, doc.Buffer.Text);

        // Already formatted: nothing changes, the status bar says so.
        await h.ExecuteAsync("edit.formatDocument");
        Assert.Equal(expected, doc.Buffer.Text);
        Assert.Equal("Already formatted", h.Status.Message);

        // Only Pascal documents: the commands are disabled in a text file.
        await OpenAsync(h, "Notes.txt");
        Assert.False(h.Services.Commands.Get("edit.formatDocument").CanExecute(null));
        Assert.False(h.Services.Commands.Get("edit.formatSelection").CanExecute(null));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-154")]
    public async Task FormatSelectionOnlyTouchesRange()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["Messy.pas"] = Messy);
        var doc = await OpenAsync(h);
        Assert.Equal("edit.formatSelection", h.Services.Keymap.FindCommand(KeyGesture.Parse("Shift+Alt+S"), CommandScope.Editor, null));

        // Disabled without a selection.
        doc.Selection = TextSelection.At(Messy.IndexOf("y:=1", StringComparison.Ordinal));
        Assert.False(h.Services.Commands.Get("edit.formatSelection").CanExecute(null));

        // The two statements of B selected: B's body is formatted, A is left exactly as it was.
        var start = Messy.IndexOf("y:=1", StringComparison.Ordinal);
        var end = Messy.IndexOf("y:=y+1;", StringComparison.Ordinal) + "y:=y+1;".Length;
        // Pressed in the editor (the keymap comes before the menu mnemonics, UI-KEY-004).
        Assert.True(h.Find<TextEditor>("Editor.Messy.pas").TextArea.Focus());
        Dispatcher.UIThread.RunJobs();
        doc.Selection = new TextSelection(start, end);
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Services.Commands.Get("edit.formatSelection").CanExecute(null));
        h.Press("Shift+Alt+S");

        var expected = Formatted(Messy, FormatProfile.Default, (start, end - start));
        await h.WaitForAsync(() => doc.Buffer.Text == expected, "the formatted selection");
        Assert.Contains("  y := 1;\r\n  y := y + 1;\r\n", doc.Buffer.Text, StringComparison.Ordinal);
        var routineA = Messy[..Messy.IndexOf("procedure B;\r\nvar", StringComparison.Ordinal)];
        Assert.StartsWith(routineA, doc.Buffer.Text, StringComparison.Ordinal);

        // One undo step.
        await h.ExecuteAsync("edit.undo");
        Assert.Equal(Messy, doc.Buffer.Text);
        Assert.False(doc.Buffer.CanUndo);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-154")]
    public async Task RefusedShowsStatus()
    {
        var broken = Messy.Replace("y:=y+1;", "y:=;", StringComparison.Ordinal);
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["Messy.pas"] = broken);
        var doc = await OpenAsync(h);
        await h.ExecuteAsync("edit.formatDocument");

        Assert.Equal(broken, doc.Buffer.Text);
        Assert.False(doc.Buffer.CanUndo);
        Assert.False(doc.IsModified);
        var message = h.Status.Message;
        Assert.NotNull(message);
        Assert.Matches(@"^Not formatted: \(21,\d+\): error E1\d{3}: ", message);
        Assert.Empty(ToolRows(h));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-084")]
    public async Task FormatOnSave()
    {
        var broken = Messy.Replace("y:=y+1;", "y:=;", StringComparison.Ordinal);
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["Messy.pas"] = Messy;
            o.InitialFiles["Broken.pas"] = broken;
        });
        Assert.False(h.Services.Settings.Get(SettingsCatalog.EditorFormatOnSave)); // off by default
        var path = Path.Combine(h.TempDir, "Messy.pas");

        // Off: a save writes the text as it is.
        var doc = await OpenAsync(h);
        doc.TryInsertText(0, 0, "{ x }\r\n");
        await h.ExecuteAsync("file.save");
        Assert.Equal("{ x }\r\n" + Messy, File.ReadAllText(path));

        // On: the save formats first (one undo step of its own) and writes the formatted text.
        h.Services.Settings.Set(SettingsCatalog.EditorFormatOnSave, true);
        doc.TryInsertText(0, 7, "");
        await h.ExecuteAsync("file.save");
        var expected = Formatted(Messy, FormatProfile.Default);
        Assert.Equal(expected, doc.Buffer.Text);
        Assert.Equal(expected, File.ReadAllText(path));
        Assert.False(doc.IsModified);
        await h.ExecuteAsync("edit.undo");
        Assert.Equal(Messy, doc.Buffer.Text);

        // A file the formatter refuses is saved unformatted, with the reason in the status bar and no prompt.
        var other = await OpenAsync(h, "Broken.pas");
        other.TryInsertText(0, 0, "{ y }\r\n");
        await h.ExecuteAsync("file.save");
        Assert.Equal("{ y }\r\n" + broken, File.ReadAllText(Path.Combine(h.TempDir, "Broken.pas")));
        Assert.False(other.IsModified);
        Assert.StartsWith("Not formatted: (", h.Status.Message, StringComparison.Ordinal);
        Assert.Empty(h.Messages.Unanswered);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-155")]
    public async Task InvalidProfileReportsOnce()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.InitialFiles["Messy.pas"] = Messy;
            o.InitialFiles[@"Sub\Messy.pas"] = Messy;
            o.InitialFiles[@"Sub\.pasformat"] = "{ \"indentSize\": 4 }";
            o.InitialFiles[".pasformat"] = "{ indentSize: ";
        });
        var profile = Path.Combine(h.TempDir, ".pasformat");

        // A run with a broken profile: one warning row naming the file, the built-in style is used.
        var doc = await OpenAsync(h);
        await h.ExecuteAsync("edit.formatDocument");
        Assert.Equal(Formatted(Messy, FormatProfile.Default), doc.Buffer.Text);
        var row = Assert.Single(ToolRows(h));
        Assert.Equal(MessageSeverity.Warning, row.Severity);
        Assert.Equal($"Format profile {profile} is not valid JSON; the built-in style was used.", row.Text);
        Assert.Equal(profile, row.Location?.FilePath);

        // Each run reports once: a second run (nothing to change) adds one more row, not more.
        await h.ExecuteAsync("edit.formatDocument");
        Assert.Equal(2, ToolRows(h).Count);

        // A valid profile nearer the file is used and reports nothing.
        var nested = await OpenAsync(h, @"Sub\Messy.pas");
        await h.ExecuteAsync("edit.formatDocument");
        Assert.Contains("\r\n    y := 1;\r\n", nested.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(2, ToolRows(h).Count);
    }
}
