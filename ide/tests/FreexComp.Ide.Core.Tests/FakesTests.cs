using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests;

[Trait("Phase", "1")]
public sealed class FakesTests
{
    /// <summary>The same contract runs against the fake and the real disk (testing.md §5.1).</summary>
    [Theory(Timeout = 30000)]
    [InlineData("memory")]
    [InlineData("disk")]
    [Trait("Category", "Integration")]
    [Trait("Req", "IDE-ARCH-012")]
    public void InMemoryFileSystemBehavesLikeDisk(string kind)
    {
        using var target = FileSystemUnderTest.Create(kind);
        var fs = target.FileSystem;
        var root = target.Root;
        var sub = Path.Combine(root, "sub");
        var a = Path.Combine(sub, "a.txt");
        var b = Path.Combine(sub, "b.pas");
        var c = Path.Combine(sub, "c.txt");

        Assert.True(fs.DirectoryExists(root));
        Assert.False(fs.FileExists(a));
        Assert.False(fs.DirectoryExists(sub));

        // Writing needs an existing parent directory.
        Assert.Throws<DirectoryNotFoundException>(() => fs.WriteAllBytes(a, [1]));

        fs.CreateDirectory(Path.Combine(sub, "deep"));
        Assert.Throws<IOException>(() => fs.DeleteDirectory(sub)); // not empty
        fs.DeleteDirectory(Path.Combine(sub, "deep"));
        Assert.False(fs.DirectoryExists(Path.Combine(sub, "deep")));
        fs.DeleteDirectory(Path.Combine(sub, "deep")); // missing: no-op
        fs.CreateDirectory(Path.Combine(sub, "deep"));
        fs.CreateDirectory(sub); // existing: no-op
        Assert.True(fs.DirectoryExists(sub));
        Assert.False(fs.FileExists(sub));

        fs.WriteAllBytes(a, [1, 2, 3, 4]);
        fs.WriteAllBytes(a, [1, 2, 3]); // overwrite truncates
        Assert.Equal([1, 2, 3], fs.ReadAllBytes(a));
        Assert.Equal(3, fs.GetLength(a));
        Assert.True(fs.FileExists(a.ToUpperInvariant()));  // Windows: case-insensitive
        Assert.False(fs.DirectoryExists(a));
        Assert.NotEqual(default, fs.GetLastWriteTimeUtc(a));

        // The durable write (flushed to disk before it returns) has the same semantics.
        var durable = Path.Combine(sub, "durable.txt");
        Assert.Throws<DirectoryNotFoundException>(() => fs.WriteAllBytesDurable(Path.Combine(root, "nope", "x.txt"), [1]));
        fs.WriteAllBytesDurable(durable, [5, 6, 7]);
        fs.WriteAllBytesDurable(durable, [5, 6]);
        Assert.Equal([5, 6], fs.ReadAllBytes(durable));
        target.SetReadOnly(durable, true);
        Assert.Throws<UnauthorizedAccessException>(() => fs.WriteAllBytesDurable(durable, [0]));
        target.SetReadOnly(durable, false);
        fs.DeleteFile(durable);

        Assert.Throws<FileNotFoundException>(() => fs.ReadAllBytes(Path.Combine(sub, "missing.txt")));
        Assert.Throws<FileNotFoundException>(() => fs.GetLength(Path.Combine(sub, "missing.txt")));
        Assert.Throws<FileNotFoundException>(() => fs.GetLastWriteTimeUtc(Path.Combine(sub, "missing.txt")));

        fs.WriteAllBytes(b, [9]);
        Assert.Equal([a], fs.EnumerateFiles(sub, "*.txt"));
        Assert.Equal([a, b], fs.EnumerateFiles(sub, "*").Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal([Path.Combine(sub, "deep")], fs.EnumerateDirectories(sub));
        Assert.Throws<DirectoryNotFoundException>(() => fs.EnumerateFiles(Path.Combine(root, "nope"), "*"));

        fs.MoveFile(a, c, overwrite: false);
        Assert.False(fs.FileExists(a));
        Assert.Equal([1, 2, 3], fs.ReadAllBytes(c));
        Assert.Throws<IOException>(() => fs.MoveFile(b, c, overwrite: false));
        fs.MoveFile(b, c, overwrite: true);
        Assert.False(fs.FileExists(b));
        Assert.Equal([9], fs.ReadAllBytes(c));
        Assert.Throws<FileNotFoundException>(() => fs.MoveFile(b, a, overwrite: true));

        // Replace: the destination must exist and takes the source's content; the source is gone.
        fs.WriteAllBytes(b, [7, 7]);
        Assert.Throws<FileNotFoundException>(() => fs.ReplaceFile(b, a));
        fs.ReplaceFile(b, c);
        Assert.False(fs.FileExists(b));
        Assert.Equal([7, 7], fs.ReadAllBytes(c));

        // Overwrite in place: the file must exist; it is truncated first.
        Assert.Throws<FileNotFoundException>(() => fs.OverwriteFile(a, [1]));
        fs.OverwriteFile(c, [9]);
        Assert.Equal([9], fs.ReadAllBytes(c));

        Assert.False(fs.IsReadOnly(c));
        target.SetReadOnly(c, true);
        Assert.True(fs.IsReadOnly(c));
        Assert.Throws<UnauthorizedAccessException>(() => fs.WriteAllBytes(c, [0]));
        Assert.Throws<UnauthorizedAccessException>(() => fs.OverwriteFile(c, [0]));
        fs.WriteAllBytes(b, [5]);
        Assert.Throws<UnauthorizedAccessException>(() => fs.ReplaceFile(b, c));
        fs.DeleteFile(b);
        Assert.Equal([9], fs.ReadAllBytes(c));
        target.SetReadOnly(c, false);

        // The attribute through the interface, and the open-for-write probe (IDE-P1-040).
        Assert.True(fs.CanOpenForWrite(c));
        fs.SetReadOnly(c, true);
        Assert.True(fs.IsReadOnly(c));
        Assert.False(fs.CanOpenForWrite(c));
        fs.SetReadOnly(c, false);
        Assert.False(fs.IsReadOnly(c));
        Assert.True(fs.CanOpenForWrite(c));
        Assert.Equal([9], fs.ReadAllBytes(c));             // the probe writes nothing
        Assert.False(fs.CanOpenForWrite(Path.Combine(sub, "missing.txt")));
        Assert.Throws<FileNotFoundException>(() => fs.SetReadOnly(Path.Combine(sub, "missing.txt"), true));

        fs.DeleteFile(c);
        Assert.False(fs.FileExists(c));
        fs.DeleteFile(c); // missing: no-op
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-054")]
    [Trait("Req", "UI-DLG-027")]
    public Task FakeDialogServiceAnswersScriptedRequestsAndFailsOthers() => TestUiThread.RunAsync(() => FakeDialogServiceAnswersScriptedRequestsAndFailsOthersOnUiThread()); // I-209

    private async Task FakeDialogServiceAnswersScriptedRequestsAndFailsOthersOnUiThread()
    {
        IDialogService dialogs = new FakeDialogService();
        var fake = (FakeDialogService)dialogs;
        fake.Answer("Msg.File.SaveChangesOne", "DontSave");
        Assert.Equal("DontSave", await dialogs.ShowMessageAsync(new MessageRequest("Msg.File.SaveChangesOne", ["a.pas"])));
        await Assert.ThrowsAsync<InvalidOperationException>(() => dialogs.ShowMessageAsync(new MessageRequest("Msg.File.RevertConfirm", ["a.pas"])));
        Assert.Equal(["Msg.File.SaveChangesOne", "Msg.File.RevertConfirm"], fake.Requests.Select(r => r.MessageId));
    }

    [Fact(Timeout = 30000)]
    public void ManualClockFiresDueCallbacksInOrder()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
        var fired = new List<string>();
        clock.Schedule(TimeSpan.FromSeconds(5), () => fired.Add("5s"));
        clock.Schedule(TimeSpan.FromSeconds(1), () =>
        {
            fired.Add("1s");
            clock.Schedule(TimeSpan.FromSeconds(1), () => fired.Add("2s"));
        });
        var cancelled = clock.Schedule(TimeSpan.FromSeconds(3), () => fired.Add("3s"));
        cancelled.Dispose();

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(["1s", "2s"], fired);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 12, 0, 4, TimeSpan.Zero), clock.Now);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["1s", "2s", "5s"], fired);
    }

    [Fact(Timeout = 30000)]
    public void ManualFileWatcherDeliversToWatchersOfTheDirectory()
    {
        var factory = new ManualFileWatcherFactory();
        using var w1 = factory.Watch(@"C:\p");
        var w2 = factory.Watch(@"C:\q");
        var seen = new List<FileChange>();
        w1.Changed += (_, e) => seen.Add(e);
        w2.Changed += (_, e) => seen.Add(e);
        Exception? error = null;
        w1.Error += (_, e) => error = e;

        factory.Raise(new FileChange(FileChangeKind.Changed, @"C:\P\a.pas"));
        w2.Dispose();
        factory.Raise(new FileChange(FileChangeKind.Deleted, @"C:\q\b.pas"));
        factory.RaiseError(@"C:\p", new InternalBufferOverflowException());

        Assert.Equal([new FileChange(FileChangeKind.Changed, @"C:\P\a.pas")], seen);
        Assert.IsType<InternalBufferOverflowException>(error);
        Assert.Equal([@"C:\p"], factory.ActiveDirectories);
    }

    [Fact(Timeout = 30000)]
    public void ImmediateDispatcherRunsInline()
    {
        var dispatcher = new ImmediateDispatcher();
        var ran = 0;

        dispatcher.Post(() => ran++);
        dispatcher.Post(() => ran++, UiPriority.Background);

        Assert.Equal(2, ran);
        Assert.True(dispatcher.CheckAccess());
    }

    [Fact(Timeout = 30000)]
    public Task FakeClipboardHoldsText() => TestUiThread.RunAsync(() => FakeClipboardHoldsTextOnUiThread()); // I-209

    private async Task FakeClipboardHoldsTextOnUiThread()
    {
        var clipboard = new FakeClipboard();
        Assert.Null(await clipboard.GetTextAsync());

        await clipboard.SetTextAsync("x := 1;");

        Assert.Equal("x := 1;", await clipboard.GetTextAsync());
    }

    [Fact(Timeout = 30000)]
    public void FakeShellRecordsCallsAndRecyclesFromFileSystem()
    {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(@"C:\p");
        fs.WriteAllBytes(@"C:\p\a.pas", [1]);
        var shell = new FakeShellService(fs);

        Assert.True(shell.OpenFolder(@"C:\p"));
        Assert.True(shell.RevealFile(@"C:\p\a.pas"));
        Assert.True(shell.MoveToRecycleBin(@"C:\p\a.pas"));

        Assert.False(fs.FileExists(@"C:\p\a.pas"));
        Assert.Equal(
            ["OpenFolder C:\\p", "RevealFile C:\\p\\a.pas", "MoveToRecycleBin C:\\p\\a.pas"],
            shell.Calls);
    }

    private sealed class FileSystemUnderTest : IDisposable
    {
        private readonly string? _tempDir;

        private FileSystemUnderTest(IFileSystem fs, string root, string? tempDir)
        {
            FileSystem = fs;
            Root = root;
            _tempDir = tempDir;
        }

        public IFileSystem FileSystem { get; }
        public string Root { get; }

        public static FileSystemUnderTest Create(string kind)
        {
            if (kind == "memory")
            {
                var memory = new InMemoryFileSystem();
                memory.CreateDirectory(@"C:\mem");
                return new FileSystemUnderTest(memory, @"C:\mem", null);
            }

            var temp = Path.Combine(Path.GetTempPath(), "fxc-fs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            return new FileSystemUnderTest(new PhysicalFileSystem(), temp, temp);
        }

        public void SetReadOnly(string path, bool readOnly)
        {
            if (FileSystem is InMemoryFileSystem memory)
            {
                memory.SetReadOnly(path, readOnly);
                return;
            }

            var attributes = File.GetAttributes(path);
            File.SetAttributes(path, readOnly ? attributes | FileAttributes.ReadOnly : attributes & ~FileAttributes.ReadOnly);
        }

        public void Dispose()
        {
            if (_tempDir is null || !Directory.Exists(_tempDir))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
