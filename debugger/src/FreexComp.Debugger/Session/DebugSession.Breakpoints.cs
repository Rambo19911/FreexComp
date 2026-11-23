using FreexComp.Debugger.Dap;
using FreexComp.Debugger.Expressions;

namespace FreexComp.Debugger.Session;

// Breakpoint traffic (DAP usage §4.1; architecture DBG-016).
internal sealed partial class DebugSession
{
    // Session loop only.
    private readonly BreakpointManager _breakpoints = new();

    /// <summary>
    /// Replaces the complete set of one file (DBG-409). Before the configuration phase the set is only stored (it is
    /// sent during launch); from then on it is sent at once, also while the debuggee runs (DBG-066).
    /// </summary>
    private async Task<IReadOnlyList<BreakpointStatus>> SetBreakpointsCoreAsync(
        string path, IReadOnlyList<BreakpointSpec> breakpoints, CancellationToken ct)
    {
        // During a session a condition is parsed at once; while idle it is not (DBG-211).
        var source = _breakpoints.Set(PathText.Normalize(path), breakpoints, State == DebugState.Idle ? null : ConditionSyntaxError);
        if (State is DebugState.Running or DebugState.Stopped || (State == DebugState.Starting && _initializedProcessed))
            await SendBreakpointsAsync(source, ct, raiseChanges: false).ConfigureAwait(false);
        var statuses = source.Statuses;
        _breakpoints.DropIfEmpty(source);
        return statuses;
    }

    /// <summary>The syntax error of a condition, or null when it parses (what it means is found out when it is evaluated).</summary>
    private string? ConditionSyntaxError(string condition)
    {
        try
        {
            SubsetValidator.Parse(condition, name => _typeInfo is not null && Evaluator.FindType(_typeInfo, name) is not null);
            return null;
        }
        catch (DebuggerException e)
        {
            return e.Code == DebugErrorCode.ExpressionSyntax ? e.Message : null;
        }
    }

    /// <summary>
    /// One <c>setBreakpoints</c> request with the complete enabled set of the file (and, last, the temporary
    /// run-to-line breakpoint, DBG-065). A file without enabled breakpoints
    /// is sent only if the adapter still holds breakpoints for it, and then once (DBG-063). A rejected request makes
    /// the breakpoints <c>Invalid</c> (DBG-054); a timeout faults.
    /// </summary>
    /// <param name="source">The file.</param>
    /// <param name="ct">Caller's token.</param>
    /// <param name="raiseChanges">Raise <c>BreakpointChanged</c> for every status that changed (launch: nobody gets a return value).</param>
    private async Task SendBreakpointsAsync(BreakpointManager.Source source, CancellationToken ct, bool raiseChanges)
    {
        var sent = source.Sent;
        if (sent.Count == 0 && !source.KnownToAdapter)
            return;

        var before = source.Statuses;
        var arguments = new SetBreakpointsArguments(
            new DapSource { Name = Path.GetFileName(source.Path), Path = source.Path },
            [.. sent.Select(e => new DapSourceBreakpoint(e.Spec.Line))]);
        try
        {
            var response = DapJson.Deserialize<SetBreakpointsResponseBody>(
                await RequestAsync("setBreakpoints", arguments, ct).ConfigureAwait(false));
            BreakpointManager.ApplyResponse(sent, response.Breakpoints);
            source.KnownToAdapter = sent.Count > 0;
        }
        catch (DebuggerException e) when (e.Code == DebugErrorCode.RequestFailed)
        {
            BreakpointManager.ApplyFailure(sent, e.Message);
        }

        if (!raiseChanges)
            return;
        var after = source.Statuses;
        for (var i = 0; i < after.Count; i++)
        {
            if (after[i] != before[i])
                _events.Raise(() => BreakpointChanged, new BreakpointChangedEventArgs(after[i]));
        }
    }

    /// <summary>A <c>breakpoint</c> event updates the IDE breakpoint with that adapter id; unknown ids are ignored (DBG-064).</summary>
    private void OnBreakpointEvent(BreakpointEventBody body)
    {
        if (body.Breakpoint.Id is not { } adapterId || _breakpoints.FindByAdapterId(adapterId) is not { } entry)
            return;
        if (body.Reason == "removed")
        {
            entry.SetUnbound(Messages.NotBoundYet);
            entry.AdapterId = null;
        }
        else
        {
            BreakpointManager.Apply(entry, body.Breakpoint);
        }

        _events.Raise(() => BreakpointChanged, new BreakpointChangedEventArgs(entry.Status));
    }
}
