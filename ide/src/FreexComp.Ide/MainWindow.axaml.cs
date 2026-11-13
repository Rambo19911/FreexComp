using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
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
    private static readonly Core.Commands.KeyGesture ToToolBar = Core.Commands.KeyGesture.Parse("Ctrl+Tab");

    private TextBoxCommandTarget? _textTarget;
    private WeakReference<InputElement>? _lastContent;
    private Core.Models.WindowPlacement? _normalBounds;
    private bool _closed;

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
                RestorePlacement(vm);
            }
        };
        PositionChanged += (_, _) => TrackNormalBounds();
        Closing += (_, _) =>
        {
            if (!_closed) // Close() on a closed window raises Closing again
            {
                ViewModel?.SaveUiState(CurrentPlacement());
            }
        };
        Closed += (_, _) => _closed = true;
        ToolBars.EscapeRequested += (_, _) => ViewModel?.Layout.FocusPane(Core.Models.Pane.Documents); // UI-TB-008
        StatusBar.EscapeRequested += (_, _) => ViewModel?.Layout.FocusPane(Core.Models.Pane.Documents);
        MenuBar.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (IsInMenuBar(FocusManager?.GetFocusedElement() as Avalonia.Visual))
            {
                RestoreFocus(); // UI-A11Y-002: a closed menu gives focus back to the element that had it
            }
        });
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, _) => ReportFocus(), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, (_, _) => Dispatcher.UIThread.Post(OnFocusMaybeLost), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty || change.Property == WindowStateProperty)
        {
            TrackNormalBounds();
        }
    }

    /// <summary>UI-WIN-052/053: opens at the saved normal bounds when they are on a monitor, else centerd on the primary one.</summary>
    private void RestorePlacement(MainWindowViewModel vm)
    {
        if (Screens is not { All.Count: > 0 } screens)
        {
            return; // no monitor information (headless without screens): the window keeps its XAML size
        }

        var all = screens.All;
        var areas = all.Select(ToDip).ToList();
        var primary = screens.Primary ?? all[0];
        var placement = vm.RestorePlacement(areas, ToDip(primary));
        var scaling = all.FirstOrDefault(s => Contains(ToDip(s), placement.X, placement.Y))?.Scaling ?? primary.Scaling;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new Avalonia.PixelPoint((int)Math.Round(placement.X * scaling), (int)Math.Round(placement.Y * scaling));
        Width = placement.Width;
        Height = placement.Height;
        _normalBounds = placement with { Maximized = false };
        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized; // on the monitor holding the normal bounds
        }
    }

    /// <summary>The normal bounds are remembered while the window is normal, so a maximized window saves them too.</summary>
    private void TrackNormalBounds()
    {
        if (WindowState == WindowState.Normal && IsVisible)
        {
            _normalBounds = NormalBoundsNow();
        }
    }

    /// <summary>IDE-ARCH-060: normal bounds plus maximized; a minimized window is saved as normal (UI-WIN-052).</summary>
    private Core.Models.WindowPlacement? CurrentPlacement()
    {
        var normal = WindowState == WindowState.Normal ? NormalBoundsNow() : _normalBounds;
        return normal is null ? null : normal with { Maximized = WindowState is WindowState.Maximized or WindowState.FullScreen };
    }

    private Core.Models.WindowPlacement NormalBoundsNow()
    {
        var scaling = Screens?.ScreenFromWindow(this)?.Scaling ?? 1;
        return new Core.Models.WindowPlacement(
            (int)Math.Round(Position.X / scaling),
            (int)Math.Round(Position.Y / scaling),
            (int)Math.Round(ClientSize.Width),
            (int)Math.Round(ClientSize.Height),
            Maximized: false);
    }

    private static Core.Models.WorkArea ToDip(Avalonia.Platform.Screen screen) => new(
        screen.WorkingArea.X / screen.Scaling,
        screen.WorkingArea.Y / screen.Scaling,
        screen.WorkingArea.Width / screen.Scaling,
        screen.WorkingArea.Height / screen.Scaling);

    private static bool Contains(Core.Models.WorkArea area, double x, double y) =>
        x >= area.X && x < area.X + area.Width && y >= area.Y && y < area.Y + area.Height;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm || KeyMapping.FromKeyEvent(e.Key, e.KeyModifiers, e.KeySymbol) is not { } gesture)
        {
            return;
        }

        if (gesture.Modifiers == Core.Commands.KeyModifiers.None && FocusManager?.GetFocusedElement() is MenuItem item
            && IsInMenuBar(item) && MenuMnemonics.Handle(item, e.KeySymbol, MenuBar))
        {
            e.Handled = true;
            return;
        }

        if (gesture == ToToolBar && IsInMenuBar(FocusManager?.GetFocusedElement() as Avalonia.Visual))
        {
            MenuBar.Close();
            if (!ToolBars.FocusFirstButton()) // UI-TB-008: Alt, then Ctrl+Tab
            {
                RestoreFocus(); // no button can take focus
            }

            e.Handled = true;
            return;
        }

        ReportFocus(); // the state must be current even if a focus event is still queued
        if (vm.HandleKey(gesture))
        {
            e.Handled = true;
        }
    }

    private void ReportFocus()
    {
        if (ViewModel is not { } vm || CommandRouting.StateOf(FocusManager?.GetFocusedElement() as Avalonia.Visual) is not { } state)
        {
            return; // focus in a menu, the toolbar row or a popup: commands keep acting on the previous element (I-153)
        }

        var textTarget = state.Target as TextBoxCommandTarget;
        if (textTarget != _textTarget)
        {
            _textTarget?.StateChanged -= OnTextTargetChanged;
            _textTarget = textTarget;
            _textTarget?.StateChanged += OnTextTargetChanged;
        }

        if (FocusManager?.GetFocusedElement() is InputElement focused && (_lastContent?.TryGetTarget(out var last) != true || last != focused))
        {
            _lastContent = new WeakReference<InputElement>(focused);
            focused.DetachedFromVisualTree += OnFocusedDetached;
        }

        vm.UpdateFocus(state);
    }

    /// <summary>
    /// UI-A11Y-013 / UI-A11Y-002: when focus went nowhere (the focused element disappeared, a menu closed), it returns to
    /// the last focused element outside menus and toolbars if that is still there, else to the active document, else to
    /// the menu bar. (Next row / owning list are the lists' own rules.)
    /// </summary>
    private void OnFocusMaybeLost()
    {
        ReportFocus();
        if (OwnedWindows.Any(w => w.IsVisible))
        {
            return; // focus went to a dialog over this window (UI-DLG-001); it comes back when the dialog closes
        }

        if (IsVisible && (FocusManager?.GetFocusedElement() is not Avalonia.Visual focused || TopLevel.GetTopLevel(focused) != this))
        {
            RestoreFocus();
        }
    }

    private void OnFocusedDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        ((InputElement)sender!).DetachedFromVisualTree -= OnFocusedDetached;
        Dispatcher.UIThread.Post(OnFocusMaybeLost);
    }

    private void RestoreFocus()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (_lastContent?.TryGetTarget(out var last) == true && TopLevel.GetTopLevel(last as Avalonia.Visual) == this
            && last.IsEffectivelyVisible && last.IsEffectivelyEnabled && last.Focusable && last.Focus())
        {
            return;
        }

        vm.Layout.FocusPane(Core.Models.Pane.Documents);
        if (FocusManager?.GetFocusedElement() is not Avalonia.Visual now || TopLevel.GetTopLevel(now) != this)
        {
            MenuBar.Focus();
        }
    }

    /// <summary>Logical containment: open submenus live in the overlay layer, not under the menu bar visually.</summary>
    private bool IsInMenuBar(Avalonia.Visual? visual) =>
        visual is Avalonia.LogicalTree.ILogical logical && (logical == MenuBar || logical.GetLogicalAncestors().Contains(MenuBar));

    /// <summary>Selection, clipboard or undo state of the focused TextBox changed: re-query edit.* (I-153).</summary>
    private void OnTextTargetChanged(object? sender, EventArgs e) => ViewModel?.InvalidateCommands();
}
