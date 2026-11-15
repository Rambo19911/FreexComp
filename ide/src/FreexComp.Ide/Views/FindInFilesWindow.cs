using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Find in Files dialog (dialogs.md §11), 560 × auto. Fields <c>SearchText</c>, <c>MatchCase</c>, <c>WholeWord</c>,
/// <c>Regex</c>, the scope radio buttons <c>Scope.Project|OpenFiles|Folder</c>, <c>Folder</c> (+ <c>.Browse</c>),
/// <c>Subfolders</c>, <c>FileTypes</c>; Up/Down in a text field step through its history. Initial focus on the search text.
/// </summary>
public sealed class FindInFilesWindow : DialogWindow
{
    public FindInFilesWindow(FindInFilesViewModel model)
        : this(model, new DialogFieldBuilder(FindInFilesViewModel.DialogName))
    {
    }

    private FindInFilesWindow(FindInFilesViewModel model, DialogFieldBuilder fields)
        : base(model, Body(fields, model))
    {
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<TextBox>().FirstOrDefault() is { } box)
            {
                box.Focus(Avalonia.Input.NavigationMethod.Tab);
                box.SelectAll();
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private static StackPanel Body(DialogFieldBuilder fields, FindInFilesViewModel model)
    {
        var text = fields.Text("SearchText", () => model.SearchText, v => model.SearchText = v, code: true);
        WithHistory(text, model.SearchHistory);
        var options = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children =
            {
                Check(fields, "MatchCase", Strings.FindInFiles_MatchCase, () => model.MatchCase, v => model.MatchCase = v),
                Check(fields, "WholeWord", Strings.FindInFiles_WholeWord, () => model.WholeWord, v => model.WholeWord = v),
                Check(fields, "Regex", Strings.FindInFiles_Regex, () => model.Regex, v => model.Regex = v),
            },
        };

        var project = Radio(fields, model, FindInFilesScope.Project, Strings.FindInFiles_ScopeProject);
        project.IsEnabled = model.HasProject;
        var folder = fields.Text("Folder", () => model.Folder, v => model.Folder = v);
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FindInFilesViewModel.Folder) && folder.Text != model.Folder)
                folder.Text = model.Folder;
        };
        var subfolders = Check(fields, "Subfolders", Strings.FindInFiles_Subfolders, () => model.Subfolders, v => model.Subfolders = v);
        var folderRow = fields.WithBrowse("Folder", folder, model.BrowseAsync);
        void SyncEnabled()
        {
            folderRow.IsEnabled = model.IsFolderScope;
            subfolders.IsEnabled = model.IsFolderScope;
        }

        model.PropertyChanged += (_, _) => SyncEnabled();
        SyncEnabled();
        var folderGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var folderRadio = Radio(fields, model, FindInFilesScope.Folder, Strings.FindInFiles_ScopeFolder);
        folderRadio.MinWidth = 120;
        folderGrid.Children.Add(folderRadio);
        Grid.SetColumn(folderRow, 1);
        folderGrid.Children.Add(folderRow);
        var lookIn = new TextBlock { Text = Strings.FindInFiles_LookIn };
        lookIn.Bind(TextBlock.ForegroundProperty, lookIn.GetResourceObservable("FxcTextPrimaryBrush"));

        var types = fields.Text("FileTypes", () => model.FileTypes, v => model.FileTypes = v, code: true);
        WithHistory(types, model.FileTypesHistory);
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new StackPanel
                {
                    Children =
                    {
                        DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.FindInFiles_SearchText, text), text),
                        fields.Message("SearchText", model, () => model.SearchTextMessage, () => true, text),
                    },
                },
                options,
                lookIn,
                new StackPanel
                {
                    Margin = new Thickness(16, 0, 0, 0),
                    Spacing = 4,
                    Children =
                    {
                        project,
                        Radio(fields, model, FindInFilesScope.OpenFiles, Strings.FindInFiles_ScopeOpenFiles),
                        folderGrid,
                        fields.Message("Folder", model, () => model.FolderMessage, () => true, folder),
                        new Border { Margin = new Thickness(120, 0, 0, 0), Child = subfolders },
                    },
                },
                DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.FindInFiles_FileTypes, types), types),
            },
        };
    }

    private static CheckBox Check(DialogFieldBuilder fields, string field, string label, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = get() };
        AutomationProperties.SetAutomationId(box, fields.Id(field));
        box.IsCheckedChanged += (_, _) => set(box.IsChecked == true);
        return box;
    }

    private static RadioButton Radio(DialogFieldBuilder fields, FindInFilesViewModel model, FindInFilesScope scope, string label)
    {
        var button = new RadioButton { Content = label, GroupName = "FindInFilesScope", IsChecked = model.Scope == scope };
        AutomationProperties.SetAutomationId(button, fields.Id("Scope." + scope));
        button.IsCheckedChanged += (_, _) =>
        {
            if (button.IsChecked == true)
                model.Scope = scope;
        };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FindInFilesViewModel.Scope))
                button.IsChecked = model.Scope == scope;
        };
        return button;
    }

    /// <summary>UI-DLG-009: Up/Down step through the field's history (most recent first).</summary>
    private static void WithHistory(TextBox box, IReadOnlyList<string> history)
    {
        var index = -1;
        box.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers != Avalonia.Input.KeyModifiers.None || history.Count == 0 || e.Key is not (Avalonia.Input.Key.Up or Avalonia.Input.Key.Down))
                return;
            index = Math.Clamp(index + (e.Key == Avalonia.Input.Key.Up ? 1 : -1), 0, history.Count - 1);
            box.Text = history[index];
            box.CaretIndex = box.Text.Length;
            e.Handled = true;
        };
    }
}
