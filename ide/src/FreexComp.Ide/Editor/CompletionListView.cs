using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.CodeModel;

namespace FreexComp.Ide.Editor;

/// <summary>
/// The completion popup (editor.md §11.1, I-115): the rows of the document's <see cref="CompletionSession"/>, drawn over the
/// text below the caret line (UI-ED-111), at most 10 rows of 22 with a scrollbar (UI-ED-112), the selected row in
/// <c>bg.selected</c> (UI-ED-116). It never takes focus: the editor passes the keys to the session (UI-ED-118); a click
/// selects, a double-click accepts. AutomationIds <c>Editor.Completion</c>, <c>Editor.Completion.Item.&lt;text&gt;</c>.
/// </summary>
public sealed class CompletionListView : Border
{
    public const double RowHeight = 22;
    public const int VisibleRows = 10;

    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroll;
    private CompletionSession? _session;

    public CompletionListView()
    {
        Focusable = false;
        IsVisible = false;
        Width = 360;
        MaxHeight = RowHeight * VisibleRows + 2;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Bind(BackgroundProperty, this.GetResourceObservable("FxcBgRaisedBrush"));
        Bind(BorderBrushProperty, this.GetResourceObservable("FxcBorderDefaultBrush"));
        AutomationProperties.SetAutomationId(this, "Editor.Completion");
        _scroll = new ScrollViewer { Content = _rows, Focusable = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Child = _scroll;
    }

    public CompletionSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(value, _session))
                return;
            _session?.Changed -= OnChanged;
            _session = value;
            _session?.Changed += OnChanged;
            Sync();
        }
    }

    /// <summary>The rows' labels (tests).</summary>
    public IReadOnlyList<string> Labels => _session is { IsOpen: true } s ? [.. s.Items.Select(i => i.Label)] : [];

    /// <summary>Asks the owner to place the popup (the caret moved or it opened).</summary>
    public event EventHandler? PlacementRequested;

    private void OnChanged(object? sender, EventArgs e) => Sync();

    private void Sync()
    {
        _rows.Children.Clear();
        if (_session is not { IsOpen: true } session || session.Items.Count == 0)
        {
            IsVisible = false;
            return;
        }

        for (int i = 0; i < session.Items.Count; i++)
        {
            var item = session.Items[i];
            var index = i;
            var name = NameBlock(item.Label, CompletionRules.MatchedIndices(session.TypedText, item.Label));
            var detail = new TextBlock
            {
                Text = item.Detail ?? "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "codeSmall" },
            };
            detail.Bind(TextBlock.ForegroundProperty, detail.GetResourceObservable("FxcTextSecondaryBrush"));
            var row = new Border
            {
                Height = RowHeight,
                Padding = new Thickness(8, 0),
                Child = new DockPanel { Children = { name, detail } },
            };
            DockPanel.SetDock(name, Dock.Left);
            if (i == session.SelectedIndex)
                row.Bind(BackgroundProperty, row.GetResourceObservable("FxcBgSelectedBrush"));
            else
                row.Background = Brushes.Transparent;
            AutomationProperties.SetAutomationId(row, "Editor.Completion.Item." + item.Label);
            row.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                if (e.ClickCount >= 2)
                {
                    session.Select(index);
                    session.Commit(wholeIdentifier: false);
                }
                else
                {
                    session.Select(index);
                }
            };
            _rows.Children.Add(row);
        }

        IsVisible = true;
        // Keep the selected row in view.
        var top = session.SelectedIndex * RowHeight;
        var visible = Math.Min(session.Items.Count, VisibleRows) * RowHeight;
        if (top < _scroll.Offset.Y)
            _scroll.Offset = new Vector(0, top);
        else if (top + RowHeight > _scroll.Offset.Y + visible)
            _scroll.Offset = new Vector(0, top + RowHeight - visible);
        PlacementRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// UI-ED-115: the name with the characters the typed text matched in <c>type.bodyStrong</c> weight and <c>text.link</c>
    /// colour (runs of consecutive matched characters share one <see cref="Run"/>).
    /// </summary>
    private static TextBlock NameBlock(string label, IReadOnlyList<int> matched)
    {
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Classes = { "code" } };
        var inlines = name.Inlines ??= [];
        var set = matched.ToHashSet();
        for (var start = 0; start < label.Length;)
        {
            var highlighted = set.Contains(start);
            var end = start;
            while (end < label.Length && set.Contains(end) == highlighted)
                end++;
            var run = new Run(label[start..end]);
            if (highlighted)
            {
                run.FontWeight = FontWeight.SemiBold;
                run.Bind(TextElement.ForegroundProperty, name.GetResourceObservable("FxcTextLinkBrush"));
                run.Classes.Add(MatchClass);
            }

            inlines.Add(run);
            start = end;
        }

        return name;
    }

    /// <summary>The class of the highlighted runs of a row name (tests).</summary>
    public const string MatchClass = "completionMatch";

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true; // the editor keeps focus and the caret
    }
}
