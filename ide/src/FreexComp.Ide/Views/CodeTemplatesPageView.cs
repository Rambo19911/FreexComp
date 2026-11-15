using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Options page <c>Editor.CodeTemplates</c> (dialogs.md §9.3, I-119): the template grid with Add / Delete / Reset, the
/// selected template's abbreviation, description and body with their messages. The Expand-with-Tab check box is the
/// page's ordinary field below. All logic is in <see cref="CodeTemplatesPageViewModel"/>.
/// </summary>
public static class CodeTemplatesPageView
{
    private static string Id(string field) => OptionsWindow.Id(field);

    public static Control Create(CodeTemplatesPageViewModel model)
    {
        // The grid (Abbreviation 120, Description *) and the buttons at its right.
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), Margin = new Thickness(8, 0, 0, 4) };
        header.Children.Add(Text(Strings.Options_Templates_ColumnAbbreviation, "bodyStrong"));
        var descriptionHeader = Text(Strings.Options_Templates_ColumnDescription, "bodyStrong");
        Grid.SetColumn(descriptionHeader, 1);
        header.Children.Add(descriptionHeader);
        var list = new ListBox
        {
            ItemsSource = model.Rows,
            SelectionMode = SelectionMode.Single,
            Height = 160,
            ItemTemplate = new FuncDataTemplate<CodeTemplateRow>((row, _) =>
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") };
                var abbreviation = Text("", "code");
                abbreviation.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(CodeTemplateRow.Abbreviation)));
                var description = Text("", "body");
                description.TextTrimming = TextTrimming.CharacterEllipsis;
                description.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(CodeTemplateRow.Description)));
                Grid.SetColumn(description, 1);
                grid.Children.Add(abbreviation);
                grid.Children.Add(description);
                return grid;
            }),
        };
        AutomationProperties.SetAutomationId(list, Id("Templates"));
        AutomationProperties.SetName(list, Strings.Options_Templates_Templates.Replace("_", "", StringComparison.Ordinal));
        var listLabel = Mnemonic(Strings.Options_Templates_Templates, list);
        var frame = new Border { BorderThickness = new Thickness(1), Child = list };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));
        var add = CreateButton("Templates.Add", Strings.Options_Templates_Add);
        var delete = CreateButton("Templates.Delete", Strings.Options_Templates_Delete);
        var reset = CreateButton("Templates.Reset", Strings.Options_Templates_Reset);
        var buttons = new StackPanel { Spacing = 8, Margin = new Thickness(8, 0, 0, 0), Children = { add, delete, reset } };
        var top = new DockPanel { Children = { buttons, new StackPanel { Children = { header, frame } } } };
        DockPanel.SetDock(buttons, Dock.Right);

        // The selected template's fields.
        var abbreviationBox = new TextBox { MaxLength = 16, Width = 140 };
        var descriptionBox = new TextBox { MaxLength = 80 };
        var body = new TextBox { AcceptsReturn = true, AcceptsTab = true, MinHeight = 8 * 18 + 8, TextWrapping = TextWrapping.NoWrap };
        body.Classes.Add("code");
        body.Bind(TextBox.FontFamilyProperty, body.GetResourceObservable("FxcFontMono")); // type.code
        foreach (var (box, field, label) in new[] { (abbreviationBox, "Abbreviation", Strings.Options_Templates_Abbreviation), (descriptionBox, "Description", Strings.Options_Templates_Description), (body, "Body", Strings.Options_Templates_Body) })
        {
            AutomationProperties.SetAutomationId(box, Id(field));
            AutomationProperties.SetName(box, label.Replace("_", "", StringComparison.Ordinal).TrimEnd(':'));
        }

        var abbreviationError = Error("Error.Abbreviation");
        var bodyError = Error("Error.Body");
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        var abbreviationLabel = Mnemonic(Strings.Options_Templates_Abbreviation, abbreviationBox);
        var descriptionLabel = Mnemonic(Strings.Options_Templates_Description, descriptionBox);
        descriptionLabel.Margin = new Thickness(16, 0, 8, 0);
        Place(fields, abbreviationLabel, 0, 0);
        Place(fields, abbreviationBox, 0, 1);
        Place(fields, descriptionLabel, 0, 2);
        Place(fields, descriptionBox, 0, 3);
        Place(fields, abbreviationError, 1, 1);
        Grid.SetColumnSpan(abbreviationError, 3);
        var bodyLabel = Mnemonic(Strings.Options_Templates_Body, body);
        bodyLabel.Margin = new Thickness(0, 8, 0, 4);

        var syncing = false;
        void Sync()
        {
            syncing = true;
            try
            {
                var row = model.Selected;
                list.SelectedItem = row;
                abbreviationBox.Text = row?.Abbreviation ?? "";
                descriptionBox.Text = row?.Description ?? "";
                body.Text = row?.Body ?? "";
                abbreviationBox.IsEnabled = descriptionBox.IsEnabled = body.IsEnabled = row is not null;
                abbreviationError.Text = model.AbbreviationError ?? "";
                bodyError.Text = model.BodyError ?? "";
                delete.IsEnabled = model.CanDelete;
                reset.IsEnabled = model.CanReset;
            }
            finally
            {
                syncing = false;
            }
        }

        list.SelectionChanged += (_, _) =>
        {
            if (!syncing && list.SelectedItem is CodeTemplateRow row)
                model.Selected = row;
        };
        abbreviationBox.TextChanged += (_, _) =>
        {
            if (!syncing && model.Selected is { } row)
                row.Abbreviation = abbreviationBox.Text ?? "";
        };
        descriptionBox.TextChanged += (_, _) =>
        {
            if (!syncing && model.Selected is { } row)
                row.Description = descriptionBox.Text ?? "";
        };
        body.TextChanged += (_, _) =>
        {
            if (!syncing && model.Selected is { } row)
                row.Body = (body.Text ?? "").ReplaceLineEndings("\n");
        };
        add.Click += (_, _) =>
        {
            model.AddTemplate();
            abbreviationBox.Focus(); // dialogs.md §9.3: focuses Abbreviation
        };
        delete.Click += (_, _) => model.DeleteTemplate();
        reset.Click += (_, _) =>
        {
            model.ResetTemplate();
            Sync(); // the restored text fills the fields
        };
        model.PropertyChanged += (_, e) =>
        {
            if (syncing)
                return;
            if (e.PropertyName == nameof(CodeTemplatesPageViewModel.Selected))
                Sync(); // a new selection fills the fields
            else if (e.PropertyName == nameof(CodeTemplatesPageViewModel.CanReset))
                SyncState(); // while typing only the messages and buttons follow
        };

        void SyncState()
        {
            abbreviationError.Text = model.AbbreviationError ?? "";
            bodyError.Text = model.BodyError ?? "";
            delete.IsEnabled = model.CanDelete;
            reset.IsEnabled = model.CanReset;
        }

        Sync();
        return new StackPanel { Margin = new Thickness(0, 0, 0, 12), Children = { listLabel, top, fields, bodyLabel, body, bodyError } };
    }

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static Label Mnemonic(string text, Control target)
    {
        var label = new Label { Content = text, Target = target, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(TemplatedControl.ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        return label;
    }

    private static TextBlock Error(string id)
    {
        var block = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("FxcStatusErrorBrush"));
        AutomationProperties.SetAutomationId(block, Id(id));
        return block;
    }

    private static TextBlock Text(string text, string textClass)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.NoWrap };
        block.Classes.Add(textClass);
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("FxcTextPrimaryBrush"));
        return block;
    }

    private static Button CreateButton(string field, string label)
    {
        var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Bind(Layoutable.MinWidthProperty, button.GetResourceObservable("FxcMetricButtonMinWidth"));
        button.Bind(Layoutable.HeightProperty, button.GetResourceObservable("FxcMetricButtonHeight"));
        AutomationProperties.SetAutomationId(button, Id(field));
        AutomationProperties.SetName(button, label.Replace("_", "", StringComparison.Ordinal));
        return button;
    }
}
