using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FreexComp.Debugger.Dap;
using FreexComp.Debugger.Values;

namespace FreexComp.Debugger.Session;

// Debuggee memory and "address of" over the adapter (DAP usage §5.6, §5.7: DBG-082, DBG-086 … DBG-088), and the
// access object that value decoding and expression evaluation use (architecture §10).
internal sealed partial class DebugSession
{
    // Session loop only. The blocks are valid for one stop: dropped when the state leaves Stopped.
    private MemoryReader? _memory;
    private DebuggeeAccess? _debuggee;

    private MemoryReader Memory => _memory ??= new MemoryReader(FetchMemoryAsync);

    private IDebuggeeAccess Debuggee => _debuggee ??= new DebuggeeAccess(this);

    /// <summary>
    /// One <c>readMemory</c> request (DBG-086): the reference is "0x" and upper-case digits without leading zeros, the
    /// offset is 0. Returns what the adapter could read: fewer bytes than asked, or null for none (DBG-087).
    /// </summary>
    private async Task<byte[]?> FetchMemoryAsync(ulong address, int count, CancellationToken ct)
    {
        JsonElement body;
        try
        {
            var reference = "0x" + address.ToString("X", CultureInfo.InvariantCulture);
            body = await RequestAsync("readMemory", new ReadMemoryArguments(reference, count), ct).ConfigureAwait(false);
        }
        catch (DebuggerException e) when (e.Code == DebugErrorCode.RequestFailed)
        {
            return null;
        }

        if (DapJson.Deserialize<ReadMemoryResponseBody>(body).Data is not { Length: > 0 } data)
            return null;
        try
        {
            return Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task<byte[]> ReadMemoryCoreAsync(ulong address, int count, CancellationToken ct)
    {
        if (count <= 0)
            return [];
        return await Memory.ReadAsync(address, count, ct).ConfigureAwait(false)
            ?? throw new DebuggerException(DebugErrorCode.MemoryUnreadable, Messages.MemoryUnreadable(address, count), "readMemory");
    }

    /// <summary>
    /// DBG-082: <c>evaluate</c> of "&amp;" + the variable's declared spelling in the adapter's frame. The address is
    /// the <c>memoryReference</c> when the adapter sends one, else the first "0x…" token of <c>result</c> (what the
    /// pinned adapter sends); null when the request fails or carries no address.
    /// </summary>
    private async Task<ulong?> AddressOfCoreAsync(int adapterFrameId, string name, CancellationToken ct)
    {
        JsonElement body;
        try
        {
            body = await RequestAsync("evaluate", new EvaluateArguments("&" + name, adapterFrameId), ct).ConfigureAwait(false);
        }
        catch (DebuggerException e) when (e.Code == DebugErrorCode.RequestFailed)
        {
            return null;
        }

        var response = DapJson.Deserialize<EvaluateResponseBody>(body);
        return FirstHexToken(response.MemoryReference) ?? FirstHexToken(response.Result);
    }

    private static ulong? FirstHexToken(string? text)
    {
        if (text is null || HexToken().Match(text) is not { Success: true } match)
            return null;
        return ulong.TryParse(match.Groups[1].ValueSpan, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    [GeneratedRegex("0[xX]([0-9A-Fa-f]+)", RegexOptions.CultureInvariant)]
    private static partial Regex HexToken();

    /// <summary>
    /// Test hook: runs <paramref name="body"/> on the session loop with the debuggee access, under the same state
    /// rule as an inspection call (only while <c>Stopped</c>).
    /// </summary>
    internal Task<T> InspectAsync<T>(Func<IDebuggeeAccess, Task<T>> body, CancellationToken ct) =>
        PostAsync(SessionMethod.ReadMemoryAsync, () => body(Debuggee), ct);

    private sealed class DebuggeeAccess(DebugSession session) : IDebuggeeAccess
    {
        public Task<byte[]?> ReadMemoryAsync(ulong address, int count, CancellationToken ct) => session.Memory.ReadAsync(address, count, ct);

        public Task<ulong?> AddressOfAsync(int frameId, string variablePath, CancellationToken ct) =>
            session.AddressOfCoreAsync(frameId, variablePath, ct);

        public Task<bool> WriteMemoryAsync(ulong address, ReadOnlyMemory<byte> data, CancellationToken ct) =>
            throw NotYet(SessionMethod.SetValueAsync, "G-048");

        public Task<IReadOnlyList<RawVariable>> GetRawVariablesAsync(int frameId, ScopeKind scope, CancellationToken ct) =>
            throw NotYet(SessionMethod.GetScopesAsync, "G-041");
    }
}
