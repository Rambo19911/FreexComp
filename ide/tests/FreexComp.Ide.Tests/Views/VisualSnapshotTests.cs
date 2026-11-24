using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// I-240: the visual check of a Phase 8 UI card — the real UI rendered with the Skia renderer in light and dark theme, at
/// scale 1 and 2 and in two window sizes, written under <c>artifacts/visual/</c> for inspection, plus the layout rules
/// that can be measured (<see cref="VisualSnapshot.LayoutDefects"/>). Each test runs its body in a child process that
/// renders with Skia (<see cref="SkiaProcess"/>).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class VisualSnapshotTests
{
    private static readonly string[] Themes = ["light", "dark"];
    private static readonly double[] Scales = [1.0, 2.0];

    /// <summary>
    /// Tools › Options, page Environment › General (dialogs.md §9.6) at its stated size 820 × 580 — which is also
    /// its minimum (UI-DLG-005) — and enlarged to 1100 × 760, in three states: the top of the page with the disabled Language field (I-230),
    /// the Build group with the jobs field and its hint (I-227), and the Build group with a value outside 0…256 so that
    /// the range message shows.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TGT-003")]
    [Trait("Req", "IDE-P8-002")]
    public async Task OptionsEnvironmentGeneral_RendersAllVariants_NoClippingOrOverlap()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        Size[] sizes = [new(820, 580), new(1100, 760)];
        (string Subject, string? Jobs, bool AtEnd)[] states =
        [
            ("options-environment-general", null, false),
            ("options-environment-general-build", null, true),
            ("options-environment-general-build-range", "257", true),
        ];
        foreach (var state in states)
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(state.Subject)))
                Directory.Delete(VisualSnapshot.FolderOf(state.Subject), recursive: true); // no file of an earlier run counts
        }

        var files = new List<(string Subject, string Theme, double Scale, Size Size, SnapshotFile File)>();
        var defects = new List<(string Variant, string Defect)>();
        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = theme);
            var running = h.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
            var dlg = await h.WaitForDialogAsync("Options");
            try
            {
                var jobs = dlg.Find<NumericUpDown>("BuildJobs");
                var scroll = jobs.FindAncestorOfType<ScrollViewer>()!;
                Assert.False(dlg.Find<ComboBox>("Language").IsEnabled);
                Assert.True(jobs.IsEnabled);

                foreach (var state in states)
                {
                    jobs.Text = state.Jobs ?? "0";
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(state.Jobs is null ? null : "Enter a number of jobs from 0 to 256 (0 = automatic).", dlg.ErrorText("BuildJobs"));

                    foreach (var scale in Scales)
                    {
                        foreach (var size in sizes)
                        {
                            VisualSnapshot.Arrange(dlg.Window, size, scale);
                            if (state.AtEnd)
                                scroll.ScrollToEnd();
                            else
                                scroll.ScrollToHome();
                            Dispatcher.UIThread.RunJobs();

                            var variant = $"{state.Subject} {VisualSnapshot.FileName(theme, scale, size)}";
                            Assert.Equal(size, dlg.Window.ClientSize);
                            Assert.Equal(scale, dlg.Window.RenderScaling);
                            Assert.True(InViewPort(scroll, dlg.Find<Control>(state.AtEnd ? "BuildJobs" : "Language")), variant + ": the subject is not in view");
                            if (state.AtEnd)
                                Assert.True(InViewPort(scroll, dlg.Find<TextBlock>("BuildJobs.Hint")), variant + ": the hint is not in view");
                            defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                            files.Add((state.Subject, theme, scale, size, VisualSnapshot.Capture(dlg.Window, state.Subject, theme)));
                        }
                    }
                }

            }
            finally
            {
                dlg.Click("Cancel"); // also after a failure, so that the failure is what the test reports
                await running;
            }
        }

        // 8 files per subject, each a real rendering of the expected pixel size (scale 2 = double).
        Assert.Equal(states.Length * 8, files.Count);
        foreach (var state in states)
        {
            Assert.Equal(8, Directory.GetFiles(VisualSnapshot.FolderOf(state.Subject), "*.png").Length);
        }

        foreach (var (_, _, scale, size, file) in files)
        {
            Assert.True(new FileInfo(file.Path).Length > 0, file.Path);
            var expected = ((int)(size.Width * scale), (int)(size.Height * scale));
            Assert.Equal(expected, (file.PixelWidth, file.PixelHeight));
            Assert.Equal(expected, VisualSnapshot.PngSize(file.Path));
            Assert.True(file.DistinctColors >= 16, $"{file.Path}: only {file.DistinctColors} colours — not a real rendering");
        }

        // The same variant is darker in the dark theme.
        foreach (var light in files.Where(f => f.Theme == "light"))
        {
            var dark = files.Single(f => f.Theme == "dark" && f.Subject == light.Subject && f.Scale == light.Scale && f.Size == light.Size);
            Assert.True(dark.File.MeanLuminance + 64 < light.File.MeanLuminance, $"{dark.File.Path}: {dark.File.MeanLuminance} against {light.File.MeanLuminance}");
        }

        Assert.True(defects.Count == 0, Report(defects));
    }

    /// <summary>
    /// I-241: every numeric field of the Options dialog shows the widest value it allows (the longer of the ends of its
    /// range) completely — on every page that has one, in both themes and at both scales; one PNG per page and variant
    /// under <c>options-&lt;page&gt;-widest-values</c>.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    public async Task OptionsNumericFields_ShowTheWidestAllowedValue()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        var size = new Size(820, 580);
        var defects = new List<(string Variant, string Defect)>();
        var checkedFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = theme);
            var running = h.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
            var dlg = await h.WaitForDialogAsync("Options");
            try
            {
                var model = (OptionsViewModel)dlg.Window.Model;
                foreach (var page in model.Pages.Where(p => p.Fields.Any(f => f.Kind == OptionFieldKind.Integer)))
                {
                    var subject = $"options-{page.Id.ToLowerInvariant().Replace('.', '-')}-widest-values";
                    if (theme == Themes[0] && Directory.Exists(VisualSnapshot.FolderOf(subject)))
                        Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
                    model.SelectRow(model.Rows.First(r => r.Page == page));
                    Dispatcher.UIThread.RunJobs();
                    NumericUpDown? first = null;
                    foreach (var field in page.Fields.Where(f => f.Kind == OptionFieldKind.Integer))
                    {
                        var widest = field.Key.Ranges.SelectMany(r => new[] { r.Minimum, r.Maximum })
                            .Select(v => ((int)v).ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .OrderByDescending(t => t.Length).First();
                        var numeric = dlg.Find<NumericUpDown>(field.Id);
                        numeric.Text = widest;
                        Dispatcher.UIThread.RunJobs();
                        Assert.Null(field.Error);
                        Assert.Equal(widest, numeric.Text);
                        first ??= numeric;
                        checkedFields.Add(field.Id);
                    }

                    foreach (var scale in Scales)
                    {
                        VisualSnapshot.Arrange(dlg.Window, size, scale);
                        first!.BringIntoView();
                        var variant = $"{subject} {VisualSnapshot.FileName(theme, scale, size)}";
                        defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                        var file = VisualSnapshot.Capture(dlg.Window, subject, theme);
                        Assert.Equal(((int)(size.Width * scale), (int)(size.Height * scale)), VisualSnapshot.PngSize(file.Path));
                    }
                }
            }
            finally
            {
                dlg.Click("Cancel");
                await running;
            }
        }

        // Every integer field of the dialog was filled, the two named in the bug among them.
        Assert.Superset(new HashSet<string> { "CompletionDelay", "BuildJobs", "TabWidth", "RightMargin", "RecoveryInterval", "GridSizeX" }, checkedFields);
        Assert.True(defects.Count == 0, Report(defects));
    }

    /// <summary>
    /// I-242: the labels of the footer buttons (OK, Cancel, Apply) are laid out completely inside their buttons, and the
    /// buttons inside the client area of the dialog, at both scales.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-003")]
    public async Task OptionsFooterButtons_LabelsAreNotCutOff()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        await using var h = await HeadlessHarness.StartAsync();
        var running = h.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
        var dlg = await h.WaitForDialogAsync("Options");
        try
        {
            foreach (var scale in Scales)
            {
                VisualSnapshot.Arrange(dlg.Window, new Size(820, 580), scale);
                foreach (var id in new[] { "Ok", "Cancel", "Apply" })
                {
                    var button = dlg.Find<Button>(id);
                    Assert.Equal(26, button.Bounds.Height); // UI-DLG-003
                    Assert.True(button.Bounds.Width >= 80, $"{id}: {button.Bounds.Width} wide");
                    var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
                    Assert.True(label.IsEffectivelyVisible);
                    Assert.Empty(VisualSnapshot.LayoutDefects(button));
                    var inWindow = new Rect(button.TranslatePoint(default, dlg.Window)!.Value, button.Bounds.Size);
                    Assert.True(new Rect(dlg.Window.ClientSize).Contains(inWindow), $"{id}: {inWindow} outside {dlg.Window.ClientSize}");
                }
            }
        }
        finally
        {
            dlg.Click("Cancel");
            await running;
        }
    }

    /// <summary>
    /// I-243: the browse button of <c>Default project folder</c> shows its "…" — laid out completely and drawn (more than
    /// one colour where the glyph is) in both themes — and has a tool tip and an accessible name.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    public async Task OptionsBrowseButton_ShowsItsGlyph()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = theme);
            var running = h.Services.Commands.ExecuteAsync("tools.options", "Environment.General");
            var dlg = await h.WaitForDialogAsync("Options");
            try
            {
                var browse = dlg.Find<Button>("DefaultProjectsDir.Browse");
                Assert.Equal("…", browse.Content);
                Assert.Equal("Browse", Avalonia.Automation.AutomationProperties.GetName(browse));
                Assert.Equal("Browse", ToolTip.GetTip(browse));
                foreach (var scale in Scales)
                {
                    VisualSnapshot.Arrange(dlg.Window, new Size(820, 580), scale);
                    browse.BringIntoView();
                    Assert.Empty(VisualSnapshot.LayoutDefects(browse));
                    var glyph = Assert.Single(browse.GetVisualDescendants().OfType<TextBlock>());
                    var ink = VisualSnapshot.DistinctColors(dlg.Window, glyph);
                    Assert.True(ink >= 2, $"{theme} {scale}x: the glyph area is one flat colour");
                }
            }
            finally
            {
                dlg.Click("Cancel");
                await running;
            }
        }
    }

    /// <summary>The control lies completely inside the view port of the scroll viewer.</summary>
    private static bool InViewPort(ScrollViewer scroll, Control control) =>
        control.IsEffectivelyVisible
        && control.TranslatePoint(default, scroll) is { } origin
        && new Rect(scroll.Bounds.Size).Contains(new Rect(origin, control.Bounds.Size));

    /// <summary>One line per distinct defect with the number of variants showing it and the first of them.</summary>
    private static string Report(List<(string Variant, string Defect)> defects) => string.Join(Environment.NewLine,
        defects.GroupBy(d => d.Defect).Select(g => $"{g.Key} ({g.Count()} variants, first {g.First().Variant})"));
}
