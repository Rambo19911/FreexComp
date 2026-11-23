using System.Text;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Settings;

/// <summary><see cref="SettingsService"/> saving against a disk that other processes use (I-171).</summary>
[Trait("Phase", "1")]
public sealed class SettingsServiceTests
{
    private const string UserDir = @"C:\u";
    private const string SettingsFile = @"C:\u\roaming\settings.json";
    private static readonly SettingKey<int> Size = SettingKey.Int("t.size", 13, new SettingRange(6, 72));

    private readonly InMemoryFileSystem _disk = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingLog _log = new();

    /// <summary>ERROR_SHARING_VIOLATION: a reader (editor, backup tool, virus scanner) holds the target during the replace.</summary>
    private static IOException SharingViolation() => new("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020));

    private SettingsService Create(RecordingFileSystem fs) =>
        new(UserPaths.Create(UserDir, _ => null, _ => throw new InvalidOperationException()), fs, _clock, _log, [Size]);

    /// <summary>Advances the clock in 10 ms steps until <paramref name="task"/> completes (bounded by the test's timeout, I-205); returns the clock time used.</summary>
    private async Task<TimeSpan> AdvanceUntilAsync(Task task)
    {
        var start = _clock.Now;
        var deadline = CurrentTest.Deadline;
        while (!task.IsCompleted)
        {
            if (deadline.IsExpired)
                throw deadline.Expired("the save to finish");
            _clock.Advance(TimeSpan.FromMilliseconds(10));
            await Task.Yield();
        }

        await task;
        return _clock.Now - start;
    }

    /// <summary>
    /// IDE-SET-029: the initial load reads <c>settings.json</c> on the calling (UI) thread (IDE-SET-020); every later file
    /// access — the save after a change and a flush — runs on the thread pool, never on the thread that changed the value.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-029")]
    [Trait("Req", "IDE-SET-020")]
    public Task FileAccessAfterTheInitialLoadRunsOnTheThreadPool() => TestUiThread.RunAsync(() => FileAccessAfterTheInitialLoadRunsOnTheThreadPoolOnUiThread()); // I-209

    private async Task FileAccessAfterTheInitialLoadRunsOnTheThreadPoolOnUiThread()
    {
        _disk.CreateDirectory(@"C:\u\roaming");
        _disk.WriteAllBytes(SettingsFile, Encoding.UTF8.GetBytes("{ \"formatVersion\": 1, \"t.size\": 12 }"));
        var calls = new System.Collections.Concurrent.ConcurrentQueue<(string Call, int Thread, bool Pool)>();
        var spy = new RecordingFileSystem(_disk)
        {
            FailWith = c =>
            {
                calls.Enqueue((c, Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread));
                return null;
            },
        };
        var settings = Create(spy);

        // A dedicated (non-pool) thread plays the UI thread: load, change, the save delay running out, flush.
        int ui = 0, loaded = 0, loadCalls = 0, afterSet = 0;
        Task flushed = Task.CompletedTask;
        var uiThread = new Thread(() =>
        {
            ui = Environment.CurrentManagedThreadId;
            settings.Load();
            loaded = settings.Get(Size);
            loadCalls = calls.Count;
            settings.Set(Size, 20);
            afterSet = calls.Count;
            _clock.Advance(TimeSpan.FromMilliseconds(500)); // the save timer fires on this thread
            flushed = settings.FlushAsync();
        });
        uiThread.Start();
        uiThread.Join();
        await AdvanceUntilAsync(flushed);

        Assert.Equal((12, 20), (loaded, settings.Get(Size)));
        Assert.True(loadCalls > 0, "the initial load read the file");
        Assert.All(calls.Take(loadCalls), c => Assert.Equal(ui, c.Thread)); // IDE-SET-020: synchronous initial load
        Assert.Equal(loadCalls, afterSet); // Set writes nothing itself
        var saveCalls = calls.Skip(loadCalls).ToList();
        Assert.Contains(saveCalls, c => c.Call.StartsWith("MoveFile", StringComparison.Ordinal));
        Assert.All(saveCalls, c => Assert.True(c.Pool && c.Thread != ui, $"{c.Call} ran on the UI thread"));
        Assert.Contains("\"t.size\": 20", Encoding.UTF8.GetString(_disk.ReadAllBytes(SettingsFile)), StringComparison.Ordinal);
    }

    /// <summary>
    /// A save that meets a sharing violation while replacing <c>settings.json</c> is retried with backoff on
    /// <see cref="Core.Platform.IClock"/> (50, 100, 200, 400 ms) and succeeds without an error in the log; when the file
    /// stays locked through every retry, the existing failure path logs one error (IDE-SET-027).
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-027")]
    public Task SaveRetriesWhileFileIsLocked() => TestUiThread.RunAsync(() => SaveRetriesWhileFileIsLockedOnUiThread()); // I-209

    private async Task SaveRetriesWhileFileIsLockedOnUiThread()
    {
        int locked = 2;
        var spy = new RecordingFileSystem(_disk)
        {
            FailWith = c => c.StartsWith("MoveFile", StringComparison.Ordinal) && Interlocked.Decrement(ref locked) >= 0 ? SharingViolation() : null,
        };
        var settings = Create(spy);
        settings.Load();

        settings.Set(Size, 14);
        var used = await AdvanceUntilAsync(settings.FlushAsync());

        Assert.Equal(3, spy.Calls.Count(c => c.StartsWith("MoveFile", StringComparison.Ordinal)));
        Assert.True(used >= TimeSpan.FromMilliseconds(150), $"retries wait 50 + 100 ms on the clock (used {used.TotalMilliseconds} ms)");
        Assert.Equal("{\n  \"formatVersion\": 1,\n  \"t.size\": 14\n}\n", Encoding.UTF8.GetString(_disk.ReadAllBytes(SettingsFile)));
        Assert.Empty(_log.Lines);

        // Locked for good: four retries, then one logged error; the file keeps the last saved value.
        locked = int.MaxValue;
        settings.Set(Size, 15);
        used = await AdvanceUntilAsync(settings.FlushAsync());
        Assert.Equal(3 + 5, spy.Calls.Count(c => c.StartsWith("MoveFile", StringComparison.Ordinal)));
        Assert.True(used >= TimeSpan.FromMilliseconds(750), $"backoff 50 + 100 + 200 + 400 ms (used {used.TotalMilliseconds} ms)");
        Assert.Single(_log.Lines, l => l.StartsWith("ERROR settings: ", StringComparison.Ordinal));
        Assert.Contains("\"t.size\": 14", Encoding.UTF8.GetString(_disk.ReadAllBytes(SettingsFile)), StringComparison.Ordinal);
    }
}
