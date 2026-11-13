namespace FreexComp.Ide.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>
/// The IDE's only logging facility (IDE-ARCH-090). Implementations never throw and never block the caller
/// on I/O (IDE-SET-092). May be called from any thread.
/// </summary>
public interface ILog
{
    bool IsEnabled(LogLevel level);

    void Write(LogLevel level, string category, string message, Exception? exception = null);
}

public static class LogExtensions
{
    public static void Debug(this ILog log, string category, string message) =>
        log.Write(LogLevel.Debug, category, message);

    public static void Info(this ILog log, string category, string message) =>
        log.Write(LogLevel.Info, category, message);

    public static void Warning(this ILog log, string category, string message, Exception? exception = null) =>
        log.Write(LogLevel.Warning, category, message, exception);

    public static void Error(this ILog log, string category, string message, Exception? exception = null) =>
        log.Write(LogLevel.Error, category, message, exception);
}
