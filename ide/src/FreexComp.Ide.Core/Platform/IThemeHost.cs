namespace FreexComp.Ide.Core.Platform;

public enum ThemeKind
{
    Light,
    Dark,
}

/// <summary>The UI toolkit's theme switch (UI-TOK-004), without Avalonia types.</summary>
public interface IThemeHost
{
    /// <summary>The theme in effect: the requested one, or the OS theme when following the system.</summary>
    ThemeKind ActualTheme { get; }

    /// <summary>Raised when <see cref="ActualTheme"/> changes (request, or OS change while following the system).</summary>
    event EventHandler? ActualThemeChanged;

    /// <summary>Requests a theme; null follows the OS.</summary>
    void Apply(ThemeKind? requested);

    /// <summary>
    /// IDE-SET-040: replaces the colour of an editor token (<c>syntax.keyword</c>) in <paramref name="theme"/>'s resources
    /// (<c>Fxc&lt;Token&gt;</c> and <c>Fxc&lt;Token&gt;Brush</c>); <paramref name="color"/> is <c>#RRGGBB</c>.
    /// </summary>
    void SetColor(ThemeKind theme, string token, string color);
}
