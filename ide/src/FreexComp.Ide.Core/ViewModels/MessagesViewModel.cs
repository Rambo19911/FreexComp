using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ToolWindows;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>One visible row of the Messages list: a diagnostic, a related child row or the summary (UI-TW-400…403).</summary>
public sealed partial class MessageRow : ObservableObject
{
    internal MessageRow(MessageItem item, string key, MessageItem? parent)
    {
        Item = item;
        Key = key;
        Parent = parent;
    }

    public MessageItem Item { get; }

    /// <summary>UI-TW-411: arrival index, <c>&lt;index&gt;.r&lt;n&gt;</c> for related rows, <c>summary</c>.</summary>
    public string Key { get; }

    /// <summary>The diagnostic of a related child row.</summary>
    public MessageItem? Parent { get; }

    public bool IsChild => Parent is not null;

    public bool HasChildren => Parent is null && Item.Related.Count > 0;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Severity word: the automation name and tooltip of the icon cell (UI-A11Y-021).</summary>
    public string SeverityText => IsChild ? "" : Item.Severity switch
    {
        MessageSeverity.Error => Strings.MessagesView_Severity_Error,
        MessageSeverity.Warning => Strings.MessagesView_Severity_Warning,
        MessageSeverity.Hint => Strings.MessagesView_Severity_Hint,
        _ => Strings.MessagesView_Severity_Info,
    };

    /// <summary>Icon kind: <c>error</c>, <c>warning</c>, <c>hint</c>, <c>info</c>, <c>success</c>; empty for child rows.</summary>
    public string Icon => IsChild ? "" : Item switch
    {
        { IsSummary: true, Severity: MessageSeverity.Info } => "success",
        { Severity: MessageSeverity.Error } => "error",
        { Severity: MessageSeverity.Warning } => "warning",
        { Severity: MessageSeverity.Hint } => "hint",
        _ => "info",
    };

    public string Code => Item.Code;

    /// <summary>UI-TW-403: related rows carry the ↳ prefix.</summary>
    public string Description => IsChild ? "↳ " + Item.Text : Item.Text;

    public string File => Item.Location?.FilePath is { } path ? Path.GetFileName(path) : "";

    public string FullPath => Item.Location?.FilePath ?? "";

    public string Line => Item.Location is { Line: > 0 } l ? l.Line.ToString(CultureInfo.CurrentCulture) : "";

    public string Col => Item.Location is { Line: > 0 } l ? l.Column.ToString(CultureInfo.CurrentCulture) : "";

    /// <summary>UI-TW-408: the compiler's text diagnostic format; rows without position give their description.</summary>
    public string CopyText
    {
        get
        {
            if (Item.Location is not { FilePath: { } path, Line: > 0 } location)
                return Item.Text;
            var severity = Item.Severity switch
            {
                MessageSeverity.Error => "error",
                MessageSeverity.Warning => "warning",
                MessageSeverity.Hint => "hint",
                _ => "info",
            };
            return string.Create(CultureInfo.InvariantCulture, $"{path}({location.Line},{location.Column}): {severity} {Item.Code}: {Item.Text}")
                .Replace(" : ", ": ", StringComparison.Ordinal);
        }
    }
}

/// <summary>
/// The Messages tool window (tool-windows.md §6): rows of <see cref="MessagesService"/>, severity toggles with counts
/// (UI-TW-406), filter (UI-TW-407), copy (UI-TW-408), related rows (UI-TW-403), tab title counts (UI-TW-410). UI thread only.
/// </summary>
public sealed class MessagesViewModel : ToolWindowViewModel, ICommandTarget
{
    public const string ToolName = "Messages";

    private readonly MessagesService _messages;
    private readonly ISettingsService _settings;
    private readonly IClipboardService _clipboard;
    private readonly HashSet<int> _expanded = [];
    private ICommandService? _commands;
    private IReadOnlyList<MessageRow> _rows = [];
    private string _filter = "";

    public MessagesViewModel(MessagesService messages, ISettingsService settings, IClipboardService clipboard)
    {
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _messages.Changed += (_, _) =>
        {
            if (_messages.Items.Count == 0)
                _expanded.Clear();
            Refresh();
        };
        _settings.Changed += (_, e) =>
        {
            if (e.Key.StartsWith("messages.show", StringComparison.Ordinal))
                Refresh();
        };
        _messages.IsVisible = Shows;
        _messages.CurrentChanged += (_, _) => OnPropertyChanged(nameof(CurrentRow));
        Refresh();
    }

    /// <summary>tool-windows.md §1.2: bottom dock, Phase 3.</summary>
    public ToolWindowDefinition Definition => new(ToolName, "Cmd_view_messages", ToolGroupId.Bottom, 3, () => this);

    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands { get; } =
    [
        new("messages.toggleErrors", "error"),
        new("messages.toggleWarnings", "warning"),
        new("messages.toggleHints", "hint"),
        new("messages.clear", "clear"),
    ];

    public override bool HasFilter => true;

    /// <summary>The visible rows in display order (summary last).</summary>
    public IReadOnlyList<MessageRow> Rows => _rows;

    /// <summary>The rows selected in the view (multi-selection, UI-TW-408).</summary>
    public IReadOnlyList<MessageRow> SelectedRows { get; set; } = [];

    /// <summary>No build yet or cleared: <c>Msg.Messages.Empty</c>.</summary>
    public bool IsEmpty => _messages.Items.Count == 0;

    /// <summary>UI-TW-407: diagnostics exist but toggles/filter hide all of them.</summary>
    public bool IsFilteredEmpty => !IsEmpty && _rows.All(r => r.Item.IsSummary) && _messages.Items.Any(i => !i.IsSummary);

    public override string TabAutomationName
    {
        get
        {
            var parts = new List<string>();
            if (_messages.ErrorCount > 0)
                parts.Add(Count(_messages.ErrorCount, Strings.MessagesView_ErrorsOne, Strings.MessagesView_ErrorsMany).ToLower(CultureInfo.CurrentCulture));
            if (_messages.WarningCount > 0)
                parts.Add(Count(_messages.WarningCount, Strings.MessagesView_WarningsOne, Strings.MessagesView_WarningsMany).ToLower(CultureInfo.CurrentCulture));
            return parts.Count == 0 ? Strings.Cmd_view_messages : string.Join(", ", [Strings.Cmd_view_messages, .. parts]);
        }
    }

    /// <summary>The label of a toggle button: icon + count + noun (UI-TW-406).</summary>
    public string ToggleLabel(MessageSeverity severity) => severity switch
    {
        MessageSeverity.Error => Count(_messages.ErrorCount, Strings.MessagesView_ErrorsOne, Strings.MessagesView_ErrorsMany),
        MessageSeverity.Warning => Count(_messages.WarningCount, Strings.MessagesView_WarningsOne, Strings.MessagesView_WarningsMany),
        _ => Count(_messages.HintCount, Strings.MessagesView_HintsOne, Strings.MessagesView_HintsMany),
    };

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler("messages.clear", new Handler(() => !IsEmpty, () =>
        {
            _messages.Clear();
            return Task.CompletedTask;
        }));
        commands.RegisterHandler("messages.copy", new Handler(() => SelectedRows.Count > 0, CopySelectedAsync));
        commands.RegisterHandler("messages.copyAll", new Handler(() => !IsEmpty, () => _clipboard.SetTextAsync(Join(AllRows()))));
        commands.RegisterHandler("messages.goToSource", new Handler(() => SelectedRows.Count > 0 && SelectedRows[0].Item.IsNavigable,
            () => _messages.NavigateToAsync(SelectedRows[0].Item)));
        Toggle(commands, "messages.toggleErrors", SettingsCatalog.MessagesShowErrors, MessageSeverity.Error);
        Toggle(commands, "messages.toggleWarnings", SettingsCatalog.MessagesShowWarnings, MessageSeverity.Warning);
        Toggle(commands, "messages.toggleHints", SettingsCatalog.MessagesShowHints, MessageSeverity.Hint);
    }

    /// <summary>tool-windows.md §6 context menu (null = separator).</summary>
    public static readonly IReadOnlyList<string?> ContextCommands =
        ["messages.goToSource", null, "messages.copy", "messages.copyAll", null, "messages.clear"];

    public IReadOnlyList<Menus.MenuItemModel> BuildContextMenu()
    {
        var commands = _commands ?? throw new InvalidOperationException("Commands are not registered.");
        return [.. ContextCommands.Select(id => id is null
            ? Menus.MenuItemModel.Separator()
            : Menus.MenuItemModel.ForCommand(commands.Get(id).Title, commands.Get(id), null, Menus.MenuToggleKind.None))];
    }

    /// <summary>Ctrl+C (<c>edit.copy</c>, Focus scope) in the list copies the selected rows (UI-TW-408).</summary>
    public bool CanExecute(string commandId, string? argument) => commandId == "edit.copy" && SelectedRows.Count > 0;

    public async Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        if (!CanExecute(commandId, argument))
            return false;
        await CopySelectedAsync();
        return true;
    }

    /// <summary>UI-TW-408: the selected rows in the text diagnostic format, one per line, CRLF.</summary>
    public Task CopySelectedAsync() => _clipboard.SetTextAsync(Join(SelectedRows));

    /// <summary>UI-TW-404: the default action of a row (double-click; Enter runs <c>messages.goToSource</c>).</summary>
    public Task GoToAsync(MessageRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        SelectedRows = [row];
        return _messages.NavigateToAsync(row.Item);
    }

    /// <summary>UI-TW-403: Right expands, Left collapses a row with related locations.</summary>
    public void SetExpanded(MessageRow row, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.HasChildren || row.IsExpanded == expanded)
            return;
        if (expanded)
            _expanded.Add(row.Item.Index);
        else
            _expanded.Remove(row.Item.Index);
        Refresh();
    }

    /// <summary>The <c>Clear Filter</c> link of <c>Msg.Messages.FilteredEmpty</c>: clears the filter and presses every toggle.</summary>
    public void ClearFilter()
    {
        FilterText = "";
        _filter = "";
        _settings.Set(SettingsCatalog.MessagesShowErrors, true);
        _settings.Set(SettingsCatalog.MessagesShowWarnings, true);
        _settings.Set(SettingsCatalog.MessagesShowHints, true);
        Refresh();
    }

    /// <summary>The row of <see cref="MessagesService.Current"/> (F8 selects it, UI-TW-405).</summary>
    public MessageRow? CurrentRow => _messages.Current is { } current ? _rows.FirstOrDefault(r => r.Item == current) : null;

    protected override void OnFilterApplied(string filter)
    {
        _filter = filter;
        Refresh();
    }

    private static string Count(int count, string one, string many) =>
        string.Format(CultureInfo.CurrentCulture, count == 1 ? one : many, count);

    private static string Join(IEnumerable<MessageRow> rows) => string.Join("\r\n", rows.Select(r => r.CopyText));

    private IEnumerable<MessageRow> AllRows()
    {
        foreach (var item in _messages.Items)
        {
            yield return new MessageRow(item, Key(item), null);
            for (var i = 0; i < item.Related.Count; i++)
                yield return new MessageRow(item.Related[i], Key(item) + ".r" + i.ToString(CultureInfo.InvariantCulture), item);
        }
    }

    private static string Key(MessageItem item) => item.IsSummary ? "summary" : item.Index.ToString(CultureInfo.InvariantCulture);

    private bool Shows(MessageItem item)
    {
        if (item.IsSummary)
            return true;
        var shown = item.Severity switch
        {
            MessageSeverity.Error => _settings.Get(SettingsCatalog.MessagesShowErrors),
            MessageSeverity.Warning => _settings.Get(SettingsCatalog.MessagesShowWarnings),
            MessageSeverity.Hint => _settings.Get(SettingsCatalog.MessagesShowHints),
            _ => true,
        };
        if (!shown)
            return false;
        if (_filter.Length == 0)
            return true;
        var file = item.Location?.FilePath is { } path ? Path.GetFileName(path) : "";
        return item.Code.Contains(_filter, StringComparison.OrdinalIgnoreCase)
            || item.Text.Contains(_filter, StringComparison.OrdinalIgnoreCase)
            || file.Contains(_filter, StringComparison.OrdinalIgnoreCase);
    }

    private void Refresh()
    {
        var rows = new List<MessageRow>();
        foreach (var item in _messages.Items.Where(Shows))
        {
            var row = new MessageRow(item, Key(item), null) { IsExpanded = _expanded.Contains(item.Index) };
            rows.Add(row);
            if (row.HasChildren && row.IsExpanded)
            {
                for (var i = 0; i < item.Related.Count; i++)
                    rows.Add(new MessageRow(item.Related[i], row.Key + ".r" + i.ToString(CultureInfo.InvariantCulture), item));
            }
        }

        _rows = rows;
        SelectedRows = [.. SelectedRows.Select(s => rows.FirstOrDefault(r => r.Key == s.Key)).OfType<MessageRow>()];
        var counts = new List<string>();
        if (_messages.ErrorCount > 0)
            counts.Add(string.Format(CultureInfo.CurrentCulture, Strings.MessagesView_TitleErrors, _messages.ErrorCount));
        if (_messages.WarningCount > 0)
            counts.Add(string.Format(CultureInfo.CurrentCulture, Strings.MessagesView_TitleWarnings, _messages.WarningCount));
        SetTitle(counts.Count == 0
            ? Strings.Cmd_view_messages
            : string.Format(CultureInfo.CurrentCulture, Strings.MessagesView_TitleCounts, Strings.Cmd_view_messages, string.Join(", ", counts)));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(TabAutomationName));
        _commands?.InvalidateCanExecute();
    }

    private void Toggle(ICommandService commands, string id, SettingKey<bool> key, MessageSeverity severity) =>
        commands.RegisterHandler(id, new ToggleHandler(this, () => _settings.Get(key), () =>
        {
            _settings.Set(key, !_settings.Get(key));
            return Task.CompletedTask;
        }, severity));

    private sealed class Handler(Func<bool> canExecute, Func<Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();
    }

    private sealed class ToggleHandler(MessagesViewModel owner, Func<bool> isChecked, Func<Task> execute, MessageSeverity severity) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument) => execute();

        public bool IsChecked(string? argument) => isChecked();

        public string? TitleOverride => owner.ToggleLabel(severity);
    }
}
