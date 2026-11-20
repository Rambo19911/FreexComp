using System.Text;
using System.Text.Json;

namespace FreexComp.Designer.Catalog;

/// <summary>
/// Source positions of the objects of a JSON text, keyed by path (<c>components[2].properties[0]</c>,
/// <c>types.TAlign</c>), for problem reports. Lines and columns are 1-based, columns in UTF-16 code units.
/// </summary>
internal sealed class JsonPositions
{
    private readonly byte[] _bytes;
    private readonly Dictionary<string, int> _offsets = new(StringComparer.Ordinal);

    private JsonPositions(byte[] bytes) => _bytes = bytes;

    /// <summary>Records the start of every object of an already validated JSON text.</summary>
    public static JsonPositions Of(byte[] bytes)
    {
        var positions = new JsonPositions(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
        var stack = new Stack<Frame>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    stack.Peek().Name = reader.GetString();
                    break;
                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    var path = ChildPath(stack);
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        positions._offsets.TryAdd(path, (int)reader.TokenStartIndex);
                    }

                    stack.Push(new Frame(path, reader.TokenType == JsonTokenType.StartArray));
                    break;
                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                    stack.Pop();
                    break;
                default:
                    ChildPath(stack);
                    break;
            }
        }

        return positions;
    }

    /// <summary>Line and column of the object at <paramref name="path"/>, or of the nearest recorded parent.</summary>
    public (int Line, int Column) Find(string path)
    {
        while (true)
        {
            if (_offsets.TryGetValue(path, out var offset))
            {
                return LineColumn(_bytes, offset);
            }

            var cut = Math.Max(path.LastIndexOf('.'), path.LastIndexOf('['));
            if (cut <= 0)
            {
                return (1, 1);
            }

            path = path[..cut];
        }
    }

    /// <summary>1-based line and UTF-16 column of a byte offset.</summary>
    public static (int Line, int Column) LineColumn(byte[] bytes, int offset)
    {
        offset = Math.Clamp(offset, 0, bytes.Length);
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < offset; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, Encoding.UTF8.GetCharCount(bytes, lineStart, offset - lineStart) + 1);
    }

    private static string ChildPath(Stack<Frame> stack)
    {
        if (!stack.TryPeek(out var parent))
        {
            return "";
        }

        if (parent.IsArray)
        {
            return $"{parent.Path}[{parent.Index++}]";
        }

        return parent.Path.Length == 0 ? parent.Name ?? "" : $"{parent.Path}.{parent.Name}";
    }

    private sealed class Frame(string path, bool isArray)
    {
        public string Path { get; } = path;

        public bool IsArray { get; } = isArray;

        public int Index { get; set; }

        public string? Name { get; set; }
    }
}
