using System.Runtime.InteropServices;
using Avalonia;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide;

internal static class Program
{
    private const int AttachParentProcess = -1;

    /// <summary>How long a second instance waits for the running one to take its files before it starts itself.</summary>
    private static readonly TimeSpan HandOverTimeout = TimeSpan.FromSeconds(5);

    [STAThread]
    public static int Main(string[] args)
    {
        // IDE-P3-087: console-host mode runs before any UI or service is created.
        if (args.Length > 0 && args[0] == Platform.Windows.ConsoleHost.Option)
            return Platform.Windows.ConsoleHost.Run(args);

        // IDE-P1-013: --version, --help and unknown options are answered before any window exists.
        var options = StartupOptions.Parse(args);
        if (options.UnknownOption is not null || options.ShowHelp || options.ShowVersion || options.SmokeDpi)
        {
            AttachConsole(AttachParentProcess); // a windowed executable: print into the console that started it, if any
            if (options.HandleInformationOptions(Console.Out, Console.Error) is { } exitCode)
                return exitCode;
        }

        // INS-021: a portable installation keeps its user directory next to the executable; the folder exists from the
        // first start on (also after the installation check below). Its parts are created on first write (IDE-SET-001).
        var portable = UserPaths.PortableRootFromProcess(options.UserDir);
        if (portable is not null)
            TryCreateDirectory(portable);

        if (options.SmokeDpi)
        {
            // UI-TOK-067: --smoke-dpi. Nothing of the UI library has run yet: this is the awareness the application
            // manifest gave the process (UI-TOK-066).
            Console.Out.WriteLine(Platform.Windows.DpiAwareness.Current);
            return 0;
        }

        // I-213: with files to open, hand them to the IDE already running for this user directory and exit (no window).
        var key = Platform.Windows.InstanceHandOver.Key(options.UserDir ?? portable);
        if (options.Paths.Count > 0 && !options.NewWindow && Platform.Windows.InstanceHandOver.TrySend(key, options.Paths, HandOverTimeout))
            return 0;
        using var instance = Platform.Windows.InstanceHandOver.TryClaim(key); // null: another instance runs (IDE-P1-014)
        App.HandOver = instance;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// A folder that cannot be created (read-only medium, no rights) does not stop the start: the user directory is then
    /// as unwritable as one given with <c>--user-dir</c> would be, and each write into it fails where it is made.
    /// </summary>
    private static void TryCreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();

    [DllImport("kernel32.dll")]
    private static extern int AttachConsole(int processId); // non-zero on success; blittable, no marshalling
}
