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
/// view-model and shown modally over the main window. Focus returns to the element that had it (UI-A11Y-008).
/// </summary>
public sealed class WindowDialogService(Func<Window?> owner, IClipboardService clipboard) : IDialogService
{
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

        var window = CreateWindow(viewModel);
        var previous = main.FocusManager?.GetFocusedElement() as InputElement;
        await window.ShowDialog(main);
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
