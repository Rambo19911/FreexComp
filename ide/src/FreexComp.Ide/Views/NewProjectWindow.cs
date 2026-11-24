using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// The New Project dialog (dialogs.md §3), 640 × 400, resizable: template list (<c>Dialog.NewProject.Templates</c>, rows
/// <c>.Templates.&lt;id&gt;</c>) with the description pane (<c>.TemplateDescription</c>), <c>Name</c>, <c>Location</c> with
/// <c>Location.Browse</c>, the read-only <c>TargetPath</c>; messages <c>Error.Name</c> / <c>Error.Location</c> under
/// their fields. Initial focus on Name with its text selected (UI-DLG-033).
/// </summary>
public sealed class NewProjectWindow : DialogWindow
{
    private readonly TextBox _name;

    public NewProjectWindow(NewProjectViewModel model)
        : this(model, new TextBox())
    {
    }

    private NewProjectWindow(NewProjectViewModel model, TextBox name)
        : base(model, Body(model, name))
    {
        _name = name;
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _name.Focus(Avalonia.Input.NavigationMethod.Tab);
            _name.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(NewProjectViewModel.DialogName, field);

    private static Control Body(NewProjectViewModel model, TextBox name)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto,Auto,Auto"),
        };

        // Row 0–1: templates and their description.
        var list = CreateTemplateList(model);
        var templateLabel = CreateLabel(Strings.NewProject_Template, list);
        Grid.SetColumnSpan(templateLabel, 3);
        grid.Children.Add(templateLabel);
        var templates = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*"), Margin = new Thickness(0, 0, 0, 8) };
        templates.Children.Add(Framed(list));
        var description = Framed(CreateDescription(model));
        Grid.SetColumn(description, 1);
        templates.Children.Add(description);
        Grid.SetRow(templates, 1);
        Grid.SetColumnSpan(templates, 3);
        grid.Children.Add(templates);

        // Row 2–3: Name.
        name.Text = model.ProjectName;
        name.MaxLength = NewProjectValidation.MaxNameLength;
        AutomationProperties.SetAutomationId(name, Id("Name"));
        name.TextChanged += (_, _) => model.ProjectName = name.Text ?? "";
        name.LostFocus += (_, _) => model.FieldLeft("Name");
        AddField(grid, 2, CreateLabel(Strings.NewProject_Name, name), name, null);
        AddError(grid, 3, "Name", name, model, nameof(NewProjectViewModel.NameError), () => model.NameError);

        // Row 4–5: Location with the Browse button (UI-DLG-011).
        var location = new TextBox { Text = model.Location };
        AutomationProperties.SetAutomationId(location, Id("Location"));
        location.TextChanged += (_, _) => model.Location = location.Text ?? "";
        location.LostFocus += (_, _) => model.FieldLeft("Location");
        // The 26 × 26 button has no room for the theme's padding: without it the glyph is clipped away (I-256).
        var browse = new Button
        {
            Content = "…", Width = 26, Height = 26, MinWidth = 0, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(browse, Id("Location.Browse"));
        AutomationProperties.SetName(browse, Strings.NewProject_Browse);
        browse.Click += async (_, _) => await model.BrowseAsync();
        AddField(grid, 4, CreateLabel(Strings.NewProject_Location, location), location, browse);
        AddError(grid, 5, "Location", location, model, nameof(NewProjectViewModel.LocationError), () => model.LocationError);

        // Row 6: the target path, live.
        var targetLabel = new TextBlock { Text = Strings.NewProject_TargetPath, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        targetLabel.Classes.Add("body");
        var target = new TextBlock { Text = model.TargetPath, TextTrimming = TextTrimming.PrefixCharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        target.Classes.Add("body");
        target.Bind(TextBlock.ForegroundProperty, target.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(target, Id("TargetPath"));
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NewProjectViewModel.TargetPath))
                target.Text = model.TargetPath;
            else if (e.PropertyName == nameof(NewProjectViewModel.Location) && location.Text != model.Location)
                location.Text = model.Location;
        };
        AddField(grid, 6, targetLabel, target, null);
        return grid;
    }

    private static ListBox CreateTemplateList(NewProjectViewModel model)
    {
        var list = new ListBox
        {
            ItemsSource = model.Templates,
            SelectedItem = model.SelectedTemplate,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<ProjectTemplate>((template, _) =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new IdeIcon { IconName = "project", Width = 16, Height = 16 });
                var text = new TextBlock { Text = template?.Name, VerticalAlignment = VerticalAlignment.Center };
                text.Classes.Add("body");
                row.Children.Add(text);
                return row;
            }),
        };
        AutomationProperties.SetAutomationId(list, Id("Templates"));
        // The &Template mnemonic focuses the list; focus moves on to the selected row.
        list.Focusable = true;
        list.GotFocus += (_, e) =>
        {
            if (e.Source == list && list.ContainerFromItem(model.SelectedTemplate) is ListBoxItem selected)
                selected.Focus(Avalonia.Input.NavigationMethod.Tab);
        };
        list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem item && item.DataContext is ProjectTemplate template)
            {
                AutomationProperties.SetAutomationId(item, Id("Templates." + template.Id));
                AutomationProperties.SetName(item, template.Name);
            }
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ProjectTemplate template)
                model.SelectedTemplate = template;
        };
        return list;
    }

    private static Control CreateDescription(NewProjectViewModel model)
    {
        var title = new TextBlock { TextWrapping = TextWrapping.Wrap };
        title.Classes.Add("subtitle");
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        text.Classes.Add("body");
        var language = new TextBlock { TextWrapping = TextWrapping.Wrap };
        language.Classes.Add("body");
        language.Bind(TextBlock.ForegroundProperty, language.GetResourceObservable("FxcTextSecondaryBrush"));
        void Show()
        {
            title.Text = model.SelectedTemplate.Name;
            text.Text = model.SelectedTemplate.Description;
            language.Text = model.LanguageText;
        }

        Show();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NewProjectViewModel.SelectedTemplate))
                Show();
        };
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(title);
        panel.Children.Add(text);
        panel.Children.Add(language);
        AutomationProperties.SetAutomationId(panel, Id("TemplateDescription"));
        return panel;
    }

    private static Border Framed(Control content)
    {
        var border = new Border { BorderThickness = new Thickness(1), Child = content };
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("FxcBorderDefaultBrush"));
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("FxcBgEditorBrush"));
        return border;
    }

    /// <summary>A label whose mnemonic (<c>_x</c>) focuses <paramref name="target"/> (UI-DLG-004).</summary>
    private static Label CreateLabel(string text, Control target)
    {
        var label = new Label { Content = text, Target = target, Padding = new Thickness(0), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        return label;
    }

    private static void AddField(Grid grid, int row, Control label, Control field, Control? trailing)
    {
        Grid.SetRow(label, row);
        grid.Children.Add(label);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (trailing is null)
            Grid.SetColumnSpan(field, 2);
        grid.Children.Add(field);
        if (trailing is not null)
        {
            Grid.SetRow(trailing, row);
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }
    }

    /// <summary>The message under a field (UI-DLG-006): <c>type.caption</c>, <c>status.error</c>; the field gets the Invalid state.</summary>
    private static void AddError(Grid grid, int row, string fieldId, TextBox field, NewProjectViewModel model, string property, Func<string?> error)
    {
        var message = new TextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
        message.Classes.Add("caption");
        message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable("FxcStatusErrorBrush"));
        AutomationProperties.SetAutomationId(message, Id("Error." + fieldId));
        Grid.SetRow(message, row);
        Grid.SetColumn(message, 1);
        Grid.SetColumnSpan(message, 2);
        grid.Children.Add(message);
        var spacer = new Control { Height = 8 };
        Grid.SetRow(spacer, row);
        grid.Children.Add(spacer);
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != property)
                return;
            var text = error();
            message.Text = text;
            message.IsVisible = text is not null;
            field.Classes.Set("invalid", text is not null);
        };
    }
}
