using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class ViewLocatorTests
{
    /// <summary>View-models whose view is a window, created by their owner rather than by the locator.</summary>
    private static readonly HashSet<Type> WindowViewModels = [typeof(MainWindowViewModel)];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-014")]
    [Trait("Req", "IDE-ARCH-051")]
    public void EveryViewModelHasAView()
    {
        var viewModels = typeof(MainWindowViewModel).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(MainWindowViewModel).Namespace && t.Name.EndsWith("ViewModel", StringComparison.Ordinal)
                        && !t.IsAbstract && !WindowViewModels.Contains(t))
            .ToList();
        Assert.NotEmpty(viewModels);

        var missing = viewModels.Where(t => ViewLocator.ViewTypeFor(t) is null).Select(t => t.Name).ToList();

        Assert.True(missing.Count == 0, "View-models without a view: " + string.Join(", ", missing));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-014")]
    public void BuildsTheViewByNamingConvention()
    {
        var locator = new ViewLocator();
        var vm = new MainWindowViewModel(TestComposition.Build()).DocumentArea;

        Assert.True(locator.Match(vm));
        Assert.False(locator.Match("not a view-model"));
        Assert.IsType<DocumentAreaView>(locator.Build(vm));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-014")]
    public void MissingViewShowsVisiblePlaceholder()
    {
        var control = new ViewLocator().Build(new OrphanViewModel());

        var text = Assert.IsType<TextBlock>(control);
        Assert.Equal($"Missing view for {typeof(OrphanViewModel).FullName}", text.Text);
    }

    private sealed class OrphanViewModel : ObservableObject;
}
