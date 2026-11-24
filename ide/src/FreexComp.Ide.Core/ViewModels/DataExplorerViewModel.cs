using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ToolWindows;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// The Data Explorer tool window of Phase 7 (tool-windows.md §16): a static placeholder — the view shows a heading and one text; no
/// tool bar, no rows, no context menu (UI-TW-1400). Registered from Phase 7 in the left dock, hidden until
/// <c>view.dataExplorer</c> shows it (UI-TW-1401).
/// </summary>
public sealed class DataExplorerViewModel : ToolWindowViewModel
{
    public const string ToolName = "DataExplorer";

    public ToolWindowDefinition Definition => new(ToolName, "Cmd_view_dataExplorer", ToolGroupId.Left, 7, () => this);
}
