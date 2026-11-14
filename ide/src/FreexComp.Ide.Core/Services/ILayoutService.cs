using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Services;

/// <summary>
/// Layout profiles, pane cycling and the global UI state file <c>R\uistate.json</c> (architecture.md §4 and §8.1,
/// main-window.md §2 and §6). Owns <c>window.nextPane</c>, <c>window.previousPane</c>, <c>window.focusEditor</c> and
/// <c>window.resetLayout</c>.
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

    /// <summary>UI-DLG-051: the folder of the last successful pick of a picker prompt (<c>dialogs.lastFolder.&lt;promptId&gt;</c>).</summary>
    string? GetLastFolder(string promptId);

    /// <summary>Stores the last folder of a picker prompt; saved with the rest of the UI state.</summary>
    void SetLastFolder(string promptId, string folder);

    /// <summary>A string value of a dialog's UI state, <c>dialogs.&lt;Name&gt;.&lt;key&gt;</c> (dialogs.md §24.3); null when unset.</summary>
    string? GetDialogValue(string dialogName, string key);

    /// <summary>Stores <c>dialogs.&lt;Name&gt;.&lt;key&gt;</c>; saved with the rest of the UI state.</summary>
    void SetDialogValue(string dialogName, string key, string value);

    /// <summary>
    /// UI-DLG-009: the history of a text field, <c>dialogs.&lt;Name&gt;.history.&lt;Field&gt;</c>, most recent first
    /// (at most <see cref="LayoutService.MaxHistory"/> entries); empty when unset.
    /// </summary>
    IReadOnlyList<string> GetDialogHistory(string dialogName, string field);

    /// <summary>Puts <paramref name="text"/> first in a field's history (case-sensitive de-duplication, at most 20 entries).</summary>
    void AddDialogHistory(string dialogName, string field, string text);

    /// <summary>A Boolean of the UI state, <c>&lt;section&gt;.&lt;key&gt;</c> (e.g. <c>find.matchCase</c>, UI-DLG-102); null when unset.</summary>
    bool? GetUiFlag(string section, string key);

    /// <summary>Stores <c>&lt;section&gt;.&lt;key&gt;</c>; saved with the rest of the UI state.</summary>
    void SetUiFlag(string section, string key, bool value);

    /// <summary>Gives the layout the tool-window registry: names → view-models (null = not registered).</summary>
    void AttachToolWindows(Func<string, ToolWindowViewModel?> resolve);

    /// <summary>Writes <c>uistate.json</c> now (main window closing, UI-WIN-051 (a)); atomic, never throws.</summary>
    void Save();
}
