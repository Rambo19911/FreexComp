using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FreexComp.Ide.Core.Composition;

namespace FreexComp.Ide;

public sealed class App : Application
{
    /// <summary>The service graph of this process; null in the headless test application.</summary>
    internal IdeServices? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // IDE-ARCH-015 steps 1–8 (theme, view-model and UI state arrive with their tasks).
            var options = StartupOptions.Parse(desktop.Args ?? []);
            var window = new MainWindow();
            var platform = CompositionRoot.CreatePlatform(options, () => window);
            Services = CompositionRoot.Build(options, platform);
            desktop.Exit += (_, _) => (platform.Log as IDisposable)?.Dispose();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
