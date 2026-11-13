namespace FreexComp.Ide.TestSupport;

/// <summary>
/// A unique folder under <c>%TEMP%\FreexComp.Tests</c> for <c>Integration</c> tests on the real disk (IDE-TEST-023);
/// deleted with its content (attributes cleared first) on dispose.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(string relative) => System.IO.Path.Combine(Path, relative);

    public void Dispose()
    {
        // IDE-TEST-023 asks for retries on sharing violations; they are immediate here because tests never sleep
        // (IDE-TEST-004).
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (!Directory.Exists(Path))
                {
                    return;
                }

                foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
            }
        }
    }
}
