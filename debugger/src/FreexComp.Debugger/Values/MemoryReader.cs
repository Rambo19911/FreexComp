namespace FreexComp.Debugger.Values;

/// <summary>
/// Debuggee memory for one stop (DAP usage DBG-086 … DBG-088): 256-byte blocks aligned to 256 are fetched once and
/// kept until <see cref="Clear"/>; a read is assembled from the blocks it touches. A read that touches a block which
/// is not readable in full is retried once as a request for exactly its own range (memory that ends inside a block).
/// Not thread-safe: used by the session loop only.
/// </summary>
internal sealed class MemoryReader
{
    public const int BlockSize = 256;
    public const int MaxBlocks = 4096;

    /// <summary>Largest single request; larger runs of missing blocks are split.</summary>
    public const int MaxRequestBytes = 65536;

    /// <summary>Addresses below this are the nil-pointer page: never requested, always unreadable.</summary>
    public const ulong LowestReadableAddress = 0x10000;

    private readonly Func<ulong, int, CancellationToken, Task<byte[]?>> _fetch;
    // Block address -> its 256 bytes; null marks a block found unreadable in this stop.
    private readonly Dictionary<ulong, byte[]?> _blocks = [];
    // Reads that could not be served from whole blocks: exact range -> its bytes, or null when unreadable.
    private readonly Dictionary<(ulong Address, int Count), byte[]?> _exact = [];

    /// <param name="fetch">
    /// One request to the debuggee: the bytes at an address. A result shorter than the requested count means the
    /// tail is unreadable; null means nothing is readable.
    /// </param>
    public MemoryReader(Func<ulong, int, CancellationToken, Task<byte[]?>> fetch) => _fetch = fetch;

    public int CachedBlockCount => _blocks.Count;

    /// <summary>Drops every block (the state left <c>Stopped</c>, or memory was written).</summary>
    public void Clear()
    {
        _blocks.Clear();
        _exact.Clear();
    }

    /// <summary>
    /// Exactly <paramref name="count"/> bytes at <paramref name="address"/>, or null when the range is not fully
    /// readable. Callers that can use a readable prefix (string payloads) read block by block.
    /// </summary>
    public async Task<byte[]?> ReadAsync(ulong address, int count, CancellationToken ct)
    {
        if (count <= 0)
            return count == 0 ? [] : null;
        if (address < LowestReadableAddress || address > ulong.MaxValue - (ulong)count - BlockSize)
            return null;
        if (await ReadBlocksAsync(address, count, ct).ConfigureAwait(false) is { } fromBlocks)
            return fromBlocks;

        // A touched block is not readable in full: ask once for exactly this range.
        if (!_exact.TryGetValue((address, count), out var exact))
        {
            exact = await FetchExactAsync(address, count, ct).ConfigureAwait(false);
            if (_exact.Count >= MaxBlocks)
                _exact.Clear();
            _exact[(address, count)] = exact;
        }

        return exact is null ? null : [.. exact];
    }

    private async Task<byte[]?> FetchExactAsync(ulong address, int count, CancellationToken ct)
    {
        var result = new byte[count];
        for (var done = 0; done < count;)
        {
            var length = Math.Min(count - done, MaxRequestBytes);
            var data = await _fetch(address + (ulong)done, length, ct).ConfigureAwait(false);
            if (data is null || data.Length < length)
                return null;
            Array.Copy(data, 0, result, done, length);
            done += length;
        }

        return result;
    }

    private async Task<byte[]?> ReadBlocksAsync(ulong address, int count, CancellationToken ct)
    {

        var first = address & ~(ulong)(BlockSize - 1);
        var last = (address + (ulong)count - 1) & ~(ulong)(BlockSize - 1);

        // A block already known to be unreadable fails the read without any request.
        for (var block = first; ; block += BlockSize)
        {
            if (_blocks.TryGetValue(block, out var known) && known is null)
                return null;
            if (block == last)
                break;
        }

        var result = new byte[count];
        var missingFrom = (ulong?)null;
        for (var block = first; ; block += BlockSize)
        {
            if (_blocks.TryGetValue(block, out var cached))
            {
                if (missingFrom is { } start && !await FetchRunAsync(start, block, address, result, ct).ConfigureAwait(false))
                    return null;
                missingFrom = null;
                CopyBlock(block, cached!, address, result);
            }
            else
            {
                missingFrom ??= block;
                // Runs are limited to one request each.
                if (block + BlockSize - missingFrom.Value == MaxRequestBytes || block == last)
                {
                    if (!await FetchRunAsync(missingFrom.Value, block + BlockSize, address, result, ct).ConfigureAwait(false))
                        return null;
                    missingFrom = null;
                }
            }

            if (block == last)
                break;
        }

        return result;
    }

    /// <summary>Fetches the blocks <c>[from, to)</c> with one request, stores them and copies their part of the read.</summary>
    private async Task<bool> FetchRunAsync(ulong from, ulong to, ulong address, byte[] result, CancellationToken ct)
    {
        var length = (int)(to - from);
        var data = await _fetch(from, length, ct).ConfigureAwait(false) ?? [];
        var complete = Math.Min(data.Length, length) / BlockSize;
        for (var i = 0; i < complete; i++)
        {
            var block = new byte[BlockSize];
            Array.Copy(data, i * BlockSize, block, 0, BlockSize);
            Store(from + (ulong)(i * BlockSize), block);
            CopyBlock(from + (ulong)(i * BlockSize), block, address, result);
        }

        if (complete * BlockSize == length)
            return true;

        // The first block that did not arrive in full is unreadable for this stop.
        Store(from + (ulong)(complete * BlockSize), null);
        return false;
    }

    private void Store(ulong blockAddress, byte[]? block)
    {
        if (_blocks.Count >= MaxBlocks && !_blocks.ContainsKey(blockAddress))
            _blocks.Clear();
        _blocks[blockAddress] = block;
    }

    /// <summary>Copies the part of a block that lies inside the read <c>[address, address + result.Length)</c>.</summary>
    private static void CopyBlock(ulong blockAddress, byte[] block, ulong address, byte[] result)
    {
        var from = Math.Max(blockAddress, address);
        var to = Math.Min(blockAddress + BlockSize, address + (ulong)result.Length);
        if (to > from)
            Array.Copy(block, (int)(from - blockAddress), result, (int)(from - address), (int)(to - from));
    }
}
