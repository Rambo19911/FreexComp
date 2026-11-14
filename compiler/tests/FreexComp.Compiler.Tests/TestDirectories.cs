namespace FreexComp.Compiler.Tests;

/// <summary>
/// C-215: removal of a test's temporary folder. An executable the test just ran can stay locked for a moment after its process
/// has exited (the image section is released asynchronously, and scanners may open new files), so a sharing violation is
/// retried a bounded number of times with growing waits (about 6 s in all); a folder that still cannot be removed then fails
/// the test as before.
/// </summary>
internal static class TestDirectories
{
    private const int Attempts = 12;

    public static void Delete(string directory)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < Attempts)
            {
                // a handle on a just-exited image or a freshly written file: wait for its release, then try again
                Thread.Sleep(10 * attempt * attempt);
            }
        }
    }
}
