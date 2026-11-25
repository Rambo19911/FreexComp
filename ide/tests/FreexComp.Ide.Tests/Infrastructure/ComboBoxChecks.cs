using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>Layout checks of a combo box's selection box (I-282).</summary>
internal static class ComboBoxChecks
{
    /// <summary>Half a DIP: less than that is not a visibly cut glyph at any supported scale.</summary>
    private const double Tolerance = 0.5;

    /// <summary>Fails with <see cref="SelectedTextProblem"/> when there is one.</summary>
    public static void AssertSelectedTextNotClipped(ComboBox combo)
    {
        var problem = SelectedTextProblem(combo);
        Assert.True(problem is null, problem);
    }

    /// <summary>
    /// Null when the text of the selected item, as realised in the selection box, is fully visible: its text block is
    /// arranged at least as high as the text needs (measured without a height limit), and that height lies inside the
    /// combo box and inside every ancestor that clips. Otherwise what is wrong.
    /// </summary>
    public static string? SelectedTextProblem(ComboBox combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        var id = AutomationIds.Get(combo) is { Length: > 0 } automationId ? automationId : combo.GetType().Name;
        var texts = combo.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).ToList();
        if (texts.Count == 0)
            return $"{id}: the selection box shows no text";
        foreach (var text in texts)
        {
            // What the text needs when nothing limits it: the same text and font, measured on its own and without
            // rounding to the device pixels of another scale factor than the window's.
            var probe = new TextBlock
            {
                Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight,
                FontStyle = text.FontStyle, FontStretch = text.FontStretch, LineHeight = text.LineHeight, Padding = text.Padding,
                UseLayoutRounding = false,
            };
            probe.Measure(Size.Infinity);
            var needed = probe.DesiredSize.Height;
            if (text.Bounds.Height + Tolerance < needed)
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"{id}: '{text.Text}' needs {needed:0.##} high, its text block is arranged {text.Bounds.Height:0.##} high (combo box {combo.Bounds.Height:0.##} high, padding {combo.Padding})");
            }

            for (Visual? ancestor = text.GetVisualParent(); ancestor is not null; ancestor = ancestor.GetVisualParent())
            {
                if (ancestor != combo && !ancestor.ClipToBounds)
                    continue;
                var top = text.TranslatePoint(default, ancestor)!.Value.Y;
                if (top < -Tolerance || top + needed > ancestor.Bounds.Height + Tolerance)
                {
                    return string.Create(CultureInfo.InvariantCulture,
                        $"{id}: '{text.Text}' spans {top:0.##}…{top + needed:0.##} in a {ancestor.GetType().Name} {ancestor.Bounds.Height:0.##} high");
                }
            }
        }

        return null;
    }
}
