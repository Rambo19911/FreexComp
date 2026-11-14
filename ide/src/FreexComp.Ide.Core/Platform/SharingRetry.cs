namespace FreexComp.Ide.Core.Platform;

/// <summary>
/// I-171: a user file (settings, keymap) replaced while another process reads it fails with a sharing or lock violation
/// for a short moment. The write is retried with backoff on <see cref="IClock"/> (50, 100, 200, 400 ms); other errors and
/// the last failure go to the caller's existing failure path.
/// </summary>
internal static class SharingRetry
{
    public static readonly IReadOnlyList<TimeSpan> Delays =
        [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400)];

    /// <summary>ERROR_SHARING_VIOLATION (32) or ERROR_LOCK_VIOLATION (33).</summary>
    public static bool IsSharingViolation(Exception exception) =>
        exception is IOException { HResult: var result } && (result & 0xFFFF) is 32 or 33;

    /// <summary>Runs <paramref name="attempt"/>; on a sharing violation waits and runs it again, at most <see cref="Delays"/> times.</summary>
    public static async Task RunAsync(IClock clock, Action attempt)
    {
        for (int retry = 0; ; retry++)
        {
            try
            {
                attempt();
                return;
            }
            catch (Exception ex) when (retry < Delays.Count && IsSharingViolation(ex))
            {
                var waited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                clock.Schedule(Delays[retry], () => waited.TrySetResult());
                await waited.Task.ConfigureAwait(false);
            }
        }
    }
}
