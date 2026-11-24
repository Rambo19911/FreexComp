using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subjects of unit rename (I-236) and of library projects (I-228).</summary>
public sealed partial class VisualSnapshotTests
{
    private static void LibraryProject(HarnessOptions o, string theme)
    {
        o.Settings["appearance.theme"] = theme;
        o.InitialFiles[@"Mathlib\Mathlib.pproj"] = """
            { "formatVersion": 1, "name": "Mathlib", "appType": "library", "mainSource": "Mathlib.pas",
              "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" },
                                  "Release": { "optimization": 2, "debugInfo": false, "defines": [], "outputDir": "bin/Release" } },
              "run": { "arguments": "--selftest", "workingDir": "", "hostApplication": "host/Loader.exe" },
              "files": [ { "path": "Mathlib.pas" }, { "path": "Unit1.pas" }, { "path": "lib/Vectors.pas" } ] }
            """;
        o.InitialFiles[@"Mathlib\Mathlib.pas"] = "library Mathlib;\n\nuses Unit1;\n\nexports\n  Add;\n\nbegin\nend.\n";
        o.InitialFiles[@"Mathlib\Unit1.pas"] = "unit Unit1;\ninterface\nfunction Add(A, B: Integer): Integer; stdcall;\nimplementation\nfunction Add(A, B: Integer): Integer; stdcall;\nbegin\n  Result := A + B;\nend;\nend.\n";
        o.InitialFiles[@"Mathlib\lib\Vectors.pas"] = "unit Vectors;\ninterface\nimplementation\nend.\n";
    }

    /// <summary>Size, colour count and light/dark checks shared by the subjects of this file; then the defects.</summary>
    private static void AssertSnapshots(List<(string Subject, string Theme, Size Size, SnapshotFile File)> files, IReadOnlyList<string> subjects,
        int perSubject, List<(string Variant, string Defect)> defects)
    {
        foreach (var subject in subjects)
            Assert.Equal(perSubject, Directory.GetFiles(VisualSnapshot.FolderOf(subject), "*.png").Length);
        foreach (var (_, _, size, file) in files)
        {
            Assert.Equal(((int)size.Width, (int)size.Height), VisualSnapshot.PngSize(file.Path));
            Assert.True(file.DistinctColors >= 16, $"{file.Path}: only {file.DistinctColors} colours — not a real rendering");
        }

        foreach (var light in files.Where(f => f.Theme == "light"))
        {
            var dark = files.Single(f => f.Theme == "dark" && f.Subject == light.Subject && f.Size == light.Size);
            Assert.True(dark.File.MeanLuminance + 64 < light.File.MeanLuminance, $"{dark.File.Path}: {dark.File.MeanLuminance} against {light.File.MeanLuminance}");
        }

        Assert.True(defects.Count == 0, Report(defects));
    }

    /// <summary>
    /// The Project Manager of a library project (IDE-TGT-017: the root with the icon variant <c>project.library</c>) and
    /// the busy row of a unit rename that waits for the language service (UI-TW-118: typed name in
    /// <c>text.secondary</c>, the 12 px ring), in the main window at 800 × 500 and 1920 × 1040, light and dark. PNGs under
    /// <c>project-manager-library-project</c> and <c>project-manager-rename-busy</c>. Rules: the icon and the ring are
    /// drawn and lie inside the tool window, the typed name is shown completely.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TGT-017")]
    [Trait("Req", "UI-TW-118")]
    public async Task ProjectManagerLibraryAndBusyRow_RenderAtMinimumAndLargeWindow()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        string[] subjects = ["project-manager-library-project", "project-manager-rename-busy"];
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
            await using var h = await HeadlessHarness.StartAsync(o => LibraryProject(o, theme));
            Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "Mathlib", "Mathlib.pproj")));
            var pm = h.Services.ProjectManager;
            await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
            Assert.True(h.Services.ToolWindows.Show(Core.ViewModels.ProjectManagerViewModel.ToolName, focus: true));
            Dispatcher.UIThread.RunJobs();
            var tree = h.Find<TreeView>("Tool.ProjectManager.Tree");
            var view = tree.FindAncestorOfType<ProjectManagerView>()!;
            const string RootId = "Tool.ProjectManager.Row.$project";
            const string UnitId = "Tool.ProjectManager.Row.Unit1.pas";

            foreach (var subject in subjects)
            {
                var busy = subject == subjects[1];
                var unit = pm.Find("Unit1.pas")!;
                if (busy)
                {
                    pm.Select(unit);
                    Assert.True(pm.BeginRename("Unit1.pas"));
                    Dispatcher.UIThread.RunJobs();
                    h.Find<TextBox>(UnitId + ".Editor").Text = "Customers.pas";
                    unit.BusyName = "Customers.pas";
                    Dispatcher.UIThread.RunJobs();
                }

                foreach (var size in sizes)
                {
                    VisualSnapshot.Arrange(h.Window, size, 1.0);
                    var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                    Assert.Equal(size, h.Window.ClientSize);
                    var viewRight = view.TranslatePoint(new Point(view.Bounds.Width, 0), h.Window)!.Value.X;

                    var icon = h.Find<IdeIcon>(RootId + ".Icon");
                    Assert.Equal("project.library", icon.IconName);
                    Assert.True(icon.IsEffectivelyVisible, variant + ": no root icon");
                    Assert.Equal(new Size(16, 16), icon.Bounds.Size);
                    if (VisualSnapshot.HiddenPart(icon, h.Window) is { } hiddenIcon)
                        defects.Add((variant, $"cut: library project icon — {hiddenIcon}"));
                    if (VisualSnapshot.DistinctColors(h.Window, icon) < 2)
                        defects.Add((variant, "the library project icon is not drawn"));
                    AddCutText(defects, variant, "project name", h.Find<TextBlock>(RootId + ".Name"), h.Window, viewRight);

                    if (busy)
                    {
                        Assert.Null(h.TryFind<TextBox>(UnitId + ".Editor"));
                        var name = h.Find<TextBlock>(UnitId + ".Name");
                        Assert.Equal("Customers.pas", name.Text);
                        AddCutText(defects, variant, "typed name of the busy row", name, h.Window, viewRight);
                        var ring = h.Find<Control>(UnitId + ".Busy");
                        Assert.True(ring.IsEffectivelyVisible, variant + ": no busy ring");
                        Assert.Equal(new Size(12, 12), ring.Bounds.Size);
                        if (VisualSnapshot.HiddenPart(ring, h.Window) is { } hiddenRing)
                            defects.Add((variant, $"cut: busy ring — {hiddenRing}"));
                        var ringRight = ring.TranslatePoint(new Point(ring.Bounds.Width, 0), h.Window)!.Value.X;
                        if (ringRight > viewRight + 0.5)
                            defects.Add((variant, $"cut: the busy ring ends at x = {ringRight:0.#}, beyond the tool window ({viewRight:0.#})"));
                        if (VisualSnapshot.DistinctColors(h.Window, ring) < 2)
                            defects.Add((variant, "the busy ring is not drawn"));
                    }
                    else
                    {
                        Assert.Null(h.TryFind<Control>(UnitId + ".Busy"));
                    }

                    defects.AddRange(VisualSnapshot.LayoutDefects(h.Window).Select(d => (variant, d)));
                    files.Add((subject, theme, size, VisualSnapshot.Capture(h.Window, subject, theme)));
                }

                if (busy)
                {
                    unit.BusyName = null;
                    unit.Rename!.Cancel();
                    Dispatcher.UIThread.RunJobs();
                }
            }
        }

        AssertSnapshots(files, subjects, 4, defects);
    }

    /// <summary>
    /// The dialogs of a library project (IDE-TGT-010…012), light and dark: Project Options at its stated size 760 × 520
    /// and enlarged to 1200 × 800 — page Application (the <c>Library</c> type, the hint "output file name
    /// (Mathlib.dll)") and page Run (<c>HostApplication</c> enabled, with its browse button; then with the inline error
    /// of an invalid path) — and Run Parameters (560 wide, not resizable: its one size). PNGs under
    /// <c>project-options-library-application</c>, <c>project-options-library-run</c>,
    /// <c>project-options-library-run-error</c> and <c>run-parameters-library</c>.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-TGT-010")]
    [Trait("Req", "IDE-TGT-011")]
    [Trait("Req", "IDE-TGT-012")]
    public async Task LibraryProjectDialogs_RenderAtStatedAndLargeSize()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        (string Subject, string Page, string? Host)[] states =
        [
            ("project-options-library-application", "Application", null),
            ("project-options-library-run", "Run", null),
            ("project-options-library-run-error", "Run", "host|Loader.exe"),
        ];
        const string runParameters = "run-parameters-library";
        Size[] sizes = [new(760, 520), new(1200, 800)];
        foreach (var subject in states.Select(s => s.Subject).Append(runParameters))
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
                Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        }

        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();

        void CheckHostRow(DialogDriver dlg, string variant, string? error)
        {
            var host = dlg.Find<TextBox>("HostApplication");
            var browse = dlg.Find<Button>("HostApplication.Browse");
            Assert.True(host.IsEffectivelyEnabled && browse.IsEffectivelyEnabled, variant + ": the host application field is disabled");
            Assert.Equal(error, dlg.ErrorText("HostApplication"));
            foreach (var (what, control) in ((string, Control)[])[("HostApplication", host), ("HostApplication.Browse", browse), ("CommandLine", dlg.Find<TextBlock>("CommandLine"))])
            {
                Assert.True(control.IsEffectivelyVisible, $"{variant}: {what} is not shown");
                if (VisualSnapshot.HiddenPart(control, dlg.Window) is { } hidden)
                    defects.Add((variant, $"cut: {what} — {hidden}"));
            }

            if (host.Bounds.Width < 200)
                defects.Add((variant, $"cut: the host application field is {host.Bounds.Width:0.#} wide"));
            var browseRight = browse.TranslatePoint(new Point(browse.Bounds.Width, 0), dlg.Window)!.Value.X;
            if (browseRight > dlg.Window.ClientSize.Width - 8)
                defects.Add((variant, $"cut: the browse button ends at x = {browseRight:0.#} in a dialog {dlg.Window.ClientSize.Width:0.#} wide"));
        }

        foreach (var theme in Themes)
        {
            await using var h = await HeadlessHarness.StartAsync(o => LibraryProject(o, theme));
            Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "Mathlib", "Mathlib.pproj")));

            var running = h.Services.Commands.ExecuteAsync("project.options");
            var dlg = await h.WaitForDialogAsync("ProjectOptions");
            try
            {
                foreach (var state in states)
                {
                    var pages = dlg.Find<ListBox>("Pages");
                    pages.SelectedItem = pages.Items.Cast<Core.Dialogs.ProjectOptionsPage>().Single(p => p.Id == state.Page);
                    Dispatcher.UIThread.RunJobs();
                    if (state.Page == "Run")
                    {
                        dlg.Find<TextBox>("HostApplication").Text = state.Host ?? "host/Loader.exe";
                        Dispatcher.UIThread.RunJobs();
                    }

                    foreach (var size in sizes)
                    {
                        VisualSnapshot.Arrange(dlg.Window, size, 1.0);
                        Dispatcher.UIThread.RunJobs();
                        var variant = $"{state.Subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                        Assert.Equal(size, dlg.Window.ClientSize);
                        if (state.Page == "Application")
                        {
                            var appType = dlg.Find<ComboBox>("AppType");
                            Assert.Equal("library", Assert.IsType<Core.Dialogs.AppTypeItem>(appType.SelectedItem).Value);
                            var hint = dlg.Find<TextBlock>("Name.Hint");
                            Assert.Equal("output file name (Mathlib.dll)", hint.Text);
                            Assert.True(hint.IsEffectivelyVisible, variant + ": no name hint");
                            AddCutText(defects, variant, "name hint", hint, dlg.Window, size.Width);
                            if (VisualSnapshot.HiddenPart(appType, dlg.Window) is { } hiddenType)
                                defects.Add((variant, $"cut: AppType — {hiddenType}"));
                        }
                        else
                        {
                            CheckHostRow(dlg, variant, state.Host is null ? null : Core.Resources.Strings.Msg_Project_LocationInvalid);
                            Assert.Equal(state.Host is not null, dlg.Find<Control>("Page.Run.Error").IsVisible);
                        }

                        defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                        files.Add((state.Subject, theme, size, VisualSnapshot.Capture(dlg.Window, state.Subject, theme)));
                    }
                }
            }
            finally
            {
                dlg.Click("Cancel"); // also after a failure, so that the failure is what the test reports
                await running;
            }

            running = h.Services.Commands.ExecuteAsync("run.parameters");
            dlg = await h.WaitForDialogAsync("RunParameters");
            try
            {
                Dispatcher.UIThread.RunJobs();
                var size = dlg.Window.ClientSize;
                var variant = $"{runParameters} {VisualSnapshot.FileName(theme, 1.0, size)}";
                Assert.Equal(560, size.Width);
                CheckHostRow(dlg, variant, null);
                Assert.Equal(Path.Combine(h.TempDir, "Mathlib", "host", "Loader.exe") + " --selftest", ToolTip.GetTip(dlg.Find<TextBlock>("CommandLine")) ?? dlg.Find<TextBlock>("CommandLine").Text);
                defects.AddRange(VisualSnapshot.LayoutDefects(dlg.Window).Select(d => (variant, d)));
                files.Add((runParameters, theme, size, VisualSnapshot.Capture(dlg.Window, runParameters, theme)));
            }
            finally
            {
                dlg.Click("Cancel");
                await running;
            }
        }

        foreach (var state in states)
            Assert.Equal(4, Directory.GetFiles(VisualSnapshot.FolderOf(state.Subject), "*.png").Length);
        Assert.Equal(2, Directory.GetFiles(VisualSnapshot.FolderOf(runParameters), "*.png").Length);
        AssertSnapshots(files, [], 0, defects);
    }
}
