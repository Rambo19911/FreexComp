using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Input;
using FreexComp.Ide.Views;
using CoreGesture = FreexComp.Ide.Core.Commands.KeyGesture;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>
/// Drives one open dialog window (IDE-TEST-016). Field ids are relative to the dialog (<c>Name</c> →
/// <c>Dialog.&lt;Name&gt;.Name</c>); a full id starting with <c>Dialog.</c> is used as given. Keys and text go to the
/// dialog window.
/// </summary>
public sealed class DialogDriver(DialogWindow window, string dialogName)
{
    public DialogWindow Window { get; } = window;

    public string Name { get; } = dialogName;

    /// <summary>Nearest ancestor-or-self of the dialog's focused element that has an AutomationId.</summary>
    public string? FocusedAutomationId
    {
        get
        {
            for (var v = Window.FocusManager?.GetFocusedElement() as Visual; v is not null; v = v.GetVisualParent())
            {
                if (v is StyledElement s && AutomationIds.Get(s) is { Length: > 0 } id)
                {
                    return id;
                }
            }

            return null;
        }
    }

    public string FullId(string fieldId) =>
        fieldId.StartsWith("Dialog.", StringComparison.Ordinal) ? fieldId : AutomationIds.DialogField(Name, fieldId);

    public T Find<T>(string fieldId) where T : Control =>
        TryFind<T>(fieldId) ?? throw new InvalidOperationException(
            $"No {typeof(T).Name} '{FullId(fieldId)}' in dialog {Name}. Ids: {string.Join(", ", All().Select(c => AutomationIds.Get(c)).Distinct())}");

    public T? TryFind<T>(string fieldId) where T : Control =>
        All().OfType<T>().FirstOrDefault(c => AutomationIds.Get(c) == FullId(fieldId));

    public IReadOnlyList<Control> FindAll(string automationIdPrefix) =>
        [.. All().Where(c => AutomationIds.Get(c)!.StartsWith(automationIdPrefix, StringComparison.Ordinal))];

    /// <summary>Focuses the field and types <paramref name="text"/> at its end.</summary>
    public void Type(string fieldId, string text)
    {
        var field = Find<Control>(fieldId);
        field.Focus(NavigationMethod.Tab);
        if (field is TextBox box)
        {
            box.CaretIndex = box.Text?.Length ?? 0;
            box.SelectionStart = box.SelectionEnd = box.CaretIndex;
        }

        Window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>One gesture per call, as <see cref="HeadlessHarness.Press"/>.</summary>
    public void Press(string gesture)
    {
        var parsed = CoreGesture.Parse(gesture);
        var (key, modifiers) = KeyMapping.ToAvalonia(parsed);
        var raw = (modifiers.HasFlag(KeyModifiers.Control) ? RawInputModifiers.Control : RawInputModifiers.None)
                  | (modifiers.HasFlag(KeyModifiers.Shift) ? RawInputModifiers.Shift : RawInputModifiers.None)
                  | (modifiers.HasFlag(KeyModifiers.Alt) ? RawInputModifiers.Alt : RawInputModifiers.None);
        var symbol = parsed.Key is [var c] && char.IsLetterOrDigit(c)
            ? (modifiers.HasFlag(KeyModifiers.Shift) ? c.ToString().ToUpperInvariant() : c.ToString().ToLowerInvariant())
            : null;
        Window.KeyPress(key, raw, PhysicalKey.None, symbol);
        Dispatcher.UIThread.RunJobs();
        if (Window.IsVisible)
        {
            Window.KeyRelease(key, raw, PhysicalKey.None, symbol); // the key may have closed the dialog
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Clicks a button or other element: <c>Ok</c>, or for the message box the button id (<c>Save</c>).</summary>
    public void Click(string id)
    {
        var control = TryFind<Control>(id) ?? Find<Control>("Button." + id);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException($"'{id}' is not in dialog {Name}.");
        Window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        if (Window.IsVisible)
        {
            Window.MouseUp(point, MouseButton.Left);
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The validation message under a field (UI-DLG-006), or null.</summary>
    public string? ErrorText(string fieldId) =>
        TryFind<TextBlock>(AutomationIds.DialogField(Name, "Error." + fieldId)) is { IsEffectivelyVisible: true } error ? error.Text : null;

    private IEnumerable<Control> All() =>
        new Visual[] { Window }.Concat(Window.GetVisualDescendants()).OfType<Control>().Where(c => !string.IsNullOrEmpty(AutomationIds.Get(c)));
}
