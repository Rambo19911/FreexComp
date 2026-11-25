using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of the Search Results tool window (tool-windows.md §8): the header strip <c>Tool.SearchResults.Header</c>, the
/// capped info bar, the multi-selection list of file and match rows (<c>Tool.SearchResults.Row.&lt;key&gt;</c>, match
/// cells <c>.Line</c> and <c>.Text</c> with the match span on <c>editor.findMatch.bg</c>) and the empty states.
/// Double-click is the default action (UI-TW-603); Right/Left expand and collapse file rows.
/// </summary>
public sealed class SearchResultsView : UserControl
{
    private const string Prefix = "Tool.SearchResults";

    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple };
    private readonly TextBlock _header = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _headerStrip = new() { Height = 26 };
    private readonly TextBlock _empty = new() { Text = Strings.Msg_SearchResults_Empty, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
    private readonly TextBlock _none = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
    private readonly InfoBarView _capped = new() { AutomationIdBase = Prefix + ".InfoBar" };
    private SearchResultsViewModel? _model;
    private bool _syncing;

    public SearchResultsView()
    {
        AutomationProperties.SetAutomationId(_list, Prefix + ".List");
        AutomationProperties.SetName(_list, Strings.Cmd_view_searchResults);
        ToolWindowFrame.SetIsPrimary(_list, true);
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(MinHeightProperty, 22.0) },
        });
        _list.ItemTemplate = new FuncDataTemplate<SearchResultRow>((row, _) => row is null ? new Border() : CreateRow(row), supportsRecycling: false);
        _list.SelectionChanged += (_, _) =>
        {
            if (_model is not null && !_syncing)
                _model.SelectedRows = [.. _list.SelectedItems?.OfType<SearchResultRow>() ?? []];
        };
        _list.DoubleTapped += (_, e) =>
        {
            if (_model is null || RowAt(e.Source) is not { } row)
                return;
            e.Handled = true;
            _ = _model.GoToAsync(row);
        };
        _list.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Input.ListBehaviors.FocusOnRightPress(_list); // UI-TW-014 (I-283)
        _list.ContextRequested += (_, e) =>
        {
            if (_model is null)
                return;
            MenuBarBuilder.CreateContextMenu(_model.BuildContextMenu()).Open(_list);
            e.Handled = true;
        };

        AutomationProperties.SetAutomationId(_header, Prefix + ".Header");
        _header.Classes.Add("caption");
        _header.Bind(TextBlock.ForegroundProperty, _header.GetResourceObservable("FxcTextPrimaryBrush"));
        _headerStrip.Child = _header;
        _headerStrip.Bind(Border.BackgroundProperty, _headerStrip.GetResourceObservable("FxcBgHeaderBrush"));
        AutomationProperties.SetAutomationId(_empty, Prefix + ".Empty");
        _empty.Bind(TextBlock.ForegroundProperty, _empty.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(_none, Prefix + ".None");
        _none.Bind(TextBlock.ForegroundProperty, _none.GetResourceObservable("FxcTextSecondaryBrush"));

        var top = new StackPanel { Children = { _headerStrip, _capped } };
        var panel = new DockPanel();
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top);
        panel.Children.Add(top);
        panel.Children.Add(new Panel { Children = { _list, _none } });
        Content = new Panel { Children = { panel, _empty } };
        DataContextChanged += (_, _) => Attach(DataContext as SearchResultsViewModel);
    }

    private void Attach(SearchResultsViewModel? model)
    {
        if (_model is not null)
            _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (model is not null)
        {
            model.PropertyChanged += OnModelChanged;
            _capped.DataContext = new InfoBarViewModel("Capped", InfoBarSeverity.Info, model.CappedText, [], null);
        }

        Update();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchResultsViewModel.CurrentRow) && _model?.CurrentRow is { } current)
        {
            _list.SelectedItem = current;
            _list.ScrollIntoView(current);
        }
        else if (e.PropertyName is nameof(SearchResultsViewModel.Rows) or nameof(SearchResultsViewModel.Header) or nameof(SearchResultsViewModel.IsEmpty)
                 or nameof(SearchResultsViewModel.IsCapped))
        {
            Update();
        }
    }

    private void Update()
    {
        var rows = _model?.Rows ?? [];
        if (!ReferenceEquals(_list.ItemsSource, rows))
        {
            var selected = _model?.SelectedRows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal) ?? [];
            _syncing = true;
            _list.ItemsSource = rows;
            foreach (var row in rows.Where(r => selected.Contains(r.Key)))
                _list.SelectedItems?.Add(row);
            _syncing = false;
            if (_model is not null)
                _model.SelectedRows = [.. _list.SelectedItems?.OfType<SearchResultRow>() ?? []];
        }

        _header.Text = _model?.Header ?? "";
        var empty = _model?.IsEmpty ?? true;
        _empty.IsVisible = empty;
        _headerStrip.IsVisible = !empty;
        _list.IsVisible = !empty;
        _none.IsVisible = _model?.IsNoMatch == true;
        _none.Text = _model?.NoMatchText ?? "";
        _capped.IsVisible = _model?.IsCapped == true;
    }

    private SearchResultRow? RowAt(object? source)
    {
        for (var element = source as StyledElement; element is not null; element = element.Parent)
        {
            if (element is Control { Tag: SearchResultRow row })
                return row;
        }

        return _list.SelectedItem as SearchResultRow;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Right or Key.Left))
            return;
        if (_list.SelectedItem is SearchResultRow { IsFile: true } row && row.IsExpanded != (e.Key == Key.Right))
        {
            var key = row.Key;
            _model.SetExpanded(row, e.Key == Key.Right);
            _list.SelectedItem = _model.Rows.FirstOrDefault(r => r.Key == key);
            e.Handled = true;
        }
    }

    private static Control CreateRow(SearchResultRow row)
    {
        var id = $"{Prefix}.Row.{row.Key}";
        var panel = new DockPanel { MinHeight = 22, Background = Brushes.Transparent, Tag = row };
        AutomationProperties.SetAutomationId(panel, id);
        if (row.IsFile)
        {
            AutomationProperties.SetName(panel, row.FileName);
            panel.Margin = new Thickness(4, 0);
            var chevron = new IdeIcon { IconName = row.IsExpanded ? "chevron-down" : "chevron-right", Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(chevron, Avalonia.Controls.Dock.Left);
            panel.Children.Add(chevron);
            var icon = new IdeIcon { IconName = "file", Width = 16, Height = 16, Margin = new Thickness(0, 0, 4, 0) };
            DockPanel.SetDock(icon, Avalonia.Controls.Dock.Left);
            panel.Children.Add(icon);
            var directory = new TextBlock { Text = row.Directory, TextTrimming = TextTrimming.LeadingCharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 320 };
            directory.Bind(TextBlock.ForegroundProperty, directory.GetResourceObservable("FxcTextTertiaryBrush"));
            DockPanel.SetDock(directory, Avalonia.Controls.Dock.Right);
            panel.Children.Add(directory);
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Inlines!.Add(new Run(row.FileName) { FontWeight = FontWeight.SemiBold });
            name.Inlines.Add(new Run($"  ({row.Count})"));
            name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("FxcTextPrimaryBrush"));
            panel.Children.Add(name);
            return panel;
        }

        AutomationProperties.SetName(panel, row.Text);
        panel.Margin = new Thickness(26, 0, 4, 0);
        var line = new TextBlock { Text = row.LineText, Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        line.Classes.Add("codeSmall");
        line.Bind(TextBlock.ForegroundProperty, line.GetResourceObservable("FxcTextTertiaryBrush"));
        AutomationProperties.SetAutomationId(line, id + ".Line");
        var colon = new TextBlock { Text = ":  ", VerticalAlignment = VerticalAlignment.Center };
        colon.Bind(TextBlock.ForegroundProperty, colon.GetResourceObservable("FxcTextTertiaryBrush"));
        DockPanel.SetDock(line, Avalonia.Controls.Dock.Left);
        DockPanel.SetDock(colon, Avalonia.Controls.Dock.Left);
        panel.Children.Add(line);
        panel.Children.Add(colon);
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Classes.Add("code");
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(text, id + ".Text");
        var start = Math.Clamp(row.HighlightStart, 0, row.Text.Length);
        var length = Math.Clamp(row.HighlightLength, 0, row.Text.Length - start);
        if (start > 0)
            text.Inlines!.Add(new Run(row.Text[..start]));
        var match = new Run(row.Text.Substring(start, length));
        match.Bind(TextElement.BackgroundProperty, text.GetResourceObservable("FxcEditorFindMatchBgBrush"));
        text.Inlines!.Add(match);
        if (start + length < row.Text.Length)
            text.Inlines.Add(new Run(row.Text[(start + length)..]));
        panel.Children.Add(text);
        return panel;
    }
}
