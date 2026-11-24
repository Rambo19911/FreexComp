using System.Buffers.Binary;
using System.Globalization;
using FreexComp.Debugger.Dap;
using FreexComp.Debugger.Expressions;
using FreexComp.Debugger.Values;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Session;

// Scopes, children, expression evaluation and value modification (DAP usage §5.4 … §5.8; Pascal values §8, §9).
internal sealed partial class DebugSession
{
    /// <summary>A frame of the current stop: the adapter's id and where it is.</summary>
    /// <param name="AdapterId">The adapter's id of the frame.</param>
    /// <param name="Location">Where the frame is.</param>
    /// <param name="Routine">The routine whose variables the frame sees; null without type information.</param>
    /// <param name="VariablesAdapterId">The adapter's id of the frame that holds those variables: the frame itself, or for an outlined finally part the frame of its routine.</param>
    private sealed record FrameEntry(int AdapterId, SourceLocation? Location, PdiRoutine? Routine, int VariablesAdapterId);

    // Session loop only; everything here is valid for one stop.
    private ValueService? _values;
    private FrameEntry? _stopTopFrame;                                   // top frame of the stopping thread
    private readonly Dictionary<int, IReadOnlyList<DapScope>> _adapterScopes = [];   // adapter frame id -> its scopes
    private readonly Dictionary<int, IReadOnlyList<RawVariable>> _adapterVariables = [];   // variables reference -> list

    private ValueService Values
    {
        get
        {
            if (_values is null)
            {
                _values = new ValueService(_typeInfo, Debuggee, _options.Format,
                    HasCapability("supportsWriteMemoryRequest"), HasCapability("supportsSetVariable"));
                _values.Reset(_epoch);
            }

            return _values;
        }
    }

    /// <summary>Drops what was read from the debuggee: it has run, or its memory was written.</summary>
    private void DropDebuggeeCaches()
    {
        _memory?.Clear();
        _adapterScopes.Clear();
        _adapterVariables.Clear();
    }

    private static FrameContext ContextOf(FrameEntry frame) =>
        new(frame.VariablesAdapterId, frame.Routine, frame.Location?.Line ?? 0);

    /// <summary>
    /// The entry of frame <paramref name="index"/> of a page of the stack of <paramref name="threadId"/> that starts
    /// at frame <paramref name="pageStart"/> and was asked for with <paramref name="levels"/> frames
    /// (<see cref="FrameRoutines.VariablesOfAsync"/>; a page shorter than asked for ends the stack).
    /// </summary>
    private async Task<FrameEntry> EntryOfAsync(int threadId, IReadOnlyList<DapStackFrame> page, int index, int pageStart, int levels, CancellationToken ct)
    {
        var (routine, variablesFrameId) = await FrameRoutines.VariablesOfAsync(
            _typeInfo,
            page,
            index,
            page.Count < levels
                ? null
                : async past => DapJson.Deserialize<StackTraceResponseBody>(await RequestAsync(
                    "stackTrace", new StackTraceArguments(threadId, pageStart + page.Count + past, FrameRoutines.FetchCount), ct).ConfigureAwait(false)).StackFrames)
            .ConfigureAwait(false);
        return new FrameEntry(page[index].Id, FrameRoutines.LocationOf(page[index]), routine, variablesFrameId);
    }

    /// <summary>The frame of an inspection call: one of our frame ids of this stop, or (null) the top frame of the stopping thread.</summary>
    private FrameContext ContextOf(int? frameId) =>
        ContextOf(frameId is { } id
            ? RequireFrame(id)
            : _stopTopFrame ?? throw new DebuggerException(DebugErrorCode.StaleReference, Messages.StaleFrame));

    private IReadOnlyList<VariableScope> GetScopesCore(int frameId) => Values.GetScopes(ContextOf(frameId));

    private Task<IReadOnlyList<VariableInfo>> GetChildrenCoreAsync(VariableHandle handle, int start, int count, CancellationToken ct) =>
        Values.GetChildrenAsync(handle, start, count, ct);

    private async Task<EvaluateResult> EvaluateCoreAsync(string expression, int? frameId, CancellationToken ct)
    {
        var frame = ContextOf(frameId);
        return new EvaluateResult(
            await Values.EvaluateAsync(expression, frame, allowFormatSuffix: true, expandable: true, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// DBG-177: evaluate the target to a modifiable location, evaluate and convert the new value, write it
    /// (<c>writeMemory</c>, or <c>setVariable</c> for a root variable when the adapter cannot write memory), drop what
    /// was read from the debuggee and return the node read again.
    /// </summary>
    private async Task<VariableInfo> SetValueCoreAsync(string evaluateName, string valueExpression, int? frameId, CancellationToken ct)
    {
        var frame = ContextOf(frameId);
        var values = Values;
        var target = await values.ResolveTargetAsync(evaluateName, frame, ct).ConfigureAwait(false);
        var canWriteMemory = HasCapability("supportsWriteMemoryRequest");
        if (!canWriteMemory && !(HasCapability("supportsSetVariable") && target.RootName is not null))
            throw new DebuggerException(DebugErrorCode.NotModifiable, Messages.NotModifiable(evaluateName.Trim()));

        var bytes = await values.EvaluateForAssignmentAsync(valueExpression, target.Location.Type, frame, ct).ConfigureAwait(false);
        if (canWriteMemory)
        {
            if (!await WriteMemoryCoreAsync(target.Location.Address, bytes, ct).ConfigureAwait(false))
                throw new DebuggerException(DebugErrorCode.RequestFailed, Messages.MemoryWriteIncomplete, "writeMemory");
        }
        else
        {
            var scope = await FindAdapterScopeAsync(frame.AdapterFrameId, target.IsGlobal ? ScopeKind.Globals : ScopeKind.Locals, ct).ConfigureAwait(false)
                ?? throw new DebuggerException(DebugErrorCode.NotModifiable, Messages.NotModifiable(evaluateName.Trim()));
            await RequestAsync("setVariable",
                new SetVariableArguments(scope.VariablesReference, target.RootName!, AdapterValueText(bytes, target.Location.Type)), ct).ConfigureAwait(false);
        }

        // Other nodes may alias the written memory: everything is read again.
        DropDebuggeeCaches();
        return await values.PresentAsync(target.Location, evaluateName.Trim(), target.EvaluateName, null, expandable: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The value text for <c>setVariable</c> (DAP usage §5.8): a decimal integer, a C-style floating-point literal or a
    /// "0x" pointer; enums, Booleans and characters as their ordinal number.
    /// </summary>
    private static string AdapterValueText(byte[] bytes, PdiType type) => type.Kind switch
    {
        PdiTypeKind.Integer when type.Signed => ValueFormatter.Signed(bytes).ToString(CultureInfo.InvariantCulture),
        PdiTypeKind.Float when type.FloatFormat == PdiFloatFormat.Single =>
            BinaryPrimitives.ReadSingleLittleEndian(bytes).ToString("R", CultureInfo.InvariantCulture),
        PdiTypeKind.Float when type.FloatFormat == PdiFloatFormat.Currency =>
            (BinaryPrimitives.ReadInt64LittleEndian(bytes) / 10000m).ToString(CultureInfo.InvariantCulture),
        PdiTypeKind.Float => BinaryPrimitives.ReadDoubleLittleEndian(bytes).ToString("R", CultureInfo.InvariantCulture),
        PdiTypeKind.Pointer => "0x" + ValueFormatter.Unsigned(bytes).ToString("X", CultureInfo.InvariantCulture),
        _ => ValueFormatter.Unsigned(bytes).ToString(CultureInfo.InvariantCulture),
    };

    // ---- the adapter's own scopes and variables (DBG-078, DBG-080) ----

    /// <summary>
    /// The adapter's scope of a kind for a frame: found by <c>presentationHint</c> or, without one, by name
    /// (case-insensitive). The adapter has no scope for arguments (they are listed under Locals).
    /// </summary>
    private async Task<DapScope?> FindAdapterScopeAsync(int adapterFrameId, ScopeKind kind, CancellationToken ct)
    {
        var scopes = await GetAdapterScopesAsync(adapterFrameId, ct).ConfigureAwait(false);
        var wanted = kind == ScopeKind.Globals ? "globals" : "locals";
        return scopes.FirstOrDefault(s => string.Equals(s.PresentationHint, wanted, StringComparison.OrdinalIgnoreCase))
            ?? scopes.FirstOrDefault(s => string.IsNullOrEmpty(s.PresentationHint) && s.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>One <c>scopes</c> request per frame; the answer is kept for the stop.</summary>
    private async Task<IReadOnlyList<DapScope>> GetAdapterScopesAsync(int adapterFrameId, CancellationToken ct)
    {
        if (!_adapterScopes.TryGetValue(adapterFrameId, out var scopes))
        {
            scopes = DapJson.Deserialize<ScopesResponseBody>(
                await RequestAsync("scopes", new ScopesArguments(adapterFrameId), ct).ConfigureAwait(false)).Scopes;
            _adapterScopes[adapterFrameId] = scopes;
        }

        return scopes;
    }

    private async Task<IReadOnlyList<RawVariable>> GetRawVariablesCoreAsync(int adapterFrameId, ScopeKind kind, CancellationToken ct)
    {
        if (kind == ScopeKind.Arguments
            || await FindAdapterScopeAsync(adapterFrameId, kind, ct).ConfigureAwait(false) is not { } scope)
            return [];
        return await GetRawChildrenCoreAsync(scope.VariablesReference, ct).ConfigureAwait(false);
    }

    /// <summary>One <c>variables</c> request with nothing but the reference (DBG-080); the answer is kept for the stop.</summary>
    private async Task<IReadOnlyList<RawVariable>> GetRawChildrenCoreAsync(int reference, CancellationToken ct)
    {
        if (reference <= 0)
            return [];
        if (!_adapterVariables.TryGetValue(reference, out var variables))
        {
            variables = [.. DapJson.Deserialize<VariablesResponseBody>(
                    await RequestAsync("variables", new VariablesArguments(reference), ct).ConfigureAwait(false)).Variables
                .Select(v => new RawVariable(v.Name, v.Value, v.Type, v.VariablesReference))];
            _adapterVariables[reference] = variables;
        }

        return variables;
    }

    /// <summary>
    /// One <c>writeMemory</c> request (DAP usage §5.8). False when the adapter reports fewer bytes written than sent; a
    /// rejected request faults.
    /// </summary>
    private async Task<bool> WriteMemoryCoreAsync(ulong address, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var reference = "0x" + address.ToString("X", CultureInfo.InvariantCulture);
        var body = await RequestAsync("writeMemory", new WriteMemoryArguments(reference, Convert.ToBase64String(data.Span)), ct).ConfigureAwait(false);
        return DapJson.Deserialize<WriteMemoryResponseBody>(body).BytesWritten is not { } written || written == data.Length;
    }
}
