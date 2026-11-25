using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subject of the Collection Editor (dialogs.md §25; I-289).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// I-289 (found by I-286 in a render): the dialog <c>CollectionEditor</c> at its default size 640 × 400, light and dark,
    /// at scale 1, 1.5 and 2. Rules: every button — Add, Delete, Move Up, Move Down, OK, Cancel — is drawn with its full
    /// text, none cut by the column it sits in or lying under the property grid; no other layout defect. PNGs under
    /// <c>collection-editor</c>.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-241")]
    [Trait("Req", "UI-TOK-062")]
    public async Task CollectionEditor_EveryButtonShowsItsFullText()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const string subject = "collection-editor";
        if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
            Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        var size = new Size(640, 400);
        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Theme, double Scale, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await CollectionEditorViewTests.StartAsync(theme);
            var d = await DesignerViewTests.OpenFormAsync(h);
            h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
            CollectionEditorViewTests.OpenPanels(h, d);
            var dlg = await h.WaitForDialogAsync("CollectionEditor");
            try
            {
                Assert.Equal((640, 400), (dlg.Window.Width, dlg.Window.Height)); // its default size (dialogs.md §25)
                foreach (var scale in (double[])[1.0, 1.5, 2.0])
                {
                    VisualSnapshot.Arrange(dlg.Window, size, scale);
                    var variant = $"{subject} {VisualSnapshot.FileName(theme, scale, size)}";
                    var grid = dlg.Find<Control>("Grid");
                    var gridBox = new Rect(grid.TranslatePoint(default, dlg.Window)!.Value, grid.Bounds.Size);
                    foreach (var field in (string[])["Add", "Delete", "MoveUp", "MoveDown", "Ok", "Cancel"])
                    {
                        var button = dlg.Find<Button>(field);
                        Assert.True(button.IsEffectivelyVisible, $"{variant}: {field} is not shown");
                        if (VisualSnapshot.HiddenPart(button, dlg.Window) is { } hidden)
                            defects.Add((variant, $"cut: {field} — {hidden}"));
                        var box = new Rect(button.TranslatePoint(default, dlg.Window)!.Value, button.Bounds.Size);
                        if (box.Intersect(gridBox) is { Width: > 0.5, Height: > 0.5 })
                            defects.Add((variant, $"{field} {box} lies under the property grid {gridBox}"));
                        var texts = button.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
                        if (texts.Count == 0)
                            defects.Add((variant, $"{field} shows no text"));
                        foreach (var text in texts)
                        {
                            if (VisualSnapshot.HiddenPart(text, dlg.Window) is { } cut)
                                defects.Add((variant, $"cut: the text of {field} — {cut}"));
                        }
                    }

                    defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(defect => (variant, defect)));
                    files.Add((theme, scale, VisualSnapshot.Capture(dlg.Window, subject, theme)));
                }
            }
            finally
            {
                dlg.Press("Escape");
                Dispatcher.UIThread.RunJobs();
            }
        }

        Assert.Equal(6, Directory.GetFiles(VisualSnapshot.FolderOf(subject), "*.png").Length); // 2 themes × 3 scales
        foreach (var (_, scale, file) in files)
        {
            Assert.Equal(((int)(size.Width * scale), (int)(size.Height * scale)), VisualSnapshot.PngSize(file.Path));
            Assert.True(file.DistinctColors >= 16, $"{file.Path}: only {file.DistinctColors} colours — not a real rendering");
        }

        foreach (var light in files.Where(f => f.Theme == "light"))
        {
            var dark = files.Single(f => f.Theme == "dark" && f.Scale == light.Scale);
            Assert.True(dark.File.MeanLuminance + 64 < light.File.MeanLuminance, $"{dark.File.Path}: {dark.File.MeanLuminance} against {light.File.MeanLuminance}");
        }

        Assert.True(defects.Count == 0, Report(defects));
    }
}
