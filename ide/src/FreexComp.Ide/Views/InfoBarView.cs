using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;

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

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 4, LineSpacing = 4 };
        foreach (var action in model.Actions)
        {
            var button = new Button { Content = action.Label };
            AutomationProperties.SetAutomationId(button, id + ".Action." + action.Name);
            AutomationProperties.SetName(button, action.Label);
            button.Bind(IsVisibleProperty, new Binding(nameof(InfoBarAction.IsVisible)) { Source = action });
            button.Bind(IsEnabledProperty, new Binding(nameof(InfoBarAction.IsEnabled)) { Source = action });
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

        var icon = new IdeIcon
        {
            IconName = model.Severity switch
            {
                InfoBarSeverity.Warning => "warning",
                InfoBarSeverity.Error => "error",
                _ => "info",
            },
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0), // space.4 (designer.md UI-DSG-090)
        };
        var text = new TextBlock { Text = model.Text, Classes = { "body" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(text, id + ".Text");
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("FxcTextPrimaryBrush"));
        // UI-ED-148: in a document the buttons of a wrapped bar stay at the right edge.
        return new InfoBarRow(icon, text, buttons, stackedButtonsRight: AutomationIdBase == "InfoBar") { Margin = new Thickness(8, 4) };
    }

    /// <summary>
    /// The row of an info bar: icon, text, buttons. With room for the text (at least <see cref="MinTextWidth"/>) beside
    /// the buttons, the buttons sit at the right edge and the text fills the middle (the document layout). In a narrow
    /// host — a docked tool window (tool-windows.md §2: the Project Manager's bars) — the buttons go below the text,
    /// left-aligned with it, wrapping onto further lines, so that no button is cut off. In a document the buttons of the
    /// second line are right-aligned (UI-ED-148).
    /// </summary>
    private sealed class InfoBarRow : Panel
    {
        /// <summary>Below this text width the buttons move under the text.</summary>
        private const double MinTextWidth = 200;

        private const double Gap = 4;

        private readonly Control _icon;
        private readonly Control _text;
        private readonly Control _buttons;
        private readonly bool _stackedButtonsRight;
        private bool _stacked;

        public InfoBarRow(Control icon, Control text, Control buttons, bool stackedButtonsRight)
        {
            _icon = icon;
            _text = text;
            _buttons = buttons;
            _stackedButtonsRight = stackedButtonsRight;
            Children.AddRange([icon, text, buttons]);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _icon.Measure(Size.Infinity);
            _buttons.Measure(Size.Infinity);
            var icon = _icon.DesiredSize;
            var inline = _buttons.DesiredSize;
            _stacked = !double.IsInfinity(availableSize.Width) && inline.Width > 0
                && availableSize.Width - icon.Width - inline.Width - Gap < MinTextWidth;
            if (!_stacked)
            {
                _text.Measure(new Size(Math.Max(0, availableSize.Width - icon.Width - inline.Width - (inline.Width > 0 ? Gap : 0)), availableSize.Height));
                return new Size(
                    Math.Min(availableSize.Width, icon.Width + _text.DesiredSize.Width + inline.Width),
                    Math.Max(icon.Height, Math.Max(_text.DesiredSize.Height, inline.Height)));
            }

            var width = Math.Max(0, availableSize.Width - icon.Width);
            _text.Measure(new Size(width, availableSize.Height));
            _buttons.Measure(new Size(width, double.PositiveInfinity));
            return new Size(availableSize.Width, Math.Max(icon.Height, _text.DesiredSize.Height) + Gap + _buttons.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var icon = _icon.DesiredSize;
            if (!_stacked)
            {
                var buttons = _buttons.DesiredSize;
                _icon.Arrange(new Rect(0, (finalSize.Height - icon.Height) / 2, icon.Width, icon.Height));
                _buttons.Arrange(new Rect(finalSize.Width - buttons.Width, 0, buttons.Width, buttons.Height));
                _text.Arrange(new Rect(icon.Width, 0, Math.Max(0, finalSize.Width - icon.Width - buttons.Width - (buttons.Width > 0 ? Gap : 0)), finalSize.Height));
                return finalSize;
            }

            var textHeight = Math.Max(icon.Height, _text.DesiredSize.Height);
            var width = Math.Max(0, finalSize.Width - icon.Width);
            _icon.Arrange(new Rect(0, 0, icon.Width, icon.Height));
            _text.Arrange(new Rect(icon.Width, 0, width, textHeight));
            var buttonsWidth = _stackedButtonsRight ? Math.Min(width, _buttons.DesiredSize.Width) : width;
            _buttons.Arrange(new Rect(finalSize.Width - buttonsWidth, textHeight + Gap, buttonsWidth, _buttons.DesiredSize.Height));
            return finalSize;
        }
    }
}
