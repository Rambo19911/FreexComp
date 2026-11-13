using System.Globalization;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Commands;

/// <summary><see cref="ICommandService"/> over <see cref="CommandCatalog"/> (architecture.md §6).</summary>
public sealed class CommandService : ICommandService
{
    private const string Category = "commands";
    private static readonly TimeSpan UnavailableStatusDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RepeatedErrorWindow = TimeSpan.FromSeconds(10);

    /// <summary>UI-KEY-007: disabled bindings of these keys fall through to the focused control.</summary>
    private static readonly HashSet<string> FallThroughKeys =
        new(["Tab", "Enter", "Escape", "Delete", "Insert", "Up", "Down", "Left", "Right"], StringComparer.Ordinal);

    /// <summary>IDE-ARCH-046: may run again while a previous execution is pending.</summary>
    private static readonly HashSet<string> ReentrantCommands = new(["project.cancelBuild", "run.stop"], StringComparer.Ordinal);

    private readonly Keymap _keymap;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClock _clock;
    private readonly ILog _log;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly Dictionary<string, CommandDefinition> _definitions;
    private readonly Dictionary<string, ICommandHandler> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IdeCommand> _commands = new(StringComparer.Ordinal);
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);
    private bool _invalidatePending;
    private (string Text, DateTimeOffset At)? _lastError;

    public CommandService(Keymap keymap, IUiDispatcher dispatcher, IClock clock, ILog log, IDialogService dialogs, IStatusService status)
    {
        _keymap = keymap;
        _dispatcher = dispatcher;
        _clock = clock;
        _log = log;
        _dialogs = dialogs;
        _status = status;
        Definitions = CommandCatalog.All;
        _definitions = Definitions.ToDictionary(d => d.Id, StringComparer.Ordinal);
    }

    public event EventHandler<CommandExecutedEventArgs>? Executed;

    public IReadOnlyList<CommandDefinition> Definitions { get; }

    public void RegisterHandler(string commandId, ICommandHandler handler)
    {
        if (!_definitions.ContainsKey(commandId))
        {
            throw new InvalidOperationException($"Unknown command id '{commandId}'.");
        }

        if (!_handlers.TryAdd(commandId, handler))
        {
            throw new InvalidOperationException($"Command '{commandId}' already has a handler.");
        }
    }

    public void RegisterLaterPhaseStubs(int implementedPhase)
    {
        foreach (var definition in Definitions.Where(d => d.Phase > implementedPhase && !_handlers.ContainsKey(d.Id)))
        {
            _handlers[definition.Id] = StubHandler.Instance;
        }
    }

    public void VerifyAllHandled()
    {
        var missing = Definitions.Where(d => !_handlers.ContainsKey(d.Id)).Select(d => d.Id).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException("Commands without a handler: " + string.Join(", ", missing));
        }
    }

    public IdeCommand Get(string commandId)
    {
        if (!_commands.TryGetValue(commandId, out var command))
        {
            var definition = _definitions[commandId];
            command = new IdeCommand(this, definition, TitleOf(definition), GestureText(commandId));
            _commands[commandId] = command;
        }

        return command;
    }

    public bool CanExecute(string commandId, string? argument = null)
    {
        if (!_handlers.TryGetValue(commandId, out var handler))
        {
            return false;
        }

        if (_running.Contains(commandId) && !ReentrantCommands.Contains(commandId))
        {
            return false;
        }

        return handler.CanExecute(argument);
    }

    public async Task ExecuteAsync(string commandId, string? argument = null)
    {
        if (!CanExecute(commandId, argument))
        {
            return; // UI-CMD-012: a no-op, never a message
        }

        var handler = _handlers[commandId];
        var started = _clock.Now;
        Exception? failure = null;
        _running.Add(commandId);
        InvalidateCanExecute();
        try
        {
            await handler.ExecuteAsync(argument);
        }
        catch (OperationCanceledException)
        {
            // IDE-ARCH-045: swallowed silently.
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            _running.Remove(commandId);
            InvalidateCanExecute();
        }

        if (failure is not null)
        {
            await ReportFailureAsync(commandId, failure);
        }

        Executed?.Invoke(this, new CommandExecutedEventArgs(commandId, argument, _clock.Now - started, failure));
    }

    public bool HandleKey(KeyGesture gesture, CommandScope focusScope, string? toolWindowName)
    {
        var commandId = _keymap.FindCommand(gesture, focusScope, toolWindowName);
        if (commandId is null || !_handlers.TryGetValue(commandId, out var handler))
        {
            return false;
        }

        if (CanExecute(commandId))
        {
            _ = ExecuteAsync(commandId);
            return true;
        }

        if (FallThroughKeys.Contains(gesture.Key) && (gesture.Modifiers == KeyModifiers.None || gesture is { Key: "Tab", Modifiers: KeyModifiers.Shift }))
        {
            return false; // UI-KEY-007 exceptions
        }

        if (handler.IsBlockedByIdeState(null))
        {
            var title = handler.TitleOverride ?? TitleOf(_definitions[commandId]);
            _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Status_CommandUnavailable, title), UnavailableStatusDuration);
        }

        return true; // consumed even when disabled
    }

    public void InvalidateCanExecute()
    {
        if (_invalidatePending)
        {
            return;
        }

        _invalidatePending = true;
        _dispatcher.Post(
            () =>
            {
                _invalidatePending = false;
                foreach (var command in _commands.Values.ToList())
                {
                    var handler = _handlers.GetValueOrDefault(command.Id);
                    command.Title = handler?.TitleOverride ?? TitleOf(command.Definition);
                    command.IsChecked = handler?.IsChecked(null) ?? false;
                    command.RaiseCanExecuteChanged();
                }
            },
            UiPriority.Background);
    }

    private static string TitleOf(CommandDefinition definition) =>
        Strings.ResourceManager.GetString(definition.TitleKey, CultureInfo.CurrentCulture) ?? definition.Id;

    private string GestureText(string commandId) =>
        _keymap.GetGestures(commandId) is [var first, ..] ? first.ToString() : "";

    /// <summary>IDE-ARCH-045: log every failure; show the dialog unless the same error was shown within 10 s.</summary>
    private async Task ReportFailureAsync(string commandId, Exception ex)
    {
        _log.Error(Category, $"{commandId} failed", ex);
        var text = ex.GetType().FullName + ": " + ex.Message;
        var now = _clock.Now;
        if (_lastError is { } last && last.Text == text && now - last.At < RepeatedErrorWindow)
        {
            return;
        }

        _lastError = (text, now);
        await _dialogs.ShowMessageAsync(new MessageRequest("Msg.App.InternalError", [], ex.ToString()));
    }

    private sealed class StubHandler : ICommandHandler
    {
        public static readonly StubHandler Instance = new();

        public bool CanExecute(string? argument) => false;

        public Task ExecuteAsync(string? argument) => Task.CompletedTask;
    }
}
