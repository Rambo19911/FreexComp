using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Services;

/// <summary>
/// The exit sequence (main-window.md UI-WIN-072) and unhandled-exception handling (IDE-ARCH-091); the single place that
/// ends the IDE, through the host's <see cref="Shutdown"/>.
/// </summary>
public interface IApplicationService
{
    /// <summary>Closes the main window for good (set by the host; null until the window exists).</summary>
    Action? Shutdown { get; set; }

    /// <summary>True from an accepted exit until the process ends.</summary>
    bool IsExiting { get; }

    /// <summary>
    /// UI-WIN-072: (1) the running-program prompt (Phase 3), (2) the unsaved-changes prompt, (3) session, settings and
    /// key mappings written, this instance's recovery folder deleted, (4) <see cref="Shutdown"/>. False when canceled
    /// (nothing is saved and the IDE keeps running) or while another exit is in progress.
    /// </summary>
    Task<bool> ExitAsync();

    /// <summary>An exception that reached the UI dispatcher: logged, <c>Msg.App.InternalError</c> (repeat rule of IDE-ARCH-045).</summary>
    Task ReportUnhandledAsync(Exception exception);

    /// <summary>An unobserved background-task exception: logged only.</summary>
    void ReportUnobserved(Exception exception);

    /// <summary>A fatal process error (any thread): logged, recovery copies of all modified documents written synchronously.</summary>
    void ReportFatal(Exception exception);
}

/// <inheritdoc />
public sealed class ApplicationService : IApplicationService
{
    private const string LogCategory = "app";
    private static readonly TimeSpan RepeatedErrorWindow = TimeSpan.FromSeconds(10);

    private readonly IDocumentService _documents;
    private readonly IProjectService _projects;
    private readonly ISessionService _session;
    private readonly IRecoveryService _recovery;
    private readonly ISettingsService _settings;
    private readonly IKeymapService _keymap;
    private readonly IDialogService _dialogs;
    private readonly IClock _clock;
    private readonly ILog _log;
    private (string Text, DateTimeOffset At)? _lastError;
    private bool _prompting;

    public ApplicationService(IDocumentService documents, IProjectService projects, ISessionService session, IRecoveryService recovery,
        ISettingsService settings, IKeymapService keymap, IDialogService dialogs, IClock clock, ILog log)
    {
        _documents = documents;
        _projects = projects;
        _session = session;
        _recovery = recovery;
        _settings = settings;
        _keymap = keymap;
        _dialogs = dialogs;
        _clock = clock;
        _log = log;
    }

    public Action? Shutdown { get; set; }

    public bool IsExiting { get; private set; }

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("file.exit", new Handler(_ => !IsExiting, _ => ExitAsync()));
    }

    public async Task<bool> ExitAsync()
    {
        if (IsExiting || _prompting)
            return false;

        // (2) One prompt for every modified document: the project file first, then the others in tab order (UI-DLG-063).
        // The project is not closed (ProjectService.CloseAsync would clear its reopen flag, IDE-P1-016).
        var projectFile = _projects.Project?.FilePath;
        bool IsProjectFile(DocumentViewModel d) => projectFile is not null && string.Equals(d.FilePath, projectFile, StringComparison.OrdinalIgnoreCase);
        var documents = _documents.Documents.ToList();
        var ordered = documents.Where(IsProjectFile).Concat(documents.Where(d => !IsProjectFile(d))).ToList();
        _prompting = true;
        try
        {
            if (!await _documents.PromptSaveChangesAsync(ordered))
                return false;
        }
        finally
        {
            _prompting = false;
        }

        // (3) The session of the open project, else the loose-file session (IDE-ARCH-071 (b), IDE-ARCH-075); the UI state
        // is written by the main window as it closes.
        IsExiting = true;
        _session.Save();
        await _settings.FlushAsync();
        await _keymap.FlushAsync();
        await _recovery.DeleteInstanceDirectoryAsync(); // IDE-P1-122

        // (4)
        _log.Info(LogCategory, "Exit");
        Shutdown?.Invoke();
        return true;
    }

    public async Task ReportUnhandledAsync(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var text = exception.GetType().FullName + ": " + exception.Message;
        _log.Error(LogCategory, "Unhandled exception on the UI thread: " + text, exception);
        var now = _clock.Now;
        if (_lastError is { } last && last.Text == text && now - last.At < RepeatedErrorWindow)
            return; // IDE-ARCH-045: identical consecutive errors within 10 s only log

        _lastError = (text, now);
        await _dialogs.ShowMessageAsync(new MessageRequest("Msg.App.InternalError", [], exception.ToString()));
    }

    public void ReportUnobserved(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _log.Error(LogCategory, "Unobserved background task exception", exception);
    }

    public void ReportFatal(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            _log.Error(LogCategory, "Fatal error; writing recovery copies", exception);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // nothing else could report it; the copies matter more
        }

        _recovery.WriteCopiesNow();
    }

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
