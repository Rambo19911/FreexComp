using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Layout;

/// <summary>
/// Keeps the stored = computed invariant (layout-engine.md DSG-LAY-040): stores the computed bounds of every visual
/// component whose stored bounds differ, as ordinary <see cref="SetProperty"/> changes of the caller's undo unit.
/// </summary>
public static class LayoutCommit
{
    private static readonly string[] Paths = ["Left", "Top", "Width", "Height"];

    /// <summary>
    /// Applies a <c>SetProperty</c> for each bound of <see cref="LayoutResult.Differences"/> whose effective value differs
    /// from the computed one; returns the applied changes and their inverses (in apply order). The form root is not
    /// handled here: its size is the client size, changed only by the form resize (design-surface.md DSG-SUR-085).
    /// </summary>
    public static (IReadOnlyList<FormChange> Redo, IReadOnlyList<FormChange> Undo) StoreDifferences(FormEditor editor, LayoutResult result)
    {
        var changes = new List<FormChange>();
        foreach (var (obj, _, computed) in result.Differences())
        {
            if (obj.Parent is null)
            {
                continue;
            }

            int[] values = [computed.Left, computed.Top, computed.Width, computed.Height];
            for (var i = 0; i < Paths.Length; i++)
            {
                if (PropertyAccess.GetInt(obj, Paths[i]) != values[i] || !PropertyAccess.IsStored(obj, Paths[i]))
                {
                    changes.Add(new SetProperty(obj.QualifiedName, Paths[i], new IntegerValue(values[i], false)));
                }
            }
        }

        return changes.Count == 0 ? ([], []) : (changes, editor.Apply(changes));
    }
}
