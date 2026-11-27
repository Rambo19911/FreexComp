using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Diff;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Text;

namespace FreexComp.Ide.Editor;

/// <summary>
/// One read-only code view of a diff document (IDE-DIFF-021/022/025): an editor over a pane's text with syntax colours
/// by file extension, the diff line numbers (two columns inline), the row backgrounds and intra-line spans, and the find
/// panel. Caret and selection are mirrored into the pane's <see cref="TextDocumentViewModel"/> so Find works here (as in
/// the generated documents, I-058). AutomationId of the editor: <c>Diff.Left</c>, <c>Diff.Right</c> or <c>Diff.Inline</c>.
/// </summary>
public sealed class DiffPaneEditor : Panel
{
    private readonly Control _themeSource;
    private readonly FindPanelView _findPanel;
    private readonly DiffRowRenderer _rows;
    private readonly List<DiffLineNumberMargin> _margins = [];
    private DiffDocumentViewModel? _diff;
    private TextDocumentViewModel? _text;
    private SyntaxColorizer? _colorizer;
    private FindSession? _find;
    private bool _applying;

    public DiffPaneEditor(DiffPane pane, Control themeSource)
    {
        Pane = pane;
        _themeSource = themeSource;
        Editor = new TextEditor { IsReadOnly = true, ShowLineNumbers = false, WordWrap = false };
        var id = "Diff." + pane;
        AutomationProperties.SetAutomationId(Editor, id);
        Bind(Editor, TextEditor.BackgroundProperty, "FxcBgEditorBrush");
        Bind(Editor, TextEditor.ForegroundProperty, "FxcSyntaxTextBrush");
        Bind(Editor, TextEditor.FontFamilyProperty, "FxcFontMono");
        Bind(Editor, TextEditor.FontSizeProperty, "FxcTypeCodeSize");
        Editor.TextArea.Bind(TextArea.SelectionBrushProperty, Editor.TextArea.GetResourceObservable("FxcEditorSelectionBgBrush"));
        _rows = new DiffRowRenderer(themeSource, () => _diff?.Layout.Rows(Pane));
        Editor.TextArea.TextView.BackgroundRenderers.Add(_rows);
        Editor.TextArea.TextView.BackgroundRenderers.Add(new FindMatchRenderer(themeSource, () => _find));
        if (pane == DiffPane.Inline)
        {
            _margins.Add(new DiffLineNumberMargin(Editor.TextArea, row => Row(row)?.LeftLine));
            _margins.Add(new DiffLineNumberMargin(Editor.TextArea, row => Row(row)?.RightLine));
        }
        else
        {
            _margins.Add(new DiffLineNumberMargin(Editor.TextArea, row => pane == DiffPane.Left ? Row(row)?.LeftLine : Row(row)?.RightLine));
        }

        foreach (var margin in _margins)
            Editor.TextArea.LeftMargins.Add(margin);
        var padding = new Border(); // I-257: the text keeps space.4 free at the left
        padding.Bind(WidthProperty, padding.GetResourceObservable("FxcSpace4"));
        Editor.TextArea.LeftMargins.Add(padding);
        Editor.TextArea.Caret.PositionChanged += (_, _) => MirrorSelection();
        Editor.TextArea.SelectionChanged += (_, _) => MirrorSelection();
        Editor.TextArea.GotFocus += (_, _) =>
        {
            if (_diff is not null)
                _diff.FocusedPane = Pane;
            Focused?.Invoke(this, EventArgs.Empty);
        };
        _findPanel = new FindPanelView(() => Editor.TextArea.Focus()) { Margin = new Thickness(0, 0, 8, 0) };
        _findPanel.SetAutomationIds(id);
        Children.Add(Editor);
        Children.Add(_findPanel);

        // UI-ED-003: syntax colours are cached by the colorizer; a theme switch or colour override drops them.
        ActualThemeVariantChanged += (_, _) => ThemeChanged();
        ResourcesChanged += (_, _) => ThemeChanged();
    }

    private void ThemeChanged()
    {
        _colorizer?.ThemeChanged();
        Editor.TextArea.TextView.Redraw();
    }

    public DiffPane Pane { get; }

    public TextEditor Editor { get; }

    /// <summary>The line-number columns (one; inline two: left, right).</summary>
    public IReadOnlyList<DiffLineNumberMargin> LineNumbers => _margins;

    /// <summary>The scroll viewer of the editor's template (null before it is applied).</summary>
    public ScrollViewer? Scroller => Editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    /// <summary>The editor's text area got keyboard focus.</summary>
    public event EventHandler? Focused;

    public void Attach(DiffDocumentViewModel? diff)
    {
        Detach();
        _diff = diff;
        if (diff is null)
            return;
        _text = diff.TextOf(Pane);
        AutomationProperties.SetName(Editor, Pane switch
        {
            DiffPane.Left => diff.LeftLabel,
            DiffPane.Right => diff.RightLabel,
            _ => diff.Title,
        });
        if (_text.Buffer is AvaloniaEditTextBuffer buffer)
        {
            Editor.Document = buffer.Document;
        }
        else
        {
            Editor.Document = new TextDocument(_text.Buffer.Text);
            _text.Buffer.Changed += OnBufferChanged;
        }

        _colorizer = new SyntaxColorizer(_text.Buffer, _text.CreateClassifier(), _themeSource);
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        _text.ViewState.PropertyChanged += OnViewStateChanged;
        _find = _text.Find;
        _find?.Changed += OnFindChanged;
        _findPanel.Session = _find;
        diff.LayoutChanged += OnLayoutChanged;
        OnLayoutChanged(null, EventArgs.Empty);
    }

    public void Detach()
    {
        if (_text is not null)
        {
            _text.ViewState.PropertyChanged -= OnViewStateChanged;
            _text.Buffer.Changed -= OnBufferChanged;
        }

        if (_colorizer is not null)
            Editor.TextArea.TextView.LineTransformers.Remove(_colorizer);
        if (_diff is not null)
            _diff.LayoutChanged -= OnLayoutChanged;
        _find?.Changed -= OnFindChanged;
        _findPanel.Session = null;
        _colorizer = null;
        _find = null;
        _text = null;
        _diff = null;
    }

    /// <summary>Puts the caret at the start of <paramref name="row"/> (0-based) without a selection.</summary>
    public void MoveCaretToRow(int row)
    {
        var document = Editor.Document;
        if (document is null || document.LineCount == 0)
            return;
        var line = Math.Clamp(row + 1, 1, document.LineCount);
        _applying = true;
        try
        {
            Editor.TextArea.ClearSelection();
            Editor.TextArea.Caret.Offset = document.GetLineByNumber(line).Offset;
        }
        finally
        {
            _applying = false;
        }

        MirrorSelection();
    }

    /// <summary>The 0-based row of the caret.</summary>
    public int CaretRow => Editor.TextArea.Caret.Line - 1;

    public void SelectAll()
    {
        var area = Editor.TextArea;
        area.Selection = Selection.Create(area, 0, area.Document.TextLength);
        area.Caret.Offset = area.Document.TextLength;
    }

    private DiffRow? Row(int row) => _diff?.Layout.Rows(Pane) is { } rows && row >= 0 && row < rows.Length ? rows[row] : null;

    private void OnBufferChanged(object? sender, FreexComp.Ide.Core.Text.TextChangedEventArgs e)
    {
        if (_text is not null && Editor.Document.Text != _text.Buffer.Text)
            Editor.Document.Text = _text.Buffer.Text;
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        foreach (var margin in _margins)
            margin.Refresh();
        Editor.TextArea.TextView.Redraw();
    }

    private void OnFindChanged(object? sender, EventArgs e) => Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

    private void OnViewStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applying || _text is null || e.PropertyName is not (nameof(EditorViewState.Caret) or nameof(EditorViewState.SelectionAnchor)))
            return;
        // Core moved the caret or selection (a find): the editor follows.
        var selection = _text.Selection;
        var area = Editor.TextArea;
        var length = area.Document.TextLength;
        _applying = true;
        try
        {
            area.Caret.Offset = Math.Min(selection.Caret, length);
            if (selection.IsEmpty)
                area.ClearSelection();
            else
                area.Selection = Selection.Create(area, Math.Min(selection.Anchor, length), Math.Min(selection.Caret, length));
            area.Caret.BringCaretToView();
        }
        finally
        {
            _applying = false;
        }
    }

    private void MirrorSelection()
    {
        if (_applying || _text is null)
            return;
        var area = Editor.TextArea;
        _applying = true;
        try
        {
            var anchor = area.Selection.IsEmpty ? area.Caret.Offset : area.Selection.SurroundingSegment.Offset == area.Caret.Offset
                ? area.Selection.SurroundingSegment.EndOffset
                : area.Selection.SurroundingSegment.Offset;
            _text.Selection = new TextSelection(anchor, area.Caret.Offset);
        }
        finally
        {
            _applying = false;
        }

        if (_diff is not null && Editor.TextArea.IsKeyboardFocusWithin)
            _diff.CaretRow = CaretRow;
    }

    private static void Bind(Control target, AvaloniaProperty property, string resourceKey) =>
        target.Bind(property, target.GetResourceObservable(resourceKey));
}

/// <summary>
/// IDE-DIFF-022: the background of each diff row across the text area — <c>diff.removed</c>, <c>diff.added</c>,
/// <c>diff.filler</c> — and the intra-line spans in <c>diff.removedInline</c> / <c>diff.addedInline</c>.
/// </summary>
public sealed class DiffRowRenderer(Control themeSource, Func<IReadOnlyList<DiffRow>?> rows) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>The colour token of a row's background; null for a context row.</summary>
    public static string? BackgroundToken(DiffRowKind kind) => kind switch
    {
        DiffRowKind.Removed => "diff.removed",
        DiffRowKind.Added => "diff.added",
        DiffRowKind.Filler => "diff.filler",
        _ => null,
    };

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (rows() is not { } all || !textView.VisualLinesValid)
            return;
        foreach (var line in textView.VisualLines)
        {
            var index = line.FirstDocumentLine.LineNumber - 1;
            if (index < 0 || index >= all.Count || BackgroundToken(all[index].Kind) is not { } token)
                continue;
            var top = line.VisualTop - textView.VerticalOffset;
            if (ThemeBrushes.Find(themeSource, token) is { } background)
                drawingContext.FillRectangle(background, new Rect(0, top, textView.Bounds.Width, line.Height));
            var row = all[index];
            if (row.Changes.IsDefaultOrEmpty
                || ThemeBrushes.Find(themeSource, row.Kind == DiffRowKind.Removed ? "diff.removedInline" : "diff.addedInline") is not { } span)
                continue;
            var offset = line.FirstDocumentLine.Offset;
            foreach (var change in row.Changes)
            {
                var segment = new TextSegment { StartOffset = offset + change.Start, Length = change.Length };
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                    drawingContext.FillRectangle(span, rect);
            }
        }
    }
}

/// <summary>
/// IDE-DIFF-021: a line-number column of a diff view — the 1-based line of its side for each row, nothing on a filler
/// row (or, inline, on a row of the other side); <c>editor.lineNumber</c>, right-aligned like the editor's own.
/// </summary>
public sealed class DiffLineNumberMargin : GutterMargin
{
    private readonly Func<int, int?> _lineOf;
    private int _digits = 3;

    /// <param name="lineOf">The 0-based line of a 0-based row, or null.</param>
    public DiffLineNumberMargin(TextArea area, Func<int, int?> lineOf) : base(area) => _lineOf = lineOf;

    public double CharWidth => Format("0", Brushes.Transparent).WidthIncludingTrailingWhitespace; // measured only (UI-TOK-008)

    public override double MarginWidth => (_digits + 1) * CharWidth;

    /// <summary>The number shown on a 0-based row, or null.</summary>
    public int? NumberAt(int row) => _lineOf(row) is { } line ? line + 1 : null;

    /// <summary>The rows changed: width and numbers follow.</summary>
    public void Refresh()
    {
        var max = 0;
        for (var row = 0; row < (Document?.LineCount ?? 0); row++)
            max = Math.Max(max, NumberAt(row) ?? 0);
        var digits = Math.Max(3, max.ToString(CultureInfo.InvariantCulture).Length);
        if (digits != _digits)
        {
            _digits = digits;
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextElement.FontFamilyProperty || change.Property == TextElement.FontSizeProperty)
            InvalidateMeasure();
    }

    protected override void RenderLine(DrawingContext context, VisualLine line, int lineNumber, double top, double rowHeight, double height)
    {
        if (NumberAt(lineNumber - 1) is not { } number)
            return;
        var text = Format(number.ToString(CultureInfo.InvariantCulture), Brush("editor.lineNumber") ?? Brushes.Transparent);
        var x = MarginWidth - CharWidth - text.WidthIncludingTrailingWhitespace;
        context.DrawText(text, new Point(x, top + (rowHeight - text.Height) / 2));
    }

    private FormattedText Format(string text, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(GetValue(TextElement.FontFamilyProperty)), GetValue(TextElement.FontSizeProperty), brush);
}

/// <summary>
/// IDE-DIFF-023: the 12 px overview strip at the right edge — one mark per change block at its proportional position
/// (removed only <c>status.error</c>, added only <c>status.success</c>, both <c>accent.default</c>), the visible range as a
/// translucent box; a click selects the block of the mark under the pointer, elsewhere it scrolls there.
/// AutomationId <c>Diff.Overview</c>.
/// </summary>
public sealed class DiffOverviewStrip : Control
{
    /// <summary>Strip width (DIP).</summary>
    public const double StripWidth = 12;

    /// <summary>A mark is at least this high, and a click this close to one hits it (DIP).</summary>
    public const double MinMarkHeight = 3;

    private DiffDocumentViewModel? _diff;

    public DiffOverviewStrip()
    {
        Width = StripWidth;
        Cursor = new Cursor(StandardCursorType.Arrow);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>The visible range as fractions of the rows (top, height).</summary>
    public (double Top, double Height) Visible { get; private set; }

    public DiffDocumentViewModel? Diff
    {
        get => _diff;
        set
        {
            _diff = value;
            InvalidateVisual();
        }
    }

    public void SetVisible(double top, double height)
    {
        Visible = (Math.Clamp(top, 0, 1), Math.Clamp(height, 0, 1));
        InvalidateVisual();
    }

    /// <summary>The colour token of a mark kind.</summary>
    public static string MarkToken(DiffMarkKind kind) => kind switch
    {
        DiffMarkKind.Removed => "status.error",
        DiffMarkKind.Added => "status.success",
        _ => "accent.default",
    };

    /// <summary>A mark's rectangle in the strip.</summary>
    public Rect MarkBounds(DiffOverviewMark mark)
    {
        var height = Bounds.Height;
        return new Rect(2, mark.Top * height, Bounds.Width - 4, Math.Max(MinMarkHeight, mark.Height * height));
    }

    public override void Render(DrawingContext context)
    {
        if (ThemeBrushes.Find(this, "bg.panel") is { } background)
            context.FillRectangle(background, new Rect(Bounds.Size));
        if (ThemeBrushes.Find(this, "border.subtle") is { } edge)
            context.FillRectangle(edge, new Rect(0, 0, 1, Bounds.Height));
        if (_diff is null)
            return;
        foreach (var mark in _diff.Marks)
        {
            if (ThemeBrushes.Find(this, MarkToken(mark.Kind)) is { } brush)
                context.FillRectangle(brush, MarkBounds(mark));
        }

        if (Visible.Height > 0 && ThemeBrushes.Find(this, "text.secondary") is { } box)
        {
            var rect = new Rect(0, Visible.Top * Bounds.Height, Bounds.Width, Math.Max(MinMarkHeight, Visible.Height * Bounds.Height));
            using (context.PushOpacity(0.18))
                context.FillRectangle(box, rect);
            if (ThemeBrushes.Find(this, "border.strong") is { } outline)
                context.DrawRectangle(new Pen(outline, 1), rect.Deflate(0.5));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_diff is null || Bounds.Height <= 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        e.Handled = true;
        _diff.ClickOverview(e.GetPosition(this).Y / Bounds.Height, MinMarkHeight / Bounds.Height);
    }
}
