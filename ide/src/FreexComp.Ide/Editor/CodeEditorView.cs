using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
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
    private IReadOnlyList<TextMarker> _tooltipMarkers = [];
    private bool _applying;
    private bool _scrollPending;
    private SyntaxColorizer? _colorizer;
    private readonly FindPanelView _findPanel;
    private readonly CompletionListView _completion = new(); // I-115
    private readonly CompletionDocView _completionDoc = new(); // I-198
    private readonly StackPanel _completionContent = new() { Orientation = Avalonia.Layout.Orientation.Horizontal };
    private readonly Popup _completionPopup; // UI-ED-110 (I-198): may extend past the editor
    private readonly SignatureHelpView _signatureHelp = new(); // I-116
    private readonly HoverView _hover = new(); // I-116
    private FindSession? _find;
    private Core.CodeModel.DeclarationLinkSession? _link; // I-322: the bound document's Ctrl+hover session
    private Point? _linkPointer; // the pointer over the text view (text-view coordinates), for Ctrl pressed while it rests
    private bool _handCursor;
    private Cursor? _cursorBeforeHand;
    private static Cursor? s_hand;

    public CodeEditorView()
    {
        _findPanel = new FindPanelView(FocusText);
        CommandRouting.SetScope(this, CommandScope.Editor);
        var area = _editor.TextArea;
        var textView = area.TextView;
        _editor.ShowLineNumbers = false; // our own margins replace AvaloniaEdit's (editor.md §2)
        _editor.VerticalScrollBarVisibility = ScrollBarVisibility.Visible; // UI-ED-061: always; horizontal only when needed
        _editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _editor.TemplateApplied += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            foreach (var scroller in _editor.GetVisualDescendants().OfType<ScrollViewer>().Take(1))
                scroller.Classes.Add("editorScroll"); // App.axaml: UI-ED-061 scrollbar styles
        }, DispatcherPriority.Loaded);
        Bind(_editor, TextEditor.BackgroundProperty, "FxcBgEditorBrush");
        Bind(_editor, TextEditor.ForegroundProperty, "FxcSyntaxTextBrush");
        BindCaret(overwrite: false);
        Bind(area, TextArea.SelectionBrushProperty, "FxcEditorSelectionBgBrush");
        Bind(textView, TextView.NonPrintableCharacterBrushProperty, "FxcEditorWhitespaceBrush"); // UI-ED-058
        textView.Bind(TextView.ColumnRulerPenProperty, // UI-ED-057
            textView.GetResourceObservable("FxcEditorRulerBrush", brush => brush is IBrush b ? new Pen(b, 1) : null));
        RemoveBuiltInBindings(area);
        NoLigaturesTransformer.Install(textView); // UI-TOK-021

        // editor.md §2: glyph margin, line numbers, fold margin, change bar, then the text padding.
        _glyphMargin = new GlyphMargin(area) { LineClicked = line => _document?.GlyphMarginClick?.Invoke(line) }; // UI-ED-012
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
        textView.BackgroundRenderers.Add(new TemplateFieldRenderer(this, () => _document?.TemplateFieldRanges ?? [])); // IDE-ED-065
        _markers = new TextMarkerRenderer(this); // editor.md §6 row 12
        textView.BackgroundRenderers.Add(_markers);
        textView.BackgroundRenderers.Add(new DeclarationLinkRenderer(this, () => _link?.Current)); // IDE-ED-149
        ApplyOptions(EditorOptions.Default);

        area.TextEntering += OnTextEntering;
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnPointerWheel, RoutingStrategies.Tunnel);
        area.AddHandler(ContextRequestedEvent, OnTextContextRequested, RoutingStrategies.Tunnel);
        area.AddHandler(PointerPressedEvent, OnTextPointerPressed, RoutingStrategies.Tunnel);
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
        textView.PointerMoved += (_, e) => ShowTextTooltip(e.GetPosition(textView));
        textView.PointerExited += (_, _) => ShowTextTooltip(null);
        // IDE-ED-149: Ctrl+hover follows the pointer and the Ctrl key; leaving the text or scrolling removes it.
        textView.AddHandler(PointerMovedEvent, (_, e) =>
        {
            _linkPointer = e.GetPosition(textView);
            UpdateLink(e.KeyModifiers);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        textView.PointerExited += (_, _) =>
        {
            _linkPointer = null;
            _link?.Clear();
        };
        AddHandler(KeyDownEvent, (_, e) => UpdateLink(IsCtrlKey(e.Key) ? e.KeyModifiers | Avalonia.Input.KeyModifiers.Control : e.KeyModifiers),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, (_, e) => UpdateLink(IsCtrlKey(e.Key) ? e.KeyModifiers & ~Avalonia.Input.KeyModifiers.Control : e.KeyModifiers),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        textView.ScrollOffsetChanged += (_, _) =>
        {
            _link?.Clear(); // IDE-ED-149
            MirrorScroll();
            _document?.Hover?.Close(); // UI-ED-130: scrolling closes the hover
            PlaceSignatureHelp();
        };
        // UI-ED-102: the find panel floats over the text; so do parameter info and hover. The completion list and its
        // documentation panel are a real popup (UI-ED-110) that never takes focus: the editor passes it the keys.
        _completionContent.Children.AddRange([_completion, _completionDoc]);
        _completionPopup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
            IsLightDismissEnabled = false,
            Focusable = false,
            Child = _completionContent,
        };
        Child = new Panel { Children = { _editor, _findPanel, _signatureHelp, _hover, _completionPopup } };
        _completion.PlacementRequested += (_, _) => PlaceCompletion();
        _completionDoc.VisibilityChanged += (_, _) => PlaceCompletion();
        _completion.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && !_completion.IsVisible)
                _completionPopup.IsOpen = false;
        };
        _signatureHelp.PlacementRequested += (_, _) => PlaceSignatureHelp();
        _hover.PlacementRequested += (_, _) => PlaceHover();
        area.LostFocus += (_, _) =>
        {
            _document?.Completion?.Close(); // IDE-ED-086
            _document?.ParameterInfo?.Close(); // IDE-ED-090
            if (_document?.Hover is { Current.AtCaret: true } hover)
                hover.Close();
        };
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

    /// <summary>The completion list (tests).</summary>
    public CompletionListView CompletionList => _completion;

    /// <summary>The completion popup's documentation side panel (tests).</summary>
    public CompletionDocView CompletionDoc => _completionDoc;

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
            old.TemplateFieldsChanged -= OnDecorationsChanged;
        }

        _completion.Session?.Close();
        _completion.Session = null; // IDE-ED-086: switching documents closes the popup
        _completionDoc.Session = null;
        _signatureHelp.Session?.Close();
        _signatureHelp.Session = null;
        _hover.Session?.Close();
        _hover.Session = null;
        SetLink(null);
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
        _completion.Session = document.Completion;
        _completionDoc.Session = document.Completion;
        _signatureHelp.Session = document.ParameterInfo;
        _hover.Session = document.Hover;
        SetLink(document.DeclarationLink);
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
        document.TemplateFieldsChanged += OnDecorationsChanged;
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
            // I-273: the document was opened or activated at a position (navigation, a message, a search result, a
            // debugger location) while the editor could not scroll yet — the request is carried out now, on top of the
            // restored position.
            if (document.TakePendingReveal() is { } third)
            {
                _editor.UpdateLayout(); // the extent of the new document, which the scroll range is clamped to
                if (third)
                    RevealCaretAtThird();
                else
                    ScrollCaretIntoView();
            }

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

    /// <summary>A right-click in the text (UI-MENU-043/044); the gutter margins open their own menu.</summary>
    private void OnTextContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var textView = _editor.TextArea.TextView;
        if (e.Source is Visual source && source.FindAncestorOfType<AbstractMargin>(includeSelf: true) is not null)
            return;
        if (!e.TryGetPosition(textView, out var point))
        {
            // The keyboard (Menu key, Shift+F10): at the caret, unless OnEditorKeyDown opened it already.
            e.Handled = _editor.TextArea.ContextMenu?.IsOpen == true || OpenTextMenu(null);
            return;
        }
        e.Handled = OpenTextMenu(point);
    }

    /// <summary>
    /// The text-area context menu (menus.md §12). From the pointer at <paramref name="point"/> (text-view coordinates): a
    /// click outside the selection first moves the caret there, clearing the selection (UI-MENU-044); from the keyboard
    /// (<paramref name="point"/> null) it opens at the caret.
    /// </summary>
    private bool OpenTextMenu(Point? point)
    {
        if (_document?.TextAreaMenu is not { } build)
            return false;
        var area = _editor.TextArea;
        var textView = area.TextView;
        if (point is { } p && textView.GetPositionFloor(p + textView.ScrollOffset) is { } position)
        {
            var offset = _editor.Document.GetOffset(position.Location);
            var selection = area.Selection;
            var inside = !selection.IsEmpty && selection.Segments.Any(s => offset >= s.StartOffset && offset <= s.EndOffset);
            if (!inside)
            {
                area.ClearSelection();
                area.Caret.Offset = offset;
            }
        }

        var menu = Views.MenuBarBuilder.CreateContextMenu(build());
        var caret = area.Caret.CalculateCaretRectangle();
        menu.PlacementTarget = textView;
        if (point is { } at)
        {
            menu.Placement = PlacementMode.Pointer;
        }
        else
        {
            menu.Placement = PlacementMode.Bottom;
            menu.PlacementRect = new Rect(caret.X - textView.ScrollOffset.X, caret.Y - textView.ScrollOffset.Y, 1, caret.Height);
        }

        area.ContextMenu = menu;
        menu.Open(area);
        return true;
    }

    private IDisposable? _pendingBoxClick;

    /// <summary>The platform double-click time (Windows default).</summary>
    private static readonly TimeSpan DoubleClickTime = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// IDE-ED-058: a click on the <c>…</c> box toggles (unfolds) the region once the double-click time has passed without a
    /// second click — a double-click unfolds and selects instead (<see cref="UnfoldAndSelect"/>).
    /// </summary>
    private void OnFoldBoxClicked(FoldingSection section)
    {
        _pendingBoxClick?.Dispose();
        _pendingBoxClick = null;
        if (_document is not { } document || section.Tag is not FoldRegion region)
            return;
        var delay = DoubleClickTime;
        if (document.Clock is not { } clock)
        {
            document.Folding.SetFolded(region, false);
            return;
        }

        _pendingBoxClick = clock.Schedule(delay, () =>
        {
            _pendingBoxClick = null;
            if (document.Folding.Regions.Contains(region))
                document.Folding.SetFolded(region, false);
        });
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
            _foldBoxes = new FoldBoxGenerator(inner, Brush) { BoxDoubleClicked = UnfoldAndSelect, BoxClicked = OnFoldBoxClicked };
            generators.Add(_foldBoxes);
        }

        _foldMargin.Manager = _foldings;
    }

    private void UninstallFoldings()
    {
        ShowTextTooltip(null);
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
        ShowTextTooltip(null);
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
    private void UnfoldAndSelect(FoldingSection section)
    {
        _pendingBoxClick?.Dispose(); // the double-click replaces the pending single click
        _pendingBoxClick = null;
        if (_document is not { } document || section.Tag is not FoldRegion region)
            return;
        document.Folding.SetFolded(region, false);
        var area = _editor.TextArea;
        area.Caret.Offset = region.EndOffset;
        area.Selection = Selection.Create(area, region.StartOffset, region.EndOffset);
    }

    /// <summary>
    /// UI-ED-035: hovering a <c>…</c> box shows the folded text; elsewhere, UI-ED-131: hovering text covered by markers
    /// shows the error tooltip (the standard tooltip delay).
    /// </summary>
    private void ShowTextTooltip(Point? position)
    {
        var textView = _editor.TextArea.TextView;
        FoldingSection? section = null;
        IReadOnlyList<TextMarker> markers = [];
        int? hovered = null;
        if (position is { } p && textView.VisualLinesValid)
        {
            var visual = new Point(p.X + textView.HorizontalOffset, p.Y + textView.VerticalOffset);
            if (textView.GetVisualLineFromVisualTop(visual.Y) is { } line && visual.Y < line.VisualTop + line.Height)
            {
                int column = line.GetVisualColumnFloor(visual);
                if (_foldBoxes is not null)
                    section = FoldBoxGenerator.BoxAt(line, column);
                if (section is null && _document is { } document)
                {
                    hovered = line.FirstDocumentLine.Offset + line.GetRelativeOffset(column);
                    markers = document.Decorations.MarkersAt(hovered.Value); // UI-ED-131
                }
            }
        }

        _document?.Hover?.PointerAt(hovered); // UI-ED-130
        if (_hover.IsVisible)
            markers = []; // UI-ED-131: the hover popup shows the error rows itself

        if (section == _tooltipFolding && markers.SequenceEqual(_tooltipMarkers))
            return;
        _tooltipFolding = section;
        _tooltipMarkers = markers;
        // UI-ED-132: at the pointer, but a full line (plus a gap) below it, so it never covers the hovered word; a tooltip
        // popup never takes focus.
        ToolTip.SetPlacement(textView, PlacementMode.Pointer);
        ToolTip.SetVerticalOffset(textView, textView.DefaultLineHeight + 4);
        ToolTip.SetTip(textView, section?.Tag is FoldRegion region ? BuildFoldTooltip(region)
            : markers.Count > 0 ? BuildErrorTooltip(markers)
            : null);
    }

    /// <summary>
    /// UI-ED-131 (AutomationId <c>Editor.ErrorTip</c>): one row per marker, most severe first — severity icon (16), code
    /// in <c>type.codeSmall</c> <c>text.secondary</c>, message in <c>type.body</c>.
    /// </summary>
    internal static StackPanel BuildErrorTooltip(IReadOnlyList<TextMarker> markers)
    {
        var rows = new StackPanel { Spacing = 4 };
        AutomationProperties.SetAutomationId(rows, "Editor.ErrorTip");
        foreach (var marker in markers)
        {
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new Icons.IdeIcon
            {
                IconName = marker.Severity switch
                {
                    MarkerSeverity.Error => "error",
                    MarkerSeverity.Warning => "warning",
                    _ => "hint",
                },
                Width = 16,
                Height = 16,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            });
            if (marker.Code is { } code)
            {
                var codeText = new TextBlock { Text = code, Classes = { "codeSmall" } };
                Bind(codeText, TextBlock.ForegroundProperty, "FxcTextSecondaryBrush");
                row.Children.Add(codeText);
            }

            if (marker.Message is { } message)
                row.Children.Add(new TextBlock { Text = message, Classes = { "body" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 });
            rows.Children.Add(row);
        }

        return rows;
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

    /// <summary>UI-ED-035: the tooltip of a fold box is wider than the other tooltips (design-tokens.md §8: 480).</summary>
    internal const double FoldTooltipMaxWidth = 640;

    /// <summary>
    /// The tooltip of a fold box: syntax-coloured lines in <c>type.codeSmall</c>, at most 640 wide. It is the tooltip itself,
    /// not only its content, so that its own maximum width replaces the general one.
    /// </summary>
    internal ToolTip BuildFoldTooltip(FoldRegion region) => new() { Content = BuildFoldTooltipText(region), MaxWidth = FoldTooltipMaxWidth };

    private TextBlock BuildFoldTooltipText(FoldRegion region)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.NoWrap };
        // Resolved now from the editor (the tooltip is rebuilt for every hover): a detached tooltip would not find them.
        if (this.TryFindResource("FxcFontMono", out var mono) && mono is FontFamily family)
            block.FontFamily = family;
        if (this.TryFindResource("FxcTypeCodeSmallSize", out var size) && size is double points)
            block.FontSize = points;
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
        _editor.HorizontalScrollBarVisibility = _editor.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto; // UI-ED-061
        InvalidateBackground();
    }

    /// <summary>UI-ED-061: Ctrl+wheel zooms the document when <c>editor.mouseWheelZoom</c> is on.</summary>
    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y != 0 && e.KeyModifiers is Avalonia.Input.KeyModifiers.None or Avalonia.Input.KeyModifiers.Shift
            && (e.Source as Visual)?.FindAncestorOfType<FindPanelView>(includeSelf: true) is null
            && _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroller)
        {
            // UI-ED-061: 3 lines per notch; Shift+wheel scrolls horizontally (3 lines' height per notch as well).
            var step = 3 * _editor.TextArea.TextView.DefaultLineHeight * e.Delta.Y;
            var max = new Vector(Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width), Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
            scroller.Offset = e.KeyModifiers == Avalonia.Input.KeyModifiers.Shift
                ? new Vector(Math.Clamp(scroller.Offset.X - step, 0, max.X), scroller.Offset.Y)
                : new Vector(scroller.Offset.X, Math.Clamp(scroller.Offset.Y - step, 0, max.Y));
            e.Handled = true;
            return;
        }

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

    /// <summary>UI-ED-053 / IDE-ED-075: the bracket, keyword or directive group at the caret, when <c>editor.highlightMatchingBrackets</c> is on.</summary>
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
        CurrentBracketMatch() is not { } match ? [] : [.. match.Highlighted.Select(t => (t.Offset, t.Length))];

    IReadOnlyList<(int Offset, int Length)> IEditorTestProbe.OccurrenceHighlights =>
        _document is null ? [] : [.. _document.WordHighlights.Highlights.Select(t => (t.Offset, t.Length))];

    IReadOnlyList<(int Offset, int Length)> IEditorTestProbe.FindHighlights =>
        _find is { IsOpen: true } find ? [.. find.Matches.Select(m => (m.Offset, m.Length))] : [];

    (int Offset, int Length)? IEditorTestProbe.CurrentFindHighlight =>
        _find is { IsOpen: true, CurrentIndex: > 0 } find ? (find.Matches[find.CurrentIndex - 1].Offset, find.Matches[find.CurrentIndex - 1].Length) : null;

    (int Offset, int Length)? IEditorTestProbe.DeclarationLink => _link?.Current;

    string IEditorTestProbe.DeclarationLinkToken => DeclarationLinkRenderer.Token;

    bool IEditorTestProbe.HandCursor => _handCursor;

    double IEditorTestProbe.GutterWidth =>
        _editor.TextArea.LeftMargins.OfType<GutterMargin>().Where(m => m.IsVisible).Sum(m => m.MarginWidth);

    /// <summary>
    /// UI-ED-014 per visual row: a glyph or fold-marker shape belongs to the row holding its vertical centre; a change-bar
    /// shape counts for every row it covers completely.
    /// </summary>
    IReadOnlyList<EditorVisualRow> IEditorTestProbe.VisualRows(int line)
    {
        var textView = _editor.TextArea.TextView;
        if (line < 1 || line > textView.Document.LineCount || !textView.VisualLinesValid
            || textView.GetVisualLine(line) is not { } visual || visual.FirstDocumentLine.LineNumber != line)
            return [];
        const double Epsilon = 0.01;
        var glyphs = _glyphMargin.IsVisible ? _glyphMargin.DrawnShapes(visual) : [];
        var markers = _foldMargin.IsVisible ? _foldMargin.DrawnShapes(visual).Where(s => s.Stroked).ToList() : [];
        var bars = _changeBar.IsVisible ? _changeBar.DrawnShapes(visual) : [];
        var state = _changeBar.Source.StateOf(line).ToString();
        var rows = new List<EditorVisualRow>();
        var top = visual.VisualTop - textView.VerticalOffset;
        foreach (var textLine in visual.TextLines)
        {
            double rowTop = top, rowBottom = top + textLine.Height;
            bool Holds((Rect Bounds, bool Stroked) s) => s.Bounds.Center.Y >= rowTop && s.Bounds.Center.Y < rowBottom;
            rows.Add(new EditorVisualRow(rowTop, textLine.Height,
                glyphs.Any(Holds) ? _document?.Decorations.GlyphAt(line)?.ProbeName : null,
                markers.Any(Holds),
                bars.Any(s => s.Bounds.Top <= rowTop + Epsilon && s.Bounds.Bottom >= rowBottom - Epsilon) ? state : "None"));
            top = rowBottom;
        }

        return rows;
    }

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
        _colorizer = new SyntaxColorizer(document.Buffer, document.CreateClassifier(), this, options.TokenStyle); // lexical + P3 layer (I-112)
        _colorizer.Classifier.ClassificationChanged += OnClassificationChanged;
        transformers.Add(_colorizer);
    }

    private void OnClassificationChanged(object? sender, EventArgs e) => _editor.TextArea.TextView.Redraw();

    /// <summary>UI-ED-118: the keys an open completion popup takes.</summary>
    private static string? CompletionKey(KeyEventArgs e) => (e.Key, e.KeyModifiers) switch
    {
        (Key.Up, Avalonia.Input.KeyModifiers.None) => "Up",
        (Key.Down, Avalonia.Input.KeyModifiers.None) => "Down",
        (Key.PageUp, Avalonia.Input.KeyModifiers.None) => "PageUp",
        (Key.PageDown, Avalonia.Input.KeyModifiers.None) => "PageDown",
        (Key.Enter, Avalonia.Input.KeyModifiers.None) => "Enter",
        (Key.Tab, Avalonia.Input.KeyModifiers.None) => "Tab",
        (Key.Enter, Avalonia.Input.KeyModifiers.Control) => "Ctrl+Enter",
        (Key.Escape, Avalonia.Input.KeyModifiers.None) => "Escape",
        _ => null,
    };

    /// <summary>UI-ED-111: left edge at the start of the word being completed, 2 px below the caret line.</summary>
    private void PlaceCompletion()
    {
        if (_document is not { Completion: { IsOpen: true } session } document)
            return;
        var textView = _editor.TextArea.TextView;
        var caret = document.ViewState.Caret;
        var start = new TextViewPosition(caret.Line, Math.Max(1, caret.Column - session.TypedText.Length));
        var bottom = textView.GetVisualPosition(start, VisualYPosition.LineBottom) - textView.ScrollOffset;
        var top = textView.GetVisualPosition(start, VisualYPosition.LineTop) - textView.ScrollOffset;
        if (textView.TranslatePoint(new Point(bottom.X, bottom.Y + 2), this) is not { } below
            || textView.TranslatePoint(new Point(top.X, top.Y - 2), this) is not { } above)
            return;
        // UI-ED-111: below the caret line (gap 2); above it when the popup does not fit below in the editor and does
        // above. Otherwise it stays below and extends past the editor (UI-ED-110: a popup, not an overlay).
        var height = Math.Min(session.Items.Count, CompletionListView.VisibleRows) * CompletionListView.RowHeight + 2;
        var withDoc = _completionDoc.IsVisible;
        var docHeight = Math.Max(CompletionDocView.MinPanelHeight, height);
        _completionDoc.Height = docHeight; // UI-ED-117: as high as the list, min 110
        var total = withDoc ? Math.Max(height, docHeight) : height;
        var flip = below.Y + total > Bounds.Height && above.Y - total >= 0;
        var y = flip ? above.Y - total : below.Y;
        var alignment = flip ? Avalonia.Layout.VerticalAlignment.Bottom : Avalonia.Layout.VerticalAlignment.Top;
        _completion.VerticalAlignment = _completionDoc.VerticalAlignment = alignment;

        // UI-ED-117: the documentation panel right of the list, left of it when the window has no room on the right.
        var x = Math.Max(0, below.X);
        var docLeft = false;
        if (withDoc && TopLevel.GetTopLevel(this) is { } window && this.TranslatePoint(new Point(x, 0), window) is { } inWindow)
            docLeft = inWindow.X + _completion.Width + CompletionDocView.PanelWidth > window.Bounds.Width && inWindow.X - CompletionDocView.PanelWidth >= 0;
        if (docLeft != (_completionContent.Children[0] == _completionDoc))
        {
            _completionContent.Children.Clear();
            _completionContent.Children.AddRange(docLeft ? [_completionDoc, _completion] : [_completion, _completionDoc]);
        }

        _completionPopup.HorizontalOffset = docLeft ? x - CompletionDocView.PanelWidth : x;
        _completionPopup.VerticalOffset = y;
        _completionPopup.IsOpen = true;
    }

    /// <summary>
    /// IDE-ED-101 (I-196): Ctrl+click on the text puts the caret at the clicked character and runs the global
    /// <c>search.goToDeclaration</c> there (instead of AvaloniaEdit's word selection).
    /// </summary>
    private void OnTextPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var textView = _editor.TextArea.TextView;
        if (_document is not { ExecuteCommand: { } execute } || e.KeyModifiers != Avalonia.Input.KeyModifiers.Control
            || !e.GetCurrentPoint(textView).Properties.IsLeftButtonPressed)
            return;
        var point = e.GetPosition(textView);
        if (point.X < 0 || point.Y < 0 || textView.GetPosition(point + textView.ScrollOffset) is not { } position)
            return;
        e.Handled = true;
        var area = _editor.TextArea;
        area.ClearSelection();
        area.Caret.Position = position;
        area.Focus();
        _ = execute(Core.CodeModel.GoToDeclarationService.CommandId);
    }

    private static bool IsCtrlKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl;

    private void SetLink(Core.CodeModel.DeclarationLinkSession? link)
    {
        if (_link is { } old)
        {
            old.Changed -= OnLinkChanged;
            old.Clear();
        }

        _link = link;
        _link?.Changed += OnLinkChanged;
        OnLinkChanged(null, EventArgs.Empty);
    }

    /// <summary>
    /// IDE-ED-149: with Ctrl alone held and the pointer over text the session asks for the identifier there; any other
    /// modifier state, or the pointer away from the text, removes the underline.
    /// </summary>
    private void UpdateLink(Avalonia.Input.KeyModifiers modifiers)
    {
        if (_link is not { } link)
            return;
        var textView = _editor.TextArea.TextView;
        if (modifiers != Avalonia.Input.KeyModifiers.Control || _linkPointer is not { } p || p.X < 0 || p.Y < 0 || !textView.VisualLinesValid
            || textView.GetPosition(p + textView.ScrollOffset) is not { } position)
        {
            link.Clear();
            return;
        }

        link.PointerAt(_editor.Document.GetOffset(position.Location));
    }

    /// <summary>The underline is redrawn; over a link the text view shows the hand cursor (UI-TOK-053), else its own.</summary>
    private void OnLinkChanged(object? sender, EventArgs e)
    {
        var textView = _editor.TextArea.TextView;
        textView.InvalidateLayer(KnownLayer.Selection);
        var hand = _link?.Current is not null;
        if (hand == _handCursor)
            return;
        _handCursor = hand;
        if (hand)
        {
            _cursorBeforeHand = textView.Cursor;
            textView.Cursor = s_hand ??= new Cursor(StandardCursorType.Hand);
        }
        else
        {
            textView.Cursor = _cursorBeforeHand;
        }
    }

    /// <summary>UI-ED-121: above the caret line, left edge at the opening parenthesis; below the line when there is no room above.</summary>
    private void PlaceSignatureHelp()
    {
        if (_document is not { ParameterInfo: { IsOpen: true, Help: { } help } } document || !_signatureHelp.IsVisible)
            return;
        var textView = _editor.TextArea.TextView;
        var caret = document.ViewState.Caret;
        var open = document.Buffer.GetPosition(Math.Max(0, help.ApplicableSpan.Start - 1));
        var anchor = new TextViewPosition(caret.Line, open.Line == caret.Line ? open.Column : 1);
        var top = textView.GetVisualPosition(anchor, VisualYPosition.LineTop) - textView.ScrollOffset;
        var bottom = textView.GetVisualPosition(anchor, VisualYPosition.LineBottom) - textView.ScrollOffset;
        _signatureHelp.Measure(Size.Infinity);
        var height = _signatureHelp.DesiredSize.Height - _signatureHelp.Margin.Top; // DesiredSize includes the last placement
        var y = top.Y - height - 2 >= 0 && !_completion.IsVisible ? top.Y - height - 2 : bottom.Y + 2;
        if (textView.TranslatePoint(new Point(top.X, y), this) is { } at)
            _signatureHelp.Margin = new Thickness(Math.Max(0, at.X), Math.Max(0, at.Y), 0, 0);
    }

    /// <summary>UI-ED-130: below the hovered word (above it when there is no room below).</summary>
    private void PlaceHover()
    {
        if (_document is not { Hover.Current: { } info } document || !_hover.IsVisible)
            return;
        var textView = _editor.TextArea.TextView;
        var start = document.Buffer.GetPosition(Math.Min(info.Offset, document.Buffer.Length));
        var position = new TextViewPosition(start.Line, start.Column);
        var top = textView.GetVisualPosition(position, VisualYPosition.LineTop) - textView.ScrollOffset;
        var bottom = textView.GetVisualPosition(position, VisualYPosition.LineBottom) - textView.ScrollOffset;
        _hover.Measure(Size.Infinity);
        var height = _hover.DesiredSize.Height - _hover.Margin.Top; // DesiredSize includes the last placement
        var y = bottom.Y + 2 + height <= textView.Bounds.Height || top.Y - height - 2 < 0 ? bottom.Y + 2 : top.Y - height - 2;
        if (textView.TranslatePoint(new Point(bottom.X, y), this) is { } at)
            _hover.Margin = new Thickness(Math.Max(0, at.X), Math.Max(0, at.Y), 0, 0);
    }

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
    private void OnRevealCaretRequested(object? sender, EventArgs e)
    {
        if (_scrollPending)
            return; // kept by the document until the editor has measured it (BindDocument)
        _document?.TakePendingReveal();
        ScrollCaretIntoView();
    }

    /// <summary>UI-DLG-120: the caret line at one third of the viewport (as far as the document allows).</summary>
    private void OnRevealCaretAtThirdRequested(object? sender, EventArgs e)
    {
        if (_scrollPending)
            return; // kept by the document until the editor has measured it (BindDocument)
        _document?.TakePendingReveal();
        RevealCaretAtThird();
    }

    private void RevealCaretAtThird()
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
        if (e.Key == Key.Apps && e.KeyModifiers == Avalonia.Input.KeyModifiers.None
            || e.Key == Key.F10 && e.KeyModifiers == Avalonia.Input.KeyModifiers.Shift)
        {
            e.Handled = OpenTextMenu(null); // UI-MENU-043: at the caret
            return;
        }

        document.Hover?.Close(); // UI-ED-130: any key press closes the hover
        if (document.Completion is { IsOpen: true } completion && CompletionKey(e) is { } gesture)
        {
            e.Handled = completion.HandleKey(gesture); // UI-ED-118: the popup never has focus; its keys come from here
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == Avalonia.Input.KeyModifiers.None && document.InTemplateFieldMode && document.ParameterInfo is not { IsOpen: true })
        {
            e.Handled = document.HandleKey("Escape"); // IDE-ED-065: Escape ends field mode
            return;
        }

        // UI-ED-122/123: Up/Down switch overloads, Escape closes parameter info.
        if (document.ParameterInfo is { IsOpen: true } info && e.KeyModifiers == Avalonia.Input.KeyModifiers.None
            && e.Key is Key.Up or Key.Down or Key.Escape && info.HandleKey(e.Key.ToString()))
        {
            e.Handled = true;
            return;
        }

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
