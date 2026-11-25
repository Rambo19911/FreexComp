using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace FreexComp.Compiler.Toolchain;

/// <summary>The type or the name of a resource: a number or a text.</summary>
internal readonly record struct ResourceId(int? Number, string? Text)
{
    public static implicit operator ResourceId(int number) => new(number, null);

    public static implicit operator ResourceId(string text) => new(null, text);

    /// <summary>The number in decimal digits, or the text in double quotes.</summary>
    public override string ToString() => Number is { } n ? n.ToString(CultureInfo.InvariantCulture) : "\"" + Text + "\"";
}

/// <summary>One resource of a compiled resource file.</summary>
internal sealed record ResourceEntry(ResourceId Type, ResourceId Name, int Language, int DataLength);

/// <summary>
/// Reader of compiled resource files (32-bit format; resource-compiler.md §7): a sequence of entries — data size, header
/// size, type, name, padding to a 4-byte boundary, format version, memory flags, language, version, characteristics — each
/// followed by its data padded to 4 bytes. The first entry is the empty one. It never changes a file.
/// </summary>
internal static class ResourceFile
{
    /// <summary>The resource type of an application manifest.</summary>
    public const int ManifestType = 24;

    private const int EmptyEntrySize = 32;

    /// <summary>The smallest header: two sizes, a type and a name of one 16-bit unit each, and the 16 fixed bytes.</summary>
    private const int SmallestHeader = 28;

    /// <summary>The entries after the leading empty one.</summary>
    /// <exception cref="InvalidDataException">The data is not a compiled resource file.</exception>
    public static ImmutableArray<ResourceEntry> Read(byte[] data)
    {
        if (data.Length < EmptyEntrySize)
        {
            throw new InvalidDataException("the file is shorter than a resource header");
        }

        var entries = ImmutableArray.CreateBuilder<ResourceEntry>();
        var offset = 0;
        var first = true;
        while (offset < data.Length)
        {
            var left = (uint)(data.Length - offset);
            if (left < 8)
            {
                throw new InvalidDataException("an entry reaches past the end of the file");
            }

            var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            if (headerSize < SmallestHeader)
            {
                throw new InvalidDataException("a header is smaller than its fixed part");
            }

            if (headerSize > left || dataSize > left - headerSize)
            {
                throw new InvalidDataException("an entry reaches past the end of the file");
            }

            var end = offset + (int)headerSize;
            var position = offset + 8;
            var type = ReadId(data, ref position, end);
            var name = ReadId(data, ref position, end);
            position = offset + ((position - offset + 3) & ~3);
            if (end - position < 16)
            {
                throw new InvalidDataException("a header is smaller than its fixed part");
            }

            var language = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position + 6));
            if (first)
            {
                if (dataSize != 0 || headerSize != EmptyEntrySize || type.Number != 0 || name.Number != 0)
                {
                    throw new InvalidDataException("the first entry is not the empty entry");
                }

                first = false;
            }
            else
            {
                entries.Add(new ResourceEntry(type, name, language, (int)dataSize));
            }

            offset = (int)Math.Min((uint)data.Length, (uint)end + ((dataSize + 3) & ~3u));
        }

        return entries.ToImmutable();
    }

    private static ResourceId ReadId(byte[] data, ref int position, int end)
    {
        if (end - position < 2)
        {
            throw new InvalidDataException("a header is smaller than its fixed part");
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position)) == 0xFFFF)
        {
            if (end - position < 4)
            {
                throw new InvalidDataException("a header is smaller than its fixed part");
            }

            position += 4;
            return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position - 2));
        }

        var text = new StringBuilder();
        while (true)
        {
            if (end - position < 2)
            {
                throw new InvalidDataException("a text in a header is not terminated");
            }

            var c = (char)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position));
            position += 2;
            if (c == '\0')
            {
                return text.ToString();
            }

            text.Append(c);
        }
    }
}
