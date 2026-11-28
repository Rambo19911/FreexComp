using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Tests.Editor;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subject of the inline rename (I-381).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// The main window at 800 × 500 and 1920 × 1040, light and dark, with an inline rename session on a local: just started,
    /// the primary occurrence selected (<c>editor-inline-rename-selected</c>), typed name, no problem
    /// (<c>editor-inline-rename</c>) and a reserved word (<c>editor-inline-rename-problem</c>). Rules (UI-ED-160,
    /// 161): every occurrence is drawn on <c>accent.subtle</c>; the primary occurrence has an outline in <c>border.focus</c>,
    /// with a problem in <c>status.error</c>; the status bar's message field shows the hint or the reason and is not covered
    /// (its text may end in an ellipsis at 800 wide: the field fills the gap the other fields leave, UI-WIN-045).
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-ED-160")]
    [Trait("Req", "UI-ED-161")]
    public async Task InlineRename()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        string[] subjects = ["editor-inline-rename-selected", "editor-inline-rename", "editor-inline-rename-problem"];
        foreach (var subject in subjects.Where(s => Directory.Exists(VisualSnapshot.FolderOf(s))))
            Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        Size[] sizes = [new(800, 500), new(1920, 1040)];
        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            var (h, editor, doc, probe, session) = await InlineRenameViewTests.StartAsync(o => o.Settings["appearance.theme"] = theme);
            await using var _ = h;
            await TrustDialogTests.Guarded(h, async () =>
            {
                var textView = editor.TextArea.TextView;
                void Capture(string subject, string outlineToken)
                {
                    foreach (var size in sizes)
                    {
                        VisualSnapshot.Arrange(h.Window, size, 1.0);
                        editor.ScrollTo(doc.Buffer.GetPosition(probe.RenameFields[0].Offset).Line, 1);
                        Dispatcher.UIThread.RunJobs();
                        VisualSnapshot.Arrange(h.Window, size, 1.0);
                        var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                        int[] Pixels(int offset, int length, Func<Rect, Rect> area) => [.. BackgroundGeometryBuilder.GetRectsForSegment(textView,
                            new TextSegment { StartOffset = offset, Length = length }).SelectMany(r => VisualSnapshot.PixelsOf(h.Window,
                                area(new Rect(textView.TranslatePoint(r.TopLeft, h.Window)!.Value, r.Size))))];

                        var outline = Token(h.Window, outlineToken);
                        var fill = Token(h.Window, "accent.subtle");
                        var (offset, length) = probe.RenameFields[0];
                        if (Pixels(offset, length, r => r.Inflate(0.5)).Count(p => Near(p, outline)) < 2 * length)
                            defects.Add((variant, $"the primary occurrence has no {outlineToken} outline"));
                        foreach (var (other, otherLength) in probe.RenameFields.Skip(1))
                        {
                            if (Pixels(other, otherLength, r => r.Deflate(1)).Count(p => Near(p, fill)) < 4)
                                defects.Add((variant, $"the occurrence at {other} is not drawn on accent.subtle"));
                            // Its left and right edges (the line above or below may have the primary's outline).
                            if (Pixels(other, otherLength, r => new Rect(r.X - 0.5, r.Y + 3, r.Width + 1, Math.Max(1, r.Height - 6))).Any(p => Near(p, outline)))
                                defects.Add((variant, $"the occurrence at {other} is outlined"));
                        }

                        var message = h.Find<Avalonia.Controls.Control>(AutomationIds.StatusBarField("Message"));
                        if (VisualSnapshot.HiddenPart(message, h.Window) is { } hidden)
                            defects.Add((variant, "cut: the status message — " + hidden));
                        defects.AddRange(VisualSnapshot.LayoutDefects(h.Window).Select(d => (variant, d)));
                        files.Add((subject, theme, size, VisualSnapshot.Capture(h.Window, subject, theme)));
                    }
                }

                // Just started: the primary is selected, its outline stays visible above the selection.
                Assert.Equal(5, editor.SelectionLength);
                Capture(subjects[0], "border.focus");

                h.Type("Sum");
                await h.WaitForAsync(() => session.Checking.IsCompleted && h.Services.CodeModel.Worker.IsIdle, "the name checked");
                h.Clock.Advance(InlineRenameSession.CheckDelay);
                await h.WaitForAsync(() => session.Checking.IsCompleted && h.Services.CodeModel.Worker.IsIdle, "the name checked");
                Assert.Equal(RenameFieldState.Editing, probe.RenameFieldState);
                Capture(subjects[1], "border.focus");

                h.Press("Backspace");
                h.Press("Backspace");
                h.Press("Backspace");
                h.Type("begin");
                await h.WaitForAsync(() => probe.RenameFieldState == RenameFieldState.Problem, "the problem shown");
                Assert.Equal("begin is a reserved word.", h.Status.Message);
                Capture(subjects[2], "status.error");

                h.Press("Escape");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(InlineRenameViewTests.Unit1, doc.Buffer.Text);
                await InlineRenameViewTests.SettleAsync(h, doc, session);
            });
        }

        AssertSnapshots(files, subjects, 4, defects);
    }
}
