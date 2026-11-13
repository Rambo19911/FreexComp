using System.Globalization;
using System.Text;

namespace FreexComp.Ide.Core.Logging;

/// <summary>
/// Daily log file <c>ide-&lt;yyyyMMdd&gt;.log</c> in the log directory (settings.md §8, IDE-SET-090…092).
/// Entries are formatted on the calling thread and written by a thread-pool worker.
/// </summary>
public sealed class FileLog : ILog, IDisposable
{
    public const string LevelVariable = "FREEXCOMP_LOG";
    public const long DefaultMaxFileBytes = 20L * 1024 * 1024;
    private const int RetentionDays = 14;
    private const string Prefix = "ide-";
    private const string Extension = ".log";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;
    private readonly Func<DateTime> _now;
    private readonly LogLevel _minimumLevel;
    private readonly long _maxFileBytes;
    private readonly object _gate = new();
    private readonly Queue<Entry> _pending = new();
    private readonly Dictionary<string, long> _sizes = new(StringComparer.OrdinalIgnoreCase);
    private bool _draining;
    private bool _disposed;

    /// <param name="directory">The log directory (<c>IUserPaths.LogDirectory</c>); created on first write.</param>
    /// <param name="localNow">Local wall-clock time; decides the file name and the time stamp.</param>
    public FileLog(string directory, Func<DateTime> localNow, LogLevel minimumLevel, long maxFileBytes = DefaultMaxFileBytes)
    {
        _directory = directory;
        _now = localNow;
        _minimumLevel = minimumLevel;
        _maxFileBytes = maxFileBytes;
        DeleteExpiredFiles();
    }

    /// <summary><c>FREEXCOMP_LOG=debug</c> enables DEBUG; anything else means INFO (IDE-SET-091).</summary>
    public static LogLevel MinimumLevelFrom(string? environmentValue) =>
        string.Equals(environmentValue, "debug", StringComparison.OrdinalIgnoreCase) ? LogLevel.Debug : LogLevel.Info;

    public bool IsEnabled(LogLevel level) => level >= _minimumLevel;

    public void Write(LogLevel level, string category, string message, Exception? exception = null)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        try
        {
            var now = _now();
            var text = Format(now, level, category, message, exception);
            var path = Path.Combine(_directory, Prefix + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + Extension);

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _pending.Enqueue(new Entry(path, text, level == LogLevel.Error));
                if (!_draining)
                {
                    _draining = true;
                    ThreadPool.QueueUserWorkItem(_ => Drain());
                }
            }
        }
        catch (Exception)
        {
            // IDE-SET-092: logging never throws.
        }
    }

    /// <summary>Returns when every entry written so far has reached the file (or was dropped).</summary>
    public void Flush()
    {
        lock (_gate)
        {
            while (_draining)
            {
                Monitor.Wait(_gate);
            }
        }
    }

    public void Dispose()
    {
        Flush();
        lock (_gate)
        {
            _disposed = true;
        }
    }

    private static string Format(DateTime now, LogLevel level, string category, string message, Exception? exception)
    {
        var sb = new StringBuilder();
        sb.Append(now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
          .Append(' ').Append(LevelText(level))
          .Append(' ').Append(category).Append(": ").Append(message).Append('\n');

        if (exception is not null)
        {
            foreach (var line in exception.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                sb.Append("    ").Append(line).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN",
        _ => "ERROR",
    };

    private void Drain()
    {
        while (true)
        {
            List<Entry> batch;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _draining = false;
                    Monitor.PulseAll(_gate);
                    return;
                }

                batch = [.. _pending];
                _pending.Clear();
            }

            foreach (var entry in batch)
            {
                TryAppend(entry);
            }
        }
    }

    private void TryAppend(Entry entry)
    {
        try
        {
            if (!_sizes.TryGetValue(entry.Path, out var size))
            {
                size = File.Exists(entry.Path) ? new FileInfo(entry.Path).Length : 0;
            }

            if (size >= _maxFileBytes && !entry.IsError)
            {
                _sizes[entry.Path] = size;
                return;
            }

            var bytes = Utf8NoBom.GetBytes(entry.Text);
            Directory.CreateDirectory(_directory);
            using (var stream = new FileStream(entry.Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                stream.Write(bytes);
            }

            _sizes[entry.Path] = size + bytes.Length;
        }
        catch (Exception)
        {
            // IDE-SET-092: a failed write loses the entry; logging never throws.
        }
    }

    private void DeleteExpiredFiles()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            var today = _now().Date;
            foreach (var file in Directory.EnumerateFiles(_directory, Prefix + "*" + Extension))
            {
                var stamp = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
                if (DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && (today - date).Days > RetentionDays)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }
        catch (Exception)
        {
            // Never throws.
        }
    }

    private sealed record Entry(string Path, string Text, bool IsError);
}
