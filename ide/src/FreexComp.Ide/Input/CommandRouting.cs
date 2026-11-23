using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Commands;

namespace FreexComp.Ide.Input;

/// <summary>
/// Marks the root of a command scope in the view tree (commands.md §1): editor views set <c>Editor</c>, tool windows
/// <c>ToolList</c>/<c>ProjectManager</c> plus their name, the design surface <c>Designer</c>. The element itself, or
/// else its <c>DataContext</c>, when it is an <see cref="ICommandTarget"/>, receives <c>Focus</c> commands.
/// </summary>
public static class CommandRouting
{
    public static readonly AttachedProperty<CommandScope?> ScopeProperty =
        AvaloniaProperty.RegisterAttached<Control, CommandScope?>("Scope", typeof(CommandRouting));

    public static readonly AttachedProperty<string?> ToolWindowProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("ToolWindow", typeof(CommandRouting));

    /// <summary>Keys the marked element handles itself although they are bound to commands (<see cref="IFocusTracker.LocalKeys"/>).</summary>
    public static readonly AttachedProperty<IReadOnlyList<KeyGesture>?> LocalKeysProperty =
        AvaloniaProperty.RegisterAttached<Control, IReadOnlyList<KeyGesture>?>("LocalKeys", typeof(CommandRouting));

    public static void SetLocalKeys(Control element, IReadOnlyList<KeyGesture>? value) => element.SetValue(LocalKeysProperty, value);

    /// <summary>The local keys of the innermost scope element around <paramref name="focused"/>; empty when it has none.</summary>
    public static IReadOnlyList<KeyGesture> LocalKeysOf(Visual? focused)
    {
        for (var v = focused; v is not null; v = v.GetVisualParent())
        {
            if (v is Control control && GetScope(control) is not null)
            {
                return control.GetValue(LocalKeysProperty) ?? [];
            }
        }

        return [];
    }

    public static CommandScope? GetScope(Control element) => element.GetValue(ScopeProperty);

    public static void SetScope(Control element, CommandScope? value) => element.SetValue(ScopeProperty, value);

    public static string? GetToolWindow(Control element) => element.GetValue(ToolWindowProperty);

    public static void SetToolWindow(Control element, string? value) => element.SetValue(ToolWindowProperty, value);

    /// <summary>
    /// The focus state of <paramref name="focused"/> (IDE-ARCH-042): a <see cref="TextBox"/> is a text input in the
    /// <c>Global</c> scope whose target is the TextBox itself (I-153); otherwise the innermost marked ancestor gives
    /// scope, tool window and target. Null when focus is in transient chrome — the menu bar, a menu, the toolbar row, a
    /// status-bar field or any popup — whose commands act on what had focus before (I-153); the caller keeps the previous state.
    /// </summary>
    public static FocusState? StateOf(Visual? focused)
    {
        if (IsTransientChrome(focused))
        {
            return null;
        }

        if (focused is TextBox box)
        {
            return new FocusState(CommandScope.Global, null, TextBoxCommandTarget.For(box), IsTextInput: true);
        }

        for (var v = focused; v is not null; v = v.GetVisualParent())
        {
            if (v is Control control && GetScope(control) is { } scope)
            {
                // A view that is itself a target (the code editor, which owns the selection) wins over its view-model.
                return new FocusState(scope, GetToolWindow(control), control as ICommandTarget ?? control.DataContext as ICommandTarget, IsTextInput: false);
            }
        }

        return FocusState.None;
    }

    private static bool IsTransientChrome(Visual? focused)
    {
        Visual? last = null;
        for (var v = focused; v is not null; v = v.GetVisualParent())
        {
            if (v is MenuBase or MenuItem or Views.ToolBarRow or Views.StatusFieldButton)
            {
                return true;
            }

            last = v;
        }

        return last is PopupRoot;
    }
}
