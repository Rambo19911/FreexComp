using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Editing;

/// <summary>
/// The designer's tab-order bookkeeping (design-surface.md DSG-SUR-091, DSG-SUR-096; storage and load rule:
/// form-format.md §2.3, ui-framework core.md UIF-102): the focusable children of each parent hold explicit
/// <c>TabOrder</c> values 0 … n−1.
/// </summary>
public static class TabOrderRules
{
    /// <summary>DSG-SUR-091: metadata kind <c>wincontrol</c> or <c>container</c>, a <c>TabOrder</c> property, not preserved.</summary>
    public static bool IsFocusable(FormObject obj) =>
        obj.Parent is not null && !obj.IsPreserved
        && obj.Info is { Kind: ComponentKind.WinControl or ComponentKind.Container } info && info.FindProperty("TabOrder") is not null;

    /// <summary>
    /// The effective tab sequence of <paramref name="parent"/>'s focusable children, as the runtime builds it (UIF-102):
    /// explicit values (≥ 0) first, sorted by value with ties in creation order, then the unassigned ones in creation order.
    /// </summary>
    public static IReadOnlyList<FormObject> Sequence(FormObject parent) =>
        parent.Children.Where(IsFocusable)
            .Select((c, i) => (Child: c, Index: i, Value: PropertyAccess.GetInt(c, "TabOrder")))
            .OrderBy(e => e.Value >= 0 ? e.Value : int.MaxValue)
            .ThenBy(e => e.Index)
            .Select(e => e.Child)
            .ToList();

    /// <summary>DSG-SUR-096: the <c>TabOrder</c> of a focusable control added to <paramref name="parent"/> (last position).</summary>
    public static int NextValue(FormObject parent) => parent.Children.Count(IsFocusable);

    /// <summary>
    /// The changes that number <paramref name="parent"/>'s effective sequence 0 … n−1 after removing
    /// <paramref name="removed"/> from it and appending <paramref name="appended"/> (in that order): one
    /// <see cref="SetProperty"/> per control whose stored value differs (unassigned controls receive their value,
    /// DSG-SUR-091). Non-focusable objects in either list are ignored. DSG-SUR-240: an inherited control keeps its position — no
    /// change is made for it; only the new controls of a descendant (and every ordinary control) are numbered.
    /// </summary>
    public static IReadOnlyList<FormChange> Renumber(FormObject parent, IEnumerable<FormObject>? removed = null,
        IEnumerable<FormObject>? appended = null)
    {
        var skip = new HashSet<FormObject>(removed ?? [], ReferenceEqualityComparer.Instance);
        var add = (appended ?? []).Where(IsFocusable).ToList();
        foreach (var a in add)
        {
            skip.Add(a);
        }

        return Number([.. Sequence(parent).Where(c => !skip.Contains(c)), .. add], keepInherited: true);
    }

    /// <summary>
    /// One <see cref="SetProperty"/> per control of <paramref name="sequence"/> whose stored <c>TabOrder</c> is not its index;
    /// with <paramref name="keepInherited"/> none for an inherited control (DSG-SUR-240).
    /// </summary>
    public static IReadOnlyList<FormChange> Number(IReadOnlyList<FormObject> sequence, bool keepInherited = false)
    {
        var changes = new List<FormChange>();
        for (var i = 0; i < sequence.Count; i++)
        {
            var c = sequence[i];
            if (!(keepInherited && c.IsInherited) && (!PropertyAccess.IsStored(c, "TabOrder") || PropertyAccess.GetInt(c, "TabOrder") != i))
            {
                changes.Add(new SetProperty(c.QualifiedName, "TabOrder", new IntegerValue(i, false)));
            }
        }

        return changes;
    }
}
