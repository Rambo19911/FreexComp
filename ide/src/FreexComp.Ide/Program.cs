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
        // IDE-P3-087: console-host mode runs before any UI or service is created.
        if (args.Length > 0 && args[0] == Platform.Windows.ConsoleHost.Option)
            return Platform.Windows.ConsoleHost.Run(args);

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
