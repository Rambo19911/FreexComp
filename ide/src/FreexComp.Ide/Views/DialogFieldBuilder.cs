using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// Building blocks of form-like dialogs (dialogs.md §1): a label column, the field, the inline message
/// <c>Dialog.&lt;Name&gt;.Error.&lt;Field&gt;</c> under it (UI-DLG-006) and the Browse button of a path field (§24.2).
/// </summary>
internal sealed class DialogFieldBuilder(string dialogName)
{
    public const double LabelWidth = 180;

    public string Id(string field) => AutomationIds.DialogField(dialogName, field);

    /// <summary>A label whose mnemonic focuses <paramref name="target"/> (null = a plain caption).</summary>
    public static Control Label(string text, Control? target, string? note = null)
    {
        Control label = target is null
            ? new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
            : new Label { Content = text, Target = target, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        label.Bind(target is null ? TextBlock.ForegroundProperty : Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, label.GetResourceObservable("FxcTextPrimaryBrush"));
        if (note is null)
            return label;
        var noteBlock = new TextBlock { Text = note, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        noteBlock.Bind(TextBlock.ForegroundProperty, noteBlock.GetResourceObservable("FxcTextTertiaryBrush"));
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { label, noteBlock } };
    }

    /// <summary>A two-column row: label (fixed width) and field.</summary>
    public static Grid Row(Control label, Control field)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelWidth},*") };
        grid.Children.Add(label);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        return grid;
    }

    /// <summary>A read-only text with its id (selectable, start-trimmed paths use <paramref name="trimStart"/>).</summary>
    public TextBlock ReadOnly(string field, string? text, bool code = false)
    {
        var block = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (code)
            block.Classes.Add("codeSmall");
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("FxcTextPrimaryBrush"));
        AutomationProperties.SetAutomationId(block, Id(field));
        ToolTip.SetTip(block, text);
        return block;
    }

    /// <summary>
    /// The message line under a field: <paramref name="message"/> is read on every property change of
    /// <paramref name="source"/>; <paramref name="isError"/> picks <c>status.error</c> over <c>status.warning</c>.
    /// </summary>
    public TextBlock Message(string field, INotifyPropertyChanged source, Func<string?> message, Func<bool> isError, Control? invalidField = null)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(LabelWidth, 2, 0, 0) };
        block.Classes.Add("caption");
        AutomationProperties.SetAutomationId(block, Id("Error." + field));
        void Update()
        {
            var text = message();
            block.Text = text;
            block.IsVisible = text is not null;
            block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable(isError() ? "FxcStatusErrorBrush" : "FxcStatusWarningBrush"));
            invalidField?.Classes.Set("invalid", text is not null && isError());
        }

        source.PropertyChanged += (_, _) => Update();
        Update();
        return block;
    }

    /// <summary>A path field with its Browse button <c>&lt;Field&gt;.Browse</c> at the right.</summary>
    public Control WithBrowse(string field, Control editor, Func<Task> browse)
    {
        var button = new Button { Content = "…", Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(button, Id(field + ".Browse"));
        AutomationProperties.SetName(button, Strings.Options_Browse);
        ToolTip.SetTip(button, Strings.Options_Browse);
        button.Click += async (_, _) => await browse();
        var panel = new DockPanel();
        DockPanel.SetDock(button, Avalonia.Controls.Dock.Right);
        panel.Children.Add(button);
        panel.Children.Add(editor);
        return panel;
    }

    /// <summary>A text box with its id, two-way over a getter/setter.</summary>
    public TextBox Text(string field, Func<string> get, Action<string> set, bool code = false)
    {
        var box = new TextBox { Text = get() };
        if (code)
            box.Classes.Add("code");
        AutomationProperties.SetAutomationId(box, Id(field));
        box.TextChanged += (_, _) => set(box.Text ?? "");
        return box;
    }
}
