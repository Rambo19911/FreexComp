using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using LineEndingKind = FreexComp.Ide.Core.Services.LineEndingKind;
using FreexComp.Ide.Editor;

namespace FreexComp.Ide.Views;

/// <summary>
/// The IDE Options dialog (dialogs.md §9), 820 × 580, resizable: on the left the filter (<c>Filter</c>) above the page
/// tree (<c>Pages</c>, rows <c>Page.&lt;PageId&gt;</c>; <c>FilterEmpty</c> when nothing matches); on the right the page
/// title (<c>PageTitle</c>), a separator, the later-version note (<c>LaterVersion</c>) and the page's fields
/// (<c>&lt;Field&gt;</c>, <c>Error.&lt;Field&gt;</c>, <c>&lt;Field&gt;.Reset</c>). Each page's content is built once and
/// swapped in when the page is selected.
/// </summary>
public sealed class OptionsWindow : DialogWindow
{
    private static readonly Lazy<IReadOnlyList<string>> MonospacedFamilies = new(FindMonospacedFamilies);

    public OptionsWindow(OptionsViewModel model)
        : base(model, Body(model))
    {
        model.SetFontFamilies(MonospacedFamilies.Value);
    }

    internal static string Id(string field) => AutomationIds.DialogField(OptionsViewModel.DialogName, field);

    private static Control Body(OptionsViewModel model)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("200,16,*") };

        // Left: filter above the page tree (UI-DLG-090).
        var left = new DockPanel();
        var filter = new TextBox { Text = model.Filter, PlaceholderText = Strings.Options_FilterPlaceholder, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(filter, Id("Filter"));
        AutomationProperties.SetName(filter, Strings.Options_FilterPlaceholder);
        filter.TextChanged += (_, _) => model.Filter = filter.Text ?? "";
        DockPanel.SetDock(filter, Avalonia.Controls.Dock.Top);
        left.Children.Add(filter);
        var empty = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 0, 8) };
        empty.Classes.Add("body");
        empty.Bind(TextBlock.ForegroundProperty, empty.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(empty, Id("FilterEmpty"));
        DockPanel.SetDock(empty, Avalonia.Controls.Dock.Top);
        left.Children.Add(empty);
        var pages = CreatePageList(model);
        var frame = new Border { BorderThickness = new Thickness(1), Child = pages };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("FxcBgEditorBrush"));
        left.Children.Add(frame);
        grid.Children.Add(left);

        // Right: title, separator, later-version note, page content.
        var right = new DockPanel();
        Grid.SetColumn(right, 2);
        var title = new TextBlock();
        title.Classes.Add("subtitle");
        title.Bind(TextBlock.ForegroundProperty, title.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(title, Id("PageTitle"));
        DockPanel.SetDock(title, Avalonia.Controls.Dock.Top);
        right.Children.Add(title);
        var separator = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 12) };
        separator.Bind(Border.BackgroundProperty, separator.GetResourceObservable("FxcBorderSubtleBrush"));
        DockPanel.SetDock(separator, Avalonia.Controls.Dock.Top);
        right.Children.Add(separator);
        var later = new TextBlock { Text = Strings.Msg_Options_LaterVersion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        later.Classes.Add("body");
        later.Bind(TextBlock.ForegroundProperty, later.GetResourceObservable("FxcStatusInfoBrush"));
        AutomationProperties.SetAutomationId(later, Id("LaterVersion"));
        DockPanel.SetDock(later, Avalonia.Controls.Dock.Top);
        right.Children.Add(later);
        var content = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        right.Children.Add(content);
        grid.Children.Add(right);

        var built = new Dictionary<OptionsPage, Control>();
        void ShowPage()
        {
            var page = model.SelectedPage;
            title.Text = page?.Title ?? "";
            later.IsVisible = page?.IsLaterPhase == true;
            if (page is null)
            {
                content.Content = null;
            }
            else
            {
                if (!built.TryGetValue(page, out var control))
                    built[page] = control = page.Id switch
                    {
                        OptionsViewModel.ColorsPage => ColorsPageView.Create(model.Colors),
                        OptionsViewModel.KeyMappingsPage => KeyMappingsPageView.Create(model.KeyMappings),
                        _ => CreatePage(model, page),
                    };
                control.IsEnabled = !page.IsLaterPhase;
                content.Content = control;
            }

            if (pages.ItemsSource is IEnumerable<OptionsTreeRow> rows && rows.FirstOrDefault(r => r.Page == page && page is not null) is { } row)
                pages.SelectedItem = row;
        }

        void ShowEmpty()
        {
            empty.Text = model.FilterEmptyText;
            empty.IsVisible = model.FilterEmptyText is not null;
        }

        ShowPage();
        ShowEmpty();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OptionsViewModel.SelectedPage))
                ShowPage();
            else if (e.PropertyName == nameof(OptionsViewModel.FilterEmptyText))
                ShowEmpty();
        };
        return grid;
    }

    private static ListBox CreatePageList(OptionsViewModel model)
    {
        var list = new ListBox
        {
            ItemsSource = model.Rows,
            SelectionMode = SelectionMode.Single,
            [AutomationProperties.NameProperty] = model.Title, // UI-A11Y-020: the page list is named after the dialog
            ItemTemplate = new FuncDataTemplate<OptionsTreeRow>((row, _) =>
            {
                var text = new TextBlock
                {
                    Text = row is null ? "" : (row.IsGroup ? "▾ " : "") + row.Title,
                    Margin = new Thickness(row?.Level * 16 ?? 0, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                text.Classes.Add("body");
                return text;
            }),
        };
        AutomationProperties.SetAutomationId(list, Id("Pages"));
        list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is not ListBoxItem item || item.DataContext is not OptionsTreeRow row)
                return;
            AutomationProperties.SetAutomationId(item, Id("Page." + row.Id));
            AutomationProperties.SetName(item, row.Title);
            item.IsVisible = row.IsVisible;
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(OptionsTreeRow.IsVisible))
                    item.IsVisible = row.IsVisible;
            };
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is OptionsTreeRow row)
            {
                model.SelectRow(row);
                // A group row selects its first page (UI-DLG-090): move the list selection there.
                if (model.SelectedPage is { } page && row.Page != page)
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => list.SelectedItem = model.Rows.First(r => r.Page == page));
            }
        };
        return list;
    }

    /// <summary>One page: label column, field column, reset column; check boxes span the first two columns.</summary>
    private static Control CreatePage(OptionsViewModel model, OptionsPage page)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 16, 0) }; // clear of the overlay scroll bar
        if (page.Id == OptionsViewModel.CodeTemplatesPage)
            panel.Children.Add(CodeTemplatesPageView.Create(model.CodeTemplates)); // dialogs.md §9.3: the grid above the check box
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        panel.Children.Add(grid);
        var row = 0;
        void AddRow(Control control, int column, int span = 1)
        {
            while (grid.RowDefinitions.Count <= row)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            Grid.SetColumnSpan(control, span);
            grid.Children.Add(control);
        }

        foreach (var field in page.Fields)
        {
            if (field.GroupHeading is { } heading)
            {
                var text = new TextBlock { Text = heading, Margin = new Thickness(0, 8, 0, 8) };
                text.Classes.Add("bodyStrong");
                text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("FxcTextPrimaryBrush"));
                AddRow(text, 0, 3);
                row++;
            }

            var editor = CreateField(field, out var label);
            if (label is null)
            {
                AddRow(editor, 0, 2);
            }
            else
            {
                AddRow(label, 0);
                AddRow(field.Kind == OptionFieldKind.Path ? WithBrowse(model, field, editor) : editor, 1);
            }

            AddRow(CreateReset(field), 2);
            row++;
            if (field.Kind is OptionFieldKind.Integer or OptionFieldKind.Path)
            {
                AddRow(CreateError(field), 1, 2);
                row++;
            }

            if (field.FolderCheck is not null)
            {
                AddRow(CreateStatus(field), 1, 2);
                row++;
            }

            if (field.Hint is { } hint)
            {
                AddRow(CreateHint(field, hint), 1, 2);
                row++;
            }
        }

        if (page.Id == OptionsViewModel.EditorDisplayPage)
            panel.Children.Add(CreatePreview(page));
        return panel;
    }

    private static Control CreateField(OptionField field, out Label? label)
    {
        Control editor;
        switch (field.Kind)
        {
            case OptionFieldKind.Check:
                var check = new CheckBox { Content = field.Label, IsChecked = (bool)field.Value, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 2) };
                check.IsCheckedChanged += (_, _) =>
                {
                    if (check.IsChecked is bool value && !value.Equals(field.Value))
                        field.Value = value;
                };
                field.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(OptionField.Value))
                        check.IsChecked = (bool)field.Value;
                };
                Highlight(check, field);
                editor = check;
                label = null;
                break;
            case OptionFieldKind.Integer:
                var number = new NumericUpDown
                {
                    Value = (int)field.Value,
                    MinWidth = 120, // I-241: room for four digits (the widest value of any field); grows with a longer text, never clips it
                    HorizontalAlignment = HorizontalAlignment.Left,
                    ParsingNumberStyle = System.Globalization.NumberStyles.Integer,
                    FormatString = "0",
                    Increment = 1,
                    Margin = new Thickness(0, 2, 0, 2),
                };
                number.PropertyChanged += (_, e) =>
                {
                    if (e.Property == NumericUpDown.TextProperty && number.Text is { } text && text != field.Text)
                        field.Text = text;
                };
                field.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(OptionField.Text) && number.Text != field.Text)
                        number.Value = field.Error is null ? (int)field.Value : number.Value;
                    else if (e.PropertyName == nameof(OptionField.Error))
                        number.Classes.Set("invalid", field.Error is not null);
                };
                editor = number;
                label = CreateLabel(field, number);
                break;
            case OptionFieldKind.Path:
                var box = new TextBox { Text = field.Text, PlaceholderText = field.Placeholder, Margin = new Thickness(0, 2, 0, 2) };
                box.TextChanged += (_, _) =>
                {
                    if (box.Text is { } text && text != field.Text)
                        field.Text = text;
                };
                field.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(OptionField.Text) && box.Text != field.Text)
                        box.Text = field.Text;
                    else if (e.PropertyName == nameof(OptionField.Error))
                        box.Classes.Set("invalid", field.Error is not null);
                };
                editor = box;
                label = CreateLabel(field, box);
                break;
            default:
                var combo = new ComboBox
                {
                    ItemsSource = field.Choices,
                    MinWidth = 240,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 2, 0, 2),
                    ItemTemplate = new FuncDataTemplate<OptionChoice>((choice, _) =>
                    {
                        var text = new TextBlock { Text = choice?.Display };
                        if (choice is { Value.Length: > 0 })
                            text.FontFamily = new FontFamily(choice.Value); // each entry in its own font (§9.2)
                        return text;
                    }),
                };
                void Select() => combo.SelectedItem = field.Choices.FirstOrDefault(c => c.Value == field.ChoiceValue);
                Select();
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is OptionChoice choice && choice.Value != field.ChoiceValue)
                        field.ChoiceValue = choice.Value;
                };
                field.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(OptionField.Value))
                        Select();
                };
                field.Choices.CollectionChanged += (_, _) => Select();
                editor = combo;
                label = CreateLabel(field, combo);
                break;
        }

        AutomationProperties.SetAutomationId(editor, Id(field.Id));
        AutomationProperties.SetName(editor, field.LabelText);
        editor.IsEnabled = field.IsEnabled;
        if (label is not null)
            label.IsEnabled = field.IsEnabled;
        var fieldLabel = label;
        field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OptionField.IsEnabled))
            {
                editor.IsEnabled = field.IsEnabled;
                fieldLabel?.IsEnabled = field.IsEnabled;
            }
        };
        return editor;
    }

    /// <summary>A path field with its Browse button (<c>&lt;Field&gt;.Browse</c>, dialogs.md §24.2) at the right.</summary>
    private static Control WithBrowse(OptionsViewModel model, OptionField field, Control editor)
    {
        var browse = new Button { Content = "…", Margin = new Thickness(4, 2, 0, 2), VerticalAlignment = VerticalAlignment.Center, IsEnabled = field.IsEnabled };
        browse.Bind(HeightProperty, browse.GetResourceObservable("FxcMetricButtonHeight"));
        // I-242: the 26 px button holds its 18 px text line — no vertical padding, the label centred.
        browse.Bind(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, browse.GetResourceObservable("FxcSpace5Horizontal"));
        browse.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(browse, Id(field.Id + ".Browse"));
        AutomationProperties.SetName(browse, Strings.Options_Browse);
        ToolTip.SetTip(browse, Strings.Options_Browse);
        browse.Click += async (_, _) => await model.BrowseAsync(field);
        field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OptionField.IsEnabled))
                browse.IsEnabled = field.IsEnabled;
        };
        var panel = new DockPanel();
        DockPanel.SetDock(browse, Avalonia.Controls.Dock.Right);
        panel.Children.Add(browse);
        panel.Children.Add(editor);
        return panel;
    }

    /// <summary>A label whose mnemonic focuses <paramref name="target"/>; highlighted while it matches the filter.</summary>
    private static Label CreateLabel(OptionField field, Control target)
    {
        var label = new Label { Content = field.Label, Target = target, Padding = new Thickness(2, 0, 2, 0), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        Highlight(label, field);
        return label;
    }

    /// <summary>UI-DLG-091: matching labels get the <c>editor.findMatch.bg</c> background.</summary>
    private static void Highlight(TemplatedControl control, OptionField field)
    {
        IDisposable? binding = null;
        void Apply()
        {
            binding?.Dispose();
            binding = null;
            if (field.IsHighlighted)
                binding = control.Bind(TemplatedControl.BackgroundProperty, control.GetResourceObservable("FxcEditorFindMatchBgBrush"));
            else
                control.ClearValue(TemplatedControl.BackgroundProperty);
        }

        Apply();
        field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OptionField.IsHighlighted))
                Apply();
        };
    }

    /// <summary>UI-DLG-094: ↺, shown while the pending value differs from the key's default.</summary>
    private static Button CreateReset(OptionField field)
    {
        var reset = new Button
        {
            Content = "↺",
            Width = 16,
            Height = 16,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0), // the 16 px box holds the glyph line
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 11,
            IsVisible = field.CanReset,
        };
        AutomationProperties.SetAutomationId(reset, Id(field.Id + ".Reset"));
        AutomationProperties.SetName(reset, field.ResetToolTip);
        ToolTip.SetTip(reset, field.ResetToolTip);
        reset.Click += (_, _) => field.Reset();
        field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OptionField.CanReset))
                reset.IsVisible = field.CanReset;
        };
        return reset;
    }

    /// <summary>The message under a field (UI-DLG-006).</summary>
    private static TextBlock CreateError(OptionField field)
    {
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        message.Classes.Add("caption");
        message.Bind(TextBlock.ForegroundProperty, message.GetResourceObservable("FxcStatusErrorBrush"));
        AutomationProperties.SetAutomationId(message, Id("Error." + field.Id));
        void Show()
        {
            message.Text = field.Error;
            message.IsVisible = field.Error is not null;
        }

        Show();
        field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OptionField.Error))
                Show();
        };
        return message;
    }

    /// <summary>The fixed explanation under a field (<c>&lt;Field&gt;.Hint</c>), in <c>text.secondary</c>.</summary>
    private static TextBlock CreateHint(OptionField field, string text)
    {
        var hint = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        hint.Classes.Add("caption");
        hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(hint, Id(field.Id + ".Hint"));
        return hint;
    }

    /// <summary>
    /// A toolchain folder's status line (<c>&lt;Field&gt;.Status</c>, dialogs.md §9.9): tick + found files in
    /// <c>status.success</c>, or the warning glyph + <c>Msg.Options.ToolchainAutoNotFound</c> in <c>status.warning</c>.
    /// </summary>
    private static TextBlock CreateStatus(OptionField field)
    {
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        status.Classes.Add("caption");
        AutomationProperties.SetAutomationId(status, Id(field.Id + ".Status"));
        IDisposable? brush = null;
        void Show()
        {
            status.Text = field.Status is null ? null : (field.StatusIsWarning ? "⚠ " : "✔ ") + field.Status;
            status.IsVisible = field.Status is not null;
            brush?.Dispose();
            brush = status.Bind(TextBlock.ForegroundProperty,
                status.GetResourceObservable(field.StatusIsWarning ? "FxcStatusWarningBrush" : "FxcStatusSuccessBrush"));
        }

        Show();
        field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OptionField.Status) or nameof(OptionField.StatusIsWarning))
                Show();
        };
        return status;
    }

    /// <summary>Editor › Display preview (§9.2): the first 6 sample lines, reflecting this page's pending values.</summary>
    private static Control CreatePreview(OptionsPage page)
    {
        var text = string.Join("\n", OptionsViewModel.PreviewSample.Take(6));
        var editor = new TextEditor
        {
            Document = new AvaloniaEdit.Document.TextDocument(text),
            IsReadOnly = true,
            Height = 136,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        editor.Bind(TextEditor.BackgroundProperty, editor.GetResourceObservable("FxcBgEditorBrush"));
        editor.Bind(TextEditor.ForegroundProperty, editor.GetResourceObservable("FxcSyntaxTextBrush"));
        editor.Bind(TextEditor.LineNumbersForegroundProperty, editor.GetResourceObservable("FxcEditorLineNumberBrush"));
        editor.TextArea.TextView.Bind(AvaloniaEdit.Rendering.TextView.ColumnRulerPenProperty,
            editor.GetResourceObservable("FxcEditorRulerBrush", brush => brush is IBrush b ? new Pen(b, 1) : null));
        editor.TextArea.TextView.LineTransformers.Add(
            new SyntaxColorizer(new StringTextBuffer(text, LineEndingKind.Lf), new LanguageRegistry().GetById("pascal")!.CreateClassifier(), editor));
        AutomationProperties.SetAutomationId(editor, Id("Preview"));
        AutomationProperties.SetName(editor, Strings.Options_Preview);

        OptionField F(string id) => page.Fields.Single(f => f.Id == id);
        IDisposable? monoBinding = null;
        void Apply()
        {
            var family = (string)F("FontFamily").Value;
            monoBinding?.Dispose();
            monoBinding = family.Length == 0
                ? editor.Bind(TextEditor.FontFamilyProperty, editor.GetResourceObservable("FxcFontMono"))
                : null;
            if (family.Length > 0)
                editor.FontFamily = new FontFamily(family);
            editor.FontSize = (int)F("FontSize").Value;
            editor.ShowLineNumbers = (bool)F("ShowLineNumbers").Value;
            editor.WordWrap = (bool)F("WordWrap").Value;
            var whitespace = (bool)F("ShowWhitespace").Value;
            editor.Options.ShowSpaces = whitespace;
            editor.Options.ShowTabs = whitespace;
            editor.Options.HighlightCurrentLine = (bool)F("HighlightCurrentLine").Value;
            var margin = (int)F("RightMargin").Value;
            editor.Options.ShowColumnRulers = margin > 0;
            editor.Options.ColumnRulerPositions = [margin];
        }

        Apply();
        foreach (var field in page.Fields)
        {
            field.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(OptionField.Value))
                    Apply();
            };
        }

        var label = new TextBlock { Text = Strings.Options_Preview, Margin = new Thickness(0, 12, 0, 4) };
        label.Classes.Add("bodyStrong");
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        var frame = new Border { BorderThickness = new Thickness(1), Child = editor };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));
        var panel = new StackPanel();
        panel.Children.Add(label);
        panel.Children.Add(frame);
        return panel;
    }

    /// <summary>Installed families whose glyphs <c>i</c> and <c>W</c> have equal advance (§9.2).</summary>
    private static IReadOnlyList<string> FindMonospacedFamilies()
    {
        var result = new List<string>();
        foreach (var family in FontManager.Current.SystemFonts)
        {
            try
            {
                var glyphs = new Typeface(family).GlyphTypeface;
                var map = glyphs.CharacterToGlyphMap;
                if (map.TryGetGlyph('i', out var i) && map.TryGetGlyph('W', out var w) && i != 0
                    && glyphs.TryGetHorizontalGlyphAdvance(i, out var iAdvance) && glyphs.TryGetHorizontalGlyphAdvance(w, out var wAdvance)
                    && iAdvance == wAdvance)
                    result.Add(family.Name);
            }
            catch (InvalidOperationException)
            {
                // a family the platform cannot load is not offered
            }
        }

        return result;
    }
}
