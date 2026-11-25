using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// The dialog <c>TrustProject</c> (dialogs.md §29, UI-DLG-290…296), 640 × 420, resizable: the introduction, the read-only
/// tree of the projects and their commands (<c>Dialog.TrustProject.Commands</c>: a project row per project, its rows
/// below it, indented and always shown) and the inline message. The default button has the initial focus (UI-DLG-291).
/// </summary>
public sealed class TrustProjectWindow(TrustProjectViewModel model) : DialogWindow(model, Body(model))
{
    /// <summary>Indent of the rows below a project row.</summary>
    private const double ChildIndent = 20;

    /// <summary>Width of the list-name cell of a command row (dialogs.md §29).</summary>
    private const double ListNameWidth = 96;

    protected override bool FocusesDefaultButtonFirst => true;

    private static string Id(string field) => AutomationIds.DialogField(TrustProjectViewModel.DialogName, field);

    private static Control Body(TrustProjectViewModel model)
    {
        var intro = new TextBlock { Text = model.Intro, Classes = { "body" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        intro.Bind(TextBlock.ForegroundProperty, intro.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(intro, Id("Intro"));

        // UI-DLG-006 look: the message row under the tree, hidden while there is nothing to say.
        var error = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable("FxcStatusErrorBrush"));
        AutomationProperties.SetAutomationId(error, Id("Error"));

        var list = new ListBox { SelectionMode = SelectionMode.Multiple };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto); // UI-DLG-294: a long command scrolls, it is never cut
        AutomationProperties.SetAutomationId(list, Id("Commands"));
        AutomationProperties.SetName(list, Strings.Dialog_TrustProject_Commands);
        list.KeyDown += async (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.Control)
                return;
            if (e.Key == Key.A)
            {
                e.Handled = true;
                list.SelectAll();
            }
            else if (e.Key == Key.C)
            {
                e.Handled = true;
                var selected = list.Selection.SelectedIndexes.Order().Select(i => model.Rows[i]).ToList();
                await model.CopyAsync(selected);
            }
        };
        list.GotFocus += (_, _) =>
        {
            if (list.SelectedIndex < 0 && list.ItemCount > 0)
                list.SelectedIndex = 0; // the arrow keys move from the first row
        };

        void Update()
        {
            list.Items.Clear();
            foreach (var row in model.Rows)
                list.Items.Add(CreateRow(row));
            error.Text = model.Error;
            error.IsVisible = model.Error is not null;
        }

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TrustProjectViewModel.Rows) or nameof(TrustProjectViewModel.Error))
                Update();
        };
        Update();

        var body = new DockPanel();
        DockPanel.SetDock(intro, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(error, Avalonia.Controls.Dock.Bottom);
        body.Children.Add(intro);
        body.Children.Add(error);
        body.Children.Add(list);
        return body;
    }

    private static ListBoxItem CreateRow(TrustRow row)
    {
        Control content;
        string id;
        switch (row.Kind)
        {
            case TrustRowKind.Project:
                content = Text(row.Text, "bodyStrong", "FxcTextPrimaryBrush");
                id = Id($"Project.{row.Project}");
                break;
            case TrustRowKind.Changed:
                // UI-DLG-293: the warning glyph and the note, the first row below the project.
                var note = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(ChildIndent, 0, 0, 0) };
                note.Children.Add(Text("⚠ ", "body", "FxcStatusWarningBrush"));
                note.Children.Add(Text(row.Text, "body", "FxcTextPrimaryBrush"));
                content = note;
                id = Id($"Project.{row.Project}.Changed");
                break;
            default:
                var name = Text(row.ListName, "caption", "FxcTextSecondaryBrush");
                name.Width = ListNameWidth;
                var cells = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(ChildIndent, 0, 0, 0) };
                cells.Children.Add(name);
                cells.Children.Add(CommandText(row.Text));
                content = cells;
                id = Id($"Command.{row.Project}.{row.Index}");
                break;
        }

        var item = new ListBoxItem { Content = content };
        AutomationProperties.SetAutomationId(item, id);
        AutomationProperties.SetName(item, row.AutomationName);
        if (row.Kind == TrustRowKind.Command)
            ToolTip.SetTip(item, row.Text); // UI-DLG-294: the full command
        return item;
    }

    private static TextBlock Text(string text, string type, string brush)
    {
        var block = new TextBlock { Text = text, Classes = { type }, VerticalAlignment = VerticalAlignment.Center };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable(brush));
        return block;
    }

    /// <summary>
    /// UI-DLG-294: the command on one line, complete; a tab and a line break are drawn as the glyphs <c>→</c> and <c>↵</c> in
    /// <c>text.disabled</c>, so that a command cannot hide a second line.
    /// </summary>
    private static TextBlock CommandText(string command)
    {
        var block = new TextBlock { Classes = { "code" }, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("FxcTextPrimaryBrush"));
        var start = 0;
        for (var i = 0; i < command.Length; i++)
        {
            var glyph = command[i] switch
            {
                '\t' => "→",
                '\n' => "↵",
                '\r' when i + 1 < command.Length && command[i + 1] == '\n' => "", // one glyph for a CR LF pair
                '\r' => "↵",
                _ => null,
            };
            if (glyph is null)
                continue;
            if (i > start)
                block.Inlines!.Add(new Run(command[start..i]));
            if (glyph.Length > 0)
            {
                var run = new Run(glyph);
                run.Bind(TextElement.ForegroundProperty, block.GetResourceObservable("FxcTextDisabledBrush"));
                block.Inlines!.Add(run);
            }

            start = i + 1;
        }

        if (start < command.Length)
            block.Inlines!.Add(new Run(command[start..]));
        return block;
    }
}
