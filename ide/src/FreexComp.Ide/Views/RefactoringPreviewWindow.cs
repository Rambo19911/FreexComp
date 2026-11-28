using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Preview dialog of a refactoring (<c>RefactoringPreview</c>, UI-DLG-360…367), 960 × 600, resizable down to
/// 720 × 420: the tree <c>Tree</c> of file rows (tri-state check box, expander, icon, name, number of edits, directory)
/// and edit rows (check box, line, the line's text with the replaced range on <c>diff.removedInline</c> followed by the
/// inserted text on <c>diff.addedInline</c>), the summary line, a splitter, and the diff view of the current row's file
/// (<see cref="DiffDocumentView"/>, with its own <c>Diff.*</c> ids). Space toggles the focused row and with it every
/// selected row; Left and Right collapse and expand a file row. The first focus is on the tree's first row.
/// In the confirm mode (UI-DLG-387) the rows have no check box, Space does nothing, and the note — when there is one —
/// is a wrapping line <c>Note</c> below the tree and the diff.
/// </summary>
public sealed class RefactoringPreviewWindow : DialogWindow
{
    private const double SplitterWidth = 5; // metric: splitter hit area
    private const double RowHeight = 22;    // UI-TW-010
    private const double BoxSize = 14;      // metric: check box

    public RefactoringPreviewWindow(RefactoringPreviewViewModel model)
        : this(model, new Parts())
    {
    }

    private RefactoringPreviewWindow(RefactoringPreviewViewModel model, Parts parts)
        : base(model, Body(model, parts))
    {
        // UI-DLG-360: the first focus is on the tree, on its first file row.
        Opened += (_, _) => Dispatcher.UIThread.Post(() => FocusCurrent(model, parts.List), DispatcherPriority.Loaded);
        Closed += (_, _) => model.TreeWidth = parts.TreeColumn.ActualWidth;
    }

    /// <summary>The controls the window needs after the body is built.</summary>
    private sealed class Parts
    {
        public ListBox List { get; } = new() { SelectionMode = SelectionMode.Multiple, Focusable = true };

        public ColumnDefinition TreeColumn { get; } = new() { MinWidth = RefactoringPreviewViewModel.MinimumTreeWidth };

        /// <summary>The list's selection is being set from the model.</summary>
        public bool Syncing { get; set; }
    }

    private static string Id(string field) => AutomationIds.DialogField(RefactoringPreviewViewModel.DialogName, field);

    private static void FocusCurrent(RefactoringPreviewViewModel model, ListBox list)
    {
        if (model.CurrentRow is { } row && list.ContainerFromItem(row) is { } container)
            container.Focus(NavigationMethod.Tab);
        else
            list.Focus(NavigationMethod.Tab);
    }

    private static Control Body(RefactoringPreviewViewModel model, Parts parts)
    {
        var list = parts.List;
        AutomationProperties.SetAutomationId(list, Id("Tree"));
        AutomationProperties.SetName(list, MnemonicLabel.ToPlainText(model.ChangesLabel).TrimEnd(':'));
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(MinHeightProperty, RowHeight) },
        });
        list.ItemTemplate = new FuncDataTemplate<RefactoringPreviewRow>((row, _) => row is null ? new Border() : CreateRow(model, row), supportsRecycling: false);
        list.ItemsSource = model.Rows;
        list.SelectedItem = model.CurrentRow;
        list.SelectionChanged += (_, e) =>
        {
            if (parts.Syncing)
                return;
            var selected = list.SelectedItems?.OfType<RefactoringPreviewRow>().ToList() ?? [];
            model.SelectedRows = selected;

            // The row the selection was moved to is the focused one; when rows left the selection, one that stayed.
            if (e.AddedItems.OfType<RefactoringPreviewRow>().LastOrDefault() is { } added)
                model.CurrentRow = added;
            else if (model.CurrentRow is null || !selected.Contains(model.CurrentRow))
                model.CurrentRow = selected.FirstOrDefault() ?? model.CurrentRow;
        };
        list.GotFocus += (_, e) =>
        {
            if (ReferenceEquals(e.Source, list))
                FocusCurrent(model, list); // the label's mnemonic lands on the list itself
        };
        list.AddHandler(KeyDownEvent, (_, e) => OnListKeyDown(model, list, e), RoutingStrategies.Tunnel);
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RefactoringPreviewViewModel.Rows))
                ShowRows(model, parts);
        };

        var label = new Label { Content = MnemonicLabel.ToAccessText(model.ChangesLabel), Target = list, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(TemplatedControl.ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));

        // The summary: a live region whose name follows its text.
        var summary = new TextBlock { Classes = { "body" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        summary.Bind(TextBlock.ForegroundProperty, summary.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(summary, Id("Summary"));
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        void ShowSummary()
        {
            summary.Text = model.Summary;
            AutomationProperties.SetName(summary, model.Summary);
        }

        ShowSummary();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RefactoringPreviewViewModel.Summary))
                ShowSummary();
        };

        // The diff view of the current row's file; it never takes the focus from the tree by itself.
        var view = new DiffDocumentView(embedded: true) { DataContext = model.Diff, IsVisible = model.Diff is not null };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(RefactoringPreviewViewModel.Diff))
                return;
            view.DataContext = model.Diff;
            view.IsVisible = model.Diff is not null;
        };
        var diff = Frame(view, new Thickness(0, 1, 1, 1));
        AutomationProperties.SetAutomationId(diff, Id("Diff"));

        // The splitter (a child of the grid whose columns it resizes): 5 wide, drawn as a 1 px line in its middle; the
        // frame behind it continues the frames of its neighbours.
        var splitter = new GridSplitter
        {
            ResizeDirection = GridResizeDirection.Columns, Width = SplitterWidth, MinWidth = 0, Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
        };
        AutomationProperties.SetAutomationId(splitter, Id("Splitter"));
        var line = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Center };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("FxcBorderDefaultBrush"));
        var divider = Frame(line, new Thickness(0, 1, 0, 1));
        divider.IsHitTestVisible = false;

        // One grid: the label and the summary above the tree and the diff; the summary starts above the diff.
        parts.TreeColumn.Width = new GridLength(model.TreeWidth);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        body.ColumnDefinitions.Add(parts.TreeColumn);
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = RefactoringPreviewViewModel.MinimumDiffWidth });
        label.Margin = summary.Margin = new Thickness(0, 0, 0, 4);
        Place(body, label, 0, 0);
        Place(body, summary, 0, 2);
        Place(body, Frame(list, new Thickness(1, 1, 0, 1)), 1, 0);
        Place(body, divider, 1, 1);
        Place(body, splitter, 1, 1);
        Place(body, diff, 1, 2);

        // UI-DLG-387: the note of the confirm mode, between the panes and the button strip, as wide as both.
        if (model.Note is { } text)
        {
            var note = new TextBlock { Text = text, Classes = { "body" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }; // space.4
            note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("FxcTextSecondaryBrush"));
            AutomationProperties.SetAutomationId(note, Id("Note"));
            Grid.SetColumnSpan(note, 3);
            Place(body, note, 2, 0);
        }

        // UI-DLG-360: the tree gives way before the diff gets narrower than its minimum.
        body.SizeChanged += (_, e) => parts.TreeColumn.MaxWidth = Math.Max(RefactoringPreviewViewModel.MinimumTreeWidth,
            e.NewSize.Width - SplitterWidth - RefactoringPreviewViewModel.MinimumDiffWidth);
        return body;
    }

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static Border Frame(Control child, Thickness thickness)
    {
        var frame = new Border { BorderThickness = thickness, Child = child };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));
        return frame;
    }

    /// <summary>The rows changed (a file row was collapsed or expanded): the list shows them, selection and focus are kept.</summary>
    private static void ShowRows(RefactoringPreviewViewModel model, Parts parts)
    {
        var list = parts.List;
        var focused = list.IsKeyboardFocusWithin;
        parts.Syncing = true;
        try
        {
            list.ItemsSource = model.Rows;
            foreach (var row in model.SelectedRows)
                list.SelectedItems?.Add(row);
            if (list.SelectedItems is null or { Count: 0 } && model.CurrentRow is { } current)
                list.SelectedItem = current;
        }
        finally
        {
            parts.Syncing = false;
        }

        model.SelectedRows = [.. list.SelectedItems?.OfType<RefactoringPreviewRow>() ?? []];
        if (focused)
        {
            list.UpdateLayout(); // the containers of the new rows exist
            FocusCurrent(model, list);
        }
    }

    /// <summary>
    /// UI-DLG-363: Space toggles the focused row (and the selected rows with it); Left / Right collapse / expand a file row.
    /// UI-DLG-387: in the confirm mode Space does nothing (the model leaves every edit as it is).
    /// </summary>
    private static void OnListKeyDown(RefactoringPreviewViewModel model, ListBox list, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
            return;
        var focused = (TopLevel.GetTopLevel(list)?.FocusManager?.GetFocusedElement() as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        var row = focused?.DataContext as RefactoringPreviewRow ?? model.CurrentRow;
        if (row is null)
            return;
        if (e.Key == Key.Space)
        {
            model.Toggle(row, withSelection: true);
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.Right && row is RefactoringPreviewFile file && file.IsExpanded != (e.Key == Key.Right))
        {
            model.SetExpanded(file, e.Key == Key.Right);
            e.Handled = true;
        }
    }

    private static Control CreateRow(RefactoringPreviewViewModel model, RefactoringPreviewRow row)
    {
        var id = Id(row.Key);
        var panel = new DockPanel { MinHeight = RowHeight, Background = Brushes.Transparent };
        CheckBox? check = null;
        if (row.HasCheckBox) // not in the confirm mode (UI-DLG-387)
        {
            var box = check = new CheckBox
            {
                IsChecked = row.IsChecked, Focusable = false, IsTabStop = false, VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0),
            };
            box.TemplateApplied += CompactCheckBox;
            AutomationProperties.SetAutomationId(box, id + ".Check");
            AutomationProperties.SetName(box, row.AutomationName);
            box.Click += (_, _) =>
            {
                // The model decides the new state (a file row never becomes indeterminate by a click); the box shows it.
                model.Toggle(row);
                box.IsChecked = row.IsChecked;
            };
            DockPanel.SetDock(box, Avalonia.Controls.Dock.Left);
            panel.Children.Add(box);
        }

        // The row's container carries the row's id and name (and a file row's path as tooltip); both follow the check box.
        ListBoxItem? container = null;
        void ShowState()
        {
            if (check is not null)
            {
                check.IsChecked = row.IsChecked;
                AutomationProperties.SetName(check, row.AutomationName);
            }

            if (container is not null)
                AutomationProperties.SetName(container, row.AutomationName);
        }

        void OnRowChanged(object? sender, PropertyChangedEventArgs e) => ShowState();
        panel.AttachedToVisualTree += (_, _) =>
        {
            container = panel.FindAncestorOfType<ListBoxItem>();
            if (container is not null)
            {
                AutomationProperties.SetAutomationId(container, id);
                ToolTip.SetTip(container, (row as RefactoringPreviewFile)?.Path);
            }

            row.PropertyChanged += OnRowChanged;
            ShowState();
        };
        panel.DetachedFromVisualTree += (_, _) =>
        {
            row.PropertyChanged -= OnRowChanged;
            container?.ClearValue(AutomationProperties.AutomationIdProperty); // a recycled container is not this row any more
            container = null;
        };

        if (row is RefactoringPreviewFile file)
            AddFileCells(model, panel, file);
        else
            AddEditCells(panel, (RefactoringPreviewEdit)row, id);
        return panel;
    }

    /// <summary>
    /// design-tokens.md: a check box is a 14 × 14 box in a 22 high row. The box of the theme is 20 in a 32 high cell; its
    /// template sets those sizes, which only local values override (as for the radio buttons of a tool bar).
    /// </summary>
    private static void CompactCheckBox(object? sender, TemplateAppliedEventArgs e)
    {
        if (e.NameScope.Find<Border>("NormalRectangle") is not { } box)
            return;
        box.Width = box.Height = BoxSize;
        if (box.Parent is not Grid cell)
            return;
        cell.Width = BoxSize;
        cell.Height = RowHeight;
        cell.VerticalAlignment = VerticalAlignment.Center;
        if (cell.Parent is Grid { ColumnDefinitions.Count: > 0 } root)
            root.ColumnDefinitions[0].Width = new GridLength(BoxSize);
    }

    /// <summary>UI-DLG-361: expander, icon, name in bodyStrong with the number of edits, the directory right-aligned and start-trimmed.</summary>
    private static void AddFileCells(RefactoringPreviewViewModel model, DockPanel panel, RefactoringPreviewFile file)
    {
        panel.Margin = new Thickness(4, 0);
        var chevron = new Border
        {
            Background = Brushes.Transparent, Width = 16, Height = RowHeight,
            Child = new IdeIcon { IconName = file.IsExpanded ? "chevron-down" : "chevron-right", Width = 8, Height = 8 },
        };
        chevron.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(chevron).Properties.IsLeftButtonPressed)
                return;
            model.SetExpanded(file, !file.IsExpanded);
            e.Handled = true;
        };
        var icon = new IdeIcon { IconName = file.Icon, Width = 16, Height = 16, Margin = new Thickness(2, 0, 4, 0) };
        var name = new TextBlock { Classes = { "body" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Inlines!.Add(new Run(file.FileName) { FontWeight = FontWeight.SemiBold });
        name.Inlines.Add(new Run(string.Create(System.Globalization.CultureInfo.CurrentCulture, $"  ({file.Count})")));
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("FxcTextPrimaryBrush"));

        // The name is shown whole; the directory has the rest of the row and is cut at its start.
        var directory = new TextBlock
        {
            Text = file.Directory, Classes = { "body" }, TextTrimming = TextTrimming.LeadingCharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0),
        };
        directory.Bind(TextBlock.ForegroundProperty, directory.GetResourceObservable("FxcTextTertiaryBrush"));
        var cells = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*") };
        foreach (var (cell, column) in new (Control, int)[] { (chevron, 0), (icon, 1), (name, 2), (directory, 3) })
        {
            Grid.SetColumn(cell, column);
            cells.Children.Add(cell);
        }

        panel.Children.Add(cells);
    }

    /// <summary>UI-DLG-362: the line in a 6-character column, a colon, the line's text with the removed and the inserted span.</summary>
    private static void AddEditCells(DockPanel panel, RefactoringPreviewEdit edit, string id)
    {
        panel.Margin = new Thickness(26, 0, 4, 0);
        var line = new TextBlock { Text = edit.LineText, Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        line.Classes.Add("codeSmall");
        line.Bind(TextBlock.ForegroundProperty, line.GetResourceObservable("FxcTextTertiaryBrush"));
        AutomationProperties.SetAutomationId(line, id + ".Line");
        var colon = new TextBlock { Text = ":  ", VerticalAlignment = VerticalAlignment.Center };
        colon.Bind(TextBlock.ForegroundProperty, colon.GetResourceObservable("FxcTextTertiaryBrush"));
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Classes.Add("code");
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(text, id + ".Text");
        void Add(string part, string? brush)
        {
            if (part.Length == 0)
                return;
            var run = new Run(part);
            if (brush is not null)
                run.Bind(TextElement.BackgroundProperty, text.GetResourceObservable(brush));
            text.Inlines!.Add(run);
        }

        Add(edit.Before, null);
        Add(edit.Removed, "FxcDiffRemovedInlineBrush");
        Add(edit.Inserted, "FxcDiffAddedInlineBrush");
        Add(edit.After, null);
        DockPanel.SetDock(line, Avalonia.Controls.Dock.Left);
        DockPanel.SetDock(colon, Avalonia.Controls.Dock.Left);
        panel.Children.Add(line);
        panel.Children.Add(colon);
        panel.Children.Add(text);
    }
}
