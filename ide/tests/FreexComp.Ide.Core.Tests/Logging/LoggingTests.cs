using System.Text;
using FreexComp.Ide.Core.Logging;

namespace FreexComp.Ide.Core.Tests.Logging;

[Trait("Phase", "1")]
[Trait("Category", "Integration")]
public sealed class LoggingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fxc-log-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 13, 9, 5, 7, 42, DateTimeKind.Local);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-090")]
    public void WritesDailyLogFile()
    {
        using (var log = new FileLog(_dir, () => _now, LogLevel.Info))
        {
            log.Info("startup", "IDE started");
            log.Warning("settings", "invalid value");
            log.Debug("noise", "not written at INFO");
            _now = new DateTime(2026, 11, 14, 0, 0, 1, 5, DateTimeKind.Local);
            log.Error("app", "boom");
        }

        var day1 = Path.Combine(_dir, "ide-20261113.log");
        var day2 = Path.Combine(_dir, "ide-20261114.log");
        Assert.Equal(
            "09:05:07.042 INFO startup: IDE started\n09:05:07.042 WARN settings: invalid value\n",
            File.ReadAllText(day1));
        Assert.Equal("00:00:01.005 ERROR app: boom\n", File.ReadAllText(day2));

        var bytes = File.ReadAllBytes(day1);
        Assert.NotEqual(0xEF, bytes[0]); // UTF-8 without BOM
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-090")]
    public void AppendsAndIndentsExceptionLines()
    {
        using (var log = new FileLog(_dir, () => _now, LogLevel.Info))
        {
            log.Info("a", "first");
        }

        using (var log = new FileLog(_dir, () => _now, LogLevel.Debug))
        {
            log.Debug("a", "second");
            log.Error("a", "failed", new InvalidOperationException("bad state"));
        }

        var lines = File.ReadAllText(Path.Combine(_dir, "ide-20261113.log")).Split('\n');
        Assert.Equal("09:05:07.042 INFO a: first", lines[0]);
        Assert.Equal("09:05:07.042 DEBUG a: second", lines[1]);
        Assert.Equal("09:05:07.042 ERROR a: failed", lines[2]);
        Assert.StartsWith("    System.InvalidOperationException: bad state", lines[3]);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-091")]
    public void DebugLevelComesFromEnvironmentValue()
    {
        Assert.Equal(LogLevel.Debug, FileLog.MinimumLevelFrom("debug"));
        Assert.Equal(LogLevel.Debug, FileLog.MinimumLevelFrom("DEBUG"));
        Assert.Equal(LogLevel.Info, FileLog.MinimumLevelFrom(null));
        Assert.Equal(LogLevel.Info, FileLog.MinimumLevelFrom("verbose"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-091")]
    public void DeletesFilesOlderThan14Days()
    {
        Directory.CreateDirectory(_dir);
        var old = Path.Combine(_dir, "ide-20261029.log");    // 15 days before 2026-11-13
        var kept = Path.Combine(_dir, "ide-20261030.log");   // 14 days before
        var foreign = Path.Combine(_dir, "notes.txt");
        foreach (var f in new[] { old, kept, foreign })
        {
            File.WriteAllText(f, "x");
        }

        using (new FileLog(_dir, () => _now, LogLevel.Info))
        {
        }

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(kept));
        Assert.True(File.Exists(foreign));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-091")]
    public void OverCapOnlyErrorsAreWritten()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "ide-20261113.log");
        File.WriteAllBytes(file, new byte[16]);

        using (var log = new FileLog(_dir, () => _now, LogLevel.Info, maxFileBytes: 16))
        {
            log.Info("a", "dropped");
            log.Error("a", "kept");
        }

        Assert.EndsWith("09:05:07.042 ERROR a: kept\n", Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        Assert.DoesNotContain("dropped", Encoding.UTF8.GetString(File.ReadAllBytes(file)));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-092")]
    public void UnwritableDirectoryNeverThrows()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "file-not-dir");
        File.WriteAllText(blocker, "x");

        var thrown = Record.Exception(() =>
        {
            using var log = new FileLog(blocker, () => _now, LogLevel.Info);
            log.Error("a", "lost");
            log.Flush(); // the background writer finished: the failed write did not stop it
            log.Info("a", "also lost");
            log.Flush();
        });

        Assert.Null(thrown);
        // The entries are dropped: the blocking file is untouched and nothing was written anywhere else.
        Assert.Equal("x", File.ReadAllText(blocker));
        Assert.Equal([blocker], Directory.GetFileSystemEntries(_dir));
    }
}
