using System.Text.Unicode;

namespace FreexComp.Ide.Core.Text;

/// <summary>
/// Result of <see cref="EncodingDetector.Detect"/>. When <see cref="IsBinary"/> is true the file is not opened
/// (<c>Msg.File.Binary</c>) and <see cref="Text"/> is empty. <see cref="IsFallback"/> = the file was not valid
/// UTF-8 and was read as Windows-1252 (info bar <c>Msg.File.EncodingFallback</c>).
/// </summary>
public sealed record EncodingDetection(bool IsBinary, FileEncoding Encoding, bool IsFallback, string Text);

/// <summary>Encoding detection on open (IDE-P1-071, 072).</summary>
public static class EncodingDetector
{
    /// <summary>Bytes scanned for NUL by the binary check (IDE-P1-072).</summary>
    public const int BinaryCheckLength = 8000;

    public static EncodingDetection Detect(ReadOnlySpan<byte> bytes)
    {
        bool utf16Bom = bytes.StartsWith(FileEncodings.Preamble(FileEncoding.Utf16Le)) || bytes.StartsWith(FileEncodings.Preamble(FileEncoding.Utf16Be));
        if (!utf16Bom && bytes[..Math.Min(bytes.Length, BinaryCheckLength)].Contains((byte)0))
            return new EncodingDetection(true, FileEncoding.Utf8, false, "");

        // (1) BOM. UTF-16 LE is tested first: FF FE also starts a UTF-32 LE BOM, which is not supported (read as UTF-16 LE).
        foreach (var encoding in new[] { FileEncoding.Utf8Bom, FileEncoding.Utf16Le, FileEncoding.Utf16Be })
        {
            if (bytes.StartsWith(FileEncodings.Preamble(encoding)))
                return new EncodingDetection(false, encoding, false, FileEncodings.Decode(bytes, encoding));
        }

        // (2) Strict UTF-8 (rejects overlong forms, surrogates, truncated sequences), (3) otherwise Windows-1252.
        return Utf8.IsValid(bytes)
            ? new EncodingDetection(false, FileEncoding.Utf8, false, FileEncodings.Decode(bytes, FileEncoding.Utf8))
            : new EncodingDetection(false, FileEncoding.Windows1252, true, FileEncodings.Decode(bytes, FileEncoding.Windows1252));
    }
}
