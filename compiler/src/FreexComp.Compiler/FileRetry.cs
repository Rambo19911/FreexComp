namespace FreexComp.Compiler;

/// <summary>
/// C-362: replacing or deleting a file on disk that another process holds for a moment — a program that has just exited (its
/// image is released asynchronously) or a scanner that opened a freshly written file — fails with a sharing violation or
/// access denied. The operation is retried after short, growing waits (<see cref="Delays"/>, 2 s in all); any other error,
/// and the last failure, go to the caller's existing failure path (for an output exe: <c>E4008</c>, CMP-806).
/// </summary>
internal static class FileRetry
{
    public static readonly IReadOnlyList<TimeSpan> Delays =
    [
        TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(650),
    ];

    /// <summary>Access denied, ERROR_SHARING_VIOLATION (32), ERROR_LOCK_VIOLATION (33) or ERROR_USER_MAPPED_FILE (1224).</summary>
    public static bool IsInUse(Exception exception) =>
        exception is UnauthorizedAccessException
        || (exception is IOException { HResult: var result } && (result & 0xFFFF) is 32 or 33 or 1224);

    /// <summary>Runs <paramref name="operation"/>; while it fails because the file is in use, waits and runs it again, at most <see cref="Delays"/> times.</summary>
    public static void Run(Action operation, Action<TimeSpan> wait)
    {
        for (var retry = 0; ; retry++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception e) when (retry < Delays.Count && IsInUse(e))
            {
                wait(Delays[retry]);
            }
        }
    }
}
