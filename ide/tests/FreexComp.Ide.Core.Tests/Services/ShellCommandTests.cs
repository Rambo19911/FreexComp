using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Services;

/// <summary><c>file.openInTerminal</c> (phase8-project-files.md §6).</summary>
[Trait("Phase", "8")]
public sealed class ShellCommandTests
{
    private const string Dir = @"C:\p\Hello";

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PM-140")]
    [Trait("Req", "IDE-PM-142")]
    public Task OpenInConsole_UsesSelectedNodeDirectory() => TestUiThread.RunAsync(() => OpenInConsole_UsesSelectedNodeDirectoryOnUiThread());

    private static async Task OpenInConsole_UsesSelectedNodeDirectoryOnUiThread()
    {
        var fs = new InMemoryFileSystem();
        var shell = new FakeShellService();
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.Shell = shell;
        });
        void File(string path, string text)
        {
            fs.CreateDirectory(Path.GetDirectoryName(path)!);
            fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        }

        File(Dir + @"\Hello.pproj", """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "lib/Unit2.pas" }, { "path": "gone/Unit3.pas" }, { "path": "../Shared/Common.pas" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        File(Dir + @"\lib\Unit2.pas", "unit Unit2;\ninterface\nimplementation\nend.\n");
        File(@"C:\p\Shared\Common.pas", "unit Common;\ninterface\nimplementation\nend.\n");
        File(@"C:\other\Loose.pas", "unit Loose;\ninterface\nimplementation\nend.\n");
        var commands = services.Commands;
        var dialogs = (FakeDialogService)services.Dialogs;
        Assert.False(commands.CanExecute("file.openInTerminal")); // no document, nothing selected

        Assert.True(await services.Projects.OpenAsync(Dir + @"\Hello.pproj"));
        var pm = services.ProjectManager;
        await pm.MissingFileChecks;

        // Outside the Project Manager: the active document's directory.
        Assert.True((await services.Documents.OpenAsync(@"C:\other\Loose.pas")).Success);
        await commands.ExecuteAsync("file.openInTerminal");
        Assert.Equal([@"OpenConsole C:\other"], shell.Calls);

        // In the Project Manager: root → project directory, folder → that directory, file → its directory.
        services.FocusTracker.Update(new FocusState(CommandScope.ProjectManager, ProjectManagerViewModel.ToolName, null, false));
        shell.Calls.Clear();
        foreach (var key in new[] { ProjectManagerViewModel.RootKey, "lib/", "lib/Unit2.pas", "Hello.pas", "../Shared/Common.pas" })
        {
            pm.Select(pm.Find(key)!);
            Assert.True(commands.CanExecute("file.openInTerminal"), key);
            await commands.ExecuteAsync("file.openInTerminal");
        }

        Assert.Equal(
            ["OpenConsole " + Dir, "OpenConsole " + Dir + @"\lib", "OpenConsole " + Dir + @"\lib", "OpenConsole " + Dir, @"OpenConsole C:\p\Shared"],
            shell.Calls);

        // Disabled when the directory does not exist, on the External group, with several rows and without a selection.
        pm.Select(pm.Find("gone/")!);
        Assert.False(commands.CanExecute("file.openInTerminal"));
        pm.Select(pm.Find("gone/Unit3.pas")!);
        Assert.False(commands.CanExecute("file.openInTerminal"));
        pm.Select(pm.Find(ProjectManagerViewModel.ExternalKey)!);
        Assert.False(commands.CanExecute("file.openInTerminal"));
        pm.Select(pm.Find("Hello.pas")!);
        pm.Select(pm.Find("lib/Unit2.pas")!, ProjectSelectMode.Toggle);
        Assert.False(commands.CanExecute("file.openInTerminal"));
        pm.ClearSelection();
        Assert.False(commands.CanExecute("file.openInTerminal"));

        // A failure of the shell: Msg.File.ConsoleFailed with the folder and the OS reason.
        shell.Calls.Clear();
        shell.Succeeds = false;
        dialogs.Answer("Msg.File.ConsoleFailed", "OK");
        pm.Select(pm.Find("lib/")!);
        await commands.ExecuteAsync("file.openInTerminal");
        var request = Assert.Single(dialogs.Requests);
        Assert.Equal("Msg.File.ConsoleFailed", request.MessageId);
        Assert.Equal([Dir + @"\lib", FakeShellService.ConsoleFailure], request.Args);

        // IDE-PM-142: nothing but the shell service was asked; no process is started by the command itself.
        Assert.Equal(["OpenConsole " + Dir + @"\lib"], shell.Calls);
        Assert.Empty(((FakeProcessLauncher)services.Processes).Started);
    }
}
