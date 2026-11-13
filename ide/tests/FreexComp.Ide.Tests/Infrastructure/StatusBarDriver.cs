using Avalonia.Controls;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>The status bar as a test sees it (testing.md §2.1): field texts by field name, null while hidden.</summary>
public sealed class StatusBarDriver(HeadlessHarness harness)
{
    /// <summary><c>StatusBar.State</c>.</summary>
    public string? State => Field("State");

    /// <summary><c>StatusBar.Message</c>; empty when there is no message.</summary>
    public string? Message => Field("Message");

    /// <summary>The text of <c>StatusBar.&lt;name&gt;</c>, or null when the field is not shown.</summary>
    public string? Field(string name)
    {
        var control = harness.TryFind<Control>(AutomationIds.StatusBarField(name));
        if (control is null || !control.IsEffectivelyVisible)
        {
            return null;
        }

        return control switch
        {
            TextBlock text => text.Text ?? "",
            ContentControl { Content: TextBlock text } => text.Text ?? "",
            _ => throw new InvalidOperationException($"StatusBar.{name} is a {control.GetType().Name}, not a text field."),
        };
    }
}
