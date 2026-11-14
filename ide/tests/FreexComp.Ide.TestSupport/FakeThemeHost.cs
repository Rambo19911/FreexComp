using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// <see cref="IThemeHost"/> with a settable OS theme; raises the change event only when the actual theme changes;
/// records colour overrides in <see cref="Colors"/>.
/// </summary>
public sealed class FakeThemeHost : IThemeHost
{
    private ThemeKind _systemTheme = ThemeKind.Light;

    public event EventHandler? ActualThemeChanged;

    /// <summary>The last request; null = follow the OS.</summary>
    public ThemeKind? Requested { get; private set; }

    public ThemeKind ActualTheme => Requested ?? _systemTheme;

    /// <summary>The OS theme; changing it while following the system raises the change event.</summary>
    public ThemeKind SystemTheme
    {
        get => _systemTheme;
        set => Change(() => _systemTheme = value);
    }

    /// <summary>Every <see cref="SetColor"/> call, last value per (theme, token).</summary>
    public Dictionary<(ThemeKind Theme, string Token), string> Colors { get; } = [];

    public void Apply(ThemeKind? requested) => Change(() => Requested = requested);

    public void SetColor(ThemeKind theme, string token, string color) => Colors[(theme, token)] = color;

    private void Change(Action change)
    {
        var before = ActualTheme;
        change();
        if (ActualTheme != before)
        {
            ActualThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
