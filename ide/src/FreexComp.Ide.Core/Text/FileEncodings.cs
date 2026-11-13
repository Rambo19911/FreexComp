using System.Text;

namespace FreexComp.Ide.Core.Text;

/// <summary>The five supported file encodings (IDE-P1-070), in the order of <c>files.defaultEncoding</c>.</summary>
public enum FileEncoding
{
    Utf8,
    Utf8Bom,
    Utf16Le,
    Utf16Be,
    Windows1252,
}

/// <summary>Decoding and encoding of file bytes in a <see cref="FileEncoding"/> (IDE-P1-070, 073, 083, 084).</summary>
public static class FileEncodings
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

    // Encoders throw instead of writing replacement characters (lossy saving is never performed, IDE-P1-083);
    // decoders replace invalid input (only reachable through "Reopen with Encoding", IDE-P1-074).
    private static readonly Encoding Utf8 = new UTF8Encoding(false, throwOnInvalidBytes: false);
    private static readonly Encoding Utf16Le = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
    private static readonly Encoding Utf16Be = new UnicodeEncoding(bigEndian: true, byteOrderMark: false);

    /// <summary>The <c>files.defaultEncoding</c> value (settings.md): <c>utf-8</c>, <c>utf-8-bom</c>, ….</summary>
    public static string ToId(FileEncoding encoding) => encoding switch
    {
        FileEncoding.Utf8 => "utf-8",
        FileEncoding.Utf8Bom => "utf-8-bom",
        FileEncoding.Utf16Le => "utf-16le",
        FileEncoding.Utf16Be => "utf-16be",
        FileEncoding.Windows1252 => "windows-1252",
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };

    /// <summary>The display name (resources <c>Encoding_&lt;id&gt;</c>, dialogs.md §24.1).</summary>
    public static string DisplayName(FileEncoding encoding) => encoding switch
    {
        FileEncoding.Utf8 => Resources.Strings.Encoding_utf_8,
        FileEncoding.Utf8Bom => Resources.Strings.Encoding_utf_8_bom,
        FileEncoding.Utf16Le => Resources.Strings.Encoding_utf_16le,
        FileEncoding.Utf16Be => Resources.Strings.Encoding_utf_16be,
        FileEncoding.Windows1252 => Resources.Strings.Encoding_windows_1252,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };

    public static FileEncoding? FromId(string id) =>
        Enum.GetValues<FileEncoding>().Cast<FileEncoding?>().FirstOrDefault(e => ToId(e!.Value) == id);

    /// <summary>The byte-order mark written on save: only <c>utf-8-bom</c> and the UTF-16 encodings have one (IDE-P1-084).</summary>
    public static ReadOnlySpan<byte> Preamble(FileEncoding encoding) => encoding switch
    {
        FileEncoding.Utf8Bom => Utf8Bom,
        FileEncoding.Utf16Le => Utf16LeBom,
        FileEncoding.Utf16Be => Utf16BeBom,
        _ => [],
    };

    /// <summary>Text of <paramref name="bytes"/> in <paramref name="encoding"/>; that encoding's BOM, when present, is skipped.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes, FileEncoding encoding)
    {
        var preamble = Preamble(encoding);
        if (preamble.Length > 0 && bytes.StartsWith(preamble))
            bytes = bytes[preamble.Length..];
        return encoding switch
        {
            FileEncoding.Utf8 or FileEncoding.Utf8Bom => Utf8.GetString(bytes),
            FileEncoding.Utf16Le => Utf16Le.GetString(bytes),
            FileEncoding.Utf16Be => Utf16Be.GetString(bytes),
            _ => Windows1252.Decode(bytes),
        };
    }

    /// <summary>True when every character of <paramref name="text"/> can be stored (only Windows-1252 can fail).</summary>
    public static bool CanEncode(string text, FileEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        return encoding != FileEncoding.Windows1252 || Windows1252.CanEncode(text);
    }

    /// <summary>File bytes of <paramref name="text"/>, BOM included (IDE-P1-084).</summary>
    /// <exception cref="EncoderFallbackException">A character is not representable (check <see cref="CanEncode"/> first).</exception>
    public static byte[] Encode(string text, FileEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] body = encoding switch
        {
            FileEncoding.Utf8 or FileEncoding.Utf8Bom => Utf8.GetBytes(text),
            FileEncoding.Utf16Le => Utf16Le.GetBytes(text),
            FileEncoding.Utf16Be => Utf16Be.GetBytes(text),
            _ => Windows1252.Encode(text),
        };
        return [.. Preamble(encoding), .. body];
    }
}

/// <summary>
/// Windows-1252 by an own table, so that no code-page provider has to be registered process-wide. Bytes 00-7F and
/// A0-FF are the same code points; the five bytes undefined in the code page (81, 8D, 8F, 90, 9D) map to the C1
/// control with the same code so every byte sequence round-trips (IDE-P1-073).
/// </summary>
internal static class Windows1252
{
    private static readonly char[] High80To9F =
    [
        '€', '\u0081', '‚', 'ƒ', '„', '…', '†', '‡',
        'ˆ', '‰', 'Š', '‹', 'Œ', '\u008D', 'Ž', '\u008F',
        '\u0090', '‘', '’', '“', '”', '•', '–', '—',
        '˜', '™', 'š', '›', 'œ', '\u009D', 'ž', 'Ÿ',
    ];

    private static readonly Dictionary<char, byte> ToByte = BuildReverse();

    private static Dictionary<char, byte> BuildReverse()
    {
        var map = new Dictionary<char, byte>(256);
        for (int b = 0; b < 256; b++)
            map.Add(ToChar((byte)b), (byte)b);
        return map;
    }

    public static char ToChar(byte b) => b is >= 0x80 and <= 0x9F ? High80To9F[b - 0x80] : (char)b;

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            chars[i] = ToChar(bytes[i]);
        return new string(chars);
    }

    public static bool CanEncode(string text) => text.All(ToByte.ContainsKey);

    public static byte[] Encode(string text)
    {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            if (!ToByte.TryGetValue(text[i], out bytes[i]))
                throw new EncoderFallbackException($"Character U+{(int)text[i]:X4} at index {i} cannot be stored in Windows-1252.");
        }
        return bytes;
    }
}
