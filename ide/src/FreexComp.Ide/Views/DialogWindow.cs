using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Dialogs;

namespace FreexComp.Ide.Views;

/// <summary>
/// Base window of every dialog (dialogs.md §1): modal, centred on the owner, not in the task bar, fixed size unless
/// the view-model says otherwise (UI-DLG-001/005); body plus the button strip (UI-DLG-003); Enter = default button,
/// Escape and the title-bar close = Cancel, focus trapped, initial focus on the first editable field
/// (UI-DLG-004, UI-A11Y-008). The window closes when the view-model asks to. Dialog windows are named
/// <c>&lt;X&gt;Window</c> for <c>&lt;X&gt;ViewModel</c> and take the view-model in their constructor.
/// </summary>
public class DialogWindow : Window
{
    private readonly Control _body;

    public DialogWindow(DialogViewModel model, Control body)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(body);
        Model = model;
        _body = body;
        Title = model.Title;
        AutomationProperties.SetAutomationId(this, AutomationIds.Dialog(model.Name));
        CanResize = model.CanResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = model.Width;
        if (model.Height is { } height)
            Height = height;
        else
            SizeToContent = SizeToContent.Height;
        if (model.CanResize)
        {
            // UI-DLG-005: the stated size is the minimum; the last size is remembered.
            MinWidth = model.Width;
            MinHeight = model.Height ?? 0;
            if (model.StoredSize is { } stored)
            {
                Width = Math.Max(stored.Width, MinWidth);
                Height = Math.Max(stored.Height, MinHeight);
            }

            Closed += (_, _) => model.StoredSize = (Width, Height);
        }
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        this.Bind(BackgroundProperty, this.GetResourceObservable("FxcBgPanelBrush"));

        var root = new DockPanel();
        var strip = CreateButtonStrip();
        DockPanel.SetDock(strip, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(strip);
        body.Bind(MarginProperty, body.GetResourceObservable("FxcSpace6All"));
        root.Children.Add(body);
        Content = root;

        model.CloseRequested += (_, _) => Close();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            FocusInitial();
            // A field whose template is not applied yet cannot take focus on Opened: retry once loaded.
            Dispatcher.UIThread.Post(() =>
            {
                if (FocusManager?.GetFocusedElement() is not Visual focused || TopLevel.GetTopLevel(focused) != this)
                    FocusInitial();
            }, DispatcherPriority.Loaded);
        };
    }

    public DialogViewModel Model { get; }

    /// <summary>AutomationId of a strip button: <c>Dialog.&lt;Name&gt;.&lt;Id&gt;</c>.</summary>
    protected virtual string ButtonAutomationId(DialogButtonViewModel button) => AutomationIds.DialogField(Model.Name, button.Id);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (!Model.IsClosed)
        {
            e.Cancel = true; // the title-bar close equals Escape (UI-DLG-004)
            _ = Model.PressCancelAsync();
        }
    }

    private Control CreateButtonStrip()
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        left.Bind(StackPanel.SpacingProperty, left.GetResourceObservable("FxcSpace4"));
        right.Bind(StackPanel.SpacingProperty, right.GetResourceObservable("FxcSpace4"));
        foreach (var button in Model.Buttons)
        {
            (button.IsAuxiliary ? left : right).Children.Add(CreateButton(button));
        }

        var grid = new Grid();
        grid.Children.Add(left);
        grid.Children.Add(right);
        var strip = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = grid };
        strip.Bind(Border.BorderBrushProperty, strip.GetResourceObservable("FxcBorderSubtleBrush"));
        strip.Bind(Border.PaddingProperty, strip.GetResourceObservable("FxcSpace4All"));
        return strip;
    }

    private Button CreateButton(DialogButtonViewModel model)
    {
        var button = new Button { Content = model.AccessLabel, IsEnabled = model.IsEnabled, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (model.IsAccent)
            button.Classes.Add("accent");
        button.Bind(MinWidthProperty, button.GetResourceObservable("FxcMetricButtonMinWidth"));
        button.Bind(HeightProperty, button.GetResourceObservable("FxcMetricButtonHeight"));
        AutomationProperties.SetAutomationId(button, ButtonAutomationId(model));
        AutomationProperties.SetName(button, model.Label);
        button.Click += async (_, _) => await model.PressAsync();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DialogButtonViewModel.IsEnabled))
                button.IsEnabled = model.IsEnabled;
        };
        return button;
    }

    /// <summary>UI-DLG-004: Enter = default button unless a multi-line field or another button handles it; Escape = Cancel.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None)
            return;

        if (e.Key == Key.Enter)
        {
            if (FocusManager?.GetFocusedElement() is TextBox { AcceptsReturn: true })
                return;
            e.Handled = true;
            _ = Model.PressDefaultAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _ = Model.PressCancelAsync();
        }
    }

    /// <summary>
    /// UI-DLG-004: a check box or radio button would take Enter as a click (toggling itself), a closed combo box would
    /// swallow it; in a dialog Enter is the default button there too.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None
            && FocusManager?.GetFocusedElement() is Avalonia.Controls.Primitives.ToggleButton or ComboBox { IsDropDownOpen: false })
        {
            e.Handled = true;
            _ = Model.PressDefaultAsync();
        }
    }

    /// <summary>UI-A11Y-008: the first editable field, else the default button.</summary>
    private void FocusInitial()
    {
        var focusable = _body.GetVisualDescendants().OfType<InputElement>()
            .Where(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible);
        var field = focusable.FirstOrDefault(c => c is TextBox or ComboBox or CheckBox or RadioButton or ListBox or NumericUpDown);
        var target = field
            ?? FindButton(Model.DefaultButton)
            ?? FindButton(Model.Buttons.FirstOrDefault());
        target?.Focus(NavigationMethod.Tab);
    }

    private Button? FindButton(DialogButtonViewModel? model) =>
        model is null ? null : this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => AutomationIds.Get(b) == ButtonAutomationId(model) && b.IsEffectivelyEnabled);
}
