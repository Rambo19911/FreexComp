using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
/// under it — New is a drop-down of class names (<c>Dialog.ChildListEditor.New.&lt;ClassName&gt;</c>) only when several
/// item classes exist — and the embedded property grid with its Properties / Events page tabs. Keys in the list: Insert,
/// Delete, Ctrl+Up / Ctrl+Down, Enter or a double-click (the default handler, UI-DLG-260). Close (Escape / ×) only
/// closes: every operation is already applied.
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

        public bool Syncing { get; set; }
    }

    private static Grid Body(ChildListEditorViewModel model, Parts p)
    {
        var itemsLabel = new Label { Content = Strings.ChildListEditor_Items, Target = p.List, Padding = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetAutomationId(p.List, Id("Items"));
        SetKeepsEnter(p.List, true); // UI-DLG-260: Enter on a row is the default handler, not Close
        AutomationProperties.SetName(p.List, Plain(Strings.ChildListEditor_Items));
        var placeholder = new TextBlock { Text = model.EmptyText, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
        placeholder.Bind(TextBlock.ForegroundProperty, placeholder.GetResourceObservable("FxcTextTertiaryBrush"));
        AutomationProperties.SetAutomationId(placeholder, Id("Items.Empty"));
        var listFrame = new Border { BorderThickness = new Thickness(1), Child = new Panel { Children = { p.List, placeholder } } };
        listFrame.Bind(Border.BorderBrushProperty, listFrame.GetResourceObservable("FxcBorderDefaultBrush"));

        Button? newButton = null;
        newButton = Button("New", Strings.ChildListEditor_New, () => New(model, newButton!));
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
            var items = p.List.Items.Cast<ListBoxItem>().ToList();
            if (items.Count != rows.Count || !items.Zip(rows).All(pair => ReferenceEquals(pair.First.Tag, pair.Second.Item) && (string?)pair.First.Content == pair.Second.Text))
            {
                p.List.Items.Clear();
                foreach (var row in rows)
                {
                    var item = new ListBoxItem { Content = row.Text, Tag = row.Item };
                    AutomationProperties.SetAutomationId(item, Id("Item." + row.Name));
                    AutomationProperties.SetName(item, row.Text);
                    p.List.Items.Add(item);
                }
            }

            var selected = new HashSet<FormObject>(model.Selection, ReferenceEqualityComparer.Instance);
            p.List.SelectedItems!.Clear();
            foreach (var item in p.List.Items.Cast<ListBoxItem>().Where(i => i.Tag is FormObject o && selected.Contains(o)))
            {
                p.List.SelectedItems.Add(item);
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

        // New: the grid's Caption (else Name) row takes the focus.
        model.GridFocusRequested += (_, _) => p.Grid.Focus(NavigationMethod.Tab);
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

    /// <summary>New: one item class creates at once; several open the class list under the button.</summary>
    private static void New(ChildListEditorViewModel model, Button button)
    {
        if (!model.HasClassChoice)
        {
            model.New();
            return;
        }

        var menu = new ContextMenu();
        foreach (var className in model.ItemClasses)
        {
            var item = new MenuItem { Header = className };
            AutomationProperties.SetAutomationId(item, Id("New." + className));
            item.Click += (_, _) => model.New(className);
            menu.Items.Add(item);
        }

        menu.Open(button);
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
        var button = new Button { Content = text, MinWidth = 0 };
        AutomationProperties.SetAutomationId(button, Id(field));
        AutomationProperties.SetName(button, Plain(text));
        button.Click += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return button;
    }

    private static string Plain(string text) => text.Replace("&", "", StringComparison.Ordinal);

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
