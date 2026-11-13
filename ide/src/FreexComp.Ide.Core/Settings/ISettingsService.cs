namespace FreexComp.Ide.Core.Settings;

public sealed class SettingChangedEventArgs(string key, object? oldValue, object? newValue) : EventArgs
{
    public string Key { get; } = key;

    public object? OldValue { get; } = oldValue;

    public object? NewValue { get; } = newValue;
}

/// <summary>Option values (settings.md §2–§3). Used on the UI thread only.</summary>
public interface ISettingsService
{
    /// <summary>
    /// After loading: the file name shown in <c>Msg.App.SettingsCorrupt</c> when <c>settings.json</c> was
    /// unreadable (IDE-SET-021), otherwise null.
    /// </summary>
    string? CorruptFileName { get; }

    T Get<T>(SettingKey<T> key) where T : notnull;

    /// <exception cref="ArgumentException">The value breaks the key's type or rule (IDE-SET-024).</exception>
    void Set<T>(SettingKey<T> key, T value) where T : notnull;

    bool IsDefault(string keyName);

    void Reset(string keyName);

    void ResetAll(string keyPrefix);

    IDisposable BeginBatch();

    event EventHandler<SettingChangedEventArgs>? Changed;

    event EventHandler? BatchCompleted;

    /// <summary>Saves pending changes now (exit, Options OK/Apply).</summary>
    Task FlushAsync();
}
