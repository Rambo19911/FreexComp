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

    /// <summary>The variables the adapter lists in a scope of a frame (the adapter's frame id); empty when it has no such scope.</summary>
    Task<IReadOnlyList<RawVariable>> GetRawVariablesAsync(int frameId, ScopeKind scope, CancellationToken ct);

    /// <summary>The adapter's own children of a variable it listed (<see cref="RawVariable.Reference"/>); used only for raw nodes.</summary>
    Task<IReadOnlyList<RawVariable>> GetRawChildrenAsync(int reference, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RawVariable>>([]);
}

/// <summary>A variable as the adapter lists and prints it.</summary>
/// <param name="Name">Name as the adapter spells it.</param>
/// <param name="Value">The adapter's value text.</param>
/// <param name="Type">The adapter's type text, if any.</param>
/// <param name="Reference">The adapter's reference for the variable's children; 0 for none.</param>
internal sealed record RawVariable(string Name, string Value, string? Type, int Reference = 0);
