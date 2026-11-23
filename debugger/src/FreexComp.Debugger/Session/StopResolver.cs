namespace FreexComp.Debugger.Session;

/// <summary>The step the user asked for and that is still in progress (kept across internal stops).</summary>
/// <param name="command">The adapter command of the step: <c>next</c>, <c>stepIn</c> or <c>stepOut</c>.</param>
/// <param name="threadId">The stepping thread.</param>
/// <param name="depth">Frames on the stepping thread's stack when the step started; 0 when unknown.</param>
internal sealed class StepState(string command, int threadId, int depth)
{
    public string Command { get; } = command;
    public int ThreadId { get; } = threadId;
    public int Depth { get; } = depth;

    /// <summary>Internal steps the user-code filter has issued for this user command (features DBG-222).</summary>
    public int InternalSteps { get; set; }

    /// <summary>The filter stepped out of non-user code after a Step Into and still owes the step in.</summary>
    public bool StepInPending { get; set; }
}

/// <summary>How the session resumes after a stop it resolved itself (architecture §7).</summary>
internal static class StopResolver
{
    public const string Continue = "continue";

    /// <summary>
    /// DBG-030: after a skipped breakpoint the same step kind is issued again on the stepping thread when the stop is
    /// on that thread and its stack is at least as deep as where the step started; otherwise the debuggee continues.
    /// </summary>
    public static (string Command, int ThreadId) ResumeAfterSkip(StepState? step, int threadId, int depth) =>
        step is not null && step.ThreadId == threadId && depth >= step.Depth
            ? (step.Command, step.ThreadId)
            : (Continue, threadId);
}
