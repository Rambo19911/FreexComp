using System.Globalization;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// Evaluate / Modify (dialogs.md §21, UI-DLG-210), 560 × 360, resizable: evaluates an expression in the selected frame
/// through <see cref="IDebugService"/> (<c>EvaluateContext.Console</c>) and assigns a new value to a modifiable result.
/// Opened with a non-empty expression it evaluates at once (<see cref="Evaluation"/>).
/// </summary>
public sealed class EvaluateViewModel : DialogViewModel<bool>
{
    public const string DialogName = "Evaluate";
    private const string HistoryField = "Expression";

    private readonly IDebugService _debug;
    private readonly ILayoutService? _layout;
    private string _expression;
    private string _result = "";
    private bool _resultIsError;
    private string _type = "";
    private string _newValue = "";
    private string? _modifyError;
    private VariableInfo? _value;

    public EvaluateViewModel(IDebugService debug, ILayoutService? layout, string expression)
    {
        ArgumentNullException.ThrowIfNull(debug);
        _debug = debug;
        _layout = layout;
        _expression = expression ?? "";
        History = layout?.GetDialogHistory(DialogName, HistoryField) ?? [];
        AddWatch = new DialogButtonViewModel("AddWatch", Strings.Evaluate_AddWatch.Replace("_", "", StringComparison.Ordinal), () => { _debug.Watches.Add(Expression); })
        {
            IsAuxiliary = true,
            AccessLabel = Strings.Evaluate_AddWatch,
        };
        Buttons = [AddWatch, new DialogButtonViewModel("Close", Strings.Button_Close, () => Close(true)) { IsCancel = true }];
        Refresh();
        if (!_debug.IsPaused)
            (_result, _resultIsError) = (Strings.Msg_Debug_NotPaused, false);
        else if (CanEvaluate)
            Evaluation = EvaluateAsync();
    }

    public override string Name => DialogName;

    public override string Title => Strings.Evaluate_Title;

    public override double Width => 560;

    public override double? Height => 360;

    public override bool CanResize => true;

    public override (double Width, double Height)? StoredSize
    {
        get => StoredDialogSize.Read(_layout, DialogName);
        set => StoredDialogSize.Write(_layout, DialogName, value);
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel AddWatch { get; }

    public IReadOnlyList<string> History { get; }

    /// <summary>The last evaluation or assignment started (completed when none).</summary>
    public Task Evaluation { get; private set; } = Task.CompletedTask;

    public string Expression
    {
        get => _expression;
        set
        {
            if (SetProperty(ref _expression, value ?? ""))
                Refresh();
        }
    }

    /// <summary>The value text, the evaluator's message (<see cref="ResultIsError"/>) or <c>Msg.Debug.NotPaused</c>.</summary>
    public string ResultText
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    public bool ResultIsError
    {
        get => _resultIsError;
        private set => SetProperty(ref _resultIsError, value);
    }

    /// <summary>The type name of the last result; empty until evaluated.</summary>
    public string Type
    {
        get => _type;
        private set => SetProperty(ref _type, value);
    }

    public string NewValue
    {
        get => _newValue;
        set
        {
            if (SetProperty(ref _newValue, value ?? ""))
                OnPropertyChanged(nameof(CanModify));
        }
    }

    /// <summary><c>Msg.Debug.ModifyFailed</c> under <c>NewValue</c>; null when none.</summary>
    public string? ModifyError
    {
        get => _modifyError;
        private set => SetProperty(ref _modifyError, value);
    }

    /// <summary>The expression is not empty and the session is paused.</summary>
    public bool CanEvaluate => Expression.Trim().Length > 0 && _debug.IsPaused;

    /// <summary>The last evaluation succeeded and its value is a modifiable location.</summary>
    public bool CanEditNewValue => _value is { CanModify: true, EvaluateName: not null } && _debug.IsPaused;

    public bool CanModify => CanEditNewValue && NewValue.Trim().Length > 0;

    /// <summary>The <c>Evaluate</c> button (default while focus is in <c>Expression</c>).</summary>
    public Task EvaluateAsync() => Evaluation = RunEvaluateAsync();

    /// <summary>The <c>Modify</c> button (default while focus is in <c>NewValue</c>): assigns, then re-evaluates.</summary>
    public Task ModifyAsync() => Evaluation = RunModifyAsync();

    private async Task RunEvaluateAsync()
    {
        if (!CanEvaluate)
            return;
        var expression = Expression.Trim();
        _layout?.AddDialogHistory(DialogName, HistoryField, expression);
        try
        {
            var value = (await _debug.EvaluateAsync(expression, EvaluateContext.Console)).Value;
            SetValue(value, value.Value, isError: false);
        }
        catch (DebuggerException e)
        {
            // Msg.Debug.EvalFailed: the evaluator's message as it is.
            SetValue(null, string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_EvalFailed, e.Message), isError: true);
        }
        catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
        {
            // The session is gone (disposed under the open dialog): as without a paused program.
            SetValue(null, string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_EvalFailed, Strings.Msg_Debug_NotPaused), isError: true);
        }
    }

    private async Task RunModifyAsync()
    {
        if (!CanModify || _value?.EvaluateName is not { } name)
            return;
        try
        {
            await _debug.SetValueAsync(name, NewValue.Trim());
            ModifyError = null;
        }
        catch (DebuggerException e)
        {
            ModifyError = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_ModifyFailed, e.Message);
            return;
        }
        catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
        {
            // The session is gone (disposed under the open dialog): as without a paused program; the result stays.
            ModifyError = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_ModifyFailed, Strings.Msg_Debug_NotPaused);
            return;
        }

        await RunEvaluateAsync();
    }

    private void SetValue(VariableInfo? value, string text, bool isError)
    {
        _value = value;
        ResultText = text;
        ResultIsError = isError;
        Type = value?.TypeName ?? "";
        OnPropertyChanged(nameof(CanEditNewValue));
        OnPropertyChanged(nameof(CanModify));
    }

    private void Refresh()
    {
        AddWatch.IsEnabled = Expression.Trim().Length > 0;
        OnPropertyChanged(nameof(CanEvaluate));
    }
}
