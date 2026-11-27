using FreexComp.Designer.Model;

namespace FreexComp.Designer.Editing;

/// <summary>Design-time rules of the tab control's <c>Tabs</c> and <c>TabIndex</c> (design-renderers.md DSG-RND-024; D-148).</summary>
public static class TabControlRules
{
    /// <summary>
    /// DSG-RND-024: the <c>TabIndex</c> change that joins a <c>Tabs</c> edit of a <c>tabcontrol</c> (in the same undo unit):
    /// old list empty, new list not, and no <c>TabIndex</c> line → <c>TabIndex = 0</c> (the run-time auto-select, UIF-1507);
    /// <c>TabIndex</c> ≥ the new count → <c>count − 1</c>, or the line removed when the list is empty. Null otherwise
    /// (also for every object that is not drawn by the <c>tabcontrol</c> renderer).
    /// </summary>
    public static FormChange? TabIndexAfterTabsEdit(FormObject obj, IReadOnlyList<string> oldTabs, IReadOnlyList<string> newTabs)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(oldTabs);
        ArgumentNullException.ThrowIfNull(newTabs);
        if (obj.Info?.RendererId != "tabcontrol")
        {
            return null;
        }

        var line = PropertyAccess.EffectiveLine(obj, "TabIndex");
        if (line is null)
        {
            return oldTabs.Count == 0 && newTabs.Count > 0 ? Set(obj, 0) : null;
        }

        if (line.Value.Line.Value is not IntegerValue { Value: var index } || index < newTabs.Count)
        {
            return null;
        }

        // An inherited line (frame instance) cannot be removed here: it is overridden with the default instead.
        return newTabs.Count > 0 ? Set(obj, newTabs.Count - 1)
            : obj.FindProperty("TabIndex") is not null ? new RemoveProperty(obj.QualifiedName, "TabIndex")
            : Set(obj, -1);
    }

    private static SetProperty Set(FormObject obj, int index) => new(obj.QualifiedName, "TabIndex", new IntegerValue(index, false));
}
