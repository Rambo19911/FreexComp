using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Views;

/// <summary>
/// The New File dialog (dialogs.md §4), 520 × auto, not resizable: <c>Types</c> (rows <c>.Types.&lt;id&gt;</c>),
/// <c>Name</c> with the read-only <c>Extension</c> suffix, <c>Folder</c> with <c>Folder.Browse</c>, <c>AddToProject</c>;
/// messages <c>Error.Name</c> / <c>Error.Folder</c> under their fields. Initial focus on Name (UI-DLG-042).
/// </summary>
public sealed class NewFileWindow : DialogWindow
{
    public NewFileWindow(NewFileViewModel model)
        : this(model, new TextBox())
    {
    }

    private NewFileWindow(NewFileViewModel model, TextBox name)
        : base(model, Body(model, name))
    {
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            name.Focus(Avalonia.Input.NavigationMethod.Tab);
            name.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(NewFileViewModel.DialogName, field);

    private static Control Body(NewFileViewModel model, TextBox name)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
        };

        // Rows 0–1: the type list.
        var types = CreateTypeList(model);
        var typesLabel = CreateLabel(Strings.NewFile_Types, types);
        Grid.SetColumnSpan(typesLabel, 3);
        grid.Children.Add(typesLabel);
        var frame = new Border { BorderThickness = new Thickness(1), Child = types, Margin = new Thickness(0, 4, 0, 12) };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("FxcBgEditorBrush"));
        Grid.SetRow(frame, 1);
        Grid.SetColumnSpan(frame, 3);
        grid.Children.Add(frame);

        // Rows 2–3: Name with the extension suffix.
        name.Text = model.FileBaseName;
        AutomationProperties.SetAutomationId(name, Id("Name"));
        name.TextChanged += (_, _) => model.FileBaseName = name.Text ?? "";
        var extension = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 32 };
        extension.Classes.Add("body");
        extension.Bind(TextBlock.ForegroundProperty, extension.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(extension, Id("Extension"));
        void ShowExtension()
        {
            extension.Text = model.Extension;
            extension.IsVisible = model.Extension is not null;
        }

        ShowExtension();
        AddField(grid, 2, CreateLabel(Strings.NewFile_Name, name), name, extension);
        AddError(grid, 3, "Name", name, model, nameof(NewFileViewModel.NameError), () => model.NameError);

        // Rows 4–5: Folder with the Browse button (UI-DLG-011).
        var folder = new TextBox { Text = model.Folder };
        AutomationProperties.SetAutomationId(folder, Id("Folder"));
        folder.TextChanged += (_, _) => model.Folder = folder.Text ?? "";
        var browse = new Button { Content = "…", Width = 26, Height = 26, Margin = new Thickness(8, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAutomationId(browse, Id("Folder.Browse"));
        AutomationProperties.SetName(browse, Strings.NewProject_Browse);
        browse.Click += async (_, _) => await model.BrowseAsync();
        AddField(grid, 4, CreateLabel(Strings.NewFile_Folder, folder), folder, browse);
        AddError(grid, 5, "Folder", folder, model, nameof(NewFileViewModel.FolderError), () => model.FolderError);

        // Row 6: Add to project (hidden without a project).
        var add = new CheckBox { Content = model.AddToProjectLabel, IsChecked = model.AddToProject, IsVisible = model.ShowAddToProject };
        AutomationProperties.SetAutomationId(add, Id("AddToProject"));
        add.IsCheckedChanged += (_, _) => model.AddToProject = add.IsChecked == true;
        Grid.SetRow(add, 6);
        Grid.SetColumnSpan(add, 3);
        grid.Children.Add(add);

        model.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(NewFileViewModel.FileBaseName) when name.Text != model.FileBaseName:
                    name.Text = model.FileBaseName;
                    break;
                case nameof(NewFileViewModel.Folder) when folder.Text != model.Folder:
                    folder.Text = model.Folder;
                    break;
                case nameof(NewFileViewModel.Extension):
                    ShowExtension();
                    break;
            }
        };
        return grid;
    }

    private static ListBox CreateTypeList(NewFileViewModel model)
    {
        var list = new ListBox
        {
            ItemsSource = model.Types,
            SelectedItem = model.SelectedType,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<NewFileType>((type, _) =>
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,120,*") };
                row.Children.Add(new IdeIcon { IconName = type?.Kind == NewFileKind.Unit ? "unit" : "file", Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) });
                var label = new TextBlock { Text = type?.Label, VerticalAlignment = VerticalAlignment.Center };
                label.Classes.Add("body");
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var description = new TextBlock { Text = type?.Description, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                description.Classes.Add("body");
                description.Bind(TextBlock.ForegroundProperty, description.GetResourceObservable("FxcTextSecondaryBrush"));
                Grid.SetColumn(description, 2);
                row.Children.Add(description);
                return row;
            }),
        };
        AutomationProperties.SetAutomationId(list, Id("Types"));
        list.Focusable = true;
        list.GotFocus += (_, e) =>
        {
            if (e.Source == list && list.ContainerFromItem(model.SelectedType) is ListBoxItem selected)
                selected.Focus(Avalonia.Input.NavigationMethod.Tab);
        };
        list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem item && item.DataContext is NewFileType type)
            {
                AutomationProperties.SetAutomationId(item, Id("Types." + type.Id));
                AutomationProperties.SetName(item, type.Label);
            }
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is NewFileType type)
                model.SelectedType = type;
        };
        return list;
    }

    /// <summary>A label whose mnemonic (<c>_x</c>) focuses <paramref name="target"/> (UI-DLG-004).</summary>
    private static Label CreateLabel(string text, Control target)
    {
        var label = new Label { Content = text, Target = target, Padding = new Thickness(0), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        return label;
    }

    private static void AddField(Grid grid, int row, Control label, Control field, Control trailing)
    {
        Grid.SetRow(label, row);
        grid.Children.Add(label);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        Grid.SetRow(trailing, row);
        Grid.SetColumn(trailing, 2);
        grid.Children.Add(trailing);
    }

    /// <summary>The message under a field (UI-DLG-006): <c>type.caption</c>, <c>status.error</c>; the field gets the Invalid state.</summary>
    private static void AddError(Grid grid, int row, string fieldId, TextBox field, NewFileViewModel model, string property, Func<string?> error)
    {
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
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
        void Show()
        {
            var text = error();
            message.Text = text;
            message.IsVisible = text is not null;
            field.Classes.Set("invalid", text is not null);
        }

        Show();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == property)
                Show();
        };
    }
}
