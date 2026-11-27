using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreexComp.Setup;

/// <summary>
/// <c>H\install.json</c> (INS-010): what one install created, so that uninstall (INS-011) and a reinstall (INS-007)
/// remove exactly that. Registry paths are relative to <see cref="RegistryRoot"/>.
/// </summary>
public sealed record InstallRecord
{
    public const string FileName = "install.json";
    public const string UninstallProgram = "uninstall.exe";

    public int FormatVersion { get; init; } = 1;

    public required string Version { get; init; }

    /// <summary><c>user</c> or <c>machine</c>.</summary>
    public required string Scope { get; init; }

    /// <summary>The registry root every key is under: <c>HKEY_CURRENT_USER</c>, <c>HKEY_LOCAL_MACHINE</c> or a test root below HKCU.</summary>
    public required string RegistryRoot { get; init; }

    /// <summary>Every installed file, relative to H (<c>\</c>), this record excluded.</summary>
    public List<string> Files { get; init; } = [];

    /// <summary>The Start-menu shortcut; null when none was made.</summary>
    public string? Shortcut { get; set; }

    /// <summary>Keys this install created, in creation order.</summary>
    public List<RegistryKeyRecord> Keys { get; init; } = [];

    /// <summary>Values this install wrote outside its own keys (associations of the extensions).</summary>
    public List<RegistryValueRecord> Values { get; init; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static InstallRecord? Read(string home)
    {
        var path = Path.Combine(home, FileName);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(path), Options)
                   ?? throw new SetupException(ExitCode.IoError, $"{path} is empty.");
        }
        catch (JsonException e)
        {
            throw new SetupException(ExitCode.IoError, $"{path} cannot be read: {e.Message}", e);
        }
    }
}

/// <summary>A registry key the install created. <see cref="Tree"/>: the key belongs to the product and is removed with its subkeys;
/// otherwise it is removed only when it is empty again.</summary>
public sealed record RegistryKeyRecord(string Key, bool Tree);

/// <summary>A registry value the install wrote (<see cref="Name"/> "" = the default value), with the value that was there before (null = none).</summary>
public sealed record RegistryValueRecord(string Key, string Name, string Data, string? Previous);
