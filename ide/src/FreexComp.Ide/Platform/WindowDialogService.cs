using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Platform;

/// <summary>
/// <see cref="IDialogService"/> over real windows (IDE-ARCH-054, UI-DLG-008): the window of a dialog view-model is
/// <c>FreexComp.Ide.Views.&lt;X&gt;Window</c> for <c>&lt;X&gt;ViewModel</c> (or, for a view-model in an
/// <c>….ViewModels</c> namespace of another assembly, <c>….Views.&lt;X&gt;Window</c> there), constructed with the
/// view-model and shown modally over the main window — or, while a dialog is open, over the innermost open dialog (a
/// message box over the Menu Designer, D-112). When it closes, its owner is active again and the focus returns to the
/// element of the owner that had it (UI-A11Y-008).
/// </summary>
public sealed class WindowDialogService(Func<Window?> owner, IClipboardService clipboard) : IDialogService
{
    /// <summary>The dialog windows shown by this service and not closed yet, innermost last.</summary>
    private readonly List<Window> _open = [];

    public async Task<string> ShowMessageAsync(MessageRequest request)
    {
        var model = new MessageBoxViewModel(request, clipboard);
        return await ShowAsync(model) ?? model.CancelButton!.Id;
    }

    public async Task<TResult?> ShowAsync<TResult>(DialogViewModel<TResult> viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var main = owner();
        if (main is null || !main.IsVisible)
        {
            // No window to be modal to (start-up or shut-down): the non-destructive answer.
            await viewModel.PressCancelAsync();
            return viewModel.Result;
        }

        var parent = _open.LastOrDefault(w => w.IsVisible) ?? main;
        var window = CreateWindow(viewModel);
        window.FitToWorkingArea(parent);
        var previous = parent.FocusManager?.GetFocusedElement() as InputElement;
        _open.Add(window);
        try
        {
            await window.ShowDialog(parent);
        }
        finally
        {
            _open.Remove(window);
        }

        if (!ReferenceEquals(parent, main) && parent.IsVisible)
            parent.Activate(); // the owner dialog, not the main window, is active again
        if (previous is Visual visual && visual.IsAttachedToVisualTree() && previous.IsEffectivelyVisible)
            previous.Focus(NavigationMethod.Tab);
        return viewModel.Result;
    }

    public static Type? WindowTypeFor(Type viewModelType)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        const string suffix = "ViewModel";
        if (!viewModelType.Name.EndsWith(suffix, StringComparison.Ordinal))
            return null;
        var name = viewModelType.Name[..^suffix.Length] + "Window";
        var type = typeof(DialogWindow).Assembly.GetType($"{typeof(DialogWindow).Namespace}.{name}");
        if (type is null && viewModelType.Namespace is { } ns && ns.EndsWith(".ViewModels", StringComparison.Ordinal))
            type = viewModelType.Assembly.GetType($"{ns[..^".ViewModels".Length]}.Views.{name}");
        return type is not null && typeof(DialogWindow).IsAssignableFrom(type) && type.GetConstructor([viewModelType]) is not null ? type : null;
    }

    private static DialogWindow CreateWindow(DialogViewModel viewModel) =>
        WindowTypeFor(viewModel.GetType()) is { } type
            ? (DialogWindow)Activator.CreateInstance(type, viewModel)!
            : throw new InvalidOperationException($"No dialog window for {viewModel.GetType().FullName}.");
}
