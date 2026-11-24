using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Policy.Tests;

/// <summary>
/// The generated plain-text notices file and its generator <c>scripts/gen-notices.ps1</c> (LIC-010…014). The generator
/// reads local files only; the tests run it with the system's script host.
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Native")]
public sealed class NoticesFileTests
{
    private const string OutOfDate = "THIRD-PARTY-NOTICES.txt is out of date";
    private const string TxtName = "THIRD-PARTY-NOTICES.txt";
    private const string TableName = "THIRD-PARTY-NOTICES.md";
    private static readonly string JsonRelative = Path.Combine("ide", "src", "FreexComp.Ide.Core", "Resources", "ThirdPartyNotices.json");
    private static readonly string Rule = new('=', 80);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private sealed record ScriptResult(int ExitCode, string Output, string Error);

    private static ScriptResult RunScript(params string[] arguments)
    {
        var start = new ProcessStartInfo("powershell")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var argument in (string[])["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                     RepoPaths.Combine("scripts", "gen-notices.ps1"), .. arguments])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = FreexComp.Tests.Shared.ChildPipe.ReadToEndOnThread(process.StandardOutput, "stdout");
        var error = FreexComp.Tests.Shared.ChildPipe.ReadToEndOnThread(process.StandardError, "stderr");
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("gen-notices.ps1 did not finish within two minutes");
        }

        process.WaitForExit();
        return new ScriptResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    /// <summary>A copy of the generator's inputs (and, on request, of the committed output) under a temporary root.</summary>
    private static TempDirectory CopyInputs(bool withOutput)
    {
        var temp = new TempDirectory();
        Directory.CreateDirectory(Path.GetDirectoryName(temp.Combine(JsonRelative))!);
        File.Copy(RepoPaths.Combine(JsonRelative), temp.Combine(JsonRelative));
        File.Copy(RepoPaths.Combine(TableName), temp.Combine(TableName));
        if (withOutput)
        {
            File.Copy(RepoPaths.Combine(TxtName), temp.Combine(TxtName));
        }

        return temp;
    }

    private static void EditJson(TempDirectory temp, Action<JsonArray> edit)
    {
        var path = temp.Combine(JsonRelative);
        var entries = JsonNode.Parse(File.ReadAllText(path, Utf8))!.AsArray();
        edit(entries);
        File.WriteAllText(path, entries.ToJsonString(), Utf8);
    }

    private static JsonObject Entry(JsonArray entries, string name) =>
        entries.Select(e => e!.AsObject()).Single(e => (string?)e["name"] == name);

    [Fact]
    [Trait("Req", "LIC-010")]
    [Trait("Req", "LIC-014")]
    public void GeneratedFileIsUpToDate()
    {
        var result = RunScript("-Check");
        Assert.True(result.ExitCode == 0,
            $"exit code {result.ExitCode}: {result.Output}{result.Error} Regenerate the file with scripts/gen-notices.ps1.");
        Assert.Equal("", result.Output);
    }

    [Fact]
    [Trait("Req", "LIC-010")]
    public void CheckFailsOnStaleFile()
    {
        using var temp = CopyInputs(withOutput: true);
        Assert.Equal(0, RunScript("-Check", "-Root", temp.Path).ExitCode);

        EditJson(temp, entries => Entry(entries, "SQLite")["version"] = "0.0.1-changed");
        var before = File.ReadAllBytes(temp.Combine(TxtName));
        var stale = RunScript("-Check", "-Root", temp.Path);
        Assert.Equal(1, stale.ExitCode);
        Assert.Equal(OutOfDate, stale.Output.TrimEnd('\r', '\n'));
        Assert.Equal(before, File.ReadAllBytes(temp.Combine(TxtName))); // -Check writes nothing

        // -Output names the file to write and to check; the default file stays as it was.
        var other = temp.Combine("other.txt");
        Assert.Equal(1, RunScript("-Check", "-Root", temp.Path, "-Output", other).ExitCode); // a missing file is out of date
        Assert.False(File.Exists(other));
        Assert.Equal(0, RunScript("-Root", temp.Path, "-Output", other).ExitCode);
        Assert.Equal(0, RunScript("-Check", "-Root", temp.Path, "-Output", other).ExitCode);
        Assert.Equal(before, File.ReadAllBytes(temp.Combine(TxtName)));

        var rewrite = RunScript("-Root", temp.Path);
        Assert.True(rewrite.ExitCode == 0, rewrite.Error);
        Assert.Contains("\nSQLite 0.0.1-changed\n", Utf8.GetString(File.ReadAllBytes(temp.Combine(TxtName))), StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(other), File.ReadAllBytes(temp.Combine(TxtName)));
        Assert.Equal(0, RunScript("-Check", "-Root", temp.Path).ExitCode);
    }

    [Fact]
    [Trait("Req", "LIC-011")]
    [Trait("Req", "LIC-012")]
    [Trait("Req", "LIC-013")]
    public void FormatIsCanonical()
    {
        var bytes = File.ReadAllBytes(RepoPaths.Combine(TxtName));
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], "byte order mark");
        var text = Utf8.GetString(bytes);
        Assert.DoesNotContain('\r', text);
        Assert.True(text.EndsWith('\n') && !text.EndsWith("\n\n", StringComparison.Ordinal), "exactly one final LF");
        var lines = text[..^1].Split('\n');
        Assert.All(lines, line => Assert.False(line.EndsWith(' ') || line.EndsWith('\t'), "trailing blank: " + line));

        Assert.Equal(
            (string[])
            [
                "FreexComp third-party notices",
                "",
                "This file is generated by scripts/gen-notices.ps1. Do not edit it.",
                "Each component below is used unmodified under the license shown.",
                "",
                Rule,
            ],
            lines[..6]);

        using var json = JsonDocument.Parse(File.ReadAllBytes(RepoPaths.Combine(JsonRelative)));
        var entries = json.RootElement.EnumerateArray().ToList();
        static string Field(JsonElement entry, string name) => entry.GetProperty(name).GetString()!;
        var names = entries.Select(e => Field(e, "name")).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        var sorted = entries.OrderBy(e => Field(e, "name"), StringComparer.Ordinal).ToList();
        var sources = SourcesByEntry(names);

        // One block per entry, in ordinal order of the names.
        var heads = Enumerable.Range(0, lines.Length - 2)
            .Where(i => lines[i] == Rule && lines[i + 2].StartsWith("License: ", StringComparison.Ordinal))
            .Select(i => lines[i + 1])
            .ToList();
        Assert.Equal(sorted.Select(e => Field(e, "name") + " " + Field(e, "version")), heads);
        Assert.Contains("Source: https://www.sqlite.org/copyright.html", lines);
        Assert.Contains("Packages: Avalonia.AvaloniaEdit", lines);

        // The whole file, built here from the same two tracked sources (§3.1 of the notices specification).
        var expected = new StringBuilder(string.Join('\n', lines[..4]) + "\n");
        foreach (var entry in sorted)
        {
            var name = Field(entry, "name");
            var licenseText = entry.TryGetProperty("text", out var own)
                ? own.GetString()!
                : Field(entries.Single(e => Field(e, "name") == Field(entry, "textOf")), "text");
            expected.Append('\n').Append(Rule).Append('\n');
            expected.Append(name).Append(' ').Append(Field(entry, "version")).Append('\n');
            expected.Append("License: ").Append(Field(entry, "license")).Append('\n');
            expected.Append("Source: ").Append(sources[name]).Append('\n');
            var packages = entry.TryGetProperty("packages", out var list) ? list.EnumerateArray().Select(p => p.GetString()!).ToList() : [];
            if (packages.Count > 0)
            {
                expected.Append("Packages: ").Append(string.Join(", ", packages)).Append('\n');
            }

            expected.Append(Rule).Append("\n\n").Append(Normalize(licenseText)).Append('\n');
        }

        var expectedText = expected.ToString();
        if (expectedText != text)
        {
            var common = Math.Min(expectedText.Length, text.Length);
            var at = Enumerable.Range(0, common).FirstOrDefault(i => expectedText[i] != text[i], common);
            Assert.Fail($"{TxtName} differs from the canonical format at line {text[..at].Count(c => c == '\n') + 1} "
                        + $"(length {text.Length}, expected {expectedText.Length})");
        }
    }

    [Fact]
    [Trait("Req", "LIC-010")]
    public void InputErrorExitsWithTwo()
    {
        // An entry with neither a text nor a resolvable textOf.
        using (var temp = CopyInputs(withOutput: false))
        {
            EditJson(temp, entries =>
            {
                var entry = Entry(entries, "SQLite");
                entry.Remove("text");
                entry["textOf"] = "No such component";
            });
            AssertInputError(temp, "SQLite");
        }

        // An entry with neither of the two.
        using (var temp = CopyInputs(withOutput: false))
        {
            EditJson(temp, entries => Entry(entries, "SQLite").Remove("text"));
            AssertInputError(temp, "SQLite");
        }

        // An entry without a row in the table.
        using (var temp = CopyInputs(withOutput: false))
        {
            EditJson(temp, entries => entries.Add(new JsonObject
            {
                ["name"] = "Unlisted",
                ["version"] = "1.0",
                ["license"] = "MIT",
                ["packages"] = new JsonArray(),
                ["text"] = "text",
            }));
            AssertInputError(temp, "Unlisted");
        }

        // A missing input file.
        using (var temp = CopyInputs(withOutput: false))
        {
            File.Delete(temp.Combine(TableName));
            AssertInputError(temp, TableName);
        }
    }

    private static void AssertInputError(TempDirectory temp, string mentioned)
    {
        foreach (var arguments in (string[][])[["-Root", temp.Path], ["-Check", "-Root", temp.Path]])
        {
            var result = RunScript(arguments);
            Assert.Equal(2, result.ExitCode);
            Assert.Contains(mentioned, result.Error, StringComparison.Ordinal);
            Assert.Equal("", result.Output);
            Assert.False(File.Exists(temp.Combine(TxtName)), "an input error must not write the file");
        }
    }

    private static string Normalize(string text)
    {
        // Only CR LF and CR become LF: a form feed inside a licence text (page break) stays as published.
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd(' ', '\t')).ToList();
        var first = lines.FindIndex(l => l.Length > 0);
        var last = lines.FindLastIndex(l => l.Length > 0);
        return string.Join('\n', lines.Skip(first).Take(last - first + 1));
    }

    /// <summary>LIC-012: the Source cell of the "Shipped with the product" row that belongs to each entry.</summary>
    private static Dictionary<string, string> SourcesByEntry(IReadOnlyList<string> names)
    {
        var table = File.ReadAllLines(RepoPaths.Combine(TableName))
            .SkipWhile(l => l.Trim() != "## Shipped with the product").Skip(1)
            .TakeWhile(l => !l.StartsWith('#'))
            .Where(l => l.StartsWith('|'))
            .Select(l => l.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray())
            .ToList();
        var component = Array.IndexOf(table[0], "Component");
        var source = Array.IndexOf(table[0], "Source");
        Assert.True(component >= 0 && source >= 0);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in table.Skip(2))
        {
            var cell = row[component];
            var name = names
                .Where(n => cell == n || cell.StartsWith(n + " (", StringComparison.Ordinal) || cell.StartsWith(n + ",", StringComparison.Ordinal))
                .MaxBy(n => n.Length);
            Assert.True(name is not null, $"no entry for the table row '{cell}'");
            result.Add(name, row[source]);
        }

        Assert.Equal(names.Order(StringComparer.Ordinal), result.Keys.Order(StringComparer.Ordinal));
        return result;
    }
}
