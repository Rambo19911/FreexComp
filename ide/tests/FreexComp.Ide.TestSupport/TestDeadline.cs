using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// I-205: the wall time a test has left before its own timeout (<c>[Fact(Timeout = …)]</c>, IDE-TEST-006). A wait on a
/// completion signal is bounded by it and nothing else, so a loaded machine (every test project at once) slows the wait
/// down instead of failing it; a real hang still fails, <see cref="Margin"/> before the test timeout, with a message
/// that names the step. Each test project registers every running test through an assembly-level before/after
/// attribute (<see cref="Begin"/>, <see cref="End"/>) and looks the current one up with <see cref="Of"/>.
/// </summary>
public sealed class TestDeadline
{
    /// <summary>The timeout of a test that declares none (IDE-TEST-006).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// I-222: the timeout, in milliseconds, of a test that runs a debug session over the scripted adapter — a real process,
    /// whose start and answers take as long as the machine's load makes them. A safety net against a hang, never a
    /// budget: nothing in such a test depends on how long a step takes (docs/process/TESTING.md, the CPU-load rule).
    /// </summary>
    public const int DebugTestTimeout = 120_000;

    /// <summary>How long before the test timeout a wait gives up, so that its own message is the one reported.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromSeconds(2);

    private static readonly ConcurrentDictionary<string, TestDeadline> Running = new(StringComparer.Ordinal);

    private readonly Func<TimeSpan> _elapsed;

    /// <summary>
    /// A deadline <paramref name="timeout"/> (0 or less: <see cref="DefaultTimeout"/>) from now for the test whose
    /// cancellation is <paramref name="token"/>; <paramref name="elapsed"/> replaces the wall clock (self-tests).
    /// </summary>
    public TestDeadline(TimeSpan timeout, CancellationToken token = default, Func<TimeSpan>? elapsed = null)
    {
        Timeout = timeout > TimeSpan.Zero ? timeout : DefaultTimeout;
        Token = token;
        if (elapsed is null)
        {
            var watch = Stopwatch.StartNew();
            elapsed = () => watch.Elapsed;
        }

        _elapsed = elapsed;
    }

    /// <summary>The test's cancellation (the test framework cancels it at the timeout); every wait observes it.</summary>
    public CancellationToken Token { get; }

    /// <summary>The test's own timeout.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Wall time since the test started.</summary>
    public TimeSpan Elapsed => _elapsed();

    /// <summary>Wall time left for a wait: the test timeout less <see cref="Margin"/> less <see cref="Elapsed"/>, never negative.</summary>
    public TimeSpan Remaining
    {
        get
        {
            var left = Timeout - Margin - Elapsed;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>True once <see cref="Remaining"/> is used up.</summary>
    public bool IsExpired => Remaining == TimeSpan.Zero;

    /// <summary>Registers the test <paramref name="testId"/> as started now, with its timeout in milliseconds (0: none declared) and its cancellation.</summary>
    public static void Begin(string testId, int timeoutMilliseconds, CancellationToken token) =>
        Running[testId] = new TestDeadline(TimeSpan.FromMilliseconds(timeoutMilliseconds), token);

    /// <summary>Removes the registration of <paramref name="testId"/>.</summary>
    public static void End(string testId) => Running.TryRemove(testId, out _);

    /// <summary>
    /// The deadline of the running test <paramref name="testId"/>. A test that was not registered before it ran (one in an
    /// assembly without the registering attribute, e.g. the end-to-end tests that use the IDE harness) is registered now,
    /// with <paramref name="timeoutMilliseconds"/> from now; no test at all gets <see cref="DefaultTimeout"/> from now.
    /// </summary>
    public static TestDeadline Of(string? testId, int timeoutMilliseconds = 0, CancellationToken token = default) =>
        testId is null
            ? new TestDeadline(DefaultTimeout, token)
            : Running.GetOrAdd(testId, _ => new TestDeadline(TimeSpan.FromMilliseconds(timeoutMilliseconds), token));

    /// <summary>The failure of a wait for <paramref name="step"/> that used up the test's time.</summary>
    public TimeoutException Expired(string step) => new(string.Create(CultureInfo.InvariantCulture,
        $"Timed out waiting for: {step} ({Elapsed.TotalSeconds:0.0} s into the test, timeout {Timeout.TotalSeconds:0} s)"));

    /// <summary>Awaits <paramref name="task"/> until the deadline; on expiry throws <see cref="Expired"/> for <paramref name="step"/>.</summary>
    public async Task WaitAsync(Task task, string step)
    {
        ArgumentNullException.ThrowIfNull(task);
        try
        {
            await task.WaitAsync(Remaining, Token).ConfigureAwait(false);
        }
        catch (TimeoutException) when (!task.IsCompleted)
        {
            throw Expired(step);
        }
    }

    /// <summary>Awaits <paramref name="task"/> until the deadline; on expiry throws <see cref="Expired"/> for <paramref name="step"/>.</summary>
    public async Task<T> WaitAsync<T>(Task<T> task, string step)
    {
        ArgumentNullException.ThrowIfNull(task);
        try
        {
            return await task.WaitAsync(Remaining, Token).ConfigureAwait(false);
        }
        catch (TimeoutException) when (!task.IsCompleted)
        {
            throw Expired(step);
        }
    }

    /// <summary>Waits for <paramref name="semaphore"/> until the deadline; on expiry throws <see cref="Expired"/> for <paramref name="step"/>.</summary>
    public async Task WaitAsync(SemaphoreSlim semaphore, string step)
    {
        ArgumentNullException.ThrowIfNull(semaphore);
        if (!await semaphore.WaitAsync(Remaining, Token).ConfigureAwait(false))
            throw Expired(step);
    }

    /// <summary>Blocks on <paramref name="semaphore"/> until the deadline (a background thread holding a gate); on expiry throws <see cref="Expired"/>.</summary>
    public void Wait(SemaphoreSlim semaphore, string step)
    {
        ArgumentNullException.ThrowIfNull(semaphore);
        if (!semaphore.Wait(Remaining, Token))
            throw Expired(step);
    }

    /// <summary>Blocks on <paramref name="gate"/> until the deadline (a background thread holding a gate); on expiry throws <see cref="Expired"/>.</summary>
    public void Wait(ManualResetEventSlim gate, string step)
    {
        ArgumentNullException.ThrowIfNull(gate);
        if (!gate.Wait(Remaining, Token))
            throw Expired(step);
    }
}
