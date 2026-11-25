using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subjects of protected library documents (F02-004, I-232).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// The main window at 800 × 500 and 1920 × 1040, light and dark, with a library source open (UI-ED-144/145):
    /// <c>editor-library-source</c> — the protected document with the library bar above the text (a project is open: its
    /// actions are Copy to Project, then Edit Anyway — UI-ED-147) and the lock glyph on its tab; <c>editor-library-source-disk-read-only</c> — after Edit Anyway on a file that is read-only on disk
    /// (IDE-LIB-005): the read-only bar with Make Writable has taken its place, the lock glyph stays. Rules: the bar, its
    /// text and its buttons are drawn completely inside the window (nothing clipped, nothing overlapping), the bar is
    /// the first thing below the tab strip, and the glyph is drawn.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-ED-144")]
    [Trait("Req", "UI-ED-145")]
    [Trait("Req", "UI-ED-147")]
    [Trait("Req", "IDE-LIB-005")]
    public async Task LibrarySourceDocument_RendersBarAndLock_NoClippingOrOverlap()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        string[] subjects = ["editor-library-source", "editor-library-source-disk-read-only"];
        Size[] sizes = [new(800, 500), new(1920, 1040)];
        foreach (var subject in subjects)
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
                Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        }

        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await BuildScenario.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                o.InitialFiles[@"tc\rtl\SysUtils.pas"] = LibrarySourceViewTests.LibraryUnit;
            });
            var path = Path.Combine(h.TempDir, @"tc\rtl\SysUtils.pas");
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                var document = Assert.IsType<TextDocumentViewModel>((await h.Services.Documents.OpenAsync(path)).Value);
                Dispatcher.UIThread.RunJobs();
                foreach (var subject in subjects)
                {
                    var kind = subject == subjects[0] ? "LibrarySource" : "ReadOnly";
                    string[] parts = subject == subjects[0]
                        ? ["InfoBar.LibrarySource.Text", "InfoBar.LibrarySource.Action.CopyToProject", "InfoBar.LibrarySource.Action.EditAnyway"]
                        : ["InfoBar.ReadOnly.Text", "InfoBar.ReadOnly.Action.MakeWritable", "InfoBar.ReadOnly.Dismiss"];
                    if (subject == subjects[1])
                    {
                        await h.ExecuteAsync("file.editLibrarySource");
                        Dispatcher.UIThread.RunJobs();
                    }

                    foreach (var size in sizes)
                    {
                        VisualSnapshot.Arrange(h.Window, size, 1.0);
                        var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                        Assert.Equal(size, h.Window.ClientSize);
                        Assert.Equal(["InfoBar." + kind], LibrarySourceViewTests.BarsTopToBottom(h));
                        Assert.True(document.IsReadOnly);
                        var bar = h.Find<Control>("InfoBar." + kind);
                        var tab = h.Find<Control>(AutomationIds.DocumentTab("SysUtils.pas"));
                        var editor = h.Find<Control>("Editor.SysUtils.pas");
                        var glyph = LibrarySourceViewTests.LockOf(h, "SysUtils.pas");
                        if (subject == subjects[0])
                        {
                            // UI-ED-147: Copy to Project is left of Edit Anyway, both on the bar's button line.
                            var copy = h.Find<Control>(parts[1]).TranslatePoint(default, h.Window)!.Value;
                            var edit = h.Find<Control>(parts[2]).TranslatePoint(default, h.Window)!.Value;
                            if (copy.X + h.Find<Control>(parts[1]).Bounds.Width > edit.X || Math.Abs(copy.Y - edit.Y) > 0.5)
                                defects.Add((variant, $"button order: Copy to Project at {copy}, Edit Anyway at {edit}"));
                        }

                        Assert.True(glyph.IsEffectivelyVisible, $"{variant}: no lock glyph");
                        foreach (var control in parts.Select(h.Find<Control>).Append(bar).Append(glyph))
                        {
                            Assert.True(control.IsEffectivelyVisible, $"{variant}: {control} is not shown");
                            if (VisualSnapshot.HiddenPart(control, h.Window) is { } hidden)
                                defects.Add((variant, $"cut: {control} — {hidden}"));
                        }

                        // Directly below the tab strip, directly above the text.
                        var barTop = bar.TranslatePoint(default, h.Window)!.Value.Y;
                        var tabBottom = tab.TranslatePoint(new Point(0, tab.Bounds.Height), h.Window)!.Value.Y;
                        var editorTop = editor.TranslatePoint(default, h.Window)!.Value.Y;
                        if (barTop < tabBottom - 0.5 || editorTop < barTop + bar.Bounds.Height - 0.5)
                            defects.Add((variant, $"order: tab bottom {tabBottom}, bar {barTop}+{bar.Bounds.Height}, text {editorTop}"));
                        if (VisualSnapshot.DistinctColors(h.Window, glyph) < 2)
                            defects.Add((variant, "the lock glyph is not drawn"));
                        if (VisualSnapshot.DistinctColors(h.Window, bar) < 3)
                            defects.Add((variant, "the bar is not drawn"));
                        defects.AddRange(VisualSnapshot.LayoutDefects(bar).Select(d => (variant, d)));
                        defects.AddRange(VisualSnapshot.LayoutDefects(tab).Select(d => (variant, d)));
                        files.Add((subject, theme, size, VisualSnapshot.Capture(h.Window, subject, theme)));
                    }
                }
            }
            finally
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
        }

        AssertSnapshots(files, subjects, 4, defects);
    }
}
