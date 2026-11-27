using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Diff;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Input;

namespace FreexComp.Ide.Views;

/// <summary>
/// A diff document (diff-viewer.md §2, IDE-DIFF-020…026): the header bar (labels with full-path tooltips, notes, change
/// count, mode toggles, Ignore whitespace, Previous / Next Difference, Copy Block to Current when the right side is a live
/// editor buffer (IDE-HIST-021), Refresh), an optional banner, the two side-by-side views or the inline view with their
/// context menu (<c>Diff.ContextMenu</c>), and the overview strip at the right edge. Both side-by-side views scroll together.
/// <c>Editor</c> command scope with the keymap context <c>Diff</c> (shortcuts.md <c>E Diff</c>); the view is the target of
/// <c>edit.copy</c> and <c>edit.selectAll</c> in the focused view. AutomationId <c>Document.Diff</c>. The Preview dialog
/// of a refactoring embeds the view (UI-DLG-364; the constructor with <c>embedded</c>).
/// </summary>
public sealed class DiffDocumentView : DockPanel, ICommandTarget
{
    private const string CopyCommandId = "edit.copy";
    private const string SelectAllCommandId = "edit.selectAll";

    private readonly DiffPaneEditor _left;
    private readonly DiffPaneEditor _right;
    private readonly DiffPaneEditor _inline;
    private readonly Grid _body;
    private readonly DiffOverviewStrip _overview = new();
    private readonly bool _embedded;
    private DiffDocumentViewModel? _diff;
    private bool _syncing;
    private bool _ready;

    public DiffDocumentView()
        : this(embedded: false)
    {
    }

    /// <param name="embedded">
    /// The view is a part of a dialog (the Preview dialog of a refactoring, UI-DLG-364), not a document tab: binding a
    /// diff does not take the keyboard focus, and the header has two rows — the labels, below them the tools — so that
    /// it fits a narrow pane.
    /// </param>
    public DiffDocumentView(bool embedded)
    {
        _embedded = embedded;
        if (embedded)
            AddHandler(KeyDownEvent, OnEmbeddedKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        CommandRouting.SetScope(this, CommandScope.Editor);
        CommandRouting.SetToolWindow(this, DiffDocumentViewModel.DocumentKind);
        AutomationProperties.SetAutomationId(this, "Document.Diff");
        _left = new DiffPaneEditor(DiffPane.Left, this);
        _right = new DiffPaneEditor(DiffPane.Right, this);
        _inline = new DiffPaneEditor(DiffPane.Inline, this);
        AutomationProperties.SetAutomationId(_overview, "Diff.Overview");
        AutomationProperties.SetName(_overview, Strings.Diff_Overview);

        var header = Header();
        SetDock(header, Dock.Top);
        Children.Add(header);
        var banner = Banner();
        SetDock(banner, Dock.Top);
        Children.Add(banner);
        SetDock(_overview, Dock.Right);
        Children.Add(_overview);

        var separator = new Border { Width = 1 };
        separator.Bind(Border.BackgroundProperty, separator.GetResourceObservable("FxcBorderDefaultBrush"));
        separator.Bind(IsVisibleProperty, new Binding(nameof(DiffDocumentViewModel.IsSideBySide)));
        _body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        Place(_left, 0);
        Place(separator, 1);
        Place(_right, 2);
        Grid.SetColumnSpan(_inline, 3);
        _body.Children.Add(_inline);
        _left.Bind(IsVisibleProperty, new Binding(nameof(DiffDocumentViewModel.IsSideBySide)));
        _right.Bind(IsVisibleProperty, new Binding(nameof(DiffDocumentViewModel.IsSideBySide)));
        _inline.Bind(IsVisibleProperty, new Binding(nameof(DiffDocumentViewModel.IsInline)));
        Children.Add(_body);

        foreach (var pane in new[] { _left, _right, _inline })
        {
            var own = pane;
            own.Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => OnScrolled(own);
            own.Editor.TextArea.ContextRequested += (_, e) => e.Handled = OpenContextMenu(own, e);
        }
    }

    /// <summary>
    /// The views' context menu: from the pointer, a click outside the selection first moves the caret there (UI-MENU-044),
    /// so the block under the pointer is the one Copy Block works on; from the keyboard it opens at the caret.
    /// </summary>
    private bool OpenContextMenu(DiffPaneEditor pane, ContextRequestedEventArgs e)
    {
        if (_diff?.ContextMenu is not { } build)
            return false;
        var area = pane.Editor.TextArea;
        var textView = area.TextView;
        var fromPointer = e.TryGetPosition(textView, out var point);
        if (fromPointer && textView.GetPositionFloor(point + textView.ScrollOffset) is { } position)
        {
            var offset = pane.Editor.Document.GetOffset(position.Location);
            var selection = area.Selection;
            if (selection.IsEmpty || !selection.Segments.Any(s => offset >= s.StartOffset && offset <= s.EndOffset))
            {
                area.ClearSelection();
                area.Caret.Offset = offset;
            }
        }

        var menu = MenuBarBuilder.CreateContextMenu(build());
        AutomationProperties.SetAutomationId(menu, "Diff.ContextMenu");
        menu.PlacementTarget = textView;
        if (fromPointer)
        {
            menu.Placement = PlacementMode.Pointer;
        }
        else
        {
            var caret = area.Caret.CalculateCaretRectangle();
            menu.Placement = PlacementMode.Bottom;
            menu.PlacementRect = new Rect(caret.X - textView.ScrollOffset.X, caret.Y - textView.ScrollOffset.Y, 1, caret.Height);
        }

        area.ContextMenu = menu;
        menu.Open(area);
        return true;
    }

    /// <summary>The view of a pane (tests and the find panel reach the editors through it).</summary>
    public DiffPaneEditor PaneOf(DiffPane pane) => pane switch
    {
        DiffPane.Left => _left,
        DiffPane.Right => _right,
        _ => _inline,
    };

    public DiffOverviewStrip Overview => _overview;

    private DiffPaneEditor Focused => _diff is null ? _left : PaneOf(_diff.FocusedPane);

    public bool CanExecute(string commandId, string? argument) => commandId switch
    {
        CopyCommandId => !Focused.Editor.TextArea.Selection.IsEmpty,
        SelectAllCommandId => true,
        _ => false,
    };

    public async Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        if (!CanExecute(commandId, argument))
            return false;
        if (commandId == SelectAllCommandId)
            Focused.SelectAll();
        else if (_diff is not null && Focused.Editor.TextArea.Selection.SurroundingSegment is { } segment)
            await _diff.CopyToClipboardAsync(Focused.Pane, segment.Offset, segment.EndOffset); // IDE-DIFF-025: no filler rows
        return true;
    }

    /// <summary>
    /// No keymap reaches a dialog (UI-A11Y-014), so the one command of the view that changes what a key does is bound
    /// here: Ctrl+C copies the selection without filler rows (IDE-DIFF-025).
    /// </summary>
    private void OnEmbeddedKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || e.KeyModifiers != Avalonia.Input.KeyModifiers.Control || !Focused.Editor.TextArea.IsKeyboardFocusWithin
            || !CanExecute(CopyCommandId, null))
            return;
        e.Handled = true;
        _ = ExecuteAsync(CopyCommandId, null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null)
            BindDocument();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindDocument();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unbind();
    }

    private void BindDocument()
    {
        Unbind();
        _diff = DataContext as DiffDocumentViewModel;
        if (_diff is null)
            return;
        AutomationProperties.SetName(this, _diff.Title);
        _left.Attach(_diff);
        _right.Attach(_diff);
        _inline.Attach(_diff);
        _overview.Diff = _diff;
        _diff.RevealRequested += OnRevealRequested;
        _diff.PropertyChanged += OnDiffChanged;
        _ready = false;
        var diff = _diff;
        // The scroll offsets can be applied once the editors have measured their text (as the code editor, I-273).
        Dispatcher.UIThread.Post(() =>
        {
            if (_diff != diff)
                return;
            _ready = true;
            UpdateLayout();
            if (diff.TakePendingReveal() is { } pending)
                Reveal(pending);
            else
                ScrollToRow(diff.TopRow, third: false);
            UpdateOverview();
            if (!_embedded)
                Focused.Editor.TextArea.Focus(); // a diff tab that opens; an embedded view leaves the focus where it is
        }, DispatcherPriority.Loaded);
    }

    private void Unbind()
    {
        if (_diff is not null)
        {
            _diff.RevealRequested -= OnRevealRequested;
            _diff.PropertyChanged -= OnDiffChanged;
        }

        _left.Detach();
        _right.Detach();
        _inline.Detach();
        _overview.Diff = null;
        _diff = null;
    }

    private void OnDiffChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiffDocumentViewModel.Marks) or nameof(DiffDocumentViewModel.Layout))
            _overview.InvalidateVisual();
        // The view of the new mode takes the focus — embedded only when a code view had it (a toggle of the header keeps it).
        if (e.PropertyName == nameof(DiffDocumentViewModel.Mode) && _diff is not null && (!_embedded || _body.IsKeyboardFocusWithin))
            Dispatcher.UIThread.Post(() => Focused.Editor.TextArea.Focus(), DispatcherPriority.Loaded);
    }

    private void OnRevealRequested(object? sender, DiffReveal reveal)
    {
        if (!_ready)
            return; // kept by the document until the views have measured it
        if (_diff?.Mode == DiffViewMode.Inline || !_left.IsVisible)
            UpdateLayout(); // a mode switch just made the views visible
        Reveal(reveal);
    }

    private void Reveal(DiffReveal reveal)
    {
        _diff?.RevealDone();
        if (_diff is null)
            return;
        foreach (var pane in Visible())
            pane.MoveCaretToRow(_diff.CaretRow);
        ScrollToRow(reveal.Row, reveal.Third);
    }

    private IEnumerable<DiffPaneEditor> Visible() =>
        _diff?.Mode == DiffViewMode.Inline ? [_inline] : [_left, _right];

    /// <summary>Scrolls the visible views so <paramref name="row"/> is at the top or (IDE-DIFF-023/024) at one third.</summary>
    private void ScrollToRow(int row, bool third)
    {
        var pane = Visible().First();
        if (pane.Scroller is not { } scroller)
            return;
        var textView = pane.Editor.TextArea.TextView;
        textView.EnsureVisualLines();
        var line = Math.Clamp(row + 1, 1, Math.Max(1, pane.Editor.Document.LineCount));
        var top = textView.GetVisualTopByDocumentLine(line);
        var y = third ? top - scroller.Viewport.Height / 3 : top;
        y = Math.Clamp(y, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
        scroller.Offset = new Vector(scroller.Offset.X, y);
        OnScrolled(pane);
    }

    /// <summary>IDE-DIFF-021: the other side-by-side view follows vertically and horizontally; the strip shows the range.</summary>
    private void OnScrolled(DiffPaneEditor source)
    {
        if (_syncing || _diff is null || !source.IsVisible)
            return;
        _syncing = true;
        try
        {
            if (source.Pane != DiffPane.Inline && source.Scroller is { } from
                && (source == _left ? _right : _left).Scroller is { } to && to.Offset != from.Offset)
                to.Offset = from.Offset;
            var textView = source.Editor.TextArea.TextView;
            if (textView.VisualLinesValid && textView.VisualLines.Count > 0)
                _diff.TopRow = textView.GetDocumentLineByVisualTop(textView.VerticalOffset + 1).LineNumber - 1; // +1: offsets are layout-rounded
        }
        finally
        {
            _syncing = false;
        }

        UpdateOverview();
    }

    private void UpdateOverview()
    {
        if (Visible().FirstOrDefault()?.Scroller is { Extent.Height: > 0 } scroller)
            _overview.SetVisible(scroller.Offset.Y / scroller.Extent.Height, scroller.Viewport.Height / scroller.Extent.Height);
    }

    private Control Header()
    {
        var leftLabel = MakeLabel("Diff.LeftLabel", nameof(DiffDocumentViewModel.LeftLabel), nameof(DiffDocumentViewModel.LeftToolTip));
        var arrow = new TextBlock { Text = "↔", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
        var rightLabel = MakeLabel("Diff.RightLabel", nameof(DiffDocumentViewModel.RightLabel), nameof(DiffDocumentViewModel.RightToolTip));
        var notes = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Classes = { "caption" } };
        AutomationProperties.SetAutomationId(notes, "Diff.Notes");
        notes.Bind(TextBlock.TextProperty, new Binding(nameof(DiffDocumentViewModel.NotesText)));
        notes.Bind(IsVisibleProperty, new Binding(nameof(DiffDocumentViewModel.HasNotes)));
        notes.Bind(TextBlock.ForegroundProperty, notes.GetResourceObservable("FxcTextSecondaryBrush"));
        var changes = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        AutomationProperties.SetAutomationId(changes, "Diff.Changes");
        changes.Bind(TextBlock.TextProperty, new Binding(nameof(DiffDocumentViewModel.ChangesText)));
        changes.Bind(TextBlock.ForegroundProperty, changes.GetResourceObservable("FxcTextPrimaryBrush"));

        var sideBySide = MakeToggle("Diff.Mode.SideBySide", Strings.Diff_SideBySide, nameof(DiffDocumentViewModel.IsSideBySide));
        var inline = MakeToggle("Diff.Mode.Inline", Strings.Diff_Inline, nameof(DiffDocumentViewModel.IsInline));
        var whitespace = MakeToggle("Diff.IgnoreWhitespace", Strings.Diff_IgnoreWhitespace, nameof(DiffDocumentViewModel.IgnoreWhitespace));
        whitespace.Margin = new Thickness(8, 0, 0, 0);
        var previous = MakeButton("Diff.PreviousDifference", Strings.Cmd_diff_previousDifference, nameof(DiffDocumentViewModel.PreviousDifferenceCommand));
        previous.Margin = new Thickness(8, 0, 0, 0);
        var next = MakeButton("Diff.NextDifference", Strings.Cmd_diff_nextDifference, nameof(DiffDocumentViewModel.NextDifferenceCommand));
        var copyBlock = MakeButton("Diff.CopyBlockToRight", Strings.Cmd_diff_copyBlockToRight, nameof(DiffDocumentViewModel.CopyBlockCommand));
        copyBlock.Margin = new Thickness(8, 0, 0, 0);
        copyBlock.Bind(IsVisibleProperty, new Binding(nameof(DiffDocumentViewModel.HasLiveRight)));
        var refresh = MakeButton("Diff.Refresh", Strings.Diff_Refresh, nameof(DiffDocumentViewModel.RefreshCommand));
        refresh.Margin = new Thickness(8, 0, 0, 0);
        refresh.Bind(IsVisibleProperty, new Binding(nameof(DiffDocumentViewModel.CanRefresh)));

        var labels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { leftLabel, arrow, rightLabel, notes, changes } };
        // The tools wrap onto a second line when the tab is too narrow for one (none is ever cut off).
        var tools = new WrapPanel
        {
            Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 0, 0), Children = { sideBySide, inline, whitespace, previous, next, copyBlock, refresh },
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        if (_embedded)
        {
            // Labels above the tools; the right label is cut (its tooltip has the path) before the change count is.
            labels.Children.Clear();
            var first = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto") };
            rightLabel.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
            rightLabel.HorizontalAlignment = HorizontalAlignment.Left;
            foreach (var (control, column) in new (Control, int)[] { (leftLabel, 0), (arrow, 1), (rightLabel, 2), (notes, 3), (changes, 4) })
            {
                Grid.SetColumn(control, column);
                first.Children.Add(control);
            }

            tools.HorizontalAlignment = HorizontalAlignment.Left;
            tools.Margin = new Thickness(0, 4, 0, 0);
            row = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
            row.Children.Add(first);
            Grid.SetRow(tools, 1);
            row.Children.Add(tools);
        }
        else
        {
            row.Children.Add(labels);
            Grid.SetColumn(tools, 1);
            row.Children.Add(tools);
        }

        var bar = new Border { Child = row, Padding = new Thickness(8, 4), BorderThickness = new Thickness(0, 0, 0, 1) };
        AutomationProperties.SetAutomationId(bar, "Diff.Header");
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable("FxcBgHeaderBrush"));
        bar.Bind(Border.BorderBrushProperty, bar.GetResourceObservable("FxcBorderSubtleBrush"));
        arrow.Bind(TextBlock.ForegroundProperty, arrow.GetResourceObservable("FxcTextSecondaryBrush"));
        return bar;
    }

    private Control Banner()
    {
        var text = new TextBlock { Classes = { "body" }, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(text, "Diff.Banner");
        text.Bind(TextBlock.TextProperty, new Binding(nameof(DiffDocumentViewModel.BannerText)));
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("FxcTextPrimaryBrush"));
        var banner = new Border { Child = text, Padding = new Thickness(8, 6), BorderThickness = new Thickness(0, 0, 0, 1) };
        banner.Bind(IsVisibleProperty, new Binding(nameof(DiffDocumentViewModel.IsBannerVisible)));
        banner.Bind(Border.BackgroundProperty, banner.GetResourceObservable("FxcStatusInfoBgBrush"));
        banner.Bind(Border.BorderBrushProperty, banner.GetResourceObservable("FxcBorderSubtleBrush"));
        return banner;
    }

    private static TextBlock MakeLabel(string id, string text, string toolTip)
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        AutomationProperties.SetAutomationId(label, id);
        label.Bind(TextBlock.TextProperty, new Binding(text));
        label.Bind(ToolTip.TipProperty, new Binding(toolTip));
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        return label;
    }

    private static ToggleButton MakeToggle(string id, string text, string property)
    {
        var toggle = new ToggleButton { Content = text, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(toggle, id);
        AutomationProperties.SetName(toggle, text);
        toggle.Bind(ToggleButton.IsCheckedProperty, new Binding(property) { Mode = BindingMode.TwoWay });
        return toggle;
    }

    private static Button MakeButton(string id, string text, string command)
    {
        var button = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, text);
        button.Bind(Button.CommandProperty, new Binding(command));
        return button;
    }

    private void Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        _body.Children.Add(control);
    }
}
