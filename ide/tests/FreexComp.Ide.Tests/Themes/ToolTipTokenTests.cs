using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Editor;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;
using static FreexComp.Ide.Tests.Editor.FoldingTests;

namespace FreexComp.Ide.Tests.Themes;

/// <summary>The tooltip tokens (I-246; design-tokens.md §8 Tooltip, UI-TOK-051, UI-TOK-052) apply to every tooltip of the product.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class ToolTipTokenTests
{
    /// <summary>Opens <paramref name="tip"/> on <paramref name="host"/> so that the application styles apply to it.</summary>
    private static void Open(Control host, ToolTip tip)
    {
        ToolTip.SetTip(host, tip);
        ToolTip.SetIsOpen(host, true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(tip.IsAttachedToVisualTree());
    }

    /// <summary>
    /// The tooltip parts of UI-TOK-051 (fade-in 80 ms) and UI-TOK-052 (open delay 600 ms) and the metrics of
    /// design-tokens.md §8. The two ids stay in <c>coverage-exempt.txt</c>: their other parts (progress animations, caret
    /// blink, hiding after 10 s) are timing of rendering.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    public async Task PaddingDelayFadeAndMaxWidthFromTokens()
    {
        await using var h = await HeadlessHarness.StartAsync();

        // UI-TOK-052: every control that has a tooltip opens it after 600 ms, without setting the delay itself; a
        // control that set its own delay keeps it.
        foreach (var control in (Control[])[new Button(), new TextBlock(), new Border()])
        {
            ToolTip.SetTip(control, "tip");
            Assert.Equal(600, ToolTip.GetShowDelay(control));
        }

        var own = new Button();
        ToolTip.SetShowDelay(own, 50);
        ToolTip.SetTip(own, "tip");
        Assert.Equal(50, ToolTip.GetShowDelay(own));
        var menu = h.Find<Menu>(AutomationIds.MenuBar);
        var withTips = h.Window.GetVisualDescendants().OfType<Control>().Where(c => ToolTip.GetTip(c) is not null).ToList();
        Assert.NotEmpty(withTips); // the toolbar buttons at least
        Assert.All(withTips, c => Assert.Equal(600, ToolTip.GetShowDelay(c)));

        // design-tokens.md §8: max width 480, padding 6 × 4 — from the tokens, for a text tip and for a control as the tip.
        var padding = Assert.IsType<Thickness>(Application.Current!.FindResource("FxcMetricTooltipPadding"));
        var maxWidth = Assert.IsType<double>(Application.Current!.FindResource("FxcMetricTooltipMaxWidth"));
        Assert.Equal((new Thickness(6, 4), 480.0), (padding, maxWidth));
        var text = new ToolTip { Content = string.Join(" ", Enumerable.Repeat("a tooltip text that is long enough to wrap", 6)) };
        Open(menu, text);
        Assert.Equal((padding, maxWidth), (text.Padding, text.MaxWidth));
        var shown = Assert.Single(text.GetVisualDescendants().OfType<ToolTipText>());
        Assert.True(text.Bounds.Width <= 480, $"the tooltip is {text.Bounds.Width} wide");
        // The text starts 6 px from the left and 4 px from the top of the tooltip (its border is part of the padding box).
        var origin = shown.TranslatePoint(default, text)!.Value;
        Assert.InRange(origin.X, 6, 7);
        Assert.InRange(origin.Y, 4, 5);
        ToolTip.SetIsOpen(menu, false);
        var panel = new ToolTip { Content = new StackPanel { Children = { new TextBlock { Text = "E2003" }, new TextBlock { Text = "Undeclared identifier" } } } };
        Open(menu, panel);
        Assert.Equal((padding, maxWidth), (panel.Padding, panel.MaxWidth));
        Assert.True(((Control)panel.Content!).IsAttachedToVisualTree(), "a control as the tip is shown itself");
        Assert.Equal(["E2003", "Undeclared identifier"], panel.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

        // A control given as the tip (the tooltip is made around it) is shown too — not its type name.
        ToolTip.SetIsOpen(menu, false);
        var rows = new StackPanel { Children = { new TextBlock { Text = "W1001" } } };
        ToolTip.SetTip(menu, rows);
        ToolTip.SetIsOpen(menu, true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(rows.IsAttachedToVisualTree(), "a control as the tip is shown itself");
        var around = rows.FindAncestorOfType<ToolTip>()!;
        Assert.Equal((padding, maxWidth), (around.Padding, around.MaxWidth));
        panel = around;

        // UI-TOK-051: the only animation of a tooltip is its fade-in of 80 ms.
        var fade = Assert.IsType<DoubleTransition>(Assert.Single(panel.Transitions!));
        Assert.Equal((Visual.OpacityProperty, TimeSpan.FromMilliseconds(80)), (fade.Property, fade.Duration));
        ToolTip.SetIsOpen(menu, false);
        ToolTip.SetTip(menu, null);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>UI-ED-035: the tooltip of a fold box keeps its own maximum width 640 (the other tokens apply to it too).</summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-035")]
    public async Task FoldTooltip_MaxWidth640()
    {
        var longLine = "  Writeln('" + new string('x', 3000) + "');\r\n";
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["L.pas"] = "program L;\r\nbegin\r\n" + longLine + "  Writeln;\r\nend.\r\n");
        var (doc, editor, probe) = await OpenAsync(h, "L.pas");
        var view = (CodeEditorView)probe;
        var region = doc.Folding.Regions.Single();
        doc.Folding.SetFolded(region, true);
        Dispatcher.UIThread.RunJobs();

        var tip = view.BuildFoldTooltip(region);
        Assert.Equal(640, tip.MaxWidth);
        var textView = editor.TextArea.TextView;
        ToolTip.SetTip(textView, tip);
        ToolTip.SetIsOpen(textView, true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(tip.IsAttachedToVisualTree());
        Assert.Equal(640, tip.MaxWidth); // not narrowed to the general 480 by the application style
        var dbg = (TextBlock)tip.Content!;
        Assert.Equal(640, tip.Bounds.Width); // a very long line uses all of it, not only the general 480
        Assert.True(((Control)tip.Content!).IsAttachedToVisualTree(), "the folded text is shown");
        Assert.Equal(Application.Current!.FindResource("FxcMetricTooltipPadding"), tip.Padding);
        var content = Assert.IsType<TextBlock>(tip.Content);
        Assert.Equal(Application.Current!.FindResource("FxcTypeCodeSmallSize"), content.FontSize); // type.codeSmall
        ToolTip.SetIsOpen(textView, false);
        ToolTip.SetTip(textView, null);
        Dispatcher.UIThread.RunJobs();
    }
}
