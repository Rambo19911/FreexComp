using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Editing;
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

    public GeneratedTextDocumentView()
    {
        CommandRouting.SetScope(this, CommandScope.Editor);
        Bind(TextEditor.BackgroundProperty, "FxcBgEditorBrush");
        Bind(TextEditor.ForegroundProperty, "FxcSyntaxTextBrush");
        Bind(TextEditor.FontFamilyProperty, "FxcFontMono");
        Bind(TextEditor.FontSizeProperty, "FxcTypeCodeSize");
        _editor.TextArea.Bind(AvaloniaEdit.Editing.TextArea.SelectionBrushProperty, _editor.TextArea.GetResourceObservable("FxcEditorSelectionBgBrush"));
        _editor.TextArea.TextView.BackgroundRenderers.Add(new FindMatchRenderer(this, () => _find));
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
        if (content.Buffer is AvaloniaEditTextBuffer buffer)
        {
            _editor.Document = buffer.Document;
        }
        else
        {
            _editor.Document = new AvaloniaEdit.Document.TextDocument();
            _editor.Text = _document.Text;
            _document.PropertyChanged += OnDocumentChanged;
        }

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
        if (e.PropertyName == nameof(GeneratedTextDocumentViewModel.Text) && _document is not null)
            _editor.Text = _document.Text;
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

    private void Bind(AvaloniaProperty property, string resourceKey) => _editor.Bind(property, _editor.GetResourceObservable(resourceKey));
}
