using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// Evaluate / Modify (dialogs.md §21), 560 × 360, resizable (the result box grows): <c>Expression</c> with the
/// <c>Evaluate</c> button, the read-only <c>Result</c> box (the evaluator's message in <c>status.error</c>), <c>Type</c>,
/// <c>NewValue</c> with the <c>Modify</c> button and its message <c>Error.NewValue</c>. Enter in <c>Expression</c>
/// evaluates, Enter in <c>NewValue</c> modifies. Initial focus on <c>Expression</c> with its text selected.
/// </summary>
public sealed class EvaluateWindow : DialogWindow
{
    private const double ButtonWidth = 88;

    public EvaluateWindow(EvaluateViewModel model)
        : this(model, new DialogFieldBuilder(EvaluateViewModel.DialogName))
    {
    }

    private EvaluateWindow(EvaluateViewModel model, DialogFieldBuilder fields)
        : this(model, fields, fields.Text("Expression", () => model.Expression, v => model.Expression = v, code: true))
    {
    }

    private EvaluateWindow(EvaluateViewModel model, DialogFieldBuilder fields, TextBox expression)
        : base(model, Body(model, fields, expression))
    {
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            expression.Focus(NavigationMethod.Tab);
            expression.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static Grid Body(EvaluateViewModel model, DialogFieldBuilder fields, TextBox expression)
    {
        History.Attach(expression, model.History);
        var evaluate = ActionButton(fields, "Evaluate", Strings.Evaluate_Evaluate, model.EvaluateAsync);
        OnEnter(expression, model.EvaluateAsync);

        var result = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 76, // four rows of type.code
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        result.Classes.Add("code");
        AutomationProperties.SetAutomationId(result, fields.Id("Result"));
        AutomationProperties.SetName(result, Strings.Evaluate_Result.TrimEnd(':'));
        var resultLabel = DialogFieldBuilder.Label(Strings.Evaluate_Result, null);
        var type = fields.ReadOnly("Type", model.Type);
        var typeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { DialogFieldBuilder.Label(Strings.Evaluate_Type, null), type } };

        var newValue = fields.Text("NewValue", () => model.NewValue, v => model.NewValue = v, code: true);
        var modify = ActionButton(fields, "Modify", Strings.Evaluate_Modify, model.ModifyAsync);
        OnEnter(newValue, model.ModifyAsync);
        var modifyMessage = fields.Message("NewValue", model, () => model.ModifyError, () => true, newValue);

        void Update()
        {
            evaluate.IsEnabled = model.CanEvaluate;
            result.Text = model.ResultText;
            result.Classes.Set("error", model.ResultIsError);
            result.Bind(TextBox.ForegroundProperty, result.GetResourceObservable(model.ResultIsError ? "FxcStatusErrorBrush" : "FxcTextPrimaryBrush"));
            type.Text = model.Type;
            newValue.IsEnabled = model.CanEditNewValue;
            modify.IsEnabled = model.CanModify;
        }

        model.PropertyChanged += (_, _) => Update();
        Update();

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto"), RowSpacing = 8 };
        Add(grid, 0, WithButton(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.Evaluate_Expression, expression), expression), evaluate));
        Add(grid, 1, resultLabel);
        Add(grid, 2, result);
        Add(grid, 3, typeRow);
        Add(grid, 4, WithButton(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.Evaluate_NewValue, newValue), newValue), modify));
        Add(grid, 5, modifyMessage);
        return grid;
    }

    private static void Add(Grid grid, int row, Control control)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private static DockPanel WithButton(Control field, Button button)
    {
        var panel = new DockPanel();
        DockPanel.SetDock(button, Avalonia.Controls.Dock.Right);
        panel.Children.Add(button);
        panel.Children.Add(field);
        return panel;
    }

    private static Button ActionButton(DialogFieldBuilder fields, string id, string accessLabel, Func<Task> action)
    {
        var button = new Button
        {
            Content = new Avalonia.Controls.Primitives.AccessText { Text = accessLabel },
            MinWidth = ButtonWidth,
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(button, fields.Id(id));
        AutomationProperties.SetName(button, accessLabel.Replace("_", "", StringComparison.Ordinal));
        button.Click += async (_, _) => await action();
        return button;
    }

    /// <summary>dialogs.md §21: the button beside a field is the default button while focus is in that field.</summary>
    private static void OnEnter(TextBox box, Func<Task> action) => box.AddHandler(KeyDownEvent, async (_, e) =>
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
            return;
        e.Handled = true;
        await action();
    }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
}
