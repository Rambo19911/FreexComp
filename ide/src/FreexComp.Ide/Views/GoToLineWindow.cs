using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Go to Line dialog (dialogs.md §12), 320 × auto: <c>Line</c> with its message <c>Error.Line</c> under it. Initial
/// focus on the field with its text (the caret line) selected (UI-DLG-009).
/// </summary>
public sealed class GoToLineWindow : DialogWindow
{
    public GoToLineWindow(GoToLineViewModel model)
        : this(model, new TextBox())
    {
    }

    private GoToLineWindow(GoToLineViewModel model, TextBox line)
        : base(model, Body(model, line))
    {
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            line.Focus(Avalonia.Input.NavigationMethod.Tab);
            line.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(GoToLineViewModel.DialogName, field);

    private static StackPanel Body(GoToLineViewModel model, TextBox line)
    {
        line.Text = model.Line;
        AutomationProperties.SetAutomationId(line, Id("Line"));
        line.TextChanged += (_, _) => model.Line = line.Text ?? "";
        var label = new Label { Content = model.Label, Target = line, Padding = new Thickness(0), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(label);
        Grid.SetColumn(line, 1);
        row.Children.Add(line);

        var message = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        message.Classes.Add("caption");
        message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable("FxcStatusErrorBrush"));
        AutomationProperties.SetAutomationId(message, Id("Error.Line"));
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(GoToLineViewModel.Error))
                return;
            message.Text = model.Error;
            message.IsVisible = model.Error is not null;
            line.Classes.Set("invalid", model.Error is not null);
        };

        return new StackPanel { Spacing = 8, Children = { row, message } };
    }
}
