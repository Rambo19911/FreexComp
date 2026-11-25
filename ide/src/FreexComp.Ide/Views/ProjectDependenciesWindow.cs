using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// The dialog <c>ProjectDependencies</c> (UI-DLG-310…315), 520 × 360, resizable down to 400 × 260: the check list
/// <c>Projects</c> — check box, project name, path (elided, the full path as tooltip); rows of dependencies outside the
/// group in <c>text.disabled</c> with their suffix — which takes all extra space and never shrinks below three rows, its
/// placeholder when there is no row, and the inline <c>Error</c> line (at most three lines, a live region). The first
/// focus is on the list; Space toggles the focused row.
/// </summary>
public sealed class ProjectDependenciesWindow : DialogWindow
{
    /// <summary>The minimum client size (dialogs.md, <c>ProjectDependencies</c>).</summary>
    public const double MinimumWidth = 400;

    public const double MinimumHeight = 260;

    /// <summary>UI-DLG-312: the list never shrinks below this number of rows.</summary>
    public const int MinimumRows = 3;

    /// <summary>UI-DLG-312: the error text is cut after this number of lines.</summary>
    public const int ErrorMaxLines = 3;

    private const double RowHeight = 24; // metric.menuItemHeight

    public ProjectDependenciesWindow(ProjectDependenciesViewModel model)
        : this(model, new ListBox { SelectionMode = SelectionMode.Single })
    {
    }

    private ProjectDependenciesWindow(ProjectDependenciesViewModel model, ListBox list)
        : base(model, Body(model, list))
    {
        // The stated minimum is smaller than the opening size (an exception to UI-DLG-005 "the stated size is the minimum").
        MinWidth = MinimumWidth;
        MinHeight = MinimumHeight;
        if (model.StoredSize is { } stored)
        {
            Width = Math.Max(stored.Width, MinWidth);
            Height = Math.Max(stored.Height, MinHeight);
        }

        // UI-DLG-310: the first focus is on the list — on its first row, so that Space and the arrow keys work from there.
        Opened += (_, _) => Dispatcher.UIThread.Post(() => FocusRow(list), DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(ProjectDependenciesViewModel.DialogName, field);

    private static void FocusRow(ListBox list)
    {
        if (list.ItemCount == 0)
        {
            list.Focus(NavigationMethod.Tab);
            return;
        }

        if (list.SelectedIndex < 0)
            list.SelectedIndex = 0;
        (list.SelectedItem as ListBoxItem)?.Focus(NavigationMethod.Tab);
    }

    private static Control Body(ProjectDependenciesViewModel model, ListBox list)
    {
        AutomationProperties.SetAutomationId(list, Id("Projects"));
        AutomationProperties.SetName(list, model.ProjectsLabel.Replace("_", "", StringComparison.Ordinal).TrimEnd(':'));
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        // UI-DLG-312: never below three rows — of the height the rows really have.
        list.MinHeight = MinimumRows * RowHeight;
        list.LayoutUpdated += (_, _) =>
        {
            var row = list.Items.OfType<ListBoxItem>().FirstOrDefault()?.Bounds.Height ?? 0;
            var minimum = MinimumRows * Math.Max(row, RowHeight);
            if (list.MinHeight != minimum)
                list.MinHeight = minimum;
        };
        list.Focusable = true;
        list.GotFocus += (_, e) =>
        {
            if (ReferenceEquals(e.Source, list))
                FocusRow(list); // Alt+P (the label's mnemonic) lands on the list itself
        };

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in model.Rows)
        {
            // Two projects of the same name: the later rows get their position, so that every id is unique.
            var id = "Item." + row.Name;
            if (!ids.Add(id))
                ids.Add(id = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Item.{row.Name}#{list.ItemCount + 1}"));
            list.Items.Add(CreateRow(row, id));
        }

        // Space toggles the focused row (the check boxes themselves are not tab stops).
        list.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None || list.SelectedItem is not ListBoxItem { Tag: DependencyRow row })
                return;
            row.IsChecked = !row.IsChecked;
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var placeholder = new TextBlock
        {
            Text = model.Placeholder,
            IsVisible = model.Placeholder is not null,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16),
            Classes = { "body" },
        };
        placeholder.Bind(TextBlock.ForegroundProperty, placeholder.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(placeholder, Id("Projects.Placeholder"));
        var frame = new Border { BorderThickness = new Thickness(1), Child = new Panel { Children = { list, placeholder } } };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));

        // The inline error (UI-DLG-312): glyph and text; announced when it appears.
        var glyph = new IdeIcon { IconName = "error", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 8, 0) };
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = ErrorMaxLines, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "body" } };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("FxcStatusErrorBrush"));
        AutomationProperties.SetAutomationId(text, Id("Error"));
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Assertive);
        var error = new DockPanel { Margin = new Thickness(0, 8, 0, 0), Children = { glyph, text } }; // space.4
        DockPanel.SetDock(glyph, Avalonia.Controls.Dock.Left);

        void Sync()
        {
            text.Text = model.Error;
            ToolTip.SetTip(text, model.Error); // the full text of a cut message
            error.IsVisible = model.Error is not null;
        }

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectDependenciesViewModel.Error))
                Sync();
        };
        Sync();

        var label = new Label { Content = model.ProjectsLabel, Target = list, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) };
        label.Bind(TemplatedControl.ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        var body = new DockPanel();
        DockPanel.SetDock(label, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(error, Avalonia.Controls.Dock.Bottom);
        body.Children.Add(label);
        body.Children.Add(error);
        body.Children.Add(frame);
        return body;
    }

    private static ListBoxItem CreateRow(DependencyRow row, string id)
    {
        // UI-DLG-311: a dependency outside the group is greyed; its check box stays operable.
        var brush = row.IsOutside ? "FxcTextDisabledBrush" : "FxcTextPrimaryBrush";
        var check = new CheckBox { IsChecked = row.IsChecked, Focusable = false, IsTabStop = false, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0) };
        AutomationProperties.SetAutomationId(check, Id(id + ".Check"));
        AutomationProperties.SetName(check, row.Name);
        check.IsCheckedChanged += (_, _) => row.IsChecked = check.IsChecked == true;
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DependencyRow.IsChecked))
                check.IsChecked = row.IsChecked;
        };

        var name = new TextBlock { Text = row.Name, Classes = { "body" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 16, 0) };
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable(brush));
        AutomationProperties.SetAutomationId(name, Id(id + ".Name"));
        var path = new TextBlock { Text = row.DisplayPath, Classes = { "body" }, TextTrimming = TextTrimming.PrefixCharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        path.Bind(TextBlock.ForegroundProperty, path.GetResourceObservable(row.IsOutside ? "FxcTextDisabledBrush" : "FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(path, Id(id + ".Path"));
        ToolTip.SetTip(path, row.FullPath);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
        columns.ColumnDefinitions[1].MaxWidth = 200;
        columns.Children.Add(check);
        Grid.SetColumn(name, 1);
        columns.Children.Add(name);
        Grid.SetColumn(path, 2);
        columns.Children.Add(path);
        if (row.Suffix is { } suffixText)
        {
            var suffix = new TextBlock { Text = suffixText, Classes = { "body" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            suffix.Bind(TextBlock.ForegroundProperty, suffix.GetResourceObservable("FxcTextDisabledBrush"));
            AutomationProperties.SetAutomationId(suffix, Id(id + ".Suffix"));
            Grid.SetColumn(suffix, 3);
            columns.Children.Add(suffix);
        }

        var item = new ListBoxItem { Content = columns, Tag = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4, 0) };
        item.Bind(MinHeightProperty, item.GetResourceObservable("FxcMetricMenuItemHeight"));
        AutomationProperties.SetAutomationId(item, Id(id));
        AutomationProperties.SetName(item, row.Suffix is null ? row.Name : row.Name + " " + row.Suffix);
        TextSearch.SetText(item, row.Name);
        return item;
    }
}
