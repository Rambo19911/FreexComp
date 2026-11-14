using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Input;

/// <summary>
/// Accessibility and focus rules of input fields, registered once as class handlers:
/// a <see cref="Label"/> with a <see cref="Label.Target"/> labels it (<c>LabeledBy</c>), a visible validation message
/// <c>Dialog.&lt;Name&gt;.Error.&lt;Field&gt;</c> is the <c>HelpText</c> of <c>Dialog.&lt;Name&gt;.&lt;Field&gt;</c> (UI-A11Y-022).
/// </summary>
public static class FieldBehaviors
{
    private const string ErrorSegment = ".Error.";
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;
        _registered = true;
        Label.TargetProperty.Changed.AddClassHandler<Label>((label, _) => LinkLabel(label));
        Visual.IsVisibleProperty.Changed.AddClassHandler<TextBlock>((text, _) => UpdateHelpText(text));
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((text, _) => UpdateHelpText(text));
    }

    private static void LinkLabel(Label label)
    {
        if (label.Target is Control target)
            AutomationProperties.SetLabeledBy(target, label);
    }

    private static void UpdateHelpText(TextBlock message)
    {
        if (AutomationIds.Get(message) is not { } id || !id.StartsWith("Dialog.", StringComparison.Ordinal))
            return;
        var at = id.IndexOf(ErrorSegment, StringComparison.Ordinal);
        if (at < 0)
            return;
        var fieldId = id[..at] + "." + id[(at + ErrorSegment.Length)..];
        if (TopLevel.GetTopLevel(message) is not Visual root)
            return;
        var field = root.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => AutomationIds.Get(c) == fieldId);
        field?.SetValue(AutomationProperties.HelpTextProperty, message.IsVisible && !string.IsNullOrEmpty(message.Text) ? message.Text : null);
    }
}
