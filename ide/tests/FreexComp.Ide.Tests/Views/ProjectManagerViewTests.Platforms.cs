using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

public sealed partial class ProjectManagerViewTests
{
    private const string PlatformsRow = Row + ProjectManagerViewModel.PlatformsKey;

    private static Color TokenColor(HeadlessHarness h, string key)
    {
        Assert.True(h.Window.TryFindResource(key, h.Window.ActualThemeVariant, out var brush), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    }

    /// <summary>
    /// UI-TW-131…135 in the light and the dark theme: the node <c>Target Platforms</c> is the last row of the loaded
    /// project (icon <c>platforms</c>, <c>type.body</c>, no badge, no modified marker); its rows show the display name
    /// and the label (icon <c>platform</c>), the active one in <c>type.bodyStrong</c> with ", active platform" in its
    /// screen-reader name, a platform that is not installed in <c>text.disabled</c> with the warning overlay and the
    /// tooltip <c>Msg.Platform.NotInstalled</c>. Context menus: the node — Add Platform…; a row — Set as Active
    /// Platform (disabled on the active row), a separator, Add Platform… and Remove Platform. Enter on a row makes it
    /// the active platform; Add Platform… opens the quick pick titled <c>Add Platform</c>.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-131")]
    [Trait("Req", "UI-TW-132")]
    [Trait("Req", "UI-TW-133")]
    [Trait("Req", "UI-TW-134")]
    [Trait("Req", "UI-TW-135")]
    [Trait("Req", "IDE-PLAT-010")]
    [Trait("Req", "IDE-PLAT-011")]
    [Trait("Req", "IDE-PLAT-012")]
    public async Task TargetPlatformsNodeRenders()
    {
        Assert.Contains("platforms", IconCatalog.Names);
        Assert.Contains("platform", IconCatalog.Names);
        var disabledColors = new List<Color>();
        foreach (var theme in (string[])["light", "dark"])
        {
            await using var h = await HeadlessHarness.StartAsync(o =>
            {
                o.Settings["appearance.theme"] = theme;
                o.PlatformCatalog = new TestPlatformCatalog();
                o.InitialFiles[@"Hello\Hello.pproj"] = """
                    { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
                      "platforms": ["win64", "win32"], "activePlatform": "win64",
                      "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" } ] }
                    """;
                o.InitialFiles[@"Hello\Hello.pas"] = "program Hello;\nbegin\nend.\n";
                o.InitialFiles[@"Hello\Unit1.pas"] = "unit Unit1;\ninterface\nimplementation\nend.\n";
            });
            var pm = await OpenHelloAsync(h);
            await h.ExecuteAsync("view.projectManager");
            Dispatcher.UIThread.RunJobs();

            // The node: the last row of the project, collapsed; icon, text style, no badge, no marker.
            Assert.Equal(["$project", "Hello.pas", "Unit1.pas", ProjectManagerViewModel.PlatformsKey], VisibleKeys(pm));
            var node = h.Find<TreeViewItem>(PlatformsRow);
            Assert.Equal("Target Platforms", h.Find<TextBlock>(PlatformsRow + ".Name").Text);
            Assert.Equal("Target Platforms", AutomationProperties.GetName(node));
            Assert.Equal("platforms", IconOf(h, ProjectManagerViewModel.PlatformsKey));
            Assert.True(h.Find<IdeIcon>(PlatformsRow + ".Icon").IsEffectivelyVisible);
            Assert.Contains("body", h.Find<TextBlock>(PlatformsRow + ".Name").Classes);
            Assert.DoesNotContain("bodyStrong", h.Find<TextBlock>(PlatformsRow + ".Name").Classes);
            Assert.Null(h.TryFind<TextBlock>(PlatformsRow + ".Badge"));
            Assert.False(h.TryFind<Control>(PlatformsRow + ".Modified")?.IsEffectivelyVisible == true);
            Assert.False(h.Find<IdeIcon>(PlatformsRow + ".Icon.Missing").IsVisible);
            Assert.Null(ToolTip.GetTip(node));

            // Expanded: one row per listed platform, in file order.
            pm.Find(ProjectManagerViewModel.PlatformsKey)!.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["$project", "Hello.pas", "Unit1.pas", "$platforms/", "$platforms/win64", "$platforms/win32"], VisibleKeys(pm));
            const string Active = PlatformsRow + "win64", Absent = PlatformsRow + "win32";
            Assert.Equal(("Windows 64-bit (Win64)", "Win32 (not installed)"), (h.Find<TextBlock>(Active + ".Name").Text, h.Find<TextBlock>(Absent + ".Name").Text));
            Assert.Equal(("platform", "platform"), (IconOf(h, "$platforms/win64"), IconOf(h, "$platforms/win32")));

            // UI-TW-132: the active platform's row is strong and says so to a screen reader.
            Assert.Contains("bodyStrong", h.Find<TextBlock>(Active + ".Name").Classes);
            Assert.DoesNotContain("bodyStrong", h.Find<TextBlock>(Absent + ".Name").Classes);
            Assert.Equal("Windows 64-bit (Win64), active platform", AutomationProperties.GetName(h.Find<TreeViewItem>(Active)));
            Assert.Equal("Win32 (not installed)", AutomationProperties.GetName(h.Find<TreeViewItem>(Absent)));
            Assert.Null(ToolTip.GetTip(h.Find<TreeViewItem>(Active)));

            // UI-TW-133: not installed — text.disabled, the warning overlay, the tooltip.
            var disabled = TokenColor(h, "FxcTextDisabledBrush");
            disabledColors.Add(disabled);
            Assert.NotEqual(TokenColor(h, "FxcTextPrimaryBrush"), disabled);
            Assert.Equal(disabled, Assert.IsAssignableFrom<ISolidColorBrush>(h.Find<TextBlock>(Absent + ".Name").Foreground).Color);
            Assert.Equal(TokenColor(h, "FxcTextPrimaryBrush"), Assert.IsAssignableFrom<ISolidColorBrush>(h.Find<TextBlock>(Active + ".Name").Foreground).Color);
            Assert.True(h.Find<IdeIcon>(Absent + ".Icon.Missing").IsVisible);
            Assert.False(h.Find<IdeIcon>(Active + ".Icon.Missing").IsVisible);
            Assert.Equal(Strings.Msg_Platform_NotInstalled, ToolTip.GetTip(h.Find<TreeViewItem>(Absent)));
            Assert.All((string[])[Active, Absent], id => Assert.True(h.Find<TreeViewItem>(id).Bounds.Height > 0, id));

            // UI-TW-134: the context menu of a platform row — opened by a right click, which selects the row.
            h.RightClick(Absent);
            Assert.Equal(["$platforms/win32"], pm.Selection.Select(n => n.Key));
            var menu = h.Find<TreeViewItem>(Absent).ContextMenu ?? h.Find<TreeView>("Tool.ProjectManager.Tree").ContextMenu;
            Assert.NotNull(menu);
            Assert.Equal(["Context.project.setPlatform", null, "Context.project.addPlatform", "Context.project.removePlatform"],
                menu!.Items.OfType<Control>().Select(i => i is MenuItem item ? AutomationIds.Get(item) : null));
            var entries = menu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(["Set as _Active Platform", "A_dd Platform…", "_Remove Platform"], entries.Select(i => i.Header));
            Assert.Equal([true, true, true], entries.Select(i => i.IsEffectivelyEnabled)); // test32 is installed and not listed
            menu.Close();

            // … on the active row Set as Active Platform is disabled.
            h.RightClick(Active);
            menu = h.Find<TreeViewItem>(Active).ContextMenu ?? h.Find<TreeView>("Tool.ProjectManager.Tree").ContextMenu;
            entries = [.. menu!.Items.OfType<MenuItem>()];
            Assert.Equal([false, true, true], entries.Select(i => i.IsEffectivelyEnabled));
            menu.Close();

            // The node's menu: Add Platform… only.
            h.RightClick(PlatformsRow + ".Name"); // the row itself: the item also spans its expanded children
            menu = h.Find<TreeViewItem>(PlatformsRow).ContextMenu ?? h.Find<TreeView>("Tool.ProjectManager.Tree").ContextMenu;
            Assert.Equal(["Context.project.addPlatform"], menu!.Items.OfType<MenuItem>().Select(AutomationIds.Get));
            menu.Close();

            // UI-TW-135: Add Platform… — the quick pick with its title and one item, display name and label.
            await h.ExecuteAsync("project.addPlatform");
            Dispatcher.UIThread.RunJobs();
            var input = h.Find<TextBox>("QuickPick.Input");
            Assert.Equal("Add Platform", input.PlaceholderText);
            Assert.Equal("Add Platform", AutomationProperties.GetName(input));
            Assert.Equal(["Test 32-bit (Test32)"], h.Services.QuickPick.Current!.Items.Select(i => i.Text));
            h.Press("Enter");
            await h.WaitForAsync(() => h.Services.Projects.Project!.Platforms.Count == 3, "platform added");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(h.Services.QuickPick.Current);
            Assert.Equal("Test 32-bit (Test32)", h.Find<TextBlock>(PlatformsRow + "test32.Name").Text);
            Assert.Equal("win64", h.Services.Projects.Project!.ActivePlatform);

            // Nothing left to add: the item is disabled and its tooltip says why — also while disabled.
            h.RightClick(PlatformsRow + ".Name"); // the row itself: the item also spans its expanded children
            menu = h.Find<TreeViewItem>(PlatformsRow).ContextMenu ?? h.Find<TreeView>("Tool.ProjectManager.Tree").ContextMenu;
            var add = Assert.Single(menu!.Items.OfType<MenuItem>());
            Assert.False(add.IsEffectivelyEnabled);
            Assert.Equal(Strings.Msg_Platform_NothingToAdd, ToolTip.GetTip(add));
            Assert.True(ToolTip.GetShowOnDisabled(add));
            menu.Close();

            // IDE-PLAT-012: Enter on a platform row makes it the active platform; the strong row follows.
            pm.Select(pm.Find("$platforms/test32")!);
            Dispatcher.UIThread.RunJobs();
            h.Find<TreeViewItem>(PlatformsRow + "test32").Focus();
            h.Press("Enter");
            await h.WaitForAsync(() => h.Services.Projects.Project!.ActivePlatform == "test32", "platform activated");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("bodyStrong", h.Find<TextBlock>(PlatformsRow + "test32.Name").Classes);
            Assert.DoesNotContain("bodyStrong", h.Find<TextBlock>(Active + ".Name").Classes);
            Assert.Equal("Debug · Test32", h.Find<TextBlock>(Row + "$project.Badge").Text);
            Assert.True(pm.Find(ProjectManagerViewModel.PlatformsKey)!.IsExpanded); // the node stays open across the rebuild

            // Every interactive control has an AutomationId; none twice.
            Assert.Empty(AutomationIdChecks.ControlsWithoutId(h.Window));
            Assert.Empty(AutomationIdChecks.DuplicateIds(h.Window));
        }

        Assert.NotEqual(disabledColors[0], disabledColors[1]); // the tokens of both themes resolved
    }
}
