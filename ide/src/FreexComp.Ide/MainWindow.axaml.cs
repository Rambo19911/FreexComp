using Avalonia.Controls;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Views;

namespace FreexComp.Ide;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                MenuBarBuilder.Populate(MenuBar, vm.MainMenu); // UI-MENU-002: generated, not written in XAML
            }
        };
    }
}
