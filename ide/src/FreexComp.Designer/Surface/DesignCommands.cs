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

    /// <summary>DSG-SUR-192 (arg <c>Class[;Prop=Value…]</c>).</summary>
    public const string NewChild = "designer.newChild";

    public const string DeletePage = "designer.deletePage";

    public const string NextPage = "designer.nextPage";

    public const string PreviousPage = "designer.previousPage";

    /// <summary>DSG-SUR-195 (arg = the collection property).</summary>
    public const string EditCollection = "designer.editCollection";

    public const string EditComponent = "designer.editComponent";

    /// <summary>DSG-SUR-229: removes the overrides of the selected inherited objects and instances.</summary>
    public const string RevertToInherited = "designer.revertToInherited";

    /// <summary>The commands a metadata verb may name (component-metadata.md §7); other verbs are not shown.</summary>
    public static IReadOnlyList<string> Verbs { get; } = [NewChild, DeletePage, NextPage, PreviousPage, EditCollection, EditComponent];

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

    /// <summary>A drop in the Structure window's Design view (structure-tree.md UI-TW-1613, "Move in Structure").</summary>
    public const string MoveInStructure = "Undo_Designer_MoveInStructure";

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

    /// <summary>An Object Inspector value commit (object-inspector.md §9).</summary>
    public const string Property = "Undo_Designer_Property";

    /// <summary>An Object Inspector <c>Name</c> commit (object-inspector.md §7).</summary>
    public const string Rename = "Undo_Designer_Rename";
    public const string RevertToInherited = "Undo_Designer_RevertToInherited";
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

    /// <summary>DSG-CS-101: a designer undo left code the user had changed.</summary>
    public const string UndoCodeSkipped = "Msg.Designer.UndoCodeSkipped";

    /// <summary>DSG-CS-004: a code-sync edit was refused (it would add syntax errors); {0} = the reason.</summary>
    public const string CodeSyncConflict = "Msg.Designer.CodeSyncConflict";
    public const string FrameChildFixed = "Msg.Designer.FrameChildFixed";
    public const string FrameCycle = "Msg.Designer.FrameCycle";

    /// <summary>DSG-SUR-238: a structural change of an inherited component of a descendant; {0} = the ancestor class.</summary>
    public const string InheritedComponentFixed = "Msg.Designer.InheritedComponentFixed";

    /// <summary>DSG-SUR-241: send to back stopped after the inherited components.</summary>
    public const string InheritedOrderKept = "Msg.Designer.InheritedOrderKept";
}
