using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Input;

/// <summary>
/// Behaviours every IDE list, tree and grid shares, registered once as class handlers (no per-view code):
/// prefix type-ahead (UI-A11Y-007) and the full text of a trimmed cell as its tooltip (UI-TW-016).
/// </summary>
public static class ListBehaviors
{
    private static readonly ConditionalWeakTable<Control, TypeAheadSearch> Searches = [];
    private static readonly ConditionalWeakTable<TextBlock, object> OwnToolTips = [];
    private static readonly ConditionalWeakTable<TreeView, Control> Anchors = [];
    private static readonly IClock FallbackClock = new Platform.DispatcherClock();
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;
        _registered = true;
        InputElement.TextInputEvent.AddClassHandler<ListBox>((list, e) => OnTextInput(list, e));
        InputElement.TextInputEvent.AddClassHandler<TreeView>((tree, e) => OnTextInput(tree, e));
        InputElement.KeyDownEvent.AddClassHandler<TreeView>((tree, e) => OnTreeKeyDown(tree, e), RoutingStrategies.Tunnel); // before the tree's own keys
        InputElement.KeyDownEvent.AddClassHandler<ListBox>((list, e) => OpenContextMenuByKey(list, e), RoutingStrategies.Tunnel);
        Visual.BoundsProperty.Changed.AddClassHandler<TextBlock>((text, _) => ScheduleTrimCheck(text));
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((text, _) => ScheduleTrimCheck(text));
    }

    /// <summary>
    /// UI-TW-014 (I-281, I-283): the commands of a tool window's context menu take their rows from the tool window that
    /// has the focus, and the toolkit moves the focus on a left press only. A right press on <paramref name="list"/>
    /// moves it the same way, so the menu belongs to the right-clicked row whatever had the focus before. (The list
    /// has selected the row by then: its own handlers run first.)
    /// </summary>
    public static void FocusOnRightPress(Control list)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(list).Properties.IsRightButtonPressed)
                FocusPressed(e);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// Moves the keyboard focus to the nearest focusable element around the pressed one — the row, or the list itself
    /// for its empty area — as a left press does.
    /// </summary>
    public static void FocusPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        for (var v = e.Source as Visual; v is not null; v = v.GetVisualParent())
        {
            if (v is InputElement { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true } target)
            {
                target.Focus(NavigationMethod.Pointer);
                break;
            }
        }
    }

    /// <summary>The services' clock through the window (a dialog: its owner); the system clock otherwise.</summary>
    private static IClock ClockOf(Visual visual)
    {
        var top = TopLevel.GetTopLevel(visual);
        var main = top as Window;
        while (main?.Owner is Window owner)
            main = owner;
        return (main?.DataContext as MainWindowViewModel)?.Clock ?? FallbackClock;
    }

    private static void OnTextInput(Control list, TextInputEventArgs e)
    {
        if (e.Handled || string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl)
            || (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is not null
            || list is ListBox { IsTextSearchEnabled: true })
            return;

        var rows = Rows(list);
        if (rows.Count == 0)
            return;
        var search = Searches.GetValue(list, l => new TypeAheadSearch(ClockOf(l)));
        var current = rows.FindIndex(r => r.Container is ListBoxItem { IsSelected: true } or TreeViewItem { IsSelected: true });
        var index = search.Type(e.Text, [.. rows.Select(r => r.Text)], current);
        e.Handled = true;
        if (index < 0)
            return;

        var target = rows[index].Container;
        switch (list)
        {
            case ListBox box:
                box.SelectedItems?.Clear();
                box.SelectedIndex = box.IndexFromContainer(target);
                break;
            case TreeView tree:
                tree.SelectedItems.Clear();
                tree.SelectedItem = target.DataContext is { } item && tree.ItemsSource is not null ? item : target;
                break;
        }

        target.BringIntoView();
        target.Focus(NavigationMethod.Directional);
    }

    /// <summary>
    /// UI-A11Y-007 in trees: Home / End first / last row, PageUp / PageDown by a page of rows; Shift+Up / Shift+Down extend
    /// a multi-selection from its anchor (UI-TW-015); Shift+F10 / Menu key open the context menu.
    /// </summary>
    private static void OnTreeKeyDown(TreeView tree, KeyEventArgs e)
    {
        if (e.Handled || (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
            return;
        if (OpenContextMenuByKey(tree, e))
            return;
        var extend = e.KeyModifiers == KeyModifiers.Shift && e.Key is Key.Up or Key.Down && tree.SelectionMode.HasFlag(SelectionMode.Multiple);
        if (!extend && (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Home or Key.End or Key.PageUp or Key.PageDown)))
            return;
        var rows = Rows(tree);
        if (rows.Count == 0)
            return;
        var current = rows.FindIndex(r => r.Container.IsFocused);
        if (current < 0)
            current = Math.Max(0, rows.FindIndex(r => r.Container is TreeViewItem { IsSelected: true }));
        var page = Math.Max(1, (int)(tree.Bounds.Height / Math.Max(1, rows[0].Container.Bounds.Height)) - 1);
        var index = e.Key switch
        {
            Key.Home => 0,
            Key.End => rows.Count - 1,
            Key.PageUp => Math.Max(0, current - page),
            Key.PageDown => Math.Min(rows.Count - 1, current + page),
            Key.Up => Math.Max(0, current - 1),
            _ => Math.Min(rows.Count - 1, current + 1),
        };
        var target = rows[index].Container;
        if (extend)
        {
            if (!Anchors.TryGetValue(tree, out var anchor) || anchor is not TreeViewItem { IsSelected: true } || !rows.Any(r => r.Container == anchor))
                anchor = rows[current].Container;
            Anchors.AddOrUpdate(tree, anchor);
            var from = rows.FindIndex(r => r.Container == anchor);
            tree.SelectedItems.Clear();
            for (var i = Math.Min(from, index); i <= Math.Max(from, index); i++)
                tree.SelectedItems.Add(ItemOf(tree, rows[i].Container));
        }
        else
        {
            Anchors.Remove(tree);
            tree.SelectedItems.Clear();
            tree.SelectedItem = ItemOf(tree, target);
        }

        target.BringIntoView();
        target.Focus(extend ? NavigationMethod.Unspecified : NavigationMethod.Directional); // directional focus would replace the selection
        e.Handled = true;
    }

    private static object ItemOf(TreeView tree, Control container) =>
        container.DataContext is { } item && tree.ItemsSource is not null ? item : container;

    /// <summary>UI-A11Y-007 / UI-MENU-043: Shift+F10 or the Menu key opens the list's context menu at the focused row.</summary>
    private static bool OpenContextMenuByKey(Control list, KeyEventArgs e)
    {
        if (e.Handled || !(e.Key == Key.Apps && e.KeyModifiers == KeyModifiers.None || e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift))
            return false;
        var row = Rows(list).FirstOrDefault(r => r.Container.IsFocused || r.Container.IsKeyboardFocusWithin).Container;
        var source = row ?? list;
        var args = new ContextRequestedEventArgs();
        source.RaiseEvent(args);
        var owner = source.ContextMenu is not null ? source : list;
        if (!args.Handled && owner.ContextMenu is { } menu)
        {
            menu.Placement = PlacementMode.Bottom;
            menu.PlacementTarget = source; // at the focused row
            menu.Open(owner);
        }

        e.Handled = true;
        return true;
    }
    /// <summary>The realized, visible rows in screen order with their text (automation name, else the content's text).</summary>
    private static List<(Control Container, string Text)> Rows(Control list)
    {
        IEnumerable<Control> containers = list is TreeView
            ? list.GetVisualDescendants().OfType<TreeViewItem>().Where(i => i.IsEffectivelyVisible && i.FindAncestorOfType<TreeView>() == list)
            : list.GetVisualDescendants().OfType<ListBoxItem>().Where(i => i.IsEffectivelyVisible && i.FindAncestorOfType<ListBox>() == list);
        return [.. containers
            .Select(c => (Container: c, Y: c.TranslatePoint(default, list)?.Y ?? double.MaxValue, Text: TextOf(c)))
            .OrderBy(r => r.Y)
            .Select(r => (r.Container, r.Text))];
    }

    private static string TextOf(Control container)
    {
        if (AutomationProperties.GetName(container) is { Length: > 0 } name)
            return name;
        if (container is ContentControl { Content: string text })
            return text;
        return container.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrEmpty(t.Text))?.Text
            ?? container.DataContext?.ToString() ?? "";
    }

    private static void ScheduleTrimCheck(TextBlock text)
    {
        if (text.TextTrimming == TextTrimming.None || !IsCell(text))
            return;
        Dispatcher.UIThread.Post(() => UpdateTrimToolTip(text), DispatcherPriority.Loaded);
    }

    private static bool IsCell(Visual text) =>
        text.FindAncestorOfType<ListBoxItem>() is not null || text.FindAncestorOfType<TreeViewItem>() is not null;

    /// <summary>UI-TW-016: a trimmed cell shows its full text as tooltip; a tooltip set by the view is left alone.</summary>
    private static void UpdateTrimToolTip(TextBlock text)
    {
        var own = OwnToolTips.TryGetValue(text, out _);
        if (ToolTip.GetTip(text) is not null && !own)
            return;
        if (text.GetVisualAncestors().OfType<Control>().TakeWhile(a => a is not ListBox and not TreeView).Any(a => ToolTip.GetTip(a) is not null))
            return; // the row's own tooltip (Project Manager: the full path) already shows more
        var trimmed = text.TextLayout.TextLines.Any(l => l.HasCollapsed);
        if (trimmed)
        {
            ToolTip.SetTip(text, text.Text);
            OwnToolTips.AddOrUpdate(text, text);
        }
        else if (own)
        {
            ToolTip.SetTip(text, null);
            OwnToolTips.Remove(text);
        }
    }
}
