using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The collection editor (dialogs.md §25, object-inspector.md §5.7): 640 × 400, resizable; the item list (240 px, extended
/// selection, <c>Dialog.CollectionEditor.Item.&lt;index&gt;</c>), the Add / Delete / Move Up / Move Down row under it and
/// the embedded property grid of the selected item (<c>Dialog.CollectionEditor.Grid.Row.&lt;PropertyPath&gt;</c>, name
/// column 45 %). Keys (UI-DLG-240): Insert, Delete, Ctrl+Up / Ctrl+Down in the list; F4 moves to the grid; in the grid
/// Up/Down, F2 or typing edits, Space toggles; Enter / Escape in a grid editor commit / cancel that edit only.
/// </summary>
public sealed class CollectionEditorWindow : DialogWindow
{
    public CollectionEditorWindow(CollectionEditorViewModel model)
        : this(model, new Parts())
    {
    }

    private CollectionEditorWindow(CollectionEditorViewModel model, Parts parts)
        : base(model, Body(model, parts))
    {
        // UI-DLG-241: the dialog opens with the focus in the item list, on the selected item (after the base focus pass).
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(
            () => FocusSelected(parts.List), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(CollectionEditorViewModel.DialogName, field);

    /// <summary>The controls the handlers share.</summary>
    private sealed class Parts
    {
        public ListBox List { get; } = new() { SelectionMode = SelectionMode.Multiple };

        /// <summary>The Object Inspector's grid control (D-110), without categories or description.</summary>
        public PropertyGridView Grid { get; } = new(Id("Grid"), Id("Grid.Row.")) { IsInDialog = true, BorderThickness = new Thickness(1) };

        public bool Syncing { get; set; }
    }

    private static Grid Body(CollectionEditorViewModel model, Parts p)
    {
        var itemsLabel = new Label { Content = Strings.CollectionEditor_Items, Target = p.List, Padding = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetAutomationId(p.List, Id("Items"));
        AutomationProperties.SetName(p.List, Strings.CollectionEditor_Items.Replace("&", "", StringComparison.Ordinal));
        var placeholder = new TextBlock
        {
            Text = model.EmptyText, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false,
        };
        placeholder.Bind(TextBlock.ForegroundProperty, placeholder.GetResourceObservable("FxcTextTertiaryBrush"));
        AutomationProperties.SetAutomationId(placeholder, Id("Items.Empty"));
        var listHost = new Panel { Children = { p.List, placeholder } };

        var add = Button("Add", Strings.CollectionEditor_Add, model.Add);
        var delete = Button("Delete", Strings.CollectionEditor_Delete, model.Delete);
        var up = Button("MoveUp", Strings.CollectionEditor_MoveUp, model.MoveUp);
        var down = Button("MoveDown", Strings.CollectionEditor_MoveDown, model.MoveDown);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0), Children = { add, delete, up, down } };

        var gridLabel = new TextBlock { Text = Strings.CollectionEditor_Grid, Margin = new Thickness(0, 0, 0, 4) };
        p.Grid.Grid = model.Grid;
        p.Grid.Bind(Border.BorderBrushProperty, p.Grid.GetResourceObservable("FxcBorderDefaultBrush"));
        AutomationProperties.SetName(p.Grid, Strings.CollectionEditor_Grid);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("240,12,*"),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            MinHeight = 240,
        };
        Place(body, itemsLabel, 0, 0);
        Place(body, listHost, 1, 0);
        Place(body, buttons, 2, 0);
        Place(body, gridLabel, 0, 2);
        Place(body, p.Grid, 1, 2);
        Grid.SetRowSpan(p.Grid, 2);

        void Sync()
        {
            p.Syncing = true;
            var hadFocus = p.List.IsKeyboardFocusWithin;
            var texts = model.ItemTexts;
            if (p.List.ItemCount != texts.Count || !p.List.Items.Cast<ListBoxItem>().Select(i => (string?)i.Content).SequenceEqual(texts))
            {
                p.List.Items.Clear();
                for (var i = 0; i < texts.Count; i++)
                {
                    var item = new ListBoxItem { Content = texts[i] };
                    AutomationProperties.SetAutomationId(item, Id("Item." + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    p.List.Items.Add(item);
                }
            }

            p.List.SelectedItems!.Clear();
            foreach (var index in model.SelectedIndices)
            {
                p.List.SelectedItems.Add(p.List.Items[index]);
            }

            placeholder.IsVisible = model.IsEmpty;
            delete.IsEnabled = model.CanDelete;
            up.IsEnabled = model.CanMoveUp;
            down.IsEnabled = model.CanMoveDown;
            p.Syncing = false;
            if (hadFocus && !p.List.IsKeyboardFocusWithin)
            {
                // Rebuilt items lost the focus: the list keeps it, on the (first) selected item (UI-DLG-240).
                FocusSelected(p.List);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => FocusSelected(p.List), Avalonia.Threading.DispatcherPriority.Loaded);
            }
        }

        p.List.SelectionChanged += (_, _) =>
        {
            if (!p.Syncing)
            {
                model.Select(p.List.SelectedItems!.Cast<object>().Select(i => p.List.Items.IndexOf(i)));
            }
        };
        p.List.AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key, e.KeyModifiers)
            {
                case (Key.Insert, KeyModifiers.None):
                    model.Add();
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
                case (Key.F4, KeyModifiers.None):
                    if (model.Grid.CurrentPath is null)
                    {
                        model.Grid.MoveCurrent(1);
                    }

                    p.Grid.Focus(NavigationMethod.Tab);
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        model.PropertyChanged += (_, _) => Sync();
        Sync();
        return body;
    }

    private static void FocusSelected(ListBox list)
    {
        var target = list.SelectedIndex >= 0 ? list.ContainerFromIndex(list.SelectedIndex) : null;
        if (!(target?.Focus(NavigationMethod.Tab) ?? false))
        {
            list.Focus(NavigationMethod.Tab);
        }
    }

    private static Button Button(string field, string text, Action action)
    {
        var button = new Button { Content = text, MinWidth = 0 };
        AutomationProperties.SetAutomationId(button, Id(field));
        AutomationProperties.SetName(button, text.Replace("&", "", StringComparison.Ordinal));
        button.Click += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return button;
    }

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
