using System.Diagnostics;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Services;

/// <summary>
/// INS-021 (I-338): a file <c>portable.json</c> next to the IDE executable moves the user directory to
/// <c>&lt;executable directory&gt;\userdata</c> — unless <c>--user-dir</c> or <c>FREEXCOMP_USER_DIR</c> names one.
/// The executable directory and the file check are injected; the one test that starts the IDE runs a copy of it from a
/// scratch folder, so no marker ever lies next to the built IDE (IDE-TEST-003).
/// </summary>
[Trait("Phase", "8")]
public sealed class PortableModeTests
{
    /// <summary>An install folder with a space and non-ASCII letters (INS-020).</summary>
    private const string FolderName = "Fr\u00EBe \u0108omp dir";
    private const string ExeDir = @"D:\" + FolderName;
    private const string ArgDir = @"C:\fxc-arg";
    private const string EnvDir = @"C:\fxc-env";
    private static readonly string SmokeOutput = "per-monitor-v2" + Environment.NewLine;

    [Fact(Timeout = 30000)]
    [Trait("Req", "INS-021")]
    public void MarkerMovesUserDirectory()
    {
        var folders = new FolderSpy();
        var probes = new List<string>();

        var paths = UserPaths.Create(null, Env(null), folders.Get, ExeDir, Marker(probes, present: true));

        // Both parts of the user directory, the log and the Documents fallback lie under <exe dir>\userdata:
        // the same layout as --user-dir <exe dir>\userdata (IDE-SET-002).
        Assert.Equal(ExeDir + @"\userdata\roaming", paths.RoamingDirectory);
        Assert.Equal(ExeDir + @"\userdata\local", paths.LocalDirectory);
        Assert.Equal(ExeDir + @"\userdata\local\logs", paths.LogDirectory);
        Assert.Equal(ExeDir + @"\userdata\documents", paths.DocumentsDirectory);

        // The per-user folders are not asked for, and the only file looked at is the marker.
        Assert.Empty(folders.Requested);
        Assert.Equal([ExeDir + @"\portable.json"], probes);

        // What the start-up code creates and binds the running instance to.
        Assert.Equal(ExeDir + @"\userdata", UserPaths.PortableRoot(null, Env(null), ExeDir, Marker(probes, present: true)));

        // An empty variable is no variable; the executable directory as the runtime gives it (trailing separator).
        Assert.Equal(ExeDir + @"\userdata", UserPaths.PortableRoot("", Env(""), ExeDir + @"\", Marker(probes, present: true)));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "INS-021")]
    public void NoMarkerNoChange()
    {
        var folders = new FolderSpy();
        var probes = new List<string>();

        var paths = UserPaths.Create(null, Env(null), folders.Get, ExeDir, Marker(probes, present: false));

        Assert.Equal(@"C:\Users\u\AppData\Roaming\FreexComp", paths.RoamingDirectory);
        Assert.Equal(@"C:\Users\u\AppData\Local\FreexComp", paths.LocalDirectory);
        Assert.Equal(@"C:\Users\u\AppData\Local\FreexComp\logs", paths.LogDirectory);
        Assert.Equal(@"C:\Users\u\Documents", paths.DocumentsDirectory);
        Assert.Equal([ExeDir + @"\portable.json"], probes);
        Assert.Null(UserPaths.PortableRoot(null, Env(null), ExeDir, Marker(probes, present: false)));

        // Without the marker the overrides work as before.
        var fromEnv = UserPaths.Create(null, Env(EnvDir), folders.Get, ExeDir, Marker(probes, present: false));
        Assert.Equal(EnvDir + @"\roaming", fromEnv.RoamingDirectory);

        // An unknown executable directory: nothing to look next to.
        probes.Clear();
        Assert.Null(UserPaths.PortableRoot(null, Env(null), null, Marker(probes, present: true)));
        Assert.Empty(probes);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "INS-021")]
    [Trait("Req", "IDE-P1-011")]
    public void UserDirArgumentWinsOverMarker()
    {
        var folders = new FolderSpy();
        var probes = new List<string>();

        var paths = UserPaths.Create(ArgDir, Env(null), folders.Get, ExeDir, Marker(probes, present: true));

        Assert.Equal(ArgDir + @"\roaming", paths.RoamingDirectory);
        Assert.Equal(ArgDir + @"\local", paths.LocalDirectory);
        Assert.Equal(ArgDir + @"\documents", paths.DocumentsDirectory);
        Assert.Null(UserPaths.PortableRoot(ArgDir, Env(null), ExeDir, Marker(probes, present: true)));
        Assert.Empty(probes); // the marker is not even looked for
        Assert.Empty(folders.Requested);
    }

    /// <summary>IDE-P1-011: the variable has the same effect as <c>--user-dir</c>, so it wins over the marker too.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "INS-021")]
    [Trait("Req", "IDE-SET-002")]
    public void EnvironmentVariableWinsOverMarker()
    {
        var folders = new FolderSpy();
        var probes = new List<string>();

        var paths = UserPaths.Create(null, Env(EnvDir), folders.Get, ExeDir, Marker(probes, present: true));

        Assert.Equal(EnvDir + @"\roaming", paths.RoamingDirectory);
        Assert.Equal(EnvDir + @"\local", paths.LocalDirectory);
        Assert.Null(UserPaths.PortableRoot(null, Env(EnvDir), ExeDir, Marker(probes, present: true)));
        Assert.Empty(probes);
        Assert.Empty(folders.Requested);
    }

    /// <summary>
    /// Only the presence of the marker counts: the file is never opened (it is held with an exclusive lock here), so
    /// the documented content (INS-020), an empty object, an empty file, a later format version and text that is no
    /// JSON at all behave alike and none can stop the start-up.
    /// </summary>
    [Theory(Timeout = 30000)]
    [Trait("Category", "Integration")]
    [Trait("Req", "INS-021")]
    [InlineData("{ \"formatVersion\": 1 }")]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("{ \"formatVersion\": 99, \"later\": [true] }")]
    [InlineData("not json {")]
    public void MarkerContentIsNotRead(string content)
    {
        using var temp = new TempDirectory();
        var exeDir = Directory.CreateDirectory(temp.Combine(FolderName)).FullName;
        var marker = Path.Combine(exeDir, UserPaths.PortableMarkerFile);
        File.WriteAllText(marker, content);

        using (new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var paths = UserPaths.Create(null, Env(null), new FolderSpy().Get, exeDir, File.Exists);
            Assert.Equal(Path.Combine(exeDir, "userdata", "roaming"), paths.RoamingDirectory);
            Assert.Equal(Path.Combine(exeDir, "userdata", "local"), paths.LocalDirectory);
        }

        Assert.Equal(content, File.ReadAllText(marker));
        Assert.False(Directory.Exists(Path.Combine(exeDir, "userdata")), "computing the paths creates nothing (IDE-SET-001)");
    }

    /// <summary>A folder of that name is not the marker.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Category", "Integration")]
    [Trait("Req", "INS-021")]
    public void FolderNamedLikeTheMarkerIsNoMarker()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine(UserPaths.PortableMarkerFile));

        Assert.Null(UserPaths.PortableRoot(null, Env(null), temp.Path, File.Exists));
    }

    /// <summary>
    /// The real executable, copied to a scratch folder with a space and non-ASCII letters in its name (only the files of
    /// the output folder itself: the installation check <c>--smoke-dpi</c> loads no native library). Without the marker
    /// the run creates nothing; with the marker and <c>--user-dir</c>, or with <c>FREEXCOMP_USER_DIR</c>, it creates
    /// nothing either; with the marker alone <c>userdata</c> exists afterwards (created on first start) and is empty —
    /// its parts are created on first write (IDE-SET-001).
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Category", "Integration")]
    [Trait("Req", "INS-021")]
    public async Task StartCreatesUserDataNextToTheExecutable()
    {
        using var temp = new TempDirectory();
        var exeDir = Directory.CreateDirectory(temp.Combine(FolderName)).FullName;
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory))
            File.Copy(file, Path.Combine(exeDir, Path.GetFileName(file)));
        var exe = Path.Combine(exeDir, "FreexComp.Ide.exe");
        Assert.True(File.Exists(exe), exe);
        var userData = Path.Combine(exeDir, "userdata");
        var marker = Path.Combine(exeDir, UserPaths.PortableMarkerFile);
        var before = Directory.GetFileSystemEntries(exeDir).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal((SmokeOutput, "", 0), await SmokeAsync(exe, temp.Path, null));
        Assert.Empty(Added()); // no marker: nothing changes

        File.WriteAllText(marker, "{ \"formatVersion\": 1 }");
        Assert.Equal((SmokeOutput, "", 0), await SmokeAsync(exe, temp.Path, null, "--user-dir", temp.Combine("user")));
        Assert.Equal((SmokeOutput, "", 0), await SmokeAsync(exe, temp.Path, temp.Combine("env-user")));
        Assert.Equal([marker], Added()); // --user-dir and the variable win over the marker
        Assert.False(Directory.Exists(temp.Combine("user")));
        Assert.False(Directory.Exists(temp.Combine("env-user")));

        Assert.Equal((SmokeOutput, "", 0), await SmokeAsync(exe, temp.Path, null));
        Assert.Equal([marker, userData], Added());
        Assert.True(Directory.Exists(userData), userData);
        Assert.Empty(Directory.GetFileSystemEntries(userData));

        // A second start finds the folder and leaves it alone.
        Assert.Equal((SmokeOutput, "", 0), await SmokeAsync(exe, temp.Path, null));
        Assert.Equal([marker, userData], Added());
        Assert.Empty(Directory.GetFileSystemEntries(userData));

        List<string> Added() => [.. Directory.GetFileSystemEntries(exeDir).Where(e => !before.Contains(e)).Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Runs <c>&lt;exe&gt; [arguments] --smoke-dpi</c> (no window, UI-TOK-067) and returns what it printed and its exit code.</summary>
    private static async Task<(string Output, string Error, int ExitCode)> SmokeAsync(string exe, string workingDirectory, string? userDirVariable, params string[] arguments)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        start.Environment.Remove(UserPaths.UserDirVariable);
        if (userDirVariable is not null)
            start.Environment[UserPaths.UserDirVariable] = userDirVariable;
        foreach (var argument in (string[])[.. arguments, "--smoke-dpi"])
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = ChildProcessOutput.Read(process); // I-262: dedicated threads, not the pool
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true); // the test's deadline: no IDE is left behind
            throw;
        }

        return (await output.StandardOutput, await output.StandardError, process.ExitCode);
    }

    private static Func<string, string?> Env(string? userDir) =>
        name => name == UserPaths.UserDirVariable ? userDir : null;

    private static Func<string, bool> Marker(List<string> probes, bool present) => path =>
    {
        probes.Add(path);
        return present;
    };

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
