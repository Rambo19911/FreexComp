using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Designer;

namespace FreexComp.Ide.Views;

/// <summary>
/// A unit with a companion form (designer.md §1, UI-DSG-001/002; I-129): the active view above a 26-high view switcher
/// with the tabs <c>Code</c>, <c>Design</c> and <c>Text</c>. Only the active view is in the visual tree, so each text
/// buffer has a single live editor (I-043). The Design view hosts the designer surface in a scroll viewer.
/// </summary>
public sealed class FormDocumentView : DockPanel
{
    private const double SwitcherHeight = 26; // UI-DSG-001
    private const double TabPadding = 12;     // space.5
    private const double TabGap = 4;          // space.2

    private readonly Panel _content = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = TabGap };
    private readonly Dictionary<DocumentView, ToggleButton> _tabButtons = [];
    private readonly Dictionary<DocumentView, Control> _views = [];
    private FormDocumentViewModel? _document;

    public FormDocumentView()
    {
        var strip = new Border { Height = SwitcherHeight, BorderThickness = new Thickness(0, 1, 0, 0), Child = _tabs };
        strip.Bind(Border.BackgroundProperty, strip.GetResourceObservable("FxcBgWindowBrush"));
        strip.Bind(Border.BorderBrushProperty, strip.GetResourceObservable("FxcBorderDefaultBrush"));
        SetDock(strip, Avalonia.Controls.Dock.Bottom);
        Children.Add(strip);
        Children.Add(_content);
        foreach (var (view, label) in (ReadOnlySpan<(DocumentView, string)>)
                 [(DocumentView.Code, Strings.DocumentView_Code), (DocumentView.Design, Strings.DocumentView_Design), (DocumentView.Text, Strings.DocumentView_Text)])
        {
            var button = CreateTab(label);
            button.Click += (_, _) =>
            {
                _document?.ShowView(view);
                Show(focus: true); // a click on the active tab must not leave it unchecked
            };
            _tabButtons[view] = button;
            _tabs.Children.Add(button);
        }
    }

    /// <summary>A flat toggle tab: caption text; when active, <c>bg.editor</c> with a 2 px <c>tab.active.indicator</c> line at its bottom.</summary>
    private static ToggleButton CreateTab(string label)
    {
        var text = new TextBlock { Text = label, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
        var face = new Border { Padding = new Thickness(TabPadding, 0), BorderThickness = new Thickness(0, 0, 0, 2), Child = text };
        var button = new ToggleButton
        {
            Content = face,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        AutomationProperties.SetName(button, label);
        return button;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _document?.PropertyChanged -= OnDocumentChanged;
        _document = DataContext as FormDocumentViewModel;
        _document?.PropertyChanged += OnDocumentChanged;
        _views.Clear();
        _content.Children.Clear();
        if (_document is null)
            return;
        foreach (var (view, button) in _tabButtons)
            AutomationProperties.SetAutomationId(button, AutomationIds.DocumentViewTab(_document.Title, view.ToString()));
        Show(focus: false);
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FormDocumentViewModel.ActiveView))
            Show(focus: IsKeyboardFocusWithin || _content.Children.Count == 0);
        else if (e.PropertyName == nameof(DocumentViewModel.Title) && _document is not null)
            foreach (var (view, button) in _tabButtons)
                AutomationProperties.SetAutomationId(button, AutomationIds.DocumentViewTab(_document.Title, view.ToString()));
    }

    /// <summary>Puts the active view in place, marks its tab and (when focus was in the document) focuses it.</summary>
    private void Show(bool focus)
    {
        if (_document is not { } document)
            return;
        var active = document.ActiveView;
        if (!_views.TryGetValue(active, out var view))
            _views[active] = view = CreateView(document, active);
        if (_content.Children.Count != 1 || _content.Children[0] != view)
        {
            _content.Children.Clear();
            _content.Children.Add(view);
        }

        foreach (var (tabView, button) in _tabButtons)
        {
            var isActive = tabView == active;
            button.IsChecked = isActive;
            var face = (Border)button.Content!;
            face.Bind(Border.BackgroundProperty, face.GetResourceObservable(isActive ? "FxcBgEditorBrush" : "FxcBgWindowBrush"));
            if (isActive)
                face.Bind(Border.BorderBrushProperty, face.GetResourceObservable("FxcTabActiveIndicatorBrush"));
            else
                face.BorderBrush = Brushes.Transparent;
        }

        if (focus)
            Dispatcher.UIThread.Post(() => Focus(view), DispatcherPriority.Loaded);
    }

    private static void Focus(Control view)
    {
        if (view is TextDocumentView text)
            text.FocusText();
        else if (view is DockPanel { Children: [_, TextDocumentView formText] })
            formText.FocusText();
        else if (view is DockPanel { Children: [_, Panel { Children: [ScrollViewer { Content: DesignSurfaceControl surface }, _] }] })
            surface.Focus();
        else
            view.Focus();
    }

    private static Control CreateView(FormDocumentViewModel document, DocumentView view)
    {
        switch (view)
        {
            case DocumentView.Code:
                return new TextDocumentView { DataContext = document };
            case DocumentView.Text:
                // DSG-FD-100: the document's own bars (external change or deletion of either file) above the form text's.
                var textView = new DockPanel();
                var documentBars = new ItemsControl { ItemsSource = document.VisibleInfoBars };
                SetDock(documentBars, Avalonia.Controls.Dock.Top);
                textView.Children.Add(documentBars);
                textView.Children.Add(new TextDocumentView { DataContext = document.FormText });
                return textView;
            default:
                // UI-DSG-003: the backdrop scrolls (both scroll bars, auto) when the surface exceeds the view (D-078).
                var surface = new DesignSurfaceControl { DataContext = document.Designer };
                var scroller = new ScrollViewer
                {
                    Content = surface,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                };
                scroller.Bind(ScrollViewer.BackgroundProperty, scroller.GetResourceObservable("FxcDesignerBackdropBrush"));
                // UI-DSG-004: the info bars (designer.md §9) sit above the backdrop, outside the scroll area.
                // DSG-FD-100: the document's own bars (external change or deletion) first, then the designer's.
                var bars = new StackPanel
                {
                    Children =
                    {
                        new ItemsControl { ItemsSource = document.VisibleInfoBars },
                        new ItemsControl { ItemsSource = document.Designer.DesignInfoBars },
                    },
                };
                // DSG-TP-003: while the component metadata loads, a centered progress ring stands in for the surface.
                var designer = document.Designer;
                var ring = new ProgressRing { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetAutomationId(ring, AutomationIds.DesignerLoading(Path.GetFileNameWithoutExtension(document.Title)));
                var loading = new Panel { Children = { ring } };
                loading.Bind(Panel.BackgroundProperty, loading.GetResourceObservable("FxcDesignerBackdropBrush"));
                void ShowLoading()
                {
                    loading.IsVisible = designer.IsMetadataLoading;
                    scroller.IsVisible = !designer.IsMetadataLoading;
                }

                ShowLoading();
                designer.MetadataLoadingChanged += (_, _) => ShowLoading();
                var design = new DockPanel();
                SetDock(bars, Avalonia.Controls.Dock.Top);
                design.Children.Add(bars);
                design.Children.Add(new Panel { Children = { scroller, loading } });
                return design;
        }
    }
}
