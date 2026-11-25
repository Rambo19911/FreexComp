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

    /// <summary>
    /// I-219 (lead visual check, 1456 × 819): in every tool window, both themes, with a form open in its Design view, no
    /// interactive control is cut by an ancestor or squeezed below its content — every control lies inside each ancestor
    /// up to its tool-window frame (content scrolled out of a viewport is skipped), and the parts of a button, check box,
    /// column header or palette item lie inside the control; at the default and at the minimum side-dock widths.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-001")]
    [Trait("Req", "UI-DSG-102")]
    [Trait("Req", "UI-DSG-106")]
    public async Task NoInteractiveControlIsClipped()
    {
        var problems = new List<string>();
        var controls = 0;
        var windows = new HashSet<string>(StringComparer.Ordinal);
        foreach (var theme in Themes)
        {
            await using var h = await StartDesignAsync(theme);
            foreach (var narrow in (bool[])[false, true])
            {
                if (narrow)
                {
                    // The side docks at their minimum width (UI-WIN-005; never narrower than their tool bars and
                    // content minimums, I-215, I-219).
                    h.Services.Layout.Layout.LeftSize = 1;
                    h.Services.Layout.Layout.RightSize = 1;
                }

                foreach (var (name, frame) in ShowAll(h))
                {
                    windows.Add(name);
                    foreach (var control in Interactive(frame))
                    {
                        controls++;
                        problems.AddRange(ClipProblems(control, frame).Select(p => $"{theme}{(narrow ? " narrow" : "")} {name}: {Id(control)} {p}"));
                    }
                }
            }
        }

        Assert.Contains("ObjectInspector", windows);
        Assert.Contains("ToolPalette", windows);
        Assert.Contains("Messages", windows);
        Assert.True(controls >= 2 * 40, $"only {controls} interactive controls found");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// I-219: every tool-bar button and toggle of every tool window (both themes, Design view open) shows its glyph: an
    /// icon at its full size, or text whose natural size fits the space the button gives it.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-001")]
    [Trait("Req", "UI-DSG-102")]
    public async Task ToolbarGlyphsAreVisible()
    {
        var problems = new List<string>();
        var glyphs = 0;
        foreach (var theme in Themes)
        {
            await using var h = await StartDesignAsync(theme);
            foreach (var (name, frame) in ShowAll(h))
            {
                foreach (var button in frame.GetVisualDescendants().OfType<Button>()
                             .Where(b => b.IsEffectivelyVisible && Id(b).Contains(".ToolBar.", StringComparison.Ordinal)))
                {
                    glyphs++;
                    if (GlyphProblem(button) is { } problem)
                        problems.Add($"{theme} {name}: {Id(button)} {problem}");
                }
            }
        }

        Assert.True(glyphs >= 2 * 8, $"only {glyphs} tool-bar glyphs found");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// I-282 (owner visual report): every combo box of a tool window — the channel of Output, the instance selector of
    /// the Object Inspector, and any other one a tool window shows — displays its selected item at the full height of
    /// the text, in both themes and at every scale factor (UI-TOK-062).
    /// </summary>
    [AvaloniaTheory(Timeout = 30000)]
    [Trait("Req", "UI-TW-008")]
    [Trait("Req", "UI-TOK-062")]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task ToolWindowHeaderCombos_SelectedTextIsNotClipped(double scale)
    {
        var problems = new List<string>();
        var combos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var theme in Themes)
        {
            await using var h = await StartDesignAsync(theme, scale);
            Assert.Equal(scale, h.Window.RenderScaling);
            foreach (var (name, frame) in ShowAll(h))
            {
                foreach (var combo in frame.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsEffectivelyVisible))
                {
                    combos.Add(Id(combo));
                    if (ComboBoxChecks.SelectedTextProblem(combo) is { } problem)
                        problems.Add($"{theme} {name}: {problem}");
                }
            }
        }

        Assert.Contains("Tool.Output.Channel", combos);
        Assert.Contains("Tool.ObjectInspector.InstanceSelector", combos);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems) + Environment.NewLine + "combo boxes: " + string.Join(", ", combos.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// I-285 (found by I-282), design-tokens.md §8: the combo boxes of the Build tool bar are 24 high (tool-bar combo
    /// box) and the instance selector of the Object Inspector is 26 high (combo box) — the height set, not the 32 of the
    /// theme's minimum — and each shows its selected item at the full height of the text, in both themes and at every
    /// scale factor (UI-TOK-062). The tool-bar combos stay inside the 32-high tool bar.
    /// </summary>
    [AvaloniaTheory(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TOK-062")]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task ToolBarCombos_HaveTheSpecifiedHeight_TextNotClipped(double scale)
    {
        var problems = new List<string>();
        void Check(string theme, ComboBox combo, double height)
        {
            if (combo.SelectedItem is null)
                problems.Add($"{theme}: {Id(combo)} has no selected item");
            else if (ComboBoxChecks.SelectedTextProblem(combo) is { } problem)
                problems.Add($"{theme}: {problem}");
            if (Math.Abs(combo.Bounds.Height - height) > 0.01)
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{theme}: {Id(combo)} is {combo.Bounds.Height:0.##} high, specified {height:0.##}"));
        }

        foreach (var theme in Themes)
        {
            await using (var h = await BuildScenario.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                o.Scale = scale;
            }, projectExtra: """, "platforms": ["win64", "win32"]"""))
            {
                Assert.Equal(scale, h.Window.RenderScaling);
                var (comboHeight, barHeight) = (Assert.IsType<double>(h.Window.FindResource("FxcMetricToolbarComboHeight")), Assert.IsType<double>(h.Window.FindResource("FxcMetricToolbarHeight")));
                Assert.Equal((24.0, 32.0), (comboHeight, barHeight));
                foreach (var id in (string[])["ToolBar.Build.Configuration", "ToolBar.Build.Platform"])
                {
                    var combo = h.Find<ComboBox>(id);
                    Check(theme, combo, comboHeight);
                    var bar = h.Find<Control>("ToolBar.Build");
                    var top = combo.TranslatePoint(default, bar)!.Value.Y;
                    if (top < -0.01 || top + combo.Bounds.Height > bar.Bounds.Height + 0.01 || bar.Bounds.Height > barHeight + 0.01)
                        problems.Add(string.Create(CultureInfo.InvariantCulture, $"{theme}: {id} spans {top:0.##}…{top + combo.Bounds.Height:0.##} in a tool bar {bar.Bounds.Height:0.##} high"));
                }
            }

            await using (var h = await StartDesignAsync(theme, scale))
            {
                Assert.True(h.Services.ToolWindows.Show("ObjectInspector", focus: false));
                Dispatcher.UIThread.RunJobs();
                h.Window.UpdateLayout();
                var inputHeight = Assert.IsType<double>(h.Window.FindResource("FxcMetricInputHeight"));
                Assert.Equal(26.0, inputHeight);
                Check(theme, h.Find<ComboBox>("Tool.ObjectInspector.InstanceSelector"), inputHeight);
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static string Id(Control control) => AutomationProperties.GetAutomationId(control) ?? control.GetType().Name;

    /// <summary>
    /// The controls a user operates: buttons, toggles, check boxes, combo and text boxes, column headers, palette items —
    /// not the parts of the toolkit's scroll bars (their page buttons shrink to nothing by design).
    /// </summary>
    private static IEnumerable<Control> Interactive(Control frame) =>
        frame.GetVisualDescendants().OfType<Control>()
            .Where(c => c.IsEffectivelyVisible && (c is Button or ComboBox or TextBox || IsHeaderOrItem(Id(c))))
            .Where(c => !c.GetVisualAncestors().OfType<ScrollBar>().Any())
            .Where(c => !c.GetVisualAncestors().OfType<Control>().TakeWhile(a => a != frame).Any(a => a is Button or ComboBox or TextBox));

    private static bool IsHeaderOrItem(string id) =>
        (id.Contains(".Column.", StringComparison.Ordinal) && !id.Contains(".Row.", StringComparison.Ordinal) && !id.EndsWith(".Divider", StringComparison.Ordinal))
        || id.StartsWith("Tool.ToolPalette.Item.", StringComparison.Ordinal);

    private static IEnumerable<string> ClipProblems(Control control, Control frame)
    {
        var rect = RectIn(control, frame);
        if (control.Bounds.Width < 1 || control.Bounds.Height < 1)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"is squeezed to {control.Bounds.Width:0.#} × {control.Bounds.Height:0.#}");
            yield break;
        }

        var ancestors = control.GetVisualAncestors().TakeWhile(a => a != frame).Append(frame).ToList();
        if (ancestors.OfType<ScrollContentPresenter>().Any(p => !Contains(RectIn(p, frame), rect)))
            yield break; // scrolled (partly) out of its viewport: not a layout clip

        foreach (var ancestor in ancestors)
        {
            var outer = RectIn(ancestor, frame);
            if (!Contains(outer, rect))
            {
                yield return $"{Describe(rect)} is cut by {(ancestor is Control c ? Id(c) : ancestor.GetType().Name)} {Describe(outer)}";
                yield break;
            }
        }

        if (control is Button || IsHeaderOrItem(Id(control)))
        {
            foreach (var part in control.GetVisualDescendants().OfType<Control>().Where(d => d.IsEffectivelyVisible && d.Bounds.Width > 0 && d.Bounds.Height > 0))
            {
                var inner = RectIn(part, frame);
                if (!Contains(rect, inner))
                {
                    yield return $"part {part.GetType().Name} '{part.Name}' {Describe(inner)} sticks out of {Describe(rect)}";
                    yield break;
                }
            }
        }
    }

    /// <summary>Null when the button's glyph (icon or text) is shown whole; else what is wrong.</summary>
    private static string? GlyphProblem(Button button)
    {
        var bounds = new Rect(button.Bounds.Size);
        if (button.GetVisualDescendants().OfType<IdeIcon>().FirstOrDefault(i => i.IsEffectivelyVisible) is { } icon)
        {
            var r = RectIn(icon, button);
            return icon.Bounds.Width < 12 || icon.Bounds.Height < 12 || !Contains(bounds, r) ? $"icon {Describe(r)} in a button {Describe(bounds)}" : null;
        }

        if (button.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)) is not { } text)
            return "has no glyph";
        var arranged = RectIn(text, button);
        text.Measure(Size.Infinity);
        var natural = text.DesiredSize;
        text.InvalidateMeasure();
        return natural.Width > arranged.Width + 0.5 || natural.Height > arranged.Height + 0.5 || !Contains(bounds, arranged)
            ? string.Create(CultureInfo.InvariantCulture, $"text '{text.Text}' needs {natural.Width:0.#} × {natural.Height:0.#}, gets {Describe(arranged)} in {Describe(bounds)}")
            : null;
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 && inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static string Describe(Rect r) => string.Create(CultureInfo.InvariantCulture, $"[{r.Left:0.#},{r.Top:0.#} {r.Width:0.#}×{r.Height:0.#}]");

    /// <summary>The lead's window size, Phase 5 files, the form open in its Design view with an edit box selected (Boolean rows).</summary>
    private static async Task<HeadlessHarness> StartDesignAsync(string theme, double scale = 1)
    {
        var h = await HeadlessHarness.StartAsync(o =>
        {
            ToolPaletteViewTests.Phase5(o);
            o.Settings["appearance.theme"] = theme;
            o.WindowSize = new Size(1456, 819);
            o.Scale = scale;
        });
        var document = await ToolPaletteViewTests.OpenDesignAsync(h);
        document.Designer.Surface!.Selection.Select(document.Designer.Model!.FindByName("NameEdit")!);
        Dispatcher.UIThread.RunJobs();
        h.Window.UpdateLayout();
        return h;
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
