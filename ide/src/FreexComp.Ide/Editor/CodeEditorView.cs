using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using FreexComp.Ide.Core.Commands;
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
/// The <c>Editor</c> command scope; the view is the command target for the commands that need the selection
/// (clipboard, select all) and passes the rest to the document.
/// </summary>
public sealed class CodeEditorView : Decorator, ICommandTarget
{
    private const string CutCommandId = "edit.cut";
    private const string CopyCommandId = "edit.copy";
    private const string PasteCommandId = "edit.paste";
    private const string SelectAllCommandId = "edit.selectAll";

    private readonly TextEditor _editor = new();
    private TextDocumentViewModel? _document;
    private bool _applying;
    private bool _scrollPending;
    private SyntaxColorizer? _colorizer;

    public CodeEditorView()
    {
        CommandRouting.SetScope(this, CommandScope.Editor);
        _editor.ShowLineNumbers = false; // the gutter is I-045's
        Bind(_editor, TextEditor.BackgroundProperty, "FxcBgEditorBrush");
        Bind(_editor, TextEditor.ForegroundProperty, "FxcSyntaxTextBrush");
        Bind(_editor, TextEditor.FontFamilyProperty, "FxcFontMono");
        Bind(_editor, TextEditor.FontSizeProperty, "FxcTypeCodeSize");
        Bind(_editor.TextArea, TextArea.CaretBrushProperty, "FxcEditorCaretBrush");
        Bind(_editor.TextArea, TextArea.SelectionBrushProperty, "FxcEditorSelectionBgBrush");
        RemoveBuiltInBindings(_editor.TextArea);
        _editor.TextArea.TextEntering += OnTextEntering;
        _editor.TextArea.Caret.PositionChanged += (_, _) => MirrorSelection();
        _editor.TextArea.SelectionChanged += (_, _) => MirrorSelection();
        _editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => MirrorScroll();
        Child = _editor;
        // UI-ED-003: the brushes bound above follow the theme by themselves; syntax colours need a redraw.
        ActualThemeVariantChanged += (_, _) =>
        {
            _colorizer?.ThemeChanged();
            _editor.TextArea.TextView.Redraw();
        };
    }

    /// <summary>The hosted editor (tests and later editor tasks).</summary>
    public TextEditor Editor => _editor;

    public bool CanExecute(string commandId, string? argument)
    {
        if (_document is null)
            return false;
        return commandId switch
        {
            CutCommandId or CopyCommandId => !_editor.TextArea.Selection.IsEmpty,
            PasteCommandId or SelectAllCommandId => true,
            _ => ((ICommandTarget)_document).CanExecute(commandId, argument),
        };
    }

    public async Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        if (_document is not { } document || !CanExecute(commandId, argument))
            return false;
        var area = _editor.TextArea;
        switch (commandId)
        {
            case CopyCommandId:
                await SetClipboardAsync(area.Selection.GetText());
                return true;
            case CutCommandId:
                var cut = area.Selection.GetText();
                if (InsertAtSelection(document, ""))
                    await SetClipboardAsync(cut);
                return true;
            case PasteCommandId:
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && await clipboard.TryGetTextAsync() is { Length: > 0 } text
                    && _document == document)
                    InsertAtSelection(document, text);
                return true;
            case SelectAllCommandId:
                area.Selection = Selection.Create(area, 0, area.Document.TextLength);
                area.Caret.Offset = area.Document.TextLength;
                return true;
            default:
                return await ((ICommandTarget)document).ExecuteAsync(commandId, argument);
        }
    }

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

    private static void Bind(Control target, AvaloniaProperty property, string resourceKey) =>
        target.Bind(property, target.GetResourceObservable(resourceKey));

    /// <summary>
    /// AvaloniaEdit's undo/redo would bypass the buffer's save-point tracking (IDE-ARCH-030); its paste would insert
    /// the current line's break and skip the read-only refusal. <c>edit.undo/redo/paste</c> replace them.
    /// </summary>
    private static void RemoveBuiltInBindings(TextArea area)
    {
        static bool IsReplaced(object? command) =>
            command == ApplicationCommands.Undo || command == ApplicationCommands.Redo || command == ApplicationCommands.Paste;

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
        }

        SetColorizer(null);

        _document = null; // nothing is mirrored while the editor switches documents
        // Unbound: an empty document, not null (the editor template binds to Document.Text), and no reference to the old one.
        _editor.Document = document is null ? new AvaloniaEdit.Document.TextDocument()
            : (document.Buffer as AvaloniaEditTextBuffer ?? throw new InvalidOperationException("The code editor needs an AvaloniaEdit buffer.")).Document;
        if (document is null)
            return;

        _editor.IsReadOnly = document.IsReadOnly;
        AutomationProperties.SetAutomationId(_editor, AutomationIds.Editor(document.Title));
        ApplySelection(document);
        SetColorizer(document);
        _document = document;
        document.PropertyChanged += OnDocumentPropertyChanged;
        document.ViewState.PropertyChanged += OnViewStatePropertyChanged;

        // The scroll offsets can be applied once the editor has measured its text.
        _scrollPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_document != document)
                return;
            // TextEditor.ScrollToVerticalOffset does not move the template's ScrollViewer (AvaloniaEdit 12.0.0).
            if (_editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroller)
                scroller.Offset = new Vector(document.ViewState.HorizontalOffset, document.ViewState.VerticalOffset);
            _scrollPending = false;
            MirrorScroll();
        }, DispatcherPriority.Loaded);
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_document is not { } document)
            return;
        if (e.PropertyName == nameof(TextDocumentViewModel.IsReadOnly))
            _editor.IsReadOnly = document.IsReadOnly;
        else if (e.PropertyName == nameof(TextDocumentViewModel.Title))
            AutomationProperties.SetAutomationId(_editor, AutomationIds.Editor(document.Title));
        else if (e.PropertyName == nameof(TextDocumentViewModel.Language))
            SetColorizer(document); // Save As with another extension
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
        _colorizer = new SyntaxColorizer(document.Buffer, document.Language.CreateClassifier(), this);
        _colorizer.Classifier.ClassificationChanged += OnClassificationChanged;
        transformers.Add(_colorizer);
    }

    private void OnClassificationChanged(object? sender, EventArgs e) => _editor.TextArea.TextView.Redraw();

    /// <summary>Core changed the caret or selection (revert, save trimming): the editor follows.</summary>
    private void OnViewStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_applying && _document is { } document
            && e.PropertyName is nameof(EditorViewState.Caret) or nameof(EditorViewState.SelectionAnchor))
            ApplySelection(document);
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
            if (state.SelectionAnchor is { } anchor && OffsetOf(document.Buffer, anchor) != caret)
                area.Selection = Selection.Create(area, OffsetOf(document.Buffer, anchor), caret);
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
    }

    /// <summary>AvaloniaEdit never inserts user text itself; Enter arrives here as a line break (IDE-P1-076).</summary>
    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        e.Handled = true;
        if (_document is { } document && !string.IsNullOrEmpty(e.Text))
            InsertAtSelection(document, e.Text);
    }

    /// <summary>Replaces the selection (or inserts at the caret) through the document; the caret ends after the text.</summary>
    private bool InsertAtSelection(TextDocumentViewModel document, string text)
    {
        var area = _editor.TextArea;
        var (offset, length) = area.Selection.IsEmpty
            ? (area.Caret.Offset, 0)
            : (area.Selection.SurroundingSegment.Offset, area.Selection.SurroundingSegment.Length);
        var before = document.Buffer.Length;
        if (!document.TryInsertText(offset, length, text))
            return false;
        area.ClearSelection();
        area.Caret.Offset = offset + document.Buffer.Length - before + length;
        return true;
    }

    private async Task SetClipboardAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
