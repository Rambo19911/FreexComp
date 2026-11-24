using System.Security.Cryptography;
using System.Text.Json;

namespace FreexComp.E2E.Tests;

/// <summary>
/// A-002: the SQLite DLL used by the data-access layer is installed by scripts/bootstrap.ps1 from the pinned package
/// and matches the file hash pinned in scripts/toolchain.json. Fails (never skips) when the toolchain is missing.
/// </summary>
[Trait("Phase", "7")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class ToolchainSqliteTests
{
    [Fact]
    public void Toolchain_SqliteDll_PresentWithPinnedHash()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "toolchain.json")));
        var sqlite = config.RootElement.GetProperty("sqlite");
        var installDir = Path.Combine(RepoPaths.Root, sqlite.GetProperty("installDir").GetString()!);

        foreach (var file in sqlite.GetProperty("requiredFiles").EnumerateArray())
        {
            var path = Path.Combine(installDir, file.GetString()!);
            Assert.True(File.Exists(path), "missing " + path + " (run scripts/bootstrap.ps1)");
        }

        Assert.True(sqlite.TryGetProperty("dllSha256", out var pinned), "scripts/toolchain.json sqlite.dllSha256 is not pinned");
        var dll = Path.Combine(installDir, "sqlite3.dll");
        var actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dll)));
        Assert.Equal(pinned.GetString()!.ToLowerInvariant(), actual);
    }
}
