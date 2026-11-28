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
/// cells <c>.Line</c> and <c>.Text</c> with the match span on <c>editor.findMatch.bg</c>) and the empty states. A
/// References result set (UI-TW-609…614) adds container rows between them and the kind badge (<c>.Kind</c>) on its
/// reference rows. Double-click is the default action (UI-TW-603); Right/Left expand and collapse file and container rows.
/// </summary>
public sealed class SearchResultsView : UserControl
{
    private const string Prefix = "Tool.SearchResults";

    /// <summary>UI-TW-610: the slot of a kind badge.</summary>
    public const double BadgeSize = 16;

    /// <summary>UI-TW-609: a container row starts one level in (under the file row's icon).</summary>
    private const double ContainerIndent = 18;

    /// <summary>UI-TW-609: a reference row starts two levels in (under the container's name).</summary>
    private const double ReferenceIndent = 32;

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
            model.PropertyChanged += OnModelChanged;

        Update();
        // IDE-FREF-003: a result set shown before this view existed (the window was hidden) has its current row selected.
        if (_model?.CurrentRow is { } current && _list.SelectedItem is null)
        {
            _list.SelectedItem = current;
            _list.ScrollIntoView(current);
        }
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
        // UI-TW-606 / IDE-FREF-006: the text names what was capped — matches or references.
        if (_model is not null && (_capped.DataContext as InfoBarViewModel)?.Text != _model.CappedText)
            _capped.DataContext = new InfoBarViewModel("Capped", InfoBarSeverity.Info, _model.CappedText, [], null);
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
        if (_list.SelectedItem is SearchResultRow { Match: null } row && row.IsExpanded != (e.Key == Key.Right))
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
            var directory = new TextBlock
            {
                Text = row.Directory,
                TextTrimming = TextTrimming.LeadingCharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 320,
                Margin = new Thickness(8, 0, 0, 0), // never touches the name, its count and the library suffix
            };
            directory.Bind(TextBlock.ForegroundProperty, directory.GetResourceObservable("FxcTextTertiaryBrush"));
            DockPanel.SetDock(directory, Avalonia.Controls.Dock.Right);
            panel.Children.Add(directory);
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Inlines!.Add(new Run(row.FileName) { FontWeight = FontWeight.SemiBold });
            name.Inlines.Add(new Run($"  ({row.Count})"));
            if (row.IsLibrary)
            {
                // UI-TW-609: a library source says so after its count.
                var library = new Run("   " + Strings.Msg_References_Library);
                library.Bind(TextElement.ForegroundProperty, name.GetResourceObservable("FxcTextTertiaryBrush"));
                name.Inlines.Add(library);
            }

            name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("FxcTextPrimaryBrush"));
            panel.Children.Add(name);
            return panel;
        }

        if (row.IsContainer)
        {
            // UI-TW-609: one level in — the container's name and the number of its references.
            AutomationProperties.SetName(panel, row.Container);
            panel.Margin = new Thickness(ContainerIndent, 0, 4, 0);
            var chevron = new IdeIcon { IconName = row.IsExpanded ? "chevron-down" : "chevron-right", Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(chevron, Avalonia.Controls.Dock.Left);
            panel.Children.Add(chevron);
            var name = new TextBlock { Text = $"{row.Container}  ({row.Count})", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Classes.Add("body");
            name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("FxcTextPrimaryBrush"));
            panel.Children.Add(name);
            return panel;
        }

        AutomationProperties.SetName(panel, row.Text);
        panel.Margin = new Thickness(row.ReferenceKind is null ? 26 : ReferenceIndent, 0, 4, 0);
        if (row.ReferenceKind is { } kind)
        {
            // UI-TW-610: the kind as a letter badge in a 16 × 16 slot; the letter carries the meaning, the colour supports it.
            var letter = new TextBlock
            {
                Text = SearchResultsViewModel.KindLetter(kind),
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            letter.Classes.Add("caption");
            letter.Bind(TextBlock.ForegroundProperty, letter.GetResourceObservable(IconCatalog.BrushKey(SearchResultsViewModel.KindToken(kind))));
            var badge = new Border { Width = BadgeSize, Height = BadgeSize, Child = letter, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            AutomationProperties.SetAutomationId(badge, id + ".Kind");
            AutomationProperties.SetName(badge, SearchResultsViewModel.KindName(kind));
            ToolTip.SetTip(badge, SearchResultsViewModel.KindName(kind));
            DockPanel.SetDock(badge, Avalonia.Controls.Dock.Left);
            panel.Children.Add(badge);
        }

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
