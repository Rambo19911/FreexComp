namespace FreexComp.Debugger.Values;

/// <summary>
/// Everything value decoding and expression evaluation need from the debuggee (architecture §10). The session
/// implements it over the debug adapter; nothing in this namespace uses adapter message types.
/// </summary>
internal interface IDebuggeeAccess
{
    /// <summary>Exactly <paramref name="count"/> bytes at <paramref name="address"/>; null when any of them is unreadable.</summary>
    Task<byte[]?> ReadMemoryAsync(ulong address, int count, CancellationToken ct);

    Task<bool> WriteMemoryAsync(ulong address, ReadOnlyMemory<byte> data, CancellationToken ct);

    /// <summary>Address of a root variable of a frame (the adapter's frame id); null when it cannot be obtained.</summary>
    Task<ulong?> AddressOfAsync(int frameId, string variablePath, CancellationToken ct);

    Task<IReadOnlyList<RawVariable>> GetRawVariablesAsync(int frameId, ScopeKind scope, CancellationToken ct);
}

/// <summary>A variable as the adapter lists and prints it.</summary>
internal sealed record RawVariable(string Name, string Value, string? Type);
