using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Themes;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// I-215 (owner visual report): tool-window filter boxes fit their text, tool bars never overlap their filter at the
/// narrowest dock, and checked toggle glyphs keep the non-text contrast of 3:1 (UI-A11Y). Every registered tool window,
/// light and dark theme.
/// </summary>
[Trait("Phase", "5")]
[Trait("Kind", "Ui")]
public sealed class ToolWindowVisualTests
{
    private static readonly string[] Themes = ["light", "dark"];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-008")]
    public async Task FilterBoxesDoNotClipText()
    {
        var problems = new List<string>();
        var checkedBoxes = 0;
        foreach (var theme in Themes)
        {
            await using var h = await StartAsync(theme);
            foreach (var (name, frame) in ShowAll(h))
            {
                foreach (var box in frame.GetVisualDescendants().OfType<TextBox>().Where(b => AutomationProperties.GetAutomationId(b)?.EndsWith(".Filter", StringComparison.Ordinal) == true))
                {
                    checkedBoxes++;
                    var placeholder = box.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == box.PlaceholderText && t.IsVisible);
                    var presenter = box.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
                    if (placeholder is null || presenter is null)
                    {
                        problems.Add($"{theme} {name}: {AutomationProperties.GetAutomationId(box)} has no visible placeholder / content presenter");
                        continue;
                    }

                    // The line the placeholder needs, unconstrained, against the clip of the box's content presenter.
                    placeholder.Measure(Size.Infinity);
                    var line = placeholder.DesiredSize.Height;
                    var top = placeholder.TranslatePoint(default, box)!.Value.Y;
                    var clip = new Rect(presenter.TranslatePoint(default, box)!.Value, presenter.Bounds.Size);
                    var inner = new Rect(box.Bounds.Size).Deflate(box.BorderThickness);
                    if (top < clip.Top - 0.5 || top + line > clip.Bottom + 0.5 || clip.Top < inner.Top - 0.5 || clip.Bottom > inner.Bottom + 0.5)
                    {
                        problems.Add(string.Create(CultureInfo.InvariantCulture,
                            $"{theme} {name}: {AutomationProperties.GetAutomationId(box)} line {top:0.#}…{top + line:0.#} outside clip {clip.Top:0.#}…{clip.Bottom:0.#} (box inner {inner.Top:0.#}…{inner.Bottom:0.#})"));
                    }
                }
            }
        }

        Assert.True(checkedBoxes >= 2 * 5, $"only {checkedBoxes} filter boxes found");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-008")]
    [Trait("Req", "UI-WIN-005")]
    public async Task ToolbarNeverOverlapsAtMinimumWidth()
    {
        var problems = new List<string>();
        var toolbars = 0;
        foreach (var theme in Themes)
        {
            await using var h = await StartAsync(theme);
            var layout = h.Services.Layout.Layout;
            layout.LeftSize = 1; // the narrowest the docks can be: their minimum
            layout.RightSize = 1;
            foreach (var (name, frame) in ShowAll(h))
            {
                var prefix = "Tool." + name + ".";
                var buttons = frame.GetVisualDescendants().OfType<Control>()
                    .Where(c => c.IsEffectivelyVisible && AutomationProperties.GetAutomationId(c) is { } id && id.StartsWith(prefix + "ToolBar.", StringComparison.Ordinal)
                        && c is Button or ComboBox).ToList();
                var filter = frame.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == prefix + "Filter");
                if (buttons.Count == 0 || filter is null)
                    continue;
                toolbars++;
                var frameRect = new Rect(frame.Bounds.Size);
                var filterRect = RectIn(filter, frame);
                foreach (var button in buttons)
                {
                    var r = RectIn(button, frame);
                    if (r.Right > filterRect.Left + 0.5)
                        problems.Add(string.Create(CultureInfo.InvariantCulture, $"{theme} {name}: {AutomationProperties.GetAutomationId(button)} ends at {r.Right:0.#}, filter starts at {filterRect.Left:0.#} (frame {frameRect.Width:0.#})"));
                }

                if (filterRect.Right > frameRect.Right + 0.5 || filterRect.Width < Core.ViewModels.ToolWindowViewModel.FilterMinWidth - 0.5)
                    problems.Add(string.Create(CultureInfo.InvariantCulture, $"{theme} {name}: filter {filterRect.Left:0.#}…{filterRect.Right:0.#} in a frame {frameRect.Width:0.#} wide"));
            }
        }

        Assert.True(toolbars >= 2 * 3, $"only {toolbars} tool bars with a filter found");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-008")]
    [Trait("Req", "UI-A11Y-040")]
    public async Task ToggleGlyphContrastAtLeast3To1()
    {
        var problems = new List<string>();
        var glyphs = 0;
        foreach (var theme in Themes)
        {
            await using var h = await StartAsync(theme);
            foreach (var (name, frame) in ShowAll(h))
            {
                var toggles = frame.GetVisualDescendants().OfType<ToggleButton>().Where(t => t.IsEffectivelyVisible).ToList();
                foreach (var toggle in toggles)
                    toggle.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                foreach (var toggle in toggles)
                {
                    var presenter = toggle.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
                    var panel = Solid(frame, "FxcBgPanelBrush");
                    var background = Over(presenter.Background is ISolidColorBrush b ? b.Color : Colors.Transparent, panel);
                    foreach (var (what, color) in GlyphColors(toggle, presenter))
                    {
                        glyphs++;
                        var ratio = ColorContrast.Ratio(Hex(Over(color, background)), Hex(background));
                        if (ratio < 3)
                            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{theme} {name}: {AutomationProperties.GetAutomationId(toggle)} {what} {Hex(color)} on {Hex(background)} = {ratio:0.00}:1"));
                    }
                }
            }
        }

        Assert.True(glyphs >= 2 * 3, $"only {glyphs} toggle glyphs found");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static Task<HeadlessHarness> StartAsync(string theme) => HeadlessHarness.StartAsync(o =>
    {
        o.Settings["appearance.theme"] = theme;
        o.WindowSize = new Size(1024, 700);
    });

    /// <summary>Shows every registered tool window in turn and yields its frame, laid out.</summary>
    private static IEnumerable<(string Name, Control Frame)> ShowAll(HeadlessHarness h)
    {
        foreach (var definition in h.Services.ToolWindows.Registered.ToList())
        {
            Assert.True(h.Services.ToolWindows.Show(definition.Name, focus: false), "cannot show " + definition.Name);
            Dispatcher.UIThread.RunJobs();
            h.Window.UpdateLayout();
            var frame = h.TryFind<Control>("Tool." + definition.Name);
            if (frame is { IsEffectivelyVisible: true })
                yield return (definition.Name, frame);
        }
    }

    /// <summary>The colours a toggle draws its glyph in: the icon's layer tokens, or the presenter's foreground for text.</summary>
    private static IEnumerable<(string What, Color Color)> GlyphColors(ToggleButton toggle, ContentPresenter presenter)
    {
        if (toggle.Content is IdeIcon { IconName: { } icon })
        {
            foreach (var token in IconCatalog.Get(icon).Layers.Select(l => l.ColorToken).Distinct(StringComparer.Ordinal))
                yield return ("icon " + icon + " " + token, Solid(toggle, IconCatalog.BrushKey(token)));
        }
        else
        {
            yield return ("text", presenter.Foreground is ISolidColorBrush f ? f.Color : Colors.Transparent);
        }
    }

    private static Color Solid(Control scope, string key)
    {
        Assert.True(scope.TryFindResource(key, scope.ActualThemeVariant, out var value), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    /// <summary><paramref name="top"/> composited over the opaque <paramref name="bottom"/>.</summary>
    private static Color Over(Color top, Color bottom)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte b) => (byte)Math.Round((t * a) + (b * (1 - a)));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static string Hex(Color c) => string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");

    private static Rect RectIn(Visual control, Visual frame) => new(control.TranslatePoint(default, frame)!.Value, control.Bounds.Size);
}
