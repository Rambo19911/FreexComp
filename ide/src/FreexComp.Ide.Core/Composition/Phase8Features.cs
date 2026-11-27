using System.Collections.Frozen;

namespace FreexComp.Ide.Core.Composition;

/// <summary>
/// The phase-8 items that work (phase8-delivery.md, IDE-P8-001…006). Phase 8 is delivered card by card, so
/// <see cref="ServiceGraph.ImplementedPhase"/> stays 7 until the backlog is empty; this list is the only place that
/// says which catalogued phase-8 command, Options page or field, tool window, toolbar or setting is treated as
/// implemented. Every phase comparison of such an item goes through <see cref="IsAvailable"/>.
/// </summary>
public static class Phase8Features
{
    /// <summary>
    /// IDE-P8-002: a command id as catalogued; <c>options:&lt;PageId&gt;</c>; <c>options:&lt;PageId&gt;.&lt;FieldId&gt;</c>;
    /// <c>toolwindow:&lt;Id&gt;</c>; <c>toolbar:&lt;Id&gt;</c>; <c>setting:&lt;key&gt;</c>. Ordinal order, one id per line, the
    /// card id as a trailing comment. A command in the list needs its real handler in the same change (IDE-P8-004).
    /// </summary>
    internal static readonly IReadOnlyList<string> DeliveredIds =
    [
        "bookmarks.delete", // F11-045
        "bookmarks.goToSource", // F11-045
        "bookmarks.rename", // F11-045
        "designer.revertToInherited", // F05-006, F05-007 (D-134)
        "diff.copyBlockToRight", // F11-061
        "diff.nextDifference", // F12-014
        "diff.previousDifference", // F12-014
        "edit.formatDocument", // F11-101
        "edit.formatSelection", // F11-101
        "edit.quickActions", // F13-003
        "file.compareWithSaved", // F12-014
        "file.copyLibraryUnitToProject", // F05-099
        "file.editLibrarySource", // F02-004
        "file.newFrame", // F05-006
        "file.newInheritedForm", // F05-007
        "file.openInTerminal", // F09-054
        "file.saveGroupAs", // F09-009
        "help.contents", // I-257
        "history.compare", // F11-060
        "history.label", // F11-060
        "history.refresh", // F11-060
        "history.revert", // F11-061
        "locals.viewText", // F14-013
        "options:Editor.General.FormatOnSave", // F11-101
        "options:Editor.General.LiveDiagnosticsSeverities", // F11-065
        "options:Editor.General.WrapSelection", // F11-047
        "options:Environment.Files.HistoryDays", // F11-059
        "options:Environment.Files.HistoryEnabled", // F11-059
        "options:Environment.Files.HistoryRevisions", // F11-059
        "options:Environment.Files.HistorySize", // F11-059
        "options:Environment.General.BuildJobs", // F01-010
        "options:Environment.General.ContinueAfterFailure", // F09-009
        "options:Environment.Trust", // F09-008
        "project.addExistingProject", // F09-009
        "project.addNewFile", // F09-054
        "project.addNewFrame", // F05-006
        "project.addNewInheritedForm", // F05-007
        "project.addNewProject", // F09-009
        "project.addPlatform", // F03-003
        "project.buildAll", // F09-009
        "project.cleanAll", // F09-009
        "project.configurationManager", // F09-004
        "project.dependencies", // F09-010
        "project.moveFile", // F09-054
        "project.moveProjectDown", // F09-009
        "project.moveProjectUp", // F09-009
        "project.newFolder", // F09-054
        "project.rebuildAll", // F09-009
        "project.removeFromGroup", // F09-009
        "project.removePlatform", // F03-003
        "project.resources", // F11-091
        "project.setActive", // F09-009
        "project.setPlatform", // F03-003
        "refactor.undo", // F13-003
        "run.toggleLibraryStepping", // F02-004
        "search.bookmark.addNamed", // F11-045
        "setting:history.enabled", // F11-059
        "templates.edit", // F11-052
        "templates.insert", // F11-052
        "templates.new", // F11-052
        "tools.compareFiles", // F12-014
        "toolwindow:Bookmarks", // F11-045
        "toolwindow:History", // F11-060
        "toolwindow:Templates", // F11-052
        "view.bookmarks", // F11-045
        "view.history", // F11-060
        "view.templates", // F11-052
    ];

    private static readonly FrozenSet<string> Shipped = DeliveredIds.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The set of a test scope (<see cref="WithDelivered"/>); it follows the test's own execution flow only.</summary>
    private static readonly AsyncLocal<IReadOnlySet<string>?> Scoped = new();

    /// <summary>The delivered ids (ordinal comparison).</summary>
    public static IReadOnlySet<string> Delivered => Scoped.Value ?? Shipped;

    /// <summary>
    /// IDE-P8-003: whether the item <paramref name="id"/> of phase <paramref name="phase"/> is treated as implemented by a
    /// composition of <paramref name="implementedPhase"/> (the value tests may lower: below 7 no phase-8 item is available).
    /// </summary>
    public static bool IsAvailable(string id, int phase, int implementedPhase) =>
        phase <= implementedPhase || (phase == 8 && implementedPhase >= 7 && Delivered.Contains(id));

    /// <summary>Test seam: until the result is disposed, <see cref="Delivered"/> is exactly <paramref name="ids"/> for the calling flow.</summary>
    internal static IDisposable WithDelivered(params string[] ids)
    {
        var scope = new Scope(Scoped.Value);
        Scoped.Value = ids.ToFrozenSet(StringComparer.Ordinal);
        return scope;
    }

    private sealed class Scope(IReadOnlySet<string>? previous) : IDisposable
    {
        public void Dispose() => Scoped.Value = previous;
    }
}
