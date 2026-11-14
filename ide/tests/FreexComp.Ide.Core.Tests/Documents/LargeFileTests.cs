using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Documents;

/// <summary>Large-file mode (I-061, phase1-shell.md IDE-P1-054, states-and-messages.md <c>Msg.File.TooLarge</c>).</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Unit")]
public sealed class LargeFileTests
{
    private const string Path = @"C:\t\Big.pas";

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-054")]
    public async Task OpensAsPlainTextAfterConfirmation()
    {
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        var dialogs = (FakeDialogService)services.Dialogs;
        services.Settings.Set(SettingsCatalog.FilesLargeFileThresholdMb, 1);
        services.Settings.Set(SettingsCatalog.EditorWordWrap, true);
        fs.CreateDirectory(@"C:\t");

        // 1.5 MB of Object Pascal with foldable routines and brackets.
        const string Routine = "procedure P;\r\nbegin\r\n  WriteLn((1 + 2) * 3);\r\nend;\r\n\r\n";
        var text = new StringBuilder("unit Big;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\n");
        while (text.Length < 1536 * 1024 - 16)
            text.Append(Routine);
        text.Append("end.\r\n");
        var bytes = Encoding.ASCII.GetBytes(text.ToString().PadRight(1536 * 1024 - 2) + "\r\n");
        Assert.Equal(1536 * 1024, bytes.Length);
        fs.WriteAllBytes(Path, bytes);

        // Cancel: nothing opens and no further message is shown.
        dialogs.Enqueue("Msg.File.TooLarge", "Cancel");
        var canceled = await services.Documents.OpenAsync(Path);
        Assert.Null(canceled.Value);
        Assert.True(canceled.IsCanceled);
        Assert.Empty(services.Documents.Documents);
        var asked = Assert.Single(dialogs.Requests);
        Assert.Equal(("Msg.File.TooLarge", "1.5"), (asked.MessageId, asked.Args.Single()));

        // file.open reports nothing for a canceled large file.
        ((FakeFileDialogService)services.FileDialogs).NextOpen(Path);
        dialogs.Enqueue("Msg.File.TooLarge", "Cancel");
        await services.Commands.ExecuteAsync("file.open");
        Assert.Equal(["Msg.File.TooLarge", "Msg.File.TooLarge"], dialogs.Requests.Select(r => r.MessageId));
        Assert.Empty(services.Documents.Documents);

        // Open: Plain Text (no highlighting, folding or bracket matching), word wrap forced off.
        dialogs.Enqueue("Msg.File.TooLarge", "Open");
        var document = Assert.IsType<TextDocumentViewModel>((await services.Documents.OpenAsync(Path)).Value);
        services.FocusTracker.Update(new FocusState(CommandScope.Editor, null, document, false));
        Assert.Equal("plaintext", document.Language.Id);
        Assert.True(document.IsLargeFile);
        Assert.Empty(document.Folding.Regions);
        Assert.False(document.ViewState.WordWrap);
        Assert.False(services.Commands.CanExecute("view.wordWrap"));
        document.Selection = TextSelection.At(document.Buffer.GetOffset(9, 11)); // after the first "(" of "WriteLn((1 + 2)"
        await services.Commands.ExecuteAsync("search.goToMatchingBracket");
        Assert.Equal(new TextPosition(9, 11), document.ViewState.Caret);
        Assert.Equal(Strings.Msg_Editor_BracketNotFound, services.Status.Message);
        Assert.Equal(bytes.Length, document.Buffer.Length);
        Assert.False(document.IsModified);

        // A file of exactly the threshold size opens without asking, with its own language.
        Assert.True(services.Documents.Close(document));
        fs.WriteAllBytes(Path, [.. bytes.Take(1024 * 1024 - 6), .. "end.\r\n"u8.ToArray()]);
        var normal = Assert.IsType<TextDocumentViewModel>((await services.Documents.OpenAsync(Path)).Value);
        Assert.Equal(3, dialogs.Requests.Count);
        Assert.Equal(("pascal", false, true), (normal.Language.Id, normal.IsLargeFile, normal.ViewState.WordWrap));
        // Above 200 KB its regions are computed in the background (I-159).
        var folded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        normal.Folding.Changed += (_, _) => folded.TrySetResult();
        if (normal.Folding.Regions.Count == 0)
            await folded.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.NotEmpty(normal.Folding.Regions);
    }
}
