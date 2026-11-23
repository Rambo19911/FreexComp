using Avalonia.Controls;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Views;

/// <summary>
/// Add / Edit Watch (dialogs.md §20), 440 × auto: the <c>Expression</c> field with its history on Up/Down (UI-DLG-009).
/// Initial focus on the field with its text selected.
/// </summary>
public sealed class WatchWindow : DialogWindow
{
    public WatchWindow(WatchViewModel model)
        : this(model, new DialogFieldBuilder(WatchViewModel.DialogName).Text("Expression", () => model.Expression, v => model.Expression = v, code: true))
    {
    }

    private WatchWindow(WatchViewModel model, TextBox expression)
        : base(model, DialogFieldBuilder.Row(DialogFieldBuilder.Label(Strings.Watch_Expression, expression), expression))
    {
        History.Attach(expression, model.History);
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            expression.Focus(Avalonia.Input.NavigationMethod.Tab);
            expression.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }
}

/// <summary>UI-DLG-009: Up/Down in a text field step through its history (most recent first).</summary>
internal static class History
{
    public static void Attach(TextBox box, IReadOnlyList<string> history)
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
