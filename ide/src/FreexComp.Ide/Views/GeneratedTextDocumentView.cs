using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Input;

namespace FreexComp.Ide.Views;

/// <summary>
/// A generated read-only document (UI-MENU-030): its text in a read-only editor with the code font, selectable and
/// copyable. AutomationId <c>Document.&lt;Kind with an upper-case first letter&gt;</c> (<c>Document.KeyboardShortcuts</c>).
/// <c>Editor</c> command scope; the view is the target of <c>edit.copy</c> (selection) and <c>edit.selectAll</c>.
/// </summary>
public sealed class GeneratedTextDocumentView : Decorator, ICommandTarget
{
    private const string CopyCommandId = "edit.copy";
    private const string SelectAllCommandId = "edit.selectAll";

    private readonly TextEditor _editor = new() { IsReadOnly = true, ShowLineNumbers = false };
    private GeneratedTextDocumentViewModel? _document;

    public GeneratedTextDocumentView()
    {
        CommandRouting.SetScope(this, CommandScope.Editor);
        Bind(TextEditor.BackgroundProperty, "FxcBgEditorBrush");
        Bind(TextEditor.ForegroundProperty, "FxcSyntaxTextBrush");
        Bind(TextEditor.FontFamilyProperty, "FxcFontMono");
        Bind(TextEditor.FontSizeProperty, "FxcTypeCodeSize");
        _editor.TextArea.Bind(AvaloniaEdit.Editing.TextArea.SelectionBrushProperty, _editor.TextArea.GetResourceObservable("FxcEditorSelectionBgBrush"));
        Child = _editor;
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
        _document.PropertyChanged += OnDocumentChanged;
        AutomationProperties.SetAutomationId(_editor, "Document." + char.ToUpperInvariant(_document.Kind[0]) + _document.Kind[1..]);
        AutomationProperties.SetName(_editor, _document.Title);
        _editor.Text = _document.Text;
    }

    private void Unbind()
    {
        if (_document is not null)
            _document.PropertyChanged -= OnDocumentChanged;
        _document = null;
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GeneratedTextDocumentViewModel.Text) && _document is not null)
            _editor.Text = _document.Text;
    }

    private void Bind(AvaloniaProperty property, string resourceKey) => _editor.Bind(property, _editor.GetResourceObservable(resourceKey));
}
