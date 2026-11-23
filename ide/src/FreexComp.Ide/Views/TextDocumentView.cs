using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;

namespace FreexComp.Ide.Views;

/// <summary>
/// A text document in the document area: its shown info bars (newest on top, at most three, editor.md §9) above the
/// code editor (<see cref="CodeEditorView"/>, which takes the document as its <c>DataContext</c>). Keyboard access to
/// the bars (UI-ED-094): the document asks for it (Shift+Tab from the text); Tab past the last button, Shift+Tab before
/// the first one and Escape return to the text area.
/// </summary>
public sealed class TextDocumentView : DockPanel
{
    private readonly ItemsControl _infoBars = new();
    private readonly CodeEditorView _editor = new();
    private TextDocumentViewModel? _document;

    public TextDocumentView()
    {
        SetDock(_infoBars, Avalonia.Controls.Dock.Top);
        Children.Add(_infoBars);
        Children.Add(_editor);
        _infoBars.KeyDown += OnInfoBarKeyDown;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _infoBars.ItemsSource = (DataContext as TextDocumentViewModel)?.VisibleInfoBars;
        if (VisualRoot is not null)
            Subscribe(DataContext as TextDocumentViewModel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(DataContext as TextDocumentViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Subscribe(null);
    }

    private void Subscribe(TextDocumentViewModel? document)
    {
        _document?.InfoBarFocusRequested -= OnInfoBarFocusRequested;
        _document = document;
        _document?.InfoBarFocusRequested += OnInfoBarFocusRequested;
    }

    /// <summary>Moves keyboard focus to the text area (a view switch of a form document, I-129).</summary>
    public bool FocusText() => _editor.FocusText();

    private List<Button> Buttons() =>
        [.. _infoBars.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled)];

    private void OnInfoBarFocusRequested(object? sender, EventArgs e)
    {
        if (Buttons().FirstOrDefault() is { } first)
            first.Focus(NavigationMethod.Tab);
        else // a bar shown in the same turn is not built yet
            Dispatcher.UIThread.Post(() => Buttons().FirstOrDefault()?.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
    }

    private void OnInfoBarKeyDown(object? sender, KeyEventArgs e)
    {
        var buttons = Buttons();
        if (buttons.Count == 0 || e.Source is not Visual source)
            return;
        var index = buttons.FindIndex(b => b == source || b.IsVisualAncestorOf(source));
        var back = (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
            || (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None && index == buttons.Count - 1)
            || (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift && index == 0);
        if (back)
        {
            _editor.FocusText();
            e.Handled = true;
        }
    }
}
