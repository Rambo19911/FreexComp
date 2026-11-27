using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Values.Visualizers;

/// <summary>
/// The visualisers of one debug session: the table, selection (DBG-503) and every call into a visualiser under its time
/// limit on the session's clock (DBG-510), with error isolation, once-logging and the turn-off after three failures
/// (DBG-511, DBG-512). The failure counters belong to the session: one host lives as long as the session, the value
/// services of its stops share it. Used on the session's value path only (no thread of its own).
/// </summary>
/// <param name="registry">The table.</param>
/// <param name="time">The session's clock.</param>
/// <param name="log">Raises one <c>Output(Debugger)</c> line.</param>
internal sealed class VisualizerHost(VisualizerRegistry registry, TimeProvider time, Action<string> log)
{
    /// <summary>Failures after which a visualiser is turned off for the session (DBG-512).</summary>
    public const int MaxFailures = 3;

    private readonly Dictionary<string, int> _failures = new(StringComparer.Ordinal);

    /// <summary>No visualisers (a value service of a test that does not ask for any).</summary>
    public static VisualizerHost None { get; } = new(VisualizerRegistry.BuiltIn, TimeProvider.System, _ => { });

    public bool IsEmpty => registry.IsEmpty;

    /// <summary>Failures of a visualiser in this session so far.</summary>
    public int Failures(string id) => _failures.GetValueOrDefault(id);

    /// <summary>
    /// The registration selected for a node (DBG-503), or null for the plain rendering. Error and adapter nodes and
    /// <c>nil</c> instances are never visualised; in raw mode only transparent visualisers are; an immediate
    /// (computed) value only by a visualiser whose kinds contain its kind (DBG-500).
    /// </summary>
    public VisualizerRegistration? Select(ValueNode node, bool raw, bool immediate)
    {
        if (registry.IsEmpty || node.Kind is ValueKind.Error or ValueKind.Raw
            || (node.Kind == ValueKind.ClassInstance && node.Pointer == 0))
            return null;
        return registry.Select(Candidates(node), (registration, type) =>
            (!raw || registration.Transparent)
            && (!immediate || registration.Kinds?.Contains(type.Kind) == true)
            && Failures(registration.Visualizer.Id) < MaxFailures
            && Matches(registration, node, type));
    }

    /// <summary>The node's type, the aliased types, and for a class instance (whose type is its dynamic class) each ancestor.</summary>
    private static List<PdiType> Candidates(ValueNode node)
    {
        var candidates = new List<PdiType>();
        for (var type = node.Type; type is not null && candidates.Count < 16; type = type.AliasOf)
            candidates.Add(type);
        if (node.Kind == ValueKind.ClassInstance)
        {
            for (var (ancestor, depth) = (candidates[^1].Ancestor, 0); ancestor is not null && depth < 256; ancestor = ancestor.Ancestor, depth++)
                candidates.Add(ancestor);
        }

        return candidates;
    }

    private bool Matches(VisualizerRegistration registration, ValueNode node, PdiType type)
    {
        try
        {
            return registration.Visualizer.Matches(type);
        }
        catch (Exception e)
        {
            Failed(registration, node, e.Message);
            return false;
        }
    }

    /// <summary>
    /// One visualiser call (<c>SummaryAsync</c>, <c>ChildrenAsync</c> or one <c>ChildAt</c>) under the time limit and
    /// with a fresh read budget. A failure is recorded and logged and gives <c>Ok = false</c>; the caller's own
    /// cancellation propagates as <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<(bool Ok, T? Result, string? Reason)> CallAsync<T>(
        VisualizerRegistration registration, ValueNode node, VisualizerContext context, TimeSpan limit,
        Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var timer = new CancellationTokenSource(limit, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);
        string reason;
        context.Begin(linked.Token);
        try
        {
            // A visualiser that ignores its token is left behind at the limit; its context is closed then.
            var result = await call(linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
            if (context.Failure is null)
                return (true, result, null);
            reason = context.Failure;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timer.IsCancellationRequested)
        {
            reason = Messages.VisualizerTimedOut(limit);
        }
        catch (Exception e)
        {
            reason = context.Failure ?? e.Message;
        }
        finally
        {
            context.End();
        }

        Failed(registration, node, reason);
        return (false, default, reason);
    }

    /// <summary>DBG-512: the first failure is logged; the third turns the visualiser off and says so.</summary>
    private void Failed(VisualizerRegistration registration, ValueNode node, string reason)
    {
        var id = registration.Visualizer.Id;
        var failures = _failures[id] = Failures(id) + 1;
        if (failures == 1)
            log(Messages.VisualizerFailed(id, node.TypeName, reason));
        else if (failures == MaxFailures)
            log(Messages.VisualizerTurnedOff(id, MaxFailures));
    }
}
