using FreexComp.Debugger.Dap;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Session;

/// <summary>
/// Which routine a stack frame of the adapter runs and where its variables are. The code of a finally part is a
/// subprogram of its own that the routine calls - directly, or from its clean-up code while an exception unwinds the
/// stack: a frame in it sees the variables of the routine (those declared in the part are listed there, by line),
/// which are read from the nearest frame of that routine further down the stack.
/// </summary>
internal static class FrameRoutines
{
    /// <summary>How many frames are asked for at a time when the routine's frame is not on the page.</summary>
    public const int FetchCount = 20;

    /// <summary>No frame is looked for further down than this many frames past the page.</summary>
    public const int FetchLimit = 1000;

    /// <summary>The source position of an adapter frame; null when it has no source file (DBG-074).</summary>
    public static SourceLocation? LocationOf(DapStackFrame frame) =>
        frame.Source is { Path: { Length: > 0 } path } source && (source.SourceReference ?? 0) == 0
        && Path.IsPathFullyQualified(path) && !path.Contains('`') && frame.Line > 0
            ? new SourceLocation(PathText.Normalize(path), frame.Line, Math.Max(frame.Column, 1))
            : null;

    /// <summary>DBG-134, with the adapter's name of the frame deciding on the lines of an outlined finally part.</summary>
    public static PdiRoutine? RoutineOf(PdiFile? typeInfo, DapStackFrame frame) =>
        typeInfo is not null && LocationOf(frame) is { } location ? typeInfo.FindRoutine(location.Path, location.Line, frame.Name) : null;

    /// <summary>
    /// The routine whose variables frame <paramref name="index"/> of <paramref name="page"/> sees, and the adapter's
    /// id of the frame that holds them. For a frame in an outlined finally part that is the part's routine and the
    /// nearest frame of it further down: on the page, else among the frames after the page, which
    /// <paramref name="fetchAfterPage"/> returns <see cref="FetchCount"/> at a time (argument: how many frames past
    /// the end of the page to start at; an empty answer ends the stack; null: the page ends the stack). A part whose
    /// routine has no frame keeps its own routine, which lists no variables, and its own frame.
    /// </summary>
    public static async Task<(PdiRoutine? Routine, int VariablesFrameId)> VariablesOfAsync(
        PdiFile? typeInfo, IReadOnlyList<DapStackFrame> page, int index, Func<int, Task<IReadOnlyList<DapStackFrame>>>? fetchAfterPage)
    {
        var routine = RoutineOf(typeInfo, page[index]);
        if (routine is null || typeInfo!.VariableOwner(routine) is not { } owner || ReferenceEquals(owner, routine))
            return (routine, page[index].Id);

        for (var below = index + 1; below < page.Count; below++)
        {
            if (ReferenceEquals(RoutineOf(typeInfo, page[below]), owner))
                return (owner, page[below].Id);
        }

        for (var past = 0; fetchAfterPage is not null && past < FetchLimit;)
        {
            var further = await fetchAfterPage(past).ConfigureAwait(false);
            if (further.Count == 0)
                break;
            foreach (var frame in further)
            {
                if (ReferenceEquals(RoutineOf(typeInfo, frame), owner))
                    return (owner, frame.Id);
            }

            past += further.Count;
        }

        return (routine, page[index].Id);
    }
}
