using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class AutomationIdTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-030")]
    [Trait("Req", "UI-A11Y-035")]
    [Trait("Req", "IDE-ARCH-056")]
    public async Task AllInteractiveControlsHaveIds()
    {
        await using var h = await HeadlessHarness.StartAsync();

        foreach (var window in h.AllWindows)
        {
            Assert.Contains(AutomationIds.Menu("file.save"), AutomationIdChecks.AllIds(window)); // closed menus are checked too
            var missing = AutomationIdChecks.ControlsWithoutId(window);
            Assert.True(missing.Count == 0, $"{AutomationIds.Get(window)}: interactive controls without AutomationId: {string.Join(", ", missing)}");

            var malformed = AutomationIdChecks.AllIds(window).Where(id => !AutomationIds.IsWellFormed(id)).ToList();
            Assert.True(malformed.Count == 0, "Malformed AutomationIds (UI-A11Y-031): " + string.Join(", ", malformed));
        }
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-031")]
    [Trait("Req", "UI-A11Y-035")]
    public async Task IdsAreUniquePerWindow()
    {
        await using var h = await HeadlessHarness.StartAsync();

        foreach (var window in h.AllWindows)
        {
            var duplicates = AutomationIdChecks.DuplicateIds(window);
            Assert.True(duplicates.Count == 0, "Duplicate AutomationIds: " + string.Join(", ", duplicates));
        }
    }

    [AvaloniaFact(Timeout = 30000)]
    public async Task ChecksReportOffendersButIgnoreTemplateParts()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var bar = h.Find<Panel>(AutomationIds.StatusBar);
        var unnamed = new Button();
        var first = new TextBox();
        var second = new CheckBox();
        AutomationProperties.SetAutomationId(first, "StatusBar.Same");
        AutomationProperties.SetAutomationId(second, "StatusBar.Same");
        bar.Children.Add(unnamed);
        bar.Children.Add(first);
        bar.Children.Add(second);
        bar.Children.Add(new ScrollViewer { Content = new TextBlock { Text = "x" }, Height = 10, Width = 10 }); // template has buttons
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Button"], AutomationIdChecks.ControlsWithoutId(h.Window));
        Assert.Equal(["StatusBar.Same"], AutomationIdChecks.DuplicateIds(h.Window));
        Assert.DoesNotContain(AutomationIdChecks.ControlsWithoutId(h.Window), d => d.Contains(nameof(RepeatButton), StringComparison.Ordinal));
        bar.Children.Clear();
    }

    [Theory]
    [Trait("Req", "UI-A11Y-031")]
    [Trait("Req", "UI-A11Y-033")]
    [InlineData("MainWindow", true)]
    [InlineData("Menu.file.saveAll", true)]
    [InlineData(@"Menu.file.openRecentProject:C:\p\Hello\Hello.pproj", true)]
    [InlineData("DocumentTab.Unit1.pas@2", true)]
    [InlineData("Tool.Watches.Row.$new", true)]
    [InlineData("", false)]
    [InlineData("Menu..file", false)]
    [InlineData("Menu.File\tEdit", false)] // control character
    [InlineData("Menu.File ", false)] // trailing blank
    [InlineData("menu.File", false)] // first segment PascalCase
    [InlineData(".Menu", false)]
    public void WellFormedIds(string id, bool expected) => Assert.Equal(expected, AutomationIds.IsWellFormed(id));

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-A11Y-033")]
    [Trait("Req", "UI-A11Y-034")]
    [Trait("Req", "UI-A11Y-032")]
    public void BuildersFollowTheFixedTable()
    {
        Assert.Equal("Menu.file.saveAll", AutomationIds.Menu("file.saveAll"));
        Assert.Equal(@"Menu.file.openRecentProject:C:\p\H.pproj", AutomationIds.Menu("file.openRecentProject", @"C:\p\H.pproj"));
        Assert.Equal("ToolBar.project.build", AutomationIds.ToolBarButton("project.build"));
        Assert.Equal("Context.edit.copy", AutomationIds.Context("edit.copy"));
        Assert.Equal("Menu.File", AutomationIds.TopMenu("File"));
        Assert.Equal("Menu.File.OpenRecent", AutomationIds.SubMenu("File", "OpenRecent"));
        Assert.Equal("DocumentTab.Unit1.pas", AutomationIds.DocumentTab("Unit1.pas"));
        Assert.Equal("DocumentTab.Unit1.pas@2", AutomationIds.DocumentTab("Unit1.pas", 2));
        Assert.Equal("DocumentTab.Unit1.pas.Close", AutomationIds.DocumentTabClose("Unit1.pas"));
        Assert.Equal("DocumentTab.Unit1.pas.Modified", AutomationIds.DocumentTabModified("Unit1.pas"));
        Assert.Equal("Tool.Messages", AutomationIds.Tool("Messages"));
        Assert.Equal("ToolTab.Messages", AutomationIds.ToolTab("Messages"));
        Assert.Equal("Dialog.NewProject", AutomationIds.Dialog("NewProject"));
        Assert.Equal("Dialog.NewProject.Ok", AutomationIds.DialogField("NewProject", "Ok"));
        Assert.Equal("InfoBar.ExternalChange.Action.Reload", AutomationIds.InfoBarAction("ExternalChange", "Reload"));
        Assert.Equal("StatusBar.Message", AutomationIds.StatusBarField("Message"));
    }
}
