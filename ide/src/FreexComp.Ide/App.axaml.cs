using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;
using FreexComp.Ide.Core.Composition;

namespace FreexComp.Ide;

public sealed class App : Application
{
    /// <summary>The service graph of this process; null in the headless test application.</summary>
    internal IdeServices? Services { get; private set; }

    /// <summary>I-213: this process's claim as the running instance of its user directory (null: not the first one).</summary>
    internal static Platform.Windows.InstanceHandOver? HandOver { get; set; }

    /// <summary>UI-TOK-052: a tooltip opens 600 ms after the pointer came to rest.</summary>
    public const int ToolTipShowDelay = 600;

    static App()
    {
        // The delay is a property of the control the tooltip belongs to (its default cannot be replaced for all
        // controls): every control that gets a tooltip gets the delay with it, unless it has set its own.
        ToolTip.TipProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (!control.IsSet(ToolTip.ShowDelayProperty))
                ToolTip.SetShowDelay(control, ToolTipShowDelay);
        });
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // UI-TOK-007: Fluent's brushes resolve their palette colours inside the theme's own resources first, so the
        // overrides are merged there too (App.axaml merges them into the application resources for the brush keys).
        var fluent = Styles.OfType<FluentTheme>().Single();
        fluent.Resources.MergedDictionaries.Add((ResourceDictionary)AvaloniaXamlLoader.Load(FluentOverrides));
    }

    internal static readonly Uri FluentOverrides = new("avares://FreexComp.Ide/Themes/FluentOverrides.axaml");

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // IDE-ARCH-015 steps 1–8 (theme, view-model and UI state arrive with their tasks).
            var options = StartupOptions.Parse(desktop.Args ?? []);
            MainWindow? window = null;
            var platform = CompositionRoot.CreatePlatform(options, () => window);
            try
            {
                Services = CompositionRoot.Build(options, platform);
            }
            catch
            {
                // A failed start-up check (I-091: a command without a handler) is logged by the graph; flush it, then fail.
                (platform.Log as IDisposable)?.Dispose();
                throw;
            }

            window = CompositionRoot.CreateMainWindow(Services);
            var services = Services;
            CompositionRoot.AttachProcessErrorHandlers(services); // IDE-ARCH-091
            window.Opened += (_, _) =>
            {
                _ = StartupSequence.RunAsync(services); // step 9: recovery, then the start-up content
                // I-213: files a second instance hands over open here.
                HandOver?.Serve(paths => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = CompositionRoot.ReceiveHandOverAsync(services, window, paths)));
            };
            // The exit sequence (UI-WIN-072, IApplicationService) has written the state and deleted the recovery folder.
            desktop.Exit += (_, _) => (platform.Log as IDisposable)?.Dispose();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
