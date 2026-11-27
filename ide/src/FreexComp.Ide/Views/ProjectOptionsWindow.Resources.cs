using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The field <c>Icon</c> of the page <c>Application</c> (UI-DLG-331) and the pages <c>Resources</c> (UI-DLG-332, 333) and
/// <c>Version Info</c> (UI-DLG-334) of the Project Options dialog.
/// </summary>
public sealed partial class ProjectOptionsWindow
{
    /// <summary>UI-DLG-331: side of the icon preview.</summary>
    internal const double IconPreviewSize = 32;

    /// <summary>UI-DLG-333: height of the resource preview.</summary>
    internal const double ResourcePreviewHeight = 160;

    /// <summary>The row <c>Icon</c>: path, Browse, Clear and the 32 × 32 preview, with its message line.</summary>
    private static Control IconField(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        var box = f.Text("Icon", () => model.Icon, v => model.Icon = v);
        var clear = new Button { Content = Strings.ProjectOptions_IconClear, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(clear, f.Id("Icon.Clear"));
        clear.Click += (_, _) => model.ClearIcon();
        var image = new Image { Width = IconPreviewSize, Height = IconPreviewSize, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        AutomationProperties.SetAutomationId(image, f.Id("Icon.Image"));
        var frame = new Border { Width = IconPreviewSize + 2, Height = IconPreviewSize + 2, BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0), Child = image };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));
        AutomationProperties.SetAutomationId(frame, f.Id("Icon.Preview"));
        AutomationProperties.SetName(frame, Strings.ProjectOptions_Icon.Replace("_", "", StringComparison.Ordinal).TrimEnd(':'));
        var shown = (ResourceImage?)null;
        void Sync()
        {
            if (box.Text != model.Icon)
                box.Text = model.Icon;
            if (ReferenceEquals(shown, model.IconImage))
                return;
            shown = model.IconImage;
            image.Source = Decode(shown);
        }

        model.PropertyChanged += (_, _) => Sync();
        Sync();
        var row = new DockPanel();
        foreach (var right in (Control[])[frame, clear])
        {
            DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);
            row.Children.Add(right);
        }

        row.Children.Add(f.WithBrowse("Icon", box, model.BrowseIconAsync));
        return new StackPanel
        {
            Children =
            {
                DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_Icon, box), row),
                f.Message("Icon", model, () => model.IconError, () => true, box),
            },
        };
    }

    /// <summary>UI-DLG-332, 333: the grid <c>ResourceList</c> with its buttons, the cell message line and the preview.</summary>
    private static Control ResourcesPage(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        // Not virtualized: every row exists with its cells (a project lists a few resources).
        var list = new ListBox
        {
            Height = 150,
            SelectionMode = SelectionMode.Multiple,
            ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new StackPanel()),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AutomationProperties.SetAutomationId(list, f.Id("ResourceList"));
        AutomationProperties.SetName(list, Strings.ProjectOptions_Page_Resources);
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ResourceRow>((row, _) => row is null ? new Panel() : RowCells(f, model, list, row),
            supportsRecycling: false);
        list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is not ListBoxItem item || item.DataContext is not ResourceRow row)
                return;
            item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            item.Padding = new Thickness(12, 2); // compact rows: the cells are the editors
            AutomationProperties.SetAutomationId(item, f.Id("ResourceList.Row." + model.ResourceList.IndexOf(row)));
            AutomationProperties.SetName(item, row.Name + " " + row.Type + " " + row.Path);
        };

        var header = new Grid { ColumnDefinitions = Columns(), Height = 24, Margin = new Thickness(12, 0) };
        foreach (var (text, field, column) in (ReadOnlySpan<(string, string, int)>)[(Strings.ProjectOptions_ResourceName, "Name", 0),
                     (Strings.ProjectOptions_ResourceType, "Type", 1), (Strings.ProjectOptions_ResourceFile, "File", 2)])
        {
            var cell = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            cell.Classes.Add("caption");
            cell.Bind(TextBlock.ForegroundProperty, cell.GetResourceObservable("FxcTextSecondaryBrush"));
            AutomationProperties.SetAutomationId(cell, f.Id("ResourceList.Column." + field));
            Grid.SetColumn(cell, column);
            header.Children.Add(cell);
        }

        var headerHost = new Border { Child = header };
        headerHost.Bind(Border.BackgroundProperty, headerHost.GetResourceObservable("FxcBgHeaderBrush"));
        var table = new DockPanel();
        DockPanel.SetDock(headerHost, Avalonia.Controls.Dock.Top);
        table.Children.Add(headerHost);
        table.Children.Add(list);
        var frame = new Border { BorderThickness = new Thickness(1), Child = table };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));

        List<ResourceRow> Selected() => [.. list.SelectedItems?.OfType<ResourceRow>() ?? []];
        void Fill()
        {
            var keep = Selected();
            list.ItemsSource = model.ResourceList.ToList();
            list.SelectedItems?.Clear(); // a new source keeps the selected items: they are restored once below
            foreach (var row in keep.Where(model.ResourceList.Contains))
                list.SelectedItems?.Add(row);
            if (list.SelectedItems?.Count is null or 0 && model.SelectedResource is { } current)
                list.SelectedItem = current;
        }

        void Move(int delta)
        {
            if (Selected() is [var row])
            {
                model.MoveResource(row, delta);
                list.SelectedItem = row;
            }
        }

        Button Action(string id, string label, Action action)
        {
            var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(button, f.Id("ResourceList." + id));
            button.Click += (_, _) => action();
            return button;
        }

        var add = new Button { Content = Strings.ProjectOptions_Add, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(add, f.Id("ResourceList.Add"));
        add.Click += async (_, _) => await model.AddResourcesAsync();
        var remove = Action("Remove", Strings.ProjectOptions_Remove, () => model.RemoveResources(Selected()));
        var up = Action("MoveUp", Strings.ProjectOptions_MoveUp, () => Move(-1));
        var down = Action("MoveDown", Strings.ProjectOptions_MoveDown, () => Move(1));
        void SyncButtons()
        {
            var selected = Selected();
            var index = selected is [var row] ? model.ResourceList.IndexOf(row) : -1;
            remove.IsEnabled = selected.Count > 0;
            up.IsEnabled = index > 0;
            down.IsEnabled = index >= 0 && index < model.ResourceList.Count - 1;
        }

        list.SelectionChanged += (_, _) =>
        {
            model.SelectedResource = list.SelectedItem as ResourceRow;
            SyncButtons();
        };
        list.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Delete when e.Source is not TextBox:
                    model.RemoveResources(Selected());
                    break;
                case Key.Up when e.KeyModifiers == KeyModifiers.Alt:
                    Move(-1);
                    break;
                case Key.Down when e.KeyModifiers == KeyModifiers.Alt:
                    Move(1);
                    break;
                default:
                    return;
            }

            e.Handled = true;
        };

        // The model picks the current row after Add and Remove.
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectOptionsViewModel.SelectedResource) && model.SelectedResource is { } current
                && list.SelectedItems?.Contains(current) != true)
                list.SelectedItem = current;
        };

        // The page was built for this model; the list is filled again after every add, remove and move.
        model.ResourceList.CollectionChanged += (_, _) =>
        {
            Fill();
            SyncButtons();
        };
        Fill();
        SyncButtons();

        var buttons = new StackPanel { Spacing = 4, Width = 110, Margin = new Thickness(8, 0, 0, 0), Children = { add, remove, up, down } };
        var grid = new DockPanel();
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Right);
        grid.Children.Add(buttons);
        grid.Children.Add(frame);
        var message = f.Message("ResourceList", model, () => model.ResourceListError, () => true);
        message.Margin = new Thickness(0, 2, 0, 0);
        return Page(grid, message, PreviewPanel(f, model));
    }

    private static ColumnDefinitions Columns() => new("160,110,*");

    /// <summary>One row: the name and type cells edited in place, the file as stored.</summary>
    private static Control RowCells(DialogFieldBuilder f, ProjectOptionsViewModel model, ListBox list, ResourceRow row)
    {
        var index = model.ResourceList.IndexOf(row);
        var name = new TextBox { Text = row.Name, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(name, f.Id("ResourceList.Row." + index + ".Name"));
        AutomationProperties.SetName(name, Strings.ProjectOptions_ResourceName);
        name.TextChanged += (_, _) => row.Name = name.Text ?? "";
        var type = new ComboBox { ItemsSource = model.ResourceTypes, SelectedItem = row.Type, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetAutomationId(type, f.Id("ResourceList.Row." + index + ".Type"));
        AutomationProperties.SetName(type, Strings.ProjectOptions_ResourceType);
        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedItem is string value)
                row.Type = value;
        };
        var file = new TextBlock { Text = row.Path, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(file, row.Path);
        AutomationProperties.SetAutomationId(file, f.Id("ResourceList.Row." + index + ".File"));

        // A cell that takes the focus makes its row the current one (the preview follows).
        foreach (var cell in (Control[])[name, type])
        {
            cell.GotFocus += (_, _) =>
            {
                if (list.SelectedItems?.Contains(row) != true)
                    list.SelectedItem = row;
            };
        }

        void ShowError()
        {
            name.Classes.Set("invalid", row.NameError is not null);
            ToolTip.SetTip(name, row.NameError);
            AutomationProperties.SetHelpText(name, row.NameError ?? "");
        }

        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ResourceRow.NameError))
                ShowError();
        };
        ShowError();
        var cells = new Grid { ColumnDefinitions = Columns() };
        Grid.SetColumn(type, 1);
        Grid.SetColumn(file, 2);
        cells.Children.Add(name);
        cells.Children.Add(type);
        cells.Children.Add(file);
        return cells;
    }

    /// <summary>UI-DLG-333: the image, text or hex dump of the current row (160 DIP), and the size and type line under it.</summary>
    private static Control PreviewPanel(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        var image = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(image, f.Id("ResourcePreview.Image"));
        var text = new SelectableTextBlock { TextWrapping = TextWrapping.NoWrap, FontSize = 12, LineHeight = 16 }; // type.codeSmall
        text.Classes.Add("codeSmall");
        text.Bind(TextBlock.FontFamilyProperty, text.GetResourceObservable("FxcFontMono"));
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(text, f.Id("ResourcePreview.Text"));
        var scroller = new ScrollViewer { Content = text, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var box = new Border { Height = ResourcePreviewHeight, BorderThickness = new Thickness(1), Padding = new Thickness(4), Margin = new Thickness(0, 8, 0, 0), Child = new Panel { Children = { image, scroller } } };
        box.Bind(Border.BorderBrushProperty, box.GetResourceObservable("FxcBorderDefaultBrush"));
        box.Bind(Border.BackgroundProperty, box.GetResourceObservable("FxcBgEditorBrush"));
        AutomationProperties.SetAutomationId(box, f.Id("ResourcePreview"));
        var info = new TextBlock { Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        info.Classes.Add("caption");
        info.Bind(TextBlock.ForegroundProperty, info.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(info, f.Id("ResourcePreview.Info"));
        var shown = (ResourcePreview?)null;
        void Sync()
        {
            var preview = model.Preview;
            if (preview == shown)
                return;
            shown = preview;
            image.IsVisible = preview.Image is not null;
            image.Source = Decode(preview.Image);
            image.MaxWidth = (preview.Image?.PixelWidth ?? 0) * ResourcePreviews.MaxScale;
            image.MaxHeight = (preview.Image?.PixelHeight ?? 0) * ResourcePreviews.MaxScale;
            text.IsVisible = preview.Text is not null;
            text.Text = preview.Text;
            // The scroller stays in the tree (its text is found by id) but shows bars only for text.
            var bars = preview.Text is null ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = bars;
            scroller.VerticalScrollBarVisibility = bars;
            scroller.Offset = default;
            info.Text = preview.Info;
            info.IsVisible = preview.Info is not null;
        }

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProjectOptionsViewModel.Preview) or nameof(ProjectOptionsViewModel.SelectedResource) or "")
                Sync();
        };
        Sync();
        return new StackPanel { Children = { box, info } };
    }

    /// <summary>UI-DLG-334: the check box, the seven fields (disabled while it is off) and the two read-only lines.</summary>
    private static Control VersionInfoPage(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        var include = new CheckBox { Content = Strings.ProjectOptions_IncludeVersionInfo, IsChecked = model.VersionInfoEnabled };
        AutomationProperties.SetAutomationId(include, f.Id("IncludeVersionInfo"));
        include.IsCheckedChanged += (_, _) => model.VersionInfoEnabled = include.IsChecked == true;
        (string Field, string Label, Func<string> Get, Action<string> Set, Func<string?>? Error)[] fields =
        [
            ("FileVersion", Strings.ProjectOptions_FileVersion, () => model.FileVersion, v => model.FileVersion = v, () => model.FileVersionError),
            ("ProductVersion", Strings.ProjectOptions_ProductVersion, () => model.ProductVersion, v => model.ProductVersion = v, () => model.ProductVersionError),
            ("CompanyName", Strings.ProjectOptions_CompanyName, () => model.CompanyName, v => model.CompanyName = v, null),
            ("FileDescription", Strings.ProjectOptions_FileDescription, () => model.FileDescription, v => model.FileDescription = v, null),
            ("ProductName", Strings.ProjectOptions_ProductName, () => model.ProductName, v => model.ProductName = v, null),
            ("LegalCopyright", Strings.ProjectOptions_LegalCopyright, () => model.LegalCopyright, v => model.LegalCopyright = v, null),
            ("Comments", Strings.ProjectOptions_Comments, () => model.Comments, v => model.Comments = v, null),
        ];
        var rows = new List<Control> { include };
        var boxes = new List<(TextBox Box, Func<string> Get)>();
        foreach (var (field, label, get, set, error) in fields)
        {
            var box = f.Text(field, get, set);
            boxes.Add((box, get));
            rows.Add(DialogFieldBuilder.Row(DialogFieldBuilder.Label(label, box), box));
            if (error is not null)
                rows.Add(f.Message(field, model, error, () => true, box));
        }

        var internalName = f.ReadOnly("InternalName", model.InternalName);
        var originalFileName = f.ReadOnly("OriginalFileName", model.OriginalFileName);
        rows.Add(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_InternalName, null), internalName));
        rows.Add(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_OriginalFileName, null), originalFileName));
        void Sync()
        {
            include.IsChecked = model.VersionInfoEnabled;
            foreach (var (box, get) in boxes)
            {
                box.IsEnabled = model.VersionInfoEnabled;
                if (box.Text != get())
                    box.Text = get();
            }
        }

        model.PropertyChanged += (_, _) => Sync();
        Sync();
        return Page([.. rows]);
    }

    /// <summary>The bitmap of an image file; null when there is none or it cannot be decoded.</summary>
    private static Bitmap? Decode(ResourceImage? image)
    {
        if (image is null)
            return null;
        try
        {
            using var stream = new MemoryStream(image.Data, writable: false);
            return new Bitmap(stream);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
