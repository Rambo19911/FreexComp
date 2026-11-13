using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class HighDpiTests
{
    [AvaloniaTheory(Timeout = 30000)]
    [Trait("Req", "UI-TOK-062")]
    [Trait("Req", "UI-TOK-061")]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task MainWindowLaysOutAtScales(double scale)
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.Scale = scale);

        Assert.Equal(scale, h.Window.RenderScaling);
        Assert.True(h.Window.UseLayoutRounding);
        AssertNoClippedText(h.Window);
    }

    [AvaloniaTheory(Timeout = 30000)]
    [Trait("Req", "UI-WIN-002")]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public async Task MinimumWindowKeepsRegionMinimums(double scale)
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.Scale = scale;
            o.WindowSize = new Size(800, 500);
        });

        Assert.Equal(800, h.Window.MinWidth); // main-window.md §7
        Assert.Equal(500, h.Window.MinHeight);
        var area = h.Find<Control>(AutomationIds.DocumentArea);
        Assert.True(area.Bounds.Width >= 320 && area.Bounds.Height >= 160, $"document area {area.Bounds.Size}");
        Assert.Equal(24, h.Find<Control>(AutomationIds.StatusBar).Bounds.Height);
        Assert.Equal(28, h.Find<Control>(AutomationIds.MenuBar).Bounds.Height);
        AssertNoClippedText(h.Window);
    }

    private static void AssertNoClippedText(Window window)
    {
        var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(texts);
        foreach (var t in texts.Where(t => t.TextTrimming == TextTrimming.None))
        {
            var width = t.DesiredSize.Width - t.Margin.Left - t.Margin.Right;
            Assert.True(width <= t.Bounds.Width + 0.01, $"'{t.Text}' clipped: desired {width}, bounds {t.Bounds.Width}");
        }
    }
}
