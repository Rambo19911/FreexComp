using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Values.Visualizers;

/// <summary>
/// The built-in string visualiser (visualizers.md DBG-515): transparent - every summary stays the plain one - for every
/// string flavour, static, dynamic and open arrays of <c>Char</c> or <c>AnsiChar</c>, and computed strings. What it adds
/// is the text source of the value (DBG-516 … DBG-523).
/// </summary>
internal sealed class StringVisualizer : IValueVisualizer
{
    /// <summary>The row of the built-in table (first, DBG-515).</summary>
    public static VisualizerRegistration Registration { get; } = new(
        new StringVisualizer(), Unit: null, NamePattern: null,
        new HashSet<PdiTypeKind> { PdiTypeKind.String, PdiTypeKind.StaticArray, PdiTypeKind.DynArray, PdiTypeKind.OpenArray },
        Transparent: true, SummarizesNested: true);

    /// <summary>Units a zero-terminated text is scanned for its terminator (DBG-517, DBG-519).</summary>
    public const long MaxTerminatedLength = 16_777_216;

    public string Id => "string";

    public bool Matches(PdiType type) =>
        type.Kind == PdiTypeKind.String
        || (type.Kind is PdiTypeKind.StaticArray or PdiTypeKind.DynArray or PdiTypeKind.OpenArray
            && type.Element is { Kind: PdiTypeKind.Char, Size: 1 or 2 });

    // Transparent: the framework never calls these two; they give the plain rendering.
    public Task<string> SummaryAsync(ValueNode value, VisualizerContext context, CancellationToken ct) =>
        Task.FromResult(context.Nested ? context.FormatNested(value) : new ValueFormatter(context.Options).Format(value));

    public Task<VisualizerChildren?> ChildrenAsync(ValueNode value, VisualizerContext context, CancellationToken ct) =>
        Task.FromResult<VisualizerChildren?>(null);

    /// <summary>
    /// DBG-516: the text of a string, character array or computed string; null for a <c>nil</c> PChar / PAnsiChar
    /// (whose value is <c>nil</c>) and for anything else. Reads no memory.
    /// </summary>
    public ITextSource? TextSource(ValueNode value, VisualizerContext context)
    {
        if (value.Kind is not (ValueKind.String or ValueKind.StaticArray or ValueKind.DynamicArray))
            return null;
        // An immediate result has no storage: its text is the computed value (DBG-523).
        if (value.Kind == ValueKind.String && value.Address is null)
            return new ComputedText(value.Text ?? "");

        var type = value.Type;
        var abi = context.Abi;
        // A length the plain decoding already found: the header's, or the terminator within the units it read.
        long? found = !value.Partial && !value.Capped ? value.Text?.Length : null;
        if (type.Kind != PdiTypeKind.String)
        {
            // DBG-521: the elements up to the first #0, at most the element count.
            var data = type.Kind == PdiTypeKind.StaticArray ? value.Address ?? 0 : value.Pointer;
            return new MemoryText(context.ReadTextMemoryAsync, TextKind.CharArray, type.Element!.Size, data, found, Math.Max(value.Length, 0));
        }

        return type.Flavor switch
        {
            PdiStringFlavor.Unicode => new MemoryText(context.ReadTextMemoryAsync, TextKind.Unicode, 2, value.Pointer, value.Length, value.Length)
            {
                RefCountField = (abi.UnicodeString.RefCountOffset, abi.UnicodeString.RefCountSize),
            },
            PdiStringFlavor.Ansi => new MemoryText(context.ReadTextMemoryAsync, TextKind.Ansi, 1, value.Pointer, value.Length, value.Length)
            {
                RefCountField = (abi.AnsiString.RefCountOffset, abi.AnsiString.RefCountSize),
                CodePageField = (abi.AnsiString.CodePageOffset, abi.AnsiString.CodePageSize),
            },
            // DBG-522: byte 0 (clamped to the maximum) is the length; the bytes follow it.
            PdiStringFlavor.Short => new MemoryText(context.ReadTextMemoryAsync, TextKind.Short, 1, (value.Address ?? 0) + 1, value.Length, value.Length)
            {
                MaxLength = type.MaxLength,
            },
            _ when value.Pointer == 0 => null,
            PdiStringFlavor.PChar => new MemoryText(context.ReadTextMemoryAsync, TextKind.PChar, 2, value.Pointer, found, MaxTerminatedLength),
            _ => new MemoryText(context.ReadTextMemoryAsync, TextKind.PAnsiChar, 1, value.Pointer, found, MaxTerminatedLength),
        };
    }

    /// <summary>
    /// DBG-519: where a chunk of UTF-16 units that would end after <paramref name="take"/> units ends instead: one unit
    /// earlier between the two units of a valid surrogate pair, or (a chunk of one unit) one later.
    /// </summary>
    internal static int KeepSurrogatePair(int take, int available, Func<int, char> unit) =>
        take > 0 && take < available && char.IsHighSurrogate(unit(take - 1)) && char.IsLowSurrogate(unit(take))
            ? take > 1 ? take - 1 : take + 1
            : take;

    /// <summary>
    /// DBG-519: where a chunk of UTF-8 bytes that would end after <paramref name="take"/> bytes ends instead: before a
    /// valid sequence it would cut (up to 3 bytes earlier), or (a chunk that is only that sequence's start) after it.
    /// </summary>
    internal static int KeepUtf8Sequence(int take, ReadOnlySpan<byte> bytes)
    {
        for (var back = 1; back <= Math.Min(3, take); back++)
        {
            var lead = bytes[take - back];
            if ((lead & 0xC0) == 0x80)
                continue;
            var length = lead switch
            {
                >= 0xC2 and <= 0xDF => 2,
                >= 0xE0 and <= 0xEF => 3,
                >= 0xF0 and <= 0xF4 => 4,
                _ => 1,
            };
            var from = take - back;
            if (length > back && from + length <= bytes.Length
                && Rune.DecodeFromUtf8(bytes.Slice(from, length), out _, out var consumed) == OperationStatus.Done && consumed == length)
                return from > 0 ? from : length;
            break;
        }

        return take;
    }

    /// <summary>The text of a computed string (DBG-523): the value itself.</summary>
    private sealed class ComputedText(string text) : ITextSource
    {
        public Task<TextDetails> DetailsAsync(CancellationToken ct) =>
            Task.FromResult(new TextDetails(TextKind.Computed, text.Length, null, null, Utf16Name, 2, null, null));

        public Task<TextChunk> ReadAsync(long start, int count, CancellationToken ct)
        {
            if (start >= text.Length)
                return Task.FromResult(new TextChunk(start, start, "", true, false));
            var from = (int)start;
            var take = KeepSurrogatePair((int)Math.Min(count, text.Length - from), text.Length - from, i => text[from + i]);
            return Task.FromResult(new TextChunk(start, start + take, text.Substring(from, take), from + take >= text.Length, false));
        }
    }

    private const string Utf16Name = "UTF-16";

    /// <summary>
    /// A text in debuggee memory: <paramref name="elementSize"/>-byte elements from <paramref name="payload"/> (0: an
    /// empty text). With a known <paramref name="length"/> the text has that many elements; without one it ends at
    /// the first #0 element, at the latest after <paramref name="bound"/> elements.
    /// </summary>
    private sealed class MemoryText(
        Func<ulong, int, CancellationToken, Task<byte[]?>> read, TextKind kind, int elementSize, ulong payload, long? length, long bound)
        : ITextSource
    {
        /// <summary>Bytes of one read of the terminator scan (DBG-517).</summary>
        private const int ScanBytes = 65536;

        /// <summary>Alignment of the steps that find the readable prefix of a range (the memory reader's block).</summary>
        private const int Alignment = MemoryReader.BlockSize;

        private Task<long>? _scan;
        private Task<int?>? _codePage;

        /// <summary>Offset from the payload and size of the reference count (Unicode, Ansi).</summary>
        public (long Offset, int Size)? RefCountField { get; init; }

        /// <summary>Offset from the payload and size of the code page (Ansi).</summary>
        public (long Offset, int Size)? CodePageField { get; init; }

        public int? MaxLength { get; init; }

        private bool Terminated => length is null;

        public async Task<TextDetails> DetailsAsync(CancellationToken ct)
        {
            var codePage = await CodePageAsync(ct).ConfigureAwait(false);
            int? refCount = null;
            if (RefCountField is var (offset, size) && payload != 0
                && await read(Offset(payload, offset), size, ct).ConfigureAwait(false) is { } bytes)
                refCount = (int)Math.Clamp(ValueFormatter.Signed(bytes), int.MinValue, int.MaxValue);
            var textLength = payload == 0 ? 0 : length ?? await ScanAsync(ct).ConfigureAwait(false);
            return new TextDetails(kind, textLength, refCount, codePage, EncodingName(codePage), elementSize,
                payload == 0 ? null : payload, MaxLength);
        }

        public async Task<TextChunk> ReadAsync(long start, int count, CancellationToken ct)
        {
            if (payload == 0)
                return new TextChunk(start, start, "", true, false);
            // A length the scan of the details found serves too; without one the chunk looks for the terminator.
            var known = length ?? (_scan is { IsCompletedSuccessfully: true, Result: >= 0 and var scanned } ? (long?)scanned : null);
            var end = known ?? bound;
            if (start >= end)
                return new TextChunk(start, start, "", true, false);

            var codePage = await CodePageAsync(ct).ConfigureAwait(false);
            var utf8 = kind == TextKind.Short || (kind == TextKind.Ansi && codePage is null or 0 or 65001);
            var want = (int)Math.Min(count, end - start);
            // One element beyond the chunk (up to 3 bytes for UTF-8) tells whether the chunk would cut a pair or a sequence, or ends at #0.
            var lookahead = (int)Math.Min(utf8 ? 3 : 1, end - start - want);
            var address = unchecked(payload + (ulong)(start * elementSize));
            var bytes = await ReadPrefixAsync(address, (want + lookahead) * elementSize, ct).ConfigureAwait(false);
            var got = bytes.Length / elementSize;
            if (got == 0)
                throw new DebuggerException(DebugErrorCode.MemoryUnreadable, Messages.UnreadableMemory(address), "readMemory");

            var take = Math.Min(got, want);
            var truncated = got < want;
            var atEnd = false;
            if (Terminated && known is null && FirstZero(bytes, got) is var zero and >= 0 && zero <= take)
                (take, truncated, atEnd) = (zero, false, true);
            if (!atEnd && !truncated && start + take < end)
            {
                if (elementSize == 2)
                    take = KeepSurrogatePair(take, got, i => (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2 * i)));
                else if (utf8)
                    take = KeepUtf8Sequence(take, bytes);
                else if (kind == TextKind.Ansi && codePage == 1200 && take % 2 == 1)
                    take = take > 1 ? take - 1 : Math.Min(take + 1, got);   // whole UTF-16 units
            }

            var next = start + take;
            return new TextChunk(start, next, Decode(codePage, bytes[..(take * elementSize)]), atEnd || truncated || next >= end, truncated);
        }

        /// <summary>Index of the first #0 element among the first <paramref name="count"/> elements; -1 for none.</summary>
        private int FirstZero(byte[] bytes, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (bytes[i * elementSize] == 0 && (elementSize == 1 || bytes[i * elementSize + 1] == 0))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// DBG-517: the elements before the first #0, scanned in reads of 64 KiB up to <paramref name="bound"/>
        /// elements; none there: a character array has all its elements, a pointer -1 (also when memory ends first).
        /// </summary>
        private Task<long> ScanAsync(CancellationToken ct) => _scan ??= ScanCoreAsync(ct);

        private async Task<long> ScanCoreAsync(CancellationToken ct)
        {
            for (long position = 0; position < bound;)
            {
                var elements = (int)Math.Min(ScanBytes / elementSize, bound - position);
                var bytes = await ReadPrefixAsync(unchecked(payload + (ulong)(position * elementSize)), elements * elementSize, ct).ConfigureAwait(false);
                var got = bytes.Length / elementSize;
                if (FirstZero(bytes, got) is var zero and >= 0)
                    return position + zero;
                if (got < elements)
                    return -1;
                position += got;
            }

            return kind == TextKind.CharArray ? bound : -1;
        }

        /// <summary>The code page of the text: Ansi the header field (null for <c>nil</c> or unreadable), Unicode 1200, else null.</summary>
        private Task<int?> CodePageAsync(CancellationToken ct) => _codePage ??= CodePageCoreAsync(ct);

        private async Task<int?> CodePageCoreAsync(CancellationToken ct)
        {
            if (kind == TextKind.Unicode)
                return 1200;
            if (CodePageField is not var (offset, size) || payload == 0
                || await read(Offset(payload, offset), size, ct).ConfigureAwait(false) is not { } bytes)
                return null;
            return (int)Math.Min(ValueFormatter.Unsigned(bytes), int.MaxValue);
        }

        /// <summary>DBG-518.</summary>
        private string EncodingName(int? codePage) => kind switch
        {
            TextKind.Short => "UTF-8",
            TextKind.PAnsiChar => "8-bit",
            TextKind.CharArray => elementSize == 2 ? Utf16Name : "8-bit",
            TextKind.Ansi => codePage switch
            {
                null or 0 or 65001 => "UTF-8 (65001)",
                1200 => "UTF-16 (1200)",
                65535 => "raw bytes (65535)",
                1252 => "Windows-1252 (1252)",
                var other => "code page " + other,
            },
            _ => Utf16Name,
        };

        /// <summary>The bytes decoded exactly as the plain rendering decodes the same kind (DBG-161, DBG-518).</summary>
        private string Decode(int? codePage, byte[] bytes) => kind switch
        {
            TextKind.Short => ValueDecoder.Utf8WithLatin1Fallback(bytes),
            TextKind.Ansi => ValueDecoder.DecodeAnsi(codePage ?? 0, bytes),
            _ when elementSize == 2 => ValueDecoder.Utf16(bytes),
            _ => Encoding.Latin1.GetString(bytes),
        };

        /// <summary>
        /// <paramref name="count"/> bytes, or when they are not all readable the readable prefix (found in steps that
        /// end at the memory reader's block boundaries).
        /// </summary>
        private async Task<byte[]> ReadPrefixAsync(ulong address, int count, CancellationToken ct)
        {
            if (count <= 0)
                return [];
            if (await read(address, count, ct).ConfigureAwait(false) is { } all)
                return all;
            var prefix = new List<byte>();
            while (prefix.Count < count)
            {
                var position = address + (ulong)prefix.Count;
                var step = Math.Min(Alignment - (int)(position % Alignment), count - prefix.Count);
                if (await read(position, step, ct).ConfigureAwait(false) is not { } chunk)
                    break;
                prefix.AddRange(chunk);
            }

            return [.. prefix];
        }

        private static ulong Offset(ulong address, long offset) => unchecked(address + (ulong)offset);
    }
}
