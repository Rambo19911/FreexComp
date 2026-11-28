using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Menu Designer (dialogs.md §26, DSG-SUR-197, 199; D-098): 720 × 460, resizable; the item tree (260 px,
/// <c>Dialog.MenuDesigner.Node.&lt;ComponentName&gt;</c>) with the operation buttons under it, the Caption and ShortCut
/// fields and the embedded property grid (the Object Inspector's grid control, D-110) with its Properties / Events page
/// tabs. Keys in the tree: Insert, Ctrl+Insert, Ctrl+Right, Delete, Ctrl+Up / Ctrl+Down, F2 (Caption), Enter or a
/// double-click (the OnClick handler). Close (Enter / Escape / ×) only closes: every operation is already applied.
/// Items that share an access key with another item of their level carry the warning mark, and the warning line on the
/// button strip (<c>Dialog.MenuDesigner.Warning</c>) names the current warning (DSG-SUR-252, UI-DLG-340; D-155).
/// Items move by a drag in the tree — before, after or into the row under the pointer, shown by the insertion line
/// (<c>Dialog.MenuDesigner.InsertionLine</c>) or the row highlight, Escape cancels the drag only — and by
/// Ctrl+Shift+Left (move out) / Ctrl+Shift+Right (move in) (DSG-SUR-253…257, UI-DLG-341; D-156).
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
        SetStripInfo(parts.Warning);

        // DSG-SUR-255: Escape during a drag cancels the drag only — consumed before the dialog takes it as Close.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (parts.Dragging && e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelDrag(parts);
            }
        }, RoutingStrategies.Tunnel);
        // The dialog opens with the focus in the tree, on the selected item (after the base focus pass).
        Opened += (_, _) => Dispatcher.UIThread.Post(() => FocusSelected(parts), DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(MenuDesignerViewModel.DialogName, field);

    /// <summary>DSG-SUR-253: the pointer distance (DIP) that turns a press on a node into a drag.</summary>
    private const double DragThreshold = 4;

    /// <summary>Style class of the node that is the "into" target of a drag (UI-DLG-341).</summary>
    private const string DropTargetClass = "dropTarget";

    /// <summary>The controls the handlers share.</summary>
    private sealed class Parts
    {
        public TreeView Tree { get; } = new() { SelectionMode = SelectionMode.Single, Focusable = true }; // also when empty (Insert)

        public TextBox Caption { get; } = new();

        public ComboBox ShortCut { get; } = new() { IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };

        public PropertyGridView Grid { get; } = new(Id("Grid"), Id("Grid.Row.")) { IsInDialog = true, BorderThickness = new Thickness(1) };

        /// <summary>UI-DLG-340: the warning line of the button strip — the icon and one trimmed line; hidden without a warning.</summary>
        public DockPanel Warning { get; } = new()
        {
            IsVisible = false, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent /* the tooltip on the whole line */,
            Margin = new Thickness(0, 0, 8, 0), // space.4 before Close
        };

        public TextBlock WarningText { get; } = new()
        {
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0), // space.3 between icon and text
        };

        public Dictionary<FormObject, TreeViewItem> Nodes { get; } = new(ReferenceEqualityComparer.Instance);

        public bool Syncing { get; set; }

        /// <summary>UI-DLG-341: the layer over the tree that shows the drop position; it takes no input.</summary>
        public Canvas DropLayer { get; } = new() { IsHitTestVisible = false, ClipToBounds = true };

        /// <summary>The insertion line (before / after): 2 px, accent.</summary>
        public Border DropLine { get; } = new() { Height = 2, IsVisible = false };

        /// <summary>The 1 px accent outline of the "into" row (its fill is the row's own background).</summary>
        public Border DropOutline { get; } = new() { BorderThickness = new Thickness(1), IsVisible = false };

        /// <summary>The pressed node that may become a drag, and where it was pressed (tree coordinates).</summary>
        public (Point At, FormObject Item)? Press { get; set; }

        public bool Dragging { get; set; }

        /// <summary>The last pointer position of the drag (tree coordinates): the wheel scrolls under it.</summary>
        public Point DragAt { get; set; }

        public IPointer? Pointer { get; set; }

        public TreeViewItem? Highlighted { get; set; }

        public Cursor? NoCursor { get; set; }
    }

    /// <summary>A drop position of the running drag: the target item, where, and whether the item may go there (DSG-SUR-255).</summary>
    private readonly record struct DropTarget(FormObject Item, MenuDropPosition Position, bool IsValid);

    private static Grid Body(MenuDesignerViewModel model, Parts p)
    {
        var itemsLabel = new Label { Content = MnemonicLabel.ToAccessText(Strings.MenuDesigner_Items), Target = p.Tree, Padding = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetAutomationId(p.Tree, Id("Tree"));
        AutomationProperties.SetName(p.Tree, MnemonicLabel.ToPlainText(Strings.MenuDesigner_Items));
        var placeholder = new TextBlock { Text = model.EmptyText, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
        placeholder.Bind(TextBlock.ForegroundProperty, placeholder.GetResourceObservable("FxcTextTertiaryBrush"));
        AutomationProperties.SetAutomationId(placeholder, Id("Tree.Empty"));
        // UI-DLG-341: the drop feedback over the tree, in accent; the "into" row itself is filled with bg.selected.inactive.
        p.DropLine.Bind(Border.BackgroundProperty, p.DropLine.GetResourceObservable("FxcAccentDefaultBrush"));
        p.DropOutline.Bind(Border.BorderBrushProperty, p.DropOutline.GetResourceObservable("FxcAccentDefaultBrush"));
        AutomationProperties.SetAutomationId(p.DropLine, Id("InsertionLine"));
        p.DropLayer.Children.Add(p.DropLine);
        p.DropLayer.Children.Add(p.DropOutline);
        var dropTarget = new Style(x => x.OfType<TreeViewItem>().Class(DropTargetClass).Template().OfType<Border>().Name("PART_LayoutRoot"));
        dropTarget.Setters.Add(new Setter(Border.BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("FxcBgSelectedInactiveBrush")));
        p.Tree.Styles.Add(dropTarget);
        var treeFrame = new Border { BorderThickness = new Thickness(1), Child = new Panel { Children = { p.Tree, p.DropLayer, placeholder } } };
        treeFrame.Bind(Border.BorderBrushProperty, treeFrame.GetResourceObservable("FxcBorderDefaultBrush"));

        var newItem = Button("NewItem", Strings.MenuDesigner_NewItem, model.NewItem);
        var insertBefore = Button("InsertBefore", Strings.MenuDesigner_InsertBefore, model.InsertBefore);
        var newSubItem = Button("NewSubItem", Strings.MenuDesigner_NewSubItem, model.NewSubItem);
        var newSeparator = Button("NewSeparator", Strings.MenuDesigner_NewSeparator, model.NewSeparator);
        var delete = Button("Delete", Strings.MenuDesigner_Delete, () => _ = model.DeleteAsync() /* a question box returns the focus here (D-112) */);
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
        AutomationProperties.SetName(p.Caption, MnemonicLabel.ToPlainText(Strings.MenuDesigner_Caption));
        AutomationProperties.SetAutomationId(p.ShortCut, Id("ShortCut"));
        AutomationProperties.SetName(p.ShortCut, MnemonicLabel.ToPlainText(Strings.MenuDesigner_ShortCut));
        p.ShortCut.ItemsSource = model.ShortCutChoices;
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*"), RowDefinitions = new RowDefinitions("Auto,6,Auto") };
        Place(fields, new Label { Content = MnemonicLabel.ToAccessText(Strings.MenuDesigner_Caption), Target = p.Caption, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0) }, 0, 0);
        Place(fields, p.Caption, 0, 2);
        Place(fields, new Label { Content = MnemonicLabel.ToAccessText(Strings.MenuDesigner_ShortCut), Target = p.ShortCut, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0) }, 2, 0);
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

        // UI-DLG-340: the warning line (placed on the button strip by the window); announced politely, not a tab stop.
        var warningIcon = new IdeIcon { IconName = "warning", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(warningIcon, Dock.Left);
        p.Warning.Children.Add(warningIcon);
        p.Warning.Children.Add(p.WarningText);
        p.WarningText.Bind(TextBlock.ForegroundProperty, p.WarningText.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(p.Warning, Id("Warning"));
        AutomationProperties.SetLiveSetting(p.Warning, AutomationLiveSetting.Polite);

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
            if (p.Dragging)
            {
                CancelDrag(p); // the nodes are rebuilt: a drag over the old ones ends (a change behind the dialog)
            }

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

            var warning = model.WarningText;
            p.Warning.IsVisible = warning.Length > 0; // hidden: it takes no room
            p.WarningText.Text = warning;
            AutomationProperties.SetName(p.Warning, warning);
            ToolTip.SetTip(p.Warning, warning.Length > 0 ? warning : null); // the full text when the line is trimmed
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

        // DSG-SUR-253…256: a press on a node selects it (the tree's own handling) and may become a drag of that item.
        p.Tree.AddHandler(PointerPressedEvent, (_, e) =>
        {
            p.Press = null;
            if (e.GetCurrentPoint(p.Tree).Properties.IsLeftButtonPressed && e.KeyModifiers == KeyModifiers.None && e.Source is Visual source
                && source.FindAncestorOfType<Avalonia.Controls.Primitives.ToggleButton>(includeSelf: true) is null // not the expander of a node
                && source.FindAncestorOfType<TreeViewItem>(includeSelf: true) is { Tag: FormObject item }) // not the scroll bar, not the empty area
            {
                p.Press = (e.GetPosition(p.Tree), item);
            }
        }, RoutingStrategies.Tunnel);
        p.Tree.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (p.Press is not { } press)
            {
                return;
            }

            var at = e.GetPosition(p.Tree);
            if (!p.Dragging)
            {
                if (Math.Abs(at.X - press.At.X) < DragThreshold && Math.Abs(at.Y - press.At.Y) < DragThreshold)
                {
                    return;
                }

                if (!model.CanMove(press.Item))
                {
                    p.Press = null; // a fixed item, a form that cannot be changed: the press stays a selection
                    return;
                }

                e.Pointer.Capture(p.Tree);
                p.Pointer = e.Pointer;
                p.Dragging = true;
            }

            e.Handled = true;
            p.DragAt = at;
            ShowDropTarget(p, TargetAt(model, p, press.Item, at));
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        p.Tree.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (p.Press is not { } press)
            {
                return;
            }

            if (!p.Dragging)
            {
                p.Press = null;
                return;
            }

            e.Handled = true;
            var target = TargetAt(model, p, press.Item, e.GetPosition(p.Tree));
            CancelDrag(p);
            if (target is { IsValid: true } drop)
            {
                model.MoveTo(press.Item, drop.Item, drop.Position); // one "Order" unit; false: the present place, or the form refused
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        p.Tree.PointerCaptureLost += (_, _) => CancelDrag(p); // DSG-SUR-255: losing the capture cancels

        // DSG-SUR-254: the tree does not scroll by itself during a drag, the wheel does (the captured pointer sends it here).
        p.Tree.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (!p.Dragging || p.Press is not { } press || p.Tree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroll)
            {
                return;
            }

            e.Handled = true;
            var y = Math.Clamp(scroll.Offset.Y - (e.Delta.Y * 50), 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
            scroll.Offset = scroll.Offset.WithY(y);
            p.Tree.UpdateLayout(); // the rows are at their new places before the position under the pointer is taken
            ShowDropTarget(p, TargetAt(model, p, press.Item, p.DragAt));
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
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

            // The view-model raises its properties together after every change, Nodes among them: one rebuild per change.
            if (e.PropertyName == nameof(MenuDesignerViewModel.Nodes))
            {
                Sync();
            }
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
                _ = model.DeleteAsync();
                break;
            case (Key.Up, KeyModifiers.Control):
                model.MoveUp();
                break;
            case (Key.Down, KeyModifiers.Control):
                model.MoveDown();
                break;
            case (Key.Left, KeyModifiers.Control | KeyModifiers.Shift):
                model.MoveOut(); // DSG-SUR-257
                break;
            case (Key.Right, KeyModifiers.Control | KeyModifiers.Shift):
                model.MoveIn();
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

    /// <summary>
    /// A tree node: the caption as stored, the shortcut right-aligned (text.secondary); a separator is a rule. An item of a
    /// duplicate-access-key warning has the mark after its caption, the warning as tooltip and ", duplicate access key" at
    /// the end of its automation name (UI-DLG-340).
    /// </summary>
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
            if (node.Warning is not null)
            {
                DockPanel.SetDock(caption, Dock.Left);
                cells.Children.Add(new IdeIcon
                {
                    IconName = "warning-corner", Width = 10, Height = 10, Margin = new Thickness(4, 0, 0, 0) /* space.2 */,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                });
                cells.Background = Brushes.Transparent; // the tooltip on the whole row
                ToolTip.SetTip(cells, node.Warning);
            }

            header = cells;
        }

        var item = new TreeViewItem { Header = header, Tag = node.Item, IsExpanded = true };
        AutomationProperties.SetAutomationId(item, Id("Node." + node.Name));
        AutomationProperties.SetName(item, (node.IsSeparator ? node.Name : MenuDesignerViewModel.PlainCaption(node.Text))
            + (node.Warning is null ? "" : Strings.MenuDesigner_DuplicateAccessKeySuffix));
        p.Nodes[node.Item] = item;
        foreach (var child in node.Children)
        {
            item.Items.Add(Node(child, p));
        }

        return item;
    }

    /// <summary>The row of a node — its own line of the tree, without its sub-items — when it is shown.</summary>
    private static Border? RowOf(TreeViewItem node) =>
        node.IsEffectivelyVisible ? node.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_LayoutRoot") : null;

    /// <summary>The item whose row is at <paramref name="at"/> (tree coordinates) and where in the row (0 = upper edge, 1 = lower edge).</summary>
    private static (FormObject Item, double Fraction)? RowAt(MenuDesignerViewModel model, Parts p, Point at)
    {
        foreach (var item in model.Items)
        {
            if (p.Nodes.TryGetValue(item, out var node) && RowOf(node) is { Bounds.Height: > 0 } row && row.TranslatePoint(default, p.Tree) is { } origin
                && at.Y >= origin.Y && at.Y < origin.Y + row.Bounds.Height)
            {
                return (item, (at.Y - origin.Y) / row.Bounds.Height);
            }
        }

        return null;
    }

    /// <summary>
    /// DSG-SUR-254: the drop position at <paramref name="at"/> (tree coordinates) — by the zone of the row under the pointer;
    /// below the last row: after the last top-level item. Null outside the tree and above the first row: no position.
    /// </summary>
    private static DropTarget? TargetAt(MenuDesignerViewModel model, Parts p, FormObject dragged, Point at)
    {
        if (!new Rect(p.Tree.Bounds.Size).Contains(at))
        {
            return null;
        }

        if (RowAt(model, p, at) is { } row)
        {
            var position = MenuDesignerViewModel.DropPositionAt(row.Item, row.Fraction);
            return new DropTarget(row.Item, position, model.CanMoveTo(dragged, row.Item, position));
        }

        return model.Menu.Children.LastOrDefault() is { } last && p.Nodes.TryGetValue(last, out var node)
            && node.TranslatePoint(new Point(0, node.Bounds.Height), p.Tree) is { } end && at.Y >= end.Y
            ? new DropTarget(last, MenuDropPosition.After, model.CanMoveTo(dragged, last, MenuDropPosition.After))
            : null;
    }

    /// <summary>
    /// UI-DLG-341: the feedback of a drag — before / after: the insertion line at the upper edge of the target's row or
    /// under the target with its sub-items, from the indent of the target's level to the right edge of the tree; into: the
    /// row filled and outlined; not valid (or no position): the "no" cursor only. Null with no drag running: nothing.
    /// </summary>
    private static void ShowDropTarget(Parts p, DropTarget? target)
    {
        p.Highlighted?.Classes.Remove(DropTargetClass);
        p.Highlighted = null;
        p.DropLine.IsVisible = false;
        p.DropOutline.IsVisible = false;
        p.Tree.Cursor = p.Dragging && target is not { IsValid: true } ? p.NoCursor ??= new Cursor(StandardCursorType.No) : null;
        if (!p.Dragging || target is not { IsValid: true } drop || !p.Nodes.TryGetValue(drop.Item, out var node) || RowOf(node) is not { } row
            || row.TranslatePoint(default, p.DropLayer) is not { } origin)
        {
            return;
        }

        if (drop.Position == MenuDropPosition.Into)
        {
            node.Classes.Add(DropTargetClass);
            p.Highlighted = node;
            Canvas.SetLeft(p.DropOutline, origin.X);
            Canvas.SetTop(p.DropOutline, origin.Y);
            p.DropOutline.Width = row.Bounds.Width;
            p.DropOutline.Height = row.Bounds.Height;
            p.DropOutline.IsVisible = true;
            return;
        }

        var before = drop.Position == MenuDropPosition.Before;
        var left = (node.Header as Control)?.TranslatePoint(default, p.DropLayer)?.X ?? origin.X;
        var edge = before ? origin.Y : node.TranslatePoint(new Point(0, node.Bounds.Height), p.DropLayer)?.Y ?? origin.Y + row.Bounds.Height;
        Canvas.SetLeft(p.DropLine, left);
        Canvas.SetTop(p.DropLine, Math.Clamp(edge - 1, 0, Math.Max(0, p.DropLayer.Bounds.Height - p.DropLine.Height)));
        p.DropLine.Width = Math.Max(0, p.DropLayer.Bounds.Width - left);
        var name = drop.Item.FindProperty("Caption")?.Value is StringValue { Value: { Length: > 0 } and not "-" and var caption }
            ? MenuDesignerViewModel.PlainCaption(caption) : drop.Item.Name; // a separator and an empty caption: the component name
        AutomationProperties.SetName(p.DropLine, string.Format(System.Globalization.CultureInfo.CurrentCulture,
            before ? Strings.MenuDesigner_DropBefore : Strings.MenuDesigner_DropAfter, name));
        p.DropLine.IsVisible = true;
    }

    /// <summary>DSG-SUR-255: ends a drag without a change (Escape, a lost capture, the release itself): no line, no highlight, no capture.</summary>
    private static void CancelDrag(Parts p)
    {
        var pointer = p.Pointer;
        p.Dragging = false;
        p.Press = null;
        p.Pointer = null;
        ShowDropTarget(p, null);
        pointer?.Capture(null);
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

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
