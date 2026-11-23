namespace FreexComp.Designer.Surface;

/// <summary>The read-only form (design-surface.md DSG-SUR-003; code-sync.md §8).</summary>
public sealed partial class DesignSurfaceController
{
    private bool _isReadOnly;

    /// <summary>
    /// DSG-SUR-003: only selection, lasso, Copy and the context menu work. Set by the IDE from the code-sync state
    /// (code-sync.md §8); becoming read-only cancels a drag in progress (DSG-CS-081) and leaves tab-order mode.
    /// </summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (_isReadOnly == value)
            {
                return;
            }

            _isReadOnly = value;
            if (value)
            {
                ResetPress();
                ExitTabOrderMode();
            }

            Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The commands that do not change the form (DSG-SUR-003): selection and Copy.</summary>
    private static bool IsReadOnlySafe(string commandId) =>
        commandId is Commands.SelectAll or Commands.SelectNext or Commands.SelectPrevious or Commands.SelectParent or Commands.Copy;

    /// <summary>
    /// DSG-SUR-004: an attempt to move, resize or edit a preserved component shows <c>Msg.Designer.ReadOnlyItem</c> (the first
    /// preserved one selected); true when one was selected.
    /// </summary>
    private bool ReportPreserved()
    {
        if (Selection.Items.FirstOrDefault(o => o.IsPreserved && o.Parent is not null) is not { } preserved)
        {
            return false;
        }

        _host.SetStatus(StatusIds.ReadOnlyItem, preserved.Name);
        return true;
    }
}
