using System.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Text;

/// <summary>
/// The data-driven editor-operation cases of <c>TestData/editor-ops/*.txt</c> (testing.md §4.3, IDE-TEST-021). Each
/// case runs against a real composition: the document is opened through the document service, commands go through
/// the command service, keys first through the keymap (Editor scope) and then to the document's key handling, exactly
/// as the code editor view routes them.
/// <para>Format additions (lead to merge into §4.3): <c># readonly: true</c> opens the document read-only;
/// <c># status: &lt;resource name&gt;</c> expects that status-bar message afterwards. A selection whose caret is at its
/// start is written <c>|‹…›</c>. A case with a single action that changes the text must be undone by one undo step.</para>
/// </summary>
[Trait("Phase", "1")]
public sealed class EditorOperationsTests
{
    private static string CaseDirectory => Path.Combine(AppContext.BaseDirectory, "TestData", "editor-ops");

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var c in LoadCases())
            data.Add(c.Key);
        return data;
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Cases))]
    [Trait("Req", "IDE-TEST-021")]
    public async Task DataDrivenCases(string key)
    {
        var c = LoadCases().Single(x => x.Key == key);
        var eol = c.Eol == "lf" ? "\n" : "\r\n";
        var before = Marked.Parse(c.Before, eol);
        var expected = Marked.Parse(c.After, eol);

        var fs = new InMemoryFileSystem();
        var services = TestComposition.Build(o => o.FileSystem = fs);
        foreach (var (name, value) in c.Settings)
            Set(services.Settings, name, value);
        var path = @"C:\t\Case" + c.Language switch
        {
            "pascal" => ".pas",
            "form" => ".form",
            "json" => ".json",
            "plaintext" => ".txt",
            _ => throw new InvalidOperationException("Unknown language " + c.Language),
        };
        fs.CreateDirectory(@"C:\t");
        fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(before.Text));
        var opened = await services.Documents.OpenAsync(path);
        Assert.True(opened.Success, opened.MessageId);
        var doc = Assert.IsType<TextDocumentViewModel>(opened.Value);
        Assert.Equal(c.Language, doc.Language.Id);
        doc.Buffer.IsReadOnly = c.ReadOnly;
        doc.ViewState.SelectionAnchor = before.Anchor == before.Caret ? null : doc.Buffer.GetPosition(before.Anchor);
        doc.ViewState.Caret = doc.Buffer.GetPosition(before.Caret);

        foreach (var action in c.Actions)
            await RunAsync(services, doc, action);

        var caret = doc.Buffer.GetOffset(doc.ViewState.Caret.Line, doc.ViewState.Caret.Column);
        var anchor = doc.ViewState.SelectionAnchor is { } a ? doc.Buffer.GetOffset(a.Line, a.Column) : caret;
        var actual = new Marked(doc.Buffer.Text, anchor, caret);
        Assert.True(expected == actual, $"{c.Name}\n--- expected\n{expected.Render()}\n--- actual\n{actual.Render()}");
        if (c.Status is { } status)
            Assert.Equal(Strings.ResourceManager.GetString(status, Strings.Culture), services.Status.Message);

        // Every operation is one undo step (IDE-ED-002, IDE-ED-037, editor-features.md §4).
        var steps = c.Actions.Sum(a => a.StartsWith("type ", StringComparison.Ordinal) ? a.Length - 5 : 1); // one per typed character
        if (steps == 1 && actual.Text != before.Text)
        {
            doc.Buffer.Undo();
            Assert.Equal(before.Text, doc.Buffer.Text);
        }
    }

    /// <summary>Rules of editor-features.md §2–§4 and §7 whose behaviour arrives with a later task (no case yet).</summary>
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal)
    {
        ["IDE-ED-024"] = "I-120", ["IDE-ED-038"] = "I-054",
        ["IDE-ED-039"] = "I-055", ["IDE-ED-040"] = "I-055", ["IDE-ED-070"] = "I-052", ["IDE-ED-071"] = "I-052",
        ["IDE-ED-072"] = "I-052", ["IDE-ED-073"] = "I-052", ["IDE-ED-074"] = "I-053",
    };

    /// <summary>Rules the case format cannot express; a test elsewhere under <c>ide/tests</c> carries their <c>Req</c> trait.</summary>
    private static readonly string[] CoveredElsewhere = ["IDE-ED-010", "IDE-ED-012", "IDE-ED-026", "IDE-ED-045", "IDE-ED-046"];

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TEST-021")]
    [Trait("Category", "Spec")]
    public void EveryRuleHasACase()
    {
        var spec = File.ReadAllLines(SpecTables.DocPath("spec/ide/editor-features.md"));
        var rules = new List<string>();
        var inScope = false;
        foreach (var line in spec)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
                inScope = line.StartsWith("## 2.", StringComparison.Ordinal) || line.StartsWith("## 3.", StringComparison.Ordinal)
                    || line.StartsWith("## 4.", StringComparison.Ordinal) || line.StartsWith("## 7.", StringComparison.Ordinal);
            if (inScope && System.Text.RegularExpressions.Regex.Match(line, @"^\| (IDE-ED-0\d\d) \|") is { Success: true } m)
                rules.Add(m.Groups[1].Value);
        }

        Assert.Contains("IDE-ED-001", rules);
        Assert.Contains("IDE-ED-045", rules); // §4.7 belongs to §4
        var withCase = LoadCases().Select(c => c.Name.Split(' ')[0]).ToHashSet(StringComparer.Ordinal);
        var testSources = string.Join("\n", Directory.GetFiles(Path.Combine(SpecTables.RepoRootPath(), "ide", "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));

        var problems = new List<string>();
        foreach (var rule in rules)
        {
            var kinds = (withCase.Contains(rule) ? 1 : 0) + (Pending.ContainsKey(rule) ? 1 : 0) + (CoveredElsewhere.Contains(rule) ? 1 : 0);
            if (kinds != 1)
                problems.Add($"{rule}: needs exactly one of a case, a Pending entry or a CoveredElsewhere entry (has {kinds})");
            if (CoveredElsewhere.Contains(rule) && !testSources.Contains($"[Trait(\"Req\", \"{rule}\")]", StringComparison.Ordinal))
                problems.Add($"{rule}: no test carries [Trait(\"Req\", \"{rule}\")]");
        }

        problems.AddRange(withCase.Concat(Pending.Keys).Concat(CoveredElsewhere).Where(r => !rules.Contains(r)).Select(r => r + ": not a rule of §2–§4, §7"));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static async Task RunAsync(IdeServices services, TextDocumentViewModel doc, string action)
    {
        var space = action.IndexOf(' ', StringComparison.Ordinal);
        var verb = space < 0 ? action : action[..space];
        var rest = space < 0 ? "" : action[(space + 1)..];
        switch (verb)
        {
            case "command":
                var parts = rest.Split(' ', 2);
                await services.Commands.ExecuteAsync(parts[0], parts.Length > 1 ? parts[1] : null);
                break;
            case "key":
                // The code editor view: the keymap first (Editor scope), then the document's own key handling.
                if (!services.Commands.HandleKey(KeyGesture.Parse(rest), CommandScope.Editor, null))
                    Assert.True(doc.HandleKey(rest), "Key not handled: " + rest);
                break;
            case "type":
                foreach (var ch in rest.Replace('·', ' ').Replace('→', '\t'))
                    doc.TypeText(ch.ToString());
                break;
            default:
                throw new InvalidOperationException("Unknown action " + action);
        }
    }

    private static void Set(ISettingsService settings, string name, string value)
    {
        if (name == SettingsCatalog.EditorTabWidth.Name)
            settings.Set(SettingsCatalog.EditorTabWidth, int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        else if (name == SettingsCatalog.EditorInsertSpaces.Name)
            settings.Set(SettingsCatalog.EditorInsertSpaces, bool.Parse(value));
        else if (name == SettingsCatalog.EditorAutoIndent.Name)
            settings.Set(SettingsCatalog.EditorAutoIndent, bool.Parse(value));
        else if (name == SettingsCatalog.EditorSmartHome.Name)
            settings.Set(SettingsCatalog.EditorSmartHome, bool.Parse(value));
        else
            throw new InvalidOperationException("Setting not supported by the case runner: " + name);
    }

    private sealed record Case(string Key, string Name, string Language, string Eol, bool ReadOnly, string? Status,
        IReadOnlyList<(string Name, string Value)> Settings, string Before, IReadOnlyList<string> Actions, string After);

    private static List<Case> LoadCases()
    {
        var cases = new List<Case>();
        foreach (var file in Directory.GetFiles(CaseDirectory, "*.txt").Order(StringComparer.Ordinal))
        {
            var lines = File.ReadAllText(file, Encoding.UTF8).Split('\n');
            Assert.DoesNotContain(lines, l => l.EndsWith('\r')); // case files are LF (testing.md §4.3)
            var chunk = new List<string>();
            foreach (var line in lines.Append("===="))
            {
                if (line == "====")
                {
                    if (chunk.Any(l => l.Length > 0))
                        cases.Add(ParseCase(Path.GetFileNameWithoutExtension(file), chunk));
                    chunk.Clear();
                }
                else
                {
                    chunk.Add(line);
                }
            }
        }

        var duplicates = cases.GroupBy(c => c.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate case names: " + string.Join(", ", duplicates));
        return cases;
    }

    private static Case ParseCase(string file, List<string> lines)
    {
        // The last case of a file ends with the file's final line break.
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines = lines[..^1];
        string? name = null, status = null;
        string language = "pascal", eol = "crlf";
        var readOnly = false;
        var settings = new List<(string, string)>();
        int i = 0;
        for (; i < lines.Count && !lines[i].StartsWith("--- ", StringComparison.Ordinal); i++)
        {
            if (!lines[i].StartsWith('#'))
                continue;
            var header = lines[i][1..].Trim();
            var colon = header.IndexOf(':', StringComparison.Ordinal);
            var (k, v) = (header[..colon].Trim(), header[(colon + 1)..].Trim());
            switch (k)
            {
                case "name": name = v; break;
                case "language": language = v; break;
                case "eol": eol = v; break;
                case "readonly": readOnly = bool.Parse(v); break;
                case "status": status = v; break;
                case "settings":
                    foreach (var pair in v.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        settings.Add((pair[..pair.IndexOf('=', StringComparison.Ordinal)], pair[(pair.IndexOf('=', StringComparison.Ordinal) + 1)..]));
                    break;
                default: throw new InvalidOperationException($"{file}: unknown header '{k}'");
            }
        }

        Assert.False(name is null, $"{file}: case without '# name:'");
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? current = null;
        for (; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("--- ", StringComparison.Ordinal))
                sections[lines[i][4..].Trim()] = current = [];
            else
                current!.Add(lines[i]);
        }

        Assert.True(sections.ContainsKey("before") && sections.ContainsKey("action") && sections.ContainsKey("after"), $"{file}: {name}: sections");
        return new Case(file + ": " + name, name!, language, eol, readOnly, status, settings, string.Join("\n", sections["before"]),
            [.. sections["action"].Where(l => l.Length > 0)], string.Join("\n", sections["after"]));
    }

    /// <summary>Text with caret and selection (testing.md §4.3 markers).</summary>
    private readonly record struct Marked(string Text, int Anchor, int Caret)
    {
        public static Marked Parse(string marked, string eol)
        {
            var text = new StringBuilder();
            int? caret = null, start = null, end = null;
            foreach (var ch in marked.Replace("\n", eol, StringComparison.Ordinal))
            {
                switch (ch)
                {
                    case '·': text.Append(' '); break;
                    case '→': text.Append('\t'); break;
                    case '|': caret = text.Length; break;
                    case '‹': start = text.Length; break;
                    case '›': end = text.Length; break;
                    default: text.Append(ch); break;
                }
            }

            if (start is { } s && end is { } e)
                return caret == s ? new Marked(text.ToString(), e, s) : new Marked(text.ToString(), s, e);
            Assert.True(caret is not null && start is null && end is null, "Markers: '|' or a complete '‹…›' in\n" + marked);
            return new Marked(text.ToString(), caret!.Value, caret.Value);
        }

        public string Render()
        {
            var marks = new SortedDictionary<int, string>();
            if (Anchor == Caret)
                marks[Caret] = "|";
            else if (Anchor < Caret)
                (marks[Anchor], marks[Caret]) = ("‹", "›");
            else
                (marks[Caret], marks[Anchor]) = ("|‹", "›");
            var sb = new StringBuilder();
            for (int i = 0; i <= Text.Length; i++)
            {
                if (marks.TryGetValue(i, out var m))
                    sb.Append(m);
                if (i < Text.Length)
                    sb.Append(Text[i] switch { ' ' => '·', '\t' => '→', '\r' => '␍', var other => other });
            }

            return sb.ToString();
        }
    }
}
