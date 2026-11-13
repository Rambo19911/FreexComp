using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(FreexComp.Ide.Tests.TestAppBuilder))]

namespace FreexComp.Ide.Tests;

// Headless only: no test opens a real window.
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
