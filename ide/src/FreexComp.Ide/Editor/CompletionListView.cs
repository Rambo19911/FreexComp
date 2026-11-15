using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.LanguageService;

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
            var icon = new SymbolKindIcon { Kind = item.Kind, Margin = new Thickness(0, 0, 6, 0) }; // UI-ED-113: icon · space.3
            var cells = new DockPanel { Children = { icon, name } };
            DockPanel.SetDock(icon, Dock.Left);
            DockPanel.SetDock(name, Dock.Left);
            if (session.ShownOrigin(item.Origin) is { } unit)
            {
                // UI-ED-113: right-aligned origin in type.caption text.tertiary, only for another unit's symbol.
                var origin = new TextBlock
                {
                    Text = unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
                    Classes = { "caption", OriginClass },
                };
                origin.Bind(TextBlock.ForegroundProperty, origin.GetResourceObservable("FxcTextTertiaryBrush"));
                DockPanel.SetDock(origin, Dock.Right);
                cells.Children.Add(origin);
            }

            cells.Children.Add(detail); // fills the rest, trimmed
            var row = new Border
            {
                Height = RowHeight,
                Padding = new Thickness(8, 0),
                Child = cells,
            };
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

    /// <summary>The class of a row's origin text (tests).</summary>
    public const string OriginClass = "completionOrigin";

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true; // the editor keeps focus and the caret
    }
}

/// <summary>
/// UI-ED-113: the 16 × 16 symbol icon of a completion row — a letter for the item's kind in the syntax colour of that
/// kind (the icon set of toolbars.md §4 has no symbol icons).
/// </summary>
public sealed class SymbolKindIcon : Border
{
    private readonly TextBlock _glyph = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold,
        Classes = { "caption" },
    };

    private CompletionItemKind _kind;

    public SymbolKindIcon()
    {
        Width = Height = 16;
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Center;
        Child = _glyph;
        Kind = CompletionItemKind.Keyword;
    }

    public CompletionItemKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            var (glyph, brush) = Look(value);
            _glyph.Text = glyph;
            _glyph.Bind(TextBlock.ForegroundProperty, _glyph.GetResourceObservable(brush));
        }
    }

    /// <summary>The drawn letter (tests).</summary>
    public string Glyph => _glyph.Text ?? "";

    private static (string Glyph, string Brush) Look(CompletionItemKind kind) => kind switch
    {
        CompletionItemKind.Keyword => ("K", "FxcSyntaxKeywordBrush"),
        CompletionItemKind.Directive => ("D", "FxcSyntaxDirectiveBrush"),
        CompletionItemKind.Unit => ("U", "FxcTextSecondaryBrush"),
        CompletionItemKind.Type => ("T", "FxcSyntaxTypeBrush"),
        CompletionItemKind.Class => ("C", "FxcSyntaxTypeBrush"),
        CompletionItemKind.Interface => ("I", "FxcSyntaxTypeBrush"),
        CompletionItemKind.Enum => ("E", "FxcSyntaxTypeBrush"),
        CompletionItemKind.EnumMember or CompletionItemKind.Constant => ("#", "FxcSyntaxConstantBrush"),
        CompletionItemKind.Method => ("M", "FxcSyntaxRoutineBrush"),
        CompletionItemKind.Routine => ("R", "FxcSyntaxRoutineBrush"),
        CompletionItemKind.Property => ("P", "FxcSyntaxTextBrush"),
        CompletionItemKind.Field => ("F", "FxcSyntaxTextBrush"),
        CompletionItemKind.Parameter => ("A", "FxcSyntaxTextBrush"),
        CompletionItemKind.Variable => ("V", "FxcSyntaxTextBrush"),
        CompletionItemKind.Label => ("L", "FxcSyntaxLabelBrush"),
        CompletionItemKind.Snippet => ("S", "FxcTextSecondaryBrush"),
        _ => ("?", "FxcTextSecondaryBrush"),
    };
}

/// <summary>
/// UI-ED-117: the documentation side panel of the completion popup (AutomationId <c>Editor.Completion.Doc</c>): width 300,
/// as high as the list (min 110), the declaration in <c>type.codeSmall</c> (wrapped), then the doc comment in
/// <c>type.body</c>. Shown while the session has a <see cref="CompletionSession.Description"/>.
/// </summary>
public sealed class CompletionDocView : Border
{
    public const double PanelWidth = 300;
    public const double MinPanelHeight = 110;

    private readonly StackPanel _content = new() { Spacing = 4 };
    private CompletionSession? _session;

    public CompletionDocView()
    {
        Focusable = false;
        IsVisible = false;
        Width = PanelWidth;
        MinHeight = MinPanelHeight;
        VerticalAlignment = VerticalAlignment.Stretch;
        Padding = new Thickness(6, 4);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Bind(BackgroundProperty, this.GetResourceObservable("FxcBgRaisedBrush"));
        Bind(BorderBrushProperty, this.GetResourceObservable("FxcBorderDefaultBrush"));
        AutomationProperties.SetAutomationId(this, "Editor.Completion.Doc");
        Child = new ScrollViewer
        {
            Content = _content, Focusable = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
    }

    public CompletionSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(value, _session))
                return;
            _session?.DescriptionChanged -= OnChanged;
            _session = value;
            _session?.DescriptionChanged += OnChanged;
            Sync();
        }
    }

    public string? Declaration { get; private set; }

    public string? Documentation { get; private set; }

    /// <summary>The declaring unit line; null when none is shown.</summary>
    public string? Origin { get; private set; }

    /// <summary>Shown or hidden (the owner places the popup again).</summary>
    public event EventHandler? VisibilityChanged;

    private void OnChanged(object? sender, EventArgs e) => Sync();

    private void Sync()
    {
        _content.Children.Clear();
        var description = _session is { IsOpen: true } session ? session.Description : null;
        Declaration = description?.Signature;
        Documentation = description?.Documentation;
        Origin = description is null ? null : _session!.ShownOrigin(description.Origin);
        if (description is not null)
        {
            _content.Children.Add(new TextBlock { Text = description.Signature, Classes = { "codeSmall" }, TextWrapping = TextWrapping.Wrap });
            if (Origin is { } origin)
            {
                // UI-ED-117: the declaring unit in type.caption text.tertiary
                var line = new TextBlock { Text = origin, Classes = { "caption" } };
                line.Bind(TextBlock.ForegroundProperty, line.GetResourceObservable("FxcTextTertiaryBrush"));
                _content.Children.Add(line);
            }

            if (description.Documentation is { Length: > 0 } documentation)
                _content.Children.Add(new TextBlock { Text = documentation, Classes = { "body" }, TextWrapping = TextWrapping.Wrap });
        }

        var visible = description is not null;
        if (visible == IsVisible)
            return;
        IsVisible = visible;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
