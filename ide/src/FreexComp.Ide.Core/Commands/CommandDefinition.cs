namespace FreexComp.Ide.Core.Commands;

/// <summary>Focus scopes of commands.md §1 (<c>Debug</c> is a condition, see <see cref="CommandDefinition.RequiresDebugSession"/>).</summary>
public enum CommandScope
{
    Global,
    Editor,
    Designer,
    ProjectManager,
    ToolList,
    Focus,
}

/// <summary>
/// One command of commands.md (UI-CMD-002). <see cref="Scopes"/> lists every scope of the "Scope" column in order
/// (<c>Editor, ToolList</c>); <see cref="Scope"/> is the first. <c>+Debug</c> sets <see cref="RequiresDebugSession"/>.
/// Default gestures use the notation of UI-CMD-007 (<c>Ctrl+Shift+Alt+Key</c>).
/// </summary>
public sealed record CommandDefinition(
    string Id,
    IReadOnlyList<CommandScope> Scopes,
    int Phase,
    IReadOnlyList<string> DefaultGestures,
    bool IsToggle = false,
    bool TakesArgument = false,
    bool RequiresDebugSession = false)
{
    /// <summary>The <c>category</c> part of the id (UI-CMD-001).</summary>
    public string Category => Id[..Id.IndexOf('.', StringComparison.Ordinal)];

    public CommandScope Scope => Scopes[0];

    /// <summary><c>Strings.resx</c> key of the title (UI-CMD-003).</summary>
    public string TitleKey => "Cmd_" + Id.Replace('.', '_');
}
