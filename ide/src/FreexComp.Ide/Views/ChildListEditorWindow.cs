using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Child List Editor (dialogs.md §27, DSG-SUR-198, 199; D-099): 640 × 400, resizable; the child list (240 px,
/// extended selection, <c>Dialog.ChildListEditor.Item.&lt;ComponentName&gt;</c>) with New / Delete / Move Up / Move Down
/// under it and the embedded property grid with its Properties / Events page tabs. When a child has a category the list
/// is grouped under header rows (<c>Dialog.ChildListEditor.Group.&lt;Category&gt;</c>; UI-DLG-306, DSG-SUR-217, 218): not
/// selectable, skipped by the keyboard. With several item classes New is a split button (UI-DLG-307): its button part
/// creates the first class, its arrow part (Alt+Down) opens the class picker — a menu with section headers
/// (<c>Dialog.ChildListEditor.New.Group.&lt;Category&gt;</c>) and class rows (<c>Dialog.ChildListEditor.New.&lt;ClassName&gt;</c>).
/// Keys in the list: Insert, Delete, Ctrl+Up / Ctrl+Down, Enter or a double-click (the default handler, UI-DLG-260).
/// Close (Escape / ×) only closes: every operation is already applied.
/// </summary>
public sealed class ChildListEditorWindow : DialogWindow
{
    public ChildListEditorWindow(ChildListEditorViewModel model)
        : this(model, new Parts())
    {
    }

    private ChildListEditorWindow(ChildListEditorViewModel model, Parts parts)
        : base(model, Body(model, parts))
    {
        // The dialog opens with the focus in the list, on the selected child (after the base focus pass).
        Opened += (_, _) => Dispatcher.UIThread.Post(() => FocusSelected(parts.List), DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(ChildListEditorViewModel.DialogName, field);

    /// <summary>The controls the handlers share.</summary>
    private sealed class Parts
    {
        public ListBox List { get; } = new() { SelectionMode = SelectionMode.Multiple, Focusable = true };

        public PropertyGridView Grid { get; } = new(Id("Grid"), Id("Grid.Row.")) { IsInDialog = true, BorderThickness = new Thickness(1) };

        /// <summary>The rows the list shows now.</summary>
        public IReadOnlyList<ChildListRow> Shown { get; set; } = [];

        public bool Syncing { get; set; }
    }

    private static Grid Body(ChildListEditorViewModel model, Parts p)
    {
        var itemsLabel = new Label { Content = MnemonicLabel.ToAccessText(Strings.ChildListEditor_Items), Target = p.List, Padding = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetAutomationId(p.List, Id("Items"));
        SetKeepsEnter(p.List, true); // UI-DLG-260: Enter on a row is the default handler, not Close
        // UI-DLG-306: a header row has no selection fill (a range of the extended selection runs across it).
        p.List.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(HeaderClass).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) },
        });
        AutomationProperties.SetName(p.List, MnemonicLabel.ToPlainText(Strings.ChildListEditor_Items));
        var placeholder = new TextBlock { Text = model.EmptyText, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
        placeholder.Bind(TextBlock.ForegroundProperty, placeholder.GetResourceObservable("FxcTextTertiaryBrush"));
        AutomationProperties.SetAutomationId(placeholder, Id("Items.Empty"));
        var listFrame = new Border { BorderThickness = new Thickness(1), Child = new Panel { Children = { p.List, placeholder } } };
        listFrame.Bind(Border.BorderBrushProperty, listFrame.GetResourceObservable("FxcBorderDefaultBrush"));

        // UI-DLG-307: a split button with the class picker when the metadata allows more than one item class.
        Control newButton = model.HasClassChoice ? SplitNew(model) : Button("New", Strings.ChildListEditor_New, () => model.New());
        var delete = Button("Delete", Strings.ChildListEditor_Delete, model.Delete);
        var up = Button("MoveUp", Strings.ChildListEditor_MoveUp, model.MoveUp);
        var down = Button("MoveDown", Strings.ChildListEditor_MoveDown, model.MoveDown);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0), Children = { newButton, delete, up, down } };

        // The grid's page tabs (Dialog.ChildListEditor.Grid.Page.Properties / .Events).
        p.Grid.Grid = model.Grid;
        p.Grid.Bind(Border.BorderBrushProperty, p.Grid.GetResourceObservable("FxcBorderDefaultBrush"));
        AutomationProperties.SetName(p.Grid, Strings.ChildListEditor_Grid);
        var properties = PageTab("Grid.Page.Properties", Strings.Inspector_Tab_Properties, () => model.Grid.Page = InspectorPage.Properties);
        var events = PageTab("Grid.Page.Events", Strings.Inspector_Tab_Events, () => model.Grid.Page = InspectorPage.Events);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 0, 4), Children = { properties, events } };
        var right = new DockPanel();
        DockPanel.SetDock(tabs, Dock.Top);
        right.Children.Add(tabs);
        right.Children.Add(p.Grid);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("240,12,*"),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            MinHeight = 240,
        };
        Place(body, itemsLabel, 0, 0);
        Place(body, listFrame, 1, 0);
        Place(body, buttons, 2, 0);
        Grid.SetColumnSpan(buttons, 3); // the split button makes the row wider than the list column
        Place(body, right, 0, 2);
        Grid.SetRowSpan(right, 2);

        void SyncPage()
        {
            var isEvents = model.Grid.Page == InspectorPage.Events;
            properties.Classes.Set("active", !isEvents);
            events.Classes.Set("active", isEvents);
            properties.FontWeight = isEvents ? FontWeight.Normal : FontWeight.SemiBold;
            events.FontWeight = isEvents ? FontWeight.SemiBold : FontWeight.Normal;
        }

        void Sync()
        {
            p.Syncing = true;
            var hadFocus = p.List.IsKeyboardFocusWithin;
            var focused = (TopLevel.GetTopLevel(p.List)?.FocusManager?.GetFocusedElement() as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.Tag;
            var rows = model.Rows;
            if (!rows.SequenceEqual(p.Shown))
            {
                p.Shown = rows;
                p.List.Items.Clear();
                for (var i = 0; i < rows.Count; i++)
                {
                    var item = rows[i].IsHeader ? HeaderItem(rows[i]) : ChildItem(rows[i]);
                    if (!rows[i].IsHeader && i > 0 && rows[i - 1].IsHeader)
                        item.AddHandler(RequestBringIntoViewEvent, BringHeaderAlong);
                    p.List.Items.Add(item);
                }
            }

            // The list's own selection is kept while it shows the model's (its anchor serves the next Shift+arrow).
            var selected = new HashSet<FormObject>(model.Selection, ReferenceEqualityComparer.Instance);
            if (!selected.SetEquals(p.List.SelectedItems!.OfType<ListBoxItem>().Select(i => i.Tag).OfType<FormObject>()))
            {
                p.List.SelectedItems!.Clear();
                foreach (var item in p.List.Items.Cast<ListBoxItem>().Where(i => i.Tag is FormObject o && selected.Contains(o)))
                {
                    p.List.SelectedItems.Add(item);
                }
            }

            placeholder.IsVisible = model.IsEmpty;
            newButton.IsEnabled = model.CanNew;
            delete.IsEnabled = model.CanDelete;
            up.IsEnabled = model.CanMoveUp;
            down.IsEnabled = model.CanMoveDown;
            p.Syncing = false;
            if (hadFocus && !p.List.IsKeyboardFocusWithin)
            {
                // Rebuilt rows lost the focus: the list keeps it, on the row that had it or the selected one.
                FocusRow(p.List, focused as FormObject);
                Dispatcher.UIThread.Post(() =>
                {
                    if (p.List.IsKeyboardFocusWithin)
                    {
                        FocusRow(p.List, focused as FormObject);
                    }
                }, DispatcherPriority.Loaded);
            }
        }

        p.List.SelectionChanged += (_, _) =>
        {
            if (!p.Syncing)
            {
                // DSG-SUR-218: a header row is not counted by the extended selection (a range across it, Ctrl+A): the
                // list's own selection may hold it — it shows no selection fill — and the children alone are the selection.
                model.Select(p.List.SelectedItems!.OfType<ListBoxItem>().Select(i => i.Tag).OfType<FormObject>());
            }
        };
        p.List.AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key, e.KeyModifiers)
            {
                case (Key.Insert, KeyModifiers.None):
                    model.New();
                    break;
                case (Key.Delete, KeyModifiers.None):
                    model.Delete();
                    break;
                case (Key.Up, KeyModifiers.Control):
                    model.MoveUp();
                    break;
                case (Key.Down, KeyModifiers.Control):
                    model.MoveDown();
                    break;
                case (Key.Enter, KeyModifiers.None):
                    if ((FocusedRow(p.List) ?? model.Selection.FirstOrDefault()) is { } target)
                    {
                        model.Activate(target);
                    }

                    break;
                default:
                    return;
            }

            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        p.List.AddHandler(DoubleTappedEvent, (_, e) =>
        {
            if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { Tag: FormObject item })
            {
                e.Handled = true;
                model.Activate(item);
            }
        });

        // New: the new child is scrolled into view (DSG-SUR-220); the grid's Caption (else Name) row takes the focus.
        model.GridFocusRequested += (_, _) =>
        {
            if (p.List.SelectedItems?.OfType<ListBoxItem>().FirstOrDefault() is { } created)
            {
                p.List.ScrollIntoView(created);
            }

            p.Grid.Focus(NavigationMethod.Tab);
        };
        model.PropertyChanged += (_, _) => Sync();
        model.Grid.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PropertyGridViewModel.Page))
            {
                SyncPage();
            }
        };
        Sync();
        SyncPage();
        return body;
    }

    /// <summary>The style class of a header row of the list.</summary>
    public const string HeaderClass = "header";

    /// <summary>The rows of the class picker that are visible without scrolling (UI-DLG-307).</summary>
    public const int PickerVisibleRows = 12;

    /// <summary>
    /// I-297: the first child of a group is brought into view together with the header row above it — as high as a
    /// child row (UI-DLG-306) — so that the header is never half hidden above the row the keyboard moved to.
    /// </summary>
    private static void BringHeaderAlong(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (sender is ListBoxItem item && ReferenceEquals(e.TargetObject, item))
            e.TargetRect = new Rect(e.TargetRect.X, e.TargetRect.Y - item.Bounds.Height, e.TargetRect.Width, e.TargetRect.Height + item.Bounds.Height);
    }

    /// <summary>
    /// UI-DLG-306: a child row — in a grouped list indented by <c>space.4</c>, its screen-reader name prefixed with the
    /// category; in a flat list exactly the row of before.
    /// </summary>
    private static ListBoxItem ChildItem(ChildListRow row)
    {
        // I-297: the caption without its mnemonic marker (I-286), as text — a row has no access key of its own.
        var item = new ListBoxItem { Tag = row.Item };
        var text = MnemonicLabel.ToPlainText(row.Text);
        if (row.Category is null)
        {
            item.Content = text;
        }
        else
        {
            item.Content = new TextBlock { Text = text, Margin = new Thickness(Metric("FxcSpace4"), 0, 0, 0) };
        }

        AutomationProperties.SetAutomationId(item, Id("Item." + row.Name));
        AutomationProperties.SetName(item, row.Category is null ? text : row.AccessibleName);
        return item;
    }

    /// <summary>
    /// UI-DLG-306, DSG-SUR-218: the header row of a category — <c>type.caption</c>, <c>text.secondary</c>, as high as a
    /// child row, left padding <c>space.2</c>; no hover or selection fill, no keyboard focus.
    /// </summary>
    private static ListBoxItem HeaderItem(ChildListRow row)
    {
        var caption = new TextBlock { Text = row.Text, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
        caption.Bind(TextBlock.ForegroundProperty, caption.GetResourceObservable("FxcTextSecondaryBrush"));
        // An invisible line of body text gives the header the height of a child row.
        var line = new TextBlock { Text = " ", Opacity = 0, IsHitTestVisible = false };
        var item = new ListBoxItem
        {
            Content = new Panel { Children = { line, caption } }, Tag = row, Focusable = false, IsHitTestVisible = false, Classes = { HeaderClass },
        };
        item.AttachedToVisualTree += (_, _) =>
        {
            var styled = item.Padding; // the list row padding of the theme; only its left side differs
            item.Padding = new Thickness(Metric("FxcSpace2"), styled.Top, styled.Right, styled.Bottom);
        };
        AutomationProperties.SetAutomationId(item, Id("Group." + row.Name));
        AutomationProperties.SetName(item, row.Text);
        return item;
    }

    /// <summary>
    /// UI-DLG-307, 308: New as a split button — the button part creates the first class; the arrow part, Alt+Down or F4
    /// on the focused button open the picker, a menu under the button (above it when there is no room below; a popup of
    /// its own, never cut by the dialog) with at most <see cref="PickerVisibleRows"/> rows before it scrolls.
    /// </summary>
    private static SplitButton SplitNew(ChildListEditorViewModel model)
    {
        var split = new SplitButton { Content = MnemonicLabel.ToAccessText(Strings.ChildListEditor_New), MinWidth = 0 };
        AutomationProperties.SetAutomationId(split, Id("New"));
        AutomationProperties.SetName(split, MnemonicLabel.ToPlainText(Strings.ChildListEditor_New));
        split.Click += (_, e) =>
        {
            e.Handled = true;
            model.New();
        };
        // Enter on the focused button is its own click (on release), not the dialog's default button.
        split.AddHandler(KeyDownEvent, (_, e) => e.Handled |= e is { Key: Key.Enter, KeyModifiers: KeyModifiers.None });

        var menu = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.SlideX | PopupPositionerConstraintAdjustment.SlideY,
        };
        var items = new List<MenuItem>();
        foreach (var row in model.PickerRows)
        {
            var item = row.IsHeader ? PickerHeader(row) : PickerClass(model, row, items);
            items.Add(item);
            menu.Items.Add(item);
        }

        menu.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!menu.IsOpen || items.FirstOrDefault(i => i.IsEnabled) is not { } first)
            {
                return;
            }

            // At most twelve rows, then scrolling: the natural height less the rows beyond the twelfth.
            if (first.Parent is MenuFlyoutPresenter { Tag: null } presenter && items.Count > PickerVisibleRows && presenter.Bounds.Height > 0)
            {
                presenter.Tag = presenter; // measured once
                presenter.MaxHeight = presenter.Bounds.Height - items.Skip(PickerVisibleRows).Sum(i => i.Bounds.Height);
            }

            Highlight(first); // the keyboard starts at the first class
        }, DispatcherPriority.Loaded);
        split.Flyout = menu;
        return split;
    }

    /// <summary>A section header of the picker (DSG-SUR-216, UI-DLG-307): as the list headers, not selectable.</summary>
    private static MenuItem PickerHeader(ChildListPickerRow row)
    {
        var caption = new TextBlock { Text = row.Text, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
        caption.Bind(TextBlock.ForegroundProperty, caption.GetResourceObservable("FxcTextSecondaryBrush"));
        var item = new MenuItem { Header = caption, IsEnabled = false, IsHitTestVisible = false, Focusable = false };
        AutomationProperties.SetAutomationId(item, Id("New.Group." + row.Text));
        AutomationProperties.SetName(item, row.Text);
        return item;
    }

    /// <summary>
    /// A class row of the picker (UI-DLG-307): the display name, then the class name in <c>text.secondary</c> after a gap
    /// of <c>space.4</c>; rows of a section are indented like the children of a group. A click or Enter creates the class;
    /// a typed character moves to the next class whose display name starts with it.
    /// </summary>
    private static MenuItem PickerClass(ChildListEditorViewModel model, ChildListPickerRow row, IReadOnlyList<MenuItem> all)
    {
        var name = new TextBlock { Text = row.Text, MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
        var className = new TextBlock { Text = row.ClassName, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Metric("FxcSpace4"), 0, 0, 0) };
        className.Bind(TextBlock.ForegroundProperty, className.GetResourceObservable("FxcTextSecondaryBrush"));
        if (row.Category is not null)
        {
            name.Padding = new Thickness(Metric("FxcSpace4"), 0, 0, 0);
        }

        var item = new MenuItem { Header = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, className } }, Tag = row };
        AutomationProperties.SetAutomationId(item, Id("New." + row.ClassName));
        AutomationProperties.SetName(item, row.Text + " " + row.ClassName);
        item.Click += (_, _) => model.New(row.ClassName);
        item.AddHandler(TextInputEvent, (_, e) =>
        {
            if (e.Text is not [var typed] || char.IsControl(typed))
            {
                return;
            }

            // Type-ahead on the display name: the next class row after the highlighted one, wrapping around.
            var at = all.ToList().IndexOf(item);
            var next = Enumerable.Range(1, all.Count).Select(i => all[(at + i) % all.Count])
                .FirstOrDefault(i => i.Tag is ChildListPickerRow { Text: [var initial, ..] } && char.ToUpperInvariant(initial) == char.ToUpperInvariant(typed));
            if (next is not null)
            {
                e.Handled = true;
                Highlight(next);
            }
        });
        return item;
    }

    /// <summary>Makes <paramref name="item"/> the highlighted row of its open menu.</summary>
    private static void Highlight(MenuItem item)
    {
        if (item.Parent is SelectingItemsControl menu)
        {
            menu.SelectedItem = item;
        }

        item.Focus(NavigationMethod.Directional);
    }

    private static FormObject? FocusedRow(ListBox list) =>
        (TopLevel.GetTopLevel(list)?.FocusManager?.GetFocusedElement() as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.Tag as FormObject;

    private static void FocusRow(ListBox list, FormObject? row)
    {
        var target = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => row is not null && ReferenceEquals(i.Tag, row))
            ?? list.SelectedItems?.OfType<ListBoxItem>().FirstOrDefault();
        if (!(target?.Focus(NavigationMethod.Tab) ?? false))
        {
            list.Focus(NavigationMethod.Tab);
        }
    }

    private static void FocusSelected(ListBox list) => FocusRow(list, null);

    private static Button PageTab(string field, string text, Action action)
    {
        var tab = new Button { Content = text, Padding = new Thickness(8, 0), Height = 26, MinWidth = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 2) };
        tab.Classes.Add("flat");
        AutomationProperties.SetAutomationId(tab, Id(field));
        AutomationProperties.SetName(tab, text);
        tab.Click += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return tab;
    }

    private static Button Button(string field, string text, Action action)
    {
        var button = new Button { Content = MnemonicLabel.ToAccessText(text), MinWidth = 0 };
        AutomationProperties.SetAutomationId(button, Id(field));
        AutomationProperties.SetName(button, MnemonicLabel.ToPlainText(text));
        button.Click += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return button;
    }

    /// <summary>A metric of design-tokens.md §5 (the spacing scale).</summary>
    private static double Metric(string key) => Application.Current?.FindResource(key) as double? ?? 0;

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
