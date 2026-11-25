using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using FreexComp.Ide.Core.Commands;
using Avalonia.Media;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Input;
using FreexComp.Ide.Text;

namespace FreexComp.Ide.Views;

/// <summary>
/// A generated read-only document (UI-MENU-030): its text in a read-only editor with the code font, selectable and
/// copyable. AutomationId <c>Document.&lt;Kind with an upper-case first letter&gt;</c> (<c>Document.KeyboardShortcuts</c>).
/// <c>Editor</c> command scope; the view is the target of <c>edit.copy</c> (selection) and <c>edit.selectAll</c>.
/// The editor shows the buffer of <see cref="GeneratedTextDocumentViewModel.Content"/> and mirrors caret and selection
/// into it, so the find panel (find mode only, the text is read-only) and its highlights work here too (I-058).
/// A help document (UI-MENU-032; kind <c>help.&lt;id&gt;</c>, so <c>Document.Help.&lt;id&gt;</c>) is shown in the body font
/// with word wrap and no horizontal scroll bar, its lines styled by <see cref="HelpPageColorizer"/>, a wrapped list
/// item continuing under its text (<see cref="HelpListItemWrapGenerator"/>).
/// </summary>
public sealed class GeneratedTextDocumentView : Decorator, ICommandTarget
{
    private const string CopyCommandId = "edit.copy";
    private const string SelectAllCommandId = "edit.selectAll";

    private readonly TextEditor _editor = new() { IsReadOnly = true, ShowLineNumbers = false };
    private readonly FindPanelView _findPanel;
    private GeneratedTextDocumentViewModel? _document;
    private FindSession? _find;
    private bool _applying;
    private IDisposable? _fontFamily;
    private IDisposable? _fontSize;

    public GeneratedTextDocumentView()
    {
        CommandRouting.SetScope(this, CommandScope.Editor);
        Bind(TextEditor.BackgroundProperty, "FxcBgEditorBrush");
        Bind(TextEditor.ForegroundProperty, "FxcSyntaxTextBrush");
        _editor.TextArea.Bind(AvaloniaEdit.Editing.TextArea.SelectionBrushProperty, _editor.TextArea.GetResourceObservable("FxcEditorSelectionBgBrush"));
        // I-257: the text keeps space.4 free at the left; at x = 0 the first glyph of every line was cut by the tab's edge.
        var padding = new Border();
        padding.Bind(WidthProperty, padding.GetResourceObservable("FxcSpace4"));
        _editor.TextArea.LeftMargins.Add(padding);
        _editor.TextArea.TextView.BackgroundRenderers.Add(new FindMatchRenderer(this, () => _find));
        _editor.TextArea.TextView.SizeChanged += (_, e) =>
        {
            // I-266: the wrapped lines of a help page's list items are broken for the view's width.
            if (e.WidthChanged && _editor.TextArea.TextView.ElementGenerators.OfType<HelpListItemWrapGenerator>().Any())
                _editor.TextArea.TextView.Redraw();
        };
        _editor.TextArea.Caret.PositionChanged += (_, _) => MirrorSelection();
        _editor.TextArea.SelectionChanged += (_, _) => MirrorSelection();
        _findPanel = new FindPanelView(() => _editor.TextArea.Focus()) { Margin = new Thickness(0, 0, 8, 0) };
        Child = new Panel { Children = { _editor, _findPanel } };
    }

    public bool CanExecute(string commandId, string? argument) => commandId switch
    {
        CopyCommandId => !_editor.TextArea.Selection.IsEmpty,
        SelectAllCommandId => true,
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
            await clipboard.SetTextAsync(area.Selection.GetText());
        }

        return true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Unbind();
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
        _document = DataContext as GeneratedTextDocumentViewModel;
        if (_document is null)
            return;
        var content = _document.Content;
        var id = "Document." + char.ToUpperInvariant(_document.Kind[0]) + _document.Kind[1..];
        AutomationProperties.SetAutomationId(_editor, id);
        AutomationProperties.SetName(_editor, _document.Title);
        var transformers = _editor.TextArea.TextView.LineTransformers;
        foreach (var styles in transformers.OfType<HelpPageColorizer>().ToList())
            transformers.Remove(styles);
        var generators = _editor.TextArea.TextView.ElementGenerators;
        foreach (var wrap in generators.OfType<HelpListItemWrapGenerator>().ToList())
            generators.Remove(wrap);
        var help = _document.Kind.StartsWith(HelpPage.KindPrefix, StringComparison.Ordinal); // UI-MENU-032
        _fontFamily?.Dispose();
        _fontSize?.Dispose();
        _fontFamily = Bind(TextEditor.FontFamilyProperty, help ? "FxcFontUi" : "FxcFontMono");
        _fontSize = Bind(TextEditor.FontSizeProperty, help ? "FxcTypeBodySize" : "FxcTypeCodeSize");
        _editor.WordWrap = help;
        _editor.HorizontalScrollBarVisibility = help ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        if (help)
        {
            generators.Add(new HelpListItemWrapGenerator(() => _document?.HelpLines, indent: false)); // I-266
            generators.Add(new HelpListItemWrapGenerator(() => _document?.HelpLines, indent: true));
            transformers.Add(new HelpPageColorizer(
                () => _document?.HelpLines,
                () => this.TryFindResource("FxcTextPrimaryBrush", ActualThemeVariant, out var value) ? value as IBrush : null,
                () => this.TryFindResource("FxcFontMono", ActualThemeVariant, out var value) ? value as FontFamily : null,
                () => this.TryFindResource("FxcTypeCodeSize", ActualThemeVariant, out var value) && value is double size ? size : _editor.FontSize));
        }

        if (content.Buffer is AvaloniaEditTextBuffer buffer)
        {
            _editor.Document = buffer.Document;
        }
        else
        {
            _editor.Document = new AvaloniaEdit.Document.TextDocument();
            _editor.Text = _document.Text;
        }

        _document.PropertyChanged += OnDocumentChanged;

        ApplySelection();
        content.ViewState.PropertyChanged += OnViewStateChanged;
        content.RevealCaretRequested += OnRevealCaretRequested;
        _find = content.Find;
        _find?.Changed += OnFindChanged;
        _findPanel.SetAutomationIds(id);
        _findPanel.Session = _find;
    }

    private void Unbind()
    {
        if (_document is not null)
        {
            _document.PropertyChanged -= OnDocumentChanged;
            _document.Content.ViewState.PropertyChanged -= OnViewStateChanged;
            _document.Content.RevealCaretRequested -= OnRevealCaretRequested;
        }

        _find?.Changed -= OnFindChanged;
        _find = null;
        _findPanel.Session = null;
        _document = null;
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_document is null)
            return;
        if (e.PropertyName == nameof(GeneratedTextDocumentViewModel.Text) && _document.Content.Buffer is not AvaloniaEditTextBuffer)
            _editor.Text = _document.Text;
        else if (e.PropertyName == nameof(GeneratedTextDocumentViewModel.HelpLines))
            _editor.TextArea.TextView.Redraw();
    }

    private void OnFindChanged(object? sender, EventArgs e) => _editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);

    private void OnRevealCaretRequested(object? sender, EventArgs e) => _editor.TextArea.Caret.BringCaretToView();

    private void OnViewStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_applying && e.PropertyName is nameof(EditorViewState.Caret) or nameof(EditorViewState.SelectionAnchor))
            ApplySelection();
    }

    /// <summary>Core moved the caret or selection (a find): the editor follows.</summary>
    private void ApplySelection()
    {
        if (_document is null)
            return;
        var content = _document.Content;
        var selection = content.Selection;
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

    private void MirrorSelection()
    {
        if (_applying || _document is null)
            return;
        var area = _editor.TextArea;
        _applying = true;
        try
        {
            var anchor = area.Selection.IsEmpty ? area.Caret.Offset : area.Selection.SurroundingSegment.Offset == area.Caret.Offset
                ? area.Selection.SurroundingSegment.EndOffset
                : area.Selection.SurroundingSegment.Offset;
            _document.Content.Selection = new TextSelection(anchor, area.Caret.Offset);
        }
        finally
        {
            _applying = false;
        }
    }

    private IDisposable Bind(AvaloniaProperty property, string resourceKey) => _editor.Bind(property, _editor.GetResourceObservable(resourceKey));
}
