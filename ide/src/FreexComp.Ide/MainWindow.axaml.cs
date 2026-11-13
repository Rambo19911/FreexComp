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
        ToolBars.EscapeRequested += (_, _) => ViewModel?.Layout.FocusPane(Core.Models.Pane.Documents); // UI-TB-008
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
