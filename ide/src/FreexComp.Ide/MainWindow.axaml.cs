using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Input;
using FreexComp.Ide.Views;

namespace FreexComp.Ide;

/// <summary>
/// The main window. Code-behind only adapts toolkit events (IDE-ARCH-002): focus changes and the tunnelling
/// <c>KeyDown</c> are reported to the view-model, which decides (IDE-ARCH-042/043).
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (ViewModel is { } vm)
            {
                MenuBarBuilder.Populate(MenuBar, vm.MainMenu); // UI-MENU-002: generated, not written in XAML
                ToolBars.Populate(vm.ToolBars); // UI-TB-002
                ToolBars.ContextMenu = MenuBarBuilder.CreateContextMenu(vm.ToolBarContextMenu); // UI-TB-004
            }
        };
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, _) => ReportFocus(), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, (_, _) => Dispatcher.UIThread.Post(ReportFocus), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm || KeyMapping.FromKeyEvent(e.Key, e.KeyModifiers, e.KeySymbol) is not { } gesture)
        {
            return;
        }

        ReportFocus(); // the state must be current even if a focus event is still queued
        if (vm.HandleKey(gesture))
        {
            e.Handled = true;
        }
    }

    private void ReportFocus() => ViewModel?.UpdateFocus(CommandRouting.StateOf(FocusManager?.GetFocusedElement() as Avalonia.Visual));
}
