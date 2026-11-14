using System.Runtime.InteropServices;
using Avalonia;
using FreexComp.Ide.Core.Composition;

namespace FreexComp.Ide;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [STAThread]
    public static int Main(string[] args)
    {
        // IDE-P1-013: --version, --help and unknown options are answered before any window exists.
        var options = StartupOptions.Parse(args);
        if (options.UnknownOption is not null || options.ShowHelp || options.ShowVersion)
        {
            AttachConsole(AttachParentProcess); // a windowed executable: print into the console that started it, if any
            return options.HandleInformationOptions(Console.Out, Console.Error) ?? 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();

    [DllImport("kernel32.dll")]
    private static extern int AttachConsole(int processId); // non-zero on success; blittable, no marshalling
}
