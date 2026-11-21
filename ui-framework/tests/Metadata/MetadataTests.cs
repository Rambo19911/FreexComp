using FreexComp.MetadataTesting;
using FreexComp.Rtl.Tests.Harness;

namespace FreexComp.UiFramework.Tests;

/// <summary>Group D (testing.md §1; metadata-authoring.md §4): the metadata root <c>ui-framework/design/</c> against the schema and the source.</summary>
[Trait("Category", "Integration")]
public sealed class MetadataTests
{
    /// <summary>
    /// The units whose components file exists so far (U-097 … U-099); <c>MetadataMatchesSource</c> covers every unit of
    /// <c>ui-framework/src</c> with U-100.
    /// </summary>
    public static readonly string[] MetadataUnits =
        ["Controls", "ImgList", "ActnList", "Menus", "Forms", "StdCtrls", "ExtCtrls", "Grids", "Dialogs"];

    public static string DesignDirectory => Path.Combine(UiCorpus.AreaDirectory, "design");

    public static string ControlsStandardPath => Path.Combine(RtlSetup.RepositoryRoot, "docs", "spec", "ui-framework", "controls-standard.md");

    private static readonly Lazy<SourceComponents> Source = new(() => new SourceComponents(UiCorpus.SourceDirectory, RtlSetup.Default.RtlDirectory));

    public static IEnumerable<TheoryDataRow<string>> Units() =>
        MetadataUnits.Select(u => new TheoryDataRow<string>(u) { TestDisplayName = "metadata/" + u });

    [Fact]
    [Trait("Req", "UIF-951")]
    [Trait("Req", "UIF-952")]
    public void MetadataSchemaValid()
    {
        var root = MetadataRoot.Load(DesignDirectory);

        var problems = MetadataSchema.Validate(root, ControlsSpec.Read(ControlsStandardPath));

        Assert.True(problems.Count == 0, MetadataMatch.Report(problems));
    }

    [Theory]
    [Trait("Req", "UIF-950")]
    [Trait("Req", "UIF-955")]
    [MemberData(nameof(Units))]
    public void MetadataMatchesSource(string unit)
    {
        var root = MetadataRoot.Load(DesignDirectory);

        var problems = MetadataMatch.Compare(root, Source.Value, unit);

        Assert.True(problems.Count == 0, MetadataMatch.Report(problems));
    }
}

/// <summary>U-096: the rules of the metadata test library on small fixtures (no repository files).</summary>
public sealed class MetadataLibraryTests : IDisposable
{
    private const string EmptyIndex = "{\n  \"formatVersion\": 1,\n  \"palettePages\": [\"Standard\"],\n  \"files\": []\n}\n";

    private readonly string temp = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));

    public MetadataLibraryTests() => Directory.CreateDirectory(temp);

    public void Dispose() => GoldenRunner.TryDelete(temp);

    [Fact]
    [Trait("Req", "UIF-951")]
    public void MetadataSchema_EmptySetIsValid()
    {
        File.WriteAllText(Path.Combine(temp, "index.json"), EmptyIndex);

        Assert.Empty(MetadataSchema.Validate(MetadataRoot.Load(temp), SchemaExpectations.None));
    }

    [Fact]
    [Trait("Req", "UIF-951")]
    public void MetadataSchema_MissingIndexAndUnlistedFilesAreReported()
    {
        Assert.Contains(MetadataSchema.Validate(MetadataRoot.Load(temp), SchemaExpectations.None), p => p.Contains("index.json: missing"));

        File.WriteAllText(Path.Combine(temp, "index.json"), EmptyIndex);
        File.WriteAllText(Path.Combine(temp, "X.components.json"), "{}\n");

        Assert.Contains(MetadataSchema.Validate(MetadataRoot.Load(temp), SchemaExpectations.None), p => p.Contains("X.components.json exists but is not listed"));
    }

    [Fact]
    [Trait("Req", "UIF-952")]
    public void MetadataSchema_TextRulesAndKeyOrder()
    {
        File.WriteAllText(Path.Combine(temp, "index.json"), "{\r\n  \"files\": [],\r\n  \"formatVersion\": 1\r\n}");

        var problems = MetadataSchema.Validate(MetadataRoot.Load(temp), SchemaExpectations.None);

        Assert.Contains(problems, p => p.Contains("contains CR"));
        Assert.Contains(problems, p => p.Contains("must end with LF"));
        Assert.Contains(problems, p => p.Contains("'formatVersion' out of order"));

        File.WriteAllText(Path.Combine(temp, "index.json"), "{\n  // comment\n  \"formatVersion\": 1, \"files\": []\n}\n");
        Assert.Contains(MetadataSchema.Validate(MetadataRoot.Load(temp), SchemaExpectations.None), p => p.Contains("not strict JSON"));
    }

    [Fact]
    [Trait("Req", "UIF-951")]
    public void MetadataSchema_ComponentRules()
    {
        File.WriteAllText(Path.Combine(temp, "index.json"), EmptyIndex.Replace("[]", "[\"U.components.json\"]", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(temp, "U.components.json"), """
            {
              "formatVersion": 1,
              "unit": "U",
              "types": {
                "TE": { "kind": "enum", "values": ["eA", "eB"] }
              },
              "components": [
                {
                  "class": "TA",
                  "ancestor": "TComponent",
                  "abstract": false,
                  "palette": { "page": "Other", "order": 10, "icon": "icons/TA.svg" },
                  "kind": "wincontrol",
                  "namePrefix": "A",
                  "renderer": "generic",
                  "properties": [
                    { "name": "E", "type": "TE", "default": "eC", "category": "Looks" },
                    { "name": "F", "type": "TMissing" },
                    { "name": "G", "category": "Misc", "type": "Integer" }
                  ],
                  "events": [
                    { "name": "OnX", "signature": "TNotifyEvent", "default": true },
                    { "name": "OnY", "signature": "TNotifyEvent", "default": true }
                  ]
                },
                {
                  "class": "TB",
                  "ancestor": "TNowhere",
                  "abstract": false,
                  "kind": "nonvisual",
                  "namePrefix": "A",
                  "renderer": "nonvisual",
                  "properties": [],
                  "events": []
                }
              ],
              "eventSignatures": {
                "TNotifyEvent": "procedure(Sender: TObject)"
              }
            }

            """.ReplaceLineEndings("\n"));

        var problems = MetadataSchema.Validate(MetadataRoot.Load(temp), SchemaExpectations.None);

        Assert.Contains(problems, p => p.Contains("TA: defaultSize is required"));
        Assert.Contains(problems, p => p.Contains("palette.page 'Other'"));
        Assert.Contains(problems, p => p.Contains("TA.E: category must be one of"));
        Assert.Contains(problems, p => p.Contains("TA.E: default \"eC\": must be a member"));
        Assert.Contains(problems, p => p.Contains("TA.F: type TMissing"));
        Assert.Contains(problems, p => p.Contains("TA.G: key 'type' out of order"));
        Assert.Contains(problems, p => p.Contains("TA: more than one default event"));
        Assert.Contains(problems, p => p.Contains("TB: ancestor TNowhere is not declared"));
        Assert.Contains(problems, p => p.Contains("TB: namePrefix A is also used by TA"));
    }

    [Fact]
    [Trait("Req", "UIF-955")]
    public void MetadataRoot_EffectiveListReplacesInPlace()
    {
        File.WriteAllText(Path.Combine(temp, "index.json"), EmptyIndex.Replace("[]", "[\"U.components.json\"]", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(temp, "U.components.json"), """
            {"formatVersion": 1, "unit": "U", "types": {}, "components": [
              {"class": "TA", "ancestor": "TComponent", "abstract": true, "kind": "control", "properties": [
                {"name": "P", "type": "Integer", "default": 1}, {"name": "Q", "type": "Integer", "default": 2}], "events": []},
              {"class": "TB", "ancestor": "TA", "properties": [{"name": "P", "type": "Integer", "hidden": true}, {"name": "R", "type": "Boolean"}], "events": []}
            ], "eventSignatures": {}}

            """);
        var root = MetadataRoot.Load(temp);

        var list = root.Effective(root.Find("TB")!, "properties");

        Assert.Equal(["P", "Q", "R"], list.Select(p => p["name"].GetString()));
        Assert.Equal(1, list[0]["default"].GetInt32());
        Assert.True(list[0]["hidden"].GetBoolean());
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "UIF-955")]
    public void MetadataMatch_ReportsMissingExtraTypeAndOrderProblems()
    {
        foreach (var f in new[] { "index.json", "Controls.components.json", "ImgList.components.json", "ActnList.components.json", "Menus.components.json" })
        {
            File.Copy(Path.Combine(MetadataTests.DesignDirectory, f), Path.Combine(temp, f));
        }

        var menus = Path.Combine(temp, "Menus.components.json");
        var text = File.ReadAllText(menus)
            .Replace("{ \"name\": \"Caption\", \"type\": \"string\"", "{ \"name\": \"Caption\", \"type\": \"Integer\"", StringComparison.Ordinal)
            .Replace("},\n        { \"name\": \"Visible\", \"type\": \"Boolean\", \"default\": true, \"category\": \"Appearance\" }\n", "}\n", StringComparison.Ordinal)
            .Replace("\"name\": \"RadioItem\"", "\"name\": \"Bogus\"", StringComparison.Ordinal)
            .Replace("{ \"name\": \"OnPopup\", \"signature\": \"TNotifyEvent\"", "{ \"name\": \"OnPopup\", \"signature\": \"TKeyEvent\"", StringComparison.Ordinal)
            .Replace("\"ancestor\": \"TMenu\"", "\"ancestor\": \"TComponent\"", StringComparison.Ordinal);
        File.WriteAllText(menus, text);
        var source = new SourceComponents(UiCorpus.SourceDirectory, RtlSetup.Default.RtlDirectory);

        var problems = MetadataMatch.Compare(MetadataRoot.Load(temp), source, "Menus");

        Assert.Contains("TMenuItem.Caption: type mismatch: metadata Integer, source string", problems);
        Assert.Contains("TMenuItem.RadioItem: missing (metadata position 11 is Bogus)", problems);
        Assert.Contains(problems, p => p.StartsWith("TMenuItem.Visible: missing", StringComparison.Ordinal));
        Assert.Contains("TPopupMenu.OnPopup: signature mismatch: metadata TKeyEvent, source TNotifyEvent", problems);
        Assert.Contains("TMainMenu: ancestor: metadata TComponent, source TMenu", problems);
        Assert.Empty(MetadataMatch.Compare(MetadataRoot.Load(MetadataTests.DesignDirectory), source, "Menus"));
    }

    [Fact]
    [Trait("Req", "UIF-951")]
    public void ControlsSpec_ReadsDefaultEventsAndVerbs()
    {
        var spec = ControlsSpec.Read(MetadataTests.ControlsStandardPath);

        Assert.Equal("OnClick", spec.DefaultEvents["TButton"]);
        Assert.Null(spec.DefaultEvents["TBevel"]);
        Assert.Equal("menu", spec.ComponentEditors["TMenu"]);
        Assert.Equal(new SpecVerb("menuDesigner", "&Menu Designer…", "designer.editComponent", null), Assert.Single(spec.Verbs["TMenu"]));
        Assert.Equal(new SpecVerb("newSeparator", "New &Separator", "designer.newChild", "TToolButton;Style=tbsSeparator;Width=8"), spec.Verbs["TToolBar"][1]);
        Assert.False(spec.Verbs.ContainsKey("TMainMenu"));
    }
}
