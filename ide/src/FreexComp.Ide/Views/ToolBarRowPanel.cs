using Avalonia;
using Avalonia.Controls;

namespace FreexComp.Ide.Views;

/// <summary>
/// I-215: the row of a tool-window tool bar (UI-TW-008). The first child (the buttons) keeps its natural width at the
/// left; the optional second child (the filter box) is right-aligned at its preferred width and shrinks down to its
/// minimum width when space is short. The two never overlap; the row's desired width is buttons + minimum filter.
/// </summary>
public sealed class ToolBarRowPanel : Panel
{
    /// <summary>The filter's width when there is room (UI-TW-008: 120 … 240).</summary>
    public const double FilterPreferredWidth = 180;

    protected override Size MeasureOverride(Size availableSize)
    {
        var buttons = Children.Count > 0 ? Children[0] : null;
        var filter = Children.Count > 1 ? Children[1] : null;
        buttons?.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var buttonsWidth = buttons?.DesiredSize.Width ?? 0;
        var height = buttons?.DesiredSize.Height ?? 0;
        if (filter is null)
            return new Size(buttonsWidth, height);

        filter.Measure(new Size(FilterWidth(filter, availableSize.Width - buttonsWidth), availableSize.Height));
        return new Size(buttonsWidth + MinimumWidth(filter), Math.Max(height, filter.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var buttons = Children.Count > 0 ? Children[0] : null;
        var filter = Children.Count > 1 ? Children[1] : null;
        var buttonsWidth = buttons?.DesiredSize.Width ?? 0;
        buttons?.Arrange(new Rect(0, 0, buttonsWidth, finalSize.Height));
        if (filter is not null)
        {
            var width = FilterWidth(filter, finalSize.Width - buttonsWidth);
            filter.Arrange(new Rect(Math.Max(buttonsWidth, finalSize.Width - width), 0, width, finalSize.Height));
        }

        return finalSize;
    }

    /// <summary>The filter's slot including its margin: preferred width when it fits, else what is left, never below the minimum.</summary>
    private static double FilterWidth(Control filter, double room)
    {
        var margin = filter.Margin.Left + filter.Margin.Right;
        return Math.Max(MinimumWidth(filter), Math.Min(FilterPreferredWidth + margin, room));
    }

    private static double MinimumWidth(Control filter) => filter.MinWidth + filter.Margin.Left + filter.Margin.Right;
}
