using System.Text;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Values;

/// <summary>
/// The one decoding path (DBG-150): bytes of debuggee memory + a type of the type information → <see cref="ValueNode"/>.
/// Every layout constant comes from the <c>abi</c> block of the type information. Unreadable memory turns the node
/// that needed it into an <see cref="ValueKind.Error"/> node, never its siblings (DBG-152).
/// </summary>
internal sealed class ValueDecoder
{
    /// <summary>Largest plausible string length (DBG-159).</summary>
    private const long MaxStringLength = 268_435_455;

    /// <summary>Largest plausible payload of a dynamic array in bytes (DBG-163).</summary>
    private const long MaxArrayBytes = 1L << 40;

    private const int MaxClassNameChars = 255;
    private const int MaxAncestorDepth = 64;
    private const int MaxClassChain = 320;      // guards against a cyclic ancestor chain in damaged type information
    private const int ChunkAlignment = 256;

    private readonly PdiFile _pdi;
    private readonly IDebuggeeAccess _memory;
    private readonly ValueFormatOptions _options;
    private readonly int _pointerSize;

    static ValueDecoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public ValueDecoder(PdiFile pdi, IDebuggeeAccess memory, ValueFormatOptions options)
    {
        _pdi = pdi;
        _memory = memory;
        _options = options;
        _pointerSize = pdi.Abi.PointerSize;
    }

    /// <summary>Decodes the value of <paramref name="type"/> stored at <paramref name="address"/>.</summary>
    /// <param name="name">Display name of the node.</param>
    /// <param name="type">Static type of the location.</param>
    /// <param name="address">Address of the variable's storage.</param>
    /// <param name="evaluateName">Expression for this location, or null.</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="openArrayHigh">For an open-array parameter: the value of its hidden upper-bound variable (DBG-164).</param>
    public Task<ValueNode> DecodeAsync(
        string name, PdiType type, ulong address, string? evaluateName, CancellationToken ct, long? openArrayHigh = null) =>
        DecodeAsync(name, type, address, evaluateName, nested: false, openArrayHigh, ct);

    /// <summary>
    /// Children <paramref name="start"/> … of a node, in declaration or index order (DBG-411): <paramref name="count"/>
    /// of them, or with 0 up to <see cref="ValueFormatOptions.ChildPageSize"/>.
    /// </summary>
    public async Task<IReadOnlyList<ValueNode>> GetChildrenAsync(ValueNode node, int start, int count, CancellationToken ct)
    {
        if (node.ChildAt is not { } childAt)
            return [];
        start = Math.Max(start, 0);
        var end = (int)Math.Min(node.ChildCount, (long)start + (count <= 0 ? Math.Max(_options.ChildPageSize, 1) : count));
        var children = new List<ValueNode>(Math.Max(end - start, 0));
        for (var index = start; index < end; index++)
            children.Add(await childAt(index, ct).ConfigureAwait(false));
        return children;
    }

    private async Task<ValueNode> DecodeAsync(
        string name, PdiType type, ulong address, string? evaluateName, bool nested, long? openArrayHigh, CancellationToken ct)
    {
        ValueNode Node(ValueKind kind, byte[] bytes) => new()
        {
            Name = name, EvaluateName = evaluateName, Kind = kind, Type = type, TypeName = ValueFormatter.TypeName(type),
            Address = address, Bytes = bytes,
        };

        ValueNode Failed(string text) => new()
        {
            Name = name, EvaluateName = evaluateName, Kind = ValueKind.Error, Type = type, TypeName = ValueFormatter.TypeName(type),
            Address = address, Error = text,
        };

        // Records and static arrays have no storage of their own to read: their parts are read when decoded.
        if (type.Kind == PdiTypeKind.Record)
        {
            var record = Node(ValueKind.Record, []);
            return await WithFieldsAsync(record, type.Fields, address, nested, ct).ConfigureAwait(false);
        }

        if (type.Kind == PdiTypeKind.StaticArray)
            return await ArrayAsync(Node(ValueKind.StaticArray, []), type, address, type.Count, nested, ct).ConfigureAwait(false);

        var size = type.Kind switch
        {
            PdiTypeKind.Opaque => Math.Min(type.Size, 17),
            PdiTypeKind.Procedural => type.Method ? 2 * _pointerSize : _pointerSize,
            PdiTypeKind.String when type.Flavor != PdiStringFlavor.Short => _pointerSize,
            PdiTypeKind.DynArray or PdiTypeKind.OpenArray or PdiTypeKind.Class or PdiTypeKind.ClassRef or PdiTypeKind.Pointer
                or PdiTypeKind.Interface => _pointerSize,
            _ => type.Size,
        };
        var bytes = await _memory.ReadMemoryAsync(address, size, ct).ConfigureAwait(false);
        if (bytes is null)
            return Failed(Messages.UnreadableMemory(address));
        var pointer = ValueFormatter.Unsigned(bytes.AsSpan(0, Math.Min(bytes.Length, _pointerSize)));

        switch (type.Kind)
        {
            case PdiTypeKind.Integer:
                return Node(ValueKind.Integer, bytes);
            case PdiTypeKind.Float:
                return Node(ValueKind.Float, bytes);
            case PdiTypeKind.Boolean:
                return Node(ValueKind.Boolean, bytes);
            case PdiTypeKind.Char:
                return Node(ValueKind.Char, bytes);
            case PdiTypeKind.Enum:
                return Node(ValueKind.Enum, bytes);
            case PdiTypeKind.Set:
                return Node(ValueKind.Set, bytes);

            case PdiTypeKind.String:
            {
                var text = type.Flavor switch
                {
                    PdiStringFlavor.Unicode => await UnicodeStringAsync(pointer, _options.MaxStringChars, ct).ConfigureAwait(false),
                    PdiStringFlavor.Ansi => await AnsiStringAsync(pointer, ct).ConfigureAwait(false),
                    PdiStringFlavor.Short => ShortString(bytes, type.MaxLength),
                    PdiStringFlavor.PChar => await TerminatedStringAsync(pointer, 2, ct).ConfigureAwait(false),
                    _ => await TerminatedStringAsync(pointer, 1, ct).ConfigureAwait(false),
                };
                if (text.Error is not null)
                    return Failed(text.Error);
                return new ValueNode
                {
                    Name = name, EvaluateName = evaluateName, Kind = ValueKind.String, Type = type, TypeName = ValueFormatter.TypeName(type),
                    Address = address, Bytes = bytes, Pointer = type.Flavor == PdiStringFlavor.Short ? 0 : pointer,
                    Text = text.Units, Length = text.Length, Partial = text.Partial, Capped = text.Capped,
                };
            }

            case PdiTypeKind.DynArray:
            {
                long length = 0;
                if (pointer != 0)
                {
                    var abi = _pdi.Abi.DynArray;
                    var header = await _memory.ReadMemoryAsync(Offset(pointer, abi.LengthOffset), abi.LengthSize, ct).ConfigureAwait(false);
                    if (header is null)
                        return Failed(Messages.UnreadableMemory(Offset(pointer, abi.LengthOffset)));
                    length = ValueFormatter.Signed(header);
                    if (length < 0 || length > MaxArrayBytes / Math.Max(type.Element!.Size, 1))
                        return Failed(Messages.InvalidArray(pointer));
                }

                var array = new ValueNode
                {
                    Name = name, EvaluateName = evaluateName, Kind = ValueKind.DynamicArray, Type = type,
                    TypeName = ValueFormatter.TypeName(type), Address = address, Bytes = bytes, Pointer = pointer,
                };
                return await ArrayAsync(array, type, pointer, length, nested, ct).ConfigureAwait(false);
            }

            case PdiTypeKind.OpenArray:
            {
                // The element count is the hidden upper bound + 1; without it the array cannot be shown.
                if (openArrayHigh is not { } high)
                    return Failed(Messages.NotAvailable);
                var length = pointer == 0 ? 0 : Math.Max(high + 1, 0);
                if (length > MaxArrayBytes / Math.Max(type.Element!.Size, 1))
                    return Failed(Messages.InvalidArray(pointer));
                var array = new ValueNode
                {
                    Name = name, EvaluateName = evaluateName, Kind = ValueKind.DynamicArray, Type = type,
                    TypeName = ValueFormatter.TypeName(type), Address = address, Bytes = bytes, Pointer = pointer,
                };
                return await ArrayAsync(array, type, pointer, length, nested, ct).ConfigureAwait(false);
            }

            case PdiTypeKind.Class:
                return await InstanceAsync(name, type, address, evaluateName, bytes, pointer, nested, ct).ConfigureAwait(false);

            case PdiTypeKind.ClassRef:
            {
                string? className = null;
                if (pointer != 0 && (className = await ClassNameAsync(pointer, ct).ConfigureAwait(false)) is null)
                    return Failed(Messages.UnreadableMemory(pointer));
                return new ValueNode
                {
                    Name = name, EvaluateName = evaluateName, Kind = ValueKind.ClassReference, Type = type,
                    TypeName = ValueFormatter.TypeName(type), Address = address, Bytes = bytes, Pointer = pointer, Text = className,
                };
            }

            case PdiTypeKind.Pointer:
            {
                // A typed pointer has one child, its target, decoded when asked for (DBG-172).
                var target = type.To;
                var targetName = evaluateName is null ? null : evaluateName + "^";
                return new ValueNode
                {
                    Name = name, EvaluateName = evaluateName, Kind = ValueKind.Pointer, Type = type, TypeName = ValueFormatter.TypeName(type),
                    Address = address, Bytes = bytes, Pointer = pointer,
                    ChildCount = target is not null && pointer != 0 ? 1 : 0,
                    ChildAt = target is not null && pointer != 0
                        ? (_, token) => DecodeAsync("^", target, pointer, targetName, nested: false, null, token)
                        : null,
                };
            }

            case PdiTypeKind.Interface:
                return new ValueNode
                {
                    Name = name, EvaluateName = evaluateName, Kind = ValueKind.Interface, Type = type, TypeName = ValueFormatter.TypeName(type),
                    Address = address, Bytes = bytes, Pointer = pointer,
                };

            case PdiTypeKind.Procedural:
                return new ValueNode
                {
                    Name = name, EvaluateName = evaluateName, Kind = ValueKind.Procedural, Type = type, TypeName = ValueFormatter.TypeName(type),
                    Address = address, Bytes = bytes, Pointer = pointer,
                };

            default:
                return Node(ValueKind.Raw, bytes);
        }
    }

    // ---- records and class instances ----

    /// <summary>Gives a record or instance node its fields: lazily decoded children and, unless nested, the summary's first ones.</summary>
    private async Task<ValueNode> WithFieldsAsync(
        ValueNode node, IReadOnlyList<PdiField> fields, ulong baseAddress, bool nested, CancellationToken ct, bool summary = true)
    {
        Task<ValueNode> FieldAsync(int index, bool nestedField, CancellationToken token)
        {
            var field = fields[index];
            var fieldName = node.EvaluateName is null ? null : node.EvaluateName + "." + field.Name;
            return DecodeAsync(field.Name, field.Type, Offset(baseAddress, field.Offset), fieldName, nestedField, null, token);
        }

        var first = new List<ValueNode>();
        if (!nested && summary)
        {
            for (var i = 0; i < Math.Min(fields.Count, Math.Max(_options.MaxSummaryElements, 0)); i++)
                first.Add(await FieldAsync(i, true, ct).ConfigureAwait(false));
        }

        return new ValueNode
        {
            Name = node.Name, EvaluateName = node.EvaluateName, Kind = node.Kind, Type = node.Type, TypeName = node.TypeName,
            Address = node.Address, Bytes = node.Bytes, Pointer = node.Pointer, Text = node.Text, InvalidInstance = node.InvalidInstance,
            ChildCount = fields.Count,
            SummaryChildren = first,
            ChildAt = fields.Count == 0 ? null : (index, token) => FieldAsync(index, false, token),
        };
    }

    private async Task<ValueNode> InstanceAsync(
        string name, PdiType staticType, ulong address, string? evaluateName, byte[] bytes, ulong instance, bool nested, CancellationToken ct)
    {
        if (instance == 0)
        {
            // nil: a leaf of the static class (DBG-167).
            return new ValueNode
            {
                Name = name, EvaluateName = evaluateName, Kind = ValueKind.ClassInstance, Type = staticType,
                TypeName = ValueFormatter.TypeName(staticType), Address = address, Bytes = bytes,
            };
        }

        // DBG-168: the class named by the instance's VMT; unknown (built without type information) → the nearest
        // known ancestor supplies the fields, the name shown stays the real one.
        string? className = null;
        PdiType? dynamicType = null;
        var vmtBytes = await _memory.ReadMemoryAsync(instance, _pointerSize, ct).ConfigureAwait(false);
        if (vmtBytes is not null)
        {
            var vmt = ValueFormatter.Unsigned(vmtBytes);
            className = await ClassNameAsync(vmt, ct).ConfigureAwait(false);
            if (className is not null)
                dynamicType = _pdi.FindClass(className, staticType);
            for (var depth = 0; className is not null && dynamicType is null && depth < MaxAncestorDepth; depth++)
            {
                if (await ParentVmtAsync(vmt, ct).ConfigureAwait(false) is not { } parent || parent == 0
                    || await ClassNameAsync(parent, ct).ConfigureAwait(false) is not { } parentName)
                    break;
                vmt = parent;
                dynamicType = _pdi.FindClass(parentName, staticType);
            }
        }

        var invalid = dynamicType is null;
        var type = dynamicType ?? staticType;
        var shownName = invalid ? ValueFormatter.TypeName(staticType) : className!;
        var node = new ValueNode
        {
            Name = name, EvaluateName = evaluateName, Kind = ValueKind.ClassInstance, Type = type, TypeName = shownName,
            Address = address, Bytes = bytes, Pointer = instance, Text = shownName, InvalidInstance = invalid,
        };
        // The value of an instance is its class and address: no field is read for the summary.
        return await WithFieldsAsync(node, type.AllFields(), instance, nested, ct, summary: false).ConfigureAwait(false);
    }

    /// <summary>
    /// The dynamic class name of the exception object at <paramref name="instance"/> and the text of its message: the
    /// string field named <c>abi.exceptionMessageField</c> of the dynamic class or its ancestors (the nearest known
    /// ancestor when the class itself has no type information). What cannot be read is "".
    /// <c>Classes</c> is the dynamic class followed by its ancestors as far as they are known (DBG-244): from the type
    /// information; for a class it does not describe, from the parent pointers of the VMTs up to the first described
    /// class and on from there. Where neither goes on the chain ends, so at least it holds the class itself; it is
    /// empty when the class name cannot be read.
    /// </summary>
    public async Task<(string ClassName, string Message, IReadOnlyList<string> Classes)> ReadExceptionAsync(ulong instance, CancellationToken ct)
    {
        if (await _memory.ReadMemoryAsync(instance, _pointerSize, ct).ConfigureAwait(false) is not { } vmtBytes)
            return ("", "", []);
        var vmt = ValueFormatter.Unsigned(vmtBytes);
        if (await ClassNameAsync(vmt, ct).ConfigureAwait(false) is not { } className)
            return ("", "", []);

        var classes = new List<string> { className };
        var type = _pdi.FindClass(className, null);
        for (var depth = 0; type is null && depth < MaxAncestorDepth; depth++)
        {
            if (await ParentVmtAsync(vmt, ct).ConfigureAwait(false) is not { } parent || parent == 0
                || await ClassNameAsync(parent, ct).ConfigureAwait(false) is not { } parentName)
                break;
            vmt = parent;
            classes.Add(parentName);
            type = _pdi.FindClass(parentName, null);
        }

        for (var ancestor = type?.Ancestor; ancestor is not null && classes.Count < MaxClassChain; ancestor = ancestor.Ancestor)
            classes.Add(ancestor.Name);

        var field = type?.AllFields().LastOrDefault(f =>
            f.Type.Kind == PdiTypeKind.String && string.Equals(f.Name, _pdi.Abi.ExceptionMessageField, StringComparison.OrdinalIgnoreCase));
        if (field is null)
            return (className, "", classes);
        var message = await DecodeAsync(field.Name, field.Type, Offset(instance, field.Offset), null, ct).ConfigureAwait(false);
        return (className, message.Kind == ValueKind.String ? message.Text ?? "" : "", classes);
    }

    /// <summary>The class name stored in a VMT (<c>abi.vmt</c>); null when it cannot be read.</summary>
    private async Task<string?> ClassNameAsync(ulong vmt, CancellationToken ct)
    {
        var abi = _pdi.Abi.Vmt;
        var location = Offset(vmt, abi.ClassNameOffset);
        if (abi.ClassNameKind != PdiClassNameKind.InlineShortString)
        {
            if (await _memory.ReadMemoryAsync(location, _pointerSize, ct).ConfigureAwait(false) is not { } pointerBytes)
                return null;
            location = ValueFormatter.Unsigned(pointerBytes);
            if (location == 0)
                return null;
        }

        if (abi.ClassNameKind == PdiClassNameKind.PointerToUnicodeString)
        {
            var text = await UnicodeStringAsync(location, MaxClassNameChars, ct).ConfigureAwait(false);
            return text.Error is null && !text.Partial && text.Units.Length > 0 ? text.Units : null;
        }

        // A length byte followed by that many bytes of UTF-8.
        if (await _memory.ReadMemoryAsync(location, 1, ct).ConfigureAwait(false) is not { } lengthByte || lengthByte[0] == 0)
            return null;
        return await _memory.ReadMemoryAsync(location + 1, lengthByte[0], ct).ConfigureAwait(false) is { } nameBytes
            ? Utf8WithLatin1Fallback(nameBytes)
            : null;
    }

    private async Task<ulong?> ParentVmtAsync(ulong vmt, CancellationToken ct)
    {
        var abi = _pdi.Abi.Vmt;
        if (await _memory.ReadMemoryAsync(Offset(vmt, abi.ParentOffset), _pointerSize, ct).ConfigureAwait(false) is not { } bytes)
            return null;
        var parent = ValueFormatter.Unsigned(bytes);
        if (abi.ParentKind == PdiParentKind.PointerToVmt || parent == 0)
            return parent;
        return await _memory.ReadMemoryAsync(parent, _pointerSize, ct).ConfigureAwait(false) is { } indirect
            ? ValueFormatter.Unsigned(indirect)
            : null;
    }

    // ---- arrays ----

    /// <summary>
    /// Completes an array node: <paramref name="count"/> elements of the element size starting at
    /// <paramref name="data"/>. A character array also carries its text up to the first #0 (DBG-165).
    /// </summary>
    private async Task<ValueNode> ArrayAsync(ValueNode node, PdiType type, ulong data, long count, bool nested, CancellationToken ct)
    {
        var element = type.Element!;
        var isStatic = type.Kind == PdiTypeKind.StaticArray;
        var low = isStatic ? type.ArrayLow : 0;
        var indexType = isStatic ? type.IndexType : null;

        Task<ValueNode> ElementAsync(long index, bool nestedElement, CancellationToken token)
        {
            var indexName = ValueFormatter.IndexName(indexType, low + index);
            return DecodeAsync(
                "[" + indexName + "]", element, data + (ulong)(index * element.Size),
                node.EvaluateName is null ? null : node.EvaluateName + "[" + indexName + "]", nestedElement, null, token);
        }

        string? text = null;
        var capped = false;
        var partial = false;
        var first = new List<ValueNode>();
        if (element.Kind == PdiTypeKind.Char && element.Size is 1 or 2)
        {
            // Shown as text: the units up to the first #0.
            var limit = (int)Math.Min(count, Math.Max(_options.MaxStringChars, 0));
            var (bytes, complete) = await ReadPrefixAsync(data, limit * element.Size, ct).ConfigureAwait(false);
            var units = element.Size == 2 ? Utf16(bytes) : Encoding.Latin1.GetString(bytes);
            var terminator = units.IndexOf('\0');
            text = terminator >= 0 ? units[..terminator] : units;
            partial = terminator < 0 && !complete;
            capped = terminator < 0 && complete && count > limit;
        }
        else if (!nested)
        {
            for (var i = 0; i < Math.Min(count, Math.Max(_options.MaxSummaryElements, 0)); i++)
                first.Add(await ElementAsync(i, true, ct).ConfigureAwait(false));
        }

        return new ValueNode
        {
            Name = node.Name, EvaluateName = node.EvaluateName, Kind = node.Kind, Type = node.Type, TypeName = node.TypeName,
            Address = node.Address, Bytes = node.Bytes, Pointer = node.Pointer,
            Text = text, Length = count, Partial = partial, Capped = capped,
            ChildCount = (int)Math.Min(count, int.MaxValue),
            SummaryChildren = first,
            ChildAt = count == 0 ? null : (index, token) => ElementAsync(index, false, token),
        };
    }

    // ---- strings ----

    /// <summary>Text read for a string: code units, declared length (-1 unknown), flags; or the text of an error node.</summary>
    private readonly record struct StringText(string Units, long Length, bool Partial, bool Capped, string? Error)
    {
        public static StringText Failed(string error) => new("", 0, false, false, error);
    }

    /// <summary>DBG-159: the payload pointer's length field, then at most <paramref name="maxChars"/> code units.</summary>
    private async Task<StringText> UnicodeStringAsync(ulong payload, int maxChars, CancellationToken ct)
    {
        if (payload == 0)
            return new StringText("", 0, false, false, null);
        var abi = _pdi.Abi.UnicodeString;
        var header = await _memory.ReadMemoryAsync(Offset(payload, abi.LengthOffset), abi.LengthSize, ct).ConfigureAwait(false);
        if (header is null)
            return StringText.Failed(Messages.UnreadableMemory(Offset(payload, abi.LengthOffset)));
        var length = ValueFormatter.Signed(header);
        if (abi.LengthInBytes)
            length /= abi.CharSize;
        if (length is < 0 or > MaxStringLength)
            return StringText.Failed(Messages.InvalidString(payload));

        var shown = (int)Math.Min(length, Math.Max(maxChars, 0));
        var (bytes, complete) = await ReadPrefixAsync(payload, shown * abi.CharSize, ct).ConfigureAwait(false);
        if (!complete && bytes.Length < abi.CharSize)
            return StringText.Failed(Messages.UnreadableMemory(payload));
        return new StringText(Utf16(bytes), length, !complete, length > shown, null);
    }

    /// <summary>DBG-161 <c>ansi</c>: length in bytes and code page from the header.</summary>
    private async Task<StringText> AnsiStringAsync(ulong payload, CancellationToken ct)
    {
        if (payload == 0)
            return new StringText("", 0, false, false, null);
        var abi = _pdi.Abi.AnsiString;
        var header = await _memory.ReadMemoryAsync(Offset(payload, abi.LengthOffset), abi.LengthSize, ct).ConfigureAwait(false);
        if (header is null)
            return StringText.Failed(Messages.UnreadableMemory(Offset(payload, abi.LengthOffset)));
        var length = ValueFormatter.Signed(header);
        if (length is < 0 or > MaxStringLength)
            return StringText.Failed(Messages.InvalidString(payload));
        var codePageBytes = await _memory.ReadMemoryAsync(Offset(payload, abi.CodePageOffset), abi.CodePageSize, ct).ConfigureAwait(false);
        var codePage = codePageBytes is null ? 0 : (int)Math.Min(ValueFormatter.Unsigned(codePageBytes), int.MaxValue);

        // A character takes at most four bytes; the formatter cuts the text to the display limit.
        var maxChars = Math.Max(_options.MaxStringChars, 0);
        var wanted = (int)Math.Min(length, 4L * maxChars);
        var (bytes, complete) = await ReadPrefixAsync(payload, wanted, ct).ConfigureAwait(false);
        if (!complete && bytes.Length == 0)
            return StringText.Failed(Messages.UnreadableMemory(payload));
        return new StringText(DecodeAnsi(codePage, bytes), length, !complete, wanted < length, null);
    }

    /// <summary>DBG-161: the bytes of a byte string in its code page.</summary>
    internal static string DecodeAnsi(int codePage, byte[] bytes) =>
        codePage == 1200 ? Utf16(bytes)
        : codePage == 1252 ? DecodeCodePage1252(bytes)
        : AnsiEncoding(codePage).GetString(bytes);

    // Code page 1252, bytes 0x80 ... 0x9F; the five bytes the code page leaves undefined are the C1 controls.
    private const string CodePage1252High =
        "\u20AC\u0081\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152\u008D\u017D\u008F"
        + "\u0090\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153\u009D\u017E\u0178";

    /// <summary>
    /// DBG-161: text in code page 1252, the usual ANSI code page, decoded with the decoder's own table: it differs
    /// from Latin-1 in 0x80 ... 0x9F only and must not depend on a code-page provider being available.
    /// </summary>
    internal static string DecodeCodePage1252(ReadOnlySpan<byte> bytes)
    {
        var units = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
            units[i] = bytes[i] is >= 0x80 and < 0xA0 ? CodePage1252High[bytes[i] - 0x80] : (char)bytes[i];
        return new string(units);
    }

    private static Encoding AnsiEncoding(int codePage)
    {
        if (codePage is 0 or 65001)
            return Encoding.UTF8;
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1;
        }
    }

    /// <summary>DBG-161 <c>short</c>: a length byte (clamped to the declared maximum) and that many bytes of UTF-8.</summary>
    private static StringText ShortString(byte[] storage, int maxLength)
    {
        if (storage.Length == 0)
            return new StringText("", 0, false, false, null);
        var length = Math.Min(Math.Min(storage[0], maxLength), storage.Length - 1);
        return new StringText(Utf8WithLatin1Fallback(storage.AsSpan(1, length)), length, false, false, null);
    }

    /// <summary>
    /// DBG-161 <c>pchar</c> / <c>pansichar</c>: units up to a #0, read in steps that end at 256-byte boundaries, at
    /// most the display limit.
    /// </summary>
    private async Task<StringText> TerminatedStringAsync(ulong pointer, int unitSize, CancellationToken ct)
    {
        if (pointer == 0)
            return new StringText("", -1, false, false, null);
        var maxUnits = Math.Max(_options.MaxStringChars, 0);
        var buffer = new List<byte>();
        var position = pointer;
        var terminated = false;
        var partial = false;
        while (!terminated && buffer.Count / unitSize < maxUnits)
        {
            var step = ChunkAlignment - (int)(position % ChunkAlignment);
            step += step % unitSize;   // whole units
            if (await _memory.ReadMemoryAsync(position, step, ct).ConfigureAwait(false) is not { } chunk)
            {
                partial = true;
                break;
            }

            for (var i = 0; i + unitSize <= chunk.Length && !terminated && buffer.Count / unitSize < maxUnits; i += unitSize)
            {
                terminated = chunk[i] == 0 && (unitSize == 1 || chunk[i + 1] == 0);
                if (!terminated)
                    buffer.AddRange(chunk.AsSpan(i, unitSize));
            }

            position += (ulong)step;
        }

        if (partial && buffer.Count == 0)
            return StringText.Failed(Messages.UnreadableMemory(pointer));
        var bytes = buffer.ToArray();
        return new StringText(unitSize == 2 ? Utf16(bytes) : Encoding.Latin1.GetString(bytes), -1, partial, !terminated && !partial, null);
    }

    /// <summary>UTF-16LE code units as they are: unpaired surrogates are kept (a trailing odd byte is dropped).</summary>
    internal static string Utf16(byte[] bytes) =>
        string.Create(bytes.Length / 2, bytes, static (units, source) =>
        {
            for (var i = 0; i < units.Length; i++)
                units[i] = (char)(source[2 * i] | source[2 * i + 1] << 8);
        });

    /// <summary>UTF-8 where it is valid; every other byte as the Latin-1 character of the same code.</summary>
    internal static string Utf8WithLatin1Fallback(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder(bytes.Length);
        while (!bytes.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(bytes, out var rune, out var consumed) == System.Buffers.OperationStatus.Done)
            {
                result.Append(rune.ToString());
                bytes = bytes[consumed..];
            }
            else
            {
                result.Append((char)bytes[0]);
                bytes = bytes[1..];
            }
        }

        return result.ToString();
    }

    // ---- memory ----

    /// <summary>
    /// <paramref name="count"/> bytes, or when they are not all readable the readable prefix (found in steps that end
    /// at 256-byte boundaries).
    /// </summary>
    private async Task<(byte[] Bytes, bool Complete)> ReadPrefixAsync(ulong address, int count, CancellationToken ct)
    {
        if (count <= 0)
            return ([], true);
        if (await _memory.ReadMemoryAsync(address, count, ct).ConfigureAwait(false) is { } all)
            return (all, true);
        var prefix = new List<byte>();
        while (prefix.Count < count)
        {
            var position = address + (ulong)prefix.Count;
            var step = Math.Min(ChunkAlignment - (int)(position % ChunkAlignment), count - prefix.Count);
            if (await _memory.ReadMemoryAsync(position, step, ct).ConfigureAwait(false) is not { } chunk)
                break;
            prefix.AddRange(chunk);
        }

        return (prefix.ToArray(), false);
    }

    /// <summary>An address plus a signed byte offset (header fields lie before the payload).</summary>
    private static ulong Offset(ulong address, long offset) => unchecked(address + (ulong)offset);
}
