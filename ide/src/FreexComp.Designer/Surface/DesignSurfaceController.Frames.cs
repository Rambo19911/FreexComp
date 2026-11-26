using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Surface;

/// <summary>
/// Frame instances and descendant forms on the design surface (frames-designer.md §6, DSG-SUR-225…233;
/// form-inheritance-designer.md §5, DSG-SUR-237…245).
/// </summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>
    /// DSG-SUR-227, 237 (a) "no structural change": an inherited object — a component of a frame shown in an instance or a
    /// component of a descendant's ancestor — is not deleted, cut, renamed, re-parented or reordered.
    /// </summary>
    internal static bool IsStructureFixed(FormObject obj) => obj.IsInherited;

    /// <summary>
    /// DSG-SUR-227, 237 (b) "no new children": a frame instance and every object inside one is no placement, paste or drop
    /// target; the inherited root and the other inherited objects of a descendant accept new components.
    /// </summary>
    internal static bool RefusesNewChildren(FormObject obj) => !obj.AcceptsNewChildren;

    /// <summary>
    /// DSG-SUR-227, 237: the object a command is refused for and whether the refusal is a "no new children" one; null when
    /// the command is not refused. A structure command with an inherited object in the selection is refused for all.
    /// </summary>
    private (FormObject Object, bool NewChildren)? Refusal(string commandId)
    {
        switch (commandId)
        {
            case Commands.Delete or Commands.Cut or Commands.BringToFront or Commands.SendToBack:
                return Selection.Items.FirstOrDefault(IsStructureFixed) is { } fixedObject ? (fixedObject, false) : null;
            case Commands.NewChild:
                return VerbComponent is { } parent && RefusesNewChildren(parent) ? (parent, true) : null;
            case Commands.DeletePage:
                return VerbComponent is { Info: { } info } pageControl && IsPageControl(info) && Pages(pageControl) is [var first, ..]
                    && (PageControlRenderer.ActiveSheet(pageControl) ?? first) is { IsInherited: true } page
                    ? (page, false)
                    : null;
            default:
                return null;
        }
    }

    /// <summary>DSG-SUR-227, 237: the command is refused for the selection (see <see cref="Refusal"/>).</summary>
    private bool RefusedInFrame(string commandId) => Refusal(commandId) is not null;

    /// <summary>The status message of a refused command (<see cref="Refusal"/>); always true (the key or command was handled).</summary>
    private bool ReportRefusal(string commandId)
    {
        var (obj, newChildren) = Refusal(commandId)!.Value;
        return ReportFixed(obj, newChildren);
    }

    /// <summary>DSG-SUR-227, 238: the status message of <see cref="FixedMessage"/>; always true.</summary>
    private bool ReportFixed(FormObject obj, bool newChildren)
    {
        var (messageId, className) = FixedMessage(obj, newChildren);
        _host.SetStatus(messageId, className);
        return true;
    }

    /// <summary>
    /// DSG-SUR-238: the message of a change refused for <paramref name="obj"/>. A structural change of a component that comes
    /// from the descendant's ancestor — an inherited object outside every instance, an inherited instance, or a component
    /// inside an inherited instance — is <c>Msg.Designer.InheritedComponentFixed</c> naming the ancestor class; a "no new
    /// children" refusal (<paramref name="newChildren"/>) and every refusal inside an instance the file creates is
    /// <c>Msg.Designer.FrameChildFixed</c> naming the frame (DSG-SUR-227).
    /// </summary>
    public static (string MessageId, string ClassName) FixedMessage(FormObject obj, bool newChildren = false)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (!newChildren)
        {
            for (var p = obj; p is not null; p = p.Parent)
            {
                if (p.IsInherited && p.Scope is null && p.InheritedSource is { } source)
                {
                    return (StatusIds.InheritedComponentFixed, source.ClassName);
                }
            }
        }

        return (StatusIds.FrameChildFixed, FrameClassOf(obj));
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
    /// DSG-SUR-229, 242: for each selected inherited object and inherited root its own overrides; for each selected instance
    /// every override line of its inherited objects (nested instances included) and its own lines except
    /// <see cref="InstanceKeeps"/>. Preserved blocks (<c>F0109</c>, <c>F0110</c>) are not overrides and stay.
    /// </summary>
    private List<FormChange> RevertChanges()
    {
        var lines = new List<(FormObject Object, string Path)>();
        foreach (var obj in Selection.Items)
        {
            if (obj.IsInherited || obj.IsInheritedRoot) // DSG-SUR-242: the root of a descendant too, not its children
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
