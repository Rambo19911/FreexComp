using Avalonia;
using Avalonia.Headless;
using Avalonia.Logging;

[assembly: AvaloniaTestApplication(typeof(FreexComp.Ide.Tests.TestAppBuilder))]

namespace FreexComp.Ide.Tests;

// Headless only: no test opens a real window.
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Logger.Sink = Infrastructure.BindingErrorSink.Instance; // IDE-ARCH-055: binding errors fail the test
        return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
