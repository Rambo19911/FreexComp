using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of the Project Manager tool window (tool-windows.md §2): missing-files info bar, the tree (primary control,
/// <c>Tool.ProjectManager.Tree</c>) and the empty state. All state lives in <see cref="ProjectManagerViewModel"/>; the view
/// mirrors the selection both ways and builds the context menu from the model when it opens.
/// </summary>
public sealed class ProjectManagerView : UserControl
{
    internal const string RowPrefix = "Tool.ProjectManager.Row.";

    private readonly TreeView _tree;
    private readonly Border _barHost = new();
    private readonly Control _empty;
    private readonly StackPanel _links = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8 };
    private ProjectManagerViewModel? _model;
    private Window? _window;
    private bool _syncing;

    public ProjectManagerView()
    {
        _tree = new TreeView
        {
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncTreeDataTemplate<ProjectNodeViewModel>((node, _) => new ProjectNodeView { DataContext = node }, node => node.Children),
            ContextMenu = new ContextMenu(),
        };
        AutomationProperties.SetAutomationId(_tree, "Tool.ProjectManager.Tree");
        ToolWindowFrame.SetIsPrimary(_tree, true);
        _tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters =
            {
                new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(ProjectNodeViewModel.IsExpanded)) { Mode = BindingMode.TwoWay }),
                new Setter(IsVisibleProperty, new Binding(nameof(ProjectNodeViewModel.IsVisibleInTree))),
                new Setter(ToolTip.TipProperty, new Binding(nameof(ProjectNodeViewModel.FullPath))),
                new Setter(AutomationProperties.NameProperty, new Binding(nameof(ProjectNodeViewModel.AutomationName))),
                new Setter(AutomationProperties.AutomationIdProperty, new Binding(nameof(ProjectNodeViewModel.Key)) { StringFormat = RowPrefix + "{0}" }),
                new Setter(MinHeightProperty, 22d),
            },
        });
        _tree.SelectionChanged += OnTreeSelectionChanged;
        _tree.AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
        _tree.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        // UI-TW-113: files dragged from the OS over the tree get the drop-target highlight (design-tokens §2.3
        // accent.subtle); the main window adds them on drop (MainWindowViewModel.DropFilesAsync).
        _tree.AddHandler(DragDrop.DragEnterEvent, OnFilesDraggedOver);
        _tree.AddHandler(DragDrop.DragOverEvent, OnFilesDraggedOver);
        _tree.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropTarget(false), handledEventsToo: true);
        _tree.AddHandler(DragDrop.DropEvent, (_, _) => SetDropTarget(false), handledEventsToo: true);

        _empty = CreateEmptyState();
        var body = new Panel { Children = { _tree, _empty } };
        DockPanel.SetDock(_barHost, Avalonia.Controls.Dock.Top);
        Content = new DockPanel { Children = { _barHost, body } };
    }

    private IDisposable? _dropHighlight;

    private void OnFilesDraggedOver(object? sender, DragEventArgs e) =>
        SetDropTarget(e.DataTransfer.TryGetFiles() is { Length: > 0 });

    private void SetDropTarget(bool on)
    {
        if (on && _dropHighlight is null)
            _dropHighlight = _tree.Bind(BackgroundProperty, _tree.GetResourceObservable("FxcAccentSubtleBrush"));
        else if (!on)
        {
            _dropHighlight?.Dispose();
            _dropHighlight = null;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
            _model.SelectionChanged -= OnModelSelectionChanged;
            _model.RenameEnded -= OnRenameEnded;
        }

        _model?.Roots.CollectionChanged -= OnRootsChanged;
        _model = DataContext as ProjectManagerViewModel;
        _model?.Roots.CollectionChanged += OnRootsChanged; // before the tree's own handler: focus is still inside
        _tree.ItemsSource = _model?.Roots;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
            _model.SelectionChanged += OnModelSelectionChanged;
            _model.RenameEnded += OnRenameEnded;
            BuildEmptyLinks();
        }

        UpdateState();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        _window?.Activated += OnWindowActivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _window?.Activated -= OnWindowActivated;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowActivated(object? sender, EventArgs e) => _model?.OnWindowActivated();

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectManagerViewModel.HasProject) or nameof(ProjectManagerViewModel.MissingFilesBar))
            UpdateState();
    }

    private void UpdateState()
    {
        var hasProject = _model?.HasProject == true;
        _tree.IsVisible = hasProject;
        _empty.IsVisible = !hasProject;
        _barHost.Child = _model?.MissingFilesBar is { } bar
            ? new InfoBarView { AutomationIdBase = "Tool.ProjectManager.InfoBar", DataContext = bar }
            : null;
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _model is null)
            return;
        _model.SetSelection([.. _tree.SelectedItems.OfType<ProjectNodeViewModel>()]);
    }

    /// <summary>The model's selection (sync, Show, UI-TW-109 corrections) goes to the tree; the last row is brought into view.</summary>
    private void OnModelSelectionChanged(object? sender, EventArgs e)
    {
        if (_model is null || _model.Selection.SequenceEqual(_tree.SelectedItems.OfType<ProjectNodeViewModel>()))
            return;
        _syncing = true;
        try
        {
            _tree.SelectedItems.Clear();
            foreach (var node in _model.Selection)
                _tree.SelectedItems.Add(node);
        }
        finally
        {
            _syncing = false;
        }

        if (_model.Selection.Count > 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_model is { Selection.Count: > 0 } m && _tree.TreeContainerFromItem(m.Selection[^1]) is Control container)
                    container.BringIntoView();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// IDE-PM-100: a rebuild after a file operation replaces every row; when focus was in the tree it stays there (on the
    /// selected row, else the tree) instead of falling back to the document.
    /// </summary>
    private void OnRootsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // IsActive covers a confirmation box that holds focus meanwhile (focus is tracked in the main window only).
        if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset || !(_tree.IsKeyboardFocusWithin || _model?.IsActive == true))
            return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var row = _model is { Selection.Count: > 0 } m ? _tree.TreeContainerFromItem(m.Selection[^1]) : _tree.ContainerFromIndex(0);
            row?.Focus(NavigationMethod.Tab);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>UI-TW-112: after the in-place rename, focus returns to the (renamed) selected row.</summary>
    private void OnRenameEnded(object? sender, EventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_model is { Selection.Count: > 0 } m && _tree.TreeContainerFromItem(m.Selection[^1]) is Control container)
                container.Focus(NavigationMethod.Tab);
        }, Avalonia.Threading.DispatcherPriority.Loaded);

    /// <summary>
    /// UI-TW-014: a right-click on an unselected row selects it first; on the empty area the selection is cleared.
    /// A double-click runs the default action (UI-TW-018) instead of the tree's own expand toggle.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_model is null)
            return;
        var node = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as ProjectNodeViewModel;
        var point = e.GetCurrentPoint(_tree);
        if (point.Properties.IsRightButtonPressed)
        {
            if (node is null)
                _model.ClearSelection();
            else if (!_model.Selection.Contains(node))
                _model.Select(node);
            e.Handled = true;
        }
        else if (point.Properties.IsLeftButtonPressed && e.ClickCount == 2 && node is not null)
        {
            _model.Select(node);
            _ = _model.OpenSelectionAsync();
            e.Handled = true;
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_model is null)
            return;
        var menu = MenuBarBuilder.CreateContextMenu(_model.ContextMenu());
        _tree.ContextMenu!.Items.Clear();
        foreach (var item in menu.Items.Cast<Control>().ToList())
        {
            menu.Items.Remove(item);
            _tree.ContextMenu.Items.Add(item);
        }
    }

    /// <summary>UI-TW-013 empty state: <c>Msg.Project.NoProject</c>, its secondary line and the two links.</summary>
    private Control CreateEmptyState()
    {
        var heading = new TextBlock { Text = Strings.Msg_Project_NoProject, Classes = { "body" }, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        heading.Bind(TextBlock.ForegroundProperty, heading.GetResourceObservable("FxcTextSecondaryBrush"));
        var secondary = new TextBlock { Text = Strings.ProjectManager_NoProject_Secondary, Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        secondary.Bind(TextBlock.ForegroundProperty, secondary.GetResourceObservable("FxcTextTertiaryBrush"));
        var block = new StackPanel { Spacing = 4, Children = { heading, secondary, _links } };
        Grid.SetRow(block, 1);
        var grid = new Grid { RowDefinitions = new RowDefinitions("1*,Auto,2*"), Children = { block } };
        AutomationProperties.SetAutomationId(grid, "Tool.ProjectManager.Empty");
        return grid;
    }

    private void BuildEmptyLinks()
    {
        _links.Children.Clear();
        foreach (var command in _model!.EmptyLinks)
        {
            var link = new Button
            {
                Content = command.Title,
                Command = command,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
            };
            link.Bind(ForegroundProperty, link.GetResourceObservable("FxcTextLinkBrush"));
            AutomationProperties.SetAutomationId(link, "Tool.ProjectManager.Empty.Link." + command.Id);
            _links.Children.Add(link);
        }
    }
}
