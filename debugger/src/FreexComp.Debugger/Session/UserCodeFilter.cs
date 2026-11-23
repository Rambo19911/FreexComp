namespace FreexComp.Debugger.Session;

/// <summary>
/// The user-code filter for step results (features DBG-222): a step that ends in code that is not the user's does
/// not stop there.
/// </summary>
internal static class UserCodeFilter
{
    /// <summary>Internal steps per user command; beyond them the debuggee just continues.</summary>
    public const int MaxInternalSteps = 8;

    public const string StepIn = "stepIn";
    public const string StepOut = "stepOut";

    /// <summary>
    /// What to do with a step stop: null to publish it, else the internal command. After a Step Into: step out of the
    /// non-user code, then step in again on the same thread (the next call on the same statement can still be
    /// entered); after Step Over and Step Out: step out, repeatedly. When the limit is used up, or no frame on the
    /// stack is user code, the debuggee continues.
    /// </summary>
    /// <param name="step">The user's step; its filter state is updated.</param>
    /// <param name="frames">The stack of the stopping thread, innermost first.</param>
    public static string? Decide(StepState step, IReadOnlyList<StackFrameInfo> frames)
    {
        string command;
        if (frames.Count > 0 && frames[0].IsUserCode)
        {
            if (!step.StepInPending)
                return null;
            step.StepInPending = false;
            command = StepIn;
        }
        else if (!frames.Any(frame => frame.IsUserCode))
        {
            return StopResolver.Continue;
        }
        else
        {
            step.StepInPending = step.Command == StepIn;
            command = StepOut;
        }

        if (step.InternalSteps >= MaxInternalSteps)
            return StopResolver.Continue;
        step.InternalSteps++;
        return command;
    }
}
