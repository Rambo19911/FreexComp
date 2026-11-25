using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Toolchain;

/// <summary>
/// CMP-1317: <c>&lt;objdir&gt;\&lt;base&gt;.res.json</c> — what a compiled script was made from: the compiler, the defines, the include
/// directories and every dependency (path, size, last-write time, SHA-256). The script is compiled again when the record or the
/// output is missing, a recorded value differs or a recorded file is gone.
/// </summary>
internal static class ResourceBuildRecord
{
    private const string TimeFormat = "O";

    public static string PathFor(string compiledPath) => compiledPath + ".json";

    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string Render(IFileSystem fs, string compilerFingerprint, ResourceCompileRequest request, ImmutableArray<string> dependencies)
    {
        var record = new JsonObject
        {
            ["formatVersion"] = 1,
            ["compilerFingerprint"] = compilerFingerprint,
            ["script"] = request.ScriptPath,
            ["defines"] = new JsonArray([.. request.Defines.Select(d => (JsonNode)d)]),
            ["includeDirectories"] = new JsonArray([.. request.IncludeDirectories.Select(d => (JsonNode)d)]),
            ["dependencies"] = new JsonArray([.. dependencies.Select(path => (JsonNode)new JsonObject
            {
                ["path"] = path,
                ["size"] = fs.GetLength(path),
                ["mtime"] = fs.GetLastWriteTimeUtc(path).ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture),
                ["sha256"] = Hash(fs.ReadAllBytes(path)),
            })]),
        };
        return record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>Whether the output of <paramref name="request"/> and its record are there and nothing recorded has changed.</summary>
    public static bool IsUpToDate(IFileSystem fs, string compilerFingerprint, ResourceCompileRequest request)
    {
        try
        {
            var recordPath = PathFor(request.OutputPath);
            if (!fs.FileExists(request.OutputPath) || !fs.FileExists(recordPath) || JsonNode.Parse(fs.ReadAllText(recordPath)) is not JsonObject record)
            {
                return false;
            }

            if (Number(record["formatVersion"]) != 1
                || Text(record["compilerFingerprint"]) != compilerFingerprint
                || !PathUtil.AreEqual(Text(record["script"]) ?? @"C:\", request.ScriptPath)
                || !Strings(record["defines"]).SequenceEqual(request.Defines, StringComparer.Ordinal)
                || !Strings(record["includeDirectories"]).SequenceEqual(request.IncludeDirectories, PathUtil.Comparer)
                || record["dependencies"] is not JsonArray dependencies
                || dependencies.Count == 0)
            {
                return false;
            }

            foreach (var node in dependencies)
            {
                if (node is not JsonObject dependency || Text(dependency["path"]) is not { } path || !fs.FileExists(path))
                {
                    return false;                                           // a recorded file is gone
                }

                // the fast pre-check of CMP-150 rule 4: equal size and last-write time count as equal
                var sameStamp = Number(dependency["size"]) == fs.GetLength(path)
                    && Text(dependency["mtime"]) == fs.GetLastWriteTimeUtc(path).ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);
                if (!sameStamp && Text(dependency["sha256"]) != Hash(fs.ReadAllBytes(path)))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static long? Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;

    private static IEnumerable<string> Strings(JsonNode? node) => node is JsonArray array ? array.Select(n => Text(n) ?? "") : ["\0"];
}
