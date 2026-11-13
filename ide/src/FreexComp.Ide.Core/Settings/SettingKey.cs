namespace FreexComp.Ide.Core.Settings;

/// <summary>Value types of settings.md §4 (IDE-SET-014).</summary>
public enum SettingType
{
    Bool,
    Int,
    Number,
    String,
    Enum,
    StringArray,
    Color,
}

/// <summary>Inclusive numeric interval; a single allowed value has <c>Minimum == Maximum</c>.</summary>
public readonly record struct SettingRange(double Minimum, double Maximum)
{
    public bool Contains(double value) => value >= Minimum && value <= Maximum;
}

/// <summary>Untyped view of a <see cref="SettingKey{T}"/>, used by the service and the catalog test.</summary>
public interface ISettingKey
{
    string Name { get; }

    SettingType Type { get; }

    object DefaultValue { get; }

    /// <summary>Enum values (lower-case, as listed in the spec); empty for other types.</summary>
    IReadOnlyList<string> AllowedValues { get; }

    /// <summary>Allowed numeric intervals; empty = unrestricted.</summary>
    IReadOnlyList<SettingRange> Ranges { get; }

    /// <summary>Type and rule check of IDE-SET-022 / IDE-SET-024.</summary>
    bool IsValid(object? value);
}

/// <summary>A setting (settings.md §3). Create through the factory methods of <see cref="SettingKey"/>.</summary>
public sealed record SettingKey<T>(string Name, T Default, Func<T, bool>? Validate = null) : ISettingKey
    where T : notnull
{
    public SettingType Type { get; init; }

    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    public IReadOnlyList<SettingRange> Ranges { get; init; } = [];

    public object DefaultValue => Default;

    public bool IsValid(object? value) =>
        value is T typed && SettingValues.SatisfiesRule(this, typed) && (Validate?.Invoke(typed) ?? true);
}

/// <summary>Factory methods, one per <see cref="SettingType"/>.</summary>
public static class SettingKey
{
    public static SettingKey<bool> Bool(string name, bool defaultValue) =>
        new(name, defaultValue) { Type = SettingType.Bool };

    public static SettingKey<int> Int(string name, int defaultValue, params SettingRange[] ranges) =>
        new(name, defaultValue) { Type = SettingType.Int, Ranges = ranges };

    public static SettingKey<double> Number(string name, double defaultValue, params SettingRange[] ranges) =>
        new(name, defaultValue) { Type = SettingType.Number, Ranges = ranges };

    public static SettingKey<string> String(string name, string defaultValue) =>
        new(name, defaultValue) { Type = SettingType.String };

    public static SettingKey<string> Enum(string name, string defaultValue, params string[] values) =>
        new(name, defaultValue) { Type = SettingType.Enum, AllowedValues = values };

    /// <summary><c>#RRGGBB</c>; stored upper-case.</summary>
    public static SettingKey<string> Color(string name, string defaultValue) =>
        new(name, defaultValue.ToUpperInvariant()) { Type = SettingType.Color };

    public static SettingKey<IReadOnlyList<string>> StringArray(string name, params string[] defaultValue) =>
        new(name, defaultValue) { Type = SettingType.StringArray };
}
