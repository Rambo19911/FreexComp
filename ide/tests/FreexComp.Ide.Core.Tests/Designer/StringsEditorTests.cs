using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// The strings editor's <c>Load…</c> and <c>Save…</c> of a text file (phase8/menu-designer-collection-editors.md §4.2,
/// DSG-OI-130 … 134; D-159): the pickers are the fake file dialogs, the message boxes the fake dialog service, the files
/// live in the in-memory file system.
/// </summary>
[Trait("Phase", "8")]
public sealed class StringsEditorTests
{
    private const string Dir = @"C:\texts";

    private static readonly string[] Encodings = ["utf-8", "utf-8-bom", "utf-16le", "utf-16be", "windows-1252"];

    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeDialogService _messages = new();
    private readonly IdeServices _services;

    public StringsEditorTests()
    {
        _services = TestComposition.Build(o => o.FileSystem = _fs);
        _fs.CreateDirectory(Dir);
    }

    private FakeFileDialogService Pickers => (FakeFileDialogService)_services.FileDialogs;

    private StringsEditorViewModel Editor(params string[] lines) =>
        new("Lines", "Memo1", lines, _services.FilePrompts, _messages, StringsEditorViewModel.SuggestedFileName("Lines", "Memo1"));

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(Dir, name);
        _fs.WriteAllBytes(path, bytes);
        return path;
    }

    private void Configure(string encoding, string lineEnding)
    {
        _services.Settings.Set(SettingsCatalog.FilesDefaultEncoding, encoding);
        _services.Settings.Set(SettingsCatalog.FilesDefaultLineEnding, lineEnding);
    }

    private static string[] Labels(FilePickerRequest request) => [.. request.Filters.Select(f => f.Label)];

    private static byte[] Bytes(params object[] parts) =>
        [.. parts.SelectMany(p => p switch { string s => Encoding.UTF8.GetBytes(s), byte b => [b], byte[] a => a, _ => throw new ArgumentException(p.ToString()) })];

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-130")]
    [Trait("Req", "DSG-OI-132")]
    [Trait("Req", "DSG-OI-134")]
    public Task LoadAndSaveFile() => TestUiThread.RunAsync(async () =>
    {
        // Load: a UTF-8 file with BOM, LF line ends and a final line break replaces the whole text with its lines.
        var source = Write("in.txt", Bytes(new byte[] { 0xEF, 0xBB, 0xBF }, "first\nsecond é→\nthird\n"));
        var editor = Editor("old", "text");
        var (loaded, completed) = (0, 0);
        editor.TextLoaded += (_, _) => loaded++;
        editor.FileActionCompleted += (_, _) => completed++;
        Assert.True(editor.HasFileButtons);
        Assert.Equal(["Load", "Save", "Ok", "Cancel"], editor.Buttons.Select(b => b.Id));
        Assert.Equal([true, true, false, false], editor.Buttons.Select(b => b.IsAuxiliary));
        Assert.Equal(("Load…", "_Load…", "Save…", "_Save…"), (editor.Load!.Label, editor.Load.AccessLabel, editor.Save!.Label, editor.Save.AccessLabel));

        Pickers.NextOpen(source);
        await editor.Load.PressAsync();
        Assert.Equal("first\r\nsecond é→\r\nthird", editor.Text); // no extra line for the final line break
        Assert.Equal("3 lines", editor.LineCountText);
        Assert.Equal((1, 1), (loaded, completed));
        Assert.Empty(_messages.Requests);
        Assert.False(editor.IsClosed); // nothing reaches the form before OK

        // Save writes the editor's text (not what was loaded): the settings' encoding and line ending, a break after every line.
        editor.Text = "first\r\nchanged é→";
        var target = Path.Combine(Dir, "out.txt");
        Pickers.NextSave(target);
        await editor.Save.PressAsync();
        Assert.Equal(Bytes("first\r\nchanged é→\r\n"), _fs.ReadAllBytes(target)); // utf-8 without BOM, CR LF: the defaults
        Assert.Equal(("first\r\nchanged é→", false, 2), (editor.Text, editor.IsClosed, completed));
        Assert.Equal(1, loaded);
        Assert.Empty(_messages.Requests);

        // OK returns the lines of the editor.
        await editor.PressDefaultAsync();
        Assert.Equal(["first", "changed é→"], editor.Result);

        // An empty text gives an empty file; loading it gives no line.
        var empty = Editor();
        Pickers.NextSave(target);
        await empty.SaveAsync();
        Assert.Empty(_fs.ReadAllBytes(target));
        var reloaded = Editor("x");
        Pickers.NextOpen(target);
        await reloaded.LoadAsync();
        Assert.Equal(("", "0 lines"), (reloaded.Text, reloaded.LineCountText));

        // Round trip (DSG-OI-134): saved, loaded again → the same lines, for the five encodings and both line endings.
        string[][] samples = [["alpha", "", "é € ÿ", "last"], ["one"], ["ends with an empty line", ""], [""], []];
        foreach (var encoding in Encodings)
        {
            foreach (var lineEnding in (string[])["crlf", "lf"])
            {
                Configure(encoding, lineEnding);
                foreach (var lines in samples)
                {
                    var writer = Editor(lines);
                    Assert.Equal(lines, StringsEditorViewModel.Split(writer.Text)); // the editor shows the lines it was given
                    var file = Path.Combine(Dir, $"{encoding}-{lineEnding}.txt");
                    Pickers.NextSave(file);
                    await writer.SaveAsync();
                    var bytes = _fs.ReadAllBytes(file);
                    var id = FileEncodings.FromId(encoding)!.Value;
                    Assert.True(bytes.AsSpan().StartsWith(FileEncodings.Preamble(id)), $"{encoding}: preamble");
                    var breakText = lineEnding == "lf" ? "\n" : "\r\n";
                    Assert.Equal(string.Concat(lines.Select(l => l + breakText)), FileEncodings.Decode(bytes, id));

                    var reader = Editor("something else");
                    Pickers.NextOpen(file);
                    await reader.LoadAsync();
                    Assert.Equal(lines, StringsEditorViewModel.Split(reader.Text));
                    await reader.PressDefaultAsync();
                    Assert.Equal(lines, reader.Result);
                }
            }
        }

        Assert.Empty(_messages.Requests);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-131")]
    public Task LoadRefusesBinaryTooLargeAndUnreadable() => TestUiThread.RunAsync(async () =>
    {
        foreach (var id in (string[])["Msg.StringsEditor.NotText", "Msg.StringsEditor.FileTooLarge", "Msg.StringsEditor.LoadFailed"])
        {
            _messages.Answer(id, "OK");
        }

        var editor = Editor("kept", "text");
        var (loaded, completed) = (0, 0);
        editor.TextLoaded += (_, _) => loaded++;
        editor.FileActionCompleted += (_, _) => completed++;
        var reads = new List<string>();
        _fs.BeforeRead = reads.Add;

        async Task<MessageRequest> RefusedAsync(string path)
        {
            var before = _messages.Requests.Count;
            Pickers.NextOpen(path);
            await editor.LoadAsync();
            Assert.Equal(before + 1, _messages.Requests.Count); // one message box
            Assert.Equal(("kept\r\ntext", false, 0), (editor.Text, editor.IsClosed, loaded)); // text unchanged, the dialog stays open
            return _messages.Requests[^1];
        }

        // A binary file (IDE-P1-072: a 00 byte in the first 8 000 bytes, no UTF-16 BOM).
        var binary = Write("image.bin", Bytes("MZ", (byte)0, "rest"));
        var request = await RefusedAsync(binary);
        Assert.Equal("Msg.StringsEditor.NotText", request.MessageId);
        Assert.Equal([binary], request.Args);

        // Larger than 1 048 576 bytes: refused before it is read.
        var large = Write("large.txt", [.. Enumerable.Repeat((byte)'a', StringsEditorViewModel.MaxLoadBytes + 1)]);
        reads.Clear();
        request = await RefusedAsync(large);
        Assert.Equal("Msg.StringsEditor.FileTooLarge", request.MessageId);
        Assert.Equal([large], request.Args);
        Assert.Empty(reads);

        // A file that is not there, and one that cannot be read: the system's reason is shown.
        var missing = Path.Combine(Dir, "missing.txt");
        request = await RefusedAsync(missing);
        Assert.Equal("Msg.StringsEditor.LoadFailed", request.MessageId);
        Assert.Equal(missing, request.Args[0]);
        Assert.False(string.IsNullOrWhiteSpace(request.Args[1]));
        var locked = Write("locked.txt", Bytes("text"));
        _fs.BeforeRead = path => throw new IOException("The process cannot access the file '" + path + "' because it is being used by another process.");
        request = await RefusedAsync(locked);
        Assert.Equal("Msg.StringsEditor.LoadFailed", request.MessageId);
        Assert.Equal([locked, "The process cannot access the file '" + locked + "' because it is being used by another process."], request.Args);
        _fs.BeforeRead = null;
        Assert.Equal(4, completed); // each time the focus goes back to the text (UI-DLG-345)

        // The limit itself is allowed: exactly 1 048 576 bytes load.
        var limit = Write("limit.txt", [.. Enumerable.Repeat((byte)'b', StringsEditorViewModel.MaxLoadBytes)]);
        Pickers.NextOpen(limit);
        await editor.LoadAsync();
        Assert.Equal((StringsEditorViewModel.MaxLoadBytes, 1, 4), (editor.Text.Length, loaded, _messages.Requests.Count));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-132")]
    [Trait("Req", "DSG-OI-133")]
    public Task SaveUsesNewFileSettingsNeverLossy() => TestUiThread.RunAsync(async () =>
    {
        // windows-1252, LF: a text the code page holds is written in it (€ = 80), without a preamble.
        Configure("windows-1252", "lf");
        var target = Path.Combine(Dir, "price.txt");
        Pickers.NextSave(target);
        await Editor("price 5 €", "é").SaveAsync();
        Assert.Equal(Bytes("price 5 ", (byte)0x80, "\n", (byte)0xE9, "\n"), _fs.ReadAllBytes(target));

        // A character the code page cannot hold: UTF-8 is written instead, without a question (never lossy).
        Pickers.NextSave(target);
        await Editor("a → b", "é").SaveAsync();
        Assert.Equal(Bytes("a → b\né\n"), _fs.ReadAllBytes(target));
        Assert.Empty(_messages.Requests);

        // A failing write: the message with the system's reason; the target is untouched and no temp file is left.
        _messages.Answer("Msg.StringsEditor.SaveFailed", "OK");
        _fs.SetReadOnly(target, true);
        var files = _fs.AllFiles;
        var editor = Editor("new text");
        var completed = 0;
        editor.FileActionCompleted += (_, _) => completed++;
        Pickers.NextSave(target);
        await editor.SaveAsync();
        var request = Assert.Single(_messages.Requests);
        Assert.Equal("Msg.StringsEditor.SaveFailed", request.MessageId);
        Assert.Equal(target, request.Args[0]);
        Assert.False(string.IsNullOrWhiteSpace(request.Args[1]));
        Assert.Equal(files, _fs.AllFiles);
        Assert.Equal(Bytes("a → b\né\n"), _fs.ReadAllBytes(target));
        Assert.Equal(("new text", false, 1), (editor.Text, editor.IsClosed, completed));

        // A folder that does not exist: the same message, no file at all.
        var nowhere = @"C:\nowhere\out.txt";
        Pickers.NextSave(nowhere);
        await editor.SaveAsync();
        Assert.Equal(("Msg.StringsEditor.SaveFailed", nowhere), (_messages.Requests[^1].MessageId, _messages.Requests[^1].Args[0]));
        Assert.Equal(files, _fs.AllFiles);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-130")]
    [Trait("Req", "DSG-OI-132")]
    public Task CanceledPickersChangeNothing() => TestUiThread.RunAsync(async () =>
    {
        var editor = Editor("kept");
        var (loaded, completed) = (0, 0);
        editor.TextLoaded += (_, _) => loaded++;
        editor.FileActionCompleted += (_, _) => completed++;
        var files = _fs.AllFiles;

        // Load…: the open picker of the prompt stringsLoad — one file, text files first.
        Pickers.NextCancel();
        await editor.LoadAsync();
        var load = Assert.Single(Pickers.Requests);
        Assert.Equal(("stringsLoad", "Load Text", false), (load.PromptId, load.Title, load.AllowMultiple));
        Assert.Equal(["Text files (*.txt)", "All files (*.*)"], Labels(load));
        Assert.Equal(("kept", false, 0, 1), (editor.Text, editor.IsClosed, loaded, completed));

        // Save…: the save picker of the prompt stringsSave with the suggested name {Component}.{Property}.txt.
        Pickers.NextCancel();
        await editor.SaveAsync();
        var save = Pickers.Requests[^1];
        Assert.Equal(("stringsSave", "Save Text As", "txt", "Memo1.Lines.txt"), (save.PromptId, save.Title, save.DefaultExtension, save.SuggestedName));
        Assert.Equal(["Text files (*.txt)", "All files (*.*)"], Labels(save));
        Assert.Equal(("kept", false, 0, 2), (editor.Text, editor.IsClosed, loaded, completed));
        Assert.Equal(files, _fs.AllFiles);
        Assert.Empty(_messages.Requests);

        // The initial folder follows UI-DLG-051: the last folder of the same prompt after a successful pick.
        Pickers.NextSave(Path.Combine(Dir, "kept.txt"));
        await editor.SaveAsync();
        Pickers.NextCancel();
        await editor.SaveAsync();
        Assert.Equal(Dir, Pickers.Requests[^1].InitialFolder);
        Pickers.NextCancel();
        await editor.LoadAsync();
        Assert.NotEqual(Dir, Pickers.Requests[^1].InitialFolder); // per prompt

        // A multi-selection has no component name in the suggested name.
        Assert.Equal("Memo1.Lines.txt", StringsEditorViewModel.SuggestedFileName("Lines", "Memo1"));
        Assert.Equal("Lines.txt", StringsEditorViewModel.SuggestedFileName("Lines", null));

        // Without the file services the editor has no Load… / Save… (and both calls do nothing).
        var plain = new StringsEditorViewModel("Lines", "Memo1", ["a"]);
        Assert.False(plain.HasFileButtons);
        Assert.Equal(["Ok", "Cancel"], plain.Buttons.Select(b => b.Id));
        Assert.Null(plain.Load ?? plain.Save);
        var requests = Pickers.Requests.Count;
        await plain.LoadAsync();
        await plain.SaveAsync();
        Assert.Equal(requests, Pickers.Requests.Count);
    });
}
