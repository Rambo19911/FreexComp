using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Options page <c>Environment.Trust</c> (dialogs.md §9.10, UI-DLG-297): the hint, the warning of an unreadable list,
/// the list <c>Dialog.Options.Trust.Entries</c> (columns Kind 80 and Path; rows <c>Dialog.Options.Trust.Entry.&lt;i&gt;</c>;
/// multi-selection; Delete = Remove) with its empty text, and the buttons Remove and Remove All. No reset buttons.
/// </summary>
public static class TrustedLocationsPageView
{
    private const double KindWidth = 80;
    private const double ListHeight = 200;

    private static string Id(string field) => OptionsWindow.Id("Trust." + field);

    public static Control Create(TrustedLocationsPageViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var hint = Text(model.Hint, "body", "FxcTextSecondaryBrush");
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 0, 0, 8);
        AutomationProperties.SetAutomationId(hint, Id("Hint"));

        var warning = Text(model.Warning ?? "", "body", "FxcStatusWarningBrush");
        warning.TextWrapping = TextWrapping.Wrap;
        warning.Margin = new Thickness(0, 0, 0, 8);
        warning.IsVisible = model.Warning is not null;
        AutomationProperties.SetAutomationId(warning, Id("Warning"));

        var list = new ListBox { SelectionMode = SelectionMode.Multiple };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); // the path is trimmed, not scrolled
        AutomationProperties.SetAutomationId(list, Id("Entries"));
        AutomationProperties.SetName(list, Strings.Options_Trust_ListLabel.Replace("_", "", StringComparison.Ordinal).TrimEnd(':'));
        var label = new Label { Content = Strings.Options_Trust_ListLabel, Target = list, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) };
        label.Bind(TemplatedControl.ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions($"{KindWidth},*"), Margin = new Thickness(8, 0, 0, 4) };
        var kindHeader = Text(Strings.Options_Trust_Column_Kind, "bodyStrong", "FxcTextPrimaryBrush");
        var pathHeader = Text(Strings.Options_Trust_Column_Path, "bodyStrong", "FxcTextPrimaryBrush");
        Grid.SetColumn(pathHeader, 1);
        header.Children.Add(kindHeader);
        header.Children.Add(pathHeader);

        var empty = Text(model.EmptyText, "body", "FxcTextSecondaryBrush");
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(empty, Id("Empty"));
        var layers = new Grid { Height = ListHeight, Children = { list, empty } };
        var frame = new Border { BorderThickness = new Thickness(1), Child = layers };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("FxcBgEditorBrush"));

        var remove = CreateButton("Remove", Strings.Options_Trust_Remove);
        var removeAll = CreateButton("RemoveAll", Strings.Options_Trust_RemoveAll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { remove, removeAll } };

        IReadOnlyList<TrustEntry> Selected() => [.. list.Selection.SelectedIndexes.Order().Where(i => i < model.Entries.Count).Select(i => model.Entries[i])];

        void SyncState()
        {
            remove.IsEnabled = list.Selection.Count > 0; // both are disabled when they would do nothing (IDE-TRUST-016)
            removeAll.IsEnabled = model.CanRemoveAll;
        }

        void Fill()
        {
            list.Items.Clear();
            for (var i = 0; i < model.Entries.Count; i++)
                list.Items.Add(CreateRow(model.Entries[i], i));
            empty.IsVisible = model.Entries.Count == 0;
            SyncState();
        }

        void RemoveSelected()
        {
            var first = list.SelectedIndex;
            model.Remove(Selected()); // the rows disappear at once (UI-DLG-297)
            if (model.Entries.Count > 0 && first >= 0)
                list.SelectedIndex = Math.Min(first, model.Entries.Count - 1);
        }

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TrustedLocationsPageViewModel.Entries))
                Fill();
        };
        list.SelectionChanged += (_, _) => SyncState();
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None && list.Selection.Count > 0)
            {
                e.Handled = true;
                RemoveSelected();
            }
        };
        remove.Click += (_, _) => RemoveSelected();
        removeAll.Click += (_, _) => model.RemoveAll();
        Fill();

        return new StackPanel { Margin = new Thickness(0, 0, 16, 12), Children = { hint, warning, label, header, frame, buttons } };
    }

    private static ListBoxItem CreateRow(TrustEntry entry, int index)
    {
        var kind = Text(TrustedLocationsPageViewModel.KindText(entry.Kind), "body", "FxcTextPrimaryBrush");
        var path = Text(entry.Path, "body", "FxcTextPrimaryBrush");
        path.TextTrimming = TextTrimming.LeadingCharacterEllipsis; // start-trimmed; the tooltip is the full path
        Grid.SetColumn(path, 1);
        var cells = new Grid { ColumnDefinitions = new ColumnDefinitions($"{KindWidth},*"), Children = { kind, path } };
        var item = new ListBoxItem { Content = cells, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(item, Id($"Entry.{index}"));
        AutomationProperties.SetName(item, TrustedLocationsPageViewModel.KindText(entry.Kind) + ": " + entry.Path);
        ToolTip.SetTip(item, entry.Path);
        return item;
    }

    private static TextBlock Text(string text, string type, string brush)
    {
        var block = new TextBlock { Text = text, Classes = { type }, VerticalAlignment = VerticalAlignment.Center };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable(brush));
        return block;
    }

    private static Button CreateButton(string field, string label)
    {
        var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        button.Bind(Layoutable.MinWidthProperty, button.GetResourceObservable("FxcMetricButtonMinWidth"));
        button.Bind(Layoutable.HeightProperty, button.GetResourceObservable("FxcMetricButtonHeight"));
        button.Bind(TemplatedControl.PaddingProperty, button.GetResourceObservable("FxcSpace5Horizontal"));
        AutomationProperties.SetAutomationId(button, Id(field));
        AutomationProperties.SetName(button, label.Replace("_", "", StringComparison.Ordinal));
        return button;
    }
}
