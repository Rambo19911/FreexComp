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

/// <summary>One row of the Child List Editor (dialogs.md §27): <c>{Name}</c> and, when the caption is not empty, <c> – {Caption}</c>.</summary>
public sealed record ChildListRow(FormObject Item, string Name, string Text);

/// <summary>
/// The Child List Editor (design-surface.md DSG-SUR-198, DSG-SUR-199; dialogs.md §27; D-099) of a component whose class
/// names <c>componentEditor: "childlist"</c> (<c>TActionList</c>): the flat list of its children, New (one item class: a
/// button; several: a choice of class names), Delete (all selected children, one unit), Move Up / Move Down (one selected
/// child) and the embedded property grid with Properties and Events pages for the selected children. Every operation is
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
        ItemClasses = FindItemClasses(designer.Catalog, component);
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
    /// <c>parentClasses</c> accept it (non-abstract, catalog order).
    /// </summary>
    public IReadOnlyList<string> ItemClasses { get; }

    /// <summary>New is a drop-down of class names (more than one item class).</summary>
    public bool HasClassChoice => ItemClasses.Count > 1;

    /// <summary>The view moves the focus to the grid's row of that property path (New: <c>Caption</c>, else <c>Name</c>).</summary>
    public event EventHandler<string>? GridFocusRequested;

    /// <summary>The rows in creation order.</summary>
    public IReadOnlyList<ChildListRow> Rows => [.. Component.Children.Select(Row)];

    public bool IsEmpty => Component.Children.Count == 0;

    public string EmptyText => Strings.ChildListEditor_Empty;

    /// <summary>The selected children in list order.</summary>
    public IReadOnlyList<FormObject> Selection => _selection;

    private bool CanChange => _designer.Surface is { IsReadOnly: false } && !Component.IsPreserved;

    public bool CanNew => CanChange && ItemClasses.Count > 0;

    public bool CanDelete => CanChange && _selection.Count > 0 && _selection.All(o => !o.IsPreserved);

    public bool CanMoveUp => CanChange && _selection is [var item] && IndexOf(item) > 0;

    public bool CanMoveDown => CanChange && _selection is [var item] && IndexOf(item) < Component.Children.Count - 1;

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

    /// <summary>Move Up: one place towards the first child ("Order" unit).</summary>
    public void MoveUp() => Move(-1);

    /// <summary>Move Down: one place towards the last child ("Order" unit).</summary>
    public void MoveDown() => Move(+1);

    private void Move(int delta)
    {
        if (!(delta < 0 ? CanMoveUp : CanMoveDown))
        {
            return;
        }

        var item = _selection[0];
        if (_designer.Surface!.Commit(UndoTitles.Order, [new MoveObject(item.Name, Component.Name, IndexOf(item) + delta)]))
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

    private static ChildListRow Row(FormObject item) =>
        new(item, item.Name, item.FindProperty("Caption")?.Value is StringValue { Value: { Length: > 0 } caption } ? item.Name + " – " + caption : item.Name);

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
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(CanNew));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        Grid.Refresh();
    }
}
