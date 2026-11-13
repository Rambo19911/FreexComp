using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.ViewModels;

[Trait("Phase", "1")]
public sealed class WindowTitleTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-030")]
    [Trait("Req", "IDE-P3-005")]
    public async Task FormatPerState()
    {
        // main-window.md §4: [<project>][ (<state>)] – FreexComp, en dash between parts.
        Assert.Equal("FreexComp", WindowTitle.Format(null, IdeState.Idle));
        Assert.Equal("Unit1.pas \u2013 FreexComp", WindowTitle.Format("Unit1.pas", IdeState.Idle));
        Assert.Equal("Hello \u2013 FreexComp", WindowTitle.Format("Hello", IdeState.Idle));
        Assert.Equal(@"C:\Projects\Hello\Hello.pproj" + " \u2013 FreexComp", WindowTitle.Format(@"C:\Projects\Hello\Hello.pproj", IdeState.Idle));
        Assert.Equal("Hello (Building) \u2013 FreexComp", WindowTitle.Format("Hello", IdeState.Building));
        Assert.Equal("Hello (Running) \u2013 FreexComp", WindowTitle.Format("Hello", IdeState.Running));
        Assert.Equal("Hello (Debugging) \u2013 FreexComp", WindowTitle.Format("Hello", IdeState.DebugRunning));
        Assert.Equal("Hello (Paused) \u2013 FreexComp", WindowTitle.Format("Hello", IdeState.Paused));
        Assert.Equal("(Building) \u2013 FreexComp", WindowTitle.Format(null, IdeState.Building));

        // The main window follows the active loose document (no project service yet) and the full-path option.
        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        var vm = new MainWindowViewModel(services);
        var changes = 0;
        vm.PropertyChanged += (_, e) => changes += e.PropertyName == nameof(MainWindowViewModel.Title) ? 1 : 0;
        Assert.Equal("FreexComp", vm.Title);

        var unit = services.Documents.NewUntitledUnit();
        Assert.Equal("Unit1.pas \u2013 FreexComp", vm.Title);
        Assert.True(changes > 0);

        fs.CreateDirectory(@"C:\p");
        Assert.True((await services.Documents.SaveAsAsync(unit, @"C:\p\Hello.pas")).Success);
        Assert.Equal("Hello.pas \u2013 FreexComp", vm.Title);

        services.Settings.Set(SettingsCatalog.AppearanceShowFullPathInTitle, true);
        Assert.Equal(@"C:\p\Hello.pas" + " \u2013 FreexComp", vm.Title);
        var untitled = services.Documents.NewUntitledUnit();
        Assert.Equal("Unit1.pas \u2013 FreexComp", vm.Title);         // no path: the name

        services.Documents.Close(untitled);
        services.Documents.Close(unit);
        Assert.Equal("FreexComp", vm.Title);
    }
}
