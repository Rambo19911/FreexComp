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
public sealed partial class ProjectOptionsWindow : DialogWindow
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
            {
                AutomationProperties.SetAutomationId(item, f.Id("Page." + page.Id));
                AutomationProperties.SetName(item, page.Title); // read out by its title, not its type name
            }
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
            // The Search Paths page lists "All configurations" (the project-level list) before the configurations.
            var index = configuration.SelectedIndex - (model.HasAllConfigurationsChoice ? 1 : 0);
            if (syncing || !model.ConfigurationBarEnabled || configuration.SelectedIndex < 0 || index >= model.ConfigurationNames.Count)
                return;
            if (index >= 0)
                model.SelectedConfiguration = model.ConfigurationNames[index];
            if (model.HasAllConfigurationsChoice)
                model.SearchPathsAllConfigurations = index < 0;
        };
        void SyncBar()
        {
            syncing = true;
            var titles = model.ConfigurationBarEnabled ? model.ConfigurationNames.Select(model.ConfigurationTitle).ToList() : [];
            if (!model.ConfigurationBarEnabled || model.HasAllConfigurationsChoice)
                titles.Insert(0, Strings.ProjectOptions_AllConfigurations);
            configuration.ItemsSource = titles;
            configuration.SelectedIndex = model.ConfigurationSelected
                ? model.ConfigurationNames.ToList().IndexOf(model.SelectedConfiguration) + (model.HasAllConfigurationsChoice ? 1 : 0)
                : 0;
            syncing = false;
            configuration.IsEnabled = model.ConfigurationBarEnabled;
            newButton.IsEnabled = model.ConfigurationSelected;
            deleteButton.IsEnabled = model.CanDeleteConfiguration;
            // UI-DLG-084, IDE-CFG-014: a disabled Delete tells why, also to a screen reader.
            ToolTip.SetTip(deleteButton, model.DeleteConfigurationToolTip);
            ToolTip.SetShowOnDisabled(deleteButton, true);
            AutomationProperties.SetHelpText(deleteButton, model.DeleteConfigurationToolTip ?? "");
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
                "Resources" => ResourcesPage(f, model),
                "VersionInfo" => VersionInfoPage(f, model),
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
                or nameof(ProjectOptionsViewModel.ConfigurationNames) or nameof(ProjectOptionsViewModel.SearchPathsAllConfigurations))
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
            IconField(f, model),
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
        AutomationProperties.SetAutomationId(optimization, f.Id("Optimization"));
        var debugInfo = new CheckBox { Content = Strings.ProjectOptions_DebugInfo, IsChecked = model.DebugInfo };
        AutomationProperties.SetAutomationId(debugInfo, f.Id("DebugInfo"));
        // The page was built for this configuration; a later one gets its own page (the model outlives both).
        var configuration = model.SelectedConfiguration;
        var syncing = false;
        optimization.SelectionChanged += (_, _) =>
        {
            if (!syncing && model.SelectedConfiguration == configuration)
                model.Optimization = optimization.SelectedIndex;
        };
        debugInfo.IsCheckedChanged += (_, _) =>
        {
            if (!syncing && model.SelectedConfiguration == configuration)
                model.DebugInfo = debugInfo.IsChecked == true;
        };

        // IDE-CFG-021…023: the effective values, the own state in bold, Revert at the right end of the row.
        var optimizationLabel = WrappingLabel(Strings.ProjectOptions_Optimization, optimization);
        var optimizationRevert = RevertButton(f, "Optimization", model.RevertOptimization);
        var debugInfoRevert = RevertButton(f, "DebugInfo", model.RevertDebugInfo);
        void Sync()
        {
            if (model.SelectedConfiguration != configuration)
                return;
            syncing = true;
            optimization.SelectedIndex = model.Optimization;
            debugInfo.IsChecked = model.DebugInfo;
            syncing = false;
            optimizationLabel.FontWeight = Weight(model.OptimizationIsOwn);
            debugInfo.FontWeight = Weight(model.DebugInfoIsOwn);
            optimizationRevert.IsVisible = model.OptimizationIsOwn;
            debugInfoRevert.IsVisible = model.DebugInfoIsOwn;
            AutomationProperties.SetHelpText(optimization, model.OptimizationHelpText ?? "");
            AutomationProperties.SetHelpText(debugInfo, model.DebugInfoHelpText ?? "");
        }

        model.PropertyChanged += (_, _) => Sync();
        Sync();
        var debugInfoText = Strings.ProjectOptions_DebugInfo.Replace("_", "", StringComparison.Ordinal);
        var debugInfoGhost = new CheckBox { Content = debugInfoText, FontWeight = FontWeight.SemiBold, Opacity = 0, IsHitTestVisible = false, Focusable = false, IsTabStop = false };
        AutomationProperties.SetAccessibilityView(debugInfoGhost, AccessibilityView.Raw);
        var debugInfoCell = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { new Grid { Children = { debugInfoGhost, debugInfo } }, Note(model) },
        };
        return Page(
            DialogFieldBuilder.Row(StateLabel(optimizationLabel, Strings.ProjectOptions_Optimization, model), WithRevert(optimization, optimizationRevert, model.IsDerived)),
            WithRevert(debugInfoCell, debugInfoRevert, model.IsDerived));
    }

    /// <summary><c>type.bodyStrong</c> for an own value of a derived configuration, <c>type.body</c> otherwise (UI-DLG-277).</summary>
    private static FontWeight Weight(bool own) => own ? FontWeight.SemiBold : FontWeight.Normal;

    /// <summary>UI-DLG-085, UI-DLG-278: the configuration note — <c>text.tertiary</c>, regular weight in both states.</summary>
    private static TextBlock Note(ProjectOptionsViewModel model)
    {
        var note = new TextBlock
        {
            Text = model.ConfigurationNote,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.Normal,
        };
        note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("FxcTextTertiaryBrush"));
        return note;
    }

    /// <summary>
    /// The label of a [cfg] field followed by the configuration note. The label takes the width of its bold form, so
    /// that nothing moves when the state changes (UI-DLG-277); a note that does not fit beside it goes to its own line
    /// under the label (UI-DLG-278).
    /// </summary>
    private static Control StateLabel(Label label, string text, ProjectOptionsViewModel model)
    {
        var ghost = new TextBlock
        {
            Text = text.Replace("_", "", StringComparison.Ordinal),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        AutomationProperties.SetAccessibilityView(ghost, AccessibilityView.Raw);
        return new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Children = { new Grid { Children = { ghost, label } }, Note(model) },
        };
    }

    /// <summary>A label whose mnemonic focuses <paramref name="target"/> and whose text wraps inside the label column.</summary>
    private static Label WrappingLabel(string text, Control target)
    {
        var label = new Label
        {
            Content = new Avalonia.Controls.Primitives.AccessText { Text = text, TextWrapping = TextWrapping.Wrap },
            Target = target,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        return label;
    }

    /// <summary>UI-DLG-277: the 26 × 26 icon button <c>&lt;Field&gt;.Revert</c> ("Revert to inherited").</summary>
    private static Button RevertButton(DialogFieldBuilder f, string field, Action revert)
    {
        var button = new Button
        {
            Content = new Icons.IdeIcon { IconName = "undo", Width = 16, Height = 16 },
            Width = RevertSize,
            Height = RevertSize,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(button, f.Id(field + ".Revert"));
        AutomationProperties.SetName(button, Strings.ProjectOptions_Revert);
        ToolTip.SetTip(button, Strings.ProjectOptions_Revert);
        button.Click += (_, _) => revert();
        return button;
    }

    /// <summary>UI-DLG-277: side of the Revert button; its slot is this plus <c>space.4</c>.</summary>
    internal const double RevertSize = 26;

    /// <summary>
    /// The field with the slot of its Revert button at the right end of the row. The slot is reserved while a derived
    /// configuration is selected, so that the field keeps its width when the button appears; a root configuration has
    /// no slot (UI-DLG-277).
    /// </summary>
    private static Control WithRevert(Control field, Button revert, bool derived)
    {
        if (!derived)
            return field;
        var slot = new Panel { Width = RevertSize + 8, Children = { revert } };
        revert.HorizontalAlignment = HorizontalAlignment.Right;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(field);
        Grid.SetColumn(slot, 1);
        grid.Children.Add(slot);
        return grid;
    }

    /// <summary>
    /// §8.3. Under <c>All configurations</c> the project-level list; for a configuration its own rows below the read-only
    /// rows it inherits (<c>text.tertiary</c>, origin in the second column) and the check box <c>Do not inherit</c>.
    /// The indexes of the model count the own rows only.
    /// </summary>
    private static Control SearchPathsPage(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        var configuration = model.SelectedConfiguration;
        var all = model.SearchPathsAllConfigurations;
        // The page was built for this list; a later one gets its own page (the model outlives both).
        bool Current() => model.SelectedPage.Id == "SearchPaths" && model.SearchPathsAllConfigurations == all && (all || model.SelectedConfiguration == configuration);
        var list = new ListBox { Height = 220, SelectionMode = SelectionMode.Single };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AutomationProperties.SetAutomationId(list, f.Id("SearchPaths"));
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SearchPathRow>((row, _) =>
        {
            if (row is null)
                return new Panel(); // a container between two fillings of the list
            var text = new TextBlock { Text = row.Path, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(text, row.ToolTip ?? row.Path);
            var glyph = new TextBlock { Text = "⚠", IsVisible = row.IsMissing, Margin = new Thickness(0, 0, 4, 0) };
            glyph.Bind(TextBlock.ForegroundProperty, glyph.GetResourceObservable("FxcStatusWarningBrush"));
            var cells = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            Grid.SetColumn(text, 1);
            cells.Children.Add(glyph);
            cells.Children.Add(text);
            if (row.Origin is { } origin)
            {
                // Inherited: read-only, text.tertiary, the origin (Project or a configuration) at the right.
                var from = new TextBlock { Text = origin, Margin = new Thickness(16, 0, 0, 0), MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis };
                ToolTip.SetTip(from, origin);
                foreach (var block in (TextBlock[])[text, from])
                    block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("FxcTextTertiaryBrush"));
                Grid.SetColumn(from, 2);
                cells.Children.Add(from);
            }

            return cells;
        }, supportsRecycling: false);
        list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is not ListBoxItem item || item.DataContext is not SearchPathRow row)
                return;
            item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(item, row.Path);
            AutomationProperties.SetHelpText(item, row.Origin is { } origin
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_ValueInherited, origin)
                : "");
        };

        var inherited = 0;
        int Own() => list.SelectedIndex - inherited; // the selected own row; negative for an inherited row or none
        void Fill(int? select = null)
        {
            var rows = model.InheritedSearchPaths;
            var index = select is { } own ? rows.Count + own : list.SelectedIndex;
            inherited = rows.Count;
            list.ItemsSource = rows.Concat(model.SearchPaths).ToList();
            list.SelectedIndex = Math.Min(index, list.ItemCount - 1);
        }

        var editor = new TextBox { IsVisible = false };
        AutomationProperties.SetAutomationId(editor, f.Id("SearchPaths.Editor"));
        var editing = -1;
        void BeginEdit()
        {
            if (Own() < 0)
                return;
            editing = Own();
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
            if (commit && Current())
                model.EditSearchPath(index, editor.Text ?? "");
            list.Focus();
        }

        void Move(int delta)
        {
            var index = Own();
            if (index < 0)
                return;
            model.MoveSearchPath(index, delta);
            Fill(Math.Clamp(index + delta, 0, model.SearchPaths.Count - 1));
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
            switch (e.Key)
            {
                case Key.F2:
                    BeginEdit();
                    break;
                case Key.Delete:
                    model.RemoveSearchPath(Own());
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

        Button Action(string id, string label, Action action)
        {
            var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(button, f.Id("SearchPaths." + id));
            button.Click += (_, _) => action();
            return button;
        }

        var add = new Button { Content = Strings.ProjectOptions_Add, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(add, f.Id("SearchPaths.Add"));
        add.Click += async (_, _) =>
        {
            var before = model.SearchPaths.Count;
            await model.AddSearchPathAsync();
            if (Current() && model.SearchPaths.Count > before)
                Fill(model.SearchPaths.Count - 1);
        };
        var edit = Action("Edit", Strings.ProjectOptions_Edit, BeginEdit);
        var remove = Action("Remove", Strings.ProjectOptions_Remove, () => model.RemoveSearchPath(Own()));
        var up = Action("MoveUp", Strings.ProjectOptions_MoveUp, () => Move(-1));
        var down = Action("MoveDown", Strings.ProjectOptions_MoveDown, () => Move(1));
        void SyncButtons()
        {
            var i = Own();
            edit.IsEnabled = remove.IsEnabled = i >= 0;
            up.IsEnabled = i > 0;
            down.IsEnabled = i >= 0 && i < model.SearchPaths.Count - 1;
        }

        // Do not inherit: the reset marker of the configuration (CMP-1153); no part of the project-level list.
        var doNotInherit = new CheckBox { Content = Strings.ProjectOptions_SearchPathsDoNotInherit, IsChecked = model.SearchPathsDoNotInherit, IsVisible = model.CanSearchPathsDoNotInherit };
        AutomationProperties.SetAutomationId(doNotInherit, f.Id("SearchPaths.DoNotInherit"));
        var syncing = false;
        doNotInherit.IsCheckedChanged += (_, _) =>
        {
            if (!syncing && Current())
                model.SearchPathsDoNotInherit = doNotInherit.IsChecked == true;
        };

        list.SelectionChanged += (_, _) => SyncButtons();
        void Refresh()
        {
            if (!Current())
                return;
            Fill();
            syncing = true;
            doNotInherit.IsChecked = model.SearchPathsDoNotInherit;
            syncing = false;
            SyncButtons();
        }

        model.SearchPaths.CollectionChanged += (_, _) => Refresh();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProjectOptionsViewModel.InheritedSearchPaths) or nameof(ProjectOptionsViewModel.SearchPathsDoNotInherit))
                Refresh();
        };
        Fill();
        SyncButtons();
        var buttons = new StackPanel { Spacing = 4, Width = 110, Margin = new Thickness(8, 0, 0, 0), Children = { add, edit, remove, up, down } };
        var grid = new DockPanel();
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Right);
        grid.Children.Add(buttons);
        grid.Children.Add(new StackPanel { Children = { list, editor } });
        return Page(
            DialogFieldBuilder.Label(Strings.ProjectOptions_SearchPaths, list, all ? Strings.ProjectOptions_SearchPathsNote : model.ConfigurationNote),
            grid,
            doNotInherit,
            f.Message("SearchPaths", model, () => model.SearchPathsError, () => true));
    }

    private static Control DefinesPage(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        var configuration = model.SelectedConfiguration;
        var project = f.Text("ProjectDefines", () => model.ProjectDefines, v => model.ProjectDefines = v, code: true);
        project.VerticalAlignment = VerticalAlignment.Center; // beside a label of two lines the field keeps its height
        var config = new TextBox { Text = model.ConfigDefines, VerticalAlignment = VerticalAlignment.Top };
        config.Classes.Add("code");
        AutomationProperties.SetAutomationId(config, f.Id("ConfigDefines"));
        config.TextChanged += (_, _) =>
        {
            if (model.SelectedConfiguration == configuration)
                model.ConfigDefines = config.Text ?? "";
        };
        // UI-DLG-279: read-only, selectable, wrapping; never a Revert button.
        var inherited = ReadOnlyList(f, "InheritedDefines");
        var effective = ReadOnlyList(f, "Effective");
        var configLabel = WrappingLabel(Strings.ProjectOptions_ConfigDefines, config);
        var revert = RevertButton(f, "ConfigDefines", model.RevertConfigDefines);
        revert.VerticalAlignment = VerticalAlignment.Top;
        void Sync()
        {
            if (model.SelectedConfiguration != configuration)
                return;
            effective.Text = model.EffectiveDefines;
            inherited.Text = model.InheritedDefines;
            ToolTip.SetTip(inherited, model.InheritedDefines);
            ToolTip.SetTip(effective, model.EffectiveDefines);
            if ((config.Text ?? "") != model.ConfigDefines)
                config.Text = model.ConfigDefines; // Revert emptied the list
            configLabel.FontWeight = Weight(model.ConfigDefinesIsOwn);
            revert.IsVisible = model.ConfigDefinesIsOwn;
            AutomationProperties.SetHelpText(config, model.ConfigDefinesHelpText ?? "");
        }

        model.PropertyChanged += (_, _) => Sync();
        Sync();
        var rows = new List<Control>
        {
            // The label is a little wider than the label column: it wraps instead of being cut.
            Field(DialogFieldBuilder.Row(WrappingLabel(Strings.ProjectOptions_ProjectDefines, project), project),
                f.Message("ProjectDefines", model, () => model.ProjectDefinesError, () => true, project)),
        };
        if (model.IsDerived)
            rows.Add(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_InheritedDefines, null), inherited));
        // The message and the hint stand directly under the field, also when the label beside it takes more lines.
        var message = f.Message("ConfigDefines", model, () => model.ConfigDefinesError, () => true, config);
        message.Margin = new Thickness(0, 2, 0, 0);
        var configField = new StackPanel { Children = { WithRevert(config, revert, model.IsDerived), message } };
        if (model.DefinesHint is { } hintText)
        {
            var hint = new TextBlock { Text = hintText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, RevertSize + 8, 0) };
            hint.Classes.Add("caption");
            hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("FxcTextSecondaryBrush"));
            AutomationProperties.SetAutomationId(hint, f.Id("ConfigDefines.Hint"));
            configField.Children.Add(hint);
        }

        rows.Add(DialogFieldBuilder.Row(StateLabel(configLabel, Strings.ProjectOptions_ConfigDefines, model), configField));
        rows.Add(DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_Effective, null), effective));
        return Page([.. rows]);
    }

    /// <summary>A read-only list of defines: selectable, wrapping, in the small code font (UI-DLG-279).</summary>
    private static SelectableTextBlock ReadOnlyList(DialogFieldBuilder f, string field)
    {
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, LineHeight = 16 };
        block.Classes.Add("codeSmall");
        block.Bind(TextBlock.FontFamilyProperty, block.GetResourceObservable("FxcFontMono"));
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(block, f.Id(field));
        return block;
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
                // IDE-CFG-026: a folder shared with another configuration only warns (glyph, status.warning).
                f.Message("OutputDir", model, () => model.OutputDirError ?? (model.OutputDirWarning is { } shared ? "⚠ " + shared : null),
                    () => model.OutputDirError is not null, outputDir)),
            DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_OutputFile, null), outputFile),
            DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.ProjectOptions_ObjDir, null), objDir));
    }

    /// <summary>Line height of <c>type.code</c> (design-tokens.md): the unit of the list fields' least height.</summary>
    private const double CodeLineHeight = 18;

    /// <summary>UI-DLG-299: a list field never gets lower than three rows (plus its padding, 4 above and below, and its border).</summary>
    private const double EventListMinHeight = (3 * CodeLineHeight) + 10;

    /// <summary>
    /// The page <c>Build Events</c> (dialogs.md §8.7, UI-DLG-299…305): the hint, for a configuration the check box
    /// <c>OwnEvents</c> and the line <c>Inherited</c>, the two list fields — one command per line, sharing the free height —
    /// the row <c>PostBuildWhen</c> + <c>InsertMacro</c> and the trust hint. Built for one choice of the configuration bar.
    /// </summary>
    private static Control BuildEventsPage(DialogFieldBuilder f, ProjectOptionsViewModel model)
    {
        var alive = true;
        var syncing = false;

        TextBlock Hint(string id, string text)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("FxcTextSecondaryBrush"));
            AutomationProperties.SetAutomationId(block, f.Id(id));
            return block;
        }

        // IDE-EVT-020: Enter adds a line (the box takes the key, so the default button is not activated), Tab leaves the
        // field; a command is one line, so the text does not wrap and scrolls in both directions when needed.
        TextBox List(string id, EventsField field, Func<string> get, Action<string> set)
        {
            var box = new TextBox { Text = get(), AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.NoWrap, FontSize = 13, LineHeight = CodeLineHeight, Padding = new Thickness(8, 4) };
            box.Classes.Add("code");
            box.Bind(Avalonia.Controls.Primitives.TemplatedControl.FontFamilyProperty, box.GetResourceObservable("FxcFontMono"));
            ScrollViewer.SetHorizontalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            AutomationProperties.SetAutomationId(box, f.Id(id));
            box.TextChanged += (_, _) =>
            {
                if (alive && !syncing)
                    set(box.Text ?? "");
            };
            box.GotFocus += (_, _) =>
            {
                if (alive)
                    model.LastFocusedEventsField = field;
            };
            return box;
        }

        var hint = Hint("Hint", Strings.Msg_ProjectOptions_EventsHint);
        var trustHint = Hint("TrustHint", Strings.Msg_ProjectOptions_EventsTrustHint);
        var own = new CheckBox { Content = Strings.ProjectOptions_OwnEvents };
        AutomationProperties.SetAutomationId(own, f.Id("OwnEvents"));
        own.IsCheckedChanged += (_, _) =>
        {
            if (alive && !syncing)
                model.OwnEvents = own.IsChecked == true;
        };
        var inherited = Hint("Inherited", "");

        var pre = List("PreBuild", EventsField.PreBuild, () => model.PreBuild, v => model.PreBuild = v);
        var post = List("PostBuild", EventsField.PostBuild, () => model.PostBuild, v => model.PostBuild = v);
        // UI-DLG-303: at most one line under a field — the error, else the warning of a list that cannot be edited here.
        TextBlock Message(string id, TextBox box, Func<string?> error, Func<string?> warning)
        {
            var line = f.Message(id, model, () => error() ?? (warning() is { } text ? "⚠ " + text : null), () => error() is not null, box);
            line.Margin = new Thickness(0, 2, 0, 0);
            return line;
        }

        var preMessage = Message("PreBuild", pre, () => model.PreBuildError, () => model.PreBuildWarning);
        var postMessage = Message("PostBuild", post, () => model.PostBuildError, () => model.PostBuildWarning);

        var when = new ComboBox
        {
            ItemsSource = new[] { Strings.ProjectOptions_PostBuildWhen_Success, Strings.ProjectOptions_PostBuildWhen_Always },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
        };
        AutomationProperties.SetAutomationId(when, f.Id("PostBuildWhen"));
        when.SelectionChanged += (_, _) =>
        {
            if (alive && !syncing && when.SelectedIndex >= 0)
                model.PostBuildWhen = when.SelectedIndex == 1 ? FreexComp.Build.PostBuildWhen.Always : FreexComp.Build.PostBuildWhen.Success;
        };

        // UI-DLG-302: the menu button; its menu opens below it and names every macro of the engine.
        var insert = new Button { Content = Strings.ProjectOptions_InsertMacro + " ▾", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetAutomationId(insert, f.Id("InsertMacro"));
        AutomationProperties.SetName(insert, Strings.ProjectOptions_InsertMacro.Replace("_", "", StringComparison.Ordinal));
        var menu = new MenuFlyout { Placement = PlacementMode.Bottom };
        insert.Flyout = menu;
        {
            foreach (var macro in model.EventMacroItems)
            {
                var text = new TextBlock { Text = macro.Text, FontSize = 13, MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
                text.Bind(TextBlock.FontFamilyProperty, text.GetResourceObservable("FxcFontMono"));
                var description = new TextBlock { Text = macro.Description, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                description.Bind(TextBlock.ForegroundProperty, description.GetResourceObservable("FxcTextSecondaryBrush"));
                var item = new MenuItem { Header = new StackPanel { Orientation = Orientation.Horizontal, Children = { text, description } } };
                AutomationProperties.SetAutomationId(item, f.Id("InsertMacro." + macro.Name));
                AutomationProperties.SetName(item, macro.Text + " " + macro.Description);
                item.Click += (_, _) =>
                {
                    var target = model.LastFocusedEventsField == EventsField.PreBuild ? pre : post;
                    if (!alive || model.InsertMacro(macro.Name, target.SelectionStart, target.SelectionEnd) is not { } caret)
                        return;
                    // The focus returns to the list, the caret stands after the inserted text.
                    target.Focus();
                    target.SelectionStart = target.SelectionEnd = target.CaretIndex = caret;
                };
                menu.Items.Add(item);
            }
        }

        void SyncList(TextBox box, string text, bool readOnly, bool controlCharacters)
        {
            if ((box.Text ?? "") != text)
                box.Text = text;
            box.IsReadOnly = readOnly;
            // UI-DLG-301, 303: read-only text is text.secondary (a list shown with glyphs: text.disabled) and has no caret.
            box.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty,
                box.GetResourceObservable(!readOnly ? "FxcTextPrimaryBrush" : controlCharacters ? "FxcTextDisabledBrush" : "FxcTextSecondaryBrush"));
            if (readOnly)
                box.CaretBrush = Brushes.Transparent;
            else
                box.ClearValue(TextBox.CaretBrushProperty);
        }

        void Sync()
        {
            if (!alive)
                return;
            syncing = true;
            own.IsVisible = model.ShowOwnEvents;
            own.IsChecked = model.OwnEvents;
            inherited.IsVisible = model.EventsInheritedText is not null;
            inherited.Text = model.EventsInheritedText is { } source ? "ⓘ " + source : "";
            SyncList(pre, model.PreBuild, model.PreBuildReadOnly, model.PreBuildWarning is not null);
            SyncList(post, model.PostBuild, model.PostBuildReadOnly, model.PostBuildWarning is not null);
            when.SelectedIndex = model.PostBuildWhen == FreexComp.Build.PostBuildWhen.Always ? 1 : 0;
            when.IsEnabled = model.PostBuildWhenEnabled;
            insert.IsEnabled = model.CanInsertMacro;
            syncing = false;
        }

        model.PropertyChanged += (_, _) => Sync();
        Sync();

        // UI-DLG-085: the label suffix "(Release)" while a configuration is selected. A long suffix moves to the next
        // line, and one that is longer than the page is shortened — nothing of the row leaves the page.
        var note = model.ShowOwnEvents ? model.ConfigurationNote : null;
        WrapPanel Labelled(string text, Control target)
        {
            var panel = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(DialogFieldBuilder.Label(text, target));
            if (note is not null)
            {
                var suffix = new TextBlock { Text = note, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                suffix.Bind(TextBlock.ForegroundProperty, suffix.GetResourceObservable("FxcTextTertiaryBrush"));
                ToolTip.SetTip(suffix, note);
                panel.Children.Add(suffix);
            }

            return panel;
        }

        // The label, its suffix and the combo flow in the room left of the menu button, which keeps the right end.
        var whenCell = Labelled(Strings.ProjectOptions_PostBuildWhen, when);
        when.Margin = new Thickness(8, 0, 0, 0);
        whenCell.Children.Add(when);
        var whenRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(insert, 1);
        whenRow.Children.Add(whenCell);
        whenRow.Children.Add(insert);

        // UI-DLG-299: top to bottom; the two list fields share the free height equally (rows 4 and 7).
        Control[] rows =
        [
            hint, own, inherited,
            Labelled(Strings.ProjectOptions_PreBuild, pre), new ListHeight { Child = pre }, preMessage,
            Labelled(Strings.ProjectOptions_PostBuild, post), new ListHeight { Child = post }, postMessage,
            whenRow, trustHint,
        ];
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto,*,Auto,Auto,Auto") };
        for (var i = 0; i < rows.Length; i++)
        {
            // 8 between the parts; a label stands 4 above its field, a message line directly under it, the trust hint 4
            // under the row above. The check box brings its own room above and below (it is 32 high).
            var top = i switch { 0 or 2 => 0, 1 => 2, 5 or 8 => rows[i].Margin.Top, 4 or 7 or 10 => 4, _ => 8 };
            rows[i].Margin = new Thickness(rows[i].Margin.Left, top, rows[i].Margin.Right, 0);
            Grid.SetRow(rows[i], i);
            page.Children.Add(rows[i]);
        }

        page.DetachedFromVisualTree += (_, _) => alive = false;
        return page;
    }

    /// <summary>
    /// Holds a list field of the Build Events page: it asks for the least height only, whatever the text needs, so that the
    /// two fields get equal shares of the page's free height and scroll inside (UI-DLG-299).
    /// </summary>
    private sealed class ListHeight : Decorator
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            Child?.Measure(new Size(availableSize.Width, double.IsInfinity(availableSize.Height) ? EventListMinHeight : Math.Max(EventListMinHeight, availableSize.Height)));
            return new Size(Math.Min(Child?.DesiredSize.Width ?? 0, double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width), EventListMinHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
