namespace FreexComp.Build.Tests;

/// <summary>
/// C-292: removal of a test's temporary folder. An executable the test just ran (or a file it just wrote) can stay open for a
/// moment after its process has exited — the image section is released asynchronously and scanners open new files — so a
/// sharing or access error is retried a few times with short, growing waits (under a second in all). A folder that still cannot
/// be removed is left behind: cleaning up is not what a test checks, so it never fails one.
/// </summary>
internal static class TempDirectories
{
    /// <returns>Whether the folder is gone.</returns>
    public static bool Delete(string directory) => Delete(directory, attempts: 6, wait: attempt => Thread.Sleep(50 * attempt));

    /// <param name="attempts">How often the removal is tried in all.</param>
    /// <param name="wait">Called with the number of the failed attempt before the next one.</param>
    internal static bool Delete(string directory, int attempts, Action<int> wait)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= attempts)
                {
                    return false;                                        // given up: the folder stays in the temp directory
                }

                wait(attempt);
            }
        }
    }
}
