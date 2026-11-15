using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Editor;
using KeyModifiers = Avalonia.Input.KeyModifiers;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of the Output tool window (tool-windows.md §7): a read-only AvaloniaEdit view of the visible channel
/// (UI-TW-500, AutomationId <c>Tool.Output.Text</c>) with whole-line colours (UI-TW-502), auto-scroll (UI-TW-504), the
/// empty-state overlay (UI-TW-507) and navigation from lines naming a source position (UI-TW-503: Enter or double-click;
/// the path part is underlined under the pointer). The view is the target of <c>edit.copy</c> and <c>edit.selectAll</c>.
/// </summary>
public sealed class OutputView : UserControl, ICommandTarget
{
    private const string CopyCommandId = "edit.copy";
    private const string SelectAllCommandId = "edit.selectAll";

    private readonly TextEditor _editor = new() { IsReadOnly = true, ShowLineNumbers = false };
    private readonly TextBlock _empty = new() { Text = Strings.Msg_Output_Empty, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly LineColorizer _colorizer;
    private OutputViewModel? _model;

    public OutputView()
    {
        AutomationProperties.SetAutomationId(_editor, "Tool.Output.Text");
        AutomationProperties.SetName(_editor, Strings.Cmd_view_output);
        AutomationProperties.SetAutomationId(_empty, "Tool.Output.Empty");
        ToolWindowFrame.SetIsPrimary(_editor, true); // UI-TW-508
        Bind(_editor, TextEditor.BackgroundProperty, "FxcBgEditorBrush");
        Bind(_editor, TextEditor.ForegroundProperty, "FxcTextPrimaryBrush");
        Bind(_editor, TextEditor.FontFamilyProperty, "FxcFontMono");
        _editor.TextArea.Bind(TextArea.SelectionBrushProperty, _editor.TextArea.GetResourceObservable("FxcEditorSelectionBgBrush"));
        Bind(_empty, TextBlock.ForegroundProperty, "FxcTextSecondaryBrush");
        _editor.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _editor.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _colorizer = new LineColorizer(this);
        _editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        _editor.TextArea.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _editor.TextArea.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            _ = NavigateCaretLineAsync();
        };
        _editor.TextArea.TextView.PointerMoved += OnPointerMoved;
        _editor.TextArea.TextView.PointerExited += (_, _) => SetHoverLine(0);
        _editor.ContextRequested += OnContextRequested;
        // UI-TW-500: the editor's find panel (find only) over Content, its match highlights, Ctrl+wheel zoom.
        _findPanel = new FindPanelView(() => _editor.TextArea.Focus()) { Margin = new Thickness(0, 0, 8, 0) };
        _findPanel.SetAutomationIds("Tool.Output.Text");
        _editor.TextArea.TextView.BackgroundRenderers.Add(new FindMatchRenderer(this, () => _find));
        _editor.TextArea.Caret.PositionChanged += (_, _) => MirrorSelection();
        _editor.TextArea.SelectionChanged += (_, _) => MirrorSelection();
        _editor.AddHandler(PointerWheelChangedEvent, OnPointerWheel, RoutingStrategies.Tunnel);
        _editor.GetResourceObservable("FxcTypeCodeSize").Subscribe(new Avalonia.Reactive.AnonymousObserver<object?>(v =>
        {
            if (v is double size)
                _baseFontSize = size;
            ApplyFontSize();
        }));
        Content = new Panel { Children = { _editor, _empty, _findPanel } };
        SizeChanged += (_, _) => PlaceEmpty();
    }

    private readonly FindPanelView _findPanel;
    private FindSession? _find;
    private bool _applying;
    private double _baseFontSize = 13;

    private void ApplyFontSize() => _editor.FontSize = _model?.FontSize ?? _baseFontSize;

    /// <summary>UI-TW-500 / UI-ED-061: Ctrl+wheel zooms by 1 pt per notch (when <c>editor.mouseWheelZoom</c> is on).</summary>
    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_model is not { MouseWheelZoom: true } model || e.Delta.Y == 0 || !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || (e.Source as Visual)?.FindAncestorOfType<FindPanelView>(includeSelf: true) is not null)
            return;
        e.Handled = true;
        model.Zoom(e.Delta.Y > 0, (int)Math.Round(_baseFontSize));
    }

    /// <summary>The user moved the caret or selection: <see cref="OutputViewModel.Content"/> follows (a find starts there).</summary>
    private void MirrorSelection()
    {
        if (_applying || _model is null)
            return;
        var area = _editor.TextArea;
        var length = _model.Content.Buffer.Length;
        var segment = area.Selection.SurroundingSegment;
        var anchor = area.Selection.IsEmpty ? area.Caret.Offset : segment.Offset == area.Caret.Offset ? segment.EndOffset : segment.Offset;
        _applying = true;
        try
        {
            _model.Content.Selection = new FreexComp.Ide.Core.Text.TextSelection(Math.Min(anchor, length), Math.Min(area.Caret.Offset, length));
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>A find moved the selection of <see cref="OutputViewModel.Content"/>: the text view follows.</summary>
    private void ApplySelection()
    {
        if (_model is null)
            return;
        var selection = _model.Content.Selection;
        var area = _editor.TextArea;
        var length = area.Document.TextLength;
        _applying = true;
        try
        {
            area.Caret.Offset = Math.Min(selection.Caret, length);
            if (selection.IsEmpty)
                area.ClearSelection();
            else
                area.Selection = Selection.Create(area, Math.Min(selection.Anchor, length), Math.Min(selection.Caret, length));
        }
        finally
        {
            _applying = false;
        }
    }

    private void OnContentViewStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_applying && e.PropertyName is nameof(EditorViewState.Caret) or nameof(EditorViewState.SelectionAnchor))
            ApplySelection();
    }

    private void OnRevealCaretRequested(object? sender, EventArgs e) => _editor.TextArea.Caret.BringCaretToView();

    private void OnFindChanged(object? sender, EventArgs e) => _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

    /// <summary>The 1-based line whose path part is underlined (pointer over it); 0 = none.</summary>
    internal int HoverLine { get; private set; }

    public bool CanExecute(string commandId, string? argument) => commandId switch
    {
        CopyCommandId => !_editor.TextArea.Selection.IsEmpty,
        SelectAllCommandId => _editor.Document.TextLength > 0,
        _ => false,
    };

    public async Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        if (!CanExecute(commandId, argument))
            return false;
        var area = _editor.TextArea;
        if (commandId == SelectAllCommandId)
        {
            area.Selection = Selection.Create(area, 0, area.Document.TextLength);
            area.Caret.Offset = area.Document.TextLength;
        }
        else if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, area.Selection.GetText());
        }

        return true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.TextChanged -= OnTextChanged;
            _model.PropertyChanged -= OnModelChanged;
            _model.Content.ViewState.PropertyChanged -= OnContentViewStateChanged;
            _model.Content.RevealCaretRequested -= OnRevealCaretRequested;
        }

        _find?.Changed -= OnFindChanged;
        _find = null;
        _findPanel.Session = null;
        _model = DataContext as OutputViewModel;
        ApplyFontSize();
        if (_model is null)
            return;
        _model.TextChanged += OnTextChanged;
        _model.PropertyChanged += OnModelChanged;
        _model.Content.ViewState.PropertyChanged += OnContentViewStateChanged;
        _model.Content.RevealCaretRequested += OnRevealCaretRequested;
        _find = _model.Content.Find;
        _find?.Changed += OnFindChanged;
        _findPanel.Session = _find;
        _editor.Document = new TextDocument(Join(_model.Lines));
        _editor.WordWrap = _model.WordWrap;
        _empty.IsVisible = _model.IsEmpty;
        _editor.TextArea.Caret.Offset = _editor.Document.TextLength; // UI-TW-508: caret at the end
    }

    private static string Join(IEnumerable<OutputLine> lines) => string.Join("\n", lines.Select(l => l.Text));

    private void Bind(Control control, AvaloniaProperty property, string key) => control.Bind(property, control.GetResourceObservable(key));

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model is null)
            return;
        if (e.PropertyName == nameof(OutputViewModel.WordWrap))
            _editor.WordWrap = _model.WordWrap;
        else if (e.PropertyName == nameof(OutputViewModel.IsEmpty))
            _empty.IsVisible = _model.IsEmpty;
        else if (e.PropertyName == nameof(OutputViewModel.FontSize))
            ApplyFontSize();
    }

    /// <summary>UI-TW-504: appended text keeps the view at the end when it was there, unless Scroll Lock is on.</summary>
    private void OnTextChanged(object? sender, OutputTextChange change)
    {
        if (_model is null)
            return;
        var follow = !_model.ScrollLock && (change.Reset || IsAtEnd());
        var document = _editor.Document;
        if (change.Reset)
        {
            document.Text = Join(change.Lines);
        }
        else
        {
            var caretAtEnd = _editor.TextArea.Caret.Offset == document.TextLength;
            document.Insert(document.TextLength, (document.TextLength > 0 ? "\n" : "") + Join(change.Lines));
            if (caretAtEnd && follow)
                _editor.TextArea.Caret.Offset = document.TextLength;
        }

        if (follow)
        {
            _editor.TextArea.Caret.Offset = document.TextLength;
            ScrollToEnd();
        }

        _editor.TextArea.TextView.Redraw();
    }

    private ScrollViewer? Scroller => _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private bool IsAtEnd()
    {
        if (Scroller is not { } s)
            return true;
        return s.Offset.Y + s.Viewport.Height >= s.Extent.Height - 1;
    }

    private void ScrollToEnd()
    {
        if (Scroller is not { } s)
            return;
        _editor.UpdateLayout();
        s.Offset = new Vector(s.Offset.X, Math.Max(0, s.Extent.Height - s.Viewport.Height));
    }

    /// <summary>UI-TW-503: Enter in a line navigates. Ctrl+End goes to the end, so following resumes (UI-TW-504).</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            _ = NavigateCaretLineAsync();
        }
        else if (e.Key == Key.End && e.KeyModifiers == KeyModifiers.Control)
        {
            // UI-TW-504: also when the caret already is at the end but the view was scrolled up.
            e.Handled = true;
            _editor.TextArea.ClearSelection();
            _editor.TextArea.Caret.Offset = _editor.Document.TextLength;
            ScrollToEnd();
        }
    }

    private Task<bool> NavigateCaretLineAsync() =>
        _model?.NavigateToLineAsync(_editor.TextArea.Caret.Line) ?? Task.FromResult(false);

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var view = _editor.TextArea.TextView;
        var position = view.GetPosition(e.GetPosition(view) + view.ScrollOffset);
        var line = position is { } p && Location(p.Line) is { } location
            && p.Column - 1 >= location.PathStart && p.Column - 1 < location.PathStart + location.PathLength
            ? p.Line
            : 0;
        SetHoverLine(line);
    }

    private void SetHoverLine(int line)
    {
        if (line == HoverLine)
            return;
        HoverLine = line;
        _editor.TextArea.TextView.Cursor = line > 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        _editor.TextArea.TextView.Redraw();
    }

    private OutputLocation? Location(int line) =>
        _model is not null && line >= 1 && line <= _model.Lines.Count ? OutputLocation.TryParse(_model.Lines[line - 1].Text) : null;

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_model is null)
            return;
        var menu = MenuBarBuilder.CreateContextMenu(_model.BuildContextMenu());
        menu.Open(_editor);
        e.Handled = true;
    }

    /// <summary>UI-TW-013: the empty-state text sits at one third of the content height.</summary>
    private void PlaceEmpty() => _empty.Margin = new Thickness(0, Math.Max(0, Bounds.Height / 3), 0, 0);

    /// <summary>UI-TW-502 line colours and the UI-TW-503 hover underline.</summary>
    private sealed class LineColorizer(OutputView owner) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            var model = owner._model;
            if (model is null || line.LineNumber > model.Lines.Count || line.Length == 0)
                return;
            var token = OutputViewModel.ColorToken(model.SelectedChannel, model.Lines[line.LineNumber - 1]);
            if (owner.TryFindResource(IconCatalog.BrushKey(token), out var resource) && resource is IBrush brush)
                ChangeLinePart(line.Offset, line.EndOffset, e => e.TextRunProperties.SetForegroundBrush(brush));
            if (line.LineNumber == owner.HoverLine && owner.Location(line.LineNumber) is { } location)
            {
                var start = line.Offset + location.PathStart;
                ChangeLinePart(start, Math.Min(line.EndOffset, start + location.PathLength),
                    e => e.TextRunProperties.SetTextDecorations(TextDecorations.Underline));
            }
        }
    }
}
