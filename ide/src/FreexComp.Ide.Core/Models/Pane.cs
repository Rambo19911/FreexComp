namespace FreexComp.Ide.Core.Models;

/// <summary>
/// The panes of the <c>F6</c> cycle, in cycle order (UI-A11Y-003): document area → left → bottom → right dock → status
/// bar (UI-WIN-042: one tab stop after the right dock).
/// </summary>
public enum Pane
{
    Documents,
    Left,
    Bottom,
    RightUpper,
    RightLower,
    StatusBar,
}
