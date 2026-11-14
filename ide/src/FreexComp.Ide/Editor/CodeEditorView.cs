using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Input;
using FreexComp.Ide.Text;

namespace FreexComp.Ide.Editor;

/// <summary>
/// The code editor of a <see cref="TextDocumentViewModel"/> (IDE-ARCH-110, UI-ED-001): one AvaloniaEdit
/// <see cref="TextEditor"/> over the document's <see cref="AvaloniaEditTextBuffer"/>.
/// <list type="bullet">
/// <item>Typed text, Enter and paste enter through <see cref="TextDocumentViewModel.TryInsertText"/> (document line
/// ending, read-only refusal); AvaloniaEdit's own undo/redo/paste bindings are removed, so <c>edit.undo</c> /
/// <c>edit.redo</c> reach <see cref="ITextBuffer.Undo"/> / <see cref="ITextBuffer.Redo"/> (IDE-ARCH-030).</item>
/// <item>Caret, selection and scroll offsets are mirrored into <see cref="EditorViewState"/> and restored from it when
/// the view is bound, so a document survives its view (IDE-ARCH-111). The document area shows only the active
/// document, so only its view exists (IDE-ARCH-112).</item>
/// </list>
/// The <c>Editor</c> command scope; the view is the command target and passes every command to the document.
/// </summary>
public sealed class CodeEditorView : Decorator, ICommandTarget, IEditorTestProbe
{
    /// <summary>UI-ED-032: the pointer counts as near the gutter up to this far into the text area.</summary>
    private const double FoldMarkerHoverZone = 24;

    private readonly TextEditor _editor = new();
    private readonly GlyphMargin _glyphMargin;
    private readonly LineNumberMargin _lineNumbers;
    private readonly FoldMargin _foldMargin;
    private readonly ChangeBarMargin _changeBar;
    private readonly CurrentLineRenderer _currentLine;
    private readonly LineStateBackgroundRenderer _lineStates;
    private readonly TextMarkerRenderer _markers;
    private TextDocumentViewModel? _document;
    private CodeTokenIndex? _tokens;
    private FoldingManager? _foldings;
    private FoldBoxGenerator? _foldBoxes;
    private FoldingSection? _tooltipFolding;
    private bool _applying;
    private bool _scrollPending;
    private SyntaxColorizer? _colorizer;
    private readonly FindPanelView _findPanel;
    private FindSession? _find;

    public CodeEditorView()
    {
        _findPanel = new FindPanelView(FocusText);
        CommandRouting.SetScope(this, CommandScope.Editor);
        var area = _editor.TextArea;
        var textView = area.TextView;
        _editor.ShowLineNumbers = false; // our own margins replace AvaloniaEdit's (editor.md §2)
        Bind(_editor, TextEditor.BackgroundProperty, "FxcBgEditorBrush");
        Bind(_editor, TextEditor.ForegroundProperty, "FxcSyntaxTextBrush");
        BindCaret(overwrite: false);
        Bind(area, TextArea.SelectionBrushProperty, "FxcEditorSelectionBgBrush");
        Bind(textView, TextView.NonPrintableCharacterBrushProperty, "FxcEditorWhitespaceBrush"); // UI-ED-058
        textView.Bind(TextView.ColumnRulerPenProperty, // UI-ED-057
            textView.GetResourceObservable("FxcEditorRulerBrush", brush => brush is IBrush b ? new Pen(b, 1) : null));
        RemoveBuiltInBindings(area);

        // editor.md §2: glyph margin, line numbers, fold margin, change bar, then the text padding.
        _glyphMargin = new GlyphMargin(area);
        _lineNumbers = new LineNumberMargin(area);
        _foldMargin = new FoldMargin(area) { ToggleRequested = ToggleFolding };
        _changeBar = new ChangeBarMargin(area);
        foreach (var margin in new Control[] { _glyphMargin, _lineNumbers, _foldMargin, _changeBar, new TextPaddingMargin() })
        {
            area.LeftMargins.Add(margin);
            if (margin is GutterMargin gutter)
                gutter.ContextMenuRequested = OpenGutterMenu;
        }

        // editor.md §6: our current-line highlight (the built-in one is off), line-state backgrounds over it.
        var options = _editor.Options;
        options.HighlightCurrentLine = false;
        options.ShowEndOfLine = false;
        options.ShowSpacesGlyph = "·";
        options.ShowTabsGlyph = "→";
        options.EnableHyperlinks = false;
        options.EnableEmailHyperlinks = false;
        options.InheritWordWrapIndentation = true; // IDE-ED-040: continuation lines keep the line's indentation
        _currentLine = new CurrentLineRenderer(area, this);
        _lineStates = new LineStateBackgroundRenderer(this);
        textView.BackgroundRenderers.Add(_currentLine);
        textView.BackgroundRenderers.Add(_lineStates);
        textView.BackgroundRenderers.Add(new IndentGuideRenderer(this, CurrentGuides));
        textView.BackgroundRenderers.Add(new FindMatchRenderer(this, () => _find));
        textView.BackgroundRenderers.Add(new OccurrenceRenderer(this, () => _document?.WordHighlights.Highlights ?? []));
        textView.BackgroundRenderers.Add(new BracketMatchRenderer(this, CurrentBracketMatch));
        _markers = new TextMarkerRenderer(this); // editor.md §6 row 12
        textView.BackgroundRenderers.Add(_markers);
        ApplyOptions(EditorOptions.Default);

        area.TextEntering += OnTextEntering;
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnPointerWheel, RoutingStrategies.Tunnel);
        area.Caret.PositionChanged += (_, _) =>
        {
            MirrorSelection();
            InvalidateBackground();
        };
        area.SelectionChanged += (_, _) =>
        {
            MirrorSelection();
            InvalidateBackground();
        };
        area.GotFocus += (_, _) => InvalidateBackground();
        area.LostFocus += (_, _) => InvalidateBackground();
        area.AddHandler(PointerMovedEvent, (_, e) => _foldMargin.IsPointerNear = e.GetPosition(textView).X < FoldMarkerHoverZone,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        area.PointerExited += (_, _) => _foldMargin.IsPointerNear = false;
        textView.PointerMoved += (_, e) => ShowFoldTooltip(e.GetPosition(textView));
        textView.PointerExited += (_, _) => ShowFoldTooltip(null);
        textView.ScrollOffsetChanged += (_, _) => MirrorScroll();
        Child = new Panel { Children = { _editor, _findPanel } }; // UI-ED-102: the find panel floats over the text
        _findPanel.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && _findPanel.IsVisible)
                PlaceFindPanel();
        };
        // UI-ED-003: the brushes bound above follow the theme by themselves; syntax colours need a redraw (the
        // margins and renderers look their brushes up on every draw).
        ActualThemeVariantChanged += (_, _) =>
        {
            _colorizer?.ThemeChanged();
            textView.Redraw();
        };
        // IDE-SET-040: a colour override replaces a theme resource.
        ResourcesChanged += (_, _) =>
        {
            _colorizer?.ThemeChanged();
            textView.Redraw();
        };
    }

    /// <summary>The hosted editor (tests and later editor tasks).</summary>
    public TextEditor Editor => _editor;

    /// <summary>Everything, the clipboard commands included (IDE-ED-038), is the document's: the view state holds the selection.</summary>
    public bool CanExecute(string commandId, string? argument) =>
        _document is ICommandTarget document && document.CanExecute(commandId, argument);

    public Task<bool> ExecuteAsync(string commandId, string? argument) =>
        _document is ICommandTarget document ? document.ExecuteAsync(commandId, argument) : Task.FromResult(false);

    public bool IsChecked(string commandId) => _document is ICommandTarget document && document.IsChecked(commandId);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindDocument(DataContext as TextDocumentViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        BindDocument(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (this.IsAttachedToVisualTree())
            BindDocument(DataContext as TextDocumentViewModel);
    }

    private IDisposable? _caretBrush;

    /// <summary>UI-ED-052: overwrite mode draws a block caret (AvaloniaEdit's overstrike caret) at 50 % opacity.</summary>
    private void SetOverwrite(bool overwrite)
    {
        _editor.TextArea.OverstrikeMode = overwrite;
        BindCaret(overwrite);
    }

    /// <summary><c>editor.caret</c>, following the theme; at 50 % opacity for the overwrite block (AvaloniaEdit draws it opaque).</summary>
    private void BindCaret(bool overwrite)
    {
        _caretBrush?.Dispose();
        var area = _editor.TextArea;
        _caretBrush = area.Bind(TextArea.CaretBrushProperty, area.GetResourceObservable("FxcEditorCaretBrush",
            brush => overwrite && brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.5) : brush));
    }

    private static void Bind(Control target, AvaloniaProperty property, string resourceKey) =>
        target.Bind(property, target.GetResourceObservable(resourceKey));

    /// <summary>
    /// AvaloniaEdit's undo/redo would bypass the buffer's save-point tracking (IDE-ARCH-030); its paste would insert
    /// the current line's break and skip the read-only refusal; its cut/copy would use the OS clipboard directly
    /// (IDE-ED-038). <c>edit.undo/redo/cut/copy/paste</c> replace them.
    /// </summary>
    private static void RemoveBuiltInBindings(TextArea area)
    {
        static bool IsReplaced(object? command) =>
            command == ApplicationCommands.Undo || command == ApplicationCommands.Redo || command == ApplicationCommands.Paste
            || command == ApplicationCommands.Cut || command == ApplicationCommands.Copy;

        var handler = area.DefaultInputHandler;
        foreach (var h in new[] { handler, handler.Editing, handler.CaretNavigation })
        {
            foreach (var b in h.CommandBindings.Where(b => IsReplaced(b.Command)).ToList())
                h.CommandBindings.Remove(b);
            foreach (var k in h.KeyBindings.Where(k => IsReplaced(k.Command)).ToList())
                h.KeyBindings.Remove(k);
        }

        foreach (var b in area.CommandBindings.Where(b => IsReplaced(b.Command)).ToList())
            area.CommandBindings.Remove(b);
        foreach (var k in area.KeyBindings.Where(k => IsReplaced(k.Command)).ToList())
            area.KeyBindings.Remove(k);
    }

    private void BindDocument(TextDocumentViewModel? document)
    {
        if (document == _document)
            return;
        if (_document is { } old)
        {
            old.PropertyChanged -= OnDocumentPropertyChanged;
            old.ViewState.PropertyChanged -= OnViewStatePropertyChanged;
            old.Options.PropertyChanged -= OnOptionsChanged;
            old.Decorations.Changed -= OnDecorationsChanged;
            old.Folding.Changed -= OnFoldingChanged;
            old.RevealCaretRequested -= OnRevealCaretRequested;
            old.RevealCaretAtThirdRequested -= OnRevealCaretAtThirdRequested;
        }

        SetColorizer(null);
        SetDecorations(null);
        SetTokens(null);
        SetFind(null);
        UninstallFoldings();

        _document = null; // nothing is mirrored while the editor switches documents
        // Unbound: an empty document, not null (the editor template binds to Document.Text), and no reference to the old one.
        _editor.Document = document is null ? new AvaloniaEdit.Document.TextDocument()
            : (document.Buffer as AvaloniaEditTextBuffer ?? throw new InvalidOperationException("The code editor needs an AvaloniaEdit buffer.")).Document;
        if (document is null)
            return;

        _editor.IsReadOnly = document.IsReadOnly;
        SetOverwrite(document.ViewState.Overwrite);
        SetAutomationIds(document);
        ApplySelection(document);
        SetColorizer(document);
        SetDecorations(document);
        SetTokens(document);
        InstallFoldings();
        ApplyOptions(document.Options);
        _document = document;
        document.PropertyChanged += OnDocumentPropertyChanged;
        document.ViewState.PropertyChanged += OnViewStatePropertyChanged;
        document.Options.PropertyChanged += OnOptionsChanged;
        document.Decorations.Changed += OnDecorationsChanged;
        document.Folding.Changed += OnFoldingChanged;
        document.RevealCaretRequested += OnRevealCaretRequested;
        document.RevealCaretAtThirdRequested += OnRevealCaretAtThirdRequested;
        SetFind(document.Find);
        ApplyZoomAndWrap();
        SyncFoldings();

        // The scroll offsets can be applied once the editor has measured its text.
        _scrollPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_document != document)
                return;
            // TextEditor.ScrollToVerticalOffset does not move the template's ScrollViewer (AvaloniaEdit 12.0.0).
            var state = document.ViewState;
            var y = state.VerticalOffset;
            if (y == 0 && state.TopLine > 1) // a restored session stores the top line, not the offset
                y = _editor.TextArea.TextView.GetVisualTopByDocumentLine(Math.Min(state.TopLine, _editor.Document.LineCount));
            if (_editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroller)
                scroller.Offset = new Vector(state.HorizontalOffset, y);
            _scrollPending = false;
            MirrorScroll();
            TakeFocusFromDocumentArea();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Activating a tab focuses its editor: when the keyboard focus is nowhere, on the window, or on the document area
    /// around this view (a tab click, or the previous document's editor that just left the tree), it moves into the
    /// text. Focus in a tool window or a dialog stays where it is.
    /// </summary>
    private void TakeFocusFromDocumentArea()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return;
        var focused = top.FocusManager?.GetFocusedElement() as Visual;
        if (focused is null || !focused.IsAttachedToVisualTree() || focused == top || this.GetVisualAncestors().Contains(focused))
            _editor.TextArea.Focus();
    }

    /// <summary>
    /// UI-ED-022: the gutter context menu of <paramref name="line"/>; the caret moves to that line first (unless it is
    /// already there), so the line commands act on it.
    /// </summary>
    private void OpenGutterMenu(GutterMargin margin, int line)
    {
        if (_document?.GutterMenu is not { } build)
            return;
        var area = _editor.TextArea;
        if (area.Caret.Line != line)
        {
            area.ClearSelection();
            area.Caret.Offset = _editor.Document.GetLineByNumber(line).Offset;
        }

        var menu = Views.MenuBarBuilder.CreateContextMenu(build(line));
        margin.ContextMenu = menu;
        menu.Open(margin);
    }

    /// <summary>"Focus the editor" (<c>window.focusEditor</c>, pane cycling): the text, not the first focusable element.</summary>
    public bool FocusText() => _document is not null && _editor.TextArea.Focus();

    private void SetAutomationIds(TextDocumentViewModel document)
    {
        AutomationProperties.SetAutomationId(_editor, AutomationIds.Editor(document.Title));
        AutomationProperties.SetAutomationId(_glyphMargin, AutomationIds.Editor(document.Title) + ".Gutter");
        _findPanel.SetAutomationIds(AutomationIds.Editor(document.Title));
    }

    /// <summary>The document's find session drives the panel and the match highlights (UI-ED-055); null detaches it.</summary>
    private void SetFind(FindSession? find)
    {
        _find?.Changed -= OnFindChanged;
        _find = find;
        _find?.Changed += OnFindChanged;
        _findPanel.Session = find;
        InvalidateBackground();
    }

    private void OnFindChanged(object? sender, EventArgs e) => InvalidateBackground();

    /// <summary>UI-ED-100: 8 px left of the vertical scrollbar, flush with the top.</summary>
    private void PlaceFindPanel()
    {
        var bar = _editor.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
            .FirstOrDefault(b => b.Orientation == Avalonia.Layout.Orientation.Vertical && b.IsVisible);
        _findPanel.Margin = new Thickness(0, 0, 8 + (bar?.Bounds.Width ?? 0), 0);
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_document is not { } document)
            return;
        if (e.PropertyName == nameof(TextDocumentViewModel.IsReadOnly))
            _editor.IsReadOnly = document.IsReadOnly;
        else if (e.PropertyName == nameof(TextDocumentViewModel.Title))
            SetAutomationIds(document);
        else if (e.PropertyName == nameof(TextDocumentViewModel.Language))
            SetColorizer(document); // Save As with another extension
        else if (e.PropertyName == nameof(TextDocumentViewModel.LineChanges))
            _changeBar.Source = document.LineChanges;
    }

    /// <summary>Gutter glyphs, change bar and line backgrounds of the document; null detaches them.</summary>
    private void SetDecorations(TextDocumentViewModel? document)
    {
        _glyphMargin.Decorations = document?.Decorations;
        _currentLine.Decorations = document?.Decorations;
        _lineStates.Decorations = document?.Decorations;
        _markers.Decorations = document?.Decorations;
        _changeBar.Source = document?.LineChanges ?? NoLineChanges.Instance;
        InvalidateBackground();
    }

    /// <summary>
    /// UI-ED-030: a <see cref="FoldingManager"/> on the text area, filled from the document's
    /// <see cref="TextDocumentViewModel.Folding"/>; its own folding margin is replaced by <see cref="FoldMargin"/> and its
    /// box by <see cref="FoldBoxGenerator"/> (UI-ED-034).
    /// </summary>
    private void InstallFoldings()
    {
        var area = _editor.TextArea;
        _foldings = FoldingManager.Install(area);
        foreach (var builtIn in area.LeftMargins.OfType<FoldingMargin>().ToList())
            area.LeftMargins.Remove(builtIn);
        var generators = area.TextView.ElementGenerators;
        if (generators.OfType<FoldingElementGenerator>().FirstOrDefault() is { } inner)
        {
            generators.Remove(inner);
            _foldBoxes = new FoldBoxGenerator(inner, Brush) { BoxDoubleClicked = UnfoldAndSelect };
            generators.Add(_foldBoxes);
        }

        _foldMargin.Manager = _foldings;
    }

    private void UninstallFoldings()
    {
        ShowFoldTooltip(null);
        if (_foldBoxes is not null)
        {
            _editor.TextArea.TextView.ElementGenerators.Remove(_foldBoxes);
            _foldBoxes = null;
        }

        if (_foldings is not null)
        {
            FoldingManager.Uninstall(_foldings);
            _foldings = null;
        }

        _foldMargin.Manager = null;
    }

    private void OnFoldingChanged(object? sender, EventArgs e) => SyncFoldings();

    /// <summary>The manager's foldings become the document's regions with their folded state.</summary>
    private void SyncFoldings()
    {
        if (_foldings is null || _document is not { } document)
            return;
        _foldMargin.HoveredFolding = null;
        ShowFoldTooltip(null);
        _foldings.Clear();
        foreach (var region in document.Folding.Regions)
        {
            var section = _foldings.CreateFolding(region.StartOffset, region.EndOffset);
            section.Title = region.Title;
            section.Tag = region;
            section.IsFolded = region.IsFolded;
        }

        _foldMargin.InvalidateVisual();
    }

    /// <summary>A click on a fold marker (UI-ED-012).</summary>
    private void ToggleFolding(FoldingSection section)
    {
        if (_document is { } document && section.Tag is FoldRegion region)
            document.Folding.SetFolded(region, !region.IsFolded);
    }

    /// <summary>Double-click on the <c>…</c> box: unfolds it and selects the text it hid (UI-ED-035, IDE-ED-058).</summary>
    internal void UnfoldAndSelect(FoldingSection section)
    {
        if (_document is not { } document || section.Tag is not FoldRegion region)
            return;
        document.Folding.SetFolded(region, false);
        var area = _editor.TextArea;
        area.Caret.Offset = region.EndOffset;
        area.Selection = Selection.Create(area, region.StartOffset, region.EndOffset);
    }

    /// <summary>UI-ED-035: hovering a <c>…</c> box shows the folded text.</summary>
    private void ShowFoldTooltip(Point? position)
    {
        var textView = _editor.TextArea.TextView;
        FoldingSection? section = null;
        if (position is { } p && _foldBoxes is not null && textView.VisualLinesValid)
        {
            var visual = new Point(p.X + textView.HorizontalOffset, p.Y + textView.VerticalOffset);
            if (textView.GetVisualLineFromVisualTop(visual.Y) is { } line)
                section = FoldBoxGenerator.BoxAt(line, line.GetVisualColumnFloor(visual));
        }

        if (section == _tooltipFolding)
            return;
        _tooltipFolding = section;
        ToolTip.SetTip(textView, section?.Tag is FoldRegion region ? BuildFoldTooltip(region) : null);
    }

    /// <summary>The first 20 hidden lines of a region, a last line <c>…</c> when cut (UI-ED-035).</summary>
    internal IReadOnlyList<string> FoldTooltipLines(FoldRegion region)
    {
        if (_document is not { } document)
            return [];
        var buffer = document.Buffer;
        var lines = new List<string>();
        for (int line = region.StartLine + 1; line <= Math.Min(region.EndLine, buffer.LineCount) && lines.Count < FoldTooltipMaxLines; line++)
        {
            var (offset, length, _) = buffer.GetLine(line);
            lines.Add(buffer.GetText(offset, length));
        }

        if (region.EndLine - region.StartLine > FoldTooltipMaxLines)
            lines.Add(FoldRegion.DefaultTitle);
        return lines;
    }

    private const int FoldTooltipMaxLines = 20;

    /// <summary>The tooltip content: syntax-coloured lines in <c>type.codeSmall</c>, at most 640 wide.</summary>
    private TextBlock BuildFoldTooltip(FoldRegion region)
    {
        var block = new TextBlock { MaxWidth = 640, TextWrapping = TextWrapping.NoWrap };
        Bind(block, TextBlock.FontFamilyProperty, "FxcFontMono");
        Bind(block, TextBlock.FontSizeProperty, "FxcTypeCodeSmallSize");
        if (_document is not { } document)
            return block;
        var buffer = document.Buffer;
        var classifier = _colorizer?.Classifier;
        var lines = FoldTooltipLines(region);
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                block.Inlines!.Add(new LineBreak());
            int lineNumber = region.StartLine + 1 + i;
            bool cut = i == FoldTooltipMaxLines;
            int position = 0, lineOffset = cut ? 0 : buffer.GetLine(lineNumber).Offset;
            if (!cut && classifier is not null)
            {
                foreach (var span in classifier.Classify(buffer, new LineRange(lineNumber, lineNumber)))
                {
                    int start = Math.Max(0, span.Offset - lineOffset), end = Math.Min(lines[i].Length, span.Offset + span.Length - lineOffset);
                    if (start >= end || start < position)
                        continue;
                    if (start > position)
                        block.Inlines!.Add(new Run(lines[i][position..start]));
                    block.Inlines!.Add(new Run(lines[i][start..end]) { Foreground = Brush(span.TokenClass) });
                    position = end;
                }
            }

            if (position < lines[i].Length)
                block.Inlines!.Add(new Run(lines[i][position..]));
        }

        return block;
    }

    /// <summary>The current theme's brush of a colour token.</summary>
    private IBrush? Brush(string token) =>
        this.TryFindResource(Icons.IconCatalog.BrushKey(token), ActualThemeVariant, out var value) ? value as IBrush : null;

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorOptions.TokenStyle))
        {
            _colorizer?.ThemeChanged(); // colors.<theme>.<token>.style changed
            _editor.TextArea.TextView.Redraw();
            return;
        }

        if (e.PropertyName == nameof(EditorOptions.HighlightWordOccurrences))
            _document?.WordHighlights.OptionChanged();
        ApplyOptions((EditorOptions)sender!);
    }

    /// <summary>Margin visibility (editor.md §2), current line (UI-ED-050), ruler (UI-ED-057) and whitespace (UI-ED-058).</summary>
    private void ApplyOptions(EditorOptions options)
    {
        _lineNumbers.IsVisible = options.ShowLineNumbers;
        _foldMargin.IsVisible = options.ShowFoldMargin;
        _changeBar.IsVisible = options.ShowChangeBar;
        _currentLine.IsEnabled = options.HighlightCurrentLine;
        var editorOptions = _editor.Options;
        editorOptions.ShowColumnRulers = options.RightMargin > 0;
        if (options.RightMargin > 0)
            editorOptions.ColumnRulerPositions = [options.RightMargin];
        editorOptions.ShowSpaces = options.ShowWhitespace;
        editorOptions.ShowTabs = options.ShowWhitespace;
        editorOptions.EnableRectangularSelection = options.RectangularSelection; // IDE-ED-026
        editorOptions.IndentationSize = Math.Max(1, options.TabWidth);
        ApplyFontFamily(options.FontFamily);
        ApplyZoomAndWrap();
    }

    private IDisposable? _monoFont;
    private string? _fontFamily;

    /// <summary><c>editor.fontFamily</c>; empty follows the theme's monospace font resource.</summary>
    private void ApplyFontFamily(string family)
    {
        if (family == _fontFamily)
            return;
        _fontFamily = family;
        _monoFont?.Dispose();
        _monoFont = null;
        if (family.Length == 0)
            _monoFont = _editor.Bind(TextEditor.FontFamilyProperty, _editor.GetResourceObservable("FxcFontMono"));
        else
            _editor.FontFamily = new FontFamily(family);
    }

    /// <summary>
    /// IDE-ED-039/040: the document's zoomed font size (else <c>editor.fontSize</c>; the margins inherit it, so the
    /// line numbers' <c>charWidth</c> follows, UI-ED-010) and its word wrap.
    /// </summary>
    private void ApplyZoomAndWrap()
    {
        _editor.FontSize = _document?.EffectiveFontSize ?? (_document?.Options ?? EditorOptions.Default).FontSize;
        _editor.WordWrap = _document?.ViewState.WordWrap ?? false;
        InvalidateBackground();
    }

    /// <summary>UI-ED-061: Ctrl+wheel zooms the document when <c>editor.mouseWheelZoom</c> is on.</summary>
    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_document is not { Options.MouseWheelZoom: true } document || e.Delta.Y == 0
            || !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
            return;
        e.Handled = true;
        var target = (ICommandTarget)document;
        var id = e.Delta.Y > 0 ? TextDocumentViewModel.ZoomInCommandId : TextDocumentViewModel.ZoomOutCommandId;
        if (target.CanExecute(id, null))
            _ = target.ExecuteAsync(id, null);
    }

    private void OnDecorationsChanged(object? sender, EventArgs e) => InvalidateBackground();

    /// <summary>The background decorations (current line, line states, guides) and those under the selection (bracket match).</summary>
    private void InvalidateBackground()
    {
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    /// <summary>The document's tokens (brackets, keywords, identifiers) follow it; a background scan that ends redraws.</summary>
    private void SetTokens(TextDocumentViewModel? document)
    {
        _tokens?.Changed -= OnTokensChanged;
        _document?.WordHighlights.Changed -= OnTokensChanged;
        _tokens = document?.Tokens;
        _tokens?.Changed += OnTokensChanged;
        document?.WordHighlights.Changed += OnTokensChanged;
        document?.WordHighlights.OptionChanged(); // the setting may have changed while the document had no view
    }

    private void OnTokensChanged(object? sender, EventArgs e) => InvalidateBackground();

    /// <summary>UI-ED-053: the bracket or keyword at the caret and its partner, when <c>editor.highlightMatchingBrackets</c> is on.</summary>
    private BracketMatch? CurrentBracketMatch() =>
        _document is { Options.HighlightMatchingBrackets: true } document ? document.Tokens.Current?.MatchAt(_editor.TextArea.Caret.Offset) : null;

    /// <summary>UI-ED-056: guides while <c>editor.showIndentGuides</c> is on and word wrap is off.</summary>
    private IndentGuideRenderer.State? CurrentGuides()
    {
        if (_document is not { Options.ShowIndentGuides: true } document || _editor.WordWrap)
            return null;
        var tabWidth = Math.Max(1, document.Options.TabWidth);
        var active = document.Tokens.Current is { } tokens ? IndentGuides.Active(document.Buffer, tokens, _editor.TextArea.Caret.Offset, tabWidth) : null;
        return new IndentGuideRenderer.State(document.Buffer, tabWidth, active);
    }

    // IEditorTestProbe (editor.md §13.3)

    string? IEditorTestProbe.GlyphAt(int line) => _document?.Decorations.GlyphAt(line)?.ProbeName;

    string? IEditorTestProbe.LineBackgroundAt(int line) =>
        _document?.Decorations.LineBackgroundAt(line) ?? (_currentLine.HighlightedLine == line ? "editor.currentLine.bg" : null);

    /// <summary>The drawn marker pieces (UI-ED-067: the most severe per character); Kind = <c>Error</c>, <c>Warning</c>, <c>Hint</c>.</summary>
    IReadOnlyList<(int Offset, int Length, string Kind)> IEditorTestProbe.Markers =>
        _document is null ? [] : [.. _document.Decorations.DrawnMarkers.Select(m => (m.Offset, m.Length, m.Severity.ToString()))];

    string IEditorTestProbe.ChangeBarAt(int line) => _changeBar.Source.StateOf(line).ToString();

    IReadOnlyList<(int StartLine, int EndLine, bool IsFolded)> IEditorTestProbe.Foldings =>
        _foldings is null ? [] : [.. _foldings.AllFoldings.Select(f =>
            (_editor.Document.GetLineByOffset(f.StartOffset).LineNumber, _editor.Document.GetLineByOffset(f.EndOffset).LineNumber, f.IsFolded))];

    string IEditorTestProbe.TokenClassAt(int offset)
    {
        if (_document is not { } document || _colorizer is null)
            return "syntax.text";
        var line = document.Buffer.GetPosition(offset).Line;
        foreach (var span in _colorizer.Classifier.Classify(document.Buffer, new LineRange(line, line)))
        {
            if (span.Offset <= offset && offset < span.Offset + span.Length)
                return span.TokenClass;
        }

        return "syntax.text";
    }

    IReadOnlyList<(int Offset, int Length)> IEditorTestProbe.BracketHighlights =>
        CurrentBracketMatch() is not { } match ? []
        : match.Partner is { } partner ? [.. new[] { match.Bracket, partner }.OrderBy(t => t.Offset).Select(t => (t.Offset, t.Length))]
        : [(match.Bracket.Offset, match.Bracket.Length)];

    IReadOnlyList<(int Offset, int Length)> IEditorTestProbe.OccurrenceHighlights =>
        _document is null ? [] : [.. _document.WordHighlights.Highlights.Select(t => (t.Offset, t.Length))];

    IReadOnlyList<(int Offset, int Length)> IEditorTestProbe.FindHighlights =>
        _find is { IsOpen: true } find ? [.. find.Matches.Select(m => (m.Offset, m.Length))] : [];

    (int Offset, int Length)? IEditorTestProbe.CurrentFindHighlight =>
        _find is { IsOpen: true, CurrentIndex: > 0 } find ? (find.Matches[find.CurrentIndex - 1].Offset, find.Matches[find.CurrentIndex - 1].Length) : null;

    double IEditorTestProbe.GutterWidth =>
        _editor.TextArea.LeftMargins.OfType<GutterMargin>().Where(m => m.IsVisible).Sum(m => m.MarginWidth);

    /// <summary>One <see cref="SyntaxColorizer"/> over the document's language (IDE-P1-057); null removes it.</summary>
    private void SetColorizer(TextDocumentViewModel? document)
    {
        var transformers = _editor.TextArea.TextView.LineTransformers;
        if (_colorizer is { } old)
        {
            old.Classifier.ClassificationChanged -= OnClassificationChanged;
            transformers.Remove(old);
            _colorizer = null;
        }

        if (document is null)
            return;
        var options = document.Options;
        _colorizer = new SyntaxColorizer(document.Buffer, document.Language.CreateClassifier(), this, options.TokenStyle);
        _colorizer.Classifier.ClassificationChanged += OnClassificationChanged;
        transformers.Add(_colorizer);
    }

    private void OnClassificationChanged(object? sender, EventArgs e) => _editor.TextArea.TextView.Redraw();

    /// <summary>Core changed the caret or selection (revert, save trimming): the editor follows.</summary>
    private void OnViewStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applying || _document is not { } document)
            return;
        if (e.PropertyName is nameof(EditorViewState.Caret) or nameof(EditorViewState.SelectionAnchor) or nameof(EditorViewState.RectangularSelection))
            ApplySelection(document);
        else if (e.PropertyName == nameof(EditorViewState.Overwrite))
            SetOverwrite(document.ViewState.Overwrite);
        else if (e.PropertyName is nameof(EditorViewState.FontSize) or nameof(EditorViewState.WordWrap))
            ApplyZoomAndWrap();
    }

    /// <summary>
    /// Scrolls the template's <see cref="ScrollViewer"/> vertically just enough to show the caret line
    /// (AvaloniaEdit 12's own scrolling does not move it).
    /// </summary>
    private void OnRevealCaretRequested(object? sender, EventArgs e) => ScrollCaretIntoView();

    /// <summary>UI-DLG-120: the caret line at one third of the viewport (as far as the document allows).</summary>
    private void OnRevealCaretAtThirdRequested(object? sender, EventArgs e)
    {
        if (_scrollPending || _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroller)
            return;
        var textView = _editor.TextArea.TextView;
        textView.EnsureVisualLines(); // an unfold just changed the line heights
        var top = textView.GetVisualTopByDocumentLine(_editor.TextArea.Caret.Line);
        var y = Math.Clamp(top - scroller.Viewport.Height / 3, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
        scroller.Offset = new Vector(scroller.Offset.X, y);
    }

    private void ScrollCaretIntoView()
    {
        if (_scrollPending || _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroller)
            return;
        var textView = _editor.TextArea.TextView;
        var top = textView.GetVisualTopByDocumentLine(_editor.TextArea.Caret.Line);
        var bottom = top + textView.DefaultLineHeight;
        var y = scroller.Offset.Y;
        if (top < y)
            y = top;
        else if (bottom > y + scroller.Viewport.Height)
            y = bottom - scroller.Viewport.Height;
        if (FindPanelCovers(top - y))
            y = Math.Max(0, top - scroller.Viewport.Height / 3); // UI-ED-101
        if (y != scroller.Offset.Y)
            scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, y));
    }

    /// <summary>
    /// UI-ED-101: the selection's line would be within the top 80 px of the viewport (<paramref name="lineTop"/> =
    /// its top there) and its x-range intersects the open find panel.
    /// </summary>
    private bool FindPanelCovers(double lineTop)
    {
        if (!_findPanel.IsVisible || lineTop < 0 || lineTop >= 80)
            return false;
        var area = _editor.TextArea;
        var textView = area.TextView;
        if (_findPanel.TranslatePoint(default, textView) is not { } panel)
            return false;
        var selection = area.Selection;
        var start = selection.IsEmpty ? area.Caret.Position : selection.StartPosition;
        var end = selection.IsEmpty ? area.Caret.Position : selection.EndPosition;
        double x1 = textView.GetVisualPosition(start, VisualYPosition.LineTop).X - textView.HorizontalOffset;
        double x2 = textView.GetVisualPosition(end, VisualYPosition.LineTop).X - textView.HorizontalOffset;
        return Math.Max(x1, x2) >= panel.X && Math.Min(x1, x2) <= panel.X + _findPanel.Bounds.Width;
    }

    private void ApplySelection(TextDocumentViewModel document)
    {
        var state = document.ViewState;
        var area = _editor.TextArea;
        _applying = true;
        try
        {
            var caret = OffsetOf(document.Buffer, state.Caret);
            area.Caret.Offset = caret;
            if (state.SelectionAnchor is { } anchor && state.RectangularSelection)
                area.Selection = new RectangleSelection(area, Position(document.Buffer, anchor), Position(document.Buffer, state.Caret));
            else if (state.SelectionAnchor is { } linear && OffsetOf(document.Buffer, linear) != caret)
                area.Selection = Selection.Create(area, OffsetOf(document.Buffer, linear), caret);
            else
                area.ClearSelection();
        }
        finally
        {
            _applying = false;
        }
    }

    private static int OffsetOf(ITextBuffer buffer, TextPosition position) =>
        buffer.GetOffset(Math.Clamp(position.Line, 1, buffer.LineCount), position.Column);

    private static TextViewPosition Position(ITextBuffer buffer, TextPosition position)
    {
        var clamped = buffer.GetPosition(OffsetOf(buffer, position));
        return new TextViewPosition(clamped.Line, clamped.Column);
    }

    private void MirrorSelection()
    {
        if (_applying || _document is not { } document)
            return;
        var area = _editor.TextArea;
        var state = document.ViewState;
        _applying = true;
        try
        {
            state.Caret = new TextPosition(area.Caret.Line, area.Caret.Column);
            state.SelectionAnchor = area.Selection.IsEmpty
                ? null
                : new TextPosition(area.Selection.StartPosition.Line, area.Selection.StartPosition.Column);
            state.RectangularSelection = area.Selection is RectangleSelection && !area.Selection.IsEmpty;
        }
        finally
        {
            _applying = false;
        }
    }

    private void MirrorScroll()
    {
        if (_scrollPending || _document is not { } document)
            return;
        document.ViewState.HorizontalOffset = _editor.HorizontalOffset;
        document.ViewState.VerticalOffset = _editor.VerticalOffset;
        if (_editor.TextArea.TextView.GetDocumentLineByVisualTop(_editor.VerticalOffset) is { } top)
            document.ViewState.TopLine = top.LineNumber;
    }

    /// <summary>
    /// AvaloniaEdit never inserts user text itself: typed text goes to <see cref="TextDocumentViewModel.TypeText"/>
    /// (closer re-indent, IDE-ED-008); a line break that still arrives as text is Enter (IDE-ED-006, IDE-P1-076).
    /// </summary>
    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        e.Handled = true;
        if (_document is not { } document || string.IsNullOrEmpty(e.Text))
            return;
        if (e.Text is "\r\n" or "\n" or "\r")
            document.HandleKey("Enter");
        else
            document.TypeText(e.Text);
    }

    /// <summary>
    /// Keys the keymap left to the editor (the main window's tunnel handler runs first): Tab, Shift+Tab, Backspace, Enter
    /// and Home follow the indentation rules of the document (editor-features.md §2) instead of AvaloniaEdit's, and are
    /// refused on a read-only document with <c>Msg.Editor.ReadOnlyEdit</c>.
    /// </summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_document is not { } document || e.Source is not Visual source || !_editor.TextArea.IsVisualAncestorOf(source) && source != _editor.TextArea)
            return; // keys of the find panel are its own
        var key = e.Key switch
        {
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Enter => "Enter",
            Key.Home => "Home",
            _ => null,
        };
        if (key is null || (e.KeyModifiers != Avalonia.Input.KeyModifiers.None && e.KeyModifiers != Avalonia.Input.KeyModifiers.Shift))
            return;
        e.Handled = document.HandleKey(e.KeyModifiers == Avalonia.Input.KeyModifiers.Shift ? "Shift+" + key : key);
    }
}
