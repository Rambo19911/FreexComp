using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Designer;

/// <summary>The <c>objectInspector.*</c> commands (object-inspector.md §1–§4, §12) over the Object Inspector view-model.</summary>
public static class ObjectInspectorCommands
{
    public static void Register(ICommandService commands, ObjectInspectorViewModel inspector, IClipboardService? clipboard)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(inspector);
        inspector.AttachCommands(commands, clipboard); // D-109: context menu, Ctrl+C / Ctrl+V on a row
        bool Shown() => !inspector.IsEmpty;
        commands.RegisterHandler("objectInspector.togglePage", new Handler(() => true, () => inspector.TogglePage()));
        commands.RegisterHandler("objectInspector.showProperties", new Handler(() => true,
            () => inspector.Page = InspectorPage.Properties, () => inspector.Page == InspectorPage.Properties));
        commands.RegisterHandler("objectInspector.showEvents", new Handler(() => true,
            () => inspector.Page = InspectorPage.Events, () => inspector.Page == InspectorPage.Events));
        commands.RegisterHandler("objectInspector.resetToDefault", new Handler(() => inspector.Page == InspectorPage.Properties && inspector.CanResetToDefault,
            inspector.ResetToDefault));
        commands.RegisterHandler("objectInspector.toggleCategorized", new Handler(() => true, inspector.ToggleArrangement, () => !inspector.IsArrangedByName));
        commands.RegisterHandler("objectInspector.expandAll", new Handler(Shown, () => inspector.ExpandAll(true)));
        commands.RegisterHandler("objectInspector.collapseAll", new Handler(Shown, () => inspector.ExpandAll(false)));
        commands.RegisterHandler("objectInspector.copyPropertyName", new Handler(() => inspector.CurrentPropertyName is not null && clipboard is not null,
            () => _ = clipboard!.SetTextAsync(inspector.CurrentPropertyName!)));
        commands.RegisterHandler("objectInspector.toggleDescription", new Handler(() => true, inspector.ToggleDescription, () => inspector.ShowDescription));
        inspector.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ObjectInspectorViewModel.Page) or nameof(ObjectInspectorViewModel.IsEmpty)
                or nameof(ObjectInspectorViewModel.IsArrangedByName) or nameof(ObjectInspectorViewModel.ShowDescription)
                or nameof(ObjectInspectorViewModel.CurrentPath) or nameof(ObjectInspectorViewModel.CurrentEvent))
            {
                commands.InvalidateCanExecute();
            }
        };
    }

    private sealed class Handler(Func<bool> canExecute, Action execute, Func<bool>? isChecked = null) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument)
        {
            execute();
            return Task.CompletedTask;
        }

        public bool IsChecked(string? argument) => isChecked?.Invoke() ?? false;
    }
}
