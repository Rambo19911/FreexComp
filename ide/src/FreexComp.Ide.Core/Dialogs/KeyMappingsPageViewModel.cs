using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;

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

    /// <summary>All pending gestures as one text (<see cref="KeyGesture.Join"/>: <c>, </c>, or <c>; </c> when a chord is among them).</summary>
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

/// <summary>Sort column of the Key Mappings grid (dialogs.md §9.5: Command and Scope are sortable).</summary>
public enum KeyMappingsSort
{
    Natural,
    Command,
    Scope,
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
    private readonly ISettingsService _settings;
    private Keymap _defaults;
    private string _preset;
    private readonly Dictionary<string, List<KeyGesture>> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KeyMappingRow> _rows = new(StringComparer.Ordinal);
    private string _filter = "";
    private bool _byShortcut;
    private KeyMappingsSort _sort = KeyMappingsSort.Natural;
    private bool _descending;
    private KeyMappingRow? _selectedRow;
    private KeyGesture? _captured;
    private string? _captureError;
    private string? _conflictText;
    private bool _resetAll;

    internal KeyMappingsPageViewModel(IKeymapService keymap, ISettingsService settings, IDialogService dialogs, int implementedPhase)
    {
        _keymap = keymap;
        _settings = settings;
        _dialogs = dialogs;
        _preset = keymap.Preset;
        _defaults = KeymapPresets.Create(_preset);
        Presets = [.. KeymapPresets.All.Select(p => new OptionChoice(p, Strings.ResourceManager.GetString("Options_Keys_KeyMap_" + p, CultureInfo.CurrentCulture) ?? p))];
        var categories = CommandCatalog.All.Select(c => c.Category).Distinct().ToList();
        Rows =
        [
            .. CommandCatalog.All.Where(c => !c.TakesArgument)
                .Select(c => new KeyMappingRow(c, TitleOf(c), !Composition.Phase8Features.IsAvailable(c.Id, c.Phase, implementedPhase)))
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

    /// <summary>The built-in key maps of the <c>Key map:</c> drop-down (phase8-ui-additions.md D3).</summary>
    public IReadOnlyList<OptionChoice> Presets { get; }

    /// <summary>
    /// The pending built-in key map. Changing it shows the gestures of the chosen map at once; what the working copy
    /// has more or less than the map it leaves (the user's assignments) stays and wins over the new map.
    /// </summary>
    public string Preset
    {
        get => _preset;
        set
        {
            if (value == _preset || !KeymapPresets.All.Contains(value))
                return;
            var next = KeymapPresets.Create(value);
            var added = new List<(KeyMappingRow Row, KeyGesture Gesture)>();
            foreach (var row in Rows)
            {
                var old = _defaults.GetGestures(row.CommandId);
                var pending = _pending[row.CommandId];
                added.AddRange(pending.Where(g => !old.Contains(g)).Select(g => (row, g)));
                var removed = old.Where(g => !pending.Contains(g)).ToList();
                _pending[row.CommandId] = [.. next.GetGestures(row.CommandId).Where(g => !removed.Contains(g))];
            }

            foreach (var (row, gesture) in added)
            {
                TakeFromOthers(row.Command, gesture);
                _pending[row.CommandId].Add(gesture);
            }

            _preset = value;
            _defaults = next;
            OnPropertyChanged();
            Changed();
        }
    }

    /// <summary>Every listed command in natural order.</summary>
    public IReadOnlyList<KeyMappingRow> Rows { get; }

    /// <summary>The rows the filter lets through.</summary>
    public ObservableCollection<KeyMappingRow> VisibleRows { get; } = [];

    /// <summary>
    /// Case-insensitive substring of <c>&lt;Category&gt;: &lt;Title&gt;</c> or the id; with <see cref="ByShortcut"/>, a gesture:
    /// the commands bound to it and, for a single key, also those with a chord that starts with it.
    /// </summary>
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

    /// <summary>The grid's sort column (<see cref="KeyMappingsSort.Natural"/> = category order of commands.md, then title).</summary>
    public KeyMappingsSort Sort => _sort;

    public bool SortDescending => _descending;

    /// <summary>
    /// A column header was clicked: sorts the visible rows by it (Command by its text, Scope by its text then the
    /// command), a second click on the same column reverses (as in the Documents dialog); the filter keeps the order.
    /// </summary>
    public void SortBy(KeyMappingsSort sort)
    {
        _descending = _sort == sort && !_descending;
        _sort = sort;
        OnPropertyChanged(nameof(Sort));
        OnPropertyChanged(nameof(SortDescending));
        ApplyFilter();
    }

    private List<KeyMappingRow> Ordered(IEnumerable<KeyMappingRow> rows)
    {
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        var ordered = _sort switch
        {
            KeyMappingsSort.Command => rows.OrderBy(r => r.Text, comparer),
            KeyMappingsSort.Scope => rows.OrderBy(r => r.ScopeText, comparer).ThenBy(r => r.Text, comparer),
            _ => rows,
        };
        return [.. _descending ? ordered.Reverse() : ordered];
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

    /// <summary>
    /// While the captured gesture cannot stay beside a gesture of another command in an overlapping scope:
    /// <c>Msg.Options.KeyConflict</c> (the same gesture), <c>Msg.Options.KeyPrefixConflict</c> (it is the first key of a
    /// chord there) or <c>Msg.Options.KeyChordConflict</c> (it is a chord whose first key is a command there); else null.
    /// </summary>
    public string? ConflictText
    {
        get => _conflictText;
        private set => SetProperty(ref _conflictText, value);
    }

    public bool CanAssign => SelectedRow is not null && _captured is { } g && CaptureError is null && !_pending[SelectedRow.CommandId].Contains(g);

    public bool HasChanges => _preset != _keymap.Preset || Rows.Any(r => !SameSet(_pending[r.CommandId], _keymap.GetGestures(r.CommandId)));

    /// <summary>Pending gestures of a command.</summary>
    public IReadOnlyList<KeyGesture> PendingGestures(string commandId) => _pending[commandId];

    /// <summary>The capture box holds <paramref name="gesture"/> (null = cleared with Backspace).</summary>
    public void Capture(KeyGesture? gesture)
    {
        _captured = gesture;
        UpdateCapture();
    }

    /// <summary>
    /// A key was pressed in the capture box (UI-KEY-024): after one valid single key it is the second key of a chord;
    /// otherwise — the box is empty, holds a chord already or a refused key — it starts over as the first key.
    /// </summary>
    public void CaptureKey(KeyGesture key) =>
        Capture(_captured is { IsChord: false } first && CaptureError is null ? KeyGesture.Chord(first, key) : key);

    /// <summary>
    /// A key was pressed in the filter box while <see cref="ByShortcut"/> (null = Backspace, cleared): the filter is that
    /// key — or, when the filter is a key that some command's chord starts with, the chord of the two (UI-KEY-024).
    /// </summary>
    public void CaptureFilterKey(KeyGesture? key)
    {
        if (key is not { } pressed)
        {
            Filter = "";
            return;
        }

        var startsChord = KeyGesture.TryParse(_filter, out var current) && !current.IsChord
                          && Rows.Any(r => _pending[r.CommandId].Exists(g => g.StartsWith(current)));
        Filter = (startsChord ? KeyGesture.Chord(current, pressed) : pressed).ToString();
    }

    /// <summary>
    /// <c>Assign</c>: adds the captured gesture and removes the conflicting gestures from the other commands — and the
    /// selected command's own gesture that overlaps it (its chord <c>Ctrl+K, Ctrl+F</c> when <c>Ctrl+K</c> is assigned, or
    /// the other way round), so that no gesture is left that could never be pressed (UI-KEY-012).
    /// </summary>
    public void Assign()
    {
        if (!CanAssign || SelectedRow is not { } row || _captured is not { } gesture)
            return;
        TakeFromOthers(row.Command, gesture);
        _pending[row.CommandId].RemoveAll(own => own.Overlaps(gesture)); // the command's own chord or first key gives way too
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
            TakeFromOthers(row.Command, gesture);
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
        Strings.Options_Keys_KeyMap, Strings.Options_Keys_Filter, Strings.Options_Keys_ByShortcut, Strings.Options_Keys_NewShortcut, Strings.Options_Keys_Assign,
        Strings.Options_Keys_ResetCommand, Strings.Options_Keys_ResetAll,
    ];

    /// <summary>Writes the changed commands through the keymap service (OK/Apply).</summary>
    internal void Apply()
    {
        if (_preset != _keymap.Preset)
        {
            _settings.Set(SettingsCatalog.KeyboardPreset, _preset);
            _keymap.SetPreset(_preset); // the user's assignments are kept on top of it
        }

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

    /// <summary>
    /// The pending gestures of other commands in an overlapping scope that cannot stay beside <paramref name="gesture"/>
    /// (<see cref="KeyGesture.Overlaps"/>, UI-KEY-012): the same gesture, a chord that starts with it, or the single key
    /// it starts with.
    /// </summary>
    private List<(CommandDefinition Command, KeyGesture Gesture)> Conflicts(CommandDefinition command, KeyGesture gesture) =>
    [
        .. from row in Rows
           where row.CommandId != command.Id && Overlap(command, row.Command)
           from theirs in _pending[row.CommandId]
           where theirs.Overlaps(gesture)
           select (row.Command, theirs),
    ];

    /// <summary>Removes what <see cref="Conflicts"/> lists: <paramref name="gesture"/> goes to <paramref name="command"/> (UI-KEY-021).</summary>
    private void TakeFromOthers(CommandDefinition command, KeyGesture gesture)
    {
        foreach (var (other, theirs) in Conflicts(command, gesture))
        {
            _pending[other.Id].Remove(theirs);
        }
    }

    /// <summary>One line per kind of conflict: the same gesture, chords that start with it, the single key it starts with.</summary>
    private string ConflictMessage(KeyGesture gesture, List<(CommandDefinition Command, KeyGesture Gesture)> others)
    {
        string? Titles(Func<KeyGesture, bool> kind) =>
            others.Where(o => kind(o.Gesture)).Select(o => _rows[o.Command.Id].Text).Distinct().ToList() is { Count: > 0 } titles
                ? string.Join(", ", titles)
                : null;

        var lines = new List<string>();
        if (Titles(theirs => theirs == gesture) is { } same)
            lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_KeyConflict, gesture, same));
        if (Titles(theirs => theirs.StartsWith(gesture)) is { } chords)
            lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_KeyPrefixConflict, gesture, chords));
        if (Titles(gesture.StartsWith) is { } singles)
            lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_KeyChordConflict, gesture, gesture.First, singles));
        return string.Join(Environment.NewLine, lines);
    }

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
        row.ShortcutText = KeyGesture.Join(pending);
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
            // A chord is refused for the key that is wrong: a reserved key in either place, a first key that would be typed.
            if (KeymapService.ReservedKey(gesture) is { } reserved)
                error = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_KeyReserved, reserved);
            else if (SelectedRow is { } row && PreventsTyping(gesture.First) && IsTypingScope(row.Command))
                error = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Options_KeyNeedsModifier, gesture.First);
            if (error is null && SelectedRow is { } selected && Conflicts(selected.Command, gesture) is { Count: > 0 } others)
                conflict = ConflictMessage(gesture, others);
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
            match = KeyGesture.TryParse(text, out var gesture)
                ? r => _pending[r.CommandId].Exists(g => g == gesture || g.StartsWith(gesture))
                : _ => text.Length == 0;
        }
        else
        {
            match = r => text.Length == 0 || r.Text.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                                          || r.CommandId.Contains(text, StringComparison.OrdinalIgnoreCase);
        }

        var visible = Ordered(Rows.Where(match));
        if (visible.SequenceEqual(VisibleRows))
            return;
        VisibleRows.Clear();
        foreach (var row in visible)
        {
            VisibleRows.Add(row);
        }
    }
}
