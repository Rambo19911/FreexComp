using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>Tab-order mode (design-surface.md §8, DSG-SUR-090 … 095).</summary>
public sealed partial class DesignSurfaceController
{
    // DSG-SUR-090: the next position per parent in the current numbering pass; the controls clicked in it (UI-DSG-071).
    private readonly Dictionary<FormObject, int> _tabCounters = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<FormObject> _tabClicked = new(ReferenceEqualityComparer.Instance);

    /// <summary>The control under the keyboard focus rectangle in tab-order mode (DSG-SUR-095); null otherwise.</summary>
    public FormObject? TabOrderFocus { get; private set; }

    /// <summary>The controls already clicked in the current numbering pass (their badges are highlighted, UI-DSG-071).</summary>
    public IReadOnlyCollection<FormObject> TabOrderClicked => _tabClicked;

    /// <summary>DSG-SUR-094: the info bar's Restart Numbering — clears the counters of every parent.</summary>
    public void RestartTabOrderNumbering()
    {
        _tabCounters.Clear();
        _tabClicked.Clear();
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>DSG-SUR-094: Done, Escape, the command again, undo/redo or leaving the Design view leave the mode.</summary>
    public void ExitTabOrderMode()
    {
        if (State != InteractionState.TabOrder)
        {
            return;
        }

        State = InteractionState.Idle;
        TabOrderFocus = null;
        RestartTabOrderNumbering();
    }

    /// <summary>DSG-SUR-090: entry sets the selection to { form } and clears the counters; not an undo unit.</summary>
    private void ToggleTabOrderMode()
    {
        if (State == InteractionState.TabOrder)
        {
            ExitTabOrderMode();
            return;
        }

        ResetPress();
        Selection.Select(Document.Root);
        RestartTabOrderNumbering();
        State = InteractionState.TabOrder;
    }

    /// <summary>A left press in tab-order mode: a click on a focusable control numbers it (DSG-SUR-092); anything else does nothing (DSG-SUR-093).</summary>
    private void TabOrderPress(PointerInput input)
    {
        var hit = Current.HitTester.HitTest(input.X, input.Y, [], handleSize: _settings.HandleSize);
        if (hit is { Kind: HitKind.Control, Object: { } obj } && TabOrderRules.IsFocusable(obj))
        {
            TabOrderClick(obj);
        }
    }

    /// <summary>
    /// DSG-SUR-092: the control moves to position k = counter[parent] of its parent's sequence (a parent whose every
    /// position was assigned restarts at 0); the others shift; then counter = k + 1. The changed values are one
    /// "TabOrder" unit (nothing when none changed); unassigned siblings receive their values in it (DSG-SUR-091).
    /// </summary>
    private void TabOrderClick(FormObject control)
    {
        var parent = control.Parent!;
        var sequence = TabOrderRules.Sequence(parent).ToList();
        var k = _tabCounters.GetValueOrDefault(parent);
        if (k >= sequence.Count)
        {
            k = 0;
            _tabClicked.RemoveWhere(o => ReferenceEquals(o.Parent, parent));
        }

        sequence.Remove(control);
        sequence.Insert(k, control);
        _tabCounters[parent] = k + 1;
        _tabClicked.Add(control);
        var changes = TabOrderRules.Number(sequence);
        if (changes.Count > 0)
        {
            var selection = Selection.ToSnapshot();
            var undo = ApplyEdit(changes);
            // DSG-LAY-040 / 041: like every unit, it stores the differences of a loaded form that violates the invariant.
            var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
            PushUnit(new DesignerUndoUnit(UndoTitles.TabOrder, [.. changes, .. commitRedo], [.. commitUndo, .. undo], [], selection, selection));
        }

        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>DSG-SUR-095: Tab / Shift+Tab move the focus rectangle in pre-order (wrapping); Enter / Space click; Escape leaves.</summary>
    private bool TabOrderKey(KeyInput input)
    {
        switch (input.Key)
        {
            case DesignKey.Escape:
                ExitTabOrderMode();
                return true;
            case DesignKey.Tab:
                var cycle = Document.AllObjects().Where(TabOrderRules.IsFocusable).ToList();
                if (cycle.Count > 0)
                {
                    var step = input.Modifiers.HasFlag(InputModifiers.Shift) ? -1 : 1;
                    var index = TabOrderFocus is { } f ? cycle.FindIndex(o => ReferenceEquals(o, f)) : -1;
                    TabOrderFocus = cycle[index < 0 ? (step > 0 ? 0 : cycle.Count - 1) : (index + step + cycle.Count) % cycle.Count];
                    Invalidated?.Invoke(this, EventArgs.Empty);
                }

                return true;
            case DesignKey.Enter or DesignKey.Space:
                if (TabOrderFocus is { } focused && TabOrderRules.IsFocusable(focused))
                {
                    TabOrderClick(focused);
                }

                return true;
            default:
                return false; // nothing else acts in the mode (no nudging, no deleting)
        }
    }
}
