using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Menu Designer (dialogs.md §26, DSG-SUR-197, 199; D-098): 720 × 460, resizable; the item tree (260 px,
/// <c>Dialog.MenuDesigner.Node.&lt;ComponentName&gt;</c>) with the operation buttons under it, the Caption and ShortCut
/// fields and the embedded property grid (the Object Inspector's grid control, D-110) with its Properties / Events page
/// tabs. Keys in the tree: Insert, Ctrl+Insert, Ctrl+Right, Delete, Ctrl+Up / Ctrl+Down, F2 (Caption), Enter or a
/// double-click (the OnClick handler). Close (Enter / Escape / ×) only closes: every operation is already applied.
/// </summary>
public sealed class MenuDesignerWindow : DialogWindow
{
    public MenuDesignerWindow(MenuDesignerViewModel model)
        : this(model, new Parts())
    {
    }

    private MenuDesignerWindow(MenuDesignerViewModel model, Parts parts)
        : base(model, Body(model, parts))
    {
        // The dialog opens with the focus in the tree, on the selected item (after the base focus pass).
        Opened += (_, _) => Dispatcher.UIThread.Post(() => FocusSelected(parts), DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(MenuDesignerViewModel.DialogName, field);

    /// <summary>The controls the handlers share.</summary>
    private sealed class Parts
    {
        public TreeView Tree { get; } = new() { SelectionMode = SelectionMode.Single, Focusable = true }; // also when empty (Insert)

        public TextBox Caption { get; } = new();

        public ComboBox ShortCut { get; } = new() { IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };

        public PropertyGridView Grid { get; } = new(Id("Grid"), Id("Grid.Row.")) { IsInDialog = true, BorderThickness = new Thickness(1) };

        public Dictionary<FormObject, TreeViewItem> Nodes { get; } = new(ReferenceEqualityComparer.Instance);

        public bool Syncing { get; set; }
    }

    private static Grid Body(MenuDesignerViewModel model, Parts p)
    {
        var itemsLabel = new Label { Content = Strings.MenuDesigner_Items, Target = p.Tree, Padding = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetAutomationId(p.Tree, Id("Tree"));
        AutomationProperties.SetName(p.Tree, Plain(Strings.MenuDesigner_Items));
        var placeholder = new TextBlock { Text = model.EmptyText, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
        placeholder.Bind(TextBlock.ForegroundProperty, placeholder.GetResourceObservable("FxcTextTertiaryBrush"));
        AutomationProperties.SetAutomationId(placeholder, Id("Tree.Empty"));
        var treeFrame = new Border { BorderThickness = new Thickness(1), Child = new Panel { Children = { p.Tree, placeholder } } };
        treeFrame.Bind(Border.BorderBrushProperty, treeFrame.GetResourceObservable("FxcBorderDefaultBrush"));

        var newItem = Button("NewItem", Strings.MenuDesigner_NewItem, model.NewItem);
        var insertBefore = Button("InsertBefore", Strings.MenuDesigner_InsertBefore, model.InsertBefore);
        var newSubItem = Button("NewSubItem", Strings.MenuDesigner_NewSubItem, model.NewSubItem);
        var newSeparator = Button("NewSeparator", Strings.MenuDesigner_NewSeparator, model.NewSeparator);
        var delete = Button("Delete", Strings.MenuDesigner_Delete, () => _ = DeleteAsync(model, p));
        var up = Button("MoveUp", Strings.MenuDesigner_MoveUp, model.MoveUp);
        var down = Button("MoveDown", Strings.MenuDesigner_MoveDown, model.MoveDown);
        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0),
            Children = { newItem, insertBefore, newSubItem, newSeparator, delete, up, down },
        };
        foreach (var button in buttons.Children.OfType<Button>())
        {
            button.Margin = new Thickness(0, 0, 6, 6);
        }

        AutomationProperties.SetAutomationId(p.Caption, Id("Caption"));
        AutomationProperties.SetName(p.Caption, Plain(Strings.MenuDesigner_Caption));
        AutomationProperties.SetAutomationId(p.ShortCut, Id("ShortCut"));
        AutomationProperties.SetName(p.ShortCut, Plain(Strings.MenuDesigner_ShortCut));
        p.ShortCut.ItemsSource = model.ShortCutChoices;
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*"), RowDefinitions = new RowDefinitions("Auto,6,Auto") };
        Place(fields, new Label { Content = Strings.MenuDesigner_Caption, Target = p.Caption, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0) }, 0, 0);
        Place(fields, p.Caption, 0, 2);
        Place(fields, new Label { Content = Strings.MenuDesigner_ShortCut, Target = p.ShortCut, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0) }, 2, 0);
        Place(fields, p.ShortCut, 2, 2);

        // The grid's page tabs (Dialog.MenuDesigner.Grid.Page.Properties / .Events).
        p.Grid.Grid = model.Grid;
        p.Grid.Bind(Border.BorderBrushProperty, p.Grid.GetResourceObservable("FxcBorderDefaultBrush"));
        AutomationProperties.SetName(p.Grid, Strings.MenuDesigner_Grid);
        var properties = PageTab("Grid.Page.Properties", Strings.Inspector_Tab_Properties, () => model.Grid.Page = InspectorPage.Properties);
        var events = PageTab("Grid.Page.Events", Strings.Inspector_Tab_Events, () => model.Grid.Page = InspectorPage.Events);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 8, 0, 4), Children = { properties, events } };

        var right = new DockPanel();
        DockPanel.SetDock(fields, Dock.Top);
        DockPanel.SetDock(tabs, Dock.Top);
        right.Children.Add(fields);
        right.Children.Add(tabs);
        right.Children.Add(p.Grid);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("260,12,*"),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            MinHeight = 260,
        };
        Place(body, itemsLabel, 0, 0);
        Place(body, treeFrame, 1, 0);
        Place(body, buttons, 2, 0);
        Grid.SetColumnSpan(buttons, 3);
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
            var hadFocus = p.Tree.IsKeyboardFocusWithin;
            p.Tree.Items.Clear();
            p.Nodes.Clear();
            foreach (var node in model.Nodes)
            {
                p.Tree.Items.Add(Node(node, p));
            }

            if (model.Selected is { } selected && p.Nodes.TryGetValue(selected, out var item))
            {
                for (var parent = item.Parent as TreeViewItem; parent is not null; parent = parent.Parent as TreeViewItem)
                {
                    parent.IsExpanded = true;
                }

                p.Tree.SelectedItem = item;
            }
            else
            {
                p.Tree.SelectedItem = null;
            }

            placeholder.IsVisible = model.IsEmpty;
            insertBefore.IsEnabled = model.CanInsertBefore;
            newItem.IsEnabled = model.CanNewItem;
            newSubItem.IsEnabled = model.CanNewSubItem;
            newSeparator.IsEnabled = model.CanNewSeparator;
            delete.IsEnabled = model.CanDelete;
            up.IsEnabled = model.CanMoveUp;
            down.IsEnabled = model.CanMoveDown;
            p.Caption.IsEnabled = model.IsCaptionEnabled;
            p.ShortCut.IsEnabled = model.IsShortCutEnabled;
            SyncFields(model, p);
            p.Syncing = false;
            if (hadFocus && !p.Tree.IsKeyboardFocusWithin)
            {
                // Rebuilt nodes lost the focus: the tree keeps it, on the selected node (once its container is laid out).
                FocusSelected(p);
                Dispatcher.UIThread.Post(() =>
                {
                    if (p.Tree.IsKeyboardFocusWithin)
                    {
                        FocusSelected(p); // unless the focus moved on meanwhile (Caption, UI-DLG-250)
                    }
                }, DispatcherPriority.Loaded);
            }
        }

        p.Tree.SelectionChanged += (_, _) =>
        {
            if (!p.Syncing && p.Tree.SelectedItem is TreeViewItem { Tag: FormObject item })
            {
                model.Select(item);
            }
        };
        p.Tree.AddHandler(KeyDownEvent, (_, e) => TreeKey(model, p, e), RoutingStrategies.Tunnel);
        p.Tree.AddHandler(DoubleTappedEvent, (_, e) =>
        {
            if ((e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true) is { Tag: FormObject item })
            {
                e.Handled = true;
                model.Select(item);
                model.ActivateSelected();
            }
        });

        // Caption: Enter and focus loss commit (one "Property" unit, DSG-SUR-197).
        p.Caption.TextChanged += (_, _) =>
        {
            if (!p.Syncing)
            {
                model.CaptionText = p.Caption.Text ?? "";
            }
        };
        p.Caption.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true; // not the dialog's Close
                model.CommitCaption();
            }
        }, RoutingStrategies.Tunnel);
        p.Caption.LostFocus += (_, _) =>
        {
            if (!p.Syncing)
            {
                model.CommitCaption();
            }
        };

        // ShortCut: a choice from the list, Enter and focus loss commit.
        p.ShortCut.SelectionChanged += (_, _) =>
        {
            if (!p.Syncing && p.ShortCut.SelectedItem is string choice)
            {
                model.ShortCutText = choice;
                model.CommitShortCut();
            }
        };
        p.ShortCut.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && !p.ShortCut.IsDropDownOpen)
            {
                e.Handled = true;
                model.ShortCutText = p.ShortCut.Text ?? "";
                model.CommitShortCut();
            }
        }, RoutingStrategies.Tunnel);
        p.ShortCut.LostFocus += (_, _) =>
        {
            if (!p.Syncing && !p.ShortCut.IsKeyboardFocusWithin && p.ShortCut.Text != model.ShortCutText)
            {
                model.ShortCutText = p.ShortCut.Text ?? "";
                model.CommitShortCut();
            }
        };

        // UI-DLG-250: after New Item / Insert Before / New Sub-Item the caption is typed at once.
        model.CaptionFocusRequested += (_, _) =>
        {
            p.Caption.Focus(NavigationMethod.Tab);
            p.Caption.SelectAll();
        };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MenuDesignerViewModel.CaptionText) or nameof(MenuDesignerViewModel.ShortCutText))
            {
                if (!p.Syncing)
                {
                    p.Syncing = true;
                    SyncFields(model, p);
                    p.Syncing = false;
                }

                return;
            }

            Sync();
        };
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

    private static void SyncFields(MenuDesignerViewModel model, Parts p)
    {
        if (!string.Equals(p.Caption.Text, model.CaptionText, StringComparison.Ordinal))
        {
            p.Caption.Text = model.CaptionText;
        }

        if (!string.Equals(p.ShortCut.Text, model.ShortCutText, StringComparison.Ordinal))
        {
            p.ShortCut.SelectedItem = model.ShortCutChoices.Contains(model.ShortCutText) ? model.ShortCutText : null;
            p.ShortCut.Text = model.ShortCutText;
        }
    }

    private static void TreeKey(MenuDesignerViewModel model, Parts p, KeyEventArgs e)
    {
        switch (e.Key, e.KeyModifiers)
        {
            case (Key.Insert, KeyModifiers.None):
                model.NewItem();
                break;
            case (Key.Insert, KeyModifiers.Control):
                model.InsertBefore();
                break;
            case (Key.Right, KeyModifiers.Control):
                model.NewSubItem();
                break;
            case (Key.Delete, KeyModifiers.None):
                _ = DeleteAsync(model, p);
                break;
            case (Key.Up, KeyModifiers.Control):
                model.MoveUp();
                break;
            case (Key.Down, KeyModifiers.Control):
                model.MoveDown();
                break;
            case (Key.F2, KeyModifiers.None):
                if (!model.IsCaptionEnabled)
                {
                    return;
                }

                p.Caption.Focus(NavigationMethod.Tab);
                p.Caption.SelectAll();
                break;
            case (Key.Enter, KeyModifiers.None):
                model.ActivateSelected();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>A tree node: the caption as stored, the shortcut right-aligned (text.secondary); a separator is a rule.</summary>
    private static TreeViewItem Node(MenuDesignerNode node, Parts p)
    {
        Control header;
        if (node.IsSeparator)
        {
            var rule = new Border { Height = 1, MinWidth = 120, Margin = new Thickness(0, 6), VerticalAlignment = VerticalAlignment.Center };
            rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("FxcBorderStrongBrush"));
            header = rule;
        }
        else
        {
            var caption = new TextBlock { Text = node.Text, VerticalAlignment = VerticalAlignment.Center };
            if (node.IsCaptionEmpty)
            {
                caption.Bind(TextBlock.ForegroundProperty, caption.GetResourceObservable("FxcTextTertiaryBrush"));
            }

            var shortcut = new TextBlock { Text = node.ShortCut, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            shortcut.Bind(TextBlock.ForegroundProperty, shortcut.GetResourceObservable("FxcTextSecondaryBrush"));
            var cells = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(shortcut, Dock.Right);
            cells.Children.Add(shortcut);
            cells.Children.Add(caption);
            header = cells;
        }

        var item = new TreeViewItem { Header = header, Tag = node.Item, IsExpanded = true };
        AutomationProperties.SetAutomationId(item, Id("Node." + node.Name));
        AutomationProperties.SetName(item, node.IsSeparator ? node.Name : Plain(node.Text));
        p.Nodes[node.Item] = item;
        foreach (var child in node.Children)
        {
            item.Items.Add(Node(child, p));
        }

        return item;
    }

    /// <summary>
    /// Delete (UI-DLG-251); when it asked first, the message box gave the focus back to the main window: the tree takes it
    /// again, on the selected node.
    /// </summary>
    private static async Task DeleteAsync(MenuDesignerViewModel model, Parts p)
    {
        var asks = model.Selected is { Children.Count: > 0 };
        await model.DeleteAsync();
        if (asks && p.Tree.IsAttachedToVisualTree())
        {
            (TopLevel.GetTopLevel(p.Tree) as Window)?.Activate();
            FocusSelected(p);
            Dispatcher.UIThread.Post(() => FocusSelected(p), DispatcherPriority.Loaded);
        }
    }

    private static void FocusSelected(Parts p)
    {
        if (p.Tree.SelectedItem is TreeViewItem item && item.Focus(NavigationMethod.Tab))
        {
            return;
        }

        p.Tree.Focus(NavigationMethod.Tab);
    }

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
