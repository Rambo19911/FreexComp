using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Services;

/// <summary>
/// Layout profiles, pane cycling and the global UI state file <c>R\uistate.json</c> (architecture.md §4 and §8.1,
/// main-window.md §2 and §6). Owns <c>window.nextPane</c>, <c>window.previousPane</c> and <c>window.focusEditor</c>.
/// UI thread only.
/// </summary>
public interface ILayoutService
{
    /// <summary>The docks around the document area, arranged by the active profile.</summary>
    LayoutViewModel Layout { get; }

    /// <summary>The saved normal bounds and state of the main window; null on first start (UI-WIN-053).</summary>
    WindowPlacement? WindowPlacement { get; set; }

    /// <summary>UI-TW-012: the stored width of a tool window's grid column, or null when the user never resized it.</summary>
    double? GetColumnWidth(string toolWindow, string columnId);

    /// <summary>Stores a column width (at least 32 DIP, UI-TW-011); saved with the rest of the UI state.</summary>
    void SetColumnWidth(string toolWindow, string columnId, double width);

    /// <summary>Gives the layout the tool-window registry: names → view-models (null = not registered).</summary>
    void AttachToolWindows(Func<string, ToolWindowViewModel?> resolve);

    /// <summary>Writes <c>uistate.json</c> now (main window closing, UI-WIN-051 (a)); atomic, never throws.</summary>
    void Save();
}
