using System.Text;

namespace FreexComp.Debugger.Dap;

/// <summary>
/// Base-protocol framing over two byte streams (architecture §4.1): <c>Content-Length: n\r\n\r\n</c> followed by
/// <c>n</c> bytes of UTF-8 JSON. Reading is single-consumer; sends are serialised and never interleave.
/// </summary>
internal sealed class DapTransport
{
    internal const int MaxContentLength = 64 * 1024 * 1024;
    private const int MaxHeaderLength = 64 * 1024;
    private static readonly byte[] HeaderEnd = "\r\n\r\n"u8.ToArray();

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private byte[] _buffer = new byte[4096];
    private int _start;
    private int _end;
    private bool _eof;

    /// <param name="input">Bytes from the adapter (its stdout).</param>
    /// <param name="output">Bytes to the adapter (its stdin).</param>
    public DapTransport(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>
    /// Reads the next message body. Returns null at end of stream (also when the stream ends inside a message: the
    /// adapter exited). Throws <see cref="DebuggerException"/> (<see cref="DebugErrorCode.ProtocolError"/>) for a
    /// header without a valid <c>Content-Length</c> or a length above 64 MiB.
    /// </summary>
    public async Task<byte[]?> ReadAsync(CancellationToken ct)
    {
        int headerLength;
        while ((headerLength = _buffer.AsSpan(_start, _end - _start).IndexOf(HeaderEnd)) < 0)
        {
            if (_end - _start > MaxHeaderLength)
                throw ProtocolError(Messages.HeaderTooLong);
            if (!await FillAsync(ct).ConfigureAwait(false))
                return null;
        }

        var contentLength = ParseContentLength(_buffer.AsSpan(_start, headerLength));
        _start += headerLength + HeaderEnd.Length;

        var body = new byte[contentLength];
        var copied = Math.Min(contentLength, _end - _start);
        _buffer.AsSpan(_start, copied).CopyTo(body);
        _start += copied;
        while (copied < contentLength)
        {
            var read = _eof ? 0 : await _input.ReadAsync(body.AsMemory(copied), ct).ConfigureAwait(false);
            if (read == 0)
            {
                _eof = true;
                return null;
            }

            copied += read;
        }

        return body;
    }

    /// <summary>Writes one framed message with a single write and a flush.</summary>
    public async Task SendAsync(ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        var header = Encoding.ASCII.GetBytes("Content-Length: " + body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n\r\n");
        var frame = new byte[header.Length + body.Length];
        header.CopyTo(frame, 0);
        body.CopyTo(frame.AsMemory(header.Length));

        await _sendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(frame, ct).ConfigureAwait(false);
            await _output.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>Appends more input to the buffer; false at end of stream.</summary>
    private async Task<bool> FillAsync(CancellationToken ct)
    {
        if (_eof)
            return false;
        if (_start > 0)
        {
            _buffer.AsSpan(_start, _end - _start).CopyTo(_buffer);
            _end -= _start;
            _start = 0;
        }

        if (_end == _buffer.Length)
            Array.Resize(ref _buffer, _buffer.Length * 2);
        var read = await _input.ReadAsync(_buffer.AsMemory(_end), ct).ConfigureAwait(false);
        if (read == 0)
        {
            _eof = true;
            return false;
        }

        _end += read;
        return true;
    }

    private static int ParseContentLength(ReadOnlySpan<byte> header)
    {
        const string name = "Content-Length:";
        foreach (var line in Encoding.ASCII.GetString(header).Split("\r\n"))
        {
            if (!line.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                continue;   // unknown header lines are ignored
            var value = line[name.Length..].Trim();
            if (value.Length == 0 || !value.All(char.IsAsciiDigit))
                throw ProtocolError(Messages.InvalidContentLength + value);
            if (!long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var length)
                || length > MaxContentLength)
                throw ProtocolError(Messages.OversizeMessage + value);
            return (int)length;
        }

        throw ProtocolError(Messages.MissingContentLength);
    }

    private static DebuggerException ProtocolError(string detail) =>
        new(DebugErrorCode.ProtocolError, Messages.ProtocolError(detail));
}
