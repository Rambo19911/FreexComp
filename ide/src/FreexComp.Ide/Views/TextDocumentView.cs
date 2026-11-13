using Avalonia.Controls;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;

namespace FreexComp.Ide.Views;

/// <summary>
/// A text document in the document area: its info bars (newest on top, editor.md §9) above the code editor
/// (<see cref="CodeEditorView"/>, which takes the document as its <c>DataContext</c>).
/// </summary>
public sealed class TextDocumentView : DockPanel
{
    private readonly ItemsControl _infoBars = new();

    public TextDocumentView()
    {
        SetDock(_infoBars, Avalonia.Controls.Dock.Top);
        Children.Add(_infoBars);
        Children.Add(new CodeEditorView());
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _infoBars.ItemsSource = (DataContext as TextDocumentViewModel)?.InfoBars;
    }
}
