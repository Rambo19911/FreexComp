using System.Buffers.Binary;
using FreexComp.Debugger.Values;

namespace FreexComp.Debugger.Tests.TestSupport;

/// <summary>
/// Debuggee memory for decoder tests: address → byte. A read succeeds only when every byte of it was put; every read
/// is recorded. Header layouts written by the helpers are those of the fixture's <c>abi</c> block.
/// </summary>
internal sealed class FakeMemory : IDebuggeeAccess
{
    private readonly Dictionary<ulong, byte> _bytes = [];

    /// <summary>Every read as "ADDRESS+count" (upper-case hexadecimal address).</summary>
    public List<string> Reads { get; } = [];

    public FakeMemory Put(ulong address, params byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
            _bytes[address + (ulong)i] = bytes[i];
        return this;
    }

    /// <summary>Makes a range readable (zero bytes) without changing bytes already put.</summary>
    public FakeMemory Map(ulong address, int count)
    {
        for (var i = 0; i < count; i++)
            _bytes.TryAdd(address + (ulong)i, 0);
        return this;
    }

    public FakeMemory PutHex(ulong address, string hex) => Put(address, Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal)));

    public FakeMemory PutInt32(ulong address, int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return Put(address, bytes);
    }

    public FakeMemory PutInt64(ulong address, long value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return Put(address, bytes);
    }

    public FakeMemory PutPointer(ulong address, ulong value) => PutInt64(address, (long)value);

    /// <summary>
    /// A string payload at <paramref name="payload"/>: Int32 length at -12, the UTF-16 units as they are (lone
    /// surrogates included), a #0.
    /// </summary>
    public FakeMemory PutUnicodeString(ulong payload, string text, int? length = null) =>
        PutInt32(payload - 12, length ?? text.Length)
            .Put(payload, [.. (text + "\0").SelectMany(unit => new[] { (byte)unit, (byte)(unit >> 8) })]);

    /// <summary>A byte-string payload: Int32 length at -12, Word code page at -8, the bytes, a #0.</summary>
    public FakeMemory PutAnsiString(ulong payload, byte[] bytes, ushort codePage, int? length = null) =>
        PutInt32(payload - 12, length ?? bytes.Length).Put(payload - 8, (byte)codePage, (byte)(codePage >> 8)).Put(payload, [.. bytes, 0]);

    /// <summary>A dynamic-array payload of Int32 elements: Int64 length at -8.</summary>
    public FakeMemory PutIntArray(ulong payload, params int[] elements)
    {
        PutInt64(payload - 8, elements.Length);
        for (var i = 0; i < elements.Length; i++)
            PutInt32(payload + (ulong)(4 * i), elements[i]);
        return this;
    }

    /// <summary>A VMT: parent VMT pointer at +0, class-name string pointer at +16 (the name payload is put too).</summary>
    public FakeMemory PutVmt(ulong vmt, string className, ulong parentVmt, ulong namePayload) =>
        PutPointer(vmt, parentVmt).PutPointer(vmt + 8, 0).PutPointer(vmt + 16, namePayload).PutUnicodeString(namePayload, className);

    /// <summary>Everything put, as the <c>memory</c> array of a FakeAdapter scenario (one entry per contiguous run).</summary>
    public string ScenarioJson()
    {
        var runs = new List<string>();
        var addresses = _bytes.Keys.Order().ToList();
        for (var i = 0; i < addresses.Count;)
        {
            var start = i;
            while (i + 1 < addresses.Count && addresses[i + 1] == addresses[i] + 1)
                i++;
            var bytes = addresses[start..(i + 1)].Select(a => _bytes[a]).ToArray();
            runs.Add($$"""{ "address": "0x{{addresses[start]:X}}", "hex": "{{Convert.ToHexString(bytes)}}" }""");
            i++;
        }

        return "[ " + string.Join(", ", runs) + " ]";
    }

    public void Remove(ulong address, int count)
    {
        for (var i = 0; i < count; i++)
            _bytes.Remove(address + (ulong)i);
    }

    public Task<byte[]?> ReadMemoryAsync(ulong address, int count, CancellationToken ct)
    {
        Reads.Add($"{address:X}+{count}");
        var result = new byte[count];
        for (var i = 0; i < count; i++)
        {
            if (!_bytes.TryGetValue(address + (ulong)i, out result[i]))
                return Task.FromResult<byte[]?>(null);
        }

        return Task.FromResult<byte[]?>(result);
    }

    public Task<bool> WriteMemoryAsync(ulong address, ReadOnlyMemory<byte> data, CancellationToken ct) => throw new NotSupportedException();

    public Task<ulong?> AddressOfAsync(int frameId, string variablePath, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<RawVariable>> GetRawVariablesAsync(int frameId, ScopeKind scope, CancellationToken ct) => throw new NotSupportedException();
}
