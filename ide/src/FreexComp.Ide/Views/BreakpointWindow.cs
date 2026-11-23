using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// Add Breakpoint / Breakpoint Properties (dialogs.md §19), 520 × auto: <c>File</c> (with its picker in Add mode),
/// <c>Line</c>, <c>Condition</c>, <c>PassCount</c> with the explanation <c>PassCount.Hint</c> beside it, <c>Enabled</c>;
/// each field's message <c>Error.&lt;Field&gt;</c> under it. Initial focus on <c>Condition</c> in Properties mode, else on <c>File</c>.
/// </summary>
public sealed class BreakpointWindow : DialogWindow
{
    public BreakpointWindow(BreakpointViewModel model)
        : this(model, new DialogFieldBuilder(BreakpointViewModel.DialogName))
    {
    }

    private BreakpointWindow(BreakpointViewModel model, DialogFieldBuilder fields)
        : base(model, Body(model, fields))
    {
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<TextBox>().FirstOrDefault(b => !b.IsReadOnly) is { } box)
                box.Focus(Avalonia.Input.NavigationMethod.Tab);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static StackPanel Body(BreakpointViewModel model, DialogFieldBuilder fields)
    {
        var file = fields.Text("File", () => model.File, v => model.File = v);
        var line = fields.Text("Line", () => model.Line, v => model.Line = v);
        file.IsReadOnly = line.IsReadOnly = model.IsProperties; // UI-DLG-190
        line.Width = 96;
        line.HorizontalAlignment = HorizontalAlignment.Left;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BreakpointViewModel.File) && file.Text != model.File)
                file.Text = model.File; // the picker's choice
        };
        var condition = fields.Text("Condition", () => model.Condition, v => model.Condition = v, code: true);

        var passCount = fields.Text("PassCount", () => model.PassCountText, v => model.PassCount = v);
        passCount.IsReadOnly = model.IsPassCountReadOnly;
        passCount.Width = 96;
        var hint = new TextBlock { Text = model.PassCountHint, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        hint.Classes.Add("caption");
        hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(hint, fields.Id("PassCount.Hint"));
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BreakpointViewModel.PassCountHint))
                hint.Text = model.PassCountHint;
        };

        var enabled = new CheckBox { Content = Strings.Breakpoint_Enabled, IsChecked = model.Enabled, Margin = new Thickness(DialogFieldBuilder.LabelWidth, 0, 0, 0) };
        AutomationProperties.SetAutomationId(enabled, fields.Id("Enabled"));
        enabled.IsCheckedChanged += (_, _) => model.Enabled = enabled.IsChecked == true;

        Control fileField = model.IsProperties ? file : fields.WithBrowse("File", file, model.BrowseAsync);
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Field(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.Breakpoint_File, file), fileField),
                    fields.Message("File", model, () => model.FileError, () => true, file)),
                Field(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.Breakpoint_Line, line), line),
                    fields.Message("Line", model, () => model.LineError, () => true, line)),
                Field(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.Breakpoint_Condition, condition), condition),
                    fields.Message("Condition", model, () => model.ConditionError, () => true, condition)),
                Field(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.Breakpoint_PassCount, passCount),
                        new StackPanel { Orientation = Orientation.Horizontal, Children = { passCount, hint } }),
                    fields.Message("PassCount", model, () => model.PassCountError, () => true, passCount)),
                enabled,
            },
        };
    }

    private static StackPanel Field(Control row, Control message) => new() { Children = { row, message } };
}
