using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>The main-window title (main-window.md §4, UI-WIN-030): <c>[&lt;subject&gt;][ (&lt;state&gt;)] – FreexComp</c>.</summary>
public static class WindowTitle
{
    private const string Separator = " – ";

    /// <param name="subject">The project name or path, else the active loose document; null for neither.</param>
    public static string Format(string? subject, IdeState state)
    {
        var stateText = state switch
        {
            IdeState.Building => Strings.Title_State_Building,
            IdeState.Running => Strings.Title_State_Running,
            IdeState.DebugRunning => Strings.Title_State_Debugging,
            IdeState.Paused => Strings.Title_State_Paused,
            _ => null,
        };
        var left = string.Join(" ", new[] { subject, stateText is null ? null : $"({stateText})" }.Where(p => !string.IsNullOrEmpty(p)));
        return left.Length == 0 ? Strings.App_ProductName : left + Separator + Strings.App_ProductName;
    }
}
