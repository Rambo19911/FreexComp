using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Input;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of the Options › Key Mappings page (dialogs.md §9.5): the key map drop-down <c>KeyMap</c>
/// (phase8-ui-additions.md D3); <c>KeyMappings.Filter</c>, <c>ByShortcut</c>,
/// <c>ResetAll</c>; the grid <c>Commands</c> (rows <c>KeyMappings.Row.&lt;commandId&gt;</c>: command, shortcut, scope);
/// the selected command's chips (<c>KeyMappings.CurrentLabel</c>, <c>KeyMappings.Current.&lt;gesture&gt;</c> with
/// <c>.Remove</c>); the capture box <c>NewShortcut</c> with <c>Error.NewShortcut</c> and <c>Conflict</c>; <c>Assign</c>,
/// <c>ResetCommand</c>.
/// </summary>
internal static class KeyMappingsPageView
{
    private static string Id(string field) => OptionsWindow.Id(field);

    public static Control Create(KeyMappingsPageViewModel model)
    {
        // Key map: the built-in map under the user's assignments (I-253).
        var keyMap = new ComboBox { ItemsSource = model.Presets, DisplayMemberBinding = new Avalonia.Data.Binding(nameof(OptionChoice.Display)), MinWidth = 160, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(keyMap, Id("KeyMap"));
        AutomationProperties.SetName(keyMap, Strings.Options_Keys_KeyMap.Replace("_", "", StringComparison.Ordinal).TrimEnd(':'));
        keyMap.SelectedItem = model.Presets.First(p => p.Value == model.Preset);
        keyMap.SelectionChanged += (_, _) =>
        {
            if (keyMap.SelectedItem is OptionChoice choice)
                model.Preset = choice.Value;
        };
        var keyMapLabel = new Label { Content = Strings.Options_Keys_KeyMap, Target = keyMap, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        keyMapLabel.Bind(TemplatedControl.ForegroundProperty, keyMapLabel.GetResourceObservable("FxcTextPrimaryBrush"));
        var keyMapRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        keyMapRow.Children.Add(keyMapLabel);
        keyMapRow.Children.Add(keyMap);

        // Top row: filter, search by shortcut, Reset All.
        var filter = new TextBox { PlaceholderText = Strings.Options_Keys_Filter, Width = 260 };
        AutomationProperties.SetAutomationId(filter, Id("KeyMappings.Filter"));
        AutomationProperties.SetName(filter, Strings.Options_Keys_Filter);
        filter.TextChanged += (_, _) =>
        {
            if (!model.ByShortcut)
                model.Filter = filter.Text ?? "";
        };
        filter.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (!model.ByShortcut || CaptureKey(e) is not { } captured)
                return;
            model.CaptureFilterKey(captured.Gesture); // UI-KEY-024: a second key after a chord's first key searches the chord
            filter.Text = model.Filter;
        }, RoutingStrategies.Tunnel);
        var byShortcut = new CheckBox { Content = Strings.Options_Keys_ByShortcut, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(byShortcut, Id("ByShortcut"));
        byShortcut.IsCheckedChanged += (_, _) =>
        {
            model.ByShortcut = byShortcut.IsChecked == true;
            filter.Text = model.Filter;
        };
        var resetAll = CreateButton("ResetAll", Strings.Options_Keys_ResetAll);
        resetAll.Click += async (_, _) => await model.ResetAllAsync();
        resetAll.Margin = new Thickness(16, 0, 0, 0);
        var top = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) }; // wraps when the text is wide
        top.Children.Add(filter);
        top.Children.Add(byShortcut);
        top.Children.Add(resetAll);

        // The grid: header and rows.
        var header = Header(model);
        var list = CreateList(model);
        var frame = new Border { BorderThickness = new Thickness(1), Child = list, Height = 240 };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("FxcBgEditorBrush"));

        // Current shortcuts of the selected command: the label, then the chips. One wrapping row (I-342): a chord's chip
        // is wide, and chips that do not fit beside the label go to the next line instead of leaving the page.
        var currentLabel = Text("", "body");
        currentLabel.Margin = new Thickness(0, 8, 8, 0);
        AutomationProperties.SetAutomationId(currentLabel, Id("KeyMappings.CurrentLabel"));
        var currentRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        currentRow.Children.Add(currentLabel);

        // Capture box, Assign, Reset Command; messages under it.
        var capture = new TextBox { IsReadOnly = true, Width = 196, VerticalAlignment = VerticalAlignment.Center }; // a chord fits, and the row stays one line (I-342)
        AutomationProperties.SetAutomationId(capture, Id("NewShortcut"));
        AutomationProperties.SetName(capture, Strings.Options_Keys_NewShortcut.Replace("_", "", StringComparison.Ordinal));
        capture.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (CaptureKey(e) is not { } captured)
                return;
            if (captured.Gesture is { } key)
                model.CaptureKey(key); // UI-KEY-024: two keys in a row are a chord
            else
                model.Capture(null);
        }, RoutingStrategies.Tunnel);
        var assign = CreateButton("Assign", Strings.Options_Keys_Assign);
        assign.Margin = new Thickness(8, 0, 0, 0);
        assign.Click += (_, _) => model.Assign();
        var resetCommand = CreateButton("ResetCommand", Strings.Options_Keys_ResetCommand);
        resetCommand.Margin = new Thickness(8, 0, 0, 0);
        resetCommand.Click += (_, _) => model.ResetCommand();
        var captureLabel = new Label { Content = Strings.Options_Keys_NewShortcut, Target = capture, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        captureLabel.Bind(TemplatedControl.ForegroundProperty, captureLabel.GetResourceObservable("FxcTextPrimaryBrush"));
        var captureRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var control in new Control[] { captureLabel, capture, assign, resetCommand })
            captureRow.Children.Add(control);
        var error = Text("", "caption");
        error.TextWrapping = TextWrapping.Wrap;
        error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable("FxcStatusErrorBrush"));
        AutomationProperties.SetAutomationId(error, Id("Error.NewShortcut"));
        var glyph = Text("⚠ ", "caption");
        glyph.Bind(TextBlock.ForegroundProperty, glyph.GetResourceObservable("FxcStatusWarningBrush"));
        var conflict = Text("", "caption");
        conflict.TextWrapping = TextWrapping.Wrap; // the chord conflicts are long sentences (I-342)
        conflict.Bind(TextBlock.ForegroundProperty, conflict.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(conflict, Id("Conflict"));
        var conflictRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(glyph, Dock.Left);
        conflictRow.Children.Add(glyph);
        conflictRow.Children.Add(conflict);

        var body = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        foreach (var control in new Control[] { keyMapRow, top, header, frame, currentRow, captureRow, error, conflictRow })
            body.Children.Add(control);

        void Update()
        {
            list.SelectedItem = model.SelectedRow;
            keyMap.SelectedItem = model.Presets.First(p => p.Value == model.Preset);
            currentLabel.Text = model.CurrentLabel;
            currentRow.IsVisible = model.SelectedRow is not null;
            if (capture.Text != model.CapturedText)
                capture.Text = model.CapturedText;
            capture.Classes.Set("invalid", model.CaptureError is not null);
            error.Text = model.CaptureError;
            error.IsVisible = model.CaptureError is not null;
            conflict.Text = model.ConflictText;
            conflictRow.IsVisible = model.ConflictText is not null;
            assign.IsEnabled = model.CanAssign;
            resetCommand.IsEnabled = model.SelectedRow is not null;
        }

        void UpdateChips()
        {
            currentRow.Children.RemoveRange(1, currentRow.Children.Count - 1); // everything after the label
            foreach (var chip in model.Current)
                currentRow.Children.Add(CreateChip(model, chip));
        }

        Update();
        UpdateChips();
        model.PropertyChanged += (_, _) => Update();
        model.Current.CollectionChanged += (_, _) => UpdateChips();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is KeyMappingRow row)
                model.SelectedRow = row;
        };
        return body;
    }

    /// <summary>
    /// The key of a key press in a capture box: Backspace clears (null gesture); Tab, Shift+Tab, Escape and modifier
    /// keys are left alone (null result); everything else is captured and handled. The page model makes a chord of two
    /// keys in a row.
    /// </summary>
    private static Captured? CaptureKey(KeyEventArgs e)
    {
        if ((e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift) || (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None))
            return null;
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Back)
        {
            e.Handled = true;
            return new Captured(null);
        }

        if (KeyMapping.FromKeyEvent(e.Key, e.KeyModifiers, e.KeySymbol) is not { } gesture)
            return null;
        e.Handled = true;
        return new Captured(gesture);
    }

    private static Control CreateChip(KeyMappingsPageViewModel model, KeyGestureChip chip)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var text = Text(chip.Text, "body");
        text.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(text, Id("KeyMappings.Current." + chip.Text));
        panel.Children.Add(text);
        if (chip.CanRemove)
        {
            var remove = new Button { Content = "×", Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(4, 0, 0, 0), MinHeight = 0, FontSize = 11 };
            var name = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Options_Keys_Remove, chip.Text);
            AutomationProperties.SetAutomationId(remove, Id("KeyMappings.Current." + chip.Text + ".Remove"));
            AutomationProperties.SetName(remove, name);
            ToolTip.SetTip(remove, name);
            remove.Click += (_, _) => model.Remove(chip.Gesture);
            remove.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
                {
                    e.Handled = true;
                    model.Remove(chip.Gesture);
                }
            };
            panel.Children.Add(remove);
        }

        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Padding = new Thickness(6, 1, 2, 1), Margin = new Thickness(0, 4, 6, 0), Child = panel };
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("FxcBorderDefaultBrush"));
        return border;
    }

    private static ListBox CreateList(KeyMappingsPageViewModel model)
    {
        var list = new ListBox
        {
            ItemsSource = model.VisibleRows,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<KeyMappingRow>((row, _) =>
            {
                var cells = Cells(row?.Text ?? "", row?.ShortcutText ?? "", row?.ScopeText ?? "", "body");
                if (row is null)
                    return cells;
                var shortcut = (TextBlock)cells.Children[1];
                void Changed()
                {
                    shortcut.Text = row.ShortcutText;
                    shortcut.Classes.Set("body", !row.IsUserChanged);
                    shortcut.Classes.Set("bodyStrong", row.IsUserChanged);
                }

                Changed();
                row.PropertyChanged += (_, _) => Changed();
                if (row.IsLaterPhase)
                {
                    foreach (var cell in cells.Children.OfType<TextBlock>())
                        cell.Bind(TextBlock.ForegroundProperty, cell.GetResourceObservable("FxcTextDisabledBrush"));
                }

                return cells;
            }),
        };
        AutomationProperties.SetAutomationId(list, Id("Commands"));
        AutomationProperties.SetName(list, Strings.Options_Keys_Commands);
        list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is not ListBoxItem item || item.DataContext is not KeyMappingRow row)
                return;
            item.Padding = new Thickness(8, 2, 8, 2);
            item.MinHeight = 0;
            AutomationProperties.SetAutomationId(item, Id("KeyMappings.Row." + row.CommandId));
            AutomationProperties.SetName(item, row.Text);
        };
        return list;
    }

    /// <summary>The column headers; Command and Scope sort (I-164), Shortcut does not (dialogs.md §9.5).</summary>
    private static Grid Header(KeyMappingsPageViewModel model)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,220,120"), Margin = new Thickness(9, 0, 9, 2) };
        var shortcut = Text(Strings.Options_Keys_Shortcut, "bodyStrong");
        Grid.SetColumn(shortcut, 1);
        var scope = HeaderButton(Strings.Options_Keys_Scope, "Scope", () => model.SortBy(KeyMappingsSort.Scope));
        Grid.SetColumn(scope, 2);
        grid.Children.Add(HeaderButton(Strings.Options_Keys_Command, "Command", () => model.SortBy(KeyMappingsSort.Command)));
        grid.Children.Add(shortcut);
        grid.Children.Add(scope);
        return grid;
    }

    /// <summary>A flat header button (<c>KeyMappings.Column.&lt;field&gt;</c>) that sorts by its column.</summary>
    private static Button HeaderButton(string text, string field, Action sort)
    {
        var label = Text(text, "bodyStrong");
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var button = new Button
        {
            Content = label,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(button, Id("KeyMappings.Column." + field));
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => sort();
        return button;
    }

    /// <summary>Command (<c>*</c>), Shortcut (220), Scope (120).</summary>
    private static Grid Cells(string command, string shortcut, string scope, string textClass)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,220,120") };
        var texts = new[] { command, shortcut, scope };
        for (var i = 0; i < texts.Length; i++)
        {
            var text = Text(texts[i], textClass);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(text, i);
            grid.Children.Add(text);
        }

        return grid;
    }

    private static TextBlock Text(string text, string textClass)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.NoWrap };
        block.Classes.Add(textClass);
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("FxcTextPrimaryBrush"));
        return block;
    }

    private static Button CreateButton(string field, string label)
    {
        var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Bind(Layoutable.MinWidthProperty, button.GetResourceObservable("FxcMetricButtonMinWidth"));
        button.Bind(Layoutable.HeightProperty, button.GetResourceObservable("FxcMetricButtonHeight"));
        // I-242: the 26 px button holds its 18 px text line — no vertical padding, the label centred.
        button.Bind(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, button.GetResourceObservable("FxcSpace5Horizontal"));
        button.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(button, Id(field));
        AutomationProperties.SetName(button, label.Replace("_", "", StringComparison.Ordinal));
        return button;
    }

    private sealed record Captured(Core.Commands.KeyGesture? Gesture);
}
