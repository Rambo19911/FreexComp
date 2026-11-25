using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// One row of the Child List Editor (dialogs.md §27): a child — <c>{Name}</c> and, when the caption is not empty,
/// <c> – {Caption}</c> — or, in a grouped list (DSG-SUR-217), the header row of a category (<see cref="Item"/> null,
/// <see cref="Name"/> the category or <c>(none)</c>, <see cref="Text"/> the category or <c>(No Category)</c>).
/// </summary>
public sealed record ChildListRow(FormObject? Item, string Name, string Text)
{
    /// <summary>A header row: not selectable, no keyboard focus (DSG-SUR-218).</summary>
    public bool IsHeader => Item is null;

    /// <summary>The category value of the row's group (empty: no category); null in a flat list.</summary>
    public string? Category { get; init; }

    /// <summary>The screen-reader name: a child of a grouped list is prefixed with its category (UI-DLG-306).</summary>
    public string AccessibleName { get; init; } = Text;
}

/// <summary>
/// One row of the class picker of New (DSG-SUR-216): a class — <see cref="Text"/> the class name without its leading
/// <c>T</c> — or the header row of a section (<see cref="ClassName"/> null, <see cref="Text"/> the category).
/// </summary>
public sealed record ChildListPickerRow(string? ClassName, string Text, string? Category)
{
    public bool IsHeader => ClassName is null;
}

/// <summary>
/// The Child List Editor (design-surface.md DSG-SUR-198, DSG-SUR-199; dialogs.md §27; D-099) of a component whose class
/// names <c>componentEditor: "childlist"</c> (<c>TActionList</c>): the list of its children — flat, or grouped by their
/// <c>Category</c> value when one has any (DSG-SUR-217) — New (one item class: a button; several: a picker sectioned by
/// the classes' initial category, DSG-SUR-216), Delete (all selected children, one unit), Move Up / Move Down (one selected
/// child, inside its group) and the embedded property grid with Properties and Events pages for the selected children.
/// New creates through the placement path of a palette drop (DSG-SUR-215). Every operation is
/// applied to the form at once as its own ordinary undo unit including its code-sync edits; there is no Cancel, Close only
/// closes (UI-DLG-261). The surface selection is kept while the dialog works. The dialog remembers its size.
/// </summary>
public sealed class ChildListEditorViewModel : DialogViewModel<bool>
{
    public const string DialogName = "ChildListEditor";

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly FormDesignerViewModel _designer;
    private readonly ILayoutService? _layout;
    private List<FormObject> _selection = [];

    public ChildListEditorViewModel(FormDesignerViewModel designer, FormObject component, IDialogService? dialogs = null, ILayoutService? layout = null)
    {
        ArgumentNullException.ThrowIfNull(designer);
        ArgumentNullException.ThrowIfNull(component);
        _designer = designer;
        _layout = layout;
        Component = component;
        Title = string.Format(CultureInfo.CurrentCulture, Strings.ChildListEditor_Title, component.Name);
        PickerRows = Picker(designer.Catalog, FindItemClasses(designer.Catalog, component));
        ItemClasses = [.. PickerRows.Where(r => !r.IsHeader).Select(r => r.ClassName!)];
        Grid = new PropertyGridViewModel(new DesignerGridTarget(() => _designer, () => _selection, designer.Status), dialogs);
        CloseButton = new DialogButtonViewModel("Close", Strings.Button_Close, () => Close(true)) { IsDefault = true, IsCancel = true };
        Buttons = [CloseButton];
        designer.ModelChanged += OnModelChanged;
        CloseRequested += (_, _) => designer.ModelChanged -= OnModelChanged;
        Select(component.Children.Take(1)); // dialogs.md §27: the first child selected
    }

    public override string Name => DialogName;

    public override string Title { get; }

    public override double Width => 640;

    public override double? Height => 400;

    public override bool CanResize => true;

    /// <summary>UI state <c>dialogs.ChildListEditor.size</c> (dialogs.md §27.1).</summary>
    public override (double Width, double Height)? StoredSize
    {
        get => StoredDialogSize.Read(_layout, DialogName);
        set => StoredDialogSize.Write(_layout, DialogName, value);
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    /// <summary>Close (the default button; Escape and × are the same, UI-DLG-261).</summary>
    public DialogButtonViewModel CloseButton { get; }

    /// <summary>The component whose children are edited.</summary>
    public FormObject Component { get; }

    /// <summary>The embedded grid of the selected children (Properties with the Name row, Events).</summary>
    public PropertyGridViewModel Grid { get; }

    /// <summary>
    /// The classes New can create: the component's <c>childClasses</c> when it names any, else every class whose
    /// <c>parentClasses</c> accept it (non-abstract) — in picker order (DSG-SUR-216): first the classes whose metadata
    /// gives <c>Category</c> no <c>initial</c> value, then section by section, each in catalog order.
    /// </summary>
    public IReadOnlyList<string> ItemClasses { get; }

    /// <summary>The picker of New (DSG-SUR-216): the unsectioned classes, then per section its header row and classes.</summary>
    public IReadOnlyList<ChildListPickerRow> PickerRows { get; }

    /// <summary>New is a drop-down of class names (more than one item class).</summary>
    public bool HasClassChoice => ItemClasses.Count > 1;

    /// <summary>The view moves the focus to the grid's row of that property path (New: <c>Caption</c>, else <c>Name</c>).</summary>
    public event EventHandler<string>? GridFocusRequested;

    /// <summary>The key of the no-category group in AutomationIds (DSG-SUR-222).</summary>
    public const string NoCategoryKey = "(none)";

    /// <summary>The list is grouped: at least one child has a non-empty <c>Category</c> value (DSG-SUR-217).</summary>
    public bool IsGrouped => Component.Children.Any(c => CategoryOf(c).Length > 0);

    /// <summary>
    /// The rows: the children in child order (flat list), or — grouped — per category a header row and its children in
    /// child order; groups in order of their first child, the no-category group first (DSG-SUR-217).
    /// </summary>
    public IReadOnlyList<ChildListRow> Rows
    {
        get
        {
            if (!IsGrouped)
            {
                return [.. Component.Children.Select(c => Row(c))];
            }

            var rows = new List<ChildListRow>();
            foreach (var group in Component.Children.GroupBy(CategoryOf, StringComparer.Ordinal).OrderBy(g => g.Key.Length == 0 ? 0 : 1))
            {
                var text = group.Key.Length == 0 ? Strings.ChildListEditor_NoCategory : group.Key;
                rows.Add(new ChildListRow(null, group.Key.Length == 0 ? NoCategoryKey : group.Key, text) { Category = group.Key });
                rows.AddRange(group.Select(c => Row(c, group.Key, text)));
            }

            return rows;
        }
    }

    public bool IsEmpty => Component.Children.Count == 0;

    public string EmptyText => Strings.ChildListEditor_Empty;

    /// <summary>The selected children in list order.</summary>
    public IReadOnlyList<FormObject> Selection => _selection;

    private bool CanChange => _designer.Surface is { IsReadOnly: false } && !Component.IsPreserved;

    public bool CanNew => CanChange && ItemClasses.Count > 0;

    public bool CanDelete => CanChange && _selection.Count > 0 && _selection.All(o => !o.IsPreserved);

    /// <summary>DSG-SUR-219: disabled at the first row of the list — in a grouped list, of the child's group.</summary>
    public bool CanMoveUp => CanChange && _selection is [var item] && Neighbour(item, -1) is not null;

    /// <summary>DSG-SUR-219: disabled at the last row of the list — in a grouped list, of the child's group.</summary>
    public bool CanMoveDown => CanChange && _selection is [var item] && Neighbour(item, +1) is not null;

    /// <summary>The list selection (a click, Shift/Ctrl+click, the arrow keys); children of other components are ignored.</summary>
    public void Select(IEnumerable<FormObject> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var chosen = new HashSet<FormObject>(items, ReferenceEqualityComparer.Instance);
        var selection = Component.Children.Where(chosen.Contains).ToList();
        if (selection.SequenceEqual(_selection))
        {
            return;
        }

        _selection = selection;
        Grid.SelectionChanged();
        Changed();
    }

    /// <summary>Selects the children of those names.</summary>
    public void Select(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        Select(Component.Children.Where(c => names.Contains(c.Name, Names)));
    }

    /// <summary>
    /// New: appends a component of <paramref name="className"/> (default: the first item class) as the last child — named
    /// per DSG-TP-050, with its field — as one "Add {ClassName}" unit; it becomes the selection and the grid's
    /// <c>Caption</c> row (else its <c>Name</c> row) the current row (dialogs.md §27).
    /// </summary>
    public void New(string? className = null)
    {
        className ??= ItemClasses.FirstOrDefault();
        if (!CanNew || className is null || !ItemClasses.Contains(className, Names) || _designer.Catalog.Find(className) is not { } info)
        {
            return;
        }

        if (_designer.Surface!.CreateChild(Component, info, Component.Children.Count, [], select: false) is not { } created)
        {
            return; // code sync refused (DSG-TP-053): the status bar says why
        }

        Select([created]);
        var row = info.FindProperty("Caption") is not null ? "Caption" : "Name";
        Grid.Page = InspectorPage.Properties;
        Grid.SetCurrent(row);
        GridFocusRequested?.Invoke(this, row);
    }

    /// <summary>Delete: all selected children with their fields as one "Delete" unit; the first remaining child after the first deleted one, else the one before it, is selected next.</summary>
    public void Delete()
    {
        if (!CanDelete)
        {
            return;
        }

        var children = Component.Children;
        var first = IndexOf(_selection[0]);
        var next = children.Skip(first + 1).FirstOrDefault(c => !_selection.Contains(c)) ?? children.Take(first).LastOrDefault();
        var deleted = _selection.ToList();
        var surfaceSelection = _designer.Surface!.Selection.Primary;
        var keep = deleted.Any(d => Subtree(d).Contains(surfaceSelection)) ? Component : surfaceSelection;
        if (_designer.Surface.DeleteObjects(deleted, keep))
        {
            _selection = [];
            Select(next is null ? [] : [next]);
            Changed();
        }
    }

    /// <summary>Move Up: exchanges the child with the previous child of the list — grouped: of its group (one "Order" unit, DSG-SUR-219).</summary>
    public void MoveUp() => Move(-1);

    /// <summary>Move Down: exchanges the child with the next child of the list — grouped: of its group (one "Order" unit, DSG-SUR-219).</summary>
    public void MoveDown() => Move(+1);

    private void Move(int delta)
    {
        if (!(delta < 0 ? CanMoveUp : CanMoveDown))
        {
            return;
        }

        var item = _selection[0];
        var other = Neighbour(item, delta)!;
        var (low, high) = delta < 0 ? (other, item) : (item, other);
        var (lowIndex, highIndex) = (IndexOf(low), IndexOf(high));
        // The two children change places; the children between them (of other groups) keep theirs.
        List<FormChange> changes = [new MoveObject(high.Name, Component.Name, lowIndex)];
        if (highIndex - lowIndex > 1)
        {
            changes.Add(new MoveObject(low.Name, Component.Name, highIndex));
        }

        if (_designer.Surface!.Commit(UndoTitles.Order, changes))
        {
            Changed();
        }
    }

    /// <summary>
    /// Enter or double-click on a row (UI-DLG-260): <paramref name="item"/> becomes the selection and its default event
    /// handler (<c>TAction</c>: <c>OnExecute</c>) is created and assigned — or the existing one kept — without leaving the
    /// dialog; the Events page then shows the method name.
    /// </summary>
    public void Activate(FormObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Select([item]);
        if (_selection is not [{ Info: { } info }] || (info.Events.FirstOrDefault(e => e.IsDefault) ?? info.Events.FirstOrDefault()) is not { } ev)
        {
            return;
        }

        Grid.Page = InspectorPage.Events;
        Grid.ActivateEvent(ev.Name, navigate: false);
        Changed();
    }

    // ---- internals --------------------------------------------------------------------------------------------------

    private static IReadOnlyList<string> FindItemClasses(ComponentCatalog catalog, FormObject component)
    {
        if (component.Info is not { } owner)
        {
            return [];
        }

        bool Fits(ComponentInfo child) => !child.IsAbstract
            && (owner.ChildClasses.IsEmpty || owner.ChildClasses.Any(c => catalog.IsAssignable(child.ClassName, c)))
            && (child.ParentClasses.IsEmpty || child.ParentClasses.Any(c => catalog.IsAssignable(owner.ClassName, c)));
        return [.. catalog.All.Where(c => (!owner.ChildClasses.IsEmpty || !c.ParentClasses.IsEmpty) && Fits(c)).Select(c => c.ClassName)];
    }

    /// <summary>
    /// DSG-SUR-216: the classes without an <c>initial</c> value of <c>Category</c> in catalog order, then one section per
    /// distinct initial value (sections in order of first occurrence, classes in catalog order) behind its header row.
    /// </summary>
    private static IReadOnlyList<ChildListPickerRow> Picker(ComponentCatalog catalog, IReadOnlyList<string> classes)
    {
        string Section(string className) => catalog.Find(className)?.FindProperty("Category")?.Initial ?? "";
        ChildListPickerRow ClassRow(string className, string? section) =>
            new(className, className is ['T', _, ..] ? className[1..] : className, section);
        var rows = new List<ChildListPickerRow>(classes.Where(c => Section(c).Length == 0).Select(c => ClassRow(c, null)));
        foreach (var section in classes.Select(Section).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal))
        {
            rows.Add(new ChildListPickerRow(null, section, section));
            rows.AddRange(classes.Where(c => string.Equals(Section(c), section, StringComparison.Ordinal)).Select(c => ClassRow(c, section)));
        }

        return rows;
    }

    /// <summary>The <c>Category</c> value of a child; empty when it has none or its class has no such property (DSG-SUR-217).</summary>
    private static string CategoryOf(FormObject item) => item.FindProperty("Category")?.Value is StringValue { Value: { } category } ? category : "";

    /// <summary>DSG-SUR-219: the previous / next child in child order — in a grouped list, of the same group; null at the end.</summary>
    private FormObject? Neighbour(FormObject item, int delta)
    {
        var grouped = IsGrouped;
        var category = CategoryOf(item);
        var children = Component.Children;
        for (var i = IndexOf(item) + delta; i >= 0 && i < children.Count; i += delta)
        {
            if (!grouped || string.Equals(CategoryOf(children[i]), category, StringComparison.Ordinal))
            {
                return children[i];
            }
        }

        return null;
    }

    private static ChildListRow Row(FormObject item, string? category = null, string? categoryText = null)
    {
        var caption = item.FindProperty("Caption")?.Value is StringValue { Value: { Length: > 0 } value } ? value : null;
        var text = caption is null ? item.Name : item.Name + " – " + caption;
        return category is null
            ? new ChildListRow(item, item.Name, text)
            : new ChildListRow(item, item.Name, text)
            {
                Category = category,
                AccessibleName = categoryText + ", " + text.Replace("&", "", StringComparison.Ordinal),
            };
    }

    private int IndexOf(FormObject child)
    {
        for (var i = 0; i < Component.Children.Count; i++)
        {
            if (ReferenceEquals(Component.Children[i], child))
            {
                return i;
            }
        }

        return -1;
    }

    private static IEnumerable<FormObject> Subtree(FormObject obj) => obj.Children.SelectMany(Subtree).Prepend(obj);

    private void OnModelChanged(object? sender, EventArgs e)
    {
        var kept = _selection.Where(o => IndexOf(o) >= 0).ToList();
        if (kept.Count != _selection.Count)
        {
            _selection = kept; // undone behind the dialog
            Grid.SelectionChanged();
        }
        else
        {
            _selection = [.. Component.Children.Where(kept.Contains)]; // list order after a move
        }

        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(IsGrouped));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(CanNew));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        Grid.Refresh();
    }
}
