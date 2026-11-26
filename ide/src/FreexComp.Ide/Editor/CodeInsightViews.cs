using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Editor;

/// <summary>
/// The parameter-info popup (editor.md §11.2, I-116; AutomationId <c>Editor.SignatureHelp</c>): the selected signature in
/// <c>type.codeSmall</c> with the current parameter in <c>type.bodyStrong</c> weight on <c>accent.subtle</c>, and the
/// overload counter <c>▲ i of n ▼</c> in <c>type.caption</c> <c>text.secondary</c>. It floats over the text above the
/// caret line and never takes focus.
/// </summary>
public sealed class SignatureHelpView : Border
{
    private readonly TextBlock _counter = new() { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _signature = new() { Classes = { "codeSmall" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private ParameterInfoSession? _session;

    public SignatureHelpView()
    {
        Focusable = false;
        IsHitTestVisible = false;
        IsVisible = false;
        MaxWidth = 600;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Padding = new Thickness(6, 4);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Bind(BackgroundProperty, this.GetResourceObservable("FxcBgRaisedBrush"));
        Bind(BorderBrushProperty, this.GetResourceObservable("FxcBorderDefaultBrush"));
        _counter.Bind(TextBlock.ForegroundProperty, _counter.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(this, "Editor.SignatureHelp");
        var panel = new DockPanel { Children = { _counter, _signature } };
        DockPanel.SetDock(_counter, Dock.Left);
        Child = panel;
    }

    public ParameterInfoSession? Session
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

    /// <summary>The shown signature (tests).</summary>
    public string Signature { get; private set; } = "";

    /// <summary>The part drawn strong (tests); empty when none.</summary>
    public string CurrentParameter { get; private set; } = "";

    /// <summary>Asks the owner to place the popup.</summary>
    public event EventHandler? PlacementRequested;

    private void OnChanged(object? sender, EventArgs e) => Sync();

    private void Sync()
    {
        _signature.Inlines?.Clear();
        if (_session is not { IsOpen: true, SelectedSignature: { } signature } session)
        {
            Signature = CurrentParameter = "";
            IsVisible = false;
            return;
        }

        var label = signature.Label;
        Signature = label;
        _counter.Text = session.CounterText is { } counter ? $"▲ {counter} ▼" : "";
        _counter.IsVisible = session.CounterText is not null;
        var inlines = _signature.Inlines ??= [];
        if (session.CurrentParameter is { LabelSpanInSignature: { Length: > 0 } span } && span.Start + span.Length <= label.Length)
        {
            CurrentParameter = label.Substring(span.Start, span.Length);
            var strong = new Run(CurrentParameter) { FontWeight = FontWeight.SemiBold }; // type.bodyStrong weight
            strong.Bind(TextElement.BackgroundProperty, this.GetResourceObservable("FxcAccentSubtleBrush"));
            inlines.Add(new Run(label[..span.Start]));
            inlines.Add(strong);
            inlines.Add(new Run(label[(span.Start + span.Length)..]));
        }
        else
        {
            CurrentParameter = "";
            inlines.Add(new Run(label));
        }

        IsVisible = true;
        PlacementRequested?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// The hover tooltip (editor.md §11.3 UI-ED-130/131, I-116; AutomationId <c>Editor.Hover</c>): the error rows of markers
/// at the position first, a 1 px <c>border.subtle</c> separator, then the declaration (<c>type.codeSmall</c>), the origin
/// line (<c>type.caption</c> <c>text.tertiary</c>) and the documentation (<c>type.body</c>). Max width 480; it floats
/// below the hovered word and never takes focus.
/// </summary>
public sealed class HoverView : Border
{
    private readonly StackPanel _content = new() { Spacing = 4 };
    private HoverSession? _session;
    private Core.Debugging.HoverValueNode? _node;

    public HoverView()
    {
        Focusable = false;
        IsVisible = false;
        MaxWidth = 480;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Padding = new Thickness(6, 4);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Bind(BackgroundProperty, this.GetResourceObservable("FxcBgRaisedBrush"));
        Bind(BorderBrushProperty, this.GetResourceObservable("FxcBorderDefaultBrush"));
        AutomationProperties.SetAutomationId(this, "Editor.Hover");
        Child = _content;
        PointerEntered += (_, _) => _session?.KeepOpen(); // UI-ED-130: it stays while the pointer is over it
        PointerExited += (_, _) => _session?.PointerAt(null);
    }

    public HoverSession? Session
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

    public string? Declaration { get; private set; }

    public string? Origin { get; private set; }

    public string? Documentation { get; private set; }

    /// <summary>UI-ED-130 (P6): the first line <c>= value</c> of a variable while the program is paused; null = none.</summary>
    public string? ValueLine { get; private set; }

    public event EventHandler? PlacementRequested;

    private void OnChanged(object? sender, EventArgs e) => Sync();

    private void Sync()
    {
        _content.Children.Clear();
        var info = _session?.Current;
        Declaration = info?.Content?.Declaration;
        Origin = info?.Content?.Origin;
        Documentation = info?.Content?.Documentation;
        ValueLine = info?.Value is { } value ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Core.Resources.Strings.Debug_Hover_Value, value) : null;
        if (info is null)
        {
            IsVisible = false;
            return;
        }

        if (info.Markers.Count > 0)
        {
            _content.Children.Add(CodeEditorView.BuildErrorTooltip(info.Markers));
            if (info.Content is not null || info.Value is not null)
            {
                var separator = new Border { Height = 1 };
                separator.Bind(BackgroundProperty, separator.GetResourceObservable("FxcBorderSubtleBrush"));
                _content.Children.Add(separator);
            }
        }

        if (_node != info.ValueNode)
        {
            _node?.Changed -= OnChanged;
            _node = info.ValueNode;
            _node?.Changed += OnChanged; // DBG-258: expanding a node draws the tooltip again
        }

        if (ValueLine is { } valueLine && info.ValueNode is { } node)
        {
            var line = new TextBlock { Text = valueLine, Classes = { "codeSmall" }, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetAutomationId(line, "Editor.Hover.Value");
            var tree = new StackPanel();
            tree.Children.Add(ValueRow(node, line, "Editor.Hover.Value.Expander"));
            AddChildren(tree, node);
            _content.Children.Add(new ScrollViewer { Content = tree, MaxHeight = ValueTreeMaxHeight, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        }

        if (info.Content is { } content)
        {
            _content.Children.Add(new TextBlock { Text = content.Declaration, Classes = { "codeSmall" }, TextWrapping = TextWrapping.Wrap });
            if (content.Origin is { } origin)
            {
                var line = new TextBlock { Text = origin, Classes = { "caption" } };
                line.Bind(TextBlock.ForegroundProperty, line.GetResourceObservable("FxcTextTertiaryBrush"));
                _content.Children.Add(line);
            }

            if (content.Documentation is { Length: > 0 } documentation)
                _content.Children.Add(new TextBlock { Text = documentation, Classes = { "body" }, TextWrapping = TextWrapping.Wrap });
        }

        IsVisible = true;
        PlacementRequested?.Invoke(this, EventArgs.Empty);
    }

    private const double ValueTreeMaxHeight = 320;
    private const double ValueIndent = 16; // space.6 per level, as the debug tool windows' trees

    /// <summary>
    /// DBG-258: a line of the value tree — the expander (chevron, <c>Editor.Hover.Row.&lt;key&gt;.Expander</c>) of an
    /// expandable node or the space of one, then the text; indented per level.
    /// </summary>
    private static DockPanel ValueRow(Core.Debugging.HoverValueNode node, TextBlock text, string expanderId)
    {
        var row = new DockPanel { Margin = new Thickness(node.Level * ValueIndent, 0, 0, 0) };
        var expander = new Border { Width = 16, MinHeight = 16, Background = Brushes.Transparent, Focusable = false };
        if (node.CanExpand)
        {
            expander.Child = new IdeIcon
            {
                IconName = node.IsExpanded ? "chevron-down" : "chevron-right", Width = 8, Height = 8,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetAutomationId(expander, expanderId);
            // Every press toggles (two quick presses are a double tap, not two taps).
            expander.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(expander).Properties.IsLeftButtonPressed)
                    return;
                e.Handled = true;
                _ = node.ToggleAsync(); // never faults: what cannot be read shows no children
            };
        }

        DockPanel.SetDock(expander, Dock.Left);
        row.Children.Add(expander);
        row.Children.Add(text);
        return row;
    }

    /// <summary>The lines of the expanded children of <paramref name="node"/>, depth first; "Loading…" while they are read.</summary>
    private static void AddChildren(StackPanel tree, Core.Debugging.HoverValueNode node)
    {
        if (!node.IsExpanded)
            return;
        if (node.Children is not { } children)
        {
            var loading = new TextBlock { Text = Core.Resources.Strings.Msg_Debug_Loading, Classes = { "caption" }, Margin = new Thickness((node.Level + 1) * ValueIndent + 16, 0, 0, 0) };
            loading.Bind(TextBlock.ForegroundProperty, loading.GetResourceObservable("FxcTextTertiaryBrush"));
            tree.Children.Add(loading);
            return;
        }

        foreach (var child in children)
        {
            var id = "Editor.Hover.Row." + child.Key;
            var text = new TextBlock
            {
                Text = child.Info is { } info
                    ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Core.Resources.Strings.Debug_Hover_Child, child.Name, info.Value)
                    : child.Name,
                Classes = { "codeSmall" },
                TextWrapping = TextWrapping.Wrap,
            };
            AutomationProperties.SetAutomationId(text, id);
            tree.Children.Add(ValueRow(child, text, id + ".Expander"));
            AddChildren(tree, child);
        }
    }
}

/// <summary>Code-template fields in field mode (IDE-ED-065): every occurrence on an <c>accent.subtle</c> background.</summary>
public sealed class TemplateFieldRenderer(Control themeSource, Func<IReadOnlyList<(int Start, int End)>> fields) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var ranges = fields();
        if (ranges.Count == 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0
            || ThemeBrushes.Find(themeSource, "accent.subtle") is not { } brush)
            return;
        int first = textView.VisualLines[0].FirstDocumentLine.Offset, last = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        foreach (var (start, end) in ranges)
        {
            if (end < first || start > last)
                continue;
            // An empty field is drawn one character wide so that it stays visible.
            var length = Math.Max(1, end - start);
            if (start + length > textView.Document.TextLength)
                continue;
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = start, Length = length }))
                drawingContext.FillRectangle(brush, end > start ? rect : rect.WithWidth(2));
        }
    }
}