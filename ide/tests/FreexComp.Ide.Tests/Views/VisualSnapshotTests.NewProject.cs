using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subject of the New Project dialog (I-256).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// File › New › Project (dialogs.md §3) at its stated size 640 × 400 — also its minimum — and enlarged to 900 × 560,
    /// light and dark. PNGs under <c>new-project-dialog</c>. Rules: the <c>…</c> button beside Location is 26 × 26 and
    /// draws its glyph (more than one colour where the glyph is); the path follows <c>Will be created in:</c> after a
    /// gap, in the column of the fields; the layout rules hold for the whole dialog.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-011")]
    [Trait("Req", "UI-DLG-030")]
    public async Task NewProjectDialog_RendersGlyphAndTargetPath()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const string subject = "new-project-dialog";
        Size[] sizes = [new(640, 400), new(900, 560)];
        if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
            Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);

        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        var defects = new List<(string Variant, string Defect)>();
        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = theme);
            h.Services.Settings.Set(SettingsCatalog.EnvironmentDefaultProjectsDir, h.TempDir);
            var running = h.Services.Commands.ExecuteAsync("file.newProject");
            var dlg = await h.WaitForDialogAsync("NewProject");
            try
            {
                await h.WaitForAsync(() => !((Core.Dialogs.NewProjectViewModel)dlg.Window.Model).IsChecking, "New Project check done");
                var browse = dlg.Find<Button>("Location.Browse");
                var target = dlg.Find<TextBlock>("TargetPath");
                var label = Assert.Single(dlg.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Will be created in:");
                foreach (var size in sizes)
                {
                    VisualSnapshot.Arrange(dlg.Window, size, 1.0);
                    Dispatcher.UIThread.RunJobs();
                    var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                    Assert.Equal(size, dlg.Window.ClientSize);

                    Assert.Equal(new Size(26, 26), browse.Bounds.Size);
                    var glyph = Assert.Single(browse.GetVisualDescendants().OfType<TextBlock>());
                    Assert.True(VisualSnapshot.DistinctColors(dlg.Window, glyph) >= 2, variant + ": the glyph area of the browse button is one flat colour");

                    var labelRight = label.TranslatePoint(new Point(label.Bounds.Width, 0), dlg.Window)!.Value.X;
                    var targetLeft = target.TranslatePoint(default, dlg.Window)!.Value.X;
                    Assert.True(targetLeft - labelRight >= 16, $"{variant}: the path starts {targetLeft - labelRight} after its label");

                    defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                    files.Add((subject, theme, size, VisualSnapshot.Capture(dlg.Window, subject, theme)));
                }
            }
            finally
            {
                dlg.Click("Cancel"); // also after a failure, so that the failure is what the test reports
                await running;
            }
        }

        AssertSnapshots(files, [subject], 4, defects);
    }
}
