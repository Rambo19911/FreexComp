using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Services;

/// <summary>
/// Applies <c>appearance.theme</c> (architecture.md §4, IDE-P1-021) and the user colour overrides
/// <c>colors.&lt;theme&gt;.&lt;token&gt;</c> (IDE-SET-040), at start-up and on every change.
/// </summary>
public interface IThemeService
{
    /// <summary>The setting value: <c>system</c>, <c>light</c> or <c>dark</c>.</summary>
    string RequestedTheme { get; }

    ThemeKind ActualTheme { get; }

    /// <summary>Raised when <see cref="ActualTheme"/> changes; views that draw colours themselves repaint (UI-ED-003).</summary>
    event EventHandler? Changed;

    /// <summary>Applies every overridden editor colour to the theme resources (IDE-SET-040).</summary>
    void ApplyOverrides();
}
