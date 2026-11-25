using System.Text;
using System.Text.Json;

namespace FreexComp.Build;

/// <summary>Raised while reading a project file; carries the diagnostic code, its arguments and a UTF-16 offset.</summary>
internal sealed class ProjectFileException(string code, int offset, params object[] args) : Exception(code)
{
    public string Code { get; } = code;

    public int Offset { get; } = offset;

    public object[] Arguments { get; } = args;

    /// <summary>Further errors of the same load, reported after this one (CMP-1154: one per broken link or cycle).</summary>
    public IReadOnlyList<ProjectFileException> Additional { get; init; } = [];

    public static ProjectFileException Invalid(int offset, string detail) => new("E9002", offset, detail);
}

/// <summary>
/// Strict JSON (no comments, no trailing commas, no duplicate keys) with the UTF-16 offset of every value,
/// keyed by a pointer of the form "", "/files", "/files/0", "/files/0/path", and of every property name, keyed by the pointer of
/// its value.
/// </summary>
internal sealed class StrictJson
{
    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private readonly Dictionary<string, int> _offsets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _keyOffsets = new(StringComparer.Ordinal);
    private readonly byte[] _bytes;

    private StrictJson(string text)
    {
        Text = text;
        _bytes = Encoding.UTF8.GetBytes(text);
    }

    public string Text { get; }

    public JsonElement Root { get; private set; }

    /// <exception cref="ProjectFileException">E9002 for malformed JSON.</exception>
    public static StrictJson Parse(string text)
    {
        var json = new StrictJson(text);
        json.Scan();
        using var document = JsonDocument.Parse(json._bytes, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
        json.Root = document.RootElement.Clone();
        return json;
    }

    public int OffsetOf(string pointer) => _offsets.TryGetValue(pointer, out var offset) ? offset : 0;

    /// <summary>The offset of the property name whose value <paramref name="pointer"/> names.</summary>
    public int KeyOffsetOf(string pointer) => _keyOffsets.TryGetValue(pointer, out var offset) ? offset : 0;

    private int CharOffset(long byteOffset) =>
        Encoding.UTF8.GetCharCount(_bytes, 0, (int)Math.Clamp(byteOffset, 0, _bytes.Length));

    private void Scan()
    {
        var reader = new Utf8JsonReader(_bytes, ReaderOptions);
        var stack = new Stack<Frame>();
        var sawToken = false;
        try
        {
            while (reader.Read())
            {
                sawToken = true;
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        var top = stack.Peek();
                        top.Name = reader.GetString()!;
                        if (!top.Keys.Add(top.Name))
                        {
                            throw ProjectFileException.Invalid(CharOffset(reader.TokenStartIndex), $"duplicate key '{top.Name}'");
                        }

                        _keyOffsets[top.Path + "/" + top.Name] = CharOffset(reader.TokenStartIndex);
                        break;
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        var path = ValuePath(stack);
                        _offsets[path] = CharOffset(reader.TokenStartIndex);
                        stack.Push(new Frame(path, reader.TokenType == JsonTokenType.StartArray));
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        stack.Pop();
                        AdvanceIndex(stack);
                        break;
                    default:
                        _offsets[ValuePath(stack)] = CharOffset(reader.TokenStartIndex);
                        AdvanceIndex(stack);
                        break;
                }
            }
        }
        catch (JsonException e)
        {
            throw SyntaxError(e);
        }

        if (!sawToken)
        {
            throw ProjectFileException.Invalid(0, "malformed JSON");
        }
    }

    private ProjectFileException SyntaxError(JsonException e)
    {
        // Byte offset of the error: start of line LineNumber (0-based, LF-separated) + BytePositionInLine.
        long lineStart = 0;
        for (long line = 0; line < (e.LineNumber ?? 0) && lineStart < _bytes.Length; lineStart++)
        {
            if (_bytes[lineStart] == (byte)'\n')
            {
                line++;
            }
        }

        var offset = CharOffset(lineStart + (e.BytePositionInLine ?? 0));
        if (offset < Text.Length && Text[offset] == '/')
        {
            return ProjectFileException.Invalid(offset, "comments are not allowed");
        }

        // A closing bracket right after a comma (ignoring whitespace) is a trailing comma: report the comma.
        if (offset < Text.Length && Text[offset] is '}' or ']')
        {
            var i = offset - 1;
            while (i >= 0 && char.IsWhiteSpace(Text[i]))
            {
                i--;
            }

            if (i >= 0 && Text[i] == ',')
            {
                return ProjectFileException.Invalid(i, "trailing comma is not allowed");
            }
        }

        return ProjectFileException.Invalid(offset, "malformed JSON");
    }

    private static string ValuePath(Stack<Frame> stack) =>
        stack.Count == 0 ? "" : stack.Peek() is var top && top.IsArray ? top.Path + "/" + top.Index : top.Path + "/" + top.Name;

    private static void AdvanceIndex(Stack<Frame> stack)
    {
        if (stack.Count > 0 && stack.Peek().IsArray)
        {
            stack.Peek().Index++;
        }
    }

    private sealed class Frame(string path, bool isArray)
    {
        public string Path { get; } = path;

        public bool IsArray { get; } = isArray;

        public int Index { get; set; }

        public string Name { get; set; } = "";

        public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);
    }
}
