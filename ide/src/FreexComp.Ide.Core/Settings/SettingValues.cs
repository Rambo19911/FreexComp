using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FreexComp.Ide.Core.Settings;

/// <summary>Rules, normalization, equality and JSON conversion of setting values (IDE-SET-014, -022).</summary>
internal static class SettingValues
{
    internal static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    private static readonly JsonSerializerOptions StringOptions = new() { Encoder = Encoder };

    public static bool SatisfiesRule(ISettingKey key, object value) => key.Type switch
    {
        SettingType.Enum => value is string s && key.AllowedValues.Contains(s, StringComparer.Ordinal),
        SettingType.Color => value is string c && IsColor(c),
        SettingType.Int => value is int i && InRanges(key, i),
        SettingType.Number => value is double d && double.IsFinite(d) && InRanges(key, d),
        SettingType.StringArray => value is IReadOnlyList<string> list && list.All(x => x is not null),
        _ => true,
    };

    public static object Normalize(ISettingKey key, object value) => key.Type switch
    {
        SettingType.Color => ((string)value).ToUpperInvariant(),
        SettingType.StringArray => ((IReadOnlyList<string>)value).ToArray(),
        _ => value,
    };

    public static bool AreEqual(object? a, object? b) =>
        a is IReadOnlyList<string> x && b is IReadOnlyList<string> y ? x.SequenceEqual(y, StringComparer.Ordinal) : Equals(a, b);

    /// <summary>Reads a JSON member for <paramref name="key"/>; false when the type or rule does not match.</summary>
    public static bool TryRead(ISettingKey key, JsonElement element, out object value)
    {
        value = null!;
        object? candidate = key.Type switch
        {
            SettingType.Bool when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            SettingType.Int when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var i) => i,
            SettingType.Number when element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var d) => d,
            SettingType.String or SettingType.Enum or SettingType.Color when element.ValueKind == JsonValueKind.String
                => element.GetString(),
            SettingType.StringArray when element.ValueKind == JsonValueKind.Array
                && element.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String)
                => element.EnumerateArray().Select(e => e.GetString()!).ToArray(),
            _ => null,
        };

        if (candidate is null || !key.IsValid(candidate))
        {
            return false;
        }

        value = Normalize(key, candidate);
        return true;
    }

    public static void Write(Utf8JsonWriter writer, ISettingKey key, object value)
    {
        switch (key.Type)
        {
            case SettingType.Bool:
                writer.WriteBooleanValue((bool)value);
                break;
            case SettingType.Int:
                writer.WriteNumberValue((int)value);
                break;
            case SettingType.Number:
                writer.WriteRawValue(((double)value).ToString("R", CultureInfo.InvariantCulture));
                break;
            case SettingType.StringArray:
                // One member per line (IDE-SET-011): arrays stay on the member's line.
                writer.WriteRawValue(
                    "[" + string.Join(", ", ((IReadOnlyList<string>)value).Select(s => JsonSerializer.Serialize(s, StringOptions))) + "]",
                    skipInputValidation: true);
                break;
            default:
                writer.WriteStringValue((string)value);
                break;
        }
    }

    private static bool InRanges(ISettingKey key, double value) =>
        key.Ranges.Count == 0 || key.Ranges.Any(r => r.Contains(value));

    private static bool IsColor(string s) =>
        s.Length == 7 && s[0] == '#' && s.Skip(1).All(char.IsAsciiHexDigit);
}
