using System.Runtime.InteropServices;

namespace FreexComp.Ide.Platform.Windows;

/// <summary>
/// UI-TOK-067: the DPI awareness of the calling thread as the operating system reports it. The IDE's comes from its
/// application manifest (<c>app.manifest</c>, UI-TOK-066: per-monitor-v2), so it holds from the first instruction of the
/// process; the value is written to the log at start-up and printed by <c>--smoke-dpi</c>.
/// </summary>
internal static class DpiAwareness
{
    public const string PerMonitorV2 = "per-monitor-v2";

    /// <summary>The awareness contexts the system knows (its pseudo-handles −1 … −5), most capable first.</summary>
    private static readonly (IntPtr Context, string Name)[] Known =
    [
        (-4, PerMonitorV2),
        (-3, "per-monitor"),
        (-2, "system"),
        (-1, "unaware"),
        (-5, "unaware-gdi-scaled"),
    ];

    /// <summary>
    /// <c>per-monitor-v2</c>, <c>per-monitor</c>, <c>system</c>, <c>unaware</c>, <c>unaware-gdi-scaled</c>; <c>unknown</c>
    /// for a context this list does not have.
    /// </summary>
    public static string Current
    {
        get
        {
            var context = GetThreadDpiAwarenessContext();
            foreach (var (known, name) in Known)
            {
                if (AreDpiAwarenessContextsEqual(context, known) != 0)
                    return name;
            }

            return "unknown";
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetThreadDpiAwarenessContext(); // blittable, no marshalling

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b); // non-zero: equal
}
