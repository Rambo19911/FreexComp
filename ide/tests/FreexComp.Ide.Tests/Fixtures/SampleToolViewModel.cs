using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ToolWindows;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Tests.Fixtures.ViewModels;

/// <summary>
/// A tool window that exists only in tests (IDE-ARCH-100): a list as primary control, one tool-bar button and a
/// filter box. Its view, <c>FreexComp.Ide.Tests.Fixtures.Views.SampleToolView</c>, is found by the view locator's
/// convention for view-models outside the IDE assembly.
/// </summary>
public sealed class SampleToolViewModel : ToolWindowViewModel
{
    public List<string> AppliedFilters { get; } = [];

    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands { get; } = [new("messages.clear", "clear")];

    public override bool HasFilter => true;

    /// <summary>A definition for <paramref name="name"/>, titled with an existing resource.</summary>
    public static ToolWindowDefinition Definition(string name, ToolGroupId home, int phase = 1, string titleKey = "Cmd_view_messages") =>
        new(name, titleKey, home, phase, () => new SampleToolViewModel());

    protected override void OnFilterApplied(string filter) => AppliedFilters.Add(filter);
}
