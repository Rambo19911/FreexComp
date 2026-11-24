using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Rename File dialog (dialogs.md §7), 420 × auto: <c>NewName</c> with its message <c>Error.NewName</c> under it and,
/// for Pascal units, <c>UpdateReferences</c> (heading, uses clauses and qualified names, IDE-PM-130). Initial focus on the name with
/// the base name selected (UI-DLG-071).
/// </summary>
public sealed class RenameFileWindow : DialogWindow
{
    public RenameFileWindow(RenameFileViewModel model)
        : this(model, new TextBox())
    {
    }

    private RenameFileWindow(RenameFileViewModel model, TextBox name)
        : base(model, Body(model, name))
    {
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            name.Focus(Avalonia.Input.NavigationMethod.Tab);
            name.SelectionStart = 0;
            name.SelectionEnd = model.BaseNameLength;
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(RenameFileViewModel.DialogName, field);

    private static StackPanel Body(RenameFileViewModel model, TextBox name)
    {
        name.Text = model.NewName;
        AutomationProperties.SetAutomationId(name, Id("NewName"));
        name.TextChanged += (_, _) => model.NewName = name.Text ?? "";
        var label = new Label { Content = Strings.RenameFile_NewName, Target = name, Padding = new Thickness(0), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(label);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        var message = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        message.Classes.Add("caption");
        message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable("FxcStatusErrorBrush"));
        AutomationProperties.SetAutomationId(message, Id("Error.NewName"));
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(RenameFileViewModel.Error))
                return;
            message.Text = model.Error;
            message.IsVisible = model.Error is not null;
            name.Classes.Set("invalid", model.Error is not null);
        };

        var panel = new StackPanel { Spacing = 8, Children = { row, message } };
        if (model.IsUnit)
        {
            var update = new CheckBox { Content = Strings.RenameFile_UpdateReferences, IsChecked = model.UpdateReferences };
            AutomationProperties.SetAutomationId(update, Id("UpdateReferences"));
            update.IsCheckedChanged += (_, _) => model.UpdateReferences = update.IsChecked == true;
            panel.Children.Add(update);
        }

        return panel;
    }
}
