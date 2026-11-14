using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Services;

[Trait("Phase", "1")]
public sealed class ShellCommandsTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-093")]
    public async Task OpenFolderCommandsCreateTheFolderFirst()
    {
        var shell = new FakeShellService();
        var services = TestComposition.Build(o => o.Shell = shell);
        var logs = services.UserPaths.LogDirectory;
        var roaming = services.UserPaths.RoamingDirectory;
        Assert.False(services.FileSystem.DirectoryExists(logs)); // the recording log writes no file

        await services.Commands.ExecuteAsync("tools.openLogFolder");
        Assert.True(services.FileSystem.DirectoryExists(logs));
        await services.Commands.ExecuteAsync("tools.openSettingsFolder");
        Assert.True(services.FileSystem.DirectoryExists(roaming));

        Assert.Equal([$"OpenFolder {logs}", $"OpenFolder {roaming}"], shell.Calls);
    }
}
