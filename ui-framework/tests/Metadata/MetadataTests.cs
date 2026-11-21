using FreexComp.MetadataTesting;
using FreexComp.Rtl.Tests.Harness;

namespace FreexComp.UiFramework.Tests;

/// <summary>Group D (testing.md §1; metadata-authoring.md §4): the metadata root <c>ui-framework/design/</c> against the schema and the source.</summary>
[Trait("Category", "Integration")]
public sealed class MetadataTests
{
    /// <summary>
    /// UIF-950 (U-100): every unit of <c>ui-framework/src</c> that declares a <c>TComponent</c> class in its interface has a
    /// components file; the list is taken from the source, never written by hand.
    /// </summary>
    public static string[] MetadataUnits => Source.Value.ComponentUnits();

    public static string DesignDirectory => Path.Combine(UiCorpus.AreaDirectory, "design");

    public static string ControlsStandardPath => Path.Combine(RtlSetup.RepositoryRoot, "docs", "spec", "ui-framework", "controls-standard.md");

    internal static readonly Lazy<SourceComponents> Source = new(() => new SourceComponents(UiCorpus.SourceDirectory, RtlSetup.Default.RtlDirectory));

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

    [Fact]
    [Trait("Req", "UIF-957")]
    [Trait("Req", "UIF-960")]
    public void MetadataSignaturesMatchSource()
    {
        var problems = MetadataDecls.SignaturesMatchSource(MetadataRoot.Load(DesignDirectory), Source.Value);

        Assert.True(problems.Count == 0, MetadataMatch.Report(problems));
    }

    [Fact]
    [Trait("Req", "UIF-957")]
    [Trait("Req", "UIF-960")]
    public void MetadataSharedDeclsAgree()
    {
        var problems = MetadataDecls.SharedDeclsAgree(MetadataRoot.Load(DesignDirectory));

        Assert.True(problems.Count == 0, MetadataMatch.Report(problems));
    }

    public static string MetadataAuthoringPath => Path.Combine(RtlSetup.RepositoryRoot, "docs", "spec", "ui-framework", "metadata-authoring.md");

    [Fact]
    [Trait("Req", "UIF-955")]
    public void MetadataCategoriesFollowSpec()
    {
        var problems = MetadataSpecChecks.CategoriesFollowSpec(MetadataRoot.Load(DesignDirectory), MetadataAuthoringPath);

        Assert.True(problems.Count == 0, MetadataMatch.Report(problems));
    }

    [Fact]
    [Trait("Req", "UIF-951")]
    public void MetadataMatchesControlsSpec()
    {
        var problems = MetadataSpecChecks.MatchesControlsSpec(MetadataRoot.Load(DesignDirectory), ControlsStandardPath);

        Assert.True(problems.Count == 0, MetadataMatch.Report(problems));
    }

    [Theory]
    [Trait("Req", "UIF-950")]
    [Trait("Req", "UIF-955")]
    [Trait("Req", "UIF-970")]                  // source and metadata change together: any drift fails here
    [MemberData(nameof(Units))]
    public void MetadataMatchesSource(string unit)
    {
        var root = MetadataRoot.Load(DesignDirectory);

        var problems = MetadataMatch.Compare(root, Source.Value, unit);

        Assert.True(problems.Count == 0, MetadataMatch.Report(problems));
    }
}

/// <summary>Group D facts that compile and run a generated Pascal program (testing.md §1: Kind=Native, Category=Toolchain).</summary>
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
[Trait("Phase", "4")]
public sealed class MetadataRuntimeTests
{
    [Fact]
    [Trait("Req", "UIF-956")]
    public async Task MetadataDefaultsMatchRuntime()
    {
        var (program, checks) = MetadataDefaults.Generate(MetadataRoot.Load(MetadataTests.DesignDirectory));

        var output = await CompileAndRunAsync(MetadataDefaults.ProgramName, program);
        var problems = MetadataDefaults.Compare(checks, output);

        Assert.True(problems.Count == 0, MetadataMatch.Report(problems));
    }

    [Fact]
    [Trait("Req", "UIF-002")]
    public async Task Reg_AllClassesRegistered()
    {
        var root = MetadataRoot.Load(MetadataTests.DesignDirectory);
        var expected = root.Components.Count(c => !c.Abstract);

        var output = await CompileAndRunAsync(MetadataRegistration.ProgramName, MetadataRegistration.Generate(root));

        Assert.Equal($"checked {expected}\n", output);
    }

    [Fact]
    [Trait("Req", "UIF-002")]
    public async Task Reg_ReportsAnUnregisteredClass()
    {
        var root = MetadataRoot.Load(MetadataTests.DesignDirectory);
        var program = MetadataRegistration.Generate(root).Replace("  Check('TButton');\n", "  Check('TButton');\n  Check('TCustomEdit');\n", StringComparison.Ordinal);

        var output = await CompileAndRunAsync(MetadataRegistration.ProgramName, program);

        Assert.Equal($"TCustomEdit: not registered\nchecked {root.Components.Count(c => !c.Abstract) + 1}\n", output);
    }

    /// <summary>Writes a generated program to a fresh directory, compiles it as group A programs are compiled, runs it; returns stdout (LF).</summary>
    private static async Task<string> CompileAndRunAsync(string name, string program)
    {
        var work = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var source = Path.Combine(work, name + ".pas");
            File.WriteAllText(source, program);
            var ct = TestContext.Current.CancellationToken;

            var (exe, failure) = await LayoutHost.CompileAsync(source, ct);
            Assert.True(exe is not null, failure + "\n--- generated program\n" + program);
            var run = await LayoutHost.RunProcessAsync(exe, "", work, LayoutHost.RunLimit, ct);
            Assert.True(run.Stopped is null && run.ExitCode == 0, $"{run.Stopped} exit code {run.ExitCode}\n{run.StdOut}\n{run.StdErr}");
            return run.StdOut.Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        finally
        {
            GoldenRunner.TryDelete(work);
        }
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
    [Trait("Req", "UIF-953")]
    public void MetadataSchema_IconRules()
    {
        var ok = Path.Combine(temp, "ok.svg");
        File.WriteAllText(ok, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\">\n  <rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" fill=\"currentColor\"/>\n</svg>\n");
        var bad = Path.Combine(temp, "bad.svg");
        File.WriteAllText(bad, "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:x=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 16 16\" stroke=\"#ff0000\">\n"
            + "  <text>A</text>\n  <image x:href=\"data:image/png;base64,AA==\"/>\n  <path style=\"fill:red\" d=\"M0 0H1\"/>\n</svg>\n");
        var problems = new List<string>();

        MetadataSchema.CheckIcon("ok", ok, problems);
        Assert.Empty(problems);
        MetadataSchema.CheckIcon("bad", bad, problems);
        MetadataSchema.CheckIcon("gone", Path.Combine(temp, "gone.svg"), problems);

        Assert.Contains("bad: icon root must be <svg viewBox=\"0 0 24 24\">", problems);
        Assert.Contains("bad: icon stroke=\"#ff0000\": only currentColor or none", problems);
        Assert.Contains("bad: icon element <text> is not allowed", problems);
        Assert.Contains("bad: icon element <image> is not allowed", problems);
        Assert.Contains("bad: icon attribute href references other content", problems);
        Assert.Contains("bad: icon style attributes are not allowed (use fill/stroke)", problems);
        Assert.Contains("gone: icon file gone.svg does not exist", problems);
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
    [Trait("Category", "Integration")]
    [Trait("Req", "UIF-955")]
    public void MetadataMatch_ReportsItemClassMismatch()
    {
        foreach (var f in Directory.EnumerateFiles(MetadataTests.DesignDirectory, "*.json"))
        {
            File.Copy(f, Path.Combine(temp, Path.GetFileName(f)));
        }

        var file = Path.Combine(temp, "ComCtrls.components.json");
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"itemClass\": \"TStatusPanel\"", "\"itemClass\": \"TListColumn\"", StringComparison.Ordinal));

        var problems = MetadataMatch.Compare(MetadataRoot.Load(temp), MetadataTests.Source.Value, "ComCtrls");

        Assert.Equal(["TStatusBar.Panels: itemClass mismatch: metadata TListColumn, source TStatusPanel"], problems);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "UIF-957")]
    [Trait("Req", "UIF-960")]
    public void MetadataDecls_ReportSourceAndSharedDifferences()
    {
        foreach (var f in Directory.EnumerateFiles(MetadataTests.DesignDirectory, "*.json"))
        {
            File.Copy(f, Path.Combine(temp, Path.GetFileName(f)));
        }

        var grids = Path.Combine(temp, "Grids.components.json");
        File.WriteAllText(grids, File.ReadAllText(grids)
            .Replace("\"procedure(Sender: TObject; var Key: Char)\"", "\"procedure(Sender: TObject; Key: Char)\"", StringComparison.Ordinal)
            .Replace("{ \"name\": \"Pitch\", \"type\": \"TFontPitch\", \"default\": \"fpDefault\" },\n      { \"name\": \"Quality\", \"type\": \"TFontQuality\", \"default\": \"fqDefault\" },",
                "{ \"name\": \"Quality\", \"type\": \"TFontQuality\", \"default\": \"fqDefault\" },\n      { \"name\": \"Pitch\", \"type\": \"TFontPitch\", \"default\": \"fpDefault\" },", StringComparison.Ordinal));
        var controls = Path.Combine(temp, "Controls.components.json");
        File.WriteAllText(controls, File.ReadAllText(controls)
            .Replace("\"alRight\", \"alClient\"", "\"alClient\", \"alRight\"", StringComparison.Ordinal)
            .Replace("\"TAnchors\": { \"kind\": \"set\", \"of\": \"TAnchorKind\" }", "\"TAnchors\": { \"kind\": \"set\", \"of\": \"TAlign\" }", StringComparison.Ordinal));
        var root = MetadataRoot.Load(temp);

        var source = MetadataDecls.SignaturesMatchSource(root, MetadataTests.Source.Value);
        var shared = MetadataDecls.SharedDeclsAgree(root);

        Assert.Contains("Grids.components.json: eventSignatures.TKeyPressEvent: metadata \"procedure(Sender: TObject; Key: Char)\", source \"procedure(Sender: TObject; var Key: Char)\"", source);
        Assert.Contains("Grids.components.json: types.TFont.Pitch: missing or out of order (metadata position 6 is Quality)", source);
        Assert.Contains(source, p => p.StartsWith("Controls.components.json: types.TAlign: values [alNone, alTop, alBottom, alLeft, alClient, alRight]", StringComparison.Ordinal));
        Assert.Contains("Controls.components.json: types.TAnchors: of TAlign, source TAnchorKind", source);
        Assert.Contains(shared, p => p.StartsWith("eventSignatures.TKeyPressEvent: differs between files", StringComparison.Ordinal));
        Assert.Contains(shared, p => p.StartsWith("types.TFont: differs between files", StringComparison.Ordinal));
        Assert.Equal(2, shared.Count);
    }

    [Fact]
    [Trait("Req", "UIF-956")]
    public void MetadataDefaults_GeneratesChecksAndComparesValues()
    {
        var (program, checks) = MetadataDefaults.Generate(MetadataRoot.Load(MetadataTests.DesignDirectory));

        Assert.Contains("    C := TButton.Create(nil);\n", program, StringComparison.Ordinal);
        Assert.DoesNotContain("TCustomEdit.Create", program, StringComparison.Ordinal);
        Assert.Contains(new DefaultCheck("TButton.Width", "75", "Integer"), checks);
        Assert.Contains(new DefaultCheck("TButton.Font.Height", "-12", "Integer"), checks);
        Assert.Contains(new DefaultCheck("TPanel.Padding.Left", "0", "Integer"), checks);
        Assert.Contains(new DefaultCheck("TStatusBar.Panels[0].Width", "50", "Integer"), checks);
        Assert.Contains(new DefaultCheck("TButton.Anchors", "[akLeft,akTop]", "TAnchors"), checks);
        Assert.DoesNotContain(checks, c => c.Path == "TForm.Width");

        List<DefaultCheck> sample = [new("A.B", "True", "Boolean"), new("A.S", "[x,y]", "TS"), new("A.C", "clBtnFace", "TColor"), new("A.N", "3", "Integer"), new("A.M", "1", "Integer")];
        var problems = MetadataDefaults.Compare(sample, "A.B=TRUE\nA.S=y,x\nA.C=CLBTNFACE\nA.N=4\nB: EOutOfResources: boom\n");

        Assert.Equal(["B: EOutOfResources: boom", "A.N: metadata 3, runtime 4", "A.M: metadata 1, runtime (not printed)"], problems);
    }

    [Fact]
    [Trait("Req", "UIF-955")]
    [Trait("Req", "UIF-951")]
    public void MetadataSpecChecks_ReadTablesAndReportDifferences()
    {
        var (entries, forClass) = MetadataSpecChecks.ReadCategories(MetadataTests.MetadataAuthoringPath);
        Assert.Contains(new CategoryEntry("Columns", "Behavior", "TListBox"), entries);
        Assert.Contains(new CategoryEntry("Columns", "Data", "TListView"), entries);
        Assert.Contains(new CategoryEntry("Default", "Appearance", "TMenuItem"), entries);
        Assert.Contains(("Position", "Layout", "TForm"), forClass);
        Assert.Contains(("Default", "Behavior", "TButton"), forClass);
        var rows = MetadataSpecChecks.ReadSummary(MetadataTests.ControlsStandardPath);
        Assert.Contains(new SummaryRow("TPageControl", "ComCtrls", "container", "289 × 193", "PageControl", "Win32", "10", "pagecontrol", "OnChange"), rows);
        Assert.True(rows.Count >= 37, $"{rows.Count} rows");

        foreach (var f in Directory.EnumerateFiles(MetadataTests.DesignDirectory, "*.json"))
        {
            File.Copy(f, Path.Combine(temp, Path.GetFileName(f)));
        }

        var com = Path.Combine(temp, "ComCtrls.components.json");
        File.WriteAllText(com, File.ReadAllText(com)
            .Replace("{ \"name\": \"Position\", \"type\": \"Integer\", \"default\": 0, \"category\": \"Data\" },\n        { \"name\": \"Smooth\"",
                "{ \"name\": \"Position\", \"type\": \"Integer\", \"default\": 0, \"category\": \"Layout\" },\n        { \"name\": \"Smooth\"", StringComparison.Ordinal)
            .Replace("\"defaultSize\": { \"width\": 150, \"height\": 45 }", "\"defaultSize\": { \"width\": 150, \"height\": 44 }", StringComparison.Ordinal)
            .Replace("\"renderer\": \"treeview\"", "\"renderer\": \"tree\"", StringComparison.Ordinal));
        var root = MetadataRoot.Load(temp);

        Assert.Equal(["TProgressBar.Position: category Layout, expected Data (metadata-authoring.md §2.3)"],
            MetadataSpecChecks.CategoriesFollowSpec(root, MetadataTests.MetadataAuthoringPath));
        Assert.Equal(["§29 TTrackBar: Default W × H: spec 150 × 45, metadata 150 × 44", "§29 TTreeView: renderer: spec treeview, metadata tree"],
            MetadataSpecChecks.MatchesControlsSpec(root, MetadataTests.ControlsStandardPath));
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
