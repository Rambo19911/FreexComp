using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.ViewModels;

[Trait("Phase", "1")]
public sealed class MainWindowViewModelTests
{
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-030")]
    [Trait("Req", "UI-WIN-002")]
    public void ShellStartsWithProductTitleAndEmptyDocumentArea()
    {
        var vm = new MainWindowViewModel(TestComposition.Build());

        Assert.Equal(Strings.App_ProductName, vm.Title);
        Assert.True(vm.DocumentArea.IsEmpty);
        Assert.Equal(Strings.Msg_Editor_NoDocuments, vm.DocumentArea.EmptyHeading);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-002")]
    public void DocumentAreaEmptyStateFollowsOpenDocuments()
    {
        var services = TestComposition.Build();
        var area = new MainWindowViewModel(services).DocumentArea;
        var changed = new List<string?>();
        area.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var unit = services.Documents.NewUntitledUnit();
        Assert.False(area.IsEmpty);
        Assert.Contains(nameof(area.IsEmpty), changed);

        services.Documents.Close(unit);
        Assert.True(area.IsEmpty);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-ED-080")]
    public void EmptyStateHintsListOnlyFunctionalCommandsWithTheirGestures()
    {
        var services = TestComposition.Build();
        var area = new MainWindowViewModel(services).DocumentArea;
        var changed = new List<string?>();
        area.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("", area.EmptyHints); // no handler yet for file.newFile, file.open, search.goToFile

        services.Commands.RegisterHandler("file.newFile", new Enabled());
        services.Commands.RegisterHandler("file.open", new Enabled());
        services.Commands.InvalidateCanExecute();

        Assert.Equal("New File (Ctrl+N) · Open File (Ctrl+O)", area.EmptyHints);
        Assert.Contains(nameof(area.EmptyHints), changed);

        services.Keymap.SetGestures("file.open", [KeyGesture.Parse("Ctrl+Shift+O")]);
        Assert.Equal("New File (Ctrl+N) · Open File (Ctrl+Shift+O)", area.EmptyHints);
    }

    private sealed class Enabled : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument) => Task.CompletedTask;
    }
}
