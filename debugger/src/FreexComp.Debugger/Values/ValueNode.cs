using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Values;

/// <summary>
/// A decoded value: what <see cref="ValueDecoder"/> read from debuggee memory for one location, before it is
/// rendered by <see cref="ValueFormatter"/> (value pipeline, DBG-150). Children are decoded on demand.
/// </summary>
internal sealed class ValueNode
{
    public required string Name { get; init; }

    /// <summary>Expression that evaluates to this location (DBG-175); null when there is none.</summary>
    public string? EvaluateName { get; init; }

    public required ValueKind Kind { get; init; }

    /// <summary>The type the value was decoded with; for a class instance the dynamic class (or its known ancestor).</summary>
    public required PdiType Type { get; init; }

    public required string TypeName { get; init; }

    /// <summary>Address of the variable's storage (for strings and arrays not of the payload).</summary>
    public ulong? Address { get; init; }

    /// <summary>The storage bytes of scalars, sets, pointers and inline strings; empty for records and static arrays.</summary>
    public byte[] Bytes { get; init; } = [];

    /// <summary>The value text of a node of kind <see cref="ValueKind.Error"/>.</summary>
    public string? Error { get; init; }

    /// <summary>The pointer stored in the variable, for pointer-like storage (strings, dynamic arrays, instances …).</summary>
    public ulong Pointer { get; init; }

    /// <summary>UTF-16 code units read for a string or a character array; the class name of an instance or class reference.</summary>
    public string? Text { get; init; }

    /// <summary>Declared length of a string, element count of an array; -1 when unknown (zero-terminated strings).</summary>
    public long Length { get; init; }

    /// <summary>Only a prefix of the text could be read from memory.</summary>
    public bool Partial { get; init; }

    /// <summary>Reading stopped at the display limit before the end of the text.</summary>
    public bool Capped { get; init; }

    /// <summary>A class instance whose class could not be determined (DBG-168).</summary>
    public bool InvalidInstance { get; init; }

    public int ChildCount { get; init; }

    /// <summary>The first children, decoded for the one-line summary of an array or record; empty for a nested node.</summary>
    public IReadOnlyList<ValueNode> SummaryChildren { get; init; } = [];

    /// <summary>Decodes the child with the given index; null for a leaf.</summary>
    internal Func<int, CancellationToken, Task<ValueNode>>? ChildAt { get; init; }

    /// <summary>The same value under another name and expression (a visualiser's child, visualizers.md DBG-506).</summary>
    public ValueNode Renamed(string name, string? evaluateName) => new()
    {
        Name = name, EvaluateName = evaluateName, Kind = Kind, Type = Type, TypeName = TypeName, Address = Address, Bytes = Bytes,
        Error = Error, Pointer = Pointer, Text = Text, Length = Length, Partial = Partial, Capped = Capped,
        InvalidInstance = InvalidInstance, ChildCount = ChildCount, SummaryChildren = SummaryChildren, ChildAt = ChildAt,
    };

    /// <summary>DBG-176, the part that does not depend on the adapter: a scalar or pointer with a known address.</summary>
    public bool IsModifiableKind =>
        Address is not null && Kind is ValueKind.Integer or ValueKind.Float or ValueKind.Boolean or ValueKind.Char or ValueKind.Enum or ValueKind.Pointer;
}
