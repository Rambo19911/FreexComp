using System.Diagnostics;
using System.Text;
using FreexComp.Ide.Core.Settings;
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

    /// <summary>Advances the clock in 10 ms steps until <paramref name="task"/> completes; returns the clock time used.</summary>
    private async Task<TimeSpan> AdvanceUntilAsync(Task task)
    {
        var start = _clock.Now;
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "save did not finish");
            _clock.Advance(TimeSpan.FromMilliseconds(10));
            await Task.Yield();
        }

        await task;
        return _clock.Now - start;
    }

    /// <summary>
    /// A save that meets a sharing violation while replacing <c>settings.json</c> is retried with backoff on
    /// <see cref="Core.Platform.IClock"/> (50, 100, 200, 400 ms) and succeeds without an error in the log; when the file
    /// stays locked through every retry, the existing failure path logs one error (IDE-SET-027).
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-027")]
    public async Task SaveRetriesWhileFileIsLocked()
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
