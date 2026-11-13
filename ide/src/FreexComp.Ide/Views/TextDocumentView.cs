using Avalonia.Controls;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Views;

/// <summary>
/// A text document in the document area: its info bars (newest on top, editor.md §9) above the editor. The editor
/// host (<c>CodeEditorView</c>) is added by I-043; the document area shows this view from I-042.
/// </summary>
public sealed class TextDocumentView : DockPanel
{
    private readonly ItemsControl _infoBars = new();

    public TextDocumentView()
    {
        SetDock(_infoBars, Avalonia.Controls.Dock.Top);
        Children.Add(_infoBars);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _infoBars.ItemsSource = (DataContext as TextDocumentViewModel)?.InfoBars;
    }
}
