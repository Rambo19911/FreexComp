using FreexComp.Build;

namespace FreexComp.Ide.Core.Build;

/// <summary>
/// The project a start acts on (group-member-run.md IDE-PM-193): the loaded project, or a listed member of the project
/// group with its own values. Every step of a start — runnable check, build request, previous executable, host
/// application, arguments, working directory, console mode — reads this value, never the loaded project.
/// </summary>
/// <param name="ProjectPath">Full path of the <c>.pproj</c>.</param>
/// <param name="Name">The project name.</param>
/// <param name="Directory">The folder of the project file; relative run parameters are resolved against it.</param>
/// <param name="AppType"><c>console</c>, <c>gui</c>, <c>library</c> or <c>package</c>.</param>
/// <param name="Configuration">The target's active configuration.</param>
/// <param name="Platform">The target's effective platform, the id.</param>
/// <param name="Definition">The project as read by <c>FreexComp.Build</c> (run parameters, configurations).</param>
/// <param name="IsListedMember">A listed member of the group; false = the loaded project.</param>
public sealed record RunTarget(string ProjectPath, string Name, string Directory, string AppType, string Configuration, string Platform,
    Project? Definition, bool IsListedMember)
{
    /// <summary>The target's run parameters (<c>run.arguments</c>, <c>run.workingDir</c>, <c>run.hostApplication</c>).</summary>
    public RunSettings Run => Definition?.Run ?? new RunSettings("", "");

    /// <summary>
    /// IDE-TGT-013: the host application of a library — <c>run.hostApplication</c> resolved against the target's
    /// directory; null when the target is no library or names none.
    /// </summary>
    public string? HostApplication => AppType == "library" && Definition is { } definition ? ProjectOptions.GetHostApplicationPath(definition) : null;

    /// <summary>IDE-P3-080, IDE-TGT-013: a console or gui program, or a library that names a host application.</summary>
    public bool IsRunnable => AppType is "console" or "gui" || HostApplication is not null;
}
