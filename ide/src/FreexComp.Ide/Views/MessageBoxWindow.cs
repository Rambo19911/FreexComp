using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The generic message box (dialogs.md §2, UI-DLG-020…025): icon, paragraphs, optional Details expander, buttons
/// <c>Dialog.Message.Button.&lt;Id&gt;</c>. Width 440, height to content, not resizable.
/// </summary>
public sealed class MessageBoxWindow(MessageBoxViewModel model) : DialogWindow(model, Body(model))
{
    private const double TextMaxHeight = 240;
    private const double DetailsHeight = 160;

    protected override string ButtonAutomationId(DialogButtonViewModel button) =>
        AutomationIds.DialogField(MessageBoxViewModel.DialogName, MessageBoxViewModel.ButtonAutomationName(button.Id));

    private static string Id(string field) => AutomationIds.DialogField(MessageBoxViewModel.DialogName, field);

    private static Control Body(MessageBoxViewModel model)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var icon = CreateIcon(model.Kind, model.KindName, Id("Icon"));
        icon.Margin = new Thickness(0, 0, 16, 0); // space.6
        grid.Children.Add(icon);

        var column = new StackPanel();
        column.Bind(StackPanel.SpacingProperty, column.GetResourceObservable("FxcSpace4"));
        var paragraphs = new StackPanel();
        paragraphs.Bind(StackPanel.SpacingProperty, paragraphs.GetResourceObservable("FxcSpace4"));
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var paragraph in model.Paragraphs)
        {
            paragraphs.Children.Add(CreateParagraph(paragraph, named));
        }

        column.Children.Add(new ScrollViewer { Content = paragraphs, MaxHeight = TextMaxHeight, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        if (model.HasDetails)
        {
            column.Children.Add(CreateDetails(model));
        }

        Grid.SetColumn(column, 1);
        grid.Children.Add(column);
        return grid;
    }

    private static TextBlock CreateParagraph(MessageParagraph paragraph, HashSet<string> named)
    {
        var text = new TextBlock { Text = paragraph.Text, TextWrapping = TextWrapping.Wrap };
        var (field, textClass, brush) = paragraph.Style switch
        {
            MessageParagraphStyle.Path => ("Text.Path", "codeSmall", "FxcTextSecondaryBrush"),
            MessageParagraphStyle.Secondary => ("Text.Reason", "body", "FxcTextSecondaryBrush"),
            _ => ("Text", "body", "FxcTextPrimaryBrush"),
        };
        text.Classes.Add(textClass);
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(brush));
        if (paragraph.Style == MessageParagraphStyle.Path)
        {
            text.TextWrapping = TextWrapping.NoWrap;
            text.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
            ToolTip.SetTip(text, paragraph.Text);
        }

        if (named.Add(field))
        {
            AutomationProperties.SetAutomationId(text, Id(field));
        }

        return text;
    }

    private static Expander CreateDetails(MessageBoxViewModel model)
    {
        var box = new TextBox
        {
            Text = model.Details,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = DetailsHeight,
        };
        box.Bind(TextBox.FontFamilyProperty, box.GetResourceObservable("FxcFontMono"));
        box.Bind(TextBox.FontSizeProperty, box.GetResourceObservable("FxcTypeCodeSmallSize"));
        AutomationProperties.SetAutomationId(box, Id("Details.Text"));
        AutomationProperties.SetName(box, Strings.MessageBox_Details);

        var expander = new Expander
        {
            Header = Strings.MessageBox_Details,
            Content = box,
            IsExpanded = model.IsDetailsExpanded,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(expander, Id("Details"));
        AutomationProperties.SetName(expander, Strings.MessageBox_Details);
        expander.PropertyChanged += (_, e) =>
        {
            if (e.Property == Expander.IsExpandedProperty)
                model.IsDetailsExpanded = expander.IsExpanded;
        };
        return expander;
    }

    /// <summary>UI-DLG-022: original outline glyphs, 2 px, 32 × 32 (<c>icon.large</c>).</summary>
    internal static Control CreateIcon(MessageKind kind, string kindName, string automationId)
    {
        var (outline, mark, dot, brush) = kind switch
        {
            MessageKind.Question => ("M3,16 A13,13 0 1,0 29,16 A13,13 0 1,0 3,16 Z", "M12,12.5 A4,4 0 1,1 18.2,15.8 C16.6,16.9 16,17.9 16,19.5", (16.0, 23.5), "FxcStatusInfoBrush"),
            MessageKind.Info => ("M3,16 A13,13 0 1,0 29,16 A13,13 0 1,0 3,16 Z", "M16,14 V23", (16.0, 9.5), "FxcStatusInfoBrush"),
            MessageKind.Warning => ("M16,3.5 L29.5,28 H2.5 Z", "M16,12 V19.5", (16.0, 23.5), "FxcStatusWarningBrush"),
            _ => ("M10.6,3 H21.4 L29,10.6 V21.4 L21.4,29 H10.6 L3,21.4 V10.6 Z", "M11.5,11.5 L20.5,20.5 M20.5,11.5 L11.5,20.5", (double.NaN, double.NaN), "FxcStatusErrorBrush"),
        };

        var canvas = new Canvas { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top };
        canvas.Bind(WidthProperty, canvas.GetResourceObservable("FxcIconLarge"));
        canvas.Bind(HeightProperty, canvas.GetResourceObservable("FxcIconLarge"));
        foreach (var data in new[] { outline, mark })
        {
            var path = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(data), StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
            path.Bind(Shape.StrokeProperty, path.GetResourceObservable(brush));
            canvas.Children.Add(path);
        }

        if (!double.IsNaN(dot.Item1))
        {
            var point = new Ellipse { Width = 2.6, Height = 2.6 };
            Canvas.SetLeft(point, dot.Item1 - 1.3);
            Canvas.SetTop(point, dot.Item2 - 1.3);
            point.Bind(Shape.FillProperty, point.GetResourceObservable(brush));
            canvas.Children.Add(point);
        }

        AutomationProperties.SetAutomationId(canvas, automationId);
        AutomationProperties.SetName(canvas, kindName);
        return canvas;
    }
}
