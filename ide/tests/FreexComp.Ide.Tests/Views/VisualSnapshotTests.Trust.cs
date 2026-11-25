using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subjects of the trust prompt and of the Trusted Locations page (card F09-008; I-267, I-268).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// The dialog <c>TrustProject</c>, light and dark, at its minimum size (640 × 420) and enlarged (900 × 600):
    /// <c>trust-prompt</c> — two projects, the one that was trusted with other commands carrying the note;
    /// <c>trust-prompt-folder-message</c> — after a folder was trusted that covers only one of them (the message row, the
    /// singular button text). Rules: the introduction, the tree, every row, the message and the four buttons are drawn
    /// completely inside the dialog; the four buttons are in one row (UI-DLG-296); the tree is drawn.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-296")]
    [Trait("Req", "UI-DLG-293")]
    public async Task TrustPrompt_RendersWithoutClipping()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        string[] subjects = ["trust-prompt", "trust-prompt-folder-message"];
        Size[] sizes = [new(640, 420), new(900, 600)];
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
                TrustDialogTests.AddLibrary(o, "echo done");
            }, TrustDialogTests.HelloEvents + TrustDialogTests.DependsOnLibrary);
            var hello = BuildScenario.ProjectPath(h);
            h.Services.Trust.Store.TrustProject(hello, "sha256:" + new string('0', 64)); // trusted earlier, with other commands

            var building = h.Services.Build.BuildAsync(BuildKind.Build);
            var dlg = await h.WaitForDialogAsync("TrustProject");
            try
            {
                void Shoot(string subject, string[] rows, bool message)
                {
                    foreach (var size in sizes)
                    {
                        VisualSnapshot.Arrange(dlg.Window, size, 1.0);
                        var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                        Assert.Equal(size, dlg.Window.ClientSize);
                        var buttons = ((string[])["BuildWithoutEvents", "TrustProject", "TrustFolder", "Cancel"]).Select(dlg.Find<Button>).ToList();
                        Control[] parts = [dlg.Find<Control>("Intro"), dlg.Find<Control>("Commands"), .. rows.Select(dlg.Find<Control>), .. buttons];
                        if (message)
                            parts = [.. parts, dlg.Find<Control>("Error")];
                        else
                            Assert.False(dlg.Find<Control>("Error").IsEffectivelyVisible);
                        Assert.Equal(rows.Length, dlg.Find<ListBox>("Commands").ItemCount);
                        foreach (var control in parts)
                        {
                            Assert.True(control.IsEffectivelyVisible, $"{variant}: {AutomationIds.Get(control)} is not shown");
                            if (VisualSnapshot.HiddenPart(control, dlg.Window) is { } hidden)
                                defects.Add((variant, $"cut: {AutomationIds.Get(control)} — {hidden}"));
                        }

                        // One row of buttons, left to right, none overlapping its neighbour.
                        var boxes = buttons.Select(b => new Rect(b.TranslatePoint(default, dlg.Window)!.Value, b.Bounds.Size)).ToList();
                        if (boxes.Any(b => Math.Abs(b.Y - boxes[0].Y) > 0.5) || boxes.Zip(boxes.Skip(1)).Any(p => p.First.Right > p.Second.X + 0.5))
                            defects.Add((variant, "the four buttons are not in one row: " + string.Join(" | ", boxes)));
                        if (VisualSnapshot.DistinctColors(dlg.Window, parts[1]) < 3)
                            defects.Add((variant, "the command tree is not drawn"));
                        defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                        files.Add((subject, theme, size, VisualSnapshot.Capture(dlg.Window, subject, theme)));
                    }
                }

                Shoot(subjects[0], ["Project.0", "Command.0.0", "Project.1", "Project.1.Changed", "Command.1.0", "Command.1.1"], message: false);
                Assert.Equal("_Trust These Projects", dlg.Find<Button>("TrustProject").Content);

                h.FileDialogs.NextFolder(Path.Combine(h.TempDir, "Lib"));
                dlg.Press("Alt+F"); // Trust Folder… by its mnemonic: no button is left in its pressed look
                await h.WaitForAsync(() => dlg.Find<Control>("Error").IsEffectivelyVisible, "the folder message");
                Assert.Equal("_Trust This Project", dlg.Find<Button>("TrustProject").Content);
                Shoot(subjects[1], ["Project.0", "Project.0.Changed", "Command.0.0", "Command.0.1"], message: true);
            }
            finally
            {
                dlg.Press("Escape"); // also after a failure, so that the failure is what the test reports
                await building;
            }

            Dispatcher.UIThread.RunJobs();
        }

        AssertSnapshots(files, subjects, 4, defects);
    }

    /// <summary>
    /// The Options page Environment › Trusted Locations, light and dark, at the dialog's minimum size (820 × 580) and
    /// enlarged (1100 × 760): <c>options-trusted-locations</c> — two folders and two projects, one with a path longer than
    /// the list is wide (start-trimmed, a row selected); <c>options-trusted-locations-unreadable</c> — the warning above the
    /// empty list. Rules: the hint, the warning, the list, every row, the empty text and the two buttons are drawn
    /// completely inside the dialog; the long path is trimmed inside its row; the list is drawn.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-297")]
    public async Task TrustedLocationsPage_RendersWithoutClipping()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        string[] subjects = ["options-trusted-locations", "options-trusted-locations-unreadable"];
        Size[] sizes = [new(820, 580), new(1100, 760)];
        foreach (var subject in subjects)
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
                Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        }

        const string hash = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var longPath = @"C:\Users\Sample\Documents\Projects\Customers\Northwind\Inventory\Applications\Desktop\WarehouseManager\Source\Main\WarehouseManagerDesktopApplication.pproj";
        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o => o.Settings["appearance.theme"] = theme);
            var store = h.Services.Trust.Store;
            var trustFile = Path.Combine(h.Services.UserPaths.RoamingDirectory, "trust.json");

            async Task ShootAsync(string subject, int rows, bool warning)
            {
                var running = h.Services.Commands.ExecuteAsync("tools.options", Core.Dialogs.OptionsViewModel.EnvironmentTrustPage);
                var dlg = await h.WaitForDialogAsync("Options");
                try
                {
                    if (rows > 0)
                        dlg.Click("Trust.Entry.1");
                    foreach (var size in sizes)
                    {
                        VisualSnapshot.Arrange(dlg.Window, size, 1.0);
                        var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                        Assert.Equal(size, dlg.Window.ClientSize);
                        Assert.Equal("Environment › Trusted Locations", dlg.Find<TextBlock>("PageTitle").Text);
                        var list = dlg.Find<ListBox>("Trust.Entries");
                        Assert.Equal(rows, list.ItemCount);
                        Control[] parts =
                        [
                            dlg.Find<Control>("Page.Environment.Trust"), dlg.Find<Control>("Trust.Hint"), list,
                            .. dlg.FindAll("Dialog.Options.Trust.Entry."), dlg.Find<Control>("Trust.Remove"), dlg.Find<Control>("Trust.RemoveAll"),
                        ];
                        parts = [.. parts, dlg.Find<Control>(warning ? "Trust.Warning" : "Trust.Hint"), .. rows == 0 ? [dlg.Find<Control>("Trust.Empty")] : Array.Empty<Control>()];
                        Assert.Equal(warning, dlg.Find<Control>("Trust.Warning").IsEffectivelyVisible);
                        Assert.Equal(rows == 0, dlg.Find<Control>("Trust.Empty").IsEffectivelyVisible);
                        foreach (var control in parts)
                        {
                            Assert.True(control.IsEffectivelyVisible, $"{variant}: {AutomationIds.Get(control)} is not shown");
                            if (VisualSnapshot.HiddenPart(control, dlg.Window) is { } hidden)
                                defects.Add((variant, $"cut: {AutomationIds.Get(control)} — {hidden}"));
                        }

                        if (rows > 0)
                        {
                            // The long path stays inside its row (start-trimmed), right of the 80 px Kind cell.
                            var row = dlg.Find<ListBoxItem>("Trust.Entry.3");
                            var cells = ((Grid)row.Content!).Children.OfType<TextBlock>().ToList();
                            var path = new Rect(cells[1].TranslatePoint(default, row)!.Value, cells[1].Bounds.Size);
                            if (path.Right > row.Bounds.Width + 0.5 || path.X < 80)
                                defects.Add((variant, $"the long path {path} leaves its row {row.Bounds.Size}"));
                            if (size == sizes[0] && !cells[1].TextLayout.TextLines[0].HasCollapsed)
                                defects.Add((variant, "the long path is not trimmed at the minimum size"));
                            if (VisualSnapshot.DistinctColors(dlg.Window, list) < 3)
                                defects.Add((variant, "the list is not drawn"));
                        }

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

            store.TrustFolder(@"C:\Work");
            store.TrustFolder(@"D:\Shared\Projects");
            store.TrustProject(@"C:\Samples\Demo\Demo.pproj", hash);
            store.TrustProject(longPath, hash);
            await ShootAsync(subjects[0], rows: 4, warning: false);

            File.WriteAllText(trustFile, "{ \"formatVersion\": 1, \"folders\": [ \"C:\\Work\" ");
            await ShootAsync(subjects[1], rows: 0, warning: true);
            Dispatcher.UIThread.RunJobs();
        }

        AssertSnapshots(files, subjects, 4, defects);
    }
}
