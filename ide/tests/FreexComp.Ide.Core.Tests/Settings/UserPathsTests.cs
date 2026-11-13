using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Tests.Settings;

[Trait("Phase", "1")]
public sealed class UserPathsTests
{
    private const string EnvDir = @"C:\fxc-env";
    private const string ArgDir = @"C:\fxc-arg";

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-002")]
    [Trait("Req", "IDE-P1-011")]
    public void OverrideRedirectsBothDirectories()
    {
        var folders = new FolderSpy();

        // Environment variable only.
        var fromEnv = UserPaths.Create(null, Env(EnvDir), folders.Get);
        Assert.Equal(@"C:\fxc-env\roaming", fromEnv.RoamingDirectory);
        Assert.Equal(@"C:\fxc-env\local", fromEnv.LocalDirectory);

        // --user-dir takes precedence over the environment variable.
        var fromArg = UserPaths.Create(ArgDir, Env(EnvDir), folders.Get);
        Assert.Equal(@"C:\fxc-arg\roaming", fromArg.RoamingDirectory);
        Assert.Equal(@"C:\fxc-arg\local", fromArg.LocalDirectory);

        // --user-dir alone.
        var argOnly = UserPaths.Create(ArgDir, Env(null), folders.Get);
        Assert.Equal(@"C:\fxc-arg\roaming", argOnly.RoamingDirectory);
        Assert.Equal(@"C:\fxc-arg\local", argOnly.LocalDirectory);

        // The Documents folder (file-picker fallback, UI-DLG-051) is redirected too.
        Assert.Equal(@"C:\fxc-arg\documents", argOnly.DocumentsDirectory);

        // The real user folders are never asked for when overridden.
        Assert.Empty(folders.Requested);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-001")]
    public void DefaultsToAppDataFolders()
    {
        var folders = new FolderSpy();

        var paths = UserPaths.Create(null, Env(""), folders.Get);

        Assert.Equal(@"C:\Users\u\AppData\Roaming\FreexComp", paths.RoamingDirectory);
        Assert.Equal(@"C:\Users\u\AppData\Local\FreexComp", paths.LocalDirectory);
        Assert.Equal(
            [Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData],
            folders.Requested.Order());

        // The Documents folder is asked for only when it is needed.
        Assert.Equal(@"C:\Users\u\Documents", paths.DocumentsDirectory);
        Assert.Equal(Environment.SpecialFolder.MyDocuments, folders.Requested[^1]);
    }

    [Fact(Timeout = 30000)]
    public void LogDirectoryIsUnderLocal()
    {
        var paths = UserPaths.Create(ArgDir, Env(null), new FolderSpy().Get);

        Assert.Equal(@"C:\fxc-arg\local\logs", paths.LogDirectory);
    }

    [Fact(Timeout = 30000)]
    public void RelativeOverrideIsMadeAbsolute()
    {
        var paths = UserPaths.Create("rel", Env(null), new FolderSpy().Get);

        Assert.Equal(Path.Combine(Path.GetFullPath("rel"), "roaming"), paths.RoamingDirectory);
    }

    private static Func<string, string?> Env(string? userDir) =>
        name => name == UserPaths.UserDirVariable ? userDir : null;

    private sealed class FolderSpy
    {
        public List<Environment.SpecialFolder> Requested { get; } = [];

        public string Get(Environment.SpecialFolder folder)
        {
            Requested.Add(folder);
            return folder switch
            {
                Environment.SpecialFolder.ApplicationData => @"C:\Users\u\AppData\Roaming",
                Environment.SpecialFolder.LocalApplicationData => @"C:\Users\u\AppData\Local",
                Environment.SpecialFolder.MyDocuments => @"C:\Users\u\Documents",
                _ => throw new InvalidOperationException(folder.ToString()),
            };
        }
    }
}
