using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Project Options dialog (dialogs.md §8, UI-DLG-080…086), 760 × 520, resizable: the page list <c>Pages</c> at the
/// left (rows <c>Page.&lt;PageId&gt;</c> with an error dot <c>Page.&lt;PageId&gt;.Error</c>), then the configuration bar,
/// a separator, the page title and the page content, rebuilt when the page or the edited configuration changes.
/// </summary>
public sealed class ProjectOptionsWindow : DialogWindow
{
    public ProjectOptionsWindow(ProjectOptionsViewModel model)
        : base(model, Body(model))
    {
    }

    private static Control Body(ProjectOptionsViewModel model)
    {
        var f = new DialogFieldBuilder(ProjectOptionsViewModel.DialogName);
        var pages = new ListBox { Width = 180, ItemsSource = model.Pages, SelectedItem = model.SelectedPage, SelectionMode = SelectionMode.Single };
        AutomationProperties.SetAutomationId(pages, f.Id("Pages"));
        pages.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ProjectOptionsPage>((page, _) => PageRow(f, page), supportsRecycling: false);
        pages.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem item && item.DataContext is ProjectOptionsPage page)
                AutomationProperties.SetAutomationId(item, f.Id("Page." + page.Id));
        };
        pages.SelectionChanged += (_, _) =>
        {
            if (pages.SelectedItem is ProjectOptionsPage page)
                model.SelectedPage = page;
        };

        // UI-DLG-082 configuration bar.
        var configuration = new ComboBox { MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(configuration, f.Id("Configuration"));
        var newButton = new Button { Content = Strings.ProjectOptions_NewConfiguration, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetAutomationId(newButton, f.Id("NewConfiguration"));
        newButton.Click += async (_, _) => await model.NewConfigurationAsync();
        var deleteButton = new Button { Content = Strings.ProjectOptions_DeleteConfiguration, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetAutomationId(deleteButton, f.Id("DeleteConfiguration"));
        deleteButton.Click += async (_, _) => await model.DeleteConfigurationAsync();
        var syncing = false;
        configuration.SelectionChanged += (_, _) =>
        {
            if (!syncing && model.ConfigurationBarEnabled && configuration.SelectedIndex >= 0 && configuration.SelectedIndex < model.ConfigurationNames.Count)
                model.SelectedConfiguration = model.ConfigurationNames[configuration.SelectedIndex];
        };
        void SyncBar()
        {
            syncing = true;
            configuration.ItemsSource = model.ConfigurationBarEnabled
                ? model.ConfigurationNames.Select(model.ConfigurationTitle).ToList()
                : [Strings.ProjectOptions_AllConfigurations];
            configuration.SelectedIndex = model.ConfigurationBarEnabled ? model.ConfigurationNames.ToList().IndexOf(model.SelectedConfiguration) : 0;
            syncing = false;
            configuration.IsEnabled = model.ConfigurationBarEnabled;
            newButton.IsEnabled = model.ConfigurationBarEnabled;
            deleteButton.IsEnabled = model.CanDeleteConfiguration;
            ToolTip.SetTip(deleteButton, model.ConfigurationNames.Count <= 1 ? Strings.Msg_ProjectOptions_ConfigDeleteLast : null);
        }

        var barLabel = DialogFieldBuilder.Label(Strings.ProjectOptions_Configuration, configuration);
        barLabel.Margin = new Thickness(0, 0, 8, 0);
        configuration.HorizontalAlignment = HorizontalAlignment.Stretch;
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        foreach (var (control, column) in (ReadOnlySpan<(Control, int)>)[(barLabel, 0), (configuration, 1), (newButton, 2), (deleteButton, 3)])
        {
            Grid.SetColumn(control, column);
            bar.Children.Add(control);
        }
        var separator = new Rectangle { Height = 1, Margin = new Thickness(0, 8, 0, 8) };
        separator.Bind(Shape.FillProperty, separator.GetResourceObservable("FxcBorderSubtleBrush"));
        var title = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        title.Classes.Add("subtitle");
        var content = new ContentControl();
        var scroller = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        void ShowPage()
        {
            SyncBar();
            title.Text = model.SelectedPage.Title;
            content.Content = model.SelectedPage.Id switch
            {
                "Application" => ApplicationPage(f, model),
                "Compiler" => CompilerPage(f, model),
                "SearchPaths" => SearchPathsPage(f, model),
                "ConditionalDefines" => DefinesPage(f, model),
                "Output" => OutputPage(f, model),
                "Run" => RunParametersWindow.Fields(f, model.Run, BrowseWorkingDirAsync, model.BrowseHostAsync),
                _ => BuildEventsPage(f, model),
            };
        }

        async Task BrowseWorkingDirAsync() => await model.BrowseWorkingDirAsync();

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProjectOptionsViewModel.SelectedPage) or nameof(ProjectOptionsViewModel.SelectedConfiguration)
                or nameof(ProjectOptionsViewModel.ConfigurationNames))
                ShowPage();
            else if (e.PropertyName == nameof(ProjectOptionsViewModel.CanDeleteConfiguration))
                SyncBar();
        };
        ShowPage();

        var right = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
        foreach (var top in (Control[])[bar, separator, title])
        {
            DockPanel.SetDock(top, Avalonia.Controls.Dock.Top);
            right.Children.Add(top);
        }

        right.Children.Add(scroller);
        var body = new DockPanel();
        DockPanel.SetDock(pages, Avalonia.Controls.Dock.Left);
        body.Children.Add(pages);
        body.Children.Add(right);
        return body;
    }

    private static Control PageRow(DialogFieldBuilder f, ProjectOptionsPage page)
    {
        var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = page.HasError };
        dot.Bind(Shape.FillProperty, dot.GetResourceObservable("FxcStatusErrorBrush"));
        AutomationProperties.SetAutomationId(dot, f.Id("Page." + page.Id + ".Error"));
        page.PropertyChanged += (_, _) => dot.IsVisible = page.HasError;
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = page.Title }, dot } };
    }

    private static StackPanel Page(params Control[] rows)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var row in rows)
            panel.Children.Add(row);
        return panel;
    }

    private static Control Field(Control row, TextBlock message) => new StackPanel { Children = { row, message } };

    private static Control ApplicationPage(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        var name = f.Text("Name", () => model.ProjectName, v => model.ProjectName = v);
        var appType = new ComboBox { ItemsSource = model.AppTypes, HorizontalAlignment = HorizontalAlignment.Stretch };
        appType.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<AppTypeItem>((item, _) => new TextBlock { Text = item.Title });
        appType.ContainerPrepared += (_, e) => e.Container.IsEnabled = e.Container.DataContext is AppTypeItem { IsEnabled: true };
        appType.SelectedItem = model.AppTypes.FirstOrDefault(a => a.Value == model.AppType);
        appType.SelectionChanged += (_, _) =>
        {
            if (appType.SelectedItem is AppTypeItem item)
                model.AppType = item.Value;
        };
        AutomationProperties.SetAutomationId(appType, f.Id("AppType"));
        var main = new ComboBox { ItemsSource = model.UnitFiles, SelectedItem = model.UnitFiles.FirstOrDefault(u => u.Equals(model.MainSource, StringComparison.OrdinalIgnoreCase)), HorizontalAlignment = HorizontalAlignment.Stretch };
        main.SelectionChanged += (_, _) =>
        {
            if (main.SelectedItem is string path)
                model.MainSource = path;
        };
        AutomationProperties.SetAutomationId(main, f.Id("MainSource"));
        // A path longer than the page is trimmed; the tooltip and a copy of the selection give the whole path.
        var projectFile = new SelectableTextBlock { Text = model.ProjectFile, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(projectFile, model.ProjectFile);
        projectFile.Classes.Add("codeSmall");
        AutomationProperties.SetAutomationId(projectFile, f.Id("ProjectFile"));
        // IDE-TGT-011: the output file the name gives — <name>.dll for a library.
        var nameHint = new TextBlock { Text = model.NameHint, Margin = new Thickness(DialogFieldBuilder.LabelWidth, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        nameHint.Classes.Add("caption");
        nameHint.Bind(TextBlock.ForegroundProperty, nameHint.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(nameHint, f.Id("Name.Hint"));
        model.PropertyChanged += (_, _) => nameHint.Text = model.NameHint;
        return Page(
            new StackPanel
            {
                Children =
                {
                    DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_Name, name), name),
                    nameHint,
                    f.Message("Name", model, () => model.ProjectNameError, () => true, name),
                },
            },
            DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_AppType, appType), appType),
            DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_MainSource, main), main),
            DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_Language, null), f.ReadOnly("Language", model.LanguageText)),
            DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_Platforms, null), f.ReadOnly("Platforms", model.PlatformText)),
            DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_ProjectFile, null), projectFile));
    }

    private static Control CompilerPage(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        var optimization = new ComboBox
        {
            ItemsSource = new[] { Strings.ProjectOptions_Optimization0, Strings.ProjectOptions_Optimization1, Strings.ProjectOptions_Optimization2 },
            SelectedIndex = model.Optimization,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        optimization.SelectionChanged += (_, _) => model.Optimization = optimization.SelectedIndex;
        AutomationProperties.SetAutomationId(optimization, f.Id("Optimization"));
        var debugInfo = new CheckBox { Content = Strings.ProjectOptions_DebugInfo, IsChecked = model.DebugInfo };
        debugInfo.IsCheckedChanged += (_, _) => model.DebugInfo = debugInfo.IsChecked == true;
        AutomationProperties.SetAutomationId(debugInfo, f.Id("DebugInfo"));
        return Page(
            DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_Optimization, optimization, model.ConfigurationNote), optimization),
            new StackPanel { Orientation = Orientation.Horizontal, Children = { debugInfo, Note(model) } });
    }

    private static TextBlock Note(ProjectOptionsViewModel model)
    {
        var note = new TextBlock { Text = model.ConfigurationNote, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("FxcTextTertiaryBrush"));
        return note;
    }

    private static Control SearchPathsPage(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        var list = new ListBox { ItemsSource = model.SearchPaths, Height = 220, SelectionMode = SelectionMode.Single };
        AutomationProperties.SetAutomationId(list, f.Id("SearchPaths"));
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SearchPathRow>((row, _) =>
        {
            var text = new TextBlock { Text = row.Path };
            ToolTip.SetTip(text, row.ToolTip);
            var glyph = new TextBlock { Text = "⚠", IsVisible = row.IsMissing, Margin = new Thickness(0, 0, 4, 0) };
            glyph.Bind(TextBlock.ForegroundProperty, glyph.GetResourceObservable("FxcStatusWarningBrush"));
            return new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, text } };
        }, supportsRecycling: false);
        var editor = new TextBox { IsVisible = false };
        AutomationProperties.SetAutomationId(editor, f.Id("SearchPaths.Editor"));
        var editing = -1;
        void BeginEdit()
        {
            if (list.SelectedIndex < 0)
                return;
            editing = list.SelectedIndex;
            editor.Text = model.SearchPaths[editing].Path;
            editor.IsVisible = true;
            editor.Focus();
            editor.SelectAll();
        }

        void EndEdit(bool commit)
        {
            if (editing < 0)
                return;
            var index = editing;
            editing = -1;
            editor.IsVisible = false;
            if (commit)
                model.EditSearchPath(index, editor.Text ?? "");
            list.Focus();
        }

        editor.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                EndEdit(e.Key == Key.Enter);
                e.Handled = true;
            }
        };
        editor.LostFocus += (_, _) => EndEdit(true);
        list.DoubleTapped += (_, _) => BeginEdit();
        list.KeyDown += (_, e) =>
        {
            var index = list.SelectedIndex;
            switch (e.Key)
            {
                case Key.F2:
                    BeginEdit();
                    break;
                case Key.Delete:
                    model.RemoveSearchPath(index);
                    break;
                case Key.Up when e.KeyModifiers == KeyModifiers.Alt:
                    model.MoveSearchPath(index, -1);
                    list.SelectedIndex = Math.Max(0, index - 1);
                    break;
                case Key.Down when e.KeyModifiers == KeyModifiers.Alt:
                    model.MoveSearchPath(index, 1);
                    list.SelectedIndex = Math.Min(model.SearchPaths.Count - 1, index + 1);
                    break;
                default:
                    return;
            }

            e.Handled = true;
        };

        Button Action(string id, string label, Action action)
        {
            var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(button, f.Id("SearchPaths." + id));
            button.Click += (_, _) => action();
            return button;
        }

        var add = new Button { Content = Strings.ProjectOptions_Add, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(add, f.Id("SearchPaths.Add"));
        add.Click += async (_, _) => await model.AddSearchPathAsync();
        var edit = Action("Edit", Strings.ProjectOptions_Edit, BeginEdit);
        var remove = Action("Remove", Strings.ProjectOptions_Remove, () => model.RemoveSearchPath(list.SelectedIndex));
        var up = Action("MoveUp", Strings.ProjectOptions_MoveUp, () =>
        {
            var i = list.SelectedIndex;
            model.MoveSearchPath(i, -1);
            list.SelectedIndex = Math.Max(0, i - 1);
        });
        var down = Action("MoveDown", Strings.ProjectOptions_MoveDown, () =>
        {
            var i = list.SelectedIndex;
            model.MoveSearchPath(i, 1);
            list.SelectedIndex = Math.Min(model.SearchPaths.Count - 1, i + 1);
        });
        void SyncButtons()
        {
            var i = list.SelectedIndex;
            edit.IsEnabled = remove.IsEnabled = i >= 0;
            up.IsEnabled = i > 0;
            down.IsEnabled = i >= 0 && i < model.SearchPaths.Count - 1;
        }

        list.SelectionChanged += (_, _) => SyncButtons();
        model.SearchPaths.CollectionChanged += (_, _) => SyncButtons();
        SyncButtons();
        var buttons = new StackPanel { Spacing = 4, Width = 110, Margin = new Thickness(8, 0, 0, 0), Children = { add, edit, remove, up, down } };
        var grid = new DockPanel();
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Right);
        grid.Children.Add(buttons);
        grid.Children.Add(new StackPanel { Children = { list, editor } });
        return Page(
            DialogFieldBuilder.Label(Strings.ProjectOptions_SearchPaths, list, Strings.ProjectOptions_SearchPathsNote),
            grid,
            f.Message("SearchPaths", model, () => model.SearchPathsError, () => true));
    }

    private static Control DefinesPage(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        var project = f.Text("ProjectDefines", () => model.ProjectDefines, v => model.ProjectDefines = v, code: true);
        var config = f.Text("ConfigDefines", () => model.ConfigDefines, v => model.ConfigDefines = v, code: true);
        var effective = f.ReadOnly("Effective", model.EffectiveDefines, code: true);
        model.PropertyChanged += (_, _) => effective.Text = model.EffectiveDefines;
        return Page(
            Field(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_ProjectDefines, project), project),
                f.Message("ProjectDefines", model, () => model.ProjectDefinesError, () => true, project)),
            Field(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_ConfigDefines, config, model.ConfigurationNote), config),
                f.Message("ConfigDefines", model, () => model.ConfigDefinesError, () => true, config)),
            DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_Effective, null), effective));
    }

    private static Control OutputPage(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        var outputDir = f.Text("OutputDir", () => model.OutputDir, v => model.OutputDir = v);
        var outputFile = f.ReadOnly("OutputFile", model.OutputFile, code: true);
        var objDir = f.ReadOnly("ObjDir", model.ObjDir, code: true);
        model.PropertyChanged += (_, e) =>
        {
            outputFile.Text = model.OutputFile;
            if (e.PropertyName == nameof(ProjectOptionsViewModel.OutputDir) && outputDir.Text != model.OutputDir)
                outputDir.Text = model.OutputDir;
        };
        return Page(
            Field(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_OutputDir, outputDir, model.ConfigurationNote),
                    f.WithBrowse("OutputDir", outputDir, model.BrowseOutputDirAsync)),
                f.Message("OutputDir", model, () => model.OutputDirError, () => true, outputDir)),
            DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_OutputFile, null), outputFile),
            DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_ObjDir, null), objDir));
    }

    private static Control BuildEventsPage(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        TextBox Events(string id, string text)
        {
            var box = new TextBox { Text = text, IsReadOnly = true, IsEnabled = false, AcceptsReturn = true, Height = 100 };
            box.Classes.Add("code");
            AutomationProperties.SetAutomationId(box, f.Id(id));
            return box;
        }

        var pre = Events("PreBuild", model.PreBuild);
        var post = Events("PostBuild", model.PostBuild);
        var notice = new TextBlock { Text = Strings.Msg_ProjectOptions_EventsNotSupported, TextWrapping = TextWrapping.Wrap };
        notice.Bind(TextBlock.ForegroundProperty, notice.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(notice, f.Id("Notice"));
        return Page(DialogFieldBuilder.Label(Strings.ProjectOptions_PreBuild, pre), pre, DialogFieldBuilder.Label(Strings.ProjectOptions_PostBuild, post), post, notice);
    }
}
