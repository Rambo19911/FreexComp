using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Views;

/// <summary>
/// One info bar (editor.md §9, UI-ED-090/091/095): message text, action buttons, close button for dismissible kinds.
/// Stacking limits and keyboard access (UI-ED-092/094) are task I-056's.
/// </summary>
public sealed class InfoBarView : Border
{
    /// <summary>The AutomationId stem: <c>InfoBar</c> in documents, <c>Tool.&lt;Name&gt;.InfoBar</c> in a tool window (tool-windows.md §17.3).</summary>
    public string AutomationIdBase { get; init; } = "InfoBar";

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Child = DataContext is InfoBarViewModel model ? Build(model) : null;
    }

    private Control Build(InfoBarViewModel model)
    {
        var id = AutomationIdBase + "." + model.Kind;
        AutomationProperties.SetAutomationId(this, id);
        this.Bind(BackgroundProperty, this.GetResourceObservable(model.Severity switch
        {
            InfoBarSeverity.Warning => "FxcStatusWarningBgBrush",
            InfoBarSeverity.Error => "FxcStatusErrorBgBrush",
            _ => "FxcAccentSubtleBrush",
        }));
        this.Bind(BorderBrushProperty, this.GetResourceObservable("FxcBorderDefaultBrush"));
        BorderThickness = new Thickness(0, 0, 0, 1);
        this.Bind(MinHeightProperty, this.GetResourceObservable("FxcMetricInfoBarHeight"));

        var row = new DockPanel { Margin = new Thickness(8, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Top };
        foreach (var action in model.Actions)
        {
            var button = new Button { Content = action.Label };
            AutomationProperties.SetAutomationId(button, id + ".Action." + action.Name);
            AutomationProperties.SetName(button, action.Label);
            button.Click += async (_, _) => await action.ExecuteAsync();
            buttons.Children.Add(button);
        }

        if (model.CanDismiss)
        {
            var dismiss = new Button { Content = "×" };
            AutomationProperties.SetAutomationId(dismiss, id + ".Dismiss");
            AutomationProperties.SetName(dismiss, Strings.InfoBar_Dismiss);
            ToolTip.SetTip(dismiss, Strings.InfoBar_Dismiss);
            dismiss.Click += (_, _) => model.Dismiss();
            buttons.Children.Add(dismiss);
        }

        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Right);
        row.Children.Add(buttons);
        var text = new TextBlock { Text = model.Text, Classes = { "body" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(text, id + ".Text");
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("FxcTextPrimaryBrush"));
        row.Children.Add(text);
        return row;
    }
}
