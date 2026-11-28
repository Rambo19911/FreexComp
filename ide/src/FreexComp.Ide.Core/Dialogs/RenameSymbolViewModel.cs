using System.Globalization;
using FreexComp.Compiler.Syntax;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Resources;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>One row of the conflict list (UI-DLG-384): <c>{file name}({line},{column}): {Name}</c>, tooltip = full path and message.</summary>
public sealed record RenameConflictRow(string Text, string ToolTip);

/// <summary>
/// The Rename Symbol dialog (UI-DLG-380…386; F13-001, I-380), 520 × auto: the symbol line, <c>NewName</c> validated 200 ms
/// after the last change, the live check of each valid name (a plan through <c>plan</c>; one request at a time, a result for
/// another input is dropped) with its summary or conflict list, the overload check box of an overloaded routine. Its result
/// is the chosen name with its plan; Cancel, Escape and × give none. UI thread.
/// </summary>
public sealed class RenameSymbolViewModel : DialogViewModel<RenameDialogResult>
{
    public const string DialogName = "RenameSymbol";

    /// <summary>UI-DLG-382: the pause after the last change before the name is validated and checked.</summary>
    public static readonly TimeSpan CheckDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>UI-DLG-382: the longest name the field takes.</summary>
    public const int MaxNameLength = 255;

    private readonly RenameInfo _info;
    private readonly Func<string, bool, CancellationToken, Task<RenamePlan>> _plan;
    private readonly IClock _clock;
    private readonly ILayoutService? _layout;
    private readonly Func<string, string?>? _validateUnit;
    private readonly string _extension;
    private string _newName;
    private bool _overloads;
    private string? _error;
    private IReadOnlyList<RenameConflictRow> _conflicts = [];
    private string _summary = "";
    private string? _notes;
    private string? _fileNote;
    private bool _isInputEnabled = true;
    private int _version;              // one per input (name, option): a check result for an older one is dropped
    private IDisposable? _timer;
    private CancellationTokenSource? _request;
    private Task _checking = Task.CompletedTask;
    private int _checkingVersion = -1; // the input the running or last check is for
    private RenamePlan? _planned;      // the successful plan of the present input
    private bool _failed;              // the check of the present input failed
    private bool _committing;

    /// <param name="info">The facts of the target (its <c>CanRename</c> is true).</param>
    /// <param name="plan">The plan of a trimmed new name with the overload choice (<see cref="RenamePlanner.PlanAsync"/> on the target's snapshot).</param>
    /// <param name="layout">UI state <c>dialogs.RenameSymbol.overloads</c>; null = not remembered.</param>
    /// <param name="validateUnit">
    /// Unit mode (IDE-REFAC-057): the message of <c>IProjectService.ValidateRename</c> for a new file name (name + the
    /// file's extension), null when it may be used; null = the syntactic check of a symbol name.
    /// </param>
    public RenameSymbolViewModel(RenameInfo info, Func<string, bool, CancellationToken, Task<RenamePlan>> plan, IClock clock, ILayoutService? layout,
        Func<string, string?>? validateUnit = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(clock);
        _info = info;
        _plan = plan;
        _clock = clock;
        _layout = layout;
        _validateUnit = validateUnit;
        _extension = info.DeclarationPaths.IsEmpty ? ".pas" : Path.GetExtension(info.DeclarationPaths[0]);
        _newName = info.CurrentName;
        ShowOverloads = info.OverloadCount > 1 && info.Kind is CompletionItemKind.Method or CompletionItemKind.Routine;
        _overloads = ShowOverloads && layout?.GetDialogValue(DialogName, "overloads") != "false"; // checked by default
        Symbol = SymbolLine(info);
        Rename = new DialogButtonViewModel("Ok", Label(Strings.Dialog_RenameSymbol_Rename), CommitAsync)
        {
            IsDefault = true,
            IsAccent = true,
            IsEnabled = false,
            AccessLabel = Access(Strings.Dialog_RenameSymbol_Rename),
        };
        Buttons = [Rename, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, Cancel) { IsCancel = true }];
    }

    public override string Name => DialogName;

    public override string Title => IsUnitMode ? Strings.Dialog_RenameSymbol_TitleUnit : Strings.Dialog_RenameSymbol_Title;

    /// <summary>IDE-REFAC-057: the name of a unit, program or library (title <c>Rename Unit</c>, the <c>File</c> line).</summary>
    public bool IsUnitMode => _info.Kind == CompletionItemKind.Unit;

    /// <summary>
    /// Unit mode (IDE-REFAC-057): <c>Msg.Rename.UnitFileNote</c> with the new file name while the name is valid; null = hidden.
    /// </summary>
    public string? FileNote
    {
        get => _fileNote;
        private set => SetProperty(ref _fileNote, value);
    }

    /// <summary>Unit mode: the new file name of a name (the name with the unit file's extension).</summary>
    public string FileNameOf(string name) => name + _extension;

    public override double Width => 520;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Rename { get; }

    /// <summary>The name as the target has it.</summary>
    public string CurrentName => _info.CurrentName;

    /// <summary>UI-DLG-381: <c>{kind} {name} — {file}</c>; also its tooltip.</summary>
    public string Symbol { get; }

    /// <summary>The label of <c>NewName</c> with its mnemonic marked by <c>_</c>.</summary>
    public static string NewNameLabel => Access(Strings.Dialog_RenameSymbol_NewName);

    /// <summary>UI-DLG-383: the overload check box is shown (a routine or method with more than one overload).</summary>
    public bool ShowOverloads { get; }

    /// <summary>The check box label with its mnemonic marked by <c>_</c> and the overload count.</summary>
    public string OverloadsLabel => Access(string.Format(CultureInfo.CurrentCulture, Strings.Dialog_RenameSymbol_Overloads, _info.OverloadCount));

    /// <summary>The text of <c>NewName</c> (untrimmed); every change starts the 200 ms again.</summary>
    public string NewName
    {
        get => _newName;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _newName, value))
                InputChanged();
        }
    }

    /// <summary>UI-DLG-383: rename the whole overload group; toggling it starts the check again. Remembered.</summary>
    public bool Overloads
    {
        get => _overloads;
        set
        {
            if (!SetProperty(ref _overloads, value))
                return;
            _layout?.SetDialogValue(DialogName, "overloads", value ? "true" : "false");
            InputChanged();
        }
    }

    /// <summary>The field's error text (<c>Dialog.RenameSymbol.Error.NewName</c>): a validation message or a refusal; null = none.</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
                OnPropertyChanged(nameof(IsInvalid));
        }
    }

    /// <summary>The field is drawn invalid: an error text or a conflict list.</summary>
    public bool IsInvalid => _error is not null || _conflicts.Count > 0;

    /// <summary>UI-DLG-384: the conflicts of the present name (shown with <c>ConflictsLabel</c> when not empty).</summary>
    public IReadOnlyList<RenameConflictRow> Conflicts
    {
        get => _conflicts;
        private set
        {
            if (!SetProperty(ref _conflicts, value))
                return;
            OnPropertyChanged(nameof(HasConflicts));
            OnPropertyChanged(nameof(IsInvalid));
        }
    }

    public bool HasConflicts => _conflicts.Count > 0;

    /// <summary>UI-DLG-384: <c>Checking…</c> while the check runs, then the counts of the plan; empty otherwise.</summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>UI-DLG-384: the notes of a successful plan (unrenamed occurrences); null = hidden.</summary>
    public string? Notes
    {
        get => _notes;
        private set => SetProperty(ref _notes, value);
    }

    /// <summary>UI-DLG-385: false while <c>Rename</c> waits for its check (<c>NewName</c>, <c>Overloads</c> and <c>Rename</c> disabled).</summary>
    public bool IsInputEnabled
    {
        get => _isInputEnabled;
        private set => SetProperty(ref _isInputEnabled, value);
    }

    /// <summary>The running check, or the last one (tests await it so that no work outlives them, I-210).</summary>
    public Task Checking => _checking;

    /// <summary>Raised when the focus goes back to <c>NewName</c> after a failed check that <c>Rename</c> waited for.</summary>
    public event EventHandler? FocusNameRequested;

    private string Trimmed => _newName.Trim();

    /// <summary>UI-DLG-381: kind (resource <c>RenameKind_&lt;Kind&gt;</c>), qualified name, the first declaring file and how many more.</summary>
    internal static string SymbolLine(RenameInfo info)
    {
        var kind = Strings.ResourceManager.GetString("RenameKind_" + info.Kind, CultureInfo.CurrentCulture) ?? info.Kind.ToString();
        var name = info.ContainerName.Length == 0 ? info.CurrentName : info.ContainerName + "." + info.CurrentName;
        if (info.DeclarationPaths.IsEmpty)
            return kind + " " + name;
        var file = Path.GetFileName(info.DeclarationPaths[0]);
        if (info.DeclarationPaths.Length > 1)
            file += string.Create(CultureInfo.CurrentCulture, $" (+{info.DeclarationPaths.Length - 1})");
        return kind + " " + name + " — " + file;
    }

    private static string Access(string label) => label.Replace("_", "__", StringComparison.Ordinal).Replace('&', '_');

    private static string Label(string label) => label.Replace("&", "", StringComparison.Ordinal);

    /// <summary>
    /// UI-DLG-382 without the check: no message and false for an empty or unchanged name; else the message of a reserved word
    /// or of a name that is no identifier (a label's syntax is the service's).
    /// </summary>
    private (bool Valid, string? Message) Validate()
    {
        var name = Trimmed;
        if (name.Length == 0 || string.Equals(name, _info.CurrentName, StringComparison.Ordinal))
            return (false, null);
        if (IsUnitMode)
            return _validateUnit?.Invoke(FileNameOf(name)) is { } refused ? (false, refused) : (true, null); // UI-DLG-382, unit mode
        if (SyntaxFacts.IsReservedWord(name))
            return (false, string.Format(CultureInfo.CurrentCulture, Strings.Msg_Rename_NameReserved, name));
        if (_info.Kind != CompletionItemKind.Label && !SyntaxFacts.IsValidIdentifier(name))
            return (false, Strings.Msg_Rename_NameInvalid);
        return (true, null);
    }

    /// <summary>A new input: what was known is dropped, the running check is cancelled and the 200 ms start again.</summary>
    private void InputChanged()
    {
        _version++;
        _timer?.Dispose();
        _request?.Cancel();
        _planned = null;
        _failed = false;
        Error = null;
        Conflicts = [];
        Summary = "";
        Notes = null;
        FileNote = null;
        Rename.IsEnabled = !_committing && Validate().Valid;
        _timer = _clock.Schedule(CheckDelay, () => StartCheck());
    }

    /// <summary>UI-DLG-382, 384: validates the present input and, for a valid name, starts its check (once per input).</summary>
    private Task StartCheck()
    {
        _timer?.Dispose();
        _timer = null;
        if (IsClosed)
            return Task.CompletedTask;
        var (valid, message) = Validate();
        Error = message;
        FileNote = valid && IsUnitMode ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_Rename_UnitFileNote, FileNameOf(Trimmed)) : null;
        if (!valid)
        {
            Rename.IsEnabled = false;
            return Task.CompletedTask;
        }

        if (_checkingVersion == _version)
            return _checking; // running or ended for this input
        _request?.Cancel();
        _request = new CancellationTokenSource();
        _checkingVersion = _version;
        Summary = Strings.Msg_Rename_Checking;
        _checking = CheckAsync(_version, Trimmed, _overloads, _request.Token);
        return _checking;
    }

    private async Task CheckAsync(int version, string name, bool overloads, CancellationToken ct)
    {
        RenamePlan plan;
        try
        {
            plan = await _plan(name, overloads, ct);
        }
        catch (OperationCanceledException)
        {
            return; // replaced by a newer input, or the dialog closed: nothing is shown
        }
        catch (IOException e)
        {
            if (version == _version && !IsClosed)
                Fail(string.Format(CultureInfo.CurrentCulture, Strings.Reason_Rename_NoCodeModel, e.Message), []);
            return;
        }

        if (version != _version || IsClosed)
            return;
        if (plan.Result.Success && plan.Edit is not null)
        {
            _planned = plan;
            Summary = SummaryText(plan);
            Notes = null; // unrenamed occurrences arrive with the language service's report (C-452, I-385)
            Rename.IsEnabled = !_committing;
            return;
        }

        if (!plan.Result.Conflicts.IsEmpty)
        {
            var rows = plan.Result.Conflicts.Select(c => new RenameConflictRow(
                string.Create(CultureInfo.CurrentCulture, $"{Path.GetFileName(c.Location.Path)}({c.Location.LineSpan.Start.Line + 1},{c.Location.LineSpan.Start.Column + 1}): {c.Name}"),
                c.Location.Path + Environment.NewLine + c.Message)).ToList();
            if (rows.Count >= RenameService.MaxConflicts)
                rows.Add(new RenameConflictRow(Strings.Msg_Rename_ConflictsMore, Strings.Msg_Rename_ConflictsMore));
            Fail(null, rows);
            return;
        }

        Fail(plan.Result.Error ?? "", []);
    }

    private void Fail(string? error, IReadOnlyList<RenameConflictRow> conflicts)
    {
        _failed = true;
        Error = error;
        Conflicts = conflicts;
        Summary = "";
        Notes = null;
        Rename.IsEnabled = false;
    }

    private static string SummaryText(RenamePlan plan) => (plan.EditCount, plan.FileCount) switch
    {
        (1, _) => Strings.Msg_Rename_SummaryOne,
        (var edits, 1) => string.Format(CultureInfo.CurrentCulture, Strings.Msg_Rename_SummaryOneFile, edits),
        var (edits, files) => string.Format(CultureInfo.CurrentCulture, Strings.Msg_Rename_SummaryMany, edits, files),
    };

    /// <summary>
    /// UI-DLG-385: the check of the present input has succeeded → closes with the name, the option and the plan; it is pending
    /// or has not started → it starts at once and the fields wait for it, then the dialog closes or shows the failure.
    /// </summary>
    private async Task CommitAsync()
    {
        if (IsClosed || _committing || !Validate().Valid || _failed)
            return;
        if (_planned is { } ready)
        {
            Close(new RenameDialogResult(Trimmed, _overloads, ready));
            return;
        }

        _committing = true;
        IsInputEnabled = false;
        Rename.IsEnabled = false;
        try
        {
            await StartCheck();
        }
        finally
        {
            _committing = false;
        }

        if (IsClosed)
            return;
        if (_planned is { } plan)
        {
            Close(new RenameDialogResult(Trimmed, _overloads, plan));
            return;
        }

        IsInputEnabled = true;
        Rename.IsEnabled = !_failed && Validate().Valid;
        FocusNameRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cancel, Escape, ×: the running check is cancelled; nothing is returned.</summary>
    private void Cancel()
    {
        _timer?.Dispose();
        _request?.Cancel();
        Close(null);
    }
}
