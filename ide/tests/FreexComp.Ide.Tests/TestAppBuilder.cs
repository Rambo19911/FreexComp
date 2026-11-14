using Avalonia;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Threading;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Platform;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Text;

[assembly: AvaloniaTestApplication(typeof(FreexComp.Ide.Tests.TestAppBuilder))]

namespace FreexComp.Ide.Tests;

// Headless only: no test opens a real window.
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Logger.Sink = Infrastructure.BindingErrorSink.Instance; // IDE-ARCH-055: binding errors fail the test
        return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).AfterSetup(_ => WarmUp());
    }

    /// <summary>
    /// I-172: the first shell of a process pays the one-time costs (type loading, JIT, XAML and theme loading, the editor
    /// control) — seconds, and more than the 30 s per-test timeout (IDE-TEST-006) on a fully loaded machine. Session
    /// set-up runs before any test's timeout starts, so the shell is built and closed once here, with in-memory files.
    /// </summary>
    private static void WarmUp()
    {
        var services = TestComposition.Build(o =>
        {
            o.Dispatcher = new AvaloniaUiDispatcher();
            o.ThemeHost = new AvaloniaThemeHost(Application.Current!);
            o.TextBuffers = new AvaloniaEditTextBufferFactory();
        });
        services.Documents.OpenUntitled("Unit1.pas", "unit Unit1;\r\n\r\ninterface\r\n\r\nimplementation\r\n\r\nend.\r\n",
            FileEncoding.Utf8, LineEndingKind.CrLf);
        var window = CompositionRoot.CreateMainWindow(services);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
