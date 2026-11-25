using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>C-154: an output exe held by another process is <c>E4008</c> (build-engine.md CMP-806).</summary>
[Trait("Phase", "3")]
public sealed class OutputLockedTests
{
    private const string Changed = "program Hello; begin Writeln('changed') end.";

    [Fact]
    [Trait("Req", "CMP-806")]
    public async Task OldExeCannotBeDeletedBeforeTheLink_IsE4008_AndTheLinkerDoesNotRun()
    {
        var w = new BuildWorkspace();
        await w.RunAsync(w.Request());
        w.Fs.Add(BuildWorkspace.MainPath, Changed);
        var locked = new LockedFileSystem(w.Fs, BuildWorkspace.Exe);
        var links = w.Tools.Calls.Count(c => c == "lld-link.exe");

        var (result, log) = await w.RunAsync(w.Request(), fs: locked);

        Assert.False(result.Success);
        Assert.Equal(
            [@"C:\p\Hello.pas(0,0): error E4008: Cannot write output file 'C:\p\bin\Debug\Hello.exe': the file is in use by another process"],
            log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText()));
        Assert.Equal(links, w.Tools.Calls.Count(c => c == "lld-link.exe"));
        Assert.Null(result.OutputPath);
    }

    [Theory]
    [Trait("Req", "CMP-806")]
    [InlineData(@"lld-link: error: failed to write output 'C:\p\bin\Debug\Hello.exe': permission denied")]
    [InlineData(@"lld-link: error: failed to open C:\p\bin\Debug\Hello.exe: The process cannot access the file because it is being used by another process.")]
    [InlineData(@"lld-link: error: cannot open output file C:\p\bin\Debug\Hello.exe: Access is denied.")]
    public async Task LinkerReportsTheOutputInUse_IsE4008_NoPartialExe(string line)
    {
        var w = new BuildWorkspace();
        w.Tools.BeforeTool = (tool, _, _) =>
        {
            if (tool == "lld-link.exe")
            {
                w.Fs.Add(BuildWorkspace.Exe, "partial");
            }

            return Task.CompletedTask;
        };
        w.Tools.LinkFailure = ([line], 1);

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success);
        Assert.Equal(["E4008"], log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.Code));
        Assert.Contains(line, log.Events.OfType<BuildEvent.OutputLine>().Select(o => o.Text));
        Assert.False(w.Fs.FileExists(BuildWorkspace.Exe));
    }

    [Fact]
    [Trait("Req", "CMP-806")]
    public async Task CleanOfALockedExe_IsE4008_NotAnException()
    {
        var w = new BuildWorkspace();
        await w.RunAsync(w.Request());

        var (result, log) = await w.RunAsync(w.Request(BuildTarget.Clean), fs: new LockedFileSystem(w.Fs, BuildWorkspace.Exe));

        Assert.False(result.Success);
        Assert.Equal(["E4008"], log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.Code));
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "CMP-806")]
    public async Task ExeOpenedWithoutSharingOnDisk_IsE4008()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        var llvm = Path.Combine(dir, "llvm");
        Directory.CreateDirectory(llvm);
        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            File.WriteAllText(Path.Combine(llvm, tool), "");
        }

        File.WriteAllText(Path.Combine(dir, "Hello.pproj"), BuildWorkspace.ProjectJson("Hello"));
        File.WriteAllText(Path.Combine(dir, "Hello.pas"), BuildWorkspace.HelloProgram);
        var tools = new FakeToolchain(PhysicalFileSystem.Instance);
        var engine = new BuildEngine(PhysicalFileSystem.Instance, tools);
        var request = new BuildRequest
        {
            ProjectPath = Path.Combine(dir, "Hello.pproj"),
            ToolchainDirectory = llvm,
            RtlDirectory = Path.Combine(RepoPaths.Root, "rtl", "src"),
        };
        var ct = TestContext.Current.CancellationToken;
        var exe = Path.Combine(dir, "bin", "Debug", "Hello.exe");
        try
        {
            Assert.True((await engine.RunAsync(request, new EventLog(), ct)).Success);
            File.WriteAllText(Path.Combine(dir, "Hello.pas"), Changed);
            BuildResult result;
            var log = new EventLog();
            using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                result = await engine.RunAsync(request, log, ct);
            }

            Assert.False(result.Success);
            Assert.Equal(["E4008"], log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.Code));
        }
        finally
        {
            TempDirectories.Delete(dir);
        }
    }

    /// <summary>Deleting <paramref name="lockedPath"/> fails as it does for a running exe.</summary>
    internal sealed class LockedFileSystem(IFileSystem inner, string lockedPath) : IFileSystem
    {
        public bool FileExists(string path) => inner.FileExists(path);

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) => inner.EnumerateFiles(directory, searchPattern);

        public IEnumerable<string> EnumerateDirectories(string directory) => inner.EnumerateDirectories(directory);

        public DateTime GetLastWriteTimeUtc(string path) => inner.GetLastWriteTimeUtc(path);

        public long GetLength(string path) => inner.GetLength(path);

        public void WriteAllBytes(string path, byte[] data) => inner.WriteAllBytes(path, data);

        public void Delete(string path)
        {
            if (string.Equals(path, lockedPath, StringComparison.OrdinalIgnoreCase) && inner.FileExists(path))
            {
                throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");
            }

            inner.Delete(path);
        }

        public void CreateDirectory(string path) => inner.CreateDirectory(path);
    }
}
