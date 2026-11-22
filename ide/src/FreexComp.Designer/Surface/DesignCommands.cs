namespace FreexComp.Designer.Surface;

/// <summary>The command ids the design surface executes (design-surface.md §3, §9, §12; docs/spec/ui command ids).</summary>
public static class Commands
{
    public const string SelectAll = "edit.selectAll";

    public const string SelectParent = "designer.selectParent";

    public const string SelectNext = "designer.selectNext";

    public const string SelectPrevious = "designer.selectPrevious";

    public const string Undo = "edit.undo";

    public const string Redo = "edit.redo";
}

/// <summary>Resource keys of the undo-unit titles (design-surface.md DSG-SUR-172: <c>Undo_Designer_&lt;Kind&gt;</c>).</summary>
public static class UndoTitles
{
    public const string Move = "Undo_Designer_Move";
}
