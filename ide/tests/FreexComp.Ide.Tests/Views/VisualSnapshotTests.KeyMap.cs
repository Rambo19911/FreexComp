using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// I-253 (phase8-ui-additions.md D3): Tools › Options, page Key Mappings with the <c>Key map:</c> drop-down, at the
    /// dialog's stated size 820 × 580 and enlarged, with <c>Default</c> and with <c>Classic</c> chosen (the run commands
    /// listed, one of them selected), in both themes and at both scales. PNGs under <c>options-key-map-default</c> and
    /// <c>options-key-map-classic</c>.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    public async Task OptionsKeyMappings_KeyMapSelector_RendersBothPresets()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        Size[] sizes = [new(820, 580), new(1100, 760)];
        (string Subject, int Index, string StepOver)[] states =
        [
            ("options-key-map-default", 0, "F10"),
            ("options-key-map-classic", 1, "F8"),
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
            var running = h.Services.Commands.ExecuteAsync("tools.keyMappings");
            var dlg = await h.WaitForDialogAsync("Options");
            try
            {
                var combo = dlg.Find<ComboBox>("KeyMap");
                var label = combo.GetVisualParent()!.GetVisualChildren().OfType<Label>().Single();
                dlg.Find<TextBox>("KeyMappings.Filter").Text = "run.st";
                Dispatcher.UIThread.RunJobs();

                foreach (var state in states)
                {
                    combo.SelectedIndex = state.Index;
                    Dispatcher.UIThread.RunJobs();
                    dlg.Click("KeyMappings.Row.run.stepOver");

                    foreach (var scale in Scales)
                    {
                        foreach (var size in sizes)
                        {
                            VisualSnapshot.Arrange(dlg.Window, size, scale);
                            var variant = $"{state.Subject} {VisualSnapshot.FileName(theme, scale, size)}";
                            Assert.Equal(size, dlg.Window.ClientSize);
                            Assert.Equal(scale, dlg.Window.RenderScaling);
                            Assert.Equal(state.StepOver, dlg.Find<TextBlock>("KeyMappings.Current." + state.StepOver).Text);

                            // The drop-down and its label are whole, drawn, and the label's text is not cut.
                            Assert.Null(VisualSnapshot.HiddenPart(combo, dlg.Window));
                            Assert.Null(VisualSnapshot.HiddenPart(label, dlg.Window));
                            Assert.True(VisualSnapshot.DistinctColors(dlg.Window, combo) > 2, variant + ": the drop-down is not drawn");
                            Assert.True(VisualSnapshot.DistinctColors(dlg.Window, label) > 2, variant + ": the label is not drawn");
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

        Assert.Equal(states.Length * 8, files.Count);
        foreach (var (_, _, scale, size, file) in files)
        {
            var expected = ((int)(size.Width * scale), (int)(size.Height * scale));
            Assert.Equal(expected, VisualSnapshot.PngSize(file.Path));
            Assert.True(file.DistinctColors >= 16, $"{file.Path}: only {file.DistinctColors} colours — not a real rendering");
        }

        Assert.True(defects.Count == 0, Report(defects));
    }
}
