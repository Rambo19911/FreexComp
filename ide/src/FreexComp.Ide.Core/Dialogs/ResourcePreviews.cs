using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Resources;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>What the preview of the page <c>Resources</c> shows (UI-DLG-333).</summary>
public enum ResourcePreviewKind
{
    /// <summary>No row is selected.</summary>
    None,

    /// <summary>An ICON / BITMAP / CURSOR file that holds an image.</summary>
    Image,

    /// <summary>The first 4 KB as text.</summary>
    Text,

    /// <summary>The first 4 KB as a hex dump.</summary>
    Hex,

    /// <summary>The file does not exist (<c>Msg.ProjectOptions.ResourceMissing</c>).</summary>
    Missing,
}

/// <summary>An image file: its bytes as read (the view decodes them) and the pixel size of its largest frame.</summary>
public sealed record ResourceImage(byte[] Data, int PixelWidth, int PixelHeight);

/// <param name="Text">The text, the hex dump or the missing-file message.</param>
/// <param name="Info">The file size and type line under the preview; null for a missing file or no selection.</param>
public sealed record ResourcePreview(ResourcePreviewKind Kind, string? Text, ResourceImage? Image, string? Info)
{
    public static ResourcePreview Empty { get; } = new(ResourcePreviewKind.None, null, null, null);
}

/// <summary>
/// The previews of the Project Options pages <c>Application</c> (icon, UI-DLG-331) and <c>Resources</c> (UI-DLG-333): the image
/// formats are read only as far as needed to find the largest frame; decoding the pixels is the view's.
/// </summary>
public static class ResourcePreviews
{
    /// <summary>UI-DLG-333: the part of an RCDATA file shown as text or hex.</summary>
    public const int PreviewBytes = 4096;

    /// <summary>UI-DLG-333: an image is never drawn larger than four times its pixel size.</summary>
    public const double MaxScale = 4;

    /// <summary>The preview of <paramref name="fullPath"/> as a resource of <paramref name="type"/>.</summary>
    public static ResourcePreview Create(IdeFileSystem fileSystem, string? fullPath, string type)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        byte[] data;
        try
        {
            if (fullPath is null || !fileSystem.FileExists(fullPath))
                return new ResourcePreview(ResourcePreviewKind.Missing, Strings.Msg_ProjectOptions_ResourceMissing, null, null);
            data = fileSystem.ReadAllBytes(fullPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ResourcePreview(ResourcePreviewKind.Missing, Strings.Msg_ProjectOptions_ResourceMissing, null, null);
        }

        var info = string.Format(CultureInfo.CurrentCulture, Strings.ProjectOptions_ResourceInfo, data.Length, type);
        if (type != "RCDATA" && ImageOf(data, type) is { } image)
            return new ResourcePreview(ResourcePreviewKind.Image, null, image, info);
        var head = data.AsSpan(0, Math.Min(data.Length, PreviewBytes));
        return AsText(head, truncated: data.Length > PreviewBytes) is { } text
            ? new ResourcePreview(ResourcePreviewKind.Text, text, null, info)
            : new ResourcePreview(ResourcePreviewKind.Hex, HexDump(head), null, info);
    }

    /// <summary>
    /// The image of a file of resource <paramref name="type"/>: ICON / CURSOR an icon or cursor file (its largest frame, a PNG
    /// or a device-independent bitmap), BITMAP a bitmap file; null when the bytes are not of that form.
    /// </summary>
    public static ResourceImage? ImageOf(byte[] data, string type)
    {
        ArgumentNullException.ThrowIfNull(data);
        var size = type switch
        {
            "ICON" => LargestFrame(data, 1),
            "CURSOR" => LargestFrame(data, 2),
            "BITMAP" => BitmapSize(data),
            _ => null,
        };
        return size is var (width, height) ? new ResourceImage(data, width, height) : null;
    }

    /// <summary>
    /// The text of <paramref name="data"/> when it is UTF-8 (a leading byte-order mark is skipped) without control characters other
    /// than tab, CR and LF; null otherwise. A sequence cut at the end of a <paramref name="truncated"/> part is dropped.
    /// </summary>
    public static string? AsText(ReadOnlySpan<byte> data, bool truncated)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            data = data[3..];
        var decoder = new UTF8Encoding(false, throwOnInvalidBytes: true).GetDecoder();
        var chars = new char[data.Length];
        int count;
        try
        {
            count = decoder.GetChars(data, chars, flush: !truncated);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        var text = new string(chars, 0, count);
        return text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n')) ? null : text;
    }

    /// <summary>UI-DLG-333: lines of <c>offset  16 bytes  ascii</c> — eight hex digits, the bytes in hex, printable ASCII or '.'.</summary>
    public static string HexDump(ReadOnlySpan<byte> data)
    {
        var text = new StringBuilder();
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            var line = data.Slice(offset, Math.Min(16, data.Length - offset));
            if (offset > 0)
                text.Append('\n');
            text.Append(offset.ToString("X8", CultureInfo.InvariantCulture)).Append("  ");
            for (var i = 0; i < 16; i++)
                text.Append(i < line.Length ? line[i].ToString("X2", CultureInfo.InvariantCulture) + " " : "   ");
            text.Append(' ');
            foreach (var b in line)
                text.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }

        return text.ToString();
    }

    /// <summary>
    /// The pixel size of the largest frame of an icon (<paramref name="kind"/> 1) or cursor (2) file: by area, a frame whose data
    /// is a PNG or a device-independent bitmap of that size; null for anything else.
    /// </summary>
    private static (int Width, int Height)? LargestFrame(byte[] data, int kind)
    {
        if (data.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0)) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)) != kind)
            return null;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        if (count == 0 || data.Length < 6 + (16 * count))
            return null;
        (int Width, int Height)? best = null;
        for (var i = 0; i < count; i++)
        {
            var entry = data.AsSpan(6 + (16 * i), 16);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (length == 0 || offset > data.Length || length > data.Length - offset || FrameSize(data.AsSpan((int)offset, (int)length)) is not { } size)
                return null;
            if (best is not { } b || size.Width * size.Height > b.Width * b.Height)
                best = size;
        }

        return best;
    }

    /// <summary>The size of one icon frame: a PNG (from its header) or a device-independent bitmap (its height holds both masks).</summary>
    private static (int Width, int Height)? FrameSize(ReadOnlySpan<byte> frame)
    {
        if (frame.Length >= 24 && frame.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
            return Positive(BinaryPrimitives.ReadInt32BigEndian(frame[16..]), BinaryPrimitives.ReadInt32BigEndian(frame[20..]));
        if (frame.Length < 40 || BinaryPrimitives.ReadUInt32LittleEndian(frame) < 40)
            return null;
        return Positive(BinaryPrimitives.ReadInt32LittleEndian(frame[4..]), BinaryPrimitives.ReadInt32LittleEndian(frame[8..]) / 2);
    }

    /// <summary>The size of a bitmap file (<c>BM</c>, then a core or an info header).</summary>
    private static (int Width, int Height)? BitmapSize(byte[] data)
    {
        if (data.Length < 26 || data[0] != 'B' || data[1] != 'M')
            return null;
        var header = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(14));
        if (header == 12)
            return Positive(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(18)), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(20)));
        if (header < 40 || data.Length < 14 + 40)
            return null;
        return Positive(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(18)), (int)Math.Min(Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(22))), int.MaxValue));
    }

    private static (int Width, int Height)? Positive(int width, int height) =>
        width is > 0 and <= 65536 && height is > 0 and <= 65536 ? (width, height) : null;
}
