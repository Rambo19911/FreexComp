using System.Globalization;
using FreexComp.Ide.Core.Build;
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
    private static readonly TimeSpan ChordStatusDuration = TimeSpan.FromSeconds(5);
    private static readonly KeyGesture Escape = KeyGesture.Parse("Escape");

    /// <summary>UI-KEY-007: disabled bindings of these keys fall through to the focused control.</summary>
    private static readonly HashSet<string> FallThroughKeys =
        new(["Tab", "Enter", "Escape", "Delete", "Insert", "Up", "Down", "Left", "Right"], StringComparer.Ordinal);

    /// <summary>IDE-ARCH-046: may run again while a previous execution is pending.</summary>
    private static readonly HashSet<string> ReentrantCommands = new(["project.cancelBuild", "run.stop"], StringComparer.Ordinal);

    private readonly IKeymapService _keymap;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClock _clock;
    private readonly ILog _log;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly IFocusTracker _focus;
    private readonly Dictionary<string, CommandDefinition> _definitions;
    private readonly Dictionary<string, ICommandHandler> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IdeCommand> _commands = new(StringComparer.Ordinal);
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);

    /// <summary>The phase the later-phase stubs were registered for (the start-up check of IDE-P8-004 uses it).</summary>
    private int _stubPhase;
    private bool _invalidatePending;
    private (string Text, DateTimeOffset At)? _lastError;

    /// <summary>UI-KEY-010: the first key of a started chord, and its waiting text in the status bar.</summary>
    private KeyGesture? _chordPrefix;
    private IDisposable? _chordStatus;

    public CommandService(IKeymapService keymap, IUiDispatcher dispatcher, IClock clock, ILog log, IDialogService dialogs, IStatusService status, IFocusTracker focus)
    {
        _focus = focus;
        _keymap = keymap;
        _dispatcher = dispatcher;
        _clock = clock;
        _log = log;
        _dialogs = dialogs;
        _status = status;
        Definitions = CommandCatalog.All;
        _definitions = Definitions.ToDictionary(d => d.Id, StringComparer.Ordinal);
        _keymap.Changed += (_, _) => RefreshGestureTexts(); // UI-KEY-023
        _focus.Changed += (_, _) =>
        {
            CancelChord(); // UI-KEY-011: the second key belongs to where the first was pressed
            InvalidateCanExecute(); // Focus commands follow the focused element
        };
    }

    public event EventHandler<CommandExecutedEventArgs>? Executed;

    public IReadOnlyList<CommandDefinition> Definitions { get; }

    /// <summary>
    /// IDE-P3-003: the IDE state that rules commands out on top of their handlers (<see cref="IdeCommandAvailability"/>);
    /// null = no state gate. A state change re-queries every command.
    /// </summary>
    public IIdeState? IdeState
    {
        get => _ideState;
        init
        {
            _ideState = value;
            _ideState?.Changed += (_, _) => InvalidateCanExecute();
        }
    }

    private readonly IIdeState? _ideState;

    private bool IsBlockedByState(string commandId) =>
        _ideState is { } state && !IdeCommandAvailability.IsAvailable(commandId, state.Current.State);

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
        _stubPhase = implementedPhase;
        foreach (var definition in Definitions.Where(d => !Composition.Phase8Features.IsAvailable(d.Id, d.Phase, implementedPhase) && !_handlers.ContainsKey(d.Id)))
        {
            _handlers[definition.Id] = StubHandler.Instance;
        }
    }

    public bool IsHandled(string commandId) => _handlers.ContainsKey(commandId);

    public void VerifyAllHandled()
    {
        var missing = Definitions.Where(d => !_handlers.ContainsKey(d.Id)).Select(d => d.Id).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException("Commands without a handler: " + string.Join(", ", missing));
        }

        // IDE-P8-004: a command treated as implemented (a delivered phase-8 one) must not sit on the later-phase stub.
        var stubbed = Definitions
            .Where(d => _handlers[d.Id] is StubHandler && Composition.Phase8Features.IsAvailable(d.Id, d.Phase, _stubPhase))
            .Select(d => d.Id).ToList();
        if (stubbed.Count > 0)
        {
            throw new InvalidOperationException("Delivered commands on the later-phase stub: " + string.Join(", ", stubbed));
        }
    }

    public IdeCommand Get(string commandId)
    {
        if (!_commands.TryGetValue(commandId, out var command))
        {
            var definition = _definitions[commandId];
            var handler = _handlers.GetValueOrDefault(commandId);
            command = new IdeCommand(this, definition, handler?.TitleOverride ?? TitleOf(definition), GestureText(commandId))
            {
                MenuLabel = handler?.MenuLabelOverride,
                IsChecked = handler?.IsChecked(null) ?? false,
            };
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

        return !IsBlockedByState(commandId) && handler.CanExecute(argument);
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

    public bool IsChordPending => _chordPrefix is not null;

    public void CancelChord()
    {
        _chordPrefix = null;
        _chordStatus?.Dispose(); // takes the waiting text away unless a newer message replaced it
        _chordStatus = null;
    }

    public bool HandleKey(KeyGesture gesture)
    {
        var state = _focus.Current;
        if (_chordPrefix is null
            && state.IsTextInput
            && _keymap.FindCommand(gesture, CommandScope.Global, null) is { } id
            && _definitions[id].Scope == CommandScope.Focus)
        {
            return false; // IDE-ARCH-042: edit.* stay with the text input
        }

        return HandleKey(gesture, state.Scope, state.ToolWindowName);
    }

    public bool HandleKey(KeyGesture gesture, CommandScope focusScope, string? toolWindowName)
    {
        if (_chordPrefix is { } prefix)
        {
            return CompleteChord(prefix, gesture, focusScope, toolWindowName);
        }

        if (_focus.LocalKeys.Contains(gesture))
        {
            return false; // IDE-ARCH-043: a popup of the focused control handles this key
        }

        if (_keymap.IsChordPrefix(gesture, focusScope, toolWindowName))
        {
            // UI-KEY-010: the first key of a chord does nothing else; the status bar says that the IDE waits.
            _chordPrefix = gesture;
            _chordStatus = _status.ShowStickyMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Status_ChordWaiting, gesture));
            return true;
        }

        var binding = _keymap.FindBinding(gesture, focusScope, toolWindowName);
        return binding is not null && _handlers.TryGetValue(binding.CommandId, out var handler) && Run(binding, handler);
    }

    /// <summary>
    /// UI-KEY-010 / UI-KEY-011: the key after a chord's first key. It is never passed on: it runs the chord's command, or
    /// ends the wait — silently for Escape, with <c>Msg.Status.ChordNotAShortcut</c> when no chord ends with it.
    /// </summary>
    private bool CompleteChord(KeyGesture prefix, KeyGesture second, CommandScope focusScope, string? toolWindowName)
    {
        CancelChord();
        if (second == Escape || second.IsChord)
        {
            return true;
        }

        var chord = KeyGesture.Chord(prefix, second);
        if (_keymap.FindBinding(chord, focusScope, toolWindowName) is { } binding && _handlers.TryGetValue(binding.CommandId, out var handler))
        {
            Run(binding, handler);
        }
        else
        {
            _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Status_ChordNotAShortcut, chord), ChordStatusDuration);
        }

        return true;
    }

    /// <summary>Executes the binding's command; false when a disabled key falls through to the focused control (UI-KEY-007).</summary>
    private bool Run(KeyBinding binding, ICommandHandler handler)
    {
        var commandId = binding.CommandId;
        var gesture = binding.Gesture;
        if (CanExecute(commandId, binding.Argument))
        {
            _ = ExecuteAsync(commandId, binding.Argument);
            return true;
        }

        if (!gesture.IsChord && FallThroughKeys.Contains(gesture.Key)
            && (gesture.Modifiers == KeyModifiers.None || gesture is { Key: "Tab", Modifiers: KeyModifiers.Shift }))
        {
            return false; // UI-KEY-007 exceptions
        }

        if (IsBlockedByState(commandId) || handler.IsBlockedByIdeState(binding.Argument))
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
                    command.MenuLabel = handler?.MenuLabelOverride;
                    command.IsChecked = handler?.IsChecked(null) ?? false;
                    command.RaiseCanExecuteChanged();
                }
            },
            UiPriority.Background);
    }

    private void RefreshGestureTexts()
    {
        foreach (var command in _commands.Values)
        {
            command.GestureText = GestureText(command.Id);
        }
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
