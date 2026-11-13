using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Infrastructure;

namespace FreexComp.Ide.Core.Tests.Resources;

[Trait("Phase", "1")]
public sealed partial class MessageCatalogTests
{
    /// <summary>(spec file, heading) of every message table: states-and-messages.md §2–§8 and the additions of its §10.</summary>
    private static readonly (string File, string Heading)[] MessageTables =
    [
        ("spec/ui/states-and-messages.md", "2. "),
        ("spec/ui/states-and-messages.md", "3. "),
        ("spec/ui/states-and-messages.md", "4. "),
        ("spec/ui/states-and-messages.md", "5. "),
        ("spec/ui/states-and-messages.md", "6. "),
        ("spec/ui/states-and-messages.md", "7. "),
        ("spec/ui/states-and-messages.md", "8. "),
        ("spec/ui/tool-windows.md", "17.2 "),
        ("spec/ui/editor.md", "13.1 "),
        ("spec/ui/dialogs.md", "24.1 "),
        ("spec/ui/dialogs.md", "27.1 "),
        ("spec/designer/design-surface.md", "15. Additions"),
        ("spec/designer/code-sync.md", "13. Additions"),
    ];

    [Fact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-MSG-001")]
    public void SpecMatchesResx()
    {
        var expected = ExpectedResources();
        var actual = ResxEntries();
        var problems = new List<string>();

        foreach (var (key, text) in expected)
        {
            if (!actual.TryGetValue(key, out var value))
            {
                problems.Add($"{key}: missing from Strings.resx");
            }
            else if (value != text)
            {
                problems.Add($"{key}: resx \"{value}\", spec \"{text}\"");
            }
        }

        // Keys of the spec-defined families must all come from the spec.
        var families = expected.Keys.Select(k => k[..(k.IndexOf('_') + 1)]).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(actual.Keys
            .Where(k => families.Any(f => k.StartsWith(f, StringComparison.Ordinal)) && !expected.ContainsKey(k))
            .Select(k => $"{k}: in Strings.resx but not in the spec"));

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-MSG-001")]
    public void IndexOfAdditionsIsCovered()
    {
        var index = SpecTables.Read(SpecTables.DocPath("spec/ui/states-and-messages.md"), "10. ");
        var expected = ExpectedResources();

        var listed = index.SelectMany(r => IdPattern().Matches(r["Message ids"]).Select(m => m.Value)).ToList();

        Assert.NotEmpty(listed);
        Assert.All(listed, id => Assert.True(expected.ContainsKey(Key(id)), $"{id} is indexed in §10 but no table defines it"));
    }

    [Fact(Timeout = 30000)]
    public void GeneratedAccessorReadsResources()
    {
        Assert.Equal("Ready", Strings.Msg_State_Idle);
        Assert.Equal("The file cannot be saved.\n{0}\n{1}", Strings.Msg_File_SaveFailed);
        Assert.Equal("FreexComp", Strings.App_ProductName);
    }

    private static Dictionary<string, string> ExpectedResources()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string key, string text)
        {
            if (result.TryGetValue(key, out var existing))
            {
                Assert.True(existing == text, $"{key} is defined twice with different texts");
            }

            result[key] = text;
        }

        foreach (var (file, heading) in MessageTables)
        {
            foreach (var row in SpecTables.Read(SpecTables.DocPath(file), heading))
            {
                var text = row["Text"];
                if (text.StartsWith("*(", StringComparison.Ordinal))
                {
                    continue; // "— (no message)" rows
                }

                var key = Key(row["Id"]);
                Add(key, Paragraphs(text));
                var secondary = SecondaryPattern().Match(row["Placeholders"]);
                if (secondary.Success)
                {
                    Add(key + "_Secondary", secondary.Groups[1].Value);
                }
            }
        }

        // docs/spec/ide/README.md §5: plain resource keys; one row may list several keys with texts joined by " · ".
        foreach (var row in SpecTables.Read(SpecTables.DocPath("spec/ide/README.md"), "5. "))
        {
            var keys = row["Resource key"].Split(" (")[0].Split(", ");
            var texts = keys.Length == 1 ? [row["Text"]] : row["Text"].Split(" · ");
            Assert.Equal(keys.Length, texts.Length);
            for (var i = 0; i < keys.Length; i++)
            {
                Add(Key(keys[i]), Paragraphs(texts[i]));
            }
        }

        // dialogs.md §24.1: "Encoding display names (resources `Encoding_<id>`): `utf-8` → `UTF-8`; …"
        var dialogs = File.ReadAllText(SpecTables.DocPath("spec/ui/dialogs.md"));
        var start = dialogs.IndexOf("Encoding display names", StringComparison.Ordinal);
        Assert.True(start >= 0, "Encoding display names paragraph not found");
        var paragraph = dialogs[start..dialogs.IndexOf("\n\n", start, StringComparison.Ordinal)];
        var names = EncodingPattern().Matches(paragraph);
        Assert.Equal(5, names.Count);
        foreach (Match m in names)
        {
            Add("Encoding_" + m.Groups[1].Value.Replace('-', '_'), m.Groups[2].Value);
        }

        return result;
    }

    private static Dictionary<string, string> ResxEntries()
    {
        var set = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!;
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.Ordinal);
    }

    /// <summary>UI-MSG-001: <c>.</c> → <c>_</c>.</summary>
    private static string Key(string id) => id.Trim().Replace('.', '_');

    /// <summary>UI-DLG-021: <c>⏎</c> starts a new paragraph; stored as a line feed.</summary>
    private static string Paragraphs(string text) => text.Replace("⏎", "\n", StringComparison.Ordinal);

    [GeneratedRegex(@"Msg(\.[A-Za-z]+)+")]
    private static partial Regex IdPattern();

    [GeneratedRegex("secondary: \"([^\"]*)\"")]
    private static partial Regex SecondaryPattern();

    [GeneratedRegex(@"`([a-z0-9-]+)` → `([^`]+)`")]
    private static partial Regex EncodingPattern();
}
