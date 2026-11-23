using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.ViewModels;
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

        // The designer (D-087): the Design view with the real Object Inspector, Tool Palette and Structure, then each
        // designer dialog (strings, collection, menu, child list) while it is open.
        await ForEachDesignerWindowAsync(window =>
        {
            var missing = AutomationIdChecks.ControlsWithoutId(window);
            Assert.True(missing.Count == 0, $"{AutomationIds.Get(window)}: interactive controls without AutomationId: {string.Join(", ", missing)}");
            var malformed = AutomationIdChecks.AllIds(window).Where(id => !AutomationIds.IsWellFormed(id)).ToList();
            Assert.True(malformed.Count == 0, $"{AutomationIds.Get(window)}: malformed AutomationIds (UI-A11Y-031): " + string.Join(", ", malformed));
        });
    }

    /// <summary>
    /// Opens the designer form (a memo, a status bar with a panel, a main menu and an action list) in the Design view and
    /// runs <paramref name="check"/> on the main window, then on each designer dialog while it is open (D-087).
    /// </summary>
    private static async Task ForEachDesignerWindowAsync(Action<Window> check)
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            DesignerViewTests.Files(o);
            o.ImplementedPhase = 5; // the real Object Inspector and Tool Palette
            var unit = DesignerViewTests.Unit.Replace("GreetButton: TButton;", "GreetButton: TButton;\n    StatusBar1: TStatusBar;\n    MainMenu1: TMainMenu;\n"
                + "    ActionList1: TActionList;\n    Memo1: TMemo;", StringComparison.Ordinal).Replace("StdCtrls;", "StdCtrls, ComCtrls, Menus, ActnList;", StringComparison.Ordinal);
            o.InitialFiles["MainForm.pas"] = unit.ReplaceLineEndings("\r\n");
            var form = DesignerViewTests.FormText.ReplaceLineEndings("\n");
            var at = form.TrimEnd().LastIndexOf("end", StringComparison.Ordinal);
            o.InitialFiles["MainForm.form"] = (form[..at]
                + "  object StatusBar1: TStatusBar\n    Left = 0\n    Top = 281\n    Width = 400\n    Height = 19\n    TabOrder = 2\n"
                + "    Panels = <\n      item\n        Text = 'Ready'\n      end\n    >\n  end\n"
                + "  object Memo1: TMemo\n    Left = 16\n    Top = 60\n    Width = 185\n    Height = 89\n    TabOrder = 3\n  end\n"
                + "  object MainMenu1: TMainMenu\n    DesignLeft = 8\n    DesignTop = 8\n  end\n"
                + "  object ActionList1: TActionList\n    DesignLeft = 88\n    DesignTop = 8\n  end\n" + form[at..]).ReplaceLineEndings("\r\n");
        });
        var document = await DesignerViewTests.OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        var model = document.Designer.Model!;
        Assert.NotNull(model.FindByName("Memo1"));
        Assert.NotNull(h.TryFind<Control>("Tool.ObjectInspector.Grid"));
        Assert.NotNull(h.TryFind<Control>("Tool.ToolPalette.Item.TButton"));
        Assert.NotNull(h.TryFind<Control>(AutomationIds.Designer("MainForm")));
        h.Press("Alt+2");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(h.TryFind<Control>("Tool.Structure"));
        check(h.Window);

        var inspector = (ObjectInspectorViewModel)h.Find<Control>("Tool.ObjectInspector").DataContext!;
        async Task Dialog(string select, string name, Func<Task> open)
        {
            document.Designer.Surface!.Selection.Select(model.FindByName(select)!);
            Dispatcher.UIThread.RunJobs();
            var task = open();
            var dlg = await h.WaitForDialogAsync(name);
            try
            {
                check(dlg.Window);
            }
            finally
            {
                dlg.Window.Close();
                Dispatcher.UIThread.RunJobs();
                await task;
            }
        }

        Task Property(string path)
        {
            inspector.SetCurrent(path);
            return inspector.EditInDialogAsync();
        }

        await Dialog("Memo1", "StringsEditor", () => Property("Lines"));
        await Dialog("StatusBar1", "CollectionEditor", () => Property("Panels"));
        await Dialog("MainMenu1", "MenuDesigner", () => h.Services.Commands.ExecuteAsync("designer.editComponent"));
        await Dialog("ActionList1", "ChildListEditor", () => h.Services.Commands.ExecuteAsync("designer.editComponent"));
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

        await ForEachDesignerWindowAsync(window =>
        {
            var duplicates = AutomationIdChecks.DuplicateIds(window);
            Assert.True(duplicates.Count == 0, $"{AutomationIds.Get(window)}: duplicate AutomationIds: " + string.Join(", ", duplicates));
        });
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
