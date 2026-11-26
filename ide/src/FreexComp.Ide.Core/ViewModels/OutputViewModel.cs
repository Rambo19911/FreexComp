using System.ComponentModel;
using System.Text.RegularExpressions;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ToolWindows;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>A change of the visible Output text: replace everything (<see cref="Reset"/>) or append <see cref="Lines"/>.</summary>
public sealed record OutputTextChange(bool Reset, IReadOnlyList<OutputLine> Lines);

/// <summary>
/// A navigable position in an Output line (IDE-P3-075, UI-TW-503): <c>&lt;abs-path&gt;(&lt;line&gt;,&lt;col&gt;)</c>.
/// <see cref="PathStart"/>/<see cref="PathLength"/> locate the path part in the line (0-based, for the hover underline);
/// <see cref="Severity"/> is <c>error</c>/<c>warning</c>/<c>hint</c> when the line has the text diagnostic format
/// (<c>compiler-cli.md</c> §2), else null.
/// </summary>
public sealed partial record OutputLocation(string Path, int Line, int Column, int PathStart, int PathLength, string? Severity)
{
    /// <summary>The first <c>&lt;abs-path&gt;(&lt;line&gt;,&lt;col&gt;)</c> of <paramref name="text"/>, else null.</summary>
    public static OutputLocation? TryParse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (Match m in Location().Matches(text))
        {
            var path = m.Groups["path"].Value;
            if (!System.IO.Path.IsPathFullyQualified(path)
                || !int.TryParse(m.Groups["line"].ValueSpan, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var line)
                || !int.TryParse(m.Groups["col"].ValueSpan, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var column))
                continue;
            var severity = m.Groups["sev"].Success && m.Index == 0 ? m.Groups["sev"].Value : null;
            return new OutputLocation(path, line, column, m.Groups["path"].Index, path.Length, severity);
        }

        return null;
    }

    // The path starts at a drive root or a UNC prefix; lazy, so it ends at the first "(line,col)" (paths may contain "(x86)").
    [GeneratedRegex(@"(?<path>(?:[A-Za-z]:\\|\\\\)[^<>|""*?\r\n]*?)\((?<line>\d{1,9}),(?<col>\d{1,9})\)(?:: (?<sev>error|warning|hint) )?")]
    private static partial Regex Location();
}

/// <summary>
/// The Output tool window (tool-windows.md §7): the visible channel of <see cref="IOutputService"/>, the channel combo
/// (UI-TW-501), Clear / Copy All / Word Wrap / Scroll Lock (UI-TW-504, 506), line colours (UI-TW-502) and navigation from
/// lines naming <c>&lt;abs-path&gt;(&lt;line&gt;,&lt;col&gt;)</c> (UI-TW-503, IDE-P3-075). UI thread only.
/// </summary>
public sealed class OutputViewModel : ToolWindowViewModel
{
    public const string ToolName = "Output";

    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly IOutputService _output;
    private readonly ISettingsService _settings;
    private readonly IClipboardService _clipboard;
    private readonly IDocumentService _documents;
    private readonly INavigationService _navigation;
    private readonly IStatusService _status;
    private readonly IFileSystem _fileSystem;
    private readonly ToolWindowToolBarChoice _channel;
    private IReadOnlyList<OutputLine> _lines = [];
    private bool _scrollLock;
    private int? _fontSize;
    private ICommandService? _commands;

    public OutputViewModel(IOutputService output, ISettingsService settings, IClipboardService clipboard, IDocumentService documents,
        INavigationService navigation, IStatusService status, IFileSystem fileSystem)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _channel = new ToolWindowToolBarChoice("Channel", Strings.Msg_Output_ChannelLabel, 140,
            [.. output.Channels.Select(c => new ToolBarChoiceItem(c, ChannelLabel(c)))]);
        _channel.PropertyChanged += OnChannelChanged;
        _output.Changed += OnOutputChanged;
        _settings.Changed += (_, e) =>
        {
            if (e.Key == SettingsCatalog.OutputWordWrap.Name)
            {
                OnPropertyChanged(nameof(WordWrap));
                _commands?.InvalidateCanExecute();
            }
        };
        _lines = _output.GetLines(SelectedChannel);
        var buffer = new StringTextBufferFactory().Create(Join(_lines), LineEndingKind.Lf);
        buffer.IsReadOnly = true;
        Content = new TextDocumentViewModel(buffer, null, Strings.Cmd_view_output, new Languages.PlainTextLanguage(), FileEncoding.Utf8,
            LineEndingKind.Lf);
    }

    /// <summary>
    /// UI-TW-500: the visible text as a read-only, never-saved text document (lines joined with LF, as the view shows
    /// them), so the editor's find panel works over the Output (<c>FindService.For(Content)</c>, find mode only). The
    /// view mirrors its caret and selection into it and follows the selection a find makes.
    /// </summary>
    public TextDocumentViewModel Content { get; }

    /// <summary>UI-TW-500 / UI-ED-061: Ctrl+wheel zooms the Output text when <c>editor.mouseWheelZoom</c> is on.</summary>
    public bool MouseWheelZoom => _settings.Get(SettingsCatalog.EditorMouseWheelZoom);

    /// <summary>The zoomed font size (6…72 pt); null = the code font size. Not persisted (the spec stores no Output zoom).</summary>
    public int? FontSize
    {
        get => _fontSize;
        private set => SetProperty(ref _fontSize, value);
    }

    /// <summary>One wheel notch: ±1 pt from <paramref name="baseSize"/> (or the current zoom), clamped to 6…72 pt.</summary>
    public void Zoom(bool zoomIn, int baseSize)
    {
        var size = Math.Clamp((FontSize ?? baseSize) + (zoomIn ? 1 : -1), TextDocumentViewModel.MinFontSize, TextDocumentViewModel.MaxFontSize);
        FontSize = size == baseSize ? null : size;
    }

    private static string Join(IEnumerable<OutputLine> lines) => string.Join("\n", lines.Select(l => l.Text));

    /// <summary>The registration of the Output window (tool-windows.md §1.2: bottom dock, Phase 3).</summary>
    public ToolWindowDefinition Definition => new(ToolName, "Cmd_view_output", ToolGroupId.Bottom, 3, () => this);

    public override IReadOnlyList<ToolWindowToolBarItem> ToolBarCommands { get; } =
    [
        new("output.clear", "trash"),
        new("output.toggleWordWrap", "word-wrap"),
        new("output.toggleScrollLock", "scroll-lock"),
    ];

    public override ToolWindowToolBarChoice? ToolBarChoice => _channel;

    /// <summary>The id of the visible channel.</summary>
    public string SelectedChannel => _channel.SelectedId;

    /// <summary>The lines of the visible channel as shown.</summary>
    public IReadOnlyList<OutputLine> Lines => _lines;

    /// <summary>UI-TW-507: the visible channel has no lines.</summary>
    public bool IsEmpty => _lines.Count == 0;

    /// <summary>UI-TW-506: <c>output.wordWrap</c>.</summary>
    public bool WordWrap => _settings.Get(SettingsCatalog.OutputWordWrap);

    /// <summary>UI-TW-504: while on, appended text never scrolls the view.</summary>
    public bool ScrollLock
    {
        get => _scrollLock;
        private set
        {
            if (SetProperty(ref _scrollLock, value))
                _commands?.InvalidateCanExecute();
        }
    }

    /// <summary>The visible text changed; the view applies the change (append keeps the scroll position logic, UI-TW-504).</summary>
    public event EventHandler<OutputTextChange>? TextChanged;

    /// <summary>UI-TW-502: the colour token of a line (whole line).</summary>
    public static string ColorToken(string channel, OutputLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (channel == OutputChannels.Build && OutputLocation.TryParse(line.Text) is { Severity: { } severity })
            return "status." + severity;
        return line.Kind switch
        {
            OutputKind.Error or OutputKind.Failure => "status.error",
            OutputKind.Command => "text.tertiary",
            OutputKind.Success => "status.success",
            OutputKind.Ide when channel == OutputChannels.Program => "text.tertiary",
            _ => "text.primary",
        };
    }

    /// <summary>The text of the visible channel (lines joined with CRLF): what Copy All copies.</summary>
    public string AllText => string.Join("\r\n", _lines.Select(l => l.Text));

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler("output.clear", new Handler(() => !IsEmpty, _ =>
        {
            _output.Clear(SelectedChannel);
            return Task.CompletedTask;
        }));
        commands.RegisterHandler("output.copyAll", new Handler(() => !IsEmpty, _ => _clipboard.SetTextAsync(AllText)));
        commands.RegisterHandler("output.toggleWordWrap", new Handler(() => true, _ =>
        {
            _settings.Set(SettingsCatalog.OutputWordWrap, !WordWrap);
            return Task.CompletedTask;
        }, () => WordWrap));
        commands.RegisterHandler("output.toggleScrollLock", new Handler(() => true, _ =>
        {
            ScrollLock = !ScrollLock;
            return Task.CompletedTask;
        }, () => ScrollLock));
        commands.RegisterHandler("output.selectChannel", new Handler(() => true, argument =>
        {
            SelectChannel(argument ?? SelectedChannel);
            return Task.CompletedTask;
        }));
    }

    /// <summary>tool-windows.md §7 context menu (null = separator); labels are the command titles.</summary>
    public static readonly IReadOnlyList<string?> ContextCommands =
    [
        "edit.copy", "edit.selectAll", "output.copyAll", null, "search.find", null, "output.toggleWordWrap",
        "output.toggleScrollLock", null, "output.clear",
    ];

    public IReadOnlyList<Menus.MenuItemModel> BuildContextMenu()
    {
        var commands = _commands ?? throw new InvalidOperationException("Commands are not registered.");
        return [.. ContextCommands.Select(id => id is null
            ? Menus.MenuItemModel.Separator()
            : Menus.MenuItemModel.ForCommand(commands.Get(id).Title, commands.Get(id), null,
                commands.Get(id).Definition.IsToggle ? Menus.MenuToggleKind.Check : Menus.MenuToggleKind.None))];
    }

    /// <summary><c>output.selectChannel</c>: shows <paramref name="channel"/>; an unknown id changes nothing.</summary>
    public void SelectChannel(string channel)
    {
        var index = _channel.Items.ToList().FindIndex(i => i.Id == channel);
        if (index >= 0)
            _channel.SelectedIndex = index;
    }

    /// <summary>
    /// UI-TW-503: navigates to the location named in the 1-based <paramref name="line"/> of the visible text — open the
    /// file, caret to line:col (scrolled to one third), editor focused. False when the line names no location or the file
    /// is missing (<c>Msg.Messages.SourceMissing</c>).
    /// </summary>
    public async Task<bool> NavigateToLineAsync(int line)
    {
        if (line < 1 || line > _lines.Count || OutputLocation.TryParse(_lines[line - 1].Text) is not { } location)
            return false;
        if (!_fileSystem.FileExists(location.Path))
        {
            _status.ShowMessage(Strings.Msg_Messages_SourceMissing, StatusDuration);
            return false;
        }

        var opened = await _documents.OpenAsync(location.Path);
        if (opened.Value is not TextDocumentViewModel document)
            return false;
        _navigation.GoToLine(document, location.Line, location.Column);
        ReturnFocusToDocument();
        return true;
    }

    private static string ChannelLabel(string channel) =>
        Strings.ResourceManager.GetString("Output_Channel_" + channel, System.Globalization.CultureInfo.CurrentCulture) ?? channel;

    private void OnChannelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ToolWindowToolBarChoice.SelectedIndex))
            return;
        Replace(_output.GetLines(SelectedChannel), reset: true);
        OnPropertyChanged(nameof(SelectedChannel));
    }

    private void OnOutputChanged(object? sender, string channel)
    {
        if (channel != SelectedChannel)
            return;
        var lines = _output.GetLines(channel);
        // Append only when what is shown is still the start of the channel (no truncation or clear in between).
        var appended = lines.Count >= _lines.Count && _lines.Select((l, i) => l == lines[i]).All(same => same);
        Replace(lines, reset: !appended);
    }

    private void Replace(IReadOnlyList<OutputLine> lines, bool reset)
    {
        var wasEmpty = IsEmpty;
        var added = reset ? lines : lines.Skip(_lines.Count).ToList();
        _lines = lines;
        SyncContent(reset, added);
        if (reset || added.Count > 0)
            TextChanged?.Invoke(this, new OutputTextChange(reset, added));
        if (wasEmpty != IsEmpty)
            OnPropertyChanged(nameof(IsEmpty));
        _commands?.InvalidateCanExecute();
    }

    /// <summary>Keeps <see cref="Content"/> equal to the view's text: a reset reloads it, an append inserts at the end.</summary>
    private void SyncContent(bool reset, IReadOnlyList<OutputLine> added)
    {
        if (reset)
        {
            Content.Reload(Join(_lines));
            return;
        }

        if (added.Count == 0)
            return;
        var buffer = Content.Buffer;
        var text = (buffer.Length > 0 ? "\n" : "") + Join(added);
        buffer.IsReadOnly = false;
        try
        {
            buffer.ApplyEdits([new TextEdit(buffer.Length, 0, text)]);
        }
        finally
        {
            buffer.IsReadOnly = true;
        }
    }

    private sealed class Handler(Func<bool> canExecute, Func<string?, Task> execute, Func<bool>? isChecked = null) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute(argument);

        public bool IsChecked(string? argument) => isChecked?.Invoke() ?? false;
    }
}
