using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Editor;
using LineEndingKind = FreexComp.Ide.Core.Services.LineEndingKind;

namespace FreexComp.Ide.Views;

/// <summary>
/// Content of the Options › Colors page (dialogs.md §9.4): <c>Theme</c>, <c>ResetElement</c>, <c>ResetAll</c>; the
/// element list <c>Elements</c> (rows <c>Colors.Element.&lt;token&gt;</c>); <c>Color</c> (hex) with <c>Color.Swatch</c>,
/// which opens the colour popup <c>Colors.Palette</c> (UI-DLG-095); <c>Bold</c>, <c>Italic</c>, <c>Contrast</c> and the
/// <c>Preview</c> of the whole sample (UI-DLG-096), drawn with the selected theme's pending colours. Clicking a token in
/// the preview selects its element.
/// </summary>
internal static class ColorsPageView
{
    private static string Id(string field) => OptionsWindow.Id(field);

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    public static Control Create(ColorsPageViewModel model)
    {
        var updating = false;
        IDisposable? contrastBrush = null;

        // Top row: Theme on the left, the reset buttons on the right.
        var theme = new ComboBox { ItemsSource = new[] { Strings.Options_Theme_Light, Strings.Options_Theme_Dark }, MinWidth = 120 };
        AutomationProperties.SetAutomationId(theme, Id("Theme"));
        theme.SelectionChanged += (_, _) =>
        {
            if (!updating && theme.SelectedIndex >= 0)
                model.Theme = model.Themes[theme.SelectedIndex];
        };
        var resetElement = CreateButton("ResetElement", Strings.Options_Colors_ResetElement);
        resetElement.Click += (_, _) => model.ResetElement();
        var resetAll = CreateButton("ResetAll", Strings.Options_Colors_ResetAll);
        resetAll.Click += async (_, _) => await model.ResetAllAsync();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(resetElement);
        buttons.Children.Add(resetAll);
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Right);
        top.Children.Add(buttons);
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal };
        themeRow.Children.Add(Label(Strings.Options_Colors_Theme, theme));
        themeRow.Children.Add(theme);
        top.Children.Add(themeRow);

        // Left: the element list.
        var elements = CreateElementList(model);
        var left = new DockPanel { Width = 240 };
        var elementLabel = Label(Strings.Options_Colors_Element, elements);
        DockPanel.SetDock(elementLabel, Avalonia.Controls.Dock.Top);
        left.Children.Add(elementLabel);
        var frame = new Border { BorderThickness = new Thickness(1), Child = elements, Height = 330, Margin = new Thickness(0, 4, 0, 0) };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("FxcBgEditorBrush"));
        left.Children.Add(frame);

        // Right: colour, style, contrast, preview.
        var hex = new TextBox { Width = 100, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(hex, Id("Color"));
        hex.TextChanged += (_, _) =>
        {
            if (!updating)
                model.ColorText = hex.Text ?? "";
        };
        var swatch = new Button { Width = 26, Height = 26, Padding = new Thickness(0), BorderThickness = new Thickness(1) };
        swatch.Bind(TemplatedControl.BorderBrushProperty, swatch.GetResourceObservable("FxcBorderDefaultBrush"));
        AutomationProperties.SetAutomationId(swatch, Id("Color.Swatch"));
        AutomationProperties.SetName(swatch, Strings.Options_Colors_Swatch);
        var popup = CreatePalette(model, swatch);
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
        colorRow.Children.Add(Label(Strings.Options_Colors_Color, hex));
        colorRow.Children.Add(swatch);
        colorRow.Children.Add(hex);
        colorRow.Children.Add(popup);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        error.Classes.Add("caption");
        error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable("FxcStatusErrorBrush"));
        AutomationProperties.SetAutomationId(error, Id("Error.Color"));

        var bold = new CheckBox { Content = Strings.Options_Colors_Bold, Margin = new Thickness(0, 8, 16, 0) };
        AutomationProperties.SetAutomationId(bold, Id("Bold"));
        bold.IsCheckedChanged += (_, _) =>
        {
            if (!updating)
                model.Bold = bold.IsChecked == true;
        };
        var italic = new CheckBox { Content = Strings.Options_Colors_Italic, Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetAutomationId(italic, Id("Italic"));
        italic.IsCheckedChanged += (_, _) =>
        {
            if (!updating)
                model.Italic = italic.IsChecked == true;
        };
        var styleRow = new StackPanel { Orientation = Orientation.Horizontal };
        styleRow.Children.Add(bold);
        styleRow.Children.Add(italic);

        var warning = new TextBlock { Text = "⚠ ", VerticalAlignment = VerticalAlignment.Center };
        warning.Bind(TextBlock.ForegroundProperty, warning.GetResourceObservable("FxcStatusWarningBrush"));
        var contrast = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        contrast.Classes.Add("body");
        AutomationProperties.SetAutomationId(contrast, Id("Contrast"));
        var contrastLabel = new TextBlock { Text = Strings.Options_Colors_Contrast, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        contrastLabel.Classes.Add("body");
        contrastLabel.Bind(TextBlock.ForegroundProperty, contrastLabel.GetResourceObservable("FxcTextPrimaryBrush"));
        var contrastRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        contrastRow.Children.Add(contrastLabel);
        contrastRow.Children.Add(warning);
        contrastRow.Children.Add(contrast);

        var (preview, redraw) = CreatePreview(model);
        var previewLabel = new TextBlock { Text = Strings.Options_Preview, Margin = new Thickness(0, 12, 0, 4) };
        previewLabel.Classes.Add("bodyStrong");
        previewLabel.Bind(TextBlock.ForegroundProperty, previewLabel.GetResourceObservable("FxcTextPrimaryBrush"));
        var previewFrame = new Border { BorderThickness = new Thickness(1), Child = preview };
        previewFrame.Bind(Border.BorderBrushProperty, previewFrame.GetResourceObservable("FxcBorderDefaultBrush"));

        var right = new StackPanel { Margin = new Thickness(16, 0, 16, 0) };
        foreach (var control in new Control[] { colorRow, error, styleRow, contrastRow, previewLabel, previewFrame })
            right.Children.Add(control);

        var body = new DockPanel();
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top);
        body.Children.Add(top);
        DockPanel.SetDock(left, Avalonia.Controls.Dock.Left);
        body.Children.Add(left);
        body.Children.Add(right);

        void Update()
        {
            updating = true;
            try
            {
                theme.SelectedIndex = model.IsDarkTheme ? 1 : 0;
                elements.SelectedItem = model.SelectedElement;
                if (hex.Text != model.ColorText)
                    hex.Text = model.ColorText;
                hex.Classes.Set("invalid", model.ColorError is not null);
                error.Text = model.ColorError;
                error.IsVisible = model.ColorError is not null;
                if (model.SelectedElement?.Token is { } token)
                    swatch.Background = Brush(model.ColorOf(model.Theme, token));
                bold.IsChecked = model.Bold;
                italic.IsChecked = model.Italic;
                bold.IsEnabled = italic.IsEnabled = model.CanEditStyle;
                contrastRow.IsVisible = model.ShowContrast;
                contrast.Text = model.ContrastText;
                warning.IsVisible = model.IsContrastLow;
                contrastBrush?.Dispose();
                contrastBrush = contrast.Bind(TextBlock.ForegroundProperty, contrast.GetResourceObservable(model.IsContrastLow ? "FxcStatusWarningBrush" : "FxcTextPrimaryBrush"));
                resetElement.IsEnabled = model.CanResetElement;
            }
            finally
            {
                updating = false;
            }
        }

        Update();
        model.PropertyChanged += (_, _) => Update();
        model.PendingChanged += (_, _) =>
        {
            redraw();
            if (model.SelectedElement?.Token is { } token)
                swatch.Background = Brush(model.ColorOf(model.Theme, token));
        };
        elements.SelectionChanged += (_, _) =>
        {
            if (!updating && elements.SelectedItem is ColorElement element)
                model.SelectedElement = element;
        };
        swatch.Click += (_, _) => popup.IsOpen = true;
        return body;
    }

    private static Button CreateButton(string field, string label)
    {
        var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Bind(Layoutable.MinWidthProperty, button.GetResourceObservable("FxcMetricButtonMinWidth"));
        button.Bind(Layoutable.HeightProperty, button.GetResourceObservable("FxcMetricButtonHeight"));
        // I-242: the 26 px button holds its 18 px text line — no vertical padding, the label centred.
        button.Bind(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, button.GetResourceObservable("FxcSpace5Horizontal"));
        button.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(button, Id(field));
        AutomationProperties.SetName(button, label.Replace("_", "", StringComparison.Ordinal));
        return button;
    }

    private static Label Label(string text, Control target)
    {
        var label = new Label { Content = text, Target = target, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(TemplatedControl.ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        return label;
    }

    /// <summary>Two-level list (group headings, then their tokens); overridden rows in <c>type.bodyStrong</c>; type-ahead.</summary>
    private static ListBox CreateElementList(ColorsPageViewModel model)
    {
        var list = new ListBox
        {
            ItemsSource = model.Elements,
            SelectionMode = SelectionMode.Single,
            IsTextSearchEnabled = true, // UI-A11Y-007
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel()),
            ItemTemplate = new FuncDataTemplate<ColorElement>((element, _) =>
            {
                var text = new TextBlock { Text = element?.Label, Margin = new Thickness(element?.IsGroup == false ? 16 : 0, 0, 0, 0) };
                void Weight()
                {
                    text.Classes.Set("body", element is { IsGroup: false, IsOverridden: false });
                    text.Classes.Set("bodyStrong", element is { IsGroup: true } or { IsOverridden: true });
                }

                Weight();
                if (element is not null)
                {
                    element.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(ColorElement.IsOverridden))
                            Weight();
                    };
                }

                return text;
            }),
        };
        AutomationProperties.SetAutomationId(list, Id("Elements"));
        list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is not ListBoxItem item || item.DataContext is not ColorElement element)
                return;
            item.Padding = new Thickness(8, 2, 8, 2);
            item.MinHeight = 0;
            AutomationProperties.SetAutomationId(item, Id(element.IsGroup ? "Colors.Group." + element.Label : "Colors.Element." + element.Token));
            AutomationProperties.SetName(item, element.Label);
            TextSearch.SetText(item, element.Label);
        };
        return list;
    }

    /// <summary>UI-DLG-095: the colour popup under the swatch.</summary>
    private static Popup CreatePalette(ColorsPageViewModel model, Button swatch)
    {
        var popup = new Popup { PlacementTarget = swatch, Placement = PlacementMode.Bottom, IsLightDismissEnabled = true };
        var swatches = new List<Button>();
        var grid = new UniformGrid { Columns = 10 };
        foreach (var color in ColorsPageViewModel.Palette)
        {
            var button = new Button { Width = 20, Height = 20, Margin = new Thickness(1), Padding = new Thickness(0), Background = Brush(color), BorderThickness = new Thickness(0) };
            AutomationProperties.SetAutomationId(button, Id("Colors.Palette.Swatch." + color[1..]));
            AutomationProperties.SetName(button, color);
            ToolTip.SetTip(button, color);
            button.Click += (_, _) => Pick(color);
            swatches.Add(button);
            grid.Children.Add(button);
        }

        var defaultButton = new Button { Content = Strings.Options_Colors_Default, Margin = new Thickness(1, 6, 1, 1), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(defaultButton, Id("Colors.Palette.Default"));
        defaultButton.Click += (_, _) =>
        {
            if (model.DefaultColor is { } color)
                Pick(color);
        };
        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(defaultButton);
        var root = new Border { Child = panel, Padding = new Thickness(6), BorderThickness = new Thickness(1) };
        root.Bind(Border.BackgroundProperty, root.GetResourceObservable("FxcBgRaisedBrush"));
        root.Bind(Border.BorderBrushProperty, root.GetResourceObservable("FxcBorderDefaultBrush"));
        AutomationProperties.SetAutomationId(root, Id("Colors.Palette"));
        AutomationProperties.SetName(root, Strings.Options_Colors_Palette);
        popup.Child = root;

        // Arrow keys move, Enter picks, Escape closes (only the popup).
        var index = 0;
        root.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Left or Key.Right or Key.Up or Key.Down:
                    var step = e.Key switch { Key.Left => -1, Key.Right => 1, Key.Up => -10, _ => 10 };
                    index = Math.Clamp(index + step, 0, swatches.Count - 1);
                    swatches[index].Focus(NavigationMethod.Directional);
                    break;
                case Key.Enter:
                    Pick(ColorsPageViewModel.Palette[index]);
                    break;
                case Key.Escape:
                    Close();
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        popup.Opened += (_, _) =>
        {
            var current = model.SelectedElement?.Token is { } token ? model.ColorOf(model.Theme, token) : null;
            index = Math.Max(0, ColorsPageViewModel.Palette.ToList().IndexOf(current ?? ""));
            swatches[index].Focus(NavigationMethod.Tab);
        };

        void Close()
        {
            popup.IsOpen = false;
            swatch.Focus(NavigationMethod.Tab);
        }

        void Pick(string color)
        {
            model.PickColor(color);
            Close();
        }

        return popup;
    }

    /// <summary>The full sample (UI-DLG-096) coloured with the selected theme's pending colours and styles.</summary>
    private static (TextEditor Editor, Action Redraw) CreatePreview(ColorsPageViewModel model)
    {
        var text = string.Join("\n", OptionsViewModel.PreviewSample);
        var buffer = new StringTextBuffer(text, LineEndingKind.Lf);
        var classifier = new LanguageRegistry().GetById("pascal")!.CreateClassifier();
        var editor = new TextEditor
        {
            Document = new AvaloniaEdit.Document.TextDocument(text),
            IsReadOnly = true,
            Height = 200,
            ShowLineNumbers = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        editor.Bind(TextEditor.FontFamilyProperty, editor.GetResourceObservable("FxcFontMono"));
        editor.Bind(TextEditor.FontSizeProperty, editor.GetResourceObservable("FxcTypeCodeSize"));
        AutomationProperties.SetAutomationId(editor, Id("Preview"));
        AutomationProperties.SetName(editor, Strings.Options_Preview);
        var colorizer = new SyntaxColorizer(buffer, classifier, token => (Brush(model.ColorOf(model.Theme, token)), model.StyleOf(model.Theme, token)));
        editor.TextArea.TextView.LineTransformers.Add(colorizer);
        editor.Options.HighlightCurrentLine = true;

        // The selection on line 5 and the caret line 6 show the Editor elements (UI-DLG-096).
        var line5 = editor.Document.GetLineByNumber(5);
        editor.TextArea.Selection = AvaloniaEdit.Editing.Selection.Create(editor.TextArea, line5.Offset, line5.EndOffset);
        editor.TextArea.Caret.Line = 6;

        void Redraw()
        {
            string Color(string token) => model.ColorOf(model.Theme, token);
            editor.Background = Brush(model.EditorBackground);
            editor.Foreground = Brush(Color("syntax.text"));
            editor.LineNumbersForeground = Brush(Color("editor.lineNumber"));
            editor.TextArea.SelectionBrush = Brush(Color("editor.selection.bg"));
            editor.TextArea.CaretBrush = Brush(Color("editor.caret"));
            editor.TextArea.TextView.CurrentLineBackground = Brush(Color("editor.currentLine.bg"));
            editor.TextArea.TextView.CurrentLineBorder = new Pen(Brush(Color("editor.currentLine.border")), 1);
            colorizer.ThemeChanged();
            editor.TextArea.TextView.Redraw();
        }

        Redraw();

        // A click on a token selects its element.
        editor.TextArea.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (editor.GetPositionFromPoint(e.GetPosition(editor)) is not { } position)
                return;
            var offset = editor.Document.GetOffset(position.Line, position.Column);
            var span = classifier.Classify(buffer, new LineRange(position.Line, position.Line))
                .FirstOrDefault(s => offset >= s.Offset && offset < s.Offset + s.Length);
            model.SelectToken(span.TokenClass ?? "syntax.text");
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        return (editor, Redraw);
    }
}
