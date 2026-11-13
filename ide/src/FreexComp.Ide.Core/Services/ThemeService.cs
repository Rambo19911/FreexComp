using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Services;

/// <summary>
/// <see cref="IThemeService"/>: follows <c>appearance.theme</c> and owns the <c>view.theme.*</c> commands
/// (exclusive group, UI-MENU-007). UI thread only.
/// </summary>
public sealed class ThemeService : IThemeService
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    private readonly ISettingsService _settings;
    private readonly IThemeHost _host;

    /// <summary>Applies the stored theme immediately (IDE-ARCH-015 step 5).</summary>
    public ThemeService(ISettingsService settings, IThemeHost host)
    {
        _settings = settings;
        _host = host;
        Apply();
        _settings.Changed += (_, e) =>
        {
            if (e.Key == SettingsCatalog.AppearanceTheme.Name)
            {
                Apply();
            }
        };
        _host.ActualThemeChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public string RequestedTheme => _settings.Get(SettingsCatalog.AppearanceTheme);

    public ThemeKind ActualTheme => _host.ActualTheme;

    /// <summary>Registers the handlers of <c>view.theme.light</c>, <c>.dark</c> and <c>.system</c>.</summary>
    public void RegisterCommands(ICommandService commands)
    {
        foreach (var value in new[] { Light, Dark, System })
        {
            commands.RegisterHandler("view.theme." + value, new SetThemeHandler(this, value));
        }

        _settings.Changed += (_, e) =>
        {
            if (e.Key == SettingsCatalog.AppearanceTheme.Name)
            {
                commands.InvalidateCanExecute(); // moves the check mark
            }
        };
        commands.InvalidateCanExecute();
    }

    private void Apply() => _host.Apply(RequestedTheme switch
    {
        Light => ThemeKind.Light,
        Dark => ThemeKind.Dark,
        _ => null,
    });

    private sealed class SetThemeHandler(ThemeService owner, string value) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument)
        {
            owner._settings.Set(SettingsCatalog.AppearanceTheme, value);
            return Task.CompletedTask;
        }

        public bool IsChecked(string? argument) => owner.RequestedTheme == value;
    }
}
