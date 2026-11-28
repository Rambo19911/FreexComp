using System.Globalization;
using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Designer.CodeSync;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// One node of the Menu Designer's tree (dialogs.md §26): the item, its <c>Caption</c> exactly as stored (the <c>&amp;</c>
/// shown), its shortcut text (empty for none), whether it is a separator (<c>Caption = '-'</c>, drawn as a rule) and whether
/// its caption is empty (the component name is shown in <c>text.tertiary</c>), and its sub-items. <paramref name="Warning"/>
/// is the text of the duplicate-access-key warning the item takes part in (DSG-SUR-252 a: the mark and the tooltip), else null.
/// </summary>
public sealed record MenuDesignerNode(FormObject Item, string Name, string Caption, string ShortCut, bool IsSeparator, bool IsCaptionEmpty,
    IReadOnlyList<MenuDesignerNode> Children, string? Warning = null)
{
    /// <summary>The text shown: the stored caption, or the component name when the caption is empty.</summary>
    public string Text => IsCaptionEmpty ? Name : Caption;
}

/// <summary>
/// One duplicate-access-key warning of the Menu Designer (DSG-SUR-251): the items of one level (the children of
/// <paramref name="Parent"/> — the menu component for the top level) that have the access key <paramref name="Key"/>, in
/// level order, and the text <c>Msg.MenuDesigner.DuplicateAccessKey</c>.
/// </summary>
public sealed record MenuDesignerWarning(FormObject Parent, char Key, IReadOnlyList<FormObject> Items, string Text);

/// <summary>
/// One entry of the Menu Designer's <c>Image</c> field (DSG-SUR-258): <c>(None)</c> for −1, a cell of the menu's image list
/// with its picture, or — <paramref name="Cell"/> null — a stored index that the list does not hold, shown as its number alone.
/// </summary>
public sealed record MenuImageChoice(int Index, ImageListCell? Cell)
{
    /// <summary>The index of <c>(None)</c>: the default of <c>ImageIndex</c>.</summary>
    public const int None = -1;

    /// <summary>The text of the entry: <c>(None)</c>, else the number.</summary>
    public string Text => Index == None ? Strings.MenuDesigner_ImageNone : Index.ToString(CultureInfo.CurrentCulture);

    /// <summary>UI-DLG-342: the automation name — "(None)" / "Image {n}".</summary>
    public string AutomationName => Index == None ? Strings.MenuDesigner_ImageNone
        : string.Format(CultureInfo.CurrentCulture, Strings.MenuDesigner_ImageEntryName, Index);
}

/// <summary>Where a moved item of the Menu Designer goes, relative to the target item (DSG-SUR-254).</summary>
public enum MenuDropPosition
{
    /// <summary>Before the target, in the target's level.</summary>
    Before,

    /// <summary>After the target, in the target's level.</summary>
    After,

    /// <summary>Into the target, as its last sub-item (the menu component as target: the end of the top level).</summary>
    Into,
}

/// <summary>
/// The Menu Designer (design-surface.md DSG-SUR-197, DSG-SUR-199; dialogs.md §26; D-098) of a <c>TMainMenu</c> or
/// <c>TPopupMenu</c>: the item tree, New Item / Insert Before / New Sub-Item / New Separator / Delete / Move Up / Move Down,
/// the Caption and ShortCut fields and the embedded property grid (<see cref="Grid"/>, Properties with the Name row and
/// Events). Every operation is applied to the form at once as its own ordinary undo unit including its code-sync edits;
/// there is no Cancel, Close only closes (UI-DLG-252). The surface selection is kept while the dialog works. Items move
/// to another place or level by <see cref="MoveTo"/> (a drag in the view), <see cref="MoveOut"/> and <see cref="MoveIn"/>,
/// each one "Order" unit (DSG-SUR-253…257; D-156); fixed (inherited, preserved) items do not move. The <c>Image</c> field
/// (<see cref="ImageChoices"/>, <see cref="CommitImage"/>) edits the item's <c>ImageIndex</c> with the pictures of the
/// menu's image list (DSG-SUR-258; D-157).
/// </summary>
public sealed class MenuDesignerViewModel : DialogViewModel<bool>
{
    public const string DialogName = "MenuDesigner";

    /// <summary>The class of the items (component-metadata: <c>parentClasses</c> TMenu, TMenuItem).</summary>
    public const string ItemClass = "TMenuItem";

    /// <summary>UIF-727: the name prefix of separators.</summary>
    public const string SeparatorPrefix = "N";

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly FormDesignerViewModel _designer;
    private readonly IDialogService? _dialogs;
    private readonly Layout.ILayoutService? _layout;
    private FormObject? _selected;
    private string _captionText = "";
    private string _shortCutText = "";
    private IReadOnlyList<MenuDesignerWarning> _warnings = [];
    private IReadOnlyList<MenuImageChoice> _imageChoices = [new MenuImageChoice(MenuImageChoice.None, null)];
    private (FormValue? Bitmap, FormValue? Width, FormValue? Height, ImageListData? Data) _imageList; // the last decoded Bitmap value
    private string? _imageTip;
    private bool _imagesReadable;
    private Dictionary<FormObject, MenuDesignerWarning> _warningOf = new(ReferenceEqualityComparer.Instance);

    public MenuDesignerViewModel(FormDesignerViewModel designer, FormObject menu, IDialogService? dialogs = null, Layout.ILayoutService? layout = null)
    {
        ArgumentNullException.ThrowIfNull(designer);
        ArgumentNullException.ThrowIfNull(menu);
        _designer = designer;
        _dialogs = dialogs;
        _layout = layout;
        Menu = menu;
        Title = string.Format(CultureInfo.CurrentCulture, Strings.MenuDesigner_Title, menu.Name);
        Grid = new PropertyGridViewModel(new DesignerGridTarget(() => _designer, () => _selected is { } item ? [item] : [], designer.Status), dialogs);
        CloseButton = new DialogButtonViewModel("Close", Strings.Button_Close, () => Close(true)) { IsDefault = true, IsCancel = true };
        Buttons = [CloseButton];
        designer.ModelChanged += OnModelChanged;
        CloseRequested += (_, _) => designer.ModelChanged -= OnModelChanged;
        Select(menu.Children.FirstOrDefault()); // dialogs.md §26: the first top-level item selected (and expanded)
    }

    public override string Name => DialogName;

    public override string Title { get; }

    public override double Width => 720;

    public override double? Height => 460;

    public override bool CanResize => true;

    /// <summary>UI-DLG-252: the dialog remembers its size between openings (UI state <c>dialogs.MenuDesigner.size</c>), nothing else.</summary>
    public override (double Width, double Height)? StoredSize
    {
        get => StoredDialogSize.Read(_layout, DialogName);
        set => StoredDialogSize.Write(_layout, DialogName, value);
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    /// <summary>Close (the default button; Escape and × are the same, UI-DLG-252).</summary>
    public DialogButtonViewModel CloseButton { get; }

    /// <summary>The <c>TMainMenu</c> / <c>TPopupMenu</c> whose items are edited (UI-DLG-253: the same for both).</summary>
    public FormObject Menu { get; }

    /// <summary>The embedded grid of the selected item (Properties with the Name row, Events; DSG-SUR-197).</summary>
    public PropertyGridViewModel Grid { get; }

    /// <summary>UI-DLG-250: the view moves focus to Caption with its text selected.</summary>
    public event EventHandler? CaptionFocusRequested;

    /// <summary>The tree: the menu's children in creation order, sub-items as children.</summary>
    public IReadOnlyList<MenuDesignerNode> Nodes => [.. Menu.Children.Select(Node)];

    /// <summary>Every item of the tree in display order (pre-order).</summary>
    public IReadOnlyList<FormObject> Items => [.. Menu.Children.SelectMany(Subtree)];

    public bool IsEmpty => Menu.Children.Count == 0;

    public string EmptyText => Strings.MenuDesigner_Empty;

    /// <summary>The selected item; null for none.</summary>
    public FormObject? Selected => _selected;

    /// <summary>
    /// DSG-SUR-251: the duplicate-access-key warnings — one per group of two or more items of a level with the same access
    /// key — the levels in tree pre-order (top level first), within a level by the first item of each group. Computed when
    /// the dialog opens and after every change of the model (also undo / redo). Advisory: nothing is blocked or stored.
    /// </summary>
    public IReadOnlyList<MenuDesignerWarning> Warnings => _warnings;

    /// <summary>DSG-SUR-252 (b): the warning the selected item takes part in, else the first of the list; null without a warning.</summary>
    public MenuDesignerWarning? CurrentWarning =>
        _selected is { } item && _warningOf.TryGetValue(item, out var own) ? own : _warnings.Count > 0 ? _warnings[0] : null;

    /// <summary>
    /// The warning line (DSG-SUR-252 b): the text of <see cref="CurrentWarning"/>, with <i>k</i> &gt; 1 warnings followed by
    /// <c>Msg.MenuDesigner.MoreWarnings</c> with <i>k</i> − 1; empty without a warning (the line is hidden).
    /// </summary>
    public string WarningText => CurrentWarning is not { } current ? ""
        : _warnings.Count > 1 ? current.Text + string.Format(CultureInfo.CurrentCulture, Strings.Msg_MenuDesigner_MoreWarnings, _warnings.Count - 1)
        : current.Text;

    /// <summary>
    /// DSG-SUR-250: the access key of a caption — scanning from the left, <c>&amp;&amp;</c> is a literal ampersand, the first
    /// <c>&amp;</c> followed by another character marks that character (upper case, invariant); null for none (also for a
    /// separator, an empty caption and a <c>&amp;</c> at the end).
    /// </summary>
    public static char? AccessKey(string caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        for (var i = 0; i + 1 < caption.Length; i++)
        {
            if (caption[i] != '&')
            {
                continue;
            }

            if (caption[i + 1] != '&')
            {
                return char.ToUpperInvariant(caption[i + 1]);
            }

            i++; // "&&": both characters are skipped
        }

        return null;
    }

    /// <summary>The plain caption: every single <c>&amp;</c> removed and <c>&amp;&amp;</c> turned into <c>&amp;</c>.</summary>
    public static string PlainCaption(string caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        return caption.Replace("&&", "\0", StringComparison.Ordinal).Replace("&", "", StringComparison.Ordinal).Replace('\0', '&');
    }

    private bool CanChange => _designer.Surface is { IsReadOnly: false } && !Menu.IsPreserved && ItemInfo is not null;

    private ComponentInfo? ItemInfo => _designer.Catalog.Find(ItemClass);

    public bool CanNewItem => CanChange;

    public bool CanInsertBefore => CanChange && _selected is not null;

    /// <summary>A separator cannot get sub-items (dialogs.md §26).</summary>
    public bool CanNewSubItem => CanChange && _selected is not null && !IsSeparator(_selected);

    public bool CanNewSeparator => CanChange;

    public bool CanDelete => CanChange && _selected is not null;

    /// <summary>Not for a fixed item, nor above the inherited items of its level (they come first, DSG-SUR-241).</summary>
    public bool CanMoveUp => CanChange && _selected is { Parent: { } parent } item && !IsFixed(item) && IndexIn(parent, item) > InheritedCount(parent);

    public bool CanMoveDown => CanChange && _selected is { Parent: { } parent } item && !IsFixed(item) && IndexIn(parent, item) < parent.Children.Count - 1;

    /// <summary>DSG-SUR-257: move out — the selected item becomes the sibling directly after its parent; not at the top level.</summary>
    public bool CanMoveOut => _selected is { Parent: { } parent } item && !ReferenceEquals(parent, Menu) && Place(item, parent, MenuDropPosition.After) is not null;

    /// <summary>DSG-SUR-257: move in — the selected item becomes the last sub-item of the sibling before it; not without one, nor into a separator.</summary>
    public bool CanMoveIn => _selected is { Parent: { } parent } item && IndexIn(parent, item) is > 0 and var index
        && Place(item, parent.Children[index - 1], MenuDropPosition.Into) is not null;

    /// <summary>Caption is disabled without a selection.</summary>
    public bool IsCaptionEnabled => CanChange && _selected is not null;

    /// <summary>ShortCut is disabled without a selection, for items with sub-items and for a separator (DSG-SUR-259 b).</summary>
    public bool IsShortCutEnabled => CanChange && _selected is { Children.Count: 0 } item && !IsSeparator(item);

    /// <summary>
    /// DSG-SUR-258: the entries of the <c>Image</c> field for the selected item — <c>(None)</c>, one entry per cell of the
    /// menu's image source, and one more with the number alone when the item's stored <c>ImageIndex</c> is not among them.
    /// Computed when the selection changes and after every model change; the same list while nothing of it changed.
    /// </summary>
    public IReadOnlyList<MenuImageChoice> ImageChoices => _imageChoices;

    /// <summary>The entry of the selected item's stored <c>ImageIndex</c> (<c>(None)</c> for −1 and without a selection).</summary>
    public MenuImageChoice SelectedImage => _imageChoices.FirstOrDefault(c => c.Index == StoredImageIndex) ?? _imageChoices[0];

    /// <summary>
    /// Image is enabled when the form can be changed, an item is selected, it is not a separator and the menu's
    /// <c>Images</c> names a readable image source (DSG-SUR-258).
    /// </summary>
    public bool IsImageEnabled => CanChange && _selected is { } item && !IsSeparator(item) && _imagesReadable;

    /// <summary>
    /// Why Image shows no pictures: <c>Msg.MenuDesigner.ImagesNotAssigned</c> without an <c>Images</c> value,
    /// <c>Msg.MenuDesigner.ImagesUnavailable</c> for a value that is no readable source; else null — also for a separator,
    /// which has no image whatever the source is.
    /// </summary>
    public string? ImageTip => _selected is { } item && IsSeparator(item) ? null : _imageTip;

    /// <summary>The Caption field's text (committed by <see cref="CommitCaption"/>: Enter, focus loss).</summary>
    public string CaptionText
    {
        get => _captionText;
        set => SetProperty(ref _captionText, value ?? "");
    }

    /// <summary>The ShortCut field's text (gesture text, <c>(None)</c> for 0).</summary>
    public string ShortCutText
    {
        get => _shortCutText;
        set => SetProperty(ref _shortCutText, value ?? "");
    }

    /// <summary>The ShortCut drop-down (object-inspector.md §5.1).</summary>
    public IReadOnlyList<string> ShortCutChoices => PropertyGridViewModel.ShortCutChoices;

    /// <summary>The selection of the tree (a click, the arrow keys).</summary>
    public void Select(FormObject? item)
    {
        if (item is not null && !Subtree(Menu).Contains(item))
        {
            item = null;
        }

        if (ReferenceEquals(item, _selected) && item is not null)
        {
            return;
        }

        _selected = item;
        LoadFields();
        Grid.SelectionChanged();
        Changed();
    }

    /// <summary>Selects the item of that name (a tree node).</summary>
    public void Select(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Select(Subtree(Menu).Skip(1).FirstOrDefault(o => Names.Equals(o.Name, name)));
    }

    // ---- operations (DSG-SUR-197) -----------------------------------------------------------------------------------

    /// <summary>New Item: after the selected item at the same level; with nothing selected appended to the top level.</summary>
    public void NewItem()
    {
        if (CanNewItem && Add(_selected?.Parent ?? Menu, _selected is { Parent: { } p } s ? IndexIn(p, s) + 1 : Menu.Children.Count, separator: false))
        {
            CaptionFocusRequested?.Invoke(this, EventArgs.Empty); // UI-DLG-250
        }
    }

    /// <summary>Insert Before: before the selected item at the same level.</summary>
    public void InsertBefore()
    {
        if (CanInsertBefore && Add(_selected!.Parent!, IndexIn(_selected.Parent!, _selected), separator: false))
        {
            CaptionFocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>New Sub-Item: appended to the selected item's children.</summary>
    public void NewSubItem()
    {
        if (CanNewSubItem && Add(_selected!, _selected!.Children.Count, separator: false))
        {
            CaptionFocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>New Separator: as New Item with <c>Caption = '-'</c>, named <c>N&lt;k&gt;</c>; focus stays in the tree (UI-DLG-250).</summary>
    public void NewSeparator()
    {
        if (CanNewSeparator)
        {
            Add(_selected?.Parent ?? Menu, _selected is { Parent: { } p } s ? IndexIn(p, s) + 1 : Menu.Children.Count, separator: true);
        }
    }

    private bool Add(FormObject parent, int index, bool separator)
    {
        var surface = _designer.Surface!;
        IReadOnlyList<(string, FormValue)> properties = separator ? [("Caption", new StringValue("-"))] : [];
        var name = separator ? surface.FreeComponentName(SeparatorPrefix) : null;
        if (surface.CreateChild(parent, ItemInfo!, index, properties, select: false, name) is not { } created)
        {
            return false; // code sync refused (DSG-TP-053): the status bar says why
        }

        Select(created);
        return true;
    }

    /// <summary>
    /// Delete: the selected item and its sub-items as one "Delete" unit (with their fields); an item with sub-items asks
    /// <c>Msg.MenuDesigner.DeleteWithSubItems</c> first (UI-DLG-251, Cancel is the default). The following sibling, else the
    /// preceding one, else the parent item is selected next.
    /// </summary>
    public async Task DeleteAsync()
    {
        if (!CanDelete || _selected is not { Parent: { } parent } item)
        {
            return;
        }

        var descendants = Subtree(item).Count() - 1;
        if (descendants > 0)
        {
            if (_dialogs is null)
            {
                return;
            }

            var caption = item.FindProperty("Caption")?.Value is StringValue { Value: { Length: > 0 } text } ? text : item.Name;
            var answer = await _dialogs.ShowMessageAsync(new MessageRequest("Msg.MenuDesigner.DeleteWithSubItems",
                [caption, descendants.ToString(CultureInfo.CurrentCulture)]));
            if (answer != "Delete" || !ReferenceEquals(_selected, item))
            {
                return;
            }
        }

        var siblings = parent.Children;
        var index = IndexIn(parent, item);
        var next = index + 1 < siblings.Count ? siblings[index + 1] : index > 0 ? siblings[index - 1] : ReferenceEquals(parent, Menu) ? null : parent;
        var surfaceSelection = _designer.Surface!.Selection.Primary;
        var keep = Subtree(item).Contains(surfaceSelection) ? Menu : surfaceSelection;
        if (_designer.Surface.DeleteObjects([item], keep))
        {
            Select(next);
        }
    }

    /// <summary>Move Up: one place towards the first sibling ("Order" unit).</summary>
    public void MoveUp() => Move(-1);

    /// <summary>Move Down: one place towards the last sibling ("Order" unit).</summary>
    public void MoveDown() => Move(+1);

    private void Move(int delta)
    {
        if (!(delta < 0 ? CanMoveUp : CanMoveDown) || _selected is not { Parent: { } parent } item)
        {
            return;
        }

        if (_designer.Surface!.Commit(UndoTitles.Order, [new MoveObject(item.Name, parent.Name, IndexIn(parent, item) + delta)]))
        {
            Changed();
        }
    }

    /// <summary>
    /// DSG-SUR-253: the item can be moved (dragged) — the dialog can change the form and the item is not a fixed one
    /// (inherited from an ancestor form or a frame definition, or preserved).
    /// </summary>
    public bool CanMove(FormObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return CanChange && !ReferenceEquals(item, Menu) && Subtree(Menu).Contains(item) && !IsFixed(item);
    }

    /// <summary>
    /// DSG-SUR-254: the drop position in the row of <paramref name="target"/> at <paramref name="fraction"/> of its height
    /// (0 = upper edge, 1 = lower edge): upper quarter before, lower quarter after, the middle half into; a separator row has
    /// no "into" — upper half before, lower half after.
    /// </summary>
    public static MenuDropPosition DropPositionAt(FormObject target, double fraction)
    {
        ArgumentNullException.ThrowIfNull(target);
        return IsSeparator(target) ? fraction < 0.5 ? MenuDropPosition.Before : MenuDropPosition.After
            : fraction < 0.25 ? MenuDropPosition.Before
            : fraction >= 0.75 ? MenuDropPosition.After
            : MenuDropPosition.Into;
    }

    /// <summary>
    /// DSG-SUR-255: <paramref name="item"/> may be dropped at <paramref name="position"/> of <paramref name="target"/> —
    /// unless the new parent is the item or one of its sub-items, or the position is into a separator. The menu component as
    /// target takes only <see cref="MenuDropPosition.Into"/> (the end of the top level). The item's present place is valid.
    /// </summary>
    public bool CanMoveTo(FormObject item, FormObject target, MenuDropPosition position)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(target);
        if (!CanMove(item) || !Subtree(Menu).Contains(target))
        {
            return false;
        }

        var parent = position == MenuDropPosition.Into ? target : ReferenceEquals(target, Menu) ? null : target.Parent;
        if (parent is null || (position == MenuDropPosition.Into && IsSeparator(target)))
        {
            return false;
        }

        return !Subtree(item).Contains(parent);
    }

    /// <summary>
    /// DSG-SUR-256: moves <paramref name="item"/> with its sub-items to <paramref name="position"/> of
    /// <paramref name="target"/> as one "Order" unit with one <see cref="MoveObject"/>; the moved item is selected. False —
    /// nothing recorded — when the position is not valid, is the item's present place, or the form refuses the move (a place
    /// before the inherited items of the new level, a level that takes no new items; DSG-SUR-255).
    /// </summary>
    public bool MoveTo(FormObject item, FormObject target, MenuDropPosition position)
    {
        if (Place(item, target, position) is not { } place
            || !_designer.Surface!.Commit(UndoTitles.Order, [new MoveObject(item.QualifiedName, place.Parent.QualifiedName, place.Index)]))
        {
            return false;
        }

        if (ReferenceEquals(_selected, item))
        {
            Changed();
        }
        else
        {
            Select(item);
        }

        return true;
    }

    /// <summary>
    /// The new parent and the index in it (counted after the removal, DSG-FD-085) of a move that changes something; null for
    /// a position that is not valid, for the item's present place and for a place the form refuses — before the inherited
    /// items of the new level (DSG-SUR-241) or in a level that takes no new items.
    /// </summary>
    private (FormObject Parent, int Index)? Place(FormObject item, FormObject target, MenuDropPosition position)
    {
        if (!CanMoveTo(item, target, position))
        {
            return null;
        }

        var parent = position == MenuDropPosition.Into ? target : target.Parent!;
        var index = position switch
        {
            MenuDropPosition.Before => IndexIn(parent, target),
            MenuDropPosition.After => IndexIn(parent, target) + 1,
            _ => parent.Children.Count,
        };
        if (ReferenceEquals(item.Parent, parent))
        {
            var current = IndexIn(parent, item);
            index -= current < index ? 1 : 0;
            if (index == current)
            {
                return null; // the present place
            }
        }

        return parent.IsPreserved || !parent.AcceptsNewChildren || index < InheritedCount(parent) ? null : (parent, index);
    }

    /// <summary>Move out (<c>Ctrl+Shift+Left</c>): one "Order" unit.</summary>
    public void MoveOut()
    {
        if (CanMoveOut)
        {
            MoveTo(_selected!, _selected!.Parent!, MenuDropPosition.After);
        }
    }

    /// <summary>Move in (<c>Ctrl+Shift+Right</c>): one "Order" unit.</summary>
    public void MoveIn()
    {
        if (CanMoveIn)
        {
            var parent = _selected!.Parent!;
            MoveTo(_selected, parent.Children[IndexIn(parent, _selected) - 1], MenuDropPosition.Into);
        }
    }

    /// <summary>
    /// Caption (Enter, focus loss): the selected item's <c>Caption</c> as one "Property" unit; while the item still has its
    /// generated name (<c>MenuItem&lt;n&gt;</c>, <c>N&lt;k&gt;</c>) it is renamed in the same unit per UIF-727 — the caption
    /// without non-identifier characters, a numeric suffix when taken; <c>N&lt;k&gt;</c> for a separator. False when refused.
    /// </summary>
    public bool CommitCaption()
    {
        if (!IsCaptionEnabled || _selected is not { } item)
        {
            return false;
        }

        var text = CaptionText;
        var stored = item.FindProperty("Caption")?.Value is StringValue { Value: var value } ? value : "";
        if (text == stored)
        {
            return true;
        }

        FormChange change = text.Length == 0 ? new RemoveProperty(item.Name, "Caption") : new SetProperty(item.Name, "Caption", new StringValue(text));
        bool ok;
        if (IsGeneratedName(item) && DerivedName(item, text) is { } name && !Names.Equals(name, item.Name))
        {
            ok = PropertyGridViewModel.RenameUnit(_designer, item, name, [change], UndoTitles.Property);
        }
        else
        {
            ok = _designer.Surface!.Commit(UndoTitles.Property, [change]);
        }

        LoadFields();
        Changed();
        return ok;
    }

    /// <summary>ShortCut (Enter, a list choice, focus loss): the selected item's <c>ShortCut</c> as one "Property" unit; false when the text is no shortcut.</summary>
    public bool CommitShortCut()
    {
        if (!IsShortCutEnabled || _selected is not { } item)
        {
            return false;
        }

        if (!FreexComp.Designer.Inspector.ShortCutText.TryParse(ShortCutText, out var value))
        {
            LoadFields();
            return false;
        }

        var stored = item.FindProperty("ShortCut")?.Value is IntegerValue { Value: var current } ? current : 0;
        if (value != stored)
        {
            FormChange change = value == 0 ? new RemoveProperty(item.Name, "ShortCut") : new SetProperty(item.Name, "ShortCut", new IntegerValue(value, false));
            _designer.Surface!.Commit(UndoTitles.Property, [change]);
        }

        LoadFields();
        Changed();
        return true;
    }

    /// <summary>
    /// Image (a choice of the list): the selected item's <c>ImageIndex</c> as one "Property" unit — removed for
    /// <c>(None)</c> (−1 is the default); the stored value again records nothing. False when the field is disabled or
    /// <paramref name="index"/> is no entry.
    /// </summary>
    public bool CommitImage(int index)
    {
        if (!IsImageEnabled || _selected is not { } item || !_imageChoices.Any(c => c.Index == index))
        {
            return false;
        }

        if (index != StoredImageIndex)
        {
            FormChange change = index == MenuImageChoice.None ? new RemoveProperty(item.Name, "ImageIndex")
                : new SetProperty(item.Name, "ImageIndex", new IntegerValue(index, false));
            return _designer.Surface!.Commit(UndoTitles.Property, [change]); // the model change brings the field up to date
        }

        return true;
    }

    /// <summary>
    /// Enter or double-click on an item: its default event handler (<c>OnClick</c>) is created and assigned — or the
    /// existing one kept — without leaving the dialog (DSG-SUR-197); the Events page shows the method name.
    /// </summary>
    public void ActivateSelected()
    {
        if (_selected?.Info is not { } info || (info.Events.FirstOrDefault(e => e.IsDefault) ?? info.Events.FirstOrDefault()) is not { } ev)
        {
            return;
        }

        Grid.ActivateEvent(ev.Name, navigate: false);
        Changed();
    }

    // ---- internals --------------------------------------------------------------------------------------------------

    private static bool IsSeparator(FormObject item) => item.FindProperty("Caption")?.Value is StringValue { Value: "-" };

    /// <summary>A fixed item (DSG-SUR-237, 257): its place cannot change — inherited, or (part of) a preserved object.</summary>
    private static bool IsFixed(FormObject item) => item.IsInherited || item.IsPreserved || item.Parent is { IsPreserved: true };

    /// <summary>The inherited items of a level: they come first, and no other item is placed before them (DSG-SUR-241).</summary>
    private static int InheritedCount(FormObject parent) => parent.Children.TakeWhile(c => c.IsInherited).Count();

    /// <summary>The item's name is still one the dialog would generate: <c>&lt;namePrefix&gt;&lt;n&gt;</c> or <c>N&lt;k&gt;</c>.</summary>
    private static bool IsGeneratedName(FormObject item)
    {
        foreach (var prefix in new[] { item.Info?.NamePrefix ?? "MenuItem", SeparatorPrefix })
        {
            if (item.Name.Length > prefix.Length && item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && item.Name[prefix.Length..].All(char.IsAsciiDigit))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// UIF-727: the name a caption gives — its identifier characters (a leading digit is dropped until a letter or
    /// underscore starts it), with the smallest numeric suffix 1, 2, … while the name is taken (a component, a member of
    /// the form class, a reserved word); <c>N&lt;k&gt;</c> for a separator; null (no rename) when nothing remains.
    /// </summary>
    private string? DerivedName(FormObject item, string caption)
    {
        var surface = _designer.Surface!;
        if (caption == "-")
        {
            return IsGeneratedName(item) && item.Name.StartsWith(SeparatorPrefix, StringComparison.OrdinalIgnoreCase)
                   && !item.Name.StartsWith(item.Info?.NamePrefix ?? "MenuItem", StringComparison.OrdinalIgnoreCase)
                ? item.Name
                : surface.FreeComponentName(SeparatorPrefix);
        }

        var stem = new string([.. caption.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_')]).TrimStart("0123456789".ToCharArray());
        if (stem.Length == 0)
        {
            return null;
        }

        stem = stem[..Math.Min(stem.Length, ObjectInspectorViewModel.MaxNameLength - 4)];
        bool Taken(string name) => !Names.Equals(name, item.Name)
            && (_designer.Model!.FindByName(name) is not null || _designer.CodeSync.LookupMember(name) != MemberLookup.None || SyntaxFacts.IsReservedWord(name));
        var candidate = stem;
        for (var n = 1; Taken(candidate); n++)
        {
            candidate = stem + n.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    private MenuDesignerNode Node(FormObject item)
    {
        var caption = Caption(item);
        var shortCut = item.FindProperty("ShortCut")?.Value is IntegerValue { Value: not 0 and var s } ? FreexComp.Designer.Inspector.ShortCutText.Format(s) : "";
        return new MenuDesignerNode(item, item.Name, caption, shortCut, caption == "-", caption.Length == 0, [.. item.Children.Select(Node)],
            _warningOf.TryGetValue(item, out var warning) ? warning.Text : null);
    }

    private static string Caption(FormObject item) => item.FindProperty("Caption")?.Value is StringValue { Value: var caption } ? caption : "";

    /// <summary>The plain caption of an item; for an empty caption — and for the menu component — the component name.</summary>
    private static string PlainCaption(FormObject item) => Caption(item) is { Length: > 0 } caption ? PlainCaption(caption) : item.Name;

    private int StoredImageIndex => _selected?.FindProperty("ImageIndex")?.Value is IntegerValue { Value: var index } ? (int)index : MenuImageChoice.None;

    /// <summary>
    /// DSG-SUR-258: the image source of the menu — its <c>Images</c> value names an image-list component of this form
    /// document whose <c>Bitmap</c> value decodes without error (no <c>Bitmap</c> value: a readable source with no cells) —
    /// and from it the entries of the field and the reason it shows no pictures.
    /// </summary>
    private void ComputeImages()
    {
        var images = Menu.FindProperty("Images")?.Value;
        ImageListData? data = null;
        _imagesReadable = false;
        if (images is IdentValue { Name: var name } && _designer.Model?.FindByName(name) is { Info: { } info } list
            && _designer.Catalog.IsAssignable(info.ClassName, Menu.Info?.FindProperty("Images")?.Type ?? "TCustomImageList"))
        {
            switch (list.FindProperty("Bitmap")?.Value)
            {
                case null:
                    _imagesReadable = true;
                    break;
                case BinaryValue bitmap:
                    var (width, height) = (list.FindProperty("Width")?.Value, list.FindProperty("Height")?.Value);
                    if (!ReferenceEquals(_imageList.Bitmap, bitmap) || !Equals(_imageList.Width, width) || !Equals(_imageList.Height, height))
                    {
                        _imageList = (bitmap, width, height, ImageListData.Decode(list, bitmap).Data); // decoded once per value
                    }

                    data = _imageList.Data;
                    _imagesReadable = data is not null;
                    break;
            }
        }

        _imageTip = _imagesReadable ? null
            : images is null ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_MenuDesigner_ImagesNotAssigned, Menu.Name)
            : string.Format(CultureInfo.CurrentCulture, Strings.Msg_MenuDesigner_ImagesUnavailable, images switch
            {
                IdentValue ident => ident.Name,
                QualifiedIdentValue qualified => string.Join(".", qualified.Parts),
                _ => images.ToString(),
            });

        // The same list while its entries are the same: the view keeps what it made of them (pictures).
        var cells = data?.Cells ?? [];
        var stored = StoredImageIndex;
        var extra = stored != MenuImageChoice.None && (stored < 0 || stored >= cells.Length);
        var current = _imageChoices;
        if (current.Count != 1 + cells.Length + (extra ? 1 : 0) || (extra && current[^1].Index != stored)
            || cells.Where((cell, i) => !ReferenceEquals(current[i + 1].Cell, cell)).Any())
        {
            List<MenuImageChoice> choices = [new(MenuImageChoice.None, null), .. cells.Select((cell, i) => new MenuImageChoice(i, cell))];
            if (extra)
            {
                choices.Add(new MenuImageChoice(stored, null));
            }

            _imageChoices = choices;
        }
    }

    /// <summary>DSG-SUR-251: every level's groups of two or more items with the same access key, the levels in tree pre-order.</summary>
    private void ComputeWarnings()
    {
        var warnings = new List<MenuDesignerWarning>();
        var byItem = new Dictionary<FormObject, MenuDesignerWarning>(ReferenceEqualityComparer.Instance);
        foreach (var parent in Subtree(Menu))
        {
            // GroupBy keeps the order of the first item of each group, and the level order within a group.
            foreach (var group in parent.Children.Select(item => (Item: item, Key: AccessKey(Caption(item)))).Where(e => e.Key is not null)
                         .GroupBy(e => e.Key!.Value).Where(g => g.Count() > 1))
            {
                List<FormObject> items = [.. group.Select(e => e.Item)];
                var warning = new MenuDesignerWarning(parent, group.Key, items, string.Format(CultureInfo.CurrentCulture,
                    Strings.Msg_MenuDesigner_DuplicateAccessKey, group.Key, PlainCaption(parent), string.Join(", ", items.Select(PlainCaption))));
                warnings.Add(warning);
                foreach (var item in items)
                {
                    byItem[item] = warning;
                }
            }
        }

        _warnings = warnings;
        _warningOf = byItem;
    }

    private static int IndexIn(FormObject parent, FormObject child)
    {
        for (var i = 0; i < parent.Children.Count; i++)
        {
            if (ReferenceEquals(parent.Children[i], child))
            {
                return i;
            }
        }

        return -1;
    }

    private static IEnumerable<FormObject> Subtree(FormObject obj) => obj.Children.SelectMany(Subtree).Prepend(obj);

    private void LoadFields()
    {
        CaptionText = _selected?.FindProperty("Caption")?.Value is StringValue { Value: var caption } ? caption : "";
        ShortCutText = _selected is null ? "" : FreexComp.Designer.Inspector.ShortCutText.Format(
            _selected.FindProperty("ShortCut")?.Value is IntegerValue { Value: var value } ? value : 0);
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (_selected is not null && !Subtree(Menu).Contains(_selected))
        {
            _selected = null; // undone behind the dialog
            Grid.SelectionChanged();
        }

        LoadFields();
        Changed();
    }

    private void Changed()
    {
        ComputeWarnings();
        ComputeImages();
        OnPropertyChanged(nameof(ImageChoices));
        OnPropertyChanged(nameof(SelectedImage));
        OnPropertyChanged(nameof(IsImageEnabled));
        OnPropertyChanged(nameof(ImageTip));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(CurrentWarning));
        OnPropertyChanged(nameof(WarningText));
        OnPropertyChanged(nameof(Nodes));
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(CanNewItem));
        OnPropertyChanged(nameof(CanInsertBefore));
        OnPropertyChanged(nameof(CanNewSubItem));
        OnPropertyChanged(nameof(CanNewSeparator));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanMoveOut));
        OnPropertyChanged(nameof(CanMoveIn));
        OnPropertyChanged(nameof(IsCaptionEnabled));
        OnPropertyChanged(nameof(IsShortCutEnabled));
        Grid.Refresh();
    }
}
