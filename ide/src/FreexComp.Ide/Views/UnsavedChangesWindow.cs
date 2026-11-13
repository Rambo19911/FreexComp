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
/// The multi-file Unsaved Changes dialog (dialogs.md §6): question icon, heading <c>Dialog.UnsavedChanges.Text</c>,
/// one check box row per file (<c>Dialog.UnsavedChanges.File.&lt;fileName&gt;</c>: name, start-trimmed folder);
/// Space toggles the focused row, Ctrl+A checks all. 520 × 320, resizable.
/// </summary>
public sealed class UnsavedChangesWindow(UnsavedChangesViewModel model) : DialogWindow(model, Body(model))
{
    private static string Id(string field) => AutomationIds.DialogField(UnsavedChangesViewModel.DialogName, field);

    private static Control Body(UnsavedChangesViewModel model)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,*") };
        var icon = MessageBoxWindow.CreateIcon(MessageKind.Question, Strings.MessageBox_Kind_Question, Id("Icon"));
        icon.Margin = new Thickness(0, 0, 16, 0); // space.6
        Grid.SetRowSpan(icon, 2);
        grid.Children.Add(icon);

        var heading = new TextBlock { Text = model.Heading, TextWrapping = TextWrapping.Wrap };
        heading.Classes.Add("body");
        heading.Bind(TextBlock.ForegroundProperty, heading.GetResourceObservable("FxcTextPrimaryBrush"));
        heading.Margin = new Thickness(0, 0, 0, 8); // space.4
        AutomationProperties.SetAutomationId(heading, Id("Text"));
        Grid.SetColumn(heading, 1);
        grid.Children.Add(heading);

        var rows = new StackPanel();
        foreach (var file in model.Files)
        {
            rows.Children.Add(CreateRow(file));
        }

        var list = new Border
        {
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
        };
        list.Bind(Border.BorderBrushProperty, list.GetResourceObservable("FxcBorderDefaultBrush"));
        list.Bind(Border.BackgroundProperty, list.GetResourceObservable("FxcBgEditorBrush"));
        list.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
            {
                model.CheckAll();
                e.Handled = true;
            }
        });
        Grid.SetColumn(list, 1);
        Grid.SetRow(list, 1);
        grid.Children.Add(list);
        return grid;
    }

    private static CheckBox CreateRow(UnsavedChangesFile file)
    {
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var name = new TextBlock { Text = file.FileName, VerticalAlignment = VerticalAlignment.Center };
        name.Classes.Add("body");
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("FxcTextPrimaryBrush"));
        columns.Children.Add(name);
        if (file.Folder is { } folder)
        {
            var path = new TextBlock
            {
                Text = folder,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.PrefixCharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0), // space.6
            };
            path.Classes.Add("body");
            path.Bind(TextBlock.ForegroundProperty, path.GetResourceObservable("FxcTextTertiaryBrush"));
            ToolTip.SetTip(path, folder);
            Grid.SetColumn(path, 1);
            columns.Children.Add(path);
        }

        var row = new CheckBox
        {
            Content = columns,
            IsChecked = file.IsChecked,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        row.Bind(PaddingProperty, row.GetResourceObservable("FxcSpace2Horizontal"));
        row.IsCheckedChanged += (_, _) => file.IsChecked = row.IsChecked == true;
        file.PropertyChanged += (_, _) => row.IsChecked = file.IsChecked;
        AutomationProperties.SetAutomationId(row, Id("File." + file.FileName));
        AutomationProperties.SetName(row, file.FileName);
        return row;
    }
}
