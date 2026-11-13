using CommunityToolkit.Mvvm.ComponentModel;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// One button of a dialog's button strip (UI-DLG-003). <see cref="IsDefault"/> = Enter, <see cref="IsCancel"/> =
/// Escape and the title-bar close (UI-DLG-004); <see cref="IsAccent"/> = accent-filled; <see cref="IsAuxiliary"/>
/// = left-aligned, outside the commit/cancel set. <see cref="AccessLabel"/> carries the mnemonic as <c>_x</c>.
/// </summary>
public sealed class DialogButtonViewModel : ObservableObject
{
    private readonly Func<Task> _execute;
    private bool _isEnabled = true;

    public DialogButtonViewModel(string id, string label, Func<Task> execute)
    {
        Id = id;
        Label = label;
        AccessLabel = label.Replace("_", "__", StringComparison.Ordinal);
        _execute = execute;
    }

    public DialogButtonViewModel(string id, string label, Action execute)
        : this(id, label, () =>
        {
            execute();
            return Task.CompletedTask;
        })
    {
    }

    public string Id { get; }

    public string Label { get; }

    /// <summary>The label with its mnemonic marked by <c>_</c> (no mnemonic: the label with <c>_</c> doubled).</summary>
    public string AccessLabel { get; set; }

    public bool IsDefault { get; init; }

    public bool IsCancel { get; init; }

    public bool IsAccent { get; init; }

    public bool IsAuxiliary { get; init; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>Runs the button's action when it is enabled.</summary>
    public Task PressAsync() => IsEnabled ? _execute() : Task.CompletedTask;
}

/// <summary>
/// Base of every dialog view-model (UI-DLG-008, dialogs.md §1). <see cref="Name"/> is the <c>Dialog.&lt;Name&gt;</c>
/// part of the AutomationIds. The window closes when <see cref="CloseRequested"/> is raised.
/// </summary>
public abstract class DialogViewModel : ObservableObject
{
    public abstract string Name { get; }

    public abstract string Title { get; }

    /// <summary>The button strip, left to right within each group (UI-DLG-003).</summary>
    public abstract IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    /// <summary>Client width in DIP (UI-DLG-005).</summary>
    public virtual double Width => 440;

    /// <summary>Client height in DIP; null = sized to content.</summary>
    public virtual double? Height => null;

    public virtual bool CanResize => false;

    public bool IsClosed { get; private set; }

    public event EventHandler? CloseRequested;

    public DialogButtonViewModel? DefaultButton => Buttons.FirstOrDefault(b => b.IsDefault);

    public DialogButtonViewModel? CancelButton => Buttons.FirstOrDefault(b => b.IsCancel);

    /// <summary>Enter (UI-DLG-004): the default button, when enabled.</summary>
    public Task PressDefaultAsync() => DefaultButton?.PressAsync() ?? Task.CompletedTask;

    /// <summary>Escape and the title-bar close: the Cancel button, or closing with no result when there is none.</summary>
    public Task PressCancelAsync()
    {
        if (CancelButton is { } cancel)
            return cancel.PressAsync();
        CloseWithoutResult();
        return Task.CompletedTask;
    }

    /// <summary>Closes with the default result (no Cancel button).</summary>
    protected abstract void CloseWithoutResult();

    protected void RequestClose()
    {
        if (IsClosed)
            return;
        IsClosed = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A dialog that produces a <typeparamref name="TResult"/> (UI-DLG-008).</summary>
public abstract class DialogViewModel<TResult> : DialogViewModel
{
    public TResult? Result { get; private set; }

    protected void Close(TResult? result)
    {
        if (IsClosed)
            return;
        Result = result;
        RequestClose();
    }

    protected override void CloseWithoutResult() => Close(default);
}
