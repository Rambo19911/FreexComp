using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FreexComp.Rtl.Tests.Harness;

namespace FreexComp.UiFramework.Tests;

/// <summary>
/// Group C harness (U-106, <c>sample-apps.md</c> §2): builds a sample or a probe program as a GUI exe, starts it,
/// and drives its windows by messages only — never <c>SendInput</c>, never the foreground (UIF-980). Every
/// coordinate call runs with the thread's DPI awareness set to per-monitor v2 so that device pixels are seen.
/// </summary>
public static partial class Gui
{
    public const uint WM_NULL = 0x0000;
    public const uint WM_SETTEXT = 0x000C;
    public const uint WM_GETTEXT = 0x000D;
    public const uint WM_GETTEXTLENGTH = 0x000E;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint BM_GETCHECK = 0x00F0;
    public const uint LB_GETTEXT = 0x0189;
    public const uint LB_GETTEXTLEN = 0x018A;
    public const uint LB_GETCOUNT = 0x018B;
    public const uint SB_GETTEXTW = 0x0400 + 13;
    public const ushort BN_CLICKED = 0;
    public const int VK_TAB = 0x09;
    public const int VK_RETURN = 0x0D;
    public const int VK_ESCAPE = 0x1B;
    public const uint GR_GDIOBJECTS = 0;
    public const uint GR_USEROBJECTS = 1;

    /// <summary>
    /// U-139: the one safety net of every wait of this harness (window, text, state, output line, process exit, message
    /// round trip). It is not a performance budget: a wait returns as soon as its event happens, and the net only ends a
    /// wait for an event that never comes (the test then fails, naming what did not happen). It is generous because the
    /// full suite runs on a shared, CPU-loaded machine; no test asserts a duration.
    /// </summary>
    public static TimeSpan SafetyNet { get; } = TimeSpan.FromSeconds(120);

    /// <summary>The net of one <c>opc</c> build of a test program (a build is several net-sized steps of work under load).</summary>
    public static TimeSpan BuildSafetyNet { get; } = 2 * SafetyNet;

    /// <summary>A single message round trip; a target that the system reports as hung still fails at once (<c>SMTO_ABORTIFHUNG</c>).</summary>
    private static readonly uint MessageTimeoutMs = (uint)SafetyNet.TotalMilliseconds;

    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint MK_LBUTTON = 0x0001;
    private const uint GW_HWNDNEXT = 2;
    private const uint GW_CHILD = 5;
    private const uint MF_BYPOSITION = 0x0400;
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;

    private static readonly ConcurrentDictionary<string, Lazy<string>> Built = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>U-127: the windows of <see cref="StartForegroundHolder"/> (with their thread, against a reused handle value).</summary>
    private static readonly ConcurrentDictionary<(IntPtr Window, uint Thread), bool> HolderWindows = new();

    /// <summary>
    /// Backstop of a whole GUI session (testing.md UIF-1208): the process tree is killed when it is still running this
    /// long after its start, even if a test is stuck outside the harness waits. Several safety nets, so that a test that
    /// waits for several events in turn is never cut short by it.
    /// </summary>
    public static TimeSpan SessionTimeout { get; } = 4 * SafetyNet;

    /// <summary>
    /// <c>BuildSample(dir)</c>: the sample's <c>&lt;Name&gt;.pproj</c> built as a GUI exe (once per test run). Until
    /// <c>pbuild</c> exists the fallback is <c>opc &lt;mainSource&gt; --app-type gui -U ui-framework/src</c>; the build
    /// must succeed without warnings from <c>ui-framework/</c> or <c>rtl/</c> files.
    /// </summary>
    public static string BuildSample(string directory)
    {
        var project = Directory.GetFiles(directory, "*.pproj").Single();
        using var json = JsonDocument.Parse(File.ReadAllText(project));
        var root = json.RootElement;
        Assert.Equal("gui", root.GetProperty("appType").GetString());
        var main = Path.Combine(directory, root.GetProperty("mainSource").GetString()!);
        return BuildProgram(main, root.GetProperty("name").GetString()!);
    }

    /// <summary>Compiles <paramref name="source"/> as a GUI program (default manifest: comctl32 6, per-monitor v2) into a temporary folder.</summary>
    public static string BuildProgram(string source, string? name = null) =>
        Built.GetOrAdd(Path.GetFullPath(source), s => new Lazy<string>(() => Compile(s, name ?? Path.GetFileNameWithoutExtension(s)))).Value;

    private static string Compile(string source, string name)
    {
        var setup = UiCorpus.Setup;
        var work = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "gui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var exe = Path.Combine(work, name + ".exe");
        List<string> args =
        [
            source, "--app-type", "gui", "-U", UiCorpus.SourceDirectory, "-o", exe, "--obj-dir", Path.Combine(work, "obj"),
            "--rtl", setup.RtlDirectory, "--toolchain", setup.ToolchainDirectory,
        ];
        var psi = new ProcessStartInfo(setup.OpcPath, GoldenRunner.CommandLine(args, null))
        {
            WorkingDirectory = Path.GetDirectoryName(source)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var opc = Process.Start(psi)!;
        var stdout = PipeCapture.Start(opc.StandardOutput, "opc stdout");   // U-142: never on the thread pool
        var stderr = PipeCapture.Start(opc.StandardError, "opc stderr");
        if (!opc.WaitForExit(BuildSafetyNet))
        {
            opc.Kill(entireProcessTree: true);
            Assert.Fail($"opc did not finish building {source} (safety net {BuildSafetyNet.TotalSeconds:F0} s; its process tree was killed)");
        }

        var output = stdout.ReadToEnd() + stderr.ReadToEnd();
        Assert.True(opc.ExitCode == 0, $"opc exit code {opc.ExitCode} building {source}\n{output}");
        var ours = new[] { UiCorpus.AreaDirectory, Path.Combine(RtlSetup.RepositoryRoot, "rtl") };
        var warnings = output.ReplaceLineEndings("\n").Split('\n')
            .Select(l => WarningLine().Match(l))
            .Where(m => m.Success)
            .Where(m => ours.Any(dir => Path.GetFullPath(m.Groups["file"].Value, psi.WorkingDirectory)
                .StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Value)
            .ToList();
        Assert.True(warnings.Count == 0, "warnings from ui-framework/ or rtl/:\n" + string.Join("\n", warnings));
        return exe;
    }

    [GeneratedRegex(@"^(?<file>.+?)\(\d+,\d+\): warning ", RegexOptions.CultureInvariant)]
    private static partial Regex WarningLine();

    /// <summary><c>StartGui(exe, args, env)</c>: stdout/stderr redirected, <c>FXC_UI_NONINTERACTIVE=1</c> plus <paramref name="env"/>, in the job of the test host (U-162), killed on dispose.</summary>
    public static GuiProcess StartGui(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env)
    {
        var psi = new ProcessStartInfo(exe, GoldenRunner.CommandLine(args, null))
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.Environment["FXC_UI_NONINTERACTIVE"] = "1";
        foreach (var (key, value) in env)
        {
            psi.Environment[key] = value;
        }

        var process = Process.Start(psi)!;
        AssignToHostJob(process);                                   // U-162: dies with the test host
        return new GuiProcess(process);
    }

    /// <summary>
    /// <c>WaitForMainWindow</c>: polls the visible top-level windows of the process for exactly this class and
    /// title; zero when the process exited without it or after <paramref name="timeout"/> (default <see cref="SafetyNet"/>).
    /// <see cref="TimeSpan.Zero"/> looks once.
    /// </summary>
    public static IntPtr WaitForMainWindow(GuiProcess app, string className, string title, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        var limit = timeout ?? SafetyNet;
        while (true)
        {
            var found = IntPtr.Zero;
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out var pid);
                if (pid == app.Process.Id && IsWindowVisible(h) && ClassName(h) == className && GetText(h) == title)
                {
                    found = h;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero)
            {
                app.MainWindow = found;
                return found;
            }

            if (watch.Elapsed >= limit || app.Process.HasExited)
            {
                return IntPtr.Zero;
            }

            Thread.Sleep(10);                                      // polling interval, not a wait for a result
        }
    }

    /// <summary><c>FindDescendant</c>: depth-first in z-order; class name case-insensitive, text (if given) exact.</summary>
    public static IntPtr FindDescendant(IntPtr parent, string className, string? text = null)
    {
        for (var child = GetWindow(parent, GW_CHILD); child != IntPtr.Zero; child = GetWindow(child, GW_HWNDNEXT))
        {
            if (string.Equals(ClassName(child), className, StringComparison.OrdinalIgnoreCase) && (text is null || GetText(child) == text))
            {
                return child;
            }

            var inner = FindDescendant(child, className, text);
            if (inner != IntPtr.Zero)
            {
                return inner;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>All direct and indirect child windows, depth-first in z-order.</summary>
    public static IEnumerable<IntPtr> Descendants(IntPtr parent)
    {
        var all = new List<IntPtr>();
        void Walk(IntPtr p)
        {
            for (var child = GetWindow(p, GW_CHILD); child != IntPtr.Zero; child = GetWindow(child, GW_HWNDNEXT))
            {
                all.Add(child);
                Walk(child);
            }
        }

        Walk(parent);
        return all;
    }

    public static string ClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        var length = GetClassNameW(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(0, length));
    }

    /// <summary><c>GetText</c>: <c>WM_GETTEXTLENGTH</c> then <c>WM_GETTEXT</c>, each with a 2 s abort-if-hung timeout.</summary>
    public static string GetText(IntPtr hwnd)
    {
        Send(hwnd, WM_GETTEXTLENGTH, 0, 0, out var length);
        var buffer = new char[(int)length + 1];
        if (SendMessageTimeoutW(hwnd, WM_GETTEXT, (UIntPtr)buffer.Length, buffer, SMTO_ABORTIFHUNG, MessageTimeoutMs, out var copied) == IntPtr.Zero)
        {
            throw new TimeoutException($"WM_GETTEXT to {hwnd} timed out");
        }

        return new string(buffer, 0, (int)copied);
    }

    public static void SetText(IntPtr hwnd, string text)
    {
        if (SendMessageTimeoutW(hwnd, WM_SETTEXT, UIntPtr.Zero, text, SMTO_ABORTIFHUNG, MessageTimeoutMs, out _) == IntPtr.Zero)
        {
            throw new TimeoutException($"WM_SETTEXT to {hwnd} timed out");
        }
    }

    /// <summary><c>SendMessageTimeoutW</c> with scalar arguments; the result of the target's window procedure.</summary>
    public static nint Send(IntPtr hwnd, uint msg, nuint wParam, nint lParam)
    {
        Send(hwnd, msg, wParam, lParam, out var result);
        return (nint)result;
    }

    private static void Send(IntPtr hwnd, uint msg, nuint wParam, nint lParam, out nuint result)
    {
        if (SendMessageTimeoutW(hwnd, msg, wParam, lParam, SMTO_ABORTIFHUNG, MessageTimeoutMs, out result) == IntPtr.Zero)
        {
            throw new TimeoutException($"message 0x{msg:X4} to {hwnd} timed out");
        }
    }

    public static void Post(IntPtr hwnd, uint msg, nuint wParam = 0, nint lParam = 0) =>
        Assert.True(PostMessageW(hwnd, msg, wParam, lParam), $"PostMessageW 0x{msg:X4} failed");

    /// <summary><c>Command(parent, child, code)</c>: a control notification sent to the parent, independent of the foreground.</summary>
    public static void Command(IntPtr parent, IntPtr child, ushort code) =>
        Send(parent, WM_COMMAND, (nuint)(((uint)code << 16) | ((uint)GetDlgCtrlID(child) & 0xFFFF)), child);

    /// <summary><c>PostKey</c>: key down (repeat count 1) and key up (previous state, transition) posted, then <see cref="WaitIdle(IntPtr)"/>.</summary>
    public static void PostKey(IntPtr hwnd, int vk)
    {
        Post(hwnd, WM_KEYDOWN, (nuint)vk, 1);
        Post(hwnd, WM_KEYUP, (nuint)vk, unchecked((nint)0xC0000001));
        WaitIdle(hwnd);
    }

    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;
    public const uint WM_SYSCHAR = 0x0106;
    public const int VK_MENU = 0x12;
    public const int VK_F10 = 0x79;
    public const uint GUI_INMENUMODE = 0x0004;

    /// <summary>
    /// U-134: a system key (Alt held: context code bit 29 when <paramref name="altDown"/>, except on the release of Alt
    /// itself) posted as the keyboard driver delivers it to the active window: <c>WM_SYSKEYDOWN</c>, then <c>WM_SYSCHAR</c> when <paramref name="character"/> is given, then
    /// <c>WM_SYSKEYUP</c>; then <see cref="WaitIdle(IntPtr)"/>. No input is injected into the system queue.
    /// </summary>
    public static void PostSysKey(IntPtr hwnd, int vk, bool altDown, char? character = null)
    {
        var context = altDown ? 0x20000000 : 0;
        Post(hwnd, WM_SYSKEYDOWN, (nuint)vk, context | 1);
        if (character is { } c)
        {
            Post(hwnd, WM_SYSCHAR, c, context | 1);
        }

        Post(hwnd, WM_SYSKEYUP, (nuint)vk, unchecked((nint)(0xC0000001u | (vk == VK_MENU ? 0u : (uint)context))));   // Alt's own release: context 0
        WaitIdle(hwnd);
    }

    /// <summary>U-134: the <c>GUI_*</c> flags of the thread that owns <paramref name="hwnd"/> (<c>GetGUIThreadInfo</c>).</summary>
    public static uint GuiThreadFlags(IntPtr hwnd)
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        Assert.True(GetGUIThreadInfo(GetWindowThreadProcessId(hwnd, out _), ref info), "GetGUIThreadInfo failed");
        return info.Flags;
    }

    /// <summary>U-134: the window that owns the active menu of <paramref name="hwnd"/>'s thread (0 when none).</summary>
    public static IntPtr MenuOwner(IntPtr hwnd)
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        Assert.True(GetGUIThreadInfo(GetWindowThreadProcessId(hwnd, out _), ref info), "GetGUIThreadInfo failed");
        return info.MenuOwner;
    }

    /// <summary>U-134: the visible menu windows (system class <c>#32768</c>: an open drop-down or popup menu) of <paramref name="hwnd"/>'s thread.</summary>
    public static int OpenMenuWindows(IntPtr hwnd)
    {
        var thread = GetWindowThreadProcessId(hwnd, out _);
        var count = 0;
        EnumWindows((h, _) =>
        {
            if (GetWindowThreadProcessId(h, out int _) == thread && IsWindowVisible(h) && ClassName(h) == "#32768")
            {
                count++;
            }

            return true;
        }, IntPtr.Zero);
        return count;
    }

    /// <summary><c>PostClick</c>: left button down (<c>MK_LBUTTON</c>) and up at client coordinates, then <see cref="WaitIdle(IntPtr)"/>.</summary>
    public static void PostClick(IntPtr hwnd, int x, int y)
    {
        // posted mouse coordinates are rescaled by the system between DPI contexts: post as a per-monitor-v2 thread
        var point = (nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
        Aware(() =>
        {
            Post(hwnd, WM_LBUTTONDOWN, MK_LBUTTON, point);
            Post(hwnd, WM_LBUTTONUP, 0, point);
        });
        WaitIdle(hwnd);
    }

    /// <summary>
    /// <c>WaitIdle</c>: <c>WM_NULL</c> round trips to the main window. A sent message is delivered at the target's
    /// next message retrieval, before posted messages, so one round trip does not prove that everything posted
    /// before has been handled; three consecutive round trips make it overwhelmingly likely, and assertions on the
    /// effect of posted input use <see cref="WaitForText"/> / <see cref="WaitUntil"/> as the authoritative wait.
    /// </summary>
    public static void WaitIdle(GuiProcess app) => WaitIdle(app.MainWindow);

    public static void WaitIdle(IntPtr anyWindowOfTarget)
    {
        var root = GetAncestor(anyWindowOfTarget, 2 /* GA_ROOT */);
        for (var i = 0; i < 3; i++)
        {
            Send(root == IntPtr.Zero ? anyWindowOfTarget : root, WM_NULL, 0, 0);
        }
    }

    /// <summary>Waits (bounded) until the window text equals <paramref name="expected"/>; returns the last text read.</summary>
    public static string WaitForText(IntPtr hwnd, string expected, TimeSpan? timeout = null)
    {
        var text = GetText(hwnd);
        WaitUntil(() => (text = GetText(hwnd)) == expected, timeout);
        return text;
    }

    /// <summary>Re-evaluates <paramref name="condition"/> until it holds or <paramref name="timeout"/> (default <see cref="SafetyNet"/>) passes; returns its last value.</summary>
    public static bool WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        var limit = timeout ?? SafetyNet;
        while (!condition())
        {
            if (watch.Elapsed >= limit)
            {
                return false;
            }

            Thread.Sleep(5);
        }

        return true;
    }

    /// <summary>
    /// <c>RemoteText(hwnd, msg, wParam)</c>: for common-control messages whose buffer pointer is not marshalled across
    /// processes: a buffer in the target (<c>VirtualAllocEx</c>), the message sent, the text read back.
    /// </summary>
    public static string RemoteText(IntPtr hwnd, uint msg, nuint wParam)
    {
        const int Chars = 2048;
        GetWindowThreadProcessId(hwnd, out var pid);
        var process = OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, false, pid);   // VM_OPERATION | VM_READ | VM_WRITE | QUERY_INFORMATION
        Assert.True(process != IntPtr.Zero, "OpenProcess failed");
        try
        {
            var remote = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)(Chars * 2), 0x3000, 0x04);   // MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE (zeroed)
            Assert.True(remote != IntPtr.Zero, "VirtualAllocEx failed");
            try
            {
                Send(hwnd, msg, wParam, remote);
                var bytes = new byte[Chars * 2];
                Assert.True(ReadProcessMemory(process, remote, bytes, (UIntPtr)bytes.Length, out _), "ReadProcessMemory failed");
                var text = Encoding.Unicode.GetString(bytes);
                var end = text.IndexOf('\0', StringComparison.Ordinal);
                return end < 0 ? text : text[..end];
            }
            finally
            {
                VirtualFreeEx(process, remote, UIntPtr.Zero, 0x8000);                   // MEM_RELEASE
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary><c>LB_GETTEXT</c> (marshalled by the system for list boxes).</summary>
    public static string ListBoxText(IntPtr list, int index)
    {
        var length = Send(list, LB_GETTEXTLEN, (nuint)index, 0);
        var buffer = new char[length + 1];
        if (SendMessageTimeoutW(list, LB_GETTEXT, (UIntPtr)index, buffer, SMTO_ABORTIFHUNG, MessageTimeoutMs, out var copied) == IntPtr.Zero)
        {
            throw new TimeoutException("LB_GETTEXT timed out");
        }

        return new string(buffer, 0, (int)copied);
    }

    /// <summary><c>ClientRectOf</c>: <c>GetClientRect</c> size in device pixels.</summary>
    public static (int Width, int Height) ClientRectOf(IntPtr hwnd) => Aware(() =>
    {
        Assert.True(GetClientRect(hwnd, out var r));
        return (r.Right - r.Left, r.Bottom - r.Top);
    });

    /// <summary><c>RectInParent</c>: <c>GetWindowRect</c> mapped to the parent's client coordinates; (left, top, width, height).</summary>
    public static (int Left, int Top, int Width, int Height) RectInParent(IntPtr child) => Aware(() =>
    {
        Assert.True(GetWindowRect(child, out var r));
        var parent = GetAncestor(child, 1 /* GA_PARENT */);
        MapWindowPoints(IntPtr.Zero, parent, ref r, 2);
        return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    });

    /// <summary>Outer size of a top-level window in device pixels.</summary>
    public static (int Width, int Height) WindowSize(IntPtr hwnd) => Aware(() =>
    {
        Assert.True(GetWindowRect(hwnd, out var r));
        return (r.Right - r.Left, r.Bottom - r.Top);
    });

    /// <summary><c>SetWindowPos(…, SWP_NOMOVE or SWP_NOZORDER or SWP_NOACTIVATE)</c> to an outer size in device pixels.</summary>
    public static void SetWindowSize(IntPtr hwnd, int width, int height) => Aware(() =>
        Assert.True(SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, 0x0002 | 0x0004 | 0x0010), "SetWindowPos failed"));

    /// <summary>Invalidates the window and its children and paints them now (<c>RDW_UPDATENOW</c> sends WM_PAINT synchronously).</summary>
    public static void Repaint(IntPtr hwnd) =>
        Assert.True(RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100), "RedrawWindow failed");   // INVALIDATE | ERASE | ALLCHILDREN | UPDATENOW

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr update, IntPtr region, uint flags);

    /// <summary><c>Dpi(hwnd)</c> = <c>GetDpiForWindow</c>.</summary>
    public static int Dpi(IntPtr hwnd) => (int)GetDpiForWindow(hwnd);

    /// <summary><c>Px(v, dpi)</c> = <c>MulDiv(v, dpi, 96)</c> rounded half away from zero (<c>layout.md</c> §1).</summary>
    public static int Px(int v, int dpi)
    {
        var product = (long)v * dpi;
        return (int)(product >= 0 ? (product + 48) / 96 : (product - 48) / 96);
    }

    /// <summary>The window's menu bar (0 if none) and menu queries (cross-process safe).</summary>
    public static IntPtr Menu(IntPtr hwnd) => GetMenu(hwnd);

    public static int MenuItemCount(IntPtr menu) => GetMenuItemCount(menu);

    public static IntPtr SubMenu(IntPtr menu, int position) => GetSubMenu(menu, position);

    public static uint MenuItemId(IntPtr menu, int position) => GetMenuItemID(menu, position);

    public static string MenuItemText(IntPtr menu, int position)
    {
        var buffer = new char[256];
        var length = GetMenuStringW(menu, (uint)position, buffer, buffer.Length, MF_BYPOSITION);
        return new string(buffer, 0, Math.Max(0, length));
    }

    public static bool IsVisible(IntPtr hwnd) => IsWindowVisible(hwnd);

    public static IntPtr ParentOf(IntPtr hwnd) => GetAncestor(hwnd, 1 /* GA_PARENT */);

    public static IntPtr Foreground() => GetForegroundWindow();

    /// <summary>
    /// U-126: makes <paramref name="hwnd"/> (a top-level window of another process) the foreground window; false only
    /// when it still is not after <paramref name="timeout"/> (default 5 s). A plain <c>SetForegroundWindow</c> from the
    /// test process is refused by the foreground lock whenever another process holds the foreground, and even when it
    /// succeeds the activation of another thread's window completes asynchronously (the foreground is briefly none),
    /// so attempts take turns until the deadline, each followed by polling <c>GetForegroundWindow</c> for up to 500 ms:
    /// <list type="number">
    /// <item>U-127: when a window of <see cref="StartForegroundHolder"/> is the foreground, the holder is asked to hand
    /// it over (<see cref="WM_HANDOVER"/>): the foreground process may give the foreground away, so the target is
    /// really activated (it receives <c>WM_ACTIVATE</c>);</item>
    /// <item><c>SetForegroundWindow</c>;</item>
    /// <item>the same with the foreground window's thread attached to the target's input (<c>AttachThreadInput</c>),
    /// detached again at once;</item>
    /// <item>minimise and restore (<c>ShowWindow</c> <c>SW_MINIMIZE</c>, <c>SW_RESTORE</c>, which activates the window).</item>
    /// </list>
    /// U-127 found that methods 2 and 3 can make <c>GetForegroundWindow</c> report the target although the target never
    /// receives <c>WM_ACTIVATE</c> (seen after a few cycles between the holder and a form); a test of activation
    /// therefore takes the foreground from a holder, where method 1 applies. A holder window itself starts with
    /// minimise/restore, which activates it reliably (methods 2 and 3 had no effect on it while a form was the
    /// foreground). No input is injected and no system setting is changed.
    /// </summary>
    public static bool BringToForeground(IntPtr hwnd, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        var limit = timeout ?? SafetyNet;
        var first = IsHolder(hwnd) ? 3 : 0;
        for (var attempt = first; ; attempt++)
        {
            if (GetForegroundWindow() == hwnd)
            {
                return true;
            }

            if (watch.Elapsed >= limit)
            {
                return false;
            }

            switch (attempt % 4)
            {
                case 0:
                    var holder = GetForegroundWindow();
                    if (!IsHolder(holder))
                    {
                        continue;                                      // no holder to ask: next method at once
                    }

                    Send(holder, WM_HANDOVER, (nuint)hwnd, 0);
                    break;
                case 1:
                    SetForegroundWindow(hwnd);
                    break;
                case 2:
                    var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                    var targetThread = GetWindowThreadProcessId(hwnd, out _);
                    var attached = foregroundThread != 0 && foregroundThread != targetThread
                        && AttachThreadInput(foregroundThread, targetThread, true);
                    SetForegroundWindow(hwnd);
                    if (attached)
                    {
                        AttachThreadInput(foregroundThread, targetThread, false);
                    }

                    break;
                default:
                    if (!IsIconic(hwnd))
                    {
                        ShowWindow(hwnd, SW_MINIMIZE);
                    }

                    ShowWindow(hwnd, SW_RESTORE);
                    break;
            }

            WaitUntil(() => GetForegroundWindow() == hwnd, TimeSpan.FromMilliseconds(500));   // attempt slice, not a budget: a late change is seen by the next round
        }
    }

    /// <summary>U-127: the message of <c>tests/gui/ForegroundHolder.pas</c> that hands the foreground to window wParam.</summary>
    private const uint WM_HANDOVER = 0x8001;                         // WM_APP + 1

    private static bool IsHolder(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && HolderWindows.ContainsKey((hwnd, GetWindowThreadProcessId(hwnd, out _)));

    /// <summary>
    /// U-126: starts <c>tests/gui/ForegroundHolder.pas</c>, a second process whose window takes the foreground, and
    /// waits until its window is shown, idle and the foreground window (it does not always win it on its own: the
    /// foreground lock may refuse it too); the caller disposes it (killed) or posts <c>WM_CLOSE</c>.
    /// </summary>
    public static GuiProcess StartForegroundHolder()
    {
        var exe = BuildProgram(Path.Combine(UiCorpus.AreaDirectory, "tests", "gui", "ForegroundHolder.pas"));
        var holder = StartGui(exe, [], new Dictionary<string, string>());
        var window = WaitForMainWindow(holder, "FxcForegroundHolder", "Foreground Holder");
        Assert.True(window != IntPtr.Zero, "the foreground holder shows no window\n" + holder.StdErr);
        WaitIdle(holder);
        HolderWindows[(window, GetWindowThreadProcessId(window, out _))] = true;
        Assert.True(BringToForeground(window), $"the foreground holder did not get the foreground (it is {GetForegroundWindow():X})");
        return holder;
    }

    /// <summary>The GDI (<see cref="GR_GDIOBJECTS"/>) or USER (<see cref="GR_USEROBJECTS"/>) object count of <paramref name="process"/> (<c>GetGuiResources</c>).</summary>
    public static int GuiResources(Process process, uint kind) => (int)GetGuiResources(process.Handle, kind);

    /// <summary>
    /// U-136: the GDI objects of <paramref name="process"/> by type (DC, region, bitmap, palette, font, brush, …), read
    /// from the GDI handle table mapped into every GUI process (PEB of the 64-bit target), for failure messages:
    /// e.g. <c>DC=4 Region=3 Bitmap=9 Font=6 Brush=12</c>; <c>"?"</c> when the table cannot be read.
    /// </summary>
    public static string GdiTypeCounts(Process process)
    {
        const int entrySize = 24, entries = 65536;
        var info = new byte[48];
        if (NtQueryInformationProcess(process.Handle, 0, info, info.Length, out _) != 0)
        {
            return "?";
        }

        var pointer = new byte[8];
        var peb = (IntPtr)BitConverter.ToInt64(info, 8);
        if (!ReadProcessMemory(process.Handle, peb + 0xF8, pointer, 8, out _))
        {
            return "?";
        }

        var table = new byte[entrySize * entries];
        if (!ReadProcessMemory(process.Handle, (IntPtr)BitConverter.ToInt64(pointer, 0), table, (UIntPtr)table.Length, out _))
        {
            return "?";
        }

        var names = new Dictionary<int, string> { [1] = "DC", [4] = "Region", [5] = "Bitmap", [8] = "Palette", [10] = "Font", [16] = "Brush" };
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < entries; i++)
        {
            var at = i * entrySize;
            if (BitConverter.ToUInt16(table, at + 8) != (ushort)process.Id)
            {
                continue;
            }

            var type = table[at + 14] & 0x7F;
            if (type == 0)
            {
                continue;
            }

            var name = names.TryGetValue(type, out var n) ? n : "Type" + type;
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        return string.Join(" ", counts.Select(c => $"{c.Key}={c.Value}"));
    }

    private static bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out UIntPtr read) =>
        ReadProcessMemory(process, address, buffer, (UIntPtr)size, out read);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, [Out] byte[] info, int size, out int returned);

    private static T Aware<T>(Func<T> action)
    {
        var old = SetThreadDpiAwarenessContext(-4);                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        try
        {
            return action();
        }
        finally
        {
            SetThreadDpiAwarenessContext(old);
        }
    }

    private static void Aware(Action action) => Aware(() =>
    {
        action();
        return 0;
    });

    /// <summary>A started GUI program: output captured, the <see cref="SessionTimeout"/> backstop, killed (tree) on dispose.</summary>
    public sealed class GuiProcess : IDisposable
    {
        private readonly Timer deadline;
        private bool killed;

        internal GuiProcess(Process process)
        {
            Process = process;
            // U-142: the pipes are read line by line on threads of their own for the whole session, never on the pool
            OutputReader = PipeCapture.Start(process.StandardOutput, $"gui stdout {process.Id}", lines: true);
            ErrorReader = PipeCapture.Start(process.StandardError, $"gui stderr {process.Id}", lines: true);
            deadline = new Timer(_ => Kill(), null, SessionTimeout, Timeout.InfiniteTimeSpan);
        }

        public Process Process { get; }

        public IntPtr MainWindow { get; internal set; }

        internal PipeCapture OutputReader { get; }

        internal PipeCapture ErrorReader { get; }

        /// <summary>The lines written so far, each ended by <c>\n</c>. Complete only after <see cref="WaitForExit"/> returned true.</summary>
        public string StdOut => OutputReader.Text;

        public string StdErr => ErrorReader.Text;

        /// <summary>True once the session backstop (<see cref="SessionTimeout"/>) or a missed exit (<see cref="WaitForExit"/>) killed the process tree.</summary>
        public bool Killed
        {
            get => Volatile.Read(ref killed);
            private set => Volatile.Write(ref killed, value);
        }

        /// <summary>Waits (default <see cref="SafetyNet"/>) until stdout holds the line <paramref name="line"/>; false on timeout or exit without it.</summary>
        public bool WaitForOutputLine(string line, TimeSpan? timeout = null) =>
            Gui.WaitUntil(() => StdOut.Split('\n').Contains(line) || Process.HasExited, timeout ?? SafetyNet)
            && StdOut.Split('\n').Contains(line);

        /// <summary>Writes one line to the program's stdin (a synchronisation point of a program that waits with ReadLn).</summary>
        public void SendLine(string line)
        {
            Process.StandardInput.WriteLine(line);
            Process.StandardInput.Flush();
        }

        /// <summary>
        /// Waits for the process to exit by itself (the exit event; the net is <paramref name="safetyNet"/>, default
        /// <see cref="SafetyNet"/>). True when it did and was not killed: its output is then complete. False when the net
        /// ended the wait (the process tree is then killed, <see cref="Killed"/>) or the session backstop had killed it;
        /// <see cref="ExitFailure"/> words the failure.
        /// </summary>
        public bool WaitForExit(TimeSpan? safetyNet = null)
        {
            if (!Process.WaitForExit(safetyNet ?? SafetyNet))
            {
                Kill();
                return false;
            }

            OutputReader.ReadToEnd();                               // drains the redirected streams
            ErrorReader.ReadToEnd();
            return !Killed;
        }

        /// <summary>The message of a failed <see cref="WaitForExit"/>: what did not exit, why the wait ended, and the captured output.</summary>
        public string ExitFailure(string what) =>
            $"{what} did not exit by itself (safety net {SafetyNet.TotalSeconds:F0} s, session backstop {SessionTimeout.TotalSeconds:F0} s); "
            + $"its process tree was {(Killed ? "killed" : "not killed")}\n--- stdout\n{StdOut}\n--- stderr\n{StdErr}";

        public void Dispose()
        {
            deadline.Dispose();
            Kill();
        }

        private void Kill()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Killed = true;
                    Process.Kill(entireProcessTree: true);
                    Process.WaitForExit(SafetyNet);                  // the kill's own exit event
                }
            }
            catch (InvalidOperationException)
            {
                // exited meanwhile
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public Rect CaretRect;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, [Out] char[] buffer, int length);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, UIntPtr wParam, [Out] char[] buffer, uint flags, uint timeout, out nuint result);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, UIntPtr wParam, string text, uint flags, uint timeout, out nuint result);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hwnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetDlgCtrlID(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref Rect points, uint count);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetMenu(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetMenuItemCount(IntPtr menu);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetSubMenu(IntPtr menu, int position);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetMenuItemID(IntPtr menu, int position);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetMenuStringW(IntPtr menu, uint item, [Out] char[] buffer, int length, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint type, uint protect);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, UIntPtr size, out UIntPtr read);
}

/// <summary>
/// U-126: the group C and E classes, whose programs show windows that take or win back the foreground, run one test
/// at a time: a foreground change in the middle of another such test perturbs it (activation repaints the frame and
/// fills comctl32's caches, so GDI/USER counts move by several objects; focus moves).
/// </summary>
[CollectionDefinition(Name)]
public sealed class DesktopCollection
{
    public const string Name = "Desktop";
}
