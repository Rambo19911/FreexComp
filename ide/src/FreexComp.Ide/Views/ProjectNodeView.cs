using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// One row (UI-TW-100…107): icon with the main-source / missing overlays, the name (bold for the root and the active
/// document, filter match highlighted, disabled colour when missing), and the badge or the modified dot on the right.
/// </summary>
public sealed class ProjectNodeView : DockPanel
{
    private readonly IdeIcon _icon = new() { Width = 16, Height = 16 };
    private readonly IdeIcon _missing = new() { IconName = "warning", Width = 9, Height = 9, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _name = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Ellipse _dot = new() { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
    private ProjectNodeViewModel? _bound;

    public ProjectNodeView()
    {
        _dot.Bind(Shape.FillProperty, _dot.GetResourceObservable("FxcTabModifiedDotBrush"));
        ActualThemeVariantChanged += (_, _) => Update();
    }

    private ProjectNodeViewModel Node => _bound!;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _bound?.PropertyChanged -= OnNodeChanged;
        _bound = DataContext as ProjectNodeViewModel;
        (_icon.Parent as Panel)?.Children.Clear();
        Children.Clear();
        if (_bound is not { } node)
            return;

        node.PropertyChanged += OnNodeChanged;
        var id = ProjectManagerView.RowPrefix + node.Key;
        AutomationProperties.SetAutomationId(_icon, id + ".Icon");
        AutomationProperties.SetAutomationId(_missing, id + ".Icon.Missing");
        AutomationProperties.SetAutomationId(_name, id + ".Name");
        AutomationProperties.SetAutomationId(_dot, id + ".Modified");

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
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        if (_bound is null)
            return;

        _icon.IconName = Node.IconName;
        _icon.IsEnabled = !Node.IsMissing;
        _missing.IsVisible = Node.IsMissing;
        _name.Classes.Set("body", !Strong);
        _name.Classes.Set("bodyStrong", Strong);
        _name.Bind(TextBlock.ForegroundProperty, _name.GetResourceObservable(Node.IsMissing ? "FxcTextDisabledBrush" : "FxcTextPrimaryBrush"));
        _name.Inlines!.Clear();
        if (Node.MatchStart >= 0 && Node.MatchLength > 0)
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

        var showDot = Node.IsModified;
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
