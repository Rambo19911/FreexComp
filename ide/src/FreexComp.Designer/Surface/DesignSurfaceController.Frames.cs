using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>Frame instances on the design surface (frames-designer.md §6, DSG-SUR-225…233).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>
    /// DSG-SUR-227: an instance and the objects inherited into it keep the frame's structure — nothing is added to them,
    /// removed from them or reordered; they are never drop or paste targets.
    /// </summary>
    internal static bool HasFixedStructure(FormObject obj) => obj.IsInherited || obj.IsFrameInstance;

    /// <summary>DSG-SUR-227: the commands that change the structure of the selected components.</summary>
    private static bool ChangesStructure(string commandId) =>
        commandId is Commands.Delete or Commands.Cut or Commands.BringToFront or Commands.SendToBack or Commands.NewChild or Commands.DeletePage;

    /// <summary>DSG-SUR-227: a structure command with inherited objects in the selection — disabled for the whole selection.</summary>
    private bool RefusedInFrame(string commandId) => ChangesStructure(commandId) && Selection.Items.Any(o => o.IsInherited);

    /// <summary>
    /// DSG-SUR-227: <c>Msg.Designer.FrameChildFixed</c> naming the frame whose component <paramref name="obj"/> is (the first
    /// inherited object of the selection when null); always true (the key or command was handled by refusing it).
    /// </summary>
    private bool ReportFrameChildFixed(FormObject? obj = null)
    {
        obj ??= Selection.Items.First(o => o.IsInherited);
        _host.SetStatus(StatusIds.FrameChildFixed, FrameClassOf(obj));
        return true;
    }

    /// <summary>
    /// DSG-SUR-232: an instance of a frame that is this document's class or contains it (directly or through other
    /// frames) is not placed; <c>Msg.Designer.FrameCycle</c> names the frame. True when refused.
    /// </summary>
    private bool RefusedAsCycle(FormObject obj)
    {
        if (obj.Kind != FormObjectKind.Inline || Document.Frames is not { } frames
            || FrameInstances.RepeatedClass(frames, obj.TypeName, Document.Root.TypeName) is null)
        {
            return false;
        }

        _host.SetStatus(StatusIds.FrameCycle, obj.TypeName);
        return true;
    }

    /// <summary>The lines an instance keeps on Revert to Inherited (DSG-SUR-229): its bounds and its tab position.</summary>
    private static readonly string[] InstanceKeeps = ["Left", "Top", "Width", "Height", "TabOrder"];

    /// <summary>
    /// DSG-SUR-229: for each selected inherited object its own overrides; for each selected instance every override line of
    /// its inherited objects (nested instances included) and its own lines except <see cref="InstanceKeeps"/>. Preserved
    /// blocks (<c>F0109</c>, <c>F0110</c>) are not overrides and stay.
    /// </summary>
    private List<FormChange> RevertChanges()
    {
        var lines = new List<(FormObject Object, string Path)>();
        foreach (var obj in Selection.Items)
        {
            if (obj.IsInherited)
            {
                lines.AddRange(obj.Properties.Select(p => (obj, p.Path)));
            }

            if (obj.IsFrameInstance)
            {
                lines.AddRange(obj.Properties.Where(p => !obj.IsInherited && !InstanceKeeps.Contains(p.Path, StringComparer.OrdinalIgnoreCase))
                    .Select(p => (obj, p.Path)));
                foreach (var inherited in obj.Children.SelectMany(Subtree))
                {
                    lines.AddRange(inherited.Properties.Select(p => (inherited, p.Path)));
                }
            }
        }

        return lines.DistinctBy(l => (l.Object, l.Path.ToUpperInvariant()))
            .Select(l => (FormChange)new RemoveProperty(l.Object.QualifiedName, l.Path))
            .ToList();
    }

    /// <summary>DSG-SUR-229: one "Revert to Inherited" unit with the removals and the layout commit; the selection stays.</summary>
    private void RevertToInherited()
    {
        var changes = RevertChanges();

        // An event override that goes leaves its form method unreferenced: a removal candidate like any cleared handler (DSG-CS-071).
        foreach (var remove in changes.OfType<RemoveProperty>())
        {
            if (Document.FindByQualifiedName(remove.ObjectName) is { } obj && obj.Info?.FindEvent(remove.Path) is not null
                && obj.FindProperty(remove.Path)?.Value is IdentValue { Name: var method })
            {
                _host.CodeSync.NoteHandlerCandidate(method);
            }
        }

        var selection = Selection.ToSnapshot();
        var undo = ApplyEdit(changes);
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        PushUnit(new DesignerUndoUnit(UndoTitles.RevertToInherited, [.. changes, .. commitRedo], [.. commitUndo, .. undo], [], selection,
            selection));
    }

    /// <summary>The class of the frame that owns <paramref name="obj"/>: the frame of its instance, or its own for an instance.</summary>
    private static string FrameClassOf(FormObject obj) =>
        (obj.InheritedFrame ?? obj.Frame)?.ClassName ?? obj.TypeName;
}
