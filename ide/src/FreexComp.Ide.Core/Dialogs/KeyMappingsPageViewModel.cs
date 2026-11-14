using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>One row of the Key Mappings grid: a catalog command and its pending gestures.</summary>
public sealed class KeyMappingRow : ObservableObject
{
    private string _shortcutText = "";
    private bool _isUserChanged;

    internal KeyMappingRow(CommandDefinition command, string text, bool isLaterPhase)
    {
        Command = command;
        Text = text;
        IsLaterPhase = isLaterPhase;
        ScopeText = string.Join(", ", command.Scopes.Select(s =>
            Strings.ResourceManager.GetString("Options_Keys_Scope_" + s, CultureInfo.CurrentCulture) ?? s.ToString()));
    }

    public CommandDefinition Command { get; }

    public string CommandId => Command.Id;

    /// <summary><c>&lt;Category&gt;: &lt;Title&gt;</c> (UI-CMD-003).</summary>
    public string Text { get; }

    public string ScopeText { get; }

    /// <summary>A command of a later phase (<c>text.disabled</c>, still assignable).</summary>
    public bool IsLaterPhase { get; }

    /// <summary>All pending gestures joined with <c>, </c>.</summary>
    public string ShortcutText
    {
        get => _shortcutText;
        internal set => SetProperty(ref _shortcutText, value);
    }

    /// <summary>The pending gestures differ from the defaults (<c>type.bodyStrong</c>).</summary>
    public bool IsUserChanged
    {
        get => _isUserChanged;
        internal set => SetProperty(ref _isUserChanged, value);
    }
}

/// <summary>A gesture chip of the selected command; reserved gestures (UI-A11Y-015) cannot be removed.</summary>
public sealed record KeyGestureChip(KeyGesture Gesture, bool CanRemove)
{
    public string Text => Gesture.ToString();
}

/// <summary>
/// The Key Mappings page (dialogs.md §9.5): a working copy of every listed command's gestures. Assign, remove and the
/// resets change only the copy; <see cref="OptionsViewModel"/>'s OK/Apply write the changed commands through
/// <see cref="IKeymapService"/> (UI-DLG-093).
/// </summary>
public sealed class KeyMappingsPageViewModel : ObservableObject
{
    private readonly IKeymapService _keymap;
    private readonly IDialogService _dialogs;
    private readonly Keymap _defaults = DefaultKeymap.Create();
    private readonly Dictionary<string, List<KeyGesture>> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KeyMappingRow> _rows = new(StringComparer.Ordinal);
    private string _filter = "";
    private bool _byShortcut;
    private KeyMappingRow? _selectedRow;
    private KeyGesture? _captured;
    private string? _captureError;
    private string? _conflictText;
    private bool _resetAll;

    internal KeyMappingsPageViewModel(IKeymapService keymap, IDialogService dialogs, int implementedPhase)
    {
        _keymap = keymap;
        _dialogs = dialogs;
        var categories = CommandCatalog.All.Select(c => c.Category).Distinct().ToList();
        Rows =
        [
            .. CommandCatalog.All.Where(c => !c.TakesArgument)
                .Select(c => new KeyMappingRow(c, TitleOf(c), c.Phase > implementedPhase))
                .OrderBy(r => categories.IndexOf(r.Command.Category))
                .ThenBy(r => r.Text, StringComparer.CurrentCultureIgnoreCase),
        ];
        foreach (var row in Rows)
        {
            _rows[row.CommandId] = row;
            _pending[row.CommandId] = [.. keymap.GetGestures(row.CommandId)];
            UpdateRow(row);
        }

        ApplyFilter();
    }

    /// <summary>Raised when the working copy changes (the dialog updates OK/Apply).</summary>
    public event EventHandler? PendingChanged;

    /// <summary>Every listed command in natural order.</summary>
    public IReadOnlyList<KeyMappingRow> Rows { get; }

    /// <summary>The rows the filter lets through.</summary>
    public ObservableCollection<KeyMappingRow> VisibleRows { get; } = [];

    /// <summary>Case-insensitive substring of <c>&lt;Category&gt;: &lt;Title&gt;</c> or the id; with <see cref="ByShortcut"/>, a gesture.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _filter, value))
                ApplyFilter();
        }
    }

    /// <summary>The filter box captures a gesture and the grid lists the commands bound to it.</summary>
    public bool ByShortcut
    {
        get => _byShortcut;
        set
        {
            if (!SetProperty(ref _byShortcut, value))
                return;
            _filter = "";
            OnPropertyChanged(nameof(Filter));
            ApplyFilter();
        }
    }

    public KeyMappingRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!SetProperty(ref _selectedRow, value))
                return;
            UpdateChips();
            UpdateCapture();
        }
    }

    /// <summary><c>Shortcuts for "&lt;title&gt;":</c>, or null without a selection.</summary>
    public string? CurrentLabel => SelectedRow is { } row ? string.Format(CultureInfo.CurrentCulture, Strings.Options_Keys_CurrentFor, row.Text) : null;

    public ObservableCollection<KeyGestureChip> Current { get; } = [];

    /// <summary>The gesture in the capture box, or null.</summary>
    public KeyGesture? Captured => _captured;

    public string CapturedText => _captured?.ToString() ?? "";

    /// <summary><c>Msg.Options.KeyReserved</c> / <c>KeyNeedsModifier</c> for the captured gesture, or null.</summary>
    public string? CaptureError
    {
        get => _captureError;
        private set => SetProperty(ref _captureError, value);
    }

    /// <summary><c>Msg.Options.KeyConflict</c> while the captured gesture is bound in an overlapping scope, or null.</summary>
    public string? ConflictText
    {
        get => _conflictText;
        private set => SetProperty(ref _conflictText, value);
    }

    public bool CanAssign => SelectedRow is not null && _captured is { } g && CaptureError is null && !_pending[SelectedRow.CommandId].Contains(g);

    public bool HasChanges => Rows.Any(r => !SameSet(_pending[r.CommandId], _keymap.GetGestures(r.CommandId)));

    /// <summary>Pending gestures of a command.</summary>
    public IReadOnlyList<KeyGesture> PendingGestures(string commandId) => _pending[commandId];

    /// <summary>The capture box received a gesture (null = cleared with Backspace).</summary>
    public void Capture(KeyGesture? gesture)
    {
        _captured = gesture;
        UpdateCapture();
    }

    /// <summary><c>Assign</c>: adds the captured gesture and removes it from conflicting commands.</summary>
    public void Assign()
    {
        if (!CanAssign || SelectedRow is not { } row || _captured is not { } gesture)
            return;
        foreach (var other in Conflicts(row.Command, gesture))
        {
            _pending[other.Id].Remove(gesture);
        }

        _pending[row.CommandId].Add(gesture);
        _captured = null;
        Changed();
    }

    /// <summary>The chip's <c>×</c>.</summary>
    public void Remove(KeyGesture gesture)
    {
        if (SelectedRow is not { } row || KeymapService.IsReserved(gesture))
            return;
        if (_pending[row.CommandId].Remove(gesture))
            Changed();
    }

    /// <summary><c>Reset Command</c>: the defaults again, taken back from other commands.</summary>
    public void ResetCommand()
    {
        if (SelectedRow is not { } row)
            return;
        var defaults = _defaults.GetGestures(row.CommandId);
        foreach (var gesture in defaults)
        {
            foreach (var other in Conflicts(row.Command, gesture))
            {
                _pending[other.Id].Remove(gesture);
            }
        }

        _pending[row.CommandId] = [.. defaults];
        Changed();
    }

    /// <summary><c>Reset All…</c>: asks <c>Msg.Options.ResetKeysConfirm</c>, then every command gets its defaults (pending).</summary>
    public async Task ResetAllAsync()
    {
        if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Options.ResetKeysConfirm", [])) != "Reset")
            return;
        foreach (var row in Rows)
        {
            _pending[row.CommandId] = [.. _defaults.GetGestures(row.CommandId)];
        }

        _resetAll = true;
        Changed();
    }

    /// <summary>Labels of the page's fields, for the dialog filter (UI-DLG-091).</summary>
    internal static IReadOnlyList<string> FieldLabels =>
    [
        Strings.Options_Keys_Filter, Strings.Options_Keys_ByShortcut, Strings.Options_Keys_NewShortcut, Strings.Options_Keys_Assign,
        Strings.Options_Keys_ResetCommand, Strings.Options_Keys_ResetAll,
    ];

    /// <summary>Writes the changed commands through the keymap service (OK/Apply).</summary>
    internal void Apply()
    {
        if (_resetAll)
        {
            _keymap.ResetAll();
            _resetAll = false;
        }

        foreach (var row in Rows.Where(r => !SameSet(_pending[r.CommandId], _keymap.GetGestures(r.CommandId))))
        {
            _keymap.SetGestures(row.CommandId, _pending[row.CommandId]);
        }
    }

    private static string TitleOf(CommandDefinition command)
    {
        var category = Strings.ResourceManager.GetString("CmdCategory_" + command.Category, CultureInfo.CurrentCulture) ?? command.Category;
        var title = Strings.ResourceManager.GetString(command.TitleKey, CultureInfo.CurrentCulture) ?? command.Id;
        return $"{category}: {title}";
    }

    private static bool SameSet(IReadOnlyList<KeyGesture> a, IReadOnlyList<KeyGesture> b) => a.Count == b.Count && a.All(b.Contains);

    /// <summary>Same scope, or one of them is Global (Focus counts as Global: it applies everywhere).</summary>
    private static bool Overlap(CommandDefinition a, CommandDefinition b)
    {
        static bool IsGlobal(CommandScope s) => s is CommandScope.Global or CommandScope.Focus;
        return a.Scopes.Any(x => b.Scopes.Any(y => x == y || IsGlobal(x) || IsGlobal(y)));
    }

    /// <summary>Other commands whose pending gestures contain <paramref name="gesture"/> in an overlapping scope.</summary>
    private List<CommandDefinition> Conflicts(CommandDefinition command, KeyGesture gesture) =>
        [.. Rows.Where(r => r.CommandId != command.Id && _pending[r.CommandId].Contains(gesture) && Overlap(command, r.Command)).Select(r => r.Command)];

    /// <summary>Printable keys, Enter and Space without Ctrl/Alt (dialogs.md §9.5).</summary>
    private static bool PreventsTyping(KeyGesture gesture) =>
        (gesture.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0
        && (gesture.Key.Length == 1 || gesture.Key is "Space" or "Enter" or "Multiply" or "Add" or "Subtract");

    /// <summary>
    /// Scopes where typing happens: Editor and Focus (dialogs.md §9.5), and Global, whose bindings also apply while an
    /// editor has the focus (UI-KEY-004).
    /// </summary>
    private static bool IsTypingScope(CommandDefinition command) =>
        command.Scopes.Any(s => s is CommandScope.Editor or CommandScope.Focus or CommandScope.Global);

    private void Changed()
    {
        foreach (var row in Rows)
        {
            UpdateRow(row);
        }

        ApplyFilter();
        UpdateChips();
        UpdateCapture();
        OnPropertyChanged(nameof(HasChanges));
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateRow(KeyMappingRow row)
    {
        var pending = _pending[row.CommandId];
        row.ShortcutText = string.Join(", ", pending);
        row.IsUserChanged = !SameSet(pending, _defaults.GetGestures(row.CommandId));
    }

    private void UpdateChips()
    {
        Current.Clear();
        if (SelectedRow is { } row)
        {
            foreach (var gesture in _pending[row.CommandId])
            {
                Current.Add(new KeyGestureChip(gesture, !KeymapService.IsReserved(gesture)));
            }
        }

        OnPropertyChanged(nameof(CurrentLabel));
    }

    private void UpdateCapture()
    {
        string? error = null;
        string? conflict = null;
        if (_captured is { } gesture)
        {
            if (KeymapService.IsReserved(gesture))
                error = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_KeyReserved, gesture);
            else if (SelectedRow is { } row && PreventsTyping(gesture) && IsTypingScope(row.Command))
                error = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_KeyNeedsModifier, gesture);
            if (error is null && SelectedRow is { } selected && Conflicts(selected.Command, gesture) is { Count: > 0 } others)
                conflict = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_KeyConflict, gesture, string.Join(", ", others.Select(o => _rows[o.Id].Text)));
        }

        CaptureError = error;
        ConflictText = conflict;
        OnPropertyChanged(nameof(Captured));
        OnPropertyChanged(nameof(CapturedText));
        OnPropertyChanged(nameof(CanAssign));
    }

    private void ApplyFilter()
    {
        var text = _filter.Trim();
        Func<KeyMappingRow, bool> match;
        if (_byShortcut)
        {
            match = KeyGesture.TryParse(text, out var gesture) ? r => _pending[r.CommandId].Contains(gesture) : _ => text.Length == 0;
        }
        else
        {
            match = r => text.Length == 0 || r.Text.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                                          || r.CommandId.Contains(text, StringComparison.OrdinalIgnoreCase);
        }

        var visible = Rows.Where(match).ToList();
        if (visible.SequenceEqual(VisibleRows))
            return;
        VisibleRows.Clear();
        foreach (var row in visible)
        {
            VisibleRows.Add(row);
        }
    }
}
