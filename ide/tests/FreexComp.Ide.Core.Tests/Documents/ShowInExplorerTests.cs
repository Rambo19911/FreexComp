using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Documents;

/// <summary>
/// I-251 (phase8-ui-additions.md D2): a Show in File Explorer that cannot be carried out — the shell cannot be started,
/// or the path no longer exists — shows <c>Status_ShowInExplorerFailed</c> in the status bar; no dialog, no retry.
/// </summary>
[Trait("Phase", "8")]
public sealed class ShowInExplorerTests
{
    private const string Unit = @"C:\p\Hello\src\Unit1.pas";
    private const string Folder = @"C:\p\Hello\lib";

    [Fact(Timeout = 30000)]
    public Task ShellRefusal_ShowsStatusMessage() => TestUiThread.RunAsync(async () =>
    {
        var shell = new FakeShellService();
        var clock = new ManualClock();
        var dialogs = new FakeDialogService();
        var services = TestComposition.Build(o =>
        {
            o.Shell = shell;
            o.Clock = clock;
            o.Dialogs = dialogs;
        });
        services.FileSystem.CreateDirectory(System.IO.Path.GetDirectoryName(Unit)!);
        services.FileSystem.WriteAllBytes(Unit, []);
        services.FileSystem.CreateDirectory(Folder);
        Assert.Equal("Cannot show \"{0}\" in File Explorer.", Strings.Status_ShowInExplorerFailed);

        // Carried out: no message.
        await services.Commands.ExecuteAsync("file.showInExplorer", Unit);
        Assert.Equal(["RevealFile " + Unit], shell.Calls);
        Assert.Null(services.Status.Message);

        // The shell refuses: the message names the file without its directory; nothing else happens.
        shell.Succeeds = false;
        await services.Commands.ExecuteAsync("file.showInExplorer", Unit);
        Assert.Equal(["RevealFile " + Unit, "RevealFile " + Unit], shell.Calls); // one attempt, no retry
        Assert.Equal("Cannot show \"Unit1.pas\" in File Explorer.", services.Status.Message);
        Assert.Empty(dialogs.Requests);

        // A folder is named by its last part.
        await services.Commands.ExecuteAsync("file.showInExplorer", Folder + @"\");
        Assert.Equal("Cannot show \"lib\" in File Explorer.", services.Status.Message);

        // The message is a status message: it goes after its time; a later success shows none.
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Null(services.Status.Message);
        shell.Succeeds = true;
        await services.Commands.ExecuteAsync("file.showInExplorer", Unit);
        Assert.Null(services.Status.Message);
    });

    [Fact(Timeout = 30000)]
    public Task MissingPath_ShowsStatusMessage() => TestUiThread.RunAsync(async () =>
    {
        // The file exists when the command is enabled and is gone when it runs (deleted outside the IDE in between).
        var inner = new InMemoryFileSystem();
        var files = new RecordingFileSystem(inner);
        var shell = new FakeShellService();
        var clock = new ManualClock();
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = files;
            o.Shell = shell;
            o.Clock = clock;
        });
        inner.CreateDirectory(System.IO.Path.GetDirectoryName(Unit)!);
        inner.WriteAllBytes(Unit, []);
        Assert.True(services.Commands.CanExecute("file.showInExplorer", Unit));

        var checks = 0;
        files.FailWhen = call =>
        {
            if (call == "FileExists " + Unit && ++checks == 2)
                inner.DeleteFile(Unit); // after the enablement check of the execution, before the handler looks
            return false;
        };
        await services.Commands.ExecuteAsync("file.showInExplorer", Unit);
        files.FailWhen = null;

        Assert.Equal(2, checks);
        Assert.False(inner.FileExists(Unit));
        Assert.Empty(shell.Calls); // the shell is not asked for a path that is not there
        Assert.Equal("Cannot show \"Unit1.pas\" in File Explorer.", services.Status.Message);

        // A path that is missing beforehand keeps the command disabled: no message (UI-CMD-012).
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.False(services.Commands.CanExecute("file.showInExplorer", Unit));
        await services.Commands.ExecuteAsync("file.showInExplorer", Unit);
        Assert.Null(services.Status.Message);
        Assert.Empty(shell.Calls);
    });
}
