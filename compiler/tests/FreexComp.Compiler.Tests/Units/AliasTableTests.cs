using System.Text;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>
/// C-164: the unit alias table <c>&lt;rtl root&gt;\unit-aliases.json</c> (LANG-533) and its validity rules (LANG-534, E2209): a
/// missing file is an empty table; keys and values match case-insensitively; aliases never chain; the content hash feeds the
/// options fingerprint (CMP-150).
/// </summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-533")]
public sealed class AliasTableTests
{
    private const string Rtl = @"C:\rtl";
    private const string TablePath = @"C:\rtl\unit-aliases.json";

    private static (UnitAliasTable Table, Diagnostic? Error) Load(string? json)
    {
        var fs = new InMemoryFileSystem();
        if (json is not null)
        {
            fs.Add(TablePath, json);
        }

        var table = UnitAliasTable.Load(fs, Rtl, out var error);
        return (table, error);
    }

    [Fact]
    public void MissingFile_IsAnEmptyTable()
    {
        var (table, error) = Load(null);
        Assert.Null(error);
        Assert.Empty(table.Aliases);
        Assert.False(table.TryGetCanonical("System.SysUtils", out _));
        Assert.Equal("", table.Hash);
    }

    [Fact]
    public void NoRtlRoot_IsAnEmptyTable()
    {
        var table = UnitAliasTable.Load(new InMemoryFileSystem().Add(@"C:\unit-aliases.json", "x"), "", out var error);
        Assert.Null(error);
        Assert.Empty(table.Aliases);
    }

    [Fact]
    public void Aliases_MapToCanonicalNamesCaseInsensitively()
    {
        var (table, error) = Load("""{ "aliases": { "System.SysUtils": "SysUtils", "System.Classes": "Classes", "Winapi.Windows": "Windows", "Old.Utils": "SysUtils" } }""");
        Assert.Null(error);
        Assert.Equal(4, table.Aliases.Count);
        Assert.True(table.TryGetCanonical("SYSTEM.sysutils", out var canonical));
        Assert.Equal("SysUtils", canonical);
        Assert.True(table.TryGetCanonical("old.utils", out canonical));
        Assert.Equal("SysUtils", canonical);
        Assert.False(table.TryGetCanonical("SysUtils", out _));
    }

    [Fact]
    public void Utf8ByteOrderMark_IsAccepted()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllBytes(TablePath, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("""{"aliases":{"A.B":"B"}}""")]);
        var table = UnitAliasTable.Load(fs, Rtl, out var error);
        Assert.Null(error);
        Assert.True(table.TryGetCanonical("A.B", out _));
    }

    [Fact]
    [Trait("Req", "CMP-150")]
    public void Hash_IsTheSha256OfTheFileContent()
    {
        var (a, _) = Load("""{ "aliases": { "A.B": "B" } }""");
        var (same, _) = Load("""{ "aliases": { "A.B": "B" } }""");
        var (other, _) = Load("""{ "aliases": { "A.B": "B", "A.C": "C" } }""");
        Assert.StartsWith("sha256:", a.Hash, StringComparison.Ordinal);
        Assert.Equal(71, a.Hash.Length);
        Assert.Equal(a.Hash, same.Hash);
        Assert.NotEqual(a.Hash, other.Hash);
    }

    [Theory]
    [Trait("Req", "LANG-534")]
    [InlineData("{ \"aliases\": ", "not valid JSON (line 1)")]
    [InlineData("[]", "the root must be an object with exactly the member 'aliases'")]
    [InlineData("{ }", "the root must be an object with exactly the member 'aliases'")]
    [InlineData("{ \"aliases\": {}, \"more\": {} }", "the root must be an object with exactly the member 'aliases'")]
    [InlineData("{ \"aliases\": {}, \"aliases\": {} }", "the root must be an object with exactly the member 'aliases'")]
    [InlineData("{ \"Aliases\": {} }", "the root must be an object with exactly the member 'aliases'")]
    [InlineData("{ \"aliases\": [] }", "'aliases' must be an object")]
    [InlineData("{ \"aliases\": { \"A.B\": 1 } }", "the value of 'A.B' is not a string")]
    [InlineData("{ \"aliases\": { \"A..B\": \"B\" } }", "'A..B' is not a valid unit name")]
    [InlineData("{ \"aliases\": { \"A.begin\": \"B\" } }", "'A.begin' is not a valid unit name")]
    [InlineData("{ \"aliases\": { \"\": \"B\" } }", "'' is not a valid unit name")]
    [InlineData("{ \"aliases\": { \"A.B\": \"1B\" } }", "'1B' is not a valid unit name")]
    [InlineData("{ \"aliases\": { \"A.B\": \"a.b\" } }", "'A.B' maps to itself")]
    [InlineData("{ \"aliases\": { \"A.B\": \"B\", \"B\": \"C\" } }", "'B' is both an alias and a canonical name (aliases are never chained)")]
    [InlineData("{ \"aliases\": { \"A.B\": \"B\", \"a.b\": \"B\" } }", "'a.b' is listed twice")]
    public void InvalidTable_IsE2209(string json, string reason)
    {
        var (table, error) = Load(json);
        Assert.NotNull(error);
        Assert.Equal("E2209", error.Code);
        Assert.Equal($"Invalid unit alias table '{TablePath}': {reason}", error.Message);
        Assert.Equal(TablePath, error.Location.Path);
        Assert.Empty(table.Aliases);
    }

    /// <summary>LANG-534: E2209 stops the compilation before any unit is bound.</summary>
    [Fact]
    [Trait("Req", "LANG-534")]
    public void InvalidTable_StopsTheCompilation()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", "program P; begin x := 1 end.")
            .Add(@"C:\rtl\System.pas", "unit System; interface implementation end.")
            .Add(TablePath, "{ \"aliases\": { \"A\": \"A\" } }");
        var result = Compilation.Create(new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = Rtl }, [], fs)
            .Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("E2209", diagnostic.Code);
    }
}
