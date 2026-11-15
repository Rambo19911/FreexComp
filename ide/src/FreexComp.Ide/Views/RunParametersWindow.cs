using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>The Run Parameters dialog (dialogs.md §22), 560 × auto. Initial focus on <c>Arguments</c>.</summary>
public sealed class RunParametersWindow : DialogWindow
{
    public RunParametersWindow(RunParametersViewModel model)
        : this(model, new DialogFieldBuilder(RunParametersViewModel.DialogName))
    {
    }

    private RunParametersWindow(RunParametersViewModel model, DialogFieldBuilder fields)
        : base(model, Fields(fields, model.Fields, model.BrowseAsync))
    {
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<TextBox>().FirstOrDefault() is { } box)
                box.Focus(Avalonia.Input.NavigationMethod.Tab);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The §22 fields, shared with the Run page of Project Options (§8.6).</summary>
    internal static StackPanel Fields(DialogFieldBuilder fields, RunFields model, Func<Task> browse)
    {
        var arguments = fields.Text("Arguments", () => model.Arguments, v => model.Arguments = v, code: true);
        var working = fields.Text("WorkingDir", () => model.WorkingDir, v => model.WorkingDir = v);
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunFields.WorkingDir) && working.Text != model.WorkingDir)
                working.Text = model.WorkingDir;
        };
        var hint = new TextBlock { Text = Strings.Msg_RunParameters_WorkingDirHint, Margin = new Thickness(DialogFieldBuilder.LabelWidth, 2, 0, 0) };
        hint.Classes.Add("caption");
        hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("FxcTextSecondaryBrush"));
        Avalonia.Automation.AutomationProperties.SetAutomationId(hint, fields.Id("WorkingDir.Hint"));
        var commandLine = fields.ReadOnly("CommandLine", model.CommandLine, code: true);
        commandLine.TextTrimming = TextTrimming.LeadingCharacterEllipsis;
        model.PropertyChanged += (_, _) =>
        {
            commandLine.Text = model.CommandLine;
            ToolTip.SetTip(commandLine, model.CommandLine);
        };
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.RunParameters_Arguments, arguments), arguments),
                new StackPanel
                {
                    Children =
                    {
                        DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.RunParameters_WorkingDir, working), fields.WithBrowse("WorkingDir", working, browse)),
                        hint,
                        fields.Message("WorkingDir", model, () => model.WorkingDirMessage, () => model.WorkingDirIsError, working),
                    },
                },
                DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.RunParameters_CommandLine, null), commandLine),
            },
        };
    }
}
