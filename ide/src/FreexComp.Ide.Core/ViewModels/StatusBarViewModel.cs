using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>The status bar's background kind (one <c>statusbar.*.bg</c> token each).</summary>
public enum StatusBarState
{
    Idle,
    Building,
    Running,
    Debugging,
}

/// <summary>
/// Region 8, the status bar (main-window.md §5): state (L1), message (L3) and the active text editor's fields R2–R8.
/// Fields of later phases (L2 progress, R1 diagnostics, R9 configuration, the designer field) arrive with their tasks;
/// R6/R7 open menus (I-082). Texts come from <see cref="Strings"/>; the view only lays them out.
/// </summary>
public sealed class StatusBarViewModel : ObservableObject
{
    private readonly IStatusService _status;
    private readonly ISettingsService _settings;
    private readonly IDocumentService? _documents;
    private readonly IIdeState? _state;
    private readonly IProjectService? _projects;
    private readonly IMessagesService? _messages;
    private string? _result;

    public StatusBarViewModel(IStatusService status, ICommandService commands, ISettingsService settings, IDocumentService? documents = null,
        IIdeState? state = null, IProjectService? projects = null, IBuildService? build = null, IMessagesService? messages = null)
    {
        _state = state;
        _projects = projects;
        _messages = messages;
        state?.Changed += (_, _) =>
        {
            // IDE-P3-005: a build result stays in L1 until the next state change (the build's own return to Idle keeps it).
            if (state.Current.State != IdeState.Idle)
                _result = null;
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(StateKind));
        };
        build?.BuildFinished += (_, _) =>
        {
            _result = build.ResultText;
            OnPropertyChanged(nameof(StateText));
        };
        if (documents is not null)
        {
            // IDE-P3-005: ... or until the next edit of any document.
            foreach (var document in documents.Documents.OfType<TextDocumentViewModel>())
                document.Buffer.Changed += OnDocumentEdited;
            documents.DocumentOpened += (_, e) =>
            {
                if (e.Document is TextDocumentViewModel text)
                    text.Buffer.Changed += OnDocumentEdited;
            };
            documents.DocumentClosed += (_, e) =>
            {
                if (e.Document is TextDocumentViewModel text)
                    text.Buffer.Changed -= OnDocumentEdited;
            };
        }
        messages?.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(DiagnosticsText));
            OnPropertyChanged(nameof(HasDiagnostics));
        };
        projects?.ProjectChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ConfigurationText));
            OnPropertyChanged(nameof(HasProject));
        };
        ConfigurationCommand = commands.Get("project.setConfiguration");
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(settings);
        _status = status;
        _settings = settings;
        _documents = documents;
        StateCommand = commands.Get("view.messages");
        CaretCommand = commands.Get("search.goToLine");
        InsertModeCommand = commands.Get("edit.toggleOverwrite");
        IndentCommand = commands.Get("tools.options");
        LineEndingCommand = commands.Get(LineEndingModel.ConvertCommandId);
        EncodingCommand = commands.Get("edit.setEncoding");
        status.Changed += (_, _) => OnPropertyChanged(string.Empty);
        settings.Changed += (_, e) =>
        {
            if (e.Key == SettingsCatalog.AppearanceShowStatusBar.Name)
            {
                OnPropertyChanged(nameof(IsVisible));
            }
        };
    }

    private void OnDocumentEdited(object? sender, TextChangedEventArgs e)
    {
        if (_result is null)
            return;
        _result = null;
        OnPropertyChanged(nameof(StateText));
    }

    /// <summary>UI-WIN-044 (<c>appearance.showStatusBar</c>).</summary>
    public bool IsVisible => _settings.Get(SettingsCatalog.AppearanceShowStatusBar);

    /// <summary>L1: the IDE state text (states-and-messages.md §9); Idle without a state machine.</summary>
    public string StateText => _state?.Current switch
    {
        { State: IdeState.Idle } when _result is not null => _result,
        null or { State: IdeState.Idle } => Strings.Msg_State_Idle,
        { State: IdeState.Building, CancelRequested: true } => Strings.Msg_State_Canceling,
        { State: IdeState.Building, BuildKind: BuildKind.Clean } => Format(Strings.Msg_State_Cleaning, ProjectName),
        { State: IdeState.Building, BuildKind: BuildKind.CheckSyntax } => Strings.Msg_State_Checking,
        { State: IdeState.Building } => Format(Strings.Msg_State_Building, ProjectName, _projects?.Project?.ActiveConfiguration ?? ""),
        { State: IdeState.Running } s => Format(Strings.Msg_State_Running, Path.GetFileName(s.ProgramPath ?? "")),
        { State: IdeState.DebugRunning } s => Format(Strings.Msg_State_Debugging, Path.GetFileName(s.ProgramPath ?? "")),
        { PausedFile: { } file } s => Format(Strings.Msg_State_Paused, Path.GetFileName(file), s.PausedLine),
        _ => Strings.Msg_State_PausedNoSource,
    };

    /// <summary>L1 background: <c>statusbar.&lt;kind&gt;.bg</c> (states-and-messages.md §9, design-tokens.md §2.5).</summary>
    public StatusBarState StateKind => _state?.Current.State switch
    {
        IdeState.Building => StatusBarState.Building,
        IdeState.Running => StatusBarState.Running,
        IdeState.DebugRunning or IdeState.Paused => StatusBarState.Debugging,
        _ => StatusBarState.Idle,
    };

    private string ProjectName => _projects?.Project?.Name ?? "";

    /// <summary>L3: the transient or sticky message, empty when there is none.</summary>
    public string Message => _status.Message ?? "";

    /// <summary>UI-WIN-040: R2–R8 are shown while a text editor view is the active document view.</summary>
    public bool HasEditor => _status.Editor is not null;

    public string CaretText => Editor is { } e ? Format(Strings.StatusBar_CaretPosition, e.Line, e.Column) : "";

    /// <summary>R3 is hidden while the selection is empty.</summary>
    public bool HasSelection => Editor is { SelectedChars: > 0 };

    public string SelectionText => Editor switch
    {
        { SelectedChars: > 0, SelectedLines: > 1 } e => Format(Strings.StatusBar_SelectionLines, e.SelectedChars, e.SelectedLines),
        { SelectedChars: > 0 } e => Format(Strings.StatusBar_Selection, e.SelectedChars),
        _ => "",
    };

    public string InsertModeText => Editor is { Overwrite: true } ? Strings.StatusBar_Overwrite : Strings.StatusBar_Insert;

    public string IndentText => Editor is { } e
        ? Format(e.InsertSpaces ? Strings.StatusBar_IndentSpaces : Strings.StatusBar_IndentTabs, e.IndentSize)
        : "";

    public string LineEndingText => Editor?.LineEnding switch
    {
        LineEndingKind.Lf => Strings.StatusBar_LineEnding_Lf,
        LineEndingKind.Mixed => Strings.StatusBar_LineEnding_Mixed,
        LineEndingKind.CrLf => Strings.StatusBar_LineEnding_CrLf,
        _ => "",
    };

    /// <summary>R7: the encoding's short name (<c>StatusBar_Encoding_&lt;id&gt;</c>); an unknown id is shown as it is.</summary>
    public string EncodingText => Editor is { } e
        ? Strings.ResourceManager.GetString("StatusBar_Encoding_" + e.EncodingId.Replace('-', '_'), CultureInfo.CurrentCulture) ?? e.EncodingId
        : "";

    /// <summary>UI-DSG-130: the Design view shows <c>Form</c>.</summary>
    public string LanguageText => HasDesigner ? Strings.Language_Form : Editor?.Language ?? "";

    /// <summary>UI-DSG-130: <c>StatusBar.DesignerSelection</c> replaces the caret and selection fields while a Design view is active.</summary>
    public bool HasDesigner => _status.DesignerSelection is not null;

    public string DesignerSelectionText => _status.DesignerSelection ?? "";

    /// <summary>L1 click: <c>view.messages</c>.</summary>
    public IdeCommand StateCommand { get; }

    /// <summary>R1 (main-window.md §5): <c>⊗ &lt;errors&gt; ⚠ &lt;warnings&gt;</c> of the Messages model.</summary>
    public string DiagnosticsText => _messages is null ? "" : Format("⊗ {0} ⚠ {1}", _messages.ErrorCount, _messages.WarningCount);

    /// <summary>R1 is hidden when both counts are 0.</summary>
    public bool HasDiagnostics => _messages is { } m && (m.ErrorCount > 0 || m.WarningCount > 0);

    /// <summary>R9: <c>&lt;configuration&gt; | &lt;platform&gt;</c>; hidden without a project.</summary>
    public string ConfigurationText => _projects?.Project is { } p
        ? p.ActiveConfiguration + " | " + Projects.PlatformLabels.Label(p.ActivePlatform, _projects.PlatformCatalog)
        : "";

    public bool HasProject => _projects?.Project is not null;

    /// <summary>R9 click: a menu of the configurations (<c>project.setConfiguration</c>).</summary>
    public IdeCommand ConfigurationCommand { get; }

    /// <summary>The configurations of the open project (the R9 menu).</summary>
    public IReadOnlyList<string> Configurations => _projects?.Project?.Configurations ?? [];

    /// <summary>R2 click: <c>search.goToLine</c>.</summary>
    public IdeCommand CaretCommand { get; }

    /// <summary>R4 click: <c>edit.toggleOverwrite</c>.</summary>
    public IdeCommand InsertModeCommand { get; }

    /// <summary>R5 click: Options at the page Editor › General.</summary>
    public IdeCommand IndentCommand { get; }

    /// <summary>R6 menu: <c>edit.convertLineEndings</c> with <see cref="LineEndingArguments"/>.</summary>
    public IdeCommand LineEndingCommand { get; }

    /// <summary>R6 menu entries: (argument, label with <c>&amp;</c> mnemonic) = the Edit menu's labels.</summary>
    public static IReadOnlyList<(string Argument, string Label)> LineEndingArguments { get; } =
    [
        ("crlf", Strings.Menu_edit_convertLineEndings_crlf),
        ("lf", Strings.Menu_edit_convertLineEndings_lf),
    ];

    /// <summary>R7 "Save with Encoding" submenu: <c>edit.setEncoding</c> with <see cref="EncodingArguments"/>.</summary>
    public IdeCommand EncodingCommand { get; }

    /// <summary>R7 submenu entries: (encoding id, display name).</summary>
    public static IReadOnlyList<(string Argument, string Label)> EncodingArguments { get; } =
        [.. Enum.GetValues<FileEncoding>().Select(e => (FileEncodings.ToId(e), FileEncodings.DisplayName(e)))];

    /// <summary>The active editor's encoding id (the bullet in the R7 submenu); null without an editor.</summary>
    public string? EncodingId => Editor?.EncodingId;

    /// <summary>R7 "Reopen with Encoding…": possible for the active text document with a file.</summary>
    public bool CanReopenWithEncoding => _documents?.ActiveDocument is TextDocumentViewModel { FilePath: not null };

    /// <summary>R7 "Reopen with Encoding…" (dialogs.md section 18) for the active document.</summary>
    public Task ReopenWithEncodingAsync() =>
        CanReopenWithEncoding ? _documents!.ReopenWithEncodingAsync(_documents.ActiveDocument!) : Task.CompletedTask;

    private EditorStatus? Editor => _status.Editor;

    /// <summary>A field was clicked or activated with the keyboard: its command runs when it can.</summary>
    public static void Activate(IdeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        // R5 opens Options at Editor › General (main-window.md §5).
        var argument = command.Id == "tools.options" ? Dialogs.OptionsViewModel.EditorGeneralPage : null;
        if (command.CanExecute(argument))
        {
            command.Execute(argument);
        }
    }

    /// <summary>Registers <c>view.statusBar</c> (UI-WIN-044), a toggle of <c>appearance.showStatusBar</c>.</summary>
    public static void RegisterCommands(ICommandService commands, ISettingsService settings)
    {
        commands.RegisterHandler("view.statusBar", new ToggleHandler(settings));
        settings.Changed += (_, e) =>
        {
            if (e.Key == SettingsCatalog.AppearanceShowStatusBar.Name)
            {
                commands.InvalidateCanExecute(); // moves the check mark
            }
        };
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private sealed class ToggleHandler(ISettingsService settings) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument)
        {
            settings.Set(SettingsCatalog.AppearanceShowStatusBar, !settings.Get(SettingsCatalog.AppearanceShowStatusBar));
            return Task.CompletedTask;
        }

        public bool IsChecked(string? argument) => settings.Get(SettingsCatalog.AppearanceShowStatusBar);
    }
}
