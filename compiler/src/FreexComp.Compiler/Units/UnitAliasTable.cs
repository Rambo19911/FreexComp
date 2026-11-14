using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Units;

/// <summary>
/// LANG-533, 534 (C-164): the unit alias table <c>&lt;rtl root&gt;\unit-aliases.json</c>, <c>{ "aliases": { "&lt;alias&gt;":
/// "&lt;canonical name&gt;", … } }</c>. Read once per compilation; a missing file (or no RTL root) is an empty table. Keys and
/// values are unit names compared case-insensitively (LANG-110); a violation of LANG-534 is <c>E2209</c> (the table is then
/// empty and the caller stops the compilation). <see cref="Hash"/> is the file's SHA-256, part of the options fingerprint.
/// </summary>
internal sealed class UnitAliasTable
{
    public const string FileName = "unit-aliases.json";

    private UnitAliasTable(ImmutableDictionary<string, string> aliases, string hash)
    {
        Aliases = aliases;
        Hash = hash;
    }

    public static UnitAliasTable Empty { get; } = new(ImmutableDictionary.Create<string, string>(IdentifierComparer.Instance), "");

    /// <summary>Alias → canonical name, keys compared with <see cref="IdentifierComparer"/>.</summary>
    public ImmutableDictionary<string, string> Aliases { get; }

    /// <summary><c>sha256:&lt;64 hex digits&gt;</c> of the file's bytes; "" when there is no file.</summary>
    public string Hash { get; }

    public bool TryGetCanonical(string alias, out string canonical) => Aliases.TryGetValue(alias, out canonical!);

    /// <summary>Reads <c>&lt;<paramref name="rtlRoot"/>&gt;\unit-aliases.json</c>; <paramref name="error"/> is the E2209 of an invalid table.</summary>
    public static UnitAliasTable Load(IFileSystem fileSystem, string rtlRoot, out Diagnostic? error)
    {
        error = null;
        if (rtlRoot.Length == 0)
        {
            return Empty;
        }

        var path = PathUtil.Normalize(Path.Combine(rtlRoot, FileName));
        if (!fileSystem.FileExists(path))
        {
            return Empty;
        }

        byte[] bytes;
        try
        {
            bytes = fileSystem.ReadAllBytes(path);
        }
        catch (IOException e)
        {
            error = Diagnostic.Create("E2209", Location.ForFile(path), path, e.Message);
            return Empty;
        }

        if (Parse(bytes, out var reason) is not { } aliases)
        {
            error = Diagnostic.Create("E2209", Location.ForFile(path), path, reason!);
            return Empty;
        }

        return new UnitAliasTable(aliases, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static ImmutableDictionary<string, string>? Parse(byte[] bytes, out string? reason)
    {
        const string RootShape = "the root must be an object with exactly the member 'aliases'";
        JsonDocument document;
        try
        {
            var bom = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
            document = JsonDocument.Parse(bytes.AsMemory(bom));
        }
        catch (JsonException e)
        {
            reason = $"not valid JSON (line {(e.LineNumber ?? 0) + 1})";
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("aliases", out var members))
            {
                reason = RootShape;
                return null;
            }

            if (members.ValueKind != JsonValueKind.Object)
            {
                reason = "'aliases' must be an object";
                return null;
            }

            var aliases = ImmutableDictionary.CreateBuilder<string, string>(IdentifierComparer.Instance);
            foreach (var member in members.EnumerateObject())
            {
                if (member.Value.ValueKind != JsonValueKind.String)
                {
                    reason = $"the value of '{member.Name}' is not a string";
                    return null;
                }

                var value = member.Value.GetString()!;
                reason = !IsUnitName(member.Name) ? $"'{member.Name}' is not a valid unit name"
                    : !IsUnitName(value) ? $"'{value}' is not a valid unit name"
                    : IdentifierComparer.Instance.Equals(member.Name, value) ? $"'{member.Name}' maps to itself"
                    : aliases.ContainsKey(member.Name) ? $"'{member.Name}' is listed twice"
                    : null;
                if (reason is not null)
                {
                    return null;
                }

                aliases.Add(member.Name, value);
            }

            // aliases are never chained: in key order, the first value that is itself a key
            foreach (var member in members.EnumerateObject())
            {
                var value = member.Value.GetString()!;
                if (aliases.ContainsKey(value))
                {
                    reason = $"'{value}' is both an alias and a canonical name (aliases are never chained)";
                    return null;
                }
            }

            reason = null;
            return aliases.ToImmutable();
        }
    }

    /// <summary>LANG-152: one or more identifiers (no reserved words) joined by dots.</summary>
    private static bool IsUnitName(string text) => text.Split('.').All(SyntaxFacts.IsValidIdentifier);
}
