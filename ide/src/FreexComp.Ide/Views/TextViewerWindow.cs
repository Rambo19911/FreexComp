using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// Text Viewer (dialogs.md §32, UI-DLG-323…328), 720 × 480, resizable (minimum 400 × 260; the text grows): the
/// <c>Details</c> line, the read-only <c>Text</c> (an editor view, so that a text of many megabytes is appended chunk by
/// chunk and only its visible lines are laid out), the <c>Wrap</c> and <c>Hex</c> check boxes with the <c>Status</c> live
/// region right of them, and the buttons <c>Copy</c> and <c>Close</c>. Switching the hex view keeps the element at the top.
/// </summary>
public sealed class TextViewerWindow : DialogWindow
{
    public TextViewerWindow(TextViewerViewModel model)
        : this(model, new DialogFieldBuilder(TextViewerViewModel.DialogName))
    {
    }

    private TextViewerWindow(TextViewerViewModel model, DialogFieldBuilder fields)
        : this(model, fields, CreateEditor(model, fields))
    {
    }

    private TextViewerWindow(TextViewerViewModel model, DialogFieldBuilder fields, TextEditor editor)
        : base(model, Body(model, fields, editor))
    {
    }

    private static TextEditor CreateEditor(TextViewerViewModel model, DialogFieldBuilder fields)
    {
        var document = new TextDocument(model.DisplayText);
        document.UndoStack.SizeLimit = 0; // read-only: no undo copy of a large text
        var editor = new TextEditor
        {
            Document = document,
            IsReadOnly = true,
            ShowLineNumbers = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        editor.Bind(TextEditor.BackgroundProperty, editor.GetResourceObservable("FxcBgEditorBrush"));
        editor.Bind(TextEditor.ForegroundProperty, editor.GetResourceObservable("FxcTextPrimaryBrush"));
        editor.Bind(TextEditor.FontFamilyProperty, editor.GetResourceObservable("FxcFontMono"));
        editor.Bind(TextEditor.FontSizeProperty, editor.GetResourceObservable("FxcTypeCodeSize"));
        editor.TextArea.Bind(TextArea.SelectionBrushProperty, editor.TextArea.GetResourceObservable("FxcEditorSelectionBgBrush"));
        AutomationProperties.SetAutomationId(editor, fields.Id("Text"));
        AutomationProperties.SetName(editor, model.Title); // UI-DLG-328
        model.DisplayChanged += (_, e) =>
        {
            if (e.Keep < document.TextLength)
                document.Replace(e.Keep, document.TextLength - e.Keep, e.Appended);
            else
                document.Insert(document.TextLength, e.Appended);
        };
        return editor;
    }

    private static Grid Body(TextViewerViewModel model, DialogFieldBuilder fields, TextEditor editor)
    {
        var details = new TextBlock { Text = model.Details, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        details.Classes.Add("caption");
        details.Bind(TextBlock.ForegroundProperty, details.GetResourceObservable("FxcTextSecondaryBrush"));
        AutomationProperties.SetAutomationId(details, fields.Id("Details"));

        var frame = new Border { BorderThickness = new Thickness(1), Child = editor };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("FxcBorderDefaultBrush"));

        var wrap = Check(fields, "Wrap", Strings.TextViewer_Wrap, model.Wrap);
        var hex = Check(fields, "Hex", Strings.TextViewer_Hex, model.Hex);
        var status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(16, 0, 0, 0),
        };
        status.Classes.Add("body");
        AutomationProperties.SetAutomationId(status, fields.Id("Status"));
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); // UI-DLG-328

        void ApplyWrap()
        {
            editor.WordWrap = model.Wrap;
            editor.HorizontalScrollBarVisibility = model.Wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        }

        IDisposable? statusBrush = null;
        void Update()
        {
            details.Text = model.Details;
            status.Text = model.Status;
            AutomationProperties.SetName(status, model.Status);
            statusBrush?.Dispose();
            statusBrush = status.Bind(TextBlock.ForegroundProperty, status.GetResourceObservable(model.StatusIsError ? "FxcStatusErrorBrush" : "FxcTextTertiaryBrush"));
        }

        wrap.IsCheckedChanged += (_, _) =>
        {
            model.Wrap = wrap.IsChecked == true;
            ApplyWrap();
        };
        hex.IsCheckedChanged += (_, _) =>
        {
            // UI-DLG-326: the element at the top of the view stays at the top in the other view.
            var element = model.ElementAt(TopOffset(editor));
            model.Hex = hex.IsChecked == true;
            Dispatcher.UIThread.Post(() => ScrollTo(editor, model.DisplayOffsetOf(element)), DispatcherPriority.Loaded);
        };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TextViewerViewModel.Details) or nameof(TextViewerViewModel.Status) or nameof(TextViewerViewModel.StatusIsError))
                Update();
        };
        ApplyWrap();
        Update();

        var options = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var boxes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { wrap, hex } };
        DockPanel.SetDock(boxes, Avalonia.Controls.Dock.Left);
        options.Children.Add(boxes);
        options.Children.Add(status);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(details, 0);
        Grid.SetRow(frame, 1);
        Grid.SetRow(options, 2);
        grid.Children.Add(details);
        grid.Children.Add(frame);
        grid.Children.Add(options);
        return grid;
    }

    private static CheckBox Check(DialogFieldBuilder fields, string field, string label, bool isChecked)
    {
        var box = new CheckBox { Content = label, IsChecked = isChecked };
        AutomationProperties.SetAutomationId(box, fields.Id(field));
        AutomationProperties.SetName(box, label.Replace("_", "", StringComparison.Ordinal));
        return box;
    }

    /// <summary>The document offset at the top left of the view.</summary>
    private static int TopOffset(TextEditor editor)
    {
        var view = editor.TextArea.TextView;
        if (editor.Document.TextLength == 0 || !view.VisualLinesValid)
            return 0;
        return view.GetPositionFloor(new Point(0, view.ScrollOffset.Y)) is { } position ? editor.Document.GetOffset(position.Location) : 0;
    }

    /// <summary>Scrolls the line holding <paramref name="offset"/> to the top.</summary>
    private static void ScrollTo(TextEditor editor, int offset)
    {
        var document = editor.Document;
        if (document.TextLength == 0)
            return;
        editor.UpdateLayout();
        var view = editor.TextArea.TextView;
        var location = document.GetLocation(Math.Clamp(offset, 0, document.TextLength));
        var y = view.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineTop).Y;
        // TextEditor.ScrollToVerticalOffset does not move the template's ScrollViewer (AvaloniaEdit 12.0.0).
        if (editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroller)
            scroller.Offset = new Vector(scroller.Offset.X, y);
    }
}
