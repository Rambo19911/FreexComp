using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// Add / Edit Watch (dialogs.md §20, UI-DLG-200), 440 × auto: one expression with history; an empty one (after trim)
/// disables <c>OK</c>. No syntax validation — errors appear in the Watches value cell. The result is the trimmed
/// expression; null = canceled.
/// </summary>
public sealed class WatchViewModel : DialogViewModel<string>
{
    public const string DialogName = "Watch";
    private const string HistoryField = "Expression";

    private readonly ILayoutService? _layout;
    private string _expression;

    /// <param name="layout">The UI state that keeps the field's history; null = no history.</param>
    /// <param name="expression">Add: the selection or the word at the caret; Edit: the watch's expression.</param>
    public WatchViewModel(ILayoutService? layout, string expression, bool edit)
    {
        _layout = layout;
        _expression = expression ?? "";
        IsEdit = edit;
        History = layout?.GetDialogHistory(DialogName, HistoryField) ?? [];
        Ok = new DialogButtonViewModel("Ok", Strings.Button_OK, Commit) { IsDefault = true, IsAccent = true, IsEnabled = _expression.Trim().Length > 0 };
        Buttons = [Ok, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true }];
    }

    public override string Name => DialogName;

    public override string Title => IsEdit ? Strings.Watch_Title_Edit : Strings.Watch_Title_Add;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Ok { get; }

    public bool IsEdit { get; }

    /// <summary>Earlier expressions, most recent first (UI-DLG-009).</summary>
    public IReadOnlyList<string> History { get; }

    public string Expression
    {
        get => _expression;
        set
        {
            if (SetProperty(ref _expression, value ?? ""))
                Ok.IsEnabled = _expression.Trim().Length > 0;
        }
    }

    private void Commit()
    {
        var expression = Expression.Trim();
        if (expression.Length == 0)
            return;
        _layout?.AddDialogHistory(DialogName, HistoryField, expression);
        Close(expression);
    }
}
