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
