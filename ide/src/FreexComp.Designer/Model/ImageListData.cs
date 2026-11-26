using System.Buffers.Binary;
using System.Collections.Immutable;

namespace FreexComp.Designer.Model;

/// <summary>Why a <c>Bitmap</c> value of an image list could not be read (UIF-1473, graphics.md UIF-544).</summary>
public enum ImageListError
{
    /// <summary>The value does not start with <c>'B'</c>, <c>'M'</c>.</summary>
    BadSignature,

    /// <summary>The value ends before the headers, the palette or the pixels it announces.</summary>
    Truncated,

    /// <summary>A header field is impossible: info header under 40 bytes, width or height 0, pixels inside the headers.</summary>
    InvalidHeader,

    /// <summary>A valid image the bitmap reader does not take: core header, 16 bits, compression, other bit masks.</summary>
    UnsupportedFormat,

    /// <summary>The list's <c>Width</c> or <c>Height</c> is below 1.</summary>
    InvalidCellSize,
}

/// <summary>
/// One cell of an image list: <c>Width × Height</c> canonical pixels (UIF-1461), top row first. A pixel is a
/// premultiplied BGRA value read as a little-endian <see cref="uint"/>: <c>$AARRGGBB</c>.
/// </summary>
public sealed record ImageListCell(int Width, ImmutableArray<uint> Pixels)
{
    public bool Equals(ImageListCell? other) => other is not null && Width == other.Width && Pixels.SequenceEqual(other.Pixels);

    public override int GetHashCode() => Pixels.Aggregate(Width, (h, p) => HashCode.Combine(h, p));

    /// <summary>The pixel at column <paramref name="x"/> and row <paramref name="y"/>.</summary>
    public uint Pixel(int x, int y) => Pixels[y * Width + x];
}

/// <summary>The outcome of <see cref="ImageListData.Decode(ReadOnlySpan{byte}, int, int)"/>: the list, or the reason it could not be read.</summary>
public sealed record ImageListDecodeResult(ImageListData? Data, ImageListError? Error);

/// <summary>
/// The designer's codec of the image list's defined binary property <c>Bitmap</c> (UIF-1485): one 32-bit BMP strip of
/// all cells side by side (byte layout UIF-1472), read by the rules of UIF-1473 and cut into cells by UIF-1463. The
/// designer never changes a value it cannot read: a malformed value gives an error result and stays preserved.
/// </summary>
public sealed class ImageListData
{
    /// <summary>The default of the <c>Width</c> and <c>Height</c> properties.</summary>
    public const int DefaultSize = 16;

    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;
    private const uint BiRgb = 0;
    private const uint BiBitfields = 3;

    /// <summary>A list of <paramref name="cells"/>, each <c>width × height</c> pixels; every pixel is made canonical (UIF-1461).</summary>
    public ImageListData(int width, int height, IEnumerable<ImageListCell> cells)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        var list = ImmutableArray.CreateBuilder<ImageListCell>();
        foreach (var cell in cells)
        {
            if (cell.Width != width || cell.Pixels.Length != width * height)
                throw new ArgumentException($"A cell must be {width} × {height} pixels.", nameof(cells));
            list.Add(cell.Pixels.All(p => Canonical(p) == p) ? cell : new ImageListCell(width, [.. cell.Pixels.Select(Canonical)]));
        }

        Cells = list.ToImmutable();
    }

    public int Width { get; }

    public int Height { get; }

    public ImmutableArray<ImageListCell> Cells { get; }

    public int Count => Cells.Length;

    /// <summary>Reads <paramref name="value"/> with the <c>Width</c> and <c>Height</c> of the image list <paramref name="owner"/> (defaults 16).</summary>
    public static ImageListDecodeResult Decode(FormObject owner, BinaryValue value) =>
        Decode(value.Bytes.AsSpan(), SizeProperty(owner, "Width"), SizeProperty(owner, "Height"));

    /// <summary>Reads a <c>Bitmap</c> value into cells of <paramref name="width"/> × <paramref name="height"/> pixels; never throws.</summary>
    public static ImageListDecodeResult Decode(ReadOnlySpan<byte> value, int width = DefaultSize, int height = DefaultSize)
    {
        if (width < 1 || height < 1)
            return Failed(ImageListError.InvalidCellSize);
        var image = ReadBitmap(value, out var error);
        if (image is null)
            return Failed(error);
        var (imageWidth, imageHeight, pixels, alpha) = image.Value;

        // UIF-1463, UIF-1473: an image narrower than Width gives no cell; rows and columns outside the source are transparent.
        var count = imageWidth >= width ? imageWidth / width : 0;
        var cells = new List<ImageListCell>(count);
        for (var i = 0; i < count; i++)
        {
            var cell = new uint[width * height];
            for (var y = 0; y < Math.Min(imageHeight, height); y++)
                for (var x = 0; x < width; x++)
                {
                    var source = pixels[y * imageWidth + i * width + x];
                    cell[y * width + x] = alpha ? Canonical(source) : source | 0xFF000000;
                }

            cells.Add(new ImageListCell(width, [.. cell]));
        }

        return new ImageListDecodeResult(new ImageListData(width, height, cells), null);
    }

    /// <summary>The writer's bytes (UIF-1472); empty when the list has no cell (the property is then not stored).</summary>
    public byte[] Encode()
    {
        if (Count == 0)
            return [];
        var stripWidth = Count * Width;
        var imageSize = 4 * stripWidth * Height;
        var bytes = new byte[FileHeaderSize + InfoHeaderSize + imageSize];
        var span = bytes.AsSpan();
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], FileHeaderSize + InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[18..], stripWidth);
        BinaryPrimitives.WriteInt32LittleEndian(span[22..], Height);
        BinaryPrimitives.WriteInt16LittleEndian(span[26..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[28..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(span[34..], imageSize);
        var at = FileHeaderSize + InfoHeaderSize;
        for (var y = Height - 1; y >= 0; y--)
            foreach (var cell in Cells)
                for (var x = 0; x < Width; x++, at += 4)
                    BinaryPrimitives.WriteUInt32LittleEndian(span[at..], cell.Pixels[y * Width + x]);
        return bytes;
    }

    /// <summary>The value to store in the form, or null when nothing is stored (<c>Count = 0</c>).</summary>
    public BinaryValue? ToValue() => Count == 0 ? null : new BinaryValue([.. Encode()]);

    /// <summary>UIF-1461: colour bytes above the alpha byte are lowered to it; alpha 0 is all zero.</summary>
    public static uint Canonical(uint pixel)
    {
        var a = pixel >> 24;
        if (a == 0)
            return 0;
        var result = a << 24;
        for (var shift = 0; shift < 24; shift += 8)
            result |= Math.Min((pixel >> shift) & 0xFF, a) << shift;
        return result;
    }

    private static ImageListDecodeResult Failed(ImageListError error) => new(null, error);

    private static int SizeProperty(FormObject owner, string name) =>
        owner.FindProperty(name)?.Value is IntegerValue { Value: >= int.MinValue and <= int.MaxValue } v ? (int)v.Value : DefaultSize;

    /// <summary>
    /// The bitmap reader of graphics.md UIF-544: the pixels top row first as BGRA values (palettised and 24-bit input
    /// with alpha 0), and whether the image is 32-bit with at least one alpha byte not 0 (UIF-1473).
    /// </summary>
    private static (int Width, int Height, uint[] Pixels, bool Alpha)? ReadBitmap(ReadOnlySpan<byte> value, out ImageListError error)
    {
        error = ImageListError.Truncated;
        if (value.Length < FileHeaderSize)
            return Fail(value.Length >= 2 && !IsSignature(value) ? ImageListError.BadSignature : ImageListError.Truncated, out error);
        if (!IsSignature(value))
            return Fail(ImageListError.BadSignature, out error);
        var offBits = BinaryPrimitives.ReadUInt32LittleEndian(value[10..]);
        if (value.Length < FileHeaderSize + 4)
            return null;
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(value[14..]);
        if (headerSize == 12)
            return Fail(ImageListError.UnsupportedFormat, out error);
        if (headerSize < InfoHeaderSize)
            return Fail(ImageListError.InvalidHeader, out error);
        if (value.Length < FileHeaderSize + InfoHeaderSize)
            return null;
        var header = value[FileHeaderSize..];
        var width = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(header[32..]);
        long position = FileHeaderSize + InfoHeaderSize;

        // The masks of BI_BITFIELDS: part of a V4/V5 header, else the 12 bytes after the 40-byte header.
        (uint Red, uint Green, uint Blue) masks = (0x00FF0000, 0x0000FF00, 0x000000FF);
        if (headerSize > InfoHeaderSize)
        {
            if (value.Length < FileHeaderSize + (long)headerSize)
                return null;
            if (compression == BiBitfields && headerSize - InfoHeaderSize >= 12)
                masks = ReadMasks(value[(int)position..]);
            position = FileHeaderSize + headerSize;
        }
        else if (compression == BiBitfields)
        {
            if (value.Length < position + 12)
                return null;
            masks = ReadMasks(value[(int)position..]);
            position += 12;
        }

        var supported = compression switch
        {
            BiRgb => bits is 1 or 4 or 8 or 24 or 32,
            BiBitfields => bits == 32 && masks == (0x00FF0000, 0x0000FF00, 0x000000FF),
            _ => false,
        };
        if (!supported)
            return Fail(ImageListError.UnsupportedFormat, out error);
        var topDown = height < 0;
        if (topDown)
            height = height == int.MinValue ? 0 : -height;
        if (width <= 0 || height <= 0)
            return Fail(ImageListError.InvalidHeader, out error);

        var palette = Array.Empty<uint>();
        if (bits <= 8)
        {
            var colors = colorsUsed == 0 || colorsUsed > 1u << bits ? 1 << bits : (int)colorsUsed;
            if (value.Length < position + 4L * colors)
                return null;
            palette = new uint[colors];
            for (var i = 0; i < colors; i++)
                palette[i] = BinaryPrimitives.ReadUInt32LittleEndian(value[(int)(position + 4 * i)..]) & 0xFFFFFF;
            position += 4L * colors;
        }

        // The pixels start at bfOffBits from the start of the value.
        if (offBits < position)
            return Fail(ImageListError.InvalidHeader, out error);
        long stride = ((long)width * bits + 31) / 32 * 4;
        if (value.Length < offBits + stride * height)
            return null;

        var pixels = new uint[(long)width * height];
        var alpha = false;
        for (var row = 0; row < height; row++)
        {
            var source = value.Slice((int)(offBits + stride * (topDown ? row : height - 1 - row)), (int)stride);
            for (var x = 0; x < width; x++)
            {
                uint pixel = bits switch
                {
                    32 => BinaryPrimitives.ReadUInt32LittleEndian(source[(4 * x)..]),
                    24 => source[3 * x] | (uint)source[3 * x + 1] << 8 | (uint)source[3 * x + 2] << 16,
                    _ => PaletteColor(palette, bits switch
                    {
                        1 => (source[x / 8] >> (7 - x % 8)) & 1,
                        4 => (source[x / 2] >> (4 * (1 - x % 2))) & 15,
                        _ => source[x],
                    }),
                };
                alpha |= bits == 32 && pixel >> 24 != 0;
                pixels[row * width + x] = pixel;
            }
        }

        return (width, height, pixels, alpha);
    }

    private static bool IsSignature(ReadOnlySpan<byte> value) => value[0] == 'B' && value[1] == 'M';

    private static (int, int, uint[], bool)? Fail(ImageListError reason, out ImageListError error)
    {
        error = reason;
        return null;
    }

    private static (uint, uint, uint) ReadMasks(ReadOnlySpan<byte> at) =>
        (BinaryPrimitives.ReadUInt32LittleEndian(at), BinaryPrimitives.ReadUInt32LittleEndian(at[4..]), BinaryPrimitives.ReadUInt32LittleEndian(at[8..]));

    // An index past the palette gives black (the reader's rule).
    private static uint PaletteColor(uint[] palette, int index) => index < palette.Length ? palette[index] : 0;
}
