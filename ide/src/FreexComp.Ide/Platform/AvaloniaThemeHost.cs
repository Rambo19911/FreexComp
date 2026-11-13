using Avalonia;
using Avalonia.Styling;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Platform;

/// <summary>
/// <see cref="IThemeHost"/> over <see cref="Application.RequestedThemeVariant"/> (UI-TOK-004): <c>Default</c> follows
/// the OS, and Avalonia reports OS changes through <see cref="Application.ActualThemeVariantChanged"/>.
/// </summary>
public sealed class AvaloniaThemeHost : IThemeHost
{
    private readonly Application _application;

    public AvaloniaThemeHost(Application application)
    {
        _application = application;
        _application.ActualThemeVariantChanged += (_, _) => ActualThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ActualThemeChanged;

    public ThemeKind ActualTheme => IsDark(_application.ActualThemeVariant) ? ThemeKind.Dark : ThemeKind.Light;

    public void Apply(ThemeKind? requested) => _application.RequestedThemeVariant = requested switch
    {
        ThemeKind.Light => ThemeVariant.Light,
        ThemeKind.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    private static bool IsDark(ThemeVariant variant) =>
        variant == ThemeVariant.Dark || variant.InheritVariant == ThemeVariant.Dark;
}
