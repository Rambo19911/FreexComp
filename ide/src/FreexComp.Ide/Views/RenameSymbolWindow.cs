using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The Rename Symbol dialog (UI-DLG-380…386), 520 × auto, not resizable: the symbol line; <c>New name</c> with its error
/// text, or the conflict list (at most five rows high) under it; the overload check box of an overloaded routine; the
/// summary of the live check and the notes; for a unit the line with the new file name (IDE-REFAC-057). First focus on the
/// name, all of it selected.
/// </summary>
public sealed class RenameSymbolWindow : DialogWindow
{
    /// <summary>The height of a list row (UI-DLG-384: the conflict list is at most five rows high).</summary>
    private const double RowHeight = 22;

    public RenameSymbolWindow(RenameSymbolViewModel model)
        : this(model, new TextBox())
    {
    }

    private RenameSymbolWindow(RenameSymbolViewModel model, TextBox name)
        : base(model, Body(model, name))
    {
        void FocusName()
        {
            name.Focus(Avalonia.Input.NavigationMethod.Tab);
            name.SelectAll();
        }

        Opened += (_, _) => Dispatcher.UIThread.Post(FocusName, DispatcherPriority.Loaded);
        model.FocusNameRequested += (_, _) => Dispatcher.UIThread.Post(FocusName, DispatcherPriority.Loaded);
    }

    private static string Id(string field) => AutomationIds.DialogField(RenameSymbolViewModel.DialogName, field);

    private static TextBlock Line(string field, string resource, bool wrap = false)
    {
        var block = new TextBlock { TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable(resource));
        AutomationProperties.SetAutomationId(block, Id(field));
        return block;
    }

    private static StackPanel Body(RenameSymbolViewModel model, TextBox name)
    {
        // UI-DLG-381: the symbol line, end-trimmed, its whole text as the tooltip.
        var symbol = Line("Symbol", "FxcTextSecondaryBrush");
        symbol.Text = model.Symbol;
        ToolTip.SetTip(symbol, model.Symbol);

        // UI-DLG-382: New name, the current name all selected.
        name.Text = model.NewName;
        name.MaxLength = RenameSymbolViewModel.MaxNameLength;
        AutomationProperties.SetAutomationId(name, Id("NewName"));
        name.TextChanged += (_, _) => model.NewName = name.Text ?? "";
        var label = new Label { Content = RenameSymbolViewModel.NewNameLabel, Target = name, Padding = new Thickness(0), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetLabeledBy(name, label);

        var error = Line("Error.NewName", "FxcStatusErrorBrush", wrap: true);
        error.Classes.Add("caption");
        error.Margin = new Thickness(0, 2, 0, 0);
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);

        // UI-DLG-384: the conflict list with its label, read only, at most five rows high.
        var conflictsLabel = Line("ConflictsLabel", "FxcTextPrimaryBrush", wrap: true);
        conflictsLabel.Text = Strings.Msg_Rename_Conflicts;
        conflictsLabel.Margin = new Thickness(0, 4, 0, 4);
        var conflicts = new ListBox { MaxHeight = 5 * RowHeight + 2 };
        conflicts.Bind(BorderBrushProperty, conflicts.GetResourceObservable("FxcBorderDefaultBrush"));
        conflicts.BorderThickness = new Thickness(1);
        AutomationProperties.SetAutomationId(conflicts, Id("Conflicts"));
        AutomationProperties.SetName(conflicts, Strings.Msg_Rename_Conflicts);
        ScrollViewer.SetHorizontalScrollBarVisibility(conflicts, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);

        // The label is centred on the text box; the message or the conflict list lies under the text box.
        var below = new StackPanel { Children = { error, conflictsLabel, conflicts } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        row.Children.Add(label);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        Grid.SetColumn(below, 1);
        Grid.SetRow(below, 1);
        row.Children.Add(below);

        var panel = new StackPanel { Spacing = 8, Children = { symbol, row } };

        // UI-DLG-383: only for an overloaded routine or method.
        if (model.ShowOverloads)
        {
            var overloads = new CheckBox { Content = model.OverloadsLabel, IsChecked = model.Overloads };
            AutomationProperties.SetAutomationId(overloads, Id("Overloads"));
            overloads.IsCheckedChanged += (_, _) => model.Overloads = overloads.IsChecked == true;
            model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RenameSymbolViewModel.IsInputEnabled))
                    overloads.IsEnabled = model.IsInputEnabled;
            };
            panel.Children.Add(overloads);
        }

        // IDE-REFAC-057: the unit mode's file line, while the name is valid.
        var file = Line("File", "FxcTextPrimaryBrush");
        if (model.IsUnitMode)
            panel.Children.Add(file);

        var summary = Line("Summary", "FxcTextPrimaryBrush");
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        var notes = Line("Notes", "FxcTextSecondaryBrush", wrap: true);
        panel.Children.Add(summary);
        panel.Children.Add(notes);

        void Update()
        {
            error.Text = model.Error;
            error.IsVisible = model.Error is not null;
            name.Classes.Set("invalid", model.IsInvalid);
            name.IsEnabled = model.IsInputEnabled;
            conflictsLabel.IsVisible = conflicts.IsVisible = model.HasConflicts;
            summary.Text = model.Summary;
            summary.IsVisible = model.IsSummaryVisible; // the list, or the designer note, replaces the summary (UI-DLG-384, I-410)
            notes.Text = model.Notes;
            notes.IsVisible = model.Notes is not null;
            file.Text = model.FileNote;
            file.IsVisible = model.FileNote is not null;
        }

        IReadOnlyList<RenameConflictRow>? shown = null;
        model.PropertyChanged += (_, e) =>
        {
            Update();
            if (e.PropertyName != nameof(RenameSymbolViewModel.Conflicts) || ReferenceEquals(shown, model.Conflicts))
                return;
            shown = model.Conflicts;
            conflicts.ItemsSource = shown.Select((c, i) =>
            {
                var text = new TextBlock { Text = c.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                var item = new ListBoxItem { Content = text, MinHeight = RowHeight, Height = RowHeight, Padding = new Thickness(6, 0) };
                AutomationProperties.SetAutomationId(item, Id("Conflicts.Row." + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                AutomationProperties.SetName(item, c.Text);
                ToolTip.SetTip(item, c.ToolTip);
                return item;
            }).ToList();
        };
        Update();
        return panel;
    }
}
