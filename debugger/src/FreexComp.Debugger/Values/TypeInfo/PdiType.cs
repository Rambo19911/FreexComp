namespace FreexComp.Debugger.Values.TypeInfo;

/// <summary>Type kinds of the type-information file (interface document §4).</summary>
internal enum PdiTypeKind
{
    Integer, Float, Boolean, Char, Enum, Set, String, StaticArray, DynArray, OpenArray, Record, Class, ClassRef,
    Pointer, Interface, Procedural, Opaque
}

internal enum PdiFloatFormat { Single, Double, Currency, DateTime }

internal enum PdiStringFlavor { Unicode, Ansi, Short, PChar, PAnsiChar }

/// <summary>A named ordinal of an enumeration.</summary>
internal sealed record PdiEnumValue(string Name, long Value);

/// <summary>A field of a record or of a class (for a class: declared by that class only).</summary>
internal sealed class PdiField
{
    public required string Name { get; init; }
    public required long Offset { get; init; }
    public string? Visibility { get; init; }

    /// <summary>Linked by the loader.</summary>
    public PdiType Type { get; internal set; } = null!;
}

/// <summary>
/// One entry of <c>types[]</c>. References to other types are linked objects (the loader resolves the ids and
/// rejects a file with a dangling id).
/// </summary>
internal sealed class PdiType
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? Unit { get; init; }
    public required PdiTypeKind Kind { get; init; }

    /// <summary>Bytes of a variable of this type.</summary>
    public required int Size { get; init; }

    // integer
    public bool Signed { get; init; }
    public long? Low { get; init; }
    public long? High { get; init; }

    // float / string
    public PdiFloatFormat FloatFormat { get; init; }
    public PdiStringFlavor Flavor { get; init; }
    public int MaxLength { get; init; }

    // enum
    public IReadOnlyList<PdiEnumValue> Values { get; init; } = [];

    // set
    public long BitBase { get; init; }

    // static array: first index and element count
    public long ArrayLow { get; init; }
    public long Count { get; init; }

    // record / class
    public IReadOnlyList<PdiField> Fields { get; init; } = [];
    public int InstanceSize { get; init; }

    // procedural
    public bool Method { get; init; }

    // Linked references (null when the member is absent or null in the file).
    /// <summary>Set: the element type.</summary>
    public PdiType? Base { get; internal set; }
    /// <summary>Arrays: the element type.</summary>
    public PdiType? Element { get; internal set; }
    /// <summary>Static array: the enum or char type of a non-integer index.</summary>
    public PdiType? IndexType { get; internal set; }
    /// <summary>Class: the ancestor class.</summary>
    public PdiType? Ancestor { get; internal set; }
    /// <summary>Class reference: the class.</summary>
    public PdiType? Of { get; internal set; }
    /// <summary>Pointer: the target type; null for an untyped pointer.</summary>
    public PdiType? To { get; internal set; }
    /// <summary>Plain alias: the aliased entry.</summary>
    public PdiType? AliasOf { get; internal set; }

    /// <summary>All fields of a class including inherited ones, root class first, each class in declaration order.</summary>
    public IReadOnlyList<PdiField> AllFields()
    {
        var chain = new List<PdiType>();
        for (var type = this; type is not null && chain.Count < 256; type = type.Ancestor)
            chain.Add(type);
        chain.Reverse();
        return [.. chain.SelectMany(type => type.Fields)];
    }

    /// <summary>True when this class is <paramref name="other"/> or descends from it.</summary>
    public bool IsOrDescendsFrom(PdiType other)
    {
        var depth = 0;
        for (var type = this; type is not null && depth < 256; type = type.Ancestor, depth++)
        {
            if (ReferenceEquals(type, other))
                return true;
        }

        return false;
    }
}
