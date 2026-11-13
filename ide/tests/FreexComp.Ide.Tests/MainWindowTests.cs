using Avalonia.Headless.XUnit;

namespace FreexComp.Ide.Tests;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    [Trait("Phase", "1")]
    [Trait("Kind", "Ui")]
    public void MainWindowShowsWithAppIcon()
    {
        var window = new MainWindow();
        window.Show();

        Assert.True(window.IsVisible);
        Assert.NotNull(window.Icon); // BR-001
        window.Close();
    }
}
