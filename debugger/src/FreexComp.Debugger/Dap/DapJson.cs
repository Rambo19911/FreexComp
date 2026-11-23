using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreexComp.Debugger.Dap;

/// <summary>JSON conventions of the protocol (DBG-010): camelCase names, nulls omitted, unknown properties ignored.</summary>
internal static class DapJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Plain UTF-8 text (no \uXXXX escapes for quotes, back-ticks or non-ASCII letters): the stream is not HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The content part of a request; <c>arguments</c> is omitted when null.</summary>
    public static byte[] SerializeRequest(int seq, string command, object? arguments)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("seq", seq);
            writer.WriteString("type", "request");
            writer.WriteString("command", command);
            if (arguments is not null)
            {
                writer.WritePropertyName("arguments");
                JsonSerializer.Serialize(writer, arguments, arguments.GetType(), Options);
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The content part of a body-less response (used to reject reverse requests).</summary>
    public static byte[] SerializeResponse(int seq, int requestSeq, string command, bool success, string? message)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("seq", seq);
            writer.WriteString("type", "response");
            writer.WriteNumber("request_seq", requestSeq);
            writer.WriteBoolean("success", success);
            writer.WriteString("command", command);
            if (message is not null)
                writer.WriteString("message", message);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Reads a response or event body. A missing body yields the record's defaults; a body of the wrong shape is a
    /// protocol error.
    /// </summary>
    public static T Deserialize<T>(JsonElement body) where T : class, new()
    {
        if (body.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return new T();
        try
        {
            return body.Deserialize<T>(Options) ?? new T();
        }
        catch (JsonException e)
        {
            throw new DebuggerException(DebugErrorCode.ProtocolError,
                Messages.ProtocolError(Messages.MalformedJson + typeof(T).Name + ": " + e.Message), inner: e);
        }
    }

    /// <summary>Text form of an id that the protocol allows to be a number or a string; null when absent.</summary>
    public static string? IdText(JsonElement? id) => id?.ValueKind switch
    {
        JsonValueKind.String => id.Value.GetString(),
        JsonValueKind.Number => id.Value.GetRawText(),
        _ => null,
    };
}
