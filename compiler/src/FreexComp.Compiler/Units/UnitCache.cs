using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Units;

/// <summary>What a unit depends on now (CMP-150 rule 5): the file it resolves to and its current interface hash.</summary>
internal sealed record UnitCacheDependency(string Path, string InterfaceHash);

/// <summary>
/// C-075: the up-to-date decision of CMP-150. A unit is up to date when its <c>.pcu</c> and <c>.obj</c> exist and the <c>.pcu</c>
/// parses with the current format (rule 1), carries the current compiler (rule 2) and options (rule 3) fingerprints, every recorded
/// source still has the recorded SHA-256 (rule 4; equal size and last-write time count as equal, open files are hashed) and every
/// recorded dependency resolves to the same file with the same current interface hash (rule 5). CMP-1490 (e) (C-443): a
/// <c>.pcu</c> whose record helpers do not record their mutating methods is out of date as well. Each check returns null when it
/// holds, else the reason (<c>CMP-150 rule N: …</c>).
/// </summary>
/// <param name="openFile">The text of a file open in the compilation (it wins over the disk), or null.</param>
internal sealed class UnitCache(IFileSystem fs, string compilerFingerprint, string optionsFingerprint, Func<string, string?>? openFile = null)
{
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    /// <summary>Rules 1–5 for the <c>.pcu</c> of a source unit in the object directory. <paramref name="pcu"/> receives the parsed file when up to date.</summary>
    /// <param name="dependency">The current state of a dependency by canonical unit name; null when the compilation has no such unit.</param>
    /// <param name="sourcePath">The file the unit resolves to now; the <c>.pcu</c>'s first source must be that file (C-148: a search-path
    /// change that makes the unit resolve to another file recompiles it, build-engine.md §5). Null: not compared.</param>
    public string? Check(string pcuPath, string objPath, Func<string, UnitCacheDependency?> dependency, out JsonObject? pcu, string? sourcePath = null)
    {
        pcu = null;
        if (!fs.FileExists(pcuPath) || !fs.FileExists(objPath))
        {
            return "CMP-150 rule 1: no .pcu or .obj";
        }

        JsonObject? parsed;
        try
        {
            parsed = PcuReader.Parse(fs.ReadAllText(pcuPath));
        }
        catch (IOException)
        {
            parsed = null;
        }

        if (parsed is null)
        {
            return "CMP-150 rule 1: the .pcu does not parse with the current format";
        }

        var reason = Fingerprints(parsed) ?? SameSource(parsed, sourcePath) ?? Sources(parsed) ?? Dependencies(parsed, dependency, comparePaths: true)
                     ?? (PcuReader.HelpersAreClassified(parsed) ? null : "CMP-1490 (e): a record helper without its mutating methods")
                     ?? (PcuReader.ClassHelpersHaveClassSelf(parsed) ? null : "CMP-1490 (e): a class helper without the Self of its class methods");
        pcu = reason is null ? parsed : null;
        return reason;
    }

    /// <summary>
    /// The rules that apply to a precompiled unit without its source (C-074): fingerprints (rules 2, 3) and the dependencies'
    /// interface hashes (rule 5; their recorded paths are those of the machine that compiled the unit, so they are not compared).
    /// </summary>
    public string? CheckPrecompiled(JsonObject pcu, Func<string, UnitCacheDependency?> dependency) =>
        Fingerprints(pcu) ?? Dependencies(pcu, dependency, comparePaths: false);

    /// <summary>Rules 2 and 3.</summary>
    public string? Fingerprints(JsonObject pcu)
    {
        if (Text(pcu, "compilerFingerprint") != compilerFingerprint)
        {
            return "CMP-150 rule 2: another compiler fingerprint";
        }

        return Text(pcu, "optionsFingerprint") != optionsFingerprint ? "CMP-150 rule 3: another options fingerprint" : null;
    }

    /// <summary>Rule 4 for the unit's own file: the <c>.pcu</c> was compiled from <paramref name="sourcePath"/> (null → not compared).</summary>
    private static string? SameSource(JsonObject pcu, string? sourcePath) =>
        sourcePath is null || (pcu["sources"] is JsonArray { Count: > 0 } sources && Text(sources[0], "path") is { } recorded && PathUtil.AreEqual(recorded, sourcePath))
            ? null
            : "CMP-150 rule 4: the unit now resolves to " + sourcePath;

    /// <summary>Rule 4.</summary>
    public string? Sources(JsonObject pcu)
    {
        if (pcu["sources"] is not JsonArray sources)
        {
            return "CMP-150 rule 1: no sources";
        }

        foreach (var source in sources)
        {
            var path = Text(source, "path");
            var sha256 = Text(source, "sha256");
            if (path is null || sha256 is null)
            {
                return "CMP-150 rule 1: a source entry without path or hash";
            }

            if (openFile?.Invoke(path) is { } text)
            {
                if (Hash(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text)) != sha256)
                {
                    return "CMP-150 rule 4: " + path + " changed";
                }

                continue;
            }

            if (!fs.FileExists(path))
            {
                return "CMP-150 rule 4: " + path + " is missing";
            }

            try
            {
                var sameStamp = source!["size"] is JsonValue size && size.TryGetValue<long>(out var recordedSize) && recordedSize == fs.GetLength(path)
                    && Text(source, "mtime") == fs.GetLastWriteTimeUtc(path).ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);
                if (!sameStamp && Hash(fs.ReadAllBytes(path)) != sha256)
                {
                    return "CMP-150 rule 4: " + path + " changed";
                }
            }
            catch (IOException)
            {
                return "CMP-150 rule 4: " + path + " cannot be read";
            }
        }

        return null;
    }

    /// <summary>Rule 5 (the dependencies are brought up to date first: <paramref name="dependency"/> gives their current state).</summary>
    public static string? Dependencies(JsonObject pcu, Func<string, UnitCacheDependency?> dependency, bool comparePaths)
    {
        if (pcu["dependencies"] is not JsonArray dependencies)
        {
            return "CMP-150 rule 1: no dependencies";
        }

        foreach (var entry in dependencies)
        {
            var unit = Text(entry, "unit");
            if (unit is null || dependency(unit) is not { } current)
            {
                return "CMP-150 rule 5: unit " + unit + " is no longer part of the compilation";
            }

            if (comparePaths && !PathUtil.AreEqual(Text(entry, "path") ?? "", current.Path))
            {
                return "CMP-150 rule 5: unit " + unit + " resolves to " + current.Path;
            }

            if (Text(entry, "interfaceHash") != current.InterfaceHash)
            {
                return "CMP-150 rule 5: the interface of " + unit + " changed";
            }
        }

        return null;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string? Text(JsonNode? owner, string key) =>
        owner?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
