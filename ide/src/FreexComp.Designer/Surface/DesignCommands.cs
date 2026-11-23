namespace FreexComp.Designer.Surface;

/// <summary>The command ids the design surface executes (design-surface.md §3, §9, §12; docs/spec/ui command ids).</summary>
public static class Commands
{
    public const string SelectAll = "edit.selectAll";

    public const string SelectParent = "designer.selectParent";

    public const string SelectNext = "designer.selectNext";

    public const string SelectPrevious = "designer.selectPrevious";

    public const string TabOrder = "designer.tabOrder";

    public const string Delete = "edit.delete";

    public const string Copy = "edit.copy";

    public const string Cut = "edit.cut";

    public const string Paste = "edit.paste";

    public const string Undo = "edit.undo";

    public const string Redo = "edit.redo";

    public const string AlignLeft = "designer.alignLeft";

    public const string AlignRight = "designer.alignRight";

    public const string AlignTop = "designer.alignTop";

    public const string AlignBottom = "designer.alignBottom";

    public const string AlignHCenter = "designer.alignHCenter";

    public const string AlignVCenter = "designer.alignVCenter";

    public const string SameWidth = "designer.sameWidth";

    public const string SameHeight = "designer.sameHeight";

    public const string SameSize = "designer.sameSize";

    public const string CenterHInParent = "designer.centerHInParent";

    public const string CenterVInParent = "designer.centerVInParent";

    public const string SpaceHEqually = "designer.spaceHEqually";

    public const string SpaceVEqually = "designer.spaceVEqually";

    public const string AlignToGrid = "designer.alignToGrid";

    public const string SizeToGrid = "designer.sizeToGrid";

    public const string BringToFront = "designer.bringToFront";

    public const string SendToBack = "designer.sendToBack";

    public const string CreateDefaultHandler = "designer.createDefaultHandler";

    /// <summary>The commands of design-surface.md §11.1.</summary>
    public static IReadOnlyList<string> Arrange { get; } =
    [
        AlignLeft, AlignRight, AlignTop, AlignBottom, AlignHCenter, AlignVCenter, SameWidth, SameHeight, SameSize,
        CenterHInParent, CenterVInParent, SpaceHEqually, SpaceVEqually, AlignToGrid, SizeToGrid,
    ];
}

/// <summary>Resource keys of the undo-unit titles (design-surface.md DSG-SUR-172: <c>Undo_Designer_&lt;Kind&gt;</c>).</summary>
public static class UndoTitles
{
    public const string Move = "Undo_Designer_Move";

    public const string Resize = "Undo_Designer_Resize";

    public const string TabOrder = "Undo_Designer_TabOrder";

    public const string Delete = "Undo_Designer_Delete";

    public const string Paste = "Undo_Designer_Paste";

    public const string Cut = "Undo_Designer_Cut";

    public const string Copy = "Undo_Designer_Copy";

    public const string Align = "Undo_Designer_Align";

    public const string Size = "Undo_Designer_Size";

    public const string Order = "Undo_Designer_Order";

    /// <summary>Palette placement ("Add {ClassName}"; the class name is the title's argument, <see cref="FreexComp.Designer.Editing.DesignerUndoUnit.TitleArgument"/>).</summary>
    public const string Add = "Undo_Designer_Add";

    public const string Event = "Undo_Designer_Event";
}

/// <summary>Status message ids the surface shows (states-and-messages.md; design-surface.md §15).</summary>
public static class StatusIds
{
    public const string Locked = "Msg.Designer.Locked";

    public const string AlignedCannotMove = "Msg.Designer.AlignedCannotMove";

    public const string ReadOnlyItem = "Msg.Designer.ReadOnlyItem";

    public const string PasteNothing = "Msg.Designer.PasteNothing";

    public const string PasteUnknownClass = "Msg.Designer.PasteUnknownClass";

    public const string CannotDropHere = "Msg.Designer.CannotDropHere";
}
