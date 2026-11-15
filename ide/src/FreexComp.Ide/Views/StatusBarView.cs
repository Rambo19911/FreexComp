using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.ViewModels;
using IdeCommand = FreexComp.Ide.Core.Commands.IdeCommand;

namespace FreexComp.Ide.Views;

/// <summary>
/// Region 8 (main-window.md §5): the left group (state, message) and the right group (the active editor's fields),
/// fields separated by 1 px <c>border.subtle</c> lines with <c>space.4</c> padding, text <c>type.caption</c>. Clickable
/// fields are flat buttons with a tooltip naming the action (UI-WIN-042). Like the toolbar row, a click leaves focus
/// where it was (the commands act on the editor); the bar is one stop of the <c>F6</c> cycle, arrow keys move between
/// its buttons and Escape returns to the document.
/// </summary>
public sealed class StatusBarView : DockPanel
{
    private readonly Button _state;
    private readonly Button _diagnostics;
    private readonly Button _configuration;
    private readonly Border _diagnosticsCell;
    private readonly Border _configurationCell;
    private readonly TextBlock _message = Text(AutomationIds.StatusBarField("Message"));
    private readonly Button _caret;
    private readonly TextBlock _selection = Text(AutomationIds.StatusBarField("Selection"));
    private readonly Button _insertMode;
    private readonly Button _indent;
    private readonly Button _lineEnding;
    private readonly Button _encoding;
    private readonly TextBlock _language = Text(AutomationIds.StatusBarField("Language"));
    private readonly List<Button> _buttons = [];
    private readonly List<Control> _editorFields = [];
    private StatusBarViewModel? _model;
    private StatusBarState _stateKind;
    private IDisposable? _backgroundBinding; // statusbar.<state>.bg follows the IDE state (states-and-messages.md §9)

    public StatusBarView()
    {
        AutomationProperties.SetAutomationId(this, AutomationIds.StatusBar);
        Height = Metric("FxcMetricStatusBarHeight");
        _backgroundBinding = Bind(BackgroundProperty, this.GetResourceObservable("FxcStatusbarIdleBgBrush"));
        LastChildFill = true;

        _state = FieldButton("State", m => m.StateCommand);
        _caret = FieldButton("CaretPosition", m => m.CaretCommand);
        _caret.MinWidth = 96;
        _insertMode = FieldButton("InsertMode", m => m.InsertModeCommand);
        _insertMode.MinWidth = 36;
        _indent = FieldButton("Indent", m => m.IndentCommand);
        _lineEnding = FieldButton("LineEnding", m => m.LineEndingCommand, OpenLineEndingMenu);
        _lineEnding.MinWidth = 44;
        _encoding = FieldButton("Encoding", m => m.EncodingCommand, OpenEncodingMenu);
        _diagnostics = FieldButton("Diagnostics", m => m.StateCommand);
        _configuration = FieldButton("Configuration", m => m.ConfigurationCommand, OpenConfigurationMenu);
        ((TextBlock)_state.Content!).MaxWidth = 360;

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Field(_state, lineBefore: false));
        SetDock(left, Avalonia.Controls.Dock.Left);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        _diagnosticsCell = Field(_diagnostics, lineBefore: true);
        right.Children.Add(_diagnosticsCell);
        foreach (var field in new Control[] { _caret, _selection, _insertMode, _indent, _lineEnding, _encoding, _language })
        {
            var cell = Field(field, lineBefore: true);
            _editorFields.Add(cell);
            right.Children.Add(cell);
        }

        _configurationCell = Field(_configuration, lineBefore: true);
        right.Children.Add(_configurationCell);
        SetDock(right, Avalonia.Controls.Dock.Right);
        var message = Field(_message, lineBefore: true);
        Children.Add(left);
        Children.Add(right);
        Children.Add(message);

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, (_, _) => Dispatcher.UIThread.Post(LeaveIfFocusOutside), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Escape in the status bar: focus goes back to the document.</summary>
    public event EventHandler? EscapeRequested;

    /// <summary>
    /// The <c>F6</c> cycle enters the bar: its buttons become focusable while focus is inside, the first one gets it.
    /// False when no button can take focus.
    /// </summary>
    public bool FocusFirstField()
    {
        foreach (var button in _buttons)
        {
            button.Focusable = true;
        }

        if (KeyboardButtons() is [var first, ..] && first.Focus(NavigationMethod.Tab))
        {
            return true;
        }

        LeaveIfFocusOutside();
        return false;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _model?.PropertyChanged -= OnModelChanged;
        _model = DataContext as StatusBarViewModel;
        _model?.PropertyChanged += OnModelChanged;
        Refresh();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_model is not { } m)
        {
            return;
        }

        IsVisible = m.IsVisible;
        if (m.StateKind != _stateKind || _backgroundBinding is null)
        {
            _stateKind = m.StateKind;
            _backgroundBinding?.Dispose();
            _backgroundBinding = Bind(BackgroundProperty, this.GetResourceObservable($"FxcStatusbar{_stateKind}BgBrush"));
        }

        SetText(_state, m.StateText);
        _message.Text = m.Message;
        foreach (var cell in _editorFields)
        {
            cell.IsVisible = m.HasEditor;
        }

        SetText(_caret, m.CaretText);
        _selection.Text = m.SelectionText;
        ((Control)_selection.Parent!).IsVisible = m.HasEditor && m.HasSelection;
        SetText(_insertMode, m.InsertModeText);
        SetText(_indent, m.IndentText);
        SetText(_lineEnding, m.LineEndingText);
        SetText(_encoding, m.EncodingText);
        _language.Text = m.LanguageText;
        SetText(_diagnostics, m.DiagnosticsText);
        _diagnosticsCell.IsVisible = m.HasDiagnostics;
        SetText(_configuration, m.ConfigurationText);
        AutomationProperties.SetName(_configuration, m.ConfigurationText.Length > 0 ? m.ConfigurationText : m.ConfigurationCommand.Title);
        _configurationCell.IsVisible = m.HasProject;
        foreach (var button in _buttons)
        {
            ToolTip.SetTip(button, CommandOf(button)?.Title);
        }
    }

    private Button FieldButton(string name, Func<StatusBarViewModel, IdeCommand> command, Action<Button>? openMenu = null)
    {
        var button = new StatusFieldButton
        {
            Content = Text(null),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false, // a click leaves focus where it was (as in the toolbar row, I-153)
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = command,
        };
        AutomationProperties.SetAutomationId(button, AutomationIds.StatusBarField(name));
        button.Click += (_, _) =>
        {
            if (openMenu is not null)
            {
                openMenu(button);
            }
            else if (CommandOf(button) is { } c)
            {
                StatusBarViewModel.Activate(c);
            }
        };
        _buttons.Add(button);
        return button;
    }

    /// <summary>R6 (main-window.md section 5): <c>edit.convertLineEndings:crlf</c> / <c>:lf</c>.</summary>
    /// <summary>R9: the configurations of the project, a bullet on the active one (IDE-PM-090).</summary>
    private void OpenConfigurationMenu(Button button)
    {
        if (_model is not { } m)
        {
            return;
        }

        var menu = new ContextMenu();
        var active = m.ConfigurationText.Split(" | ")[0];
        foreach (var name in m.Configurations)
        {
            menu.Items.Add(CommandItem(m.ConfigurationCommand, name, name, isChecked: name == active));
        }

        ShowMenu(button, menu);
    }

    private void OpenLineEndingMenu(Button button)
    {
        if (_model is not { } m)
        {
            return;
        }

        var menu = new ContextMenu();
        foreach (var (argument, label) in StatusBarViewModel.LineEndingArguments)
        {
            menu.Items.Add(CommandItem(m.LineEndingCommand, argument, label, isChecked: null));
        }

        ShowMenu(button, menu);
    }

    /// <summary>R7: "Reopen with Encoding…", then "Save with Encoding" with a bullet on the current encoding.</summary>
    private void OpenEncodingMenu(Button button)
    {
        if (_model is not { } m)
        {
            return;
        }

        var reopen = new MenuItem { Header = Core.Resources.Strings.StatusBar_ReopenWithEncoding, IsEnabled = m.CanReopenWithEncoding };
        AutomationProperties.SetAutomationId(reopen, AutomationIds.StatusBarField("Encoding.Reopen"));
        reopen.Click += async (_, _) => await m.ReopenWithEncodingAsync();
        var saveWith = new MenuItem { Header = Core.Resources.Strings.StatusBar_SaveWithEncoding };
        AutomationProperties.SetAutomationId(saveWith, AutomationIds.StatusBarField("Encoding.SaveWith"));
        foreach (var (argument, label) in StatusBarViewModel.EncodingArguments)
        {
            saveWith.Items.Add(CommandItem(m.EncodingCommand, argument, label.Replace("_", "__", StringComparison.Ordinal), isChecked: argument == m.EncodingId));
        }

        var menu = new ContextMenu();
        menu.Items.Add(reopen);
        menu.Items.Add(saveWith);
        ShowMenu(button, menu);
    }

    /// <summary>A command item (<c>Context.&lt;commandId&gt;:&lt;argument&gt;</c>); <paramref name="label"/> marks its mnemonic with <c>&amp;</c>.</summary>
    private static MenuItem CommandItem(IdeCommand command, string argument, string label, bool? isChecked)
    {
        var item = new MenuItem { Header = label.Replace('&', '_'), Command = command, CommandParameter = argument };
        if (isChecked is { } value)
        {
            item.ToggleType = MenuItemToggleType.Radio;
            item.IsChecked = value;
        }

        AutomationProperties.SetAutomationId(item, AutomationIds.Context(command.Id, argument));
        return item;
    }

    private static void ShowMenu(Button button, ContextMenu menu)
    {
        button.ContextMenu = menu;
        menu.Open(button);
    }

    private IdeCommand? CommandOf(Button button) =>
        _model is { } m && button.Tag is Func<StatusBarViewModel, IdeCommand> select ? select(m) : null;

    /// <summary>A field cell: <c>space.4</c> padding each side, a 1 px <c>border.subtle</c> line before it.</summary>
    private static Border Field(Control content, bool lineBefore)
    {
        var padding = Metric("FxcSpace4");
        var cell = new Border
        {
            Child = content,
            Padding = new Thickness(padding, 0),
            BorderThickness = new Thickness(lineBefore ? 1 : 0, 0, 0, 0),
        };
        cell.Bind(Border.BorderBrushProperty, cell.GetResourceObservable("FxcBorderSubtleBrush"));
        return cell;
    }

    private static TextBlock Text(string? automationId)
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Classes.Add("caption");
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("FxcStatusbarFgBrush"));
        if (automationId is not null)
        {
            AutomationProperties.SetAutomationId(text, automationId);
        }

        return text;
    }

    private static void SetText(Button button, string text) => ((TextBlock)button.Content!).Text = text;

    /// <summary>Buttons that can take focus now, left to right.</summary>
    private List<Button> KeyboardButtons() => [.. _buttons.Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled)];

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Button current
            || !_buttons.Contains(current))
        {
            return;
        }

        var buttons = KeyboardButtons();
        var index = buttons.IndexOf(current);
        switch (e.Key)
        {
            case Key.Left when index > 0:
                buttons[index - 1].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Right when index >= 0 && index < buttons.Count - 1:
                buttons[index + 1].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Left or Key.Right:
                e.Handled = true; // at either end: stays
                break;
            case Key.Escape:
                EscapeRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void LeaveIfFocusOutside()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is null || !this.IsVisualAncestorOf(focused))
        {
            foreach (var button in _buttons)
            {
                button.Focusable = false;
            }
        }
    }

    private static double Metric(string key) => Application.Current?.FindResource(key) as double? ?? 0;
}

/// <summary>
/// A clickable status-bar field: transient chrome for command routing (I-153), so while it has keyboard focus the
/// commands keep acting on what had focus before (the editor).
/// </summary>
public sealed class StatusFieldButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
}
