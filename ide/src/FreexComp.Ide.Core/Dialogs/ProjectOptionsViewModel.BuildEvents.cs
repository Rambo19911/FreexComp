using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Build;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>The two command lists of the page <c>Build Events</c>.</summary>
public enum EventsField
{
    PreBuild,
    PostBuild,
}

/// <summary>An item of the <c>Insert Macro</c> menu (UI-DLG-302): the macro as written in a command and what it stands for.</summary>
public sealed record EventMacroItem(string Name, string Text, string Description);

/// <summary>
/// The page <c>Build Events</c> (phase8 build-events.md §3, IDE-EVT-010…020): one command per line, the project-level lists
/// under <c>All configurations</c>, the own lists of a configuration or — read-only — the lists it is built with. The dialog
/// edits the project file only; it never reads or writes the trust list (IDE-EVT-018).
/// </summary>
public sealed partial class ProjectOptionsViewModel
{
    private static readonly string[] LineBreaks = ["\r\n", "\n", "\r"];

    /// <summary>The engine's macro names with a value each: what <see cref="EventMacros.Expand"/> does not know is unknown.</summary>
    private static readonly ImmutableDictionary<string, string> KnownMacros =
        EventMacros.Names.ToImmutableDictionary(n => n, _ => "", StringComparer.OrdinalIgnoreCase);

    private EventsState _projectEvents = new(new ProjectEvents([], []));
    private EventsField _lastFocusedEventsField = EventsField.PreBuild;

    /// <summary><c>OwnEvents</c> and <c>Inherited</c> exist only while a configuration is selected (UI-DLG-301).</summary>
    public bool ShowOwnEvents => !SearchPathsAllConfigurations;

    /// <summary>
    /// <c>OwnEvents</c> (IDE-EVT-014): the selected configuration has an <c>events</c> key. Checking creates it with the
    /// commands that were shown; unchecking removes it.
    /// </summary>
    public bool OwnEvents
    {
        get => ShowOwnEvents && Selected.Events is not null;
        set
        {
            if (!ShowOwnEvents || value == (Selected.Events is not null))
                return;
            if (value)
            {
                var shown = ProjectOptions.GetEffectiveEvents(Build(), SelectedConfiguration);
                Replace(Selected with { Events = new EventsState(new ProjectEvents(shown.PreBuild, shown.PostBuild) { PostBuildWhen = shown.PostBuildWhen }) });
            }
            else
            {
                Replace(Selected with { Events = null });
            }
        }
    }

    /// <summary><c>Msg.ProjectOptions.EventsInherited</c> while a configuration without its own commands is shown; else null.</summary>
    public string? EventsInheritedText => EventsInherited
        ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_EventsInherited, EventsSource().Name ?? Strings.ProjectOptions_AllConfigurations)
        : null;

    /// <summary>The <c>Before build</c> list, one command per line (IDE-EVT-012).</summary>
    public string PreBuild
    {
        get => EventsText(EventsField.PreBuild);
        set => SetEventsText(EventsField.PreBuild, value);
    }

    /// <summary>The <c>After build</c> list, one command per line.</summary>
    public string PostBuild
    {
        get => EventsText(EventsField.PostBuild);
        set => SetEventsText(EventsField.PostBuild, value);
    }

    public bool PreBuildReadOnly => EventsReadOnly(EventsField.PreBuild);

    public bool PostBuildReadOnly => EventsReadOnly(EventsField.PostBuild);

    /// <summary><c>Msg.ProjectOptions.EventsUnknownMacro</c> for the first unknown reference of the shown, editable list (IDE-EVT-016).</summary>
    public string? PreBuildError => EventsError(EventsField.PreBuild);

    public string? PostBuildError => EventsError(EventsField.PostBuild);

    /// <summary><c>Msg.ProjectOptions.EventsControlCharacter</c> when the shown list holds a command with a control character (IDE-EVT-017).</summary>
    public string? PreBuildWarning => EventsWarning(EventsField.PreBuild);

    public string? PostBuildWarning => EventsWarning(EventsField.PostBuild);

    /// <summary><c>PostBuildWhen</c> ↔ <c>postBuildWhen</c> (IDE-EVT-013) of the shown level.</summary>
    public PostBuildWhen PostBuildWhen
    {
        get => EventsSource().State.Value.PostBuildWhen;
        set
        {
            if (EventsInherited || value == PostBuildWhen)
                return;
            var state = EventsSource().State;
            StoreEvents(state with { Stored = state.Stored with { PostBuildWhen = value } });
        }
    }

    public bool PostBuildWhenEnabled => !EventsInherited;

    /// <summary>The items of <c>InsertMacro</c>: the engine's macros in the engine's order (IDE-EVT-015).</summary>
    public IReadOnlyList<EventMacroItem> EventMacroItems { get; } =
    [
        .. EventMacros.Names.Select(n => new EventMacroItem(n, "$(" + n + ")",
            Strings.ResourceManager.GetString("ProjectOptions_Macro_" + n, CultureInfo.CurrentUICulture) ?? "")),
    ];

    /// <summary>The list field that had the keyboard focus last; the target of <see cref="InsertMacro"/>.</summary>
    public EventsField LastFocusedEventsField
    {
        get => _lastFocusedEventsField;
        set
        {
            if (SetProperty(ref _lastFocusedEventsField, value))
                OnPropertyChanged(nameof(CanInsertMacro));
        }
    }

    /// <summary><c>InsertMacro</c> is disabled while its target is read-only.</summary>
    public bool CanInsertMacro => !EventsReadOnly(LastFocusedEventsField);

    /// <summary>
    /// Inserts <c>$(name)</c> into the list that had the focus last, replacing the text between the two positions (the
    /// caret when they are equal). Returns the position after the inserted text; null when nothing was inserted.
    /// </summary>
    public int? InsertMacro(string name, int selectionStart, int selectionEnd)
    {
        var item = EventMacroItems.FirstOrDefault(m => m.Name == name);
        if (item is null || !CanInsertMacro)
            return null;
        var text = EventsText(LastFocusedEventsField);
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
        SetEventsText(LastFocusedEventsField, text[..start] + item.Text + text[end..]);
        return start + item.Text.Length;
    }

    /// <summary>A configuration is selected and has no commands of its own: the fields show what it is built with.</summary>
    private bool EventsInherited => ShowOwnEvents && Selected.Events is null;

    /// <summary>
    /// The level whose lists are shown and, for the inherited view, where they come from: the nearest ancestor that has its
    /// own (its name), else the project (null) — the walk of <see cref="ProjectOptions.GetEffectiveEvents"/> on the working copy.
    /// </summary>
    private (EventsState State, string? Name) EventsSource()
    {
        if (!ShowOwnEvents)
            return (_projectEvents, null);
        var current = Selected;
        for (var steps = 0; steps <= _configs.Count; steps++)
        {
            if (current.Events is { } own)
                return (own, current.Name);
            if (current.Inherits is not { } parent || _configs.FirstOrDefault(c => c.Name == parent) is not { } next)
                break;
            current = next;
        }

        return (_projectEvents, null);
    }

    private string EventsText(EventsField field)
    {
        var state = EventsSource().State;
        if (state.Text(field) is { } typed && !EventsInherited)
            return typed;
        var commands = state.Value.List(field);
        return string.Join(Environment.NewLine, HasControlCharacter(commands) ? commands.Select(WithGlyphs) : commands);
    }

    private bool EventsReadOnly(EventsField field) => EventsInherited || EventsSource().State.IsProtected(field);

    private string? EventsError(EventsField field) => EventsReadOnly(field) ? null : UnknownMacro(EventsSource().State, field);

    private string? EventsWarning(EventsField field) =>
        EventsReadOnly(field) && HasControlCharacter(EventsSource().State.Value.List(field)) ? Strings.Msg_ProjectOptions_EventsControlCharacter : null;

    private void SetEventsText(EventsField field, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (EventsReadOnly(field) || value == EventsText(field))
            return;
        var state = EventsSource().State;
        StoreEvents(field == EventsField.PreBuild ? state with { PreText = value } : state with { PostText = value });
    }

    /// <summary>Stores the edited state of the shown level: the project or the selected configuration.</summary>
    private void StoreEvents(EventsState state)
    {
        if (ShowOwnEvents)
        {
            Replace(Selected with { Events = state });
        }
        else
        {
            _projectEvents = state;
            OnEventsChanged();
            MarkChanged();
        }
    }

    private void OnEventsChanged()
    {
        foreach (var name in (string[])[nameof(ShowOwnEvents), nameof(OwnEvents), nameof(EventsInheritedText), nameof(PreBuild), nameof(PostBuild),
                     nameof(PreBuildReadOnly), nameof(PostBuildReadOnly), nameof(PreBuildError), nameof(PostBuildError), nameof(PreBuildWarning),
                     nameof(PostBuildWarning), nameof(PostBuildWhen), nameof(PostBuildWhenEnabled), nameof(CanInsertMacro)])
            OnPropertyChanged(name);
    }

    /// <summary>UI-DLG-086: some editable list of the working copy refers to an unknown macro.</summary>
    private bool EventsHaveError() =>
        _configs.Select(c => c.Events).Append(_projectEvents).OfType<EventsState>()
            .Any(s => UnknownMacro(s, EventsField.PreBuild) is not null || UnknownMacro(s, EventsField.PostBuild) is not null);

    /// <summary>
    /// IDE-EVT-016: the message for the first reference to an unknown macro in the list, by the engine's own rule
    /// (<see cref="EventMacros.Expand"/>); a list that cannot be edited here is not checked.
    /// </summary>
    private static string? UnknownMacro(EventsState state, EventsField field)
    {
        if (state.IsProtected(field))
            return null;
        foreach (var command in state.Value.List(field))
        {
            if (EventMacros.Expand(command, KnownMacros).UnknownNames is [var unknown, ..])
                return string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_EventsUnknownMacro, "$(" + unknown + ")");
        }

        return null;
    }

    /// <summary>CMP-1259: a character below U+0020 other than tab, or U+007F.</summary>
    private static bool HasControlCharacter(ImmutableArray<string> commands) =>
        commands.Any(command => command.Any(c => (c < ' ' && c != '\t') || c == '\u007F'));

    /// <summary>UI-DLG-303: tab as <c>→</c>, a line break as <c>↵</c> (one glyph for CR LF), any other control character as <c>·</c>.</summary>
    private static string WithGlyphs(string command)
    {
        var text = new StringBuilder(command.Length);
        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (c == '\r' && i + 1 < command.Length && command[i + 1] == '\n')
                continue;
            text.Append(c switch
            {
                '\t' => '→',
                '\n' or '\r' => '↵',
                < ' ' or '\u007F' => '·',
                _ => c,
            });
        }

        return text.ToString();
    }

    private static ImmutableArray<string> SplitCommands(string text) =>
        [.. text.Split(LineBreaks, StringSplitOptions.None).Where(line => !string.IsNullOrWhiteSpace(line))];

    /// <summary>
    /// The edited state of one <c>events</c> value: the stored one and, per list, the text typed for it — null while the
    /// user did not change the field, so that the stored list stays as it is, entry for entry (IDE-EVT-012).
    /// </summary>
    private sealed record EventsState(ProjectEvents Stored, string? PreText = null, string? PostText = null)
    {
        /// <summary>The value as it would be written: an edited list is its text split at line breaks, blank lines dropped.</summary>
        public ProjectEvents Value => PreText is null && PostText is null
            ? Stored
            : Stored with
            {
                PreBuild = PreText is null ? Stored.PreBuild : SplitCommands(PreText),
                PostBuild = PostText is null ? Stored.PostBuild : SplitCommands(PostText),
            };

        public string? Text(EventsField field) => field == EventsField.PreBuild ? PreText : PostText;

        /// <summary>IDE-EVT-017: the stored list holds a control character; it is shown read-only and never rewritten.</summary>
        public bool IsProtected(EventsField field) => Text(field) is null && HasControlCharacter(Stored.List(field));
    }
}

/// <summary>Access to the two lists of <see cref="ProjectEvents"/> by <see cref="EventsField"/>.</summary>
internal static class ProjectEventsLists
{
    public static ImmutableArray<string> List(this ProjectEvents events, EventsField field)
    {
        var list = field == EventsField.PreBuild ? events.PreBuild : events.PostBuild;
        return list.IsDefault ? [] : list;
    }
}
