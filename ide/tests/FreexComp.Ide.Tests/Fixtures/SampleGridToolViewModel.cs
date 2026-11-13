using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ToolWindows;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Tests.Fixtures.ViewModels;

/// <summary>
/// A test-only tool window whose content is a grid with resizable columns (UI-TW-011/012). Its view,
/// <c>FreexComp.Ide.Tests.Fixtures.Views.SampleGridToolView</c>, reports column widths through
/// <see cref="ToolWindowViewModel.SetColumnWidth"/> and reads them back with <see cref="ToolWindowViewModel.ColumnWidth"/>.
/// </summary>
public sealed class SampleGridToolViewModel : ToolWindowViewModel
{
    /// <summary>Column ids and default widths (DIP), left to right; a filler column takes the rest.</summary>
    public IReadOnlyList<(string Id, double Width)> Columns { get; } = [("Code", 72), ("File", 160)];

    public static ToolWindowDefinition Definition(string name, ToolGroupId home = ToolGroupId.Bottom) =>
        new(name, "Cmd_view_messages", home, 1, () => new SampleGridToolViewModel());
}
