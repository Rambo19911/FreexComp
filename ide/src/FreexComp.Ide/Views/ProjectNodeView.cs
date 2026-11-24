using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;
using Avalonia.VisualTree;

namespace FreexComp.Ide.Views;

/// <summary>
/// One row (UI-TW-100…107): icon with the main-source / missing overlays, the name (bold for the root and the active
/// document, filter match highlighted, disabled colour when missing), and the badge or the modified dot on the right.
/// While <see cref="ProjectNodeViewModel.Rename"/> is set the name is an input (<c>.Editor</c>, UI-TW-112) with its
/// validation message in a popup under it (<c>.Error</c>).
/// </summary>
public sealed class ProjectNodeView : DockPanel
{
    /// <summary>Style class of the row container while it is the drop target (UI-TW-121).</summary>
    internal const string DropTargetClass = "dropTarget";

    private readonly IdeIcon _icon = new() { Width = 16, Height = 16 };
    private readonly IdeIcon _missing = new() { IconName = "warning", Width = 9, Height = 9, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _name = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Ellipse _dot = new() { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
    private readonly FreexComp.Ide.Designer.ProgressRing _busy = new(12, 1.5) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
    private ProjectNodeViewModel? _bound;
    private TextBox? _editor;
    private Popup? _errorPopup;
    private Panel? _editorHost;

    public ProjectNodeView()
    {
        _dot.Bind(Shape.FillProperty, _dot.GetResourceObservable("FxcTabModifiedDotBrush"));
        ActualThemeVariantChanged += (_, _) => Update();
    }

    private ProjectNodeViewModel Node => _bound!;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Update(); // the row container exists now: its drop-target class follows the node
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _bound?.PropertyChanged -= OnNodeChanged;
        _bound = DataContext as ProjectNodeViewModel;
        (_icon.Parent as Panel)?.Children.Clear();
        Children.Clear();
        _errorPopup?.IsOpen = false;
        (_editor, _errorPopup, _editorHost) = (null, null, null);
        if (_bound is not { } node)
            return;

        node.PropertyChanged += OnNodeChanged;
        var id = ProjectManagerView.RowPrefix + node.Key;
        AutomationProperties.SetAutomationId(_icon, id + ".Icon");
        AutomationProperties.SetAutomationId(_missing, id + ".Icon.Missing");
        AutomationProperties.SetAutomationId(_name, id + ".Name");
        AutomationProperties.SetAutomationId(_dot, id + ".Modified");
        AutomationProperties.SetAutomationId(_busy, id + ".Busy");

        if (node.Badge is { } badge)
        {
            var text = new TextBlock { Text = badge, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("FxcTextTertiaryBrush"));
            AutomationProperties.SetAutomationId(text, id + ".Badge");
            SetDock(text, Avalonia.Controls.Dock.Right);
            Children.Add(text);
        }

        var glyph = new Panel { Width = 16, Height = 16, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Children = { _icon, _missing } };
        if (node.IsMainSource)
        {
            var play = new IdeIcon { IconName = "run", Width = 8, Height = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            AutomationProperties.SetAutomationId(play, id + ".Icon.MainSource");
            glyph.Children.Add(play);
        }

        SetDock(glyph, Avalonia.Controls.Dock.Left);
        Children.Add(glyph);
        Children.Add(_name);
        Update();
        if (node.Rename is not null)
            UpdateEditor(); // a row created by the operation that names it (new folder, new file): the editor from the start
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectNodeViewModel.Rename) or nameof(ProjectNodeViewModel.BusyName))
            UpdateEditor();
        if (e.PropertyName != nameof(ProjectNodeViewModel.Rename))
            Update();
    }

    /// <summary>UI-TW-112: the input replaces the label, focused with the base name selected.</summary>
    private void UpdateEditor()
    {
        if (_editor is not null)
        {
            // The fields first: taking the focused editor out of the tree makes it lose focus, which must not commit.
            var (oldPopup, oldHost) = (_errorPopup!, _editorHost!);
            (_editor, _errorPopup, _editorHost) = (null, null, null);
            oldPopup.IsOpen = false;
            Children.Remove(oldHost);
            if (!Children.Contains(_name))
                Children.Add(_name);
        }

        if (Node.Rename is not { } rename || Node.BusyName is not null) // UI-TW-118: closed while the rename is busy
            return;
        var id = ProjectManagerView.RowPrefix + Node.Key;
        var editor = new TextBox { Text = rename.NewName, MinHeight = 20, Padding = new Thickness(2, 0), VerticalContentAlignment = VerticalAlignment.Center };
        // UI-TW-116: the naming editor of a new folder has its own id; file rows keep UI-TW-112's.
        AutomationProperties.SetAutomationId(editor, id + (Node.Kind == ProjectNodeKind.Folder ? ".NameEditor" : ".Editor"));
        AutomationProperties.SetName(editor, rename.Title);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
        message.Classes.Add("caption");
        message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(message, id + ".Error");
        var frame = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(6, 3), Child = message };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcStatusErrorBrush"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("FxcBgRaisedBrush"));
        var popup = new Popup { PlacementTarget = editor, Placement = PlacementMode.BottomEdgeAlignedLeft, ShouldUseOverlayLayer = true, Child = frame };

        void ShowError()
        {
            message.Text = rename.Error;
            popup.IsOpen = rename.Error is not null && _editor == editor;
            editor.Classes.Set("invalid", rename.Error is not null);
        }

        editor.TextChanged += (_, _) => rename.NewName = editor.Text ?? "";
        rename.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RenameFileViewModel.Error))
                ShowError();
        };
        editor.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.None)
                return;
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = rename.CommitAsync();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                rename.Cancel();
            }
        }, RoutingStrategies.Tunnel);
        editor.LostFocus += (_, _) => _ = _editor == editor ? rename.FocusLostAsync() : Task.CompletedTask; // focus loss commits (UI-TW-112; UI-TW-116 for folders)

        Children.Remove(_name);
        _editorHost = new Panel { Children = { editor, popup } };
        Children.Add(_editorHost);
        _editor = editor;
        _errorPopup = popup;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_editor != editor)
                return;
            editor.Focus(NavigationMethod.Tab);
            editor.SelectionStart = 0;
            // A refused rename opens the editor again with the typed name selected (IDE-PM-134).
            editor.SelectionEnd = rename.NewName == rename.OriginalName ? rename.BaseNameLength : System.IO.Path.GetFileNameWithoutExtension(rename.NewName).Length;
            ShowError();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void Update()
    {
        if (_bound is null)
            return;

        _icon.IconName = Node.IconName;
        _icon.IsEnabled = !Node.IsMissing;
        Opacity = Node.IsDragged ? 0.5 : 1; // UI-TW-120
        this.FindAncestorOfType<TreeViewItem>()?.Classes.Set(DropTargetClass, Node.IsDropTarget); // UI-TW-121
        _missing.IsVisible = Node.IsMissing;
        _name.Classes.Set("body", !Strong);
        _name.Classes.Set("bodyStrong", Strong);
        _name.Bind(TextBlock.ForegroundProperty, _name.GetResourceObservable(
            Node.BusyName is not null ? "FxcTextSecondaryBrush" : Node.IsMissing ? "FxcTextDisabledBrush" : "FxcTextPrimaryBrush"));
        _name.Inlines!.Clear();
        if (Node.BusyName is { } typed)
        {
            _name.Text = typed; // UI-TW-118
        }
        else if (Node.MatchStart >= 0 && Node.MatchLength > 0)
        {
            var highlight = this.TryFindResource("FxcEditorFindMatchBgBrush", ActualThemeVariant, out var brush) ? brush as IBrush : null;
            _name.Inlines.Add(new Run(Node.Name[..Node.MatchStart]));
            _name.Inlines.Add(new Run(Node.Name.Substring(Node.MatchStart, Node.MatchLength)) { Background = highlight });
            _name.Inlines.Add(new Run(Node.Name[(Node.MatchStart + Node.MatchLength)..]));
        }
        else
        {
            _name.Text = Node.Name;
        }

        // UI-TW-118: the 12 px ring takes the place of the modified marker while the rename is busy.
        var busy = Node.BusyName is not null;
        if (busy && !Children.Contains(_busy))
        {
            SetDock(_busy, Avalonia.Controls.Dock.Right);
            Children.Insert(0, _busy);
        }
        else if (!busy)
        {
            Children.Remove(_busy);
        }

        var showDot = Node.IsModified && !busy;
        if (showDot && !Children.Contains(_dot))
        {
            SetDock(_dot, Avalonia.Controls.Dock.Right);
            Children.Insert(0, _dot);
        }
        else if (!showDot)
        {
            Children.Remove(_dot);
        }
    }

    private bool Strong => Node.Kind == ProjectNodeKind.Project || Node.IsActiveDocument;
}
