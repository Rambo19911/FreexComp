using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subjects of build events (card F09-007; I-269, I-270).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// The Project Options page <c>Build Events</c>, light and dark, at the dialog's minimum size (760 × 520) and enlarged
    /// (1200 × 800), in its states: <c>project-options-events-all</c> (All configurations),
    /// <c>project-options-events-inherited</c> (a configuration without its own commands: check box, info line, read-only
    /// fields), <c>project-options-events-own</c> (a configuration with its own), <c>project-options-events-error</c> (an
    /// unknown macro: the message under its field, the dot on the page row), <c>project-options-events-control</c> (a stored
    /// list with a control character: read-only, glyphs, the warning). Rules (UI-DLG-299, 303, 305): every control of the
    /// page is drawn completely inside the dialog and overlaps no other; the two list fields have the same height, at least
    /// three rows; the menu button stands in the row of the combo, at the right end; no layout defect.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-299")]
    [Trait("Req", "UI-DLG-301")]
    [Trait("Req", "UI-DLG-303")]
    [Trait("Req", "UI-DLG-305")]
    public async Task BuildEventsPage_RendersWithoutClipping()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        string[] subjects =
        [
            "project-options-events-all", "project-options-events-inherited", "project-options-events-own", "project-options-events-error",
            "project-options-events-control",
        ];
        Size[] sizes = [new(760, 520), new(1200, 800)];
        foreach (var subject in subjects)
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
                Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        }

        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();

        void Shoot(DialogDriver dlg, string theme, string subject, string[] shown, string[] absent)
        {
            foreach (var size in sizes)
            {
                VisualSnapshot.Arrange(dlg.Window, size, 1.0);
                Dispatcher.UIThread.RunJobs();
                var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                Assert.Equal(size, dlg.Window.ClientSize);
                string[] always = ["Hint", "PreBuild", "PostBuild", "PostBuildWhen", "InsertMacro", "TrustHint", "Configuration", "NewConfiguration", "DeleteConfiguration"];
                foreach (var id in always.Concat(shown))
                {
                    var control = dlg.Find<Control>(id);
                    Assert.True(control.IsEffectivelyVisible, $"{variant}: {id} is not shown");
                    if (VisualSnapshot.HiddenPart(control, dlg.Window) is { } hidden)
                        defects.Add((variant, $"cut: {id} — {hidden}"));
                    if (control is TextBlock text && text.TextWrapping == Avalonia.Media.TextWrapping.NoWrap && VisualSnapshot.TextWidth(text) > text.Bounds.Width + 0.5)
                        defects.Add((variant, $"cut: {id} needs {VisualSnapshot.TextWidth(text):0.#} wide, has {text.Bounds.Width:0.#}"));
                }

                foreach (var id in absent)
                    Assert.False(dlg.TryFind<Control>(id)?.IsEffectivelyVisible == true, $"{variant}: {id} is shown");
                Assert.Null(dlg.TryFind<Control>("Notice"));

                Rect Box(string id)
                {
                    var control = dlg.Find<Control>(id);
                    return new Rect(control.TranslatePoint(default, dlg.Window)!.Value, control.Bounds.Size);
                }

                // UI-DLG-299: equal shares of the free height, never lower than three rows; nothing scrolls at these sizes.
                var (pre, post, when, insert) = (Box("PreBuild"), Box("PostBuild"), Box("PostBuildWhen"), Box("InsertMacro"));
                if (Math.Abs(pre.Height - post.Height) > 1 || pre.Height < 3 * 18) // equal up to the rounding of an odd free height
                    defects.Add((variant, $"the list fields are {pre.Height:0.#} and {post.Height:0.#} high"));
                if (Math.Abs(pre.Right - insert.Right) > 0.5 || insert.Center.Y < when.Top || insert.Center.Y > when.Bottom || insert.Left < when.Right)
                    defects.Add((variant, $"the menu button {insert} is not at the right end of the combo's row {when} (fields end at {pre.Right:0.#})"));
                // UI-DLG-305: the whole page is on screen — it does not scroll at the minimum size.
                var port = Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(dlg.Find<Control>("Hint")).OfType<ScrollViewer>().First();
                if (port.Extent.Height > port.Viewport.Height + 0.5)
                    defects.Add((variant, $"the page needs {port.Extent.Height:0.#} of height and has {port.Viewport.Height:0.#}"));
                if (VisualSnapshot.DistinctColors(dlg.Window, dlg.Find<Control>("PreBuild")) < 3 && dlg.Find<TextBox>("PreBuild").Text is { Length: > 0 })
                    defects.Add((variant, "the Before build list is not drawn"));
                defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                files.Add((subject, theme, size, VisualSnapshot.Capture(dlg.Window, subject, theme)));
            }
        }

        foreach (var theme in Themes)
        {
            await using (var h = await BuildScenario.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                ProjectOptionsDialogTests.UseEvents(o);
            }))
            {
                var running = h.Services.Commands.ExecuteAsync("project.options");
                var dlg = await h.WaitForDialogAsync("ProjectOptions");
                try
                {
                    ProjectOptionsViewTests.ShowPage(dlg, "BuildEvents");
                    Shoot(dlg, theme, subjects[0], [], ["OwnEvents", "Inherited", "Error.PreBuild", "Error.PostBuild", "Page.BuildEvents.Error"]);

                    ProjectOptionsDialogTests.ChooseConfiguration(dlg, "Debug (active)");
                    Assert.True(dlg.Find<TextBox>("PreBuild").IsReadOnly);
                    Shoot(dlg, theme, subjects[1], ["OwnEvents", "Inherited"], ["Error.PreBuild", "Error.PostBuild"]);

                    ProjectOptionsDialogTests.ChooseConfiguration(dlg, "Release");
                    Assert.True(dlg.Find<CheckBox>("OwnEvents").IsChecked);
                    Shoot(dlg, theme, subjects[2], ["OwnEvents"], ["Inherited", "Error.PreBuild", "Error.PostBuild"]);

                    ProjectOptionsDialogTests.ChooseConfiguration(dlg, Core.Resources.Strings.ProjectOptions_AllConfigurations);
                    dlg.Find<TextBox>("PostBuild").Text = "copy /y \"$(OutputPath)\" \"$(OutDir)\\deploy\"" + Environment.NewLine + "echo done";
                    Dispatcher.UIThread.RunJobs();
                    Assert.NotNull(dlg.ErrorText("PostBuild"));
                    Shoot(dlg, theme, subjects[3], ["Error.PostBuild", "Page.BuildEvents.Error"], ["OwnEvents", "Inherited", "Error.PreBuild"]);
                }
                finally
                {
                    dlg.Click("Cancel"); // also after a failure, so that the failure is what the test reports
                    await running;
                }
            }

            await using (var h = await BuildScenario.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                ProjectOptionsDialogTests.UseEvents(o, "\"echo first\\necho hidden\", \"gen\\u0001x\\ty\"");
            }))
            {
                var running = h.Services.Commands.ExecuteAsync("project.options");
                var dlg = await h.WaitForDialogAsync("ProjectOptions");
                try
                {
                    ProjectOptionsViewTests.ShowPage(dlg, "BuildEvents");
                    var pre = dlg.Find<TextBox>("PreBuild");
                    Assert.True(pre.IsReadOnly);
                    Assert.Equal("echo first↵echo hidden" + Environment.NewLine + "gen·x→y", pre.Text);
                    Assert.False(dlg.Find<TextBox>("PostBuild").IsReadOnly);
                    Assert.Equal("⚠ " + Core.Resources.Strings.Msg_ProjectOptions_EventsControlCharacter, dlg.ErrorText("PreBuild"));
                    Shoot(dlg, theme, subjects[4], ["Error.PreBuild"], ["OwnEvents", "Inherited", "Error.PostBuild", "Page.BuildEvents.Error"]);
                }
                finally
                {
                    dlg.Click("Cancel");
                    await running;
                }
            }

            Dispatcher.UIThread.RunJobs();
        }

        AssertSnapshots(files, subjects, 4, defects);
    }

    /// <summary>
    /// The dialog <c>TrustProject</c> with the command lists of configurations (UI-DLG-298), one of them with a name of 32
    /// characters; light and dark, 640 × 420 and 900 × 600: <c>trust-prompt-configurations</c>. Rules: every list name is
    /// drawn in full, every command starts at the same x, right of the widest name; no layout defect.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-DLG-298")]
    public async Task TrustPromptConfigurationLists_RenderWithoutClipping()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const string subject = "trust-prompt-configurations";
        Size[] sizes = [new(640, 420), new(900, 600)];
        if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
            Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await BuildScenario.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                TrustDialogTests.UseConfigurationEvents(o);
            });
            var building = h.Services.Build.BuildAsync(BuildKind.Build);
            var dlg = await h.WaitForDialogAsync("TrustProject");
            try
            {
                foreach (var size in sizes)
                {
                    VisualSnapshot.Arrange(dlg.Window, size, 1.0);
                    var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                    var rows = Enumerable.Range(0, 5).Select(j => dlg.Find<ListBoxItem>($"Command.0.{j}")).ToList();
                    var commandX = new List<double>();
                    foreach (var row in rows)
                    {
                        var cells = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(row).OfType<TextBlock>().ToList();
                        var (name, command) = (cells[0], cells[^1]);
                        if (VisualSnapshot.HiddenPart(name, dlg.Window) is { } hidden)
                            defects.Add((variant, $"cut: list name '{name.Text}' — {hidden}"));
                        if (VisualSnapshot.TextWidth(name) > name.Bounds.Width + 0.5)
                            defects.Add((variant, $"cut: list name '{name.Text}' needs {VisualSnapshot.TextWidth(name):0.#} wide, has {name.Bounds.Width:0.#}"));
                        commandX.Add(command.TranslatePoint(default, dlg.Window)!.Value.X);
                        if (VisualSnapshot.HiddenPart(command, dlg.Window) is { } cut)
                            defects.Add((variant, $"cut: command of '{name.Text}' — {cut}"));
                    }

                    if (commandX.Any(x => Math.Abs(x - commandX[0]) > 0.5))
                        defects.Add((variant, "the commands do not start at the same x: " + string.Join(", ", commandX)));
                    defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                    files.Add((subject, theme, size, VisualSnapshot.Capture(dlg.Window, subject, theme)));
                }
            }
            finally
            {
                dlg.Press("Escape"); // also after a failure, so that the failure is what the test reports
                await building;
            }

            Dispatcher.UIThread.RunJobs();
        }

        AssertSnapshots(files, [subject], 4, defects);
    }
}
