using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subject of the Rename Symbol dialog (I-380).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// The Rename Symbol dialog (UI-DLG-380…386) at 520 × its content height, light and dark, in three states: initial (the
    /// current name, Rename disabled), a valid name with its summary, and a conflict list of seven rows (at most five rows
    /// high, scrolled). PNGs under <c>rename-symbol-dialog-initial</c>, <c>-summary</c>, <c>-conflicts</c>. Rules: every part of
    /// the state is shown and not cut, the list is no higher than five rows, the layout rules hold.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-380")]
    [Trait("Req", "UI-DLG-384")]
    public async Task RenameSymbolDialog()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const string Subject = "rename-symbol-dialog";
        string[] subjects = [Subject + "-initial", Subject + "-summary", Subject + "-conflicts"];
        foreach (var subject in subjects.Where(s => Directory.Exists(VisualSnapshot.FolderOf(s))))
            Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);

        // Seven uses of the constant Prefix in TCustomer.GetCity: renaming GetName to Prefix gives eight conflicts.
        var unit1 = RenameDialogTests.Unit1.Replace("Result := Prefix + GetName;",
            "Result := Prefix + Prefix + Prefix + Prefix + Prefix + Prefix + Prefix + GetName;", StringComparison.Ordinal);
        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            var (h, editor, doc) = await RenameDialogTests.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                o.InitialFiles[@"Hello\Unit1.pas"] = unit1;
            });
            await using var _ = h;
            await TrustDialogTests.Guarded(h, async () =>
            {
                RenameDialogTests.CaretOn(editor, doc, "GetName");
                var (dlg, model) = await RenameDialogTests.F2Async(h);
                const string Prefix = "Dialog.RenameSymbol.";

                void Capture(string subject, string[] ids)
                {
                    dlg.Window.SizeToContent = SizeToContent.Height;
                    Dispatcher.UIThread.RunJobs();
                    var size = new Size(dlg.Window.ClientSize.Width, Math.Ceiling(dlg.Window.ClientSize.Height));
                    VisualSnapshot.Arrange(dlg.Window, size, 1.0);
                    var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                    Assert.Equal(520, size.Width);
                    foreach (var id in ids)
                    {
                        var control = dlg.Window.GetVisualDescendants().OfType<Control>().Single(c => AutomationIds.Get(c) == id);
                        if (!control.IsEffectivelyVisible)
                            defects.Add((variant, $"{id} is not shown"));
                        else if (VisualSnapshot.HiddenPart(control, dlg.Window) is { } hidden)
                            defects.Add((variant, $"cut: {id} — {hidden}"));
                    }

                    defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                    files.Add((subject, theme, size, VisualSnapshot.Capture(dlg.Window, subject, theme)));
                }

                string[] always = [Prefix + "Symbol", Prefix + "NewName", Prefix + "Ok", Prefix + "Cancel"];
                Capture(subjects[0], always);

                RenameDialogTests.Replace(dlg, "FullName");
                await RenameDialogTests.SummaryAsync(h, dlg, "5 changes in 3 files");
                Capture(subjects[1], [.. always, Prefix + "Summary"]);

                RenameDialogTests.Replace(dlg, "Prefix");
                await h.WaitForAsync(() => model.HasConflicts, "the conflicts listed");
                Dispatcher.UIThread.RunJobs();
                var list = dlg.Find<ListBox>("Conflicts");
                Assert.Equal(8, model.Conflicts.Count);
                if (list.Bounds.Height > 5 * 22 + 2)
                    defects.Add(("conflicts " + theme, $"the list is {list.Bounds.Height} high, more than five rows"));
                var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                if (scroller.Extent.Height <= scroller.Viewport.Height)
                    defects.Add(("conflicts " + theme, "the list of eight rows does not scroll"));
                Capture(subjects[2], [.. always, Prefix + "ConflictsLabel", Prefix + "Conflicts"]);

                dlg.Press("Escape");
                await h.WaitForAsync(() => !dlg.Window.IsVisible && !h.Services.Refactoring.IsBusy, "the dialog closed");
                await model.Checking;
            });
        }

        foreach (var subject in subjects)
            AssertSnapshots([.. files.Where(f => f.Subject == subject)], [subject], 2, defects);
    }
}
