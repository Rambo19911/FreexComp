using System.Collections.Immutable;

namespace FreexComp.Designer.Model;

/// <summary>A property value of a form file (form-document.md §1); immutable (DSG-FD-001).</summary>
public abstract record FormValue;

/// <summary>An integer; <paramref name="Hex"/> = written as <c>$XXXXXXXX</c>.</summary>
public sealed record IntegerValue(long Value, bool Hex) : FormValue;

public sealed record FloatValue(double Value) : FormValue;

public sealed record StringValue(string Value) : FormValue;

/// <summary>Enum member, <c>True</c>/<c>False</c>, <c>nil</c>, component name, colour/cursor constant.</summary>
public sealed record IdentValue(string Name) : FormValue;

/// <summary>A qualified reference such as <c>Form.Component</c>.</summary>
public sealed record QualifiedIdentValue(ImmutableArray<string> Parts) : FormValue
{
    public bool Equals(QualifiedIdentValue? other) => other is not null && Parts.SequenceEqual(other.Parts);

    public override int GetHashCode() => Parts.Aggregate(0, (h, p) => HashCode.Combine(h, p));
}

/// <summary>A set; elements in declared order as read.</summary>
public sealed record SetValue(ImmutableArray<string> Elements) : FormValue
{
    public bool Equals(SetValue? other) => other is not null && Elements.SequenceEqual(other.Elements);

    public override int GetHashCode() => Elements.Aggregate(0, (h, e) => HashCode.Combine(h, e));
}

public sealed record StringListValue(ImmutableArray<string> Lines) : FormValue
{
    public bool Equals(StringListValue? other) => other is not null && Lines.SequenceEqual(other.Lines);

    public override int GetHashCode() => Lines.Aggregate(0, (h, l) => HashCode.Combine(h, l));
}

public sealed record BinaryValue(ImmutableArray<byte> Bytes) : FormValue
{
    public bool Equals(BinaryValue? other) => other is not null && Bytes.SequenceEqual(other.Bytes);

    public override int GetHashCode() => Bytes.Aggregate(0, (h, b) => HashCode.Combine(h, b));
}

public sealed record CollectionValue(ImmutableArray<CollectionItem> Items) : FormValue
{
    public bool Equals(CollectionValue? other) => other is not null && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => Items.Length;
}

/// <summary>One <c>item … end</c> block of a collection.</summary>
public sealed record CollectionItem(ImmutableArray<(string Path, FormValue Value, string? RawText)> Properties)
{
    public bool Equals(CollectionItem? other) => other is not null && Properties.SequenceEqual(other.Properties);

    public override int GetHashCode() => Properties.Length;
}

/// <summary>The value text of a preserved property that does not fit its type.</summary>
public sealed record RawValue(string Text) : FormValue;
