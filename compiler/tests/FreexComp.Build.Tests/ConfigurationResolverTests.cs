using System.Collections.Immutable;

namespace FreexComp.Build.Tests;

/// <summary>configurations.md §3, §4 (CMP-1153, CMP-1158, CMP-1160; task C-298): the effective values of a configuration.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class ConfigurationResolverTests
{
    internal const string ProjectPath = @"C:\p\Hello\Hello.pproj";

    /// <summary>The example of CMP-1150 with project <c>defines</c> <c>["APP"]</c>, <c>searchPaths</c> <c>["lib"]</c> and <c>Release</c> <c>searchPaths</c> <c>["rel"]</c>.</summary>
    internal const string Example = """
        {
          "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
          "searchPaths": ["lib"],
          "defines": ["APP"],
          "configurations": {
            "Debug":        { "optimization": 0, "debugInfo": true,  "defines": ["DEBUG"],   "outputDir": "bin/Debug" },
            "Release":      { "optimization": 2, "debugInfo": false, "defines": ["RELEASE"], "searchPaths": ["rel"], "outputDir": "bin/Release" },
            "Release-Demo": { "inherits": "Release", "defines": ["DEMO"] },
            "Profiling":    { "inherits": "Release-Demo", "debugInfo": true, "defines": ["!", "PROFILE"] }
          }
        }
        """;

    internal static Project Load(string text)
    {
        var result = ProjectSerializer.ParseProject(ProjectPath, text);
        Assert.Empty(result.Diagnostics);
        return result.Value!;
    }

    private static Project WithConfigurations(string configurations, string projectLevel = "") => Load(
        $$"""{ "formatVersion": 1, "name": "A", "mainSource": "A.pas"{{projectLevel}}, "configurations": { {{configurations}} } }""");

    private static string Golden(string name) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "compiler", "tests", "FreexComp.Build.Tests", "Golden", name)).ReplaceLineEndings("\n");

    [Theory]
    [Trait("Req", "CMP-1158")]
    [Trait("Req", "CMP-1160")]
    [InlineData("Profiling")]
    [InlineData("Release-Demo")]
    public void ThreeLevelChain_MatchesGoldenJson(string configuration)
    {
        var p = Load(Example);

        var json = ConfigurationResolver.ToJson(ConfigurationResolver.Resolve(p, configuration));

        Assert.Equal(Golden($"config.{configuration}.json"), json);
        Assert.DoesNotContain('\r', json);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string, int, bool, string[], string[]> MergeRows => new()
    {
        // a root configuration: the project level, then its own entries
        { "root", "\"R\": { \"optimization\": 1, \"debugInfo\": true, \"defines\": [\"R1\"], \"searchPaths\": [\"r\"] }", "R", 1, true, ["P", "R1"], ["p", "r"] },
        // absent scalars: the parent's value; own scalars replace it
        { "scalars inherited", "\"R\": { \"optimization\": 2, \"debugInfo\": true }, \"D\": { \"inherits\": \"R\" }", "D", 2, true, ["P"], ["p"] },
        { "optimization own", "\"R\": { \"optimization\": 2, \"debugInfo\": true }, \"D\": { \"inherits\": \"R\", \"optimization\": 0 }", "D", 0, true, ["P"], ["p"] },
        { "debugInfo own", "\"R\": { \"optimization\": 2, \"debugInfo\": true }, \"D\": { \"inherits\": \"R\", \"debugInfo\": false }", "D", 2, false, ["P"], ["p"] },
        // the value of the nearest member that has one
        {
            "three levels", "\"R\": { \"optimization\": 2 }, \"M\": { \"inherits\": \"R\", \"optimization\": 1, \"debugInfo\": true }, \"D\": { \"inherits\": \"M\" }",
            "D", 1, true, ["P"], ["p"]
        },
        // lists append in chain order, without de-duplication
        {
            "lists append", "\"R\": { \"defines\": [\"A\"], \"searchPaths\": [\"a\"] }, \"D\": { \"inherits\": \"R\", \"defines\": [\"B\", \"A\", \"P\"], \"searchPaths\": [\"b\"] }",
            "D", 0, false, ["P", "A", "B", "A", "P"], ["p", "a", "b"]
        },
        // the reset marker discards the project level and all ancestors — for that list only
        {
            "reset in derived", "\"R\": { \"defines\": [\"A\"], \"searchPaths\": [\"a\"] }, \"D\": { \"inherits\": \"R\", \"defines\": [\"!\", \"B\"], \"searchPaths\": [\"b\"] }",
            "D", 0, false, ["B"], ["p", "a", "b"]
        },
        {
            "reset of search paths", "\"R\": { \"defines\": [\"A\"], \"searchPaths\": [\"a\"] }, \"D\": { \"inherits\": \"R\", \"searchPaths\": [\"!\"] }",
            "D", 0, false, ["P", "A"], []
        },
        // … and a later member appends to what the reset left
        {
            "reset in the middle", "\"R\": { \"defines\": [\"A\"] }, \"M\": { \"inherits\": \"R\", \"defines\": [\"!\", \"B\"] }, \"D\": { \"inherits\": \"M\", \"defines\": [\"C\"] }",
            "D", 0, false, ["B", "C"], ["p"]
        },
        // in a root configuration it discards the project level
        { "reset in root", "\"R\": { \"defines\": [\"!\", \"A\"], \"searchPaths\": [\"!\", \"a\"] }", "R", 0, false, ["A"], ["a"] },
        { "reset alone in root", "\"R\": { \"defines\": [\"!\"] }", "R", 0, false, [], ["p"] },
    };

    [Theory]
    [MemberData(nameof(MergeRows))]
    [Trait("Req", "CMP-1153")]
    [Trait("Req", "CMP-1160")]
    public void ScalarsOverride_ListsAppend_ResetClears(
        string row, string configurations, string name, int optimization, bool debugInfo, string[] defines, string[] searchPaths)
    {
        var p = WithConfigurations(configurations, ", \"defines\": [\"P\"], \"searchPaths\": [\"p\"]");

        var e = ConfigurationResolver.Resolve(p, name);

        Assert.True(optimization == e.Optimization, row);
        Assert.True(debugInfo == e.DebugInfo, row);
        Assert.Equal(defines, e.Defines);
        Assert.Equal(searchPaths, e.SearchPaths);
        Assert.Equal(name, e.Name);
        Assert.Equal(name, e.Chain[^1]);
        Assert.DoesNotContain("!", e.Defines);
        Assert.DoesNotContain("!", e.SearchPaths);
    }

    [Fact]
    [Trait("Req", "CMP-1158")]
    public void Resolve_Chain_RootFirst_AndErrors()
    {
        var p = Load(Example);

        Assert.Equal(["Debug"], ConfigurationResolver.Resolve(p, "Debug").Chain);
        Assert.Equal(["Release", "Release-Demo"], ConfigurationResolver.Resolve(p, "Release-Demo").Chain);
        Assert.Equal(["Release", "Release-Demo", "Profiling"], ConfigurationResolver.Resolve(p, "Profiling").Chain);

        // the name is matched exactly; a model with a missing parent or a cycle throws (the loader never returns one)
        Assert.Throws<ArgumentException>(() => ConfigurationResolver.Resolve(p, "release"));
        Assert.Throws<ArgumentException>(() => ConfigurationResolver.Resolve(p, "Missing"));
        var orphan = p with { Configurations = p.Configurations.Remove("Release") };
        Assert.Contains("unknown configuration 'Release'", Assert.Throws<ArgumentException>(() => ConfigurationResolver.Resolve(orphan, "Profiling")).Message, StringComparison.Ordinal);
        var cycle = p with { Configurations = p.Configurations.SetItem("Release", p.Configurations["Release"] with { Inherits = "Profiling" }) };
        Assert.Contains("Circular configuration inheritance", Assert.Throws<ArgumentException>(() => ConfigurationResolver.Resolve(cycle, "Profiling")).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ConfigurationResolver.Resolve(cycle, "Release-Demo"));
        Assert.Equal(["Debug"], ConfigurationResolver.Resolve(cycle, "Debug").Chain);
    }

    [Fact]
    [Trait("Req", "CMP-1160")]
    public void OutputDirIsNeverInherited()
    {
        var p = WithConfigurations("""
            "Release": { "optimization": 2, "outputDir": "out/release" },
            "Demo": { "inherits": "Release" },
            "Own": { "inherits": "Release", "outputDir": "D:/builds/own" },
            "Deep": { "inherits": "Own" }
            """);

        Assert.Equal("out/release", ConfigurationResolver.Resolve(p, "Release").OutputDir);
        Assert.Equal("bin/Demo", ConfigurationResolver.Resolve(p, "Demo").OutputDir);
        Assert.Equal("D:/builds/own", ConfigurationResolver.Resolve(p, "Own").OutputDir);
        Assert.Equal("bin/Deep", ConfigurationResolver.Resolve(p, "Deep").OutputDir);

        // no two configurations share an output folder by accident
        var outputs = p.ConfigurationNames.Select(n => ProjectOptions.GetOutputPath(p, n)).ToList();
        Assert.Equal(outputs.Count, outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(@"C:\p\Hello\bin\Demo\A.exe", ProjectOptions.GetOutputPath(p, "Demo"));
    }

    [Fact]
    [Trait("Req", "CMP-1160")]
    public void RttiOrder_ChainThenProjectThenBroad()
    {
        const string Configurations = """
            "Root": {},
            "Child": { "inherits": "Root" },
            "Published": { "inherits": "Child", "rtti": "published" },
            "Below": { "inherits": "Published" },
            "Broad": { "inherits": "Below", "rtti": "broad" },
            "Last": { "inherits": "Broad" }
            """;
        string Rtti(Project p, string name) => ConfigurationResolver.Resolve(p, name).Rtti;

        // nothing set anywhere: broad
        var plain = WithConfigurations(Configurations);
        Assert.Equal("broad", Rtti(plain, "Root"));
        Assert.Equal("broad", Rtti(plain, "Child"));

        // the project value where no member of the chain sets one
        var project = WithConfigurations(Configurations, ", \"rtti\": \"published\"");
        Assert.Equal("published", Rtti(project, "Root"));
        Assert.Equal("published", Rtti(project, "Child"));

        // the last member of the chain that sets it wins over the project and over earlier members
        foreach (var p in new[] { plain, project })
        {
            Assert.Equal("published", Rtti(p, "Published"));
            Assert.Equal("published", Rtti(p, "Below"));
            Assert.Equal("broad", Rtti(p, "Broad"));
            Assert.Equal("broad", Rtti(p, "Last"));
        }

        var rootSet = WithConfigurations("\"Root\": { \"rtti\": \"published\" }, \"Child\": { \"inherits\": \"Root\" }", ", \"rtti\": \"broad\"");
        Assert.Equal("published", Rtti(rootSet, "Child"));
    }
}
